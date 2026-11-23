using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Sdk.Interfaces
{
    /// <summary>
    /// One trade on a venue's public tape: when, at what price, how much, and which side took
    /// liquidity. <see cref="TakerSide"/> Buy means a buyer lifted the ask (buying pressure);
    /// Sell means a seller hit the bid. Quantity is in the same unit the venue's candles count
    /// volume in, so a bar's buy and sell add up to its volume.
    /// </summary>
    public readonly record struct PublicTrade(DateTime Time, double Price, double Quantity, OrderSide TakerSide);

    /// <summary>
    /// Optional capability for plugins whose venue streams its public trades with the aggressor
    /// side (most crypto exchanges do; stock and forex feeds do not). It is what true buy/sell
    /// volume is counted from: the candles carry only total volume.
    /// </summary>
    public interface ITradeStreamProvider
    {
        /// <summary>
        /// Opens the public trade stream for one symbol.
        /// </summary>
        /// <param name="market">The chart's market (a futures market name contains "Futures").</param>
        /// <param name="symbol">The symbol as the chart names it.</param>
        /// <param name="onTrade">Called for every trade, on the socket's thread.</param>
        /// <param name="onCounting">Called with true each time the stream is (re)subscribed, from
        /// which point every trade will arrive, and with false when it drops. A bar that a drop
        /// fell inside is missing trades, and the caller does not count it.</param>
        /// <returns>Dispose to close the stream.</returns>
        Task<IAsyncDisposable> SubscribeTradesAsync(string market, string symbol,
            Action<PublicTrade> onTrade, Action<bool> onCounting);
    }
}
