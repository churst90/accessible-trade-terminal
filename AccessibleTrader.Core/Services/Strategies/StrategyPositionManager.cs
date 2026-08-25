using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Implements <see cref="IStrategyPositionManager"/>: the live half of the exit plan the
    /// backtester replays, plus the memory that survives a restart.
    ///
    /// <para>
    /// ── What this closes ───────────────────────────────────────────────────────
    /// Three defects that were one defect. An Auto-mode strategy placed a market order carrying
    /// a stop and the FIRST take-profit rung and nothing else, so the ladder, the move to
    /// breakeven and the ATR trail — all modelled, all part of the numbers the user accepted the
    /// strategy on — never existed live. It had no idea whether it already held a position, so a
    /// counter-signal pyramided instead of reversing (and on a spot venue a sell while flat is a
    /// naked sell the venue rejects). And a restart rebuilt every strategy flat while the broker
    /// still held the position, so the same conditions on the next bar opened a second one on
    /// top of the first, with the original order's stop the only protection either had.
    /// </para>
    ///
    /// <para>
    /// ── Where the exits run ────────────────────────────────────────────────────
    /// Entries stay a focused-chart act: <c>BackgroundWorkspaceMonitor</c> announces signals for
    /// charts that are not on screen and deliberately places nothing. Exits do NOT follow that
    /// rule, and the asymmetry is the point — declining to open a position is conservative,
    /// declining to close one is not. A locally emulated bracket that only runs while the user
    /// happens to be looking at the chart is worse than no bracket at all, because it is
    /// believed. So the monitor drives <see cref="OnBarClosed"/> for its own symbol too, at its
    /// polling cadence.
    /// </para>
    ///
    /// <para>
    /// ── What it does not promise ───────────────────────────────────────────────
    /// Levels are evaluated on bar CLOSE and exited with reduce-only market orders, so the fill
    /// is wherever the market is when the bar closes — not at the level. On the daily and 4-hour
    /// timeframes these strategies are validated at that is close; on a fast intraday chart it
    /// is not, and no amount of wording makes a bar-close emulation into a resting exchange
    /// order. Broker-native brackets stay filed as the better answer per venue. The speech says
    /// which one is running.
    /// </para>
    /// </summary>
    public sealed class StrategyPositionManager : IStrategyPositionManager, IDisposable
    {
        private readonly IEventBus _eventBus;
        private readonly IOrderExecutionService _orderService;
        private readonly IAppLogger _logger;
        private readonly IPlatformPathService _pathService;

        private readonly object _gate = new();
        private readonly Dictionary<string, ManagedStrategyPosition> _byInstance = new();
        /// <summary>Positions read from disk that no live instance has adopted yet.</summary>
        private readonly List<ManagedStrategyPosition> _orphans = new();
        /// <summary>Rollback snapshots for exits that have been booked but not yet accepted.</summary>
        private readonly Dictionary<string, ManagedStrategyPosition> _pendingExits = new();

        private readonly IDisposable? _fillSub;

        // Resolved on FIRST USE, never in the constructor. On the hosted head this service is
        // per-circuit and the user's identity is set after construction, so a path captured in
        // the constructor is users/anon — which is how one shared shortcuts.json shipped. Same
        // shape as ShortcutManager and SettingsManager; see the note there.
        private string? _filepath;
        private bool _loaded;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        public StrategyPositionManager(
            IEventBus eventBus,
            IOrderExecutionService orderService,
            IAppLogger logger,
            IPlatformPathService pathService)
        {
            _eventBus     = eventBus;
            _orderService = orderService;
            _logger       = logger;
            _pathService  = pathService;

            // The entry's real fill price. Until it arrives the breakeven anchor is the close of
            // the bar the signal was decided on, which is a reference, not a fill — and a stop
            // moved to "breakeven" at a price the user never traded at is a stop at a loss or a
            // stop already behind the market.
            _fillSub = _eventBus.Subscribe<OrderFilledEvent>(OnOrderFilled);
        }

        // ── Reads ────────────────────────────────────────────────────────────────

        public IReadOnlyList<ManagedStrategyPosition> Open
        {
            get { lock (_gate) { EnsureLoaded(); return _byInstance.Values.Select(p => p.Clone()).ToList(); } }
        }

        public ManagedStrategyPosition? Get(string instanceId)
        {
            lock (_gate) { EnsureLoaded(); return _byInstance.TryGetValue(instanceId, out var p) ? p : null; }
        }

        // ── Adoption ─────────────────────────────────────────────────────────────

        public void Adopt(string instanceId, string? specId)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(specId)) return;

            lock (_gate)
            {
                EnsureLoaded();
                if (_byInstance.ContainsKey(instanceId)) return;

                int idx = _orphans.FindIndex(p => string.Equals(p.SpecId, specId, StringComparison.Ordinal));
                if (idx < 0) return;

                var position = _orphans[idx];
                _orphans.RemoveAt(idx);
                position.InstanceId = instanceId;
                position.Verified = false;
                _byInstance[instanceId] = position;

                _logger.LogInfo(
                    $"Strategy '{position.StrategyName}' re-adopted a remembered {Word(position.Side)} position "
                    + $"of {Qty(position.RemainingQuantity)} {position.Symbol} on {position.Provider}.",
                    nameof(StrategyPositionManager));
            }
        }

        // ── Entry ────────────────────────────────────────────────────────────────

        public StrategyEntryPlan PlanEntry(ActiveStrategy active, StrategySignal signal, double quantity,
            string provider, string symbol)
        {
            lock (_gate)
            {
                EnsureLoaded();
                if (!_byInstance.TryGetValue(active.InstanceId, out var open))
                    return new StrategyEntryPlan(StrategyEntryDisposition.Open, null, null);

                if (open.Side == signal.Side)
                {
                    // Adding to a live position is not something the replay ever modelled and not
                    // something the user asked for — the size they accepted was one position's.
                    // Re-arm the protective plan from the fresh signal (its stop and ladder are
                    // this bar's levels, which are better than the entry bar's) and place nothing.
                    ReArm(open, signal);
                    Save();
                    return new StrategyEntryPlan(
                        StrategyEntryDisposition.AlreadyOpen, null,
                        $"{active.Strategy.Name} signalled {Word(signal.Side)} again while already "
                        + $"{Word(open.Side)} {Qty(open.RemainingQuantity)} {open.Symbol}. Nothing was added; "
                        + "the stop and targets were updated to the new levels.");
                }

                // Opposite side: close what is open first. The replay reverses on a counter-signal;
                // placing the new order without closing pyramids into a hedge on a futures venue
                // and is a naked sell on a spot one.
                double closing = open.RemainingQuantity;
                var order = BookExit(open, ManagedExitRules.ClosingSide(open.Side), closing, "reversed by a counter-signal");
                _byInstance.Remove(active.InstanceId);
                Save();

                return new StrategyEntryPlan(
                    StrategyEntryDisposition.Reverse, order,
                    $"{active.Strategy.Name} reversed: closing {Qty(closing)} {open.Symbol} "
                    + $"before opening {Word(signal.Side)}.");
            }
        }

        public void OpenPosition(ActiveStrategy active, StrategySignal signal, double quantity,
            string provider, string symbol, double referencePrice, string? entryOrderId)
        {
            var (prices, portions) = ManagedExitRules.BuildLadder(signal);

            var position = new ManagedStrategyPosition
            {
                InstanceId        = active.InstanceId,
                SpecId            = active.SpecId ?? "",
                StrategyName      = active.Strategy.Name,
                Provider          = provider,
                Symbol            = symbol,
                Side              = signal.Side,
                EntryPrice        = double.IsFinite(referencePrice) && referencePrice > 0 ? referencePrice : 0,
                InitialQuantity   = quantity,
                RemainingQuantity = quantity,
                StopPrice         = signal.StopLoss,
                TargetPrices      = prices.ToList(),
                TargetPortions    = portions.ToList(),
                LadderSize        = prices.Count,
                StopAdjust        = signal.StopAdjust,
                TrailAtrPeriod    = signal.TrailAtrPeriod,
                TrailAtrMultiple  = signal.TrailAtrMultiple,
                FirstTargetFilled = false,
                EntryOrderId      = string.IsNullOrWhiteSpace(entryOrderId) ? null : entryOrderId,
                OpenedUtc         = DateTime.UtcNow,
                Verified          = true,
            };

            lock (_gate)
            {
                EnsureLoaded();
                _byInstance[active.InstanceId] = position;
                // A spec that has no id cannot be found again after a restart. Say so once, at
                // the moment the risk is taken, rather than discovering it at reconciliation.
                if (position.SpecId.Length == 0)
                {
                    _logger.LogWarning(
                        $"Strategy '{position.StrategyName}' opened a managed position but has no library spec id, "
                        + "so the position cannot be re-adopted after a restart.",
                        nameof(StrategyPositionManager));
                }
                Save();
            }
        }

        /// <summary>Replaces the protective plan without touching size or entry.</summary>
        private static void ReArm(ManagedStrategyPosition position, StrategySignal signal)
        {
            var (prices, portions) = ManagedExitRules.BuildLadder(signal);
            position.StopPrice        = signal.StopLoss;
            position.TargetPrices     = prices.ToList();
            position.TargetPortions   = portions.ToList();
            position.LadderSize       = prices.Count;
            position.StopAdjust       = signal.StopAdjust;
            position.TrailAtrPeriod   = signal.TrailAtrPeriod;
            position.TrailAtrMultiple = signal.TrailAtrMultiple;
            position.FirstTargetFilled = false;
        }

        // ── The bar walk ─────────────────────────────────────────────────────────

        public IReadOnlyList<StrategyExitOrder> OnBarClosed(string instanceId, Ohlcv bar,
            IReadOnlyList<Ohlcv> history)
        {
            lock (_gate)
            {
                EnsureLoaded();
                if (!_byInstance.TryGetValue(instanceId, out var p)) return Array.Empty<StrategyExitOrder>();
                if (p.RemainingQuantity <= ManagedExitRules.QuantityEpsilon)
                {
                    _byInstance.Remove(instanceId);
                    Save();
                    return Array.Empty<StrategyExitOrder>();
                }

                var orders = new List<StrategyExitOrder>();
                bool dirty = false;

                // ── Stop, first. If both a stop and a target could have been reached inside one
                // bar we take the stop: the replay makes the same conservative assumption, and a
                // live emulation that guessed the other way would report exits the user never got.
                if (p.StopPrice is { } stop && ManagedExitRules.StopHit(p.Side, stop, bar))
                {
                    orders.Add(BookExit(p, ManagedExitRules.ClosingSide(p.Side), p.RemainingQuantity,
                        p.FirstTargetFilled ? "the trailing stop" : "the stop"));
                    _byInstance.Remove(instanceId);
                    Save();
                    return orders;
                }

                // ── Ladder rungs. A fast bar can clear several; they fire in order, each closing
                // its portion of the INITIAL size.
                // Against the ladder's ORIGINAL size, not what is left of it.
                int totalRungs = p.LadderSize > 0 ? p.LadderSize : p.TargetPrices.Count;
                while (p.TargetPrices.Count > 0)
                {
                    double target = p.TargetPrices[0];
                    if (!ManagedExitRules.TargetHit(p.Side, target, bar)) break;

                    double portion = p.TargetPortions.Count > 0 ? p.TargetPortions[0] : 1.0;
                    p.TargetPrices.RemoveAt(0);
                    if (p.TargetPortions.Count > 0) p.TargetPortions.RemoveAt(0);

                    double closeQty = ManagedExitRules.CloseQuantity(p.RemainingQuantity, p.InitialQuantity, portion);
                    if (closeQty <= ManagedExitRules.QuantityEpsilon) break;

                    int fired = Math.Max(1, totalRungs - p.TargetPrices.Count);
                    orders.Add(BookExit(p, ManagedExitRules.ClosingSide(p.Side), closeQty,
                        totalRungs > 1 ? $"target {fired} of {totalRungs}" : "the target"));
                    p.RemainingQuantity -= closeQty;
                    dirty = true;

                    if (!p.FirstTargetFilled)
                    {
                        p.StopPrice = ManagedExitRules.StopAfterFirstTarget(p.StopAdjust, p.EntryPrice, p.StopPrice);
                        p.FirstTargetFilled = true;
                    }

                    if (p.RemainingQuantity <= ManagedExitRules.QuantityEpsilon)
                    {
                        _byInstance.Remove(instanceId);
                        Save();
                        return orders;
                    }
                }

                // ── ATR trail. Only after the first rung has cleared, exactly as in the replay:
                // before that the stop is the one the strategy chose and the trail must not
                // second-guess it.
                if (p.FirstTargetFilled && p.StopAdjust == StopAdjustOnTp1.TrailByAtr
                    && p.StopPrice is { } current && history != null && history.Count > 0)
                {
                    double moved = ManagedExitRules.AtrTrailStop(
                        history, history.Count - 1, p.TrailAtrPeriod, p.TrailAtrMultiple, p.Side, current);
                    if (moved != current)
                    {
                        p.StopPrice = moved;
                        dirty = true;
                    }
                }

                if (dirty) Save();
                return orders;
            }
        }

        /// <summary>Mints an exit order and snapshots the position so a refusal can be undone.</summary>
        private StrategyExitOrder BookExit(ManagedStrategyPosition p, OrderSide side, double quantity, string reason)
        {
            string exitId = Guid.NewGuid().ToString("N");
            _pendingExits[exitId] = p.Clone();
            return new StrategyExitOrder(exitId, p.InstanceId, p.StrategyName, p.Provider, p.Symbol,
                side, quantity, reason);
        }

        public async Task<bool> PlaceExitsAsync(IReadOnlyList<StrategyExitOrder> orders)
        {
            if (orders == null || orders.Count == 0) return true;

            bool all = true;
            // Sequential, deliberately. Rung two closing before rung one is not merely untidy: the
            // portions are fractions of the initial size, so an out-of-order pair can oversell the
            // remainder and open the opposite position on a venue that allows it.
            foreach (var order in orders)
            {
                bool ok = await PlaceExitAsync(order).ConfigureAwait(false);
                all &= ok;
                if (!ok) break;   // the rest of the ladder is now against stale bookkeeping
            }
            return all;
        }

        private async Task<bool> PlaceExitAsync(StrategyExitOrder order)
        {
            try
            {
                var trade = new TradeSignal(
                    Symbol:     order.Symbol,
                    Side:       order.Side,
                    Quantity:   order.Quantity,
                    Type:       OrderType.Market,
                    // Reduce-only unconditionally, exactly as the Close-position path is. A
                    // managed exit that is allowed to open a position is the one thing it must
                    // never do — an oversized rung would flip a long into a short.
                    ReduceOnly: true);

                string result = await _orderService.PlaceOrderAsync(order.Provider, trade).ConfigureAwait(false);
                string? failure = OrderResult.DescribeFailure(result);

                if (failure == null)
                {
                    ExitAccepted(order.ExitId);
                    Announce(FeedbackType.Info,
                        $"{order.StrategyName} closed {Qty(order.Quantity)} {order.Symbol} on {order.Reason}.");
                    return true;
                }

                ExitRejected(order.ExitId);
                Announce(FeedbackType.Error,
                    $"{order.StrategyName} could not close {Qty(order.Quantity)} {order.Symbol} on {order.Reason}. "
                    + failure + " The position is still open and the level stays armed.");
                return false;
            }
            catch (Exception ex)
            {
                ExitRejected(order.ExitId);
                _logger.LogError($"Managed exit for '{order.StrategyName}' threw: {ex.Message}",
                    nameof(StrategyPositionManager), ex);
                Announce(FeedbackType.Error,
                    $"{order.StrategyName} could not close {Qty(order.Quantity)} {order.Symbol} on {order.Reason}. "
                    + "The position is still open and the level stays armed.");
                return false;
            }
        }

        public void ExitAccepted(string exitId)
        {
            lock (_gate) { _pendingExits.Remove(exitId); }
        }

        public void ExitRejected(string exitId)
        {
            lock (_gate)
            {
                if (!_pendingExits.TryGetValue(exitId, out var snapshot)) return;
                _pendingExits.Remove(exitId);
                // Straight back to the state before the exit was booked — including the level that
                // fired, so the next bar still beyond it tries again. A level that is silently
                // consumed by a refused order is a stop that has been cancelled without anyone
                // being told.
                _byInstance[snapshot.InstanceId] = snapshot;
                Save();
            }
        }

        public void Forget(string instanceId)
        {
            ManagedStrategyPosition? dropped;
            lock (_gate)
            {
                EnsureLoaded();
                if (!_byInstance.TryGetValue(instanceId, out dropped)) return;
                _byInstance.Remove(instanceId);
                Save();
            }

            // Removing the strategy is the user's decision and it is honoured — but a position
            // that was being managed and now is not has to be said out loud. Silence here means
            // a stop the user believes is running has stopped running.
            if (dropped.RemainingQuantity > ManagedExitRules.QuantityEpsilon)
            {
                Announce(FeedbackType.Alert,
                    $"{dropped.StrategyName} was removed while holding {Qty(dropped.RemainingQuantity)} "
                    + $"{dropped.Symbol} {Word(dropped.Side)} on {dropped.Provider}. Its stop and targets are no "
                    + "longer being managed — the position is open and yours to close.");
            }
        }

        // ── Fill correction ──────────────────────────────────────────────────────

        private void OnOrderFilled(OrderFilledEvent e)
        {
            var update = e.Order;
            if (update == null || !double.IsFinite(update.FilledPrice) || update.FilledPrice <= 0) return;

            lock (_gate)
            {
                foreach (var p in _byInstance.Values)
                {
                    if (p.EntryOrderId == null || !string.Equals(p.EntryOrderId, update.OrderId, StringComparison.Ordinal))
                        continue;
                    if (Math.Abs(p.EntryPrice - update.FilledPrice) < double.Epsilon) return;

                    p.EntryPrice = update.FilledPrice;
                    // A breakeven stop already placed from the reference price is now wrong in the
                    // direction of "not actually breakeven", so move it with the anchor.
                    if (p.FirstTargetFilled && p.StopAdjust == StopAdjustOnTp1.MoveToBreakeven)
                        p.StopPrice = update.FilledPrice;
                    Save();
                    return;
                }
            }
        }

        // ── Restart reconciliation ───────────────────────────────────────────────

        public async Task ReconcileAsync()
        {
            List<ManagedStrategyPosition> toCheck;
            lock (_gate)
            {
                EnsureLoaded();
                toCheck = _byInstance.Values.Concat(_orphans).Select(p => p.Clone()).ToList();
            }
            if (toCheck.Count == 0) return;

            // One read per provider, not one per position.
            var byProvider = toCheck.GroupBy(p => p.Provider, StringComparer.OrdinalIgnoreCase);

            foreach (var group in byProvider)
            {
                ProviderResult<List<Position>> result;
                try
                {
                    result = await _orderService.GetPositionsAsync(group.Key).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result = ProviderResult<List<Position>>.Failed(ex.Message);
                }

                foreach (var remembered in group)
                {
                    if (!result.IsOk || result.Value == null)
                    {
                        // A spot venue has no positions concept and a failed read has no answer;
                        // neither is evidence the position is gone. Keep it — the managed exits are
                        // the only protection it has — and say that it is unconfirmed.
                        MarkUnverified(remembered);
                        Announce(FeedbackType.Alert,
                            $"{remembered.StrategyName} is still managing a {Word(remembered.Side)} position of "
                            + $"{Qty(remembered.RemainingQuantity)} {remembered.Symbol} from before the restart, but "
                            + $"{remembered.Provider} could not confirm it"
                            + (string.IsNullOrWhiteSpace(result.Reason) ? "" : $" — {result.Reason}")
                            + ". Check your positions.");
                        continue;
                    }

                    var live = result.Value.FirstOrDefault(
                        pos => SymbolsMatch(pos.Symbol, remembered.Symbol) && Math.Abs(pos.Quantity) > 0);

                    if (live == null)
                    {
                        // Flat at the broker. Whatever closed it — a stop that filled while the app
                        // was down, a manual exit — the strategy is not managing anything.
                        Drop(remembered);
                        Announce(FeedbackType.Info,
                            $"{remembered.StrategyName}'s {Word(remembered.Side)} position in {remembered.Symbol} "
                            + $"is no longer open at {remembered.Provider}. The strategy is flat and free to signal again.");
                        continue;
                    }

                    var liveSide = live.Quantity >= 0 ? OrderSide.Buy : OrderSide.Sell;
                    if (liveSide != remembered.Side)
                    {
                        // The broker holds the opposite of what we remember. We do not know how that
                        // happened and we will not place reduce-only orders against a position we
                        // cannot explain — hand it to the user rather than guess.
                        Drop(remembered);
                        Announce(FeedbackType.Error,
                            $"{remembered.StrategyName} expected a {Word(remembered.Side)} position in "
                            + $"{remembered.Symbol} but {remembered.Provider} holds a {Word(liveSide)} one. "
                            + "The strategy has stopped managing it — the position is yours to handle.");
                        continue;
                    }

                    double brokerQty = Math.Abs(live.Quantity);
                    bool resized = Math.Abs(brokerQty - remembered.RemainingQuantity) > ManagedExitRules.QuantityEpsilon;
                    Confirm(remembered, brokerQty);

                    // ── Is anybody actually running this? ────────────────────────
                    // A remembered position whose spec was never re-registered — deleted from the
                    // library, or its auto-activate flag turned off while it was open — has no
                    // engine instance behind it, so no bar-close walk ever reaches it. Saying
                    // "resumed managing" there would be the exact lie this whole change exists to
                    // stop telling: a stop the user believes is running and is not.
                    bool adopted = remembered.InstanceId.Length > 0;
                    if (!adopted)
                    {
                        Announce(FeedbackType.Alert,
                            $"{remembered.Provider} still holds a {Word(remembered.Side)} position of "
                            + $"{Qty(brokerQty)} {remembered.Symbol} that '{remembered.StrategyName}' opened, but "
                            + "that strategy is not running, so its stop and targets are NOT being managed. "
                            + "Re-activate the strategy or close the position yourself.");
                        continue;
                    }

                    Announce(FeedbackType.Info,
                        $"{remembered.StrategyName} resumed managing its {Word(remembered.Side)} position in "
                        + $"{remembered.Symbol}: {Qty(brokerQty)} at {remembered.Provider}"
                        + (resized ? ", resized to what the broker actually holds" : "")
                        + (remembered.TargetPrices.Count > 0
                            ? $", {remembered.TargetPrices.Count} target(s) still to run"
                            : "")
                        + (remembered.StopPrice is { } s ? $", stop {s.ToString("0.####", CultureInfo.InvariantCulture)}" : ", no stop")
                        + ".");
                }
            }
        }

        private void MarkUnverified(ManagedStrategyPosition remembered)
        {
            lock (_gate)
            {
                var live = Find(remembered);
                if (live != null) live.Verified = false;
                Save();
            }
        }

        private void Confirm(ManagedStrategyPosition remembered, double brokerQuantity)
        {
            lock (_gate)
            {
                var live = Find(remembered);
                if (live == null) return;
                live.Verified = true;
                // The broker is the authority on size. Ours can be stale by exactly one exit: an
                // order that went out as the process died.
                live.RemainingQuantity = brokerQuantity;
                if (live.InitialQuantity < brokerQuantity) live.InitialQuantity = brokerQuantity;
                Save();
            }
        }

        private void Drop(ManagedStrategyPosition remembered)
        {
            lock (_gate)
            {
                var live = Find(remembered);
                if (live == null) return;
                if (live.InstanceId.Length > 0) _byInstance.Remove(live.InstanceId);
                _orphans.Remove(live);
                Save();
            }
        }

        /// <summary>Locates the live record a reconciliation snapshot came from.</summary>
        private ManagedStrategyPosition? Find(ManagedStrategyPosition snapshot)
        {
            if (snapshot.InstanceId.Length > 0 && _byInstance.TryGetValue(snapshot.InstanceId, out var byId))
                return byId;
            return _orphans.FirstOrDefault(p => string.Equals(p.SpecId, snapshot.SpecId, StringComparison.Ordinal)
                                             && SymbolsMatch(p.Symbol, snapshot.Symbol));
        }

        /// <summary>
        /// Whether two spellings name the same instrument. Venues disagree about separators and
        /// case for the same pair — BTC-USD, BTC/USD, btcusd — and a reconciliation that missed
        /// on punctuation would report every position as closed and free every strategy to
        /// re-enter on top of one it still holds.
        /// </summary>
        internal static bool SymbolsMatch(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

            static string Normalize(string s)
            {
                Span<char> buffer = s.Length <= 64 ? stackalloc char[s.Length] : new char[s.Length];
                int n = 0;
                foreach (char c in s)
                    if (char.IsLetterOrDigit(c)) buffer[n++] = char.ToUpperInvariant(c);
                return new string(buffer[..n]);
            }
        }

        // ── Speech ───────────────────────────────────────────────────────────────

        private void Announce(FeedbackType type, string message)
        {
            _logger.LogInfo(message, nameof(StrategyPositionManager));
            _eventBus.Publish(new FeedbackRequestEvent(type, message, true));
        }

        private static string Word(OrderSide side) => side == OrderSide.Buy ? "long" : "short";

        private static string Qty(double q) => q.ToString("0.########", CultureInfo.InvariantCulture);

        // ── Persistence ──────────────────────────────────────────────────────────

        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                _filepath = Path.Combine(_pathService.AppDataDirectory, "strategy-positions.json");
                if (!File.Exists(_filepath)) return;

                string json = File.ReadAllText(_filepath);
                if (string.IsNullOrWhiteSpace(json)) return;

                var stored = JsonSerializer.Deserialize<List<ManagedStrategyPosition>>(json, JsonOptions);
                if (stored == null) return;

                foreach (var p in stored)
                {
                    if (p.RemainingQuantity <= ManagedExitRules.QuantityEpsilon) continue;
                    // Instance ids do not survive the process. Everything read from disk starts as
                    // an orphan and is claimed by Adopt() when its spec is registered again.
                    p.InstanceId = "";
                    p.Verified = false;
                    _orphans.Add(p);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Could not read remembered strategy positions: {ex.Message}",
                    nameof(StrategyPositionManager), ex);
                // The file describes real money at a real venue. Move it aside intact rather than
                // let the next Save() overwrite it with an empty list.
                if (_filepath != null) CorruptFileQuarantine.MoveAside(_filepath, ex);
                _orphans.Clear();
            }
        }

        /// <summary>Caller holds <see cref="_gate"/>.</summary>
        private void Save()
        {
            if (_filepath == null) return;
            try
            {
                var all = _byInstance.Values.Concat(_orphans).ToList();
                AtomicFile.WriteAllText(_filepath, JsonSerializer.Serialize(all, JsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Could not persist strategy positions: {ex.Message}",
                    nameof(StrategyPositionManager), ex);
            }
        }

        public void Dispose() => _fillSub?.Dispose();
    }
}
