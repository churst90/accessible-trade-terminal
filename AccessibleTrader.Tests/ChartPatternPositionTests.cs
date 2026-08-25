using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// The positional half of chart-pattern announcement: where a formation starts, where it ends, what
/// happened to it, and how the terminal steps between those points.
///
/// <para>
/// These are the properties that came out of using the feature rather than reading its code, and
/// each names a way the first version was misleading rather than wrong:
/// </para>
/// <list type="bullet">
///   <item>A formation was announced when the cursor entered it, with no way to tell from which
///         side — so "walking into the start of this" and "walking back into the end of this"
///         sounded identical.</item>
///   <item>"Completed" said nothing about whether the pattern had worked or failed, which is the
///         only thing a listener wants to know at that moment.</item>
///   <item>A pattern that never confirmed had no ending at all: it stayed forming forever, or was
///         credited with an unrelated break hundreds of bars later.</item>
/// </list>
/// </summary>
public class ChartPatternPositionTests
{
    private static string Fmt(double v) => v.ToString("0.####");

    private static ChartPattern P(
        ChartPatternKind kind = ChartPatternKind.DoubleTop,
        ChartPatternState state = ChartPatternState.Forming,
        int start = 10, int end = 40, int known = 45,
        double trigger = 100, int? completed = null, int? expires = null,
        bool breaksBelow = true, double? target = null)
        => new(kind, state, start, end, known, trigger, DateTime.Today, DateTime.Today,
               completed, expires, breaksBelow, target);

    /// <summary>
    /// Price rotating between a flat 100 and a flat 110, four turns, then leaving. Single-point
    /// bars so the boundaries are exactly the numbers the assertions name.
    /// </summary>
    private static List<Ohlcv> RangeSeries(bool breakUp = false)
    {
        var path = new List<double> { 105 };
        void Ramp(double from, double to, int steps)
        {
            for (int i = 1; i <= steps; i++) path.Add(from + (to - from) * i / steps);
        }

        for (int cycle = 0; cycle < 3; cycle++)
        {
            Ramp(path[^1], 110, 12);
            Ramp(110, 100, 12);
        }
        Ramp(100, breakUp ? 130 : 70, 25);
        for (int i = 0; i < 60; i++) path.Add(breakUp ? 130 : 70);

        var bars = new List<Ohlcv>(path.Count);
        var t = new DateTime(2020, 1, 1);
        for (int i = 0; i < path.Count; i++)
            bars.Add(new Ohlcv
            {
                Date = t.AddDays(i),
                Open = path[i], Close = path[i], High = path[i], Low = path[i],
                Volume = 1000
            });
        return bars;
    }

    // ── Expiry: the third outcome ───────────────────────────────────────────────

    /// <summary>
    /// A formation's life ends a formation-length after it became knowable. Anchoring the decay to
    /// the pattern's own span rather than a fixed bar count is what keeps it proportionate on a
    /// 1-minute chart and a weekly one at the same time.
    /// </summary>
    [Fact]
    public void RelevanceEndsAFormationLengthAfterItBecameKnowable()
    {
        Assert.Equal(75, P(start: 10, end: 40, known: 45).RelevanceEndsAt);   // 45 + 30
        Assert.Equal(20, P(start: 5, end: 15, known: 10).RelevanceEndsAt);    // 10 + 10
    }

    /// <summary>
    /// A break bar always wins over the expiry bar. The story ends when price resolved it, not when
    /// the clock would have.
    /// </summary>
    [Fact]
    public void AConfirmedPatternResolvesAtItsBreakBarNotItsExpiry()
    {
        var p = P(state: ChartPatternState.Completed, completed: 52, expires: 75);
        Assert.Equal(52, p.ResolvesAt);
    }

    /// <summary>
    /// An explicit expiry from the detector is honoured over the fallback rule, so the two can
    /// never disagree about the same pattern.
    /// </summary>
    [Fact]
    public void AnExplicitExpiryWins()
        => Assert.Equal(60, P(start: 10, end: 40, known: 45, expires: 60).RelevanceEndsAt);

    // ── The detector's own resolution ───────────────────────────────────────────

