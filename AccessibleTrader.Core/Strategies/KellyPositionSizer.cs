using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Strategies;

/// <summary>
/// Half-Kelly position sizer.
///
/// Raw Kelly fraction f* = (p·b − q) / b where p = win rate, q = 1 − p, and
/// b = average win / average loss (the payoff ratio). The fraction is HALVED
/// first (half-Kelly — standard practice because Kelly is hypersensitive to
/// estimation error in p and b), THEN clamped to [0, MaxFraction/2].
///
/// History (2026-06-12 audit): the previous implementation had three defects —
/// it fabricated avgLoss = $1 whenever TotalPnL was net-positive (no gross
/// figures existed on StrategyMetrics), it clamped BEFORE halving with a 0.02
/// floor that forced negative-edge strategies UP to a positive size, and it
/// labelled position notional as "riskAmount". Now: real gross profit/loss
/// feed avg win/loss, a non-positive Kelly falls back to the minimum size
/// (the sizer cannot veto a trade the strategy decided to take, but it will
/// not size conviction it doesn't have), and when the signal carries a stop
/// the fraction is applied to actual risk-per-unit rather than notional.
/// </summary>
public class KellyPositionSizer : IPositionSizer
{
    /// <summary>Ceiling on the raw Kelly fraction; halved this is 12.5% of balance.</summary>
    private const double MaxFraction = 0.25;
    private const double FallbackPercent = 0.01; // 1% of balance
    private const int MinTradeHistory = 10;

    public double CalculateSize(StrategySignal signal, double accountBalance, StrategyMetrics metrics)
    {
        // Insufficient history, no wins/losses recorded, or gross figures absent
        // (older metrics producers) — fall back to 1% of balance.
        if (metrics.TotalSignals < MinTradeHistory
            || metrics.WinRate <= 0 || metrics.WinRate >= 1
            || metrics.GrossProfit <= 0 || metrics.GrossLoss <= 0
            || metrics.WinningTrades <= 0)
            return FallbackSize(signal, accountBalance);

        int losingTrades = metrics.TotalSignals - metrics.WinningTrades;
        if (losingTrades <= 0)
            return FallbackSize(signal, accountBalance);

        double p = metrics.WinRate;
        double q = 1.0 - p;
        double avgWin = metrics.GrossProfit / metrics.WinningTrades;
        double avgLoss = metrics.GrossLoss / losingTrades;
        double b = avgWin / avgLoss;

        double kelly = (p * b - q) / b;

        // Half-Kelly FIRST, then clamp. A non-positive Kelly means the edge is
        // negative — never size that above the conservative minimum.
        double fraction = Math.Clamp(kelly / 2.0, 0.0, MaxFraction / 2.0);
        if (fraction <= 0.0)
            return FallbackSize(signal, accountBalance);

        double allocation = accountBalance * fraction;

        // With a stop we can size on true risk: quantity such that a stop-out
        // loses `allocation`. Capped at full-balance notional — a tight stop
        // must not imply implicit leverage. Without a stop, treat the fraction
        // as notional.
        double? entry = signal.LimitPrice;
        if (entry is > 0 && signal.StopLoss is double stop && stop > 0)
        {
            double riskPerUnit = Math.Abs(entry.Value - stop);
            if (riskPerUnit > 0)
                return Math.Min(allocation / riskPerUnit, accountBalance / entry.Value);
        }
        if (entry is > 0)
            return allocation / entry.Value;

        return signal.Quantity ?? 1.0;
    }

    private static double FallbackSize(StrategySignal signal, double accountBalance)
    {
        double fallback = accountBalance * FallbackPercent;
        if (signal.LimitPrice is > 0)
            return fallback / signal.LimitPrice.Value;
        return signal.Quantity ?? 1.0;
    }
}
