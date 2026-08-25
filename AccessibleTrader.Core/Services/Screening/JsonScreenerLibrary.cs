using System.Text.Json;
using AccessibleTrader.Sdk.Screening;

namespace AccessibleTrader.Core.Services.Screening
{
    /// <summary>Persisted store of saved screens.</summary>
    public interface IScreenerLibrary
    {
        IReadOnlyList<ScreenerSpec> All { get; }
        ScreenerSpec? GetById(string id);
        void Upsert(ScreenerSpec spec);
        void Remove(string id);
        void Save();
        void Reload();
    }

    /// <summary>
    /// JSON-on-disk <see cref="IScreenerLibrary"/> (<c>screeners.json</c>). Polymorphic round-trip
    /// of the condition tree relies on the <c>JsonPolymorphic</c> attributes on
    /// <c>ConditionNode</c> — the same mechanism <c>JsonStrategyLibrary</c> depends on.
    /// </summary>
    public class JsonScreenerLibrary : IScreenerLibrary
    {
        private readonly string _filepath;
        private List<ScreenerSpec> _specs = new();

        private static readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        public IReadOnlyList<ScreenerSpec> All => _specs;

        public JsonScreenerLibrary(IPlatformPathService pathService)
        {
            _filepath = Path.Combine(pathService.AppDataDirectory, "screeners.json");
            Reload();
        }

        public ScreenerSpec? GetById(string id) => _specs.FirstOrDefault(s => s.Id == id);

        public void Upsert(ScreenerSpec spec)
        {
            int idx = _specs.FindIndex(s => s.Id == spec.Id);
            if (idx >= 0) _specs[idx] = spec;
            else _specs.Add(spec);
            Save();
        }

        public void Remove(string id)
        {
            if (_specs.RemoveAll(s => s.Id == id) > 0) Save();
        }

        public void Save()
        {
            try
            {
                AtomicFile.WriteAllText(_filepath, JsonSerializer.Serialize(_specs, _options));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save screeners: {ex.Message}");
            }
        }

        public void Reload()
        {
            try
            {
                if (!File.Exists(_filepath))
                {
                    _specs = new List<ScreenerSpec>();
                    return;
                }

                string json = File.ReadAllText(_filepath);
                _specs = string.IsNullOrWhiteSpace(json)
                    ? new List<ScreenerSpec>()
                    : JsonSerializer.Deserialize<List<ScreenerSpec>>(json, _options) ?? new List<ScreenerSpec>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load screeners: {ex.Message}");
                CorruptFileQuarantine.MoveAside(_filepath, ex);
                _specs = new List<ScreenerSpec>();
            }
        }
    }
}
