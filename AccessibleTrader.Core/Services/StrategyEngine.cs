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
        private readonly Strategies.IStrategyPositionManager? _positions;
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
            Feeds.IMarketFeedHub? feedHub = null,
            Strategies.IStrategyPositionManager? positions = null)
        {
            _positions = positions;
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
            _indicatorCache.BeginSeries(state.Identity, state.Data.Count);
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
                    _indicatorCache.BeginSeries(feed.Identity, history.Count);
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
                    // ── EXITS BEFORE ENTRIES ──────────────────────────────────
                    // Same order the replay runs in (StrategyBacktester walks the bar's range
                    // against the open position before it asks the strategy anything), and for
                    // the same reason: a bar that reached the stop AND produced a fresh signal
                    // is a bar you were stopped out on, not one you reversed on.
                    RunManagedExits(active, newBar, history);

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

        /// <summary>
        /// Walks the closed bar against whatever this strategy currently holds and dispatches
        /// the reduce-only exits it earns — the stop, the ladder rungs, and the ATR trail the
        /// backtester has always simulated and the live path used to discard.
        ///
        /// <para>The walk itself is synchronous and happens under <c>_evalGate</c>: the
        /// bookkeeping has to be applied before the next bar can be evaluated, or a stop that
        /// has already fired fires again. Only the placement is dispatched off the gate.</para>
        /// </summary>
        private void RunManagedExits(ActiveStrategy active, Sdk.Models.Ohlcv bar, IReadOnlyList<Sdk.Models.Ohlcv> history)
        {
            if (_positions == null) return;

            var exits = _positions.OnBarClosed(active.InstanceId, bar, history);
            if (exits.Count == 0) return;

            SafeFireAndForget.Run(
                () => _positions.PlaceExitsAsync(exits),
                _msLogger,
                $"ManagedExits_{active.Strategy.Name}");
        }

        private async Task ExecuteSignalAsync(ActiveStrategy active, StrategySignal signal)
        {
            try
            {
                var state = _store.State;
                var providerName = state.Identity.Provider;
                if (string.IsNullOrEmpty(providerName)) return;

                // ── A signal with no size is not an order ────────────────────────
                // This used to default to 1.0, which on a live venue is one whole BTC,
                // one whole ETH or one whole contract — chosen by nobody, from a strategy
                // that simply did not set the field, in Auto mode with nobody at the
                // keyboard. 1.0 sails under MaxOrderQuantity, so GeneralOrderService's
                // sanity clamp waves it straight through. Refuse it, out loud: a strategy
                // that has not stated a size has not stated an order, and the author needs
                // to hear that rather than discover it in a fill.
                if (signal.Quantity is not double qty || !double.IsFinite(qty) || qty <= 0)
                {
                    _logger.LogError(
                        $"Auto-execute for strategy '{active.Strategy.Name}' refused: the signal carried no quantity.",
                        nameof(StrategyEngine));
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error,
                        $"{active.Strategy.Name} produced a {signal.Side} signal with no position size, so nothing "
                        + "was placed. The strategy must set Quantity on the signal.", true));
                    return;
                }

                // ── What is already open ─────────────────────────────────────────
                // This path used to place its order knowing nothing about the position the same
                // strategy already held. On a futures venue that pyramids — two positions, one
                // stop between them — and on a spot venue a Sell while flat is a naked sell the
                // exchange refuses. The replay reverses on a counter-signal; so does this now.
                string symbol = state.Identity.Symbol;
                if (_positions != null)
                {
                    var plan = _positions.PlanEntry(active, signal, qty, providerName, symbol);

                    if (plan.Message != null)
                    {
                        _logger.LogInfo(plan.Message, nameof(StrategyEngine));
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info, plan.Message, false));
                    }

                    if (plan.Disposition == Strategies.StrategyEntryDisposition.AlreadyOpen)
                        return;

                    if (plan.Disposition == Strategies.StrategyEntryDisposition.Reverse && plan.CloseFirst != null)
                    {
                        // Opening the reversed position while the old one is still on is exactly the
                        // pyramid this is here to prevent, so a refused close refuses the entry too.
                        bool closed = await _positions.PlaceExitsAsync(new[] { plan.CloseFirst })
                            .ConfigureAwait(false);
                        if (!closed)
                        {
                            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error,
                                $"{active.Strategy.Name} did not open its {signal.Side} position because the "
                                + "existing one could not be closed first.", true));
                            return;
                        }
                    }
                }

                var tradeSignal = new Sdk.Plugins.TradeSignal(
                    Symbol:     symbol,
                    Side:       signal.Side,
                    Quantity:   qty,
                    Type:       signal.OrderType,
                    Price:      signal.LimitPrice,
                    StopLoss:   signal.StopLoss,
                    // The FIRST rung only, and that has not changed: no broker takes a ladder on
                    // one order. What changed is that rungs two and three are no longer dropped —
                    // IStrategyPositionManager holds them and closes their portions as price
                    // reaches them. See ManagedExitRules.
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
                    return;
                }

                // The order went. Hand the position — and the whole exit plan the order could not
                // carry — to the manager, which walks it against every subsequent bar close.
                // The reference price is the close of the bar the signal was decided on; the real
                // fill replaces it when the venue reports it (OrderFilledEvent), because a
                // breakeven stop anchored on a price nobody traded at is not breakeven.
                if (_positions != null)
                {
                    double reference = LastClose(state);
                    _positions.OpenPosition(active, signal, qty, providerName, symbol, reference, result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Auto-execute failed for strategy '{active.Strategy.Name}': {ex.Message}",
                    nameof(StrategyEngine), ex);
            }
        }

        /// <summary>The most recent closed price on the workspace, or 0 when there is none.</summary>
        private static double LastClose(WorkspaceState state)
        {
            var data = state.Data;
            if (data == null || data.Count == 0) return 0;
            int idx = state.CurrentDataIndex;
            if (idx < 0 || idx >= data.Count) idx = data.Count - 1;
            return data[idx].Close;
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

            // A restart rebuilds every strategy flat. If this spec had a position open when the
            // process died, the broker still holds it — re-attach it here, BEFORE the first bar
            // can be evaluated, or the same conditions open a second one on top of the first with
            // the original order's stop the only protection either has.
            _positions?.Adopt(instanceId, specId);

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

                StopStrategy(active.Strategy);
                ReleaseStrategy(active.Strategy);
                _activeStrategies = _activeStrategies.RemoveAll(a => a.InstanceId == instanceId);
                _pendingSignals.Remove(instanceId);
                _lastSignalTimes.Remove(instanceId);
                // Removing the strategy stops the managed exits, so the record must go too —
                // otherwise it is persisted forever and re-adopted by the next instance of the
                // same spec, which would run a stop against a position the user has since closed.
                _positions?.Forget(instanceId);
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
                {
                    StopStrategy(active.Strategy);
                    ReleaseStrategy(active.Strategy);
                }
            }
        }

        /// <summary>
        /// The strategy's own teardown, which is allowed to fail.
        ///
        /// <para>
        /// A script strategy's <c>OnStop</c> is a round trip to a worker process, so it now throws
        /// for reasons that have nothing to do with the strategy's code — a worker the memory
        /// quota already killed, a pipe that broke. Letting that escape would abandon the rest of
        /// the removal (the worker never released, the managed position never forgotten) or, in
        /// <see cref="Dispose"/>, leave every strategy after this one running. Teardown reports
        /// and continues.
        /// </para>
        /// </summary>
        private void StopStrategy(ITradingStrategy strategy)
        {
            try { strategy.OnStop(); }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    $"Strategy '{strategy.Name}' threw from OnStop; carrying on with its teardown: {ex.Message}",
                    nameof(StrategyEngine));
            }
        }

        /// <summary>
        /// Releases whatever a strategy instance is holding beyond its own memory.
        ///
        /// <para>
        /// A script strategy is a proxy for code running in the sandbox worker, and dropping the
        /// reference does not end that process — it sits there holding one of the sixteen
        /// concurrency slots until the app exits, and a user who adds and removes four scripts a
        /// day runs out of slots without ever being told why. <c>OnStop</c> is the strategy's own
        /// teardown and deliberately does NOT kill the worker (the causality probe calls it
        /// between runs); this is the separate step that does.
        /// </para>
        /// </summary>
        private void ReleaseStrategy(ITradingStrategy strategy)
        {
            if (strategy is not IAsyncDisposable disposable) return;

            // Fire-and-forget: the worker's own DisposeAsync sends Shutdown, waits out a one
            // second grace window and then kills the process. Blocking a strategy removal — which
            // can run from the UI thread — on that is not worth the certainty.
            SafeFireAndForget.Run(
                async () => await disposable.DisposeAsync().ConfigureAwait(false),
                _msLogger,
                $"ReleaseStrategy_{strategy.Name}");
        }
    }
}
