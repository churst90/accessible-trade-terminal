using System.Collections.Generic;
using AccessibleTrader.Sdk.Screening;

namespace AccessibleTrader.Core.Services.Screening
{
    /// <summary>
    /// Persisted store of the user's watchlists. Mirrors <c>IStrategyLibrary</c>'s shape so the
    /// two read the same way at call sites, including the "never load-blocking" contract: a
    /// missing, empty or corrupt file yields an empty library rather than an exception.
    /// </summary>
    public interface IWatchlistLibrary
    {
        IReadOnlyList<Watchlist> All { get; }

        Watchlist? GetById(string id);

        /// <summary>Inserts or replaces by <see cref="Watchlist.Id"/>, then saves.</summary>
        void Upsert(Watchlist list);

        void Remove(string id);

        /// <summary>
        /// Appends an entry to a list, ignoring duplicates by <see cref="WatchlistEntry.Key"/>.
        /// Returns true when the entry was actually added.
        /// </summary>
        bool AddEntry(string watchlistId, WatchlistEntry entry);

        /// <summary>Removes an entry by its <see cref="WatchlistEntry.Key"/>. Returns true when removed.</summary>
        bool RemoveEntry(string watchlistId, string entryKey);

        /// <summary>
        /// Moves an entry by <paramref name="delta"/> positions (negative = towards the front),
        /// clamped to the list bounds. Returns the entry's new index, or -1 when not found.
        /// </summary>
        int MoveEntry(string watchlistId, string entryKey, int delta);

        void Save();
        void Reload();
    }
}
