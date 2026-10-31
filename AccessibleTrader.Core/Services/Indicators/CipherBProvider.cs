using System.Text;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Accessible Cipher B — a native C# replica of Market Cipher B (MCB), fitted on 2026-10-17 to
    /// five dated MCB screenshots (the strongest a TradingView chart whose status line reads
    /// "MarketCipher B (2.2.6, …)") and on 2026-10-23 checked against MCB 2.4.0's own plotted
    /// values (BTCUSD 1D, 5,513 bars): waves, VWAP, both 0–100 lines and every dot exact, the money
    /// flow not (correlation 0.97). The per-element evidence is in the comments below.
    ///
    /// <para><b>What MCB draws, and is shown by default:</b> the fast and slow WaveTrend waves (light-
    /// and dark-blue areas), MCB's "VWAP" (our Wave Gap, WT1 − WT2, one yellow area), the money
    /// flow (lime/red area) and the money-flow bar along the bottom, a small dot at every wave cross,
    /// the big green "Buy dot" (Oversold Crossover), the 0–100 "RSI" and "Stochastic" lines (drawn,
    /// muted), and plain level lines at ±60, ±53 and zero. There is no gold dot: Market Cipher B
    /// draws none (removed 2026-10-22, Cody: "it isn't faithful"); its rule and look are kept, with
    /// no indicator using them, in <see cref="GoldDot"/>.</para>
    ///
    /// <para><b>Nothing that is not Market Cipher B's</b> (removed 2026-10-31, Cody: "Yes remove all
    /// of that old code from cipher b"): the red Overbought Crossover, the regular and hidden
    /// divergences with their pivot-to-pivot lines, the higher-timeframe anchor waves, Anchor
    /// Polarity, the Anchor Fill and WT Fill bands, and the older behaviour behind parameters (the
    /// Legacy money flow, the Gated buy dot, percentile thresholds, confirmation and cooldown bars,
    /// anchor suppression and timeframe-aware gates). None appears on any MCB chart or in MCB
    /// 2.4.0's data window. A strategy or alert that names a removed part is told so
    /// (SignalCatalog.Retired); a saved chart drops them on load (IndicatorModelFactory, layer 1,
    /// and WorkspaceInitializer for the two bands).</para>
    ///
    /// <para><b>Retired</b> (still in old saves and old docs): Stoch %K/%D, Laguerre RSI, Trigger
    /// Wave and a rolling-VWAP deviation (before 2026-10; the 2026-10-17 "RSI" / "Stochastic" lines
    /// are MCB's own, not these); the red/green OB/OS zone shading; the Adaptive OB / Adaptive OS
    /// components; the gold dot (2026-10-22); and everything listed above (2026-10-31).</para>
    /// </summary>
    public class CipherBProvider : IIndicatorProvider, IComputesHeikinAshiItself
    {
        public string Name => "Cipher B";

        // ── Component name constants ──────────────────────────────────────────
        public const string CompWT1          = "Wave Trend";
        public const string CompWT2          = "Wave Trend 2";
        public const string CompWtHistogram  = "WT Histogram";
        public const string CompMoneyFlowWave= "Money Flow Wave";
        /// <summary>The names the user hears for <see cref="CompWT1"/> and
        /// <see cref="CompMoneyFlowWave"/> — and the keys the app's speech formatter hands this
        /// provider's speech its data under (see <see cref="SpeechValue"/>).</summary>
        internal const string DisplayWT1 = "Wave Trend Fast", DisplayMoneyFlow = "Money Flow";
        /// <summary>What the user hears and sees for <see cref="CompWtHistogram"/> (the name kept
        /// for saved charts, strategies and alerts): the gap between the two waves, WT1 − WT2.
        /// Named 2026-10-30 (Cody: "what is a more appropriate name for it").</summary>
        internal const string DisplayWaveGap = "Wave Gap";
        public const string CompCrossBull    = "WaveTrend Cross Bull";
        public const string CompCrossBear    = "WaveTrend Cross Bear";
        /// <summary>MCB's big green buy dot. Named "Blue" for the colour it had until 2026-10-17;
        /// the name is kept because strategies, alerts and tests reference it. Heard and shown as
        /// <see cref="DisplayBuyDot"/>.</summary>
        public const string CompBlue         = "Oversold Crossover";
        // The gold dot ("Triple Confluence Buy", shown as "Gold dot") was removed on 2026-10-22:
        // Market Cipher B has none. A strategy or alert that names it is told why
        // (SignalCatalog.Retired); its rule and look are in GoldDot.
        /// <summary>What the user hears and sees for <see cref="CompBlue"/> — the status line,
        /// Ctrl+Up/Down, Alt+Shift+D, narration, Properties and the alert and strategy pickers all
        /// read DisplayName (2026-10-18, Cody: "Buy dot").</summary>
        internal const string DisplayBuyDot = "Buy dot";
        /// <summary>
        /// The pane's levels — the waves' scale, ±53 / ±60 and zero. Named explicitly on every
        /// component that used to take "all levels" (null), so the undrawn 80/20 below reach only
        /// the two 0–100 lines; nothing that heard ±53 before hears anything new.
        /// </summary>
        internal static readonly string[] WaveLevels = { "Extreme OB", "Overbought", "Zero", "Oversold", "Extreme OS" };

        /// <summary>The 0–100 RSI and Stochastic lines' own extremes: undrawn levels at 80 and 20.</summary>
        internal static readonly string[] LineLevels = { "Stoch Overbought", "Stoch Oversold" };

        /// <summary>The settings, named as Market Cipher B 2.4.0 names its inputs (see Parameters).</summary>
        public const string ParamUseChartTimeframe = "UseChartTimeframe", ParamOtherTimeframe = "OtherTimeframe",
                            ParamOBLevel1 = "OBLevel1", ParamTrigger1 = "Trigger1",
                            ParamOSLevel1 = "OSLevel1", ParamTrigger2 = "Trigger2";

        /// <summary>MCB's fixed periods: the fast wave's channel and average (9, 12), the slow wave's
        /// SMA (3) and the money flow's average (60). MCB offers no setting for any of them.</summary>
        internal const int WaveChannelBars = 9, WaveAverageBars = 12, SlowWaveBars = 3, MoneyFlowBars = 60;

        // The red Overbought Crossover, the four divergences, the anchor waves, Anchor Polarity and
        // the adaptive-threshold arrays were removed on 2026-10-31 (see the class summary); their
        // names live on only in SignalCatalog.Retired.
        public const string CompMcbRsi       = "RSI";
        public const string CompMcbStoch     = "Stochastic";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "CIPHER_B",
                Causality = ComponentCausality.Causal,
                Name        = "Cipher B",
                Category    = "Multi-Signal",
                DefaultPane = "Pane_CIPHER_B",
                // The wave trend lives in ±100 and the OB/OS levels sit at ±53/±60; the axis
                // never shrinks inside this. Was a by-name floor in ViewportRangeCalculator.
                // −103 is the bottom of Market Cipher B's money-flow bar (Trigger 2 − 50), which
                // its pane's scale includes (2026-10-31; was −100).
                RangeMin = -103, RangeMax = 100,
                Components = new List<IndicatorComponentMetadata>
                {
                    // ── Wave Gap (component name "WT Histogram") — MCB's "VWAP" plot ──
                    // This is WT1 − WT2. Real MCB's "VWAP" has the same sign as WT1 − WT2 on 96%
                    // of bars (BTCUSD 1D Bitstamp HA, Jul 2021 – Aug 2022, digitised from a
                    // TradingView chart whose status line reads "MarketCipher B (2.2.6, …)"), so
                    // it flips at the WT crosses. The old rolling VWAP deviation was the wrong
                    // abstraction.
                    //
                    // ONE colour, MCB's yellow, both sides of zero. Until 2026-10-17 this was a
                    // green/red pair (#00FF7F / #FF2D55) on the claim that MCB draws green/red
                    // "VWAP" bars; no MCB screenshot shows that. Every one shows a single yellow
                    // area: the price-scale label of MCB's VWAP plot reads exactly #FFEB3B
                    // (TradingView idea LfGIzkND, Oct 2023), and the official product image on
                    // marketciphertrading.com plus two 2022 charts composite to #FFEB3B at about
                    // 73% opacity within 6 per channel. The green/red pair also sat on top of
                    // Money Flow's (#00E676 / #FF1744), so the two keys and the two sets of bars
                    // looked alike (docs/TODO.md, 2026-10-16).
                    // Shape (2026-10-17): an AREA from zero at MCB's ~73% opacity (#BA = 186),
                    // painted OVER the two waves (DrawOrder −1; the waves are −2) as MCB paints it
                    // — while it stays FIRST in the declared order, so the keyboard still meets it
                    // first. Colour is not read by audio or speech.
                    // Two forms (2026-10-30, Cody: "perhaps a toggle here would be best, with the
                    // waves being the default view ... it's a yellow filled area so this should
                    // read correctly as well"). WAVE, the default: an Oscillator painted as the
                    // area above (DefaultAreaBySign), sounded as a wave — pitch follows the value
                    // on the pane's own ±100 scale, the scale it is drawn on — with no levels
                    // (it has no overbought or oversold) and a click at each zero cross.
                    // HISTOGRAM, Properties → Appearance → Read as: the same picture, read and
                    // navigated as a histogram and sounded by the bar rule (constant loudness,
                    // grit by size, direction pitch). The picture is the wave's in both forms.
                    // Heard as "Wave Gap": it is the gap between the fast and slow waves
                    // (WT1 − WT2), as MACD's histogram is the gap between its two lines. Not a
                    // volume-weighted price, whatever the original calls its plot.
                    new() { Name = CompWtHistogram,   DisplayName = DisplayWaveGap, DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            FormName = "wave",
                            // MCB 2.4.0 declares rgba(255,235,59,0.75) (2026-10-31; was #BA, 73%).
                            DefaultColorHex = "#BFFFEB3B", DefaultColorHexSecondary = "#BFFFEB3B",
                            DefaultIsAreaFill = true, DefaultAreaBySign = true, DefaultDrawOrder = -1,
                            DefaultColorSource = ColorSource.Value,
                            ColorBaseline = 0.0,
                            DefaultReferenceLevel = 0.0,
                            DefaultUsePolarityColoring = false,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultEnvelopeType = "Sustain",
                            DefaultWaveform = "square", DefaultAboveWaveform = "square", DefaultBelowWaveform = "sawtooth",
                            DefaultTriggerBoundaryClick = true, DefaultNoiseAmount = 0f,
                            SpeechTemplate = "Wave Gap. {value:F1}.",
                            SubscribedLevelNames = Array.Empty<string>(),
                            // The histogram form: the same picture, sounded and navigated as a
                            // histogram (only the type, sound and speech are a form's).
                            OtherForm = new() { Name = CompWtHistogram, DisplayName = DisplayWaveGap, DisplayType = ComponentDisplayType.Histogram, Role = ComponentRole.Signal,
                                FormName = "histogram",
                                DefaultPlaybackLayer = PlaybackLayer.Background,
                                DefaultPitchMapping = PitchMapping.Direction,
                                DefaultBullishFrequency = 360.0,
                                DefaultBearishFrequency = 140.0,
                                // No amplitude override: a histogram takes the shared bar rule
                                // (constant loudness, grit ∝ magnitude) from the sonification
                                // profile, like MACD's and volume's.
                                SpeechTemplate = "Wave Gap. {value:F1}.",
                                SubscribedLevelNames = Array.Empty<string>() } },

                    // ── The two waves: areas from zero, painted first (DrawOrder −2) ──
                    new() { Name = CompWT1,           DisplayName = DisplayWT1, DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            // Market Cipher B's fast wave: an OPAQUE light-blue (#90CAF9) area from
                            // zero, whose edge shows as a light-blue rim wherever it runs ahead of the
                            // slow wave. #90CAF9 is exact: MCB's composites inside both waves solve to
                            // #0D47A1 at 90% over #90CAF9 on all three channels, and the rim samples
                            // #90C6F6–#90C8F8 in the official product image and the 2.2.6 chart.
                            // Was a white line (#FFFFFF) until 2026-10-17.
                            // 2026-10-31: MCB 2.4.0's own declaration, an area of rgba(144,202,249,0.7)
                            // with a 1-pixel line (its plot styles, read from the script's published
                            // description and checked in a render of TVC:DJI 1D: where only the fast
                            // wave shows, #6C96BA = 70% #90CAF9 over the pane). The opaque #90CAF9 and
                            // 2-pixel edge were fitted to 2.2.6 screenshots.
                            DefaultColorHex = "#B390CAF9", DefaultThickness = 1.0f,
                            DefaultFillColorHex = "#B390CAF9", DefaultDrawOrder = -2,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultWaveform = "triangle",
                            DefaultAboveWaveform = "triangle",
                            DefaultBelowWaveform = "sine",
                            DefaultReferenceLevel = 0.0, DefaultEnvelopeType = "Sustain",
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultNoiseAmount = 0.15f, DefaultTriggerBoundaryClick = true,
                            SpeechTemplate = "Wave Trend 1. Oscillator. {value:F1}.",
                            DefaultIsAreaFill = true, DefaultUsePolarityColoring = false,
                            SubscribedLevelNames = WaveLevels },
                    new() { Name = CompWT2,           DisplayName = "Wave Trend Slow", DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            // Market Cipher B's slow wave: a #0D47A1 area at ~90% (#E6) over the fast
                            // one — the dark-blue body of every MCB chart; MCB's own price-scale label
                            // for it reads exactly #0D47A1 (TradingView idea LfGIzkND). Its EDGE is
                            // drawn in #1E88E5 rather than the body colour: #0D47A1 is only 1.7–2.4:1
                            // against the dark chart themes, so where the slow wave runs beyond the
                            // fast one (after every peak) a body-coloured edge would vanish; #1E88E5
                            // holds 3.6:1 or more on every theme but SteelGray (2.1:1, recorded in
                            // CipherBLookTests). Was a cyan line (#00D9FF) until 2026-10-17.
                            // 2026-10-31: MCB 2.4.0's own declaration, rgba(13,71,161,0.7) for the area
                            // AND its 1-pixel line (inside both waves #295EA8 = 70% #0D47A1 over 70%
                            // #90CAF9 over the pane, as rendered). The #1E88E5 edge above was our
                            // contrast choice, not MCB's; it went with Cody's "true to spec" (the
                            // contrast question is in docs/TODO.md).
                            DefaultColorHex = "#B30D47A1", DefaultThickness = 1.0f,
                            DefaultFillColorHex = "#B30D47A1", DefaultDrawOrder = -2,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultWaveform = "sine",
                            DefaultAboveWaveform = "triangle",
                            DefaultBelowWaveform = "sine",
                            DefaultReferenceLevel = 0.0, DefaultEnvelopeType = "Sustain",
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultNoiseAmount = 0.15f, DefaultTriggerBoundaryClick = true,
                            SpeechTemplate = "Wave Trend 2. Oscillator. {value:F1}.",
                            DefaultIsAreaFill = true, DefaultUsePolarityColoring = false,
                            SubscribedLevelNames = WaveLevels },

                    // ── Money Flow — a smoothed typical-price deviation ratio, centred on zero ──
                    // Volumeless (it works where volume is synthetic): 200/3 × the average over
                    // MFPeriod bars of each candle's hlc3 less its 5-bar mean, divided by the 5-bar
                    // mean size of that difference. Formula and fit evidence at its computation in
                    // Calculate. Clamped to ±100.
                    //
                    // An area from zero, coloured by its sign against ColorBaseline 0.
                    //
                    // Colours from MCB, not from VuManChu (#3EE145 / #FF3D2E). MCB's money-flow
                    // area is a lime green and a pure red at about 50% opacity: solved from its
                    // composites over both TradingView's #131722 background and MCB's own blue
                    // wave in a "MarketCipher B (2.2.6, …)" chart (green #248A12 / #2AA854, red
                    // #8A1212 / #8C3254 → about #3FFF00 and #FF1100 at alpha 0.5, within 8 per
                    // channel). #FF1100 is a red MCB itself uses (its Stoch line's label). The red
                    // over the blue wave is the maroon-pink that guides call MCB's "pink" area.
                    // Until 2026-10-17 this was #00E676 / #FF1744, a pair almost identical to
                    // the Wave Gap's.
                    // Shape (2026-10-17): an AREA at MCB's ~50% (#80), painted OVER the VWAP and the
                    // waves as MCB paints it (DrawOrder 0, after their −1 and −2).
                    // The two tones per side are compositing, not two areas: dark green over the
                    // background, teal over the blue wave; deep red over the background, maroon over
                    // the wave. Our fills measured within 6 per channel of MCB's in a render of the
                    // same window (2026-10-22). What was missing is the rim: MCB strokes the area's
                    // value edge in the same 50% colour, so the edge composites to about 75% over
                    // ~2 px (measured 0.65-0.74) — the brighter outline along the top of the wave.
                    // Two forms, as the Wave Gap's above (2026-10-30): a WAVE by default (sounded
                    // on the pane's scale) or a HISTOGRAM (the bar rule), one picture in both: the
                    // area (a green/red line when unfilled) and the Money Flow Bar band along the
                    // bottom, which is hidden with it.
                    new() { Name = CompMoneyFlowWave, DisplayName = DisplayMoneyFlow, DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            FormName = "wave",
                            // MCB 2.4.0 declares rgba(63,255,0,0.4) / rgba(255,17,0,0.4), a 2-pixel
                            // area (2026-10-31; was #80, 50%, fitted to 2.2.6 screenshots). Over the
                            // waves its render reads #329E65 / #7F3F65, as these composite.
                            DefaultColorHex = "#663FFF00", DefaultColorHexSecondary = "#66FF1100",
                            DefaultIsAreaFill = true, DefaultAreaBySign = true,
                            DefaultAreaEdge = true,
                            DefaultThickness = 2f,
                            DefaultColorSource = ColorSource.Value,
                            ColorBaseline = 0.0,
                            DefaultReferenceLevel = 0.0,
                            DefaultUsePolarityColoring = true,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultEnvelopeType = "Sustain",
                            DefaultWaveform = "sawtooth", DefaultAboveWaveform = "sawtooth", DefaultBelowWaveform = "square",
                            DefaultTriggerBoundaryClick = true, DefaultNoiseAmount = 0f,
                            SpeechTemplate = "Money Flow. {value:F1}.",
                            SubscribedLevelNames = Array.Empty<string>(),
                            OtherForm = new() { Name = CompMoneyFlowWave, DisplayName = DisplayMoneyFlow, DisplayType = ComponentDisplayType.Histogram, Role = ComponentRole.Signal,
                                FormName = "histogram",
                                DefaultPlaybackLayer = PlaybackLayer.Background,
                                DefaultPitchMapping = PitchMapping.Direction,
                                DefaultBullishFrequency = 300.0,
                                DefaultBearishFrequency = 100.0,
                                SpeechTemplate = "Money Flow. {value:F1}.",
                                SubscribedLevelNames = Array.Empty<string>() } },

                    // ── WaveTrend crossover dots ────────────────────────────
                    // Skia reads an 8-digit hex as #AARRGGBB. These were written #RRGGBBAA
                    // ("#4FC3F7A0", "#EF9A9AA0"), so the bull dot drew #C3F7A0 at 31% — a faint
                    // pale green — and the bear dot drew #9A9AA0, grey. MCB's cross dots are
                    // TradingView's red #FF5252 and lime #00E676 at about 85% (the official
                    // product image composites to #D9524F / #06D27D over #131722), so that is
                    // what these are now, written the way Skia reads them.
                    // 2026-10-31: MCB 2.4.0's declared colours, which replace that 2.2.6 fit: up
                    // rgba(63,255,0,0.8), down rgba(255,0,0,0.65), 3-pixel circles on the slow wave
                    // (the down dot over the blue wave renders #B4213B, as this composites).
                    new() { Name = CompCrossBull,     DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                            DefaultColorHex = "#CC3FFF00", DefaultThickness = 3.0f, DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "sine_bell",
                            DefaultDecayMs = 100,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultBaseFrequency = 660.0,
                            DefaultSignalSpeechTemplate = "Wave cross up {value}",
                            DefaultUsePolarityColoring = false,
                            SubscribedLevelNames = WaveLevels },
                    new() { Name = CompCrossBear,     DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                            DefaultColorHex = "#A6FF0000", DefaultThickness = 3.0f, DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "sine_bell",
                            DefaultDecayMs = 100,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultBaseFrequency = 220.0,
                            DefaultSignalSpeechTemplate = "Wave cross down {value}",
                            DefaultUsePolarityColoring = false,
                            SubscribedLevelNames = WaveLevels },

                    // The adaptive OB/OS thresholds are not components since 2026-10-17 (Cody's report: hidden
                    // stops that said ±53), and their mode went on 2026-10-31.

                    // ── Signal dots ─────────────────────────────────────────
                    // MCB's big green buy dot: opaque #3FFF00 (sampled exactly in the 2.2.6 chart),
                    // drawn in the money-flow bar. Was blue (#0BBCF5) on the wave until 2026-10-17.
                    // Spoken "Buy dot, waves crossed up from oversold" (was "Oversold crossover, long
                    // signal"): it fires on a cross up with the fast wave below Over Sold Level 1 (−60).
                    // 2026-10-31: MCB 2.4.0 declares rgba(63,255,0,0.8) at the bottom of the pane.
                    new() { Name = CompBlue,          DisplayName = DisplayBuyDot, DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                            DefaultColorHex = "#CC3FFF00", DefaultThickness = 5.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "sine_bell",
                            DefaultDecayMs = 350,
                            DefaultBaseFrequency = 840.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Buy dot, waves crossed up from oversold",
                            DefaultUsePolarityColoring = false,
                            SubscribedLevelNames = WaveLevels },
                    // ── MCB's two stochastic lines (added 2026-10-17, MUTED by default) ──────
                    // Drawn because Market Cipher B draws them; muted (DefaultIsMuted) so they add no
                    // voice to the chart Cody already hears — they are navigable and speak their value
                    // on request like any component. Appended LAST so no existing navigation stop
                    // moves. Formulas and evidence at WaveTrendMath.SmoothedStochastic's call site.
                    new() { Name = CompMcbRsi,        DisplayName = "RSI", DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#E600E6", DefaultThickness = 1.0f,   // MCB's label colour, exact
                            DefaultIsMuted = true,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultReferenceLevel = 50.0, DefaultEnvelopeType = "Sustain",
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultTriggerBoundaryClick = true,
                            SpeechTemplate = "RSI. {value:F1}. {zone}.",   // "overbought" past its own 80
                            SubscribedLevelNames = LineLevels,   // their own undrawn 80/20, not the waves' ±53/±60
                            DefaultIsAreaFill = false, DefaultUsePolarityColoring = false },
                    // Green at or below the RSI line, red above it: MCB colours it that way on 397 of
                    // 397 digitised bars. #3FFF00 / #FF1100 are the line's own sampled colours.
                    new() { Name = CompMcbStoch,      DisplayName = "Stochastic", DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#3FFF00", DefaultColorHexSecondary = "#FF1100", DefaultThickness = 2.0f,
                            DefaultColorAboveComponent = CompMcbRsi,
                            DefaultIsMuted = true,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultReferenceLevel = 50.0, DefaultEnvelopeType = "Sustain",
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultTriggerBoundaryClick = true,
                            SpeechTemplate = "Stochastic. {value:F1}. {zone}.",
                            SubscribedLevelNames = LineLevels,   // their own undrawn 80/20, not the waves' ±53/±60
                            DefaultIsAreaFill = false, DefaultUsePolarityColoring = false },
                },
                // No cloud fills: the Anchor Fill and WT Fill bands, hidden by default since 2026-10-17,
                // were removed on 2026-10-31 (Market Cipher B draws neither). A saved chart drops them
                // on load (WorkspaceInitializer).
                DefaultZoneBands = new List<ZoneBandConfig>
                {
                    // Market Cipher B's money-flow bar: a strip along the bottom of the pane, green
                    // while money flow is positive and maroon while negative, with the big green buy
                    // dots in it. Its composites in the 2.2.6 chart (#205D18 / #5A1518 over #131722)
                    // solve to the money-flow colours at 30% (#4D) on all three channels. Visual
                    // only: no component, so no navigation stop and no voice.
                    //
                    // The red "OB Zone" (53..100) and green "OS Zone" (−100..−53) shading that were
                    // here until 2026-10-17 are gone: MCB shades nothing — its levels are plain lines
                    // (GetDefaultLevels). A saved chart drops them on load (WorkspaceInitializer).
                    //
                    // 2026-10-31, from MCB 2.4.0: its only fill, "Plots Background", between two hidden
                    // plots at Trigger 2 − 40 and Trigger 2 − 50 (−93 and −103 by default; setting
                    // Trigger 2 to −30 on TradingView moved them to −70 and −80), coloured
                    // rgba(63,255,0,0.4) / rgba(255,17,0,0.4) by the money flow's sign (renders
                    // #257415 / #721515). Was 30% at −92…−100, fitted to 2.2.6 screenshots. Placed by
                    // Trigger 2 through ParameterBinding.
                    new() { DisplayName = "Money Flow Bar", ColorHex = "#663FFF00", NegativeColorHex = "#66FF1100",
                            SignComponentName = CompMoneyFlowWave,
                            FixedTop = -93.0, FixedBottom = -103.0, IsVisible = true,
                            AnchorParameter = ParamTrigger2, AnchorValue = -53.0 },
                },
                // Market Cipher B 2.4.0's inputs and nothing else (2026-10-31, Cody: "all options found
                // in the trading view version also in our version, true to spec"), each tried on
                // TradingView to see what it moves. Its "Version" text input is a label (2.4.0) that
                // changes nothing, so it is not offered. The wave, money-flow and line periods are
                // MCB's fixed numbers (9, 12, 3, 60, 40, 81): MCB offers no setting for them, and the
                // four this indicator offered went (a saved chart drops them on load).
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamUseChartTimeframe, DisplayName = "Use Current Chart Resolution", DefaultValue = true, DataType = typeof(bool),
                            Description = "On: computed on the chart's own bars. Off: computed on the timeframe below, each bar showing the last value finished by then. On by default." },
                    new() { Name = ParamOtherTimeframe, DisplayName = "Different Timeframe", DefaultValue = "60", DataType = typeof(string),
                            Description = "The timeframe used when Use Current Chart Resolution is off: minutes (60, 240) or D, W, M. One no longer than the chart's uses the chart's own bars. 60 by default." },
                    new() { Name = ParamOBLevel1, DisplayName = "Over Bought Level 1", DefaultValue = 60.0, DataType = typeof(int), MinValue = -100, MaxValue = 100, Step = 1,
                            Description = "The upper solid line. 60 by default." },
                    new() { Name = ParamTrigger1, DisplayName = "Trigger 1", DefaultValue = 53.0, DataType = typeof(int), MinValue = -100, MaxValue = 100, Step = 1,
                            Description = "The upper dotted line. 53 by default." },
                    new() { Name = ParamOSLevel1, DisplayName = "Over Sold Level 1", DefaultValue = -60.0, DataType = typeof(int), MinValue = -100, MaxValue = 100, Step = 1,
                            Description = "The lower solid line. A buy dot is a cross up with the fast wave below it. -60 by default." },
                    new() { Name = ParamTrigger2, DisplayName = "Trigger 2", DefaultValue = -53.0, DataType = typeof(int), MinValue = -100, MaxValue = 100, Step = 1,
                            Description = "The lower dotted line. The money flow bar runs from 40 to 50 below it. -53 by default." },
                }
            }
        };

        // ── Calculation ───────────────────────────────────────────────────────

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            if (!code.Equals("CIPHER_B", StringComparison.OrdinalIgnoreCase)) return;
            int n = data.Length;
            if (n < 10) return;

            double osLevel1 = GetDbl(parameters, ParamOSLevel1, -60.0);
            bool heikinAshi = IndicatorCandles.IsHeikinAshi(parameters);
            string? otherTf = HigherTimeframe(data, parameters);

            Core core;
            if (otherTf == null)
                core = ComputeCore(data.ToArray(), heikinAshi);
            else
            {
                // "Use Current Chart Resolution" off: MCB computes everything on the other
                // timeframe's bars and shows each on the chart bar that finishes it; the bars before
                // show the period before, and the newest bar the period still forming. Read on
                // TradingView (2026-10-31, TVC:DJI 1D with the input off and "W"): every plotted
                // value steps on the last daily bar of each week, the crosses and buy dots fall on
                // those bars, and a timeframe no longer than the chart's gives the chart's own values.
                // The other timeframe's bars are built from the chart's (ResamplerService, weeks from
                // Monday 00:00 UTC, months from the 1st), then turned into Heikin-Ashi on a
                // Heikin-Ashi chart.
                var raw = data.ToArray();
                var other = new ResamplerService().Resample(raw.ToList(), otherTf);
                var oc = ComputeCore(other.ToArray(), heikinAshi);
                core = Core.Empty(n);
                int k = -1;
                long current = long.MinValue;
                var otherStarts = new Dictionary<long, int>(other.Count);
                for (int j = 0; j < other.Count; j++) otherStarts[Ticks(other[j].Date)] = j;
                for (int i = 0; i < n; i++)
                {
                    long bucket = Ticks(TimeframeUtility.GetPeriodStart(raw[i].Date, otherTf));
                    if (bucket != current) { current = bucket; k = otherStarts[bucket]; }
                    bool finishes = i == n - 1
                        || Ticks(TimeframeUtility.GetPeriodStart(raw[i + 1].Date, otherTf)) != bucket;
                    int src = finishes ? k : k - 1;
                    if (src < 0) continue;
                    core.Wt1[i] = oc.Wt1[src]; core.Wt2[i] = oc.Wt2[src]; core.Mf[i] = oc.Mf[src];
                    core.Rsi[i] = oc.Rsi[src]; core.Stoch[i] = oc.Stoch[src];
                }
            }
            var (wt1, wt2) = (core.Wt1, core.Wt2);

            // WT Histogram = WT1 − WT2 (MCB's "VWAP")
            var wtHist = new double[n];
            for (int i = 0; i < n; i++)
                wtHist[i] = (double.IsNaN(wt1[i]) || double.IsNaN(wt2[i])) ? double.NaN : wt1[i] - wt2[i];

            // ── Signal dots ──────────────────────────────────────────────────
            var crossBullSignal = new double[n];
            var crossBearSignal = new double[n];
            var mcbBuy          = new double[n];
            Array.Fill(crossBullSignal, double.NaN);
            Array.Fill(crossBearSignal, double.NaN);
            Array.Fill(mcbBuy, double.NaN);

            for (int i = 1; i < n; i++)
            {
                if (double.IsNaN(wt1[i]) || double.IsNaN(wt2[i]) ||
                    double.IsNaN(wt1[i - 1]) || double.IsNaN(wt2[i - 1])) continue;

                // Pine's crossover/crossunder, as MCB's cross dots fire: 524 of 524 each on BTCUSD 1D.
                bool crossUp   = WaveTrendMath.CrossedOver(wt1, wt2, i);
                bool crossDown = WaveTrendMath.CrossedUnder(wt1, wt2, i);

                // ON the slow wave, as MCB draws its cross dots (they sat 3 points off it).
                if (crossUp)   crossBullSignal[i] = wt2[i];
                if (crossDown) crossBearSignal[i] = wt2[i];

                // MCB's big green buy dot: a cross up with the FAST wave below Over Sold Level 1
                // (−60), nothing else — no confirmation bars, no cooldown, no anchor gate. Against
                // MCB 2.4.0's own "Green Dot" column on BTCUSD 1D: 64 of 64 dots on candles and 68 of
                // 68 on Heikin-Ashi, no extra; with the input at −40 on TVC:DJI 1D, every dot MCB drew
                // (2026-10-31). Testing the slow wave instead (this indicator before 2026-10-23, from
                // screenshots) adds 10 and 7 dots that MCB never drew. MCB pins the dot to the
                // pane's bottom edge in pixels; it is drawn here at a fixed value inside the
                // money-flow bar, which is where that edge falls with the default Trigger 2.
                if (crossUp && wt1[i] < osLevel1) mcbBuy[i] = McbBuyDotY;
            }

            // ── Write to buffer ───────────────────────────────────────────────
            WriteToBuffer(buffer, CompWT1,            wt1,             n);
            WriteToBuffer(buffer, CompWT2,            wt2,             n);
            WriteToBuffer(buffer, CompWtHistogram,    wtHist,          n);
            WriteToBuffer(buffer, CompMoneyFlowWave,  core.Mf,         n);
            WriteToBuffer(buffer, CompCrossBull,      crossBullSignal, n);
            WriteToBuffer(buffer, CompCrossBear,      crossBearSignal, n);
            WriteToBuffer(buffer, CompBlue,           mcbBuy,          n);
            WriteToBuffer(buffer, CompMcbRsi,         core.Rsi,        n);
            WriteToBuffer(buffer, CompMcbStoch,       core.Stoch,      n);
        }

        /// <summary>The per-bar values Market Cipher B computes from candles; the dots and the Wave
        /// Gap follow from the two waves.</summary>
        private sealed record Core(double[] Wt1, double[] Wt2, double[] Mf, double[] Rsi, double[] Stoch)
        {
            public static Core Empty(int n)
            {
                double[] Nan() { var a = new double[n]; Array.Fill(a, double.NaN); return a; }
                return new Core(Nan(), Nan(), Nan(), Nan(), Nan());
            }
        }

        private static Core ComputeCore(Ohlcv[] bars, bool heikinAshi)
        {
            int n = bars.Length;
            // On a Heikin-Ashi chart Market Cipher B reads Heikin-Ashi candles, as every
            // TradingView indicator does. Read from MCB 2.4.0's own plotted values on BTCUSD 1D
            // Bitstamp, 5,513 bars, once on a Heikin-Ashi chart and once on candles (2026-10-23):
            // the waves, VWAP and both 0–100 lines equal these formulas on the chart's candles to
            // the five decimals TradingView prints. (The 2026-10-22 screenshot digitising had the
            // two lines on the real bars; the exact values say otherwise.) The money flow too
            // (2026-10-24, exact on both charts). This provider is handed the raw bars
            // (IComputesHeikinAshiItself) and turns them into Heikin-Ashi itself when the chart
            // shows Heikin-Ashi.
            if (heikinAshi && n > 0) bars = ChartMath.CalculateHeikinAshi(bars.ToList()).ToArray();

            var close = new double[n];
            var high  = new double[n];
            var low   = new double[n];
            var hlc3  = new double[n];
            for (int i = 0; i < n; i++)
            {
                close[i] = bars[i].Close;
                high[i]  = bars[i].High;
                low[i]   = bars[i].Low;
                hlc3[i]  = (high[i] + low[i] + close[i]) / 3.0;
            }

            // ── Wave Trend ────────────────────────────────────────────────────
            // The slow wave is a 3-bar SMA of the fast one (4 before 2026-10-17). Against four
            // Market Cipher B screenshots (BTC 1D Bitstamp HA x2, BTCUSDT 1D Binance, BTCUSDT 30m
            // Binance), SMA3 put the WT crosses on the very bar of MCB's cross dots 164/182 times
            // against 121/182 for SMA4 (within one bar: 175/182 vs 158/182), fitted MCB's
            // slow-wave body better on all four (RMSE 6.5/8.2/6.2/3.8 vs 7.2/8.6/7.6/5.9) and made
            // MCB's VWAP equal WT1 − WT2 at slope 1.00–1.06 instead of 0.69–0.74; MCB 2.4.0's
            // plotted values later matched exactly.
            var (wt1, wt2) = WaveTrendMath.Compute(hlc3, WaveChannelBars, WaveAverageBars, SlowWaveBars);

            // ── Money Flow ───────────────────────────────────────────────────
            // 200/3 × SMA(d / SMA(|d|, 5), 60), d = hlc3 − SMA(hlc3, 5), on the chart's
            // candles (Heikin-Ashi on a Heikin-Ashi chart), linear, clamped to ±100. EXACT against
            // Market Cipher B 2.4.0's plotted money flow (2026-10-24): TVC:DJI 1D since 1896
            // (32,585 bars), BTCUSD 1D on candles and on Heikin-Ashi (5,446 each), BTCUSD 15m
            // (5,009), every finished bar to the five decimals TradingView prints, the first
            // value on bar 68 as MCB's (4 + 4 + 59 bars of warm-up). Found by recovering MCB's
            // per-bar input from its line (the 60-bar SMA makes each step 1/60 of the newest
            // input less the one 60 back; one unknown offset per bar-of-60, fitted) and fitting
            // that input as a weighted sum of the last ten candles' prices: high, low and close
            // weigh alike, the open almost nothing, and the four bars before weigh a quarter
            // each against the newest, nothing older — hlc3 less its 5-bar mean.
            // Earlier fits, superseded: 68 × SMA60(Heikin-Ashi body / SMA5 |body|) (2026-10-23,
            // error ~1.8, starting 4 bars early) and 146 × SMA60(Heikin-Ashi body / SMA5 range).
            // The constant 200/3 came out of the fit (66.6667 on every series), not assumed.
            // No constant offset. Not timeframe-scaled: MCB keeps 60 on 15m and 30m charts.
            var mf = McbMoneyFlow(high, low, close, MoneyFlowBars);

            // ── MCB "RSI" and "Stochastic" lines (drawn, muted by default) ──────────────────
            // Market Cipher B draws two 0–100 lines in the wave pane: a thin magenta one it titles
            // "RSI" and a thicker green/red one titled "Sto RSI". Neither is an RSI. Searched
            // against MCB 2.4.0's plotted values (BTCUSD 1D, 5,513 bars, candle and Heikin-Ashi
            // charts) over stochastics, RSIs and smoothings of 2–100 bars on four sources: "RSI" is
            // a 40-bar stochastic of the CLOSE smoothed by SMA2 and "Sto RSI" an 81-bar one, also
            // SMA2, both exact (RMSE 0.000003); the next best were 1.3 and 0.7 points off. Before
            // 2026-10-23 these were fitted to a digitised screenshot: hlc3, 40/80 bars, SMA3.
            var rsi   = WaveTrendMath.SmoothedStochastic(close, high, low, McbRsiLength, McbLineSmoothing);
            var stoch = WaveTrendMath.SmoothedStochastic(close, high, low, McbStochLength, McbLineSmoothing);
            return new Core(wt1, wt2, mf, rsi, stoch);
        }

        /// <summary>The timeframe to compute on when "Use Current Chart Resolution" is off and the
        /// other timeframe is longer than the chart's, as an app token (1h, 4h, 1d, 1w, 1M); null to
        /// compute on the chart's own bars.</summary>
        internal static string? HigherTimeframe(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters)
        {
            if (IndicatorParams.GetBool(parameters, ParamUseChartTimeframe, true)) return null;
            string? tf = ToTimeframeToken(GetStr(parameters, ParamOtherTimeframe, "60"));
            if (tf == null) return null;
            long chartMs = parameters.TryGetValue("__timeframe", out var ct) && ct is string cts
                ? TimeframeUtility.ToMilliseconds(cts) : 0;
            if (chartMs <= 0) chartMs = MedianSpacingMs(data);
            return chartMs > 0 && TimeframeUtility.ToMilliseconds(tf) > chartMs ? tf : null;
        }

        /// <summary>TradingView's timeframe text as an app token: a plain number is minutes
        /// ("60" → 1h, "240" → 4h, "1440" → 1d), "D" / "W" / "M" with an optional count are days,
        /// weeks and months; an app token (15m, 4h, 1d, 1w, 1M) passes through. Null for anything
        /// else, seconds included.</summary>
        internal static string? ToTimeframeToken(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string t = text.Trim();
            if (t.All(char.IsDigit))
            {
                if (!int.TryParse(t, out int minutes) || minutes <= 0) return null;
                return minutes % 1440 == 0 ? $"{minutes / 1440}d"
                     : minutes % 60 == 0   ? $"{minutes / 60}h"
                     :                       $"{minutes}m";
            }
            var m = System.Text.RegularExpressions.Regex.Match(t, @"^(\d*)([DdWwM])$");
            if (m.Success)
            {
                int count = m.Groups[1].Value.Length == 0 ? 1 : int.Parse(m.Groups[1].Value);
                if (count <= 0) return null;
                string unit = m.Groups[2].Value switch { "D" or "d" => "d", "W" or "w" => "w", _ => "M" };
                return $"{count}{unit}";
            }
            return TimeframeUtility.IsValid(t) ? t : null;
        }

        private static long MedianSpacingMs(ReadOnlySpan<Ohlcv> data)
        {
            if (data.Length < 2) return 0;
            var gaps = new long[data.Length - 1];
            for (int i = 1; i < data.Length; i++) gaps[i - 1] = (long)(data[i].Date - data[i - 1].Date).TotalMilliseconds;
            Array.Sort(gaps);
            return gaps[gaps.Length / 2];
        }

        private static long Ticks(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc).Ticks;

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        /// <summary>Bars of warm-up before values stop changing: the waves' EMAs (four × their
        /// combined periods) plus the money flow's average and the longer line's window, 225. A
        /// generous upper bound, not a derivation. (It was the anchor pair's, five times longer,
        /// until 2026-10-31.) On another timeframe that many of ITS bars, in chart bars.</summary>
        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            int bars = (WaveChannelBars + WaveAverageBars) * 4 + MoneyFlowBars + McbStochLength;
            if (IndicatorParams.GetBool(parameters, ParamUseChartTimeframe, true)) return bars;
            string? tf = ToTimeframeToken(GetStr(parameters, ParamOtherTimeframe, "60"));
            long chartMs = parameters.TryGetValue("__timeframe", out var ct) && ct is string cts
                ? TimeframeUtility.ToMilliseconds(cts) : 0;
            long otherMs = tf == null ? 0 : TimeframeUtility.ToMilliseconds(tf);
            if (chartMs <= 0 || otherMs <= chartMs) return bars;
            return (int)Math.Min(int.MaxValue / 2, (long)bars * ((otherMs + chartMs - 1) / chartMs));
        }

        /// <summary>
        /// What a component says at a bar when the keyboard lands on it or scans across it — the
        /// provider's own wording, which <c>SpeechFormatter.ProviderSpeechStrategy</c> asks for
        /// before any template (so on the keyboard the SpeechTemplate / SignalSpeechTemplate declared
        /// above are not what these components say; playback and live narration still speak the
        /// SignalSpeechTemplate). Null for the RSI and Stochastic lines, which take their
        /// templates (they need the {zone} word only the level path knows). "no data" on a bar
        /// with no value, for every component.
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
                CompWtHistogram    => $"{value:F1}",
                CompMoneyFlowWave  => GetMFWaveSpeech(value),
                CompCrossBull      => GetSignalDotSpeech(bar, allComponentData, dataIndex),
                CompCrossBear      => GetSignalDotSpeech(bar, allComponentData, dataIndex),
                CompBlue           => GetSignalDotSpeech(bar, allComponentData, dataIndex),
                _ => null
            };
        }

        private static string GetMFWaveSpeech(double value)
        {
            // The number drawn, signed, as the waves say theirs: "minus 18.0" is "-18.0" to a
            // screen reader. It said "selling pressure, 18.0" until 2026-10-30 (Cody: "why does
            // money flow say x buying pressure or y selling pressure yes wt gives just a raw
            // number"): the side is the sign, and "pressure" claimed a meaning the number does
            // not have. Same precision as the status line and Alt+Shift+D.
            if (double.IsNaN(value)) return "no data";
            return value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>The wave's value, with "overbought" / "oversold" past ±53, the pane's
        /// Overbought / Oversold levels.</summary>
        private static string GetWTValueSpeech(double value)
        {
            if (value > 53)  return $"{value:F1}, overbought";
            if (value < -53) return $"{value:F1}, oversold";
            return $"{value:F1}";
        }

        private static string GetSignalDotSpeech(Ohlcv bar, IReadOnlyDictionary<string, double[]> data, int idx)
        {
            double wt1 = SpeechValue(data, CompWT1, DisplayWT1, idx);
            string wtStr = double.IsNaN(wt1) ? "" : $" Wave Trend {wt1:F1}.";
            return $"Price {SpeechPriceFormatter.FormatPrice(bar.Close)}.{wtStr}";
        }

        /// <summary>
        /// A one-sentence summary of the bar. NOT spoken anywhere in the app today: Alt+Shift+D
        /// (BarDetailService) bypasses every provider's detail fact and reads the visible
        /// components' values instead. It has no view of visibility either (it is handed the result
        /// arrays, not the components), so it would name hidden signals; keep that in mind before
        /// wiring it to speech.
        /// </summary>
        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> results, int index, Dictionary<string, object> parameters)
        {
            if (!code.Equals("CIPHER_B", StringComparison.OrdinalIgnoreCase) || index < 0) return string.Empty;

            double wt1Val  = GetVal(results, CompWT1,          index);
            double mfVal   = GetVal(results, CompMoneyFlowWave, index);
            double histVal = GetVal(results, CompWtHistogram,  index);
            double blueVal = GetVal(results, CompBlue,         index);

            double trigger1 = GetDbl(parameters, ParamTrigger1, OverboughtLevel);
            double trigger2 = GetDbl(parameters, ParamTrigger2, -OverboughtLevel);
            var sb = new StringBuilder();

            if (!double.IsNaN(wt1Val))
            {
                string zone = wt1Val > trigger1 ? ", overbought"
                            : wt1Val < trigger2 ? ", oversold"
                            :                     "";
                string dir = wt1Val >= 0 ? "above zero" : "below zero";
                sb.Append($"Wave Trend {dir}{zone} at {wt1Val:F1}. ");
            }
            if (!double.IsNaN(histVal))
            {
                string hDir = histVal >= 0 ? "bullish" : "bearish";
                sb.Append($"Wave Gap {hDir} at {histVal:F1}. ");
            }
            if (!double.IsNaN(mfVal))
            {
                string mfDir = mfVal > 0.0 ? "positive" : "negative";
                sb.Append($"Money flow {mfDir}. ");
            }

            if (!double.IsNaN(blueVal)) sb.Append(DisplayBuyDot + ".");

            return sb.Length > 0 ? sb.ToString().TrimEnd() : string.Empty;
        }

        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>The 0–100 lines' stochastic windows, read from MCB 2.4.0's values (see their call
        /// site): "RSI" 40 bars, "Stochastic" 81, each smoothed by <see cref="McbLineSmoothing"/>.</summary>
        internal const int McbRsiLength = 40, McbStochLength = 81, McbLineSmoothing = 2;

        /// <summary>Where MCB's big green buy dot sits: in the money-flow bar (−93..−103 by default).</summary>
        internal const double McbBuyDotY = -96.0;

        /// <summary>The default Trigger 1 / Trigger 2, ±53. The wave speech says "overbought" /
        /// "oversold" past it whatever the settings (the speech is not handed them; see docs/TODO.md);
        /// the bar summary reads the settings.</summary>
        private const double OverboughtLevel = 53.0;

        /// <summary>The money flow as Cipher B draws it: 200/3 × <see cref="McbMoneyFlowShare"/>,
        /// clamped to ±100. Cipher A's Yellow Diamond reads it (2026-10-23).</summary>
        public static double[] McbMoneyFlow(double[] high, double[] low, double[] close, int period)
        {
            var share = McbMoneyFlowShare(high, low, close, period);
            for (int i = 0; i < share.Length; i++)
                if (!double.IsNaN(share[i])) share[i] = Math.Clamp(share[i] * McbMoneyFlowScale, -MoneyFlowClamp, MoneyFlowClamp);
            return share;
        }

        /// <summary>SMA over <paramref name="period"/> bars of each candle's typical price (hlc3) less its
        /// <see cref="McbMoneyFlowTypicalBars"/>-bar mean, divided by the mean size of that difference over
        /// the same number of bars. Read on the candles given, as the chart shows them. Unscaled; see the
        /// MCB money flow notes in Calculate.</summary>
        internal static double[] McbMoneyFlowShare(double[] high, double[] low, double[] close, int period)
        {
            int n = close.Length;
            var typical = new double[n];
            for (int i = 0; i < n; i++) typical[i] = (high[i] + low[i] + close[i]) / 3.0;
            var typicalMean = IndicatorMath.Sma(typical, McbMoneyFlowTypicalBars);
            var deviation = new double[n];
            var size = new double[n];
            for (int i = 0; i < n; i++)
            {
                deviation[i] = typical[i] - typicalMean[i];
                size[i] = Math.Abs(deviation[i]);
            }
            var meanSize = IndicatorMath.Sma(size, McbMoneyFlowTypicalBars);
            var share = new double[n];
            for (int i = 0; i < n; i++)
                // Five unchanged typical prices give 0 / 0. MCB's line was never seen there (no such
                // run in any series read); 0 keeps the window going.
                share[i] = double.IsNaN(meanSize[i]) ? double.NaN
                         : meanSize[i] < 1e-10 ? 0.0
                         : deviation[i] / meanSize[i];
            return IndicatorMath.Sma(share, period);
        }

        /// <summary>The money flow's display scale, a linear 200/3 (exact; see Calculate), clamped to
        /// ±<see cref="MoneyFlowClamp"/> so a rare run of extreme values stays inside the pane.</summary>
        private const double McbMoneyFlowScale = 200.0 / 3.0, MoneyFlowClamp = 100.0;
        private const int McbMoneyFlowTypicalBars = 5;

        // ── Buffer / parameter helpers ────────────────────────────────────────

        private static void WriteToBuffer(IIndicatorResultBuffer buffer, string name, double[] data, int n)
        {
            var span = buffer.GetComponentSpan(name);
            int len = Math.Min(span.Length, data.Length);
            for (int i = 0; i < len; i++) span[i] = data[i];
        }

        /// <summary>
        /// A sibling's value for <see cref="GetComponentSpeech"/>. The app's formatter
        /// (<c>SpeechFormatter.ProviderSpeechStrategy</c>) keys the data it hands over by DISPLAY
        /// name, so a lookup by component name alone found nothing and every "Wave Trend …" clause
        /// was silently dropped (2026-10-17 review; CipherBProviderSpeechTests). The name is tried
        /// first for callers that key by name (StrategyLab, the provider's own tests).
        /// </summary>
        private static double SpeechValue(IReadOnlyDictionary<string, double[]> data, string name, string displayName, int idx)
        {
            double v = GetVal(data, name, idx);
            return double.IsNaN(v) ? GetVal(data, displayName, idx) : v;
        }

        private static double GetVal(IReadOnlyDictionary<string, double[]> r, string key, int idx)
        {
            if (!r.TryGetValue(key, out var arr) || arr == null || idx >= arr.Length) return double.NaN;
            return arr[idx];
        }

        /// <summary>The undrawn 80/20 levels' colour (seen only in Properties).</summary>
        internal const string LevelGrey = "#8C8C96";
        /// <summary>MCB 2.4.0's level colours: rgba(255,255,255,0.4) for the solid lines,
        /// rgba(255,255,255,0.5) for the dotted ones.</summary>
        internal const string SolidWhite = "#66FFFFFF", DottedWhite = "#80FFFFFF";
        /// <summary>MCB's dotted line at +100, named as MCB titles it.</summary>
        internal const string Level100 = "100%";

        public List<LevelDescriptor> GetDefaultLevels(string code)
        {
            if (!code.Equals("CIPHER_B", StringComparison.OrdinalIgnoreCase)) return new();
            return new()
            {
                // Market Cipher B 2.4.0's lines, as it declares them (2026-10-31, Cody: "true to
                // spec"): ±60 solid white at 40%, 2 pixels; ±53 white at 50% in circles (drawn
                // dotted); zero solid white at 40%, 1 pixel; and a dotted 50% white "100%" line at
                // +100. Each of ±60 and ±53 IS its setting (ValueParameter): Over Bought Level 1,
                // Trigger 1, Over Sold Level 1, Trigger 2. They were #8C8C96, our contrast choice
                // for light themes (3.2:1 or more against every theme but SteelGray), from
                // 2026-10-17 to 2026-10-31; white on a light theme is the open question in
                // docs/TODO.md. Until 2026-10-17 they were red/green.
                //
                // Zone texture 0.30 at ±53 and 0.45 at ±60 (was 0.10 / 0.20). The waves carry a base
                // texture of 0.15 of their own and the engine plays the STRONGER of base and zone, so
                // 0.10 was never heard: between ±53 and ±60 the waves sounded exactly as they did at
                // zero, and the zone only began at ±60. Found answering Cody's 2026-10-17 "shouldn't
                // the os/ob zones be -53 and 53?" — now three audible steps: clean, rough past ±53,
                // rougher past ±60 (0.45 is a plain Stochastic's zone texture).
                //
                // The names stay as they were (saved charts, alerts and strategies find levels by
                // name, and the names give the roles).
                new("Extreme OB",  60.0, SolidWhite,  DashStyle.Solid, PlayEarcon: true, EarconVolume: 0.8f, ZoneNoiseAmount: 0.45f, ZoneNoiseType: "white",
                    Thickness: 2f, ValueParameter: ParamOBLevel1),
                new("Overbought",  53.0, DottedWhite, DashStyle.Dot,   PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.30f, ZoneNoiseType: "white",
                    ValueParameter: ParamTrigger1),
                new("Zero",         0.0, SolidWhite,  DashStyle.Solid, PlayEarcon: true, EarconVolume: 0.7f),
                new("Oversold",   -53.0, DottedWhite, DashStyle.Dot,   PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.30f, ZoneNoiseType: "white",
                    ValueParameter: ParamTrigger2),
                new("Extreme OS", -60.0, SolidWhite,  DashStyle.Solid, PlayEarcon: true, EarconVolume: 0.8f, ZoneNoiseAmount: 0.45f, ZoneNoiseType: "white",
                    Thickness: 2f, ValueParameter: ParamOSLevel1),
                // MCB's "100%" line at the pane's top: drawn, silent, no component listens to it.
                new(Level100, 100.0, DottedWhite, DashStyle.Dot),

                // The RSI and Stochastic lines' OWN extremes. They run 0–100 on this −100…+100 pane
                // (as MCB draws them), where the waves' ±53 means "just above the middle" and −53 is
                // never reached. Cody, 2026-10-17: 80 / 20, sound only — and "yes, they should be
                // narrated". Undrawn (IsDrawn: false) so the pane keeps MCB's look; subscribed to by
                // those two lines alone (LineLevels), so texture, clicks, the spoken zone and the
                // crossing narration all come from the one level path every indicator uses. A plain
                // Stochastic's texture (0.45 pink).
                new("Stoch Overbought", 80.0, LevelGrey, DashStyle.Dot, PlayEarcon: true, EarconVolume: 0.6f,
                    ZoneNoiseAmount: 0.45f, ZoneNoiseType: "pink", IsDrawn: false),
                new("Stoch Oversold",   20.0, LevelGrey, DashStyle.Dot, PlayEarcon: true, EarconVolume: 0.6f,
                    ZoneNoiseAmount: 0.45f, ZoneNoiseType: "pink", IsDrawn: false),
            };
        }

        private static double GetDbl(Dictionary<string, object> p, string k, double def) =>
            p.TryGetValue(k, out var v) && v is not string ? Convert.ToDouble(v) : def;
        private static string GetStr(Dictionary<string, object> p, string k, string def) =>
            p.TryGetValue(k, out var v) ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? def : def;
    }
}
