using System.Net;
using System.Text;
using AccessibleTrader.Plugins.Gemini;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Gemini before real money: account reads that do not queue up an order, and each
    /// market's smallest order and decimals.
    ///
    /// <para>
    /// <b>The queue.</b> The private limiter is 30 calls per 30 seconds on purpose (the nonce
    /// is second-resolution; see GeminiProvider), but the trading dashboard asked for
    /// balances, open orders and fills every two seconds: about 45 calls per 30 seconds, so
    /// the queue never drained and an order or a close waited behind it.
    /// </para>
    ///
    /// <para>
    /// <b>The rules.</b> Every order went out at eight decimals with no minimum checked, and
    /// Gemini answered in its own codes. The symbol-details fixtures are Gemini's real
    /// answers from <c>api.gemini.com/v1/symbols/details/{btcusd,solusd}</c>, read
    /// 2026-11-10. What these cannot show is Gemini accepting the orders; that needs a funded
    /// account.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class GeminiVenueRulesTests
    {
        // Read live 2026-11-10. Note the steps are JSON numbers and the minimum a string.
        internal const string BtcUsdDetails =
            """{"symbol":"BTCUSD","quote_currency":"USD","tick_size":0.00000001,"quote_increment":0.01,"min_order_size":"0.00001","status":"open","wrap_enabled":false,"product_type":"spot","contract_type":"vanilla","contract_price_currency":"USD","base_currency":"BTC"}""";
        internal const string SolUsdDetails =
            """{"symbol":"SOLUSD","quote_currency":"USD","tick_size":0.000001,"quote_increment":0.001,"min_order_size":"0.001","status":"open","wrap_enabled":false,"product_type":"spot","contract_type":"vanilla","contract_price_currency":"USD","base_currency":"SOL"}""";

        private const string BalancesJson =
            """[{"type":"exchange","currency":"BTC","amount":"1.5","available":"1.2","availableForWithdrawal":"1.2"},{"type":"exchange","currency":"USD","amount":"2500","available":"2500","availableForWithdrawal":"2500"}]""";
        private const string OrdersJson =
            """[{"order_id":"77","symbol":"btcusd","side":"buy","type":"exchange limit","price":"90000.00","remaining_amount":"0.01","is_live":true}]""";
        private const string FillsJson =
            """[{"tid":5,"symbol":"BTCUSD","type":"Buy","amount":"0.01","price":"91000","timestampms":1762765000000,"fee_amount":"0.5","order_id":"70"}]""";
        private const string AcceptedJson =
            """{"order_id":"106817811","is_cancelled":false,"executed_amount":"0"}""";

        private sealed class Clock
        {
            public DateTime Now = new(2026, 11, 10, 9, 30, 0, DateTimeKind.Utc);
            public void Step(TimeSpan by) => Now += by;
        }

        private static GeminiProvider Gemini(FakeHttpMessageHandler h, Clock clock)
        {
            var p = new GeminiProvider(() => clock.Now);
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "gk", ["ApiSecret"] = "gs", ["Environment"] = "Live" });
            SymbolListHarness.SwapEveryHttpClient(p, h);
            return p;
        }

        private static FakeHttpMessageHandler Venue()
        {
            var h = new FakeHttpMessageHandler { StrictMode = true };
            h.Get(@"api\.gemini\.com/v1/symbols/details/btcusd$", BtcUsdDetails);
            h.Get(@"api\.gemini\.com/v1/symbols/details/solusd$", SolUsdDetails);
            h.Post(@"api\.gemini\.com/v1/balances$", BalancesJson);
            h.Post(@"api\.gemini\.com/v1/orders$", OrdersJson);
            h.Post(@"api\.gemini\.com/v1/mytrades$", FillsJson);
            h.Post(@"api\.gemini\.com/v1/order/new$", AcceptedJson);
            h.Post(@"api\.gemini\.com/v1/order/cancel$", """{"order_id":"77","is_cancelled":true}""");
            return h;
        }

        private static int Calls(FakeHttpMessageHandler h, string path) =>
            h.Captured.Count(r => r.RequestUri!.AbsolutePath == path);

        private static int PrivateCalls(FakeHttpMessageHandler h) =>
            h.Captured.Count(r => r.Method == HttpMethod.Post);

        private static List<JObject> SentOrders(FakeHttpMessageHandler h) =>
            h.Captured.Where(r => r.RequestUri!.AbsolutePath == "/v1/order/new")
             .Select(r => JObject.Parse(Encoding.UTF8.GetString(
                 Convert.FromBase64String(r.Headers.GetValues("X-GEMINI-PAYLOAD").Single()))))
             .ToList();

        // ── Account reads ────────────────────────────────────────────────────

        [Fact]
        public async Task A_second_read_inside_the_time_to_live_sends_nothing_and_one_after_it_asks_again()
        {
            var clock = new Clock();
            var h = Venue();
            var p = Gemini(h, clock);

            var first  = await p.GetBalancesAsync();
            var orders = await p.GetOpenOrdersAsync();
            var fills  = await p.GetFillsAsync("BTCUSD");
            clock.Step(TimeSpan.FromSeconds(14));
            var second = await p.GetBalancesAsync();
            await p.GetOpenOrdersAsync("BTCUSD");
            await p.GetFillsAsync("BTCUSD");

            Assert.Equal(1, Calls(h, "/v1/balances"));
            Assert.Equal(1, Calls(h, "/v1/orders"));
            Assert.Equal(1, Calls(h, "/v1/mytrades"));
            Assert.Contains(first, b => b.Asset == "BTC" && b.Free == 1.2);
            Assert.Single(orders);
            Assert.Single(fills);
            // Each caller has its own list.
            Assert.NotSame(first, second);
            first.Clear();
            Assert.Equal(2, (await p.GetBalancesAsync()).Count);

            clock.Step(TimeSpan.FromSeconds(2));   // 16 s: balances and orders are due again
            await p.GetBalancesAsync();
            await p.GetOpenOrdersAsync();
            await p.GetFillsAsync("BTCUSD");       // history keeps for a minute
            Assert.Equal(2, Calls(h, "/v1/balances"));
            Assert.Equal(2, Calls(h, "/v1/orders"));
            Assert.Equal(1, Calls(h, "/v1/mytrades"));
        }

        [Fact]
        public async Task Fills_for_another_symbol_are_a_separate_question()
        {
            var h = Venue();
            var p = Gemini(h, new Clock());

            await p.GetFillsAsync("BTCUSD");
            await p.GetFillsAsync("SOLUSD");
            await p.GetFillsAsync("BTCUSD", 10);

            Assert.Equal(3, Calls(h, "/v1/mytrades"));
        }

        /// <summary>A transport that holds every answer until released, to show callers
        /// asking at the same moment share one request.</summary>
        private sealed class HeldHandler : HttpMessageHandler
        {
            public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int Sent;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Interlocked.Increment(ref Sent);
                await Release.Task.ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BalancesJson, Encoding.UTF8, "application/json"),
                };
            }
        }

        [Fact]
        public async Task Callers_asking_at_the_same_moment_share_one_request()
        {
            var held = new HeldHandler();
            var p = new GeminiProvider(() => DateTime.UtcNow);
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "gk", ["ApiSecret"] = "gs", ["Environment"] = "Live" });
            SymbolListHarness.SwapEveryHttpClient(p, held);

            var reads = Enumerable.Range(0, 5).Select(_ => p.GetBalancesAsync()).ToList();
            held.Release.SetResult();
            var answers = await Task.WhenAll(reads);

            Assert.Equal(1, held.Sent);
            Assert.All(answers, a => Assert.Contains(a, b => b.Asset == "USD" && b.Free == 2500));
        }

        [Fact]
        public async Task Half_a_minute_of_the_dashboard_leaves_the_limiter_room_for_an_order()
        {
            // The dashboard's two-second refresh: balances, positions (no call on a spot
            // venue), open orders and fills, fifteen times over thirty seconds. Uncached that
            // was 45 private calls against a limit of 30 per 30 s, and the order queued behind
            // them. The limiter only ever waits once 30 calls have gone in its window, so
            // showing the count stays under 30 shows the order was not held — no timing.
            var clock = new Clock();
            var h = Venue();
            var p = Gemini(h, clock);

            for (int tick = 0; tick < 15; tick++)
            {
                await p.GetBalancesAsync();
                await p.GetPositionsAsync();
                await p.GetOpenOrdersAsync();
                await p.GetFillsAsync("BTCUSD", 50);
                clock.Step(TimeSpan.FromSeconds(2));
            }
            int dashboard = PrivateCalls(h);

            string id = await p.PlaceOrderAsync(new TradeSignal("BTCUSD", OrderSide.Sell, 0.01,
                OrderType.Limit, Price: 95000));

            // balances at 0 s and 16 s, orders at 0 s and 16 s, fills once.
            Assert.Equal(5, dashboard);
            Assert.Equal("106817811", id);
            Assert.Equal(dashboard + 1, PrivateCalls(h));
            Assert.True(PrivateCalls(h) < 30, $"{PrivateCalls(h)} private calls in 30 s fill the nonce limiter");
        }

        [Fact]
        public async Task An_order_a_cancel_and_a_status_poll_that_shows_a_fill_each_make_the_next_read_fresh()
        {
            var clock = new Clock();
            var h = Venue();
            string status = """{"order_id":"77","symbol":"btcusd","side":"buy","is_live":true,"is_cancelled":false,"executed_amount":"0","remaining_amount":"0.01","avg_execution_price":"0"}""";
            h.Add(HttpMethod.Post, @"api\.gemini\.com/v1/order/status$", _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(status, Encoding.UTF8, "application/json"),
            });
            var p = Gemini(h, clock);

            await p.GetBalancesAsync();
            await p.PlaceOrderAsync(new TradeSignal("BTCUSD", OrderSide.Buy, 0.01, OrderType.Limit, Price: 90000));
            await p.GetBalancesAsync();
            Assert.Equal(2, Calls(h, "/v1/balances"));

            await p.CancelOrderAsync("77", "BTCUSD");
            await p.GetBalancesAsync();
            Assert.Equal(3, Calls(h, "/v1/balances"));

            // A working order polled again and again, unchanged, does not empty the cache:
            // it can rest for hours, and emptying on every poll would bring the queue back.
            await p.GetOrderStatusAsync("77", "BTCUSD");
            await p.GetOrderStatusAsync("77", "BTCUSD");
            await p.GetBalancesAsync();
            Assert.Equal(3, Calls(h, "/v1/balances"));

            // The poll that first shows it filled is Gemini's execution report.
            status = """{"order_id":"77","symbol":"btcusd","side":"buy","is_live":false,"is_cancelled":false,"executed_amount":"0.01","remaining_amount":"0","avg_execution_price":"90000"}""";
            await p.GetOrderStatusAsync("77", "BTCUSD");
            await p.GetBalancesAsync();
            Assert.Equal(4, Calls(h, "/v1/balances"));
        }

        // ── Each market's rules ──────────────────────────────────────────────

        [Fact]
        public async Task The_rules_are_read_from_Geminis_own_symbol_details()
        {
            var p = Gemini(Venue(), new Clock());

            var btc = await p.GetSymbolRulesAsync("btcusd");
            var sol = await p.GetSymbolRulesAsync("solusd");

            Assert.NotNull(btc);
            // tick_size is the AMOUNT step on Gemini; quote_increment is the price step.
            Assert.Equal(0.00001m, btc!.MinQuantity);
            Assert.Equal(0.00000001m, btc.QuantityStep);
            Assert.Equal(0.01m, btc.PriceTick);
            Assert.Null(btc.NotTrading);
            Assert.Equal("BTC/USD", btc.Symbol);
            Assert.Equal("BTC", btc.QuantityUnit);

            Assert.Equal(0.001m, sol!.MinQuantity);
            Assert.Equal(0.000001m, sol.QuantityStep);
            Assert.Equal(0.001m, sol.PriceTick);
        }

        [Fact]
        public async Task An_order_under_the_minimum_is_refused_in_words_and_nothing_is_sent()
        {
            var h = Venue();
            var p = Gemini(h, new Clock());

            string result = await p.PlaceOrderAsync(new TradeSignal("BTCUSD", OrderSide.Buy, 0.000005,
                OrderType.Limit, Price: 95000));

            Assert.Equal("ORDER_FAILED:Gemini's smallest BTC/USD order is 0.00001 BTC; this one is 0.000005 BTC", result);
            Assert.Empty(SentOrders(h));
            Assert.Equal(0, PrivateCalls(h));
        }

        [Fact]
        public async Task Amounts_and_prices_go_out_on_the_markets_own_steps()
        {
            var h = Venue();
            var p = Gemini(h, new Clock());

            // SOL: amount step 0.000001 (cut down, never up), price step 0.001.
            await p.PlaceOrderAsync(new TradeSignal("SOLUSD", OrderSide.Sell, 1.23456789,
                OrderType.Limit, Price: 187.12345));
            // BTC stop-limit: both the trigger and the limit on the cent.
            await p.PlaceOrderAsync(new TradeSignal("BTCUSD", OrderSide.Sell, 0.00002,
                OrderType.StopLimit, Price: 89999.994, TriggerPrice: 90000.005));

            var sent = SentOrders(h);
            Assert.Equal(2, sent.Count);
            Assert.Equal("1.234567", sent[0]["amount"]!.ToString());
            Assert.Equal("187.123", sent[0]["price"]!.ToString());
            Assert.Equal("0.00002", sent[1]["amount"]!.ToString());   // never 2E-05
            Assert.Equal("90000.01", sent[1]["stop_price"]!.ToString());
            Assert.Equal("89999.99", sent[1]["price"]!.ToString());
        }

        [Fact]
        public async Task A_market_not_open_is_refused_by_name()
        {
            var h = new FakeHttpMessageHandler { StrictMode = true };
            h.Get(@"api\.gemini\.com/v1/symbols/details/btcusd$", BtcUsdDetails.Replace("\"status\":\"open\"", "\"status\":\"cancel_only\""));
            h.Post(@"api\.gemini\.com/v1/order/new$", AcceptedJson);
            var p = Gemini(h, new Clock());

            string result = await p.PlaceOrderAsync(new TradeSignal("BTCUSD", OrderSide.Buy, 0.01,
                OrderType.Limit, Price: 95000));

            Assert.Equal("ORDER_FAILED:Gemini is not taking new BTC/USD orders (the market is cancel only)", result);
            Assert.Empty(SentOrders(h));
        }

        // Cody, 2026-11-26, on Gemini's limit-only markets: "of course" let limits through.
        // Until then every status but "open" refused every order.
        private static (GeminiProvider P, FakeHttpMessageHandler H) MarketIn(string status)
        {
            var h = new FakeHttpMessageHandler { StrictMode = true };
            h.Get(@"api\.gemini\.com/v1/symbols/details/btcusd$", BtcUsdDetails.Replace("\"status\":\"open\"", $"\"status\":\"{status}\""));
            h.Post(@"api\.gemini\.com/v1/order/new$", AcceptedJson);
            return (Gemini(h, new Clock()), h);
        }

        [Fact]
        public async Task A_limit_on_a_limit_only_market_goes_out()
        {
            var (p, h) = MarketIn("limit_only");

            string id = await p.PlaceOrderAsync(new TradeSignal("BTCUSD", OrderSide.Buy, 0.01, OrderType.Limit, Price: 95000));

            Assert.Equal("106817811", id);
            var sent = Assert.Single(SentOrders(h));
            Assert.Equal("exchange limit", sent["type"]!.ToString());
            Assert.Null(sent["options"]);
        }

        [Fact]
        public async Task A_market_or_stop_order_on_a_limit_only_market_is_refused_in_words()
        {
            var (p, h) = MarketIn("limit_only");

            string market = await p.PlaceOrderAsync(new TradeSignal("BTCUSD", OrderSide.Buy, 0.01, OrderType.Market));
            string stop = await p.PlaceOrderAsync(new TradeSignal("BTCUSD", OrderSide.Sell, 0.01,
                OrderType.StopLimit, Price: 89000, TriggerPrice: 90000));

            Assert.Equal("ORDER_FAILED:Gemini takes only limit orders on BTC/USD right now (the market is limit only); "
                       + "a market order would be refused", market);
            Assert.Equal("ORDER_FAILED:Gemini takes only limit orders on BTC/USD right now (the market is limit only); "
                       + "a stop-limit order would be refused", stop);
            Assert.Empty(SentOrders(h));
        }

        [Fact]
        public async Task A_post_only_market_takes_a_post_only_limit_as_maker_or_cancel_and_nothing_else()
        {
            var (p, h) = MarketIn("post_only");

            string plain = await p.PlaceOrderAsync(new TradeSignal("BTCUSD", OrderSide.Buy, 0.01, OrderType.Limit, Price: 95000));
            Assert.Equal("ORDER_FAILED:Gemini takes only post-only limit orders on BTC/USD right now (the market is post only); "
                       + "send a limit order with Post-only ticked", plain);
            Assert.Empty(SentOrders(h));

            string id = await p.PlaceOrderAsync(new TradeSignal("BTCUSD", OrderSide.Buy, 0.01, OrderType.Limit, Price: 95000, PostOnly: true));
            Assert.Equal("106817811", id);
            var sent = Assert.Single(SentOrders(h));
            Assert.Equal("maker-or-cancel", Assert.Single((JArray)sent["options"]!)!.ToString());
        }

        [Fact]
        public async Task When_the_rules_cannot_be_read_the_order_goes_out_as_before()
        {
            // A failed public lookup must not block a real order (a close, a stop) the venue
            // would have taken; Gemini then judges it itself, as it always did.
            var h = new FakeHttpMessageHandler { StrictMode = true };
            h.Get(@"api\.gemini\.com/v1/symbols/details/btcusd$", "<html>bad gateway</html>", HttpStatusCode.BadGateway);
            h.Post(@"api\.gemini\.com/v1/order/new$", AcceptedJson);
            var p = Gemini(h, new Clock());

            string id = await p.PlaceOrderAsync(new TradeSignal("BTCUSD", OrderSide.Buy, 0.123456789,
                OrderType.Limit, Price: 95000.555));

            Assert.Equal("106817811", id);
            var sent = Assert.Single(SentOrders(h));
            Assert.Equal("0.12345679", sent["amount"]!.ToString());   // the old eight decimals
            Assert.Equal("95000.555", sent["price"]!.ToString());
        }

        [Fact]
        public async Task The_rules_are_read_once_per_market_and_again_after_six_hours()
        {
            var clock = new Clock();
            var h = Venue();
            var p = Gemini(h, clock);

            await p.PlaceOrderAsync(new TradeSignal("BTCUSD", OrderSide.Buy, 0.01, OrderType.Limit, Price: 95000));
            clock.Step(TimeSpan.FromHours(5));
            await p.PlaceOrderAsync(new TradeSignal("BTCUSD", OrderSide.Buy, 0.01, OrderType.Limit, Price: 95000));
            Assert.Equal(1, Calls(h, "/v1/symbols/details/btcusd"));

            clock.Step(TimeSpan.FromHours(1.5));
            await p.PlaceOrderAsync(new TradeSignal("BTCUSD", OrderSide.Buy, 0.01, OrderType.Limit, Price: 95000));
            Assert.Equal(2, Calls(h, "/v1/symbols/details/btcusd"));
        }
    }
}
