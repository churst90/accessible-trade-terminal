using System;
using System.Collections.Generic;
using System.Text;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Accessible Cipher B — native C# replica of the Market Cipher B indicator suite.
    ///
    /// Wave components (oscillator pane):
    ///   WT1                      — Wave Trend fast line; gray/white — the "cutter"
    ///   WT2                      — Wave Trend slow channel; teal/blue — the wave body
    ///   WT Fill                  — Cloud fill between WT1 and WT2 (defined in DefaultCloudFills)
    ///   Money Flow Wave          — Continuous MF oscillator with green/red zero-crossing area fill
    ///   Money Flow               — Zero-line signal dots colored by MF sign
    ///   RSI~                     — Laguerre RSI (gamma-smoothed, ≈market cipher purple line)
    ///   Stoch %K                 — Stochastic RSI fast line
    ///   Stoch %D                 — Stochastic RSI slow line
    ///   VWAP~                    — Rolling VWAP deviation oscillator (approximation, pink)
    ///
    /// Signal dots:
    ///   Oversold Crossover       — WT1 × WT2 from oversold (buy dot, blue)
    ///   Overbought Crossover     — WT1 × WT2 from overbought (sell dot, red)
    ///   Triple Confluence Buy    — Oversold cross + RSI oversold + positive MF (gold dot)
    ///   Bullish Divergence       — Regular bullish divergence at pivot low (green dot)
    ///   Bearish Divergence       — Regular bearish divergence at pivot high (red dot)
    ///   Hidden Bull Continuation — Hidden bullish divergence / trend continuation (teal dot)
    ///   Hidden Bear Continuation — Hidden bearish divergence / trend continuation (orange dot)
    ///
    /// Reference levels: ±60 (extreme), ±53 (OB/OS), 0 (zero).
    ///
    /// Parameters:
    ///   WT1Period      — Wave Trend channel period (default 9)
    ///   WT2Period      — Wave Trend average period (default 12)
    ///   MFPeriod       — Money flow smoothing period (default 3)
    ///   OBLevel        — Overbought threshold (default 53)
    ///   RSIPeriod      — RSI period for gold signal and Laguerre base (default 14)
    ///   RSIOSLevel     — RSI oversold for gold signal (default 30)
    ///   PivotBars      — Bars each side for divergence pivot detection (default 3)
    ///   LaguerreGamma  — Laguerre RSI smoothing factor 0–1; higher = smoother (default 0.7)
    ///   StochRSIPeriod — Stochastic RSI lookback period (default 14)
    ///   StochKSmooth   — %K SMA smoothing period (default 3)
    ///   StochDSmooth   — %D SMA smoothing period (default 3)
    ///   VWAPPeriod     — Rolling VWAP lookback period (default 20)
    /// </summary>
    public class CipherBProvider : IIndicatorProvider
    {
        public string Name => "Cipher B";

        // ── Component name constants ──────────────────────────────────────────
        public const string CompWT1          = "Wave Trend";
        public const string CompWT2          = "Wave Trend 2";
        public const string CompMoneyFlowWave= "Money Flow Wave";
        public const string CompCrossBull    = "WaveTrend Cross Bull";
        public const string CompCrossBear    = "WaveTrend Cross Bear";
        public const string CompLaguerreRSI  = "RSI~";
        public const string CompStochK       = "Stoch %K";
        public const string CompStochD       = "Stoch %D";
        public const string CompVWAP         = "VWAP~";
        public const string CompBlue         = "Oversold Crossover";
        public const string CompRed          = "Overbought Crossover";
        public const string CompGold         = "Triple Confluence Buy";
        public const string CompBullDiv      = "Bullish Divergence";
        public const string CompBearDiv      = "Bearish Divergence";
        public const string CompHidBull      = "Hidden Bull Continuation";
        public const string CompHidBear      = "Hidden Bear Continuation";
        public const string CompWT1Anchor    = "Anchor Wave";
        public const string CompWT2Anchor    = "Anchor Wave 2";
        public const string CompTrigger      = "Trigger Wave";

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
                    // Period-scaled WT pair (5× default) for macro wave structure context.
                    // Lines are hidden by default — the Anchor Fill cloud in DefaultCloudFills
                    // carries the visual, matching how real MC-B renders the anchor as a band
                    // rather than exposed lines. Power users can toggle lines on via Properties.
                    // Audio: Background layer — data still available for playback/navigation
                    // even when the lines are hidden, audible if user un-hides them.
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

                    // ── Wave lines (on top of anchors) ───────────────────────
                    // WT1 is a thinner "cutter" line — sits above/below WT2.
                    // Audio: triangle above zero (sharp angular cutter), sawtooth below zero (cutting descent).
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
                    // WT2 is the dominant wave body — slightly thicker to read as the main oscillator.
                    // Audio: smooth sine throughout — channel/envelope character, no waveform switch at zero.
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

                    // ── Trigger Wave (MACD-of-WT1) ───────────────────────────
                    // WT1 minus EMA(WT1, TriggerPeriod) — a fast momentum derivative.
                    // Thin bright yellow line that leads WT1/WT2 crossovers by 1-2 bars.
                    // Hidden by default — present in MC-B but off by default; toggle on in Properties.
                    // Audio: Midground, triangle, slightly higher freq multiplier (1.3×) for "ahead" feel.
                    new() { Name = CompTrigger,       DisplayName = "Trigger Wave", DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#FFEB3B", DefaultThickness = 1.5f, IsVisible = false,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultWaveform = "triangle",
                            DefaultAboveWaveform = "triangle", DefaultBelowWaveform = "sine",
                            DefaultReferenceLevel = 0.0, DefaultEnvelopeType = "Sustain",
                            DefaultFreqMultiplier = 1.3,
                            DefaultTriggerBoundaryClick = true,
                            DefaultIsAreaFill = false, DefaultUsePolarityColoring = false },

                    // ── Money Flow Histogram — sub-pane strip anchored at -80 (neutral centre).
                    // Uses RSI(hlc3*volume, period) scaled to −100..−60, centre at −80.
                    // Rendered as Histogram (discrete per-candle bars) matching real MCB's MF strip.
                    // DefaultReferenceLevel = −80 drives:
                    //   • bar height: bars drawn from −80 to data value
                    //   • color split: value > −80 = green (buying), value < −80 = red (selling)
                    //   • audio Direction split: value > −80 = bullish freq, < −80 = bearish freq
                    // ColorBaseline = −80 routes the bar color correctly through ColorSource.Value.
                    // Audio: Background, Direction pitch — 300 Hz buying / 100 Hz selling.
                    // SubscribedLevelNames = [] because MF values live in the −100..−60 range and
                    // would always appear "oversold" relative to the WT ±53/60 thresholds. The real
                    // MCB Money Flow strip has no reference lines — it uses bar colour alone.
                    new() { Name = CompMoneyFlowWave, DisplayName = "Money Flow", DisplayType = ComponentDisplayType.Histogram, Role = ComponentRole.Signal,
                            DefaultColorHex = "#00E676", DefaultColorHexSecondary = "#FF1744",
                            DefaultColorSource = ColorSource.Value,
                            ColorBaseline = -80.0,
                            DefaultReferenceLevel = -80.0,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultPitchMapping = PitchMapping.Direction,
                            DefaultBullishFrequency = 300.0,
                            DefaultBearishFrequency = 100.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.ReferenceDeviation,
                            DefaultDeviationNorm = 20.0,   // MF range is −100..−60 (±20 from −80); without this the
                                                           // WT pane range (±180 from −80) makes bars nearly silent.
                            SpeechTemplate = "Money Flow. {value:F1}.",
                            SubscribedLevelNames = Array.Empty<string>() },

                    // ── WaveTrend crossover dots (all crosses) ────────────────────
                    // Real MCB places a SMALL dot at every WT1/WT2 crossover regardless of OB/OS level,
                    // sitting just under/over the WT2 wave body.  The existing CompBlue/CompRed dots
                    // cover the LARGE circle at oversold/overbought crosses — these small dots fire at
                    // every cross and are visually subordinate (smaller, less opaque).
                    // Audio: Foreground, very short ping — just a tick to mark the crossing event.
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

                    // ── Oscillator overlays ──────────────────────────────────
                    // RSI~/Stoch are secondary oscillators — thin and slightly transparent to stay
                    // visually subordinate to the dominant WT1/WT2 waves (matches MC-B look).
                    // Declared as Oscillator so: (a) speech says "Oscillator", (b) ReferenceLevel=0.0
                    // is auto-applied by IndicatorModelFactory, activating the above/below waveform switch.
                    // Audio: Background layer for all three — contextual, should not dominate the mix.
                    // RSI~ is Laguerre RSI normalised to ±50 (raw 0–1 → −50..+50).
                    // Its gamma-smoothed nature means it rarely reaches the WT's ±53/60 OB/OS
                    // thresholds, so subscribing to those levels would produce misleading earcons.
                    // SubscribedLevelNames = ["Zero"] restricts it to zero-crossing only —
                    // the crossing earcon fires, but OB/OS zone noise does not apply.
                    new() { Name = CompLaguerreRSI,   DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#E600E6BF", DefaultThickness = 1.0f,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultWaveform = "triangle",
                            DefaultAboveWaveform = "triangle",
                            DefaultBelowWaveform = "sine",
                            DefaultReferenceLevel = 0.0, DefaultEnvelopeType = "Sustain",
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultNoiseAmount = 0.15f, DefaultTriggerBoundaryClick = true,
                            SpeechTemplate = "Smoothed RSI. Oscillator. {value:F1}.",
                            DefaultIsAreaFill = false, DefaultUsePolarityColoring = false,
                            SubscribedLevelNames = new[] { "Zero" } },
                    // Stoch %K is normalised to ±35 (raw 0–100 → (v/100−0.5)×70). OB/OS at ±21.
                    new() { Name = CompStochK,        DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#00B8D4B0", DefaultThickness = 1.0f, IsVisible = false,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultWaveform = "triangle",
                            DefaultAboveWaveform = "triangle",
                            DefaultBelowWaveform = "sine",
                            DefaultReferenceLevel = 0.0, DefaultEnvelopeType = "Sustain",
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultNoiseAmount = 0.15f, DefaultTriggerBoundaryClick = true,
                            SpeechTemplate = "Stochastic K. Oscillator. {value:F1}.",
                            DefaultIsAreaFill = false, DefaultUsePolarityColoring = false },
                    new() { Name = CompStochD,        DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#E65100B0", DefaultThickness = 1.0f, IsVisible = false,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultWaveform = "triangle",
                            DefaultAboveWaveform = "triangle",
                            DefaultBelowWaveform = "sine",
                            DefaultReferenceLevel = 0.0, DefaultEnvelopeType = "Sustain",
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultNoiseAmount = 0.15f, DefaultTriggerBoundaryClick = true,
                            SpeechTemplate = "Stochastic D. Oscillator. {value:F1}.",
                            DefaultIsAreaFill = false, DefaultUsePolarityColoring = false },
                    // VWAP~: rolling VWAP deviation oscillator — zero = price at VWAP, above/below = premium/discount.
                    // No fixed OB/OS zone for VWAP deviation — threshold varies by asset volatility — so no
                    // boundary declarations. Zero-crossing click comes from DefaultTriggerBoundaryClick=true
                    // combined with the auto-applied ReferenceLevel=0 for Oscillator display type.
                    new() { Name = CompVWAP,          DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#F06292", DefaultThickness = 1.0f, IsVisible = false,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultWaveform = "triangle",
                            DefaultAboveWaveform = "triangle",
                            DefaultBelowWaveform = "sine",
                            DefaultReferenceLevel = 0.0, DefaultEnvelopeType = "Sustain",
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultTriggerBoundaryClick = true,
                            SpeechTemplate = "VWAP deviation. Oscillator. {value:F1}. {zone}.",
                            DefaultIsAreaFill = false, DefaultUsePolarityColoring = false },

                    // ── Signal dots (crossover buy/sell) ─────────────────────
                    // Oversold Crossover (Blue): primary long entry — bright, high pitch.
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
                    // Overbought Crossover (Red): primary short entry — dark, low pitch.
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
                    // Triple Confluence Buy (Gold): highest-confidence signal — dual simultaneous tones (440+660 Hz golden chord).
                    new() { Name = CompGold,          DisplayType = ComponentDisplayType.Dot,        Role = ComponentRole.Signal,
                            DefaultColorHex = "#FFD700", DefaultThickness = 6.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "dual_tone_bell",
                            DefaultDecayMs = 500,
                            DefaultBaseFrequency = 440.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Triple confluence buy, strong confirmation",
                            DefaultUsePolarityColoring = false },

                    // ── Divergence dots — remain Dot so Ctrl+Left/Right sparse navigation works ──
                    // (Diamond shape is available for future indicators that don't need Dot-based navigation)
                    // Bullish Divergence (Green): triangle_bell, 620 Hz, 230ms.
                    new() { Name = CompBullDiv,       DisplayType = ComponentDisplayType.Dot,        Role = ComponentRole.Signal,
                            DefaultColorHex = "#00E676", DefaultThickness = 5.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "triangle_bell",
                            DefaultDecayMs = 230,
                            DefaultBaseFrequency = 620.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Bullish divergence",
                            DefaultUsePolarityColoring = false },
                    // Bearish Divergence (Red): triangle_bell, 310 Hz, 230ms.
                    new() { Name = CompBearDiv,       DisplayType = ComponentDisplayType.Dot,        Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF1744", DefaultThickness = 5.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "triangle_bell",
                            DefaultDecayMs = 230,
                            DefaultBaseFrequency = 310.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Bearish divergence",
                            DefaultUsePolarityColoring = false },

                    // ── Hidden continuation dots — remain Dot for the same navigation reason ──
                    // Hidden Bull Continuation (Teal): triangle_bell, 520 Hz, 180ms.
                    new() { Name = CompHidBull,       DisplayType = ComponentDisplayType.Dot,        Role = ComponentRole.Signal,
                            DefaultColorHex = "#26C6DA", DefaultThickness = 5.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "triangle_bell",
                            DefaultDecayMs = 180,
                            DefaultBaseFrequency = 520.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Hidden bullish continuation",
                            DefaultUsePolarityColoring = false },
                    // Hidden Bear Continuation (Orange): triangle_bell, 360 Hz, 180ms.
                    new() { Name = CompHidBear,       DisplayType = ComponentDisplayType.Dot,        Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF9800", DefaultThickness = 5.0f,
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
                    // Anchor Fill: the macro-wave band between the two anchor WT lines.
                    // This is the primary visual for the anchor waves — the lines themselves are
                    // hidden by default (IsVisible = false above), matching how real MC-B renders
                    // the anchor as a broad background wash rather than exposed line plots.
                    //
                    // Colors match the real MC-B anchor palette (#AARRGGBB — SkiaSharp format):
                    //   Bullish — deep navy/indigo (#1A237E) at ~20% opacity (AA=33): macro uptrend context
                    //   Bearish — deep crimson (#880E4F) at ~20% opacity (AA=33): macro downtrend context
                    // The low opacity ensures the anchor band never competes with the main WT fill
                    // or the signal dots. It reads as a soft "mood light" behind the chart.
                    //
                    // Rendered FIRST so it sits behind the WT Fill and all wave lines.
                    // Audio: very quiet deep tone during playback — 160 Hz bullish / 110 Hz bearish,
                    // Anchor Fill: slow macro cloud between the 5× anchor wave pair.
                    // MaxVolume=0.30 keeps it subordinate — it's a background context cue,
                    // not a primary signal. Deep frequencies (160/110 Hz) convey the broad trend.
                    new() { UpperComponentName = CompWT1Anchor, LowerComponentName = CompWT2Anchor,
                            BullishColorHex = "#331A237E", BearishColorHex = "#33880E4F",
                            DisplayName = "Anchor Fill", IsVisible = true,
                            Sonification = new CloudSonificationConfig(
                                BullishFrequency: 160f,
                                BearishFrequency: 110f,
                                SoundPatchId: "sine_bell",
                                DecayMs: 400,
                                MaxVolume: 0.30f) },   // 30% of master — background cue only

                    // WT Fill: semi-transparent channel between the two active wave lines.
                    // Matches Market Cipher B's translucent teal/red channel appearance.
                    // #AARRGGBB (SkiaSharp format) — bullish AA=59 hex ≈ 35% opacity, bearish AA=40 hex ≈ 25%.
                    // MaxVolume=0.70 gives it presence without competing with signal dots (Foreground).
                    new() { UpperComponentName = CompWT1, LowerComponentName = CompWT2,
                            BullishColorHex = "#590BBCF5", BearishColorHex = "#40FF1744",
                            DisplayName = "WT Fill", IsVisible = true,
                            Sonification = new CloudSonificationConfig(
                                BullishFrequency: 480f,
                                BearishFrequency: 200f,
                                SoundPatchId: "sine_bell",
                                DecayMs: 180,
                                MaxVolume: 0.70f) },   // 70% of master — audible mid-ground
                },
                Parameters = new List<IndicatorParameterMetadata>
                {
                    // Wave Trend core
                    new() { Name = "WT1Period",      DisplayName = "WT Channel Period",      DefaultValue = 9.0,  DataType = typeof(int)    },
                    new() { Name = "WT2Period",      DisplayName = "WT Average Period",      DefaultValue = 12.0, DataType = typeof(int)    },
                    new() { Name = "MFPeriod",       DisplayName = "Money Flow Period",      DefaultValue = 3.0,  DataType = typeof(int)    },
                    new() { Name = "OBLevel",        DisplayName = "OB/OS Threshold",        DefaultValue = 53.0, DataType = typeof(double) },
                    // Signal logic
                    new() { Name = "RSIPeriod",      DisplayName = "RSI Period (Gold/Base)", DefaultValue = 14.0, DataType = typeof(int)    },
                    new() { Name = "RSIOSLevel",     DisplayName = "RSI Oversold (Gold)",    DefaultValue = 30.0, DataType = typeof(double) },
                    new() { Name = "PivotBars",      DisplayName = "Divergence Pivot Bars",  DefaultValue = 3.0,  DataType = typeof(int)    },
                    // Laguerre RSI
                    new() { Name = "LaguerreGamma",  DisplayName = "Laguerre RSI Gamma",     DefaultValue = 0.7,  DataType = typeof(double) },
                    // Stochastic RSI
                    new() { Name = "StochRSIPeriod", DisplayName = "Stoch RSI Period",       DefaultValue = 14.0, DataType = typeof(int)    },
                    new() { Name = "StochKSmooth",   DisplayName = "Stoch %K Smooth",        DefaultValue = 3.0,  DataType = typeof(int)    },
                    new() { Name = "StochDSmooth",   DisplayName = "Stoch %D Smooth",        DefaultValue = 3.0,  DataType = typeof(int)    },
                    // VWAP oscillator
                    new() { Name = "VWAPPeriod",     DisplayName = "VWAP~ Approx Period",    DefaultValue = 20.0, DataType = typeof(int)    },
                    // Anchor Waves
                    new() { Name = "AnchorMultiplier", DisplayName = "Anchor Wave Multiplier", DefaultValue = 5.0, DataType = typeof(int),
                            Description = "Period multiplier for the anchor WT pair (default 5 = 5× WT periods)." },
                    // Trigger Wave
                    new() { Name = "TriggerPeriod",    DisplayName = "Trigger Wave Period",    DefaultValue = 4.0, DataType = typeof(int),
                            Description = "EMA period for the Trigger Wave derivative (WT1 − EMA(WT1))." },
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
            int    mfPeriod      = GetInt(parameters, "MFPeriod",       3);
            double obLevel       = GetDbl(parameters, "OBLevel",        53.0);
            int    rsiPeriod     = GetInt(parameters, "RSIPeriod",      14);
            double rsiOS         = GetDbl(parameters, "RSIOSLevel",     30.0);
            int    pivotBars     = GetInt(parameters, "PivotBars",      3);
            double lagGamma      = GetDbl(parameters, "LaguerreGamma",  0.7);
            int    stochPeriod   = GetInt(parameters, "StochRSIPeriod", 14);
            int    stochKSmooth  = GetInt(parameters, "StochKSmooth",   3);
            int    stochDSmooth  = GetInt(parameters, "StochDSmooth",   3);
            int    vwapPeriod    = GetInt(parameters, "VWAPPeriod",     20);
            int    anchorMult   = GetInt(parameters, "AnchorMultiplier", 5); // default matches metadata DefaultValue=5
            int    triggerPeriod = GetInt(parameters, "TriggerPeriod",   4);

            // ── Source series ─────────────────────────────────────────────────
            var close  = new double[n];
            var open   = new double[n];
            var high   = new double[n];
            var low    = new double[n];
            var volume = new double[n];
            var hlc3   = new double[n];
            for (int i = 0; i < n; i++)
            {
                close[i]  = data[i].Close;
                open[i]   = data[i].Open;
                high[i]   = data[i].High;
                low[i]    = data[i].Low;
                volume[i] = data[i].Volume;
                hlc3[i]   = (high[i] + low[i] + close[i]) / 3.0;
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

            // ── Anchor Waves (same WT algorithm, period-scaled) ───────────────
            // Identical computation to WT1/WT2 but using wt1Period×anchorMult and
            // wt2Period×anchorMult as periods. No extra infrastructure needed.
            int    ancPeriod1  = wt1Period * anchorMult;
            int    ancPeriod2  = wt2Period * anchorMult;
            var    esaAnc      = IndicatorMath.Ema(hlc3, ancPeriod1);
            var    absDiffAnc  = new double[n];
            for (int i = 0; i < n; i++)
                absDiffAnc[i] = double.IsNaN(esaAnc[i]) ? double.NaN : Math.Abs(hlc3[i] - esaAnc[i]);
            var    dAnc        = IndicatorMath.Ema(absDiffAnc, ancPeriod1);
            var    ciAnc       = new double[n];
            for (int i = 0; i < n; i++)
                ciAnc[i] = (double.IsNaN(dAnc[i]) || dAnc[i] < 1e-10) ? double.NaN : (hlc3[i] - esaAnc[i]) / (0.015 * dAnc[i]);
            var    wt1Anc      = IndicatorMath.Ema(ciAnc, ancPeriod2);
            var    wt2Anc      = IndicatorMath.Sma(wt1Anc, 4);

            // ── Money Flow ────────────────────────────────────────────────────
            // Two representations:
            //   mf          — normalised −1..+1, used for signal logic (gold dot, speech)
            //   mfDisplay   — RSI(hlc3×volume) scaled to −100..−60, used for visual wave
            //
            // mfDisplay matches the VuManChu Cipher B approach: RSI of volume-weighted price
            // maps to a range anchored at −80 in WT value space so the wave sits at the bottom
            // of the single-pane WT chart.  ReferenceLevel = −80 is set by StylingService so
            // RenderZeroArea fills relative to −80: above = green (buying), below = red (selling).

            // mf (−1..+1) for signal logic
            var mfDirV = new double[n];
            var mfTotV = new double[n];
            for (int i = 0; i < n; i++)
            {
                mfDirV[i] = close[i] >= open[i] ? volume[i] : -volume[i];
                mfTotV[i] = volume[i];
            }
            var mfDirSma = IndicatorMath.Sma(mfDirV, mfPeriod);
            var mfTotSma = IndicatorMath.Sma(mfTotV, mfPeriod);
            var mf = new double[n];
            for (int i = 0; i < n; i++)
                mf[i] = (!double.IsNaN(mfTotSma[i]) && mfTotSma[i] > 1e-10)
                    ? mfDirSma[i] / mfTotSma[i]
                    : double.NaN;

            // mfDisplay: RSI(hlc3×volume) − double-EMA smoothed − scaled to −100..−60
            const double MfCenter = -80.0, MfAmplitude = 20.0;
            var hlc3Vol = new double[n];
            for (int i = 0; i < n; i++) hlc3Vol[i] = hlc3[i] * volume[i];
            var mfRsiRaw = IndicatorMath.Rsi(hlc3Vol, mfPeriod);
            var mfScaled = new double[n];
            for (int i = 0; i < n; i++)
                mfScaled[i] = double.IsNaN(mfRsiRaw[i]) ? double.NaN
                    : (mfRsiRaw[i] - 50.0) / 50.0 * MfAmplitude + MfCenter;
            var mfEma1    = IndicatorMath.Ema(mfScaled, mfPeriod);
            var mfDisplay = IndicatorMath.Ema(mfEma1, mfPeriod);

            // ── RSI (Wilder — used for gold signal and as StochRSI base) ─────
            var rsi = IndicatorMath.Rsi(close, rsiPeriod);

            // ── Laguerre RSI (Ehlers, gamma-smoothed) ────────────────────────
            // Output normalised to WT pane: 0–1 → −50 to +50
            var lagRsi = IndicatorMath.LaguerreRsi(close, lagGamma);

            // ── Stochastic RSI (%K and %D) ────────────────────────────────────
            // Applied to the Wilder RSI series; same normalisation to WT range.
            var (stochK, stochD) = IndicatorMath.ComputeStochRsi(rsi, stochPeriod, stochKSmooth, stochDSmooth);

            // ── Rolling VWAP Oscillator (approximation) ───────────────────────
            // (close − rollingVWAP) / rollingStdDev × 15  →  fits WT range
            var vwapOsc = IndicatorMath.RollingVwapOscillator(hlc3, close, volume, vwapPeriod);

            // ── Trigger Wave (MACD-of-WT1) ────────────────────────────────────
            // triggerSignal = EMA(WT1, triggerPeriod); trigger = WT1 − triggerSignal.
            // The resulting line is a fast momentum derivative — positive means WT1 is
            // accelerating upward; negative means it's pulling back. Oscillates around
            // zero with smaller amplitude than WT1/WT2, so it reads as a leading signal.
            var triggerSignal = IndicatorMath.Ema(wt1, triggerPeriod);
            var trigger       = new double[n];
            for (int i = 0; i < n; i++)
                trigger[i] = (double.IsNaN(wt1[i]) || double.IsNaN(triggerSignal[i]))
                    ? double.NaN
                    : wt1[i] - triggerSignal[i];

            // ── Signal dots ───────────────────────────────────────────────────
            var crossBullSignal = new double[n];   // small dot — every cross-up
            var crossBearSignal = new double[n];   // small dot — every cross-down
            var blueSignal      = new double[n];   // large circle — oversold cross-up
            var redSignal       = new double[n];   // large circle — overbought cross-down
            var goldSignal      = new double[n];   // triple confluence buy
            Array.Fill(crossBullSignal, double.NaN);
            Array.Fill(crossBearSignal, double.NaN);
            Array.Fill(blueSignal, double.NaN);
            Array.Fill(redSignal,  double.NaN);
            Array.Fill(goldSignal, double.NaN);

            for (int i = 1; i < n; i++)
            {
                if (double.IsNaN(wt1[i]) || double.IsNaN(wt2[i]) ||
                    double.IsNaN(wt1[i-1]) || double.IsNaN(wt2[i-1])) continue;

                bool crossUp   = wt1[i-1] < wt2[i-1] && wt1[i] >= wt2[i];
                bool crossDown = wt1[i-1] > wt2[i-1] && wt1[i] <= wt2[i];

                // Small dot at every cross (MCB: appears at every WT1/WT2 crossing)
                if (crossUp)   crossBullSignal[i] = wt2[i] - 3.0;
                if (crossDown) crossBearSignal[i] = wt2[i] + 3.0;

                // Large circle only at OB/OS crossings
                if (crossUp && wt1[i] < -obLevel)
                    blueSignal[i] = wt2[i] - 5.0;
                if (crossDown && wt1[i] > obLevel)
                    redSignal[i] = wt2[i] + 5.0;

                if (crossUp && wt1[i] < -obLevel &&
                    !double.IsNaN(rsi[i]) && rsi[i] < rsiOS &&
                    !double.IsNaN(mf[i])  && mf[i]  > 0)
                    goldSignal[i] = wt2[i] - 12.0;
            }

            // ── Divergence detection ──────────────────────────────────────────
            var bullDiv  = new double[n];
            var bearDiv  = new double[n];
            var hidBull  = new double[n];
            var hidBear  = new double[n];
            Array.Fill(bullDiv, double.NaN);
            Array.Fill(bearDiv, double.NaN);
            Array.Fill(hidBull, double.NaN);
            Array.Fill(hidBear, double.NaN);

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

            for (int k = 1; k < pivotLowIdx.Count; k++)
            {
                int prev = pivotLowIdx[k - 1], curr = pivotLowIdx[k];
                if (double.IsNaN(close[prev]) || double.IsNaN(close[curr])) continue;
                if (close[curr] < close[prev] && wt1[curr] > wt1[prev]) bullDiv[curr] = wt1[curr] - 4.0;
                if (close[curr] > close[prev] && wt1[curr] < wt1[prev]) hidBull[curr] = wt1[curr] - 4.0;
            }
            for (int k = 1; k < pivotHighIdx.Count; k++)
            {
                int prev = pivotHighIdx[k - 1], curr = pivotHighIdx[k];
                if (double.IsNaN(close[prev]) || double.IsNaN(close[curr])) continue;
                if (close[curr] > close[prev] && wt1[curr] < wt1[prev]) bearDiv[curr] = wt1[curr] + 4.0;
                if (close[curr] < close[prev] && wt1[curr] > wt1[prev]) hidBear[curr] = wt1[curr] + 4.0;
            }

            // ── Write to buffer ───────────────────────────────────────────────
            WriteToBuffer(buffer, CompWT1,           wt1,             n);
            WriteToBuffer(buffer, CompWT2,           wt2,             n);
            WriteToBuffer(buffer, CompWT1Anchor,     wt1Anc,          n);
            WriteToBuffer(buffer, CompWT2Anchor,     wt2Anc,          n);
            WriteToBuffer(buffer, CompTrigger,       trigger,         n);
            WriteToBuffer(buffer, CompMoneyFlowWave, mfDisplay,       n);
            WriteToBuffer(buffer, CompCrossBull,     crossBullSignal, n);
            WriteToBuffer(buffer, CompCrossBear,     crossBearSignal, n);
            WriteToBuffer(buffer, CompLaguerreRSI,   lagRsi,          n);
            WriteToBuffer(buffer, CompStochK,        stochK,          n);
            WriteToBuffer(buffer, CompStochD,        stochD,          n);
            WriteToBuffer(buffer, CompVWAP,          vwapOsc,         n);
            WriteToBuffer(buffer, CompBlue,          blueSignal,      n);
            WriteToBuffer(buffer, CompRed,           redSignal,       n);
            WriteToBuffer(buffer, CompGold,          goldSignal,      n);
            WriteToBuffer(buffer, CompBullDiv,       bullDiv,         n);
            WriteToBuffer(buffer, CompBearDiv,       bearDiv,         n);
            WriteToBuffer(buffer, CompHidBull,       hidBull,         n);
            WriteToBuffer(buffer, CompHidBear,       hidBear,         n);
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            int wt1    = GetInt(parameters, "WT1Period",        9);
            int wt2    = GetInt(parameters, "WT2Period",        12);
            int rsi    = GetInt(parameters, "RSIPeriod",        14);
            int stoch  = GetInt(parameters, "StochRSIPeriod",   14);
            int vwap   = GetInt(parameters, "VWAPPeriod",       20);
            int anchor = GetInt(parameters, "AnchorMultiplier", 5);
            // Anchor periods are wt1*anchor and wt2*anchor — dominate the warm-up.
            int ancP1  = wt1 * anchor;
            int ancP2  = wt2 * anchor;
            return Math.Max(100, (ancP1 + ancP2) * 4 + rsi * 2 + stoch + vwap);
        }

        /// <summary>
        /// Returns an imperative speech string for the given component at the current bar.
        /// Handles components that require runtime context the declarative SpeechTemplate cannot
        /// express: oscillator OB/OS position (WT1/WT2/RSI~/Stoch/VWAP), Money Flow pressure
        /// label, and signal dots with confluences. Returns null to fall through to the
        /// SpeechTemplate or generic SpeechFormatter for components not handled here.
        /// </summary>
        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value))
                return "no data";

            // Providers return VALUE-ONLY strings. NavigationFeedbackManager prepends
            // "[Component Name]. [Type]. " on UP/DOWN moves, so providers must not include
            // the name themselves — that would double-announce it.

            return componentName switch
            {
                CompWT1         => GetWTValueSpeech(value),
                CompWT2         => GetWTValueSpeech(value),
                CompWT1Anchor   => $"{value:F1}",
                CompWT2Anchor   => $"{value:F1}",
                CompTrigger     => $"{value:F1}",

                // Money Flow Wave: values are in the −100..−60 range (anchored at −80 = neutral).
                // Normalise back to a −100..+100 percentage for readable speech:
                //   value −60 → +100% (strong buying), −80 → 0% (neutral), −100 → −100% (strong selling).
                CompMoneyFlowWave => GetMFWaveSpeech(value),

                // WaveTrend cross dots: report the WT level at the cross for directional context.
                CompCrossBull => GetSignalDotSpeech(bar, allComponentData, dataIndex),
                CompCrossBear => GetSignalDotSpeech(bar, allComponentData, dataIndex),

                // Oscillator overlays: value with zone context when relevant.
                // RSI~ is Laguerre RSI normalised to ±50 (0–1 → −50 to +50), so OB/OS
                // thresholds are ±20 (≈70th and 30th percentile of the ±50 range).
                CompLaguerreRSI => GetOscillatorValueSpeech(value, -20.0, 20.0),
                // Stoch values are normalised to ±35 (raw 0–100 → (v/100−0.5)×70).
                // Raw 80 → +21, raw 20 → −21: use those thresholds for zone speech.
                CompStochK      => GetOscillatorValueSpeech(value, -21.0, 21.0),
                CompStochD      => GetOscillatorValueSpeech(value, -21.0, 21.0),
                CompVWAP        => $"{value:F1}",

                // Signal dots: price + the WT1 value that caused the signal.
                // (stored value is a Y-offset, not the WT reading — read WT1 from component data)
                CompBlue    => GetSignalDotSpeech(bar, allComponentData, dataIndex),
                CompRed     => GetSignalDotSpeech(bar, allComponentData, dataIndex),
                CompBullDiv => GetSignalDotSpeech(bar, allComponentData, dataIndex),
                CompBearDiv => GetSignalDotSpeech(bar, allComponentData, dataIndex),
                CompHidBull => GetSignalDotSpeech(bar, allComponentData, dataIndex),
                CompHidBear => GetSignalDotSpeech(bar, allComponentData, dataIndex),

                // Triple Confluence: price + all three confirming values.
                CompGold => GetTripleConfluenceSpeech(bar, allComponentData, dataIndex),

                _ => null
            };
        }

        /// <summary>
        /// Money Flow Wave speech. Values are in the −100..−60 range anchored at −80 (neutral).
        /// Converts back to a −100..+100 percentage for readable TTS output.
        /// </summary>
        private static string GetMFWaveSpeech(double value)
        {
            double pct = (value - (-80.0)) / 20.0 * 100.0;
            string dir = pct >= 0 ? "buying" : "selling";
            return $"{Math.Abs(pct):F0}% {dir} pressure";
        }

        /// <summary>Wave Trend value-only speech: value with zone context (no name).</summary>
        private static string GetWTValueSpeech(double value)
        {
            if (value > 53)  return $"{value:F1}, overbought";
            if (value < -53) return $"{value:F1}, oversold";
            return $"{value:F1}";
        }

        /// <summary>Oscillator value-only speech: value with zone label when outside neutral band.</summary>
        private static string GetOscillatorValueSpeech(double value, double os, double ob)
        {
            if (value > ob) return $"{value:F1}, overbought";
            if (value < os) return $"{value:F1}, oversold";
            return $"{value:F1}";
        }

        /// <summary>Signal dot speech: price at the bar + actual WT1 oscillator value.</summary>
        private static string GetSignalDotSpeech(Ohlcv bar, IReadOnlyDictionary<string, double[]> data, int idx)
        {
            double wt1 = GetCompValue(data, CompWT1, idx);
            string wtStr = double.IsNaN(wt1) ? "" : $" Wave Trend {wt1:F1}.";
            return $"Price {bar.Close:F2}.{wtStr}";
        }

        /// <summary>Triple Confluence: price + WT1 + Laguerre RSI + money flow — all three confirming values.</summary>
        private static string GetTripleConfluenceSpeech(Ohlcv bar, IReadOnlyDictionary<string, double[]> data, int idx)
        {
            double wt1 = GetCompValue(data, CompWT1, idx);
            double rsi = GetCompValue(data, CompLaguerreRSI, idx);
            double mf  = GetCompValue(data, CompMoneyFlowWave, idx);

            var parts = new System.Text.StringBuilder();
            parts.Append($"Price {bar.Close:F2}.");
            if (!double.IsNaN(wt1)) parts.Append($" Wave Trend {wt1:F1}, oversold.");
            if (!double.IsNaN(rsi)) parts.Append($" RSI {rsi:F1}, oversold.");
            if (!double.IsNaN(mf) && mf > 0) parts.Append(" Money flow positive.");
            return parts.ToString();
        }

        private static double GetCompValue(IReadOnlyDictionary<string, double[]> data, string key, int idx)
        {
            return data.TryGetValue(key, out var arr) && idx < arr.Length ? arr[idx] : double.NaN;
        }

        /// <summary>
        /// Returns a spoken summary of the indicator's state at the given bar index.
        /// Triggered by Ctrl+Shift+D (full analysis) and F4 (context summary).
        /// Describes WT zone position, active signal dots, divergence state, Money Flow direction,
        /// Laguerre RSI position, and Triple Confluence conditions. Returns empty string when
        /// no meaningful fact is available (NaN data, wrong code, out-of-range index, etc.).
        /// </summary>
        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> results, int index, Dictionary<string, object> parameters)
        {
            if (!code.Equals("CIPHER_B", StringComparison.OrdinalIgnoreCase) || index < 0) return string.Empty;

            double obLevel = GetDbl(parameters, "OBLevel", 53.0);
            double wt1Val  = GetVal(results, CompWT1,          index);
            double mfVal   = GetVal(results, CompMoneyFlowWave, index);
            double lagVal  = GetVal(results, CompLaguerreRSI,  index);
            double stochKVal = GetVal(results, CompStochK,     index);
            double blueVal = GetVal(results, CompBlue,         index);
            double redVal  = GetVal(results, CompRed,          index);
            double goldVal = GetVal(results, CompGold,         index);
            double bullVal   = GetVal(results, CompBullDiv,      index);
            double bearVal   = GetVal(results, CompBearDiv,      index);
            double hidBull   = GetVal(results, CompHidBull,      index);
            double hidBear   = GetVal(results, CompHidBear,      index);
            double ancVal    = GetVal(results, CompWT1Anchor,    index);
            double trigVal   = GetVal(results, CompTrigger,      index);

            var sb = new StringBuilder();

            // Sentence 1: Wave Trend + Anchor status
            if (!double.IsNaN(wt1Val))
            {
                string zone = wt1Val > obLevel  ? ", overbought"
                            : wt1Val < -obLevel ? ", oversold"
                            :                     "";
                string dir = wt1Val >= 0 ? "above zero" : "below zero";
                sb.Append($"Wave Trend {dir}{zone} at {wt1Val:F1}. ");
            }
            if (!double.IsNaN(ancVal))
            {
                string ancZone = ancVal > obLevel  ? ", overbought"
                               : ancVal < -obLevel ? ", oversold"
                               :                     "";
                sb.Append($"Anchor wave {ancVal:F1}{ancZone}. ");
            }
            if (!double.IsNaN(trigVal))
            {
                string trigDir = trigVal >= 0 ? "positive" : "negative";
                sb.Append($"Trigger {trigDir} at {trigVal:F1}. ");
            }

            // Sentence 2: Money flow direction
            if (!double.IsNaN(mfVal))
            {
                string mfDir = mfVal > 0 ? "positive" : "negative";
                sb.Append($"Money flow {mfDir}. ");
            }

            // Sentence 3: Active signal dots (comma-separated)
            var signals = new List<string>();
            if (!double.IsNaN(goldVal))  signals.Add("Triple confluence buy");
            else if (!double.IsNaN(blueVal)) signals.Add("Long signal");
            else if (!double.IsNaN(redVal))  signals.Add("Short signal");
            if (!double.IsNaN(bullVal))  signals.Add("Bullish divergence");
            if (!double.IsNaN(bearVal))  signals.Add("Bearish divergence");
            if (!double.IsNaN(hidBull))  signals.Add("Hidden bull");
            if (!double.IsNaN(hidBear))  signals.Add("Hidden bear");
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
    }
}