    /// <summary>
    /// The bound on the resolve scan is what makes the three states mean anything.
    ///
    /// <para>
    /// Unbounded, a double top whose neckline broke two hundred bars later was reported as that
    /// double top completing — an unrelated move wearing the pattern's name. It also meant nothing
    /// could ever be reported as having failed to confirm, because every pattern was either
    /// Completed or waiting forever. This walks a real series and asserts both halves.
    /// </para>
    /// </summary>
    [Fact]
    public void NoPatternIsCreditedWithABreakAfterItExpired()
    {
        var bars = RandomWalk(900, seed: 7);
        var found = new ChartPatternDetector(new SwingStructureAnalyzer()).Detect(bars);
        Assert.NotEmpty(found);

        foreach (var p in found.Where(x => x.CompletedAtIndex is not null))
            Assert.True(p.CompletedAtIndex <= p.RelevanceEndsAt,
                $"{p.Kind} was credited with a break at {p.CompletedAtIndex} but expired at {p.RelevanceEndsAt}");
    }

    /// <summary>
    /// Expired must actually occur on real data, or the state is decorative and the wording that
    /// depends on it is never exercised.
    /// </summary>
    [Fact]
    public void ExpiredPatternsAreProducedOnRealData()
    {
        var bars = RandomWalk(900, seed: 7);
        var found = new ChartPatternDetector(new SwingStructureAnalyzer()).Detect(bars);

        Assert.Contains(found, p => p.State == ChartPatternState.Expired);
    }

    /// <summary>
    /// Forming means the verdict is genuinely not in — the series has not yet reached the expiry
    /// bar. Anything still called Forming deep in history would be a stale decision presented as a
    /// live one, which is the specific dishonesty the state exists to avoid.
    /// </summary>
    [Fact]
    public void FormingOnlySurvivesNearTheRightHandEdge()
    {
        var bars = RandomWalk(900, seed: 7);
        var found = new ChartPatternDetector(new SwingStructureAnalyzer()).Detect(bars);

        foreach (var p in found.Where(x => x.State == ChartPatternState.Forming))
            Assert.True(p.RelevanceEndsAt >= bars.Count - 1,
                $"{p.Kind} is still Forming but expired at {p.RelevanceEndsAt} of {bars.Count} bars");
    }

    // ── Measured targets ────────────────────────────────────────────────────────

    /// <summary>
    /// The measured move is the formation's height projected from the trigger, in the direction the
    /// break goes. It is geometry, and the only thing worth pinning is that the arithmetic runs the
    /// right way — a target on the wrong side of the trigger would be actively dangerous, since it
    /// is a number a user might put into an order ticket.
    /// </summary>
    [Fact]
    public void EveryTargetSitsOnTheBreakSideOfItsTrigger()
    {
        var bars = RandomWalk(900, seed: 11);
        var found = new ChartPatternDetector(new SwingStructureAnalyzer()).Detect(bars);
        Assert.NotEmpty(found);

        foreach (var p in found)
        {
            Assert.True(p.MeasuredTarget.HasValue, $"{p.Kind} carries no measured target");
            double t = p.MeasuredTarget!.Value;
            if (p.BreaksBelow)
                Assert.True(t < p.TriggerLevel, $"{p.Kind} breaks down but targets {t} above trigger {p.TriggerLevel}");
            else
                Assert.True(t > p.TriggerLevel, $"{p.Kind} breaks up but targets {t} below trigger {p.TriggerLevel}");
        }
    }

    /// <summary>
    /// Worked by hand, because a projection is the one place an off-by-one-side error looks
    /// plausible in every direction. Twin highs at 110 over a neckline at 100 is a 10-point
    /// formation, so the convention's target is 90.
    /// </summary>
    [Fact]
    public void ADoubleTopProjectsItsHeightBelowTheNeckline()
    {
        var bars = DoubleTopSeries();
        var found = new ChartPatternDetector(new SwingStructureAnalyzer()).Detect(bars)
            .Where(p => p.Kind == ChartPatternKind.DoubleTop).ToList();

        Assert.NotEmpty(found);
        var p = found[0];
        double height = 110 - p.TriggerLevel;
        Assert.Equal(p.TriggerLevel - height, p.MeasuredTarget!.Value, 3);
    }

