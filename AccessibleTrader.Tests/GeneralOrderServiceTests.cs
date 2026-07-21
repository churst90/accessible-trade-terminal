using System;
using System.Collections.Generic;
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
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Direct unit tests for <see cref="GeneralOrderService"/>, the money-touching
    /// order dispatcher. Complements <see cref="OrderSafetyTests"/> (input sanity
    /// pins) with coverage of the dedup window, the MaxOrderQuantity boundary,
    /// paper-mode routing, the lifetime paper order-stream subscription, and the
    /// error-propagation paths (not-connected, submit-throw with empty recovery scan).
    /// </summary>
    public class GeneralOrderServiceTests
    {
        private static readonly TradeSignal SaneSignal = new(
            Symbol: "BTC/USD",
            Side: OrderSide.Buy,
            Quantity: 0.5,
            Type: OrderType.Market);

        private sealed record Harness(
            GeneralOrderService Svc,
            ITradingProvider LiveTp,
            IPaperTradingProvider Paper,
            ISettingsManager Settings,
            IGlobalErrorCoordinator Err,
            EventBus Bus,
            Subject<OrderUpdate> PaperStream);

        private static Harness Build()
        {
            var data = Substitute.For<IDataService>();
            // GetTradingProviderAsync casts IMarketDataProvider → ITradingProvider,
            // so the live substitute must implement both interfaces.
            var tp = Substitute.For<IMarketDataProvider, ITradingProvider>();
            var live = (ITradingProvider)tp;
            live.IsConnected.Returns(true);
            live.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
            data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<IMarketDataProvider?>(tp));

            var err = Substitute.For<IGlobalErrorCoordinator>();
            var bus = new EventBus();
            var paperStream = new Subject<OrderUpdate>();
            var paper = Substitute.For<IPaperTradingProvider>();
            paper.IsConnected.Returns(true);
            // Must be wired BEFORE construction: the service subscribes to the paper
            // stream for its whole lifetime in the constructor.
            paper.OrderUpdateStream.Returns(paperStream);
            var settings = Substitute.For<ISettingsManager>();

            // HostMode.Full → AllowLiveTrading true → routing follows the
            // trading.paperTradingMode setting (unset = live provider).
            var svc = new GeneralOrderService(
                data, err, NullLogger<GeneralOrderService>.Instance, bus, paper, settings,
                new DemoPolicy(isDemo: false));
            return new Harness(svc, live, paper, settings, err, bus, paperStream);
        }

        // ── Dedup window ─────────────────────────────────────────────────────

        [Fact]
        public async Task Duplicate_ClientOid_within_window_is_suppressed()
        {
            // A UI double-click / network-flap retry re-submits the same ClientOid
            // seconds apart; the second attempt must never reach the exchange.
            var h = Build();
            h.LiveTp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("EX-1"));
            var signal = SaneSignal with { ClientOid = "dup-1" };

            var first = await h.Svc.PlaceOrderAsync("Binance", signal);
            var second = await h.Svc.PlaceOrderAsync("Binance", signal);

            Assert.Equal("EX-1", first);
            Assert.Equal("ORDER_DUPLICATE_SUPPRESSED", second);
            await h.LiveTp.Received(1).PlaceOrderAsync(Arg.Any<TradeSignal>());
        }

        [Fact]
        public async Task Different_ClientOids_both_pass_the_dedup_gate()
        {
            var h = Build();
            h.LiveTp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("EX-OK"));

            var a = await h.Svc.PlaceOrderAsync("Binance", SaneSignal with { ClientOid = "oid-a" });
            var b = await h.Svc.PlaceOrderAsync("Binance", SaneSignal with { ClientOid = "oid-b" });

            Assert.Equal("EX-OK", a);
            Assert.Equal("EX-OK", b);
            await h.LiveTp.Received(2).PlaceOrderAsync(Arg.Any<TradeSignal>());
        }

        // ── MaxOrderQuantity boundary ────────────────────────────────────────

        [Fact]
        public async Task Quantity_above_MaxOrderQuantity_is_rejected_not_clamped()
        {
            // The service REJECTS oversized quantities outright (sentinel return,
            // nothing sent to the provider) — it does not silently clamp. A silent
            // clamp would fill a different size than the strategy asked for.
            var h = Build();
            var signal = SaneSignal with { Quantity = 10_000_000.0 + 1 };

            var result = await h.Svc.PlaceOrderAsync("Binance", signal);

            Assert.Equal("ORDER_REJECTED_QUANTITY", result);
            await h.LiveTp.DidNotReceive().PlaceOrderAsync(Arg.Any<TradeSignal>());
            h.Err.Received().ReportError(
                Arg.Is<string>(m => m.Contains("outside the allowed range")),
                ErrorSeverity.High,
                Arg.Any<ErrorCategory>());
        }

        [Fact]
        public async Task Quantity_exactly_at_MaxOrderQuantity_is_accepted()
        {
            // Boundary pin: the guard is "> MaxOrderQuantity", so exactly 1e7 passes.
            var h = Build();
            h.LiveTp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("EX-MAX"));

            var result = await h.Svc.PlaceOrderAsync("Binance", SaneSignal with { Quantity = 10_000_000.0 });

            Assert.Equal("EX-MAX", result);
            await h.LiveTp.Received(1).PlaceOrderAsync(Arg.Is<TradeSignal>(s => s.Quantity == 10_000_000.0));
        }

        // ── Paper-mode routing ───────────────────────────────────────────────

        [Fact]
        public async Task PaperMode_routes_order_to_paper_provider_not_live()
        {
            // With the desktop opt-in toggle on, ALL orders go to the simulated
            // broker — the live provider must never see the payload.
            var h = Build();
            h.Settings.GetSetting("trading.paperTradingMode").Returns(JToken.FromObject(true));
            h.Paper.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("paper-42"));

            var result = await h.Svc.PlaceOrderAsync("Binance", SaneSignal);

            Assert.Equal("paper-42", result);
            await h.Paper.Received(1).PlaceOrderAsync(Arg.Any<TradeSignal>());
            await h.LiveTp.DidNotReceive().PlaceOrderAsync(Arg.Any<TradeSignal>());
        }

        [Fact]
        public void Paper_fill_publishes_OrderFilledEvent_via_lifetime_subscription()
        {
            // The paper broker's stream is subscribed in the constructor for the
            // service's whole lifetime, so paper fills announce even when
            // SubscribeOrderUpdatesAsync was never called for the paper broker.
            var h = Build();
            OrderFilledEvent? seen = null;
            using var sub = h.Bus.Subscribe<OrderFilledEvent>(e => seen = e);

            h.PaperStream.OnNext(new OrderUpdate(
                "paper-1", "BTC/USD", OrderSide.Buy, 0.5, 45000, 0,
                OrderStatus.Filled, false, false, DateTime.UtcNow));

            Assert.NotNull(seen);
            Assert.Equal("paper-1", seen!.Order.OrderId);
        }

        [Fact]
        public void Paper_stop_update_publishes_StopHitEvent()
        {
            // StopTriggered outranks Status: a stop execution must announce as a
            // stop hit (distinct earcon), not a generic fill.
            var h = Build();
            StopHitEvent? stop = null;
            OrderFilledEvent? fill = null;
            using var s1 = h.Bus.Subscribe<StopHitEvent>(e => stop = e);
            using var s2 = h.Bus.Subscribe<OrderFilledEvent>(e => fill = e);

            h.PaperStream.OnNext(new OrderUpdate(
                "paper-2", "BTC/USD", OrderSide.Sell, 0.5, 44000, 0,
                OrderStatus.Filled, StopTriggered: true, TakeProfitTriggered: false, Timestamp: DateTime.UtcNow));

            Assert.NotNull(stop);
            Assert.Null(fill);
        }

        // ── Error propagation ────────────────────────────────────────────────

        [Fact]
        public async Task Disconnected_provider_returns_sentinel_and_reports_high_severity()
        {
            var h = Build();
            h.LiveTp.IsConnected.Returns(false);

            var result = await h.Svc.PlaceOrderAsync("Binance", SaneSignal);

            Assert.Equal("PROVIDER_NOT_CONNECTED", result);
            h.Err.Received().ReportError(
                Arg.Is<string>(m => m.Contains("not connected")),
                ErrorSeverity.High,
                Arg.Any<ErrorCategory>());
            await h.LiveTp.DidNotReceive().PlaceOrderAsync(Arg.Any<TradeSignal>());
        }

        [Fact]
        public async Task Submit_exception_with_empty_recovery_scan_returns_ORDER_FAILED()
        {
            // When PlaceOrderAsync throws AND the recovery scan of open orders finds
            // no plausible match, the caller gets the plain failure sentinel and the
            // user is told the order failed (High severity — money didn't move).
            var h = Build();
            h.LiveTp.PlaceOrderAsync(Arg.Any<TradeSignal>())
                .Returns<Task<string>>(_ => throw new InvalidOperationException("connection refused"));
            h.LiveTp.GetOpenOrdersAsync(Arg.Any<string?>())
                .Returns(_ => Task.FromResult(new List<OpenOrder>()));

            var result = await h.Svc.PlaceOrderAsync("Binance", SaneSignal);

            Assert.Equal("ORDER_FAILED", result);
            h.Err.Received().ReportError(
                Arg.Is<string>(m => m.Contains("Order failed")),
                ErrorSeverity.High,
                Arg.Any<ErrorCategory>());
        }

        // ── Live order-stream wiring (the 2026-07 audit fix) ─────────────────
        // Before this, SubscribeOrderUpdatesAsync had ZERO production callers:
        // live-broker fills never announced. It is now called reactively on
        // ConnectionStatusEvent(Connected) and must never touch the paper stream
        // (which has its own lifetime subscription — double-subscribing it would
        // double-announce every paper fill).

        private static OrderUpdate LiveFill(string id) => new(
            id, "BTC/USD", OrderSide.Buy, 0.5, 45000, 0,
            OrderStatus.Filled, false, false, DateTime.UtcNow);

        [Fact]
        public async Task Live_fill_publishes_OrderFilledEvent_after_subscription()
        {
            var h = Build();
            var liveStream = new Subject<OrderUpdate>();
            h.LiveTp.OrderUpdateStream.Returns(liveStream);
            var fills = new List<OrderFilledEvent>();
            using var sub = h.Bus.Subscribe<OrderFilledEvent>(fills.Add);

            await h.Svc.SubscribeOrderUpdatesAsync("Binance");
            liveStream.OnNext(LiveFill("live-1"));

            var fill = Assert.Single(fills);
            Assert.Equal("live-1", fill.Order.OrderId);
        }

        [Fact]
        public async Task SubscribeOrderUpdates_is_idempotent_per_provider()
        {
            // A reconnect blip publishes Connected again; the second subscribe must
            // not stack a second subscription (one fill = one announcement).
            var h = Build();
            var liveStream = new Subject<OrderUpdate>();
            h.LiveTp.OrderUpdateStream.Returns(liveStream);
            var fills = new List<OrderFilledEvent>();
            using var sub = h.Bus.Subscribe<OrderFilledEvent>(fills.Add);

            await h.Svc.SubscribeOrderUpdatesAsync("Binance");
            await h.Svc.SubscribeOrderUpdatesAsync("Binance");
            liveStream.OnNext(LiveFill("live-2"));

            Assert.Single(fills);
        }

        [Fact]
        public async Task PaperMode_does_not_reroute_or_double_subscribe_the_streams()
        {
            // Paper mode ON: (a) the LIVE stream still gets subscribed — real-money
            // orders resting on the exchange keep announcing while the user
            // practices; (b) a paper fill announces exactly ONCE (the old code
            // rerouted this call to the paper broker, stacking a second
            // subscription on top of the constructor's lifetime one).
            var h = Build();
            h.Settings.GetSetting("trading.paperTradingMode").Returns(JToken.FromObject(true));
            var liveStream = new Subject<OrderUpdate>();
            h.LiveTp.OrderUpdateStream.Returns(liveStream);
            var fills = new List<OrderFilledEvent>();
            using var sub = h.Bus.Subscribe<OrderFilledEvent>(fills.Add);

            await h.Svc.SubscribeOrderUpdatesAsync("Binance");

            liveStream.OnNext(LiveFill("live-3"));
            Assert.Single(fills);

            h.PaperStream.OnNext(LiveFill("paper-3"));
            Assert.Equal(2, fills.Count); // live + paper, each exactly once
        }

        [Fact]
        public async Task DataOnly_provider_is_skipped_without_error()
        {
            var h = Build();
            var dataOnly = Substitute.For<IMarketDataProvider>();
            var data = Substitute.For<IDataService>();
            data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<IMarketDataProvider?>(dataOnly));
            var svc = new GeneralOrderService(
                data, h.Err, NullLogger<GeneralOrderService>.Instance, h.Bus, h.Paper, h.Settings,
                new DemoPolicy(isDemo: false));

            await svc.SubscribeOrderUpdatesAsync("FRED"); // must not throw
        }

        [Fact]
        public void ConnectionStatusEvent_Connected_wires_the_live_stream()
        {
            // The end-to-end wiring: no head calls SubscribeOrderUpdatesAsync —
            // the service reacts to the connection event on its own.
            var h = Build();
            var liveStream = new Subject<OrderUpdate>();
            h.LiveTp.OrderUpdateStream.Returns(liveStream);
            var fills = new List<OrderFilledEvent>();
            using var sub = h.Bus.Subscribe<OrderFilledEvent>(fills.Add);

            h.Bus.Publish(new ConnectionStatusEvent("Binance", ConnectionState.Connected, "up"));

            // The subscription hops through the thread pool (SafeFireAndForget);
            // poll until the fill lands rather than racing it.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (fills.Count == 0 && DateTime.UtcNow < deadline)
            {
                liveStream.OnNext(LiveFill("live-4"));
                System.Threading.Thread.Sleep(20);
            }
            Assert.NotEmpty(fills);
        }

        [Fact]
        public void ConnectionStatusEvent_Error_does_not_subscribe()
        {
            var h = Build();
            var liveStream = new Subject<OrderUpdate>();
            h.LiveTp.OrderUpdateStream.Returns(liveStream);
            var fills = new List<OrderFilledEvent>();
            using var sub = h.Bus.Subscribe<OrderFilledEvent>(fills.Add);

            h.Bus.Publish(new ConnectionStatusEvent("Binance", ConnectionState.Error, "down"));
            System.Threading.Thread.Sleep(100);
            liveStream.OnNext(LiveFill("live-5"));

            Assert.Empty(fills);
        }
    }
}
