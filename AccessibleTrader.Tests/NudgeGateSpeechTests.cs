using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The two gates in <see cref="CommandDispatcher.Dispatch"/> that used to drop an anchor-nudge
/// chord with no sound — indistinguishable from an unbound key, which is why the whole feature
/// read as "the commands don't work" (reported from real use, 2026-09-03).
///
/// <para>
/// Both are BOUNDARY tier: the key was understood and cannot act right now. The earcon answers
/// every press; the sentence is spoken once and not again until the situation changes. Error
/// tier would speak on the channel F2 cannot mute, fifteen times a second under a held key.
/// </para>
///
/// <para>
/// And the strongest rule is not to need the refusal: the Object Tree is where a drawing is
/// focused, so the nudge RUNS under it and is refused only under an editing dialog.
/// </para>
/// </summary>
public class NudgeGateSpeechTests
{
    private static Ohlcv Bar(double close, int minute) => new(
        new DateTime(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc),
        close - 1, close + 1, close - 2, close, 1000);

    /// <summary>A loaded chart: candles plus one trend line. The FOCUSED series is the candles
    /// unless <paramref name="focusDrawing"/>, because that is where a user opening the tree
    /// usually is — and the difference is a spoken sentence.</summary>
    private static WorkspaceState LoadedState(bool focusDrawing = false)
    {
        var config = new SeriesConfig { Id = "candles", IndicatorCode = "candles", Name = "Price" };
        config.Components.Add(new ComponentConfig { Name = "Body", DisplayName = "Body", IsVisible = true });
        var candles = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "candles" });
        var lineCfg = new SeriesConfig { Id = "draw-1", IndicatorCode = "TrendLine", Name = "Trend line (1)" };
        lineCfg.Components.Add(new ComponentConfig { Name = "Line", DisplayName = "Line", IsVisible = true });
        var line = new ChartSeries(lineCfg, new SeriesDataBuffer { SeriesId = "draw-1" })
        {
            Drawing = new DrawingData { Type = DrawingType.TrendLine },
        };
        return WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, 5).Select(i => Bar(100 + i, i)).ToList()),
            ActiveSeries = System.Collections.Immutable.ImmutableList.Create(candles, line),
            PrimarySeriesId = "candles",
            FocusedSeriesId = focusDrawing ? "draw-1" : "candles",
            CurrentDataIndex = 4,
        };
    }

    private static (CommandDispatcher dispatcher, SpyEventBus bus) Build(bool focusDrawing = false)
    {
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        store.EmitState(LoadedState(focusDrawing));
        var dispatcher = new CommandDispatcher(bus, Substitute.For<INavigationEngine>(), store,
            Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));
        return (dispatcher, bus);
    }

    private static List<FeedbackRequestEvent> Feedback(SpyEventBus bus) => bus.Log.OfType<FeedbackRequestEvent>().ToList();
    private static int Nudges(SpyEventBus bus) => bus.Log.OfType<NudgeDrawingAnchorEvent>().Count();

    // ── Gate 2: the chart does not have focus ───────────────────────────────

    [Fact]
    public void OffChart_TheFirstPressSpeaksTheRemedy_AndEveryPressPlaysTheEarcon()
    {
        var (dispatcher, bus) = Build();

        dispatcher.Dispatch(SystemCommand.NudgeAnchorLater);
        dispatcher.Dispatch(SystemCommand.NudgeAnchorLater);
        dispatcher.Dispatch(SystemCommand.NudgeAnchorUp);

        var fb = Feedback(bus);
        Assert.Equal(0, Nudges(bus));
        Assert.Equal(3, fb.Count);                                   // an earcon per press
        Assert.All(fb, f => Assert.Equal(FeedbackType.Boundary, f.Type));
        Assert.Equal("The chart does not have focus. Control Alt Shift C returns to the chart.", fb[0].Message);
        Assert.Null(fb[1].Message);                                  // the sentence once
        Assert.Null(fb[2].Message);
    }

    [Fact]
    public void OffChart_TheSentenceReturnsAfterFocusChanges()
    {
        var (dispatcher, bus) = Build();
        dispatcher.Dispatch(SystemCommand.NudgeAnchorLater);
        dispatcher.SetChartActive(true);
        dispatcher.SetChartActive(false);
        dispatcher.Dispatch(SystemCommand.NudgeAnchorLater);

        var spoken = Feedback(bus).Where(f => f.Message != null).ToList();
        Assert.Equal(2, spoken.Count);
    }

    [Fact]
    public void OffChart_OtherChartScopedCommandsStaySilent()
    {
        // An arrow key with focus on a toolbar button belongs to the button. Only the nudge
        // chords — which nothing else answers to — earn the refusal.
        var (dispatcher, bus) = Build();
        dispatcher.Dispatch(SystemCommand.NavRight);
        dispatcher.Dispatch(SystemCommand.ZoomIn);
        Assert.Empty(Feedback(bus));
    }

    // ── Gate 1: a modal is open ─────────────────────────────────────────────

    [Fact]
    public void UnderAnEditingDialog_TheRefusalNamesTheDialog_Once()
    {
        var (dispatcher, bus) = Build();
        dispatcher.SetChartActive(true);
        bus.Publish(new ModalStateChangedEvent(true, "Properties"));

        dispatcher.Dispatch(SystemCommand.NudgeAnchorLater);
        dispatcher.Dispatch(SystemCommand.NudgeAnchorLater);

        var fb = Feedback(bus);
        Assert.Equal(0, Nudges(bus));
        Assert.Equal(2, fb.Count);
        Assert.Equal("Not while Properties is open. Escape closes it.", fb[0].Message);
        Assert.Null(fb[1].Message);
    }

    [Fact]
    public void UnderAnEditingDialog_TheSentenceReturnsWhenTheStackChanges()
    {
        var (dispatcher, bus) = Build();
        dispatcher.SetChartActive(true);
        bus.Publish(new ModalStateChangedEvent(true, "Properties"));
        dispatcher.Dispatch(SystemCommand.NudgeAnchorLater);
        bus.Publish(new ModalStateChangedEvent(true, "LabelText"));
        dispatcher.Dispatch(SystemCommand.NudgeAnchorLater);

        var spoken = Feedback(bus).Where(f => f.Message != null).Select(f => f.Message).ToList();
        Assert.Equal(new[]
        {
            "Not while Properties is open. Escape closes it.",
            "Not while Label Text is open. Escape closes it.",     // CamelCase split for the synthesiser
        }, spoken);
    }

    [Fact]
    public void UnderTheObjectTree_TheNudgeRuns_EvenThoughTheChartDoesNotHaveFocus()
    {
        var (dispatcher, bus) = Build(focusDrawing: true);
        bus.Publish(new ModalStateChangedEvent(true, CommandDispatcher.ObjectTreeModalName));

        dispatcher.Dispatch(SystemCommand.NudgeAnchorLater);
        dispatcher.Dispatch(SystemCommand.CycleDrawingAnchor);
        dispatcher.Dispatch(SystemCommand.SnapAnchorToBar);

        Assert.Equal(1, Nudges(bus));
        Assert.Single(bus.Log.OfType<CycleDrawingAnchorEvent>());
        Assert.Single(bus.Log.OfType<SnapDrawingAnchorEvent>());
        Assert.Empty(Feedback(bus));
    }

    [Fact]
    public void UnderTheObjectTree_OtherChartCommandsAreStillTheDialogs()
    {
        // The allowance is for the six nudge chords, not a hole in the modal trap.
        var (dispatcher, bus) = Build();
        var nav = Substitute.For<INavigationEngine>();
        bus.Publish(new ModalStateChangedEvent(true, CommandDispatcher.ObjectTreeModalName));
        dispatcher.Dispatch(SystemCommand.NavRight);
        dispatcher.Dispatch(SystemCommand.PlayChart);
        nav.DidNotReceive().ProcessNavigation(Arg.Any<string>());
        Assert.DoesNotContain(bus.Log, e => e is not ModalStateChangedEvent && e is not FeedbackRequestEvent);
    }

    [Fact]
    public void AnEditingDialogOnTopOfTheTree_IsRefused_NamingTheTopDialog()
    {
        var (dispatcher, bus) = Build(focusDrawing: true);
        bus.Publish(new ModalStateChangedEvent(true, CommandDispatcher.ObjectTreeModalName));
        bus.Publish(new ModalStateChangedEvent(true, "Properties"));
        dispatcher.Dispatch(SystemCommand.NudgeAnchorLater);
        Assert.Equal(0, Nudges(bus));
        Assert.Equal("Not while Properties is open. Escape closes it.", Assert.Single(Feedback(bus)).Message);

        // Close Properties: the tree is top again and the nudge runs.
        bus.Publish(new ModalStateChangedEvent(false, "Properties"));
        dispatcher.Dispatch(SystemCommand.NudgeAnchorLater);
        Assert.Equal(1, Nudges(bus));
    }

    [Fact]
    public void UnderTheObjectTree_WithNoDrawingFocused_TheRemedyIsATreeKey()
    {
        // The manager's own refusal says "Page Up and Page Down move between series" — chart
        // keys the tree does not honour. The tree is selection-follows-focus: arrowing onto a
        // series row focuses it on the chart (ObjectTreeSelectionFollowsFocusTests), so the
        // remedy is the arrow key. A remedy naming the wrong key is worse than no remedy, so the
        // dispatcher answers this one itself.
        var (dispatcher, bus) = Build(focusDrawing: false);
        bus.Publish(new ModalStateChangedEvent(true, CommandDispatcher.ObjectTreeModalName));

        dispatcher.Dispatch(SystemCommand.NudgeAnchorLater);
        dispatcher.Dispatch(SystemCommand.NudgeAnchorLater);

        var fb = Feedback(bus);
        Assert.Equal(0, Nudges(bus));
        Assert.Equal(2, fb.Count);
        Assert.Equal("Focus a drawing first. Arrow to its row in the tree.", fb[0].Message);
        Assert.DoesNotContain("Page", fb[0].Message);
        Assert.Null(fb[1].Message);
    }

    [Theory]
    [InlineData("Object tree", true)]
    [InlineData("OBJECT TREE", true)]
    [InlineData("Properties", false)]
    [InlineData(null, false)]
    public void OnlyTheObjectTreeAllowsTheNudge(string? top, bool allowed) =>
        Assert.Equal(allowed, CommandDispatcher.NudgeAllowedUnder(top));

    [Theory]
    [InlineData("Properties", "Properties")]
    [InlineData("LabelText", "Label Text")]
    [InlineData("ThemeEditor", "Theme Editor")]
    [InlineData("Order book", "Order book")]
    [InlineData("AI Analyst", "AI Analyst")]
    [InlineData(null, "a dialog")]
    public void ModalNamesAreSpokenAsWords(string? name, string spoken) =>
        Assert.Equal(spoken, CommandDispatcher.SpokenModalName(name));
}
