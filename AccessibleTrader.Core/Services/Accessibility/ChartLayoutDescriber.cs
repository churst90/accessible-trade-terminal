using System.Globalization;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Describes the chart's LAYOUT — what the axes measure, at what scale, how many panes there
    /// are and what lives in each.
    ///
    /// <para>
    /// This is the one thing a sighted user gets for free by glancing at the screen, and until now
    /// the only route to it was navigating every pane and counting. Everything else the terminal
    /// speaks answers "what is the value here?"; this answers "what am I looking at?", which is
    /// the question you have before the first one — and the question you come back to after
    /// loading a saved workspace, or switching tabs, or returning from lunch.
    /// </para>
    ///
    /// <para>
    /// Pure and static: it takes a state and returns a sentence. No DI, no events, no side
    /// effects, so what it says can be tested exactly rather than approximately.
    /// </para>
    /// </summary>
    public static class ChartLayoutDescriber
    {
        /// <summary>
        /// A spoken summary of the chart's structure.
        /// </summary>
        /// <param name="state">Current workspace.</param>
        /// <param name="symbol">Symbol on the chart, if known.</param>
        /// <param name="timeframe">Bar interval, e.g. "1d".</param>
        public static string Describe(WorkspaceState state, string? symbol = null, string? timeframe = null)
        {
            if (state == null) return "No chart loaded.";

            var parts = new List<string>();
            var bars = state.Data;

            if (bars == null || bars.Count == 0)
                return "No chart loaded.";

            // ── What, and at what interval ────────────────────────────────
            string head = string.IsNullOrWhiteSpace(symbol) ? "Chart" : symbol!;
            if (!string.IsNullOrWhiteSpace(timeframe)) head += $", {SpokenTimeframe(timeframe!)} per bar";
            parts.Add(head + ".");

            // ── X axis: how much time is on screen, and which slice ───────
            int start = Math.Clamp(state.ViewportStartIndex, 0, Math.Max(0, bars.Count - 1));
            int length = Math.Clamp(state.ViewportLength, 1, Math.Max(1, bars.Count - start));
            int end = Math.Min(bars.Count - 1, start + length - 1);

            var first = bars[start];
            var last = bars[end];
            parts.Add($"X axis, time: {length} bars in view of {bars.Count.ToString("N0", CultureInfo.InvariantCulture)} loaded, " +
                      $"{SpeechTimeFormatter.FormatLongDate(first.Date)} to {SpeechTimeFormatter.FormatLongDate(last.Date)}.");

            // ── Y axis: the range, and the step between gridlines ─────────
            var (min, max) = state.ViewportRange;
            if (max > min && !double.IsNaN(min) && !double.IsNaN(max))
            {
                string scale = state.IsLogScale ? "logarithmic" : "linear";
                parts.Add($"Y axis, price: {Money(min)} to {Money(max)}, " +
                          $"about {Money(GridStep(max - min))} between gridlines, {scale} scale.");
            }

            // ── Panes: how the vertical space is divided ──────────────────
            var visible = state.ActiveSeries.Where(s => s.IsVisible).ToList();
            var byPane = visible.GroupBy(s => string.IsNullOrEmpty(s.Pane) ? "Main" : s.Pane).ToList();

            var main = byPane.FirstOrDefault(g => g.Key.Equals("Main", StringComparison.OrdinalIgnoreCase));
            int mainCount = main?.Count() ?? 0;
            parts.Add($"Main pane: {Count(mainCount, "series")}.");

            var others = byPane.Where(g => g != main).ToList();
            parts.Add(others.Count == 0
                ? "No separate indicator panes."
                : $"{Count(others.Count, "indicator pane")} below it, each with its own Y axis: " +
                  string.Join(", ", others.Select(g => $"{g.Key} with {Count(g.Count(), "series")}")) + ".");

            // ── What is switched off ──────────────────────────────────────
            // Stated because it explains a silent or empty-looking chart, and because the two
            // recovery shortcuts are useless to someone who does not know there is anything to
            // recover.
            int hidden = state.ActiveSeries.SelectMany(s => s.Components).Count(c => !c.IsVisible);
            int muted  = state.ActiveSeries.SelectMany(s => s.Components).Count(c => c.IsMuted);
            if (hidden > 0 || muted > 0)
            {
                var notes = new List<string>();
                if (hidden > 0) notes.Add($"{Count(hidden, "component")} hidden");
                if (muted > 0) notes.Add($"{Count(muted, "component")} muted");
                parts.Add(string.Join(", ", notes) + ".");
            }

            if (state.IsHeikinAshi) parts.Add("Heikin Ashi candles.");

            // ── Is this chart LIVE? ──────────────────────────────────────────
            //
            // Alt+Shift+L is the orientation key — "the one thing a sighted user gets for free
            // by glancing at the screen" — and until now it could not answer the question that
            // matters most about a trading chart. Three watchdogs each spoke once into a
            // transient channel and left nothing to ask. A user who missed the line had no way
            // to find out whether the prices in front of them were current.
            //
            // The elapsed time, not just the word: "no data for eleven minutes" is actionable
            // in a way that "stale" is not.
            parts.Add(DescribeFeedFreshness(state));

            return string.Join(" ", parts);
        }

        /// <summary>
        /// Whether the feed is live, and how long since anything arrived.
        /// </summary>
        internal static string DescribeFeedFreshness(WorkspaceState state)
        {
            if (state.LastTickUtc is not { } last)
            {
                // Never having ticked is not the same as having gone quiet — a historical-only
                // provider is working exactly as intended, and calling that "stale" would cry
                // wolf on every analytics chart.
                return state.DataStatus == DataStatus.Stale
                    ? "Feed reported quiet; no live data has arrived."
                    : "No live data yet.";
            }

            var since = DateTime.UtcNow - last;
            string ago = since.TotalMinutes < 1
                ? $"{Math.Max(0, (int)since.TotalSeconds)} seconds"
                : $"{(int)since.TotalMinutes} minutes";

            return state.DataStatus == DataStatus.Stale
                ? $"Feed is QUIET: last update {ago} ago."
                : $"Feed live, last update {ago} ago.";
        }

        /// <summary>"1 series" / "3 series", "1 pane" / "2 panes" — pluralised without the
        /// "(s)" construction, which reads badly aloud.</summary>
        private static string Count(int n, string noun)
        {
            if (noun.EndsWith('s')) return $"{n} {noun}";          // "series" is already plural
            return n == 1 ? $"1 {noun}" : $"{n} {noun}s";
        }

        /// <summary>
        /// The step the Y axis actually labels at.
        ///
        /// <para>
        /// This deliberately duplicates <c>ChartRenderer.RenderYAxis</c>'s nice-number arithmetic
        /// rather than approximating it. The summary is describing the chart in front of the user;
        /// if it says "20,000 between gridlines" and the axis is labelled every 50,000, the
        /// summary is worse than silence — it is confidently wrong about something the user cannot
        /// independently check. The thresholds below (1.5 / 3.5 / 7.5) are the renderer's, and
        /// must move together with it.
        /// </para>
        /// </summary>
        internal static double GridStep(double range)
        {
            if (range <= 0 || double.IsNaN(range) || double.IsInfinity(range)) return 0;

            const int TargetLabelCount = 5;   // a full-size pane; small panes use 3
            double rough = range / TargetLabelCount;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            double fraction = rough / magnitude;

            double step = fraction < 1.5 ? 1
                        : fraction < 3.5 ? 2
                        : fraction < 7.5 ? 5
                        : 10;
            return step * magnitude;
        }

        /// <summary>"1d" → "1 day"; "4h" → "4 hours". Bare codes read as spelling aloud.</summary>
        internal static string SpokenTimeframe(string timeframe)
        {
            if (string.IsNullOrWhiteSpace(timeframe)) return timeframe;

            string t = timeframe.Trim();
            int i = 0;
            while (i < t.Length && char.IsDigit(t[i])) i++;
            if (i == 0 || i == t.Length) return t;

            if (!int.TryParse(t[..i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return t;

            string unit = t[i..].ToLowerInvariant() switch
            {
                "m"  => "minute",
                "h"  => "hour",
                "d"  => "day",
                "w" or "wk" => "week",
                "mo" or "M" => "month",
                _    => null!,
            };
            if (unit == null) return t;

            return n == 1 ? $"1 {unit}" : $"{n} {unit}s";
        }

        /// <summary>Price with thousands separators and no trailing noise.</summary>
        private static string Money(double v)
        {
            double abs = Math.Abs(v);
            string format = abs >= 1000 ? "N0" : abs >= 1 ? "N2" : "N6";
            return v.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
