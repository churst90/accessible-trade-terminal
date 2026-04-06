using System.Collections.Generic;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Interfaces;

public interface IAlertOrchestrator
{
    void Start();
    void Stop();
    void AddAlert(AlertDefinition alert);
    void RemoveAlert(string id);
    IEnumerable<AlertDefinition> GetAlerts();
}
