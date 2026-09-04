using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Earcon overrides: maps a feedback-type key (e.g. "Boundary", "Error") to a patch ID.
    /// Persisted alongside patch data in <c>patches.json</c>.
    /// </summary>
    public class EarconSettings
    {
        /// <summary>Key = FeedbackType name or earcon key; Value = SoundPatch.Id.</summary>
        public Dictionary<string, string> EarconPatchIds { get; set; } = new();
    }

    public interface ISoundPatchLibrary
    {
        IReadOnlyList<SoundPatch> GetPatches();
        void AddPatch(SoundPatch patch);
        void RemovePatch(string id);
        void UpdatePatch(SoundPatch patch);
        SoundPatch? GetPatch(string? id);
        void SavePatches();

        EarconSettings EarconOverrides { get; }
        void SaveEarconOverrides();

        string ExportPatchJson(SoundPatch patch);
        SoundPatch? ImportPatchJson(string json);

        /// <summary>
        /// Drops every user patch and every earcon override, then writes both files. In memory
        /// as well as on disk, for the reason <c>ISettingsManager.ResetToDefaults</c> records.
        /// </summary>
        void ResetToDefaults();
    }

    public class SoundPatchLibrary : ISoundPatchLibrary
    {
        private readonly string _patchesPath;
        private readonly string _earconPath;
        private readonly ILogger<SoundPatchLibrary> _logger;
        private readonly List<SoundPatch> _patches = new();

        public EarconSettings EarconOverrides { get; private set; } = new();

        public SoundPatchLibrary(IPlatformPathService pathService, ILogger<SoundPatchLibrary> logger)
        {
            _logger = logger;
            string dir = pathService.AppDataDirectory;
            _patchesPath = Path.Combine(dir, "patches.json");
            _earconPath  = Path.Combine(dir, "earcon-settings.json");
            LoadPatches();
        }

        public IReadOnlyList<SoundPatch> GetPatches() => _patches.AsReadOnly();

        public SoundPatch? GetPatch(string? id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            // User patches win (a user can clone-and-tweak a factory voice under the
            // same id); the factory voice bank ("voice_*", used by sound themes)
            // resolves through the same call so every patch consumer — components,
            // earcon overrides, previews — can use factory voices without special
            // cases.
            return _patches.FirstOrDefault(p => p.Id == id)
                ?? (Audio.SoundThemes.FactoryPatches.TryGetValue(id, out var factory) ? factory : null);
        }

        public void AddPatch(SoundPatch patch)
        {
            _patches.Add(patch);
            SavePatches();
        }

        public void RemovePatch(string id)
        {
            _patches.RemoveAll(p => p.Id == id);
            SavePatches();
        }

        public void UpdatePatch(SoundPatch patch)
        {
            int idx = _patches.FindIndex(p => p.Id == patch.Id);
            if (idx >= 0) _patches[idx] = patch;
            else _patches.Add(patch);
            SavePatches();
        }

        public void SavePatches()
        {
            try
            {
                AtomicFile.WriteAllText(_patchesPath, JsonConvert.SerializeObject(_patches, Formatting.Indented));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save patches");
            }
        }

        public void SaveEarconOverrides()
        {
            try
            {
                AtomicFile.WriteAllText(_earconPath, JsonConvert.SerializeObject(EarconOverrides, Formatting.Indented));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save earcon settings");
            }
        }

        private void LoadPatches()
        {
            try
            {
                if (File.Exists(_patchesPath))
                {
                    var loaded = JsonConvert.DeserializeObject<List<SoundPatch>>(File.ReadAllText(_patchesPath));
                    if (loaded != null) _patches.AddRange(loaded);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load patches; starting empty");
            }

            try
            {
                if (File.Exists(_earconPath))
                {
                    var settings = JsonConvert.DeserializeObject<EarconSettings>(File.ReadAllText(_earconPath));
                    if (settings != null) EarconOverrides = settings;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load earcon settings; using defaults");
            }
        }

        public void ResetToDefaults()
        {
            _patches.Clear();
            EarconOverrides = new EarconSettings();
            SavePatches();
            SaveEarconOverrides();
        }

        public string ExportPatchJson(SoundPatch patch) =>
            JsonConvert.SerializeObject(patch, Formatting.Indented);

        public SoundPatch? ImportPatchJson(string json)
        {
            try
            {
                var patch = JsonConvert.DeserializeObject<SoundPatch>(json);
                if (patch == null) return null;
                // Always assign a fresh ID on import to prevent collision
                patch.Id = Guid.NewGuid().ToString();
                return patch;
            }
            catch
            {
                return null;
            }
        }
    }
}
