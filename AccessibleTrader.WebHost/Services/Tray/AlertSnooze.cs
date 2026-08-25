namespace AccessibleTrader.WebHost.Services.Tray
{
    /// <summary>
    /// A simple "silence alerts for a while" flag, shared between the tray (which sets it)
    /// and the background monitor (which checks it). Singleton. Thread-safe via volatile
    /// read/write of a single timestamp field.
    /// </summary>
    public sealed class AlertSnooze
    {
        private long _untilUtcTicks; // 0 = not snoozed

        /// <summary>Clock indirection so tests can advance time; defaults to the system clock.</summary>
        public Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;

        public void SilenceFor(TimeSpan duration)
        {
            var until = UtcNow().Add(duration);
            System.Threading.Interlocked.Exchange(ref _untilUtcTicks, until.Ticks);
        }

        public void Resume() => System.Threading.Interlocked.Exchange(ref _untilUtcTicks, 0);

        public bool IsActive
        {
            get
            {
                long ticks = System.Threading.Interlocked.Read(ref _untilUtcTicks);
                return ticks != 0 && UtcNow().Ticks < ticks;
            }
        }

        public int RemainingMinutes
        {
            get
            {
                long ticks = System.Threading.Interlocked.Read(ref _untilUtcTicks);
                if (ticks == 0) return 0;
                var remaining = new DateTime(ticks, DateTimeKind.Utc) - UtcNow();
                return remaining > TimeSpan.Zero ? (int)Math.Ceiling(remaining.TotalMinutes) : 0;
            }
        }
    }
}
