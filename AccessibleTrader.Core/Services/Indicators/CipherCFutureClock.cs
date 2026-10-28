using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Cipher C's bars past the last one, as the chart itself would print them: on the chart's
    /// trading days, at its session's time of day. Read from the chart's own recent bars, with no
    /// symbol or exchange table: bars stamped at one UTC time of day (crypto) step on in UTC; bars
    /// stamped at one US-Eastern time of day (09:30 for New York stocks, 18:00 for futures, whose
    /// Monday session is stamped on the Sunday) step on in US-Eastern time, so a summer bar is
    /// an hour earlier in UTC than a winter one. A US holiday is skipped when the chart has no bar
    /// on it the last time it fell on a weekday (the newest only: the Dow traded Juneteenth 2021,
    /// then closed for it). Anything else steps on at the last bar's time
    /// of day on weekdays, as before (2026-10-28).
    /// </summary>
    internal sealed class CipherCFutureClock
    {
        /// <summary>The recent bars the clock is read from.</summary>
        internal const int RecentBars = 260;

        /// <summary>The share of the recent bars that must keep their weekday's time of day in a zone.</summary>
        internal const double Agreement = 0.95;

        private readonly TimeZoneInfo _zone;
        private readonly TimeSpan _timeOfDay;
        private readonly Dictionary<DayOfWeek, TimeSpan> _times;
        private readonly int _shift;          // session date = local date + this many days
        private readonly bool _everyDay;
        private readonly HashSet<DateOnly> _closed = new();
        private readonly DateTime _last;

        /// <summary>The US holidays checked against the chart's bars.</summary>
        internal static readonly string[] HolidayNames =
            { "New Year", "Martin Luther King", "Presidents", "Good Friday", "Memorial", "Juneteenth", "Independence", "Labor", "Thanksgiving", "Christmas" };

        /// <summary>The US holidays the chart is closed on, by name.</summary>
        internal IReadOnlyList<string> Observed { get; }

        /// <summary>The chart's time zone: UTC, US-Eastern, or null when neither keeps one time of day.</summary>
        internal TimeZoneInfo? Zone { get; }

        internal CipherCFutureClock(ReadOnlySpan<Ohlcv> data, bool everyDay)
        {
            _everyDay = everyDay;
            _last = data[^1].Date;
            var recent = data[Math.Max(0, data.Length - RecentBars)..];
            var utc = Times(recent, TimeZoneInfo.Utc, out double utcShare);
            var eastern = Times(recent, ExchangeTime.UsEastern, out double easternShare);
            Zone = utcShare >= Agreement && utcShare >= easternShare ? TimeZoneInfo.Utc
                : easternShare >= Agreement ? ExchangeTime.UsEastern : null;
            _zone = Zone ?? TimeZoneInfo.Utc;
            _times = ReferenceEquals(Zone, ExchangeTime.UsEastern) ? eastern : Zone is null ? new() : utc;
            _timeOfDay = Local(_last).TimeOfDay;
            if (!everyDay)
            {
                bool sunday = false, friday = false;
                foreach (var b in recent)
                {
                    var d = Local(b.Date).DayOfWeek;
                    sunday |= d == DayOfWeek.Sunday; friday |= d == DayOfWeek.Friday;
                }
                _shift = sunday && !friday ? 1 : 0;
            }
            var observed = new List<string>();
            if (!everyDay && Zone is not null)
            {
                var have = new HashSet<DateOnly>();
                for (int i = 0; i < data.Length; i++) have.Add(Session(data[i].Date));
                var first = Session(data[0].Date); var lastDay = Session(_last);
                for (int h = 0; h < HolidayNames.Length; h++)
                {
                    int seen = 0; bool open = false;
                    for (int y = lastDay.Year; y >= first.Year && seen < 1; y--)
                    {
                        var day = Holiday(h, y);
                        if (day > lastDay || day < first || day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                        seen++; open |= have.Contains(day);
                    }
                    if (seen == 0 || open) continue;
                    observed.Add(HolidayNames[h]);
                    for (int y = lastDay.Year; y <= lastDay.Year + 12; y++) _closed.Add(Holiday(h, y));
                }
            }
            Observed = observed;
        }

        /// <summary>The index of the first bar stamped at or after <paramref name="target"/>,
        /// counting on from the last bar at <paramref name="last"/>.</summary>
        internal int BarOnOrAfter(DateTime target, int last)
        {
            int k = last;
            var day = Session(_last);
            var stamp = _last;
            while (stamp < target)
            {
                day = day.AddDays(1);
                if (!Trades(day)) continue;
                k++;
                stamp = Stamp(day);
            }
            return k;
        }

        /// <summary>The stamps of the next <paramref name="count"/> bars past the last one.</summary>
        internal IEnumerable<DateTime> Future(int count)
        {
            var day = Session(_last);
            for (int k = 0; k < count;)
            {
                day = day.AddDays(1);
                if (!Trades(day)) continue;
                k++;
                yield return Stamp(day);
            }
        }

        private bool Trades(DateOnly day)
            => _everyDay || (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !_closed.Contains(day));

        private DateTime Stamp(DateOnly session)
        {
            var date = session.AddDays(-_shift);
            var local = date.ToDateTime(TimeOnly.MinValue) + (_times.TryGetValue(date.DayOfWeek, out var t) ? t : _timeOfDay);
            if (ReferenceEquals(_zone, TimeZoneInfo.Utc)) return DateTime.SpecifyKind(local, _last.Kind);
            if (_zone.IsInvalidTime(local)) local = local.AddHours(1);
            return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _zone), _last.Kind);
        }

        private DateTime Local(DateTime utc)
            => ReferenceEquals(_zone, TimeZoneInfo.Utc) ? utc : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);

        private DateOnly Session(DateTime utc) => DateOnly.FromDateTime(Local(utc)).AddDays(_shift);

        /// <summary>Each weekday's most common time of day in <paramref name="zone"/>, and the share
        /// of <paramref name="bars"/> stamped at it.</summary>
        private static Dictionary<DayOfWeek, TimeSpan> Times(ReadOnlySpan<Ohlcv> bars, TimeZoneInfo zone, out double share)
        {
            var counts = new Dictionary<(DayOfWeek, TimeSpan), int>();
            foreach (var b in bars)
            {
                var at = ReferenceEquals(zone, TimeZoneInfo.Utc) ? b.Date : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(b.Date, DateTimeKind.Utc), zone);
                var key = (at.DayOfWeek, at.TimeOfDay);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
            var times = new Dictionary<DayOfWeek, TimeSpan>(); var best = new Dictionary<DayOfWeek, int>();
            foreach (var ((day, time), c) in counts)
                if (c > best.GetValueOrDefault(day)) { best[day] = c; times[day] = time; }
            share = bars.Length < 2 ? 0 : best.Values.Sum() / (double)bars.Length;
            return times;
        }

        /// <summary>The US holiday <paramref name="which"/> (an index into <see cref="HolidayNames"/>)
        /// in <paramref name="year"/>, on the weekday it is kept.</summary>
        internal static DateOnly Holiday(int which, int year) => which switch
        {
            0 => new DateOnly(year, 1, 1).DayOfWeek == DayOfWeek.Saturday ? new DateOnly(year, 1, 1) : Kept(new DateOnly(year, 1, 1)),
            1 => Nth(year, 1, DayOfWeek.Monday, 3),
            2 => Nth(year, 2, DayOfWeek.Monday, 3),
            3 => Easter(year).AddDays(-2),
            4 => LastMonday(year, 5),
            5 => Kept(new DateOnly(year, 6, 19)),
            6 => Kept(new DateOnly(year, 7, 4)),
            7 => Nth(year, 9, DayOfWeek.Monday, 1),
            8 => Nth(year, 11, DayOfWeek.Thursday, 4),
            _ => Kept(new DateOnly(year, 12, 25)),
        };

        private static DateOnly Kept(DateOnly d) => d.DayOfWeek switch
        {
            DayOfWeek.Saturday => d.AddDays(-1),
            DayOfWeek.Sunday => d.AddDays(1),
            _ => d,
        };

        private static DateOnly Nth(int year, int month, DayOfWeek day, int nth)
        {
            var d = new DateOnly(year, month, 1);
            while (d.DayOfWeek != day) d = d.AddDays(1);
            return d.AddDays(7 * (nth - 1));
        }

        private static DateOnly LastMonday(int year, int month)
        {
            var d = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
            while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(-1);
            return d;
        }

        private static DateOnly Easter(int y)
        {
            int a = y % 19, b = y / 100, c = y % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
            int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7;
            int m = (a + 11 * h + 22 * l) / 451, month = (h + l - 7 * m + 114) / 31, day = (h + l - 7 * m + 114) % 31 + 1;
            return new DateOnly(y, month, day);
        }
    }
}
