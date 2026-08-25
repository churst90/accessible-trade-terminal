using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Decides which bars a volume or market profile is built from.
    ///
    /// <para>
    /// ── Visible versus fixed, and why it is one idea and not three indicators ──
    /// A profile answers "where did trade concentrate?" over some window. There are only two useful
    /// answers to <i>which</i> window: <b>whatever I am looking at</b>, which recomputes as you pan
    /// and zoom, and <b>a window I chose</b>, which stays put so you can navigate away and still
    /// compare price against it. That distinction is orthogonal to what is being counted — volume for
    /// a volume profile, time periods for a market profile (TPO) — which is why the anchoring rule
    /// lives here rather than being duplicated inside each one.
    /// </para>
    ///
    /// <para>
    /// ── The defect this replaced ───────────────────────────────────────────────
    /// The rule used to be <c>!code.Contains("FIXED")</c>. <c>"VPFR"</c> does not contain the string
    /// <c>"FIXED"</c>, so the Fixed Range profile was sliced to the viewport just like the Visible
    /// Range one: two catalogue entries, two descriptions, one behaviour, and no error anywhere.
    /// A string guess is not a policy, so the set is now named.
    /// </para>
    /// </summary>
    public static class ProfileAnchoring
    {
        /// <summary>
        /// Parameter holding the first bar's timestamp, as Unix seconds.
        ///
        /// <para>
        /// Indicator parameters are <c>double</c>, so the anchor is stored as a Unix timestamp rather
        /// than a formatted date. That is not a compromise: a double carries whole seconds exactly
        /// well past any date a chart will show, and it survives the workspace round-trip without a
        /// culture or format to get wrong.
        /// </para>
        /// </summary>
        public const string AnchorStartParam = "AnchorStart";

        /// <summary>Parameter holding the last bar's timestamp, as Unix seconds.</summary>
        public const string AnchorEndParam = "AnchorEnd";

        /// <summary>
        /// Profiles that recompute against whatever is on screen.
        ///
        /// <para>
        /// TPO is here because a market profile is conventionally read against the session you are
        /// looking at. A fixed-range TPO is a perfectly reasonable thing to want; it would be a new
        /// catalogue code with <see cref="AnchorStartParam"/> set, and this method is the only place
        /// that would need to know about it.
        /// </para>
        /// </summary>
        private static readonly HashSet<string> ViewportProfiles =
            new(StringComparer.OrdinalIgnoreCase) { "VPVR", "TPO" };

        /// <summary>Whether this profile code recomputes as the viewport moves.</summary>
        public static bool FollowsViewport(string? code) =>
            !string.IsNullOrEmpty(code) && ViewportProfiles.Contains(code);

        /// <summary>
        /// Records the window a fixed-range profile covers, taken from the viewport at the moment it
        /// was created.
        ///
        /// <para>
        /// Timestamps rather than bar indices, deliberately. Indices shift the moment older history
        /// is loaded or a gap is back-filled, so an index-anchored profile would silently slide onto
        /// a different stretch of the chart. A timestamp means the same thing forever.
        /// </para>
        /// </summary>
        public static void CaptureAnchor(Dictionary<string, double> parameters,
            IReadOnlyList<Ohlcv> data, int viewportStart, int viewportLength)
        {
            if (parameters == null || data == null || data.Count == 0) return;

            int start = Math.Clamp(viewportStart, 0, data.Count - 1);
            int end = Math.Clamp(start + Math.Max(1, viewportLength) - 1, start, data.Count - 1);

            parameters[AnchorStartParam] = ToUnix(data[start].Date);
            parameters[AnchorEndParam] = ToUnix(data[end].Date);
        }

        internal static double ToUnix(DateTime t) =>
            new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)).ToUnixTimeSeconds();

        /// <summary>
        /// The bars a fixed-range profile covers.
        ///
        /// <para>
        /// Returns every loaded bar when no anchor was recorded — which is still fixed in the sense
        /// that matters, because it does not move when the viewport does. An anchor that selects
        /// nothing (history not loaded that far back yet) also falls back to everything rather than
        /// rendering an empty profile, since a blank pane looks identical to a broken indicator.
        /// </para>
        /// </summary>
        public static IReadOnlyList<Ohlcv> SliceToAnchor(
            IReadOnlyList<Ohlcv> data, IReadOnlyDictionary<string, double>? parameters)
        {
            if (data == null || data.Count == 0) return data ?? Array.Empty<Ohlcv>();
            if (parameters == null) return data;

            if (!parameters.TryGetValue(AnchorStartParam, out double from) ||
                !parameters.TryGetValue(AnchorEndParam, out double to))
                return data;

            if (from <= 0 || to <= 0) return data;
            if (to < from) (from, to) = (to, from);

            var slice = data.Where(b =>
            {
                double t = ToUnix(b.Date);
                return t >= from && t <= to;
            }).ToList();

            return slice.Count > 0 ? slice : data;
        }
    }
}
