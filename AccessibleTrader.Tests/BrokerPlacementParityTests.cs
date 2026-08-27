using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.Tests.Fakes;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The 2026-08-23 placement-path closure for the brokers BrokerParityTests
    /// never reached: Binance, Bitstamp, MEXC, Oanda, Gemini, KrakenFutures.
    /// Each broker gets (a) the wire payload of a representative entry pinned
    /// field-by-field, and (b) where the venue signs requests, a
    /// sign-what-you-send assertion: the signature header is recomputed over the
    /// EXACT bytes captured on the wire, never over a re-encoding of them —
    /// re-encoding is how Kraken spot's bracket signature bug stayed green.
    /// </summary>
    // Shares the signed-path collection: these tests rely on Configure()-supplied
    // credentials, which the global PluginHostServices.ApiKeys bridge preempts
    // when another test class installs a fake into it.
    [Collection("ProviderCredentialBridge")]
    public class BrokerPlacementParityTests
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
        private static int VenueCallIndex(FakeHttpMessageHandler h)
        {
            var idx = Enumerable.Range(0, h.Captured.Count)
                .Where(i => !h.Captured[i].RequestUri!.AbsolutePath
                              .EndsWith("/api/v3/time", StringComparison.Ordinal))
                .ToList();
            return Assert.Single(idx);
        }

        private static HttpRequestMessage OnlyVenueCall(FakeHttpMessageHandler h)
            => h.Captured[VenueCallIndex(h)];

        /// <summary>The body of that same call — <c>CapturedBodies</c> runs parallel to
        /// <c>Captured</c>, so the clock probe occupies a slot there too.</summary>
        private static string OnlyVenueBody(FakeHttpMessageHandler h)
            => h.CapturedBodies[VenueCallIndex(h)];
        private static void SwapField(object target, string fieldName, object? value)
        {
            var f = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(f);
            f!.SetValue(target, value);
        }

        private static Dictionary<string, string> FormPairs(string form) =>
            form.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .ToDictionary(p => Uri.UnescapeDataString(p[0]),
                              p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");

        // ── Binance spot: HMAC-SHA256 over the query actually sent ───────────

        private static AccessibleTrader.Plugins.Binance.BinanceProvider Binance(FakeHttpMessageHandler h)
        {
            var p = new AccessibleTrader.Plugins.Binance.BinanceProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "bk", ["ApiSecret"] = "bs" });
            // The REST client is the lazy Http property's backing field, not the
            // `_httpClient`/`_http` names the other providers use.
            SwapField(p, "_httpField", new HttpClient(h));
            return p;
        }

        [Fact]
        public async Task Binance_spot_limit_buy_signs_the_exact_query_it_sends()
        {
            var h = new FakeHttpMessageHandler().Post(@"api\.binance\.com/api/v3/order\?", """{"orderId":123}""");
            var p = Binance(h);

            string id = await p.PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Buy, 0.5,
                OrderType.Limit, Price: 95000.5));

            Assert.Equal("123", id);
            var req = OnlyVenueCall(h);
            // RAW query, no unescaping: the signature must verify over what left
            // the process, not over a normalized copy of it.
            string query = req.RequestUri!.Query.TrimStart('?');
            int sigAt = query.IndexOf("&signature=", StringComparison.Ordinal);
            Assert.True(sigAt > 0, "signature missing from query");
            string signed = query[..sigAt];
            string sig = query[(sigAt + "&signature=".Length)..];

            Assert.Equal(RestSigning.HmacSha256Hex("bs", signed), sig);
            Assert.Equal("bk", req.Headers.GetValues("X-MBX-APIKEY").Single());

            var pairs = FormPairs(signed);
            Assert.Equal("BTCUSDT", pairs["symbol"]);
            Assert.Equal("BUY", pairs["side"]);
            Assert.Equal("LIMIT", pairs["type"]);
            Assert.Equal("0.5", pairs["quantity"]);
            Assert.Equal("95000.5", pairs["price"]);
            Assert.Equal("GTC", pairs["timeInForce"]);
            Assert.True(pairs.ContainsKey("timestamp"), "timestamp must be part of the signed query");
        }

        [Fact]
        public async Task Binance_spot_stop_market_sell_is_STOP_LOSS_at_the_trigger()
        {
            var h = new FakeHttpMessageHandler().Post(@"api\.binance\.com/api/v3/order\?", """{"orderId":124}""");
            var p = Binance(h);

            await p.PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Sell, 0.5,
                OrderType.StopMarket, TriggerPrice: 90000));

            var pairs = FormPairs(OnlyVenueCall(h).RequestUri!.Query.TrimStart('?'));
            Assert.Equal("STOP_LOSS", pairs["type"]);
            Assert.Equal("90000", pairs["stopPrice"]);
            Assert.Equal("SELL", pairs["side"]);
            Assert.False(pairs.ContainsKey("price"), "a stop-market carries no limit price");
        }

        // ── Bitstamp: legacy v0 signature, full-precision invariant prices ───

        private static AccessibleTrader.Plugins.Bitstamp.BitstampProvider Bitstamp(FakeHttpMessageHandler h)
        {
            var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
            p.Configure(new Dictionary<string, string>
                { ["ApiKey"] = "stampkey", ["ApiSecret"] = "stampsecret", ["CustomerId"] = "cust77" });
            SwapField(p, "_httpClient", new HttpClient(h));
            return p;
        }

        [Fact]
        public async Task Bitstamp_limit_buy_posts_full_precision_and_a_verifiable_signature()
        {
            var h = new FakeHttpMessageHandler().Post(@"bitstamp\.net/api/v2/buy/btcusd/", """{"id":"9911"}""");
            var p = Bitstamp(h);

            // The 2026-08-21 fixed-precision find: 0.0363 used to go out as "0.04".
            string id = await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 0.5,
                OrderType.Limit, Price: 0.0363));

            Assert.Equal("9911", id);
            var pairs = FormPairs(OnlyVenueBody(h));
            Assert.Equal("0.5", pairs["amount"]);
            Assert.Equal("0.0363", pairs["price"]);

            // signature = HMAC-SHA256(secret, nonce + customerId + apiKey), upper hex —
            // recomputed from the nonce that was actually sent.
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("stampsecret"));
            string expected = Convert.ToHexString(hmac.ComputeHash(
                Encoding.UTF8.GetBytes(pairs["nonce"] + "cust77" + "stampkey")));
            Assert.Equal(expected, pairs["signature"]);
            Assert.Equal("stampkey", pairs["key"]);
        }

        [Fact]
        public async Task Bitstamp_market_sell_routes_to_the_market_endpoint_without_a_price()
        {
            var h = new FakeHttpMessageHandler().Post(@"bitstamp\.net/api/v2/sell/market/btcusd/", """{"id":"9912"}""");
            var p = Bitstamp(h);

            string id = await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Sell, 0.25, OrderType.Market));

            Assert.Equal("9912", id);
            var pairs = FormPairs(OnlyVenueBody(h));
            Assert.False(pairs.ContainsKey("price"), "market orders carry no price field");
            Assert.False(pairs.ContainsKey("limit_price"), "no ceiling was asked for");
        }

        [Fact]
        public async Task Bitstamp_error_status_surfaces_the_reason_not_a_success_id()
        {
            var h = new FakeHttpMessageHandler().Post(@"bitstamp\.net/api/v2/buy/btcusd/",
                """{"status":"error","reason":"Minimum order size is 10 USD","id":"0"}""");
            var p = Bitstamp(h);

            string result = await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 0.0001,
                OrderType.Limit, Price: 100));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Contains("Minimum order size", result);
        }

        // ── MEXC: spot query signing + futures header signing with legs ──────

        private static AccessibleTrader.Plugins.Mexc.MexcProvider Mexc(FakeHttpMessageHandler h)
        {
            var p = new AccessibleTrader.Plugins.Mexc.MexcProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "mk", ["ApiSecret"] = "ms" });
            // _rest captured the original HttpClient at construction, so the
            // client swap must rebuild it — swapping _http alone changes nothing.
            SwapField(p, "_rest", new AccessibleTrader.Plugins.Mexc.MexcRestApi(new HttpClient(h)));
            SwapField(p, "_connected", true); // IsConnected gates PlaceOrderAsync
            return p;
        }

        [Fact]
        public async Task Mexc_spot_limit_sell_signs_the_exact_query_it_sends()
        {
            var h = new FakeHttpMessageHandler().Post(@"api\.mexc\.com/api/v3/order\?", """{"orderId":"C02__443"}""");
            var p = Mexc(h);

            string id = await p.PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Sell, 0.5,
                OrderType.Limit, Price: 95000.5));

            Assert.Equal("C02__443", id);
            var req = OnlyVenueCall(h);
            string query = req.RequestUri!.Query.TrimStart('?');
            int sigAt = query.IndexOf("&signature=", StringComparison.Ordinal);
            Assert.True(sigAt > 0, "signature missing from query");
            Assert.Equal(RestSigning.HmacSha256Hex("ms", query[..sigAt]),
                         query[(sigAt + "&signature=".Length)..]);
            Assert.Equal("mk", req.Headers.GetValues("X-MEXC-APIKEY").Single());

            var pairs = FormPairs(query[..sigAt]);
            Assert.Equal("SELL", pairs["side"]);
            Assert.Equal("LIMIT", pairs["type"]);
            Assert.Equal("95000.5", pairs["price"]);
        }

        [Fact]
        public async Task Mexc_futures_limit_buy_carries_both_protective_legs_and_a_verifiable_signature()
        {
            var h = new FakeHttpMessageHandler().Post(@"contract\.mexc\.com/api/v1/private/order/submit",
                """{"success":true,"code":0,"data":"102015012431"}""");
            var p = Mexc(h);

            string id = await p.PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Buy, 2,
                OrderType.Limit, Price: 95000, StopLoss: 90000, TakeProfit: 105000, SubType: "Futures"));

            Assert.Equal("102015012431", id);
            var req = OnlyVenueCall(h);
            string body = OnlyVenueBody(h);
            var order = JObject.Parse(body);
            Assert.Equal("BTC_USDT", order["symbol"]?.ToString());
            Assert.Equal(1, order["side"]?.Value<int>());   // open long
            Assert.Equal(1, order["type"]?.Value<int>());   // limit
            Assert.Equal(90000, order["stopLossPrice"]?.Value<double>());
            Assert.Equal(105000, order["takeProfitPrice"]?.Value<double>());

            // Signature = HMAC-SHA256(secret, apiKey + Request-Time + rawBody) —
            // over the body bytes actually sent, via the header's own timestamp.
            string reqTime = req.Headers.GetValues("Request-Time").Single();
            Assert.Equal(RestSigning.HmacSha256Hex("ms", "mk" + reqTime + body),
                         req.Headers.GetValues("Signature").Single());
        }

        [Fact]
        public async Task Mexc_futures_reduce_only_sell_is_close_long_not_open_short()
        {
            var h = new FakeHttpMessageHandler().Post(@"contract\.mexc\.com/api/v1/private/order/submit",
                """{"success":true,"code":0,"data":"7"}""");
            var p = Mexc(h);

            await p.PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Sell, 2,
                OrderType.Market, SubType: "Futures", ReduceOnly: true));

            var order = JObject.Parse(OnlyVenueBody(h));
            Assert.Equal(4, order["side"]?.Value<int>()); // close long — 3 would OPEN a short
            Assert.Equal(5, order["type"]?.Value<int>()); // market
        }

        // ── Oanda: signed units, on-fill legs, honest percent-trail refusal ──

        private static AccessibleTrader.Plugins.Oanda.OandaProvider Oanda(FakeHttpMessageHandler h)
        {
            var p = new AccessibleTrader.Plugins.Oanda.OandaProvider();
            p.Configure(new Dictionary<string, string>
                { ["AccessToken"] = "tok", ["AccountId"] = "001-001-1234567-001", ["Environment"] = "practice" });
            SwapField(p, "_httpClient", new HttpClient(h));
            return p;
        }

        [Fact]
        public async Task Oanda_market_buy_with_both_legs_attaches_them_on_fill()
        {
            var h = new FakeHttpMessageHandler().Post(@"api-fxpractice\.oanda\.com/v3/accounts/001-001-1234567-001/orders",
                """{"orderFillTransaction":{"id":"6367"}}""");
            var p = Oanda(h);

            string id = await p.PlaceOrderAsync(new TradeSignal("EUR/USD", OrderSide.Buy, 10000,
                OrderType.Market, StopLoss: 1.05, TakeProfit: 1.15));

            Assert.Equal("6367", id);
            var order = (JObject)JObject.Parse(OnlyVenueBody(h))["order"]!;
            Assert.Equal("MARKET", order["type"]?.ToString());
            Assert.Equal("EUR_USD", order["instrument"]?.ToString());
            Assert.Equal("10000", order["units"]?.ToString());   // positive = buy
            Assert.Equal("FOK", order["timeInForce"]?.ToString());
            Assert.Equal("1.05", order["stopLossOnFill"]?["price"]?.ToString());
            Assert.Equal("1.15", order["takeProfitOnFill"]?["price"]?.ToString());
        }

        [Fact]
        public async Task Oanda_limit_sell_has_negative_units_and_keeps_its_stop()
        {
            var h = new FakeHttpMessageHandler().Post(@"oanda\.com/v3/accounts/.*/orders",
                """{"orderCreateTransaction":{"id":"6368"}}""");
            var p = Oanda(h);

            string id = await p.PlaceOrderAsync(new TradeSignal("EUR/USD", OrderSide.Sell, 10000,
                OrderType.Limit, Price: 1.12, StopLoss: 1.15));

            Assert.Equal("6368", id);
            var order = (JObject)JObject.Parse(OnlyVenueBody(h))["order"]!;
            Assert.Equal("LIMIT", order["type"]?.ToString());
            Assert.Equal("-10000", order["units"]?.ToString()); // negative = sell
            Assert.Equal("1.12", order["price"]?.ToString());
            // The limit-entry stop: the leg that used to be silently dropped.
            Assert.Equal("1.15", order["stopLossOnFill"]?["price"]?.ToString());
        }

        [Fact]
        public async Task Oanda_percent_trail_on_a_market_order_is_refused_before_any_request()
        {
            // A percent needs a reference price; a market order has none here.
            // Refusing with spoken text beats attaching a trail at a wrong distance.
            var h = new FakeHttpMessageHandler();
            var p = Oanda(h);

            string result = await p.PlaceOrderAsync(new TradeSignal("EUR/USD", OrderSide.Buy, 10000,
                OrderType.Market, TrailStopMode: TrailMode.Percent, TrailStopValue: 1.5));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Contains("reference price", result);
            Assert.Empty(h.Captured); // nothing reached the wire
        }

        // ── Gemini: the signature covers the exact payload header sent ───────

        private static AccessibleTrader.Plugins.Gemini.GeminiProvider Gemini(FakeHttpMessageHandler h)
        {
            var p = new AccessibleTrader.Plugins.Gemini.GeminiProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "gk", ["ApiSecret"] = "gs" });
            SwapField(p, "_http", new HttpClient(h));
            return p;
        }

        [Fact]
        public async Task Gemini_limit_buy_sends_a_signed_payload_header_with_an_empty_body()
        {
            var h = new FakeHttpMessageHandler().Post(@"api\.gemini\.com/v1/order/new",
                """{"order_id":"106817811","is_cancelled":false,"executed_amount":"0"}""");
            var p = Gemini(h);

            string id = await p.PlaceOrderAsync(new TradeSignal("BTCUSD", OrderSide.Buy, 2,
                OrderType.Limit, Price: 95000.5));

            Assert.Equal("106817811", id);
            var req = OnlyVenueCall(h);
            Assert.Equal("", OnlyVenueBody(h)); // everything travels in the headers

            string b64 = req.Headers.GetValues("X-GEMINI-PAYLOAD").Single();
            var payload = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
            Assert.Equal("/v1/order/new", payload["request"]?.ToString());
            Assert.Equal("btcusd", payload["symbol"]?.ToString()); // venue wants lowercase
            Assert.Equal("buy", payload["side"]?.ToString());
            Assert.Equal("exchange limit", payload["type"]?.ToString());
            Assert.Equal("2", payload["amount"]?.ToString());
            Assert.Equal("95000.5", payload["price"]?.ToString());
            Assert.NotNull(payload["nonce"]);

            // HMAC-SHA384 over the base64 STRING as sent — not over the JSON.
            Assert.Equal(AccessibleTrader.Plugins.Gemini.GeminiAuth.Sign(b64, "gs"),
                         req.Headers.GetValues("X-GEMINI-SIGNATURE").Single());
            Assert.Equal("gk", req.Headers.GetValues("X-GEMINI-APIKEY").Single());
        }

        [Fact]
        public async Task Gemini_an_instantly_cancelled_unfilled_order_is_a_failure_not_an_id()
        {
            // HTTP 200 with a valid order id — but the order no longer exists.
            var h = new FakeHttpMessageHandler().Post(@"api\.gemini\.com/v1/order/new",
                """{"order_id":"106817812","is_cancelled":true,"executed_amount":"0"}""");
            var p = Gemini(h);

            string result = await p.PlaceOrderAsync(new TradeSignal("BTCUSD", OrderSide.Buy, 2,
                OrderType.Limit, Price: 95000.5, PostOnly: true));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Contains("without filling", result);
        }

        // ── Kraken Futures: Authent verifies over the exact body sent ────────

        private static readonly string KfSecret = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

        private static AccessibleTrader.Plugins.KrakenFutures.KrakenFuturesProvider KrakenFutures(FakeHttpMessageHandler h)
        {
            var p = new AccessibleTrader.Plugins.KrakenFutures.KrakenFuturesProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "kfk", ["ApiSecret"] = KfSecret });
            SwapField(p, "_http", new HttpClient(h));
            return p;
        }

        [Fact]
        public async Task KrakenFutures_limit_buy_signs_the_exact_body_it_sends()
        {
            var h = new FakeHttpMessageHandler().Post(@"futures\.kraken\.com/derivatives/api/v3/sendorder",
                """{"result":"success","sendStatus":{"order_id":"OID-1","status":"placed"}}""");
            var p = KrakenFutures(h);

            string id = await p.PlaceOrderAsync(new TradeSignal("PI_XBTUSD", OrderSide.Buy, 1,
                OrderType.Limit, Price: 50000));

            Assert.Equal("OID-1", id);
            var req = OnlyVenueCall(h);
            string body = OnlyVenueBody(h); // RAW — the same encoded string must be signed and sent
            var pairs = FormPairs(body);
            Assert.Equal("lmt", pairs["orderType"]);
            Assert.Equal("pi_xbtusd", pairs["symbol"]);
            Assert.Equal("buy", pairs["side"]);
            Assert.Equal("1", pairs["size"]);
            Assert.Equal("50000", pairs["limitPrice"]);

            // Authent = HMAC-SHA512(SHA256(body + nonce + path-without-/derivatives)).
            string nonce = req.Headers.GetValues("Nonce").Single();
            Assert.Equal(
                AccessibleTrader.Plugins.KrakenFutures.KrakenFuturesAuth.Sign(body, nonce, "/api/v3/sendorder", KfSecret),
                req.Headers.GetValues("Authent").Single());
            Assert.Equal("kfk", req.Headers.GetValues("APIKey").Single());
        }

        [Fact]
        public async Task KrakenFutures_a_200_with_a_non_placed_status_is_a_failure_naming_it()
        {
            // Rejections arrive as HTTP 200 with a status string; returning the id
            // blindly would report a rejected order as accepted.
            var h = new FakeHttpMessageHandler().Post(@"/derivatives/api/v3/sendorder",
                """{"result":"success","sendStatus":{"order_id":"OID-2","status":"insufficientAvailableFunds"}}""");
            var p = KrakenFutures(h);

            string result = await p.PlaceOrderAsync(new TradeSignal("PI_XBTUSD", OrderSide.Buy, 1000,
                OrderType.Market));

            Assert.Equal("ORDER_FAILED:insufficientAvailableFunds", result);
        }

        [Fact]
        public async Task KrakenFutures_post_only_limit_becomes_the_post_order_type()
        {
            var h = new FakeHttpMessageHandler().Post(@"/derivatives/api/v3/sendorder",
                """{"result":"success","sendStatus":{"order_id":"OID-3","status":"placed"}}""");
            var p = KrakenFutures(h);

            await p.PlaceOrderAsync(new TradeSignal("PI_XBTUSD", OrderSide.Buy, 1,
                OrderType.Limit, Price: 50000, PostOnly: true));

            var pairs = FormPairs(OnlyVenueBody(h));
            Assert.Equal("post", pairs["orderType"]); // the venue's spelling of maker-only
        }
    }
}
