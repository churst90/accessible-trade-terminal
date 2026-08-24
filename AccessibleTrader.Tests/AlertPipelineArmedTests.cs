using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The alert pipeline is ARMED by application startup — not by a test.
    ///
    /// Every test in <see cref="AlertOrchestratorTests"/> calls <c>orch.Start()</c> itself,
    /// which is why all nine of them stayed green for the entire period during which
    /// <c>Start()</c> had no production caller at all and no in-session alert could ever
    /// fire. Those tests guard the orchestrator's behaviour *once armed*; nothing guarded
    /// that anything arms it.
    ///
    /// So these tests drive the real <see cref="AppStartupService"/> over a container and
    /// assert the observable effect — an <see cref="AlertFiredEvent"/> reaches the bus —
    /// without ever touching <c>Start()</c>. Deleting the <c>Start()</c> call from
    /// <c>AppStartupService</c> must turn <see cref="Startup_ArmsInSessionAlerts_SoAnAlertFiresWithoutAnyoneCallingStart"/>
    /// red; that is the whole point of the file.
    /// </summary>
    public class AlertPipelineArmedTests
    {
        private static Ohlcv Bar(double close, int minute) => new(
            new DateTime(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc),
            close - 1, close + 1, close - 2, close, 1000);

        private static WorkspaceState ReadyState(int barCount) =>
            WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(
                    Enumerable.Range(0, barCount).Select(i => Bar(100 + i, i)).ToList()),
                CurrentDataIndex = barCount - 1,
                InitStatus = InitializationStatus.Ready,
                SymbolDisplayName = "BTC/USD",
            };

        private static AlertDefinition CrossesAbove(double threshold) => new()
        {
            Id = "armed-1",
            Name = "Armed alert",
            Target = AlertTarget.Price,
            Condition = AlertCondition.CrossesAbove,
            Threshold = threshold,
            Delivery = AlertDelivery.Speech,
        };

        /// <summary>
        /// Everything <see cref="AppStartupService.InitializeAsync"/> resolves with
        /// <c>GetRequiredService</c>, substituted. The optional (<c>GetService</c>) ones are
        /// deliberately left unregistered so this harness exercises the null-tolerant paths
        /// too — if startup ever starts *requiring* one of them, this goes red and the new
        /// hard dependency has to be a deliberate decision rather than a surprise.
        /// </summary>
        private static (AppStartupService startup, MockWorkspaceStore store, SpyEventBus bus)
            BuildStartupOver(IAlertEvaluator evaluator, List<AlertDefinition> persisted)
        {
            var store = new MockWorkspaceStore();
            var bus = new SpyEventBus();

            var library = Substitute.For<IWorkspaceLibraryService>();
            library.LoadAlerts().Returns(persisted);

            var services = new ServiceCollection();
            services.AddSingleton<IDataService>(Substitute.For<IDataService>());
            services.AddSingleton<IPluginLoaderService>(Substitute.For<IPluginLoaderService>());
            services.AddSingleton<IIndicatorService>(Substitute.For<IIndicatorService>());
            services.AddSingleton<IDataOrchestrationService>(Substitute.For<IDataOrchestrationService>());
            services.AddSingleton<IInputRouter>(Substitute.For<IInputRouter>());
            services.AddSingleton<IChartCommandManager>(Substitute.For<IChartCommandManager>());
            services.AddSingleton<IHistoryBufferCoordinator>(Substitute.For<IHistoryBufferCoordinator>());
            services.AddSingleton<IAccessibilityFeedbackCoordinator>(Substitute.For<IAccessibilityFeedbackCoordinator>());
            services.AddSingleton<IWorkspaceInitializer>(Substitute.For<IWorkspaceInitializer>());
            services.AddSingleton<IEventBus>(bus);

            // The REAL orchestrator over the REAL store — this is the object under test.
            services.AddSingleton<IWorkspaceStore>(store);
            services.AddSingleton<IAlertEvaluator>(evaluator);
            services.AddSingleton<IWorkspaceLibraryService>(library);
            services.AddSingleton<IAlertOrchestrator>(sp => new AlertOrchestrator(
                store, evaluator, bus, library, NullLogger<AlertOrchestrator>.Instance));

            var provider = services.BuildServiceProvider();
            var startup = new AppStartupService(provider, NullLogger<AppStartupService>.Instance);
            return (startup, store, bus);
        }

        [Fact]
        public async Task Startup_ArmsInSessionAlerts_SoAnAlertFiresWithoutAnyoneCallingStart()
        {
            var alert = CrossesAbove(100);
            var fired = new AlertFired(alert, 101, 99, "Armed alert: crossed above 100");

            var evaluator = Substitute.For<IAlertEvaluator>();
            evaluator.EvaluateAlerts(
                    Arg.Any<IReadOnlyList<AlertDefinition>>(), Arg.Any<WorkspaceState>(),
                    Arg.Any<Ohlcv>(), Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyDictionary<string, double>>())
                .Returns(new[] { fired });

            var (startup, store, bus) = BuildStartupOver(evaluator, new List<AlertDefinition> { alert });

            await startup.InitializeAsync();

            // First Ready tick is the orchestrator's warm-up (seeds previous values, fires
            // nothing). The second is a real evaluation. Both arrive only because startup
            // subscribed — nothing in this test calls Start().
            store.EmitState(ReadyState(barCount: 3));
            store.EmitState(ReadyState(barCount: 4));

            var events = bus.Log.OfType<AlertFiredEvent>().ToList();
            Assert.True(events.Count == 1,
                $"Expected exactly one AlertFiredEvent from the composed startup graph, got {events.Count}. " +
                "If this is 0, nothing armed the in-session alert pipeline and a price alert set " +
                "while watching a chart cannot fire.");
            Assert.Equal("armed-1", events[0].Alert.Definition.Id);
            // The orchestrator stamps the firing chart's symbol onto an "any"-scoped alert.
            Assert.Equal("BTC/USD", events[0].Alert.Symbol);
        }

        [Fact]
        public async Task StartupTwice_DoesNotDoubleFire_BecauseStartIsIdempotent()
        {
            // AppStartupService is reachable twice on the MAUI head (MainPage at launch,
            // MainLayout on first render). Its own Task guard makes the body run once, but
            // Start() must also be safe on its own: a second subscription would leak the
            // first AND deliver every alert twice — a duplicate email, Telegram and push
            // per crossing.
            var alert = CrossesAbove(100);
            var evaluator = Substitute.For<IAlertEvaluator>();
            evaluator.EvaluateAlerts(
                    Arg.Any<IReadOnlyList<AlertDefinition>>(), Arg.Any<WorkspaceState>(),
                    Arg.Any<Ohlcv>(), Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyDictionary<string, double>>())
                .Returns(new[] { new AlertFired(alert, 101, 99, "Armed alert: crossed above 100") });

            var (startup, store, bus) = BuildStartupOver(evaluator, new List<AlertDefinition> { alert });

            await startup.InitializeAsync();
            await startup.InitializeAsync();

            store.EmitState(ReadyState(barCount: 3));
            store.EmitState(ReadyState(barCount: 4));

            Assert.Single(bus.Log.OfType<AlertFiredEvent>());
        }

        [Fact]
        public async Task StartupWithNoAlertOrchestratorRegistered_DoesNotThrow()
        {
            // The resolve is optional by design: heads that do not register the
            // orchestrator (and the StrategyLab/ScriptWorker composition roots) must still
            // boot. Guards the `?.` rather than assuming it.
            var services = new ServiceCollection();
            services.AddSingleton<IDataService>(Substitute.For<IDataService>());
            services.AddSingleton<IPluginLoaderService>(Substitute.For<IPluginLoaderService>());
            services.AddSingleton<IIndicatorService>(Substitute.For<IIndicatorService>());
            services.AddSingleton<IDataOrchestrationService>(Substitute.For<IDataOrchestrationService>());
            services.AddSingleton<IInputRouter>(Substitute.For<IInputRouter>());
            services.AddSingleton<IChartCommandManager>(Substitute.For<IChartCommandManager>());
            services.AddSingleton<IHistoryBufferCoordinator>(Substitute.For<IHistoryBufferCoordinator>());
            services.AddSingleton<IAccessibilityFeedbackCoordinator>(Substitute.For<IAccessibilityFeedbackCoordinator>());
            services.AddSingleton<IWorkspaceInitializer>(Substitute.For<IWorkspaceInitializer>());

            var startup = new AppStartupService(
                services.BuildServiceProvider(), NullLogger<AppStartupService>.Instance);

            await startup.InitializeAsync();
        }
    }
}
