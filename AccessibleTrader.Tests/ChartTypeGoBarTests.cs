using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.GoBar;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The four chart types are Go bar commands as well as a toolbar select. "candles" used to be
/// an alias of the Heikin-Ashi TOGGLE — typing it turned HA on — and now belongs to the candle
/// chart. On an analytics tab every one of them refuses aloud and changes nothing.
/// </summary>
public sealed class ChartTypeGoBarTests
{
    private static GoBarEntry Entry(SystemCommand command) =>
        GoBarRegistry.All.Single(e => e.Command == command);

    [Theory]
    [InlineData(SystemCommand.ShowCandleChart, "CANDLE", "candle chart")]
    [InlineData(SystemCommand.ShowHeikinAshiChart, null, "heikin ashi chart")]
    [InlineData(SystemCommand.ShowLineChart, "LINE", "line chart")]
    [InlineData(SystemCommand.ShowAreaChart, "AREA", "area chart")]
    public void Each_chart_type_is_reachable_by_its_own_words(SystemCommand command, string? code, string alias)
    {
        var entry = Entry(command);

        foreach (string text in new[] { entry.Name, alias }.Concat(code is null ? Array.Empty<string>() : new[] { code }))
        {
            var top = GoBarMatcher.Match(text, GoBarRegistry.All).FirstOrDefault()?.Entry;
            Assert.Equal(command, top?.Command);
        }
    }

    [Fact]
    public void Typing_candles_now_reaches_the_candle_chart_not_the_Heikin_Ashi_toggle()
    {
        var top = GoBarMatcher.Match("candles", GoBarRegistry.All).FirstOrDefault()?.Entry;

        Assert.Equal(SystemCommand.ShowCandleChart, top?.Command);
        Assert.DoesNotContain("candles", Entry(SystemCommand.ToggleHeikinAshi).Aliases);
    }

    // ── Dispatch ──────────────────────────────────────────────────────────

    private static (CommandDispatcher dispatcher, MockWorkspaceStore store, SpyEventBus bus)
        Build(PriceChartStyle style = PriceChartStyle.Candles, bool heikinAshi = false,
              ProviderDataShape shape = ProviderDataShape.Ohlcv)
    {
        var bus = new SpyEventBus();
        var nav = Substitute.For<INavigationEngine>();
        var store = new MockWorkspaceStore();
        var config = new SeriesConfig { Id = "candles", Name = "Candles" };
        store.EmitState(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(new[]
            {
                new Ohlcv(DateTime.UtcNow, 100, 101, 99, 100.5, 1000),
            }),
            ActiveSeries = ImmutableList.Create(new ChartSeries(config, new SeriesDataBuffer { SeriesId = "candles" })),
            FocusedSeriesId = "candles",
            ChartStyle = style,
            IsHeikinAshi = heikinAshi,
            CurrentDataShape = shape,
        });
        var barDetail = Substitute.For<IBarDetailService>();
        var crossing = new IndicatorCrossingEngine(store, bus);
        return (new CommandDispatcher(bus, nav, store, barDetail, crossing), store, bus);
    }

    [Theory]
    [InlineData(SystemCommand.ShowCandleChart, PriceChartStyle.Candles, false)]
    [InlineData(SystemCommand.ShowHeikinAshiChart, PriceChartStyle.Candles, true)]
    [InlineData(SystemCommand.ShowLineChart, PriceChartStyle.Line, false)]
    [InlineData(SystemCommand.ShowAreaChart, PriceChartStyle.Area, false)]
    public void The_commands_dispatch_the_chosen_type(SystemCommand command, PriceChartStyle style, bool heikinAshi)
    {
        // Start on a different choice each time, so "already" cannot satisfy this.
        var from = command == SystemCommand.ShowAreaChart ? PriceChartStyle.Candles : PriceChartStyle.Area;
        var (dispatcher, store, _) = Build(from);

        dispatcher.DispatchFromGoBar(command);

        var action = Assert.Single(store.DispatchedActions.OfType<SetChartStyleAction>());
        Assert.Equal(style, action.Style);
        Assert.Equal(heikinAshi, action.HeikinAshi);
    }

    [Fact]
    public void Choosing_the_type_already_shown_says_already_and_changes_nothing()
    {
        var (dispatcher, store, bus) = Build(PriceChartStyle.Line);

        dispatcher.DispatchFromGoBar(SystemCommand.ShowLineChart);

        Assert.Empty(store.DispatchedActions.OfType<SetChartStyleAction>());
        Assert.Contains(bus.Log.OfType<FeedbackRequestEvent>(),
            e => e.Message == "Already a line chart.");
    }

    [Fact]
    public void An_area_chart_already_on_screen_says_already_an_area_chart()
    {
        var (dispatcher, store, bus) = Build(PriceChartStyle.Area);

        dispatcher.DispatchFromGoBar(SystemCommand.ShowAreaChart);

        Assert.Empty(store.DispatchedActions.OfType<SetChartStyleAction>());
        Assert.Contains(bus.Log.OfType<FeedbackRequestEvent>(),
            e => e.Message == "Already an area chart.");
    }

    [Fact]
    public void On_an_analytics_chart_every_chart_type_refuses_aloud_and_changes_nothing()
    {
        foreach (var command in new[]
                 {
                     SystemCommand.ShowCandleChart, SystemCommand.ShowHeikinAshiChart,
                     SystemCommand.ShowLineChart, SystemCommand.ShowAreaChart,
                     SystemCommand.ToggleHeikinAshi,
                 })
        {
            var (dispatcher, store, bus) = Build(shape: ProviderDataShape.SingleValueLine);

            dispatcher.DispatchFromGoBar(command);

            Assert.Empty(store.DispatchedActions.OfType<SetChartStyleAction>());
            Assert.Empty(store.DispatchedActions.OfType<ToggleHeikinAshiAction>());
            Assert.Contains(bus.Log.OfType<FeedbackRequestEvent>(),
                e => e.Message == "This chart is already a single line; chart types are for price bars.");
        }
    }
}
