namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Symbol shape conversions shared across providers. The app's canonical symbol
    /// is separator-flexible (BTC/USD, BTC-USD, BTCUSD); each exchange wants a
    /// specific wire shape. Base/quote splitting was reimplemented per provider —
    /// this centralizes it against a known quote list.
    /// </summary>
    public static class SymbolFormat
    {
        // Longest-first so "FDUSD"/"USDT" win over "USD". The original 8-entry
        // list is why MEXC produced "BTCTUSD" (TUSD missing) and why Oanda could
        // not use this helper at all (no JPY/AUD/CAD/CHF/NZD).
        private static readonly string[] KnownQuotes =
        {
            "FDUSD",
            "USDT", "USDC", "TUSD", "BUSD",
            "USD", "EUR", "GBP", "JPY", "AUD", "CAD", "CHF", "NZD",
            "DAI", "BTC", "XBT", "ETH", "BNB",
        };

        /// <summary>Splits into (base, quote) using the known quote suffixes. Falls back
        /// to (whole, "") when no known quote matches.</summary>
        public static (string Base, string Quote) SplitBaseQuote(string symbol)
        {
            var s = Concatenated(symbol);
            // Longest quote first, but a split that leaves a base shorter than
            // 3 characters loses to one that doesn't: "XBTUSD" ends with "TUSD"
            // yet is XBT/USD, not XB/TUSD — no real base asset is 2 letters,
            // while overlapping quote suffixes are common.
            (string Base, string Quote)? shortBase = null;
            foreach (var q in KnownQuotes)
            {
                if (!s.EndsWith(q, StringComparison.OrdinalIgnoreCase) || s.Length <= q.Length) continue;
                var b = s[..^q.Length];
                if (b.Length >= 3) return (b, q);
                shortBase ??= (b, q);
            }
            return shortBase ?? (s, string.Empty);
        }

        /// <summary>Separator-free upper-case pair (BTCUSDT).</summary>
        public static string Concatenated(string symbol) =>
            symbol.Replace("/", "").Replace("-", "").Replace("_", "").ToUpperInvariant();

        /// <summary>base + separator + quote (e.g. "BTC_USDT", "BTC/USD").</summary>
        public static string WithSeparator(string symbol, char separator)
        {
            var s = Concatenated(symbol);
            if (symbol.Contains(separator)) return symbol.ToUpperInvariant();
            var (b, q) = SplitBaseQuote(s);
            return q.Length == 0 ? s : $"{b}{separator}{q}";
        }

        public static string Underscored(string symbol) => WithSeparator(symbol, '_');
        public static string Slashed(string symbol)     => WithSeparator(symbol, '/');
    }
}
