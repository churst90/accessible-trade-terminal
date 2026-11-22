using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// Shift+F12 (and P) with no series focused said nothing: the Properties dialog found no series
/// and returned, which to a screen-reader user is the same as an unbound key (A3 F3).
/// </summary>
public sealed class PropertiesWithNothingFocusedTests
{
    private static (CommandDispatcher Dispatcher, SpyEventBus Bus) Build(string? focusedId)
    {
        var config = new SeriesConfig { Id = CoreSeriesIds.Candles, IndicatorCode = "CANDLES", Name = "Candles", Pane = "Main" };
        var candles = new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id });
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(new List<Ohlcv>
            {
                new(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), 100, 101, 99, 100, 1),
            }),
            ActiveSeries = ImmutableList.Create(candles),
            PrimarySeriesId = candles.Id,
            FocusedSeriesId = focusedId,
            CurrentDataIndex = 0,
        });
        var dispatcher = new CommandDispatcher(bus, Substitute.For<INavigationEngine>(), store,
            Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));
        dispatcher.SetChartActive(true);
        return (dispatcher, bus);
    }

    [Fact]
    public void With_nothing_focused_it_says_so_and_opens_nothing()
    {
        var (dispatcher, bus) = Build(focusedId: null);
        dispatcher.Dispatch(SystemCommand.OpenProperties);

        Assert.Empty(bus.Log.OfType<OpenPropertiesEvent>());
        var said = Assert.Single(bus.Log.OfType<FeedbackRequestEvent>());
        Assert.Contains("No series focused", said.Message);
    }

    [Fact]
    public void With_a_series_focused_it_opens()
    {
        var (dispatcher, bus) = Build(focusedId: CoreSeriesIds.Candles);
        dispatcher.Dispatch(SystemCommand.OpenProperties);

        Assert.Single(bus.Log.OfType<OpenPropertiesEvent>());
        Assert.Empty(bus.Log.OfType<FeedbackRequestEvent>());
    }
}
