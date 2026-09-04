using System.Collections.Immutable;
using System.Reactive.Subjects;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Regression fences for the 2026-07-22 adversarial review of the keyed-feeds
    /// code: channel-residue misattribution, pump leaks from concurrent starts,
    /// eviction racing an in-flight subscribe, background reconcile interleaving,
    /// lease leaks on failure, consolidator poisoning, and Binance socket teardown.
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class KeyedFeedsHardeningTests
    {
        private static Ohlcv Bar(int hours, double close = 100) =>
            new(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc).AddHours(hours),
                close, close + 1, close - 1, close, 10);

        private static ChartIdentity Id(string provider = "MultiProv", string symbol = "BTC/USD") =>
            new("Spot", provider, symbol, "1h");

        private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 4000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (!condition())
            {
                Assert.True(Environment.TickCount64 < deadline, "Condition not met within timeout.");
                await Task.Delay(10);
            }
        }

        private static MarketFeedHub Hub(KeyedFeedsTests.FakeOrchestrator? orch = null, IDataService? ds = null) =>
            new(orch ?? new KeyedFeedsTests.FakeOrchestrator(),
                ds ?? Substitute.For<IDataService>(),
                new DemoPolicy(false), NullLoggerFactory.Instance);

        // ── F3: channel residue must not reach the newly focused feed ────────

        [Fact]
        public async Task Focus_start_drains_buffered_ticks_of_the_previous_symbol()
        {
            var orch = new KeyedFeedsTests.FakeOrchestrator();
            using var hub = Hub(orch);
            var feed = hub.SetFocus(Id(symbol: "ETH/USD"));
            feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0) }));

            // Residue from a PREVIOUS subscription for this same identity, still
            // buffered. Stamped with the focused identity on purpose: a tick for a
            // different symbol is now dropped by the pump's identity check, so only a
            // same-identity stale tick can still prove the drain does its own job.
            orch.PushTick(Id(symbol: "ETH/USD"), Bar(5, close: 999));
            orch.PushTick(Id(symbol: "ETH/USD"), Bar(6, close: 998));

            await hub.StartFocusedLiveAsync();
            await Task.Delay(100);
            Assert.Equal(1, feed.Bars.Count); // stale ticks drained, not merged

            orch.PushTick(Id(symbol: "ETH/USD"), Bar(1, close: 111)); // genuinely new tick
            await WaitUntil(() => feed.Bars.Count == 2);
            Assert.Equal(111, feed.Bars[^1].Close);
            await hub.StopFocusedLiveAsync();
        }

        // ── F7: concurrent starts must not leak a pump ───────────────────────

        [Fact]
        public async Task Stop_after_concurrent_starts_leaves_no_live_pump()
        {
            var orch = new KeyedFeedsTests.FakeOrchestrator();
            using var hub = Hub(orch);
            var feed = hub.SetFocus(Id());
            feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0) }));

            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => hub.StartFocusedLiveAsync()));
            await hub.StopFocusedLiveAsync();

            orch.PushTick(Id(), Bar(1));
            await Task.Delay(200);
            Assert.Equal(1, feed.Bars.Count); // a leaked pump would have applied it
        }

        // ── F1: eviction + disposal safety ───────────────────────────────────

        [Fact]
        public async Task Evicted_feed_is_replaced_on_next_request_and_its_late_callers_noop()
        {
            using var hub = Hub();
            var victimId = Id(symbol: "VICTIM/USD");
            var victim = hub.GetOrCreateFeed(victimId); // never touched → coldest

            // Touch 31 others so the victim is deterministically the LRU choice,
            // then one more feed pushes past the 32 cap and evicts it.
            for (int i = 0; i < 31; i++)
                hub.GetOrCreateFeed(Id(symbol: $"SYM{i}/USD"))
                   .RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0) }));
            hub.GetOrCreateFeed(Id(symbol: "OVERFLOW/USD"))
               .RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0) }));

            Assert.True(victim.IsDisposed);
            // Late callers (a socket delivering its final ticks) no-op cleanly.
            Assert.False(victim.ApplyLiveTick(Bar(1)));
            Assert.Equal(-1, await victim.PrependOlderAsync());
            // The registry never hands out the corpse.
            var replacement = hub.GetOrCreateFeed(victimId);
            Assert.NotSame(victim, replacement);
            Assert.False(replacement.IsDisposed);
        }

        private sealed class GatedMultiSubProvider : KeyedFeedsPhaseBTestsSupport.MinimalProviderBase
        {
            public readonly TaskCompletionSource Gate = new();
            public int SubscribeCalls;
            public override bool SupportsMultipleLiveSubscriptions => true;
            public override async Task<IAsyncDisposable> SubscribeLiveAsync(string market, string symbol, string timeframe, Action<Ohlcv> onBar)
            {
                SubscribeCalls++;
                await Gate.Task;
                return new Noop();
            }
            private sealed class Noop : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
        }

        [Fact]
        public async Task Feed_is_pinned_against_eviction_while_its_subscription_is_in_flight()
        {
            var provider = new GatedMultiSubProvider();
            var ds = Substitute.For<IDataService>();
            ds.GetProviderAsync("MultiProv").Returns(Task.FromResult<IMarketDataProvider?>(provider));
            using var hub = Hub(ds: ds);

            var pinnedId = Id(symbol: "PINNED/USD");
            var pinned = hub.GetOrCreateFeed(pinnedId); // untouched → would be the LRU victim
            var starting = hub.TryStartFeedLiveAsync(pinnedId);
            await WaitUntil(() => provider.SubscribeCalls == 1);

            // Heavy eviction pressure while the subscribe is parked on the gate.
            for (int i = 0; i < 40; i++)
                hub.GetOrCreateFeed(Id(symbol: $"SYM{i}/USD"))
                   .RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0) }));

            Assert.False(pinned.IsDisposed); // the in-flight subscribe holds a lease

            provider.Gate.SetResult();
            Assert.Equal(FeedLiveStart.Started, await starting);
            Assert.False(pinned.IsDisposed);
            Assert.True(hub.IsFeedLive(pinnedId));
        }

        // ── F4/F5/F13: background reconcile robustness ───────────────────────

        private sealed class FlakyMultiSubProvider : KeyedFeedsPhaseBTestsSupport.MinimalProviderBase
        {
            public int FailuresRemaining = 1;
            public int SubscribeCalls;
            public readonly List<int> Disposals = new();
            public override bool SupportsMultipleLiveSubscriptions => true;
            public override Task<IAsyncDisposable> SubscribeLiveAsync(string market, string symbol, string timeframe, Action<Ohlcv> onBar)
            {
                SubscribeCalls++;
                if (FailuresRemaining-- > 0) throw new TimeoutException("transient socket failure");
                return Task.FromResult<IAsyncDisposable>(new Noop(this));
            }
            private sealed class Noop : IAsyncDisposable
            {
                private readonly FlakyMultiSubProvider _p;
                public Noop(FlakyMultiSubProvider p) { _p = p; }
                public ValueTask DisposeAsync() { _p.Disposals.Add(1); return ValueTask.CompletedTask; }
            }
        }

        private sealed class TabFeedHarness
        {
            public readonly MarketFeedHub Hub;
            public readonly IDataService DataService = Substitute.For<IDataService>();
            public readonly IWorkspaceStore Store = Substitute.For<IWorkspaceStore>();
            public readonly Subject<WorkspaceState> StateSubject = new();
            public readonly BackgroundTabFeedService Service;
            private WorkspaceState _state = WorkspaceState.Initial;

            public TabFeedHarness(IMarketDataProvider provider)
            {
                DataService.GetProviderAsync("MultiProv").Returns(Task.FromResult<IMarketDataProvider?>(provider));
                Hub = new MarketFeedHub(new KeyedFeedsTests.FakeOrchestrator(), DataService,
                    new DemoPolicy(false), NullLoggerFactory.Instance);
                Store.State.Returns(_ => _state);
                Store.StateStream.Returns(StateSubject);
                var settings = Substitute.For<ISettingsManager>();
                settings.GetSetting(BackgroundTabFeedService.EnabledKey).ReturnsForAnyArgs(JToken.FromObject(true));
                Service = new BackgroundTabFeedService(Store, Substitute.For<IEventBus>(),
                    settings, Hub, NullLogger<BackgroundTabFeedService>.Instance);
            }

            public void SetTabs(ChartIdentity focused, params ChartIdentity[] background)
            {
                var s = WorkspaceState.Initial;
                _state = s with
                {
                    Identity = focused,
                    TabSnapshots = background.Select((id, i) => new TabSnapshot(
                        TabIndex: i + 1, Identity: id, Data: s.Data,
                        ActiveSeries: s.ActiveSeries, FocusedSeriesIndex: s.FocusedSeriesIndex,
                        FocusedSeriesId: s.FocusedSeriesId, FocusedComponentIndex: s.FocusedComponentIndex,
                        FocusedBinIndex: s.FocusedBinIndex, CurrentDataIndex: s.CurrentDataIndex,
                        ViewportStartIndex: s.ViewportStartIndex, ViewportLength: s.ViewportLength,
                        RightMarginBars: s.RightMarginBars, ViewportRange: s.ViewportRange,
                        PaneRanges: s.PaneRanges, IsHeikinAshi: false, IsLogScale: false,
                        LastInteractionContext: s.LastInteractionContext, PaneHeightRatios: s.PaneHeightRatios,
                        InitStatus: InitializationStatus.Ready,
                        DataStatus: DataStatus.Ready, IsCoordinateEntryMode: false,
                        PendingDrawingTool: null, CoordinateEntryAnchorCount: 0,
                        CoordinateEntryAnchor1Index: -1, SymbolDisplayName: id.Symbol)).ToImmutableList(),
                };
            }

            public void PushState() => StateSubject.OnNext(Store.State);
        }

        [Fact]
        public async Task Transient_subscribe_failure_releases_the_lease_and_retries_next_reconcile()
        {
            var provider = new FlakyMultiSubProvider { FailuresRemaining = 1 };
            var h = new TabFeedHarness(provider);
            var background = Id(symbol: "ETH/USD");
            h.SetTabs(Id(symbol: "BTC/USD"), background);

            h.Service.Reconcile(); // subscribe throws (transient)
            await WaitUntil(() => provider.SubscribeCalls == 1 && h.Service.LiveBackgroundFeeds.Count == 0);
            Assert.False(h.Hub.IsFeedLive(background));

            h.Service.Reconcile(); // NOT blacklisted → retried, now succeeds
            await WaitUntil(() => h.Hub.IsFeedLive(background));
            Assert.Equal(2, provider.SubscribeCalls);
        }

        [Fact]
        public async Task Rapid_desired_set_flapping_converges_live_via_the_serialized_chain()
        {
            var provider = new FlakyMultiSubProvider { FailuresRemaining = 0 };
            var h = new TabFeedHarness(provider);
            var background = Id(symbol: "ETH/USD");

            // start → stop → start with no awaits in between: the ordered apply
            // chain must end with a LIVE feed, not a leased-but-dead one.
            h.SetTabs(Id(symbol: "BTC/USD"), background);
            h.Service.Reconcile();
            h.SetTabs(Id(symbol: "BTC/USD"));
            h.Service.Reconcile();
            h.SetTabs(Id(symbol: "BTC/USD"), background);
            h.Service.Reconcile();

            await WaitUntil(() => h.Hub.IsFeedLive(background)
                                  && h.Service.LiveBackgroundFeeds.Contains(background));
        }

        [Fact]
        public async Task Focused_identity_change_stops_the_matching_background_subscription()
        {
            var provider = new FlakyMultiSubProvider { FailuresRemaining = 0 };
            var h = new TabFeedHarness(provider);
            var eth = Id(symbol: "ETH/USD");
            h.SetTabs(Id(symbol: "BTC/USD"), eth);
            h.Service.Reconcile();
            await WaitUntil(() => h.Hub.IsFeedLive(eth));

            // User loads ETH on the CURRENT tab: no TabSwitchedEvent — the
            // identity-change subscription must reconcile, or the legacy pump and
            // the background sub would double-feed one buffer.
            h.SetTabs(focused: eth, background: eth);
            h.PushState();

            await WaitUntil(() => !h.Hub.IsFeedLive(eth));
        }

        // ── F11: consolidator must not be poisoned by rejected ticks ─────────

        private static readonly DateTime H10 = new(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Malformed_tick_leaves_the_bucket_untouched()
        {
            var c = new BarBucketConsolidator("1h", LiveTickStyle.TradeDeltas);
            c.Apply(new Ohlcv(H10, 100, 101, 99, 100, 5));

            // Glitch tick: zero Low but a huge High — must not contaminate H/L.
            Assert.Null(c.Apply(new Ohlcv(H10.AddMinutes(1), 100, 99999, 0, 100, 5)));

            var bar = c.Apply(new Ohlcv(H10.AddMinutes(2), 100, 102, 98, 101, 1))!.Value;
            Assert.Equal(102, bar.High);   // not 99999
            Assert.Equal(98, bar.Low);
            Assert.Equal(6, bar.Volume);   // rejected tick's volume never entered
        }

        [Fact]
        public void Old_period_replay_is_dropped_instead_of_resetting_the_bucket()
        {
            var c = new BarBucketConsolidator("1h", LiveTickStyle.TradeDeltas);
            c.Apply(new Ohlcv(H10, 100, 101, 99, 100, 5));

            Assert.Null(c.Apply(new Ohlcv(H10.AddHours(-1), 90, 91, 89, 90, 3))); // reconnect replay

            var bar = c.Apply(new Ohlcv(H10.AddMinutes(1), 100, 103, 99, 102, 2))!.Value;
            Assert.Equal(H10, bar.Date);
            Assert.Equal(7, bar.Volume);   // current bucket survived the replay
        }

        // ── F12: Binance disconnect tears down keyed sockets ─────────────────

        [Fact]
        public async Task Binance_disconnect_stops_all_keyed_subscriptions()
        {
            var p = new AccessibleTrader.Plugins.Binance.BinanceProvider();
            var h1 = await ((IMarketDataProvider)p).SubscribeLiveAsync("Crypto", "BTC/USDT", "1m", _ => { });
            var h2 = await ((IMarketDataProvider)p).SubscribeLiveAsync("Crypto", "ETH/USDT", "1m", _ => { });
            Assert.Equal(2, p.ActiveKeyedSubscriptionCount);

            await p.DisconnectAsync();
            Assert.Equal(0, p.ActiveKeyedSubscriptionCount);

            // Late handle disposal after disconnect is a clean no-op.
            await h1.DisposeAsync();
            await h2.DisposeAsync();
            Assert.Equal(0, p.ActiveKeyedSubscriptionCount);
        }
    }
}
