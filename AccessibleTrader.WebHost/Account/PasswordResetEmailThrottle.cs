using System.Collections.Concurrent;

namespace AccessibleTrader.WebHost.Account
{
    /// <summary>
    /// At most one password-reset email per address per five minutes.
    ///
    /// <para>
    /// In memory only, which is fine for the job: a restart forgets the window, and the
    /// window exists to stop one address being used to make the relay send on demand, not
    /// to enforce a permanent quota. Later requests inside the window are silently not
    /// sent — the page's neutral message is identical either way, so nothing leaks.
    /// </para>
    /// </summary>
    public sealed class PasswordResetEmailThrottle
    {
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

        private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSent =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly TimeProvider _time;

        public PasswordResetEmailThrottle(TimeProvider time) => _time = time;

        /// <summary>
        /// True when a send is allowed and records it; false when this address already had
        /// one inside <see cref="Window"/>.
        /// </summary>
        public bool TryBegin(string? email)
        {
            var key = (email ?? "").Trim();
            if (key.Length == 0) return false;

            var now = _time.GetUtcNow();
            bool allowed = false;
            _lastSent.AddOrUpdate(
                key,
                _ => { allowed = true; return now; },
                (_, previous) =>
                {
                    if (now - previous >= Window)
                    {
                        allowed = true;
                        return now;
                    }
                    return previous;
                });
            return allowed;
        }
    }
}
