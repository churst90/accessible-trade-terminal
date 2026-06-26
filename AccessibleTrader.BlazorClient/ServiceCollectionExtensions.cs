using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Core.Services.Drawing.Calculators;
using AccessibleTrader.Core.Services.AI;
using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Persistence;
using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Alerts;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.BlazorClient
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers all Accessible Trader services in dependency order.
        /// Delegates to private domain-scoped helpers for readability and testability.
        /// </summary>
        public static IServiceCollection AddAccessibleTraderServices(this IServiceCollection services)
        {
            services.AddCoreInfrastructure();
            services.AddDataPipeline();
            services.AddIndicatorPipeline();
            services.AddRenderingServices();
            services.AddBusinessServices();
            services.AddInputRouting();
            services.AddAudioServices();
            services.AddAccessibilityServices();
            return services;
        }

        // ── Core Infrastructure ───────────────────────────────────────────────

        private static IServiceCollection AddCoreInfrastructure(this IServiceCollection services)
        {
            // Logging, platform utilities, event bus, and workspace state — everything
            // else in the application depends on at least one of these.
            services.AddSingleton<IAppLogger, MauiAppLogger>();
            services.AddSingleton<IPlatformPathService, MauiPathService>();
            services.AddSingleton<IRuntimePlatform, MauiRuntimePlatform>();
            services.AddSingleton<IMainThreadService, MauiMainThreadService>();
            services.AddSingleton<IEventBus, EventBus>();

            // Blazor → native bridge so the SKCanvasView margin tracks the actual
            // chart region rather than the hardcoded 185/100 DIP constants.
            services.AddSingleton<ICanvasRegionProvider, CanvasRegionProvider>();

            // WorkspaceStore decomposition services — registered before the store itself.
            services.AddSingleton<IViewportRangeCalculator, ViewportRangeCalculator>();
            services.AddSingleton<IViewportNavigationService, ViewportNavigationService>();
            services.AddSingleton<IVolumeStateService, VolumeStateService>();
            services.AddSingleton<IWorkspaceStore, WorkspaceStore>();

            // SQLite local cache — AppDbContext is used by DataCacheService and ApiKeyService.
            services.AddDbContextFactory<AppDbContext>((sp, options) =>
            {
                var pathService = sp.GetRequiredService<IPlatformPathService>();
                options.UseSqlite($"Data Source={Path.Combine(pathService.AppDataDirectory, "trader_local.db")}");
            });

            // Native UI drivers — platform-specific implementations of core SDK interfaces.
            services.AddSingleton<IInputService, BlazorInputService>();
            services.AddSingleton<ISpeechManager, BlazorSpeechManager>();
            services.AddSingleton<IAudioDriver, BlazorAudioDriver>();

            // Single MauiSecureStorageService instance serves both Core
            // (ISecureStorageService) and the plugin-host bridge
            // (IPluginSecureStorage). Register the concrete type as a singleton
            // and forward both interfaces to it so plugins and Core see the
            // same backing SecureStorage.
            services.AddSingleton<MauiSecureStorageService>();
            services.AddSingleton<ISecureStorageService>(sp => sp.GetRequiredService<MauiSecureStorageService>());
            services.AddSingleton<AccessibleTrader.Sdk.Services.IPluginSecureStorage>(sp => sp.GetRequiredService<MauiSecureStorageService>());

            // Plugin-host bridges added in phase 4 Track B: sign-time credential
            // checkout and an outbound-host-allow-listed HttpClient factory.
            // Both are read via PluginHostServices static accessors from plugin
            // code; we register through DI here and hand the resolved instance
            // to PluginHostServices in MauiProgram after builder.Build().
            // Per-provider P50/P95/P99 latency for the credential-checkout hot
            // path. Pure measurement — feeds the data-driven decision on whether
            // the 60-second session cache discussed in docs/TODO.md is justified.
            services.AddSingleton<AccessibleTrader.Core.Services.Diagnostics.CheckoutLatencyTracker>();
            services.AddSingleton<AccessibleTrader.Sdk.Services.IApiKeyCheckout, MauiApiKeyCheckoutAdapter>();
            services.AddSingleton<AccessibleTrader.Sdk.Services.IPluginHttpClientFactory, MauiPluginHttpClientFactory>();

            // Ring-buffer audit log for security-relevant runtime events
            // (AppContainer fallbacks, memory kills, credential checkout
            // failures, plugin-trust rejections). Operators inspect this
            // via the settings panel or an export. Also mirrors each event
            // to ILogger at Warning level so file-sink logs still capture
            // it for post-incident review.
            //
            // Post-audit 2026-04-23: the ring-buffer log is optionally wrapped
            // in a persistent JSONL file sink so events survive process
            // crashes. Behaviour:
            //   - Default ON: events appended to `%LocalAppData%/AccessibleTrader/SecurityEvents/security-events-YYYY-MM-DD.jsonl`.
            //   - ACCESSIBLETRADER_SECURITY_EVENT_DIR=<path> overrides the directory.
            //   - ACCESSIBLETRADER_SECURITY_EVENT_PERSIST=0 (or "false") disables the file sink entirely.
            // The sink degrades gracefully to in-memory-only if the directory
            // can't be created or a write fails (logs the failure via ILogger).
            services.AddSingleton<AccessibleTrader.Core.Services.Security.SecurityEventLog>();
            services.AddSingleton<AccessibleTrader.Sdk.Services.ISecurityEventLog>(sp =>
            {
                var ringBuffer = sp.GetRequiredService<AccessibleTrader.Core.Services.Security.SecurityEventLog>();
                var persistEnv = Environment.GetEnvironmentVariable("ACCESSIBLETRADER_SECURITY_EVENT_PERSIST");
                bool persistEnabled = string.IsNullOrEmpty(persistEnv)
                    || !(persistEnv.Equals("0", StringComparison.Ordinal)
                      || persistEnv.Equals("false", StringComparison.OrdinalIgnoreCase));
                if (!persistEnabled) return ringBuffer;

                string dir = Environment.GetEnvironmentVariable("ACCESSIBLETRADER_SECURITY_EVENT_DIR")
                    ?? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "AccessibleTrader", "SecurityEvents");

                var sinkLogger = sp.GetService<Microsoft.Extensions.Logging.ILogger<AccessibleTrader.Core.Services.Security.SecurityEventFileSink>>();
                return new AccessibleTrader.Core.Services.Security.SecurityEventFileSink(ringBuffer, dir, sinkLogger);
            });
            services.AddSingleton<GlobalInputService>();

            // Configuration, themes, and styling.
            services.AddSingleton<ISettingsManager, SettingsManager>();
            services.AddSingleton<ThemeService>();
            services.AddSingleton<IThemeService>(sp => sp.GetRequiredService<ThemeService>());
            services.AddSingleton<IComponentRoleMapper, ComponentRoleMapper>();
            services.AddSingleton<ISonificationProfileProvider, SonificationProfileProvider>();
            services.AddSingleton<IPaneAssignmentService, PaneAssignmentService>();
            services.AddSingleton<IStylingService, StylingService>();

            // Workspace persistence and startup sequencing.
            services.AddSingleton<ISoundPatchLibrary, SoundPatchLibrary>();
            services.AddSingleton<IWorkspaceLibraryService, WorkspaceLibraryService>();
            services.AddSingleton<IIndicatorPreferencesService, IndicatorPreferencesService>();
            services.AddSingleton<IWorkspaceInitializer, WorkspaceInitializer>();
            services.AddSingleton<IAppStartupService, AppStartupService>();

            return services;
        }

        // ── Data Pipeline ─────────────────────────────────────────────────────

        private static IServiceCollection AddDataPipeline(this IServiceCollection services)
        {
            // Plugin trust policy: load a manifest of approved plugin DLL hashes
            // from the app base directory (ships alongside the binary, generated
            // by the GeneratePluginTrustManifest MSBuild target in the BlazorClient
            // csproj). RequireTrusted defaults to TRUE — unverified DLLs are
            // refused, which is the shipping-default from phase 4 Track A.
            //
            // Escape hatches:
            //   ACCESSIBLETRADER_ALLOW_UNVERIFIED_PLUGINS=1 — disables enforcement
            //       at runtime. Intended for developers hand-dropping a new plugin
            //       into Plugins/ before the manifest has been regenerated. Leaves
            //       a loud warning in the log for every unverified DLL loaded.
            //   ACCESSIBLETRADER_REQUIRE_TRUSTED_PLUGINS=1 — kept for back-compat
            //       with phase-2/3 deploys that set it explicitly. Now redundant
            //       since the default is already enforcing.
            services.AddSingleton(sp =>
            {
                var policy = new PluginTrustPolicy { RequireTrusted = true };
                try
                {
                    var baseDir = AppContext.BaseDirectory;
                    var manifestPath = System.IO.Path.Combine(baseDir, "plugins_trusted.manifest");
                    policy.LoadManifest(manifestPath);
                }
                catch { /* best-effort: a missing/unreadable manifest leaves the policy enforcing an empty allow-list, which refuses every plugin — exactly what we want when the manifest is supposed to be there but isn't */ }

                var envAllow = System.Environment.GetEnvironmentVariable("ACCESSIBLETRADER_ALLOW_UNVERIFIED_PLUGINS");
                if (!string.IsNullOrEmpty(envAllow)
                    && (envAllow.Equals("1", StringComparison.Ordinal)
                     || envAllow.Equals("true", StringComparison.OrdinalIgnoreCase)))
                {
                    policy.RequireTrusted = false;
                }
                return policy;
            });

            // Plugin loader discovers provider assemblies from the Plugins/ directory.
            services.AddSingleton<IPluginLoaderService, PluginLoaderService>();

            // Public-demo policy — always a no-op in the MAUI/desktop heads, but
            // registered so components and MarketOrchestrator can always @inject it.
            services.AddSingleton(new DemoPolicy(isDemo: false));

            // Market / symbol / timeframe selection cascade.
            services.AddSingleton<IMarketOrchestrator, MarketOrchestrator>();
            services.AddSingleton<IProfileService, ProfileService>();

            // Core data services in dependency order.
            services.AddSingleton<IDataService, DataService>();
            services.AddSingleton<IDataManager, DataManager>();
            services.AddSingleton<IOrderBookHistoryService, OrderBookHistoryService>();
            services.AddSingleton<IDataCacheService, DataCacheService>();
            services.AddSingleton<ICacheService, FileCacheService>();
            services.AddSingleton<IResamplerService, ResamplerService>();
            services.AddSingleton<IConnectionManager, ConnectionManager>();
            services.AddSingleton<IApiKeyService, ApiKeyService>();
            services.AddSingleton<IAnalyticsDataResolver, AnalyticsDataResolver>();

            // Historical fetcher and live stream manager are internal building blocks
            // consumed by DataOrchestrator — registered as concrete types for easy mocking.
            services.AddSingleton<HistoricalDataFetcher>();
            services.AddSingleton<LiveStreamManager>();
            services.AddSingleton<BackfillManager>();

            // Orchestration façade — ties historical fetch + live stream together with
            // Polly resilience policies and a DataStateMachine.
            services.AddSingleton<IDataOrchestrator, DataOrchestrator>();

            // Glue layer: wires DataManager events → IndicatorOrchestrator → Store.
            services.AddSingleton<IDataOrchestrationService, DataOrchestrationService>();

            return services;
        }

        // ── Indicator Pipeline ────────────────────────────────────────────────

        private static IServiceCollection AddIndicatorPipeline(this IServiceCollection services)
        {
            // IIndicatorProvider implementations — Core, Skender, Profile, and the native suites.
            services.AddSingleton<IIndicatorProvider, CoreIndicatorProvider>();
            services.AddSingleton<IIndicatorProvider, SkenderBoundedOscillatorProvider>();
            services.AddSingleton<IIndicatorProvider, SkenderZeroCrossProvider>();
            services.AddSingleton<IIndicatorProvider, SkenderBandProvider>();
            services.AddSingleton<IIndicatorProvider, SkenderTrendProvider>();
            services.AddSingleton<IIndicatorProvider, SkenderVolatilityProvider>();
            services.AddSingleton<IIndicatorProvider, SkenderVolumeProvider>();
            services.AddSingleton<IIndicatorProvider, ProfileIndicatorProvider>();
            services.AddSingleton<IIndicatorProvider, CipherBProvider>();
            services.AddSingleton<IIndicatorProvider, CipherAProvider>();
            services.AddSingleton<IIndicatorProvider, CipherSrProvider>();
            services.AddSingleton<IIndicatorProvider, MACloudProvider>();
            services.AddSingleton<IIndicatorProvider, SpiderLinesProvider>();
            services.AddSingleton<IIndicatorProvider, IchimokuProvider>();
            services.AddSingleton<IIndicatorProvider, CipherCProvider>();
            services.AddSingleton<IIndicatorProvider, CipherSProvider>();
            services.AddSingleton<IIndicatorProvider, LoukasCyclesProvider>();
            // Shared cross-series cache backing FundingRate / OpenInterest / FearGreed /
            // CrowdingIndex. Single instance, holds all per-key caches and in-flight tasks
            // so independent indicators that share a source don't double-fetch.
            services.AddSingleton<ICrossSeriesCache, CrossSeriesCache>();
            services.AddSingleton<IIndicatorProvider, FundingRateProvider>();
            services.AddSingleton<IIndicatorProvider, OpenInterestProvider>();
            services.AddSingleton<IIndicatorProvider, FearGreedProvider>();
            services.AddSingleton<IIndicatorProvider, CrowdingIndexProvider>();
            services.AddSingleton<IIndicatorProvider, PulseProvider>();
            services.AddSingleton<IIndicatorProvider, RegimeProvider>();
            services.AddSingleton<IIndicatorProvider, VolRegimeProvider>();
            services.AddSingleton<IIndicatorProvider, CoinMetricsProvider>();
            services.AddSingleton<IIndicatorProvider, TopBottomDetectorProvider>();
            services.AddSingleton<IIndicatorProvider, AnchoredVwapProvider>();
            services.AddSingleton<IIndicatorProvider, HurstExponentProvider>();
            services.AddSingleton<IIndicatorProvider, PivotLevelsProvider>();
            services.AddSingleton<IIndicatorProvider, BtcStrengthProvider>();

            // Runtime custom indicator registry — stores Roslyn/Pine compiled ICustomIndicator instances.
            services.AddSingleton<ICustomIndicatorRegistry, CustomIndicatorRegistry>();

            services.AddSingleton<IIndicatorService, IndicatorService>();
            services.AddSingleton<IIndicatorEngine, IndicatorEngine>();
            services.AddSingleton<IIndicatorStateMapper, IndicatorStateMapper>();
            services.AddSingleton<IIndicatorRegistry, IndicatorRegistry>();
            services.AddSingleton<IIndicatorModelFactory, IndicatorModelFactory>();
            services.AddSingleton<IHeatmapService, HeatmapService>();
            services.AddSingleton<IIndicatorOrchestrator, IndicatorOrchestrator>();
            services.AddSingleton<ISeriesManagementService, SeriesManagementService>();

            return services;
        }

        // ── Rendering & Math ──────────────────────────────────────────────────

        private static IServiceCollection AddRenderingServices(this IServiceCollection services)
        {
            // ChartRenderer is registered as a concrete type because MainPage.xaml.cs
            // resolves it directly (it owns the SKCanvasView and drives the paint callback).
            services.AddSingleton<ChartRenderer>();
            services.AddSingleton<IPaneLayoutService, PaneLayoutService>();

            // Drawing tool calculators — one per DrawingType, resolved as IEnumerable<IDrawingCalculator>
            // by DrawingService. Drop a new calculator into Drawing/Calculators/ and add it here.
            services.AddSingleton<IDrawingCalculator, HorizontalLineCalculator>();
            services.AddSingleton<IDrawingCalculator, VerticalLineCalculator>();
            services.AddSingleton<IDrawingCalculator, TrendLineCalculator>();
            services.AddSingleton<IDrawingCalculator, ChannelCalculator>();
            services.AddSingleton<IDrawingCalculator, FibRetracementCalculator>();
            services.AddSingleton<IDrawingCalculator, TextLabelCalculator>();
            services.AddSingleton<IDrawingCalculator, FibExtensionCalculator>();
            services.AddSingleton<IDrawingCalculator, GannFanCalculator>();
            services.AddSingleton<IDrawingCalculator, RectangleCalculator>();
            services.AddSingleton<IDrawingCalculator, RiskRewardCalculator>();
            services.AddSingleton<IDrawingCalculator, AnchoredVwapCalculator>();
            services.AddSingleton<IDrawingCalculator, MeasureToolCalculator>();
            services.AddSingleton<IDrawingCalculator, GannBoxCalculator>();
            services.AddSingleton<IDrawingCalculator, AndrewsPitchforkCalculator>();
            services.AddSingleton<IDrawingCalculator, AngleFibCalculator>();
            services.AddSingleton<IDrawingService, DrawingService>();

            return services;
        }

        // ── Business Services ─────────────────────────────────────────────────

        private static IServiceCollection AddBusinessServices(this IServiceCollection services)
        {
            // Data export service.
            services.AddSingleton<IDataExportService, DataExportService>();

            // Order execution, trading strategies, and scripting.
            services.AddSingleton<IPaperTradingProvider, PaperTradingProvider>();
            services.AddSingleton<IOrderExecutionService, GeneralOrderService>();
            services.AddSingleton<IStrategyIndicatorCache, StrategyIndicatorCache>();
            services.AddSingleton<IStrategyEngine, StrategyEngine>();
            services.AddSingleton<IStrategyBacktester, StrategyBacktester>();

            // Composite signal-composer pipeline (Session A foundation):
            //   SignalCatalog walks every IIndicatorProvider at startup → flat SignalDescriptor list.
            //   ConditionEvaluator + RiskPlanResolver compute setup state per bar.
            //   ConfigurableStrategyFactory wires those into a ConfigurableStrategy from a StrategySpec.
            //   StrategyLibrary persists user-built specs to strategies.json.
            //   SetupSonifier renders SetupConfirmed/Reconfirmed/Dropped events to bell+speech.
            services.AddSingleton<ISignalCatalog, SignalCatalog>();
            services.AddSingleton<IConditionEvaluator, ConditionEvaluator>();
            services.AddSingleton<IRiskPlanResolver, RiskPlanResolver>();
            services.AddSingleton<IConfigurableStrategyFactory, ConfigurableStrategyFactory>();
            services.AddSingleton<IStrategyLibrary, JsonStrategyLibrary>();
            // Facade: wraps IStrategyLibrary + IConfigurableStrategyFactory + IStrategyEngine
            // so Build-Setup UI code can call a single method per save/delete/add-to-engine
            // operation instead of orchestrating the trio by hand.
            services.AddSingleton<IStrategyLibraryFacade, StrategyLibraryFacade>();
            // StrategyModalCoordinator — wraps Engine + Backtester + WarmupAnalyzer +
            // Library + Factory + Roslyn into one multi-service facade so the modal
            // injects the coordinator once instead of orchestrating six services from
            // every event handler.
            services.AddSingleton<IStrategyModalCoordinator, StrategyModalCoordinator>();
            services.AddSingleton<SetupSonifier>();

            // Alert delivery channels — SMTP + Telegram external dispatchers. The
            // AlertDeliveryService subscribes to AlertFiredEvent and fans out to every
            // configured channel. Config providers pull from ISettingsManager so edits
            // via the Settings modal take effect immediately without a service reload.
            services.AddSingleton<AccessibleTrader.Sdk.Alerts.IAlertChannel>(sp =>
                new AccessibleTrader.Core.Services.Alerts.EmailAlertChannel(
                    () => LoadEmailAlertConfig(sp.GetRequiredService<ISettingsManager>())));
            services.AddSingleton<AccessibleTrader.Sdk.Alerts.IAlertChannel>(sp =>
                new AccessibleTrader.Core.Services.Alerts.TelegramAlertChannel(
                    BuildAlertChannelHttpClient(),
                    () => LoadTelegramAlertConfig(sp.GetRequiredService<ISettingsManager>())));
            services.AddSingleton<AccessibleTrader.Core.Services.Alerts.AlertDeliveryService>();

            // Session B additions:
            services.AddSingleton<IMultiTimeframeDataService, MultiTimeframeDataService>();
            services.AddSingleton<IBacktestWarmupAnalyzer, BacktestWarmupAnalyzer>();

            // Session C — Level providers + aggregator. RiskPlanResolver and ConditionEvaluator
            // optionally inject ILevelService to resolve Phase-4 stop/target sources (BelowSupport,
            // BelowKijun, BelowKumo, NextResistance) and the new leaf operators (PriceRejectsLevel,
            // PriceBreaksLevel). Each provider owns one level source — drop a new provider class
            // into Core/Services/Strategies/Levels/ and add it here to extend the system.
            services.AddSingleton<ILevelProvider, AccessibleTrader.Core.Services.Strategies.Levels.DrawnHorizontalLevelProvider>();
            services.AddSingleton<ILevelProvider, AccessibleTrader.Core.Services.Strategies.Levels.SwingPivotLevelProvider>();
            services.AddSingleton<ILevelProvider, AccessibleTrader.Core.Services.Strategies.Levels.IchimokuLevelProvider>();
            services.AddSingleton<ILevelProvider, AccessibleTrader.Core.Services.Strategies.Levels.CipherSrLevelProvider>();
            services.AddSingleton<ILevelProvider, AccessibleTrader.Core.Services.Strategies.Levels.VolumeProfileLevelProvider>();
            services.AddSingleton<ILevelService, LevelService>();

            // Backtest profile cache: lets the backtester replay VPVR/TPO bins per bar so
            // VolumeProfileLevelProvider doesn't future-leak the workspace's final profile state.
            services.AddSingleton<IBacktestProfileCache, BacktestProfileCache>();

            // Strategy auto-loader: walks IStrategyLibrary at app startup and registers every
            // spec marked IsAutoActivate with the engine. Eagerly resolved via MainLayout.
            services.AddSingleton<StrategyAutoLoader>();

            // DLL strategy plugins (Phase 10-F): scans the host-shipped Strategies/ folder
            // plus the user-writable drop-in folder for DLLs matching
            // AccessibleTrader.Plugins.Strategy.*.dll, loads each through the trust policy +
            // isolated ALC (reusing the trading-provider loader infrastructure), and caches
            // the exported ITradingStrategy templates for the unified strategy registry.
            services.AddSingleton<IStrategyPluginRegistry>(sp =>
                new StrategyPluginRegistry(
                    sp.GetRequiredService<ILogger<StrategyPluginRegistry>>(),
                    sp.GetRequiredService<AccessibleTrader.Core.Services.PluginTrustPolicy>(),
                    AccessibleTrader.Core.Services.Strategies.StrategyPluginDirectories.Default()));
            services.AddSingleton<IStrategyRegistry, StrategyRegistry>();

            services.AddSingleton<ScriptingService>();

            // Script-worker launcher is registered separately so per-platform
            // launchers (e.g. AndroidIsolatedProcessLauncher from
            // Platforms/Android/) can override it via a later registration.
            // Core's default picks the appropriate launcher for Windows
            // AppContainer / macOS sandbox-exec / plain desktop — MAUI
            // Android replaces this binding at startup.
            services.AddSingleton<AccessibleTrader.Core.Services.Scripting.IScriptWorkerLauncher>(_ =>
                RoslynScriptingService.CreateDefaultLauncher());
            services.AddSingleton<IRoslynScriptingService>(sp =>
                new RoslynScriptingService(
                    sp.GetRequiredService<AccessibleTrader.Core.Services.Scripting.IScriptWorkerLauncher>(),
                    RoslynScriptingService.DefaultWorkerPathResolver));

            // Analysis — candle pattern recogniser and indicator context facts.
            services.AddSingleton<CandlePatternThresholds>();
            services.AddSingleton<ISdkCandlePatternAnalyzer, SdkCandlePatternAnalyzer>();
            services.AddSingleton<IIndicatorContextAnalyzer, IndicatorContextAnalyzer>();

            // Bar detail (Ctrl+Shift+D) and alert system.
            services.AddSingleton<IBarDetailService, BarDetailService>();
            services.AddSingleton<IAlertEvaluator, AlertEvaluator>();
            services.AddSingleton<IAlertOrchestrator, AlertOrchestrator>();

            // AI Technical Analyst — LLM providers (Claude priority, then OpenAI, then Ollama)
            services.AddSingleton<ILLMProvider, ClaudeProvider>();
            services.AddSingleton<ILLMProvider, OpenAIProvider>();
            services.AddSingleton<ILLMProvider, OllamaProvider>();
            services.AddSingleton<IAIAnalystService, AIAnalystService>();

            return services;
        }

        // ── Input Routing ─────────────────────────────────────────────────────

        private static IServiceCollection AddInputRouting(this IServiceCollection services)
        {
            // Key normalisation → ShortcutManager resolves → CommandDispatcher routes.
            services.AddSingleton<IKeyNormalizationService, KeyNormalizationService>();
            services.AddSingleton<IShortcutManager, ShortcutManager>();
            services.AddSingleton<IndicatorCrossingEngine>();
            services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
            services.AddSingleton<IInputRouter, InputRouter>();
            services.AddSingleton<IDrawingInteractionManager, DrawingInteractionManager>();
            services.AddSingleton<IChartCommandManager, ChartCommandManager>();

            return services;
        }

        // ── Audio Services ────────────────────────────────────────────────────

        private static IServiceCollection AddAudioServices(this IServiceCollection services)
        {
            // AudioSequencer and SonificationManager are the two public audio authorities:
            //   AudioSequencer  → playback (Space / Shift+Space / Ctrl+Shift+Space).
            //   SonificationManager → navigation (arrow keys / Home / End).
            services.AddSingleton<ISoundPatchRegistry, SoundPatchRegistry>();
            services.AddSingleton<IAudioSequencer, AudioSequencer>();
            services.AddSingleton<ISonificationStrategy, DefaultSonificationStrategy>();
            services.AddSingleton<IPlaybackOrchestrator, PlaybackOrchestrator>();
            services.AddSingleton<INavigationSonifier, NavigationSonifier>();
            services.AddSingleton<ILevelCrossingMonitor, LevelCrossingMonitor>();
            services.AddSingleton<ISonificationManager, SonificationManager>();

            return services;
        }

        // ── Accessibility Services ────────────────────────────────────────────

        private static IServiceCollection AddAccessibilityServices(this IServiceCollection services)
        {
            // Navigation strategies — registered as concrete types so SeriesNavigationRegistry
            // can resolve them by display-type without an abstraction layer.
            services.AddSingleton<PointNavigationStrategy>();
            services.AddSingleton<BinnedNavigationStrategy>();
            services.AddSingleton<ISeriesNavigationRegistry, SeriesNavigationRegistry>();
            services.AddSingleton<IViewportManager, ViewportManager>();
            services.AddSingleton<INavigationEngine, NavigationEngine>();

            // Feedback coordinators — subscribe to StateStream and EventBus on construction
            // and route events to speech and audio outputs.
            services.AddSingleton<ISpeechFormatter, SpeechFormatter>();
            services.AddSingleton<IAccessibilityFeedbackCoordinator, AccessibilityFeedbackCoordinator>();
            services.AddSingleton<IEarconService, EarconService>();
            services.AddSingleton<ISpeechFeedbackRouter, SpeechFeedbackRouter>();
            services.AddSingleton<IAudioFeedbackRouter, AudioFeedbackRouter>();
            services.AddSingleton<INavigationFeedbackManager, NavigationFeedbackManager>();
            services.AddSingleton<IStateFeedbackManager, StateFeedbackManager>();
            services.AddSingleton<IAutoNarrationService, AutoNarrationService>();
            services.AddSingleton<INotificationHub, NotificationHub>();
            services.AddSingleton<IGlobalErrorCoordinator, GlobalErrorCoordinator>();
            services.AddSingleton<IHistoryBufferCoordinator, HistoryBufferCoordinator>();

            // Tactile output for the Dot Pad 2nd-gen refreshable display. The Windows
            // native binding (DotPadSDK-3.0.0.dll via P/Invoke) is the only real
            // implementation today; non-Windows platforms get a null stub so DI still
            // resolves and the driver no-ops cleanly. The coordinator subscribes to
            // RedrawEvent + StateStream in its constructor — eager-resolved in
            // MainLayout to wire those subscriptions before the first chart paint.
            if (OperatingSystem.IsWindows())
            {
                services.AddSingleton<AccessibleTrader.Core.Services.Accessibility.Dotpad.IDotPadNative,
                                       AccessibleTrader.Core.Services.Accessibility.Dotpad.WindowsDotPadNative>();
            }
            else
            {
                services.AddSingleton<AccessibleTrader.Core.Services.Accessibility.Dotpad.IDotPadNative,
                                       AccessibleTrader.Core.Services.Accessibility.Dotpad.NullDotPadNative>();
            }
            services.AddSingleton<ITactileDriver, AccessibleTrader.Core.Services.Accessibility.Dotpad.DotpadTactileDriver>();
            services.AddSingleton<ITactileCanvasCoordinator, TactileCanvasCoordinator>();

            // Journal — captures every TTS phrase, alert, strategy signal, and error
            // for review in the JournalModal (Ctrl+J). Singleton so it can be queried
            // by the modal at any time.
            services.AddSingleton<IJournalService, JournalService>();

            return services;
        }

        // ── Alert delivery helpers ───────────────────────────────────────────
        // Shared HttpClient for the Telegram channel. Capped at 1 MB response
        // (Telegram responses are small JSON) with a 30s timeout so a hung
        // api.telegram.org call doesn't pin the alert-delivery thread.
        private static System.Net.Http.HttpClient BuildAlertChannelHttpClient()
        {
            return new System.Net.Http.HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30),
                MaxResponseContentBufferSize = 1 * 1024 * 1024,
            };
        }

        /// <summary>Loads email alert channel config from settings under the
        /// "alerts.email" key-path. Missing / malformed settings return null so the
        /// channel's IsConfigured check declines to attempt delivery.</summary>
        private static AccessibleTrader.Core.Services.Alerts.EmailAlertChannelConfig? LoadEmailAlertConfig(ISettingsManager settings)
        {
            var host = settings.GetSetting("alerts.email.host")?.ToString();
            var port = settings.GetSetting("alerts.email.port")?.ToObject<int>() ?? 587;
            var useTls = settings.GetSetting("alerts.email.useTls")?.ToObject<bool>() ?? true;
            var username = settings.GetSetting("alerts.email.username")?.ToString();
            var password = settings.GetSetting("alerts.email.password")?.ToString();
            var from = settings.GetSetting("alerts.email.fromAddress")?.ToString();
            var to   = settings.GetSetting("alerts.email.toAddress")?.ToString();
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                return null;
            return new AccessibleTrader.Core.Services.Alerts.EmailAlertChannelConfig
            {
                Host = host, Port = port, UseTls = useTls,
                Username = username, Password = password,
                FromAddress = from, ToAddress = to,
            };
        }

        /// <summary>Loads Telegram alert channel config from settings under the
        /// "alerts.telegram" key-path. Bot token + chat id are required.</summary>
        private static AccessibleTrader.Core.Services.Alerts.TelegramAlertChannelConfig? LoadTelegramAlertConfig(ISettingsManager settings)
        {
            var token = settings.GetSetting("alerts.telegram.botToken")?.ToString();
            var chat  = settings.GetSetting("alerts.telegram.chatId")?.ToString();
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chat))
                return null;
            return new AccessibleTrader.Core.Services.Alerts.TelegramAlertChannelConfig
            {
                BotToken = token,
                ChatId = chat,
            };
        }
    }
}

