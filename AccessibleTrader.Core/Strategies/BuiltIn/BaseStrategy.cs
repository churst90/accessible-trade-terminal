using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Core.Strategies.BuiltIn;

/// <summary>
/// Abstract base class for built-in trading strategies.
/// Tracks performance metrics automatically; subclasses override OnBar only.
/// </summary>
public abstract class BaseStrategy : ITradingStrategy
{
    // ── Metrics state ────────────────────────────────────────────────────────
    private int _totalFills;       // number of OnOrderFilled calls (opens + closes)
    private int _closedTrades;     // completed round-trip trades (open fill → close fill)
    private int _winningTrades;
    private double _totalPnL;
    private double _peakEquity;
    private double _maxDrawdown;
    private double _currentEquity = 10_000.0; // notional starting capital for drawdown tracking

    // Last open trade tracking (null when no position is open)
    private OrderSide? _openSide;
    private double _openPrice;

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

    public abstract StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state);

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
            if (pnl > 0) _winningTrades++;

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
    }

    public StrategyMetrics GetMetrics()
    {
        double winRate = _closedTrades > 0 ? (double)_winningTrades / _closedTrades : 0.0;
        return new StrategyMetrics(
            TotalSignals:  _totalFills,
            WinningTrades: _winningTrades,
            WinRate:       winRate,
            MaxDrawdown:   _maxDrawdown,
            TotalPnL:      _totalPnL,
            SharpeRatio:   double.NaN   // Computed by StrategyBacktester only; not meaningful in live mode
        );
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
