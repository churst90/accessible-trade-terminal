using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Plugins;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public interface IDataOrchestrator
    {
        Task<List<Ohlcv>> FetchOhlcvAsync(string market, string provider, string symbol, string timeframe, long? since = null, int? limit = null, long? until = null, bool silent = false);
        System.Threading.Channels.ChannelReader<Ohlcv> LiveStream { get; }
        
        /// <summary>
        /// Fast-path notification for price ticks. Bypasses Rx overhead for maximum performance 
        /// in high-frequency sonification and indicator update loops.
        /// </summary>
        event Action<Ohlcv>? OnTickReceived;

        DataState CurrentState { get; }
        IObservable<DataState> StateChanged { get; }

        Task StartLiveStreamAsync(string market, string providerName, string symbol, string timeframe);
        Task StopLiveStreamAsync();
    }

    /// <summary>
    /// Facade that orchestrates data requests between the HistoricalDataFetcher and LiveStreamManager.
    /// Implements robust retry policies and a circuit breaker for network resilience.
    /// 
    /// THREAD SAFETY:
    /// All public async methods are thread-safe and can be called concurrently. They do not block the calling thread.
    /// The `LiveStream` observable pushes items on a background thread.
    /// </summary>
    public class DataOrchestrator : IDataOrchestrator, IDisposable
    {
        private readonly HistoricalDataFetcher _historicalFetcher;
        private readonly LiveStreamManager _liveStreamManager;
        private readonly IEventBus _eventBus;
        private readonly ILogger<DataOrchestrator> _logger;
        
        private readonly IAsyncPolicy _resiliencePolicy;
        private readonly AsyncCircuitBreakerPolicy _circuitBreaker;
        private readonly DataStateMachine _stateMachine;
        private readonly System.Reactive.Disposables.CompositeDisposable _subscriptions = new();
        private readonly System.Threading.Channels.Channel<Ohlcv> _liveStreamChannel = System.Threading.Channels.Channel.CreateUnbounded<Ohlcv>();

        public System.Threading.Channels.ChannelReader<Ohlcv> LiveStream => _liveStreamChannel.Reader;
        public DataState CurrentState => _stateMachine.CurrentState;
        public IObservable<DataState> StateChanged => _stateMachine.StateChanged;

        public event Action<Ohlcv>? OnTickReceived;

        public DataOrchestrator(HistoricalDataFetcher historicalFetcher, LiveStreamManager liveStreamManager, IEventBus eventBus, ILogger<DataOrchestrator> logger)
        {
            _historicalFetcher = historicalFetcher;
            _liveStreamManager = liveStreamManager;
            _eventBus = eventBus;
            _logger = logger;
            _stateMachine = new DataStateMachine(logger, eventBus);

            // 1. Define the Circuit Breaker: 10 consecutive failures trips the circuit for 5 seconds
            // Use explicit non-generic handle to ensure we get a PolicyBuilder (not PolicyBuilder<T>)
            _circuitBreaker = Polly.Policy
                .Handle<HttpRequestException>()
                .Or<System.Net.Sockets.SocketException>()
                .Or<System.IO.IOException>()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 10,
                    durationOfBreak: TimeSpan.FromSeconds(5),
                    onBreak: (ex, breakDelay) => 
                    {
                        _logger.LogCritical(ex, "CIRCUIT BROKEN: Suspending network requests for {BreakDelaySeconds}s.", breakDelay.TotalSeconds);
                        _eventBus.Publish(new ConnectionStatusEvent("GLOBAL_NETWORK", ConnectionState.Error, "Network connection lost. Circuit tripped."));
                        _stateMachine.Fire(DataTrigger.ErrorOccurred);
                    },
                    onReset: () => 
                    {
                        _logger.LogInformation("CIRCUIT RESET: Network connection restored.");
                        _eventBus.Publish(new ConnectionStatusEvent("GLOBAL_NETWORK", ConnectionState.Connected, "Network connection restored."));
                        _stateMachine.Fire(DataTrigger.Reset);
                    },
                    onHalfOpen: () => 
                    {
                        _logger.LogInformation("CIRCUIT HALF-OPEN: Testing connection...");
                    });

            // 2. Define a simple Pass-Through or Fallback Policy
            var retryPolicy = Polly.Policy
                .Handle<HttpRequestException>()
                .WaitAndRetryAsync(
                    retryCount: 1, 
                    sleepDurationProvider: _ => TimeSpan.FromSeconds(1));

            // 3. Wrap them together
            _resiliencePolicy = Polly.Policy.WrapAsync(retryPolicy, _circuitBreaker);

            SafeFireAndForget.Run(ProcessLiveStreamAsync, logger, "ProcessLiveStream");
        }

        private async Task ProcessLiveStreamAsync()
        {
            try
            {
                await foreach (var tick in _liveStreamManager.LiveStream.ReadAllAsync())
                {
                    _stateMachine.Fire(DataTrigger.TickReceived);
                    OnTickReceived?.Invoke(tick);
                    _liveStreamChannel.Writer.TryWrite(tick);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing live stream.");
            }
        }

        public async Task<List<Ohlcv>> FetchOhlcvAsync(string market, string provider, string symbol, string timeframe, long? since = null, int? limit = null, long? until = null, bool silent = false)
        {
            try
            {
                if (!silent) _stateMachine.Fire(DataTrigger.FetchHistoricalStarted);
                
                // Execute through the wrapped resilience shield
                var results = await _resiliencePolicy.ExecuteAsync(() =>
                    _historicalFetcher.FetchOhlcvAsync(market, provider, symbol, timeframe, since, limit, until)).ConfigureAwait(false);
                
                if (!silent)
                {
                    _stateMachine.Fire(DataTrigger.HistoricalDataReceived);
                    
                    // If we got data, we are now technically ready to start the gap fill/live transition
                    if (results.Any())
                    {
                        _stateMachine.Fire(DataTrigger.GapFillStarted);
                    }
                }

                return results;
            }
            catch (BrokenCircuitException)
            {
                // Instant fail if circuit is open - prevents UI hanging on timeouts
                _logger.LogWarning("Fetch aborted: Circuit is OPEN for {Symbol}.", symbol);
                if (!silent) _stateMachine.Fire(DataTrigger.ErrorOccurred);
                return new List<Ohlcv>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch data for {Symbol} after multiple attempts.", symbol);
                if (!silent)
                {
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error, $"Failed to load data for {symbol} from {provider}. Please check connection."));
                    _stateMachine.Fire(DataTrigger.ErrorOccurred);
                }
                return new List<Ohlcv>();
            }
        }

        public async Task StartLiveStreamAsync(string market, string providerName, string symbol, string timeframe)
        {
            _stateMachine.Fire(DataTrigger.GapFillStarted);
            // Gap fill is deferred to the DataManager pipeline.
            
            await _liveStreamManager.StartLiveStreamAsync(market, providerName, symbol, timeframe).ConfigureAwait(false);
            _stateMachine.Fire(DataTrigger.LiveStreamStarted);
        }

        public Task StopLiveStreamAsync()
        {
            _stateMachine.Fire(DataTrigger.Reset);
            return _liveStreamManager.StopLiveStreamAsync();
        }

        public void Dispose()
        {
            _subscriptions.Dispose();
            _stateMachine.Dispose();
            _liveStreamManager.Dispose();
        }

        private class DataStateMachine : AccessibleTrader.Sdk.Services.StateMachine<DataState, DataTrigger>
        {
            private readonly ILogger _logger;
            private readonly IEventBus _eventBus;

            public DataStateMachine(ILogger logger, IEventBus eventBus) : base(DataState.Initializing)
            {
                _logger = logger;
                _eventBus = eventBus;
            }

            protected override DataState Transition(DataState currentState, DataTrigger trigger)
            {
                return (currentState, trigger) switch
                {
                    (DataState.Initializing, DataTrigger.FetchHistoricalStarted) => DataState.HistoricalFilling,
                    (DataState.HistoricalFilling, DataTrigger.HistoricalDataReceived) => DataState.GapFilling,
                    
                    // Permissive Gap/Live transitions: handle out-of-order triggers
                    (DataState.HistoricalFilling, DataTrigger.GapFillStarted) => DataState.GapFilling,
                    (DataState.HistoricalFilling, DataTrigger.LiveStreamStarted) => DataState.LiveStreaming,
                    (DataState.GapFilling, DataTrigger.GapFillStarted) => DataState.GapFilling, 
                    (DataState.GapFilling, DataTrigger.LiveStreamStarted) => DataState.LiveStreaming,
                    
                    (DataState.LiveStreaming, DataTrigger.TickReceived) => DataState.LiveStreaming,
                    (DataState.LiveStreaming, DataTrigger.NetworkLagged) => DataState.Stalled,
                    (DataState.Stalled, DataTrigger.TickReceived) => DataState.LiveStreaming,
                    
                    // Global error/reset transitions
                    (_, DataTrigger.ErrorOccurred) => DataState.Faulted,
                    (_, DataTrigger.Reset) => DataState.Initializing,
                    
                    _ => currentState
                };
            }

            protected override void OnTransitioned(DataState newState)
            {
                _logger.LogInformation("DataOrchestrator state changed to: {NewState}.", newState);

                // SILENT LIVE UPDATES: Only announce MAJOR transitions that the user is waiting for.
                // We avoid announcing every tick-driven state change to keep the earcon noise low.
                bool isMajorTransition = (PreviousState == DataState.Initializing || PreviousState == DataState.Faulted);
                
                if (isMajorTransition || newState == DataState.Faulted)
                {
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange, $"Data link: {newState}"));
                }
            }
        }
    }
}