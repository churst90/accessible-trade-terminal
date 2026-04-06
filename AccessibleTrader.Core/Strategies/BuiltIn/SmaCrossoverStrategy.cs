using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Strategies.BuiltIn;

/// <summary>
/// Simple SMA crossover strategy.
/// Buy when the fast SMA crosses above the slow SMA.
/// Sell when the fast SMA crosses below the slow SMA.
/// </summary>
public class SmaCrossoverStrategy : BaseStrategy
{
    public override string Id => "sma_crossover";
    public override string Name => "SMA Crossover";
    public override string Description => "Generates signals when a fast SMA crosses a slow SMA.";
    public override StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;

    public override IReadOnlyList<StrategyParameter> Parameters { get; } = new List<StrategyParameter>
    {
        new("FastPeriod", "Fast SMA period", StrategyParameterType.Integer, 10, 2, 200),
        new("SlowPeriod", "Slow SMA period", StrategyParameterType.Integer, 20, 5, 500),
        new("Quantity",   "Order quantity",  StrategyParameterType.Double,  1.0, 0.0001, null),
    };

    private int _fastPeriod = 10;
    private int _slowPeriod = 20;
    private double _quantity = 1.0;

    private double _prevFast = double.NaN;
    private double _prevSlow = double.NaN;

    public override void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameterValues)
    {
        if (parameterValues.TryGetValue("FastPeriod", out var fp)) _fastPeriod = Convert.ToInt32(fp);
        if (parameterValues.TryGetValue("SlowPeriod", out var sp)) _slowPeriod = Convert.ToInt32(sp);
        if (parameterValues.TryGetValue("Quantity",   out var q))  _quantity   = Convert.ToDouble(q);
    }

    public override StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
    {
        if (history.Count < _slowPeriod + 1) return null;

        double fast = Sma(history, _fastPeriod);
        double slow = Sma(history, _slowPeriod);

        if (double.IsNaN(fast) || double.IsNaN(slow))
        {
            _prevFast = fast;
            _prevSlow = slow;
            return null;
        }

        StrategySignal? signal = null;

        if (!double.IsNaN(_prevFast) && !double.IsNaN(_prevSlow))
        {
            bool crossedAbove = _prevFast <= _prevSlow && fast > slow;
            bool crossedBelow = _prevFast >= _prevSlow && fast < slow;

            if (crossedAbove)
            {
                signal = new StrategySignal(
                    Side: OrderSide.Buy,
                    OrderType: OrderType.Market,
                    Quantity: _quantity,
                    LimitPrice: null,
                    StopLoss: null,
                    TakeProfit: null,
                    Rationale: $"SMA{_fastPeriod} crossed above SMA{_slowPeriod}",
                    Confidence: 0.6
                );
            }
            else if (crossedBelow)
            {
                signal = new StrategySignal(
                    Side: OrderSide.Sell,
                    OrderType: OrderType.Market,
                    Quantity: _quantity,
                    LimitPrice: null,
                    StopLoss: null,
                    TakeProfit: null,
                    Rationale: $"SMA{_fastPeriod} crossed below SMA{_slowPeriod}",
                    Confidence: 0.6
                );
            }
        }

        _prevFast = fast;
        _prevSlow = slow;
        return signal;
    }
}
