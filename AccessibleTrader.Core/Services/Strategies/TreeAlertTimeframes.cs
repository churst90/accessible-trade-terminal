using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// The alert paths' half of higher-timeframe leaves. One rule, called by every place that
    /// evaluates advanced alerts — the focused chart (<c>AlertOrchestrator</c>), a background tab
    /// (<c>BackgroundWorkspaceMonitor</c>) and the browser-closed local monitor — so they cannot
    /// drift apart.
    ///
    /// <para>
    /// Until 2026-10 nothing in any alert path loaded HTF data: only a strategy's
    /// <c>Initialize</c> and the screener did. An advanced alert with a "1w" leaf was therefore
    /// false on every bar and, the first time, announced as degraded — the editor offered the
    /// timeframe picker and the alert it built could never fire.
    /// </para>
    /// </summary>
    public static class TreeAlertTimeframes
    {
        /// <summary>
        /// Starts or refreshes the HTF loads each tree alert needs, and returns the alerts that
        /// may be evaluated now: every non-tree alert, and every tree alert whose HTF data has
        /// landed at least once. A tree still loading is held back rather than evaluated — its
        /// unloaded leaf reads false, and under a NOT that is a TRUE that would fire.
        /// </summary>
        public static List<AlertDefinition> ReadyToEvaluate(
            List<AlertDefinition> alerts, IConditionEvaluator? evaluator, WorkspaceState state)
        {
            if (evaluator == null) return alerts;
            int bars = state.Data?.Count ?? 0;
            var ready = new List<AlertDefinition>(alerts.Count);
            foreach (var a in alerts)
            {
                if (a.ConditionTree == null || !a.IsActive
                    || evaluator.PrepareTimeframes(a.ConditionTree, state.Identity, bars))
                    ready.Add(a);
            }
            return ready;
        }
    }
}
