using System.Globalization;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// <b>Cipher C — cycle lows, after Camel Finance's CF Cycle Trading Indicator.</b> Rebuilt
    /// 2026-10-21 (Cody: "Cipher c needs to be designed as close to the cf indicator as possible
    /// from any and all screen grabs"). The previous Cipher C was an Ehlers cyber-cycle oscillator
    /// in its own pane; its ten components are retired in <see cref="Strategies.SignalCatalog"/>.
    /// It also replaces Loukas Cycle Detection, removed on 2026-10-22 (see <see cref="RetiredIndicators"/>).
    ///
    /// <para>
    /// <b>What is drawn, and what CF draws.</b> CF is closed source; everything here comes from
    /// 86 vendor images and 405 frames of Camel's videos (strategy-lab-data/cf-frames/REPORT.md,
    /// summarised in docs/CIPHER_C_DESIGN.md). On the price chart:
    /// </para>
    /// <list type="bullet">
    /// <item>A green triangle and D ring under each daily cycle low; a bigger blue triangle and W
    /// ring under each weekly low; and a gold triangle and Y ring under each yearly low. Drawn on the
    /// LOW's bar, so they are known only once confirmed (look-ahead, like CF's).</item>
    /// <item>The cycle's length above each low, with CF's unit suffix: 49D for days and 24W for
    /// weeks, counted inclusively as CF does. The suffix is printed, not spoken.</item>
    /// <item>A green rounded check box on the bar a low is CONFIRMED, a grey-blue rounded refresh
    /// box while one is pending, a red cross when a bar goes under a pending low before it is due
    /// and cancels it (CF's red X), and an orange cross when price closes under a confirmed low,
    /// which is a failed cycle (a confirmed low is never taken back).</item>
    /// <item>Triangles over each finished cycle's top: red when it came early (left translated,
    /// bearish), orange in the middle, green late (right translated, bullish).</item>
    /// <item>A soft green tint behind the bars where the daily cycle is inside its timing window
    /// (blue for the weekly window on a chart with no daily cycle).</item>
    /// <item>CF's yellow arcs, a level wave under the candles with a trough under each low of the
    /// shortest running tier, drawn as that tier's timing window's picture
    /// (<see cref="ComponentCompanions.Path"/>). Look-ahead: drawn between known lows, and the live
    /// arc runs on to where the next low is due.</item>
    /// <item>The 10-bar simple moving average Camel trades against (CF's yellow line).</item>
    /// </list>
    ///
    /// <para>
    /// <b>Every component holds something to hear (2026-10-22).</b> Cody: <i>"Each component must
    /// hold value, simply hiding and muting it isn't what I want. hiding and muting should be a
    /// user defined preferential function ... a component which is only visual isn't any use to a
    /// screen reader."</i> Twenty-eight components became fifteen. Nothing is hidden or muted by
    /// default. A number that only explains another component (a low's cycle length, a
    /// confirmation's low and age, a window's bounds) is a companion array of that component
    /// (<see cref="ComponentCompanions"/>) and is said WITH it, not a keyboard stop of its own. The
    /// arcs are not a component: on CF they are a picture of where the cycle is, which the timing
    /// window says in words on every bar, so they came back (2026-10-22) as the window's drawing —
    /// named when the user lands on the window, switched off in Appearance. The rule for what is navigable: a dated one-bar event is a component; a
    /// state over a stretch of bars is one component with a value worth hearing on every bar (the
    /// timing windows); a fact about the whole chart (the preset) is said by Alt+Shift+D.
    /// </para>
    ///
    /// <para>
    /// <b>Tiers follow the chart.</b> A tier runs only where its cycle spans at least six bars and
    /// the bar is no longer than its unit, so a weekly chart has weekly and yearly lows and no
    /// "daily cycle low" at all — the confusion Cody hit on Loukas's weekly chart. The detector,
    /// the presets and its measured agreement with CF are in <see cref="CipherCEngine"/>.
    /// </para>
    /// </summary>
    public class CipherCProvider : IIndicatorProvider
    {
        public const string Code = "CIPHER_C";
        public string Name => "Cipher C";

        // ── Components ─────────────────────────────────────────────────────────
        // DisplayName is the Name throughout: speech and navigation key on either.
        public const string CompSma           = "SMA";
        public const string CompYcl           = "YCL";
        public const string CompWcl           = "WCL";
        public const string CompDcl           = "DCL";
        public const string CompDclConfirmed  = "DCL Confirmed";
        public const string CompWclConfirmed  = "WCL Confirmed";
        public const string CompYclConfirmed  = "YCL Confirmed";
        public const string CompPending       = "Cycle Low Pending";
        public const string CompCancelled     = "Cycle Low Cancelled";
        public const string CompFailed        = "Cycle Failed";
        public const string CompLeftTop       = "Left Translated Top";
        public const string CompMidTop        = "Mid Translated Top";
        public const string CompRightTop      = "Right Translated Top";
        public const string CompDailyWindow   = "Daily Timing Window";
        public const string CompWeeklyWindow  = "Weekly Timing Window";

        // ── Companions (see ComponentCompanions) ───────────────────────────────
        /// <summary>A low's cycle length in its tier's unit: days counted inclusively (CF's 49D), weeks, or years.</summary>
        public const string LengthSuffix = "_length";
        /// <summary>A low: how long after it was confirmed — days for a daily low, weeks for the others.</summary>
        public const string AfterSuffix = "_after";
        /// <summary>A confirmation: the price of the low it confirmed.</summary>
        public const string LowSuffix = "_low";
        /// <summary>A confirmation, cancellation or failure: how long ago the low was — days for the daily tier, weeks for the others.</summary>
        public const string AgoSuffix = "_ago";
        /// <summary>A confirmation after a cancellation: the price of the cancelled low it replaces.</summary>
        public const string ReplacedSuffix = "_replaced";
        /// <summary>A pending low: the lowest low since the last cycle low, and how long ago.</summary>
        public const string LowestSuffix = "_lowest", LowestAgoSuffix = "_lowestAgo";
        /// <summary>A translated top: where in its cycle it came (day, week or year N).</summary>
        public const string DaySuffix = "_day";
        /// <summary>A timing window's bounds, in its count's unit.</summary>
        public const string StartSuffix = "_start", EndSuffix = "_end";
        /// <summary>A cancellation or failure that still stands (no low confirmed since): how long ago, per bar.</summary>
        public const string SinceSuffix = "_since";
        /// <summary>Which tier the pending, cancelled, failed and top events belong to: 1 daily, 2 weekly, 3 yearly.</summary>
        public const string EventsTierKey = "__events_tier";
        /// <summary>The preset: its index, or -1-index when the symbol had none and this is the fallback.</summary>
        public const string PresetKey = "__preset";
        /// <summary>1 when the chart is not daily and the counts run in its bars, as the original runs them there; 0 on a daily chart.</summary>
        public const string InBarsKey = "__in_bars";

        // ── Parameters ─────────────────────────────────────────────────────────
        public const string ParamPreset     = "Preset";
        public const string ParamDailyMin   = "DailyMinDays";
        public const string ParamDailyMax   = "DailyMaxDays";
        public const string ParamWeeklyMin  = "WeeklyMinWeeks";
        public const string ParamWeeklyMax  = "WeeklyMaxWeeks";
        public const string ParamYearlyMin  = "YearlyMinYears";
        public const string ParamYearlyMax  = "YearlyMaxYears";
        public const string ParamSma        = "SmaLength";
        public const string ParamOverbought = "Overbought";
        public const string ParamOversold   = "Oversold";

        public const int DefaultSma = 10;
        public const double DefaultOverbought = 75, DefaultOversold = 10;

        internal const string DclHex = "#00E676", WclHex = "#2979FF", YclHex = "#FFD600", SmaHex = "#FFEB3B", ArcHex = "#D4A72C";

        private static IndicatorComponentMetadata Low(string name, string hex, float size, string tier, string markerText, string patch, double freq, int order, string confirmedBy) => new()
        {
            Name = name, DisplayName = name, DisplayType = ComponentDisplayType.TriangleUpRing, MarkerText = markerText, Role = ComponentRole.Signal,
            Causality = ComponentCausality.Lookahead, ConfirmedBy = confirmedBy, DefaultMarkerAnchor = MarkerAnchor.BelowBar,
            DefaultColorHex = hex, DefaultThickness = size, DefaultDrawOrder = order,
            DefaultEnvelopeType = "Ping", DefaultPlaybackLayer = PlaybackLayer.Midground,
            DefaultSoundPatchId = patch, DefaultBaseFrequency = freq, DefaultDecayMs = 400,
            DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
            DefaultSignalSpeechTemplate = $"{tier} cycle low.",
        };

        // No {price} in these templates: narration fills it with the bar's close, but the
        // cross-series signal list on the candles fills it with the component's own value — the
        // low's price for a cancellation, the confluence score for a confirmation.
        private static IndicatorComponentMetadata Event(string name, ComponentDisplayType shape, MarkerAnchor anchor, string hex,
            float size, string patch, double freq, PlaybackLayer layer, string speech, bool count = false) => new()
        {
            Name = name, DisplayName = name, DisplayType = shape, Role = ComponentRole.Signal, IsCount = count,
            Causality = ComponentCausality.Causal, DefaultMarkerAnchor = anchor,
            DefaultColorHex = hex, DefaultThickness = size,
            DefaultEnvelopeType = "Ping", DefaultPlaybackLayer = layer,
            DefaultSoundPatchId = patch, DefaultBaseFrequency = freq, DefaultDecayMs = 300,
            DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
            DefaultSignalSpeechTemplate = speech,
        };

        private static IndicatorComponentMetadata Top(string name, string hex, string speech) => new()
        {
            Name = name, DisplayName = name, DisplayType = ComponentDisplayType.TriangleDown, Role = ComponentRole.Signal,
            Causality = ComponentCausality.Lookahead, DefaultMarkerAnchor = MarkerAnchor.AboveBar,
            DefaultColorHex = hex, DefaultThickness = 3f,
            DefaultEnvelopeType = "Ping", DefaultPlaybackLayer = PlaybackLayer.Background,
            DefaultSoundPatchId = "triangle_bell", DefaultBaseFrequency = 520, DefaultDecayMs = 200,
            DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
            DefaultSignalSpeechTemplate = speech,
        };

        /// <summary>
        /// A timing window (<see cref="ComponentDisplayType.Window"/>): its value is the cycle's
        /// day (or week) count on every bar, which is what is heard; the tint is painted from its
        /// <see cref="ComponentCompanions.Tint"/> companion (1 inside the window, 0 outside), and
        /// Ctrl+Left/Right stops where the window opens and where it closes with no new low
        /// (<see cref="ComponentCompanions.Stops"/>). The day count is never drawn: it is not a
        /// price. The lowest running tier's window also carries the cycle arc
        /// (<see cref="ComponentCompanions.Path"/>), drawn in its colour.
        /// </summary>
        private static IndicatorComponentMetadata Window(string name, string tintHex, string openSpeech) => new()
        {
            Name = name, DisplayName = name, DisplayType = ComponentDisplayType.Window, Role = ComponentRole.Signal,
            Causality = ComponentCausality.Causal, IsCount = true, DefaultColorHex = ArcHex, DefaultThickness = 1f,
            PathName = "cycle arc",
            // New-bar narration says this on the bar the window opens ({value} = the count).
            DefaultSignalSpeechTemplate = openSpeech,
            DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
            DefaultPlaybackLayer = PlaybackLayer.Background,
            // The whole 0x18 alpha at 1, nothing at 0.
            DefaultBackgroundTint = BackgroundTint.PriceChart, DefaultTintColorAboveHex = tintHex, DefaultTintColorBelowHex = "#00000000",
            DefaultTintMidline = 0.5, DefaultTintFullAt = 1.0,
        };

        private static readonly string PresetKeys = string.Join(", ", CipherCEngine.Presets.Select(p => p.Key));

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code,
                Name = "Cipher C",
                Category = "Cycles",
                DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                // The confirm/cancel state machine and the tier's moving windows are re-decided
                // from the whole history; a cheap last-bar update would miss a cancellation.
                RequiresFullRecalcOnTick = true,
                Description =
                    "Daily, weekly and yearly cycle lows on the price chart. Green triangles under daily cycle lows " +
                    "with a ringed D, blue with a W under weekly, gold with a Y under yearly, each low's length with " +
                    "its unit (49D, 24W), a green check box when a low is confirmed, a grey-blue refresh box while " +
                    "one is pending, crosses when a pending low is cancelled or a cycle fails, translated-top triangles, and a " +
                    "tint over the daily timing window, whose day count is read on every bar, with the cycle arcs " +
                    "drawn under the candles from low to low. Cycle ranges are tuned " +
                    "for sixteen assets (the S&P, Nasdaq, Dow, dollar index, gold, silver, oil, gold and silver " +
                    "miners, Bitcoin, Ethereum and four crypto stocks). Lows are drawn on the low's bar but known " +
                    "only when confirmed, after the low has held for half the shortest cycle. A map of cycle " +
                    "timing, not an edge.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamPreset, DisplayName = "Asset preset", DataType = typeof(string), DefaultValue = "Auto",
                            Description = $"Whose cycle ranges to use. Auto reads the chart's symbol (SPX, US500 and ^GSPC are the S&P; XAUUSD is gold; a fund or future such as SPY, ES or GLD has none and takes the default count). Or type one of: {PresetKeys}." },
                    new() { Name = ParamDailyMin, DisplayName = "Daily cycle shortest (days)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 1,
                            Description = "0 = the preset's. In the chart's own days: calendar days for crypto, trading days for stocks. Off a daily chart, in its bars." },
                    new() { Name = ParamDailyMax, DisplayName = "Daily cycle longest (days)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 1,
                            Description = "0 = the preset's." },
                    new() { Name = ParamWeeklyMin, DisplayName = "Weekly cycle shortest (weeks)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 1,
                            Description = "0 = the preset's." },
                    new() { Name = ParamWeeklyMax, DisplayName = "Weekly cycle longest (weeks)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 1,
                            Description = "0 = the preset's." },
                    new() { Name = ParamYearlyMin, DisplayName = "Yearly cycle shortest (years)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 0.5,
                            Description = "0 = the preset's." },
                    new() { Name = ParamYearlyMax, DisplayName = "Yearly cycle longest (years)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 0.5,
                            Description = "0 = the preset's." },
                    new() { Name = ParamSma, DisplayName = "Moving average length", DataType = typeof(int), DefaultValue = (double)DefaultSma, MinValue = 2, Step = 1,
                            Description = "The simple moving average drawn on price, 10 by default." },
                    new() { Name = ParamOverbought, DisplayName = "Confirm level", DataType = typeof(double), DefaultValue = DefaultOverbought, MinValue = 1, MaxValue = 99, Step = 1,
                            Description = "75 by default. A low is confirmed when the hidden cycle line climbs through this after reaching the oversold level." },
                    new() { Name = ParamOversold, DisplayName = "Arm level", DataType = typeof(double), DefaultValue = DefaultOversold, MinValue = 0, MaxValue = 99, Step = 1,
                            Description = "10 by default. The hidden cycle line must reach this before the next low can be confirmed." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompSma, DisplayName = CompSma, DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Median,
                            Causality = ComponentCausality.Causal, DefaultColorHex = SmaHex, DefaultThickness = 1.5f,
                            DefaultUsePolarityColoring = false, DefaultIsAreaFill = false, DefaultPlaybackLayer = PlaybackLayer.Background },

                    // Biggest first, so the daily triangle is drawn over the weekly over the yearly.
                    Low(CompYcl, YclHex, 6f,   "Yearly", "Y", "dual_tone_bell", 196, 0, CompYclConfirmed),
                    Low(CompWcl, WclHex, 4.5f, "Weekly", "W", "dual_tone_bell", 262, 1, CompWclConfirmed),
                    Low(CompDcl, DclHex, 3f,   "Daily",  "D", "sine_bell",      330, 2, CompDclConfirmed),

                    // Value: the confluence score, 1 to 3 (a normal-length cycle, a close over the
                    // moving average, a close over the low bar's high), so a strategy can ask for
                    // "DCL Confirmed at least 2".
                    Event(CompDclConfirmed, ComponentDisplayType.CheckBox, MarkerAnchor.AboveBar, DclHex, 4.5f, "sine_bell", 392, PlaybackLayer.Foreground,
                          "Daily cycle low confirmed.", count: true),
                    Event(CompWclConfirmed, ComponentDisplayType.CheckBox, MarkerAnchor.AboveBar, DclHex, 5.5f, "dual_tone_bell", 294, PlaybackLayer.Foreground,
                          "Weekly cycle low confirmed.", count: true),
                    Event(CompYclConfirmed, ComponentDisplayType.CheckBox, MarkerAnchor.AboveBar, DclHex, 6.5f, "dual_tone_bell", 220, PlaybackLayer.Foreground,
                          "Yearly cycle low confirmed.", count: true),
                    // Value: the close. The bar the hidden cycle line reached oversold.
                    Event(CompPending, ComponentDisplayType.RefreshBox, MarkerAnchor.AboveBar, "#78909C", 4f, "triangle_bell", 440, PlaybackLayer.Background,
                          "Cycle low pending."),
                    // Value: the price of the low closed under. Warnings, not sell signals.
                    Event(CompCancelled, ComponentDisplayType.Cross, MarkerAnchor.BelowBar, "#FF1744", 3f, "triangle_bell", 220, PlaybackLayer.Midground,
                          "Pending cycle low cancelled, a bar went under it before it was due."),
                    Event(CompFailed, ComponentDisplayType.Cross, MarkerAnchor.AboveBar, "#FF6D00", 3f, "triangle_bell", 175, PlaybackLayer.Midground,
                          "Cycle failed, price closed under the cycle low."),

                    // Value: the top's high.
                    Top(CompLeftTop,  "#FF5252", "Left translated top, bearish."),
                    Top(CompMidTop,   "#FFA726", "Mid translated top."),
                    Top(CompRightTop, "#66BB6A", "Right translated top, bullish."),

                    Window(CompDailyWindow, "#1800E676", "Daily timing window open, day {value}."),
                    Window(CompWeeklyWindow, "#182979FF", "Weekly timing window open, week {value}."),
                },
            }
        };

        // ── Calculation ────────────────────────────────────────────────────────

        /// <summary>One tier's unit: what its counts and lengths are said in.</summary>
        private readonly record struct Unit(string Tier, string Word, string Words, double Bars);

        /// <summary>Everything one run decides, by component name and companion key. Public for the tests and the lab.</summary>
        public static Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object>? parameters)
        {
            int n = data.Length;
            var o = new Dictionary<string, double[]>();
            double[] A(string key) => o.TryGetValue(key, out var a) ? a : o[key] = CipherCEngine.Nan(n);
            foreach (var c in new[] { CompSma, CompYcl, CompWcl, CompDcl, CompDclConfirmed, CompWclConfirmed, CompYclConfirmed,
                                      CompPending, CompCancelled, CompFailed, CompLeftTop, CompMidTop, CompRightTop,
                                      CompDailyWindow, CompWeeklyWindow })
                A(c);
            // The tint companions always exist, so a window with no tint of its own paints nothing
            // rather than falling back to tinting by its day count.
            A(CompDailyWindow + ComponentCompanions.Tint); A(CompWeeklyWindow + ComponentCompanions.Tint);
            A(CompDailyWindow + ComponentCompanions.Path); A(CompWeeklyWindow + ComponentCompanions.Path);
            if (n == 0) return o;

            var p = parameters ?? new Dictionary<string, object>();
            var chartClock = CipherCEngine.MeasureBars(data);
            var (preset, fineTuned) = CipherCEngine.Resolve(Str(p, ParamPreset), Str(p, "__symbol"), chartClock.TradesEveryDay);
            // Off a daily chart the original says "Timeframe Not Optimised, Switch to Daily" and runs
            // its daily count in the chart's own bars: the Dow's 36 is 36 bars on its weekly and
            // four-hour charts alike (best fits to its labels 36 and 36; gold's 22 fits at 23 on
            // weekly, the miners' 22 at 21, Bitcoin's 48 at 51 on four-hour, 2026-10-25). So does
            // this: a bar is a day, on the preset's own calendar, and every count is in bars.
            bool inBars = Math.Abs(chartClock.BarsPerDay - 1) > 1e-6;
            var clock = inBars ? new CipherCEngine.BarClock(1, preset.CalendarDays) : chartClock;
            double ob = Dbl(p, ParamOverbought, DefaultOverbought), os = Dbl(p, ParamOversold, DefaultOversold);

            // The preset's days are converted to the chart's own: a trading-day range on a chart
            // that trades every day grows by 7/5, and the other way shrinks.
            double dayScale = (clock.TradesEveryDay ? 7.0 : 5.0) / (preset.CalendarDays ? 7.0 : 5.0);
            // The window drawn and said is the preset's range; the cycle is counted from
            // CountedDailyMin, which differs from it only where the original counts from another length.
            double dWindowMin = Override(p, ParamDailyMin, preset.DailyMin * dayScale);
            double dMin = Override(p, ParamDailyMin, preset.CountedDailyMin * dayScale), dMax = Override(p, ParamDailyMax, preset.DailyMax * dayScale);
            double wMin = Override(p, ParamWeeklyMin, preset.WeeklyMin), wMax = Override(p, ParamWeeklyMax, preset.WeeklyMax);
            double yMin = Override(p, ParamYearlyMin, preset.YearlyMin), yMax = Override(p, ParamYearlyMax, preset.YearlyMax);

            var dTier = CipherCEngine.Tier(dMin, dMax, 1, clock);
            var wTier = CipherCEngine.Tier(wMin * clock.DaysPerWeek, wMax * clock.DaysPerWeek, clock.DaysPerWeek, clock);
            var yTier = CipherCEngine.Tier(yMin * clock.DaysPerYear, yMax * clock.DaysPerYear, clock.DaysPerYear, clock);
            double barsPerWeek = clock.BarsPerDay * clock.DaysPerWeek;
            var d = CipherCEngine.Detect(data, dTier, ob, os, searchOnTurn: true);
            // With a daily tier the weekly lows are picked from the daily ones, as the original
            // picks them; on a weekly chart the weekly tier finds its own.
            bool weeklyFromDaily = dTier.Active && wTier.Active;
            var w = !weeklyFromDaily ? CipherCEngine.Detect(data, wTier, ob, os)
                : preset.WeeklyEveryTwoDaily ? CipherCEngine.WeeklyFromTwoDaily(d, data, 2 * (dTier.MinBars - 2))
                : preset.WeeklyNextDaily ? CipherCEngine.WeeklyFromNextDaily(d, data,
                    4.5 * dWindowMin * clock.BarsPerDay, 4 * dWindowMin * clock.BarsPerDay, 6 * dWindowMin * clock.BarsPerDay)
                : CipherCEngine.WeeklyFromDaily(d, data, CipherCEngine.KeepBars(wMin, barsPerWeek), dTier.MinBars);
            var y = CipherCEngine.Detect(data, yTier, ob, os);
            // Off daily a week of the count is five or seven bars, not a week, so its lengths,
            // ages and windows are said in bars too.
            var day = inBars ? new Unit("daily", "bar", "bars", 1) : new Unit("daily", "day", "days", clock.BarsPerDay);
            var week = inBars ? new Unit("weekly", "bar", "bars", 1) : new Unit("weekly", "week", "weeks", barsPerWeek);
            var year = inBars ? new Unit("yearly", "bar", "bars", 1) : new Unit("yearly", "year", "years", clock.BarsPerDay * clock.DaysPerYear);
            double weeksInUnits = inBars ? clock.DaysPerWeek : 1;

            var close = new double[n]; var high = new double[n]; var low = new double[n];
            for (int i = 0; i < n; i++) { close[i] = data[i].Close; high[i] = data[i].High; low[i] = data[i].Low; }

            // The moving average.
            int smaLen = Math.Max(2, Int(p, ParamSma, DefaultSma));
            var sma = o[CompSma];
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += close[i];
                if (i >= smaLen) sum -= close[i - smaLen];
                if (i >= smaLen - 1) sma[i] = sum / smaLen;
            }

            // Where the lows are. A weekly low is one of the daily lows near it, a yearly low one
            // of the weekly (or daily) ones — CF's stacks always sit on one another.
            int dReach = (int)Math.Round(dTier.MinBars / 2), wReach = (int)Math.Round(wTier.MinBars / 2);
            var dConfirm = Confirmations(d, null, 0, data);
            var wConfirm = Confirmations(w, dTier.Active && !weeklyFromDaily ? d.Lows : null, dReach, data);
            var yConfirm = Confirmations(y, wTier.Active ? w.Lows : dTier.Active ? d.Lows : null, wTier.Active ? wReach : dReach, data);
            var dLows = new List<int>(d.Lows);
            var wLows = dTier.Active && !weeklyFromDaily ? CipherCEngine.SnapTo(w.Lows, dLows, dReach, data) : new List<int>(w.Lows);
            var wDrawn = new List<int>(wLows);
            var yLows = wTier.Active ? CipherCEngine.SnapTo(y.Lows, wLows, wReach, data)
                      : dTier.Active ? CipherCEngine.SnapTo(y.Lows, dLows, dReach, data) : y.Lows;
            // And a higher low with no lower one near it is a lower low as well: a weekly cycle
            // low IS a daily cycle low. Picked from the daily lows, a weekly low is one already,
            // unless the daily tier later cancelled it; a yearly low, or a weekly one found on its
            // own, can miss a daily low the daily detector skipped. CF draws a D under every W; so
            // does this. A low added this way was confirmed by the higher tier, so it takes that
            // tier's confirmation.
            if (wTier.Active) foreach (int i in yLows) { AddLow(wLows, i); wConfirm.TryAdd(i, yConfirm.GetValueOrDefault(i, -1)); }
            if (dTier.Active)
            {
                foreach (int i in wLows) { AddLow(dLows, i); dConfirm.TryAdd(i, wConfirm.GetValueOrDefault(i, -1)); }
                foreach (int i in yLows) { AddLow(dLows, i); dConfirm.TryAdd(i, yConfirm.GetValueOrDefault(i, -1)); }
            }

            // Each low: its price, its cycle's length (CF's count, drawn as the label), and how long
            // after it was confirmed. The weekly label is drawn only where there is no daily one, so
            // the two never print on top of each other.
            WriteLows(o, CompDcl, dLows, dConfirm, low, day, day, label: true, LengthInDays(clock));
            WriteLows(o, CompWcl, wLows, wConfirm, low, week, week, label: !dTier.Active, (a, b) => Math.Round((b - a) / week.Bars));
            WriteLows(o, CompYcl, yLows, yConfirm, low, year, week, label: false, (a, b) => Math.Round((b - a) / year.Bars, 1));

            // The confirmations: their confluence, the low, its age, and the low a confirmation
            // after a cancellation replaces.
            // A confirmation names the low as it is drawn: a weekly or yearly low moved onto a
            // lower tier's, as the lists above were built (before the lower tiers took in the
            // higher ones' lows).
            WriteConfirmed(o, CompDclConfirmed, d, dTier, day, sma, close, high, low, DrawnAt(d, null, 0, data));
            WriteConfirmed(o, CompWclConfirmed, w, wTier, week, sma, close, high, low,
                DrawnAt(w, dTier.Active && !weeklyFromDaily ? d.Lows : null, dReach, data));
            WriteConfirmed(o, CompYclConfirmed, y, yTier, week, sma, close, high, low,
                DrawnAt(y, wTier.Active ? wDrawn : dTier.Active ? d.Lows : null, wTier.Active ? wReach : dReach, data));

            // The events and the tops belong to the shortest tier that runs.
            var (lowest, lowestTier, lowestLows, tierNo, unit, ago) =
                dTier.Active ? (d, dTier, dLows, 1, day, day) : wTier.Active ? (w, wTier, wLows, 2, week, week) : (y, yTier, yLows, 3, year, week);
            if (dTier.Active || wTier.Active || yTier.Active) Array.Fill(A(EventsTierKey), tierNo);

            Copy(lowest.Pending, o[CompPending]);
            // Every companion exists whether or not its event fires: a stretch with no cancellation
            // has the same components as one with one, all NaN.
            double[] pendingLowest = A(CompPending + LowestSuffix), pendingLowestAgo = A(CompPending + LowestAgoSuffix);
            for (int t = 0; t < n; t++)
            {
                if (double.IsNaN(lowest.Pending[t])) continue;
                int last = lowest.LastLowAt[t];
                if (last < 0) continue;
                int at = last + 1;
                for (int i = last + 1; i <= t; i++) if (low[i] < low[at]) at = i;
                if (at > t) continue;
                pendingLowest[t] = low[at];
                pendingLowestAgo[t] = Units(t - at, ago);
            }

            Copy(lowest.Cancelled, o[CompCancelled]);
            Copy(lowest.Failed, o[CompFailed]);
            double[] cancelledSince = A(CompCancelled + SinceSuffix), failedSince = A(CompFailed + SinceSuffix);
            double[] cancelledAgo = A(CompCancelled + AgoSuffix), failedAgo = A(CompFailed + AgoSuffix);
            int cancelledAt = -1, failedAt = -1;
            for (int t = 0; t < n; t++)
            {
                if (!double.IsNaN(lowest.Confirmed[t])) cancelledAt = failedAt = -1;
                if (!double.IsNaN(lowest.Cancelled[t])) { cancelledAt = t; cancelledAgo[t] = Units(t - lowest.CancelledLow[t], ago); }
                if (!double.IsNaN(lowest.Failed[t])) { failedAt = t; failedAgo[t] = Units(t - lowest.ClosedUnder[t], ago); }
                if (cancelledAt >= 0) cancelledSince[t] = Units(t - cancelledAt, ago);
                if (failedAt >= 0) failedSince[t] = Units(t - failedAt, ago);
            }

            // Each finished cycle's top: where it came in the cycle, and how long the cycle was.
            var length = tierNo == 1 ? LengthInDays(clock) : tierNo == 2 ? (a, b) => Math.Round((b - a) / week.Bars) : (Func<int, int, double>)((a, b) => Math.Round((b - a) / year.Bars, 1));
            for (int k = 1; k < lowestLows.Count; k++)
            {
                int a = lowestLows[k - 1], b = lowestLows[k];
                int top = a;
                for (int i = a + 1; i < b; i++) if (high[i] > high[top]) top = i;
                if (top == a) continue;
                double ratio = (double)(top - a) / (b - a);
                string comp = ratio < 0.45 ? CompLeftTop : ratio > 0.55 ? CompRightTop : CompMidTop;
                o[comp][top] = high[top];
                A(comp + DaySuffix)[top] = Math.Floor((top - a) / unit.Bars + 1e-9) + 1;
                A(comp + LengthSuffix)[top] = length(a, b);
            }

            // Day and week counts against the timing windows — known on the bar.
            WriteWindow(o, CompDailyWindow, d, dTier, day.Bars, dWindowMin, dMax, tint: true, n);
            WriteWindow(o, CompWeeklyWindow, w, wTier, week.Bars, wMin * weeksInUnits, wMax * weeksInUnits, tint: !dTier.Active, n);

            // The cycle arc, on the window of the tier the events belong to (a yearly-only chart
            // has no window, so no arc). A picture, not a reading: what it shows — how far into
            // the cycle each bar is — the window says as its day count. Look-ahead: each arc is
            // drawn between two lows known only once confirmed, and the live arc runs on to where
            // the next low is due.
            if (tierNo <= 2)
                DrawArcs(o[(tierNo == 1 ? CompDailyWindow : CompWeeklyWindow) + ComponentCompanions.Path],
                    lowestLows, lowestTier, high, low, n);

            int presetIndex = IndexOf(preset);
            Array.Fill(A(PresetKey), fineTuned ? presetIndex : -1 - presetIndex);
            Array.Fill(A(InBarsKey), inBars ? 1 : 0);
            return o;
        }

        /// <summary>CF's count: the cycle that ended at a low, in days, counting both ends.</summary>
        private static Func<int, int, double> LengthInDays(CipherCEngine.BarClock clock) =>
            (a, b) => Math.Round((b - a) / clock.BarsPerDay) + 1;

        /// <summary>A span of bars in a unit: whole above ten, to a tenth below (an hourly chart's "2.4 days").</summary>
        private static double Units(int bars, Unit unit)
        {
            double v = bars / unit.Bars;
            return v >= 10 ? Math.Round(v) : Math.Round(v, 1);
        }

        /// <summary>
        /// Each of a tier's lows, by bar, with the bar it was confirmed on. A tier whose lows are
        /// snapped onto a lower tier's (<paramref name="lowerTier"/>) is keyed by the bar it snaps
        /// to, as <see cref="CipherCEngine.SnapTo"/> moves it; two that snap to one bar keep the
        /// earlier confirmation.
        /// </summary>
        private static Dictionary<int, int> Confirmations(CipherCEngine.TierRun run, IReadOnlyList<int>? lowerTier, int reach, ReadOnlySpan<Ohlcv> data)
        {
            var map = new Dictionary<int, int>();
            for (int k = 0; k < run.Lows.Count; k++)
            {
                int at = lowerTier == null ? run.Lows[k] : CipherCEngine.SnapTo(new[] { run.Lows[k] }, lowerTier, reach, data)[0];
                int when = run.ConfirmedAt[k];
                if (!map.TryGetValue(at, out int had) || when < had) map[at] = when;
            }
            return map;
        }

        private static void WriteLows(Dictionary<string, double[]> o, string comp, List<int> lows, Dictionary<int, int> confirmedAt,
            double[] low, Unit unit, Unit ago, bool label, Func<int, int, double> length)
        {
            int n = low.Length;
            var values = o[comp];
            double[] Companion(string suffix) => o.TryGetValue(comp + suffix, out var a) ? a : o[comp + suffix] = CipherCEngine.Nan(n);
            for (int k = 0; k < lows.Count; k++)
            {
                int i = lows[k];
                values[i] = low[i];
                if (k > 0)
                {
                    double len = length(lows[k - 1], i);
                    Companion(LengthSuffix)[i] = len;
                    if (label) Companion(ComponentCompanions.Label)[i] = len;
                }
                if (confirmedAt.TryGetValue(i, out int at) && at > i)
                    Companion(AfterSuffix)[i] = Units(at - i, ago);
            }
        }

        /// <summary>Where each low a tier's run confirmed is drawn: on the lower tier's low it is moved to, as <see cref="CipherCEngine.SnapTo"/> moves it.</summary>
        private static Dictionary<int, int> DrawnAt(CipherCEngine.TierRun run, IReadOnlyList<int>? lowerTier, int reach, ReadOnlySpan<Ohlcv> data)
        {
            var map = new Dictionary<int, int>();
            foreach (int c in run.LastLowAt)
                if (c >= 0 && !map.ContainsKey(c))
                    map[c] = lowerTier == null ? c : CipherCEngine.SnapTo(new[] { c }, lowerTier, reach, data)[0];
            return map;
        }

        private static void WriteConfirmed(Dictionary<string, double[]> o, string comp, CipherCEngine.TierRun run, CipherCEngine.TierBars tier,
            Unit ago, double[] sma, double[] close, double[] high, double[] low, Dictionary<int, int> drawnAt)
        {
            int n = close.Length;
            if (!tier.Active) return;
            var values = o[comp];
            double[] lowAt = CipherCEngine.Nan(n), age = CipherCEngine.Nan(n), replaced = CipherCEngine.Nan(n),
                     confirms = CipherCEngine.Nan(n);
            for (int t = 0; t < n; t++)
            {
                if (double.IsNaN(run.Confirmed[t])) continue;
                // The low a confirmation on bar t confirmed is the one LastLowAt names from then on;
                // the one before it is the low as it stood on the bar before.
                int c = run.LastLowAt[t];
                if (c < 0) continue;
                int before = t > 0 ? run.LastLowAt[t - 1] : -1;
                int prev = before >= 0 && before < c ? before : -1;
                int score = 0;
                if (prev >= 0 && c - prev >= tier.MinBars - 0.5 && c - prev <= tier.MaxBars + 0.5) score++;
                if (!double.IsNaN(sma[t]) && close[t] > sma[t]) score++;
                if (close[t] > high[c]) score++;
                values[t] = Math.Max(1, score);
                // Where the low is drawn; never after the confirmation (a lower tier's low that
                // comes later is not what this bar confirmed).
                int drawn = drawnAt.GetValueOrDefault(c, c);
                if (drawn > t) drawn = c;
                lowAt[t] = low[drawn];
                age[t] = Units(t - drawn, ago);
                confirms[t] = t - drawn;
                replaced[t] = run.Updated[t];
            }
            o[comp + LowSuffix] = lowAt;
            o[comp + AgoSuffix] = age;
            o[comp + ReplacedSuffix] = replaced;
            o[comp + ComponentCompanions.Confirms] = confirms;
        }

        /// <summary>
        /// A timing window: the count of the cycle's units since its last confirmed low (day 1 is
        /// the low's own), whether that is inside the window, its bounds, and the bars where the
        /// window opens or closes without a new low. <paramref name="tint"/> false leaves the tint
        /// companion all NaN — the weekly window on a chart with a daily one, whose green tint it
        /// would cover.
        /// </summary>
        private static void WriteWindow(Dictionary<string, double[]> o, string comp, CipherCEngine.TierRun run, CipherCEngine.TierBars tier,
            double barsPerUnit, double min, double max, bool tint, int n)
        {
            if (!tier.Active) return;
            var count = o[comp];
            var inWindow = CipherCEngine.Nan(n);
            var start = CipherCEngine.Nan(n); var end = CipherCEngine.Nan(n); var stops = CipherCEngine.Nan(n);
            Array.Fill(start, Math.Round(min, 1));
            Array.Fill(end, Math.Round(max, 1));
            for (int t = 0; t < n; t++)
            {
                int last = run.LastLowAt[t];
                if (last < 0) continue;
                double units = Math.Floor((t - last) / barsPerUnit + 1e-9) + 1;
                count[t] = units;
                inWindow[t] = units >= min && units <= max ? 1 : 0;
                bool sameCycle = t > 0 && run.LastLowAt[t - 1] == last;
                bool wasIn = t > 0 && inWindow[t - 1] == 1;
                if (inWindow[t] == 1 && !(wasIn && sameCycle)) stops[t] = 1;          // opens
                else if (inWindow[t] == 0 && wasIn && sameCycle) stops[t] = 0;       // closes with no new low
            }
            o[comp + ComponentCompanions.Tint] = tint ? inWindow : CipherCEngine.Nan(n);
            o[comp + StartSuffix] = start;
            o[comp + EndSuffix] = end;
            o[comp + ComponentCompanions.Stops] = stops;
        }

        /// <summary>
        /// CF's yellow arcs: one hump from each low to the next, and a live one from the last low
        /// to the middle of the window the next is due in. They sit as a level wave under the
        /// candles (the vendor's demo frames), every hump the same height.
        /// </summary>
        private static void DrawArcs(double[] arc, List<int> lows, CipherCEngine.TierBars tier, double[] high, double[] low, int n)
        {
            if (lows.Count == 0) return;
            var floor = ArcFloors(lows, high, low, n, out double hump);
            for (int k = 1; k < lows.Count; k++)
                DrawArc(arc, lows[k - 1], lows[k], lows[k] - lows[k - 1], floor[k - 1], floor[k], hump);
            int a = lows[^1], due = Math.Max(1, (int)Math.Round(tier.MeanBars));
            DrawArc(arc, a, Math.Min(n - 1, a + due), due, floor[^1], floor[^1], hump);
        }

        /// <summary>
        /// Where the arcs sit. The hump is a quarter of the median cycle's swing (log of its high
        /// over its low), so it reads the same on any asset and timeframe and does not squeeze the
        /// candles when the price pane fits it in. Each low's floor is one and a quarter humps under
        /// the lowest low of the two cycles it joins, so every crest stays a quarter hump under the
        /// candles. Returned in log price, one per low.
        /// </summary>
        private static double[] ArcFloors(List<int> lows, double[] high, double[] low, int n, out double hump)
        {
            var swings = new List<double>();
            for (int k = 1; k < lows.Count; k++)
            {
                double hi = 0, lo = double.MaxValue;
                for (int i = lows[k - 1]; i <= lows[k]; i++) { hi = Math.Max(hi, high[i]); lo = Math.Min(lo, low[i]); }
                if (lo > 0) swings.Add(Math.Log(hi / lo));
            }
            swings.Sort();
            hump = swings.Count > 0 ? 0.25 * swings[swings.Count / 2] : 0.05;
            var floor = new double[lows.Count];
            for (int k = 0; k < lows.Count; k++)
            {
                int from = k > 0 ? lows[k - 1] : lows[k], to = k + 1 < lows.Count ? lows[k + 1] : n - 1;
                double lo = double.MaxValue;
                for (int i = from; i <= to; i++) if (low[i] > 0) lo = Math.Min(lo, low[i]);
                floor[k] = Math.Log(lo) - 1.25 * hump;
            }
            return floor;
        }

        /// <summary>
        /// One arc: from the floor at low <paramref name="a"/> to the floor at the far end,
        /// <paramref name="span"/> bars later, rising a hump in the middle; rounded at both ends, as
        /// CF draws them. The floor eases from one level to the next and is flat at each low, so the
        /// trough stays on the low's bar. Drawn up to bar <paramref name="last"/>; the live arc's far
        /// end is in the future.
        /// </summary>
        private static void DrawArc(double[] arc, int a, int last, int span, double fa, double fb, double hump)
        {
            if (span <= 0) return;
            for (int i = a; i <= last; i++)
            {
                double t = Math.Min(1, (double)(i - a) / span);
                double level = fa + (fb - fa) * t * t * t * (10 - 15 * t + 6 * t * t);
                arc[i] = Math.Exp(level + hump * 0.5 * (1 - Math.Cos(2 * Math.PI * t)));
            }
        }

        private static void AddLow(List<int> lows, int i)
        {
            int at = lows.BinarySearch(i);
            if (at < 0) lows.Insert(~at, i);
        }

        private static int IndexOf(CyclePreset preset)
        {
            for (int i = 0; i < CipherCEngine.Presets.Count; i++) if (CipherCEngine.Presets[i].Key == preset.Key) return i;
            return 0;
        }

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            if (!code.Equals(Code, StringComparison.OrdinalIgnoreCase)) return;
            foreach (var (name, values) in Compute(data, parameters))
            {
                var span = buffer.GetComponentSpan(name);
                int len = Math.Min(span.Length, values.Length);
                for (int i = 0; i < len; i++) span[i] = values[i];
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        /// <summary>
        /// The daily detector's settling time on a daily chart with the widest preset (Bitcoin's 66
        /// days): its slow average is a cycle long and it starts after two of them.
        /// </summary>
        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 132;

        public List<LevelDescriptor> GetDefaultLevels(string code) => new();

        // ── Speech ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The cycle's state on a bar in words — where the daily and weekly cycles are against
        /// their timing windows, a confirmation's confluence, and a cancellation or failure that
        /// still stands. Read by Alt+Shift+D after the components' values
        /// (<see cref="Accessibility.BarDetailService"/>) and by the detail fact. With
        /// <paramref name="barDate"/>, the preset the chart uses is said last — a fact about the
        /// whole chart, so it is said when the Cipher C is the series being read, not on the ladder.
        /// </summary>
        public static IReadOnlyList<string> StateWords(Func<string, double[]?> component, int index, DateTime? barDate = null)
        {
            var words = new List<string>();
            double At(string name) => component(name) is { } a && index >= 0 && index < a.Length ? a[index] : double.NaN;

            bool inBars = At(InBarsKey) == 1;
            var (dayWord, dayWords) = Words(inBars, "day", "days");
            var (weekWord, weekWords) = Words(inBars, "week", "weeks");
            string? daily = WindowWords("daily cycle", dayWord, dayWords, At(CompDailyWindow), At(CompDailyWindow + StartSuffix), At(CompDailyWindow + EndSuffix));
            if (daily != null) words.Add(daily);
            string? weekly = WindowWords("weekly cycle", weekWord, weekWords, At(CompWeeklyWindow), At(CompWeeklyWindow + StartSuffix), At(CompWeeklyWindow + EndSuffix));
            if (weekly != null) words.Add(weekly);

            foreach (var (comp, tier) in new[] { (CompDclConfirmed, "daily"), (CompWclConfirmed, "weekly"), (CompYclConfirmed, "yearly") })
            {
                double conf = At(comp);
                if (!double.IsNaN(conf)) words.Add($"{tier} cycle low confirmed, {ConfluenceWord(conf)} confluence");
            }

            string events = TierWord(At(EventsTierKey));
            (string unit, string units) = AgoUnit(At(EventsTierKey), inBars);
            if (!double.IsNaN(At(CompCancelled))) words.Add($"{events} pending cycle low cancelled");
            else if (At(CompCancelled + SinceSuffix) is var cs && !double.IsNaN(cs))
                words.Add($"the last {events} pending cycle low was cancelled {Span(cs, unit, units)} ago");
            if (!double.IsNaN(At(CompFailed))) words.Add($"{events} cycle failed");
            else if (At(CompFailed + SinceSuffix) is var fs && !double.IsNaN(fs))
                words.Add($"the {events} cycle failed {Span(fs, unit, units)} ago");

            if (barDate is { } date && !double.IsNaN(At(PresetKey)))
                words.Add(PresetWords(At(PresetKey), date, inBars));
            return words;
        }

        internal static string? WindowWords(string cycle, string unit, string units, double count, double start, double end)
        {
            if (double.IsNaN(start)) return null;          // the tier does not run on this chart
            if (double.IsNaN(count)) return $"no {cycle} low confirmed yet";
            string Units(double v) => Math.Abs(v - 1) < 1e-9 ? unit : units;
            string head = $"{cycle} {unit} {Num(count)}";
            if (count < start)
            {
                double inN = Math.Ceiling(start - count);
                return $"{head}, its window opens in {Num(inN)} {Units(inN)}";
            }
            if (count <= end)
            {
                double left = Math.Floor(end - count);
                // The first unit inside it: the bounds are said once, here.
                if (count - start < 1)
                    return $"{head}, its {Num(start)} to {Num(end)} {unit} window opens, {Num(left)} {Units(left)} left";
                return left < 1 ? $"{head}, the last {unit} of its window" : $"{head}, in its window, {Num(left)} {Units(left)} left";
            }
            double past = Math.Ceiling(count - end);
            return $"{head}, {Num(past)} {Units(past)} past its window";
        }

        public static string ConfluenceWord(double score) => score >= 3 ? "high" : score >= 2 ? "mid" : "low";

        private static string TierWord(double tier) => tier switch { 2 => "weekly", 3 => "yearly", _ => "daily" };

        /// <summary>What an age is counted in: days on the daily tier, weeks on the others; bars off a daily chart.</summary>
        private static (string, string) AgoUnit(double tier, bool inBars) => Words(inBars, tier is 2 or 3 ? "week" : "day", tier is 2 or 3 ? "weeks" : "days");

        /// <summary>A unit's words, or "bar" and "bars" when the chart is not daily and the counts run in bars.</summary>
        private static (string, string) Words(bool inBars, string unit, string units) => inBars ? ("bar", "bars") : (unit, units);

        private static string Span(double v, string unit, string units) => $"{Num(v)} {(Math.Abs(v - 1) < 1e-9 ? unit : units)}";

        /// <summary>The preset a chart used, in words, from the value of <see cref="PresetKey"/>;
        /// off a daily chart (<paramref name="inBars"/>), with the original's warning after it.</summary>
        public static string PresetWords(double value, DateTime barDate, bool inBars = false)
        {
            string text = PresetRanges(value, barDate);
            return inBars ? text + ". Not tuned for this timeframe: its days are counted as this chart's bars; switch to daily for the tuned count" : text;
        }

        private static string PresetRanges(double value, DateTime barDate)
        {
            if (double.IsNaN(value)) return "no preset";
            bool fineTuned = value >= 0;
            int index = fineTuned ? (int)value : -1 - (int)value;
            if (index < 0 || index >= CipherCEngine.Presets.Count) return "no preset";
            var preset = CipherCEngine.Presets[index];
            if (!fineTuned)
                return $"no preset for this symbol, so the default count: daily lows from {Num(CipherCEngine.DefaultCountDailyMin)} bars, " +
                       $"weekly {Range(preset.WeeklyMin, preset.WeeklyMax)} weeks, yearly {Range(preset.YearlyMin, preset.YearlyMax)} years";
            string text = fineTuned
                ? $"{preset.Name} ranges, {preset.Source}"
                : $"no preset for this symbol, so {preset.Name}'s ranges";
            text += $": daily {Range(preset.DailyMin, preset.DailyMax)} {(preset.CalendarDays ? "days" : "trading days")}" +
                    (preset.CountDailyMin > 0 ? $", counted from {Num(preset.CountDailyMin)}, " : ", ") +
                    $"weekly {Range(preset.WeeklyMin, preset.WeeklyMax)} weeks, yearly {Range(preset.YearlyMin, preset.YearlyMax)} years";
            if (preset.Key == "BTC" && fineTuned)
            {
                var halving = CipherCEngine.HalvingPhase(barDate);
                if (halving != null) text += $". {halving}";
            }
            return text;
        }

        private static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>"22 to 42", or "4" when a range is one value (Bitcoin's yearly cycle).</summary>
        private static string Range(double min, double max) => min == max ? Num(min) : $"{Num(min)} to {Num(max)}";

        /// <summary>"a 49 day cycle", "an 82 day cycle", "an 11 week cycle": the article for a number said aloud.</summary>
        internal static string A(double v)
        {
            string whole = Math.Floor(Math.Abs(v)).ToString(CultureInfo.InvariantCulture);
            bool vowel = whole.StartsWith('8')
                      || (whole.Length % 3 == 2 && (whole.StartsWith("11", StringComparison.Ordinal) || whole.StartsWith("18", StringComparison.Ordinal)));
            return vowel ? "an" : "a";
        }

        private static string Price(double v) => SpeechPriceFormatter.FormatPrice(v);

        /// <summary>
        /// What each component says on a bar where it has a value: the value, and what it means
        /// from its companions. Navigation (arrows, Ctrl+Left/Right) reads this.
        /// </summary>
        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            double At(string name) => allComponentData.TryGetValue(name, out var a) && dataIndex >= 0 && dataIndex < a.Length ? a[dataIndex] : double.NaN;
            string tier = TierWord(At(EventsTierKey));
            bool inBars = At(InBarsKey) == 1;
            var (unit, units) = AgoUnit(At(EventsTierKey), inBars);
            var (dayWord, dayWords) = Words(inBars, "day", "days");
            var (weekWord, weekWords) = Words(inBars, "week", "weeks");
            string Tier(string t) => char.ToUpperInvariant(t[0]) + t[1..];

            switch (componentName)
            {
                case CompDcl: return LowWords("Daily", value, At(CompDcl + LengthSuffix), dayWord, At(CompDcl + AfterSuffix), dayWord, dayWords);
                case CompWcl: return LowWords("Weekly", value, At(CompWcl + LengthSuffix), weekWord, At(CompWcl + AfterSuffix), weekWord, weekWords);
                case CompYcl: return LowWords("Yearly", value, At(CompYcl + LengthSuffix), inBars ? "bar" : "year", At(CompYcl + AfterSuffix), weekWord, weekWords);

                case CompDclConfirmed: case CompWclConfirmed: case CompYclConfirmed:
                {
                    string t = componentName == CompDclConfirmed ? "daily" : componentName == CompWclConfirmed ? "weekly" : "yearly";
                    var (u, us) = componentName == CompDclConfirmed ? (dayWord, dayWords) : (weekWord, weekWords);
                    string s = $"{Tier(t)} cycle low confirmed, {ConfluenceWord(value)} confluence";
                    double lowPrice = At(componentName + LowSuffix), age = At(componentName + AgoSuffix);
                    if (!double.IsNaN(lowPrice))
                        s += $": the low of {Price(lowPrice)}" + (double.IsNaN(age) ? "" : $", {Span(age, u, us)} ago");
                    s += ".";
                    double replaced = At(componentName + ReplacedSuffix);
                    if (!double.IsNaN(replaced)) s += $" It replaces the cancelled low of {Price(replaced)}.";
                    foreach (var (other, ot) in new[] { (CompDclConfirmed, "daily"), (CompWclConfirmed, "weekly"), (CompYclConfirmed, "yearly") })
                        if (other != componentName && !double.IsNaN(At(other))) s += $" The {ot} low is confirmed on this bar too.";
                    return s;
                }

                case CompPending:
                {
                    string s = $"{Tier(tier)} cycle low pending, the cycle line reached oversold";
                    double count = tier == "daily" ? At(CompDailyWindow) : tier == "weekly" ? At(CompWeeklyWindow) : double.NaN;
                    string countUnit = tier == "weekly" ? weekWord : dayWord;
                    if (!double.IsNaN(count)) s += $" on {countUnit} {Num(count)}";
                    s += ".";
                    double lowest = At(CompPending + LowestSuffix), lowestAgo = At(CompPending + LowestAgoSuffix);
                    if (!double.IsNaN(lowest))
                        s += $" Lowest low since the last cycle low: {Price(lowest)}" +
                             (double.IsNaN(lowestAgo) ? "." : lowestAgo == 0 ? ", this bar." : $", {Span(lowestAgo, unit, units)} ago.");
                    return s;
                }

                case CompCancelled: case CompFailed:
                {
                    double age = At(componentName + AgoSuffix);
                    string from = double.IsNaN(age) ? "." : $" from {Span(age, unit, units)} ago.";
                    return componentName == CompCancelled
                        ? $"{Tier(tier)} pending cycle low cancelled: a low of {Price(bar.Low)}, under the pending low of {Price(value)}" + from
                        : $"{Tier(tier)} cycle failed: closed at {Price(bar.Close)}, under the cycle low of {Price(value)}" + from;
                }

                case CompLeftTop: case CompMidTop: case CompRightTop:
                {
                    string what = componentName == CompLeftTop ? "Left translated top, bearish" : componentName == CompRightTop ? "Right translated top, bullish" : "Mid translated top";
                    string topUnit = inBars ? "bar" : tier == "weekly" ? "week" : tier == "yearly" ? "year" : "day";
                    double dayOf = At(componentName + DaySuffix), len = At(componentName + LengthSuffix);
                    string s = $"{what}: high {Price(value)}";
                    if (!double.IsNaN(dayOf) && !double.IsNaN(len)) s += $", {topUnit} {Num(dayOf)} of {A(len)} {Num(len)} {topUnit} cycle";
                    return s + ".";
                }

                case CompDailyWindow:
                    return WindowWords("daily cycle", dayWord, dayWords, value, At(CompDailyWindow + StartSuffix), At(CompDailyWindow + EndSuffix));
                case CompWeeklyWindow:
                    return WindowWords("weekly cycle", weekWord, weekWords, value, At(CompWeeklyWindow + StartSuffix), At(CompWeeklyWindow + EndSuffix));
                default:
                    return null;
            }
        }

        /// <summary>"Daily cycle low, 61,234. A 49 day cycle. Confirmed 9 days later."</summary>
        private static string LowWords(string tier, double price, double length, string lengthUnit, double after, string afterUnit, string afterUnits)
        {
            string s = $"{tier} cycle low, {Price(price)}.";
            if (!double.IsNaN(length)) s += $" {(A(length) == "an" ? "An" : "A")} {Num(length)} {lengthUnit} cycle.";
            if (!double.IsNaN(after)) s += $" Confirmed {Span(after, afterUnit, afterUnits)} later.";
            return s;
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> results,
            int index, Dictionary<string, object> parameters)
        {
            if (!code.Equals(Code, StringComparison.OrdinalIgnoreCase) || index < 0) return string.Empty;
            DateTime? date = index < data.Length ? data[index].Date : null;
            var words = StateWords(name => results.TryGetValue(name, out var a) ? a : null, index, date);
            if (words.Count == 0) return "Calculating...";
            return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w[1..] + "."));
        }

        // ── Parameters ─────────────────────────────────────────────────────────

        private static void Copy(double[] from, double[] to) => Array.Copy(from, to, Math.Min(from.Length, to.Length));

        private static string? Str(Dictionary<string, object> p, string k) => p.TryGetValue(k, out var v) ? v?.ToString() : null;

        private static double Dbl(Dictionary<string, object> p, string k, double def)
        {
            if (!p.TryGetValue(k, out var v) || v == null) return def;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return def; }
        }

        private static int Int(Dictionary<string, object> p, string k, int def) => (int)Math.Round(Dbl(p, k, def));

        /// <summary>A range setting: 0 (or nonsense) keeps the preset's value.</summary>
        private static double Override(Dictionary<string, object> p, string k, double preset)
        {
            double v = Dbl(p, k, 0);
            return v > 0 && !double.IsNaN(v) ? v : preset;
        }
    }
}
