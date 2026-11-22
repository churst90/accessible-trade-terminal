using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Tests;

/// <summary>
/// A level source that throws is still skipped, but no longer silently (audit, archive 12682;
/// fixed 2026-11-22). <c>LevelService</c> had <c>catch { continue; }</c>, so a stop "below
/// support" whose support source threw resolved to nothing and the strategy skipped the setup
/// exactly as if the market had no support. Now the service records which source failed, the
/// resolver says it, and the strategy speaks it once.
/// </summary>
public sealed class LevelFailureSpokenTests
{
    private sealed class Source : ILevelProvider
    {
        private readonly Func<IReadOnlyList<PriceLevel>> _levels;
        public Source(string id, Func<IReadOnlyList<PriceLevel>> levels) { SourceId = id; _levels = levels; }
        public string SourceId { get; }
        public IReadOnlyList<PriceLevel> GetLevels(IReadOnlyList<Ohlcv> history, WorkspaceState state) => _levels();
    }

    private static Source Throwing() => new("cipher_sr", () => throw new InvalidOperationException("boom"));

    private static Ohlcv[] Bars(int n)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, n)
            .Select(i => new Ohlcv(start.AddHours(i), 100, 101, 99, 100, 1000)).ToArray();
    }

    private static RiskPlan BelowSupport() => new(
        Stop: new StopSource(StopSourceKind.BelowSupport),
        TpLadder: new[] { new TpLadderRung(TargetSourceKind.RiskRewardMultiple, Multiple: 2, ClosePortion: 1) },
        Sizing: new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005),
        Entry: new EntryTrigger(EntryTriggerKind.Immediate),
        MinRewardRiskRatio: 1);

    [Fact]
    public void The_service_names_the_source_that_threw_and_still_uses_the_others()
    {
        var svc = new LevelService(new ILevelProvider[]
        {
            Throwing(),
            new Source("drawn", () => new[] { new PriceLevel(95, LevelKind.Support) }),
        });

        var all = svc.GetAllLevels(Bars(3), WorkspaceState.Initial);

        Assert.Equal(95.0, Assert.Single(all).Price);
        Assert.Equal("the Cipher SR levels (boom)", svc.LastFailure);
    }

    [Fact]
    public void A_query_where_every_source_answers_clears_the_failure()
    {
        bool fail = true;
        var svc = new LevelService(new ILevelProvider[]
        {
            new Source("swing", () => fail ? throw new InvalidOperationException("x") : Array.Empty<PriceLevel>()),
        });
        svc.GetAllLevels(Bars(3), WorkspaceState.Initial);
        Assert.NotNull(svc.LastFailure);

        fail = false;
        svc.GetAllLevels(Bars(3), WorkspaceState.Initial);
        Assert.Null(svc.LastFailure);
    }

    [Fact]
    public void The_resolver_says_why_a_stop_below_support_could_not_be_placed()
    {
        var resolver = new RiskPlanResolver(new LevelService(new ILevelProvider[] { Throwing() }));

        var plan = resolver.Resolve(BelowSupport(), OrderSide.Buy, Bars(3), WorkspaceState.Initial, null, out var whyNot);

        Assert.Null(plan);
        Assert.Equal("no stop: the Cipher SR levels (boom) failed", whyNot);
    }

    [Fact]
    public void No_support_at_all_is_not_a_failure()
    {
        // The control: a market with no support is an ordinary refusal, said by nobody.
        var resolver = new RiskPlanResolver(new LevelService(new ILevelProvider[]
        {
            new Source("drawn", () => Array.Empty<PriceLevel>()),
        }));

        var plan = resolver.Resolve(BelowSupport(), OrderSide.Buy, Bars(3), WorkspaceState.Initial, null, out var whyNot);

        Assert.Null(plan);
        Assert.Null(whyNot);
    }

    [Fact]
    public void The_strategy_says_the_skipped_setup_once_not_every_bar()
    {
        var bus = new RecordingBus();
        var spec = new StrategySpec("spec.levels", "Support Stop", "desc", OrderSide.Buy,
            new ConditionLeaf("a", "TEST.Value", LeafOperator.GreaterThan, 0),
            BelowSupport(), StrategyExecutionMode.Suggestion);
        var strategy = new ConfigurableStrategy(spec, new AlwaysTrue(),
            new RiskPlanResolver(new LevelService(new ILevelProvider[] { Throwing() })),
            new Catalog(), bus, instanceId: "test", mtf: null, liveWarmupBars: 0);
        var history = Bars(5);
        var state = WorkspaceState.Initial with { Identity = new ChartIdentity("Spot", "binance", "BTC/USDT", "1h") };
        strategy.Initialize(history, state, new Dictionary<string, object>());

        for (int i = 0; i < 3; i++) Assert.Null(strategy.OnBar(history[^1], history, state));

        var said = bus.Published.OfType<Core.Models.FeedbackRequestEvent>()
            .Where(e => e.Message?.Contains("setup skipped") == true).ToList();
        Assert.Equal("Strategy 'Support Stop': setup skipped, no stop: the Cipher SR levels (boom) failed.",
            Assert.Single(said).Message);
    }

    private sealed class AlwaysTrue : IConditionEvaluator
    {
        public string? LastDegradation => null;
        public ConditionEvaluation Evaluate(ConditionNode root, IReadOnlyList<Ohlcv> history, WorkspaceState state)
            => new(true, new Dictionary<string, bool> { ["a"] = true }, 1, 1);
    }

    private sealed class Catalog : ISignalCatalog
    {
        public IReadOnlyList<SignalDescriptor> All { get; } = new[]
        {
            new SignalDescriptor("TEST.Value", "TEST", "Value", SignalKind.Line, "Test Value"),
        };
        public SignalDescriptor? GetById(string id) => All.FirstOrDefault(d => d.Id == id);
        public IReadOnlyList<SignalDescriptor> GetForIndicator(string code) => All.Where(d => d.IndicatorCode == code).ToList();
        public void Refresh() { }
    }

    private sealed class RecordingBus : IEventBus
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
