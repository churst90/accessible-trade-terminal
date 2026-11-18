using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>
    /// The events that need no file: options expiries, US market holidays and Bitcoin halvings,
    /// worked out by rule for any year range asked.
    ///
    /// <para>
    /// The holidays are NOT re-derived here. <see cref="CipherCFutureClock.Holiday"/> already
    /// knows the exchange's observance rules (a Saturday New Year is kept on the 1st, a Sunday one
    /// on the Monday, Good Friday by Easter, and so on), so this uses it unchanged.
    /// </para>
    /// </summary>
    public static class MarketEventRules
    {
        internal static readonly IReadOnlyList<MarketType> StockScope =
            new[] { MarketType.Stock, MarketType.Index, MarketType.Options };

        internal static readonly IReadOnlyList<MarketType> CryptoScope =
            new[] { MarketType.Crypto };

        /// <summary>Every rule-worked event in a year that touches <paramref name="market"/>.</summary>
        public static IReadOnlyList<MarketEvent> ForYear(int year, MarketType market)
        {
            var all = new List<MarketEvent>();
            if (StockScope.Contains(market))
            {
                all.AddRange(StockExpiries(year));
                all.AddRange(Holidays(year));
            }
            if (CryptoScope.Contains(market))
            {
                all.AddRange(CryptoExpiries(year));
                all.AddRange(Halvings());
            }
            return all;
        }

        /// <summary>Every rule-worked event in a year, whatever the market.</summary>
        public static IReadOnlyList<MarketEvent> ForYear(int year)
        {
            var all = new List<MarketEvent>(StockExpiries(year));
            all.AddRange(CryptoExpiries(year));
            all.AddRange(Holidays(year));
            all.AddRange(Halvings());
            return all;
        }

        /// <summary>
        /// The third Friday of the month, or the Thursday before when that Friday is Good Friday.
        /// March, June, September and December are the quarterly expiries.
        /// </summary>
        public static IReadOnlyList<MarketEvent> StockExpiries(int year)
        {
            var events = new List<MarketEvent>(12);
            var goodFriday = CipherCFutureClock.Holiday(3, year);
            for (int month = 1; month <= 12; month++)
            {
                var day = Nth(year, month, DayOfWeek.Friday, 3);
                if (day == goodFriday) day = day.AddDays(-1);
                bool quarterly = month is 3 or 6 or 9 or 12;
                events.Add(new MarketEvent(
                    Id: $"stock-expiry-{day:yyyy-MM-dd}",
                    Kind: MarketEventKind.Expiry,
                    SpokenName: quarterly ? "quarterly options expiry" : "options expiry",
                    LongName: quarterly ? "Quarterly stock options expiry" : "Stock options expiry",
                    Scope: StockScope,
                    Importance: quarterly ? MarketEventImportance.Medium : MarketEventImportance.Low,
                    MomentUtc: EasternMidnight(day),
                    Precision: MarketEventPrecision.DayOnly,
                    Date: day,
                    Source: "Rule: third Friday of the month, Thursday when that Friday is Good Friday"));
            }
            return events;
        }

        /// <summary>The last Friday of each month at 08:00 UTC, quarterly in March, June, September and December.</summary>
        public static IReadOnlyList<MarketEvent> CryptoExpiries(int year)
        {
            var events = new List<MarketEvent>(12);
            for (int month = 1; month <= 12; month++)
            {
                var day = Last(year, month, DayOfWeek.Friday);
                bool quarterly = month is 3 or 6 or 9 or 12;
                var moment = new DateTime(year, month, day.Day, 8, 0, 0, DateTimeKind.Utc);
                events.Add(new MarketEvent(
                    Id: $"crypto-expiry-{day:yyyy-MM-dd}",
                    Kind: MarketEventKind.Expiry,
                    SpokenName: quarterly ? "quarterly crypto options expiry" : "crypto options expiry",
                    LongName: quarterly ? "Quarterly crypto options expiry" : "Crypto options expiry",
                    Scope: CryptoScope,
                    Importance: quarterly ? MarketEventImportance.Medium : MarketEventImportance.Low,
                    MomentUtc: moment,
                    Precision: MarketEventPrecision.Exact,
                    Date: DateOnly.FromDateTime(moment),
                    Source: "Rule: last Friday of the month at 08:00 UTC"));
            }
            return events;
        }

        /// <summary>
        /// US market holidays, named as <see cref="CipherCFutureClock.HolidayNames"/> names them.
        /// Juneteenth only from 2022 — the exchanges first closed for it that year.
        /// </summary>
        public static IReadOnlyList<MarketEvent> Holidays(int year)
        {
            var events = new List<MarketEvent>();
            for (int h = 0; h < CipherCFutureClock.HolidayNames.Length; h++)
            {
                if (h == 5 && year < 2022) continue;
                var day = CipherCFutureClock.Holiday(h, year);
                string name = CipherCFutureClock.HolidayNames[h];
                events.Add(new MarketEvent(
                    Id: $"holiday-{day:yyyy-MM-dd}",
                    Kind: MarketEventKind.Holiday,
                    SpokenName: name,
                    LongName: $"US market closed for {name}",
                    Scope: StockScope,
                    Importance: MarketEventImportance.Low,
                    MomentUtc: EasternMidnight(day),
                    Precision: MarketEventPrecision.DayOnly,
                    Date: day,
                    Source: "Rule: CipherCFutureClock.Holiday"));
            }
            return events;
        }

        /// <summary>The four past Bitcoin halvings, UTC dates. A future one depends on block times and is a later stage.</summary>
        public static IReadOnlyList<MarketEvent> Halvings() => new[]
        {
            Halving(new DateOnly(2012, 11, 28)),
            Halving(new DateOnly(2016, 7, 9)),
            Halving(new DateOnly(2020, 5, 11)),
            Halving(new DateOnly(2024, 4, 20)),
        };

        private static MarketEvent Halving(DateOnly day) => new(
            Id: $"halving-{day:yyyy-MM-dd}",
            Kind: MarketEventKind.Halving,
            SpokenName: "Bitcoin halving",
            LongName: "Bitcoin halving",
            Scope: CryptoScope,
            Importance: MarketEventImportance.High,
            MomentUtc: new DateTime(day.Year, day.Month, day.Day, 0, 0, 0, DateTimeKind.Utc),
            Precision: MarketEventPrecision.DayOnly,
            Date: day,
            Source: "Known Bitcoin halving dates");

        private static DateTime EasternMidnight(DateOnly day)
            => TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified),
                ExchangeTime.UsEastern);

        private static DateOnly Nth(int year, int month, DayOfWeek weekday, int nth)
        {
            var day = new DateOnly(year, month, 1);
            while (day.DayOfWeek != weekday) day = day.AddDays(1);
            return day.AddDays(7 * (nth - 1));
        }

        private static DateOnly Last(int year, int month, DayOfWeek weekday)
        {
            var day = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
            while (day.DayOfWeek != weekday) day = day.AddDays(-1);
            return day;
        }
    }
}
