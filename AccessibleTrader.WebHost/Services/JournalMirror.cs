using System.Collections.Concurrent;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// A process-wide, per-owner copy of what each circuit's journal recorded, so
    /// <c>/diag/journal</c> can answer with something.
    ///
    /// <para>
    /// <b>The endpoint could only ever return <c>[]</c>.</b> <c>IJournalService</c> is
    /// registered <c>AddScoped</c> and <c>JournalService</c> keeps its ring buffer in an
    /// instance field with no static backing store. A minimal-API endpoint resolves from
    /// the <i>request</i> scope, never the circuit scope, so every call constructed a
    /// brand-new empty journal, read it, and returned an empty array — in every mode.
    /// The comment above the endpoint warned that "an anonymous dump of it on a hosted
    /// instance would leak positions, balances and alerts", which was not true of the
    /// code as written, and the existing guard
    /// (<c>Diag_journal_requires_a_signed_in_user_when_it_is_mapped</c>) asserts the auth
    /// redirect only, so it stayed green throughout. The one diagnostic you would reach
    /// for when the hosted speech pipeline goes quiet was itself silent.
    /// </para>
    ///
    /// <para>
    /// <b>Why a mirror and not a singleton journal.</b> Making <c>JournalService</c> a
    /// singleton would pool every hosted user's spoken transcript — positions, balances,
    /// alerts — into one buffer that any signed-in user could then read, which is exactly
    /// the leak that comment was worried about. The mirror keeps one ring per owner key
    /// (<c>ICurrentUser.DataKey</c>, i.e. the Identity user id) and the endpoint can only
    /// ask for the caller's own. On the single-user heads there is one owner,
    /// <see cref="LocalOwner"/>.
    /// </para>
    ///
    /// <para>
    /// It subscribes to <c>IJournalService.EntryAdded</c> rather than living inside
    /// <c>JournalService</c>, so Core is untouched and the MAUI head — which has no such
    /// endpoint — carries none of this.
    /// </para>
    /// </summary>
    public sealed class JournalMirror
    {
        /// <summary>Owner key used when no hosted-accounts identity exists (Full / demo).</summary>
        public const string LocalOwner = "local";

        /// <summary>
        /// Entries retained per owner. Smaller than <c>JournalService</c>'s own 2,000: this
        /// is a diagnostic tail, and on the hosted head it is multiplied by the number of
        /// users who have ever opened a circuit in this process.
        /// </summary>
        public const int PerOwnerCapacity = 500;

        /// <summary>
        /// Ceiling on distinct owners held at once. A process serving many users must not
        /// accumulate a ring per user forever; past the cap the least recently written
        /// owner is dropped, which is the one least likely to be under investigation.
        /// </summary>
        public const int MaxOwners = 64;

        private sealed class OwnerLog
        {
            public readonly LinkedList<JournalEntry> Entries = new();
            public long LastWriteTicks;
        }

        private readonly ConcurrentDictionary<string, OwnerLog> _byOwner = new(StringComparer.Ordinal);
        private readonly object _gate = new();
        private long _sequence;

        public void Record(string owner, JournalEntry entry)
        {
            if (string.IsNullOrEmpty(owner)) owner = LocalOwner;

            lock (_gate)
            {
                var log = _byOwner.GetOrAdd(owner, _ => new OwnerLog());
                log.Entries.AddLast(entry);
                while (log.Entries.Count > PerOwnerCapacity) log.Entries.RemoveFirst();
                // A monotonic counter, not a clock: recency ordering is all this needs and
                // it must not depend on the wall clock moving forward.
                log.LastWriteTicks = ++_sequence;

                while (_byOwner.Count > MaxOwners)
                {
                    var coldest = _byOwner.OrderBy(kv => kv.Value.LastWriteTicks).First().Key;
                    _byOwner.TryRemove(coldest, out _);
                }
            }
        }

        /// <summary>Oldest-first snapshot for one owner; empty when that owner has recorded nothing.</summary>
        public IReadOnlyList<JournalEntry> Snapshot(string owner)
        {
            if (string.IsNullOrEmpty(owner)) owner = LocalOwner;
            lock (_gate)
            {
                return _byOwner.TryGetValue(owner, out var log)
                    ? log.Entries.ToList()
                    : Array.Empty<JournalEntry>();
            }
        }

        /// <summary>Number of owners currently held. Diagnostics and tests only.</summary>
        public int OwnerCount => _byOwner.Count;
    }
}
