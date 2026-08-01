// bUnit modal coverage — first real-component test target (post-RCL-extraction).
//
// What changed since the spike: AccessibleTrader.BlazorClient.Components is now
// a Razor Class Library targeting net10.0. The MAUI host references it; the
// test project references it; we can render the actual production component.
// The fixture replica from the spike is deleted — no more drift risk between
// fixture and real.
//
// Recipe (validated):
//   1. Build a TestContext, register stubs for every @inject in the modal +
//      its visible child components.
//   2. Shim every IJSRuntime call BEFORE rendering; call Setup* on the
//      JSInterop strict-mode mock for each unique invocation site.
//   3. Open the modal by calling its public ShowAsync() (mirrors the production
//      path where IEventBus.Subscribe<OpenStrategiesEvent> invokes the same).
//   4. Use [data-testid] selectors when adding new ones; fall back to ARIA
//      role + accessible-name selectors against the existing DOM.
//   5. Verify mock interactions and DOM state.

using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.StrategyLab.Catalogue;
using Bunit;
using DynamicData;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests.Blazor;

public class StrategyModalTests
{
    // ── Stub coordinator (records calls, returns canned results) ─────────
    private sealed class StubCoordinator : IStrategyModalCoordinator
    {
        public List<string> StartCalls { get; } = new();
        public List<string> StopCalls { get; } = new();
        public List<string> RemoveCalls { get; } = new();
        public List<ActiveStrategy> Active { get; } = new();

        public IEnumerable<ActiveStrategy> ActiveStrategies => Active;

        public StrategyCoordinatorResult StartSpec(string specId)
        {
            StartCalls.Add(specId);
            return StrategyCoordinatorResult.Ok($"Started {specId}");
        }

        public StrategyCoordinatorResult StopSpec(string specId)
        {
            StopCalls.Add(specId);
            return StrategyCoordinatorResult.Ok($"Stopped {specId}");
        }

        public StrategyCoordinatorResult RemoveActive(string instanceId)
        {
            RemoveCalls.Add(instanceId);
            return StrategyCoordinatorResult.Ok("");
        }

        public StrategyCoordinatorResult TogglePause(string instanceId, bool currentlyPaused) =>
            StrategyCoordinatorResult.Ok("");

        public (int RecommendedBars, StrategyCoordinatorResult Result) RecommendedWarmup(string specId) =>
            (0, StrategyCoordinatorResult.Ok(""));

        public Task<(BacktestResult? Result, StrategyCoordinatorResult Status)> RunBacktestAsync(
            string specId, IReadOnlyList<Ohlcv> data, BacktestConfig config, WorkspaceState state) =>
            Task.FromResult<(BacktestResult?, StrategyCoordinatorResult)>(
                (null, StrategyCoordinatorResult.Ok("")));

        public Task<StrategyCoordinatorResult> CompileAndAddStrategyAsync(string code, StrategyExecutionMode execMode) =>
            Task.FromResult(StrategyCoordinatorResult.Ok(""));
    }

    // ── In-memory IStrategyLibrary backed by a list ──────────────────────
    private sealed class StubLibrary : IStrategyLibrary
    {
        private readonly List<StrategySpec> _specs;
        public StubLibrary(IEnumerable<StrategySpec>? seeded = null)
            => _specs = seeded?.ToList() ?? new();

        public IReadOnlyList<StrategySpec> All => _specs;
        public StrategySpec? GetById(string id) => _specs.FirstOrDefault(s => s.Id == id);
        public void Upsert(StrategySpec spec)
        {
            _specs.RemoveAll(s => s.Id == spec.Id);
            _specs.Add(spec);
        }
        public void Remove(string id) => _specs.RemoveAll(s => s.Id == id);
        public void Save() { }
        public void Reload() { }
    }

    // ── Stub workspace store with a static state ─────────────────────────
    private sealed class StubWorkspaceStore : IWorkspaceStore
    {
        public WorkspaceState State { get; }
        public IObservable<WorkspaceState> StateStream { get; } =
            System.Reactive.Linq.Observable.Empty<WorkspaceState>();
        public IObservable<IChangeSet<Ohlcv, DateTime>> DataStream { get; } =
            System.Reactive.Linq.Observable.Empty<IChangeSet<Ohlcv, DateTime>>();
        public IObservable<IChangeSet<ChartSeries, string>> SeriesStream { get; } =
            System.Reactive.Linq.Observable.Empty<IChangeSet<ChartSeries, string>>();

        public StubWorkspaceStore(WorkspaceState state) => State = state;
        public void Dispatch(WorkspaceAction action) { }
    }

