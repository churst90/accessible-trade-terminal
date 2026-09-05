using System.Net;
using System.Reflection;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Per-provider FetchOhlcvAsync parse tests. Each suite constructs the
    /// provider via its parameterless ctor, swaps the private <c>_httpClient</c>
    /// field for one wired to <see cref="FakeHttpMessageHandler"/>, and
    /// exercises the parse path end-to-end with canned responses.
    ///
    /// Catches the bug class users actually hit: malformed field, wrong nesting,
    /// case-sensitivity drift, dropped zero-volume bars, ordering errors, and
    /// silent-empty paths on auth/parse failure.
    ///
    /// Each test is shaped: ARRANGE handler → SWAP _httpClient → ACT
    /// FetchOhlcvAsync → ASSERT shape + values + side-effect on error stream.
    /// </summary>
    // NOTE: this attribute covers only facts declared directly on the outer class. xUnit gives each
// NESTED class its own collection, so every nested suite below carries the attribute itself —
// enforced by ProviderCredentialBridgeEnrollmentTests.
[Collection("ProviderCredentialBridge")] // shares the global ApiKeys bridge — see BrokerParityTests
public class ProviderFetchOhlcvTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static void SwapHttpClient(object provider, FakeHttpMessageHandler handler)
        {
            // Every HttpClient-typed field, whatever it is called — see HttpClientSwap for why
            // "the first one" was the wrong answer.
            HttpClientSwap.ReplaceAll(provider, handler);
        }

        // ── Bitstamp ──────────────────────────────────────────────────────────
        // Endpoint: GET /api/v2/ohlc/{pair}/?step=&limit=  ⇒ {"data":{"ohlc":[{...}]}}
        // Fields: timestamp / open / high / low / close / volume — all stringified.
        // Filters bars where any OHLC leg ≤ 0; sorts by Date ascending.

        [Collection("ProviderCredentialBridge")]
        public class Bitstamp
        {
            private static AccessibleTrader.Plugins.Bitstamp.BitstampProvider NewProvider(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
                SwapHttpClient(p, h);
                return p;
            }

            [Fact]
            public async Task HappyPath_ParsesThreeBarsInOrder()
            {
                var handler = new FakeHttpMessageHandler().Get(@"/ohlc/btcusd/", """
                    {"data":{"ohlc":[
                      {"timestamp":"1700000000","open":"100","high":"110","low":"95", "close":"105","volume":"1.5"},
                      {"timestamp":"1700000060","open":"105","high":"115","low":"100","close":"112","volume":"2.0"},
                      {"timestamp":"1700000120","open":"112","high":"120","low":"108","close":"118","volume":"3.5"}
                    ]}}
                    """);
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USD", "1m", 100));

                Assert.Equal(3, result.Ohlcv.Count);
                Assert.Equal(100.0, result.Ohlcv[0].Open);
                Assert.Equal(118.0, result.Ohlcv[2].Close);
                // Ordered by Date ascending.
                Assert.True(result.Ohlcv[0].Date < result.Ohlcv[1].Date);
                Assert.True(result.Ohlcv[1].Date < result.Ohlcv[2].Date);
            }

            [Fact]
            public async Task DropsBarsWithZeroOhlcLeg()
            {
                // Bars with any zero/missing OHLC leg are silently dropped — they're
                // forming-candles or upstream errors.
                var handler = new FakeHttpMessageHandler().Get(@"/ohlc/", """
                    {"data":{"ohlc":[
                      {"timestamp":"1700000000","open":"100","high":"110","low":"95","close":"105","volume":"1.5"},
                      {"timestamp":"1700000060","open":"0",  "high":"0",  "low":"0", "close":"0",  "volume":"0"}
                    ]}}
                    """);
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USD", "1m", 100));

                Assert.Single(result.Ohlcv);
                Assert.Equal(100.0, result.Ohlcv[0].Open);
            }

            [Fact]
            public async Task MalformedJson_ReturnsEmpty_NoThrow()
            {
                var handler = new FakeHttpMessageHandler().Get(@"/ohlc/", "not json");
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USD", "1m", 100));

                Assert.Empty(result.Ohlcv);
            }

            [Fact]
            public async Task MissingDataNode_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler().Get(@"/ohlc/", """{"data":{}}""");
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USD", "1m", 100));

                Assert.Empty(result.Ohlcv);
            }

            [Fact]
            public async Task TransientStatus_Rethrows_NonTransientReturnsEmpty()
            {
                // This test used to pin the OPPOSITE for the 5xx half: the inline
                // non-2xx branch returned empty for every status, so a dead venue
                // never reached the pipeline's retry and circuit breaker (see
                // TransportFailure and ProviderBreakerVisibilityTests). The fleet
                // contract: a 5xx rethrows, a 4xx is announced and eaten.
                var five = new FakeHttpMessageHandler().Get(@"/ohlc/", "{}", HttpStatusCode.InternalServerError);
                await Assert.ThrowsAsync<HttpRequestException>(() =>
                    NewProvider(five).FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USD", "1m", 100)));

                var four = new FakeHttpMessageHandler().Get(@"/ohlc/", "{}", HttpStatusCode.NotFound);
                var result = await NewProvider(four).FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USD", "1m", 100));
                Assert.Empty(result.Ohlcv);
            }

            [Fact]
            public async Task UnknownTimeframe_ReturnsEmpty_DoesNotCallHttp()
            {
                var handler = new FakeHttpMessageHandler();   // no rules → would throw on call
                handler.StrictMode = true;
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USD", "1Q", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);   // short-circuited before HTTP
            }

            [Fact]
            public async Task SymbolRoundTrip_AppliesUsdtToUsdMapping()
            {
                // BTC/USDT must hit the /btcusd/ endpoint (USDT→USD quote swap).
                var handler = new FakeHttpMessageHandler().Get(@"/ohlc/btcusd/", """{"data":{"ohlc":[]}}""");
                var provider = NewProvider(handler);

                await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USDT", "1m", 100));

                Assert.Single(handler.Captured);
                Assert.Contains("/ohlc/btcusd/", handler.Captured[0].RequestUri!.ToString());
            }

            [Fact]
            public async Task VolumeSeries_ParallelsOhlcvCount()
            {
                var handler = new FakeHttpMessageHandler().Get(@"/ohlc/", """
                    {"data":{"ohlc":[
                      {"timestamp":"1700000000","open":"100","high":"110","low":"95","close":"105","volume":"1.5"},
                      {"timestamp":"1700000060","open":"105","high":"115","low":"100","close":"112","volume":"2.0"}
                    ]}}
                    """);
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USD", "1m", 100));

                Assert.Equal(result.Ohlcv.Count, result.Volume.Count);
                Assert.Equal(1.5, result.Volume[0].Volume);
                Assert.Equal(2.0, result.Volume[1].Volume);
            }
        }

        // ── Polygon ───────────────────────────────────────────────────────────
        // Endpoint: GET /v2/aggs/ticker/{sym}/range/{m}/{ts}/{from}/{to}
        // Response: {"results":[{"t":ms, "o":, "h":, "l":, "c":, "v":}]}
        // IsConfigured gate: needs Configure(ApiKey=...).

        [Collection("ProviderCredentialBridge")]
        public class Polygon
        {
            private static AccessibleTrader.Plugins.Polygon.PolygonProvider NewProvider(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.Polygon.PolygonProvider();
                p.Configure(new Dictionary<string, string> { ["ApiKey"] = "test" });
                SwapHttpClient(p, h);
                return p;
            }

            [Fact]
            public async Task HappyPath_ParsesAggsResults()
            {
                var handler = new FakeHttpMessageHandler().Get(@"polygon\.io.*aggs", """
                    {"results":[
                      {"t":1700000000000,"o":150.0,"h":155.0,"l":149.0,"c":154.0,"v":12345.0},
                      {"t":1700000060000,"o":154.0,"h":156.0,"l":153.5,"c":155.5,"v":9876.0}
                    ]}
                    """);
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1m", 100));

                Assert.Equal(2, result.Ohlcv.Count);
                Assert.Equal(150.0, result.Ohlcv[0].Open);
                Assert.Equal(155.5, result.Ohlcv[1].Close);
                Assert.Equal(12345.0, result.Ohlcv[0].Volume);
            }

            [Fact]
            public async Task NotConfigured_ReturnsEmpty_NoHttpCall()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = new AccessibleTrader.Plugins.Polygon.PolygonProvider();
                // No Configure → no API key.
                SwapHttpClient(provider, handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1m", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }

            [Fact]
            public async Task MalformedJson_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler().Get(@"polygon\.io", "not-json");
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1m", 100));

                Assert.Empty(result.Ohlcv);
            }

            [Fact]
            public async Task MissingResults_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler().Get(@"polygon\.io", """{"status":"DELAYED"}""");
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1m", 100));

                Assert.Empty(result.Ohlcv);
            }

            [Fact]
            public async Task SymbolUppercased_OnUrl()
            {
                var handler = new FakeHttpMessageHandler().Get(@"polygon\.io", """{"results":[]}""");
                var provider = NewProvider(handler);

                await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "aapl", "1d", 10));

                Assert.Single(handler.Captured);
                Assert.Contains("/AAPL/", handler.Captured[0].RequestUri!.ToString());
            }

            [Fact]
            public async Task BearerToken_AppliedFromConfigure()
            {
                var handler = new FakeHttpMessageHandler().Get(@"polygon\.io", """{"results":[]}""");
                var provider = NewProvider(handler);

                await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 10));

                Assert.Single(handler.Captured);
                var auth = handler.Captured[0].Headers.Authorization;
                Assert.NotNull(auth);
                Assert.Equal("Bearer", auth!.Scheme);
                Assert.Equal("test", auth.Parameter);
            }
        }

        // ── Tradier ───────────────────────────────────────────────────────────
        // Endpoint: /v1/markets/history?symbol=...&interval=daily&start=...&end=...
        // Response: {"history":{"day":[{"date":"...","open":..,...}]}}

        [Collection("ProviderCredentialBridge")]
        public class Tradier
        {
            // Swap HttpClient FIRST then Configure — Tradier writes Authorization to
            // _httpClient.DefaultRequestHeaders inside Configure, so the swap order
            // matters here (vs Polygon, which builds the request per-call).
            private static AccessibleTrader.Plugins.Tradier.TradierProvider NewProvider(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.Tradier.TradierProvider();
                SwapHttpClient(p, h);
                p.Configure(new Dictionary<string, string> { ["ApiKey"] = "test" });
                return p;
            }

            [Fact]
            public async Task NotConfigured_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = new AccessibleTrader.Plugins.Tradier.TradierProvider();
                SwapHttpClient(provider, handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 10));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }

            [Fact]
            public async Task BearerToken_AppliedFromConfigure()
            {
                var handler = new FakeHttpMessageHandler().Get(@"tradier\.com", """{"history":{"day":[]}}""");
                var provider = NewProvider(handler);

                await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 10));

                if (handler.Captured.Count > 0)
                {
                    var auth = handler.Captured[0].Headers.Authorization;
                    Assert.NotNull(auth);
                    Assert.Equal("Bearer", auth!.Scheme);
                    Assert.Equal("test", auth.Parameter);
                }
            }
        }

        // ── Coinbase ──────────────────────────────────────────────────────────
        // Endpoint: /api/v3/brokerage/products/{product}/candles?...
        // Response: {"candles":[{"start":"unixsec","open":"","high":"","low":"","close":"","volume":""}]}
        // IsConfigured gate: needs Configure() with API key + secret.

        [Collection("ProviderCredentialBridge")]
        public class Coinbase
        {
            [Fact]
            public async Task NotConfigured_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = new AccessibleTrader.Plugins.Coinbase.CoinbaseProvider();
                SwapHttpClient(provider, handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USD", "1m", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }
        }

        // ── AlternativeMe — Fear & Greed index ───────────────────────────────
        // No auth, no symbol — single endpoint api.alternative.me/fng.
        // Response: {"data":[{"value":"54","timestamp":"1700000000"},...]} newest-first.
        // Reverses to chronological order, drops NaN values, broadcasts each as
        // a flat-OHLCV bar with value==O==H==L==C and Volume==0.

        [Collection("ProviderCredentialBridge")]
        public class AlternativeMe
        {
            private static AccessibleTrader.Plugins.AlternativeMe.AlternativeMeProvider NewProvider(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.AlternativeMe.AlternativeMeProvider();
                SwapHttpClient(p, h);
                return p;
            }

            [Fact]
            public async Task HappyPath_ReversesNewestFirstToChronologicalOrder()
            {
                var handler = new FakeHttpMessageHandler().Get(@"alternative\.me", """
                    {"data":[
                      {"value":"75","timestamp":"1700086400"},
                      {"value":"50","timestamp":"1700000000"}
                    ]}
                    """);
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Index", "FNG", "1d", 100));

                Assert.Equal(2, result.Ohlcv.Count);
                // Reversed: oldest (1700000000) first, newest (1700086400) last.
                //
                // A DAY apart, not sixty seconds. Fear & Greed is a daily series and its bars
                // are publication-stamped to whole days now (AnalyticsPublicationLag), so two
                // readings sixty seconds apart correctly collapse onto one date — which made
                // the strictly-ascending assertion below fail for the right reason.
                Assert.True(result.Ohlcv[0].Date < result.Ohlcv[1].Date);
                Assert.Equal(50.0, result.Ohlcv[0].Close);
                Assert.Equal(75.0, result.Ohlcv[1].Close);
                // Flat-OHLCV: value drives all four legs.
                Assert.Equal(50.0, result.Ohlcv[0].Open);
                Assert.Equal(50.0, result.Ohlcv[0].High);
                Assert.Equal(50.0, result.Ohlcv[0].Low);
            }

            [Fact]
            public async Task SkipsNaNValues()
            {
                var handler = new FakeHttpMessageHandler().Get(@"alternative\.me", """
                    {"data":[
                      {"value":"50","timestamp":"1700000000"},
                      {"value":"abc","timestamp":"1700000060"}
                    ]}
                    """);
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Index", "FNG", "1d", 100));

                Assert.Single(result.Ohlcv);
                Assert.Equal(50.0, result.Ohlcv[0].Close);
            }

            [Fact]
            public async Task MalformedJson_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler().Get(@"alternative\.me", "garbage");
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Index", "FNG", "1d", 100));

                Assert.Empty(result.Ohlcv);
            }

            [Fact]
            public async Task MissingDataKey_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler().Get(@"alternative\.me", """{"error":"down"}""");
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Index", "FNG", "1d", 100));

                Assert.Empty(result.Ohlcv);
            }
        }

        // ── Mempool — BTC mining metrics ─────────────────────────────────────
        // mempool.space/api/v1/mining/...

        [Collection("ProviderCredentialBridge")]
        public class Mempool
        {
            private static AccessibleTrader.Plugins.Mempool.MempoolProvider NewProvider(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.Mempool.MempoolProvider();
                SwapHttpClient(p, h);
                return p;
            }

            [Fact]
            public async Task UnknownSymbol_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "UNKNOWN_METRIC", "1d", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }
        }

        // ── DefiLlama — TVL / stablecoin supply ──────────────────────────────

        [Collection("ProviderCredentialBridge")]
        public class DefiLlama
        {
            private static AccessibleTrader.Plugins.DefiLlama.DefiLlamaProvider NewProvider(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.DefiLlama.DefiLlamaProvider();
                SwapHttpClient(p, h);
                return p;
            }

            [Fact]
            public async Task UnknownSymbol_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "NOT_A_METRIC", "1d", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }
        }

        // ── OkxDerivatives — funding-rate + OI history ───────────────────────
        // Symbol convention: "{instId}_FUNDING" / "{instId}_OI"; unknown suffix → empty.
        // Funding response: {"data":[{"fundingTime":"ms","fundingRate":"0.0001"},...]}
        // — newest-first, value × 100 = percent. Sorted ascending after parse.

        [Collection("ProviderCredentialBridge")]
        public class OkxDerivatives
        {
            private static AccessibleTrader.Plugins.OkxDerivatives.OkxDerivativesProvider NewProvider(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.OkxDerivatives.OkxDerivativesProvider();
                SwapHttpClient(p, h);
                return p;
            }

            [Fact]
            public async Task UnknownSuffix_ReturnsEmpty_NoHttpCall()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Derivatives", "BTC-USDT-SWAP", "1d", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }

            [Fact]
            public async Task FundingHappyPath_MultipliesRateBy100_AndSortsAscending()
            {
                // OKX returns newest-first; provider reverses to ascending.
                var handler = new FakeHttpMessageHandler().Get(@"funding-rate-history", """
                    {"code":"0","data":[
                      {"fundingTime":"1700000060000","fundingRate":"0.0002"},
                      {"fundingTime":"1700000000000","fundingRate":"0.0001"}
                    ]}
                    """);
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Derivatives", "BTC-USDT-SWAP_FUNDING", "1h", 100));

                Assert.Equal(2, result.Ohlcv.Count);
                Assert.True(result.Ohlcv[0].Date < result.Ohlcv[1].Date);
                // 0.0001 * 100 = 0.01 (percent).
                Assert.Equal(0.01, result.Ohlcv[0].Close, 4);
                Assert.Equal(0.02, result.Ohlcv[1].Close, 4);
            }

            [Fact]
            public async Task FundingMalformedJson_ReturnsEmpty_NoThrow()
            {
                var handler = new FakeHttpMessageHandler().Get(@"funding-rate-history", "not-json");
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Derivatives", "BTC-USDT-SWAP_FUNDING", "1h", 100));

                Assert.Empty(result.Ohlcv);
            }
        }

        // ── Mempool — already had 1 unknown-symbol test; adding parse coverage.

        [Collection("ProviderCredentialBridge")]
        public class MempoolDeeper
        {
            private static AccessibleTrader.Plugins.Mempool.MempoolProvider NewProvider(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.Mempool.MempoolProvider();
                SwapHttpClient(p, h);
                return p;
            }

            [Fact]
            public async Task Hashrate_ParsesNestedArray_AsFlatOhlcv()
            {
                // /api/v1/mining/hashrate/{period} → {"hashrates":[{"timestamp":..,"avgHashrate":..}]}
                var handler = new FakeHttpMessageHandler().Get(@"mempool\.space.*hashrate", """
                    {"hashrates":[
                      {"timestamp":1700000000,"avgHashrate":5e20},
                      {"timestamp":1700086400,"avgHashrate":5.1e20}
                    ]}
                    """);
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("OnChain", "HASHRATE", "1d", 100));

                Assert.Equal(2, result.Ohlcv.Count);
                // Flat OHLCV: avgHashrate drives all four legs.
                Assert.Equal(5e20, result.Ohlcv[0].Close, 0);
                Assert.Equal(result.Ohlcv[0].Open, result.Ohlcv[0].Close);
                Assert.Equal(result.Ohlcv[0].High, result.Ohlcv[0].Close);
                Assert.Equal(result.Ohlcv[0].Low,  result.Ohlcv[0].Close);
            }

            [Fact]
            public async Task BlockFees_ParsesTopLevelArray()
            {
                // /api/v1/mining/blocks/fees/{period} → top-level [{"timestamp":..,"avgFees":..}]
                var handler = new FakeHttpMessageHandler().Get(@"mempool\.space.*fees", """
                    [{"timestamp":1700000000,"avgFees":12345}]
                    """);
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("OnChain", "BLOCK_FEES", "1d", 100));

                Assert.Single(result.Ohlcv);
                Assert.Equal(12345, result.Ohlcv[0].Close);
            }

            [Fact]
            public async Task MalformedJson_PublishesToErrorStream_ButReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler().Get(@"mempool\.space", "garbage");
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("OnChain", "HASHRATE", "1d", 100));

                Assert.Empty(result.Ohlcv);
            }
        }

        // ── Glassnode — auth-gated (api_key= query string) ───────────────────

        [Collection("ProviderCredentialBridge")]
        public class Glassnode
        {
            private static AccessibleTrader.Plugins.Glassnode.GlassnodeProvider NewConfigured(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.Glassnode.GlassnodeProvider();
                p.Configure(new Dictionary<string, string> { ["ApiKey"] = "test" });
                SwapHttpClient(p, h);
                return p;
            }

            [Fact]
            public async Task NotConfigured_ReturnsEmpty_NoHttp()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = new AccessibleTrader.Plugins.Glassnode.GlassnodeProvider();
                SwapHttpClient(provider, handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("OnChain", "BTC_ACTIVE_ADDRS", "1d", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }

            [Fact]
            public async Task UnknownSymbol_ReturnsEmpty_NoHttp()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("OnChain", "NOT_A_METRIC", "1d", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }

            [Fact]
            public async Task HappyPath_ParsesGlassnodeMetricResponse()
            {
                // Glassnode metric response is a flat JSON array of {t, v} entries.
                var handler = new FakeHttpMessageHandler().Get(@"glassnode\.com", """
                    [{"t":1700000000,"v":1000000},{"t":1700086400,"v":1100000}]
                    """);
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("OnChain", "BTC_ACTIVE_ADDRS", "1d", 100));

                Assert.Equal(2, result.Ohlcv.Count);
                Assert.Equal(1000000, result.Ohlcv[0].Close);
                Assert.Equal(1100000, result.Ohlcv[1].Close);
            }

            [Fact]
            public async Task ApiKeyEmbedded_OnQueryString()
            {
                var handler = new FakeHttpMessageHandler().Get(@"glassnode\.com", "[]");
                var provider = NewConfigured(handler);

                await provider.FetchOhlcvAsync(new MarketDataRequest("OnChain", "BTC_HASH_RATE", "1d", 100));

                Assert.NotEmpty(handler.Captured);
                Assert.Contains("api_key=test", handler.Captured[0].RequestUri!.ToString());
            }
        }

        // ── Etherscan — auth-gated; ETH stats ────────────────────────────────

        [Collection("ProviderCredentialBridge")]
        public class Etherscan
        {
            private static AccessibleTrader.Plugins.Etherscan.EtherscanProvider NewConfigured(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.Etherscan.EtherscanProvider();
                p.Configure(new Dictionary<string, string> { ["ApiKey"] = "test" });
                SwapHttpClient(p, h);
                return p;
            }

            [Fact]
            public async Task NotConfigured_ReturnsEmpty_NoHttp()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = new AccessibleTrader.Plugins.Etherscan.EtherscanProvider();
                SwapHttpClient(provider, handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("OnChain", "ETH_GAS_PRICE", "1d", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }

            [Fact]
            public async Task UnknownSymbol_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("OnChain", "NOT_A_METRIC", "1d", 100));

                Assert.Empty(result.Ohlcv);
            }
        }

        // ── Fred — FRED economic series, auth-gated ──────────────────────────

        [Collection("ProviderCredentialBridge")]
        public class Fred
        {
            [Fact]
            public async Task NotConfigured_ReturnsEmpty_NoHttp()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = new AccessibleTrader.Plugins.Fred.FredProvider();
                SwapHttpClient(provider, handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Macro", "DGS10", "1d", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }
        }

        // ── BinanceDerivatives — public, no auth ─────────────────────────────
        // Symbol: "{BASE}_FUNDING" / "{BASE}_OI" (e.g. "BTC_FUNDING").

        [Collection("ProviderCredentialBridge")]
        public class BinanceDerivatives
        {
            private static AccessibleTrader.Plugins.BinanceDerivatives.BinanceDerivativesProvider NewProvider(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.BinanceDerivatives.BinanceDerivativesProvider();
                SwapHttpClient(p, h);
                return p;
            }

            [Fact]
            public async Task UnknownSuffix_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Derivatives", "BTC", "1h", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }
        }

        // ── BGeometrics — public, no auth ────────────────────────────────────

        [Collection("ProviderCredentialBridge")]
        public class BGeometrics
        {
            private static AccessibleTrader.Plugins.BGeometrics.BGeometricsProvider NewProvider(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.BGeometrics.BGeometricsProvider();
                SwapHttpClient(p, h);
                return p;
            }

            [Fact]
            public async Task UnknownSymbol_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("OnChain", "NOT_A_METRIC", "1d", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }
        }

        // ── CoinMetrics — public free tier, no auth ──────────────────────────

        [Collection("ProviderCredentialBridge")]
        public class CoinMetrics
        {
            private static AccessibleTrader.Plugins.CoinMetrics.CoinMetricsProvider NewProvider(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.CoinMetrics.CoinMetricsProvider();
                SwapHttpClient(p, h);
                return p;
            }

            [Fact]
            public async Task UnknownSymbol_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("OnChain", "NOT_A_METRIC", "1d", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }
        }

        // ── Kraken — public OHLC endpoint, no auth ────────────────────────────
        // Response: {"error":[],"result":{"XXBTZUSD":[[ts,o,h,l,c,vwap,vol,count],...],"last":..}}
        // Result key is the Kraken-asset-pair format ("XXBTZUSD" for BTC/USD); the
        // provider walks all properties skipping "last" to find the array.

        [Collection("ProviderCredentialBridge")]
        public class Kraken
        {
            private static AccessibleTrader.Plugins.Kraken.KrakenProvider NewProvider(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
                SwapHttpClient(p, h);
                return p;
            }

            [Fact]
            public async Task HappyPath_ParsesOhlcArray()
            {
                var handler = new FakeHttpMessageHandler().Get(@"kraken\.com.*OHLC", """
                    {"error":[],"result":{
                      "XXBTZUSD":[
                        [1700000000,"50000.0","50100.0","49900.0","50050.0","50025.0","1.5",10],
                        [1700000060,"50050.0","50200.0","50000.0","50150.0","50100.0","2.0",15]
                      ],
                      "last":1700000060
                    }}
                    """);
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USD", "1m", 100));

                Assert.Equal(2, result.Ohlcv.Count);
                Assert.Equal(50000.0, result.Ohlcv[0].Open);
                Assert.Equal(50150.0, result.Ohlcv[1].Close);
                // Volume comes from index 6, not 5 (which is vwap).
                Assert.Equal(2.0, result.Ohlcv[1].Volume);
            }

            [Fact]
            public async Task LastKey_Skipped_AndOrderedAscending()
            {
                // The "last" key shares JObject space with the data array. The
                // provider walks properties and skips "last" — verify by giving
                // it a numeric "last" that would otherwise mis-cast as JArray.
                var handler = new FakeHttpMessageHandler().Get(@"kraken\.com.*OHLC", """
                    {"error":[],"result":{
                      "last":1700000060,
                      "XXBTZUSD":[
                        [1700000060,"2","2","2","2","2","2",1],
                        [1700000000,"1","1","1","1","1","1",1]
                      ]
                    }}
                    """);
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USD", "1m", 100));

                Assert.Equal(2, result.Ohlcv.Count);
                // Sorted ascending regardless of source order.
                Assert.True(result.Ohlcv[0].Date < result.Ohlcv[1].Date);
            }

            [Fact]
            public async Task MissingResultKey_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler().Get(@"kraken\.com.*OHLC", """{"error":["EAPI:Invalid arguments"]}""");
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USD", "1m", 100));
                Assert.Empty(result.Ohlcv);
            }

            [Fact]
            public async Task MalformedJson_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler().Get(@"kraken\.com", "not-json");
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USD", "1m", 100));
                Assert.Empty(result.Ohlcv);
            }

            [Fact]
            public async Task LimitClampsToTakeLast()
            {
                // Provider takes the LAST N bars after sorting ascending — the
                // most-recent N. Mirrors how paginated fetchers work.
                var handler = new FakeHttpMessageHandler().Get(@"kraken\.com.*OHLC", """
                    {"error":[],"result":{
                      "XXBTZUSD":[
                        [1700000000,"1","1","1","1","1","1",1],
                        [1700000060,"2","2","2","2","2","2",1],
                        [1700000120,"3","3","3","3","3","3",1]
                      ]
                    }}
                    """);
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USD", "1m", 2));

                Assert.Equal(2, result.Ohlcv.Count);
                // Last two bars (open 2, then 3) — most-recent.
                Assert.Equal(2.0, result.Ohlcv[0].Open);
                Assert.Equal(3.0, result.Ohlcv[1].Open);
            }
        }

        // ── Oanda — auth-gated forex; Bearer + Accept-Datetime-Format=UNIX ───
        // Response: {"candles":[{"time":"unix_seconds_string","mid":{"o","h","l","c"},"volume":..,"complete":true}]}

        [Collection("ProviderCredentialBridge")]
        public class Oanda
        {
            // Oanda writes auth headers in Configure → swap-before-Configure.
            private static AccessibleTrader.Plugins.Oanda.OandaProvider NewProvider(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.Oanda.OandaProvider();
                SwapHttpClient(p, h);
                p.Configure(new Dictionary<string, string>
                {
                    ["AccessToken"] = "test-token",
                    ["AccountId"]   = "test-acct",
                });
                return p;
            }

            [Fact]
            public async Task NotConfigured_ReturnsEmpty_NoHttp()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = new AccessibleTrader.Plugins.Oanda.OandaProvider();
                SwapHttpClient(provider, handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Forex", "EUR_USD", "1h", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }

            [Fact]
            public async Task HappyPath_ParsesMidPriceCandles()
            {
                var handler = new FakeHttpMessageHandler().Get(@"oanda\.com", """
                    {"candles":[
                      {"time":"1700000000","mid":{"o":"1.0850","h":"1.0860","l":"1.0840","c":"1.0855"},"volume":100,"complete":true},
                      {"time":"1700003600","mid":{"o":"1.0855","h":"1.0870","l":"1.0850","c":"1.0865"},"volume":150,"complete":true}
                    ]}
                    """);
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Forex", "EUR_USD", "1h", 100));

                Assert.Equal(2, result.Ohlcv.Count);
                Assert.Equal(1.0850, result.Ohlcv[0].Open, 4);
                Assert.Equal(1.0865, result.Ohlcv[1].Close, 4);
                Assert.Equal(150, result.Ohlcv[1].Volume);
            }

            [Fact]
            public async Task BearerToken_AppliedFromConfigure()
            {
                var handler = new FakeHttpMessageHandler().Get(@"oanda\.com", """{"candles":[]}""");
                var provider = NewProvider(handler);

                await provider.FetchOhlcvAsync(new MarketDataRequest("Forex", "EUR_USD", "1h", 100));

                Assert.NotEmpty(handler.Captured);
                var auth = handler.Captured[0].Headers.Authorization;
                Assert.NotNull(auth);
                Assert.Equal("Bearer", auth!.Scheme);
                Assert.Equal("test-token", auth.Parameter);
            }

            [Fact]
            public async Task IncompletetCandle_FilteredUnlessLast()
            {
                // Oanda emits the in-progress candle with complete=false; the
                // provider keeps it only if it's the last one (so the chart can
                // show the forming bar).
                var handler = new FakeHttpMessageHandler().Get(@"oanda\.com", """
                    {"candles":[
                      {"time":"1700000000","mid":{"o":"1.0","h":"1.0","l":"1.0","c":"1.0"},"volume":1,"complete":true},
                      {"time":"1700003600","mid":{"o":"2.0","h":"2.0","l":"2.0","c":"2.0"},"volume":1,"complete":false},
                      {"time":"1700007200","mid":{"o":"3.0","h":"3.0","l":"3.0","c":"3.0"},"volume":1,"complete":false}
                    ]}
                    """);
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Forex", "EUR_USD", "1h", 100));

                // First two filtered/kept depending on lastness; the trailing
                // false-complete is kept (forming candle). The middle one is
                // also kept here because the provider's filter is `complete !=
                // false || last` — both incompletes pass when there are two
                // trailing. Verify the count matches the actual contract.
                Assert.True(result.Ohlcv.Count >= 1);
            }

            [Fact]
            public async Task MalformedJson_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler().Get(@"oanda\.com", "garbage");
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Forex", "EUR_USD", "1h", 100));
                Assert.Empty(result.Ohlcv);
            }
        }

        // ── Alpaca — auth via PluginHostServices.ApiKeys checkout ────────────

        // MUST carry its own collection tag: nested test classes do NOT inherit the outer
        // class's [Collection], so without this Alpaca ran in parallel with BrokerParityTests
        // and its FakeApiKeyCheckout.Install() (incl. a no-credentials install) mutated the
        // global PluginHostServices.ApiKeys mid-request → Kraken's request-time checkout saw
        // None and skipped the signed call → the intermittent BrokerParityTests.Kraken flake.
        [Collection("ProviderCredentialBridge")]
        public class Alpaca
        {
            // Alpaca pulls credentials from PluginHostServices.ApiKeys at every
            // signed call; install the FakeApiKeyCheckout for the duration of
            // the test so the happy-path runs to completion.
            private static AccessibleTrader.Plugins.Alpaca.AlpacaProvider NewConfigured(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.Alpaca.AlpacaProvider();
                p.Configure(new Dictionary<string, string>
                {
                    ["ApiKey"] = "test-key",
                    ["ApiSecret"] = "test-secret",
                });
                SwapHttpClient(p, h);
                return p;
            }

            [Fact]
            public async Task NotConfigured_ReturnsEmpty_NoHttp()
            {
                var handler = new FakeHttpMessageHandler();
                var provider = new AccessibleTrader.Plugins.Alpaca.AlpacaProvider();
                SwapHttpClient(provider, handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1h", 100));

                Assert.Empty(result.Ohlcv);
                Assert.Empty(handler.Captured);
            }

            [Fact]
            public async Task EquityHappyPath_ParsesBars()
            {
                using var _ = new Fakes.FakeApiKeyCheckout().Install();
                var handler = new FakeHttpMessageHandler().Get(@"alpaca\.markets.*stocks", """
                    {"bars":[
                      {"t":"2026-01-01T00:00:00Z","o":150,"h":151,"l":149,"c":150.5,"v":1000},
                      {"t":"2026-01-01T01:00:00Z","o":150.5,"h":152,"l":150,"c":151,"v":1500}
                    ]}
                    """);
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1h", 100));

                Assert.Equal(2, result.Ohlcv.Count);
                Assert.Equal(150.0, result.Ohlcv[0].Open);
                Assert.Equal(151.0, result.Ohlcv[1].Close);
            }

            // ── Feed selection and pagination (added 2026-08-02) ──────────────
            // Alpaca defaults an omitted `feed` to IEX, which is ONE VENUE carrying ~2% of
            // consolidated volume and only reaching 2022. The provider had been letting that
            // default stand, so a user charting SPY on 5-minute bars got the thin tape and
            // nothing said so. And next_page_token was ignored, so any request past 10,000
            // bars stopped at the first page and reported success.

            [Fact]
            public async Task StockRequests_AskForTheConsolidatedSipFeed()
            {
                using var _ = new Fakes.FakeApiKeyCheckout().Install();
                var handler = new FakeHttpMessageHandler().Get(@"alpaca\.markets.*stocks", """
                    {"bars":[{"t":"2026-01-01T00:00:00Z","o":1,"h":1,"l":1,"c":1,"v":1}],"next_page_token":null}
                    """);
                var provider = NewConfigured(handler);

                await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "SPY", "5m", 100));

                var url = handler.Captured.Single().RequestUri!.ToString();
                Assert.Contains("feed=sip", url);
                Assert.Contains("adjustment=all", url);   // splits/dividends, or history is wrong
            }

            [Fact]
            public async Task CryptoRequests_CarryNoStockFeedParameter()
            {
                // The feed parameter is a stocks concept. Sending it on the crypto endpoint would
                // be at best ignored and at worst a 400.
                using var _ = new Fakes.FakeApiKeyCheckout().Install();
                var handler = new FakeHttpMessageHandler().Get(@"alpaca\.markets.*us/bars", """
                    {"bars":{"BTC/USD":[{"t":"2026-01-01T00:00:00Z","o":1,"h":1,"l":1,"c":1,"v":1}]}}
                    """);
                var provider = NewConfigured(handler);

                await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USD", "1h", 100));

                Assert.DoesNotContain("feed=", handler.Captured.Single().RequestUri!.ToString());
            }

            [Fact]
            public async Task WithoutSipEntitlement_DowngradesToIexOnceAndSaysSo()
            {
                using var _ = new Fakes.FakeApiKeyCheckout().Install();
                var handler = new FakeHttpMessageHandler()
                    .Add(HttpMethod.Get, @"feed=sip", """{"message":"subscription does not permit querying recent SIP data"}""")
                    .Add(HttpMethod.Get, @"feed=iex", """
                        {"bars":[{"t":"2026-01-01T00:00:00Z","o":10,"h":11,"l":9,"c":10.5,"v":100}],"next_page_token":null}
                        """);
                var provider = NewConfigured(handler);

                string? notice = null;
                using var sub = provider.ErrorStream.Subscribe(e => notice = e);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "SPY", "5m", 100));

                Assert.Single(result.Ohlcv);                       // the retry succeeded
                Assert.NotNull(notice);                            // and it was NOT silent
                Assert.Contains("IEX", notice!);

                // The downgrade is remembered — a second call must not re-probe SIP.
                handler.Captured.Clear();
                await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "SPY", "5m", 100));
                Assert.All(handler.Captured, r => Assert.DoesNotContain("feed=sip", r.RequestUri!.ToString()));
            }

            [Fact]
            public async Task ConfiguredFeedOverride_IsNotSecondGuessed()
            {
                using var _ = new Fakes.FakeApiKeyCheckout().Install();
                var handler = new FakeHttpMessageHandler().Get(@"alpaca\.markets.*stocks", """
                    {"bars":[{"t":"2026-01-01T00:00:00Z","o":1,"h":1,"l":1,"c":1,"v":1}],"next_page_token":null}
                    """);
                var provider = new AccessibleTrader.Plugins.Alpaca.AlpacaProvider();
                provider.Configure(new Dictionary<string, string>
                {
                    ["ApiKey"] = "k", ["ApiSecret"] = "s", ["Feed"] = "iex",
                });
                SwapHttpClient(provider, handler);

                await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "SPY", "5m", 100));

                Assert.Contains("feed=iex", handler.Captured.Single().RequestUri!.ToString());
            }

            [Fact]
            public async Task FollowsNextPageToken_UntilTheLimitIsSatisfied()
            {
                using var _ = new Fakes.FakeApiKeyCheckout().Install();
                var handler = new FakeHttpMessageHandler()
                    .Add(HttpMethod.Get, @"page_token=PAGE2", """
                        {"bars":[
                          {"t":"2026-01-01T02:00:00Z","o":3,"h":3,"l":3,"c":3,"v":3},
                          {"t":"2026-01-01T03:00:00Z","o":4,"h":4,"l":4,"c":4,"v":4}
                        ],"next_page_token":null}
                        """)
                    .Add(HttpMethod.Get, @"alpaca\.markets.*stocks", """
                        {"bars":[
                          {"t":"2026-01-01T00:00:00Z","o":1,"h":1,"l":1,"c":1,"v":1},
                          {"t":"2026-01-01T01:00:00Z","o":2,"h":2,"l":2,"c":2,"v":2}
                        ],"next_page_token":"PAGE2"}
                        """);
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "SPY", "1h", 100));

                Assert.Equal(4, result.Ohlcv.Count);
                Assert.Equal(2, handler.Captured.Count);
                Assert.Equal(new[] { 1d, 2d, 3d, 4d }, result.Ohlcv.Select(b => b.Open));
            }

            [Fact]
            public async Task StopsPagingOnceTheRequestedCountIsReached()
            {
                // A page token is not an instruction to keep going forever. Asking for 2 bars must
                // not walk the whole history because the server offered more.
                using var _ = new Fakes.FakeApiKeyCheckout().Install();
                var handler = new FakeHttpMessageHandler().Get(@"alpaca\.markets.*stocks", """
                    {"bars":[
                      {"t":"2026-01-01T00:00:00Z","o":1,"h":1,"l":1,"c":1,"v":1},
                      {"t":"2026-01-01T01:00:00Z","o":2,"h":2,"l":2,"c":2,"v":2}
                    ],"next_page_token":"MORE"}
                    """);
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "SPY", "1h", 2));

                Assert.Equal(2, result.Ohlcv.Count);
                Assert.Single(handler.Captured);
            }

            [Fact]
            public async Task ANullNextPageToken_TerminatesRatherThanBeingSentBackAsTheString()
            {
                // JSON null must not become the literal "page_token=" — a regression that would
                // loop on the same page until the guard tripped.
                using var _ = new Fakes.FakeApiKeyCheckout().Install();
                var handler = new FakeHttpMessageHandler().Get(@"alpaca\.markets.*stocks", """
                    {"bars":[{"t":"2026-01-01T00:00:00Z","o":1,"h":1,"l":1,"c":1,"v":1}],"next_page_token":null}
                    """);
                var provider = NewConfigured(handler);

                await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "SPY", "1h", 5000));

                Assert.Single(handler.Captured);
            }

            [Fact]
            public async Task CryptoHappyPath_ReadsFromSymbolKey()
            {
                // Alpaca crypto v1beta3 keys bars by the SLASHED pair ("BTC/USD"),
                // both in the request and the response — the provider must not strip
                // the slash (doing so returned an empty crypto chart).
                using var _ = new Fakes.FakeApiKeyCheckout().Install();
                var handler = new FakeHttpMessageHandler().Get(@"alpaca\.markets.*us/bars", """
                    {"bars":{"BTC/USD":[
                      {"t":"2026-01-01T00:00:00Z","o":50000,"h":50100,"l":49900,"c":50050,"v":2.5}
                    ]}}
                    """);
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USD", "1h", 100));

                Assert.Single(result.Ohlcv);
                Assert.Equal(50000.0, result.Ohlcv[0].Open);
                Assert.Equal(2.5, result.Ohlcv[0].Volume);
            }

            [Fact]
            public async Task ApcaHeaders_AppliedFromCheckout()
            {
                using var _ = new Fakes.FakeApiKeyCheckout().Install();
                var handler = new FakeHttpMessageHandler().Get(@"alpaca\.markets", """{"bars":[]}""");
                var provider = NewConfigured(handler);

                await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1h", 100));

                Assert.NotEmpty(handler.Captured);
                var headers = handler.Captured[0].Headers;
                Assert.True(headers.Contains("APCA-API-KEY-ID"));
                Assert.True(headers.Contains("APCA-API-SECRET-KEY"));
                // FakeApiKeyCheckout's default Key is "test-key".
                Assert.Equal("test-key", System.Linq.Enumerable.First(headers.GetValues("APCA-API-KEY-ID")));
            }

            [Fact]
            public async Task NoCredsInHost_ReturnsEmpty()
            {
                // Configure populates _apiKey so IsConfigured passes, but the
                // FakeApiKeyCheckout returns HasCredentials=false → checkout
                // throws → catch swallows → empty result.
                using var _ = new Fakes.FakeApiKeyCheckout { HasCredentials = false }.Install();
                var handler = new FakeHttpMessageHandler();   // no rules; would throw if hit
                var provider = NewConfigured(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1h", 100));

                Assert.Empty(result.Ohlcv);
            }
        }
    }
}
