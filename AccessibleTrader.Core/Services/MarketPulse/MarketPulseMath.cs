using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Screening;

namespace AccessibleTrader.Core.Services.MarketPulse
{
    /// <summary>
    /// One pulse item, from bars to a quote. Pure: the caller supplies the clock and the bars,
    /// and the closed rules read US Eastern through the shared exchange-time resolver.
    /// </summary>
    public static class MarketPulseMath
    {
        private static readonly TimeSpan TwoMinutes = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan FifteenMinutes = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);
        private static readonly TimeSpan UsOpen = new(9, 30, 0);
        private static readonly TimeSpan UsClose = new(16, 0, 0);
        private static readonly TimeSpan ForexFridayClose = new(17, 0, 0);
        private static readonly TimeSpan ForexSundayOpen = new(17, 0, 0);

        /// <summary>An item with no usable answer, still carrying where it was asked.</summary>
        public static MarketPulseQuote Unavailable(WatchlistEntry entry)
            => Stamp(entry, Unavailable(MarketPulseDefaults.SpokenName(entry), MarketPulseDefaults.IsYield(entry)));

        public static MarketPulseQuote Quote(
            WatchlistEntry entry,
            IReadOnlyList<Ohlcv> dailyBars,
            IReadOnlyList<Ohlcv> intradayBars,
            DateTime nowUtc,
            PulseCryptoStart cryptoStart)
            => Stamp(entry, Answer(entry, dailyBars, intradayBars, nowUtc, cryptoStart));

        private static MarketPulseQuote Stamp(WatchlistEntry entry, MarketPulseQuote quote)
            => quote with
            {
                Short = MarketPulseDefaults.ShortName(entry),
                Provider = entry.Provider,
                Symbol = entry.Symbol,
            };

        private static MarketPulseQuote Answer(
            WatchlistEntry entry,
            IReadOnlyList<Ohlcv> dailyBars,
            IReadOnlyList<Ohlcv> intradayBars,
            DateTime nowUtc,
            PulseCryptoStart cryptoStart)
        {
            string name = MarketPulseDefaults.SpokenName(entry);
            bool isYield = MarketPulseDefaults.IsYield(entry);
            nowUtc = AsUtc(nowUtc);

            var daily = dailyBars
                .Where(b => b.Date != default && double.IsFinite(b.Close))
                .OrderBy(b => b.Date)
                .ToList();
            var intraday = intradayBars
                .Where(b => b.Date != default && double.IsFinite(b.Close))
                .OrderBy(b => b.Date)
                .ToList();

            if (daily.Count == 0)
                return Unavailable(name, isYield);

            if (entry.Market == MarketType.Crypto)
                return Crypto(entry, daily, intraday, nowUtc, cryptoStart, name, isYield);

            if (daily.Count < 2)
                return Unavailable(name, isYield);

            if (IsClosed(entry.Market, daily, intraday, nowUtc))
                return ClosedQuote(daily[^1], daily[^2], name, isYield);

            // While the newest daily bar is the day still being traded, the bar before it holds
            // the previous close. Otherwise the newest bar is the previous session: a Sunday
            // evening forex session has no bar yet, and some providers publish the day's bar only
            // after the close (IsClosed checked the minute bars are fresh).
            var newest = daily[^1];
            double reference = InProgress(newest, nowUtc) ? daily[^2].Close : newest.Close;

            double last = intraday.Count > 0 ? intraday[^1].Close : newest.Close;
            int? minutesOld = MinutesOld(intraday, nowUtc);
            return OpenQuote(name, isYield, last, reference, minutesOld)
                with { Start = MarketPulseStart.PreviousClose, QuotedUtc = QuotedAt(intraday) };
        }

        private static MarketPulseQuote Crypto(
            WatchlistEntry entry,
            IReadOnlyList<Ohlcv> daily,
            IReadOnlyList<Ohlcv> intraday,
            DateTime nowUtc,
            PulseCryptoStart start,
            string name,
            bool isYield)
        {
            double reference;
            double last;
            if (start == PulseCryptoStart.Last24Hours)
            {
                DateTime target = nowUtc.AddHours(-24);
                var nearest = daily
                    .OrderBy(b => Math.Abs((AsUtc(b.Date).Add(OneHour) - target).TotalSeconds))
                    .First();
                reference = nearest.Close;
                last = intraday.Count > 0 ? intraday[^1].Close : daily[^1].Close;
            }
            else
            {
                var newest = daily[^1];
                reference = InProgress(newest, nowUtc) ? newest.Open : newest.Close;
                last = intraday.Count > 0 ? intraday[^1].Close : newest.Close;
            }

            return OpenQuote(name, isYield, last, reference, MinutesOld(intraday, nowUtc)) with
            {
                Start = start == PulseCryptoStart.Last24Hours
                    ? MarketPulseStart.Last24Hours
                    : MarketPulseStart.MidnightUtc,
                QuotedUtc = QuotedAt(intraday),
            };
        }

        /// <summary>When the newest minute bar ended; null without minute bars.</summary>
        private static DateTime? QuotedAt(IReadOnlyList<Ohlcv> intraday)
            => intraday.Count > 0 ? AsUtc(intraday[^1].Date).AddMinutes(1) : null;

