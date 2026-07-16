using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Workspace
{
    /// <summary>
    /// A self-contained evaluation loop for ONE non-focused workspace (tab): fetches
    /// its symbol's bars on a polling cadence, recomputes the tab's indicators, and
    /// evaluates that symbol's alerts and strategies — publishing the same
    /// AlertFiredEvent / Setup* events the focused pipeline publishes, so delivery
    /// (speech, earcons, Discord webhooks, journal) all Just Works.
    ///
    /// Architecture note (why this is NOT a WorkspaceStore refactor): the focused
    /// chart keeps the entire existing single-workspace pipeline untouched — store,
    /// DataManager, live stream, full sonification. A monitor never touches any of
    /// that; it builds its own private <see cref="WorkspaceState"/> from freshly
    /// fetched bars (the same technique the StrategyLab's WorkspaceFactory uses for
    /// backtests) and evaluates against it. Background workspaces therefore hear
    /// their EARCONS and SPEECH (alerts, setups) but never contribute to the
    /// sonification bed — playback and navigation audio belong exclusively to the
    /// focused chart, per the product's audio policy.
    ///
    /// Data cadence: polling via <see cref="IDataService.FetchOhlcvAsync"/> (which
    /// rides each provider's own rate limiter), NOT a second live websocket — so
    /// providers whose transport allows one subscription are never fought over, and
    /// N monitors cost N small REST calls per poll interval. Alerts and setups fire
    /// within one poll interval of the condition becoming true; at the daily/4h
    /// timeframes the validated strategies run on, that is indistinguishable from
    /// live. (A tick-level background feed would require the keyed-feed DataManager
    /// refactor documented in the multi-workspace spec — deliberately deferred.)
    /// </summary>
    public sealed class BackgroundWorkspaceMonitor : IDisposable
    {
        private readonly ChartIdentity _identity;
        private readonly string _symbolDisplayName;
        private readonly ImmutableList<ChartSeries> _seriesTemplate;
        private readonly IDataService _dataService;
        private readonly IIndicatorService _indicators;
        private readonly IAlertOrchestrator _alerts;
        private readonly IAlertEvaluator _alertEvaluator;
        private readonly IStrategyEngine _strategyEngine;
        private readonly IEventBus _eventBus;
        private readonly ILogger _logger;
        private readonly TimeSpan _pollInterval;
        private readonly int _barsToFetch;

        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;

        // Alert crossover memory ("previous tick" values), keyed
        // "IndicatorCode.ComponentName" — same contract as AlertOrchestrator.
        private readonly Dictionary<string, double> _previousValues = new();
        private bool _warmedUp;

        // Local signal dedup, mirroring StrategyEngine's 30-second throttle.
        private readonly Dictionary<string, DateTime> _lastSignalTimes = new();

        public ChartIdentity Identity => _identity;
        public string SymbolDisplayName => _symbolDisplayName;
        public DateTime? LastEvaluatedUtc { get; private set; }
        public int LastBarCount { get; private set; }
        public string? LastError { get; private set; }

        public BackgroundWorkspaceMonitor(
            ChartIdentity identity,
            string symbolDisplayName,
            ImmutableList<ChartSeries> seriesTemplate,
            IDataService dataService,
            IIndicatorService indicators,
            IAlertOrchestrator alerts,
            IAlertEvaluator alertEvaluator,
            IStrategyEngine strategyEngine,
            IEventBus eventBus,
            ILogger logger,
            TimeSpan pollInterval,
            int barsToFetch = 400)
        {
            _identity = identity;
            _symbolDisplayName = string.IsNullOrWhiteSpace(symbolDisplayName)
                ? identity.Symbol : symbolDisplayName;
            _seriesTemplate = seriesTemplate;
            _dataService = dataService;
            _indicators = indicators;
            _alerts = alerts;
            _alertEvaluator = alertEvaluator;
            _strategyEngine = strategyEngine;
            _eventBus = eventBus;
            _logger = logger;
            _pollInterval = pollInterval;
            _barsToFetch = Math.Clamp(barsToFetch, 50, 2000);
        }

        public void Start()
        {
            _loop ??= Task.Run(() => RunLoopAsync(_cts.Token));
        }

        private async Task RunLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await EvaluateOnceAsync(ct).ConfigureAwait(false);
                    LastError = null;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    // A failed poll must never kill the loop — providers hiccup.
                    LastError = ex.Message;
                    _logger.LogWarning(ex, "Background monitor poll failed for {Symbol}", _symbolDisplayName);
                }

                try { await Task.Delay(_pollInterval, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>One full poll: fetch → compute indicators → evaluate. Internal-visible for tests.</summary>
        internal async Task EvaluateOnceAsync(CancellationToken ct)
        {
            var (bars, _) = await _dataService.FetchOhlcvAsync(
                _identity.Provider,
                new MarketDataRequest(_identity.Market, _identity.Symbol, _identity.Timeframe, _barsToFetch))
                .ConfigureAwait(false);
            if (bars == null || bars.Count < 2) return;
            ct.ThrowIfCancellationRequested();

            var state = BuildState(bars);
            LastBarCount = bars.Count;
            LastEvaluatedUtc = DateTime.UtcNow;

            EvaluateAlerts(state, bars);
            EvaluateStrategies(state, bars);
        }

        /// <summary>
        /// Builds a private, fully-evaluable WorkspaceState from fresh bars: the tab's
        /// series template with every indicator recomputed against the new data. Never
        /// dispatched anywhere — this state exists only for evaluation.
        /// </summary>
        private WorkspaceState BuildState(List<Ohlcv> bars)
        {
            var span = bars.ToArray();
            var computed = ImmutableList.CreateBuilder<ChartSeries>();

            foreach (var series in _seriesTemplate)
            {
                if (string.IsNullOrEmpty(series.Config.IndicatorCode))
                {
                    computed.Add(series); // candles/price/volume — evaluators read bars directly
                    continue;
                }

                try
                {
                    var parameters = series.Config.Parameters
                        .ToDictionary(k => k.Key, v => (object)v.Value);
                    parameters["__symbol"] = _identity.Symbol;

                    var results = new Dictionary<string, double[]>();
                    var buffer = new IndicatorResultBuffer(results, span.Length);
                    _indicators.CalculateIndicator(series.Config.IndicatorCode, span, parameters, buffer);

                    computed.Add(series.WithData(new SeriesDataBuffer
                    {
                        SeriesId = series.Id,
                        ComponentData = results,
                    }));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background monitor: indicator {Code} failed for {Symbol}",
                        series.Config.IndicatorCode, _symbolDisplayName);
                    computed.Add(series); // keep the series; its leaves evaluate false
                }
            }

            return WorkspaceState.Initial with
            {
                Identity = _identity,
                SymbolDisplayName = _symbolDisplayName,
                Data = new TimeSeriesBuffer<Ohlcv>(bars),
                ActiveSeries = computed.ToImmutable(),
                CurrentDataIndex = bars.Count - 1,
                InitStatus = InitializationStatus.Ready,
                DataStatus = DataStatus.Ready,
            };
        }

        // ── Alerts ───────────────────────────────────────────────────────────

        private void EvaluateAlerts(WorkspaceState state, List<Ohlcv> bars)
        {
            // STRICT scoping: a monitor only evaluates alerts explicitly bound to its
            // symbol. Null-symbol ("any / current chart") alerts belong to the focused
            // pipeline — evaluating them here too would double-fire every alert.
            var applicable = _alerts.GetAlerts()
                .Where(a => a.IsActive
                         && a.Symbol != null
                         && string.Equals(a.Symbol, _symbolDisplayName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (applicable.Count == 0) { _warmedUp = false; _previousValues.Clear(); return; }

            var lastBar = bars[^1];
            var prevBar = bars[^2];

            if (!_warmedUp)
            {
                // First pass after (re)start: populate crossover memory without firing,
                // mirroring AlertOrchestrator's warm-up tick.
                SnapshotIndicatorValues(state);
                _warmedUp = true;
                return;
            }

            foreach (var fired in _alertEvaluator.EvaluateAlerts(applicable, state, lastBar, prevBar, _previousValues))
            {
                // The symbol prefix makes background alerts self-identifying in speech;
                // delivery channels get the structured Symbol field as usual.
                var announced = fired with
                {
                    Symbol = _symbolDisplayName,
                    SpeechText = $"{_symbolDisplayName}: {fired.SpeechText}",
                };
                _eventBus.Publish(new AlertFiredEvent(announced));
            }

            SnapshotIndicatorValues(state);
        }

        private void SnapshotIndicatorValues(WorkspaceState state)
        {
            int idx = state.CurrentDataIndex;
            foreach (var series in state.ActiveSeries)
            {
                if (string.IsNullOrEmpty(series.Config.IndicatorCode)) continue;
                foreach (var comp in series.Config.Components)
                {
                    var data = series.GetComponentData(comp.Name);
                    if (data == null || idx < 0 || idx >= data.Length) continue;
                    _previousValues[$"{series.Config.IndicatorCode}.{comp.Name}"] = data[idx];
                }
            }
        }

        // ── Strategies ───────────────────────────────────────────────────────

        private void EvaluateStrategies(WorkspaceState state, List<Ohlcv> bars)
        {
            var lastBar = bars[^1];
            IReadOnlyList<Ohlcv> history = bars;

            foreach (var active in _strategyEngine.ActiveStrategies)
            {
                if (active.IsPaused) continue;
                // Only symbol-bound strategies matching THIS monitor. The foreground
                // engine skips these while their chart isn't focused (see
                // StrategyEngine.OnDataUpdated), so exactly one driver runs at a time.
                if (string.IsNullOrEmpty(active.Symbol)
                    || !string.Equals(active.Symbol, _symbolDisplayName, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var signal = active.Strategy.OnBar(lastBar, history, state);
                    if (signal == null) continue;

                    if (_lastSignalTimes.TryGetValue(active.InstanceId, out var last)
                        && (DateTime.UtcNow - last).TotalSeconds < 30)
                        continue;
                    _lastSignalTimes[active.InstanceId] = DateTime.UtcNow;

                    // Background signals are announce-only: even an Auto-mode strategy
                    // never places orders from a background monitor. Auto execution
                    // stays a focused-chart, eyes-on (ears-on) act by design.
                    _eventBus.Publish(new StrategySignalEvent(active.Strategy.Name, signal, active.InstanceId));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background monitor: strategy {Name} threw for {Symbol}",
                        active.Strategy.Name, _symbolDisplayName);
                }
            }
        }

        public void Dispose()
        {
            // Cancel, but only dispose the CTS after the loop task has actually
            // finished — disposing while the loop is inside Task.Delay(token)
            // races the token registration and throws ObjectDisposedException
            // (surfaced as an unobserved-task error during app shutdown / Ctrl+C).
            try { _cts.Cancel(); } catch (ObjectDisposedException) { }
            var loop = _loop;
            if (loop == null || loop.IsCompleted)
                _cts.Dispose();
            else
                loop.ContinueWith(_ => _cts.Dispose(), TaskScheduler.Default);
        }
    }
}