    /// <summary>
    /// A target at or below zero is dropped rather than spoken.
    ///
    /// <para>
    /// The projection is a subtraction, so on a low-priced instrument a tall formation puts the
    /// conventional target underneath zero — measuring live snapshots produced "measured target
    /// -0.0001" on a sub-cent coin. A negative price is not a conservative estimate, it is a number
    /// that cannot happen, and it is one a user might type into an order ticket.
    /// </para>
    /// </summary>
    [Fact]
    public void AnImpossibleTargetIsNotSpoken()
    {
        var p = P(trigger: 0.0049, target: -0.0001);
        string s = ChartPatternNarrator.Describe(p, Fmt);

        Assert.DoesNotContain("measured target", s);
        Assert.DoesNotContain("-", s);
        Assert.Contains("0.0049", s);   // the trigger, which is real, is still there
    }

    // ── No lookahead in the WORDING ─────────────────────────────────────────────

    /// <summary>
    /// The bug this pins, in one sentence: walking onto the left edge of a formation used to
    /// announce "start of double top: price closed below the neckline", stating a break that had
    /// not happened yet.
    ///
    /// <para>
    /// A pattern record carries the outcome the whole series eventually produced, and the narrator
    /// was reading it verbatim at every bar the formation overlapped. Every unit test passed —
    /// each sentence was individually well-formed — and it was only visible when the narration was
    /// measured across real bars. It is the same class of defect as the Cipher SR proximity
    /// artifact: a level anchored to something only knowable later.
    /// </para>
    /// </summary>
    [Fact]
    public void AConfirmedPatternIsStillOnlyFormingBeforeItsBreakBar()
    {
        var p = P(state: ChartPatternState.Completed, known: 45, completed: 60, expires: 75,
                  trigger: 42100, target: 39400);

        string atStart = ChartPatternNarrator.Describe(ChartPatternNarrator.AsOf(p, 45), Fmt);
        Assert.Contains("Possible", atStart);
        Assert.Contains("forming", atStart);
        Assert.DoesNotContain("closed below", atStart);

        string atBreak = ChartPatternNarrator.Describe(ChartPatternNarrator.AsOf(p, 60), Fmt);
        Assert.Contains("closed below", atBreak);
    }

    /// <summary>An expired pattern has not failed yet either — not until the bar it aged out on.</summary>
    [Fact]
    public void AnExpiredPatternIsStillFormingBeforeItsExpiryBar()
    {
        var p = P(state: ChartPatternState.Expired, known: 45, expires: 75, trigger: 42100);

        Assert.Contains("forming", ChartPatternNarrator.Describe(ChartPatternNarrator.AsOf(p, 50), Fmt));
        Assert.Contains("did not confirm", ChartPatternNarrator.Describe(ChartPatternNarrator.AsOf(p, 75), Fmt));
    }

    /// <summary>
    /// The projection is applied by <see cref="ChartPatternNarrator.AtBar"/> itself, so no caller
    /// can forget it. Three features consume this and honesty must not depend on each of them
    /// remembering to ask.
    /// </summary>
    [Fact]
    public void AtBarProjectsEveryPatternToTheRequestedBar()
    {
        var all = new List<ChartPattern>
        {
            P(state: ChartPatternState.Completed, known: 45, completed: 60, expires: 75),
        };

        Assert.Equal(ChartPatternState.Forming, ChartPatternNarrator.AtBar(all, 50)[0].State);
        Assert.Equal(ChartPatternState.Completed, ChartPatternNarrator.AtBar(all, 60)[0].State);
    }

    /// <summary>
    /// Identity must survive the projection. Diffing the records themselves would report the same
    /// formation as newly entered on the bar it resolved — announcing "start of" at the finish line.
    /// </summary>
    [Fact]
    public void ProjectionPreservesIdentityEvenThoughItChangesTheRecord()
    {
        var p = P(state: ChartPatternState.Completed, known: 45, completed: 60, expires: 75);

        var early = ChartPatternNarrator.AsOf(p, 50);
        var late  = ChartPatternNarrator.AsOf(p, 60);

        Assert.NotEqual(early, late);        // different records…
        Assert.Equal(early.Key, late.Key);   // …same formation
    }

