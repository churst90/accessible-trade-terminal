namespace AccessibleTrader.Sdk.Plugins
{
    /// <summary>
    /// Declares the natural rendering shape of a provider's data so the chart engine can
    /// seed the right default series instead of always assuming OHLCV candlesticks.
    ///
    /// Why this exists: every provider currently returns its data as <c>Ohlcv</c> records
    /// (because that's the universal data shape the rest of the pipeline understands), but
    /// analytics providers like FRED, BinanceDerivatives, AlternativeMe, and Glassnode
    /// produce single-value time series — they fill <c>Ohlcv</c> with O=H=L=C=value and
    /// Volume=0. Rendering those as candles produces a chart of degenerate dojis (a
    /// horizontal stub at every bar) and a flat zero volume pane underneath, which is
    /// useless visually. Providers declare their natural shape via this enum and the
    /// chart loader honors it when seeding the default series for a fresh load.
    /// </summary>
    public enum ProviderDataShape
    {
        /// <summary>
        /// Standard OHLCV market data: distinct open/high/low/close per bar plus volume.
        /// Rendered as candlesticks on the price pane plus a volume pane below. The
        /// historical default for every market-data provider (Binance, Alpaca, etc.).
        /// </summary>
        Ohlcv,

        /// <summary>
        /// Single-value time series: one numeric value per timestamp, no volume. The
        /// chart engine renders these as a Line on the price pane, with no candle pane
        /// and no volume pane. Used by every analytics provider — economic series, on-
        /// chain metrics, sentiment indices, funding rates, open interest, market breadth.
        /// </summary>
        SingleValueLine
    }
}
