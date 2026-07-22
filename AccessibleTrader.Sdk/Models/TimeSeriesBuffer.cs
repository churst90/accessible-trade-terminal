using System;
using System.Collections;
using System.Collections.Generic;

namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// A high-performance, pseudo-immutable buffer for time series data.
    /// Provides O(1) indexed access and minimizes allocations during appends/replaces.
    /// Thread-safe for multiple readers since length (Count) is fixed per instance.
    /// </summary>
    public class TimeSeriesBuffer<T> : IReadOnlyList<T>
    {
        private readonly T[] _data;
        public int Count { get; }

        private TimeSeriesBuffer(T[] data, int count)
        {
            _data = data;
            Count = count;
        }

        public TimeSeriesBuffer()
        {
            _data = new T[1024];
            Count = 0;
        }

        public TimeSeriesBuffer(params T[] items) : this((IEnumerable<T>)items)
        {
        }

        public TimeSeriesBuffer(IEnumerable<T> items)
        {
            if (items is IReadOnlyList<T> list)
            {
                Count = list.Count;
                _data = new T[Math.Max(1024, Count * 2)];
                for (int i = 0; i < Count; i++) _data[i] = list[i];
            }
            else
            {
                var l = new List<T>(items);
                Count = l.Count;
                _data = new T[Math.Max(1024, Count * 2)];
                l.CopyTo(_data);
            }
        }

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
                return _data[index];
            }
        }

        public ReadOnlySpan<T> AsSpan() => new ReadOnlySpan<T>(_data, 0, Count);
        public ReadOnlyMemory<T> AsMemory() => new ReadOnlyMemory<T>(_data, 0, Count);

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return _data[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public static TimeSeriesBuffer<T> Empty { get; } = new TimeSeriesBuffer<T>(Array.Empty<T>(), 0);

        public TimeSeriesBuffer<T> Append(T item)
        {
            if (Count < _data.Length)
            {
                _data[Count] = item;
                return new TimeSeriesBuffer<T>(_data, Count + 1);
            }
            else
            {
                // Math.Max guards the Empty singleton: its backing array is
                // zero-length, and 0 * 2 == 0 made the first Append overflow.
                var newArray = new T[Math.Max(1024, _data.Length * 2)];
                Array.Copy(_data, newArray, _data.Length);
                newArray[Count] = item;
                return new TimeSeriesBuffer<T>(newArray, Count + 1);
            }
        }

        public TimeSeriesBuffer<T> ReplaceLast(T item)
        {
            if (Count == 0) return Append(item);
            _data[Count - 1] = item;
            // Return a new instance to ensure reference inequality for state change detection
            return new TimeSeriesBuffer<T>(_data, Count);
        }

        public TimeSeriesBuffer<T> PrependRange(IReadOnlyList<T> items)
        {
            int extra = items.Count;
            if (extra == 0) return this;
            
            var newArray = new T[Math.Max(_data.Length, (Count + extra) * 2)];
            for (int i = 0; i < extra; i++) newArray[i] = items[i];
            if (Count > 0) Array.Copy(_data, 0, newArray, extra, Count);
            
            return new TimeSeriesBuffer<T>(newArray, Count + extra);
        }

        public TimeSeriesBuffer<T> RemoveFirst()
        {
            if (Count == 0) return this;
            var newArray = new T[_data.Length];
            Array.Copy(_data, 1, newArray, 0, Count - 1);
            return new TimeSeriesBuffer<T>(newArray, Count - 1);
        }

        public TimeSeriesBuffer<T> RemoveLast()
        {
            if (Count == 0) return this;
            var newArray = new T[_data.Length];
            Array.Copy(_data, 0, newArray, 0, Count - 1);
            return new TimeSeriesBuffer<T>(newArray, Count - 1);
        }
    }
}