        private static MarketPulseQuote OpenQuote(
            string name, bool isYield, double last, double reference, int? minutesOld)
        {
            if (!double.IsFinite(reference) || reference == 0 || !double.IsFinite(last))
                return Unavailable(name, isYield);

            if (isYield)
            {
                // CBOE quotes TNX at ten times the yield. Above 20 is unambiguously the
                // ten-times form; below it is already a percentage.
                if (last > 20)
                {
                    last /= 10;
                    reference /= 10;
                }
                double basisPoints = Math.Round(
                    (last - reference) * 100, MidpointRounding.AwayFromZero);
                return new MarketPulseQuote(
                    name,
                    MarketPulseKind.Yield,
                    Change: basisPoints,
                    YieldLevel: last,
                    MinutesOld: minutesOld,
                    IsYield: true,
                    Last: last,
                    Reference: reference);
            }

            return new MarketPulseQuote(
                name,
                MarketPulseKind.Change,
                Change: (last - reference) / reference * 100,
                MinutesOld: minutesOld,
                Last: last,
                Reference: reference);
        }

        private static MarketPulseQuote ClosedQuote(
            Ohlcv newest, Ohlcv previous, string name, bool isYield)
        {
            var session = SessionDate(newest);
            if (isYield)
            {
                double last = newest.Close;
                double reference = previous.Close;
                if (!double.IsFinite(last) || !double.IsFinite(reference) || reference == 0)
                    return Unavailable(name, isYield);
                if (last > 20)
                {
                    last /= 10;
                    reference /= 10;
                }
                double basisPoints = Math.Round(
                    (last - reference) * 100, MidpointRounding.AwayFromZero);
                return new MarketPulseQuote(
                    name,
                    MarketPulseKind.Closed,
                    Change: basisPoints,
                    YieldLevel: last,
                    SessionDate: session,
                    IsYield: true,
                    Last: last,
                    Reference: reference,
                    Start: MarketPulseStart.SessionBefore);
            }

            if (!double.IsFinite(newest.Close) || !double.IsFinite(previous.Close)
                || previous.Close == 0)
                return Unavailable(name, isYield);

            return new MarketPulseQuote(
                name,
                MarketPulseKind.Closed,
                Change: (newest.Close - previous.Close) / previous.Close * 100,
                SessionDate: session,
                Last: newest.Close,
                Reference: previous.Close,
                Start: MarketPulseStart.SessionBefore);
        }

        private static MarketPulseQuote Unavailable(string name, bool isYield)
            => new(name, MarketPulseKind.Unavailable, IsYield: isYield);

        /// <summary>
        /// Stock, index, bond and the rest of the US-listed markets: Monday–Friday 09:30–16:00
        /// Eastern, and a minute bar more than fifteen minutes old during those hours means a
        /// holiday. Forex and commodities have their own Friday/Sunday window and never use the
        /// equity hours. Crypto is never closed.
        /// </summary>
        private static bool IsClosed(
            MarketType market,
            IReadOnlyList<Ohlcv> daily,
            IReadOnlyList<Ohlcv> intraday,
            DateTime nowUtc)
        {
            var eastern = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, ExchangeTime.UsEastern);
            var date = eastern.Date;
            var time = eastern.TimeOfDay;

            if (market is MarketType.Forex or MarketType.Commodity)
            {
                if (date.DayOfWeek == DayOfWeek.Saturday) return true;
                if (date.DayOfWeek == DayOfWeek.Friday && time >= ForexFridayClose) return true;
                if (date.DayOfWeek == DayOfWeek.Sunday && time < ForexSundayOpen) return true;
                return false;
            }

            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return true;
            if (time < UsOpen || time >= UsClose) return true;

            // In hours, the minute bars decide: a newest minute bar more than fifteen minutes old
            // is a holiday. Some providers publish the day's bar only after the close, so a
            // missing daily bar alone does not close the market; with no minute bars at all,
            // the day's bar must be in progress.
            if (intraday.Count > 0)
            {
                var end = AsUtc(intraday[^1].Date).AddMinutes(1);
                return nowUtc - end > FifteenMinutes;
            }

            return !InProgress(daily[^1], nowUtc);
        }

        private static int? MinutesOld(IReadOnlyList<Ohlcv> intraday, DateTime nowUtc)
        {
            if (intraday.Count == 0) return null;
            var end = AsUtc(intraday[^1].Date).AddMinutes(1);
            var age = nowUtc - end;
            if (age <= TwoMinutes) return null;
            return (int)Math.Floor(age.TotalMinutes);
        }

        /// <summary>
        /// True when <paramref name="bar"/> is the day still being traded: its open is at or
        /// before now and less than a day ago. Providers stamp a daily bar differently (00:00 UTC,
        /// midnight New York, the session open, or 17:00 New York the evening before for Oanda's
        /// forex), so the bar's UTC date alone is not "today".
        /// </summary>
        private static bool InProgress(Ohlcv bar, DateTime nowUtc)
        {
            var open = AsUtc(bar.Date);
            return open <= nowUtc && nowUtc - open < TimeSpan.FromDays(1);
        }

        /// <summary>
        /// The New York date of the session a daily bar describes, read half a day into the bar:
        /// right for bars stamped at 00:00 UTC, at midnight New York, at the session open, and at
        /// 17:00 New York the evening before.
        /// </summary>
        private static DateOnly SessionDate(Ohlcv bar)
            => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                AsUtc(bar.Date).AddHours(12), ExchangeTime.UsEastern));

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
