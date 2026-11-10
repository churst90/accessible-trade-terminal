using System.Net;
using System.Reflection;
using System.Text.Json;
using AccessibleTrader.Plugins.Binance;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Binance's own rules, checked before an order leaves: the market's minimums, steps and
    /// tick (exchangeInfo), the leverage an opening futures order is placed at (set every
    /// time, never cut), and account reads that keep up without spending the request weight.
    ///
    /// <para>
    /// The exchangeInfo fixtures are Binance's own answers read on 2026-11-10, trimmed to the
    /// fields the provider reads: spot BTCUSDT from data-api.binance.vision (api.binance.com
    /// answers 451 from here) and futures BTCUSDT and EOSUSDT from
    /// testnet.binancefuture.com (fapi.binance.com answers 451 too). The testnet's numbers are
    /// not mainnet's (mainnet BTCUSDT futures is a 0.001 step and a 100 USDT minimum, from
    /// memory), but the shape is the same. The leverage-bracket and position answers are
    /// shaped from Binance's documented responses; they need keys to read live. None of this
    /// shows Binance accepting an order — that needs a funded account.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class BinanceVenueRulesTests
    {
        // GET /api/v3/exchangeInfo?symbol=BTCUSDT, data-api.binance.vision, 2026-11-10.
        internal const string SpotExchangeInfo = """
        {"timezone":"UTC","serverTime":1762740000000,
         "rateLimits":[{"rateLimitType":"REQUEST_WEIGHT","interval":"MINUTE","intervalNum":1,"limit":6000},
                       {"rateLimitType":"ORDERS","interval":"SECOND","intervalNum":10,"limit":100},
                       {"rateLimitType":"ORDERS","interval":"DAY","intervalNum":1,"limit":200000}],
         "symbols":[{"symbol":"BTCUSDT","status":"TRADING","baseAsset":"BTC","quoteAsset":"USDT",
           "filters":[
             {"filterType":"PRICE_FILTER","minPrice":"0.01000000","maxPrice":"1000000.00000000","tickSize":"0.01000000"},
             {"filterType":"LOT_SIZE","minQty":"0.00001000","maxQty":"9000.00000000","stepSize":"0.00001000"},
             {"filterType":"MARKET_LOT_SIZE","minQty":"0.00000000","maxQty":"146.22046811","stepSize":"0.00000000"},
             {"filterType":"NOTIONAL","minNotional":"5.00000000","applyMinToMarket":true,"maxNotional":"9000000.00000000","applyMaxToMarket":false,"avgPriceMins":5}]}]}
        """;

        // GET /fapi/v1/exchangeInfo, testnet.binancefuture.com, 2026-11-10 (two of 743 markets).
        internal const string FuturesExchangeInfo = """
        {"timezone":"UTC","serverTime":1762740000000,
         "symbols":[
          {"symbol":"BTCUSDT","status":"TRADING","contractType":"PERPETUAL","baseAsset":"BTC","quoteAsset":"USDT",
           "filters":[
             {"minPrice":"261.10","tickSize":"0.10","filterType":"PRICE_FILTER","maxPrice":"809484"},
             {"maxQty":"1000","minQty":"0.0001","filterType":"LOT_SIZE","stepSize":"0.0001"},
             {"stepSize":"0.0001","maxQty":"120","filterType":"MARKET_LOT_SIZE","minQty":"0.0001"},
             {"filterType":"MIN_NOTIONAL","notional":"50"}]},
          {"symbol":"EOSUSDT","status":"SETTLING","contractType":"PERPETUAL","baseAsset":"EOS","quoteAsset":"USDT",
           "filters":[
             {"minPrice":"0.021","filterType":"PRICE_FILTER","maxPrice":"36.061","tickSize":"0.001"},
             {"filterType":"LOT_SIZE","minQty":"0.1","stepSize":"0.1","maxQty":"1000000"},
             {"maxQty":"10000000","stepSize":"0.1","filterType":"MARKET_LOT_SIZE","minQty":"0.1"},
             {"notional":"5","filterType":"MIN_NOTIONAL"}]}]}
        """;

        // GET /fapi/v1/leverageBracket (signed, no symbol): Binance's documented shape.
        private const string Brackets = """
        [{"symbol":"BTCUSDT","notionalCoef":1.0,"brackets":[
            {"bracket":1,"initialLeverage":125,"notionalCap":50000,"notionalFloor":0,"maintMarginRatio":0.004,"cum":0},
            {"bracket":2,"initialLeverage":100,"notionalCap":600000,"notionalFloor":50000,"maintMarginRatio":0.005,"cum":50}]},
         {"symbol":"EOSUSDT","brackets":[{"bracket":1,"initialLeverage":50,"notionalCap":5000,"notionalFloor":0,"maintMarginRatio":0.01,"cum":0}]}]
        """;

        private static string Position(string symbol, string amt, string leverage) => $$"""
        [{"symbol":"{{symbol}}","positionAmt":"{{amt}}","entryPrice":"100000.0","markPrice":"100500.0",
          "unRealizedProfit":"5.0","liquidationPrice":"90000","leverage":"{{leverage}}","marginType":"cross","positionSide":"BOTH"}]
        """;

        private static FakeHttpMessageHandler Handler()
        {
            var h = new FakeHttpMessageHandler { StrictMode = true };
            h.Get(@"/api/v3/time", """{"serverTime":1762740000000}""");
            return h;
        }

        private static BinanceProvider Provider(FakeHttpMessageHandler h)
        {
            var p = new BinanceProvider();
            p.Configure(SymbolListHarness.Credentials());
            SymbolListHarness.SwapEveryHttpClient(p, h);
            return p;
        }

        /// <summary>A futures handler with the venue's rules, brackets and a flat account,
        /// and a leverage change the venue applies as asked.</summary>
        private static FakeHttpMessageHandler Futures(string positions = "[]")
        {
            var h = Handler();
            h.Get(@"/fapi/v1/exchangeInfo", FuturesExchangeInfo);
            h.Get(@"/fapi/v1/leverageBracket\?", Brackets);
            h.Get(@"/fapi/v2/positionRisk\?", positions);
            h.Add(HttpMethod.Post, @"/fapi/v1/leverage\?", EchoLeverage);
            h.Post(@"/fapi/v1/order\?", """{"orderId":777,"status":"NEW"}""");
            return h;
        }

        private static HttpResponseMessage EchoLeverage(HttpRequestMessage req)
        {
            var q = QueryOf(req);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"leverage":{{q["leverage"]}},"maxNotionalValue":"1000000","symbol":"{{q["symbol"]}}"}"""),
            };
        }

        private static Dictionary<string, string> QueryOf(HttpRequestMessage r) =>
            r.RequestUri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");

        private static List<Dictionary<string, string>> Sent(FakeHttpMessageHandler h, HttpMethod method, string pathEnd) =>
            h.Captured.Where(r => r.Method == method && r.RequestUri!.AbsolutePath.EndsWith(pathEnd, StringComparison.Ordinal))
                .Select(QueryOf).ToList();

        private static int Count(FakeHttpMessageHandler h, string pathEnd) =>
            h.Captured.Count(r => r.RequestUri!.AbsolutePath.EndsWith(pathEnd, StringComparison.Ordinal));

        private static TradeSignal Fut(OrderSide side, double qty, OrderType type = OrderType.Market,
            double? price = null, double? leverage = null, bool reduceOnly = false,
            double? tp = null, double? sl = null) =>
            new TradeSignal("BTC/USDT", side, qty, type, Price: price, Leverage: leverage,
                TakeProfit: tp, StopLoss: sl) { SubType = "Futures", ReduceOnly = reduceOnly };

        // ── Reading the rules ────────────────────────────────────────────────

        [Fact]
        public void Spot_rules_are_read_from_the_live_answer()
        {
            using var doc = JsonDocument.Parse(SpotExchangeInfo);
            var r = BinanceOrderRules.Parse(doc.RootElement, futures: false)["BTCUSDT"];

            Assert.Equal(0.00001m, r.Orders.MinQuantity);
            Assert.Equal(0.00001m, r.Orders.QuantityStep);
            Assert.Equal(0.01m, r.Orders.PriceTick);
            Assert.Equal(5m, r.Orders.MinNotional);
            Assert.Null(r.Orders.NotTrading);
            Assert.Equal("BTC/USDT", r.Orders.Symbol);
            // Spot's MARKET_LOT_SIZE says 0 and 0 ("none"): a market order keeps LOT_SIZE's
            // step rather than losing it.
            Assert.Equal(0.00001m, r.MarketOrders.QuantityStep);
            Assert.Equal(0.00001m, r.MarketOrders.MinQuantity);
        }

        [Fact]
        public void Futures_rules_read_the_notional_key_and_the_status()
        {
            using var doc = JsonDocument.Parse(FuturesExchangeInfo);
            var map = BinanceOrderRules.Parse(doc.RootElement, futures: true);

            var btc = map["BTCUSDT"];
            Assert.Equal(0.1m, btc.Orders.PriceTick);
            Assert.Equal(0.0001m, btc.Orders.QuantityStep);
            Assert.Equal(50m, btc.Orders.MinNotional);
            Assert.Equal("BTC/USDT futures", btc.Orders.Symbol);
            Assert.Contains("SETTLING", map["EOSUSDT"].Orders.NotTrading);
        }

        [Fact]
        public void Leverage_limit_is_the_highest_bracket_in_either_shape()
        {
            using var many = JsonDocument.Parse(Brackets);
            Assert.Equal(125, BinanceOrderRules.ParseLeverageBrackets(many.RootElement)["BTCUSDT"]);
            Assert.Equal(50, BinanceOrderRules.ParseLeverageBrackets(many.RootElement)["EOSUSDT"]);

            using var one = JsonDocument.Parse("""{"symbol":"ETHUSDT","brackets":[{"bracket":1,"initialLeverage":75}]}""");
            Assert.Equal(75, BinanceOrderRules.ParseLeverageBrackets(one.RootElement)["ETHUSDT"]);
        }

        // ── Spot orders ──────────────────────────────────────────────────────

        private static FakeHttpMessageHandler Spot()
        {
            var h = Handler();
            h.Get(@"/api/v3/exchangeInfo\?symbol=BTCUSDT", SpotExchangeInfo);
            h.Post(@"/api/v3/order\?", """{"orderId":555,"status":"NEW"}""");
            return h;
        }

        [Fact]
        public async Task Spot_order_below_the_smallest_quantity_is_refused_in_words()
        {
            var h = Spot();
            string r = await Provider(h).PlaceOrderAsync(new TradeSignal("BTC/USDT", OrderSide.Buy, 0.000004, OrderType.Limit, Price: 100000));

            Assert.StartsWith("ORDER_FAILED:", r);
            Assert.Contains("smallest BTC/USDT order is 0.00001 BTC", r);
            Assert.Empty(Sent(h, HttpMethod.Post, "/api/v3/order"));
        }

        [Fact]
        public async Task Spot_order_below_the_smallest_value_is_refused_in_words()
        {
            var h = Spot();
            // 0.00004 BTC at 100,000 is 4 USDT; the minimum is 5.
            string r = await Provider(h).PlaceOrderAsync(new TradeSignal("BTC/USDT", OrderSide.Buy, 0.00004, OrderType.Limit, Price: 100000));

            Assert.Contains("worth 5 USDT", r);
            Assert.Contains("worth 4 USDT", r);
            Assert.Empty(Sent(h, HttpMethod.Post, "/api/v3/order"));
        }

        [Fact]
        public async Task Spot_quantity_is_cut_to_the_step_and_prices_to_the_tick()
        {
            var h = Spot();
            await Provider(h).PlaceOrderAsync(new TradeSignal("BTC/USDT", OrderSide.Sell, 0.123456789,
                OrderType.StopLimit, Price: 95000.123, TriggerPrice: 95100.006));

            var q = Assert.Single(Sent(h, HttpMethod.Post, "/api/v3/order"));
            Assert.Equal("0.12345", q["quantity"]);
            Assert.Equal("95000.12", q["price"]);
            Assert.Equal("95100.01", q["stopPrice"]);
        }

        [Fact]
        public async Task Spot_order_whose_rules_cannot_be_read_goes_out_as_before()
        {
            var h = Handler();
            h.Get(@"/api/v3/exchangeInfo", """{"code":-1003,"msg":"Too much request weight used"}""", HttpStatusCode.BadRequest);
            h.Post(@"/api/v3/order\?", """{"orderId":556,"status":"NEW"}""");

            string id = await Provider(h).PlaceOrderAsync(new TradeSignal("BTC/USDT", OrderSide.Buy, 0.000004, OrderType.Limit, Price: 100000));

            Assert.Equal("556", id);
            Assert.Equal("0.000004", Assert.Single(Sent(h, HttpMethod.Post, "/api/v3/order"))["quantity"]);
        }

        [Fact]
        public async Task Spot_one_cancels_other_is_judged_and_formatted_too()
        {
            var h = Spot();
            h.Post(@"/api/v3/orderList/oco\?", """{"orderListId":9,"orders":[]}""");
            var p = Provider(h);

            string refused = await p.PlaceOcoPairAsync("BTC/USDT", OrderSide.Sell, 0.00004, 110000, 100000);
            Assert.Contains("worth 5 USDT", refused);
            Assert.Empty(Sent(h, HttpMethod.Post, "/api/v3/orderList/oco"));

            await p.PlaceOcoPairAsync("BTC/USDT", OrderSide.Sell, 0.123456789, 110000.004, 95000.006);
            var q = Assert.Single(Sent(h, HttpMethod.Post, "/api/v3/orderList/oco"));
            Assert.Equal("0.12345", q["quantity"]);
            Assert.Equal("110000", q["abovePrice"]);
            Assert.Equal("95000.01", q["belowStopPrice"]);
        }

        // ── Futures orders ───────────────────────────────────────────────────

        [Fact]
        public async Task Futures_order_on_a_market_not_trading_is_refused_in_words()
        {
            var h = Futures();
            string r = await Provider(h).PlaceOrderAsync(
                new TradeSignal("EOS/USDT", OrderSide.Buy, 100, OrderType.Limit, Price: 1) { SubType = "Futures" });

            Assert.Contains("not taking EOS/USDT futures orders", r);
            Assert.Contains("SETTLING", r);
            Assert.Empty(Sent(h, HttpMethod.Post, "/fapi/v1/order"));
            Assert.Empty(Sent(h, HttpMethod.Post, "/fapi/v1/leverage"));
        }

        [Fact]
        public async Task Futures_entry_and_its_take_profit_and_stop_are_formatted()
        {
            var h = Futures();
            await Provider(h).PlaceOrderAsync(Fut(OrderSide.Buy, 0.012345, OrderType.Limit, price: 100000.04,
                tp: 101000.06, sl: 99000.04));

            var orders = Sent(h, HttpMethod.Post, "/fapi/v1/order");
            Assert.Equal(3, orders.Count);
            Assert.All(orders, o => Assert.Equal("0.0123", o["quantity"]));
            Assert.Equal("100000", orders[0]["price"]);
            Assert.Equal("101000.1", orders.Single(o => o["type"] == "TAKE_PROFIT_MARKET")["stopPrice"]);
            Assert.Equal("99000", orders.Single(o => o["type"] == "STOP_MARKET")["stopPrice"]);
        }

        [Fact]
        public async Task Futures_close_worth_less_than_the_minimum_still_goes()
        {
            var h = Futures();
            // 0.0002 BTC at 100,000 is 20 USDT, under the 50 minimum — but it closes.
            string id = await Provider(h).PlaceOrderAsync(Fut(OrderSide.Sell, 0.0002, OrderType.Limit, price: 100000, reduceOnly: true));

            Assert.Equal("777", id);
        }

        // ── Futures leverage ─────────────────────────────────────────────────

        [Theory]
        [InlineData(null)]
        [InlineData(1.0)]
        [InlineData(0.5)]
        public async Task Order_at_no_leverage_sets_the_market_to_1(double? asked)
        {
            var h = Futures();
            string id = await Provider(h).PlaceOrderAsync(Fut(OrderSide.Buy, 0.001, leverage: asked));

            Assert.Equal("777", id);
            Assert.Equal("1", Assert.Single(Sent(h, HttpMethod.Post, "/fapi/v1/leverage"))["leverage"]);
            int set = h.Captured.FindIndex(r => r.RequestUri!.AbsolutePath.EndsWith("/fapi/v1/leverage"));
            int order = h.Captured.FindIndex(r => r.RequestUri!.AbsolutePath.EndsWith("/fapi/v1/order"));
            Assert.True(set < order, "the leverage must be set before the order goes");
        }

        [Fact]
        public async Task Fractional_leverage_is_refused_not_cut()
        {
            var h = Futures();
            string r = await Provider(h).PlaceOrderAsync(Fut(OrderSide.Buy, 0.001, leverage: 2.5));

            Assert.Contains("whole-number leverage only", r);
            Assert.Contains("2.5", r);
            Assert.Empty(Sent(h, HttpMethod.Post, "/fapi/v1/leverage"));
            Assert.Empty(Sent(h, HttpMethod.Post, "/fapi/v1/order"));
        }

        [Fact]
        public async Task Leverage_above_the_market_limit_is_refused_naming_the_limit()
        {
            var h = Futures();
            string r = await Provider(h).PlaceOrderAsync(Fut(OrderSide.Buy, 0.001, leverage: 150));

            Assert.Contains("at most 125 times on BTCUSDT", r);
            Assert.Empty(Sent(h, HttpMethod.Post, "/fapi/v1/leverage"));
            Assert.Empty(Sent(h, HttpMethod.Post, "/fapi/v1/order"));
        }

        [Fact]
        public async Task Without_the_limits_the_venue_judges_the_leverage_in_its_words()
        {
            var h = Handler();
            h.Get(@"/fapi/v1/exchangeInfo", FuturesExchangeInfo);
            h.Get(@"/fapi/v1/leverageBracket\?", """{"code":-2015,"msg":"Invalid API-key, IP, or permissions for action."}""", HttpStatusCode.Unauthorized);
            h.Get(@"/fapi/v2/positionRisk\?", "[]");
            h.Post(@"/fapi/v1/leverage\?", """{"code":-4028,"msg":"Leverage 150 is not valid"}""", HttpStatusCode.BadRequest);
            h.Post(@"/fapi/v1/order\?", """{"orderId":777}""");

            string r = await Provider(h).PlaceOrderAsync(Fut(OrderSide.Buy, 0.001, leverage: 150));

            Assert.Equal("150", Assert.Single(Sent(h, HttpMethod.Post, "/fapi/v1/leverage"))["leverage"]);
            Assert.Contains("Leverage 150 is not valid", r);
            Assert.Contains("nothing was placed", r);
            Assert.Empty(Sent(h, HttpMethod.Post, "/fapi/v1/order"));
        }

        [Fact]
        public async Task Venue_applying_other_leverage_refuses_the_order()
        {
            var h = Handler();
            h.Get(@"/fapi/v1/exchangeInfo", FuturesExchangeInfo);
            h.Get(@"/fapi/v1/leverageBracket\?", Brackets);
            h.Get(@"/fapi/v2/positionRisk\?", "[]");
            h.Post(@"/fapi/v1/leverage\?", """{"leverage":20,"maxNotionalValue":"1000000","symbol":"BTCUSDT"}""");
            h.Post(@"/fapi/v1/order\?", """{"orderId":777}""");

            string r = await Provider(h).PlaceOrderAsync(Fut(OrderSide.Buy, 0.001, leverage: 10));

            Assert.Contains("still at 20 times", r);
            Assert.Empty(Sent(h, HttpMethod.Post, "/fapi/v1/order"));
        }

        [Fact]
        public async Task Reduce_only_close_leaves_the_leverage_alone()
        {
            var h = Futures(Position("BTCUSDT", "0.010", "10"));
            string id = await Provider(h).PlaceOrderAsync(Fut(OrderSide.Sell, 0.01, reduceOnly: true));

            Assert.Equal("777", id);
            Assert.Empty(Sent(h, HttpMethod.Post, "/fapi/v1/leverage"));
        }

        [Fact]
        public async Task Order_at_other_leverage_than_an_open_position_is_refused()
        {
            var h = Futures(Position("BTCUSDT", "0.010", "10"));
            var p = Provider(h);

            string r = await p.PlaceOrderAsync(Fut(OrderSide.Buy, 0.001));
            Assert.Contains("already has a position open at 10 times", r);
            Assert.Empty(Sent(h, HttpMethod.Post, "/fapi/v1/leverage"));

            Assert.Equal("777", await p.PlaceOrderAsync(Fut(OrderSide.Buy, 0.001, leverage: 10)));
            Assert.Equal("10", Assert.Single(Sent(h, HttpMethod.Post, "/fapi/v1/leverage"))["leverage"]);
        }

        [Fact]
        public async Task Set_leverage_sends_the_amount_asked_and_refuses_a_fraction()
        {
            var h = Futures();
            var p = Provider(h);

            Assert.Equal(150.0, await p.SetLeverageAsync("BTC/USDT", 150));
            Assert.Equal("150", Assert.Single(Sent(h, HttpMethod.Post, "/fapi/v1/leverage"))["leverage"]);

            Assert.Equal(1.0, await p.SetLeverageAsync("BTC/USDT", 2.5));
            Assert.Single(Sent(h, HttpMethod.Post, "/fapi/v1/leverage"));
        }

        [Fact]
        public async Task Ticket_is_offered_the_market_own_leverage()
        {
            var h = Futures();
            var p = Provider(h);

            var btc = await p.GetAllowedLeverageAsync("BTC/USDT", OrderSide.Buy);
            Assert.Equal(Enumerable.Range(2, 124), btc!);
            Assert.Equal(50, (await p.GetAllowedLeverageAsync("EOSUSDT", OrderSide.Sell))!.Max());
            Assert.Empty((await p.GetAllowedLeverageAsync("KAS/USDT", OrderSide.Buy))!);
            Assert.Equal(1, Count(h, "/fapi/v1/leverageBracket"));
        }

        [Fact]
        public async Task Ticket_falls_back_when_the_limits_cannot_be_read()
        {
            var h = Handler();
            h.Get(@"/fapi/v1/leverageBracket\?", """{"code":-2015,"msg":"Invalid API-key, IP, or permissions for action."}""", HttpStatusCode.Unauthorized);

            Assert.Null(await Provider(h).GetAllowedLeverageAsync("BTC/USDT", OrderSide.Buy));
        }

        // ── Account reads ────────────────────────────────────────────────────

        [Fact]
        public async Task Account_reads_are_shared_until_an_order_cancel_or_account_frame()
        {
            var h = Futures(Position("BTCUSDT", "0.010", "1"));
            h.Add(HttpMethod.Delete, @"/api/v3/order\?", """{"orderId":1}""");
            var p = Provider(h);

            await p.GetPositionsAsync();
            await p.GetPositionsAsync();
            Assert.Equal(1, Count(h, "/fapi/v2/positionRisk"));

            await p.PlaceOrderAsync(Fut(OrderSide.Buy, 0.001));
            int afterOrder = Count(h, "/fapi/v2/positionRisk");
            await p.GetPositionsAsync();
            Assert.Equal(afterOrder + 1, Count(h, "/fapi/v2/positionRisk"));

            Assert.True(await p.CancelOrderAsync("1", "BTC/USDT"));
            await p.GetPositionsAsync();
            Assert.Equal(afterOrder + 2, Count(h, "/fapi/v2/positionRisk"));

            typeof(BinanceProvider).GetMethod("HandleUserDataFrame", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(p, new object[] { """{"e":"ACCOUNT_UPDATE","a":{}}""", true });
            await p.GetPositionsAsync();
            Assert.Equal(afterOrder + 3, Count(h, "/fapi/v2/positionRisk"));
        }

        [Fact]
        public async Task Orders_do_not_wait_behind_the_reads_limiter()
        {
            var h = Spot();
            var p = Provider(h);
            Assert.NotNull(await p.GetOrderRulesAsync("BTCUSDT", futures: false));   // rules cached

            // The reads limiter, full for the next hour.
            var full = new RateLimiter(1, TimeSpan.FromHours(1));
            await full.WaitAsync();
            typeof(BinanceProvider).GetField("_rateLimiter", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(p, full);

            var place = p.PlaceOrderAsync(new TradeSignal("BTC/USDT", OrderSide.Buy, 0.001, OrderType.Limit, Price: 100000));
            Assert.Same(place, await Task.WhenAny(place, Task.Delay(TimeSpan.FromSeconds(3))));
            Assert.Equal("555", await place);
        }
    }
}
