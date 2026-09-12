using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Alerts
{
    /// <summary>
    /// Whether the background monitors can honestly evaluate an alert with no chart open.
    /// Shared between the WebHost monitors (which exclude and log these) and the alerts UI
    /// (which says so at creation time) — one definition, so the warning and the exclusion can
    /// never disagree.
    ///
    /// <para><b>Two answers, because there are two monitors with different chests.</b> The
    /// LOCAL monitor (<c>LocalBackgroundMonitor</c>, since 2026-09-11) composes a real chart
    /// per watched symbol — warmup-deep bars, the indicators the alerts reference recomputed
    /// through the store-free engine, the profiles binned — so it can evaluate everything the
    /// in-session pipeline can, given a symbol to fetch. That is <see cref="WhyUnwatchable"/>.
    /// The HOSTED monitor still evaluates against <c>WorkspaceState.Initial</c> — no indicator
    /// series, no volume profile — and an alert that reads chart state is not "evaluated with
    /// a blank chart"; it silently returns null on every poll while the user believes the market
    /// is being watched. That is <see cref="WhyUnwatchableWithoutAChart"/>, the list this class
    /// carried for both monitors until the local one could do better.</para>
    /// </summary>
    public static class BackgroundWatchability
    {
        /// <summary>
        /// Why the LOCAL background monitor cannot watch this alert; null = it can.
        /// </summary>
        /// <param name="chartSeries">The series saved on the chart the alert belongs to — the
        /// last autosaved tab for its symbol, or the open chart's series at creation time. An
        /// empty list means there is no such chart. It decides one thing: a point-of-control
        /// alert reads a profile, and a profile is a series on a chart, so an alert whose chart
        /// has none is an alert nothing can answer.</param>
        public static string? WhyUnwatchable(AlertDefinition a, IEnumerable<SeriesConfig> chartSeries)
        {
            if (string.IsNullOrWhiteSpace(a.Symbol) || string.IsNullOrWhiteSpace(a.Provider))
                return "it has no explicit symbol and provider to fetch by";
            if (a.Target == AlertTarget.Poc
                && !chartSeries.Any(s => s.Drawing == null && ProfileAnchoring.IsProfileCode(s.IndicatorCode)))
                return "a point-of-control alert reads a volume or market profile, and this chart has none saved";
            return null;
        }

        /// <summary>
        /// Why a monitor that evaluates against a BLANK chart cannot watch this alert; null = it
        /// can. The hosted monitor's list — and, until 2026-09-11, the local one's too.
        /// </summary>
        public static string? WhyUnwatchableWithoutAChart(AlertDefinition a)
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
