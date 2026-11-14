using AccessibleTrader.Plugins.KrakenFutures;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Kraken Futures — a separate venue from Kraken spot, with separate keys and a
    /// different signature.
    ///
    /// <para>
    /// The network calls need a real futures key — and since Kraken decommissioned
    /// the demo venue (2026-07-14, no replacement), a real key means a funded live
    /// account. What is pinned here is everything that can be wrong without one —
    /// and the signature above all, because it is the single thing that is either
    /// exactly right or rejects every authenticated call with no clue as to why.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class KrakenFuturesTests
    {
        // ── The signature ────────────────────────────────────────────────────

        [Fact]
        public void The_authent_signature_matches_an_independent_implementation()
        {
            // Inputs are Kraken's own documented example. The expected value was
            // computed by a SEPARATE implementation of the published steps rather
            // than read back from this code — a signature test that asserts whatever
            // the code already produces verifies nothing at all.
            const string secret = "rttp4AzwRfYEdQ7R7X8Z/04Y4TZPa97pqCypi3xXxAqftygftnI6H9yGV+OcUOOJeFtZkr8mVwbAndU3Kz4Q+eG=";
            const string expected = "DqUyz8Wh/72af7dimSXHw91IFxrAriTgVodyg2s67PU2mVStwLDQak+uIoCtfb43XONq0xVAp+vm5dqnhFAB1Q==";

            string actual = KrakenFuturesAuth.Sign(
                postData: "symbol=fi_xbtusd_180615",
                nonce: "1415957147987",
                endpointPath: "/api/v3/orderbook",
                apiSecretBase64: secret);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void The_signature_covers_the_path_without_the_derivatives_prefix()
        {
            // The request goes to /derivatives/api/v3/orderbook but the hash covers
            // /api/v3/orderbook. Signing the wrong one is the likeliest single cause
            // of a 401 here, and the two differ by a prefix that looks like it
            // belongs.
            const string secret = "rttp4AzwRfYEdQ7R7X8Z/04Y4TZPa97pqCypi3xXxAqftygftnI6H9yGV+OcUOOJeFtZkr8mVwbAndU3Kz4Q+eG=";

            string right = KrakenFuturesAuth.Sign("", "1", "/api/v3/accounts", secret);
            string wrong = KrakenFuturesAuth.Sign("", "1", "/derivatives/api/v3/accounts", secret);

            Assert.NotEqual(right, wrong);
        }

        [Fact]
        public void Every_input_changes_the_signature()
        {
            // Guards against a signer that silently ignores an argument — which
            // would still authenticate for one request shape and fail for others.
            const string secret = "rttp4AzwRfYEdQ7R7X8Z/04Y4TZPa97pqCypi3xXxAqftygftnI6H9yGV+OcUOOJeFtZkr8mVwbAndU3Kz4Q+eG=";
            string baseline = KrakenFuturesAuth.Sign("a=1", "100", "/api/v3/x", secret);

            Assert.NotEqual(baseline, KrakenFuturesAuth.Sign("a=2", "100", "/api/v3/x", secret));
            Assert.NotEqual(baseline, KrakenFuturesAuth.Sign("a=1", "101", "/api/v3/x", secret));
            Assert.NotEqual(baseline, KrakenFuturesAuth.Sign("a=1", "100", "/api/v3/y", secret));
        }

        [Fact]
        public void Url_encoding_is_part_of_what_is_signed()
        {
            // Kraken changed this on 2024-02-20: the ENCODED form is hashed, so a
            // value containing a space must be signed as hello%20world. Signing the
            // decoded form fails on exactly the parameters that contain punctuation.
            const string secret = "rttp4AzwRfYEdQ7R7X8Z/04Y4TZPa97pqCypi3xXxAqftygftnI6H9yGV+OcUOOJeFtZkr8mVwbAndU3Kz4Q+eG=";

            Assert.NotEqual(
                KrakenFuturesAuth.Sign("greeting=hello%20world", "1", "/api/v3/x", secret),
                KrakenFuturesAuth.Sign("greeting=hello world",   "1", "/api/v3/x", secret));
        }

        [Fact]
        public void Nonces_never_repeat_even_within_the_same_millisecond()
        {
            // A repeated nonce is rejected, and the failure presents as a bad key
            // rather than as a timing problem — so it would be debugged in the wrong
            // place entirely.
            var seen = Enumerable.Range(0, 500).Select(_ => KrakenFuturesAuth.Nonce()).ToList();

            Assert.Equal(seen.Count, seen.Distinct().Count());
            Assert.Equal(seen.OrderBy(long.Parse), seen);   // and they only go up
        }

        // ── Wiring ───────────────────────────────────────────────────────────

        [Fact]
        public void It_is_a_trading_provider_and_an_order_book_provider()
        {
            var p = new KrakenFuturesProvider();

            Assert.IsAssignableFrom<ITradingProvider>(p);
            Assert.IsAssignableFrom<IOrderBookProvider>(p);
            Assert.NotNull(p.GetCapability<ITradingProvider>());
            Assert.NotNull(p.GetCapability<IOrderBookProvider>());
        }

        [Fact]
        public void It_is_a_separate_provider_from_Kraken_spot()
        {
            // Different name means a different credential slot, which is the whole
            // reason this is a second plugin: futures keys are generated somewhere
            // else and do not work against the spot API or vice versa.
            Assert.NotEqual(new AccessibleTrader.Plugins.Kraken.KrakenProvider().Name,
                            new KrakenFuturesProvider().Name);
        }

        [Fact]
        public void It_declares_futures_and_leverage_but_not_spot_margin()
        {
            // Leverage on a derivatives venue with no lending desk: futures without
            // margin trading is coherent, and it is the same shape MEXC has. An
            // earlier draft of the capability audit called that a contradiction and
            // was wrong.
            var caps = new KrakenFuturesProvider().Capabilities;

            Assert.True(caps.HasFlag(ProviderCapabilities.FuturesTrading));
            Assert.True(caps.HasFlag(ProviderCapabilities.Leverage));
            Assert.True(caps.HasFlag(ProviderCapabilities.Shorting));
            Assert.False(caps.HasFlag(ProviderCapabilities.MarginTrading));
            Assert.True(new KrakenFuturesProvider().MaxLeverage > 1);
        }

        [Fact]
        public void It_does_not_claim_order_event_streaming_it_has_not_built()
        {
            // False here makes the order service POLL for fills. Left at the default
            // true, it would wait forever on a stream that never emits, and fills
            // would simply never announce.
            Assert.False(new KrakenFuturesProvider().SupportsOrderEventStreaming);
        }

        [Fact]
        public void A_paper_key_profile_still_reports_the_paper_environment()
        {
            // The routing is kept even though the venue it led to is gone (below):
            // the profile's environment label must stay truthful, and a revived
            // demo venue would be one constant away.
            var p = new KrakenFuturesProvider();
            Assert.Equal(ProviderEnvironment.Live, p.Environment);

            p.Configure(new Dictionary<string, string> { ["Environment"] = "Paper" });

            Assert.Equal(ProviderEnvironment.Paper, p.Environment);
        }

        [Fact]
        public async Task A_paper_profile_refuses_with_the_decommission_not_a_parse_error()
        {
            // demo-futures.kraken.com was decommissioned on 2026-07-14; every path,
            // REST API included, 301s to a marketing page. Without this guard the
            // failure surfaces as an allow-list violation or HTML fed to the JSON
            // parser — either of which reads as a bug in OUR signing code and sends
            // someone debugging the wrong thing. No network in this test: the
            // refusal must fire before any request leaves.
            var p = new KrakenFuturesProvider();
            p.Configure(new Dictionary<string, string>
            {
                ["ApiKey"] = "k",
                ["ApiSecret"] = Convert.ToBase64String(new byte[32]),
                ["Environment"] = "Paper",
            });

            var (ok, message) = await p.ValidateApiKeyAsync();

            Assert.False(ok);
            Assert.Contains("decommissioned", message);
            Assert.Contains("demo-futures.kraken.com", message);
            // Says what still works, not merely what is gone.
            Assert.Contains("F12", message);
        }

        // ── Positions and contract rules (2026-11-14) ───────────────────────

        [Fact]
        public void A_position_reads_its_own_pnl_and_leverage_not_the_funding()
        {
            // Kraken's documented /openpositions item. The P&L used to be unrealizedFunding and the
            // leverage a constant 1.
            var p = KrakenFuturesProvider.ReadPosition(Newtonsoft.Json.Linq.JObject.Parse(
                """{"side":"short","symbol":"PF_DEFIUSD","price":570,"size":1,"unrealizedPnl":12.34,"unrealizedFunding":-0.0073,"pnlCurrency":"BTC","maxFixedLeverage":5}"""));

            Assert.Equal(-1, p.Quantity);
            Assert.Equal(12.34, p.UnrealizedPnL);
            Assert.Equal(5, p.Leverage);
            Assert.Equal(MarginMode.Isolated, p.MarginMode);
            Assert.Equal(0, p.LiquidationPrice);   // the venue gives none per position; "—", not a guess

            var cross = KrakenFuturesProvider.ReadPosition(Newtonsoft.Json.Linq.JObject.Parse(
                """{"side":"long","symbol":"PF_XBTUSD","price":90000,"size":0.01,"unrealizedPnl":-3,"crossMaxLeverage":10}"""));
            Assert.Equal(10, cross.Leverage);
            Assert.Equal(MarginMode.Cross, cross.MarginMode);
        }

        // Live /instruments shape, 2026-11-14, cut to the fields read.
        private const string Instruments = """
            {"result":"success","instruments":[
              {"symbol":"PF_XBTUSD","type":"flexible_futures","tickSize":1,"contractSize":1,"tradeable":true,"contractValueTradePrecision":4,"postOnly":false,"suspended":false,"base":"BTC","quote":"USD","isExpired":false},
              {"symbol":"PF_SHIBUSD","type":"flexible_futures","tickSize":0.000000001,"contractSize":1,"tradeable":true,"contractValueTradePrecision":-3,"postOnly":false,"suspended":false,"base":"SHIB","quote":"USD","isExpired":false},
              {"symbol":"PF_OLDUSD","type":"flexible_futures","tickSize":0.01,"contractSize":1,"tradeable":true,"contractValueTradePrecision":1,"postOnly":false,"suspended":true,"base":"OLD","quote":"USD","isExpired":false}
            ]}
            """;

        [Fact]
        public void Contract_rules_read_the_size_step_from_the_trade_precision()
        {
            var all = Newtonsoft.Json.Linq.JObject.Parse(Instruments)["instruments"]!;
            var btc = KrakenFuturesProvider.ReadContractRules(all[0]!)!.Rules;
            Assert.Equal(0.0001m, btc.QuantityStep);
            Assert.Equal(1m, btc.PriceTick);
            Assert.Equal("BTC", btc.QuantityUnit);
            Assert.Equal("0.0123", btc.FormatQuantity(0.01239));   // cut down, never up
            Assert.Equal("90124", btc.FormatPrice(90123.6));

            Assert.Equal(1000m, KrakenFuturesProvider.ReadContractRules(all[1]!)!.Rules.QuantityStep);
            Assert.Contains("suspended", KrakenFuturesProvider.ReadContractRules(all[2]!)!.Rules.NotTrading);
        }

        [Fact]
        public async Task An_order_below_the_contract_step_is_refused_in_words_and_not_sent()
        {
            var h = new AccessibleTrader.Tests.Fakes.FakeHttpMessageHandler()
                .Get(@"futures\.kraken\.com/derivatives/api/v3/instruments", Instruments)
                .Post(@"futures\.kraken\.com/derivatives/api/v3/sendorder",
                    """{"result":"success","sendStatus":{"order_id":"OID-1","status":"placed"}}""");
            var p = Live(h);

            string small = await p.PlaceOrderAsync(new TradeSignal("PF_XBTUSD", OrderSide.Buy, 0.00005, OrderType.Market));
            Assert.StartsWith("ORDER_FAILED:Kraken Futures's smallest PF_XBTUSD order is 0.0001 BTC", small);
            Assert.DoesNotContain(h.Captured, r => r.RequestUri!.AbsolutePath.EndsWith("/sendorder"));

            // Vacuity: a size on the step goes, cut to the step and written plainly.
            Assert.Equal("OID-1", await p.PlaceOrderAsync(new TradeSignal("PF_XBTUSD", OrderSide.Buy, 0.01239, OrderType.Market)));
            string body = h.CapturedBodies[h.Captured.FindIndex(r => r.RequestUri!.AbsolutePath.EndsWith("/sendorder"))];
            Assert.Contains("size=0.0123", body);
        }

        private static KrakenFuturesProvider Live(AccessibleTrader.Tests.Fakes.FakeHttpMessageHandler h)
        {
            var p = new KrakenFuturesProvider();
            p.Configure(new Dictionary<string, string>
            {
                ["ApiKey"] = "kfk",
                ["ApiSecret"] = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()),
                ["Environment"] = "Live",
            });
            typeof(KrakenFuturesProvider).GetField("_http",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(p, new HttpClient(h));
            return p;
        }

        // ── Timeframes ───────────────────────────────────────────────────────

        [Theory]
        [InlineData("1m", "1m")]
        [InlineData("4h", "4h")]
        [InlineData("1d", "1d")]
        [InlineData("1w", "1w")]
        [InlineData("3m", "1h")]   // unsupported → a sane default rather than a 404
        public void Resolutions_map_to_what_the_charts_api_accepts(string tf, string expected) =>
            Assert.Equal(expected, KrakenFuturesProvider.MapResolution(tf));

        [Fact]
        public void Every_advertised_timeframe_maps_to_itself()
        {
            // A timeframe offered in the dropdown that silently resolves to a
            // different one would draw the wrong chart without saying so.
            foreach (string tf in new KrakenFuturesProvider().NativelySupportedTimeframes)
                Assert.Equal(tf, KrakenFuturesProvider.MapResolution(tf));
        }
    }
}
