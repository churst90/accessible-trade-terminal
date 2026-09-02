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

            // Historical OHLCV store — shared, like the DB it sits on: public market data, one
            // copy for everyone. Singleton so the write lock inside it is process-wide.
            services.AddSingleton<IOhlcvStore, OhlcvStore>();

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

            // Singleton, unlike its per-user siblings above: WebHostPluginHttpClientFactory
            // is stateless (no ctor, no fields — it builds a fresh HttpClient per policy),
            // and PluginHostServices.HttpClientFactory is a process-wide static. Registering
            // it Scoped meant it could never be resolved from the root provider to fill that
            // static, so the bridge in Program.cs was impossible and the allow-list was never
            // installed on this head at all — every plugin fell through to a bare HttpClient
            // with no host check, on the one head that faces the public internet.
            services.AddSingleton<AccessibleTrader.Sdk.Services.IPluginHttpClientFactory, WebHostPluginHttpClientFactory>();

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

                // IPlatformPathService, not GetFolderPath: the latter returns an empty string on
                // Unix when the target does not exist, which wrote the audit log into whatever
                // directory the process was started from (the deployment directory a redeploy
                // replaces). An explicit *_DIR override still wins and stays deliberately
                // process-wide, for operators who ship this to a log collector.
                //
                // The directory is resolved PER EVENT, and that is the fix for two defects at
                // once. Every authentication event is recorded from a Razor Page; a Razor Page
                // request is not a Blazor circuit, and ICurrentUser was only ever populated by
                // the circuit handler — so DataKey was "anon" for all of them and every user's
                // sign-ins, lockouts, 2FA changes and email addresses pooled into ONE shared
                // users/anon/SecurityEvents/ file. CurrentUser now falls back to the ambient
                // HttpContext principal, but a captured path would still not be enough: a
                // sign-in request is anonymous right up until PasswordSignInAsync succeeds,
                // which happens long after this sink was constructed. Late binding is what
                // makes a successful sign-in land under the account that just signed in.
                //
                // And when there IS no user — a failed sign-in for an address that may not even
                // exist, a forgot-password POST — the event goes to the INSTANCE log rather than
                // to users/anon. Those events are not attributable to an account, and an
                // operator should not have to know to look in a directory whose name says it
                // holds no user's data.
                var instancePaths = sp.GetRequiredService<InstancePaths>();
                var explicitDir = Environment.GetEnvironmentVariable("ACCESSIBLETRADER_SECURITY_EVENT_DIR");
                var sinkLogger = sp.GetService<Microsoft.Extensions.Logging.ILogger<AccessibleTrader.Core.Services.Security.SecurityEventFileSink>>();

                return new AccessibleTrader.Core.Services.Security.SecurityEventFileSink(
                    ringBuffer,
                    () =>
                    {
                        if (!string.IsNullOrEmpty(explicitDir)) return explicitDir;

                        var key = sp.GetService<Account.ICurrentUser>()?.DataKey;
                        if (string.IsNullOrEmpty(key) || key == "anon")
                            return instancePaths.SecurityEventDirectory;

                        return Path.Combine(
                            sp.GetRequiredService<IPlatformPathService>().AppDataDirectory,
                            "SecurityEvents");
                    },
                    sinkLogger);
            });
            // ── The two bridges plugins reach through a process-wide static ─────
            // PluginHostServices.ApiKeys and .SecurityEvents were both left null on this
            // head while MauiProgram assigned them, so the credential-checkout migration
            // was inert and 22 audit call sites wrote to nothing. Both are registered as
            // SINGLETONS on purpose — the statics they fill are process-wide, and neither
            // of these carries per-user state (the credential store is a singleton; host
            // security events are instance-level by nature). See PluginHostBridges.
            services.AddSingleton<PluginHostApiKeyBridge>();

            // NOT the registered IPlatformPathService: on the hosted head that one is Scoped
            // and per-user, which is the whole thing an instance-level sink must not be.
            // AccountsServiceExtensions replaces this registration with one pinned to the
            // instance data root, exactly as it does for the shared secret store.
            services.AddSingleton(new InstancePaths(new WebHostPathService()));
            services.AddSingleton<PluginHostSecurityEventLog>();

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
            services.AddSingleton<ICacheService, FileCacheService>();
            services.AddSingleton<IResamplerService, ResamplerService>();
            services.AddSingleton<IApiKeyService, ApiKeyService>();

            services.AddScoped<HistoricalDataFetcher>();
            // Lazy hub + store so a reconnect can gap-fill the outage and record feed
            // freshness. Func<>, not the interface: MarketFeedHub -> IDataOrchestrator ->
            // LiveStreamManager -> IMarketFeedHub is a cycle, and deferring the lookup to
            // first use is what breaks it.
            services.AddScoped<LiveStreamManager>(sp => new LiveStreamManager(
                sp.GetRequiredService<IDataService>(),
                sp.GetRequiredService<IGlobalErrorCoordinator>(),
                sp.GetRequiredService<ILogger<LiveStreamManager>>(),
                () => sp.GetService<AccessibleTrader.Core.Services.Feeds.IMarketFeedHub>(),
                sp.GetService<IWorkspaceStore>()));

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
            services.AddScoped<IIndicatorProvider, SwingStructureProvider>();
            services.AddScoped<IIndicatorProvider, ValueDeviationProvider>();
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

            // ONE paper account per user, not per browser tab. Registered scoped so each circuit
            // still resolves normally, but the instance comes from the process-wide hub and the
            // per-circuit PaperAccountAttachment owns the chart binding. AddScoped<..., Paper-
            // TradingProvider> gave every tab its own account object over one file, and the last
            // tab to persist silently erased the others' trades — see PaperAccountHub.
            services.AddSingleton<PaperAccountHub>();
            services.AddScoped<Services.PaperAccountAttachment>();
            services.AddScoped<IPaperTradingProvider>(sp =>
                sp.GetRequiredService<Services.PaperAccountAttachment>().Account);
            // Portfolio valuation: the Balances tab showed quantities with no value,
            // total, allocation or day change. The price source is separate so the
            // arithmetic that decides the number a user reads is testable offline.
            services.AddScoped<AccessibleTrader.Core.Services.Trading.IAssetPriceSource,
                             AccessibleTrader.Core.Services.Trading.MarketDataPriceSource>();
            services.AddScoped<AccessibleTrader.Core.Services.Trading.PortfolioValuationService>();
            services.AddScoped<AccessibleTrader.Core.Services.Trading.WalletService>();
            services.AddScoped<AccessibleTrader.Core.Services.Trading.WithdrawalService>();
            // Quick-trade equity: one cache per USER (hub), resolved per circuit. Scoping the
            // cache itself would give one user a stale copy per browser tab; the static it
            // replaced leaked one user's balance into another's position sizing.
            services.AddSingleton<AccessibleTrader.Core.Services.Trading.QuickTradeEquityHub>();
            services.AddScoped<AccessibleTrader.Core.Services.Trading.QuickTradeEquity>(sp =>
                sp.GetRequiredService<AccessibleTrader.Core.Services.Trading.QuickTradeEquityHub>()
                  .ForUser(sp.GetService<Account.ICurrentUser>()?.DataKey));
            services.AddScoped<IOrderExecutionService, GeneralOrderService>();
            services.AddScoped<IStrategyIndicatorCache, StrategyIndicatorCache>();
            // Per-circuit, like the engine it serves: the positions file it persists is
            // per-user, and its path resolves on first use so the hosted head reads it after
            // ICurrentUser is set rather than under users/anon.
            services.AddScoped<AccessibleTrader.Core.Services.Strategies.IStrategyPositionManager,
                               AccessibleTrader.Core.Services.Strategies.StrategyPositionManager>();
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

            // Screening — see the MAUI head's registration block for the rationale.
            services.AddScoped<IOfflineWorkspaceBuilder, OfflineWorkspaceBuilder>();
            services.AddScoped<AccessibleTrader.Core.Services.Theming.IThemeLibrary, AccessibleTrader.Core.Services.Theming.JsonThemeLibrary>();  // user-made themes
            services.AddScoped<IWatchlistLibrary, JsonWatchlistLibrary>();
            services.AddScoped<IScreenerLibrary, JsonScreenerLibrary>();
            services.AddScoped<IScreenerService, ScreenerService>();

            // Respect analysis — see the MAUI head's registration block for the rationale.
            services.AddScoped<ILevelRespectAnalyzer, LevelRespectAnalyzer>();
            services.AddScoped<IMaRespectRanker, MaRespectRanker>();

            // Chart-pattern description — see the MAUI head's registration block for the rationale.
            services.AddScoped<ISwingStructureAnalyzer, SwingStructureAnalyzer>();
            services.AddScoped<IChartPatternDetector, ChartPatternDetector>();
            // One detection result shared by navigation speech, the detail summary and the
            // comma/period jump keys — three caches of the same derived value is three chances
            // for them to disagree about what is on the chart.
            services.AddScoped<IChartPatternCache, ChartPatternCache>();
            services.AddScoped<IChartPatternFocus, ChartPatternFocus>();
            // Quick trade. Equity is supplied as a delegate so the service can never reach a
            // broker itself — sizing is arithmetic and must stay unit-testable.
            services.AddScoped<AccessibleTrader.Core.Services.Trading.IQuickTradeService>(sp =>
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
            services.AddScoped<AccessibleTrader.Core.Services.Trading.QuickTradeExecutor>();

            // Asset dossier (Alt+I). The two remote sources get their own capped, allow-listed
            // HttpClients: SEC requires a contact email in the User-Agent or www.sec.gov 403s, and
            // GitHub rejects requests with no agent at all. Both are registered even when unused --
            // a missing source degrades one section rather than failing the dossier.
            services.AddScoped<ICryptoProfileSource>(_ => new CoinGeckoCryptoProfileSource(
                AccessibleTrader.Sdk.Services.PluginHostServices.CreateHttpClient(
                    "AssetDossier.Crypto",
                    new[] { "api.coingecko.com", "api.github.com" },
                    userAgent: "AccessibleTrader/2.2 (accessible-trade-terminal)")));
            services.AddScoped<ICompanyProfileSource>(_ => new EdgarCompanyProfileSource(
                AccessibleTrader.Sdk.Services.PluginHostServices.CreateHttpClient(
                    "AssetDossier.Company",
                    new[] { "data.sec.gov", "www.sec.gov" },
                    userAgent: "AccessibleTrader/2.2 (codythurst@gmail.com)")));
            services.AddScoped<IAssetDossierService, AssetDossierService>();
            services.AddScoped<ILevelProvenanceService, LevelProvenanceService>();
            services.AddScoped<IReplayService, ReplayService>();
            services.AddScoped<ISplitViewCoordinator, SplitViewCoordinator>();

            // Alert channels connect to USER-supplied targets (webhook URL, SMTP
            // host/port), so their HttpClient comes from AlertChannelHttpClient —
            // no redirects, and on non-Full hosts a public-internet-only connect
            // guard. The provider allow-list factory cannot serve here: its whole
            // model is a fixed host list, and these targets are chosen by the user.
            services.AddScoped<AccessibleTrader.Sdk.Alerts.IAlertChannel>(sp =>
                new AccessibleTrader.Core.Services.Alerts.EmailAlertChannel(
                    () => LoadEmailAlertConfig(sp.GetRequiredService<ISettingsManager>()),
                    sp.GetRequiredService<AccessibleTrader.Core.Services.DemoPolicy>()));
            services.AddScoped<AccessibleTrader.Sdk.Alerts.IAlertChannel>(sp =>
                new AccessibleTrader.Core.Services.Alerts.TelegramAlertChannel(
                    BuildAlertChannelHttpClient(sp),
                    () => LoadTelegramAlertConfig(sp.GetRequiredService<ISettingsManager>())));
            services.AddScoped<AccessibleTrader.Sdk.Alerts.IAlertChannel>(sp =>
                new AccessibleTrader.Core.Services.Alerts.WebhookAlertChannel(
                    BuildAlertChannelHttpClient(sp),
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
                    RoslynScriptingService.DefaultWorkerPathResolver,
                    // Service-layer enforcement of AllowCustomScripts: on Hosted/Demo the
                    // compile entry points throw — the Razor @if that hides the modal is
                    // presentation, not a security boundary.
                    sp.GetRequiredService<AccessibleTrader.Core.Services.DemoPolicy>()));

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
            services.AddScoped<AccessibleTrader.Core.Services.Analysis.ChartPatternNavigator>();
            // The one ordered modal stack. Same lifetime as the dispatcher that aims Escape by
            // it and the layout that pushes it to the browser's Tab trap — see ModalStack.
            services.AddScoped<AccessibleTrader.Core.Services.Input.ModalStack>();
            services.AddScoped<ICommandDispatcher, CommandDispatcher>();
            services.AddScoped<IInputRouter, InputRouter>();
            // Chart undo/redo. Same lifetime as the two managers that write to it, so
            // the stack a drag pushes onto is the stack Ctrl+Z reads.
            services.AddScoped<AccessibleTrader.Core.Services.Accessibility.IChartUndoStack,
                             AccessibleTrader.Core.Services.Accessibility.ChartUndoStack>();
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

            // The journal is per-circuit (it is the visitor's own spoken transcript), but
            // /diag/journal is a plain HTTP request and so resolves a DIFFERENT scope —
            // which is why that endpoint could only ever return an empty array. Mirror
            // each circuit's entries into a process-wide, per-owner ring so the endpoint
            // has something to read, keyed so it can only ever hand back the caller's own.
            // See JournalMirror.
            services.AddSingleton<JournalMirror>();
            services.AddScoped<IJournalService>(sp =>
            {
                var journal = new JournalService(
                    sp.GetRequiredService<IEventBus>(),
                    sp.GetRequiredService<IWorkspaceStore>());
                var mirror = sp.GetRequiredService<JournalMirror>();
                // Owner resolved per ENTRY, not captured at construction: on the hosted
                // head the circuit handler sets ICurrentUser after the DI graph is built,
                // so a key captured here would be "anon" for the whole circuit.
                journal.EntryAdded += entry => mirror.Record(
                    sp.GetService<Account.ICurrentUser>()?.DataKey ?? JournalMirror.LocalOwner,
                    entry);
                return journal;
            });

            return services;
        }

        // ── Alert delivery helpers (config loaders shared with the MAUI head) ──

        private static System.Net.Http.HttpClient BuildAlertChannelHttpClient(IServiceProvider sp)
            => AccessibleTrader.Core.Services.Alerts.AlertChannelHttpClient.Create(
                sp.GetRequiredService<AccessibleTrader.Core.Services.DemoPolicy>().BlockPrivateNetworkTargets);

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
