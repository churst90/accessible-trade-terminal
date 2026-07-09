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
    public class LiveStreamManager : IAsyncDisposable, IDisposable
    {
        private readonly IDataService _dataService;
        private readonly HistoricalDataFetcher _historicalFetcher;
        private readonly IGlobalErrorCoordinator _errorCoordinator;
        private readonly ILogger<LiveStreamManager> _logger;

        // Bounded with drop-oldest so a slow/stalled consumer can't grow memory
        // without limit on a fast feed. 1024 consolidated bars is far more than a
        // healthy UI ever leaves unread; when the consumer lags that badly, the
        // stale oldest bars are the right thing to shed (each newer write of the
        // same bucket supersedes them anyway).
        private readonly System.Threading.Channels.Channel<Ohlcv> _liveStreamChannel =
            System.Threading.Channels.Channel.CreateBounded<Ohlcv>(
                new System.Threading.Channels.BoundedChannelOptions(1024)
                {
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest,
                });
        public virtual System.Threading.Channels.ChannelReader<Ohlcv> LiveStream => _liveStreamChannel.Reader;

        private IDisposable? _currentProviderSubscription;
        private IDisposable? _currentErrorSubscription;
        private IDisposable? _currentConnStateSubscription;
        private ConnectionState _lastConnectionState = ConnectionState.Disconnected;
        private IMarketDataProvider? _currentLiveProvider;
        private Ohlcv? _currentBucketCandle;
        private string? _currentLiveTimeframe;
        private CancellationTokenSource? _fallbackCts;
        // Monotonic (Environment.TickCount64) rather than DateTime.Now: the silence
        // watchdog compares intervals, and wall-clock time is not monotonic — an NTP
        // step or VM time jump could stall the watchdog forever (clock back) or fire
        // it spuriously (clock forward). 0 == "long ago" before the first tick.
        private long _lastTickAtMs;
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
            // Idempotency guard: rapid re-entry (e.g. workspace restore firing two
            // subscribe calls, or a flaky auto-reconnect) would otherwise tear down
            // the live subscription and rebuild it, losing any ticks that arrive
            // between Dispose and the fresh Subscribe. If the caller asks for the
            // same (provider, market, symbol, timeframe) that's already running on
            // an attached provider, no-op.
            if (_currentLiveProvider != null
                && string.Equals(_currentProviderName, providerName, StringComparison.Ordinal)
                && string.Equals(_currentMarket, market, StringComparison.Ordinal)
                && string.Equals(_currentSymbol, symbol, StringComparison.Ordinal)
                && string.Equals(_currentLiveTimeframe, timeframe, StringComparison.Ordinal)
                && _currentProviderSubscription != null)
            {
                _logger.LogDebug(
                    "LiveStreamManager: StartLiveStreamAsync called for already-active subscription {Provider}/{Symbol}@{Timeframe}; no-op.",
                    providerName, symbol, timeframe);
                return;
            }

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
            _lastTickAtMs = Environment.TickCount64;

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
            _currentConnStateSubscription?.Dispose();

            // Track the socket's connection state so the watchdog can tell a *dropped*
            // connection (worth reconnecting) from one that is up but quiet (a sparse
            // feed, or a tier with no live stream — reconnecting that loops forever).
            _currentConnStateSubscription = provider.ConnectionStateStream.Subscribe(s => _lastConnectionState = s);

            _currentErrorSubscription = provider.ErrorStream.Subscribe(err =>
            {
                _errorCoordinator.ReportError(err, ErrorSeverity.Medium, ErrorCategory.Provider);
            });

            _currentProviderSubscription = provider.LiveStream.Subscribe(tick =>
            {
                _lastTickAtMs = Environment.TickCount64;
                _fallbackAnnounced = false;
                _reconnectAttempts = 0;

                DateTime periodStart = TimeframeUtility.GetPeriodStart(tick.Date, _currentLiveTimeframe!);
                lock (this)
                {
                    if (!_currentBucketCandle.HasValue || _currentBucketCandle.Value.Date != periodStart)
                        _currentBucketCandle = new Ohlcv(periodStart, tick.Open, tick.High, tick.Low, tick.Close, tick.Volume);
                    else
                        _currentBucketCandle = _currentBucketCandle.Value.UpdateWith(tick);

                    // Drop malformed ticks before they reach RecalculateLastAsync and corrupt
                    // indicator buffers. Require all four OHLC legs > 0 (a zero on any leg
                    // means the feed glitched — real market ticks satisfy Low <= Close <= High
                    // and Low > 0 for any tradable instrument) and Volume >= 0 (first tick of
                    // a new period legitimately has zero volume on thin books or pre-market).
                    var bar = _currentBucketCandle.Value;
                    if (bar.Open > 0 && bar.High > 0 && bar.Low > 0 && bar.Close > 0 && bar.Volume >= 0)
                        _liveStreamChannel.Writer.TryWrite(bar);
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

                    if (Environment.TickCount64 - _lastTickAtMs <= (long)SilenceThreshold.TotalMilliseconds)
                        continue;

                    // A socket that is *up but quiet* must NOT be storm-reconnected:
                    // reconnecting a healthy-but-silent connection (a sparse feed, or a
                    // tier with no live data such as Twelve Data's free plan) loops
                    // forever and can wedge the session. Only a dropped/errored
                    // connection benefits from a reconnect. If it is connected and quiet,
                    // leave it — a real tick will reset the clock if one ever arrives.
                    if (_lastConnectionState == ConnectionState.Connected)
                    {
                        if (!_fallbackAnnounced)
                        {
                            _logger.LogInformation(
                                "Live stream for {Provider} is connected but quiet ({Seconds}s); leaving it as historical/sparse rather than reconnecting.",
                                provider, SilenceThreshold.TotalSeconds);
                            _fallbackAnnounced = true;
                        }
                        continue;
                    }

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
                        return; // stop the watchdog — spinning on a dead feed wastes a thread
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
            _lastTickAtMs = Environment.TickCount64;

            SubscribeToProvider(provider, market, symbol, timeframe);
            await provider.SetSubscriptionAsync(market, symbol, timeframe).ConfigureAwait(false);

            _logger.LogInformation("Reconnected live stream for {Provider} {Symbol} @ {Timeframe}.", providerName, symbol, timeframe);
            _errorCoordinator.ReportError(
                $"{providerName} stream reconnected successfully.",
                ErrorSeverity.Low, ErrorCategory.Informational);
        }

        private void StopFallbackPolling()
        {
            // The watchdog uses _lastTickAtMs and _reconnectAttempts to self-throttle.
            // No explicit stop needed — successful ticks reset the counters.
        }

        public virtual async Task StopLiveStreamAsync()
        {
            _fallbackCts?.Cancel();
            _currentProviderSubscription?.Dispose();
            _currentErrorSubscription?.Dispose();
            _currentConnStateSubscription?.Dispose();
            _currentProviderSubscription = null;
            _currentErrorSubscription = null;
            _currentConnStateSubscription = null;
            _currentBucketCandle = null;
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            _fallbackCts?.Cancel();
            _currentProviderSubscription?.Dispose();
            _currentErrorSubscription?.Dispose();
            _currentConnStateSubscription?.Dispose();
            if (_currentLiveProvider != null)
            {
                try { await _currentLiveProvider.DisconnectAsync().ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogWarning(ex, "DisconnectAsync during DisposeAsync failed."); }
            }
            _liveStreamChannel.Writer.TryComplete();
        }

        public void Dispose()
        {
            // Sync fallback for callers that don't await-using. Offloaded to the
            // thread pool so we don't capture a SynchronizationContext and deadlock
            // on a provider whose DisconnectAsync resumes on the captured context.
            _fallbackCts?.Cancel();
            _currentProviderSubscription?.Dispose();
            _currentErrorSubscription?.Dispose();
            _currentConnStateSubscription?.Dispose();
            if (_currentLiveProvider != null)
            {
                try { Task.Run(() => _currentLiveProvider.DisconnectAsync()).GetAwaiter().GetResult(); }
                catch (Exception ex) { _logger.LogWarning(ex, "DisconnectAsync during Dispose failed."); }
            }
            _liveStreamChannel.Writer.TryComplete();
        }
    }
}
