using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using AccessibleTrader.Plugins.Cftc;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// CFTC Commitment-of-Traders provider: Socrata JSON parsing, net-%-of-OI math,
    /// release-date stamping (report Tuesday + 3 days — no lookahead), dataset/field
    /// selection per contract, and the standard provider error contract (malformed /
    /// HTTP-error / unknown-symbol → empty, never throw).
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class CftcProviderTests
    {
        private static CftcProvider NewProvider(FakeHttpMessageHandler handler)
        {
            var provider = new CftcProvider();
            var field = typeof(CftcProvider)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .First(f => f.FieldType == typeof(HttpClient));
            field.SetValue(provider, new HttpClient(handler));
            return provider;
        }

        // Two weekly TFF rows for Bitcoin (report Tuesdays 2026-06-30 and 2026-07-07).
        private const string TffBody = """
            [
              {"report_date_as_yyyy_mm_dd":"2026-06-30T00:00:00.000","open_interest_all":"40000",
               "lev_money_positions_long":"12000","lev_money_positions_short":"8000"},
              {"report_date_as_yyyy_mm_dd":"2026-07-07T00:00:00.000","open_interest_all":"50000",
               "lev_money_positions_long":"10000","lev_money_positions_short":"15000"}
            ]
            """;

        [Fact]
        public async Task HappyPath_TFF_ComputesNetPctOi_AndStampsReleaseFriday()
        {
            var handler = new FakeHttpMessageHandler()
                .Get(@"publicreporting\.cftc\.gov/resource/gpe5-46if\.json", TffBody);
            var provider = NewProvider(handler);

            var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Derivatives", "BITCOIN_COT", "1w", 100));

            Assert.Equal(2, result.Ohlcv.Count);
            // (12000-8000)/40000*100 = +10%; (10000-15000)/50000*100 = -10%
            Assert.Equal(10.0, result.Ohlcv[0].Close, 6);
            Assert.Equal(-10.0, result.Ohlcv[1].Close, 6);
            // Report Tuesday 2026-06-30 → public knowledge Friday 2026-07-03.
            Assert.Equal(new DateTime(2026, 7, 3), result.Ohlcv[0].Date.Date);
            Assert.Equal(new DateTime(2026, 7, 10), result.Ohlcv[1].Date.Date);
            Assert.True(result.Ohlcv[0].Date < result.Ohlcv[1].Date);
        }

        [Fact]
        public async Task CommodityContract_UsesDisaggDataset_AndManagedMoneyFields()
        {
            var handler = new FakeHttpMessageHandler()
                .Get(@"publicreporting\.cftc\.gov/resource/72hh-3qpy\.json", """
                    [
                      {"report_date_as_yyyy_mm_dd":"2026-07-07T00:00:00.000","open_interest_all":"200000",
                       "m_money_positions_long_all":"90000","m_money_positions_short_all":"30000"}
                    ]
                    """);
            var provider = NewProvider(handler);

            var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Derivatives", "GOLD_COT", "1w", 100));

            var bar = Assert.Single(result.Ohlcv);
            Assert.Equal(30.0, bar.Close, 6); // (90000-30000)/200000*100
            var url = Assert.Single(handler.Captured).RequestUri!.ToString();
            Assert.Contains("72hh-3qpy", url);                       // disaggregated dataset
            Assert.Contains("m_money_positions_long_all", url);      // managed-money cohort
            Assert.Contains("cftc_contract_market_code='088691'", Uri.UnescapeDataString(url)); // gold
        }

        [Fact]
        public async Task UnknownSymbol_ReturnsEmpty_WithoutHttpCall()
        {
            var handler = new FakeHttpMessageHandler();
            var provider = NewProvider(handler);

            var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Derivatives", "NOT_A_CONTRACT", "1w", 100));

            Assert.Empty(result.Ohlcv);
            Assert.Empty(handler.Captured);
        }

        [Fact]
        public async Task MalformedJson_ReturnsEmpty_NoThrow()
        {
            var handler = new FakeHttpMessageHandler()
                .Get(@"publicreporting\.cftc\.gov", "not json at all");
            var provider = NewProvider(handler);

            var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Derivatives", "GOLD_COT", "1w", 100));

            Assert.Empty(result.Ohlcv);
        }

        /// <summary>
        /// A 5xx must ESCAPE the provider so DataOrchestrator's retry and circuit breaker
        /// can see it. It used to be swallowed into an empty result, which is what made
        /// the whole resilience layer decorative — and for a research series like COT,
        /// "no bars" and "the server is down" look identical on a chart you cannot see.
        /// See TransportFailure.
        /// </summary>
        [Fact]
        public async Task HttpError_Throws_SoTheResiliencePolicyCanSeeIt()
        {
            var handler = new FakeHttpMessageHandler()
                .Get(@"publicreporting\.cftc\.gov", "{}", HttpStatusCode.InternalServerError);
            var provider = NewProvider(handler);

            await Assert.ThrowsAsync<HttpRequestException>(() =>
                provider.FetchOhlcvAsync(new MarketDataRequest("Derivatives", "GOLD_COT", "1w", 100)));
        }

        [Fact]
        public async Task RowsWithZeroOrMissingOpenInterest_AreSkipped()
        {
            var handler = new FakeHttpMessageHandler()
                .Get(@"publicreporting\.cftc\.gov", """
                    [
                      {"report_date_as_yyyy_mm_dd":"2026-06-30T00:00:00.000","open_interest_all":"0",
                       "lev_money_positions_long":"1","lev_money_positions_short":"2"},
                      {"report_date_as_yyyy_mm_dd":"2026-07-07T00:00:00.000",
                       "lev_money_positions_long":"1","lev_money_positions_short":"2"}
                    ]
                    """);
            var provider = NewProvider(handler);

            var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Derivatives", "BITCOIN_COT", "1w", 100));

            Assert.Empty(result.Ohlcv); // division by ~zero OI must never produce a bar
        }

        [Fact]
        public async Task LimitParameter_KeepsMostRecentBars()
        {
            var handler = new FakeHttpMessageHandler()
                .Get(@"publicreporting\.cftc\.gov", TffBody);
            var provider = NewProvider(handler);

            var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Derivatives", "BITCOIN_COT", "1w", 1));

            var bar = Assert.Single(result.Ohlcv);
            Assert.Equal(new DateTime(2026, 7, 10), bar.Date.Date); // the newer of the two
        }

        [Fact]
        public async Task SymbolList_CoversAllContracts_AndHintsAndNamesResolve()
        {
            var provider = new CftcProvider();
            var symbols = await provider.GetAvailableSymbolsAsync(AccessibleTrader.Sdk.Enums.MarketType.Derivatives);

            Assert.Equal(11, symbols.Count);
            Assert.All(symbols, s =>
            {
                Assert.NotNull(provider.GetSymbolRenderHints(s));
                Assert.NotEqual(s, provider.GetSymbolDisplayName(s)); // every contract has a friendly name
            });
            Assert.Null(provider.GetSymbolRenderHints("NOT_A_CONTRACT"));
        }
    }
}
