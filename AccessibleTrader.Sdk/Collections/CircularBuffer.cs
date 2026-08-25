using System.Collections;

namespace AccessibleTrader.Sdk.Collections
{
    /// <summary>
    /// High-performance, fixed-capacity circular buffer.
    /// Provides O(1) complexity for both addition and eviction by using a rotating head pointer.
    /// This prevents the O(N) memory shifting associated with standard lists when 
    /// handling high-frequency market data streams.
    /// </summary>
    /// <typeparam name="T">The type of elements in the buffer (usually Ohlcv).</typeparam>
    public class CircularBuffer<T> : IEnumerable<T>
    {
        private readonly T[] _buffer;
        private int _head;
        private int _tail;
        private int _size;
        private readonly int _capacity;

        public int Count => _size;
        public int Capacity => _capacity;

        public CircularBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentException("Capacity must be greater than zero.", nameof(capacity));
            _capacity = capacity;
            _buffer = new T[capacity];
            _size = 0;
            _head = 0;
            _tail = 0;
        }

        /// <summary>
        /// Adds an item to the end of the buffer. 
        /// If at capacity, the oldest item (at the head) is automatically evicted.
        /// </summary>
        public void Add(T item)
        {
            _buffer[_tail] = item;
            _tail = (_tail + 1) % _capacity;

            if (_size < _capacity)
            {
                _size++;
            }
            else
            {
                // Buffer is full, the head moves forward to evict the oldest item
                _head = (_head + 1) % _capacity;
            }
        }

        public void AddRange(IEnumerable<T> items)
        {
            foreach (var item in items) Add(item);
        }

        /// <summary>
        /// Prepends an item to the start of the buffer.
        /// Moves the head pointer backwards.
        /// </summary>
        public void Prepend(T item)
        {
            _head = (_head - 1 + _capacity) % _capacity;
            _buffer[_head] = item;

            if (_size < _capacity)
            {
                _size++;
            }
            else
            {
                // Buffer is full, the tail moves backward to evict the newest item (rare use case)
                _tail = (_tail - 1 + _capacity) % _capacity;
            }
        }

        public void PrependRange(IEnumerable<T> items)
        {
            // To maintain chronological order when prepending historical ranges, 
            // we iterate backwards through the incoming range.
            var list = new List<T>(items);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Prepend(list[i]);
            }
        }

        /// <summary>
        /// Accesses the buffer using a 'Logical Index' (0 to Count-1).
        /// Index 0 is always the oldest data point currently in the buffer.
        /// Index Count-1 is always the newest.
        /// </summary>
        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= _size) throw new IndexOutOfRangeException();
                // Map logical index to physical array position
                return _buffer[(_head + index) % _capacity];
            }
            set
            {
                if (index < 0 || index >= _size) throw new IndexOutOfRangeException();
                // Note: callers typically hold their own lock, but the assignment to the
                // underlying array slot is atomic on its own.
                _buffer[(_head + index) % _capacity] = value;
            }
        }

        /// <summary>
        /// Converts a Physical Index (raw array position) to a Logical Index (relative to head).
        /// Required for external services that track raw positions to stay synced after a wrap.
        /// </summary>
        public int GetLogicalIndex(int physicalIndex)
        {
            int relative = physicalIndex - _head;
            if (relative < 0) relative += _capacity;
            return relative;
        }

        public void Clear()
        {
            _head = 0;
            _tail = 0;
            _size = 0;
            Array.Clear(_buffer, 0, _buffer.Length);
        }

        public List<T> ToList()
        {
            var list = new List<T>(_size);
            for (int i = 0; i < _size; i++)
            {
                list.Add(this[i]);
            }
            return list;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _size; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
