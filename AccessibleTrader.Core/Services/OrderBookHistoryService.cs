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
            => AddSnapshot(symbol, bids, asks, DateTime.UtcNow);

        /// <summary>The same, stamped with the time given: the tests' clock.</summary>
        internal void AddSnapshot(string symbol, List<OrderBookEntry> bids, List<OrderBookEntry> asks, DateTime at)
        {
            lock (_lock)
            {
                if (!_history.ContainsKey(symbol)) _history[symbol] = new List<OrderBookSnapshot>();
                
                var snapshot = new OrderBookSnapshot(symbol, bids, asks, 0, at);
                _history[symbol].Add(snapshot);

                // Memory management: keep last 5000 snapshots
                if (_history[symbol].Count > 5000) _history[symbol].RemoveAt(0);
            }
        }

        /// <summary>
        /// For each bar, the last book seen while that bar was open: taken at or after the bar's
        /// own time and before the next bar's (the last bar, still forming, has no end). A bar
        /// with no book of its own gets empty lists rather than an older bar's book.
        ///
        /// <para>
        /// This used to take the newest snapshot at or before <c>bar.Date + 1 minute</c>, "assuming
        /// 1m bars for now". A bar's date is when it OPENS, so on an hour chart a book taken at
        /// 14:37 never matched the 14:00 bar's "14:01", and the heatmap was empty on every
        /// timeframe but one minute. The bar's end is now the next bar's time, whatever the
        /// timeframe. Snapshots arrive in time order, so each bar is a binary search rather than
        /// a sort of every snapshot per bar.
        /// </para>
        /// </summary>
        public List<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetHistory(string symbol, IReadOnlyList<Ohlcv> bars)
        {
            var result = new List<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)>(bars.Count);
            lock (_lock)
            {
                if (!_history.TryGetValue(symbol, out var snapshots) || snapshots.Count == 0)
                {
                    // No history: return empty lists for each bar
                    for (int i = 0; i < bars.Count; i++) result.Add((new(), new()));
                    return result;
                }

                for (int i = 0; i < bars.Count; i++)
                {
                    DateTime start = bars[i].Date;
                    DateTime? end = i + 1 < bars.Count ? bars[i + 1].Date : null;

                    // The last snapshot before the bar's end, then whether it is inside the bar.
                    int last = end is DateTime e ? LastBefore(snapshots, e) : snapshots.Count - 1;
                    if (last >= 0 && snapshots[last].Timestamp >= start)
                        result.Add((snapshots[last].Bids, snapshots[last].Asks));
                    else
                        result.Add((new(), new()));
                }
            }
            return result;
        }

        /// <summary>The index of the last snapshot stamped strictly before <paramref name="end"/>, or -1.</summary>
        private static int LastBefore(List<OrderBookSnapshot> snapshots, DateTime end)
        {
            int lo = 0, hi = snapshots.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (snapshots[mid].Timestamp < end) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return found;
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
