using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Which chart a running strategy belongs to.
    ///
    /// <para>
    /// ── Why the whole chart ───────────────────────────────────────────────────────
    /// Until 2026-11-05 a strategy was bound by its display symbol alone. With a Kraken
    /// BTC/USDT 1h tab and a Bitstamp BTC/USDT 15m tab open, a strategy started on the Kraken
    /// tab was evaluated on the Bitstamp 15m bars whenever that tab was in front, and in Auto
    /// mode placed its order on Bitstamp. With both tabs in the background, both monitors ran
    /// it, each on its own bars. A strategy's signals, stops and size come from one chart; it
    /// now runs only on that chart.
    /// </para>
    ///
    /// <para>
    /// Market is not compared: it is the same chart under a composite ("Crypto|Spot") or bare
    /// ("Crypto") market key. Venue, symbol and timeframe are compared ignoring case.
    /// </para>
    /// </summary>
    public static class StrategyBinding
    {
        /// <summary>True when <paramref name="active"/> should be evaluated on this chart.</summary>
        /// <param name="displayName">The chart's display symbol, for a strategy bound the old way.</param>
        public static bool Matches(ActiveStrategy active, ChartIdentity chart, string? displayName)
        {
            if (active.Chart is ChartIdentity bound) return SameChart(bound, chart);
            return string.IsNullOrEmpty(active.Symbol)
                || string.Equals(active.Symbol, displayName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when the strategy is bound to a chart at all (a blank-chart start is not).</summary>
        public static bool IsBound(ActiveStrategy active) =>
            active.Chart != null || !string.IsNullOrEmpty(active.Symbol);

        public static bool SameChart(ChartIdentity a, ChartIdentity b) =>
            string.Equals(a.Provider, b.Provider, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Symbol, b.Symbol, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Timeframe, b.Timeframe, StringComparison.OrdinalIgnoreCase);
    }
}
