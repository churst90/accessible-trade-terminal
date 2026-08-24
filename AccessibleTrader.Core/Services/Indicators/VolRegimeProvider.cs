using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Volatility Regime — exposes the fast/slow realized-volatility RATIO as a
    /// leaf-comparable signal. Built 2026-06-12 for the era-robustness research
    /// programme (see strategy-volatility-plan): every existing regime gate in the
    /// suite (Faber 200MA, Hurst, funding, MVRV) conditions on trend or sentiment;
    /// none conditions on the volatility CYCLE, and none is immune to the secular
    /// volatility decay that makes 2017-calibrated thresholds meaningless in 2026.
    ///
    /// The ratio is the trick: realized vol of the last <c>ShortWindow</c> bars over
    /// realized vol of the last <c>LongWindow</c> bars. Both legs decay together as
    /// an asset matures, so the ratio is approximately stationary across eras BY
    /// CONSTRUCTION — a reading of 0.7 meant "compressed vs. its own recent history"
    /// in 2017 and means the same thing today, even though absolute vol halved.
    ///
    /// Components:
    ///   • VolRatio       — shortVol / longVol. &gt;1.25 expansion, &lt;0.75 compression
    ///                      (classic coil before expansion; mean-reversion friendly).
    ///   • VolPercentile  — percentile rank (0..1) of current short vol within the
    ///                      trailing <c>RankWindow</c> distribution of itself. Rank
    ///                      form of the same idea; pairs with PercentileAbove/Below.
    ///   • VolState       — ternary: +1 expansion, −1 compression, 0 neutral/warmup.
    ///
    /// Strategy leaves: <c>VOL_REGIME.VolState LessThan 0</c> = "only fire reversals
    /// in compression", <c>VOL_REGIME.VolRatio GreaterThan 1.25</c> = "only fire
    /// breakouts in expansion".
    /// </summary>
    public sealed class VolRegimeProvider : IIndicatorProvider
    {
        public const string Code = "VOL_REGIME";
        public const string CompVolRatio = "VolRatio";
        public const string CompVolPercentile = "VolPercentile";
        public const string CompVolState = "VolState";

        public string Name => "Volatility Regime";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code,
                Causality = ComponentCausality.Causal,
                Name = "Volatility Regime (fast/slow ratio)",
                Category = "Volatility",
                DefaultPane = "Pane_VOL_REGIME",
                Description = "Fast/slow realized-volatility ratio. >1.25 = expansion regime " +
                              "(breakouts work), <0.75 = compression (mean-reversion works, coil " +
                              "before expansion). Ratio form is stationary across eras even as " +
                              "absolute volatility decays with asset maturation.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "ShortWindow", DisplayName = "Short Window", DefaultValue = 30.0,
                            DataType = typeof(int), MinValue = 5, MaxValue = 200, Step = 5,
                            Description = "Bars for the fast realized-vol leg." },
                    new() { Name = "LongWindow", DisplayName = "Long Window", DefaultValue = 365.0,
                            DataType = typeof(int), MinValue = 50, MaxValue = 2000, Step = 5,
                            Description = "Bars for the slow realized-vol leg (the era baseline)." },
                    new() { Name = "RankWindow", DisplayName = "Rank Window", DefaultValue = 500.0,
                            DataType = typeof(int), MinValue = 100, MaxValue = 2000, Step = 50,
                            Description = "Trailing window for the VolPercentile rank." },
                    new() { Name = "ExpandThreshold", DisplayName = "Expansion Threshold", DefaultValue = 1.25,
                            DataType = typeof(double), MinValue = 1.0, MaxValue = 3.0, Step = 0.05 },
                    new() { Name = "CompressThreshold", DisplayName = "Compression Threshold", DefaultValue = 0.75,
                            DataType = typeof(double), MinValue = 0.2, MaxValue = 1.0, Step = 0.05 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new()
                    {
                        Name = CompVolRatio, DisplayName = "Vol Ratio",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = true,
                        DefaultColorHex = "#FF9800", DefaultThickness = 1.5f,
                        DefaultReferenceLevel = 1.0,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Vol ratio {value:F2}.",
                    },
                    new()
                    {
                        Name = CompVolPercentile, DisplayName = "Vol Percentile",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Vol percentile {value:F2}.",
                    },
                    new()
                    {
                        Name = CompVolState, DisplayName = "Vol State",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = false,
                        DefaultReferenceLevel = 0.0,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Vol state {value:F0}.",
                    },
                }
            }
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int n = data.Length;
            if (n == 0) return;
            int shortW = GetInt(parameters, "ShortWindow", 30);
            int longW  = GetInt(parameters, "LongWindow", 365);
            int rankW  = GetInt(parameters, "RankWindow", 500);
            double expandTh   = GetDbl(parameters, "ExpandThreshold", 1.25);
            double compressTh = GetDbl(parameters, "CompressThreshold", 0.75);
            if (shortW < 2) shortW = 2;
            if (longW <= shortW) longW = shortW * 2;

            var ratio = buffer.GetComponentSpan(CompVolRatio);
            var pct   = buffer.GetComponentSpan(CompVolPercentile);
            var state = buffer.GetComponentSpan(CompVolState);

            // Log returns. r[0] is NaN by convention (no prior bar).
            var r = new double[n];
            r[0] = double.NaN;
            for (int i = 1; i < n; i++)
            {
                double prev = data[i - 1].Close, cur = data[i].Close;
                r[i] = prev > 0 && cur > 0 ? Math.Log(cur / prev) : double.NaN;
            }

            // Rolling std-dev of returns over both windows via running sums, O(n).
            // NaN returns inside a window poison it honestly (output NaN) — same
            // convention as the rest of the indicator suite.
            var shortVol = RollingStd(r, shortW);
            var longVol  = RollingStd(r, longW);

            for (int i = 0; i < n; i++)
            {
                double sv = shortVol[i], lv = longVol[i];
                if (double.IsNaN(sv) || double.IsNaN(lv) || lv <= 0)
                {
                    ratio[i] = double.NaN;
                    state[i] = 0.0;
                }
                else
                {
                    double q = sv / lv;
                    ratio[i] = q;
                    state[i] = q >= expandTh ? 1.0 : (q <= compressTh ? -1.0 : 0.0);
                }
            }

            // Percentile rank of current short vol within its own trailing rankW window.
            // O(n·rankW) worst case — fine for research-sized series (4000 bars × 500).
            for (int i = 0; i < n; i++)
            {
                double sv = shortVol[i];
                if (double.IsNaN(sv)) { pct[i] = double.NaN; continue; }
                int lo = Math.Max(0, i - rankW + 1);
                int count = 0, total = 0;
                for (int j = lo; j <= i; j++)
                {
                    double v = shortVol[j];
                    if (double.IsNaN(v)) continue;
                    total++;
                    if (v <= sv) count++;
                }
                pct[i] = total >= 20 ? (double)count / total : double.NaN;
            }
        }

        private static double[] RollingStd(double[] values, int window)
        {
            int n = values.Length;
            var outArr = new double[n];
            double sum = 0, sumSq = 0;
            int valid = 0;
            for (int i = 0; i < n; i++)
            {
                double add = values[i];
                if (!double.IsNaN(add)) { sum += add; sumSq += add * add; valid++; }
                int drop = i - window;
                if (drop >= 0)
                {
                    double d = values[drop];
                    if (!double.IsNaN(d)) { sum -= d; sumSq -= d * d; valid--; }
                }
                int span = Math.Min(i + 1, window);
                // Require the window to be fully populated with valid returns —
                // partial windows under-estimate vol and would skew the ratio.
                if (span < window || valid < window)
                {
                    outArr[i] = double.NaN;
                    continue;
                }
                double mean = sum / valid;
                double variance = Math.Max(0, sumSq / valid - mean * mean);
                outArr[i] = Math.Sqrt(variance);
            }
            return outArr;
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
            => GetInt(parameters, "LongWindow", 365) + 1;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters)
        {
            if (index < 0 || index >= data.Length) return string.Empty;
            if (!calculatedResults.TryGetValue(CompVolRatio, out var arr) || index >= arr.Length) return string.Empty;
            double q = arr[index];
            if (double.IsNaN(q)) return "Vol regime: warming up.";
            string label = q >= 1.25 ? "expansion" : (q <= 0.75 ? "compression" : "neutral");
            return $"Vol regime: {label} (fast/slow ratio {q:F2}).";
        }

        public string GetSpeechFact(string code, string componentName, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters) => "";

        private static int GetInt(Dictionary<string, object> p, string key, int dflt)
            => p.TryGetValue(key, out var v) ? Convert.ToInt32(v) : dflt;

        private static double GetDbl(Dictionary<string, object> p, string key, double dflt)
            => p.TryGetValue(key, out var v) ? Convert.ToDouble(v) : dflt;
    }
}
