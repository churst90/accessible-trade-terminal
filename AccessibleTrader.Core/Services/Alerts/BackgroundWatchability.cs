using AccessibleTrader.Sdk.Alerts;

namespace AccessibleTrader.Core.Services.Alerts
{
    /// <summary>
    /// Whether the background monitors can honestly evaluate an alert with no
    /// chart open. They evaluate against <c>WorkspaceState.Initial</c> — no
    /// indicator series, no volume profile — so an alert that reads chart state
    /// is not "evaluated with a blank chart"; it silently returns null on every
    /// poll while the user believes the market is being watched. Shared between
    /// the WebHost monitors (which exclude and log these) and the alerts UI
    /// (which says so at creation time) — one definition, so the warning and the
    /// exclusion can never disagree.
    /// </summary>
    public static class BackgroundWatchability
    {
        /// <summary>Why background evaluation cannot watch this alert; null = it can.</summary>
        public static string? WhyUnwatchable(AlertDefinition a)
        {
            if (a.ConditionTree != null)
                return "advanced condition trees need the chart's indicator pipeline";
            if (a.Target == AlertTarget.Indicator)
                return "indicator values only exist while the chart is open";
            if (a.Target == AlertTarget.Poc)
                return "the volume profile only exists while the chart is open";
            if (a.Condition is AlertCondition.TrendChange
                or AlertCondition.EntersZone or AlertCondition.ExitsZone)
                return "trend and zone conditions read an indicator, which only exists while the chart is open";
            if (string.IsNullOrWhiteSpace(a.Symbol) || string.IsNullOrWhiteSpace(a.Provider))
                return "it has no explicit symbol and provider to fetch by";
            return null;
        }
    }
}
