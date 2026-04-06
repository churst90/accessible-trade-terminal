using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public class AlertOrchestrator : IAlertOrchestrator, IDisposable
    {
        private readonly IWorkspaceStore _store;
        private readonly IAlertEvaluator _evaluator;
        private readonly IEventBus _eventBus;
        private readonly IWorkspaceLibraryService _library;
        private readonly ILogger<AlertOrchestrator> _logger;
        private IDisposable? _sub;
        private readonly List<AlertDefinition> _alerts;
        // Snapshot of indicator component values from the previous tick, keyed by
        // "IndicatorCode.ComponentName". Passed to EvaluateAlerts so crossover
        // conditions can compare current vs previous values. Previously this was
        // always an empty dict, which meant crossover alerts never fired.
        private Dictionary<string, double> _previousValues = new(StringComparer.OrdinalIgnoreCase);
        private bool _initialized = false;

        public AlertOrchestrator(
            IWorkspaceStore store,
            IAlertEvaluator evaluator,
            IEventBus eventBus,
            IWorkspaceLibraryService library,
            ILogger<AlertOrchestrator> logger)
        {
            _store = store;
            _evaluator = evaluator;
            _eventBus = eventBus;
            _library = library;
            _logger = logger;
            // Restore previously-saved alerts so they survive app restart.
            _alerts = _library.LoadAlerts();
        }

        public void Start()
        {
            _sub = _store.StateStream
                .Select(s => new { s.Data, s.ActiveSeries, s.InitStatus })
                .DistinctUntilChanged()
                .Subscribe(x => {
                    if (x.InitStatus == InitializationStatus.Ready)
                        EvaluateAlerts(_store.State);
                });
        }

        public void Stop() => _sub?.Dispose();

        public void AddAlert(AlertDefinition alert)
        {
            _alerts.Add(alert);
            _library.SaveAlerts(_alerts);
        }

        public void RemoveAlert(string id)
        {
            _alerts.RemoveAll(a => a.Id == id);
            _library.SaveAlerts(_alerts);
        }

        public IEnumerable<AlertDefinition> GetAlerts() => _alerts.ToList();

        private void EvaluateAlerts(WorkspaceState state)
        {
            var dataList = state.Data?.ToList();
            if (dataList == null || dataList.Count < 2) return;

            var lastBar = dataList[^1];
            var prevBar = dataList[^2];
            int idx = state.CurrentDataIndex;

            if (!_initialized)
            {
                // First tick: populate _previousValues without firing any alerts.
                // This prevents false-positive crossover alerts on cold start where
                // the previous snapshot dict is empty (missing keys default to 0.0).
                _initialized = true;
                var seed = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var series in state.ActiveSeries)
                {
                    foreach (var comp in series.Components)
                    {
                        var data = series.GetComponentData(comp.Name);
                        if (data == null || idx < 0 || idx >= data.Length) continue;
                        double val = data[idx];
                        if (!double.IsNaN(val))
                            seed[$"{series.IndicatorCode}.{comp.Name}"] = val;
                    }
                }
                _previousValues = seed;
                return; // skip alert evaluation on warm-up tick
            }

            var results = _evaluator.EvaluateAlerts(_alerts, state, lastBar, prevBar, _previousValues);

            foreach (var res in results)
            {
                _eventBus.Publish(new AlertFiredEvent(res));
            }

            // Snapshot current indicator component values for next tick's crossover comparison.
            var next = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var series in state.ActiveSeries)
            {
                foreach (var comp in series.Components)
                {
                    var data = series.GetComponentData(comp.Name);
                    if (data == null || idx < 0 || idx >= data.Length) continue;
                    double val = data[idx];
                    if (!double.IsNaN(val))
                        next[$"{series.IndicatorCode}.{comp.Name}"] = val;
                }
            }
            _previousValues = next;
        }

        public void Dispose() => Stop();
    }
}
