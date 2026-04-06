namespace AccessibleTrader.Sdk.Strategies;

public interface IPositionSizer
{
    double CalculateSize(StrategySignal signal, double accountBalance, StrategyMetrics metrics);
}
