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
            services.AddSingleton<IMainThreadService, MauiMainThreadService>();
            services.AddSingleton<IEventBus, EventBus>();

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
            services.AddSingleton<ISecureStorageService, MauiSecureStorageService>();
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
            // Plugin loader discovers provider assemblies from the Plugins/ directory.
            services.AddSingleton<IPluginLoaderService, PluginLoaderService>();

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
            services.AddSingleton<IIndicatorProvider, EmaFillProvider>();
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
            services.AddSingleton<IIndicatorProvider, CoinMetricsProvider>();

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
            services.AddSingleton<SetupSonifier>();

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
            services.AddSingleton<ScriptingService>();
            services.AddSingleton<IRoslynScriptingService, RoslynScriptingService>();

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

            // Journal — captures every TTS phrase, alert, strategy signal, and error
            // for review in the JournalModal (Ctrl+J). Singleton so it can be queried
            // by the modal at any time.
            services.AddSingleton<IJournalService, JournalService>();

            return services;
        }
    }
}

