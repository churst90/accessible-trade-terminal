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

        /// <summary>
        /// Why this alert cannot fire <b>at all</b>, anywhere, ever; null = it can.
        ///
        /// <para><see cref="WhyUnwatchable"/> answers a narrower question — "can the
        /// background monitors watch it with no chart open" — and answering only that
        /// question is what let the alerts modal tell a user
        /// <i>"It works while this chart is open, but background and server-side monitoring
        /// cannot watch it"</i> about an alert that <b>worked nowhere</b>. The modal offered
        /// Target=Indicator and Condition=EntersZone/ExitsZone with no way to name the
        /// indicator, the component or the zone, and <c>AddAlert</c> never set
        /// <c>IndicatorCode</c>, <c>ComponentName</c> or <c>Zone</c>. In
        /// <c>AlertEvaluator.TryEvaluate</c> the Indicator arm requires both
        /// <c>IndicatorCode</c> and <c>ComponentName</c>, so such an alert fell through every
        /// arm to <c>return null</c>, and <c>EvaluateZone</c> returned false immediately for a
        /// null <c>IndicatorCode</c>. <b>A blind user was told their alert was live.</b></para>
        ///
        /// <para>The pickers now exist, so this is a backstop rather than the primary fix —
        /// but it is the backstop that makes "the alert is armed" a claim the app can
        /// actually stand behind, including for alerts restored from an older
        /// <c>alerts.json</c> written before the pickers did.</para>
        /// </summary>
        public static string? WhyUnfireable(AlertDefinition a)
        {
            // A tree alert carries its own conditions and does not use these fields.
            if (a.ConditionTree != null) return null;

            if (a.Target == AlertTarget.Indicator
                && (string.IsNullOrWhiteSpace(a.IndicatorCode) || string.IsNullOrWhiteSpace(a.ComponentName)))
                return "it targets an indicator but names no indicator and component";

            if (a.Condition is AlertCondition.EntersZone or AlertCondition.ExitsZone)
            {
                if (string.IsNullOrWhiteSpace(a.IndicatorCode))
                    return "a zone condition needs an indicator to read the zone from";
                if (a.Zone == null)
                    return "a zone condition needs a zone to watch";
            }

            if (a.Condition == AlertCondition.TrendChange && string.IsNullOrWhiteSpace(a.IndicatorCode))
                return "a trend-change condition needs an indicator to read the trend from";

            return null;
        }
    }
}
