using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// What one broken listener is allowed to cost.
///
/// <para>
/// Both of the streams below were built on a bare <c>Subject&lt;T&gt;</c>, which walks its
/// observers on the publishing thread and stops at the first one that throws. So a fault anywhere
/// in the listener set did two things at once: it denied the event to every subscriber behind the
/// broken one, and it threw the exception back out of whoever published.
/// </para>
///
/// <para>
/// On the order stream that is the worst shape a money-path failure can take, and it was measured
/// rather than theorised: <c>PlaceOrderAsync</c> threw <c>InvalidOperationException</c>, the
/// healthy subscriber received nothing, and <c>GetPositionsAsync</c> then returned one position.
/// A trader told their order failed, while holding it. Every announcement path reads that stream —
/// the spoken fill, the earcon, the journal, the reconciliation coordinator — so "the subscriber
/// after the broken one hears nothing" also means the fill is never announced.
/// </para>
///
/// <para>
/// Both are fixed by isolating each subscriber rather than the batch: a listener that throws loses
/// its own notification and no one else's, the publisher never sees the exception, and the fault
/// is logged rather than swallowed — in this app an unreported error is inaudible.
/// </para>
/// </summary>
public class SubscriberFaultIsolationTests
{
    private sealed record Ping(int N);

    // ── EventBus ────────────────────────────────────────────────────────────

    [Fact]
    public void A_throwing_EventBus_subscriber_does_not_take_the_exception_out_of_Publish()
    {
        var bus = new EventBus();
        bus.Subscribe<Ping>(_ => throw new InvalidOperationException("handler fault"));

        // The assertion is the absence of a throw; stated explicitly so the intent survives.
        var ex = Record.Exception(() => bus.Publish(new Ping(1)));

        Assert.Null(ex);
    }

    [Fact]
    public void A_throwing_EventBus_subscriber_does_not_stop_delivery_to_the_others()
    {
        var bus = new EventBus();
        var before = new List<int>();
        var after = new List<int>();

        bus.Subscribe<Ping>(p => before.Add(p.N));
        bus.Subscribe<Ping>(_ => throw new InvalidOperationException("handler fault"));
        bus.Subscribe<Ping>(p => after.Add(p.N));

        bus.Publish(new Ping(1));
        bus.Publish(new Ping(2));

        Assert.Equal(new[] { 1, 2 }, before);
        // The one that matters: registered AFTER the broken handler, so it is the one the old
        // observer walk never reached.
        Assert.Equal(new[] { 1, 2 }, after);
    }

    /// <summary>
    /// A handler that throws keeps its subscription. Rx's default is to treat a throwing observer
    /// as terminated, so before the fix a handler that failed once went silent for the rest of the
    /// session — losing one event and then every event, having reported neither.
    /// </summary>
    [Fact]
    public void A_subscriber_that_throws_once_still_receives_the_next_event()
    {
        var bus = new EventBus();
        var seen = new List<int>();

        bus.Subscribe<Ping>(p =>
        {
            seen.Add(p.N);
            if (p.N == 1) throw new InvalidOperationException("fails on the first one only");
        });

        bus.Publish(new Ping(1));
        bus.Publish(new Ping(2));
        bus.Publish(new Ping(3));

        Assert.Equal(new[] { 1, 2, 3 }, seen);
    }

    /// <summary>
    /// Vacuity: the harness must be able to observe a subscriber NOT running, or every assertion
    /// above is about a list nobody was ever going to fill.
    /// </summary>
    [Fact]
    public void An_unsubscribed_handler_stops_receiving()
    {
        var bus = new EventBus();
        var seen = new List<int>();
        var sub = bus.Subscribe<Ping>(p => seen.Add(p.N));

        bus.Publish(new Ping(1));
        sub.Dispose();
        bus.Publish(new Ping(2));

        Assert.Equal(new[] { 1 }, seen);
    }

    // ── The paper broker's order stream ─────────────────────────────────────

    private sealed class PaperHarness : IDisposable
    {
        public readonly PaperTradingProvider Paper;
        public readonly MockWorkspaceStore Store;
        private readonly string _dir;

