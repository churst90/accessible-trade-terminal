using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AccessibleTrader.Core.Models;
using Newtonsoft.Json;

namespace AccessibleTrader.Core.Services
{
    public record ShortcutDisplayBinding(string Description, string DisplayString);

    public interface IShortcutManager
    {
        ShortcutProfile CurrentProfile { get; }
        SystemCommand GetCommand(string key, bool shift, bool ctrl, bool alt);
        void LoadProfile(ShortcutProfile profile);
        void SaveToDisk();
        void LoadFromDisk();
        IReadOnlyList<ShortcutDisplayBinding> GetAllBindings();

        /// <summary>
        /// Replaces all existing bindings for <paramref name="command"/> with the new key combo.
        /// Also removes any conflicting binding that already uses the same combo.
        /// Persists the updated profile to disk immediately.
        /// </summary>
        /// <returns>
        /// Any OTHER commands that were left with NO binding at all as a result of this
        /// rebind stealing their combo (a command with a second binding is not reported).
        /// Callers surface this so a keyboard user learns a shortcut went unbound rather
        /// than discovering it silently later. Empty when nothing was displaced.
        /// </returns>
        IReadOnlyList<SystemCommand> UpdateBinding(SystemCommand command, string key, bool shift = false, bool ctrl = false, bool alt = false);
    }

    public class ShortcutManager : IShortcutManager
    {
        public ShortcutProfile CurrentProfile { get; private set; } = new();
        private readonly Dictionary<string, ShortcutDefinition> _lookup = new();
        private readonly string _filepath;

        public ShortcutManager(IPlatformPathService pathService)
        {
            // PLATFORM PATH: Use the provided path service for cross-platform support.
            _filepath = Path.Combine(pathService.AppDataDirectory, "shortcuts.json");

            InitializeDefaultProfile();
            LoadFromDisk(); // Override with user settings if they exist
        }

