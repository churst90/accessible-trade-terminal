using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Core.Services.Accessibility;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public class LiveStreamManager : IDisposable
    {
        private readonly IDataService _dataService;
        private readonly HistoricalDataFetcher _historicalFetcher;
        private readonly IGlobalErrorCoordinator _errorCoordinator;
        private readonly ILogger<LiveStreamManager> _logger;

        private readonly System.Threading.Channels.Channel<Ohlcv> _liveStreamChannel = System.Threading.Channels.Channel.CreateUnbounded<Ohlcv>();
        public virtual System.Threading.Channels.ChannelReader<Ohlcv> LiveStream => _liveStreamChannel.Reader;

        private IDisposable? _currentProviderSubscription;
        private IDisposable? _currentErrorSubscription;
        private IMarketDataProvider? _currentLiveProvider;
        private Ohlcv? _currentBucketCandle;
        private string? _currentLiveTimeframe;
        private CancellationTokenSource? _fallbackCts;
        private DateTime _lastTickReceived = DateTime.MinValue;
        private bool _fallbackAnnounced = false;

        // Reconnect state — tracks the current subscription parameters so the
        // watchdog can tear down and re-subscribe without caller intervention.
        private string? _currentMarket;
        private string? _currentProviderName;
        private string? _currentSymbol;
        private int _reconnectAttempts;
        private const int MaxReconnectAttempts = 5;
        private static readonly TimeSpan SilenceThreshold = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(15);

        public LiveStreamManager(
            IDataService dataService, 
            HistoricalDataFetcher historicalFetcher,
            IGlobalErrorCoordinator errorCoordinator,
            ILogger<LiveStreamManager> logger)
        {
            _dataService = dataService;
            _historicalFetcher = historicalFetcher;
            _errorCoordinator = errorCoordinator;
            _logger = logger;
        }

        public virtual async Task StartLiveStreamAsync(string market, string providerName, string symbol, string timeframe)
        {
            _logger.LogInformation("LiveStreamManager: Requesting live stream for {Symbol} @ {Timeframe}.", symbol, timeframe);

            var provider = await _dataService.GetProviderAsync(providerName).ConfigureAwait(false);
            if (provider == null) return;

            await provider.EnsureConnectedAsync().ConfigureAwait(false);

            _currentProviderSubscription?.Dispose();
            _currentErrorSubscription?.Dispose();
            _currentBucketCandle = null;
            _currentLiveTimeframe = timeframe;
            _currentLiveProvider = provider;
            _currentMarket = market;
            _currentProviderName = providerName;
            _currentSymbol = symbol;
            _reconnectAttempts = 0;
            _lastTickReceived = DateTime.Now;

            SubscribeToProvider(provider, market, symbol, timeframe);

            try
            {
                await provider.SetSubscriptionAsync(market, symbol, timeframe).ConfigureAwait(false);
                StartFallbackWatchdog(market, providerName, symbol, timeframe);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start live stream subscription.");
                _currentProviderSubscription?.Dispose();
                _currentProviderSubscription = null;
                throw;
            }
        }

        private void SubscribeToProvider(IMarketDataProvider provider, string market, string symbol, string timeframe)
        {
            _currentErrorSubscription?.Dispose();
            _currentProviderSubscription?.Dispose();

            _currentErrorSubscription = provider.ErrorStream.Subscribe(err =>
            {
                _errorCoordinator.ReportError(err, ErrorSeverity.Medium, ErrorCategory.Provider);
            });

            _currentProviderSubscription = provider.LiveStream.Subscribe(tick =>
            {
                _lastTickReceived = DateTime.Now;
                _fallbackAnnounced = false;
                _reconnectAttempts = 0;

                DateTime periodStart = TimeframeUtility.GetPeriodStart(tick.Date, _currentLiveTimeframe!);
                lock (this)
                {
                    if (!_currentBucketCandle.HasValue || _currentBucketCandle.Value.Date != periodStart)
                        _currentBucketCandle = new Ohlcv(periodStart, tick.Open, tick.High, tick.Low, tick.Close, tick.Volume);
                    else
                        _currentBucketCandle = _currentBucketCandle.Value.UpdateWith(tick);

                    // Only emit bars with a valid close price — drops malformed ticks before
                    // they reach RecalculateLastAsync and corrupt indicator buffers.
                    if (_currentBucketCandle.Value.Close > 0)
                        _liveStreamChannel.Writer.TryWrite(_currentBucketCandle.Value);
                }
            });
        }

        private void StartFallbackWatchdog(string market, string provider, string symbol, string timeframe)
        {
            _fallbackCts?.Cancel();
            _fallbackCts = new CancellationTokenSource();
            var token = _fallbackCts.Token;

            SafeFireAndForget.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(WatchdogInterval, token).ConfigureAwait(false);

                    if (DateTime.Now - _lastTickReceived <= SilenceThreshold)
                        continue;

                    if (_reconnectAttempts >= MaxReconnectAttempts)
                    {
                        if (!_fallbackAnnounced)
                        {
                            _logger.LogError("Live stream for {Provider} exceeded {Max} reconnect attempts. Giving up.", provider, MaxReconnectAttempts);
                            _errorCoordinator.ReportError(
                                $"{provider} stream lost after {MaxReconnectAttempts} reconnect attempts. Reload chart to retry.",
                                ErrorSeverity.High, ErrorCategory.Provider);
                            _fallbackAnnounced = true;
                        }
                        continue;
                    }

                    _reconnectAttempts++;
                    _logger.LogWarning(
                        "Live stream for {Provider} silent for {Seconds}s. Reconnect attempt {Attempt}/{Max}.",
                        provider, SilenceThreshold.TotalSeconds, _reconnectAttempts, MaxReconnectAttempts);

                    _errorCoordinator.ReportError(
                        $"{provider} stream delayed. Reconnecting ({_reconnectAttempts}/{MaxReconnectAttempts})...",
                        ErrorSeverity.Low, ErrorCategory.Informational);

                    try
                    {
                        await AttemptReconnectAsync(market, provider, symbol, timeframe).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Reconnect attempt {Attempt} for {Provider} failed.", _reconnectAttempts, provider);
                    }
                }
            }, _logger, "FallbackWatchdog");
        }

        private async Task AttemptReconnectAsync(string market, string providerName, string symbol, string timeframe)
        {
            var provider = await _dataService.GetProviderAsync(providerName).ConfigureAwait(false);
            if (provider == null) return;

            // Tear down old subscription before reconnecting.
            _currentProviderSubscription?.Dispose();
            _currentErrorSubscription?.Dispose();
            _currentBucketCandle = null;

            try
            {
                await provider.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Disconnect before reconnect threw (non-fatal).");
            }

            await provider.EnsureConnectedAsync().ConfigureAwait(false);

            _currentLiveProvider = provider;
            _lastTickReceived = DateTime.Now;

            SubscribeToProvider(provider, market, symbol, timeframe);
            await provider.SetSubscriptionAsync(market, symbol, timeframe).ConfigureAwait(false);

            _logger.LogInformation("Reconnected live stream for {Provider} {Symbol} @ {Timeframe}.", providerName, symbol, timeframe);
            _errorCoordinator.ReportError(
                $"{providerName} stream reconnected successfully.",
                ErrorSeverity.Low, ErrorCategory.Informational);
        }

        private void StopFallbackPolling()
        {
            // The watchdog uses _lastTickReceived and _reconnectAttempts to self-throttle.
            // No explicit stop needed — successful ticks reset the counters.
        }

        public virtual async Task StopLiveStreamAsync()
        {
            _fallbackCts?.Cancel();
            _currentProviderSubscription?.Dispose();
            _currentErrorSubscription?.Dispose();
            _currentProviderSubscription = null;
            _currentErrorSubscription = null;
            _currentBucketCandle = null;
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public void Dispose()
        {
            _fallbackCts?.Cancel();
            _currentProviderSubscription?.Dispose();
            _currentErrorSubscription?.Dispose();
            if (_currentLiveProvider != null) _currentLiveProvider.DisconnectAsync().GetAwaiter().GetResult();
            _liveStreamChannel.Writer.TryComplete();
        }
    }
}
