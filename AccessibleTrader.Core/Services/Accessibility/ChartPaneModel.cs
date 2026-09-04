using System.Globalization;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// One top-level pane: a Y axis, a horizontal band of the canvas, and the series drawn in it.
    /// </summary>
    /// <param name="Key">The raw pane key carried by <see cref="ChartSeries.Pane"/> — "Main",
    /// "Volume", "Pane_CIPHER_B". This is what <c>WorkspaceState.PaneRanges</c> is keyed by.</param>
    /// <param name="DisplayName">What the pane is CALLED aloud, without the word "pane".</param>
    /// <param name="Series">Every series in the pane, in visual order.</param>
    public sealed record PaneInfo(string Key, string DisplayName, IReadOnlyList<ChartSeries> Series);

    /// <summary>
    /// The chart's structural model, in one place: which panes exist, what order they are drawn
    /// in, what each is called, and which series belong to which.
    ///
    /// <para>
    /// A PANE IS A Y AXIS. That is the differentiator and the only one — <c>ChartRenderer</c>
    /// groups the series list by <see cref="ChartSeries.Pane"/>, lays the groups out top to
    /// bottom, and gives each group its own axis and its own range. Candles and Price share the
    /// Main pane because they share a price axis; Cipher B gets a pane of its own because it
    /// declares <c>DefaultPane = "Pane_CIPHER_B"</c>.
    /// </para>
    ///
    /// <para>
    /// A SUB-PANE is a strip inside a pane, declared by a COMPONENT
    /// (<c>ComponentConfig.SubPaneName</c>) — but the renderer collects sub-panes across every
    /// series in the pane, so visually a sub-pane belongs to the PANE and not to the series that
    /// declared it. Navigation used to disagree with that: it built its pane list from one
    /// series' components alone, so Ctrl+Up/Down on the candle series could never reach Price.
    /// Everything that walks or names the structure now comes through here, so there is one
    /// answer rather than three.
    /// </para>
    ///
    /// <para>
    /// Visual order is Main first, then every other pane in first-appearance order — the same
    /// order <c>ChartRenderer</c> lays them out in. It matters because "PageDown moves down the
    /// chart" is a promise the list order alone does not keep: a series added out of pane
    /// sequence (an indicator added before a second price overlay) puts the flat list and the
    /// picture in different orders, and the keys then disagree about which way is down.
    /// </para>
    ///
    /// <para>
    /// Pure and static: state in, structure out. No DI, no events.
    /// </para>
    /// </summary>
    public static class ChartPaneModel
    {
        /// <summary>The pane every price series shares, and the one that is always drawn first.</summary>
        public const string MainPaneKey = "Main";

        /// <summary>A series' pane key, with the empty/absent case resolved to Main.</summary>
        public static string KeyOf(ChartSeries series) =>
            string.IsNullOrEmpty(series?.Pane) ? MainPaneKey : series!.Pane;

        /// <summary>
        /// Every pane on the chart, in the order the renderer draws them: Main first, then the
        /// indicator panes in first-appearance order.
        ///
        /// <para>
        /// Hidden series are INCLUDED. The renderer skips them because there is nothing to draw;
        /// navigation must not, because "hidden" is a state the user is told about and can
        /// reverse, and a pane that vanishes from the keyboard the moment its series is hidden is
        /// a one-way door.
        /// </para>
        /// </summary>
        public static IReadOnlyList<PaneInfo> Panes(IEnumerable<ChartSeries>? series)
        {
            var list = series?.ToList() ?? new List<ChartSeries>();
            if (list.Count == 0) return Array.Empty<PaneInfo>();

            var order = new List<string>();
            var groups = new Dictionary<string, List<ChartSeries>>(StringComparer.Ordinal);

            foreach (var s in list)
            {
                string key = KeyOf(s);
                if (!groups.TryGetValue(key, out var bucket))
                {
                    bucket = new List<ChartSeries>();
                    groups[key] = bucket;
                    order.Add(key);
                }
                bucket.Add(s);
            }

            // Main is drawn first whether or not it was declared first.
            order.Sort((a, b) =>
            {
                bool aMain = a.Equals(MainPaneKey, StringComparison.OrdinalIgnoreCase);
                bool bMain = b.Equals(MainPaneKey, StringComparison.OrdinalIgnoreCase);
                if (aMain == bMain) return 0;
                return aMain ? -1 : 1;
            });

            return order
                .Select(k => new PaneInfo(k, DisplayName(k, groups[k]), groups[k]))
                .ToList();
        }

        /// <summary>
        /// Every series on the chart in VISUAL top-to-bottom order — the order Page Up and Page
        /// Down walk. Within a pane, list order is preserved.
        /// </summary>
        public static IReadOnlyList<ChartSeries> SeriesInVisualOrder(IEnumerable<ChartSeries>? series) =>
            Panes(series).SelectMany(p => p.Series).ToList();

        /// <summary>
        /// What a pane is called aloud, without the word "pane".
        ///
        /// <para>
        /// Main is "Main". A pane holding exactly one series is named by that series — the pane
        /// key is a machine string ("Pane_CIPHER_B") and the series already carries the name the
        /// user chose it by ("Cipher B"). Only a shared pane with no single owner falls back to
        /// prettifying the key.
        /// </para>
        /// </summary>
        public static string DisplayName(string? paneKey, IReadOnlyList<ChartSeries>? paneSeries = null)
        {
            if (string.IsNullOrEmpty(paneKey) ||
                paneKey.Equals(MainPaneKey, StringComparison.OrdinalIgnoreCase))
                return "Main";

            if (paneSeries is { Count: 1 })
            {
                string? name = paneSeries[0].Name;
                if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            }

            return Prettify(paneKey!);
        }

        /// <summary>
        /// What a sub-pane strip is called aloud, without the word "pane". Null/empty — the main
        /// area of the pane — returns null, because the main area is not a thing with a name of
        /// its own and saying "main area" on every move into it would be noise.
        ///
        /// <para>
        /// A sub-pane key is a two-letter machine string ("MF", "FY"), so it is resolved through
        /// the display name of a component that lives in it ("Money Flow Wave" → "Money Flow
        /// Wave") in preference to spelling the key aloud. The search runs across every series in
        /// the pane, not just one, because that is where the renderer draws the strip from.
        /// </para>
        /// </summary>
        public static string? SubPaneDisplayName(string? subPaneName, IEnumerable<ChartSeries>? paneSeries)
        {
            if (string.IsNullOrEmpty(subPaneName)) return null;

            foreach (var s in paneSeries ?? Enumerable.Empty<ChartSeries>())
            {
                foreach (var comp in s.Components)
                {
                    if (!string.Equals(comp.SubPaneName, subPaneName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string dn = comp.DisplayName ?? comp.Name ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(dn) &&
                        !dn.Trim().Equals(subPaneName, StringComparison.OrdinalIgnoreCase))
                        return dn.Trim();
                }
            }

            return Prettify(subPaneName!);
        }

        /// <summary>
        /// Distinct sub-pane keys inside a pane, in first-appearance order, collected across
        /// EVERY series in the pane. Null (the pane's main area) is not included.
        /// </summary>
        public static IReadOnlyList<string> SubPaneKeys(IEnumerable<ChartSeries>? paneSeries)
        {
            var seen = new List<string>();
            foreach (var s in paneSeries ?? Enumerable.Empty<ChartSeries>())
            {
                foreach (var comp in s.Components)
                {
                    if (string.IsNullOrEmpty(comp.SubPaneName)) continue;
                    if (!seen.Contains(comp.SubPaneName, StringComparer.OrdinalIgnoreCase))
                        seen.Add(comp.SubPaneName!);
                }
            }
            return seen;
        }

        /// <summary>"Pane_CIPHER_B" → "Cipher B". Machine keys read as spelling aloud.</summary>
        private static string Prettify(string key)
        {
            string t = key.Trim();
            if (t.StartsWith("Pane_", StringComparison.OrdinalIgnoreCase)) t = t[5..];
            t = t.Replace('_', ' ').Replace('-', ' ').Trim();
            if (t.Length == 0) return key.Trim();

            // ALL-CAPS machine keys ("CIPHER B") read better title-cased; anything already mixed
            // case was written by a human and is left exactly as it was.
            if (t.All(c => !char.IsLower(c)))
                t = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(t.ToLowerInvariant());

            return t;
        }
    }
}
