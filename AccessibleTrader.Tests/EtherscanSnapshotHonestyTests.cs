using System.Reflection;
using AccessibleTrader.Plugins.Etherscan;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>A snapshot provider says it is a snapshot instead of wearing a history's dates.</b>
    ///
    /// <para>
    /// Etherscan's free tier has no historical form for the gas oracle, supply or node-count
    /// endpoints: each call returns the value as of now. <c>FetchOhlcvAsync</c> wrapped that
    /// scalar in one bar stamped <c>DateTime.UtcNow.Date</c> and returned it for ANY request,
    /// which went wrong in two directions at once.
    /// </para>
    ///
    /// <para>
    /// Forward: a request for a window that has already closed got today's reading carrying a
    /// date inside that window, and <c>DataService.AnalyticsCacheKey</c> keys the analytics
    /// disk cache by <c>Since</c>/<c>Until</c> — so every distinct historical window acquired
    /// its own cache entry holding a point it was never from. The chart showed a single dot
    /// with nothing spoken; for a user who cannot see the axis, one dot and a real series are
    /// the same picture until the numbers are read out.
    /// </para>
    ///
    /// <para>
    /// Backward: midnight is a lie in the direction that matters. <c>CrossSeriesForwardFill</c>
    /// admits ties (<c>ticks[i].Ts &lt;= barTs</c>, <c>CrossSeriesCache.cs</c>), so a gas price
    /// read at noon was visible to an indicator sitting on today's 00:00 bar — up to
    /// twenty-four hours of look-ahead in the one series whose entire purpose is to be live.
    /// This is the same class the 2026-08-27 <c>AnalyticsPublicationLag</c> work fixed for the
    /// daily statistics; a snapshot's honest stamp is simply the moment it was read.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class EtherscanSnapshotHonestyTests
    {
        private const string GasOracleBody =
            """{"status":"1","message":"OK","result":{"SafeGasPrice":"12","ProposeGasPrice":"14","FastGasPrice":"18"}}""";

        private static EtherscanProvider NewProvider(FakeHttpMessageHandler handler)
        {
            var provider = new EtherscanProvider();
            provider.Configure(new Dictionary<string, string> { ["ApiKey"] = "test-key" });
            var field = typeof(EtherscanProvider)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .First(f => f.FieldType == typeof(HttpClient));
            field.SetValue(provider, new HttpClient(handler));
            return provider;
        }

        private static long Ms(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds();

        [Fact]
        public async Task The_snapshot_is_stamped_when_it_was_read_not_at_todays_midnight()
        {
            var before = DateTime.UtcNow;
            var provider = NewProvider(new FakeHttpMessageHandler().Get(@"action=gasoracle", GasOracleBody));

            var result = await provider.FetchOhlcvAsync(
                new MarketDataRequest("OnChain", "ETH_GAS_FAST", "1d", 1));

            var bar = Assert.Single(result.Ohlcv);
            Assert.Equal(18.0, bar.Close);
            Assert.InRange(bar.Date, before, DateTime.UtcNow.AddMinutes(1));
            // The specific defect: midnight today, which is in the past and therefore readable
            // by every bar of the current day.
            Assert.NotEqual(DateTime.UtcNow.Date, bar.Date);
        }

        [Fact]
        public async Task A_closed_window_is_refused_out_loud_rather_than_filled_with_todays_reading()
        {
            var handler = new FakeHttpMessageHandler().Get(@"action=gasoracle", GasOracleBody);
            var provider = NewProvider(handler);

            var errors = new List<string>();
            using var sub = provider.ErrorStream.Subscribe(errors.Add);

            var result = await provider.FetchOhlcvAsync(new MarketDataRequest(
                "OnChain", "ETH_GAS_FAST", "1d", 200,
                Since: Ms(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                Until: Ms(new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc))));

            Assert.Empty(result.Ohlcv);
            Assert.Single(errors);
            Assert.Contains("current reading, not a history", errors[0]);
            // And it does not spend the user's rate-limited quota discovering that.
            Assert.Empty(handler.Captured);
        }

        [Fact]
        public async Task A_live_request_is_still_served()
        {
            // No window, or a window that runs to now, is the overlay case the provider exists
            // for — it must not be caught by the refusal above.
            var provider = NewProvider(new FakeHttpMessageHandler().Get(@"action=gasoracle", GasOracleBody));

            var live = await provider.FetchOhlcvAsync(new MarketDataRequest("OnChain", "ETH_GAS_SAFE", "1d", 1));
            Assert.Single(live.Ohlcv);
            Assert.Equal(12.0, live.Ohlcv[0].Close);

            var toNow = await provider.FetchOhlcvAsync(new MarketDataRequest(
                "OnChain", "ETH_GAS_SAFE", "1d", 200,
                Since: Ms(DateTime.UtcNow.AddDays(-30)),
                Until: Ms(DateTime.UtcNow.AddHours(1))));
            Assert.Single(toNow.Ohlcv);
        }
    }
}
