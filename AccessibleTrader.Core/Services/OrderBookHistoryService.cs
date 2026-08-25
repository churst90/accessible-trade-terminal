using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Core.Services
{
    public interface IOrderBookHistoryService
    {
        void AddSnapshot(string symbol, List<OrderBookEntry> bids, List<OrderBookEntry> asks);
        List<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetHistory(string symbol, IReadOnlyList<Ohlcv> bars);
        void Clear(string symbol);
    }

    /// <summary>
    /// Caches real-time L2 order book data to allow reconstruction of the Heatmap
    /// even if the chart is refreshed or scrolled.
    /// PHASE 4: Honest Heatmap Architecture.
    /// </summary>
    public class OrderBookHistoryService : IOrderBookHistoryService
    {
        // Simple memory-based cache: Symbol -> List of snapshots with timestamps
        private readonly Dictionary<string, List<OrderBookSnapshot>> _history = new();
        private readonly object _lock = new();

        public void AddSnapshot(string symbol, List<OrderBookEntry> bids, List<OrderBookEntry> asks)
        {
            lock (_lock)
            {
                if (!_history.ContainsKey(symbol)) _history[symbol] = new List<OrderBookSnapshot>();
                
                var snapshot = new OrderBookSnapshot(symbol, bids, asks, 0, DateTime.UtcNow);
                _history[symbol].Add(snapshot);

                // Memory management: keep last 5000 snapshots
                if (_history[symbol].Count > 5000) _history[symbol].RemoveAt(0);
            }
        }

        public List<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetHistory(string symbol, IReadOnlyList<Ohlcv> bars)
        {
            var result = new List<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)>();
            lock (_lock)
            {
                if (!_history.TryGetValue(symbol, out var snapshots) || !snapshots.Any())
                {
                    // No history: return empty lists for each bar
                    for (int i = 0; i < bars.Count; i++) result.Add((new(), new()));
                    return result;
                }

                foreach (var bar in bars)
                {
                    // Find the snapshot closest to the bar's close time but not after it
                    var snapshot = snapshots
                        .Where(s => s.Timestamp <= bar.Date.AddMinutes(1)) // Assuming 1m bars for now, logic can be more precise
                        .OrderByDescending(s => s.Timestamp)
                        .FirstOrDefault();

                    if (snapshot != null)
                    {
                        result.Add((snapshot.Bids, snapshot.Asks));
                    }
                    else
                    {
                        result.Add((new(), new()));
                    }
                }
            }
            return result;
        }

        public void Clear(string symbol)
        {
            lock (_lock)
            {
                if (_history.ContainsKey(symbol)) _history[symbol].Clear();
            }
        }
    }
}
