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
        /// The events on this bar that pass the user's level, kinds and market, in the order the
        /// speech layer reads them (importance, then moment, then name). The earcon uses the
        /// same function, so sound and speech can never disagree about which events count.
        /// </summary>
        public static IReadOnlyList<MarketEventBarPlacement> Passing(
            IReadOnlyList<MarketEventBarPlacement> placements,
            MarketEventLevel level,
            MarketEventKindFilter kinds,
            MarketType market)
        {
            if (level == MarketEventLevel.Off) return Array.Empty<MarketEventBarPlacement>();

            var passing = placements
                .Where(p => p.IsPlaced
                    && p.Event.Touches(market)
                    && MarketEventLevels.Includes(level, p.Event.Importance)
                    && kinds.Includes(p.Event.Kind))
                .ToList();
            if (passing.Count == 0) return Array.Empty<MarketEventBarPlacement>();

            passing.Sort((a, b) =>
            {
                int byImportance = a.Event.Importance.CompareTo(b.Event.Importance);
                if (byImportance != 0) return byImportance;
                int byMoment = a.Event.MomentUtc.CompareTo(b.Event.MomentUtc);
                if (byMoment != 0) return byMoment;
                return string.CompareOrdinal(a.Event.SpokenName, b.Event.SpokenName);
            });
            return passing;
        }

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
            var passing = Passing(placements, level, kinds, market);
            if (passing.Count == 0) return "";

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

            // The figures go after the time and before the placement word. Only on a bar with
            // ONE passing event, and never on a weekly or longer bar where the bar is not a
            // single release's moment.
            string figures = !weeklyOrLonger ? FigureClause(e) : "";
            if (figures.Length > 0)
            {
                text += ", " + figures;
                // The surprise word follows the figure it already reads. No expected figure
                // means the stage-one clause is unchanged.
                string surprise = e.Kind == MarketEventKind.Earnings ? "" : PrimarySurprise(e);
                if (surprise.Length > 0) text += ", " + surprise;
            }

            // "AAPL earnings after the close" already said where it falls.
            if (!string.IsNullOrEmpty(placement.Placement) && placement.Placement != TimeText(e))
                text += ", " + placement.Placement;
            return text;
        }

        /// <summary>
        /// The spoken figure clause for one event, or "" when it carries no figure. The signs
        /// and units are the whole point: percentages always show one decimal, thousands are
        /// whole, and 1,000 thousand or more becomes millions with the trailing .0 trimmed.
        /// </summary>
        public static string FigureClause(MarketEvent e)
        {
            var figures = e.Figures;
            if (figures == null) return "";
            if (e.Kind == MarketEventKind.Earnings) return EarningsClause(figures);
            if (Find(figures, KalshiExpectedFigures.FedRateName) is { } fed)
                return fed.Actual is double decided ? FedDecision(decided, fed.Previous) : "";

            var cpi = Find(figures, "CPI");
            var core = Find(figures, "core CPI");
            var payrolls = Find(figures, "payrolls");
            var unemployment = Find(figures, "unemployment");
            var gdp = Find(figures, "GDP");
            var ppi = Find(figures, "PPI");

            if (payrolls != null || unemployment != null)
            {
                var parts = new List<string>(3);
                if (payrolls?.Actual is double pay)
                    parts.Add($"payrolls {ThousandsPhrase(pay)}");
                if (payrolls?.Previous is double payPrevious && payrolls.Actual is not null)
                    parts.Add($"previous {ThousandsPhrase(payPrevious)}");
                if (unemployment?.Actual is double rate)
                    parts.Add($"unemployment {PercentValue(rate)}%");
                return string.Join(", ", parts);
            }

            if (gdp != null)
            {
                if (gdp.Actual is not double growth) return "";
                var parts = new List<string>(3)
                {
                    EstimateWord(figures.EstimateNumber),
                    $"{Direction(growth)} {PercentValue(growth)}% annualized",
                };
                if (gdp.Previous is double previous)
                {
                    string label = (figures.EstimateNumber ?? 1) <= 1 ? "previous quarter" : "was";
                    parts.Add($"{label} {Direction(previous)} {PercentValue(previous)}%");
                }
                return string.Join(", ", parts);
            }

            var month = cpi ?? ppi ?? core;
            if (month != null)
            {
                if (month.Actual is not double actual) return "";
                string prefix = month == core ? "core CPI " : "";
                string text = prefix + MonthPhrase(actual);
                if (month.Previous is double previous)
                    text += ", previous " + SignedPercent(previous);
                return text;
            }

            return "";
        }

        /// <summary>
        /// The previous figure alone, in the same words <see cref="FigureClause"/> uses for it:
        /// "previous up 2.9%", "previous payrolls up 151 thousand", "previous quarter up 2.1%".
        /// Empty when no previous figure is stored. The calendar says this for an event that has
        /// not happened yet, where there is no actual figure to read.
        /// </summary>
        public static string PreviousFigureClause(MarketEvent e)
        {
            var figures = e.Figures;
            if (figures == null) return "";
            if (e.Kind == MarketEventKind.Earnings)
                return Find(figures, EarningsEvents.EpsName)?.Previous is double epsPrevious
                    ? "previous quarter EPS " + PerShareValue(epsPrevious)
                    : "";
            if (Find(figures, KalshiExpectedFigures.FedRateName) is { } fed)
                return fed.Previous is double rateNow ? "rate now " + RateValue(rateNow) : "";

            var cpi = Find(figures, "CPI");
            var core = Find(figures, "core CPI");
            var payrolls = Find(figures, "payrolls");
            var unemployment = Find(figures, "unemployment");
            var gdp = Find(figures, "GDP");
            var ppi = Find(figures, "PPI");

            if (payrolls != null || unemployment != null)
            {
                var parts = new List<string>(2);
                if (payrolls?.Previous is double payPrevious)
                    parts.Add($"previous payrolls {ThousandsPhrase(payPrevious)}");
                if (unemployment?.Previous is double ratePrevious)
                    parts.Add($"previous unemployment {PercentValue(ratePrevious)}%");
                return string.Join(", ", parts);
            }

            if (gdp != null)
            {
                if (gdp.Previous is not double previous) return "";
                string label = (figures.EstimateNumber ?? 1) <= 1 ? "previous quarter" : "was";
                return $"{label} {Direction(previous)} {PercentValue(previous)}%";
            }

            var month = cpi ?? ppi ?? core;
            return month?.Previous is double monthPrevious
                ? "previous " + SignedPercent(monthPrevious)
                : "";
        }

        /// <summary>
        /// The detail key's figure lines for one event. Full sentences, and a dossier value or
        /// "unavailable" on every fact — never a blank.
        /// </summary>
        public static string FigureDetail(MarketEvent e)
        {
            var figures = e.Figures;
            if (figures == null) return "Figure: unavailable.";
            if (e.Kind == MarketEventKind.Earnings) return EarningsDetail(figures);
            if (Find(figures, KalshiExpectedFigures.FedRateName) is { } fed) return FedDetail(fed);

            var cpi = Find(figures, "CPI");
            var core = Find(figures, "core CPI");
            var payrolls = Find(figures, "payrolls");
            var unemployment = Find(figures, "unemployment");
            var gdp = Find(figures, "GDP");
            var ppi = Find(figures, "PPI");

            if (payrolls != null || unemployment != null)
            {
                var parts = new List<string>(4);
                parts.Add(payrolls?.Actual is double pay
                    ? $"Figure: payrolls {ThousandsPhrase(pay)}{ExpectedSuffix(payrolls)}."
                    : "Figure: payrolls unavailable" + ExpectedSuffix(payrolls) + ".");
                if (payrolls?.Actual is not null)
                {
                    parts.Add(payrolls.Previous is double payPrevious
                        ? $"Previous: {ThousandsPhrase(payPrevious)}."
                        : "Previous: unavailable.");
                }
                parts.Add(unemployment?.Actual is double rate
                    ? $"Unemployment: {PercentValue(rate)}%{ExpectedSuffix(unemployment)}, previous "
                      + (unemployment.Previous is double ratePrevious
                          ? $"{PercentValue(ratePrevious)}%."
                          : "unavailable.")
                    : "Unemployment: unavailable" + ExpectedSuffix(unemployment) + ".");
                parts.Add(FirstPrintLine);
                return string.Join(" ", parts);
            }

            if (gdp != null)
            {
                if (gdp.Actual is not double growth) return "Figure: unavailable.";
                var parts = new List<string>(3)
                {
                    $"Figure: {EstimateWord(figures.EstimateNumber)}, "
                    + $"{Direction(growth)} {PercentValue(growth)}% annualized"
                    + QuarterPhrase(figures.Period) + ExpectedSuffix(gdp) + ".",
                };
                if ((figures.EstimateNumber ?? 1) <= 1)
                {
                    parts.Add(gdp.Previous is double previous
                        ? $"Previous quarter: {Direction(previous)} {PercentValue(previous)}%."
                        : "Previous quarter: unavailable.");
                }
                else
                {
                    parts.Add(gdp.Previous is double previous
                        ? $"Was {Direction(previous)} {PercentValue(previous)}%."
                        : "Was unavailable.");
                }
                parts.Add(FirstPrintLine);
                return string.Join(" ", parts);
            }

            if (cpi != null || core != null || ppi != null)
            {
                var primary = cpi ?? ppi;
                var parts = new List<string>(4);
                string name = ppi != null && cpi == null ? "PPI" : "CPI";
                parts.Add(primary?.Actual is double actual
                    ? $"Figure: {name} {MonthPhrase(actual)}{ExpectedSuffix(primary)}."
                    : $"Figure: {name} unavailable" + ExpectedSuffix(primary) + ".");
                if (primary?.Actual is not null)
                {
                    parts.Add(primary.Previous is double previous
                        ? $"Previous: {SignedPercent(previous)}."
                        : "Previous: unavailable.");
                }
                if (cpi != null)
                {
                    parts.Add(core?.Actual is double coreActual
                        ? $"Core CPI: {MonthPhrase(coreActual)}{ExpectedSuffix(core)}, previous "
                          + (core.Previous is double corePrevious
                              ? $"{SignedPercent(corePrevious)}."
                              : "unavailable.")
                        : "Core CPI: unavailable" + ExpectedSuffix(core) + ".");
                }
                parts.Add(FirstPrintLine);
                return string.Join(" ", parts);
            }

            return "Figure: unavailable.";
        }

        private const string FirstPrintLine = "First print, from FRED/ALFRED.";

        /// <summary>
        /// The surprise word when both an actual and an expected figure exist. Both are rounded
        /// to the figure's own step first, so 0.34 against 0.3 is "as expected" on a 0.1 grid.
        /// </summary>
        public static string SurpriseWord(MarketEventFigure figure)
        {
            if (figure.Actual is not double actual || figure.Expected is not double expected)
                return "";

            double a = Rounded(figure.Unit, actual);
            double e = Rounded(figure.Unit, expected);
            if (a == e) return "as expected";
            if (figure.Unit is MarketEventFigureUnit.PerShare or MarketEventFigureUnit.Money)
                return a > e ? "beat estimates" : "missed estimates";
            // A rate is neither hot nor strong; the Fed set it higher or lower than priced.
            if (figure.Unit == MarketEventFigureUnit.Rate)
                return a > e ? "higher than expected" : "lower than expected";

            bool higher = a > e;
            return figure.Direction switch
            {
                MarketEventFigureDirection.HigherIsStronger =>
                    higher ? "stronger than expected" : "weaker than expected",
                MarketEventFigureDirection.HigherIsWeaker =>
                    higher ? "weaker than expected" : "stronger than expected",
                _ => higher ? "hotter than expected" : "cooler than expected",
            };
        }

        /// <summary>The same word in the release read-out's short form: "hotter", not "hotter than expected".</summary>
        public static string ShortSurpriseWord(MarketEventFigure figure)
        {
            string word = SurpriseWord(figure);
            if (word.EndsWith(" estimates", StringComparison.Ordinal))
                return word[..^" estimates".Length];
            return word.EndsWith(" than expected", StringComparison.Ordinal)
                ? word[..^" than expected".Length]
                : word;
        }

        /// <summary>The expected figure in its own unit: "0.3", "-0.1", "up 128 thousand".</summary>
        public static string ExpectedValue(MarketEventFigure figure)
        {
            if (figure.Expected is not double expected) return "";
            return Value(figure.Unit, expected);
        }

        /// <summary>A figure's number in its own unit's words: "0.3", "up 128 thousand",
        /// "2.10", "114.5 billion".</summary>
        private static string Value(MarketEventFigureUnit unit, double value) => unit switch
        {
            MarketEventFigureUnit.Thousands => ThousandsPhrase(value),
            MarketEventFigureUnit.PerShare => PerShareValue(value),
            MarketEventFigureUnit.Money => MoneyValue(value),
            MarketEventFigureUnit.Rate => RateValue(value),
            _ => value.ToString("0.0", CultureInfo.InvariantCulture),
        };

        /// <summary>A figure rounded to the step it is said in, so the surprise word never
        /// disagrees with the two numbers the user hears.</summary>
        private static double Rounded(MarketEventFigureUnit unit, double value) => unit switch
        {
            MarketEventFigureUnit.Thousands => Math.Round(value, 0, MidpointRounding.AwayFromZero),
            MarketEventFigureUnit.PerShare or MarketEventFigureUnit.Rate
                => Math.Round(value, 2, MidpointRounding.AwayFromZero),
            MarketEventFigureUnit.Money => MoneyStep(value) is var step and > 0
                ? Math.Round(value / step, 1, MidpointRounding.AwayFromZero) * step
                : Math.Round(value, 0, MidpointRounding.AwayFromZero),
            _ => Math.Round(value, 1, MidpointRounding.AwayFromZero),
        };

        private static double MoneyStep(double value)
        {
            double abs = Math.Abs(value);
            return abs >= 1e9 ? 1e9 : abs >= 1e6 ? 1e6 : 0;
        }

        private static string PerShareValue(double value)
            => value.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>"4.25%": the Fed's quarter points need both decimals.</summary>
        private static string RateValue(double value)
            => value.ToString("0.00", CultureInfo.InvariantCulture) + "%";

        /// <summary>"raised to 4.25% from 4.00%", "cut to 3.75% from 4.00%", "held at 4.00%",
        /// or "rate 4.25%" when the meeting before was not read.</summary>
        private static string FedDecision(double decided, double? previous)
        {
            if (previous is not double before) return "rate " + RateValue(decided);
            double a = Math.Round(decided, 2, MidpointRounding.AwayFromZero);
            double b = Math.Round(before, 2, MidpointRounding.AwayFromZero);
            if (a == b) return "held at " + RateValue(decided);
            return (a > b ? "raised to " : "cut to ") + RateValue(decided) + " from " + RateValue(before);
        }

        /// <summary>The detail key's Fed lines: the decided rate beside what was priced, the one
        /// before, and where the rate came from.</summary>
        private static string FedDetail(MarketEventFigure fed)
        {
            var parts = new List<string>(3)
            {
                fed.Actual is double decided
                    ? $"Figure: Fed rate, upper bound of the target range, {FedDecision(decided, fed.Previous)}{ExpectedSuffix(fed)}."
                    : "Figure: Fed rate unavailable" + ExpectedSuffix(fed) + ".",
                fed.Previous is double previous
                    ? $"Previous: {RateValue(previous)}."
                    : "Previous: unavailable.",
                "Rate as Kalshi settled it from the Federal Reserve's published target.",
            };
            return string.Join(" ", parts);
        }

        /// <summary>"114.5 billion", "350.2 million", or the whole number below a million.</summary>
        private static string MoneyValue(double value)
        {
            double step = MoneyStep(value);
            if (step == 0) return value.ToString("#,0", CultureInfo.InvariantCulture);
            string word = step == 1e9 ? "billion" : "million";
            return (value / step).ToString("0.0", CultureInfo.InvariantCulture) + " " + word;
        }

        /// <summary>
        /// The arrow keys' earnings figures, each beside its estimate: "EPS 2.10, beat 2.03
        /// expected, revenue 114.5 billion, missed 115.7 billion expected". Empty before the
        /// report, when there is no actual to read.
        /// </summary>
        private static string EarningsClause(MarketEventFigures figures)
        {
            var parts = new List<string>(2);
            foreach (string name in new[] { EarningsEvents.EpsName, EarningsEvents.RevenueName })
            {
                if (Find(figures, name) is not { Actual: double actual } figure) continue;
                string text = $"{name} {Value(figure.Unit, actual)}";
                string word = ShortSurpriseWord(figure);
                if (word == "as expected") text += ", as expected";
                else if (word.Length > 0) text += $", {word} {ExpectedValue(figure)} expected";
                parts.Add(text);
            }
            return string.Join(", ", parts);
        }

        /// <summary>The detail key's earnings lines: each figure with its estimate and source,
        /// and the quarter before.</summary>
        private static string EarningsDetail(MarketEventFigures figures)
        {
            var parts = new List<string>(3);
            var eps = Find(figures, EarningsEvents.EpsName);
            parts.Add(eps?.Actual is double epsActual
                ? $"Figure: EPS {PerShareValue(epsActual)}{ExpectedSuffix(eps)}."
                : "Figure: EPS unavailable" + ExpectedSuffix(eps) + ".");
            parts.Add(eps?.Previous is double previous
                ? $"Previous quarter: EPS {PerShareValue(previous)}."
                : "Previous quarter: unavailable.");
            var revenue = Find(figures, EarningsEvents.RevenueName);
            parts.Add(revenue?.Actual is double revenueActual
                ? $"Revenue: {MoneyValue(revenueActual)}{ExpectedSuffix(revenue)}."
                : "Revenue: unavailable" + ExpectedSuffix(revenue) + ".");
            return string.Join(" ", parts);
        }

        /// <summary>"expected 0.3, by Kalshi" — the source always travels with the number.</summary>
        public static string ExpectedClause(MarketEventFigure figure)
        {
            string value = ExpectedValue(figure);
            return value.Length == 0 ? "" : $"expected {value}, by {figure.ExpectedBy}";
        }

        /// <summary>The inline detail suffix: ", expected 0.3, by Kalshi, hotter than expected".</summary>
        public static string ExpectedSuffix(MarketEventFigure? figure)
        {
            if (figure == null) return "";
            string clause = ExpectedClause(figure);
            if (clause.Length == 0) return "";
            string word = SurpriseWord(figure);
            return ", " + clause + (word.Length > 0 ? ", " + word : "");
        }

        /// <summary>The calendar's short form, with no source: "expected 0.3".</summary>
        public static string ExpectedShort(MarketEvent e)
        {
            var figure = PrimaryFigureWithExpected(e);
            if (figure == null) return "";
            // "EPS expected 2.96": a company's estimate is not one number the name implies.
            return (e.Kind == MarketEventKind.Earnings ? figure.Name + " " : "")
                + "expected " + ExpectedValue(figure);
        }

        /// <summary>The word for the headline figure alone; core CPI gets its own in the detail.</summary>
        public static string PrimarySurprise(MarketEvent e)
        {
            var figure = PrimaryExpectedFigure(e);
            return figure == null ? "" : SurpriseWord(figure);
        }

        /// <summary>
        /// The figure whose surprise word decides the short clause: payrolls for jobs, CPI for a
        /// CPI release, GDP, PPI, then unemployment and core CPI when they are all there is. A
        /// figure without both actual and expected is skipped.
        /// </summary>
        public static MarketEventFigure? PrimaryExpectedFigure(MarketEvent e)
            => PrimaryFigureWhere(e, f => f.Actual is not null && f.Expected is not null);

        /// <summary>The same priority, but an expected figure alone is enough (an upcoming release).</summary>
        public static MarketEventFigure? PrimaryFigureWithExpected(MarketEvent e)
            => PrimaryFigureWhere(e, f => f.Expected is not null);

        private static MarketEventFigure? PrimaryFigureWhere(
            MarketEvent e, Func<MarketEventFigure, bool> test)
        {
            var figures = e.Figures;
            if (figures == null) return null;
            foreach (string name in new[] { "payrolls", "CPI", "PPI", "GDP", "unemployment", "core CPI",
                         EarningsEvents.EpsName, EarningsEvents.RevenueName, KalshiExpectedFigures.FedRateName })
            {
                var figure = Find(figures, name);
                if (figure != null && test(figure)) return figure;
            }
            return null;
        }

        /// <summary>
        /// The release read-out's compact figure when a surprise can be said: "CPI 0.4,
        /// expected 0.3, hotter." Empty when there is no expected figure, so the older wording
        /// below is untouched.
        /// </summary>
        public static string ReleaseFigureClause(MarketEvent e)
        {
            var figure = PrimaryExpectedFigure(e);
            if (figure?.Actual is not double actual || figure.Expected is not double expected)
                return "";
            string value = Value(figure.Unit, actual);
            return $"{figure.Name} {value}, expected {ExpectedValue(figure)}, "
                + ShortSurpriseWord(figure);
        }

        private static MarketEventFigure? Find(MarketEventFigures figures, string name)
            => figures.Figures.FirstOrDefault(f => f.Name == name);

        private static string MonthPhrase(double value)
            => value == 0 ? "unchanged on the month" : $"{Direction(value)} {PercentValue(value)}% on the month";

        private static string SignedPercent(double value)
            => value == 0 ? "unchanged" : $"{Direction(value)} {PercentValue(value)}%";

        private static string PercentValue(double value)
            => Math.Abs(value).ToString("0.0", CultureInfo.InvariantCulture);

        private static string ThousandsPhrase(double value)
        {
            if (value == 0) return "unchanged";
            double abs = Math.Abs(value);
            string direction = value > 0 ? "up" : "down";
            if (abs >= 1000)
            {
                string millions = (abs / 1000).ToString("0.0", CultureInfo.InvariantCulture);
                if (millions.EndsWith(".0", StringComparison.Ordinal))
                    millions = millions[..^2];
                return $"{direction} {millions} million";
            }
            return $"{direction} {abs.ToString("0", CultureInfo.InvariantCulture)} thousand";
        }

        private static string Direction(double value)
            => value > 0 ? "up" : value < 0 ? "down" : "unchanged";

        private static string EstimateWord(int? estimate) => estimate switch
        {
            1 => "first estimate",
            2 => "second estimate",
            3 => "third estimate",
            _ => "revised estimate",
        };

        private static string QuarterPhrase(DateOnly? period)
        {
            if (period is not { } day) return "";
            int quarter = (day.Month - 1) / 3 + 1;
            string word = quarter switch
            {
                1 => "first",
                2 => "second",
                3 => "third",
                _ => "fourth",
            };
            return $" for the {word} quarter of {day.Year}";
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
            if (e.Session == MarketEventSession.BeforeOpen) return MarketEventPlacement.BeforeTheOpen;
            if (e.Session == MarketEventSession.AfterClose) return MarketEventPlacement.AfterTheClose;
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
                string where = p.Placement == null || p.Placement == time ? "on this bar" : p.Placement;
                parts.Add(
                    $"{e.LongName}, {time}, {ImportanceWord(e.Importance)} importance, "
                    + $"{where}, {e.Source}.");
                parts.Add(FigureDetail(e));
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
