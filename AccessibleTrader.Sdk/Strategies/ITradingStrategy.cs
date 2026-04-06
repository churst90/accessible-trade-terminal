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
    double Confidence
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
