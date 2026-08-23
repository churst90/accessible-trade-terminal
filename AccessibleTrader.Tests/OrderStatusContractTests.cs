using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Plugins.Alpaca;
using AccessibleTrader.Plugins.Binance;
using AccessibleTrader.Plugins.Coinbase;
using AccessibleTrader.Plugins.InteractiveBrokers;
using AccessibleTrader.Plugins.Kraken;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The order-status contract: every <see cref="OrderStatus"/> a provider can
    /// produce is CONSUMED — published as a bus event or deliberately log-only —
    /// and every provider maps its venue's vocabulary onto the enum without
    /// squashing distinct facts together.
    ///
    /// Regression guards for the 2026-08-21 audit's ship-blocker cluster:
    /// - <c>OrderStatus.Triggered</c> was produced by four providers' fallback
    ///   arms and consumed by NOBODY — every unrecognised venue status (Coinbase
    ///   FAILED, Alpaca expired/replaced, Kraken new/pending_new) vanished with
    ///   no event, no log, no announcement.
    /// - Schwab mapped REPLACED → Cancelled: a replaced order is still live under
    ///   a new id, so the trader heard "cancelled", believed they were flat,
    ///   re-entered, and was double-sized.
    /// - Binance mapped EXPIRED → Rejected; Tradier/Schwab/Coinbase/Kraken mapped
    ///   expired → Cancelled — three different facts, one announcement.
    /// - Coinbase mapped OPEN → PartialFill unconditionally, so every
    ///   freshly-accepted resting limit order announced "partially filled" of zero.
    /// - MEXC status 5 (partially filled, then cancelled) collapsed to a bare
    ///   Cancelled, hiding the live position the partial fill opened.
    /// </summary>
    public class OrderStatusContractTests
    {
        // ── The consumption contract ─────────────────────────────────────────

        /// <summary>
        /// Statuses the order service deliberately logs without publishing:
        /// New (placement was already announced when PlaceOrderAsync returned)
        /// and Unknown (nothing actionable to say; the log names the raw venue
        /// word). Adding an enum member without deciding its consumption fails
        /// the exhaustiveness test below.
        /// </summary>
        private static readonly HashSet<OrderStatus> LogOnly = new()
        {
            OrderStatus.New,
            OrderStatus.Unknown,
        };

        private static readonly Dictionary<OrderStatus, Type> PublishedAs = new()
        {
            [OrderStatus.Filled]      = typeof(OrderFilledEvent),
            [OrderStatus.PartialFill] = typeof(OrderPartialFillEvent),
            [OrderStatus.Rejected]    = typeof(OrderRejectedEvent),
            [OrderStatus.Cancelled]   = typeof(OrderCancelledEvent),
            [OrderStatus.Expired]     = typeof(OrderExpiredEvent),
            [OrderStatus.Replaced]    = typeof(OrderReplacedEvent),
        };

        private static (GeneralOrderService svc, SpyEventBus bus, Subject<OrderUpdate> paperStream) BuildService()
        {
            var data = Substitute.For<IDataService>();
            var err = Substitute.For<IGlobalErrorCoordinator>();
            var bus = new SpyEventBus();
            var paperStream = new Subject<OrderUpdate>();
            var paper = Substitute.For<IPaperTradingProvider>();
            paper.IsConnected.Returns(true);
            paper.OrderUpdateStream.Returns(paperStream);
            var settings = Substitute.For<ISettingsManager>();
            var svc = new GeneralOrderService(
                data, err, NullLogger<GeneralOrderService>.Instance, bus, paper, settings,
                new DemoPolicy(isDemo: false), new AccessibleTrader.Core.Services.Trading.QuickTradeEquity());
            return (svc, bus, paperStream);
        }

        private static OrderUpdate UpdateWith(OrderStatus status) => new(
            "ord-1", "BTC/USD", OrderSide.Buy,
            FilledQuantity: 0, FilledPrice: 0, RemainingQuantity: 1,
            status, StopTriggered: false, TakeProfitTriggered: false,
            Timestamp: DateTime.UtcNow);

        [Fact]
        public void Every_OrderStatus_member_is_consumed_published_or_pinned_log_only()
        {
            // THE guard for "produced by four providers, consumed by nobody".
            // A new enum member must either publish a bus event or be added to
            // LogOnly here — a deliberate decision, not a silent fall-through.
            var (_, bus, paperStream) = BuildService();

            foreach (var status in Enum.GetValues<OrderStatus>())
            {
                int before = bus.Log.Count;
                paperStream.OnNext(UpdateWith(status));
                var published = bus.Log.Skip(before).ToList();

                if (LogOnly.Contains(status))
                {
                    Assert.True(published.Count == 0,
                        $"{status} is pinned log-only but published {published.Count} event(s).");
                }
                else if (PublishedAs.TryGetValue(status, out var expectedType))
                {
                    var evt = Assert.Single(published);
                    Assert.IsType(expectedType, evt);
                }
                else
                {
                    Assert.Fail(
                        $"OrderStatus.{status} is neither in PublishedAs nor in LogOnly — " +
                        "a provider can produce it and the order service will discard it. " +
                        "Wire it to an event (or pin it log-only) and record the decision here.");
                }
            }
        }

        [Fact]
        public void Executed_trigger_legs_still_route_to_stop_and_take_profit_events()
        {
            // The trigger flags only reroute EXECUTIONS. An expired stop leg must
            // announce as expired, not as "stop hit" — same class as the rejected
            // stop leg bug already pinned in OrderEventAnnouncementTests.
            var (_, bus, paperStream) = BuildService();

            paperStream.OnNext(UpdateWith(OrderStatus.Expired) with { StopTriggered = true });
            Assert.IsType<OrderExpiredEvent>(Assert.Single(bus.Log));
        }

        // ── Poller resolution for the new terminal states ────────────────────

        private static readonly TradeSignal Signal = new(
            Symbol: "BTC/USD", Side: OrderSide.Buy, Quantity: 0.5, Type: OrderType.Market);

        [Theory]
        [InlineData(PolledOrderState.Expired,  typeof(OrderExpiredEvent))]
        [InlineData(PolledOrderState.Replaced, typeof(OrderReplacedEvent))]
        public async Task Poll_resolves_expired_and_replaced_to_their_own_events(
            PolledOrderState polled, Type expectedEvent)
        {
            // Schwab REPLACED / EXPIRED used to resolve through Cancelled here.
            var (svc, bus, _) = BuildService();
            svc.OrderPollFastInterval = TimeSpan.FromMilliseconds(5);
            var tp = Substitute.For<IMarketDataProvider, ITradingProvider>();
            var live = (ITradingProvider)tp;
            live.IsConnected.Returns(true);
            live.SupportsOrderStatusQuery.Returns(true);
            live.GetOrderStatusAsync("ORD-X", Arg.Any<string?>()).Returns(
                Task.FromResult<OrderStatusSnapshot?>(new OrderStatusSnapshot(
                    polled, OrderSide.Buy, "BTC/USD", 0, 0, 0.5)));

            await svc.PollOrderUntilResolvedAsync(live, "Schwab", Signal, "ORD-X");

            Assert.Contains(bus.Log, e => e.GetType() == expectedEvent);
            Assert.DoesNotContain(bus.Log, e => e is OrderCancelledEvent or OrderRejectedEvent);
        }

        // ── Provider vocabulary → OrderStatus ────────────────────────────────

        [Theory]
        // The audit's named squashes, now distinct facts:
        [InlineData("expired",  OrderStatus.Expired)]
        [InlineData("replaced", OrderStatus.Replaced)]
        // The old fallback arm swallowed these entirely:
        [InlineData("new",         OrderStatus.New)]
        [InlineData("accepted",    OrderStatus.New)]
        [InlineData("pending_new", OrderStatus.New)]
        [InlineData("stopped",     OrderStatus.Unknown)]
        [InlineData("done_for_day", OrderStatus.Unknown)]
        // Unchanged:
        [InlineData("fill",         OrderStatus.Filled)]
        [InlineData("partial_fill", OrderStatus.PartialFill)]
        [InlineData("canceled",     OrderStatus.Cancelled)]
        [InlineData("rejected",     OrderStatus.Rejected)]
        public void Alpaca_trade_events_map_to_status(string wire, OrderStatus expected)
            => Assert.Equal(expected, AlpacaProvider.MapTradeEvent(wire));

        [Theory]
        [InlineData("filled",           OrderStatus.Filled)]
        [InlineData("partially_filled", OrderStatus.PartialFill)]
        [InlineData("canceled",         OrderStatus.Cancelled)]
        [InlineData("expired",          OrderStatus.Expired)]
        [InlineData("new",              OrderStatus.New)]
        [InlineData("pending_new",      OrderStatus.New)]
        // A stop that fires reports "triggered": it just became a working order.
        // The execution, when it comes, is its own update.
        [InlineData("triggered",        OrderStatus.New)]
        [InlineData("some_future_word", OrderStatus.Unknown)]
        public void Kraken_execution_statuses_map_to_status(string wire, OrderStatus expected)
            => Assert.Equal(expected, KrakenProvider.MapExecutionStatus(wire));

        [Theory]
        [InlineData("PARTIALLY_FILLED", OrderStatus.PartialFill)]
        [InlineData("FILLED",           OrderStatus.Filled)]
        [InlineData("CANCELED",         OrderStatus.Cancelled)]
        [InlineData("REJECTED",         OrderStatus.Rejected)]
        // EXPIRED was squashed into Rejected — but the venue ACCEPTED the order
        // and it timed out, a different fact with a different fix.
        [InlineData("EXPIRED",          OrderStatus.Expired)]
        [InlineData("EXPIRED_IN_MATCH", OrderStatus.Expired)]
        [InlineData("NEW",              OrderStatus.New)]
        [InlineData("SOMETHING_ELSE",   OrderStatus.Unknown)]
        public void Binance_execution_statuses_map_to_status(string wire, OrderStatus expected)
            => Assert.Equal(expected, BinanceProvider.MapExecutionStatus(wire));

        [Fact]
        public void Coinbase_OPEN_with_nothing_filled_is_New_not_PartialFill()
        {
            // The user channel sends status OPEN with filled_size 0 the instant a
            // limit order rests — the old mapping announced "partially filled".
            Assert.Equal(OrderStatus.New, CoinbaseProvider.MapToOrderStatus("OPEN", filledSize: 0));
            Assert.Equal(OrderStatus.PartialFill, CoinbaseProvider.MapToOrderStatus("OPEN", filledSize: 0.25));
        }

        [Theory]
        [InlineData("FILLED",    0.5, OrderStatus.Filled)]
        [InlineData("CANCELLED", 0,   OrderStatus.Cancelled)]
        [InlineData("EXPIRED",   0,   OrderStatus.Expired)]
        [InlineData("REJECTED",  0,   OrderStatus.Rejected)]
        // FAILED used to fall into the discarded Triggered arm — a refusal the
        // trader never heard.
        [InlineData("FAILED",    0,   OrderStatus.Rejected)]
        [InlineData("PENDING",   0,   OrderStatus.New)]
        [InlineData("QUEUED",    0,   OrderStatus.New)]
        [InlineData("MYSTERY",   0,   OrderStatus.Unknown)]
        public void Coinbase_statuses_map_to_status(string wire, double filled, OrderStatus expected)
            => Assert.Equal(expected, CoinbaseProvider.MapToOrderStatus(wire, filled));

        [Theory]
        [InlineData("Filled",        0,   0, OrderStatus.Filled)]
        [InlineData("Cancelled",     0,   1, OrderStatus.Cancelled)]
        [InlineData("ApiCancelled",  0,   1, OrderStatus.Cancelled)]
        [InlineData("Inactive",      0,   1, OrderStatus.Rejected)]
        [InlineData("Rejected",      0,   1, OrderStatus.Rejected)]
        [InlineData("Submitted",     0,   1, OrderStatus.New)]
        [InlineData("Submitted",     0.5, 0.5, OrderStatus.PartialFill)]
        [InlineData("PreSubmitted",  0,   1, OrderStatus.New)]
        [InlineData("PendingSubmit", 0,   1, OrderStatus.New)]
        [InlineData("PendingCancel", 0,   1, OrderStatus.New)]
        [InlineData("ApiPending",    0,   1, OrderStatus.New)]
        [InlineData("NewIbState",    0,   1, OrderStatus.Unknown)]
        public void InteractiveBrokers_statuses_map_to_status(
            string wire, double filled, double remaining, OrderStatus expected)
            => Assert.Equal(expected, InteractiveBrokersProvider.MapIbStatus(wire, filled, remaining));

        [Theory]
        // live, cancelled, executed, remaining → state
        [InlineData(true,  false, 0,    1,   PolledOrderState.Working)]
        [InlineData(true,  false, 0.25, 0.75, PolledOrderState.PartiallyFilled)]
        [InlineData(false, false, 1,    0,   PolledOrderState.Filled)]
        // A fully-executed order is Filled even if the venue flags the zero
        // remainder as cancelled (IOC bookkeeping).
        [InlineData(false, true,  1,    0,   PolledOrderState.Filled)]
        // The audit's case: Gemini's emulated market order (IOC limit) partially
        // fills and cancels — the trader owns coins. Cancelled is the truthful
        // terminal state; the snapshot carries the executed amount and the
        // announcement speaks it.
        [InlineData(false, true,  0.25, 0.75, PolledOrderState.Cancelled)]
        [InlineData(false, true,  0,    1,   PolledOrderState.Cancelled)]
        [InlineData(false, false, 0,    1,   PolledOrderState.Cancelled)]
        public void Gemini_order_state_ladder(
            bool live, bool cancelled, double executed, double remaining, PolledOrderState expected)
            => Assert.Equal(expected,
                AccessibleTrader.Plugins.Gemini.GeminiProvider.MapOrderState(live, cancelled, executed, remaining));
    }
}
