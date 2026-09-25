using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Tests;

/// <summary>
/// The causality probe compares ORDERS, and an order is more than a side and a bar.
///
/// <para>
/// Written for mutation campaign A2l (2026-09-24). Every look-ahead strategy in
/// <c>StrategyCausalityGateTests</c> leaks through its ENTRY — whether it trades on a bar — so
/// deleting the stop from the order fingerprint left the whole suite green. But the stop is where
/// a very ordinary script bug lives: <c>state.Data</c> holds the WHOLE series from the first
/// <c>OnBar</c>, so an author who writes <c>state.Data[^1]</c> meaning "the current bar" is
/// reading the last bar on the chart. Entries honest, stop clairvoyant, backtest flattering — and
/// it has to be refused like any other look-ahead.
/// </para>
/// </summary>
public class ScriptStrategyCausalityFingerprintTests
{
    private abstract class Base : ITradingStrategy
    {
        public abstract string Id { get; }
        public string Name => Id;
        public string Description => Id;
        public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
        public IReadOnlyList<StrategyParameter> Parameters => Array.Empty<StrategyParameter>();
        public void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameterValues) { }
        public abstract StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state);
        public void OnOrderFilled(OrderUpdate fill) { }
        public void OnStop() { }
        public StrategyMetrics GetMetrics() => new(0, 0, 0, 0, 0, 0);

        /// <summary>The same honest entry rule for both strategies below.</summary>
        protected static bool Enters(Ohlcv bar, IReadOnlyList<Ohlcv> history) =>
            history.Count >= 2 && bar.Close > history[^2].Close * 1.002;
    }

    /// <summary>Stop two percent under the bar it decided on. The control.</summary>
    private sealed class HonestStop : Base
    {
        public override string Id => "PROBE_HONEST_STOP";
        public override StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state) =>
            Enters(newBar, history)
                ? new StrategySignal(OrderSide.Buy, OrderType.Market, 1.0, null, newBar.Close * 0.98, newBar.Close * 1.04, "probe", 0.5)
                : null;
    }

    /// <summary>Same entries; the stop is placed from <c>state.Data[^1]</c> — the chart's LAST bar.</summary>
    private sealed class StopFromTheLastBarOnTheChart : Base
    {
        public override string Id => "PROBE_STOP_LOOKAHEAD";
        public override StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state) =>
            Enters(newBar, history)
                ? new StrategySignal(OrderSide.Buy, OrderType.Market, 1.0, null, state.Data[^1].Low * 0.98, newBar.Close * 1.04, "probe", 0.5)
                : null;
    }

    [Fact]
    public void A_strategy_whose_entries_are_honest_but_whose_stop_reads_the_future_is_refused()
    {
        var report = ScriptStrategyCausalityProbe.Probe(new StopFromTheLastBarOnTheChart());

        Assert.True(report.Refused, "A stop placed from the last bar on the chart was not refused.");
        Assert.Contains(report.Findings, f => f.Contains("decides differently", StringComparison.Ordinal));
    }

    [Fact]
    public void The_same_entries_with_an_honest_stop_pass()
    {
        // The control: the refusal above is about the stop, not about the entry rule.
        var report = ScriptStrategyCausalityProbe.Probe(new HonestStop());

        Assert.False(report.Refused, string.Join("\n", report.Findings));
        Assert.True(report.SignalsSeen > 0);
    }
}
