using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Top/Bottom Detector — first indicator built on the explicit asymmetry
    /// "bottoms are events, tops are processes."
    ///
    /// Bottoms are scored as a single-bar capitulation event (volume spike +
    /// range expansion + lower-wick rejection + below-band exhaustion +
    /// oversold RSI + momentum flip), gated to only fire when price is in
    /// the bottom 20% of the trailing window.
    ///
    /// Tops are scored as a multi-bar distribution accumulator with
    /// exponential decay. Evidence streams (bearish divergence on confirmed
    /// pivots, volume drying up at the swing high, volatility compression
    /// at price highs, upthrusts above prior pivots, sideways regime near
    /// highs) accumulate over time; "Top Confirmed" fires when accumulated
    /// confidence crosses a threshold WHILE price is in the top 20% of
    /// the window.
    ///
    /// All math is z-score / percentile / ATR-relative — no fixed price
    /// or time thresholds. The same parameters work on 1h, 4h, and 1d
    /// charts; lower timeframes are noisier per Gemini's argument but
    /// the same shape of signal is detected.
    /// </summary>
    public sealed class TopBottomDetectorProvider : IIndicatorProvider
    {
        public string Name => "Top/Bottom Detector";

        public const string Code             = "TOP_BOTTOM_DETECTOR";
        public const string CompCapitulation = "Capitulation Confidence";
        public const string CompDistribution = "Distribution Confidence";
        public const string CompBottom       = "Bottom Confirmed";
        public const string CompTop          = "Top Confirmed";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = Code,
                Causality = ComponentCausality.Causal,
                Name        = "Top/Bottom Detector",
                Category    = "Reversal",
                DefaultPane = "Pane_TBD",
                Description =
                    "Asymmetric reversal detector. Bottoms are detected as single-bar " +
                    "capitulation events (volume spike + lower-wick rejection + below-band " +
                    "exhaustion + oversold RSI + momentum flip). Tops are detected as a " +
                    "multi-bar distribution accumulator with exponential decay (bearish " +
                    "divergence + volume drying up + volatility compression + upthrusts + " +
                    "sideways-at-highs). All math is z-score / percentile / ATR-relative " +
                    "so the same parameters generalise across 1h/4h/1d timeframes.",
                RequiresFullRecalcOnTick = false,

                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompCapitulation,
                            DisplayName = "Capitulation Confidence",
                            DisplayType = ComponentDisplayType.Oscillator,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#26A69A",
                            DefaultThickness = 1.5f,
                            DefaultWaveform = "sine",
                            DefaultReferenceLevel = 0.0,
                            DefaultEnvelopeType = "Sustain",
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultNoiseAmount = 0.05f,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultIsAreaFill = false,
                            SpeechTemplate = "Capitulation {value:F2}.",
                            IsVisible = true },

                    new() { Name = CompDistribution,
                            DisplayName = "Distribution Confidence",
                            DisplayType = ComponentDisplayType.Oscillator,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#EF5350",
                            DefaultThickness = 1.5f,
                            DefaultWaveform = "sine",
                            DefaultReferenceLevel = 0.0,
                            DefaultEnvelopeType = "Sustain",
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultNoiseAmount = 0.05f,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultIsAreaFill = false,
                            SpeechTemplate = "Distribution {value:F2}.",
                            IsVisible = true },

                    new() { Name = CompBottom,
                            DisplayName = "Bottom Confirmed",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#00E676",
                            DefaultThickness = 7.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "dual_tone_bell",
                            DefaultDecayMs = 500,
                            DefaultBaseFrequency = 220.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Bottom confirmed",
                            IsVisible = true },

                    new() { Name = CompTop,
                            DisplayName = "Top Confirmed",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF1744",
                            DefaultThickness = 7.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "dual_tone_bell",
                            DefaultDecayMs = 500,
                            DefaultBaseFrequency = 540.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Top confirmed",
                            IsVisible = true },
                },

                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "LookbackWindow", DisplayName = "Lookback Window (bars)",
                            DataType = typeof(int), DefaultValue = 100.0,
                            Description = "Window for z-score / percentile / range computations." },
                    new() { Name = "PivotLookback", DisplayName = "Pivot Lookback (bars)",
                            DataType = typeof(int), DefaultValue = 5.0,
                            Description = "Bars on either side required for swing-high / swing-low confirmation." },
                    new() { Name = "DistributionDecay", DisplayName = "Distribution Half-Life (bars)",
                            DataType = typeof(int), DefaultValue = 30.0,
                            Description = "Exponential half-life for the distribution accumulator. Smaller = " +
                                          "faster forgetting; larger = more inertia." },
                    new() { Name = "ConfirmThreshold", DisplayName = "Confirm Threshold",
                            DataType = typeof(double), DefaultValue = 0.6,
                            Description = "Confidence above which Top/Bottom Confirmed markers fire (0..1)." },
                    new() { Name = "AtrPeriod", DisplayName = "ATR Period",
                            DataType = typeof(int), DefaultValue = 14.0,
                            Description = "ATR Wilder lookback for volatility-relative measurements." },
                    new() { Name = "RsiPeriod", DisplayName = "RSI Period",
                            DataType = typeof(int), DefaultValue = 14.0,
                            Description = "RSI lookback for divergence detection." },
                    new() { Name = "TimeframeAdaptive", DisplayName = "Timeframe Adaptive",
                            DataType = typeof(bool), DefaultValue = 0.0,
                            Description = "When 1, auto-detect bar interval and scale Lookback/Decay/Threshold so " +
                                          "the same calendar gate applies across timeframes (reference: 4h). " +
                                          "Higher TFs relax the meaningful-range gate and slightly lower the " +
                                          "confirm threshold because each bar carries more weight." }
                }
            }
        };

        public List<LevelDescriptor> GetDefaultLevels(string code)
        {
            if (!code.Equals(Code, StringComparison.OrdinalIgnoreCase)) return new();
            return new()
            {
                new("Confirm", 0.6, "#FFB300", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.7f),
                new("Watch",   0.4, "#666666", DashStyle.Dot,  PlayEarcon: false),
            };
        }

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int lookback     = Math.Max(20, GetInt(parameters, "LookbackWindow", 100));
            int pivotLb      = Math.Max(2,  GetInt(parameters, "PivotLookback", 5));
            int distHalfLife = Math.Max(2,  GetInt(parameters, "DistributionDecay", 30));
            double confirm   = Math.Clamp(GetDbl(parameters, "ConfirmThreshold", 0.6), 0.05, 0.95);
            int atrPeriod    = Math.Max(2,  GetInt(parameters, "AtrPeriod", 14));
            int rsiPeriod    = Math.Max(2,  GetInt(parameters, "RsiPeriod", 14));
            bool tfAdaptive  = GetInt(parameters, "TimeframeAdaptive", 0) != 0;

            // Meaningful-range gate (winRange / atr ≥ this) before atTop/atBottom qualify.
            // Default 5.0 was tuned for 4h. Higher TFs need a looser gate because the
            // trailing window covers a longer real-time span — a clean range-bound 1w
            // chart easily spans ≥3 ATR but rarely 5.
            double meaningfulRangeAtr = 5.0;
            // 20-bar consolidation gate (winHi20-winLo20)/atr < this contributes evidence.
            double consolidationAtr = 6.0;
            // Capitulation score-component thresholds. Tuned for 4h; on weekly+ they
            // need to come down because the bar AGGREGATES the intra-bar event spike
            // (a 6× volume hour gets averaged to ~1.2× weekly volume). Without
            // adapting these the indicator produces zero capitulation hits on weekly.
            double capVolZThresh = 1.5;
            double capRngZThresh = 1.0;
            double capRsiThresh  = 30.0;

            if (tfAdaptive)
            {
                double barMinutes = DetectBarIntervalMinutes(data);
                // Empirical research (2026-04-27): default 100/30/0.6 produces +0.654R
                // on BTC 1d (best result of the v22 investigation) and +0.220R on 4h.
                // Both are positive at default — so adaptation is a NO-OP for TFs ≤ 1d.
                // Only weekly+ needs intervention because the 5-ATR meaningful-range gate
                // and 100-bar lookback combine to produce zero fires on small weekly
                // snapshots (BTC 1w has only ~750 bars total over 14 years).
                if (barMinutes >= 5 * 1440)
                {
                    // sqrt-scaled lookback so the gate calmly relaxes rather than
                    // collapsing. 1w (10080 min): scale=sqrt(7)=2.65, lookback=38,
                    // distHalfLife=11. 1mo (43200): scale=5.5, lookback=18→clamped to 30,
                    // distHalfLife=5.
                    double scale = Math.Sqrt(barMinutes / 1440.0);
                    lookback     = Math.Clamp((int)Math.Round(lookback / scale), 30, 500);
                    distHalfLife = Math.Clamp((int)Math.Round(distHalfLife / scale), 5, 200);

                    // Each weekly bar already carries ~7× the weight of a daily bar.
                    // The 5-ATR meaningful-range gate (tuned for 4h) excludes most
                    // weekly windows because weekly ATR scales faster than weekly range.
                    // Lower the bar so a clean weekly cup-and-handle can qualify.
                    meaningfulRangeAtr = barMinutes >= 7 * 1440 ? 2.5 : 3.5;
                    consolidationAtr   = barMinutes >= 7 * 1440 ? 3.0 : 4.5;
                    confirm            = Math.Clamp(confirm - 0.10, 0.30, 0.95);
                    // Score-component relaxation for weekly+: the intra-bar event
                    // spike is partially averaged out by aggregation, so we need to
                    // accept a smaller-but-still-meaningful spike on the bar that
                    // contains the event.
                    capVolZThresh = barMinutes >= 7 * 1440 ? 0.8 : 1.0;
                    capRngZThresh = barMinutes >= 7 * 1440 ? 0.5 : 0.7;
                    capRsiThresh  = barMinutes >= 7 * 1440 ? 40.0 : 35.0;
                }
            }

            int n = data.Length;
            var capSpan  = buffer.GetComponentSpan(CompCapitulation);
            var distSpan = buffer.GetComponentSpan(CompDistribution);
            var botSpan  = buffer.GetComponentSpan(CompBottom);
            var topSpan  = buffer.GetComponentSpan(CompTop);

            for (int i = 0; i < n; i++)
            {
                if (i < capSpan.Length)  capSpan[i]  = double.NaN;
                if (i < distSpan.Length) distSpan[i] = double.NaN;
                if (i < botSpan.Length)  botSpan[i]  = double.NaN;
                if (i < topSpan.Length)  topSpan[i]  = double.NaN;
            }

            int minBars = Math.Max(lookback + pivotLb + 5, atrPeriod + rsiPeriod + 5);
            if (n < minBars) return;

            double[] volume = new double[n];
            for (int i = 0; i < n; i++) volume[i] = data[i].Volume;

            double[] atr = ComputeAtrWilder(data, atrPeriod);
            double[] rsi = ComputeRsiWilder(data, rsiPeriod);
            double[] volZ = ComputeRollingZ(volume, lookback);

            double[] rangePct = new double[n];
            for (int i = 0; i < n; i++)
            {
                double r = data[i].High - data[i].Low;
                double mid = (data[i].High + data[i].Low) * 0.5;
                rangePct[i] = mid > 0 ? r / mid : 0.0;
            }
            double[] rngZ = ComputeRollingZ(rangePct, lookback);

            double[] bbLower = ComputeBollingerLower(data, 20, 2.0);
            double[] bbUpper = ComputeBollingerUpper(data, 20, 2.0);

            double decayFactor = Math.Pow(0.5, 1.0 / distHalfLife);
            // Scale chosen so a textbook distribution scenario (sustained sideways
            // at highs + ATR compression + one upthrust + one bearish divergence)
            // saturates around confidence 0.7-0.8 via 1 - exp(-acc/scale).
            const double distScale = 3.0;
            double dist = 0.0;

            int lastSwingHighIdx = -1, prevSwingHighIdx = -1;
            int lastSwingLowIdx  = -1, prevSwingLowIdx  = -1;

            for (int i = 0; i < n; i++)
            {
                bool warmedUp = i >= lookback
                                && !double.IsNaN(atr[i]) && atr[i] > 0
                                && !double.IsNaN(rsi[i]);

                double winHigh = 0, winLow = 0, winRange = 0;
                bool atTop = false, atBottom = false;
                bool meaningfulRange = false;
                if (i >= lookback)
                {
                    winHigh = double.MinValue; winLow = double.MaxValue;
                    for (int j = i - lookback + 1; j <= i; j++)
                    {
                        if (data[j].High > winHigh) winHigh = data[j].High;
                        if (data[j].Low  < winLow ) winLow  = data[j].Low;
                    }
                    winRange = winHigh - winLow;
                    // Require the trailing window to have spanned at least 5 ATR before
                    // any "at top / at bottom" gate qualifies. In flat oscillation the
                    // range-to-ATR ratio is ~2-3; in a real rally it's >10. Without this
                    // gate, sideways noise pattern-matches as distribution.
                    meaningfulRange = winRange > 0 && atr[i] > 0 && winRange / atr[i] >= meaningfulRangeAtr;
                    if (meaningfulRange)
                    {
                        // Use bar extremes, not close, so that a long-wick capitulation
                        // bar (low pierces window low, close recovers mid-bar) still
                        // counts as atBottom.
                        atTop    = (winHigh - data[i].High) / winRange <= 0.20;
                        atBottom = (data[i].Low  - winLow ) / winRange <= 0.20;
                    }
                }

                if (warmedUp)
                {
                    double cap = 0.0;
                    int parts = 0;

                    if (!double.IsNaN(volZ[i]))
                    { cap += Clamp01((volZ[i] - capVolZThresh) / 2.0); parts++; }

                    if (!double.IsNaN(rngZ[i]))
                    { cap += Clamp01((rngZ[i] - capRngZThresh) / 2.0); parts++; }

                    double r = data[i].High - data[i].Low;
                    if (r > 0)
                    {
                        double closePos = (data[i].Close - data[i].Low) / r;
                        double bodyLow  = Math.Min(data[i].Open, data[i].Close);
                        double lowerWickFrac = (bodyLow - data[i].Low) / r;
                        cap += Clamp01((closePos - 0.5) * 2.0) * Clamp01(lowerWickFrac / 0.3);
                        parts++;
                    }

                    if (i < bbLower.Length && !double.IsNaN(bbLower[i]) && atr[i] > 0)
                    {
                        double atrsBelow = (bbLower[i] - data[i].Low) / atr[i];
                        cap += Clamp01(atrsBelow / 1.5);
                        parts++;
                    }

                    cap += Clamp01((capRsiThresh - rsi[i]) / 20.0);
                    parts++;

                    if (i >= 2)
                    {
                        bool downTwo = data[i - 1].Close < data[i - 2].Close;
                        bool upNow   = data[i].Close > data[i - 1].Close;
                        cap += (downTwo && upNow) ? 1.0 : 0.0;
                        parts++;
                    }

                    double capScore = parts > 0 ? cap / parts : 0.0;
                    if (i < capSpan.Length) capSpan[i] = capScore;

                    if (atBottom && capScore >= confirm)
                    {
                        double prevCap = (i - 1 >= 0 && i - 1 < capSpan.Length)
                            ? capSpan[i - 1] : double.NaN;
                        if (double.IsNaN(prevCap) || prevCap < confirm)
                        {
                            if (i < botSpan.Length) botSpan[i] = capScore;
                        }
                    }
                }

                int pIdx = i - pivotLb;
                if (pIdx >= pivotLb && pIdx + pivotLb < n)
                {
                    bool isHigh = true, isLow = true;
                    double pH = data[pIdx].High, pL = data[pIdx].Low;
                    for (int k = 1; k <= pivotLb; k++)
                    {
                        if (data[pIdx - k].High >= pH) isHigh = false;
                        if (data[pIdx + k].High >= pH) isHigh = false;
                        if (data[pIdx - k].Low  <= pL) isLow  = false;
                        if (data[pIdx + k].Low  <= pL) isLow  = false;
                        if (!isHigh && !isLow) break;
                    }
                    if (isHigh)
                    {
                        prevSwingHighIdx = lastSwingHighIdx;
                        lastSwingHighIdx = pIdx;
                    }
                    if (isLow)
                    {
                        prevSwingLowIdx = lastSwingLowIdx;
                        lastSwingLowIdx = pIdx;
                    }
                }

                if (warmedUp)
                {
                    double evidence = 0.0;

                    // All distribution evidence streams gate on meaningfulRange — without
                    // a real rally into a high zone, pivot-based noise can pattern-match
                    // false divergences and false upthrusts. Decay still operates so an
                    // accumulator built up during a real move fades naturally if the
                    // window range collapses back to noise.
                    if (meaningfulRange)
                    {
                    if (pIdx >= 0 && pIdx == lastSwingHighIdx
                        && prevSwingHighIdx >= 0 && pIdx > prevSwingHighIdx)
                    {
                        double pricePrev = data[prevSwingHighIdx].High;
                        double priceNow  = data[lastSwingHighIdx].High;
                        double rsiPrev   = rsi[prevSwingHighIdx];
                        double rsiNow    = rsi[lastSwingHighIdx];
                        if (!double.IsNaN(rsiPrev) && !double.IsNaN(rsiNow)
                            && priceNow > pricePrev && rsiNow < rsiPrev)
                        {
                            evidence += 0.40;
                        }
                    }

                    if (pIdx >= 0 && pIdx == lastSwingHighIdx)
                    {
                        double vNow = data[lastSwingHighIdx].Volume;
                        double vMed = MedianOver(volume, Math.Max(0, i - lookback + 1), i);
                        if (vMed > 0 && vNow < 0.7 * vMed)
                            evidence += 0.25;
                    }

                    if (atTop && i >= 30)
                    {
                        double atrMed = MedianOver(atr, Math.Max(atrPeriod, i - 30 + 1), i);
                        if (atrMed > 0 && atr[i] < 0.85 * atrMed)
                            evidence += 0.15;
                    }

                    if (i >= pivotLb + 1)
                    {
                        double priorMax = double.MinValue;
                        for (int k = 2; k <= pivotLb + 1; k++)
                            if (data[i - k].High > priorMax) priorMax = data[i - k].High;
                        if (data[i - 1].High > priorMax && data[i].Close < priorMax)
                            evidence += 0.40;
                    }

                    if (atTop && i >= 20 && atr[i] > 0)
                    {
                        double winHi20 = double.MinValue, winLo20 = double.MaxValue;
                        for (int j = i - 20 + 1; j <= i; j++)
                        {
                            if (data[j].High > winHi20) winHi20 = data[j].High;
                            if (data[j].Low  < winLo20) winLo20 = data[j].Low;
                        }
                        double rangeOverAtr = (winHi20 - winLo20) / atr[i];
                        if (rangeOverAtr < consolidationAtr)
                            evidence += 0.15;
                    }
                    } // meaningfulRange

                    dist = dist * decayFactor + evidence;
                    double conf = 1.0 - Math.Exp(-dist / distScale);
                    if (i < distSpan.Length) distSpan[i] = conf;

                    if (atTop && conf >= confirm)
                    {
                        double prevConf = (i - 1 >= 0 && i - 1 < distSpan.Length)
                            ? distSpan[i - 1] : double.NaN;
                        if (double.IsNaN(prevConf) || prevConf < confirm)
                        {
                            if (i < topSpan.Length) topSpan[i] = conf;
                        }
                    }
                }
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer) =>
            Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            int lookback = GetInt(parameters, "LookbackWindow", 100);
            int pivotLb  = GetInt(parameters, "PivotLookback", 5);
            return lookback + pivotLb + 5;
        }

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (componentName == CompCapitulation)
            {
                if (double.IsNaN(value)) return "Capitulation, no data.";
                string interp = value >= 0.6 ? "high — bottom likely"
                              : value >= 0.4 ? "elevated"
                              :                 "low";
                return $"Capitulation {value:F2}, {interp}.";
            }
            if (componentName == CompDistribution)
            {
                if (double.IsNaN(value)) return "Distribution, no data.";
                string interp = value >= 0.6 ? "high — top forming"
                              : value >= 0.4 ? "building"
                              :                 "low";
                return $"Distribution {value:F2}, {interp}.";
            }
            if (componentName == CompBottom && !double.IsNaN(value))
                return $"Bottom confirmed at {value:F2} confidence.";
            if (componentName == CompTop && !double.IsNaN(value))
                return $"Top confirmed at {value:F2} confidence.";
            return null;
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            double cap  = TryGet(calculatedResults, CompCapitulation, index);
            double dist = TryGet(calculatedResults, CompDistribution, index);
            string capS  = double.IsNaN(cap)  ? "n/a" : cap.ToString("F2");
            string distS = double.IsNaN(dist) ? "n/a" : dist.ToString("F2");
            return $"Top/Bottom Detector: capitulation {capS}, distribution {distS}.";
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        // Median positive bar delta, in minutes. This used to sample the first eleven deltas
        // only, which made the detected timeframe a function of where the array started: a
        // scroll-back slid the sample onto older bars and could re-tune the whole indicator for
        // bars already on the chart. IndicatorMath medians the whole series instead — see the
        // note there.
        internal static double DetectBarIntervalMinutes(ReadOnlySpan<Ohlcv> data) =>
            IndicatorMath.MedianBarIntervalMinutes(data);

        private static double[] ComputeAtrWilder(ReadOnlySpan<Ohlcv> data, int period)
        {
            int n = data.Length;
            var atr = new double[n];
            if (n == 0) return atr;
            for (int i = 0; i < n; i++) atr[i] = double.NaN;
            if (n < 2) return atr;

            double trSum = 0;
            for (int i = 1; i <= period && i < n; i++)
            {
                double tr = TrueRange(data, i);
                trSum += tr;
                if (i == period) atr[i] = trSum / period;
            }
            for (int i = period + 1; i < n; i++)
            {
                double tr = TrueRange(data, i);
                atr[i] = (atr[i - 1] * (period - 1) + tr) / period;
            }
            return atr;
        }

        private static double TrueRange(ReadOnlySpan<Ohlcv> data, int i)
        {
            double hl = data[i].High - data[i].Low;
            double hc = Math.Abs(data[i].High - data[i - 1].Close);
            double lc = Math.Abs(data[i].Low  - data[i - 1].Close);
            return Math.Max(hl, Math.Max(hc, lc));
        }

        private static double[] ComputeRsiWilder(ReadOnlySpan<Ohlcv> data, int period)
        {
            int n = data.Length;
            var rsi = new double[n];
            for (int i = 0; i < n; i++) rsi[i] = double.NaN;
            if (n < period + 1) return rsi;

            double gainSum = 0, lossSum = 0;
            for (int i = 1; i <= period; i++)
            {
                double diff = data[i].Close - data[i - 1].Close;
                if (diff > 0) gainSum += diff; else lossSum += -diff;
            }
            double avgGain = gainSum / period;
            double avgLoss = lossSum / period;
            rsi[period] = avgLoss == 0 ? 100.0 : 100.0 - (100.0 / (1.0 + avgGain / avgLoss));

            for (int i = period + 1; i < n; i++)
            {
                double diff = data[i].Close - data[i - 1].Close;
                double gain = diff > 0 ?  diff : 0;
                double loss = diff < 0 ? -diff : 0;
                avgGain = (avgGain * (period - 1) + gain) / period;
                avgLoss = (avgLoss * (period - 1) + loss) / period;
                rsi[i] = avgLoss == 0 ? 100.0 : 100.0 - (100.0 / (1.0 + avgGain / avgLoss));
            }
            return rsi;
        }

        private static double[] ComputeRollingZ(double[] series, int window)
        {
            int n = series.Length;
            var z = new double[n];
            for (int i = 0; i < n; i++) z[i] = double.NaN;
            if (n < window) return z;

            double sum = 0, sumSq = 0;
            for (int i = 0; i < window; i++) { sum += series[i]; sumSq += series[i] * series[i]; }

            for (int i = window - 1; i < n; i++)
            {
                if (i >= window)
                {
                    sum   += series[i] - series[i - window];
                    sumSq += series[i] * series[i] - series[i - window] * series[i - window];
                }
                double mean = sum / window;
                double var  = (sumSq / window) - mean * mean;
                if (var > 0)
                {
                    double std = Math.Sqrt(var);
                    z[i] = (series[i] - mean) / std;
                }
            }
            return z;
        }

        private static double[] ComputeBollingerLower(ReadOnlySpan<Ohlcv> data, int period, double k)
        {
            int n = data.Length;
            var lower = new double[n];
            for (int i = 0; i < n; i++) lower[i] = double.NaN;
            if (n < period) return lower;

            double sum = 0, sumSq = 0;
            for (int i = 0; i < period; i++) { sum += data[i].Close; sumSq += data[i].Close * data[i].Close; }
            for (int i = period - 1; i < n; i++)
            {
                if (i >= period)
                {
                    double prev = data[i - period].Close;
                    sum   += data[i].Close - prev;
                    sumSq += data[i].Close * data[i].Close - prev * prev;
                }
                double mean = sum / period;
                double var  = (sumSq / period) - mean * mean;
                if (var > 0) lower[i] = mean - k * Math.Sqrt(var);
            }
            return lower;
        }

        private static double[] ComputeBollingerUpper(ReadOnlySpan<Ohlcv> data, int period, double k)
        {
            int n = data.Length;
            var upper = new double[n];
            for (int i = 0; i < n; i++) upper[i] = double.NaN;
            if (n < period) return upper;

            double sum = 0, sumSq = 0;
            for (int i = 0; i < period; i++) { sum += data[i].Close; sumSq += data[i].Close * data[i].Close; }
            for (int i = period - 1; i < n; i++)
            {
                if (i >= period)
                {
                    double prev = data[i - period].Close;
                    sum   += data[i].Close - prev;
                    sumSq += data[i].Close * data[i].Close - prev * prev;
                }
                double mean = sum / period;
                double var  = (sumSq / period) - mean * mean;
                if (var > 0) upper[i] = mean + k * Math.Sqrt(var);
            }
            return upper;
        }

        private static double MedianOver(double[] series, int from, int to)
        {
            if (to < from) return double.NaN;
            int count = to - from + 1;
            var buf = new List<double>(count);
            for (int i = from; i <= to; i++)
                if (!double.IsNaN(series[i])) buf.Add(series[i]);
            if (buf.Count == 0) return double.NaN;
            buf.Sort();
            int m = buf.Count / 2;
            return (buf.Count % 2 == 0) ? 0.5 * (buf[m - 1] + buf[m]) : buf[m];
        }

        private static double Clamp01(double x) => x < 0 ? 0 : (x > 1 ? 1 : x);

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
