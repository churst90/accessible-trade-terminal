using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Tests;

/// <summary>
/// Pins the HTF pre-warm contract on <see cref="ConfigurableStrategy.Initialize"/>:
/// <list type="bullet">
///   <item>Every unique <c>(Timeframe, IndicatorCode)</c> pair in the condition tree triggers one
///         <see cref="IMultiTimeframeDataService.PrewarmIndicatorAsync"/> call (fire-and-forget).</item>
///   <item>Every unique HTF timeframe (even on price-only leaves) triggers one
///         <see cref="IMultiTimeframeDataService.GetBarsAsync"/> call so the sync evaluator can
///         read bars on the hot path without awaiting.</item>
///   <item>Specs with no HTF leaves leave the prewarm gate <c>IsPrewarmComplete=true</c> from
///         the first bar, so <see cref="ConfigurableStrategy.OnBar"/> never blocks.</item>
/// </list>
/// Prior to this being wired, every HTF leaf on a freshly-added strategy silently read stale /
/// missing data for the first several bars (the evaluator's degraded-HTF log warning) and the
/// strategy never fired until the user's manual chart activity happened to populate the cache.
/// </summary>
public sealed class ConfigurableStrategyPrewarmTests
{
    [Fact]
    public void Initialize_fires_prewarm_per_unique_timeframe_indicator_pair()
    {
        // Tree with three distinct HTF leaves:
        //   (1h, TEST)   (4h, TEST)   (4h, OTHER)
        // Duplicate pair (1h, TEST) should collapse to one call.
        var leaves = new List<ConditionNode>
        {
            new ConditionLeaf("a", "TEST.Value",  LeafOperator.GreaterThan, 0, Timeframe: "1h"),
            new ConditionLeaf("b", "TEST.Value",  LeafOperator.GreaterThan, 0, Timeframe: "1h"),
            new ConditionLeaf("c", "TEST.Value",  LeafOperator.GreaterThan, 0, Timeframe: "4h"),
            new ConditionLeaf("d", "OTHER.Value", LeafOperator.GreaterThan, 0, Timeframe: "4h"),
        };
        var spec = BuildSpec(new ConditionGroup("root", LogicOperator.And, leaves));

        var mtf = new RecordingMtf();
        var strategy = new ConfigurableStrategy(
            spec,
            new StubEvaluator(),
            new StubResolver(),
            new StubCatalog(),
            new StubEventBus(),
            instanceId: "test",
            mtf: mtf);

        strategy.Initialize(Array.Empty<Ohlcv>(), BuildState(), new Dictionary<string, object>());

        // One PrewarmIndicatorAsync per unique (tf, code) pair.
        var indicatorCalls = mtf.PrewarmCalls
            .Select(c => (c.Timeframe, c.IndicatorCode))
            .ToHashSet();
        Assert.Contains(("1h", "TEST"),  indicatorCalls);
        Assert.Contains(("4h", "TEST"),  indicatorCalls);
        Assert.Contains(("4h", "OTHER"), indicatorCalls);
        Assert.Equal(3, indicatorCalls.Count);

        // One GetBarsAsync per unique HTF timeframe.
        var barTimeframes = mtf.BarCalls.Select(c => c.Timeframe).ToHashSet();
        Assert.Contains("1h", barTimeframes);
        Assert.Contains("4h", barTimeframes);
        Assert.Equal(2, barTimeframes.Count);
    }

    [Fact]
    public void Initialize_with_no_htf_leaves_leaves_prewarm_gate_open()
    {
        var spec = BuildSpec(new ConditionLeaf("active-tf", "TEST.Value", LeafOperator.Fired));
        var mtf = new RecordingMtf();
        var strategy = new ConfigurableStrategy(
            spec, new StubEvaluator(), new StubResolver(),
            new StubCatalog(), new StubEventBus(),
            instanceId: "test", mtf: mtf);

        strategy.Initialize(Array.Empty<Ohlcv>(), BuildState(), new Dictionary<string, object>());

        Assert.Empty(mtf.PrewarmCalls);
        Assert.Empty(mtf.BarCalls);
        Assert.True(strategy.IsPrewarmComplete);
    }

    [Fact]
    public void Initialize_with_null_mtf_tolerates_htf_leaf_without_throwing()
    {
        // A strategy may be constructed without IMultiTimeframeDataService (e.g. tests) —
        // Initialize must not throw and must leave the gate open so OnBar can run.
        var spec = BuildSpec(new ConditionLeaf("htf", "TEST.Value", LeafOperator.GreaterThan, 0, Timeframe: "1h"));
        var strategy = new ConfigurableStrategy(
            spec, new StubEvaluator(), new StubResolver(),
            new StubCatalog(), new StubEventBus(),
            instanceId: "test", mtf: null);

        strategy.Initialize(Array.Empty<Ohlcv>(), BuildState(), new Dictionary<string, object>());
        Assert.True(strategy.IsPrewarmComplete);
    }

    [Fact]
    public async Task IsPrewarmComplete_flips_true_only_after_every_prewarm_task_completes()
    {
        var spec = BuildSpec(new ConditionLeaf("htf", "TEST.Value", LeafOperator.GreaterThan, 0, Timeframe: "1h"));
        var mtf = new RecordingMtf { HoldPrewarm = true };
        var strategy = new ConfigurableStrategy(
            spec, new StubEvaluator(), new StubResolver(),
            new StubCatalog(), new StubEventBus(),
            instanceId: "test", mtf: mtf);

        strategy.Initialize(Array.Empty<Ohlcv>(), BuildState(), new Dictionary<string, object>());

        Assert.False(strategy.IsPrewarmComplete);

        // Release every held prewarm task and wait until each one has transitioned to a
        // completed state (rather than just "signaled") — the strategy's gate samples
        // Task.IsCompleted on every call, and a TCS.SetResult does not make the task
        // transition synchronously when there are post-set continuations.
        await mtf.ReleaseAllAsync();

        Assert.True(strategy.IsPrewarmComplete);
    }

