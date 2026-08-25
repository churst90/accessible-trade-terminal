using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// A tree alert that cannot answer one of its leaves must SAY SO.
    ///
    /// <c>ConditionEvaluator.LastDegradation</c> is set when an HTF leaf has no pre-warmed
    /// data or the causality contract refuses a component. Its own doc comment said "UI
    /// layers read this to say which it was" — no production code read it at all, and it
    /// was not even on <c>IConditionEvaluator</c>, which is the type the alerts path holds,
    /// so the alerts path could not have read it without a cast.
    ///
    /// The consequence was the worst shape a failure takes in this product: the tree
    /// evaluated false forever, which is byte-identical to "the market never met your
    /// condition", so the user believed an alert was watching when it structurally could
    /// not fire. These tests pin the announcement, its once-per-alert gate, and the re-arm
    /// on edit.
    /// </summary>
    public class AlertDegradationSurfacedTests
    {
        /// <summary>Evaluator that always reports a degraded leaf and never evaluates true.</summary>
        private sealed class DegradedEvaluator : IConditionEvaluator
        {
            public string? LastDegradation { get; private set; }
            public string Reason = "Leaf 'leafA' needs 1h data that is not loaded";

            public ConditionEvaluation Evaluate(ConditionNode root, IReadOnlyList<Ohlcv> history, WorkspaceState state)
            {
                LastDegradation = Reason;
                return new ConditionEvaluation(false, new Dictionary<string, bool>(), 0, 0);
            }
        }

        /// <summary>Evaluator that answers cleanly — nothing should be announced.</summary>
        private sealed class HealthyEvaluator : IConditionEvaluator
        {
            public string? LastDegradation => null;
            public ConditionEvaluation Evaluate(ConditionNode root, IReadOnlyList<Ohlcv> history, WorkspaceState state)
                => new(false, new Dictionary<string, bool>(), 0, 0);
        }

        private static Ohlcv Bar(double close, int minute) => new(
            new DateTime(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc),
            close - 1, close + 1, close - 2, close, 1000);

        private static WorkspaceState State(int bars = 3) => WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(
                Enumerable.Range(0, bars).Select(i => Bar(100 + i, i)).ToList()),
            CurrentDataIndex = bars - 1,
            InitStatus = InitializationStatus.Ready,
        };

        private static AlertDefinition TreeAlert(string id = "tree-1") => new()
        {
            Id = id,
            Name = "Tree alert",
            Target = AlertTarget.Indicator,
            Condition = AlertCondition.CrossesAbove, // placeholder — the tree replaces it
            Delivery = AlertDelivery.Speech,
            ConditionTree = new ConditionLeaf(
                Id: "leafA",
                SignalDescriptorId: "TEST.Value",
                Operator: LeafOperator.GreaterThan,
                Value: 0,
                Timeframe: "1h"),
        };

        private static (AlertEvaluator eval, AlertOrchestrator orch, SpyEventBus bus, MockWorkspaceStore store)
            Build(IConditionEvaluator conditionEvaluator, AlertDefinition alert)
        {
            var evaluator = new AlertEvaluator(
                Substitute.For<ISdkCandlePatternAnalyzer>(),
                Substitute.For<IIndicatorContextAnalyzer>(),
                levels: null,
                conditionEvaluator: conditionEvaluator);

            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            var library = Substitute.For<IWorkspaceLibraryService>();
            library.LoadAlerts().Returns(new List<AlertDefinition> { alert });

            var orch = new AlertOrchestrator(store, evaluator, bus, library,
                NullLogger<AlertOrchestrator>.Instance);

            return (evaluator, orch, bus, store);
        }

        private static List<string> ErrorMessages(SpyEventBus bus) =>
            bus.Log.OfType<FeedbackRequestEvent>()
               .Where(e => e.Type == FeedbackType.Error)
               .Select(e => e.Message ?? "")
               .ToList();

        [Fact]
        public void ADegradedTreeAlert_IsAnnounced_RatherThanStayingSilentlyFalse()
        {
            var alert = TreeAlert();
            var (eval, _, bus, _) = Build(new DegradedEvaluator(), alert);

            eval.EvaluateAlerts(new[] { alert }, State(), Bar(101, 3), Bar(100, 2),
                new Dictionary<string, double>());

            var errors = ErrorMessages(bus);
            Assert.True(errors.Count == 1,
                $"Expected one spoken error naming the degraded alert, got {errors.Count}. " +
                "A tree that cannot answer a leaf is indistinguishable from a market that did " +
                "not trigger, so the user believes the alert is armed when it cannot fire.");
            Assert.Contains("Tree alert", errors[0], StringComparison.Ordinal);
            Assert.Contains("leafA", errors[0], StringComparison.Ordinal);
        }

        [Fact]
        public void TheDegradationIsAnnouncedOncePerAlert_NotOnEveryTick()
        {
            var alert = TreeAlert();
            var (eval, _, bus, _) = Build(new DegradedEvaluator(), alert);

            for (int i = 0; i < 5; i++)
                eval.EvaluateAlerts(new[] { alert }, State(), Bar(101, 3), Bar(100, 2),
                    new Dictionary<string, double>());

            Assert.Single(ErrorMessages(bus));
        }

        [Fact]
        public void EditingTheAlert_ReArmsTheAnnouncement()
        {
            // A user who edits a broken alert must be told if it is STILL broken — otherwise
            // the once-per-alert gate turns into permanent silence after the first report.
            var alert = TreeAlert();
            var (eval, orch, bus, _) = Build(new DegradedEvaluator(), alert);

            eval.EvaluateAlerts(new[] { alert }, State(), Bar(101, 3), Bar(100, 2),
                new Dictionary<string, double>());
            Assert.Single(ErrorMessages(bus));

            orch.RemoveAlert(alert.Id);
            orch.AddAlert(alert);

            eval.EvaluateAlerts(new[] { alert }, State(), Bar(101, 3), Bar(100, 2),
                new Dictionary<string, double>());

            Assert.Equal(2, ErrorMessages(bus).Count);
        }

        [Fact]
        public void AHealthyTreeThatSimplyDoesNotTrigger_AnnouncesNothing()
        {
            // The other half of the contract: "false" must stay quiet. If this ever goes red
            // the terminal has started nagging about every unmet condition, which trains the
            // user to ignore the channel that carries real failures.
            var alert = TreeAlert();
            var (eval, _, bus, _) = Build(new HealthyEvaluator(), alert);

            eval.EvaluateAlerts(new[] { alert }, State(), Bar(101, 3), Bar(100, 2),
                new Dictionary<string, double>());

            Assert.Empty(ErrorMessages(bus));
        }
    }
}
