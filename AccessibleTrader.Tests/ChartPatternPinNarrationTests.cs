using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// Reported from live use: "I cycle with semicolon and it says 'leading with …', but the
/// closing message doesn't change to match — the end still says double bottom."
///
/// <para>
/// The pin decides which of several overlapping formations leads the readout. These tests drive
/// the real <see cref="AccessibilityFeedbackCoordinator"/> with a controlled set of patterns,
/// because every previous defect in this area was in how the pieces combine rather than in any
/// one of them.
/// </para>
/// </summary>
public sealed class ChartPatternPinNarrationTests
{
    // Two formations over the same region that break on the SAME bar — the ordinary case when
    // two definitions share a neckline, and the case where the pin has to decide.
    private static readonly ChartPattern Big = new(
        Kind: ChartPatternKind.DoubleBottom,
        State: ChartPatternState.Completed,
        StartBarIndex: 10, EndBarIndex: 60, KnownAtIndex: 65,
        TriggerLevel: 100, StartTime: default, EndTime: default,
        CompletedAtIndex: 80, ExpiresAtIndex: 120,
        BreaksBelow: false, MeasuredTarget: 120);

    private static readonly ChartPattern Small = new(
        Kind: ChartPatternKind.AscendingTriangle,
        State: ChartPatternState.Completed,
        StartBarIndex: 30, EndBarIndex: 58, KnownAtIndex: 65,
        TriggerLevel: 101, StartTime: default, EndTime: default,
        CompletedAtIndex: 80, ExpiresAtIndex: 120,
        BreaksBelow: false, MeasuredTarget: 118);

    private sealed class FixedPatterns : IChartPatternCache
    {
        private readonly IReadOnlyList<ChartPattern> _all;
        public FixedPatterns(params ChartPattern[] all) => _all = all;
        public IReadOnlyList<ChartPattern> For(ChartIdentity identity, IReadOnlyList<Ohlcv>? bars) => _all;
    }

    private sealed class Harness
    {
        public MockWorkspaceStore Store = new();
        public SpyEventBus Bus = new();
        public CounterSpeechManager Speech = new();
        public ChartPatternFocus Focus = new();
        public List<string> Spoken = new();
        public ChartPatternNavigator Navigator = null!;
        public AccessibilityFeedbackCoordinator Coordinator = null!;
    }

    private static Harness Build(params ChartPattern[] patterns)
    {
        var h = new Harness();
        h.Speech.OnSpeak = t => h.Spoken.Add(t);

        var formatter = new SpeechFormatter();
        var speechRouter = new SpeechFeedbackRouter(h.Speech, formatter, h.Store);
        var sonify = new MockNavigationSonifier();
        var cache = new FixedPatterns(patterns);

        h.Coordinator = new AccessibilityFeedbackCoordinator(
            h.Store,
            new NavigationFeedbackManager(speechRouter, formatter),
            speechRouter,
            new AudioFeedbackRouter(sonify, new MockEarconService()),
            formatter,
            h.Bus,
            new MockEarconService(),
            new SdkCandlePatternAnalyzer(),
            cache,
            h.Focus,
            new MockAutoNarrationService());

        h.Navigator = new ChartPatternNavigator(h.Store, h.Bus, cache, h.Focus);
        return h;
    }

