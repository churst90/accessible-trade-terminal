namespace AccessibleTrader.Sdk.Enums
{
    public enum MarketType
    {
        Crypto,
        Stock,
        Forex,
        Commodity,
        Economic,
        OnChain,
        Options,
        Futures,
        Bonds,
        Index,
        /// <summary>
        /// Derivatives metadata: funding rates, open interest, basis, perpetual mark price.
        /// Lives under the Analytics terminal mode (alongside Economic / OnChain) — these
        /// are non-tradeable analytical series, not orderable instruments. Symbols are
        /// suffixed to indicate the metric (e.g. BTCUSDT_FUNDING, BTCUSDT_OI).
        /// </summary>
        Derivatives,
        /// <summary>
        /// Sentiment indices: Fear &amp; Greed, social volume, news polarity. Daily
        /// resolution. Lives under Analytics. Single-value series rendered as line/area.
        /// </summary>
        Sentiment
    }
}