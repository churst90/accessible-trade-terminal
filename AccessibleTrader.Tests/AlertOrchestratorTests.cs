using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Phase E test-debt: AlertOrchestrator had ZERO coverage. Pins the routing layer
    /// around AlertEvaluator: alert persistence via IWorkspaceLibraryService, the
    /// cold-start warm-up tick (seed previous values WITHOUT firing — the fix for
    /// false-positive crossovers where a missing previous key defaulted to 0.0),
    /// Ready-state gating, and AlertFiredEvent publication on the event bus.
    /// </summary>
    public class AlertOrchestratorTests
    {
        private static Ohlcv Bar(double close, int minute) => new(
            new DateTime(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc),
            close - 1, close + 1, close - 2, close, 1000);

        private static WorkspaceState ReadyState(int barCount, string symbol = "")
        {
            var bars = Enumerable.Range(0, barCount).Select(i => Bar(100 + i, i)).ToList();
            // Fresh buffer per call: the orchestrator's DistinctUntilChanged compares
            // Data by reference, so each "tick" needs a new buffer instance — exactly
            // what a real data update produces.
            return WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(bars),
                CurrentDataIndex = barCount - 1,
                InitStatus = InitializationStatus.Ready,
                SymbolDisplayName = symbol,
            };
        }

        private static AlertDefinition SymbolAlert(string id, string? symbol) => new()
        {
            Id = id,
            Name = $"Alert {id}",
            Target = AlertTarget.Price,
            Condition = AlertCondition.CrossesAbove,
            Threshold = 100,
            Delivery = AlertDelivery.Speech,
            Symbol = symbol,
        };

        private static AlertDefinition SomeAlert(string id = "a1") => new()
        {
            Id = id,
            Name = $"Alert {id}",
            Target = AlertTarget.Price,
            Condition = AlertCondition.CrossesAbove,
            Threshold = 100,
            Delivery = AlertDelivery.Speech,
        };

        private static (AlertOrchestrator orch, MockWorkspaceStore store, SpyEventBus bus,
                        IAlertEvaluator evaluator, IWorkspaceLibraryService library)
            Build(List<AlertDefinition>? persisted = null)
        {
            var store = new MockWorkspaceStore();
            var bus = new SpyEventBus();
            var evaluator = Substitute.For<IAlertEvaluator>();
            evaluator.EvaluateAlerts(
                    Arg.Any<IReadOnlyList<AlertDefinition>>(), Arg.Any<WorkspaceState>(),
                    Arg.Any<Ohlcv>(), Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyDictionary<string, double>>())
                .Returns(Enumerable.Empty<AlertFired>());
            var library = Substitute.For<IWorkspaceLibraryService>();
            library.LoadAlerts().Returns(persisted ?? new List<AlertDefinition>());

            var orch = new AlertOrchestrator(store, evaluator, bus, library,
                NullLogger<AlertOrchestrator>.Instance);
            return (orch, store, bus, evaluator, library);
        }

        [Fact]
        public void Constructor_RestoresPersistedAlerts_SoTheySurviveRestart()
        {
            var saved = new List<AlertDefinition> { SomeAlert("persisted") };
            var (orch, _, _, _, _) = Build(saved);

            Assert.Contains(orch.GetAlerts(), a => a.Id == "persisted");
        }

        [Fact]
        public void AddAlert_PersistsImmediately_AndAppearsInGetAlerts()
        {
            var (orch, _, _, _, library) = Build();

            orch.AddAlert(SomeAlert("new"));

            Assert.Contains(orch.GetAlerts(), a => a.Id == "new");
            library.Received(1).SaveAlerts(
                Arg.Is<IEnumerable<AlertDefinition>>(l => l.Any(a => a.Id == "new")));
        }

        [Fact]
        public void RemoveAlert_PersistsTheRemoval()
        {
            var (orch, _, _, _, library) = Build(new List<AlertDefinition> { SomeAlert("gone") });

            orch.RemoveAlert("gone");

            Assert.DoesNotContain(orch.GetAlerts(), a => a.Id == "gone");
            library.Received().SaveAlerts(
                Arg.Is<IEnumerable<AlertDefinition>>(l => !l.Any(a => a.Id == "gone")));
        }

        [Fact]
        public void FirstReadyTick_IsWarmupOnly_NoEvaluationAndNoEvents()
        {
            // Cold-start guard: the first Ready tick only seeds the previous-value
            // snapshot. Evaluating on it would compare against an EMPTY dict (missing
            // keys default to 0.0), making every "crosses above 0-ish threshold" alert
            // fire spuriously the moment a chart loads.
            var (orch, store, bus, evaluator, _) = Build(new List<AlertDefinition> { SomeAlert() });
            orch.Start();

            store.EmitState(ReadyState(barCount: 3));

            evaluator.DidNotReceiveWithAnyArgs().EvaluateAlerts(
                default!, default!, default, default, default!);
            Assert.Empty(bus.Log.OfType<AlertFiredEvent>());
        }

        [Fact]
        public void SecondReadyTick_Evaluates_AndPublishesOneEventPerFiredAlert()
        {
            var (orch, store, bus, evaluator, _) = Build(new List<AlertDefinition> { SomeAlert() });
            var fired = new AlertFired(SomeAlert(), 101, 99, "Alert a1: crossed above 100");
            evaluator.EvaluateAlerts(
                    Arg.Any<IReadOnlyList<AlertDefinition>>(), Arg.Any<WorkspaceState>(),
                    Arg.Any<Ohlcv>(), Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyDictionary<string, double>>())
                .Returns(new[] { fired });
            orch.Start();

            store.EmitState(ReadyState(barCount: 3)); // warm-up
            store.EmitState(ReadyState(barCount: 4)); // real evaluation

            var ev = Assert.Single(bus.Log.OfType<AlertFiredEvent>());
            Assert.Same(fired, ev.Alert);
        }

        [Fact]
        public void NonReadyStates_AreIgnored()
        {
            var (orch, store, bus, evaluator, _) = Build(new List<AlertDefinition> { SomeAlert() });
            orch.Start();

            store.EmitState(ReadyState(3) with { InitStatus = InitializationStatus.Loading });
            store.EmitState(ReadyState(3) with { InitStatus = InitializationStatus.Resetting });

            evaluator.DidNotReceiveWithAnyArgs().EvaluateAlerts(
                default!, default!, default, default, default!);
            Assert.Empty(bus.Log.OfType<AlertFiredEvent>());
        }

        [Fact]
        public void SingleBarTick_DoesNotConsumeTheWarmup()
        {
            // A cross needs two bars. A 1-bar tick returns before the warm-up flag is
            // set, so the warm-up is spent on the FIRST tick with >= 2 bars and real
            // evaluation only starts on the tick after that.
            var (orch, store, bus, evaluator, _) = Build(new List<AlertDefinition> { SomeAlert() });
            evaluator.EvaluateAlerts(
                    Arg.Any<IReadOnlyList<AlertDefinition>>(), Arg.Any<WorkspaceState>(),
                    Arg.Any<Ohlcv>(), Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyDictionary<string, double>>())
                .Returns(new[] { new AlertFired(SomeAlert(), 101, 99, "spoken") });
            orch.Start();

            store.EmitState(ReadyState(barCount: 1)); // too short — ignored entirely
            store.EmitState(ReadyState(barCount: 2)); // first evaluable tick = warm-up
            Assert.Empty(bus.Log.OfType<AlertFiredEvent>());

            store.EmitState(ReadyState(barCount: 3)); // now evaluation runs
            Assert.Single(bus.Log.OfType<AlertFiredEvent>());
        }

        [Fact]
        public void SymbolScopedAlert_IsSkipped_WhenChartSymbolDiffers()
        {
            // A "KAS/USD" alert must NOT be evaluated while the on-screen chart is BTC/USD
            // (the cross-contamination bug Part A fixes). The orchestrator filters the alert
            // out of the list it hands the evaluator.
            var (orch, store, _, evaluator, _) = Build(
                new List<AlertDefinition> { SymbolAlert("kas", "KAS/USD") });
            orch.Start();

            store.EmitState(ReadyState(3, symbol: "BTC/USD")); // warm-up
            store.EmitState(ReadyState(4, symbol: "BTC/USD")); // real evaluation

            evaluator.Received().EvaluateAlerts(
                Arg.Is<IReadOnlyList<AlertDefinition>>(l => l.Count == 0),
                Arg.Any<WorkspaceState>(), Arg.Any<Ohlcv>(), Arg.Any<Ohlcv>(),
                Arg.Any<IReadOnlyDictionary<string, double>>());
        }

        [Fact]
        public void SymbolScopedAlert_IsEvaluated_WhenChartSymbolMatches_CaseInsensitively()
        {
            var (orch, store, _, evaluator, _) = Build(
                new List<AlertDefinition> { SymbolAlert("btc", "btc/usd") });
            orch.Start();

            store.EmitState(ReadyState(3, symbol: "BTC/USD")); // warm-up
            store.EmitState(ReadyState(4, symbol: "BTC/USD")); // real evaluation

            evaluator.Received().EvaluateAlerts(
                Arg.Is<IReadOnlyList<AlertDefinition>>(l => l.Any(a => a.Id == "btc")),
                Arg.Any<WorkspaceState>(), Arg.Any<Ohlcv>(), Arg.Any<Ohlcv>(),
                Arg.Any<IReadOnlyDictionary<string, double>>());
        }

        [Fact]
        public void NullSymbolAlert_IsAlwaysEvaluated_ForAnyChart()
        {
            var (orch, store, _, evaluator, _) = Build(
                new List<AlertDefinition> { SymbolAlert("any", null) });
            orch.Start();

            store.EmitState(ReadyState(3, symbol: "KAS/USD")); // warm-up
            store.EmitState(ReadyState(4, symbol: "KAS/USD")); // real evaluation

            evaluator.Received().EvaluateAlerts(
                Arg.Is<IReadOnlyList<AlertDefinition>>(l => l.Any(a => a.Id == "any")),
                Arg.Any<WorkspaceState>(), Arg.Any<Ohlcv>(), Arg.Any<Ohlcv>(),
                Arg.Any<IReadOnlyDictionary<string, double>>());
        }

        [Fact]
        public void FiredEvent_IsStampedWithTheChartSymbol_WhenAlertSymbolWasNull()
        {
            // The evaluator returns a fired alert with a null Symbol (the simple case).
            // The orchestrator stamps the on-screen symbol so delivery/routing know the market.
            var (orch, store, bus, evaluator, _) = Build(new List<AlertDefinition> { SomeAlert() });
            var fired = new AlertFired(SomeAlert(), 101, 99, "spoken", Symbol: null);
            evaluator.EvaluateAlerts(
                    Arg.Any<IReadOnlyList<AlertDefinition>>(), Arg.Any<WorkspaceState>(),
                    Arg.Any<Ohlcv>(), Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyDictionary<string, double>>())
                .Returns(new[] { fired });
            orch.Start();

            store.EmitState(ReadyState(3, symbol: "ETH/USD")); // warm-up
            store.EmitState(ReadyState(4, symbol: "ETH/USD")); // evaluation

            var ev = Assert.Single(bus.Log.OfType<AlertFiredEvent>());
            Assert.Equal("ETH/USD", ev.Alert.Symbol);
            Assert.Equal("ETH/USD", ev.Symbol);
        }

        [Fact]
        public void Stop_UnsubscribesFromStateStream()
        {
            var (orch, store, bus, evaluator, _) = Build(new List<AlertDefinition> { SomeAlert() });
            orch.Start();
            store.EmitState(ReadyState(3)); // warm-up consumed

            orch.Stop();
            store.EmitState(ReadyState(4)); // would evaluate if still subscribed

            evaluator.DidNotReceiveWithAnyArgs().EvaluateAlerts(
                default!, default!, default, default, default!);
            Assert.Empty(bus.Log.OfType<AlertFiredEvent>());
        }
    }
}
