using System.Text.Json;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Phase B of the keyed-feeds refactor: the multi-live SDK capability
    /// (SupportsMultipleLiveSubscriptions + SubscribeLiveAsync), style-aware bar
    /// consolidation — including the cumulative-kline volume-inflation fix — the
    /// hub's independent per-feed subscriptions, and Binance's implementation.
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class KeyedFeedsPhaseBTests
    {
        private static ChartIdentity Id(string provider = "MultiProv", string symbol = "BTC/USD", string tf = "1h") =>
            new("Spot", provider, symbol, tf);

        // ── BarBucketConsolidator ────────────────────────────────────────────

        private static Ohlcv Tick(DateTime date, double o, double h, double l, double c, double v) =>
            new(date, o, h, l, c, v);

        private static readonly DateTime H10 = new(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void TradeDeltas_accumulates_volume_and_expands_range()
        {
            var c = new BarBucketConsolidator("1h", LiveTickStyle.TradeDeltas);

            c.Apply(Tick(H10.AddMinutes(1), 100, 101, 99, 100, 5));
            var bar = c.Apply(Tick(H10.AddMinutes(2), 100, 103, 98, 102, 3))!.Value;

            Assert.Equal(H10, bar.Date);      // bucketed to the period start
            Assert.Equal(103, bar.High);
            Assert.Equal(98, bar.Low);
            Assert.Equal(102, bar.Close);
            Assert.Equal(8, bar.Volume);      // trade volumes ADD
        }

        [Fact]
        public void CumulativeBars_do_not_double_count_the_running_total()
        {
            // The Binance kline case: same source bar re-sent every ~1s with
            // volume-so-far. The old UpdateWith accumulation produced 10+25+40=75;
            // the truth is 40.
            var c = new BarBucketConsolidator("1h", LiveTickStyle.CumulativeBars);

            c.Apply(Tick(H10, 100, 101, 99, 100, 10));
            c.Apply(Tick(H10, 100, 102, 99, 101, 25));
            var bar = c.Apply(Tick(H10, 100, 102, 98, 102, 40))!.Value;

            Assert.Equal(40, bar.Volume);
            Assert.Equal(102, bar.High);
            Assert.Equal(98, bar.Low);
            Assert.Equal(102, bar.Close);
        }

        [Fact]
        public void CumulativeBars_across_source_bars_in_a_coarser_bucket_sum_once_each()
        {
            // 2h chart fed by 1h klines: each kline's final volume enters once,
            // its intra-bar updates contribute only growth. 30 (kline 1) + 20
            // (kline 2 so far) = 50.
            var c = new BarBucketConsolidator("2h", LiveTickStyle.CumulativeBars);

            c.Apply(Tick(H10, 100, 101, 99, 100, 10));               // kline 10:00 progressing
            c.Apply(Tick(H10, 100, 102, 99, 101, 30));               // kline 10:00 final total
            var bar = c.Apply(Tick(H10.AddHours(1), 101, 104, 100, 103, 20))!.Value; // kline 11:00

            Assert.Equal(H10, bar.Date);       // both fold into the 10:00 2h bucket
            Assert.Equal(50, bar.Volume);
            Assert.Equal(104, bar.High);
            Assert.Equal(103, bar.Close);
        }

        [Fact]
        public void New_period_starts_a_fresh_bucket()
        {
            var c = new BarBucketConsolidator("1h", LiveTickStyle.TradeDeltas);

            c.Apply(Tick(H10.AddMinutes(5), 100, 101, 99, 100, 5));
            var bar = c.Apply(Tick(H10.AddHours(1).AddMinutes(1), 105, 106, 104, 105, 2))!.Value;

            Assert.Equal(H10.AddHours(1), bar.Date);
            Assert.Equal(2, bar.Volume);       // previous bucket's volume gone
            Assert.Equal(106, bar.High);
        }

        [Fact]
        public void Malformed_bars_are_dropped()
        {
            var c = new BarBucketConsolidator("1h", LiveTickStyle.TradeDeltas);
            Assert.Null(c.Apply(Tick(H10, 100, 101, 0, 100, 5))); // zero Low leg
        }

        // ── SDK capability defaults (via the BaseMarketDataProvider anchors) ─

        private class MinimalProvider : BaseMarketDataProvider
        {
            public override string Name => "Minimal";
            public override string Description => "";
            public override List<MarketType> SupportedMarkets => new() { MarketType.Crypto };
            public override bool SupportsSymbolSearch => false;
            public override bool RequiresApiKey => false;
            public override bool IsConfigured => true;
            public override bool SupportsLiveUpdates => false;
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
        }

        [Fact]
        public async Task Providers_default_to_single_subscription_trade_deltas()
        {
            IMarketDataProvider p = new MinimalProvider();

            Assert.False(p.SupportsMultipleLiveSubscriptions);
            Assert.Equal(LiveTickStyle.TradeDeltas, p.LiveTickStyle);
            await Assert.ThrowsAsync<NotSupportedException>(
                () => p.SubscribeLiveAsync("Spot", "BTC/USD", "1h", _ => { }));
        }

        // ── Hub: independent per-feed subscriptions ──────────────────────────

        private sealed class MultiSubProvider : MinimalProvider
        {
            public readonly List<(string Symbol, string Timeframe)> Subscriptions = new();
            public readonly List<(string Symbol, string Timeframe)> Disposals = new();
            public Action<Ohlcv>? LastOnBar;

            public override bool SupportsMultipleLiveSubscriptions => true;
            public override Task<IAsyncDisposable> SubscribeLiveAsync(string market, string symbol, string timeframe, Action<Ohlcv> onBar)
            {
                Subscriptions.Add((symbol, timeframe));
                LastOnBar = onBar;
                return Task.FromResult<IAsyncDisposable>(new Handle(this, symbol, timeframe));
            }

            private sealed class Handle : IAsyncDisposable
            {
                private readonly MultiSubProvider _p; private readonly string _s, _t;
                public Handle(MultiSubProvider p, string s, string t) { _p = p; _s = s; _t = t; }
                public ValueTask DisposeAsync() { _p.Disposals.Add((_s, _t)); return ValueTask.CompletedTask; }
            }
        }

        private static (MarketFeedHub Hub, MultiSubProvider Provider) HubWithMultiSub(DemoPolicy? demo = null)
        {
            var provider = new MultiSubProvider();
            var dataService = Substitute.For<IDataService>();
            dataService.GetProviderAsync("MultiProv").Returns(Task.FromResult<IMarketDataProvider?>(provider));
            var hub = new MarketFeedHub(Substitute.For<IDataOrchestrator>(), dataService,
                demo ?? new DemoPolicy(false), NullLoggerFactory.Instance);
            return (hub, provider);
        }

        [Fact]
        public async Task Hub_starts_an_independent_subscription_and_ticks_flow_into_the_feed()
        {
            var (hub, provider) = HubWithMultiSub();
            using (hub)
            {
                Assert.Equal(FeedLiveStart.Started, await hub.TryStartFeedLiveAsync(Id()));
                Assert.True(hub.IsFeedLive(Id()));
                Assert.Equal(("BTC/USD", "1h"), provider.Subscriptions.Single());

                provider.LastOnBar!(new Ohlcv(H10, 100, 101, 99, 100, 5));
                var feed = hub.TryGetFeed(Id())!;
                Assert.Equal(1, feed.Bars.Count);
                Assert.Equal(100, feed.Bars[0].Close);

                // Idempotent: second start is a no-op, not a second socket.
                Assert.Equal(FeedLiveStart.AlreadyLive, await hub.TryStartFeedLiveAsync(Id()));
                Assert.Single(provider.Subscriptions);
            }
        }

        [Fact]
        public async Task Hub_stop_disposes_the_provider_handle()
        {
            var (hub, provider) = HubWithMultiSub();
            using (hub)
            {
                await hub.TryStartFeedLiveAsync(Id());
                await hub.StopFeedLiveAsync(Id());

                Assert.False(hub.IsFeedLive(Id()));
                Assert.Equal(("BTC/USD", "1h"), provider.Disposals.Single());
            }
        }

        [Fact]
        public async Task Hub_refuses_single_subscription_providers()
        {
            var dataService = Substitute.For<IDataService>();
            dataService.GetProviderAsync("SingleProv")
                .Returns(Task.FromResult<IMarketDataProvider?>(new MinimalProvider()));
            using var hub = new MarketFeedHub(Substitute.For<IDataOrchestrator>(), dataService,
                new DemoPolicy(false), NullLoggerFactory.Instance);

            Assert.Equal(FeedLiveStart.NotSupported, await hub.TryStartFeedLiveAsync(Id(provider: "SingleProv")));
            Assert.False(hub.IsFeedLive(Id(provider: "SingleProv")));
        }

        [Fact]
        public async Task Hub_honors_the_demo_live_stream_policy()
        {
            var dataService = Substitute.For<IDataService>();
            using var hub = new MarketFeedHub(Substitute.For<IDataOrchestrator>(), dataService,
                new DemoPolicy(true), NullLoggerFactory.Instance); // demo: only Bitstamp streams

            Assert.Equal(FeedLiveStart.PolicyDenied, await hub.TryStartFeedLiveAsync(Id(provider: "MultiProv")));
            await dataService.DidNotReceive().GetProviderAsync(Arg.Any<string>());
        }

        [Fact]
        public async Task Hub_dispose_tears_down_active_subscriptions()
        {
            var (hub, provider) = HubWithMultiSub();
            await hub.TryStartFeedLiveAsync(Id());

            hub.Dispose();

            Assert.Single(provider.Disposals);
        }

        // ── Binance implementation ───────────────────────────────────────────

        [Fact]
        public void Binance_declares_cumulative_kline_ticks_and_multi_sub()
        {
            IMarketDataProvider p = new AccessibleTrader.Plugins.Binance.BinanceProvider();
            Assert.True(p.SupportsMultipleLiveSubscriptions);
            Assert.Equal(LiveTickStyle.CumulativeBars, p.LiveTickStyle);
        }

        [Fact]
        public void Enrolled_providers_declare_multi_live_capability()
        {
            // The 2026-07-22 enrollment pass: each gets an independent
            // per-subscription stream (own socket, or own SDK subscription).
            Assert.True(((IMarketDataProvider)new AccessibleTrader.Plugins.Bitstamp.BitstampProvider()).SupportsMultipleLiveSubscriptions);
            Assert.True(((IMarketDataProvider)new AccessibleTrader.Plugins.Kraken.KrakenProvider()).SupportsMultipleLiveSubscriptions);
            Assert.True(((IMarketDataProvider)new AccessibleTrader.Plugins.Mexc.MexcProvider()).SupportsMultipleLiveSubscriptions);
            // IB stays single-subscription: its smd stream is price-only quote
            // ticks (see the classification comment in the provider).
            Assert.False(((IMarketDataProvider)new AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider()).SupportsMultipleLiveSubscriptions);
        }

        [Fact]
        public void Bitstamp_trade_parse_reads_price_amount_timestamp()
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(
                "{\"event\":\"trade\",\"channel\":\"live_trades_btcusd\"," +
                "\"data\":{\"price\":95123.5,\"amount\":0.25,\"timestamp\":\"1753100000\"}}");
            Assert.True(AccessibleTrader.Plugins.Bitstamp.BitstampProvider.TryParseTrade(json, out var bar));
            Assert.Equal(95123.5, bar.Close);
            Assert.Equal(0.25, bar.Volume);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1753100000).UtcDateTime, bar.Date);

            var zero = Newtonsoft.Json.Linq.JObject.Parse(
                "{\"event\":\"trade\",\"data\":{\"price\":0,\"amount\":1,\"timestamp\":\"1753100000\"}}");
            Assert.False(AccessibleTrader.Plugins.Bitstamp.BitstampProvider.TryParseTrade(zero, out _));
        }

        [Fact]
        public void Kraken_ohlc_parse_reads_v2_candles()
        {
            var item = Newtonsoft.Json.Linq.JObject.Parse(
                "{\"open\":95000.0,\"high\":95100.0,\"low\":94900.0,\"close\":95050.0,\"volume\":12.5," +
                "\"timestamp\":\"2026-07-22T10:00:00.000000Z\"}");
            Assert.True(AccessibleTrader.Plugins.Kraken.KrakenProvider.TryParseOhlcItem(item, out var bar));
            Assert.Equal(95050.0, bar.Close);
            Assert.Equal(12.5, bar.Volume);
            Assert.Equal(new DateTime(2026, 7, 22, 10, 0, 0, DateTimeKind.Utc), bar.Date);

            var empty = Newtonsoft.Json.Linq.JObject.Parse(
                "{\"open\":0,\"high\":0,\"low\":0,\"close\":0,\"volume\":0}");
            Assert.False(AccessibleTrader.Plugins.Kraken.KrakenProvider.TryParseOhlcItem(empty, out _));
        }

        [Fact]
        public void Kline_style_providers_declare_cumulative_ticks()
        {
            // The 2026-07-22 fleet audit: these providers' live streams carry the
            // current period's TOTAL volume, so accumulating them double-counted.
            // Trade-tick providers (Bitstamp, Coinbase, Finnhub, Oanda, TwelveData,
            // Tradier) correctly keep the TradeDeltas default.
            Assert.Equal(LiveTickStyle.CumulativeBars,
                ((IMarketDataProvider)new AccessibleTrader.Plugins.Mexc.MexcProvider()).LiveTickStyle);
            Assert.Equal(LiveTickStyle.CumulativeBars,
                ((IMarketDataProvider)new AccessibleTrader.Plugins.Kraken.KrakenProvider()).LiveTickStyle);
            Assert.Equal(LiveTickStyle.CumulativeBars,
                ((IMarketDataProvider)new AccessibleTrader.Plugins.Alpaca.AlpacaProvider()).LiveTickStyle);
            Assert.Equal(LiveTickStyle.CumulativeBars,
                ((IMarketDataProvider)new AccessibleTrader.Plugins.Polygon.PolygonProvider()).LiveTickStyle);
            Assert.Equal(LiveTickStyle.TradeDeltas,
                ((IMarketDataProvider)new AccessibleTrader.Plugins.Bitstamp.BitstampProvider()).LiveTickStyle);
        }

        [Fact]
        public void Binance_kline_stream_uri_covers_spot_futures_and_interval_fallback()
        {
            var p = new AccessibleTrader.Plugins.Binance.BinanceProvider();

            Assert.Equal("wss://stream.binance.com:9443/ws/btcusdt@kline_5m",
                p.BuildKlineStreamUri("Crypto", "BTC/USDT", "5m").ToString());
            Assert.Equal("wss://fstream.binance.com/ws/ethusdt@kline_1d",
                p.BuildKlineStreamUri("Crypto|Futures USD-M", "ETH/USDT", "1d").ToString());
            // Unknown timeframe falls back to 1h — consolidation re-buckets client-side.
            Assert.Contains("@kline_1h",
                p.BuildKlineStreamUri("Crypto", "BTC/USDT", "45m").ToString());
        }

        [Fact]
        public void Binance_kline_parse_reads_the_k_payload_and_rejects_empty_frames()
        {
            using var doc = JsonDocument.Parse("""
                {"e":"kline","k":{"t":1767607200000,"o":"95000.1","h":"95100.5","l":"94900.0","c":"95050.2","v":"12.5"}}
                """);
            Assert.True(AccessibleTrader.Plugins.Binance.BinanceProvider.TryParseKline(doc.RootElement, out var bar));
            Assert.Equal(95050.2, bar.Close);
            Assert.Equal(12.5, bar.Volume);
            Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1767607200000).UtcDateTime, bar.Date);

            using var ping = JsonDocument.Parse("""{"e":"ping"}""");
            Assert.False(AccessibleTrader.Plugins.Binance.BinanceProvider.TryParseKline(ping.RootElement, out _));

            using var zero = JsonDocument.Parse("""
                {"k":{"t":1767607200000,"o":"0","h":"0","l":"0","c":"0","v":"0"}}
                """);
            Assert.False(AccessibleTrader.Plugins.Binance.BinanceProvider.TryParseKline(zero.RootElement, out _));
        }
    }
}
