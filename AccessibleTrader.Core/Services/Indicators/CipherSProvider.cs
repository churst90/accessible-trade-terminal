using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Cipher S — Sentiment Cycle overlay.
    ///
    /// Replaces the default bullish/bearish candle coloring with 11 sentiment phase colors
    /// derived from the normalized price position within a rolling window of the asset's history.
    ///
    ///   Phase 0  Max Fear       — bright green (#00E676)   cheapest in window
    ///   Phase 1  Fear           — cyan (#00E5FF)
    ///   Phase 2  Concern        — teal (#00BCD4)
    ///   Phase 3  Caution        — blue (#2196F3)
    ///   Phase 4  Mild Caution   — purple (#9C27B0)
    ///   Phase 5  Neutral        — pale yellow (#FFF176)
    ///   Phase 6  Mild Greed     — yellow (#FFD54F)
    ///   Phase 7  Greed          — orange (#FF9800)
    ///   Phase 8  High Greed     — orange-red (#FF5722)
    ///   Phase 9  Extreme Greed  — red (#F44336)
    ///   Phase 10 Max Euphoria   — hot pink (#E91E63)        most expensive in window
    ///
    /// Algorithm (three-stage):
    ///
    ///   Stage 1 — Robust channel extremes:
    ///     Sort closes within the window; use the 5th-percentile close as wLow and the
    ///     95th-percentile close as wHigh. This prevents a single flash-crash wick or a
    ///     thin-volume spike from dominating the channel anchor for the next 1500 bars.
    ///     (For windows smaller than ~20 bars the 5/95 indices collapse to absolute min/max.)
    ///
    ///   Stage 2 — Channel position:
    ///     rawPct[i] = (close[i] − wLow) / (wHigh − wLow) × 100
    ///
    ///   Stage 3 — 3-bar EMA smoothing on rawPct before phase mapping:
    ///     smoothPct[i] = 0.5 × rawPct[i] + 0.5 × smoothPct[i−1]   (α = 2/(3+1) = 0.5)
    ///     This removes single-candle phase flicker on compressed charts without
    ///     introducing meaningful lag relative to the 1500-bar window.
    ///     BufKeyPercentile stores smoothPct (used for speech and UpdateLast EMA seeding).
    ///
    /// Adaptive window (IAdaptiveIndicatorProvider):
    ///   When WindowBars = 0 (the "Auto" sentinel), SuggestParameters() runs a cycle-detection
    ///   algorithm on the loaded history:
    ///     1. Find significant local price peaks (±40-bar local maxima, min 100 bars apart).
    ///     2. Find troughs between consecutive peaks that dropped ≥ asset-adaptive threshold%.
    ///     3. Measure median trough-to-trough interval → estimated cycle length.
    ///     4. WindowBars = detected cycle × 1.25 (generous to capture full peak-to-trough).
    ///   Two guards prevent re-detection spam:
    ///     Guard 1 — Data milestone: only re-detects when bar count ≥ 1.5× count at last detection.
    ///     Guard 2 — Significance: only announces when new window differs from last by >15%.
    ///   Threshold calibration is self-adaptive based on the asset's own observed max drawdown:
    ///     maxDrawdown > 60% → 40% drop = bear cycle  (crypto)
    ///     maxDrawdown > 25% → 20% drop               (equities)
    ///     maxDrawdown > 10% → 10% drop               (forex)
    ///     else              →  5% drop               (bonds / low-vol)
    ///
    /// Parameters:
    ///   WindowBars = 0  → Auto-detect cycle (default and recommended).
    ///   WindowBars > 0  → Fixed rolling window (power users).
    ///
    /// RequiresFullRecalcOnTick = false — historical bars are causal (bar i only looks back to i);
    /// only the current live bar needs recomputation on each tick, handled by UpdateLast().
    /// </summary>
    public class CipherSProvider : IIndicatorProvider, IAdaptiveIndicatorProvider
    {
        public string Name => "Cipher S";

        // ── Component / buffer key constants ──────────────────────────────────
        public const string CompCandlePhase  = "Candle Phase";
        public const string BufKeyPercentile = "Candle Phase_percentile";   // stores smoothed pct

        // ── EMA smoothing constant (3-bar EMA: α = 2/(3+1)) ──────────────────
        private const double SmoothAlpha = 0.5;

        // ── Phase labels ──────────────────────────────────────────────────────
        internal static readonly string[] PhaseNames =
        {
            "Max Fear",      // 0
            "Fear",          // 1
            "Concern",       // 2
            "Caution",       // 3
            "Mild Caution",  // 4
            "Neutral",       // 5
            "Mild Greed",    // 6
            "Greed",         // 7
            "High Greed",    // 8
            "Extreme Greed", // 9
            "Max Euphoria",  // 10
        };

        // ── Contextual trading advice per phase ───────────────────────────────
        private static readonly string[] PhaseAdvice =
        {
            "Extreme fear territory. Price near multi-year lows. Historical accumulation zone — strong DCA opportunity.",
            "Fear zone. Price significantly below recent highs. Watch for reversal signals.",
            "Concern territory. Price below average cycle level. Entry zone for patient buyers.",
            "Caution — below median. Reduce risk or wait for clarity.",
            "Mild caution. Slight pessimism. Neutral-to-bearish lean.",
            "Neutral. Price near historical median of current cycle. No strong directional bias.",
            "Mild greed. Slightly elevated price level. Trend following appropriate.",
            "Greed zone. Price elevated within cycle. Consider scaling into profits.",
            "High greed. Significant extension toward cycle top. Tighten stops.",
            "Extreme greed. Near historical distribution territory. Trim positions.",
            "Max Euphoria. Price at or near the highest level in the observable window. Classic distribution zone.",
        };

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "CIPHER_S",
                Name        = "Cipher S",
                Category    = "Overlays",
                DefaultPane = "Main",
                Description =
                    "Cipher S — Sentiment Cycle overlay. Colors every candle with its sentiment phase " +
                    "from Max Fear (bright green) through Max Euphoria (hot pink). Uses a robust " +
                    "high-low channel (5th/95th percentile of closes) so outlier spikes don't distort " +
                    "the channel anchor. Output is smoothed with a 3-bar EMA to eliminate phase flicker " +
                    "on compressed charts. Set WindowBars to 0 (Auto) to auto-detect cycle length.",
                RequiresFullRecalcOnTick = false,

                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() {
                        Name         = "WindowBars",
                        DisplayName  = "Window (bars, 0 = Auto)",
                        DataType     = typeof(int),
                        DefaultValue = 0.0,
                        Description  =
                            "Rolling window for sentiment normalization. " +
                            "0 = Auto-detect cycle length from major lows in loaded history (recommended). " +
                            "Any positive value = fixed window. " +
                            "Manual guidance: daily BTC/stocks → 1500 (≈4 years); " +
                            "daily commodities → 2500–3500; hourly → multiply × 24; 4-hour → multiply × 6."
                    },
                    new() {
                        Name         = "AdaptiveSmoothing",
                        DisplayName  = "Adaptive Smoothing",
                        DataType     = typeof(bool),
                        DefaultValue = false,
                        Description  =
                            "When true, the EMA smoothing constant varies with the local variance of raw pct: " +
                            "lower alpha (slower) during trends (low variance), higher alpha (faster) during chop (high variance). " +
                            "This reduces phase flicker in bursty regimes without over-smoothing in stable trends."
                    },
                },

                Components = new List<IndicatorComponentMetadata>
                {
                    new()
                    {
                        Name             = CompCandlePhase,
                        DisplayName      = "Market Phase",
                        DisplayType      = ComponentDisplayType.CandleColor,
                        Role             = ComponentRole.Signal,
                        IsVisible        = true,
                        DefaultColorHex  = "#FFF176",   // neutral yellow shown in legend swatch
                        DefaultThickness = 3f,
                    },
                },
            }
        };

        // ─────────────────────────────────────────────────────────────────────
        // Calculation — full recalc (data load / window change)
        // ─────────────────────────────────────────────────────────────────────

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int n = data.Length;
            if (n == 0) return;

            int windowBars = ResolveWindow(parameters);

            var phase     = new double[n];
            var smoothPct = new double[n];
            for (int i = 0; i < n; i++) phase[i] = smoothPct[i] = double.NaN;

            // ── Pass 1: Raw channel position per bar ──────────────────────────
            // Robust extremes: sort window closes, clip to 5th/95th percentile so
            // single-bar spikes (flash crashes, thin-volume ATH wicks) don't anchor
            // the channel for the entire subsequent window period.
            var rawPct = new double[n];
            for (int i = 0; i < n; i++) rawPct[i] = double.NaN;

            for (int i = 0; i < n; i++)
            {
                int wStart = Math.Max(0, i - windowBars + 1);
                int wLen   = i - wStart + 1;

                var scratch = new double[wLen];
                for (int j = wStart; j <= i; j++)
                    scratch[j - wStart] = (double)data[j].Close;

                Array.Sort(scratch);

                // For wLen < 20 the percentile indices collapse gracefully toward 0 / wLen-1.
                int    loIdx = (int)(wLen * 0.05);
                int    hiIdx = Math.Min(wLen - 1, (int)(wLen * 0.95));
                double wLow  = scratch[loIdx];
                double wHigh = scratch[hiIdx];

                if (wHigh <= wLow) continue;

                rawPct[i] = ((double)data[i].Close - wLow) / (wHigh - wLow) * 100.0;
            }

            // ── Pass 2: EMA smoothing → phase mapping ─────────────────────────
            // Smoothing eliminates single-candle phase flicker visible on compressed
            // charts. Fixed mode uses a 3-bar EMA (alpha = 0.5). Adaptive mode scales
            // alpha by the local variance of rawPct: flat/trend regions get a slower
            // alpha (0.25-0.4) while chop gets a faster alpha (0.55-0.7). This
            // tracks fast sentiment pivots in bursty markets without introducing
            // flicker in orderly trends.
            bool adaptive = GetBool(parameters, "AdaptiveSmoothing", false);
            const int varWin = 10;
            double prevSmooth = double.NaN;

            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(rawPct[i])) continue;

                double alpha = SmoothAlpha;
                if (adaptive && i >= varWin)
                {
                    // Rolling std of rawPct over varWin. High std = chop → faster alpha.
                    double mean = 0; int cnt = 0;
                    for (int j = i - varWin + 1; j <= i; j++)
                    {
                        if (!double.IsNaN(rawPct[j])) { mean += rawPct[j]; cnt++; }
                    }
                    if (cnt > 2)
                    {
                        mean /= cnt;
                        double ssq = 0;
                        for (int j = i - varWin + 1; j <= i; j++)
                        {
                            if (!double.IsNaN(rawPct[j])) ssq += (rawPct[j] - mean) * (rawPct[j] - mean);
                        }
                        double std = Math.Sqrt(ssq / cnt);
                        // Map std [0..20] → alpha [0.25..0.70].
                        double t = Math.Min(1.0, std / 20.0);
                        alpha = 0.25 + 0.45 * t;
                    }
                }

                smoothPct[i] = double.IsNaN(prevSmooth)
                    ? rawPct[i]
                    : alpha * rawPct[i] + (1.0 - alpha) * prevSmooth;

                prevSmooth = smoothPct[i];
                phase[i]   = PercentileToPhase(smoothPct[i]);
            }

            WriteToBuffer(buffer, CompCandlePhase,  phase,     n);
            WriteToBuffer(buffer, BufKeyPercentile, smoothPct, n);
        }

        // ─────────────────────────────────────────────────────────────────────
        // UpdateLast — live tick: recompute only the current (last) bar
        // ─────────────────────────────────────────────────────────────────────

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int n = data.Length;
            if (n == 0) return;

            int i          = n - 1;
            int windowBars = ResolveWindow(parameters);
            int wStart     = Math.Max(0, i - windowBars + 1);
            int wLen       = i - wStart + 1;

            // Robust extremes — same logic as Calculate().
            var scratch = new double[wLen];
            for (int j = wStart; j <= i; j++)
                scratch[j - wStart] = (double)data[j].Close;
            Array.Sort(scratch, 0, wLen);

            int    loIdx = (int)(wLen * 0.05);
            int    hiIdx = Math.Min(wLen - 1, (int)(wLen * 0.95));
            double wLow  = scratch[loIdx];
            double wHigh = scratch[hiIdx];

            var phaseSpan = buffer.GetComponentSpan(CompCandlePhase);
            var pctSpan   = buffer.GetComponentSpan(BufKeyPercentile);
            if (i >= phaseSpan.Length) return;

            if (wHigh <= wLow)
            {
                phaseSpan[i] = double.NaN;
                pctSpan[i]   = double.NaN;
                return;
            }

            double rawPct = ((double)data[i].Close - wLow) / (wHigh - wLow) * 100.0;

            // Seed EMA from the previous bar's smoothed pct already written to the buffer.
            double prevSmooth = (i > 0 && i - 1 < pctSpan.Length) ? pctSpan[i - 1] : double.NaN;

            double smooth = double.IsNaN(prevSmooth)
                ? rawPct
                : SmoothAlpha * rawPct + (1.0 - SmoothAlpha) * prevSmooth;

            phaseSpan[i] = PercentileToPhase(smooth);
            pctSpan[i]   = smooth;
        }

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 2;

        // ─────────────────────────────────────────────────────────────────────
        // Adaptive parameter detection (IAdaptiveIndicatorProvider)
        // ─────────────────────────────────────────────────────────────────────

        private readonly ConcurrentDictionary<string, (int window, int dataCount)> _detectionCache = new();

        public Dictionary<string, object>? SuggestParameters(
            ReadOnlySpan<Ohlcv> data,
            string instrumentCode,
            Dictionary<string, object> currentParams,
            out string? notificationMessage)
        {
            notificationMessage = null;
            int n = data.Length;

            int currentWindow = GetInt(currentParams, "WindowBars", 0);
            if (currentWindow != 0) return null;

            string key = string.IsNullOrEmpty(instrumentCode) ? "__default" : instrumentCode;

            // Guard 1: only re-detect when bar count is >= 1.5x the count at last detection.
            if (_detectionCache.TryGetValue(key, out var cached))
            {
                if (n < (int)(cached.dataCount * 1.5))
                    return null;
            }

            int detected = DetectCycleWindow(data);

            if (detected <= 0)
            {
                _detectionCache[key] = (1500, n);
                return null;
            }

            int suggested = Math.Clamp((int)(detected * 1.25), 200, 5000);

            // Guard 2: only announce when change exceeds 15%.
            int lastDetected = _detectionCache.TryGetValue(key, out var prev) ? prev.window : 0;
            bool significant = lastDetected == 0 ||
                Math.Abs(suggested - lastDetected) / (double)lastDetected > 0.15;

            _detectionCache[key] = (suggested, n);

            if (significant)
            {
                int troughCount = CountTroughs(data, out int medianInterval);
                notificationMessage =
                    $"Cipher S. Cycle auto-detected. " +
                    $"{troughCount} major lows found, averaging {medianInterval} bars apart. " +
                    $"Window set to {suggested} bars.";
            }

            return new Dictionary<string, object> { ["WindowBars"] = (double)suggested };
        }

        // ─────────────────────────────────────────────────────────────────────
        // Cycle detection
        // ─────────────────────────────────────────────────────────────────────

        private static int DetectCycleWindow(ReadOnlySpan<Ohlcv> data)
        {
            CountTroughs(data, out int median);
            return median > 0 ? median : -1;
        }

        private static int CountTroughs(ReadOnlySpan<Ohlcv> data, out int medianInterval)
        {
            medianInterval = 0;
            int n = data.Length;
            if (n < 200) return 0;

            double ath = 0, atl = double.MaxValue;
            for (int i = 0; i < n; i++)
            {
                double c = (double)data[i].Close;
                if (c > ath) ath = c;
                if (c < atl) atl = c;
            }
            if (ath == 0 || atl >= ath) return 0;

            double maxDrawdown = (ath - atl) / ath;
            double dropThreshold = maxDrawdown switch
            {
                > 0.60 => 0.40,
                > 0.25 => 0.20,
                > 0.10 => 0.10,
                _      => 0.05,
            };

            const int halfWindow     = 40;
            const int minPeakSpacing = 100;

            var peaks = new List<(int idx, double price)>();

            for (int i = halfWindow; i < n - halfWindow; i++)
            {
                double c = (double)data[i].Close;
                bool isPeak = true;
                for (int j = i - halfWindow; j <= i + halfWindow; j++)
                {
                    if (j != i && (double)data[j].Close >= c) { isPeak = false; break; }
                }
                if (!isPeak) continue;
                if (peaks.Count > 0 && i - peaks[^1].idx < minPeakSpacing) continue;
                peaks.Add((i, c));
            }

            if (peaks.Count < 2) return 0;

            var troughs = new List<int>();

            for (int p = 0; p < peaks.Count - 1; p++)
            {
                int    start      = peaks[p].idx;
                int    end        = peaks[p + 1].idx;
                double peakPrice  = peaks[p].price;
                double floorPrice = peakPrice * (1.0 - dropThreshold);

                double lowestClose = double.MaxValue;
                int    lowestIdx   = -1;

                for (int i = start + 1; i < end; i++)
                {
                    double c = (double)data[i].Close;
                    if (c < lowestClose) { lowestClose = c; lowestIdx = i; }
                }

                if (lowestIdx >= 0 && lowestClose <= floorPrice)
                    troughs.Add(lowestIdx);
            }

            if (troughs.Count < 2) return 0;

            var intervals = new List<int>(troughs.Count - 1);
            for (int i = 1; i < troughs.Count; i++)
                intervals.Add(troughs[i] - troughs[i - 1]);

            intervals.Sort();
            int mid = intervals.Count / 2;
            medianInterval = intervals.Count % 2 == 1
                ? intervals[mid]
                : (intervals[mid - 1] + intervals[mid]) / 2;

            return troughs.Count;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Speech
        // ─────────────────────────────────────────────────────────────────────

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            if (index < 0 || data.IsEmpty) return "Cipher S — no data.";

            int windowBars = ResolveWindow(parameters);

            double phaseVal = GetVal(calculatedResults, CompCandlePhase,  index);
            double pct      = GetVal(calculatedResults, BufKeyPercentile, index);

            if (double.IsNaN(phaseVal))
                return "Cipher S — warming up. Needs at least 2 bars of data.";

            int    phase     = Math.Clamp((int)Math.Round(phaseVal), 0, PhaseNames.Length - 1);
            string phaseName = PhaseNames[phase];
            string advice    = PhaseAdvice[phase];

            var sb = new StringBuilder();
            sb.Append($"Cipher S. {phaseName}. ");

            if (!double.IsNaN(pct))
                sb.Append($"Channel position: {pct:F0}% of window range. ");

            string windowNote = GetInt(parameters, "WindowBars", 0) == 0
                ? $"Auto-calibrated window ({windowBars} bars). "
                : $"Ranked against last {windowBars} bars. ";
            sb.Append(windowNote);

            sb.Append(advice);
            return sb.ToString();
        }

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (componentName != CompCandlePhase) return null;
            if (double.IsNaN(value)) return "Market Phase — warming up.";

            int    phase     = Math.Clamp((int)Math.Round(value), 0, PhaseNames.Length - 1);
            string phaseName = PhaseNames[phase];
            string advice    = PhaseAdvice[phase];

            return $"{phaseName}. {advice}";
        }

        public List<LevelDescriptor> GetDefaultLevels(string code) => new();

        // ─────────────────────────────────────────────────────────────────────
        // Phase mapping
        // ─────────────────────────────────────────────────────────────────────

        private static int PercentileToPhase(double pct) => pct switch
        {
            >= 97 => 10,   // Max Euphoria   — top 3%
            >= 93 => 9,    // Extreme Greed  — top 7%
            >= 85 => 8,    // High Greed     — top 15%
            >= 72 => 7,    // Greed          — top 28%
            >= 58 => 6,    // Mild Greed     — top 42%
            >= 42 => 5,    // Neutral        — middle 16%
            >= 28 => 4,    // Mild Caution   — bottom 42%
            >= 15 => 3,    // Caution        — bottom 28%
            >= 7  => 2,    // Concern        — bottom 15%
            >= 3  => 1,    // Fear           — bottom 7%
            _     => 0,    // Max Fear       — bottom 3%
        };

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the effective window size from parameters, applying the 1500-bar
        /// fallback when WindowBars = 0 (Auto) and detection hasn't run yet.
        /// </summary>
        private static int ResolveWindow(Dictionary<string, object> parameters)
        {
            int w = Math.Max(0, GetInt(parameters, "WindowBars", 0));
            return w == 0 ? 1500 : w;
        }

        private static void WriteToBuffer(IIndicatorResultBuffer buffer, string name, double[] data, int n)
        {
            var span = buffer.GetComponentSpan(name);
            int len  = Math.Min(span.Length, n);
            for (int i = 0; i < len; i++) span[i] = data[i];
        }

        private static int GetInt(Dictionary<string, object> p, string key, int def)
        {
            if (p.TryGetValue(key, out var v))
            {
                if (v is double d) return (int)d;
                if (v is int i)    return i;
                if (int.TryParse(v?.ToString(), out int parsed)) return parsed;
            }
            return def;
        }

        private static bool GetBool(Dictionary<string, object> p, string key, bool def)
        {
            if (!p.TryGetValue(key, out var v)) return def;
            if (v is bool b)   return b;
            if (v is string s) return bool.TryParse(s, out var r) ? r : def;
            return def;
        }

        private static double GetVal(IReadOnlyDictionary<string, double[]> d, string key, int index)
        {
            if (!d.TryGetValue(key, out var arr)) return double.NaN;
            if (index < 0 || index >= arr.Length) return double.NaN;
            return arr[index];
        }
    }
}