    // ── Ranges: the one formation with two live levels ──────────────────────────

    /// <summary>
    /// Flat top against flat bottom used to fall through the triangle grid and be reported as
    /// nothing at all — so the single most common state a market is in produced silence, which is
    /// the worst possible gap in a feature whose job is to say what the chart is doing.
    /// </summary>
    [Fact]
    public void AHorizontalRangeIsDetected()
    {
        var found = new ChartPatternDetector(new SwingStructureAnalyzer()).Detect(RangeSeries());

        Assert.Contains(found, p => p.Kind == ChartPatternKind.Rectangle);
    }

    /// <summary>
    /// Both boundaries are spoken while the range is intact. Naming only one would quietly nominate
    /// a direction the shape has not chosen — and "undecided" is the entire content of a range.
    /// </summary>
    [Fact]
    public void AnIntactRangeSpeaksBothBoundariesAndNoTarget()
    {
        var p = new ChartPattern(ChartPatternKind.Rectangle, ChartPatternState.Forming,
            10, 60, 65, 110, DateTime.Today, DateTime.Today, SecondaryLevel: 100);

        string s = ChartPatternNarrator.Describe(p, Fmt);

        Assert.Contains("top 110", s);
        Assert.Contains("bottom 100", s);
        Assert.Contains("Height 10", s);
        Assert.DoesNotContain("measured target", s);   // no side has broken, so nothing to project
    }

    /// <summary>
    /// A range resolves on whichever boundary breaks first, and the direction is an OUTPUT of that
    /// scan rather than an input. Scanning one side only would mis-report every break the other way
    /// as the range still being intact.
    /// </summary>
    [Fact]
    public void ARangeResolvesOnWhicheverSideBreaksFirst()
    {
        var down = new ChartPatternDetector(new SwingStructureAnalyzer())
            .Detect(RangeSeries(breakUp: false))
            .Where(p => p.Kind == ChartPatternKind.Rectangle && p.State == ChartPatternState.Completed)
            .ToList();
        var up = new ChartPatternDetector(new SwingStructureAnalyzer())
            .Detect(RangeSeries(breakUp: true))
            .Where(p => p.Kind == ChartPatternKind.Rectangle && p.State == ChartPatternState.Completed)
            .ToList();

        Assert.NotEmpty(down);
        Assert.NotEmpty(up);
        Assert.All(down, p => Assert.True(p.BreaksBelow, "a downside break was reported as upside"));
        Assert.All(up, p => Assert.False(p.BreaksBelow, "an upside break was reported as downside"));
    }

    /// <summary>
    /// Once a side breaks there IS a direction, so the conventional projection becomes available —
    /// the range's own height, from the boundary that gave way.
    /// </summary>
    [Fact]
    public void ABrokenRangeProjectsItsHeightFromTheSideThatGaveWay()
    {
        var p = new ChartPattern(ChartPatternKind.Rectangle, ChartPatternState.Completed,
            10, 60, 65, 110, DateTime.Today, DateTime.Today, CompletedAtIndex: 70,
            BreaksBelow: false, MeasuredTarget: 120, SecondaryLevel: 100);

        string s = ChartPatternNarrator.Describe(p, Fmt);

        Assert.Contains("closed above the top at 110", s);
        Assert.Contains("measured target 120", s);
    }

    /// <summary>A range that never broke is reported as intact, not as a failure.</summary>
    [Fact]
    public void AnExpiredRangeIsReportedAsStillIntact()
    {
        var p = new ChartPattern(ChartPatternKind.Rectangle, ChartPatternState.Expired,
            10, 60, 65, 110, DateTime.Today, DateTime.Today, ExpiresAtIndex: 115,
            SecondaryLevel: 100);

        string s = ChartPatternNarrator.Describe(p, Fmt);

        Assert.Contains("intact", s);
        Assert.Contains("100", s);
        Assert.Contains("110", s);
        Assert.DoesNotContain("did not confirm", s);   // wrong verb for a shape that never had one
    }

