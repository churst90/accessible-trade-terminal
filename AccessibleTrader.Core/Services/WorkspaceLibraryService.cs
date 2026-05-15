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

        /// <summary>
        /// Captures the full current workspace state (all tabs) into a named profile on disk.
        /// </summary>
        void SaveWorkspaceProfile(string name, IWorkspaceStore store);

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

        /// <summary>
        /// Reject profile names that could escape the library directory. Applied to every
        /// save/load/delete path so a malicious workspace name like "../../something" or
        /// "C:\\Windows\\..." cannot touch files outside _libraryDir.
        /// </summary>
        private static string? SanitizeProfileName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var trimmed = name.Trim();
            if (trimmed.Contains("..", StringComparison.Ordinal)) return null;
            if (Path.IsPathRooted(trimmed)) return null;
            if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            // Strip any directory component; only the filename portion can name a profile.
            var baseName = Path.GetFileName(trimmed);
            if (string.IsNullOrWhiteSpace(baseName)) return null;
            if (baseName.Equals("alerts", StringComparison.OrdinalIgnoreCase)) return null;
            return baseName;
        }

        public List<string> GetAvailableProfiles()
        {
            try
            {
                return Directory.GetFiles(_libraryDir, "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(x => x != null && x != "alerts")
                    .Cast<string>()
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list workspace profiles.");
                return new List<string>();
            }
        }

        public void SaveProfile(string name, WorkspaceConfiguration config)
        {
            var safe = SanitizeProfileName(name);
            if (safe == null)
            {
                _logger.LogWarning("Refused to save workspace profile with unsafe name: {Name}", name);
                return;
            }
            try
            {
                string path = Path.Combine(_libraryDir, $"{safe}.json");
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                AtomicFile.WriteAllText(path, json);
                _logger.LogInformation("Workspace profile {Name} saved.", safe);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save workspace profile {Name}.", safe);
            }
        }

        public void SaveWorkspaceProfile(string name, IWorkspaceStore store)
        {
            var state = store.State;

            var config = new WorkspaceConfiguration
            {
                Mode = state.Mode,
                SelectedMarketType = state.SelectedMarketType,
                ActiveTabIndex = state.ActiveTabIndex,
                Tabs = new List<TabConfiguration>()
            };

            // Capture the active tab.
            config.Tabs.Add(CreateTabConfig(state));

            // Capture all inactive tab snapshots.
            if (state.TabSnapshots != null)
            {
                foreach (var snap in state.TabSnapshots.OrderBy(s => s.TabIndex))
                {
                    config.Tabs.Add(CreateTabConfigFromSnapshot(snap));
                }
            }

            // Sort tabs by their original index so the saved order matches the tab bar.
            // The active tab's index is state.ActiveTabIndex; snapshots carry their own TabIndex.
            // We inserted active first then snapshots, so re-sort by index.
            var sortedTabs = new List<TabConfiguration>(config.Tabs.Count);
            // Build a mapping: active tab at ActiveTabIndex, snapshots at their indices.
            var indexedTabs = new SortedDictionary<int, TabConfiguration>();
            indexedTabs[state.ActiveTabIndex] = config.Tabs[0]; // active tab
            for (int i = 1; i < config.Tabs.Count; i++)
            {
                var snap = state.TabSnapshots![i - 1];
                indexedTabs[snap.TabIndex] = config.Tabs[i];
            }
            config.Tabs = indexedTabs.Values.ToList();

            // Remap ActiveTabIndex to the position in the sorted list.
            config.ActiveTabIndex = config.Tabs.IndexOf(indexedTabs[state.ActiveTabIndex]);

            SaveProfile(name, config);
        }

        private static TabConfiguration CreateTabConfig(WorkspaceState state)
        {
            return new TabConfiguration
            {
                Market = state.Identity.Market,
                Provider = state.Identity.Provider,
                Symbol = state.Identity.Symbol,
                Timeframe = state.Identity.Timeframe,
                ViewportStartIndex = state.ViewportStartIndex,
                ViewportLength = state.ViewportLength,
                IsHeikinAshi = state.IsHeikinAshi,
                IsLogScale = state.IsLogScale,
                Series = state.ActiveSeries.Select(s => s.Config).ToList(),
                PaneHeightRatios = state.PaneHeightRatios != null
                    ? new Dictionary<string, float>(state.PaneHeightRatios)
                    : new()
            };
        }

        private static TabConfiguration CreateTabConfigFromSnapshot(TabSnapshot snap)
        {
            return new TabConfiguration
            {
                Market = snap.Identity.Market,
                Provider = snap.Identity.Provider,
                Symbol = snap.Identity.Symbol,
                Timeframe = snap.Identity.Timeframe,
                ViewportStartIndex = snap.ViewportStartIndex,
                ViewportLength = snap.ViewportLength,
                IsHeikinAshi = snap.IsHeikinAshi,
                IsLogScale = snap.IsLogScale,
                Series = snap.ActiveSeries.Select(s => s.Config).ToList(),
                PaneHeightRatios = snap.PaneHeightRatios != null
                    ? new Dictionary<string, float>(snap.PaneHeightRatios)
                    : new()
            };
        }

        public WorkspaceConfiguration? LoadProfile(string name)
        {
            var safe = SanitizeProfileName(name);
            if (safe == null) return null;
            try
            {
                string path = Path.Combine(_libraryDir, $"{safe}.json");
                if (!File.Exists(path)) return null;

                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<WorkspaceConfiguration>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load workspace profile {Name}.", safe);
                return null;
            }
        }

        public void DeleteProfile(string name)
        {
            var safe = SanitizeProfileName(name);
            if (safe == null) return;
            try
            {
                string path = Path.Combine(_libraryDir, $"{safe}.json");
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete workspace profile {Name}.", safe);
            }
        }

        public void SaveAlerts(IEnumerable<AlertDefinition> alerts)
        {
            try
            {
                string path = Path.Combine(_libraryDir, "alerts.json");
                string json = JsonConvert.SerializeObject(alerts, Formatting.Indented);
                AtomicFile.WriteAllText(path, json);
                _logger.LogInformation("Alerts saved.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save alerts.");
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
                _logger.LogError(ex, "Failed to load alerts.");
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
        }
    }
}
