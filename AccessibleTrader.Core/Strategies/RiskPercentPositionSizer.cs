using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Strategies;

/// <summary>
/// Sizes positions to risk a fixed percentage of account equity per trade.
/// If a stop-loss level is known: size = (balance * riskPercent/100) / |entryPrice - stopLoss|.
/// Without a stop-loss: size = (balance * riskPercent/100) / entryPrice (rough estimate).
/// </summary>
public class RiskPercentPositionSizer : IPositionSizer
{
    private readonly double _riskPercent;

    /// <param name="riskPercent">Risk as a percentage of account equity, e.g. 1.0 = 1%.</param>
    public RiskPercentPositionSizer(double riskPercent = 1.0)
    {
        _riskPercent = Math.Clamp(riskPercent, 0.01, 50.0);
    }

    public double CalculateSize(StrategySignal signal, double accountBalance, StrategyMetrics metrics)
    {
        double riskAmount = accountBalance * _riskPercent / 100.0;

        // If the signal carries a stop-loss and a limit/reference price, use them
        if (signal.StopLoss.HasValue && signal.LimitPrice.HasValue)
        {
            double risk = Math.Abs(signal.LimitPrice.Value - signal.StopLoss.Value);
            if (risk > 0) return riskAmount / risk;
        }

        // Fallback: divide by the limit/entry price if available
        if (signal.LimitPrice.HasValue && signal.LimitPrice.Value > 0)
            return riskAmount / signal.LimitPrice.Value;

        // Last resort: fixed quantity
        return signal.Quantity ?? 1.0;
    }
}
