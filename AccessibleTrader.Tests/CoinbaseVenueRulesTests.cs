using System.Net;
using AccessibleTrader.Plugins.Coinbase;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Coinbase's two live-money defects found by the 2026-11-10 venue audit, after Kraken's.
    ///
    /// <para>
    /// <b>Sizes and prices.</b> Every number went out at "F8" whatever the product, and no
    /// minimum was checked. The product fixture below is the public BTC-USD answer read live on
    /// 2026-11-10 (fields the code does not read trimmed): base_increment and base_min_size
    /// 0.00000001, price_increment 0.01, quote_min_size 1.
    /// </para>
    ///
    /// <para>
    /// <b>Balances.</b> The accounts read took the first page and stopped, so a holding whose
    /// wallet sorted past it was not in the balances at all.
    /// </para>
    ///
    /// <para>
    /// What these cannot show is Coinbase accepting the orders; that needs a funded account.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class CoinbaseVenueRulesTests
    {
        // GET https://api.coinbase.com/api/v3/brokerage/market/products/BTC-USD, 2026-11-10.
        internal const string BtcUsdProduct = """
        {"product_id":"BTC-USD","price":"74348.3","base_increment":"0.00000001","quote_increment":"0.01",
         "quote_min_size":"1","quote_max_size":"150000000","base_min_size":"0.00000001","base_max_size":"3400",
         "base_name":"Bitcoin","quote_name":"US Dollar","is_disabled":false,"status":"online","cancel_only":false,
         "limit_only":false,"post_only":false,"trading_disabled":false,"auction_mode":false,"product_type":"SPOT",
         "quote_currency_id":"USD","base_currency_id":"BTC","base_display_symbol":"BTC","quote_display_symbol":"USD",
         "view_only":false,"price_increment":"0.01","display_name":"BTC-USD","product_venue":"CBE"}
        """;

        private static string Product(Action<JObject> change)
        {
            var o = JObject.Parse(BtcUsdProduct);
            change(o);
            return o.ToString();
        }

        private const string ProductRoute = @"api\.coinbase\.com/api/v3/brokerage/market/products/BTC-USD$";
        private const string OrderRoute   = @"api\.coinbase\.com/api/v3/brokerage/orders$";
        private const string Accepted     = """{"success":true,"success_response":{"order_id":"cb-1"}}""";

        private static (CoinbaseProvider P, FakeHttpMessageHandler H) Rig(string? product = BtcUsdProduct)
        {
            var p = new CoinbaseProvider();
            p.Configure(SymbolListHarness.Credentials());
            var h = new FakeHttpMessageHandler { StrictMode = true };
            if (product != null) h.Get(ProductRoute, product);
            h.Post(OrderRoute, Accepted);
            SymbolListHarness.SwapEveryHttpClient(p, h);
            return (p, h);
        }

        private static JObject? SentOrder(FakeHttpMessageHandler h)
        {
            int i = h.Captured.FindIndex(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/api/v3/brokerage/orders");
            return i < 0 ? null : (JObject)JObject.Parse(h.CapturedBodies[i])["order_configuration"]!;
        }

        // ── The product answer ───────────────────────────────────────────────

        [Fact]
        public void The_real_BTC_USD_answer_reads_as_its_steps_and_minimums()
        {
            var rules = CoinbaseProductRules.Parse(JObject.Parse(BtcUsdProduct))!;

            Assert.Equal("BTC/USD", rules.Rules.Symbol);
            Assert.Equal("BTC", rules.Rules.QuantityUnit);
            Assert.Equal("USD", rules.Rules.QuoteAsset);
            Assert.Equal(0.00000001m, rules.Rules.QuantityStep);
            Assert.Equal(0.00000001m, rules.Rules.MinQuantity);
            Assert.Equal(0.01m, rules.Rules.PriceTick);
            Assert.Equal(1m, rules.Rules.MinNotional);
            Assert.Null(rules.Rules.NotTrading);
            Assert.False(rules.LimitOnly);
            Assert.False(rules.PostOnly);
        }

        // ── Numbers on the wire ──────────────────────────────────────────────

        [Fact]
        public async Task A_limit_goes_out_on_the_products_tick_and_step_not_at_eight_decimals()
        {
            var (p, h) = Rig();

            string id = await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 0.123456789,
                OrderType.Limit, Price: 74348.123456));

            Assert.Equal("cb-1", id);
            var cfg = SentOrder(h)!["limit_limit_gtc"]!;
            // Cut DOWN to the step (never up: that would buy more than was asked for)...
            Assert.Equal("0.12345678", cfg["base_size"]!.ToString());
            // ...and the price on the 0.01 tick. F8 sent "74348.12345600".
            Assert.Equal("74348.12", cfg["limit_price"]!.ToString());
        }

        [Fact]
        public async Task A_stop_formats_its_trigger_and_its_protective_limit_on_the_tick()
        {
            var (p, h) = Rig();

            await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Sell, 0.01,
                OrderType.StopMarket, TriggerPrice: 70000.555));

            var cfg = SentOrder(h)!["stop_limit_stop_limit_gtc"]!;
            Assert.Equal("0.01", cfg["base_size"]!.ToString());
            Assert.Equal("70000.56", cfg["stop_price"]!.ToString());
            // 70000.555 × 0.95 = 66500.52725
            Assert.Equal("66500.53", cfg["limit_price"]!.ToString());
        }

        [Fact]
        public async Task Without_the_rules_the_order_goes_out_as_it_did_before()
        {
            var (p, h) = Rig(product: null);
            h.Get(ProductRoute, """{"error":"unavailable"}""", HttpStatusCode.ServiceUnavailable);

            string id = await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 0.5,
                OrderType.Limit, Price: 0.0363));

            Assert.Equal("cb-1", id);
            var cfg = SentOrder(h)!["limit_limit_gtc"]!;
            Assert.Equal("0.50000000", cfg["base_size"]!.ToString());
            Assert.Equal("0.03630000", cfg["limit_price"]!.ToString());
        }

        [Fact]
        public async Task The_rules_are_read_once_for_two_orders()
        {
            var (p, h) = Rig();

            await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 0.01, OrderType.Limit, Price: 70000));
            await p.PlaceOrderAsync(new TradeSignal("BTC-USD", OrderSide.Buy, 0.01, OrderType.Limit, Price: 70000));

            Assert.Single(h.Captured, r => r.Method == HttpMethod.Get);
            Assert.Equal(2, h.Captured.Count(r => r.Method == HttpMethod.Post));
        }

        // ── Refusals, before anything is sent ────────────────────────────────

        [Fact]
        public async Task An_order_under_the_products_minimum_value_is_refused_in_words_and_not_sent()
        {
            var (p, h) = Rig();

            // 0.00001 BTC at 50,000 is worth 0.50 USD; the product's minimum is 1 USD.
            string result = await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 0.00001,
                OrderType.Limit, Price: 50000));

            Assert.Equal("ORDER_FAILED:Coinbase's smallest BTC/USD order is worth 1 USD; this one is worth 0.5 USD", result);
            Assert.Null(SentOrder(h));
        }

        [Fact]
        public async Task A_size_under_one_step_is_refused_in_words_and_not_sent()
        {
            var (p, h) = Rig();

            string result = await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Sell, 0.000000004));

            Assert.StartsWith("ORDER_FAILED:Coinbase's smallest BTC/USD order is 0.00000001 BTC", result);
            Assert.Null(SentOrder(h));
        }

        [Theory]
        [InlineData("cancel_only", "cancel-only")]
        [InlineData("trading_disabled", "switched off")]
        [InlineData("post_only", "post-only")]
        public async Task A_product_not_taking_these_orders_refuses_in_words(string flag, string words)
        {
            var (p, h) = Rig(Product(o => o[flag] = true));

            string result = await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 0.01, OrderType.Limit, Price: 70000));

            Assert.StartsWith("ORDER_FAILED:Coinbase", result);
            Assert.Contains(words, result);
            Assert.Null(SentOrder(h));
        }

        [Fact]
        public async Task A_limit_only_product_refuses_a_market_order_and_takes_a_limit()
        {
            var (p, h) = Rig(Product(o => o["limit_only"] = true));

            string market = await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 0.01));
            Assert.Equal("ORDER_FAILED:Coinbase takes only limit orders on BTC/USD right now; a market order would be refused", market);
            Assert.Null(SentOrder(h));

            string limit = await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 0.01, OrderType.Limit, Price: 70000));
            Assert.Equal("cb-1", limit);
        }

        // ── Balances, every page ─────────────────────────────────────────────

        private static string AccountsPage(string currency, string value, bool hasNext, string cursor) => $$$"""
        {"accounts":[
          {"uuid":"a-{{{currency}}}","name":"{{{currency}}} Wallet","currency":"{{{currency}}}",
           "available_balance":{"value":"{{{value}}}","currency":"{{{currency}}}"},"hold":{"value":"0","currency":"{{{currency}}}"}},
          {"uuid":"a-EMPTY-{{{currency}}}","name":"Empty","currency":"ZZ{{{currency}}}",
           "available_balance":{"value":"0","currency":"ZZ{{{currency}}}"},"hold":{"value":"0","currency":"ZZ{{{currency}}}"}}
         ],"has_next":{{{(hasNext ? "true" : "false")}}},"cursor":"{{{cursor}}}","size":2}
        """;

        [Fact]
        public async Task The_balance_read_walks_every_page_of_accounts()
        {
            using var p = new CoinbaseProvider();
            p.Configure(SymbolListHarness.Credentials());
            var h = new FakeHttpMessageHandler { StrictMode = true };
            h.Get(@"/api/v3/brokerage/accounts\?limit=250$", AccountsPage("USD", "1500.25", hasNext: true, cursor: "page-2"));
            h.Get(@"/api/v3/brokerage/accounts\?limit=250&cursor=page-2$", AccountsPage("KAS", "200", hasNext: false, cursor: ""));
            // A spot-only account: the futures summary answers, with nothing in it.
            h.Get(@"/api/v3/brokerage/cfm/balance_summary$", """{"balance_summary":{"cfm_usd_balance":{"value":"0","currency":"USD"}}}""");
            SymbolListHarness.SwapEveryHttpClient(p, h);

            var balances = await p.GetBalancesAsync();

            Assert.Contains(balances, b => b.Asset == "USD" && b.Free == 1500.25);
            // The holding on the SECOND page: the one-page read never saw it.
            Assert.Contains(balances, b => b.Asset == "KAS" && b.Free == 200);
            Assert.Equal(2, balances.Count);
            Assert.Equal(3, h.Captured.Count);
        }

        [Fact]
        public async Task A_list_that_says_more_follow_but_gives_no_cursor_fails_the_read()
        {
            using var p = new CoinbaseProvider();
            p.Configure(SymbolListHarness.Credentials());
            var h = new FakeHttpMessageHandler { StrictMode = true };
            h.Get(@"/api/v3/brokerage/accounts\?limit=250$", AccountsPage("USD", "10", hasNext: true, cursor: ""));
            SymbolListHarness.SwapEveryHttpClient(p, h);

            // A part-list is the one-page lie again; the read fails so the order service sees it.
            await Assert.ThrowsAsync<InvalidOperationException>(() => p.GetBalancesAsync());
        }
    }
}
