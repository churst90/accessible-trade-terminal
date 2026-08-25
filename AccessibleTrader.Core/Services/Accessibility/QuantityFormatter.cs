using System.Globalization;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Renders an order size or quantity for display and for speech.
    ///
    /// <para>
    /// ── The defect this replaced ───────────────────────────────────────────────
    /// Order book sizes were formatted with <c>ToString("G4")</c>. The <c>G</c> specifier switches
    /// to <b>scientific notation</b> as soon as the exponent reaches the precision, so a Kaspa book
    /// with 74,200,000 units on the bid rendered as <c>7.42E+07</c>. Read aloud by a screen reader
    /// that is "seven point four two E plus zero seven", which is not a quantity anybody can act on.
    /// Prices had the same problem in the other direction: <c>G6</c> turns 0.0000123 into
    /// <c>1.23E-05</c>.
    /// </para>
    ///
    /// <para>
    /// ── Why magnitude has to drive the format ──────────────────────────────────
    /// This terminal shows instruments whose sizes span roughly twelve orders of magnitude: 0.0034
    /// BTC and 74,000,000 KAS are both ordinary. No single precision works. So the rule is stated in
    /// terms of what a reader needs — a few significant figures, thousands separated, and never an
    /// exponent — rather than in terms of a format string that happens to look right on the one
    /// instrument it was written against.
    /// </para>
    /// </summary>
    public static class QuantityFormatter
    {
        /// <summary>
        /// A size as digits: grouped, never in scientific notation, with decimals only where they
        /// carry information.
        ///
        /// <para>
        /// Large sizes get no decimals — the tenth of a unit in 74,200,000 is noise. Small ones keep
        /// enough figures to distinguish 0.0034 from 0.0035, which on an expensive instrument is a
        /// real difference in money.
        /// </para>
        /// </summary>
        public static string Format(double quantity)
        {
            if (double.IsNaN(quantity)) return "—";
            if (double.IsInfinity(quantity)) return quantity > 0 ? "∞" : "-∞";

            double abs = Math.Abs(quantity);

            string pattern =
                abs >= 1_000 ? "N0" :        //     74,200,000   / 1,234
                abs >= 1     ? "N2" :        //          12.50
                abs >= 0.01  ? "N4" :        //           0.0340
                abs > 0      ? "N8" :        //           0.00003400
                               "N0";         //           0

            return quantity.ToString(pattern, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A size for a dense table or a spoken list, where 74,200,000 is more work to take in than
        /// "74.2 million".
        ///
        /// <para>
        /// Only above a million. Abbreviating 12,400 to "12.4K" saves nothing and costs precision,
        /// and an order book's whole job is comparing sizes at a glance.
        /// </para>
        /// </summary>
        public static string FormatCompact(double quantity)
        {
            if (double.IsNaN(quantity)) return "—";
            if (double.IsInfinity(quantity)) return quantity > 0 ? "∞" : "-∞";

            double abs = Math.Abs(quantity);
            if (abs < 1_000_000) return Format(quantity);

            string sign = quantity < 0 ? "-" : "";
            if (abs >= 1_000_000_000)
                return sign + (abs / 1_000_000_000).ToString("0.##", CultureInfo.InvariantCulture) + "B";
            return sign + (abs / 1_000_000).ToString("0.##", CultureInfo.InvariantCulture) + "M";
        }

        /// <summary>
        /// The spoken form. Screen readers pronounce "74.2M" unpredictably — sometimes "M", sometimes
        /// "metres" — so speech gets the word.
        /// </summary>
        public static string FormatSpoken(double quantity)
        {
            if (double.IsNaN(quantity)) return "unknown";

            double abs = Math.Abs(quantity);
            if (abs < 1_000_000) return Format(quantity);

            string sign = quantity < 0 ? "minus " : "";
            if (abs >= 1_000_000_000)
                return sign + (abs / 1_000_000_000).ToString("0.##", CultureInfo.InvariantCulture) + " billion";
            return sign + (abs / 1_000_000).ToString("0.##", CultureInfo.InvariantCulture) + " million";
        }
    }
}
