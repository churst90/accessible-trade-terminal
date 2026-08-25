using System.Text.RegularExpressions;

namespace AccessibleTrader.Sdk.Services
{
    /// <summary>
    /// Shared defense against path/query injection via provider symbols.
    /// Providers interpolate <c>symbol</c> directly into REST URLs; even with an
    /// allow-listed HttpClient, a crafted symbol like <c>"BTCUSD/status?x=1"</c>
    /// can still land on an unintended endpoint on the approved host. Every
    /// provider's fetch/order-book/live-subscribe path should run symbols
    /// through <see cref="Validate"/> before any URL or signing operation.
    /// </summary>
    public static class SymbolValidator
    {
        // Allowed: ASCII letters, digits, and the punctuation real providers use:
        //   - "/"  Coinbase (BTC-USD→BTC/USD forms), Oanda (EUR_USD), some crypto pairs
        //   - "-"  Coinbase, Alpaca, Polygon ("BTC-USD")
        //   - "_"  Oanda ("EUR_USD"), Binance Futures ("BTC_USDT")
        //   - "."  Some exchanges use dotted root tickers ("BRK.B")
        //   - ":"  IBKR-style routed tickers ("AAPL:NASDAQ") and some futures codes
        private static readonly Regex AllowedChars = new("^[A-Za-z0-9_./\\-:]+$", RegexOptions.Compiled);

        // Hard caps — no real ticker approaches these; used to defuse
        // resource-exhaustion attempts (giant regex backtracking, huge URLs).
        public const int MinLength = 1;
        public const int MaxLength = 32;

        /// <summary>
        /// Returns true if <paramref name="symbol"/> contains only the characters
        /// expected of a market symbol. Rejects null/empty, length outliers, and
        /// anything that could introduce a URL path segment, query parameter, or
        /// control character.
        /// </summary>
        public static bool IsValid(string? symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return false;
            if (symbol.Length < MinLength || symbol.Length > MaxLength) return false;
            // Reject path-traversal dots regardless of the regex.
            if (symbol.Contains("..", StringComparison.Ordinal)) return false;
            // Reject leading/trailing separators that can canonicalise to traversal.
            if (symbol.StartsWith('/') || symbol.StartsWith('\\')) return false;
            return AllowedChars.IsMatch(symbol);
        }

        /// <summary>
        /// Throws <see cref="ArgumentException"/> if the symbol is not valid. Use this
        /// at provider entry points (FetchOhlcvAsync, SubscribeLive, OrderBookAsync)
        /// before any URL interpolation or signed-request construction.
        /// </summary>
        public static void Validate(string? symbol, string providerId)
        {
            if (!IsValid(symbol))
                throw new ArgumentException(
                    $"Invalid symbol for provider {providerId}. Symbols must be 1–{MaxLength} chars of [A-Za-z0-9_./:-].",
                    nameof(symbol));
        }
    }
}
