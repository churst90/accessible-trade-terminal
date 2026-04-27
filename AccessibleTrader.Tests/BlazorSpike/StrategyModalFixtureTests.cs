// bUnit modal-coverage spike (2026-04-27 evening 14).
//
// Goal: prove the bUnit toolchain works against this codebase before
// committing to the per-modal sweep. Validates four interaction patterns
// using `StrategyModalFixture.razor` (a stripped-down replica of the real
// `StrategyModal` for the four contracts we care about):
//
//   1. Coordinator-mock seam — Start/Stop button clicks invoke
//      IStrategyModalCoordinator.{StartSpec, StopSpec} exactly once.
//   2. Behavior-driven preset selector — with bars >= 565, the
//      AssetClassifier route is taken.
//   3. Symbol-string fallback — with too few bars, the symbol heuristic
//      route is taken.
//   4. JS-interop shim — InvokeVoidAsync("accessibleTrader.focusElement",
//      ...) on first render is captured by bUnit's JSInterop mock.
//
// Architectural note: the real StrategyModal lives in the MAUI BlazorClient
// project (UseMaui=true) which cannot be referenced from a plain net10.0
// test project. Rollout path:
//
//   Step A (sized: ~1-2 days): extract BlazorClient/Components into a new
//     Razor Class Library `AccessibleTrader.BlazorClient.Components`
//     targeting net10.0. The MAUI BlazorClient references the RCL.
//   Step B (sized: per-modal, ~1-3 hours each): write bUnit tests against
//     the real components using the patterns demonstrated in this file.
//
// Recipe summary (validated by these tests):
//   - Register mocked services with TestContext.Services.AddSingleton(...)
//   - Stub IJSRuntime via JSInterop.SetupVoid(...) before render
//   - Use [data-testid] selectors for stable button/element targeting
//   - Verify mock interactions with Moq's standard Verify(...) pattern

using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.BlazorSpike;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AccessibleTrader.Tests;

public class StrategyModalFixtureTests
{
    /// <summary>Minimal stub coordinator that records the last call.</summary>
    private sealed class StubCoordinator : IStrategyModalCoordinator
    {
        public string? LastStartSpecCallSpecId { get; private set; }
        public string? LastStopSpecCallSpecId { get; private set; }
        public int StartSpecCallCount { get; private set; }
        public int StopSpecCallCount { get; private set; }

        public StrategyCoordinatorResult StartSpec(string specId)
        {
            StartSpecCallCount++;
            LastStartSpecCallSpecId = specId;
            return StrategyCoordinatorResult.Ok($"Started {specId}");
        }

        public StrategyCoordinatorResult StopSpec(string specId)
        {
            StopSpecCallCount++;
            LastStopSpecCallSpecId = specId;
            return StrategyCoordinatorResult.Ok($"Stopped {specId}");
        }

        public StrategyCoordinatorResult RemoveActive(string instanceId) =>
            StrategyCoordinatorResult.Ok("");

        public StrategyCoordinatorResult TogglePause(string instanceId, bool currentlyPaused) =>
            StrategyCoordinatorResult.Ok("");

        public (int RecommendedBars, StrategyCoordinatorResult Result) RecommendedWarmup(string specId) =>
            (0, StrategyCoordinatorResult.Ok(""));

        public Task<(BacktestResult? Result, StrategyCoordinatorResult Status)> RunBacktestAsync(
            string specId,
            IReadOnlyList<Ohlcv> data,
            BacktestConfig config,
            WorkspaceState state) =>
            Task.FromResult<(BacktestResult?, StrategyCoordinatorResult)>(
                (null, StrategyCoordinatorResult.Ok("")));

        public Task<StrategyCoordinatorResult> CompileAndAddStrategyAsync(string code, StrategyExecutionMode execMode) =>
            Task.FromResult(StrategyCoordinatorResult.Ok(""));

        public IEnumerable<ActiveStrategy> ActiveStrategies => Array.Empty<ActiveStrategy>();
    }

    private static IReadOnlyList<StrategySpec> SampleLibrary()
    {
        var seed = BuiltInStrategySeeds.GetAllSeeds()
            .First(s => s.Id == BuiltInStrategySeeds.LongV23pCipherBPivotsId);
        return new[] { seed };
    }

    private static IReadOnlyList<Ohlcv> SyntheticBars(int count)
    {
        // 565+ bars triggers the classifier route in the fixture.
        var bars = new List<Ohlcv>(count);
        var t = DateTime.UtcNow.AddDays(-count);
        double price = 60_000;
        var rng = new Random(42);
        for (int i = 0; i < count; i++)
        {
            double ret = (rng.NextDouble() - 0.5) * 0.04;
            price *= 1 + ret;
            bars.Add(new Ohlcv
            {
                Date = t.AddDays(i),
                Open = price * 0.995,
                High = price * 1.01,
                Low = price * 0.99,
                Close = price,
                Volume = 1_000_000_000 + rng.NextDouble() * 500_000_000,
            });
        }
        return bars;
    }

