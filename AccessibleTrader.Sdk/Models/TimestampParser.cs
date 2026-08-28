using System.Globalization;

namespace AccessibleTrader.Sdk.Models
{
    public static class TimestampParser
    {
        /// <summary>
        /// The value returned when nothing usable could be read.
        ///
        /// <para>
        /// It is <see cref="DateTime.MinValue"/> stamped UTC, and it is a CONSTANT. The two
        /// failure paths used to return <c>DateTime.MinValue.ToUniversalTime()</c>, which
        /// converts from the machine's local zone: on a US-Eastern box the "invalid" sentinel
        /// came back as 0001-01-01T05:00:00Z, and west of Greenwich it cannot be represented at
        /// all so the conversion clamps. A sentinel whose value depends on where the terminal is
        /// running cannot be compared against, which is exactly what callers do with it.
        /// </para>
        /// </summary>
        public static readonly DateTime Invalid = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

        public static DateTime Parse(object? timestampObj) => Parse(timestampObj, TimeZoneInfo.Local);

        /// <summary>
        /// <see cref="Parse(object?)"/> with the zone a <see cref="DateTimeKind.Local"/> value is
        /// read against supplied explicitly.
        ///
        /// <para>
        /// The zone is a PARAMETER rather than a settable static on purpose. Every build agent
        /// and this workstation run UTC, so a test that only compares this method against
        /// <c>ToUniversalTime()</c> agrees vacuously and would pass against the very bug it is
        /// meant to catch. A test needs a fixed non-zero offset to say anything — and a
        /// process-wide "current zone" hook to give it one is exactly the shared mutable state
        /// that leaked across test collections the last time one was introduced.
        /// </para>
        /// </summary>
        internal static DateTime Parse(object? timestampObj, TimeZoneInfo localZone)
        {
            if (timestampObj == null) return Invalid;

            // Handle direct DateTime.
            //
            // A Local DateTime must be CONVERTED, not relabelled. Newtonsoft's JObject yields
            // exactly that for an ISO string carrying a non-zero offset under its default
            // handling, and SpecifyKind(dt, Utc) stamped "UTC" onto a local wall-clock reading —
            // so on a US-Eastern box every such bar landed 4-5 hours in the FUTURE rather than
            // being corrected back. Unspecified is still treated as UTC: that is the venue
            // convention this fleet reads, and there is nothing better to assume.
            if (timestampObj is DateTime dt)
                return dt.Kind == DateTimeKind.Local
                    // Unspecified, because ConvertTimeToUtc refuses a Local value against any
                    // zone other than TimeZoneInfo.Local — which is the whole point of passing
                    // one in. The Kind has already told us how to read it.
                    ? TimeZoneInfo.ConvertTimeToUtc(
                          DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), localZone)
                    : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            if (timestampObj is DateTimeOffset dto) return dto.UtcDateTime;

            string tsStr = timestampObj.ToString() ?? "";
            
            // Try ISO 8601 parsing first if it looks like a date string
            if (tsStr.Contains("T") || tsStr.Contains("-"))
            {
                // Invariant, not null: null means CurrentCulture — under th-TH that reads the
                // year as Buddhist-era, and every venue in this repo speaks ISO/Gregorian.
                if (DateTime.TryParse(tsStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsedDt))
                {
                    return parsedDt;
                }
            }

            // Fractional unix SECONDS ("1622505600.000000000" — OANDA's UNIX
            // datetime format). Must run before the integer branch, which
            // cannot parse the fraction at all.
            if (tsStr.Contains('.') &&
                double.TryParse(tsStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double fractional))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds((long)(fractional * 1000)).UtcDateTime;
            }

            // Fallback to Unix timestamp heuristic
            if (long.TryParse(tsStr, out long ts))
            {
                // If it's less than 10,000,000,000 (Nov 20, 2286), it's highly likely seconds.
                // Otherwise, it's milliseconds (or microseconds, but ms is the standard high-precision tier).
                if (ts < 10000000000L)
                {
                    return DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
                }
                else
                {
                    // Milliseconds is the standard high-precision tier, but some venues publish
                    // microseconds (16 digits) and some nanoseconds (19 digits). The old code
                    // claimed in its comment to handle both and divided by 1000 exactly ONCE, so
                    // a nanosecond epoch (~1.75e18) became ~1.75e15, was read as milliseconds,
                    // and dated the bar to roughly the year 57000. Step down until the value is
                    // in the millisecond range rather than assuming which tier it came from.
                    //
                    // The threshold is 1e14 ms — 5138-11-16 — comfortably above any real bar
                    // date and comfortably below the smallest microsecond epoch (~1.6e15).
                    const long MaxPlausibleMilliseconds = 100_000_000_000_000L;
                    while (ts > MaxPlausibleMilliseconds) ts /= 1000;

                    return DateTimeOffset.FromUnixTimeMilliseconds(ts).UtcDateTime;
                }
            }

            return Invalid;
        }
    }
}
