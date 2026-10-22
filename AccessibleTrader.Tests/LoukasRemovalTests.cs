using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Loukas Cycle Detection was removed (2026-10-22), and everything saved while it existed says
/// so once, naming the Cipher C signal that replaces it.</b>
///
/// <para>Cody: "loukas is what we want to get rid of we don't need it." Cipher C, rebuilt on
/// 2026-10-21, finds daily, weekly and yearly cycle lows per asset, which is what Loukas did. The
/// removal goes through <see cref="RetiredIndicators"/> the way Spider Lines did
/// (<see cref="SpiderLinesRemovalTests"/> holds the full path for every kind of saved item); these
/// tests pin what is particular to Loukas: the notice, and the three signals that have an exact
/// Cipher C counterpart.</para>
/// </summary>
public sealed class LoukasRemovalTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly TempWorkspacePaths _paths = new();
    public LoukasRemovalTests(ITestOutputHelper output) => _out = output;
    public void Dispose() { try { Directory.Delete(_paths.AppDataDirectory, recursive: true); } catch { } }

    private const string Notice =
        "Loukas Cycle Detection was removed on 2026-10-22, so it was left off this chart: Cipher C now finds daily, "
        + "weekly and yearly cycle lows for each asset, which is what it did. To get the same lines back, add Cipher C.";

    private const string Why =
        "Loukas Cycle Detection was removed on 2026-10-22: Cipher C now finds daily, weekly and yearly cycle lows "
        + "for each asset, which is what it did";

    /// <summary>A Loukas series as a build before 2026-10-22 saved it, in its own pane.</summary>
    private static string LoukasSeriesJson(string id) => $$"""
        {
          "Id": "{{id}}", "Name": "Loukas Cycle Detection", "FriendlyName": "Loukas Cycle Detection",
          "IndicatorCode": "LOUKAS_CYCLES", "Pane": "Pane_LOUKAS_CYCLES", "IsVisible": true, "IsAutoNarrated": true,
          "Parameters": { "SwingLookback": 10.0, "DcMinBars": 20.0 },
          "Components": [
            { "Name": "DC Day Count",  "DisplayName": "DC Day Count",  "DisplayType": 0, "IsVisible": true, "ColorHex": "#42A5F5" },
            { "Name": "DCL Confirmed", "DisplayName": "DCL Confirmed", "DisplayType": 0, "IsVisible": true, "ColorHex": "#66BB6A" }
          ]
        }
        """;

    private static string CandlesJson() => """
        { "Id": "candles", "Name": "Candles", "FriendlyName": "Candles", "IndicatorCode": "CANDLES", "Pane": "Main", "IsVisible": true }
        """;

    private static string RsiJson(string id) => $$"""
        { "Id": "{{id}}", "Name": "RSI", "FriendlyName": "RSI", "IndicatorCode": "Rsi", "Pane": "Pane_Rsi", "IsVisible": true,
          "Parameters": { "lookbackPeriods": 14.0 } }
        """;

    private static string SavedSessionJson() => $$"""
        {
          "ActiveTabIndex": 1,
          "Tabs": [
            { "Market": "Spot", "Provider": "Binance", "Symbol": "ETHUSDT", "Timeframe": "1d",
              "Series": [ {{CandlesJson()}}, {{LoukasSeriesJson("loukas-eth")}} ] },
            { "Market": "Spot", "Provider": "Binance", "Symbol": "BTCUSDT", "Timeframe": "1d",
              "Series": [ {{CandlesJson()}}, {{RsiJson("rsi-btc")}}, {{LoukasSeriesJson("loukas-btc")}} ] }
          ]
        }
        """;

    private (WorkspaceStore Store, WorkspaceInitializer Initializer, WorkspaceLibraryService Library) RealRestore()
    {
        var storeBus = new EventBus();
        var store = new WorkspaceStore(storeBus, new MockViewportRangeCalculator(),
            new MockViewportNavigationService(), new MockVolumeStateService());
        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var factory = new IndicatorModelFactory(styling, new MockIndicatorPreferencesService());
        var library = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, _paths);
        var registry = new CustomIndicatorRegistry();
        var providers = IndicatorProviderFixture.AllProviders();
        var indicatorService = new IndicatorService(providers, NullLogger<IndicatorService>.Instance);
        var engine = new IndicatorEngine(indicatorService, registry, providers);
        var seriesService = new SeriesManagementService(store, storeBus, factory, styling,
            library, registry, engine, new MockIndicatorPreferencesService());
        return (store, new WorkspaceInitializer(seriesService, store, indicatorService), library);
    }

    /// <summary>The startup resume of a session with Loukas on two tabs: the Loukas series are on
    /// no tab, the RSI stays, and the one resume sentence ends with the notice, said once.</summary>
    [Fact]
    public void A_saved_session_with_Loukas_resumes_without_it_and_says_so_once()
    {
        var (store, initializer, library) = RealRestore();
        File.WriteAllText(Path.Combine(_paths.AppDataDirectory, "Workspaces", SessionAutosaveService.LastSessionProfileName + "fixture.json"),
            SavedSessionJson());

        var announcements = Substitute.For<IEventBus>();
        using var svc = new SessionAutosaveService(store, library, initializer,
            Substitute.For<ISettingsManager>(), new DemoPolicy(false), announcements,
            NullLogger<SessionAutosaveService>.Instance);

        Assert.True(svc.TryResumeAtStartup(), "the saved session did not load at all");

        var all = store.State.ActiveSeries
            .Concat((store.State.TabSnapshots ?? ImmutableList<TabSnapshot>.Empty).SelectMany(t => t.ActiveSeries)).ToList();
        Assert.DoesNotContain(all, s => string.Equals(s.IndicatorCode, "LOUKAS_CYCLES", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(all, s => s.Id == "rsi-btc");

        var only = Assert.Single(announcements.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IEventBus.Publish))
            .Select(c => c.GetArguments()[0])
            .OfType<AnnouncementEvent>());
        _out.WriteLine(only.Message);
        Assert.Equal("Resumed your last session: 2 tabs. BTCUSDT 1 day, with RSI. " + Notice, only.Message);
    }

    // ── Strategies ─────────────────────────────────────────────────────────

    private static readonly List<IIndicatorProvider> Fleet = IndicatorProviderFixture.AllProviders();
    private static readonly SignalCatalog Catalog = new(Fleet);

    private static (List<Ohlcv> Bars, WorkspaceState State) Market()
    {
        var bars = CausalityProbeSeries.Bars(CausalityProbeSeries.HourlyFlavours.First(), 300);
        return (bars, WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            Identity = new ChartIdentity("Spot", "binance", "BTC/USDT", "1d"),
            CurrentDataIndex = bars.Count - 1,
        });
    }

    private List<string> RunSavedStrategy(string signalId)
    {
        var spec = new StrategySpec("user.loukas", "Cycle low buy", "built before 2026-10-22", OrderSide.Buy,
            new ConditionGroup("root", LogicOperator.And, new List<ConditionNode>
            {
                new ConditionLeaf("a", "Rsi.Rsi", LeafOperator.LessThan, 40),
                new ConditionLeaf("b", signalId, LeafOperator.FiredWithin, 0, WithinNBars: 2),
            }),
            new RiskPlan(
                Stop: new StopSource(StopSourceKind.PercentOfPrice, PercentValue: 1.0),
                TpLadder: Array.Empty<TpLadderRung>(),
                Sizing: new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005),
                Entry: new EntryTrigger(EntryTriggerKind.Immediate)),
            StrategyExecutionMode.Suggestion);

        new JsonStrategyLibrary(_paths).Upsert(spec);
        var loaded = Assert.Single(new JsonStrategyLibrary(_paths).All);
        var bus = new SpyEventBus();
        var strategy = new ConfigurableStrategyFactory(new ConditionEvaluator(Catalog), new RiskPlanResolver(), Catalog, bus)
            .Create(loaded, "t");
        var (bars, state) = Market();
        strategy.Initialize(bars, state, new Dictionary<string, object>());
        strategy.OnBar(bars[^1], bars, state);
        strategy.OnBar(bars[^1], bars, state);

        var errors = bus.Log.OfType<FeedbackRequestEvent>()
            .Where(e => e.Type == FeedbackType.Error && e.Message != null)
            .Select(e => e.Message!)
            .ToList();
        foreach (var e in errors) _out.WriteLine(e);
        return errors;
    }

    /// <summary>The three Loukas signals with a Cipher C counterpart name it; the rest (the day
    /// counts, the four-year phase) fall back to adding Cipher C.</summary>
    [Theory]
    [InlineData("DCL Confirmed", "use DCL Confirmed on Cipher C instead")]
    [InlineData("ICL Confirmed", "use WCL Confirmed on Cipher C instead")]
    [InlineData("DC In Window", "use Daily Timing Window on Cipher C instead")]
    [InlineData("DC Day Count", "add Cipher C instead")]
    public void A_saved_strategy_on_a_Loukas_signal_says_so_once_with_its_replacement(string component, string instead)
    {
        var once = Assert.Single(RunSavedStrategy($"LOUKAS_CYCLES.{component}"));
        Assert.Equal(
            $"Strategy 'Cycle low buy' has a condition that can never be true: {component} on Loukas Cycle Detection "
            + $"cannot be read because {Why}; {instead}. Edit the strategy to replace it.",
            once);
    }

    /// <summary>
    /// A replacement the user is told to use has to exist: each Cipher C name the notices give is
    /// a component Cipher C declares, read off the provider the app registers. If Cipher C renames
    /// one, the sentence would send a blind user looking for a signal that is not there.
    /// </summary>
    [Fact]
    public void Every_replacement_named_is_a_Cipher_C_component()
    {
        var cipherC = Fleet.SelectMany(p => p.GetIndicators()).Single(m => m.Code == "CIPHER_C");
        var components = cipherC.Components.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var loukas in new[] { "DCL Confirmed", "ICL Confirmed", "DC In Window" })
        {
            string why = RetiredIndicators.SignalReason($"LOUKAS_CYCLES.{loukas}")!;
            var named = System.Text.RegularExpressions.Regex.Match(why, @"; use (.+) on Cipher C instead$");
            Assert.True(named.Success, why);
            Assert.Contains(named.Groups[1].Value, components);
        }
        Assert.False(Fleet.SelectMany(p => p.GetIndicators()).Any(m => m.Code == "LOUKAS_CYCLES"),
            "a provider still answers to LOUKAS_CYCLES");
    }

    // ── Alerts ─────────────────────────────────────────────────────────────

    /// <summary>A saved alert on a Loukas cycle low says once that it cannot fire, and what to use.</summary>
    [Fact]
    public void A_saved_alert_on_Loukas_says_once_it_cannot_fire()
    {
        var library = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, _paths);
        library.SaveAlerts(new[]
        {
            new AlertDefinition
            {
                Id = "dcl", Name = "Daily cycle low", Delivery = AlertDelivery.Speech, Target = AlertTarget.Indicator,
                Condition = AlertCondition.CrossesAbove, Threshold = 0, IndicatorCode = "LOUKAS_CYCLES", ComponentName = "DCL Confirmed",
            },
        });

        var (bars, state) = Market();
        state = state with { InitStatus = InitializationStatus.Ready };
        var conditions = new ConditionEvaluator(Catalog);
        var evaluator = new AlertEvaluator(new SdkCandlePatternAnalyzer(), new IndicatorContextAnalyzer(), conditionEvaluator: conditions);
        var bus = new SpyEventBus();
        var loaded = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, _paths).LoadAlerts();
        _ = new AlertOrchestrator(new MockWorkspaceStore(), evaluator, bus,
            new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, _paths),
            NullLogger<AlertOrchestrator>.Instance, conditions);

        for (int i = 0; i < 3; i++)
            Assert.Empty(evaluator.EvaluateAlerts(loaded, state, bars[^1], bars[^2], new Dictionary<string, double>()));

        var once = Assert.Single(bus.Log.OfType<FeedbackRequestEvent>().Where(e => e.Type == FeedbackType.Error).Select(e => e.Message!));
        _out.WriteLine(once);
        Assert.Equal(
            "Alert 'Daily cycle low' can't be fully evaluated and may stay silent: DCL Confirmed on Loukas Cycle Detection "
            + $"cannot be read because {Why}; use DCL Confirmed on Cipher C instead. Edit the alert or delete it.",
            once);
    }
}
