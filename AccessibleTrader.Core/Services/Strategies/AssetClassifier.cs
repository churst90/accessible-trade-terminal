using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Classifies an asset's bar history into four orthogonal class axes:
    ///   1. Volatility   — low / medium / high / extreme
    ///   2. Cycle        — trender / random / mean-reverter
    ///   3. Regime bias  — bull-biased / range / bear-biased
    ///   4. Liquidity    — tier-1 / tier-2 / micro
    ///
    /// Used to auto-tune strategy presets for unknown assets without hard-coding
    /// per-symbol rules. Classification looks at the last <c>WindowBars</c> bars
    /// (default 365 — one trading "year" worth at daily; one quarter at 4h).
    ///
    /// All metrics are bar-relative and price-scale-invariant so the same
    /// classifier works on $0.0001 KAS and $60,000 BTC.
    /// </summary>
    public static class AssetClassifier
    {
        public enum VolatilityClass { Low, Medium, High, Extreme }
        public enum CycleClass      { Trender, Random, MeanReverter }
        public enum RegimeClass     { BullBiased, Range, BearBiased }
        public enum LiquidityClass  { Tier1, Tier2, Micro }

        public sealed record Profile(
            VolatilityClass Volatility,
            CycleClass      Cycle,
            RegimeClass     Regime,
            LiquidityClass  Liquidity,
            double          AtrPctMedian,         // ATR as % of price, median of window
            double          HurstMedian,          // Median Hurst over window
            double          PctBarsAboveSma200,   // 0..1
            double          AvgVolDollar);        // average bar volume × close

        /// <summary>
        /// Classify based on the last <paramref name="windowBars"/> bars of OHLCV.
        /// Returns <c>null</c> if not enough bars to form a stable profile.
        /// </summary>
        public static Profile? Classify(IReadOnlyList<Ohlcv> bars, int windowBars = 365)
        {
            if (bars == null || bars.Count < windowBars + 200) return null;

            int n = bars.Count;
            int from = n - windowBars;

            // ── 1. Volatility (median ATR-as-pct-of-price) ────────────────────
            // ATR(14) Wilder, then median over the window.
            var atr = ComputeAtr(bars, 14);
            var atrPctList = new List<double>(windowBars);
            for (int i = from; i < n; i++)
            {
                if (!double.IsNaN(atr[i]) && bars[i].Close > 0)
                    atrPctList.Add(atr[i] / bars[i].Close);
            }
            atrPctList.Sort();
            double atrPctMedian = atrPctList.Count > 0 ? atrPctList[atrPctList.Count / 2] : 0;
            VolatilityClass vol = atrPctMedian switch
            {
                < 0.012 => VolatilityClass.Low,       // <1.2%/bar — equities-like
                < 0.025 => VolatilityClass.Medium,    // BTC daily territory
                < 0.045 => VolatilityClass.High,      // ETH/large-altcoin daily
                _       => VolatilityClass.Extreme,   // small-cap altcoins / KAS-like
            };

            // ── 2. Cycle (rolling Hurst median) ───────────────────────────────
            // Compute Hurst over a 100-bar rolling window each bar in [from, n).
            var ret = new double[n];
            for (int i = 1; i < n; i++)
            {
                if (bars[i - 1].Close > 0 && bars[i].Close > 0)
                    ret[i] = Math.Log(bars[i].Close / bars[i - 1].Close);
            }
            var hurstList = new List<double>();
            int hurstWindow = 100;
            int[] scales = { 10, 20, 25, 50 };
            for (int i = Math.Max(from, hurstWindow); i < n; i += 5) // sample every 5 bars for speed
            {
                double h = EstimateHurst(ret, i - hurstWindow + 1, hurstWindow, scales);
                hurstList.Add(h);
            }
            hurstList.Sort();
            double hurstMedian = hurstList.Count > 0 ? hurstList[hurstList.Count / 2] : 0.5;
            CycleClass cycle = hurstMedian switch
            {
                > 0.55 => CycleClass.Trender,
                < 0.45 => CycleClass.MeanReverter,
                _      => CycleClass.Random,
            };

            // ── 3. Regime bias (% of bars price > SMA200) ─────────────────────
            var sma = ComputeSma(bars, 200);
            int aboveCount = 0, considered = 0;
            for (int i = from; i < n; i++)
            {
                if (double.IsNaN(sma[i])) continue;
                considered++;
                if (bars[i].Close > sma[i]) aboveCount++;
            }
            double pctAbove = considered > 0 ? (double)aboveCount / considered : 0.5;
            RegimeClass regime = pctAbove switch
            {
                > 0.65 => RegimeClass.BullBiased,
                < 0.35 => RegimeClass.BearBiased,
                _      => RegimeClass.Range,
            };

            // ── 4. Liquidity (avg dollar volume per bar) ──────────────────────
            double sumDollarVol = 0;
            int dvCount = 0;
            for (int i = from; i < n; i++)
            {
                double dv = bars[i].Volume * bars[i].Close;
                if (dv > 0) { sumDollarVol += dv; dvCount++; }
            }
            double avgDollarVol = dvCount > 0 ? sumDollarVol / dvCount : 0;
            LiquidityClass liquidity = avgDollarVol switch
            {
                >= 100_000_000 => LiquidityClass.Tier1,    // BTC/ETH-grade
                >= 5_000_000   => LiquidityClass.Tier2,    // major alts
                _              => LiquidityClass.Micro,    // sub-$5M/bar — illiquid
            };

            return new Profile(vol, cycle, regime, liquidity,
                atrPctMedian, hurstMedian, pctAbove, avgDollarVol);
        }

        // NOTE: RecommendV23Long/Short were removed 2026-08-01. Classify() stays — profiling an
        // asset by volatility, cycle, regime and liquidity is a neutral, useful measurement and is
        // the basis of the research lab's character classifier. Mapping that profile to a specific
        // strategy was an OPINION, it was shipped automatically, and every branch of it returned a
        // Cipher-B variant — a component this project's own research falsified. Removed rather than
        // corrected: the terminal should ship tools, not picks.

        // ── Helpers ────────────────────────────────────────────────────────────

        private static double[] ComputeAtr(IReadOnlyList<Ohlcv> bars, int period)
        {
            int n = bars.Count;
            var atr = new double[n];
            for (int i = 0; i < n; i++) atr[i] = double.NaN;
            if (n < period + 1) return atr;
            double trSum = 0;
            for (int i = 1; i <= period; i++)
            {
                double tr = Math.Max(bars[i].High - bars[i].Low,
                            Math.Max(Math.Abs(bars[i].High - bars[i - 1].Close),
                                     Math.Abs(bars[i].Low  - bars[i - 1].Close)));
                trSum += tr;
                if (i == period) atr[i] = trSum / period;
            }
            for (int i = period + 1; i < n; i++)
            {
                double tr = Math.Max(bars[i].High - bars[i].Low,
                            Math.Max(Math.Abs(bars[i].High - bars[i - 1].Close),
                                     Math.Abs(bars[i].Low  - bars[i - 1].Close)));
                atr[i] = (atr[i - 1] * (period - 1) + tr) / period;
            }
            return atr;
        }

        private static double[] ComputeSma(IReadOnlyList<Ohlcv> bars, int period)
        {
            int n = bars.Count;
            var sma = new double[n];
            for (int i = 0; i < n; i++) sma[i] = double.NaN;
            if (n < period) return sma;
            double sum = 0;
            for (int i = 0; i < period; i++) sum += bars[i].Close;
            sma[period - 1] = sum / period;
            for (int i = period; i < n; i++)
            {
                sum += bars[i].Close - bars[i - period].Close;
                sma[i] = sum / period;
            }
            return sma;
        }

        private static double EstimateHurst(double[] ret, int from, int window, int[] scales)
        {
            // Same R/S analysis as HurstExponentProvider — duplicated here to keep
            // the classifier self-contained and dependency-free.
            int sCount = scales.Length;
            double[] logN = new double[sCount];
            double[] logRS = new double[sCount];
            int valid = 0;
            for (int si = 0; si < sCount; si++)
            {
                int s = scales[si];
                int subPeriods = window / s;
                if (subPeriods < 2) continue;
                double rsSum = 0;
                int rsCount = 0;
                for (int p = 0; p < subPeriods; p++)
                {
                    int subFrom = from + p * s;
                    if (subFrom + s > ret.Length) break;
                    double mean = 0;
                    for (int k = 0; k < s; k++) mean += ret[subFrom + k];
                    mean /= s;
                    double cumDev = 0, maxDev = double.MinValue, minDev = double.MaxValue;
                    double sqSum = 0;
                    for (int k = 0; k < s; k++)
                    {
                        double dev = ret[subFrom + k] - mean;
                        cumDev += dev;
                        if (cumDev > maxDev) maxDev = cumDev;
                        if (cumDev < minDev) minDev = cumDev;
                        sqSum += dev * dev;
                    }
                    double range = maxDev - minDev;
                    double std = Math.Sqrt(sqSum / s);
                    if (std > 1e-12 && range > 0)
                    {
                        rsSum += range / std;
                        rsCount++;
                    }
                }
                if (rsCount > 0)
                {
                    logN[valid]  = Math.Log(s);
                    logRS[valid] = Math.Log(rsSum / rsCount);
                    valid++;
                }
            }
            if (valid < 2) return 0.5;
            double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
            for (int i = 0; i < valid; i++)
            {
                sumX  += logN[i];
                sumY  += logRS[i];
                sumXY += logN[i] * logRS[i];
                sumXX += logN[i] * logN[i];
            }
            double denom = valid * sumXX - sumX * sumX;
            if (Math.Abs(denom) < 1e-12) return 0.5;
            double slope = (valid * sumXY - sumX * sumY) / denom;
            return Math.Clamp(slope, 0.0, 1.0);
        }
    }
}
