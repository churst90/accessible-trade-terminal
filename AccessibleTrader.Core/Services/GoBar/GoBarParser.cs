using System.Globalization;
using System.Text.RegularExpressions;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.GoBar
{
    /// <summary>The verbs that take a value. Their spellings are pinned against the registry.</summary>
    public enum GoBarVerb
    {
        Timeframe,
        Symbol,
        Provider,
        Indicator,
        PriceAlert,
        Template,
        Renko,
        RangeBars,
        LineBreak,
        Kagi,
        GoTo,
        Find,
        Settings,
        Help,
        Buy,
        Sell,
        Flat,
    }

    /// <summary>One verb and every word it answers to, plus the 6a entry it belongs to.</summary>
    public sealed record GoBarVerbSpec(
        GoBarVerb Verb,
        IReadOnlyList<string> Words,
        SystemCommand? Entry);

    /// <summary>
    /// The verb table. <c>go to</c> is a phrase and is handled beside the single words.
    ///
    /// <para>The registry test <c>EveryArgumentVerbBelongsToItsEntry</c> pins every word here
    /// against every code and alias in the registry: a verb may only be a code or alias of its
    /// own entry, and GOTO/FIND's words may be no entry's code or alias at all.</para>
    /// </summary>
    public static class GoBarVerbs
    {
        public static IReadOnlyList<GoBarVerbSpec> All { get; } = new[]
        {
            new GoBarVerbSpec(GoBarVerb.Timeframe,
                new[] { "TF", "TIMEFRAME" }, SystemCommand.ChangeTimeframe),
            new GoBarVerbSpec(GoBarVerb.Symbol,
                new[] { "GP", "SYM", "CHART", "SYMBOL" }, SystemCommand.ChangeSymbol),
            new GoBarVerbSpec(GoBarVerb.Provider,
                new[] { "PROV", "PROVIDER", "VENUE" }, SystemCommand.ChangeProvider),
            new GoBarVerbSpec(GoBarVerb.Indicator,
                new[] { "IND", "INDICATOR", "ADD" }, SystemCommand.OpenIndicators),
            new GoBarVerbSpec(GoBarVerb.PriceAlert,
                new[] { "ALERT" }, SystemCommand.OpenAlerts),
            new GoBarVerbSpec(GoBarVerb.Template,
                new[] { "TPL", "TEMPLATE" }, SystemCommand.ApplyIndicatorTemplate),
            new GoBarVerbSpec(GoBarVerb.Renko,
                new[] { "RENKO", "BRICKS" }, SystemCommand.ShowRenkoChart),
            new GoBarVerbSpec(GoBarVerb.RangeBars,
                new[] { "RANGEBAR", "RANGEBARS" }, SystemCommand.ShowRangeBarsChart),
            new GoBarVerbSpec(GoBarVerb.LineBreak,
                new[] { "LINEBREAK", "LBREAK" }, SystemCommand.ShowLineBreakChart),
            new GoBarVerbSpec(GoBarVerb.Kagi,
                new[] { "KAGI" }, SystemCommand.ShowKagiChart),
            new GoBarVerbSpec(GoBarVerb.GoTo,
                new[] { "GOTO", "DATE" }, null),
            new GoBarVerbSpec(GoBarVerb.Find,
                new[] { "FIND" }, null),
            new GoBarVerbSpec(GoBarVerb.Settings,
                new[] { "SET" }, SystemCommand.OpenSettings),
            new GoBarVerbSpec(GoBarVerb.Help,
                new[] { "HELP" }, SystemCommand.OpenHelp),
        };

        public const string GoToPhrase = "GO TO";

        public static GoBarVerbSpec? For(GoBarVerb verb)
            => All.FirstOrDefault(v => v.Verb == verb);

        /// <summary>Every single-word spelling, invariant upper case.</summary>
        public static IEnumerable<string> AllWords => All.SelectMany(v => v.Words);

        /// <summary>The words that make up the two-word GOTO phrase.</summary>
        public static IReadOnlyList<string> GoToWords { get; } = new[] { "GO", "TO" };
    }

    public enum GoBarParseKind
    {
        /// <summary>Nothing but an ordinary command (or nothing at all).</summary>
        Command,

        /// <summary>The whole typed text is a timeframe, with no verb.</summary>
        BareTimeframe,

        /// <summary>The whole typed text is an ISO date, with no verb.</summary>
        BareDate,

        /// <summary>A verb and a value.</summary>
        Argument,

        /// <summary>A verb and no value: the 6a entry, or a hint when it has none.</summary>
        BareVerb,

        /// <summary>A verb and a value that is not a valid timeframe, price or date.</summary>
        Invalid,

        /// <summary>A typed money command: BUY, SELL or FLAT, first word only.</summary>
        Money,
    }

    /// <summary>The pure result of reading one line of Go bar text.</summary>
    public sealed record GoBarParseResult(
        GoBarParseKind Kind,
        GoBarVerb? Verb = null,
        string Argument = "",
        string? Timeframe = null,
        string? TimeframeWords = null,
        double? Price = null,
        DateTime? Moment = null,
        bool MomentHasTime = false,
        string? Refusal = null,
        double? Quantity = null,
        double? LimitPrice = null,
        bool Flat = false,
        bool Market = false);

    /// <summary>
    /// The Go bar's reader: words in either order, one optional argument, and the two bare
    /// shapes that cannot mean anything else.
    ///
    /// <para><b>Either order.</b> "TF 4h" and "4h TF" are the same request. The first or the
    /// last token must be a verb (or the two-word phrase "go to" at either end); the tokens in
    /// between, joined with one space, are the argument. If both ends are verbs the first wins.
    /// A verb with no argument is the 6a entry.</para>
    ///
    /// <para><b>Money is never an argument command.</b> No verb spelling is a Q-prefixed money
    /// code, so "QSTOP 5" reads as an ordinary command (and matches nothing), never as a
    /// command that arms, sizes or places an order.</para>
    /// </summary>
    public static class GoBarParser
    {
        private static readonly Regex AppTimeframe =
            new(@"^(\d{1,4})\s*([mhdwM])$", RegexOptions.Compiled);

        private static readonly Regex SpelledTimeframe =
            new(@"^(\d{1,4})\s*([A-Za-z]+)$", RegexOptions.Compiled);

        private static readonly Regex SlashDate =
            new(@"^\d{1,2}/\d{1,2}/\d{2,4}(\s+\d{1,2}:\d{2})?$", RegexOptions.Compiled);

        private static readonly Regex IsoLookingDate =
            new(@"^\d{4}-\d{1,2}-\d{1,2}", RegexOptions.Compiled);

        private static readonly string[] DateFormats =
        {
            "yyyy-MM-dd HH:mm", "yyyy-MM-dd",
            "d MMMM yyyy HH:mm", "d MMMM yyyy",
            "d MMM yyyy HH:mm", "d MMM yyyy",
            "MMMM d yyyy HH:mm", "MMMM d yyyy",
            "MMMM d, yyyy HH:mm", "MMMM d, yyyy",
            "MMM d yyyy HH:mm", "MMM d yyyy",
            "MMM d, yyyy HH:mm", "MMM d, yyyy",
        };

        public const string DateRefusal = "Write the date as 2024-03-01 or 1 March 2024.";

        public const string MoneyOutOfOrder =
            "Money commands start with the word, like BUY 0.5. Nothing was opened.";

        public static GoBarParseResult Parse(string? input)
        {
            string typed = Collapse(input);
            if (typed.Length == 0) return new GoBarParseResult(GoBarParseKind.Command);

            var tokens = typed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (IsMoneyWord(tokens[0]))
                return ParseMoney(tokens);

            bool atStart = TryVerbAtStart(tokens, out var verb, out int consumed);

            // A money word anywhere but first is the deliberate exception to either order: it
            // is never read backwards into an order, and it says so rather than matching a
            // command by accident. A real verb at the start still owns its value ("TF BUY" is
            // a timeframe mistake, not a money request).
            if (!atStart && tokens.Any(IsMoneyWord))
                return new GoBarParseResult(GoBarParseKind.Invalid, Refusal: MoneyOutOfOrder);
            bool atEnd = false;
            if (!atStart)
                atEnd = TryVerbAtEnd(tokens, out verb, out consumed);

            if (atStart || atEnd)
            {
                var argumentTokens = atStart
                    ? tokens.Skip(consumed).ToArray()
                    : tokens.Take(tokens.Length - consumed).ToArray();
                string argument = string.Join(' ', argumentTokens);

                if (argument.Length == 0)
                    return new GoBarParseResult(GoBarParseKind.BareVerb, verb);

                return WithArgument(verb, argument);
            }

            if (TryParseTimeframe(typed, out string tf, out string words))
                return new GoBarParseResult(GoBarParseKind.BareTimeframe,
                    GoBarVerb.Timeframe, typed, tf, words);

            if (TryParseDate(typed, out DateTime moment, out bool hasTime, out string? refusal))
            {
                if (refusal != null)
                    return new GoBarParseResult(GoBarParseKind.Invalid,
                        GoBarVerb.GoTo, typed, Refusal: refusal);
                return new GoBarParseResult(GoBarParseKind.BareDate,
                    GoBarVerb.GoTo, typed, Moment: moment, MomentHasTime: hasTime);
            }

            return new GoBarParseResult(GoBarParseKind.Command);
        }

        private static bool IsMoneyWord(string token) =>
            token.Equals("BUY", StringComparison.OrdinalIgnoreCase)
            || token.Equals("SELL", StringComparison.OrdinalIgnoreCase)
            || token.Equals("FLAT", StringComparison.OrdinalIgnoreCase);

        private static GoBarParseResult ParseMoney(string[] tokens)
        {
            string word = tokens[0].ToUpperInvariant();

            if (word == "FLAT")
            {
                if (tokens.Length > 1)
                    return new GoBarParseResult(GoBarParseKind.Invalid, GoBarVerb.Flat, tokens[1],
                        Refusal: "FLAT takes nothing; it uses the chart's symbol. Nothing was opened.");
                return new GoBarParseResult(GoBarParseKind.Money, GoBarVerb.Flat, Flat: true);
            }

            GoBarVerb side = word == "SELL" ? GoBarVerb.Sell : GoBarVerb.Buy;
            string refusal = $"{word} takes a quantity and an optional limit price, "
                + $"like {word} 0.5 or {word} 0.5 95000. Nothing was opened.";

            if (tokens.Length == 1)
                return new GoBarParseResult(GoBarParseKind.Money, side, Market: false);

            double quantity;
            double? limit = null;

            switch (tokens.Length)
            {
                case 2:
                    if (!TryParsePositiveDecimal(tokens[1], out quantity))
                        return new GoBarParseResult(GoBarParseKind.Invalid, side, tokens[1],
                            Refusal: refusal);
                    break;

                case 3:
                    if (!TryParsePositiveDecimal(tokens[1], out quantity)
                        || !TryParsePositiveDecimal(tokens[2], out double price))
                        return new GoBarParseResult(GoBarParseKind.Invalid, side,
                            string.Join(' ', tokens.Skip(1)), Refusal: refusal);
                    limit = price;
                    break;

                case 4 when tokens[2].Equals("at", StringComparison.OrdinalIgnoreCase):
                    if (!TryParsePositiveDecimal(tokens[1], out quantity)
                        || !TryParsePositiveDecimal(tokens[3], out double atPrice))
                        return new GoBarParseResult(GoBarParseKind.Invalid, side,
                            string.Join(' ', tokens.Skip(1)), Refusal: refusal);
                    limit = atPrice;
                    break;

                default:
                    return new GoBarParseResult(GoBarParseKind.Invalid, side,
                        string.Join(' ', tokens.Skip(1)), Refusal: refusal);
            }

            return new GoBarParseResult(GoBarParseKind.Money, side,
                Quantity: quantity, LimitPrice: limit, Market: limit == null);
        }

        /// <summary>
        /// RENKO's argument: a positive box with optional thousands separators ("500", "1,000"),
        /// or "ATR" (default 14) with an optional length 1..200 ("ATR 20"). Anything else is
        /// refused with the one sentence the Go bar always uses for this command.
        /// </summary>
        public static bool TryParseRenko(string? text, out RenkoSettings settings)
        {
            settings = new RenkoSettings();
            if (!TryParseSize(text, out var mode, out double size, out int length)) return false;
            settings = new RenkoSettings(mode, size, length);
            return true;
        }

        /// <summary>The chart type a built chart's verb shows.</summary>
        public static PriceChartStyle BuiltStyle(GoBarVerb verb) => verb switch
        {
            GoBarVerb.RangeBars => PriceChartStyle.RangeBars,
            GoBarVerb.LineBreak => PriceChartStyle.LineBreak,
            GoBarVerb.Kagi => PriceChartStyle.Kagi,
            _ => PriceChartStyle.Renko,
        };

        /// <summary>RANGEBAR's argument, in RENKO's forms ("500", "ATR", "ATR 20"), as the range fields.</summary>
        public static bool TryParseRange(string? text, out RenkoSettings settings)
        {
            settings = new RenkoSettings();
            if (!TryParseSize(text, out var mode, out double size, out int length)) return false;
            settings = new RenkoSettings(RangeMode: mode, RangeSize: size, RangeAtrLength: length);
            return true;
        }

        /// <summary>KAGI's argument, in RENKO's forms ("500", "ATR", "ATR 20"), as the reversal amount.</summary>
        public static bool TryParseKagi(string? text, out RenkoSettings settings)
        {
            settings = new RenkoSettings();
            if (!TryParseSize(text, out var mode, out double size, out int length)) return false;
            settings = new RenkoSettings(KagiMode: mode, KagiSize: size, KagiAtrLength: length);
            return true;
        }

        /// <summary>LINEBREAK's argument: a whole number of lines, 1 to 50.</summary>
        public static bool TryParseLineBreak(string? text, out RenkoSettings settings)
        {
            settings = new RenkoSettings();
            if (!int.TryParse((text ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int lines)
                || lines < Charts.LineBreakBuilder.MinLines || lines > Charts.LineBreakBuilder.MaxLines)
                return false;
            settings = new RenkoSettings(LineBreakLines: lines);
            return true;
        }

        /// <summary>The argument of a built chart's word, by type.</summary>
        public static bool TryParseBuiltChart(PriceChartStyle style, string? text, out RenkoSettings settings) => style switch
        {
            PriceChartStyle.RangeBars => TryParseRange(text, out settings),
            PriceChartStyle.LineBreak => TryParseLineBreak(text, out settings),
            PriceChartStyle.Kagi => TryParseKagi(text, out settings),
            _ => TryParseRenko(text, out settings),
        };

        /// <summary>A size argument: a positive price, or ATR with an optional length 1..200.</summary>
        private static bool TryParseSize(string? text, out RenkoBoxMode mode, out double size, out int length)
        {
            mode = RenkoBoxMode.Atr;
            size = 0;
            length = 14;
            string typed = (text ?? "").Trim();
            if (typed.Length == 0) return false;

            var parts = typed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 1 && parts[0].Equals("ATR", StringComparison.OrdinalIgnoreCase))
                return true;

            if (parts.Length == 2 && parts[0].Equals("ATR", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out length)
                    || length < 1 || length > 200)
                    return false;
                return true;
            }

            if (parts.Length == 1
                && double.TryParse(typed, NumberStyles.Number, CultureInfo.InvariantCulture, out double box)
                && double.IsFinite(box) && box > 0)
            {
                mode = RenkoBoxMode.Fixed;
                size = box;
                return true;
            }

            return false;
        }

        /// <summary>
        /// A typed quantity or price: digits with at most one dot, positive and finite, parsed
        /// with the invariant culture. Commas, exponents, signs and words are refused — money
        /// is never interpreted generously.
        /// </summary>
        public static bool TryParsePositiveDecimal(string? text, out double value)
        {
            value = 0;
            string typed = (text ?? "").Trim();
            if (typed.Length == 0) return false;

            int dots = 0;
            foreach (char c in typed)
            {
                if (c == '.') { if (++dots > 1) return false; }
                else if (!char.IsAsciiDigit(c)) return false;
            }
            if (typed[0] == '.' || typed[^1] == '.') return false;

            return double.TryParse(typed, NumberStyles.AllowDecimalPoint,
                       CultureInfo.InvariantCulture, out value)
                   && double.IsFinite(value) && value > 0;
        }

        private static GoBarParseResult WithArgument(GoBarVerb verb, string argument)
        {
            switch (verb)
            {
                case GoBarVerb.Timeframe:
                    if (!TryParseTimeframe(argument, out string tf, out string words))
                        return new GoBarParseResult(GoBarParseKind.Invalid, verb, argument,
                            Refusal: $"{argument} is not a timeframe. Try 4h, 15m or 1d.");
                    return new GoBarParseResult(GoBarParseKind.Argument, verb, argument, tf, words);

                case GoBarVerb.PriceAlert:
                    if (!TryParsePrice(argument, out double price))
                        return new GoBarParseResult(GoBarParseKind.Invalid, verb, argument,
                            Refusal: "A price must be more than zero, like ALERT 95000.");
                    return new GoBarParseResult(GoBarParseKind.Argument, verb, argument, Price: price);

                case GoBarVerb.Renko:
                case GoBarVerb.RangeBars:
                case GoBarVerb.LineBreak:
                case GoBarVerb.Kagi:
                    var builtStyle = BuiltStyle(verb);
                    if (!TryParseBuiltChart(builtStyle, argument, out _))
                        return new GoBarParseResult(GoBarParseKind.Invalid, verb, argument,
                            Refusal: BrickChartSpeech.BadArgumentFor(builtStyle));
                    return new GoBarParseResult(GoBarParseKind.Argument, verb, argument);

                case GoBarVerb.GoTo:
                    if (TryParseDate(argument, out DateTime moment, out bool hasTime, out string? refusal))
                    {
                        if (refusal != null)
                            return new GoBarParseResult(GoBarParseKind.Invalid, verb, argument,
                                Refusal: refusal);
                        return new GoBarParseResult(GoBarParseKind.Argument, verb, argument,
                            Moment: moment, MomentHasTime: hasTime);
                    }
                    // Not a date: an event name. Resolution happens against the chart's events.
                    return new GoBarParseResult(GoBarParseKind.Argument, verb, argument);

                default:
                    return new GoBarParseResult(GoBarParseKind.Argument, verb, argument);
            }
        }

        /// <summary>
        /// A verb at the start. <paramref name="consumed"/> is 1, or 2 for "go to". The caller
        /// takes the tokens from <c>consumed</c> to <c>length - consumed</c>.
        /// </summary>
        private static bool TryVerbAtStart(string[] tokens, out GoBarVerb verb, out int consumed)
        {
            verb = default;
            consumed = 0;
            if (tokens.Length == 0) return false;

            if (tokens.Length >= 2 && IsWord(tokens[0], "GO") && IsWord(tokens[1], "TO"))
            {
                verb = GoBarVerb.GoTo;
                consumed = 2;
                return true;
            }

            if (TrySingleWord(tokens[0], out verb))
            {
                consumed = 1;
                return true;
            }

            return false;
        }

        private static bool TryVerbAtEnd(string[] tokens, out GoBarVerb verb, out int consumed)
        {
            verb = default;
            consumed = 0;
            if (tokens.Length == 0) return false;

            if (tokens.Length >= 2
                && IsWord(tokens[^2], "GO") && IsWord(tokens[^1], "TO"))
            {
                verb = GoBarVerb.GoTo;
                consumed = 2;
                return true;
            }

            if (TrySingleWord(tokens[^1], out verb))
            {
                consumed = 1;
                return true;
            }

            return false;
        }

        private static bool TrySingleWord(string token, out GoBarVerb verb)
        {
            string upper = token.ToUpperInvariant();
            foreach (var spec in GoBarVerbs.All)
            {
                if (spec.Words.Contains(upper, StringComparer.Ordinal))
                {
                    verb = spec.Verb;
                    return true;
                }
            }
            verb = default;
            return false;
        }

        private static bool IsWord(string token, string upper) =>
            string.Equals(token.ToUpperInvariant(), upper, StringComparison.Ordinal);

        /// <summary>
        /// A timeframe in the app's own form (<c>4h</c>, <c>15m</c>, <c>1M</c>) or with a spelled
        /// unit (<c>4 hours</c>, <c>15min</c>). Normalised to the app's form, capped at 999 like
        /// the toolbar. Unit case matters for the app's form: <c>m</c> is minutes, <c>M</c> months.
        /// </summary>
        public static bool TryParseTimeframe(string? text, out string timeframe, out string words)
        {
            timeframe = "";
            words = "";
            string typed = (text ?? "").Trim();
            if (typed.Length == 0) return false;

            int number;
            string unit;

            var app = AppTimeframe.Match(typed);
            if (app.Success)
            {
                if (!int.TryParse(app.Groups[1].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out number)) return false;
                unit = app.Groups[2].Value;
            }
            else
            {
                var spelled = SpelledTimeframe.Match(typed);
                if (!spelled.Success) return false;
                if (!int.TryParse(spelled.Groups[1].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out number)) return false;

                string raw = spelled.Groups[2].Value;
                unit = raw.ToLowerInvariant() switch
                {
                    "m" or "min" or "mins" or "minute" or "minutes" => "m",
                    "h" or "hr" or "hrs" or "hour" or "hours" => "h",
                    "d" or "day" or "days" => "d",
                    "w" or "wk" or "wks" or "week" or "weeks" => "w",
                    "mo" or "mos" or "month" or "months" => "M",
                    _ => "",
                };
                if (unit.Length == 0) return false;
            }

            number = Math.Min(number, 999);
            if (number < 1) return false;

            timeframe = $"{number}{unit}";
            words = $"{number} {UnitWords(unit, number)}";
            return true;
        }

        public static string UnitWords(string unit, int number) => unit switch
        {
            "m" => number == 1 ? "minute" : "minutes",
            "h" => number == 1 ? "hour" : "hours",
            "d" => number == 1 ? "day" : "days",
            "w" => number == 1 ? "week" : "weeks",
            "M" => number == 1 ? "month" : "months",
            _ => unit,
        };

        /// <summary>Words for a timeframe already in app form, or the text itself when it is not.</summary>
        public static string WordsFor(string? timeframe)
            => TryParseTimeframe(timeframe, out _, out string words) ? words : (timeframe ?? "");

        /// <summary>
        /// <c>95000</c>, <c>95,000</c>, <c>95000.5</c>, <c>95k</c>, <c>1.2m</c>. Never negative,
        /// never zero: a price alert at either cannot mean anything.
        /// </summary>
        public static bool TryParsePrice(string? text, out double price)
        {
            price = 0;
            string typed = (text ?? "").Trim();
            if (typed.Length == 0) return false;
            if (typed.StartsWith('-') || typed.StartsWith('\u2212')) return false;

            string digits = typed.Replace(",", "");
            double multiplier = 1;
            if (digits.EndsWith('k') || digits.EndsWith('K'))
            {
                multiplier = 1_000;
                digits = digits[..^1];
            }
            else if (digits.EndsWith('m') || digits.EndsWith('M'))
            {
                multiplier = 1_000_000;
                digits = digits[..^1];
            }

            if (!double.TryParse(digits, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out double value)) return false;

            value *= multiplier;
            if (!double.IsFinite(value) || value <= 0) return false;
            price = value;
            return true;
        }

        /// <summary>
        /// ISO and long-form dates. Returns true when the text is recognisably a date
        /// (<paramref name="moment"/> set), or when it is a numeric date that must be refused
        /// (<paramref name="refusal"/> set). Returns false for ordinary words — GOTO's event
        /// names — which the caller resolves against the chart.
        /// </summary>
        public static bool TryParseDate(
            string? text, out DateTime moment, out bool hasTime, out string? refusal)
        {
            moment = default;
            hasTime = false;
            refusal = null;
            string typed = (text ?? "").Trim();
            if (typed.Length == 0) return false;

            if (SlashDate.IsMatch(typed))
            {
                refusal = DateRefusal;
                return true;
            }

            hasTime = typed.Contains(':');

            if (DateTime.TryParseExact(typed, DateFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out moment))
                return true;

            // Month names are typed in whatever case the hand found; exact parsing is
            // case-sensitive on some platforms, so try a title-cased spelling too.
            string titled = string.Join(' ', typed.Split(' ')
                .Select(w => w.Length <= 1 ? w.ToUpperInvariant()
                    : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

            if (!string.Equals(titled, typed, StringComparison.Ordinal)
                && DateTime.TryParseExact(titled, DateFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out moment))
                return true;

            if (IsoLookingDate.IsMatch(typed))
            {
                refusal = DateRefusal;
                return true;
            }

            return false;
        }

        /// <summary>Trim and collapse every run of whitespace to one space. Case is left alone
        /// for the argument; verb matching upper-cases each token itself.</summary>
        private static string Collapse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
