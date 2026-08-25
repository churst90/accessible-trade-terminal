using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The shared REST-signing and symbol-format primitives that replaced the
    /// per-provider copies. Pinned so a change can't silently alter every
    /// provider's signature or symbol wire-shape.
    /// </summary>
    public class SdkHelperTests
    {
        // ── RestSigning ──────────────────────────────────────────────────────

        [Fact]
        public void HmacSha256Hex_matches_known_vector()
        {
            // RFC-style known vector: HMAC-SHA256(key="key", msg="The quick brown fox
            // jumps over the lazy dog") = f7bc83f430538424b13298e6aa6fb143ef4d59a149...
            var hex = RestSigning.HmacSha256Hex("key", "The quick brown fox jumps over the lazy dog");
            Assert.Equal("f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8", hex);
        }

        [Fact]
        public void HmacSha256Hex_uppercase_option()
        {
            var lower = RestSigning.HmacSha256Hex("secret", "msg");
            var upper = RestSigning.HmacSha256Hex("secret", "msg", upperCase: true);
            Assert.Equal(lower.ToUpperInvariant(), upper);
        }

        [Fact]
        public void HmacSha384Hex_matches_known_vector()
        {
            // Independently computed (Python hmac/hashlib): HMAC-SHA384(key="key",
            // msg="The quick brown fox jumps over the lazy dog"). Gemini's
            // X-GEMINI-SIGNATURE shape — GeminiAuth.Sign delegates here.
            var hex = RestSigning.HmacSha384Hex("key", "The quick brown fox jumps over the lazy dog");
            Assert.Equal("d7f4727e2c0b39ae0f1e40cc96f60242d5b7801841cea6fc592c5d3e1ae50700582a96cf35e1e554995fe4e03381c237", hex);
        }

        [Fact]
        public void HmacSha512Base64_matches_known_vector()
        {
            // Independently computed (Python): HMAC-SHA512(key="key", same msg),
            // base64 — the output shape both Kraken APIs put in their auth header.
            var b64 = RestSigning.HmacSha512Base64(
                System.Text.Encoding.UTF8.GetBytes("key"),
                System.Text.Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog"));
            Assert.Equal("tCrwkFe6weLUFwjkipAuCbX/fxKrQopP6GZTxz3SSPuC+UilSfe3kaW0GRXuTR7Dk1NX5OIxclDQNyr6Lr7rOg==", b64);
        }

        [Fact]
        public void Sha256_matches_known_vector()
        {
            // FIPS 180 vector: SHA-256("abc").
            Assert.Equal(
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                RestSigning.ToHex(RestSigning.Sha256("abc")));
        }

        // ── TimestampParser ──────────────────────────────────────────────────

        [Fact]
        public void TimestampParser_handles_fractional_unix_seconds()
        {
            // OANDA's UNIX datetime format is fractional seconds as a string.
            // long.TryParse cannot read it, so before the fractional branch this
            // returned DateTime.MinValue and the caller silently kept UtcNow.
            Assert.Equal(
                new System.DateTime(2021, 6, 1, 0, 0, 0, System.DateTimeKind.Utc),
                TimestampParser.Parse("1622505600.000000000"));
            Assert.Equal(
                new System.DateTime(2021, 6, 1, 0, 0, 0, 500, System.DateTimeKind.Utc),
                TimestampParser.Parse("1622505600.5"));
        }

        [Fact]
        public void BuildQuery_url_encodes_and_preserves_order()
        {
            var q = RestSigning.BuildQuery(new[]
            {
                new KeyValuePair<string, string>("symbol", "BTC/USD"),
                new KeyValuePair<string, string>("side", "BUY"),
            });
            Assert.Equal("symbol=BTC%2FUSD&side=BUY", q);
        }

        [Fact]
        public void QueryPrefixed_empty_is_blank()
        {
            Assert.Equal("", RestSigning.QueryPrefixed(null));
            Assert.Equal("", RestSigning.QueryPrefixed(new Dictionary<string, string>()));
            Assert.Equal("?a=1", RestSigning.QueryPrefixed(new Dictionary<string, string> { ["a"] = "1" }));
        }

        // ── SymbolFormat ─────────────────────────────────────────────────────

        [Theory]
        [InlineData("BTCUSDT", "BTC", "USDT")]   // USDT wins over USD (longest-first)
        [InlineData("ETHUSD",  "ETH", "USD")]
        [InlineData("SOLBTC",  "SOL", "BTC")]
        [InlineData("USDT",    "USDT", "")]      // quote alone → no split (length guard)
        [InlineData("ABCXYZ",  "ABCXYZ", "")]    // unknown quote → no split
        public void SplitBaseQuote_uses_longest_known_quote(string input, string b, string q)
        {
            var (bb, qq) = SymbolFormat.SplitBaseQuote(input);
            Assert.Equal(b, bb);
            Assert.Equal(q, qq);
        }

        [Theory]
        [InlineData("BTC/USDT", "BTC_USDT")]
        [InlineData("btc-usd",  "BTC_USD")]
        [InlineData("BTCUSDT",  "BTC_USDT")]
        [InlineData("BTC_USDT", "BTC_USDT")]  // already underscored → passthrough (upper)
        public void Underscored_produces_futures_shape(string input, string expected)
            => Assert.Equal(expected, SymbolFormat.Underscored(input));

        [Theory]
        [InlineData("BTCUSD",  "BTC/USD")]
        [InlineData("eth-usdt", "ETH/USDT")]
        public void Slashed_produces_slashed_pair(string input, string expected)
            => Assert.Equal(expected, SymbolFormat.Slashed(input));

        [Theory]
        [InlineData("btc/usd", "BTCUSD")]
        [InlineData("ETH-USDT", "ETHUSDT")]
        [InlineData("sol_usdc", "SOLUSDC")]
        public void Concatenated_strips_all_separators(string input, string expected)
            => Assert.Equal(expected, SymbolFormat.Concatenated(input));
    }
}
