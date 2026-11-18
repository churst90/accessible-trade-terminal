using System.Globalization;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>
    /// The words for the Alt+E calendar: the day heading, one line per event, the two button
    /// names, the alert announcement and the release history. Pure and static, so the razor only
    /// renders and every sentence can be pinned by a test.
    ///
    /// <para>
    /// The figure words and the price sentence are the existing ones on
    /// <see cref="MarketEventSpeech"/>: the calendar is another place the same facts are said,
    /// never a second set of words for them.
    /// </para>
    /// </summary>
    public static class MarketEventCalendarWording
    {
        /// <summary>"Wednesday 11 March 2026".</summary>
        public static string DayHeading(DateOnly day)
            => day.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture);

        /// <summary>"11 March" — the short day the button names carry.</summary>
        public static string ShortDate(DateOnly day)
            => day.ToString("d MMMM", CultureInfo.InvariantCulture);

        /// <summary>"11 March 2026", as the not-on-the-bars sentence says it.</summary>
        public static string LongDate(DateOnly day)
            => day.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

        /// <summary>"all day" for a day-only event, otherwise the time on the F12 clock.</summary>
        public static string TimeWord(MarketEvent e)
            => e.Precision == MarketEventPrecision.DayOnly
                ? "all day"
                : MarketEventSpeech.TimeText(e);

        /// <summary>
        /// "8:30, CPI, important, expected 3.0, up 3.1% on the month, previous down 2.9%."
        ///
        /// <para>
        /// A past event reads its actual figure in the existing figure-clause words. A future one
        /// has no actual, so it says only what the previous figure was, when that is known. An
        /// expected figure, when Kalshi has one, comes after the importance word for both.
        /// </para>
        /// </summary>
        public static string EventLine(MarketEvent e, DateTime nowUtc)
        {
            var parts = new List<string>(5)
            {
                TimeWord(e),
                e.SpokenName,
                ImportanceWord(e.Importance),
            };

            string expected = MarketEventSpeech.ExpectedShort(e);
            if (expected.Length > 0) parts.Add(expected);

            string figure = IsPast(e, nowUtc)
                ? (e.Figures != null ? MarketEventSpeech.FigureClause(e) : "")
                : MarketEventSpeech.PreviousFigureClause(e);
            if (figure.Length > 0) parts.Add(figure);

            return string.Join(", ", parts) + ".";
        }

        /// <summary>"important", "medium" or "minor" — the calendar's short importance word.</summary>
        public static string ImportanceWord(MarketEventImportance importance) => importance switch
        {
            MarketEventImportance.High => "important",
            MarketEventImportance.Medium => "medium",
            _ => "minor",
        };

        /// <summary>"CPI on 11 March 2026 is not on this chart's bars."</summary>
        public static string NotOnBars(MarketEvent e)
            => $"{e.SpokenName} on {LongDate(e.Date)} is not on this chart's bars.";

        /// <summary>"Alert me before CPI, 11 March" — the toggle's accessible name.</summary>
        public static string WatchLabel(MarketEvent e)
            => $"Alert me before {e.SpokenName}, {ShortDate(e.Date)}";

        /// <summary>"History for CPI, 11 March" — the disclosure's accessible name.</summary>
        public static string HistoryLabel(MarketEvent e)
            => $"History for {e.SpokenName}, {ShortDate(e.Date)}";

        /// <summary>
        /// "Alert set for CPI, 11 March, 15 and 1 minutes before." or
        /// "Alert removed for CPI, 11 March." The leads are the effective ones: when the countdown
        /// setting is off a watched event uses 15 and 1, so that is what this says too.
        /// </summary>
        public static string WatchAnnouncement(
            MarketEvent e, bool nowWatched, IReadOnlyList<int> effectiveLeads)
            => nowWatched
                ? $"Alert set for {e.SpokenName}, {ShortDate(e.Date)}, "
                  + $"{MarketEventCountdowns.Label(effectiveLeads)} before."
                : $"Alert removed for {e.SpokenName}, {ShortDate(e.Date)}.";

        public const string NoEvents = "No events in this range with these filters.";
        public const string NoEarlierReleases = "No earlier releases.";

        /// <summary>
        /// The last <paramref name="count"/> releases of the same source before
        /// <paramref name="e"/>, newest first. Same source means the same id prefix: every
        /// <c>cpi-…</c> event is a CPI release, and a Fed decision is not a CPI release just
        /// because both are important.
        /// </summary>
        public static IReadOnlyList<MarketEvent> History(
            MarketEvent e,
            IReadOnlyList<MarketEvent> all,
            int count = 6)
        {
            string source = SourceId(e);
            return all
                .Where(x => !ReferenceEquals(x, e)
                    && x.Id != e.Id
                    && string.Equals(SourceId(x), source, StringComparison.Ordinal)
                    && Before(x, e))
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.MomentUtc)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// "13 February 2026, up 3.0% on the month, previous down 2.9%, expected 3.0, by
        /// Kalshi, as expected; Price fell 1.2% over this bar." A release with no figure says
        /// "no figure"; the price part is left out entirely when <paramref name="bar"/> is null
        /// (not loaded, or the chart does not touch it).
        /// </summary>
        public static string HistoryLine(MarketEvent e, Ohlcv? bar)
        {
            string figure = e.Figures != null ? MarketEventSpeech.FigureClause(e) : "";
            if (figure.Length == 0) figure = "no figure";
            figure += MarketEventSpeech.ExpectedSuffix(MarketEventSpeech.PrimaryFigureWithExpected(e));

            string line = $"{LongDate(e.Date)}, {figure}";
            return bar is { } drawn ? line + "; " + MarketEventSpeech.PriceSentence(drawn) : line + ".";
        }

        /// <summary>The id prefix: "cpi" from "cpi-2026-03-11".</summary>
        public static string SourceId(MarketEvent e)
        {
            int dash = e.Id.IndexOf('-');
            return dash > 0 ? e.Id[..dash] : e.Id;
        }

        private static bool Before(MarketEvent a, MarketEvent b)
            => a.Date < b.Date
               || (a.Date == b.Date && AsUtc(a.MomentUtc) < AsUtc(b.MomentUtc));

        private static bool IsPast(MarketEvent e, DateTime nowUtc)
            => AsUtc(e.MomentUtc) <= AsUtc(nowUtc);

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
