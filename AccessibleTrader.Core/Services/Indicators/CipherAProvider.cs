using System;
using System.Collections.Generic;
using System.Text;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Accessible Cipher A — price-chart overlay indicator inspired by Market Cipher A.
    ///
    /// All signals render directly on the main price chart at actual price Y positions
    /// (above or below candle wicks), offset by a fraction of the candle range so they
    /// do not overlap with the body.
    ///
    /// Components:
    ///   WT Momentum           — Continuous small dot on every candle at the close price.
    ///                           Color gradient: teal (oversold) → gray (neutral) → red (overbought).
    ///                           DisplayType = Dot; companion "_color" array drives RenderDot's gradient path.
    ///   Buy Signal            — Blue dot below the candle low: WT1 × WT2 cross-up from oversold.
    ///   Sell Signal           — Red dot above the candle high: WT1 × WT2 cross-down from overbought.
    ///   Bullish Divergence    — Bright green diamond below low: price lower low but WT higher low.
    ///   Bearish Divergence    — Bright red diamond above high: price higher high but WT lower high.
    ///   Overbought Bearish Divergence — Dark red diamond above high: bearish divergence from OB zone.
    ///                           Supersedes Bearish Divergence (both cannot appear on the same bar).
    ///   Manipulation          — Gold X below low: WT oversold AND Money Flow positive.
    ///                           Indicates smart-money accumulation while price is depressed —
    ///                           the "manipulation" phase before a reversal upward.
    ///   Exhaustion            — Red X above high: WT overbought AND Money Flow negative.
    ///                           Indicates distribution/selling into strength — momentum exhausted.
    ///
    /// Y-offset formula (all signals):
    ///   offset = Max(candleRange × 0.15, closePrice × 0.002)
    ///   Ensures shapes clear the wicks at any price scale.
    ///
    /// Parameters:
    ///   WT1Period  — Wave Trend channel period (default 9)
    ///   WT2Period  — Wave Trend average period (default 12)
    ///   OBLevel    — Overbought / oversold threshold (default 53)
    ///   MFPeriod   — Money Flow smoothing period (default 3)
    ///   PivotBars  — Bars each side for divergence pivot detection (default 3)
    /// </summary>
    public class CipherAProvider : IIndicatorProvider
    {
        public string Name => "Cipher A";

        public const string CompWtMomentum   = "WT Momentum";
        public const string CompBuySignal    = "Buy Signal";
        public const string CompSellSignal   = "Sell Signal";
        public const string CompBullDiv      = "Bullish Divergence";
        public const string CompBearDiv      = "Bearish Divergence";
        public const string CompBloodDiamond = "Overbought Bearish Divergence";
        public const string CompManipulation = "Manipulation";
        public const string CompExhaustion   = "Exhaustion";

        // Companion array key for GradientDot color source — not a navigable component.
        private const string CompWtMomentumColor = CompWtMomentum + "_color";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "CIPHER_A",
                Name        = "Cipher A",
                Category    = "Multi-Signal",
                DefaultPane = "Main",
                Description =
                    "Price-chart overlay: continuous WT momentum ribbon, buy/sell dots, divergence diamonds, " +
                    "and smart-money confluence signals. All signals render at actual price levels.",
                Components = new List<IndicatorComponentMetadata>
                {
                    // ── WT Momentum ribbon ───────────────────────────────────────────────────────
                    // Continuous Dot on every candle at close price level.
                    // RenderDot detects the companion "_color" array and applies teal→gray→red gradient.
                    // Accessibility: speaks and sounds as "dot" — one Ping per bar, gradient timbre.
                    // UsesGradientSpeech: navigation speech produces "strong bullish momentum" etc.
                    // Rendered first so signal shapes layer on top of it.
                    new() { Name = CompWtMomentum,   DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                            DefaultColorHex = "#00E5FF", DefaultColorHexSecondary = "#F23645",
                            DefaultThickness = 6.0f,
                            DefaultWaveform = "triangle", DefaultEnvelopeType = "Ping",
                            DefaultDecayMs = 80, DefaultBaseFrequency = 440.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultSoundPatchId = "gradient_blend",
                            UsesGradientSpeech = true,
                            SpeechTemplate = "Wave Trend momentum. {gradient_speech}.",
                            IsVisible = true,
                            DefaultUsePolarityColoring = false },

                    // ── Buy / Sell dots ──────────────────────────────────────────────────────────
                    // Filled circles matching the real MC A visual style.
                    // Blue dot below low: WT1 × WT2 cross-up from oversold territory.
                    new() { Name = CompBuySignal,    DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                            DefaultColorHex = "#0BBCF5",
                            DefaultThickness = 9.0f,
                            DefaultWaveform = "sine", DefaultEnvelopeType = "Ping",
                            DefaultDecayMs = 380, DefaultBaseFrequency = 880.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "sine_bell",
                            DefaultSignalSpeechTemplate = "Buy signal at {price}",
                            IsVisible = true,
                            DefaultUsePolarityColoring = false },

                    // Red dot above high: WT1 × WT2 cross-down from overbought territory.
                    new() { Name = CompSellSignal,   DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF1744",
                            DefaultThickness = 9.0f,
                            DefaultWaveform = "sine", DefaultEnvelopeType = "Ping",
                            DefaultDecayMs = 380, DefaultBaseFrequency = 220.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "sine_bell",
                            DefaultSignalSpeechTemplate = "Sell signal at {price}",
                            IsVisible = true,
                            DefaultUsePolarityColoring = false },

                    // ── Divergence diamonds ──────────────────────────────────────────────────────
                    // Bright green diamond below low: price lower low + WT higher low = bullish divergence.
                    new() { Name = CompBullDiv,      DisplayType = ComponentDisplayType.Diamond, Role = ComponentRole.Signal,
                            DefaultColorHex = "#00E676",
                            DefaultThickness = 11.0f,
                            DefaultWaveform = "triangle", DefaultEnvelopeType = "Ping",
                            DefaultDecayMs = 280, DefaultBaseFrequency = 660.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "triangle_bell",
                            DefaultSignalSpeechTemplate = "Bullish divergence detected",
                            IsVisible = true,
                            DefaultUsePolarityColoring = false },

                    // Bright red diamond above high: price higher high + WT lower high = bearish divergence.
                    // Suppressed on bars where Overbought Bearish Divergence fires.
                    new() { Name = CompBearDiv,      DisplayType = ComponentDisplayType.Diamond, Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF1744",
                            DefaultThickness = 11.0f,
                            DefaultWaveform = "triangle", DefaultEnvelopeType = "Ping",
                            DefaultDecayMs = 280, DefaultBaseFrequency = 330.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "triangle_bell",
                            DefaultSignalSpeechTemplate = "Bearish divergence detected",
                            IsVisible = true,
                            DefaultUsePolarityColoring = false },

                    // Dark red diamond above high: bearish divergence AND prior WT pivot was in overbought.
                    // Stronger bearish signal — supersedes Bearish Divergence on the same bar.
                    new() { Name = CompBloodDiamond, DisplayType = ComponentDisplayType.Diamond, Role = ComponentRole.Signal,
                            DefaultColorHex = "#D50000",
                            DefaultThickness = 13.0f,
                            DefaultWaveform = "triangle", DefaultEnvelopeType = "Ping",
                            DefaultDecayMs = 500, DefaultBaseFrequency = 165.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "triangle_bell",
                            DefaultSignalSpeechTemplate = "Overbought bearish divergence, high confidence",
                            IsVisible = true,
                            DefaultUsePolarityColoring = false },

                    // ── Money Flow confluence signals ────────────────────────────────────────────
                    // Gold X below low: WT in oversold zone AND Money Flow positive.
                    // "Manipulation" — smart-money accumulation while price is depressed.
                    new() { Name = CompManipulation, DisplayType = ComponentDisplayType.Cross, Role = ComponentRole.Signal,
                            DefaultColorHex = "#FFD600",
                            DefaultThickness = 8.0f,
                            DefaultWaveform = "triangle", DefaultEnvelopeType = "Ping",
                            DefaultDecayMs = 320, DefaultBaseFrequency = 550.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "detuned_pair_bell",
                            DefaultSignalSpeechTemplate = "Potential smart money accumulation",
                            IsVisible = true,
                            DefaultUsePolarityColoring = false },

                    // Red X above high: WT in overbought zone AND Money Flow negative.
                    // "Exhaustion" — distribution/selling into strength, momentum running dry.
                    new() { Name = CompExhaustion,   DisplayType = ComponentDisplayType.Cross, Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF6D00",
                            DefaultThickness = 8.0f,
                            DefaultWaveform = "triangle", DefaultEnvelopeType = "Ping",
                            DefaultDecayMs = 320, DefaultBaseFrequency = 250.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "detuned_pair_bell",
                            DefaultSignalSpeechTemplate = "Potential distribution, exhaustion signal",
                            IsVisible = true,
                            DefaultUsePolarityColoring = false },

                },
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "WT1Period",  DisplayName = "WT Channel Period",     DataType = typeof(int),    DefaultValue = 9.0,
                            Description = "Period for the Wave Trend channel EMA." },
                    new() { Name = "WT2Period",  DisplayName = "WT Average Period",     DataType = typeof(int),    DefaultValue = 12.0,
                            Description = "Period for the Wave Trend signal SMA." },
                    new() { Name = "OBLevel",    DisplayName = "OB/OS Threshold",       DataType = typeof(double), DefaultValue = 53.0,
                            Description = "Overbought/oversold threshold. Buy signals require WT below −threshold; sell above +threshold." },
                    new() { Name = "MFPeriod",   DisplayName = "Money Flow Period",     DataType = typeof(int),    DefaultValue = 3.0,
                            Description = "SMA period for the Money Flow calculation used in Manipulation and Exhaustion signals." },
                    new() { Name = "PivotBars",  DisplayName = "Divergence Pivot Bars", DataType = typeof(int),    DefaultValue = 3.0,
                            Description = "Bars each side required for divergence pivot confirmation." },
                }
            }
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            if (!code.Equals("CIPHER_A", StringComparison.OrdinalIgnoreCase)) return;
            int n = data.Length;
            if (n < 10) return;

            int    wt1Period = GetInt(parameters, "WT1Period", 9);
            int    wt2Period = GetInt(parameters, "WT2Period", 12);
            double obLevel   = GetDbl(parameters, "OBLevel",   53.0);
            int    mfPeriod  = GetInt(parameters, "MFPeriod",  3);
            int    pivotBars = GetInt(parameters, "PivotBars", 3);

            // ── Price source arrays ────────────────────────────────────────────────────────
            var close  = new double[n];
            var open   = new double[n];
            var high   = new double[n];
            var low    = new double[n];
            var volume = new double[n];
            var hlc3   = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (data[i].Close <= 0 || data[i].Open <= 0 || data[i].High <= 0)
                {
                    hlc3[i] = double.NaN;  // propagates as NaN through EMA — prevents zero from corrupting WT
                    continue;
                }
                close[i]  = data[i].Close;
                open[i]   = data[i].Open;
                high[i]   = data[i].High;
                low[i]    = data[i].Low;
                volume[i] = data[i].Volume;
                hlc3[i]   = (high[i] + low[i] + close[i]) / 3.0;
            }

            // ── Wave Trend ─────────────────────────────────────────────────────────────────
            var esa     = Ema(hlc3, wt1Period);
            var absDiff = new double[n];
            for (int i = 0; i < n; i++)
                absDiff[i] = double.IsNaN(esa[i]) ? double.NaN : Math.Abs(hlc3[i] - esa[i]);
            var d  = Ema(absDiff, wt1Period);
            var ci = new double[n];
            for (int i = 0; i < n; i++)
                ci[i] = (double.IsNaN(d[i]) || d[i] < 1e-10)
                    ? double.NaN
                    : (hlc3[i] - esa[i]) / (0.015 * d[i]);
            var wt1 = Ema(ci, wt2Period);
            // WT2 uses WMA(4) instead of SMA(4): same smoothing window but weights
            // recent bars more heavily, reducing WT2's lag at regime changes by ~1 bar.
            var wt2 = MovingAverageHelper.Wma(wt1, 4);

            // ── Money Flow ─────────────────────────────────────────────────────────────────
            // Magnitude-weighted directional volume. Weighting the volume by the candle's
            // body-to-range ratio (Chaikin-style CLV) means a strong close near the high
            // counts more than a marginal up-close that barely cleared open — this
            // dampens Manipulation/Exhaustion false positives in doji-heavy chop.
            var mfv = new double[n];
            for (int i = 0; i < n; i++)
            {
                double range = high[i] - low[i];
                if (range < 1e-10) { mfv[i] = 0.0; continue; }
                double clv = ((close[i] - low[i]) - (high[i] - close[i])) / range; // Chaikin CLV in [-1..+1]
                mfv[i] = volume[i] * clv;
            }
            var mf = Sma(mfv, mfPeriod);

            // ── Output arrays ──────────────────────────────────────────────────────────────
            var wtMomentum   = new double[n];  // close price (Y position for Dot)
            var wtMomentumCl = new double[n];  // raw WT1 value (gradient color source for RenderDot)
            var buySignal    = new double[n];
            var sellSignal   = new double[n];
            var bullDiv      = new double[n];
            var bearDiv      = new double[n];
            var bloodDiamond = new double[n];
            var manipulation = new double[n];
            var exhaustion   = new double[n];
            Array.Fill(wtMomentum,   double.NaN);
            Array.Fill(wtMomentumCl, double.NaN);
            Array.Fill(buySignal,    double.NaN);
            Array.Fill(sellSignal,   double.NaN);
            Array.Fill(bullDiv,      double.NaN);
            Array.Fill(bearDiv,      double.NaN);
            Array.Fill(bloodDiamond, double.NaN);
            Array.Fill(manipulation, double.NaN);
            Array.Fill(exhaustion,   double.NaN);

            // ── WT Momentum ribbon (every bar with valid WT1) ──────────────────────────────
            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(wt1[i])) continue;
                wtMomentum[i]   = close[i];   // Y position = close price
                wtMomentumCl[i] = wt1[i];     // color = raw WT1 oscillator value
            }

            // ── Crossover buy / sell signals (2-bar sustained OS/OB) ───────────────────────
            // A bare wt1-crosses-wt2 event at oversold fires too often during wick noise:
            // one bar dips to -60, the next bar pops back to -40, and the "crossover"
            // happens on the pop with WT1 already headed up. Requiring the PRIOR bar to
            // also have been oversold means the setup was sustained, not a one-bar spike.
            for (int i = 2; i < n; i++)
            {
                if (double.IsNaN(wt1[i]) || double.IsNaN(wt2[i]) ||
                    double.IsNaN(wt1[i - 1]) || double.IsNaN(wt2[i - 1])) continue;

                bool crossUp   = wt1[i - 1] < wt2[i - 1] && wt1[i] >= wt2[i];
                bool crossDown = wt1[i - 1] > wt2[i - 1] && wt1[i] <= wt2[i];

                double range  = high[i] - low[i];
                double offset = Math.Max(range * 0.15, close[i] * 0.002);

                bool sustainedOs = wt1[i] < -obLevel && !double.IsNaN(wt1[i - 1]) && wt1[i - 1] < -obLevel;
                bool sustainedOb = wt1[i] >  obLevel && !double.IsNaN(wt1[i - 1]) && wt1[i - 1] >  obLevel;

                if (crossUp   && sustainedOs) buySignal[i]  = low[i]  - offset;
                if (crossDown && sustainedOb) sellSignal[i] = high[i] + offset;
            }

            // ── Money Flow confluence signals (persistent condition, not event-based) ───────
            // Manipulation: WT oversold AND MF positive — smart money buying while price is low.
            // Exhaustion:   WT overbought AND MF negative — distribution into strength.
            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(wt1[i]) || double.IsNaN(mf[i])) continue;
                double range  = high[i] - low[i];
                double offset = Math.Max(range * 0.15, close[i] * 0.002);

                if (wt1[i] < -obLevel && mf[i] > 0) manipulation[i] = low[i]  - offset;
                if (wt1[i] >  obLevel && mf[i] < 0) exhaustion[i]   = high[i] + offset;
            }

            // ── Divergence detection via pivot highs / lows ────────────────────────────────
            int start = pivotBars;
            int end   = n - pivotBars;

            var pivotLowIdx  = new List<int>();
            var pivotHighIdx = new List<int>();

            for (int i = start; i < end; i++)
            {
                if (double.IsNaN(wt1[i])) continue;
                bool isLow = true, isHigh = true;
                for (int j = i - pivotBars; j <= i + pivotBars; j++)
                {
                    if (j == i || double.IsNaN(wt1[j])) continue;
                    if (wt1[j] < wt1[i]) isLow  = false;
                    if (wt1[j] > wt1[i]) isHigh = false;
                }
                if (isLow)  pivotLowIdx.Add(i);
                if (isHigh) pivotHighIdx.Add(i);
            }

            // Bull Divergence: price lower low AND WT higher low at consecutive pivot lows.
            for (int k = 1; k < pivotLowIdx.Count; k++)
            {
                int prev = pivotLowIdx[k - 1], curr = pivotLowIdx[k];
                if (double.IsNaN(wt1[prev]) || double.IsNaN(wt1[curr])) continue;
                if (low[curr] < low[prev] && wt1[curr] > wt1[prev])
                {
                    double range  = high[curr] - low[curr];
                    double offset = Math.Max(range * 0.15, close[curr] * 0.002);
                    bullDiv[curr] = low[curr] - offset;
                }
            }

            // Bear Divergence: price higher high AND WT lower high at consecutive pivot highs.
            // Overbought Bearish Divergence: same AND prior WT pivot was in overbought → supersedes.
            for (int k = 1; k < pivotHighIdx.Count; k++)
            {
                int prev = pivotHighIdx[k - 1], curr = pivotHighIdx[k];
                if (double.IsNaN(wt1[prev]) || double.IsNaN(wt1[curr])) continue;
                if (high[curr] > high[prev] && wt1[curr] < wt1[prev])
                {
                    double range  = high[curr] - low[curr];
                    double offset = Math.Max(range * 0.15, close[curr] * 0.002);
                    if (wt1[prev] > obLevel)
                        bloodDiamond[curr] = high[curr] + offset;   // supersedes Bear Div
                    else
                        bearDiv[curr]      = high[curr] + offset;
                }
            }

            // ── Write to buffer ────────────────────────────────────────────────────────────
            WriteToBuffer(buffer, CompWtMomentum,     wtMomentum,   n);
            WriteToBuffer(buffer, CompWtMomentumColor, wtMomentumCl, n);  // companion color array
            WriteToBuffer(buffer, CompBuySignal,      buySignal,    n);
            WriteToBuffer(buffer, CompSellSignal,     sellSignal,   n);
            WriteToBuffer(buffer, CompBullDiv,        bullDiv,      n);
            WriteToBuffer(buffer, CompBearDiv,        bearDiv,      n);
            WriteToBuffer(buffer, CompBloodDiamond,   bloodDiamond, n);
            WriteToBuffer(buffer, CompManipulation,   manipulation, n);
            WriteToBuffer(buffer, CompExhaustion,     exhaustion,   n);
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            int pivot = GetInt(parameters, "PivotBars", 3);
            return pivot * 2 + 20;
        }

        /// <summary>
        /// Returns an imperative speech string for the given component at the current bar.
        /// Called by NavigationFeedbackManager when the component has a SpeechTemplate that
        /// requires runtime context (e.g. momentum direction, divergence type) beyond what
        /// the declarative SpeechTemplate token substitution can express.
        /// Returns null to fall through to the declarative SpeechTemplate or generic formatter.
        /// </summary>
        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value))
                return "no data";

            // Providers return VALUE-ONLY strings. NavigationFeedbackManager prepends
            // "[Component Name]. [Type]. " on UP/DOWN moves.

            return componentName switch
            {
                // WT Momentum ribbon — gradient strength speech (value-only: no name prefix)
                CompWtMomentum => GetMomentumValueSpeech(allComponentData, dataIndex),

                // Signal dots — which waves crossed, WT1 value at crossing, and price.
                CompBuySignal    => BuildWaveCrossSpeech(allComponentData, dataIndex, bar.Close, isBuy: true),
                CompSellSignal   => BuildWaveCrossSpeech(allComponentData, dataIndex, bar.Close, isBuy: false),

                // Divergences — price + cause description + pivot lag disclosure.
                // Divergence detection uses a pivot window of PivotBars; the signal appears
                // at the center of the window, so it's effectively PivotBars bars late.
                CompBullDiv      => $"Price {AccessibleTrader.Core.Services.Accessibility.SpeechPriceFormatter.FormatPrice(bar.Close)}. Price lower low, WT higher low. Confirmed on pivot.",
                CompBearDiv      => $"Price {AccessibleTrader.Core.Services.Accessibility.SpeechPriceFormatter.FormatPrice(bar.Close)}. Price higher high, WT lower high. Confirmed on pivot.",
                CompBloodDiamond => $"Price {AccessibleTrader.Core.Services.Accessibility.SpeechPriceFormatter.FormatPrice(bar.Close)}. Overbought zone, high confidence. Confirmed on pivot.",

                // Confluence signals — price + the two conditions that fired.
                CompManipulation => $"Price {AccessibleTrader.Core.Services.Accessibility.SpeechPriceFormatter.FormatPrice(bar.Close)}. Oversold with positive money flow.",
                CompExhaustion   => $"Price {AccessibleTrader.Core.Services.Accessibility.SpeechPriceFormatter.FormatPrice(bar.Close)}. Overbought with negative money flow.",

                _ => null
            };
        }

        private static string GetMomentumValueSpeech(IReadOnlyDictionary<string, double[]> data, int idx)
        {
            if (!data.TryGetValue(CompWtMomentum + "_color", out var colorArr) ||
                colorArr == null || idx >= colorArr.Length || double.IsNaN(colorArr[idx]))
                return "no data";

            double v = colorArr[idx];
            if (v > 60)  return $"Strong bullish, {v:F1}";
            if (v > 20)  return $"Bullish, {v:F1}";
            if (v > -20) return $"Neutral, {v:F1}";
            if (v > -60) return $"Bearish, {v:F1}";
            return $"Strong bearish, {v:F1}";
        }

        /// <summary>
        /// Buy/Sell signal speech: identifies the crossing waves (WT1 crossed WT2), the WT1
        /// oscillator value at the moment of crossing, and the bar's close price.
        /// E.g. "WT1 crossed WT2 upward at -62.3, price 84,500.00."
        /// </summary>
        private static string BuildWaveCrossSpeech(
            IReadOnlyDictionary<string, double[]> data, int idx, double closePrice, bool isBuy)
        {
            double wt1 = double.NaN;
            if (data.TryGetValue(CompWtMomentumColor, out var colorArr) &&
                colorArr != null && idx < colorArr.Length)
                wt1 = colorArr[idx];

            string direction = isBuy ? "upward" : "downward";
            string wt1Str    = double.IsNaN(wt1) ? "" : $" at {wt1:F1}";
            return $"WT1 crossed WT2 {direction}{wt1Str}, price {closePrice:F2}.";
        }

        /// <summary>
        /// Returns a spoken summary of the indicator's state at the given bar index.
        /// Triggered by Ctrl+Shift+D (full analysis) and F4 (context summary).
        /// Describes active signals, divergence state, and WT momentum direction in plain language.
        /// Returns an empty string when no meaningful fact is available (NaN data, wrong code, etc.).
        /// </summary>
        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> results, int index, Dictionary<string, object> parameters)
        {
            if (!code.Equals("CIPHER_A", StringComparison.OrdinalIgnoreCase) || index < 0) return string.Empty;

            double buyVal    = GetVal(results, CompBuySignal,    index);
            double sellVal   = GetVal(results, CompSellSignal,   index);
            double bullVal   = GetVal(results, CompBullDiv,      index);
            double bearVal   = GetVal(results, CompBearDiv,      index);
            double bloodVal  = GetVal(results, CompBloodDiamond, index);
            double manipVal  = GetVal(results, CompManipulation, index);
            double exhVal    = GetVal(results, CompExhaustion,   index);
            double wtColor   = GetVal(results, CompWtMomentumColor, index);

            var sb = new StringBuilder();

            // WT Momentum context (1-sentence)
            if (!double.IsNaN(wtColor))
            {
                string momentumDesc = wtColor > 60  ? "strong bullish"
                                    : wtColor > 20  ? "bullish"
                                    : wtColor > -20 ? "neutral"
                                    : wtColor > -60 ? "bearish"
                                    :                 "strong bearish";
                sb.Append($"WT momentum {momentumDesc}. ");
            }

            // Active signals on this bar (comma-separated)
            var activeSignals = new List<string>();
            if (!double.IsNaN(buyVal))    activeSignals.Add("Buy signal");
            if (!double.IsNaN(manipVal))  activeSignals.Add("Accumulation signal");
            if (!double.IsNaN(bullVal))   activeSignals.Add("Bullish divergence");
            if (!double.IsNaN(bloodVal))  activeSignals.Add("Overbought bearish divergence");
            else if (!double.IsNaN(bearVal)) activeSignals.Add("Bearish divergence");
            if (!double.IsNaN(sellVal))   activeSignals.Add("Sell signal");
            if (!double.IsNaN(exhVal))    activeSignals.Add("Exhaustion signal");

            sb.Append(activeSignals.Count > 0
                ? string.Join(", ", activeSignals) + "."
                : "No active signals.");

            return sb.ToString().TrimEnd();
        }

        // ── DSP helpers ───────────────────────────────────────────────────────────────────

        private static double[] Ema(double[] src, int period)
        {
            var r = new double[src.Length];
            double k = 2.0 / (period + 1.0);
            double ema = double.NaN;
            int warmup = 0;
            for (int i = 0; i < src.Length; i++)
            {
                double v = src[i];
                if (double.IsNaN(v)) { r[i] = double.NaN; continue; }
                if (double.IsNaN(ema)) { ema = v; warmup = 1; }
                else { ema = v * k + ema * (1.0 - k); warmup++; }
                r[i] = warmup < period ? double.NaN : ema;
            }
            return r;
        }

        private static double[] Sma(double[] src, int period)
        {
            var r = new double[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                if (i < period - 1) { r[i] = double.NaN; continue; }
                double sum = 0; int cnt = 0;
                for (int j = i - period + 1; j <= i; j++)
                    if (!double.IsNaN(src[j])) { sum += src[j]; cnt++; }
                r[i] = cnt == period ? sum / period : double.NaN;
            }
            return r;
        }

        private static void WriteToBuffer(IIndicatorResultBuffer buffer, string name, double[] data, int n)
        {
            var span = buffer.GetComponentSpan(name);
            int len = Math.Min(span.Length, data.Length);
            for (int i = 0; i < len; i++) span[i] = data[i];
        }

        private static double GetVal(IReadOnlyDictionary<string, double[]> r, string key, int idx)
        {
            if (!r.TryGetValue(key, out var arr) || arr == null || idx >= arr.Length) return double.NaN;
            return arr[idx];
        }

        // Cipher A renders on the price pane — no separate OB/OS reference level lines.
        public List<LevelDescriptor> GetDefaultLevels(string code)
            => new();

        private static int    GetInt(Dictionary<string, object> p, string k, int    def) => p.TryGetValue(k, out var v) ? (int)Convert.ToDouble(v) : def;
        private static double GetDbl(Dictionary<string, object> p, string k, double def) => p.TryGetValue(k, out var v) ? Convert.ToDouble(v) : def;
    }
}
