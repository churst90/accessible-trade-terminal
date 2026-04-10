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
