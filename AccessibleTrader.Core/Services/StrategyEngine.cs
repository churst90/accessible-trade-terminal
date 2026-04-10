using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

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
            IDataManager dataManager,
            IWorkspaceStore store,
            IStrategyIndicatorCache indicatorCache)
        {
            _eventBus       = eventBus;
            _orderService   = orderService;
            _logger         = logger;
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
                        ExecuteSignalAsync(active, signal);
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

        private async void ExecuteSignalAsync(ActiveStrategy active, StrategySignal signal)
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

                await _orderService.PlaceOrderAsync(providerName, tradeSignal);
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
            StrategyExecutionMode mode = StrategyExecutionMode.Suggestion)
        {
            var instanceId = Guid.NewGuid().ToString("N");
            var @params = parameters ?? new Dictionary<string, object>();

            // Initialise with current history
            var state = _store.State;
            strategy.Initialize(state.Data, state, @params);

            var active = new ActiveStrategy(instanceId, strategy, @params, mode, IsPaused: false);
            _activeStrategies = _activeStrategies.Add(active);
            _logger.LogInfo($"Strategy '{strategy.Name}' added (id={instanceId}, mode={mode})", nameof(StrategyEngine));
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
