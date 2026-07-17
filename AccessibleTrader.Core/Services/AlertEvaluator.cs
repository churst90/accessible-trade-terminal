using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Core.Services.Strategies;

namespace AccessibleTrader.Core.Services
{
    public class AlertEvaluator : IAlertEvaluator
    {
        private readonly ISdkCandlePatternAnalyzer _patternAnalyzer;
        private readonly IIndicatorContextAnalyzer _contextAnalyzer;
        private readonly ILevelService? _levels;
        // Tracks the trend direction seen on the previous bar per alert+series pair,
        // so EvaluateTrendChange detects actual direction flips rather than any non-flat trend.
        private readonly Dictionary<string, TrendDirection> _previousTrends =
            new(StringComparer.OrdinalIgnoreCase);

        // Part D: strategy condition evaluator for advanced (tree) alerts.
        // Optional so minimal test constructions keep working; when null,
        // tree alerts simply never fire (and log once via the try/catch above).
        private readonly Strategies.IConditionEvaluator? _conditionEvaluator;

        // Edge-trigger memory per tree alert: was the tree true on the last
        // evaluation, and when did it last fire? Concurrent because the focused
        // pipeline and a background monitor can hand an alert off between them
        // (only one drives it at a time, but the handoff can overlap a tick).
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (bool WasTrue, DateTime LastFiredUtc)>
            _treeState = new(StringComparer.OrdinalIgnoreCase);

        public AlertEvaluator(
            ISdkCandlePatternAnalyzer patternAnalyzer,
            IIndicatorContextAnalyzer contextAnalyzer,
            ILevelService? levels = null,
            Strategies.IConditionEvaluator? conditionEvaluator = null)
        {
            _patternAnalyzer = patternAnalyzer;
            _contextAnalyzer = contextAnalyzer;
            _levels          = levels;
            _conditionEvaluator = conditionEvaluator;
        }

