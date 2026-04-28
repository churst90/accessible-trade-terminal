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
using AccessibleTrader.Core.Services.Strategies;
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
    public IStrategyBacktester StrategyBacktester { get; }
    public IBacktestWarmupAnalyzer BacktestWarmupAnalyzer { get; }

    private readonly List<IAlertChannel> _alertChannels = new();

    public BlazorTestHarness()
    {
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
        StrategyBacktester          = Substitute.For<IStrategyBacktester>();
        BacktestWarmupAnalyzer      = Substitute.For<IBacktestWarmupAnalyzer>();

        Ctx.Services.AddSingleton(EventBus);
        Ctx.Services.AddSingleton(WorkspaceStore);
        Ctx.Services.AddSingleton(StrategyCoordinator);
        Ctx.Services.AddSingleton(StrategyLibrary);
        Ctx.Services.AddSingleton(SettingsManager);
        Ctx.Services.AddSingleton(ThemeService);
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
        Ctx.Services.AddSingleton(StrategyBacktester);
        Ctx.Services.AddSingleton(BacktestWarmupAnalyzer);
        Ctx.Services.AddSingleton<IEnumerable<IAlertChannel>>(_alertChannels);

        // Most modals call accessibleTrader.focusElement on first render via
        // ModalBase.ShowModalAsync. Shim it once for every test.
        Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true);
    }

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
