using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Strategies.BuiltIn;

/// <summary>
/// RSI oversold/overbought reversal strategy.
/// Buy when RSI crosses below the oversold threshold; Sell when it crosses above overbought.
/// </summary>
public class RsiOversoldStrategy : BaseStrategy
{
    public override string Id => "rsi_oversold";
    public override string Name => "RSI Oversold/Overbought";
    public override string Description => "Buys on RSI oversold crossover, sells on overbought crossover.";
    public override StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;

    public override IReadOnlyList<StrategyParameter> Parameters { get; } = new List<StrategyParameter>
    {
        new("Period",               "RSI period",               StrategyParameterType.Integer, 14, 2, 200),
        new("OversoldThreshold",    "Oversold threshold",       StrategyParameterType.Double,  30.0, 1.0, 49.0),
        new("OverboughtThreshold",  "Overbought threshold",     StrategyParameterType.Double,  70.0, 51.0, 99.0),
        new("Quantity",             "Order quantity",            StrategyParameterType.Double,  1.0, 0.0001, null),
    };

    private int _period = 14;
    private double _oversold = 30.0;
    private double _overbought = 70.0;
    private double _quantity = 1.0;

    private double _prevRsi = double.NaN;

    public override void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameterValues)
    {
        if (parameterValues.TryGetValue("Period",              out var p))  _period     = Convert.ToInt32(p);
        if (parameterValues.TryGetValue("OversoldThreshold",   out var os)) _oversold   = Convert.ToDouble(os);
        if (parameterValues.TryGetValue("OverboughtThreshold", out var ob)) _overbought = Convert.ToDouble(ob);
        if (parameterValues.TryGetValue("Quantity",            out var q))  _quantity   = Convert.ToDouble(q);
    }

    public override StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
    {
        double rsi = Rsi(history, _period);

        StrategySignal? signal = null;

        if (!double.IsNaN(_prevRsi) && !double.IsNaN(rsi))
        {
            bool crossedBelowOversold   = _prevRsi >= _oversold   && rsi < _oversold;
            bool crossedAboveOverbought = _prevRsi <= _overbought  && rsi > _overbought;

            if (crossedBelowOversold)
            {
                signal = new StrategySignal(
                    Side: OrderSide.Buy,
                    OrderType: OrderType.Market,
                    Quantity: _quantity,
                    LimitPrice: null,
                    StopLoss: null,
                    TakeProfit: null,
                    Rationale: $"RSI({_period}) crossed into oversold zone ({rsi:F2})",
                    Confidence: 0.65
                );
            }
            else if (crossedAboveOverbought)
            {
                signal = new StrategySignal(
                    Side: OrderSide.Sell,
                    OrderType: OrderType.Market,
                    Quantity: _quantity,
                    LimitPrice: null,
                    StopLoss: null,
                    TakeProfit: null,
                    Rationale: $"RSI({_period}) crossed into overbought zone ({rsi:F2})",
                    Confidence: 0.65
                );
            }
        }

        _prevRsi = rsi;
        return signal;
    }
}
