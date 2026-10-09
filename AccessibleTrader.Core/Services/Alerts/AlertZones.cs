using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Alerts
{
    /// <summary>
    /// The zones an alert can watch on an indicator component, and where each one starts — ONE
    /// answer for the alerts dialog (what it offers) and the evaluator (what it tests), so the
    /// dialog can never offer a zone the engine cannot fire.
    ///
    /// <para>
    /// ── Why the indicator's LEVELS and not a table ────────────────────────────
    /// Cody, 2026-10-09: <i>"if I select SMA on the chart under indicator, this doesn't have
    /// overbought/oversold zones"</i>. The dialog offered Overbought, Oversold, Upper band and
    /// Lower band for every indicator, and the evaluator read a hardcoded table in
    /// <c>IndicatorContextAnalyzer</c> that knew RSI and three Cipher B waves. So an SMA zone
    /// alert was accepted and could never fire; Stochastic, Williams %R and the Ultimate
    /// Oscillator — which DECLARE their 80/20, -20/-80, 70/30 lines — could not fire either;
    /// and the two band options matched nothing at all (the analyzer looked for components
    /// called "Upper"/"Lower", which Bollinger stopped having long ago).
    /// </para>
    ///
    /// <para>
    /// The rest of the app already knows an indicator's zones from its levels: the
    /// <c>{zone}</c> speech token (<c>SpeechFormatter.ResolveZone</c>), the zone noise
    /// (<c>AudioZoneHelper</c>) and Ctrl+Left/Right all read <see cref="LevelConfig.EffectiveRole"/>
    /// on the series' levels, scoped to the component through
    /// <see cref="ComponentConfig.SubscribedLevelNames"/>. Those levels are the provider's
    /// declared defaults plus whatever the user added or edited in Properties, and they are
    /// saved with the workspace, so the background monitors rebuild the same ones. Hidden levels
    /// count: hiding a line declutters the picture, it does not undeclare the zone.
    /// </para>
    ///
    /// <para>
    /// Where several lines share a role (Cipher B's Overbought 53 and Extreme OB 60) the zone
    /// starts at the one nearest the middle: "overbought" begins at 53, as the speech token
    /// already says.
    /// </para>
    /// </summary>
    public static class AlertZones
    {
        /// <summary>The zones this component has, in a stable order. Empty when it has none.</summary>
        public static IReadOnlyList<AlertZone> For(SeriesConfig series, string? componentName)
        {
            var zones = new List<AlertZone>(2);
            if (Threshold(series, componentName, AlertZone.Overbought).HasValue) zones.Add(AlertZone.Overbought);
            if (Threshold(series, componentName, AlertZone.Oversold).HasValue) zones.Add(AlertZone.Oversold);
            return zones;
        }

        /// <summary>
        /// Where <paramref name="zone"/> starts on this component, or null when it has no such
        /// zone. Only Overbought and Oversold exist: every other <see cref="AlertZone"/> member is
        /// a band or a profile edge, which is a LINE and is alerted on as one.
        /// </summary>
        public static double? Threshold(SeriesConfig series, string? componentName, AlertZone zone)
        {
            if (series == null) return null;
            var role = zone switch
            {
                AlertZone.Overbought => LevelRole.Overbought,
                AlertZone.Oversold => LevelRole.Oversold,
                _ => (LevelRole?)null,
            };
            if (role == null) return null;

            var comp = Component(series, componentName);
            double? best = null;
            foreach (var level in series.Levels)
            {
                if (level.EffectiveRole != role || !Subscribes(comp, level.Name)) continue;
                if (double.IsNaN(level.Value)) continue;
                // Nearest the middle: the lowest overbought line, the highest oversold one.
                best = best == null ? level.Value
                     : role == LevelRole.Overbought ? Math.Min(best.Value, level.Value)
                     : Math.Max(best.Value, level.Value);
            }
            return best;
        }

        /// <summary>Whether <paramref name="value"/> is inside a zone starting at <paramref name="threshold"/>.</summary>
        public static bool IsIn(AlertZone zone, double value, double threshold) =>
            zone == AlertZone.Overbought ? value >= threshold : value <= threshold;

        /// <summary>
        /// The component a zone alert reads. The one it names; or, for an alert written before
        /// the dialog let a zone alert name one, the first component that HAS a zone — the old
        /// evaluator read the indicator's first registered component, which is the same choice
        /// for every indicator it could read at all.
        /// </summary>
        public static string? ResolveComponent(SeriesConfig series, string? componentName)
        {
            if (!string.IsNullOrWhiteSpace(componentName)) return componentName;
            return series.Components.FirstOrDefault(c => For(series, c.Name).Count > 0)?.Name;
        }

        private static ComponentConfig? Component(SeriesConfig series, string? componentName) =>
            componentName == null ? null
                : series.Components.FirstOrDefault(c => c.Name.Equals(componentName, StringComparison.OrdinalIgnoreCase));

        /// <summary>The subscription rule <c>SpeechFormatter.ResolveZone</c> and
        /// <c>AudioZoneHelper</c> both apply: null subscribes to every level, empty to none.</summary>
        private static bool Subscribes(ComponentConfig? comp, string levelName)
        {
            if (comp?.SubscribedLevelNames is not { } subs) return true;
            if (subs.Count == 0) return false;
            return subs.Contains(levelName, StringComparer.OrdinalIgnoreCase);
        }
    }
}
