using System.Reflection;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.ScriptSandbox;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Regression tests for the 2026-04-23 post-audit fixes. Each fact below
    /// asserts behaviour that the Week 1-3 changes deliberately introduced —
    /// a future refactor that re-opens one of these holes will fail fast here
    /// instead of only surfacing in production.
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class PostAuditRegressionTests
    {
        // ── Week 1: IPC decoder bounds (AccessibleTrader.ScriptSandbox/Messages.cs) ──

        [Fact]
        public void DecodeMetadata_ArrayCountExceedingCap_Throws()
        {
            // 10 M components is well over the 1 M cap. Build a payload with a
            // valid Id + DisplayName and a u32 count > cap; decoder must throw
            // before attempting a huge allocation.
            using var ms = new MemoryStream();
            WriteString(ms, "id");
            WriteString(ms, "display");
            WriteU32(ms, 10_000_000u);
            var payload = ms.ToArray();

            var ex = Assert.Throws<InvalidDataException>(
                () => MessageCodec.DecodeMetadata(payload));
            Assert.Contains("ComponentNames", ex.Message);
        }

        [Fact]
        public void DecodeMetadata_StringLengthExceedingCap_Throws()
        {
            // Claim a 128 KB string header (cap is 64 KB) inside a short buffer —
            // ByteReader must refuse before the UTF8 decode.
            using var ms = new MemoryStream();
            WriteU32(ms, 128u * 1024u);     // string length header
            // Deliberately leave the buffer short.
            var payload = ms.ToArray();

            Assert.Throws<InvalidDataException>(
                () => MessageCodec.DecodeMetadata(payload));
        }

        [Fact]
        public void DecodeCalculateRequest_TruncatedAfterCount_Throws()
        {
            // 5 bars claimed but the payload has zero bytes of bar data.
            using var ms = new MemoryStream();
            WriteU32(ms, 5u);
            var payload = ms.ToArray();

            Assert.Throws<InvalidDataException>(
                () => MessageCodec.DecodeCalculateRequest(payload));
        }

        [Fact]
        public void RoundtripMetadata_SmallPayload_Succeeds()
        {
            // Sanity: the caps don't reject legitimate-size payloads.
            var meta = new IndicatorMetadataMessage(
                Id: "RSI",
                DisplayName: "Relative Strength Index",
                ComponentNames: new[] { "rsi", "rsi_ma" },
                DisplayTypeValues: new[] { 0, 1 },
                DefaultParameters: new System.Collections.Generic.Dictionary<string, double>
                {
                    ["Period"] = 14,
                    ["Overbought"] = 70
                },
                CausalityValues: new[] { (int)ComponentCausality.Causal, (int)ComponentCausality.Lookahead });
            var bytes = MessageCodec.EncodeMetadata(meta);
            var decoded = MessageCodec.DecodeMetadata(bytes);

            Assert.Equal(meta.Id, decoded.Id);
            Assert.Equal(meta.DisplayName, decoded.DisplayName);
            Assert.Equal(meta.ComponentNames, decoded.ComponentNames);
            Assert.Equal(meta.DisplayTypeValues, decoded.DisplayTypeValues);
            Assert.Equal(meta.DefaultParameters, decoded.DefaultParameters);
            // Appended to the frame after DefaultParameters — a decoder that stopped short would
            // still pass every assertion above it.
            Assert.Equal(meta.CausalityValues, decoded.CausalityValues);
        }

        private static void WriteU32(MemoryStream ms, uint v)
        {
            Span<byte> b = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(b, v);
            ms.Write(b);
        }

        private static void WriteString(MemoryStream ms, string s)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            WriteU32(ms, (uint)bytes.Length);
            ms.Write(bytes, 0, bytes.Length);
        }

        // ── Week 1: LiveStreamManager zero-value filter, driven not mirrored ──
        //
        // This used to define the predicate INSIDE the test body and assert the copy;
        // LiveStreamManager was never constructed, so if the real filter had started
        // admitting zero-close bars — corrupt ticks poisoning the chart and every
        // indicator downstream — all seven cases stayed green. The rule now lives in
        // BarBucketConsolidator.Apply, and the assertions below push a tick through a
        // REAL LiveStreamManager and look at what comes out of its channel. That covers
        // both halves: that the rule is right, and that the rule is still wired in.

        /// <summary>A provider whose live ticks the test can push by hand.</summary>
        private sealed class TickProvider : BaseMarketDataProvider
        {
            public override string Name => "TickProv";
            public override string Description => "test";
            public override List<MarketType> SupportedMarkets => new() { MarketType.Crypto };
            public override bool SupportsSymbolSearch => false;
            public override bool RequiresApiKey => false;
            public override bool IsConfigured => true;
            public override bool SupportsLiveUpdates => true;
            public override ProviderEnvironment Environment => ProviderEnvironment.Live;
            public override int MaxBarsPerRequest => 100;
            public override List<string> NativelySupportedTimeframes => new() { "1h" };
            public override void Configure(Dictionary<string, string> config) { }
            public override Task EnsureConnectedAsync() => Task.CompletedTask;
            public override Task SetSubscriptionAsync(string market, string symbol, string timeframe) => Task.CompletedTask;
            public override Task DisconnectAsync() => Task.CompletedTask;
            public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot") => Task.FromResult(new List<string>());
            public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) => Task.FromResult(new List<string>());
            public override Task<List<string>> GetSupportedTimeframesAsync() => Task.FromResult(new List<string>());
            public override Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
                => Task.FromResult((new List<Ohlcv>(), new List<(long, double)>()));
            public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
                => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

            public void PushTick(Ohlcv tick) => _liveStream.OnNext(tick);
        }

        [Theory]
        [InlineData(100.0, 100.0, 100.0, 100.0, 0.0,  true)]   // first tick, zero volume ok
        [InlineData(100.0, 100.0, 100.0, 100.0, 1.5,  true)]
        [InlineData(0.0,   100.0, 100.0, 100.0, 1.0,  false)]  // zero open
        [InlineData(100.0, 0.0,   100.0, 100.0, 1.0,  false)]  // zero high
        [InlineData(100.0, 100.0, 0.0,   100.0, 1.0,  false)]  // zero low
        [InlineData(100.0, 100.0, 100.0, 0.0,   1.0,  false)]  // zero close
        [InlineData(100.0, 100.0, 100.0, 100.0, -0.1, false)]  // negative volume
        public async Task ZeroValueFilter_IsEnforcedByTheRealLiveStreamPath(
            double o, double h, double l, double c, double v, bool expectPublished)
        {
            var provider = new TickProvider();
            var data = Substitute.For<IDataService>();
            data.GetProviderAsync("TickProv").Returns(Task.FromResult<IMarketDataProvider?>(provider));

            using var manager = new LiveStreamManager(
                data,
                Substitute.For<IGlobalErrorCoordinator>(),
                NullLogger<LiveStreamManager>.Instance);

            await manager.StartLiveStreamAsync("Crypto", "TickProv", "BTC/USD", "1h");

            provider.PushTick(new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), o, h, l, c, v));

            bool published = manager.LiveStream.TryRead(out var emitted);
            Assert.Equal(expectPublished, published);
            if (expectPublished)
            {
                Assert.Equal(c, emitted.Bar.Close);
                Assert.Equal("TickProv", emitted.Identity.Provider);
            }
        }

        /// <summary>
        /// Anti-vacuity for the theory above: if <c>StartLiveStreamAsync</c> silently failed to
        /// subscribe — a null provider, a changed name — every "must be dropped" row would pass
        /// for the wrong reason. A known-good tick must make it all the way through.
        /// </summary>
        [Fact]
        public async Task TheLiveStreamPath_UnderTest_ActuallyDeliversAGoodTick()
        {
            var provider = new TickProvider();
            var data = Substitute.For<IDataService>();
            data.GetProviderAsync("TickProv").Returns(Task.FromResult<IMarketDataProvider?>(provider));

            using var manager = new LiveStreamManager(
                data, Substitute.For<IGlobalErrorCoordinator>(),
                NullLogger<LiveStreamManager>.Instance);

            await manager.StartLiveStreamAsync("Crypto", "TickProv", "BTC/USD", "1h");
            provider.PushTick(new Ohlcv(new DateTime(2026, 1, 1, 0, 30, 0, DateTimeKind.Utc), 10, 12, 9, 11, 3));

            Assert.True(manager.LiveStream.TryRead(out var bar));
            Assert.Equal(11, bar.Bar.Close);
        }

        // ── Week 1: ChartSeries.Clone isolates Levels / ZoneBands ──
        //
        // The SeriesReducer rewrite in Week 1 relies on ChartSeries.Clone()
        // producing a collection instance distinct from the source. Assert
        // that directly so a future SeriesConfig.Clone refactor that drops
        // the foreach-copy of Levels breaks this test first, not the reducer.

        [Fact]
        public void ChartSeries_Clone_ProducesDistinctLevelsCollection()
        {
            var config = new SeriesConfig { Id = "rsi", Name = "RSI" };
            config.Levels.Add(new LevelConfig { Value = 30, Name = "Oversold" });

            var original = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "rsi" });
            var cloned = original.Clone();

            // Distinct collection references -> reducer mutations of cloned.Levels
            // can't leak into original.Levels, which is what the reducer fix relies on.
            Assert.NotSame(original.Levels, cloned.Levels);

            cloned.Levels.Add(new LevelConfig { Value = 70, Name = "Overbought" });
            Assert.Single(original.Levels);
            Assert.Equal(2, cloned.Levels.Count);
        }
    }

    /// <summary>
    /// Week 1: Kraken nonce CAS idempotence — against the real provider.
    ///
    /// <para>
    /// The old version of this reimplemented Kraken's CAS loop inside the test and asserted the
    /// reimplementation, on the stated grounds that "the real provider lives in a plugin DLL and
    /// depends on HttpClient and credentials". <see cref="BrokerParityTests"/> already
    /// contradicts that: it constructs a real <c>KrakenProvider</c> and swaps its
    /// <c>HttpClient</c> by reflection. The machinery was in the suite the whole time.
    /// </para>
    /// <para>
    /// What is at stake: a duplicate nonce means Kraken rejects the authenticated request, so
    /// order placement, cancellation and balance reads all fail under concurrency. The signer is
    /// private, so it is invoked by reflection — the alternative is a public entry point behind
    /// a rate limiter that would serialize the callers and make the contention this test exists
    /// to create impossible.
    /// </para>
    /// </summary>
    // Constructs a real KrakenProvider, which reads the global PluginHostServices.ApiKeys
    // bridge at sign time — see BrokerParityTests for why that must stay serialized.
    [Collection("ProviderCredentialBridge")]
    public class KrakenNonceRegressionTests
    {
        /// <summary>Records every nonce that reached the wire. Thread-safe on purpose: the whole
        /// point is to have many signers in flight at once.</summary>
        private sealed class NonceRecordingHandler : HttpMessageHandler
        {
            public readonly System.Collections.Concurrent.ConcurrentBag<long> Nonces = new();

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string body = request.Content == null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken);

                var match = System.Text.RegularExpressions.Regex.Match(body, @"nonce=(\d+)");
                if (match.Success)
                    Nonces.Add(long.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));

                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"error\":[],\"result\":{}}",
                        System.Text.Encoding.UTF8, "application/json"),
                };
            }
        }

        [Fact]
        public async Task Kraken_concurrent_signers_never_reuse_a_nonce()
        {
            var provider = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
            provider.Configure(new Dictionary<string, string>
            {
                ["ApiKey"] = "k",
                ["ApiSecret"] = Convert.ToBase64String(new byte[32]),
            });

            var handler = new NonceRecordingHandler();
            HttpClientSwap.ReplaceAll(provider, handler);

            var sign = provider.GetType().GetMethod("PostPrivateAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(sign);

            const int threads = 16;
            const int perThread = 100;

            var tasks = new Task[threads];
            for (int t = 0; t < threads; t++)
            {
                tasks[t] = Task.Run(async () =>
                {
                    for (int i = 0; i < perThread; i++)
                        await (Task<string>)sign!.Invoke(provider,
                            new object[] { "/0/private/Balance", new Dictionary<string, string>() })!;
                });
            }
            await Task.WhenAll(tasks);

            var nonces = handler.Nonces.ToList();
            Assert.Equal(threads * perThread, nonces.Count);
            Assert.Equal(nonces.Count, nonces.Distinct().Count());
        }
    }
}
