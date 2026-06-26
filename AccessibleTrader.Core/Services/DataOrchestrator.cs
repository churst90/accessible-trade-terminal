using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Services;
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
        private readonly DemoPolicy _demo;

        // Resilience policies scoped per provider so one flaky source doesn't
        // suspend traffic for the other 25. Built lazily on first use; stored
        // in a concurrent dictionary keyed by provider id (case-insensitive).
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (IAsyncPolicy Policy, AsyncCircuitBreakerPolicy Breaker)> _providerPolicies
            = new(StringComparer.OrdinalIgnoreCase);

        private readonly DataStateMachine _stateMachine;
        private readonly System.Reactive.Disposables.CompositeDisposable _subscriptions = new();
        private readonly System.Threading.Channels.Channel<Ohlcv> _liveStreamChannel = System.Threading.Channels.Channel.CreateUnbounded<Ohlcv>();

        public System.Threading.Channels.ChannelReader<Ohlcv> LiveStream => _liveStreamChannel.Reader;
        public DataState CurrentState => _stateMachine.CurrentState;
        public IObservable<DataState> StateChanged => _stateMachine.StateChanged;

        public event Action<Ohlcv>? OnTickReceived;

        public DataOrchestrator(HistoricalDataFetcher historicalFetcher, LiveStreamManager liveStreamManager, IEventBus eventBus, ILogger<DataOrchestrator> logger, DemoPolicy demo)
        {
            _historicalFetcher = historicalFetcher;
            _liveStreamManager = liveStreamManager;
            _eventBus = eventBus;
            _logger = logger;
            _demo = demo;
            _stateMachine = new DataStateMachine(logger, eventBus);

            SafeFireAndForget.Run(ProcessLiveStreamAsync, logger, "ProcessLiveStream");
        }

        /// <summary>
        /// Returns the retry+circuit-breaker policy pair for a given provider, building
        /// it on first use. Scoping the breaker per provider means one dead source
        /// (e.g. Polygon throwing timeouts) no longer blocks every other provider for
        /// five seconds. 10 consecutive failures trip the breaker; 5 seconds open;
        /// single retry before the first failure count increments.
        /// </summary>
        private (IAsyncPolicy Policy, AsyncCircuitBreakerPolicy Breaker) GetResiliencePolicy(string providerId)
        {
            return _providerPolicies.GetOrAdd(providerId, pid =>
            {
                var breaker = Polly.Policy
                    .Handle<HttpRequestException>()
                    .Or<System.Net.Sockets.SocketException>()
                    .Or<System.IO.IOException>()
                    .CircuitBreakerAsync(
                        exceptionsAllowedBeforeBreaking: 10,
                        durationOfBreak: TimeSpan.FromSeconds(5),
                        onBreak: (ex, breakDelay) =>
                        {
                            _logger.LogCritical(ex, "CIRCUIT BROKEN [{Provider}]: Suspending requests for {BreakDelaySeconds}s.", pid, breakDelay.TotalSeconds);
                            _eventBus.Publish(new ConnectionStatusEvent(pid, ConnectionState.Error, $"{pid} network issue. Circuit tripped."));
                            _stateMachine.Fire(DataTrigger.ErrorOccurred);
                        },
                        onReset: () =>
                        {
                            _logger.LogInformation("CIRCUIT RESET [{Provider}]: Connection restored.", pid);
                            _eventBus.Publish(new ConnectionStatusEvent(pid, ConnectionState.Connected, $"{pid} connection restored."));
                            _stateMachine.Fire(DataTrigger.Reset);
                        },
                        onHalfOpen: () =>
                        {
                            _logger.LogInformation("CIRCUIT HALF-OPEN [{Provider}]: Testing connection...", pid);
                        });

                var retryPolicy = Polly.Policy
                    .Handle<HttpRequestException>()
                    .WaitAndRetryAsync(
                        retryCount: 1,
                        sleepDurationProvider: _ => TimeSpan.FromSeconds(1));

                return (Polly.Policy.WrapAsync(retryPolicy, breaker), breaker);
            });
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
            // Validate at the choke point so every provider inherits the same shape check
            // without each plugin having to reimplement it. Rejects path/query injection
            // before any URL is built or any signed request is constructed.
            if (!SymbolValidator.IsValid(symbol))
            {
                _logger.LogWarning("Rejected invalid symbol '{Symbol}' for provider {Provider}.", symbol, provider);
                if (!silent)
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error, $"Invalid symbol '{symbol}' for {provider}."));
                return new List<Ohlcv>();
            }

            try
            {
                if (!silent) _stateMachine.Fire(DataTrigger.FetchHistoricalStarted);

                // Execute through the per-provider resilience shield so a tripped
                // breaker only affects this one provider.
                var (policy, _) = GetResiliencePolicy(provider);
                var results = await policy.ExecuteAsync(() =>
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
            if (!SymbolValidator.IsValid(symbol))
            {
                _logger.LogWarning("Rejected invalid symbol '{Symbol}' for live stream on {Provider}.", symbol, providerName);
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error, $"Invalid symbol '{symbol}' for {providerName}."));
                return;
            }

            _stateMachine.Fire(DataTrigger.GapFillStarted);
            // Gap fill is deferred to the DataManager pipeline.

            // Demo: some providers have no live feed (Twelve Data's free tier has no
            // WebSocket), where a live subscription only loops on reconnects. Skip the
            // live stream for those and serve the chart from history alone.
            if (_demo.AllowsLiveStream(providerName))
            {
                await _liveStreamManager.StartLiveStreamAsync(market, providerName, symbol, timeframe).ConfigureAwait(false);
            }
            else
            {
                _logger.LogInformation("Live stream skipped for {Provider} (demo: historical-only).", providerName);
            }
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