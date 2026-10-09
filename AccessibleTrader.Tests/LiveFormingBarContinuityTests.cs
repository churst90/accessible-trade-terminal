using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The live forming bar CONTINUES the provider's fetched forming bar.
    ///
    /// <para>Reported by Cody, 2026-10-09: on a weekly chart (and every other) the most recent
    /// candle "starts flat", as if it began when he opened the chart. The providers return the
    /// forming bar on fetch; the live subscription's consolidator then built its first bucket
    /// from the first tick alone and <c>ChartFeed.ApplyLiveTick</c> replaced the real bar with
    /// it — the week's open, range and volume gone on the first trade.</para>
    ///
    /// <para>These drive the REAL pipeline: <see cref="LiveStreamManager"/>'s consolidator into
    /// <see cref="MarketFeedHub"/>'s focused pump into a <see cref="ChartFeed"/> that fetched
    /// its bars through the orchestrator — and, for background tabs, the hub's per-feed
    /// subscription. One fact per tick style: trade deltas (Bitstamp), full chart-interval
    /// klines (Binance/MEXC/Kraken/Schwab), and minute bars on a coarser chart
    /// (Alpaca/Polygon).</para>
    /// </summary>
    public class LiveFormingBarContinuityTests
    {
        private const string Market = "Crypto";
        private const string Prov = "ContinuityProv";
        private const string Symbol = "BTC/USD";

        // Monday 2026-10-05 is the forming week; the fetch says it opened at 100, has ranged
        // 90-120, last traded 110 and done 500 so far.
        private static readonly DateTime Week = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Thursday = new(2026, 10, 8, 15, 0, 0, DateTimeKind.Utc);
        private static readonly Ohlcv PriorWeek = new(Week.AddDays(-7), 95, 105, 94, 100, 900);
        private static readonly Ohlcv FetchedWeek = new(Week, 100, 120, 90, 110, 500);

        private static ChartIdentity Id(string tf = "1w") => new(Market, Prov, Symbol, tf);

        private sealed class StreamProvider : BaseMarketDataProvider
        {
            private readonly LiveTickStyle _style;
            public StreamProvider(LiveTickStyle style) { _style = style; }

            public override string Name => Prov;
            public override string Description => "test";
            public override List<MarketType> SupportedMarkets => new() { MarketType.Crypto };
            public override bool SupportsSymbolSearch => false;
            public override bool RequiresApiKey => false;
            public override bool IsConfigured => true;
            public override bool SupportsLiveUpdates => true;
            public override ProviderEnvironment Environment => ProviderEnvironment.Live;
            public override int MaxBarsPerRequest => 100;
            public override List<string> NativelySupportedTimeframes => new() { "1d", "1w" };
            public override LiveTickStyle LiveTickStyle => _style;
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

            /// <summary>The focused chart's socket.</summary>
            public void PushTick(Ohlcv tick) => _liveStream.OnNext(tick);

            // A background subscription shaped exactly like Bitstamp's/Kraken's: its own
            // consolidator, raw ticks in, consolidated buckets out.
            public override bool SupportsMultipleLiveSubscriptions => true;
            private Action<Ohlcv>? _onBackgroundTick;
            public override Task<IAsyncDisposable> SubscribeLiveAsync(string market, string symbol, string timeframe, Action<Ohlcv> onBar)
            {
                var consolidator = new BarBucketConsolidator(timeframe, _style);
                _onBackgroundTick = raw => { var bar = consolidator.Apply(raw); if (bar.HasValue) onBar(bar.Value); };
                return Task.FromResult<IAsyncDisposable>(new NoopHandle());
            }
            public void PushBackgroundTick(Ohlcv tick) => _onBackgroundTick!(tick);

            private sealed class NoopHandle : IAsyncDisposable
            {
                public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            }
        }

        /// <summary>The fetch the orchestrator serves; tests swap it to model a re-fetch.</summary>
        private sealed class Fetch { public List<Ohlcv> Bars = new() { PriorWeek, FetchedWeek }; }

        private sealed class Rig : IDisposable
        {
            public required StreamProvider Provider;
            public required LiveStreamManager Manager;
            public required MarketFeedHub Hub;
            public required Fetch Fetch;
            public void Dispose() { Hub.Dispose(); Manager.Dispose(); }
        }

        private static Rig Build(LiveTickStyle style)
        {
            var provider = new StreamProvider(style);
            var data = Substitute.For<IDataService>();
            data.GetProviderAsync(Prov).Returns(Task.FromResult<IMarketDataProvider?>(provider));
            var manager = new LiveStreamManager(data, Substitute.For<IGlobalErrorCoordinator>(),
                NullLogger<LiveStreamManager>.Instance);

            var fetch = new Fetch();
            var orchestrator = Substitute.For<IDataOrchestrator>();
            orchestrator.LiveStream.Returns(manager.LiveStream);
            orchestrator.FetchOhlcvAsync(default!, default!, default!, default!, default, default, default, default, default)
                .ReturnsForAnyArgs(_ => Task.FromResult(new List<Ohlcv>(fetch.Bars)));
            orchestrator.StartLiveStreamAsync(default!, default!, default!, default!)
                .ReturnsForAnyArgs(ci => manager.StartLiveStreamAsync(
                    ci.ArgAt<string>(0), ci.ArgAt<string>(1), ci.ArgAt<string>(2), ci.ArgAt<string>(3)));
            orchestrator.StopLiveStreamAsync().Returns(_ => manager.StopLiveStreamAsync());

            var hub = new MarketFeedHub(orchestrator, data, new DemoPolicy(false), NullLoggerFactory.Instance);
            return new Rig { Provider = provider, Manager = manager, Hub = hub, Fetch = fetch };
        }

        private static async Task Until(Func<bool> condition, string what)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new Xunit.Sdk.XunitException($"Timed out waiting for {what}.");
                await Task.Delay(10);
            }
        }

        private static Ohlcv Trade(DateTime at, double price, double size) => new(at, price, price, price, price, size);

        /// <summary>Pushes one tick on the focused socket and waits for the feed to apply it.</summary>
        private static async Task FocusedTick(Rig rig, ChartFeed feed, Ohlcv tick)
        {
            int beforeCount = feed.Bars.Count;
            var beforeBar = beforeCount > 0 ? feed.Bars[^1] : default;
            rig.Provider.PushTick(tick);
            await Until(() => feed.Bars.Count != beforeCount || (beforeCount > 0 && !feed.Bars[^1].Equals(beforeBar)),
                $"the tick at {tick.Close} to reach the feed");
        }

        private static void AssertBar(Ohlcv bar, DateTime date, double o, double h, double l, double c, double v)
        {
            Assert.Equal(date, bar.Date);
            Assert.True(bar.Open == o, $"Open: expected {o} (the provider's), got {bar.Open}");
            Assert.True(bar.High == h, $"High: expected {h}, got {bar.High}");
            Assert.True(bar.Low == l, $"Low: expected {l}, got {bar.Low}");
            Assert.True(bar.Close == c, $"Close: expected {c} (the latest tick), got {bar.Close}");
            Assert.True(Math.Abs(bar.Volume - v) < 1e-9, $"Volume: expected {v}, got {bar.Volume}");
        }

        private static async Task<ChartFeed> FocusAndLoad(Rig rig, string tf = "1w")
        {
            var feed = rig.Hub.SetFocus(Id(tf));
            Assert.True(await feed.RefreshAsync());
            await rig.Hub.StartFocusedLiveAsync();
            return feed;
        }

        // ── Trade deltas (Bitstamp, the demo/default provider) ────────────────────

        [Fact]
        public async Task Trade_ticks_continue_the_fetched_weekly_bar_instead_of_starting_it_flat()
        {
            using var rig = Build(LiveTickStyle.TradeDeltas);
            var feed = await FocusAndLoad(rig);

            await FocusedTick(rig, feed, Trade(Thursday, 111, 2));
            AssertBar(feed.Bars[^1], Week, o: 100, h: 120, l: 90, c: 111, v: 502);

            await FocusedTick(rig, feed, Trade(Thursday.AddMinutes(1), 85, 1));
            await FocusedTick(rig, feed, Trade(Thursday.AddMinutes(2), 125, 0.5));
            AssertBar(feed.Bars[^1], Week, o: 100, h: 125, l: 85, c: 125, v: 503.5);
            Assert.Equal(2, feed.Bars.Count);
        }

        [Fact]
        public async Task Trade_ticks_on_a_background_feed_continue_the_fetched_bar_too()
        {
            using var rig = Build(LiveTickStyle.TradeDeltas);
            var feed = rig.Hub.GetOrCreateFeed(Id());
            Assert.True(await feed.RefreshAsync());
            Assert.Equal(FeedLiveStart.Started, await rig.Hub.TryStartFeedLiveAsync(Id()));

            rig.Provider.PushBackgroundTick(Trade(Thursday, 111, 2));
            rig.Provider.PushBackgroundTick(Trade(Thursday.AddMinutes(1), 85, 1));

            AssertBar(feed.Bars[^1], Week, o: 100, h: 120, l: 85, c: 85, v: 503);
        }

        [Fact]
        public async Task Ticks_before_the_fetch_lands_are_not_counted_twice()
        {
            // Subscribed first, history second: the trades the stream saw before the fetch
            // returned are inside the fetched bar, so only what comes AFTER it adds.
            using var rig = Build(LiveTickStyle.TradeDeltas);
            var feed = rig.Hub.SetFocus(Id());
            await rig.Hub.StartFocusedLiveAsync();

            await FocusedTick(rig, feed, Trade(Thursday, 111, 2));      // buffer empty: appended
            await FocusedTick(rig, feed, Trade(Thursday.AddSeconds(1), 112, 3));
            Assert.Single(feed.Bars);

            rig.Fetch.Bars = new() { PriorWeek, new Ohlcv(Week, 100, 120, 90, 112, 505) };
            Assert.True(await feed.RefreshAsync());

            await FocusedTick(rig, feed, Trade(Thursday.AddSeconds(2), 113, 1));
            AssertBar(feed.Bars[^1], Week, o: 100, h: 120, l: 90, c: 113, v: 506);
        }

        [Fact]
        public async Task A_gap_fill_mid_stream_resets_the_bar_to_the_provider_and_keeps_counting_from_there()
        {
            using var rig = Build(LiveTickStyle.TradeDeltas);
            var feed = await FocusAndLoad(rig);
            await FocusedTick(rig, feed, Trade(Thursday, 111, 3));          // 503

            // The re-fetch already counts those 3 (and 17 more the socket missed).
            rig.Fetch.Bars = new() { PriorWeek, new Ohlcv(Week, 100, 121, 90, 111, 520) };
            Assert.True(await feed.GapFillAsync());
            AssertBar(feed.Bars[^1], Week, o: 100, h: 121, l: 90, c: 111, v: 520);

            await FocusedTick(rig, feed, Trade(Thursday.AddMinutes(1), 112, 1));
            AssertBar(feed.Bars[^1], Week, o: 100, h: 121, l: 90, c: 112, v: 521);
        }

        [Fact]
        public async Task A_period_rollover_closes_the_continued_bar_and_opens_the_next_from_its_first_trade()
        {
            using var rig = Build(LiveTickStyle.TradeDeltas);
            var feed = await FocusAndLoad(rig);
            await FocusedTick(rig, feed, Trade(Thursday, 111, 2));

            var nextWeek = Week.AddDays(7);
            await FocusedTick(rig, feed, Trade(nextWeek.AddSeconds(3), 130, 4));
            await FocusedTick(rig, feed, Trade(nextWeek.AddSeconds(9), 128, 1));

            Assert.Equal(3, feed.Bars.Count);
            AssertBar(feed.Bars[^2], Week, o: 100, h: 120, l: 90, c: 111, v: 502);
            // The stream saw the whole of the new week, so its bucket IS that week.
            AssertBar(feed.Bars[^1], nextWeek, o: 130, h: 130, l: 128, c: 128, v: 5);
        }

        [Fact]
        public async Task A_resubscribe_starts_a_new_consolidator_without_losing_or_recounting_volume()
        {
            using var rig = Build(LiveTickStyle.TradeDeltas);
            var feed = await FocusAndLoad(rig);
            await FocusedTick(rig, feed, Trade(Thursday, 111, 2));          // 502

            // Tab away and back: a fresh subscription, a fresh consolidator starting at zero.
            await rig.Hub.StopFocusedLiveAsync();
            await rig.Hub.StartFocusedLiveAsync();

            await FocusedTick(rig, feed, Trade(Thursday.AddMinutes(5), 109, 0.25));
            AssertBar(feed.Bars[^1], Week, o: 100, h: 120, l: 90, c: 109, v: 502.25);
        }

        [Fact]
        public void A_superseded_streams_late_ticks_are_dropped()
        {
            var feed = new ChartFeed(Id(), Substitute.For<IDataOrchestrator>(), NullLogger.Instance);
            feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new List<Ohlcv> { PriorWeek, FetchedWeek }));
            var old = LiveStreamSource.Create(LiveTickStyle.TradeDeltas);
            var current = LiveStreamSource.Create(LiveTickStyle.TradeDeltas);

            Assert.True(feed.ApplyLiveTick(new Ohlcv(Week, 111, 111, 111, 111, 2), current));
            // The outgoing socket's bucket counts the same trades; it must not add them again.
            Assert.False(feed.ApplyLiveTick(new Ohlcv(Week, 111, 112, 111, 112, 2), old));
            AssertBar(feed.Bars[^1], Week, o: 100, h: 120, l: 90, c: 111, v: 502);
        }

        // ── Cumulative bars ──────────────────────────────────────────────────────

        [Fact]
        public async Task A_full_chart_interval_kline_keeps_replacing_and_is_not_double_counted()
        {
            // Binance/MEXC/Kraken send the whole forming candle with volume-so-far. It already
            // contains everything the fetch did; adding the two would double the week.
            using var rig = Build(LiveTickStyle.CumulativeBars);
            var feed = await FocusAndLoad(rig);

            await FocusedTick(rig, feed, new Ohlcv(Week, 100, 120, 90, 111, 504));
            AssertBar(feed.Bars[^1], Week, o: 100, h: 120, l: 90, c: 111, v: 504);

            await FocusedTick(rig, feed, new Ohlcv(Week, 100, 122, 90, 121, 507));
            AssertBar(feed.Bars[^1], Week, o: 100, h: 122, l: 90, c: 121, v: 507);
        }

        [Fact]
        public async Task Minute_bars_on_a_daily_chart_add_each_new_minute_once_on_top_of_the_fetched_day()
        {
            // Alpaca/Polygon stream minute bars whatever the chart. The day's fetched bar
            // already holds the minute in progress when we subscribed; every later minute,
            // and growth in the current one, is new.
            using var rig = Build(LiveTickStyle.CumulativeBars);
            var day = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);
            rig.Fetch.Bars = new() { new Ohlcv(day.AddDays(-1), 50, 55, 49, 52, 800), new Ohlcv(day, 52, 60, 51, 58, 1000) };
            var feed = await FocusAndLoad(rig, "1d");

            var m1 = day.AddHours(14).AddMinutes(5);
            await FocusedTick(rig, feed, new Ohlcv(m1, 58, 58.5, 57.5, 58.1, 10));
            AssertBar(feed.Bars[^1], day, o: 52, h: 60, l: 51, c: 58.1, v: 1000);

            await FocusedTick(rig, feed, new Ohlcv(m1, 58, 58.5, 57.5, 58.2, 12));     // same minute grows by 2
            await FocusedTick(rig, feed, new Ohlcv(m1.AddMinutes(1), 58.2, 61, 58, 61, 7)); // next minute: all 7 new
            AssertBar(feed.Bars[^1], day, o: 52, h: 61, l: 51, c: 61, v: 1009);
        }

        // ── Providers that stamp a bar away from the UTC period start ─────────────

        [Fact]
        public async Task A_stock_daily_bar_stamped_at_eastern_midnight_still_takes_the_live_minutes()
        {
            // Alpaca and Polygon stamp a US session's daily bar at 04:00Z (midnight New York);
            // the consolidator's bucket for that day is 00:00Z. Compared for equality, every
            // live bucket was "older" than the bar and dropped: the daily candle never moved.
            using var rig = Build(LiveTickStyle.CumulativeBars);
            var day = new DateTime(2026, 10, 9, 4, 0, 0, DateTimeKind.Utc);
            rig.Fetch.Bars = new() { new Ohlcv(day.AddDays(-1), 50, 55, 49, 52, 800), new Ohlcv(day, 52, 60, 51, 58, 1000) };
            var feed = await FocusAndLoad(rig, "1d");

            var m1 = new DateTime(2026, 10, 9, 14, 5, 0, DateTimeKind.Utc);
            await FocusedTick(rig, feed, new Ohlcv(m1, 58, 58.5, 57.5, 58.1, 10));
            await FocusedTick(rig, feed, new Ohlcv(m1.AddMinutes(1), 58.1, 62, 58, 61.5, 7));

            Assert.Equal(2, feed.Bars.Count);
            // The provider's stamp is kept — it is the bar's identity everywhere else.
            AssertBar(feed.Bars[^1], day, o: 52, h: 62, l: 51, c: 61.5, v: 1007);
        }

        [Fact]
        public async Task A_sunday_stamped_weekly_bar_is_the_week_the_live_bucket_belongs_to()
        {
            // Polygon's weekly aggregates start on the Sunday (04:00Z). The live bucket is the
            // Monday; nearest-period matching makes them one bar instead of a phantom second week.
            using var rig = Build(LiveTickStyle.TradeDeltas);
            var sunday = new DateTime(2026, 10, 4, 4, 0, 0, DateTimeKind.Utc);
            rig.Fetch.Bars = new() { new Ohlcv(sunday.AddDays(-7), 95, 105, 94, 100, 900), new Ohlcv(sunday, 100, 120, 90, 110, 500) };
            var feed = await FocusAndLoad(rig);

            await FocusedTick(rig, feed, Trade(Thursday, 111, 2));

            Assert.Equal(2, feed.Bars.Count);
            AssertBar(feed.Bars[^1], sunday, o: 100, h: 120, l: 90, c: 111, v: 502);
        }

        [Fact]
        public async Task A_gap_fill_after_a_live_appended_day_replaces_it_rather_than_charting_the_day_twice()
        {
            // The stream crossed midnight and appended the new day at 00:00Z; the provider's own
            // bar for that day is stamped 04:00Z. They are one day.
            using var rig = Build(LiveTickStyle.TradeDeltas);
            var day = new DateTime(2026, 10, 8, 4, 0, 0, DateTimeKind.Utc);
            rig.Fetch.Bars = new() { new Ohlcv(day.AddDays(-1), 50, 55, 49, 52, 800), new Ohlcv(day, 52, 60, 51, 58, 1000) };
            var feed = await FocusAndLoad(rig, "1d");

            var nextUtcDay = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);
            await FocusedTick(rig, feed, Trade(nextUtcDay.AddHours(13.5), 59, 3));
            Assert.Equal(3, feed.Bars.Count);
            Assert.Equal(nextUtcDay, feed.Bars[^1].Date);

            var providerDay = new Ohlcv(day.AddDays(1), 58, 60, 57, 59, 40);
            rig.Fetch.Bars = new() { new Ohlcv(day, 52, 60, 51, 58, 1100), providerDay };
            Assert.True(await feed.GapFillAsync());

            Assert.Equal(3, feed.Bars.Count);
            Assert.Equal(providerDay, feed.Bars[^1]);
        }
    }
}
