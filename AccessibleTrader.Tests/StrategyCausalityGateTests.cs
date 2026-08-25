using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Scripting;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Trading;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// The causality gate for script STRATEGIES.
///
/// <para>
/// Scripted indicators got one in the 2026-08-25 sandbox audit: probed at registration, and any
/// component that moved is not offered to the strategy builder. A strategy never passes through
/// <c>SignalCatalog</c>, so that gate does not touch it — and a strategy is the half of the
/// scripting surface that places orders. The probe compares the ORDERS instead, which is the only
/// output a strategy has.
/// </para>
///
/// <para>
/// Every case below is driven twice on purpose: once against the probe directly, and once through
/// <c>CompileStrategyAsync</c>. Two of the blocker-6 tests stayed green when the engine's call to
/// the collaborator was deleted; testing only the probe would repeat that mistake, since a gate
/// nothing calls is not a gate.
/// </para>
/// </summary>
public class StrategyCausalityGateTests
{
    // ── The strategies under test ────────────────────────────────────────────────────────────

    private abstract class ProbeStrategyBase : ITradingStrategy
    {
        public abstract string Id { get; }
        public string Name => Id;
        public string Description => Id;
        public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
        public IReadOnlyList<StrategyParameter> Parameters => Array.Empty<StrategyParameter>();

        protected WorkspaceState State = WorkspaceState.Initial;

        public virtual void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state,
            IDictionary<string, object> parameterValues) => State = state;

