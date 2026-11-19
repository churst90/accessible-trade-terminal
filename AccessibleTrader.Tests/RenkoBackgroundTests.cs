using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// A Renko tab away from the front: its ALERTS read the bricks it draws, while the strategies
/// and the paper/fill path keep the real bars. The focused chart and the background monitor must
/// agree about what the same saved tab shows.
/// </summary>
public sealed class RenkoBackgroundTests
{
    private static readonly ChartIdentity Id = new("Spot", "Bitstamp", "KAS/USDT", "1h");

    private static List<Ohlcv> Bars(int n = 60)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, n)
            .Select(i => new Ohlcv(start.AddHours(i), 100 + i, 101 + i, 99 + i, 100 + i, 100))
            .ToList();
    }

    private static AlertDefinition Alert() => new()
    {
        Id = "a1",
        Name = "Price",
        Target = AlertTarget.Price,
        Condition = AlertCondition.CrossesAbove,
        Threshold = 100,
        Delivery = AlertDelivery.Both,
        Symbol = "KAS/USDT",
    };

    private static (BackgroundWorkspaceMonitor Monitor, IAlertEvaluator Evaluator, IStrategyEngine Engine) Monitor(
        IReadOnlyList<AlertDefinition> alerts, IReadOnlyList<ActiveStrategy>? strategies = null)
    {
        var data = Substitute.For<IMarketFeeds>();
        data.GetBarsAsync(Arg.Any<ChartIdentity>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Ohlcv>)Bars());

        var alertHub = Substitute.For<IAlertOrchestrator>();
        alertHub.GetAlerts().Returns(alerts.ToList());
        var evaluator = Substitute.For<IAlertEvaluator>();
        evaluator.EvaluateAlerts(
                Arg.Any<IReadOnlyList<AlertDefinition>>(), Arg.Any<WorkspaceState>(),
                Arg.Any<Ohlcv>(), Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyDictionary<string, double>>())
            .Returns(Enumerable.Empty<AlertFired>());

        var engine = Substitute.For<IStrategyEngine>();
        engine.ActiveStrategies.Returns(strategies ?? (IReadOnlyList<ActiveStrategy>)Array.Empty<ActiveStrategy>());

        var monitor = new BackgroundWorkspaceMonitor(
            Id, "KAS/USDT", ImmutableList<ChartSeries>.Empty,
            data, Substitute.For<IIndicatorService>(), alertHub, evaluator, engine,
            new SpyEventBus(), NullLogger.Instance, TimeSpan.FromHours(1),
            chartStyle: PriceChartStyle.Renko,
            renko: new RenkoSettings(RenkoBoxMode.Fixed, 10),
            brickBoxSize: 10);

        return (monitor, evaluator, engine);
    }

    [Fact]
    public async Task A_Renko_tabs_alerts_read_bricks_built_with_its_frozen_box()
    {
        var (monitor, evaluator, _) = Monitor(new[] { Alert() });
        WorkspaceState? seen = null;
        evaluator.EvaluateAlerts(
                Arg.Any<IReadOnlyList<AlertDefinition>>(),
                Arg.Do<WorkspaceState>(s => seen = s),
                Arg.Any<Ohlcv>(), Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyDictionary<string, double>>())
            .Returns(Enumerable.Empty<AlertFired>());

        await monitor.EvaluateOnceAsync(CancellationToken.None); // warm-up
        await monitor.EvaluateOnceAsync(CancellationToken.None); // evaluates

        Assert.NotNull(seen);
        Assert.Equal(PriceChartStyle.Renko, seen!.ChartStyle);
        Assert.Equal(10, seen.BrickBoxSize, 12);
        Assert.Equal(seen.Data.Count, seen.BrickSpans.Length);
        Assert.Equal(Bars().Count, seen.TimeBars!.Count);
        Assert.Equal(Bars()[^1].Close, seen.MarketBars[^1].Close);
        Assert.NotEqual(seen.Data[^1].Close, seen.MarketBars[^1].Close);
    }

    [Fact]
    public async Task Strategies_in_the_monitor_still_read_the_real_bars()
    {
        var strategy = Substitute.For<ITradingStrategy>();
        strategy.Name.Returns("Test");
        var active = new ActiveStrategy("i1", strategy, new Dictionary<string, object>(),
            StrategyExecutionMode.Suggestion, IsPaused: false, Symbol: "KAS/USDT", Chart: Id);

        var (monitor, _, _) = Monitor(Array.Empty<AlertDefinition>(), new[] { active });
        WorkspaceState? seen = null;
        strategy.OnBar(Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(),
                Arg.Do<WorkspaceState>(s => seen = s))
            .Returns((StrategySignal?)null);

        await monitor.EvaluateOnceAsync(CancellationToken.None);

        Assert.NotNull(seen);
        Assert.Equal(Bars().Count, seen!.Data.Count);
        Assert.Equal(Bars()[^1].Close, seen.Data[^1].Close);
        Assert.Null(seen.TimeBars);
        Assert.NotEqual(PriceChartStyle.Renko, seen.ChartStyle);
    }

    [Fact]
    public async Task The_headless_chart_bricks_its_state_with_the_tabs_box()
    {
        var config = new SeriesConfig { Id = "candles", IndicatorCode = "CANDLES", Name = "Price", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "close", DisplayName = "Close", IsVisible = true,
            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.PriceAction, DataMapping = "close",
        });
        var series = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "candles" });

        var mapper = Substitute.For<IIndicatorStateMapper>();
        mapper.MapInternalDataToBuffer(Arg.Any<ChartSeries>(), Arg.Any<IReadOnlyList<Ohlcv>>())
            .Returns(ci => new SeriesDataBuffer { SeriesId = "candles" });

        var chart = new HeadlessChart(
            Id, new[] { series },
            Substitute.For<IIndicatorEngine>(), mapper, Substitute.For<IIndicatorContextAnalyzer>(),
            NullLogger.Instance, profiles: null, missingIndicators: null, removedIndicators: null,
            heikinAshi: false, chartStyle: PriceChartStyle.Renko,
            renko: new RenkoSettings(RenkoBoxMode.Fixed, 10), brickBoxSize: 10);

        var observed = await chart.ObserveAsync(Bars(), isFullHistory: true, CancellationToken.None);

        Assert.NotNull(observed.State);
        Assert.Equal(PriceChartStyle.Renko, observed.State!.ChartStyle);
        Assert.Equal(10, observed.State.BrickBoxSize, 12);
        Assert.Equal(observed.State.Data.Count, observed.State.BrickSpans.Length);
        Assert.Equal(Bars().Count, observed.State.TimeBars!.Count);
        Assert.NotEqual(observed.State.Data[^1].Close, observed.State.MarketBars[^1].Close);
    }
}
