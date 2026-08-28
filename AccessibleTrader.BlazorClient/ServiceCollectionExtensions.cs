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
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Screening;
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

            // SQLite local cache — AppDbContext is used by ApiKeyService.
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

                // IPlatformPathService, not GetFolderPath: the latter returns an empty string on
                // Unix when the target does not exist, which would write the audit log into the
                // process's working directory instead of app data. Same path on desktop as before.
                string dir = Environment.GetEnvironmentVariable("ACCESSIBLETRADER_SECURITY_EVENT_DIR")
                    ?? Path.Combine(
                        sp.GetRequiredService<IPlatformPathService>().AppDataDirectory,
                        "SecurityEvents");

                var sinkLogger = sp.GetService<Microsoft.Extensions.Logging.ILogger<AccessibleTrader.Core.Services.Security.SecurityEventFileSink>>();
                return new AccessibleTrader.Core.Services.Security.SecurityEventFileSink(ringBuffer, dir, sinkLogger);
            });
            services.AddSingleton<GlobalInputService>();
            services.AddSingleton<ChartHoverTracker>();

            // Configuration, themes, and styling.
            services.AddSingleton<ISettingsManager, SettingsManager>();
            services.AddSingleton<IAppSettings, AppSettings>(); // typed facade (debt item 3a)
            services.AddSingleton<IPreferencePersistenceService, PreferencePersistenceService>(); // store prefs → settings.json (3b)
            services.AddSingleton<Core.Services.Workspace.IMarketFeeds, Core.Services.Workspace.MarketFeeds>(); // data-access seam (debt item 7)
            services.AddSingleton<ThemeService>();
            services.AddSingleton<IThemeService>(sp => sp.GetRequiredService<ThemeService>());
            services.AddSingleton<IComponentRoleMapper, ComponentRoleMapper>();
            services.AddSingleton<ISonificationProfileProvider, SonificationProfileProvider>();
            services.AddSingleton<IPaneAssignmentService, PaneAssignmentService>();
            services.AddSingleton<IStylingService, StylingService>();

            // Workspace persistence and startup sequencing.
            services.AddSingleton<ISoundPatchLibrary, SoundPatchLibrary>();
            // Wavetable/sample imports are process-global (static WavetableBank), so a
            // singleton on both hosts; the ctor loads persisted imports at startup.
            services.AddSingleton<Core.Services.Audio.IWavetableLibrary, Core.Services.Audio.WavetableLibraryService>();
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
            // The withdrawal release gate, injected rather than read off a static so the markup
            // that depends on it can be rendered both ways in tests. Shipped == closed for 2.4.0.
            services.AddSingleton(AccessibleTrader.Core.Services.Trading.WithdrawalReleasePolicy.Shipped);

            // Market / symbol / timeframe selection cascade.
            services.AddSingleton<IMarketOrchestrator, MarketOrchestrator>();
            services.AddSingleton<IProfileService, ProfileService>();

            // Core data services in dependency order.
            services.AddSingleton<IDataService, DataService>();
            services.AddSingleton<AccessibleTrader.Core.Services.Feeds.IMarketFeedHub, AccessibleTrader.Core.Services.Feeds.MarketFeedHub>();
            services.AddSingleton<IDataManager, DataManager>();
            services.AddSingleton<IOrderBookHistoryService, OrderBookHistoryService>();
            services.AddSingleton<ICacheService, FileCacheService>();
            // Historical OHLCV store (see OhlcvStore): shared public market data, one writer.
            services.AddSingleton<IOhlcvStore, OhlcvStore>();
            services.AddSingleton<IResamplerService, ResamplerService>();
            services.AddSingleton<IApiKeyService, ApiKeyService>();

            // Historical fetcher and live stream manager are internal building blocks
            // consumed by DataOrchestrator — registered as concrete types for easy mocking.
            services.AddSingleton<HistoricalDataFetcher>();
            // Lazy hub + store so a reconnect can gap-fill the outage and record feed
            // freshness. Func<>, not the interface: MarketFeedHub -> IDataOrchestrator ->
            // LiveStreamManager -> IMarketFeedHub is a cycle, and deferring the lookup to
            // first use is what breaks it.
            services.AddSingleton<LiveStreamManager>(sp => new LiveStreamManager(
                sp.GetRequiredService<IDataService>(),
                sp.GetRequiredService<IGlobalErrorCoordinator>(),
                sp.GetRequiredService<ILogger<LiveStreamManager>>(),
                () => sp.GetService<AccessibleTrader.Core.Services.Feeds.IMarketFeedHub>(),
                sp.GetService<IWorkspaceStore>()));

            // Orchestration façade — ties historical fetch + live stream together with
            // Polly resilience policies and a DataStateMachine.
            services.AddSingleton<IDataOrchestrator, DataOrchestrator>();

            // Glue layer: wires DataManager events → IndicatorOrchestrator → Store.
            services.AddSingleton<IDataOrchestrationService, DataOrchestrationService>();

            // My Data: user-imported CSV datasets (desktop: singleton, app-data dir).
            services.AddSingleton<AccessibleTrader.Core.Services.MyData.IMyDataStore,
                AccessibleTrader.Core.Services.MyData.MyDataStore>();
            services.AddSingleton<AccessibleTrader.Core.Services.MyData.IBuiltInDataProvider,
                AccessibleTrader.Core.Services.MyData.MyDataProvider>();

            return services;
        }

        // ── Indicator Pipeline ────────────────────────────────────────────────

        private static IServiceCollection AddIndicatorPipeline(this IServiceCollection services)
        {
            // IIndicatorProvider implementations — Core, Skender, Profile, and the native suites.
            services.AddSingleton<IIndicatorProvider, CoreIndicatorProvider>();
            services.AddSingleton<IIndicatorProvider, MyDataEventsProvider>();
            services.AddSingleton<IIndicatorProvider, MyDataSeriesProvider>();
            services.AddSingleton<IIndicatorProvider, SymbolCompareProvider>();
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
            services.AddSingleton<IIndicatorProvider, CotPositioningProvider>();
            services.AddSingleton<IIndicatorProvider, OpenInterestProvider>();
            services.AddSingleton<IIndicatorProvider, FearGreedProvider>();
            services.AddSingleton<IIndicatorProvider, CrowdingIndexProvider>();
            services.AddSingleton<IIndicatorProvider, PulseProvider>();
            services.AddSingleton<IIndicatorProvider, RegimeProvider>();
            services.AddSingleton<IIndicatorProvider, VolRegimeProvider>();
            services.AddSingleton<IIndicatorProvider, SwingStructureProvider>();
            services.AddSingleton<IIndicatorProvider, ValueDeviationProvider>();
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
            // Portfolio valuation: the Balances tab showed quantities with no value,
            // total, allocation or day change. The price source is separate so the
            // arithmetic that decides the number a user reads is testable offline.
            services.AddSingleton<AccessibleTrader.Core.Services.Trading.IAssetPriceSource,
                             AccessibleTrader.Core.Services.Trading.MarketDataPriceSource>();
            services.AddSingleton<AccessibleTrader.Core.Services.Trading.PortfolioValuationService>();
            services.AddSingleton<AccessibleTrader.Core.Services.Trading.WalletService>();
            services.AddSingleton<AccessibleTrader.Core.Services.Trading.WithdrawalService>();
            // Quick-trade equity: one instance for the process — the desktop head has one
            // account, so this matches the old static's behaviour without the WebHost's
            // cross-user leak (there, the hub hands each user their own).
            services.AddSingleton<AccessibleTrader.Core.Services.Trading.QuickTradeEquity>();
            services.AddSingleton<IOrderExecutionService, GeneralOrderService>();
            services.AddSingleton<IStrategyIndicatorCache, StrategyIndicatorCache>();
            // Holds the live half of the exit plan the backtester replays — the TP ladder,
            // the move to breakeven and the ATR trail an order cannot carry — and remembers
            // open positions across a restart. Registered BEFORE the engine so the engine's
            // optional dependency resolves.
            services.AddSingleton<AccessibleTrader.Core.Services.Strategies.IStrategyPositionManager,
                                  AccessibleTrader.Core.Services.Strategies.StrategyPositionManager>();
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
            services.AddSingleton<ILabRunner, LabRunner>(); // in-app Lab tab (walk-windows + battery comparison)
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

            // Screening — watchlists plus the screener, which reuses the composer's condition
            // tree (same ISignalCatalog + IConditionEvaluator) evaluated across many symbols.
            // OfflineWorkspaceBuilder is the shared "compute indicators off a bar list" seam that
            // lets the screener and the respect analyzer run against unloaded symbols.
            services.AddSingleton<IOfflineWorkspaceBuilder, OfflineWorkspaceBuilder>();
            services.AddSingleton<AccessibleTrader.Core.Services.Theming.IThemeLibrary, AccessibleTrader.Core.Services.Theming.JsonThemeLibrary>();  // user-made themes
            services.AddSingleton<IWatchlistLibrary, JsonWatchlistLibrary>();
            services.AddSingleton<IScreenerLibrary, JsonScreenerLibrary>();
            services.AddSingleton<IScreenerService, ScreenerService>();

            // Respect analysis — measures how often price actually honoured a line, so "that
            // level looks important" becomes a number. Every candidate (horizontal, moving
            // average, multi-timeframe average, sloped line) is scored by the same analyzer.
            services.AddSingleton<ILevelRespectAnalyzer, LevelRespectAnalyzer>();
            services.AddSingleton<IMaRespectRanker, MaRespectRanker>();

            // Chart-pattern description — an accessibility feature, opt-in, and descriptive only.
            // It reports patterns that are still FORMING (with the level that would confirm them)
            // as well as completed ones, because a pattern announced only on completion cannot be
            // acted on. Built on the swing analyzer so every shape inherits its confirmation lag.
            services.AddSingleton<ISwingStructureAnalyzer, SwingStructureAnalyzer>();
            services.AddSingleton<IChartPatternDetector, ChartPatternDetector>();
            // One detection result shared by navigation speech, the detail summary and the
            // comma/period jump keys — three caches of the same derived value is three chances
            // for them to disagree about what is on the chart.
            services.AddSingleton<IChartPatternCache, ChartPatternCache>();
            services.AddSingleton<IChartPatternFocus, ChartPatternFocus>();
            // Quick trade. Equity is supplied as a delegate so the service can never reach a
            // broker itself — sizing is arithmetic and must stay unit-testable.
            services.AddSingleton<AccessibleTrader.Core.Services.Trading.IQuickTradeService>(sp =>
                new AccessibleTrader.Core.Services.Trading.QuickTradeService(
                    sp.GetRequiredService<IWorkspaceStore>(),
                    sp.GetRequiredService<IEventBus>(),
                    equitySource: () => sp.GetRequiredService<AccessibleTrader.Core.Services.Trading.QuickTradeEquity>().Latest,
                    // Lets arming ASK for a balance when none has been cached yet. Without this the
                    // cache was only ever filled as a side effect of opening the trading dashboard,
                    // so anyone who ticked paper trading and went straight to the chart hit "connect
                    // a trading provider first" with one already connected.
                    //
                    // Reading the balance of the provider on screen — which, in paper mode, the order
                    // service reroutes to the paper broker, so practising works on any chart.
                    // GetService rather than GetRequiredService: a missing registration should
                    // degrade to a spoken refusal, not throw inside a keystroke.
                    equityRefresh: async () =>
                    {
                        var orders = sp.GetService<IOrderExecutionService>();
                        if (orders == null) return;
                        var store = sp.GetRequiredService<IWorkspaceStore>();
                        await orders.GetBalancesAsync(store.State.Identity.Provider ?? string.Empty);
                    },
                    // Read live rather than captured, so changing it in settings takes effect on the
                    // next keypress instead of the next restart.
                    sizingMode: () =>
                    {
                        var settings = sp.GetService<ISettingsManager>();
                        int raw = settings?.GetSetting(SettingsKeys.QuickTradeSizingMode)?.ToObject<int>() ?? 0;
                        return System.Enum.IsDefined(typeof(AccessibleTrader.Core.Services.Trading.QuickTradeSizingMode), raw)
                            ? (AccessibleTrader.Core.Services.Trading.QuickTradeSizingMode)raw
                            : AccessibleTrader.Core.Services.Trading.QuickTradeSizingMode.PositionValue;
                    }));

            // The half of quick trade that actually places the order.
            //
            // It was never registered. QuickTradeService published QuickTradeRequestedEvent, nothing
            // was subscribed, and the order was never sent — so the feature announced "sent",
            // produced no fill, no rejection and no position, and had never placed a single order in
            // its life. Registering is only half of it: this subscribes in its constructor, so it
            // also has to be RESOLVED for that constructor to run. MainLayout injects it.
            services.AddSingleton<AccessibleTrader.Core.Services.Trading.QuickTradeExecutor>();

            // Asset dossier (Alt+I). The two remote sources get their own capped, allow-listed
            // HttpClients: SEC requires a contact email in the User-Agent or www.sec.gov 403s, and
            // GitHub rejects requests with no agent at all. Both are registered even when unused --
            // a missing source degrades one section rather than failing the dossier.
            services.AddSingleton<ICryptoProfileSource>(_ => new CoinGeckoCryptoProfileSource(
                AccessibleTrader.Sdk.Services.PluginHostServices.CreateHttpClient(
                    "AssetDossier.Crypto",
                    new[] { "api.coingecko.com", "api.github.com" },
                    userAgent: "AccessibleTrader/2.2 (accessible-trade-terminal)")));
            services.AddSingleton<ICompanyProfileSource>(_ => new EdgarCompanyProfileSource(
                AccessibleTrader.Sdk.Services.PluginHostServices.CreateHttpClient(
                    "AssetDossier.Company",
                    new[] { "data.sec.gov", "www.sec.gov" },
                    userAgent: "AccessibleTrader/2.2 (codythurst@gmail.com)")));
            services.AddSingleton<IAssetDossierService, AssetDossierService>();
            services.AddSingleton<ILevelProvenanceService, LevelProvenanceService>();
            services.AddSingleton<IReplayService, ReplayService>();
            services.AddSingleton<ISplitViewCoordinator, SplitViewCoordinator>();

            // Alert delivery channels — SMTP + Telegram external dispatchers. The
            // AlertDeliveryService subscribes to AlertFiredEvent and fans out to every
            // configured channel. Config providers pull from ISettingsManager so edits
            // via the Settings modal take effect immediately without a service reload.
            services.AddSingleton<AccessibleTrader.Sdk.Alerts.IAlertChannel>(sp =>
                new AccessibleTrader.Core.Services.Alerts.EmailAlertChannel(
                    () => LoadEmailAlertConfig(sp.GetRequiredService<ISettingsManager>()),
                    sp.GetRequiredService<AccessibleTrader.Core.Services.DemoPolicy>()));
            services.AddSingleton<AccessibleTrader.Sdk.Alerts.IAlertChannel>(sp =>
                new AccessibleTrader.Core.Services.Alerts.TelegramAlertChannel(
                    BuildAlertChannelHttpClient(sp),
                    () => LoadTelegramAlertConfig(sp.GetRequiredService<ISettingsManager>())));
            services.AddSingleton<AccessibleTrader.Sdk.Alerts.IAlertChannel>(sp =>
                new AccessibleTrader.Core.Services.Alerts.WebhookAlertChannel(
                    BuildAlertChannelHttpClient(sp),
                    () => LoadWebhookAlertConfig(sp.GetRequiredService<ISettingsManager>()),
                    // Wire the diagnostics dependencies so missing-target and delivery
                    // failures actually reach the log and the user's speech (both were
                    // silently null before — the warning feature never fired).
                    sp.GetService<Microsoft.Extensions.Logging.ILogger<AccessibleTrader.Core.Services.Alerts.WebhookAlertChannel>>(),
                    sp.GetRequiredService<AccessibleTrader.Core.Services.IEventBus>()));
            services.AddSingleton<AccessibleTrader.Core.Services.Alerts.AlertDeliveryService>();
            // Part C — bridges strategy setup events into AlertFiredEvent (default-off,
            // gated by the "alerts.setups.enabled" setting) so setups can reach webhooks.
            services.AddSingleton<AccessibleTrader.Core.Services.Alerts.SetupAlertBridge>();

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
                    RoslynScriptingService.DefaultWorkerPathResolver,
                    sp.GetRequiredService<DemoPolicy>()));

            // Analysis — candle pattern recogniser and indicator context facts.
            services.AddSingleton<CandlePatternThresholds>();
            services.AddSingleton<ISdkCandlePatternAnalyzer, SdkCandlePatternAnalyzer>();
            services.AddSingleton<IIndicatorContextAnalyzer, IndicatorContextAnalyzer>();

            // Bar detail (Ctrl+Shift+D) and alert system.
            services.AddSingleton<IBarDetailService, BarDetailService>();
            services.AddSingleton<IAlertEvaluator, AlertEvaluator>();
            services.AddSingleton<IAlertOrchestrator, AlertOrchestrator>();
            // Multi-workspace background monitoring: one polling evaluation loop per
            // non-focused tab (alerts + symbol-bound strategies), desktop-gated by
            // DemoPolicy.AllowBackgroundMonitoring and the settings toggle.
            services.AddSingleton<AccessibleTrader.Core.Services.Workspace.ISessionAutosaveService,
                AccessibleTrader.Core.Services.Workspace.SessionAutosaveService>();
            services.AddSingleton<AccessibleTrader.Core.Services.Feeds.IBackgroundTabFeedService,
                AccessibleTrader.Core.Services.Feeds.BackgroundTabFeedService>();
            services.AddSingleton<AccessibleTrader.Core.Services.Workspace.IBackgroundMonitoringService,
                                  AccessibleTrader.Core.Services.Workspace.BackgroundMonitoringService>();

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
            services.AddSingleton<AccessibleTrader.Core.Services.Analysis.ChartPatternNavigator>();
            services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
            services.AddSingleton<IInputRouter, InputRouter>();
            // Chart undo/redo. Same lifetime as the two managers that write to it, so
            // the stack a drag pushes onto is the stack Ctrl+Z reads.
            services.AddSingleton<AccessibleTrader.Core.Services.Accessibility.IChartUndoStack,
                             AccessibleTrader.Core.Services.Accessibility.ChartUndoStack>();
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
            services.AddSingleton<IAutoNarrationService, AutoNarrationService>();
            services.AddSingleton<INotificationHub, NotificationHub>();
            services.AddSingleton<IGlobalErrorCoordinator, GlobalErrorCoordinator>();
            services.AddSingleton<IHistoryBufferCoordinator, HistoryBufferCoordinator>();
            services.AddSingleton<ITradingReconciliationCoordinator, TradingReconciliationCoordinator>();

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
        // Alert-channel HttpClient from the shared Core factory (no redirects; 1 MB /
        // 30 s envelope). The desktop is HostMode.Full, so BlockPrivateNetworkTargets
        // is false and LAN webhook targets (e.g. Home Assistant) keep working.
        private static System.Net.Http.HttpClient BuildAlertChannelHttpClient(IServiceProvider sp)
            => AccessibleTrader.Core.Services.Alerts.AlertChannelHttpClient.Create(
                sp.GetRequiredService<DemoPolicy>().BlockPrivateNetworkTargets);

        /// <summary>Loads email alert channel config from settings under the
        /// "alerts.email" key-path. Missing / malformed settings return null so the
        /// channel's IsConfigured check declines to attempt delivery.</summary>
        private static AccessibleTrader.Core.Services.Alerts.EmailAlertChannelConfig? LoadEmailAlertConfig(ISettingsManager settings)
        {
            var host = settings.GetSetting(SettingsKeys.EmailHost)?.ToString();
            var port = settings.GetSetting(SettingsKeys.EmailPort)?.ToObject<int>() ?? 587;
            var useTls = settings.GetSetting(SettingsKeys.EmailUseTls)?.ToObject<bool>() ?? true;
            var username = settings.GetSetting(SettingsKeys.EmailUsername)?.ToString();
            var password = settings.GetSetting(SettingsKeys.EmailPassword)?.ToString();
            var from = settings.GetSetting(SettingsKeys.EmailFromAddress)?.ToString();
            var to   = settings.GetSetting(SettingsKeys.EmailToAddress)?.ToString();
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
            var token = settings.GetSetting(SettingsKeys.TelegramBotToken)?.ToString();
            var chat  = settings.GetSetting(SettingsKeys.TelegramChatId)?.ToString();
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chat))
                return null;
            return new AccessibleTrader.Core.Services.Alerts.TelegramAlertChannelConfig
            {
                BotToken = token,
                ChatId = chat,
            };
        }

        /// <summary>Loads webhook alert channel config from settings: the named list at
        /// "alerts.webhooks", migrating a legacy single "alerts.webhook.url" into a
        /// {Name:"Default"} entry. HTTPS is enforced by the channel's IsConfigured check.</summary>
        private static AccessibleTrader.Core.Services.Alerts.WebhookAlertChannelConfig? LoadWebhookAlertConfig(ISettingsManager settings)
            => AccessibleTrader.Core.Services.Alerts.WebhookAlertConfigLoader.Load(settings);
    }
}

