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
            services.AddSingleton<IAppLogger, WebHostAppLogger>();
            services.AddSingleton<IPlatformPathService, WebHostPathService>();
            services.AddSingleton<IRuntimePlatform, WebHostRuntimePlatform>();
            services.AddSingleton<IMainThreadService, WebHostMainThreadService>();
            services.AddSingleton<IEventBus, EventBus>();

            services.AddSingleton<ICanvasRegionProvider, CanvasRegionProvider>();

            services.AddSingleton<IViewportRangeCalculator, ViewportRangeCalculator>();
            services.AddSingleton<IViewportNavigationService, ViewportNavigationService>();
            services.AddSingleton<IVolumeStateService, VolumeStateService>();
            services.AddSingleton<IWorkspaceStore, WorkspaceStore>();

            services.AddDbContextFactory<AppDbContext>((sp, options) =>
            {
                var pathService = sp.GetRequiredService<IPlatformPathService>();
                options.UseSqlite($"Data Source={Path.Combine(pathService.AppDataDirectory, "trader_local.db")}");
            });

            services.AddSingleton<IInputService, BlazorInputService>();

            // Speech: BlazorSpeechManager handles journaling + the ARIA live
            // region path; WebHostSpeechManager wraps it to ALSO publish a
            // BrowserSpeakRequest event so BrowserSpeechBridge can call
            // window.speechSynthesis. Without the JS-side speech, Orca on
            // Linux+Firefox does not reliably announce live-region updates.
            services.AddSingleton<BlazorSpeechManager>();
            services.AddSingleton<ISpeechManager>(sp =>
                new WebHostSpeechManager(
                    sp.GetRequiredService<BlazorSpeechManager>(),
                    sp.GetRequiredService<IEventBus>(),
                    sp.GetRequiredService<ILogger<WebHostSpeechManager>>()));

            // Browser WebAudio fallback sink (L3-B). Constructed even when a
            // local PCM sink is present so DI is uniform; HasSubscribers will
            // simply stay false and the publish path is never exercised.
            services.AddSingleton<WebHostBrowserAudioSink>();
            services.AddSingleton<IAudioDriver, WebHostAudioDriver>();

            // Single instance backing both interfaces, mirroring the MAUI head's pattern.
            services.AddSingleton<WebHostSecureStorageService>();
            services.AddSingleton<ISecureStorageService>(sp => sp.GetRequiredService<WebHostSecureStorageService>());
            services.AddSingleton<AccessibleTrader.Sdk.Services.IPluginSecureStorage>(sp => sp.GetRequiredService<WebHostSecureStorageService>());

            services.AddSingleton<AccessibleTrader.Core.Services.Diagnostics.CheckoutLatencyTracker>();
            services.AddSingleton<AccessibleTrader.Sdk.Services.IApiKeyCheckout, WebHostApiKeyCheckoutAdapter>();
            services.AddSingleton<AccessibleTrader.Sdk.Services.IPluginHttpClientFactory, WebHostPluginHttpClientFactory>();

            // Security audit log + optional file sink. Identical environment-variable
            // contract as the MAUI head so operators can ignore the host they're on.
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

            services.AddSingleton<ISettingsManager, SettingsManager>();
            services.AddSingleton<ThemeService>();
            services.AddSingleton<IThemeService>(sp => sp.GetRequiredService<ThemeService>());
            services.AddSingleton<IComponentRoleMapper, ComponentRoleMapper>();
            services.AddSingleton<ISonificationProfileProvider, SonificationProfileProvider>();
            services.AddSingleton<IPaneAssignmentService, PaneAssignmentService>();
            services.AddSingleton<IStylingService, StylingService>();

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

            services.AddSingleton<IMarketOrchestrator, MarketOrchestrator>();
            services.AddSingleton<IProfileService, ProfileService>();

            services.AddSingleton<IDataService, DataService>();
            services.AddSingleton<IDataManager, DataManager>();
            services.AddSingleton<IOrderBookHistoryService, OrderBookHistoryService>();
            services.AddSingleton<IDataCacheService, DataCacheService>();
            services.AddSingleton<ICacheService, FileCacheService>();
            services.AddSingleton<IResamplerService, ResamplerService>();
            services.AddSingleton<IConnectionManager, ConnectionManager>();
            services.AddSingleton<IApiKeyService, ApiKeyService>();
            services.AddSingleton<IAnalyticsDataResolver, AnalyticsDataResolver>();

            services.AddSingleton<HistoricalDataFetcher>();
            services.AddSingleton<LiveStreamManager>();
            services.AddSingleton<BackfillManager>();

            services.AddSingleton<IDataOrchestrator, DataOrchestrator>();
            services.AddSingleton<IDataOrchestrationService, DataOrchestrationService>();

            return services;
        }

        // ── Indicator Pipeline ────────────────────────────────────────────────

        private static IServiceCollection AddIndicatorPipeline(this IServiceCollection services)
        {
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

        // ── Rendering ─────────────────────────────────────────────────────────

        private static IServiceCollection AddRenderingServices(this IServiceCollection services)
        {
            services.AddSingleton<ChartRenderer>();
            services.AddSingleton<IPaneLayoutService, PaneLayoutService>();

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
            services.AddSingleton<IDataExportService, DataExportService>();

            services.AddSingleton<IPaperTradingProvider, PaperTradingProvider>();
            services.AddSingleton<IOrderExecutionService, GeneralOrderService>();
            services.AddSingleton<IStrategyIndicatorCache, StrategyIndicatorCache>();
            services.AddSingleton<IStrategyEngine, StrategyEngine>();
            services.AddSingleton<IStrategyBacktester, StrategyBacktester>();

            services.AddSingleton<ISignalCatalog, SignalCatalog>();
            services.AddSingleton<IConditionEvaluator, ConditionEvaluator>();
            services.AddSingleton<IRiskPlanResolver, RiskPlanResolver>();
            services.AddSingleton<IConfigurableStrategyFactory, ConfigurableStrategyFactory>();
            services.AddSingleton<IStrategyLibrary, JsonStrategyLibrary>();
            services.AddSingleton<IStrategyLibraryFacade, StrategyLibraryFacade>();
            services.AddSingleton<IStrategyModalCoordinator, StrategyModalCoordinator>();
            services.AddSingleton<SetupSonifier>();

            services.AddSingleton<AccessibleTrader.Sdk.Alerts.IAlertChannel>(sp =>
                new AccessibleTrader.Core.Services.Alerts.EmailAlertChannel(
                    () => LoadEmailAlertConfig(sp.GetRequiredService<ISettingsManager>())));
            services.AddSingleton<AccessibleTrader.Sdk.Alerts.IAlertChannel>(sp =>
                new AccessibleTrader.Core.Services.Alerts.TelegramAlertChannel(
                    BuildAlertChannelHttpClient(),
                    () => LoadTelegramAlertConfig(sp.GetRequiredService<ISettingsManager>())));
            services.AddSingleton<AccessibleTrader.Core.Services.Alerts.AlertDeliveryService>();

            services.AddSingleton<IMultiTimeframeDataService, MultiTimeframeDataService>();
            services.AddSingleton<IBacktestWarmupAnalyzer, BacktestWarmupAnalyzer>();

            services.AddSingleton<ILevelProvider, AccessibleTrader.Core.Services.Strategies.Levels.DrawnHorizontalLevelProvider>();
            services.AddSingleton<ILevelProvider, AccessibleTrader.Core.Services.Strategies.Levels.SwingPivotLevelProvider>();
            services.AddSingleton<ILevelProvider, AccessibleTrader.Core.Services.Strategies.Levels.IchimokuLevelProvider>();
            services.AddSingleton<ILevelProvider, AccessibleTrader.Core.Services.Strategies.Levels.CipherSrLevelProvider>();
            services.AddSingleton<ILevelProvider, AccessibleTrader.Core.Services.Strategies.Levels.VolumeProfileLevelProvider>();
            services.AddSingleton<ILevelService, LevelService>();
            services.AddSingleton<IBacktestProfileCache, BacktestProfileCache>();
            services.AddSingleton<StrategyAutoLoader>();

            services.AddSingleton<IStrategyPluginRegistry>(sp =>
                new StrategyPluginRegistry(
                    sp.GetRequiredService<ILogger<StrategyPluginRegistry>>(),
                    sp.GetRequiredService<AccessibleTrader.Core.Services.PluginTrustPolicy>(),
                    AccessibleTrader.Core.Services.Strategies.StrategyPluginDirectories.Default()));
            services.AddSingleton<IStrategyRegistry, StrategyRegistry>();

            services.AddSingleton<ScriptingService>();

            // L1: use Core's default launcher selector. On Linux this falls back to
            // DefaultProcessLauncher (no OS-enforced sandbox). The real bwrap
            // launcher arrives in phase L5.
            services.AddSingleton<AccessibleTrader.Core.Services.Scripting.IScriptWorkerLauncher>(_ =>
                RoslynScriptingService.CreateDefaultLauncher());
            services.AddSingleton<IRoslynScriptingService>(sp =>
                new RoslynScriptingService(
                    sp.GetRequiredService<AccessibleTrader.Core.Services.Scripting.IScriptWorkerLauncher>(),
                    RoslynScriptingService.DefaultWorkerPathResolver));

            services.AddSingleton<CandlePatternThresholds>();
            services.AddSingleton<ISdkCandlePatternAnalyzer, SdkCandlePatternAnalyzer>();
            services.AddSingleton<IIndicatorContextAnalyzer, IndicatorContextAnalyzer>();

            services.AddSingleton<IBarDetailService, BarDetailService>();
            services.AddSingleton<IAlertEvaluator, AlertEvaluator>();
            services.AddSingleton<IAlertOrchestrator, AlertOrchestrator>();

            services.AddSingleton<ILLMProvider, ClaudeProvider>();
            services.AddSingleton<ILLMProvider, OpenAIProvider>();
            services.AddSingleton<ILLMProvider, OllamaProvider>();
            services.AddSingleton<IAIAnalystService, AIAnalystService>();

            return services;
        }

        // ── Input Routing ─────────────────────────────────────────────────────

        private static IServiceCollection AddInputRouting(this IServiceCollection services)
        {
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
            services.AddSingleton<PointNavigationStrategy>();
            services.AddSingleton<BinnedNavigationStrategy>();
            services.AddSingleton<ISeriesNavigationRegistry, SeriesNavigationRegistry>();
            services.AddSingleton<IViewportManager, ViewportManager>();
            services.AddSingleton<INavigationEngine, NavigationEngine>();

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

            // Tactile output. Linux + non-Windows hosts use NullDotPadNative
            // (see the docs in IDotPadNative.cs and the SDK research findings
            // for why a real Linux driver isn't possible against the official
            // 1.0.0 SDK — text-only / 20-cell-only, no graphic API).
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

            services.AddSingleton<IJournalService, JournalService>();

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
            var host = settings.GetSetting("alerts.email.host")?.ToString();
            var port = settings.GetSetting("alerts.email.port")?.ToObject<int>() ?? 587;
            var useTls = settings.GetSetting("alerts.email.useTls")?.ToObject<bool>() ?? true;
            var username = settings.GetSetting("alerts.email.username")?.ToString();
            var password = settings.GetSetting("alerts.email.password")?.ToString();
            var from = settings.GetSetting("alerts.email.fromAddress")?.ToString();
            var to = settings.GetSetting("alerts.email.toAddress")?.ToString();
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
            var token = settings.GetSetting("alerts.telegram.botToken")?.ToString();
            var chat = settings.GetSetting("alerts.telegram.chatId")?.ToString();
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
