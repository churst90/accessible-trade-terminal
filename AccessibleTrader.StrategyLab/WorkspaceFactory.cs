using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Builds a backtester-ready <see cref="WorkspaceState"/> from a snapshot file plus a list
/// of indicator codes. Each requested indicator is computed once via <see cref="IIndicatorEngine"/>
/// and dropped into the resulting <see cref="ChartSeries"/> as a <see cref="SeriesDataBuffer"/>
/// keyed by component name.
///
/// The backtester only reads <c>state.Identity</c> (for HTF cache routing — irrelevant here)
/// and <c>state.ActiveSeries</c> (for ConditionEvaluator's series lookup and the CSV feature
/// snapshot capture). All other <see cref="WorkspaceState"/> fields use defaults.
/// </summary>
public static class WorkspaceFactory
{
    /// <summary>
    /// Default indicator pack for v11/v12 family backtests. Mirrors what the live UI populates
    /// when you load a fresh BTC/USDT chart in Trading mode: candles + cipher A + cipher B.
    /// CANDLES / PRICE / VOLUME are required for the feature snapshot to mirror live results.
    /// </summary>
    public static readonly string[] DefaultIndicatorPack =
    {
        "CANDLES",
        "PRICE",
        "VOLUME",
        "CIPHER_A",
        "CIPHER_B",
        "CIPHER_SR",
        "CIPHER_C",
        "LOUKAS_CYCLES",
        "REGIME",       // synthetic — see ProjectRegime
        "BNVISION_FUNDING",
        "BNVISION_OI",
        "CFTC_COT",
        "PULSE",

        // Cross-series — only meaningful when LabHost picks up xs_*.json snapshots from
        // strategy-lab-data. If the cache is empty for these keys, the providers' Calculate
        // methods produce all-NaN arrays (no harm done), and any condition leaf gating on
        // them will silently fail to fire. Keeping them in the default pack means strategies
        // that DO want to use sentiment/funding/OI just work without caller plumbing.
        "FEAR_GREED",
        "FUNDING_RATE",
        "OPEN_INTEREST",
        "COINMETRICS",  // CoinMetrics community-tier on-chain (MVRV, active addresses, hash rate)

        // v22 family — single-indicator reversal detector. Self-contained
        // (no cross-series cache); included in the default pack so v22 specs
        // work in walk-forward without caller plumbing.
        "TOP_BOTTOM_DETECTOR",

        // v23+ family — universal price-action indicators added 2026-04-27 e7
        // for the KAS/TAO investigation. All self-contained, no cross-series.
        "ANCHORED_VWAP",
        "HURST",
        "PIVOTS",

        // BTC strength — synthetic, projected from a sibling BTC snapshot in the
        // same strategy-lab-data folder. Skipped automatically if no matching BTC
        // snapshot exists. Components: BtcRatio, BtcRatioMomentum.
        "BTC_STRENGTH",
    };

