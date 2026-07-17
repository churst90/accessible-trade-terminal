using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Alerts Part D: condition-tree alerts. Pins the evaluation contract (tree
    /// replaces the simple rule; edge-triggered with RepeatIfStillActive/Cooldown),
    /// and the persistence bridge (the tree's System.Text.Json polymorphism must
    /// round-trip through the Newtonsoft alerts.json path).
    /// </summary>
    public class TreeAlertTests
    {
        private static readonly ConditionNode SampleTree = new ConditionGroup(
            "g1", LogicOperator.And,
            new List<ConditionNode>
            {
                new ConditionLeaf("l1", "RSI.Rsi", LeafOperator.LessThan, 30),
                new ConditionLeaf("l2", "EMA.Ema", LeafOperator.GreaterThan, 0, Timeframe: "1d"),
            });

        private static AlertDefinition TreeAlert(bool repeat = false, TimeSpan? cooldown = null) => new()
        {
            Id = "tree-1",
            Name = "Oversold in uptrend",
            Target = AlertTarget.Indicator,
            Condition = AlertCondition.CrossesAbove, // placeholder — tree replaces it
            Delivery = AlertDelivery.Both,
            ConditionTree = SampleTree,
            RepeatIfStillActive = repeat,
            Cooldown = cooldown ?? TimeSpan.Zero,
        };

        private static WorkspaceState State() => WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(
                new Ohlcv(DateTime.UtcNow.AddMinutes(-1), 100, 101, 99, 100, 1000),
                new Ohlcv(DateTime.UtcNow, 100, 102, 99, 101, 1000)),
            CurrentDataIndex = 1,
        };

        private static (AlertEvaluator evaluator, IConditionEvaluator trees) Build(params bool[] treeResults)
        {
            var trees = Substitute.For<IConditionEvaluator>();
            var queue = new Queue<bool>(treeResults);
            trees.Evaluate(Arg.Any<ConditionNode>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>())
                .Returns(_ =>
                {
                    bool r = queue.Count > 0 ? queue.Dequeue() : treeResults.LastOrDefault();
                    return new ConditionEvaluation(r, new Dictionary<string, bool>(), r ? 2 : 0, 2);
                });
            var evaluator = new AlertEvaluator(
                Substitute.For<ISdkCandlePatternAnalyzer>(),
                Substitute.For<IIndicatorContextAnalyzer>(),
                conditionEvaluator: trees);
            return (evaluator, trees);
        }

        private static IEnumerable<AlertFired> Eval(AlertEvaluator e, AlertDefinition a, WorkspaceState s) =>
            e.EvaluateAlerts(new[] { a }, s, s.Data[^1], s.Data[^2], new Dictionary<string, double>());

        [Fact]
        public void TreeAlert_FiresOnRisingEdge_Once()
        {
            var (evaluator, _) = Build(false, true, true);
            var alert = TreeAlert();
            var state = State();

            Assert.Empty(Eval(evaluator, alert, state));          // false → nothing
            var fired = Assert.Single(Eval(evaluator, alert, state)); // false→true edge → fires
            Assert.Contains("conditions met", fired.SpeechText);
            Assert.Empty(Eval(evaluator, alert, state));          // still true → no refire
        }

        [Fact]
        public void TreeAlert_ReArmsAfterGoingFalse()
        {
            var (evaluator, _) = Build(true, false, true);
            var alert = TreeAlert();
            var state = State();

            Assert.Single(Eval(evaluator, alert, state)); // true (first sight = edge)
            Assert.Empty(Eval(evaluator, alert, state));  // false → re-arm
            Assert.Single(Eval(evaluator, alert, state)); // true again → fires again
        }

        [Fact]
        public void TreeAlert_RepeatIfStillActive_HonoursCooldown()
        {
            var (evaluator, _) = Build(true, true, true);
            var alert = TreeAlert(repeat: true, cooldown: TimeSpan.Zero);
            var state = State();

            Assert.Single(Eval(evaluator, alert, state)); // edge
            Assert.Single(Eval(evaluator, alert, state)); // repeat allowed, zero cooldown
            var longCooldown = TreeAlert(repeat: true, cooldown: TimeSpan.FromHours(1)) ;
            var (e2, _) = Build(true, true);
            Assert.Single(Eval(e2, longCooldown, state)); // edge
            Assert.Empty(Eval(e2, longCooldown, state));  // repeat blocked by cooldown
        }

        [Fact]
        public void TreeAlert_ScoreTree_SpeaksTheScore()
        {
            var trees = Substitute.For<IConditionEvaluator>();
            trees.Evaluate(Arg.Any<ConditionNode>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>())
                .Returns(new ConditionEvaluation(true, new Dictionary<string, bool>(), 7, 9));
            var evaluator = new AlertEvaluator(
                Substitute.For<ISdkCandlePatternAnalyzer>(),
                Substitute.For<IIndicatorContextAnalyzer>(),
                conditionEvaluator: trees);

            var fired = Assert.Single(Eval(evaluator, TreeAlert(), State()));
            Assert.Contains("score 7 of 9", fired.SpeechText);
            Assert.Equal(7, fired.TriggeringValue);
        }

        [Fact]
        public void TreeAlert_NoEvaluatorInjected_NeverFires_NeverThrows()
        {
            var evaluator = new AlertEvaluator(
                Substitute.For<ISdkCandlePatternAnalyzer>(),
                Substitute.For<IIndicatorContextAnalyzer>());
            Assert.Empty(Eval(evaluator, TreeAlert(), State()));
        }

        [Fact]
        public void SimpleAlerts_NeverTouchTheTreeEvaluator()
        {
            var (evaluator, trees) = Build(true);
            var simple = new AlertDefinition
            {
                Id = "s1", Name = "Simple", Target = AlertTarget.Price,
                Condition = AlertCondition.CrossesAbove, Threshold = 100.5,
                Delivery = AlertDelivery.Both,
            };
            Eval(evaluator, simple, State());
            trees.DidNotReceive().Evaluate(Arg.Any<ConditionNode>(),
                Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>());
        }

        // ── Persistence: the Newtonsoft ↔ System.Text.Json bridge ────────────

        [Fact]
        public void TreeAlert_RoundTripsThroughNewtonsoftWithBridge()
        {
            var settings = new JsonSerializerSettings
            {
                Converters = { new ConditionNodeNewtonsoftBridge() },
            };
            var alerts = new List<AlertDefinition> { TreeAlert() };

            string json = JsonConvert.SerializeObject(alerts, Formatting.Indented, settings);
            Assert.Contains("$kind", json); // STJ polymorphism discriminator survives

            var back = JsonConvert.DeserializeObject<List<AlertDefinition>>(json, settings)!;
            var tree = Assert.IsType<ConditionGroup>(back[0].ConditionTree);
            Assert.Equal(LogicOperator.And, tree.Logic);
            Assert.Equal(2, tree.Children.Count);
            var leaf = Assert.IsType<ConditionLeaf>(tree.Children[0]);
            Assert.Equal("RSI.Rsi", leaf.SignalDescriptorId);
            Assert.Equal(LeafOperator.LessThan, leaf.Operator);
            Assert.Equal(30, leaf.Value);
            var htfLeaf = Assert.IsType<ConditionLeaf>(tree.Children[1]);
            Assert.Equal("1d", htfLeaf.Timeframe);
        }

        [Fact]
        public void SimpleAlert_WithoutTree_RoundTripsUnchanged()
        {
            var settings = new JsonSerializerSettings
            {
                Converters = { new ConditionNodeNewtonsoftBridge() },
            };
            var simple = new AlertDefinition
            {
                Id = "s1", Name = "Simple", Target = AlertTarget.Price,
                Condition = AlertCondition.CrossesAbove, Threshold = 50000,
                Delivery = AlertDelivery.Both, Symbol = "BTC/USD",
            };
            string json = JsonConvert.SerializeObject(new[] { simple }, settings);
            var back = JsonConvert.DeserializeObject<List<AlertDefinition>>(json, settings)!;
            Assert.Null(back[0].ConditionTree);
            Assert.Equal(50000, back[0].Threshold);
            Assert.Equal("BTC/USD", back[0].Symbol);
        }
    }
}
