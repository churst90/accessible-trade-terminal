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
        // The same condition evaluator the alert evaluator holds (one per composition root), so
        // the HTF data it loads here is the data the tree is evaluated against.
        private readonly Strategies.IConditionEvaluator? _conditionEvaluator;
        private IDisposable? _sub;
        private readonly List<AlertDefinition> _alerts;
        // Snapshot of indicator component values from the previous tick, keyed by
        // "IndicatorCode.ComponentName". Passed to EvaluateAlerts so crossover
        // conditions can compare current vs previous values. Previously this was
        // always an empty dict, which meant crossover alerts never fired.
        private Dictionary<string, double> _previousValues = new(StringComparer.OrdinalIgnoreCase);
        private bool _initialized = false;
        // The chart _previousValues was taken on. See EvaluateAlerts: a snapshot from another
        // chart is not "the tick before" on this one.
        private string? _previousChart;

        public AlertOrchestrator(
            IWorkspaceStore store,
            IAlertEvaluator evaluator,
            IEventBus eventBus,
            IWorkspaceLibraryService library,
            ILogger<AlertOrchestrator> logger,
            Strategies.IConditionEvaluator? conditionEvaluator = null)
        {
            _conditionEvaluator = conditionEvaluator;
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
                    // The remedy comes from the evaluator when it has one: "check the chart" is the
                    // right advice for a missing indicator and the wrong one for a weekly series the
                    // provider did not serve. Read on the same call that raised this event.
                    // An indicator that was removed will never be on the chart, so "check the
                    // chart" would send the user looking for something that cannot be there.
                    bool onRemoved = Indicators.RetiredIndicators.IsRetired(alert.IndicatorCode)
                                  || Indicators.RetiredIndicators.IsRetired(alert.LineIndicatorCode);
                    string remedy = onRemoved ? "Edit the alert or delete it."
                        : _conditionEvaluator?.LastDegradationRemedy
                        ?? "Check the indicator it references is on this chart.";
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error,
                        $"Alert '{alert.Name}' can't be fully evaluated and may stay silent: {reason}. {remedy}",
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
                // TimeBars too: on a Renko, range bar or line break chart the drawn bars move only
                // when one completes, and a price alert reads the real bars every tick.
                .Select(s => new { s.Data, s.TimeBars, s.ActiveSeries, s.InitStatus })
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
            // The market's bars: on a chart built off the clock, the real time bars behind the
            // bricks (AlertEvaluator.OnTheMarket reads them again; this only decides there is a
            // price to read). Indicator values below are still indexed on the drawn bars.
            var market = state.MarketBars;
            if (market == null || market.Count < 2) return;

            var lastBar = market[^1];
            var prevBar = market[^2];

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
            int idx = (state.Data?.Count ?? 0) - 1;

            string chart = ChartKey(state);
            if (!_initialized)
            {
                // First tick: populate _previousValues without firing any alerts.
                // This prevents false-positive crossover alerts on cold start where
                // the previous snapshot dict is empty (missing keys default to 0.0).
                _initialized = true;
                _previousValues = Snapshot(state, idx);
                _previousChart = chart;
                return; // skip alert evaluation on warm-up tick
            }

            // The snapshot belongs to the chart it was taken on. Switching from the 1-day tab to
            // the 4-hour one (or turning Heikin-Ashi on) used to compare the 1-day RSI with the
            // 4-hour RSI as if they were two ticks of one line, so "RSI crosses above 70" could
            // fire on a tab switch with no move in the market. A new chart starts its memory
            // from where it is; price alerts still read this tick, from the chart's own bars.
            if (!string.Equals(chart, _previousChart, StringComparison.Ordinal))
            {
                _previousValues = Snapshot(state, idx);
                _previousChart = chart;
            }

            // Only the alerts set on THIS chart: its symbol, and its timeframe and provider when
            // the alert records them (Alerts.AlertScope has the account of Cody's 61,450 alert
            // that fired on every BTCUSDT tab). A null Symbol means "any / current chart".
            var applicable = _alerts
                .Where(a => Alerts.AlertScope.AppliesTo(a, state.SymbolDisplayName, state.Identity))
                .ToList();

            applicable = Strategies.TreeAlertTimeframes.ReadyToEvaluate(applicable, _conditionEvaluator, state);

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
            _previousValues = Snapshot(state, idx);
        }

        /// <summary>Every computed component's value on the live bar, by code and by instance
        /// (an SMA 20 and an SMA 50 share the code key). Left-out parts too: an alert may watch one
        /// (ComponentInclusion.Computed).</summary>
        private static Dictionary<string, double> Snapshot(WorkspaceState state, int idx)
        {
            var next = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var series in state.ActiveSeries)
            {
                foreach (var comp in ComponentInclusion.Computed(series.Config))
                {
                    var data = series.GetComponentData(comp.Name);
                    if (data == null || idx < 0 || idx >= data.Length) continue;
                    double val = data[idx];
                    if (!double.IsNaN(val))
                    {
                        next[$"{series.IndicatorCode}.{comp.Name}"] = val;
                        next[AlertEvaluator.InstanceKey(series, comp.Name)] = val;
                    }
                }
            }
            return next;
        }

        /// <summary>What makes two ticks the same line: the chart, and how its bars are built.</summary>
        private static string ChartKey(WorkspaceState state) =>
            $"{state.Identity.Market}|{state.Identity.Provider}|{state.Identity.Symbol}|{state.Identity.Timeframe}|{state.ChartStyle}|{state.IsHeikinAshi}";

        public void Dispose() => Stop();
    }
}
