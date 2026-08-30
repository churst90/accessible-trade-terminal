using System.Collections.Concurrent;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// <b>The dead-feed rule, in one place.</b> A watch whose fetch keeps failing has stopped
    /// being watched, and the user's only signal is an alert that never arrives — so after
    /// <see cref="FailuresBeforeReporting"/> consecutive failures they are told, exactly once,
    /// and the count resets the moment the feed answers again.
    ///
    /// <para>
    /// ── Why this is a type rather than two private dictionaries ─────────────────
    /// It was two. <see cref="LocalBackgroundMonitor"/> and <see cref="HostedAlertMonitor"/> each
    /// grew their own counter, their own reported-set and their own <c>if (n &lt; 3) return;</c>,
    /// added at different times for the same reason. That is the shape
    /// <c>LevelPolarity</c> was created to collapse after the same one-line invariant was got
    /// wrong twice in three weeks, and the 2026-08-29 mutation campaign made the cost concrete
    /// from the other direction: N08/N09 survived because one guard had been copied to four
    /// sites and a test killed only the copy it knew about. A duplicated rule is a rule that can
    /// be half-fixed.
    /// </para>
    ///
    /// <para>
    /// What deliberately stays at the call sites is DELIVERY, not policy: the local monitor
    /// speaks through Orca and raises a desktop toast, the hosted one sends a Web Push, and each
    /// writes its own message. The escalation, the once-only latch and the reset are here.
    /// </para>
    ///
    /// <para>
    /// Thread safety: concurrent, because the hosted monitor polls users in parallel and two
    /// watches for one user can be evaluated on different threads. The local monitor's loop is
    /// sequential and pays nothing measurable for it.
    /// </para>
    /// </summary>
    /// <typeparam name="TKey">
    /// What a "feed" is scoped to. The local monitor is single-user, so the key is the symbol.
    /// The hosted monitor keys on (user, symbol) — two users can watch one symbol through
    /// different credentials, so one user's key expiring is not the other's feed going down.
    /// </typeparam>
    public sealed class DeadFeedTracker<TKey> where TKey : notnull
    {
        /// <summary>
        /// How many polls in a row must fail before the user is told. Above one, because a
        /// single transient failure is normal and announcing it would be noise; low enough that
        /// a genuinely dead feed is reported within a few minutes rather than never.
        /// </summary>
        public const int FailuresBeforeReporting = 3;

        private readonly ConcurrentDictionary<TKey, int> _consecutiveFailures;

        /// <summary>Keys already reported dead, so the warning is said once and not once a
        /// minute for the rest of the session — which trains a user to ignore it.</summary>
        private readonly ConcurrentDictionary<TKey, byte> _reported;

        public DeadFeedTracker(IEqualityComparer<TKey>? comparer = null)
        {
            _consecutiveFailures = comparer is null ? new() : new(comparer);
            _reported = comparer is null ? new() : new(comparer);
        }

        /// <summary>Records one failed poll for <paramref name="key"/>.</summary>
        /// <returns>
        /// The consecutive-failure count when this failure is the one worth reporting — the
        /// caller says its piece and passes the number on to the user — or <c>null</c> when
        /// nothing should be said, either because the feed has not failed enough times yet or
        /// because it has already been reported.
        /// </returns>
        public int? NoteFailure(TKey key)
        {
            int n = _consecutiveFailures.AddOrUpdate(key, 1, (_, prev) => prev + 1);

            if (n < FailuresBeforeReporting) return null;
            if (!_reported.TryAdd(key, 0)) return null;
            return n;
        }

        /// <summary>Records a successful poll for <paramref name="key"/>, clearing the count.</summary>
        /// <returns>
        /// <c>true</c> when this feed had been reported dead, so the recovery is news: a user who
        /// heard "alerts on this symbol are not being watched" has no other way to learn that
        /// they are live again, and would keep watching manually. <c>false</c> on the ordinary
        /// poll where nothing was ever wrong.
        /// </returns>
        public bool NoteRecovery(TKey key)
        {
            _consecutiveFailures.TryRemove(key, out _);
            return _reported.TryRemove(key, out _);
        }

        /// <summary>Consecutive failures currently recorded for <paramref name="key"/>; zero
        /// when the feed is healthy. For diagnostics and tests — no policy reads it.</summary>
        public int FailureCount(TKey key) =>
            _consecutiveFailures.TryGetValue(key, out int n) ? n : 0;
    }
}
