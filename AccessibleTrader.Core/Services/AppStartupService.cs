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

        public AppStartupService(IServiceProvider services, ILogger<AppStartupService> logger)
        {
            _services = services;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            // Resolve in dependency order: data pipeline first, then input routing,
            // then accessibility coordinators that depend on both.

            // 1. Plugins & Data Services
            var dataService = _services.GetRequiredService<IDataService>();
            var pluginLoader = _services.GetRequiredService<IPluginLoaderService>();
            await dataService.InitializeAsync(pluginLoader);

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
            //    calls LoadAll().
            var autoLoader = _services.GetService<Strategies.StrategyAutoLoader>();
            autoLoader?.LoadAll();

            // 7. Announce any platform features that are stubbed on the current target.
            // This converts silent no-ops into audible warnings so users and testers can
            // identify missing capabilities without needing to read source code.
            WarnAboutUnimplementedPlatformFeatures();
        }

        private void WarnAboutUnimplementedPlatformFeatures()
        {
            // Mac Catalyst keyboard, Android audio, and iOS audio are now implemented (Phase 7).
            // No platform-specific startup warnings required.
        }
    }
}
