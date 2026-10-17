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
/// <b>Spider Lines was removed (2026-10-17), and everything saved while it existed says so once.</b>
///
/// <para>Cody: the "Spider Lines" indicator (SPIDER_LINES — eight Fibonacci-length EMAs and a
/// Stacking Score, every EMA flagged as a zone line) was misnamed and had nothing to do with
/// Market Cipher's spider lines (CryptoFace's diagonal support/resistance lines on the BTC log
/// chart). "I'd just remove the indicator for now."</para>
///
/// <para>Removing the provider is one line per host. What a removal owes a blind user is what
/// happens to everything that still NAMES it: a saved chart, a strategy, an alert. Before this,
/// each one met an unknown code. The restore rebuilt the series verbatim with no provider behind
/// it — a series on the list that draws nothing and says nothing; a strategy leaf resolved to no
/// signal and was false on every bar; an alert stayed armed and could never fire. Silence in all
/// three, and silence is indistinguishable from a market that never got there. Every test here
/// runs the REAL path from a saved file on disk (<see cref="WorkspaceLibraryService"/>,
/// <see cref="JsonStrategyLibrary"/>, <c>alerts.json</c>) to the sentence the user hears.</para>
/// </summary>
public sealed class SpiderLinesRemovalTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly TempWorkspacePaths _paths = new();
    public SpiderLinesRemovalTests(ITestOutputHelper output) => _out = output;
    public void Dispose() { try { Directory.Delete(_paths.AppDataDirectory, recursive: true); } catch { } }

    private const string Notice =
        "Spider Lines was removed on 2026-10-17, so it was left off this chart: it was eight exponential moving averages, "
        + "lengths 8 to 200, under a misleading name, and had nothing to do with Market Cipher's spider lines, which are "
        + "diagonal support and resistance lines. To get the same lines back, add an EMA for each length you want.";

    /// <summary>One Spider Lines series as a build before 2026-10-17 saved it: the eight EMAs and
    /// the hidden Stacking Score, narrated, on the price pane.</summary>
    private static string SpiderSeriesJson(string id) => $$"""
        {
          "Id": "{{id}}", "Name": "Spider Lines", "FriendlyName": "Spider Lines", "IndicatorCode": "SPIDER_LINES",
          "Pane": "Main", "IsVisible": true, "IsAutoNarrated": true,
          "Parameters": { "FastMode": 0.0 },
          "Components": [
            { "Name": "EMA 8",   "DisplayName": "EMA 8",   "DisplayType": 0, "IsVisible": true, "IsZoneLine": true, "ColorHex": "#FF4D4D" },
            { "Name": "EMA 13",  "DisplayName": "EMA 13",  "DisplayType": 0, "IsVisible": true, "IsZoneLine": true, "ColorHex": "#FF8C00" },
            { "Name": "EMA 21",  "DisplayName": "EMA 21",  "DisplayType": 0, "IsVisible": true, "IsZoneLine": true, "ColorHex": "#FFD700" },
            { "Name": "EMA 34",  "DisplayName": "EMA 34",  "DisplayType": 0, "IsVisible": true, "IsZoneLine": true, "ColorHex": "#66BB6A" },
            { "Name": "EMA 55",  "DisplayName": "EMA 55",  "DisplayType": 0, "IsVisible": true, "IsZoneLine": true, "ColorHex": "#26C6DA" },
            { "Name": "EMA 89",  "DisplayName": "EMA 89",  "DisplayType": 0, "IsVisible": true, "IsZoneLine": true, "ColorHex": "#42A5F5" },
            { "Name": "EMA 144", "DisplayName": "EMA 144", "DisplayType": 0, "IsVisible": true, "IsZoneLine": true, "ColorHex": "#AB47BC" },
            { "Name": "EMA 200", "DisplayName": "EMA 200", "DisplayType": 0, "IsVisible": true, "IsZoneLine": true, "ColorHex": "#EC407A" },
            { "Name": "Stacking Score", "DisplayName": "Stacking Score", "DisplayType": 0, "IsVisible": false, "ColorHex": "#FFFFFF" }
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

    /// <summary>
    /// The saved session: TWO tabs that both carry Spider Lines (so "once" means once, not once
    /// per tab), the second active with an RSI beside it (so what is NOT removed is seen to stay).
    /// </summary>
    private static string SavedSessionJson() => $$"""
        {
          "ActiveTabIndex": 1,
          "Tabs": [
            { "Market": "Spot", "Provider": "Binance", "Symbol": "ETHUSDT", "Timeframe": "4h",
              "Series": [ {{CandlesJson()}}, {{SpiderSeriesJson("spider-eth")}} ] },
            { "Market": "Spot", "Provider": "Binance", "Symbol": "BTCUSDT", "Timeframe": "1d",
              "Series": [ {{CandlesJson()}}, {{RsiJson("rsi-btc")}}, {{SpiderSeriesJson("spider-btc")}} ] }
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
        // Every provider the app can register — so the saved RSI has its metadata, and so the test
        // fails if anything still answers to SPIDER_LINES.
        var providers = IndicatorProviderFixture.AllProviders();
        var indicatorService = new IndicatorService(providers, NullLogger<IndicatorService>.Instance);
        var engine = new IndicatorEngine(indicatorService, registry, providers);
        var seriesService = new SeriesManagementService(store, storeBus, factory, styling,
            library, registry, engine, new MockIndicatorPreferencesService());
        return (store, new WorkspaceInitializer(seriesService, store, indicatorService), library);
    }

    private static IEnumerable<ChartSeries> EverySeries(WorkspaceState state) =>
        state.ActiveSeries.Concat((state.TabSnapshots ?? ImmutableList<TabSnapshot>.Empty).SelectMany(t => t.ActiveSeries));

    /// <summary>
    /// The startup resume of a saved session that carries Spider Lines on two tabs: it loads, the
    /// Spider Lines series are not on any tab, the RSI is, and the ONE resume sentence ends with
    /// the notice — said once although two tabs carried it, on the sentence that would otherwise
    /// interrupt it.
    /// </summary>
    [Fact]
    public void A_saved_session_with_Spider_Lines_resumes_without_it_and_says_so_once()
    {
        var (store, initializer, library) = RealRestore();
        File.WriteAllText(Path.Combine(_paths.AppDataDirectory, "Workspaces", SessionAutosaveService.LastSessionProfileName + "fixture.json"),
            SavedSessionJson());

        var announcements = Substitute.For<IEventBus>();
        using var svc = new SessionAutosaveService(store, library, initializer,
            Substitute.For<ISettingsManager>(), new DemoPolicy(false), announcements,
            NullLogger<SessionAutosaveService>.Instance);

        Assert.True(svc.TryResumeAtStartup(), "the saved session did not load at all");

        var all = EverySeries(store.State).ToList();
        Assert.DoesNotContain(all, s => string.Equals(s.IndicatorCode, "SPIDER_LINES", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(all, s => s.Id == "rsi-btc");
        Assert.Equal("BTCUSDT", store.State.Identity.Symbol);

        var spoken = announcements.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IEventBus.Publish))
            .Select(c => c.GetArguments()[0])
            .OfType<AnnouncementEvent>()
            .ToList();
        var only = Assert.Single(spoken);
        _out.WriteLine(only.Message);
        Assert.Equal(SpeechChannel.Interface, only.Channel);
        Assert.Equal("Resumed your last session: 2 tabs. BTCUSDT 1 day, with RSI. " + Notice, only.Message);
    }

    /// <summary>
    /// The named-workspace load (Ctrl+O) reads the same notice off the initializer, and a load
    /// that drops nothing has none — the property is per restore, not a latch that keeps saying
    /// it about a workspace that no longer carries it.
    /// </summary>
    [Fact]
    public void The_notice_belongs_to_the_restore_that_dropped_it()
    {
        var (_, initializer, library) = RealRestore();
        File.WriteAllText(Path.Combine(_paths.AppDataDirectory, "Workspaces", "with-spider.json"), SavedSessionJson());
        File.WriteAllText(Path.Combine(_paths.AppDataDirectory, "Workspaces", "without.json"), $$"""
            { "Tabs": [ { "Market": "Spot", "Provider": "Binance", "Symbol": "BTCUSDT", "Timeframe": "1d",
                          "Series": [ {{CandlesJson()}}, {{RsiJson("rsi-2")}} ] } ] }
            """);

        initializer.RestoreWorkspace(library.LoadProfile("with-spider")!);
        Assert.Equal(Notice, initializer.LastRestoreNotice);

        initializer.RestoreWorkspace(library.LoadProfile("without")!);
        Assert.Null(initializer.LastRestoreNotice);
    }

    /// <summary>The legacy single-tab format restores through its own loop; it skips and says so too.</summary>
    [Fact]
    public void A_legacy_single_tab_save_with_Spider_Lines_drops_it_and_says_so()
    {
        var (store, initializer, library) = RealRestore();
        File.WriteAllText(Path.Combine(_paths.AppDataDirectory, "Workspaces", "legacy.json"), $$"""
            { "Symbol": "BTCUSDT", "Timeframe": "1d", "Series": [ {{CandlesJson()}}, {{SpiderSeriesJson("spider-legacy")}} ] }
            """);

        initializer.RestoreWorkspace(library.LoadProfile("legacy")!);

        Assert.DoesNotContain(EverySeries(store.State), s => s.Id == "spider-legacy");
        Assert.Equal(Notice, initializer.LastRestoreNotice);
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
            Identity = new ChartIdentity("Spot", "binance", "BTC/USDT", "1h"),
            CurrentDataIndex = bars.Count - 1,
        });
    }

    private static RiskPlan PercentStop(StopSource? stop = null, IReadOnlyList<TpLadderRung>? tps = null) => new(
        Stop: stop ?? new StopSource(StopSourceKind.PercentOfPrice, PercentValue: 1.0),
        TpLadder: tps ?? Array.Empty<TpLadderRung>(),
        Sizing: new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005),
        Entry: new EntryTrigger(EntryTriggerKind.Immediate));

    /// <summary>Saves <paramref name="spec"/> to strategies.json through the real library, reads it
    /// back with a fresh one, builds it through the real factory, and runs two live bars.</summary>
    private List<string> RunSavedStrategy(StrategySpec spec)
    {
        new JsonStrategyLibrary(_paths).Upsert(spec);
        var loaded = Assert.Single(new JsonStrategyLibrary(_paths).All);

        var bus = new SpyEventBus();
        var strategy = new ConfigurableStrategyFactory(new ConditionEvaluator(Catalog), new RiskPlanResolver(), Catalog, bus)
            .Create(loaded, "t");
        var (bars, state) = Market();
        strategy.Initialize(bars, state, new Dictionary<string, object>());
        strategy.OnBar(bars[^1], bars, state);
        strategy.OnBar(bars[^1], bars, state);

        return bus.Log.OfType<FeedbackRequestEvent>()
            .Where(e => e.Type == FeedbackType.Error && e.Message != null)
            .Select(e => e.Message!)
            .ToList();
    }

    /// <summary>A saved strategy whose condition reads a Spider Lines EMA says, once, that the
    /// condition can never be true, why, and the exact replacement.</summary>
    [Fact]
    public void A_saved_strategy_on_a_Spider_Lines_EMA_says_so_once()
    {
        var spec = new StrategySpec("user.spider", "Spider bounce", "built before 2026-10-17", OrderSide.Buy,
            new ConditionGroup("root", LogicOperator.And, new List<ConditionNode>
            {
                new ConditionLeaf("a", "Rsi.Rsi", LeafOperator.LessThan, 40),
                new ConditionLeaf("b", "SPIDER_LINES.EMA 21", LeafOperator.LessThan, 0),
            }),
            PercentStop(), StrategyExecutionMode.Suggestion);

        var errors = RunSavedStrategy(spec);
        foreach (var e in errors) _out.WriteLine(e);

        var once = Assert.Single(errors);
        Assert.Equal(
            "Strategy 'Spider bounce' has a condition that can never be true: EMA 21 on Spider Lines cannot be read because "
            + "Spider Lines was removed on 2026-10-17: it was eight exponential moving averages, lengths 8 to 200, under a "
            + "misleading name, and had nothing to do with Market Cipher's spider lines, which are diagonal support and "
            + "resistance lines; use an EMA with length 21 instead. Edit the strategy to replace it.",
            once);
    }

    /// <summary>A stop placed on a Spider Lines line reads nothing and the plan is refused on every
    /// bar; the strategy says so once, naming the stop.</summary>
    [Fact]
    public void A_saved_strategy_with_its_stop_on_Spider_Lines_says_so_once()
    {
        var spec = new StrategySpec("user.spider-stop", "Spider stop", "built before 2026-10-17", OrderSide.Buy,
            new ConditionLeaf("a", "Rsi.Rsi", LeafOperator.LessThan, 40),
            PercentStop(stop: new StopSource(StopSourceKind.BelowComponent, IndicatorCode: "SPIDER_LINES", ComponentName: "EMA 55")),
            StrategyExecutionMode.Suggestion);

        var errors = RunSavedStrategy(spec);
        foreach (var e in errors) _out.WriteLine(e);

        var once = Assert.Single(errors);
        Assert.StartsWith("Strategy 'Spider stop' has a stop that can never be placed: EMA 55 on Spider Lines cannot be read because Spider Lines was removed on 2026-10-17", once);
        Assert.EndsWith("use an EMA with length 55 instead. Edit the strategy to replace it.", once);
    }

    /// <summary>A profit target placed on a Spider Lines line, the same.</summary>
    [Fact]
    public void A_saved_strategy_with_a_target_on_Spider_Lines_says_so_once()
    {
        var spec = new StrategySpec("user.spider-tp", "Spider target", "built before 2026-10-17", OrderSide.Buy,
            new ConditionLeaf("a", "Rsi.Rsi", LeafOperator.LessThan, 40),
            PercentStop(tps: new[] { new TpLadderRung(TargetSourceKind.AtComponent, ClosePortion: 1.0, IndicatorCode: "SPIDER_LINES", ComponentName: "EMA 200") }),
            StrategyExecutionMode.Suggestion);

        var once = Assert.Single(RunSavedStrategy(spec));
        _out.WriteLine(once);
        Assert.StartsWith("Strategy 'Spider target' has a profit target that can never be placed: EMA 200 on Spider Lines cannot be read", once);
    }

    /// <summary>Vacuity: the same strategy shape on signals that exist says nothing at all.</summary>
    [Fact]
    public void A_strategy_on_live_signals_says_nothing()
    {
        var spec = new StrategySpec("user.fine", "Fine", "d", OrderSide.Buy,
            new ConditionLeaf("a", "Rsi.Rsi", LeafOperator.LessThan, 40),
            PercentStop(stop: new StopSource(StopSourceKind.BelowComponent, IndicatorCode: "Ema", ComponentName: "Ema")),
            StrategyExecutionMode.Suggestion);

        Assert.Empty(RunSavedStrategy(spec));
    }

    // ── Alerts ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A plain indicator alert and a condition-tree alert, both saved on Spider Lines, loaded from
    /// alerts.json by the real orchestrator: each says ONCE that it cannot fire, why, and — for the
    /// plain one — a remedy that does not send the user looking for an indicator that cannot be there.
    /// </summary>
    [Fact]
    public void Saved_alerts_on_Spider_Lines_each_say_once_they_cannot_fire()
    {
        var library = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, _paths);
        library.SaveAlerts(new[]
        {
            new AlertDefinition
            {
                Id = "plain", Name = "Price over the 89", Delivery = AlertDelivery.Speech, Target = AlertTarget.Indicator,
                Condition = AlertCondition.CrossesAbove, Threshold = 0, IndicatorCode = "SPIDER_LINES", ComponentName = "EMA 89",
            },
            new AlertDefinition
            {
                Id = "tree", Name = "Stacked bull", Delivery = AlertDelivery.Speech, Target = AlertTarget.Indicator,
                Condition = AlertCondition.CrossesAbove,
                ConditionTree = new ConditionLeaf("s", "SPIDER_LINES.Stacking Score", LeafOperator.GreaterThan, 20),
            },
            new AlertDefinition
            {
                Id = "line", Name = "Price crosses the 200", Delivery = AlertDelivery.Speech, Target = AlertTarget.Price,
                Condition = AlertCondition.CrossesAbove, LineIndicatorCode = "SPIDER_LINES", LineComponentName = "EMA 200",
            },
        });

        var (bars, state) = Market();
        state = state with { InitStatus = InitializationStatus.Ready };
        var conditions = new ConditionEvaluator(Catalog);
        var evaluator = new AlertEvaluator(new SdkCandlePatternAnalyzer(), new IndicatorContextAnalyzer(), conditionEvaluator: conditions);
        var bus = new SpyEventBus();
        var loaded = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, _paths).LoadAlerts();
        Assert.Equal(3, loaded.Count);
        _ = new AlertOrchestrator(new MockWorkspaceStore(), evaluator, bus,
            new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, _paths),
            NullLogger<AlertOrchestrator>.Instance, conditions);

        for (int i = 0; i < 3; i++)
            Assert.Empty(evaluator.EvaluateAlerts(loaded, state, bars[^1], bars[^2], new Dictionary<string, double>()));

        var errors = bus.Log.OfType<FeedbackRequestEvent>().Where(e => e.Type == FeedbackType.Error).Select(e => e.Message!).ToList();
        foreach (var e in errors) _out.WriteLine(e);
        Assert.Equal(3, errors.Count);
        Assert.Equal(
            "Alert 'Price over the 89' can't be fully evaluated and may stay silent: EMA 89 on Spider Lines cannot "
            + "be read because Spider Lines was removed on 2026-10-17: it was eight exponential moving averages, lengths 8 to 200, "
            + "under a misleading name, and had nothing to do with Market Cipher's spider lines, which are diagonal support and "
            + "resistance lines; use an EMA with length 89 instead. Edit the alert or delete it.",
            errors.Single(e => e.Contains("Price over the 89")));
        Assert.Equal(
            "Alert 'Price crosses the 200' can't be fully evaluated and may stay silent: EMA 200 on Spider Lines cannot "
            + "be read because Spider Lines was removed on 2026-10-17: it was eight exponential moving averages, lengths 8 to 200, "
            + "under a misleading name, and had nothing to do with Market Cipher's spider lines, which are diagonal support and "
            + "resistance lines; use an EMA with length 200 instead. Edit the alert or delete it.",
            errors.Single(e => e.Contains("Price crosses the 200")));
        var tree = errors.Single(e => e.Contains("Stacked bull"));
        Assert.Contains("Stacking Score on Spider Lines cannot be read because Spider Lines was removed on 2026-10-17", tree);
        Assert.Contains("add an EMA for each length you want instead", tree);
    }

    // ── The background monitor (browser closed) ────────────────────────────

    private static HeadlessChartFactory Headless()
    {
        var indicators = new IndicatorService(Fleet, NullLogger<IndicatorService>.Instance);
        var engine = new IndicatorEngine(indicators, new CustomIndicatorRegistry(), Fleet);
        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var prefs = new MockIndicatorPreferencesService();
        return new HeadlessChartFactory(indicators, engine, new IndicatorStateMapper(), new IndicatorModelFactory(styling, prefs),
            prefs, new IndicatorContextAnalyzer(), catalog: Catalog);
    }

    /// <summary>
    /// The saved tab the background monitor reads, from the same session file. The narrated Spider
    /// Lines is not built (no provider: the verbatim fallback was a series that never computes) and
    /// is reported as REMOVED, not as "not available here"; an alert on it builds nothing and is
    /// not reported as missing either — the evaluator says that per alert.
    /// </summary>
    [Fact]
    public void The_headless_chart_reports_a_narrated_Spider_Lines_as_removed()
    {
        var config = Newtonsoft.Json.JsonConvert.DeserializeObject<WorkspaceConfiguration>(SavedSessionJson())!;
        var tab = config.Tabs[1];
        var alert = new AlertDefinition
        {
            Id = "plain", Name = "Price over the 89", Delivery = AlertDelivery.Speech, Target = AlertTarget.Indicator,
            Condition = AlertCondition.CrossesAbove, Threshold = 0, IndicatorCode = "SPIDER_LINES", ComponentName = "EMA 89",
        };
        foreach (var s in tab.Series) s.IsAutoNarrated = s.IndicatorCode is "SPIDER_LINES" or "Rsi";

        var chart = Headless().Create(new ChartIdentity("Spot", "Binance", "BTCUSDT", "1d"), tab.Series, new[] { alert });

        Assert.DoesNotContain(chart.Series, s => string.Equals(s.IndicatorCode, "SPIDER_LINES", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(chart.Series, s => s.Id == "rsi-btc");
        Assert.Equal(new[] { "SPIDER_LINES" }, chart.RemovedIndicators);
        Assert.Empty(chart.MissingIndicators);

        string said = AccessibleTrader.WebHost.Services.LocalBackgroundMonitor.RemovedIndicatorNotice("SPIDER_LINES", "BTCUSDT");
        _out.WriteLine(said);
        Assert.Equal(
            "Spider Lines on BTCUSDT is not narrated in the background. Spider Lines was removed on 2026-10-17: it was eight "
            + "exponential moving averages, lengths 8 to 200, under a misleading name, and had nothing to do with Market Cipher's "
            + "spider lines, which are diagonal support and resistance lines; add an EMA for each length you want instead.",
            said);
    }

    // ── The fleet ──────────────────────────────────────────────────────────

    /// <summary>
    /// No provider any host can register answers to a retired code. If one did, the restore would
    /// still drop the series (the retired check runs first) while the Add Indicator dialog offered
    /// it — removed and present at once.
    /// </summary>
    [Fact]
    public void No_provider_declares_a_retired_indicator()
    {
        Assert.NotEmpty(RetiredIndicators.Codes);
        var live = Fleet.SelectMany(p => p.GetIndicators()).Select(m => m.Code).ToList();
        Assert.True(live.Count > 50, $"the fleet scan found only {live.Count} indicators; it is not looking at the fleet");
        Assert.Empty(live.Intersect(RetiredIndicators.Codes, StringComparer.OrdinalIgnoreCase));
    }
}