        public PaperHarness()
        {
            _dir = TestTemp.NewDir("at-fault-isolation-");
            Store = new MockWorkspaceStore();
            var paths = Substitute.For<IPlatformPathService>();
            paths.AppDataDirectory.Returns(_dir);
            Paper = new PaperTradingProvider(Store, paths, NullLogger<PaperTradingProvider>.Instance);
        }

        public void LivePrice(double close) => Store.EmitState(WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Spot", "Test", "BTC/USDT", "1h"),
            Data = new TimeSeriesBuffer<Ohlcv>(
                new Ohlcv(DateTime.UtcNow, close - 1, close + 1, close - 2, close, 1000)),
        });

        public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }
    }

    [Fact]
    public async Task A_throwing_order_subscriber_does_not_fail_the_order_that_succeeded()
    {
        using var h = new PaperHarness();
        h.Paper.OrderUpdateStream.Subscribe(_ => throw new InvalidOperationException("listener fault"));
        h.LivePrice(100);

        string result = await h.Paper.PlaceOrderAsync(new TradeSignal("BTC/USDT", OrderSide.Buy, 1.0));

        // Before the fix this threw, and it threw AFTER the position was open — so the two
        // assertions below contradicted each other from the trader's point of view.
        Assert.StartsWith("paper-", result);
        Assert.Single(await h.Paper.GetPositionsAsync());
    }

    [Fact]
    public async Task A_throwing_order_subscriber_does_not_silence_the_announcement_paths()
    {
        using var h = new PaperHarness();
        var heard = new List<OrderUpdate>();

        h.Paper.OrderUpdateStream.Subscribe(_ => throw new InvalidOperationException("listener fault"));
        h.Paper.OrderUpdateStream.Subscribe(heard.Add);   // e.g. the speech announcer
        h.LivePrice(100);

        await h.Paper.PlaceOrderAsync(new TradeSignal("BTC/USDT", OrderSide.Buy, 1.0));

        var fill = Assert.Single(heard);
        Assert.Equal(OrderStatus.Filled, fill.Status);
        Assert.Equal(100, fill.FilledPrice);
    }

    /// <summary>
    /// The limitation, pinned deliberately rather than left as folklore.
    ///
    /// <para>
    /// On <see cref="EventBus"/> a handler that throws keeps its subscription, because the bus
    /// owns the <c>Action&lt;T&gt;</c> and wraps it. On <c>OrderUpdateStream</c> it does not:
    /// consumers call <c>IObservable.Subscribe(action)</c> themselves, and Rx's own
    /// <c>AnonymousObserver</c> disposes the subscription when the action throws — before the
    /// publisher-side guard can see it. So the broken listener goes silent for the rest of the
    /// session while everyone else carries on.
    /// </para>
    ///
    /// <para>
    /// That is a real gap and it is written down as one: an announcement path that throws once
    /// stops announcing, and the only trace is the log line. Closing it means giving the stream a
    /// subscribe method of its own rather than handing out a bare <c>IObservable</c>, which is a
    /// change to a contract that provider plugins also implement — filed, not smuggled in here.
    /// This test exists so that if the fix lands, its author is told exactly which claim changed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_order_subscriber_that_throws_loses_its_own_subscription_and_nobody_elses()
    {
        using var h = new PaperHarness();
        var faulty = new List<OrderStatus>();
        var healthy = new List<OrderStatus>();

        h.Paper.OrderUpdateStream.Subscribe(u =>
        {
            faulty.Add(u.Status);
            if (faulty.Count == 1) throw new InvalidOperationException("fails on the first fill");
        });
        h.Paper.OrderUpdateStream.Subscribe(u => healthy.Add(u.Status));
        h.LivePrice(100);

        await h.Paper.PlaceOrderAsync(new TradeSignal("BTC/USDT", OrderSide.Buy, 1.0));
        await h.Paper.PlaceOrderAsync(new TradeSignal("BTC/USDT", OrderSide.Sell, 1.0));

        Assert.Single(faulty);          // Rx terminated it — the documented gap
        Assert.Equal(2, healthy.Count); // and it cost nobody else anything
    }

    // ── Concurrency on the paper broker ─────────────────────────────────────
    //
    // No test in the trading area used Task.WhenAll or Parallel.For before this. The broker is a
    // shared object behind one lock and the WebHost runs it per circuit, so the interleavings
    // below are reachable: a strategy placing while the user cancels, and a fill evaluation
    // arriving from the feed thread while both are in flight.

    [Fact]
    public async Task Concurrent_market_buys_leave_a_position_that_matches_what_was_filled()
    {
        using var h = new PaperHarness();
        var fills = new System.Collections.Concurrent.ConcurrentBag<OrderUpdate>();
        h.Paper.OrderUpdateStream.Subscribe(fills.Add);
        h.LivePrice(100);

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            h.Paper.PlaceOrderAsync(new TradeSignal("BTC/USDT", OrderSide.Buy, 1.0))));

        int accepted = results.Count(r => r.StartsWith("paper-", StringComparison.Ordinal));
        var filled = fills.Where(f => f.Status == OrderStatus.Filled).ToList();

        Assert.Equal(accepted, filled.Count);
        var position = Assert.Single(await h.Paper.GetPositionsAsync());
        // The books must agree with the notifications: quantity held == quantity announced.
        Assert.Equal(filled.Sum(f => f.FilledQuantity), position.Quantity, 6);
    }

    /// <summary>
    /// Cash is the invariant that survives any interleaving: 16 concurrent buys of 1 BTC at 100
    /// cost 1,600 plus fees against a 100,000 account, so every one of them is affordable and the
    /// balance must land exactly on the arithmetic. A lost update in the cash ledger shows up here
    /// as free cash that is too high — the account quietly funding trades it did not have.
    /// </summary>
    [Fact]
    public async Task Concurrent_buys_do_not_lose_a_cash_debit()
    {
        using var h = new PaperHarness();
        h.LivePrice(100);
        double startingCash = h.Paper.StartingBalance;

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            h.Paper.PlaceOrderAsync(new TradeSignal("BTC/USDT", OrderSide.Buy, 1.0))));

        var cash = Assert.Single(await h.Paper.GetBalancesAsync(), b => b.Asset == "USDT");
        var position = Assert.Single(await h.Paper.GetPositionsAsync());

        // Each unit cost price + the 0.04% taker fee, and nothing else moved.
        double expected = startingCash - position.Quantity * 100 * 1.0004;
        Assert.Equal(expected, cash.Free, 6);
        Assert.True(cash.Free >= 0, $"cash went negative under concurrency: {cash.Free}");
    }

    /// <summary>
    /// A cancel racing the fill evaluation must resolve one way or the other, never both: the
    /// resting order is either cancelled or filled, and what is left on the books has to agree
    /// with which happened. The failure this guards is the one that is invisible until it matters
    /// — a cancelled order that fills anyway, leaving a position the trader believes they refused.
    /// </summary>
    [Fact]
    public async Task A_cancel_racing_a_fill_resolves_to_exactly_one_of_them()
    {
        for (int attempt = 0; attempt < 25; attempt++)
        {
            using var h = new PaperHarness();
            h.LivePrice(100);

            // A buy limit below the market, so it rests rather than filling on placement.
            string id = await h.Paper.PlaceOrderAsync(
                new TradeSignal("BTC/USDT", OrderSide.Buy, 1.0, OrderType.Limit, Price: 90));
            Assert.StartsWith("paper-", id);

            // Drive price down through the limit at the same moment as the cancel.
            var cancel = Task.Run(() => h.Paper.CancelOrderAsync(id, "BTC/USDT"));
            var tick = Task.Run(() => h.LivePrice(85));
            await Task.WhenAll(cancel, tick);

            var open = await h.Paper.GetOpenOrdersAsync("BTC/USDT");
            var positions = await h.Paper.GetPositionsAsync();

            Assert.Empty(open);                       // resolved either way, never left resting
            Assert.True(positions.Count <= 1, "the race produced more than one position");
            if (positions.Count == 1)
                Assert.Equal(1.0, positions[0].Quantity, 6);   // filled once, not twice
        }
    }
}
