using System.Globalization;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.MarketPulse
{
    /// <summary>
    /// One spoken sentence, in list order. The words are the product: a change is "up 0.4" with
    /// no percent sign, a yield is "4.31, up 3 basis points", and a closed market names the
    /// session it describes.
    /// </summary>
    public static class MarketPulseWording
    {
        public const string EmptyList =
            "Your Pulse list is empty. Add markets to it in the watchlist screen, Alt+M.";

        public static string Sentence(IReadOnlyList<MarketPulseQuote> quotes, DateTime nowUtc)
        {
            if (quotes.Count == 0) return EmptyList;

            var clauses = new List<string>(quotes.Count);
            foreach (var quote in quotes)
                clauses.Add(Clause(quote, nowUtc));

            return string.Join(", ", clauses) + ".";
        }

        private static string Clause(MarketPulseQuote quote, DateTime nowUtc)
        {
            switch (quote.Kind)
            {
                case MarketPulseKind.Unavailable:
                    return $"{quote.Name} unavailable";

                case MarketPulseKind.Closed:
                {
                    string day = DayLabel(quote.SessionDate, nowUtc);
                    if (quote.IsYield)
                        return $"{quote.Name} closed, {Level(quote)}, "
                             + $"{YieldChange(quote.Change)} {day}".TrimEnd();
                    return $"{quote.Name} closed, {PercentChange(quote.Change)} {day}".TrimEnd();
                }

                case MarketPulseKind.Yield:
                {
                    string clause = $"{quote.Name} {Level(quote)}, {YieldChange(quote.Change)}";
                    return WithAge(clause, quote.MinutesOld);
                }

                default:
                {
                    string clause = $"{quote.Name} {PercentChange(quote.Change)}";
                    return WithAge(clause, quote.MinutesOld);
                }
            }
        }

        private static string WithAge(string clause, int? minutesOld)
        {
            if (minutesOld is not { } minutes || minutes < 1) return clause;
            return $"{clause}, {minutes} {(minutes == 1 ? "minute" : "minutes")} old";
        }

        private static string Level(MarketPulseQuote quote)
            => (quote.YieldLevel ?? 0).ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>One decimal, invariant culture, no percent word, no plus sign.</summary>
        private static string PercentChange(double change)
        {
            double rounded = Math.Round(change, 1, MidpointRounding.AwayFromZero);
            if (rounded == 0) return "flat";
            string amount = Math.Abs(rounded).ToString("0.0", CultureInfo.InvariantCulture);
            return rounded > 0 ? $"up {amount}" : $"down {amount}";
        }

        /// <summary>Whole basis points, with the singular where it belongs.</summary>
        private static string YieldChange(double basisPoints)
        {
            int rounded = (int)Math.Round(basisPoints, MidpointRounding.AwayFromZero);
            if (rounded == 0) return "flat";
            int amount = Math.Abs(rounded);
            string unit = amount == 1 ? "basis point" : "basis points";
            return rounded > 0 ? $"up {amount} {unit}" : $"down {amount} {unit}";
        }

        private static string DayLabel(DateOnly? session, DateTime nowUtc)
        {
            if (session is not { } date) return "";
            var easternNow = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), ExchangeTime.UsEastern);
            var today = DateOnly.FromDateTime(easternNow);
            if (date == today) return "today";
            if (date == today.AddDays(-1)) return "yesterday";
            return date.DayOfWeek.ToString();
        }
    }
}
