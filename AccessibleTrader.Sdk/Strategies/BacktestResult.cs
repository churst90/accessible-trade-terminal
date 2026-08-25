using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Sdk.Strategies;

/// <param name="StopPrice">Original stop price from the entry signal (for R-multiple math).</param>
/// <param name="BarsInTrade">Number of bars between entry and exit (for hold-time stats).</param>
/// <param name="FeatureSnapshot">v11 diagnostic: snapshot of every numeric indicator component
/// value from the workspace's active series at the entry decision bar. Keyed by
/// "{IndicatorCode}.{ComponentName}". Null when no indicators are loaded or capture is disabled.
/// Used by the trade-level CSV export to find which features discriminate winners from losers
/// without having to instrument the strategy spec itself.</param>
public record BacktestTrade(
    DateTime EntryTime,
    double EntryPrice,
    OrderSide Side,
    double Quantity,
    DateTime? ExitTime,
    double? ExitPrice,
    double? PnL,
    string ExitReason,
    double? StopPrice = null,
    int BarsInTrade = 0,
    IReadOnlyDictionary<string, double>? FeatureSnapshot = null
);

/// <param name="WarmupBars">Bars at the start of the input that were fed-only (no signals taken).</param>
/// <param name="EvaluatedBars">Bars actually evaluated for signals (TotalBars - WarmupBars).</param>
/// <param name="AverageR">Mean reward/risk multiple actually achieved across closed trades that
/// had a known stop. NaN if no trades had stops.</param>
/// <param name="Expectancy">Average R per trade (same as AverageR — kept as a separate field
/// because the discretionary trader vocabulary uses both names).</param>
/// <param name="ProfitFactor">Sum(winning P&amp;L) / Sum(|losing P&amp;L|). NaN when no losers.</param>
/// <param name="AverageBarsInTrade">Mean hold time across all closed trades.</param>
/// <param name="LongestLosingStreak">Largest run of consecutive losing trades.</param>
public record BacktestResult(
    StrategyMetrics Metrics,
    IReadOnlyList<BacktestTrade> Trades,
    IReadOnlyList<(DateTime Date, double EquityValue)> EquityCurve,
    string SpeechSummary,
    int WarmupBars = 0,
    int EvaluatedBars = 0,
    double AverageR = double.NaN,
    double Expectancy = double.NaN,
    double ProfitFactor = double.NaN,
    double AverageBarsInTrade = 0.0,
    int LongestLosingStreak = 0
);
