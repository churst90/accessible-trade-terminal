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

            // Broken alert rules must be HEARD once, not buried in debug logs:
            // first failure per alert id speaks + journals; repeats stay silent
            // until the alert is edited (which resets the gate via Add/Remove).
            if (_evaluator is AlertEvaluator concrete)
            {
                concrete.EvaluationFailed += (alert, ex) =>
                {
                    if (!_announcedFailures.Add(alert.Id)) return;
                    _logger.LogWarning(ex, "Alert '{Name}' ({Id}) failed to evaluate.", alert.Name, alert.Id);
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error,
                        $"Alert '{alert.Name}' can't be evaluated and will stay silent: {ex.Message}. Edit or delete it in the Alerts dialog.",
                        true));
                };

                // A tree alert that cannot answer one of its leaves stays false forever and
                // is indistinguishable from a market that never met it. Same treatment as an
                // outright failure — said once, then quiet — because "your alert is not
                // actually watching" is information the user has to have to act on.
                concrete.EvaluationDegraded += (alert, reason) =>
                {
                    _logger.LogWarning("Alert '{Name}' ({Id}) is degraded: {Reason}", alert.Name, alert.Id, reason);
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error,
                        $"Alert '{alert.Name}' can't be fully evaluated and may stay silent: {reason}. " +
                        "Check the indicator it references is on this chart.",
                        true));
                };
            }
        }

        private readonly HashSet<string> _announcedFailures = new();

        /// <summary>
        /// Arms in-session alert evaluation. Called once per composition root by
        /// <c>AppStartupService</c> — singleton on the MAUI head, per-circuit on the
        /// WebHost, matching this service's own DI lifetime in each.
        ///
        /// Idempotent on purpose. AppStartupService is itself reachable twice on the
        /// MAUI head (MainPage at launch and MainLayout on first render), and while it
        /// guards the body behind a shared Task, a second Start() here would leak the
        /// first subscription and evaluate — and therefore FIRE — every alert twice.
        /// </summary>
        public void Start()
        {
            if (_sub != null) return;

            _sub = _store.StateStream
                .Select(s => new { s.Data, s.ActiveSeries, s.InitStatus })
                .DistinctUntilChanged()
                .Subscribe(x => {
                    if (x.InitStatus == InitializationStatus.Ready)
                        EvaluateAlerts(_store.State);
                });
        }

        public void Stop()
        {
            _sub?.Dispose();
            _sub = null;
        }

        public void AddAlert(AlertDefinition alert)
        {
            // An edited alert gets a fresh announcement for BOTH gates — otherwise a user
            // who fixes the reason an alert was degraded is never told it is still degraded
            // for a different reason.
            _announcedFailures.Remove(alert.Id);
            (_evaluator as AlertEvaluator)?.ResetDegradationGate(alert.Id);

            _alerts.Add(alert);
            _library.SaveAlerts(_alerts);
        }

        public void RemoveAlert(string id)
        {
            _announcedFailures.Remove(id);
            (_evaluator as AlertEvaluator)?.ResetDegradationGate(id);

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

            // The LIVE BAR, not the navigation cursor.
            //
            // This read `state.CurrentDataIndex`, which is the user's keyboard cursor:
            // PointNavigationStrategy moves it on every arrow key, and ViewportReducer
            // explicitly refuses to move it on live data ("Preserve user focus — do NOT move
            // the cursor based on live data arrivals"). So while price alerts used data[^1],
            // an Indicator-target alert evaluated whatever bar the user happened to be parked
            // on. Arrow-key back 200 bars to inspect history and "RSI crosses 70" watched bar
            // N-200 forever.
            //
            // Worse: _previousValues was snapshotted at the OLD cursor and compared against
            // the value at the NEW one, so moving the cursor across a threshold between two
            // ticks SYNTHESISED A CROSSOVER THAT NEVER HAPPENED IN THE MARKET — an alert
            // fired, and spoke, about a price movement that did not occur.
            //
            // BackgroundWorkspaceMonitor already evaluated at Data.Count - 1 in its own
            // private state, which is why it was unaffected and why this is the right index.
            int idx = dataList.Count - 1;

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

            // Part A — symbol gating. Until multi-workspace lands, alerts still only
            // fire for the on-screen symbol, but an alert scoped to a specific Symbol is
            // skipped entirely when the current chart's SymbolDisplayName doesn't match
            // (case-insensitive). A null Symbol means "any / current chart" (back-compat).
            var applicable = _alerts
                .Where(a => a.Symbol == null
                         || string.Equals(a.Symbol, state.SymbolDisplayName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var results = _evaluator.EvaluateAlerts(applicable, state, lastBar, prevBar, _previousValues);

            foreach (var res in results)
            {
                // Stamp the firing symbol (the on-screen chart) so delivery payloads and
                // per-asset webhook routing know which market fired, even for "any"-scoped alerts.
                var enriched = res.Symbol == null && !string.IsNullOrEmpty(state.SymbolDisplayName)
                    ? res with { Symbol = state.SymbolDisplayName }
                    : res;
                _eventBus.Publish(new AlertFiredEvent(enriched));
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
