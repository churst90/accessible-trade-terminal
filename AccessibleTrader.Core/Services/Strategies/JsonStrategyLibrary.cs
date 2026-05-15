using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// JSON-on-disk implementation of <see cref="IStrategyLibrary"/>. Stores all specs in a
    /// single <c>strategies.json</c> file under the app-data directory. Polymorphic round-trip
    /// for the condition tree relies on the <c>JsonPolymorphic</c> attributes on
    /// <see cref="ConditionNode"/>. Survives a missing/empty/corrupt file by starting with
    /// an empty list rather than throwing — strategies are user data, never load-blocking.
    /// </summary>
    public class JsonStrategyLibrary : IStrategyLibrary
    {
        private readonly string _filepath;
        private List<StrategySpec> _specs = new();

        private static readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        public IReadOnlyList<StrategySpec> All => _specs;

        public JsonStrategyLibrary(IPlatformPathService pathService)
        {
            _filepath = Path.Combine(pathService.AppDataDirectory, "strategies.json");
            Reload();
        }

        public StrategySpec? GetById(string id) =>
            _specs.FirstOrDefault(s => s.Id == id);

        public void Upsert(StrategySpec spec)
        {
            int idx = _specs.FindIndex(s => s.Id == spec.Id);
            if (idx >= 0) _specs[idx] = spec;
            else _specs.Add(spec);
            Save();
        }

        public void Remove(string id)
        {
            int removed = _specs.RemoveAll(s => s.Id == id);
            if (removed > 0) Save();
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(_specs, _options);
                AtomicFile.WriteAllText(_filepath, json);
            }
            catch (Exception ex)
            {
                // Strategy persistence is best-effort. A disk failure must never crash the app —
                // the in-memory list remains the working source of truth and the next save retries.
                System.Diagnostics.Debug.WriteLine($"Failed to save strategies: {ex.Message}");
            }
        }

        public void Reload()
        {
            try
            {
                if (!File.Exists(_filepath))
                {
                    _specs = new List<StrategySpec>();
                    return;
                }
                string json = File.ReadAllText(_filepath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _specs = new List<StrategySpec>();
                    return;
                }
                var loaded = JsonSerializer.Deserialize<List<StrategySpec>>(json, _options);
                _specs = loaded ?? new List<StrategySpec>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load strategies: {ex.Message}");
                _specs = new List<StrategySpec>();
            }

            // Insert any built-in starter specs that aren't already in the library. Idempotent —
            // existing specs (including ones the user has edited) are preserved untouched.
            BuiltInStrategySeeds.EnsureSeeded(this);
        }
    }
}
