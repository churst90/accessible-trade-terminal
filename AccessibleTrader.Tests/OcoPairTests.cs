using System.Reactive.Linq;
using System.Reflection;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Live OCO. Two layers pinned: the ORDER SERVICE's routing (native provider
    /// → one exchange call; paper → grouped legs with rollback; flag-only
    /// providers → refused, because two unlinked orders must never masquerade as
    /// a pair) and BINANCE's native request shape against the current
    /// /api/v3/orderList/oco endpoint (above/below leg vocabulary per side).
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class OcoPairTests
    {

        /// <summary>
        /// The one venue call this operation is about.
        ///
        /// <para>Binance and MEXC now probe <c>/api/v3/time</c> before signing, so that a
        /// desktop with a drifted clock does not have every signed call rejected with
        /// <c>-1021</c>. That probe is captured like any other request, so a bare
        /// <c>Captured.Single()</c> would now see two. Excluding it keeps what these
        /// assertions were always for: exactly ONE order left the process.</para>
        /// </summary>
        private static HttpRequestMessage OnlyVenueCall(FakeHttpMessageHandler h) =>
            h.Captured.Single(r =>
                !r.RequestUri!.AbsolutePath.EndsWith("/api/v3/time", StringComparison.Ordinal));
        // ── Order-service routing ────────────────────────────────────────────

        private sealed record Harness(GeneralOrderService Svc, ITradingProvider LiveTp,
            IPaperTradingProvider Paper, ISettingsManager Settings, IGlobalErrorCoordinator Err);

        private static Harness Build(bool liveIsNativeOco = false)
        {
            var data = Substitute.For<IDataService>();
            var tp = liveIsNativeOco
                ? (ITradingProvider)Substitute.For<IMarketDataProvider, ITradingProvider, IOcoTradingProvider>()
                : (ITradingProvider)Substitute.For<IMarketDataProvider, ITradingProvider>();
            tp.IsConnected.Returns(true);
            tp.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
            tp.SupportsOrderEventStreaming.Returns(true);
            data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<IMarketDataProvider?>((IMarketDataProvider)tp));

            var paper = Substitute.For<IPaperTradingProvider>();
            paper.IsConnected.Returns(true);
            paper.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());

            var settings = Substitute.For<ISettingsManager>();
            var err = Substitute.For<IGlobalErrorCoordinator>();
            var svc = new GeneralOrderService(data, err, NullLogger<GeneralOrderService>.Instance,
                new EventBus(), paper, settings, new DemoPolicy(isDemo: false),
                new AccessibleTrader.Core.Services.Trading.QuickTradeEquity());
            return new Harness(svc, tp, paper, settings, err);
        }

        [Fact]
        public async Task Native_provider_gets_one_exchange_call_not_two_orders()
        {
            var h = Build(liveIsNativeOco: true);
            var native = (IOcoTradingProvider)h.LiveTp;
            native.PlaceOcoPairAsync(Arg.Any<string>(), Arg.Any<OrderSide>(), Arg.Any<double>(),
                Arg.Any<double>(), Arg.Any<double>()).Returns(Task.FromResult("12345"));

            var (ok, msg) = await h.Svc.PlaceOcoPairAsync("Binance", "BTC/USDT", OrderSide.Sell, 0.5, 110, 95);

            Assert.True(ok);
            Assert.Contains("exchange", msg);
            await native.Received(1).PlaceOcoPairAsync("BTC/USDT", OrderSide.Sell, 0.5, 110, 95);
            await h.LiveTp.DidNotReceive().PlaceOrderAsync(Arg.Any<TradeSignal>());
        }

        [Fact]
        public async Task Paper_mode_places_two_grouped_legs()
        {
            var h = Build();
            h.Settings.GetSetting("trading.paperTradingMode").Returns(Newtonsoft.Json.Linq.JToken.FromObject(true));
            var signals = new List<TradeSignal>();
            h.Paper.PlaceOrderAsync(Arg.Do<TradeSignal>(signals.Add)).Returns(_ => Task.FromResult("paper-1"));

            var (ok, _) = await h.Svc.PlaceOcoPairAsync("Binance", "BTC/USDT", OrderSide.Sell, 0.5, 110, 95);

            Assert.True(ok);
            Assert.Equal(2, signals.Count);
            Assert.NotNull(signals[0].OcoGroupId);
            Assert.Equal(signals[0].OcoGroupId, signals[1].OcoGroupId); // same pair
            Assert.Equal(OrderType.Limit, signals[0].Type);
            Assert.Equal(OrderType.StopMarket, signals[1].Type);
        }

        [Fact]
        public async Task Paper_second_leg_failure_rolls_back_the_first()
        {
            var h = Build();
            h.Settings.GetSetting("trading.paperTradingMode").Returns(Newtonsoft.Json.Linq.JToken.FromObject(true));
            int calls = 0;
            h.Paper.PlaceOrderAsync(Arg.Any<TradeSignal>())
                .Returns(_ => Task.FromResult(++calls == 1 ? "paper-1" : "ORDER_FAILED:nope"));
            h.Paper.CancelOrderAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult(true));

            var (ok, msg) = await h.Svc.PlaceOcoPairAsync("Binance", "BTC/USDT", OrderSide.Sell, 0.5, 110, 95);

            Assert.False(ok);
            Assert.Contains("cancelled", msg);
            await h.Paper.Received(1).CancelOrderAsync("paper-1", "BTC/USDT"); // never half a pair
        }

        /// <summary>
        /// A rollback that did not work is not a rollback, and must not be announced as one.
        ///
        /// <para>
        /// <c>CancelOrderAsync</c> returns <c>bool</c> and the result was discarded, so when
        /// the cancel failed — a provider that had gone offline, or an exception swallowed
        /// into false — the user was told "the limit leg was cancelled, nothing is resting"
        /// while a naked limit leg sat on the book. That is the one outcome the rollback
        /// exists to prevent, announced as its own success.
        /// </para>
        /// </summary>
        [Fact]
        public async Task Paper_a_rollback_that_fails_is_not_announced_as_a_rollback()
        {
            var h = Build();
            h.Settings.GetSetting("trading.paperTradingMode").Returns(Newtonsoft.Json.Linq.JToken.FromObject(true));
            int calls = 0;
            h.Paper.PlaceOrderAsync(Arg.Any<TradeSignal>())
                .Returns(_ => Task.FromResult(++calls == 1 ? "paper-1" : "ORDER_FAILED:nope"));
            h.Paper.CancelOrderAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult(false));

            var (ok, msg) = await h.Svc.PlaceOcoPairAsync("Binance", "BTC/USDT", OrderSide.Sell, 0.5, 110, 95);

            Assert.False(ok);
            Assert.DoesNotContain("nothing is resting", msg);
            // Names the order, says it may still be live, and says what to do about it.
            Assert.Contains("paper-1", msg);
            Assert.Contains("may still be resting", msg);
            h.Err.Received().ReportError(Arg.Is<string>(m => m.Contains("rollback failed")),
                                         ErrorSeverity.High);
        }

        [Fact]
        public async Task Flag_only_provider_is_refused()
        {
            // IBKR declares the OCO capability flag but implements no linking —
            // the service must refuse rather than rest two unlinked orders.
            var h = Build(liveIsNativeOco: false);

            Assert.False(await h.Svc.SupportsOcoPairsAsync("IBKR"));
            var (ok, msg) = await h.Svc.PlaceOcoPairAsync("IBKR", "AAPL", OrderSide.Sell, 10, 250, 230);
            Assert.False(ok);
            Assert.Contains("cannot place linked OCO", msg);
        }

        [Theory]
        [InlineData("Sell", 95, 110)]  // sell: limit BELOW stop → inverted
        [InlineData("Buy", 110, 95)]   // buy: stop BELOW limit → inverted
        public async Task Inverted_price_layouts_are_refused_before_anything_rests(
            string side, double limit, double stop)
        {
            var h = Build(liveIsNativeOco: true);

            var (ok, _) = await h.Svc.PlaceOcoPairAsync("Binance", "BTC/USDT",
                side == "Buy" ? OrderSide.Buy : OrderSide.Sell, 0.5, limit, stop);

            Assert.False(ok);
            await ((IOcoTradingProvider)h.LiveTp).DidNotReceive().PlaceOcoPairAsync(
                Arg.Any<string>(), Arg.Any<OrderSide>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<double>());
        }

        // ── Binance native request shape ─────────────────────────────────────

        private static AccessibleTrader.Plugins.Binance.BinanceProvider NewBinance(FakeHttpMessageHandler handler)
        {
            var p = new AccessibleTrader.Plugins.Binance.BinanceProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "k", ["ApiSecret"] = "s" });
            HttpClientSwap.ReplaceAll(p, handler);
            return p;
        }

        [Fact]
        public async Task Binance_sell_pair_puts_the_limit_above_and_the_stop_below()
        {
            var handler = new FakeHttpMessageHandler()
                .Post(@"api\.binance\.com/api/v3/orderList/oco", """{"orderListId": 4276}""");
            var provider = NewBinance(handler);

            string result = await provider.PlaceOcoPairAsync("BTC/USDT", OrderSide.Sell, 0.5, 110000, 95000);

            Assert.Equal("4276", result);
            string url = OnlyVenueCall(handler).RequestUri!.ToString();
            Assert.Contains("symbol=BTCUSDT", url);
            Assert.Contains("side=SELL", url);
            Assert.Contains("aboveType=LIMIT_MAKER", url);
            Assert.Contains("abovePrice=110000", url);
            Assert.Contains("belowType=STOP_LOSS", url);
            Assert.Contains("belowStopPrice=95000", url);
            Assert.Contains("signature=", url); // signed endpoint
        }

        [Fact]
        public async Task Binance_buy_pair_is_the_mirror_layout()
        {
            var handler = new FakeHttpMessageHandler()
                .Post(@"api\.binance\.com/api/v3/orderList/oco", """{"orderListId": 9}""");
            var provider = NewBinance(handler);

            await provider.PlaceOcoPairAsync("ETH/USDT", OrderSide.Buy, 1.0, 3000, 3500);

            string url = OnlyVenueCall(handler).RequestUri!.ToString();
            Assert.Contains("side=BUY", url);
            Assert.Contains("aboveType=STOP_LOSS", url);
            Assert.Contains("aboveStopPrice=3500", url);
            Assert.Contains("belowType=LIMIT_MAKER", url);
            Assert.Contains("belowPrice=3000", url);
        }

        [Fact]
        public async Task Binance_error_response_returns_a_sentinel_not_a_throw()
        {
            var handler = new FakeHttpMessageHandler()
                .Post(@"api\.binance\.com/api/v3/orderList/oco",
                    """{"code":-2010,"msg":"insufficient balance"}""", System.Net.HttpStatusCode.BadRequest);
            var provider = NewBinance(handler);

            string result = await provider.PlaceOcoPairAsync("BTC/USDT", OrderSide.Sell, 0.5, 110000, 95000);

            Assert.StartsWith("ORDER_FAILED", result);
        }
    }
}
