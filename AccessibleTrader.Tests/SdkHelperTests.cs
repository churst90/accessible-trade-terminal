using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Services;
using Xunit;

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
