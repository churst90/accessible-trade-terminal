using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Alerts
{
    /// <summary>
    /// Which chart an alert belongs to: ONE answer for every place that evaluates alerts (the
    /// focused chart's <c>AlertOrchestrator</c>, each background tab's
    /// <c>BackgroundWorkspaceMonitor</c>) and for the coverage claims that tell the headless
    /// monitors what an open browser is already watching.
    ///
    /// <para>
    /// ── The defect this closes (Cody, 2026-11-22) ─────────────────────────────
    /// An alert records the symbol, provider and timeframe of the chart it was set on, but only
    /// the symbol was ever checked; the timeframe was "informational / future routing". Cody had
    /// six BTCUSDT tabs open (1d, 4h, 1h, 15m, 1w, 1M) with background tabs monitored every ten
    /// seconds, and set "price touches 61,450" on one of them. Every tab tested it against its
    /// own newest candle: the day, week and month candles all spanned 61,450, so they touched.
    /// Each tab's candle has its own date, so the "already fired for this bar" memory, keyed on
    /// the alert, was reset by the next tab and the alert fired again on every poll: "it kept
    /// firing over and over when the price hadn't even been near that price".
    /// </para>
    ///
    /// <para>
    /// An alert now belongs to the chart it was set on. A part the alert does not record (an
    /// alert written before scoping, or a "current chart" alert with no symbol) matches any
    /// chart, exactly as before.
    /// </para>
    /// </summary>
    public static class AlertScope
    {
        /// <summary>
        /// Whether <paramref name="alert"/> is for the chart shown as
        /// <paramref name="symbolDisplayName"/> with <paramref name="identity"/>. The symbol
        /// matches either spelling (the display name or the identity's raw symbol), as the
        /// coverage claims do.
        /// </summary>
        public static bool AppliesTo(AlertDefinition alert, string? symbolDisplayName, ChartIdentity identity)
        {
            if (alert.Symbol != null
                && !string.Equals(alert.Symbol.Trim(), symbolDisplayName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(alert.Symbol.Trim(), identity.Symbol, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(alert.Timeframe) && !string.IsNullOrWhiteSpace(identity.Timeframe)
                && !SameTimeframe(alert.Timeframe!, identity.Timeframe))
                return false;

            if (!string.IsNullOrWhiteSpace(alert.Provider) && !string.IsNullOrWhiteSpace(identity.Provider)
                && !string.Equals(alert.Provider!.Trim(), identity.Provider.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        /// <summary>
        /// Two spellings of one timeframe. Case matters: "1M" is a month and "1m" a minute, so a
        /// case-insensitive compare would put Cody's monthly alert on his one-minute chart.
        /// </summary>
        public static bool SameTimeframe(string a, string b)
        {
            a = a.Trim(); b = b.Trim();
            if (string.Equals(a, b, StringComparison.Ordinal)) return true;
            long ma = TimeframeUtility.ToMilliseconds(a), mb = TimeframeUtility.ToMilliseconds(b);
            return ma > 0 && ma == mb;
        }

        /// <summary>
        /// The coverage keys one chart claims: its symbol alone (what bar closes and alerts that
        /// name no timeframe are suppressed by), the symbol with its timeframe, and the symbol
        /// with its timeframe and provider. Every coverage set is case-insensitive, which is why
        /// the timeframe goes in as a length and never as its spelling.
        /// </summary>
        public static IEnumerable<string> CoverageKeys(string? symbol, string? timeframe, string? provider)
        {
            if (string.IsNullOrWhiteSpace(symbol)) yield break;
            yield return symbol.Trim();
            if (string.IsNullOrWhiteSpace(timeframe)) yield break;
            yield return Key(symbol, timeframe, null);
            if (!string.IsNullOrWhiteSpace(provider)) yield return Key(symbol, timeframe, provider);
        }

        /// <summary>
        /// The coverage key that says this alert is already being watched: the narrowest one it
        /// records. An alert with no timeframe is covered by its symbol being on screen anywhere,
        /// as before; a 4-hour alert only by a 4-hour chart of its symbol.
        /// </summary>
        public static string CoverageKey(AlertDefinition alert, string symbol) =>
            string.IsNullOrWhiteSpace(alert.Timeframe) ? symbol.Trim()
                : Key(symbol, alert.Timeframe!, string.IsNullOrWhiteSpace(alert.Provider) ? null : alert.Provider);

        private static string Key(string symbol, string timeframe, string? provider)
        {
            long ms = TimeframeUtility.ToMilliseconds(timeframe.Trim());
            string tf = ms > 0 ? ms.ToString(System.Globalization.CultureInfo.InvariantCulture) : timeframe.Trim();
            return provider == null ? $"{symbol.Trim()}|{tf}" : $"{symbol.Trim()}|{tf}|{provider.Trim()}";
        }
    }
}