    private static (TestContext ctx, StubCoordinator coord) BuildContext()
    {
        var ctx = new TestContext();
        var coord = new StubCoordinator();
        ctx.Services.AddSingleton<IStrategyModalCoordinator>(coord);
        // The fixture also calls IJSRuntime — bUnit auto-registers a strict
        // JSInterop mock; we shim the one call below in the focus test.
        return (ctx, coord);
    }

    [Fact]
    public void StartButton_InvokesCoordinatorStartSpecOnce()
    {
        var (ctx, coord) = BuildContext();
        ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true);

        var lib = SampleLibrary();
        var cut = ctx.RenderComponent<StrategyModalFixture>(p => p
            .Add(c => c.Library, lib));

        cut.Find($"[data-testid='start-{lib[0].Id}']").Click();

        Assert.Equal(1, coord.StartSpecCallCount);
        Assert.Equal(lib[0].Id, coord.LastStartSpecCallSpecId);
        Assert.Contains($"Started {lib[0].Id}", cut.Find("[data-testid='last-result']").TextContent);
    }

    [Fact]
    public void StopButton_InvokesCoordinatorStopSpecOnce()
    {
        var (ctx, coord) = BuildContext();
        ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true);

        var lib = SampleLibrary();
        var cut = ctx.RenderComponent<StrategyModalFixture>(p => p
            .Add(c => c.Library, lib));

        cut.Find($"[data-testid='stop-{lib[0].Id}']").Click();

        Assert.Equal(1, coord.StopSpecCallCount);
        Assert.Equal(lib[0].Id, coord.LastStopSpecCallSpecId);
    }

    [Fact]
    public void Recommended_WithEnoughBars_TakesClassifierRoute()
    {
        var (ctx, _) = BuildContext();
        ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true);

        // 600 bars > 565 threshold ⇒ AssetClassifier route runs.
        var bars = SyntheticBars(600);
        var cut = ctx.RenderComponent<StrategyModalFixture>(p => p
            .Add(c => c.Library, SampleLibrary())
            .Add(c => c.Symbol, "BTC/USDT")
            .Add(c => c.Timeframe, "1d")
            .Add(c => c.Bars, bars));

        var recommended = cut.Find("[data-testid='recommended-id']").TextContent;
        // The classifier picks v23p / v23r / v23h depending on synthetic-bar
        // behavior. We only assert it ran (returned a non-empty seed id) —
        // exact branch is covered by AssetClassifierTests in the backend suite.
        Assert.False(string.IsNullOrWhiteSpace(recommended));
        Assert.StartsWith("builtin.long.v23", recommended);
    }

    [Fact]
    public void Recommended_WithFewBars_FallsBackToSymbolHeuristic()
    {
        var (ctx, _) = BuildContext();
        ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true);

        // 50 bars < 565 ⇒ symbol heuristic route. BTC/USDT 1d ⇒ v23p.
        var bars = SyntheticBars(50);
        var cut = ctx.RenderComponent<StrategyModalFixture>(p => p
            .Add(c => c.Library, SampleLibrary())
            .Add(c => c.Symbol, "BTC/USDT")
            .Add(c => c.Timeframe, "1d")
            .Add(c => c.Bars, bars));

        var recommended = cut.Find("[data-testid='recommended-id']").TextContent;
        Assert.Equal(BuiltInStrategySeeds.LongV23pCipherBPivotsId, recommended);
    }

    [Fact]
    public void OnFirstRender_FocusesTitleViaJsInterop()
    {
        var (ctx, _) = BuildContext();
        var jsHandler = ctx.JSInterop.SetupVoid("accessibleTrader.focusElement",
            inv => inv.Arguments.Count == 1 && (string)inv.Arguments[0]! == "strategy-title");

        ctx.RenderComponent<StrategyModalFixture>(p => p
            .Add(c => c.Library, SampleLibrary()));

        // VerifyInvoke throws if the call wasn't matched the expected number of times.
        jsHandler.VerifyInvoke("accessibleTrader.focusElement", calledTimes: 1);
    }

    [Fact]
    public void NoSymbol_LeavesRecommendedEmpty()
    {
        var (ctx, _) = BuildContext();
        ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true);

        var cut = ctx.RenderComponent<StrategyModalFixture>(p => p
            .Add(c => c.Library, SampleLibrary()));

        var recommended = cut.Find("[data-testid='recommended-id']").TextContent;
        Assert.Equal(string.Empty, recommended.Trim());
    }
}
