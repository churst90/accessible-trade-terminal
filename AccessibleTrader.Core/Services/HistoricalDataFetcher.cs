using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Persistence;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Core.Services.Accessibility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public class HistoricalDataFetcher
    {
        private readonly IDataService _dataService;
        private readonly IResamplerService _resampler;
        private readonly IGlobalErrorCoordinator _errorCoordinator;
        private readonly ILogger<HistoricalDataFetcher> _logger;
        private readonly IOhlcvStore? _store;

        // No IDbContextFactory: the fetcher no longer touches the database directly. All OHLCV
        // persistence goes through IOhlcvStore, which owns the closed-window and all-or-nothing
        // rules that made the old inline reader unsafe to keep alongside it.
        public HistoricalDataFetcher(
            IDataService dataService,
            IResamplerService resampler,
            IGlobalErrorCoordinator errorCoordinator,
            ILogger<HistoricalDataFetcher> logger,
            IOhlcvStore? store = null)
        {
            _dataService = dataService;
            _resampler = resampler;
            _errorCoordinator = errorCoordinator;
            _logger = logger;
            _store = store;
        }

        // The retry + circuit-breaker stack that used to live here has moved UP into
        // DataOrchestrator, which already wrapped this method in a second, nearly
        // identical one. Both were dead — DataService swallowed every exception below
        // them — and simply reviving both would have turned one failed request into
        // four (outer retry × inner retry) against a provider that is already
        // struggling, which is the classic retry-amplification failure. One layer,
        // one place: DataOrchestrator.GetResiliencePolicy, which is also the one that
        // publishes ConnectionStatusEvent and fires the pipeline state triggers.
        // What this class contributed and the orchestrator did not — TimeoutException
        // in the retry set, and the 429/rate-limit breaker — moved with it.

        public virtual async Task<List<Ohlcv>> FetchOhlcvAsync(string market, string providerName, string symbol, string timeframe, long? since = null, int? limit = null, long? until = null)
        {
            _logger.LogInformation("Orchestrating fetch for {Market}:{ProviderName}:{Symbol} @ {Timeframe}.", market, providerName, symbol, timeframe);
            
            var provider = await _dataService.GetProviderAsync(providerName).ConfigureAwait(false);
            if (provider == null) return new List<Ohlcv>();

            // ── Served from the on-disk store? ───────────────────────────────────────────
            // Only for a window that has entirely closed — in practice the scrollback/prepend
            // path, which asks for the 200 bars before a fixed point. Those bars are immutable,
            // they are the bulk of what gets re-fetched, and serving them costs nothing in
            // freshness. A live-edge request always goes to the provider: its newest bar is still
            // forming, and a stale last price is a worse failure than a slow chart because
            // nothing about it looks wrong. See OhlcvStore.
            if (_store != null && until.HasValue && IsClosedWindow(until.Value, timeframe))
            {
                // Guarded on its own: a local disk fault must not be reported as, or
                // counted as, a network failure — the network is right there and can
                // serve the same window. Same reason DataService guards its cache.
                try
                {
                    var stored = await _store
                        .TryReadClosedWindowAsync(market, providerName, symbol, timeframe, until.Value, limit ?? 200)
                        .ConfigureAwait(false);
                    if (stored.Count > 0)
                    {
                        _logger.LogInformation("Served {Count} {Timeframe} bars for {Symbol} from the local store.",
                            stored.Count, timeframe, symbol);
                        return ApplyFinalFilters(stored, since, until, limit ?? 200);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Local store read failed for {Symbol} @ {Timeframe}; falling through to the provider.",
                        symbol, timeframe);
                }
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

            var request = new MarketDataRequest(market, symbol, effectiveTimeframe, effectiveLimit, since, until);

            // No try/catch: transport failures belong to DataOrchestrator's policy, which
            // is what turns them into a retry, a tripped breaker, a ConnectionStatusEvent
            // and an audible "failed to load data". Swallowing them here — as this method
            // and DataService both used to — is what left an empty chart as the only
            // symptom of a dead network.
            var (nativeBars, _) = await _dataService.FetchOhlcvAsync(providerName, request).ConfigureAwait(false);

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

                // Store what the user actually charts — post-resample, at the REQUESTED
                // timeframe. The old read path only ever looked at 1m, which is why a
                // 1h or 1d chart could never be served from disk.
                var finalBars = actualNeedsResample
                    ? ApplyFinalFilters(_resampler.Resample(nativeBars, timeframe), since, until, limit ?? 200)
                    : ApplyFinalFilters(nativeBars, since, until, limit ?? 200);

                if (_store != null && finalBars.Count > 0)
                {
                    // Fire-and-forget would race the next read; awaiting costs a few ms of
                    // local I/O against a network fetch we just paid for. Guarded for the
                    // same reason the read above is: a full disk must not discard bars we
                    // already have in hand, nor be reported as a network fault.
                    try
                    {
                        await _store.SaveAsync(market, providerName, symbol, timeframe, finalBars)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Local store write failed for {Symbol} @ {Timeframe}; the bars are still returned.",
                            symbol, timeframe);
                    }
                }

                return finalBars;
            }

            return new List<Ohlcv>();
        }

        /// <summary>
        /// True when every bar up to <paramref name="until"/> has closed, so the window can never
        /// change again and is safe to serve from disk.
        /// </summary>
        private static bool IsClosedWindow(long until, string timeframe)
        {
            long barMs = TimeframeUtility.ToMilliseconds(timeframe ?? "");
            if (barMs <= 0) return false;
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return until <= nowMs - barMs;
        }

        // FetchFromLocalCache lived here. It was the 1m-only read path that the OhlcvStore read
        // above replaced, and once that landed nothing called it — the comment claiming it was
        // "retained for the 1m resampling path and for tests" was already untrue when written.
        // Removed rather than left as a second, differently-behaved reader of the same table
        // (it had neither the closed-window rule nor the all-or-nothing rule).

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