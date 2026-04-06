using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Strategies;

/// <summary>
/// Replays historical OHLCV data through a strategy bar-by-bar and returns a full backtest result.
/// Simulates fills at the next bar's open price plus slippage.
/// Tracks equity curve and key performance metrics.
/// </summary>
public class StrategyBacktester : IStrategyBacktester
{
    public Task<BacktestResult> RunAsync(
        ITradingStrategy strategy,
        IReadOnlyList<Ohlcv> data,
        BacktestConfig config)
    {
        var result = Run(strategy, data, config);
        return Task.FromResult(result);
    }

    private BacktestResult Run(
        ITradingStrategy strategy,
        IReadOnlyList<Ohlcv> data,
        BacktestConfig config)
    {
        if (data.Count < 2)
        {
            var empty = new StrategyMetrics(0, 0, 0, 0, 0, 0);
            return new BacktestResult(empty, Array.Empty<BacktestTrade>(),
                Array.Empty<(DateTime, double)>(), "Insufficient data for backtest.");
        }

        var sizer = config.PositionSizer ?? new FixedSizePositionSizer();

        double equity = config.StartingCapital;
        double peakEquity = equity;
        double maxDrawdown = 0.0;

        var trades = new List<BacktestTrade>();
        var equityCurve = new List<(DateTime Date, double EquityValue)>();
        equityCurve.Add((data[0].Date, equity));

        // Strategy state
        OrderSide? openSide = null;
        double openPrice = 0;
        double openQty = 0;
        DateTime openTime = default;
        int winningTrades = 0;

        // Dummy state for strategy (no live series)
        var dummyState = WorkspaceState.Initial;

        // Build a growing history window; use immutable list for type compat
        var historyBuffer = ImmutableList<Ohlcv>.Empty;

        // Initialize with first bar
        var initParams = new Dictionary<string, object>();
        strategy.Initialize(historyBuffer, dummyState, initParams);

        for (int i = 0; i < data.Count - 1; i++)
        {
            historyBuffer = historyBuffer.Add(data[i]);
            var bar = data[i];

            var signal = strategy.OnBar(bar, historyBuffer, dummyState);

            if (signal != null)
            {
                var liveMetrics = strategy.GetMetrics();
                double qty = signal.Quantity ?? sizer.CalculateSize(signal, equity, liveMetrics);

                // Simulate fill at next bar open + slippage
                double fillPrice = data[i + 1].Open;
                double slippage = fillPrice * config.SlippagePercent;
                fillPrice += signal.Side == OrderSide.Buy ? slippage : -slippage;

                double commission = fillPrice * qty * config.CommissionRate;

                if (openSide.HasValue)
                {
                    // Close existing position
                    double pnl = openSide.Value == OrderSide.Buy
                        ? (fillPrice - openPrice) * openQty - commission
                        : (openPrice - fillPrice) * openQty - commission;

                    equity += pnl;
                    if (equity > peakEquity) peakEquity = equity;
                    double dd = peakEquity > 0 ? (peakEquity - equity) / peakEquity : 0;
                    if (dd > maxDrawdown) maxDrawdown = dd;

                    trades.Add(new BacktestTrade(
                        openTime, openPrice, openSide.Value, openQty,
                        data[i + 1].Date, fillPrice, pnl,
                        $"Reversed by {signal.Rationale}"));

                    if (pnl > 0) winningTrades++;
                    equityCurve.Add((data[i + 1].Date, equity));

                    // Immediately open new position in signal direction
                    openSide = signal.Side;
                    openPrice = fillPrice;
                    openQty = qty;
                    openTime = data[i + 1].Date;
                }
                else
                {
                    // Open new position
                    equity -= commission;
                    openSide = signal.Side;
                    openPrice = fillPrice;
                    openQty = qty;
                    openTime = data[i + 1].Date;
                    equityCurve.Add((data[i + 1].Date, equity));
                }
            }
        }

        // Close any open position at last bar close
        if (openSide.HasValue && data.Count > 0)
        {
            var lastBar = data[^1];
            double fillPrice = lastBar.Close;
            double commission = fillPrice * openQty * config.CommissionRate;
            double pnl = openSide.Value == OrderSide.Buy
                ? (fillPrice - openPrice) * openQty - commission
                : (openPrice - fillPrice) * openQty - commission;

            equity += pnl;
            if (pnl > 0) winningTrades++;

            trades.Add(new BacktestTrade(
                openTime, openPrice, openSide.Value, openQty,
                lastBar.Date, fillPrice, pnl, "End of data"));

            equityCurve.Add((lastBar.Date, equity));
        }

        int totalTrades = trades.Count;
        double winRate = totalTrades > 0 ? (double)winningTrades / totalTrades : 0.0;
        double totalPnL = equity - config.StartingCapital;
        double totalReturn = config.StartingCapital > 0 ? totalPnL / config.StartingCapital * 100.0 : 0.0;

        // Sharpe: (annualised return) / (annualised stddev of daily returns)
        double sharpe = ComputeSharpe(equityCurve);

        var metrics = new StrategyMetrics(
            TotalSignals: totalTrades,
            WinningTrades: winningTrades,
            WinRate: winRate,
            MaxDrawdown: maxDrawdown,
            TotalPnL: totalPnL,
            SharpeRatio: sharpe
        );

        string speech = $"{totalTrades} trades, {winRate * 100.0:F6} percent win rate, " +
                        $"maximum drawdown {maxDrawdown * 100.0:F6} percent, " +
                        $"total return {totalReturn:F6} percent";

        return new BacktestResult(metrics, trades, equityCurve, speech);
    }

    private static double ComputeSharpe(List<(DateTime Date, double EquityValue)> curve)
    {
        if (curve.Count < 2) return 0.0;

        var returns = new List<double>();
        for (int i = 1; i < curve.Count; i++)
        {
            double prev = curve[i - 1].EquityValue;
            if (prev == 0) continue;
            returns.Add((curve[i].EquityValue - prev) / prev);
        }

        if (returns.Count < 2) return 0.0;

        double mean = returns.Average();
        double variance = returns.Select(r => (r - mean) * (r - mean)).Average();
        double stdDev = Math.Sqrt(variance);

        if (stdDev == 0) return 0.0;

        // Annualise assuming ~252 trading periods per year
        return mean / stdDev * Math.Sqrt(252);
    }
}
