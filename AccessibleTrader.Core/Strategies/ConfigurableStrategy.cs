using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Strategies.BuiltIn;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Strategies;

/// <summary>
/// The signal-composer-driven strategy. Wraps a <see cref="StrategySpec"/> (condition tree +
/// risk plan + side + execution mode) and runs it through the existing <c>IStrategyEngine</c>
/// pipeline. Owns the per-setup state machine:
///
///   Inactive + conditions true →
///     resolve risk → if R:R gate clears
///       → if EntryTrigger.Immediate → transition to Active, emit StrategySignal,
///                                     publish SetupConfirmedEvent
///       → else                      → transition to Armed, publish SetupArmedEvent
///                                     (no order yet — waiting for entry trigger)
///   Armed    + conditions true →
///     check entry trigger this bar
///       → fired  → transition to Active, emit StrategySignal,
///                  publish SetupEntryReachedEvent
///       → not fired → publish SetupReconfirmedEvent (heartbeat)
///   Armed    + conditions false →
///     transition back to Inactive (dropouts already published below)
///   Active   + conditions true  →
///     publish SetupReconfirmedEvent (no new order — engine dedup would block anyway)
///   Active   + conditions false →
///     transition back to Inactive
///
/// The Inactive→Armed→Active path is the user's "I see the setup forming, now I know
/// when I can actually enter" loop. Per the user directive, setups have NO time-based
/// expiration — Armed remains valid indefinitely until either the trigger fires or the
/// underlying conditions invalidate.
/// </summary>
public class ConfigurableStrategy : BaseStrategy
{
    private enum SetupState { Inactive, Armed, Active }

    private readonly StrategySpec _spec;
    private readonly IConditionEvaluator _evaluator;
    private readonly IRiskPlanResolver _resolver;
    private readonly ISignalCatalog _catalog;
    private readonly IEventBus _eventBus;
    private readonly IMultiTimeframeDataService? _mtf;
    private readonly string _instanceId;

    // Setup state machine.
    private SetupState _state = SetupState.Inactive;
    private int  _barsSinceFirstConfirm;
    private int  _barsSinceArmed;
    private ResolvedRiskPlan? _armedPlan;
    private Dictionary<string, bool> _lastLeafResults = new();

    // Cached at construction: true when every leaf in the spec uses a one-bar transient operator
    // (Fired, CrossesAbove/Below, CrossesAbove/BelowLine, ChangesDirection, PriceBreaks/Rejects
    // Level, WickIntoLvn). For these trees the "true" state is structurally a single-bar pulse,
    // which has two consequences:
    //
    //  (1) An EntryTrigger of anything other than Immediate is unsatisfiable — by the next bar
    //      the conditions are already false again, so the Armed→Active path can never fire and
    //      the strategy produces zero trades. We auto-promote to Immediate execution.
    //
    //  (2) The natural per-bar drop-off is not an actionable "setup is losing strength" event —
    //      it's just how pulse markers work. Publishing SetupDroppedEvent every single bar
    //      after a pulse fires produces the constant "X dropped off" speech the user reported.
    //      We suppress dropout publication for pure-pulse trees.
    //
    // FiredWithin is excluded from the "pulse" set because it explicitly debounces over N bars
    // and is the documented workaround for users who want a persistent setup window.
    private readonly bool _isPurePulseTree;

    // HTF pre-warm gate. Initialize() kicks off PrewarmIndicatorAsync / GetBarsAsync for every
    // (timeframe, indicator) the spec references, but those are async. Until they resolve, the
    // synchronous evaluator on the hot path reads NaN for HTF leaves — NaN-compare fails the
    // condition silently, producing phantom non-fires (and with NOT groups, phantom fires).
    // This gate blocks OnBar from evaluating HTF leaves until every pre-warm task has completed;
    // during the warm-up window a per-lifetime speech announcement is emitted once so the user
    // knows why their strategy hasn't fired yet.
    private readonly List<Task> _prewarmTasks = new();
    private volatile bool _prewarmComplete = false;
    private volatile bool _prewarmNotified = false;
    private bool _hasHtfLeaves = false;

