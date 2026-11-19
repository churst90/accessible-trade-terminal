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
                    ModalStack ModalStack) Build()
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
            bus, nav, store, barDetail, crossing, modalStack: stack);
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

    [Fact]
    public void DispatchFromGoBarStillObeysTheModalTrap()
    {
        var (dispatcher, bus, _, stack) = Build();
        stack.Apply(new ModalStateChangedEvent(true, "Settings"));

        dispatcher.DispatchFromGoBar(SystemCommand.OpenSettings);

        Assert.Empty(bus.Log.OfType<OpenSettingsEvent>());
    }
}
