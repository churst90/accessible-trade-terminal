using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using AccessibleTrader.Plugins.Finra;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fakes;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// FINRA daily short-sale volume provider: Reg SHO pipe-delimited parsing,
    /// short-% math, weekend/holiday handling (404 = no bar, no throw), symbol
    /// suffix convention, and the standard error contract.
    /// </summary>
    public class FinraShortVolumeProviderTests
    {
        private static FinraShortVolumeProvider NewProvider(FakeHttpMessageHandler handler)
        {
            var provider = new FinraShortVolumeProvider();
            var field = typeof(FinraShortVolumeProvider)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .First(f => f.FieldType == typeof(HttpClient));
            field.SetValue(provider, new HttpClient(handler));
            return provider;
        }

        private static string DayBody(string date) => $"""
            Date|Symbol|ShortVolume|ShortExemptVolume|TotalVolume|Market
            {date}|AAPL|600000|100|1000000|B,Q,N
            {date}|TSLA|250000|50|1000000|B,Q,N
            {date}|BAD|abc|0|xyz|B,Q,N
            {date}|ZERO|100|0|0|B,Q,N
            """;

        [Fact]
        public void ParseDayFile_ComputesShortPct_AndSkipsMalformedRows()
        {
            var map = FinraShortVolumeProvider.ParseDayFile(DayBody("20260710"));

            Assert.Equal(60.0, map["AAPL"], 6);
            Assert.Equal(25.0, map["TSLA"], 6);
            Assert.False(map.ContainsKey("BAD"));   // non-numeric volumes
            Assert.False(map.ContainsKey("ZERO"));  // zero total volume
        }

        [Fact]
        public async Task FetchOhlcv_ReturnsDailyShortPct_ForRequestedTicker()
        {
            // Serve the same body for any requested day; 404s never occur here.
            var handler = new FakeHttpMessageHandler()
                .Add(HttpMethod.Get, @"cdn\.finra\.org/equity/regsho/daily/CNMSshvol\d{8}\.txt",
                     req =>
                     {
                         var date = req.RequestUri!.ToString()[^12..^4];
                         return new HttpResponseMessage(HttpStatusCode.OK)
                         { Content = new StringContent(DayBody(date)) };
                     });
            var provider = NewProvider(handler);

            var result = await provider.FetchOhlcvAsync(
                new MarketDataRequest("Derivatives", "AAPL_SHORTVOL", "1d", 10));

            Assert.Equal(10, result.Ohlcv.Count);
            Assert.All(result.Ohlcv, b => Assert.Equal(60.0, b.Close, 6));
            // Chronological, weekdays only.
            for (int i = 1; i < result.Ohlcv.Count; i++)
                Assert.True(result.Ohlcv[i - 1].Date < result.Ohlcv[i].Date);
            Assert.All(result.Ohlcv, b =>
                Assert.True(b.Date.DayOfWeek != DayOfWeek.Saturday && b.Date.DayOfWeek != DayOfWeek.Sunday));
        }

        [Fact]
        public async Task FetchOhlcv_HolidayFileMissing_SkipsDay_NoThrow()
        {
            var handler = new FakeHttpMessageHandler()
                .Get(@"CNMSshvol\d{8}\.txt", "not found", HttpStatusCode.NotFound);
            var provider = NewProvider(handler);

            var result = await provider.FetchOhlcvAsync(
                new MarketDataRequest("Derivatives", "AAPL_SHORTVOL", "1d", 5));

            Assert.Empty(result.Ohlcv);
        }

        [Fact]
        public async Task FetchOhlcv_InvalidSymbol_ReturnsEmpty_WithoutHttp()
        {
            var handler = new FakeHttpMessageHandler();
            var provider = NewProvider(handler);

            var result = await provider.FetchOhlcvAsync(
                new MarketDataRequest("Derivatives", "NOT A TICKER!!", "1d", 5));

            Assert.Empty(result.Ohlcv);
            Assert.Empty(handler.Captured);
        }

        [Fact]
        public async Task DayFiles_AreCached_AcrossSymbols()
        {
            int hits = 0;
            var handler = new FakeHttpMessageHandler()
                .Add(HttpMethod.Get, @"CNMSshvol\d{8}\.txt", req =>
                {
                    hits++;
                    var date = req.RequestUri!.ToString()[^12..^4];
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(DayBody(date)) };
                });
            var provider = NewProvider(handler);

            await provider.FetchOhlcvAsync(new MarketDataRequest("Derivatives", "AAPL_SHORTVOL", "1d", 5));
            int afterFirst = hits;
            var second = await provider.FetchOhlcvAsync(new MarketDataRequest("Derivatives", "TSLA_SHORTVOL", "1d", 5));

            Assert.Equal(afterFirst, hits); // second symbol served entirely from the day cache
            Assert.All(second.Ohlcv, b => Assert.Equal(25.0, b.Close, 6));
        }

        [Fact]
        public void RenderHints_AndDisplayNames_ResolveForTickers()
        {
            var provider = new FinraShortVolumeProvider();
            Assert.NotNull(provider.GetSymbolRenderHints("AAPL_SHORTVOL"));
            Assert.Contains("AAPL", provider.GetSymbolDisplayName("AAPL_SHORTVOL"));
            Assert.Null(provider.GetSymbolRenderHints("NOT A TICKER!!"));
        }
    }
}
