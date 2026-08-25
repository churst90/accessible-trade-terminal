namespace AccessibleTrader.WebHost.Services
{
    public enum RecentAlertState { Unread, Read, Dismissed }

    /// <summary>One entry in the tray's recent-alerts list.</summary>
    public sealed class RecentAlertItem
    {
        public required string Id { get; init; }
        public DateTime FiredAtUtc { get; init; }
        public string? Symbol { get; init; }
        public required string Text { get; init; }
        public RecentAlertState State { get; set; } = RecentAlertState.Unread;
    }

    /// <summary>
    /// A small, thread-safe ring buffer of recently fired alerts, shared between the
    /// background monitor (which appends every browser-closed alert) and the Linux tray
    /// (which reads the unread count for its label and lists them on the recent-alerts
    /// page). Per-item state moves Unread → Read (kept for reference) → Dismissed (hidden).
    /// Singleton — one buffer for the local process.
    /// </summary>
    public sealed class RecentAlertsBuffer
    {
        private const int MaxItems = 100;
        private readonly object _lock = new();
        private readonly LinkedList<RecentAlertItem> _items = new(); // newest first

        /// <summary>Raised after any mutation so the tray can refresh its unread-count label.</summary>
        public event Action? Changed;

        public void Add(string text, string? symbol)
        {
            lock (_lock)
            {
                _items.AddFirst(new RecentAlertItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    FiredAtUtc = DateTime.UtcNow,
                    Symbol = string.IsNullOrWhiteSpace(symbol) ? null : symbol,
                    Text = text,
                });
                while (_items.Count > MaxItems) _items.RemoveLast();
            }
            Changed?.Invoke();
        }

        public int UnreadCount
        {
            get { lock (_lock) return _items.Count(i => i.State == RecentAlertState.Unread); }
        }

        /// <summary>Newest-first, excluding dismissed entries.</summary>
        public IReadOnlyList<RecentAlertItem> Snapshot()
        {
            lock (_lock) return _items.Where(i => i.State != RecentAlertState.Dismissed).ToList();
        }

        public void MarkRead(string id) => Mutate(id, i => i.State = RecentAlertState.Read);
        public void Dismiss(string id) => Mutate(id, i => i.State = RecentAlertState.Dismissed);

        public void MarkAllRead()
        {
            lock (_lock)
                foreach (var i in _items)
                    if (i.State == RecentAlertState.Unread) i.State = RecentAlertState.Read;
            Changed?.Invoke();
        }

        private void Mutate(string id, Action<RecentAlertItem> apply)
        {
            bool changed = false;
            lock (_lock)
            {
                var it = _items.FirstOrDefault(i => i.Id == id);
                if (it != null) { apply(it); changed = true; }
            }
            if (changed) Changed?.Invoke();
        }
    }
}
