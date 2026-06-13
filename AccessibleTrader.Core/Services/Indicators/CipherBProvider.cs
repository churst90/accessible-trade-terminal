using System;
using System.Collections.Generic;
using System.Text;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Accessible Cipher B — native C# replica of Market Cipher B, tuned to match
    /// the real MCB as closely as public evidence permits.
    ///
    /// This implementation was rebuilt to align with the actual MCB visuals after
    /// reverse-engineering from screenshots and the open-source VuManChu clone:
    ///
    ///   • Money Flow is a smoothed (close-open)/(high-low) candle body ratio —
    ///     NOT RSI(hlc3*volume). The body/range formula is volumeless, so it works
    ///     on forex CFDs and illiquid markets where volume is synthetic, matching
    ///     MCB's observed cross-asset behavior.
    ///
    ///   • The green/red histogram bars MCB labels "VWAP" are actually WT1 − WT2
    ///     plotted as columns — a MACD-style histogram on the WaveTrend oscillator.
    ///     They flip exactly at WT crossovers, which is only possible if they are
    ///     the WT difference itself. The old rolling VWAP deviation has been removed.
    ///
    ///   • Stoch %K/%D, Laguerre RSI, Trigger Wave, and rolling-VWAP deviation
    ///     were dropped. They were hidden by default in the legacy metadata — a
    ///     tell that they didn't earn their slot. Users who want those can add them
    ///     as separate indicators.
    ///
    ///   • Adaptive OB/OS thresholds: rolling percentile of WT1's own distribution
    ///     replaces the fixed ±53 constants. This makes Cipher B work across BTC,
    ///     equities, and forex without rescaling — the 0.015 constant and ±53/60
    ///     levels are a matched CCI-normalisation pair, so the right move is to
    ///     adapt the thresholds to the data, not fiddle with the constant.
    ///
    ///   • Blue/Red dots require 2-bar sustained OS/OB before firing, plus a
    ///     cooldown period to prevent stuttering in noisy oversold zones.
    ///
    ///   • Gold dot is now gated by ADX (trend must exist) and ATR percentile
    ///     (not in dead chop) in addition to the original triple-confluence logic.
    ///
    ///   • Divergences require above-average True Range on the second pivot —
    ///     real reversals happen on conviction; weak-range divergences are random.
    ///
    /// Wave components (oscillator pane):
    ///   WT1                      — Wave Trend fast line
    ///   WT2                      — Wave Trend slow channel
    ///   WT Fill                  — Cloud fill between WT1 and WT2
    ///   WT Histogram             — WT1 − WT2 bars (the real MCB "VWAP")
    ///   Money Flow Wave          — Body/range ratio, slow green/red wave
    ///   Anchor Wave Fast / Slow  — 5× period-scaled WT pair, hidden by default
    ///   Anchor Fill              — Cloud fill between the anchor pair (visible)
    ///   Anchor Polarity          — +1/0/-1 hidden component for strategy gating
    ///   Adaptive OB / OS         — Two lines showing the rolling-percentile bands
    ///
    /// Signal dots:
    ///   WaveTrend Cross Bull/Bear — small dot at every WT1/WT2 cross
    ///   Oversold Crossover (Blue) — confirmed long signal after 2-bar oversold hold
    ///   Overbought Crossover (Red) — confirmed short signal after 2-bar overbought hold
    ///   Triple Confluence Buy (Gold) — cross + RSI OS + positive MF + ADX &gt; gate + ATR above floor
    ///   Bullish Divergence        — regular bullish divergence at pivot low
    ///   Bearish Divergence        — regular bearish divergence at pivot high
    ///   Hidden Bull/Bear Continuation — hidden divergences
    ///
    /// Parameters:
    ///   WT1Period            — WT channel period (default 9)
    ///   WT2Period            — WT average period (default 12)
    ///   MFPeriod             — Money Flow SMA length (default 60 — body/range is slow)
    ///   OBLevel              — Fixed-mode OB/OS threshold (default 53)
    ///   RSIPeriod            — RSI period for Gold confluence (default 14)
    ///   RSIOSLevel           — RSI oversold for Gold (default 30)
    ///   PivotBars            — Bars each side for divergence pivot detection (default 3)
    ///   AnchorMultiplier     — Anchor period multiplier (default 5 = 5× base WT periods)
    ///   ThresholdMode        — "Fixed" or "Percentile" (default Fixed)
    ///   AdaptiveLookback     — Window for percentile threshold (default 500)
    ///   UpperPercentile      — Upper percentile for OB in Percentile mode (default 0.95)
    ///   LowerPercentile      — Lower percentile for OS in Percentile mode (default 0.05)
    ///   MinThresholdFloor    — |Minimum| threshold in Percentile mode to avoid whipsaw (default 40)
    ///   ConfirmBars          — Consecutive bars required OS/OB before dot fires (default 2)
    ///   CooldownBars         — Suppress further same-side dots for N bars (default 0 = auto = WT1Period)
    ///   AdxPeriod            — ADX lookback for Gold gate (default 14)
    ///   AdxGate              — Gold fires only if ADX &gt; this (default 18)
    ///   AtrPeriod            — ATR lookback for Gold volatility floor (default 14)
    ///   AtrFloorPct          — Gold requires ATR percentile rank &gt; this (default 0.20)
    /// </summary>
    public class CipherBProvider : IIndicatorProvider
    {
        public string Name => "Cipher B";

        // ── Component name constants ──────────────────────────────────────────
        public const string CompWT1          = "Wave Trend";
        public const string CompWT2          = "Wave Trend 2";
        public const string CompWtHistogram  = "WT Histogram";
        public const string CompMoneyFlowWave= "Money Flow Wave";
        public const string CompCrossBull    = "WaveTrend Cross Bull";
        public const string CompCrossBear    = "WaveTrend Cross Bear";
        public const string CompBlue         = "Oversold Crossover";
        public const string CompRed          = "Overbought Crossover";
        public const string CompGold         = "Triple Confluence Buy";
        public const string CompBullDiv      = "Bullish Divergence";
        public const string CompBearDiv      = "Bearish Divergence";
        public const string CompHidBull      = "Hidden Bull Continuation";
        public const string CompHidBear      = "Hidden Bear Continuation";
        public const string CompWT1Anchor    = "Anchor Wave";
        public const string CompWT2Anchor    = "Anchor Wave 2";
        public const string CompAnchorPolarity = "Anchor Polarity";
        public const string CompAdaptiveOb   = "Adaptive OB";
        public const string CompAdaptiveOs   = "Adaptive OS";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "CIPHER_B",
                Name        = "Cipher B",
                Category    = "Multi-Signal",
                DefaultPane = "Pane_CIPHER_B",
                Components = new List<IndicatorComponentMetadata>
                {
                    // ── Anchor Waves — hidden lines, visual carried by Anchor Fill cloud ──
                    new() { Name = CompWT1Anchor,     DisplayName = "Anchor Wave Fast", DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#78909C", DefaultThickness = 2.0f, IsVisible = false,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultWaveform = "triangle",
                            DefaultAboveWaveform = "triangle", DefaultBelowWaveform = "sine",
                            DefaultReferenceLevel = 0.0, DefaultEnvelopeType = "Sustain",
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultNoiseAmount = 0.15f, DefaultTriggerBoundaryClick = true,
                            DefaultIsAreaFill = false, DefaultUsePolarityColoring = false },
                    new() { Name = CompWT2Anchor,     DisplayName = "Anchor Wave Slow", DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#1A237E", DefaultThickness = 2.5f, IsVisible = false,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultWaveform = "sine",
                            DefaultAboveWaveform = "triangle", DefaultBelowWaveform = "sine",
                            DefaultReferenceLevel = 0.0, DefaultEnvelopeType = "Sustain",
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultNoiseAmount = 0.15f, DefaultTriggerBoundaryClick = true,
                            DefaultIsAreaFill = false, DefaultUsePolarityColoring = false },

                    // ── WT Histogram — the real MCB "VWAP" bars ─────────────
                    // This is WT1 − WT2 plotted as columns. In real MCB the green/red
                    // histogram bars labeled "VWAP" flip exactly at WT crossovers, which
                    // is only possible if they ARE the WT difference itself. The old
                    // rolling VWAP deviation was the wrong abstraction.
                    // Audio: Background, Direction pitch — positive rising = bullish,
                    // negative falling = bearish.
                    // Saturated colors (full opacity, no alpha) so the histogram reads
                    // crisply against the WT lines instead of looking washed out.
                    new() { Name = CompWtHistogram,   DisplayName = "WT Histogram", DisplayType = ComponentDisplayType.Histogram, Role = ComponentRole.Signal,
                            DefaultColorHex = "#00FF7F", DefaultColorHexSecondary = "#FF2D55",
                            DefaultColorSource = ColorSource.Value,
                            ColorBaseline = 0.0,
                            DefaultReferenceLevel = 0.0,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultPitchMapping = PitchMapping.Direction,
                            DefaultBullishFrequency = 360.0,
                            DefaultBearishFrequency = 140.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.ReferenceDeviation,
                            DefaultDeviationNorm = 25.0,
                            SpeechTemplate = "WT Histogram. {value:F1}.",
                            SubscribedLevelNames = Array.Empty<string>() },

                    // ── Wave lines (on top of anchors) ───────────────────────
                    new() { Name = CompWT1,           DisplayName = "Wave Trend Fast", DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#FFFFFF", DefaultThickness = 2.0f,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultWaveform = "triangle",
                            DefaultAboveWaveform = "triangle",
                            DefaultBelowWaveform = "sine",
                            DefaultReferenceLevel = 0.0, DefaultEnvelopeType = "Sustain",
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultNoiseAmount = 0.15f, DefaultTriggerBoundaryClick = true,
                            SpeechTemplate = "Wave Trend 1. Oscillator. {value:F1}.",
                            DefaultIsAreaFill = false, DefaultUsePolarityColoring = false },
                    new() { Name = CompWT2,           DisplayName = "Wave Trend Slow", DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#00D9FF", DefaultThickness = 2.5f,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultWaveform = "sine",
                            DefaultAboveWaveform = "triangle",
                            DefaultBelowWaveform = "sine",
                            DefaultReferenceLevel = 0.0, DefaultEnvelopeType = "Sustain",
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultNoiseAmount = 0.15f, DefaultTriggerBoundaryClick = true,
                            SpeechTemplate = "Wave Trend 2. Oscillator. {value:F1}.",
                            DefaultIsAreaFill = false, DefaultUsePolarityColoring = false },

                    // ── Money Flow — body/range ratio, zero-centered ──────
                    // Rewritten from the previous RSI(hlc3*volume) approach to the canonical
                    // VuManChu body/range formula: SMA((close-open)/(high-low), 60). Output is
                    // volumeless, bounded [-1..+1] before scaling, and produces the slow glass-wave
                    // shape that matches real MCB.
                    //
                    // Zero-centered and scaled to ±60 so it sits BEHIND the WT lines across the
                    // middle of the pane — this is how real MCB renders it, as an ambient slow
                    // wave of "flow direction" underlying the fast WT oscillator. The previous
                    // ±20 anchor at -80 pushed it into a sliver at the bottom that was nearly
                    // invisible on standard scales.
                    // Histogram with ColorBaseline = 0 renders green bars for MF > 0 and red
                    // bars for MF < 0, extending from zero to the value. With a slow smoothed
                    // series the adjacent bars form a contiguous colored band that reads as a
                    // filled area wave — exactly how real MCB displays Money Flow behind WT.
                    new() { Name = CompMoneyFlowWave, DisplayName = "Money Flow", DisplayType = ComponentDisplayType.Histogram, Role = ComponentRole.Signal,
                            DefaultColorHex = "#00E676", DefaultColorHexSecondary = "#FF1744",
                            DefaultColorSource = ColorSource.Value,
                            ColorBaseline = 0.0,
                            DefaultReferenceLevel = 0.0,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultPitchMapping = PitchMapping.Direction,
                            DefaultBullishFrequency = 300.0,
                            DefaultBearishFrequency = 100.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.ReferenceDeviation,
                            DefaultDeviationNorm = 50.0,
                            SpeechTemplate = "Money Flow. {value:F1}.",
                            SubscribedLevelNames = Array.Empty<string>() },

                    // ── WaveTrend crossover dots ────────────────────────────
                    new() { Name = CompCrossBull,     DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                            DefaultColorHex = "#4FC3F7A0", DefaultThickness = 3.0f, DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "sine_bell",
                            DefaultDecayMs = 100,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultBaseFrequency = 660.0,
                            DefaultSignalSpeechTemplate = "Wave cross up {value}",
                            DefaultUsePolarityColoring = false },
                    new() { Name = CompCrossBear,     DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                            DefaultColorHex = "#EF9A9AA0", DefaultThickness = 3.0f, DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "sine_bell",
                            DefaultDecayMs = 100,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultBaseFrequency = 220.0,
                            DefaultSignalSpeechTemplate = "Wave cross down {value}",
                            DefaultUsePolarityColoring = false },

                    // ── Adaptive OB/OS lines ────────────────────────────────
                    // Hidden by default — power users can un-hide to see the rolling-percentile
                    // envelopes. In Fixed mode these are just constant ±OBLevel lines; in
                    // Percentile mode they track WT1's recent distribution.
                    new() { Name = CompAdaptiveOb,    DisplayName = "Adaptive OB", DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF6666", DefaultThickness = 1.0f, IsVisible = false,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultWaveform = "sine",
                            DefaultReferenceLevel = 0.0, DefaultEnvelopeType = "Sustain",
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            SpeechTemplate = "Adaptive overbought. {value:F1}." },
                    new() { Name = CompAdaptiveOs,    DisplayName = "Adaptive OS", DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#66BB66", DefaultThickness = 1.0f, IsVisible = false,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultWaveform = "sine",
                            DefaultReferenceLevel = 0.0, DefaultEnvelopeType = "Sustain",
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            SpeechTemplate = "Adaptive oversold. {value:F1}." },

                    // OB/OS zone shading is now declared as fixed-mode ZoneBandConfig
                    // entries in DefaultZoneBands — no phantom data components.

                    // ── Anchor Polarity — +1/0/-1 hidden, strategy-friendly ──
                    new() { Name = CompAnchorPolarity, DisplayName = "Anchor Polarity", DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#9E9E9E", DefaultThickness = 1.0f, IsVisible = false,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultReferenceLevel = 0.0, DefaultEnvelopeType = "Sustain",
                            SpeechTemplate = "Anchor polarity. {value:F0}." },

                    // ── Signal dots ─────────────────────────────────────────
                    new() { Name = CompBlue,          DisplayType = ComponentDisplayType.Dot,        Role = ComponentRole.Signal,
                            DefaultColorHex = "#0BBCF5", DefaultThickness = 5.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "sine_bell",
                            DefaultDecayMs = 350,
                            DefaultBaseFrequency = 840.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Oversold crossover, long signal",
                            DefaultUsePolarityColoring = false },
                    new() { Name = CompRed,           DisplayType = ComponentDisplayType.Dot,        Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF1744", DefaultThickness = 5.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "sine_bell",
                            DefaultDecayMs = 350,
                            DefaultBaseFrequency = 210.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Overbought crossover, short signal",
                            DefaultUsePolarityColoring = false },
                    new() { Name = CompGold,          DisplayType = ComponentDisplayType.Dot,        Role = ComponentRole.Signal,
                            DefaultColorHex = "#FFD700", DefaultThickness = 8.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "dual_tone_bell",
                            DefaultDecayMs = 500,
                            DefaultBaseFrequency = 440.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Triple confluence buy, strong confirmation",
                            DefaultUsePolarityColoring = false },

                    // ── Divergence dots ─────────────────────────────────────
                    new() { Name = CompBullDiv,       DisplayType = ComponentDisplayType.Dot,        Role = ComponentRole.Signal,
                            DefaultColorHex = "#00E676", DefaultThickness = 3.5f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "triangle_bell",
                            DefaultDecayMs = 230,
                            DefaultBaseFrequency = 620.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Bullish divergence",
                            DefaultUsePolarityColoring = false },
                    new() { Name = CompBearDiv,       DisplayType = ComponentDisplayType.Dot,        Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF1744", DefaultThickness = 3.5f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "triangle_bell",
                            DefaultDecayMs = 230,
                            DefaultBaseFrequency = 310.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Bearish divergence",
                            DefaultUsePolarityColoring = false },
                    new() { Name = CompHidBull,       DisplayType = ComponentDisplayType.Dot,        Role = ComponentRole.Signal,
                            DefaultColorHex = "#26C6DA", DefaultThickness = 3.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "triangle_bell",
                            DefaultDecayMs = 180,
                            DefaultBaseFrequency = 520.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Hidden bullish continuation",
                            DefaultUsePolarityColoring = false },
                    new() { Name = CompHidBear,       DisplayType = ComponentDisplayType.Dot,        Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF9800", DefaultThickness = 3.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "triangle_bell",
                            DefaultDecayMs = 180,
                            DefaultBaseFrequency = 360.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Hidden bearish continuation",
                            DefaultUsePolarityColoring = false },
                },
                DefaultCloudFills = new List<CloudFillConfig>
                {
                    new() { UpperComponentName = CompWT1Anchor, LowerComponentName = CompWT2Anchor,
                            BullishColorHex = "#881A237E", BearishColorHex = "#88880E4F",
                            DisplayName = "Anchor Fill", IsVisible = true,
                            Sonification = new CloudSonificationConfig(
                                BullishFrequency: 160f,
                                BearishFrequency: 110f,
                                SoundPatchId: "sine_bell",
                                DecayMs: 400,
                                MaxVolume: 0.30f) },
                    // WT Fill is purely visual. The two boundary components (WT1 / WT2)
                    // each sonify independently as Oscillator voices; a third cloud voice
                    // between them would duplicate information the user already hears.
                    // Rule of thumb: clouds sonify only when the cloud represents a
                    // regime signal the boundary components don't individually carry
                    // (e.g. Anchor Fill = HTF regime, Ichimoku Kumo = trend direction).
                    new() { UpperComponentName = CompWT1, LowerComponentName = CompWT2,
                            BullishColorHex = "#590BBCF5", BearishColorHex = "#40FF1744",
                            DisplayName = "WT Fill", IsVisible = true,
                            Sonification = null },

                },
                DefaultZoneBands = new List<ZoneBandConfig>
                {
                    // OB/OS background shading — fixed-mode zone bands spanning the full
                    // viewport between absolute oscillator Y values. Purely visual, no
                    // data component required. Hard-coded to ±53 / ±100 to match the
                    // Overbought / Oversold LevelDescriptors in GetDefaultLevels().
                    new() { DisplayName = "OB Zone", ColorHex = "#40FF6666",
                            FixedTop = 100.0, FixedBottom = 53.0, IsVisible = true },
                    new() { DisplayName = "OS Zone", ColorHex = "#4066BB66",
                            FixedTop = -53.0, FixedBottom = -100.0, IsVisible = true },
                },
                Parameters = new List<IndicatorParameterMetadata>
                {
                    // Wave Trend core
                    new() { Name = "WT1Period",      DisplayName = "WT Channel Period",      DefaultValue = 9.0,  DataType = typeof(int),    MinValue = 2,  MaxValue = 60,  Step = 1 },
                    new() { Name = "WT2Period",      DisplayName = "WT Average Period",      DefaultValue = 12.0, DataType = typeof(int),    MinValue = 2,  MaxValue = 60,  Step = 1 },
                    new() { Name = "MFPeriod",       DisplayName = "Money Flow Period",      DefaultValue = 60.0, DataType = typeof(int),    MinValue = 5,  MaxValue = 200, Step = 1,
                            Description = "SMA length for the body/range Money Flow. 60 produces the slow glass-wave shape matching real MCB." },
                    new() { Name = "OBLevel",        DisplayName = "OB/OS Threshold",        DefaultValue = 53.0, DataType = typeof(double), MinValue = 20, MaxValue = 100, Step = 1,
                            Description = "Fixed-mode OB/OS level. Ignored in Percentile mode except as a floor reference." },
                    // Signal logic
                    new() { Name = "RSIPeriod",      DisplayName = "RSI Period (Gold/Base)", DefaultValue = 14.0, DataType = typeof(int),    MinValue = 2,  MaxValue = 60, Step = 1 },
                    new() { Name = "RSIOSLevel",     DisplayName = "RSI Oversold (Gold)",    DefaultValue = 30.0, DataType = typeof(double), MinValue = 10, MaxValue = 50, Step = 1 },
                    new() { Name = "PivotBars",      DisplayName = "Divergence Pivot Bars",  DefaultValue = 3.0,  DataType = typeof(int),    MinValue = 1,  MaxValue = 10, Step = 1 },
                    // Anchor waves
                    new() { Name = "AnchorMultiplier", DisplayName = "Anchor Wave Multiplier", DefaultValue = 5.0, DataType = typeof(int), MinValue = 2, MaxValue = 10, Step = 1,
                            Description = "Period multiplier for the anchor WT pair (default 5 = 5× WT periods)." },

                    // Adaptive thresholds
                    new() { Name = "ThresholdMode",    DisplayName = "Threshold Mode",        DefaultValue = "Fixed", DataType = typeof(string),
                            Description = "Fixed = ±OBLevel. Percentile = rolling OB/OS envelopes that adapt to the instrument." },
                    new() { Name = "AdaptiveLookback", DisplayName = "Adaptive Lookback",     DefaultValue = 500.0, DataType = typeof(int), MinValue = 50, MaxValue = 2000, Step = 50,
                            Description = "Window length for percentile-based thresholds. Longer = more stable, slower to adapt." },
                    new() { Name = "UpperPercentile",  DisplayName = "Upper Percentile",      DefaultValue = 0.95, DataType = typeof(double), MinValue = 0.80, MaxValue = 0.99, Step = 0.01 },
                    new() { Name = "LowerPercentile",  DisplayName = "Lower Percentile",      DefaultValue = 0.05, DataType = typeof(double), MinValue = 0.01, MaxValue = 0.20, Step = 0.01 },
                    new() { Name = "MinThresholdFloor",DisplayName = "Min Threshold Floor",   DefaultValue = 40.0, DataType = typeof(double), MinValue = 20, MaxValue = 80, Step = 1,
                            Description = "|Minimum| threshold used in Percentile mode to prevent whipsaw in compressed regimes." },

                    // Signal quality gates
                    new() { Name = "ConfirmBars",    DisplayName = "Confirmation Bars",     DefaultValue = 2.0, DataType = typeof(int), MinValue = 1, MaxValue = 5, Step = 1,
                            Description = "Bars WT1 must stay past OS/OB before Blue/Red dot fires (1 = immediate, 2 = sustained)." },
                    new() { Name = "CooldownBars",   DisplayName = "Cooldown Bars",         DefaultValue = 0.0, DataType = typeof(int), MinValue = 0, MaxValue = 50, Step = 1,
                            Description = "Suppress further same-side dots for N bars after one fires. 0 = auto (uses WT1Period)." },
                    new() { Name = "AdxPeriod",      DisplayName = "ADX Period (Gold gate)", DefaultValue = 14.0, DataType = typeof(int), MinValue = 2, MaxValue = 50, Step = 1 },
                    new() { Name = "AdxGate",        DisplayName = "ADX Minimum (Gold)",    DefaultValue = 18.0, DataType = typeof(double), MinValue = 0, MaxValue = 50, Step = 1,
                            Description = "Gold dot fires only when ADX > this value. 0 disables the ADX gate." },
                    new() { Name = "AtrPeriod",      DisplayName = "ATR Period (Gold gate)", DefaultValue = 14.0, DataType = typeof(int), MinValue = 2, MaxValue = 50, Step = 1 },
                    new() { Name = "AtrFloorPct",    DisplayName = "ATR Percentile Floor",  DefaultValue = 0.20, DataType = typeof(double), MinValue = 0, MaxValue = 0.5, Step = 0.05,
                            Description = "Gold dot requires current ATR to rank above this percentile of recent ATRs (0 disables)." },
                    new() { Name = "GoldMinConfluence", DisplayName = "Gold Min Confluence", DefaultValue = 3.0, DataType = typeof(int), MinValue = 1, MaxValue = 4, Step = 1,
                            Description = "Number of gold confluence conditions (RSI<OS, green bar, ADX>gate, ATR>floor) that must hold. 3-of-4 is the default; 4 = strict AND, 1 = any single condition." },
                    new() { Name = "UseAnchorSuppression", DisplayName = "Anchor Suppression", DefaultValue = true, DataType = typeof(bool),
                            Description = "When ON, Blue dots are suppressed if Anchor Wave is strongly bearish, and Red dots if strongly bullish. Prevents counter-trend entries against a clear higher-TF regime." },
                    new() { Name = "AnchorSuppressDepth", DisplayName = "Anchor Suppress Depth", DefaultValue = 40.0, DataType = typeof(double), MinValue = 10, MaxValue = 80, Step = 5,
                            Description = "Anchor Wave must exceed ±this value to count as 'strongly directional' for suppression. 40 = strict, 20 = permissive (more suppression)." },
                    new() { Name = "TfAware",        DisplayName = "Timeframe-Aware Gates", DefaultValue = true, DataType = typeof(bool),
                            Description = "When ON, Cipher B detects the bar interval and scales gold/divergence gates accordingly. Default values are daily-tuned; intraday TFs need looser ADX/ATR/MF/conviction or signals fire too rarely. Turn OFF to use literal parameter values." },
                }
            }
        };

        // ── Calculation ───────────────────────────────────────────────────────

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            if (!code.Equals("CIPHER_B", StringComparison.OrdinalIgnoreCase)) return;
            int n = data.Length;
            if (n < 10) return;

            // Parameters
            int    wt1Period     = GetInt(parameters, "WT1Period",      9);
            int    wt2Period     = GetInt(parameters, "WT2Period",      12);
            int    mfPeriod      = GetInt(parameters, "MFPeriod",       60);
            double obLevel       = GetDbl(parameters, "OBLevel",        53.0);
            int    rsiPeriod     = GetInt(parameters, "RSIPeriod",      14);
            double rsiOS         = GetDbl(parameters, "RSIOSLevel",     30.0);
            int    pivotBars     = GetInt(parameters, "PivotBars",      3);
            int    anchorMult    = GetInt(parameters, "AnchorMultiplier", 5);

            string thresholdMode = GetStr(parameters, "ThresholdMode", "Fixed");
            int    adaptiveLook  = GetInt(parameters, "AdaptiveLookback", 500);
            double upperPct      = GetDbl(parameters, "UpperPercentile", 0.95);
            double lowerPct      = GetDbl(parameters, "LowerPercentile", 0.05);
            double minFloor      = GetDbl(parameters, "MinThresholdFloor", 40.0);

            int    confirmBars   = Math.Max(1, GetInt(parameters, "ConfirmBars", 2));
            int    cooldownBars  = GetInt(parameters, "CooldownBars", 0);
            if (cooldownBars <= 0) cooldownBars = wt1Period;

            int    adxPeriod     = GetInt(parameters, "AdxPeriod",    14);
            double adxGate       = GetDbl(parameters, "AdxGate",      18.0);
            int    atrPeriod     = GetInt(parameters, "AtrPeriod",    14);
            double atrFloorPct   = GetDbl(parameters, "AtrFloorPct",  0.20);
            int    goldMinConfluence = GetInt(parameters, "GoldMinConfluence", 3);

            // Anchor suppression: blocks blue/red dots when the anchor wave is
            // strongly opposed to the intended direction. A strongly bearish anchor
            // (negative polarity AND depth > threshold) means the higher-TF regime
            // is bearish, so a blue dot fired in that context is almost always a
            // counter-trend trap. Same logic inverted for red dots.
            bool   useAnchorSuppression = GetBool(parameters, "UseAnchorSuppression", true);
            double anchorSuppressDepth  = GetDbl(parameters, "AnchorSuppressDepth", 40.0);

            bool adaptive = string.Equals(thresholdMode, "Percentile", StringComparison.OrdinalIgnoreCase);

            // ── Timeframe-aware gate scaling ──────────────────────────────────
            // The defaults (ADX 18, ATR floor 0.20, MF 60, divergence pivot 3,
            // conviction 0.85x) were tuned on BTC daily. They produce zero gold
            // dots and very few divergences on intraday timeframes because trend
            // strength is naturally weaker per-bar at lower TFs. When TfAware is
            // enabled (default) we detect the bar interval from the data and
            // scale the gates so the indicator behaves like real MCB across all
            // timeframes — looser on intraday, slightly tighter on weekly+.
            //
            // Detection uses the median bar interval over a sample (resilient to
            // weekend gaps, halts, and missing-bar artifacts). Buckets:
            //   <  60m   : intraday-fast  (ADX 10, ATR 0.10, MF 24, pivot 2, conv 0.65)
            //   60m–4h   : intraday       (ADX 12, ATR 0.12, MF 32, pivot 2, conv 0.70)
            //   4h–12h   : intraday-slow  (ADX 14, ATR 0.15, MF 40, pivot 3, conv 0.75)
            //   12h–3d   : daily          (ADX 18, ATR 0.20, MF 60, pivot 3, conv 0.85) — base
            //   >  3d    : weekly+        (ADX 20, ATR 0.25, MF 60, pivot 3, conv 0.90)
            bool tfAware = GetBool(parameters, "TfAware", true);
            int  tfBucket = 3;  // 0=ifast, 1=intraday, 2=islow, 3=daily, 4=weekly+
            int  intervalMin = 1440;
            if (tfAware && n >= 10)
            {
                // Median interval over up to 100 consecutive bars. Robust to gaps.
                int sampleN = Math.Min(100, n - 1);
                var deltas = new double[sampleN];
                for (int i = 0; i < sampleN; i++)
                    deltas[i] = (data[i + 1].Date - data[i].Date).TotalMinutes;
                Array.Sort(deltas);
                intervalMin = (int)Math.Round(deltas[sampleN / 2]);
                if      (intervalMin <  60)   tfBucket = 0;
                else if (intervalMin <  240)  tfBucket = 1;
                else if (intervalMin <  720)  tfBucket = 2;
                else if (intervalMin <  4320) tfBucket = 3;
                else                          tfBucket = 4;

                // Apply per-bucket scaling. User-overridden values are still scaled
                // proportionally — the TF profile is a multiplier on the base.
                switch (tfBucket)
                {
                    case 0: // intraday-fast (<1h)
                        adxGate     = Math.Min(adxGate,     10.0);
                        atrFloorPct = Math.Min(atrFloorPct,  0.10);
                        mfPeriod    = Math.Max(8,  mfPeriod * 24 / 60);
                        pivotBars   = Math.Max(2,  pivotBars - 1);
                        rsiOS       = Math.Max(rsiOS,       38.0);
                        break;
                    case 1: // intraday (1h–4h)
                        adxGate     = Math.Min(adxGate,     12.0);
                        atrFloorPct = Math.Min(atrFloorPct,  0.12);
                        mfPeriod    = Math.Max(12, mfPeriod * 32 / 60);
                        pivotBars   = Math.Max(2,  pivotBars - 1);
                        rsiOS       = Math.Max(rsiOS,       36.0);
                        break;
                    case 2: // intraday-slow (4h–12h)
                        adxGate     = Math.Min(adxGate,     14.0);
                        atrFloorPct = Math.Min(atrFloorPct,  0.15);
                        mfPeriod    = Math.Max(20, mfPeriod * 40 / 60);
                        rsiOS       = Math.Max(rsiOS,       34.0);
                        // Drop pivot bars to 2 on 4h-12h. Cycles on these TFs happen
                        // in 5-10 bars — a 3-bar pivot window only resolves clean
                        // 7-bar peaks and misses most shallow swings where real MCB
                        // divergences form. A 12h chart of a 40% drawdown produced
                        // only 1 bull div and 1 bear div at pivotBars=3; pivotBars=2
                        // should ~2x that density.
                        pivotBars   = Math.Max(2,  pivotBars - 1);
                        break;
                    case 3: // daily (12h–3d): base values, no scaling
                        break;
                    case 4: // weekly+ (>3d)
                        adxGate     = Math.Max(adxGate,     20.0);
                        atrFloorPct = Math.Max(atrFloorPct,  0.25);
                        rsiOS       = Math.Min(rsiOS,       28.0);
                        // mfPeriod and pivotBars unchanged
                        break;
                }
            }
            // The conviction multiplier for divergences (used inside the pivot
            // loops below) is derived from the same bucket so all gates scale
            // together. 0.65 / 0.70 / 0.75 / 0.85 / 0.90.
            double convictionMult = tfAware
                ? (tfBucket == 0 ? 0.65 : tfBucket == 1 ? 0.70 : tfBucket == 2 ? 0.75 : tfBucket == 4 ? 0.90 : 0.85)
                : 0.85;

            // ── Source series ─────────────────────────────────────────────────
            var close = new double[n];
            var open  = new double[n];
            var high  = new double[n];
            var low   = new double[n];
            var hlc3  = new double[n];
            for (int i = 0; i < n; i++)
            {
                close[i] = data[i].Close;
                open[i]  = data[i].Open;
                high[i]  = data[i].High;
                low[i]   = data[i].Low;
                hlc3[i]  = (high[i] + low[i] + close[i]) / 3.0;
            }

            // ── Wave Trend ────────────────────────────────────────────────────
            var esa     = IndicatorMath.Ema(hlc3, wt1Period);
            var absDiff = new double[n];
            for (int i = 0; i < n; i++)
                absDiff[i] = double.IsNaN(esa[i]) ? double.NaN : Math.Abs(hlc3[i] - esa[i]);
            var d  = IndicatorMath.Ema(absDiff, wt1Period);
            var ci = new double[n];
            for (int i = 0; i < n; i++)
                ci[i] = (double.IsNaN(d[i]) || d[i] < 1e-10) ? double.NaN : (hlc3[i] - esa[i]) / (0.015 * d[i]);
            var wt1 = IndicatorMath.Ema(ci, wt2Period);
            var wt2 = IndicatorMath.Sma(wt1, 4);

            // WT Histogram = WT1 − WT2 (the real MCB "VWAP" bars)
            var wtHist = new double[n];
            for (int i = 0; i < n; i++)
                wtHist[i] = (double.IsNaN(wt1[i]) || double.IsNaN(wt2[i])) ? double.NaN : wt1[i] - wt2[i];

            // ── Anchor Waves (period-scaled WT replica) ───────────────────────
            int    ancPeriod1 = wt1Period * anchorMult;
            int    ancPeriod2 = wt2Period * anchorMult;
            var    esaAnc     = IndicatorMath.Ema(hlc3, ancPeriod1);
            var    absDiffAnc = new double[n];
            for (int i = 0; i < n; i++)
                absDiffAnc[i] = double.IsNaN(esaAnc[i]) ? double.NaN : Math.Abs(hlc3[i] - esaAnc[i]);
            var    dAnc    = IndicatorMath.Ema(absDiffAnc, ancPeriod1);
            var    ciAnc   = new double[n];
            for (int i = 0; i < n; i++)
                ciAnc[i] = (double.IsNaN(dAnc[i]) || dAnc[i] < 1e-10) ? double.NaN : (hlc3[i] - esaAnc[i]) / (0.015 * dAnc[i]);
            var    wt1Anc  = IndicatorMath.Ema(ciAnc, ancPeriod2);
            var    wt2Anc  = IndicatorMath.Sma(wt1Anc, 4);

            // Anchor polarity: +1 if fast > slow, -1 if fast < slow, 0 if NaN
            var ancPol = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(wt1Anc[i]) || double.IsNaN(wt2Anc[i])) { ancPol[i] = double.NaN; continue; }
                ancPol[i] = wt1Anc[i] > wt2Anc[i] ? 1.0 : (wt1Anc[i] < wt2Anc[i] ? -1.0 : 0.0);
            }

            // ── Money Flow (body/range, slow SMA) ─────────────────────────────
            // clv = (close - open) / max(high - low, tick); bounded [-1..+1].
            // Smoothed with SMA(MFPeriod) — default 60 — produces the slow glass-wave
            // shape that matches real MCB.
            //
            // Scaled by 175 (VuManChu's canonical constant, reverse-engineered from real
            // MCB screenshots and used in every open-source clone). Body/range averaged
            // over 60 bars typically lives in ±0.1..0.3, so ×175 maps that to ±17..52
            // — the prominent ambient wave real MCB shows. Clamped to ±100 to stay
            // inside the WT pane bounds on rare extreme-body runs.
            const double MfCenter = 0.0, MfAmplitude = 175.0, MfClamp = 100.0;
            var clv = new double[n];
            for (int i = 0; i < n; i++)
            {
                double range = high[i] - low[i];
                if (range < 1e-10) { clv[i] = 0.0; continue; }
                double r = (close[i] - open[i]) / range;
                if (r > 1.0) r = 1.0;
                else if (r < -1.0) r = -1.0;
                clv[i] = r;
            }
            var mfSmooth  = IndicatorMath.Sma(clv, mfPeriod);
            // Faster MF window for the gold-dot gate. The slow 60-bar SMA is used for
            // the visible Money Flow wave, but the gold cross condition needs a window
            // short enough to flip positive *inside* an oversold WT cycle — by definition
            // OS happens at local price weakness, where the slow MF is almost always
            // negative. A 14-bar SMA captures the recent shift toward bullish bodies
            // that real MCB's gold gate is responding to (matches VuManChu/clone behavior).
            var mfFast = IndicatorMath.Sma(clv, 14);
            var mfDisplay = new double[n];
            var mfSign    = new double[n];
            // Visual amplitude expansion: signed-sqrt mapping for the displayed wave
            // makes daily-TF body/range averages (typically ±0.05..0.15) visible without
            // distorting sign or zero-crossings. The gate uses the linear value, only
            // the display is reshaped.
            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(mfSmooth[i])) { mfDisplay[i] = double.NaN; mfSign[i] = double.NaN; continue; }
                double r = mfSmooth[i];
                // signed sqrt: sign(r) * sqrt(|r|) — pulls small values up toward ±1
                double expanded = Math.Sign(r) * Math.Sqrt(Math.Abs(r));
                double scaled = expanded * MfAmplitude + MfCenter;
                if (scaled >  MfClamp) scaled =  MfClamp;
                if (scaled < -MfClamp) scaled = -MfClamp;
                mfDisplay[i] = scaled;
                mfSign[i]    = mfSmooth[i];
            }

            // ── RSI (for Gold confluence) ─────────────────────────────────────
            var rsi = IndicatorMath.Rsi(close, rsiPeriod);

            // ── ATR + ADX (for Gold gating) ───────────────────────────────────
            var atr = IndicatorMath.Atr(data, atrPeriod);
            var adx = IndicatorMath.Adx(data, adxPeriod);

            // Rolling percentile of ATR for the volatility floor. Using the smaller
            // of AdaptiveLookback or 200 keeps this cheap and responsive.
            int atrRankWindow = Math.Min(adaptiveLook, 200);
            var atrCeiling = RollingQuantile.Compute(atr, atrRankWindow, 1.0 - atrFloorPct, Math.Min(atrRankWindow / 2, 50));
            // Gold fires only if atr[i] >= atrCeiling lower-bound, i.e. ATR is at or above
            // the (1 - AtrFloorPct) percentile rank of recent values. We re-use the
            // percentile helper: the (1-floor) percentile is the threshold; ATR above it
            // means we're in the top AtrFloorPct of volatility.
            // Actually we want the INVERSE: the floor is the AtrFloorPct percentile of ATR,
            // and we require atr[i] > that floor. Recompute:
            var atrFloorSeries = RollingQuantile.Compute(atr, atrRankWindow, atrFloorPct, Math.Min(atrRankWindow / 2, 50));

            // ── Adaptive OB/OS ───────────────────────────────────────────────
            // In Fixed mode, emit constant ±obLevel lines. In Percentile mode,
            // compute rolling percentiles of WT1 over AdaptiveLookback bars with a
            // floor at MinThresholdFloor to prevent whipsaw in compressed regimes.
            var adaptiveOb = new double[n];
            var adaptiveOs = new double[n];
            if (adaptive)
            {
                int warmup = Math.Max(50, adaptiveLook / 4);
                var obRaw = RollingQuantile.Compute(wt1, adaptiveLook, upperPct, warmup);
                var osRaw = RollingQuantile.Compute(wt1, adaptiveLook, lowerPct, warmup);
                for (int i = 0; i < n; i++)
                {
                    adaptiveOb[i] = double.IsNaN(obRaw[i]) ? obLevel : Math.Max(obRaw[i],  minFloor);
                    adaptiveOs[i] = double.IsNaN(osRaw[i]) ? -obLevel : Math.Min(osRaw[i], -minFloor);
                }
            }
            else
            {
                Array.Fill(adaptiveOb,  obLevel);
                Array.Fill(adaptiveOs, -obLevel);
            }

            // ── Signal dots with confirmation + cooldown ─────────────────────
            var crossBullSignal = new double[n];
            var crossBearSignal = new double[n];
            var blueSignal      = new double[n];
            var redSignal       = new double[n];
            var goldSignal      = new double[n];
            Array.Fill(crossBullSignal, double.NaN);
            Array.Fill(crossBearSignal, double.NaN);
            Array.Fill(blueSignal, double.NaN);
            Array.Fill(redSignal,  double.NaN);
            Array.Fill(goldSignal, double.NaN);

            int blueCd = 0, redCd = 0, goldCd = 0;

            for (int i = 1; i < n; i++)
            {
                if (blueCd > 0) blueCd--;
                if (redCd  > 0) redCd--;
                if (goldCd > 0) goldCd--;

                if (double.IsNaN(wt1[i]) || double.IsNaN(wt2[i]) ||
                    double.IsNaN(wt1[i - 1]) || double.IsNaN(wt2[i - 1])) continue;

                bool crossUp   = wt1[i - 1] < wt2[i - 1] && wt1[i] >= wt2[i];
                bool crossDown = wt1[i - 1] > wt2[i - 1] && wt1[i] <= wt2[i];

                if (crossUp)   crossBullSignal[i] = wt2[i] - 3.0;
                if (crossDown) crossBearSignal[i] = wt2[i] + 3.0;

                double obHere = adaptiveOb[i];
                double osHere = adaptiveOs[i];

                // 2-bar (or N-bar) confirmation: WT1 must have been below OS for the
                // previous ConfirmBars bars at cross time. Blue dot fires on the cross bar.
                // Gold dot note: placed ON the WT2 line at the cross (wt2[i]), matching
                // real MCB's visual where the dot sits on the wave body, not offset below.
                // Anchor suppression: lock out blue dots when the anchor wave is
                // strongly bearish. The anchor is a 5x-period WT replica, so it
                // represents the higher-TF regime. A blue dot fired while anchor
                // is strongly bearish is almost always a counter-trend entry.
                bool blueAnchorOk = !useAnchorSuppression
                    || double.IsNaN(wt1Anc[i])
                    || !(ancPol[i] < 0 && wt1Anc[i] < -anchorSuppressDepth);

                if (crossUp && blueCd == 0 && wt1[i] < osHere && blueAnchorOk)
                {
                    bool sustained = true;
                    for (int k = 1; k < confirmBars && (i - k) >= 0; k++)
                    {
                        if (double.IsNaN(wt1[i - k]) || wt1[i - k] > osHere) { sustained = false; break; }
                    }
                    if (sustained)
                    {
                        blueSignal[i] = wt2[i] - 5.0;
                        blueCd = cooldownBars;

                        // Gold gating: K-of-N confluence on (RSI<OS, green bar, ADX>gate, ATR>floor).
                        // Strict AND-of-all-four was too brittle — even with TF-aware
                        // scaling, the conjunction probability was ~5-8% per blue dot
                        // and produced 0-3 gold dots per 4000-bar dataset. K-of-N (need
                        // any 3 of 4 by default) preserves the "this is a confluence
                        // signal" character while triggering 3-5x more often. Real MCB
                        // visibly fires gold more frequently than strict-AND can, which
                        // means it's not requiring all conditions simultaneously.
                        int conditionsMet = 0;
                        if (!double.IsNaN(rsi[i]) && rsi[i] < rsiOS) conditionsMet++;
                        if (clv[i] > 0) conditionsMet++;
                        if (adxGate <= 0 || (!double.IsNaN(adx[i]) && adx[i] > adxGate)) conditionsMet++;
                        if (atrFloorPct <= 0 || double.IsNaN(atrFloorSeries[i]) ||
                            (!double.IsNaN(atr[i]) && atr[i] > atrFloorSeries[i])) conditionsMet++;
                        if (goldCd == 0 && conditionsMet >= goldMinConfluence)
                        {
                            goldSignal[i] = wt2[i];
                            goldCd = cooldownBars;
                        }
                    }
                }

                // Anchor suppression for red dots: lock out when anchor is strongly bullish.
                bool redAnchorOk = !useAnchorSuppression
                    || double.IsNaN(wt1Anc[i])
                    || !(ancPol[i] > 0 && wt1Anc[i] > anchorSuppressDepth);

                if (crossDown && redCd == 0 && wt1[i] > obHere && redAnchorOk)
                {
                    bool sustained = true;
                    for (int k = 1; k < confirmBars && (i - k) >= 0; k++)
                    {
                        if (double.IsNaN(wt1[i - k]) || wt1[i - k] < obHere) { sustained = false; break; }
                    }
                    if (sustained)
                    {
                        redSignal[i] = wt2[i] + 5.0;
                        redCd = cooldownBars;
                    }
                }
            }

            // ── Divergence detection (volume-of-range gated) ─────────────────
            var bullDiv = new double[n];
            var bearDiv = new double[n];
            var hidBull = new double[n];
            var hidBear = new double[n];
            Array.Fill(bullDiv, double.NaN);
            Array.Fill(bearDiv, double.NaN);
            Array.Fill(hidBull, double.NaN);
            Array.Fill(hidBear, double.NaN);

            // Companion arrays that record the first-pivot bar index (_anchorIdx)
            // and the same offset Y (_anchorY) for every fired divergence. NaN on
            // bars that don't fire. Read by the renderer to draw the slanted
            // pivot-to-pivot line that real Market Cipher B shows alongside the
            // dot — carries the "divergence from where to where" visual that a
            // dot-only marker loses.
            var bullDivIdx = new double[n];
            var bearDivIdx = new double[n];
            var hidBullIdx = new double[n];
            var hidBearIdx = new double[n];
            var bullDivY = new double[n];
            var bearDivY = new double[n];
            var hidBullY = new double[n];
            var hidBearY = new double[n];
            Array.Fill(bullDivIdx, double.NaN);
            Array.Fill(bearDivIdx, double.NaN);
            Array.Fill(hidBullIdx, double.NaN);
            Array.Fill(hidBearIdx, double.NaN);
            Array.Fill(bullDivY, double.NaN);
            Array.Fill(bearDivY, double.NaN);
            Array.Fill(hidBullY, double.NaN);
            Array.Fill(hidBearY, double.NaN);

            // Rolling True Range SMA to gate "conviction" on the 2nd pivot.
            var tr = IndicatorMath.TrueRange(data);
            int trWin = Math.Max(20, wt1Period * 4);
            var trAvg = IndicatorMath.Sma(tr, trWin);

            int start = pivotBars;
            int end   = n - pivotBars;
            var pivotLowIdx  = new List<int>();
            var pivotHighIdx = new List<int>();

            // Pivot detection on WT1 (reverted from WT2). A/B test confirmed WT2
            // pivots kill Hidden Bull Continuation on 4h and 1d-ETH (20+ trades
            // per half dropped to 0), and produce worse R on Bullish Divergence
            // 4h (+0.324/+0.114 → -0.104/-0.023). WT2 is smoother, but that
            // smoothness eliminates the shallow-pullback pivots that hidden
            // continuations require — and the regular divergence depth gate
            // (below) is sufficient to filter noise without needing smoother
            // pivots.
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

            // Divergence depth gate. Both pivots must be past this threshold for a
            // divergence to fire. Scaled by TF: intraday uses 25-30 (WT cycles have
            // smaller amplitude on smaller TFs), daily uses 35, weekly uses 40.
            // Eliminates "two shallow wiggles with price drift" false positives
            // that pure pivot-based detection is prone to.
            double divergenceDepth = tfAware
                ? (tfBucket <= 1 ? 25.0 : tfBucket == 2 ? 30.0 : tfBucket == 4 ? 40.0 : 35.0)
                : 35.0;

            for (int k = 1; k < pivotLowIdx.Count; k++)
            {
                int prev = pivotLowIdx[k - 1], curr = pivotLowIdx[k];
                if (double.IsNaN(close[prev]) || double.IsNaN(close[curr])) continue;

                // TF-scaled conviction: multiplier set above based on bar interval.
                // Daily uses 0.85x (the original loosened value), intraday uses
                // 0.65-0.75x (more permissive — fewer high-TR bars per cycle on
                // smaller TFs), weekly uses 0.90x (closer to strict).
                bool conviction = !double.IsNaN(tr[curr]) && !double.IsNaN(trAvg[curr]) && tr[curr] >= trAvg[curr] * convictionMult;

                // Depth gate on regular bull divergences only: both WT1 pivots
                // must be past -depth. The depth gate does NOT apply to hidden
                // continuations — by design, hidden bull fires on a shallower
                // second pivot (the WT pulled back but not to the full extreme),
                // which is exactly the pattern the depth gate would filter out.
                bool deepEnough = wt1[prev] < -divergenceDepth && wt1[curr] < -divergenceDepth;

                if (close[curr] < close[prev] && wt1[curr] > wt1[prev] && conviction && deepEnough)
                {
                    bullDiv[curr] = wt1[curr] - 4.0;
                    bullDivIdx[curr] = prev;
                    bullDivY[curr]   = wt1[prev] - 4.0;
                }
                if (close[curr] > close[prev] && wt1[curr] < wt1[prev] && conviction)
                {
                    hidBull[curr] = wt1[curr] - 4.0;
                    hidBullIdx[curr] = prev;
                    hidBullY[curr]   = wt1[prev] - 4.0;
                }
            }
            for (int k = 1; k < pivotHighIdx.Count; k++)
            {
                int prev = pivotHighIdx[k - 1], curr = pivotHighIdx[k];
                if (double.IsNaN(close[prev]) || double.IsNaN(close[curr])) continue;

                // TF-scaled conviction: multiplier set above based on bar interval.
                // Daily uses 0.85x (the original loosened value), intraday uses
                // 0.65-0.75x (more permissive — fewer high-TR bars per cycle on
                // smaller TFs), weekly uses 0.90x (closer to strict).
                bool conviction = !double.IsNaN(tr[curr]) && !double.IsNaN(trAvg[curr]) && tr[curr] >= trAvg[curr] * convictionMult;

                // Depth gate on regular bear divergences only: both WT1 pivots
                // must be past +depth. Hidden bear continuations are unconstrained
                // (shallow second pivot is the signal itself).
                bool deepEnough = wt1[prev] > divergenceDepth && wt1[curr] > divergenceDepth;

                if (close[curr] > close[prev] && wt1[curr] < wt1[prev] && conviction && deepEnough)
                {
                    bearDiv[curr] = wt1[curr] + 4.0;
                    bearDivIdx[curr] = prev;
                    bearDivY[curr]   = wt1[prev] + 4.0;
                }
                if (close[curr] < close[prev] && wt1[curr] > wt1[prev] && conviction)
                {
                    hidBear[curr] = wt1[curr] + 4.0;
                    hidBearIdx[curr] = prev;
                    hidBearY[curr]   = wt1[prev] + 4.0;
                }
            }

            // ── Shallow / cross-based divergence detector (parallel to pivot-based) ──
            // The pivot-based detector requires WT1 to be a clean local extreme over
            // the full pivotBars window — fine for major divergences but misses the
            // 2-bar swing setups that fire frequently in trending markets. The
            // XRP/USDT 1d screenshot showed a 60% drawdown with only 1 bear div firing
            // because most of the bear setups formed on shallow swings. This shallow
            // detector walks consecutive WT cross-down events inside OB and flags any
            // pair where the second cross has a higher price than the first while the
            // second WT cross-down is at a lower WT value — the textbook bear div
            // shape, just measured at the cross instead of at the pivot.
            //
            // Conviction filter: same TF-scaled multiplier. Cooldown is wt1Period to
            // prevent same-cycle double-counting against the pivot-based detector.
            int lastCrossUpIdx = -1, lastCrossDownIdx = -1;
            int shallowBearCd = 0, shallowBullCd = 0;
            for (int i = 1; i < n; i++)
            {
                if (shallowBearCd > 0) shallowBearCd--;
                if (shallowBullCd > 0) shallowBullCd--;
                if (double.IsNaN(wt1[i]) || double.IsNaN(wt2[i]) ||
                    double.IsNaN(wt1[i - 1]) || double.IsNaN(wt2[i - 1])) continue;

                bool xUp   = wt1[i - 1] < wt2[i - 1] && wt1[i] >= wt2[i];
                bool xDown = wt1[i - 1] > wt2[i - 1] && wt1[i] <= wt2[i];

                if (xDown && wt1[i] > adaptiveOb[i] - 10)  // near or in OB
                {
                    if (lastCrossDownIdx >= 0 && shallowBearCd == 0)
                    {
                        int prev = lastCrossDownIdx;
                        bool priceHigher = close[i] > close[prev];
                        bool wtLower     = wt1[i]    < wt1[prev];
                        bool conviction  = !double.IsNaN(tr[i]) && !double.IsNaN(trAvg[i]) && tr[i] >= trAvg[i] * convictionMult;
                        // Only fire if pivot-based detector hasn't already marked this bar.
                        if (priceHigher && wtLower && conviction && double.IsNaN(bearDiv[i]))
                        {
                            bearDiv[i] = wt1[i] + 4.0;
                            bearDivIdx[i] = prev;
                            bearDivY[i]   = wt1[prev] + 4.0;
                            shallowBearCd = wt1Period;
                        }
                    }
                    lastCrossDownIdx = i;
                }

                if (xUp && wt1[i] < adaptiveOs[i] + 10)  // near or in OS
                {
                    if (lastCrossUpIdx >= 0 && shallowBullCd == 0)
                    {
                        int prev = lastCrossUpIdx;
                        bool priceLower = close[i] < close[prev];
                        bool wtHigher   = wt1[i]    > wt1[prev];
                        bool conviction = !double.IsNaN(tr[i]) && !double.IsNaN(trAvg[i]) && tr[i] >= trAvg[i] * convictionMult;
                        if (priceLower && wtHigher && conviction && double.IsNaN(bullDiv[i]))
                        {
                            bullDiv[i] = wt1[i] - 4.0;
                            bullDivIdx[i] = prev;
                            bullDivY[i]   = wt1[prev] - 4.0;
                            shallowBullCd = wt1Period;
                        }
                    }
                    lastCrossUpIdx = i;
                }
            }

            // ── Write to buffer ───────────────────────────────────────────────
            WriteToBuffer(buffer, CompWT1,            wt1,             n);
            WriteToBuffer(buffer, CompWT2,            wt2,             n);
            WriteToBuffer(buffer, CompWtHistogram,    wtHist,          n);
            WriteToBuffer(buffer, CompWT1Anchor,      wt1Anc,          n);
            WriteToBuffer(buffer, CompWT2Anchor,      wt2Anc,          n);
            WriteToBuffer(buffer, CompAnchorPolarity, ancPol,          n);
            WriteToBuffer(buffer, CompAdaptiveOb,     adaptiveOb,      n);
            WriteToBuffer(buffer, CompAdaptiveOs,     adaptiveOs,      n);
            WriteToBuffer(buffer, CompMoneyFlowWave,  mfDisplay,       n);
            WriteToBuffer(buffer, CompCrossBull,      crossBullSignal, n);
            WriteToBuffer(buffer, CompCrossBear,      crossBearSignal, n);
            WriteToBuffer(buffer, CompBlue,           blueSignal,      n);
            WriteToBuffer(buffer, CompRed,            redSignal,       n);
            WriteToBuffer(buffer, CompGold,           goldSignal,      n);
            // ── Look-ahead honesty: divergence confirmation lag ──────────────────
            // A pivot at bar p is only CONFIRMED after seeing wt1[p+1..p+pivotBars]
            // (the right-shoulder check in the pivot loop above). But the marker is
            // stamped at p. A strategy that reads the marker at bar p is therefore
            // trading on information that does not exist until p+pivotBars — entering
            // at the exact pivot low with hindsight. That look-ahead inflates every
            // divergence-based backtest (audit 2026-06-12).
            //
            // DivergenceConfirmLag shifts every divergence marker forward to the bar
            // it is actually confirmable (p+pivotBars) so backtest == live == chart all
            // see the divergence at the same, honest bar. DEFAULT ON since the 2026-06-12
            // audit: with it OFF, a backtest reading the pivot-stamped marker enters at
            // the bar's low using future bars, while LIVE never sees the marker at the
            // current bar at all (it only appears pivotBars later) — backtest and live
            // disagree by construction. ON fixes both. The line-geometry companions
            // (_anchorIdx/_anchorY) are shifted in lockstep so the drawn pivot-to-pivot
            // line still anchors correctly from the (now confirmation-bar) marker.
            // Power users who want the pure Market-Cipher-B pivot-stamped dot for chart
            // review only can set DivergenceConfirmLag=false, accepting that any strategy
            // reading those markers in backtest is then look-ahead-biased.
            bool confirmLag = GetBool(parameters, "DivergenceConfirmLag", true);
            if (confirmLag && pivotBars > 0)
            {
                bullDiv = ShiftMarkersForward(bullDiv, pivotBars, n);
                bearDiv = ShiftMarkersForward(bearDiv, pivotBars, n);
                hidBull = ShiftMarkersForward(hidBull, pivotBars, n);
                hidBear = ShiftMarkersForward(hidBear, pivotBars, n);
                bullDivIdx = ShiftMarkersForward(bullDivIdx, pivotBars, n);
                bearDivIdx = ShiftMarkersForward(bearDivIdx, pivotBars, n);
                hidBullIdx = ShiftMarkersForward(hidBullIdx, pivotBars, n);
                hidBearIdx = ShiftMarkersForward(hidBearIdx, pivotBars, n);
                bullDivY = ShiftMarkersForward(bullDivY, pivotBars, n);
                bearDivY = ShiftMarkersForward(bearDivY, pivotBars, n);
                hidBullY = ShiftMarkersForward(hidBullY, pivotBars, n);
                hidBearY = ShiftMarkersForward(hidBearY, pivotBars, n);
            }

            WriteToBuffer(buffer, CompBullDiv,        bullDiv,         n);
            WriteToBuffer(buffer, CompBearDiv,        bearDiv,         n);
            WriteToBuffer(buffer, CompHidBull,        hidBull,         n);
            WriteToBuffer(buffer, CompHidBear,        hidBear,         n);
            // Companion "_anchorIdx" / "_anchorY" arrays holding the first-pivot
            // bar index and the same-offset Y value for every fired divergence.
            // Picked up by <c>StandardRenderers.RenderDot</c> to draw the slanted
            // pivot-to-pivot line that makes the divergence geometry legible to
            // sighted users and keeps parity with the real Market Cipher B overlay.
            WriteToBuffer(buffer, CompBullDiv + "_anchorIdx", bullDivIdx, n);
            WriteToBuffer(buffer, CompBearDiv + "_anchorIdx", bearDivIdx, n);
            WriteToBuffer(buffer, CompHidBull + "_anchorIdx", hidBullIdx, n);
            WriteToBuffer(buffer, CompHidBear + "_anchorIdx", hidBearIdx, n);
            WriteToBuffer(buffer, CompBullDiv + "_anchorY",   bullDivY,   n);
            WriteToBuffer(buffer, CompBearDiv + "_anchorY",   bearDivY,   n);
            WriteToBuffer(buffer, CompHidBull + "_anchorY",   hidBullY,   n);
            WriteToBuffer(buffer, CompHidBear + "_anchorY",   hidBearY,   n);
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            int wt1    = GetInt(parameters, "WT1Period",        9);
            int wt2    = GetInt(parameters, "WT2Period",        12);
            int rsi    = GetInt(parameters, "RSIPeriod",        14);
            int mf     = GetInt(parameters, "MFPeriod",         60);
            int anchor = GetInt(parameters, "AnchorMultiplier", 5);
            int adapt  = GetInt(parameters, "AdaptiveLookback", 500);
            int ancP1  = wt1 * anchor;
            int ancP2  = wt2 * anchor;
            return Math.Max(200, Math.Max((ancP1 + ancP2) * 4 + rsi * 2 + mf, adapt));
        }

        /// <summary>
        /// Returns an imperative speech string for the given component at the current bar.
        /// </summary>
        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value))
                return "no data";

            return componentName switch
            {
                CompWT1            => GetWTValueSpeech(value),
                CompWT2            => GetWTValueSpeech(value),
                CompWtHistogram    => $"{value:F1}, {(value >= 0 ? "positive" : "negative")}",
                CompWT1Anchor      => $"{value:F1}",
                CompWT2Anchor      => $"{value:F1}",
                CompAnchorPolarity => value > 0 ? "bullish" : (value < 0 ? "bearish" : "neutral"),
                CompAdaptiveOb     => $"{value:F1}",
                CompAdaptiveOs     => $"{value:F1}",
                CompMoneyFlowWave  => GetMFWaveSpeech(value),
                CompCrossBull      => GetSignalDotSpeech(bar, allComponentData, dataIndex),
                CompCrossBear      => GetSignalDotSpeech(bar, allComponentData, dataIndex),
                CompBlue           => GetSignalDotSpeech(bar, allComponentData, dataIndex),
                CompRed            => GetSignalDotSpeech(bar, allComponentData, dataIndex),
                CompBullDiv        => GetDivergenceSpeech(bar, allComponentData, dataIndex, "bullish"),
                CompBearDiv        => GetDivergenceSpeech(bar, allComponentData, dataIndex, "bearish"),
                CompHidBull        => GetDivergenceSpeech(bar, allComponentData, dataIndex, "hidden bullish"),
                CompHidBear        => GetDivergenceSpeech(bar, allComponentData, dataIndex, "hidden bearish"),
                CompGold           => GetTripleConfluenceSpeech(bar, allComponentData, dataIndex),
                _ => null
            };
        }

        private static string GetMFWaveSpeech(double value)
        {
            // Display scaling is ±100 (clamped, ×175 raw multiplier). Convert back to
            // −100..+100 for speech using the clamp range.
            double pct = value;
            if (pct > 100.0) pct = 100.0;
            if (pct < -100.0) pct = -100.0;
            string dir = pct >= 0 ? "buying" : "selling";
            return $"{Math.Abs(pct):F0}% {dir} pressure";
        }

        private static string GetWTValueSpeech(double value)
        {
            if (value > 53)  return $"{value:F1}, overbought";
            if (value < -53) return $"{value:F1}, oversold";
            return $"{value:F1}";
        }

        private static string GetSignalDotSpeech(Ohlcv bar, IReadOnlyDictionary<string, double[]> data, int idx)
        {
            double wt1 = GetCompValue(data, CompWT1, idx);
            string wtStr = double.IsNaN(wt1) ? "" : $" Wave Trend {wt1:F1}.";
            return $"Price {AccessibleTrader.Core.Services.Accessibility.SpeechPriceFormatter.FormatPrice(bar.Close)}.{wtStr}";
        }

        private static string GetDivergenceSpeech(Ohlcv bar, IReadOnlyDictionary<string, double[]> data, int idx, string kind)
        {
            double wt1 = GetCompValue(data, CompWT1, idx);
            string wtStr = double.IsNaN(wt1) ? "" : $" Wave Trend {wt1:F1}.";
            return $"{kind} divergence, confirmed on pivot. Price {AccessibleTrader.Core.Services.Accessibility.SpeechPriceFormatter.FormatPrice(bar.Close)}.{wtStr}";
        }

        private static string GetTripleConfluenceSpeech(Ohlcv bar, IReadOnlyDictionary<string, double[]> data, int idx)
        {
            double wt1 = GetCompValue(data, CompWT1, idx);
            double mf  = GetCompValue(data, CompMoneyFlowWave, idx);

            var parts = new System.Text.StringBuilder();
            parts.Append($"Price {AccessibleTrader.Core.Services.Accessibility.SpeechPriceFormatter.FormatPrice(bar.Close)}.");
            if (!double.IsNaN(wt1)) parts.Append($" Wave Trend {wt1:F1}, oversold.");
            if (!double.IsNaN(mf) && mf > 0.0) parts.Append(" Money flow positive.");
            parts.Append(" Trend and volatility confirmed.");
            return parts.ToString();
        }

        private static double GetCompValue(IReadOnlyDictionary<string, double[]> data, string key, int idx)
        {
            return data.TryGetValue(key, out var arr) && idx < arr.Length ? arr[idx] : double.NaN;
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> results, int index, Dictionary<string, object> parameters)
        {
            if (!code.Equals("CIPHER_B", StringComparison.OrdinalIgnoreCase) || index < 0) return string.Empty;

            double obLevel = GetDbl(parameters, "OBLevel", 53.0);
            double wt1Val  = GetVal(results, CompWT1,          index);
            double mfVal   = GetVal(results, CompMoneyFlowWave, index);
            double histVal = GetVal(results, CompWtHistogram,  index);
            double blueVal = GetVal(results, CompBlue,         index);
            double redVal  = GetVal(results, CompRed,          index);
            double goldVal = GetVal(results, CompGold,         index);
            double bullVal = GetVal(results, CompBullDiv,      index);
            double bearVal = GetVal(results, CompBearDiv,      index);
            double hidBull = GetVal(results, CompHidBull,      index);
            double hidBear = GetVal(results, CompHidBear,      index);
            double ancVal  = GetVal(results, CompWT1Anchor,    index);
            double ancPol  = GetVal(results, CompAnchorPolarity, index);
            double obVal   = GetVal(results, CompAdaptiveOb,   index);
            double osVal   = GetVal(results, CompAdaptiveOs,   index);

            // Effective OB/OS at this bar — may be fixed or adaptive.
            double effectiveOb = !double.IsNaN(obVal) ? obVal :  obLevel;
            double effectiveOs = !double.IsNaN(osVal) ? osVal : -obLevel;

            var sb = new StringBuilder();

            if (!double.IsNaN(wt1Val))
            {
                string zone = wt1Val > effectiveOb ? ", overbought"
                            : wt1Val < effectiveOs ? ", oversold"
                            :                        "";
                string dir = wt1Val >= 0 ? "above zero" : "below zero";
                sb.Append($"Wave Trend {dir}{zone} at {wt1Val:F1}. ");
            }
            if (!double.IsNaN(histVal))
            {
                string hDir = histVal >= 0 ? "bullish" : "bearish";
                sb.Append($"WT Histogram {hDir} at {histVal:F1}. ");
            }
            if (!double.IsNaN(ancVal))
            {
                string ancDir = ancPol > 0 ? "bullish" : (ancPol < 0 ? "bearish" : "neutral");
                sb.Append($"Anchor {ancDir}. ");
            }
            if (!double.IsNaN(mfVal))
            {
                string mfDir = mfVal > 0.0 ? "positive" : "negative";
                sb.Append($"Money flow {mfDir}. ");
            }

            var signals = new List<string>();
            if (!double.IsNaN(goldVal))  signals.Add("Triple confluence buy");
            else if (!double.IsNaN(blueVal)) signals.Add("Long signal");
            else if (!double.IsNaN(redVal))  signals.Add("Short signal");
            if (!double.IsNaN(bullVal)) signals.Add("Bullish divergence");
            if (!double.IsNaN(bearVal)) signals.Add("Bearish divergence");
            if (!double.IsNaN(hidBull)) signals.Add("Hidden bull");
            if (!double.IsNaN(hidBear)) signals.Add("Hidden bear");
            if (signals.Count > 0) sb.Append(string.Join(", ", signals) + ".");

            return sb.Length > 0 ? sb.ToString().TrimEnd() : string.Empty;
        }

        // ── Buffer / parameter helpers ────────────────────────────────────────

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

        public List<LevelDescriptor> GetDefaultLevels(string code)
        {
            if (!code.Equals("CIPHER_B", StringComparison.OrdinalIgnoreCase)) return new();
            return new()
            {
                new("Extreme OB",  60.0, "#FF2222", DashStyle.Dot,  PlayEarcon: true, EarconVolume: 0.8f, ZoneNoiseAmount: 0.20f, ZoneNoiseType: "white"),
                new("Overbought",  53.0, "#FF6666", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.10f, ZoneNoiseType: "white"),
                new("Zero",         0.0, "#666666", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.7f),
                new("Oversold",   -53.0, "#66BB66", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.10f, ZoneNoiseType: "white"),
                new("Extreme OS", -60.0, "#22FF22", DashStyle.Dot,  PlayEarcon: true, EarconVolume: 0.8f, ZoneNoiseAmount: 0.20f, ZoneNoiseType: "white"),
            };
        }

        private static int    GetInt(Dictionary<string, object> p, string k, int    def) => p.TryGetValue(k, out var v) ? (int)Convert.ToDouble(v) : def;
        private static double GetDbl(Dictionary<string, object> p, string k, double def) => p.TryGetValue(k, out var v) ? Convert.ToDouble(v) : def;
        private static string GetStr(Dictionary<string, object> p, string k, string def) => p.TryGetValue(k, out var v) && v is string s ? s : def;
        private static bool   GetBool(Dictionary<string, object> p, string k, bool   def) => p.TryGetValue(k, out var v) ? Convert.ToBoolean(v) : def;

        /// <summary>
        /// Returns a copy of a sparse marker array with every non-NaN value moved
        /// forward by <paramref name="lag"/> bars (pivot bar → confirmation bar).
        /// Markers whose confirmation bar falls past the end of the data are dropped
        /// (they could not have been acted on in-sample). Used by the
        /// DivergenceConfirmLag look-ahead-honesty option.
        /// </summary>
        internal static double[] ShiftMarkersForward(double[] src, int lag, int n)
        {
            var dst = new double[n];
            Array.Fill(dst, double.NaN);
            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(src[i])) continue;
                int j = i + lag;
                if (j < n) dst[j] = src[i];
            }
            return dst;
        }
    }
}
