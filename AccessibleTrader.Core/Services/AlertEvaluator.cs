using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    public class AlertEvaluator : IAlertEvaluator
    {
        private readonly ISdkCandlePatternAnalyzer _patternAnalyzer;
        private readonly IIndicatorContextAnalyzer _contextAnalyzer;
        // Tracks the trend direction seen on the previous bar per alert+series pair,
        // so EvaluateTrendChange detects actual direction flips rather than any non-flat trend.
        private readonly Dictionary<string, TrendDirection> _previousTrends =
            new(StringComparer.OrdinalIgnoreCase);

        public AlertEvaluator(ISdkCandlePatternAnalyzer patternAnalyzer, IIndicatorContextAnalyzer contextAnalyzer)
        {
            _patternAnalyzer = patternAnalyzer;
            _contextAnalyzer = contextAnalyzer;
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
                catch { /* skip silently — pure evaluation, no side effects */ }
            }

            return results;
        }

        private AlertFired? TryEvaluate(
            AlertDefinition alert,
            WorkspaceState state,
            Ohlcv newBar,
            Ohlcv previousBar,
            IReadOnlyDictionary<string, double> previousValues)
        {
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
