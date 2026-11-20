using System.Net;
using System.Security.Cryptography;
using System.Text;
using AccessibleTrader.Plugins.Kalshi;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;
using AccessibleTrader.Tests.OrderConformance;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Kalshi perpetuals (2026-11-14). The shared order theories run it through
    /// <see cref="KalshiRig"/>; these pin what is Kalshi's own: the signature (checked with the
    /// public key, not by re-running our signer), prices per contract written to two decimals at
    /// least, a market order that is an immediate limit priced off the book, whole-contract sizes
    /// refused rather than cut, and the account reads.
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class KalshiTests
    {
        private const long Now = 1_763_100_000_000;

        private static (string Pem, Ed25519PublicKeyParameters Public) Ed25519Key()
        {
            var gen = new Ed25519KeyPairGenerator();
            gen.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
            var pair = gen.GenerateKeyPair();
            string pem = "-----BEGIN PRIVATE KEY-----\n"
                       + Convert.ToBase64String(PrivateKeyInfoFactory.CreatePrivateKeyInfo(pair.Private).GetDerEncoded())
                       + "\n-----END PRIVATE KEY-----\n";
            return (pem, (Ed25519PublicKeyParameters)pair.Public);
        }

        private static (KalshiProvider P, FakeHttpMessageHandler H) Build(string? pem = null, string? env = ProviderConfigKeys.Live,
            string apiKey = "key-id")
        {
            var h = new FakeHttpMessageHandler();
            var p = new KalshiProvider();
            var config = new Dictionary<string, string>();
            if (apiKey.Length > 0) { config["ApiKey"] = apiKey; config["ApiSecret"] = pem ?? KalshiRig.Pem; }
            if (env != null) config[ProviderConfigKeys.Environment] = env;
            p.Configure(config);
            typeof(KalshiProvider).GetField("_http", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(p, new HttpClient(h));
            p.NowMs = () => Now;
            return (p, h);
        }

        private static string Header(HttpRequestMessage r, string name) => r.Headers.GetValues(name).Single();

        private static bool VerifyEd25519(Ed25519PublicKeyParameters pub, string message, string b64)
        {
            var v = new Ed25519Signer();
            v.Init(false, pub);
            byte[] m = Encoding.UTF8.GetBytes(message);
            v.BlockUpdate(m, 0, m.Length);
            return v.VerifySignature(Convert.FromBase64String(b64));
        }

        // ── The signature ─────────────────────────────────────────────────────

        [Fact]
        public async Task An_Ed25519_key_signs_milliseconds_method_and_path_without_the_query()
        {
            var (pem, pub) = Ed25519Key();
            var (p, h) = Build(pem);
            h.Get(@"/margin/balance\?compute_available_balance=true$",
                """{"subaccount_balances":[{"available_balance":"10.00","account_equity":"12.50"}]}""");

            await p.GetBalancesAsync();

            var req = h.Captured.Single();
            Assert.Equal("external-api.kalshi.com", req.RequestUri!.Host);
            Assert.Equal("key-id", Header(req, "KALSHI-ACCESS-KEY"));
            Assert.Equal(Now.ToString(), Header(req, "KALSHI-ACCESS-TIMESTAMP"));
            string sig = Header(req, "KALSHI-ACCESS-SIGNATURE");
            Assert.True(VerifyEd25519(pub, $"{Now}GET/trade-api/v2/margin/balance", sig),
                "the signature must cover the time, GET and the path from /trade-api/v2 without its query");
            Assert.False(VerifyEd25519(pub, $"{Now}GET/trade-api/v2/margin/balance?compute_available_balance=true", sig));
        }

        [Fact]
        public async Task An_RSA_key_signs_with_PSS_and_SHA256()
        {
            using var rsa = RSA.Create(2048);
            var (p, h) = Build(rsa.ExportRSAPrivateKeyPem());
            h.Get(@"/margin/enabled$", """{"enabled":true}""");

            var (ok, _) = await p.ValidateApiKeyAsync();

            Assert.True(ok);
            var req = h.Captured.Single();
            Assert.True(rsa.VerifyData(Encoding.UTF8.GetBytes($"{Now}GET/trade-api/v2/margin/enabled"),
                Convert.FromBase64String(Header(req, "KALSHI-ACCESS-SIGNATURE")), HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        }

        /// <summary>
        /// Code review, 2026-11-20: Kalshi parsed its private key once at Configure and kept it for
        /// the life of the app, where the other venues ask the key store at each signed request. A
        /// key changed in the store after Configure must be the one that signs the next request.
        /// </summary>
        [Fact]
        public async Task Each_signed_request_checks_the_key_out_of_the_store()
        {
            var (first, firstPub) = Ed25519Key();
            var (second, secondPub) = Ed25519Key();
            var (p, h) = Build();   // configured with the rig's key, which neither check-out uses
            h.Get(@"/margin/enabled$", """{"enabled":true}""");

            using (new Fakes.FakeApiKeyCheckout { Key = "store-key-1", Secret = first }.Install())
                await p.ValidateApiKeyAsync();
            using (new Fakes.FakeApiKeyCheckout { Key = "store-key-2", Secret = second }.Install())
                await p.ValidateApiKeyAsync();

            Assert.Equal(2, h.Captured.Count);
            string message = $"{Now}GET/trade-api/v2/margin/enabled";
            Assert.Equal("store-key-1", Header(h.Captured[0], "KALSHI-ACCESS-KEY"));
            Assert.True(VerifyEd25519(firstPub, message, Header(h.Captured[0], "KALSHI-ACCESS-SIGNATURE")));
            Assert.Equal("store-key-2", Header(h.Captured[1], "KALSHI-ACCESS-KEY"));
            Assert.True(VerifyEd25519(secondPub, message, Header(h.Captured[1], "KALSHI-ACCESS-SIGNATURE")),
                "the second request must be signed with the key the store holds now, not one kept from before");
        }

        /// <summary>A key pasted into a one-line box loses its line breaks, or keeps them as the
        /// two characters \n; it must still sign.</summary>
        [Theory]
        [InlineData(" ")]
        [InlineData("\\n")]
        [InlineData("")]
        public void A_pasted_key_that_lost_its_line_breaks_still_signs(string newline)
        {
            var (pem, pub) = Ed25519Key();
            var signer = KalshiSigner.Parse(pem.Replace("\n", newline));
            Assert.True(signer.IsEd25519);
            Assert.True(VerifyEd25519(pub, "1GET/x", signer.Sign("1GET/x")));
        }

        [Fact]
        public async Task An_unreadable_key_is_said_in_words_and_sends_nothing()
        {
            var (p, h) = Build("not a key");

            var (ok, msg) = await p.ValidateApiKeyAsync();
            string order = await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", OrderSide.Buy, 1, OrderType.Limit, Price: 100));

            Assert.False(ok);
            Assert.Equal(KalshiSigner.UnreadableKey, msg);
            Assert.Equal("ORDER_FAILED:" + KalshiSigner.UnreadableKey, order);
            Assert.Empty(h.Captured);
        }

        [Fact]
        public async Task An_account_without_perpetuals_is_told_so_when_the_key_is_checked()
        {
            var (p, h) = Build();
            h.Get(@"/margin/enabled$", """{"enabled":false}""");

            var (ok, msg) = await p.ValidateApiKeyAsync();

            Assert.False(ok);
            Assert.Contains("not enabled on this account", msg);
            Assert.Contains("demo.kalshi.co", msg);
        }

        // ── Orders ────────────────────────────────────────────────────────────

        private static (KalshiProvider P, FakeHttpMessageHandler H) Trading(string? book = null, string? response = null,
            HttpStatusCode status = HttpStatusCode.Created, string? env = ProviderConfigKeys.Live)
        {
            var (p, h) = Build(env: env);
            if (book != null) h.Get(@"/margin/markets/[A-Z0-9]+/orderbook", book);
            KalshiRig.ArmMarkets(h);
            h.Post(@"/margin/orders$", response ?? """{"order_id":"o-1"}""", status);
            return (p, h);
        }

        private static JObject Sent(FakeHttpMessageHandler h) =>
            JObject.Parse(h.CapturedBodies[h.Captured.FindIndex(r => r.Method == HttpMethod.Post)]);

        private static int PostCount(FakeHttpMessageHandler h) => h.Captured.Count(r => r.Method == HttpMethod.Post);

        [Theory]
        [InlineData(OrderSide.Buy, "bid", "106.05")]    // best ask 101 + 5%
        [InlineData(OrderSide.Sell, "ask", "95.00")]    // best bid 100 − 5%, written with two decimals
        public async Task A_market_order_is_an_immediate_limit_five_percent_past_the_book(OrderSide side, string wireSide, string price)
        {
            var (p, h) = Trading();

            Assert.Equal("o-1", await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", side, 3, OrderType.Market)));

            var body = Sent(h);
            Assert.Equal(wireSide, body["side"]!.ToString());
            Assert.Equal(price, body["price"]!.ToString());
            Assert.Equal("3", body["count"]!.ToString());
            Assert.Equal("immediate_or_cancel", body["time_in_force"]!.ToString());
            Assert.Equal("taker_at_cross", body["self_trade_prevention_type"]!.ToString());
            Assert.False(string.IsNullOrEmpty(body["client_order_id"]?.ToString()));
        }

        [Fact]
        public async Task A_market_order_with_nobody_on_the_other_side_is_refused_unsent()
        {
            var (p, h) = Trading(book: """{"orderbook":{"asks":[],"bids":[["100.0000","5.00"]]}}""");

            string r = await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", OrderSide.Buy, 1, OrderType.Market));

            Assert.StartsWith("ORDER_FAILED:Nobody is selling KXBTCPERP", r);
            Assert.Equal(0, PostCount(h));
        }

        [Fact]
        public async Task A_limit_price_is_written_with_two_decimals_and_rounded_to_the_tick()
        {
            var (p, h) = Trading();

            await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", OrderSide.Buy, 2, OrderType.Limit, Price: 6.67881, PostOnly: true));

            var body = Sent(h);
            Assert.Equal("6.6788", body["price"]!.ToString());
            Assert.Equal("good_till_canceled", body["time_in_force"]!.ToString());
            Assert.True(body["post_only"]!.Value<bool>());
            Assert.Null(body["reduce_only"]);
        }

        [Theory]
        [InlineData("IOC", "immediate_or_cancel")]
        [InlineData("FOK", "fill_or_kill")]
        [InlineData("GTC", "good_till_canceled")]
        [InlineData(null, "good_till_canceled")]
        public async Task Time_in_force_is_Kalshis_word(string? tif, string wire)
        {
            var (p, h) = Trading(response: """{"order_id":"o-1","fill_count":"1.00"}""");

            await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", OrderSide.Buy, 1, OrderType.Limit, Price: 100, TimeInForce: tif));

            Assert.Equal(wire, Sent(h)["time_in_force"]!.ToString());
        }

        public static IEnumerable<object[]> RefusedUnsent() => new[]
        {
            new object[] { new TradeSignal("KXBTCPERP", OrderSide.Buy, 1, OrderType.Limit, Price: 100, Leverage: 5), "no leverage setting" },
            new object[] { new TradeSignal("KXBTCPERP", OrderSide.Sell, 1, OrderType.Limit, Price: 100, ReduceOnly: true), "only if it fills at once" },
            new object[] { new TradeSignal("KXBTCPERP", OrderSide.Sell, 1, OrderType.StopMarket, TriggerPrice: 90), "cannot open one" },
            new object[] { new TradeSignal("KXBTCPERP", OrderSide.Sell, 1, OrderType.StopLimit, Price: 89, TriggerPrice: 90, ReduceOnly: true), "takes no limit price" },
            new object[] { new TradeSignal("KXBTCPERP", OrderSide.Buy, 1, OrderType.Limit, Price: 100, StopLoss: 100), "must be below the price (100.00)" },
            new object[] { new TradeSignal("KXBTCPERP", OrderSide.Sell, 1, OrderType.Limit, Price: 100, TakeProfit: 101), "must be below the price (100.00)" },
            new object[] { new TradeSignal("KXBTCPERP", OrderSide.Buy, 1, OrderType.Limit, Price: 100, TimeInForce: "DAY"), "good-till-cancelled" },
            new object[] { new TradeSignal("KXBTCPERP", OrderSide.Buy, 1, OrderType.Market, PostOnly: true), "post-only" },
            new object[] { new TradeSignal("KXBTCPERP", OrderSide.Buy, 1.5, OrderType.Limit, Price: 100), "whole contracts; this order is 1.5 (one contract is 0.0001 BTC)" },
            new object[] { new TradeSignal("KXBTCPERP", OrderSide.Buy, 1, OrderType.Limit), "needs a price" },
        };

        [Theory]
        [MemberData(nameof(RefusedUnsent))]
        public async Task What_Kalshi_cannot_take_is_refused_in_words_before_anything_is_sent(TradeSignal signal, string words)
        {
            var (p, h) = Trading();

            string r = await p.PlaceOrderAsync(signal);

            Assert.StartsWith("ORDER_FAILED:", r);
            Assert.Contains(words, r);
            Assert.Equal(0, PostCount(h));
        }

        // Vacuity for the leverage row: 1 times is what every Kalshi order is, and it goes.
        [Fact]
        public async Task An_order_at_one_times_leverage_is_sent()
        {
            var (p, h) = Trading();
            Assert.Equal("o-1", await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", OrderSide.Buy, 1, OrderType.Limit, Price: 100, Leverage: 1)));
            Assert.Equal(1, PostCount(h));
        }

        [Fact]
        public void Market_rules_read_steps_fractions_and_a_closed_market()
        {
            var frac = KalshiProvider.ReadMarketRules(JObject.Parse(
                """{"ticker":"KXAAPLPERP1","status":"active","title":"AAPL","tick_size":"0.01","minimum_quantity":"0.01","fractional_trading_enabled":true}"""))!;
            Assert.Null(KalshiProvider.SizeRefusal(frac, 0.25));
            Assert.Equal("Kalshi trades KXAAPLPERP1 in steps of 0.01 contracts; this order is 0.255",
                KalshiProvider.SizeRefusal(frac, 0.255));

            var closed = KalshiProvider.ReadMarketRules(JObject.Parse(
                """{"ticker":"KXOLDPERP","status":"inactive","title":"0.1 OLD","tick_size":"0.01","minimum_quantity":"1.00","fractional_trading_enabled":false}"""))!;
            Assert.Equal("Kalshi lists KXOLDPERP as inactive; it takes no orders", KalshiProvider.SizeRefusal(closed, 1));
        }

        [Fact]
        public async Task An_immediate_order_that_filled_nothing_is_a_failure_not_an_open_order()
        {
            var (p, _) = Trading(response: """{"order_id":"o-1","fill_count":"0.00","remaining_count":"0.00"}""");

            string r = await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", OrderSide.Buy, 1, OrderType.Market));

            Assert.StartsWith("ORDER_FAILED:Kalshi filled none of it", r);
        }

        [Fact]
        public async Task A_resting_limit_with_nothing_filled_yet_is_an_order()
        {
            var (p, _) = Trading(response: """{"order_id":"o-1","fill_count":"0.00","remaining_count":"1.00"}""");
            Assert.Equal("o-1", await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", OrderSide.Buy, 1, OrderType.Limit, Price: 100)));
        }

        [Theory]
        [InlineData(HttpStatusCode.Conflict, "stock_perps_application_required", "stocks application")]
        [InlineData(HttpStatusCode.BadRequest, "invalid_price", "inside the market's price band")]
        [InlineData(HttpStatusCode.Unauthorized, "token_authentication_failure", "clock is right")]
        [InlineData(HttpStatusCode.BadRequest, "something_new", "Kalshi refused: venue words (something_new)")]
        public async Task A_refusal_comes_back_in_words(HttpStatusCode status, string code, string words)
        {
            var (p, _) = Trading(response: $$$"""{"error":{"code":"{{{code}}}","message":"venue words","details":""}}""", status: status);

            string r = await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", OrderSide.Buy, 1, OrderType.Limit, Price: 100));

            Assert.StartsWith("ORDER_FAILED:", r);
            Assert.Contains(words, r);
        }

        [Fact]
        public async Task A_practice_credential_reads_its_rules_and_book_from_the_demo_venue()
        {
            var (p, h) = Trading(env: ProviderConfigKeys.Paper);

            await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", OrderSide.Buy, 1, OrderType.Market));

            Assert.All(h.Captured, r => Assert.Equal("external-api.demo.kalshi.co", r.RequestUri!.Host));
        }

        // ── Market data ───────────────────────────────────────────────────────

        [Fact]
        public async Task The_book_is_sorted_best_first_whatever_order_Kalshi_sends()
        {
            var (p, h) = Build(apiKey: "");
            // As the live venue sent it on 2026-11-14: asks highest first, bids lowest first, and
            // Kalshi's "no price" number on the ask side.
            h.Get(@"/margin/markets/KXBTCPERP/orderbook\?depth=3",
                """{"orderbook":{"asks":[["922337203685477.5807","1.00"],["6.6790","2.00"],["6.6788","59.00"]],"bids":[["6.6700","1.00"],["6.6750","4.00"],["6.6780","3.00"]]}}""");

            var (bids, asks) = await p.GetOrderBookAsync("KXBTCPERP", 3);

            Assert.Equal(new[] { 6.678, 6.675, 6.67 }, bids.Select(b => b.Price));
            Assert.Equal(new[] { 6.6788, 6.679 }, asks.Select(a => a.Price));
            Assert.Equal("external-api.kalshi.com", h.Captured.Single().RequestUri!.Host);
        }

        [Fact]
        public async Task Candles_ask_in_seconds_open_a_period_before_their_end_and_skip_empty_periods()
        {
            var (p, h) = Build(apiKey: "");
            h.Get(@"/margin/markets/KXBTCPERP/candlesticks\?", """
                {"ticker":"KXBTCPERP","candlesticks":[
                  {"end_period_ts":1763096400,"price":{"open":"6.60","high":"6.70","low":"6.50","close":"6.65"},"volume":"12.00"},
                  {"end_period_ts":1763100000,"price":{"open":null,"high":null,"low":null,"close":null},"volume":"0.00"}]}
                """);

            var (bars, vols) = await p.FetchOhlcvAsync(new MarketDataRequest("Crypto", "KXBTCPERP", "1h", 2,
                Since: 1763092800_000, Until: 1763100000_000));

            var q = System.Web.HttpUtility.ParseQueryString(h.Captured.Single().RequestUri!.Query);
            Assert.Equal("1763092800", q["start_ts"]);
            Assert.Equal("1763100000", q["end_ts"]);
            Assert.Equal("60", q["period_interval"]);
            var bar = Assert.Single(bars);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1763092800).UtcDateTime, bar.Date);
            Assert.Equal(6.65, bar.Close);
            Assert.Equal((1763092800L, 12.0), vols.Single());
        }

        [Fact]
        public async Task A_long_window_is_asked_for_in_pieces_Kalshi_will_answer()
        {
            var (p, h) = Build(apiKey: "");
            h.Get(@"/candlesticks\?", """{"candlesticks":[]}""");
            long until = 1763100000;
            long since = until - 12_000 * 60;    // 12,000 one-minute candles

            await p.FetchOhlcvAsync(new MarketDataRequest("Crypto", "KXBTCPERP", "1m", 12_000,
                Since: since * 1000, Until: until * 1000));

            var spans = h.Captured.Select(r => System.Web.HttpUtility.ParseQueryString(r.RequestUri!.Query))
                .Select(q => (Start: long.Parse(q["start_ts"]!), End: long.Parse(q["end_ts"]!))).ToList();
            Assert.Equal(3, spans.Count);
            Assert.All(spans, s => Assert.True(s.End - s.Start <= KalshiProvider.MaxCandlesPerCall * 60));
            Assert.Equal(since, spans[0].Start);
            Assert.Equal(until, spans[^1].End);
            for (int i = 1; i < spans.Count; i++) Assert.Equal(spans[i - 1].End, spans[i].Start);
        }

        [Fact]
        public async Task The_market_list_is_active_markets_of_the_asked_kind()
        {
            var (p, h) = Build(apiKey: "");
            h.Get(@"/margin/markets$", """
                {"markets":[
                  {"ticker":"KXBTCPERP","status":"active","asset_class":"Crypto"},
                  {"ticker":"KXGOLDPERP","status":"active","asset_class":"Metals"},
                  {"ticker":"KXWTIPERP","status":"active","asset_class":"Energy"},
                  {"ticker":"KXOLDPERP","status":"closed","asset_class":"Metals"}],"cursor":""}
                """);

            Assert.Equal(new[] { "KXGOLDPERP", "KXWTIPERP" }, await p.GetAvailableSymbolsAsync(MarketType.Commodity));
            Assert.Equal(new[] { "KXBTCPERP" }, await p.GetAvailableSymbolsAsync(MarketType.Crypto));
        }

        [Fact]
        public async Task A_demo_credential_charts_the_demo_venue_and_no_credential_charts_the_live_one()
        {
            var (demo, hd) = Build(env: ProviderConfigKeys.Paper);
            hd.Get(@"/margin/markets$", """{"markets":[]}""");
            await demo.GetAvailableSymbolsAsync(MarketType.Crypto);
            Assert.Equal("external-api.demo.kalshi.co", hd.Captured.Single().RequestUri!.Host);

            var (none, hn) = Build(apiKey: "", env: null);
            hn.Get(@"/margin/markets$", """{"markets":[]}""");
            await none.GetAvailableSymbolsAsync(MarketType.Crypto);
            Assert.Equal("external-api.kalshi.com", hn.Captured.Single().RequestUri!.Host);
        }

        // ── The account ───────────────────────────────────────────────────────

        [Fact]
        public void Balance_is_available_dollars_and_the_rest_of_the_equity_as_held()
        {
            var b = KalshiProvider.ReadBalance(JObject.Parse(
                """{"subaccount_balances":[{"available_balance":"10.00","account_equity":"12.50"},{"available_balance":"1.00","account_equity":"1.00"}]}"""));
            Assert.Equal(("USD", 11.0, 2.5), (b.Asset, b.Free, b.Locked));
        }

        [Fact]
        public void Positions_take_mark_and_liquidation_from_the_risk_answer_and_leverage_from_the_margin_held()
        {
            var positions = JObject.Parse("""
                {"positions":[
                  {"market_ticker":"KXBTCPERP","subaccount_type":"cross","position":"-30.00","entry_price":"6.6000","unrealized_pnl":"-1.20","margin_used":"39.60"},
                  {"market_ticker":"KXGOLDPERP","subaccount_type":"isolated","position":"2.00","entry_price":"40.0000","unrealized_pnl":"0.50","margin_used":null},
                  {"market_ticker":"KXETHPERP","subaccount_type":"cross","position":"0.00","entry_price":"0","unrealized_pnl":"0"}]}
                """);
            var risk = JObject.Parse("""
                {"positions":[{"market_ticker":"KXBTCPERP","mark_price":"6.6400","estimated_liquidation_price":"9.1000","position_leverage":"40"}]}
                """);

            var list = KalshiProvider.ReadPositions(positions, risk);

            Assert.Equal(2, list.Count);
            var btc = list[0];
            Assert.Equal(("KXBTCPERP", -30.0, 6.6, -1.2), (btc.Symbol, btc.Quantity, btc.AveragePrice, btc.UnrealizedPnL));
            Assert.Equal(-30 * 6.64, btc.MarketValue, 9);
            Assert.Equal(5.0, btc.Leverage, 9);          // 30 × 6.60 / 39.60, not the maintenance-based 40
            Assert.Equal(9.1, btc.LiquidationPrice);
            Assert.Equal(MarginMode.Cross, btc.MarginMode);

            var gold = list[1];
            Assert.Equal((80.0, 1.0, 0.0, MarginMode.Isolated), (gold.MarketValue, gold.Leverage, gold.LiquidationPrice, gold.MarginMode));
        }

        /// <summary>The order contract: a trading read that fails says so. Positions without their
        /// liquidation price would read as positions that have none.</summary>
        [Fact]
        public async Task A_failed_risk_answer_fails_the_positions_read()
        {
            var (p, h) = Build();
            h.Get(@"/margin/positions$", """{"positions":[{"market_ticker":"KXBTCPERP","position":"3.00","entry_price":"6.60","unrealized_pnl":"0"}]}""");
            h.Get(@"/margin/risk$", """{"error":{"code":"internal","message":"boom"}}""", HttpStatusCode.InternalServerError);

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => p.GetPositionsAsync());
            Assert.Contains("/margin/risk", ex.Message);
            Assert.DoesNotContain("kalshi.com", ex.Message);    // the path, never the URL
        }

        [Fact]
        public void Open_orders_are_those_with_something_left_and_fills_carry_fees_and_profit()
        {
            var orders = KalshiProvider.ReadOpenOrders(JObject.Parse("""
                {"orders":[{"order_id":"a","ticker":"KXBTCPERP","side":"ask","price":"6.70","remaining_count":"2.00"},
                           {"order_id":"b","ticker":"KXBTCPERP","side":"bid","price":"6.50","remaining_count":"0.00"}]}
                """)).ToList();
            var o = Assert.Single(orders);
            Assert.Equal(("a", OrderSide.Sell, 2.0, 6.7), (o.Id, o.Side, o.Quantity, o.Price));

            var f = Assert.Single(KalshiProvider.ReadFills(JObject.Parse("""
                {"fills":[{"fill_id":"f","order_id":"a","ticker":"KXBTCPERP","side":"bid","count":"3.00","price":"6.60","fees":"0.02","realized_pnl":"0.40","created_time":"2026-11-14T10:00:00Z"}]}
                """)));
            Assert.Equal((OrderSide.Buy, 3.0, 6.6, 0.02, 0.4, "a"), (f.Side, f.Quantity, f.Price, f.Fee, f.RealizedPnL, f.OrderId));
            Assert.Equal(new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc), f.FilledAt);
        }

        [Fact]
        public async Task Cancel_deletes_the_order_by_id()
        {
            var (p, h) = Build();
            h.Add(HttpMethod.Delete, @"/margin/orders/o-9$", """{"order_id":"o-9","reduced_by":"2.00"}""");
            Assert.True(await p.CancelOrderAsync("o-9", "KXBTCPERP"));

            var (p2, h2) = Build();
            h2.Add(HttpMethod.Delete, @"/margin/orders/o-9$", """{"error":{"code":"not_found","message":"order not found"}}""", HttpStatusCode.NotFound);
            Assert.False(await p2.CancelOrderAsync("o-9", "KXBTCPERP"));
        }

        [Fact]
        public async Task Leverage_is_never_pretended()
        {
            var (p, h) = Build();
            Assert.Equal(1.0, await p.SetLeverageAsync("KXBTCPERP", 10));
            Assert.Equal(1.0, p.MaxLeverage);
            Assert.False(p.Capabilities.HasFlag(ProviderCapabilities.Leverage));
            Assert.Empty(h.Captured);
        }
    

        // ── Stop loss and take profit: Kalshi's exit triggers (2026-11-15) ─────────

        private const string LongCross = """{"positions":[{"market_ticker":"KXBTCPERP","subaccount_type":"cross","position":"3.00","entry_price":"100","unrealized_pnl":"0"}]}""";
        private const string LongIsolated = """{"positions":[{"market_ticker":"KXBTCPERP","subaccount_type":"isolated","position":"3.00","entry_price":"100","unrealized_pnl":"0"}]}""";
        private const string Cross = "/margin/cross/positions/KXBTCPERP/exit_trigger";
        private const string Isolated = "/margin/isolated/positions/KXBTCPERP/exit_trigger";

        private static string Bracket(string id, string? sl, string? tp, string count = "0", string status = "active", string? anchor = null) =>
            new JObject
            {
                ["id"] = id, ["ticker"] = "KXBTCPERP", ["kind"] = "bracket", ["status"] = status, ["count"] = count, ["filled_count"] = "0",
                ["stop_loss_price"] = sl, ["take_profit_price"] = tp, ["anchor_order_id"] = anchor,
                ["created_time"] = "2026-11-15T00:00:00Z", ["updated_time"] = "2026-11-15T00:00:00Z",
            }.ToString(Newtonsoft.Json.Formatting.None);

        private static string Triggers(params string[] t) => "{\"exit_triggers\":[" + string.Join(",", t) + "]}";

        /// <summary>The writes the plugin sent, in order: method, path with query, body.</summary>
        private static List<(string Method, string Path, JObject? Body)> Writes(FakeHttpMessageHandler h) =>
            h.Captured.Select((r, i) => (r, i)).Where(x => x.r.Method != HttpMethod.Get)
                .Select(x => (x.r.Method.Method, x.r.RequestUri!.PathAndQuery.Replace("/trade-api/v2", ""),
                              h.CapturedBodies[x.i].Length > 0 ? JObject.Parse(h.CapturedBodies[x.i]) : null))
                .ToList();

        private static List<(string Method, string Path, JObject? Body)> TriggerWrites(FakeHttpMessageHandler h) =>
            Writes(h).Where(w => w.Path.Contains("exit_trigger")).ToList();

        private static List<ProviderError> Errors(KalshiProvider p)
        {
            var list = new List<ProviderError>();
            p.ProviderErrors.Subscribe(list.Add);
            return list;
        }

        private static (KalshiProvider P, FakeHttpMessageHandler H) Entry(string fill, string positions = LongCross, string triggers = """{"exit_triggers":[]}""",
            HttpStatusCode setStatus = HttpStatusCode.OK, string setAnswer = "")
        {
            var (p, h) = Trading(response: $$"""{"order_id":"o-1","fill_count":"{{fill}}"}""");
            h.Get(@"/margin/positions$", positions);
            h.Get(@"/exit_trigger$", triggers);
            h.Add(HttpMethod.Put, @"/exit_trigger", setAnswer.Length > 0 ? setAnswer : Bracket("b-new", "95.00", "110.00"), setStatus);
            return (p, h);
        }

        [Fact]
        public async Task A_filled_entry_sets_its_stop_and_target_over_the_whole_new_position()
        {
            var (p, h) = Entry(fill: "3.00");
            var errors = Errors(p);

            Assert.Equal("o-1", await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", OrderSide.Buy, 3, OrderType.Market, StopLoss: 95, TakeProfit: 110.004)));

            var w = Assert.Single(TriggerWrites(h));
            Assert.Equal(("PUT", Cross), (w.Method, w.Path));
            Assert.Equal("95.00", w.Body!["stop_loss_price"]!.ToString());
            Assert.Equal("110.004", w.Body["take_profit_price"]!.ToString());   // to the 0.0001 tick
            Assert.Null(w.Body["count"]);          // over all of it: the entry is the whole position
            Assert.Null(w.Body["anchor_order_id"]);
            Assert.Empty(errors);
            // The entry went first: a bracket is set on a position, so it cannot go before the fill.
            Assert.True(h.Captured.FindIndex(r => r.Method == HttpMethod.Post) < h.Captured.FindIndex(r => r.Method == HttpMethod.Put));
        }

        [Fact]
        public async Task An_entry_that_added_to_a_position_brackets_only_what_it_filled()
        {
            var (p, h) = Entry(fill: "2.00", positions: LongCross.Replace("3.00", "5.00"));

            await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", OrderSide.Buy, 2, OrderType.Market, StopLoss: 95));

            var w = Assert.Single(TriggerWrites(h));
            Assert.Equal("2", w.Body!["count"]!.ToString());
            Assert.Equal("at-o-1", w.Body["client_trigger_id"]!.ToString());
            Assert.Null(w.Body["take_profit_price"]);
        }

        [Fact]
        public async Task A_bracket_over_the_whole_position_is_moved_to_the_entrys_levels_and_that_is_said()
        {
            var (p, h) = Entry(fill: "2.00", positions: LongCross.Replace("3.00", "5.00"), triggers: Triggers(Bracket("b-1", "90.00", "120.00")));
            var errors = Errors(p);

            await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", OrderSide.Buy, 2, OrderType.Market, StopLoss: 95));

            var w = Assert.Single(TriggerWrites(h));
            Assert.Equal(("PUT", Cross + "/b-1"), (w.Method, w.Path));
            Assert.Equal("95.00", w.Body!["stop_loss_price"]!.ToString());
            Assert.Equal("120.00", w.Body["take_profit_price"]!.ToString());   // the target the entry did not set is kept
            Assert.Contains(errors, e => e.Message.Contains("one stop loss and take profit over the whole KXBTCPERP position")
                                      && e.Message.Contains("stop loss 95.00, take profit 120.00"));
        }

        [Fact]
        public async Task An_isolated_position_takes_its_bracket_on_the_isolated_route()
        {
            var (p, h) = Entry(fill: "3.00", positions: LongIsolated);

            await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", OrderSide.Buy, 3, OrderType.Market, StopLoss: 95));

            var w = Assert.Single(TriggerWrites(h));
            Assert.Equal(("PUT", Isolated), (w.Method, w.Path));
            Assert.Null(w.Body!["count"]);
        }

        [Fact]
        public async Task A_resting_entry_gets_a_bracket_tied_to_the_order()
        {
            var (p, h) = Entry(fill: "0.00");

            Assert.Equal("o-1", await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", OrderSide.Buy, 3, OrderType.Limit, Price: 99, StopLoss: 95, TakeProfit: 110)));

            var w = Assert.Single(TriggerWrites(h));
            Assert.Equal(("PUT", Cross), (w.Method, w.Path));
            Assert.Equal("o-1", w.Body!["anchor_order_id"]!.ToString());
            Assert.Equal("at-o-1", w.Body["client_trigger_id"]!.ToString());
            Assert.Null(w.Body["count"]);
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest, """{"error":{"code":"stop_loss_past_liquidation","message":"x"}}""", 1, "liquidated before it fired")]
        [InlineData(HttpStatusCode.InternalServerError, """{"error":{"code":"internal","message":"boom"}}""", 2, "boom")]
        public async Task A_bracket_Kalshi_refuses_leaves_the_entry_and_says_the_position_is_unprotected(
            HttpStatusCode status, string answer, int tries, string words)
        {
            var (p, h) = Entry(fill: "3.00", setStatus: status, setAnswer: answer);
            var errors = Errors(p);

            Assert.Equal("o-1", await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", OrderSide.Buy, 3, OrderType.Market, StopLoss: 95)));

            Assert.Equal(tries, TriggerWrites(h).Count);   // a refusal is not retried; a server fault is, once
            var e = Assert.Single(errors);
            Assert.Equal(ErrorSeverity.High, e.Severity);
            Assert.StartsWith("POSITION UNPROTECTED: Kalshi filled the KXBTCPERP order but did not take its stop loss", e.Message);
            Assert.Contains(words, e.Message);
        }

        [Fact]
        public async Task A_resting_entry_whose_bracket_is_refused_says_the_order_rests_without_it()
        {
            var (p, h) = Entry(fill: "0.00", setStatus: HttpStatusCode.Conflict,
                setAnswer: """{"error":{"code":"anchor_order_unavailable","message":"x"}}""");
            var errors = Errors(p);

            await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", OrderSide.Buy, 3, OrderType.Limit, Price: 99, TakeProfit: 110));

            var e = Assert.Single(errors);
            Assert.Equal(ErrorSeverity.High, e.Severity);
            Assert.StartsWith("ORDER WITHOUT ITS TAKE PROFIT: Kalshi took the KXBTCPERP order", e.Message);
        }

        // Vacuity for the entry rows: an entry with no stop or target touches no trigger.
        [Fact]
        public async Task An_entry_without_a_stop_or_target_sets_no_trigger()
        {
            var (p, h) = Entry(fill: "3.00");
            await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", OrderSide.Buy, 3, OrderType.Market));
            Assert.Empty(TriggerWrites(h));
        }

        [Fact]
        public void Kalshi_says_it_attaches_a_stop_to_its_market_and_limit_entries()
        {
            ITradingProvider p = new KalshiProvider();
            Assert.True(p.AttachesProtectiveLegs(new TradeSignal("KXBTCPERP", OrderSide.Buy, 1, OrderType.Market, StopLoss: 95)));
            Assert.True(p.AttachesProtectiveLegs(new TradeSignal("KXBTCPERP", OrderSide.Buy, 1, OrderType.Limit, Price: 99, StopLoss: 95)));
            Assert.True(p.Capabilities.HasFlag(ProviderCapabilities.Brackets));
            Assert.False(p.Capabilities.HasFlag(ProviderCapabilities.TrailingStop));
        }

        // ── A reduce-only stop or take-profit order: one leg of the position's bracket ──

        private static (KalshiProvider P, FakeHttpMessageHandler H) Legs(string positions, string triggers, string answer)
        {
            var (p, h) = Build();
            KalshiRig.ArmMarkets(h);
            h.Get(@"/margin/positions$", positions);
            h.Get(@"/exit_trigger$", triggers);
            h.Add(HttpMethod.Put, @"/exit_trigger", answer);
            h.Add(HttpMethod.Delete, @"/exit_trigger", "");
            return (p, h);
        }

        private static TradeSignal StopOrder(double at, OrderSide side = OrderSide.Sell, double qty = 3) =>
            new("KXBTCPERP", side, qty, OrderType.StopMarket, TriggerPrice: at, ReduceOnly: true);

        [Fact]
        public async Task A_stop_order_moves_the_stop_of_the_positions_bracket_and_keeps_its_target()
        {
            var (p, h) = Legs(LongCross, Triggers(Bracket("b-1", "95.00", "110.00")), Bracket("b-1", "97.00", "110.00"));

            Assert.Equal("xt:c:KXBTCPERP:b-1:sl:97.00", await p.PlaceOrderAsync(StopOrder(97)));

            var w = Assert.Single(TriggerWrites(h));
            Assert.Equal(("PUT", Cross + "/b-1"), (w.Method, w.Path));
            Assert.Equal(("97.00", "110.00"), (w.Body!["stop_loss_price"]!.ToString(), w.Body["take_profit_price"]!.ToString()));
        }

        [Fact]
        public async Task A_target_order_on_a_position_with_no_bracket_starts_one_with_only_the_target()
        {
            var (p, h) = Legs(LongCross, Triggers(), Bracket("b-2", null, "110.00"));

            Assert.Equal("xt:c:KXBTCPERP:b-2:tp:110.00",
                await p.PlaceOrderAsync(new TradeSignal("KXBTCPERP", OrderSide.Sell, 3, OrderType.TakeProfitMarket, TriggerPrice: 110, ReduceOnly: true)));

            var w = Assert.Single(TriggerWrites(h));
            Assert.Equal(("PUT", Cross), (w.Method, w.Path));
            Assert.Null(w.Body!["stop_loss_price"]);
            Assert.Null(w.Body["count"]);
        }

        [Fact]
        public async Task A_stop_for_part_of_the_position_is_its_own_bracket_of_that_size()
        {
            var (p, h) = Legs(LongCross, Triggers(Bracket("b-1", "95.00", null)), Bracket("b-2", "96.00", null, count: "1"));

            await p.PlaceOrderAsync(StopOrder(96, qty: 1));

            var w = Assert.Single(TriggerWrites(h));
            Assert.Equal(("PUT", Cross), (w.Method, w.Path));
            Assert.Equal("1", w.Body!["count"]!.ToString());
            Assert.False(string.IsNullOrEmpty(w.Body["client_trigger_id"]?.ToString()));
        }

        public static IEnumerable<object[]> LegRefusals() => new[]
        {
            new object[] { """{"positions":[]}""", StopOrder(97), "there is no KXBTCPERP position" },
            new object[] { LongCross, StopOrder(97, OrderSide.Buy), "position is long, so its stop loss sells; this order buys" },
            new object[] { LongIsolated, StopOrder(97, qty: 1), "close all of it (3); this order is for 1" },
        };

        [Theory]
        [MemberData(nameof(LegRefusals))]
        public async Task A_stop_Kalshi_could_not_set_as_asked_is_refused_unsent(string positions, TradeSignal signal, string words)
        {
            var (p, h) = Legs(positions, Triggers(), Bracket("b-1", "97.00", null));

            string r = await p.PlaceOrderAsync(signal);

            Assert.StartsWith("ORDER_FAILED:", r);
            Assert.Contains(words, r);
            Assert.Empty(TriggerWrites(h));
        }

        // ── Listing and cancelling the legs ──

        [Fact]
        public async Task Each_live_leg_is_listed_as_a_stop_or_target_order_that_closes_the_position()
        {
            var (p, h) = Build();
            h.Get(@"/margin/orders\?", """{"orders":[{"order_id":"o-2","ticker":"KXETHPERP","side":"bid","price":"2.00","remaining_count":"4.00"}],"cursor":""}""");
            h.Get(@"/margin/positions$", LongCross.Replace("3.00", "-3.00"));
            h.Get(@"/margin/cross/positions/KXBTCPERP/exit_trigger$", Triggers(Bracket("b-1", "105.00", "90.00"),
                Bracket("b-x", "1.00", null, status: "canceled")));
            h.Get(@"/margin/cross/positions/KXETHPERP/exit_trigger$", Triggers(Bracket("b-3", "1.50", null, status: "pending_on_entry", anchor: "o-2")));

            var all = await p.GetOpenOrdersAsync();

            Assert.Equal(4, all.Count);
            var stop = all.Single(o => o.Id == "xt:c:KXBTCPERP:b-1:sl:105.00");
            Assert.Equal((OrderSide.Buy, OrderType.StopMarket, 3.0, 105.0, 105.0), (stop.Side, stop.Type, stop.Quantity, stop.Price, stop.StopLoss));
            var target = all.Single(o => o.Id == "xt:c:KXBTCPERP:b-1:tp:90.00");
            Assert.Equal((OrderSide.Buy, OrderType.TakeProfitMarket, 90.0), (target.Side, target.Type, target.TakeProfit));
            // Waiting on its order: the stop of a buy that has not filled yet sells, for the order's size.
            var pending = all.Single(o => o.Id == "xt:c:KXETHPERP:b-3:sl:1.50");
            Assert.Equal((OrderSide.Sell, 4.0, "pending_on_entry"), (pending.Side, pending.Quantity, pending.Status));

            Assert.Equal(2, (await p.GetOpenOrdersAsync("KXBTCPERP")).Count(o => o.Id.StartsWith("xt:")));
        }

        [Fact]
        public async Task A_failed_trigger_read_fails_the_open_orders_read()
        {
            var (p, h) = Build();
            h.Get(@"/margin/orders\?", """{"orders":[],"cursor":""}""");
            h.Get(@"/margin/positions$", LongCross);
            h.Get(@"/exit_trigger$", """{"error":{"code":"internal","message":"boom"}}""", HttpStatusCode.InternalServerError);

            await Assert.ThrowsAsync<HttpRequestException>(() => p.GetOpenOrdersAsync("KXBTCPERP"));
        }

        [Theory]
        [InlineData("xt:c:KXBTCPERP:b-1:sl:95.00", "PUT", Cross + "/b-1", null, "110.00")]   // the target stays
        [InlineData("xt:c:KXBTCPERP:b-1:tp:110.00", "PUT", Cross + "/b-1", "95.00", null)]   // the stop stays
        [InlineData("xt:i:KXBTCPERP:b-1:sl:95.00", "PUT", Isolated, null, "110.00")]
        public async Task Cancelling_one_leg_keeps_the_other(string id, string method, string path, string? sl, string? tp)
        {
            var (p, h) = Legs(LongCross, Triggers(Bracket("b-1", "95.00", "110.00")), Bracket("b-1", sl, tp));

            Assert.True(await p.CancelOrderAsync(id, "KXBTCPERP"));

            var w = Assert.Single(TriggerWrites(h));
            Assert.Equal((method, path), (w.Method, w.Path));
            Assert.Equal(sl, w.Body!["stop_loss_price"]?.ToString());
            Assert.Equal(tp, w.Body["take_profit_price"]?.ToString());
        }

        [Theory]
        [InlineData("xt:c:KXBTCPERP:b-1:sl:95.00", Cross + "/b-1")]
        [InlineData("xt:i:KXBTCPERP:b-1:sl:95.00", Isolated + "?kind=bracket")]
        public async Task Cancelling_the_last_leg_cancels_the_bracket(string id, string path)
        {
            var (p, h) = Legs(LongCross, Triggers(Bracket("b-1", "95.00", null)), "");

            Assert.True(await p.CancelOrderAsync(id, "KXBTCPERP"));

            Assert.Equal(("DELETE", path), (TriggerWrites(h).Single().Method, TriggerWrites(h).Single().Path));
        }

        [Theory]
        [InlineData("xt:c:KXBTCPERP:b-1:sl:95.00")]   // the stop has moved to 97: the move replaced it
        [InlineData("xt:c:KXBTCPERP:b-9:sl:97.00")]   // that bracket fired or was cancelled
        public async Task A_leg_no_longer_live_at_its_price_is_already_cancelled_and_nothing_is_sent(string id)
        {
            var (p, h) = Legs(LongCross, Triggers(Bracket("b-1", "97.00", "110.00")), "");

            Assert.True(await p.CancelOrderAsync(id, "KXBTCPERP"));

            Assert.Empty(TriggerWrites(h));
        }

        // ── The Positions tab, end to end: place the new stop, then cancel the old ──

        /// <summary>Kalshi's trigger routes for one cross position, kept as state.</summary>
        private sealed class TriggerVenue
        {
            public readonly Dictionary<string, (string? Sl, string? Tp)> Live = new();
            private int _next;

            public void Arm(FakeHttpMessageHandler h)
            {
                KalshiRig.ArmMarkets(h);
                h.Get(@"/margin/orders\?", """{"orders":[],"cursor":""}""");
                h.Get(@"/margin/positions$", LongCross);
                h.Add(HttpMethod.Get, @"/exit_trigger$", _ => Json(Triggers(Live.Select(kv => Bracket(kv.Key, kv.Value.Sl, kv.Value.Tp)).ToArray())));
                h.Add(HttpMethod.Put, @"/exit_trigger(/[^/?]+)?$", r =>
                {
                    var body = JObject.Parse(r.Content!.ReadAsStringAsync().Result);
                    string? sl = body["stop_loss_price"]?.ToString(), tp = body["take_profit_price"]?.ToString();
                    string last = r.RequestUri!.AbsolutePath.Split('/')[^1];
                    string id = last == "exit_trigger" ? "b-" + ++_next : last;
                    Live[id] = (sl, tp);   // a leg left out is cleared
                    return Json(Bracket(id, sl, tp));
                });
                h.Add(HttpMethod.Delete, @"/exit_trigger/[^/?]+$", r =>
                {
                    Live.Remove(r.RequestUri!.AbsolutePath.Split('/')[^1]);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                });
            }

            private static HttpResponseMessage Json(string s) =>
                new(HttpStatusCode.OK) { Content = new StringContent(s, Encoding.UTF8, "application/json") };
        }

        private static AccessibleTrader.Core.Services.GeneralOrderService OrderService(KalshiProvider kalshi)
        {
            var data = NSubstitute.Substitute.For<AccessibleTrader.Core.Services.IDataService>();
            NSubstitute.SubstituteExtensions.Returns(data.GetProviderAsync(NSubstitute.Arg.Any<string>()),
                _ => Task.FromResult<IMarketDataProvider?>(kalshi));
            var paper = NSubstitute.Substitute.For<AccessibleTrader.Core.Services.IPaperTradingProvider>();
            NSubstitute.SubstituteExtensions.Returns(paper.OrderUpdateStream, System.Reactive.Linq.Observable.Never<AccessibleTrader.Sdk.Trading.OrderUpdate>());
            var svc = new AccessibleTrader.Core.Services.GeneralOrderService(data,
                NSubstitute.Substitute.For<AccessibleTrader.Core.Services.Accessibility.IGlobalErrorCoordinator>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AccessibleTrader.Core.Services.GeneralOrderService>.Instance,
                new AccessibleTrader.Core.Services.EventBus(), paper,
                NSubstitute.Substitute.For<AccessibleTrader.Core.Services.ISettingsManager>(),
                new AccessibleTrader.Core.Services.DemoPolicy(isDemo: false), new AccessibleTrader.Core.Services.Trading.QuickTradeEquity());
            svc.ProtectionVerifyDelay = TimeSpan.Zero;
            return svc;
        }

        [Fact]
        public async Task Moving_the_stop_on_the_Positions_tab_moves_it_and_keeps_the_target()
        {
            var venue = new TriggerVenue();
            venue.Live["b-1"] = ("95.00", "110.00");
            var (p, h) = Build();
            venue.Arm(h);
            var svc = OrderService(p);
            var open = await p.GetOpenOrdersAsync("KXBTCPERP");
            var stop = open.Single(o => o.Type == OrderType.StopMarket);
            var target = open.Single(o => o.Type == OrderType.TakeProfitMarket);

            var result = await AccessibleTrader.Core.Services.Trading.ProtectiveOrderEditor.ReplaceAsync(svc, "Kalshi", "KXBTCPERP",
                isLong: true, quantity: 3, AccessibleTrader.Core.Services.Trading.ProtectiveLevel.StopLoss, 97, stop, target, "Stop loss set to 97.");

            Assert.True(result.Ok, result.Message);
            Assert.Equal("Stop loss set to 97.", result.Message);
            Assert.Equal(("97.00", "110.00"), Assert.Single(venue.Live).Value);
        }

        [Fact]
        public async Task Setting_the_stop_to_where_it_already_is_leaves_it_there()
        {
            var venue = new TriggerVenue();
            venue.Live["b-1"] = ("95.00", "110.00");
            var (p, h) = Build();
            venue.Arm(h);
            var open = await p.GetOpenOrdersAsync("KXBTCPERP");

            var result = await AccessibleTrader.Core.Services.Trading.ProtectiveOrderEditor.ReplaceAsync(OrderService(p), "Kalshi", "KXBTCPERP",
                isLong: true, quantity: 3, AccessibleTrader.Core.Services.Trading.ProtectiveLevel.StopLoss, 95,
                open.Single(o => o.Type == OrderType.StopMarket), open.Single(o => o.Type == OrderType.TakeProfitMarket), "Stop loss set to 95.");

            Assert.True(result.Ok, result.Message);
            Assert.Equal(("95.00", "110.00"), Assert.Single(venue.Live).Value);
            Assert.Empty(TriggerWrites(h));
        }
    }
}
