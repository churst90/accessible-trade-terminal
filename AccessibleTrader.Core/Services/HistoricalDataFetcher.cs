using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Persistence;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Core.Services.Accessibility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace AccessibleTrader.Core.Services
{
    public class HistoricalDataFetcher
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly IDataService _dataService;
        private readonly IResamplerService _resampler;
        private readonly IGlobalErrorCoordinator _errorCoordinator;
        private readonly ILogger<HistoricalDataFetcher> _logger;
        private readonly ConcurrentDictionary<string, IAsyncPolicy> _providerPolicies = new();

        public HistoricalDataFetcher(
            IDbContextFactory<AppDbContext> dbContextFactory, 
            IDataService dataService, 
            IResamplerService resampler, 
            IGlobalErrorCoordinator errorCoordinator,
            ILogger<HistoricalDataFetcher> logger)
        {
            _dbContextFactory = dbContextFactory;
            _dataService = dataService;
            _resampler = resampler;
            _errorCoordinator = errorCoordinator;
            _logger = logger;
        }

        private IAsyncPolicy GetProviderPolicy(string provider)
        {
            return _providerPolicies.GetOrAdd(provider, p => 
            {
                // Simple 1-time retry for transient network issues. 
                // We don't want exponential backoff here because it blocks the UI/Announcements.
                var retryPolicy = Policy
                    .Handle<HttpRequestException>()
                    .Or<TimeoutException>()
                    .WaitAndRetryAsync(
                        retryCount: 1,
                        sleepDurationProvider: _ => TimeSpan.FromSeconds(1));

                var circuitBreakerPolicy = Policy
                    .Handle<Exception>(ex => ex.Message.Contains("429") || ex.Message.Contains("Rate"))
                    .CircuitBreakerAsync(
                        exceptionsAllowedBeforeBreaking: 2,
                        durationOfBreak: TimeSpan.FromSeconds(30));

                return Policy.WrapAsync(circuitBreakerPolicy, retryPolicy);
            });
        }

        public virtual async Task<List<Ohlcv>> FetchOhlcvAsync(string market, string providerName, string symbol, string timeframe, long? since = null, int? limit = null, long? until = null)        
        {
            _logger.LogInformation("Orchestrating fetch for {Market}:{ProviderName}:{Symbol} @ {Timeframe}.", market, providerName, symbol, timeframe);
            
            var provider = await _dataService.GetProviderAsync(providerName).ConfigureAwait(false);
            if (provider == null) return new List<Ohlcv>();

            int localLimit = Math.Min((limit ?? 200) * 5, 1000);
            try
            {
                // FIX: Only attempt local cache fetch if the target timeframe is 1m,
                // OR if we implement a more robust multi-timeframe cache later.
                // Currently, the DB only stores 1m bars for resampling.
                if (timeframe == "1m")
                {
                    var local1mData = await FetchFromLocalCache(market, providerName, symbol, "1m", since, until, localLimit).ConfigureAwait(false);
                    if (local1mData.Any())
                    {
                        return ApplyFinalFilters(local1mData, since, until, limit ?? 200);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Local cache fetch skipped.");
            }

            bool needsResample = !provider.NativelySupportedTimeframes.Contains(timeframe);
            string effectiveTimeframe = timeframe;
            int effectiveLimit = limit ?? 200;

            if (needsResample)
            {
                effectiveTimeframe = TimeframeUtility.GetBestBaseTimeframe(timeframe, provider.NativelySupportedTimeframes);
                long targetMs = TimeframeUtility.ToMilliseconds(timeframe);
                long baseMs = TimeframeUtility.ToMilliseconds(effectiveTimeframe);
                if (baseMs > 0)
                {
                    double ratio = (double)targetMs / baseMs;
                    effectiveLimit = (int)Math.Ceiling((limit ?? 200) * ratio);
                }
                
                // Safety: ensure we fetch at least enough bars to satisfy the requested limit after resampling
                if (effectiveLimit < (limit ?? 200)) effectiveLimit = limit ?? 200;
                if (effectiveLimit > provider.MaxBarsPerRequest) effectiveLimit = provider.MaxBarsPerRequest;

                _logger.LogInformation("Provider {ProviderName} does not support {Timeframe} natively. Pivoting to {EffectiveTimeframe} with limit {EffectiveLimit}.", providerName, timeframe, effectiveTimeframe, effectiveLimit);
            }

            var policy = GetProviderPolicy(providerName);

            try
            {
                var request = new MarketDataRequest(market, symbol, effectiveTimeframe, effectiveLimit, since, until);
                
                var (nativeBars, _) = await policy.ExecuteAsync(async () =>
                {
                    return await _dataService.FetchOhlcvAsync(providerName, request).ConfigureAwait(false);
                }).ConfigureAwait(false);

                if (nativeBars.Any())
                {
                    // Verification Phase
                    bool actualNeedsResample = needsResample;
                    if (nativeBars.Count >= 2 && !actualNeedsResample)
                    {
                        long actualIntervalMs = new DateTimeOffset(DateTime.SpecifyKind(nativeBars[1].Date, DateTimeKind.Utc)).ToUnixTimeMilliseconds() - new DateTimeOffset(DateTime.SpecifyKind(nativeBars[0].Date, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
                        long expectedIntervalMs = TimeframeUtility.ToMilliseconds(timeframe);
                        
                        // Fuzzy Verification: If the actual interval is significantly smaller than the expected timeframe, provider lied.
                        // We check if it's less than 95% of expected interval to allow for slight provider jitter
                        if (expectedIntervalMs > 0 && actualIntervalMs > 0 && actualIntervalMs < (expectedIntervalMs * 0.95))
                        {
                            _logger.LogWarning("Data verification failed for {ProviderName}. Expected {Timeframe} ({ExpectedIntervalMs}ms) but received ~{ActualIntervalMs}ms. Forcing local resample.", providerName, timeframe, expectedIntervalMs, actualIntervalMs);
                            actualNeedsResample = true;
                        }
                    }

                    if (actualNeedsResample)
                    {
                        var resampled = _resampler.Resample(nativeBars, timeframe);
                        return ApplyFinalFilters(resampled, since, until, limit ?? 200);
                    }
                    return ApplyFinalFilters(nativeBars, since, until, limit ?? 200);
                }
            }
            catch (BrokenCircuitException)
            {
                _logger.LogWarning("Fetch request blocked for {ProviderName} because circuit is open.", providerName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled fetch error for {ProviderName}.", providerName);
            }

            return new List<Ohlcv>();
        }

        private async Task<List<Ohlcv>> FetchFromLocalCache(string market, string provider, string symbol, string timeframe, long? since, long? until, int? limit)
        {
            using var dbContext = await _dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
            var query = dbContext.OhlcvData
                .Where(e => e.Market == market && e.Provider == provider && e.Symbol == symbol && e.Timeframe == timeframe);

            if (since.HasValue) query = query.Where(e => e.Timestamp >= since.Value);
            if (until.HasValue) query = query.Where(e => e.Timestamp <= until.Value); // INCLUSIVE FILTERING

            var entities = await query.OrderBy(e => e.Timestamp).ToListAsync().ConfigureAwait(false);

            var result = entities.Select(e => new Ohlcv
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(e.Timestamp).UtcDateTime,
                Open = e.Open,
                High = e.High,
                Low = e.Low,
                Close = e.Close,
                Volume = e.Volume
            }).ToList();

            if (limit.HasValue && result.Count > limit.Value) return result.TakeLast(limit.Value).ToList();
            return result;
        }

        private List<Ohlcv> ApplyFinalFilters(List<Ohlcv> bars, long? since, long? until, int limit)
        {
            var filtered = bars;
            if (since.HasValue) filtered = filtered.Where(b => new DateTimeOffset(DateTime.SpecifyKind(b.Date, DateTimeKind.Utc)).ToUnixTimeMilliseconds() >= since.Value).ToList();
            if (until.HasValue) filtered = filtered.Where(b => new DateTimeOffset(DateTime.SpecifyKind(b.Date, DateTimeKind.Utc)).ToUnixTimeMilliseconds() <= until.Value).ToList(); // INCLUSIVE FILTERING
            // Strip zero-price bars: some providers return a forming candle with all-zero OHLCV
            // when a new period has just started and no trades have executed yet. Passing a zero
            // bar to indicators (e.g. Cipher A crossovers, SR break detection) causes spurious
            // signals that fire all at once on load. The live stream handles the forming candle.
            filtered = filtered.Where(b => b.Open != 0 && b.High != 0 && b.Low != 0 && b.Close != 0).ToList();
            return filtered.TakeLast(limit).ToList();
        }
    }
}