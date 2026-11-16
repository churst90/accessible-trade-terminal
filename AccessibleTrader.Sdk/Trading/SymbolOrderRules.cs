using System.Globalization;

namespace AccessibleTrader.Sdk.Trading
{
    /// <summary>
    /// What a venue says one market will accept: the smallest order, the step a quantity must
    /// be a whole number of, the price tick, the smallest order value, and whether it is
    /// taking new orders at all.
    ///
    /// <para>
    /// <b>Why this exists.</b> Every crypto venue in the terminal sent the ticket's double as
    /// it came, at eight decimals, at ten, or through <c>ToString()</c> (which prints
    /// <c>1E-05</c> for a small number), and none checked a minimum. The venues refuse all of
    /// that in their own codes ("LOT_SIZE", "INVALID_PRICE_PRECISION", "InvalidQuantity"),
    /// which a person placing a real order cannot act on. Kraken got its own reader first
    /// (KrakenPairRules, 2026-11-10); this is the same idea for venues that state their rules
    /// as steps rather than decimal counts. Each provider fills one from its own
    /// exchange-information answer.
    /// </para>
    ///
    /// <para>
    /// Arithmetic is in <see cref="decimal"/> so that a step of 0.001 is exactly 0.001: in
    /// doubles 0.29 / 0.01 is 28.999…, and flooring it would sell one step short.
    /// </para>
    /// </summary>
    public sealed record SymbolOrderRules
    {
        /// <summary>The venue's name as spoken in a refusal ("Gemini").</summary>
        public required string Venue { get; init; }

        /// <summary>The market as spoken in a refusal ("BTC/USD").</summary>
        public required string Symbol { get; init; }

        /// <summary>The unit a quantity is counted in ("BTC", or "contracts" on a venue that
        /// sizes futures in contracts).</summary>
        public string QuantityUnit { get; init; } = "";

        /// <summary>The currency an order's value is counted in ("USD").</summary>
        public string QuoteAsset { get; init; } = "";

        /// <summary>The smallest quantity, or 0 when the venue states none.</summary>
        public decimal MinQuantity { get; init; }

        /// <summary>The step every quantity must be a whole number of, or 0 for none.</summary>
        public decimal QuantityStep { get; init; }

        /// <summary>The step every price must be a whole number of, or 0 for none.</summary>
        public decimal PriceTick { get; init; }

        /// <summary>The smallest order value (quantity × price) in <see cref="QuoteAsset"/>,
        /// or 0 for none.</summary>
        public decimal MinNotional { get; init; }

        /// <summary>Why the venue takes no new orders on this market, in words, or null when it
        /// does.</summary>
        public string? NotTrading { get; init; }

        /// <summary>The quantity the venue will accept: cut DOWN to the step, never up —
        /// rounding up would buy or sell more than was asked for.</summary>
        public decimal FloorQuantity(double quantity)
        {
            decimal q = ToDecimal(quantity);
            if (QuantityStep <= 0 || q <= 0) return q;
            return decimal.Floor(q / QuantityStep) * QuantityStep;
        }

        /// <summary>The nearest price the market can carry.</summary>
        public decimal RoundPrice(double price)
        {
            decimal p = ToDecimal(price);
            if (PriceTick <= 0) return p;
            return decimal.Round(p / PriceTick, 0, MidpointRounding.AwayFromZero) * PriceTick;
        }

        public string FormatQuantity(double quantity) => Plain(FloorQuantity(quantity));

        public string FormatPrice(double price) => Plain(RoundPrice(price));

        /// <summary>
        /// Why the venue would refuse this order, in words, or null when it would not on these
        /// grounds. <paramref name="price"/> is the limit or trigger, or the last price for a
        /// market order when the caller has one; with none, the order value is not checked.
        /// </summary>
        public string? Refusal(double quantity, double? price)
        {
            if (NotTrading != null) return NotTrading;

            decimal q = FloorQuantity(quantity);
            if (q <= 0 || (MinQuantity > 0 && q < MinQuantity))
            {
                decimal least = MinQuantity > 0 ? MinQuantity : QuantityStep;
                decimal asked = ToDecimal(quantity);
                return $"{Venue}'s smallest {Symbol} order is {Plain(least)} {Unit(least, QuantityUnit)}".TrimEnd()
                     + $"; this one is {Plain(asked)} {Unit(asked, QuantityUnit)}".TrimEnd();
            }

            if (price is > 0 && MinNotional > 0)
            {
                decimal value = q * ToDecimal(price.Value);
                if (value < MinNotional)
                    return $"{Venue}'s smallest {Symbol} order is worth {Plain(MinNotional)} {QuoteAsset}".TrimEnd()
                         + $"; this one is worth {Plain(decimal.Round(value, 8))} {QuoteAsset}".TrimEnd();
            }
            return null;
        }

        /// <summary>The unit for a count of it: "contracts" is "contract" for exactly one, so a
        /// refusal never says "1 contracts". Asset codes (BTC, USD) are the same for any count.</summary>
        public static string Unit(decimal count, string unit) =>
            count == 1 && unit == "contracts" ? "contract" : unit;

        /// <summary>A number as the venues want it: invariant, never in exponent form, no
        /// trailing zeros. <c>0.00001.ToString()</c> is <c>1E-05</c>, which Bitstamp and
        /// others refuse.</summary>
        public static string Plain(decimal value) =>
            value.ToString("0.############################", CultureInfo.InvariantCulture);

        public static string Plain(double value) => Plain(ToDecimal(value));

        /// <summary>Reads a venue's step or minimum, which arrive as strings ("0.00001000"),
        /// numbers, or a count of decimals elsewhere; an unreadable one is 0, meaning "none".</summary>
        public static decimal ParseStep(string? text) =>
            decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 ? d : 0m;

        /// <summary>The step for a count of decimals: 3 → 0.001.</summary>
        public static decimal StepFromDecimals(int decimals) =>
            decimals <= 0 ? 1m : 1m / (decimal)Math.Pow(10, Math.Min(decimals, 28));

        private static decimal ToDecimal(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0m;
            // The decimal conversion keeps 15 significant digits, which drops the binary
            // noise in 0.29 (0.28999999999999998) the way the user typed it.
            try { return (decimal)v; } catch (OverflowException) { return 0m; }
        }
    }
}
