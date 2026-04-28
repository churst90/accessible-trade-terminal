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
        BuiltInStrategySeeds.GetAllSeeds()
            .First(s => s.Id == BuiltInStrategySeeds.LongV23pCipherBPivotsId);

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
        var seeds = BuiltInStrategySeeds.GetAllSeeds().Take(3).ToList();
        var (ctx, _, _, _, bus) = BuildContext(seededLibrary: seeds);

        var cut = OpenModal(ctx, bus);

        var libraryTab = cut.Find("button#tab-library");
        Assert.Contains("Library (3)", libraryTab.TextContent);
    }

    /// <summary>
    /// With BTC/USDT 1d in the workspace state and an empty bar list (under
    /// the 565 classifier threshold), the symbol-string heuristic runs.
    /// The recommendation banner displays the symbol.
    /// </summary>
    [Fact]
    public void StrategyModal_RecommendationBanner_ShowsForKnownSymbol()
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
        Assert.Contains(paragraphs, p =>
            p.TextContent.Contains("BTC/USDT", StringComparison.Ordinal) &&
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
    /// Empty library renders the "No saved strategies yet" empty-state copy.
    /// Guards the no-results branch from regressing during library refactors.
    /// </summary>
    [Fact]
    public void StrategyModal_EmptyLibrary_ShowsEmptyState()
    {
        var (ctx, _, _, _, bus) = BuildContext();

        var cut = OpenModal(ctx, bus);

        Assert.Contains("No saved strategies yet", cut.Markup);
    }
}
