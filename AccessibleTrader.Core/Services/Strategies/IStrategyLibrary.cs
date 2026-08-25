using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Persistent storage for user-built <see cref="StrategySpec"/>s. Backed by a single
    /// JSON file (<c>strategies.json</c>) in the platform app-data directory. Read-through:
    /// the in-memory list is the source of truth at runtime; <see cref="Save"/> writes the
    /// full list back to disk.
    /// </summary>
    public interface IStrategyLibrary
    {
        IReadOnlyList<StrategySpec> All { get; }

        /// <summary>Look up by ID. Returns null if not found.</summary>
        StrategySpec? GetById(string id);

        /// <summary>Insert a new spec or replace an existing one with the same ID.</summary>
        void Upsert(StrategySpec spec);

        /// <summary>Remove a spec by ID. No-op if not present.</summary>
        void Remove(string id);

        /// <summary>Persist the current in-memory list to disk.</summary>
        void Save();

        /// <summary>Reload from disk, discarding any unsaved in-memory changes.</summary>
        void Reload();
    }
}
