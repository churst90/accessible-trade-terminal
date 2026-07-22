using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The 2026-07-22 broker-parity closure. The audit found Tradier and Schwab
    /// SILENTLY DROPPED stop-loss/take-profit on entries, Kraken could attach
    /// only one protective leg without telling anyone, and fill history existed
    /// on Binance alone. These tests pin the exchange-native bracket payloads
    /// and the fill parsing for every broker that gained them.
    /// </summary>
    public class BrokerParityTests
    {
        private static void Swap(object provider, FakeHttpMessageHandler handler)
        {
            var target = provider.GetType()
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .First(f => f.FieldType == typeof(HttpClient));
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

            string form = Uri.UnescapeDataString(await Form(h.Captured.Single(r =>
                r.RequestUri!.ToString().Contains("AddOrder"))));
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
    }
}
