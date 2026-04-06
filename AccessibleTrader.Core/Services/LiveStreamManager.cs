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
            _logger.LogInformation($"LiveStreamManager: Requesting live stream for {symbol} @ {timeframe}");
            
            var provider = await _dataService.GetProviderAsync(providerName);
            if (provider == null) return;

            await provider.EnsureConnectedAsync();
            
            _currentProviderSubscription?.Dispose();
            _currentErrorSubscription?.Dispose();
            _currentBucketCandle = null;
            _currentLiveTimeframe = timeframe;
            _currentLiveProvider = provider;
            _lastTickReceived = DateTime.Now;

            _currentErrorSubscription = provider.ErrorStream.Subscribe(err => 
            {
                _errorCoordinator.ReportError(err, ErrorSeverity.Medium, ErrorCategory.Provider);
            });

            _currentProviderSubscription = provider.LiveStream.Subscribe(tick =>
            {
                _lastTickReceived = DateTime.Now;
                _fallbackAnnounced = false;
                StopFallbackPolling();

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

            try
            {
                await provider.SetSubscriptionAsync(market, symbol, timeframe);
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

        private void StartFallbackWatchdog(string market, string provider, string symbol, string timeframe)
        {
            _fallbackCts?.Cancel();
            _fallbackCts = new CancellationTokenSource();
            var token = _fallbackCts.Token;

            _ = Task.Run(async () => 
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(15000, token);
                    
                    if (DateTime.Now - _lastTickReceived > TimeSpan.FromSeconds(60) && !_fallbackAnnounced)
                    {
                        _logger.LogWarning($"LiveStreamManager: Live stream for {provider} silent for 60s.");
                        _errorCoordinator.ReportError($"{provider} stream delayed. Attempting reconnect.", ErrorSeverity.Low, ErrorCategory.Informational);
                        _fallbackAnnounced = true;
                    }
                }
            }, token);
        }

        private void StopFallbackPolling()
        {
            // Just a placeholder to avoid unnecessary cancellation logic churn, 
            // the watchdog uses _lastTickReceived to self-throttle.
        }

        public virtual async Task StopLiveStreamAsync()
        {
            _fallbackCts?.Cancel();
            _currentProviderSubscription?.Dispose();
            _currentErrorSubscription?.Dispose();
            _currentProviderSubscription = null;
            _currentErrorSubscription = null;
            _currentBucketCandle = null;
            await Task.CompletedTask;
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
