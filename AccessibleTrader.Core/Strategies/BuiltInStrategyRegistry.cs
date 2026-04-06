using System.Collections.Generic;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Core.Strategies.BuiltIn;

namespace AccessibleTrader.Core.Strategies;

/// <summary>
/// Registers the three built-in trading strategies as the default strategy catalogue.
/// Descriptor instances in <see cref="GetCatalog"/> are read-only templates; each call
/// to <see cref="CreateInstance"/> allocates a fresh, independent runtime instance so
/// the same strategy can be added multiple times with different parameters.
/// </summary>
public sealed class BuiltInStrategyRegistry : IStrategyRegistry
{
    // Template instances used only to read metadata (Name, Description, Parameters).
    // Never passed to StrategyEngine directly.
    private static readonly IReadOnlyList<ITradingStrategy> _catalog =
        new List<ITradingStrategy>
        {
            new SmaCrossoverStrategy(),
            new RsiOversoldStrategy(),
            new BollingerBreakoutStrategy(),
        };

    public IReadOnlyList<ITradingStrategy> GetCatalog() => _catalog;

    public ITradingStrategy? CreateInstance(string strategyId) => strategyId switch
    {
        "sma_crossover"      => new SmaCrossoverStrategy(),
        "rsi_oversold"       => new RsiOversoldStrategy(),
        "bollinger_breakout" => new BollingerBreakoutStrategy(),
        _                    => null,
    };
}
