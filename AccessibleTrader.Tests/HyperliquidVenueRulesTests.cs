using System.Net;
using System.Text;
using AccessibleTrader.Plugins.Hyperliquid;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Hyperliquid's own rules, checked before anything is signed: each perp's leverage
    /// ceiling, whole-number leverage, and the $10 smallest order value.
    ///
    /// <para>
    /// The meta below is a TRIMMED COPY OF THE LIVE ANSWER read on 2026-11-10 from
    /// <c>api.hyperliquid.xyz/info {"type":"meta"}</c>: BTC (5 decimals, 40x), KAS (0, 3x) and
    /// TAO (3, 5x) are copied as they came. Trimming renumbers them — live they are assets 0, 60
    /// and 116 — so the asset ids asserted here are positions in this list, not the live ids.
    /// The $10 minimum is from Hyperliquid's list of order errors ("MinTradeNtl: Order must have
    /// minimum value of $10."). What these cannot show is the exchange accepting the orders.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class HyperliquidVenueRulesTests
    {
        private const string Key = "0x0123456789012345678901234567890123456789012345678901234567890123";
        private const string Main = "0x1719884eb866cb12b2287399b15f7db5e7d775ea";

        internal const string LiveMeta = """
        {"universe":[
          {"szDecimals":5,"name":"BTC","maxLeverage":40,"marginTableId":56},
          {"szDecimals":0,"name":"KAS","maxLeverage":3,"marginTableId":3},
          {"szDecimals":3,"name":"TAO","maxLeverage":5,"marginTableId":5}]}
        """;

        private const string Mids = """{"BTC":"50000.0","KAS":"0.05","TAO":"400.0","@107":"40.0"}""";

        private static string SpotMeta() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Hyperliquid", "spotMeta.json"));

        private static HyperliquidProvider Provider(FakeHttpMessageHandler h)
        {
            var p = new HyperliquidProvider();
            p.Configure(new Dictionary<string, string>
            {
                ["ApiKey"] = Main, ["ApiSecret"] = Key, [ProviderConfigKeys.Environment] = ProviderConfigKeys.Live,
            });
            typeof(HyperliquidProvider).GetField("_http", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(p, new HttpClient(h));
            return p;
        }

        private static FakeHttpMessageHandler Armed()
        {
            var h = new FakeHttpMessageHandler();
            h.Add(HttpMethod.Post, @"hyperliquid\.xyz/info", req =>
            {
                string text = JObject.Parse(req.Content!.ReadAsStringAsync().Result)["type"]?.ToString() switch
                {
                    "meta" => LiveMeta,
                    "spotMeta" => SpotMeta(),
                    "allMids" => Mids,
                    _ => "{}",
                };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
            });
            h.Add(HttpMethod.Post, @"hyperliquid\.xyz/exchange", req =>
            {
                string type = JObject.Parse(req.Content!.ReadAsStringAsync().Result)["action"]?["type"]?.ToString() ?? "";
                string text = type == "updateLeverage"
                    ? """{"status":"ok","response":{"type":"default"}}"""
                    : """{"status":"ok","response":{"type":"order","data":{"statuses":[{"resting":{"oid":777}}]}}}""";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
            });
            return h;
        }

        private static List<JObject> Actions(FakeHttpMessageHandler h) =>
            h.Captured.Zip(h.CapturedBodies)
                .Where(x => x.First.RequestUri!.AbsolutePath == "/exchange")
                .Select(x => (JObject)JObject.Parse(x.Second)["action"]!).ToList();

        // ── Leverage ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Leverage_past_TAOs_own_five_is_refused_and_five_is_sent_as_asked()
        {
            var h = Armed();
            var p = Provider(h);
            Assert.Equal("ORDER_FAILED:Hyperliquid's highest leverage on TAO-PERP is 5 times; this asks for 6",
                await p.PlaceOrderAsync(new("TAO-PERP", OrderSide.Buy, 0.03, OrderType.Limit, Price: 400, Leverage: 6)));
            Assert.Empty(Actions(h));

            Assert.Equal("777", await p.PlaceOrderAsync(new("TAO-PERP", OrderSide.Buy, 0.03, OrderType.Limit, Price: 400, Leverage: 5)));
            var lev = Actions(h)[0];
            Assert.Equal("updateLeverage", lev["type"]!.ToString());
            Assert.Equal(2, lev["asset"]!.Value<int>());
            Assert.Equal(5, lev["leverage"]!.Value<int>());
        }

        [Fact]
        public async Task Fractional_leverage_is_refused_not_rounded()
        {
            var h = Armed();
            Assert.Equal("ORDER_FAILED:Hyperliquid takes whole-number leverage from 1 up; 2.5 times is not one",
                await Provider(h).PlaceOrderAsync(new("BTC-PERP", OrderSide.Buy, 0.001, OrderType.Limit, Price: 50000, Leverage: 2.5)));
            Assert.Empty(Actions(h));
        }

        [Fact]
        public async Task The_leverage_offered_is_each_perps_own_none_on_spot()
        {
            var p = Provider(Armed());
            Assert.Equal(Enumerable.Range(2, 39), await p.GetAllowedLeverageAsync("BTC-PERP", OrderSide.Buy));
            Assert.Equal(new[] { 2, 3, 4, 5 }, await p.GetAllowedLeverageAsync("TAO-PERP", OrderSide.Buy));
            Assert.Equal(new[] { 2, 3 }, await p.GetAllowedLeverageAsync("KAS-PERP", OrderSide.Sell));
            Assert.Empty((await p.GetAllowedLeverageAsync("HYPE-USDC", OrderSide.Buy))!);
            Assert.Null(await p.GetAllowedLeverageAsync("NOPE-PERP", OrderSide.Buy));
        }

        // ── The $10 minimum ────────────────────────────────────────────────────

        [Fact]
        public async Task An_order_under_ten_dollars_is_refused_before_the_leverage_is_touched()
        {
            var h = Armed();
            Assert.Equal("ORDER_FAILED:Hyperliquid's smallest BTC-PERP order is worth 10 USDC; this one is worth 5 USDC",
                await Provider(h).PlaceOrderAsync(new("BTC-PERP", OrderSide.Buy, 0.0001, OrderType.Limit, Price: 50000, Leverage: 3)));
            Assert.Empty(Actions(h));   // no updateLeverage, no order
        }

        [Fact]
        public async Task A_market_order_is_valued_at_the_middle_price_not_its_slipped_bound()
        {
            var h = Armed();
            // 0.00019 x 50000 mid = 9.5; the 5% slipped bound would make it 9.975.
            Assert.Equal("ORDER_FAILED:Hyperliquid's smallest BTC-PERP order is worth 10 USDC; this one is worth 9.5 USDC",
                await Provider(h).PlaceOrderAsync(new("BTC-PERP", OrderSide.Buy, 0.00019, OrderType.Market)));
            Assert.Empty(Actions(h));
        }

        [Fact]
        public async Task Exactly_ten_dollars_is_sent()
        {
            var h = Armed();
            Assert.Equal("777", await Provider(h).PlaceOrderAsync(new("BTC-PERP", OrderSide.Buy, 0.0002, OrderType.Limit, Price: 50000)));
            Assert.Single(Actions(h));
        }

        [Fact]
        public async Task A_small_close_is_left_to_the_exchange_so_a_position_can_always_be_closed()
        {
            var h = Armed();
            Assert.Equal("777", await Provider(h).PlaceOrderAsync(
                new("BTC-PERP", OrderSide.Sell, 0.0001, OrderType.Limit, Price: 50000, ReduceOnly: true)));
            Assert.Single(Actions(h));
        }

        [Fact]
        public async Task Spot_under_ten_of_the_quote_token_is_refused()
        {
            var h = Armed();
            Assert.Equal("ORDER_FAILED:Hyperliquid's smallest HYPE-USDC order is worth 10 USDC; this one is worth 8 USDC",
                await Provider(h).PlaceOrderAsync(new("HYPE-USDC", OrderSide.Buy, 0.2, OrderType.Market, SubType: "Spot")));
            Assert.Empty(Actions(h));
        }

        [Fact]
        public async Task A_stop_order_is_valued_at_its_trigger()
        {
            var h = Armed();
            Assert.Equal("ORDER_FAILED:Hyperliquid's smallest BTC-PERP order is worth 10 USDC; this one is worth 4.5 USDC",
                await Provider(h).PlaceOrderAsync(new("BTC-PERP", OrderSide.Sell, 0.0001, OrderType.StopMarket, TriggerPrice: 45000)));
            Assert.Empty(Actions(h));
        }
    }
}
