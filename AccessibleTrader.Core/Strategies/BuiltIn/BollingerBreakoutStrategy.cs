using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Strategies.BuiltIn;

/// <summary>
/// Bollinger Band mean-reversion breakout strategy.
/// Buy when price breaks below the lower band (expecting mean reversion up).
/// Sell when price breaks above the upper band (expecting mean reversion down).
/// </summary>
public class BollingerBreakoutStrategy : BaseStrategy
{
    public override string Id => "bollinger_breakout";
    public override string Name => "Bollinger Band Breakout";
    public override string Description => "Mean reversion signals when price breaks Bollinger Bands.";
    public override StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;

    public override IReadOnlyList<StrategyParameter> Parameters { get; } = new List<StrategyParameter>
    {
        new("Period",      "Bollinger period",     StrategyParameterType.Integer, 20, 5, 200),
        new("Deviations",  "Standard deviations",  StrategyParameterType.Double,  2.0, 0.5, 5.0),
        new("Quantity",    "Order quantity",        StrategyParameterType.Double,  1.0, 0.0001, null),
    };

    private int _period = 20;
    private double _deviations = 2.0;
    private double _quantity = 1.0;

    private double _prevClose = double.NaN;
    private double _prevUpper = double.NaN;
    private double _prevLower = double.NaN;

    public override void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameterValues)
    {
        if (parameterValues.TryGetValue("Period",     out var p)) _period     = Convert.ToInt32(p);
        if (parameterValues.TryGetValue("Deviations", out var d)) _deviations = Convert.ToDouble(d);
        if (parameterValues.TryGetValue("Quantity",   out var q)) _quantity   = Convert.ToDouble(q);
    }

    public override StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
    {
        var (_, upper, lower) = BollingerBands(history, _period, _deviations);

        StrategySignal? signal = null;

        if (!double.IsNaN(_prevClose) && !double.IsNaN(upper) && !double.IsNaN(lower))
        {
            bool breakAbove = _prevClose <= _prevUpper && newBar.Close > upper;
            bool breakBelow = _prevClose >= _prevLower && newBar.Close < lower;

            if (breakBelow)
            {
                signal = new StrategySignal(
                    Side: OrderSide.Buy,
                    OrderType: OrderType.Market,
                    Quantity: _quantity,
                    LimitPrice: null,
                    StopLoss: null,
                    TakeProfit: null,
                    Rationale: $"Price broke below lower Bollinger Band ({lower:F6}), expecting mean reversion",
                    Confidence: 0.55
                );
            }
            else if (breakAbove)
            {
                signal = new StrategySignal(
                    Side: OrderSide.Sell,
                    OrderType: OrderType.Market,
                    Quantity: _quantity,
                    LimitPrice: null,
                    StopLoss: null,
                    TakeProfit: null,
                    Rationale: $"Price broke above upper Bollinger Band ({upper:F6}), expecting mean reversion",
                    Confidence: 0.55
                );
            }
        }

        _prevClose = newBar.Close;
        _prevUpper = upper;
        _prevLower = lower;
        return signal;
    }
}
