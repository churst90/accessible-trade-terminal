using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The Go bar's one dispatcher entry point: the chart-focus gate is skipped, the modal trap
/// is not, and the keyboard path keeps dropping chart commands when the chart has no focus.
/// </summary>
public class CommandDispatcherGoBarTests
{
    private static Ohlcv Bar(double close, int minute) => new(
        new DateTime(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc),
        close - 1, close + 1, close - 2, close, 1000);

    private static WorkspaceState LoadedState()
    {
        var config = new SeriesConfig { Id = "candles", IndicatorCode = "candles", Name = "Price" };
        config.Components.Add(new ComponentConfig { Name = "Body", DisplayName = "Body", IsVisible = true });
        var candles = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "candles" });
        return WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(
                Enumerable.Range(0, 5).Select(i => Bar(100 + i, i)).ToList()),
            ActiveSeries = System.Collections.Immutable.ImmutableList.Create(candles),
            PrimarySeriesId = "candles",
            FocusedSeriesId = "candles",
            CurrentDataIndex = 4,
        };
    }

    private static (CommandDispatcher Dispatcher, SpyEventBus Bus, INavigationEngine Nav,
                    ModalStack ModalStack) Build(DemoPolicy? demo = null)
    {
        var bus = new SpyEventBus();
        var nav = Substitute.For<INavigationEngine>();
        var store = new MockWorkspaceStore();
        store.EmitState(LoadedState());
        var barDetail = Substitute.For<IBarDetailService>();
        var crossing = new IndicatorCrossingEngine(store, bus);
        var modalBus = new EventBus();
        var stack = new ModalStack(modalBus);
        var dispatcher = new CommandDispatcher(
            bus, nav, store, barDetail, crossing, modalStack: stack, demo: demo);
        return (dispatcher, bus, nav, stack);
    }

    [Fact]
    public void DispatchFromGoBarRunsAChartScopedCommandWithTheChartUnfocused()
    {
        var (dispatcher, bus, nav, _) = Build();

        dispatcher.DispatchFromGoBar(SystemCommand.NavRight);

        nav.Received(1).ProcessNavigation("NAV_RIGHT");
        Assert.Contains(bus.Log, e => e is RequestChartFocusEvent);
    }

    [Fact]
    public void TheKeyboardPathStillDropsAChartCommandWhenTheChartHasNoFocus()
    {
        var (dispatcher, _, nav, _) = Build();

        dispatcher.Dispatch(SystemCommand.NavRight);

        nav.DidNotReceiveWithAnyArgs().ProcessNavigation(Arg.Any<string>());
    }

    [Theory]
    [InlineData(SystemCommand.SaveIndicatorTemplate)]
    [InlineData(SystemCommand.ApplyIndicatorTemplate)]
    public void TheIndicatorTemplateCommandsOpenTheirDialogs(SystemCommand command)
    {
        var (dispatcher, bus, _, _) = Build();

        dispatcher.Dispatch(command);

        if (command == SystemCommand.SaveIndicatorTemplate)
            Assert.Contains(bus.Log, e => e is OpenSaveIndicatorTemplateEvent);
        else
            Assert.Contains(bus.Log, e => e is OpenApplyIndicatorTemplateEvent);
    }

    [Theory]
    [InlineData(SystemCommand.SaveIndicatorTemplate)]
    [InlineData(SystemCommand.ApplyIndicatorTemplate)]
    public void TheIndicatorTemplateCommandsRefuseInThePublicDemo(SystemCommand command)
    {
        var (dispatcher, bus, _, _) = Build(new DemoPolicy(isDemo: true));

        dispatcher.Dispatch(command);

        Assert.Contains(bus.Log, e => e is FeedbackRequestEvent { Message: "Not in the public demo." });
        Assert.DoesNotContain(bus.Log, e => e is OpenSaveIndicatorTemplateEvent or OpenApplyIndicatorTemplateEvent);
    }

    [Fact]
    public void DispatchFromGoBarStillObeysTheModalTrap()
    {
        var (dispatcher, bus, _, stack) = Build();
        stack.Apply(new ModalStateChangedEvent(true, "Settings"));

        dispatcher.DispatchFromGoBar(SystemCommand.OpenSettings);

        Assert.Empty(bus.Log.OfType<OpenSettingsEvent>());
    }

    /// <summary>
    /// From the keyboard, Enter-to-place with nothing armed passes through unanswered: Shift+Enter
    /// and Ctrl+Enter are chords people press for other reasons. From the Go bar the user typed
    /// the command's name, so silence would read as "it did nothing"; it says what is armed.
    /// </summary>
    [Theory]
    [InlineData(SystemCommand.QuickPlaceMarket)]
    [InlineData(SystemCommand.QuickPlaceLimit)]
    public void APlaceCommandFromTheGoBarWithNothingArmedSaysSo(SystemCommand command)
    {
        var quick = Substitute.For<AccessibleTrader.Core.Services.Trading.IQuickTradeService>();
        quick.State.Returns(AccessibleTrader.Core.Services.Trading.QuickTradeState.Idle);
        var store = new MockWorkspaceStore();
        store.EmitState(LoadedState());
        var dispatcher = new CommandDispatcher(new SpyEventBus(), Substitute.For<INavigationEngine>(),
            store, Substitute.For<IBarDetailService>(),
            new IndicatorCrossingEngine(new MockWorkspaceStore(), new SpyEventBus()),
            quickTrade: quick, modalStack: new ModalStack(new EventBus()));

        dispatcher.SetChartActive(true);
        dispatcher.Dispatch(command);
        quick.DidNotReceive().Announce();

        dispatcher.SetChartActive(false);
        dispatcher.DispatchFromGoBar(command);
        quick.Received(1).Announce();
        quick.DidNotReceiveWithAnyArgs().Place(default);
    }

    /// <summary>
    /// With no chart loaded, most chart commands (zoom, draw, quick trade) fall through the
    /// data check without a word: from the keyboard that is a key on an empty chart, but a
    /// command typed by name must say why it did nothing.
    /// </summary>
    [Theory]
    [InlineData(SystemCommand.ZoomIn)]
    [InlineData(SystemCommand.DrawTrend)]
    [InlineData(SystemCommand.QuickArmRisk2)]
    public void AChartCommandFromTheGoBarWithNoChartSaysSo(SystemCommand command)
    {
        var bus = new SpyEventBus();
        var dispatcher = new CommandDispatcher(bus, Substitute.For<INavigationEngine>(),
            new MockWorkspaceStore(), Substitute.For<IBarDetailService>(),
            new IndicatorCrossingEngine(new MockWorkspaceStore(), new SpyEventBus()),
            modalStack: new ModalStack(new EventBus()));

        dispatcher.DispatchFromGoBar(command);

        var said = Assert.Single(bus.Log.OfType<FeedbackRequestEvent>());
        Assert.Equal("No chart loaded.", said.Message);
    }
}
