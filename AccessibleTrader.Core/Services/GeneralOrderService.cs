using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Core.Services.Accessibility;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services
{
    public class GeneralOrderService : IOrderExecutionService, IDisposable
    {
        private readonly IDataService _dataService;
        private readonly IGlobalErrorCoordinator _errorCoordinator;
        private readonly ILogger<GeneralOrderService> _logger;
        private readonly IEventBus _eventBus;
        private readonly IPaperTradingProvider _paper;
        private readonly ISettingsManager _settings;
        private readonly DemoPolicy _demo;
        // THIS account's equity cache — an instance, never a static, so on the
        // multi-user WebHost one user's balance can never become another's
        // quick-trade sizing input.
        private readonly Trading.QuickTradeEquity _equity;

        // One live-broker order-stream subscription per provider, held for the
        // service lifetime. Keyed per provider (not single-slot) because several
        // brokers can be connected at once (multi-workspace) and a fill on any of
        // them must announce. Populated reactively from ConnectionStatusEvent.
        private readonly Dictionary<string, IDisposable> _liveStreamSubs = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _liveStreamLock = new();
        private readonly IDisposable _connectionSub;

        // The paper broker's order stream is subscribed for the whole lifetime: it
        // only emits when orders are actually routed to it (paper mode on), so this
        // guarantees paper fills/stops always announce without depending on the
        // active-provider re-subscription dance when paper mode is toggled.
        private readonly IDisposable _paperStreamSub;

        // ── Sanity clamps + idempotency dedup ────────────────────────────────
        // Hard upper bound for any single-order quantity. Real users will never
        // legitimately submit 1e7 of any asset; a Roslyn strategy bug emitting
        // 1e308 would otherwise tie up liquidity or trigger circuit breakers.
        // Per-asset precision is enforced by the exchange — we just block
        // obviously-broken payloads at the top of the call.
        private const double MaxOrderQuantity = 10_000_000.0;

        // In-memory dedup window: any PlaceOrderAsync attempt with the same
        // (provider, order identity) within DedupWindow is rejected as a probable
        // double-submit. Covers UI double-Enter and post-network-flap retries.
        // Provider-side ClientOid enforcement (Binance) catches the rest.
        //
        // ── Why the key is NOT simply the ClientOid ──────────────────────────────
        // It used to be, and that made this gate dead code. No production caller
        // sets ClientOid — not the dashboard ticket, not Close position, not
        // QuickTradeExecutor, not StrategyEngine — so step 2 below minted a fresh
        // GUID for every submit and the map could never match itself. The comment
        // here claimed it "covers UI double-click"; it covered nothing, and two
        // tests in OrderSafetyTests pinned that as correct behaviour.
        //
        // So: when the caller DID supply a ClientOid, honour it — an explicit id is
        // an explicit statement of identity, and two different ids mean two orders
        // the caller genuinely wants. When it did not, key on what the order IS
        // (provider, symbol, side, type, quantity, price, trigger, reduce-only).
        // Two submits of the same thing inside 30 seconds are a double-fire.
        private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(30);
        private readonly Dictionary<string, DateTime> _recentOrders = new();
        private readonly object _recentOrdersLock = new();

        public GeneralOrderService(
            IDataService dataService,
            IGlobalErrorCoordinator errorCoordinator,
            ILogger<GeneralOrderService> logger,
            IEventBus eventBus,
            IPaperTradingProvider paper,
            ISettingsManager settings,
            DemoPolicy demo,
            Trading.QuickTradeEquity equity)
        {
            _dataService = dataService;
            _errorCoordinator = errorCoordinator;
            _logger = logger;
            _eventBus = eventBus;
            _paper = paper;
            _settings = settings;
            _demo = demo;
            _equity = equity;
            _paperStreamSub = paper.OrderUpdateStream.Subscribe(PublishOrderEvent);

            // Self-wire the live-broker streams: whenever a provider connects, hook
            // its order stream so fills/stops/TPs announce. No head has to remember
            // to call anything — before this subscription existed, live fills were
            // silently never announced (only the paper stream was subscribed).
            _connectionSub = eventBus.Subscribe<ConnectionStatusEvent>(e =>
            {
                if (e.State == ConnectionState.Connected)
                    SafeFireAndForget.Run(
                        () => SubscribeOrderUpdatesAsync(e.Provider),
                        _logger, "SubscribeOrderUpdates");
            });
        }

        /// <summary>
        /// Subscribes to the live OrderUpdateStream of <paramref name="providerName"/> and
        /// publishes typed trading events to the EventBus based on each update's status flags.
        /// Idempotent per provider; called automatically on ConnectionStatusEvent(Connected).
        /// Deliberately IGNORES paper mode: the paper stream has its own lifetime
        /// subscription (constructor), and real-money orders already resting on an
        /// exchange must keep announcing even while the user practices in paper mode.
        /// Subscribing the paper broker here would double-announce every paper fill.
        /// </summary>
        public async Task SubscribeOrderUpdatesAsync(string providerName)
        {
            lock (_liveStreamLock)
            {
                if (_liveStreamSubs.ContainsKey(providerName)) return;
            }

            // Resolve the NAMED provider directly — not GetTradingProviderAsync,
            // which reroutes to the paper broker when paper mode is on.
            var provider = await _dataService.GetProviderAsync(providerName).ConfigureAwait(false);
            if (provider is not ITradingProvider tp) return;

            lock (_liveStreamLock)
            {
                // Re-check under the lock: a reconnect blip can race two events.
                if (_liveStreamSubs.ContainsKey(providerName)) return;
                _liveStreamSubs[providerName] = tp.OrderUpdateStream.Subscribe(PublishOrderEvent);
            }
            _logger.LogInformation("Subscribed to live order updates from {Provider}", providerName);
        }

        private void PublishOrderEvent(OrderUpdate update)
        {
            // The stop/take-profit flags say which LEG this update belongs to, not
            // that it executed. A protective leg can also be rejected or cancelled,
            // and announcing "stop hit" for a stop that was refused reports the
            // opposite of what happened — so only an actual execution takes these
            // branches.
            bool executed = update.Status is OrderStatus.Filled or OrderStatus.PartialFill;
            if (executed && update.StopTriggered)
            {
                _eventBus.Publish(new StopHitEvent(update));
                return;
            }
            if (executed && update.TakeProfitTriggered)
            {
                _eventBus.Publish(new TakeProfitHitEvent(update));
                return;
            }

            switch (update.Status)
            {
                case OrderStatus.Filled:
                    _eventBus.Publish(new OrderFilledEvent(update));
                    break;
                case OrderStatus.PartialFill:
                    _eventBus.Publish(new OrderPartialFillEvent(update));
                    break;
                case OrderStatus.Rejected:
                    // The broker's own words when it gave any. The old fallback text
                    // named the order id — a guid, spoken aloud, telling the user
                    // nothing they could act on — so absent a reason we say none
                    // rather than reciting one.
                    _logger.LogInformation("Order {OrderId} rejected: {Reason}", update.OrderId, update.Reason ?? "(no reason given)");
                    _eventBus.Publish(new OrderRejectedEvent(update, update.Reason ?? ""));
                    break;
                case OrderStatus.Cancelled:
                    _logger.LogInformation("Order {OrderId} cancelled", update.OrderId);
                    _eventBus.Publish(new OrderCancelledEvent(update));
                    break;
                case OrderStatus.Expired:
                    _logger.LogInformation("Order {OrderId} expired: {Reason}", update.OrderId, update.Reason ?? "(no reason given)");
                    _eventBus.Publish(new OrderExpiredEvent(update));
                    break;
                case OrderStatus.Replaced:
                    _logger.LogInformation("Order {OrderId} replaced — still live under a new id", update.OrderId);
                    _eventBus.Publish(new OrderReplacedEvent(update));
                    break;
                case OrderStatus.New:
                    // Accepted and working. Logged, not announced: placement was
                    // already announced when PlaceOrderAsync returned.
                    _logger.LogInformation("Order {OrderId} accepted by the venue and working", update.OrderId);
                    break;
                case OrderStatus.Unknown:
                default:
                    // Exhaustive on purpose. The old enum had a Triggered member
                    // with no case here — four providers used it as their fallback
                    // arm, so every unrecognised venue status vanished silently and
                    // the trader could not distinguish "still working" from
                    // "refused". An unrecognised status is at minimum a log line.
                    _logger.LogWarning(
                        "Order {OrderId} on {Symbol} reported an unrecognised status ({Reason}) — treating as still working; check your open orders.",
                        update.OrderId, update.Symbol, update.Reason ?? "no detail from provider");
                    break;
            }
        }

        public void Dispose()
        {
            _disposeCts.Cancel();
            _disposeCts.Dispose();
            _connectionSub?.Dispose();
            lock (_liveStreamLock)
            {
                foreach (var sub in _liveStreamSubs.Values) sub.Dispose();
                _liveStreamSubs.Clear();
            }
            _paperStreamSub?.Dispose();
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        // Hosted/demo web builds may never route real money: DemoPolicy.AllowLiveTrading
        // is false for HostMode.Hosted (--accounts) and HostMode.Demo, forcing every
        // order to the paper broker regardless of the user setting — the user cannot turn
        // this off. On the desktop (HostMode.Full) it falls through to the opt-in setting.
        // Mirrors the gating already done in TradingDashboardModal/StatusBar so the engine
        // agrees with the UI about what "paper mode" means.
        private bool IsPaperMode =>
            !_demo.AllowLiveTrading
            || (_settings.GetSetting(SettingsKeys.PaperTradingMode)?.ToObject<bool>() ?? false);

        private async Task<ITradingProvider?> GetTradingProviderAsync(string providerName)
        {
            // Paper mode routes ALL trading to the simulated broker regardless of
            // which data provider is loaded — so you can practice on any chart,
            // even a data-only one.
            if (IsPaperMode) return _paper;
            var provider = await _dataService.GetProviderAsync(providerName).ConfigureAwait(false);
            return provider as ITradingProvider;
        }

        // ── Order execution ────────────────────────────────────────────────────

        public async Task<string> PlaceOrderAsync(string providerName, TradeSignal providedSignal)
        {
            // ── 1. Sanity clamps. Real-money path: a Roslyn-strategy bug that
            //    emits Quantity = 1e308 must not reach the exchange. NaN/Infinity
            //    on Limit-order Price is the same shape of bug — block it here.
            if (!IsFinitePositive(providedSignal.Quantity) || providedSignal.Quantity > MaxOrderQuantity)
            {
                _errorCoordinator.ReportError(
                    $"Order rejected: quantity {providedSignal.Quantity} is outside the allowed range (0, {MaxOrderQuantity}].",
                    ErrorSeverity.High);
                _logger.LogWarning(
                    "Order rejected at submission: out-of-range quantity {Quantity} on {Symbol}/{Provider}",
                    providedSignal.Quantity, providedSignal.Symbol, providerName);
                return "ORDER_REJECTED_QUANTITY";
            }
            if (providedSignal.Type == OrderType.Limit
                || providedSignal.Type == OrderType.StopLimit
                || providedSignal.Type == OrderType.TakeProfitLimit)
            {
                if (providedSignal.Price is not double p || !IsFinitePositive(p))
                {
                    _errorCoordinator.ReportError(
                        $"Order rejected: limit-style order requires a finite positive Price (got {providedSignal.Price?.ToString() ?? "null"}).",
                        ErrorSeverity.High);
                    _logger.LogWarning(
                        "Order rejected at submission: invalid limit Price {Price} on {Symbol}/{Provider}",
                        providedSignal.Price, providedSignal.Symbol, providerName);
                    return "ORDER_REJECTED_PRICE";
                }
            }

            // ── 2. Idempotency: ensure ClientOid is set so dedup + provider-side
            //    enforcement both work. Generate from a deterministic-ish source
            //    (random GUID — UI strategies don't need server-side reproducibility,
            //    they just need *some* unique tag for the dedup gate to operate on).
            var signal = providedSignal.ClientOid is null
                ? providedSignal with { ClientOid = "atc-" + Guid.NewGuid().ToString("N").Substring(0, 16) }
                : providedSignal;

            // ── 3. Dedup gate. A second Submit of the same order inside the dedup
            //    window is treated as a probable double-fire (screen-reader
            //    double-Enter, post-network-flap retry) and refused. The first
            //    submission's outcome is whatever it was — we don't second-guess it.
            string dedupKey = DedupKeyFor(providerName, providedSignal, signal);
            DateTime now = DateTime.UtcNow;
            lock (_recentOrdersLock)
            {
                // Sweep expired entries on every insert. O(n) but n is tiny in
                // practice (orders/min is bounded) and keeps the map from growing.
                if (_recentOrders.Count > 64)
                {
                    var stale = _recentOrders
                        .Where(kv => now - kv.Value > DedupWindow)
                        .Select(kv => kv.Key)
                        .ToList();
                    foreach (var k in stale) _recentOrders.Remove(k);
                }
                if (_recentOrders.TryGetValue(dedupKey, out var ts) && now - ts < DedupWindow)
                {
                    _logger.LogWarning(
                        "Duplicate order suppressed: {DedupKey} already submitted {SecondsAgo:F1}s ago (clientOid={ClientOid})",
                        dedupKey, (now - ts).TotalSeconds, signal.ClientOid);
                    // Name the order, not the mechanism. "Same client id" meant nothing
                    // to a user who never chose one; "this exact order" is the fact they
                    // need in order to decide whether to wait or to place it again.
                    _errorCoordinator.ReportError(
                        $"Duplicate order suppressed: {signal.Side} {signal.Quantity} {signal.Symbol} "
                        + $"was already submitted {(now - ts).TotalSeconds:F0} seconds ago. "
                        + "Check your open orders before placing it again.",
                        ErrorSeverity.Medium);
                    return "ORDER_DUPLICATE_SUPPRESSED";
                }
                _recentOrders[dedupKey] = now;
            }

            _logger.LogInformation(
                "Placing {Side} order for {Symbol} via {Provider} (qty={Quantity}, type={Type}, clientOid={ClientOid})",
                signal.Side, signal.Symbol, providerName, signal.Quantity, signal.Type, signal.ClientOid);

            var tp = await GetTradingProviderAsync(providerName).ConfigureAwait(false);
            if (tp == null)
            {
                _errorCoordinator.ReportError($"Provider {providerName} does not support trading.", ErrorSeverity.Medium);
                // Code:reason — the caller announces everything after the colon, so a refusal
                // arrives as a sentence rather than as a bare code with nothing to act on.
                return $"PROVIDER_NOT_SUPPORTED:{providerName} does not support trading";
            }
            if (!tp.IsConnected)
            {
                _errorCoordinator.ReportError($"Cannot place order. {providerName} is not connected.", ErrorSeverity.High);
                return $"PROVIDER_NOT_CONNECTED:{providerName} is not connected";
            }
            try
            {
                var result = await tp.PlaceOrderAsync(signal).ConfigureAwait(false);
                _errorCoordinator.ReportSuccess($"Order placed: {signal.Side} {signal.Quantity} {signal.Symbol}");

                // ── 4. Protective-order verification net (provider-agnostic). If the
                //    signal asked for a stop loss or take profit, confirm something
                //    protective actually exists on the exchange. Providers attach
                //    TP/SL as separate orders after the entry; that attach can fail
                //    (or be silently unsupported for the order type) leaving a naked
                //    position. Runs in the background so order placement isn't
                //    delayed; speaks up only when NOTHING protective can be found.
                if ((signal.StopLoss.HasValue || signal.TakeProfit.HasValue) && !IsErrorSentinel(result))
                {
                    // Brokers with a single protective slot (Kraken) attach the STOP
                    // when both were requested — say so instead of letting the user
                    // believe a take profit is resting.
                    if (signal.StopLoss.HasValue && signal.TakeProfit.HasValue
                        && !tp.SupportsSimultaneousStopAndTarget)
                    {
                        _errorCoordinator.ReportError(
                            $"{providerName} attaches only one protective order: the STOP LOSS was attached; the take profit was NOT. Place it separately if you want both.",
                            ErrorSeverity.Medium);
                    }

                    var capturedSignal = signal;
                    SafeFireAndForget.Run(
                        () => VerifyProtectiveOrdersAsync(tp, providerName, capturedSignal),
                        _logger, "VerifyProtectiveOrders");
                }

                // ── 5. Order-status polling fallback. Brokers whose OrderUpdateStream
                //    is a dead subject (Schwab, Tradier — no streaming implementation)
                //    would otherwise NEVER announce this order's outcome. Poll open
                //    orders until the order resolves and synthesize the OrderUpdate
                //    the stream would have pushed.
                if (!IsErrorSentinel(result) && !tp.SupportsOrderEventStreaming)
                {
                    var capturedSignal = signal;
                    string orderId = result;
                    OrderWatchesStarted++;
                    SafeFireAndForget.Run(
                        () => PollOrderUntilResolvedAsync(tp, providerName, capturedSignal, orderId),
                        _logger, "PollOrderStatus");
                }
                else if (result == "ORDER_SUBMITTED" && !tp.SupportsOrderEventStreaming)
                {
                    // "ORDER_SUBMITTED" is a provider's venue-accepted-but-no-id
                    // fallback. It prefix-matches the error sentinel, so the poll
                    // above never starts — without an id there is nothing to poll.
                    // The order is live and its fill will be silent: say so, instead
                    // of letting "placed" imply the outcome will be announced.
                    _errorCoordinator.ReportError(
                        $"{providerName} accepted the order but did not return an order id, so its fill "
                        + "cannot be announced. Check your open orders to follow it.",
                        ErrorSeverity.Medium);
                }
                return result;
            }
            catch (Exception ex)
            {
                // Recovery hint: an exception during PlaceOrderAsync does NOT confirm
                // the order failed to post — a network drop after the exchange
                // accepted the payload looks identical to a connection refused. We
                // can't query by ClientOid (no SDK surface for it) but we CAN scan
                // the open-orders list for a matching qty/symbol/side recently and
                // warn the user to verify before retrying. The dedup gate above
                // already prevents the next 30s of accidental resubmits.
                _logger.LogError(ex,
                    "PlaceOrder threw for {Symbol} via {Provider} (clientOid={ClientOid}). Order may or may not have posted — user should verify before retrying.",
                    signal.Symbol, providerName, signal.ClientOid);
                try
                {
                    var open = await tp.GetOpenOrdersAsync(signal.Symbol).ConfigureAwait(false);
                    var maybe = open.FirstOrDefault(o =>
                        o.Symbol == signal.Symbol
                        && o.Side == signal.Side
                        && Math.Abs(o.Quantity - signal.Quantity) < signal.Quantity * 1e-6);
                    if (maybe != null)
                    {
                        _errorCoordinator.ReportError(
                            $"Order failed mid-submit but a matching open order ({maybe.Id}) is on the exchange. VERIFY before retrying.",
                            ErrorSeverity.High);
                        return $"ORDER_UNCERTAIN:{maybe.Id}";
                    }
                }
                catch (Exception scanEx)
                {
                    _logger.LogWarning(scanEx,
                        "Recovery scan after PlaceOrder failure also failed for {Symbol}/{Provider}.",
                        signal.Symbol, providerName);
                }
                _errorCoordinator.ReportError($"Order failed: {ex.Message}", ErrorSeverity.High);
                return $"ORDER_FAILED:{ex.Message}";
            }
        }

        /// <summary>
        /// The key the dedup window is measured against.
        ///
        /// <para>
        /// A caller-supplied <c>ClientOid</c> wins: it is an explicit statement of "this is
        /// order X", and two distinct ids mean two orders that were genuinely both wanted.
        /// </para>
        ///
        /// <para>
        /// With no id — which is every production caller today — the key is the order's own
        /// identity. The fields chosen are the ones that make two submissions the SAME order
        /// to a user: provider, symbol, side, type, quantity, and whichever prices the type
        /// actually uses, plus reduce-only (a close and an entry of equal size are opposite
        /// intentions and must not collapse into one). Deliberately excluded: leverage,
        /// margin type, time-in-force, post-only, position side, and the trailing fields —
        /// nudging one of those and re-submitting inside 30 seconds is still the same order
        /// twice, and a user who really means to place both can wait out the window.
        /// </para>
        ///
        /// <para>
        /// Quantity and price are formatted round-trip ("R") so 0.1 keys identically every
        /// time; no rounding, because two orders that differ in the ninth decimal are two
        /// orders the venue will treat differently.
        /// </para>
        /// </summary>
        private static string DedupKeyFor(string providerName, TradeSignal provided, TradeSignal signal)
        {
            if (provided.ClientOid is { Length: > 0 } oid)
                return $"{providerName}|oid:{oid}";

            static string N(double? v) => v?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "-";

            return string.Join('|',
                providerName,
                "id:" + signal.Symbol,
                signal.Side.ToString(),
                signal.Type.ToString(),
                N(signal.Quantity),
                N(signal.Price),
                N(signal.TriggerPrice),
                signal.ReduceOnly ? "ro" : "-",
                signal.SubType ?? "-");
        }

        private static bool IsFinitePositive(double v) =>
            !double.IsNaN(v) && !double.IsInfinity(v) && v > 0.0;

        /// <summary>True when a PlaceOrderAsync return value is a failure sentinel rather than an order id.</summary>
        private static bool IsErrorSentinel(string result) =>
            result.StartsWith("ORDER_", StringComparison.Ordinal)
            || result.StartsWith("PROVIDER_", StringComparison.Ordinal);

        /// <summary>
        /// Confirms that a bracket order's protective legs are visible on the exchange.
        /// Conservative by design: it only alarms when NO open order could plausibly be
        /// the protection — either a conditional (stop/TP-type) order on the opposite
        /// side, or any order for the symbol carrying the requested SL/TP values
        /// (providers that embed protection in the entry order). False alarms train the
        /// user to ignore the warning, so uncertainty is phrased as "verify", and a
        /// failed scan is reported as such rather than as a missing stop.
        /// </summary>
        // Settle time before the verification scan — list endpoints lag slightly
        // behind order placement. Internal so tests can shorten it.
        internal TimeSpan ProtectionVerifyDelay = TimeSpan.FromSeconds(2);

        internal async Task VerifyProtectiveOrdersAsync(ITradingProvider tp, string providerName, TradeSignal signal)
        {
            // Give the exchange a moment to register the protective legs — they are
            // posted immediately after the entry, but list endpoints lag slightly.
            await Task.Delay(ProtectionVerifyDelay).ConfigureAwait(false);

            List<OpenOrder> open;
            try
            {
                open = await tp.GetOpenOrdersAsync(signal.Symbol).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Protective-order verification scan failed for {Symbol}/{Provider}.",
                    signal.Symbol, providerName);
                _errorCoordinator.ReportError(
                    $"Could not verify stop loss or take profit for {signal.Symbol} — check your open orders.",
                    ErrorSeverity.High);
                return;
            }

            bool hasProtection = open.Any(o =>
                (IsProtectiveType(o.Type) && o.Side != signal.Side)
                || o.StopLoss.HasValue
                || o.TakeProfit.HasValue);

            if (!hasProtection)
            {
                _logger.LogWarning(
                    "No protective order found after bracket placement: {Symbol} on {Provider} (SL={StopLoss}, TP={TakeProfit}).",
                    signal.Symbol, providerName, signal.StopLoss, signal.TakeProfit);
                _errorCoordinator.ReportError(
                    $"Warning: no stop loss or take profit found on the exchange for {signal.Symbol}. " +
                    "The position may be unprotected — verify your open orders.",
                    ErrorSeverity.High);
            }
        }

        private static bool IsProtectiveType(OrderType t) =>
            t == OrderType.StopMarket || t == OrderType.StopLimit
            || t == OrderType.TakeProfitMarket || t == OrderType.TakeProfitLimit;

        // ── Order-status polling fallback (non-streaming brokers) ─────────────
        // Poll cadence: fast while the order is fresh (market orders resolve in
        // seconds), then slow for resting limit/GTC orders. Internal so tests can
        // shorten them. The loop ends when the order resolves, the provider
        // disconnects, polling fails repeatedly, or the service is disposed.
        internal TimeSpan OrderPollFastInterval = TimeSpan.FromSeconds(5);
        internal TimeSpan OrderPollSlowInterval = TimeSpan.FromSeconds(30);
        internal int OrderPollFastCount = 12;          // ~1 min of fast polling
        internal int OrderPollMaxConsecutiveErrors = 5;

        /// <summary>How many poll watches PlaceOrderAsync has started, incremented at the
        /// decision site itself. The streaming/non-streaming choice is made synchronously
        /// inside PlaceOrderAsync, so a test can assert it the moment the call returns —
        /// "no watch was started" is a fact here, where "no poll arrived within 100 ms"
        /// was a race the thread pool usually happened to win.</summary>
        internal int OrderWatchesStarted;

        private readonly System.Threading.CancellationTokenSource _disposeCts = new();

        /// <summary>
        /// Watches one placed order on a non-streaming broker by polling
        /// GetOpenOrdersAsync until the order leaves the open list, then looks for a
        /// matching fill record and publishes the same OrderUpdate a streaming broker
        /// would have pushed — so "Order filled / Stop loss hit / Take profit hit"
        /// announce on Schwab/Tradier too. Known limitation (documented in the manual):
        /// protective legs the BROKER attaches server-side to an entry get their own
        /// order ids we never see, so only orders placed through the terminal are
        /// watched.
        /// </summary>
        internal async Task PollOrderUntilResolvedAsync(
            ITradingProvider tp, string providerName, TradeSignal signal, string orderId)
        {
            var ct = _disposeCts.Token;
            int errors = 0;
            for (int i = 0; !ct.IsCancellationRequested; i++)
            {
                try
                {
                    await Task.Delay(i < OrderPollFastCount ? OrderPollFastInterval : OrderPollSlowInterval, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }

                if (!tp.IsConnected) return; // reconnect starts a fresh session; stale watch ends here

                // Preferred path: an authoritative broker order-by-id lookup. Used
                // for brokers whose fill records don't carry the placed order id
                // (Tradier/Schwab), where the open-list + fills heuristic below
                // would mis-resolve a filled order as "cancelled".
                if (tp.SupportsOrderStatusQuery)
                {
                    OrderStatusSnapshot? snap;
                    try
                    {
                        snap = await tp.GetOrderStatusAsync(orderId, signal.Symbol).ConfigureAwait(false);
                        errors = 0;
                    }
                    catch (Exception ex)
                    {
                        if (++errors >= OrderPollMaxConsecutiveErrors)
                        {
                            _logger.LogWarning(ex,
                                "Order-status lookup gave up for {OrderId} on {Provider} after {Errors} consecutive failures.",
                                orderId, providerName, errors);
                            _errorCoordinator.ReportError(
                                $"Could not verify the status of your {signal.Symbol} order — check your open orders.",
                                ErrorSeverity.Medium);
                            return;
                        }
                        continue;
                    }

                    if (snap == null) continue;                       // transient — retry next tick
                    if (snap.State is PolledOrderState.Working
                                   or PolledOrderState.PartiallyFilled) continue; // still resolving

                    var status = snap.State switch
                    {
                        PolledOrderState.Filled    => OrderStatus.Filled,
                        PolledOrderState.Cancelled => OrderStatus.Cancelled,
                        PolledOrderState.Rejected  => OrderStatus.Rejected,
                        PolledOrderState.Expired   => OrderStatus.Expired,
                        // Replaced ends this watch: the order lives on under a new
                        // id we never learned, so it cannot be polled further. The
                        // announcement tells the trader the order is still working.
                        PolledOrderState.Replaced  => OrderStatus.Replaced,
                        // Working/PartiallyFilled continue above; anything new is a
                        // contract change and must not be guessed into a rejection.
                        _                          => OrderStatus.Unknown,
                    };
                    PublishOrderEvent(new OrderUpdate(
                        orderId, snap.Symbol, snap.Side,
                        snap.FilledQuantity, snap.FilledPrice,
                        RemainingQuantity: snap.RemainingQuantity, status,
                        StopTriggered: snap.StopTriggered || (status == OrderStatus.Filled && IsStopType(signal.Type)),
                        TakeProfitTriggered: snap.TakeProfitTriggered || (status == OrderStatus.Filled && IsTakeProfitType(signal.Type)),
                        Timestamp: DateTime.UtcNow));
                    return;
                }

                List<OpenOrder> open;
                try
                {
                    open = await tp.GetOpenOrdersAsync(signal.Symbol).ConfigureAwait(false);
                    errors = 0;
                }
                catch (Exception ex)
                {
                    if (++errors >= OrderPollMaxConsecutiveErrors)
                    {
                        _logger.LogWarning(ex,
                            "Order-status polling gave up for {OrderId} on {Provider} after {Errors} consecutive failures.",
                            orderId, providerName, errors);
                        _errorCoordinator.ReportError(
                            $"Could not verify the status of your {signal.Symbol} order — check your open orders.",
                            ErrorSeverity.Medium);
                        return;
                    }
                    continue;
                }

                if (open.Any(o => o.Id == orderId)) continue; // still working

                // Gone from the open list → resolved. Find the fill; the fills
                // endpoint can lag the open-orders endpoint by a beat, so retry a
                // few times before concluding the order was cancelled.
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        var fills = await tp.GetFillsAsync(signal.Symbol, 50).ConfigureAwait(false);
                        var fill = fills.FirstOrDefault(f => f.OrderId == orderId || f.Id == orderId);
                        if (fill != null)
                        {
                            PublishOrderEvent(new OrderUpdate(
                                orderId, fill.Symbol, fill.Side, fill.Quantity, fill.Price,
                                RemainingQuantity: 0, OrderStatus.Filled,
                                // Polling can't see the broker's trigger flags, but the
                                // ORDER TYPE we placed tells us what a fill means.
                                StopTriggered: IsStopType(signal.Type),
                                TakeProfitTriggered: IsTakeProfitType(signal.Type),
                                Timestamp: fill.FilledAt,
                                RealizedPnL: fill.RealizedPnL == 0.0 ? null : fill.RealizedPnL));
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Fill lookup failed for {OrderId} on {Provider} (attempt {Attempt}).",
                            orderId, providerName, attempt + 1);
                    }
                    try { await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }

                // No fill record → cancelled/expired/rejected upstream.
                _logger.LogInformation(
                    "Order {OrderId} on {Provider} left the open list with no fill record — treating as cancelled.",
                    orderId, providerName);
                PublishOrderEvent(new OrderUpdate(
                    orderId, signal.Symbol, signal.Side, FilledQuantity: 0, FilledPrice: 0,
                    RemainingQuantity: signal.Quantity, OrderStatus.Cancelled,
                    StopTriggered: false, TakeProfitTriggered: false, Timestamp: DateTime.UtcNow));
                return;
            }
        }

        private static bool IsStopType(OrderType t) =>
            t == OrderType.StopMarket || t == OrderType.StopLimit;

        private static bool IsTakeProfitType(OrderType t) =>
            t == OrderType.TakeProfitMarket || t == OrderType.TakeProfitLimit;

        public async Task<bool> SupportsOcoPairsAsync(string providerName)
        {
            var tp = await GetTradingProviderAsync(providerName).ConfigureAwait(false);
            return tp is IPaperTradingProvider or Sdk.Plugins.IOcoTradingProvider;
        }

        public async Task<(bool Ok, string Message)> PlaceOcoPairAsync(string providerName, string symbol,
            OrderSide side, double quantity, double limitPrice, double stopTriggerPrice)
        {
            // Same sanity clamps as single orders — money path.
            if (!IsFinitePositive(quantity) || quantity > MaxOrderQuantity
                || !IsFinitePositive(limitPrice) || !IsFinitePositive(stopTriggerPrice))
                return (false, "OCO pair rejected: quantity and both prices must be finite positive numbers.");
            // Layout sanity here too (the dashboard also pre-checks, but the modal
            // isn't the only possible caller): sell = limit above stop, buy = mirror.
            bool sane = side == OrderSide.Sell ? limitPrice > stopTriggerPrice : stopTriggerPrice > limitPrice;
            if (!sane)
                return (false, side == OrderSide.Sell
                    ? "OCO pair rejected: for a sell pair the limit price belongs above the stop trigger."
                    : "OCO pair rejected: for a buy pair the stop trigger belongs above the limit price.");

            var tp = await GetTradingProviderAsync(providerName).ConfigureAwait(false);
            if (tp == null || !tp.IsConnected)
                return (false, $"{providerName} is not connected.");

            // Exchange-native pairing when the provider implements it (paper takes
            // the grouped path below — its simulator IS the pairing engine).
            if (tp is Sdk.Plugins.IOcoTradingProvider native && tp is not IPaperTradingProvider)
            {
                string result = await native.PlaceOcoPairAsync(symbol, side, quantity, limitPrice, stopTriggerPrice)
                    .ConfigureAwait(false);
                if (IsErrorSentinel(result))
                {
                    _errorCoordinator.ReportError($"OCO pair failed on {providerName}.", ErrorSeverity.High);
                    return (false, $"OCO pair failed: {result}");
                }
                _errorCoordinator.ReportSuccess($"OCO pair placed on {providerName} (exchange-linked).");
                return (true, "OCO pair resting, linked by the exchange: whichever fills first cancels the other.");
            }

            if (tp is IPaperTradingProvider)
            {
                string group = "oco-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                var limitLeg = new TradeSignal(symbol, side, quantity, OrderType.Limit,
                    Price: limitPrice, OcoGroupId: group);
                var stopLeg = new TradeSignal(symbol, side, quantity, OrderType.StopMarket,
                    TriggerPrice: stopTriggerPrice, OcoGroupId: group);

                string r1 = await PlaceOrderAsync(providerName, limitLeg).ConfigureAwait(false);
                if (IsErrorSentinel(r1)) return (false, "OCO limit leg failed — nothing was placed.");
                string r2 = await PlaceOrderAsync(providerName, stopLeg).ConfigureAwait(false);
                if (IsErrorSentinel(r2))
                {
                    // Never leave half a pair resting.
                    await CancelOrderAsync(providerName, r1, symbol).ConfigureAwait(false);
                    return (false, "OCO stop leg failed — the limit leg was cancelled, nothing is resting.");
                }
                return (true, "OCO pair resting: whichever fills first cancels the other.");
            }

            return (false, $"{providerName} cannot place linked OCO pairs from this terminal yet.");
        }

        public async Task<bool> CancelOrderAsync(string providerName, string orderId, string symbol)
        {
            var tp = await GetTradingProviderAsync(providerName).ConfigureAwait(false);
            if (tp == null || !tp.IsConnected) return false;
            try
            {
                return await tp.CancelOrderAsync(orderId, symbol).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CancelOrder failed for {OrderId}", orderId);
                return false;
            }
        }

        // ── Account data ───────────────────────────────────────────────────────

        public async Task<ProviderResult<List<Balance>>> GetBalancesAsync(string providerName)
        {
            var tp = await GetTradingProviderAsync(providerName).ConfigureAwait(false);
            if (tp == null) return ProviderResult<List<Balance>>.Unavailable("no trading provider is configured");
            if (!tp.IsConnected) return ProviderResult<List<Balance>>.Unavailable("the provider is not connected");
            try
            {
                var balances = await tp.GetBalancesAsync().ConfigureAwait(false);

                // Feed the quick-trade sizer. It cannot fetch a balance itself — by design, so it
                // stays unit-testable and can never place an order as a side effect — so whoever
                // reads one publishes it here.
                //
                // CASH ONLY, never the sum of every balance — and never a sum ACROSS
                // currencies either. Balances are per-asset and in their own units: adding
                // 0.5 BTC to 3000 USDT to 12 ETH produces a number that is not money in any
                // currency, and it would then be multiplied by a risk percentage to size a
                // real order. This line used to make the same mistake one class down by
                // summing every *cash* line, so ¥1,000,000 plus $2,000 reported 1,002,000.
                // ReportCashLines keeps each currency separate; the sizer reads one of them.
                _equity.ReportCashLines(balances.Select(b => (b.Asset, b.Free + b.Locked)));

                return ProviderResult<List<Balance>>.Ok(balances);
            }
            catch (Exception ex)
            {
                // Deliberately does NOT report an equity of zero on failure. The
                // sizer would then treat the account as empty and refuse every
                // trade, and the reason would be invisible.
                _logger.LogWarning(ex, "GetBalances failed for {Provider}", providerName);
                return ProviderResult.FromException<List<Balance>>(ex, "Reading balances");
            }
        }

        /// <summary>
        /// Positions, or the reason there are none to show. A spot-only venue has
        /// no positions concept at all, which is a different answer from "you have
        /// none" and very different from "the fetch failed".
        /// </summary>
        public async Task<ProviderResult<List<Position>>> GetPositionsAsync(string providerName)
        {
            var tp = await GetTradingProviderAsync(providerName).ConfigureAwait(false);
            if (tp == null) return ProviderResult<List<Position>>.Unavailable("no trading provider is configured");
            if (!tp.IsConnected) return ProviderResult<List<Position>>.Unavailable("the provider is not connected");

            if (!tp.Capabilities.HasFlag(ProviderCapabilities.MarginTrading) &&
                !tp.Capabilities.HasFlag(ProviderCapabilities.FuturesTrading))
                return ProviderResult<List<Position>>.NotSupported(
                    $"{providerName} is spot only, so it has no separate positions — what you hold is on the Balances tab");

            try { return ProviderResult<List<Position>>.Ok(await tp.GetPositionsAsync().ConfigureAwait(false)); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetPositions failed for {Provider}", providerName);
                return ProviderResult.FromException<List<Position>>(ex, "Reading positions");
            }
        }

        public async Task<ProviderResult<List<OpenOrder>>> GetOpenOrdersAsync(string providerName, string? symbol = null)
        {
            var tp = await GetTradingProviderAsync(providerName).ConfigureAwait(false);
            if (tp == null) return ProviderResult<List<OpenOrder>>.Unavailable("no trading provider is configured");
            if (!tp.IsConnected) return ProviderResult<List<OpenOrder>>.Unavailable("the provider is not connected");
            try { return ProviderResult<List<OpenOrder>>.Ok(await tp.GetOpenOrdersAsync(symbol).ConfigureAwait(false)); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetOpenOrders failed for {Provider}", providerName);
                return ProviderResult.FromException<List<OpenOrder>>(ex, "Reading open orders");
            }
        }

        public async Task<double> GetMaxLeverageAsync(string providerName)
        {
            var tp = await GetTradingProviderAsync(providerName).ConfigureAwait(false);
            return tp?.MaxLeverage ?? 1.0;
        }

        public async Task<double> SetLeverageAsync(string providerName, string symbol, double leverage)
        {
            var tp = await GetTradingProviderAsync(providerName).ConfigureAwait(false);
            if (tp == null || !tp.IsConnected) return 1.0;
            try { return await tp.SetLeverageAsync(symbol, leverage).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "SetLeverage failed"); return 1.0; }
        }

        public async Task<bool> SupportsTradingAsync(string providerName)
        {
            if (string.IsNullOrEmpty(providerName)) return false;
            var tp = await GetTradingProviderAsync(providerName).ConfigureAwait(false);
            return tp != null;
        }

        public async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string providerName, string symbol, int depth = 10)
        {
            var provider = await _dataService.GetProviderAsync(providerName).ConfigureAwait(false);
            if (provider == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
            try { return await provider.GetOrderBookAsync(symbol, depth).ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetOrderBook failed for {Symbol}", symbol);
                return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
            }
        }

        public async Task<IObservable<OrderBookUpdate>?> SubscribeOrderBookAsync(string providerName, string symbol)
        {
            var provider = await _dataService.GetProviderAsync(providerName).ConfigureAwait(false);
            var ob = provider?.GetCapability<IOrderBookProvider>();
            if (ob == null) return null;
            try
            {
                return ob.SubscribeOrderBook(symbol);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SubscribeOrderBook failed for {Symbol} on {Provider}", symbol, providerName);
                return null;
            }
        }

        public async Task<ProviderResult<List<TradeFill>>> GetFillsAsync(string providerName, string? symbol = null, int limit = 50)
        {
            var tp = await GetTradingProviderAsync(providerName).ConfigureAwait(false);
            if (tp == null) return ProviderResult<List<TradeFill>>.Unavailable("no trading provider is configured");
            try { return ProviderResult<List<TradeFill>>.Ok(await tp.GetFillsAsync(symbol, limit).ConfigureAwait(false)); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetFillsAsync failed for {Provider}", providerName);
                return ProviderResult.FromException<List<TradeFill>>(ex, "Reading fill history");
            }
        }

        public async Task<bool> SupportsMarginTradingAsync(string providerName)
        {
            var tp = await GetTradingProviderAsync(providerName).ConfigureAwait(false);
            return tp?.SupportsMarginTrading ?? false;
        }

        public async Task<ProviderCapabilities> GetCapabilitiesAsync(string providerName)
        {
            // GetTradingProviderAsync already routes to the paper broker when paper
            // mode is on, so the panel gates on the broker actually executing.
            var tp = await GetTradingProviderAsync(providerName).ConfigureAwait(false);
            return tp?.Capabilities ?? ProviderCapabilities.None;
        }
    }
}
