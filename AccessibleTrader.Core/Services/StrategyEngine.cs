using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Implements IStrategyEngine.
    /// Listens for new bar data via IDataManager.DataUpdated and evaluates active strategies bar-by-bar.
    /// In Suggestion mode: publishes StrategySignalEvent for user confirmation.
    /// In Auto mode: publishes StrategySignalEvent then routes to IOrderExecutionService.
    /// </summary>
    public class StrategyEngine : IStrategyEngine, IDisposable
    {
        private readonly IEventBus _eventBus;
        private readonly IOrderExecutionService _orderService;
        private readonly IAppLogger _logger;
        private readonly ILogger<StrategyEngine> _msLogger;
        private readonly IDataManager _dataManager;
        private readonly IWorkspaceStore _store;
        private readonly IStrategyIndicatorCache _indicatorCache;

        private ImmutableList<ActiveStrategy> _activeStrategies = ImmutableList<ActiveStrategy>.Empty;
        private readonly Dictionary<string, DateTime> _lastSignalTimes = new();
        // Pending signals awaiting user confirmation (Suggestion mode)
        private readonly Dictionary<string, StrategySignal> _pendingSignals = new();

        private readonly System.Reactive.Disposables.CompositeDisposable _subscriptions = new();

        public IReadOnlyList<ActiveStrategy> ActiveStrategies => _activeStrategies;

        public StrategyEngine(
            IEventBus eventBus,
            IOrderExecutionService orderService,
            IAppLogger logger,
            ILogger<StrategyEngine> msLogger,
            IDataManager dataManager,
            IWorkspaceStore store,
            IStrategyIndicatorCache indicatorCache)
        {
            _eventBus       = eventBus;
            _orderService   = orderService;
            _logger         = logger;
            _msLogger       = msLogger;
            _dataManager    = dataManager;
            _store          = store;
            _indicatorCache = indicatorCache;

            _dataManager.DataUpdated += OnDataUpdated;

            // (StrategyConfirmedEvent subscription removed — event was never published)
        }

        private void OnDataUpdated()
        {
            if (_activeStrategies.IsEmpty) return;

            var state = _store.State;
            _indicatorCache.Invalidate(state.Data.Count);
            int idx = state.CurrentDataIndex;
            if (idx < 1 || idx >= state.Data.Count) return;

            var newBar  = state.Data[idx];
            var history = (IReadOnlyList<Sdk.Models.Ohlcv>)state.Data;

            foreach (var active in _activeStrategies)
            {
                if (active.IsPaused) continue;

                // Symbol-bound instances only evaluate while their chart is focused —
                // evaluating a KAS strategy against BTC bars was the cross-contamination
                // this closes. Background monitors evaluate the non-focused ones.
                if (!string.IsNullOrEmpty(active.Symbol)
                    && !string.Equals(active.Symbol, state.SymbolDisplayName, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var signal = active.Strategy.OnBar(newBar, history, state);
                    if (signal == null) continue;

                    // Deduplication: skip if a signal was already published for this instance recently
                    if (_lastSignalTimes.TryGetValue(active.InstanceId, out var last)
                        && (DateTime.UtcNow - last).TotalSeconds < 30)
                        continue;

                    _lastSignalTimes[active.InstanceId] = DateTime.UtcNow;
                    _logger.LogInfo($"Strategy '{active.Strategy.Name}' signal: {signal.Side} — {signal.Rationale}",
                        nameof(StrategyEngine));

                    _eventBus.Publish(new StrategySignalEvent(active.Strategy.Name, signal, active.InstanceId));

                    if (active.ExecutionMode == StrategyExecutionMode.Auto)
                    {
                        SafeFireAndForget.Run(
                            () => ExecuteSignalAsync(active, signal),
                            _msLogger,
                            $"ExecuteSignal_{active.Strategy.Name}");
                    }
                    else
                    {
                        // Store for later confirmation
                        _pendingSignals[active.InstanceId] = signal;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Strategy '{active.Strategy.Name}' threw: {ex.Message}", nameof(StrategyEngine), ex);
                }
            }
        }

        private async Task ExecuteSignalAsync(ActiveStrategy active, StrategySignal signal)
        {
            try
            {
                var state = _store.State;
                var providerName = state.Identity.Provider;
                if (string.IsNullOrEmpty(providerName)) return;

                var tradeSignal = new Sdk.Plugins.TradeSignal(
                    Symbol:     state.Identity.Symbol,
                    Side:       signal.Side,
                    Quantity:   signal.Quantity ?? 1.0,
                    Type:       signal.OrderType,
                    Price:      signal.LimitPrice,
                    StopLoss:   signal.StopLoss,
                    TakeProfit: signal.TakeProfit
                );

                await _orderService.PlaceOrderAsync(providerName, tradeSignal).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Auto-execute failed for strategy '{active.Strategy.Name}': {ex.Message}",
                    nameof(StrategyEngine), ex);
            }
        }

        // ── IStrategyEngine ───────────────────────────────────────────────────

        public string AddStrategy(
            ITradingStrategy strategy,
            IDictionary<string, object>? parameters = null,
            StrategyExecutionMode mode = StrategyExecutionMode.Suggestion,
            string? specId = null,
            string? bindSymbol = null)
        {
            var instanceId = Guid.NewGuid().ToString("N");
            var @params = parameters ?? new Dictionary<string, object>();

            // Initialise with current history
            var state = _store.State;
            strategy.Initialize(state.Data, state, @params);

            // Bind the instance to the chart it was started on. The foreground engine
            // evaluates it only while that symbol is focused; a background workspace
            // monitor picks it up while the symbol is NOT focused — one driver at a
            // time (see BackgroundWorkspaceMonitor). Empty symbol (blank chart) keeps
            // the legacy always-evaluate behaviour.
            string? boundSymbol = bindSymbol
                ?? (string.IsNullOrWhiteSpace(state.SymbolDisplayName) ? null : state.SymbolDisplayName);

            var active = new ActiveStrategy(instanceId, strategy, @params, mode, IsPaused: false, boundSymbol, specId);
            _activeStrategies = _activeStrategies.Add(active);
            _logger.LogInfo($"Strategy '{strategy.Name}' added (id={instanceId}, mode={mode}, symbol={boundSymbol ?? "any"})", nameof(StrategyEngine));
            return instanceId;
        }

        public void RemoveStrategy(string instanceId)
        {
            var active = _activeStrategies.FirstOrDefault(a => a.InstanceId == instanceId);
            if (active == null) return;

            active.Strategy.OnStop();
            _activeStrategies = _activeStrategies.RemoveAll(a => a.InstanceId == instanceId);
            _pendingSignals.Remove(instanceId);
            _lastSignalTimes.Remove(instanceId);
        }

        public void PauseStrategy(string instanceId, bool paused)
        {
            _activeStrategies = _activeStrategies.Select(a =>
                a.InstanceId == instanceId ? a with { IsPaused = paused } : a
            ).ToImmutableList();
        }

        public void SetExecutionMode(string instanceId, StrategyExecutionMode mode)
        {
            _activeStrategies = _activeStrategies.Select(a =>
                a.InstanceId == instanceId ? a with { ExecutionMode = mode } : a
            ).ToImmutableList();
        }

        public void Dispose()
        {
            _dataManager.DataUpdated -= OnDataUpdated;
            _subscriptions.Dispose();

            foreach (var active in _activeStrategies)
                active.Strategy.OnStop();
        }
    }
}
