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
        /// Every profile file on disk, including the machinery ones
        /// <see cref="GetAvailableProfiles"/> deliberately hides, paired with when it was last
        /// written. Used by session autosave to find the most recently active session slot.
        /// Returns an empty list on any I/O failure.
        /// </summary>
        IReadOnlyList<(string Name, DateTime LastWriteUtc)> GetAllProfilesWithTimes();

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
        private string _libraryDir;
        private readonly ILogger<WorkspaceLibraryService> _logger;

        private readonly Sdk.Strategies.IStrategyEngine? _engine;

        /// <summary>
        /// Workspaces live under <see cref="IPlatformPathService.AppDataDirectory"/>, which is the
        /// PER-USER directory when hosted accounts are enabled.
        ///
        /// <para>
        /// This class used to build its own path from
        /// <c>Environment.GetFolderPath(LocalApplicationData)</c>, ignoring the path service
        /// entirely. On the hosted server that had two consequences: every logged-in user shared
        /// ONE workspace library — including the <c>__last-session__</c> autosave, so whoever used
        /// the terminal last overwrote everyone else's, and any saved profile was visible to all —
        /// and the path could resolve relative (see <see cref="PlatformPaths"/>), landing inside
        /// the deployment directory. Settings, strategies and paper accounts were always routed
        /// correctly through the path service; workspaces were the one thing that wasn't.
        /// </para>
        ///
        /// <para>
        /// Desktop/local is unaffected: there <c>AppDataDirectory</c> is
        /// <c>~/.local/share/AccessibleTrader</c>, so the library stays exactly where it was.
        /// </para>
        /// </summary>
        public WorkspaceLibraryService(ILogger<WorkspaceLibraryService> logger,
            IPlatformPathService paths,
            Sdk.Strategies.IStrategyEngine? engine = null)
        {
            _logger = logger;
            _engine = engine;
            _libraryDir = Path.Combine(paths.AppDataDirectory, "Workspaces");
            Directory.CreateDirectory(_libraryDir);
        }

        /// <summary>Test seam: redirect the library to a temp directory. Also the
        /// hook a future per-user hosted scoping would use.</summary>
        internal string LibraryDirectoryOverride
        {
            get => _libraryDir;
            set { _libraryDir = value; Directory.CreateDirectory(value); }
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
                    .Where(x => x != null && x != "alerts"
                             // The session-autosave slots are machinery, not user
                             // profiles — hearing "__last-session__" in the list
                             // (or deleting one by accident) helps nobody. StartsWith,
                             // not equality: each session now writes its OWN slot so that
                             // two browser tabs cannot clobber each other, and every one of
                             // them carries this prefix.
                             && !x.StartsWith(Workspace.SessionAutosaveService.LastSessionProfileName,
                                              StringComparison.Ordinal))
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

        public IReadOnlyList<(string Name, DateTime LastWriteUtc)> GetAllProfilesWithTimes()
        {
            try
            {
                return Directory.GetFiles(_libraryDir, "*.json")
                    .Select(p => (Name: Path.GetFileNameWithoutExtension(p) ?? "",
                                  LastWriteUtc: File.GetLastWriteTimeUtc(p)))
                    .Where(x => x.Name.Length > 0 && x.Name != "alerts")
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list workspace profile timestamps.");
                return Array.Empty<(string, DateTime)>();
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

            // Library-backed active strategies (SpecId set) survive the save;
            // ad-hoc compiled scripts are session-only by design.
            if (_engine != null)
            {
                config.ActiveStrategies = _engine.ActiveStrategies
                    .Where(a => !string.IsNullOrEmpty(a.SpecId))
                    .Select(a => new SavedActiveStrategy
                    {
                        SpecId = a.SpecId!,
                        Symbol = a.Symbol,
                        ExecutionMode = a.ExecutionMode.ToString(),
                        IsPaused = a.IsPaused,
                    })
                    .ToList();
            }

            // Every tab is filed under ITS OWN TabIndex, from one source of truth.
            //
            // This used to build config.Tabs from `TabSnapshots.OrderBy(s => s.TabIndex)` and
            // then map indices by reading `state.TabSnapshots![i - 1]` — the RAW, unsorted
            // list. The snapshot list is not sorted after any SwitchTab (it appends the
            // outgoing tab to the end), so each tab config was filed under ANOTHER tab's
            // index: with raw snapshots [snap1, snap3, snap0] the config for snap0 landed at
            // index 1, snap1 at 3 and snap3 at 0. Symbols, indicator stacks and drawings were
            // written to disk against the wrong tab slots. A `sortedTabs` local was allocated
            // and never used — the leftover of an earlier attempt at this fix.
            //
            // Building the index→config map first and deriving config.Tabs from it means
            // there is no second ordering to disagree with.
            var indexedTabs = new SortedDictionary<int, TabConfiguration>
            {
                [state.ActiveTabIndex] = CreateTabConfig(state),
            };
            foreach (var snap in state.TabSnapshots ?? Enumerable.Empty<TabSnapshot>())
            {
                indexedTabs[snap.TabIndex] = CreateTabConfigFromSnapshot(snap);
            }

            config.Tabs = indexedTabs.Values.ToList();

            // ActiveTabIndex is saved as a POSITION in the saved list, which is what the
            // restore path indexes with. Because indexedTabs is sorted by TabIndex, that
            // position is the rank of ActiveTabIndex among the live indices.
            config.ActiveTabIndex = indexedTabs.Keys.ToList().IndexOf(state.ActiveTabIndex);

            SaveProfile(name, config);
        }

        /// <summary>Drawing anchors live on the runtime ChartSeries; sync them into
        /// the persisted config so trendlines/channels/fibs survive save/load
        /// (they did NOT before 2026-07-22 — every drawing vanished on restart).</summary>
        private static SeriesConfig CaptureConfig(ChartSeries s)
        {
            s.Config.Drawing = s.Drawing;
            return s.Config;
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
                Series = state.ActiveSeries.Select(CaptureConfig).ToList(),
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
                Series = snap.ActiveSeries.Select(CaptureConfig).ToList(),
                PaneHeightRatios = snap.PaneHeightRatios != null
                    ? new Dictionary<string, float>(snap.PaneHeightRatios)
                    : new()
            };
        }

        public WorkspaceConfiguration? LoadProfile(string name)
        {
            var safe = SanitizeProfileName(name);
            if (safe == null) return null;
            string path = Path.Combine(_libraryDir, $"{safe}.json");
            try
            {
                if (!File.Exists(path)) return null;

                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<WorkspaceConfiguration>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load workspace profile {Name}.", safe);
                // Genuine corruption (not a transient IO error): quarantine like every other
                // store so the next SaveProfile can't silently overwrite the recoverable
                // original, and AppStartupService announces it instead of the blind user
                // getting an unexplained blank workspace.
                if (ex is JsonException)
                    CorruptFileQuarantine.MoveAside(path, ex);
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
                string json = JsonConvert.SerializeObject(alerts, Formatting.Indented, AlertJsonSettings);
                AtomicFile.WriteAllText(path, json);
                _logger.LogInformation("Alerts saved.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save alerts.");
            }
        }

        // Part D: the advanced-condition tree is polymorphic via System.Text.Json
        // attributes; this bridge converter lets the Newtonsoft alerts.json path
        // round-trip it without duplicating the polymorphism rules.
        private static readonly JsonSerializerSettings AlertJsonSettings = new()
        {
            Converters = { new Alerts.ConditionNodeNewtonsoftBridge() },
        };

        public List<AlertDefinition> LoadAlerts()
        {
            string path = Path.Combine(_libraryDir, "alerts.json");
            try
            {
                if (!File.Exists(path)) return new List<AlertDefinition>();
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<List<AlertDefinition>>(json, AlertJsonSettings) ?? new List<AlertDefinition>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load alerts.");
                // Corrupt alerts.json quarantined (not silently swallowed) so the user is told
                // their alerts didn't load and a save can't clobber the original.
                if (ex is JsonException)
                    CorruptFileQuarantine.MoveAside(path, ex);
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
