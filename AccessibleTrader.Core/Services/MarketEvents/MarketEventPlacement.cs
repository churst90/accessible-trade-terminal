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

        /// <summary>Every event, placed or not, in the order supplied.</summary>
        public static IReadOnlyList<MarketEventBarPlacement> Place(
            IReadOnlyList<MarketEvent> events,
            IReadOnlyList<Ohlcv> bars,
            int barSeconds,
            TimeZoneInfo zone,
            DateTime nowUtc)
        {
            var result = new List<MarketEventBarPlacement>(events.Count);
            foreach (var e in events)
                result.Add(PlaceOne(e, bars, Math.Max(1, barSeconds), zone, AsUtc(nowUtc)));
            return result;
        }

        private static MarketEventBarPlacement PlaceOne(
            MarketEvent e,
            IReadOnlyList<Ohlcv> bars,
            int barSeconds,
            TimeZoneInfo zone,
            DateTime nowUtc)
        {
            if (bars.Count == 0) return MarketEventBarPlacement.NotPlaced(e);

            bool intraday = barSeconds < DaySeconds;
            bool daily = barSeconds >= DaySeconds && barSeconds < WeekSeconds;
            bool weeklyOrLonger = barSeconds >= WeekSeconds;

            var eventDate = EventDate(e, zone);
            var firstDate = BarDate(bars[0].Date, barSeconds, zone);
            var lastDate = BarDate(bars[^1].Date, barSeconds, zone);

            // 8. Before the first bar — not placed. A date with no bar (rule 5) is a different
            // case: it belongs to a later bar, and only a date before the first one is dropped.
            if (eventDate < firstDate) return MarketEventBarPlacement.NotPlaced(e);

            if (intraday)
                return PlaceIntraday(e, bars, barSeconds, zone, eventDate, nowUtc);

            if (daily)
                return PlaceDaily(e, bars, barSeconds, zone, eventDate, nowUtc);

            if (weeklyOrLonger)
                return PlaceWeekly(e, bars, barSeconds, zone, eventDate, nowUtc);

            return MarketEventBarPlacement.NotPlaced(e);
        }

        /// <summary>Rules 1, 2, 5, 6 and 7 on a chart with bars shorter than a day.</summary>
        private static MarketEventBarPlacement PlaceIntraday(
            MarketEvent e,
            IReadOnlyList<Ohlcv> bars,
            int barSeconds,
            TimeZoneInfo zone,
            DateOnly eventDate,
            DateTime nowUtc)
        {
            var day = FindDayBars(bars, barSeconds, zone, eventDate);
            if (day.Count == 0)
            {
                // 5. A date with no bar (weekend, holiday, a gap in the data): the next bar,
                // worded with the day the event came out. A DateOnly event has no moment to
                // place inside a bar, so rule 6 sends it to the day's first bar too.
                int next = FirstBarAfter(bars, barSeconds, zone, eventDate);
                if (next < 0) return MarketEventBarPlacement.NotPlaced(e);
                return new MarketEventBarPlacement(
                    e, next, CameOut(eventDate));
            }

            int first = day[0], last = day[^1];

            // 6. A day-only event goes on the day's first bar, whatever its time of day.
            if (e.Precision == MarketEventPrecision.DayOnly)
                return Finish(e, bars, first, placement: null, nowUtc);

            // 2. Same day, outside the session: before the first bar, after the last one.
            var moment = AsUtc(e.MomentUtc);
            if (moment < AsUtc(bars[first].Date))
                return Finish(e, bars, first, BeforeTheOpen, nowUtc);

            var lastEnd = AsUtc(bars[last].Date).AddSeconds(barSeconds);
            if (moment >= lastEnd)
                return Finish(e, bars, last, AfterTheClose, nowUtc);

            // 1. The bar whose span [open, next open) holds the moment. A bar's opening moment
            // is INSIDE it: 8:30 is the bar that opens at 8:30, never the one before.
            int index = BarHolding(bars, barSeconds, moment);
            return Finish(e, bars, index, placement: null, nowUtc);
        }

        /// <summary>Rules 3, 5 and 7 on a daily chart.</summary>
        private static MarketEventBarPlacement PlaceDaily(
            MarketEvent e,
            IReadOnlyList<Ohlcv> bars,
            int barSeconds,
            TimeZoneInfo zone,
            DateOnly eventDate,
            DateTime nowUtc)
        {
            for (int i = 0; i < bars.Count; i++)
                if (BarDate(bars[i].Date, barSeconds, zone) == eventDate)
                    return Finish(e, bars, i, placement: null, nowUtc);

            // 5. A date with no bar: the next daily bar, worded with the day it came out.
            for (int i = 0; i < bars.Count; i++)
                if (BarDate(bars[i].Date, barSeconds, zone) > eventDate)
                    return new MarketEventBarPlacement(e, i, CameOut(eventDate));

            // 7. Past the end of the last bar's span: not placed (stage 3 reads upcoming events).
            return MarketEventBarPlacement.NotPlaced(e);
        }

        /// <summary>Rules 4, 5 and 7 on a weekly or monthly chart.</summary>
        private static MarketEventBarPlacement PlaceWeekly(
            MarketEvent e,
            IReadOnlyList<Ohlcv> bars,
            int barSeconds,
            TimeZoneInfo zone,
            DateOnly eventDate,
            DateTime nowUtc)
        {
            // 4. The bar whose span holds the date. The last bar's span is nominal: a week for a
            // weekly chart, a calendar month for a monthly one.
            for (int i = 0; i < bars.Count; i++)
            {
                var start = BarDate(bars[i].Date, barSeconds, zone);
                var end = i + 1 < bars.Count
                    ? BarDate(bars[i + 1].Date, barSeconds, zone)
                    : NominalEnd(start, barSeconds);
                if (eventDate >= start && eventDate < end)
                    return Finish(e, bars, i, placement: null, nowUtc);
            }

            // 5. In a gap between bars: the next bar, worded with the day it came out.
            for (int i = 0; i < bars.Count; i++)
                if (BarDate(bars[i].Date, barSeconds, zone) > eventDate)
                    return new MarketEventBarPlacement(e, i, CameOut(eventDate));

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
        private static int BarHolding(IReadOnlyList<Ohlcv> bars, int barSeconds, DateTime moment)
        {
            int lo = 0, hi = bars.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (AsUtc(bars[mid].Date) <= moment) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return found < 0 ? 0 : found;
        }

        private static List<int> FindDayBars(
            IReadOnlyList<Ohlcv> bars,
            int barSeconds,
            TimeZoneInfo zone,
            DateOnly day)
        {
            var found = new List<int>();
            for (int i = 0; i < bars.Count; i++)
                if (BarDate(bars[i].Date, barSeconds, zone) == day)
                    found.Add(i);
            return found;
        }

        private static int FirstBarAfter(
            IReadOnlyList<Ohlcv> bars,
            int barSeconds,
            TimeZoneInfo zone,
            DateOnly day)
        {
            for (int i = 0; i < bars.Count; i++)
                if (BarDate(bars[i].Date, barSeconds, zone) > day)
                    return i;
            return -1;
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
            => barSeconds >= DaySeconds
                ? DateOnly.FromDateTime(SpeechTimeFormatter.BarDate(stamp))
                : DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(stamp), zone));

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
