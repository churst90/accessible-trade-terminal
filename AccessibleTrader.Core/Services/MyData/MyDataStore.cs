using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.MyData
{
    /// <summary>Manifest entry for one imported dataset.</summary>
    public sealed record MyDataset(
        string Id, string Name, MyDataShape Shape,
        IReadOnlyList<string> Columns, int RowCount,
        DateTime FirstDate, DateTime LastDate, string Timeframe);

    public interface IMyDataStore
    {
        IReadOnlyList<MyDataset> Datasets { get; }
        /// <summary>Parses, validates, persists. Throws FormatException with a
        /// human-readable message on bad input — the import dialog speaks it.</summary>
        Task<(MyDataset Dataset, IReadOnlyList<string> Warnings)> ImportAsync(string name, string csvContent);
        Task DeleteAsync(string id);
        /// <summary>Full parsed data for a dataset (cached after first read). Null if unknown.</summary>
        ParsedDataset? GetParsed(string id);
        /// <summary>Raised after import/delete so symbol lists and indicator
        /// registries can refresh.</summary>
        event Action? Changed;
    }

    /// <summary>
    /// Owns the my-data directory under the app-data root: one CSV per dataset
    /// plus manifest.json. Per-user on the hosted terminal automatically because
    /// IPlatformPathService is user-scoped there. Quotas keep a shared host safe:
    /// 5 MB per file, 200k rows (parser-enforced), 50 datasets.
    /// </summary>
    public sealed class MyDataStore : IMyDataStore
    {
        public const int MaxContentBytes = 5 * 1024 * 1024;
        public const int MaxDatasets = 50;

        private readonly string _dir;
        private readonly ILogger<MyDataStore> _logger;
        private readonly object _lock = new();
        private List<MyDataset> _datasets = new();
        private readonly Dictionary<string, ParsedDataset> _parseCache = new();

        public event Action? Changed;

        public MyDataStore(IPlatformPathService paths, ILogger<MyDataStore> logger)
        {
            _logger = logger;
            _dir = Path.Combine(paths.AppDataDirectory, "my-data");
            Directory.CreateDirectory(_dir);
            LoadManifest();
        }

        public IReadOnlyList<MyDataset> Datasets { get { lock (_lock) return _datasets.ToList(); } }

        public Task<(MyDataset, IReadOnlyList<string>)> ImportAsync(string name, string csvContent)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) throw new FormatException("Give the dataset a name first.");
            if (name.Contains('|') || name.Contains('—'))
                throw new FormatException("Dataset names can't contain '|' or '—' (they separate columns in symbol names).");
            if (csvContent.Length > MaxContentBytes)
                throw new FormatException($"File too large — the limit is {MaxContentBytes / (1024 * 1024)} MB.");

            lock (_lock)
            {
                if (_datasets.Count >= MaxDatasets)
                    throw new FormatException($"Dataset limit reached ({MaxDatasets}). Delete one first.");
                if (_datasets.Any(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    throw new FormatException($"A dataset named \"{name}\" already exists. Pick another name or delete it first.");
            }

            var parsed = CsvDataParser.Parse(csvContent);
            if (parsed.RowCount == 0) throw new FormatException("No data rows found.");

            string id = Guid.NewGuid().ToString("N")[..12];
            var ds = new MyDataset(id, name, parsed.Shape, parsed.Columns,
                parsed.RowCount, parsed.FirstDate, parsed.LastDate, parsed.InferredTimeframe);

            File.WriteAllText(Path.Combine(_dir, id + ".csv"), csvContent);
            lock (_lock)
            {
                _datasets.Add(ds);
                _parseCache[id] = parsed;
                SaveManifest();
            }
            Changed?.Invoke();
            return Task.FromResult<(MyDataset, IReadOnlyList<string>)>((ds, parsed.Warnings));
        }

        public Task DeleteAsync(string id)
        {
            lock (_lock)
            {
                _datasets.RemoveAll(d => d.Id == id);
                _parseCache.Remove(id);
                SaveManifest();
            }
            try { File.Delete(Path.Combine(_dir, id + ".csv")); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not delete dataset file {Id}.", id); }
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        public ParsedDataset? GetParsed(string id)
        {
            lock (_lock)
            {
                if (_parseCache.TryGetValue(id, out var cached)) return cached;
                if (_datasets.All(d => d.Id != id)) return null;
            }
            try
            {
                var parsed = CsvDataParser.Parse(File.ReadAllText(Path.Combine(_dir, id + ".csv")));
                lock (_lock) _parseCache[id] = parsed;
                return parsed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stored dataset {Id} failed to re-parse.", id);
                return null;
            }
        }

        private string ManifestPath => Path.Combine(_dir, "manifest.json");

        private void LoadManifest()
        {
            try
            {
                if (!File.Exists(ManifestPath)) return;
                _datasets = JsonSerializer.Deserialize<List<MyDataset>>(File.ReadAllText(ManifestPath)) ?? new();
            }
            catch (Exception ex)
            {
                // Corrupt manifest: quarantine rather than crash — same policy as
                // the settings store. The CSVs stay on disk for manual recovery.
                _logger.LogError(ex, "my-data manifest unreadable — starting empty (files kept).");
                try { File.Move(ManifestPath, ManifestPath + ".corrupt", overwrite: true); } catch { }
                _datasets = new();
            }
        }

        private void SaveManifest() =>
            File.WriteAllText(ManifestPath, JsonSerializer.Serialize(_datasets,
                new JsonSerializerOptions { WriteIndented = true }));
    }
}
