using System.Net;
using System.Reflection;
using AccessibleTrader.Plugins.Finra;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// FINRA daily short-sale volume provider: Reg SHO pipe-delimited parsing,
    /// short-% math, weekend/holiday handling (404 = no bar, no throw), symbol
    /// suffix convention, and the standard error contract.
    /// </summary>
    [Collection("ProviderCredentialBridge")]
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

        // ── Short interest (Query API, OTC-only) ─────────────────────────────

        private const string ShortIntJson = """
            [
              {"settlementDate":"2026-06-15","currentShortPositionQuantity":839207,
               "daysToCoverQuantity":2.5,"securitiesInformationProcessorSymbolIdentifier":"AABB"},
              {"settlementDate":"2026-06-30","currentShortPositionQuantity":900000,
               "daysToCoverQuantity":3.1,"securitiesInformationProcessorSymbolIdentifier":"AABB"}
            ]
            """;

        [Theory]
        [InlineData("AABB_SHORTINT", "ShortInterest")]
        [InlineData("AABB_DTC", "DaysToCover")]
        [InlineData("AABB_SHORTVOL", "ShortVolume")]
        [InlineData("AABB", "ShortVolume")]
        public void ParseSymbol_RoutesSuffixesToSeriesKinds(string symbol, string expectedKind)
        {
            var (ticker, kind) = FinraShortVolumeProvider.ParseSymbol(symbol);
            Assert.Equal("AABB", ticker);
            Assert.Equal(expectedKind, kind.ToString());
        }

        [Fact]
        public void ParseShortInterestJson_StampsPublicationLag_AndSorts()
        {
            var rows = FinraShortVolumeProvider.ParseShortInterestJson(ShortIntJson);

            Assert.Equal(2, rows.Count);
            // Settlement 2026-06-15 → public knowledge ~13 calendar days later.
            Assert.Equal(new DateTime(2026, 6, 28), rows[0].Date.Date);
            Assert.Equal(839207, rows[0].Shares, 3);
            Assert.Equal(2.5, rows[0].Dtc, 3);
            Assert.True(rows[0].Date < rows[1].Date);
        }

        [Fact]
        public async Task FetchShortInterest_ReturnsSeries_AndCachesPerTicker()
        {
            int hits = 0;
            var handler = new FakeHttpMessageHandler()
                .Add(HttpMethod.Post, @"api\.finra\.org/data/group/otcMarket", req =>
                {
                    hits++;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(ShortIntJson) };
                });
            var provider = NewProvider(handler);

            var si = await provider.FetchOhlcvAsync(new MarketDataRequest("Derivatives", "AABB_SHORTINT", "1d", 100));
            var dtc = await provider.FetchOhlcvAsync(new MarketDataRequest("Derivatives", "AABB_DTC", "1d", 100));

            Assert.Equal(2, si.Ohlcv.Count);
            Assert.Equal(900000, si.Ohlcv[1].Close, 3);
            Assert.Equal(3.1, dtc.Ohlcv[1].Close, 3);
            Assert.Equal(1, hits); // second series served from the per-ticker cache
        }

        [Fact]
        public async Task FetchShortInterest_ListedSymbol_NoContent_ReturnsEmpty()
        {
            // Exchange-listed names aren't in the OTC dataset — the API answers 204.
            var handler = new FakeHttpMessageHandler()
                .Add(HttpMethod.Post, @"api\.finra\.org/data/group/otcMarket", _ =>
                    new HttpResponseMessage(HttpStatusCode.NoContent));
            var provider = NewProvider(handler);

            var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Derivatives", "AAPL_SHORTINT", "1d", 100));

            Assert.Empty(result.Ohlcv);
        }

        [Fact]
        public void RenderHints_DifferPerSeriesKind()
        {
            var provider = new FinraShortVolumeProvider();
            Assert.Contains("days to cover", provider.GetSymbolRenderHints("AABB_DTC")!.SpeechTemplate!);
            Assert.Contains("shares short", provider.GetSymbolRenderHints("AABB_SHORTINT")!.SpeechTemplate!);
            Assert.Contains("OTC only", provider.GetSymbolDisplayName("AABB_SHORTINT"));
        }
    }
}
