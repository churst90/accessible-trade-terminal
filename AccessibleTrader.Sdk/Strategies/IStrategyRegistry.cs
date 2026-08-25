namespace AccessibleTrader.Sdk.Strategies;

/// <summary>
/// Catalogue of available strategy templates.
/// Each call to <see cref="CreateInstance"/> returns a fresh, uninitialized
/// <see cref="ITradingStrategy"/> suitable for passing to
/// <see cref="IStrategyEngine.AddStrategy"/>.
/// </summary>
public interface IStrategyRegistry
{
    /// <summary>Returns a read-only list of strategy template descriptors.</summary>
    IReadOnlyList<ITradingStrategy> GetCatalog();

    /// <summary>
    /// Creates a new, uninitialized instance of the strategy identified by
    /// <paramref name="strategyId"/>.  Returns <c>null</c> if the id is unknown.
    /// </summary>
    ITradingStrategy? CreateInstance(string strategyId);
}
