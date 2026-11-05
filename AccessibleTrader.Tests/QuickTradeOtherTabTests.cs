using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// A quick trade belongs to the chart it was armed on.
///
/// <para>
/// <b>Found 2026-11-05</b> (multi-tab audit, after Cody: "I might have a tab with a trade on
/// kraken, another tab with a trade on hyper liquid"). The armed state held a percentage, a stop
/// and an entry, but not WHICH chart they came from. Place read the symbol, and the executor read
/// the venue, from whichever tab was in front at the moment Ctrl+Enter was pressed. Arm on Kraken
/// BTC, set the stop, switch to a Bitstamp ETH tab, press Ctrl+Enter: a market order for ETH on
/// Bitstamp, sized with BTC's quantity, with BTC's stop price attached.
/// </para>
/// </summary>
public class QuickTradeOtherTabTests
{
    private static readonly ChartIdentity KrakenBtc = new("Crypto", "Kraken", "BTCUSDT", "1h");
    private static readonly ChartIdentity BitstampEth = new("Crypto", "Bitstamp", "ETHUSDT", "1h");
    private static readonly ChartIdentity KrakenBtc15 = new("Crypto", "Kraken", "BTCUSDT", "15m");

    private static WorkspaceState Chart(ChartIdentity id, double close) => WorkspaceState.Initial with
    {
        Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, 50).Select(i => new Ohlcv
        {
            Date = new DateTime(2026, 11, 1).AddHours(i),
            Open = close, High = close * 1.05, Low = close * 0.95, Close = close, Volume = 1,
        }).ToArray()),
        CurrentDataIndex = 25,
        Identity = id,
    };

    private static (QuickTradeService Svc, SpyEventBus Bus, MockWorkspaceStore Store) Armed()
    {
        var store = new MockWorkspaceStore();
        store.EmitState(Chart(KrakenBtc, 100_000));
        var bus = new SpyEventBus();
        var svc = new QuickTradeService(store, bus, () => 10_000,
            sizingMode: () => QuickTradeSizingMode.RiskAtStop);
        svc.Arm(1.0);
        svc.SetStopAtCursor();
        Assert.Equal(QuickTradeStage.Ready, svc.State.Stage);
        return (svc, bus, store);
    }

    private static string LastSpoken(SpyEventBus bus) =>
        bus.Log.OfType<FeedbackRequestEvent>().Last().Message ?? "";

    [Fact]
    public void A_trade_armed_on_one_tab_is_not_placed_from_another()
    {
        var (svc, bus, store) = Armed();
        store.EmitState(Chart(BitstampEth, 3_000));

        svc.Place(market: true);

        Assert.Empty(bus.Log.OfType<QuickTradeRequestedEvent>());
        Assert.Equal("This trade was armed on Kraken BTCUSDT 1h, not this chart. "
                   + "Go back to that tab to place it, or press Escape to cancel.", LastSpoken(bus));
        // Still armed: going back to the right tab must find it as it was left.
        Assert.Equal(QuickTradeStage.Ready, svc.State.Stage);
    }

    [Fact]
    public void The_same_symbol_on_another_timeframe_is_another_chart()
    {
        // The cursor bar a limit is priced at, and the stop, came from the 1h bars.
        var (svc, bus, store) = Armed();
        store.EmitState(Chart(KrakenBtc15, 100_000));

        svc.Place(market: false);

        Assert.Empty(bus.Log.OfType<QuickTradeRequestedEvent>());
    }

    [Fact]
    public void A_stop_is_not_taken_from_another_tab()
    {
        var store = new MockWorkspaceStore();
        store.EmitState(Chart(KrakenBtc, 100_000));
        var bus = new SpyEventBus();
        var svc = new QuickTradeService(store, bus, () => 10_000);
        svc.Arm(1.0);
        store.EmitState(Chart(BitstampEth, 3_000));

        svc.SetStopAtCursor();

        Assert.Equal(QuickTradeStage.AwaitingStop, svc.State.Stage);
        Assert.Null(svc.State.StopPrice);
        Assert.StartsWith("This trade was armed on Kraken BTCUSDT 1h", LastSpoken(bus));
    }

    [Fact]
    public void Back_on_its_own_tab_the_trade_goes_to_its_own_venue()
    {
        var (svc, bus, store) = Armed();
        store.EmitState(Chart(BitstampEth, 3_000));
        svc.Place(market: true);
        store.EmitState(Chart(KrakenBtc, 100_000));

        svc.Place(market: true);

        var sent = Assert.Single(bus.Log.OfType<QuickTradeRequestedEvent>());
        Assert.Equal("BTCUSDT", sent.Symbol);
        Assert.Equal("Kraken", sent.Provider);
    }

    [Fact]
    public void Escape_cancels_from_any_tab()
    {
        var (svc, bus, store) = Armed();
        store.EmitState(Chart(BitstampEth, 3_000));

        svc.Disarm();

        Assert.Equal(QuickTradeStage.Idle, svc.State.Stage);
        Assert.Equal("Quick trade cancelled.", LastSpoken(bus));
    }

    [Fact]
    public void The_executor_sends_to_the_armed_venue_not_the_tab_in_front()
    {
        var store = new MockWorkspaceStore();
        store.EmitState(Chart(BitstampEth, 3_000));
        var bus = new SpyEventBus();
        var orders = Substitute.For<IOrderExecutionService>();
        var sent = new List<string>();
        orders.PlaceOrderAsync(Arg.Any<string>(), Arg.Any<TradeSignal>()).Returns(ci =>
        {
            lock (sent) sent.Add(ci.ArgAt<string>(0));
            return Task.FromResult(OrderPlacement.Parse("order-1"));
        });
        using var executor = new QuickTradeExecutor(bus, orders, store, NullLogger<QuickTradeExecutor>.Instance);

        bus.Publish(new QuickTradeRequestedEvent("BTCUSDT", true, 0.01, null, 95_000, 10, Provider: "Kraken"));

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline) { lock (sent) if (sent.Count > 0) break; Thread.Sleep(5); }
        lock (sent) Assert.Equal(new[] { "Kraken" }, sent);
    }

    [Fact]
    public async Task A_balance_that_arrives_after_a_tab_switch_does_not_arm_the_new_tab()
    {
        var store = new MockWorkspaceStore();
        store.EmitState(Chart(KrakenBtc, 100_000));
        var bus = new SpyEventBus();
        double equity = 0;
        var gate = new TaskCompletionSource();
        var svc = new QuickTradeService(store, bus, () => equity, async () => { await gate.Task; equity = 10_000; });

        svc.Arm(1.0);                                  // no balance yet: fetches
        store.EmitState(Chart(BitstampEth, 3_000));    // user moves on while it is in flight
        gate.SetResult();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !LastSpoken(bus).StartsWith("Your balance is in"))
            await Task.Delay(5);

        Assert.Equal(QuickTradeStage.Idle, svc.State.Stage);
        Assert.StartsWith("Your balance is in, but you have moved to another chart", LastSpoken(bus));
    }
}
