using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Tests;

/// <summary>
/// Strategies created for live use must wait for at least the default backtest warmup,
/// and for longer when their indicators need more history.
/// </summary>
public sealed class ConfigurableStrategyFactoryWarmupTests
{
    [Fact]
    public void AnalyzerRecommendationOfSixtyStillRequiresTwoHundredBars()
    {
        var evaluator = new CountingEvaluator();
        var strategy = Factory(evaluator, recommendedWarmup: 60).Create(BuildSpec(), "warmup-60");
        var state = LiveState();
        var history = Bars(199);
        strategy.Initialize(history, state, new Dictionary<string, object>());

        strategy.OnBar(history[^1], history, state);
        Assert.Equal(0, evaluator.EvaluateCalls);

        history = Bars(200);
        strategy.OnBar(history[^1], history, state);
        Assert.Equal(1, evaluator.EvaluateCalls);
    }

    [Fact]
    public void AnalyzerRecommendationAboveDefaultIsRespected()
    {
        var evaluator = new CountingEvaluator();
        var strategy = Factory(evaluator, recommendedWarmup: 240).Create(BuildSpec(), "warmup-240");
        var state = LiveState();
        var history = Bars(239);
        strategy.Initialize(history, state, new Dictionary<string, object>());

        strategy.OnBar(history[^1], history, state);
        Assert.Equal(0, evaluator.EvaluateCalls);

        history = Bars(240);
        strategy.OnBar(history[^1], history, state);
        Assert.Equal(1, evaluator.EvaluateCalls);
    }

    [Fact]
    public void NoAnalyzerStillRequiresTwoHundredBars()
    {
        var evaluator = new CountingEvaluator();
        var strategy = Factory(evaluator).Create(BuildSpec(), "warmup-no-analyzer");
        var state = LiveState();
        var history = Bars(199);
        strategy.Initialize(history, state, new Dictionary<string, object>());

        strategy.OnBar(history[^1], history, state);
        Assert.Equal(0, evaluator.EvaluateCalls);

        history = Bars(200);
        strategy.OnBar(history[^1], history, state);
        Assert.Equal(1, evaluator.EvaluateCalls);
    }

    [Fact]
    public void BacktestModeBypassesTheFactoryLiveWarmup()
    {
        var evaluator = new CountingEvaluator();
        var strategy = Factory(evaluator, recommendedWarmup: 240).Create(BuildSpec(), "warmup-backtest");
        var state = LiveState() with { IsBacktesting = true };
        var history = Bars(10);
        strategy.Initialize(history, state, new Dictionary<string, object>());

        strategy.OnBar(history[^1], history, state);

        Assert.Equal(1, evaluator.EvaluateCalls);
    }

    private static ConfigurableStrategyFactory Factory(
        IConditionEvaluator evaluator, int? recommendedWarmup = null) =>
        new(evaluator, new StubResolver(), new StubCatalog(), new StubEventBus(),
            warmupAnalyzer: recommendedWarmup.HasValue
                ? new FixedWarmupAnalyzer(recommendedWarmup.Value)
                : null);

    private static Ohlcv[] Bars(int count)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, count)
            .Select(i => new Ohlcv(start.AddHours(i), 100 + i, 101 + i, 99 + i, 100.5 + i, 1000))
            .ToArray();
    }

    private static WorkspaceState LiveState() => WorkspaceState.Initial with
    {
        Identity = new ChartIdentity("Spot", "binance", "BTC/USDT", "1h"),
    };

    private static StrategySpec BuildSpec() =>
        new("spec.factory-warmup", "Factory Warmup Test", "desc", OrderSide.Buy,
            new ConditionLeaf("a", "TEST.Value", LeafOperator.GreaterThan, 0),
            new RiskPlan(
                Stop: new StopSource(StopSourceKind.PercentOfPrice, PercentValue: 1.0),
                TpLadder: Array.Empty<TpLadderRung>(),
                Sizing: new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005),
                Entry: new EntryTrigger(EntryTriggerKind.Immediate)),
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

    private sealed class FixedWarmupAnalyzer(int recommendedBars) : IBacktestWarmupAnalyzer
    {
        public int RecommendedWarmup(StrategySpec spec, int floor = 50) => recommendedBars;

        public IReadOnlyList<string> ReferencedIndicators(StrategySpec spec) => Array.Empty<string>();
    }

    private sealed class StubCatalog : ISignalCatalog
    {
        public IReadOnlyList<SignalDescriptor> All { get; } = new[]
        {
            new SignalDescriptor("TEST.Value", "TEST", "Value", SignalKind.Line, "Test Value"),
        };

        public SignalDescriptor? GetById(string id) => All.FirstOrDefault(descriptor => descriptor.Id == id);
        public IReadOnlyList<SignalDescriptor> GetForIndicator(string code)
            => All.Where(descriptor => descriptor.IndicatorCode == code).ToList();
        public void Refresh() { }
    }

    private sealed class StubResolver : IRiskPlanResolver
    {
        public ResolvedRiskPlan? Resolve(
            RiskPlan plan, OrderSide side, IReadOnlyList<Ohlcv> history, WorkspaceState state, double? accountEquity = null) => null;
    }

    private sealed class StubEventBus : IEventBus
    {
        public void Publish<T>(T evt) { }
        public IDisposable Subscribe<T>(Action<T> handler) => new Disposable();
        public IObservable<T> AsObservable<T>() => System.Reactive.Linq.Observable.Empty<T>();
        public IDisposable SubscribeCoalesced<T>(Action<T> handler, TimeSpan quietWindow) => new Disposable();
        public IDisposable SubscribeSampled<T>(Action<T> handler, TimeSpan window) => new Disposable();

        private sealed class Disposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
