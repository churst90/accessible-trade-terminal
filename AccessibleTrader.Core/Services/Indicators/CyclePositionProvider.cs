using System.Globalization;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using static AccessibleTrader.Core.Services.Indicators.CipherCProvider;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// <b>Cycle Position — where the chart is in its daily, weekly and yearly cycles.</b> Built
    /// 2026-10-28 (Cody: "something that helps me know where I am within a cycle so I can follow
    /// Camel's technique ... without a bunch of patch work ... that works for any asset if
    /// possible, uses all the same symbols and markers as Cipher C"). Cipher C stays the faithful
    /// copy of the original; this is the same picture with one rule set for every asset and
    /// timing windows learned from the chart's own history. Design and test results:
    /// docs/CYCLE_POSITION_DESIGN.md.
    ///
    /// <para><b>What is the same as Cipher C.</b> Everything drawn and heard (<see cref="CipherCProvider.Draw"/>):
    /// the D, W and Y lows with their lengths and signs, the confirmations, the held low with its
    /// clock, the cancelled lows, the lettered tops with their boxes, the bands over the live
    /// windows, the wave, the moving average and the display switches.</para>
    ///
    /// <para><b>What is different.</b></para>
    /// <list type="bullet">
    /// <item><b>One daily rule for every asset.</b> The original's own daily rule with its default
    /// settings (a double-smoothed stochastic 10, 9 above its 5-bar average and between 10 and
    /// 75; the lowest swing low after the top; a hold of half the shortest cycle; an undercut
    /// within a third of it drops the low), on every asset. The only number per asset is the
    /// shortest daily cycle: the symbol's own where Cipher C has one, else 32 bars. It cannot be
    /// read off the price (the spacing of price swings is about 50 to 65 bars on every asset,
    /// whatever the original's cycle; rank correlation about 0).</item>
    /// <item><b>One nesting rule.</b> Weekly lows are picked from the daily lows (the second daily
    /// low after a weekly low is the candidate, the next one judges it); yearly lows from the
    /// price. The weekly count runs from the newest weekly low, not the one before it.</item>
    /// <item><b>Learned windows.</b> A tier's window is the 20th to 80th percentile of its own
    /// past cycle lengths on this chart, known when each cycle ends (6 daily or 4 weekly or
    /// yearly cycles first; before that a default window). Each bar knows, of the past cycles
    /// still unconfirmed on its day of the cycle, the share whose low had already come; and each
    /// cycle is scored against the window it was given when it began: the running hit rate is said.</item>
    /// <item><b>EARLY, DUE, LATE</b> under the held daily low, in place of the original's
    /// LOW/MID/HIGH: where the cycle it would end falls against the learned window.</item>
    /// <item><b>Misses shown.</b> Cancelled daily lows and the bar each low was confirmed on are
    /// drawn by default.</item>
    /// </list>
    /// </summary>
    public sealed class CyclePositionProvider : IIndicatorProvider
    {
        public const string Code = "CYCLE_POSITION";
        public string Name => "Cycle Position";

        public const string ParamDailyCycle = "DailyCycleDays", ParamYearlyCycle = "YearlyCycleYears",
            ParamBandFrom = "BandFromPercent", ParamBandTo = "BandToPercent";

        /// <summary>
        /// Per bar, on each window: of the past cycles still unconfirmed on the cycle's day (their
        /// next low not yet confirmed that many days in), the share (0-100) whose low had already
        /// come by it. NaN when fewer than <see cref="MinWaiting"/> past cycles ran unconfirmed that long.
        /// </summary>
        public const string BottomedSuffix = "_bottomed";
        /// <summary>Per bar, on each window: how many past cycles <see cref="BottomedSuffix"/> is the share of.</summary>
        public const string WaitingSuffix = "_waiting";
        /// <summary>Per bar, on each window: how many past cycles the window is learned from.</summary>
        public const string SamplesSuffix = "_samples";
        /// <summary>Per bar, on each window: 1 when the window is learned, 0 when it is still the default.</summary>
        public const string LearnedSuffix = "_learned";
        /// <summary>Per bar, on each window: 0 early, 1 due, 2 late (before, in, after the window).</summary>
        public const string PhaseSuffix = "_phase";
        /// <summary>Per bar, on each window: cycles that ended inside the window they began with, and cycles scored.</summary>
        public const string HitsSuffix = "_hits", ScoredSuffix = "_scored";
        /// <summary>Per bar: the daily cycle of the weekly one (on the weekly window), the weekly of the yearly (on the yearly).</summary>
        public const string NestSuffix = "_nest";
        /// <summary>Per bar, on each window: the cycle's day (week, year to a tenth) the bar is in, the low's own being 1 (0.1 on the yearly).</summary>
        public const string DayOfSuffix = "_dayOf";
        /// <summary>Per bar, on the weekly window: 1 when a weekly candidate, not yet a weekly low, is drawn after the low <see cref="NestSuffix"/> counts from.</summary>
        public const string NestHeldSuffix = "_nestHeld";
        /// <summary>The shortest daily cycle counted with (in the chart's days, or bars off a daily chart), and where it came from: 0 the default, 1 the symbol's own, 2 set by hand.</summary>
        public const string LengthKey = "__cp_length", LengthSourceKey = "__cp_length_source";

        /// <summary>
        /// Per bar, on the daily window (<see cref="CycleLowConfidence"/>; off a daily chart in its bars): "Low is
        /// in", the share (0-1) of held daily lows like this one that were confirmed, on each bar a
        /// daily low is held; and its reasons: the chance (0-1) a random walk goes back to the low
        /// before its confirmation, the days left to it, percent above the low, average ranges risen,
        /// and the bars since the low each confirmation has run (a higher low's high broken, closes
        /// above the 10-day average, the stochastic over its trigger, the SAR under price, the first
        /// close above the 10 before it), 0 when off; and the low's price. The same on the weekly
        /// window for the weekly low being made and on the yearly window for the yearly one (the
        /// yearly window has no confirmations: the share, touch, days left, above, risen and the low).
        /// </summary>
        public const string LowInSuffix = "_lowIn", LowInTouchSuffix = "_lowInTouch", LowInLeftSuffix = "_lowInLeft",
            LowInAboveSuffix = "_lowInAbove", LowInRiseSuffix = "_lowInRise", LowInStructureSuffix = "_lowInStructure",
            LowInSmaSuffix = "_lowInSma", LowInDssSuffix = "_lowInDss", LowInSarSuffix = "_lowInSar", LowInBreakSuffix = "_lowInBreak",
            LowInLowSuffix = "_lowInLow";
        /// <summary>Per bar after a confirmed daily low: "Low expected", the chance (0-1) a new daily low is drawn within the next <see cref="ExpectedDaysSuffix"/> days.</summary>
        public const string ExpectedSuffix = "_lowExpected", ExpectedDaysSuffix = "_lowExpectedDays";

        /// <summary>
        /// Per bar a daily low is held, on the daily window: the original's confluence grade (0 low,
        /// 1 mid, 2 high: <see cref="CipherCProvider.Grade"/>) and its two points: the Awesome
        /// Oscillator, and the close against this month's VWAP (1 above, 0 not, NaN with no volume).
        /// Said as context: held lows graded high were confirmed more often (92% against 74% for
        /// low, on 31 daily charts), but within each "Low is in" band the grade adds nothing.
        /// </summary>
        public const string ConfluenceSuffix = "_confluence", AoSuffix = "_ao", VwapSuffix = "_vwap";

        /// <summary>
        /// Per bar after a confirmed daily low (not the count's seed), on the daily window: the
        /// cycle's top so far (the highest high since the low, the first of equal highs) and its
        /// day; of the past cycles on this chart still unconfirmed this far, the share (0-100)
        /// whose top had come by this day, and how many (once the window is learned and
        /// <see cref="MinWaiting"/> ran this far); from half the window's middle on, 1 when the
        /// top so far came before the middle (left-translated so far), 0 after; and, until the
        /// cycle fails, how often cycles at this stage with the top on this side and on the other
        /// went on to fail (<see cref="TopFails"/>). And this chart's finished daily cycles by
        /// translation, as each became known: left, mid, right.
        /// </summary>
        public const string TopSuffix = "_top", TopDaySuffix = "_topDay", TopCameSuffix = "_topCame", TopWaitingSuffix = "_topWaiting",
            TopLeftSuffix = "_topLeft", TopFailsSuffix = "_topFails", TopFailsOtherSuffix = "_topFailsOther",
            LeftTopsSuffix = "_leftTops", MidTopsSuffix = "_midTops", RightTopsSuffix = "_rightTops";

        /// <summary>
        /// How often a daily cycle not yet failed went on to fail (a close under its low before
        /// the next low was confirmed), by how far in it is (its day over the middle of its window,
        /// from, to) and where its top so far came: before the middle, after it. Measured on Cycle
        /// Position's own lows on 31 daily charts (2026-10-28; 15 4-hour and 15 weekly charts within
        /// 4 points; either half of the charts within 1).
        /// </summary>
        internal static readonly (double From, double To, double Left, double Right)[] TopFails =
            { (0.5, 0.8, 35, 18), (0.8, 1.0, 26, 14), (1.0, 1.25, 16, 10) };

        /// <summary>
        /// After a failed daily cycle, how often the next daily low was a weekly low, and after one
        /// that held: 3,182 failed and 4,465 held cycles on 31 daily charts (2026-10-28; 4-hour 52
        /// and 25, weekly 50 and 28).
        /// </summary>
        internal const double FailedWeeklyNext = 56, HeldWeeklyNext = 26;

        /// <summary>
        /// The live window's first and last bar on each window, on the last bar only: their
        /// dates as OLE Automation dates; past the last bar, the bars the chart would print
        /// (<see cref="CipherCFutureClock"/>), where the band is drawn. <see cref="BarSecondsKey"/>
        /// is the chart's bar length, for how the dates are said.
        /// </summary>
        internal static string DatesKey(string comp, bool closes) => (closes ? "__cp_closes_" : "__cp_opens_") + comp;
        public const string BarSecondsKey = "__cp_bar_seconds";
        /// <summary>The half-cycle low: a mark on its bar, found <see cref="HalfPivotShare"/>-th of the shortest cycle later.</summary>
        public const string CompHalfLow = "Half Cycle Low";
        /// <summary>
        /// On the daily window, from the bar the half-cycle low is found until the next daily low:
        /// its price, its day, the share of the rise from the cycle's low to its top so far that it
        /// gave back, and (while the cycle has not failed) how often cycles whose half-cycle low
        /// gave back that much failed.
        /// </summary>
        public const string HalfSuffix = "_half", HalfDaySuffix = "_halfDay", HalfGaveSuffix = "_halfGave", HalfFailsSuffix = "_halfFails";
        /// <summary>Where in the cycle a half-cycle low is looked for, in usual cycles; the pivot's reach, the shortest cycle over this.</summary>
        internal const double HalfFrom = 0.3, HalfTo = 0.7, HalfPivotShare = 8;
        /// <summary>
        /// The share of daily cycles that failed after their half-cycle low gave back this much of
        /// the rise, on 31 daily charts' 5,223 cycles not failed when it was found (2026-10-28; the
        /// same on each half of the charts within 6 points); <see cref="HalfFailsBase"/> over all of them.
        /// </summary>
        internal static readonly (double From, double To, double Fails)[] HalfFails = { (0, 0.5, 18), (0.5, 0.7, 31), (0.7, 0.8, 43), (0.8, double.PositiveInfinity, 49) };
        internal const double HalfFailsBase = 30;
        public const string ParamHalfLows = "HalfCycleLows";
        /// <summary>On the half-cycle low's own bar: how many bars later it was found.</summary>
        public const string HalfFoundSuffix = "_found";
        internal const string HalfLowHex = "#80CBC4";
        /// <summary>The last bar's own date, on the last bar (an OLE Automation date), for whether a window is open yet.</summary>
        public const string NowKey = "__cp_now";

        /// <summary>The words under the held daily low (<see cref="ComponentCompanions.BadgeWord"/>), and the count's Start plate.</summary>
        internal const string Words = "EARLY|DUE|LATE|Start@" + StartHex;

        /// <summary>Past cycles a window needs before it is learned: daily, weekly, yearly.</summary>
        public static readonly int[] MinSamples = { 6, 4, 4 };
        /// <summary>Past cycles still unconfirmed on a day that "had come by now" needs.</summary>
        public const int MinWaiting = 3;
        public const double DefaultBandFrom = 20, DefaultBandTo = 80;
        /// <summary>The default daily window before it is learned: the shortest cycle to this many times it.</summary>
        public const double DefaultDailySpan = 1.25;
        /// <summary>The default weekly window, in shortest daily cycles.</summary>
        public const double DefaultWeeklyFrom = 3, DefaultWeeklyTo = 5;
        /// <summary>
        /// A weekly low is the lowest daily low at least this share of the shortest weekly cycle
        /// after the one before (<see cref="CipherCEngine.WeeklyByTime"/>): the symbol's own
        /// shortest weekly cycle, else <see cref="DefaultWeeklyFrom"/> shortest daily ones.
        /// </summary>
        public const double WeeklyGapShare = 0.5;
        public const double DefaultYearlyYears = 4, DefaultYearlySpread = 0.25;
        public const double DssOverbought = 75, DssOversold = 10;
        /// <summary>The yearly window's steps per year.</summary>
        public const double YearlySteps = 10;

        /// <summary>
        /// Cipher C's display switches, less its table's section switches (this table has none),
        /// with the misses on: cancelled daily lows and the confirmation mark start on here.
        /// </summary>
        internal static readonly (string Name, string Display, bool On, string Description)[] Switches =
            CipherCProvider.Switches
                .Where(w => w.Name is not (ParamTableDetection or ParamTableAssets or ParamTableWindows or ParamTableCount))
                .Select(w => w.Name switch
                {
                    ParamTable => (w.Name, w.Display, true, "The table in the price pane's bottom right corner: each tier's count, phase, window and hit rate."),
                    ParamGrade => (w.Name, "Daily lows: phase", true, "The phase (EARLY, DUE, LATE) under the unconfirmed daily low: where the cycle it would end falls against the learned window."),
                    ParamCancelledLows => (w.Name, w.Display, true, "On by default. A daily low undercut before it was confirmed keeps its mark, with a red cross."),
                    ParamConfirmationMark => (w.Name, w.Display, true, "On by default. A check over a down triangle on the bar each daily low is confirmed on: where it became known."),
                    _ => w,
                })
                .Append((ParamHalfLows, "Half-cycle lows", true,
                    "On by default. A small up triangle under each daily cycle's half-cycle low: the first low from 0.3 to 0.7 of the usual cycle in that is above the cycle's low and the lowest for an eighth of the shortest cycle each side. Drawn on its bar, known that many bars later."))
                .ToArray();

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code,
                Name = "Cycle Position",
                Category = "Cycles",
                DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description =
                    "Where the chart is in its daily, weekly and yearly cycles, drawn with Cipher C's marks: D, W and Y " +
                    "under the cycle lows, a check where each was confirmed, a cross under a low that was dropped, a clock " +
                    "and EARLY, DUE or LATE under the low being held, lettered translated tops, bands over the live windows " +
                    "and a wave from low to low. One rule for every asset. Each window is learned from the chart's own past " +
                    "cycles (the middle 60% of their lengths), so it says \"day 24 of the daily cycle, the low had come by now in " +
                    "70% of 12 past cycles this far in unconfirmed\", which daily cycle of the weekly one this is, and how often the window has held. " +
                    "Lows are drawn on the low's bar but known only when confirmed. A map of cycle timing, not an edge.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamDailyCycle, DisplayName = "Shortest daily cycle (days)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 1,
                            Description = "0 = automatic: the symbol's own where Cipher C tunes one (36 trading days on the Dow, 48 calendar days on Bitcoin), else 32. In the chart's own days; off a daily chart, in its bars. It cannot be read from the price, so it is the one number per asset." },
                    new() { Name = ParamYearlyCycle, DisplayName = "Yearly cycle (years)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 0.5,
                            Description = "0 = automatic: the symbol's own where Cipher C tunes one, else 4. Yearly lows are found from the price: the lowest low for 0.15 of a cycle on both sides, at least 0.8 of a cycle after the last." },
                    new() { Name = ParamBandFrom, DisplayName = "Window from (percentile)", DataType = typeof(double), DefaultValue = DefaultBandFrom, MinValue = 0, MaxValue = 100, Step = 5,
                            Description = "20 by default: the window opens at the length 20% of past cycles were shorter than." },
                    new() { Name = ParamBandTo, DisplayName = "Window to (percentile)", DataType = typeof(double), DefaultValue = DefaultBandTo, MinValue = 0, MaxValue = 100, Step = 5,
                            Description = "80 by default: the window closes at the length 80% of past cycles were no longer than." },
                    new() { Name = ParamSma, DisplayName = "Moving average length", DataType = typeof(int), DefaultValue = (double)DefaultSma, MinValue = 2, Step = 1,
                            Description = "The simple moving average drawn on price, 10 by default, left out until included. A close over it counts toward a confirmation's confluence." },
                    new() { Name = ParamSmaSource, DisplayName = "Moving average source", DataType = typeof(string), DefaultValue = "close",
                            Description = "What it averages: open, high, low, close, hl2, hlc3, hlcc4, ohlc4 or volume. Close by default." },
                    new() { Name = ParamSmaOffset, DisplayName = "Moving average offset", DataType = typeof(int), DefaultValue = 0.0, Step = 1,
                            Description = "Bars to shift the line by, right if above 0. 0 by default." },
                }.Concat(Switches.Select(w => new IndicatorParameterMetadata
                {
                    Name = w.Name, DisplayName = w.Display, DataType = typeof(bool), DefaultValue = w.On, Description = w.Description,
                })).ToList(),
                Components = Components(Words).Append(HalfLowComponent).ToList(),
            }
        };

        /// <summary>The half-cycle low's mark: a small up triangle under the bar, in the daily band's green. Value: its low.</summary>
        internal static IndicatorComponentMetadata HalfLowComponent => new()
        {
            Name = CompHalfLow, DisplayName = CompHalfLow, DisplayType = ComponentDisplayType.TriangleUp, Role = ComponentRole.Signal,
            Causality = ComponentCausality.Lookahead, DefaultMarkerAnchor = MarkerAnchor.BelowBar, MarkerTouchesBar = true,
            DefaultColorHex = HalfLowHex, DefaultThickness = 2f, DefaultDrawOrder = 3,
            DefaultEnvelopeType = "Ping", DefaultPlaybackLayer = PlaybackLayer.Background,
            DefaultSoundPatchId = "triangle_bell", DefaultBaseFrequency = 370, DefaultDecayMs = 250,
            DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
            DefaultSignalSpeechTemplate = "Half-cycle low.",
        };

        // ── Calculation ────────────────────────────────────────────────────────

        /// <summary>Everything one run decides, by component name and companion key. Public for the tests and the lab.</summary>
        public static Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object>? parameters)
        {
            int n = data.Length;
            var o = Outputs(n);
            double[] A(string key) => o.TryGetValue(key, out var a) ? a : o[key] = CipherCEngine.Nan(n);
            A(CompHalfLow); A(CompHalfLow + ComponentCompanions.Hide);
            if (n == 0) return o;
            // A bar's stamp; past the last bar, the one the chart would print.
            List<DateTime>? future = null;
            bool everyDay = false;
            var bars0 = data.ToArray();
            DateTime BarStamp(int i)
            {
                if (i < n) return bars0[Math.Max(0, i)].Date;
                if (future == null || future.Count < i - n + 1)
                    future = FutureStamps(bars0, Math.Max(i - n + 1, 2 * (future?.Count ?? 0)), everyDay);
                return future[i - n];
            }

            // The switches this indicator starts differently from Cipher C's, filled in so the
            // shared drawing reads them.
            var p = new Dictionary<string, object>(parameters ?? new Dictionary<string, object>());
            foreach (var sw in Switches) p.TryAdd(sw.Name, sw.On);

            var tiers = BuildTiers(data, p);
            everyDay = tiers.Clock.TradesEveryDay;
            var (clock, inBars, L, years, yMin, yMax) = (tiers.Clock, tiers.InBars, tiers.L, tiers.Years, tiers.YMin, tiers.YMax);
            var (dTier, wTier, yTier, d, w, y, weeklyFromDaily) = (tiers.DTier, tiers.WTier, tiers.YTier, tiers.D, tiers.W, tiers.Y, tiers.WeeklyFromDaily);
            double qFrom = Math.Clamp(Dbl(p, ParamBandFrom, DefaultBandFrom), 0, 100) / 100;
            double qTo = Math.Clamp(Dbl(p, ParamBandTo, DefaultBandTo), 0, 100) / 100;
            if (qTo < qFrom) (qFrom, qTo) = (qTo, qFrom);

            // Each tier's unit, in bars. Off a daily chart every tier counts in the chart's bars, as
            // Cipher C's does. On one the yearly window is learned in tenths of a year (whole years
            // are too coarse: a 3.5 to 4.5 year cycle would be "year 4 to year 4") and said in years.
            double dayBars = inBars ? 1 : clock.BarsPerDay;
            double weekBars = inBars ? 1 : clock.BarsPerDay * clock.DaysPerWeek;
            double ySteps = inBars ? 1 : YearlySteps;
            double yearBars = inBars ? 1 : clock.BarsPerDay * clock.DaysPerYear / YearlySteps;
            double wPer = inBars ? 1 : 1 / clock.DaysPerWeek, yPer = inBars ? clock.DaysPerYear : YearlySteps;
            var dLearn = Learn(d, n, dayBars, MinSamples[0], qFrom, qTo, L, L * DefaultDailySpan);
            var wLearn = Learn(w, n, weekBars, MinSamples[1], qFrom, qTo, DefaultWeeklyFrom * L * wPer, DefaultWeeklyTo * L * wPer);
            var yLearn = Learn(y, n, yearBars, MinSamples[2], qFrom, qTo, yMin * yPer, yMax * yPer);

            // The held daily low's phase: where the cycle it would end falls against the window.
            var heldPhase = CipherCEngine.Nan(n);
            for (int t = 0; t < n; t++)
            {
                int held = d.HeldAt[t];
                if (held < 0) continue;
                int last = d.LastLowAt[t] >= 0 ? d.LastLowAt[t] : d.Start;
                if (last < 0) continue;
                heldPhase[t] = Phase(Length(last, held, dayBars), dLearn.Start[t], dLearn.End[t]);
            }

            var learned = new[] { dLearn, wLearn, yLearn };
            var plan = new Plan
            {
                Clock = clock, InBars = inBars, DTier = dTier, WTier = wTier, YTier = yTier, D = d, W = w, Y = y,
                WeeklyFromDaily = weeklyFromDaily, YDates = null,
                DailyWindow = (dLearn.Start, dLearn.End), WeeklyWindow = (wLearn.Start, wLearn.End),
                YearlyWindow = (yLearn.Start.Select(v => v / ySteps).ToArray(), yLearn.End.Select(v => v / ySteps).ToArray()),
                HeldWord = heldPhase,
                NormalLength = (tier, prev, c, t) =>
                {
                    var b = learned[tier - 1];
                    double len = Length(prev, c, tier == 1 ? dayBars : tier == 2 ? weekBars : yearBars);
                    return len >= b.Start[t] && len <= b.End[t];
                },
            };
            Draw(o, data, p, plan);

            // Low is in, and Low expected. Fitted on daily charts and counted in bars, they hold
            // unseen on 4-hour and weekly charts (2026-10-28: the first "very high" right 96.5% on 15
            // 4-hour charts, 97.1% on 15 weekly ones, against 96.6% on the daily); the yearly reading
            // is in calendar days and holds on weekly charts (121 of 123), not on 4-hour ones (19 of 26).
            if (dTier.Active)
            {
                bool yearly = yTier.Active && !(inBars && CipherCEngine.MeasureBars(data).BarsPerDay > 1 + 1e-6);
                bool weekly = weeklyFromDaily && w.NewestAt.Length == n;
                var lc = CycleLowConfidence.Compute(data, d, L, A(CompDailyWindow + StartSuffix), A(CompDailyWindow + EndSuffix),
                    weekly ? w : null, tiers.WeeklyGap, yearly ? y : null, years);
                foreach (var (comp, r, on) in new[] { (CompDailyWindow, (CycleLowConfidence.Reading)lc, true), (CompWeeklyWindow, lc.Weekly, weekly), (CompYearlyWindow, lc.Yearly, yearly) })
                {
                    if (!on) continue;
                    foreach (var (suffix, values) in new[]
                             {
                                 (LowInSuffix, r.Chance), (LowInTouchSuffix, r.Touch), (LowInLeftSuffix, r.Left), (LowInAboveSuffix, r.Above),
                                 (LowInRiseSuffix, r.Rise), (LowInLowSuffix, r.Low), (LowInStructureSuffix, r.Structure), (LowInSmaSuffix, r.Sma),
                                 (LowInDssSuffix, r.Dss), (LowInSarSuffix, r.Sar), (LowInBreakSuffix, r.Break),
                             })
                        // The yearly reading counts no confirmations: it does not read them.
                        if (comp != CompYearlyWindow || suffix is LowInSuffix or LowInTouchSuffix or LowInLeftSuffix or LowInAboveSuffix or LowInRiseSuffix or LowInLowSuffix)
                            o[comp + suffix] = values;
                }
                o[CompDailyWindow + ExpectedSuffix] = lc.Expected;
                o[CompDailyWindow + ExpectedDaysSuffix] = lc.ExpectedDays;
            }

            // The original's confluence grade, while a daily low is held, with its two points.
            if (dTier.Active)
            {
                var high = new double[n]; var low = new double[n]; var close = new double[n];
                for (int t = 0; t < n; t++) { high[t] = data[t].High; low[t] = data[t].Low; close[t] = data[t].Close; }
                var (ao, vwap) = GradeParts(data, high, low, close);
                double[] grade = A(CompDailyWindow + ConfluenceSuffix), aoOut = A(CompDailyWindow + AoSuffix), vwapOut = A(CompDailyWindow + VwapSuffix);
                for (int t = 0; t < n; t++)
                {
                    if (d.HeldAt[t] < 0 || double.IsNaN(ao[t])) continue;
                    aoOut[t] = ao[t]; vwapOut[t] = vwap[t];
                    grade[t] = (ao[t] > 0 ? 1 : 0) + (vwap[t] > 0 ? 1 : 0);
                }
            }

            // Where each bar is against its learned window, and how often the window has held.
            foreach (var (comp, run, tier, learn, bars, steps) in new[]
                     {
                         (CompDailyWindow, d, dTier, dLearn, dayBars, 1.0), (CompWeeklyWindow, w, wTier, wLearn, weekBars, 1.0),
                         (CompYearlyWindow, y, yTier, yLearn, yearBars, ySteps),
                     })
            {
                if (!tier.Active) continue;
                double[] bottomed = A(comp + BottomedSuffix), waiting = A(comp + WaitingSuffix), samples = A(comp + SamplesSuffix), isLearned = A(comp + LearnedSuffix),
                         phase = A(comp + PhaseSuffix), hits = A(comp + HitsSuffix), scored = A(comp + ScoredSuffix), dayOf = A(comp + DayOfSuffix);
                for (int t = 0; t < n; t++)
                {
                    samples[t] = learn.Samples[t];
                    isLearned[t] = learn.IsLearned[t] ? 1 : 0;
                    hits[t] = learn.Hits[t]; scored[t] = learn.Scored[t];
                    int last = run.LastLowAt[t];
                    if (last < 0) continue;
                    double day = Length(last, t, bars);
                    dayOf[t] = day / steps;
                    phase[t] = Phase(day, learn.Start[t], learn.End[t]);
                    var (came, still) = learn.Bottomed(t, day);
                    waiting[t] = still;
                    if (still >= MinWaiting) bottomed[t] = Math.Round(100.0 * came / still);
                }
                // The live window as a band, in bars from the newest low: the first bar of its first
                // day to the last bar of its last.
                int newest = run.LastLowAt[n - 1];
                if (newest >= 0)
                {
                    int a = newest + (int)Math.Round((learn.Start[n - 1] - 1) * bars), b = newest + (int)Math.Round(learn.End[n - 1] * bars) - 1;
                    o[comp + ComponentCompanions.BoxStart][n - 1] = a - (n - 1);
                    o[comp + ComponentCompanions.BoxEnd][n - 1] = b - (n - 1);
                    A(DatesKey(comp, false))[n - 1] = BarStamp(a).ToOADate();
                    A(DatesKey(comp, true))[n - 1] = BarStamp(b).ToOADate();
                }
            }
            Array.Fill(A(BarSecondsKey), Math.Round(86400 / CipherCEngine.MeasureBars(data).BarsPerDay));
            A(NowKey)[n - 1] = data[n - 1].Date.ToOADate();

            // The daily cycle's top so far, and what its place has meant on past cycles.
            if (dTier.Active) Tops(o, A, data, d, dLearn, n);
            // The half-cycle low, and how much of the rise it gave back.
            if (dTier.Active) HalfLows(A, data, d, dLearn, L, n);

            // Nesting: which daily cycle of the weekly one, which weekly of the yearly.
            // Picked from the daily lows by time, a weekly cycle is one daily cycle or several (two
            // or three most often, up to six or seven), counted from the newest weekly low, the one
            // the week count runs from. A candidate drawn since, not yet a weekly low, is flagged.
            Nest(A(CompWeeklyWindow + NestSuffix), w.LastLowAt, d, wTier.Active && dTier.Active, n);
            if (weeklyFromDaily && dTier.Active)
            {
                var held = A(CompWeeklyWindow + NestHeldSuffix);
                for (int t = 0; t < n; t++) if (w.LastLowAt[t] >= 0) held[t] = w.HeldAt[t] > w.LastLowAt[t] ? 1 : 0;
            }
            Nest(A(CompYearlyWindow + NestSuffix), y.LastLowAt, w, yTier.Active && wTier.Active, n);

            Array.Fill(A(LengthKey), L);
            Array.Fill(A(LengthSourceKey), tiers.LengthSource);
            ApplyDisplay(o, p, n);
            if (!IndicatorParams.GetBool(p, ParamHalfLows, true))
            {
                double[] half = o[CompHalfLow], hide = o[CompHalfLow + ComponentCompanions.Hide];
                for (int t = 0; t < n; t++)
                    if (!double.IsNaN(half[t])) hide[t] = ComponentCompanions.Hidden(hide, t) | ComponentCompanions.HideMarker;
            }
            return o;
        }

        /// <summary>
        /// The three tiers one run reads, and the numbers they were built from: the shortest daily
        /// cycle <see cref="Tiers.L"/>, the yearly cycle, the weekly gap (<see cref="WeeklyGapShare"/>
        /// of the shortest weekly cycle, in bars). <see cref="Compute"/>, Low is in and the lab
        /// harnesses all start here, so they read the same lows.
        /// </summary>
        internal sealed record Tiers(CipherCEngine.BarClock Clock, bool InBars, double L, double LengthSource, double Years, double YMin, double YMax,
            CipherCEngine.TierBars DTier, CipherCEngine.TierBars WTier, CipherCEngine.TierBars YTier,
            CipherCEngine.TierRun D, CipherCEngine.TierRun W, CipherCEngine.TierRun Y, bool WeeklyFromDaily, double WeeklyGap);

        /// <summary>Builds <see cref="Tiers"/> from the bars and the filled-in parameters.</summary>
        internal static Tiers BuildTiers(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> p)
        {
            var chartClock = CipherCEngine.MeasureBars(data);
            var own = CipherCEngine.PresetForSymbol(Str(p, "__symbol"));
            // Off a daily chart the daily count runs in the chart's own bars, as Cipher C's does.
            bool inBars = Math.Abs(chartClock.BarsPerDay - 1) > 1e-6;
            var clock = inBars ? new CipherCEngine.BarClock(1, own?.CalendarDays ?? chartClock.TradesEveryDay) : chartClock;
            double dayScale = own == null ? 1 : (clock.TradesEveryDay ? 7.0 : 5.0) / (own.CalendarDays ? 7.0 : 5.0);

            double set = Dbl(p, ParamDailyCycle, 0);
            double L = set > 0 ? set : own != null ? own.CountedDailyMin * dayScale : CipherCEngine.DefaultCountDailyMin;
            double years = Dbl(p, ParamYearlyCycle, 0) is var ys && ys > 0 ? ys
                : own != null ? (own.YearlyMin + own.YearlyMax) / 2 : DefaultYearlyYears;
            // The default yearly window, until four yearly cycles are known: the symbol's own range,
            // at least a quarter either side of its middle (Bitcoin's is 4 to 4 years).
            bool ownYears = own != null && !(Dbl(p, ParamYearlyCycle, 0) > 0);
            double yMin = Math.Min(ownYears ? own!.YearlyMin : years, years * (1 - DefaultYearlySpread));
            double yMax = Math.Max(ownYears ? own!.YearlyMax : years, years * (1 + DefaultYearlySpread));

            var dTier = CipherCEngine.Tier(L, L * DefaultDailySpan, 1, clock);
            var wTier = CipherCEngine.Tier(DefaultWeeklyFrom * L, DefaultWeeklyTo * L, clock.DaysPerWeek, clock);
            var yTier = CipherCEngine.Tier(yMin * clock.DaysPerYear, yMax * clock.DaysPerYear, clock.DaysPerYear, clock);

            var d = CipherCEngine.DetectDaily(data, dTier, CipherCEngine.DefaultDssLength, CipherCEngine.DefaultDssSmooth,
                DssOverbought, DssOversold, pivotBreak: false);
            bool weeklyFromDaily = dTier.Active && wTier.Active;
            double shortestWeekly = set <= 0 && own != null ? own.WeeklyMin * clock.DaysPerWeek * clock.BarsPerDay : wTier.MinBars;
            double gap = WeeklyGapShare * shortestWeekly;
            var w = weeklyFromDaily ? CipherCEngine.WeeklyByTime(d, data, gap, d.Start)
                                    : CipherCEngine.Detect(data, wTier, DssOverbought, DssOversold);
            if (weeklyFromDaily) CountFromNewest(w, data, d.Start);
            var y = yTier.Active ? CipherCEngine.YearlyFromPrice(data, years) : CipherCEngine.YearlyFromDates(data, Array.Empty<DateTime>());
            return new Tiers(clock, inBars, L, set > 0 ? 2 : own != null ? 1 : 0, years, yMin, yMax, dTier, wTier, yTier, d, w, y,
                weeklyFromDaily, weeklyFromDaily ? gap : double.NaN);
        }

        /// <summary>
        /// Moves a weekly run picked from the daily lows (<see cref="CipherCEngine.WeeklyByTime"/>)
        /// to count from its newest weekly low (<see cref="CipherCEngine.TierRun.NewestAt"/>) rather
        /// than the one before it, as the original counts: a weekly low is confirmed on the bar it
        /// becomes one, and the weekly window runs from it. The count's seed is not a low.
        /// </summary>
        internal static void CountFromNewest(CipherCEngine.TierRun w, ReadOnlySpan<Ohlcv> data, int seed)
        {
            int n = data.Length;
            if (w.NewestAt.Length != n) return;
            var last = new int[n];
            var confirmed = CipherCEngine.Nan(n); var updated = CipherCEngine.Nan(n);
            for (int t = 0; t < n; t++)
            {
                int now = w.NewestAt[t] == seed ? -1 : w.NewestAt[t];
                last[t] = now;
                if (now >= 0 && (t == 0 || now != last[t - 1]))
                {
                    confirmed[t] = data[now].Low;
                    updated[t] = w.Cancelled[t];
                }
            }
            w.LastLowAt = last; w.Confirmed = confirmed; w.Updated = updated;
        }

        /// <summary>
        /// The daily cycle's top so far on each bar (<see cref="TopSuffix"/> and the rest): the
        /// highest high since the newest confirmed daily low, where it came against the middle of
        /// the learned window, and the past cycles' tops on this chart, each known when its cycle's
        /// second low is confirmed (the finished cycle's top is <see cref="CipherCProvider.TranslatedTop"/>'s).
        /// </summary>
        private static void Tops(Dictionary<string, double[]> o, Func<string, double[]> A, ReadOnlySpan<Ohlcv> data,
            CipherCEngine.TierRun d, Learned learn, int n)
        {
            var high = new double[n];
            for (int t = 0; t < n; t++) high[t] = data[t].High;
            // The finished cycles: known on, the top's day, the day their next low was confirmed on, the top's side.
            var lows = new List<(int Bar, int At)>();
            for (int k = 0; k < d.Lows.Count; k++) if (d.ConfirmedAt[k] >= 0) lows.Add((d.Lows[k], d.ConfirmedAt[k]));
            lows.Sort();
            var past = new List<(int At, double TopDay, double ConfirmedDay, string Side)>();
            for (int k = 1; k < lows.Count; k++)
            {
                var (a, b) = (lows[k - 1].Bar, lows[k].Bar);
                var (top, side) = TranslatedTop(high, a, b);
                past.Add((Math.Max(lows[k - 1].At, lows[k].At), top - a + 1, Math.Max(b, lows[k].At) - a + 1, side));
            }
            past.Sort((x, y) => x.At.CompareTo(y.At));

            double[] topOut = A(CompDailyWindow + TopSuffix), dayOut = A(CompDailyWindow + TopDaySuffix), came = A(CompDailyWindow + TopCameSuffix),
                     waiting = A(CompDailyWindow + TopWaitingSuffix), leftOut = A(CompDailyWindow + TopLeftSuffix),
                     fails = A(CompDailyWindow + TopFailsSuffix), other = A(CompDailyWindow + TopFailsOtherSuffix),
                     lefts = A(CompDailyWindow + LeftTopsSuffix), mids = A(CompDailyWindow + MidTopsSuffix), rights = A(CompDailyWindow + RightTopsSuffix);
            int cycle = -1, tp = -1, failedFor = -1, next = 0, l = 0, m = 0, r = 0;
            for (int t = 0; t < n; t++)
            {
                for (; next < past.Count && past[next].At <= t; next++)
                    if (past[next].Side == CompLeftTop) l++; else if (past[next].Side == CompRightTop) r++; else m++;
                lefts[t] = l; mids[t] = m; rights[t] = r;
                if (!double.IsNaN(d.Failed[t])) failedFor = d.ClosedUnder[t];
                int a = d.LastLowAt[t];
                if (a < 0 || a == d.Start) continue;
                if (a != cycle) { cycle = a; tp = a; for (int i = a + 1; i <= t; i++) if (high[i] > high[tp]) tp = i; }
                else if (high[t] > high[tp]) tp = t;
                double day = t - a + 1, mid = (learn.Start[t] + learn.End[t]) / 2;
                topOut[t] = high[tp];
                dayOut[t] = tp - a + 1;
                if (learn.IsLearned[t])
                {
                    int c = 0, still = 0;
                    for (int k = 0; k < next; k++)
                    {
                        if (past[k].ConfirmedDay <= day) continue;
                        still++;
                        if (past[k].TopDay <= day) c++;
                    }
                    waiting[t] = still;
                    if (still >= MinWaiting) came[t] = Math.Round(100.0 * c / still);
                }
                if (day < 0.5 * mid) continue;
                bool left = 2 * (tp - a) < mid;
                leftOut[t] = left ? 1 : 0;
                if (failedFor == a) continue;
                foreach (var (from, to, onLeft, onRight) in TopFails)
                    if (day >= from * mid && day < to * mid) { fails[t] = left ? onLeft : onRight; other[t] = left ? onRight : onLeft; }
            }
        }

        /// <summary>
        /// The half-cycle low: in each daily cycle from a confirmed low <c>a</c>, the first bar
        /// <c>h</c> from 0.3 to 0.7 of the usual cycle in (the window's middle when <c>a</c> was
        /// confirmed) whose low is above <c>a</c>'s and the lowest for <c>k</c> bars each side
        /// (lower than every bar before it, no higher than every bar after; <c>k</c> = an eighth
        /// of the shortest daily cycle, at least 2). Known <c>k</c> bars on (or when <c>a</c> is
        /// confirmed), so it is a mark on <c>h</c> found later, and from then the next daily low is
        /// said with how much of the rise from the cycle's low to its top it gave back, which has
        /// told how often the cycle failed (<see cref="HalfFails"/>). Found in 69% of daily cycles
        /// on 31 daily charts. A next daily low projected from it held no more often than the
        /// learned window at the same span (59.5% against 58.4%), so none is said.
        /// </summary>
        private static void HalfLows(Func<string, double[]> A, ReadOnlySpan<Ohlcv> data, CipherCEngine.TierRun d, Learned learn, double L, int n)
        {
            int k = Math.Max(2, (int)Math.Round(L / HalfPivotShare, MidpointRounding.AwayFromZero));
            var low = new double[n];
            for (int t = 0; t < n; t++) low[t] = data[t].Low;
            double[] mark = A(CompHalfLow), lag = A(CompHalfLow + HalfFoundSuffix), price = A(CompDailyWindow + HalfSuffix), dayOut = A(CompDailyWindow + HalfDaySuffix),
                     gaveOut = A(CompDailyWindow + HalfGaveSuffix), fails = A(CompDailyWindow + HalfFailsSuffix);
            int cycle = -1, found = -1, last = -1, failedFor = -1;
            double gave = double.NaN;
            double mid = double.NaN;
            bool Pivot(int h)
            {
                if (h - k < 0 || h + k >= n || low[h] <= low[cycle]) return false;
                for (int j = h - k; j < h; j++) if (low[j] <= low[h]) return false;
                for (int j = h + 1; j <= h + k; j++) if (low[j] < low[h]) return false;
                return true;
            }
            for (int t = 0; t < n; t++)
            {
                if (!double.IsNaN(d.Failed[t])) failedFor = d.ClosedUnder[t];
                int a = d.LastLowAt[t];
                if (a < 0 || a == d.Start) { cycle = -1; continue; }
                if (a != cycle)
                {
                    cycle = a; found = -1; mid = (learn.Start[t] + learn.End[t]) / 2;
                    last = a + (int)Math.Floor(HalfTo * mid);
                    for (int h = Math.Max(a + (int)Math.Ceiling(HalfFrom * mid), a + k); h <= Math.Min(last, t - k) && found < 0; h++)
                        if (Pivot(h)) found = h;
                }
                else if (found < 0 && t - k <= last && t - k >= Math.Max(a + (int)Math.Ceiling(HalfFrom * mid), a + k) && Pivot(t - k)) found = t - k;
                if (found < 0) continue;
                if (double.IsNaN(mark[found]))
                {
                    lag[found] = t - found;
                    double top = data[a].High;
                    for (int i = a + 1; i <= found; i++) top = Math.Max(top, data[i].High);
                    gave = (top - low[found]) / (top - low[a]);
                }
                mark[found] = low[found];
                price[t] = low[found];
                dayOut[t] = found - a + 1;
                gaveOut[t] = gave;
                if (failedFor != a) fails[t] = HalfFails.First(b => gave >= b.From && gave < b.To).Fails;
            }
        }

        /// <summary>
        /// The stamps of the next <paramref name="count"/> bars past the last: on a daily chart the
        /// ones <see cref="CipherCFutureClock"/> says it would print (its trading days at its
        /// session's time, as Cipher C dates its bands); on any other, the last week's bar times
        /// again, a week on each time (a weekly chart's bar a week on), so an intraday chart keeps
        /// its sessions and weekends. Holidays and daylight saving are not read off an intraday chart.
        /// </summary>
        internal static List<DateTime> FutureStamps(Ohlcv[] bars, int count, bool everyDay)
        {
            var clock = CipherCEngine.MeasureBars(bars);
            if (Math.Abs(clock.BarsPerDay - 1) <= 1e-6) return new CipherCFutureClock(bars, everyDay).Future(count).ToList();
            var last = bars[^1].Date;
            var gaps = new List<double>();
            for (int i = Math.Max(1, bars.Length - 50); i < bars.Length; i++) gaps.Add((bars[i].Date - bars[i - 1].Date).TotalDays);
            gaps.Sort();
            double period = Math.Max(7, gaps.Count == 0 ? 7 : Math.Round(gaps[gaps.Count / 2]));
            var template = new List<TimeSpan>();
            for (int i = bars.Length - 1; i >= 0 && last - bars[i].Date < TimeSpan.FromDays(period); i--) template.Add(bars[i].Date - last);
            template.Reverse();   // offsets from the last bar, the oldest first, the last bar's own (0) last
            var r = new List<DateTime>(count);
            for (int week = 1; r.Count < count; week++)
                foreach (var off in template)
                    if (r.Count < count) r.Add(last + off + TimeSpan.FromDays(period * week));
            return r;
        }

        /// <summary>A cycle from bar <paramref name="a"/> to <paramref name="b"/> in a tier's units, both ends counted: the low's own day is day 1.</summary>
        internal static double Length(int a, int b, double barsPerUnit) => Math.Floor((b - a) / barsPerUnit + 1e-9) + 1;

        /// <summary>0 early, 1 due, 2 late.</summary>
        internal static double Phase(double day, double start, double end) => day < start ? 0 : day <= end ? 1 : 2;

        /// <summary>One tier's window on every bar, learned from the cycles that had ended by then.</summary>
        internal sealed class Learned
        {
            public double[] Start = Array.Empty<double>(), End = Array.Empty<double>();
            public int[] Samples = Array.Empty<int>(), Hits = Array.Empty<int>(), Scored = Array.Empty<int>();
            public bool[] IsLearned = Array.Empty<bool>();
            /// <summary>
            /// The cycles known on each bar, oldest known first: the bar each became known on, its
            /// length, and the day of it its next low was confirmed on (both in the tier's units,
            /// the first low's day 1).
            /// </summary>
            public List<(int At, double Length, double ConfirmedDay)> Known = new();

            /// <summary>
            /// Of the cycles known on bar <paramref name="t"/> still unconfirmed on day
            /// <paramref name="day"/> (their next low confirmed after it), how many had made that
            /// low by it, and how many there are. A plain share of past lengths would overstate it:
            /// a cycle whose low was confirmed is no longer being counted, so the cycles still
            /// counted on a late day lean long (on 46 daily charts, said 90%, came 72%).
            /// </summary>
            public (int Came, int Still) Bottomed(int t, double day)
            {
                int came = 0, still = 0;
                foreach (var (at, len, confirmedDay) in Known)
                {
                    if (at > t) break;
                    if (confirmedDay <= day) continue;
                    still++;
                    if (len <= day) came++;
                }
                return (came, still);
            }
        }

        /// <summary>
        /// A tier's learned window: each finished cycle's length, known on the bar its second low
        /// was confirmed (or the first's, if later); on each bar, once <paramref name="minSamples"/>
        /// are known, the window is the <paramref name="qFrom"/> to <paramref name="qTo"/> quantile
        /// of them (linear between ranks), rounded to whole units; before that,
        /// <paramref name="fallbackFrom"/> to <paramref name="fallbackTo"/> rounded. Each cycle is
        /// scored against the window on the bar its first low was confirmed, if that window was learned.
        /// </summary>
        internal static Learned Learn(CipherCEngine.TierRun run, int n, double barsPerUnit, int minSamples, double qFrom, double qTo,
            double fallbackFrom, double fallbackTo)
        {
            var r = new Learned
            {
                Start = new double[n], End = new double[n], Samples = new int[n], Hits = new int[n], Scored = new int[n], IsLearned = new bool[n],
            };
            // The lows that stand, with the bar each was confirmed on (a weekly candidate not yet one has none).
            var lows = new List<(int Bar, int At)>();
            for (int k = 0; k < run.Lows.Count; k++) if (run.ConfirmedAt[k] >= 0) lows.Add((run.Lows[k], run.ConfirmedAt[k]));
            lows.Sort();
            var cycles = new List<(int At, double Length, int Began, double ConfirmedDay)>();
            for (int k = 1; k < lows.Count; k++)
                cycles.Add((Math.Max(lows[k - 1].At, lows[k].At), Length(lows[k - 1].Bar, lows[k].Bar, barsPerUnit), lows[k - 1].At,
                    Length(lows[k - 1].Bar, Math.Max(lows[k].Bar, lows[k].At), barsPerUnit)));
            cycles.Sort((a, b) => a.At.CompareTo(b.At));
            foreach (var c in cycles) r.Known.Add((c.At, c.Length, c.ConfirmedDay));

            double fs = Math.Max(1, Math.Round(fallbackFrom)), fe = Math.Max(fs, Math.Round(fallbackTo));
            var sorted = new List<double>();
            int next = 0;
            double s = fs, e = fe;
            for (int t = 0; t < n; t++)
            {
                bool changed = false;
                while (next < cycles.Count && cycles[next].At <= t)
                {
                    int at = sorted.BinarySearch(cycles[next].Length);
                    sorted.Insert(at < 0 ? ~at : at, cycles[next].Length);
                    next++; changed = true;
                }
                if (changed && sorted.Count >= minSamples)
                {
                    s = Math.Max(1, Math.Round(Quantile(sorted, qFrom)));
                    e = Math.Max(s, Math.Round(Quantile(sorted, qTo)));
                }
                r.Start[t] = s; r.End[t] = e; r.Samples[t] = sorted.Count; r.IsLearned[t] = sorted.Count >= minSamples;
            }
            // The running hit rate: each cycle against the window it began with, counted from the bar it ended.
            var hit = new int[n]; var scored = new int[n];
            foreach (var c in cycles)
            {
                if (c.Began < 0 || c.Began >= n || c.At >= n || !r.IsLearned[c.Began]) continue;
                scored[c.At]++;
                if (c.Length >= r.Start[c.Began] && c.Length <= r.End[c.Began]) hit[c.At]++;
            }
            for (int t = 0, h = 0, sc = 0; t < n; t++) { h += hit[t]; sc += scored[t]; r.Hits[t] = h; r.Scored[t] = sc; }
            return r;
        }

        /// <summary>The <paramref name="q"/> quantile of sorted values, linear between ranks (numpy's default).</summary>
        internal static double Quantile(List<double> sorted, double q)
        {
            if (sorted.Count == 0) return double.NaN;
            double pos = q * (sorted.Count - 1);
            int lo = (int)Math.Floor(pos), hi = Math.Min(sorted.Count - 1, lo + 1);
            return sorted[lo] + (pos - lo) * (sorted[hi] - sorted[lo]);
        }

        /// <summary>
        /// Which lower cycle of a higher one each bar is in: 1 + the lower tier's lows after the
        /// higher tier's newest low that are confirmed by the bar.
        /// </summary>
        private static void Nest(double[] into, int[] higherLast, CipherCEngine.TierRun lower, bool active, int n)
        {
            if (!active) return;
            var lows = new List<(int Bar, int At)>();
            for (int k = 0; k < lower.Lows.Count; k++) if (lower.ConfirmedAt[k] >= 0) lows.Add((lower.Lows[k], lower.ConfirmedAt[k]));
            for (int t = 0; t < n; t++)
            {
                int from = higherLast[t];
                if (from < 0) continue;
                int c = 1;
                foreach (var (bar, at) in lows) if (bar > from && at <= t) c++;
                into[t] = c;
            }
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

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 132;

        public List<LevelDescriptor> GetDefaultLevels(string code) => new();

        // ── Speech ─────────────────────────────────────────────────────────────

        public static string PhaseWord(double phase) => phase >= 2 ? "late" : phase >= 1 ? "due" : "early";

        /// <summary>
        /// Where a cycle is, from its window's companions on a bar: "day 24 of the daily cycle,
        /// the low had come by now in 70% of 12 past cycles this far in unconfirmed, due: in its
        /// window, days 22 to 30".
        /// </summary>
        internal static string? PositionWords(string cycle, string unit, string units, Func<string, double> at, string comp)
        {
            double start = at(comp + StartSuffix), end = at(comp + EndSuffix), since = at(comp);
            if (double.IsNaN(start)) return null;
            if (double.IsNaN(since)) return $"no {cycle} low confirmed yet";
            double day = at(comp + DayOfSuffix) is var dd && !double.IsNaN(dd) ? dd : since + 1, samples = at(comp + SamplesSuffix), bottomed = at(comp + BottomedSuffix), waiting = at(comp + WaitingSuffix);
            bool years = comp == CompYearlyWindow && unit == "year";
            string s = years ? $"{Num(day)} years into the {cycle}" : $"{unit} {Num(day)} of the {cycle}";
            if (at(comp + LearnedSuffix) == 1)
                s += !double.IsNaN(bottomed)
                    ? $", the low had come by now in {Num(bottomed)}% of {Num(waiting)} past cycles this far in unconfirmed"
                    : $", {(waiting > 0 ? "only " + Num(waiting) : "no")} past {(waiting == 1 ? "cycle" : "cycles")} of {Num(samples)} ran this far unconfirmed";
            else
                s += double.IsNaN(samples) || samples == 0 ? ", no past cycles yet, so a default window" : $", only {Num(samples)} past {(samples == 1 ? "cycle" : "cycles")}, so a default window";
            string window = years ? $"{Num(start)} to {Num(end)} years" : start == end ? $"{unit} {Num(start)}" : $"{units} {Num(start)} to {Num(end)}";
            double phase = Phase(day, start, end);
            s += phase == 0 ? $", early: its window is {window}" : phase == 1 ? $", due: in its window, {window}" : $", late: past its window, {window}";
            if (at(comp + NestSuffix) is var nest && !double.IsNaN(nest))
                s += comp != CompWeeklyWindow ? $", weekly cycle {Num(nest)} of it"
                   : at(comp + NestHeldSuffix) == 1 ? $", daily cycle {Num(nest)} of it, a new weekly low drawn but not yet confirmed"
                   : $", daily cycle {Num(nest)} of it";
            return s;
        }

        /// <summary>"high, 88%": the Low is in word and share on the window <paramref name="comp"/>, or null where it has no low to read.</summary>
        internal static string? LowInWord(Func<string, double> at, string comp = CompDailyWindow)
        {
            double p = at(comp + LowInSuffix);
            return double.IsNaN(p) ? null : $"{CycleLowConfidence.Word(p)}, {Num(Math.Round(100 * p))}%";
        }

        /// <summary>
        /// "low is in: high, 88%; about 9 days to its confirmation; 14% above the low, up 3.2 average
        /// ranges; a 5% chance at random of going back to it first; confirming: a higher low's high
        /// broken, closes above the 10-day average, ...": the word and the facts it was read from. On
        /// the weekly and yearly windows, "weekly low is in: ..., for the low of 61,200; ..." (the low
        /// it reads need not be the held daily one); the yearly reading names no confirmations, as it
        /// does not read them. Off a daily chart the clock is in bars (the yearly one stays in days).
        /// With <paramref name="besideDaily"/> (said after the daily reading), a weekly reading of the
        /// same low as the daily one says so and leaves out what the daily one has just said: how far
        /// above the low and the confirmations.
        /// </summary>
        internal static string? LowInWords(Func<string, double> at, string comp = CompDailyWindow, bool besideDaily = false)
        {
            if (LowInWord(at, comp) is not { } word) return null;
            double V(string suffix) => at(comp + suffix);
            bool inBars = at(InBarsKey) == 1;
            var (day, days) = comp == CompYearlyWindow ? ("day", "days") : Unit(inBars, "day", "days");
            double left = V(LowInLeftSuffix), touch = V(LowInTouchSuffix);
            string chance = touch < 0.01 ? "under 1%" : $"{Num(Math.Round(100 * touch))}%";
            bool same = besideDaily && comp == CompWeeklyWindow && at(CompDailyWindow + LowInLowSuffix) is var dl && dl == V(LowInLowSuffix);
            string head = comp switch
            {
                CompWeeklyWindow when same => $"weekly low is in: {word}, the same low as the daily reading",
                CompWeeklyWindow => $"weekly low is in: {word}, for the low of {Price(V(LowInLowSuffix))}",
                CompYearlyWindow => $"yearly low is in: {word}, for the low of {Price(V(LowInLowSuffix))}",
                _ => $"low is in: {word}",
            };
            if (same) return $"{head}; about {Span(left, day, days)} to its weekly confirmation; a {chance} chance at random of going back to it first";
            string s = $"{head}; about {Span(left, day, days)} to its confirmation; {Num(V(LowInAboveSuffix))}% above the low, "
                     + $"up {Num(V(LowInRiseSuffix))} average ranges; a {chance} chance at random of going back to it first";
            if (comp == CompYearlyWindow) return s;
            var on = new List<string>();
            if (V(LowInStructureSuffix) > 0) on.Add("a higher low's high broken");
            if (V(LowInSmaSuffix) > 0) on.Add($"closes above the 10-{day} average");
            if (V(LowInDssSuffix) > 0) on.Add("the stochastic over its trigger");
            if (V(LowInSarSuffix) > 0) on.Add("the SAR under price");
            if (V(LowInBreakSuffix) > 0) on.Add("a close above the 10 before it");
            return s + "; " + (on.Count == 0 ? "no confirmations yet" : "confirming: " + string.Join(", ", on));
        }

        /// <summary>
        /// "confluence: mid, the Awesome Oscillator above zero, the close under this month's VWAP",
        /// while a daily low is held; "no volume, so no VWAP" on a chart without it.
        /// </summary>
        internal static string? ConfluenceWords(Func<string, double> at)
        {
            double g = at(CompDailyWindow + ConfluenceSuffix);
            if (double.IsNaN(g)) return null;
            double ao = at(CompDailyWindow + AoSuffix), vwap = at(CompDailyWindow + VwapSuffix);
            return $"confluence: {GradeWord(g)}, the Awesome Oscillator {(ao > 0 ? "above" : "not above")} zero, "
                 + (double.IsNaN(vwap) ? "no volume, so no VWAP" : $"the close {(vwap > 0 ? "above" : "not above")} this month's VWAP");
        }

        /// <summary>"a new daily low in the next 8 days: likely, 42%", on each bar after a confirmed daily low.</summary>
        internal static string? ExpectedWords(Func<string, double> at)
        {
            double p = at(CompDailyWindow + ExpectedSuffix), k = at(CompDailyWindow + ExpectedDaysSuffix);
            var (day, days) = Unit(at(InBarsKey) == 1, "day", "days");
            return double.IsNaN(p) ? null : $"a new daily low in the next {Span(k, day, days)}: {CycleLowConfidence.ExpectedWord(p)}, {Num(Math.Round(100 * p))}%";
        }

        /// <summary>
        /// "the next daily low's window opens November 3 2026 and closes November 14 2026", "the
        /// daily window is open, to November 14 2026", or "the daily window closed October 20
        /// 2026": the live window's dates, said on the last bar only (where they are known).
        /// </summary>
        internal static string? NextWords(string tier, Func<string, double> at, string comp)
        {
            double opens = at(DatesKey(comp, false)), closes = at(DatesKey(comp, true)), now = at(NowKey);
            if (double.IsNaN(opens) || double.IsNaN(closes) || double.IsNaN(now)) return null;
            double sec = at(BarSecondsKey);
            return opens > now ? $"the next {tier} low's window opens {DateWords(opens, sec)} and closes {DateWords(closes, sec)}"
                 : closes >= now ? $"the {tier} window is open, to {DateWords(closes, sec)}"
                 : $"the {tier} window closed {DateWords(closes, sec)}";
        }

        /// <summary>A stored date as said: the bar's own date on a daily or longer chart, the user's clock on an intraday one.</summary>
        internal static string DateWords(double oa, double barSeconds, string? format = null)
        {
            bool intraday = barSeconds < 86400;
            var d = SpeechTimeFormatter.ToBarDisplay(DateTime.SpecifyKind(DateTime.FromOADate(oa), DateTimeKind.Utc), (int)(double.IsNaN(barSeconds) ? 86400 : barSeconds));
            return d.ToString(format ?? (intraday ? "MMMM d, HH:mm" : SpeechTimeFormatter.LongDateFormat), CultureInfo.InvariantCulture);
        }

        /// <summary>The table's "Next low" cell: "Nov 3–Nov 14", "now–Nov 14", "closed Oct 20"; a year on the weekly and yearly.</summary>
        internal static string? NextCell(Func<string, double> at, string comp)
        {
            double opens = at(DatesKey(comp, false)), closes = at(DatesKey(comp, true)), now = at(NowKey), sec = at(BarSecondsKey);
            if (double.IsNaN(opens) || double.IsNaN(closes) || double.IsNaN(now)) return null;
            string f = comp == CompYearlyWindow ? "MMM yyyy" : sec < 86400 ? "MMM d HH:mm" : comp == CompWeeklyWindow ? "MMM d yyyy" : "MMM d";
            string D(double v) => DateWords(v, sec, f);
            return opens > now ? $"{D(opens)}–{D(closes)}" : closes >= now ? $"now–{D(closes)}" : $"closed {D(closes)}";
        }

        /// <summary>
        /// "top so far: 64,000 on day 9, before the cycle's middle (day 26 of about 52), left-translated so far;
        /// cycles this far in with the top this early went on to fail 35% of the time, against 18%
        /// with a later top; the top had come by this day in 75% of 12 past cycles this far in
        /// unconfirmed": the daily cycle's top so far, on each bar after a confirmed daily low.
        /// </summary>
        internal static string? TopWords(Func<string, double> at)
        {
            double top = at(CompDailyWindow + TopSuffix);
            if (double.IsNaN(top)) return null;
            var (day, days) = Unit(at(InBarsKey) == 1, "day", "days");
            string s = $"top so far: {Price(top)} on {day} {Num(at(CompDailyWindow + TopDaySuffix))}";
            double left = at(CompDailyWindow + TopLeftSuffix), mid = (at(CompDailyWindow + StartSuffix) + at(CompDailyWindow + EndSuffix)) / 2;
            if (!double.IsNaN(left))
                s += $", {(left == 1 ? "before" : "after")} the cycle's middle ({day} {Num(mid / 2)} of about {Num(mid)}), {(left == 1 ? "left" : "right")}-translated so far";
            if (at(CompDailyWindow + TopFailsSuffix) is var f && !double.IsNaN(f))
                s += $"; cycles this far in with the top this {(left == 1 ? "early" : "late")} went on to fail {Num(f)}% of the time, "
                   + $"against {Num(at(CompDailyWindow + TopFailsOtherSuffix))}% with {(left == 1 ? "a later" : "an earlier")} top";
            if (at(CompDailyWindow + TopCameSuffix) is var c && !double.IsNaN(c))
                s += $"; the top had come by this {day} in {Num(c)}% of {Num(at(CompDailyWindow + TopWaitingSuffix))} past cycles this far in unconfirmed";
            return s;
        }

        /// <summary>
        /// "half-cycle low: 61,200.00 on day 24, giving back 82% of the rise before it; cycles
        /// whose half-cycle low gave back 80% or more went on to fail 49% of the time, against 30%
        /// of all": from the bar the half-cycle low is found until the next daily low; the failure
        /// share left out once the cycle has failed.
        /// </summary>
        internal static string? HalfWords(Func<string, double> at)
        {
            double price = at(CompDailyWindow + HalfSuffix);
            if (double.IsNaN(price)) return null;
            var (day, _) = Unit(at(InBarsKey) == 1, "day", "days");
            double gave = at(CompDailyWindow + HalfGaveSuffix);
            string s = $"half-cycle low: {Price(price)} on {day} {Num(at(CompDailyWindow + HalfDaySuffix))}, giving back {Num(Math.Round(100 * gave))}% of the rise before it";
            if (at(CompDailyWindow + HalfFailsSuffix) is var f && !double.IsNaN(f))
            {
                var b = HalfFails.First(x => x.Fails == f);
                string band = double.IsInfinity(b.To) ? $"{Num(100 * b.From)}% or more" : b.From == 0 ? $"under {Num(100 * b.To)}%" : $"{Num(100 * b.From)} to {Num(100 * b.To)}%";
                s += $"; cycles whose half-cycle low gave back {band} went on to fail {Num(f)}% of the time, against {Num(HalfFailsBase)}% of all";
            }
            return s;
        }

        /// <summary>The table's "Half-cycle low" cell: "day 24, gave 82%, fails 49%".</summary>
        internal static string? HalfCell(Func<string, double> at)
        {
            double price = at(CompDailyWindow + HalfSuffix);
            if (double.IsNaN(price)) return null;
            string s = $"{(at(InBarsKey) == 1 ? "bar" : "day")} {Num(at(CompDailyWindow + HalfDaySuffix))}, gave {Num(Math.Round(100 * at(CompDailyWindow + HalfGaveSuffix)))}%";
            if (at(CompDailyWindow + HalfFailsSuffix) is var f && !double.IsNaN(f)) s += $", fails {Num(f)}%";
            return s;
        }

        /// <summary>"this chart's daily tops: 40 left, 2 mid, 61 right": its finished daily cycles by translation, known by the bar.</summary>
        internal static string? TopCountWords(Func<string, double> at)
        {
            double l = at(CompDailyWindow + LeftTopsSuffix), m = at(CompDailyWindow + MidTopsSuffix), r = at(CompDailyWindow + RightTopsSuffix);
            return double.IsNaN(l) || l + m + r == 0 ? null : $"this chart's daily tops: {Num(l)} left, {Num(m)} mid, {Num(r)} right";
        }

        /// <summary>After a failed daily cycle: what it has meant for the next daily low.</summary>
        internal static string FailedWords =>
            $"so a weekly low is likely next: after a failed daily cycle the next daily low was a weekly low {Num(FailedWeeklyNext)}% of the time, against {Num(HeldWeeklyNext)}% after one that held";

        /// <summary>"the daily window has held 14 of 24 cycles": each cycle against the window it began with.</summary>
        internal static string? HitWords(string tier, Func<string, double> at, string comp)
        {
            double hits = at(comp + HitsSuffix), scored = at(comp + ScoredSuffix);
            if (double.IsNaN(scored) || double.IsNaN(at(comp + StartSuffix))) return null;
            if (scored == 0) return $"no {tier} cycle scored against a learned window yet";
            return $"the {tier} window has held {Num(hits)} of {Num(scored)} cycles, {Num(Math.Round(100 * hits / scored))}%";
        }

        /// <summary>
        /// The cycle's state on a bar in words: each tier's position against its learned window,
        /// the unconfirmed counts, a confirmation's confluence, a cancellation or failure that still
        /// stands; with <paramref name="barDate"/> (the focused series), the windows' hit rates and
        /// the daily cycle counted with.
        /// </summary>
        public static IReadOnlyList<string> StateWords(Func<string, double[]?> component, int index, DateTime? barDate = null)
        {
            var words = new List<string>();
            double At(string name) => component(name) is { } a && index >= 0 && index < a.Length ? a[index] : double.NaN;
            bool inBars = At(InBarsKey) == 1;
            var (day, days) = Unit(inBars, "day", "days");
            var (week, weeks) = Unit(inBars, "week", "weeks");
            var (year, yearsW) = Unit(inBars, "year", "years");
            foreach (var (cycle, u, us, comp) in new[]
                     {
                         ("daily cycle", day, days, CompDailyWindow), ("weekly cycle", week, weeks, CompWeeklyWindow),
                         ("yearly cycle", year, yearsW, CompYearlyWindow),
                     })
                if (PositionWords(cycle, u, us, At, comp) is { } s) words.Add(s);
            foreach (var (tier, comp) in new[] { ("daily", CompDailyWindow), ("weekly", CompWeeklyWindow), ("yearly", CompYearlyWindow) })
                if (NextWords(tier, At, comp) is { } next) words.Add(next);
            if (TopWords(At) is { } top) words.Add(top);
            if (HalfWords(At) is { } half) words.Add(half);
            if (HeldWords("daily", day, days, At(HeldDaysKey)) is { } heldDaily) words.Add(heldDaily);
            if (LowInWords(At) is { } lowIn) words.Add(lowIn);
            if (ConfluenceWords(At) is { } confluence) words.Add(confluence);
            if (ExpectedWords(At) is { } expected) words.Add(expected);
            if (HeldWords("weekly", week, weeks, At(HeldWeeksKey)) is { } heldWeekly) words.Add(heldWeekly);
            foreach (var comp in new[] { CompWeeklyWindow, CompYearlyWindow })
                if (LowInWords(At, comp, besideDaily: true) is { } tierLowIn) words.Add(tierLowIn);

            foreach (var (comp, tier) in new[] { (CompDclConfirmed, "daily"), (CompWclConfirmed, "weekly"), (CompYclConfirmed, "yearly") })
                if (!double.IsNaN(At(comp))) words.Add($"{tier} cycle low confirmed, {ConfluenceWord(At(comp))} confluence");

            string events = At(EventsTierKey) switch { 2 => "weekly", 3 => "yearly", _ => "daily" };
            var (unit, units) = Unit(inBars, events == "daily" ? "day" : "week", events == "daily" ? "days" : "weeks");
            if (!double.IsNaN(At(CompCancelled))) words.Add($"{events} pending cycle low cancelled");
            else if (At(CompCancelled + SinceSuffix) is var cs && !double.IsNaN(cs))
                words.Add($"the last {events} pending cycle low was cancelled {Span(cs, unit, units)} ago");
            string failedWhy = events == "daily" ? ", " + FailedWords : "";
            if (!double.IsNaN(At(CompFailed))) words.Add($"{events} cycle failed{failedWhy}");
            else if (At(CompFailed + SinceSuffix) is var fs && !double.IsNaN(fs))
                words.Add($"the {events} cycle failed {Span(fs, unit, units)} ago{failedWhy}");

            if (barDate != null)
            {
                foreach (var (tier, comp) in new[] { ("daily", CompDailyWindow), ("weekly", CompWeeklyWindow), ("yearly", CompYearlyWindow) })
                    if (HitWords(tier, At, comp) is { } h) words.Add(h);
                if (TopCountWords(At) is { } tops) words.Add(tops);
                double len = At(LengthKey);
                if (!double.IsNaN(len))
                    words.Add($"daily cycles counted from {Span(len, day, days)}, " + At(LengthSourceKey) switch
                    {
                        1 => "this symbol's shortest", 2 => "set by hand", _ => "the default for a symbol with no count of its own",
                    } + (inBars ? "; not a daily chart, so its days are counted as bars" : ""));
            }
            return words;
        }

        /// <summary>
        /// The table in the price pane's bottom right corner, at one bar: for each tier its count,
        /// phase, the share of past cycles still unconfirmed by now whose low had come, its window, its hit rate and the lower
        /// cycle it is in; and the days since the unconfirmed daily and weekly lows.
        /// </summary>
        public static IReadOnlyList<IReadOnlyList<Rendering.TableCell>> TableRows(Func<string, double[]?> component, int index,
            IReadOnlyDictionary<string, object>? parameters = null)
        {
            double At(string name) => component(name) is { } a && index >= 0 && index < a.Length ? a[index] : double.NaN;
            if (double.IsNaN(At(LengthKey)) || !IndicatorParams.GetBool(parameters, ParamTable, true)) return Array.Empty<IReadOnlyList<Rendering.TableCell>>();
            bool inBars = At(InBarsKey) == 1;
            Rendering.TableCell C(string text, string? fill = null, int size = 0) => new(text, fill, 1, size);
            const string Dash = "—";
            var tiers = new[] { (CompDailyWindow, inBars ? "Bar" : "Day"), (CompWeeklyWindow, inBars ? "Bar" : "Week"), (CompYearlyWindow, inBars ? "Bar" : "Year") };
            string Cell(string comp, Func<string, string?> f) => double.IsNaN(At(comp + StartSuffix)) ? "" : f(comp) ?? Dash;
            IReadOnlyList<Rendering.TableCell> Row(string label, Func<string, string?> f) =>
                new[] { C(label, TableLabel, -1) }.Concat(tiers.Select(x => C(Cell(x.Item1, f), TableValue))).ToArray();
            string Held(double v) => double.IsNaN(v) ? Dash : v < 0 ? "✅" : Num(v);
            return new List<IReadOnlyList<Rendering.TableCell>>
            {
                new[] { C("Cycle Position", TableCountHead, 1), C("Daily", TableDark, 1), C("Weekly", TableDark, 1), C("Yearly", TableDark, 1) },
                Row("Count", c => double.IsNaN(At(c)) ? null : $"{tiers.First(x => x.Item1 == c).Item2.ToLowerInvariant()} {Num(At(c) + 1)}"),
                Row("Phase", c => double.IsNaN(At(c + PhaseSuffix)) ? null : PhaseWord(At(c + PhaseSuffix)).ToUpperInvariant()),
                Row("Low come by now", c => At(c + LearnedSuffix) != 1 || double.IsNaN(At(c + BottomedSuffix)) ? null : $"{Num(At(c + BottomedSuffix))}% of {Num(At(c + WaitingSuffix))}"),
                Row("Window", c => (At(c + LearnedSuffix) == 1 ? "" : "default ") + (At(c + StartSuffix) == At(c + EndSuffix) ? Num(At(c + StartSuffix)) : $"{Num(At(c + StartSuffix))}–{Num(At(c + EndSuffix))}")),
                Row("Window held", c => At(c + ScoredSuffix) is var s && !(s > 0) ? null : $"{Num(At(c + HitsSuffix))} of {Num(s)}"),
                Row("Lower cycle", c => double.IsNaN(At(c + NestSuffix)) ? null
                    : $"{(c == CompWeeklyWindow ? "daily" : "weekly")} {Num(At(c + NestSuffix))}{(c == CompWeeklyWindow && At(c + NestHeldSuffix) == 1 ? ", new drawn" : "")}"),
                Row("Next low", c => NextCell(At, c)),
                new[] { C("Top so far", TableLabel, -1), C(double.IsNaN(At(CompDailyWindow + StartSuffix)) ? "" : TopCell(At) ?? Dash, TableValue), C("", null), C("", null) },
                new[] { C("Half-cycle low", TableLabel, -1), C(double.IsNaN(At(CompDailyWindow + StartSuffix)) ? "" : HalfCell(At) ?? Dash, TableValue), C("", null), C("", null) },
                new[] { C("Unconfirmed low", TableLabel, -1), C(Held(At(HeldDaysKey)), TableValue), C(Held(At(HeldWeeksKey)), TableValue), C("", null) },
                new[]
                {
                    C("Low is in", TableLabel, -1), C(LowInWord(At) ?? (double.IsNaN(At(CompDailyWindow + ExpectedSuffix)) ? "" : Dash), TableValue),
                    C(component(CompWeeklyWindow + LowInSuffix) == null ? "" : LowInWord(At, CompWeeklyWindow) ?? Dash, TableValue),
                    C(component(CompYearlyWindow + LowInSuffix) == null ? "" : LowInWord(At, CompYearlyWindow) ?? Dash, TableValue),
                },
                new[] { C("Confluence", TableLabel, -1), C(double.IsNaN(At(CompDailyWindow + StartSuffix)) ? ""
                    : double.IsNaN(At(CompDailyWindow + ConfluenceSuffix)) ? Dash : GradeWord(At(CompDailyWindow + ConfluenceSuffix)).ToUpperInvariant(), TableValue), C("", null), C("", null) },
                new[] { C("Low expected", TableLabel, -1), C(double.IsNaN(At(CompDailyWindow + ExpectedSuffix)) ? ""
                    : $"{CycleLowConfidence.ExpectedWord(At(CompDailyWindow + ExpectedSuffix))}, {Span(At(CompDailyWindow + ExpectedDaysSuffix), inBars ? "bar" : "day", inBars ? "bars" : "days")}", TableValue), C("", null), C("", null) },
            };
        }

        /// <summary>The table's "Top so far" cell: "day 9, LEFT, came 75%"; "failed" once the cycle has, with the next low's weekly share.</summary>
        internal static string? TopCell(Func<string, double> at)
        {
            double top = at(CompDailyWindow + TopSuffix);
            if (double.IsNaN(top)) return null;
            string s = $"{(at(InBarsKey) == 1 ? "bar" : "day")} {Num(at(CompDailyWindow + TopDaySuffix))}";
            if (at(CompDailyWindow + TopLeftSuffix) is var l && !double.IsNaN(l)) s += l == 1 ? ", LEFT" : ", RIGHT";
            if (at(CompDailyWindow + TopCameSuffix) is var c && !double.IsNaN(c)) s += $", came {Num(c)}%";
            return s;
        }

        private readonly CipherCProvider _drawn = new();

        /// <summary>
        /// What each component says on a bar: the lows, confirmations, events and tops as Cipher C
        /// says them; the windows, the held low and the pending low in this indicator's terms.
        /// </summary>
        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            double At(string name) => allComponentData.TryGetValue(name, out var a) && dataIndex >= 0 && dataIndex < a.Length ? a[dataIndex] : double.NaN;
            double Last(string name) => allComponentData.TryGetValue(name, out var a) && a.Length > 0 ? a[^1] : double.NaN;
            bool inBars = At(InBarsKey) == 1;
            var (day, days) = Unit(inBars, "day", "days");
            // The weekly and yearly windows say their low is in reading after their position.
            static string? With(string? s, string? more) => more == null ? s : s == null ? more : s + "; " + more;
            string? WithLowIn(string? position, string comp) =>
                LowInWords(At, comp) is not { } lowIn ? position : position == null ? lowIn : position + "; " + lowIn;
            switch (componentName)
            {
                case CompDailyWindow: return With(With(With(PositionWords("daily cycle", day, days, At, CompDailyWindow), NextWords("daily", At, CompDailyWindow)), TopWords(At)), HalfWords(At));
                case CompHalfLow when !double.IsNaN(value):
                    return $"Half-cycle low: the low of {Price(value)}, {A(At(CompDailyWindow) + 1)} {Num(At(CompDailyWindow) + 1)} {day} into the daily cycle, found {Span(At(CompHalfLow + HalfFoundSuffix), day, days)} later.";
                case CompWeeklyWindow: { var (u, us) = Unit(inBars, "week", "weeks"); return WithLowIn(With(PositionWords("weekly cycle", u, us, At, CompWeeklyWindow), NextWords("weekly", At, CompWeeklyWindow)), CompWeeklyWindow); }
                case CompYearlyWindow: { var (u, us) = Unit(inBars, "year", "years"); return WithLowIn(With(PositionWords("yearly cycle", u, us, At, CompYearlyWindow), NextWords("yearly", At, CompYearlyWindow)), CompYearlyWindow); }
                case CompFailed when At(EventsTierKey) == 1 && !double.IsNaN(value):
                {
                    string? s = _drawn.GetComponentSpeech(componentName, value, bar, allComponentData, dataIndex);
                    return s == null ? null : s.TrimEnd('.') + ", " + FailedWords + ".";
                }
                case CompDcl when double.IsNaN(value):
                {
                    double mark = At(CompDcl + ComponentCompanions.Mark);
                    if (double.IsNaN(mark)) return null;
                    int badge = double.IsNaN(At(CompDcl + ComponentCompanions.Badge)) ? 0 : (int)At(CompDcl + ComponentCompanions.Badge);
                    if ((badge & ComponentCompanions.BadgeCross) != 0)
                        return $"Daily cycle low dropped: the low of {Price(mark)}, undercut before it was confirmed.";
                    // The held low: the cycle it would end, against the window on the last bar.
                    double length = At(CompDailyWindow) + 1, word = At(CompDcl + ComponentCompanions.BadgeWord), held = Last(HeldDaysKey);
                    string s = $"Daily cycle low, unconfirmed: the low of {Price(mark)}";
                    if (!double.IsNaN(length)) s += $", it would end {A(length)} {Num(length)} {day} cycle";
                    if (!double.IsNaN(word) && word != StartWord)
                        s += $", {PhaseWord(word)} against its window of {days} {Num(Last(CompDailyWindow + StartSuffix))} to {Num(Last(CompDailyWindow + EndSuffix))}";
                    if (!double.IsNaN(held) && held >= 0) s += $", {Span(held, day, days)} since it";
                    s += ".";
                    // Its Low is in, read on the last bar as its days since are.
                    if (LowInWords(Last) is { } lowIn) s += " " + char.ToUpperInvariant(lowIn[0]) + lowIn[1..] + ".";
                    if (ConfluenceWords(Last) is { } confluence) s += " " + char.ToUpperInvariant(confluence[0]) + confluence[1..] + ".";
                    if ((badge & ComponentCompanions.BadgeRefresh) != 0) s += " A low was dropped since the last confirmed one.";
                    return s;
                }
                case CompPending when At(EventsTierKey) == 1:
                {
                    double since = At(CompDailyWindow), lowest = At(CompPending + LowestSuffix), ago = At(CompPending + LowestAgoSuffix);
                    double phase = At(CompPending + GradeSuffix), replaced = At(CompPending + ReplacedSuffix);
                    string s = "Daily cycle low pending" + (double.IsNaN(since) ? "" : $" {Span(since, day, days)} after the last cycle low")
                             + (double.IsNaN(phase) ? "" : $", {PhaseWord(phase)}")
                             + (double.IsNaN(lowest) ? "." : $": the low of {Price(lowest)}" + (double.IsNaN(ago) ? "." : $", {Span(ago, day, days)} ago."));
                    if (!double.IsNaN(replaced)) s += $" It replaces the dropped low of {Price(replaced)}.";
                    return s;
                }
                default:
                    return _drawn.GetComponentSpeech(componentName, value, bar, allComponentData, dataIndex);
            }
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

        // ── Helpers ────────────────────────────────────────────────────────────

        private static (string, string) Unit(bool inBars, string unit, string units) => inBars ? ("bar", "bars") : (unit, units);
        private static string Span(double v, string unit, string units) => $"{Num(v)} {(Math.Abs(v - 1) < 1e-9 ? unit : units)}";
        private static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        private static string Price(double v) => SpeechPriceFormatter.FormatPrice(v);
        private static string? Str(Dictionary<string, object> p, string k) => p.TryGetValue(k, out var v) ? v?.ToString() : null;

        private static double Dbl(Dictionary<string, object> p, string k, double def)
        {
            if (!p.TryGetValue(k, out var v) || v == null) return def;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture) is var x && !double.IsNaN(x) ? x : def; } catch { return def; }
        }
    }
}
