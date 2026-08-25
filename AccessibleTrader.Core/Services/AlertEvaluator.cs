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
        // Optional so minimal test constructions keep working; when null, tree alerts
        // simply never fire. (An earlier version of this comment said the null case
        // "logs once via the try/catch above" — it does not: TryEvaluateTree returns
        // null without throwing, so nothing was logged and nothing was announced.)
        private readonly Strategies.IConditionEvaluator? _conditionEvaluator;

        // Tree alerts whose degradation has already been reported once, so a leaf that
        // cannot be evaluated is announced on the bar it first goes quiet rather than on
        // every tick for the rest of the session.
        private readonly HashSet<string> _reportedDegradations = new(StringComparer.OrdinalIgnoreCase);

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

        /// <summary>Raised when an alert rule throws during evaluation — the
        /// consumer decides how loudly to surface it (the orchestrator speaks the
        /// first failure per alert and journals it).</summary>
        public event Action<AlertDefinition, Exception>? EvaluationFailed;

        /// <summary>Raised the first time a tree alert evaluates false because a leaf could
        /// not be answered — an HTF leaf with no pre-warmed data, or a component the
        /// causality contract refuses — rather than because the market did not meet it.
        /// Those two outcomes are identical silence otherwise, which is the worst shape a
        /// failure can take in this product: the user believes the alert is watching.
        /// Once per alert id; re-armed when the alert is edited (Add/Remove clears it).</summary>
        public event Action<AlertDefinition, string>? EvaluationDegraded;

        /// <summary>Lets the orchestrator re-arm the once-per-alert gates when an alert is
        /// edited, so a fixed alert can report a NEW problem instead of staying quiet
        /// because its old one was already announced.</summary>
        public void ResetDegradationGate(string alertId) => _reportedDegradations.Remove(alertId);

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
                    // A broken alert rule (missing indicator component, divide-by-zero
                    // on a new data shape) must not silently stop firing forever with
                    // no way for the user to find out. The orchestrator subscribes to
                    // this and announces the FIRST failure per alert (then stays quiet
                    // — one broken rule must not spam every bar).
                    System.Diagnostics.Debug.WriteLine(
                        $"[AlertEvaluator] Alert '{alert.Id}' evaluation failed: {ex.GetType().Name}: {ex.Message}");
                    EvaluationFailed?.Invoke(alert, ex);
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

            // A leaf the evaluator could not answer is not the same as a market that did
            // not trigger, and until this was surfaced the two were indistinguishable:
            // the tree just stayed false forever while the user believed it was armed.
            // Reported once per alert; the gate re-arms when the alert is edited.
            string? degraded = _conditionEvaluator.LastDegradation;
            if (degraded != null && !eval.OverallTrue && _reportedDegradations.Add(alert.Id))
                EvaluationDegraded?.Invoke(alert, degraded);

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

            // A crossing belongs to the bar it happened on, and must fire once for it.
            //
            // Both background monitors re-poll on a 60-second interval (HostedAlertMonitor:33,
            // LocalBackgroundMonitor:42) and fetch the last three bars, evaluating bars[^1]
            // against bars[^2]. The default timeframe is "1h". So for the whole hour the SAME
            // pair was compared, `prevBar.Close < threshold && newBar.Close >= threshold`
            // stayed true, and nothing recorded that the alert had already fired: up to 59
            // duplicate emails / Telegram messages / Discord posts / Web Push notifications for
            // one crossing, each an unbounded SmtpClient connect. Over a weekend on an equities
            // symbol the two frozen Friday bars re-fired indefinitely.
            //
            // The tree path has carried this edge state all along (_treeState.WasTrue at :128);
            // the simple path never did, despite HostedAlertMonitor:42-45's comment asserting
            // that it must.
            //
            // RepeatIfStillActive is deliberately exempt — re-announcing a level that is still
            // held is exactly what that flag is for, and alert.Cooldown paces it.
            bool alreadyFiredThisBar =
                _lastFiredBar.TryGetValue(alert.Id, out var firedFor) && firedFor == newBar.Date;
            if (triggered && alreadyFiredThisBar) triggered = false;

            // RepeatIfStillActive parity with tree alerts, for LEVEL conditions
            // only (a crossing has a well-defined "still active" side; patterns
            // and direction changes are instantaneous events with nothing to
            // repeat). Not triggered this bar, but the level condition still
            // holds and the cooldown elapsed → re-fire, exactly like a tree.
            if (!triggered && alert.RepeatIfStillActive && IsLevelStillActive(alert, currentValue)
                && _lastSimpleFire.TryGetValue(alert.Id, out var last)
                && DateTime.UtcNow - last >= alert.Cooldown)
            {
                triggered = true;
            }

            if (!triggered) return null;
            _lastSimpleFire[alert.Id] = DateTime.UtcNow;
            _lastFiredBar[alert.Id]   = newBar.Date;

            string speechText = $"{alert.Name}: {DescribeCondition(alert, currentValue)}. Current value {currentValue:F6}";
            return new AlertFired(alert, currentValue, double.IsNaN(prevValue) ? null : prevValue, speechText);
        }

        // Last fire time per simple alert — powers RepeatIfStillActive/Cooldown.
        private readonly Dictionary<string, DateTime> _lastSimpleFire = new();

        // Timestamp of the bar a simple alert last fired for. This is the crossing-edge state
        // the simple path was missing; see the dedupe gate in TryEvaluate. Keyed on the bar's
        // own Date rather than a wall clock so a poll interval that is short relative to the
        // timeframe cannot re-announce the same crossing.
        private readonly Dictionary<string, DateTime> _lastFiredBar = new(StringComparer.OrdinalIgnoreCase);

        private static bool IsLevelStillActive(AlertDefinition alert, double currentValue) =>
            alert.Condition switch
            {
                AlertCondition.CrossesAbove => currentValue >= (alert.Threshold ?? 0),
                AlertCondition.CrossesBelow => currentValue <= (alert.Threshold ?? 0),
                _ => false,
            };

        private bool EvaluatePattern(AlertDefinition alert, Ohlcv current, Ohlcv previous, WorkspaceState state)
        {
            if (alert.Pattern == null) return false;

            // twoBarsAgo used to be passed as null here, which meant the four THREE-bar patterns —
            // morning star, evening star, three white soldiers, three black crows — could never be
            // detected on this path. An alert configured for any of them was silently dead: it
            // saved, it listed, it never fired. They are public API on AlertDefinition, so the UI
            // offering them was never the thing that made them reachable.
            var data = state.Data;
            Ohlcv? twoBarsAgo = (data != null && data.Count >= 3) ? data[^3] : (Ohlcv?)null;

            var analysis = _patternAnalyzer.Analyze(current, previous, twoBarsAgo, data);
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
