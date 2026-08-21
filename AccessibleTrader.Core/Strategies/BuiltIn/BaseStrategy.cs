using System;
using System.Collections.Generic;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Core.Strategies.BuiltIn;

/// <summary>
/// Abstract base class for built-in trading strategies.
/// Tracks performance metrics automatically; subclasses implement
/// <see cref="ComputeSignal"/>, and the base wraps it with real-fill and
/// theoretical-fill bookkeeping so <see cref="GetMetrics"/> is meaningful
/// for both Auto-mode (driven by real <see cref="OnOrderFilled"/> calls)
/// and Suggestion-mode (driven by theoretical fills against each emitted
/// signal's Stop / TakeProfit).
/// </summary>
public abstract class BaseStrategy : ITradingStrategy
{
    // ── Real-fill metrics state (Auto mode) ──────────────────────────────────
    private int _totalFills;       // number of OnOrderFilled calls (opens + closes)
    private int _closedTrades;     // completed round-trip trades (open fill → close fill)
    private int _winningTrades;
    private double _totalPnL;
    private double _grossProfit;   // sum of winning-trade P&L (for avg-win in sizers)
    private double _grossLoss;     // sum of |losing-trade P&L| (positive magnitude)
    private double _peakEquity;
    private double _maxDrawdown;
    private double _currentEquity = 10_000.0; // notional starting capital for drawdown tracking

    // Last open trade tracking (null when no position is open)
    private OrderSide? _openSide;
    private double _openPrice;

    // ── Theoretical-fill metrics state (Suggestion mode) ─────────────────────
    // Each StrategySignal returned from ComputeSignal is treated as a theoretical
    // entry at the bar's close. Subsequent bars' High/Low are walked against the
    // signal's Stop and TakeProfit; whichever is hit first closes the trade, with
    // a stop-priority policy (conservative). Signals without a Stop AND TakeProfit
    // are not tracked — they can't be resolved deterministically, so they'd bias
    // the metrics toward open-ended wins. The current-equity drawdown accounting
    // mirrors the real-fill path but uses a separate running equity so the two
    // tracks don't corrupt each other.
    //
    // Production consideration: a live Suggestion-mode strategy running for
    // months could accumulate thousands of open theoreticals if every signal
    // lacks Stop/TP — we cap at 1000 to keep the per-bar scan bounded. Practical
    // specs emit stops and close out quickly so the cap is rarely relevant.
    private const int MaxOpenTheoreticals = 1000;

    private readonly List<OpenTheoretical> _openTheoreticals = new();
    private int _theoreticalSignals;   // total ComputeSignal emissions tracked
    private int _theoreticalClosed;    // theoretical trades that hit Stop or TP
    private int _theoreticalWins;      // theoretical trades that hit TP before Stop
    private double _theoreticalPnL;
    private double _theoreticalGrossProfit;
    private double _theoreticalGrossLoss;
    private double _theoreticalPeakEquity;
    private double _theoreticalMaxDrawdown;
    private double _theoreticalEquity = 10_000.0;

    private readonly struct OpenTheoretical
    {
        public OrderSide Side { get; init; }
        public double EntryPrice { get; init; }
        public double Stop { get; init; }
        public double Target { get; init; }
        public double Quantity { get; init; }
    }

    // ── ITradingStrategy ─────────────────────────────────────────────────────
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract StrategyComplexityLevel Complexity { get; }
    public abstract IReadOnlyList<StrategyParameter> Parameters { get; }

