using AccessibleTrader.Sdk.Alerts;

namespace AccessibleTrader.Sdk.Interfaces;

public interface IAlertOrchestrator
{
    void Start();
    void Stop();
    void AddAlert(AlertDefinition alert);
    void RemoveAlert(string id);
    IEnumerable<AlertDefinition> GetAlerts();
}
