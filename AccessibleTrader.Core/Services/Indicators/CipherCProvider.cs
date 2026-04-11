using System;
using System.Collections.Generic;
using System.Text;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Cipher C — Adaptive cycle oscillator combining Ehlers-style cycle detection,
    /// Fisher Transform normalization, Hull RSI confluence confirmation, and cycle
    /// failure detection for trend-continuation signals.
    ///
    /// Oscillator pane (single pane, no price-chart overlay):
    ///
    ///   Top tier (sell signals — 3 dots, red family, above the wave):
    ///     Top Single   — Falling cross from OB zone, no RSI confirm (weak)
    ///     Top Double   — Falling cross from OB zone + Hull RSI > 50 (confirmed)
    ///     Top Triple   — Falling cross from DEEP OB (>60) + Hull RSI > 50 (strongest)
    ///
    ///   Cycle Sine     — Main oscillator: Fisher-transformed stochastic, blue
    ///   Lead Sine      — Hull MA signal line (ghost white); crossovers drive all signals
    ///   Cycle Fill     — Cloud between Sine and Lead (teal/coral, DefaultCloudFills)
    ///
    ///   Bottom tier (buy signals — 3 dots, cyan family, below the wave):
    ///     Bottom Single  — Rising cross from OS zone, no RSI confirm (weak)
    ///     Bottom Double  — Rising cross from OS zone + Hull RSI &lt; 50 (confirmed)
    ///     Bottom Triple  — Rising cross from DEEP OS (&lt;-60) + Hull RSI &lt; 50 (strongest)
    ///
    ///   Failure signals (trend-continuation dots):
    ///     Shallow Peak   — Falling cross without ever reaching OB → bear trend continuation (orange)
    ///     Shallow Trough — Rising cross without ever reaching OS → bull trend continuation (lime)
    ///
    /// Signal logic:
    ///   Every Sine/Lead crossover fires EXACTLY ONE signal (or none if in warmup).
    ///   — If oscillator reached OB/OS since last opposite cross → success tier (Single/Double/Triple)
    ///   — If oscillator never reached OB/OS → failure signal (Shallow Peak/Trough)
    ///   Tier is determined by the MAX depth reached and Hull RSI confirmation:
    ///     Triple: max depth beyond ±ExtremeLevel (60) AND Hull RSI confirms
    ///     Double: max depth beyond ±OBLevel (75) AND Hull RSI confirms
    ///     Single: max depth beyond ±OBLevel (75), no RSI confirm
    ///
    /// Math (Ehlers Cyber Cycle, v2):
    ///   1. 4-bar weighted smooth: (P + 2P[1] + 2P[2] + P[3]) / 6  — minimal-lag pre-smooth
    ///   2. Ehlers Cyber Cycle bandpass filter:
    ///        alpha = 2 / (CyclePeriod + 1)
    ///        a1    = 1 − 0.5×alpha,   a2 = 1 − alpha
    ///        Cycle = a1²×(S−2S[1]+S[2]) + 2×a2×Cycle[1] − a2²×Cycle[2]
    ///      Rejects the trend component, passes the dominant cycle band.
    ///   3. Post-filter EMA(Cycle, SmoothPeriod) — optional smoothing (1 = raw)
    ///   4. Stochastic(smoothCycle, CyclePeriod) → Fisher Transform × FisherScale → CycleSine
    ///   5. HullMA(CycleSine, SignalPeriod)       — LeadSine signal line (lower lag than EMA)
    ///   6. HullMA(RSI(close, CyclePeriod))       — Hull RSI for tier confirmation
    ///
    /// Reference levels: ±(OBLevel+15) (extreme), ±OBLevel (OB/OS), 0 (zero).
    ///
    /// Parameters:
    ///   CyclePeriod  — Ehlers bandpass alpha, stochastic lookback, RSI period (default 10)
    ///   SmoothPeriod — Post-filter EMA period (1 = raw cycle, most responsive) (default 3)
    ///   SignalPeriod — Hull MA period for Lead Sine (default 3)
    ///   OBLevel      — OB/OS threshold (default 75.0)
    /// </summary>
    public class CipherCProvider : IIndicatorProvider
    {
        public string Name => "Cipher C";

        // ── Component name constants ──────────────────────────────────────────
        // Public so test code can reference them directly (mirrors CipherAProvider pattern).
        public const string CompTopSingle     = "Top Single";
        public const string CompTopDouble     = "Top Double";
        public const string CompTopTriple     = "Top Triple";
        public const string CompCycleSine     = "Cycle Sine";
        public const string CompBottomSingle  = "Bottom Single";
        public const string CompBottomDouble  = "Bottom Double";
        public const string CompBottomTriple  = "Bottom Triple";
        public const string CompLeadSine      = "Lead Sine";
        public const string CompShallowPeak   = "Shallow Peak";
        public const string CompShallowTrough = "Shallow Trough";

        /// <summary>
        /// Fisher(0.70) × 50 ≈ 43  — sits just inside the OB/OS zone at stoch ≈ 0.85/0.15.
        /// Fisher(0.85) × 50 ≈ 55  — clears OB/OS convincingly.
        /// Fisher(0.92) × 50 ≈ 71  — pushes into the extreme zone.
        /// </summary>
        private const double FisherScale  = 50.0;
        // ExtremeLevel is now derived from OBLevel + 15 in Calculate() so it scales
        // with user edits. The Triple tier fires when the cycle reaches OBLevel + 15,
        // e.g. default OBLevel=75 → extreme at 90 (raw Fisher ~1.8, stoch ~95th pct).

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "CIPHER_C",
                Name        = "Cipher C",
                Category    = "Oscillators",
                DefaultPane = "Pane_CIPHER_C",
                Description = "Adaptive cycle oscillator. Three-tier top/bottom signals plus cycle " +
                              "failure detection for trend continuation. Uses Fisher Transform " +
                              "normalization with Hull RSI confirmation. Best on daily and weekly timeframes.",
                RequiresFullRecalcOnTick = false,

                Components = new List<IndicatorComponentMetadata>
                {
                    // ════════════════════════════════════════════════════════
                    // TOP TIER — three sell-signal dots, red family
                    // Ordered strongest→weakest so navigation encounters the
                    // most significant signal first when multiple fire.
                    // ════════════════════════════════════════════════════════

                    // Top Triple: deep OB extreme + Hull RSI confirms.
                    // dual_tone_bell at 380 Hz — simultaneous golden chord (matching
                    // Cipher B's Triple Confluence gold dot in character).
                    // Deep red (#FF1744), large (7px) — unmistakable, rare.
                    new() { Name = CompTopTriple,
                            DisplayName = "Top Triple",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF1744",
                            DefaultThickness = 7.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "dual_tone_bell",
                            DefaultDecayMs = 500,
                            DefaultBaseFrequency = 380.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Cycle top triple, strong sell confirmation",
                            DefaultUsePolarityColoring = false,
                            IsVisible = true },

                    // Top Double: OB zone reached + Hull RSI confirms.
                    // detuned_pair_bell: staggered metallic pair — distinct "warning" character.
                    // Standard red (#FF4444), medium (5px).
                    new() { Name = CompTopDouble,
                            DisplayName = "Top Double",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF4444",
                            DefaultThickness = 5.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "detuned_pair_bell",
                            DefaultDecayMs = 280,
                            DefaultBaseFrequency = 420.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Cycle top double, confirmed sell",
                            DefaultUsePolarityColoring = false,
                            IsVisible = true },

                    // Top Single: OB zone only, no Hull RSI confirm.
                    // triangle_bell: dry hollow ring — audibly "lighter" than the pair bell.
                    // Light red (#FF6666), small (3px) — visually subordinate.
                    // Background layer keeps it from competing with Double/Triple.
                    new() { Name = CompTopSingle,
                            DisplayName = "Top Single",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF6666",
                            DefaultThickness = 3.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultSoundPatchId = "triangle_bell",
                            DefaultDecayMs = 180,
                            DefaultBaseFrequency = 420.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Cycle top single",
                            DefaultUsePolarityColoring = false,
                            IsVisible = true },

                    // ════════════════════════════════════════════════════════
                    // CYCLE SINE — main oscillator
                    // ════════════════════════════════════════════════════════

                    // Fisher-transformed stochastic oscillator, normalised to ≈±50.
                    // Above zero — triangle: angular "cresting" character at cycle peaks.
                    // Below zero — sine: smooth rounded "trough" character.
                    // Pink noise (0.15) adds oscillator "breath" texture matching Cipher B.
                    // TriggerBoundaryClick = true — earcon fires on zero-crossing.
                    new() { Name = CompCycleSine,
                            DisplayName = "Cycle Sine",
                            DisplayType = ComponentDisplayType.Oscillator,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#3D9BFF",
                            DefaultThickness = 2.0f,
                            DefaultWaveform = "sine",
                            DefaultAboveWaveform = "triangle",
                            DefaultBelowWaveform = "sine",
                            DefaultReferenceLevel = 0.0,
                            DefaultEnvelopeType = "Sustain",
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultNoiseAmount = 0.15f,
                            DefaultTriggerBoundaryClick = true,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultIsAreaFill = false,
                            DefaultUsePolarityColoring = false,
                            SpeechTemplate = "Cycle sine. {value:F1}.",
                            IsVisible = true },

                    // ════════════════════════════════════════════════════════
                    // BOTTOM TIER — three buy-signal dots, cyan family
                    // Ordered weakest→strongest so the last navigated component
                    // in a bottom cluster is the strongest signal (matching how
                    // Cipher B places the gold dot last in its dot sequence).
                    // ════════════════════════════════════════════════════════

                    // Bottom Single: OS zone only, no Hull RSI confirm.
                    // triangle_bell: dry hollow ring, low pitch.
                    // Light cyan (#66E8FF), small (3px), Background.
                    new() { Name = CompBottomSingle,
                            DisplayName = "Bottom Single",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#66E8FF",
                            DefaultThickness = 3.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultSoundPatchId = "triangle_bell",
                            DefaultDecayMs = 180,
                            DefaultBaseFrequency = 280.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Cycle bottom single",
                            DefaultUsePolarityColoring = false,
                            IsVisible = true },

                    // Bottom Double: OS zone + Hull RSI confirms.
                    // sine_bell: clean pure resonant tone — "arrival" quality.
                    // Standard cyan (#00E5FF), medium (5px), Foreground.
                    new() { Name = CompBottomDouble,
                            DisplayName = "Bottom Double",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#00E5FF",
                            DefaultThickness = 5.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "sine_bell",
                            DefaultDecayMs = 350,
                            DefaultBaseFrequency = 280.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Cycle bottom double, confirmed buy",
                            DefaultUsePolarityColoring = false,
                            IsVisible = true },

                    // Bottom Triple: deep OS extreme + Hull RSI confirms.
                    // dual_tone_bell at 320 Hz — deep golden chord for maximum resonance.
                    // Deep teal (#00B8D4), large (7px) — visually the anchor of a bottom.
                    new() { Name = CompBottomTriple,
                            DisplayName = "Bottom Triple",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#00B8D4",
                            DefaultThickness = 7.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "dual_tone_bell",
                            DefaultDecayMs = 500,
                            DefaultBaseFrequency = 320.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Cycle bottom triple, strong buy confirmation",
                            DefaultUsePolarityColoring = false,
                            IsVisible = true },

                    // ════════════════════════════════════════════════════════
                    // LEAD SINE — Hull MA signal line, phase reference
                    // ════════════════════════════════════════════════════════

                    // HullMA(CycleSine, SignalPeriod): lower lag than EMA; crossovers drive all dots.
                    // Ghost white at ~30% opacity. Color is #4DFFFFFF in SkiaSharp AARRGGBB format:
                    //   AA = 0x4D ≈ 30% opacity, RR/GG/BB = 0xFF = white.
                    // NOTE: #FFFFFF4D would be parsed as AA=FF (opaque) + BB=4D → yellow.
                    //   Always use AARRGGBB for semi-transparent component colors.
                    // FreqMultiplier = 1.2 — pitch 20% higher for subtle "leading" feel.
                    // TriggerBoundaryClick = false — suppress duplicate zero-crossing earcon.
                    // Background layer: context cue, does not compete with Sine or dot bells.
                    new() { Name = CompLeadSine,
                            DisplayName = "Lead Sine",
                            DisplayType = ComponentDisplayType.Oscillator,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#4DFFFFFF",
                            DefaultThickness = 1.5f,
                            DefaultWaveform = "triangle",
                            DefaultAboveWaveform = "triangle",
                            DefaultBelowWaveform = "sine",
                            DefaultReferenceLevel = 0.0,
                            DefaultEnvelopeType = "Sustain",
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultFreqMultiplier = 1.2,
                            DefaultNoiseAmount = 0.10f,
                            DefaultTriggerBoundaryClick = false,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultIsAreaFill = false,
                            DefaultUsePolarityColoring = false,
                            SpeechTemplate = "Lead sine. {value:F1}.",
                            IsVisible = true },

                    // ════════════════════════════════════════════════════════
                    // FAILURE SIGNALS — trend continuation
                    // Fire when a crossover occurs WITHOUT the oscillator having
                    // reached OB/OS — the cycle failed to complete its swing.
                    // ════════════════════════════════════════════════════════

                    // Shallow Peak (bear): falling cross, never reached OB.
                    // = Up-cycle failed → bear trend continuing.
                    // Orange (#FF8C00): visually reads as a caution/continuation marker,
                    // distinct from the red sell family. Matches Cipher B's Hidden Bear
                    // color family for conceptual consistency (hidden/structural signals).
                    // triangle_bell at 260 Hz: slightly lower than Bottom Single (280 Hz) —
                    // the pitch communicates "sinking further" in the bear trend.
                    // Midground layer: not as urgent as Foreground bells, but more prominent
                    // than Background — these are secondary trend signals, not primary entries.
                    new() { Name = CompShallowPeak,
                            DisplayName = "Shallow Peak",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF8C00",
                            DefaultThickness = 5.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultSoundPatchId = "triangle_bell",
                            DefaultDecayMs = 220,
                            DefaultBaseFrequency = 260.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Shallow peak, bear trend continuation",
                            DefaultUsePolarityColoring = false,
                            IsVisible = true },

                    // Shallow Trough (bull): rising cross, never reached OS.
                    // = Down-cycle failed → bull trend continuing.
                    // Lime (#AAFF00): visually reads as a growth/continuation marker,
                    // distinct from the cyan buy family. The bright yellow-green is
                    // unmistakable against the dark pane background.
                    // triangle_bell at 480 Hz: higher than Top Single (420 Hz) — the pitch
                    // communicates "bouncing back up" while the dry triangle timbre keeps
                    // it clearly separate from sine_bell/dual_tone_bell buy signals.
                    new() { Name = CompShallowTrough,
                            DisplayName = "Shallow Trough",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#AAFF00",
                            DefaultThickness = 5.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultSoundPatchId = "triangle_bell",
                            DefaultDecayMs = 220,
                            DefaultBaseFrequency = 480.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Shallow trough, bull trend continuation",
                            DefaultUsePolarityColoring = false,
                            IsVisible = true },
                },

                DefaultCloudFills = new List<CloudFillConfig>
                {
                    // Cycle Fill: semi-transparent channel between CycleSine and LeadSine.
                    // Bullish (Sine > Lead): #3000E5FF — cyan at ~19% opacity (AARRGGBB).
                    // Bearish (Lead > Sine): #30FF4444 — coral at ~19% opacity.
                    // Lower opacity than Cipher B's WT Fill (35%) — fill is rhythmic
                    // context, not a primary visual element.
                    // Audio: 380/180 Hz pair avoids clashing with all 8 dot frequencies.
                    new()
                    {
                        UpperComponentName = CompCycleSine,
                        LowerComponentName = CompLeadSine,
                        BullishColorHex    = "#3000E5FF",
                        BearishColorHex    = "#30FF4444",
                        DisplayName        = "Cycle Fill",
                        IsVisible          = true,
                        Sonification       = new CloudSonificationConfig(
                            BullishFrequency: 380f,
                            BearishFrequency: 180f,
                            SoundPatchId:     "sine_bell",
                            DecayMs:          200,
                            MaxVolume:        0.40f)
                    }
                },

                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "CyclePeriod",  DisplayName = "Cycle Period",
                            DataType = typeof(int), DefaultValue = 10.0,
                            Description = "Ehlers Cyber Cycle bandpass alpha, stochastic normalization lookback, " +
                                          "and RSI period. Lower = faster cycle, more signals; higher = smoother, fewer." },
                    new() { Name = "SmoothPeriod", DisplayName = "Smooth Period",
                            DataType = typeof(int), DefaultValue = 3.0,
                            Description = "Post-filter EMA smoothing on cycle output (1 = raw cycle, most responsive). " +
                                          "Increase to reduce noise after the bandpass filter." },
                    new() { Name = "SignalPeriod", DisplayName = "Signal Period",
                            DataType = typeof(int), DefaultValue = 3.0,
                            Description = "Hull MA period for the Lead Sine signal line (lower lag than EMA). " +
                                          "Crossovers between Cycle Sine and Lead Sine drive all signals." },
                    new() { Name = "OBLevel",      DisplayName = "OB/OS Level",
                            DataType = typeof(double), DefaultValue = 75.0,
                            Description = "Overbought/oversold threshold. Standard Fisher Transform OB/OS " +
                                          "is raw ±1.5, which equals ±75 at our scale. " +
                                          "Triple tier fires at OBLevel + 15 (default 90). " +
                                          "Lower for more signals; raise for fewer, higher-confidence signals." },
                }
            }
        };

        // ── Calculation ───────────────────────────────────────────────────────

        public void Calculate(
            string code,
            ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters,
            IIndicatorResultBuffer buffer)
        {
            if (!code.Equals("CIPHER_C", StringComparison.OrdinalIgnoreCase)) return;
            int n = data.Length;
            if (n < 20) return;

            int    cyclePeriod  = GetInt(parameters, "CyclePeriod",  10);
            int    smoothPeriod = GetInt(parameters, "SmoothPeriod", 3);
            int    signalPeriod = GetInt(parameters, "SignalPeriod", 3);
            double obLevel      = GetDbl(parameters, "OBLevel",      75.0);
            double extremeLevel = obLevel + 15.0;  // Triple tier threshold

            // ── Source ───────────────────────────────────────────────────────
            var close = new double[n];
            for (int i = 0; i < n; i++) close[i] = data[i].Close;

            // ── Step 1: Ehlers 4-bar weighted smooth ─────────────────────────
            // (P + 2P[1] + 2P[2] + P[3]) / 6 — fixed minimal-lag pre-smooth.
            // Needs 4 bars; bars 0-2 default to close (no prior context).
            var smooth4 = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (i < 3)
                {
                    smooth4[i] = close[i];
                }
                else
                {
                    smooth4[i] = (close[i] + 2.0 * close[i-1] + 2.0 * close[i-2] + close[i-3]) / 6.0;
                }
            }

            // ── Step 2: Ehlers Cyber Cycle bandpass filter ───────────────────
            // Rejects the trend component; passes only the dominant cycle band.
            //   alpha = 2 / (CyclePeriod + 1)
            //   Cycle[i] = (1-0.5α)²(S-2S[1]+S[2]) + 2(1-α)Cycle[1] - (1-α)²Cycle[2]
            // Warm-up: when Cycle[i-1] or [i-2] is NaN, seed with first-difference.
            double alpha = 2.0 / (cyclePeriod + 1.0);
            double a1    = 1.0 - 0.5 * alpha;
            double a2    = 1.0 - alpha;
            var cycle = IndicatorMath.NanArray(n);
            for (int i = 2; i < n; i++)
            {
                double c1 = double.IsNaN(cycle[i-1]) ? smooth4[i-1] - smooth4[i-2] : cycle[i-1];
                double c2 = double.IsNaN(cycle[i-2]) ? smooth4[i-2] - (i >= 3 ? smooth4[i-3] : smooth4[i-2]) : cycle[i-2];
                cycle[i] = a1 * a1 * (smooth4[i] - 2.0 * smooth4[i-1] + smooth4[i-2])
                         + 2.0 * a2 * c1
                         - a2  * a2 * c2;
            }

            // ── Step 3: Post-filter EMA (optional smoothing) ─────────────────
            // smoothPeriod = 1 → raw cycle (most responsive).
            // smoothPeriod > 1 → EMA further reduces noise after the bandpass.
            var smoothCycle = smoothPeriod > 1 ? IndicatorMath.Ema(cycle, smoothPeriod) : cycle;

            // ── Step 4: Stochastic + Fisher Transform → CycleSine ────────────
            var cycleSine = IndicatorMath.NanArray(n);
            for (int i = cyclePeriod - 1; i < n; i++)
            {
                if (double.IsNaN(smoothCycle[i])) continue;
                double highest = double.MinValue, lowest = double.MaxValue;
                bool hasNan = false;
                for (int j = i - cyclePeriod + 1; j <= i; j++)
                {
                    if (double.IsNaN(smoothCycle[j])) { hasNan = true; break; }
                    if (smoothCycle[j] > highest) highest = smoothCycle[j];
                    if (smoothCycle[j] < lowest)  lowest  = smoothCycle[j];
                }
                if (hasNan) continue;
                double range = highest - lowest;
                double stoch = range > 1e-10 ? (smoothCycle[i] - lowest) / range : 0.5;
                double value = Math.Max(-0.999, Math.Min(0.999, 2.0 * stoch - 1.0));
                // Fisher saturation correction: near the ±1 boundaries, raw Fisher jumps
                // exponentially — stoch 0.95 → raw ~1.47, stoch 0.99 → raw ~2.65.
                // After clamping to ±100 this collapses all extreme tails to the same value,
                // hiding the difference between "reached 95th pct" and "pinned at 99th pct".
                // Amplifying the raw value when stoch is in the tail (>0.90 or <0.10) before
                // clamping preserves the separation between true tail events and the rest.
                double raw = 0.5 * Math.Log((1.0 + value) / (1.0 - value)) * FisherScale;
                if (stoch > 0.90 || stoch < 0.10)
                {
                    double tailBoost = Math.Sqrt(Math.Max(0.0, Math.Abs(stoch - 0.5) - 0.40));
                    raw *= 1.0 + tailBoost;
                }
                cycleSine[i] = Math.Max(-100.0, Math.Min(100.0, raw));
            }

            // ── ADX for Shallow Peak/Trough gating ───────────────────────────
            // Shallow continuation signals (cycle crosses back without reaching OB/OS)
            // are only meaningful in range-bound regimes — in a strong trend, every
            // counter-swing fails to reach the opposite extreme, producing constant
            // shallow noise. Gate by ADX: only fire Shallow in low-ADX (range-bound) regimes.
            var adxSeries = IndicatorMath.Adx(data, Math.Max(7, cyclePeriod));

            // ── Step 5: Lead Sine — Hull MA (lower lag than EMA) ─────────────
            // Hull MA's de-lagging formula (2×WMA(n/2) − WMA(n)) can overshoot
            // the bounds of its input. We keep two versions:
            //   leadSine        — unclamped, used for crossover detection so that
            //                     Hull MA's deliberate overshoot (its lead mechanism)
            //                     is preserved for accurate signal timing.
            //   leadSineDisplay — clamped to ±100, written to the buffer for visual
            //                     rendering so the line never exceeds the oscillator
            //                     boundary on screen.
            var leadSine = HullMa(cycleSine, Math.Max(2, signalPeriod));
            var leadSineDisplay = (double[])leadSine.Clone();
            for (int i = 0; i < n; i++)
                if (!double.IsNaN(leadSineDisplay[i]))
                    leadSineDisplay[i] = Math.Max(-100.0, Math.Min(100.0, leadSineDisplay[i]));

            // ── Step 6: Hull RSI ──────────────────────────────────────────────
            var rsi     = IndicatorMath.Rsi(close, cyclePeriod);
            var hullRsi = HullMa(rsi, Math.Max(2, cyclePeriod / 2));

            // ── Step 7: Signal classification ────────────────────────────────
            //
            // Every Sine/Lead crossover fires EXACTLY ONE output component.
            // State tracks: has OB/OS been touched since the last opposite crossover?
            //   reachedOB = true → the up-swing DID reach OB → top tier signals eligible
            //   reachedOB = false → the up-swing FAILED to reach OB → Shallow Peak fires
            //   (same logic inverted for reachedOS / bottom tier / Shallow Trough)
            //
            // maxSine / minSine track the peak/trough depth of the current half-cycle,
            // used to classify Single vs Double vs Triple without relying on the single
            // sp value (which may be retreating from the peak at crossover time).
            //
            // Warmup guard: signals are suppressed for the first stabilityStart bars.
            // The Ehlers bandpass filter's recursive seed values produce artificially
            // extreme cycleSine readings in the first ~2 cycles (transient response).
            // Running the state machine through warmup is intentional — it ensures
            // reachedOB/reachedOS/maxSine/minSine reflect actual market behaviour
            // from bar 1. However, state is RESET at stabilityStart so that transient
            // extremes accumulated during warmup do not inflate the depth classification
            // of the first real post-warmup signal.

            int stabilityStart = cyclePeriod * 4 + signalPeriod * 2 + 20;

            var topTriple     = IndicatorMath.NanArray(n);
            var topDouble     = IndicatorMath.NanArray(n);
            var topSingle     = IndicatorMath.NanArray(n);
            var bottomSingle  = IndicatorMath.NanArray(n);
            var bottomDouble  = IndicatorMath.NanArray(n);
            var bottomTriple  = IndicatorMath.NanArray(n);
            var shallowPeak   = IndicatorMath.NanArray(n);
            var shallowTrough = IndicatorMath.NanArray(n);

            bool   reachedOB = true;              // assume prior up-cycle completed
            bool   reachedOS = true;              // assume prior down-cycle completed
            double maxSine   = double.MinValue;   // peak since last rising cross
            double minSine   = double.MaxValue;   // trough since last falling cross

            for (int i = 1; i < n; i++)
            {
                double s  = cycleSine[i];
                double sp = cycleSine[i - 1];
                double l  = leadSine[i];
                double lp = leadSine[i - 1];
                double hr = hullRsi[i];
                if (double.IsNaN(s) || double.IsNaN(sp) ||
                    double.IsNaN(l) || double.IsNaN(lp)) continue;

                // At the stability boundary, reset depth-tracking state so warmup
                // transients (artificially extreme bandpass values) do not contaminate
                // the depth classification of the first real post-warmup signal.
                if (i == stabilityStart)
                {
                    reachedOB = true;
                    reachedOS = true;
                    maxSine   = s;
                    minSine   = s;
                }

                // Continuously track extremes for the current half-cycle
                if (s > maxSine) maxSine = s;
                if (s < minSine) minSine = s;
                if (s >  obLevel) reachedOB = true;
                if (s < -obLevel) reachedOS = true;

                bool risingCross  = sp <= lp && s > l;
                bool fallingCross = sp >= lp && s < l;
                bool hullBearish  = !double.IsNaN(hr) && hr > 50.0;
                bool hullBullish  = !double.IsNaN(hr) && hr < 50.0;

                // Range-bound gate: ADX < 20 means no dominant trend, so shallow
                // counter-swing signals are informative. ADX ≥ 20 suppresses them.
                bool rangeBound = double.IsNaN(adxSeries[i]) || adxSeries[i] < 20.0;

                if (risingCross)
                {
                    // Only emit signals after the Ehlers bandpass filter has settled.
                    if (i >= stabilityStart)
                    {
                        if (!reachedOS)
                        {
                            // Down-cycle never reached OS → bear trend failed → bull continuation
                            if (rangeBound)
                                shallowTrough[i] = s;
                        }
                        else
                        {
                            // Down-cycle completed; classify by depth (minSine) + Hull RSI
                            bool deep = minSine < -extremeLevel;
                            if (deep && hullBullish)  bottomTriple[i] = s;
                            else if (hullBullish)     bottomDouble[i] = s;
                            else                      bottomSingle[i] = s;
                        }
                    }
                    // Reset state for the new up-swing regardless of warmup
                    reachedOB = false;
                    reachedOS = false;
                    maxSine   = s;
                    minSine   = s;
                }

                if (fallingCross)
                {
                    // Only emit signals after the Ehlers bandpass filter has settled.
                    if (i >= stabilityStart)
                    {
                        if (!reachedOB)
                        {
                            // Up-cycle never reached OB → bull trend failed → bear continuation
                            if (rangeBound)
                                shallowPeak[i] = s;
                        }
                        else
                        {
                            // Up-cycle completed; classify by depth (maxSine) + Hull RSI
                            bool deep = maxSine > extremeLevel;
                            if (deep && hullBearish)  topTriple[i] = s;
                            else if (hullBearish)     topDouble[i] = s;
                            else                      topSingle[i] = s;
                        }
                    }
                    // Reset state for the new down-swing regardless of warmup
                    reachedOB = false;
                    reachedOS = false;
                    maxSine   = s;
                    minSine   = s;
                }
            }

            // ── Write to buffer ───────────────────────────────────────────────
            WriteToBuffer(buffer, CompTopTriple,     topTriple,     n);
            WriteToBuffer(buffer, CompTopDouble,     topDouble,     n);
            WriteToBuffer(buffer, CompTopSingle,     topSingle,     n);
            WriteToBuffer(buffer, CompCycleSine,     cycleSine,     n);
            WriteToBuffer(buffer, CompBottomSingle,  bottomSingle,  n);
            WriteToBuffer(buffer, CompBottomDouble,  bottomDouble,  n);
            WriteToBuffer(buffer, CompBottomTriple,  bottomTriple,  n);
            WriteToBuffer(buffer, CompLeadSine,      leadSineDisplay, n);
            WriteToBuffer(buffer, CompShallowPeak,   shallowPeak,   n);
            WriteToBuffer(buffer, CompShallowTrough, shallowTrough, n);
        }

        public void UpdateLast(
            string code,
            ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters,
            IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            int cyclePeriod  = GetInt(parameters, "CyclePeriod",  10);
            int signalPeriod = GetInt(parameters, "SignalPeriod", 3);
            // Ehlers bandpass needs ~2× the old warmup; Hull MA on Lead Sine adds signalPeriod×2.
            // Default: 10*4 + 3*2 + 20 = 66 bars.
            return cyclePeriod * 4 + signalPeriod * 2 + 20;
        }

        public string GetDetailFact(
            string code,
            ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> results,
            int index,
            Dictionary<string, object> parameters)
        {
            if (!code.Equals("CIPHER_C", StringComparison.OrdinalIgnoreCase) || index < 0)
                return string.Empty;

            double obLevel   = GetDbl(parameters, "OBLevel", 75.0);
            double extreme   = obLevel + 15.0;
            double sineVal   = GetVal(results, CompCycleSine,     index);
            double leadVal   = GetVal(results, CompLeadSine,      index);

            // Dot values — only one fires per bar
            double tTriple   = GetVal(results, CompTopTriple,     index);
            double tDouble   = GetVal(results, CompTopDouble,     index);
            double tSingle   = GetVal(results, CompTopSingle,     index);
            double bTriple   = GetVal(results, CompBottomTriple,  index);
            double bDouble   = GetVal(results, CompBottomDouble,  index);
            double bSingle   = GetVal(results, CompBottomSingle,  index);
            double sPeak     = GetVal(results, CompShallowPeak,   index);
            double sTrough   = GetVal(results, CompShallowTrough, index);

            var sb = new StringBuilder();

            // Sentence 1: current cycle position and zone
            if (!double.IsNaN(sineVal))
            {
                string zone = sineVal >  extreme  ? ", extreme overbought"
                            : sineVal >  obLevel  ? ", overbought"
                            : sineVal < -extreme  ? ", extreme oversold"
                            : sineVal < -obLevel  ? ", oversold"
                            :                       "";
                string dir  = sineVal >= 0 ? "above zero" : "below zero";
                sb.Append($"Cycle sine {dir}{zone} at {sineVal:F1}. ");
            }

            // Sentence 2: phase relationship
            if (!double.IsNaN(sineVal) && !double.IsNaN(leadVal))
            {
                sb.Append(sineVal > leadVal
                    ? "Rising phase, sine above lead. "
                    : "Falling phase, sine below lead. ");
            }

            // Sentence 3: any active signal at this bar
            if      (!double.IsNaN(tTriple))  sb.Append("Top triple, strong sell confirmation. ");
            else if (!double.IsNaN(tDouble))  sb.Append("Top double, confirmed sell. ");
            else if (!double.IsNaN(tSingle))  sb.Append("Top single. ");
            if      (!double.IsNaN(bTriple))  sb.Append("Bottom triple, strong buy confirmation. ");
            else if (!double.IsNaN(bDouble))  sb.Append("Bottom double, confirmed buy. ");
            else if (!double.IsNaN(bSingle))  sb.Append("Bottom single. ");
            if (!double.IsNaN(sPeak))   sb.Append("Shallow peak, bear trend continuation. ");
            if (!double.IsNaN(sTrough)) sb.Append("Shallow trough, bull trend continuation. ");

            return sb.Length > 0 ? sb.ToString().TrimEnd() : string.Empty;
        }

        /// <summary>
        /// Provides contextual speech for the two oscillator line components (Cycle Sine and
        /// Lead Sine). Dot signal components return null so their <c>DefaultSignalSpeechTemplate</c>
        /// handles the non-NaN case and the generic formatter handles the NaN (no-signal) case.
        ///
        /// NOTE: OB/OS thresholds are hardcoded to the default 75/90 because the interface does
        /// not carry the parameters dictionary. If the user changes <c>OBLevel</c>, the speech
        /// zone labels will still reference ±75/±90. This matches the same limitation on
        /// <see cref="GetDefaultLevels"/>.
        /// </summary>
        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            return componentName switch
            {
                CompCycleSine => BuildCycleSineSpeech(value, allComponentData, dataIndex),
                CompLeadSine  => BuildLeadSineSpeech(value, allComponentData, dataIndex),
                _             => null, // dots: fall through to DefaultSignalSpeechTemplate / generic
            };
        }

        private static string BuildCycleSineSpeech(double value,
            IReadOnlyDictionary<string, double[]> data, int idx)
        {
            if (double.IsNaN(value)) return "no data";

            // Zone label — hardcoded to default thresholds (75 OB, 90 extreme).
            string zone = value >  90.0 ? "extreme overbought"
                        : value >  75.0 ? "overbought"
                        : value < -90.0 ? "extreme oversold"
                        : value < -75.0 ? "oversold"
                        :                 "neutral";

            // Phase relative to Lead Sine.
            double lead     = GetVal(data, CompLeadSine, idx);
            string phase    = double.IsNaN(lead) ? "" :
                              value > lead ? "Rising phase." : "Falling phase.";

            return $"{zone}, {value:F1}. {phase}".TrimEnd();
        }

        private static string BuildLeadSineSpeech(double value,
            IReadOnlyDictionary<string, double[]> data, int idx)
        {
            if (double.IsNaN(value)) return "no data";

            double sine  = GetVal(data, CompCycleSine, idx);
            string rel   = double.IsNaN(sine) ? "" :
                           sine > value ? "Sine above, rising phase." : "Sine below, falling phase.";

            return $"{value:F1}. {rel}".TrimEnd();
        }

        public List<LevelDescriptor> GetDefaultLevels(string code)
        {
            if (!code.Equals("CIPHER_C", StringComparison.OrdinalIgnoreCase)) return new();
            return new()
            {
                // Reference lines calibrated to Fisher Transform scale (×50):
                //   ±90 = raw ±1.8 = stoch ~95th pct → Triple tier extreme
                //   ±75 = raw ±1.5 = stoch ~92nd pct → standard OB/OS (default OBLevel)
                //   0   = zero crossing
                new("Extreme OB",  90.0, "#FF2222", DashStyle.Dot,
                    PlayEarcon: true, EarconVolume: 0.8f, ZoneNoiseAmount: 0.20f, ZoneNoiseType: "white"),
                new("Overbought",  75.0, "#FF6666", DashStyle.Dash,
                    PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.10f, ZoneNoiseType: "white"),
                new("Zero",         0.0, "#666666", DashStyle.Dash,
                    PlayEarcon: true, EarconVolume: 0.7f),
                new("Oversold",   -75.0, "#66BB66", DashStyle.Dash,
                    PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.10f, ZoneNoiseType: "white"),
                new("Extreme OS", -90.0, "#22FF22", DashStyle.Dot,
                    PlayEarcon: true, EarconVolume: 0.8f, ZoneNoiseAmount: 0.20f, ZoneNoiseType: "white"),
            };
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>Hull MA = WMA(2×WMA(n/2) − WMA(n), √n). Lower lag than EMA/SMA.</summary>
        private static double[] HullMa(double[] src, int period)
        {
            if (period < 2) period = 2;
            int halfPeriod = Math.Max(2, period / 2);
            int sqrtPeriod = Math.Max(2, (int)Math.Round(Math.Sqrt(period)));
            var wmaHalf = Wma(src, halfPeriod);
            var wmaFull = Wma(src, period);
            var diff    = new double[src.Length];
            for (int i = 0; i < src.Length; i++)
                diff[i] = double.IsNaN(wmaHalf[i]) || double.IsNaN(wmaFull[i])
                    ? double.NaN : 2.0 * wmaHalf[i] - wmaFull[i];
            return Wma(diff, sqrtPeriod);
        }

        /// <summary>Weighted Moving Average. Returns NaN during warmup and on NaN input.</summary>
        private static double[] Wma(double[] src, int period)
        {
            if (period < 1) period = 1;
            var    r     = new double[src.Length];
            double denom = period * (period + 1.0) / 2.0;
            for (int i = 0; i < src.Length; i++)
            {
                if (i < period - 1) { r[i] = double.NaN; continue; }
                double sum = 0.0; bool hasNan = false;
                for (int j = 0; j < period; j++)
                {
                    double v = src[i - j];
                    if (double.IsNaN(v)) { hasNan = true; break; }
                    sum += v * (period - j);
                }
                r[i] = hasNan ? double.NaN : sum / denom;
            }
            return r;
        }

        private static void WriteToBuffer(IIndicatorResultBuffer buffer, string name, double[] data, int n)
        {
            var span = buffer.GetComponentSpan(name);
            int len  = Math.Min(span.Length, n);
            for (int i = 0; i < len; i++) span[i] = data[i];
        }

        private static double GetVal(IReadOnlyDictionary<string, double[]> r, string key, int idx)
        {
            if (!r.TryGetValue(key, out var arr) || arr == null || idx >= arr.Length) return double.NaN;
            return arr[idx];
        }

        private static int    GetInt(Dictionary<string, object> p, string k, int    def) =>
            p.TryGetValue(k, out var v) ? (int)Convert.ToDouble(v) : def;
        private static double GetDbl(Dictionary<string, object> p, string k, double def) =>
            p.TryGetValue(k, out var v) ? Convert.ToDouble(v) : def;
    }
}
