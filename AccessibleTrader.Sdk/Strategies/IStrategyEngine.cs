using System.Collections.Generic;

namespace AccessibleTrader.Sdk.Strategies;

public record ActiveStrategy(
    string InstanceId,
    ITradingStrategy Strategy,
    IDictionary<string, object> Parameters,
    StrategyExecutionMode ExecutionMode,
    bool IsPaused
);

public interface IStrategyEngine
{
    IReadOnlyList<ActiveStrategy> ActiveStrategies { get; }
    string AddStrategy(ITradingStrategy strategy, IDictionary<string, object>? parameters = null, StrategyExecutionMode mode = StrategyExecutionMode.Suggestion);
    void RemoveStrategy(string instanceId);
    void PauseStrategy(string instanceId, bool paused);
    void SetExecutionMode(string instanceId, StrategyExecutionMode mode);
}
