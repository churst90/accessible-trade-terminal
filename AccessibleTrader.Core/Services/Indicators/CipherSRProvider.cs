using System.Text;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Accessible Cipher SR — Support and Resistance level detector inspired by Market Cipher SR.
    ///
    /// Four navigable components:
    ///   Resistance      — Purple dot at the confirmed pivot high bar, at the high price.
    ///   Resistance Zone — Semi-transparent purple dashed line carrying the resistance price
    ///                     forward until price closes convincingly above it (break detection).
    ///   Support         — Gold dot at the confirmed pivot low bar, at the low price.
    ///   Support Zone    — Semi-transparent gold dashed line carrying the support price forward.
    ///
    /// One cloud fill (visual-only, no component):
    ///   SR Zone — subtle fill between the Resistance Zone and Support Zone lines, showing
    ///             the contested price range between the two active levels. Narrow fill =
    ///             price compression; wide fill = extended range. Follows the Cipher B WT Fill
    ///             pattern: cloud fill reuses existing navigable component data, no extra
    ///             components added purely for visual purposes.
    ///
    /// Pivot detection:
    ///   A bar at index i is a pivot high when high[i] is strictly the maximum of
    ///   [i-PivotBars .. i+PivotBars]. The last PivotBars bars never emit dots (expected).
    ///   Optional volume confluence: pivot volume must be >= VolumeMultiplier × rolling average.
    ///   Set VolumeMultiplier=0 to disable volume filtering entirely.
    ///   The DOT is drawn on the pivot bar, where the level is; the ZONE line only steps to that
    ///   level at bar i+PivotBars, the first bar the pivot could be recognised. That split is why
    ///   the dots are declared Lookahead and the zone lines Causal.
    ///
    /// Auto-Scale (works at any timeframe / zoom level):
    ///   When AutoScale=1, the pivot window for a bar is derived from that BAR'S OWN position:
    ///   PivotBars(i) = Clamp((i + 1) / 25, 2, 15). This targets roughly one pivot per 25 bars and
    ///   produces consistent SR density whether viewing 20 bars on a 5-minute chart or 200 bars on
    ///   a daily chart. It used to be Clamp(totalBarCount / 25, 2, 15), which made every bar's
    ///   answer depend on how much history was loaded after it — the same bar showed a window of 2
    ///   on a fresh 60-bar chart and 15 in a 500-bar backtest.
    ///
    /// Replaced is not broken (2026-10-17):
    ///   The zone line carries a level on EVERY bar it is live, including the bar a newer pivot
    ///   takes over from it. On that bar the companion "{zone}_replaced" array holds the OLD level
    ///   (NaN on every other bar). The narrator reads it to say "New resistance at Y, replacing X";
    ///   the line renderer reads it to lift the pen, so no slanted stroke joins the two levels.
    ///   Until then the line was left EMPTY on that bar to stop the renderer joining them, and the
    ///   narrator — which reads "had a value, now empty" as a break — announced 84 of the 136
    ///   "Resistance/Support at X broken" on Cody's BTC daily chart about levels price never
    ///   closed through (CipherSrSpeechTests).
    ///
    /// Break/Invalidation:
    ///   Resistance is invalidated when Close > level × (1 + BreakThreshold).
    ///   Support is invalidated when Close &lt; level × (1 - BreakThreshold).
    ///   Invalidated zones emit NaN for all subsequent bars until a new pivot forms. An empty
    ///   line after a live one therefore means a break and nothing else.
    ///
    /// Sonification (independent of visual display type):
    ///   Resistance dot:   660 Hz Ping — audible "ceiling".
    ///   Resistance Zone:  Sustain, PitchMapping.Value — pitch tracks where resistance sits
    ///                     within the viewport; constant for held levels, steps on new pivot.
    ///   Support dot:      330 Hz Ping — audible "floor".
    ///   Support Zone:     Sustain, PitchMapping.Value — same logic, lower register.
    ///   During playback: zone lines produce steady tones that step to new pitches when a
    ///   new pivot confirms, giving an audible sense of levels stepping up or down.
    ///
    /// GetDetailFact:
    ///   Names the most recent confirmed resistance and support and which side of them the bar
    ///   closed. No key speaks it today (BarDetailService bypasses GetDetailFact).
    /// </summary>
    public class CipherSrProvider : IIndicatorProvider
    {
        public string Name => "Cipher SR";

        public const string CompResistance     = "Resistance";
        public const string CompResistanceLine = "Resistance Zone";
        public const string CompSupport        = "Support";
        public const string CompSupportLine    = "Support Zone";

        // Companion arrays — hidden from navigation, used by GetComponentSpeech for touch counts.
        private const string CompResistanceTouches = CompResistance + "_touches";
        private const string CompSupportTouches    = CompSupport    + "_touches";

        /// <summary>
        /// The suffix of the companion array that says a zone line's level was REPLACED on a bar:
        /// it holds the old level there and NaN everywhere else. Read by the narrator
        /// (NarrationScanner.ScanZoneLines) and the line renderer (StandardRenderers.RenderLine).
        /// </summary>
        public const string ReplacedSuffix = "_replaced";
        public const string CompResistanceReplaced = CompResistanceLine + ReplacedSuffix;
        public const string CompSupportReplaced    = CompSupportLine    + ReplacedSuffix;

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "CIPHER_SR",
                Causality = ComponentCausality.Causal,
                Name        = "Cipher SR",
                Category    = "Overlays",
                DefaultPane = "Main",
                RequiresFullRecalcOnTick = true,
                Description =
                    "Support and resistance levels from volume-confirmed pivot highs/lows. " +
                    "Purple = resistance, gold = support. " +
                    "Zone lines extend until price closes convincingly through them. " +
                    "Auto-scales to any timeframe or zoom level.",
                Components = new List<IndicatorComponentMetadata>
                {
                    // ── Resistance dot ─────────────────────────────────────────────────
                    // Sparse: non-NaN only at pivot high bars, drawn ON the pivot bar because that
                    // is where the level is. A pivot is not knowable until PivotBars later, so the
                    // dot is a chart and navigation convention, not a signal — the Zone line below
                    // is the causal form and the one a strategy can be built on.
                    // crystal_bell Ping at 700 Hz = clear high-pitched ceiling earcon.
                    new() { Causality = ComponentCausality.Lookahead,
                            Name = CompResistance,
                            DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Level,
                            DefaultColorHex = "#AB47BC", DefaultThickness = 7.0f,
                            DefaultSoundPatchId = "crystal_bell",
                            DefaultEnvelopeType = "Ping", DefaultDecayMs = 220,
                            DefaultPitchMapping = PitchMapping.None, DefaultBaseFrequency = 700.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSignalSpeechTemplate = "Resistance pivot at {price}",
                            IsVisible = true },

                    // ── Resistance Zone line ────────────────────────────────────────────
                    // Carry-forward: holds the last confirmed resistance price until broken.
                    // Background sine drone — PitchMapping.Value so pitch tracks where the
                    // resistance level sits in the viewport; when the level steps to a new
                    // price, the pitch steps with it (audible level-change cue during playback).
                    // IsZoneLine = true → NavigationFeedbackManager plays proximity cue at cursor.
                    new() { Name = CompResistanceLine,
                            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Level,
                            DefaultColorHex = "#CE93D8", DefaultThickness = 1.5f,
                            DefaultDashStyle = DashStyle.Dash,
                            DefaultWaveform = "sine",
                            DefaultEnvelopeType = "Sustain",
                            DefaultPitchMapping = PitchMapping.Value, DefaultBaseFrequency = 650.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultIsZoneLine = true,
                            IsVisible = true },

                    // ── Support dot ────────────────────────────────────────────────────
                    // crystal_bell Ping at 330 Hz = low-pitched floor earcon.
                    // Lookahead for the same reason as the resistance dot.
                    new() { Causality = ComponentCausality.Lookahead,
                            Name = CompSupport,
                            DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Level,
                            DefaultColorHex = "#FFB300", DefaultThickness = 7.0f,
                            DefaultSoundPatchId = "crystal_bell",
                            DefaultEnvelopeType = "Ping", DefaultDecayMs = 220,
                            DefaultPitchMapping = PitchMapping.None, DefaultBaseFrequency = 330.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSignalSpeechTemplate = "Support pivot at {price}",
                            IsVisible = true },

                    // ── Support Zone line ───────────────────────────────────────────────
                    // Background sine drone — PitchMapping.Value so pitch tracks where the
                    // support level sits in the viewport. Steps when a new pivot confirms.
                    // IsZoneLine = true → NavigationFeedbackManager plays proximity cue at cursor.
                    new() { Name = CompSupportLine,
                            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Level,
                            DefaultColorHex = "#FFD54F", DefaultThickness = 1.5f,
                            DefaultDashStyle = DashStyle.Dash,
                            DefaultWaveform = "sine",
                            DefaultEnvelopeType = "Sustain",
                            DefaultPitchMapping = PitchMapping.Value, DefaultBaseFrequency = 300.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultIsZoneLine = true,
                            IsVisible = true },

                },

                // ── Zone bands: per-level shaded bands around each active S/R price ──────────
                // Each band hugs its carry-forward line with a ±0.3% width, giving a visible
                // zone without flooding the chart. Purely visual — no new components.
                DefaultZoneBands = new List<ZoneBandConfig>
                {
                    // Resistance zone band — semi-transparent purple, ±0.3% of price level.
                    // #AARRGGBB: A=0x40 (25%), R=0xCE, G=0x93, B=0xD8 (light purple matches dot colour).
                    new() { ComponentName = CompResistanceLine,
                            ColorHex = "#40CE93D8", BandWidthPct = 0.3f,
                            DisplayName = "Resistance Band", IsVisible = true },

                    // Support zone band — semi-transparent gold, ±0.3% of price level.
                    // #AARRGGBB: A=0x40 (25%), R=0xFF, G=0xD7, B=0x00 (matches gold dot colour).
                    new() { ComponentName = CompSupportLine,
                            ColorHex = "#40FFD700", BandWidthPct = 0.3f,
                            DisplayName = "Support Band", IsVisible = true },
                },

                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "PivotBars",        DisplayName = "Pivot Bars (each side)", DataType = typeof(int),    DefaultValue = 5.0,
                            Description = "Bars on each side a high or low must beat to be a pivot. Ignored while Auto-Scale is on (the default): set Auto-Scale to 0 to use this value." },
                    new() { Name = "VolumeLookback",   DisplayName = "Volume Lookback",        DataType = typeof(int),    DefaultValue = 20.0,
                            Description = "Bars for rolling volume average used in confluence filtering." },
                    new() { Name = "VolumeMultiplier", DisplayName = "Volume Multiplier",      DataType = typeof(double), DefaultValue = 1.2,
                            Description = "Pivot volume must be ≥ this × average. Set to 0 to disable volume filtering." },
                    new() { Name = "BreakThreshold",   DisplayName = "Break Threshold (%)",    DataType = typeof(double), DefaultValue = 0.2,
                            Description = "Fixed-mode: close must clear this % beyond the zone price to invalidate the level (e.g. 0.2 = 0.2%). Ignored when AdaptiveBreak is true." },
                    new() { Name = "AdaptiveBreak",    DisplayName = "Adaptive Break (ATR-scaled)", DataType = typeof(bool), DefaultValue = true,
                            Description = "When true, the break threshold scales with volatility: max(0.1%, 0.25 × ATR/close). This prevents a fixed 0.2% from firing spurious breaks during volatile days and missing real breaks during quiet ones." },
                    new() { Name = "AtrPeriod",        DisplayName = "ATR Period (Adaptive Break)", DataType = typeof(int), DefaultValue = 14.0,
                            Description = "ATR lookback used when AdaptiveBreak is true." },
                    new() { Name = "AutoScale",        DisplayName = "Auto-Scale Pivot Bars",  DataType = typeof(int),    DefaultValue = 1.0,
                            Description = "1 = each bar's pivot window grows with its position in the history: bar number ÷ 25, from 2 up to 15 bars each side (15 from bar 374 on). Pivot Bars is ignored while this is on. 0 = use Pivot Bars." },
                }
            }
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            if (!code.Equals("CIPHER_SR", StringComparison.OrdinalIgnoreCase)) return;
            int n = data.Length;
            if (n < 6) return;

            int    pivotBars   = GetInt(parameters, "PivotBars",        5);
            int    volLookback = GetInt(parameters, "VolumeLookback",  20);
            double volMult     = GetDbl(parameters, "VolumeMultiplier", 1.2);
            double breakPctFixed = GetDbl(parameters, "BreakThreshold", 0.2) / 100.0; // % → ratio
            bool   autoScale   = GetInt(parameters, "AutoScale",        1) != 0;
            bool   adaptiveBreak = IndicatorParams.GetBool(parameters, "AdaptiveBreak", true);
            int    atrPeriod   = Math.Max(2, GetInt(parameters, "AtrPeriod", 14));

            // Adaptive break uses ATR/close so the threshold adjusts to realized volatility.
            // A quiet daily BTC day (ATR ≈ 0.8%) produces a 0.2% threshold; a crash day
            // (ATR ≈ 4%) scales it up to 1.0%, preventing pullback-noise from firing breaks.
            double[]? atrSeries = null;
            if (adaptiveBreak)
                atrSeries = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(data, atrPeriod);

            // ── Output arrays ─────────────────────────────────────────────────────────────
            var resistance        = NanArray(n);
            var resistanceLine    = NanArray(n);
            var support           = NanArray(n);
            var supportLine       = NanArray(n);
            var resistanceTouches = NanArray(n);
            var supportTouches    = NanArray(n);
            var resistanceReplaced = NanArray(n);
            var supportReplaced    = NanArray(n);

            // Levels indexed by the bar they become CONFIRMABLE (pivot bar + its own window),
            // which is the bar the carry-forward line is allowed to step to them. Separate from
            // the dot arrays above, which stay on the pivot bar because that is where the level
            // visually is — see the Causality declarations on the two dot components.
            var resConfirmed = NanArray(n);
            var supConfirmed = NanArray(n);

            // ── Pass 1: Pivot detection with optional volume confluence ────────────────────
            for (int i = 1; i < n - 1; i++)
            {
                // Auto-scale targets ~1 pivot per 25 bars. It used to derive the window from the
                // TOTAL loaded bar count — Clamp(n / 25, 2, 15) — which made bar 40's answer depend
                // on how much history happened to be loaded after it: window 2 on a 60-bar chart,
                // window 15 on a 500-bar one, so a backtest over a long series and a live chart
                // disagreed about the same bar by construction. Deriving it from the bar's own
                // position keeps the intent (density grows as history accumulates) and makes the
                // window a property of the bar rather than of the fetch.
                int pb = autoScale ? Math.Clamp((i + 1) / 25, 2, 15) : pivotBars;
                if (i - pb < 0 || i + pb >= n) continue;

                if (data[i].Close <= 0 || data[i].High <= 0 || data[i].Low <= 0) continue;
                // Volume confluence: skip if volume doesn't clear the threshold.
                // volMult <= 0 disables the filter entirely.
                bool volOk = true;
                if (volMult > 0.01)
                {
                    double volAvg = RollingAverage(data, i, volLookback);
                    volOk = volAvg < 1e-10 || data[i].Volume >= volAvg * volMult;
                }

                // Pivot high: high[i] strictly greatest in the symmetric window.
                bool isPivotHigh = volOk;
                for (int k = 1; k <= pb && isPivotHigh; k++)
                    if (data[i].High <= data[i - k].High || data[i].High <= data[i + k].High)
                        isPivotHigh = false;
                if (isPivotHigh) { resistance[i] = data[i].High; resConfirmed[i + pb] = data[i].High; }

                // Pivot low: low[i] strictly smallest in the symmetric window.
                bool isPivotLow = volOk;
                for (int k = 1; k <= pb && isPivotLow; k++)
                    if (data[i].Low >= data[i - k].Low || data[i].Low >= data[i + k].Low)
                        isPivotLow = false;
                if (isPivotLow) { support[i] = data[i].Low; supConfirmed[i + pb] = data[i].Low; }
            }

            // ── Pass 2: Carry-forward zone lines with NaN-break and break detection ────────
            //
            // The line steps to a level at the bar the pivot became CONFIRMABLE, not at the pivot
            // bar. A pivot at bar i needs bars i+1..i+pb to be a pivot at all, so carrying the
            // level forward from i+1 — what this did until 2026-08-21 — drew the zone up to 14 bars
            // before it existed, and a strategy comparing price to the zone read a level derived
            // from bars it had not seen. This is the same rule SwingStructureAnalyzer already used
            // (ConfirmedAtIndex = BarIndex + Span).
            //
            // Replacement: at the bar where a new level is accepted while an old one is live, the
            //   line carries the NEW level and "{zone}_replaced" records the old one. The line used
            //   to be NaN on that bar (to stop the renderer drawing a connector), which every reader
            //   of the line took for "no level" and the narrator took for a break.
            //
            // Break detection: a close convincingly past the level invalidates it.
            //   The zone emits NaN for all subsequent bars until a new pivot forms.
            //   Note: break check runs on the PREVIOUS level before updating to the new one,
            //   so a break on the same bar as a new pivot doesn't accidentally invalidate the
            //   newly confirmed level.
            double lastRes = double.NaN;
            double lastSup = double.NaN;
            int resTouchCount = 0;
            int supTouchCount = 0;

            for (int i = 0; i < n; i++)
            {
                if (data[i].Close <= 0) continue;
                bool newRes = !double.IsNaN(resConfirmed[i]);
                bool newSup = !double.IsNaN(supConfirmed[i]);

                // Compute the effective break threshold for this bar.
                double breakPct = breakPctFixed;
                if (adaptiveBreak && atrSeries != null && i < atrSeries.Length && !double.IsNaN(atrSeries[i]) && data[i].Close > 0)
                {
                    double atrFrac = atrSeries[i] / data[i].Close;
                    breakPct = Math.Max(0.001, 0.25 * atrFrac);
                }

                // Break detection runs on the previously held level before accepting a new one.
                if (!newRes && !double.IsNaN(lastRes) && data[i].Close > lastRes * (1.0 + breakPct))
                {
                    lastRes = double.NaN;
                    resTouchCount = 0;
                }
                if (!newSup && !double.IsNaN(lastSup) && data[i].Close < lastSup * (1.0 - breakPct))
                {
                    lastSup = double.NaN;
                    supTouchCount = 0;
                }

                // Accept new pivot level and reset touch counter. A level still live when a newer
                // one arrives is REPLACED, which is a different event from a break and is recorded
                // as one.
                if (newRes)
                {
                    if (!double.IsNaN(lastRes) && lastRes != resConfirmed[i]) resistanceReplaced[i] = lastRes;
                    lastRes = resConfirmed[i]; resTouchCount = 0;
                }
                if (newSup)
                {
                    if (!double.IsNaN(lastSup) && lastSup != supConfirmed[i]) supportReplaced[i] = lastSup;
                    lastSup = supConfirmed[i]; supTouchCount = 0;
                }

                // Count wick touches: wick reached the level but close did not close through it.
                // Not on the bar the level is accepted: that bar's wick is no test of a level it did
                // not yet know. The count is WRITTEN there (0), so the touch array, like the line,
                // has a value on every bar of the level's life and none of the previous level's.
                if (!double.IsNaN(lastRes))
                {
                    if (!newRes && data[i].High >= lastRes && data[i].Close < lastRes)
                        resTouchCount++;
                    resistanceTouches[i] = resTouchCount;
                }
                if (!double.IsNaN(lastSup))
                {
                    if (!newSup && data[i].Low <= lastSup && data[i].Close > lastSup)
                        supTouchCount++;
                    supportTouches[i] = supTouchCount;
                }

                // Zone line: the live level on every bar it is live, the bar it is accepted included.
                if (!double.IsNaN(lastRes)) resistanceLine[i] = lastRes;
                if (!double.IsNaN(lastSup)) supportLine[i]    = lastSup;
            }

            WriteToBuffer(buffer, CompResistance,        resistance,        n);
            WriteToBuffer(buffer, CompResistanceLine,    resistanceLine,    n);
            WriteToBuffer(buffer, CompSupport,           support,           n);
            WriteToBuffer(buffer, CompSupportLine,       supportLine,       n);
            WriteToBuffer(buffer, CompResistanceTouches, resistanceTouches, n);
            WriteToBuffer(buffer, CompSupportTouches,    supportTouches,    n);
            WriteToBuffer(buffer, CompResistanceReplaced, resistanceReplaced, n);
            WriteToBuffer(buffer, CompSupportReplaced,    supportReplaced,    n);
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        /// <summary>
        /// Bars before a pivot found here is the pivot a longer history would find. With Auto-Scale
        /// on, a bar's window is Clamp((i + 1) / 25, 2, 15), so it only stops depending on where the
        /// data starts at bar 374 (window 15); before 2026-10-17 this was computed from PivotBars as
        /// if Auto-Scale were off — 40 bars — and the headless alert chart, which fetches this many,
        /// found its pivots with a window of 2 (CipherSrSpeechTests).
        /// </summary>
        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            bool autoScale = GetInt(parameters, "AutoScale", 1) != 0;
            int pivot = autoScale ? AutoScaleMaxWindow : GetInt(parameters, "PivotBars", 5);
            int vol   = GetInt(parameters, "VolumeLookback", 20);
            int fixedWindow = (pivot * 2) + vol + 10;
            return autoScale ? Math.Max(fixedWindow, AutoScaleSaturatesAt + 1) : fixedWindow;
        }

        private const int AutoScaleMaxWindow = 15;
        /// <summary>The first bar index at which (i + 1) / 25 reaches <see cref="AutoScaleMaxWindow"/>.</summary>
        private const int AutoScaleSaturatesAt = AutoScaleMaxWindow * 25 - 1;

        /// <summary>
        /// Returns an imperative speech string for the given component at the current bar.
        /// Covers pivot dots (resistance/support with touch count and price), zone carry-forward
        /// lines (distance from current price and touch history), and companion touch-count arrays
        /// (returns null — these are navigation-invisible and should never be spoken directly).
        /// Returns null to fall through to SpeechTemplate or generic SpeechFormatter.
        /// </summary>
        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value))
                return "no data";

            // Providers return VALUE-ONLY strings. NavigationFeedbackManager prepends
            // "[Component Name]. [Type]. " on UP/DOWN moves.

            // Use the current live close (injected by NavigationFeedbackManager) rather than
            // bar.Close (the navigated bar's close) so the distance always reflects the present
            // price relationship to the level, regardless of which historical bar is navigated.
            double liveClose = allComponentData.TryGetValue("__live_close", out var lcArr) &&
                               lcArr != null && lcArr.Length > 0 && lcArr[0] > 0
                ? lcArr[0]
                : bar.Close;
            return componentName switch
            {
                // Pivot dots: level price, where current price is relative to it, and how many
                // times price wick-tested this level without closing through it.
                CompResistance => FormatPivotSpeech(value, liveClose, allComponentData, CompResistanceLine, CompResistanceTouches, dataIndex),
                CompSupport    => FormatPivotSpeech(value, liveClose, allComponentData, CompSupportLine,    CompSupportTouches,    dataIndex),

                // Zone lines: level price, position of current price, and accumulated wick-test count.
                CompResistanceLine => FormatZoneLineSpeech(value, liveClose, allComponentData, CompResistanceTouches, dataIndex, isResistance: true),
                CompSupportLine    => FormatZoneLineSpeech(value, liveClose, allComponentData, CompSupportTouches,    dataIndex, isResistance: false),

                _ => null
            };
        }

        /// <summary>
        /// Returns a spoken summary of the indicator's state at the given bar index.
        /// Names the most recent confirmed resistance and support (the levels the zone lines
        /// carry on this bar) and which side of them the bar closed. No key speaks this today:
        /// BarDetailService bypasses GetDetailFact (BarDetailService.cs, "intentionally bypassed").
        /// </summary>
        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> results, int index, Dictionary<string, object> parameters)
        {
            if (!code.Equals("CIPHER_SR", StringComparison.OrdinalIgnoreCase) || index < 0 || data.Length == 0)
                return string.Empty;

            var sb = new StringBuilder();

            // The levels the zone lines carry on this bar: the most recent CONFIRMED swing high and
            // low. Not the dots — a dot sits on its pivot bar, PivotBars bars before anything could
            // know it was one, and this used to prefer the dot's value when there was one. And not
            // "nearest", which it used to say: the nearest level above price is often an older one.
            double resLevel = GetVal(results, CompResistanceLine, index);
            double supLevel = GetVal(results, CompSupportLine,    index);

            if (!double.IsNaN(resLevel))
                sb.Append($"Most recent resistance: {Accessibility.SpeechPriceFormatter.FormatPrice(resLevel)}. ");
            if (!double.IsNaN(supLevel))
                sb.Append($"Most recent support: {Accessibility.SpeechPriceFormatter.FormatPrice(supLevel)}. ");

            if (!double.IsNaN(resLevel) && !double.IsNaN(supLevel))
            {
                double close = data.Length > 0 && index < data.Length ? data[index].Close : 0;
                if (close > resLevel)       sb.Append("Price above both.");
                else if (close < supLevel)  sb.Append("Price below both.");
                else                        sb.Append("Price between levels.");
            }

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "No confirmed S/R levels yet.";
        }

        // ── Speech helpers ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Formats pivot dot speech: level price, where current price is, and touch count.
        /// E.g. "170.00, price 2.3% below, tested 3 times" or "170.00, at price, tested once".
        /// </summary>
        /// <remarks>
        /// The SIDE comes from where price is, not from the dot's type: an old resistance price
        /// has since risen above is "price 30% above", not "below" (until 2026-10-17 every
        /// resistance dot said "below" and every support dot "above").
        ///
        /// The COUNT is this dot's own level's. The level a dot marks becomes live PivotBars bars
        /// AFTER the dot — on the first bar after it that the zone line carries the dot's price —
        /// and until then the touch array counts the PREVIOUS level. So the count is read over the
        /// run of bars on which the zone line carries this price, starting there, and is the count
        /// on the run's last bar. It used to be read from the dot forward to the first empty value,
        /// which was the previous level's last bar: 44 dots on BTC daily spoke another level's
        /// count and none spoke their own (CipherSrSpeechTests).
        /// </remarks>
        private static string FormatPivotSpeech(double value, double liveClose,
            IReadOnlyDictionary<string, double[]> allComponentData, string lineKey, string touchKey, int dataIndex)
        {
            double distancePct = liveClose > 0 ? Math.Abs(liveClose - value) / liveClose * 100.0 : double.NaN;
            // NaN distancePct means no valid close was available (zero bar or no data).
            string distStr = double.IsNaN(distancePct)
                ? ""
                : distancePct < 0.1
                    ? "at price"
                    : liveClose < value
                        ? $"price {distancePct:F1}% below"
                        : $"price {distancePct:F1}% above";

            int touches = 0;
            if (allComponentData.TryGetValue(lineKey, out var line) && line.Length > 0
                && allComponentData.TryGetValue(touchKey, out var arr) && arr.Length > 0)
            {
                int start = -1;
                for (int i = dataIndex + 1; i < line.Length; i++)
                    if (line[i] == value) { start = i; break; }
                if (start >= 0)
                {
                    int end = start;
                    while (end + 1 < line.Length && line[end + 1] == value) end++;
                    if (end < arr.Length && !double.IsNaN(arr[end])) touches = (int)arr[end];
                }
            }

            string touchStr = touches == 0 ? "" : touches == 1 ? ", tested once" : $", tested {touches} times";
            string sep = string.IsNullOrEmpty(distStr) ? "" : ", ";

            return $"{Accessibility.SpeechPriceFormatter.FormatPrice(value)}{sep}{distStr}{touchStr}";
        }

        /// <summary>
        /// Formats zone line speech: level price, price position relative to the zone, and
        /// the accumulated wick-touch count from the companion _touches array at the current bar.
        /// E.g. "170.00, price below, tested 3 times" or "170.00, price above".
        /// </summary>
        private static string FormatZoneLineSpeech(double level, double closePrice,
            IReadOnlyDictionary<string, double[]> allComponentData, string touchKey, int dataIndex, bool isResistance)
        {
            string posStr = isResistance
                ? (closePrice >= level ? "price above" : "price below")
                : (closePrice <= level ? "price below" : "price above");

            int touches = 0;
            if (allComponentData.TryGetValue(touchKey, out var arr) && dataIndex < arr.Length && !double.IsNaN(arr[dataIndex]))
                touches = (int)arr[dataIndex];

            string touchStr = touches == 0 ? "" : touches == 1 ? ", tested once" : $", tested {touches} times";
            return $"{Accessibility.SpeechPriceFormatter.FormatPrice(level)}, {posStr}{touchStr}";
        }

        // ── Data helpers ──────────────────────────────────────────────────────────────────

        private static double[] NanArray(int n)
        {
            var arr = new double[n];
            for (int i = 0; i < n; i++) arr[i] = double.NaN;
            return arr;
        }

        private static double RollingAverage(ReadOnlySpan<Ohlcv> data, int idx, int lookback)
        {
            int start = Math.Max(0, idx - lookback);
            double sum = 0; int count = 0;
            for (int i = start; i < idx; i++) { sum += data[i].Volume; count++; }
            return count > 0 ? sum / count : 0;
        }

        private static void WriteToBuffer(IIndicatorResultBuffer buffer, string name, double[] src, int n)
        {
            var span = buffer.GetComponentSpan(name);
            int len  = Math.Min(span.Length, n);
            for (int i = 0; i < len; i++) span[i] = src[i];
        }

        private static double GetVal(IReadOnlyDictionary<string, double[]> results, string key, int index)
        {
            if (results.TryGetValue(key, out var arr) && index < arr.Length) return arr[index];
            return double.NaN;
        }

        private static int GetInt(Dictionary<string, object> p, string k, int def)
        {
            if (p.TryGetValue(k, out var v))
            {
                if (v is int i)    return i;
                if (v is double d) return (int)d;
                if (int.TryParse(v?.ToString(), out int parsed)) return parsed;
            }
            return def;
        }

        private static double GetDbl(Dictionary<string, object> p, string k, double def)
        {
            if (p.TryGetValue(k, out var v))
            {
                if (v is double d) return d;
                if (v is int i)    return (double)i;
                if (double.TryParse(v?.ToString(), out double parsed)) return parsed;
            }
            return def;
        }

        // SR pivots have no OB/OS reference level lines.
        public List<LevelDescriptor> GetDefaultLevels(string code)
            => new();
    }
}
