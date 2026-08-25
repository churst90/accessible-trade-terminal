using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Surrogate-data significance test — the honest control for "does price respect this line".
///
/// <para>
/// WHY THE OBVIOUS CONTROL FAILS. The first attempt compared a moving average against a copy of
/// itself displaced by ±2 ATR. That control is confounded by DISTANCE FROM PRICE: a 10 EMA hugs
/// price and is touched thousands of times, mostly in chop where price immediately crosses back,
/// while its displaced twin is only reached on genuine excursions, which mean-revert. The twin
/// therefore posts a far higher "hold" rate for reasons that have nothing to do with the level
/// being real, and the fast averages score catastrophically negative. That is an artifact.
/// </para>
///
/// <para>
/// WHAT THIS DOES INSTEAD. It builds surrogate price series by block-bootstrapping the real log
/// returns — preserving the return distribution, the drift and (via blocks) volatility
/// clustering, while destroying any memory of specific price levels. The SAME moving average is
/// then computed on each surrogate and tested against that surrogate. Proximity is preserved by
/// construction, because the line is derived from the very series it is measured against.
/// </para>
///
/// <para>
/// The question becomes precise and fair: does real price respect its own 10 EMA more than a
/// statistically identical random series respects ITS own 10 EMA? If yes, level memory exists.
/// If the real value sits inside the surrogate distribution, the line is decoration.
/// </para>
/// </summary>
public static class SurrogateTest
{
    /// <summary>Block length in bars. Long enough to carry volatility clustering across.</summary>
    public const int BlockLength = 20;

    public sealed record Result(
        string Asset,
        string Label,
        int Touches,
        double RealHold,
        double SurrogateMean,
        double SurrogateStdDev,
        int SurrogateCount,
        int BeatenBy)
    {
        /// <summary>Real minus surrogate mean, in percentage points.</summary>
        public double EdgePp => (RealHold - SurrogateMean) * 100;

        /// <summary>How many standard deviations above the surrogate mean the real value sits.</summary>
        public double ZScore => SurrogateStdDev > 1e-9 ? (RealHold - SurrogateMean) / SurrogateStdDev : double.NaN;

        /// <summary>
        /// One-sided empirical p-value: the fraction of surrogates that matched or beat the real
        /// series. Reported rather than a z-score alone because the surrogate distribution is not
        /// guaranteed to be normal.
        /// </summary>
        public double PValue => (BeatenBy + 1.0) / (SurrogateCount + 1.0);
    }

    /// <summary>
    /// Runs <paramref name="surrogateCount"/> surrogates for one (bars, spec) pair.
    /// <paramref name="seed"/> is fixed by the caller so runs are reproducible.
    /// </summary>
    public static Result Run(
        string asset,
        IReadOnlyList<Ohlcv> bars,
        string chartTimeframe,
        MaSpec spec,
        MaRespectRanker ranker,
        ILevelRespectAnalyzer analyzer,
        RespectOptions opts,
        int surrogateCount,
        int seed)
    {
        var realCandidate = ranker.BuildCandidate(bars, chartTimeframe, spec);
        if (realCandidate == null)
            return new Result(asset, spec.Label, 0, double.NaN, double.NaN, double.NaN, 0, 0);

        var realStats = analyzer.Analyze(bars, new[] { realCandidate }, opts).FirstOrDefault();
        if (realStats == null || realStats.Touches == 0)
            return new Result(asset, spec.Label, 0, double.NaN, double.NaN, double.NaN, 0, 0);

        double real = realStats.HoldRate;
        var rng = new Random(seed);
        var holds = new List<double>(surrogateCount);
        int beaten = 0;

        for (int s = 0; s < surrogateCount; s++)
        {
            var surrogate = BlockBootstrap(bars, rng);
            var candidate = ranker.BuildCandidate(surrogate, chartTimeframe, spec);
            if (candidate == null) continue;

            var stats = analyzer.Analyze(surrogate, new[] { candidate }, opts).FirstOrDefault();
            if (stats == null || stats.Touches == 0) continue;

            holds.Add(stats.HoldRate);
            if (stats.HoldRate >= real) beaten++;
        }

        if (holds.Count == 0)
            return new Result(asset, spec.Label, realStats.Touches, real, double.NaN, double.NaN, 0, 0);

        double mean = holds.Average();
        double sd = Math.Sqrt(holds.Sum(h => (h - mean) * (h - mean)) / Math.Max(1, holds.Count - 1));
        return new Result(asset, spec.Label, realStats.Touches, real, mean, sd, holds.Count, beaten);
    }

    /// <summary>
    /// Builds a surrogate OHLCV series by resampling contiguous BLOCKS of log returns, then
    /// rebuilding each bar's open/high/low around the new close using that bar's original ratios
    /// to its own close. Keeping the ratios means candle ranges and wick proportions survive, so
    /// touch detection sees realistic bars rather than a synthetic line.
    ///
    /// Timestamps are copied from the real series so higher-timeframe resampling buckets
    /// identically — otherwise the multi-timeframe specs could not be tested at all.
    /// </summary>
    public static List<Ohlcv> BlockBootstrap(IReadOnlyList<Ohlcv> bars, Random rng)
    {
        int n = bars.Count;
        var result = new List<Ohlcv>(n);
        if (n < BlockLength + 2) return new List<Ohlcv>(bars);

        // Log returns of close, plus each bar's shape as ratios to its own close.
        var logReturns = new double[n];
        var openRatio = new double[n];
        var highRatio = new double[n];
        var lowRatio = new double[n];
        for (int i = 1; i < n; i++)
        {
            double prev = bars[i - 1].Close, cur = bars[i].Close;
            logReturns[i] = prev > 0 && cur > 0 ? Math.Log(cur / prev) : 0;
        }
        for (int i = 0; i < n; i++)
        {
            double c = bars[i].Close;
            openRatio[i] = c > 0 ? bars[i].Open / c : 1;
            highRatio[i] = c > 0 ? bars[i].High / c : 1;
            lowRatio[i] = c > 0 ? bars[i].Low / c : 1;
        }

        double price = bars[0].Close;
        int written = 0;
        while (written < n)
        {
            // Draw a block start such that a whole block fits.
            int start = rng.Next(1, Math.Max(2, n - BlockLength));
            for (int k = 0; k < BlockLength && written < n; k++, written++)
            {
                int src = start + k;
                if (src >= n) src = n - 1;

                price *= Math.Exp(logReturns[src]);
                if (price <= 0 || double.IsNaN(price) || double.IsInfinity(price)) price = bars[0].Close;

                // Shape ratios come from the same source bar, keeping range realistic.
                double open = price * openRatio[src];
                double high = price * highRatio[src];
                double low = price * lowRatio[src];
                // Guard the OHLC invariant after the rescale.
                high = Math.Max(high, Math.Max(open, price));
                low = Math.Min(low, Math.Min(open, price));

                result.Add(new Ohlcv(bars[written].Date, open, high, low, price, bars[src].Volume));
            }
        }
        return result;
    }
}
