using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fakes;
using Xunit;

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
    public class ProviderFetchOhlcvTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static void SwapHttpClient(object provider, FakeHttpMessageHandler handler)
        {
            // Different providers name the field differently (_httpClient,
            // _http, _client). Find any HttpClient-typed field so the helper
            // works across the plugin set without per-provider knowledge.
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

        private static void SwapPrivateField(object obj, string fieldName, object value)
        {
            var field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                throw new InvalidOperationException($"{obj.GetType().Name} has no private field {fieldName}.");
            field.SetValue(obj, value);
        }

        // ── Bitstamp ──────────────────────────────────────────────────────────
        // Endpoint: GET /api/v2/ohlc/{pair}/?step=&limit=  ⇒ {"data":{"ohlc":[{...}]}}
        // Fields: timestamp / open / high / low / close / volume — all stringified.
        // Filters bars where any OHLC leg ≤ 0; sorts by Date ascending.

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
            public async Task NonSuccessStatus_ReturnsEmpty()
            {
                var handler = new FakeHttpMessageHandler().Get(@"/ohlc/", "{}", HttpStatusCode.InternalServerError);
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "BTC/USD", "1m", 100));

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

        public class Coinbase
        {
            private static AccessibleTrader.Plugins.Coinbase.CoinbaseProvider NewProvider(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.Coinbase.CoinbaseProvider();
                p.Configure(new Dictionary<string, string>
                {
                    ["ApiKey"] = "test",
                    ["ApiSecret"] = "test-secret",
                });
                SwapHttpClient(p, h);
                return p;
            }

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
                      {"value":"75","timestamp":"1700000060"},
                      {"value":"50","timestamp":"1700000000"}
                    ]}
                    """);
                var provider = NewProvider(handler);

                var result = await provider.FetchOhlcvAsync(new MarketDataRequest("Index", "FNG", "1d", 100));

                Assert.Equal(2, result.Ohlcv.Count);
                // Reversed: oldest (1700000000) first, newest (1700000060) last.
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

        public class Fred
        {
            private static AccessibleTrader.Plugins.Fred.FredProvider NewConfigured(FakeHttpMessageHandler h)
            {
                var p = new AccessibleTrader.Plugins.Fred.FredProvider();
                p.Configure(new Dictionary<string, string> { ["ApiKey"] = "test" });
                SwapHttpClient(p, h);
                return p;
            }

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
    }
}
