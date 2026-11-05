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

        /// <summary>
        /// The configuration <see cref="SaveWorkspaceProfile"/> would write for the store's
        /// current state, without writing it. Session autosave compares two of these to tell a
        /// change the user made from a tick that changed nothing a resume would give back.
        /// </summary>
        WorkspaceConfiguration CaptureWorkspace(IWorkspaceStore store);

        /// <summary>Persists the current alert definitions to disk.</summary>
        void SaveAlerts(IEnumerable<AlertDefinition> alerts);
        /// <summary>Loads saved alert definitions from disk. Returns an empty list if none saved.</summary>
        List<AlertDefinition> LoadAlerts();
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

        /// <summary>
        /// The workspace with only the tab in front, for Save's "This tab only" (Cody,
        /// 2026-11-05: "does this save the currently focused chart, or all workspaces (tabs) at
        /// once?" — it saved all of them, with no way to save one). The strategies kept are the
        /// ones that run on that chart, plus any saved with no chart (started on a blank chart, or
        /// saved before strategies kept their chart), which belong to no tab in particular.
        /// </summary>
        public static WorkspaceConfiguration OnlyActiveTab(WorkspaceConfiguration full)
        {
            if (!full.IsMultiTab) return full;
            var tab = full.Tabs[Math.Clamp(full.ActiveTabIndex, 0, full.Tabs.Count - 1)];
            var chart = new ChartIdentity(tab.Market, tab.Provider, tab.Symbol, tab.Timeframe);
            return new WorkspaceConfiguration
            {
                Mode = full.Mode,
                SelectedMarketType = full.SelectedMarketType,
                ActiveTabIndex = 0,
                Tabs = new List<TabConfiguration> { tab },
                ActiveStrategies = (full.ActiveStrategies ?? new()).Where(s =>
                    string.IsNullOrEmpty(s.ChartProvider)
                    || Strategies.StrategyBinding.SameChart(
                        new ChartIdentity(s.ChartMarket ?? "", s.ChartProvider, s.ChartSymbol ?? "", s.ChartTimeframe ?? ""),
                        chart)).ToList(),
            };
        }

        /// <summary>A chart as the workspace dialogs say it: "Kraken BTCUSDT 1h".</summary>
        public static string ChartName(string? provider, string? symbol, string? timeframe) =>
            string.IsNullOrWhiteSpace(symbol)
                ? "an empty chart"
                : string.Join(" ", new[] { provider, symbol, timeframe }.Where(p => !string.IsNullOrWhiteSpace(p)));

        /// <summary>
        /// What a saved workspace holds, for the Load list: "3 tabs: Kraken BTCUSDT 1h, Bitstamp
        /// BTCUSDT 1d, Yahoo DIA 1d, 1 strategy". The first four charts are named; more are counted.
        /// </summary>
        public static string Describe(WorkspaceConfiguration? config)
        {
            if (config == null) return "could not be read";
            var charts = config.IsMultiTab
                ? config.Tabs.Select(t => ChartName(t.Provider, t.Symbol, t.Timeframe)).ToList()
                : new List<string> { ChartName(config.Provider, config.Symbol, config.Timeframe) };
            string tabs = charts.Count == 1 ? "1 tab" : $"{charts.Count} tabs";
            string named = string.Join(", ", charts.Take(4)) + (charts.Count > 4 ? $", and {charts.Count - 4} more" : "");
            int n = config.ActiveStrategies?.Count ?? 0;
            return $"{tabs}: {named}" + (n == 0 ? "" : n == 1 ? ", 1 strategy" : $", {n} strategies");
        }

        public void SaveWorkspaceProfile(string name, IWorkspaceStore store) =>
            SaveProfile(name, CaptureWorkspace(store));

        public WorkspaceConfiguration CaptureWorkspace(IWorkspaceStore store)
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
                        ChartProvider = a.Chart?.Provider,
                        ChartSymbol = a.Chart?.Symbol,
                        ChartTimeframe = a.Chart?.Timeframe,
                        ChartMarket = a.Chart?.Market,
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

            return config;
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
                // Normalised on the way out as well as on the way in: a tab restored from a
                // snapshot never passes through WorkspaceInitializer.RestoreTabInto, and a
                // grown key written back to disk survives the fix that stopped it growing.
                Market = MarketKey.Normalize(state.Identity.Market),
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
                    : new(),
                EarliestBarDate = EarliestBarDate(state.Data, state.RestoredLoad, state.Identity)
            };
        }

        private static TabConfiguration CreateTabConfigFromSnapshot(TabSnapshot snap)
        {
            return new TabConfiguration
            {
                Market = MarketKey.Normalize(snap.Identity.Market),
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
                    : new(),
                EarliestBarDate = EarliestBarDate(snap.Data, snap.RestoredLoad, snap.Identity)
            };
        }

        /// <summary>
        /// The history depth to save for a tab: its first bar, or — for a restored tab whose
        /// data has not loaded yet (never visited this session) — the depth it was restored
        /// with, so saving again does not shrink it to 200 bars.
        /// </summary>
        private static DateTime? EarliestBarDate(TimeSeriesBuffer<Ohlcv> data, RestoredTabLoad? restored, ChartIdentity identity) =>
            data.Count > 0 ? data[0].Date
            : restored is { } r && r.Identity == identity ? r.HistoryFrom
            : null;

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
            Converters =
            {
                new Alerts.ConditionNodeNewtonsoftBridge(),

                // ENUMS BY NAME, not by ordinal. `AlertDefinition.Pattern` is a persisted
                // CandlePattern? and Newtonsoft's default is the NUMBER — so removing or
                // reordering a member of that enum silently rebinds every saved alert naming a
                // later one onto whatever inherited its position, in a file the user cannot read
                // and on a feature that fires unattended. The identical trap was found in the
                // shortcut profiles on 2026-09-04, where deleting five commands from mid-enum
                // would have rebound a saved keymap onto QuickPlaceMarket.
                //
                // Safe to switch on: Newtonsoft READS both spellings, so alerts.json files
                // already on disk carrying numbers still load and are rewritten as names the
                // next time they are saved. Nothing needs migrating.
                new Newtonsoft.Json.Converters.StringEnumConverter(),
            },
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
    }
}
