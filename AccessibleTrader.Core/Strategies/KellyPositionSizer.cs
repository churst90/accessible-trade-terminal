using System;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Strategies;

/// <summary>
/// Half-Kelly position sizer.
/// Kelly fraction = (winRate * avgWin - (1-winRate) * avgLoss) / avgWin.
/// Clamped to [0.02, 0.25] to prevent full-Kelly overbetting.
/// Falls back to 1% of balance if insufficient trade history.
/// </summary>
public class KellyPositionSizer : IPositionSizer
{
    private const double MinFraction = 0.02;
    private const double MaxFraction = 0.25;
    private const double FallbackPercent = 0.01; // 1% of balance

    public double CalculateSize(StrategySignal signal, double accountBalance, StrategyMetrics metrics)
    {
        if (metrics.WinRate <= 0 || metrics.TotalSignals < 10)
        {
            // Insufficient data — fall back to 1% of account balance
            double fallback = accountBalance * FallbackPercent;
            if (signal.LimitPrice.HasValue && signal.LimitPrice.Value > 0)
                return fallback / signal.LimitPrice.Value;
            return signal.Quantity ?? 1.0;
        }

        double winRate = metrics.WinRate;
        double lossRate = 1.0 - winRate;

        // Approximate avg win/loss from total P&L and win rate
        // This is a simplification; a full implementation would track individual trade P&L
        double avgWin = metrics.TotalPnL > 0 && metrics.WinningTrades > 0
            ? metrics.TotalPnL / metrics.WinningTrades
            : 1.0;
        double avgLoss = metrics.TotalPnL < 0 && metrics.WinningTrades < metrics.TotalSignals
            ? Math.Abs(metrics.TotalPnL) / (metrics.TotalSignals - metrics.WinningTrades)
            : 1.0;

        if (avgWin <= 0) avgWin = 1.0;
        if (avgLoss <= 0) avgLoss = 1.0;

        double kellyFraction = (winRate * avgWin - lossRate * avgLoss) / avgWin;
        kellyFraction = Math.Clamp(kellyFraction, MinFraction, MaxFraction);

        // Half-Kelly for safety
        kellyFraction /= 2.0;

        double riskAmount = accountBalance * kellyFraction;

        if (signal.LimitPrice.HasValue && signal.LimitPrice.Value > 0)
            return riskAmount / signal.LimitPrice.Value;

        return signal.Quantity ?? 1.0;
    }
}
