namespace AccessibleTrader.Sdk.Strategies;

public record ActiveStrategy(
    string InstanceId,
    ITradingStrategy Strategy,
    IDictionary<string, object> Parameters,
    StrategyExecutionMode ExecutionMode,
    bool IsPaused,
    // The chart symbol this strategy is bound to (stamped from the active chart
    // when the strategy is started). The foreground engine only evaluates it while
    // that symbol is on screen; a background workspace monitor evaluates it while
    // its symbol is NOT on screen — exactly one driver at a time. Null/empty =
    // legacy behaviour (always evaluates against whatever chart is focused).
    string? Symbol = null,
    // The StrategySpec this instance was built from, when it came from the
    // library (null for ad-hoc compiled scripts). Workspace saves persist
    // active strategies by this id and re-activate them on load.
    string? SpecId = null
);

public interface IStrategyEngine
{
    IReadOnlyList<ActiveStrategy> ActiveStrategies { get; }
    /// <param name="specId">Library spec id for workspace persistence (null = ad-hoc).</param>
    /// <param name="bindSymbol">Explicit symbol binding for workspace RESTORE — normally
    /// the binding is stamped from the focused chart, but a restored strategy must bind
    /// to the symbol it was saved with, not whatever chart happens to be up.</param>
    string AddStrategy(ITradingStrategy strategy, IDictionary<string, object>? parameters = null,
        StrategyExecutionMode mode = StrategyExecutionMode.Suggestion,
        string? specId = null, string? bindSymbol = null);
    void RemoveStrategy(string instanceId);
    void PauseStrategy(string instanceId, bool paused);
    void SetExecutionMode(string instanceId, StrategyExecutionMode mode);
}
