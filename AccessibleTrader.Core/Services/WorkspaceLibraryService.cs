using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public interface IWorkspaceLibraryService
    {
        List<string> GetAvailableProfiles();
        void SaveProfile(string name, WorkspaceConfiguration config);
        WorkspaceConfiguration? LoadProfile(string name);
        void DeleteProfile(string name);
        /// <summary>Persists the current alert definitions to disk.</summary>
        void SaveAlerts(IEnumerable<AlertDefinition> alerts);
        /// <summary>Loads saved alert definitions from disk. Returns an empty list if none saved.</summary>
        List<AlertDefinition> LoadAlerts();
        string ExportVisualProfile(WorkspaceState state);
        string ExportAudioProfile(WorkspaceState state);
        void ImportVisualProfile(string json, IWorkspaceStore store);
        void ImportAudioProfile(string json, IWorkspaceStore store, ISeriesManagementService seriesService);
    }

    public class WorkspaceLibraryService : IWorkspaceLibraryService
    {
        private readonly string _libraryDir;
        private readonly ILogger<WorkspaceLibraryService> _logger;

        public WorkspaceLibraryService(ILogger<WorkspaceLibraryService> logger)
        {
            _logger = logger;
            _libraryDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccessibleTrader", "Workspaces");
            if (!Directory.Exists(_libraryDir)) Directory.CreateDirectory(_libraryDir);
        }

        public List<string> GetAvailableProfiles()
        {
            try
            {
                return Directory.GetFiles(_libraryDir, "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(x => x != null)
                    .Cast<string>()
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to list workspace profiles: {ex.Message}");
                return new List<string>();
            }
        }

        public void SaveProfile(string name, WorkspaceConfiguration config)
        {
            try
            {
                string path = Path.Combine(_libraryDir, $"{name}.json");
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(path, json);
                _logger.LogInformation($"Workspace profile '{name}' saved.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to save workspace profile '{name}': {ex.Message}");
            }
        }

        public WorkspaceConfiguration? LoadProfile(string name)
        {
            try
            {
                string path = Path.Combine(_libraryDir, $"{name}.json");
                if (!File.Exists(path)) return null;

                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<WorkspaceConfiguration>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load workspace profile '{name}': {ex.Message}");
                return null;
            }
        }

        public void DeleteProfile(string name)
        {
            try
            {
                string path = Path.Combine(_libraryDir, $"{name}.json");
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to delete workspace profile '{name}': {ex.Message}");
            }
        }

        public void SaveAlerts(IEnumerable<AlertDefinition> alerts)
        {
            try
            {
                string path = Path.Combine(_libraryDir, "alerts.json");
                string json = JsonConvert.SerializeObject(alerts, Formatting.Indented);
                File.WriteAllText(path, json);
                _logger.LogInformation("Alerts saved.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to save alerts: {ex.Message}");
            }
        }

        public List<AlertDefinition> LoadAlerts()
        {
            try
            {
                string path = Path.Combine(_libraryDir, "alerts.json");
                if (!File.Exists(path)) return new List<AlertDefinition>();
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<List<AlertDefinition>>(json) ?? new List<AlertDefinition>();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load alerts: {ex.Message}");
                return new List<AlertDefinition>();
            }
        }

        public string ExportVisualProfile(WorkspaceState state)
        {
            var profile = new VisualProfile
            {
                ThemeType = "HighContrastDark",
                BackgroundColor = state.BackgroundColor ?? "#000000"
            };
            foreach (var series in state.ActiveSeries)
            {
                foreach (var comp in series.Components)
                {
                    string key = $"{series.FriendlyName}.{comp.Name}";
                    profile.ComponentColors[key] = new ComponentAppearance
                    {
                        ColorHex = comp.ColorHex,
                        ColorHexSecondary = comp.ColorHexSecondary,
                        Thickness = comp.Thickness,
                        DashStyle = comp.DashStyle.ToString()
                    };
                }
            }
            return System.Text.Json.JsonSerializer.Serialize(profile,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        public string ExportAudioProfile(WorkspaceState state)
        {
            var profile = new AudioProfile
            {
                SonificationEnabled = state.IsSonificationEnabled,
                MasterVolume = 0.7f,
                WasapiLatency = state.WasapiLatency
            };
            foreach (var series in state.ActiveSeries)
            {
                foreach (var comp in series.Components)
                {
                    string key = $"{series.FriendlyName}.{comp.Name}";
                    profile.ComponentAudio[key] = new ComponentAudioOverride
                    {
                        Waveform = comp.Waveform,
                        FreqMultiplier = comp.FreqMultiplier,
                        Volume = comp.Volume,
                        IsMuted = comp.IsMuted
                    };
                }
            }
            return System.Text.Json.JsonSerializer.Serialize(profile,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        public void ImportVisualProfile(string json, IWorkspaceStore store)
        {
            var profile = System.Text.Json.JsonSerializer.Deserialize<VisualProfile>(json);
            if (profile == null) return;

            store.Dispatch(new UpdateSettingsAction(s => s with { BackgroundColor = profile.BackgroundColor }));

            foreach (var series in store.State.ActiveSeries)
            {
                foreach (var comp in series.Components)
                {
                    string key = $"{series.FriendlyName}.{comp.Name}";
                    if (profile.ComponentColors.TryGetValue(key, out var appearance))
                    {
                        comp.ColorHex = appearance.ColorHex;
                        comp.ColorHexSecondary = appearance.ColorHexSecondary;
                        comp.Thickness = appearance.Thickness;
                    }
                }
            }
        }

        public void ImportAudioProfile(string json, IWorkspaceStore store, ISeriesManagementService seriesService)
        {
            var profile = System.Text.Json.JsonSerializer.Deserialize<AudioProfile>(json);
            if (profile == null) return;

            store.Dispatch(new UpdateSettingsAction(s => s with
            {
                IsSonificationEnabled = profile.SonificationEnabled,
                WasapiLatency = profile.WasapiLatency
            }));

            foreach (var series in store.State.ActiveSeries)
            {
                foreach (var comp in series.Components)
                {
                    string key = $"{series.FriendlyName}.{comp.Name}";
                    if (profile.ComponentAudio.TryGetValue(key, out var audio))
                    {
                        comp.Waveform = audio.Waveform;
                        comp.FreqMultiplier = audio.FreqMultiplier;
                        comp.Volume = audio.Volume;
                        comp.IsMuted = audio.IsMuted;
                    }
                }
            }
            seriesService.PersistWorkspace();
        }
    }
}
