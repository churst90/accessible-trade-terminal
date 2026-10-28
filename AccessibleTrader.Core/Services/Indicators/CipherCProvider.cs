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
    /// <item>Redrawn as the original draws, in its colours (2026-10-28, read from its drawings
    /// on its own charts; Cody: <i>"Remove our visuals and replace with the original"</i>):</item>
    /// <item>A green triangle over a green disc with a D cut out under each daily cycle low, a blue
    /// one with a W under it at a weekly low, a gold one with a Y under that at a yearly low, and
    /// a faint dotted line through the chart at each, in the tier's colour. Drawn on the LOW's
    /// bar, so they are known only once confirmed (look-ahead, like CF's).</item>
    /// <item>The cycle's length beside each daily and weekly low, with CF's unit suffix: 49D for
    /// days and 24W for weeks, counted inclusively as CF does. The suffix is printed, not spoken.</item>
    /// <item>Under the daily low's D: a check box once it is confirmed, with a refresh sign over
    /// it when it replaced a cancelled low; on a cancelled low, still drawn, a red cross; on the
    /// low being held now, a clock, the refresh sign if one was cancelled since the last
    /// confirmed low, and its grade (LOW, MID, HIGH); and Start, on an orange plate, under the
    /// first low. On the bar a daily low is confirmed, a check over a green down triangle at the
    /// bar's high. The weekly and yearly confirmations, the pending, cancelled and failed events
    /// are heard but not drawn: the original draws none of them (each can be drawn from
    /// Appearance).</item>
    /// <item>Over each finished cycle's top, its letter in a disc over a down triangle: L in red
    /// when it came before the cycle's midpoint (left translated, bearish), M in orange exactly
    /// on it, R in green after it (right translated, bullish); a dotted line through the chart at
    /// the top, and a faint box over the cycle from low to low, from its lowest low to the top.</item>
    /// <item>The live cycle's timing windows as faint bands from their first day to their last,
    /// running into the future: daily green, weekly blue, yearly yellow (the daily and weekly
    /// bands as the original's; the yearly band's bounds are ours, the original's read a date
    /// table). A tint inside the window is ours,
    /// off by default.</item>
    /// <item>CF's yellow wave, a level wave under the candles with a trough under each low of the
    /// shortest running tier, over its last 21 cycles, drawn as that tier's timing window's
    /// picture (<see cref="ComponentCompanions.Path"/>). Look-ahead: drawn between known lows,
    /// and the live hump runs on to the last bar. Its height and level are ours.</item>
    /// <item>The 10-bar simple moving average Camel trades against (CF's yellow line).</item>
    /// </list>
    ///
    /// <para>
    /// <b>Every component holds something to hear (2026-10-22).</b> Cody: <i>"Each component must
    /// hold value, simply hiding and muting it isn't what I want. hiding and muting should be a
    /// user defined preferential function ... a component which is only visual isn't any use to a
    /// screen reader."</i> Twenty-eight components became fifteen (sixteen with the yearly window, 2026-10-28). Nothing is hidden or muted by
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
        public const string CompYearlyWindow  = "Yearly Timing Window";

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
        /// <summary>
        /// A held daily low, per bar while it is held: the original's LOW/MID/HIGH tag on its pending
        /// label, 0 low, 1 mid, 2 high. A point for the Awesome Oscillator (hl2, 5 and 34) above zero
        /// and one for the close above the month's VWAP (hlc3, anchored at each calendar month); a
        /// chart with no volume gets no VWAP point.
        /// </summary>
        public const string GradeSuffix = "_grade";
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
        /// <summary>1 when the count was picked in the settings on a symbol with no tuned count, 2 on one with its own, 0 when the symbol chose it.</summary>
        public const string ChosenKey = "__presetChosen";
        /// <summary>1 when the chart is not daily and the counts run in its bars, as the original runs them there; 0 on a daily chart.</summary>
        public const string InBarsKey = "__in_bars";
        /// <summary>
        /// The original table's unconfirmed counts, per bar. Daily: whole days (bars, off a daily
        /// chart) since the daily low being held; after a drop, still since the dropped low until
        /// the next is named; -1 from a confirmation until then (the original's check). Weekly:
        /// whole weeks since the newest weekly low. NaN before there is one.
        /// </summary>
        public const string HeldDaysKey = "__held_days", HeldWeeksKey = "__held_weeks";

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
        public const string ParamFasterConfirmation = "FasterConfirmation";
        public const string ParamSmaSource  = "SmaSource";
        public const string ParamSmaOffset  = "SmaOffset";

        // The original's display switches (its inputs, read 2026-10-28), each with its default.
        // Pictures only (ApplyDisplay): what a switch turns off is still found, heard, spoken and
        // a stop; Properties → Include leaves a part out altogether.
        public const string ParamTable = "Table", ParamTableDetection = "TableDetection", ParamTableAssets = "TableAssetList",
            ParamTableWindows = "TableWindows", ParamTableCount = "TableCycleCount";
        public const string ParamDailyMarks = "DailyLowMarks", ParamDailyLines = "DailyLowLines", ParamGrade = "HeldLowGrade";
        public const string ParamWeeklyMarks = "WeeklyLowMarks", ParamWeeklyLines = "WeeklyLowLines";
        public const string ParamYearlyMarks = "YearlyLowMarks", ParamYearlyLines = "YearlyLowLines";
        public const string ParamCountNumbers = "CountNumbers", ParamDailyNumbers = "DailyCountNumbers", ParamWeeklyNumbers = "WeeklyCountNumbers";
        public const string ParamWave = "CycleWave";
        public const string ParamDailyBand = "DailyBand", ParamWeeklyBand = "WeeklyBand", ParamYearlyBand = "YearlyBand";
        public const string ParamTops = "TranslatedTops", ParamTopLines = "TopLines", ParamTopBoxes = "CycleRangeBoxes";
        public const string ParamCancelledLows = "CancelledDailyLows", ParamUpdatedSign = "UpdatedSign",
            ParamUnconfirmedLow = "UnconfirmedDailyLow", ParamConfirmedSign = "ConfirmedSign", ParamConfirmationMark = "ConfirmationMark";

        /// <summary>
        /// The original's display switches, in its order, with its defaults: the name, what
        /// Properties calls it, whether it starts on, and what it shows. Cancelled daily lows and
        /// the confirmation mark start off, as they do in the original (none of its default
        /// drawings on sixteen assets has either, 2026-10-28).
        /// </summary>
        internal static readonly (string Name, string Display, bool On, string Description)[] Switches =
        {
            (ParamTable, "Table", true, "The table in the price pane's bottom right corner."),
            (ParamTableDetection, "Table: asset detection", true, "The table's first column: whether the symbol has a tuned count, which count is used, whether the chart is daily, and the asset list."),
            (ParamTableAssets, "Table: asset list", true, "The table's list of the symbols with a tuned count."),
            (ParamTableWindows, "Table: cycle low windows", true, "The table's days and weeks since the last confirmed and unconfirmed lows."),
            (ParamTableCount, "Table: asset cycle count", true, "The table's daily, weekly and yearly cycle ranges."),
            (ParamDailyMarks, "Daily lows: marks", true, "The triangle, D and signs under each daily low. Off, the lows are still heard."),
            (ParamDailyLines, "Daily lows: lines", true, "The faint dotted line through the chart at each daily low."),
            (ParamGrade, "Daily lows: confluence", true, "The grade (LOW, MID, HIGH) under the unconfirmed daily low."),
            (ParamCountNumbers, "Cycle count numbers", true, "Each low's cycle length beside its mark (49D, 24W)."),
            (ParamDailyNumbers, "Cycle count numbers: daily", true, "The daily lows' lengths."),
            (ParamWeeklyNumbers, "Cycle count numbers: weekly", true, "The weekly lows' lengths."),
            (ParamWave, "Cycle wave", true, "The wave under the candles from low to low."),
            (ParamDailyBand, "Daily cycle low range", true, "The band over the live daily cycle's timing window."),
            (ParamWeeklyLines, "Weekly lows: lines", true, "The faint dotted line through the chart at each weekly low."),
            (ParamWeeklyMarks, "Weekly lows: marks", true, "The triangle and W under each weekly low."),
            (ParamWeeklyBand, "Weekly cycle low range", true, "The band over the live weekly cycle's timing window."),
            (ParamYearlyMarks, "Yearly lows: marks", true, "The triangle and Y under each yearly low."),
            (ParamYearlyLines, "Yearly lows: lines", true, "The faint dotted line through the chart at each yearly low."),
            (ParamYearlyBand, "Yearly cycle low range", true, "The band over the live yearly cycle's timing window."),
            (ParamTops, "Translated tops", true, "The lettered tops (L, M, R) over each finished cycle."),
            (ParamTopLines, "Translated tops: lines", true, "The dotted line through the chart at each top."),
            (ParamTopBoxes, "Translated tops: cycle range box", true, "The faint box over each cycle, low to low."),
            (ParamCancelledLows, "Cancelled daily lows", false, "Off by default. On, a daily low undercut before it was confirmed keeps its mark, with a red cross."),
            (ParamUpdatedSign, "Updated daily lows", true, "The refresh sign on a low named after a cancelled one."),
            (ParamUnconfirmedLow, "Unconfirmed daily low", true, "The low being held, with a clock, before it is confirmed."),
            (ParamConfirmedSign, "Confirmed daily lows", true, "The check under each confirmed daily low."),
            (ParamConfirmationMark, "Confirmation mark", false, "Off by default. On, a check over a down triangle on the bar each daily low is confirmed on."),
        };

        public const int DefaultSma = 10;
        public const double DefaultOverbought = 75, DefaultOversold = 10;

        // The original's own colours, read from its drawings (2026-10-28; TradingView keeps them as
        // 0xAABBGGRR): its daily, weekly and yearly marks, its three tops, and its wave and moving
        // average at 0xCC (the average's default colour and width 3 are its inputs').
        internal const string DclHex = "#4CAF50", WclHex = "#2962FF", YclHex = "#D5C110", SmaHex = "#CCFDD835", ArcHex = "#CCFDD835";
        internal const string LeftTopHex = "#F23645", MidTopHex = "#FF9800", RightTopHex = "#4CAF50", StartHex = "#FF9800";
        /// <summary>The live window bands: daily green, weekly blue, yearly yellow.</summary>
        internal const string DailyBandHex = "#4CAF50", WeeklyBandHex = "#2962FF", YearlyBandHex = "#FDD835";
        /// <summary>The original's dotted lines and bands are at alpha 51, its top boxes at 25.</summary>
        internal const int LineAlpha = 51, BandAlpha = 51, TopBoxAlpha = 25;
        /// <summary>The words printed under the daily low's signs, by <see cref="ComponentCompanions.BadgeWord"/>: a held low's grade, and the first low's Start plate.</summary>
        internal const string DailyWords = "LOW|MID|HIGH|Start@" + StartHex;
        internal const double StartWord = 3;

        private static IndicatorComponentMetadata Low(string name, string hex, float size, string tier, string markerText, string patch, double freq, int order,
            string confirmedBy, string? stacksUnder = null, string? words = null) => new()
        {
            Name = name, DisplayName = name, DisplayType = ComponentDisplayType.TriangleUpRing, MarkerText = markerText, Role = ComponentRole.Signal,
            Causality = ComponentCausality.Lookahead, ConfirmedBy = confirmedBy, DefaultMarkerAnchor = MarkerAnchor.BelowBar,
            // The original's low is a label pointing up at the bar's low, touching it.
            MarkerTouchesBar = true,
            DefaultColorHex = hex, DefaultThickness = size, DefaultDrawOrder = order,
            // The original's dotted line through the chart at each low, faint, in the tier's colour.
            MarkLineAlpha = LineAlpha, MarkerStacksUnder = stacksUnder, BadgeWords = words,
            DefaultEnvelopeType = "Ping", DefaultPlaybackLayer = PlaybackLayer.Midground,
            DefaultSoundPatchId = patch, DefaultBaseFrequency = freq, DefaultDecayMs = 400,
            DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
            DefaultSignalSpeechTemplate = $"{tier} cycle low.",
        };

        // No {price} in these templates: narration fills it with the bar's close, but the
        // cross-series signal list on the candles fills it with the component's own value — the
        // low's price for a cancellation, the confluence score for a confirmation.
        private static IndicatorComponentMetadata Event(string name, ComponentDisplayType shape, MarkerAnchor anchor, string hex,
            float size, string patch, double freq, PlaybackLayer layer, string speech, bool count = false, bool drawn = true, bool touches = false) => new()
        {
            Name = name, DisplayName = name, DisplayType = shape, Role = ComponentRole.Signal, IsCount = count, DefaultIsDrawn = drawn, MarkerTouchesBar = touches,
            Causality = ComponentCausality.Causal, DefaultMarkerAnchor = anchor,
            DefaultColorHex = hex, DefaultThickness = size,
            DefaultEnvelopeType = "Ping", DefaultPlaybackLayer = layer,
            DefaultSoundPatchId = patch, DefaultBaseFrequency = freq, DefaultDecayMs = 300,
            DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
            DefaultSignalSpeechTemplate = speech,
        };

        /// <summary>
        /// A translated top, as the original draws it: its letter (L, M, R) in a disc over a down
        /// triangle at the top's high, a dotted line through the chart at the top, and a faint box
        /// over the cycle, from low to low and from the cycle's lowest low to the top
        /// (<see cref="ComponentCompanions.BoxEnd"/>), all in the top's colour.
        /// </summary>
        private static IndicatorComponentMetadata Top(string name, string hex, string letter, string speech) => new()
        {
            Name = name, DisplayName = name, DisplayType = ComponentDisplayType.TriangleDown, Role = ComponentRole.Signal,
            Causality = ComponentCausality.Lookahead, DefaultMarkerAnchor = MarkerAnchor.AboveBar, MarkerText = letter,
            DefaultColorHex = hex, DefaultThickness = 3f, BoxAlpha = TopBoxAlpha, MarkLineAlpha = 255,
            DefaultEnvelopeType = "Ping", DefaultPlaybackLayer = PlaybackLayer.Background,
            DefaultSoundPatchId = "triangle_bell", DefaultBaseFrequency = 520, DefaultDecayMs = 200,
            DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
            DefaultSignalSpeechTemplate = speech,
        };

        /// <summary>
        /// A timing window (<see cref="ComponentDisplayType.Window"/>): its value is the cycle's
        /// day (or week, or year) count on every bar, which is what is heard; Ctrl+Left/Right stops
        /// where the window opens and where it closes with no new low
        /// (<see cref="ComponentCompanions.Stops"/>). The count is never drawn: it is not a price.
        /// Drawn as the original draws it: the live cycle's window as a faint band from its first
        /// day to its last (<see cref="ComponentCompanions.BoxEnd"/> on the last bar, running into
        /// the future), in the tier's colour. The lowest running tier's window also carries the
        /// cycle wave (<see cref="ComponentCompanions.Path"/>), drawn in the original's yellow. A
        /// tint inside the window (<see cref="ComponentCompanions.Tint"/>, 1 inside, 0 outside) is
        /// ours, not the original's: off by default, a choice in Appearance.
        /// </summary>
        private static IndicatorComponentMetadata Window(string name, string bandHex, string tintHex, string openSpeech) => new()
        {
            Name = name, DisplayName = name, DisplayType = ComponentDisplayType.Window, Role = ComponentRole.Signal,
            Causality = ComponentCausality.Causal, IsCount = true, DefaultColorHex = bandHex, DefaultThickness = 3f,
            BoxAlpha = BandAlpha, PathName = "cycle wave", PathColorHex = ArcHex,
            // New-bar narration says this on the bar the window opens ({value} = the count).
            DefaultSignalSpeechTemplate = openSpeech,
            DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
            DefaultPlaybackLayer = PlaybackLayer.Background,
            // When switched on, the whole 0x18 alpha at 1, nothing at 0.
            DefaultBackgroundTint = BackgroundTint.Off, DefaultTintColorAboveHex = tintHex, DefaultTintColorBelowHex = "#00000000",
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
                    "Daily, weekly and yearly cycle lows on the price chart. Green triangles over a D under daily cycle lows, " +
                    "blue with a W under weekly, gold with a Y under yearly, a dotted line at each, each low's length beside " +
                    "it (49D, 24W), a check under a confirmed daily low, a cross under a cancelled one, a clock and the " +
                    "grade (LOW, MID, HIGH) under the low being held, a check over the bar a low is confirmed on, " +
                    "lettered translated tops (L, M, R) with a box over each cycle, bands over the live timing windows, " +
                    "whose counts are read on every bar, and a wave under the candles from low to low. Cycle ranges are tuned " +
                    "for sixteen assets (the S&P, Nasdaq, Dow, dollar index, gold, silver, oil, gold and silver " +
                    "miners, Bitcoin, Ethereum and four crypto stocks). Lows are drawn on the low's bar but known " +
                    "only when confirmed, after the low has held for half the shortest cycle. A map of cycle " +
                    "timing, not an edge.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamPreset, DisplayName = "Asset preset", DataType = typeof(string), DefaultValue = "Auto",
                            Description = $"Whose cycle ranges to use. Auto reads the chart's symbol (SPX, US500 and ^GSPC are the S&P; XAUUSD is gold; a fund or future such as SPY, ES or GLD has none and takes the default count). Or type one of: {PresetKeys}." },
                    new() { IsAdvanced = true, Name = ParamDailyMin, DisplayName = "Daily cycle shortest (days)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 1,
                            Description = "0 = the preset's. In the chart's own days: calendar days for crypto, trading days for stocks. Off a daily chart, in its bars." },
                    new() { IsAdvanced = true, Name = ParamDailyMax, DisplayName = "Daily cycle longest (days)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 1,
                            Description = "0 = the preset's." },
                    new() { IsAdvanced = true, Name = ParamWeeklyMin, DisplayName = "Weekly cycle shortest (weeks)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 1,
                            Description = "0 = the preset's." },
                    new() { IsAdvanced = true, Name = ParamWeeklyMax, DisplayName = "Weekly cycle longest (weeks)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 1,
                            Description = "0 = the preset's." },
                    new() { IsAdvanced = true, Name = ParamYearlyMin, DisplayName = "Yearly cycle shortest (years)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 0.5,
                            Description = "0 = the preset's." },
                    new() { IsAdvanced = true, Name = ParamYearlyMax, DisplayName = "Yearly cycle longest (years)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 0.5,
                            Description = "0 = the preset's." },
                    new() { Name = ParamSma, DisplayName = "Moving average length", DataType = typeof(int), DefaultValue = (double)DefaultSma, MinValue = 2, Step = 1,
                            Description = "The simple moving average drawn on price, 10 by default. It is left out until included, as the original's is off." },
                    new() { Name = ParamSmaSource, DisplayName = "Moving average source", DataType = typeof(string), DefaultValue = "close",
                            Description = "What it averages: open, high, low, close, hl2, hlc3, hlcc4, ohlc4 or volume. Close by default." },
                    new() { Name = ParamSmaOffset, DisplayName = "Moving average offset", DataType = typeof(int), DefaultValue = 0.0, Step = 1,
                            Description = "Bars to shift the line by, right if above 0. 0 by default." },
                    new() { Name = ParamOverbought, DisplayName = "Confirm level", DataType = typeof(double), DefaultValue = DefaultOverbought, MinValue = 1, MaxValue = 99, Step = 1,
                            Description = "75 by default. A daily low is named only while the hidden stochastic is below this. On a weekly chart's own weekly tier and the yearly tier, a low is confirmed when the hidden cycle line climbs through it after reaching the arm level." },
                    new() { Name = ParamOversold, DisplayName = "Arm level", DataType = typeof(double), DefaultValue = DefaultOversold, MinValue = 0, MaxValue = 99, Step = 1,
                            Description = "10 by default. A daily low is named only while the hidden stochastic is above this. On a weekly chart's own weekly tier and the yearly tier, the hidden cycle line must reach it before the next low can be confirmed." },
                    new() { Name = ParamFasterConfirmation, DisplayName = "Faster confirmation", DataType = typeof(bool), DefaultValue = false,
                            Description = "Off by default. On, a daily low is also named when price breaks the last swing high, as it always is on gold, silver, the miners and the dollar, so lows are named sooner and some cycles end on a different low." },
                }.Concat(Switches.Select(w => new IndicatorParameterMetadata
                {
                    Name = w.Name, DisplayName = w.Display, DataType = typeof(bool), DefaultValue = w.On, Description = w.Description,
                })).ToList(),
                Components = new List<IndicatorComponentMetadata>
                {
                    // Left out until included: the original's moving average is off by default.
                    new() { Name = CompSma, DisplayName = CompSma, DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Median,
                            Causality = ComponentCausality.Causal, DefaultColorHex = SmaHex, DefaultThickness = 3f, DefaultIsEnabled = false,
                            DefaultUsePolarityColoring = false, DefaultIsAreaFill = false, DefaultPlaybackLayer = PlaybackLayer.Background },

                    // Biggest first, so the daily triangle is drawn over the weekly over the yearly;
                    // on a bar with more than one, the weekly mark sits under the daily one and the
                    // yearly under the weekly, as the original stacks them.
                    Low(CompYcl, YclHex, 6f,   "Yearly", "Y", "dual_tone_bell", 196, 0, CompYclConfirmed, stacksUnder: CompWcl),
                    Low(CompWcl, WclHex, 4.5f, "Weekly", "W", "dual_tone_bell", 262, 1, CompWclConfirmed, stacksUnder: CompDcl),
                    Low(CompDcl, DclHex, 3f,   "Daily",  "D", "sine_bell",      330, 2, CompDclConfirmed, words: DailyWords),

                    // Value: the confluence score, 1 to 3 (a normal-length cycle, a close over the
                    // moving average, a close over the low bar's high), so a strategy can ask for
                    // "DCL Confirmed at least 2". The daily one is drawn as the original draws it, a
                    // check over a down triangle pointing at the bar's high, touching it; the weekly and yearly ones, and
                    // the events below, are heard, not drawn (the original draws none of them),
                    // and can be drawn from Appearance.
                    Event(CompDclConfirmed, ComponentDisplayType.TriangleDown, MarkerAnchor.AboveBar, DclHex, 3f, "sine_bell", 392, PlaybackLayer.Foreground,
                          "Daily cycle low confirmed.", count: true, touches: true),
                    Event(CompWclConfirmed, ComponentDisplayType.CheckBox, MarkerAnchor.AboveBar, DclHex, 5.5f, "dual_tone_bell", 294, PlaybackLayer.Foreground,
                          "Weekly cycle low confirmed.", count: true, drawn: false),
                    Event(CompYclConfirmed, ComponentDisplayType.CheckBox, MarkerAnchor.AboveBar, DclHex, 6.5f, "dual_tone_bell", 220, PlaybackLayer.Foreground,
                          "Yearly cycle low confirmed.", count: true, drawn: false),
                    // Value: the close. The bar the daily tier names a low on (with no daily tier,
                    // the bar the hidden cycle line reached oversold). On the chart the daily low
                    // being held is drawn as a low with a clock and its grade (the DCL's mark).
                    Event(CompPending, ComponentDisplayType.RefreshBox, MarkerAnchor.AboveBar, "#78909C", 4f, "triangle_bell", 440, PlaybackLayer.Background,
                          "Cycle low pending.", drawn: false),
                    // Value: the price of the low closed under. Warnings, not sell signals. On the
                    // chart the cancelled daily low keeps a low's mark with a red cross under it.
                    Event(CompCancelled, ComponentDisplayType.Cross, MarkerAnchor.BelowBar, "#FF1744", 3f, "triangle_bell", 220, PlaybackLayer.Midground,
                          "Pending cycle low cancelled, a bar went under it before it was due.", drawn: false),
                    Event(CompFailed, ComponentDisplayType.Cross, MarkerAnchor.AboveBar, "#FF6D00", 3f, "triangle_bell", 175, PlaybackLayer.Midground,
                          "Cycle failed, price closed under the cycle low.", drawn: false),

                    // Value: the top's high.
                    Top(CompLeftTop,  LeftTopHex,  "L", "Left translated top, bearish."),
                    Top(CompMidTop,   MidTopHex,   "M", "Mid translated top."),
                    Top(CompRightTop, RightTopHex, "R", "Right translated top, bullish."),

                    Window(CompDailyWindow, DailyBandHex, "#184CAF50", "Daily timing window open, {value} days since the last daily low."),
                    Window(CompWeeklyWindow, WeeklyBandHex, "#182962FF", "Weekly timing window open, {value} weeks since the last weekly low."),
                    Window(CompYearlyWindow, YearlyBandHex, "#18FDD835", "Yearly timing window open, {value} years since the last yearly low."),
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
                                      CompDailyWindow, CompWeeklyWindow, CompYearlyWindow })
                A(c);
            // The tint companions always exist, so a window with no tint of its own paints nothing
            // rather than falling back to tinting by its day count; so do the pictures' companions.
            foreach (var c in new[] { CompDailyWindow, CompWeeklyWindow, CompYearlyWindow })
                foreach (var k in new[] { ComponentCompanions.Tint, ComponentCompanions.Path, ComponentCompanions.BoxStart, ComponentCompanions.BoxEnd })
                    A(c + k);
            foreach (var c in new[] { CompLeftTop, CompMidTop, CompRightTop })
                foreach (var k in new[] { ComponentCompanions.BoxStart, ComponentCompanions.BoxEnd, ComponentCompanions.BoxTop, ComponentCompanions.BoxBottom })
                    A(c + k);
            foreach (var k in new[] { ComponentCompanions.Badge, ComponentCompanions.BadgeWord, ComponentCompanions.Mark }) A(CompDcl + k);
            A(CompDclConfirmed + ComponentCompanions.Badge);
            // What the display switches hide (ApplyDisplay), whichever are on.
            foreach (var c in new[] { CompDcl, CompWcl, CompYcl, CompDclConfirmed, CompLeftTop, CompMidTop, CompRightTop })
                A(c + ComponentCompanions.Hide);
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
            // The original's "Faster Confirmation" (read off its drawings on the Dow and Bitcoin,
            // 2026-10-27) is the swing-high break switched on for an asset that lacks it.
            bool pivotBreak = preset.PivotBreak || IndicatorParams.GetBool(p, ParamFasterConfirmation, false);
            // The count starts on the original's Start bar, as a low confirmed one hold later.
            var own = CipherCEngine.PresetForSymbol(Str(p, "__symbol"));
            int start = CipherCEngine.StartBar(data, own);
            var d = CipherCEngine.DetectDaily(data, dTier, preset.DssLength, preset.DssSmooth, ob, os, pivotBreak, start,
                preset.DropUntilDue, seedConfirmed: true);
            // With a daily tier the weekly lows are picked from the daily ones, as the original
            // picks them, from the same Start; on a weekly chart the weekly tier finds its own.
            bool weeklyFromDaily = dTier.Active && wTier.Active;
            var w = !weeklyFromDaily ? CipherCEngine.Detect(data, wTier, ob, os)
                : preset.WeeklyByWeeks ? CipherCEngine.WeeklyByWeeks(d, data,
                    Math.Max(0, wMax - CipherCEngine.WeeksShortOfMax) * barsPerWeek,
                    Math.Max(0, wMax - CipherCEngine.DoubleWeeksShortOfMax) * barsPerWeek, preset.DoubleUnderNewest, d.Start)
                : CipherCEngine.WeeklyFromDaily(d, data, dTier.MinBars, preset.WeeklyDoubles, d.Start);
            // On a symbol the original tunes, its yearly lows are its table of dates (the symbol's,
            // whatever count is chosen), unless the yearly range is set by hand; elsewhere they are
            // found from the price.
            var yDates = own?.YearlyLows is { Count: > 0 } table && Dbl(p, ParamYearlyMin, 0) <= 0 && Dbl(p, ParamYearlyMax, 0) <= 0 ? table : null;
            var y = yDates != null && yTier.Active ? CipherCEngine.YearlyFromDates(data, yDates) : CipherCEngine.Detect(data, yTier, ob, os);
            // Off daily a week of the count is five or seven bars, not a week, so its lengths,
            // ages and windows are said in bars too.
            var day = inBars ? new Unit("daily", "bar", "bars", 1) : new Unit("daily", "day", "days", clock.BarsPerDay);
            var week = inBars ? new Unit("weekly", "bar", "bars", 1) : new Unit("weekly", "week", "weeks", barsPerWeek);
            var year = inBars ? new Unit("yearly", "bar", "bars", 1) : new Unit("yearly", "year", "years", clock.BarsPerDay * clock.DaysPerYear);
            double weeksInUnits = inBars ? clock.DaysPerWeek : 1;

            var close = new double[n]; var high = new double[n]; var low = new double[n];
            for (int i = 0; i < n; i++) { close[i] = data[i].Close; high[i] = data[i].High; low[i] = data[i].Low; }

            // The moving average, of its source, shifted by its offset as the original's can be.
            // The confluence grade's close-over-average test reads it unshifted, of the close.
            int smaLen = Math.Max(2, Int(p, ParamSma, DefaultSma));
            var sma = CipherCEngine.Nan(n);
            Average(close, smaLen, sma);
            var src = Source(data, Str(p, ParamSmaSource));
            var shown = ReferenceEquals(src, null) ? sma : Average(src, smaLen, CipherCEngine.Nan(n));
            int offset = Int(p, ParamSmaOffset, 0);
            for (int i = 0; i < n; i++)
            {
                int from = i - offset;
                o[CompSma][i] = from >= 0 && from < n ? shown[from] : double.NaN;
            }

            // Where the lows are. A weekly low is one of the daily lows near it, a yearly low one
            // of the weekly (or daily) ones — CF's stacks always sit on one another.
            int dReach = (int)Math.Round(dTier.MinBars / 2), wReach = (int)Math.Round(wTier.MinBars / 2);
            var dConfirm = Confirmations(d, null, 0, data);
            var wConfirm = Confirmations(w, dTier.Active && !weeklyFromDaily ? d.Lows : null, dReach, data);
            // A dated yearly low stands on its own bar: the original draws no D or W under most of
            // them (119 of 151 stand alone), so it is neither moved onto a lower tier's low nor
            // added to them.
            var yLower = yDates != null ? null : wTier.Active ? w.Lows : dTier.Active ? d.Lows : null;
            var yConfirm = Confirmations(y, yLower, wTier.Active ? wReach : dReach, data);
            var dLows = new List<int>(d.Lows);
            var wLows = dTier.Active && !weeklyFromDaily ? CipherCEngine.SnapTo(w.Lows, dLows, dReach, data) : new List<int>(w.Lows);
            var wDrawn = new List<int>(wLows);
            var yLows = yDates != null ? new List<int>(y.Lows)
                      : wTier.Active ? CipherCEngine.SnapTo(y.Lows, wLows, wReach, data)
                      : dTier.Active ? CipherCEngine.SnapTo(y.Lows, dLows, dReach, data) : y.Lows;
            // And a higher low with no lower one near it is a lower low as well: a weekly cycle
            // low IS a daily cycle low. Picked from the daily lows, a weekly low is one already,
            // unless the daily tier later cancelled it; a yearly low, or a weekly one found on its
            // own, can miss a daily low the daily detector skipped. CF draws a D under every W; so
            // does this. A low added this way was confirmed by the higher tier, so it takes that
            // tier's confirmation.
            var yUnder = yDates != null ? new List<int>() : yLows;
            if (wTier.Active) foreach (int i in yUnder) { AddLow(wLows, i); wConfirm.TryAdd(i, yConfirm.GetValueOrDefault(i, -1)); }
            if (dTier.Active)
            {
                foreach (int i in wLows) { AddLow(dLows, i); dConfirm.TryAdd(i, wConfirm.GetValueOrDefault(i, -1)); }
                foreach (int i in yUnder) { AddLow(dLows, i); dConfirm.TryAdd(i, yConfirm.GetValueOrDefault(i, -1)); }
            }

            // Each low: its price, its cycle's length (CF's count, drawn as the label beside its
            // mark, daily and weekly as the original prints them; the yearly has none), and how
            // long after it was confirmed.
            WriteLows(o, CompDcl, dLows, dConfirm, low, day, day, label: true, LengthInDays(clock));
            WriteLows(o, CompWcl, wLows, wConfirm, low, week, week, label: true, (a, b) => Math.Round((b - a) / week.Bars));
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
                DrawnAt(y, yDates != null ? null : wTier.Active ? wDrawn : dTier.Active ? d.Lows : null, wTier.Active ? wReach : dReach, data));

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
                // The daily tier names its low on the pending bar: that low, not the lowest since
                // the last one (a later swing low can be higher than an earlier dip it skips).
                int named = lowest.HeldAt[t];
                if (named >= 0 && named < t)
                {
                    pendingLowest[t] = low[named];
                    pendingLowestAgo[t] = Units(t - named, ago);
                    continue;
                }
                int last = lowest.LastLowAt[t];
                if (last < 0) continue;
                int at = last + 1;
                for (int i = last + 1; i <= t; i++) if (low[i] < low[at]) at = i;
                if (at > t) continue;
                pendingLowest[t] = low[at];
                pendingLowestAgo[t] = Units(t - at, ago);
            }

            // The daily tier's held low carries the original's LOW/MID/HIGH tag, bar by bar (it
            // follows the price, not the low), and a pending low named after a cancellation
            // since the last confirmed low is marked as replacing it (the original's 🔄 on the
            // pending label: 880 of 884 such labels, 2026-10-27).
            double[] pendingGrade = A(CompPending + GradeSuffix), pendingReplaced = A(CompPending + ReplacedSuffix);
            var grade = tierNo == 1 ? Grade(data, high, low, close) : null;
            if (grade != null)
            {
                double replacedSince = double.NaN;
                for (int t = 0; t < n; t++)
                {
                    if (!double.IsNaN(lowest.Confirmed[t])) replacedSince = double.NaN;
                    if (!double.IsNaN(lowest.Cancelled[t])) replacedSince = lowest.Cancelled[t];
                    if (lowest.HeldAt[t] >= 0) pendingGrade[t] = grade[t];
                    if (!double.IsNaN(lowest.Pending[t])) pendingReplaced[t] = replacedSince;
                }
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

            // The daily lows' signs, as the original draws them under its D: a check under each
            // confirmed low, with a refresh sign over it when the low replaced a cancelled one; a
            // cancelled low keeps its mark with a cross; the low being held now is marked with a
            // clock, the refresh sign if it replaces a cancelled one, and its grade; and Start
            // under the bar the count begins from, the seed, which is not a low. Pictures: the speech is the confirmations',
            // the pending and the cancelled components'.
            if (grade != null) DailySigns(o, d, dLows, dConfirm, low, grade, n);

            // Each finished cycle's top: where it came in the cycle, and how long the cycle was;
            // and the original's box over the cycle, low to low, its lowest low to the top.
            var length = tierNo == 1 ? LengthInDays(clock) : tierNo == 2 ? (a, b) => Math.Round((b - a) / week.Bars) : (Func<int, int, double>)((a, b) => Math.Round((b - a) / year.Bars, 1));
            for (int k = 1; k < lowestLows.Count; k++)
            {
                int a = lowestLows[k - 1], b = lowestLows[k];
                var (top, comp) = TranslatedTop(high, a, b);
                o[comp][top] = high[top];
                A(comp + DaySuffix)[top] = Math.Floor((top - a) / unit.Bars + 1e-9) + 1;
                A(comp + LengthSuffix)[top] = length(a, b);
                double floor = double.MaxValue;
                for (int i = a; i <= b; i++) floor = Math.Min(floor, low[i]);
                o[comp + ComponentCompanions.BoxStart][top] = a - top;
                o[comp + ComponentCompanions.BoxEnd][top] = b - top;
                o[comp + ComponentCompanions.BoxTop][top] = high[top];
                o[comp + ComponentCompanions.BoxBottom][top] = floor;
            }

            // Day, week and year counts against the timing windows — known on the bar. Only one
            // tint, the shortest tier's, if switched on.
            double yearsInUnits = inBars ? clock.DaysPerYear : 1;
            WriteWindow(o, CompDailyWindow, d, dTier, day.Bars, dWindowMin, dMax, tint: true, n);
            WriteWindow(o, CompWeeklyWindow, w, wTier, week.Bars, wMin * weeksInUnits, wMax * weeksInUnits, tint: !dTier.Active, n);
            WriteWindow(o, CompYearlyWindow, y, yTier, year.Bars, yMin * yearsInUnits, yMax * yearsInUnits, tint: !dTier.Active && !wTier.Active, n);

            // The live cycle's window as the original bands it: from the newest low, the shortest
            // and longest cycle on the calendar. A band starts on the first bar stamped at or after
            // the low bar's time plus the shortest cycle in calendar days (a trading day is 7/5 of
            // one, a week 7 of them), rounded, and ends that many days plus the window's length,
            // rounded, on. The daily band runs from the newest daily low, the weekly from the
            // newest weekly low (a candidate not included). Some presets' bands are not their
            // tables' ranges (the preset's Band ranges), and an untuned symbol's are calendar days
            // on any calendar (CipherCEngine.Fallback). Read from the original's bands on 21 assets,
            // 2026-10-28: 82 of 85 daily ends and 15 of 17 weekly ends dated in the past to the
            // day, the misses where its low is not ours. Past the last bar the bars are the ones the
            // chart would print (CipherCFutureClock: its session's time of day with daylight
            // saving, and the US holidays it skipped last time): 70 of 70 future daily ends and 115
            // of 128 weekly to the bar, against 56 and 37 at the last bar's time on weekdays.
            // The yearly band, on a symbol with the original's dated yearly lows, runs from the
            // preset's YearlyBandFromDays to YearlyBandToDays calendar days after the newest of
            // those dates (DatedBand); elsewhere from yMin to yMax years of 365.25 days after the
            // newest yearly low. Off a daily chart, in bars (not checked against the original).
            bool everyDay = clock.TradesEveryDay;
            double calendar = everyDay ? 1 : 7.0 / 5;
            static double Or(double band, double table) => band > 0 ? band : table;
            double bdMin = Override(p, ParamDailyMin, Or(preset.BandDailyMin, preset.DailyMin) * dayScale);
            double bdMax = Override(p, ParamDailyMax, Or(preset.BandDailyMax, preset.DailyMax) * dayScale);
            double bwMin = Override(p, ParamWeeklyMin, Or(preset.BandWeeklyMin, preset.WeeklyMin));
            double bwMax = Override(p, ParamWeeklyMax, Or(preset.BandWeeklyMax, preset.WeeklyMax));
            WriteBand(o, CompDailyWindow, d, dTier, data, n, inBars, bdMin * (inBars ? 1 : calendar), bdMax * (inBars ? 1 : calendar), everyDay);
            WriteBand(o, CompWeeklyWindow, w, wTier, data, n, inBars, bwMin * (inBars ? clock.DaysPerWeek : 7 * calendar), bwMax * (inBars ? clock.DaysPerWeek : 7 * calendar), everyDay);
            if (yDates != null && own!.YearlyBandToDays > 0)
                DatedBand(o, CompYearlyWindow, yTier, yDates, data, n, inBars, own.YearlyBandFromDays, own.YearlyBandToDays, everyDay);
            else
                WriteBand(o, CompYearlyWindow, y, yTier, data, n, inBars, yMin * (inBars ? clock.DaysPerYear : 365.25), yMax * (inBars ? clock.DaysPerYear : 365.25), everyDay);

            // The cycle wave, on the window of the tier the events belong to. A picture, not a
            // reading: what it shows — how far into the cycle each bar is — the window says as its
            // count. Look-ahead: each hump is drawn between two lows known only once confirmed,
            // and the live one runs on to the last bar.
            DrawArcs(o[(tierNo == 1 ? CompDailyWindow : tierNo == 2 ? CompWeeklyWindow : CompYearlyWindow) + ComponentCompanions.Path],
                lowestLows, lowestTier, lowest.ConfirmedAt, close, high, low, n);

            // The table's unconfirmed counts (HeldDaysKey, HeldWeeksKey).
            double[] heldDays = A(HeldDaysKey), heldWeeks = A(HeldWeeksKey);
            if (dTier.Active)
            {
                int held = -2;   // -2 none yet, -1 confirmed and nothing named since
                for (int t = 0; t < n; t++)
                {
                    if (d.HeldAt[t] >= 0) held = d.HeldAt[t];
                    else if (!double.IsNaN(d.Confirmed[t])) held = -1;
                    if (held >= 0) heldDays[t] = Math.Floor((t - held) / day.Bars + 1e-9);
                    else if (held == -1) heldDays[t] = -1;
                }
            }
            if (wTier.Active)
                for (int t = 0; t < n; t++)
                    if (w.HeldAt[t] >= 0) heldWeeks[t] = Math.Floor((t - w.HeldAt[t]) / week.Bars + 1e-9);

            int presetIndex = IndexOf(preset);
            Array.Fill(A(PresetKey), fineTuned ? presetIndex : -1 - presetIndex);
            Array.Fill(A(ChosenKey), !CipherCEngine.IsChosen(Str(p, ParamPreset)) ? 0 : CipherCEngine.PresetForSymbol(Str(p, "__symbol")) != null ? 2 : 1);
            Array.Fill(A(InBarsKey), inBars ? 1 : 0);
            ApplyDisplay(o, p, n);
            return o;
        }

        /// <summary>A simple moving average of <paramref name="v"/> into <paramref name="into"/>, NaN before it has <paramref name="length"/> bars.</summary>
        private static double[] Average(double[] v, int length, double[] into)
        {
            double sum = 0;
            for (int i = 0; i < v.Length; i++)
            {
                sum += v[i];
                if (i >= length) sum -= v[i - length];
                if (i >= length - 1) into[i] = sum / length;
            }
            return into;
        }

        /// <summary>The moving average's source as the original names them; null for the close (or an unknown name).</summary>
        private static double[]? Source(ReadOnlySpan<Ohlcv> data, string? name)
        {
            Func<Ohlcv, double>? f = (name ?? "close").Trim().ToLowerInvariant() switch
            {
                "open" => b => b.Open, "high" => b => b.High, "low" => b => b.Low,
                "hl2" => b => (b.High + b.Low) / 2, "hlc3" => b => (b.High + b.Low + b.Close) / 3,
                "hlcc4" => b => (b.High + b.Low + 2 * b.Close) / 4, "ohlc4" => b => (b.Open + b.High + b.Low + b.Close) / 4,
                "volume" => b => b.Volume, _ => null,
            };
            if (f == null) return null;
            var a = new double[data.Length];
            for (int i = 0; i < a.Length; i++) a[i] = f(data[i]);
            return a;
        }

        /// <summary>Whether one of the original's display switches (<see cref="Switches"/>) is on.</summary>
        internal static bool SwitchOn(IReadOnlyDictionary<string, object>? p, string name)
        {
            foreach (var w in Switches) if (w.Name == name) return IndicatorParams.GetBool(p, name, w.On);
            return true;
        }

        /// <summary>
        /// The original's display switches (<see cref="Switches"/>). Pictures only: one switched off
        /// hides its drawing by the part's <see cref="ComponentCompanions.Hide"/> where the bar has a
        /// value, a stop or speech (a low, its signs, its line, a top), and clears a companion that
        /// is only a picture (a length beside a low, a band, a box, the wave). What is found, heard
        /// and said does not change; the table's switches are read by <see cref="TableRows"/>.
        /// </summary>
        private static void ApplyDisplay(Dictionary<string, double[]> o, Dictionary<string, object> p, int n)
        {
            bool On(string name) => SwitchOn(p, name);
            bool Has(string key, int t) => o.TryGetValue(key, out var a) && !double.IsNaN(a[t]);
            int Bits(string key, int t) => Has(key, t) ? (int)o[key][t] : 0;
            void Hide(string comp, Func<int, int> bits)
            {
                var h = o[comp + ComponentCompanions.Hide];
                for (int t = 0; t < n; t++)
                {
                    int b = bits(t);
                    if (b != 0) h[t] = ComponentCompanions.Hidden(h, t) | b;
                }
            }
            void Clear(string key) { if (o.TryGetValue(key, out var a)) Array.Fill(a, double.NaN); }
            const int Marker = ComponentCompanions.HideMarker, Line = ComponentCompanions.HideLine, Shift = ComponentCompanions.HideBadgeShift;
            string badge = CompDcl + ComponentCompanions.Badge, mark = CompDcl + ComponentCompanions.Mark, word = CompDcl + ComponentCompanions.BadgeWord;

            // The daily lows: marks, lines, the held low's grade, and the confirmation signs.
            if (!On(ParamDailyMarks)) Hide(CompDcl, t => Has(CompDcl, t) || Has(mark, t) || Has(word, t) ? Marker : 0);
            if (!On(ParamDailyLines)) Hide(CompDcl, t => Has(CompDcl, t) ? Line : 0);
            if (!On(ParamGrade)) Hide(CompDcl, t => Has(word, t) && o[word][t] != StartWord ? ComponentCompanions.HideWord : 0);
            if (!On(ParamCancelledLows)) Hide(CompDcl, t => !Has(CompDcl, t) && (Bits(badge, t) & ComponentCompanions.BadgeCross) != 0 ? Marker : 0);
            if (!On(ParamUnconfirmedLow)) Hide(CompDcl, t => !Has(CompDcl, t) && (Bits(badge, t) & ComponentCompanions.BadgeClock) != 0 ? Marker : 0);
            if (!On(ParamUpdatedSign)) Hide(CompDcl, t => (Bits(badge, t) & ComponentCompanions.BadgeRefresh) != 0 ? ComponentCompanions.BadgeRefresh << Shift : 0);
            if (!On(ParamConfirmedSign)) Hide(CompDcl, t => (Bits(badge, t) & ComponentCompanions.BadgeCheck) != 0 ? ComponentCompanions.BadgeCheck << Shift : 0);
            if (!On(ParamConfirmationMark)) Hide(CompDclConfirmed, t => Has(CompDclConfirmed, t) ? Marker : 0);

            // The weekly and yearly lows.
            if (!On(ParamWeeklyMarks)) Hide(CompWcl, t => Has(CompWcl, t) ? Marker : 0);
            if (!On(ParamWeeklyLines)) Hide(CompWcl, t => Has(CompWcl, t) ? Line : 0);
            if (!On(ParamYearlyMarks)) Hide(CompYcl, t => Has(CompYcl, t) ? Marker : 0);
            if (!On(ParamYearlyLines)) Hide(CompYcl, t => Has(CompYcl, t) ? Line : 0);

            // The lengths beside the marks, the wave and the bands.
            if (!On(ParamCountNumbers) || !On(ParamDailyNumbers)) Clear(CompDcl + ComponentCompanions.Label);
            if (!On(ParamCountNumbers) || !On(ParamWeeklyNumbers)) Clear(CompWcl + ComponentCompanions.Label);
            foreach (var (window, band) in new[] { (CompDailyWindow, ParamDailyBand), (CompWeeklyWindow, ParamWeeklyBand), (CompYearlyWindow, ParamYearlyBand) })
            {
                if (!On(ParamWave)) Clear(window + ComponentCompanions.Path);
                if (!On(band)) { Clear(window + ComponentCompanions.BoxStart); Clear(window + ComponentCompanions.BoxEnd); }
            }

            // The tops: their marks, lines and boxes.
            foreach (var top in new[] { CompLeftTop, CompMidTop, CompRightTop })
            {
                if (!On(ParamTops)) Hide(top, t => Has(top, t) ? Marker | Line : 0);
                else if (!On(ParamTopLines)) Hide(top, t => Has(top, t) ? Line : 0);
                if (!On(ParamTops) || !On(ParamTopBoxes))
                    foreach (var k in new[] { ComponentCompanions.BoxStart, ComponentCompanions.BoxEnd, ComponentCompanions.BoxTop, ComponentCompanions.BoxBottom })
                        Clear(top + k);
            }
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
                // The window is in the cycle's length, both lows counted (day 1 is the low's own);
                // the count is the original's "days since", 0 on the low's bar (2026-10-28).
                double units = Math.Floor((t - last) / barsPerUnit + 1e-9) + 1;
                count[t] = units - 1;
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
        /// The live cycle's window as a band (<see cref="ComponentCompanions.BoxEnd"/> on the last
        /// bar, in bars from it, running into the future): from the bar <paramref name="from"/>
        /// days after the newest low (<see cref="CipherCEngine.TierRun.NewestAt"/> on a weekly
        /// machine, else the last low), rounded, to the bar that many days plus the window's length
        /// (<paramref name="to"/> less <paramref name="from"/>), rounded, after it, each the first
        /// bar on or after that date (2026-10-28: the original's weekly ends, 115 of 128 future ones
        /// against 101 rounding <paramref name="to"/> down; its daily ends the same either way). Off a daily chart
        /// (<paramref name="inBars"/>) the two are bars from the low.
        /// </summary>
        private static void WriteBand(Dictionary<string, double[]> o, string comp, CipherCEngine.TierRun run, CipherCEngine.TierBars tier,
            ReadOnlySpan<Ohlcv> data, int n, bool inBars, double from, double to, bool everyDay)
        {
            if (!tier.Active || n == 0) return;
            int last = run.NewestAt.Length == n ? run.NewestAt[n - 1] : run.LastLowAt[n - 1];
            if (last < 0) return;
            int a = inBars ? last + (int)Math.Round(from) : BarOnOrAfter(data, last, (int)Math.Round(from), everyDay);
            int b = inBars ? last + (int)Math.Floor(to + 1e-9) : BarOnOrAfter(data, last, (int)Math.Round(from) + (int)Math.Round(to - from), everyDay);
            o[comp + ComponentCompanions.BoxStart][n - 1] = a - (n - 1);
            o[comp + ComponentCompanions.BoxEnd][n - 1] = b - (n - 1);
        }

        /// <summary>
        /// The yearly band on a symbol with the original's dated yearly lows: from the first bar
        /// stamped on or after the newest date on the chart plus <paramref name="fromDays"/>
        /// calendar days to the first on or after it plus <paramref name="toDays"/>. None before
        /// the chart reaches the first date (the original draws none there).
        /// </summary>
        private static void DatedBand(Dictionary<string, double[]> o, string comp, CipherCEngine.TierBars tier,
            IReadOnlyList<DateTime> dates, ReadOnlySpan<Ohlcv> data, int n, bool inBars, double fromDays, double toDays, bool everyDay)
        {
            if (!tier.Active || n == 0) return;
            DateTime? newest = null;
            foreach (var d in dates) if (d <= data[n - 1].Date && (newest is null || d > newest)) newest = d;
            if (newest is not DateTime at) return;
            var last = data[n - 1].Date;
            var step = n > 1 ? data[n - 1].Date - data[n - 2].Date : TimeSpan.FromDays(1);
            // Off a daily chart a date past the last bar is reached at the spacing of its last two
            // bars (not checked against the original).
            var ta = at.AddDays(fromDays); var tb = at.AddDays(toDays);
            bool stepped = inBars && step > TimeSpan.Zero;
            int a = stepped && ta > last ? n - 1 + (int)Math.Ceiling((ta - last) / step) : BarOnOrAfter(data, ta, 0, everyDay);
            int b = stepped && tb > last ? n - 1 + (int)Math.Ceiling((tb - last) / step) : BarOnOrAfter(data, tb, 0, everyDay);
            o[comp + ComponentCompanions.BoxStart][n - 1] = a - (n - 1);
            o[comp + ComponentCompanions.BoxEnd][n - 1] = b - (n - 1);
        }

        /// <summary>
        /// The first bar stamped at or after <paramref name="days"/> calendar days from bar
        /// <paramref name="from"/>'s timestamp, time of day included (a winter low on bars stamped
        /// at the 14:30 UTC open lands a bar later on the 13:30 summer ones, as the original's);
        /// past the last bar, the bars <see cref="CipherCFutureClock"/> says the chart would print.
        /// </summary>
        internal static int BarOnOrAfter(ReadOnlySpan<Ohlcv> data, int from, int days, bool everyDay)
            => BarOnOrAfter(data, data[from].Date.AddDays(days), from, everyDay);

        /// <summary>The first bar stamped at or after <paramref name="target"/>, looked for from bar
        /// <paramref name="from"/>; past the last bar, counted on by <see cref="CipherCFutureClock"/>
        /// (the chart's trading days at its session's time of day).</summary>
        internal static int BarOnOrAfter(ReadOnlySpan<Ohlcv> data, DateTime target, int from, bool everyDay)
        {
            for (int i = Math.Max(0, from); i < data.Length; i++) if (data[i].Date >= target) return i;
            return new CipherCFutureClock(data, everyDay).BarOnOrAfter(target, data.Length - 1);
        }

        /// <summary>
        /// The original's yellow wave: one hump from each low to the next, troughs at the lows and
        /// crests halfway between, all on one level, over its last <see cref="WaveCycles"/>
        /// cycles and the live one, which stops at the last bar. The level is set on the bar the
        /// newest low was confirmed on: its middle 0.8 of that bar's close, its troughs and crests
        /// a fifth of the range of the 100 bars to that bar (highest high less lowest low) under
        /// and over it. The original's to the cent on 68 of its 68 charts, 2026-10-28; its middle
        /// follows the close on a Heikin-Ashi chart (the average of the four prices) and its height
        /// does not. So it sits under the candles, and moves only when a low is confirmed. In
        /// price, not log. No confirmed low yet, no wave.
        /// </summary>
        private static void DrawArcs(double[] arc, List<int> lows, CipherCEngine.TierBars tier, IReadOnlyList<int> confirmedAt,
            double[] close, double[] high, double[] low, int n)
        {
            if (lows.Count == 0) return;
            int set = -1;
            foreach (int c in confirmedAt) if (c >= 0 && c < n) set = Math.Max(set, c);
            if (set < 0) return;
            double hi = double.MinValue, lo = double.MaxValue;
            for (int i = Math.Max(0, set - WaveRangeBars + 1); i <= set; i++) { hi = Math.Max(hi, high[i]); lo = Math.Min(lo, low[i]); }
            double middle = WaveLevel * close[set], half = WaveHeight * (hi - lo);
            int first = Math.Max(0, lows.Count - 1 - WaveCycles);
            for (int k = first + 1; k < lows.Count; k++)
                DrawArc(arc, lows[k - 1], lows[k], lows[k] - lows[k - 1], middle, half);
            int a = lows[^1], due = Math.Max(1, (int)Math.Round(tier.MeanBars));
            DrawArc(arc, a, Math.Min(n - 1, a + due), due, middle, half);
        }

        /// <summary>The wave's middle, as a share of the close it is set on.</summary>
        internal const double WaveLevel = 0.8;
        /// <summary>The wave's half height, as a share of the range of <see cref="WaveRangeBars"/> bars.</summary>
        internal const double WaveHeight = 0.2;
        /// <summary>How many bars, to the confirmation, the wave's height is taken over.</summary>
        internal const int WaveRangeBars = 100;

        /// <summary>How many finished cycles the wave spans: the original draws 21 humps and the live one.</summary>
        internal const int WaveCycles = 21;

        /// <summary>
        /// One hump: from a trough at low <paramref name="a"/> (<paramref name="middle"/> less
        /// <paramref name="half"/>) up to a crest halfway through its <paramref name="span"/> bars
        /// and back down, rounded at both ends. Drawn up to bar <paramref name="last"/>; the live
        /// hump's far end is in the future.
        /// </summary>
        private static void DrawArc(double[] arc, int a, int last, int span, double middle, double half)
        {
            if (span <= 0) return;
            for (int i = a; i <= last; i++)
            {
                double t = Math.Min(1, (double)(i - a) / span);
                arc[i] = middle - half * Math.Cos(2 * Math.PI * t);
            }
        }

        /// <summary>
        /// The daily low's signs (<see cref="ComponentCompanions.Badge"/>,
        /// <see cref="ComponentCompanions.BadgeWord"/>, <see cref="ComponentCompanions.Mark"/>), as
        /// the original draws them: a check under each confirmed low, a refresh sign over it when
        /// its confirmation replaced a cancelled low; a cancelled low's mark with a cross; the low
        /// held now with a clock, a refresh sign if a low was cancelled since the last confirmed
        /// one, and its grade (LOW, MID, HIGH) on the last bar; Start, alone, under the bar the
        /// count starts from (<see cref="CipherCEngine.TierRun.Start"/>); and a
        /// check over each daily confirmation's down triangle.
        /// </summary>
        private static void DailySigns(Dictionary<string, double[]> o, CipherCEngine.TierRun d, List<int> lows, Dictionary<int, int> confirmedAt,
            double[] low, double[] grade, int n)
        {
            var values = o[CompDcl];
            var badge = o[CompDcl + ComponentCompanions.Badge];
            var word = o[CompDcl + ComponentCompanions.BadgeWord];
            var mark = o[CompDcl + ComponentCompanions.Mark];
            foreach (int i in lows)
            {
                int bits = ComponentCompanions.BadgeCheck;
                if (confirmedAt.TryGetValue(i, out int at) && at >= 0 && at < n && !double.IsNaN(d.Updated[at])) bits |= ComponentCompanions.BadgeRefresh;
                badge[i] = bits;
            }
            double replacedSince = double.NaN;
            for (int t = 0; t < n; t++)
            {
                if (!double.IsNaN(d.Confirmed[t])) replacedSince = double.NaN;
                if (double.IsNaN(d.Cancelled[t])) continue;
                replacedSince = d.Cancelled[t];
                int c = d.CancelledLow[t];
                if (c < 0 || !double.IsNaN(values[c])) continue;
                mark[c] = low[c];
                badge[c] = ComponentCompanions.BadgeCross;
            }
            int held = d.HeldAt[n - 1];
            if (held >= 0 && double.IsNaN(values[held]))
            {
                mark[held] = low[held];
                badge[held] = ComponentCompanions.BadgeClock | (double.IsNaN(replacedSince) ? 0 : ComponentCompanions.BadgeRefresh);
                word[held] = grade[n - 1];
            }
            // The original's Start: its own plate under the bar the count starts from, with no low drawn there.
            if (d.Start >= 0 && d.Start < n && double.IsNaN(word[d.Start])) word[d.Start] = StartWord;
            var confirmed = o[CompDclConfirmed];
            var confirmedBadge = o[CompDclConfirmed + ComponentCompanions.Badge];
            for (int t = 0; t < n; t++) if (!double.IsNaN(confirmed[t])) confirmedBadge[t] = ComponentCompanions.BadgeCheck;
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
            var (yearWord, yearWords) = Words(inBars, "year", "years");
            string? yearly = WindowWords("yearly cycle", yearWord, yearWords, At(CompYearlyWindow), At(CompYearlyWindow + StartSuffix), At(CompYearlyWindow + EndSuffix));
            if (yearly != null) words.Add(yearly);
            // The original's unconfirmed counts, from the table's lower half.
            if (HeldWords("daily", dayWord, dayWords, At(HeldDaysKey)) is { } heldDaily) words.Add(heldDaily);
            if (HeldWords("weekly", weekWord, weekWords, At(HeldWeeksKey)) is { } heldWeekly) words.Add(heldWeekly);

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

        /// <summary>
        /// Where a cycle is against its window, from its count (<paramref name="since"/>, the
        /// original's "days since", 0 on the low's bar) and the window's bounds (the cycle's length,
        /// both lows counted, so the low's own day is day 1): "23 days since the last daily cycle
        /// low, its window opens in 13 days".
        /// </summary>
        internal static string? WindowWords(string cycle, string unit, string units, double since, double start, double end)
        {
            if (double.IsNaN(start)) return null;          // the tier does not run on this chart
            if (double.IsNaN(since)) return $"no {cycle} low confirmed yet";
            string Units(double v) => Math.Abs(v - 1) < 1e-9 ? unit : units;
            double count = since + 1;                      // the day of the cycle, in the window's terms
            string head = $"{Num(since)} {Units(since)} since the last {cycle} low";
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

        /// <summary>
        /// The original's unconfirmed counts, as its table shows them (<see cref="HeldDaysKey"/>,
        /// <see cref="HeldWeeksKey"/>): "12 days since the unconfirmed daily low". Null when no
        /// low is held (the original's check mark).
        /// </summary>
        internal static string? HeldWords(string tier, string unit, string units, double since)
            => double.IsNaN(since) || since < 0 ? null
             : $"{Num(since)} {(Math.Abs(since - 1) < 1e-9 ? unit : units)} since the unconfirmed {tier} low";

        public static string ConfluenceWord(double score) => score >= 3 ? "high" : score >= 2 ? "mid" : "low";

        /// <summary>A held daily low's tag (<see cref="GradeSuffix"/>): 0 low, 1 mid, 2 high.</summary>
        public static string GradeWord(double grade) => grade >= 2 ? "high" : grade >= 1 ? "mid" : "low";

        /// <summary>
        /// The original's LOW/MID/HIGH tag on each bar (0-2): the Awesome Oscillator, SMA 5 less SMA
        /// 34 of hl2, above zero, plus the close above the VWAP of hlc3 anchored at each calendar
        /// month. NaN before the oscillator has 34 bars. Fitted to the original's pending labels:
        /// 98.7% of 7,486 labelled bars on sixteen assets (2022-2026), 98.8% on fourteen held out
        /// in time (2020-2022); every miss is the original one grade higher on a single bar with the
        /// oscillator under zero. A chart without volume (the original's index, dollar, gold, silver
        /// and oil charts) gets no VWAP point and is never high, as the original's never are.
        /// </summary>
        /// <summary>
        /// The top of the cycle from low <paramref name="a"/> to low <paramref name="b"/> and its
        /// translation. The top is the highest high with both lows' bars included, the first of equal
        /// highs; it is mid only exactly halfway in bars, left before, right after. This is the
        /// original's rule: its boxes and triangles follow it on 94.6% of its daily tops, where a
        /// middle band of 45-55% followed 86.0% (2026-10-28).
        /// </summary>
        internal static (int Top, string Component) TranslatedTop(double[] high, int a, int b)
        {
            int top = a;
            for (int i = a + 1; i <= b; i++) if (high[i] > high[top]) top = i;
            int twice = 2 * (top - a);
            return (top, twice < b - a ? CompLeftTop : twice > b - a ? CompRightTop : CompMidTop);
        }

        internal static double[] Grade(ReadOnlySpan<Ohlcv> data, double[] high, double[] low, double[] close)
        {
            int n = data.Length;
            var g = CipherCEngine.Nan(n);
            double s5 = 0, s34 = 0, cumPv = 0, cumV = 0;
            int month = -1;
            double Hl2(int k) => (high[k] + low[k]) / 2;
            for (int i = 0; i < n; i++)
            {
                s5 += Hl2(i); s34 += Hl2(i);
                if (i >= 5) s5 -= Hl2(i - 5);
                if (i >= 34) s34 -= Hl2(i - 34);
                var date = data[i].Date;
                int m = date.Year * 12 + date.Month;
                if (m != month) { month = m; cumPv = cumV = 0; }
                double v = data[i].Volume;
                if (!double.IsNaN(v) && v > 0) { cumPv += (high[i] + low[i] + close[i]) / 3 * v; cumV += v; }
                if (i < 33) continue;
                double score = s5 / 5 - s34 / 34 > 0 ? 1 : 0;
                if (cumV > 0 && close[i] > cumPv / cumV) score++;
                g[i] = score;
            }
            return g;
        }

        private static string TierWord(double tier) => tier switch { 2 => "weekly", 3 => "yearly", _ => "daily" };

        /// <summary>What an age is counted in: days on the daily tier, weeks on the others; bars off a daily chart.</summary>
        private static (string, string) AgoUnit(double tier, bool inBars) => Words(inBars, tier is 2 or 3 ? "week" : "day", tier is 2 or 3 ? "weeks" : "days");

        /// <summary>A unit's words, or "bar" and "bars" when the chart is not daily and the counts run in bars.</summary>
        private static (string, string) Words(bool inBars, string unit, string units) => inBars ? ("bar", "bars") : (unit, units);

        /// <summary>
        /// What a daily low's mark says (<see cref="ComponentCompanions.Mark"/>): the low held now,
        /// "Daily cycle low, unconfirmed: the low of 41200.00, low confluence, 12 days since it", with
        /// the original's refresh sign as "a low was cancelled since the last confirmed one"; a
        /// cancelled low, "Daily cycle low cancelled: the low of 41200.00, undercut before it was
        /// confirmed". <paramref name="heldDays"/> is the table's count at the chart's last bar.
        /// Null on a bar with no mark.
        /// </summary>
        internal static string? MarkWords(Func<string, double> at, double heldDays, bool inBars)
        {
            double mark = at(CompDcl + ComponentCompanions.Mark);
            if (double.IsNaN(mark)) return null;
            int badge = double.IsNaN(at(CompDcl + ComponentCompanions.Badge)) ? 0 : (int)at(CompDcl + ComponentCompanions.Badge);
            if ((badge & ComponentCompanions.BadgeCross) != 0)
                return $"Daily cycle low cancelled: the low of {Price(mark)}, undercut before it was confirmed.";
            var (dayWord, dayWords) = Words(inBars, "day", "days");
            double grade = at(CompDcl + ComponentCompanions.BadgeWord);
            string s = $"Daily cycle low, unconfirmed: the low of {Price(mark)}"
                     + (double.IsNaN(grade) || grade == StartWord ? "" : $", {GradeWord(grade)} confluence")
                     + (double.IsNaN(heldDays) || heldDays < 0 ? "" : $", {Span(heldDays, dayWord, dayWords)} since it")
                     + ".";
            if ((badge & ComponentCompanions.BadgeRefresh) != 0) s += " A low was cancelled since the last confirmed one.";
            return s;
        }

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

        /// <summary>
        /// The original's table in the price pane's bottom right corner, at one bar, in its words
        /// and layout, coloured as it is (2026-10-28: its cells read from its chart memory):
        /// whether the symbol has a tuned preset and the chart is daily, the preset's daily, weekly
        /// and yearly ranges, the days since the last confirmed daily low and weeks since the last
        /// confirmed weekly low (the windows' counts, 0 on the low's bar, as the original counts),
        /// the same for the unconfirmed ones (<see cref="HeldDaysKey"/>,
        /// <see cref="HeldWeeksKey"/>), and the symbols with a preset. Empty before the chart has
        /// a preset.
        /// </summary>
        public static IReadOnlyList<IReadOnlyList<Rendering.TableCell>> TableRows(Func<string, double[]?> component, int index,
            IReadOnlyDictionary<string, object>? parameters = null)
        {
            double At(string name) => component(name) is { } a && index >= 0 && index < a.Length ? a[index] : double.NaN;
            double value = At(PresetKey);
            if (double.IsNaN(value) || !SwitchOn(parameters, ParamTable)) return Array.Empty<IReadOnlyList<Rendering.TableCell>>();
            bool tuned = value >= 0, inBars = At(InBarsKey) == 1;
            int presetIndex = tuned ? (int)value : -1 - (int)value;
            if (presetIndex < 0 || presetIndex >= CipherCEngine.Presets.Count) return Array.Empty<IReadOnlyList<Rendering.TableCell>>();
            var preset = CipherCEngine.Presets[presetIndex];
            // A count picked in the settings (the original's custom count): its heading says so in
            // red, the first column names it and what it suits, and there is no yearly range.
            double chosenAt = At(ChosenKey);
            bool chosen = chosenAt >= 1, detected = chosen ? chosenAt == 2 : tuned;
            string head = detected ? TableTuned : TableUntuned;
            // The original prints NaN where it has no count yet; a dash here.
            string Since(double count) => double.IsNaN(count) ? "\u2014" : Num(count);
            string Held(double v) => double.IsNaN(v) ? "\u2014" : v < 0 ? "\u2705" : Num(v);
            var keys = CipherCEngine.Presets.Select(p => p.Key).ToList();
            int split = keys.IndexOf("ETH") is var e && e > 0 ? e : (keys.Count + 1) / 2;   // the stock and metal symbols, then the crypto ones
            var (pick, suits) = Picked.TryGetValue(preset.Key, out var c) ? c : (preset.Key, "NONE");

            // Each of the original's four section switches blanks its own cells; nothing moves.
            bool detection = SwitchOn(parameters, ParamTableDetection), assets = detection && SwitchOn(parameters, ParamTableAssets),
                 ranges = SwitchOn(parameters, ParamTableCount), windows = SwitchOn(parameters, ParamTableWindows);
            Rendering.TableCell C(string text, string? fill = null, int span = 1, int size = 0) => new(text, fill, span, size);
            Rendering.TableCell Blank(int span = 1) => C("", null, span);
            Rendering.TableCell Col0(bool on, Rendering.TableCell cell) => on ? cell : Blank();
            Rendering.TableCell[] Ranges(Rendering.TableCell[] cells) => ranges ? cells : cells.Select(x => Blank(x.ColSpan)).ToArray();
            Rendering.TableCell[] Windows(params Rendering.TableCell[] cells) => windows ? cells : cells.Select(x => Blank(x.ColSpan)).ToArray();
            Rendering.TableCell[] Middle(string label, string range, bool shown = true) =>
                Ranges(tuned && shown ? new[] { C(label, TableLabel, size: -1), C(range, TableValue) } : new[] { Blank(), Blank() });
            const string SinceDcl = "Days since Last DCL :", SinceWcl = "Weeks Since Last WCL :";
            return new List<IReadOnlyList<Rendering.TableCell>>
            {
                new[] { Col0(detection, C(detected ? "\u2705 Finetuned Asset Detected \u2705" : "\u26A0\uFE0F Unoptimised Asset Detected \u26A0\uFE0F", head, size: 1)) }
                    .Concat(Ranges(new[] { chosen ? C($"{pick} COUNT SELECTED", TableChosen, 2, 1) : tuned ? C($"{preset.Key} CYCLE COUNT", TableCountHead, 2, 1) : Blank(2) }))
                    .Concat(Windows(C("Cycle Low Windows", head, 2, 1))).ToArray(),
                new[] { Col0(detection, C(chosen ? $"{pick} Count Selected" : "Default Count Selected", TableDark)) }
                    .Concat(Middle("Daily Cycle Range", $"{Dash(preset.DailyMin, preset.DailyMax)} days"))
                    .Concat(Windows(C("\u2705 Confirmed \u2705", null, 2))).ToArray(),
                new[] { Col0(detection, C(chosen ? $"Recommended for {suits}" : "", TableDark)) }
                    .Concat(Middle("Weekly Cycle Range", $"{Dash(preset.WeeklyMin, preset.WeeklyMax)} weeks"))
                    .Concat(Windows(C(SinceDcl), C(Since(At(CompDailyWindow))))).ToArray(),
                new[] { Col0(detection, inBars ? C("\u26A0\uFE0F Timeframe Not Optimised, Switch to Daily \u26A0\uFE0F", TableWrongFrame) : C("\u2705 Optimised Timeframe - Daily \u2705", TableRightFrame)) }
                    .Concat(Middle("Yearly Cycle Range", $"{Dash(preset.YearlyMin, preset.YearlyMax)} years", shown: !chosen))
                    .Concat(Windows(C(SinceWcl), C(Since(At(CompWeeklyWindow))))).ToArray(),
                new[] { Col0(assets, C("\U0001F447 Finetuned Assets Listed Below \U0001F447", TableValue)) }
                    .Concat(Ranges(new[] { chosen ? C("FUTURE CYCLE LOW IS INVALID", TableChosen, 2, -1) : Blank(2) }))
                    .Concat(Windows(C("\U0001F558 Unconfirmed \U0001F558", null, 2))).ToArray(),
                new[] { Col0(assets, C(string.Join(", ", keys.Take(split)), TableDark, size: -1)), Blank(2) }.Concat(Windows(C(SinceDcl), C(Held(At(HeldDaysKey))))).ToArray(),
                new[] { Col0(assets, C(string.Join(", ", keys.Skip(split)), TableDarkLight, size: -1)), Blank(2) }.Concat(Windows(C(SinceWcl), C(Held(At(HeldWeeksKey))))).ToArray(),
            };
        }

        /// <summary>
        /// A picked count's name in the original's list of counts, and what its table says it is
        /// recommended for (its words, read from its table for every count on 2026-10-28; Marathon's
        /// is its own "NONE").
        /// </summary>
        private static readonly IReadOnlyDictionary<string, (string Pick, string For)> Picked = new Dictionary<string, (string, string)>
        {
            ["BTC"] = ("BTC", "Longer-cycle Crypto"), ["ETH"] = ("ETH", "Short-cycle Crypto"),
            ["SPX"] = ("SPX", "Equity Indexes"), ["NDQ"] = ("NDQ", "Equity Indexes"), ["DJI"] = ("DJI", "Equity Indexes"),
            ["DXY"] = ("DXY", "Forex and Bonds"), ["GOLD"] = ("XAU/USD", "Commodities/Metals"), ["SILVER"] = ("XAG/USD", "Commodities/Metals"),
            ["GDX"] = ("GDX", "Short-cycle Equities"), ["GDXJ"] = ("GDXJ", "Short-cycle Equities"), ["SILJ"] = ("SILJ", "Short-cycle Equities"),
            ["USOIL"] = ("USOIL", "Broader Commodities"), ["COIN"] = ("COIN", "Longer-cycle Crypto"), ["MSTR"] = ("MSTR", "Longer-cycle Crypto"),
            ["MARA"] = ("MARA", "NONE"), ["RIOT"] = ("RIOT", "Short-cycle Equities"),
        };

        // The original's cell fills, #RRGGBBAA.
        internal const string TableTuned = "#4CAF4F69", TableUntuned = "#FF525269", TableCountHead = "#20C12B5C",
            TableDark = "#363A455C", TableDarkLight = "#363A454B", TableLabel = "#363A4519", TableValue = "#787B8626",
            TableRightFrame = "#4CAF4FA3", TableWrongFrame = "#FF000066", TableChosen = "#F236455C";

        /// <summary>"36\u201344", or "4" when a range is one value: the original's en dash.</summary>
        private static string Dash(double min, double max) => min == max ? Num(min) : $"{Num(min)}\u2013{Num(max)}";

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
            double At(string name) => allComponentData.TryGetValue(name, out var a) && dataIndex >= 0 && dataIndex < a.Length ? a[dataIndex] : double.NaN;
            // A daily low's bar with no confirmed low on it can still carry the original's mark:
            // the low held now (clock) or a cancelled one (cross).
            if (double.IsNaN(value))
                return componentName == CompDcl ? MarkWords(At, allComponentData.TryGetValue(HeldDaysKey, out var hd) && hd.Length > 0 ? hd[^1] : double.NaN, At(InBarsKey) == 1) : null;
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
                    double since = tier == "daily" ? At(CompDailyWindow) : tier == "weekly" ? At(CompWeeklyWindow) : double.NaN;
                    var (sinceUnit, sinceUnits) = tier == "weekly" ? (weekWord, weekWords) : (dayWord, dayWords);
                    string on = double.IsNaN(since) ? "" : $" {Span(since, sinceUnit, sinceUnits)} after the last cycle low";
                    double lowest = At(CompPending + LowestSuffix), lowestAgo = At(CompPending + LowestAgoSuffix);
                    // The daily tier names the low it will confirm.
                    if (tier == "daily")
                    {
                        double grade = At(CompPending + GradeSuffix), replaced = At(CompPending + ReplacedSuffix);
                        string s0 = $"Daily cycle low pending{on}" + (double.IsNaN(grade) ? "" : $", {GradeWord(grade)} confluence") +
                                    (double.IsNaN(lowest) ? "." : $": the low of {Price(lowest)}" + (double.IsNaN(lowestAgo) ? "." : $", {Span(lowestAgo, unit, units)} ago."));
                        if (!double.IsNaN(replaced)) s0 += $" It replaces the cancelled low of {Price(replaced)}.";
                        return s0;
                    }
                    string s = $"{Tier(tier)} cycle low pending, the cycle line reached oversold{on}.";
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
                case CompYearlyWindow:
                {
                    var (yearWord, yearWords) = Words(inBars, "year", "years");
                    return WindowWords("yearly cycle", yearWord, yearWords, value, At(CompYearlyWindow + StartSuffix), At(CompYearlyWindow + EndSuffix));
                }
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
