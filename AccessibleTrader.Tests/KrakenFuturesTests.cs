using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Plugins.KrakenFutures;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Kraken Futures — a separate venue from Kraken spot, with separate keys and a
    /// different signature.
    ///
    /// <para>
    /// The network calls need a real futures key and are verified by hand against
    /// the demo venue. What is pinned here is everything that can be wrong without
    /// one — and the signature above all, because it is the single thing that is
    /// either exactly right or rejects every authenticated call with no clue as to
    /// why.
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
        public void A_paper_key_profile_routes_to_the_demo_venue()
        {
            // demo-futures.kraken.com is a real environment with its own keys and
            // paper funds — not a simulation we run — and it is reachable without
            // the account verification that gates real funding.
            var p = new KrakenFuturesProvider();
            Assert.Equal(ProviderEnvironment.Live, p.Environment);

            p.Configure(new Dictionary<string, string> { ["Environment"] = "Paper" });

            Assert.Equal(ProviderEnvironment.Paper, p.Environment);
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
