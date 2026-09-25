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
/// The last hop of a quick trade: <see cref="QuickTradeExecutor"/> turns the armed, sized, spoken
/// <see cref="QuickTradeRequestedEvent"/> into the <see cref="TradeSignal"/> a broker receives.
///
/// <para>
/// <b>Why this file exists (A2n, 2026-09-24).</b> The service that BUILDS the event is well
/// guarded — <c>QuickTradeTests</c> pins its side, quantity, stop and market/limit choice. The
/// executor that READS it had no test at all: nothing in 8,088 constructed one. Four mutants
/// survived the full suite there — a long sent as a SELL, a limit at the cursor sent to MARKET,
/// the stop the whole size was computed from never sent, and a refusal dropped on the floor after
/// "sent" had been spoken (the very defect the executor's own comment describes). Every one is a
/// trade the user did not ask for, reached by a keystroke that had just told them the opposite.
/// </para>
/// </summary>
public class QuickTradeExecutorOrderTests
{
    private sealed class Rig : IDisposable
    {
        public readonly SpyEventBus Bus = new();
        public readonly IOrderExecutionService Orders = Substitute.For<IOrderExecutionService>();
        public readonly List<(string Provider, TradeSignal Signal)> Sent = new();
        private readonly QuickTradeExecutor _executor;

        public Rig(string reply = "paper-order-1")
        {
            var store = new MockWorkspaceStore();
            store.EmitState(WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Crypto", "Paper", "BTCUSDT", "1h"),
            });

            Orders.PlaceOrderAsync(Arg.Any<string>(), Arg.Any<TradeSignal>())
                  .Returns(ci =>
                  {
                      lock (Sent) Sent.Add((ci.ArgAt<string>(0), ci.ArgAt<TradeSignal>(1)));
                      return Task.FromResult(OrderPlacement.Parse(reply));
                  });

            _executor = new QuickTradeExecutor(Bus, Orders, store, NullLogger<QuickTradeExecutor>.Instance);
        }

        /// <summary>Publish and wait for the async-void handler to reach the broker (and past it).</summary>
        public TradeSignal Request(bool isLong, double? entry, double stop = 95, double qty = 2.5)
        {
            Bus.Publish(new QuickTradeRequestedEvent("BTCUSDT", isLong, qty, entry, stop, RiskCash: 10));
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                lock (Sent) if (Sent.Count > 0) return Sent[^1].Signal;
                Thread.Sleep(5);
            }
            throw new TimeoutException("the executor never reached the order service");
        }

        public string[] Errors() =>
            Bus.Log.OfType<FeedbackRequestEvent>()
               .Where(f => f.Type == FeedbackType.Error).Select(f => f.Message).ToArray();

        public void Dispose() => _executor.Dispose();
    }

    [Fact]
    public void A_long_is_sent_as_a_buy_and_a_short_as_a_sell()
    {
        using (var rig = new Rig())
            Assert.Equal(OrderSide.Buy, rig.Request(isLong: true, entry: null).Side);
        using (var rig = new Rig())
            Assert.Equal(OrderSide.Sell, rig.Request(isLong: false, entry: null).Side);
    }

    /// <summary>
    /// Shift+Enter is a limit at the bar under the cursor; Control+Enter is market. A limit that
    /// reaches the broker as a market order fills at whatever the book offers — the price the user
    /// chose is silently discarded.
    /// </summary>
    [Fact]
    public void A_priced_request_is_a_limit_at_that_price_and_an_unpriced_one_is_market()
    {
        using (var rig = new Rig())
        {
            var limit = rig.Request(isLong: true, entry: 101.25);
            Assert.Equal(OrderType.Limit, limit.Type);
            Assert.Equal(101.25, limit.Price);
        }
        using (var rig = new Rig())
        {
            var market = rig.Request(isLong: true, entry: null);
            Assert.Equal(OrderType.Market, market.Type);
            Assert.Null(market.Price);
        }
    }

    /// <summary>
    /// The quantity was DERIVED from the stop (risk-at-stop sizing) or spoken alongside it; an
    /// entry without its protective leg is a position the user was told was protected.
    /// </summary>
    [Fact]
    public void The_stop_and_quantity_the_user_heard_are_the_ones_sent()
    {
        using var rig = new Rig();
        var sig = rig.Request(isLong: true, entry: null, stop: 94.5, qty: 3.75);

        Assert.Equal(94.5, sig.StopLoss);
        Assert.Equal(3.75, sig.Quantity);
        Assert.Equal("BTCUSDT", sig.Symbol);
        Assert.Equal("Paper", rig.Sent.Single().Provider);
    }

    /// <summary>
    /// "Market buy sent" is spoken before the broker answers. A refusal must therefore be spoken
    /// as an error, or the user believes they hold a position they do not.
    /// </summary>
    [Fact]
    public void A_refused_quick_trade_is_announced_as_an_error()
    {
        using var rig = new Rig(reply: "ORDER_FAILED:insufficient paper balance");
        rig.Request(isLong: true, entry: null);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (rig.Errors().Length == 0 && DateTime.UtcNow < deadline) Thread.Sleep(5);

        var error = Assert.Single(rig.Errors());
        Assert.Contains("Not placed", error);
    }

    /// <summary>
    /// ORDER_UNCERTAIN: the submit threw, and a matching order was then found on the exchange. It
    /// is NOT a clean success — the one thing the user must hear is "check your open orders before
    /// placing it again", because the likeliest next keystroke is a retry that doubles the position.
    /// A success says nothing here, so counting this outcome as one silences exactly that sentence.
    /// </summary>
    [Fact]
    public void An_uncertain_quick_trade_tells_the_user_to_check_before_retrying()
    {
        Assert.False(OrderPlacement.Parse("ORDER_UNCERTAIN:abc123").Succeeded);

        using var rig = new Rig(reply: "ORDER_UNCERTAIN:abc123");
        rig.Request(isLong: true, entry: null);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (rig.Errors().Length == 0 && DateTime.UtcNow < deadline) Thread.Sleep(5);

        var said = Assert.Single(rig.Errors());
        Assert.Contains("Check your open orders", said);
        Assert.Contains("abc123", said);
    }

    /// <summary>Vacuity check for the test above: a placed order says nothing extra.</summary>
    [Fact]
    public void A_placed_quick_trade_adds_no_error()
    {
        using var rig = new Rig(reply: "paper-order-1");
        rig.Request(isLong: true, entry: null);
        Thread.Sleep(50);
        Assert.Empty(rig.Errors());
    }
}
