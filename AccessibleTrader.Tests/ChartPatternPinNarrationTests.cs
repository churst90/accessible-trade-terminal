using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Xunit;

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
            new NavigationFeedbackManager(speechRouter, formatter, h.Bus, sonify, new MockIndicatorEngine()),
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
    /// The reported defect proper: pin a formation, press the next-formation key, and you must
    /// land on THAT formation's ending — not on whichever edge happened to come first.
    ///
    /// <para>
    /// Here the two shapes break on different bars: the triangle at 80, the double bottom at 90.
    /// Both announcements are individually correct, which is why this survived — the bug is that
    /// the key travelled to the wrong one of them.
    /// </para>
    /// </summary>
    [Fact]
    public void TheJumpKeysTravelToThePinnedFormationsEdges()
    {
        var doubleBottom = Big with { CompletedAtIndex = 90 };
        var triangle = Small with { CompletedAtIndex = 80 };
        var h = Build(doubleBottom, triangle);

        StandOn(h, 70);
        // Pin the double bottom — the one whose end is NOT the next edge on the chart.
        for (int i = 0; i < 4; i++)
        {
            h.Navigator.CycleFocus();
            var ranked = ChartPatternNarrator.ByDominance(new[] { doubleBottom, triangle }).ToList();
            if (h.Focus.Apply(ChartPatternCache.KeyFor(h.Store.State.Identity), ranked)[0].Key
                    .Equals(doubleBottom.Key)) break;
        }

        h.Store.DispatchedActions.Clear();
        h.Navigator.Jump(SystemCommand.NavPatternNext);

        var nav = h.Store.DispatchedActions.OfType<NavigateAction>().LastOrDefault();
        Assert.NotNull(nav);
        Assert.Equal(90, nav!.NewIndex);   // the pinned formation's end, not the triangle's at 80
    }

    /// <summary>
    /// Running out of edges inside a pin is normal — there are only two. Say so in a way that
    /// names the pin and the key that releases it, or it reads as the feature having broken.
    /// </summary>
    [Fact]
    public void RunningOutOfEdgesInsideAPinSaysWhy()
    {
        var h = Build(Big, Small);
        StandOn(h, 70);
        PinTheTriangle(h);

        StandOn(h, 119);   // past both of the pinned formation's edges
        h.Bus.Log.Clear();
        h.Navigator.Jump(SystemCommand.NavPatternNext);

        string said = string.Join(" ", h.Bus.Log.OfType<FeedbackRequestEvent>().Select(e => e.Message));
        Assert.Contains("pinned", said, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ascending triangle", said, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Shift+semicolon", said, StringComparison.OrdinalIgnoreCase);
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
}
