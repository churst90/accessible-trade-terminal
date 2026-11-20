using System.Globalization;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>One rung of a Kalshi ladder: the strike and P(figure &gt; strike).</summary>
    public sealed record KalshiStrikeProbability(double Strike, double Probability);

    /// <summary>
    /// How one of our figure names maps onto Kalshi's series and strike grid.
    /// <see cref="NamesOwnMonth"/>: the ticker names the event's own month (a Fed meeting), not
    /// the month before it. <see cref="Settles"/>: the market's own result IS the figure (the Fed
    /// rate), so the decided value is read from the settled ladder, not from FRED.
    /// </summary>
    public sealed record KalshiFigureSpec(
        string Name,
        IReadOnlyList<string> Series,
        double Step,
        double StrikeScale,
        bool UsesReleaseDate,
        bool NamesOwnMonth = false,
        bool Settles = false);

    /// <summary>
    /// The Kalshi rules, pure and without I/O: the event tickers for a release, the close-date
    /// guard, one market's probability, and the median that turns a ladder into one expected
    /// figure.
    ///
    /// <para>
    /// <b>What the market means.</b> A market with <c>strike_type</c> "greater" is "the figure is
    /// greater than <c>floor_strike</c>", so its yes probability is P(figure &gt; strike). The
    /// expected figure is the smallest grid value v with P(X &gt; v) ≤ 0.5, found by interpolating
    /// where the ladder crosses one half and rounding up onto the release's own step. That is why
    /// strikes one step apart simply give the higher one.
    /// </para>
    /// </summary>
    public static class KalshiExpectedFigures
    {
        public const double MaxSpread = 0.10;

        private static readonly IReadOnlyDictionary<string, KalshiFigureSpec> Specs =
            new Dictionary<string, KalshiFigureSpec>(StringComparer.Ordinal)
            {
                ["CPI"] = new("CPI", new[] { "KXCPI", "CPI" }, 0.1, 1, false),
                ["core CPI"] = new("core CPI", new[] { "KXCPICORE", "CPICORE" }, 0.1, 1, false),
                ["payrolls"] = new("payrolls", new[] { "KXPAYROLLS", "PAYROLLS" }, 1, 1000, false),
                ["unemployment"] = new("unemployment", new[] { "KXU3", "U3" }, 0.1, 1, false),
                ["GDP"] = new("GDP", new[] { "KXGDP", "GDP" }, 0.1, 1, true),
                // "Above 4.00%" = the upper bound of the target range after that meeting.
                // KXFED since 2026, FED before it (FED-22MAR and older use another shape and
                // are left out by the "greater" filter).
                [FedRateName] = new(FedRateName, new[] { "KXFED", "FED" }, 0.25, 1, false,
                    NamesOwnMonth: true, Settles: true),
            };

        /// <summary>The Fed decision's figure: the upper bound of the target range.</summary>
        public const string FedRateName = "Fed rate";

        /// <summary>The six figure names read from Kalshi.</summary>
        public static IReadOnlyCollection<string> FigureNames => (IReadOnlyCollection<string>)Specs.Keys;

        public static bool TryGetSpec(string figureName, out KalshiFigureSpec spec)
            => Specs.TryGetValue(figureName, out spec!);

        /// <summary>The unit a stored figure of this name carries; used for an expected-only row.</summary>
        public static MarketEventFigureUnit UnitFor(string figureName) => figureName switch
        {
            "payrolls" => MarketEventFigureUnit.Thousands,
            FedRateName => MarketEventFigureUnit.Rate,
            _ => MarketEventFigureUnit.Percent,
        };

        /// <summary>Which direction is hot for this figure name.</summary>
        public static MarketEventFigureDirection DirectionFor(string figureName) => figureName switch
        {
            "payrolls" or "GDP" => MarketEventFigureDirection.HigherIsStronger,
            "unemployment" => MarketEventFigureDirection.HigherIsWeaker,
            _ => MarketEventFigureDirection.HigherIsHotter,
        };

        /// <summary>
        /// The candidate event tickers for a release, newest series name first. Monthly figures
        /// name the month before the release month, a Fed meeting its own month; GDP names the
        /// release date itself.
        /// </summary>
        public static IReadOnlyList<string> EventTickers(string figureName, DateOnly releaseDate)
        {
            if (!Specs.TryGetValue(figureName, out var spec)) return Array.Empty<string>();
            var month = spec.UsesReleaseDate || spec.NamesOwnMonth ? releaseDate : releaseDate.AddMonths(-1);
            var tickers = new List<string>(spec.Series.Count);
            foreach (string series in spec.Series)
            {
                tickers.Add(spec.UsesReleaseDate
                    ? $"{series}-{month:yy}{Month(month)}{month:dd}"
                    : $"{series}-{month:yy}{Month(month)}");
            }
            return tickers;
        }

        /// <summary>
        /// True when a market's close is within one day of our release date. A delayed release
        /// must not borrow another month's market.
        /// </summary>
        public static bool CloseDateMatches(string? closeTime, DateOnly releaseDate)
        {
            if (string.IsNullOrWhiteSpace(closeTime)) return false;
            if (!DateTimeOffset.TryParse(closeTime, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var close))
                return false;
            int days = Math.Abs(DateOnly.FromDateTime(close.UtcDateTime).DayNumber - releaseDate.DayNumber);
            return days <= 1;
        }

        /// <summary>
        /// One candle's probability: the mid of a tight book, else the last trade, else nothing.
        /// Both the live and the historical spelling of each close are read.
        /// </summary>
        public static double? CandleProbability(JObject candle)
        {
            double? bid = CloseOf(candle["yes_bid"] as JObject);
            double? ask = CloseOf(candle["yes_ask"] as JObject);
            double? last = CloseOf(candle["price"] as JObject);
            return Pick(bid, ask, last);
        }

        /// <summary>The same quote rule for one market-list row, whose prices are decimal strings.</summary>
        public static double? MarketProbability(JObject market)
        {
            double? bid = Number(market["yes_bid_dollars"]);
            double? ask = Number(market["yes_ask_dollars"]);
            double? last = Number(market["last_price_dollars"]);
            return Pick(bid, ask, last);
        }

        /// <summary>
        /// The newest candle at or before the release moment with a usable quote, walking back
        /// through older candles when the newest ones are unquoted.
        /// </summary>
        public static double? ProbabilityAt(IEnumerable<JObject> candles, DateTime releaseMomentUtc)
        {
            long moment = new DateTimeOffset(AsUtc(releaseMomentUtc)).ToUnixTimeSeconds();
            foreach (var candle in candles
                .Select(c => (At: c["end_period_ts"]?.Value<long?>(), Candle: c))
                .Where(x => x.At is long at && at <= moment)
                .OrderByDescending(x => x.At!.Value))
            {
                double? probability = CandleProbability(candle.Candle);
                if (probability is not null) return probability;
            }
            return null;
        }

        /// <summary>
        /// The expected figure from a ladder: strikes ascending, probabilities made non-increasing
        /// by a running minimum, then the crossing of one half rounded up onto the step grid.
        /// Null when fewer than two strikes remain or the ladder never crosses.
        /// </summary>
        public static double? Median(IReadOnlyList<KalshiStrikeProbability> quotes, double step)
        {
            if (step <= 0 || quotes.Count < 2) return null;

            var ordered = quotes
                .Where(q => double.IsFinite(q.Strike) && double.IsFinite(q.Probability))
                .OrderBy(q => q.Strike)
                .ToList();
            if (ordered.Count < 2) return null;

            var monotone = new List<KalshiStrikeProbability>(ordered.Count);
            double running = double.PositiveInfinity;
            foreach (var quote in ordered)
            {
                running = Math.Min(running, quote.Probability);
                monotone.Add(quote with { Probability = running });
            }

            for (int i = 0; i < monotone.Count - 1; i++)
            {
                var first = monotone[i];
                var second = monotone[i + 1];
                if (first.Strike >= second.Strike) continue;
                if (first.Probability < 0.5 || second.Probability >= 0.5) continue;

                double crossing = first.Strike
                    + (second.Strike - first.Strike)
                    * (first.Probability - 0.5)
                    / (first.Probability - second.Probability);
                double expected = Math.Ceiling(crossing / step - 1e-9) * step;
                double rounded = Math.Round(expected, Decimals(step), MidpointRounding.AwayFromZero);
                return rounded == 0 ? 0 : rounded;
            }
            return null;
        }

        /// <summary>
        /// The figure a settled ladder decided: every "greater" market resolved, yes below and no
        /// from one strike up, the first no exactly one step above the last yes. Then the value is
        /// that first no's strike (above the yes strike, not above itself, on the step grid).
        /// Null while anything is unresolved, when the ladder disagrees with itself, or when the
        /// value falls off either end.
        /// </summary>
        public static double? SettledValue(IEnumerable<JObject> markets, double step, double strikeScale = 1)
        {
            var rungs = new List<(double Strike, bool Yes)>();
            foreach (var market in markets)
            {
                if (!string.Equals((string?)market["strike_type"], "greater", StringComparison.Ordinal)) continue;
                if (Number(market["floor_strike"]) is not double strike) continue;
                string? result = (string?)market["result"];
                if (result == "yes") rungs.Add((strike / strikeScale, true));
                else if (result == "no") rungs.Add((strike / strikeScale, false));
                else return null;
            }
            if (rungs.Count < 2) return null;
            rungs.Sort((a, b) => a.Strike.CompareTo(b.Strike));

            int firstNo = rungs.FindIndex(r => !r.Yes);
            if (firstNo <= 0) return null;
            if (rungs.Skip(firstNo).Any(r => r.Yes)) return null;
            double lastYes = rungs[firstNo - 1].Strike;
            double value = rungs[firstNo].Strike;
            if (Math.Abs(value - lastYes - step) > 1e-9) return null;
            return Math.Round(value, Decimals(step), MidpointRounding.AwayFromZero);
        }

        /// <summary>The step's own decimals: 0 for payrolls' 1, 1 for every 0.1 figure, 2 for
        /// the Fed's 0.25 (a log would say 1 and turn 4.25 into 4.3).</summary>
        public static int Decimals(double step)
        {
            for (int decimals = 0; decimals <= 10; decimals++)
            {
                double scaled = step * Math.Pow(10, decimals);
                if (Math.Abs(scaled - Math.Round(scaled)) < 1e-9) return decimals;
            }
            return 10;
        }

        private static string Month(DateOnly date)
            => date.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant();

        private static double? CloseOf(JObject? side)
            => Number(side?["close_dollars"]) ?? Number(side?["close"]);

        private static double? Number(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            string? text = token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
            if (string.IsNullOrWhiteSpace(text)) return null;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : null;
        }

        private static double? Pick(double? bid, double? ask, double? last)
        {
            if (bid is double b && ask is double a
                && b >= 0 && b <= 1 && a >= 0 && a <= 1
                && a >= b && a - b <= MaxSpread + 1e-9)
                return (b + a) / 2;
            if (last is double trade && trade >= 0 && trade <= 1) return trade;
            return null;
        }

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
