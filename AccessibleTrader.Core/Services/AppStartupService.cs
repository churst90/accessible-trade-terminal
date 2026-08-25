using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Core.Services
{
    public interface IAppStartupService
    {
        /// <summary>
        /// Resolves and initializes all self-wiring singleton services that subscribe
        /// to events or store state in their constructors. Must be called once at
        /// application startup before any user interaction.
        /// </summary>
        Task InitializeAsync();
    }

    /// <summary>
    /// Centralizes application startup sequencing. All singletons that self-wire via
    /// constructor subscriptions are resolved here in dependency order so their
    /// subscriptions are active before the first UI event is dispatched.
    /// </summary>
    public class AppStartupService : IAppStartupService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<AppStartupService> _logger;

        // Run the init body exactly once per instance, even under concurrent callers.
        // On the WebHost this service is Scoped, so "once per instance" == "once per
        // browser circuit". On the MAUI head it is a Singleton, so this makes the call
        // idempotent: MainPage.xaml.cs fires it at startup AND the shared MainLayout
        // awaits it on first render — both share this one Task and the body runs once.
        private readonly object _initLock = new();
        private Task? _initTask;

        public AppStartupService(IServiceProvider services, ILogger<AppStartupService> logger)
        {
            _services = services;
            _logger = logger;
        }

        public Task InitializeAsync()
        {
            lock (_initLock) { return _initTask ??= InitializeCoreAsync(); }
        }

        private async Task InitializeCoreAsync()
        {
            // Resolve in dependency order: data pipeline first, then input routing,
            // then accessibility coordinators that depend on both.

            // 1. Plugins & Data Services
            var dataService = _services.GetRequiredService<IDataService>();
            var pluginLoader = _services.GetRequiredService<IPluginLoaderService>();
            await dataService.InitializeAsync(pluginLoader).ConfigureAwait(false);

            // Built-in (non-plugin) providers — e.g. the My Data CSV provider.
            // Registered after plugin init so name collisions resolve in favor of
            // what the user actually installed. Optional: heads that don't
            // register any IBuiltInDataProvider simply skip this.
            foreach (var builtIn in _services.GetServices<Core.Services.MyData.IBuiltInDataProvider>())
                dataService.RegisterProvider(builtIn);

            // Configure providers that already have an active stored key, so a
            // key-required provider is usable the moment it is selected. (Provider
            // configuration is otherwise lazy and gated behind the IsConfigured
            // sentinel in RefreshSymbolsAsync, which would never clear on its own.)
            await dataService.ConfigureStoredKeyProvidersAsync().ConfigureAwait(false);

            // 1b. Indicator Plugins — scan Plugins/Indicators/ for drop-in indicator DLLs.
            var indicatorService = _services.GetRequiredService<IIndicatorService>();
            indicatorService.LoadIndicatorPlugins(pluginLoader);

            // 1c. User audio material — resolve the wavetable library so persisted
            //     wavetable/sample imports register with the audio engine before the
            //     first patch that references them can play.
            _services.GetService<Audio.IWavetableLibrary>();

            // 1d. Global preferences — seed the store's speech/audio/viewport
            //     preferences from settings.json and arm the write-back subscription
            //     (they were session-only before debt item 3b: every launch reset them).
            _services.GetService<IPreferencePersistenceService>()?.Initialize();

            // 2. Data Orchestration
            _services.GetRequiredService<IDataOrchestrationService>();

            // 3. Input & Navigation
            _services.GetRequiredService<IInputRouter>();
            _services.GetRequiredService<IChartCommandManager>();

            // 4. Accessibility Feedback System
            _services.GetRequiredService<IHistoryBufferCoordinator>();
            _services.GetRequiredService<IAccessibilityFeedbackCoordinator>();

            // 4b. In-session alerts. Start() is what creates the StateStream subscription
            //     that evaluates alerts as the chart ticks — without it the alert pipeline
            //     is fully built, fully tested, and never armed: a price alert set while
            //     watching a chart could never fire, and only the opt-in background
            //     monitors (which run only while NO browser/session is attached) delivered
            //     anything at all. Resolved here because AppStartupService's lifetime
            //     matches IAlertOrchestrator's on both heads — singleton on MAUI,
            //     per-circuit on the WebHost.
            //
            //     Placed after step 4 so IAccessibilityFeedbackCoordinator is already
            //     subscribed to AlertFiredEvent (an alert that fires into no subscriber is
            //     the same silence one layer down), and before the session resume in step
            //     10 so a restored chart's first Ready tick is the orchestrator's warm-up
            //     tick rather than a missed one.
            _services.GetService<IAlertOrchestrator>()?.Start();

            // Bar replay listens for its transport keys on the EventBus, so it must be
            // constructed at startup — nothing else resolves it until the user presses a key,
            // and by then the subscription would not exist to handle that very key.
            _services.GetService<Analysis.IReplayService>();
            _services.GetService<Rendering.ISplitViewCoordinator>();

            // 5. Workspace Initializer — resolve so it's available for chart load and
            //    workspace restore, but do NOT seed default series on boot.
            //    The app launches with a blank workspace; series are created when the
            //    user loads a chart or restores a saved workspace.
            _services.GetRequiredService<IWorkspaceInitializer>();

            // 6. Strategy Auto-Loader — activate any library specs marked IsAutoActivate.
            //    Must run after data services are ready (steps 1-2) so strategies can
            //    resolve their indicator references. Idempotent — safe if MainLayout also
            //    calls LoadAllAsync(). Awaited so Roslyn recompiles happen off the
            //    blocking path instead of via task.Wait().
            var autoLoader = _services.GetService<Strategies.StrategyAutoLoader>();
            if (autoLoader != null)
                await autoLoader.LoadAllAsync().ConfigureAwait(false);

            // 7. Announce any platform features that are stubbed on the current target.
            // This converts silent no-ops into audible warnings so users and testers can
            // identify missing capabilities without needing to read source code.
            WarnAboutUnimplementedPlatformFeatures();

            // 8. Surface persistence-file quarantines. Stores (config, settings,
            //    strategy library, indicator prefs) load before any speech pipeline
            //    exists, so corrupt-file events are collected in CorruptFileQuarantine
            //    and announced here — a silent settings "reset" with no explanation is
            //    exactly the kind of invisible failure this app must never have.
            AnnounceQuarantinedFiles();

            // 9. Trading reconciliation — resolve so live-provider connections get
            //    a first-connect exposure announcement, and speak any persisted
            //    paper positions/orders now. A user who restarted with open
            //    exposure must hear about it without opening the dashboard.
            var reconciliation = _services.GetService<ITradingReconciliationCoordinator>();
            if (reconciliation != null)
                await reconciliation.AnnounceAtStartupAsync().ConfigureAwait(false);

            // 10. Session resume — restore the autosaved last session (opt-out via
            //     workspace.resumeLastSession) BEFORE the monitors reconcile so the
            //     restored tabs are covered. Resolving the service also arms the
            //     periodic autosave sampling.
            bool resumed = _services.GetService<Workspace.ISessionAutosaveService>()?.TryResumeAtStartup() ?? false;

            // Resume/RestoreWorkspace restores tab CONFIG only (identity, series, layout), not
            // data — so a resumed tab shows the hollow "△" title with no live price until a
            // manual Load Chart. Load the active tab now so its price/title populate on resume;
            // the other tabs load on first switch (the tab-switch catch-up path handles them).
            if (resumed)
            {
                try
                {
                    var orchestrator = _services.GetService<IMarketOrchestrator>();
                    if (orchestrator != null)
                        await orchestrator.LoadRestoredActiveTabAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Loading the resumed active tab failed; the chart can still be loaded manually.");
                }
            }

            // 11. Background workspace monitoring — resolve so its tab/settings
            //     subscriptions are live, then reconcile once for tabs restored
            //     from a saved workspace profile or the resumed session.
            _services.GetService<Workspace.IBackgroundMonitoringService>()?.Reconcile();

            // 12. Live background tab feeds (keyed feeds Phase C) — same pattern:
            //     resolving wires the tab/settings subscriptions; one reconcile
            //     covers tabs restored from a saved workspace profile.
            _services.GetService<Feeds.IBackgroundTabFeedService>()?.Reconcile();
        }

        private void AnnounceQuarantinedFiles()
        {
            var reports = CorruptFileQuarantine.SessionReports;
            if (reports.Count == 0) return;

            foreach (var report in reports)
                _logger.LogWarning("Persistence quarantine: {Report}", report);

            var eventBus = _services.GetService<IEventBus>();
            eventBus?.Publish(new FeedbackRequestEvent(
                FeedbackType.Error,
                reports.Count == 1
                    ? $"Warning: {reports[0]} Defaults are in use."
                    : $"Warning: {reports.Count} settings files were unreadable and have been reset. " +
                      "Backups were saved next to the originals with a corrupt extension. Defaults are in use.",
                IsUserInitiated: false));
        }

        private void WarnAboutUnimplementedPlatformFeatures()
        {
            // Mac Catalyst keyboard, Android audio, and iOS audio are now implemented (Phase 7).
            // No platform-specific startup warnings required.
        }
    }
}
