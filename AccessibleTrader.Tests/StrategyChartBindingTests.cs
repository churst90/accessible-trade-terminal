using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// A running strategy belongs to the whole chart it was started on: venue, symbol, timeframe.
///
/// <para>
/// <b>Found 2026-11-05</b> (multi-tab audit, after Cody: "I might have a tab with a trade on
/// kraken, another tab with a trade on hyper liquid ... I really want to make sure that these type
/// of bugs don't happen"). Strategies were bound by display symbol alone, so with a Kraken
/// BTC/USDT 1h tab and a Bitstamp BTC/USDT 15m tab open:
/// </para>
/// <list type="bullet">
/// <item>the Kraken strategy ran on the Bitstamp 15m bars whenever that tab was in front;</item>
/// <item>in Auto mode its order went to the venue of whichever tab was in front when the
/// thread-pool hop landed, read again from the store;</item>
/// <item>with both tabs in the background, both monitors ran it, each on its own bars;</item>
/// <item>a background monitor walked the managed stop against the still-forming bar, whose low
/// reaches back to the bar's open (on a 4h chart, hours before the entry).</item>
/// </list>
/// </summary>
public class StrategyChartBindingTests
{
    private static readonly ChartIdentity KrakenBtc1h = new("Crypto", "Kraken", "BTC/USDT", "1h");
    private static readonly ChartIdentity BitstampBtc15m = new("Crypto", "Bitstamp", "BTC/USDT", "15m");
    private static readonly ChartIdentity KrakenBtc15m = new("Crypto", "Kraken", "BTC/USDT", "15m");
    private static readonly ChartIdentity BitstampBtc1h = new("Crypto", "Bitstamp", "BTC/USDT", "1h");

    private static WorkspaceState On(ChartIdentity id) => WorkspaceState.Initial with
    {
        Identity = id,
        SymbolDisplayName = "BTC/USDT",
    };

