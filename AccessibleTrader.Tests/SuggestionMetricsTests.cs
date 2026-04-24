using System;
using System.Collections.Generic;
using AccessibleTrader.Core.Strategies.BuiltIn;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Pins the Suggestion-mode metrics contract on <see cref="BaseStrategy"/>. Before
/// this wiring, Suggestion-mode strategies (which publish signals but never receive
/// <c>OnOrderFilled</c> callbacks) reported <c>{0, 0, 0, 0, 0, 0}</c> for every
/// field of <see cref="StrategyMetrics"/> — the ActiveStrategies tab was useless
/// for anything but Auto-mode.
///
/// The fix: <see cref="BaseStrategy.OnBar"/> now wraps <c>ComputeSignal</c> with
/// theoretical-fill tracking. Each signal that carries both a Stop and a TakeProfit
/// is recorded; subsequent bars walk the position's Stop/TP against each bar's
/// High/Low (stop-priority on same-bar tie, matching <c>StrategyBacktester</c>).
/// Metrics blend real-fill (Auto) + theoretical-fill (Suggestion) counters since
/// a running instance is always one or the other, never both.
/// </summary>
public sealed class SuggestionMetricsTests
{
    [Fact]
    public void Signal_with_TP_hit_on_next_bar_registers_as_theoretical_win()
    {
        var strategy = new ProbeStrategy(nextSignal:
            new StrategySignal(OrderSide.Buy, OrderType.Market, 1.0,
                LimitPrice: null,
                StopLoss: 99.0,
                TakeProfit: 105.0,
                Rationale: "test buy",
                Confidence: 1.0));

        var history = new List<Ohlcv>();
        var entry = Bar(t: 0, o: 100, h: 100, l: 100, c: 100);
        history.Add(entry);
        strategy.OnBar(entry, history, WorkspaceState.Initial);   // signal fires @ close=100

        // Next bar spikes high enough to hit the TP at 105.
        var next = Bar(t: 1, o: 100, h: 106, l: 100, c: 104);
        history.Add(next);
        strategy.SuppressSignal();
        strategy.OnBar(next, history, WorkspaceState.Initial);

        var m = strategy.GetMetrics();
        Assert.Equal(1, m.TotalSignals);
        Assert.Equal(1, m.WinningTrades);
        Assert.Equal(1.0, m.WinRate);
        Assert.Equal(5.0, m.TotalPnL);   // 105 - 100 = 5 at qty=1
    }

    [Fact]
    public void Signal_with_Stop_hit_on_next_bar_registers_as_theoretical_loss()
    {
        var strategy = new ProbeStrategy(nextSignal:
            new StrategySignal(OrderSide.Buy, OrderType.Market, 1.0,
                LimitPrice: null,
                StopLoss: 99.0,
                TakeProfit: 105.0,
                Rationale: "test buy",
                Confidence: 1.0));

        var history = new List<Ohlcv>();
        var entry = Bar(t: 0, o: 100, h: 100, l: 100, c: 100);
        history.Add(entry);
        strategy.OnBar(entry, history, WorkspaceState.Initial);

        var next = Bar(t: 1, o: 100, h: 100, l: 98, c: 99);  // stop at 99 hit
        history.Add(next);
        strategy.SuppressSignal();
        strategy.OnBar(next, history, WorkspaceState.Initial);

        var m = strategy.GetMetrics();
        Assert.Equal(1, m.TotalSignals);
        Assert.Equal(0, m.WinningTrades);
        Assert.Equal(0.0, m.WinRate);
        Assert.Equal(-1.0, m.TotalPnL);  // 99 - 100 = -1
    }

    [Fact]
    public void Same_bar_Stop_and_TP_both_hit_closes_as_loss_by_stop_priority()
    {
        // Fast-wick bar: the signal's Stop AND TakeProfit both fall within [Low, High].
        // The Backtester and live strategies must treat this as a stop-out (conservative).
        var strategy = new ProbeStrategy(nextSignal:
            new StrategySignal(OrderSide.Buy, OrderType.Market, 1.0,
                LimitPrice: null,
                StopLoss: 99.0,
                TakeProfit: 105.0,
                Rationale: "test buy",
                Confidence: 1.0));

        var history = new List<Ohlcv>();
        var entry = Bar(t: 0, o: 100, h: 100, l: 100, c: 100);
        history.Add(entry);
        strategy.OnBar(entry, history, WorkspaceState.Initial);

        // Next bar pierces both levels — stop priority wins.
        var next = Bar(t: 1, o: 100, h: 106, l: 98, c: 100);
        history.Add(next);
        strategy.SuppressSignal();
        strategy.OnBar(next, history, WorkspaceState.Initial);

        var m = strategy.GetMetrics();
        Assert.Equal(0, m.WinningTrades);
        Assert.Equal(-1.0, m.TotalPnL);
    }