    /// <summary>
    /// Every kind must produce a sentence, including the newest one. A kind added to the enum and
    /// forgotten in the narrator falls through to a default and reads as the raw C# identifier.
    /// </summary>
    [Fact]
    public void EveryKindHasRealWordsInEveryState()
    {
        foreach (ChartPatternKind kind in Enum.GetValues<ChartPatternKind>())
        foreach (var state in Enum.GetValues<ChartPatternState>())
        {
            var p = new ChartPattern(kind, state, 1, 20, 25, 100, DateTime.Today, DateTime.Today,
                CompletedAtIndex: state == ChartPatternState.Completed ? 30 : null,
                ExpiresAtIndex: 45, MeasuredTarget: 90,
                SecondaryLevel: kind == ChartPatternKind.Rectangle ? 90 : null);

            string s = ChartPatternNarrator.Describe(p, Fmt);

            Assert.False(string.IsNullOrWhiteSpace(s), $"{kind}/{state} said nothing");
            Assert.DoesNotContain(kind.ToString(), s);   // the raw enum name never reaches speech
        }
    }

    // ── Stepping between formations ─────────────────────────────────────────────

    /// <summary>
    /// The stops are edges, not patterns: the bar a formation became knowable and the bar its story
    /// ended. Stopping only at starts would make it impossible to answer the question people
    /// actually have, which is how the thing turned out.
    /// </summary>
    [Fact]
    public void EdgesAreBothTheStartAndTheResolutionOfEachFormation()
    {
        var patterns = new List<ChartPattern>
        {
            P(known: 45, expires: 75),                                            // 45 and 75
            P(kind: ChartPatternKind.BullFlag, state: ChartPatternState.Completed,
              start: 100, end: 112, known: 112, completed: 120, expires: 124),    // 112 and 120
        };

        Assert.Equal(new[] { 45, 75, 112, 120 }, ChartPatternNavigator.Edges(patterns, barCount: 500));
    }

    /// <summary>
    /// A resolution bar past the end of the loaded data is dropped rather than clamped. A formation
    /// still open at the right-hand edge has not resolved, and offering its notional expiry bar as
    /// a stop would invent an event that has not happened.
    /// </summary>
    [Fact]
    public void EdgesBeyondTheLoadedDataAreDroppedNotClamped()
    {
        var patterns = new List<ChartPattern> { P(known: 45, expires: 500) };

        Assert.Equal(new[] { 45 }, ChartPatternNavigator.Edges(patterns, barCount: 100));
    }

    [Fact]
    public void JumpingFindsTheNearestEdgeInTheRequestedDirection()
    {
        var patterns = new List<ChartPattern>
        {
            P(known: 45, expires: 75),
            P(kind: ChartPatternKind.BullFlag, start: 100, end: 112, known: 112, expires: 124),
        };

        Assert.Equal(75, ChartPatternNavigator.NextEdge(patterns, 45, forward: true, 500));
        Assert.Equal(45, ChartPatternNavigator.NextEdge(patterns, 75, forward: false, 500));
        Assert.Equal(-1, ChartPatternNavigator.NextEdge(patterns, 400, forward: true, 500));
        Assert.Equal(-1, ChartPatternNavigator.NextEdge(patterns, 10, forward: false, 500));
    }

    /// <summary>
    /// Strictly beyond, never onto the current bar — otherwise the key appears dead when the cursor
    /// is already parked on an edge, which is exactly where a user pressing it repeatedly will be.
    /// </summary>
    [Fact]
    public void JumpingNeverLandsOnTheBarItStartedFrom()
    {
        var patterns = new List<ChartPattern> { P(known: 45, expires: 75) };

        Assert.NotEqual(45, ChartPatternNavigator.NextEdge(patterns, 45, forward: true, 500));
        Assert.NotEqual(75, ChartPatternNavigator.NextEdge(patterns, 75, forward: false, 500));
    }

    // ── Cache ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Three features now ask for the same detection on the same bars. They must get the identical
    /// list, or they will describe the chart differently and the disagreement will be indis-
    /// tinguishable by ear from a bug in the detector.
    /// </summary>
    private static ChartIdentity Id(string symbol, string tf = "4h", string provider = "MEXC")
        => new("Crypto", provider, symbol, tf);