        public abstract StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state);
        public void OnOrderFilled(OrderUpdate fill) { }
        public void OnStop() { }
        public StrategyMetrics GetMetrics() => new(0, 0, 0, 0, 0, 0);

        protected static StrategySignal Buy(double price) =>
            new(OrderSide.Buy, OrderType.Market, 1.0, null, price * 0.99, price * 1.02, "probe", 0.5);
    }

    /// <summary>Decides from the last two bars it was handed. Nothing else.</summary>
    private sealed class CausalStrategy : ProbeStrategyBase
    {
        public override string Id => "PROBE_CAUSAL";
        public override StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            if (history.Count < 2) return null;
            return newBar.Close > history[^2].Close * 1.002 ? Buy(newBar.Close) : null;
        }
    }

    /// <summary>
    /// The one that matters: <c>state.Data</c> holds the WHOLE series from the first OnBar call,
    /// so the next bar is one index away and a backtest pays for reading it.
    /// </summary>
    private sealed class LookaheadStrategy : ProbeStrategyBase
    {
        public override string Id => "PROBE_LOOKAHEAD";
        public override StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            int next = history.Count;                       // the bar AFTER this one
            if (next >= state.Data.Count) return null;
            return state.Data[next].Close > newBar.Close ? Buy(newBar.Close) : null;
        }
    }

    /// <summary>
    /// Anchored to the start of the array rather than to a date — the shape a Pine port arrives
    /// in, where <c>bar_index</c> becomes a count of bars loaded. Nothing about it reads the
    /// future, so only the suffix sweep can catch it.
    /// </summary>
    private sealed class IndexAnchoredStrategy : ProbeStrategyBase
    {
        public override string Id => "PROBE_INDEX_ANCHORED";
        public override StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
            => history.Count % 50 == 0 ? Buy(newBar.Close) : null;
    }

    /// <summary>Consults something that is not the bars.</summary>
    private sealed class NonDeterministicStrategy : ProbeStrategyBase
    {
        private readonly Random _rng = new();
        public override string Id => "PROBE_RANDOM";
        public override StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
            => _rng.NextDouble() > 0.5 ? Buy(newBar.Close) : null;
    }

    /// <summary>Emits nothing at all — the case where the probe establishes nothing.</summary>
    private sealed class SilentStrategy : ProbeStrategyBase
    {
        public override string Id => "PROBE_SILENT";
        public override StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
            => null;
    }

    // ── The probe ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_causal_strategy_passes_and_is_actually_exercised()
    {
        var report = ScriptStrategyCausalityProbe.Probe(new CausalStrategy());

        Assert.False(report.Refused, string.Join("\n", report.Findings));
        Assert.Empty(report.Findings);

        // The load-bearing half. A strategy that emitted nothing would ALSO be "not refused", so
        // without this the pass proves only that the probe ran.
        Assert.True(report.SignalsSeen > 0,
            "The causal control emitted no orders on the check series, so every refusal below "
          + "could be an artefact of a probe that exercises nothing.");
        Assert.Empty(report.Notes);
    }

    [Fact]
    public void A_strategy_that_reads_the_next_bar_is_refused()
    {
        var report = ScriptStrategyCausalityProbe.Probe(new LookaheadStrategy());

        Assert.True(report.Refused);
        Assert.Contains(report.Findings, f => f.Contains("decides differently", StringComparison.Ordinal));
        Assert.Contains(report.Findings, f => f.Contains("state.Data", StringComparison.Ordinal));
    }

    [Fact]
    public void A_strategy_anchored_to_the_start_of_the_array_is_refused()
    {
        var report = ScriptStrategyCausalityProbe.Probe(new IndexAnchoredStrategy());

        Assert.True(report.Refused);
        Assert.Contains(report.Findings, f => f.Contains("older bars", StringComparison.Ordinal));
    }

    /// <summary>
    /// Reported as what it is. A random strategy differs between two runs of the SAME bars, which
    /// the prefix sweep would also see — and would report as reading the future, which is a
    /// different accusation and the wrong one. The determinism check runs first for that reason.
    /// </summary>
    [Fact]
    public void A_strategy_that_consults_a_random_number_is_refused_as_non_deterministic()
    {
        var report = ScriptStrategyCausalityProbe.Probe(new NonDeterministicStrategy());

        Assert.True(report.Refused);
        Assert.Contains(report.Findings, f => f.Contains("two different answers", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Findings, f => f.Contains("decides differently", StringComparison.Ordinal));
    }

    /// <summary>
    /// Silence is not evidence. A strategy that never fires is not refused — there is nothing to
    /// refuse — but the author is told that nothing was established, rather than being handed a
    /// clean bill it did not earn.
    /// </summary>
    [Fact]
    public void A_strategy_that_never_fires_is_not_refused_but_is_not_called_clean_either()
    {
        var report = ScriptStrategyCausalityProbe.Probe(new SilentStrategy());

        Assert.False(report.Refused);
        Assert.Equal(0, report.SignalsSeen);
        Assert.Contains(report.Notes, n => n.Contains("could not be established", StringComparison.Ordinal));
    }

    /// <summary>
    /// Each run gets a fresh instance. If it did not, the strategy under test would carry state
    /// from the 400-bar run into the 150-bar one and every comparison after the first would be
    /// meaningless — while still passing, for the causal control.
    /// </summary>
    [Fact]
    public void Every_run_gets_a_fresh_instance()
    {
        InstanceCountingStrategy.Constructed = 0;
        _ = ScriptStrategyCausalityProbe.Probe(new InstanceCountingStrategy());

        // Two flavours × (2 full runs + 2 prefix + 2 suffix) = 12, and the prototype handed in is
        // never driven itself. Pinned as "more than one per flavour" rather than exactly 12 so
        // adding a sweep length does not fail a test about instance freshness.
        Assert.True(InstanceCountingStrategy.Constructed >= 6,
            $"only {InstanceCountingStrategy.Constructed} instances were built — runs are sharing state");
    }

    private sealed class InstanceCountingStrategy : ProbeStrategyBase
    {
        public static int Constructed;
        public InstanceCountingStrategy() => System.Threading.Interlocked.Increment(ref Constructed);
        public override string Id => "PROBE_COUNT";
        public override StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
            => null;
    }

    /// <summary>
    /// The gate runs inside <c>CompileStrategyAsync</c>, which auto-load calls once per armed
    /// script at app start. A strategy that is quadratic in the bars it was handed would turn that
    /// into a startup hang — a worse bug than the one being caught — so the probe has a wall-clock
    /// budget. Running out is a NOTE saying what was not established: not a pass, and not a
    /// refusal, because an unfinished check has not found anything.
    /// </summary>
    [Fact]
    public void Running_out_of_budget_is_reported_as_unfinished_not_as_a_pass_or_a_refusal()
    {
        // The look-ahead strategy specifically: with no time to run, even the one the probe would
        // certainly catch must come back un-refused rather than accused on no evidence.
        var report = ScriptStrategyCausalityProbe.Probe(new LookaheadStrategy(), budget: TimeSpan.Zero);

        Assert.False(report.Refused);
        Assert.Empty(report.Findings);
        Assert.Contains(report.Notes, n => n.Contains("ran out of its", StringComparison.Ordinal));

        // …and the same strategy, given the real budget, IS refused. Without this the test above
        // would pass against a probe that had simply stopped working.
        Assert.True(ScriptStrategyCausalityProbe.Probe(new LookaheadStrategy()).Refused);
    }

    /// <summary>
    /// The budget has to stop a probe that is already RUNNING, not only one that starts too late.
    ///
    /// <para>
    /// A zero budget short-circuits at the very first check and proves nothing about the ones
    /// inside — the first draft of the test above did exactly that, and stayed green with the
    /// prefix loop's check deleted. This one is slow enough per bar that the two full runs
    /// certainly outlast the budget, so the check that fires is one of the inner ones, and the
    /// strategy is a look-ahead so deleting that check makes the probe refuse and the test go red.
    /// </para>
    /// </summary>
    [Fact]
    public void The_budget_stops_a_probe_that_is_already_running()
    {
        var report = ScriptStrategyCausalityProbe.Probe(
            new SlowLookaheadStrategy(), budget: TimeSpan.FromMilliseconds(200));

        Assert.False(report.Refused, string.Join("\n", report.Findings));
        Assert.Contains(report.Notes, n => n.Contains("ran out of its", StringComparison.Ordinal));
    }

    /// <summary>The look-ahead strategy, with roughly a millisecond of work per bar.</summary>
    private sealed class SlowLookaheadStrategy : ProbeStrategyBase
    {
        public override string Id => "PROBE_SLOW_LOOKAHEAD";
        public override StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            // Wall-clock, not a work loop: the point is to consume the budget predictably on any
            // machine. It cannot affect the DECISION, which stays a pure function of the bars.
            System.Threading.SpinWait.SpinUntil(() => false, 1);

            int next = history.Count;
            if (next >= state.Data.Count) return null;
            return state.Data[next].Close > newBar.Close ? Buy(newBar.Close) : null;
        }
    }

    // ── The seam: the gate has to be wired into the compile door ─────────────────────────────

    /// <summary>
    /// A scripting service pointed at the REAL worker binary.
    ///
    /// <para>
    /// This used to hand out a deliberately bogus worker path, on the reasoning that
    /// <c>CompileStrategyAsync</c> loaded the strategy in-process and never went looking for one.
    /// That stopped being true when strategies moved into the sandbox worker, and a test whose
    /// path argument is "never used" is exactly the shape that keeps passing against the wrong
    /// thing. Now the two compile tests below drive the gate across the process boundary it
    /// actually runs across in production.
    /// </para>
    /// </summary>
    private static RoslynScriptingService NewScripting() =>
        new RoslynScriptingService(
            workerLauncher: new DefaultProcessLauncher(),
            workerPathResolver: ScriptWorkerPath.Resolve);

    private const string ScriptPreamble = """
        using System;
        using System.Collections.Generic;
        using AccessibleTrader.Sdk.Models;
        using AccessibleTrader.Sdk.Plugins;
        using AccessibleTrader.Sdk.Strategies;
        using AccessibleTrader.Sdk.Trading;

        namespace UserStrategies {
        """;

    private static string ScriptStrategy(string id, string onBarBody) => ScriptPreamble + $$"""
        public sealed class ScriptedStrategy : ITradingStrategy
        {
            public string Id => "{{id}}";
            public string Name => "scripted";
            public string Description => "scripted";
            public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
            public IReadOnlyList<StrategyParameter> Parameters => new StrategyParameter[0];

            public void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameterValues) { }
            public void OnOrderFilled(OrderUpdate fill) { }
            public void OnStop() { }
            public StrategyMetrics GetMetrics() => new StrategyMetrics(0, 0, 0, 0, 0, 0);

            public StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
            {
                {{onBarBody}}
            }

            private static StrategySignal Buy(double price) =>
                new StrategySignal(OrderSide.Buy, OrderType.Market, 1.0, null, price * 0.99, price * 1.02, "scripted", 0.5);
        }
        }
        """;

    /// <summary>
    /// The other half of shipping the gate: a script strategy the user had ARMED can now
    /// legitimately fail to come back after a restart, and auto-load had only ever written that
    /// to the log. To the person whose strategy is no longer running, a log line is silence — they
    /// armed it, they restarted, and as far as the terminal tells them it is live.
    /// </summary>
    [Fact]
    public async Task A_saved_script_the_gate_refuses_is_SPOKEN_at_startup_not_only_logged()
    {
        var bus = new EventBus();
        var heard = new List<FeedbackRequestEvent>();
        using var sub = bus.Subscribe<FeedbackRequestEvent>(heard.Add);

        var spec = new StrategySpec(
            Id: "spec.lookahead", Name: "Peeker", Description: "saved script",
            Side: OrderSide.Buy,
            Conditions: new ConditionGroup("roslyn-placeholder", LogicOperator.And, new List<ConditionNode>()),
            Risk: new RiskPlan(
                Stop: new StopSource(StopSourceKind.PercentOfPrice, PercentValue: 1.0),
                TpLadder: Array.Empty<TpLadderRung>(),
                Sizing: new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005),
                Entry: new EntryTrigger(EntryTriggerKind.Immediate)),
            ExecutionMode: StrategyExecutionMode.Suggestion)
        {
            IsAutoActivate = true,
            RoslynSource = "// the source is never compiled here; the refusal is the fake's",
        };

        var library = Substitute.For<IStrategyLibrary>();
        library.All.Returns(new List<StrategySpec> { spec });

        var loader = new StrategyAutoLoader(
            library,
            Substitute.For<IConfigurableStrategyFactory>(),
            Substitute.For<IStrategyEngine>(),
            Substitute.For<IAppLogger>(),
            roslyn: new RefusingScripting("Reads bar 401 at bar 400."),
            positions: null,
            eventBus: bus);

        await loader.LoadAllAsync();

        var spoken = Assert.Single(heard);
        Assert.Equal(FeedbackType.Error, spoken.Type);
        Assert.Contains("Peeker", spoken.Message ?? "", StringComparison.Ordinal);
        Assert.Contains("Reads bar 401", spoken.Message ?? "", StringComparison.Ordinal);
    }

    /// <summary>
    /// The negative half. A strategy that loads fine must say nothing — an announcement on every
    /// successful startup would be noise the user learns to ignore, which is how the one that
    /// matters gets missed.
    /// </summary>
    [Fact]
    public async Task A_library_whose_strategies_all_load_says_nothing()
    {
        var bus = new EventBus();
        var heard = new List<FeedbackRequestEvent>();
        using var sub = bus.Subscribe<FeedbackRequestEvent>(heard.Add);

        var library = Substitute.For<IStrategyLibrary>();
        library.All.Returns(new List<StrategySpec>());

        var loader = new StrategyAutoLoader(
            library,
            Substitute.For<IConfigurableStrategyFactory>(),
            Substitute.For<IStrategyEngine>(),
            Substitute.For<IAppLogger>(),
            roslyn: null, positions: null, eventBus: bus);

        await loader.LoadAllAsync();

        Assert.Empty(heard);
    }

    private sealed class RefusingScripting : IRoslynScriptingService
    {
        private readonly string _reason;
        public RefusingScripting(string reason) => _reason = reason;

        public Task<CompileStrategyResult> CompileStrategyAsync(string code) =>
            Task.FromResult(new CompileStrategyResult(false, null, new[] { _reason }));

        public Task<CompileResult> CompileIndicatorAsync(string code) =>
            throw new NotSupportedException();
        public Task<ScriptResult> ExecuteSimpleAsync(string code, List<Ohlcv> data) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task CompileStrategyAsync_refuses_a_script_that_reads_the_next_bar()
    {
        var result = await NewScripting().CompileStrategyAsync(ScriptStrategy("SCRIPT_LOOKAHEAD", """
            int next = history.Count;
            if (next >= state.Data.Count) return null;
            return state.Data[next].Close > newBar.Close ? Buy(newBar.Close) : null;
            """));

        string errors = string.Join("\n  ", result.Errors ?? Array.Empty<string>());
        Assert.False(result.Success, "A look-ahead strategy compiled and loaded. Errors: " + errors);
        Assert.Null(result.Strategy);
        Assert.Contains("decides differently", errors, StringComparison.Ordinal);
    }

    /// <summary>
    /// The control, and the one that would catch a gate that simply refuses everything. A causal
    /// script has to come back loaded, with no findings.
    /// </summary>
    [Fact]
    public async Task CompileStrategyAsync_loads_a_causal_script()
    {
        var scripting = NewScripting();
        var result = await scripting.CompileStrategyAsync(ScriptStrategy("SCRIPT_CAUSAL", """
            if (history.Count < 2) return null;
            return newBar.Close > history[history.Count - 2].Close * 1.002 ? Buy(newBar.Close) : null;
            """));

        string errors = string.Join("\n  ", result.Errors ?? Array.Empty<string>());
        Assert.True(result.Success, "A causal strategy was refused. Errors: " + errors);
        Assert.NotNull(result.Strategy);
        Assert.Empty(result.Errors ?? Array.Empty<string>());

        // And it really did cross the process boundary. Without this, a regression that dropped
        // CompileStrategyAsync back onto the in-process path would leave both compile tests here
        // perfectly green — the gate would still work, it would just be running next to the
        // credentials again.
        Assert.IsType<AccessibleTrader.Core.Services.Scripting.OutOfProcessStrategy>(result.Strategy);

        scripting.UnloadScript(result.Strategy!.Id);
    }
}