    // Pure-pulse auto-promotion notification. BuildSetupTab's ValidateForSave() now blocks save
    // when a pure-pulse tree has a non-Immediate entry trigger, but legacy specs loaded from
    // disk (predating that validation, or imported .atstrat files from older versions) can
    // still land in the engine with the mismatch. When the auto-promotion fires at setup
    // confirmation time, announce once per instance so the user knows their configured
    // trigger was overridden.
    private volatile bool _autoPromotionNotified = false;

    // Live warmup gate (2026-06-12 audit fix 4). The backtester drops signals during the
    // first WarmupBars (computed by IBacktestWarmupAnalyzer from each referenced indicator's
    // stability window), but the live engine had no equivalent — a strategy loaded onto a
    // fresh chart with fewer bars than its slowest indicator needs (SMA-200, Cipher B's
    // 500-bar percentile mode) evaluated unstable values and could fire phantom setups.
    // The factory passes the same analyzer-computed bar count used by backtests; OnBar
    // returns null until the chart history is at least that deep, with a one-time
    // announcement so the user knows why nothing is firing.
    private readonly int _liveWarmupBars;
    private volatile bool _liveWarmupNotified = false;

    public override string Id => _spec.Id;
    public override string Name => _spec.Name;
    public override string Description => _spec.Description;
    public override StrategyComplexityLevel Complexity => StrategyComplexityLevel.Advanced;

    // ConfigurableStrategy is configured entirely via its constructor — no parameter dialog.
    // The builder UI (Session D) produces the StrategySpec directly.
    public override IReadOnlyList<StrategyParameter> Parameters { get; } = Array.Empty<StrategyParameter>();

    public ConfigurableStrategy(
        StrategySpec spec,
        IConditionEvaluator evaluator,
        IRiskPlanResolver resolver,
        ISignalCatalog catalog,
        IEventBus eventBus,
        string instanceId,
        IMultiTimeframeDataService? mtf = null,
        int liveWarmupBars = 0)
    {
        _spec       = spec;
        _evaluator  = evaluator;
        _resolver   = resolver;
        _catalog    = catalog;
        _eventBus   = eventBus;
        _instanceId = instanceId;
        _mtf        = mtf;
        _liveWarmupBars = Math.Max(0, liveWarmupBars);
        _isPurePulseTree = IsPurePulseTree(spec.Conditions);
    }

    private static bool IsPurePulseTree(ConditionNode node)
    {
        switch (node)
        {
            case ConditionLeaf l:
                return l.Operator switch
                {
                    LeafOperator.Fired             => true,
                    LeafOperator.CrossesAbove      => true,
                    LeafOperator.CrossesBelow      => true,
                    LeafOperator.CrossesAboveLine  => true,
                    LeafOperator.CrossesBelowLine  => true,
                    LeafOperator.ChangesDirection  => true,
                    LeafOperator.PriceBreaksLevel  => true,
                    LeafOperator.PriceRejectsLevel => true,
                    LeafOperator.WickIntoLvn       => true,
                    _ => false
                };
            case ConditionGroup g:
                if (g.Children.Count == 0) return false;
                foreach (var c in g.Children)
                    if (!IsPurePulseTree(c)) return false;
                return true;
            default:
                return false;
        }
    }

