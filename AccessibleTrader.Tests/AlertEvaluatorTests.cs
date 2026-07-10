using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Phase E test-debt: AlertEvaluator previously had ZERO coverage even though it is
    /// the sole decision point for whether a user hears an alert fire. These tests pin
    /// the price-cross semantics (strict previous-side + inclusive touch), the implicit
    /// hysteresis (no re-fire while price holds beyond the threshold), the IsActive
    /// gate, multi-alert evaluation on a single bar, and the per-alert exception
    /// isolation that keeps one broken rule from silencing all others.
    /// </summary>
    public class AlertEvaluatorTests
    {
        private static AlertEvaluator BuildEvaluator(
            ISdkCandlePatternAnalyzer? pattern = null,
            IIndicatorContextAnalyzer? context = null)
        {
            return new AlertEvaluator(
                pattern ?? Substitute.For<ISdkCandlePatternAnalyzer>(),
                context ?? Substitute.For<IIndicatorContextAnalyzer>());
        }

        /// <summary>Bar whose High/Low always bracket Open/Close so shapes stay plausible.</summary>
        private static Ohlcv Bar(double open, double close) => new(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            open,
            Math.Max(open, close) + 1,
            Math.Min(open, close) - 1,
            close,
            1000);

        private static AlertDefinition PriceCross(
            AlertCondition condition, double threshold, string id = "a1", bool isActive = true)
            => new()
            {
                Id = id,
                Name = $"Alert {id}",
                Target = AlertTarget.Price,
                Condition = condition,
                Threshold = threshold,
                Delivery = AlertDelivery.Speech,
                IsActive = isActive,
            };

        private static readonly IReadOnlyDictionary<string, double> NoPrev =
            new Dictionary<string, double>();

        [Fact]
        public void CrossesAbove_Fires_WhenCloseCrossesThresholdUpward()
        {
            var evaluator = BuildEvaluator();
            var alert = PriceCross(AlertCondition.CrossesAbove, 100);

            var fired = evaluator.EvaluateAlerts(
                new[] { alert }, WorkspaceState.Initial,
                newBar: Bar(99, 101), previousBar: Bar(98, 99), NoPrev).ToList();

            var f = Assert.Single(fired);
            Assert.Equal(101, f.TriggeringValue);
            Assert.Equal(99, f.PreviousValue);
            Assert.Contains("crossed above", f.SpeechText);
        }

        [Fact]
        public void CrossesBelow_Fires_WhenCloseCrossesThresholdDownward()
        {
            var evaluator = BuildEvaluator();
            var alert = PriceCross(AlertCondition.CrossesBelow, 100);

            var fired = evaluator.EvaluateAlerts(
                new[] { alert }, WorkspaceState.Initial,
                newBar: Bar(101, 99), previousBar: Bar(102, 101), NoPrev).ToList();

            var f = Assert.Single(fired);
            Assert.Equal(99, f.TriggeringValue);
            Assert.Contains("crossed below", f.SpeechText);
        }

        [Fact]
        public void CrossesAbove_DoesNotRefire_WhileHoldingAboveThreshold()
        {
            // Hysteresis is implicit in the cross condition: prev must be STRICTLY below
            // the threshold. Once price has crossed and stays above, every subsequent bar
            // has prev >= threshold and the alert stays quiet until price reverses below
            // and crosses again. This is the "no alarm spam while price hovers" guarantee.
            var evaluator = BuildEvaluator();
            var alert = PriceCross(AlertCondition.CrossesAbove, 100);

            // Bar 1: 99 -> 101, crosses. Fires.
            var first = evaluator.EvaluateAlerts(
                new[] { alert }, WorkspaceState.Initial, Bar(99, 101), Bar(98, 99), NoPrev);
            Assert.Single(first);

            // Bar 2: 101 -> 102, still above. Must NOT fire again.
            var second = evaluator.EvaluateAlerts(
                new[] { alert }, WorkspaceState.Initial, Bar(101, 102), Bar(99, 101), NoPrev);
            Assert.Empty(second);

            // Bar 3: reversal below (102 -> 98). CrossesAbove still quiet.
            var third = evaluator.EvaluateAlerts(
                new[] { alert }, WorkspaceState.Initial, Bar(102, 98), Bar(101, 102), NoPrev);
            Assert.Empty(third);

            // Bar 4: re-cross (98 -> 103). Fires again after the reversal.
            var fourth = evaluator.EvaluateAlerts(
                new[] { alert }, WorkspaceState.Initial, Bar(98, 103), Bar(102, 98), NoPrev);
            Assert.Single(fourth);
        }

        [Fact]
        public void CrossesAbove_BoundarySemantics_TouchFires_ButRestingAtThresholdDoesNot()
        {
            var evaluator = BuildEvaluator();
            var alert = PriceCross(AlertCondition.CrossesAbove, 100);

            // Exact touch counts as a cross (current >= threshold is inclusive).
            var touch = evaluator.EvaluateAlerts(
                new[] { alert }, WorkspaceState.Initial, Bar(99, 100), Bar(98, 99), NoPrev);
            Assert.Single(touch);

            // But a previous close sitting exactly AT the threshold is not "below" it
            // (prev < threshold is strict), so moving up from the threshold is not a
            // cross — the touch bar above already consumed it.
            var fromThreshold = evaluator.EvaluateAlerts(
                new[] { alert }, WorkspaceState.Initial, Bar(100, 105), Bar(99, 100), NoPrev);
            Assert.Empty(fromThreshold);
        }

        [Fact]
        public void DisabledAlert_NeverFires_EvenWhenConditionIsMet()
        {
            var evaluator = BuildEvaluator();
            var alert = PriceCross(AlertCondition.CrossesAbove, 100, isActive: false);

            var fired = evaluator.EvaluateAlerts(
                new[] { alert }, WorkspaceState.Initial, Bar(99, 101), Bar(98, 99), NoPrev);

            Assert.Empty(fired);
        }

        [Fact]
        public void MultipleAlerts_OnOneBar_AllFireIndependently()
        {
            // A single bar can satisfy several alerts at once (e.g. a green reversal bar
            // that also breaks a level). All matching alerts must be reported — a blind
            // user relies on hearing each configured alert, not just the first hit.
            var evaluator = BuildEvaluator();
            var crossAlert = PriceCross(AlertCondition.CrossesAbove, 100, id: "cross");
            var directionAlert = new AlertDefinition
            {
                Id = "dir",
                Name = "Direction flip",
                Target = AlertTarget.Candle,
                Condition = AlertCondition.ChangesDirection,
                Delivery = AlertDelivery.Speech,
            };

            // Previous bar bearish (100 -> 99), new bar bullish (99 -> 101) crossing 100.
            var fired = evaluator.EvaluateAlerts(
                new[] { crossAlert, directionAlert }, WorkspaceState.Initial,
                Bar(99, 101), Bar(100, 99), NoPrev).ToList();

            Assert.Equal(2, fired.Count);
            Assert.Contains(fired, f => f.Definition.Id == "cross");
            Assert.Contains(fired, f => f.Definition.Id == "dir");
        }

        [Fact]
        public void ChangesDirection_FiresOnlyOnPolarityFlip()
        {
            var evaluator = BuildEvaluator();
            var alert = new AlertDefinition
            {
                Id = "dir",
                Name = "Direction",
                Target = AlertTarget.Candle,
                Condition = AlertCondition.ChangesDirection,
                Delivery = AlertDelivery.Both,
            };

            // Bullish after bullish: no flip.
            Assert.Empty(evaluator.EvaluateAlerts(
                new[] { alert }, WorkspaceState.Initial, Bar(100, 102), Bar(99, 100), NoPrev));

            // Bearish after bullish: flip fires.
            Assert.Single(evaluator.EvaluateAlerts(
                new[] { alert }, WorkspaceState.Initial, Bar(102, 100), Bar(99, 100), NoPrev));
        }

        [Fact]
        public void ThrowingAlertRule_IsIsolated_OtherAlertsStillFire()
        {
            // A broken rule (e.g. pattern analyzer failing on unusual data) must not
            // take the whole evaluation pass down with it — the per-alert try/catch is
            // what keeps the user's remaining alerts alive.
            var pattern = Substitute.For<ISdkCandlePatternAnalyzer>();
            pattern.Analyze(Arg.Any<Ohlcv>(), Arg.Any<Ohlcv?>(), Arg.Any<Ohlcv?>())
                .Returns(_ => throw new InvalidOperationException("boom"));
            var evaluator = BuildEvaluator(pattern);

            var broken = new AlertDefinition
            {
                Id = "broken",
                Name = "Pattern",
                Target = AlertTarget.Candle,
                Condition = AlertCondition.PatternDetected,
                Pattern = CandlePattern.BullishEngulfing,
                Delivery = AlertDelivery.Speech,
            };
            var healthy = PriceCross(AlertCondition.CrossesAbove, 100, id: "healthy");

            var fired = evaluator.EvaluateAlerts(
                new[] { broken, healthy }, WorkspaceState.Initial,
                Bar(99, 101), Bar(98, 99), NoPrev).ToList();

            var f = Assert.Single(fired);
            Assert.Equal("healthy", f.Definition.Id);
        }
    }
}
