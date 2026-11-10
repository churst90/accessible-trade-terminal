using System.Reflection;
using AccessibleTrader.Plugins.Mexc;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// MEXC's own rules, checked before an order is signed: each market's smallest quantity,
    /// decimals, tick and smallest value, and each futures contract's leverage ceiling.
    ///
    /// <para>
    /// The fixtures are TRIMMED COPIES OF LIVE ANSWERS read on 2026-11-10 from
    /// <c>api.mexc.com/api/v3/exchangeInfo?symbol=BTCUSDT</c> and
    /// <c>contract.mexc.com/api/v1/contract/detail?symbol=BTC_USDT</c> (and the real answer for
    /// a contract that does not exist). What they cannot show is MEXC accepting the orders —
    /// and MEXC's contract API marks order placement "Under maintenance" for most accounts.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class MexcVenueRulesTests
    {
        internal const string SpotBtc = """
        {"timezone":"CST","serverTime":1794302955265,"rateLimits":[],"exchangeFilters":[],"symbols":[
          {"symbol":"BTCUSDT","status":"1","baseAsset":"BTC","baseAssetPrecision":8,"quoteAsset":"USDT","quotePrecision":2,
           "quoteAssetPrecision":2,"orderTypes":["LIMIT","MARKET","LIMIT_MAKER"],"isSpotTradingAllowed":true,
           "isMarginTradingAllowed":false,"quoteAmountPrecision":"1","baseSizePrecision":"0.000001","permissions":["SPOT"],
           "maxQuoteAmount":"4000000","quoteAmountPrecisionMarket":"1","maxQuoteAmountMarket":"4000000","tradeSideType":1,"st":false}]}
        """;

        internal const string FuturesBtc = """
        {"success":true,"code":0,"data":{"symbol":"BTC_USDT","displayNameEn":"BTC_USDT PERPETUAL","positionOpenType":3,
          "baseCoin":"BTC","quoteCoin":"USDT","settleCoin":"USDT","contractSize":0.0001,"minLeverage":1,"maxLeverage":500,
          "priceScale":1,"volScale":0,"amountScale":4,"priceUnit":0.1,"volUnit":1,"minVol":1,"maxVol":400000,
          "state":0,"apiAllowed":true,"isHidden":false}}
        """;

        internal const string NoContract = """{"success":false,"code":1001,"message":"Contract does not exist"}""";

        private const string Ok = """{"success":true,"code":0}""";

        private static MexcProvider Mexc(FakeHttpMessageHandler h)
        {
            var p = new MexcProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "mk", ["ApiSecret"] = "ms" });
            Swap(p, "_rest", new MexcRestApi(new HttpClient(h)));
            Swap(p, "_connected", true);
            h.Get(@"api\.mexc\.com/api/v3/time", """{"serverTime":1700000000000}""");
            return p;
        }

        private static void Swap(object target, string field, object? value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

        private static FakeHttpMessageHandler Armed(string spot = SpotBtc, string futures = FuturesBtc)
        {
            var h = new FakeHttpMessageHandler();
            h.Get(@"api\.mexc\.com/api/v3/exchangeInfo\?symbol=", spot);
            h.Get(@"contract\.mexc\.com/api/v1/contract/detail\?symbol=", futures);
            h.Post(@"api\.mexc\.com/api/v3/order\?", """{"orderId":"S1"}""");
            h.Post(@"contract\.mexc\.com/api/v1/private/order/submit", """{"success":true,"code":0,"data":"F1"}""");
            return h;
        }

        private static IEnumerable<(HttpRequestMessage Req, string Body)> Calls(FakeHttpMessageHandler h, string path) =>
            h.Captured.Zip(h.CapturedBodies).Where(x => x.First.RequestUri!.AbsolutePath.EndsWith(path, StringComparison.Ordinal));

        private static Dictionary<string, string> SpotOrderQuery(FakeHttpMessageHandler h) =>
            Calls(h, "/api/v3/order").Single().Req.RequestUri!.Query.TrimStart('?').Split('&')
                .Select(kv => kv.Split('=', 2)).ToDictionary(kv => kv[0], kv => Uri.UnescapeDataString(kv[1]));

        private static JObject FuturesOrder(FakeHttpMessageHandler h) =>
            JObject.Parse(Calls(h, "/api/v1/private/order/submit").Single().Body);

        // ── Reading the rules ──────────────────────────────────────────────────

        [Fact]
        public void Spot_rules_come_from_exchangeInfo_as_MEXC_documents_them()
        {
            var r = MexcProvider.ParseSpotRules(SpotBtc, "BTCUSDT").Order!;
            Assert.Equal("BTC/USDT", r.Symbol);
            Assert.Equal(0.000001m, r.MinQuantity);    // baseSizePrecision = smallest quantity
            Assert.Equal(0.00000001m, r.QuantityStep); // baseAssetPrecision 8 decimals
            Assert.Equal(0.01m, r.PriceTick);          // quotePrecision 2 decimals
            Assert.Equal(1m, r.MinNotional);           // quoteAmountPrecision = smallest value
            Assert.Null(r.NotTrading);
        }

        [Fact]
        public void Futures_rules_come_from_contract_detail_in_contracts()
        {
            var m = MexcProvider.ParseFuturesRules(FuturesBtc);
            Assert.Equal("BTC/USDT perpetual", m.Order!.Symbol);
            Assert.Equal("contracts", m.Order.QuantityUnit);
            Assert.Equal(1m, m.Order.MinQuantity);
            Assert.Equal(1m, m.Order.QuantityStep);
            Assert.Equal(0.1m, m.Order.PriceTick);
            Assert.Equal(500, m.MaxLeverage);
            Assert.Equal(" (one contract is 0.0001 BTC)", m.ContractNote);
            Assert.Null(MexcProvider.ParseFuturesRules(NoContract).Order);
        }

        [Theory]
        [InlineData("2", "MEXC has paused trading on BTC/USDT")]
        [InlineData("3", "MEXC has taken BTC/USDT offline")]
        public async Task A_paused_or_offline_spot_pair_is_refused_in_words_before_signing(string status, string words)
        {
            var h = Armed(spot: SpotBtc.Replace("\"status\":\"1\"", $"\"status\":\"{status}\""));
            string result = await Mexc(h).PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Buy, 0.01, OrderType.Limit, Price: 95000));
            Assert.Equal($"ORDER_FAILED:{words}", result);
            Assert.Empty(Calls(h, "/api/v3/order"));
        }

        [Theory]
        [InlineData(2, OrderSide.Sell, "MEXC takes only buy orders on BTC/USDT right now")]
        [InlineData(3, OrderSide.Buy, "MEXC takes only sell orders on BTC/USDT right now")]
        public async Task A_pair_open_to_one_side_refuses_the_other_in_words(int tradeSide, OrderSide side, string words)
        {
            var h = Armed(spot: SpotBtc.Replace("\"tradeSideType\":1", $"\"tradeSideType\":{tradeSide}"));
            string result = await Mexc(h).PlaceOrderAsync(new TradeSignal("BTCUSDT", side, 0.01, OrderType.Limit, Price: 95000));
            Assert.Equal($"ORDER_FAILED:{words}", result);
            Assert.Empty(Calls(h, "/api/v3/order"));
        }

        [Fact]
        public async Task A_paused_contract_is_refused_in_words_before_signing()
        {
            var h = Armed(futures: FuturesBtc.Replace("\"state\":0", "\"state\":4"));
            string result = await Mexc(h).PlaceOrderAsync(new TradeSignal("BTC_USDT", OrderSide.Buy, 5, OrderType.Market, SubType: "Futures"));
            Assert.StartsWith("ORDER_FAILED:MEXC has paused trading on BTC/USDT perpetual", result);
            Assert.Empty(Calls(h, "/order/submit"));
        }

        // ── Minimums and decimals: spot ────────────────────────────────────────

        [Fact]
        public async Task A_spot_quantity_under_the_minimum_is_refused_in_words()
        {
            var h = Armed();
            string result = await Mexc(h).PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Buy, 0.0000005, OrderType.Limit, Price: 95000));
            Assert.Equal("ORDER_FAILED:MEXC's smallest BTC/USDT order is 0.000001 BTC; this one is 0.0000005 BTC", result);
            Assert.Empty(Calls(h, "/api/v3/order"));
        }

        [Fact]
        public async Task A_spot_limit_worth_under_one_USDT_is_refused_in_words()
        {
            var h = Armed();
            string result = await Mexc(h).PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Buy, 0.00001, OrderType.Limit, Price: 50000));
            Assert.Equal("ORDER_FAILED:MEXC's smallest BTC/USDT order is worth 1 USDT; this one is worth 0.5 USDT", result);
            Assert.Empty(Calls(h, "/api/v3/order"));
        }

        [Fact]
        public async Task Spot_quantity_and_price_go_cut_to_the_pairs_decimals_and_never_in_exponent_form()
        {
            var h = Armed();
            await Mexc(h).PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Buy, 0.123456789, OrderType.Limit, Price: 95000.126));
            var q = SpotOrderQuery(h);
            Assert.Equal("0.12345678", q["quantity"]);   // cut DOWN to 8 decimals
            Assert.Equal("95000.13", q["price"]);         // nearest 0.01

            var h2 = Armed();
            await Mexc(h2).PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Buy, 0.00002, OrderType.Market));
            Assert.Equal("0.00002", SpotOrderQuery(h2)["quantity"]);   // ToString() said 2E-05
        }

        [Fact]
        public async Task Rules_that_cannot_be_read_leave_the_order_to_MEXC_and_still_send_plain_numbers()
        {
            var h = new FakeHttpMessageHandler();
            h.Add(HttpMethod.Get, @"api\.mexc\.com/api/v3/exchangeInfo", _ => new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
            h.Post(@"api\.mexc\.com/api/v3/order\?", """{"orderId":"S1"}""");
            string id = await Mexc(h).PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Buy, 0.00002, OrderType.Limit, Price: 95000.5));
            Assert.Equal("S1", id);
            Assert.Equal("0.00002", SpotOrderQuery(h)["quantity"]);
            Assert.Equal("95000.5", SpotOrderQuery(h)["price"]);
        }

        [Fact]
        public async Task The_rules_are_read_once_and_remembered_for_hours()
        {
            var h = Armed();
            var p = Mexc(h);
            await p.PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Buy, 0.01, OrderType.Limit, Price: 95000));
            await p.PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Sell, 0.01, OrderType.Limit, Price: 96000));
            Assert.Single(Calls(h, "/api/v3/exchangeInfo"));
            Assert.Equal(2, Calls(h, "/api/v3/order").Count());
            Assert.True(MexcProvider.MarketRulesTtl >= TimeSpan.FromHours(1));
        }

        // ── Minimums and decimals: futures ─────────────────────────────────────

        [Fact]
        public async Task A_fraction_of_a_contract_is_refused_in_words_naming_the_contract_size()
        {
            var h = Armed();
            string result = await Mexc(h).PlaceOrderAsync(new TradeSignal("BTC_USDT", OrderSide.Buy, 0.5, OrderType.Market, SubType: "Futures"));
            Assert.Equal("ORDER_FAILED:MEXC's smallest BTC/USDT perpetual order is 1 contracts; this one is 0.5 contracts"
                       + " (one contract is 0.0001 BTC)", result);
            Assert.Empty(Calls(h, "/order/submit"));
        }

        [Fact]
        public async Task Futures_vol_and_prices_go_cut_to_the_contracts_steps()
        {
            var h = Armed();
            await Mexc(h).PlaceOrderAsync(new TradeSignal("BTC_USDT", OrderSide.Buy, 3.7, OrderType.Limit,
                Price: 95000.07, StopLoss: 90000.04, TakeProfit: 105000.06, SubType: "Futures"));
            var o = FuturesOrder(h);
            string raw = o.ToString(Newtonsoft.Json.Formatting.None);
            Assert.Equal(3m, o["vol"]!.Value<decimal>());            // whole contracts, cut down
            Assert.Equal(95000.1m, o["price"]!.Value<decimal>());    // priceUnit 0.1
            Assert.Equal(90000m, o["stopLossPrice"]!.Value<decimal>());
            Assert.Equal(105000.1m, o["takeProfitPrice"]!.Value<decimal>());
            Assert.DoesNotContain("E-", raw);
        }

        // ── Leverage ───────────────────────────────────────────────────────────

        [Fact]
        public async Task An_opening_order_with_no_leverage_says_1_so_the_accounts_old_leverage_is_not_used()
        {
            var h = Armed();
            await Mexc(h).PlaceOrderAsync(new TradeSignal("BTC_USDT", OrderSide.Buy, 2, OrderType.Market, SubType: "Futures"));
            Assert.Equal(1, FuturesOrder(h)["leverage"]!.Value<int>());
        }

        [Fact]
        public async Task A_close_with_no_leverage_carries_none_it_takes_the_positions()
        {
            var h = Armed();
            await Mexc(h).PlaceOrderAsync(new TradeSignal("BTC_USDT", OrderSide.Sell, 2, OrderType.Market, SubType: "Futures", ReduceOnly: true));
            Assert.Null(FuturesOrder(h)["leverage"]);
        }

        [Fact]
        public async Task Leverage_above_the_old_fixed_200_is_sent_as_asked_when_the_contract_takes_it()
        {
            var h = Armed();
            await Mexc(h).PlaceOrderAsync(new TradeSignal("BTC_USDT", OrderSide.Buy, 2, OrderType.Market, SubType: "Futures", Leverage: 300));
            Assert.Equal(300, FuturesOrder(h)["leverage"]!.Value<int>());
        }

        [Theory]
        [InlineData(600, "ORDER_FAILED:MEXC's highest leverage on BTC/USDT perpetual is 500 times; this asks for 600")]
        [InlineData(2.5, "ORDER_FAILED:MEXC takes whole-number leverage; 2.5 times is not one")]
        public async Task Leverage_the_contract_cannot_take_is_refused_in_words_and_nothing_is_sent(double leverage, string words)
        {
            var h = Armed();
            string result = await Mexc(h).PlaceOrderAsync(new TradeSignal("BTC_USDT", OrderSide.Buy, 2, OrderType.Market, SubType: "Futures", Leverage: leverage));
            Assert.Equal(words, result);
            Assert.Empty(Calls(h, "/order/submit"));
            Assert.Empty(Calls(h, "/change_leverage"));
        }

        [Fact]
        public async Task A_cross_order_sets_the_cross_leverage_first_and_a_refusal_stops_it()
        {
            var h = Armed();
            h.Post(@"contract\.mexc\.com/api/v1/private/position/change_leverage", Ok);
            await Mexc(h).PlaceOrderAsync(new TradeSignal("BTC_USDT", OrderSide.Sell, 2, OrderType.Market, SubType: "Futures", Leverage: 7, MarginType: "Cross"));
            var change = JObject.Parse(Calls(h, "/change_leverage").Single().Body);
            Assert.Equal(7, change["leverage"]!.Value<int>());
            Assert.Equal(2, change["openType"]!.Value<int>());       // cross
            Assert.Equal(2, change["positionType"]!.Value<int>());   // a sell opens a short
            Assert.Equal(2, FuturesOrder(h)["openType"]!.Value<int>());

            var no = Armed();
            no.Post(@"contract\.mexc\.com/api/v1/private/position/change_leverage", """{"success":false,"code":600,"message":"leverage error"}""");
            string result = await Mexc(no).PlaceOrderAsync(new TradeSignal("BTC_USDT", OrderSide.Buy, 2, OrderType.Market, SubType: "Futures", Leverage: 7, MarginType: "Cross"));
            Assert.StartsWith("ORDER_FAILED:MEXC did not set 7 times cross leverage on BTC_USDT", result);
            Assert.Contains("leverage error", result);
            Assert.Empty(Calls(no, "/order/submit"));
        }

        [Fact]
        public async Task SetLeverage_uses_the_margin_mode_the_orders_use_on_both_sides()
        {
            var h = Armed();
            h.Post(@"contract\.mexc\.com/api/v1/private/position/change_leverage", Ok);
            var p = Mexc(h);

            Assert.Equal(10.0, await p.SetLeverageAsync("BTC_USDT", 10));
            var first = Calls(h, "/change_leverage").Select(c => JObject.Parse(c.Body)).ToList();
            Assert.Equal(new[] { 1, 2 }, first.Select(b => b["positionType"]!.Value<int>()));
            Assert.All(first, b => Assert.Equal(1, b["openType"]!.Value<int>()));   // isolated, as an order with no margin type

            await p.PlaceOrderAsync(new TradeSignal("BTC_USDT", OrderSide.Buy, 2, OrderType.Market, SubType: "Futures", MarginType: "Cross"));
            int before = Calls(h, "/change_leverage").Count();
            Assert.Equal(12.0, await p.SetLeverageAsync("BTC_USDT", 12));
            var after = Calls(h, "/change_leverage").Skip(before).Select(c => JObject.Parse(c.Body)).ToList();
            Assert.All(after, b => Assert.Equal(2, b["openType"]!.Value<int>()));    // cross, as the last order
        }

        [Fact]
        public async Task SetLeverage_past_the_contracts_ceiling_is_refused_not_clamped()
        {
            var h = Armed();
            h.Post(@"contract\.mexc\.com/api/v1/private/position/change_leverage", Ok);
            var p = Mexc(h);
            var errors = new List<string>();
            p.ErrorStream.Subscribe(errors.Add);

            Assert.Equal(1.0, await p.SetLeverageAsync("BTC_USDT", 501));
            Assert.Empty(Calls(h, "/change_leverage"));
            Assert.Contains(errors, e => e.Contains("highest leverage on BTC/USDT perpetual is 500 times"));
        }

        [Fact]
        public async Task The_leverage_offered_is_the_contracts_own_whatever_the_spelling()
        {
            var p = Mexc(Armed());
            var list = await p.GetAllowedLeverageAsync("BTC_USDT", OrderSide.Buy);
            Assert.Equal(Enumerable.Range(2, 499), list);
            Assert.Equal(list, await p.GetAllowedLeverageAsync("BTCUSDT", OrderSide.Sell));

            Assert.Empty((await Mexc(Armed(futures: NoContract)).GetAllowedLeverageAsync("NOPEUSDT", OrderSide.Buy))!);

            var down = new FakeHttpMessageHandler();
            down.Add(HttpMethod.Get, @"contract\.mexc\.com", _ => new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway));
            Assert.Null(await Mexc(down).GetAllowedLeverageAsync("BTC_USDT", OrderSide.Buy));
        }
    }
}
