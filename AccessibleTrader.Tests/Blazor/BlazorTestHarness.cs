// Shared scaffolding for bUnit modal tests.
//
// Why this exists: every modal under test injects ~5-10 services from Core +
// Sdk. Hand-stubbing every interface across every test file produces hundreds
// of lines of boilerplate; NSubstitute auto-generates no-op stubs that we only
// need to override per-test for the methods/properties the test actually
// exercises. The real EventBus is used (not stubbed) so OpenXxxEvent flows
// through the modal subscription paths exactly as they do in production.
//
// Usage:
//   var harness = new BlazorTestHarness();
//   var settings = harness.SettingsManager;            // already registered + Substitute.For
//   var bus      = harness.EventBus;                   // real EventBus, registered
//   harness.OverrideAlertChannels(emailChannel);       // swap default channel set
//   var cut = harness.OpenModal<SettingsModal>(b => b.Publish(new OpenSettingsEvent()));

using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using Bunit;
using DynamicData;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public sealed class BlazorTestHarness : IDisposable
{
    public TestContext Ctx { get; } = new();
    public IEventBus EventBus { get; } = new EventBus();

    // Services common to most modals — substituted at construction; tests
    // override individual members via NSubstitute's `.Returns(...)` API.
    public IWorkspaceStore WorkspaceStore { get; }
    public IStrategyModalCoordinator StrategyCoordinator { get; }
    public IStrategyLibrary StrategyLibrary { get; }
    public ISettingsManager SettingsManager { get; }
    public IThemeService ThemeService { get; }
    public IDataExportService DataExporter { get; }
    public IWorkspaceLibraryService WorkspaceLibrary { get; }
    public ISeriesManagementService SeriesManager { get; }
    public IShortcutManager ShortcutManager { get; }
    public IIndicatorModelFactory IndicatorModelFactory { get; }
    public IIndicatorService IndicatorService { get; }
    public IIndicatorPreferencesService IndicatorPreferences { get; }
    public ISignalCatalog SignalCatalog { get; }
    public IStrategyLibraryFacade StrategyLibraryFacade { get; }
    public IConfigurableStrategyFactory ConfigurableStrategyFactory { get; }
    public ISpeechManager SpeechManager { get; }
    public ISpeechFeedbackRouter SpeechRouter { get; }
    public IStrategyBacktester StrategyBacktester { get; }
    public IBacktestWarmupAnalyzer BacktestWarmupAnalyzer { get; }
    public IOrderExecutionService OrderService { get; }
    public AccessibleTrader.Core.Services.ISoundPatchLibrary SoundPatchLibrary { get; }
    public IApiKeyService ApiKeyService { get; }
    public IDataService DataService { get; }
    public IMarketOrchestrator MarketOrchestrator { get; }
    public IJournalService JournalService { get; }

    private readonly List<IAlertChannel> _alertChannels = new();

    public BlazorTestHarness()
    {
        // bUnit's default WaitForAssertion/WaitForElement timeout is ONE second,
        // and starved CI runners have now lost that race twice on two different
        // modal focus tests (each green in isolation, each polling correctly).
        // 10s changes nothing for a passing test — the wait returns the moment
        // the assertion holds — it only stops a slow runner reading as a bug.
        TestContext.DefaultWaitTimeout = TimeSpan.FromSeconds(10);

        // ── Core services with non-trivial state need the real impl seeded with
        //    safe defaults; everything else is a Substitute.For<>. The real
        //    WorkspaceStore can't be used (it depends on its own deep graph),
        //    so we stub it and seed State with WorkspaceState.Initial.
        WorkspaceStore     = Substitute.For<IWorkspaceStore>();
        WorkspaceStore.State.Returns(_ => WorkspaceState.Initial);
        WorkspaceStore.StateStream.Returns(_ => System.Reactive.Linq.Observable.Empty<WorkspaceState>());
        WorkspaceStore.DataStream.Returns(_ => System.Reactive.Linq.Observable.Empty<IChangeSet<Ohlcv, DateTime>>());
        WorkspaceStore.SeriesStream.Returns(_ => System.Reactive.Linq.Observable.Empty<IChangeSet<ChartSeries, string>>());

        StrategyCoordinator = Substitute.For<IStrategyModalCoordinator>();
        StrategyCoordinator.ActiveStrategies.Returns(_ => Array.Empty<ActiveStrategy>());

        StrategyLibrary    = Substitute.For<IStrategyLibrary>();
        StrategyLibrary.All.Returns(_ => Array.Empty<StrategySpec>());

        SettingsManager    = Substitute.For<ISettingsManager>();
        // Use the real ThemeService — its only dependency is ISettingsManager,
        // and a fully-stubbed IThemeService trips NRE in any modal that reads
        // ThemeService.Current.Background (which is many of them).
        ThemeService       = new ThemeService(SettingsManager);
        DataExporter       = Substitute.For<IDataExportService>();
        WorkspaceLibrary   = Substitute.For<IWorkspaceLibraryService>();
        SeriesManager        = Substitute.For<ISeriesManagementService>();
        ShortcutManager      = Substitute.For<IShortcutManager>();
        IndicatorModelFactory = Substitute.For<IIndicatorModelFactory>();
        IndicatorService     = Substitute.For<IIndicatorService>();
        IndicatorPreferences = Substitute.For<IIndicatorPreferencesService>();
        IndicatorService.GetAvailableIndicators().Returns(new List<IndicatorMetadata>());
        SignalCatalog               = Substitute.For<ISignalCatalog>();
        StrategyLibraryFacade       = Substitute.For<IStrategyLibraryFacade>();
        ConfigurableStrategyFactory = Substitute.For<IConfigurableStrategyFactory>();
        SpeechManager               = Substitute.For<ISpeechManager>();
        SpeechRouter                = Substitute.For<ISpeechFeedbackRouter>();
        StrategyBacktester          = Substitute.For<IStrategyBacktester>();
        BacktestWarmupAnalyzer      = Substitute.For<IBacktestWarmupAnalyzer>();
        OrderService                = Substitute.For<IOrderExecutionService>();
        SoundPatchLibrary           = Substitute.For<AccessibleTrader.Core.Services.ISoundPatchLibrary>();
        SoundPatchLibrary.GetPatches().Returns(_ => new List<AccessibleTrader.Sdk.Models.SoundPatch>());
        SoundPatchLibrary.EarconOverrides.Returns(new AccessibleTrader.Core.Services.EarconSettings());
        OrderService.GetOrderBookAsync(default!, default!, default).ReturnsForAnyArgs(
            _ => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>())));
        OrderService.SubscribeOrderBookAsync(default!, default!).ReturnsForAnyArgs(
            _ => Task.FromResult<IObservable<OrderBookUpdate>?>(null));

        Ctx.Services.AddSingleton(EventBus);
        Ctx.Services.AddSingleton(WorkspaceStore);
        Ctx.Services.AddSingleton(StrategyCoordinator);
        Ctx.Services.AddSingleton(StrategyLibrary);
        Ctx.Services.AddSingleton(SettingsManager);
        // Typed facade over the substituted manager — real implementation, so tests
        // that stub SettingsManager.GetSetting see consistent typed reads.
        Ctx.Services.AddSingleton<IAppSettings>(new AppSettings(SettingsManager));
        Ctx.Services.AddSingleton(ThemeService);
        // The theme library backs Settings' theme picker and the theme editor. Substituted rather
        // than real so no test writes a themes.json into the developer's app-data directory.
        Ctx.Services.AddSingleton(Substitute.For<AccessibleTrader.Core.Services.Theming.IThemeLibrary>());
        Ctx.Services.AddSingleton(DataExporter);
        Ctx.Services.AddSingleton(WorkspaceLibrary);
        Ctx.Services.AddSingleton(SeriesManager);
        Ctx.Services.AddSingleton(ShortcutManager);
        Ctx.Services.AddSingleton(IndicatorModelFactory);
        Ctx.Services.AddSingleton(IndicatorService);
        Ctx.Services.AddSingleton(IndicatorPreferences);
        Ctx.Services.AddSingleton(SignalCatalog);
        Ctx.Services.AddSingleton(StrategyLibraryFacade);
        Ctx.Services.AddSingleton(ConfigurableStrategyFactory);
        Ctx.Services.AddSingleton(SpeechManager);
        Ctx.Services.AddSingleton(SpeechRouter);
        Ctx.Services.AddSingleton(StrategyBacktester);
        Ctx.Services.AddSingleton(BacktestWarmupAnalyzer);
        Ctx.Services.AddSingleton(OrderService);
        // SettingsModal injects IPaperTradingProvider (paper-trading reset button).
        Ctx.Services.AddSingleton(Substitute.For<IPaperTradingProvider>());
        // SettingsModal injects IBackgroundMonitoringService (background-monitoring fieldset).
        Ctx.Services.AddSingleton(Substitute.For<AccessibleTrader.Core.Services.Workspace.IBackgroundMonitoringService>());
        // ...and IBackgroundTabFeedService (live background tabs toggle, keyed feeds Phase C).
        Ctx.Services.AddSingleton(Substitute.For<AccessibleTrader.Core.Services.Feeds.IBackgroundTabFeedService>());
        // SettingsModal injects IRuntimePlatform (braille fieldset is hidden on the
        // browser host). Substitute defaults: all-false → native-desktop-like.
        Ctx.Services.AddSingleton(Substitute.For<AccessibleTrader.Core.Services.IRuntimePlatform>());
        // StrategyModal injects ILabRunner (the in-app Lab tab).
        Ctx.Services.AddSingleton(Substitute.For<AccessibleTrader.Core.Services.Strategies.ILabRunner>());
        // SoundDesignerModal injects IWavetableLibrary (WAV import). Empty id lists by default.
        var wavetables = Substitute.For<AccessibleTrader.Core.Services.Audio.IWavetableLibrary>();
        wavetables.WavetableIds.Returns(new List<string>());
        wavetables.SampleIds.Returns(new List<string>());
        Ctx.Services.AddSingleton(wavetables);
        // MainLayout/Toolbar/AddIndicatorModal inject DemoPolicy; no-op in tests.
        Ctx.Services.AddSingleton(new DemoPolicy(isDemo: false));
        Ctx.Services.AddSingleton<IEnumerable<IAlertChannel>>(_alertChannels);
        // PropertiesModal / SoundDesignerModal inject the sound-patch services. Real registry
        // (parameterless, cheap) so patch dropdowns list built-ins; the rest are no-op substitutes.
        Ctx.Services.AddSingleton<AccessibleTrader.Core.Services.ISonificationManager>(Substitute.For<AccessibleTrader.Core.Services.ISonificationManager>());
        Ctx.Services.AddSingleton(SoundPatchLibrary);
        Ctx.Services.AddSingleton<AccessibleTrader.Core.Services.Audio.ISoundPatchRegistry>(new AccessibleTrader.Core.Services.Audio.SoundPatchRegistry());

        // ── Services required only by the full-catalog contract tests
        //    (ModalAccessibilityContractTests / AriaValueScanTests /
        //    ModalDisposeLeakTests render every openable dialog). Substitutes by
        //    default; a test that needs behaviour re-registers via With<T>(),
        //    which appends a later registration that wins resolution.
        ApiKeyService      = Substitute.For<IApiKeyService>();
        DataService        = Substitute.For<IDataService>();
        MarketOrchestrator = Substitute.For<IMarketOrchestrator>();
        JournalService     = Substitute.For<IJournalService>();
        // NSubstitute leaves Task<List<T>> / concrete-class members at null, and
        // these are awaited unguarded on several modals' open paths.
        ApiKeyService.GetAllKeysAsync().Returns(new List<ApiKeyConfig>());
        ApiKeyService.GetKeysForProviderAsync(default!).ReturnsForAnyArgs(new List<ApiKeyConfig>());
        DataService.LoadAvailableMarketsAsync().Returns(new List<string>());
        DataService.LoadProvidersByMarketTypeAsync(default!).ReturnsForAnyArgs(new List<string>());
        DataService.GetSupportedSubTypesAsync(default!, default!).ReturnsForAnyArgs(new List<string>());
        DataService.LoadSymbolsAsync(default!, default!).ReturnsForAnyArgs(new List<string>());
        ShortcutManager.CurrentProfile.Returns(new ShortcutProfile());
        Ctx.Services.AddSingleton(ApiKeyService);
        Ctx.Services.AddSingleton(DataService);
        Ctx.Services.AddSingleton(MarketOrchestrator);
        Ctx.Services.AddSingleton(JournalService);
        Ctx.Services.AddSingleton(Substitute.For<AccessibleTrader.Core.Services.Analysis.IAssetDossierService>());
        Ctx.Services.AddSingleton(Substitute.For<AccessibleTrader.Core.Services.Analysis.ILevelProvenanceService>());
        Ctx.Services.AddSingleton(Substitute.For<AccessibleTrader.Core.Services.Analysis.IMaRespectRanker>());
        Ctx.Services.AddSingleton(Substitute.For<IAlertOrchestrator>());
        Ctx.Services.AddSingleton(Substitute.For<IRoslynScriptingService>());
        Ctx.Services.AddSingleton(Substitute.For<IAIAnalystService>());
        Ctx.Services.AddSingleton(Substitute.For<IWorkspaceInitializer>());
        Ctx.Services.AddSingleton(Substitute.For<IAudioDriver>());
        Ctx.Services.AddSingleton(new AccessibleTrader.Core.Services.Diagnostics.CheckoutLatencyTracker());
        Ctx.Services.AddSingleton(Substitute.For<AccessibleTrader.Core.Services.Screening.IWatchlistLibrary>());
        Ctx.Services.AddSingleton(Substitute.For<AccessibleTrader.Core.Services.Screening.IScreenerLibrary>());
        Ctx.Services.AddSingleton(Substitute.For<AccessibleTrader.Core.Services.Screening.IScreenerService>());
        Ctx.Services.AddSingleton(Substitute.For<AccessibleTrader.Core.Services.Accessibility.IViewportManager>());
        // ThemeEditorModal injects the CONCRETE ThemeService (it edits presets in
        // place); reuse the same instance registered above as IThemeService.
        Ctx.Services.AddSingleton((ThemeService)ThemeService);
        // Wallet/Withdraw/TradingDashboard inject sealed concrete services; they are
        // constructible from the substitutes above, so their real logic runs against
        // stubbed data/keys — which is exactly what their markup tests exercise.
        Ctx.Services.AddSingleton(new WalletService(
            DataService, Microsoft.Extensions.Logging.Abstractions.NullLogger<WalletService>.Instance));
        Ctx.Services.AddSingleton(new WithdrawalService(
            DataService, ApiKeyService, Microsoft.Extensions.Logging.Abstractions.NullLogger<WithdrawalService>.Instance));
        Ctx.Services.AddSingleton(new PortfolioValuationService(
            Substitute.For<AccessibleTrader.Core.Services.Trading.IAssetPriceSource>()));
        // ILogger<T> for components that inject it directly (TradingDashboardModal, Toolbar).
        Ctx.Services.AddSingleton(
            typeof(Microsoft.Extensions.Logging.ILogger<>),
            typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));

        // Most modals call accessibleTrader.focusElement on first render via
        // ModalBase.ShowModalAsync. Shim it once for every test. bUnit records
        // every invocation, which FocusedElementIds exposes for assertions.
        Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true);
    }

    /// <summary>Element ids passed to accessibleTrader.focusElement, in call
    /// order. The stub above answers every call, but bUnit still records them —
    /// this is how tests assert WHERE focus was sent rather than merely that
    /// the modal rendered (the gap that made focus bugs untestable).</summary>
    public IReadOnlyList<string> FocusedElementIds =>
        Ctx.JSInterop.Invocations
            .Where(i => i.Identifier == "accessibleTrader.focusElement")
            .Select(i => i.Arguments.Count > 0 ? i.Arguments[0]?.ToString() ?? "" : "")
            .ToList();

    /// <summary>Registers an additional service that isn't included in the
    /// default harness (e.g. IAlertOrchestrator, indicator-pipeline services).
    /// Call before rendering the component under test.</summary>
    public BlazorTestHarness With<TService>(TService implementation) where TService : class
    {
        Ctx.Services.AddSingleton(implementation);
        return this;
    }

    /// <summary>Replace the default empty IAlertChannel set with the supplied
    /// channels. Settings modal tests use this to register an "email" channel
    /// that records SendAsync calls.</summary>
    public void OverrideAlertChannels(params IAlertChannel[] channels)
    {
        _alertChannels.Clear();
        _alertChannels.AddRange(channels);
    }

    /// <summary>Render TModal then run a publish action against EventBus to
    /// drive the Open* event into the modal's subscription. Returns the
    /// rendered component handle.</summary>
    public IRenderedComponent<TModal> OpenModal<TModal>(Action<IEventBus> publishOpenEvent)
        where TModal : Microsoft.AspNetCore.Components.IComponent
    {
        var cut = Ctx.RenderComponent<TModal>();
        cut.InvokeAsync(() => publishOpenEvent(EventBus)).GetAwaiter().GetResult();
        return cut;
    }

    public void Dispose() => Ctx.Dispose();
}
