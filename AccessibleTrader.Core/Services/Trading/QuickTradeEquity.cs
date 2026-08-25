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
        private string _latestAsset = "";
        private readonly System.Collections.Generic.Dictionary<string, double> _byAsset =
            new(System.StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new();

        /// <summary>
        /// Account equity in ONE cash currency — the largest cash line the account holds — or 0
        /// when nothing has reported a balance yet. <see cref="LatestAsset"/> names which.
        ///
        /// <para>
        /// This used to be the <i>sum</i> of every cash line, which was a category error one level
        /// down from the BTC-plus-USDT sum <see cref="IsCashAsset"/> exists to prevent: an account
        /// holding ¥1,000,000 and $2,000 reported equity 1,002,000, and a 1% quick trade then sized
        /// a ~$10,020 position against a ~$8,700 account. Currencies are not fungible by addition.
        /// </para>
        ///
        /// <para>
        /// The largest single line is a proxy for "the currency this account trades in", not a
        /// derivation of it — the instrument's quote currency is the right answer and this seam
        /// cannot see the instrument. It is chosen because it is never an invented number: whatever
        /// it reports is cash that actually exists, in one currency. Callers that DO know the
        /// instrument should ask <see cref="LatestFor"/> instead.
        /// </para>
        /// </summary>
        public double Latest { get { lock (_gate) return _latest; } }

        /// <summary>The cash asset <see cref="Latest"/> is denominated in, or "" when there is none.</summary>
        public string LatestAsset { get { lock (_gate) return _latestAsset; } }

        /// <summary>
        /// Cash held in one specific currency, for a caller that knows the instrument's quote
        /// asset. Returns 0 for an unheld or non-cash asset — which the sizing path already
        /// refuses on, rather than silently substituting a different currency.
        /// </summary>
        public double LatestFor(string? asset)
        {
            if (string.IsNullOrWhiteSpace(asset)) return 0;
            lock (_gate) return _byAsset.TryGetValue(asset.Trim(), out double v) ? v : 0;
        }

        /// <summary>
        /// Record an observed equity in a single currency. Non-finite and negative values are
        /// ignored rather than stored: a provider hiccup must not be able to turn into a position
        /// size.
        /// </summary>
        public void Report(double equity) => Report(equity, "");

        /// <summary>Record an observed equity, naming the currency it is denominated in.</summary>
        public void Report(double equity, string asset)
        {
            if (!double.IsFinite(equity) || equity < 0) return;
            lock (_gate)
            {
                _latest = equity;
                _latestAsset = asset ?? "";
                if (!string.IsNullOrWhiteSpace(asset)) _byAsset[asset.Trim()] = equity;
            }
        }

        /// <summary>
        /// Record the whole cash side of an account at once: each cash line is kept in its own
        /// currency, and <see cref="Latest"/> becomes the largest of them. Non-cash assets are
        /// dropped here rather than at each call site.
        /// </summary>
        public void ReportCashLines(System.Collections.Generic.IEnumerable<(string Asset, double Amount)> lines)
        {
            var cash = new System.Collections.Generic.Dictionary<string, double>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var (asset, amount) in lines)
            {
                if (!IsCashAsset(asset) || !double.IsFinite(amount) || amount < 0) continue;
                string key = asset.Trim();
                cash[key] = cash.TryGetValue(key, out double had) ? had + amount : amount;
            }

            lock (_gate)
            {
                _byAsset.Clear();
                foreach (var kv in cash) _byAsset[kv.Key] = kv.Value;

                _latest = 0;
                _latestAsset = "";
                foreach (var kv in cash)
                {
                    // Ties broken by name so the chosen currency is stable across refreshes —
                    // a sizing input that flips between two equal balances is a sizing input
                    // that cannot be reasoned about.
                    if (kv.Value > _latest ||
                        (kv.Value == _latest && string.CompareOrdinal(kv.Key, _latestAsset) < 0))
                    {
                        _latest = kv.Value;
                        _latestAsset = kv.Key;
                    }
                }
            }
        }

        /// <summary>Test seam: forget the cached value.</summary>
        internal void Reset()
        {
            lock (_gate)
            {
                _latest = 0;
                _latestAsset = "";
                _byAsset.Clear();
            }
        }

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