    private static void StandOn(Harness h, int index)
    {
        var bars = Enumerable.Range(0, 130)
            .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddDays(i), 100, 101, 99, 100, 10))
            .ToList();

        h.Store.EmitState(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars.ToArray()),
            CurrentDataIndex = index,
            DescribeChartPatterns = true,
        });
    }

    private static string Move(Harness h, int to)
    {
        StandOn(h, to);
        return h.Coordinator.ChartPatternContext();
    }

    private static void PinTheTriangle(Harness h)
    {
        // Cycle until the ascending triangle is the leader, exactly as pressing ';' does.
        for (int i = 0; i < 4; i++)
        {
            h.Navigator.CycleFocus();
            var ranked = ChartPatternNarrator.ByDominance(new[] { Big, Small }).ToList();
            string key = ChartPatternCache.KeyFor(h.Store.State.Identity);
            if (h.Focus.Apply(key, ranked)[0].Key.Equals(Small.Key)) return;
        }
        Assert.Fail("could not pin the ascending triangle");
    }

    /// <summary>
    /// The reported defect: step onto the bar where both formations break, and the closing
    /// announcement must lead with the one the user pinned.
    /// </summary>
    [Fact]
    public void TheClosingAnnouncementLeadsWithThePinnedFormation()
    {
        var h = Build(Big, Small);

        Move(h, 79);          // arrive inside both formations
        PinTheTriangle(h);

        string spoken = Move(h, 80);   // one step forward, onto the break bar

        int triangle = spoken.IndexOf("ascending triangle", StringComparison.OrdinalIgnoreCase);
        int doubleBottom = spoken.IndexOf("double bottom", StringComparison.OrdinalIgnoreCase);

        Assert.True(triangle >= 0, $"pinned formation was not mentioned at all: {spoken}");
        Assert.True(doubleBottom < 0 || triangle < doubleBottom,
            $"pinned formation must lead the closing announcement, got: {spoken}");
    }

    /// <summary>
    /// Cody, 2026-11-13, TAO/USDT 1h: he chose a double top with semicolon and then "can't use ,
    /// and . to move around the chart patterns anymore". The choice used to confine the jump keys
    /// to the chosen formation's two edges. Now it decides only what is said at the bar: choose
    /// the double bottom (which breaks at 90) and the next-formation key still stops first at the
    /// triangle's break at 80, the next edge on the chart.
    /// </summary>
    [Fact]
    public void TheJumpKeysReachEveryFormationWhateverSemicolonChose()
    {
        var doubleBottom = Big with { CompletedAtIndex = 90 };
        var triangle = Small with { CompletedAtIndex = 80 };
        var h = Build(doubleBottom, triangle);

        StandOn(h, 70);
        string key = ChartPatternCache.KeyFor(h.Store.State.Identity);
        h.Navigator.CycleFocus();   // the double bottom leads by size, so the first press chooses the triangle
        h.Navigator.CycleFocus();   // and the second the double bottom
        var ranked = ChartPatternNarrator.ByDominance(ChartPatternNarrator.AtBar(new[] { doubleBottom, triangle }, 70)).ToList();
        Assert.True(h.Focus.IsPinned(key));
        Assert.Equal(doubleBottom.Key, h.Focus.Apply(key, ranked)[0].Key);

        h.Store.DispatchedActions.Clear();
        h.Navigator.Jump(SystemCommand.NavPatternNext);

        var nav = h.Store.DispatchedActions.OfType<NavigateAction>().LastOrDefault();
        Assert.NotNull(nav);
        Assert.Equal(80, nav!.NewIndex);   // the triangle's end: every formation's edges count
    }

    /// <summary>
    /// With a choice made, running out of edges says the chart has no more — not that a pin
    /// is in the way, because there is no pin to be in the way any more.
    /// </summary>
    [Fact]
    public void RunningOutOfEdgesAfterAChoiceSaysOnlyThatTheChartHasNoMore()
    {
        var h = Build(Big, Small);
        StandOn(h, 70);
        PinTheTriangle(h);

        StandOn(h, 119);   // past every edge on the chart
        h.Bus.Log.Clear();
        h.Navigator.Jump(SystemCommand.NavPatternNext);

        string said = string.Join(" ", h.Bus.Log.OfType<FeedbackRequestEvent>().Select(e => e.Message));
        Assert.Equal("No further chart formations.", said);
    }

    /// <summary>The same requirement on a jump, which is how a user travels to an ending.</summary>
    [Fact]
    public void JumpingToTheEndAlsoLeadsWithThePinnedFormation()
    {
        var h = Build(Big, Small);

        Move(h, 70);
        PinTheTriangle(h);

        string spoken = Move(h, 80);   // more than one bar of travel — the jump path

        int triangle = spoken.IndexOf("ascending triangle", StringComparison.OrdinalIgnoreCase);
        int doubleBottom = spoken.IndexOf("double bottom", StringComparison.OrdinalIgnoreCase);

        Assert.True(triangle >= 0, $"pinned formation was not mentioned at all: {spoken}");
        Assert.True(doubleBottom < 0 || triangle < doubleBottom,
            $"pinned formation must lead after a jump, got: {spoken}");
    }

    /// <summary>
    /// Nesting is the case the pin exists for: a twelve-bar flag inside an eighty-bar triangle may
    /// be exactly what a setup is built on. Every formation covering the bar has to be reachable by
    /// repeated presses, container and contained alike, and the walk has to wrap rather than stop
    /// at the innermost.
    /// </summary>
    [Fact]
    public void NestedFormationsCanAllBeCycledThrough()
    {
        var outer = new ChartPattern(
            Kind: ChartPatternKind.Rectangle, State: ChartPatternState.Forming,
            StartBarIndex: 0, EndBarIndex: 200, KnownAtIndex: 60,
            TriggerLevel: 110, StartTime: default, EndTime: default,
            ExpiresAtIndex: 300, SecondaryLevel: 90);
        var middle = new ChartPattern(
            Kind: ChartPatternKind.DoubleBottom, State: ChartPatternState.Forming,
            StartBarIndex: 20, EndBarIndex: 120, KnownAtIndex: 62,
            TriggerLevel: 100, StartTime: default, EndTime: default, ExpiresAtIndex: 300);
        var inner = new ChartPattern(
            Kind: ChartPatternKind.BullFlag, State: ChartPatternState.Forming,
            StartBarIndex: 40, EndBarIndex: 60, KnownAtIndex: 65,
            TriggerLevel: 101, StartTime: default, EndTime: default, ExpiresAtIndex: 300);

        var h = Build(outer, middle, inner);
        StandOn(h, 70);

        var led = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            h.Bus.Log.Clear();
            h.Navigator.CycleFocus();
            led.Add(string.Join(" ", h.Bus.Log.OfType<FeedbackRequestEvent>().Select(e => e.Message)));
        }

        // The range leads before any press, so the first press moves OFF it. "Leading with" is
        // matched because the containment clause names the parent too ("inside a larger range").
        Assert.Contains("Leading with double bottom", led[0]);
        Assert.Contains("Leading with bull flag", led[1]);
        Assert.Contains("Leading with range", led[2]);    // wraps rather than stopping at the innermost
        Assert.Contains("Leading with double bottom", led[3]);
        Assert.All(led, l => Assert.Contains("of 3", l));   // and always says how many there are
    }

    /// <summary>
    /// Choosing the INNER formation and walking past it: the jump keys go on to the container's
    /// break. The old pin stopped here ("No further edges in the pinned bull flag") until
    /// Shift+semicolon released it — Cody's "i can't nav any other chart patterns while that one
    /// is pinned and i can't set it without clearing it".
    /// </summary>
    [Fact]
    public void ChoosingAContainedFormationNeverConfinesTheJumpKeys()
    {
        var (outer, inner) = RangeAndFlag();
        var h = Build(outer, inner);
        Move(h, 70);
        h.Navigator.CycleFocus();   // the range leads by size, so one press chooses the flag
        string key = ChartPatternCache.KeyFor(h.Store.State.Identity);
        Assert.True(h.Focus.IsPinned(key));

        StandOn(h, 90);             // past both of the flag's edges
        h.Store.DispatchedActions.Clear();
        h.Navigator.Jump(SystemCommand.NavPatternNext);

        var nav = h.Store.DispatchedActions.OfType<NavigateAction>().LastOrDefault();
        Assert.NotNull(nav);
        Assert.Equal(115, nav!.NewIndex);   // the range's break
    }

    /// <summary>
    /// "it should be sticky until I intentionally change it ... It should stay regardless of where
    /// I navigate to" (Cody, 2026-11-13). The same morning the choice was dropped on the first bar
    /// off the chosen formation, so Home and back lost it. Now leaving its bars, even for a bar with
    /// no formations at all, keeps it, and coming back finds it leading.
    /// </summary>
    [Fact]
    public void TheChoiceStaysWhereverTheCursorGoes()
    {
        var (outer, inner) = RangeAndFlag();
        var h = Build(outer, inner);
        string key = ChartPatternCache.KeyFor(WorkspaceState.Initial.Identity);

        Move(h, 70);
        h.Navigator.CycleFocus();                        // the flag
        Assert.StartsWith("Possible bull flag forming", Move(h, 72));     // on it: the choice leads a jump-landing

        Move(h, 0);                                      // Home: before any formation
        Move(h, 129);                                    // End: the flag ended at 85
        Assert.True(h.Focus.IsPinned(key));

        Assert.StartsWith("Possible bull flag forming", Move(h, 72));     // back on its bars: still the flag
    }

    // Cody's TAO/USDT 1h chart in small: an inverse head and shoulders (forming) recognised at
    // 80, a double top inside it recognised at 72 and confirmed at 100, and a falling wedge
    // recognised at 120. At 80 both the head and shoulders and the double top are in play; at
    // the last bar (129) the head and shoulders and the wedge are.
    private static (ChartPattern ihs, ChartPattern top, ChartPattern wedge) TaoChart() => (
        new ChartPattern(
            Kind: ChartPatternKind.InverseHeadAndShoulders, State: ChartPatternState.Forming,
            StartBarIndex: 0, EndBarIndex: 76, KnownAtIndex: 80,
            TriggerLevel: 110, StartTime: default, EndTime: default, ExpiresAtIndex: 300),
        new ChartPattern(
            Kind: ChartPatternKind.DoubleTop, State: ChartPatternState.Completed,
            StartBarIndex: 8, EndBarIndex: 68, KnownAtIndex: 72,
            TriggerLevel: 95, StartTime: default, EndTime: default,
            CompletedAtIndex: 100, ExpiresAtIndex: 300, BreaksBelow: true),
        new ChartPattern(
            Kind: ChartPatternKind.FallingWedge, State: ChartPatternState.Forming,
            StartBarIndex: 80, EndBarIndex: 115, KnownAtIndex: 120,
            TriggerLevel: 105, StartTime: default, EndTime: default, ExpiresAtIndex: 300));

    private static string Said(Harness h)
        => string.Join(" ", h.Bus.Log.OfType<FeedbackRequestEvent>().Select(e => e.Message));

    /// <summary>
    /// A stop that is one formation's start names that formation, even with another chosen and in
    /// play there; the choice is named at its own start and end. Cody first asked for the choice
    /// at the head and shoulders' start ("it should name the formation i selected"), then, the
    /// same day: "I just want to hear about the formation at the bar i land on, that's it, the
    /// start, the end whether it was confirmed or not ... I don't want to hear about another
    /// pattern that isn't the pattern start/end I landed on."
    /// </summary>
    [Fact]
    public void AStopNamesTheFormationThatStartsThereWhateverIsChosen()
    {
        var (ihs, top, wedge) = TaoChart();
        var h = Build(ihs, top, wedge);

        Move(h, 80);
        h.Navigator.CycleFocus();                                   // the double top

        Assert.StartsWith("Start of possible double top", Move(h, 72));   // comma: the double top's own start
        string back = Move(h, 80);                                  // period: the head and shoulders' start
        Assert.StartsWith("Start of possible inverse head and shoulders", back);
        Assert.DoesNotContain("ouble top", back);
        Assert.DoesNotContain("more formation", back);              // the double top only passes through

        Assert.StartsWith("Double top confirmed here", Move(h, 100));     // its own end: its outcome
        Move(h, 90);                                                // backslash-like: no one's start or end
        Assert.StartsWith("Possible double top forming", Move(h, 85));
    }

    /// <summary>
    /// The choice is named only where it is at the bar. Cody, 2026-11-13, after it was named from
    /// every bar ("double top confirmed 15 bars back" on the falling wedge's start, "... ahead" on
    /// the triangle's): "I just want to hear about the formation at the bar i land on, that's it,
    /// the start, the end whether it was confirmed or not ... I don't want to hear about another
    /// pattern that isn't the pattern start/end I landed on."
    /// </summary>
    [Fact]
    public void AJumpOffTheChosenFormationNamesOnlyWhatIsAtTheBar()
    {
        var (ihs, top, wedge) = TaoChart();
        var h = Build(ihs, top, wedge);
        Move(h, 80);
        h.Navigator.CycleFocus();                                   // the double top

        string wedgeStart = Move(h, 120);                           // the wedge's start; the double top is over
        Assert.StartsWith("Start of possible falling wedge", wedgeStart);
        Assert.DoesNotContain("ouble top", wedgeStart);

        string end = Move(h, 129);
        Assert.StartsWith("Possible inverse head and shoulders forming", end);
        Assert.EndsWith("Plus 1 more formation here.", end);
        Assert.DoesNotContain("ouble top", end);

        Assert.Equal("", Move(h, 50));                              // before anything is knowable: nothing at all
        Assert.Equal("", Move(h, 71));
    }

    /// <summary>
    /// A jump onto the same bar reads the same as an arrow step onto it — Cody, 2026-11-13: the
    /// arrows said the falling wedge's start where comma and period said the chosen double top,
    /// "it is not consistent".
    /// </summary>
    [Fact]
    public void AJumpAndAStepOntoAFormationsStartSayTheSameFormation()
    {
        var (ihs, top, wedge) = TaoChart();
        var h = Build(ihs, top, wedge);
        Move(h, 80);
        h.Navigator.CycleFocus();                                   // the double top

        Move(h, 119);
        string stepped = Move(h, 120);
        Move(h, 129);
        string jumped = Move(h, 120);

        Assert.StartsWith("Start of possible falling wedge", stepped);
        Assert.StartsWith("Start of possible falling wedge", jumped);
    }

    /// <summary>
    /// With the double top chosen and over, End reads the leader there, so semicolon's first press
    /// moves OFF it — forward to the wedge, backward round to the wedge too (two formations) — not
    /// onto the formation just heard. The new choice then holds across Home and End.
    /// </summary>
    [Fact]
    public void WhereTheChoiceIsNotInPlaySemicolonStartsFromTheFormationsHere()
    {
        var (ihs, top, wedge) = TaoChart();
        var h = Build(ihs, top, wedge);
        string key = ChartPatternCache.KeyFor(WorkspaceState.Initial.Identity);
        Move(h, 80);
        h.Navigator.CycleFocus();
        Assert.Equal(ChartPatternKind.DoubleTop, h.Focus.PinnedIn(key, new[] { ihs, top, wedge })?.Kind);

        Move(h, 129);                                               // reads the head and shoulders
        h.Bus.Log.Clear();
        h.Navigator.CycleFocus();
        Assert.Contains("Leading with falling wedge, 2 of 2", Said(h));

        Move(h, 0);
        Assert.StartsWith("Possible falling wedge forming", Move(h, 129));
    }

    /// <summary>
    /// On a stop that is one formation's start, with no choice there, semicolon counts from that
    /// formation — the one just read — rather than from the leader, which would make the first
    /// press choose the formation the user had just heard.
    /// </summary>
    [Fact]
    public void SemicolonOnAStartCountsFromTheFormationThatStartsThere()
    {
        var (ihs, top, wedge) = TaoChart();
        var h = Build(ihs, top, wedge);
        Assert.StartsWith("Start of possible falling wedge", Move(h, 120));   // the head and shoulders leads here
        h.Bus.Log.Clear();
        h.Navigator.CycleFocus();
        Assert.Contains("Leading with inverse head and shoulders, 1 of 2", Said(h));
    }

    /// <summary>
    /// Two formations side by side, neither inside the other: the one not read is counted. It was
    /// its own one-formation nesting, which says nothing, yet was treated as named — so End on
    /// Cody's TAO/USDT 1h chart read the head and shoulders and never mentioned the wedge.
    /// </summary>
    [Fact]
    public void AFormationBesideTheLeaderNotInsideItIsCounted()
    {
        var (ihs, top, wedge) = TaoChart();
        var h = Build(ihs, top, wedge);
        string end = Move(h, 129);
        Assert.StartsWith("Possible inverse head and shoulders forming", end);
        Assert.EndsWith("Plus 1 more formation here.", end);
    }

    /// <summary>
    /// A bar carrying one formation while the choice is elsewhere: semicolon chooses that one
    /// rather than saying "only one formation here" and leaving the readout on the other.
    /// </summary>
    [Fact]
    public void ALoneFormationCanBeChosenWhileTheChoiceIsElsewhere()
    {
        var (ihs, top, wedge) = TaoChart();
        var h = Build(ihs, top, wedge);
        string key = ChartPatternCache.KeyFor(WorkspaceState.Initial.Identity);
        Move(h, 80);
        h.Navigator.CycleFocus();                                   // the double top

        string there = Move(h, 110);                                // only the head and shoulders is here
        Assert.StartsWith("Possible inverse head and shoulders forming", there);
        Assert.DoesNotContain("ouble top", there);
        h.Bus.Log.Clear();
        h.Navigator.CycleFocus();
        Assert.Contains("Leading with inverse head and shoulders, 1 of 1", Said(h));
        Assert.Equal(ChartPatternKind.InverseHeadAndShoulders, h.Focus.PinnedIn(key, new[] { ihs, top, wedge })?.Kind);

        h.Bus.Log.Clear();
        h.Navigator.CycleFocus();                                   // now it is the choice: nothing else to pick
        Assert.Contains("Only one formation here", Said(h));
    }

    /// <summary>
    /// The choice is drawn too, and nothing in the store changes when it is made, so semicolon
    /// asks the chart to repaint — otherwise the picture lagged the speech until the next move.
    /// </summary>
    [Fact]
    public void ChoosingAFormationRepaintsTheChart()
    {
        var (ihs, top, wedge) = TaoChart();
        var h = Build(ihs, top, wedge);
        Move(h, 80);
        h.Bus.Log.Clear();
        h.Navigator.CycleFocus();
        Assert.Single(h.Bus.Log.OfType<RedrawEvent>());
    }

    /// <summary>
    /// Shift+semicolon is semicolon backwards (Cody, 2026-11-13). Previously it released the pin.
    /// Three nested formations, the range leading: backwards goes flag, double bottom, range.
    /// </summary>
    [Fact]
    public void ShiftSemicolonCyclesBackwards()
    {
        var (outer, middle, inner) = ThreeNested();
        var h = Build(outer, middle, inner);
        StandOn(h, 70);

        var led = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            h.Bus.Log.Clear();
            h.Navigator.CycleFocus(forward: false);
            led.Add(string.Join(" ", h.Bus.Log.OfType<FeedbackRequestEvent>().Select(e => e.Message)));
        }

        Assert.Contains("Leading with bull flag, 3 of 3", led[0]);
        Assert.Contains("Leading with double bottom, 2 of 3", led[1]);
        Assert.Contains("Leading with range, 1 of 3", led[2]);
        Assert.Contains("Leading with bull flag, 3 of 3", led[3]);
    }

    /// <summary>
    /// Landing inside nested formations (backslash, or any jump not onto an edge) names the
    /// nesting, innermost first, after the leader's own sentence: Cody, 2026-11-13, "say 'possible
    /// falling wedge inside possible double top'". Every formation is named, so no "plus N more".
    /// </summary>
    [Fact]
    public void LandingInsideNestedFormationsNamesTheNesting()
    {
        var (outer, middle, inner) = ThreeNested();
        var h = Build(outer, middle, inner);

        string said = Move(h, 70);   // the first readout on a chart takes the landing path

        Assert.StartsWith("Possible range forming", said);
        Assert.Contains("Possible bull flag inside possible double bottom inside possible range.", said);
        Assert.DoesNotContain("more formation", said);
    }

    /// <summary>
    /// The nesting names a chain; a formation beside it rather than in it is still counted.
    /// </summary>
    [Fact]
    public void ANestingLeavesASiblingToTheOverlapNote()
    {
        var (outer, middle, inner) = ThreeNested();
        var sibling = new ChartPattern(
            Kind: ChartPatternKind.DescendingTriangle, State: ChartPatternState.Forming,
            StartBarIndex: 130, EndBarIndex: 180, KnownAtIndex: 66,
            TriggerLevel: 95, StartTime: default, EndTime: default, ExpiresAtIndex: 300);
        var h = Build(outer, middle, inner, sibling);

        string said = Move(h, 70);

        Assert.Contains("Possible bull flag inside possible double bottom inside possible range.", said);
        Assert.EndsWith("Plus 1 more formation here.", said);
    }

    /// <summary>
    /// A chosen formation outside the nesting is read alone: the nesting sentence would be about
    /// three other formations ("bull flag inside double bottom inside range"), and a landing names
    /// the formation at the bar and nothing else. They are counted, not named.
    /// </summary>
    [Fact]
    public void AChosenFormationOutsideTheNestingIsNotFollowedByIt()
    {
        var (outer, middle, inner) = ThreeNested();
        var sibling = new ChartPattern(
            Kind: ChartPatternKind.DescendingTriangle, State: ChartPatternState.Forming,
            StartBarIndex: 130, EndBarIndex: 180, KnownAtIndex: 66,
            TriggerLevel: 95, StartTime: default, EndTime: default, ExpiresAtIndex: 300);
        var h = Build(outer, middle, inner, sibling);
        Move(h, 70);
        h.Navigator.CycleFocus();
        h.Bus.Log.Clear();
        h.Navigator.CycleFocus();
        Assert.Contains("Leading with descending triangle", Said(h));

        Move(h, 0);
        string said = Move(h, 70);

        Assert.StartsWith("Possible descending triangle forming", said);
        Assert.DoesNotContain(" inside ", said);
        Assert.EndsWith("Plus 3 more formations here.", said);
    }

    /// <summary>
    /// The landing path read the leader through DescribeMany, which re-ranks by size — so after
    /// semicolon chose the triangle, a jump that landed inside both shapes (not on an edge) still
    /// opened with the double bottom. Backslash takes exactly that path.
    /// </summary>
    [Fact]
    public void LandingInsideFormationsLeadsWithTheChosenOne()
    {
        var h = Build(Big, Small);
        Move(h, 70);
        PinTheTriangle(h);

        string said = Move(h, 73);   // two bars: the landing path, still inside both

        Assert.StartsWith("Possible ascending triangle forming", said);
    }

    /// <summary>
    /// The same through the dispatcher, the way the key arrives. The command keeps its old name,
    /// ClearPatternFocus, because saved shortcut profiles store commands by name; renaming it would
    /// have unbound Shift+semicolon for anyone with a saved profile. So what it DOES is pinned here.
    /// </summary>
    [Fact]
    public void TheShiftSemicolonCommandStepsBackwardsThroughTheDispatcher()
    {
        var (outer, middle, inner) = ThreeNested();
        var h = Build(outer, middle, inner);
        StandOn(h, 70);
        var dispatcher = new AccessibleTrader.Core.Services.Input.CommandDispatcher(
            h.Bus, NSubstitute.Substitute.For<INavigationEngine>(), h.Store,
            NSubstitute.Substitute.For<AccessibleTrader.Sdk.Interfaces.IBarDetailService>(),
            new AccessibleTrader.Core.Services.Input.IndicatorCrossingEngine(h.Store, h.Bus), h.Navigator);
        dispatcher.SetChartActive(true);

        h.Bus.Log.Clear();
        dispatcher.Dispatch(SystemCommand.ClearPatternFocus);

        string said = string.Join(" ", h.Bus.Log.OfType<FeedbackRequestEvent>().Select(e => e.Message));
        Assert.Contains("Leading with bull flag, 3 of 3", said);
    }

    private static (ChartPattern outer, ChartPattern inner) RangeAndFlag() => (
        new ChartPattern(
            Kind: ChartPatternKind.Rectangle, State: ChartPatternState.Completed,
            StartBarIndex: 0, EndBarIndex: 200, KnownAtIndex: 60,
            TriggerLevel: 110, StartTime: default, EndTime: default,
            CompletedAtIndex: 115, ExpiresAtIndex: 300, SecondaryLevel: 90),
        new ChartPattern(
            Kind: ChartPatternKind.BullFlag, State: ChartPatternState.Completed,
            StartBarIndex: 40, EndBarIndex: 60, KnownAtIndex: 65,
            TriggerLevel: 101, StartTime: default, EndTime: default,
            CompletedAtIndex: 85, ExpiresAtIndex: 300));

    private static (ChartPattern outer, ChartPattern middle, ChartPattern inner) ThreeNested() => (
        new ChartPattern(
            Kind: ChartPatternKind.Rectangle, State: ChartPatternState.Forming,
            StartBarIndex: 0, EndBarIndex: 200, KnownAtIndex: 60,
            TriggerLevel: 110, StartTime: default, EndTime: default,
            ExpiresAtIndex: 300, SecondaryLevel: 90),
        new ChartPattern(
            Kind: ChartPatternKind.DoubleBottom, State: ChartPatternState.Forming,
            StartBarIndex: 20, EndBarIndex: 120, KnownAtIndex: 62,
            TriggerLevel: 100, StartTime: default, EndTime: default, ExpiresAtIndex: 300),
        new ChartPattern(
            Kind: ChartPatternKind.BullFlag, State: ChartPatternState.Forming,
            StartBarIndex: 40, EndBarIndex: 60, KnownAtIndex: 65,
            TriggerLevel: 101, StartTime: default, EndTime: default, ExpiresAtIndex: 300));

    /// <summary>
    /// The position in "Leading with X, 2 of 3" is how a user knows how many more presses remain.
    /// The nesting test asserted only "of 3", so counting from zero (A2m, M08) read "0 of 3" on the
    /// first press and survived.
    /// </summary>
    [Fact]
    public void ThePinAnnouncementCountsFromOne()
    {
        var outer = new ChartPattern(
            Kind: ChartPatternKind.Rectangle, State: ChartPatternState.Forming,
            StartBarIndex: 0, EndBarIndex: 200, KnownAtIndex: 60,
            TriggerLevel: 110, StartTime: default, EndTime: default,
            ExpiresAtIndex: 300, SecondaryLevel: 90);
        var middle = new ChartPattern(
            Kind: ChartPatternKind.DoubleBottom, State: ChartPatternState.Forming,
            StartBarIndex: 20, EndBarIndex: 120, KnownAtIndex: 62,
            TriggerLevel: 100, StartTime: default, EndTime: default, ExpiresAtIndex: 300);
        var inner = new ChartPattern(
            Kind: ChartPatternKind.BullFlag, State: ChartPatternState.Forming,
            StartBarIndex: 40, EndBarIndex: 60, KnownAtIndex: 65,
            TriggerLevel: 101, StartTime: default, EndTime: default, ExpiresAtIndex: 300);

        var h = Build(outer, middle, inner);
        StandOn(h, 70);

        var said = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            h.Bus.Log.Clear();
            h.Navigator.CycleFocus();
            said.Add(string.Join(" ", h.Bus.Log.OfType<FeedbackRequestEvent>().Select(e => e.Message)));
        }

        // The first press moves off the leader (see TheFirstPressMovesOffTheFormationAlreadyLeading),
        // so the walk reads 2, 3, then wraps to 1 — and 1 is still spoken as one, not zero.
        Assert.Contains("double bottom, 2 of 3", said[0]);
        Assert.Contains("bull flag, 3 of 3", said[1]);
        Assert.Contains("range, 1 of 3", said[2]);
    }

    /// <summary>
    /// Reported from live use (2026-10-09): "it also doesn't seem to change the more I press ;".
    /// With nothing pinned, the first press pinned the formation ALREADY leading and announced it
    /// as "1 of 2" — the shape the user had just heard, so the key appeared to do nothing. The
    /// unpinned state is "the leader is current"; the first press must move to the second.
    /// </summary>
    [Fact]
    public void TheFirstPressMovesOffTheFormationAlreadyLeading()
    {
        var h = Build(Big, Small);
        StandOn(h, 70);
        string chartKey = ChartPatternCache.KeyFor(h.Store.State.Identity);
        var ranked = ChartPatternNarrator.ByDominance(ChartPatternNarrator.AtBar(new[] { Big, Small }, 70)).ToList();
        Assert.Equal(Big.Key, h.Focus.Apply(chartKey, ranked)[0].Key);   // the double bottom leads unpinned

        h.Bus.Log.Clear();
        h.Navigator.CycleFocus();
        string said = string.Join(" ", h.Bus.Log.OfType<FeedbackRequestEvent>().Select(e => e.Message));

        Assert.Contains("Leading with ascending triangle, 2 of 2", said);
        Assert.Equal(Small.Key, h.Focus.Apply(chartKey, ranked)[0].Key);   // and the readout follows

        // Every press changes what leads: the next one wraps back to the double bottom.
        h.Bus.Log.Clear();
        h.Navigator.CycleFocus();
        said = string.Join(" ", h.Bus.Log.OfType<FeedbackRequestEvent>().Select(e => e.Message));
        Assert.Contains("Leading with double bottom, 1 of 2", said);
    }

    // Nested formations with real start dates, so the containment clause has something to say.
    private static readonly ChartPattern DatedRange = new(
        Kind: ChartPatternKind.Rectangle, State: ChartPatternState.Forming,
        StartBarIndex: 0, EndBarIndex: 60, KnownAtIndex: 62,
        TriggerLevel: 110, StartTime: new DateTime(2026, 1, 1), EndTime: new DateTime(2026, 3, 2),
        ExpiresAtIndex: 125, SecondaryLevel: 90);

    private static readonly ChartPattern DatedFlag = new(
        Kind: ChartPatternKind.BullFlag, State: ChartPatternState.Forming,
        StartBarIndex: 40, EndBarIndex: 58, KnownAtIndex: 63,
        TriggerLevel: 101, StartTime: new DateTime(2026, 2, 10), EndTime: new DateTime(2026, 2, 28),
        ExpiresAtIndex: 125);

    /// <summary>
    /// "1 of 2, 2 of 2" is a count, not an identity. When the shape now leading sits inside a
    /// larger one, the pin announcement names the parent and when it began — the same clause the
    /// navigation readout uses — so the user can tell which level of the nest they chose.
    /// </summary>
    [Fact]
    public void ThePinAnnouncementNamesTheContainerOfTheFormationNowLeading()
    {
        var h = Build(DatedRange, DatedFlag);
        StandOn(h, 70);

        h.Bus.Log.Clear();
        h.Navigator.CycleFocus();   // range led; the flag now does
        string flag = string.Join(" ", h.Bus.Log.OfType<FeedbackRequestEvent>().Select(e => e.Message));

        h.Bus.Log.Clear();
        h.Navigator.CycleFocus();   // back to the range, which is inside nothing
        string range = string.Join(" ", h.Bus.Log.OfType<FeedbackRequestEvent>().Select(e => e.Message));

        Assert.Contains("Leading with bull flag, 2 of 2", flag);
        Assert.Contains("Inside a larger range that began 1 January.", flag);
        Assert.Contains("Leading with range, 1 of 2", range);
        Assert.DoesNotContain("Inside a larger", range);
    }

    /// <summary>
    /// Containment must come from the formations at THIS bar — the set semicolon cycles — not from
    /// every formation on the chart. Here the double bottom geometrically contains the flag but is
    /// not knowable until bar 125, when its second low has printed. Standing on the flag's first
    /// bar (65), the readout said "inside a larger double bottom" about a shape nobody standing
    /// there could have seen, and that semicolon could not select ("only one formation here").
    /// </summary>
    [Fact]
    public void ContainmentNeverNamesAFormationNotYetKnowableAtTheBar()
    {
        var futureParent = new ChartPattern(
            Kind: ChartPatternKind.DoubleBottom, State: ChartPatternState.Forming,
            StartBarIndex: 20, EndBarIndex: 120, KnownAtIndex: 125,
            TriggerLevel: 100, StartTime: new DateTime(2026, 1, 21), EndTime: default, ExpiresAtIndex: 300);
        var flag = new ChartPattern(
            Kind: ChartPatternKind.BullFlag, State: ChartPatternState.Forming,
            StartBarIndex: 40, EndBarIndex: 60, KnownAtIndex: 65,
            TriggerLevel: 101, StartTime: new DateTime(2026, 2, 10), EndTime: default, ExpiresAtIndex: 120);

        var h = Build(futureParent, flag);

        Move(h, 64);
        string stepped = Move(h, 65);    // the step path: entering the flag's first bar
        Assert.Contains("bull flag", stepped);
        Assert.DoesNotContain("double bottom", stepped);

        Move(h, 30);
        string jumped = Move(h, 65);     // the jump path: landing on the flag's first bar
        Assert.Contains("bull flag", jumped);
        Assert.DoesNotContain("double bottom", jumped);

        // Positive control: the same parent, knowable before the flag, IS named on the same bar —
        // so the silence above is the knowability rule and not a clause that never speaks.
        var knowable = Build(futureParent with { KnownAtIndex = 62 }, flag);
        Move(knowable, 64);
        Assert.Contains("Inside a larger double bottom that began 21 January.", Move(knowable, 65));
    }
}
