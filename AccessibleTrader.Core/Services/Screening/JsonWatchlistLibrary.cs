using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AccessibleTrader.Sdk.Screening;

namespace AccessibleTrader.Core.Services.Screening
{
    /// <summary>
    /// JSON-on-disk <see cref="IWatchlistLibrary"/>, stored as <c>watchlists.json</c> in the
    /// app-data directory. Follows <c>JsonStrategyLibrary</c>'s conventions exactly: atomic
    /// writes, best-effort saves that never crash the app, and quarantine-on-corrupt so a bad
    /// file is moved aside instead of being silently overwritten by the next save.
    /// </summary>
    public class JsonWatchlistLibrary : IWatchlistLibrary
    {
        private readonly string _filepath;
        private List<Watchlist> _lists = new();

        private static readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        public IReadOnlyList<Watchlist> All => _lists;

        public JsonWatchlistLibrary(IPlatformPathService pathService)
        {
            _filepath = Path.Combine(pathService.AppDataDirectory, "watchlists.json");
            Reload();
        }

        public Watchlist? GetById(string id) =>
            _lists.FirstOrDefault(w => w.Id == id);

        public void Upsert(Watchlist list)
        {
            int idx = _lists.FindIndex(w => w.Id == list.Id);
            if (idx >= 0) _lists[idx] = list;
            else _lists.Add(list);
            Save();
        }

        public void Remove(string id)
        {
            if (_lists.RemoveAll(w => w.Id == id) > 0) Save();
        }

        public bool AddEntry(string watchlistId, WatchlistEntry entry)
        {
            var list = GetById(watchlistId);
            if (list == null) return false;
            if (list.Entries.Any(e => e.Key == entry.Key)) return false;

            var entries = list.Entries.ToList();
            entries.Add(entry);
            Upsert(list with { Entries = entries });
            return true;
        }

        public bool RemoveEntry(string watchlistId, string entryKey)
        {
            var list = GetById(watchlistId);
            if (list == null) return false;

            var entries = list.Entries.ToList();
            if (entries.RemoveAll(e => e.Key == entryKey) == 0) return false;
            Upsert(list with { Entries = entries });
            return true;
        }

        public int MoveEntry(string watchlistId, string entryKey, int delta)
        {
            var list = GetById(watchlistId);
            if (list == null) return -1;

            var entries = list.Entries.ToList();
            int idx = entries.FindIndex(e => e.Key == entryKey);
            if (idx < 0) return -1;

            int target = Math.Clamp(idx + delta, 0, entries.Count - 1);
            if (target == idx) return idx;

            var item = entries[idx];
            entries.RemoveAt(idx);
            entries.Insert(target, item);
            Upsert(list with { Entries = entries });
            return target;
        }

        public void Save()
        {
            try
            {
                AtomicFile.WriteAllText(_filepath, JsonSerializer.Serialize(_lists, _options));
            }
            catch (Exception ex)
            {
                // Watchlists are user data but never load-blocking: the in-memory list stays
                // authoritative and the next mutation retries the write.
                System.Diagnostics.Debug.WriteLine($"Failed to save watchlists: {ex.Message}");
            }
        }

        public void Reload()
        {
            try
            {
                if (!File.Exists(_filepath))
                {
                    _lists = new List<Watchlist>();
                    return;
                }

                string json = File.ReadAllText(_filepath);
                _lists = string.IsNullOrWhiteSpace(json)
                    ? new List<Watchlist>()
                    : JsonSerializer.Deserialize<List<Watchlist>>(json, _options) ?? new List<Watchlist>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load watchlists: {ex.Message}");
                // Same data-loss guard as the strategy library: without the quarantine the next
                // Save() would overwrite the user's watchlists with an empty list.
                CorruptFileQuarantine.MoveAside(_filepath, ex);
                _lists = new List<Watchlist>();
            }
        }
    }
}