    [Fact]
    public void Signal_without_Stop_is_not_tracked_theoretically()
    {
        // A signal emitted by a pure Suggestion-mode strategy that lacks a Stop can't
        // be resolved — it'd bias the win rate toward 0. Skip tracking entirely.
        var strategy = new ProbeStrategy(nextSignal:
            new StrategySignal(OrderSide.Buy, OrderType.Market, 1.0,
                LimitPrice: null,
                StopLoss: null,
                TakeProfit: 105.0,
                Rationale: "no stop",
                Confidence: 1.0));

        var history = new List<Ohlcv>();
        var entry = Bar(t: 0, o: 100, h: 100, l: 100, c: 100);
        history.Add(entry);
        strategy.OnBar(entry, history, WorkspaceState.Initial);

        var next = Bar(t: 1, o: 100, h: 106, l: 95, c: 100);
        history.Add(next);
        strategy.SuppressSignal();
        strategy.OnBar(next, history, WorkspaceState.Initial);

        var m = strategy.GetMetrics();
        // Signal was still counted in TotalSignals, but no closed trade.
        Assert.Equal(1, m.TotalSignals);
        Assert.Equal(0, m.WinningTrades);
        Assert.Equal(0.0, m.WinRate);
        Assert.Equal(0.0, m.TotalPnL);
    }

    [Fact]
    public void Multiple_signals_aggregate_wins_and_losses()
    {
        var strategy = new ProbeStrategy();

        var history = new List<Ohlcv>();

        // Signal 1 @ t=0, TP at 105 → hits on next bar (win).
        strategy.QueueSignal(new StrategySignal(OrderSide.Buy, OrderType.Market, 1.0,
            null, 99.0, 105.0, "first", 1.0));
        var b0 = Bar(0, 100, 100, 100, 100);
        history.Add(b0); strategy.OnBar(b0, history, WorkspaceState.Initial);

        strategy.QueueSignal(null);
        var b1 = Bar(1, 100, 106, 100, 104);  // TP hit
        history.Add(b1); strategy.OnBar(b1, history, WorkspaceState.Initial);

        // Signal 2 @ t=2, stop at 199 → hits on next bar (loss).
        strategy.QueueSignal(new StrategySignal(OrderSide.Buy, OrderType.Market, 2.0,
            null, 199.0, 220.0, "second", 1.0));
        var b2 = Bar(2, 200, 200, 200, 200);
        history.Add(b2); strategy.OnBar(b2, history, WorkspaceState.Initial);

        strategy.QueueSignal(null);
        var b3 = Bar(3, 200, 200, 198, 199);  // stop hit
        history.Add(b3); strategy.OnBar(b3, history, WorkspaceState.Initial);

        var m = strategy.GetMetrics();
        Assert.Equal(2, m.TotalSignals);
        Assert.Equal(1, m.WinningTrades);
        Assert.Equal(0.5, m.WinRate);
        // win: (105 - 100) * 1 = +5 ; loss: (199 - 200) * 2 = -2 → net +3
        Assert.Equal(3.0, m.TotalPnL);
    }

    // ── Harness ─────────────────────────────────────────────────────────────

    private static Ohlcv Bar(int t, double o, double h, double l, double c)
        => new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(t), o, h, l, c, 1000);

    /// <summary>
    /// Minimal BaseStrategy subclass that emits a pre-configured signal on its
    /// first <c>ComputeSignal</c> call (or a queue of them via <c>QueueSignal</c>).
    /// </summary>
    private sealed class ProbeStrategy : BaseStrategy
    {
        private readonly Queue<StrategySignal?> _queued = new();
        private bool _nextSuppressed;

        public ProbeStrategy() { }
        public ProbeStrategy(StrategySignal? nextSignal)
        {
            _queued.Enqueue(nextSignal);
        }

        public void QueueSignal(StrategySignal? signal) => _queued.Enqueue(signal);
        public void SuppressSignal() => _nextSuppressed = true;

        public override string Id => "probe";
        public override string Name => "Probe";
        public override string Description => "Test probe";
        public override StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
        public override IReadOnlyList<StrategyParameter> Parameters { get; } = Array.Empty<StrategyParameter>();

        protected override StrategySignal? ComputeSignal(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            if (_nextSuppressed) { _nextSuppressed = false; return null; }
            return _queued.Count > 0 ? _queued.Dequeue() : null;
        }
    }
}
