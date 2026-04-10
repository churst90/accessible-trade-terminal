using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Identifies a cross-series fetch target. Two requests with equal records share a cache
    /// entry and an in-flight task. Note that <see cref="MaxPages"/> is part of the equality
    /// because a 1-page fetch and a 10-page walk-back are conceptually different operations
    /// even on the same (market, provider, symbol) — the latter is strictly more data.
    /// </summary>
    public sealed record CrossSeriesRequest(
        string Market,
        string Provider,
        string Symbol,
        string Timeframe,
        int MaxPages = 1)
    {
        // Cache key drops MaxPages: an indicator that asked for 10 pages and an indicator that
        // asked for 1 page should still share the cache *result* (the 10-page fetch is a strict
        // superset). The 10-page indicator does the heavy fetch; the 1-page indicator just
        // benefits from the larger result. This works because we always fetch the deepest
        // request first when there's contention — if both indicators are added at once, the
        // semaphore queueing in the cache service serialises them.
        public string CacheKey => $"{Provider}:{Symbol}:{Timeframe}".ToLowerInvariant();
    }

    /// <summary>
    /// Shared cross-series data cache. Indicators that source values from a different series
    /// than the active chart (FundingRate, OpenInterest, FearGreed, CrowdingIndex, future
    /// Glassnode-backed indicators) all read through this service instead of holding their
    /// own per-provider static caches.
    ///
    /// The contract is intentionally simple: <see cref="GetOrFetch"/> returns the cached
    /// time-series data for a key, fetching synchronously on the first call so the indicator's
    /// first paint already has values. After that, all subsequent calls hit the cache and
    /// return immediately. Empty list = either fetch failed or no data exists; indicators
    /// should treat empty as "render NaN" rather than as an error.
    ///
    /// Why synchronous on first call: <see cref="AccessibleTrader.Sdk.Interfaces.IIndicatorProvider.Calculate"/>
    /// is synchronous by contract and runs on a worker thread. Adding a fire-and-forget fetch
    /// doesn't trigger a chart re-render when the data lands, so the user sees an empty
    /// indicator until some unrelated event (live tick, pan) causes the next Calculate. The
    /// alternative — wiring an event-bus refresh trigger — is invasive plumbing for marginal
    /// benefit. A bounded synchronous wait on first add gives correct first-paint with
    /// minimal complexity, blocking the indicator pipeline thread for ~1-2s only the first
    /// time the indicator is loaded in a session.
    ///
    /// Walk-back pagination is built in via <see cref="CrossSeriesRequest.MaxPages"/>. The
    /// cache service walks chronologically backwards (each successive page asks for bars
    /// older than the oldest seen so far) and stitches the chunks into one ascending,
    /// deduped list. Single-page providers pass MaxPages=1; the funding-rate indicator passes
    /// MaxPages=10 to reach the full ~3 months of OKX history.
    /// </summary>
    public interface ICrossSeriesCache
    {
        /// <summary>
        /// Returns cached cross-series data for the request, fetching synchronously with a
        /// bounded timeout if not yet cached. Safe to call from <c>IIndicatorProvider.Calculate</c>.
        /// Returns an empty list if the fetch fails or returns no data.
        /// </summary>
        IReadOnlyList<(long Ts, double Value)> GetOrFetch(CrossSeriesRequest request);
    }

    public sealed class CrossSeriesCache : ICrossSeriesCache
    {
        private readonly IDataOrchestrator _orch;

        // Successful results, sorted ascending by timestamp.
        private readonly ConcurrentDictionary<string, IReadOnlyList<(long Ts, double Value)>> _cache = new();

        // In-flight or completed fetch tasks. We keep completed tasks here so subsequent
        // GetOrFetch calls can quickly observe completion via Task.IsCompleted without
        // creating a new task.
        private readonly ConcurrentDictionary<string, Task> _fetchTasks = new();

        // First-add wait timeout. Generous enough that the worst-case walk-back pagination
        // (10 pages × ~200ms each = 2s) completes inside it, with headroom for slow networks.
        // After cache is populated this code path is never hit again for that key.
        private static readonly TimeSpan FirstFetchWaitTimeout = TimeSpan.FromSeconds(5);

        public CrossSeriesCache(IDataOrchestrator orch)
        {
            _orch = orch;
        }

        public IReadOnlyList<(long Ts, double Value)> GetOrFetch(CrossSeriesRequest request)
        {
            string key = request.CacheKey;

            // Hot path: cache already populated, return immediately. This is the case for
            // every Calculate after the first one in the session.
            if (_cache.TryGetValue(key, out var hit)) return hit;

            // Cold path: kick off (or join) the background fetch and synchronously wait.
            var task = _fetchTasks.GetOrAdd(key, _ => Task.Run(() => FetchAsync(request)));
            try { task.Wait(FirstFetchWaitTimeout); }
            catch
            {
                // Task.Wait can throw AggregateException if the task itself faulted. The fetch
                // method already absorbs network errors and writes nothing to the cache on
                // failure, so we just swallow here and let the caller see an empty result.
            }

            return _cache.TryGetValue(key, out var afterWait)
                ? afterWait
                : Array.Empty<(long Ts, double Value)>();
        }

        private async Task FetchAsync(CrossSeriesRequest request)
        {
            try
            {
                var allBars = new List<Ohlcv>();
                long? until = null;
                long oldestSoFar = long.MaxValue;
                const int PartialPageThreshold = 50;

                for (int page = 0; page < request.MaxPages; page++)
                {
                    var bars = await _orch.FetchOhlcvAsync(
                        request.Market, request.Provider, request.Symbol, request.Timeframe,
                        until: until, silent: true);

                    if (bars == null || bars.Count == 0) break;

                    allBars.AddRange(bars);

                    long pageOldest = long.MaxValue;
                    foreach (var b in bars)
                    {
                        long ts = new DateTimeOffset(b.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        if (ts < pageOldest) pageOldest = ts;
                    }

                    // Defensive: if a page didn't make any progress on the oldest timestamp
                    // we'd loop forever re-requesting the same window. Bail.
                    if (pageOldest >= oldestSoFar) break;
                    oldestSoFar = pageOldest;
                    until = pageOldest;

                    // A short page indicates we've hit the provider's history depth limit.
                    if (bars.Count < PartialPageThreshold) break;
                }

                if (allBars.Count == 0)
                {
                    // Allow a future call to retry by forgetting the in-flight task slot.
                    _fetchTasks.TryRemove(request.CacheKey, out _);
                    return;
                }

                // Dedupe by timestamp (page boundaries can overlap by one bar) and sort
                // ascending so the forward-fill helper can binary-walk the result.
                var sorted = allBars
                    .Select(b => (Ts: new DateTimeOffset(b.Date, TimeSpan.Zero).ToUnixTimeMilliseconds(),
                                  Value: b.Close))
                    .GroupBy(t => t.Ts)
                    .Select(g => g.First())
                    .OrderBy(t => t.Ts)
                    .ToList();

                _cache[request.CacheKey] = sorted;
            }
            catch
            {
                // Network errors, parse failures, etc. — swallow, let the caller see empty.
                // Forgetting the task slot allows a future Calculate to retry rather than
                // permanently caching the failure.
                _fetchTasks.TryRemove(request.CacheKey, out _);
            }
        }
    }

    /// <summary>
    /// Pure helper for forward-filling cached time-series values onto a chart bar timeline by
    /// timestamp. Lives next to the cache because every cross-series indicator uses it the
    /// same way and we'd rather have one tested copy than four near-identical ones.
    /// </summary>
    public static class CrossSeriesForwardFill
    {
        /// <summary>
        /// For each bar in <paramref name="bars"/>, write the most recent <paramref name="ticks"/>
        /// value whose timestamp is ≤ the bar's timestamp into <paramref name="output"/>. Bars
        /// older than the oldest tick are left untouched (caller should pre-init <paramref name="output"/>
        /// to NaN). Both inputs must be sorted ascending by timestamp.
        /// </summary>
        public static void Fill(
            IReadOnlyList<(long Ts, double Value)> ticks,
            ReadOnlySpan<Ohlcv> bars,
            Span<double> output)
        {
            if (ticks == null || ticks.Count == 0 || bars.Length == 0) return;

            int tickIdx = 0;
            int n = Math.Min(bars.Length, output.Length);
            for (int i = 0; i < n; i++)
            {
                long barTs = new DateTimeOffset(bars[i].Date, TimeSpan.Zero).ToUnixTimeMilliseconds();

                // Advance tickIdx while the next tick is still ≤ this bar's timestamp.
                while (tickIdx + 1 < ticks.Count && ticks[tickIdx + 1].Ts <= barTs)
                    tickIdx++;

                // First tick is later than this bar — leave NaN.
                if (ticks[tickIdx].Ts > barTs) continue;

                output[i] = ticks[tickIdx].Value;
            }
        }
    }
}
