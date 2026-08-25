using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// One paper account per user, for the whole process.
    ///
    /// <para>
    /// <b>The bug this exists to fix.</b> <c>IPaperTradingProvider</c> was registered
    /// <c>AddScoped</c> on the WebHost, and a Blazor scope is a BROWSER TAB. So two tabs belonging
    /// to one user each built their own account object, each loaded <c>paper_account.json</c> at
    /// the moment it was created, and each wrote the whole file back on every change — with no
    /// re-read and no file watch. Whichever tab persisted last won, and a trade made in one tab
    /// could be erased by a trailing-stop update in another. Nothing failed; the position was
    /// simply gone, which is the worst way for money state to break.
    /// </para>
    ///
    /// <para>
    /// The desktop head registers the provider as a singleton and was never affected. This hub
    /// gives the hosted build the same guarantee — one account object per user — while still
    /// letting every tab attach its own chart, so a resting order fills from whichever chart is
    /// live rather than only from the tab that placed it.
    /// </para>
    ///
    /// <para>
    /// Keyed on <c>ICurrentUser.DataKey</c>: the immutable user id, or "anon". Two different users
    /// never share, which is the other half of what "one account per user" has to mean.
    /// </para>
    /// </summary>
    public sealed class PaperAccountHub : IDisposable
    {
        // Lazy, not the account itself: ConcurrentDictionary.GetOrAdd does NOT serialise
        // its factory. Under concurrent first use — two tabs opening at once, which is
        // the exact scenario this class exists for — both factories ran and the loser
        // was discarded WITHOUT DisposeAccount(). It had already attached to a store and
        // subscribed to MonitoredBarEvent in its constructor, so it went on processing
        // bars and writing the whole of paper_account.json on every change: the very
        // last-writer-wins corruption described above, reproduced by the fix for it.
        //
        // Several Lazy wrappers may still be constructed; only the one that wins the
        // dictionary is ever asked for its Value, and constructing a Lazy costs nothing.
        private readonly ConcurrentDictionary<string, Lazy<PaperTradingProvider>> _accounts =
            new(StringComparer.Ordinal);

        /// <summary>
        /// This user's account, creating it once on first use. <paramref name="create"/> is only
        /// invoked for a key that has none, and only once even under concurrent first use.
        /// </summary>
        public PaperTradingProvider ForUser(string userKey, Func<PaperTradingProvider> create)
        {
            if (string.IsNullOrEmpty(userKey)) userKey = "anon";
            return _accounts.GetOrAdd(userKey, _ => new Lazy<PaperTradingProvider>(() =>
            {
                var account = create();
                account.SharedOwnership = true;   // scope disposal must not tear it down
                return account;
            }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        /// <summary>Accounts currently held, for diagnostics.</summary>
        public IReadOnlyCollection<string> ActiveUsers => _accounts.Keys.ToList();

        public void Dispose()
        {
            foreach (var kv in _accounts)
            {
                // A Lazy nobody has forced holds no account to tear down, and forcing it
                // here would construct one only to dispose it.
                if (!kv.Value.IsValueCreated) continue;
                kv.Value.Value.SharedOwnership = false;
                kv.Value.Value.DisposeAccount();
            }
            _accounts.Clear();
        }
    }
}
