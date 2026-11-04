using System.Globalization;

namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>
    /// The two size conversions the order ticket and the positions table make (2026-11-04).
    ///
    /// <para><b>Size by value.</b> Cody: "I should be able to specify my position size in contracts,
    /// not in btc. right now if i leave it as '1' that means '1 btc' not '1 dollar'". Every provider's
    /// order quantity is in the base asset (BTC on BTC/USD), so a size typed in the quote currency is
    /// turned into base units at the price the order is judged at, and rounded DOWN so the order is
    /// never worth more than was typed.</para>
    ///
    /// <para><b>Close part of a position.</b> Cody: "when i limit close a trade i should have the option
    /// of closing a portion/percentage of the position rather than the whole thing". 100 percent is the
    /// whole position exactly, never a rounded copy of it, so a full close cannot leave dust behind.</para>
    /// </summary>
    public static class OrderSizing
    {
        /// <summary>Base units are kept to eight decimals, the finest any venue here quotes.</summary>
        private const double Step = 1e-8;

        /// <summary>
        /// Base units worth <paramref name="value"/> in the quote currency at <paramref name="price"/>,
        /// rounded down to eight decimals. 0 when either is not a positive number.
        /// </summary>
        public static double QuantityFromValue(double value, double price)
        {
            if (!(value > 0) || !(price > 0) || double.IsInfinity(value) || double.IsInfinity(price)) return 0;
            // The small nudge keeps 1000 / 50000 = 0.02 from flooring to 0.01999999 on a float error.
            return Math.Floor(value / price / Step + 1e-6) * Step;
        }

        /// <summary>
        /// The quantity to close for <paramref name="percent"/> of a position of
        /// <paramref name="positionQuantity"/> (its size, either sign). 100 is the whole position
        /// exactly; less is rounded down to eight decimals. 0 when the percent is outside (0, 100].
        /// </summary>
        public static double PartialCloseQuantity(double positionQuantity, double percent)
        {
            double size = Math.Abs(positionQuantity);
            if (!(percent > 0) || percent > 100 || !(size > 0)) return 0;
            if (percent == 100) return size;
            return Math.Floor(size * percent / 100 / Step + 1e-6) * Step;
        }

        /// <summary>
        /// A percent as typed: "50", "50%", "12.5". Ok only for a number above 0 and at most 100;
        /// otherwise the message says why, in words that can be spoken as they are.
        /// </summary>
        public static (bool Ok, double Value, string Message) ParsePercent(string? text)
        {
            string t = (text ?? "").Trim().TrimEnd('%').Trim();
            if (t.Length == 0) return (false, 0, "Enter a percent to close, from 1 to 100.");
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || double.IsNaN(v))
                return (false, 0, $"\"{text}\" is not a percent. Enter a number from 1 to 100.");
            if (v <= 0) return (false, 0, "The percent to close must be above zero.");
            if (v > 100) return (false, 0, "The percent to close cannot be more than 100.");
            return (true, v, "");
        }
    }
}
