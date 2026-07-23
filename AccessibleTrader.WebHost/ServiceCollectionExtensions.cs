using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
using AccessibleTrader.BlazorClient.Services; // RCL services — BlazorInputService, BlazorSpeechManager, GlobalInputService, CanvasRegionProvider
using AccessibleTrader.WebHost.Services;       // WebHost shim services — WebHostAppLogger, WebHostPathService, etc.
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Alerts;

namespace AccessibleTrader.WebHost
{
    /// <summary>
    /// WebHost-side mirror of the MAUI head's
    /// <c>AccessibleTrader.BlazorClient.ServiceCollectionExtensions</c>.
    ///
    /// The two are intentionally duplicated rather than extracted to a
    /// shared helper — the MAUI head is not modified by the Linux/web port
    /// (explicit user rule). The only differences below are the eight
    /// platform-shim swaps: <c>Maui*</c> types become <c>WebHost*</c>
    /// types, and the audio driver is the L1 silent stub until phase L3
    /// wires up WebAudio.
    ///
    /// Plugin and ScriptWorker references from the MAUI csproj are NOT
    /// duplicated here for L1 — provider discovery happens at runtime via
    /// <see cref="IPluginLoaderService"/> walking the
    /// <c>Plugins/</c> directory of the host output. When the WebHost is
    /// run from this project's build output that directory is empty, so
    /// zero providers register. That's intentional: L1 only proves chrome,
    /// keyboard nav, and DI composition.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAccessibleTraderWebHostServices(this IServiceCollection services)
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
            services.AddScoped<IAppLogger, WebHostAppLogger>();
            services.AddSingleton<IPlatformPathService, WebHostPathService>();
            services.AddSingleton<IRuntimePlatform, WebHostRuntimePlatform>();
            services.AddSingleton<IMainThreadService, WebHostMainThreadService>();
            services.AddScoped<IEventBus, EventBus>();

            services.AddScoped<ICanvasRegionProvider, CanvasRegionProvider>();

            services.AddScoped<IViewportRangeCalculator, ViewportRangeCalculator>();
            services.AddScoped<IViewportNavigationService, ViewportNavigationService>();
            services.AddScoped<IVolumeStateService, VolumeStateService>();
            services.AddScoped<IWorkspaceStore, WorkspaceStore>();

