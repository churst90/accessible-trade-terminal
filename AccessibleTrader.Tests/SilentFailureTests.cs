using System;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// The feedback contract's hardest rule: <b>a key that does nothing must say why.</b>
///
/// <para>
/// For a sighted user a no-op key is self-evident — the screen simply did not change. For a screen
/// reader user, "the key did nothing" and "the key is not bound" and "the application is busy" are
/// all the same experience: silence. So every path that declines to act has to explain itself, and
/// the ones that fail to are invisible in exactly the way that makes them survive code review.
/// </para>
///
/// <para>
/// These tests exist because two such paths shipped. Both are pinned below.
/// </para>
/// </summary>
public class SilentFailureTests
{
    // ── Boundary events used to swallow their own message ───────────────────────

    /// <summary>
    /// <c>FeedbackType.Boundary</c> was earcon-only, so any message published with it was
    /// discarded. <b>Ten call sites passed one.</b> Among them: "No more [component] signals in this
    /// direction" — which <c>SHORTCUTS.md</c> documents as spoken and which had never spoken since
    /// the day it was written — plus "Focused trendline has no anchors", "Focused trendline anchors
    /// are off-chart", and "No chart formations on this chart".
    ///
    /// <para>
    /// Each explains why a key the user just pressed did nothing, which is the one case where
    /// silence is indistinguishable from a broken binding.
    /// </para>
    /// </summary>
    [Fact]
    public void ABoundaryEventCarryingAMessageSpeaksIt()
    {
        var h = new Harness();

        h.Bus.Publish(new FeedbackRequestEvent(
            FeedbackType.Boundary, "No more Cipher B signals in this direction"));

        Assert.Contains(h.Speech.SpokenTexts, t => t.Contains("No more Cipher B signals"));
    }

    /// <summary>
    /// The deliberate exception, pinned so it is not "fixed" by someone applying the rule above
    /// mechanically. Reaching the end of the chart is a routine event that does not need a sentence
    /// every single time, so a bare boundary stays earcon-only.
    /// </summary>
    [Fact]
    public void ABareBoundaryStaysEarconOnly()
    {
        var h = new Harness();

        h.Bus.Publish(new FeedbackRequestEvent(FeedbackType.Boundary, null, false));

        Assert.Empty(h.Speech.SpokenTexts);
    }

    // ── The formation jump keys ─────────────────────────────────────────────────

    /// <summary>
    /// Comma and period used to move the cursor with formation description switched off — jumping
    /// the user across the chart and then saying nothing about why, because the announcement that
    /// would explain the jump is precisely what the setting disables.
    ///
    /// <para>
    /// Being teleported with no explanation is worse than the key doing nothing: the user cannot
    /// tell whether it worked, and has lost their place either way.
    /// </para>
    /// </summary>
    [Fact]
    public void TheJumpKeysRefuseAndExplainWhenDescriptionIsOff()
    {
        var store = StoreWith(StateWithBars(describePatterns: false));
        var bus = new SpyEventBus();
        var nav = new ChartPatternNavigator(store, bus, new ChartPatternCache(
            new ChartPatternDetector(new SwingStructureAnalyzer())), new ChartPatternFocus());

        int before = store.State.CurrentDataIndex;
        nav.Jump(SystemCommand.NavPatternNext);

        Assert.Equal(before, store.State.CurrentDataIndex);   // did not move
        Assert.Contains(bus.Log.OfType<FeedbackRequestEvent>(),
            e => e.Message != null && e.Message.Contains("Settings"));
    }

    /// <summary>With the setting on, the key is free to act.</summary>
    [Fact]
    public void TheJumpKeysDoNotRefuseWhenDescriptionIsOn()
    {
        var store = StoreWith(StateWithBars(describePatterns: true));
        var bus = new SpyEventBus();
        var nav = new ChartPatternNavigator(store, bus, new ChartPatternCache(
            new ChartPatternDetector(new SwingStructureAnalyzer())), new ChartPatternFocus());

        nav.Jump(SystemCommand.NavPatternNext);

        Assert.DoesNotContain(bus.Log.OfType<FeedbackRequestEvent>(),
            e => e.Message != null && e.Message.Contains("Settings"));
    }

    /// <summary>
    /// A chart with no formations at all must still answer. "There are none" and "the key is
    /// broken" are the same silence otherwise, and the second is what gets reported as a bug.
    /// </summary>
    [Fact]
    public void AChartWithNoFormationsSaysSoRatherThanStayingSilent()
    {
        // A straight line has no swings, so the detector finds nothing.
        var flat = Enumerable.Range(0, 200).Select(i => new Ohlcv
        {
            Date = new DateTime(2024, 1, 1).AddDays(i),
            Open = 100, High = 100, Low = 100, Close = 100, Volume = 1
        }).ToList();

        var store = StoreWith(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(flat.ToArray()),
            CurrentDataIndex = 100,
            DescribeChartPatterns = true,
        });
        var bus = new SpyEventBus();
        var nav = new ChartPatternNavigator(store, bus, new ChartPatternCache(
            new ChartPatternDetector(new SwingStructureAnalyzer())), new ChartPatternFocus());

        nav.Jump(SystemCommand.NavPatternNext);

        Assert.Contains(bus.Log.OfType<FeedbackRequestEvent>(),
            e => e.Message != null && e.Message.Contains("formation", StringComparison.OrdinalIgnoreCase));
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────

    /// <summary>MockWorkspaceStore starts at Initial and is driven by EmitState.</summary>
    private static MockWorkspaceStore StoreWith(WorkspaceState state)
    {
        var s = new MockWorkspaceStore();
        s.EmitState(state);
        return s;
    }

    private static WorkspaceState StateWithBars(bool describePatterns)
    {
        var rng = new Random(7);
        double px = 100;
        var bars = new Ohlcv[400];
        for (int i = 0; i < bars.Length; i++)
        {
            px *= 1 + (rng.NextDouble() - 0.5) * 0.04;
            bars[i] = new Ohlcv
            {
                Date = new DateTime(2024, 1, 1).AddDays(i),
                Open = px, Close = px,
                High = px * 1.005, Low = px * 0.995, Volume = 1000
            };
        }

        return WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = 50,
            DescribeChartPatterns = describePatterns,
        };
    }

    /// <summary>
    /// The coordinator wired to spies. Constructing it is what subscribes the feedback handlers,
    /// so the object must be held even though nothing is called on it directly.
    /// </summary>
    private sealed class Harness
    {
        public SpyEventBus Bus { get; } = new();
        public SpySpeechRouter Speech { get; } = new();
        public AccessibilityFeedbackCoordinator Coordinator { get; }

        public Harness()
        {
            Coordinator = new AccessibilityFeedbackCoordinator(
                StoreWith(WorkspaceState.Initial),
                new MockNavManager(),
                Speech,
                new MockAudioRouter(),
                new SpeechFormatter(),
                Bus,
                new MockEarconService(),
                new SdkCandlePatternAnalyzer(),
                new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())), new ChartPatternFocus(),
                new MockAutoNarrationService());
        }
    }
}