    private static (TestContext ctx, StubCoordinator coord, StubLibrary lib, StubWorkspaceStore store, IEventBus bus)
        BuildContext(IEnumerable<StrategySpec>? seededLibrary = null, WorkspaceState? state = null)
    {
        var ctx = new TestContext();
        var coord = new StubCoordinator();
        var lib   = new StubLibrary(seededLibrary);
        var store = new StubWorkspaceStore(state ?? WorkspaceState.Initial);
        // Use the real EventBus so OpenStrategiesEvent flows through to the
        // modal's Subscribe<OpenStrategiesEvent> handler. ShowAsync is private,
        // so the test path mirrors production: publish the open event, the
        // modal's subscription invokes ShowAsync on its own.
        IEventBus bus = new EventBus();

        ctx.Services.AddSingleton<IStrategyModalCoordinator>(coord);
        ctx.Services.AddSingleton<IStrategyLibrary>(lib);
        ctx.Services.AddSingleton<IWorkspaceStore>(store);
        ctx.Services.AddSingleton(bus);
        // Lab tab (Part: in-app research harness) injects ILabRunner.
        ctx.Services.AddSingleton(NSubstitute.Substitute.For<AccessibleTrader.Core.Services.Strategies.ILabRunner>());
        // The modal calls JSRuntime.InvokeVoidAsync("accessibleTrader.focusElement", ...)
        // on first render after Show. Shim here so every test inherits it.
        ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true);
        return (ctx, coord, lib, store, bus);
    }

    /// <summary>
    /// Helper: render the modal then publish OpenStrategiesEvent so the
    /// modal's subscription invokes its private ShowAsync. Mirrors the
    /// production open path (Toolbar button publishes the same event).
    /// </summary>
    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.StrategyModal>
        OpenModal(TestContext ctx, IEventBus bus)
    {
        var cut = ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.StrategyModal>();
        cut.InvokeAsync(() => bus.Publish(new OpenStrategiesEvent())).GetAwaiter().GetResult();
        return cut;
    }

    private static StrategySpec PickSeed() =>
        StrategyCatalogue.AllSpecs()
            .First(s => s.Id == StrategyCatalogue.LongV23pCipherBPivotsId);

    /// <summary>
    /// Modal renders nothing until OpenStrategiesEvent fires. Baseline:
    /// confirms the test harness can render the real RCL component.
    /// </summary>
    [Fact]
    public void StrategyModal_HiddenByDefault_RendersEmpty()
    {
        var (ctx, _, _, _, _) = BuildContext();

        var cut = ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.StrategyModal>();

        Assert.Equal(string.Empty, cut.Markup.Trim());
    }

    /// <summary>
    /// IStrategyLibrary.All count is rendered into the Library tab label
    /// (`Library (N)`). With three seeds, the count visible on the tab is 3.
    /// </summary>
    [Fact]
    public void StrategyModal_LibraryCount_ReflectsLibrarySize()
    {
        var seeds = StrategyCatalogue.AllSpecs().Take(3).ToList();
        var (ctx, _, _, _, bus) = BuildContext(seededLibrary: seeds);

        var cut = OpenModal(ctx, bus);

        var libraryTab = cut.Find("button#tab-library");
        Assert.Contains("Library (3)", libraryTab.TextContent);
    }

    /// <summary>
    /// INVERTED 2026-08-01. This test used to assert that a recommendation banner appeared for a
    /// known symbol. That banner is gone: the terminal no longer picks a strategy for the user, and
    /// the picker behind it returned a Cipher-B variant on every branch — the component this
    /// project's own research falsified. The test now guards the opposite, so the banner cannot
    /// return without a deliberate decision.
    /// </summary>
    [Fact]
    public void StrategyModal_ShowsNoRecommendationBanner_EvenForAKnownSymbol()
    {
        var seeds = new[] { PickSeed() };
        var state = WorkspaceState.Initial with
        {
            Identity = WorkspaceState.Initial.Identity with
            {
                Symbol = "BTC/USDT",
                Timeframe = "1d",
            },
            Data = TimeSeriesBuffer<Ohlcv>.Empty,
        };
        var (ctx, _, _, _, bus) = BuildContext(seededLibrary: seeds, state: state);

        var cut = OpenModal(ctx, bus);

        var paragraphs = cut.FindAll("p");
        Assert.DoesNotContain(paragraphs, p =>
            p.TextContent.Contains("recommended", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// With no symbol in the workspace, the recommendation banner is
    /// suppressed (its render condition requires a non-empty symbol).
    /// </summary>
    [Fact]
    public void StrategyModal_NoSymbol_SuppressesRecommendation()
    {
        var seeds = new[] { PickSeed() };
        var (ctx, _, _, _, bus) = BuildContext(seededLibrary: seeds);

        var cut = OpenModal(ctx, bus);

        var paragraphs = cut.FindAll("p");
        Assert.DoesNotContain(paragraphs, p =>
            p.TextContent.Contains("recommended v23 long", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The empty library is now the SHIPPING state, not an edge case — nothing is seeded on first
    /// launch. So the empty state has to do real work: name the situation, explain that it is
    /// intentional, and give both routes out (build one, or import a file). A bare "nothing here"
    /// leaves a first-run user with no idea what to do.
    /// </summary>
    [Fact]
    public void StrategyModal_EmptyLibrary_ExplainsItselfAndOffersBothRoutes()
    {
        var (ctx, _, _, _, bus) = BuildContext();

        var cut = OpenModal(ctx, bus);

        var heading = cut.Find("#library-empty-heading");
        Assert.Contains("empty", heading.TextContent, StringComparison.OrdinalIgnoreCase);
        // Focusable, so a screen reader can be moved to the explanation.
        Assert.Equal("-1", heading.GetAttribute("tabindex"));

        Assert.Contains("Build Setup", cut.Markup);
        Assert.Contains("Import a strategy file", cut.Markup);
    }

    /// <summary>
    /// The import form is the documented way into the library, so it is present in both states —
    /// an empty library and a populated one — with a labelled file input and a paste box.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StrategyModal_ImportForm_IsAlwaysAvailable(bool librarySeeded)
    {
        var (ctx, _, _, _, bus) = BuildContext(
            seededLibrary: librarySeeded ? new[] { PickSeed() } : null);

        var cut = OpenModal(ctx, bus);

        Assert.NotNull(cut.Find("#strategy-import-file"));
        Assert.NotNull(cut.Find("#strategy-import-paste"));
        Assert.Contains("never overwrites a strategy you already have", cut.Markup);
    }

    /// <summary>
    /// The import form, driven end to end: paste a bundle, press Import, and the strategy is in
    /// the library with its evidence intact and the outcome announced in a live region.
    /// </summary>
    [Fact]
    public void StrategyModal_PastingABundleAndPressingImport_AddsTheStrategy()
    {
        var (ctx, _, lib, _, bus) = BuildContext();
        var incoming = PickSeed() with
        {
            Id = "import.me",
            Name = "Imported spec",
            Provenance = new StrategyProvenance(
                StrategyEvidenceLevel.WalkForward, "BTC daily", "walk-forward", "held up in both halves"),
        };
        string json = StrategyBundleService.Write(
            new[] { incoming }, "test", "test-cat", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));

        var cut = OpenModal(ctx, bus);
        cut.Find("#strategy-import-paste").Change(json);
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Import").Click();

        var saved = Assert.Single(lib.All);
        Assert.Equal("Imported spec", saved.Name);
        Assert.Equal(StrategyEvidenceLevel.WalkForward, saved.Provenance!.Evidence);

        var status = cut.FindAll("[role=status]").Select(e => e.TextContent).ToList();
        Assert.Contains(status, s => s.Contains("1 strategy imported"));
    }

    /// <summary>
    /// A bad paste is an ordinary event — an announced message, an untouched library, no crash.
    /// </summary>
    [Fact]
    public void StrategyModal_ImportingRubbish_SaysSoAndChangesNothing()
    {
        var existing = PickSeed();
        var (ctx, _, lib, _, bus) = BuildContext(seededLibrary: new[] { existing });

        var cut = OpenModal(ctx, bus);
        cut.Find("#strategy-import-paste").Change("this is not a strategy file");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Import").Click();

        Assert.Single(lib.All);
        var status = cut.FindAll("[role=status]").Select(e => e.TextContent).ToList();
        Assert.Contains(status, s => s.Contains("Import failed"));
    }

    /// <summary>
    /// Every library row states its evidence, including "Not recorded" for a user-built spec.
    /// A table where tested and untested strategies look identical is the implied endorsement
    /// the 2026-08-01 split removed, just in a quieter form.
    /// </summary>
    [Fact]
    public void StrategyModal_LibraryRows_StateTheirEvidence()
    {
        var tested = PickSeed() with
        {
            Id = "test.tested",
            Provenance = new StrategyProvenance(
                StrategyEvidenceLevel.Falsified, "BTC daily", "walk-forward",
                "tested and it did not work"),
        };
        var userBuilt = PickSeed() with { Id = "test.user-built", Provenance = null };
        var (ctx, _, _, _, bus) = BuildContext(seededLibrary: new[] { tested, userBuilt });

        var cut = OpenModal(ctx, bus);

        Assert.Contains("Falsified", cut.Markup);
        Assert.Contains("tested and it did not work", cut.Markup);
        Assert.Contains("Not recorded", cut.Markup);
    }
}
