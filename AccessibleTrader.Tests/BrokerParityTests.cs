using System.Net;
using System.Reflection;
using Newtonsoft.Json.Linq;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The 2026-07-22 broker-parity closure. The audit found Tradier and Schwab
    /// SILENTLY DROPPED stop-loss/take-profit on entries, Kraken could attach
    /// only one protective leg without telling anyone, and fill history existed
    /// on Binance alone. These tests pin the exchange-native bracket payloads
    /// and the fill parsing for every broker that gained them.
    /// </summary>
    // Same collection as ProviderFetchOhlcvTests: that class installs a fake
    // into the GLOBAL PluginHostServices.ApiKeys bridge, which preempts the
    // Configure()-supplied credentials these signed-path tests rely on. xUnit
    // runs classes in parallel by default; sharing a collection serializes the
    // two so the bridge can never be swapped mid-test. (Exposed 2026-07-22 as
    // a rare flake when new test classes shifted the schedule.)
    [Collection("ProviderCredentialBridge")]
    public class BrokerParityTests
    {
        private static void Swap(object provider, FakeHttpMessageHandler handler)
        {
            // Swap the REST client BY NAME, never "the first HttpClient field":
            // CLR field order is not guaranteed, and Tradier really does carry a
            // second client (`_streamClient`, long-poll order events) — under a
            // .First() that happened to enumerate it first, these tests would
            // fake the stream client and let PlaceOrderAsync make a REAL network
            // call, invisible to FakeHttpMessageHandler.StrictMode because that
            // client is not wired to the fake at all. The name set is the two
            // spellings the broker plugins use for their signed REST client.
            var candidates = provider.GetType()
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(HttpClient)
                         && f.Name is "_httpClient" or "_http")
                .ToList();
            var target = Assert.Single(candidates);
            target.SetValue(provider, new HttpClient(handler));
        }

        // ── Tradier brackets (class=oto / otoco, indexed legs) ───────────────

        private static AccessibleTrader.Plugins.Tradier.TradierProvider Tradier(FakeHttpMessageHandler h)
        {
            var p = new AccessibleTrader.Plugins.Tradier.TradierProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "tok", ["AccountId"] = "ACC1" });
            Swap(p, h);
            return p;
        }

        private static async Task<string> Form(HttpRequestMessage req) =>
            req.Content == null ? "" : await req.Content.ReadAsStringAsync();

        [Fact]
        public async Task Tradier_entry_with_both_legs_is_one_native_OTOCO()
        {
            var h = new FakeHttpMessageHandler().Post(@"/accounts/ACC1/orders", """{"order":{"id":42,"status":"ok"}}""");
            var p = Tradier(h);

            string id = await p.PlaceOrderAsync(new TradeSignal("AAPL", OrderSide.Buy, 10,
                OrderType.Market, StopLoss: 180, TakeProfit: 220));

            Assert.Equal("42", id);
            string form = Uri.UnescapeDataString(await Form(h.Captured.Single()));
            Assert.Contains("class=otoco", form);
            Assert.Contains("type[0]=market", form);
            Assert.Contains("side[0]=buy", form);
            Assert.Contains("type[1]=limit", form);   // TP leg
            Assert.Contains("price[1]=220", form);
            Assert.Contains("side[1]=sell", form);
            Assert.Contains("type[2]=stop", form);    // SL leg
            Assert.Contains("stop[2]=180", form);
        }

        [Fact]
        public async Task Tradier_entry_with_stop_only_is_an_OTO()
        {
            var h = new FakeHttpMessageHandler().Post(@"/accounts/ACC1/orders", """{"order":{"id":7}}""");
            var p = Tradier(h);

            await p.PlaceOrderAsync(new TradeSignal("AAPL", OrderSide.Buy, 10, OrderType.Market, StopLoss: 180));

            string form = Uri.UnescapeDataString(await Form(h.Captured.Single()));
            Assert.Contains("class=oto", form);
            Assert.Contains("type[1]=stop", form);
            Assert.Contains("stop[1]=180", form);
            Assert.DoesNotContain("type[2]", form); // exactly two legs
        }

        [Fact]
        public async Task Tradier_standalone_take_profit_is_a_resting_limit()
        {
            // Was "ORDER_FAILED:Unsupported order type" before the parity pass.
            var h = new FakeHttpMessageHandler().Post(@"/accounts/ACC1/orders", """{"order":{"id":9}}""");
            var p = Tradier(h);

            string id = await p.PlaceOrderAsync(new TradeSignal("AAPL", OrderSide.Sell, 10,
                OrderType.TakeProfitMarket, TriggerPrice: 250));

            Assert.Equal("9", id);
            string form = Uri.UnescapeDataString(await Form(h.Captured.Single()));
            Assert.Contains("type=limit", form);
            Assert.Contains("price=250", form);
        }

        [Fact]
        public async Task Tradier_open_orders_surface_otoco_legs_and_stop_prices()
        {
            // The 2026-07-22 voice-completeness pass: protective legs used to be
            // invisible in the Orders tab, and resting stops displayed price 0.
            var h = new FakeHttpMessageHandler().Get(@"/accounts/ACC1/orders", """
                {"orders":{"order":{
                    "id":100,"symbol":"AAPL","side":"buy","type":"market","quantity":10,
                    "status":"open","class":"otoco",
                    "leg":[
                        {"id":101,"symbol":"AAPL","side":"sell","type":"limit","quantity":10,"price":220.0,"status":"open"},
                        {"id":102,"symbol":"AAPL","side":"sell","type":"stop","quantity":10,"stop_price":180.0,"status":"open"},
                        {"id":103,"symbol":"AAPL","side":"sell","type":"stop","quantity":10,"stop_price":170.0,"status":"canceled"}
                    ]}}}
                """);
            var p = Tradier(h);

            var orders = await p.GetOpenOrdersAsync();

            Assert.Equal(3, orders.Count); // entry + TP leg + SL leg; cancelled leg excluded
            Assert.Equal("101", orders[1].Id);
            Assert.Equal(220.0, orders[1].Price);
            Assert.Equal("102", orders[2].Id);
            Assert.Equal(180.0, orders[2].Price); // stop_price fallback
            Assert.Equal(OrderSide.Sell, orders[2].Side);
        }

        [Fact]
        public async Task Tradier_fills_parse_history_events_including_the_single_object_form()
        {
            var h = new FakeHttpMessageHandler().Get(@"/accounts/ACC1/history", """
                {"history":{"event":{"date":"2026-07-20T14:30:00Z","type":"trade",
                    "trade":{"commission":0.35,"price":231.5,"quantity":-10,"symbol":"AAPL","trade_type":"sell"}}}}
                """);
            var p = Tradier(h);

            var fills = await p.GetFillsAsync();

            var f = Assert.Single(fills);
            Assert.Equal("AAPL", f.Symbol);
            Assert.Equal(OrderSide.Sell, f.Side); // negative quantity = sell
            Assert.Equal(10, f.Quantity);
            Assert.Equal(231.5, f.Price);
            Assert.Equal(0.35, f.Fee);
        }

        // ── Schwab brackets (TRIGGER entry + OCO child tree) ─────────────────

        [Fact]
        public void Schwab_entry_with_both_legs_builds_a_TRIGGER_with_an_OCO_child()
        {
            var order = AccessibleTrader.Plugins.Schwab.SchwabProvider.BuildSchwabOrderForTest(
                new TradeSignal("AAPL", OrderSide.Buy, 10, OrderType.Limit, Price: 200,
                    StopLoss: 180, TakeProfit: 220))!;

            Assert.Equal("TRIGGER", order.OrderStrategyType);
            Assert.Equal("GTC", order.Duration); // protection outlives the session
            var oco = Assert.Single(order.ChildOrderStrategies!);
            Assert.Equal("OCO", oco.OrderStrategyType);
            Assert.Null(oco.OrderType);           // wrapper node carries no order fields
            Assert.Null(oco.OrderLegCollection);
            Assert.Equal(2, oco.ChildOrderStrategies!.Count);

            var tp = oco.ChildOrderStrategies[0];
            Assert.Equal("LIMIT", tp.OrderType);
            Assert.Equal("220", tp.Price);
            Assert.Equal("SELL", tp.OrderLegCollection![0].Instruction);

            var sl = oco.ChildOrderStrategies[1];
            Assert.Equal("STOP", sl.OrderType);
            Assert.Equal("180", sl.StopPrice);
        }

        [Fact]
        public void Schwab_entry_with_one_leg_attaches_a_single_child()
        {
            var order = AccessibleTrader.Plugins.Schwab.SchwabProvider.BuildSchwabOrderForTest(
                new TradeSignal("AAPL", OrderSide.Buy, 10, OrderType.Market, StopLoss: 180))!;

            Assert.Equal("TRIGGER", order.OrderStrategyType);
            var child = Assert.Single(order.ChildOrderStrategies!);
            Assert.Equal("SINGLE", child.OrderStrategyType);
            Assert.Equal("STOP", child.OrderType);
        }

        [Fact]
        public void Schwab_plain_order_payload_is_unchanged()
        {
            var order = AccessibleTrader.Plugins.Schwab.SchwabProvider.BuildSchwabOrderForTest(
                new TradeSignal("AAPL", OrderSide.Buy, 10, OrderType.Market))!;

            Assert.Equal("SINGLE", order.OrderStrategyType);
            Assert.Equal("MARKET", order.OrderType);
            Assert.Null(order.ChildOrderStrategies); // absent, not empty — payload identical to pre-bracket
        }

        [Fact]
        public void Schwab_open_orders_walk_bracket_trees_including_pending_children()
        {
            var tree = Newtonsoft.Json.Linq.JArray.Parse("""
                [{"orderId":1,"orderType":"LIMIT","price":200.0,"status":"WORKING",
                  "orderStrategyType":"TRIGGER",
                  "orderLegCollection":[{"instruction":"BUY","quantity":10,"instrument":{"symbol":"AAPL"}}],
                  "childOrderStrategies":[
                    {"orderStrategyType":"OCO","status":"PENDING_ACTIVATION",
                     "childOrderStrategies":[
                       {"orderId":2,"orderType":"LIMIT","price":220.0,"status":"PENDING_ACTIVATION",
                        "orderLegCollection":[{"instruction":"SELL","quantity":10,"instrument":{"symbol":"AAPL"}}]},
                       {"orderId":3,"orderType":"STOP","stopPrice":180.0,"status":"PENDING_ACTIVATION",
                        "orderLegCollection":[{"instruction":"SELL","quantity":10,"instrument":{"symbol":"AAPL"}}]}
                     ]}]}]
                """);

            var orders = AccessibleTrader.Plugins.Schwab.SchwabProvider.ParseOpenOrders(tree, symbol: null);

            Assert.Equal(3, orders.Count); // entry + both protective children (OCO wrapper itself has no legs)
            Assert.Equal("2", orders[1].Id);
            Assert.Equal(220.0, orders[1].Price);
            Assert.Equal("3", orders[2].Id);
            Assert.Equal(180.0, orders[2].Price); // stopPrice fallback
            Assert.Equal("PENDING_ACTIVATION", orders[2].Status);
        }

        // ── Kraken: one protective slot, stop wins ───────────────────────────

        private static AccessibleTrader.Plugins.Kraken.KrakenProvider Kraken(FakeHttpMessageHandler h)
        {
            var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "k", ["ApiSecret"] = Convert.ToBase64String(new byte[32]) });
            Swap(p, h);
            return p;
        }

        [Fact]
        public async Task Kraken_with_both_legs_attaches_the_stop_not_the_take_profit()
        {
            var h = new FakeHttpMessageHandler().Post(@"/0/private/AddOrder",
                """{"error":[],"result":{"txid":["TX1"]}}""");
            var p = Kraken(h);
            Assert.False(((ITradingProvider)p).SupportsSimultaneousStopAndTarget); // the declared limitation

            await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 0.5,
                OrderType.Market, StopLoss: 90000, TakeProfit: 120000));

            // RAW body, deliberately not unescaped: the unescape here used to
            // normalize away the signed-string-vs-sent-string mismatch (the body
            // carried %5B where the signature was computed over '[', and every
            // bracketed order died with EAPI:Invalid signature in production
            // while this test stayed green).
            string form = await Form(h.Captured.Single(r =>
                r.RequestUri!.ToString().Contains("AddOrder")));
            Assert.Contains("close[ordertype]=stop-loss", form); // safety over profit
            Assert.DoesNotContain("take-profit", form);
        }

        [Fact]
        public async Task Kraken_fills_parse_TradesHistory()
        {
            var h = new FakeHttpMessageHandler().Post(@"/0/private/TradesHistory", """
                {"error":[],"result":{"trades":{
                    "T1":{"pair":"XBT/USD","type":"buy","price":"95000.0","vol":"0.5","fee":"1.9","time":1753100000,"ordertxid":"O1"},
                    "T2":{"pair":"ETH/USD","type":"sell","price":"3500.0","vol":"2.0","fee":"0.7","time":1753100100,"ordertxid":"O2"}
                }}}
                """);
            var p = Kraken(h);

            var fills = await p.GetFillsAsync();

            Assert.Equal(2, fills.Count);
            Assert.Equal("ETH/USD", fills[0].Symbol); // newest first
            Assert.Equal(OrderSide.Sell, fills[0].Side);
            Assert.Equal(95000.0, fills[1].Price);
            Assert.Equal("O1", fills[1].OrderId);
        }

        // ── Alpaca + Coinbase fills ──────────────────────────────────────────

        [Fact]
        public async Task Alpaca_fills_parse_account_activities()
        {
            var p = new AccessibleTrader.Plugins.Alpaca.AlpacaProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "k", ["ApiSecret"] = "s" });
            var h = new FakeHttpMessageHandler().Get(@"/account/activities", """
                [{"id":"a1","activity_type":"FILL","transaction_time":"2026-07-21T15:00:00Z",
                  "type":"fill","price":"231.5","qty":"10","side":"buy","symbol":"AAPL","order_id":"ord-9"}]
                """);
            Swap(p, h);

            var fills = await p.GetFillsAsync();

            var f = Assert.Single(fills);
            Assert.Equal("AAPL", f.Symbol);
            Assert.Equal(OrderSide.Buy, f.Side);
            Assert.Equal(231.5, f.Price);
            Assert.Equal("ord-9", f.OrderId);
        }

        [Fact]
        public async Task Coinbase_fills_parse_historical_fills()
        {
            var p = new AccessibleTrader.Plugins.Coinbase.CoinbaseProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "k", ["ApiSecret"] = "s" });
            var h = new FakeHttpMessageHandler().Get(@"/orders/historical/fills", """
                {"fills":[{"trade_id":"t1","order_id":"o1","product_id":"BTC-USD","trade_time":"2026-07-21T15:00:00Z",
                           "price":"95000","size":"0.25","side":"SELL","commission":"3.2"}]}
                """);
            Swap(p, h);

            var fills = await p.GetFillsAsync();

            var f = Assert.Single(fills);
            Assert.Equal("BTC-USD", f.Symbol);
            Assert.Equal(OrderSide.Sell, f.Side);
            Assert.Equal(0.25, f.Quantity);
            Assert.Equal(3.2, f.Fee);
        }

        // ── Tradier OPTION brackets + position-effect sides (2026-08-23) ─────
        // The options branch refused protective legs until 2026-08-23, and worse,
        // the plain option path sent bare "buy"/"sell" — equity vocabulary the
        // venue refuses on class=option (it wants buy_to_open / sell_to_close /
        // etc.). These pin the OTOCO/OTO option payloads and the side mapping.

        private const string Occ = "AAPL260918C00195000";

        [Fact]
        public async Task Tradier_option_entry_with_both_legs_is_one_OTOCO_with_open_close_sides()
        {
            var h = new FakeHttpMessageHandler().Post(@"/accounts/ACC1/orders", """{"order":{"id":55}}""");
            var p = Tradier(h);

            string id = await p.PlaceOrderAsync(new TradeSignal(Occ, OrderSide.Buy, 2,
                OrderType.Limit, Price: 5.5, StopLoss: 3, TakeProfit: 8, SubType: "Options"));

            Assert.Equal("55", id);
            string form = Uri.UnescapeDataString(await Form(h.Captured.Single()));
            var pairs = form.Split('&');
            Assert.Contains("class=otoco", pairs);
            Assert.Contains("option_symbol=" + Occ, pairs);
            Assert.Contains("symbol=AAPL", pairs);          // the underlying, not the OCC
            Assert.Contains("side[0]=buy_to_open", pairs);  // entry opens
            Assert.Contains("type[0]=limit", pairs);
            Assert.Contains("price[0]=5.5", pairs);
            Assert.Contains("side[1]=sell_to_close", pairs); // TP closes
            Assert.Contains("price[1]=8", pairs);
            Assert.Contains("side[2]=sell_to_close", pairs); // SL closes
            Assert.Contains("type[2]=stop", pairs);
            Assert.Contains("stop[2]=3", pairs);
        }

        [Fact]
        public async Task Tradier_option_limit_entry_with_stop_only_is_an_OTO_with_a_resting_stop()
        {
            // The non-market-entry gap: a LIMIT entry with a stop must still rest
            // the stop leg (every earlier bracket test entered at market).
            var h = new FakeHttpMessageHandler().Post(@"/accounts/ACC1/orders", """{"order":{"id":56}}""");
            var p = Tradier(h);

            await p.PlaceOrderAsync(new TradeSignal(Occ, OrderSide.Buy, 1,
                OrderType.Limit, Price: 5.5, StopLoss: 3, SubType: "Options"));

            string form = Uri.UnescapeDataString(await Form(h.Captured.Single()));
            var pairs = form.Split('&');
            Assert.Contains("class=oto", pairs);
            Assert.Contains("type[0]=limit", pairs);
            Assert.Contains("type[1]=stop", pairs);
            Assert.Contains("stop[1]=3", pairs);
            Assert.Contains("side[1]=sell_to_close", pairs);
            Assert.DoesNotContain(pairs, s => s.StartsWith("type[2]"));
        }

        [Fact]
        public async Task Tradier_plain_option_sell_with_a_long_position_is_sell_to_close()
        {
            var h = new FakeHttpMessageHandler()
                .Get(@"/accounts/ACC1/positions",
                    """{"positions":{"position":{"symbol":"AAPL260918C00195000","quantity":2,"cost_basis":900}}}""")
                .Post(@"/accounts/ACC1/orders", """{"order":{"id":57}}""");
            var p = Tradier(h);

            await p.PlaceOrderAsync(new TradeSignal(Occ, OrderSide.Sell, 2,
                OrderType.Limit, Price: 8, SubType: "Options"));

            string form = Uri.UnescapeDataString(await Form(
                h.Captured.Single(r => r.Method == HttpMethod.Post)));
            var pairs = form.Split('&');
            Assert.Contains("class=option", pairs);
            Assert.Contains("side=sell_to_close", pairs);
            Assert.Contains("option_symbol=" + Occ, pairs);
            Assert.Contains("symbol=AAPL", pairs);
        }

        [Fact]
        public async Task Tradier_plain_option_buy_with_no_position_is_buy_to_open()
        {
            // {"positions":"null"} — the STRING — is Tradier's real empty-account
            // shape, and it used to THROW in GetPositionsAsync (JValue indexing),
            // which would have refused the very first option buy on a new account.
            var h = new FakeHttpMessageHandler()
                .Get(@"/accounts/ACC1/positions", """{"positions":"null"}""")
                .Post(@"/accounts/ACC1/orders", """{"order":{"id":58}}""");
            var p = Tradier(h);

            await p.PlaceOrderAsync(new TradeSignal(Occ, OrderSide.Buy, 1,
                OrderType.Market, SubType: "Options"));

            string form = Uri.UnescapeDataString(await Form(
                h.Captured.Single(r => r.Method == HttpMethod.Post)));
            Assert.Contains("side=buy_to_open", form.Split('&'));
        }

        [Fact]
        public async Task Tradier_plain_option_order_is_refused_when_positions_cannot_be_read()
        {
            // Open-versus-close depends on the position. If that read fails, the
            // order must be refused with spoken text — guessing "sell" into
            // sell_to_open would short a naked option the user meant to close.
            var h = new FakeHttpMessageHandler()
                .Get(@"/accounts/ACC1/positions", """{"error":"forbidden"}""", HttpStatusCode.Forbidden);
            var p = Tradier(h);

            string result = await p.PlaceOrderAsync(new TradeSignal(Occ, OrderSide.Sell, 1,
                OrderType.Market, SubType: "Options"));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Contains("could not read positions", result);
            Assert.DoesNotContain(h.Captured, r => r.Method == HttpMethod.Post); // nothing placed
        }

        [Theory]
        [InlineData("AAPL260918C00195000", "AAPL")]
        [InlineData("BRKB260116P00450000", "BRKB")]
        [InlineData("SPXW261218C05500000", "SPXW")]
        [InlineData("AAPL", null)]                  // no OCC tail at all
        [InlineData("AAPL260918X00195000", null)]   // neither call nor put
        [InlineData("260918C00195000", null)]       // tail with no root
        public void Tradier_occ_underlying_parser(string symbol, string? expected) =>
            Assert.Equal(expected,
                AccessibleTrader.Plugins.Tradier.TradierProvider.UnderlyingFromOccSymbol(symbol));

        // ── IBKR brackets (parent/child rows in ONE submit, 2026-08-23) ──────
        // IBKR refused protective legs until 2026-08-23 ("place the entry on its
        // own..."). Now the entry and its exits go up as one order array: children
        // name the parent's cOID in parentId and the gateway OCA-links the exits.

        private static AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider Ibkr(FakeHttpMessageHandler h)
        {
            var p = new AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider();
            p.Configure(new Dictionary<string, string> { ["AccountId"] = "DU111" });
            p.SeedConIdCacheForTest("AAPL", "265598");
            Swap(p, h);
            return p;
        }

        private static async Task<JArray> IbkrOrders(FakeHttpMessageHandler h)
        {
            var body = await Form(h.Captured.Single(r => r.Method == HttpMethod.Post));
            return (JArray)JObject.Parse(body)["orders"]!;
        }

        [Fact]
        public async Task Ibkr_entry_with_both_legs_is_one_parent_child_submit()
        {
            var h = new FakeHttpMessageHandler().Post(@"/iserver/account/DU111/orders", """[{"order_id":"321"}]""");
            var p = Ibkr(h);

            string id = await p.PlaceOrderAsync(new TradeSignal("AAPL", OrderSide.Buy, 10,
                OrderType.Market, StopLoss: 180, TakeProfit: 220));

            Assert.Equal("321", id);
            var orders = await IbkrOrders(h);
            Assert.Equal(3, orders.Count);

            var parent = (JObject)orders[0];
            Assert.Equal("MKT", parent["orderType"]?.ToString());
            Assert.Equal("BUY", parent["side"]?.ToString());
            string parentOid = parent["cOID"]!.ToString();
            Assert.False(string.IsNullOrEmpty(parentOid)); // children need it to link

            var tp = (JObject)orders[1];
            Assert.Equal("LMT", tp["orderType"]?.ToString());
            Assert.Equal(220, tp["price"]?.Value<double>());
            Assert.Equal("SELL", tp["side"]?.ToString());
            Assert.Equal(parentOid, tp["parentId"]?.ToString());

            var sl = (JObject)orders[2];
            Assert.Equal("STP", sl["orderType"]?.ToString());
            Assert.Equal(180, sl["auxPrice"]?.Value<double>());
            Assert.Equal("SELL", sl["side"]?.ToString());
            Assert.Equal(parentOid, sl["parentId"]?.ToString());
        }

        [Fact]
        public async Task Ibkr_limit_entry_with_stop_only_rests_the_stop()
        {
            // The non-market-entry gap again: a LIMIT entry with a stop loss must
            // produce a resting stop child, not a naked resting limit.
            var h = new FakeHttpMessageHandler().Post(@"/iserver/account/DU111/orders", """[{"order_id":"322"}]""");
            var p = Ibkr(h);

            await p.PlaceOrderAsync(new TradeSignal("AAPL", OrderSide.Buy, 10,
                OrderType.Limit, Price: 100, StopLoss: 90));

            var orders = await IbkrOrders(h);
            Assert.Equal(2, orders.Count);
            Assert.Equal("LMT", orders[0]["orderType"]?.ToString());
            Assert.Equal(100, orders[0]["price"]?.Value<double>());
            Assert.Equal("STP", orders[1]["orderType"]?.ToString());
            Assert.Equal(90, orders[1]["auxPrice"]?.Value<double>());
            Assert.Equal(orders[0]["cOID"]!.ToString(), orders[1]["parentId"]?.ToString());
        }

        [Fact]
        public async Task Ibkr_stop_entry_attaches_the_target_but_never_duplicates_its_own_trigger()
        {
            // On a STOP entry, StopLoss is the entry's own trigger — it must land
            // in the parent's auxPrice and NOT come back as a second STP child at
            // the same price. A TakeProfit alongside it is a real protective leg.
            var h = new FakeHttpMessageHandler().Post(@"/iserver/account/DU111/orders", """[{"order_id":"323"}]""");
            var p = Ibkr(h);

            await p.PlaceOrderAsync(new TradeSignal("AAPL", OrderSide.Buy, 10,
                OrderType.StopMarket, StopLoss: 90, TakeProfit: 120));

            var orders = await IbkrOrders(h);
            Assert.Equal(2, orders.Count);
            Assert.Equal("STP", orders[0]["orderType"]?.ToString());
            Assert.Equal(90, orders[0]["auxPrice"]?.Value<double>());
            var child = Assert.Single(orders.Skip(1));
            Assert.Equal("LMT", child["orderType"]?.ToString());
            Assert.Equal(120, child["price"]?.Value<double>());
        }

        [Fact]
        public async Task Ibkr_plain_order_stays_a_single_row_with_no_invented_cOID()
        {
            // No legs → exactly the pre-bracket payload: one row, and no cOID the
            // caller didn't ask for (the generated one exists only to link children).
            var h = new FakeHttpMessageHandler().Post(@"/iserver/account/DU111/orders", """[{"order_id":"324"}]""");
            var p = Ibkr(h);

            string id = await p.PlaceOrderAsync(new TradeSignal("AAPL", OrderSide.Sell, 5, OrderType.Market));

            Assert.Equal("324", id);
            var orders = await IbkrOrders(h);
            var only = (JObject)Assert.Single(orders);
            Assert.Null(only["cOID"]);
            Assert.Null(only["parentId"]);
        }
    }
}
