using System.Reactive.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.Workspace
{
    /// <summary>
    /// Session autosave + resume (2026-07-22 audit: closing the app — or
    /// refreshing the WebHost browser tab — without an explicit save lost every
    /// tab, drawing, and indicator stack; startup always began blank).
    ///
    /// The current workspace is snapshotted to a RESERVED profile
    /// ("__last-session__", hidden from the workspace list) at most once per
    /// sample interval while the state is changing, plus explicitly from the
    /// shutdown hooks (MAUI page teardown, WebHost circuit close). At startup,
    /// <see cref="TryResumeAtStartup"/> restores it through the same
    /// RestoreWorkspace flow the Load Workspace modal uses. Opt-out via the
    /// "workspace.resumeLastSession" setting (default ON); demo sessions never
    /// autosave.
    /// </summary>
    public interface ISessionAutosaveService : IDisposable
    {
        /// <summary>Snapshot the current workspace now (no-op when blank,
        /// disabled, or in demo mode). Called from shutdown hooks.</summary>
        void SaveNow();

        /// <summary>Restore the last session at startup. Returns false when
        /// disabled, in demo mode, nothing was saved, or a workspace is already
        /// loaded. Announces the resume on success.</summary>
        bool TryResumeAtStartup();
    }

    public sealed class SessionAutosaveService : ISessionAutosaveService
    {
        public const string LastSessionProfileName = "__last-session__";
        public static readonly TimeSpan DefaultSampleInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// How many session slots are kept before the oldest are pruned. Small: the only one
        /// that is ever read is the most recent, and the rest exist so that a session which is
        /// still running does not have its slot deleted out from under it.
        /// </summary>
        private const int SlotsToKeep = 4;

        /// <summary>
        /// This session's own autosave slot.
        ///
        /// <para><b>What went wrong.</b> <c>ISessionAutosaveService</c> is <c>AddScoped</c> on
        /// the WebHost — one per Blazor circuit, each with its own <see cref="IWorkspaceStore"/>
        /// — and every one of them sampled its own state and wrote
        /// <c>SaveWorkspaceProfile("__last-session__", …)</c> into the same directory. Two
        /// browser tabs on different charts made that one file flip between them every sample
        /// interval, and <c>OnCircuitClosedAsync</c> made the LAST TAB CLOSED the winner
        /// regardless of which one the user had actually been working in. The same class was
        /// filed for <c>MyDataStore</c> and the <c>PaperAccountHub</c> solution to the identical
        /// two-tab problem was never extended here.</para>
        ///
        /// <para><b>What this does.</b> Each session writes its own slot, so no session can
        /// clobber another's, and resume reads the slot that was written most recently — which
        /// is the tab the user was last working in, which is the one they meant. The legacy
        /// unsuffixed name is still readable so an existing user's saved session survives the
        /// upgrade.</para>
        /// </summary>
        private readonly string _slotName =
            $"{LastSessionProfileName}{Guid.NewGuid():N}";

        private readonly IWorkspaceStore _store;
        private readonly IWorkspaceLibraryService _library;
        private readonly IWorkspaceInitializer _initializer;
        private readonly ISettingsManager _settings;
        private readonly DemoPolicy _demo;
        private readonly IEventBus _eventBus;
        private readonly ILogger<SessionAutosaveService> _logger;
        private readonly IDisposable? _sampleSub;

        public SessionAutosaveService(
            IWorkspaceStore store,
            IWorkspaceLibraryService library,
            IWorkspaceInitializer initializer,
            ISettingsManager settings,
            DemoPolicy demo,
            IEventBus eventBus,
            ILogger<SessionAutosaveService> logger,
            TimeSpan? sampleInterval = null)
        {
            _store = store;
            _library = library;
            _initializer = initializer;
            _settings = settings;
            _demo = demo;
            _eventBus = eventBus;
            _logger = logger;

            if (!_demo.IsDemo)
            {
                // Sample, not Throttle: a busy live chart dispatches constantly and
                // a trailing-edge throttle would never fire. Sample emits at most
                // once per interval and only when the state actually changed.
                _sampleSub = _store.StateStream
                    .Sample(sampleInterval ?? DefaultSampleInterval)
                    .Subscribe(_ => Save(force: false));
            }
        }

        public bool IsEnabled =>
            _settings.GetSetting(SettingsKeys.ResumeLastSession)?.ToObject<bool>() ?? true;

        /// <summary>What this session last wrote to its slot, serialized — the sampled save's
        /// "has anything a resume would give back changed" test. Guarded by <see cref="_saveLock"/>:
        /// the sample fires on a pool thread and the shutdown hooks on the circuit's.</summary>
        private string? _lastWritten;
        private readonly object _saveLock = new();

        /// <summary>
        /// The shutdown hooks' save. Always writes, even when nothing changed since the last
        /// sample: the write time is how resume knows this was the session the user left last.
        /// </summary>
        public void SaveNow() => Save(force: true);

        /// <summary>
        /// <para><b>A tick is not the user working.</b> "The most recently written slot is the
        /// tab the user was last working in" was only true while every write meant a change the
        /// user made. A live chart's state changes on every tick (DataManager dispatches
        /// <c>UpdateDataAction</c> and <c>LiveTickObservedAction</c> for each), so the sample
        /// below rewrote an untouched live tab's slot every 30 seconds, and with two terminal
        /// tabs open the one that ticked last won the resume — close the tab you were working
        /// in, restore from the tray, and get the other one. So the sampled save writes only
        /// when the configuration it would write differs from the one it last wrote.</para>
        /// </summary>
        private void Save(bool force)
        {
            if (_demo.IsDemo || !IsEnabled) return;
            // A blank session must NEVER clobber the saved one — startup runs
            // with an empty store before resume, and the user may simply not
            // have loaded anything yet.
            if (!IsMeaningful(_store.State)) return;

            try
            {
                var config = _library.CaptureWorkspace(_store);
                if (config == null) return;
                string written = ComparisonKey(config);
                lock (_saveLock)
                {
                    // An unchanged session still rewrites a slot that has gone from disk —
                    // another session's resume prunes all but the newest few, and a session
                    // that no longer writes on ticks can fall out of them while still open.
                    if (!force && written == _lastWritten && SlotIsOnDisk()) return;
                    _library.SaveProfile(_slotName, config);
                    _lastWritten = written;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Session autosave failed; will retry on the next sample.");
            }
        }

        /// <summary>
        /// The configuration as it would be written, minus the scroll position. A new bar on a
        /// chart at the live edge scrolls it (ViewportReducer.UpdateData), so the saved
        /// ViewportStartIndex moves on every bar a live market opens — and resume deliberately
        /// never re-applies it (WorkspaceInitializer.RestoreViewportLength opens at the live
        /// edge), so a change to it alone is not a change to anything the user gets back.
        /// </summary>
        private static string ComparisonKey(WorkspaceConfiguration config)
        {
            var json = JObject.FromObject(config);
            foreach (var start in json.Descendants().OfType<JProperty>()
                                      .Where(p => p.Name == nameof(TabConfiguration.ViewportStartIndex))
                                      .ToList())
                start.Value = 0;
            return json.ToString(Formatting.None);
        }

        private bool SlotIsOnDisk() =>
            _library.GetAllProfilesWithTimes().Any(p => p.Name == _slotName);

        /// <summary>Every session slot on disk, newest first. Includes the legacy unsuffixed
        /// name so a session saved before per-slot autosave still resumes.</summary>
        private List<(string Name, DateTime LastWriteUtc)> SessionSlots() =>
            _library.GetAllProfilesWithTimes()
                    .Where(p => p.Name.StartsWith(LastSessionProfileName, StringComparison.Ordinal))
                    .OrderByDescending(p => p.LastWriteUtc)
                    .ToList();

        /// <summary>
        /// Drops the oldest slots beyond <see cref="SlotsToKeep"/>. Never touches this
        /// session's own slot, and never runs often enough to matter — once, at resume.
        /// </summary>
        private void PruneOldSlots(IReadOnlyList<(string Name, DateTime LastWriteUtc)> slots)
        {
            foreach (var stale in slots.Skip(SlotsToKeep))
            {
                if (stale.Name == _slotName) continue;
                try { _library.DeleteProfile(stale.Name); }
                catch (Exception ex)
                {
                    // Housekeeping only: a slot that will not delete costs a few KB.
                    _logger.LogDebug(ex, "Could not prune old session slot {Slot}.", stale.Name);
                }
            }
        }

        public bool TryResumeAtStartup()
        {
            if (_demo.IsDemo || !IsEnabled) return false;
            if (IsMeaningful(_store.State)) return false; // something is already loaded

            WorkspaceConfiguration? config;
            List<(string Name, DateTime LastWriteUtc)> slots;
            try
            {
                // The most recently WRITTEN slot is the tab the user was last working in.
                // Reading a fixed name is what made two browser tabs fight over one file.
                slots = SessionSlots();
                config = slots.Count == 0 ? null : _library.LoadProfile(slots[0].Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Last-session profile could not be read; starting fresh.");
                return false;
            }
            if (config == null) return false;

            PruneOldSlots(slots);

            try
            {
                _initializer.RestoreWorkspace(config);
                int tabs = config.IsMultiTab ? config.Tabs.Count : 1;
                string resumed = tabs <= 1
                    ? "Resumed your last session."
                    : $"Resumed your last session: {tabs} tabs.";
                // Anything the restore had to leave off — an indicator since removed from the
                // app — rides on the same sentence, so it is said once and nothing cuts it off.
                string said = WithActiveChart(resumed);
                if (!string.IsNullOrWhiteSpace(_initializer.LastRestoreNotice))
                    said += " " + _initializer.LastRestoreNotice;
                _eventBus.Publish(new AnnouncementEvent(said,
                    Channel: AccessibleTrader.Core.Services.Accessibility.SpeechChannel.Interface));
                _logger.LogInformation("Resumed last session ({Tabs} tab(s)).", tabs);
                return true;
            }
            catch (Exception ex)
            {
                // A failed resume must never wedge startup — the blank chart is
                // always a safe landing.
                _logger.LogError(ex, "Resuming the last session failed; starting fresh.");
                return false;
            }
        }

        /// <summary>
        /// The resume sentence followed by what came back on the chart in front of the user:
        /// "BTCUSDT 1 day, with Cipher B, Market Structure and RSI."
        ///
        /// <para>
        /// Cody, 2026-10-15: "Resumed your last session" said nothing about WHAT resumed, so
        /// after a restart he could not tell by ear whether the layout he had left was the one
        /// he got back — he believed indicators were missing and had no quick way to check. The
        /// active chart and its indicators make a wrong or stale layout audible on arrival.
        /// </para>
        ///
        /// <para>
        /// Read from the STORE after the restore, not from the saved file: the store is what is
        /// actually on screen. The saved names can be stale (restore re-derives an indicator's
        /// name from its parameters, so an old file's "Cipher SR 5 20 1.2 …" is "Cipher SR" by
        /// the time it is on the chart), and the restore is what decides which tab is active.
        /// Names are <c>Name</c>, the one series navigation speaks. Candles, Volume and Price are
        /// on every chart and are not "your indicators"; drawings are not indicators either.
        /// </para>
        ///
        /// <para>An announcement must never cost the user the resume, so anything that goes
        /// wrong here falls back to the bare sentence.</para>
        /// </summary>
        private string WithActiveChart(string resumed)
        {
            try
            {
                var state = _store.State;
                var id = state.Identity;
                string chart = string.IsNullOrWhiteSpace(id.Symbol) ? "Chart" : id.Symbol;
                if (!string.IsNullOrWhiteSpace(id.Timeframe))
                    chart += " " + Accessibility.ChartLayoutDescriber.SpokenTimeframe(id.Timeframe);

                var names = (state.ActiveSeries ?? System.Collections.Immutable.ImmutableList<ChartSeries>.Empty)
                    .Where(s => !WorkspaceInitializer.IsCoreStackSeries(s.Id) && !s.IsDrawing)
                    .Select(s => !string.IsNullOrWhiteSpace(s.Name) ? s.Name
                               : !string.IsNullOrWhiteSpace(s.FriendlyName) ? s.FriendlyName
                               : s.IndicatorCode)
                    .ToList();

                // Three names is a sentence; more is a list nobody can hold, and the count is
                // still enough to notice "that should be seven".
                string indicators = names.Count switch
                {
                    0 => "no indicators",
                    1 => $"with {names[0]}",
                    <= 3 => $"with {string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
                    _ => $"with {names.Count} indicators",
                };
                return $"{resumed} {chart}, {indicators}.";
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not describe the resumed chart; announcing the resume alone.");
                return resumed;
            }
        }

        private static bool IsMeaningful(WorkspaceState state) =>
            !string.IsNullOrEmpty(state.Identity.Symbol)
            || (state.ActiveSeries?.Count ?? 0) > 0
            || (state.TabSnapshots?.Any(t => !string.IsNullOrEmpty(t.Identity.Symbol)) ?? false);

        public void Dispose() => _sampleSub?.Dispose();
    }
}
