using System;
using System.Globalization;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// The single place a bar timestamp becomes text a user hears or reads.
    ///
    /// Bars carry <see cref="DateTimeKind.Utc"/> — <c>TimestampParser.Parse</c> normalises every
    /// provider's stamp to UTC before it reaches a chart. That makes a bare
    /// <c>bar.Date.ToString(...)</c> a *UTC* reading and <c>bar.Date.ToLocalTime().ToString(...)</c>
    /// a *local* one, and before 2026-08-27 the codebase did both: arrow keys, the profile reading
    /// and the heatmap converted, while Ctrl+Shift+D (bar detail), coordinate-entry mode, the
    /// Ctrl+Alt+Shift+Y layout description and the viewport description did not. On one bar the
    /// arrow keys said "14:30" and Ctrl+Shift+D said "18:30".
    ///
    /// For a user whose only picture of the chart is the spoken one, two authoritative-sounding
    /// times for the same bar are worse than either being wrong: there is no visual to arbitrate.
    /// Every timestamp-to-text path now goes through <see cref="ToDisplay"/>.
    ///
    /// Format strings stay at the call site — a heatmap column wants "HH:mm" and the layout
    /// description wants a date — but the *instant* they describe is now resolved in one place.
    ///
    /// <para><b>A bar of a day or longer is named by its OWN date</b> — <see cref="ToBarDisplay"/>.
    /// Reported by Cody, 2026-10-09, in America/Chicago: on the 9th the daily chart's newest
    /// bar read "October 8". The bar is stamped 2026-10-09T00:00Z; converted to Chicago that is
    /// 19:00 on the 8th, and the date of that local instant is not the date the bar is FOR.
    /// Weekly bars, which begin on a Monday at 00:00Z, read as the Sunday before. A daily bar
    /// is a calendar day, not an instant, so it is labelled the way exchanges and TradingView
    /// label it: by its own date, in every zone. Intraday bars ARE instants and keep the
    /// user's local clock. Every bar readout — arrow keys, bar-close announcements, playback,
    /// the layout description, the hover readout, the bar slider and the chart's own axis —
    /// goes through this, so what is shown and what is spoken name the same day.</para>
    /// </summary>
    public static class SpeechTimeFormatter
    {
        /// <summary>Full stamp, as the arrow keys read it by default.</summary>
        public const string DateTimeFormat = "MMMM dd, yyyy, HH:mm";

        /// <summary>Time of day. Heatmap columns, profile bins, bar detail, coordinate entry.</summary>
        public const string TimeFormat = "HH:mm";

        /// <summary>Date without a year, for the "DateOnly" speech order.</summary>
        public const string DateFormat = "MMMM dd";

        /// <summary>Date with a year, for viewport and layout descriptions.</summary>
        public const string LongDateFormat = "MMMM d yyyy";

        /// <summary>
        /// Resolves a stored stamp to the instant the user should be told about, in their own
        /// zone. Unspecified is treated as UTC because that is what every provider path produces;
        /// assuming local there would silently re-introduce the divergence this class exists to
        /// remove, for exactly the bars whose Kind got lost in a round-trip.
        /// </summary>
        public static DateTime ToDisplay(DateTime stamp) => stamp.Kind switch
        {
            DateTimeKind.Local => stamp,
            DateTimeKind.Utc => stamp.ToLocalTime(),
            _ => DateTime.SpecifyKind(stamp, DateTimeKind.Utc).ToLocalTime(),
        };

        /// <summary>
        /// The instant a BAR's stamp is read as on a chart with <paramref name="barSeconds"/>
        /// between bars: its own calendar date (<see cref="BarDate"/>) for daily bars and
        /// coarser, the user's local clock (<see cref="ToDisplay"/>) for intraday ones.
        /// </summary>
        public static DateTime ToBarDisplay(DateTime stamp, int barSeconds)
            => barSeconds >= SecondsPerDay ? BarDate(stamp) : ToDisplay(stamp);

        /// <summary><see cref="ToBarDisplay(DateTime, int)"/> in a zone named explicitly rather than
        /// inherited from the machine — so a test proves the rule on a UTC build agent too.</summary>
        internal static DateTime ToBarDisplay(DateTime stamp, int barSeconds, TimeZoneInfo zone)
        {
            if (barSeconds >= SecondsPerDay) return BarDate(stamp);
            var utc = stamp.Kind == DateTimeKind.Local ? stamp.ToUniversalTime() : DateTime.SpecifyKind(stamp, DateTimeKind.Utc);
            return TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        }

        /// <summary>
        /// The calendar date a daily-or-coarser bar is for: its UTC calendar date, at 00:00,
        /// Kind Unspecified (a date, not an instant — nothing downstream may convert it again).
        ///
        /// <para>Every provider's daily stamp falls on the date it is for: crypto venues and
        /// Tradier, Twelve Data, FMP and CSV dates at 00:00Z; Alpaca and Polygon at midnight
        /// New York (04:00Z/05:00Z); Schwab at midnight Chicago. Oanda's DEFAULT day (17:00 New
        /// York) stamped the bar for the 9th at 21:00Z on the 8th — the one that did not — and
        /// is requested on UTC days instead (OandaProvider.FetchOhlcvAsync), which also lines
        /// its bars up with the live bucket.</para>
        /// </summary>
        public static DateTime BarDate(DateTime stamp)
        {
            var utc = stamp.Kind == DateTimeKind.Local ? stamp.ToUniversalTime() : stamp;
            return DateTime.SpecifyKind(utc.Date, DateTimeKind.Unspecified);
        }

        /// <summary>Formats a bar's stamp by the bar rule (<see cref="ToBarDisplay"/>).</summary>
        public static string FormatBar(DateTime stamp, int barSeconds, string format)
            => ToBarDisplay(stamp, barSeconds).ToString(format, CultureInfo.InvariantCulture);

        /// <summary>Formats a stamp in the user's zone with an invariant culture.</summary>
        public static string Format(DateTime stamp, string format)
            => ToDisplay(stamp).ToString(format, CultureInfo.InvariantCulture);

        /// <summary>Time of day in the user's zone — "14:30".</summary>
        public static string FormatTime(DateTime stamp) => Format(stamp, TimeFormat);

        /// <summary>Date with year in the user's zone — "August 27 2026".</summary>
        public static string FormatLongDate(DateTime stamp) => Format(stamp, LongDateFormat);

        /// <summary>A day, in seconds — the line between "this chart is intraday" and not.</summary>
        private const int SecondsPerDay = 86400;

        /// <summary>
        /// Seconds between adjacent bars, inferred from a run of <paramref name="count"/> of them.
        /// A fallback for callers that have the range but not the chart's timeframe; anyone who
        /// has the state should pass <c>PlaybackNarration.BarSeconds</c> instead, which reads the
        /// declared timeframe first.
        /// </summary>
        public static int SpacingOf(DateTime start, DateTime end, int count)
            => count > 1 ? Math.Max(1, (int)((end - start).TotalSeconds / (count - 1))) : SecondsPerDay;

        /// <summary>
        /// The stamp a PER-BAR announcement carries: the time of day when bars are closer
        /// together than a day, the date when they are not.
        ///
        /// <para>
        /// Reported by Cody, 2026-09-05: on a one-minute chart the bar-close announcement named
        /// no time at all, so a run of them in the journal was a column of prices with nothing to
        /// say which minute each belonged to. The date is the wrong unit there — it does not
        /// change for hours — and the time is the wrong unit on a daily chart, where every bar
        /// would be "00:00".
        /// </para>
        /// </summary>
        public static string FormatBarClock(DateTime stamp, int barSeconds)
            => FormatBar(stamp, barSeconds, barSeconds < SecondsPerDay ? TimeFormat : LongDateFormat);

        /// <summary>
        /// A range of bars — "January 5 2024 to March 15 2024", or on an intraday chart
        /// "September 5 2026, 14:32 to 15:22".
        ///
        /// <para>
        /// The same report, from the other side: <i>"it just says from september 5 2026 to
        /// september 5 2026"</i>. Every viewport on a one-minute chart named one date twice,
        /// which is not a range at all. The date is spoken once when both ends fall on it.
        /// </para>
        /// </summary>
        public static string FormatBarRange(DateTime start, DateTime end, int barSeconds)
        {
            if (barSeconds >= SecondsPerDay)
                return $"{FormatBar(start, barSeconds, LongDateFormat)} to {FormatBar(end, barSeconds, LongDateFormat)}";

            return ToDisplay(start).Date == ToDisplay(end).Date
                ? $"{FormatLongDate(start)}, {FormatTime(start)} to {FormatTime(end)}"
                : $"{FormatLongDate(start)} {FormatTime(start)} to {FormatLongDate(end)} {FormatTime(end)}";
        }
    }
}
