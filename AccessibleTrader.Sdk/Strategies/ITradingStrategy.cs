using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Sdk.Strategies;

public enum StrategyComplexityLevel { Simple, Intermediate, Advanced }
public enum StrategyParameterType { Integer, Double, Boolean, IndicatorCode, Timeframe, String }
public enum StrategyExecutionMode { Suggestion, Auto }

public record StrategyParameter(
    string Name,
    string Description,
    StrategyParameterType Type,
    object DefaultValue,
    object? MinValue = null,
    object? MaxValue = null,
    string[]? AllowedValues = null
);

public record StrategySignal(
    OrderSide Side,
    OrderType OrderType,
    double? Quantity,
    double? LimitPrice,
    double? StopLoss,
    double? TakeProfit,
    string Rationale,
    double Confidence,
    /// <summary>
    /// Optional take-profit ladder beyond the single <see cref="TakeProfit"/> price. When set,
    /// the backtester closes <see cref="TpClosePortions"/>[i] of the position when price reaches
    /// each ladder rung in order, and (if configured) moves the stop to breakeven after TP1. The
    /// single <see cref="TakeProfit"/> field is preserved for back-compat with the broker order
    /// path which doesn't yet handle multi-leg bracket orders.
    /// </summary>
    System.Collections.Generic.IReadOnlyList<double>? TpLadder = null,
    /// <summary>
    /// Per-rung close fractions matching <see cref="TpLadder"/>. Sum should be ≤ 1.0 — any
    /// remainder rides past TP3 until end-of-data or stop.
    /// </summary>
    System.Collections.Generic.IReadOnlyList<double>? TpClosePortions = null
);

public record StrategyMetrics(
    int TotalSignals,
    int WinningTrades,
    double WinRate,
    double MaxDrawdown,
    double TotalPnL,
    double SharpeRatio
);

public interface ITradingStrategy
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    StrategyComplexityLevel Complexity { get; }
    IReadOnlyList<StrategyParameter> Parameters { get; }

    void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameterValues);
    StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state);
    void OnOrderFilled(OrderUpdate fill);
    void OnStop();
    StrategyMetrics GetMetrics();
}