    public override void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameterValues)
    {
        // No parameters — spec is fully specified at construction time.
        _state = SetupState.Inactive;
        _barsSinceFirstConfirm = 0;
        _barsSinceArmed = 0;
        _armedPlan = null;
        _lastLeafResults = new Dictionary<string, bool>();

        // Pre-warm HTF indicator computation cache for every (Timeframe, IndicatorCode) pair the
        // spec references. Fire-and-forget: the synchronous evaluator will read these on the hot
        // path once the cache populates. Until then, HTF leaves fall through to active-TF.
        if (_mtf == null)
        {
            // Check if the spec actually needs HTF data — warn if so, since results will silently degrade.
            var htfCheck = new HashSet<string>();
            CollectHtfTimeframes(_spec.Conditions, htfCheck);
            if (htfCheck.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ConfigurableStrategy] WARNING: Strategy '{_spec.Name}' ({_spec.Id}) references HTF timeframes " +
                    $"[{string.Join(", ", htfCheck)}] but IMultiTimeframeDataService is null. " +
                    "HTF leaves will silently fall through to active-timeframe data.");
            }
        }
        // Reset the pre-warm gate for this init. If the spec has no HTF leaves, flag as
        // complete immediately so OnBar never blocks on HTF evaluation.
        _prewarmTasks.Clear();
        _prewarmComplete = false;
        _prewarmNotified = false;
        _liveWarmupNotified = false;
        var htfTimeframes = new HashSet<string>();
        CollectHtfTimeframes(_spec.Conditions, htfTimeframes);
        _hasHtfLeaves = htfTimeframes.Count > 0;

        if (_mtf != null)
        {
            string market   = state.Identity.Market   ?? string.Empty;
            string provider = state.Identity.Provider ?? string.Empty;
            string symbol   = state.Identity.Symbol   ?? string.Empty;
            int prewarmCount = System.Math.Max(200, history?.Count ?? 200);

            var seenIndicator = new HashSet<(string tf, string code)>();
            var seenTimeframe = new HashSet<string>();
            CollectHtfPairs(_spec.Conditions, seenIndicator, seenTimeframe);

            // Indicator pre-warm: each unique (timeframe, indicatorCode) gets one async compute.
            // Tasks are tracked so OnBar can gate HTF-leaf evaluation on their completion.
            foreach (var (tf, code) in seenIndicator)
            {
                _prewarmTasks.Add(_mtf.PrewarmIndicatorAsync(
                    market, provider, symbol, tf, code,
                    new Dictionary<string, object>(), prewarmCount));
            }

            // Bar-only pre-warm: each unique HTF that ANY leaf references (even price-only leaves
            // that don't need an indicator computation) gets a raw bar fetch. Without this, the
            // first few bars after strategy add evaluate against an empty MTF bar cache and the
            // price-only HTF path falls through to active-TF data with the one-time warning.
            // GetBarsAsync is idempotent — if the bar cache for this (provider|symbol|tf) is
            // already populated and fresh, the call is a no-op. Carryover from Session B.
            foreach (var tf in seenTimeframe)
            {
                _prewarmTasks.Add(_mtf.GetBarsAsync(market, provider, symbol, tf, prewarmCount));
            }
        }

        // If there are no HTF leaves (or no MTF service) the gate is never active.
        if (!_hasHtfLeaves || _prewarmTasks.Count == 0)
            _prewarmComplete = true;
    }

    /// <summary>
    /// True once every HTF pre-warm task kicked off in <see cref="Initialize"/> has completed.
    /// Strategies with no HTF leaves (or no multi-timeframe service) report <c>true</c>
    /// immediately. Used by <see cref="ITradingStrategy.OnBar"/> to block condition evaluation while the HTF
    /// cache is still populating — without this gate, NaN reads on unwarmed HTF leaves silently
    /// flip condition results and produce phantom fires/non-fires in the first few bars after
    /// the strategy is loaded. Also exposed so hosts (<c>StrategyBacktester</c>, the strategy
    /// manager modal) can surface a "warming up" indicator to the user.
    /// </summary>
    public bool IsPrewarmComplete
    {
        get
        {
            if (_prewarmComplete) return true;
            if (_prewarmTasks.Count == 0) { _prewarmComplete = true; return true; }
            // Don't await here — we're on the hot path. Sample the task states cheaply.
            foreach (var t in _prewarmTasks)
                if (!t.IsCompleted) return false;
            _prewarmComplete = true;
            return true;
        }
    }

    /// <summary>
    /// Walks the condition tree collecting unique timeframe strings from leaves that have a
    /// non-null Timeframe. Lightweight version of CollectHtfPairs used only for the null-MTF
    /// warning — no catalog lookup needed.
    /// </summary>
    private static void CollectHtfTimeframes(ConditionNode node, HashSet<string> sink)
    {
        switch (node)
        {
            case ConditionLeaf leaf when !string.IsNullOrEmpty(leaf.Timeframe):
                sink.Add(leaf.Timeframe!);
                break;
            case ConditionGroup g:
                foreach (var c in g.Children) CollectHtfTimeframes(c, sink);
                break;
        }
    }

    /// <summary>
    /// Walks the condition tree collecting unique (Timeframe, IndicatorCode) pairs from leaves
    /// that have a Timeframe set. Used by Initialize to pre-warm the HTF indicator cache.
    /// </summary>
    private void CollectHtfPairs(ConditionNode node,
        HashSet<(string tf, string code)> indicatorSink,
        HashSet<string> timeframeSink)
    {
        switch (node)
        {
            case ConditionLeaf leaf when !string.IsNullOrEmpty(leaf.Timeframe):
            {
                timeframeSink.Add(leaf.Timeframe!);
                var desc = _catalog.GetById(leaf.SignalDescriptorId);
                if (desc != null && !string.IsNullOrEmpty(desc.IndicatorCode))
                    indicatorSink.Add((leaf.Timeframe!, desc.IndicatorCode));
                break;
            }
            case ConditionGroup g:
            {
                foreach (var c in g.Children) CollectHtfPairs(c, indicatorSink, timeframeSink);
                break;
            }
        }
    }

    protected override StrategySignal? ComputeSignal(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
    {
        // During backtest replay we still run the full state machine (so trades are simulated
        // correctly) but we skip every IEventBus publication — otherwise SetupSonifier speaks
        // thousands of Armed/Dropped/Reconfirm events for replayed bars. The backtester stamps
        // this flag in StrategyBacktester.Run.
        bool publishEvents = !state.IsBacktesting;

        // Live warmup gate — mirror of the backtester's `i < warmupBars` drop. Indicators
        // referenced by the spec need their stability window of history before their values
        // mean anything; the backtest already refuses to trade inside that window, and live
        // must not be looser than the simulation that validated the strategy.
        if (!state.IsBacktesting && _liveWarmupBars > 0 && history.Count < _liveWarmupBars)
        {
            if (!_liveWarmupNotified)
            {
                _liveWarmupNotified = true;
                if (publishEvents)
                    _eventBus.Publish(new FeedbackRequestEvent(
                        FeedbackType.Info,
                        $"Strategy '{_spec.Name}': indicators warming up — {history.Count} of " +
                        $"{_liveWarmupBars} bars loaded. Signals begin once warm."));
            }
            return null;
        }

        // HTF pre-warm gate. If the spec references HTF leaves and the pre-warm tasks haven't
        // all completed yet, skip evaluation entirely — NaN reads on unwarmed leaves silently
        // flip condition results. Announce once per strategy lifetime so the user isn't left
        // wondering why a valid-looking setup doesn't fire. Backtests don't need the gate
        // because the backtester awaits pre-warm before Run begins.
        if (_hasHtfLeaves && !IsPrewarmComplete && !state.IsBacktesting)
        {
            if (!_prewarmNotified)
            {
                _prewarmNotified = true;
                if (publishEvents)
                    _eventBus.Publish(new FeedbackRequestEvent(
                        FeedbackType.Info,
                        $"Strategy '{_spec.Name}': higher-timeframe data still warming up. Setups will begin firing once cache is ready."));
            }
            return null;
        }

        var eval = _evaluator.Evaluate(_spec.Conditions, history, state);

        // Detect leaves that flipped true→false since the previous bar — these are "dropouts"
        // even if the overall tree is still true (e.g. an OR group with one false child).
        var droppedLabels = new List<string>();
        foreach (var (leafId, wasTrue) in _lastLeafResults)
        {
            bool nowTrue = eval.LeafResults.TryGetValue(leafId, out var v) && v;
            if (wasTrue && !nowTrue)
            {
                // Resolve a friendly label by walking the tree for the leaf and looking up its descriptor.
                var leaf = FindLeaf(_spec.Conditions, leafId);
                if (leaf != null)
                {
                    var desc = _catalog.GetById(leaf.SignalDescriptorId);
                    droppedLabels.Add(desc?.DisplayLabel ?? leafId);
                }
            }
        }

        // Pure-pulse trees drop off by definition every bar after they fire — publishing a
        // SetupDroppedEvent for that natural expiry produces the constant "X dropped off"
        // speech the user reported. The pulse already fired the BuildSignal path on the
        // previous bar; nothing has been "lost" in a way the user needs to hear about.
        if (droppedLabels.Count > 0 && !_isPurePulseTree && publishEvents)
        {
            _eventBus.Publish(new SetupDroppedEvent(
                StrategyName: _spec.Name,
                InstanceId:   _instanceId,
                DroppedLeafLabels: droppedLabels,
                SetupStillActive: eval.OverallTrue && _state != SetupState.Inactive,
                Symbol: state.SymbolDisplayName));
        }

        _lastLeafResults = new Dictionary<string, bool>(eval.LeafResults);

        if (!eval.OverallTrue)
        {
            // Conditions no longer satisfied — exit any active/armed setup quietly (the dropout
            // event above already announces which leaves caused it).
            _state = SetupState.Inactive;
            _barsSinceFirstConfirm = 0;
            _barsSinceArmed = 0;
            _armedPlan = null;
            return null;
        }

        // Armed: waiting for the entry trigger to fire. Check it on every bar where conditions
        // still hold; if the trigger fires, transition to Active and emit the signal. Otherwise
        // emit a heartbeat reconfirm (per user directive: ongoing audio confirmation while waiting).
        if (_state == SetupState.Armed && _armedPlan != null)
        {
            _barsSinceArmed++;
            if (EntryTriggerFired(_spec.Risk.Entry, _spec.Side, history))
            {
                _state = SetupState.Active;
                _barsSinceFirstConfirm = 0;
                double trig = ResolveTriggerPrice(_spec.Risk.Entry, history);
                if (publishEvents) _eventBus.Publish(new SetupEntryReachedEvent(
                    StrategyName: _spec.Name,
                    InstanceId:   _instanceId,
                    Side:         _spec.Side,
                    TriggerPrice: trig,
                    BarsArmed:    _barsSinceArmed,
                    Symbol:       state.SymbolDisplayName));
                return BuildSignal(_armedPlan, eval, $"Entry trigger fired at {trig:F4}.");
            }
            // Trigger not yet fired — heartbeat reconfirm so the user knows the setup is still alive.
            if (publishEvents) _eventBus.Publish(new SetupReconfirmedEvent(
                StrategyName: _spec.Name,
                InstanceId:   _instanceId,
                Side:         _spec.Side,
                BarsSinceFirstConfirm: _barsSinceArmed,
                Symbol:       state.SymbolDisplayName));
            return null;
        }

        // Active: conditions still true on a previously-fired setup — heartbeat reconfirm only.
        if (_state == SetupState.Active)
        {
            _barsSinceFirstConfirm++;
            if (publishEvents) _eventBus.Publish(new SetupReconfirmedEvent(
                StrategyName: _spec.Name,
                InstanceId:   _instanceId,
                Side:         _spec.Side,
                BarsSinceFirstConfirm: _barsSinceFirstConfirm,
                Symbol:       state.SymbolDisplayName));
            return null;
        }

        // Inactive + conditions true → first-time confirmation. Resolve the risk plan; abort
        // silently if it fails the R:R gate or references unimplemented Phase-4 stop/target sources.
        var resolved = _resolver.Resolve(_spec.Risk, _spec.Side, history, state);
        if (resolved == null) return null;

        // EntryTrigger.Immediate → transition straight to Active and emit the order.
        // Anything else → transition to Armed and wait for the trigger on subsequent bars.
        //
        // Pure-pulse trees are auto-promoted to Immediate regardless of the configured entry
        // trigger: a one-bar pulse cannot satisfy the Armed→Active path (the conditions are
        // false again by the next bar) so a non-Immediate trigger guarantees zero trades. Fire
        // on the same bar the pulse appears.
        bool effectiveImmediate = _spec.Risk.Entry.Kind == EntryTriggerKind.Immediate
                                  || _isPurePulseTree;

        // Announce once per instance when the auto-promotion kicks in for a spec that was
        // saved (pre-validation) with a non-Immediate trigger. The user needs to know their
        // configured trigger isn't what the engine is actually using.
        bool autoPromoted = _isPurePulseTree && _spec.Risk.Entry.Kind != EntryTriggerKind.Immediate;
        if (autoPromoted && !_autoPromotionNotified && publishEvents)
        {
            _autoPromotionNotified = true;
            _eventBus.Publish(new FeedbackRequestEvent(
                FeedbackType.Alert,
                $"Strategy '{_spec.Name}': entry trigger overridden from {_spec.Risk.Entry.Kind} to Immediate because every condition is a one-bar pulse. Edit the strategy to set Immediate explicitly, or add a persistent condition so the trigger can resolve."));
        }

        if (effectiveImmediate)
        {
            _state = SetupState.Active;
            _barsSinceFirstConfirm = 0;

            string normalizedScore = eval.MaxScore > 0
                ? (eval.Score / eval.MaxScore).ToString("F2")
                : "1.00";

            // Full trade plan in one utterance — entry, stop, every ladder rung — so the
            // user can execute manually without opening the dashboard. The same string
            // becomes the journal's StrategySignal entry verbatim.
            string targets = resolved.TpPrices.Count == 1
                ? $"target {resolved.TpPrices[0]:F4}"
                : string.Join(", ", resolved.TpPrices.Select(
                      (tp, k) => $"target {k + 1} {tp:F4}"));
            string rationale =
                $"{(_spec.Side == OrderSide.Buy ? "Long" : "Short")} setup, {_spec.Name}, score {normalizedScore}. " +
                $"Entry {resolved.EntryPrice:F4}, stop {resolved.StopPrice:F4}, {targets} " +
                $"(R:R {resolved.RewardRiskRatio:F2}). " +
                resolved.Notes;

            if (publishEvents) _eventBus.Publish(new SetupConfirmedEvent(
                StrategyName: _spec.Name,
                InstanceId:   _instanceId,
                Side:         _spec.Side,
                Rationale:    rationale,
                ResolvedPlan: resolved,
                Symbol:       state.SymbolDisplayName));

            return BuildSignal(resolved, eval, rationale);
        }
        else
        {
            _state = SetupState.Armed;
            _barsSinceArmed = 0;
            _armedPlan = resolved;
            if (publishEvents) _eventBus.Publish(new SetupArmedEvent(
                StrategyName: _spec.Name,
                InstanceId:   _instanceId,
                Side:         _spec.Side,
                TriggerDescription: DescribeTrigger(_spec.Risk.Entry),
                ResolvedPlan: resolved,
                Symbol:       state.SymbolDisplayName));
            return null;
        }
    }

    /// <summary>
    /// Build the StrategySignal record from a resolved plan + evaluation. Used by both the
    /// Immediate path and the Armed→Active path so the order shape is identical regardless of
    /// when in the state machine the entry actually fired. Carries the full TP ladder + close
    /// portions in the optional record fields so <c>StrategyBacktester</c> can simulate
    /// partial-close exits — the single <see cref="StrategySignal.TakeProfit"/> field stays
    /// populated with the first rung for back-compat with the live broker order path.
    /// </summary>
    private StrategySignal BuildSignal(ResolvedRiskPlan resolved, ConditionEvaluation eval, string rationale)
    {
        return new StrategySignal(
            Side:       _spec.Side,
            OrderType:  OrderType.Market,
            Quantity:   resolved.Quantity,
            LimitPrice: null,
            StopLoss:   resolved.StopPrice,
            TakeProfit: resolved.TpPrices[0],
            Rationale:  rationale,
            Confidence: eval.MaxScore > 0 ? eval.Score / eval.MaxScore : 1.0,
            TpLadder:        resolved.TpPrices,
            TpClosePortions: resolved.ClosePortions,
            StopAdjust:      _spec.Risk.StopAdjust,
            TrailAtrPeriod:  _spec.Risk.Stop.AtrPeriod,
            TrailAtrMultiple: _spec.Risk.Stop.AtrMultiple
        );
    }

    /// <summary>
    /// Returns true if the entry trigger has fired on the most recent bar. The current bar's
    /// high/low range is what we test against — once price has touched the trigger level,
    /// we consider the order placed at that price.
    /// </summary>
    private static bool EntryTriggerFired(EntryTrigger trigger, OrderSide side, IReadOnlyList<Ohlcv> history)
    {
        if (history.Count == 0) return false;
        var bar = history[^1];
        switch (trigger.Kind)
        {
            case EntryTriggerKind.Immediate:
                return true;

            case EntryTriggerKind.OnPullbackToLevel:
                // Long: price retraced down to the level. Short: price retraced up to the level.
                return side == OrderSide.Buy
                    ? bar.Low  <= trigger.LevelPrice
                    : bar.High >= trigger.LevelPrice;

            case EntryTriggerKind.OnBreakoutOf:
                // Long: price broke above the level. Short: price broke below the level.
                return side == OrderSide.Buy
                    ? bar.High >= trigger.LevelPrice
                    : bar.Low  <= trigger.LevelPrice;

            case EntryTriggerKind.OnNextNCandleClose:
                // After at least N bars have passed and the most recent N bars all closed
                // in setup direction. Conservative: requires unbroken confirmation.
                if (history.Count < trigger.NCandles) return false;
                for (int i = history.Count - trigger.NCandles; i < history.Count; i++)
                {
                    var b = history[i];
                    bool inDir = side == OrderSide.Buy ? b.Close > b.Open : b.Close < b.Open;
                    if (!inDir) return false;
                }
                return true;

            default:
                return true;
        }
    }

    private static double ResolveTriggerPrice(EntryTrigger trigger, IReadOnlyList<Ohlcv> history)
    {
        if (history.Count == 0) return 0.0;
        switch (trigger.Kind)
        {
            case EntryTriggerKind.OnPullbackToLevel:
            case EntryTriggerKind.OnBreakoutOf:
                return trigger.LevelPrice;
            default:
                return history[^1].Close;
        }
    }

    private static string DescribeTrigger(EntryTrigger trigger)
    {
        return trigger.Kind switch
        {
            EntryTriggerKind.OnPullbackToLevel  => $"Waiting for pullback to {trigger.LevelPrice:F4}.",
            EntryTriggerKind.OnBreakoutOf       => $"Waiting for breakout of {trigger.LevelPrice:F4}.",
            EntryTriggerKind.OnNextNCandleClose => $"Waiting for {trigger.NCandles} confirming candle closes.",
            _ => "Entry pending."
        };
    }

    private static ConditionLeaf? FindLeaf(ConditionNode node, string id)
    {
        switch (node)
        {
            case ConditionLeaf l when l.Id == id:
                return l;
            case ConditionGroup g:
                foreach (var c in g.Children)
                {
                    var found = FindLeaf(c, id);
                    if (found != null) return found;
                }
                return null;
            default:
                return null;
        }
    }
}
