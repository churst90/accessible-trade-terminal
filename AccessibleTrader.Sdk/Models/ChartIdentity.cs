using System;

namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// The four-part address of a chart: which market, which provider, which symbol, which
    /// timeframe. Used as a dictionary key and as the identity a feed is opened against.
    ///
    /// <para>
    /// Equality is <b>case-insensitive on all four parts</b>, matching how provider and market
    /// names are resolved everywhere else — <c>MarketOrchestrator.EnsureContains</c> was made
    /// insensitive explicitly because "the Economic list says 'Fred' while the plugin calls
    /// itself 'FRED'". Until 2026-08-25 this record used the compiler's default ordinal
    /// equality, so one casing mismatch between the saved workspace and the plugin's own name
    /// produced two <c>ChartFeed</c>s for one chart, made <c>MarketFeeds.IsLive</c> answer for
    /// the wrong one, and defeated the focused-tab guard in
    /// <c>BackgroundTabFeedService.Reconcile</c> — producing exactly the double feed that
    /// guard's comment exists to prevent.
    /// </para>
    /// </summary>
    public readonly record struct ChartIdentity(string Market, string Provider, string Symbol, string Timeframe)
    {
        public static ChartIdentity Empty => new ChartIdentity("Spot", "Bitstamp", "", "1h");

        public bool Equals(ChartIdentity other) =>
            string.Equals(Market,    other.Market,    StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Provider,  other.Provider,  StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Symbol,    other.Symbol,    StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Timeframe, other.Timeframe, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode() => HashCode.Combine(
            Market    is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Market),
            Provider  is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Provider),
            Symbol    is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Symbol),
            Timeframe is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Timeframe));
    }
}
