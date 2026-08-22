using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Pins the live warmup gate (2026-06-12 audit fix 4). The backtester drops signals
/// while <c>i &lt; WarmupBars</c> so indicators settle before any trade; the live engine
/// previously had NO equivalent — a strategy on a fresh chart evaluated unstable
/// indicator values (SMA-200 at bar 20, Cipher B percentile mode at bar 50) and could
/// fire phantom setups the validated backtest would never have taken. ConfigurableStrategy
/// now refuses to evaluate live until history depth reaches the analyzer-computed warmup,
/// and announces the wait once.
/// </summary>
public sealed class ConfigurableStrategyLiveWarmupTests
{
    [Fact]
    public void Live_WithHistoryShorterThanWarmup_DoesNotEvaluate()
    {
        var evaluator = new CountingEvaluator();
        var strategy = Build(evaluator, liveWarmupBars: 50);
        var history = Bars(10);
        strategy.Initialize(history, LiveState(), new Dictionary<string, object>());

        var signal = strategy.OnBar(history[^1], history, LiveState());

        Assert.Null(signal);
        Assert.Equal(0, evaluator.EvaluateCalls);
    }

    [Fact]
    public void Live_AnnouncesWarmupExactlyOnce()
    {
        var bus = new RecordingEventBus();
        var evaluator = new CountingEvaluator();
        var strategy = Build(evaluator, liveWarmupBars: 50, bus: bus);
        var history = Bars(10);
        strategy.Initialize(history, LiveState(), new Dictionary<string, object>());

        strategy.OnBar(history[^1], history, LiveState());
        strategy.OnBar(history[^1], history, LiveState());
        strategy.OnBar(history[^1], history, LiveState());

        var warmupAnnouncements = bus.Published
            .OfType<Core.Models.FeedbackRequestEvent>()
            .Where(e => e.Message != null && e.Message.Contains("warming up"))
            .ToList();
        Assert.Single(warmupAnnouncements);
        Assert.Contains("10 of 50", warmupAnnouncements[0].Message);
    }

    [Fact]
    public void Live_WithSufficientHistory_Evaluates()
    {
        var evaluator = new CountingEvaluator();
        var strategy = Build(evaluator, liveWarmupBars: 50);
        var history = Bars(50);
        strategy.Initialize(history, LiveState(), new Dictionary<string, object>());

        strategy.OnBar(history[^1], history, LiveState());

        Assert.Equal(1, evaluator.EvaluateCalls);
    }

    [Fact]
    public void Backtest_BypassesLiveGate()
    {
        // The backtester applies its own warmup drop (`i < warmupBars`); the live gate
        // must not stack on top of it or short replay windows would evaluate nothing.
        var evaluator = new CountingEvaluator();
        var strategy = Build(evaluator, liveWarmupBars: 50);
        var history = Bars(10);
        var btState = LiveState() with { IsBacktesting = true };
        strategy.Initialize(history, btState, new Dictionary<string, object>());

        strategy.OnBar(history[^1], history, btState);

        Assert.Equal(1, evaluator.EvaluateCalls);
    }

    [Fact]
    public void ZeroWarmup_GateDisabled()
    {
        // No analyzer in the host → factory passes 0 → gate never blocks.
        var evaluator = new CountingEvaluator();
        var strategy = Build(evaluator, liveWarmupBars: 0);
        var history = Bars(3);
        strategy.Initialize(history, LiveState(), new Dictionary<string, object>());

        strategy.OnBar(history[^1], history, LiveState());

        Assert.Equal(1, evaluator.EvaluateCalls);
    }

    // ── Fixtures ────────────────────────────────────────────────────────────

    private static ConfigurableStrategy Build(
        IConditionEvaluator evaluator, int liveWarmupBars, IEventBus? bus = null) =>
        new(BuildSpec(), evaluator, new StubResolver(), new StubCatalog(),
            bus ?? new StubEventBus(), instanceId: "test", mtf: null,
            liveWarmupBars: liveWarmupBars);

    private static Ohlcv[] Bars(int n)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, n)
            .Select(i => new Ohlcv(start.AddHours(i), 100 + i, 101 + i, 99 + i, 100.5 + i, 1000))
            .ToArray();
    }

    private static WorkspaceState LiveState() => WorkspaceState.Initial with
    {
        Identity = new ChartIdentity("Spot", "binance", "BTC/USDT", "1h"),
    };

    private static StrategySpec BuildSpec() =>
        new("spec.warmup", "Warmup Test", "desc", OrderSide.Buy,
            new ConditionLeaf("a", "TEST.Value", LeafOperator.GreaterThan, 0),
            new RiskPlan(
                Stop:    new StopSource(StopSourceKind.PercentOfPrice, PercentValue: 1.0),
                TpLadder: Array.Empty<TpLadderRung>(),
                Sizing:  new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005),
                Entry:   new EntryTrigger(EntryTriggerKind.Immediate)),
            StrategyExecutionMode.Suggestion);

    private sealed class CountingEvaluator : IConditionEvaluator
    {
        public int EvaluateCalls;
        public string? LastDegradation => null;
        public ConditionEvaluation Evaluate(ConditionNode root, IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            EvaluateCalls++;
            return new(false, new Dictionary<string, bool>(), 0, 0);
        }
    }

    private sealed class StubCatalog : ISignalCatalog
    {
        public IReadOnlyList<SignalDescriptor> All { get; } = new[]
        {
            new SignalDescriptor("TEST.Value", "TEST", "Value", SignalKind.Line, "Test Value"),
        };
        public SignalDescriptor? GetById(string id) => All.FirstOrDefault(d => d.Id == id);
        public IReadOnlyList<SignalDescriptor> GetForIndicator(string code)
            => All.Where(d => d.IndicatorCode == code).ToList();
        public void Refresh() { }
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

    private sealed class RecordingEventBus : IEventBus
    {
        public List<object> Published { get; } = new();
        public void Publish<T>(T evt) { if (evt != null) Published.Add(evt); }
        public IDisposable Subscribe<T>(Action<T> handler) => new Disposable();
        public IObservable<T> AsObservable<T>() => System.Reactive.Linq.Observable.Empty<T>();
        public IDisposable SubscribeCoalesced<T>(Action<T> handler, TimeSpan quietWindow) => new Disposable();
        public IDisposable SubscribeSampled<T>(Action<T> handler, TimeSpan window) => new Disposable();
        private sealed class Disposable : IDisposable { public void Dispose() { } }
    }
}
