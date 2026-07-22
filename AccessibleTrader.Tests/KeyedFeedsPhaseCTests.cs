using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Phase C of the keyed-feeds refactor: the payoff wiring. Strategies now
    /// evaluate on live BAR CLOSES on the focused chart (fixing the audit
    /// finding that DataUpdated never fired for live ticks); tab switches bind
    /// warm feeds instantly instead of re-fetching; MarketFeeds serves live hub
    /// buffers to background monitors; and BackgroundTabFeedService keeps
    /// non-focused tabs' feeds live behind the opt-in setting.
    /// </summary>
    public class KeyedFeedsPhaseCTests
    {
        private static Ohlcv Bar(int hours, double close = 100) =>
            new(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc).AddHours(hours),
                close, close + 1, close - 1, close, 10);

        private static ChartIdentity Id(string provider = "MultiProv", string symbol = "BTC/USD") =>
            new("Spot", provider, symbol, "1h");

        private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 3000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (!condition())
            {
                Assert.True(Environment.TickCount64 < deadline, "Condition not met within timeout.");
                await Task.Delay(10);
            }
        }

        // ── Shared fakes ─────────────────────────────────────────────────────

        private sealed class MultiSubProvider : KeyedFeedsPhaseBTestsSupport.MinimalProviderBase
        {
            public int SubscribeCalls;
            public override bool SupportsMultipleLiveSubscriptions => true;
            public override Task<IAsyncDisposable> SubscribeLiveAsync(string market, string symbol, string timeframe, Action<Ohlcv> onBar)
            {
                SubscribeCalls++;
                return Task.FromResult<IAsyncDisposable>(new Noop());
            }
            private sealed class Noop : IAsyncDisposable
            {
                public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            }
        }

        private static (MarketFeedHub Hub, IDataService DataService, MultiSubProvider Provider) BuildHub()
        {
            var provider = new MultiSubProvider();
            var dataService = Substitute.For<IDataService>();
            dataService.GetProviderAsync("MultiProv").Returns(Task.FromResult<IMarketDataProvider?>(provider));
            var hub = new MarketFeedHub(Substitute.For<IDataOrchestrator>(), dataService,
                new DemoPolicy(false), NullLoggerFactory.Instance);
            return (hub, dataService, provider);
        }

        // ── Strategy live bar-close evaluation ───────────────────────────────

        private sealed class EngineHarness
        {
            public readonly MarketFeedHub Hub;
            public readonly IEventBus EventBus = Substitute.For<IEventBus>();
            public readonly ITradingStrategy Strategy = Substitute.For<ITradingStrategy>();
            public readonly StrategyEngine Engine;
            public readonly ChartFeed Feed;

            public EngineHarness()
            {
                (Hub, _, _) = BuildHub();
                Feed = Hub.SetFocus(Id());
                var store = Substitute.For<IWorkspaceStore>();
                store.State.Returns(WorkspaceState.Initial);
                Strategy.Name.Returns("TestStrategy");
                Engine = new StrategyEngine(EventBus, Substitute.For<IOrderExecutionService>(),
                    Substitute.For<IAppLogger>(), NullLogger<StrategyEngine>.Instance,
                    Substitute.For<IDataManager>(), store,
                    Substitute.For<IStrategyIndicatorCache>(), Hub);
                Engine.AddStrategy(Strategy);
            }
        }

        [Fact]
        public async Task Live_bar_close_evaluates_the_closed_bar_with_history_excluding_the_forming_bar()
        {
            var h = new EngineHarness();
            h.Feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0), Bar(1, close: 111) }));

            // A tick for a NEW period → bar 1 (close 111) just closed.
            Assert.True(h.Feed.ApplyLiveTick(Bar(2, close: 200)));

            await WaitUntil(() => h.Strategy.ReceivedCalls().Any(c => c.GetMethodInfo().Name == "OnBar"));
            var call = h.Strategy.ReceivedCalls().Single(c => c.GetMethodInfo().Name == "OnBar");
            var args = call.GetArguments();
            var evaluated = (Ohlcv)args[0]!;
            var history = (IReadOnlyList<Ohlcv>)args[1]!;

            Assert.Equal(111, evaluated.Close);          // the CLOSED bar, not the forming one
            Assert.Equal(2, history.Count);              // forming bar excluded
            Assert.Equal(111, history[^1].Close);        // closed bar is last in its own history
        }

        [Fact]
        public async Task Intrabar_updates_do_not_trigger_evaluation()
        {
            var h = new EngineHarness();
            h.Feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0), Bar(1) }));

            Assert.True(h.Feed.ApplyLiveTick(Bar(1, close: 105))); // same period → LiveReplace
            await Task.Delay(150);

            h.Strategy.DidNotReceive().OnBar(Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>());
        }

        [Fact]
        public async Task Live_bar_close_signal_publishes_a_StrategySignalEvent()
        {
            var h = new EngineHarness();
            h.Feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0), Bar(1) }));
            var signal = new StrategySignal(OrderSide.Buy, OrderType.Market, null, null, null, null, "cycle low", 0.8);
            h.Strategy.OnBar(Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>())
                .Returns(signal);

            h.Feed.ApplyLiveTick(Bar(2));

            await WaitUntil(() => h.EventBus.ReceivedCalls().Any(c =>
                c.GetArguments().FirstOrDefault() is StrategySignalEvent));
            var evt = (StrategySignalEvent)h.EventBus.ReceivedCalls()
                .Select(c => c.GetArguments().FirstOrDefault())
                .First(a => a is StrategySignalEvent)!;
            Assert.Equal("TestStrategy", evt.StrategyName);
        }

        // ── Warm-feed instant tab switch ─────────────────────────────────────

        private sealed class CatchUpHarness
        {
            public readonly KeyedFeedsTests.FakeOrchestrator Orch = new();
            public readonly MarketFeedHub Hub;
            public readonly IDataService DataService;
            public readonly MultiSubProvider Provider;
            public readonly List<WorkspaceAction> Dispatched = new();
            public readonly DataManager Manager;

            public CatchUpHarness()
            {
                var provider = new MultiSubProvider();
                Provider = provider;
                DataService = Substitute.For<IDataService>();
                DataService.GetProviderAsync("MultiProv").Returns(Task.FromResult<IMarketDataProvider?>(provider));
                Hub = new MarketFeedHub(Orch, DataService, new DemoPolicy(false), NullLoggerFactory.Instance);
                var store = Substitute.For<IWorkspaceStore>();
                store.State.Returns(WorkspaceState.Initial);
                store.When(s => s.Dispatch(Arg.Any<WorkspaceAction>())).Do(ci =>
                {
                    lock (Dispatched) Dispatched.Add(ci.Arg<WorkspaceAction>());
                });
                Manager = new DataManager(Hub, store, Substitute.For<IEventBus>(),
                    NullLogger<DataManager>.Instance, Substitute.For<IServiceProvider>());
                Manager.Identity = Id();
            }
        }

        [Fact]
        public async Task Warm_idle_feed_binds_instantly_and_gap_fills()
        {
            var h = new CatchUpHarness();
            // Feed kept warm past the snapshot: same scrollback start, newer live edge.
            h.Hub.FocusedFeed!.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0), Bar(1), Bar(2) }));
            var snapshot = new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0), Bar(1) });
            lock (h.Dispatched) h.Dispatched.Clear();

            await h.Manager.CatchUpFromSnapshotAsync(snapshot);

            // Bound the warm 3-bar buffer, then gap-filled (idle feed) once.
            lock (h.Dispatched)
            {
                var first = (UpdateDataAction)h.Dispatched.First(a => a is UpdateDataAction);
                Assert.Equal(3, first.NewData.Count);
            }
            Assert.Single(h.Orch.FetchCalls);          // gap-fill only — no 200-bar reload
            Assert.Equal(3, h.Hub.FocusedFeed!.Bars.Count); // snapshot never clobbered the warm buffer
        }

        [Fact]
        public async Task Warm_live_feed_binds_instantly_and_still_gap_fills_the_handoff()
        {
            var h = new CatchUpHarness();
            h.Hub.FocusedFeed!.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0), Bar(1), Bar(2) }));
            Assert.Equal(FeedLiveStart.Started, await h.Hub.TryStartFeedLiveAsync(Id())); // live background sub
            var snapshot = new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0), Bar(1) });

            await h.Manager.CatchUpFromSnapshotAsync(snapshot);

            // Bind was instant (no snapshot restore), but the handoff still
            // gap-fills ONCE — a subscription handover can miss a bar close.
            Assert.Single(h.Orch.FetchCalls);
            Assert.Equal(3, h.Hub.FocusedFeed!.Bars.Count);
        }

        [Fact]
        public async Task Fresh_but_shallow_feed_never_replaces_deeper_snapshot_history()
        {
            var h = new CatchUpHarness();
            // A feed live-started from empty: fresh at the edge, no scrollback.
            h.Hub.FocusedFeed!.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(5), Bar(6) }));
            var snapshot = new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0), Bar(1), Bar(2), Bar(3) });

            await h.Manager.CatchUpFromSnapshotAsync(snapshot);

            // Snapshot path taken: scrollback restored, gap-fill fetched.
            Assert.Equal(Bar(0).Date, h.Hub.FocusedFeed!.Bars[0].Date);
            Assert.Single(h.Orch.FetchCalls);
        }

        // ── MarketFeeds hub fast path ────────────────────────────────────────

        [Fact]
        public async Task MarketFeeds_serves_live_hub_buffers_without_REST()
        {
            var (hub, hubDataService, _) = BuildHub();
            using (hub)
            {
                var identity = Id();
                Assert.Equal(FeedLiveStart.Started, await hub.TryStartFeedLiveAsync(identity));
                hub.GetOrCreateFeed(identity).RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0), Bar(1) }));

                var store = Substitute.For<IWorkspaceStore>();
                store.State.Returns(WorkspaceState.Initial); // focused identity is Empty ≠ ours
                var restService = Substitute.For<IDataService>();
                var feeds = new MarketFeeds(store, restService, hub);

                Assert.True(feeds.IsLive(identity));
                var bars = await feeds.GetBarsAsync(identity, 10);

                Assert.Equal(2, bars.Count);
                await restService.DidNotReceive().FetchOhlcvAsync(Arg.Any<string>(), Arg.Any<MarketDataRequest>());
            }
        }

        // ── BackgroundTabFeedService ─────────────────────────────────────────

        private static TabSnapshot Snap(int index, ChartIdentity identity, TimeSeriesBuffer<Ohlcv>? data = null)
        {
            var s = WorkspaceState.Initial;
            return new TabSnapshot(
                TabIndex: index, Identity: identity, Data: data ?? s.Data,
                ActiveSeries: s.ActiveSeries, FocusedSeriesIndex: s.FocusedSeriesIndex,
                FocusedSeriesId: s.FocusedSeriesId, FocusedComponentIndex: s.FocusedComponentIndex,
                FocusedBinIndex: s.FocusedBinIndex, CurrentDataIndex: s.CurrentDataIndex,
                ViewportStartIndex: s.ViewportStartIndex, ViewportLength: s.ViewportLength,
                RightMarginBars: s.RightMarginBars, ViewportRange: s.ViewportRange,
                PaneRanges: s.PaneRanges, IsHeikinAshi: false, IsLogScale: false,
                LastInteractionContext: s.LastInteractionContext, PaneHeightRatios: s.PaneHeightRatios,
                IndicatorPaneScrollIndex: 0, InitStatus: InitializationStatus.Ready,
                DataStatus: DataStatus.Ready, IsCoordinateEntryMode: false,
                PendingDrawingTool: null, CoordinateEntryAnchorCount: 0,
                CoordinateEntryAnchor1Index: -1, SymbolDisplayName: identity.Symbol);
        }

        private sealed class TabFeedHarness
        {
            public readonly MarketFeedHub Hub;
            public readonly IDataService DataService;
            public readonly MultiSubProvider Provider;
            public readonly IWorkspaceStore Store = Substitute.For<IWorkspaceStore>();
            public readonly BackgroundTabFeedService Service;
            private WorkspaceState _state = WorkspaceState.Initial;

            public TabFeedHarness(bool enabled = true)
            {
                (Hub, DataService, Provider) = BuildHub();
                Store.State.Returns(_ => _state);
                Store.StateStream.Returns(System.Reactive.Linq.Observable.Never<WorkspaceState>());
                var settings = Substitute.For<ISettingsManager>();
                settings.GetSetting(BackgroundTabFeedService.EnabledKey)
                    .ReturnsForAnyArgs(enabled ? JToken.FromObject(true) : null);
                Service = new BackgroundTabFeedService(Store, Substitute.For<IEventBus>(),
                    settings, Hub, NullLogger<BackgroundTabFeedService>.Instance);
            }

            public void SetTabs(ChartIdentity focused, params TabSnapshot[] background)
            {
                _state = WorkspaceState.Initial with
                {
                    Identity = focused,
                    TabSnapshots = background.ToImmutableList(),
                };
            }
        }

        [Fact]
        public async Task Background_tab_goes_live_warmed_from_its_snapshot()
        {
            var h = new TabFeedHarness();
            var background = Id(symbol: "ETH/USD");
            h.SetTabs(focused: Id(symbol: "BTC/USD"),
                Snap(1, background, new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0), Bar(1) })));

            h.Service.Reconcile();

            await WaitUntil(() => h.Hub.IsFeedLive(background));
            await WaitUntil(() => h.Hub.TryGetFeed(background)!.Bars.Count == 2); // snapshot warmed in
            Assert.Contains(background, h.Service.LiveBackgroundFeeds);
        }

        [Fact]
        public async Task Focused_tab_is_never_background_subscribed_and_disable_stops_feeds()
        {
            var h = new TabFeedHarness();
            var background = Id(symbol: "ETH/USD");
            h.SetTabs(Id(symbol: "BTC/USD"), Snap(1, background), Snap(2, Id(symbol: "BTC/USD")));

            h.Service.Reconcile();
            await WaitUntil(() => h.Hub.IsFeedLive(background));
            Assert.False(h.Hub.IsFeedLive(Id(symbol: "BTC/USD"))); // focused stays on the legacy path
            Assert.Single(h.Service.LiveBackgroundFeeds);

            h.SetTabs(Id(symbol: "BTC/USD")); // all background tabs gone
            h.Service.Reconcile();
            await WaitUntil(() => !h.Hub.IsFeedLive(background));
            Assert.Empty(h.Service.LiveBackgroundFeeds);
        }

        [Fact]
        public async Task NonMultiplex_provider_is_remembered_and_not_retried()
        {
            var h = new TabFeedHarness();
            var single = new ChartIdentity("Spot", "SingleProv", "AAPL", "1h");
            h.DataService.GetProviderAsync("SingleProv")
                .Returns(Task.FromResult<IMarketDataProvider?>(new KeyedFeedsPhaseBTestsSupport.MinimalProviderBase()));
            h.SetTabs(Id(symbol: "BTC/USD"), Snap(1, single));

            h.Service.Reconcile();
            await WaitUntil(() => h.Service.LiveBackgroundFeeds.Count == 0);
            await h.DataService.Received(1).GetProviderAsync("SingleProv");

            h.Service.Reconcile(); // second reconcile must not re-probe the provider
            await Task.Delay(100);
            await h.DataService.Received(1).GetProviderAsync("SingleProv");
        }

        [Fact]
        public async Task Cap_limits_live_background_feeds()
        {
            var h = new TabFeedHarness();
            var tabs = Enumerable.Range(0, 10)
                .Select(i => Snap(i + 1, Id(symbol: $"SYM{i}/USD")))
                .ToArray();
            h.SetTabs(Id(symbol: "BTC/USD"), tabs);

            h.Service.Reconcile();

            await WaitUntil(() => h.Service.LiveBackgroundFeeds.Count == BackgroundTabFeedService.MaxLiveBackgroundFeeds
                                  && h.Provider.SubscribeCalls == BackgroundTabFeedService.MaxLiveBackgroundFeeds);
        }
    }

    /// <summary>Shared fakes lifted for reuse across the phase test files.</summary>
    internal static class KeyedFeedsPhaseBTestsSupport
    {
        internal class MinimalProviderBase : BaseMarketDataProvider
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
    }
}
