using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Tests;

/// <summary>
/// Pins the live warmup gate (2026-06-12 audit fix 4). The backtester drops signals
/// while <c>i &lt; WarmupBars</c> so indicators settle before any trade; the live engine
/// previously had NO equivalent — a strategy on a fresh chart evaluated unstable
/// indicator values (SMA-200 at bar 20, Cipher B percentile mode at bar 50) and could
/// fire phantom setups the validated backtest would never have taken. ConfigurableStrategy
/// now refuses to evaluate live until it holds the warmup bars plus the bar being evaluated
/// (warmup + 1, the backtest's first evaluated bar), and announces the wait once.
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
        // The count is of the bars needed to evaluate (warmup + 1), so the user never hears
        // "50 of 50 bars loaded" from a strategy that is still waiting.
        Assert.Contains("10 of 51", warmupAnnouncements[0].Message);
    }

    [Fact]
    public void Live_WithExactlyWarmupBars_DoesNotEvaluate()
    {
        // 50 warmup bars are all fed-only in a backtest; the next bar is the first evaluated.
        var evaluator = new CountingEvaluator();
        var strategy = Build(evaluator, liveWarmupBars: 50);
        var history = Bars(50);
        strategy.Initialize(history, LiveState(), new Dictionary<string, object>());

        strategy.OnBar(history[^1], history, LiveState());

        Assert.Equal(0, evaluator.EvaluateCalls);
    }

    [Fact]
    public void Live_WithWarmupPlusOneBars_Evaluates()
    {
        var evaluator = new CountingEvaluator();
        var strategy = Build(evaluator, liveWarmupBars: 50);
        var history = Bars(51);
        strategy.Initialize(history, LiveState(), new Dictionary<string, object>());

        strategy.OnBar(history[^1], history, LiveState());

        Assert.Equal(1, evaluator.EvaluateCalls);
    }

    [Fact]
    public async Task Live_FirstEvaluatesAtTheSameHistoryLengthAsTheBacktest()
    {
        // The backtester itself, not a restatement of its rule: with WarmupBars 5, a strategy
        // that buys on every bar first has its signal taken at some loop index i, and the fill
        // lands on bar i + 1. OnBar at index i saw i + 1 bars of history.
        const int warmup = 5;
        var bars = Bars(12);
        var result = await new StrategyBacktester().RunAsync(
            new BuysEveryBar(), bars,
            new BacktestConfig(StartingCapital: 10_000, CommissionRate: 0, SlippagePercent: 0,
                WarmupBars: warmup, ReplayProfiles: false, AllowReverseOnSignal: false));
        int fillIndex = Array.FindIndex(bars, b => b.Date == result.Trades[0].EntryTime);
        int backtestFirstHistory = fillIndex; // signal index (fillIndex - 1) + 1 bars
        Assert.Equal(warmup + 1, backtestFirstHistory);

        var evaluator = new CountingEvaluator();
        var strategy = Build(evaluator, liveWarmupBars: warmup);
        var state = LiveState();
        strategy.Initialize(bars[..1], state, new Dictionary<string, object>());
        int liveFirstHistory = -1;
        for (int n = 1; n <= bars.Length && liveFirstHistory < 0; n++)
        {
            var history = bars[..n];
            strategy.OnBar(history[^1], history, state);
            if (evaluator.EvaluateCalls > 0) liveFirstHistory = n;
        }

        Assert.Equal(backtestFirstHistory, liveFirstHistory);
    }

    private sealed class BuysEveryBar : ITradingStrategy
    {
        public string Id => "BUYS-EVERY-BAR";
        public string Name => "Buys every bar";
        public string Description => "emits a market buy on every bar";
        public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
        public IReadOnlyList<StrategyParameter> Parameters => Array.Empty<StrategyParameter>();
        public void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameters) { }
        public StrategySignal? OnBar(Ohlcv bar, IReadOnlyList<Ohlcv> history, WorkspaceState state) =>
            new(Side: OrderSide.Buy, OrderType: OrderType.Market, Quantity: 1, LimitPrice: null,
                StopLoss: null, TakeProfit: null, Rationale: "parity fixture", Confidence: 1);
        public void OnOrderFilled(OrderUpdate fill) { }
        public void OnStop() { }
        public StrategyMetrics GetMetrics() => new(0, 0, 0, 0, 0, 0);
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
        // A directly constructed strategy with 0 has no gate. (The factory never passes 0:
        // it floors at BacktestConfig.DefaultWarmupBars; see ConfigurableStrategyFactoryWarmupTests.)
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
        public ResolvedRiskPlan? Resolve(RiskPlan plan, OrderSide side, IReadOnlyList<Ohlcv> history, WorkspaceState state, double? accountEquity = null)
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
