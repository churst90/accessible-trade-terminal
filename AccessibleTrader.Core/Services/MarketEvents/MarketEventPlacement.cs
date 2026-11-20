using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>One event on a bar, with the placement word that explains why that bar.</summary>
    public sealed record MarketEventBarPlacement(MarketEvent Event, int BarIndex, string? Placement)
    {
        public bool IsPlaced => BarIndex >= 0;
        public static MarketEventBarPlacement NotPlaced(MarketEvent e) => new(e, -1, "not placed");
    }

    /// <summary>
    /// The chart's time zone for placing events: UTC, or US Eastern for stock-session markets.
    ///
    /// <para>
    /// <see cref="Indicators.CipherCFutureClock"/> reaches the same answer by looking at the bars,
    /// but it needs a couple of hundred bars and it answers null for a chart that keeps no one
    /// time of day. The market is the fact the user chose, so it is the one used here:
    /// <see cref="ExchangeTime.UsEastern"/> for the US equity, options, futures and bond sessions
    /// (the code <c>CipherCFutureClock</c> uses), and UTC for everything else.
    /// </para>
    /// </summary>
    public static class MarketEventChartZone
    {
        public static TimeZoneInfo For(MarketType market) => market switch
        {
            MarketType.Stock or MarketType.Index or MarketType.Options
                or MarketType.Futures or MarketType.Bonds => ExchangeTime.UsEastern,
            _ => TimeZoneInfo.Utc,
        };
    }

    /// <summary>
    /// Puts an event on a bar by Cody's rule (2026-11-18): <b>the bar of the day the event happened
    /// on, as TradingView does.</b>
    ///
    /// <para>
    /// Pure and static on purpose: the caller supplies the bars, the bar length, the chart's zone
    /// and "now", so a test can pin a daylight-saving changeover or a replay prefix without a
    /// clock. The rules are numbered as in the handoff; the tests name the same numbers.
    /// </para>
    /// </summary>
    public static class MarketEventPlacement
    {
        public const string BeforeTheOpen = "before the open";
        public const string AfterTheClose = "after the close";
        public const string Scheduled = "scheduled";
        public const string NotPlacedWord = "not placed";
        public const int NotPlaced = -1;

        private const int DaySeconds = 86400;
        private const int WeekSeconds = 7 * DaySeconds;

        // Counts BarDate calls for the placement guard. ThreadStatic so parallel tests do not
        // share it; Place is synchronous, so one thread sees the whole call.
        [ThreadStatic] private static int _barDateCalls;

        internal static int BarDateCalls
        {
            get => _barDateCalls;
            set => _barDateCalls = value;
        }

        /// <summary>Every event, placed or not, in the order supplied.</summary>
        public static IReadOnlyList<MarketEventBarPlacement> Place(
            IReadOnlyList<MarketEvent> events,
            IReadOnlyList<Ohlcv> bars,
            int barSeconds,
            TimeZoneInfo zone,
            DateTime nowUtc)
        {
            barSeconds = Math.Max(1, barSeconds);
            nowUtc = AsUtc(nowUtc);
            var result = new List<MarketEventBarPlacement>(events.Count);
            if (bars.Count == 0)
            {
                foreach (var e in events) result.Add(MarketEventBarPlacement.NotPlaced(e));
                return result;
            }

            // Each bar's date and open is worked out ONCE, then every event finds its bars by
            // binary search. Bar dates are non-decreasing because the bars are chronological.
            var barDates = new DateOnly[bars.Count];
            var barOpens = new DateTime[bars.Count];
            for (int i = 0; i < bars.Count; i++)
            {
                barOpens[i] = AsUtc(bars[i].Date);
                barDates[i] = BarDate(barOpens[i], barSeconds, zone);
            }

            foreach (var e in events)
                result.Add(PlaceOne(e, bars, barDates, barOpens, barSeconds, zone, nowUtc));
            return result;
        }

        private static MarketEventBarPlacement PlaceOne(
            MarketEvent e,
            IReadOnlyList<Ohlcv> bars,
            DateOnly[] barDates,
            DateTime[] barOpens,
            int barSeconds,
            TimeZoneInfo zone,
            DateTime nowUtc)
        {
            bool intraday = barSeconds < DaySeconds;
            bool daily = barSeconds >= DaySeconds && barSeconds < WeekSeconds;
            bool weeklyOrLonger = barSeconds >= WeekSeconds;

            var eventDate = EventDate(e, zone);

            // 8. Before the first bar — not placed. A date with no bar (rule 5) is a different
            // case: it belongs to a later bar, and only a date before the first one is dropped.
            if (eventDate < barDates[0]) return MarketEventBarPlacement.NotPlaced(e);

            if (intraday)
                return PlaceIntraday(e, bars, barDates, barOpens, barSeconds, eventDate, nowUtc);

            if (daily)
                return PlaceDaily(e, bars, barDates, eventDate, nowUtc);

            if (weeklyOrLonger)
                return PlaceWeekly(e, bars, barDates, barSeconds, eventDate, nowUtc);

            return MarketEventBarPlacement.NotPlaced(e);
        }

        /// <summary>Rules 1, 2, 5, 6 and 7 on a chart with bars shorter than a day.</summary>
        private static MarketEventBarPlacement PlaceIntraday(
            MarketEvent e,
            IReadOnlyList<Ohlcv> bars,
            DateOnly[] barDates,
            DateTime[] barOpens,
            int barSeconds,
            DateOnly eventDate,
            DateTime nowUtc)
        {
            int first = LowerBound(barDates, eventDate);
            int last = UpperBound(barDates, eventDate) - 1;
            if (first > last)
            {
                // 5. A date with no bar (weekend, holiday, a gap in the data): the next bar,
                // worded with the day the event came out. A DateOnly event has no moment to
                // place inside a bar, so rule 6 sends it to the day's first bar too.
                int next = UpperBound(barDates, eventDate);
                if (next >= bars.Count) return MarketEventBarPlacement.NotPlaced(e);
                return new MarketEventBarPlacement(
                    e, next, CameOut(eventDate));
            }

            // 6. A day-only event goes on the day's first bar, whatever its time of day — unless
            // its source says which side of the session it falls (an earnings report "before the
            // open" or "after the close"): then the day's first or last bar, worded so.
            if (e.Precision == MarketEventPrecision.DayOnly)
                return e.Session switch
                {
                    MarketEventSession.BeforeOpen => Finish(e, bars, first, BeforeTheOpen, nowUtc),
                    MarketEventSession.AfterClose => Finish(e, bars, last, AfterTheClose, nowUtc),
                    _ => Finish(e, bars, first, placement: null, nowUtc),
                };

            // 2. Same day, outside the session: before the first bar, after the last one.
            var moment = AsUtc(e.MomentUtc);
            if (moment < barOpens[first])
                return Finish(e, bars, first, BeforeTheOpen, nowUtc);

            var lastEnd = barOpens[last].AddSeconds(barSeconds);
            if (moment >= lastEnd)
                return Finish(e, bars, last, AfterTheClose, nowUtc);

            // 1. The bar whose span [open, next open) holds the moment. A bar's opening moment
            // is INSIDE it: 8:30 is the bar that opens at 8:30, never the one before.
            int index = BarHolding(barOpens, moment);
            return Finish(e, bars, index, placement: null, nowUtc);
        }

        /// <summary>Rules 3, 5 and 7 on a daily chart.</summary>
        private static MarketEventBarPlacement PlaceDaily(
            MarketEvent e,
            IReadOnlyList<Ohlcv> bars,
            DateOnly[] barDates,
            DateOnly eventDate,
            DateTime nowUtc)
        {
            int index = LowerBound(barDates, eventDate);
            if (index < bars.Count && barDates[index] == eventDate)
                return Finish(e, bars, index, placement: null, nowUtc);

            // 5. A date with no bar: the next daily bar, worded with the day it came out.
            if (index < bars.Count)
                return new MarketEventBarPlacement(e, index, CameOut(eventDate));

            // 7. Past the end of the last bar's span: not placed (stage 3 reads upcoming events).
            return MarketEventBarPlacement.NotPlaced(e);
        }

        /// <summary>Rules 4, 5 and 7 on a weekly or monthly chart.</summary>
        private static MarketEventBarPlacement PlaceWeekly(
            MarketEvent e,
            IReadOnlyList<Ohlcv> bars,
            DateOnly[] barDates,
            int barSeconds,
            DateOnly eventDate,
            DateTime nowUtc)
        {
            // 4. The bar whose span holds the date. The last bar's span is nominal: a week for a
            // weekly chart, a calendar month for a monthly one.
            int i = UpperBound(barDates, eventDate) - 1;
            if (i >= 0)
            {
                var start = barDates[i];
                var end = i + 1 < bars.Count
                    ? barDates[i + 1]
                    : NominalEnd(start, barSeconds);
                if (eventDate >= start && eventDate < end)
                    return Finish(e, bars, i, placement: null, nowUtc);
            }

            // 5. In a gap between bars: the next bar, worded with the day it came out.
            int next = UpperBound(barDates, eventDate);
            if (next < bars.Count)
                return new MarketEventBarPlacement(e, next, CameOut(eventDate));

            return MarketEventBarPlacement.NotPlaced(e);
        }

        /// <summary>
        /// 7's second sentence: inside the last bar but later than "now" is placed, worded
        /// "scheduled". Anything already placed with a word of its own (after the close, came out
        /// Saturday) keeps that word — it says where the event is even if its moment is later.
        /// </summary>
        private static MarketEventBarPlacement Finish(
            MarketEvent e,
            IReadOnlyList<Ohlcv> bars,
            int index,
            string? placement,
            DateTime nowUtc)
        {
            if (placement == null && index == bars.Count - 1 && AsUtc(e.MomentUtc) > nowUtc)
                placement = Scheduled;
            return new MarketEventBarPlacement(e, index, placement);
        }

        /// <summary>The bar whose [open, next open) holds the moment; a bar's own open is inside it.</summary>
        private static int BarHolding(DateTime[] barOpens, DateTime moment)
        {
            int found = UpperBound(barOpens, moment) - 1;
            return found < 0 ? 0 : found;
        }

        /// <summary>The first index whose date is not before <paramref name="value"/>.</summary>
        private static int LowerBound(DateOnly[] dates, DateOnly value)
        {
            int lo = 0, hi = dates.Length;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (dates[mid] < value) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>The first index whose date is after <paramref name="value"/>.</summary>
        private static int UpperBound(DateOnly[] dates, DateOnly value)
        {
            int lo = 0, hi = dates.Length;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (dates[mid] <= value) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>The first index whose open is after <paramref name="value"/>.</summary>
        private static int UpperBound(DateTime[] opens, DateTime value)
        {
            int lo = 0, hi = opens.Length;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (opens[mid] <= value) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private static DateOnly EventDate(MarketEvent e, TimeZoneInfo zone)
            => e.Precision == MarketEventPrecision.Exact
                ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(e.MomentUtc), zone))
                : e.Date;

        /// <summary>
        /// The date a bar is FOR. Daily and coarser bars are named by their own date
        /// (<see cref="SpeechTimeFormatter.BarDate"/>), intraday bars by the chart-zone date of
        /// their open.
        /// </summary>
        private static DateOnly BarDate(DateTime stamp, int barSeconds, TimeZoneInfo zone)
        {
            _barDateCalls++;
            return barSeconds >= DaySeconds
                ? DateOnly.FromDateTime(SpeechTimeFormatter.BarDate(stamp))
                : DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(stamp), zone));
        }

        private static DateOnly NominalEnd(DateOnly start, int barSeconds)
            => barSeconds >= 28 * DaySeconds
                ? start.AddMonths(1)
                : start.AddDays(Math.Max(1, barSeconds / DaySeconds));

        private static string CameOut(DateOnly day)
            => "came out " + day.DayOfWeek;

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
