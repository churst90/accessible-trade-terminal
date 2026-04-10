using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// In-memory cache + fetch wrapper around <see cref="IDataOrchestrator.FetchOhlcvAsync"/>.
    /// Per-(provider, symbol, timeframe) cache entries expire on a TTL proportional to the
    /// bar size — a 1h bar can be cached for 60 seconds without becoming stale, a 1d bar
    /// for 5 minutes. Falls through to the orchestrator on miss/expiry; the orchestrator
    /// itself respects the SQLite OHLCV cache and provider rate limits.
    /// </summary>
    public class MultiTimeframeDataService : IMultiTimeframeDataService
    {
        private record CacheEntry(IReadOnlyList<Ohlcv> Bars, DateTime FetchedUtc);

        private readonly IDataOrchestrator _orchestrator;
        private readonly IAppLogger _logger;
        private readonly Indicators.IIndicatorEngine? _indicatorEngine;
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

        // HTF indicator computation cache: keyed by (provider|symbol|timeframe|indicatorCode),
        // value is the component-name → double[] result dictionary returned by IIndicatorEngine.
        // Populated by PrewarmIndicatorAsync, read synchronously by GetCachedIndicator.
        private readonly ConcurrentDictionary<string, Dictionary<string, double[]>> _indCache = new();

        public MultiTimeframeDataService(
            IDataOrchestrator orchestrator,
            IAppLogger logger,
            Indicators.IIndicatorEngine? indicatorEngine = null)
        {
            _orchestrator    = orchestrator;
            _logger          = logger;
            _indicatorEngine = indicatorEngine;
        }

        public async Task<IReadOnlyList<Ohlcv>> GetBarsAsync(
            string market, string provider, string symbol, string timeframe, int count)
        {
            string key = MakeKey(provider, symbol, timeframe);
            var ttl    = TtlFor(timeframe);

            if (_cache.TryGetValue(key, out var existing)
                && (DateTime.UtcNow - existing.FetchedUtc) < ttl
                && existing.Bars.Count >= count)
            {
                return existing.Bars;
            }

            try
            {
                var bars = await _orchestrator.FetchOhlcvAsync(
                    market, provider, symbol, timeframe,
                    since: null, limit: count, until: null, silent: true).ConfigureAwait(false);

                if (bars == null)
                {
                    return existing?.Bars ?? (IReadOnlyList<Ohlcv>)Array.Empty<Ohlcv>();
                }

                var entry = new CacheEntry(bars, DateTime.UtcNow);
                _cache[key] = entry;
                return entry.Bars;
            }
            catch (Exception ex)
            {
                // HTF fetches must never crash the strategy engine — log and fall back to
                // whatever stale cache entry we may already have, or empty.
                _logger.LogWarning(
                    $"MTF fetch failed for {provider}:{symbol}@{timeframe}: {ex.Message}",
                    nameof(MultiTimeframeDataService));
                return existing?.Bars ?? (IReadOnlyList<Ohlcv>)Array.Empty<Ohlcv>();
            }
        }

        public IReadOnlyList<Ohlcv> GetCachedBars(string provider, string symbol, string timeframe)
        {
            string key = MakeKey(provider, symbol, timeframe);
            if (_cache.TryGetValue(key, out var entry)) return entry.Bars;
            return Array.Empty<Ohlcv>();
        }

        public void Clear()
        {
            _cache.Clear();
            _indCache.Clear();
        }

        public async Task PrewarmIndicatorAsync(
            string market, string provider, string symbol, string timeframe,
            string indicatorCode, Dictionary<string, object> parameters, int count)
        {
            if (_indicatorEngine == null) return;
            if (string.IsNullOrEmpty(indicatorCode)) return;

            string key = MakeIndicatorKey(provider, symbol, timeframe, indicatorCode);

            // Cheap idempotence: if a non-empty entry already exists, skip the recompute. The
            // cache is dropped explicitly via Clear(); inside a single strategy lifetime the HTF
            // indicator state is stable enough that one compute per strategy add is sufficient.
            // Future enhancement: TTL refresh per bar-close on the HTF, mirroring TtlFor().
            if (_indCache.TryGetValue(key, out var existing) && existing.Count > 0) return;

            try
            {
                var bars = await GetBarsAsync(market, provider, symbol, timeframe, count).ConfigureAwait(false);
                if (bars == null || bars.Count == 0) return;

                var results = await _indicatorEngine.CalculateAsync(
                    indicatorCode, bars, parameters ?? new Dictionary<string, object>(), default).ConfigureAwait(false);
                if (results != null && results.Count > 0)
                {
                    _indCache[key] = results;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    $"MTF indicator pre-warm failed for {provider}:{symbol}@{timeframe}/{indicatorCode}: {ex.Message}",
                    nameof(MultiTimeframeDataService));
            }
        }

        public Dictionary<string, double[]>? GetCachedIndicator(
            string provider, string symbol, string timeframe, string indicatorCode)
        {
            string key = MakeIndicatorKey(provider, symbol, timeframe, indicatorCode);
            return _indCache.TryGetValue(key, out var results) ? results : null;
        }

        private static string MakeIndicatorKey(string provider, string symbol, string timeframe, string indicatorCode) =>
            $"{provider?.ToLowerInvariant()}|{symbol?.ToUpperInvariant()}|{timeframe?.ToLowerInvariant()}|{indicatorCode?.ToUpperInvariant()}";

        private static string MakeKey(string provider, string symbol, string timeframe) =>
            $"{provider?.ToLowerInvariant()}|{symbol?.ToUpperInvariant()}|{timeframe?.ToLowerInvariant()}";

        /// <summary>
        /// Cache TTL roughly proportional to bar size. Tunable per-timeframe so the hot
        /// active TF doesn't suffer staleness while a daily TF doesn't refetch on every tick.
        /// Anything we don't recognise gets a 30s default which is safe for almost any TF.
        /// </summary>
        private static TimeSpan TtlFor(string timeframe)
        {
            if (string.IsNullOrEmpty(timeframe)) return TimeSpan.FromSeconds(30);
            string t = timeframe.ToLowerInvariant();
            if (t.EndsWith("m")) return TimeSpan.FromSeconds(15); // 1m, 5m, 15m
            if (t.EndsWith("h")) return TimeSpan.FromSeconds(60); // 1h, 4h
            if (t.EndsWith("d")) return TimeSpan.FromMinutes(5);  // 1d, 3d
            if (t.EndsWith("w")) return TimeSpan.FromMinutes(15); // 1w
            return TimeSpan.FromSeconds(30);
        }
    }
}
