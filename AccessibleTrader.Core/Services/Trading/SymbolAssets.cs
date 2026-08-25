namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>
    /// Splits a trading symbol into the two assets it is a trade between.
    ///
    /// <para>
    /// ── Why this matters more than it looks ────────────────────────────────────
    /// Every quantity in a trading interface is a quantity <i>of something</i>, and saying "0.714
    /// units" names the number while withholding the noun. On <c>BTCUSDT</c> the position is
    /// measured in <b>BTC</b> — the base asset, the thing you end up holding — while the money you
    /// spend, the risk, and the P&amp;L are all in <b>USDT</b>, the quote. Two different assets in
    /// one sentence, and a reader who cannot see the screen has nothing to disambiguate them.
    /// </para>
    ///
    /// <para>
    /// ── Why a suffix list rather than a clever rule ────────────────────────────
    /// Exchange symbols carry no separator, so <c>KASUSDT</c> has to be cut somewhere and only
    /// knowledge of real quote currencies says where. Longest match first, because <c>USDT</c> and
    /// <c>USD</c> both match the tail of <c>BTCUSDT</c> and only one of them is right.
    /// </para>
    /// </summary>
    public static class SymbolAssets
    {
        /// <summary>
        /// Quote currencies, longest first so the longest match wins.
        ///
        /// <para>
        /// Crypto quotes and fiat, plus <c>BTC</c> and <c>ETH</c> which are themselves quotes on
        /// pairs like <c>KASBTC</c>. That ordering matters: <c>ETHBTC</c> must resolve to ETH priced
        /// in BTC, not to something priced in ETH.
        /// </para>
        /// </summary>
        private static readonly string[] Quotes = new[]
        {
            "USDT", "USDC", "BUSD", "TUSD", "FDUSD", "DAI",
            "USD", "EUR", "GBP", "JPY", "CHF", "CAD", "AUD",
            "BTC", "ETH", "BNB",
        }.OrderByDescending(q => q.Length).ToArray();

        /// <summary>
        /// The base and quote of a symbol.
        /// </summary>
        /// <param name="Base">What you hold a position in — the thing a quantity counts.</param>
        /// <param name="Quote">What it is priced in — the thing money, risk and P&amp;L are measured in.</param>
        /// <param name="Recognised">
        /// False when the symbol could not be split. The caller must then fall back to neutral
        /// wording rather than guessing: naming the wrong asset next to a number is worse than
        /// naming none.
        /// </param>
        public record Pair(string Base, string Quote, bool Recognised);

        public static Pair Split(string? symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return new Pair("", "", false);

            // Some providers already separate the two — BTC/USDT, BTC-USD, BTC_USDT.
            foreach (char sep in new[] { '/', '-', '_' })
            {
                int i = symbol.IndexOf(sep);
                if (i > 0 && i < symbol.Length - 1)
                    return new Pair(symbol[..i].ToUpperInvariant(),
                                    symbol[(i + 1)..].ToUpperInvariant(), true);
            }

            string s = symbol.Trim().ToUpperInvariant();
            foreach (string q in Quotes)
            {
                // The base must be non-empty: "USDT" alone is not USDT priced in nothing.
                if (s.Length > q.Length && s.EndsWith(q, StringComparison.Ordinal))
                    return new Pair(s[..^q.Length], q, true);
            }

            return new Pair(s, "", false);
        }

        /// <summary>
        /// A quantity with its unit — "0.714 BTC" rather than "0.714 units".
        ///
        /// <para>
        /// Falls back to "units" only when the symbol could not be split, because a wrong unit beside
        /// a position size is worse than a vague one.
        /// </para>
        /// </summary>
        public static string WithBaseUnit(double quantity, string? symbol, string formatted)
        {
            var p = Split(symbol);
            return p.Recognised && p.Base.Length > 0 ? $"{formatted} {p.Base}" : $"{formatted} units";
        }

        /// <summary>The asset money is counted in for this symbol, or an empty string if unknown.</summary>
        public static string QuoteOf(string? symbol)
        {
            var p = Split(symbol);
            return p.Recognised ? p.Quote : "";
        }
    }
}
