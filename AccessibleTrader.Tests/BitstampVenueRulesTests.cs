using System.Net;
using AccessibleTrader.Plugins.Bitstamp;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Bitstamp's live-money defect found by the 2026-11-10 venue audit, after Kraken's: the
    /// amount and price went out through <c>ToString()</c> unrounded — finer than the pair's
    /// decimals, and a small number as "1E-05" — and no minimum was checked.
    ///
    /// <para>
    /// The fixture rows are Bitstamp's own, read live from <c>/api/v2/trading-pairs-info/</c>
    /// on 2026-11-10: BTC/USD, ETH/BTC (a minimum counted in BTC), ZEC/USD (trading, but no
    /// market orders), SHIB/USD (whole coins only) and ZK/EUR (trading switched off). What these
    /// cannot show is Bitstamp accepting the orders; that needs a funded account.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class BitstampVenueRulesTests
    {
        internal const string PairsInfo = """
        [
        {"name": "BTC/USD", "url_symbol": "btcusd", "base_decimals": 8, "counter_decimals": 2, "instant_order_counter_decimals": 2, "minimum_order": "10.00 USD", "trading": "Enabled", "instant_and_market_orders": "Enabled", "description": "Bitcoin / U.S. dollar"},
        {"name": "ETH/BTC", "url_symbol": "ethbtc", "base_decimals": 8, "counter_decimals": 8, "instant_order_counter_decimals": 8, "minimum_order": "0.00005000 BTC", "trading": "Enabled", "instant_and_market_orders": "Enabled", "description": "Ethereum / Bitcoin"},
        {"name": "ZEC/USD", "url_symbol": "zecusd", "base_decimals": 5, "counter_decimals": 2, "instant_order_counter_decimals": 2, "minimum_order": "10.00 USD", "trading": "Enabled", "instant_and_market_orders": "Disabled", "description": "Zcash / U.S. dollar"},
        {"name": "SHIB/USD", "url_symbol": "shibusd", "base_decimals": 0, "counter_decimals": 8, "instant_order_counter_decimals": 8, "minimum_order": "10.00000000 USD", "trading": "Enabled", "instant_and_market_orders": "Enabled", "description": "Shiba Inu / U.S. dollar"},
        {"name": "ZK/EUR", "url_symbol": "zkeur", "base_decimals": 0, "counter_decimals": 6, "instant_order_counter_decimals": 6, "minimum_order": "10.000000 EUR", "trading": "Disabled", "instant_and_market_orders": "Disabled", "description": "ZKsync / Euro"}
        ]
        """;

        private const string PairsRoute = @"bitstamp\.net/api/v2/trading-pairs-info/$";
        private const string OrderRoute = @"bitstamp\.net/api/v2/(buy|sell)/";

        private static (BitstampProvider P, FakeHttpMessageHandler H) Rig(bool rulesReadable = true)
        {
            var p = new BitstampProvider();
            p.Configure(SymbolListHarness.Credentials());
            var h = new FakeHttpMessageHandler { StrictMode = true };
            if (rulesReadable) h.Get(PairsRoute, PairsInfo);
            else h.Get(PairsRoute, "<html>502 Bad Gateway</html>", HttpStatusCode.BadGateway);
            h.Post(OrderRoute, """{"id":"9911"}""");
            SymbolListHarness.SwapEveryHttpClient(p, h);
            return (p, h);
        }

        /// <summary>The order's form fields, or null when no order was posted.</summary>
        private static Dictionary<string, string>? SentOrder(FakeHttpMessageHandler h)
        {
            int i = h.Captured.FindIndex(r => r.Method == HttpMethod.Post
                && (r.RequestUri!.AbsolutePath.Contains("/buy/") || r.RequestUri!.AbsolutePath.Contains("/sell/")));
            if (i < 0) return null;
            return h.CapturedBodies[i].Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(kv => kv.Split('=', 2))
                .ToDictionary(kv => Uri.UnescapeDataString(kv[0]), kv => Uri.UnescapeDataString(kv.Length > 1 ? kv[1] : ""));
        }

        // ── The pairs answer ─────────────────────────────────────────────────

        [Fact]
        public void The_real_rows_read_as_steps_ticks_and_a_minimum_order_value()
        {
            var rules = BitstampPairRules.Parse(JArray.Parse(PairsInfo));

            var btc = rules["btcusd"].Rules;
            Assert.Equal("BTC/USD", btc.Symbol);
            Assert.Equal("BTC", btc.QuantityUnit);
            Assert.Equal(0.00000001m, btc.QuantityStep);
            Assert.Equal(0.01m, btc.PriceTick);
            // minimum_order is a VALUE in the counter currency, not a quantity.
            Assert.Equal(10m, btc.MinNotional);
            Assert.Equal("USD", btc.QuoteAsset);
            Assert.Equal(0m, btc.MinQuantity);

            Assert.Equal(0.00005m, rules["ethbtc"].Rules.MinNotional);
            Assert.Equal("BTC", rules["ethbtc"].Rules.QuoteAsset);
            Assert.Equal(1m, rules["shibusd"].Rules.QuantityStep);
            Assert.False(rules["zecusd"].MarketOrders);
            Assert.Null(rules["zecusd"].Rules.NotTrading);
            Assert.NotNull(rules["zkeur"].Rules.NotTrading);
        }

        // ── Numbers on the wire ──────────────────────────────────────────────

        [Fact]
        public async Task A_limit_goes_out_on_the_pairs_decimals()
        {
            var (p, h) = Rig();

            string id = await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 0.123456789,
                OrderType.Limit, Price: 74348.126));

            Assert.Equal("9911", id);
            var form = SentOrder(h)!;
            Assert.Equal("0.12345678", form["amount"]);   // cut down, never up
            Assert.Equal("74348.13", form["price"]);
        }

        [Fact]
        public async Task A_small_price_goes_out_in_plain_digits_not_exponent_form()
        {
            var (p, h) = Rig();

            // 10 ETH at 0.00001 BTC is worth 0.0001 BTC, over the pair's 0.00005 BTC minimum.
            await p.PlaceOrderAsync(new TradeSignal("ETH/BTC", OrderSide.Sell, 10, OrderType.Limit, Price: 0.00001));

            var form = SentOrder(h)!;
            Assert.Equal("10", form["amount"]);
            Assert.Equal("0.00001", form["price"]);   // ToString() printed "1E-05"
        }

        [Fact]
        public async Task A_whole_coin_pair_sends_whole_coins()
        {
            var (p, h) = Rig();

            await p.PlaceOrderAsync(new TradeSignal("SHIB/USD", OrderSide.Buy, 1_234_567.9, OrderType.Limit, Price: 0.0000123456789));

            var form = SentOrder(h)!;
            Assert.Equal("1234567", form["amount"]);
            Assert.Equal("0.00001235", form["price"]);
        }

        [Fact]
        public async Task Without_the_rules_the_order_still_goes_out_in_plain_digits()
        {
            var (p, h) = Rig(rulesReadable: false);

            // Both numbers are small enough that double.ToString would write them as 1E-05 and 3E-05.
            string id = await p.PlaceOrderAsync(new TradeSignal("ETH/BTC", OrderSide.Buy, 0.00001, OrderType.Limit, Price: 0.00003));

            Assert.Equal("9911", id);
            var form = SentOrder(h)!;
            Assert.Equal("0.00001", form["amount"]);
            Assert.Equal("0.00003", form["price"]);
        }

        [Fact]
        public async Task The_rules_are_read_once_for_two_orders()
        {
            var (p, h) = Rig();

            await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 0.001, OrderType.Limit, Price: 70000));
            await p.PlaceOrderAsync(new TradeSignal("BTC/USDT", OrderSide.Sell, 0.001));

            Assert.Single(h.Captured, r => r.RequestUri!.AbsolutePath.EndsWith("/trading-pairs-info/"));
            Assert.Equal(2, h.Captured.Count(r => r.Method == HttpMethod.Post));
        }

        // ── Refusals, before anything is sent ────────────────────────────────

        [Fact]
        public async Task An_order_under_the_pairs_minimum_value_is_refused_in_words_and_not_sent()
        {
            var (p, h) = Rig();

            string result = await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 0.0001, OrderType.Limit, Price: 50000));

            Assert.Equal("ORDER_FAILED:Bitstamp's smallest BTC/USD order is worth 10 USD; this one is worth 5 USD", result);
            Assert.Null(SentOrder(h));
        }

        [Fact]
        public async Task A_pair_with_trading_switched_off_refuses_in_words_and_sends_nothing()
        {
            var (p, h) = Rig();

            string result = await p.PlaceOrderAsync(new TradeSignal("ZK/EUR", OrderSide.Buy, 100, OrderType.Limit, Price: 1));

            Assert.Equal("ORDER_FAILED:Bitstamp has trading on ZK/EUR switched off right now", result);
            Assert.Null(SentOrder(h));
        }

        [Fact]
        public async Task A_pair_without_market_orders_refuses_a_market_order_and_takes_a_limit()
        {
            var (p, h) = Rig();

            string market = await p.PlaceOrderAsync(new TradeSignal("ZEC/USD", OrderSide.Buy, 1));
            Assert.Equal("ORDER_FAILED:Bitstamp takes no market orders on ZEC/USD right now; a limit order is still accepted", market);
            Assert.Null(SentOrder(h));

            string limit = await p.PlaceOrderAsync(new TradeSignal("ZEC/USD", OrderSide.Buy, 1, OrderType.Limit, Price: 40));
            Assert.Equal("9911", limit);
            Assert.Equal("1", SentOrder(h)!["amount"]);
        }
    }
}
