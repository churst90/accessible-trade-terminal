using System;
using System.Globalization;

namespace AccessibleTrader.Sdk.Models
{
    public static class TimestampParser
    {
        public static DateTime Parse(object? timestampObj)
        {
            if (timestampObj == null) return DateTime.MinValue.ToUniversalTime();

            // Handle direct DateTime
            if (timestampObj is DateTime dt) return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            if (timestampObj is DateTimeOffset dto) return dto.UtcDateTime;

            string tsStr = timestampObj.ToString() ?? "";
            
            // Try ISO 8601 parsing first if it looks like a date string
            if (tsStr.Contains("T") || tsStr.Contains("-"))
            {
                if (DateTime.TryParse(tsStr, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsedDt))
                {
                    return parsedDt;
                }
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
                    // Assuming milliseconds. If an exchange uses microseconds (e.g., 16 digit timestamps), 
                    // we would divide by 1000 here, but standard exchanges use ms for high precision.
                    if (ts > 100000000000000L) // Nano or Micro seconds sanity check (unlikely for historical bars, but safe)
                        ts /= 1000;
                        
                    return DateTimeOffset.FromUnixTimeMilliseconds(ts).UtcDateTime;
                }
            }

            return DateTime.MinValue.ToUniversalTime();
        }
    }
}
