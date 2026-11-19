using System.Collections.Immutable;
using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Charts;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Tests.Blazor;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The bricks-versus-real-bars rule at the money boundary. Every state here is built so the
/// newest brick's close and the newest real bar's close DIFFER: a path that reads Data passes
/// only if it reads a price the market never traded at.
/// </summary>
public sealed class RenkoMarketBarsTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static List<Ohlcv> Bars()
    {
        double[] closes = { 100, 110, 120, 125 };
        return closes.Select((c, i) => new Ohlcv(Start.AddHours(i), c - 1, c + 1, c - 2, c, 100)).ToList();
    }

    private static WorkspaceState BrickState()
    {
        var bars = Bars();
        var time = new TimeSeriesBuffer<Ohlcv>(bars);
        var built = BrickChartBuilder.Build(bars, 10, TimeSpan.FromHours(1));
        Assert.True(built.Ok);
        Assert.NotEqual(time[^1].Close, built.Bricks[^1].Close);

        return WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "MEXC", "BTCUSDT", "1h"),
            Data = built.Bricks,
            TimeBars = time,
            BrickSpans = built.Spans,
            BrickBoxSize = 10,
            Renko = new RenkoSettings(RenkoBoxMode.Fixed, 10),
            ChartStyle = PriceChartStyle.Renko,
            CurrentDataIndex = built.Bricks.Count - 1,
            ViewportLength = 20,
            InitStatus = InitializationStatus.Ready,
        };
    }

    [Fact]
    public void QuickTrade_arms_at_the_real_last_price_not_the_newest_brick_edge()
    {
        var state = BrickState();
        Assert.Equal(120, state.Data[^1].Close);
        Assert.Equal(125, state.MarketBars[^1].Close);

        var store = new MockWorkspaceStore();
        store.EmitState(state);
        var service = new QuickTradeService(store, new SpyEventBus(), () => 100_000,
            sizingMode: () => QuickTradeSizingMode.PositionValue);

        service.Arm(1);

        Assert.Equal(125, service.State.EntryPrice);
    }

    [Fact]
    public void The_trading_dashboard_quotes_the_real_last_price()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => BrickState());

        var cut = h.Ctx.RenderComponent<TradingDashboardModal>();
        var type = cut.Instance.GetType();
        string last = (string)type
            .GetMethod("GetLastPrice", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(cut.Instance, null)!;
        double current = (double)type
            .GetMethod("GetCurrentPrice", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(cut.Instance, null)!;

        Assert.Equal(AccessibleTrader.BlazorClient.Services.PriceFormatter.FormatPrice(125), last);
        Assert.Equal(125, current);
    }

    private static (StrategyEngine Engine, IWorkspaceStore Store, IDataManager DataManager, ITradingStrategy Strategy) Engine(WorkspaceState state)
    {
        var bus = Substitute.For<IEventBus>();
        var store = Substitute.For<IWorkspaceStore>();
        store.State.Returns(state);
        var dataManager = Substitute.For<IDataManager>();
        var strategy = Substitute.For<ITradingStrategy>();
        strategy.Name.Returns("Renko test strategy");

        var engine = new StrategyEngine(bus, Substitute.For<IOrderExecutionService>(),
            Substitute.For<IAppLogger>(), NullLogger<StrategyEngine>.Instance,
            dataManager, store, Substitute.For<IStrategyIndicatorCache>());
        engine.AddStrategy(strategy);
        return (engine, store, dataManager, strategy);
    }

    [Fact]
    public void AddStrategy_initializes_from_the_time_bars()
    {
        var state = BrickState();

        var (_, _, _, strategy) = Engine(state);

        strategy.Received(1).Initialize(
            Arg.Is<IReadOnlyList<Ohlcv>>(bars => bars.Count == 4 && bars[bars.Count - 1].Close == 125),
            state,
            Arg.Any<IDictionary<string, object>>());
    }

    [Fact]
    public void OnDataUpdated_does_nothing_on_a_brick_chart()
    {
        var state = BrickState();
        var (_, _, dataManager, strategy) = Engine(state);

        dataManager.DataUpdated += Raise.Event<Action>();

        strategy.DidNotReceive().OnBar(
            Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>());
    }

    [Fact]
    public async Task Catch_up_after_a_restore_passes_the_time_bars()
    {
        var bus = new EventBus();
        var store = new WorkspaceStore(
            bus, new ViewportRangeCalculator(), new ViewportNavigationService(), new VolumeStateService());
        store.Dispatch(new UpdateSettingsAction(_ => BrickState()));
        var dataManager = Substitute.For<IDataManager>();

        var orch = new MarketOrchestrator(
            Substitute.For<IDataService>(), dataManager, store,
            Substitute.For<IWorkspaceInitializer>(), bus, new DemoPolicy(isDemo: false));

        await orch.LoadRestoredActiveTabAsync();

        await dataManager.Received(1).CatchUpFromSnapshotAsync(
            Arg.Is<TimeSeriesBuffer<Ohlcv>>(b => b.Count == 4 && b[b.Count - 1].Close == 125),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Replay_restores_the_time_bars_and_rebricks_each_prefix()
    {
        var bars = Enumerable.Range(0, 100)
            .Select(i => new Ohlcv(Start.AddHours(i), 100 + i, 101 + i, 99 + i, 100 + i, 100))
            .ToList();
        var time = new TimeSeriesBuffer<Ohlcv>(bars);
        var built = BrickChartBuilder.Build(bars, 10, TimeSpan.FromHours(1));

        var store = new WorkspaceStore(
            new EventBus(), new ViewportRangeCalculator(), new ViewportNavigationService(), new VolumeStateService());
        store.Dispatch(new UpdateSettingsAction(_ => WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Spot", "Bitstamp", "BTC/USD", "1h"),
            Data = built.Bricks,
            TimeBars = time,
            BrickSpans = built.Spans,
            BrickBoxSize = 10,
            Renko = new RenkoSettings(RenkoBoxMode.Fixed, 10),
            ChartStyle = PriceChartStyle.Renko,
            CurrentDataIndex = built.Bricks.Count - 1,
            ViewportLength = 30,
            InitStatus = InitializationStatus.Ready,
        }));

        var replay = new ReplayService(store, new EventBus());
        Assert.True(replay.Start(29));

        Assert.Equal(30, store.State.TimeBars!.Count);
        Assert.Equal(30, store.State.MarketBars.Count);
        Assert.Equal(PriceChartStyle.Renko, store.State.ChartStyle);
        Assert.Equal(store.State.Data.Count, store.State.BrickSpans.Length);

        replay.Stop();

        Assert.Equal(100, store.State.MarketBars.Count);
        Assert.Equal(100, store.State.TimeBars!.Count);
        Assert.Equal(PriceChartStyle.Renko, store.State.ChartStyle);
        Assert.Equal(store.State.Data.Count, store.State.BrickSpans.Length);
        Assert.NotEqual(store.State.Data[^1].Close, store.State.MarketBars[^1].Close);
    }
    // ── The paper broker: fills and resting orders read the real bars ────────────────────

    private static PaperTradingProvider Paper(MockWorkspaceStore store, string dir)
    {
        var paths = Substitute.For<IPlatformPathService>();
        paths.AppDataDirectory.Returns(dir);
        var data = Substitute.For<IDataService>();
        data.GetProviderAsync(Arg.Any<string>()).Returns(Task.FromResult<IMarketDataProvider?>(null));
        data.FetchOhlcvAsync(Arg.Any<string>(), Arg.Any<MarketDataRequest>())
            .Returns(Task.FromResult((new List<Ohlcv>(), new List<(long, double)>())));
        return new PaperTradingProvider(store, paths, NullLogger<PaperTradingProvider>.Instance, null, data);
    }

    /// <summary>
    /// A paper market buy fills at the market's last close, 125, not the brick edge 120, and
    /// the open position is then valued at 125 too (<c>PriceFor</c>, the P&amp;L path).
    /// </summary>
    [Fact]
    public async Task A_paper_market_order_fills_and_is_valued_at_the_real_last_price()
    {
        string dir = TestTemp.NewDir("att-renko-paper-");
        try
        {
            var store = new MockWorkspaceStore();
            store.EmitState(BrickState());
            var paper = Paper(store, dir);

            await paper.PlaceOrderAsync(new TradeSignal(
                Symbol: "BTCUSDT", Side: OrderSide.Buy, Quantity: 1.0, Type: OrderType.Market));

            var fill = Assert.Single(await paper.GetFillsAsync("BTCUSDT"));
            Assert.InRange(fill.Price, 124, 126);   // 125 plus any modelled slippage, never 120
            var position = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal(125, position.MarketValue / position.Quantity, 6);
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    /// <summary>
    /// A resting buy limit at 112. The next bar arrives on a Renko chart: the real bar's low is
    /// 123, the brick drawn for it reaches down to 110. Both are dated the same new hour, so
    /// the only thing deciding the fill is which buffer the broker reads. Reading the bricks
    /// fills the order at a price the market never traded at.
    /// </summary>
    [Fact]
    public async Task A_resting_paper_limit_is_checked_against_the_real_bar_not_the_brick()
    {
        string dir = TestTemp.NewDir("att-renko-paper-");
        try
        {
            var identity = new ChartIdentity("Crypto", "MEXC", "BTCUSDT", "1h");
            var before = Bars().Take(3).ToList();                         // last close 120
            var store = new MockWorkspaceStore();
            store.EmitState(WorkspaceState.Initial with
            {
                Identity = identity,
                Data = new TimeSeriesBuffer<Ohlcv>(before),
                InitStatus = InitializationStatus.Ready,
            });
            var paper = Paper(store, dir);
            await paper.PlaceOrderAsync(new TradeSignal(
                Symbol: "BTCUSDT", Side: OrderSide.Buy, Quantity: 1.0, Type: OrderType.Limit, Price: 112));
            Assert.Single(await paper.GetOpenOrdersAsync("BTCUSDT"));

            var t = Start.AddHours(3);
            var real = before.Append(new Ohlcv(t, 124, 126, 123, 125, 100)).ToList();
            var bricks = new List<Ohlcv>
            {
                new(Start.AddHours(1), 100, 110, 100, 110, 100),
                new(t, 120, 120, 110, 110, 100),                              // low 110 <= 112
            };
            store.EmitState(store.State with
            {
                ChartStyle = PriceChartStyle.Renko,
                TimeBars = new TimeSeriesBuffer<Ohlcv>(real),
                Data = new TimeSeriesBuffer<Ohlcv>(bricks),
                BrickBoxSize = 10,
            });

            Assert.Single(await paper.GetOpenOrdersAsync("BTCUSDT"));
            Assert.Empty(await paper.GetFillsAsync("BTCUSDT"));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }
    /// <summary>
    /// Saving a Renko tab records how far back its history reaches from the real bars. The
    /// first brick is dated an hour after the first bar, so reading the bricks would save a
    /// shallower history and each restore would fetch less than was on screen.
    /// </summary>
    [Fact]
    public void Saving_a_brick_chart_records_the_first_real_bar()
    {
        var state = BrickState();
        Assert.NotEqual(state.TimeBars![0].Date, state.Data[0].Date);
        var store = new MockWorkspaceStore();
        store.EmitState(state);
        var library = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths());

        var tab = Assert.Single(library.CaptureWorkspace(store).Tabs);

        Assert.Equal(Start, tab.EarliestBarDate);
    }
}