    public static async Task<WorkspaceState> BuildAsync(
        IServiceProvider services,
        SnapshotFile snapshot,
        IEnumerable<string>? indicatorCodes = null,
        CancellationToken ct = default)
    {
        var engine = services.GetRequiredService<IIndicatorEngine>();
        var codes = (indicatorCodes ?? DefaultIndicatorPack).ToArray();
        var bars = snapshot.Bars;

        var seriesList = ImmutableList.CreateBuilder<ChartSeries>();
        foreach (var code in codes)
        {
            Dictionary<string, double[]> result;

            // CANDLES / PRICE / VOLUME are pseudo-indicators in CoreIndicatorProvider — its
            // Calculate() is a no-op because the live IndicatorOrchestrator special-cases them
            // and projects state.Data into ComponentData directly. Replicate that projection
            // here so feature snapshots in the backtester CSV carry the same columns the live
            // app produces. Without this, the v11 diagnostic feature columns (CANDLES.Candle
            // Body, PRICE.Close, etc.) would be empty in our research CSVs and we couldn't
            // analyze winners-vs-losers on raw price/volume features.
            if (code == "CANDLES")
            {
                result = ProjectCandles(bars);
            }
            else if (code == "PRICE")
            {
                result = ProjectPrice(bars);
            }
            else if (code == "VOLUME")
            {
                result = ProjectVolume(bars);
            }
            else if (code == "BTC_STRENGTH")
            {
                // Synthetic: ratio of asset close vs BTC close on the same bar +
                // 14-bar momentum of that ratio. Loaded from the sibling BTC snapshot
                // file in the strategy-lab-data dir. Skipped (NaN columns) if BTC
                // snapshot at the same TF doesn't exist.
                result = ProjectBtcStrength(snapshot);
                if (result.Count == 0)
                {
                    Console.WriteLine($"  - BTC_STRENGTH: no sibling BTC snapshot at {snapshot.Timeframe}, skipped");
                    continue;
                }
            }
            else
            {
                // Real indicator — empty parameters dict means providers use their declared
                // defaults, matching what the live UI feeds them on a fresh chart load.
                // EXCEPT: we inject the snapshot's symbol via the well-known "__symbol" key
                // so cross-series providers (BNVISION_FUNDING / BNVISION_OI / CFTC_COT /
                // COINMETRICS) can route directly to the right asset's data instead of guessing
                // from median close. The median-close fallback is preserved for any provider
                // that doesn't read __symbol, but providers updated after 2026-04-09 should
                // prefer the explicit hint. Asset-detection bug fix: LTC's $80 median used to
                // collide with SOL's range and CFTC's WTI range; this kills both collisions.
                var parameters = new Dictionary<string, object>
                {
                    ["__symbol"] = snapshot.Symbol,
                };

                // v22 family: enable timeframe-adaptive scaling so the same Bottom/Top
                // Confirmed semantics apply across 4h/1d/1w. Without this, default
                // bar-count gates (100-bar lookback, 5-ATR meaningful range) make 1w
                // produce zero fires and 1d under-fire on shorter snapshots. Lab
                // research has confirmed (2026-04-27) that 1d v22-LONG is the highest-
                // quality setup; adapting the gates by detected bar interval lets that
                // same setup fire reliably on weekly without changing semantics.
                if (code == TopBottomDetectorProvider.Code)
                {
                    parameters["TimeframeAdaptive"] = 1;
                }
                try
                {
                    result = await engine.CalculateAsync(code, bars, parameters, ct);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  ! indicator '{code}' failed: {ex.Message} — skipped");
                    continue;
                }
            }

            var config = new SeriesConfig
            {
                Name = code,
                FriendlyName = code,
                IndicatorCode = code,
                Pane = "Main"
            };
            var buffer = new SeriesDataBuffer
            {
                SeriesId = config.Id,
                ComponentData = result
            };
            seriesList.Add(new ChartSeries(config, buffer));

            int totalCells = result.Values.Sum(arr => arr.Length);
            Console.WriteLine($"  + {code}: {result.Count} components, {totalCells} cells");
        }

        var identity = new ChartIdentity(
            Market: "Spot",
            Provider: snapshot.Provider,
            Symbol: snapshot.Symbol,
            Timeframe: snapshot.Timeframe);

        return WorkspaceState.Initial with
        {
            Identity = identity,
            ActiveSeries = seriesList.ToImmutable()
        };
    }

    // CANDLES component names mirror CoreIndicatorProvider exactly so feature snapshot column
    // names match the live-app CSVs and any analysis scripts written against one work on both.
    private static Dictionary<string, double[]> ProjectCandles(IReadOnlyList<Ohlcv> bars)
    {
        int n = bars.Count;
        var body = new double[n];
        var upper = new double[n];
        var lower = new double[n];
        for (int i = 0; i < n; i++)
        {
            body[i] = bars[i].Close;
            upper[i] = bars[i].High;
            lower[i] = bars[i].Low;
        }
        // Keys match the machine component names in CoreIndicatorProvider's CANDLES
        // metadata. Harness code (ConditionEvaluator / backtester) looks up components
        // by these names, so they must stay in sync with Phase 2's rename.
        return new Dictionary<string, double[]>
        {
            ["body"] = body,
            ["upper_wick"] = upper,
            ["lower_wick"] = lower,
        };
    }

