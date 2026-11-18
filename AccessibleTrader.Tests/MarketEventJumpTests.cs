using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>Apostrophe / Shift+apostrophe: the jump lands on the right bar, respects the setting,
/// and never refuses in silence.</summary>
public class MarketEventJumpTests
{
    private static readonly DateTime Now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static (MockWorkspaceStore Store, SpyEventBus Bus, MarketEventNavigator Navigator) Harness(
        WorkspaceState state,
        params MarketEvent[] events)
    {
        var store = new MockWorkspaceStore();
        var bus = new SpyEventBus();
        var cache = new MarketEventBarMapCache(MarketEventTestData.Calendar(events));
        var navigator = new MarketEventNavigator(store, bus, cache);
        store.EmitState(state);
        return (store, bus, navigator);
    }

    private static WorkspaceState StateAt(int index)
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 11, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 13, 0, 0, 0, DateTimeKind.Utc)),
        };
        return MarketEventTestData.State(bars, timeframe: "1d", index: index, level: "all");
    }

    private static MarketEvent MondayEvent() => MarketEventTestData.Event(
        id: "monday", moment: new DateTime(2024, 3, 11, 12, 30, 0, DateTimeKind.Utc));
    private static MarketEvent TuesdayEvent() => MarketEventTestData.Event(
        id: "tuesday", spoken: "PPI", importance: MarketEventImportance.Medium,
        moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));

    [Fact]
    public void NextLandsOnTheNextBarWithAPassingEvent()
    {
        var (store, bus, navigator) = Harness(StateAt(0), TuesdayEvent());

        navigator.Jump(forward: true);

        Assert.Contains(store.DispatchedActions, a => a is NavigateAction n && n.NewIndex == 1);
        Assert.Contains(bus.Log, e => e is FeedbackRequestEvent { Type: FeedbackType.Navigation, IsXMove: true, IsJump: true });
    }

    [Fact]
    public void PreviousLandsOnTheEarlierBarWithAPassingEvent()
    {
        var (store, _, navigator) = Harness(StateAt(2), MondayEvent());

        navigator.Jump(forward: false);

        Assert.Contains(store.DispatchedActions, a => a is NavigateAction n && n.NewIndex == 0);
    }

    [Fact]
    public void NoEventAheadSaysNoLaterMarketEvents()
    {
        var (_, bus, navigator) = Harness(StateAt(2), MondayEvent());

        navigator.Jump(forward: true);

        Assert.Contains(bus.Log, e => e is FeedbackRequestEvent { Type: FeedbackType.Boundary, Message: "No later market events." });
    }

    [Fact]
    public void NoEventBehindSaysNoEarlierMarketEvents()
    {
        var (_, bus, navigator) = Harness(StateAt(0), TuesdayEvent());

        navigator.Jump(forward: false);

        Assert.Contains(bus.Log, e => e is FeedbackRequestEvent { Type: FeedbackType.Boundary, Message: "No earlier market events." });
    }

    [Fact]
    public void LevelOffSaysWhereToTurnItOn()
    {
        var state = StateAt(0) with { MarketEventsLevel = "off" };
        var (_, bus, navigator) = Harness(state, TuesdayEvent());

        navigator.Jump(forward: true);

        Assert.Contains(bus.Log, e => e is FeedbackRequestEvent
        {
            Type: FeedbackType.Boundary,
            Message: "Market events are off. Turn them on in Settings, Narration.",
        });
    }

    [Fact]
    public void AnEventBelowTheLevelIsNotAStop()
    {
        var medium = TuesdayEvent();
        var state = StateAt(0) with { MarketEventsLevel = "importantOnly" };
        var (_, bus, navigator) = Harness(state, medium);

        navigator.Jump(forward: true);

        Assert.Contains(bus.Log, e => e is FeedbackRequestEvent { Message: "No later market events." });
    }

    [Fact]
    public void AnUntickedKindIsNotAStop()
    {
        var fed = MarketEventTestData.Event(
            id: "fed", kind: MarketEventKind.Fed, spoken: "Fed decision",
            moment: new DateTime(2024, 3, 12, 18, 0, 0, DateTimeKind.Utc));
        var state = StateAt(0) with { MarketEventsFed = false };
        var (_, bus, navigator) = Harness(state, fed);

        navigator.Jump(forward: true);

        Assert.Contains(bus.Log, e => e is FeedbackRequestEvent { Message: "No later market events." });
    }

    [Fact]
    public void AnEventOfAnotherMarketIsNotAStop()
    {
        var halving = MarketEventTestData.Event(
            id: "halving", kind: MarketEventKind.Halving, spoken: "Bitcoin halving",
            moment: new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc),
            precision: MarketEventPrecision.DayOnly,
            date: new DateOnly(2024, 3, 12),
            scope: MarketType.Crypto);
        var state = StateAt(0) with { SelectedMarketType = MarketType.Stock };
        var (_, bus, navigator) = Harness(state, halving);

        navigator.Jump(forward: true);

        Assert.Contains(bus.Log, e => e is FeedbackRequestEvent { Message: "No later market events." });
    }
}
