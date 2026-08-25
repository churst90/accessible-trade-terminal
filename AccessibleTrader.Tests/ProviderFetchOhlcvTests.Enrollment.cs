using System.Net;
using System.Reflection;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Phase E test-debt enrollment: FetchOhlcvAsync contract coverage for the
    /// providers that were listed in <see cref="ProviderTimeframeContractTests"/>
    /// but missing from <see cref="ProviderFetchOhlcvTests"/> — Binance,
    /// InteractiveBrokers, Schwab, Finnhub, TwelveData, Fmp, Mexc.
    ///
    /// Follows the established harness: construct the provider, swap its
    /// private HttpClient field(s) for one wired to
    /// <see cref="FakeHttpMessageHandler"/>, exercise the parse path with
    /// canned responses, assert shape + values + no-throw on garbage.
    ///
    /// MEXC is the one provider where the canned-HTTP pattern cannot apply
    /// without production changes: its HTTP layer is owned by the JK.Mexc.Net
    /// SDK (MexcRestClient manages its own HttpClient internally). The MEXC
    /// suite below therefore covers only the separable pure helpers
    /// (futures-symbol normalisation, empty-bar filtering) via reflection.
    /// </summary>
    public class ProviderFetchOhlcvEnrollmentTests
    {
        // ── Helpers (mirrors ProviderFetchOhlcvTests) ─────────────────────────

        private static void SwapHttpClient(object provider, FakeHttpMessageHandler handler)
        {
            var fields = provider.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo? target = null;
            foreach (var f in fields)
            {
                if (f.FieldType == typeof(HttpClient)) { target = f; break; }
            }
            if (target == null)
                throw new InvalidOperationException($"{provider.GetType().Name} has no HttpClient-typed private field.");
            target.SetValue(provider, new HttpClient(handler));
        }

        /// <summary>Swaps the HttpClient field on a nested private object (e.g. Schwab's OAuth service).</summary>
        private static void SwapNestedHttpClient(object owner, string nestedFieldName, FakeHttpMessageHandler handler)
        {
            var nestedField = owner.GetType().GetField(nestedFieldName, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"{owner.GetType().Name} has no field {nestedFieldName}.");
            var nested = nestedField.GetValue(owner)
                ?? throw new InvalidOperationException($"{nestedFieldName} is null.");
            SwapHttpClient(nested, handler);
        }

        // ── Binance — public klines, no auth required ─────────────────────────
        // GET /api/v3/klines?symbol=&interval=&limit=  (spot)
        // GET /fapi/v1/klines (futures)
        // Response: [[openTimeMs,"o","h","l","c","v",...],...] — stringified legs.

        [Collection("ProviderCredentialBridge")]
        public class Binance
        {
            private static AccessibleTrader.Plugins.Binance.BinanceProvider NewProvider(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.Binance.BinanceProvider();
                SwapHttpClient(p, h);   // sets _httpField; the lazy Http property keeps it
                return p;
            }

            [Fact]
            public async Task HappyPath_ParsesKlineRows()
            {
                var handler = new FakeHttpMessageHandler().Get(@"api\.binance\.com/api/v3/klines", """
                    [
                      [1700000000000,"50000","50100","49900","50050","1.5",1700000059999,"75000",10,"0.7","35000","0"],
                      [1700000060000,"50050","50200","50000","50150","2.0",1700000119999,"100000",12,"1.0","50000","0"]
                    ]
                    """);
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USDT", "1m", 100));

                Assert.Equal(2, result.Ohlcv.Count);
                Assert.Equal(50000.0, result.Ohlcv[0].Open);
                Assert.Equal(50150.0, result.Ohlcv[1].Close);
                Assert.Equal(1.5, result.Ohlcv[0].Volume);
                Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000).UtcDateTime, result.Ohlcv[0].Date);
                Assert.True(result.Ohlcv[0].Date < result.Ohlcv[1].Date);
                // Volume series parallels the OHLCV series.
                Assert.Equal(result.Ohlcv.Count, result.Volume.Count);
                Assert.Equal(2.0, result.Volume[1].Volume);
            }

            [Fact]
            public async Task SymbolCleaned_OnQueryString()
            {
                // "btc/usdt" must reach the wire as symbol=BTCUSDT (CleanSymbol).
                var handler = new FakeHttpMessageHandler().Get(@"api\.binance\.com/api/v3/klines", "[]");
                var provider = NewProvider(handler);

                await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "btc/usdt", "1m", 100));

                Assert.Single(handler.Captured);
                Assert.Contains("symbol=BTCUSDT", handler.Captured[0].RequestUri!.ToString());
            }

            [Fact]
            public async Task FuturesMarket_RoutesToFapiEndpoint()
            {
                var handler = new FakeHttpMessageHandler().Get(@"fapi\.binance\.com/fapi/v1/klines", "[]");
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Futures", "BTC/USDT", "1h", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Single(handler.Captured);
                Assert.Contains("fapi.binance.com/fapi/v1/klines", handler.Captured[0].RequestUri!.ToString());
            }

            [Fact]
            public async Task MalformedJson_ReturnsEmpty_NoThrow()
            {
                var handler = new FakeHttpMessageHandler().Get(@"api\.binance\.com", "not json");
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USDT", "1m", 100));

                Assert.Empty(result.Ohlcv);
            }

            /// <summary>
            /// A 5xx must ESCAPE the provider. It used to be swallowed into an empty
            /// result — which is what left DataOrchestrator's retry and circuit breaker
            /// unreachable, and an empty chart as the only symptom of a dead upstream.
            /// A 4xx is still swallowed (see the Fmp 403 case): retrying a bad key or an
            /// unknown symbol cannot help. See TransportFailure.
            /// </summary>
            [Fact]
            public async Task HttpErrorStatus_Throws_SoTheResiliencePolicyCanSeeIt()
            {
                var handler = new FakeHttpMessageHandler().Get(@"api\.binance\.com", "{}", HttpStatusCode.InternalServerError);
                var provider = NewProvider(handler);

                await Assert.ThrowsAsync<HttpRequestException>(() =>
                    provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USDT", "1m", 100)));
            }

            [Fact]
            public async Task UnknownTimeframe_FallsBackTo1h_OnWire()
            {
                // MapInterval maps anything unexpected to "1h" rather than erroring.
                var handler = new FakeHttpMessageHandler().Get(@"api\.binance\.com/api/v3/klines", "[]");
                var provider = NewProvider(handler);

                await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USDT", "1Q", 100));

                Assert.Single(handler.Captured);
                Assert.Contains("interval=1h", handler.Captured[0].RequestUri!.ToString());
            }
        }

        // ── Finnhub — auth-gated (token= query param); parallel-array response ─
        // GET /api/v1/stock/candle?symbol=&resolution=&from=&to=&token=
        // Response: {"s":"ok","t":[..],"o":[..],"h":[..],"l":[..],"c":[..],"v":[..]}

        [Collection("ProviderCredentialBridge")]
        public class Finnhub
        {
            private static AccessibleTrader.Plugins.Finnhub.FinnhubProvider NewConfigured(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.Finnhub.FinnhubProvider();
                p.Configure(new Dictionary<string, string> { ["ApiKey"] = "test" });
                SwapHttpClient(p, h);
                return p;
            }

            [Fact]
            public async Task NotConfigured_ReturnsEmpty_NoHttp()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = new AccessibleTrader.Plugins.Finnhub.FinnhubProvider();
                SwapHttpClient(provider, handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1h", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }

            [Fact]
            public async Task HappyPath_ZipsParallelArrays()
            {
                var handler = new FakeHttpMessageHandler().Get(@"finnhub\.io.*candle", """
                    {"s":"ok",
                     "t":[1700000000,1700000060],
                     "o":[100.0,105.0],
                     "h":[110.0,115.0],
                     "l":[95.0,100.0],
                     "c":[105.0,112.0],
                     "v":[1.5,2.0]}
                    """);
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1m", 100));

                Assert.Equal(2, result.Ohlcv.Count);
                Assert.Equal(100.0, result.Ohlcv[0].Open);
                Assert.Equal(112.0, result.Ohlcv[1].Close);
                Assert.Equal(2.0, result.Ohlcv[1].Volume);
                Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime, result.Ohlcv[0].Date);
                Assert.True(result.Ohlcv[0].Date < result.Ohlcv[1].Date);
            }

            [Fact]
            public async Task NoDataStatus_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler().Get(@"finnhub\.io.*candle", """{"s":"no_data"}""");
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1m", 100));

                Assert.Empty(result.Ohlcv);
            }

            [Fact]
            public async Task MalformedJson_ReturnsEmpty_NoThrow()
            {
                var handler = new FakeHttpMessageHandler().Get(@"finnhub\.io", "garbage");
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1m", 100));

                Assert.Empty(result.Ohlcv);
            }

            /// <summary>
            /// A 5xx must ESCAPE the provider. It used to be swallowed into an empty
            /// result — which is what left DataOrchestrator's retry and circuit breaker
            /// unreachable, and an empty chart as the only symptom of a dead upstream.
            /// A 4xx is still swallowed (see the Fmp 403 case): retrying a bad key or an
            /// unknown symbol cannot help. See TransportFailure.
            /// </summary>
            [Fact]
            public async Task HttpErrorStatus_Throws_SoTheResiliencePolicyCanSeeIt()
            {
                var handler = new FakeHttpMessageHandler().Get(@"finnhub\.io", "{}", HttpStatusCode.InternalServerError);
                var provider = NewConfigured(handler);

                await Assert.ThrowsAsync<HttpRequestException>(() =>
                    provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1m", 100)));
            }

            [Fact]
            public async Task TokenEmbedded_OnQueryString()
            {
                var handler = new FakeHttpMessageHandler().Get(@"finnhub\.io", """{"s":"no_data"}""");
                var provider = NewConfigured(handler);

                await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 10));

                Assert.Single(handler.Captured);
                Assert.Contains("token=test", handler.Captured[0].RequestUri!.ToString());
            }

            [Fact]
            public async Task Limit_KeepsMostRecentBars()
            {
                // TakeLast(limit) — the most-recent N bars survive.
                var handler = new FakeHttpMessageHandler().Get(@"finnhub\.io.*candle", """
                    {"s":"ok",
                     "t":[1700000000,1700000060,1700000120],
                     "o":[1.0,2.0,3.0],
                     "h":[1.0,2.0,3.0],
                     "l":[1.0,2.0,3.0],
                     "c":[1.0,2.0,3.0],
                     "v":[1.0,1.0,1.0]}
                    """);
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1m", 2));

                Assert.Equal(2, result.Ohlcv.Count);
                Assert.Equal(2.0, result.Ohlcv[0].Open);
                Assert.Equal(3.0, result.Ohlcv[1].Open);
            }
        }

        // ── TwelveData — auth-gated (apikey= query param) ────────────────────
        // GET /time_series?symbol=&interval=&outputsize=&apikey=
        // Response: {"values":[{"datetime":"yyyy-MM-dd HH:mm:ss","open":"","high":"",...}]}
        // — values are stringified, newest-first; provider sorts ascending.

        [Collection("ProviderCredentialBridge")]
        public class TwelveData
        {
            private static AccessibleTrader.Plugins.TwelveData.TwelveDataProvider NewConfigured(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.TwelveData.TwelveDataProvider();
                p.Configure(new Dictionary<string, string> { ["ApiKey"] = "test" });
                SwapHttpClient(p, h);
                return p;
            }

            [Fact]
            public async Task NotConfigured_ReturnsEmpty_NoHttp()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = new AccessibleTrader.Plugins.TwelveData.TwelveDataProvider();
                SwapHttpClient(provider, handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1h", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }

            [Fact]
            public async Task HappyPath_ParsesNewestFirstValues_SortsAscending()
            {
                var handler = new FakeHttpMessageHandler().Get(@"twelvedata\.com/time_series", """
                    {"values":[
                      {"datetime":"2026-01-02 00:00:00","open":"151.0","high":"152.0","low":"150.0","close":"151.5","volume":"2000"},
                      {"datetime":"2026-01-01 00:00:00","open":"150.0","high":"151.0","low":"149.0","close":"150.5","volume":"1000"}
                    ]}
                    """);
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 100));

                Assert.Equal(2, result.Ohlcv.Count);
                // Reordered ascending: 2026-01-01 first.
                Assert.True(result.Ohlcv[0].Date < result.Ohlcv[1].Date);
                Assert.Equal(150.0, result.Ohlcv[0].Open);
                Assert.Equal(151.5, result.Ohlcv[1].Close);
                Assert.Equal(1000.0, result.Ohlcv[0].Volume);
                Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), result.Ohlcv[0].Date);
            }

            [Fact]
            public async Task ErrorStatus_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler().Get(@"twelvedata\.com/time_series",
                    """{"status":"error","code":401,"message":"invalid api key"}""");
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 100));

                Assert.Empty(result.Ohlcv);
            }

            [Fact]
            public async Task MissingValuesNode_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler().Get(@"twelvedata\.com/time_series", """{"meta":{}}""");
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 100));

                Assert.Empty(result.Ohlcv);
            }

            [Fact]
            public async Task MalformedJson_ReturnsEmpty_NoThrow()
            {
                var handler = new FakeHttpMessageHandler().Get(@"twelvedata\.com", "not-json");
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 100));

                Assert.Empty(result.Ohlcv);
            }

            /// <summary>
            /// A 5xx must ESCAPE the provider. It used to be swallowed into an empty
            /// result — which is what left DataOrchestrator's retry and circuit breaker
            /// unreachable, and an empty chart as the only symptom of a dead upstream.
            /// A 4xx is still swallowed (see the Fmp 403 case): retrying a bad key or an
            /// unknown symbol cannot help. See TransportFailure.
            /// </summary>
            [Fact]
            public async Task HttpErrorStatus_Throws_SoTheResiliencePolicyCanSeeIt()
            {
                var handler = new FakeHttpMessageHandler().Get(@"twelvedata\.com", "{}", HttpStatusCode.InternalServerError);
                var provider = NewConfigured(handler);

                await Assert.ThrowsAsync<HttpRequestException>(() =>
                    provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 100)));
            }

            [Fact]
            public async Task ApiKeyEmbedded_OnQueryString()
            {
                var handler = new FakeHttpMessageHandler().Get(@"twelvedata\.com", """{"values":[]}""");
                var provider = NewConfigured(handler);

                await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 10));

                Assert.Single(handler.Captured);
                Assert.Contains("apikey=test", handler.Captured[0].RequestUri!.ToString());
            }

            [Fact]
            public async Task TimezoneUtc_IsOnEveryTimeSeriesRequest()
            {
                // Without timezone=UTC the venue returns EXCHANGE-LOCAL wall clock
                // (verified live 2026-08-23: AAPL's last bar reads "15:55:00" bare,
                // "19:55:00" with the parameter; BTC/USD is UTC either way), which
                // the AssumeUniversal parse then declares to be UTC — every US-stock
                // intraday bar 4-5 hours out of session, with candles that look
                // plausible so nothing signals it. This pins the parameter so it
                // cannot fall off the URL.
                var handler = new FakeHttpMessageHandler().Get(@"twelvedata\.com", """{"values":[]}""");
                var provider = NewConfigured(handler);

                await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "5m", 10));

                Assert.Contains("timezone=UTC", handler.Captured[0].RequestUri!.ToString());
            }

            [Fact]
            public async Task IntradayBars_ComeOutAsTheUtcInstantTheVenueSent()
            {
                // The response half of the timezone contract: with timezone=UTC the
                // venue's "19:55" IS 19:55Z and must come out as exactly that instant.
                var handler = new FakeHttpMessageHandler().Get(@"twelvedata\.com/time_series", """
                    {"values":[{"datetime":"2026-08-21 19:55:00","open":"1","high":"1","low":"1","close":"1","volume":"1"}]}
                    """);
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "5m", 10));

                var bar = Assert.Single(result.Ohlcv);
                Assert.Equal(new DateTime(2026, 8, 21, 19, 55, 0, DateTimeKind.Utc), bar.Date);
            }
        }

        // ── FMP — auth-gated (apikey= query param); daily vs intraday shapes ──
        // Daily (1d):    GET /historical-price-full/{sym} → {"historical":[{...}]}
        // Intraday:      GET /historical-chart/{interval}/{sym} → top-level [...]

        [Collection("ProviderCredentialBridge")]
        public class Fmp
        {
            private static AccessibleTrader.Plugins.Fmp.FmpProvider NewConfigured(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.Fmp.FmpProvider();
                // Configure first — it creates the internal HttpClient — then swap.
                p.Configure(new Dictionary<string, string> { ["ApiKey"] = "test" });
                SwapHttpClient(p, h);
                return p;
            }

            [Fact]
            public async Task NotConfigured_ReturnsEmpty_NoHttp()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = new AccessibleTrader.Plugins.Fmp.FmpProvider();
                SwapHttpClient(provider, handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }

            [Fact]
            public async Task DailyHappyPath_ParsesFlatArray_SortsAscending()
            {
                // FMP returns daily bars newest-first. UPDATED 2026-08-02 for the /stable migration:
                // v3 wrapped the rows in {"historical": [...]}, /stable returns a flat array.
                var handler = new FakeHttpMessageHandler().Get(@"historical-price-eod/full", """
                    [
                      {"date":"2026-01-02","open":151.0,"high":152.0,"low":150.0,"close":151.5,"volume":2000},
                      {"date":"2026-01-01","open":150.0,"high":151.0,"low":149.0,"close":150.5,"volume":1000}
                    ]
                    """);
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 100));

                Assert.Equal(2, result.Ohlcv.Count);
                Assert.True(result.Ohlcv[0].Date < result.Ohlcv[1].Date);
                Assert.Equal(150.0, result.Ohlcv[0].Open);
                Assert.Equal(151.5, result.Ohlcv[1].Close);
                Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), result.Ohlcv[0].Date);
                Assert.Equal(result.Ohlcv.Count, result.Volume.Count);
                Assert.Equal(1000.0, result.Volume[0].Volume);
            }

            [Fact]
            public async Task IntradayHappyPath_ParsesTopLevelArray()
            {
                var handler = new FakeHttpMessageHandler().Get(@"historical-chart/1hour", """
                    [
                      {"date":"2026-01-01 11:00:00","open":151.0,"high":152.0,"low":150.0,"close":151.5,"volume":2000},
                      {"date":"2026-01-01 10:00:00","open":150.0,"high":151.0,"low":149.0,"close":150.5,"volume":1000}
                    ]
                    """);
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1h", 100));

                Assert.Equal(2, result.Ohlcv.Count);
                Assert.True(result.Ohlcv[0].Date < result.Ohlcv[1].Date);
                Assert.Equal(150.0, result.Ohlcv[0].Open);
                Assert.Equal(151.5, result.Ohlcv[1].Close);
            }

            [Fact]
            public async Task IntradayLimit_KeepsMostRecentBars()
            {
                // Regression pin for a real defect found during Phase E enrollment:
                // FetchIntradayAsync used .Take(request.Limit) after the ascending
                // sort, keeping the OLDEST N bars — every sibling provider (Finnhub,
                // Schwab, Kraken) keeps the MOST-RECENT N via TakeLast. Fixed
                // 2026-07-09; this asserts the corrected behaviour.
                var handler = new FakeHttpMessageHandler().Get(@"historical-chart/1hour", """
                    [
                      {"date":"2026-01-01 11:00:00","open":2.0,"high":2.0,"low":2.0,"close":2.0,"volume":1},
                      {"date":"2026-01-01 10:00:00","open":1.0,"high":1.0,"low":1.0,"close":1.0,"volume":1}
                    ]
                    """);
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1h", 1));

                Assert.Single(result.Ohlcv);
                Assert.Equal(2.0, result.Ohlcv[0].Open);   // the NEWEST bar survives
            }

            [Fact]
            public async Task MalformedJson_ReturnsEmpty_NoThrow()
            {
                var handler = new FakeHttpMessageHandler().Get(@"historical-price-full", "not-json");
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 100));

                Assert.Empty(result.Ohlcv);
            }

            /// <summary>
            /// The counterpart to the 5xx tests above: a 403 is a bad or exhausted key,
            /// not a network fault. Retrying it cannot help and tripping a breaker
            /// labelled "network issue" over it would suspend a provider that is fine,
            /// so it is still reported and swallowed. See TransportFailure.
            /// </summary>
            [Fact]
            public async Task HttpForbidden_ReturnsEmpty_NoThrow()
            {
                var handler = new FakeHttpMessageHandler().Get(@"historical-price-full", "{}", HttpStatusCode.Forbidden);
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 100));

                Assert.Empty(result.Ohlcv);
            }

            [Fact]
            public async Task SymbolCleaned_IntoTheQueryString()
            {
                // "BTC/USD" → CleanSymbol → "BTCUSD". UPDATED 2026-08-02: on /stable the symbol is a
                // QUERY PARAMETER, not a path segment — which matters beyond cosmetics, because a
                // slash left in the symbol used to change the URL's shape rather than its content.
                var handler = new FakeHttpMessageHandler().Get(@"historical-price-eod/full", """[]""");
                var provider = NewConfigured(handler);

                await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USD", "1d", 100));

                Assert.Single(handler.Captured);
                var uri = handler.Captured[0].RequestUri!.ToString();
                Assert.Contains("/stable/historical-price-eod/full?symbol=BTCUSD", uri);
                Assert.DoesNotContain("/api/v3", uri);
            }

            [Theory]
            // Stocks arrive as US-Eastern wall clock: 15:55 EDT (summer) is 19:55Z,
            // and the same wall clock in EST (winter) is 20:55Z — both rows must
            // pass or the "conversion" is a fixed offset, not a time zone.
            [InlineData("Stock", "2026-08-21 15:55:00", "2026-08-21T19:55:00")]
            [InlineData("Stock", "2026-01-16 15:55:00", "2026-01-16T20:55:00")]
            // The other four market types arrive in UTC already (2026-08-21 audit;
            // intraday is plan-gated on the repo's key, so this half rests on the
            // audit) and must pass through untouched — running them through the
            // Eastern conversion is the original 4-5h skew pointing the other way.
            [InlineData("Crypto", "2026-08-21 15:55:00", "2026-08-21T15:55:00")]
            [InlineData("Forex", "2026-08-21 15:55:00", "2026-08-21T15:55:00")]
            [InlineData("Commodity", "2026-08-21 15:55:00", "2026-08-21T15:55:00")]
            [InlineData("Index", "2026-08-21 15:55:00", "2026-08-21T15:55:00")]
            public async Task IntradayWallClock_IsEasternForStocksAndUtcForTheRest(
                string market, string venueWallClock, string expectedUtc)
            {
                var handler = new FakeHttpMessageHandler().Get(@"historical-chart/5min",
                    $$"""[{"date":"{{venueWallClock}}","open":1.0,"high":1.0,"low":1.0,"close":1.0,"volume":1}]""");
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest(market, "SYM", "5m", 10));

                var bar = Assert.Single(result.Ohlcv);
                var expected = DateTime.SpecifyKind(
                    DateTime.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture),
                    DateTimeKind.Utc);
                Assert.Equal(expected, bar.Date);
            }

            [Fact]
            public async Task RetiredApiPathsAreNeverCalled()
            {
                // The whole reason for the migration: /api/v3 and /api/v4 answer 403 "Legacy
                // Endpoint" for every key issued after 2025-08-31, so the provider worked for old
                // keys and was dead for new users. Nothing caught it because the repo's own key was
                // old enough to keep working. This pins the base URL so it cannot regress.
                var handler = new FakeHttpMessageHandler().Get(@"historical-price-eod/full", """[]""");
                var provider = NewConfigured(handler);

                await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 100));

                var uri = handler.Captured[0].RequestUri!.ToString();
                Assert.DoesNotContain("/api/v3", uri);
                Assert.DoesNotContain("/api/v4", uri);
                Assert.Contains("/stable/", uri);
            }
        }

        // ── InteractiveBrokers — local Client Portal gateway (plain HttpClient) ─
        // FetchOhlcv is two-step: POST /iserver/secdef/search → conid, then
        // GET /iserver/marketdata/history?conid=&period=&bar= →
        // {"data":[{"t":ms,"o":,"h":,"l":,"c":,"v":}]}.
        // The gateway session / TLS-pinning concerns live in the transport, not
        // the parse path, so the swap-HttpClient pattern applies cleanly.

        [Collection("ProviderCredentialBridge")]
        public class InteractiveBrokers
        {
            private static AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider NewProvider(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider();
                SwapHttpClient(p, h);
                return p;
            }

            [Fact]
            public async Task HappyPath_ResolvesConId_ThenParsesHistory()
            {
                var handler = new FakeHttpMessageHandler()
                    .Post(@"iserver/secdef/search", """[{"conid":265598,"symbol":"AAPL"}]""")
                    .Get(@"iserver/marketdata/history", """
                        {"data":[
                          {"t":1700000000000,"o":150.0,"h":151.0,"l":149.0,"c":150.5,"v":1000},
                          {"t":1700000060000,"o":150.5,"h":152.0,"l":150.0,"c":151.5,"v":1500}
                        ]}
                        """);
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1m", 100));

                Assert.Equal(2, result.Ohlcv.Count);
                Assert.Equal(150.0, result.Ohlcv[0].Open);
                Assert.Equal(151.5, result.Ohlcv[1].Close);
                Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000).UtcDateTime, result.Ohlcv[0].Date);
                Assert.True(result.Ohlcv[0].Date < result.Ohlcv[1].Date);
                // Both legs hit the gateway: search then history, with the conid on the URL.
                Assert.Equal(2, handler.Captured.Count);
                Assert.Contains("conid=265598", handler.Captured[1].RequestUri!.ToString());
            }

            [Fact]
            public async Task ConIdUnresolvable_ReturnsEmpty_NoHistoryCall()
            {
                var handler = new FakeHttpMessageHandler()
                    .Post(@"iserver/secdef/search", "[]");
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "ZZZZ", "1m", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Single(handler.Captured);   // only the search leg fired
            }

            [Fact]
            public async Task MissingDataNode_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler()
                    .Post(@"iserver/secdef/search", """[{"conid":265598}]""")
                    .Get(@"iserver/marketdata/history", """{"mdAvailability":"S"}""");
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1m", 100));

                Assert.Empty(result.Ohlcv);
            }

            [Fact]
            public async Task MalformedHistoryJson_ReturnsEmpty_NoThrow()
            {
                var handler = new FakeHttpMessageHandler()
                    .Post(@"iserver/secdef/search", """[{"conid":265598}]""")
                    .Get(@"iserver/marketdata/history", "not-json");
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1m", 100));

                Assert.Empty(result.Ohlcv);
            }

            /// <summary>
            /// A 5xx must ESCAPE the provider so DataOrchestrator's retry and circuit
            /// breaker can see it — the conid lookup succeeded, so this is the gateway
            /// failing, not a bad symbol. See TransportFailure.
            /// </summary>
            [Fact]
            public async Task HttpErrorOnHistory_Throws_SoTheResiliencePolicyCanSeeIt()
            {
                var handler = new FakeHttpMessageHandler()
                    .Post(@"iserver/secdef/search", """[{"conid":265598}]""")
                    .Get(@"iserver/marketdata/history", "{}", HttpStatusCode.InternalServerError);
                var provider = NewProvider(handler);

                await Assert.ThrowsAsync<HttpRequestException>(() =>
                    provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1m", 100)));
            }
        }

        // ── Schwab — OAuth2 (refresh-token grant) + pricehistory ─────────────
        // Two HttpClients are in play: the provider's _http AND the same client
        // captured by SchwabOAuthService at construction. Both get swapped so
        // the token exchange AND the market-data call hit the fake handler.
        // The browser-based auth-code flow is bypassed by seeding a refresh
        // token via Configure(Passphrase=...) — SendWithAuthAsync then runs the
        // refresh-token grant against the canned /v1/oauth/token rule.

        [Collection("ProviderCredentialBridge")]
        public class Schwab
        {
            private const string TokenBody = """
                {"access_token":"test-access","refresh_token":"test-refresh","token_type":"Bearer","expires_in":1800}
                """;

            private static AccessibleTrader.Plugins.Schwab.SchwabProvider NewConfigured(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.Schwab.SchwabProvider();
                p.Configure(new Dictionary<string, string>
                {
                    ["ApiKey"]     = "test-client",
                    ["ApiSecret"]  = "test-secret",
                    ["Passphrase"] = "seed-refresh-token",
                });
                SwapHttpClient(p, h);                      // provider's _http
                SwapNestedHttpClient(p, "_oauth", h);      // OAuth service's captured client
                return p;
            }

            [Fact]
            public async Task NotConfigured_ReturnsEmpty_NoHttp()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = new AccessibleTrader.Plugins.Schwab.SchwabProvider();
                SwapHttpClient(provider, handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }

            [Fact]
            public async Task HappyPath_RefreshesToken_ThenParsesCandles()
            {
                var handler = new FakeHttpMessageHandler()
                    .Post(@"schwabapi\.com/v1/oauth/token", TokenBody)
                    .Get(@"marketdata/v1/pricehistory", """
                        {"candles":[
                          {"datetime":1700000000000,"open":150.0,"high":151.0,"low":149.0,"close":150.5,"volume":1000},
                          {"datetime":1700086400000,"open":150.5,"high":152.0,"low":150.0,"close":151.5,"volume":1500}
                        ],"symbol":"AAPL","empty":false}
                        """);
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 100));

                Assert.Equal(2, result.Ohlcv.Count);
                Assert.Equal(150.0, result.Ohlcv[0].Open);
                Assert.Equal(151.5, result.Ohlcv[1].Close);
                Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000).UtcDateTime, result.Ohlcv[0].Date);
                Assert.True(result.Ohlcv[0].Date < result.Ohlcv[1].Date);

                // First captured request is the token grant; second is pricehistory
                // carrying the freshly minted Bearer token.
                Assert.Equal(2, handler.Captured.Count);
                Assert.Contains("/v1/oauth/token", handler.Captured[0].RequestUri!.ToString());
                var auth = handler.Captured[1].Headers.Authorization;
                Assert.NotNull(auth);
                Assert.Equal("Bearer", auth!.Scheme);
                Assert.Equal("test-access", auth.Parameter);
                Assert.Contains("symbol=AAPL", handler.Captured[1].RequestUri!.ToString());
                Assert.Contains("periodType=year", handler.Captured[1].RequestUri!.ToString());
            }

            [Fact]
            public async Task EmptyCandles_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler()
                    .Post(@"schwabapi\.com/v1/oauth/token", TokenBody)
                    .Get(@"marketdata/v1/pricehistory", """{"candles":[],"symbol":"AAPL","empty":true}""");
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 100));

                Assert.Empty(result.Ohlcv);
            }

            [Fact]
            public async Task MalformedBody_ReturnsEmpty_NoThrow()
            {
                var handler = new FakeHttpMessageHandler()
                    .Post(@"schwabapi\.com/v1/oauth/token", TokenBody)
                    .Get(@"marketdata/v1/pricehistory", "not-json");
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 100));

                Assert.Empty(result.Ohlcv);
            }

            /// <summary>
            /// A 5xx must ESCAPE the provider. It used to be swallowed into an empty
            /// result — which is what left DataOrchestrator's retry and circuit breaker
            /// unreachable, and an empty chart as the only symptom of a dead upstream.
            /// A 4xx is still swallowed (see the Fmp 403 case): retrying a bad key or an
            /// unknown symbol cannot help. See TransportFailure.
            /// </summary>
            [Fact]
            public async Task HttpErrorStatus_Throws_SoTheResiliencePolicyCanSeeIt()
            {
                var handler = new FakeHttpMessageHandler()
                    .Post(@"schwabapi\.com/v1/oauth/token", TokenBody)
                    .Get(@"marketdata/v1/pricehistory", "{}", HttpStatusCode.InternalServerError);
                var provider = NewConfigured(handler);

                await Assert.ThrowsAsync<HttpRequestException>(() =>
                    provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 100)));
            }

            [Fact]
            public async Task TokenEndpointRejects_ReturnsEmpty_NoThrow()
            {
                // A dead refresh token surfaces as SchwabReauthRequiredException
                // internally; FetchOhlcvAsync must swallow it into an empty result.
                var handler = new FakeHttpMessageHandler()
                    .Post(@"schwabapi\.com/v1/oauth/token", """{"error":"invalid_grant"}""", HttpStatusCode.BadRequest);
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Single(handler.Captured);   // pricehistory never fired
            }

            [Fact]
            public async Task Limit_KeepsMostRecentBars()
            {
                var handler = new FakeHttpMessageHandler()
                    .Post(@"schwabapi\.com/v1/oauth/token", TokenBody)
                    .Get(@"marketdata/v1/pricehistory", """
                        {"candles":[
                          {"datetime":1700000000000,"open":1.0,"high":1.0,"low":1.0,"close":1.0,"volume":1},
                          {"datetime":1700086400000,"open":2.0,"high":2.0,"low":2.0,"close":2.0,"volume":1},
                          {"datetime":1700172800000,"open":3.0,"high":3.0,"low":3.0,"close":3.0,"volume":1}
                        ]}
                        """);
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 2));

                Assert.Equal(2, result.Ohlcv.Count);
                Assert.Equal(2.0, result.Ohlcv[0].Open);
                Assert.Equal(3.0, result.Ohlcv[1].Open);
            }
        }

        // ── MEXC — SDK-managed HTTP; only pure helpers are testable ──────────
        // MexcProvider delegates all REST/WS traffic to JK.Mexc.Net's
        // MexcRestClient / MexcSocketClient, which construct and own their
        // HttpClient internally. There is no injectable seam, so the canned-HTTP
        // FetchOhlcv pattern CANNOT apply without production changes (e.g.
        // per-plugin HttpClient injection). What we can pin without touching
        // production: the futures-symbol transform and the empty-bar filter,
        // both private statics reached via reflection. CleanSymbol coverage for
        // MEXC already lives in ProviderSymbolNormalisationTests.

        [Collection("ProviderCredentialBridge")]
        public class Mexc
        {
            private static T InvokeStatic<T>(string method, params object[] args)
            {
                var m = typeof(AccessibleTrader.Plugins.Mexc.MexcProvider).GetMethod(
                    method, BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new MissingMethodException($"MexcProvider.{method} not found.");
                return (T)m.Invoke(null, args)!;
            }

            [Theory]
            [InlineData("BTCUSDT",  "BTC_USDT")]   // USDT quote split
            [InlineData("ETHUSDC",  "ETH_USDC")]   // USDC quote split
            [InlineData("SOLBTC",   "SOL_BTC")]    // BTC quote split
            [InlineData("BTC_USDT", "BTC_USDT")]   // already futures-shaped → passthrough
            [InlineData("USDT",     "USDT")]       // quote alone (length guard) → passthrough
            [InlineData("ABCXYZ",   "ABCXYZ")]     // unknown quote → passthrough
            public void ToFuturesSymbol_SplitsKnownQuotes(string input, string expected)
            {
                Assert.Equal(expected, InvokeStatic<string>("ToFuturesSymbol", input));
            }

            [Fact]
            public void IsEmptyBar_TrueOnlyForAllZeroSentinelBar()
            {
                var epoch = DateTimeOffset.FromUnixTimeMilliseconds(0).UtcDateTime;
                var empty = new Ohlcv(epoch, 0, 0, 0, 0, 0);
                var real  = new Ohlcv(DateTime.UtcNow, 50000, 50100, 49900, 50050, 1.5);
                var zeroPriceRealDate = new Ohlcv(DateTime.UtcNow, 0, 0, 0, 0, 0);

                Assert.True(InvokeStatic<bool>("IsEmptyBar", empty));
                Assert.False(InvokeStatic<bool>("IsEmptyBar", real));
                // All-zero legs but a real timestamp is NOT treated as empty.
                Assert.False(InvokeStatic<bool>("IsEmptyBar", zeroPriceRealDate));
            }
        }
    }
}