    private static Dictionary<string, double[]> ProjectPrice(IReadOnlyList<Ohlcv> bars)
    {
        var close = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++) close[i] = bars[i].Close;
        // "line" is the machine name of the single Price component. See PRICE metadata
        // in CoreIndicatorProvider.cs.
        return new Dictionary<string, double[]> { ["line"] = close };
    }

    private static Dictionary<string, double[]> ProjectVolume(IReadOnlyList<Ohlcv> bars)
    {
        var vol = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++) vol[i] = bars[i].Volume;
        return new Dictionary<string, double[]> { ["Volume"] = vol };
    }

    /// <summary>
    /// BTC-relative strength: log(asset_close / btc_close_at_same_bar). Larger value =
    /// asset outperforming BTC since baseline. Plus a 14-bar momentum component
    /// (current ratio − ratio 14 bars ago). Asymmetric utility: for ALTCOIN charts,
    /// BTC.D rising (BtcRatioMomentum &lt; 0 — alts losing to BTC) is bearish for
    /// alt LONGs and bullish for alt SHORTs. For BTC charts, this whole indicator is
    /// trivially zero / NaN — short-circuited.
    ///
    /// Snapshot lookup convention: looks for `{provider}_BTC_USDT_{tf}.json` in the
    /// same directory as the active snapshot. If not found, returns empty (caller
    /// skips the indicator gracefully, no exception). Both bitstamp_BTC_USDT_*.json
    /// and mexc_BTC_USDT_*.json work; the loader tries the active snapshot's provider
    /// first, then falls back to any provider it can find.
    /// </summary>
    private static Dictionary<string, double[]> ProjectBtcStrength(SnapshotFile snapshot)
    {
        // BTC charts: short-circuit. Ratio is identically 1 → zero log → no signal.
        if (snapshot.Symbol.StartsWith("BTC", StringComparison.OrdinalIgnoreCase))
            return new Dictionary<string, double[]>();

        // Try to find a BTC snapshot at the same TF. Tries the active provider first.
        // Lab convention: snapshot files live next to each other in `strategy-lab-data/`.
        var dir = Path.GetDirectoryName(Path.GetFullPath("strategy-lab-data")) ?? Directory.GetCurrentDirectory();
        // Walk up: callers run from various working directories. Find the canonical
        // strategy-lab-data dir.
        string? labDir = null;
        var probe = Directory.GetCurrentDirectory();
        for (int i = 0; i < 4 && probe != null; i++)
        {
            var candidate = Path.Combine(probe, "strategy-lab-data");
            if (Directory.Exists(candidate)) { labDir = candidate; break; }
            probe = Path.GetDirectoryName(probe);
        }
        if (labDir == null) return new Dictionary<string, double[]>();

        var prov = string.IsNullOrEmpty(snapshot.Provider) ? "bitstamp" : snapshot.Provider.ToLowerInvariant();
        var primary  = Path.Combine(labDir, $"{prov}_BTC_USDT_{snapshot.Timeframe}.json");
        var fallback = Path.Combine(labDir, $"bitstamp_BTC_USDT_{snapshot.Timeframe}.json");
        var path = File.Exists(primary) ? primary : (File.Exists(fallback) ? fallback : null);
        if (path == null) return new Dictionary<string, double[]>();

        SnapshotFile btc;
        try { btc = SnapshotCommand.Load(path); }
        catch { return new Dictionary<string, double[]>(); }

        // Build a date → BTC close lookup. Use a sorted list and binary-search-by-date so
        // bars don't need exact alignment to the millisecond.
        var btcBars = btc.Bars.OrderBy(b => b.Date).ToList();
        var btcDates = btcBars.Select(b => b.Date).ToArray();

        int n = snapshot.Bars.Count;
        var ratio = new double[n];
        var momentum = new double[n];
        for (int i = 0; i < n; i++) { ratio[i] = double.NaN; momentum[i] = double.NaN; }

        // Bar-alignment behavior verified 2026-04-27 e11: MEXC altcoin and Bitstamp BTC
        // snapshots align exactly at every 4h bar boundary on KAS 4h (7850/7850 exact
        // matches). The BinarySearch is "closest <= bar" so even cross-provider snapshots
        // with minor drift fall back to the previous BTC bar gracefully. We track total
        // and max drift so future cross-provider mismatches surface in the run log.
        int exactMatches = 0;
        long maxDriftSec = 0;
        long totalDriftSec = 0;
        int driftSamples = 0;
        for (int i = 0; i < n; i++)
        {
            var d = snapshot.Bars[i].Date;
            int idx = Array.BinarySearch(btcDates, d);
            if (idx >= 0) exactMatches++;
            if (idx < 0) idx = ~idx - 1;            // closest <= bar
            if (idx < 0 || idx >= btcBars.Count) continue;
            long driftSec = (long)Math.Abs((d - btcDates[idx]).TotalSeconds);
            totalDriftSec += driftSec;
            driftSamples++;
            if (driftSec > maxDriftSec) maxDriftSec = driftSec;
            var btcClose = btcBars[idx].Close;
            if (btcClose <= 0 || snapshot.Bars[i].Close <= 0) continue;
            ratio[i] = Math.Log(snapshot.Bars[i].Close / btcClose);
            if (i >= 14 && !double.IsNaN(ratio[i - 14]))
                momentum[i] = ratio[i] - ratio[i - 14];
        }

        if (driftSamples > 0)
        {
            double meanDriftSec = (double)totalDriftSec / driftSamples;
            Console.WriteLine(
                $"  - BTC_STRENGTH: aligned {exactMatches}/{n} bars exact, " +
                $"meanDrift={meanDriftSec:0.0}s, maxDrift={maxDriftSec}s " +
                $"(source={Path.GetFileName(path)})");
        }

        return new Dictionary<string, double[]>
        {
            ["BtcRatio"]         = ratio,
            ["BtcRatioMomentum"] = momentum,
        };
    }

    /// <summary>
    /// Synthetic regime indicator: emits two components, "AboveSma200" and "AboveEma200",
    /// each containing (Close − MA(close, 200)). Positive = price above MA = bull regime.
    /// Strategy leaves can then express the textbook 200-day MA filter with a single
    /// "REGIME.AboveSma200 GreaterThan 0" leaf — the same shape Mebane Faber's 2007 paper
    /// "A Quantitative Approach to Tactical Asset Allocation" uses, replicated across
    /// equities, commodities, and crypto for two decades. SMA is the canonical version
    /// (slower, fewer whipsaws); EMA is included for direct comparison since it reacts
    /// ~30% faster but whips more in chop. The first 199 bars are NaN (warmup).
    /// </summary>
    private static Dictionary<string, double[]> ProjectRegime(IReadOnlyList<Ohlcv> bars)
    {
        const int Period = 200;
        int n = bars.Count;
        var aboveSma = new double[n];
        var aboveEma = new double[n];

        // SMA — rolling sum.
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            sum += bars[i].Close;
            if (i >= Period) sum -= bars[i - Period].Close;
            if (i < Period - 1) { aboveSma[i] = double.NaN; }
            else { aboveSma[i] = bars[i].Close - sum / Period; }
        }

        // EMA — recursive, seeded with the SMA at index Period-1 to match the conventional
        // warmup convention used by Skender.Stock.Indicators (the indicator engine the rest
        // of the codebase uses). Using the SMA seed avoids the EMA's first-N bars being
        // skewed by the leading bar value alone.
        double k = 2.0 / (Period + 1);
        double ema = 0;
        for (int i = 0; i < n; i++)
        {
            if (i < Period - 1) { aboveEma[i] = double.NaN; continue; }
            if (i == Period - 1)
            {
                double s = 0;
                for (int j = 0; j < Period; j++) s += bars[j].Close;
                ema = s / Period;
            }
            else
            {
                ema = bars[i].Close * k + ema * (1 - k);
            }
            aboveEma[i] = bars[i].Close - ema;
        }

        return new Dictionary<string, double[]>
        {
            ["AboveSma200"] = aboveSma,
            ["AboveEma200"] = aboveEma,
        };
    }
}
