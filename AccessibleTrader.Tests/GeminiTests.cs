using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Plugins.Gemini;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Gemini — US-regulated spot venue, and the first plugin whose sandbox was
    /// probed BEFORE the plugin was written (the Kraken Futures demo died three
    /// weeks before we tried to use it, and Bybit's testnet turned out to be
    /// geo-blocked; reachability is now checked first).
    ///
    /// <para>
    /// The network calls are verified by hand against the sandbox, which has
    /// self-service accounts. What is pinned here is everything that can be wrong
    /// without a key — the signature above all, because it is the single thing
    /// that is either exactly right or rejects every authenticated call with no
    /// clue as to why.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class GeminiTests
    {
        // ── The signature ────────────────────────────────────────────────────

        [Fact]
        public void The_signature_matches_an_independent_implementation()
        {
            // Payload shaped exactly as Gemini's documented /v1/order/status
            // example. The expected values were computed by a SEPARATE
            // implementation of the published steps (Python's hmac/hashlib) rather
            // than read back from this code — a signature test that asserts
            // whatever the code already produces verifies nothing at all.
            const string json = "{\"request\":\"/v1/order/status\",\"nonce\":123456,\"order_id\":18834}";

            string b64 = GeminiAuth.Payload(json);
            Assert.Equal("eyJyZXF1ZXN0IjoiL3YxL29yZGVyL3N0YXR1cyIsIm5vbmNlIjoxMjM0NTYsIm9yZGVyX2lkIjoxODgzNH0=", b64);

            Assert.Equal(
                "51f2d46b8d13add5414bb73d72c1e1e1d3e1f6f8ed411960d860510df3219d0ed3514578d14f18cd1340109bf0c0385b",
                GeminiAuth.Sign(b64, "1234abcd"));
        }

        [Fact]
        public void A_second_vector_pins_the_secret_handling()
        {
            // Different payload, different secret — computed by the same
            // independent implementation. Two vectors so a signer that hard-codes
            // anything about either input cannot pass by luck.
            const string json = "{\"request\":\"/v1/balances\",\"nonce\":1785969500000}";

            string b64 = GeminiAuth.Payload(json);
            Assert.Equal("eyJyZXF1ZXN0IjoiL3YxL2JhbGFuY2VzIiwibm9uY2UiOjE3ODU5Njk1MDAwMDB9", b64);

            Assert.Equal(
                "bce1a77794e22d805a550e8277b69bc7981fde8ce111816e099ab6e2937388eadd62dbbf26b163dff66e4223173715b0",
                GeminiAuth.Sign(b64, "topsecret"));
        }

        [Fact]
        public void What_is_signed_is_the_base64_string_not_the_raw_json()
        {
            // The subtlest part of Gemini's scheme: HMAC over the base64 TEXT.
            // Signing the raw JSON (or the decoded bytes) produces a valid-looking
            // hex signature that fails on every call.
            const string json = "{\"request\":\"/v1/balances\",\"nonce\":1}";

            Assert.NotEqual(
                GeminiAuth.Sign(GeminiAuth.Payload(json), "s"),
                GeminiAuth.Sign(json, "s"));
        }

        [Fact]
        public void The_secret_is_used_as_utf8_text_not_base64_decoded()
        {
            // Both Kraken APIs base64-decode their secret before keying; Gemini
            // does not. "MTIzNA==" decodes to "1234" — if the signer decoded, the
            // two would collide.
            string b64 = GeminiAuth.Payload("{\"request\":\"/v1/balances\",\"nonce\":1}");

            Assert.NotEqual(GeminiAuth.Sign(b64, "MTIzNA=="), GeminiAuth.Sign(b64, "1234"));
        }

        [Fact]
        public void Nonces_never_repeat_even_within_the_same_millisecond()
        {
            // A repeated nonce is rejected as InvalidNonce, which presents as an
            // auth problem — so it would be debugged in the wrong place entirely.
            var seen = Enumerable.Range(0, 500).Select(_ => GeminiAuth.Nonce()).ToList();

            Assert.Equal(seen.Count, seen.Distinct().Count());
            Assert.Equal(seen.OrderBy(n => n), seen);   // and they only go up
        }

        // ── Wiring ───────────────────────────────────────────────────────────

        [Fact]
        public void It_is_a_trading_provider_and_an_order_book_provider()
        {
            var p = new GeminiProvider();

            Assert.IsAssignableFrom<ITradingProvider>(p);
            Assert.IsAssignableFrom<IOrderBookProvider>(p);
            Assert.NotNull(p.GetCapability<ITradingProvider>());
            Assert.NotNull(p.GetCapability<IOrderBookProvider>());
        }

        [Fact]
        public void A_paper_key_profile_routes_to_the_sandbox()
        {
            // api.sandbox.gemini.com is a real environment with self-service
            // accounts and paper funds — not a simulation we run.
            var p = new GeminiProvider();
            Assert.Equal(ProviderEnvironment.Live, p.Environment);

            p.Configure(new Dictionary<string, string> { ["Environment"] = "Paper" });

            Assert.Equal(ProviderEnvironment.Paper, p.Environment);
        }

        // ── Capability honesty ───────────────────────────────────────────────

        [Fact]
        public void A_spot_only_venue_declares_no_derivative_capabilities()
        {
            // Gemini's derivatives platform is a separate non-US product with
            // separate keys; claiming any of this here would put dead controls on
            // the trading dashboard.
            var caps = new GeminiProvider().Capabilities;

            Assert.False(caps.HasFlag(ProviderCapabilities.Leverage));
            Assert.False(caps.HasFlag(ProviderCapabilities.MarginTrading));
            Assert.False(caps.HasFlag(ProviderCapabilities.FuturesTrading));
            Assert.False(caps.HasFlag(ProviderCapabilities.Shorting));
            Assert.Equal(1.0, new GeminiProvider().MaxLeverage);
        }

        [Fact]
        public void What_it_does_do_is_declared()
        {
            var caps = new GeminiProvider().Capabilities;

            Assert.True(caps.HasFlag(ProviderCapabilities.L2));
            Assert.True(caps.HasFlag(ProviderCapabilities.MarketDepth));
            Assert.True(caps.HasFlag(ProviderCapabilities.PostOnly));    // maker-or-cancel is sent
            Assert.True(caps.HasFlag(ProviderCapabilities.TimeInForce)); // IOC / FOK are sent
        }

        [Fact]
        public void It_does_not_claim_order_event_streaming_it_has_not_built()
        {
            // False makes the order service POLL for fills. Left at the default
            // true, fills would simply never announce.
            Assert.False(new GeminiProvider().SupportsOrderEventStreaming);
        }

        [Fact]
        public void It_claims_the_order_status_lookup_because_it_implements_one()
        {
            // /v1/order/status is a real authoritative order-by-id lookup, so the
            // poller resolves orders directly instead of via the open-orders +
            // fills heuristic that mis-announced fills on Tradier/Schwab.
            Assert.True(new GeminiProvider().SupportsOrderStatusQuery);
        }

        // ── Refusals that never reach the venue ──────────────────────────────

        [Fact]
        public async Task Order_types_the_venue_does_not_have_are_refused_by_name()
        {
            // Gemini offers limit and stop-limit only. A stop-MARKET silently
            // downgraded to something else would fill at a different time than
            // the user asked for; the refusal names the type so the fix is clear.
            var p = new GeminiProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "k", ["ApiSecret"] = "s" });

            string result = await p.PlaceOrderAsync(
                new TradeSignal("BTCUSD", OrderSide.Buy, 0.1, OrderType.StopMarket, TriggerPrice: 50000));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Contains("StopMarket", result);
        }

        [Fact]
        public async Task A_limit_order_without_a_price_is_refused_before_any_request()
        {
            var p = new GeminiProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "k", ["ApiSecret"] = "s" });

            string result = await p.PlaceOrderAsync(
                new TradeSignal("BTCUSD", OrderSide.Buy, 0.1, OrderType.Limit));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Contains("limit price", result);
        }

        [Fact]
        public async Task Without_a_key_no_order_is_attempted()
        {
            string result = await new GeminiProvider().PlaceOrderAsync(
                new TradeSignal("BTCUSD", OrderSide.Buy, 0.1, OrderType.Limit, Price: 50000));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Contains("no Gemini API key", result);
        }

        [Fact]
        public async Task Set_leverage_is_an_honest_one()
        {
            // Spot venue, no margin product: leverage has nothing to apply to, and
            // the answer is 1 no matter what was requested.
            Assert.Equal(1.0, await new GeminiProvider().SetLeverageAsync("BTCUSD", 10));
        }

        [Fact]
        public async Task A_spot_account_has_positions_answered_honestly_as_none()
        {
            Assert.Empty(await new GeminiProvider().GetPositionsAsync());
        }

        // ── Timeframes ───────────────────────────────────────────────────────

        [Theory]
        [InlineData("1m", "1m")]
        [InlineData("30m", "30m")]
        [InlineData("1h", "1hr")]     // Gemini spells hours "hr" and days "day"
        [InlineData("6h", "6hr")]
        [InlineData("1d", "1day")]
        [InlineData("3m", "1hr")]     // unsupported → a sane default rather than a 404
        public void Resolutions_map_to_what_the_candles_api_accepts(string tf, string expected) =>
            Assert.Equal(expected, GeminiProvider.MapResolution(tf));

        [Fact]
        public void Every_advertised_timeframe_maps_to_a_distinct_native_one()
        {
            // A timeframe offered in the dropdown that silently resolves to a
            // different one would draw the wrong chart without saying so.
            var p = new GeminiProvider();
            var mapped = p.NativelySupportedTimeframes.Select(GeminiProvider.MapResolution).ToList();

            Assert.Equal(mapped.Count, mapped.Distinct().Count());
        }
    }
}
