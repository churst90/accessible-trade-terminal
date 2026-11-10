namespace AccessibleTrader.Plugins.Kraken
{
    /// <summary>
    /// A few seconds of memory for Kraken's account reads.
    ///
    /// <para>
    /// The trading dashboard refreshes every two seconds and reads balances, positions, open
    /// orders and fills each time; the order service reads positions and open orders around
    /// every order as well. Kraken meters private reads at roughly one every three seconds on
    /// its lowest tier, so asking the venue each time builds a queue that never drains — and a
    /// close order used to wait in that queue. Callers asking within the time-to-live share
    /// one answer, and callers asking at the same moment share one request.
    /// </para>
    ///
    /// <para>
    /// A failed read is never remembered: the next caller asks the venue again, and the
    /// failure still reaches whoever was waiting on it (the order service classifies it).
    /// <see cref="Invalidate"/> forgets everything; it is called after every order, cancel and
    /// execution report, so a read after the user acts is always a fresh one.
    /// </para>
    /// </summary>
    internal sealed class KrakenReadCache
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, (DateTime At, Task<object> Load)> _entries = new(StringComparer.Ordinal);
        private readonly Func<DateTime> _clock;

        public KrakenReadCache(Func<DateTime>? clock = null) => _clock = clock ?? (() => DateTime.UtcNow);

        public async Task<T> GetAsync<T>(string key, TimeSpan ttl, Func<Task<T>> load) where T : class
        {
            Task<object> task;
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var e)
                    && _clock() - e.At < ttl
                    && !e.Load.IsFaulted && !e.Load.IsCanceled)
                {
                    task = e.Load;
                }
                else
                {
                    task = Box(load);
                    _entries[key] = (_clock(), task);
                }
            }

            try
            {
                return (T)await task.ConfigureAwait(false);
            }
            catch
            {
                lock (_gate)
                {
                    if (_entries.TryGetValue(key, out var e) && ReferenceEquals(e.Load, task))
                        _entries.Remove(key);
                }
                throw;
            }
        }

        public void Invalidate()
        {
            lock (_gate) _entries.Clear();
        }

        private static async Task<object> Box<T>(Func<Task<T>> load) where T : class =>
            await load().ConfigureAwait(false);
    }
}
