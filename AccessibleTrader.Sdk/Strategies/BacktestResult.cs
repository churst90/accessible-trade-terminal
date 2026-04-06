using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Sdk.Strategies;

public record BacktestTrade(
    DateTime EntryTime,
    double EntryPrice,
    OrderSide Side,
    double Quantity,
    DateTime? ExitTime,
    double? ExitPrice,
    double? PnL,
    string ExitReason
);

public record BacktestResult(
    StrategyMetrics Metrics,
    IReadOnlyList<BacktestTrade> Trades,
    IReadOnlyList<(DateTime Date, double EquityValue)> EquityCurve,
    string SpeechSummary
);