    public virtual void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameterValues)
    {
        // Subclasses may read parameterValues here
    }

    /// <summary>
    /// Entry point for the engine. Walks any open theoretical trades against the
    /// new bar's range (closing on Stop/TP hit), delegates to the subclass's
    /// <see cref="ComputeSignal"/>, and records a new theoretical for any signal
    /// that carries both Stop and TakeProfit so Suggestion-mode metrics reflect
    /// the strategy's actual performance rather than an all-zero placeholder.
    /// </summary>
    public StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
    {
        TickOpenTheoreticals(newBar);
        var signal = ComputeSignal(newBar, history, state);
        if (signal != null) RecordTheoretical(signal, newBar);
        return signal;
    }

    /// <summary>
    /// Subclass hook — compute the next <see cref="StrategySignal"/> for this bar,
    /// or <c>null</c> if no signal fires. The base class wraps this call with
    /// theoretical-fill tracking; do not override <see cref="OnBar"/> directly.
    /// </summary>
    protected abstract StrategySignal? ComputeSignal(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state);

    public void OnOrderFilled(OrderUpdate fill)
    {
        _totalFills++;

        if (_openSide.HasValue && _openPrice > 0)
        {
            // Closing an existing position — compute P&L and update metrics.
            double pnl = _openSide.Value == AccessibleTrader.Sdk.Plugins.OrderSide.Buy
                ? (fill.FilledPrice - _openPrice) * fill.FilledQuantity
                : (_openPrice - fill.FilledPrice) * fill.FilledQuantity;

            _totalPnL += pnl;
            if (pnl > 0) { _winningTrades++; _grossProfit += pnl; }
            else _grossLoss += -pnl;

            _currentEquity += pnl;
            if (_currentEquity > _peakEquity) _peakEquity = _currentEquity;
            double drawdown = _peakEquity > 0 ? (_peakEquity - _currentEquity) / _peakEquity : 0.0;
            if (drawdown > _maxDrawdown) _maxDrawdown = drawdown;

            _closedTrades++;
            _openSide  = null;
            _openPrice = 0;
        }
        else
        {
            // Opening a new position.
            _openSide  = fill.Side;
            _openPrice = fill.FilledPrice;
        }
    }

    public void OnStop()
    {
        _openSide = null;
        _openPrice = 0;
        _openTheoreticals.Clear();
    }

    public StrategyMetrics GetMetrics()
    {
        // Blend the two tracks. A running instance is either Auto (real fills only)
        // or Suggestion (theoretical only) per the engine's ExecutionMode, so the
        // two counters never double-count the same event. Summing is safe and keeps
        // the metric shape the UI already binds to.
        int totalSignals = _totalFills + _theoreticalSignals;
        int totalClosed  = _closedTrades + _theoreticalClosed;
        int totalWins    = _winningTrades + _theoreticalWins;
        double totalPnL  = _totalPnL + _theoreticalPnL;
        // Per-track drawdown picks the larger of the two so a Suggestion-mode
        // strategy surfaces theoretical drawdown even if no real fills exist.
        double maxDd     = Math.Max(_maxDrawdown, _theoreticalMaxDrawdown);

        double winRate = totalClosed > 0 ? (double)totalWins / totalClosed : 0.0;
        return new StrategyMetrics(
            TotalSignals:  totalSignals,
            WinningTrades: totalWins,
            WinRate:       winRate,
            MaxDrawdown:   maxDd,
            TotalPnL:      totalPnL,
            SharpeRatio:   double.NaN,  // Computed by StrategyBacktester only; not meaningful in live mode
            GrossProfit:   _grossProfit + _theoreticalGrossProfit,
            GrossLoss:     _grossLoss + _theoreticalGrossLoss
        );
    }

    // ── Theoretical-fill bookkeeping ─────────────────────────────────────────

    private void RecordTheoretical(StrategySignal signal, Ohlcv entryBar)
    {
        _theoreticalSignals++;
        if (!signal.StopLoss.HasValue || !signal.TakeProfit.HasValue) return;
        if (_openTheoreticals.Count >= MaxOpenTheoreticals) return;

        double qty = signal.Quantity ?? 1.0;
        _openTheoreticals.Add(new OpenTheoretical
        {
            Side = signal.Side,
            EntryPrice = entryBar.Close,
            Stop = signal.StopLoss.Value,
            Target = signal.TakeProfit.Value,
            Quantity = qty,
        });
    }

    private void TickOpenTheoreticals(Ohlcv bar)
    {
        if (_openTheoreticals.Count == 0) return;

        for (int i = _openTheoreticals.Count - 1; i >= 0; i--)
        {
            var t = _openTheoreticals[i];
            bool stopHit = t.Side == OrderSide.Buy ? bar.Low <= t.Stop : bar.High >= t.Stop;
            bool tpHit   = t.Side == OrderSide.Buy ? bar.High >= t.Target : bar.Low <= t.Target;

            // Stop has priority when both trigger on the same bar — the conservative
            // assumption used in StrategyBacktester. Prevents an intra-bar fast move
            // from booking a TP-win on a trade that would realistically stop out.
            // Exit at what the bar would actually have paid, not at the level. A bar
            // that opened past the stop gapped through it, and these numbers are shown
            // to the user as this strategy's live win rate and P&L — the one place an
            // optimistic fill is indistinguishable from a strategy that works.
            if (stopHit)
            {
                CloseTheoretical(t, BarFill.StopExit(t.Stop, bar.Open, t.Side), isWin: false);
                _openTheoreticals.RemoveAt(i);
            }
            else if (tpHit)
            {
                CloseTheoretical(t, BarFill.TargetExit(t.Target, bar.Open, t.Side), isWin: true);
                _openTheoreticals.RemoveAt(i);
            }
        }
    }

    private void CloseTheoretical(OpenTheoretical t, double exitPrice, bool isWin)
    {
        double pnl = t.Side == OrderSide.Buy
            ? (exitPrice - t.EntryPrice) * t.Quantity
            : (t.EntryPrice - exitPrice) * t.Quantity;

        _theoreticalPnL += pnl;
        _theoreticalClosed++;
        if (isWin) _theoreticalWins++;
        if (pnl > 0) _theoreticalGrossProfit += pnl;
        else _theoreticalGrossLoss += -pnl;

        _theoreticalEquity += pnl;
        if (_theoreticalEquity > _theoreticalPeakEquity) _theoreticalPeakEquity = _theoreticalEquity;
        double dd = _theoreticalPeakEquity > 0
            ? (_theoreticalPeakEquity - _theoreticalEquity) / _theoreticalPeakEquity
            : 0.0;
        if (dd > _theoreticalMaxDrawdown) _theoreticalMaxDrawdown = dd;
    }

    // ── Helpers for subclasses ───────────────────────────────────────────────

    /// <summary>Computes a simple moving average over the last <paramref name="period"/> closes.</summary>
    protected static double Sma(IReadOnlyList<Ohlcv> history, int period)
    {
        int count = history.Count;
        if (count < period) return double.NaN;
        double sum = 0;
        for (int i = count - period; i < count; i++)
            sum += history[i].Close;
        return sum / period;
    }

    /// <summary>Computes RSI(n) from the last n+1 closes.</summary>
    protected static double Rsi(IReadOnlyList<Ohlcv> history, int period)
    {
        int count = history.Count;
        if (count < period + 1) return double.NaN;

        double gain = 0, loss = 0;
        for (int i = count - period; i < count; i++)
        {
            double change = history[i].Close - history[i - 1].Close;
            if (change > 0) gain += change;
            else loss -= change;
        }

        double avgGain = gain / period;
        double avgLoss = loss / period;
        if (avgLoss == 0) return 100.0;
        double rs = avgGain / avgLoss;
        return 100.0 - (100.0 / (1.0 + rs));
    }

    /// <summary>Computes Bollinger Bands (middle, upper, lower) from the last <paramref name="period"/> closes.</summary>
    protected static (double Middle, double Upper, double Lower) BollingerBands(IReadOnlyList<Ohlcv> history, int period, double deviations)
    {
        int count = history.Count;
        if (count < period) return (double.NaN, double.NaN, double.NaN);

        double middle = Sma(history, period);
        double variance = 0;
        for (int i = count - period; i < count; i++)
        {
            double diff = history[i].Close - middle;
            variance += diff * diff;
        }
        double stdDev = Math.Sqrt(variance / period);
        return (middle, middle + deviations * stdDev, middle - deviations * stdDev);
    }
}
