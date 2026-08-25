using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// A chart that could not load must not look like a chart that did.
    ///
    /// <para>
    /// The defect: <c>LoadChartAsync</c> dispatched <c>SetIdentityAction</c> — whose reducer was
    /// <c>state with { Identity = a.Identity }</c> and did NOT touch <c>Data</c> — then awaited
    /// the refresh, then dispatched <c>Ready</c>. A refresh that came back empty returned early
    /// without dispatching anything at all, so the title, the toolbar and <c>State.Identity</c>
    /// all said ETH/USD while <c>State.Data</c> was still BTC/USD's two hundred bars, the status
    /// said Ready, and a blind user heard nothing whatsoever. Empty is not rare: an open circuit
    /// returns empty deliberately, and so does a 200 OK for a delisted ticker, a symbol outside
    /// the plan, or the wrong market.
    /// </para>
    ///
    /// <para>
    /// It is not only a display bug. <c>PaperTradingProvider.OnState</c> prices positions and
    /// fills resting orders from exactly the <c>(Identity, last bar of Data)</c> pair, so a
    /// resting ETH order could fill against a BTC price.
    /// </para>
    ///
    /// <para>
    /// Three things have to hold, and each is a separate test below: the data cannot outlive the
    /// identity it belongs to; the empty result has to SAY so; and the status has to stop
    /// claiming Ready over an empty chart.
    /// </para>
    /// </summary>
    public class EmptyLoadHonestyTests
    {
        private static Ohlcv Bar(int i, double close) =>
            new(new DateTime(2026, 1, 1).AddMinutes(i), close, close + 1, close - 1, close, 100);

        private static ChartIdentity Id(string symbol, string provider = "binance") =>
            new() { Provider = provider, Symbol = symbol, Timeframe = "1h", Market = "Crypto" };

        private static WorkspaceStore NewStore() =>
            new(new EventBus(), new ViewportRangeCalculator(), new ViewportNavigationService(),
                new VolumeStateService());

        // ── 1. Data cannot outlive the identity it belongs to ────────────────

        [Fact]
        public void SettingANewIdentity_DropsThePreviousSymbolsBars()
        {
            var store = NewStore();
            store.Dispatch(new SetIdentityAction(Id("BTC/USDT")));
            store.Dispatch(new UpdateDataAction(
                new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, 200).Select(i => Bar(i, 60000 + i)).ToArray()),
                IsInitialLoad: true));
            Assert.Equal(200, store.State.Data.Count);

            store.Dispatch(new SetIdentityAction(Id("ETH/USD")));

            Assert.Equal("ETH/USD", store.State.Identity.Symbol);
            Assert.Equal(0, store.State.Data.Count);
            // The cursor and viewport pointed into bars that no longer exist.
            Assert.Equal(0, store.State.CurrentDataIndex);
            Assert.Equal(0, store.State.ViewportStartIndex);
        }

        [Fact]
        public void AFreshLoadStillLandsItsBars()
        {
            // The vacuity half: a reducer that cleared Data and nothing refilled it would satisfy
            // the test above and leave every chart blank.
            var store = NewStore();
            store.Dispatch(new SetIdentityAction(Id("ETH/USD")));
            store.Dispatch(new UpdateDataAction(
                new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, 50).Select(i => Bar(i, 2000 + i)).ToArray()),
                IsInitialLoad: true));

            Assert.Equal(50, store.State.Data.Count);
            Assert.Equal("ETH/USD", store.State.Identity.Symbol);
        }

        // ── 2. The empty result has to say so ────────────────────────────────

        [Fact]
        public async Task AProviderThatReturnsNoBars_IsAnnouncedByNameAndVenue()
        {
            var bus = new SpyEventBus();
            var store = NewStore();
            var hub = new MarketFeedHub(EmptyOrchestrator(), Substitute.For<IDataService>(),
                new DemoPolicy(isDemo: false), NullLoggerFactory.Instance);
            var manager = new DataManager(hub, store, bus,
                NullLogger<DataManager>.Instance, Substitute.For<IServiceProvider>());

            manager.Identity = Id("GHOST/USD", provider: "binance");
            await manager.RefreshDataAsync();

            var spoken = bus.Log.OfType<FeedbackRequestEvent>().ToList();
            Assert.True(spoken.Count > 0,
                "An empty load said nothing at all — indistinguishable from a chart that loaded.");
            Assert.Contains(spoken, e => e.Message != null
                                      && e.Message.Contains("GHOST/USD")
                                      && e.Message.Contains("binance"));
        }

        [Fact]
        public async Task ALoadThatReturnsBars_SaysNothingAboutBeingEmpty()
        {
            // The negative half. A service that announced a failure on every load would pass the
            // test above and cry wolf on every chart the user opens.
            var bus = new SpyEventBus();
            var store = NewStore();
            var hub = new MarketFeedHub(
                OrchestratorReturning(Enumerable.Range(0, 5).Select(i => Bar(i, 100 + i)).ToList()),
                Substitute.For<IDataService>(), new DemoPolicy(isDemo: false), NullLoggerFactory.Instance);
            var manager = new DataManager(hub, store, bus,
                NullLogger<DataManager>.Instance, Substitute.For<IServiceProvider>());

            manager.Identity = Id("BTC/USDT");
            await manager.RefreshDataAsync();

            Assert.DoesNotContain(bus.Log.OfType<FeedbackRequestEvent>(),
                e => e.Message != null && e.Message.Contains("No data for"));
        }

        // ── 3. Ready is a claim about the chart, not about the call returning ─

        [Fact]
        public async Task AChartThatLoadedNothing_IsNotReported_Ready()
        {
            var bus = new EventBus();
            var store = new WorkspaceStore(bus, new ViewportRangeCalculator(),
                new ViewportNavigationService(), new VolumeStateService());

            // A data manager that returns normally and leaves the store empty — which is exactly
            // what every silent empty-result door does.
            var dataManager = Substitute.For<IDataManager>();
            var orch = new MarketOrchestrator(
                Substitute.For<IDataService>(), dataManager, store,
                Substitute.For<IWorkspaceInitializer>(), bus, new DemoPolicy(isDemo: false))
            {
                SelectedProvider = "binance",
                SelectedSymbol = "GHOST/USD",
                SelectedTimeframe = "1h",
            };

            await orch.LoadChartAsync();

            Assert.Equal(0, store.State.Data.Count);
            Assert.NotEqual(InitializationStatus.Ready, store.State.InitStatus);
            Assert.Equal(InitializationStatus.Error, store.State.InitStatus);
        }

        [Fact]
        public async Task AChartThatDidLoad_IsStillReported_Ready()
        {
            var bus = new EventBus();
            var store = new WorkspaceStore(bus, new ViewportRangeCalculator(),
                new ViewportNavigationService(), new VolumeStateService());

            var dataManager = Substitute.For<IDataManager>();
            dataManager.RefreshDataAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                store.Dispatch(new UpdateDataAction(
                    new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, 10).Select(i => Bar(i, 100 + i)).ToArray()),
                    IsInitialLoad: true));
                return Task.CompletedTask;
            });

            var orch = new MarketOrchestrator(
                Substitute.For<IDataService>(), dataManager, store,
                Substitute.For<IWorkspaceInitializer>(), bus, new DemoPolicy(isDemo: false))
            {
                SelectedProvider = "binance",
                SelectedSymbol = "BTC/USDT",
                SelectedTimeframe = "1h",
            };

            await orch.LoadChartAsync();

            Assert.Equal(10, store.State.Data.Count);
            Assert.Equal(InitializationStatus.Ready, store.State.InitStatus);
        }

        // ── 4. An empty chart must still be navigable without dying ──────────

        [Fact]
        public void ArrowKeysOnAnEmptyChart_RefuseInsteadOfThrowing()
        {
            // Clearing Data on identity change makes "a chart with no bars" an ordinary state
            // rather than a startup-only one, and the first thing anyone does with a chart is
            // press an arrow key. Math.Clamp(i, 0, Count - 1) throws when Count is 0.
            var empty = WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(),
                CurrentDataIndex = 0,
            };

            Assert.False(new PointNavigationStrategy().NavigateX(empty, +1).Success);
            Assert.False(new PointNavigationStrategy().NavigateX(empty, -1).Success);
            Assert.False(new BinnedNavigationStrategy().NavigateX(empty, +1).Success);
        }

        // ── Stubs ────────────────────────────────────────────────────────────

        private static IDataOrchestrator EmptyOrchestrator() => OrchestratorReturning(new List<Ohlcv>());

        private static IDataOrchestrator OrchestratorReturning(List<Ohlcv> bars)
        {
            var orch = Substitute.For<IDataOrchestrator>();
            orch.FetchOhlcvAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<int?>(), Arg.Any<long?>(), Arg.Any<bool>())
                .Returns(Task.FromResult(bars));
            return orch;
        }
    }
}