            // Shared OHLCV cache DB — public market data, one DB for everyone. Resolve the
            // path from a fixed shared location (not the per-user IPlatformPathService) so
            // this factory stays a Singleton even when hosted accounts route
            // IPlatformPathService per-circuit. Behaviour is unchanged when accounts are off
            // (same path the WebHostPathService used).
            var cacheDbDir = new WebHostPathService().AppDataDirectory;
            services.AddDbContextFactory<AppDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(cacheDbDir, "trader_local.db")}"));

            services.AddScoped<IInputService, BlazorInputService>();

            // Speech: BlazorSpeechManager handles journaling + the ARIA live
            // region path; WebHostSpeechManager wraps it to ALSO publish a
            // BrowserSpeakRequest event so BrowserSpeechBridge can call
            // window.speechSynthesis. Without the JS-side speech, Orca on
            // Linux+Firefox does not reliably announce live-region updates.
            services.AddScoped<BlazorSpeechManager>();
            services.AddScoped<ISpeechManager>(sp =>
                new WebHostSpeechManager(
                    sp.GetRequiredService<BlazorSpeechManager>(),
                    sp.GetRequiredService<IEventBus>(),
                    sp.GetRequiredService<ILogger<WebHostSpeechManager>>()));
            // Same instance exposed as the optional speech-output capability so the
            // first-visit prompt + Settings control can gate browser TTS vs the
            // ARIA live region (the hosted double-speech fix).
            services.AddScoped<IBrowserSpeechOutput>(sp =>
                (WebHostSpeechManager)sp.GetRequiredService<ISpeechManager>());

            // Browser WebAudio fallback sink (L3-B). Constructed even when a
            // local PCM sink is present so DI is uniform; HasSubscribers will
            // simply stay false and the publish path is never exercised.
            services.AddScoped<WebHostBrowserAudioSink>();
            services.AddScoped<IAudioDriver, WebHostAudioDriver>();

            // Single instance backing both interfaces, mirroring the MAUI head's pattern.
            services.AddSingleton<WebHostSecureStorageService>();
            services.AddSingleton<ISecureStorageService>(sp => sp.GetRequiredService<WebHostSecureStorageService>());
            services.AddScoped<AccessibleTrader.Sdk.Services.IPluginSecureStorage>(sp => sp.GetRequiredService<WebHostSecureStorageService>());

            services.AddScoped<AccessibleTrader.Core.Services.Diagnostics.CheckoutLatencyTracker>();
            services.AddScoped<AccessibleTrader.Sdk.Services.IApiKeyCheckout, WebHostApiKeyCheckoutAdapter>();
            services.AddScoped<AccessibleTrader.Sdk.Services.IPluginHttpClientFactory, WebHostPluginHttpClientFactory>();

            // Security audit log + optional file sink. Identical environment-variable
            // contract as the MAUI head so operators can ignore the host they're on.
            services.AddScoped<AccessibleTrader.Core.Services.Security.SecurityEventLog>();
            services.AddScoped<AccessibleTrader.Sdk.Services.ISecurityEventLog>(sp =>
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
            services.AddScoped<GlobalInputService>();
            services.AddScoped<ChartHoverTracker>();

            services.AddScoped<ISettingsManager, SettingsManager>();
            services.AddScoped<IAppSettings, AppSettings>(); // typed facade (debt item 3a)
            services.AddScoped<IPreferencePersistenceService, PreferencePersistenceService>(); // store prefs → settings.json (3b)
            services.AddScoped<Core.Services.Workspace.IMarketFeeds, Core.Services.Workspace.MarketFeeds>(); // data-access seam (debt item 7)
            services.AddScoped<ThemeService>();
            services.AddScoped<IThemeService>(sp => sp.GetRequiredService<ThemeService>());
            services.AddScoped<IComponentRoleMapper, ComponentRoleMapper>();
            services.AddScoped<ISonificationProfileProvider, SonificationProfileProvider>();
            services.AddScoped<IPaneAssignmentService, PaneAssignmentService>();
            services.AddScoped<IStylingService, StylingService>();

            services.AddScoped<ISoundPatchLibrary, SoundPatchLibrary>();
            // Wavetable/sample imports are process-global (static WavetableBank), so a
            // singleton on both hosts; the ctor loads persisted imports at startup.
            // Scoped (not Singleton): WavetableLibraryService depends on the per-user Scoped
            // IPlatformPathService, so a Singleton captured it and the accounts-path ValidateOnBuild
            // rejected the graph at startup (crash-loop). It's still effectively process-global —
            // the wavetable data lives in a static WavetableBank shared across instances — and it's
            // only ever resolved via GetService in per-circuit startup, so Scoped is safe (no
            // singleton constructor-injects it). Ideal follow-up: keep Singleton but give it a
            // non-scoped app-global path source.
            services.AddScoped<Core.Services.Audio.IWavetableLibrary, Core.Services.Audio.WavetableLibraryService>();
            services.AddScoped<IWorkspaceLibraryService, WorkspaceLibraryService>();
            services.AddScoped<IIndicatorPreferencesService, IndicatorPreferencesService>();
            services.AddScoped<IWorkspaceInitializer, WorkspaceInitializer>();
            services.AddScoped<IAppStartupService, AppStartupService>();

            return services;
        }

        // ── Data Pipeline ─────────────────────────────────────────────────────

        private static IServiceCollection AddDataPipeline(this IServiceCollection services)
        {
            services.AddSingleton(sp =>
            {
                var policy = new PluginTrustPolicy { RequireTrusted = true };
                try
                {
                    var baseDir = AppContext.BaseDirectory;
                    var manifestPath = Path.Combine(baseDir, "plugins_trusted.manifest");
                    policy.LoadManifest(manifestPath);
                }
                catch { /* missing manifest leaves an empty allow-list — refuses every plugin, which is the desired enforcing default */ }

                var envAllow = Environment.GetEnvironmentVariable("ACCESSIBLETRADER_ALLOW_UNVERIFIED_PLUGINS");
                if (!string.IsNullOrEmpty(envAllow)
                    && (envAllow.Equals("1", StringComparison.Ordinal)
                     || envAllow.Equals("true", StringComparison.OrdinalIgnoreCase)))
                {
                    policy.RequireTrusted = false;
                }
                return policy;
            });

            services.AddSingleton<IPluginLoaderService, PluginLoaderService>();

            services.AddScoped<IMarketOrchestrator, MarketOrchestrator>();
            services.AddScoped<IProfileService, ProfileService>();

            services.AddScoped<IDataService, DataService>();
            services.AddScoped<AccessibleTrader.Core.Services.Feeds.IMarketFeedHub, AccessibleTrader.Core.Services.Feeds.MarketFeedHub>();
            services.AddScoped<IDataManager, DataManager>();
            services.AddScoped<IOrderBookHistoryService, OrderBookHistoryService>();
            services.AddSingleton<IDataCacheService, DataCacheService>();
            services.AddSingleton<ICacheService, FileCacheService>();
            services.AddSingleton<IResamplerService, ResamplerService>();
            services.AddScoped<IConnectionManager, ConnectionManager>();
            services.AddSingleton<IApiKeyService, ApiKeyService>();
            services.AddScoped<IAnalyticsDataResolver, AnalyticsDataResolver>();

            services.AddScoped<HistoricalDataFetcher>();
            services.AddScoped<LiveStreamManager>();
            services.AddScoped<BackfillManager>();

            services.AddScoped<IDataOrchestrator, DataOrchestrator>();
            services.AddScoped<IDataOrchestrationService, DataOrchestrationService>();

            // My Data: user-imported CSV datasets. Per-circuit store (per-user
            // directory on the hosted terminal via the user-scoped path service);
            // the provider registers into DataService at startup via the
            // IBuiltInDataProvider hook. NOT registered in demo mode — the shared
            // public demo has no per-visitor persistence and no import UI.
            services.AddScoped<AccessibleTrader.Core.Services.MyData.IMyDataStore,
                AccessibleTrader.Core.Services.MyData.MyDataStore>();
            // Demo mode needs no special-case here: DemoPolicy.FilterMarkets hides
            // the MyData market (allow-list), and the import UI is demo-gated.
            services.AddScoped<AccessibleTrader.Core.Services.MyData.IBuiltInDataProvider,
                AccessibleTrader.Core.Services.MyData.MyDataProvider>();

            return services;
        }

        // ── Indicator Pipeline ────────────────────────────────────────────────

        private static IServiceCollection AddIndicatorPipeline(this IServiceCollection services)
        {
            services.AddScoped<IIndicatorProvider, CoreIndicatorProvider>();
            services.AddScoped<IIndicatorProvider, MyDataEventsProvider>();
            services.AddScoped<IIndicatorProvider, MyDataSeriesProvider>();
            services.AddScoped<IIndicatorProvider, SymbolCompareProvider>();
            services.AddScoped<IIndicatorProvider, SkenderBoundedOscillatorProvider>();
            services.AddScoped<IIndicatorProvider, SkenderZeroCrossProvider>();
            services.AddScoped<IIndicatorProvider, SkenderBandProvider>();
            services.AddScoped<IIndicatorProvider, SkenderTrendProvider>();
            services.AddScoped<IIndicatorProvider, SkenderVolatilityProvider>();
            services.AddScoped<IIndicatorProvider, SkenderVolumeProvider>();
            services.AddScoped<IIndicatorProvider, ProfileIndicatorProvider>();
            services.AddScoped<IIndicatorProvider, CipherBProvider>();
            services.AddScoped<IIndicatorProvider, CipherAProvider>();
            services.AddScoped<IIndicatorProvider, CipherSrProvider>();
            services.AddScoped<IIndicatorProvider, MACloudProvider>();
            services.AddScoped<IIndicatorProvider, SpiderLinesProvider>();
            services.AddScoped<IIndicatorProvider, IchimokuProvider>();
            services.AddScoped<IIndicatorProvider, CipherCProvider>();
            services.AddScoped<IIndicatorProvider, CipherSProvider>();
            services.AddScoped<IIndicatorProvider, LoukasCyclesProvider>();
            services.AddScoped<ICrossSeriesCache, CrossSeriesCache>();
            services.AddScoped<IIndicatorProvider, FundingRateProvider>();
            services.AddScoped<IIndicatorProvider, CotPositioningProvider>();
            services.AddScoped<IIndicatorProvider, OpenInterestProvider>();
            services.AddScoped<IIndicatorProvider, FearGreedProvider>();
            services.AddScoped<IIndicatorProvider, CrowdingIndexProvider>();
            services.AddScoped<IIndicatorProvider, PulseProvider>();
            services.AddScoped<IIndicatorProvider, RegimeProvider>();
            services.AddScoped<IIndicatorProvider, VolRegimeProvider>();
            services.AddScoped<IIndicatorProvider, CoinMetricsProvider>();
            services.AddScoped<IIndicatorProvider, TopBottomDetectorProvider>();
            services.AddScoped<IIndicatorProvider, AnchoredVwapProvider>();
            services.AddScoped<IIndicatorProvider, HurstExponentProvider>();
            services.AddScoped<IIndicatorProvider, PivotLevelsProvider>();
            services.AddScoped<IIndicatorProvider, BtcStrengthProvider>();

            services.AddScoped<ICustomIndicatorRegistry, CustomIndicatorRegistry>();

            services.AddScoped<IIndicatorService, IndicatorService>();
            services.AddScoped<IIndicatorEngine, IndicatorEngine>();
            services.AddScoped<IIndicatorStateMapper, IndicatorStateMapper>();
            services.AddScoped<IIndicatorRegistry, IndicatorRegistry>();
            services.AddScoped<IIndicatorModelFactory, IndicatorModelFactory>();
            services.AddScoped<IHeatmapService, HeatmapService>();
            services.AddScoped<IIndicatorOrchestrator, IndicatorOrchestrator>();
            services.AddScoped<ISeriesManagementService, SeriesManagementService>();

            return services;
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        private static IServiceCollection AddRenderingServices(this IServiceCollection services)
        {
            services.AddScoped<ChartRenderer>();
            services.AddScoped<IPaneLayoutService, PaneLayoutService>();

            services.AddScoped<IDrawingCalculator, HorizontalLineCalculator>();
            services.AddScoped<IDrawingCalculator, VerticalLineCalculator>();
            services.AddScoped<IDrawingCalculator, TrendLineCalculator>();
            services.AddScoped<IDrawingCalculator, ChannelCalculator>();
            services.AddScoped<IDrawingCalculator, FibRetracementCalculator>();
            services.AddScoped<IDrawingCalculator, TextLabelCalculator>();
            services.AddScoped<IDrawingCalculator, FibExtensionCalculator>();
            services.AddScoped<IDrawingCalculator, GannFanCalculator>();
            services.AddScoped<IDrawingCalculator, RectangleCalculator>();
            services.AddScoped<IDrawingCalculator, RiskRewardCalculator>();
            services.AddScoped<IDrawingCalculator, AnchoredVwapCalculator>();
            services.AddScoped<IDrawingCalculator, MeasureToolCalculator>();
            services.AddScoped<IDrawingCalculator, GannBoxCalculator>();
            services.AddScoped<IDrawingCalculator, AndrewsPitchforkCalculator>();
            services.AddScoped<IDrawingCalculator, AngleFibCalculator>();
            services.AddScoped<IDrawingService, DrawingService>();

            return services;
        }

        // ── Business Services ─────────────────────────────────────────────────

        private static IServiceCollection AddBusinessServices(this IServiceCollection services)
        {
            services.AddScoped<IDataExportService, DataExportService>();

            services.AddScoped<IPaperTradingProvider, PaperTradingProvider>();
            services.AddScoped<IOrderExecutionService, GeneralOrderService>();
            services.AddScoped<IStrategyIndicatorCache, StrategyIndicatorCache>();
            services.AddScoped<IStrategyEngine, StrategyEngine>();
            services.AddScoped<IStrategyBacktester, StrategyBacktester>();

            services.AddScoped<ISignalCatalog, SignalCatalog>();
            services.AddScoped<IConditionEvaluator, ConditionEvaluator>();
            services.AddScoped<ILabRunner, LabRunner>(); // in-app Lab tab (walk-windows + battery comparison)
            services.AddScoped<IRiskPlanResolver, RiskPlanResolver>();
            services.AddScoped<IConfigurableStrategyFactory, ConfigurableStrategyFactory>();
            services.AddScoped<IStrategyLibrary, JsonStrategyLibrary>();
            services.AddScoped<IStrategyLibraryFacade, StrategyLibraryFacade>();
            services.AddScoped<IStrategyModalCoordinator, StrategyModalCoordinator>();
            services.AddScoped<SetupSonifier>();

            services.AddScoped<AccessibleTrader.Sdk.Alerts.IAlertChannel>(sp =>
                new AccessibleTrader.Core.Services.Alerts.EmailAlertChannel(
                    () => LoadEmailAlertConfig(sp.GetRequiredService<ISettingsManager>())));
            services.AddScoped<AccessibleTrader.Sdk.Alerts.IAlertChannel>(sp =>
                new AccessibleTrader.Core.Services.Alerts.TelegramAlertChannel(
                    BuildAlertChannelHttpClient(),
                    () => LoadTelegramAlertConfig(sp.GetRequiredService<ISettingsManager>())));
            services.AddScoped<AccessibleTrader.Sdk.Alerts.IAlertChannel>(sp =>
                new AccessibleTrader.Core.Services.Alerts.WebhookAlertChannel(
                    BuildAlertChannelHttpClient(),
                    () => LoadWebhookAlertConfig(sp.GetRequiredService<ISettingsManager>()),
                    // Wire diagnostics so missing-target and delivery failures reach the
                    // log and (when a circuit is open) the user's speech — null before.
                    sp.GetService<Microsoft.Extensions.Logging.ILogger<AccessibleTrader.Core.Services.Alerts.WebhookAlertChannel>>(),
                    sp.GetRequiredService<AccessibleTrader.Core.Services.IEventBus>()));
            services.AddScoped<AccessibleTrader.Core.Services.Alerts.AlertDeliveryService>();
            // Part C — strategy-setup → AlertFiredEvent bridge (default-off; see SetupAlertBridge).
            services.AddScoped<AccessibleTrader.Core.Services.Alerts.SetupAlertBridge>();

            services.AddScoped<IMultiTimeframeDataService, MultiTimeframeDataService>();
            services.AddScoped<IBacktestWarmupAnalyzer, BacktestWarmupAnalyzer>();

            services.AddScoped<ILevelProvider, AccessibleTrader.Core.Services.Strategies.Levels.DrawnHorizontalLevelProvider>();
            services.AddScoped<ILevelProvider, AccessibleTrader.Core.Services.Strategies.Levels.SwingPivotLevelProvider>();
            services.AddScoped<ILevelProvider, AccessibleTrader.Core.Services.Strategies.Levels.IchimokuLevelProvider>();
            services.AddScoped<ILevelProvider, AccessibleTrader.Core.Services.Strategies.Levels.CipherSrLevelProvider>();
            services.AddScoped<ILevelProvider, AccessibleTrader.Core.Services.Strategies.Levels.VolumeProfileLevelProvider>();
            services.AddScoped<ILevelService, LevelService>();
            services.AddScoped<IBacktestProfileCache, BacktestProfileCache>();
            services.AddScoped<StrategyAutoLoader>();

            services.AddScoped<IStrategyPluginRegistry>(sp =>
                new StrategyPluginRegistry(
                    sp.GetRequiredService<ILogger<StrategyPluginRegistry>>(),
                    sp.GetRequiredService<AccessibleTrader.Core.Services.PluginTrustPolicy>(),
                    AccessibleTrader.Core.Services.Strategies.StrategyPluginDirectories.Default()));
            services.AddScoped<IStrategyRegistry, StrategyRegistry>();

            services.AddScoped<ScriptingService>();

            // Use Core's default launcher selector. On Linux this is the
            // LinuxBwrapLauncher (L5), which sandboxes the worker under bubblewrap
            // when bwrap is installed and falls back to an unsandboxed
            // DefaultProcessLauncher only if it isn't.
            services.AddScoped<AccessibleTrader.Core.Services.Scripting.IScriptWorkerLauncher>(_ =>
                RoslynScriptingService.CreateDefaultLauncher());
            services.AddScoped<IRoslynScriptingService>(sp =>
                new RoslynScriptingService(
                    sp.GetRequiredService<AccessibleTrader.Core.Services.Scripting.IScriptWorkerLauncher>(),
                    RoslynScriptingService.DefaultWorkerPathResolver));

            services.AddScoped<CandlePatternThresholds>();
            services.AddScoped<ISdkCandlePatternAnalyzer, SdkCandlePatternAnalyzer>();
            services.AddScoped<IIndicatorContextAnalyzer, IndicatorContextAnalyzer>();

            services.AddScoped<IBarDetailService, BarDetailService>();
            services.AddScoped<IAlertEvaluator, AlertEvaluator>();
            services.AddScoped<IAlertOrchestrator, AlertOrchestrator>();
            services.AddScoped<AccessibleTrader.Core.Services.Workspace.ISessionAutosaveService,
                AccessibleTrader.Core.Services.Workspace.SessionAutosaveService>();
            services.AddScoped<AccessibleTrader.Core.Services.Feeds.IBackgroundTabFeedService,
                AccessibleTrader.Core.Services.Feeds.BackgroundTabFeedService>();
            services.AddScoped<AccessibleTrader.Core.Services.Workspace.IBackgroundMonitoringService,
                               AccessibleTrader.Core.Services.Workspace.BackgroundMonitoringService>();

            services.AddScoped<ILLMProvider, ClaudeProvider>();
            services.AddScoped<ILLMProvider, OpenAIProvider>();
            services.AddScoped<ILLMProvider, OllamaProvider>();
            services.AddScoped<IAIAnalystService, AIAnalystService>();

            return services;
        }

        // ── Input Routing ─────────────────────────────────────────────────────

        private static IServiceCollection AddInputRouting(this IServiceCollection services)
        {
            services.AddScoped<IKeyNormalizationService, KeyNormalizationService>();
            services.AddScoped<IShortcutManager, ShortcutManager>();
            services.AddScoped<IndicatorCrossingEngine>();
            services.AddScoped<ICommandDispatcher, CommandDispatcher>();
            services.AddScoped<IInputRouter, InputRouter>();
            services.AddScoped<IDrawingInteractionManager, DrawingInteractionManager>();
            services.AddScoped<IChartCommandManager, ChartCommandManager>();

            return services;
        }

        // ── Audio Services ────────────────────────────────────────────────────

        private static IServiceCollection AddAudioServices(this IServiceCollection services)
        {
            services.AddScoped<ISoundPatchRegistry, SoundPatchRegistry>();
            services.AddScoped<IAudioSequencer, AudioSequencer>();
            services.AddScoped<ISonificationStrategy, DefaultSonificationStrategy>();
            services.AddScoped<IPlaybackOrchestrator, PlaybackOrchestrator>();
            services.AddScoped<INavigationSonifier, NavigationSonifier>();
            services.AddScoped<ILevelCrossingMonitor, LevelCrossingMonitor>();
            services.AddScoped<ISonificationManager, SonificationManager>();

            return services;
        }

        // ── Accessibility Services ────────────────────────────────────────────

        private static IServiceCollection AddAccessibilityServices(this IServiceCollection services)
        {
            services.AddScoped<PointNavigationStrategy>();
            services.AddScoped<BinnedNavigationStrategy>();
            services.AddScoped<ISeriesNavigationRegistry, SeriesNavigationRegistry>();
            services.AddScoped<IViewportManager, ViewportManager>();
            services.AddScoped<INavigationEngine, NavigationEngine>();

            services.AddScoped<ISpeechFormatter, SpeechFormatter>();
            services.AddScoped<IAccessibilityFeedbackCoordinator, AccessibilityFeedbackCoordinator>();
            services.AddScoped<IEarconService, EarconService>();
            services.AddScoped<ISpeechFeedbackRouter, SpeechFeedbackRouter>();
            services.AddScoped<IAudioFeedbackRouter, AudioFeedbackRouter>();
            services.AddScoped<INavigationFeedbackManager, NavigationFeedbackManager>();
            services.AddScoped<IStateFeedbackManager, StateFeedbackManager>();
            services.AddScoped<IAutoNarrationService, AutoNarrationService>();
            services.AddScoped<INotificationHub, NotificationHub>();
            services.AddScoped<IGlobalErrorCoordinator, GlobalErrorCoordinator>();
            services.AddScoped<IHistoryBufferCoordinator, HistoryBufferCoordinator>();
            services.AddScoped<ITradingReconciliationCoordinator, TradingReconciliationCoordinator>();

            // Tactile output. Linux + non-Windows hosts use NullDotPadNative
            // (see the docs in IDotPadNative.cs and the SDK research findings
            // for why a real Linux driver isn't possible against the official
            // 1.0.0 SDK — text-only / 20-cell-only, no graphic API).
            if (OperatingSystem.IsWindows())
            {
                services.AddScoped<AccessibleTrader.Core.Services.Accessibility.Dotpad.IDotPadNative,
                                       AccessibleTrader.Core.Services.Accessibility.Dotpad.WindowsDotPadNative>();
            }
            else
            {
                services.AddScoped<AccessibleTrader.Core.Services.Accessibility.Dotpad.IDotPadNative,
                                       AccessibleTrader.Core.Services.Accessibility.Dotpad.NullDotPadNative>();
            }
            services.AddScoped<ITactileDriver, AccessibleTrader.Core.Services.Accessibility.Dotpad.DotpadTactileDriver>();
            services.AddScoped<ITactileCanvasCoordinator, TactileCanvasCoordinator>();

            services.AddScoped<IJournalService, JournalService>();

            return services;
        }

        // ── Alert delivery helpers (verbatim copies from the MAUI head) ──

        private static System.Net.Http.HttpClient BuildAlertChannelHttpClient()
        {
            return new System.Net.Http.HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30),
                MaxResponseContentBufferSize = 1 * 1024 * 1024,
            };
        }

        private static AccessibleTrader.Core.Services.Alerts.EmailAlertChannelConfig? LoadEmailAlertConfig(ISettingsManager settings)
        {
            var host = settings.GetSetting(SettingsKeys.EmailHost)?.ToString();
            var port = settings.GetSetting(SettingsKeys.EmailPort)?.ToObject<int>() ?? 587;
            var useTls = settings.GetSetting(SettingsKeys.EmailUseTls)?.ToObject<bool>() ?? true;
            var username = settings.GetSetting(SettingsKeys.EmailUsername)?.ToString();
            var password = settings.GetSetting(SettingsKeys.EmailPassword)?.ToString();
            var from = settings.GetSetting(SettingsKeys.EmailFromAddress)?.ToString();
            var to = settings.GetSetting(SettingsKeys.EmailToAddress)?.ToString();
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                return null;
            return new AccessibleTrader.Core.Services.Alerts.EmailAlertChannelConfig
            {
                Host = host, Port = port, UseTls = useTls,
                Username = username, Password = password,
                FromAddress = from, ToAddress = to,
            };
        }

        private static AccessibleTrader.Core.Services.Alerts.TelegramAlertChannelConfig? LoadTelegramAlertConfig(ISettingsManager settings)
        {
            var token = settings.GetSetting(SettingsKeys.TelegramBotToken)?.ToString();
            var chat = settings.GetSetting(SettingsKeys.TelegramChatId)?.ToString();
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chat))
                return null;
            return new AccessibleTrader.Core.Services.Alerts.TelegramAlertChannelConfig
            {
                BotToken = token,
                ChatId = chat,
            };
        }

        private static AccessibleTrader.Core.Services.Alerts.WebhookAlertChannelConfig? LoadWebhookAlertConfig(ISettingsManager settings)
            => AccessibleTrader.Core.Services.Alerts.WebhookAlertConfigLoader.Load(settings);
    }
}
