using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The policy behind the analytics series cache (2026-08-20): WHICH fetches may be served
    /// from disk, and for HOW LONG.
    ///
    /// The dangerous mistake here is caching too much. A tradeable market's last bar changes on
    /// every tick, so serving it from cache freezes the chart — and a frozen live chart is far
    /// worse than a slow one, because nothing about it looks wrong. Hence the allow-list, and
    /// hence these tests.
    /// </summary>
    public class AnalyticsSeriesCacheTests
    {
        private static MarketDataRequest Req(string market, string symbol = "CPIAUCSL", string tf = "1d")
            => new MarketDataRequest(market, symbol, tf);

        [Theory]
        [InlineData("Economic")]
        [InlineData("OnChain")]
        [InlineData("Derivatives")]
        [InlineData("Sentiment")]
        public void AnalyticsMarkets_AreCached(string market)
        {
            Assert.NotNull(DataService.AnalyticsCacheKey("FRED", Req(market)));
        }

        [Theory]
        [InlineData("Crypto")]
        [InlineData("Stock")]
        [InlineData("Forex")]
        [InlineData("Index")]
        [InlineData("MyData")]
        [InlineData("")]
        public void TradeableMarkets_AreNeverCached(string market)
        {
            Assert.Null(DataService.AnalyticsCacheKey("Bitstamp", Req(market, "BTC/USD", "1h")));
        }

        [Fact]
        public void SubTypedMarket_IsMatchedOnItsCategory()
        {
            // FRED's sub-type makes the market string "Economic|Standard"; the category before the
            // pipe is what decides cacheability.
            Assert.NotNull(DataService.AnalyticsCacheKey("FRED", Req("Economic|Standard")));
        }

        [Fact]
        public void Key_SeparatesEveryFieldThatChangesTheResponse()
        {
            var baseline = DataService.AnalyticsCacheKey("FRED", new MarketDataRequest("Economic", "CPIAUCSL", "1d", 200));

            Assert.NotEqual(baseline, DataService.AnalyticsCacheKey("SEC EDGAR", new MarketDataRequest("Economic", "CPIAUCSL", "1d", 200)));
            Assert.NotEqual(baseline, DataService.AnalyticsCacheKey("FRED", new MarketDataRequest("Economic", "UNRATE", "1d", 200)));
            Assert.NotEqual(baseline, DataService.AnalyticsCacheKey("FRED", new MarketDataRequest("Economic", "CPIAUCSL", "1w", 200)));
            Assert.NotEqual(baseline, DataService.AnalyticsCacheKey("FRED", new MarketDataRequest("Economic", "CPIAUCSL", "1d", 500)));
            // A historical window must not be served the live-edge window.
            Assert.NotEqual(baseline, DataService.AnalyticsCacheKey("FRED", new MarketDataRequest("Economic", "CPIAUCSL", "1d", 200, Since: 1_000L)));
            Assert.NotEqual(baseline, DataService.AnalyticsCacheKey("FRED", new MarketDataRequest("Economic", "CPIAUCSL", "1d", 200, Until: 1_000L)));

            // Same request twice → same key, or the cache never hits.
            Assert.Equal(baseline, DataService.AnalyticsCacheKey("FRED", new MarketDataRequest("Economic", "CPIAUCSL", "1d", 200)));
        }

        [Fact]
        public void Ttl_IsHalfABar_ClampedTo15MinutesAnd12Hours()
        {
            // Daily and slower clamp to the 12h ceiling — a daily series cannot change more often.
            Assert.Equal(TimeSpan.FromHours(12), DataService.AnalyticsCacheTtl("1d"));
            Assert.Equal(TimeSpan.FromHours(12), DataService.AnalyticsCacheTtl("1w"));

            // Intraday: half the bar.
            Assert.Equal(TimeSpan.FromHours(2), DataService.AnalyticsCacheTtl("4h"));
            Assert.Equal(TimeSpan.FromMinutes(30), DataService.AnalyticsCacheTtl("1h"));

            // Below the floor, and unparseable input, both land somewhere sane rather than 0 —
            // a 0-second TTL would mean the cache silently never serves anything.
            Assert.Equal(TimeSpan.FromMinutes(15), DataService.AnalyticsCacheTtl("5m"));
            Assert.Equal(TimeSpan.FromMinutes(15), DataService.AnalyticsCacheTtl("1m"));
            Assert.True(DataService.AnalyticsCacheTtl("nonsense") > TimeSpan.Zero);
        }
    }
}