    private static Ohlcv At(int hours, double close = 100) =>
        new(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc).AddHours(hours), close, close + 1, close - 1, close, 10);

    private sealed class Rig
    {
        public readonly IEventBus Bus = Substitute.For<IEventBus>();
        public readonly ITradingStrategy Strategy = Substitute.For<ITradingStrategy>();
        public readonly IWorkspaceStore Store = Substitute.For<IWorkspaceStore>();
        public readonly IOrderExecutionService Orders = Substitute.For<IOrderExecutionService>();
        public readonly StrategyEngine Engine;
        public WorkspaceState Focused;

        public Rig(ChartIdentity startedOn, StrategyExecutionMode mode = StrategyExecutionMode.Suggestion)
        {
            Strategy.Name.Returns("TestStrategy");
            Strategy.OnBar(Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>())
                    .Returns(new StrategySignal(OrderSide.Buy, OrderType.Market, 0.01, null, null, null, "always", 0.9));
            Focused = On(startedOn);
            Store.State.Returns(_ => Focused);
            Orders.PlaceOrderAsync(Arg.Any<string>(), Arg.Any<TradeSignal>())
                  .Returns(Task.FromResult(OrderPlacement.Parse("order-1")));

            Engine = new StrategyEngine(Bus, Orders, Substitute.For<IAppLogger>(), NullLogger<StrategyEngine>.Instance,
                Substitute.For<IDataManager>(), Store, Substitute.For<IStrategyIndicatorCache>());
            Engine.AddStrategy(Strategy, mode: mode);
        }

        public void Evaluate(WorkspaceState state, Ohlcv bar) =>
            typeof(StrategyEngine)
                .GetMethod("EvaluateBar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(Engine, new object[] { bar, new List<Ohlcv> { At(0), bar }, state, state.Identity });

        public int OnBarCalls => Strategy.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "OnBar");
    }

    [Fact]
    public void A_strategy_is_bound_to_the_venue_and_timeframe_it_was_started_on()
    {
        var rig = new Rig(KrakenBtc1h);

        Assert.Equal(KrakenBtc1h, Assert.Single(rig.Engine.ActiveStrategies).Chart);
    }

    [Fact]
    public void The_same_symbol_on_another_venue_does_not_run_it()
    {
        var rig = new Rig(KrakenBtc1h);

        rig.Evaluate(On(BitstampBtc1h), At(1));
        rig.Evaluate(On(BitstampBtc15m), At(1));
        rig.Evaluate(On(KrakenBtc15m), At(1));
        Assert.Equal(0, rig.OnBarCalls);

        rig.Evaluate(On(KrakenBtc1h), At(1));
        Assert.Equal(1, rig.OnBarCalls);
    }

    [Fact]
    public async Task An_auto_order_goes_to_the_strategys_venue_when_another_tab_is_in_front()
    {
        var rig = new Rig(KrakenBtc1h, StrategyExecutionMode.Auto);
        // The bar is Kraken's; by the time the order is sent the user is on the Bitstamp tab.
        rig.Focused = On(BitstampBtc15m) with { SymbolDisplayName = "BTC/USDT" };

        rig.Evaluate(On(KrakenBtc1h), At(1));

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !rig.Orders.ReceivedCalls().Any()) await Task.Delay(5);
        await rig.Orders.Received(1).PlaceOrderAsync("Kraken", Arg.Is<TradeSignal>(s => s.Symbol == "BTC/USDT"));
        await rig.Orders.DidNotReceive().PlaceOrderAsync("Bitstamp", Arg.Any<TradeSignal>());
    }

    [Fact]
    public void A_workspace_saved_before_the_chart_was_kept_still_binds_by_symbol()
    {
        var restored = new Rig(KrakenBtc1h);
        restored.Engine.RemoveStrategy(restored.Engine.ActiveStrategies[0].InstanceId);
        restored.Engine.AddStrategy(restored.Strategy, bindSymbol: "BTC/USDT");

        // Not quietly bound to whatever chart was up at restore.
        Assert.Null(Assert.Single(restored.Engine.ActiveStrategies).Chart);
        restored.Evaluate(On(BitstampBtc15m), At(1));
        Assert.Equal(1, restored.OnBarCalls);
    }

    [Fact]
    public void The_saved_strategy_keeps_its_chart()
    {
        var rig = new Rig(KrakenBtc1h);
        rig.Engine.RemoveStrategy(rig.Engine.ActiveStrategies[0].InstanceId);
        rig.Engine.AddStrategy(rig.Strategy, specId: "spec-1");   // only library strategies are saved
        var store = new MockWorkspaceStore();
        store.EmitState(On(BitstampBtc15m));
        var library = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, Substitute.For<IPlatformPathService>(),
            rig.Engine);

        var saved = Assert.Single(library.CaptureWorkspace(store).ActiveStrategies);

        Assert.Equal(("Kraken", "BTC/USDT", "1h", "Crypto"),
                     (saved.ChartProvider, saved.ChartSymbol, saved.ChartTimeframe, saved.ChartMarket));
    }

    [Fact]
    public void A_loaded_workspace_puts_the_strategy_back_on_its_saved_chart()
    {
        var store = Substitute.For<IWorkspaceStore>();
        store.State.Returns(On(BitstampBtc15m));
        var spec = new StrategySpec("spec-1", "Test", "", OrderSide.Buy,
            new ConditionGroup("root", LogicOperator.And, new List<ConditionNode>()),
            new RiskPlan(
                Stop: new StopSource(StopSourceKind.PercentOfPrice, PercentValue: 1.0),
                TpLadder: Array.Empty<TpLadderRung>(),
                Sizing: new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005),
                Entry: new EntryTrigger(EntryTriggerKind.Immediate)),
            StrategyExecutionMode.Suggestion);
        var library = Substitute.For<IStrategyLibrary>();
        library.All.Returns(new List<StrategySpec> { spec });
        var engine = Substitute.For<IStrategyEngine>();
        engine.ActiveStrategies.Returns(new List<ActiveStrategy>());
        var init = new WorkspaceInitializer(Substitute.For<ISeriesManagementService>(), store,
            Substitute.For<IIndicatorService>(), engine, Substitute.For<IConfigurableStrategyFactory>(), library);

        init.RestoreWorkspace(new WorkspaceConfiguration
        {
            Tabs = { new TabConfiguration() },
            ActiveStrategies = new List<SavedActiveStrategy>
            {
                new() { SpecId = "spec-1", Symbol = "BTC/USDT", ChartProvider = "Kraken", ChartSymbol = "BTC/USDT",
                        ChartTimeframe = "1h", ChartMarket = "Crypto", ExecutionMode = "Suggestion" },
            },
        });

        engine.Received(1).AddStrategy(Arg.Any<ITradingStrategy>(), Arg.Any<IDictionary<string, object>>(),
            StrategyExecutionMode.Suggestion, "spec-1", "BTC/USDT", KrakenBtc1h);
    }

    // ── Background monitors ──────────────────────────────────────────────────

    private static ActiveStrategy Bound(ITradingStrategy s, ChartIdentity chart) =>
        new("inst-1", s, new Dictionary<string, object>(), StrategyExecutionMode.Suggestion, false, "BTC/USDT", null, chart);

    private static (BackgroundWorkspaceMonitor Monitor, ITradingStrategy Strategy, IStrategyPositionManager Positions)
        Monitor(ChartIdentity monitorChart, ChartIdentity strategyChart, IReadOnlyList<Ohlcv> bars)
    {
        var data = Substitute.For<IMarketFeeds>();
        data.GetBarsAsync(Arg.Any<ChartIdentity>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(bars);
        var alerts = Substitute.For<IAlertOrchestrator>();
        alerts.GetAlerts().Returns(new List<AlertDefinition>());
        var strategy = Substitute.For<ITradingStrategy>();
        strategy.Name.Returns("TestStrategy");
        strategy.OnBar(Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>())
                .Returns(new StrategySignal(OrderSide.Buy, OrderType.Market, 0.01, null, null, null, "always", 0.9));
        var engine = Substitute.For<IStrategyEngine>();
        engine.ActiveStrategies.Returns(new List<ActiveStrategy> { Bound(strategy, strategyChart) });
        var positions = Substitute.For<IStrategyPositionManager>();
        positions.OnBarClosed(Arg.Any<string>(), Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>())
                 .Returns(Array.Empty<StrategyExitOrder>());

        var monitor = new BackgroundWorkspaceMonitor(monitorChart, "BTC/USDT", ImmutableList<ChartSeries>.Empty,
            data, Substitute.For<IIndicatorService>(), alerts, Substitute.For<IAlertEvaluator>(), engine,
            new SpyEventBus(), NullLogger.Instance, TimeSpan.FromHours(1), positions: positions);
        return (monitor, strategy, positions);
    }

    private static List<Ohlcv> Hourly(int n, DateTime lastOpen) =>
        Enumerable.Range(0, n).Select(i => new Ohlcv(lastOpen.AddHours(i - n + 1), 100, 101, 99, 100, 10)).ToList();

    [Fact]
    public async Task A_background_tab_on_another_venue_does_not_run_the_strategy_or_its_exits()
    {
        var bars = Hourly(60, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));
        var (other, strategy, positions) = Monitor(BitstampBtc15m, KrakenBtc1h, bars);

        await other.EvaluateOnceAsync(CancellationToken.None);

        Assert.DoesNotContain(strategy.ReceivedCalls(), c => c.GetMethodInfo().Name == "OnBar");
        Assert.Empty(positions.ReceivedCalls());
    }

    [Fact]
    public async Task A_background_tab_walks_the_managed_exits_on_the_last_closed_bar_not_the_forming_one()
    {
        // The newest bar opened 20 minutes ago on a 1h chart: still forming.
        var formingOpen = DateTime.UtcNow.AddMinutes(-20);
        formingOpen = new DateTime(formingOpen.Ticks - formingOpen.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
        var bars = Hourly(60, formingOpen);
        var (own, strategy, positions) = Monitor(KrakenBtc1h, KrakenBtc1h, bars);

        await own.EvaluateOnceAsync(CancellationToken.None);

        positions.Received(1).OnBarClosed("inst-1", bars[^2], Arg.Is<IReadOnlyList<Ohlcv>>(h => h.Count == 59));
        strategy.Received(1).OnBar(bars[^2], Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>());
    }

    [Fact]
    public async Task A_background_signal_is_announced_once_per_closed_bar()
    {
        var bars = Hourly(60, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));
        var (own, _, _) = Monitor(KrakenBtc1h, KrakenBtc1h, bars);
        var bus = (SpyEventBus)typeof(BackgroundWorkspaceMonitor)
            .GetField("_eventBus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(own)!;

        await own.EvaluateOnceAsync(CancellationToken.None);
        await own.EvaluateOnceAsync(CancellationToken.None);

        Assert.Single(bus.Log.OfType<StrategySignalEvent>());
    }
}
