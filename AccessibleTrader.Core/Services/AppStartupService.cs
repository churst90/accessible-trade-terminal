using System;
using System.Threading.Tasks;
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

            // Configure providers that already have an active stored key, so a
            // key-required provider is usable the moment it is selected. (Provider
            // configuration is otherwise lazy and gated behind the IsConfigured
            // sentinel in RefreshSymbolsAsync, which would never clear on its own.)
            await dataService.ConfigureStoredKeyProvidersAsync().ConfigureAwait(false);

            // 1b. Indicator Plugins — scan Plugins/Indicators/ for drop-in indicator DLLs.
            var indicatorService = _services.GetRequiredService<IIndicatorService>();
            indicatorService.LoadIndicatorPlugins(pluginLoader);

            // 2. Data Orchestration
            _services.GetRequiredService<IDataOrchestrationService>();

            // 3. Input & Navigation
            _services.GetRequiredService<IInputRouter>();
            _services.GetRequiredService<IChartCommandManager>();

            // 4. Accessibility Feedback System
            _services.GetRequiredService<IHistoryBufferCoordinator>();
            _services.GetRequiredService<IAccessibilityFeedbackCoordinator>();

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
