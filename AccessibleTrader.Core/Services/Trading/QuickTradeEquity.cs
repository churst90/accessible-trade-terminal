namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>
    /// The most recently observed account equity, published by whatever last read a balance.
    ///
    /// <para>
    /// A cached number rather than a live fetch, and that is the point. Position sizing happens on a
    /// keystroke, in the middle of a decision — an <c>await</c> to a broker there would either block
    /// the arming announcement behind a network round trip or make the spoken size arrive after the
    /// user had already pressed Enter. Neither is acceptable when the output is an order quantity.
    /// </para>
    ///
    /// <para>
    /// <b>Zero is the honest default and the arming path refuses on it.</b> An equity of zero means
    /// nothing has reported a balance yet, and a position sized from a made-up account value is
    /// worse than no quick-trade feature at all — it would be confidently wrong about the one number
    /// that decides how much money is at stake.
    /// </para>
    ///
    /// <para>
    /// Deliberately not a service with a lifetime: the WebHost scopes per circuit and the desktop
    /// head is a singleton, and equity is a property of the account rather than of either. This
    /// keeps one value for the process and avoids a stale copy per browser tab.
    /// </para>
    /// </summary>
    public static class QuickTradeEquity
    {
        private static double _latest;

        /// <summary>Account equity in quote currency, or 0 when nothing has reported one.</summary>
        public static double Latest => System.Threading.Volatile.Read(ref _latest);

        /// <summary>
        /// Record an observed equity. Non-finite and negative values are ignored rather than
        /// stored: a provider hiccup must not be able to turn into a position size.
        /// </summary>
        public static void Report(double equity)
        {
            if (double.IsFinite(equity) && equity >= 0)
                System.Threading.Volatile.Write(ref _latest, equity);
        }

        /// <summary>Test seam: forget the cached value.</summary>
        internal static void Reset() => System.Threading.Volatile.Write(ref _latest, 0);

        /// <summary>
        /// Whether a balance line is cash the risk budget can be a percentage of.
        ///
        /// <para>
        /// Balances arrive per asset and in their own units. Summing them — 0.5 BTC plus 3,000 USDT
        /// plus 12 ETH — produces a number that is not money in any currency, and that number would
        /// then be multiplied by a risk percentage to size a real order. So only cash counts.
        /// </para>
        ///
        /// <para>
        /// Deliberately a small explicit list rather than a clever rule. "Contains USD" would catch
        /// every wrapped and synthetic dollar-named token on a crypto exchange, several of which are
        /// not dollars and one of which is usually somebody's failed algorithmic stablecoin.
        /// </para>
        /// </summary>
        public static bool IsCashAsset(string? asset) => asset is not null && CashAssets.Contains(asset.Trim());

        private static readonly System.Collections.Generic.HashSet<string> CashAssets =
            new(System.StringComparer.OrdinalIgnoreCase)
            { "USD", "USDT", "USDC", "BUSD", "DAI", "EUR", "GBP", "CAD", "AUD", "JPY", "CHF" };
    }
}
