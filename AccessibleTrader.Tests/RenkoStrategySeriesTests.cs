using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Charts;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Strategies;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// A strategy on a Renko chart reads indicators computed on the market's bars. The chart's own
/// series are computed on the bricks and indexed by brick, while every strategy host hands the
/// strategy real bars: read together, "RSI at bar 3" was brick 3's RSI, and an Auto strategy
/// traded on it.
/// </summary>
public sealed class RenkoStrategySeriesTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const string SpyCode = "RENKO_SERIES_SPY";

    /// <summary>Its one component is the closes it was handed: what it computed on is visible.</summary>
    private sealed class SpyProvider : IIndicatorProvider
    {
        public string Name => "Spy";
        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata { Name = "Spy", Code = SpyCode },
            new IndicatorMetadata { Name = "Candles", Code = "CANDLES" },
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            // The candle series fails to recompute, so the chart's copy (brick prices) is what is
            // left: the case emptying the price lines exists for.
            if (code == "CANDLES") throw new InvalidOperationException("candles not recomputed in this test");
            var close = buffer.GetComponentSpan("close");
            for (int i = 0; i < data.Length; i++) close[i] = data[i].Close;
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 0;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> calculatedResults,
            int index, Dictionary<string, object> parameters) => "";
    }

    private static IndicatorService Indicators() =>
        new(new List<IIndicatorProvider> { new SpyProvider() }, NullLogger<IndicatorService>.Instance, new CustomIndicatorRegistry());

    private static ChartSeries Series(string id, string code, string component, double[] data)
    {
        var config = new SeriesConfig { Id = id, IndicatorCode = code, Name = code };
        config.Components.Add(new ComponentConfig { Name = component });
        var buffer = new SeriesDataBuffer { SeriesId = id };
        buffer.ComponentData[component] = data;
        return new ChartSeries(config, buffer);
    }

    /// <summary>
    /// Six real bars climbing 100 → 150, box 10: five bricks, every brick close a box edge.
    /// The chart's spy and candle series hold what the chart computed — the brick closes.
    /// </summary>
    private static (WorkspaceState State, Ohlcv[] Bars) RenkoChart()
    {
        double[] closes = { 100, 112, 124, 131, 143, 155 };
        var bars = closes.Select((c, i) => new Ohlcv(Start.AddHours(i), c - 1, c + 1, c - 2, c, 100)).ToList();
        var built = BrickChartBuilder.Build(bars, 10, TimeSpan.FromHours(1));
        Assert.True(built.Ok);
        Assert.NotEqual(bars.Count, built.Bricks.Count);
        double[] brickCloses = Enumerable.Range(0, built.Bricks.Count).Select(i => built.Bricks[i].Close).ToArray();

        var state = WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "MEXC", "BTCUSDT", "1h"),
            Data = built.Bricks,
            TimeBars = new TimeSeriesBuffer<Ohlcv>(bars),
            BrickSpans = built.Spans,
            BrickBoxSize = 10,
            Renko = new RenkoSettings(RenkoBoxMode.Fixed, 10),
            ChartStyle = PriceChartStyle.Renko,
            CurrentDataIndex = built.Bricks.Count - 1,
            ActiveSeries = ImmutableList.Create(
                Series("spy", SpyCode, "close", brickCloses),
                Series(CoreSeriesIds.Candles, "CANDLES", "close", brickCloses)),
            InitStatus = InitializationStatus.Ready,
        };
        return (state, bars.ToArray());
    }

    [Fact]
    public void A_brick_chart_always_needs_its_series_rebuilt_for_a_strategy()
    {
        var (state, _) = RenkoChart();

        Assert.True(StrategyCandles.NeedsRebuild(state, heikinAshiWanted: false));
        Assert.False(StrategyCandles.NeedsRebuild(state with { ChartStyle = PriceChartStyle.Candles }, heikinAshiWanted: false));
    }

    [Fact]
    public void The_strategy_reads_indicators_computed_on_the_real_bars_bar_for_bar()
    {
        var (chart, bars) = RenkoChart();

        var state = StrategyCandles.For(Indicators(), chart, bars, heikinAshiWanted: false);

        var spy = state.ActiveSeries.Single(s => s.Id == "spy").Data.ComponentData["close"];
        Assert.Equal(bars.Select(b => b.Close), spy);   // one value per real bar, the real close
    }

    [Fact]
    public void The_strategy_sees_the_market_chart_not_the_bricks()
    {
        var (chart, bars) = RenkoChart();

        var state = StrategyCandles.For(Indicators(), chart, bars, heikinAshiWanted: false);

        // A script strategy reading state.Data reads the market.
        Assert.Equal(PriceChartStyle.Candles, state.ChartStyle);
        Assert.Equal(bars.Length, state.Data.Count);
        Assert.Equal(155, state.Data[^1].Close);
        Assert.Equal(bars.Length - 1, state.CurrentDataIndex);
        Assert.Null(state.TimeBars);
        Assert.True(state.BrickSpans.IsEmpty);
        // The candle series held brick prices; emptied, a condition reading price takes it off the bars.
        Assert.Empty(state.ActiveSeries.Single(s => s.Id == CoreSeriesIds.Candles).Data.ComponentData);
    }

    [Fact]
    public void A_candle_chart_is_handed_through_untouched()
    {
        var (chart, bars) = RenkoChart();
        var candles = chart with { ChartStyle = PriceChartStyle.Candles, TimeBars = null };

        Assert.Same(candles, StrategyCandles.For(Indicators(), candles, bars, heikinAshiWanted: false));
    }
    /// <summary>
    /// The live engine end to end: a bar closes on the focused Renko chart's feed, and the state
    /// the strategy is handed carries the spy computed on the real bars it is handed.
    /// </summary>
    [Fact]
    public async Task The_live_engine_hands_a_strategy_on_a_Renko_chart_the_real_series()
    {
        var (chart, bars) = RenkoChart();
        var store = Substitute.For<IWorkspaceStore>();
        store.State.Returns(chart);
        var hub = Substitute.For<IMarketFeedHub>();
        var strategy = Substitute.For<ITradingStrategy>();
        strategy.Name.Returns("Renko series spy");
        WorkspaceState? seen = null;
        IReadOnlyList<Ohlcv>? history = null;
        strategy.OnBar(Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>())
            .Returns(c => { history = c.ArgAt<IReadOnlyList<Ohlcv>>(1); seen = c.ArgAt<WorkspaceState>(2); return null; });

        using var engine = new StrategyEngine(Substitute.For<IEventBus>(), Substitute.For<IOrderExecutionService>(),
            Substitute.For<IAppLogger>(), NullLogger<StrategyEngine>.Instance,
            Substitute.For<IDataManager>(), store, Substitute.For<IStrategyIndicatorCache>(),
            feedHub: hub, indicators: Indicators());
        engine.AddStrategy(strategy);

        var feed = new ChartFeed(chart.Identity, Substitute.For<IDataOrchestrator>(), NullLogger.Instance);
        feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(bars.ToList()));
        hub.FocusedFeedUpdated += Raise.Event<Action<ChartFeed, FeedUpdateKind>>(feed, FeedUpdateKind.LiveAppend);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (seen == null && DateTime.UtcNow < deadline) await Task.Delay(10);

        Assert.NotNull(seen);
        var spy = seen!.ActiveSeries.Single(s => s.Id == "spy").Data.ComponentData["close"];
        Assert.Equal(history!.Select(b => b.Close), spy);
        Assert.Equal(PriceChartStyle.Candles, seen.ChartStyle);
    }
    /// <summary>
    /// A backtest started from a Renko chart: the Strategy dialog hands it the market's bars and
    /// the chart's state. The strategy reads the spy on those bars, as it would live.
    /// </summary>
    [Fact]
    public async Task A_backtest_from_a_Renko_chart_reads_the_series_on_the_real_bars()
    {
        var (chart, bars) = RenkoChart();
        var strategy = Substitute.For<ITradingStrategy>();
        strategy.Name.Returns("Renko series spy");
        var reads = new List<(int Count, double Spy)>();
        strategy.OnBar(Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>())
            .Returns(c =>
            {
                var history = c.ArgAt<IReadOnlyList<Ohlcv>>(1);
                var spy = c.ArgAt<WorkspaceState>(2).ActiveSeries.Single(s => s.Id == "spy").Data.ComponentData["close"];
                reads.Add((history.Count, spy.Length > history.Count - 1 ? spy[history.Count - 1] : double.NaN));
                return null;
            });

        await new StrategyBacktester(indicators: Indicators())
            .RunAsync(strategy, bars, new BacktestConfig(CommissionRate: 0, SlippagePercent: 0, WarmupBars: 0), chart);

        Assert.NotEmpty(reads);
        // The spy at the bar being evaluated is that bar's real close, on every bar.
        Assert.All(reads, r => Assert.Equal(bars[r.Count - 1].Close, r.Spy));
    }
}
