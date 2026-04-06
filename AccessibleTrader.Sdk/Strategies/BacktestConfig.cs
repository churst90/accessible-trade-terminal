namespace AccessibleTrader.Sdk.Strategies;

public record BacktestConfig(
    double StartingCapital = 10000.0,
    double CommissionRate = 0.001,      // 0.1% per trade
    double SlippagePercent = 0.0005,    // 0.05% slippage
    IPositionSizer? PositionSizer = null
);
