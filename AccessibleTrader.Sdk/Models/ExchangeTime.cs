using System;

namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Shared US-Eastern exchange-time helpers. US equity/options venues (and
    /// the data vendors that mirror them — Tradier <c>timesales</c>, FMP intraday)
    /// express wall-clock times in America/New_York with no offset. Converting
    /// those correctly is a recurring source of off-by-4/5-hours bugs, so the
    /// conversion lives in ONE place rather than being hand-rolled per provider.
    ///
    /// For epoch-based timestamps (which are timezone-independent) use
    /// <see cref="TimestampParser"/> instead — Eastern conversion is only needed
    /// for naive wall-clock strings and for building Eastern-interpreted request
    /// windows.
    /// </summary>
    public static class ExchangeTime
    {
        /// <summary>US-Eastern (America/New_York) with DST, resolved once and
        /// cross-platform (IANA id on Linux/macOS, Windows id as a fallback).</summary>
        public static readonly TimeZoneInfo UsEastern = ResolveEastern();

        private static TimeZoneInfo ResolveEastern()
        {
            foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            // Last resort: never throw at startup. Callers degrade to UTC-as-Eastern
            // (a bounded few-hours error) rather than crashing the provider.
            return TimeZoneInfo.Utc;
        }

        /// <summary>
        /// Interprets <paramref name="easternWallClock"/> as a naive US-Eastern
        /// wall-clock time (its <see cref="DateTime.Kind"/> is ignored) and returns
        /// the equivalent UTC instant. Use for vendor timestamps like FMP intraday
        /// "2024-01-02 09:30:00" that are Eastern with no offset.
        /// </summary>
        public static DateTime EasternWallClockToUtc(DateTime easternWallClock)
        {
            var unspecified = DateTime.SpecifyKind(easternWallClock, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, UsEastern);
        }

        /// <summary>
        /// Renders a UTC instant as a US-Eastern wall-clock string for a request
        /// parameter that the vendor interprets in Eastern (e.g. Tradier
        /// <c>timesales</c> start/end).
        /// </summary>
        public static string ToEasternString(DateTime utc, string format)
        {
            var asUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            var eastern = TimeZoneInfo.ConvertTimeFromUtc(asUtc, UsEastern);
            return eastern.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
