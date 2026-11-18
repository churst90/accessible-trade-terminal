using System.Globalization;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>
    /// The words for market events: the short clause after a bar reading, and the full section
    /// the detail key reads.
    ///
    /// <para>
    /// <b>The "time not given" rule.</b> A day-only event says "time not given" only when a time
    /// could have existed and the record does not carry it: a US release or Fed decision whose
    /// source gives dates only, and a Bitcoin halving (its moment depends on block times). An
    /// expiry or a market holiday is a whole-day fact by nature, so it says nothing about a time.
    /// That is <see cref="MarketEvent.TimeCouldBeKnownButIsNot"/>, decided once on the record.
    /// </para>
    /// </summary>
    public static class MarketEventSpeech
    {
        /// <summary>Seven days in seconds — from here on a bar holds more than one day's events.</summary>
        private const int WeekSeconds = 7 * 86400;

        /// <summary>
        /// The clause spoken after a bar's reading. Empty when the level is off, nothing on the
        /// bar passes the settings, or the bar has no placed event.
        /// </summary>
        public static string Clause(
            IReadOnlyList<MarketEventBarPlacement> placements,
            int barSeconds,
            MarketEventLevel level,
            MarketEventKindFilter kinds,
            MarketType market)
        {
            if (level == MarketEventLevel.Off) return "";

            var passing = placements
                .Where(p => p.IsPlaced
                    && p.Event.Touches(market)
                    && MarketEventLevels.Includes(level, p.Event.Importance)
                    && kinds.Includes(p.Event.Kind))
                .ToList();
            if (passing.Count == 0) return "";

            passing.Sort((a, b) =>
            {
                int byImportance = a.Event.Importance.CompareTo(b.Event.Importance);
                if (byImportance != 0) return byImportance;
                int byMoment = a.Event.MomentUtc.CompareTo(b.Event.MomentUtc);
                if (byMoment != 0) return byMoment;
                return string.CompareOrdinal(a.Event.SpokenName, b.Event.SpokenName);
            });

            bool weeklyOrLonger = barSeconds >= WeekSeconds;

            if (passing.Count == 1)
                return SingleClause(passing[0], weeklyOrLonger);

            int shown = Math.Min(2, passing.Count);
            var parts = new List<string>(shown + 1);
            for (int i = 0; i < shown; i++)
                parts.Add(ShortName(passing[i].Event, weeklyOrLonger, includeTime: !weeklyOrLonger));

            int more = passing.Count - shown;
            if (more > 0)
                parts.Add($"plus {more} more event{(more == 1 ? "" : "s")}");
            return string.Join(", ", parts);
        }

        private static string SingleClause(MarketEventBarPlacement placement, bool weeklyOrLonger)
        {
            var e = placement.Event;
            var text = ShortName(e, weeklyOrLonger, includeTime: true);

            // A holiday's clause already names the weekday it closed the market on, so the
            // generic "came out Thursday" would say the same thing twice.
            if (e.Kind == MarketEventKind.Holiday) return text;
            if (!string.IsNullOrEmpty(placement.Placement))
                text += ", " + placement.Placement;
            return text;
        }

        private static string ShortName(MarketEvent e, bool withWeekday, bool includeTime)
        {
            if (e.Kind == MarketEventKind.Holiday)
                return $"market closed {Weekday(e.Date)}, {e.SpokenName}";

            string text = e.SpokenName;
            if (withWeekday) text += " " + Weekday(e.Date);
            if (includeTime)
            {
                string time = TimeText(e);
                if (time == "time not given") text += ", " + time;
                else if (time.Length > 0) text += " " + time;
            }
            return text;
        }

        /// <summary>The time on the user's clock, or the honest absence of one.</summary>
        public static string TimeText(MarketEvent e)
        {
            if (e.Precision == MarketEventPrecision.Exact)
                return SpeechTimeFormatter.FormatTime(e.MomentUtc);
            return e.TimeCouldBeKnownButIsNot ? "time not given" : "";
        }

        /// <summary>
        /// The detail key's section. Empty when this bar has no passing event, so the caller adds
        /// no empty section.
        /// </summary>
        public static string Detail(
            IReadOnlyList<MarketEventBarPlacement> placements,
            int barIndex,
            Ohlcv barAsDrawn,
            MarketEventKindFilter kinds,
            MarketType market)
        {
            var here = placements
                .Where(p => p.IsPlaced && p.BarIndex == barIndex
                    && p.Event.Touches(market) && kinds.Includes(p.Event.Kind))
                .ToList();
            if (here.Count == 0) return "";

            here.Sort((a, b) =>
            {
                int byImportance = a.Event.Importance.CompareTo(b.Event.Importance);
                if (byImportance != 0) return byImportance;
                return a.Event.MomentUtc.CompareTo(b.Event.MomentUtc);
            });

            var parts = new List<string>(here.Count + 2)
            {
                "Events on this bar."
            };
            foreach (var p in here)
            {
                var e = p.Event;
                string time = TimeText(e);
                if (time.Length == 0) time = "day only";
                parts.Add(
                    $"{e.LongName}, {time}, {ImportanceWord(e.Importance)} importance, "
                    + $"{p.Placement ?? "on this bar"}, {e.Source}.");
            }
            parts.Add(PriceSentence(barAsDrawn));
            return string.Join(" ", parts);
        }

        /// <summary>"price rose 1.8% over this bar", "fell", or "was flat", open to close as drawn.</summary>
        public static string PriceSentence(Ohlcv bar)
        {
            double open = bar.Open, close = bar.Close;
            if (!double.IsFinite(open) || !double.IsFinite(close) || open <= 0)
                return "Price was flat over this bar.";
            double pct = (close - open) / open * 100.0;
            if (Math.Abs(pct) < 0.05)
                return "Price was flat over this bar.";
            string word = pct > 0 ? "rose" : "fell";
            return $"Price {word} {Math.Abs(pct).ToString("0.0", CultureInfo.InvariantCulture)}% over this bar.";
        }

        private static string ImportanceWord(MarketEventImportance importance) => importance switch
        {
            MarketEventImportance.High => "high",
            MarketEventImportance.Medium => "medium",
            _ => "low",
        };

        private static string Weekday(DateOnly day)
            => CultureInfo.InvariantCulture.DateTimeFormat.GetDayName(day.DayOfWeek);
    }
}
