using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AccessibleTrader.Core.Strategies.BuiltIn;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Strategies;

/// <summary>
/// A user-constructed composite strategy.
/// Evaluates a list of condition blocks against indicator values,
/// combining them with AND/OR logic operators.
/// Serialises to/from JSON via the "ConditionBlocks" parameter.
/// </summary>
public class CompositeStrategy : BuiltIn.BaseStrategy
{
    public override string Id => "composite";
    public override string Name => "Composite Strategy";
    public override string Description => "User-defined strategy composed of indicator condition blocks combined with AND/OR logic.";
    public override StrategyComplexityLevel Complexity => StrategyComplexityLevel.Intermediate;

    public override IReadOnlyList<StrategyParameter> Parameters { get; } = new List<StrategyParameter>
    {
        new("ConditionBlocks", "JSON-serialized condition blocks", StrategyParameterType.String,
            "[]", null, null, null),
        new("SignalSide", "Signal direction (Buy or Sell)", StrategyParameterType.String,
            "Buy", null, null, new[] { "Buy", "Sell" }),
        new("Quantity",  "Order quantity", StrategyParameterType.Double, 1.0, 0.0001, null),
    };

    private List<ConditionBlock> _blocks = new();
    private OrderSide _signalSide = OrderSide.Buy;
    private double _quantity = 1.0;
    private readonly IAlertEvaluator? _evaluator;

    public CompositeStrategy(IAlertEvaluator? evaluator = null)
    {
        _evaluator = evaluator;
    }

    public override void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameterValues)
    {
        if (parameterValues.TryGetValue("ConditionBlocks", out var json) && json is string jsonStr)
        {
            try
            {
                _blocks = JsonSerializer.Deserialize<List<ConditionBlock>>(jsonStr) ?? new List<ConditionBlock>();
            }
            catch
            {
                _blocks = new List<ConditionBlock>();
            }
        }

        if (parameterValues.TryGetValue("SignalSide", out var side) && side is string sideStr)
            _signalSide = sideStr.Equals("Sell", StringComparison.OrdinalIgnoreCase) ? OrderSide.Sell : OrderSide.Buy;

        if (parameterValues.TryGetValue("Quantity", out var q))
            _quantity = Convert.ToDouble(q);
    }

    public override StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
    {
        if (_blocks.Count == 0) return null;

        // Evaluate each block independently
        var results = _blocks.Select(b => EvaluateBlock(b, newBar, history, state)).ToList();

        // Combine using logical operators declared on each block
        // (the LogicalOperator on a block means "how to combine this block's result with the previous")
        bool combined = results[0];
        for (int i = 1; i < results.Count; i++)
        {
            combined = _blocks[i].LogicalOperator == LogicalOperator.Or
                ? combined || results[i]
                : combined && results[i];
        }

        if (!combined) return null;

        return new StrategySignal(
            Side: _signalSide,
            OrderType: OrderType.Market,
            Quantity: _quantity,
            LimitPrice: null,
            StopLoss: null,
            TakeProfit: null,
            Rationale: "Composite conditions met",
            Confidence: 0.6
        );
    }

    private bool EvaluateBlock(ConditionBlock block, Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
    {
        // Resolve the current value of the target
        double value = GetValue(block, newBar, state);
        double prevValue = GetPrevValue(block, history, state);

        if (double.IsNaN(value)) return false;

        return block.Condition switch
        {
            AlertCondition.CrossesAbove => !double.IsNaN(prevValue) && prevValue <= block.Threshold && value > block.Threshold,
            AlertCondition.CrossesBelow => !double.IsNaN(prevValue) && prevValue >= block.Threshold && value < block.Threshold,
            AlertCondition.ChangesDirection => !double.IsNaN(prevValue) && Math.Sign(value - prevValue) != 0 &&
                                               Math.Sign(value - prevValue) != Math.Sign(prevValue - GetPrevPrevValue(block, history, state)),
            _ => false
        };
    }

    private double GetValue(ConditionBlock block, Ohlcv bar, WorkspaceState state)
    {
        if (block.Target == AlertTarget.Price) return bar.Close;
        if (block.Target == AlertTarget.Candle) return bar.Close;

        // Indicator target: look up in ActiveSeries
        var series = state.ActiveSeries.FirstOrDefault(s => s.IndicatorCode == block.IndicatorCode);
        if (series == null) return double.NaN;
        var comp = string.IsNullOrEmpty(block.ComponentName)
            ? series.Components.FirstOrDefault()
            : series.Components.FirstOrDefault(c => c.Name == block.ComponentName);
        if (comp == null) return double.NaN;
        var data = series.GetComponentData(comp.Name);
        if (data.Length == 0) return double.NaN;
        return data[^1];
    }

    private double GetPrevValue(ConditionBlock block, IReadOnlyList<Ohlcv> history, WorkspaceState state)
    {
        if (block.Target != AlertTarget.Indicator) return history.Count >= 2 ? history[^2].Close : double.NaN;
        var series = state.ActiveSeries.FirstOrDefault(s => s.IndicatorCode == block.IndicatorCode);
        if (series == null) return double.NaN;
        var comp = string.IsNullOrEmpty(block.ComponentName)
            ? series.Components.FirstOrDefault()
            : series.Components.FirstOrDefault(c => c.Name == block.ComponentName);
        if (comp == null) return double.NaN;
        var data = series.GetComponentData(comp.Name);
        if (data.Length < 2) return double.NaN;
        return data[^2];
    }

    private double GetPrevPrevValue(ConditionBlock block, IReadOnlyList<Ohlcv> history, WorkspaceState state)
    {
        if (block.Target != AlertTarget.Indicator) return history.Count >= 3 ? history[^3].Close : double.NaN;
        var series = state.ActiveSeries.FirstOrDefault(s => s.IndicatorCode == block.IndicatorCode);
        if (series == null) return double.NaN;
        var comp = string.IsNullOrEmpty(block.ComponentName)
            ? series.Components.FirstOrDefault()
            : series.Components.FirstOrDefault(c => c.Name == block.ComponentName);
        if (comp == null) return double.NaN;
        var data = series.GetComponentData(comp.Name);
        if (data.Length < 3) return double.NaN;
        return data[^3];
    }
}

public enum LogicalOperator { And, Or }

public record ConditionBlock(
    AlertTarget Target,
    string? IndicatorCode,
    string? ComponentName,
    AlertCondition Condition,
    double Threshold,
    LogicalOperator LogicalOperator
);
