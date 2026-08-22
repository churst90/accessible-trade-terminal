using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Trading;
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
        private readonly Feeds.IMarketFeedHub? _feedHub;
        // Serializes ALL strategy evaluation — live bar-closes AND the
        // DataUpdated-driven load/tab-switch/prepend path. Strategies are
        // stateful; concurrent OnBar on one instance (and the unlocked signal
        // dictionaries) is never safe.
        private readonly object _evalGate = new();

        private ImmutableList<ActiveStrategy> _activeStrategies = ImmutableList<ActiveStrategy>.Empty;
        private readonly Dictionary<string, DateTime> _lastSignalTimes = new();
        // Pending signals awaiting user confirmation (Suggestion mode)
        private readonly Dictionary<string, StrategySignal> _pendingSignals = new();


        public IReadOnlyList<ActiveStrategy> ActiveStrategies => _activeStrategies;

        public StrategyEngine(
            IEventBus eventBus,
            IOrderExecutionService orderService,
            IAppLogger logger,
            ILogger<StrategyEngine> msLogger,
            IDataManager dataManager,
            IWorkspaceStore store,
            IStrategyIndicatorCache indicatorCache,
            Feeds.IMarketFeedHub? feedHub = null)
        {
            _eventBus       = eventBus;
            _orderService   = orderService;
            _logger         = logger;
            _msLogger       = msLogger;
            _dataManager    = dataManager;
            _store          = store;
            _indicatorCache = indicatorCache;

            _dataManager.DataUpdated += OnDataUpdated;

            // The live bar-close driver (2026-07-22 keyed-feeds fix): DataUpdated
            // has NEVER fired for live ticks, so focused-chart strategies only
            // evaluated on load/tab-switch/prepend. LiveAppend on the focused feed
            // means the previous bar just CLOSED — the correct, backtest-matching
            // moment to evaluate (see docs/KEYED_FEEDS_DESIGN.md).
            _feedHub = feedHub;
            if (_feedHub != null)
                _feedHub.FocusedFeedUpdated += OnFocusedFeedUpdated;

            // (StrategyConfirmedEvent subscription removed — event was never published)
        }

        private void OnDataUpdated()
        {
            if (_activeStrategies.IsEmpty) return;

            var state = _store.State;
            _indicatorCache.Invalidate(state.Data.Count);
            int idx = state.CurrentDataIndex;
            if (idx < 1 || idx >= state.Data.Count) return;

            lock (_evalGate)
            {
                EvaluateBar(state.Data[idx], state.Data, state);
            }
        }

        private void OnFocusedFeedUpdated(Feeds.ChartFeed feed, Feeds.FeedUpdateKind kind)
        {
            if (kind != Feeds.FeedUpdateKind.LiveAppend) return;
            if (_activeStrategies.IsEmpty) return;

            // Snapshot the buffer NOW; the closed bars in it never mutate.
            var bars = feed.Bars;
            if (bars.Count < 2) return;

            // Evaluate OFF the pump thread — this event fires while the feed's
            // prepend lock is held, and strategy evaluation (indicator math,
            // signal publication, auto-execution dispatch) must never block the
            // live merge path.
            SafeFireAndForget.Run(() =>
            {
                lock (_evalGate)
                {
                    var closedBar = bars[bars.Count - 2];
                    var history = new PrefixView(bars, bars.Count - 1);
                    _indicatorCache.Invalidate(history.Count);
                    EvaluateBar(closedBar, history, _store.State);
                }
                return System.Threading.Tasks.Task.CompletedTask;
            }, _msLogger, "LiveBarCloseEvaluation");
        }

        /// <summary>History view that excludes the still-forming live bar, so the
        /// bar-close path hands strategies the same shape a backtest replay does:
        /// the closed bar is the LAST element of its own history.</summary>
        private sealed class PrefixView : IReadOnlyList<Sdk.Models.Ohlcv>
        {
            private readonly TimeSeriesBuffer<Sdk.Models.Ohlcv> _bars;
            public PrefixView(TimeSeriesBuffer<Sdk.Models.Ohlcv> bars, int count) { _bars = bars; Count = count; }
            public int Count { get; }
            public Sdk.Models.Ohlcv this[int index] => index < Count
                ? _bars[index]
                : throw new ArgumentOutOfRangeException(nameof(index));
            public IEnumerator<Sdk.Models.Ohlcv> GetEnumerator()
            {
                for (int i = 0; i < Count; i++) yield return _bars[i];
            }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private void EvaluateBar(Sdk.Models.Ohlcv newBar, IReadOnlyList<Sdk.Models.Ohlcv> history, WorkspaceState state)
        {
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

                string result = await _orderService.PlaceOrderAsync(providerName, tradeSignal).ConfigureAwait(false);

                // ── Read the answer ──────────────────────────────────────────
                //
                // This call used to discard the return value, which is the same defect
                // QuickTradeExecutor:72 documents having fixed — and it is worse here, because
                // nobody is at the keyboard. A strategy in Auto mode announces its signal on the
                // event bus, then places the order; if the order is refused for want of a price,
                // for want of balance, or because the provider is not connected, the user has
                // heard the signal and will hear nothing else. Believing you hold a position you
                // do not hold is the most expensive wrong belief this application can create.
                string? failure = OrderResult.DescribeFailure(result);
                if (failure != null)
                {
                    _logger.LogError(
                        $"Auto-execute for strategy '{active.Strategy.Name}' was not placed: {result}",
                        nameof(StrategyEngine));
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error,
                        $"{active.Strategy.Name} could not place its {signal.Side} order. {failure}", true));
                }
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
            // Under _evalGate: OnStop must not run concurrently with OnBar on the same
            // instance (strategies are stateful), and the signal dictionaries are only safe
            // to mutate while the live bar-close evaluation — which runs on a threadpool
            // thread and also writes them — is not in flight.
            lock (_evalGate)
            {
                var active = _activeStrategies.FirstOrDefault(a => a.InstanceId == instanceId);
                if (active == null) return;

                active.Strategy.OnStop();
                _activeStrategies = _activeStrategies.RemoveAll(a => a.InstanceId == instanceId);
                _pendingSignals.Remove(instanceId);
                _lastSignalTimes.Remove(instanceId);
            }
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
            if (_feedHub != null)
                _feedHub.FocusedFeedUpdated -= OnFocusedFeedUpdated;

            // Unsubscribing above stops new evaluations being dispatched, but one may still
            // be running on a threadpool thread; take _evalGate so OnStop waits for it rather
            // than racing OnBar on the same (stateful) strategy instance.
            lock (_evalGate)
            {
                foreach (var active in _activeStrategies)
                    active.Strategy.OnStop();
            }
        }
    }
}