        /// <summary>
        /// Loads a user-customised shortcut profile from the local app-data directory.
        /// If no file exists the default profile remains active; any JSON parse failures are silently ignored
        /// so a corrupted file never prevents the app from starting.
        /// </summary>
        public void LoadFromDisk()
        {
            try
            {
                if (File.Exists(_filepath))
                {
                    string json = File.ReadAllText(_filepath);
                    var profile = JsonConvert.DeserializeObject<ShortcutProfile>(json);
                    if (profile != null)
                    {
                        LoadProfile(profile);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load shortcuts: {ex.Message}");
            }
        }

        public void SaveToDisk()
        {
            try
            {
                string json = JsonConvert.SerializeObject(CurrentProfile, Formatting.Indented);
                AtomicFile.WriteAllText(_filepath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save shortcuts: {ex.Message}");
            }
        }

        public void LoadProfile(ShortcutProfile profile)
        {
            CurrentProfile = profile;
            _lookup.Clear();
            foreach (var s in profile.Shortcuts)
            {
                string key = GenerateLookupKey(s.Key, s.Shift, s.Ctrl, s.Alt);
                _lookup[key] = s;
            }
        }

        /// <summary>
        /// Returns the <see cref="SystemCommand"/> bound to the given key combination,
        /// or <see cref="SystemCommand.None"/> if the combination is not mapped.
        /// </summary>
        public SystemCommand GetCommand(string key, bool shift, bool ctrl, bool alt)
        {
            string lookupKey = GenerateLookupKey(key, shift, ctrl, alt);
            return _lookup.TryGetValue(lookupKey, out var definition) ? definition.Command : SystemCommand.None;
        }

        private string GenerateLookupKey(string key, bool shift, bool ctrl, bool alt)
        {
            return $"{key.ToUpperInvariant()}|S:{shift}|C:{ctrl}|A:{alt}";
        }

        public IReadOnlyList<ShortcutDisplayBinding> GetAllBindings()
        {
            return CurrentProfile.Shortcuts.Select(b => new ShortcutDisplayBinding(
                b.Command.ToString(),
                FormatBinding(b)
            )).ToList();
        }

        private static string FormatBinding(ShortcutDefinition b)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (b.Ctrl)  parts.Add("Ctrl");
            if (b.Alt)   parts.Add("Alt");
            if (b.Shift) parts.Add("Shift");
            parts.Add(b.Key.Length == 1 ? b.Key.ToUpperInvariant() : b.Key);
            return string.Join("+", parts);
        }

        public IReadOnlyList<SystemCommand> UpdateBinding(SystemCommand command, string key, bool shift = false, bool ctrl = false, bool alt = false)
        {
            string newComboKey = GenerateLookupKey(key, shift, ctrl, alt);

            // Commands currently occupying the target combo (excluding the one we're
            // binding) — these are about to lose that combo to conflict resolution.
            var displaced = CurrentProfile.Shortcuts
                .Where(s => GenerateLookupKey(s.Key, s.Shift, s.Ctrl, s.Alt) == newComboKey
                            && s.Command != command)
                .Select(s => s.Command)
                .Distinct()
                .ToList();

            // Remove any binding already occupying this combo (conflict resolution)
            CurrentProfile.Shortcuts.RemoveAll(s =>
                GenerateLookupKey(s.Key, s.Shift, s.Ctrl, s.Alt) == newComboKey);
            // Remove all old bindings for this command
            CurrentProfile.Shortcuts.RemoveAll(s => s.Command == command);
            // Add the new binding
            CurrentProfile.Shortcuts.Add(new ShortcutDefinition(command, key.ToUpperInvariant(), Shift: shift, Ctrl: ctrl, Alt: alt));
            LoadProfile(CurrentProfile);
            SaveToDisk();

            // A displaced command is only truly stranded if it now has no binding at
            // all — one that had a second combo (e.g. arrow keys) is unaffected.
            return displaced
                .Where(c => !CurrentProfile.Shortcuts.Any(s => s.Command == c))
                .ToList();
        }

        private void InitializeDefaultProfile()
        {
            var p = new ShortcutProfile { Name = "Default" };
            var s = p.Shortcuts;

            // Navigation - X Axis
            s.Add(new(SystemCommand.NavLeft, "LEFT"));
            s.Add(new(SystemCommand.NavLeft, "ARROWLEFT"));
            s.Add(new(SystemCommand.NavRight, "RIGHT"));
            s.Add(new(SystemCommand.NavRight, "ARROWRIGHT"));
            s.Add(new(SystemCommand.NavHome, "HOME"));
            s.Add(new(SystemCommand.NavEnd, "END"));
            s.Add(new(SystemCommand.JumpToLatest, "OEM5")); // \ key
            s.Add(new(SystemCommand.JumpToLatest, "\\"));

            // Navigation - Y Axis (Components)
            s.Add(new(SystemCommand.NavUp, "UP"));
            s.Add(new(SystemCommand.NavUp, "ARROWUP"));
            s.Add(new(SystemCommand.NavDown, "DOWN"));
            s.Add(new(SystemCommand.NavDown, "ARROWDOWN"));

            // Navigation - Series Switching
            s.Add(new(SystemCommand.NavPageUp, "PAGEUP"));
            s.Add(new(SystemCommand.NavPageDown, "PAGEDOWN"));

            // Viewport Control
            s.Add(new(SystemCommand.PanLeft, "OEM4")); // [
            s.Add(new(SystemCommand.PanLeft, "["));
            s.Add(new(SystemCommand.PanRight, "OEM6")); // ]
            s.Add(new(SystemCommand.PanRight, "]"));
            s.Add(new(SystemCommand.ZoomOut, "OEMMINUS")); // -
            s.Add(new(SystemCommand.ZoomOut, "-"));
            s.Add(new(SystemCommand.ZoomIn, "OEMPLUS")); // =
            s.Add(new(SystemCommand.ZoomIn, "="));

            // Speed
            s.Add(new(SystemCommand.PlaySpeedDown, "OEMMINUS", Shift: true));
            s.Add(new(SystemCommand.PlaySpeedDown, "-", Shift: true));
            s.Add(new(SystemCommand.PlaySpeedUp, "OEMPLUS", Shift: true));
            s.Add(new(SystemCommand.PlaySpeedUp, "=", Shift: true));

            // Panning Granularity:
            //   Shift+[ (OEM4) — DECREASE step (e.g. 15 % → 10 %)
            //   Shift+] (OEM6) — INCREASE step (e.g. 10 % → 15 %)
            // keyboard.js normalises the shifted chars { and } back to OEM4/OEM6 so
            // ShortcutManager can distinguish them by the Shift flag alone.
            s.Add(new(SystemCommand.GranularityDown, "OEM4", Shift: true));
            s.Add(new(SystemCommand.GranularityUp,   "OEM6", Shift: true));

            // Indicator pane scrolling: Alt+Up / Alt+Down
            s.Add(new(SystemCommand.ScrollPanesUp,   "Up",   Alt: true));
            s.Add(new(SystemCommand.ScrollPanesDown, "Down", Alt: true));

            // Explicit chart focus: Ctrl+Alt+Shift+C
            s.Add(new(SystemCommand.ChartFocus, "C", Ctrl: true, Alt: true, Shift: true));

            // Series navigation is Page Up / Page Down (defined above as NavPageUp/NavPageDown).
            // Ctrl+Up / Ctrl+Down are bound below to intra-pane component navigation
            // (NavComponentInPaneNext/Prev) — see the "Intra-pane component navigation" block.
            // They are NOT used for series switching.

            // Visibility & State
            s.Add(new(SystemCommand.ToggleIndicatorVisibility, "H"));
            s.Add(new(SystemCommand.ToggleIndicatorAudio, "M"));
            s.Add(new(SystemCommand.AddReferenceLevel, "0"));
            s.Add(new(SystemCommand.OpenProperties, "P"));
            // Delete removes the focused indicator series (guards against deleting "candles").
            s.Add(new(SystemCommand.RemoveSelectedSeries, "DELETE"));

            // Chart display toggles (Alt+key) — require no data gate, so always work.
            s.Add(new(SystemCommand.ToggleHeikinAshi, "C", Alt: true)); // Alt+C
            s.Add(new(SystemCommand.ToggleLogScale,   "L", Alt: true)); // Alt+L

            // Modal shortcuts (Alt+key)
            s.Add(new(SystemCommand.OpenObjectTree,       "O", Alt: true)); // Alt+O
            s.Add(new(SystemCommand.OpenTradingDashboard, "T", Alt: true)); // Alt+T
            s.Add(new(SystemCommand.OpenOrderBook,        "B", Alt: true)); // Alt+B
            s.Add(new(SystemCommand.ToggleHeatmap,        "H", Alt: true)); // Alt+H
            s.Add(new(SystemCommand.OpenApiKeys,          "K", Alt: true)); // Alt+K
            s.Add(new(SystemCommand.OpenDrawingTools,     "D", Alt: true)); // Alt+D

            // Functional / Information
            s.Add(new(SystemCommand.OpenHelp,     "F1"));
            s.Add(new(SystemCommand.OpenSettings, "F12"));
            s.Add(new(SystemCommand.OpenProperties, "F12", Shift: true));
            // The F-key mute grammar (2026-07-21 redesign): unshifted = the
            // INTERACTIVE channel (things you asked for), Shift = the AMBIENT
            // channel (things that happen to you). F4 pairs braille the same way.
            // ContextSummary moved F4 → Shift+F1 (help family); custom bindings
            // saved by users are untouched — only these defaults changed.
            s.Add(new(SystemCommand.ToggleSpeech, "F2"));
            s.Add(new(SystemCommand.ToggleEventSpeech, "F2", Shift: true));
            s.Add(new(SystemCommand.ToggleSonification, "F3"));
            s.Add(new(SystemCommand.ToggleEarcons, "F3", Shift: true));
            s.Add(new(SystemCommand.ToggleBraille, "F4"));
            s.Add(new(SystemCommand.OpenBrailleSettings, "F4", Shift: true));
            s.Add(new(SystemCommand.ContextSummary, "F1", Shift: true));

            // Volume Control
            s.Add(new(SystemCommand.VolCompUp, "F5"));
            s.Add(new(SystemCommand.VolCompDown, "F5", Shift: true));
            s.Add(new(SystemCommand.VolSeriesUp, "F6"));
            s.Add(new(SystemCommand.VolSeriesDown, "F6", Shift: true));
            s.Add(new(SystemCommand.VolChartUp, "F7"));
            s.Add(new(SystemCommand.VolChartDown, "F7", Shift: true));
            

            // Playback
            s.Add(new(SystemCommand.PlayStop, "ESCAPE", Shift: true)); // Shift+Escape: force-stop all playback
            s.Add(new(SystemCommand.PlayChart, "SPACE"));
            s.Add(new(SystemCommand.PlayChart, " "));
            s.Add(new(SystemCommand.PlaySeries, "SPACE", Shift: true));
            s.Add(new(SystemCommand.PlaySeries, " ", Shift: true));
            s.Add(new(SystemCommand.PlayComponent, "SPACE", Ctrl: true, Shift: true));
            s.Add(new(SystemCommand.PlayComponent, " ", Ctrl: true, Shift: true));
            s.Add(new(SystemCommand.PlayPause, "SPACE", Ctrl: true));
            s.Add(new(SystemCommand.PlayPause, " ", Ctrl: true));

            // Drawing Tools (Ctrl+Shift+letter)
            s.Add(new(SystemCommand.DrawTrend,      "T", Ctrl: true, Shift: true));
            s.Add(new(SystemCommand.DrawHorizontal, "H", Ctrl: true, Shift: true));
            s.Add(new(SystemCommand.DrawVertical,   "V", Ctrl: true, Shift: true));
            s.Add(new(SystemCommand.DrawChannel,    "C", Ctrl: true, Shift: true));
            s.Add(new(SystemCommand.DrawFibonacci,  "F", Ctrl: true, Shift: true));
            s.Add(new(SystemCommand.DrawLabel,      "L", Ctrl: true, Shift: true));
            s.Add(new(SystemCommand.DrawFibExtension, "E", Ctrl: true, Shift: true));
            s.Add(new(SystemCommand.DrawPitchfork,  "A", Ctrl: true, Shift: true)); // Ctrl+Shift+A
            s.Add(new(SystemCommand.DrawRectangle,  "R", Ctrl: true, Shift: true));
            s.Add(new(SystemCommand.DrawMeasure,    "M", Ctrl: true, Shift: true));
            s.Add(new(SystemCommand.DrawGannFan,      "G", Ctrl: true, Shift: true));
            s.Add(new(SystemCommand.DrawRiskReward,   "P", Ctrl: true, Shift: true));
            s.Add(new(SystemCommand.DrawAnchoredVwap, "W", Ctrl: true, Shift: true));
            s.Add(new(SystemCommand.DrawGannBox,      "B", Ctrl: true, Shift: true));
            s.Add(new(SystemCommand.DrawAngleFib,     "J", Ctrl: true, Shift: true));
            // Escape cancels an in-progress drawing placement when the chart has focus,
            // OR closes the topmost open modal — CommandDispatcher re-routes Escape to
            // CloseModal whenever _openModalCount > 0.
            s.Add(new(SystemCommand.CancelDrawing,  "ESCAPE"));
            // NOTE: There is no Enter/Return binding for drawing. Anchors are set by
            // re-pressing the same tool shortcut (e.g. Ctrl+Shift+T) at each point — the
            // DrawingInteractionManager state machine advances one anchor per press.
            // ConfirmCoordinateEntry is reserved/unused (no handler in CommandDispatcher).
            // Application/Menu key + Shift+F10: open the right-click context menu on
            // the focused drawing — keyboard parity with mouse right-click.
            s.Add(new(SystemCommand.OpenDrawingContextMenu, "CONTEXTMENU"));
            s.Add(new(SystemCommand.OpenDrawingContextMenu, "F10", Shift: true));
            // Ctrl+Left/Right: jump to nearest trendline-price crossing
            s.Add(new(SystemCommand.NavLeftJump,  "LEFT",  Ctrl: true));
            s.Add(new(SystemCommand.NavRightJump, "RIGHT", Ctrl: true));

            // Detail summary: Ctrl+Shift+D speaks full candle pattern analysis for the current bar.
            s.Add(new(SystemCommand.DetailedPointSummary, "D", Ctrl: true, Shift: true));
            // Narration toggle: Ctrl+Alt+Shift+N enables/disables auto-narration for the focused series.
            s.Add(new(SystemCommand.ToggleNarration, "N", Ctrl: true, Alt: true, Shift: true)); // Ctrl+Alt+Shift+N

            // Multi-tab shortcuts
            s.Add(new(SystemCommand.AddTab,       "T",   Ctrl: true));                    // Ctrl+T (desktop)
            // Web-safe new-tab chord: Ctrl+T is reserved by browsers (opens a browser
            // tab), so Alt+Shift+N (a chord browsers leave alone) also adds a chart tab.
            // Bound on every head so the muscle memory is identical; on the WebHost the
            // remap drops Ctrl+T and this is the one that works.
            s.Add(new(SystemCommand.AddTab,       "N",   Alt: true, Shift: true));        // Alt+Shift+N
            s.Add(new(SystemCommand.CloseTab,     "W",   Ctrl: true));                    // Ctrl+W
            s.Add(new(SystemCommand.SwitchTabNext, "TAB", Ctrl: true));                   // Ctrl+Tab
            s.Add(new(SystemCommand.SwitchTabPrev, "TAB", Ctrl: true, Shift: true));      // Ctrl+Shift+Tab
            // Ctrl+Tab is reserved by browsers (switches browser tabs), so on the web give
            // the user a way to reach the tab switcher bar by keyboard and drive it with the
            // arrow keys / Home / End / number row / Delete. Three-modifier chord = web-safe.
            s.Add(new(SystemCommand.FocusTabBar,  "T",   Ctrl: true, Alt: true, Shift: true)); // Ctrl+Alt+Shift+T

            // AI Analyst
            s.Add(new(SystemCommand.OpenAIAnalyst, "A", Ctrl: true, Alt: true, Shift: true)); // Ctrl+Alt+Shift+A

            // Workspace management
            s.Add(new(SystemCommand.SaveWorkspace, "W", Ctrl: true, Alt: true, Shift: true)); // Ctrl+Alt+Shift+W
            s.Add(new(SystemCommand.LoadChart, "L", Ctrl: true, Alt: true, Shift: true)); // Ctrl+Alt+Shift+L
            s.Add(new(SystemCommand.OpenMyData, "I", Ctrl: true, Alt: true, Shift: true)); // Ctrl+Alt+Shift+I
            s.Add(new(SystemCommand.LoadWorkspace, "W", Ctrl: true, Alt: true));               // Ctrl+Alt+W

            // Sub-pane navigation (jump between panes)
            s.Add(new(SystemCommand.NavSubPaneNext, "PAGEDOWN", Ctrl: true)); // Ctrl+PageDown
            s.Add(new(SystemCommand.NavSubPanePrev, "PAGEUP",   Ctrl: true)); // Ctrl+PageUp

            // Intra-pane component navigation (cycle within the focused component's pane)
            s.Add(new(SystemCommand.NavComponentInPaneNext, "DOWN", Ctrl: true)); // Ctrl+Down
            s.Add(new(SystemCommand.NavComponentInPanePrev, "UP",   Ctrl: true)); // Ctrl+Up

            // Modal shortcuts (Alt combinations)
            s.Add(new(SystemCommand.OpenAlerts,        "J",     Alt: true)); // Alt+J
            s.Add(new(SystemCommand.OpenIndicators,    "A",     Alt: true)); // Alt+A
            s.Add(new(SystemCommand.OpenStrategies,    "S",     Alt: true)); // Alt+S
            s.Add(new(SystemCommand.OpenCustomScripts, ",",     Alt: true)); // Alt+,
            s.Add(new(SystemCommand.OpenSoundDesigner, "W",     Alt: true)); // Alt+W
            s.Add(new(SystemCommand.OpenWatchlist,     "M",     Alt: true)); // Alt+M: market watch (watchlists + screener)
            s.Add(new(SystemCommand.OpenLevelReport,   "R",     Alt: true)); // Alt+R: respect report (ranked levels + MAs)

            // Bar replay — practise reading a market forward without hindsight.
            // Grouped on F9-F11 so the transport keys sit together under one hand.
            s.Add(new(SystemCommand.ReplayStepForward, "F9"));                                     // F9
            s.Add(new(SystemCommand.ReplayStepBack,    "F9", Shift: true));                        // Shift+F9
            s.Add(new(SystemCommand.ReplayPlayPause,   "F10"));                                    // F10
            // F11 is the natural third key, but browsers own it for fullscreen and page-level
            // preventDefault on it is unreliable — so the three-modifier chord is kept as the
            // binding that always works, and F11 is offered alongside it for the desktop heads.
            s.Add(new(SystemCommand.ReplayToggle,      "F11"));                                    // F11 (desktop)
            s.Add(new(SystemCommand.ReplayToggle,      "P",  Ctrl: true, Alt: true, Shift: true)); // Ctrl+Alt+Shift+P

            // Split view — a second tab rendered beside the active one (reference view).
            s.Add(new(SystemCommand.SplitViewToggle,      "S", Ctrl: true, Alt: true, Shift: true)); // Ctrl+Alt+Shift+S
            s.Add(new(SystemCommand.SplitViewCycle,       "E", Ctrl: true, Alt: true, Shift: true)); // Ctrl+Alt+Shift+E
            s.Add(new(SystemCommand.SplitViewOrientation, "O", Ctrl: true, Alt: true, Shift: true)); // Ctrl+Alt+Shift+O
            s.Add(new(SystemCommand.OpenJournal,       "J",     Ctrl: true, Alt: true, Shift: true)); // Ctrl+Alt+Shift+J: speech / alert journal

            // ── Orientation and recovery ─────────────────────────────────────
            // Alt+Shift+L, not Alt+Shift+/ — on many layouts Shift+/ is not "?" at all, so a
            // binding written that way is layout-dependent and silently unavailable to anyone
            // outside a US keyboard. L for Layout is mnemonic and layout-stable.
            s.Add(new(SystemCommand.SpeakChartLayout,     "L", Alt: true, Shift: true));

            // Paired with the single-key H and M that hide and mute one component, so the
            // recovery shortcut is the same letter. NOT F5/F6, which already carry component and
            // series volume — and NOT Ctrl+Alt+Shift+M, which the conflict guard caught as
            // already belonging to Monitoring Status. All three orientation-and-recovery commands
            // sit under Alt+Shift so they are one group to learn rather than three.
            s.Add(new(SystemCommand.ShowAllComponents,    "H", Alt: true, Shift: true));
            s.Add(new(SystemCommand.UnmuteAllComponents,  "M", Alt: true, Shift: true));
            s.Add(new(SystemCommand.MonitoringStatus,  "M",     Ctrl: true, Alt: true, Shift: true)); // Ctrl+Alt+Shift+M: background monitoring summary

            LoadProfile(p);
        }
    }
}
