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
    /// An instance per account, not a process-wide static. This WAS a static — "equity is a property
    /// of the account rather than of either lifetime" — which was true on the desktop, where the
    /// process has one account. On the multi-user WebHost it meant user A's balance became user B's
    /// sizing input, and B could infer A's account size from their own quick-trade announcements.
    /// The desktop head registers one instance for the process (same behaviour as before); the
    /// WebHost hands each user their own via <see cref="QuickTradeEquityHub"/>, so tabs of one user
    /// still share a value and different users never do.
    /// </para>
    /// </summary>
    public sealed class QuickTradeEquity
    {
        private double _latest;

        /// <summary>Account equity in quote currency, or 0 when nothing has reported one.</summary>
        public double Latest => System.Threading.Volatile.Read(ref _latest);

        /// <summary>
        /// Record an observed equity. Non-finite and negative values are ignored rather than
        /// stored: a provider hiccup must not be able to turn into a position size.
        /// </summary>
        public void Report(double equity)
        {
            if (double.IsFinite(equity) && equity >= 0)
                System.Threading.Volatile.Write(ref _latest, equity);
        }

        /// <summary>Test seam: forget the cached value.</summary>
        internal void Reset() => System.Threading.Volatile.Write(ref _latest, 0);

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

    /// <summary>
    /// One equity cache per user, for the whole process — the same shape as
    /// <c>PaperAccountHub</c> and for the same reason: a Blazor scope is a browser tab, so scoping
    /// the cache would give one user a stale copy per tab, while a process-wide value leaks one
    /// user's balance into another's position sizing. Keyed on <c>ICurrentUser.DataKey</c>.
    /// </summary>
    public sealed class QuickTradeEquityHub
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, QuickTradeEquity> _byUser =
            new(System.StringComparer.Ordinal);

        /// <summary>This user's equity cache, created once on first use.</summary>
        public QuickTradeEquity ForUser(string? userKey)
        {
            if (string.IsNullOrEmpty(userKey)) userKey = "anon";
            return _byUser.GetOrAdd(userKey, _ => new QuickTradeEquity());
        }
    }
}