    [Fact]
    public void TheCacheReturnsTheSameInstanceUntilTheDataChanges()
    {
        var cache = new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer()));
        var bars = RandomWalk(400, seed: 3);

        var a = cache.For(Id("TAOUSDT"), bars);
        var b = cache.For(Id("TAOUSDT"), bars);
        Assert.Same(a, b);

        var longer = bars.Concat(RandomWalk(50, seed: 4)).ToList();
        Assert.NotSame(a, cache.For(Id("TAOUSDT"), longer));
    }

    /// <summary>
    /// <b>The cross-chart leak.</b> Reported from live use: open TAO 4h in one tab and BTC 4h in
    /// another, and the second chart described the first chart's formations.
    ///
    /// <para>
    /// The cache keyed on <c>(bar count, last bar timestamp)</c>, which looks like it identifies the
    /// data and does — for one chart. Two crypto charts on the same timeframe load the same default
    /// number of bars, and because crypto trades continuously the most recent 4-hour bar carries the
    /// <b>identical</b> timestamp on both. The key collided exactly. This fixture reproduces that
    /// precisely: same length, same final timestamp, completely different prices.
    /// </para>
    /// </summary>
    [Fact]
    public void TwoChartsWithIdenticalBarCountsAndTimestampsDoNotShareAnEntry()
    {
        var cache = new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer()));

        var tao = RandomWalk(400, seed: 11);
        var btc = RandomWalk(400, seed: 99);

        // The collision condition, made exact.
        Assert.Equal(tao.Count, btc.Count);
        Assert.Equal(tao[^1].Date, btc[^1].Date);

        var taoPatterns = cache.For(Id("TAOUSDT"), tao);
        var btcPatterns = cache.For(Id("BTCUSDT"), btc);

        Assert.NotSame(taoPatterns, btcPatterns);
        // And the second chart's answer must actually come from the second chart's prices.
        Assert.Equal(new ChartPatternDetector(new SwingStructureAnalyzer()).Detect(btc).Count,
                     btcPatterns.Count);
    }

    /// <summary>
    /// Switching back must not recompute. A single-entry cache would be correct after the fix and
    /// would turn every alt-tab between two charts into a fresh O(swings²) scan.
    /// </summary>
    [Fact]
    public void SwitchingBackToAPreviousChartIsFree()
    {
        var cache = new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer()));
        var tao = RandomWalk(400, seed: 11);
        var btc = RandomWalk(400, seed: 99);

        var first = cache.For(Id("TAOUSDT"), tao);
        cache.For(Id("BTCUSDT"), btc);

        Assert.Same(first, cache.For(Id("TAOUSDT"), tao));
    }

    /// <summary>
    /// Symbol alone is not the identity. The same ticker at two timeframes is two different charts,
    /// and the same ticker on two providers can carry different history.
    /// </summary>
    [Fact]
    public void TimeframeAndProviderArePartOfTheIdentity()
    {
        var cache = new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer()));
        var bars = RandomWalk(400, seed: 5);

        var fourHour = cache.For(Id("BTCUSDT", tf: "4h"), bars);
        var daily = cache.For(Id("BTCUSDT", tf: "1d"), bars);
        var other = cache.For(Id("BTCUSDT", tf: "4h", provider: "Bitstamp"), bars);

        Assert.NotSame(fourHour, daily);
        Assert.NotSame(fourHour, other);

        Assert.NotEqual(ChartPatternCache.KeyFor(Id("BTCUSDT", tf: "4h")),
                        ChartPatternCache.KeyFor(Id("BTCUSDT", tf: "1d")));
    }

    [Fact]
    public void TheCacheIsEmptyRatherThanThrowingOnTooLittleData()
    {
        var cache = new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer()));

        Assert.Empty(cache.For(Id("BTCUSDT"), null));
        Assert.Empty(cache.For(Id("BTCUSDT"), new List<Ohlcv>()));
        Assert.Empty(cache.For(Id("BTCUSDT"), RandomWalk(5, seed: 1)));
    }

    /// <summary>
    /// The cache is bounded. A user who opens many charts in a session must not accumulate a
    /// detection result per chart forever.
    /// </summary>
    [Fact]
    public void TheCacheEvictsRatherThanGrowingWithoutBound()
    {
        var cache = new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer()));
        var bars = RandomWalk(200, seed: 2);

        var firstEntry = cache.For(Id("SYM0"), bars);
        for (int i = 1; i <= ChartPatternCache.MaxEntries + 2; i++)
            cache.For(Id("SYM" + i), bars);

        // SYM0 was pushed out, so asking again recomputes rather than returning the old instance.
        Assert.NotSame(firstEntry, cache.For(Id("SYM0"), bars));
    }


    // ── Nesting and pinning ─────────────────────────────────────────────────────

    /// <summary>
    /// Containment is what turns overlap from noise into structure. "Plus two more formations here"
    /// says something is being withheld; "inside a larger double bottom that began 12 March" says
    /// the shape you are standing in is a component of a bigger one — which is the difference
    /// between a setup that stands alone and a detail of something still in play.
    /// </summary>
    [Fact]
    public void ASmallFormationInsideALargerOneReportsItsContainer()
    {
        var big = P(kind: ChartPatternKind.DoubleBottom, start: 0, end: 200, known: 205);
        var small = P(kind: ChartPatternKind.AscendingTriangle, start: 50, end: 90, known: 95);

        Assert.Equal(big.Key, ChartPatternNarrator.ContainerOf(small, new[] { big, small })!.Key);
        Assert.Null(ChartPatternNarrator.ContainerOf(big, new[] { big, small }));

        string clause = ChartPatternNarrator.DescribeContainment(small, new[] { big, small });
        Assert.Contains("Inside a larger double bottom", clause);
    }

    /// <summary>
    /// The SMALLEST container wins. With three nested shapes the immediate parent is informative;
    /// naming the outermost would skip the level the user is actually inside.
    /// </summary>
    [Fact]
    public void TheImmediateParentIsNamedNotTheOutermost()
    {
        var outer = P(kind: ChartPatternKind.Rectangle, start: 0, end: 400, known: 405);
        var middle = P(kind: ChartPatternKind.DoubleBottom, start: 40, end: 200, known: 205);
        var inner = P(kind: ChartPatternKind.BullFlag, start: 60, end: 80, known: 85);

        var found = ChartPatternNarrator.ContainerOf(inner, new[] { outer, middle, inner });

        Assert.Equal(middle.Key, found!.Key);
    }

    /// <summary>
    /// Two shapes over identical bars are siblings, not parent and child. Without the
    /// strictly-larger rule they would each claim to contain the other.
    /// </summary>
    [Fact]
    public void IdenticallySizedFormationsDoNotContainEachOther()
    {
        var a = P(kind: ChartPatternKind.DoubleTop, start: 10, end: 50, known: 55);
        var b = P(kind: ChartPatternKind.SymmetricalTriangle, start: 10, end: 50, known: 55);

        Assert.Null(ChartPatternNarrator.ContainerOf(a, new[] { a, b }));
        Assert.Null(ChartPatternNarrator.ContainerOf(b, new[] { a, b }));
    }

    /// <summary>
    /// Pinning lets the user override the size ranking with their own thesis, without the
    /// application acquiring an opinion. The pinned formation leads; nothing is hidden.
    /// </summary>
    [Fact]
    public void APinnedFormationLeadsTheReadout()
    {
        var focus = new ChartPatternFocus();
        var big = P(kind: ChartPatternKind.Rectangle, start: 0, end: 200, known: 205);
        var small = P(kind: ChartPatternKind.BullFlag, start: 50, end: 62, known: 65);
        var ranked = new List<ChartPattern> { big, small };

        Assert.Equal(big.Key, focus.Apply("chart", ranked)[0].Key);   // size ranking by default

        focus.CycleAt("chart", ranked);                                // pins the first
        focus.CycleAt("chart", ranked);                                // …then the second
        Assert.Equal(small.Key, focus.Apply("chart", ranked)[0].Key);

        // Everything is still present — pinning reorders, it does not filter.
        Assert.Equal(2, focus.Apply("chart", ranked).Count);
    }

    /// <summary>
    /// A pin survives walking away. The user is simply somewhere else on the chart, and coming back
    /// should find their choice still in force rather than silently reset.
    /// </summary>
    [Fact]
    public void APinIsNotClearedByMovingSomewhereItDoesNotApply()
    {
        var focus = new ChartPatternFocus();
        var a = P(kind: ChartPatternKind.DoubleTop, start: 0, end: 40, known: 45);
        var b = P(kind: ChartPatternKind.BullFlag, start: 10, end: 22, known: 25);
        var atThisBar = new List<ChartPattern> { a, b };

        focus.CycleAt("chart", atThisBar);
        Assert.True(focus.IsPinned("chart"));

        // A bar holding a completely different formation: the pin does not apply here…
        var elsewhere = new List<ChartPattern> { P(kind: ChartPatternKind.BearFlag, start: 300, end: 320, known: 325) };
        Assert.Equal(1, focus.Apply("chart", elsewhere).Count);

        // …and is still in force when we come back.
        Assert.True(focus.IsPinned("chart"));
    }

    [Fact]
    public void ClearingThePinReportsWhetherThereWasOne()
    {
        var focus = new ChartPatternFocus();
        Assert.False(focus.Clear("chart"));

        focus.CycleAt("chart", new List<ChartPattern> { P() });
        Assert.True(focus.Clear("chart"));
        Assert.False(focus.IsPinned("chart"));
    }

    /// <summary>A pin belongs to one chart, for the same reason detection results do.</summary>
    [Fact]
    public void PinsAreScopedToOneChart()
    {
        var focus = new ChartPatternFocus();
        focus.CycleAt("BTC", new List<ChartPattern> { P() });

        Assert.True(focus.IsPinned("BTC"));
        Assert.False(focus.IsPinned("TAO"));
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────

    private static List<Ohlcv> RandomWalk(int n, int seed)
    {
        var rng = new Random(seed);
        var bars = new List<Ohlcv>(n);
        double px = 100;
        var t = new DateTime(2020, 1, 1);
        for (int i = 0; i < n; i++)
        {
            px *= 1 + (rng.NextDouble() - 0.5) * 0.04;
            double hi = px * (1 + rng.NextDouble() * 0.01);
            double lo = px * (1 - rng.NextDouble() * 0.01);
            bars.Add(new Ohlcv { Date = t.AddDays(i), Open = px, High = hi, Low = lo, Close = px, Volume = 1000 });
        }
        return bars;
    }

    /// <summary>
    /// A deliberate M: rise to 110, fall to 100, rise to 110 again, then break down.
    ///
    /// <para>
    /// Every bar is a single point — open, high, low and close all equal — so the twin peaks sit at
    /// exactly 110 and the trough at exactly 100. The first version of this fixture gave each bar a
    /// 0.2 wick, and the detector correctly measured the formation from the wick highs rather than
    /// the closes, so the hand-computed target was off by exactly that wick. The fixture was wrong,
    /// not the projection — but a test that has to be told the answer by the code it is testing is
    /// worth nothing, so the fixture is built to make the intended numbers exact.
    /// </para>
    /// </summary>
    private static List<Ohlcv> DoubleTopSeries()
    {
        var path = new List<double>();
        void Ramp(double from, double to, int steps)
        {
            for (int i = 1; i <= steps; i++) path.Add(from + (to - from) * i / steps);
        }

        path.Add(90);
        Ramp(90, 110, 20);
        Ramp(110, 100, 20);
        Ramp(100, 110, 20);
        Ramp(110, 85, 30);
        for (int i = 0; i < 40; i++) path.Add(85);

        var bars = new List<Ohlcv>(path.Count);
        var t = new DateTime(2020, 1, 1);
        for (int i = 0; i < path.Count; i++)
        {
            double p = path[i];
            bars.Add(new Ohlcv
            {
                Date = t.AddDays(i),
                Open = p, Close = p, High = p, Low = p,
                Volume = 1000
            });
        }
        return bars;
    }
}
