using System.Collections.Generic;

namespace AccessibleTrader.Sdk.Strategies;

public record ActiveStrategy(
    string InstanceId,
    ITradingStrategy Strategy,
    IDictionary<string, object> Parameters,
    StrategyExecutionMode ExecutionMode,
    bool IsPaused,
    /// <summary>
    /// The chart symbol this strategy is bound to (stamped from the active chart
    /// when the strategy is started). The foreground engine only evaluates it while
    /// that symbol is on screen; a background workspace monitor evaluates it while
    /// its symbol is NOT on screen — exactly one driver at a time. Null/empty =
    /// legacy behaviour (always evaluates against whatever chart is focused).
    /// </summary>
    string? Symbol = null
);

public interface IStrategyEngine
{
    IReadOnlyList<ActiveStrategy> ActiveStrategies { get; }
    string AddStrategy(ITradingStrategy strategy, IDictionary<string, object>? parameters = null, StrategyExecutionMode mode = StrategyExecutionMode.Suggestion);
    void RemoveStrategy(string instanceId);
    void PauseStrategy(string instanceId, bool paused);
    void SetExecutionMode(string instanceId, StrategyExecutionMode mode);
}
