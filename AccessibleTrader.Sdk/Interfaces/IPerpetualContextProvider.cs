namespace AccessibleTrader.Sdk.Interfaces
{
    /// <summary>
    /// A perpetual future's state right now, as the venue publishes it.
    /// </summary>
    /// <param name="Symbol">The symbol as the chart names it.</param>
    /// <param name="FundingRate">The rate for one funding period, as a fraction (0.0001 is 0.01%).
    /// Positive: longs pay shorts.</param>
    /// <param name="FundingIntervalHours">How many hours one funding period lasts (Hyperliquid 1).</param>
    /// <param name="OpenInterest">Open contracts, in the coin (or share) the market trades.</param>
    /// <param name="MarkPrice">The price positions are valued and liquidated at.</param>
    /// <param name="IndexPrice">The outside price the market is tied to (Hyperliquid's oracle); 0 if none.</param>
    /// <param name="DayVolumeQuote">The last 24 hours' traded value in the quote currency; 0 if not given.</param>
    public sealed record PerpetualContext(
        string Symbol, double FundingRate, double FundingIntervalHours, double OpenInterest,
        double MarkPrice, double IndexPrice, double DayVolumeQuote);

    /// <summary>
    /// Optional capability for venues that publish a perpetual's current funding and open
    /// interest. It is what the spoken "funding now" readout reads; the Funding Rate and Open
    /// Interest indicators chart history through other paths.
    /// </summary>
    public interface IPerpetualContextProvider
    {
        /// <summary>The current state of one perpetual; null for a spot pair or an unknown symbol.</summary>
        Task<PerpetualContext?> GetPerpetualContextAsync(string symbol, CancellationToken ct = default);
    }
}
