using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Collections;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    public interface IDataCacheService
    {
        List<Ohlcv> Data { get; }
        int Count { get; }
        void Add(Ohlcv item);
        void AddRange(IEnumerable<Ohlcv> items);
        void PrependRange(IEnumerable<Ohlcv> items);
        void Clear();
        bool TryUpdate(Ohlcv item);
        event Action<int>? ItemsEvicted;
    }

    /// <summary>
    /// Thread-safe service for managing the local OHLCV data cache.
    /// Uses a high-performance CircularBuffer and FastLookup dictionary.
    /// </summary>
    public class DataCacheService : IDataCacheService
    {
        private readonly CircularBuffer<Ohlcv> _buffer;
        private readonly Dictionary<DateTime, int> _lookup = new();
        private readonly object _lock = new();
        private const int MaxCapacity = 5000;

        public event Action<int>? ItemsEvicted;

        public DataCacheService()
        {
            _buffer = new CircularBuffer<Ohlcv>(MaxCapacity);
        }

        public List<Ohlcv> Data 
        {
            get
            {
                lock (_lock)
                {
                    return _buffer.ToList();
                }
            }
        }

        public int Count
        {
            get
            {
                lock (_lock) return _buffer.Count;
            }
        }

        public void Add(Ohlcv item)
        {
            lock (_lock)
            {
                if (_lookup.ContainsKey(item.Date))
                {
                    TryUpdate(item);
                    return;
                }

                int evictCount = (_buffer.Count >= MaxCapacity) ? 1 : 0;

                // If evicting, remove the oldest entry from the lookup before adding
                if (evictCount > 0 && _buffer.Count > 0)
                    _lookup.Remove(_buffer[0].Date);

                _buffer.Add(item);
                _lookup[item.Date] = _buffer.Count - 1;

                if (evictCount > 0) ItemsEvicted?.Invoke(evictCount);
            }
        }

        public void AddRange(IEnumerable<Ohlcv> items)
        {
            lock (_lock)
            {
                var itemsList = items as IReadOnlyList<Ohlcv> ?? items.ToList();
                int listCount = itemsList.Count;
                int currentCount = _buffer.Count;
                int evictCount = Math.Max(0, (currentCount + listCount) - MaxCapacity);
                if (evictCount > currentCount) evictCount = currentCount;

                foreach (var item in itemsList) _buffer.Add(item);
                RebuildLookup();

                if (evictCount > 0) ItemsEvicted?.Invoke(evictCount);
            }
        }

        public void PrependRange(IEnumerable<Ohlcv> items)
        {
            lock (_lock)
            {
                _buffer.PrependRange(items);
                RebuildLookup();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _buffer.Clear();
                _lookup.Clear();
            }
        }

        public bool TryUpdate(Ohlcv item)
        {
            lock (_lock)
            {
                if (_lookup.TryGetValue(item.Date, out int logicalIndex))
                {
                    var existing = _buffer[logicalIndex];
                    // --- BUG FIX: VOLUME DOUBLE-COUNTING ---
                    // LiveStreamManager already calls UpdateWith(tick) to accumulate deltas 
                    // into a bar snapshot. If we call UpdateWith here, we add that snapshot 
                    // to the previous snapshot, effectively doubling (or more) the volume.
                    // We simply replace with the latest version of the bar.
                    if (existing != item)
                    {
                        _buffer[logicalIndex] = item;
                        return true;
                    }
                }
                return false;
            }
        }

        public bool TryGetIndex(DateTime date, out int logicalIndex)
        {
            lock (_lock)
            {
                return _lookup.TryGetValue(date, out logicalIndex);
            }
        }

        private void RebuildLookup()
        {
            _lookup.Clear();
            for (int i = 0; i < _buffer.Count; i++)
            {
                _lookup[_buffer[i].Date] = i;
            }
        }
    }
}
