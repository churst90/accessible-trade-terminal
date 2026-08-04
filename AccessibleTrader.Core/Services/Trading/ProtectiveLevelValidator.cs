using System;
using System.Globalization;
using AccessibleTrader.Core.Services.Accessibility;

namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>Which protective order a level belongs to.</summary>
    public enum ProtectiveLevel
    {
        /// <summary>The price at which the position is abandoned for a loss.</summary>
        StopLoss,

        /// <summary>The price at which the position is closed for a gain.</summary>
        TakeProfit,
    }

    /// <summary>
    /// Checks a hand-typed stop-loss or take-profit before it is sent anywhere.
    ///
    /// <para>
    /// ── Why this is not left to the exchange ───────────────────────────────────
    /// A stop on the wrong side of the market is not rejected by every venue — several will accept
    /// it and trigger it <b>immediately</b>, closing the position at market the instant it is
    /// placed. So "the broker will catch it" is not true, and the failure it does not catch is the
    /// expensive one: a long protected by a stop <i>above</i> the price is a position that liquidates
    /// itself on submission.
    /// </para>
    ///
    /// <para>
    /// ── Why it lives in Core rather than in the dialog ─────────────────────────
    /// The rule inverts with direction — a long's stop sits below the price and its target above; a
    /// short's are the other way round — and that inversion is exactly the kind of thing that gets
    /// written correctly in one place and backwards in another. One function, unit tested, called by
    /// every editor.
    /// </para>
    /// </summary>
    public static class ProtectiveLevelValidator
    {
        /// <summary>The outcome of checking a typed level.</summary>
        /// <param name="Ok">Whether the value may be sent.</param>
        /// <param name="Value">The parsed price. Meaningless unless <paramref name="Ok"/>.</param>
        /// <param name="Message">
        /// What to tell the user — the reason for a refusal, or a confirmation of what was accepted.
        /// Never empty: a silent rejection in a text field is indistinguishable from a field that
        /// does nothing.
        /// </param>
        public record Result(bool Ok, double Value, string Message);

        /// <summary>
        /// Validates typed text as a protective level for a position.
        /// </summary>
        /// <param name="text">Exactly what the user typed.</param>
        /// <param name="level">Which protective order this is.</param>
        /// <param name="isLong">True for a long position; the rule inverts for a short.</param>
        /// <param name="currentPrice">The market price the level is judged against.</param>
        public static Result Validate(string? text, ProtectiveLevel level, bool isLong, double currentPrice)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new Result(false, 0, "Enter a price, or press Escape to leave it unchanged.");

            // Accept what a person types: grouping separators, a currency symbol, stray spaces.
            string cleaned = text.Trim().Replace(",", "").Replace("$", "").Replace(" ", "");

            if (!double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return new Result(false, 0, $"\"{text.Trim()}\" is not a price.");

            if (!double.IsFinite(value) || value <= 0)
                return new Result(false, 0, "A price must be greater than zero.");

            if (currentPrice <= 0)
                return new Result(false, 0, "There is no current price to check that against, so it was not changed.");

            // The direction rule. Stated once, inverted by position side.
            bool mustBeBelow = level == ProtectiveLevel.StopLoss ? isLong : !isLong;
            string side = isLong ? "long" : "short";
            string name = level == ProtectiveLevel.StopLoss ? "stop loss" : "take profit";
            string price = SpeechPriceFormatter.FormatPrice(currentPrice);

            if (mustBeBelow && value >= currentPrice)
                return new Result(false, value,
                    $"A {name} for a {side} position must be below the current price of {price}. "
                  + (level == ProtectiveLevel.StopLoss
                        ? "Placed above it, this would close the position immediately."
                        : "Placed above it, this target is already behind you."));

            if (!mustBeBelow && value <= currentPrice)
                return new Result(false, value,
                    $"A {name} for a {side} position must be above the current price of {price}. "
                  + (level == ProtectiveLevel.StopLoss
                        ? "Placed below it, this would close the position immediately."
                        : "Placed below it, this target is already behind you."));

            return new Result(true, value,
                $"{char.ToUpperInvariant(name[0])}{name.Substring(1)} set to {SpeechPriceFormatter.FormatPrice(value)}.");
        }

        /// <summary>
        /// How far a level sits from the price, as a percentage — for the spoken row, where "stop
        /// 63,300" alone does not say whether that is prudent or a hair's breadth away.
        /// </summary>
        public static string DistanceFromPrice(double level, double currentPrice)
        {
            if (level <= 0 || currentPrice <= 0) return "";
            double pct = Math.Abs(level - currentPrice) / currentPrice * 100.0;
            return $"{pct.ToString("0.##", CultureInfo.InvariantCulture)}% away";
        }
    }
}
