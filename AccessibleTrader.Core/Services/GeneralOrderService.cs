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

namespace AccessibleTrader.Core.Services
{
    public class GeneralOrderService : IOrderExecutionService, IDisposable
    {
        private readonly IDataService _dataService;
        private readonly IGlobalErrorCoordinator _errorCoordinator;
        private readonly ILogger<GeneralOrderService> _logger;
        private readonly IEventBus _eventBus;

        // Tracks the current provider's order-stream subscription so it can be swapped when provider changes.
        private IDisposable? _orderStreamSub;
        private string? _subscribedProvider;

        // ── Sanity clamps + idempotency dedup ────────────────────────────────
        // Hard upper bound for any single-order quantity. Real users will never
        // legitimately submit 1e7 of any asset; a Roslyn strategy bug emitting
        // 1e308 would otherwise tie up liquidity or trigger circuit breakers.
        // Per-asset precision is enforced by the exchange — we just block
        // obviously-broken payloads at the top of the call.
        private const double MaxOrderQuantity = 10_000_000.0;

        // In-memory dedup window: any PlaceOrderAsync attempt with the same
        // (provider, ClientOid) tuple within DedupWindow is rejected as a probable
        // double-submit. Covers UI double-click and post-network-flap retries.
        // Provider-side ClientOid enforcement (Binance) catches the rest.
        private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(30);
        private readonly Dictionary<string, DateTime> _recentOrders = new();
        private readonly object _recentOrdersLock = new();

        public GeneralOrderService(
            IDataService dataService,
            IGlobalErrorCoordinator errorCoordinator,
            ILogger<GeneralOrderService> logger,
            IEventBus eventBus)
        {
            _dataService = dataService;
            _errorCoordinator = errorCoordinator;
            _logger = logger;
            _eventBus = eventBus;
        }

        /// <summary>
        /// Subscribes to the OrderUpdateStream of <paramref name="providerName"/> and publishes
        /// typed trading events to the EventBus based on each update's status flags.
        /// Safe to call multiple times; automatically unsubscribes from the previous provider.
        /// </summary>
        public async Task SubscribeOrderUpdatesAsync(string providerName)
        {
            if (_subscribedProvider == providerName) return;

            _orderStreamSub?.Dispose();
            _subscribedProvider = null;

            var tp = await GetTradingProviderAsync(providerName).ConfigureAwait(false);
            if (tp == null) return;

            _subscribedProvider = providerName;
            _orderStreamSub = tp.OrderUpdateStream.Subscribe(update =>
            {
                PublishOrderEvent(update);
            });
        }

        private void PublishOrderEvent(OrderUpdate update)
        {
            if (update.StopTriggered)
            {
                _eventBus.Publish(new StopHitEvent(update));
                return;
            }
            if (update.TakeProfitTriggered)
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
                    _eventBus.Publish(new OrderRejectedEvent(update, $"Order {update.OrderId} rejected"));
                    break;
                case OrderStatus.Cancelled:
                    _logger.LogInformation("Order {OrderId} cancelled", update.OrderId);
                    break;
            }
        }

        public void Dispose()
        {
            _orderStreamSub?.Dispose();
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private async Task<ITradingProvider?> GetTradingProviderAsync(string providerName)
        {
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

            // ── 3. Dedup gate. A second Submit with the same (provider, ClientOid)
            //    inside the dedup window is treated as a probable double-fire (UI
            //    double-click, post-network-flap retry) and refused. The first
            //    submission's outcome is whatever it was — we don't second-guess it.
            string dedupKey = $"{providerName}|{signal.ClientOid}";
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
                        "Duplicate order suppressed: ClientOid {ClientOid} on {Provider} already submitted {SecondsAgo:F1}s ago",
                        signal.ClientOid, providerName, (now - ts).TotalSeconds);
                    _errorCoordinator.ReportError(
                        "Duplicate order suppressed (same client id submitted moments ago).",
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
                return "PROVIDER_NOT_SUPPORTED";
            }
            if (!tp.IsConnected)
            {
                _errorCoordinator.ReportError($"Cannot place order. {providerName} is not connected.", ErrorSeverity.High);
                return "PROVIDER_NOT_CONNECTED";
            }
            try
            {
                var result = await tp.PlaceOrderAsync(signal).ConfigureAwait(false);
                _errorCoordinator.ReportSuccess($"Order placed: {signal.Side} {signal.Quantity} {signal.Symbol}");
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
                return "ORDER_FAILED";
            }
        }

        private static bool IsFinitePositive(double v) =>
            !double.IsNaN(v) && !double.IsInfinity(v) && v > 0.0;

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

        public async Task<List<Balance>> GetBalancesAsync(string providerName)
        {
            var tp = await GetTradingProviderAsync(providerName).ConfigureAwait(false);
            if (tp == null || !tp.IsConnected) return new List<Balance>();
            try { return await tp.GetBalancesAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "GetBalances failed"); return new List<Balance>(); }
        }

        public async Task<List<Position>> GetPositionsAsync(string providerName)
        {
            var tp = await GetTradingProviderAsync(providerName).ConfigureAwait(false);
            if (tp == null || !tp.IsConnected) return new List<Position>();
            try { return await tp.GetPositionsAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "GetPositions failed"); return new List<Position>(); }
        }

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string providerName, string? symbol = null)
        {
            var tp = await GetTradingProviderAsync(providerName).ConfigureAwait(false);
            if (tp == null || !tp.IsConnected) return new List<OpenOrder>();
            try { return await tp.GetOpenOrdersAsync(symbol).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "GetOpenOrders failed"); return new List<OpenOrder>(); }
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

        public async Task<List<TradeFill>> GetFillsAsync(string providerName, string? symbol = null, int limit = 50)
        {
            // Stub: no provider currently exposes GetFillsAsync. Returns empty list.
            await Task.CompletedTask.ConfigureAwait(false);
            return new List<TradeFill>();
        }

        public async Task<bool> SupportsMarginTradingAsync(string providerName)
        {
            var tp = await GetTradingProviderAsync(providerName).ConfigureAwait(false);
            return tp?.SupportsMarginTrading ?? false;
        }
    }
}
