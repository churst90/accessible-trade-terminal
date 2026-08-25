using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Multi-workspace background monitoring: a non-focused tab's alerts and
    /// symbol-bound strategies keep evaluating on a polling loop, announced with a
    /// symbol prefix — while the focused chart's pipeline is never touched. These
    /// tests pin the scoping contracts that make "exactly one driver at a time"
    /// true for every alert and strategy.
    /// </summary>
    public class BackgroundMonitoringTests
    {
        private static List<Ohlcv> Bars(int n, double start = 100)
            => Enumerable.Range(0, n)
                .Select(i => new Ohlcv(
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
                    start + i, start + i + 1, start + i - 1, start + i, 1000))
                .ToList();

        private static AlertDefinition Alert(string id, string? symbol) => new()
        {
            Id = id,
            Name = $"Alert {id}",
            Target = AlertTarget.Price,
            Condition = AlertCondition.CrossesAbove,
            Threshold = 100,
            Delivery = AlertDelivery.Both,
            Symbol = symbol,
        };

        private sealed record Harness(
            BackgroundWorkspaceMonitor Monitor,
            SpyEventBus Bus,
            IAlertEvaluator Evaluator,
            IAlertOrchestrator Alerts,
            IStrategyEngine Engine,
            IMarketFeeds Data);

        private static Harness Build(string symbol = "KAS/USDT", List<Ohlcv>? bars = null)
        {
            var bus = new SpyEventBus();
            var data = Substitute.For<IMarketFeeds>();
            data.GetBarsAsync(Arg.Any<ChartIdentity>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns((IReadOnlyList<Ohlcv>)(bars ?? Bars(60)));
            var indicators = Substitute.For<IIndicatorService>();
            var alerts = Substitute.For<IAlertOrchestrator>();
            alerts.GetAlerts().Returns(new List<AlertDefinition>());
            var evaluator = Substitute.For<IAlertEvaluator>();
            evaluator.EvaluateAlerts(
                    Arg.Any<IReadOnlyList<AlertDefinition>>(), Arg.Any<WorkspaceState>(),
                    Arg.Any<Ohlcv>(), Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyDictionary<string, double>>())
                .Returns(Enumerable.Empty<AlertFired>());
            var engine = Substitute.For<IStrategyEngine>();
            engine.ActiveStrategies.Returns(new List<ActiveStrategy>());

            var monitor = new BackgroundWorkspaceMonitor(
                new ChartIdentity("Spot", "Bitstamp", symbol, "1d"),
                symbol,
                ImmutableList<ChartSeries>.Empty,
                data, indicators, alerts, evaluator, engine, bus,
                NullLogger.Instance, TimeSpan.FromHours(1));

            return new Harness(monitor, bus, evaluator, alerts, engine, data);
        }

        // ── Alert scoping + announcement ─────────────────────────────────────

        [Fact]
        public async Task Monitor_FiresScopedAlert_WithSymbolPrefixedSpeech()
        {
            var h = Build();
            h.Alerts.GetAlerts().Returns(new List<AlertDefinition> { Alert("a1", "KAS/USDT") });
            h.Evaluator.EvaluateAlerts(
                    Arg.Any<IReadOnlyList<AlertDefinition>>(), Arg.Any<WorkspaceState>(),
                    Arg.Any<Ohlcv>(), Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyDictionary<string, double>>())
                .Returns(new[] { new AlertFired(Alert("a1", "KAS/USDT"), 101, 99, "crossed above 100.") });

            await h.Monitor.EvaluateOnceAsync(CancellationToken.None); // warm-up pass — no fire
            Assert.Empty(h.Bus.Log.OfType<AlertFiredEvent>());

            await h.Monitor.EvaluateOnceAsync(CancellationToken.None); // armed pass — fires

            var fired = Assert.Single(h.Bus.Log.OfType<AlertFiredEvent>());
            Assert.Equal("KAS/USDT", fired.Alert.Symbol);
            Assert.StartsWith("KAS/USDT: ", fired.Alert.SpeechText);
        }

        [Fact]
        public async Task Monitor_OnlyEvaluatesAlertsBoundToItsSymbol()
        {
            // Null-symbol ("any") alerts belong to the FOCUSED pipeline — a monitor
            // evaluating them too would double-fire every generic alert.
            var h = Build();
            h.Alerts.GetAlerts().Returns(new List<AlertDefinition>
            {
                Alert("mine", "KAS/USDT"),
                Alert("other", "BTC/USDT"),
                Alert("any", null),
            });

            IReadOnlyList<AlertDefinition>? seen = null;
            h.Evaluator.EvaluateAlerts(
                    Arg.Do<IReadOnlyList<AlertDefinition>>(a => seen = a), Arg.Any<WorkspaceState>(),
                    Arg.Any<Ohlcv>(), Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyDictionary<string, double>>())
                .Returns(Enumerable.Empty<AlertFired>());

            await h.Monitor.EvaluateOnceAsync(CancellationToken.None); // warm-up
            await h.Monitor.EvaluateOnceAsync(CancellationToken.None);

            Assert.NotNull(seen);
            var ids = seen!.Select(a => a.Id).ToList();
            Assert.Equal(new[] { "mine" }, ids);
        }

        // ── Strategy routing ─────────────────────────────────────────────────

        [Fact]
        public async Task Monitor_EvaluatesOnlyItsSymbolBoundStrategies_AnnounceOnly()
        {
            var h = Build();
            var mine = Substitute.For<ITradingStrategy>();
            mine.Name.Returns("Mine");
            mine.OnBar(Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>())
                .Returns(new StrategySignal(OrderSide.Buy, OrderType.Market, null, null, null, null, "go long", 1.0));
            var other = Substitute.For<ITradingStrategy>();
            other.Name.Returns("Other");

            h.Engine.ActiveStrategies.Returns(new List<ActiveStrategy>
            {
                new("i1", mine,  new Dictionary<string, object>(), StrategyExecutionMode.Auto,       false, "KAS/USDT"),
                new("i2", other, new Dictionary<string, object>(), StrategyExecutionMode.Suggestion, false, "BTC/USDT"),
                new("i3", other, new Dictionary<string, object>(), StrategyExecutionMode.Suggestion, false, null),
            });

            await h.Monitor.EvaluateOnceAsync(CancellationToken.None);

            // Only the KAS-bound strategy evaluated; legacy (null) and BTC ones skipped.
            mine.Received(1).OnBar(Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>());
            other.DidNotReceive().OnBar(Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>());

            // Announce-only contract: the signal is published even for an Auto-mode
            // strategy, but background monitors never execute orders.
            var sig = Assert.Single(h.Bus.Log.OfType<StrategySignalEvent>());
            Assert.Equal("Mine", sig.StrategyName);
        }

        [Fact]
        public async Task Monitor_BuildsEvaluationState_WithItsOwnIdentity()
        {
            var h = Build();
            WorkspaceState? seen = null;
            h.Alerts.GetAlerts().Returns(new List<AlertDefinition> { Alert("a1", "KAS/USDT") });
            h.Evaluator.EvaluateAlerts(
                    Arg.Any<IReadOnlyList<AlertDefinition>>(), Arg.Do<WorkspaceState>(st => seen = st),
                    Arg.Any<Ohlcv>(), Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyDictionary<string, double>>())
                .Returns(Enumerable.Empty<AlertFired>());

            await h.Monitor.EvaluateOnceAsync(CancellationToken.None);
            await h.Monitor.EvaluateOnceAsync(CancellationToken.None);

            Assert.NotNull(seen);
            Assert.Equal("KAS/USDT", seen!.Identity.Symbol);
            Assert.Equal("KAS/USDT", seen.SymbolDisplayName);
            Assert.Equal(60, seen.Data.Count);
            Assert.Equal(59, seen.CurrentDataIndex);
            Assert.Equal(InitializationStatus.Ready, seen.InitStatus);
        }

        // ── Foreground engine skip (the other half of "one driver at a time") ─

        [Fact]
        public void StrategyEngine_SkipsSymbolBoundStrategy_WhenAnotherChartIsFocused()
        {
            var bus = new SpyEventBus();
            var store = Substitute.For<IWorkspaceStore>();
            var bars = Bars(30);
            var kasState = WorkspaceState.Initial with
            {
                SymbolDisplayName = "KAS/USDT",
                Data = new TimeSeriesBuffer<Ohlcv>(bars),
                CurrentDataIndex = bars.Count - 1,
            };
            store.State.Returns(kasState);

            var dataManager = Substitute.For<IDataManager>();
            var engine = new StrategyEngine(
                bus,
                Substitute.For<IOrderExecutionService>(),
                Substitute.For<AccessibleTrader.Sdk.Logging.IAppLogger>(),
                NullLogger<StrategyEngine>.Instance,
                dataManager,
                store,
                Substitute.For<IStrategyIndicatorCache>());

            var strategy = Substitute.For<ITradingStrategy>();
            strategy.Name.Returns("KAS strat");
            strategy.OnBar(Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>())
                .Returns(new StrategySignal(OrderSide.Buy, OrderType.Market, null, null, null, null, "go", 1.0));

            // Started while KAS is focused → bound to KAS.
            engine.AddStrategy(strategy, mode: StrategyExecutionMode.Suggestion);
            Assert.Equal("KAS/USDT", Assert.Single(engine.ActiveStrategies).Symbol);

            // Focus moves to BTC → the KAS-bound strategy must NOT evaluate here
            // (a background monitor owns it now).
            store.State.Returns(kasState with { SymbolDisplayName = "BTC/USDT" });
            dataManager.DataUpdated += Raise.Event<Action>();
            strategy.DidNotReceive().OnBar(Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>());

            // Focus returns to KAS → it evaluates again.
            store.State.Returns(kasState);
            dataManager.DataUpdated += Raise.Event<Action>();
            strategy.Received(1).OnBar(Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>());
        }

        // ── Registry: policy gating + reconcile from tab snapshots ───────────

        private static TabSnapshot Snap(int index, string symbol)
        {
            var s = WorkspaceState.Initial;
            return new TabSnapshot(
                TabIndex: index,
                Identity: new ChartIdentity("Spot", "Bitstamp", symbol, "1d"),
                Data: s.Data,
                ActiveSeries: s.ActiveSeries,
                FocusedSeriesIndex: s.FocusedSeriesIndex,
                FocusedSeriesId: s.FocusedSeriesId,
                FocusedComponentIndex: s.FocusedComponentIndex,
                FocusedBinIndex: s.FocusedBinIndex,
                CurrentDataIndex: s.CurrentDataIndex,
                ViewportStartIndex: s.ViewportStartIndex,
                ViewportLength: s.ViewportLength,
                RightMarginBars: s.RightMarginBars,
                ViewportRange: s.ViewportRange,
                PaneRanges: s.PaneRanges,
                IsHeikinAshi: false,
                IsLogScale: false,
                LastInteractionContext: s.LastInteractionContext,
                PaneHeightRatios: s.PaneHeightRatios,
                IndicatorPaneScrollIndex: 0,
                InitStatus: InitializationStatus.Ready,
                DataStatus: DataStatus.Ready,
                IsCoordinateEntryMode: false,
                PendingDrawingTool: null,
                CoordinateEntryAnchorCount: 0,
                CoordinateEntryAnchor1Index: -1,
                SymbolDisplayName: symbol);
        }

        private static BackgroundMonitoringService BuildService(
            HostMode mode, bool settingOn, params TabSnapshot[] inactiveTabs)
        {
            var store = Substitute.For<IWorkspaceStore>();
            store.StateStream.Returns(System.Reactive.Linq.Observable.Never<WorkspaceState>());
            store.State.Returns(WorkspaceState.Initial with
            {
                TabSnapshots = inactiveTabs.ToImmutableList(),
            });

            var settings = Substitute.For<ISettingsManager>();
            settings.GetSetting(BackgroundMonitoringService.EnabledKey)
                .Returns(Newtonsoft.Json.Linq.JToken.FromObject(settingOn));
            settings.GetSetting(BackgroundMonitoringService.PollSecondsKey)
                .Returns(Newtonsoft.Json.Linq.JToken.FromObject(3600));

            var engine = Substitute.For<IStrategyEngine>();
            engine.ActiveStrategies.Returns(new List<ActiveStrategy>());

            return new BackgroundMonitoringService(
                store, new SpyEventBus(), settings, new DemoPolicy(mode),
                Substitute.For<IMarketFeeds>(), Substitute.For<IIndicatorService>(),
                Substitute.For<IAlertOrchestrator>(), Substitute.For<IAlertEvaluator>(),
                engine, NullLogger<BackgroundMonitoringService>.Instance);
        }

        [Theory]
        [InlineData(HostMode.Hosted)]
        [InlineData(HostMode.Demo)]
        public void Service_NeverMonitors_OnServerHostedBuilds(HostMode mode)
        {
            using var svc = BuildService(mode, settingOn: true, Snap(1, "BTC/USD"));
            svc.Reconcile();
            Assert.False(svc.IsEnabled);
            Assert.Empty(svc.Monitors);
        }

        [Fact]
        public void Service_IsOptIn_SettingOffMeansNoMonitors()
        {
            using var svc = BuildService(HostMode.Full, settingOn: false, Snap(1, "BTC/USD"));
            svc.Reconcile();
            Assert.False(svc.IsEnabled);
            Assert.Empty(svc.Monitors);
        }

        [Fact]
        public void Service_Reconcile_StartsOneMonitorPerInactiveSymbolTab()
        {
            using var svc = BuildService(HostMode.Full, settingOn: true,
                Snap(1, "BTC/USD"),
                Snap(2, "ETH/USD"),
                Snap(3, "ETH/USD"),   // duplicate identity coalesces into one monitor
                Snap(4, ""));         // blank tab — nothing to monitor
            svc.Reconcile();

            Assert.True(svc.IsEnabled);
            var symbols = svc.Monitors.Select(m => m.SymbolDisplayName).OrderBy(s => s).ToList();
            Assert.Equal(new[] { "BTC/USD", "ETH/USD" }, symbols);
        }

        [Fact]
        public void Service_Reconcile_StopsMonitorsForClosedTabs()
        {
            var store = Substitute.For<IWorkspaceStore>();
            store.StateStream.Returns(System.Reactive.Linq.Observable.Never<WorkspaceState>());
            store.State.Returns(WorkspaceState.Initial with
            {
                TabSnapshots = ImmutableList.Create(Snap(1, "BTC/USD"), Snap(2, "ETH/USD")),
            });
            var settings = Substitute.For<ISettingsManager>();
            settings.GetSetting(BackgroundMonitoringService.EnabledKey)
                .Returns(Newtonsoft.Json.Linq.JToken.FromObject(true));
            settings.GetSetting(BackgroundMonitoringService.PollSecondsKey)
                .Returns(Newtonsoft.Json.Linq.JToken.FromObject(3600));
            var engine = Substitute.For<IStrategyEngine>();
            engine.ActiveStrategies.Returns(new List<ActiveStrategy>());

            using var svc = new BackgroundMonitoringService(
                store, new SpyEventBus(), settings, new DemoPolicy(HostMode.Full),
                Substitute.For<IMarketFeeds>(), Substitute.For<IIndicatorService>(),
                Substitute.For<IAlertOrchestrator>(), Substitute.For<IAlertEvaluator>(),
                engine, NullLogger<BackgroundMonitoringService>.Instance);

            svc.Reconcile();
            Assert.Equal(2, svc.Monitors.Count);

            // ETH tab closes (or becomes the focused tab — same shape: it leaves TabSnapshots).
            store.State.Returns(WorkspaceState.Initial with
            {
                TabSnapshots = ImmutableList.Create(Snap(1, "BTC/USD")),
            });
            svc.Reconcile();

            var remaining = Assert.Single(svc.Monitors);
            Assert.Equal("BTC/USD", remaining.SymbolDisplayName);
        }

        // ── Setup speech prefix ──────────────────────────────────────────────

        [Fact]
        public void SetupSonifier_PrefixesSpeech_WithEventSymbol()
        {
            var bus = new SpyEventBus();
            var speech = Substitute.For<ISpeechManager>();
            using var sonifier = new Core.Services.Strategies.SetupSonifier(
                bus, Substitute.For<Core.Services.Accessibility.IEarconService>(), speech);

            var plan = new ResolvedRiskPlan(100, 95, new List<double> { 110 },
                new List<double> { 1.0 }, 1, 2, 50, "");
            bus.Publish(new SetupArmedEvent("S", "i1", OrderSide.Buy, "Waiting.", plan, Symbol: "KAS/USDT"));
            bus.Publish(new SetupConfirmedEvent("S", "i1", OrderSide.Buy, "Long setup...", plan, Symbol: "GOLD"));

            speech.Received().Speak(Arg.Is<string>(s => s.StartsWith("KAS/USDT: ")), Arg.Any<bool>());
            speech.Received().Speak(Arg.Is<string>(s => s.StartsWith("GOLD: ")), Arg.Any<bool>());
        }
    }
}
