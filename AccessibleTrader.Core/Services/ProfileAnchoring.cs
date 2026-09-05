using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    /// <summary>Which bars a profile is built from.</summary>
    public enum ProfileWindow
    {
        /// <summary>Whatever is on screen; recomputes as you pan and zoom.</summary>
        Visible,
        /// <summary>The window you were viewing when you added it; never moves.</summary>
        Fixed,
        /// <summary>
        /// One trading session — the calendar day (UTC) of the last visible bar. Recomputes
        /// as you pan from day to day, the market-profile convention.
        /// </summary>
        Session,
        /// <summary>From the bar you were on when you added it to the newest bar; grows at the live edge.</summary>
        Anchored,
    }

    /// <summary>
    /// Decides which bars a volume or market profile is built from, and names every profile
    /// code the catalogue has.
    ///
    /// <para>
    /// ── One idea crossed with another, not eight indicators ────────────────────
    /// A profile answers "where did trade concentrate?" over some window. There are only four
    /// useful answers to <i>which</i> window (<see cref="ProfileWindow"/>): whatever I am looking
    /// at, a window I chose, one trading session, or from a chosen bar to now. That is orthogonal
    /// to what is being counted — volume for a volume profile, time periods for a market profile
    /// (TPO). Four windows by two measures is eight catalogue codes, and every one of them is the
    /// same two calls with a different slice. Which is why the slicing rule lives here rather than
    /// being duplicated inside each one.
    /// </para>
    ///
    /// <para>
    /// ── The defect this replaced ───────────────────────────────────────────────
    /// The rule used to be <c>!code.Contains("FIXED")</c>. <c>"VPFR"</c> does not contain the string
    /// <c>"FIXED"</c>, so the Fixed Range profile was sliced to the viewport just like the Visible
    /// Range one: two catalogue entries, two descriptions, one behaviour, and no error anywhere.
    /// A string guess is not a policy, so the set is named.
    /// </para>
    ///
    /// <para>
    /// ── Why the codes are here and not in seven places ──────────────────────────
    /// Before 2026-09-05 the three codes were spelled out by hand in the pane assigner, the
    /// series manager (twice), the orchestrator, the backtester, the level provider and this
    /// class. Adding a fourth profile meant finding all seven, and nothing said when one was
    /// missed — the same shape as the hand-written clone that dropped narration flags on
    /// restore. <see cref="IsProfileCode"/> is now the one answer.
    /// </para>
    /// </summary>
    public static class ProfileAnchoring
    {
        // ── The codes ────────────────────────────────────────────────────────

        public const string VolumeVisible  = "VPVR";
        public const string VolumeFixed    = "VPFR";
        public const string VolumeSession  = "VPSESSION";
        public const string VolumeAnchored = "VPANCHOR";
        public const string TimeVisible    = "TPO";
        public const string TimeFixed      = "TPOFR";
        public const string TimeSession    = "TPOSESSION";
        public const string TimeAnchored   = "TPOANCHOR";

        /// <summary>Every profile code the catalogue registers, in catalogue order.</summary>
        public static readonly IReadOnlyList<string> AllCodes = new[]
        {
            VolumeVisible, VolumeFixed, VolumeSession, VolumeAnchored,
            TimeVisible, TimeFixed, TimeSession, TimeAnchored,
        };

        private static readonly Dictionary<string, ProfileWindow> Windows = new(StringComparer.OrdinalIgnoreCase)
        {
            [VolumeVisible]  = ProfileWindow.Visible,
            [VolumeFixed]    = ProfileWindow.Fixed,
            [VolumeSession]  = ProfileWindow.Session,
            [VolumeAnchored] = ProfileWindow.Anchored,
            [TimeVisible]    = ProfileWindow.Visible,
            [TimeFixed]      = ProfileWindow.Fixed,
            [TimeSession]    = ProfileWindow.Session,
            [TimeAnchored]   = ProfileWindow.Anchored,
        };

        /// <summary>
        /// Whether this code is a profile at all. The eight registered codes, plus anything
        /// whose code says "PROFILE" — the spelling saved workspaces from before the codes
        /// were fixed used, kept so they still load as profiles.
        /// </summary>
        public static bool IsProfileCode(string? code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            if (Windows.ContainsKey(code)) return true;
            return code.Contains("PROFILE", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The window this profile code covers. A legacy "…PROFILE" code follows the viewport.</summary>
        public static ProfileWindow WindowOf(string? code)
            => !string.IsNullOrEmpty(code) && Windows.TryGetValue(code, out var w) ? w : ProfileWindow.Visible;

        /// <summary>
        /// Whether the profile counts time periods at each price (a market profile, TPO) rather
        /// than volume. A legacy "MARKET PROFILE" code counts time as well.
        /// </summary>
        public static bool CountsTime(string? code)
            => !string.IsNullOrEmpty(code)
               && (code.StartsWith("TPO", StringComparison.OrdinalIgnoreCase)
                   || code.Contains("MARKET PROFILE", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Whether this profile recomputes as the viewport moves. True for the visible-range
        /// pair and the session pair — a session profile is picked BY the viewport, so panning
        /// into yesterday shows yesterday's session.
        /// </summary>
        public static bool FollowsViewport(string? code)
            => IsProfileCode(code) && WindowOf(code) is ProfileWindow.Visible or ProfileWindow.Session;

        // ── Anchors ──────────────────────────────────────────────────────────

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

        /// <summary>Parameter holding the last bar's timestamp, as Unix seconds. Absent on an anchored profile, which runs to the newest bar.</summary>
        public const string AnchorEndParam = "AnchorEnd";

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

        /// <summary>
        /// Records where an anchored profile starts: the bar the cursor was on when it was added.
        /// No end is recorded — the profile runs to the newest bar and grows with the feed, the
        /// anchored-VWAP idea applied to volume.
        /// </summary>
        public static void CaptureAnchorStart(Dictionary<string, double> parameters,
            IReadOnlyList<Ohlcv> data, int barIndex)
        {
            if (parameters == null || data == null || data.Count == 0) return;
            int start = Math.Clamp(barIndex, 0, data.Count - 1);
            parameters[AnchorStartParam] = ToUnix(data[start].Date);
            parameters.Remove(AnchorEndParam);
        }

        internal static double ToUnix(DateTime t) =>
            new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)).ToUnixTimeSeconds();

        // ── Slicing ──────────────────────────────────────────────────────────

        /// <summary>
        /// The bars a profile of <paramref name="code"/> is built from. The one entry point the
        /// orchestrator and the backtester share, so a window means the same thing on both.
        /// </summary>
        /// <param name="viewportStart">First visible bar. For a caller with no viewport (the
        /// backtester), pass 0 and <paramref name="data"/>'s count: the visible window is then
        /// everything and the session is the newest one.</param>
        public static IReadOnlyList<Ohlcv> Slice(string? code, IReadOnlyList<Ohlcv> data,
            IReadOnlyDictionary<string, double>? parameters, int viewportStart, int viewportLength)
        {
            if (data == null || data.Count == 0) return data ?? Array.Empty<Ohlcv>();
            return WindowOf(code) switch
            {
                ProfileWindow.Visible => SliceToViewport(data, viewportStart, viewportLength),
                ProfileWindow.Session => SliceToSession(data, viewportStart, viewportLength),
                _ => SliceToAnchor(data, parameters),
            };
        }

        private static IReadOnlyList<Ohlcv> SliceToViewport(IReadOnlyList<Ohlcv> data, int viewportStart, int viewportLength)
        {
            int start = Math.Clamp(viewportStart, 0, data.Count - 1);
            int length = Math.Clamp(viewportLength, 1, data.Count - start);
            return data.Skip(start).Take(length).ToList();
        }

        /// <summary>
        /// The bars of one trading session: every loaded bar on the same UTC calendar day as the
        /// LAST visible bar. The last one, not the first, because that is the day the cursor is
        /// in when the chart is scrolled to the live edge, and the day you have panned to when it
        /// is not. A daily-or-coarser chart has one bar per session, so the profile is that bar's
        /// range — the catalogue description says intraday.
        /// </summary>
        public static IReadOnlyList<Ohlcv> SliceToSession(IReadOnlyList<Ohlcv> data, int viewportStart, int viewportLength)
        {
            if (data == null || data.Count == 0) return data ?? Array.Empty<Ohlcv>();
            int start = Math.Clamp(viewportStart, 0, data.Count - 1);
            int last = Math.Clamp(start + Math.Max(1, viewportLength) - 1, start, data.Count - 1);
            DateTime session = SessionOf(data[last].Date);
            var slice = data.Where(b => SessionOf(b.Date) == session).ToList();
            return slice.Count > 0 ? slice : data;
        }

        /// <summary>The session a bar belongs to: its UTC calendar day.</summary>
        public static DateTime SessionOf(DateTime barDate)
            => DateTime.SpecifyKind(barDate, DateTimeKind.Utc).Date;

        /// <summary>
        /// The bars a fixed-range or anchored profile covers.
        ///
        /// <para>
        /// Returns every loaded bar when no anchor was recorded — which is still fixed in the sense
        /// that matters, because it does not move when the viewport does. An anchor that selects
        /// nothing (history not loaded that far back yet) also falls back to everything rather than
        /// rendering an empty profile, since a blank pane looks identical to a broken indicator.
        /// With a start and no end, the slice runs to the newest bar: that is an anchored profile.
        /// </para>
        /// </summary>
        public static IReadOnlyList<Ohlcv> SliceToAnchor(
            IReadOnlyList<Ohlcv> data, IReadOnlyDictionary<string, double>? parameters)
        {
            if (data == null || data.Count == 0) return data ?? Array.Empty<Ohlcv>();
            if (parameters == null) return data;

            if (!parameters.TryGetValue(AnchorStartParam, out double from) || from <= 0)
                return data;

            bool hasEnd = parameters.TryGetValue(AnchorEndParam, out double to) && to > 0;
            if (!hasEnd) to = double.MaxValue;
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
