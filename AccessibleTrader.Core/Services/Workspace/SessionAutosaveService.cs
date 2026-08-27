using System.Reactive.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;

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
                // once per interval and only when the state actually changed —
                // idle sessions write nothing.
                _sampleSub = _store.StateStream
                    .Sample(sampleInterval ?? DefaultSampleInterval)
                    .Subscribe(_ => SaveNow());
            }
        }

        public bool IsEnabled =>
            _settings.GetSetting(SettingsKeys.ResumeLastSession)?.ToObject<bool>() ?? true;

        public void SaveNow()
        {
            if (_demo.IsDemo || !IsEnabled) return;
            // A blank session must NEVER clobber the saved one — startup runs
            // with an empty store before resume, and the user may simply not
            // have loaded anything yet.
            if (!IsMeaningful(_store.State)) return;

            try
            {
                _library.SaveWorkspaceProfile(_slotName, _store);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Session autosave failed; will retry on the next sample.");
            }
        }

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
                _eventBus.Publish(new AnnouncementEvent(tabs <= 1
                    ? "Resumed your last session."
                    : $"Resumed your last session: {tabs} tabs."));
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

        private static bool IsMeaningful(WorkspaceState state) =>
            !string.IsNullOrEmpty(state.Identity.Symbol)
            || (state.ActiveSeries?.Count ?? 0) > 0
            || (state.TabSnapshots?.Any(t => !string.IsNullOrEmpty(t.Identity.Symbol)) ?? false);

        public void Dispose() => _sampleSub?.Dispose();
    }
}
