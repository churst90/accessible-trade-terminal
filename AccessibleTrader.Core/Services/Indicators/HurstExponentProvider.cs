using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Hurst exponent — rescaled-range (R/S) regime classifier. Estimates
    /// whether the recent log-return series is trending (H > 0.5),
    /// mean-reverting (H < 0.5), or random walk (H ≈ 0.5). Computed over a
    /// rolling window using R/S analysis decomposed into multiple sub-period
    /// scales, then log-log regression of (avg R/S) on (sub-period length).
    /// The slope IS the Hurst exponent.
    ///
    /// Why this matters for the v23 family: capitulation/distribution
    /// reversal strategies SHOULD work in mean-reverting regimes (H &lt; 0.5)
    /// and underperform in trending regimes (H > 0.5) where reversals get
    /// run over by trend continuation. A Hurst gate could materially lift
    /// the precision of v23 and similar reversal cells.
    ///
    /// Components:
    ///   Hurst   — rolling H value, range ~0.0 to 1.0 (clamped on output)
    ///   Regime  — discrete classification: -1 (mean-reverting) / 0 (random) / +1 (trending)
    ///
    /// Default window 100 bars produces a stable estimate without smearing
    /// regime transitions. 50 reacts faster but gets noisier on volatile
    /// assets; 200 is too smooth for tactical use.
    /// </summary>
    public sealed class HurstExponentProvider : IIndicatorProvider
    {
        public string Name => "Hurst Exponent";

        public const string Code       = "HURST";
        public const string CompHurst  = "Hurst";
        public const string CompRegime = "Hurst Regime";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = Code,
                Causality = ComponentCausality.Causal,
                Name        = "Hurst Exponent",
                Category    = "Regime",
                DefaultPane = "Pane_HURST",
                Description =
                    "Rescaled-range Hurst exponent over a rolling window. H < 0.5 = mean-" +
                    "reverting (where reversal strategies like v23 should outperform); " +
                    "H > 0.5 = trending (where breakouts and trend-following should work). " +
                    "Use as a regime gate to filter strategies to their natural environment.",
                RequiresFullRecalcOnTick = false,

                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompHurst, DisplayName = "Hurst",
                            DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#9C27B0", DefaultThickness = 1.5f,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultReferenceLevel = 0.5,
                            DefaultPitchMapping = PitchMapping.Value,
                            SpeechTemplate = "Hurst {value:F2}.",
                            IsVisible = true },
                    new() { Name = CompRegime, DisplayName = "Hurst Regime",
                            DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#FFB300", DefaultThickness = 1.0f,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultReferenceLevel = 0.0,
                            SpeechTemplate = "Regime {value:F0}.",
                            IsVisible = false },
                },

                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "Window", DisplayName = "Window (bars)",
                            DataType = typeof(int), DefaultValue = 100.0,
                            Description = "Rolling window for Hurst estimation. 100 is a good default; " +
                                          "50 reacts faster but noisier; 200 too smooth." },
                    new() { Name = "TrendBand", DisplayName = "Trend Band (±)",
                            DataType = typeof(double), DefaultValue = 0.05,
                            Description = "Hurst values within ±this of 0.5 are classified as random walk. " +
                                          "Values above 0.5+band = trending (+1), below 0.5-band = mean-reverting (-1)." }
                }
            }
        };

        public List<LevelDescriptor> GetDefaultLevels(string code)
        {
            if (!code.Equals(Code, StringComparison.OrdinalIgnoreCase)) return new();
            return new()
            {
                new("Mean-Revert", 0.45, "#26A69A", DashStyle.Dash, PlayEarcon: false),
                new("Random",      0.5,  "#666666", DashStyle.Dot,  PlayEarcon: false),
                new("Trend",       0.55, "#FF7043", DashStyle.Dash, PlayEarcon: false),
            };
        }

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int window    = Math.Max(40, GetInt(parameters, "Window", 100));
            double band   = Math.Max(0.0, GetDbl(parameters, "TrendBand", 0.05));

            int n = data.Length;
            var hSpan = buffer.GetComponentSpan(CompHurst);
            var rSpan = buffer.GetComponentSpan(CompRegime);

            for (int i = 0; i < n; i++)
            {
                if (i < hSpan.Length) hSpan[i] = double.NaN;
                if (i < rSpan.Length) rSpan[i] = double.NaN;
            }
            if (n < window + 1) return;

            // Pre-compute log returns.
            var ret = new double[n];
            ret[0] = 0;
            for (int i = 1; i < n; i++)
            {
                if (data[i - 1].Close > 0 && data[i].Close > 0)
                    ret[i] = Math.Log(data[i].Close / data[i - 1].Close);
                else
                    ret[i] = 0;
            }

            // Sub-period sizes for R/S analysis. Pick scales that fit cleanly into
            // the window. 4 scales is a good speed/accuracy tradeoff.
            int[] scales = ComputeScales(window);

            for (int i = window; i < n; i++)
            {
                int from = i - window + 1;
                double h = EstimateHurst(ret, from, window, scales);
                if (i < hSpan.Length) hSpan[i] = h;
                if (i < rSpan.Length)
                {
                    rSpan[i] = h > 0.5 + band ?  1.0
                             : h < 0.5 - band ? -1.0
                             :                   0.0;
                }
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer) =>
            Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) =>
            GetInt(parameters, "Window", 100) + 5;

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            if (componentName == CompHurst)
            {
                string interp = value > 0.55 ? "trending"
                              : value < 0.45 ? "mean-reverting"
                              :                "random walk";
                return $"Hurst {value:F2}, {interp}.";
            }
            if (componentName == CompRegime)
            {
                string label = value > 0.5 ? "trending" : value < -0.5 ? "mean-reverting" : "random";
                return $"Regime {label}.";
            }
            return null;
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            double h = TryGet(calculatedResults, CompHurst, index);
            return $"Hurst exponent: {(double.IsNaN(h) ? "n/a" : h.ToString("F2"))}.";
        }

        private static int[] ComputeScales(int window)
        {
            // Build a small geometric series of sub-period sizes. Each scale must
            // divide the window cleanly enough that we get ≥2 sub-periods at it.
            // For window=100: use {10, 20, 25, 50}. For 50: {10, 25}.
            var list = new List<int>();
            for (int s = 10; s <= window / 2; s = (int)Math.Round(s * 1.6))
                if (window / s >= 2) list.Add(s);
            if (list.Count < 2) list = new List<int> { Math.Max(5, window / 4), Math.Max(10, window / 2) };
            return list.ToArray();
        }

        private static double EstimateHurst(double[] ret, int from, int window, int[] scales)
        {
            // R/S analysis at each scale, then log-log regression.
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

            // Linear regression slope = Hurst exponent.
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

        private static double TryGet(IReadOnlyDictionary<string, double[]> dict, string key, int idx)
        {
            if (!dict.TryGetValue(key, out var arr) || arr == null
                || idx < 0 || idx >= arr.Length) return double.NaN;
            return arr[idx];
        }

        private static double GetDbl(Dictionary<string, object> p, string k, double def) =>
            p != null && p.TryGetValue(k, out var v) ? Convert.ToDouble(v) : def;
        private static int GetInt(Dictionary<string, object> p, string k, int def) =>
            p != null && p.TryGetValue(k, out var v) ? (int)Convert.ToDouble(v) : def;
    }
}
