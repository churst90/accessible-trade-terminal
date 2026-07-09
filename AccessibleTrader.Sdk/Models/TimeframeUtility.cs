using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AccessibleTrader.Sdk.Models
{
    public static class StandardTimeframes
    {
        public const string OneMinute = "1m";
        public const string ThreeMinutes = "3m";
        public const string FiveMinutes = "5m";
        public const string FifteenMinutes = "15m";
        public const string ThirtyMinutes = "30m";
        public const string OneHour = "1h";
        public const string TwoHours = "2h";
        public const string FourHours = "4h";
        public const string SixHours = "6h";
        public const string EightHours = "8h";
        public const string TwelveHours = "12h";
        public const string OneDay = "1d";
        public const string ThreeDays = "3d";
        public const string OneWeek = "1w";
        public const string OneMonth = "1M";
    }

    public static class TimeframeUtility
    {
        private static readonly Regex _timeframeRegex = new Regex(@"^(\d+)([mhdMw])$", RegexOptions.Compiled);

        public static long ToMilliseconds(string tf)
        {
            if (string.IsNullOrEmpty(tf)) return 0;
            var match = _timeframeRegex.Match(tf);
            if (!match.Success) return 0;

            if (!long.TryParse(match.Groups[1].Value, out long value)) return 0;
            string unit = match.Groups[2].Value;

            return unit switch
            {
                "m" => value * 60000L,
                "h" => value * 3600000L,
                "d" => value * 86400000L,
                "w" => value * 604800000L,
                "M" => value * 2592000000L, // Approx 30 days
                _ => 0
            };
        }

        public static int ToSeconds(string tf) => (int)(ToMilliseconds(tf) / 1000);

        /// <summary>
        /// True when <paramref name="tf"/> is a well-formed timeframe token
        /// (digits + one of m/h/d/w/M) with a sane magnitude. Companion to
        /// <see cref="Sdk.Services.SymbolValidator"/> at the data-path choke
        /// point: timeframe strings are interpolated into provider URLs and
        /// drive period math, so malformed or absurd values are rejected
        /// before any request is built. 8 chars covers every real token
        /// ("10000m" is already far past anything a provider serves).
        /// </summary>
        public static bool IsValid(string? tf)
        {
            if (string.IsNullOrWhiteSpace(tf)) return false;
            if (tf.Length > 8) return false;
            return ToMilliseconds(tf) > 0;
        }

        public static List<string> AllTimeframes => new List<string>
        {
            StandardTimeframes.OneMinute, StandardTimeframes.ThreeMinutes, StandardTimeframes.FiveMinutes,
            StandardTimeframes.FifteenMinutes, StandardTimeframes.ThirtyMinutes, StandardTimeframes.OneHour,
            StandardTimeframes.TwoHours, StandardTimeframes.FourHours, StandardTimeframes.SixHours,
            StandardTimeframes.EightHours, StandardTimeframes.TwelveHours, StandardTimeframes.OneDay,
            StandardTimeframes.ThreeDays, StandardTimeframes.OneWeek, StandardTimeframes.OneMonth
        };

        public static string GetBestBaseTimeframe(string target, List<string> supported)
        {
            if (supported.Contains(target)) return target;

            long targetMs = ToMilliseconds(target);
            if (targetMs <= 0) return supported.FirstOrDefault() ?? StandardTimeframes.OneHour;

            // Find the largest supported timeframe that cleanly divides the target
            var candidates = supported
                .Select(s => new { TF = s, Ms = ToMilliseconds(s) })
                .Where(x => x.Ms > 0 && x.Ms < targetMs && targetMs % x.Ms == 0)
                .OrderByDescending(x => x.Ms)
                .ToList();

            return candidates.FirstOrDefault()?.TF ?? supported.OrderBy(s => ToMilliseconds(s)).FirstOrDefault() ?? StandardTimeframes.OneMinute;
        }

        public static DateTime GetPeriodStart(DateTime date, string timeframe)
        {
            if (string.IsNullOrEmpty(timeframe)) return date;
            
            var match = _timeframeRegex.Match(timeframe);
            if (!match.Success) return date;

            int value = int.Parse(match.Groups[1].Value);
            string unit = match.Groups[2].Value;

            if (unit == "M")
            {
                // Align to first of the month, zeroed time
                // E.g., for 3M, align to quarter start? For now align to 1st of month.
                int monthAdjust = (date.Month - 1) / value * value + 1;
                return new DateTime(date.Year, monthAdjust, 1, 0, 0, 0, DateTimeKind.Utc);
            }

            if (unit == "w")
            {
                // Align to Monday 00:00:00 UTC
                // Unix epoch (Jan 1, 1970) was a Thursday. Monday is Jan 5, 1970.
                long ms = ToMilliseconds(timeframe);
                long offsetMs = 4 * 86400000L; // Offset from Thu to Mon
                
                long ticks = new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
                long alignedMs = ((ticks - offsetMs) / ms) * ms + offsetMs;
                return DateTimeOffset.FromUnixTimeMilliseconds(alignedMs).UtcDateTime;
            }
            
            if (unit == "d")
            {
                long ms = ToMilliseconds(timeframe);
                long ticks = new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
                long alignedMs = (ticks / ms) * ms;
                return DateTimeOffset.FromUnixTimeMilliseconds(alignedMs).UtcDateTime;
            }

            // For hours and minutes, simple modulo of epoch works
            long msDefault = ToMilliseconds(timeframe);
            long ticksDefault = new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
            long alignedMsDefault = (ticksDefault / msDefault) * msDefault;
            return DateTimeOffset.FromUnixTimeMilliseconds(alignedMsDefault).UtcDateTime;
        }
    }
}