    // ── Fixtures ────────────────────────────────────────────────────────────

    private static WorkspaceState BuildState() => WorkspaceState.Initial with
    {
        Identity = new ChartIdentity("Spot", "binance", "BTC/USDT", "5m"),
    };

    private static StrategySpec BuildSpec(ConditionNode conditions) =>
        new("spec.test", "Test", "desc", OrderSide.Buy,
            conditions,
            new RiskPlan(
                Stop:    new StopSource(StopSourceKind.PercentOfPrice, PercentValue: 1.0),
                TpLadder: Array.Empty<TpLadderRung>(),
                Sizing:  new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005),
                Entry:   new EntryTrigger(EntryTriggerKind.Immediate)),
            StrategyExecutionMode.Suggestion);

    private sealed class RecordingMtf : IMultiTimeframeDataService
    {
        public List<(string Market, string Provider, string Symbol, string Timeframe, string IndicatorCode)> PrewarmCalls { get; } = new();
        public List<(string Market, string Provider, string Symbol, string Timeframe, int Count)> BarCalls { get; } = new();
        public bool HoldPrewarm { get; set; }
        private readonly List<Task> _heldReturned = new();
        private readonly List<TaskCompletionSource<bool>> _heldSources = new();

        public Task<IReadOnlyList<Ohlcv>> GetBarsAsync(string market, string provider, string symbol, string timeframe, int count)
        {
            BarCalls.Add((market, provider, symbol, timeframe, count));
            if (!HoldPrewarm) return Task.FromResult((IReadOnlyList<Ohlcv>)Array.Empty<Ohlcv>());
            var tcs = new TaskCompletionSource<IReadOnlyList<Ohlcv>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _heldSources.Add(new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
            // We reuse the returned task as the held task so ReleaseAllAsync can await its completion.
            _heldReturned.Add(tcs.Task);
            _pendingBarTcs.Add(tcs);
            return tcs.Task;
        }

        private readonly List<TaskCompletionSource<IReadOnlyList<Ohlcv>>> _pendingBarTcs = new();

        public IReadOnlyList<Ohlcv> GetCachedBars(string provider, string symbol, string timeframe)
            => Array.Empty<Ohlcv>();

        public void Clear() { }

        public Task PrewarmIndicatorAsync(string market, string provider, string symbol, string timeframe,
            string indicatorCode, Dictionary<string, object> parameters, int count)
        {
            PrewarmCalls.Add((market, provider, symbol, timeframe, indicatorCode));
            if (!HoldPrewarm) return Task.CompletedTask;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _heldSources.Add(tcs);
            _heldReturned.Add(tcs.Task);
            return tcs.Task;
        }

        public Dictionary<string, double[]>? GetCachedIndicator(string provider, string symbol, string timeframe, string indicatorCode)
            => null;

        public async Task ReleaseAllAsync()
        {
            foreach (var tcs in _heldSources) tcs.TrySetResult(true);
            foreach (var tcs in _pendingBarTcs) tcs.TrySetResult(Array.Empty<Ohlcv>());
            // Yield until the held tasks have actually transitioned to completed — TrySetResult
            // queues the continuation; the task's IsCompleted isn't guaranteed true until the
            // scheduler runs it. A single Task.WhenAll awaits that transition.
            await Task.WhenAll(_heldReturned);
            _heldSources.Clear();
            _heldReturned.Clear();
            _pendingBarTcs.Clear();
        }
    }

    private sealed class StubCatalog : ISignalCatalog
    {
        public IReadOnlyList<SignalDescriptor> All { get; } = new[]
        {
            new SignalDescriptor("TEST.Value",  "TEST",  "Value", SignalKind.Line, "Test Value"),
            new SignalDescriptor("OTHER.Value", "OTHER", "Value", SignalKind.Line, "Other Value"),
        };
        public SignalDescriptor? GetById(string id) => All.FirstOrDefault(d => d.Id == id);
        public IReadOnlyList<SignalDescriptor> GetForIndicator(string code)
            => All.Where(d => d.IndicatorCode == code).ToList();
        public void Refresh() { }
    }

    private sealed class StubEvaluator : IConditionEvaluator
    {
        public string? LastDegradation => null;
        public ConditionEvaluation Evaluate(ConditionNode root, IReadOnlyList<Ohlcv> history, WorkspaceState state)
            => new(false, new Dictionary<string, bool>(), 0, 0);
    }

    private sealed class StubResolver : IRiskPlanResolver
    {
        public ResolvedRiskPlan? Resolve(RiskPlan plan, OrderSide side, IReadOnlyList<Ohlcv> history, WorkspaceState state)
            => null;
    }

    private sealed class StubEventBus : IEventBus
    {
        public void Publish<T>(T evt) { }
        public IDisposable Subscribe<T>(Action<T> handler) => new Disposable();
        public IObservable<T> AsObservable<T>() => System.Reactive.Linq.Observable.Empty<T>();
        public IDisposable SubscribeCoalesced<T>(Action<T> handler, TimeSpan quietWindow) => new Disposable();
        public IDisposable SubscribeSampled<T>(Action<T> handler, TimeSpan window) => new Disposable();
        private sealed class Disposable : IDisposable { public void Dispose() { } }
    }
}
