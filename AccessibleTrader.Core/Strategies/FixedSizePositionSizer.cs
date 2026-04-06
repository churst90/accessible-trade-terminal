using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Strategies;

/// <summary>
/// Returns the fixed quantity declared in the signal, or 1.0 if no quantity is specified.
/// </summary>
public class FixedSizePositionSizer : IPositionSizer
{
    public double CalculateSize(StrategySignal signal, double accountBalance, StrategyMetrics metrics)
        => signal.Quantity ?? 1.0;
}
