using System.Globalization;
using AccessibleTrader.Core.Services.Accessibility;
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

        // ── The braille line ────────────────────────────────────────────────

        /// <summary>
        /// The same reading for a tactile strip, in list order: "SPX +0.4, BTC -1.2, 10Y 4.31 +3bp,
        /// Gold +0.2 4m, Oil n/a, VIX cl +2.1". Short names, signed changes, "cl" for a closed
        /// market, the age in minutes when it is old, "n/a" for no answer.
        /// </summary>
        public static string BrailleLine(IReadOnlyList<MarketPulseQuote> quotes)
        {
            if (quotes.Count == 0) return "Pulse empty";
            return string.Join(", ", quotes.Select(BrailleItem));
        }

        private static string BrailleItem(MarketPulseQuote quote)
        {
            string name = string.IsNullOrWhiteSpace(quote.Short) ? quote.Name : quote.Short!;
            string body = quote.Kind switch
            {
                MarketPulseKind.Unavailable => "n/a",
                MarketPulseKind.Closed => "cl " + BrailleChange(quote),
                _ => BrailleChange(quote),
            };
            if (quote.Kind is MarketPulseKind.Change or MarketPulseKind.Yield
                && quote.MinutesOld is { } minutes && minutes >= 1)
                body += $" {minutes}m";
            return $"{name} {body}";
        }

        private static string BrailleChange(MarketPulseQuote quote)
        {
            if (quote.IsYield)
            {
                int bp = (int)Math.Round(quote.Change, MidpointRounding.AwayFromZero);
                string sign = bp > 0 ? "+" : bp < 0 ? "-" : "";
                return $"{Level(quote)} {sign}{Math.Abs(bp)}bp";
            }
            double rounded = Math.Round(quote.Change, 1, MidpointRounding.AwayFromZero);
            string amount = Math.Abs(rounded).ToString("0.0", CultureInfo.InvariantCulture);
            return rounded > 0 ? "+" + amount : rounded < 0 ? "-" + amount : amount;
        }

        // ── The detail read ─────────────────────────────────────────────────

        /// <summary>
        /// Ctrl+Alt+P: the newest reading item by item — where each quote came from, its last
        /// price, the reference its change runs from, and how old it is.
        /// </summary>
        public static string Detail(IReadOnlyList<MarketPulseQuote> quotes, DateTime readUtc, DateTime nowUtc)
        {
            string head = $"Pulse read {Ago(nowUtc - readUtc)}.";
            if (quotes.Count == 0) return head + " " + EmptyList;
            return head + " " + string.Join(" ", quotes.Select(q => DetailItem(q, nowUtc)));
        }

        private static string DetailItem(MarketPulseQuote quote, DateTime nowUtc)
        {
            string source = !string.IsNullOrWhiteSpace(quote.Symbol) && !string.IsNullOrWhiteSpace(quote.Provider)
                ? $"{quote.Symbol} on {quote.Provider}"
                : "no source";
            string lead = $"{quote.Name}: {source}";

            if (quote.Kind == MarketPulseKind.Unavailable)
                return $"{lead}, unavailable, no usable quote within the four-second limit.";

            if (quote.Last is not { } last || quote.Reference is not { } reference)
                return $"{lead}, {Clause(quote, nowUtc)}.";

            string move = quote.IsYield
                ? YieldChange(quote.Change)
                : DetailPercent(quote.Change);

            if (quote.Kind == MarketPulseKind.Closed)
            {
                string day = DayLabel(quote.SessionDate, nowUtc);
                string session = day.Length > 0 ? $"last session {day}" : "last session";
                return $"{lead}, closed; {session} ended at {DetailLevel(quote, last)}, "
                     + $"{move} from {StartWords(quote.Start)}, {DetailLevel(quote, reference)}.";
            }

            string quoted = quote.QuotedUtc is { } at
                ? $"last minute bar ended {Ago(nowUtc - at)}"
                : "daily bars only";
            return $"{lead}, {DetailLevel(quote, last)}, {move} from {StartWords(quote.Start)} "
                 + $"{DetailLevel(quote, reference)}, {quoted}.";
        }

        private static string DetailLevel(MarketPulseQuote quote, double value)
            => quote.IsYield
                ? value.ToString("0.00", CultureInfo.InvariantCulture) + "%"
                : SpeechPriceFormatter.FormatPrice(value);

        /// <summary>Two decimals and the percent sign: the detail is where the precision lives.</summary>
        private static string DetailPercent(double change)
        {
            double rounded = Math.Round(change, 2, MidpointRounding.AwayFromZero);
            if (rounded == 0) return "flat";
            string amount = Math.Abs(rounded).ToString("0.00", CultureInfo.InvariantCulture) + "%";
            return rounded > 0 ? $"up {amount}" : $"down {amount}";
        }

        private static string StartWords(MarketPulseStart start) => start switch
        {
            MarketPulseStart.PreviousClose => "the previous close",
            MarketPulseStart.MidnightUtc => "midnight UTC",
            MarketPulseStart.Last24Hours => "24 hours ago",
            MarketPulseStart.SessionBefore => "the session before",
            _ => "its reference",
        };

        private static string Ago(TimeSpan age)
        {
            int minutes = (int)Math.Floor(Math.Max(0, age.TotalMinutes));
            if (minutes < 1) return "just now";
            if (minutes < 120) return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
            return $"{minutes / 60} hours ago";
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
