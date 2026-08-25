using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Alerts;

public interface IAlertEvaluator
{
    IEnumerable<AlertFired> EvaluateAlerts(
        IReadOnlyList<AlertDefinition> alerts,
        WorkspaceState state,
        Ohlcv newBar,
        Ohlcv previousBar,
        IReadOnlyDictionary<string, double> previousValues);
}
