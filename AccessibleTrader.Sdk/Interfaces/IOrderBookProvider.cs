using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Interfaces
{
    /// <summary>
    /// Specialized interface for plugins that provide order book (L2/L3) depth data.
    /// This is used to build heatmaps and enable advanced order flow analysis.
    /// </summary>
    public interface IOrderBookProvider
    {
        /// <summary>
        /// Fetches the current full or partial snapshot of the order book for a symbol.
        /// </summary>
        /// <param name="symbol">The ticker symbol (e.g., "BTC-USD").</param>
        /// <param name="depth">The number of price levels to return (default is 100).</param>
        Task<OrderBookSnapshot> GetOrderBookAsync(string symbol, int depth = 100);

        /// <summary>
        /// Subscribes to incremental order book updates from the provider.
        /// </summary>
        /// <param name="symbol">The ticker symbol to monitor.</param>
        /// <returns>An observable stream of depth updates.</returns>
        IObservable<OrderBookUpdate> SubscribeOrderBook(string symbol);
    }

    /// <summary>A point-in-time state of the order book depth.</summary>
    public record OrderBookSnapshot(
        string Symbol, 
        List<OrderBookEntry> Bids, 
        List<OrderBookEntry> Asks, 
        long Sequence, 
        DateTime Timestamp
    );

    /// <summary>An incremental update to the order book state.</summary>
    public record OrderBookUpdate(
        string Symbol, 
        List<OrderBookEntry> Bids, 
        List<OrderBookEntry> Asks, 
        long Sequence, 
        DateTime Timestamp
    );
}
