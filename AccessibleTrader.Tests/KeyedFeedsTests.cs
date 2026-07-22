using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Phase A of the keyed-feeds refactor (docs/KEYED_FEEDS_DESIGN.md): the
    /// per-identity ChartFeed must carry the exact semantics extracted from the
    /// old focused-chart DataManager — 200-bar refresh, snapshot restore +
    /// gap-fill that never discards scrollback, drop-if-busy prepend, the
    /// live-tick/prepend race guard, and both buffer caps. The hub tests pin
    /// focus routing, lease-aware eviction, and the legacy live pump; the
    /// adapter tests pin the store-dispatch contract.
    /// </summary>
    public class KeyedFeedsTests
    {
        private static Ohlcv Bar(int daysFromEpoch, double close = 100, double vol = 1) =>
            new(new DateTime(2026, 1, 1).AddDays(daysFromEpoch), close, close + 1, close - 1, close, vol);

        private static ChartIdentity Id(string symbol = "BTC/USD", string tf = "1h") =>
            new("Spot", "TestProv", symbol, tf);

        /// <summary>
        /// IDataOrchestrator double: scripted fetch responses (with optional gate for
        /// concurrency tests), a writable live channel, and call capture.
        /// </summary>
        private sealed class FakeOrchestrator : IDataOrchestrator
        {
            public readonly Queue<List<Ohlcv>> FetchResults = new();
            public readonly List<(string Symbol, int? Limit, long? Until)> FetchCalls = new();
            public readonly List<string> LiveStarts = new();
            public int LiveStops;
            public TaskCompletionSource? FetchGate;

            private readonly System.Threading.Channels.Channel<Ohlcv> _channel =
                System.Threading.Channels.Channel.CreateUnbounded<Ohlcv>();
            public System.Threading.Channels.ChannelWriter<Ohlcv> LiveWriter => _channel.Writer;
            public System.Threading.Channels.ChannelReader<Ohlcv> LiveStream => _channel.Reader;

            public DataState CurrentState => DataState.LiveStreaming;
            public IObservable<DataState> StateChanged => System.Reactive.Linq.Observable.Never<DataState>();

            public async Task<List<Ohlcv>> FetchOhlcvAsync(string market, string provider, string symbol,
                string timeframe, long? since = null, int? limit = null, long? until = null, bool silent = false)
            {
                FetchCalls.Add((symbol, limit, until));
                var gate = FetchGate;
                if (gate != null) await gate.Task;
                return FetchResults.Count > 0 ? FetchResults.Dequeue() : new List<Ohlcv>();
            }

            public Task StartLiveStreamAsync(string market, string providerName, string symbol, string timeframe)
            {
                LiveStarts.Add($"{symbol}@{timeframe}");
                return Task.CompletedTask;
            }

            public Task StopLiveStreamAsync() { LiveStops++; return Task.CompletedTask; }
        }

        private static ChartFeed Feed(FakeOrchestrator orch, ChartIdentity? id = null) =>
            new(id ?? Id(), orch, NullLogger.Instance);

        // ── ChartFeed: refresh ───────────────────────────────────────────────

        [Fact]
        public async Task Refresh_replaces_buffer_and_raises_InitialLoad()
        {
            var orch = new FakeOrchestrator();
            orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(0), Bar(1), Bar(2) });
            var feed = Feed(orch);
            var kinds = new List<FeedUpdateKind>();
            feed.Updated += (_, k) => kinds.Add(k);

            Assert.True(await feed.RefreshAsync());

            Assert.Equal(3, feed.Bars.Count);
            Assert.Equal(new[] { FeedUpdateKind.InitialLoad }, kinds);
            Assert.Equal(200, orch.FetchCalls.Single().Limit);
        }

        [Fact]
        public async Task Refresh_with_empty_result_keeps_the_old_buffer()
        {
            var orch = new FakeOrchestrator();
            orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(0) });
            var feed = Feed(orch);
            await feed.RefreshAsync();

            Assert.False(await feed.RefreshAsync()); // second fetch dequeues nothing → empty

            Assert.Equal(1, feed.Bars.Count);
        }

        [Fact]
        public async Task Refresh_with_empty_symbol_never_touches_the_network()
        {
            var orch = new FakeOrchestrator();
            var feed = Feed(orch, ChartIdentity.Empty);

            Assert.False(await feed.RefreshAsync());
            Assert.Empty(orch.FetchCalls);
        }

        [Fact]
        public async Task Refresh_cancelled_after_fetch_throws_and_keeps_the_buffer()
        {
            var orch = new FakeOrchestrator();
            orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(0) });
            var feed = Feed(orch);
            await feed.RefreshAsync();

            orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(5), Bar(6) });
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => feed.RefreshAsync(cts.Token));
            Assert.Equal(1, feed.Bars.Count); // stale load never landed
        }

        // ── ChartFeed: snapshot restore + gap-fill ───────────────────────────

        [Fact]
        public async Task GapFill_appends_only_newer_bars_preserving_scrollback()
        {
            var orch = new FakeOrchestrator();
            var feed = Feed(orch);
            feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0), Bar(1), Bar(2) }));
            // Provider returns overlap + two genuinely new bars.
            orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(1), Bar(2), Bar(3), Bar(4) });

            Assert.True(await feed.GapFillAsync());

            Assert.Equal(5, feed.Bars.Count);
            Assert.Equal(Bar(0).Date, feed.Bars[0].Date); // scrollback intact
            Assert.Equal(Bar(4).Date, feed.Bars[^1].Date);
        }

        [Fact]
        public async Task GapFill_with_no_newer_bars_refreshes_the_live_bar_intrabar()
        {
            var orch = new FakeOrchestrator();
            var feed = Feed(orch);
            feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0), Bar(1, close: 100) }));
            orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(0), Bar(1, close: 123) });

            Assert.True(await feed.GapFillAsync());

            Assert.Equal(2, feed.Bars.Count);
            Assert.Equal(123, feed.Bars[^1].Close); // live bar updated in place
        }

        [Fact]
        public async Task GapFill_with_empty_fetch_returns_false()
        {
            var orch = new FakeOrchestrator();
            var feed = Feed(orch);
            feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0) }));

            Assert.False(await feed.GapFillAsync());
            Assert.Equal(1, feed.Bars.Count);
        }

        // ── ChartFeed: prepend ───────────────────────────────────────────────

        [Fact]
        public async Task Prepend_filters_to_strictly_older_bars_and_fetches_before_first()
        {
            var orch = new FakeOrchestrator();
            var feed = Feed(orch);
            feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(10), Bar(11) }));
            orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(8), Bar(9), Bar(10) }); // 10 overlaps

            bool started = false;
            int count = await feed.PrependOlderAsync(() => started = true);

            Assert.True(started);
            Assert.Equal(2, count);
            Assert.Equal(4, feed.Bars.Count);
            Assert.Equal(Bar(8).Date, feed.Bars[0].Date);
            // until must point just before the first bar's timestamp
            long firstMs = new DateTimeOffset(Bar(10).Date).ToUnixTimeMilliseconds();
            Assert.Equal(firstMs - 1, orch.FetchCalls.Single().Until);
        }

        [Fact]
        public async Task Prepend_while_one_is_in_flight_is_dropped_without_onStarted()
        {
            var orch = new FakeOrchestrator { FetchGate = new TaskCompletionSource() };
            var feed = Feed(orch);
            feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(10) }));
            orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(9) });

            var inFlight = feed.PrependOlderAsync();
            bool secondStarted = false;
            Assert.Equal(-1, await feed.PrependOlderAsync(() => secondStarted = true));
            Assert.False(secondStarted);

            orch.FetchGate.SetResult();
            Assert.Equal(1, await inFlight);
        }

        // ── ChartFeed: live ticks ────────────────────────────────────────────

        [Fact]
        public void LiveTick_appends_new_period_and_replaces_intrabar()
        {
            var feed = Feed(new FakeOrchestrator());
            feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0) }));
            var kinds = new List<FeedUpdateKind>();
            feed.Updated += (_, k) => kinds.Add(k);

            Assert.True(feed.ApplyLiveTick(Bar(1, close: 50)));   // new period → append
            Assert.True(feed.ApplyLiveTick(Bar(1, close: 55)));   // same period → replace

            Assert.Equal(2, feed.Bars.Count);
            Assert.Equal(55, feed.Bars[^1].Close);
            Assert.Equal(new[] { FeedUpdateKind.LiveAppend, FeedUpdateKind.LiveReplace }, kinds);
        }

        [Fact]
        public async Task LiveTick_during_prepend_is_dropped()
        {
            var orch = new FakeOrchestrator { FetchGate = new TaskCompletionSource() };
            var feed = Feed(orch);
            feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(10) }));
            orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(9) });

            var inFlight = feed.PrependOlderAsync();
            Assert.False(feed.ApplyLiveTick(Bar(11))); // prepend holds the lock → dropped

            orch.FetchGate.SetResult();
            await inFlight;
            Assert.True(feed.ApplyLiveTick(Bar(11))); // lock released → merges again
        }

        [Fact]
        public void LiveTick_growth_stops_at_the_2000_bar_live_cap()
        {
            var feed = Feed(new FakeOrchestrator());
            feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(
                Enumerable.Range(0, 2000).Select(i => Bar(i)).ToArray()));

            feed.ApplyLiveTick(Bar(2001));

            Assert.Equal(2000, feed.Bars.Count); // appended then shed the oldest
            Assert.Equal(Bar(1).Date, feed.Bars[0].Date);
            Assert.Equal(Bar(2001).Date, feed.Bars[^1].Date);
        }

        // ── MarketFeedHub ────────────────────────────────────────────────────

        [Fact]
        public void Hub_returns_the_same_feed_instance_per_identity()
        {
            using var hub = new MarketFeedHub(new FakeOrchestrator(), NullLoggerFactory.Instance);

            var a = hub.GetOrCreateFeed(Id("BTC/USD"));
            Assert.Same(a, hub.GetOrCreateFeed(Id("BTC/USD")));
            Assert.NotSame(a, hub.GetOrCreateFeed(Id("ETH/USD")));
            Assert.NotSame(a, hub.GetOrCreateFeed(Id("BTC/USD", tf: "5m"))); // timeframe is part of the key
        }

        [Fact]
        public void Hub_forwards_updates_from_the_focused_feed_only()
        {
            using var hub = new MarketFeedHub(new FakeOrchestrator(), NullLoggerFactory.Instance);
            var focused = hub.SetFocus(Id("BTC/USD"));
            var other = hub.GetOrCreateFeed(Id("ETH/USD"));
            var forwarded = new List<FeedUpdateKind>();
            hub.FocusedFeedUpdated += (_, k) => forwarded.Add(k);

            focused.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0) }));
            other.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0) }));

            Assert.Equal(new[] { FeedUpdateKind.SnapshotRestore }, forwarded);
        }

        [Fact]
        public async Task Hub_live_pump_routes_orchestrator_ticks_into_the_focused_feed()
        {
            var orch = new FakeOrchestrator();
            using var hub = new MarketFeedHub(orch, NullLoggerFactory.Instance);
            var feed = hub.SetFocus(Id("BTC/USD"));
            feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0) }));

            await hub.StartFocusedLiveAsync();
            Assert.Equal(new[] { "BTC/USD@1h" }, orch.LiveStarts);

            orch.LiveWriter.TryWrite(Bar(1, close: 77));
            await WaitUntil(() => feed.Bars.Count == 2);
            Assert.Equal(77, feed.Bars[^1].Close);

            int stopsBefore = orch.LiveStops; // Start begins with a defensive stop, same as the old pipeline
            await hub.StopFocusedLiveAsync();
            Assert.Equal(stopsBefore + 1, orch.LiveStops);
            orch.LiveWriter.TryWrite(Bar(2));
            await Task.Delay(100);
            Assert.Equal(2, feed.Bars.Count); // pump stopped — tick not applied
        }

        [Fact]
        public void Hub_eviction_skips_focused_and_leased_feeds_and_sheds_the_coldest()
        {
            using var hub = new MarketFeedHub(new FakeOrchestrator(), NullLoggerFactory.Instance);
            var focused = hub.SetFocus(Id("FOCUS"));
            using var lease = hub.AcquireLease(Id("LEASED"));

            // Fill to the 32-feed cap (FOCUS + LEASED + 30 more), then add one over.
            for (int i = 0; i < 30; i++) hub.GetOrCreateFeed(Id($"SYM{i}"));
            hub.GetOrCreateFeed(Id("OVERFLOW"));

            Assert.NotNull(hub.TryGetFeed(Id("FOCUS")));
            Assert.NotNull(hub.TryGetFeed(Id("LEASED")));
            Assert.NotNull(hub.TryGetFeed(Id("OVERFLOW")));
            // Exactly one of the unpinned feeds was evicted (the coldest).
            int survivors = Enumerable.Range(0, 30).Count(i => hub.TryGetFeed(Id($"SYM{i}")) != null);
            Assert.Equal(29, survivors);
        }

        [Fact]
        public void Hub_released_lease_unpins_the_feed_for_eviction()
        {
            using var hub = new MarketFeedHub(new FakeOrchestrator(), NullLoggerFactory.Instance);
            var lease = hub.AcquireLease(Id("PINNED"));
            lease.Dispose();
            lease.Dispose(); // double-dispose must not underflow another holder's count

            var lease2 = hub.AcquireLease(Id("PINNED"));
            for (int i = 0; i < 31; i++) hub.GetOrCreateFeed(Id($"SYM{i}"));
            hub.GetOrCreateFeed(Id("OVERFLOW"));
            Assert.NotNull(hub.TryGetFeed(Id("PINNED"))); // still pinned by lease2
        }

        // ── DataManager adapter: store-dispatch contract ─────────────────────

        private sealed class AdapterHarness
        {
            public readonly FakeOrchestrator Orch = new();
            public readonly MarketFeedHub Hub;
            public readonly IWorkspaceStore Store = Substitute.For<IWorkspaceStore>();
            public readonly List<WorkspaceAction> Dispatched = new();
            public readonly DataManager Manager;
            private DataStatus _status = DataStatus.Ready;

            public AdapterHarness()
            {
                Hub = new MarketFeedHub(Orch, NullLoggerFactory.Instance);
                Store.State.Returns(_ => WorkspaceState.Initial with { DataStatus = _status });
                Store.When(s => s.Dispatch(Arg.Any<WorkspaceAction>())).Do(ci =>
                {
                    var action = ci.Arg<WorkspaceAction>();
                    lock (Dispatched) Dispatched.Add(action);
                    if (action is SetDataStatusAction sd) _status = sd.Status;
                });
                Manager = new DataManager(Hub, Store,
                    Substitute.For<IEventBus>(), NullLogger<DataManager>.Instance,
                    Substitute.For<IServiceProvider>());
            }
        }

        [Fact]
        public async Task Adapter_refresh_dispatches_initial_load_zoom_navigate_and_starts_live()
        {
            var h = new AdapterHarness();
            h.Orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(0), Bar(1) });
            h.Manager.Identity = Id("BTC/USD");
            bool dataUpdated = false;
            h.Manager.DataUpdated += () => dataUpdated = true;

            await h.Manager.RefreshDataAsync();
            await WaitUntil(() => h.Orch.LiveStarts.Count > 0); // StartLiveUpdates is fire-and-forget

            Assert.True(dataUpdated);
            lock (h.Dispatched)
            {
                var update = Assert.IsType<UpdateDataAction>(h.Dispatched[0]);
                Assert.True(update.IsInitialLoad);
                Assert.Equal(100, Assert.IsType<ZoomAction>(h.Dispatched[1]).NewLength);
                Assert.Equal(1, Assert.IsType<NavigateAction>(h.Dispatched[2]).NewIndex);
            }
        }

        [Fact]
        public async Task Adapter_prepend_wraps_the_fetch_in_LoadingHistorical_and_resets()
        {
            var h = new AdapterHarness();
            h.Manager.Identity = Id("BTC/USD");
            h.Hub.FocusedFeed!.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(10) }));
            h.Orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(9) });
            lock (h.Dispatched) h.Dispatched.Clear();

            await h.Manager.PrependOlderDataAsync();

            lock (h.Dispatched)
            {
                var statuses = h.Dispatched.OfType<SetDataStatusAction>().Select(a => a.Status).ToList();
                Assert.Equal(new[] { DataStatus.LoadingHistorical, DataStatus.Ready }, statuses);
                Assert.Single(h.Dispatched.OfType<UpdateDataAction>());
            }
        }

        [Fact]
        public async Task Adapter_live_tick_dispatches_UpdateData_but_not_DataUpdated()
        {
            // Pins today's semantics: DataUpdated stays a load/prepend/catch-up event.
            // The Phase C strategy fix subscribes to the feed's LiveAppend instead.
            var h = new AdapterHarness();
            h.Manager.Identity = Id("BTC/USD");
            h.Hub.FocusedFeed!.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0) }));
            await h.Manager.StartLiveUpdates();
            bool dataUpdated = false;
            h.Manager.DataUpdated += () => dataUpdated = true;
            lock (h.Dispatched) h.Dispatched.Clear();

            h.Orch.LiveWriter.TryWrite(Bar(1));
            await WaitUntil(() => { lock (h.Dispatched) return h.Dispatched.OfType<UpdateDataAction>().Any(); });

            Assert.False(dataUpdated);
            await h.Manager.StopLiveUpdatesAsync();
        }

        [Fact]
        public async Task Adapter_catchup_restores_then_gapfills_then_forces_indicator_recalc()
        {
            var h = new AdapterHarness();
            h.Manager.Identity = Id("BTC/USD");
            var eventBus = Substitute.For<IEventBus>();
            var manager = new DataManager(h.Hub, h.Store, eventBus,
                NullLogger<DataManager>.Instance, Substitute.For<IServiceProvider>());
            h.Orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(2), Bar(3) });

            await manager.CatchUpFromSnapshotAsync(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0), Bar(1), Bar(2) }));

            Assert.Equal(4, h.Hub.FocusedFeed!.Bars.Count); // 3 snapshot + 1 gap bar
            lock (h.Dispatched) Assert.Equal(2, h.Dispatched.OfType<UpdateDataAction>().Count());
            eventBus.Received(1).Publish(Arg.Is<IndicatorUpdatedEvent>(e => e.SeriesId == "__catchup__"));
        }

        private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 3000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (!condition())
            {
                Assert.True(Environment.TickCount64 < deadline, "Condition not met within timeout.");
                await Task.Delay(10);
            }
        }
    }
}
