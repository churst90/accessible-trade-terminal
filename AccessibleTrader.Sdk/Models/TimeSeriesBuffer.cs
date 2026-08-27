using System.Collections;

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

        /// <summary>
        /// A buffer whose last element is <paramref name="item"/>, leaving this one untouched.
        ///
        /// <para><b>This COPIES, and it has to.</b> It used to do
        /// <c>_data[Count - 1] = item;</c> — writing into the SHARED backing array and
        /// returning a new wrapper over it. Every reader holding any previously returned
        /// buffer saw that write, and <c>Ohlcv</c> is a 48-byte <c>readonly record struct</c>,
        /// so the write is not atomic: a reader on the render, sonification or paper-fill
        /// thread doing <c>state.Data[^1]</c> during a live <c>ReplaceLast</c> could read a bar
        /// with the NEW close and the OLD high.</para>
        ///
        /// <para>That mattered because <c>ChartFeed</c>'s locking scheme rests on a comment
        /// asserting this type is immutable, and readers deliberately do not take
        /// <c>_cacheLock</c> on the strength of it. The assertion is true now.</para>
        ///
        /// <para><c>Append</c> is deliberately NOT changed: its in-place write targets index
        /// <c>Count</c>, which is one past the end of every published buffer, so no reader can
        /// see it. Copying there would cost an allocation on the common path for no
        /// correctness gain.</para>
        ///
        /// <para>The cost here is one array copy per intra-bar tick. At the 5000-bar cache
        /// ceiling that is ~240 KB; a torn OHLC bar reaching the fill engine is worse.</para>
        /// </summary>
        public TimeSeriesBuffer<T> ReplaceLast(T item)
        {
            if (Count == 0) return Append(item);

            var copy = new T[_data.Length];
            Array.Copy(_data, copy, Count);
            copy[Count - 1] = item;
            return new TimeSeriesBuffer<T>(copy, Count);
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