        public IEnumerable<AlertFired> EvaluateAlerts(
            IReadOnlyList<AlertDefinition> alerts,
            WorkspaceState state,
            Ohlcv newBar,
            Ohlcv previousBar,
            IReadOnlyDictionary<string, double> previousValues)
        {
            var results = new List<AlertFired>();

            foreach (var alert in alerts)
            {
                if (!alert.IsActive) continue;

                try
                {
                    var fired = TryEvaluate(alert, state, newBar, previousBar, previousValues);
                    if (fired != null) results.Add(fired);
                }
                catch (Exception ex)
                {
                    // A broken alert rule (e.g. missing indicator component, divide-by-zero on
                    // a new data shape) used to silently stop firing, with no way for the user
                    // to find out. Debug-log the failure keyed on the alert id so a targeted
                    // repro can find the rule without spamming speech.
                    System.Diagnostics.Debug.WriteLine(
                        $"[AlertEvaluator] Alert '{alert.Id}' evaluation failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            return results;
        }

        /// <summary>
        /// Part D: evaluates an advanced-condition alert through the strategy tree
        /// evaluator. Edge-triggered: fires on the bar where the whole tree FIRST
        /// evaluates true; while it stays true, RepeatIfStillActive + Cooldown govern
        /// re-fires; when it goes false the trigger re-arms. (Tree alerts are the
        /// first alerts to actually honour RepeatIfStillActive/Cooldown — the simple
        /// rules rely on crossing semantics for natural edge behaviour.)
        /// </summary>
        private AlertFired? TryEvaluateTree(AlertDefinition alert, WorkspaceState state)
        {
            if (_conditionEvaluator == null || state.Data == null || state.Data.Count == 0)
                return null;

            var eval = _conditionEvaluator.Evaluate(alert.ConditionTree!, state.Data, state);

            var prev = _treeState.TryGetValue(alert.Id, out var st)
                ? st : (WasTrue: false, LastFiredUtc: DateTime.MinValue);

            bool fire = eval.OverallTrue
                && (!prev.WasTrue
                    || (alert.RepeatIfStillActive && DateTime.UtcNow - prev.LastFiredUtc >= alert.Cooldown));

            _treeState[alert.Id] = (eval.OverallTrue, fire ? DateTime.UtcNow : prev.LastFiredUtc);
            if (!fire) return null;

            // Score-threshold trees speak their score ("7 of 9"); plain logic trees
            // just announce the conditions.
            string speech = eval.MaxScore > 0 && Math.Abs(eval.MaxScore - eval.Score) > 1e-9
                ? $"{alert.Name}: conditions met, score {eval.Score:0.#} of {eval.MaxScore:0.#}."
                : $"{alert.Name}: conditions met.";

            return new AlertFired(alert, eval.Score, null, speech);
        }

        private AlertFired? TryEvaluate(
            AlertDefinition alert,
            WorkspaceState state,
            Ohlcv newBar,
            Ohlcv previousBar,
            IReadOnlyDictionary<string, double> previousValues)
        {
            // Part D: an advanced condition tree REPLACES the simple rule entirely.
            if (alert.ConditionTree != null)
                return TryEvaluateTree(alert, state);

            // Resolve current and previous values depending on target
            double currentValue;
            double prevValue;

            if (alert.Target == AlertTarget.Price)
            {
                currentValue = newBar.Close;
                prevValue    = previousBar.Close;
            }
            else if (alert.Target == AlertTarget.Indicator && alert.IndicatorCode != null && alert.ComponentName != null)
            {
                string key = $"{alert.IndicatorCode}.{alert.ComponentName}";
                var series = state.ActiveSeries.FirstOrDefault(s =>
                    s.IndicatorCode.Equals(alert.IndicatorCode, StringComparison.OrdinalIgnoreCase));
                var comp = series?.Components.FirstOrDefault(c =>
                    c.Name.Equals(alert.ComponentName, StringComparison.OrdinalIgnoreCase));

                int idx = state.CurrentDataIndex;
                if (series == null || comp == null || idx < 0 || idx >= series.GetComponentData(comp.Name).Length) return null;
                currentValue = series.GetComponentData(comp.Name)[idx];
                prevValue    = previousValues.TryGetValue(key, out var pv) ? pv : double.NaN;
                if (double.IsNaN(currentValue)) return null;
            }
            else if (alert.Target == AlertTarget.Candle)
            {
                currentValue = newBar.Close;
                prevValue    = previousBar.Close;
            }
            else if (alert.Target == AlertTarget.Poc)
            {
                // POC-crossing detection. Volume-profile POC is stable across adjacent bars so
                // compare the two consecutive closes against the CURRENT POC — a true cross
                // requires the prior close to have been on the opposite side. When no profile
                // is on the chart (ILevelService unregistered or no POC kind emitted) the alert
                // is a no-op rather than firing spuriously.
                double poc = ResolveNearestPoc(state, newBar.Close);
                if (double.IsNaN(poc)) return null;
                currentValue = newBar.Close;
                prevValue    = previousBar.Close;
                // Force the threshold on the alert to the resolved POC so CrossesAbove /
                // CrossesBelow evaluate against it even when the user configured a stale
                // threshold — POC is a moving target, not a fixed user input.
                alert = alert with { Threshold = poc };
            }
            else return null;

            bool triggered = alert.Condition switch
            {
                AlertCondition.CrossesAbove   => !double.IsNaN(prevValue) && prevValue < (alert.Threshold ?? 0) && currentValue >= (alert.Threshold ?? 0),
                AlertCondition.CrossesBelow   => !double.IsNaN(prevValue) && prevValue > (alert.Threshold ?? 0) && currentValue <= (alert.Threshold ?? 0),
                AlertCondition.PatternDetected => EvaluatePattern(alert, newBar, previousBar, state),
                AlertCondition.ChangesDirection => EvaluateDirectionChange(newBar, previousBar),
                AlertCondition.TrendChange    => EvaluateTrendChange(alert, state, newBar),
                AlertCondition.EntersZone     => EvaluateZone(alert, state, currentValue, prevValue, entering: true),
                AlertCondition.ExitsZone      => EvaluateZone(alert, state, currentValue, prevValue, entering: false),
                _                             => false
            };

            if (!triggered) return null;

            string speechText = $"{alert.Name}: {DescribeCondition(alert, currentValue)}. Current value {currentValue:F6}";
            return new AlertFired(alert, currentValue, double.IsNaN(prevValue) ? null : prevValue, speechText);
        }

        private bool EvaluatePattern(AlertDefinition alert, Ohlcv current, Ohlcv previous, WorkspaceState state)
        {
            if (alert.Pattern == null) return false;
            var analysis = _patternAnalyzer.Analyze(current, previous, null);
            return analysis.Pattern == alert.Pattern;
        }

        private static bool EvaluateDirectionChange(Ohlcv current, Ohlcv previous)
        {
            bool curBull  = current.Close >= current.Open;
            bool prevBull = previous.Close >= previous.Open;
            return curBull != prevBull;
        }

        private bool EvaluateTrendChange(AlertDefinition alert, WorkspaceState state, Ohlcv newBar)
        {
            if (alert.IndicatorCode == null) return false;
            var series = state.ActiveSeries.FirstOrDefault(s =>
                s.IndicatorCode.Equals(alert.IndicatorCode, StringComparison.OrdinalIgnoreCase));
            if (series == null) return false;
            var ctx = _contextAnalyzer.Analyze(series, state);
            if (ctx == null) return false;

            // Only fire when trend direction actually changes (not simply "is non-flat").
            string key = $"{alert.Id}|{series.Id}";
            _previousTrends.TryGetValue(key, out var prevTrend);
            _previousTrends[key] = ctx.Trend;
            return ctx.Trend != TrendDirection.Flat && ctx.Trend != prevTrend;
        }

        private bool EvaluateZone(AlertDefinition alert, WorkspaceState state, double current, double prev, bool entering)
        {
            if (alert.IndicatorCode == null) return false;
            var series = state.ActiveSeries.FirstOrDefault(s =>
                s.IndicatorCode.Equals(alert.IndicatorCode, StringComparison.OrdinalIgnoreCase));
            if (series == null) return false;

            var curCtx = _contextAnalyzer.Analyze(series, state);
            if (curCtx == null) return false;

            // Simplified: if current is in zone and prev was not (or vice versa)
            bool inZone = alert.Zone switch
            {
                AlertZone.Overbought => curCtx.Zone == ZoneStatus.Overbought,
                AlertZone.Oversold   => curCtx.Zone == ZoneStatus.Oversold,
                AlertZone.UpperBand  => curCtx.Zone == ZoneStatus.AtUpperBand,
                AlertZone.LowerBand  => curCtx.Zone == ZoneStatus.AtLowerBand,
                _                    => false
            };

            return entering ? inZone : !inZone;
        }

        /// <summary>
        /// Resolve the nearest POC price from any registered volume-profile provider. Returns
        /// NaN when ILevelService is not wired or when no POC-kind level is emitted. The
        /// "nearest" selection uses absolute distance from the current close — a chart may
        /// have multiple POCs (e.g. VPVR + TPO) and the one closest to current price is the
        /// relevant cross target.
        /// </summary>
        private double ResolveNearestPoc(WorkspaceState state, double currentClose)
        {
            if (_levels == null) return double.NaN;
            var all = _levels.GetAllLevels(state.Data, state);
            double best = double.NaN;
            double bestDist = double.PositiveInfinity;
            foreach (var lvl in all)
            {
                if (lvl.Kind != LevelKind.Poc) continue;
                double dist = Math.Abs(lvl.Price - currentClose);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = lvl.Price;
                }
            }
            return best;
        }

        private static string DescribeCondition(AlertDefinition alert, double value) => alert.Condition switch
        {
            AlertCondition.CrossesAbove    => $"crossed above {alert.Threshold:F6}",
            AlertCondition.CrossesBelow    => $"crossed below {alert.Threshold:F6}",
            AlertCondition.PatternDetected => $"pattern {alert.Pattern} detected",
            AlertCondition.ChangesDirection => "direction changed",
            AlertCondition.TrendChange     => "trend changed",
            AlertCondition.EntersZone      => $"entered {alert.Zone} zone",
            AlertCondition.ExitsZone       => $"exited {alert.Zone} zone",
            _                              => alert.Condition.ToString()
        };
    }
}
