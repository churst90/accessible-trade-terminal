using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            return _patches.FirstOrDefault(p => p.Id == id);
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
