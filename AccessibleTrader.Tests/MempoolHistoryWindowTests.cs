using System.Net;
using System.Reflection;
using AccessibleTrader.Plugins.Mempool;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>Mempool asks for a window that reaches the dates it was asked for.</b>
    ///
    /// <para>
    /// mempool.space serves a period ending NOW — <c>/api/v1/mining/hashrate/1y</c> and friends.
    /// The provider chose that period from <c>request.Limit</c> alone and then applied
    /// <c>Since</c>/<c>Until</c> as a filter over whatever came back, so a request for 2019 with
    /// a 200-bar limit fetched the last year and filtered every bar of it away: an empty chart,
    /// no error, nothing spoken. The same arithmetic made the provider unusable through
    /// <c>CrossSeriesCache</c>, whose walk-back pagination asks for depth by moving <c>until</c>
    /// earlier — the period never grew, so every page after the first was empty.
    /// </para>
    ///
    /// <para>
    /// Two halves are pinned here, and they fail independently: the period must be derived from
    /// the requested dates, and a window genuinely older than the venue's history must be SAID
    /// rather than returned as a blank chart. For a user who cannot see the axis, an empty
    /// series and a flat one are the same picture until something names the difference.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class MempoolHistoryWindowTests
    {
        private static MempoolProvider NewProvider(FakeHttpMessageHandler handler)
        {
            var provider = new MempoolProvider();
            HttpClientSwap.ReplaceAll(provider, handler);
            return provider;
        }

        private static long Ms(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds();

        /// <summary>Hash-rate rows for the last <paramref name="days"/> days, one per day.</summary>
        private static string HashrateBody(int days)
        {
            var rows = Enumerable.Range(0, days).Select(i =>
            {
                long ts = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-(days - 1 - i)), TimeSpan.Zero).ToUnixTimeSeconds();
                return $$"""{"timestamp":{{ts}},"avgHashrate":{{100 + i}}}""";
            });
            return $$"""{"hashrates":[{{string.Join(",", rows)}}],"difficulty":[]}""";
        }

        [Theory]
        // No dates: the limit is the window, one bar per day — the original behaviour.
        [InlineData(30, null, null, "1m")]
        [InlineData(200, null, null, "1y")]
        // Since is the far edge, so the period has to reach back to it whatever the limit says.
        [InlineData(200, 400, null, "2y")]
        [InlineData(200, 1000, null, "3y")]
        [InlineData(200, 45, null, "3m")]
        // A pagination page: Limit bars ending at Until, so the reach is Until plus the page.
        [InlineData(200, null, 300, "2y")]
        public void Period_reaches_back_to_the_oldest_bar_requested(
            int limit, int? sinceDaysAgo, int? untilDaysAgo, string expected)
        {
            var request = new MarketDataRequest("OnChain", "HASHRATE", "1d", limit,
                Since: sinceDaysAgo.HasValue ? Ms(DateTime.UtcNow.AddDays(-sinceDaysAgo.Value)) : null,
                Until: untilDaysAgo.HasValue ? Ms(DateTime.UtcNow.AddDays(-untilDaysAgo.Value)) : null);

            Assert.Equal(expected, MempoolProvider.GetTimePeriod(request));
        }

        [Fact]
        public async Task A_historical_request_asks_the_api_for_a_window_that_covers_it()
        {
            var handler = new FakeHttpMessageHandler()
                .Get(@"/api/v1/mining/hashrate/", HashrateBody(30));
            var provider = NewProvider(handler);

            await provider.FetchOhlcvAsync(new MarketDataRequest(
                "OnChain", "HASHRATE", "1d", 200,
                Since: Ms(DateTime.UtcNow.AddDays(-900)),
                Until: Ms(DateTime.UtcNow.AddDays(-800))));

            var url = handler.Captured.Single().RequestUri!.ToString();
            Assert.EndsWith("/hashrate/3y", url);
        }

        [Fact]
        public async Task A_window_older_than_the_venues_history_is_spoken_not_left_blank()
        {
            var handler = new FakeHttpMessageHandler()
                .Get(@"/api/v1/mining/hashrate/", HashrateBody(30));
            var provider = NewProvider(handler);

            var errors = new List<string>();
            using var sub = provider.ErrorStream.Subscribe(errors.Add);

            var result = await provider.FetchOhlcvAsync(new MarketDataRequest(
                "OnChain", "HASHRATE", "1d", 200,
                Since: Ms(new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                Until: Ms(new DateTime(2019, 6, 1, 0, 0, 0, DateTimeKind.Utc))));

            Assert.Empty(result.Ohlcv);
            Assert.Single(errors);
            Assert.Contains("no HASHRATE data for the requested dates", errors[0]);
            // The message names where the history actually starts, which is the one fact that
            // lets the user pick a window that works.
            Assert.Contains(DateTime.UtcNow.Date.AddDays(-29).ToString("yyyy-MM-dd"), errors[0]);
        }

        [Fact]
        public async Task A_live_edge_request_that_returns_bars_says_nothing()
        {
            var handler = new FakeHttpMessageHandler()
                .Get(@"/api/v1/mining/hashrate/", HashrateBody(30));
            var provider = NewProvider(handler);

            var errors = new List<string>();
            using var sub = provider.ErrorStream.Subscribe(errors.Add);

            var result = await provider.FetchOhlcvAsync(
                new MarketDataRequest("OnChain", "HASHRATE", "1d", 30));

            Assert.Equal(30, result.Ohlcv.Count);
            Assert.Empty(errors);
        }
    }
}
