using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
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

            var tp = await GetTradingProviderAsync(providerName);
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
            var provider = await _dataService.GetProviderAsync(providerName);
            return provider as ITradingProvider;
        }

        // ── Order execution ────────────────────────────────────────────────────

        public async Task<string> PlaceOrderAsync(string providerName, TradeSignal signal)
        {
            _logger.LogInformation("Placing {Side} order for {Symbol} via {Provider}",
                signal.Side, signal.Symbol, providerName);

            var tp = await GetTradingProviderAsync(providerName);
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
                var result = await tp.PlaceOrderAsync(signal);
                _errorCoordinator.ReportSuccess($"Order placed: {signal.Side} {signal.Quantity} {signal.Symbol}");
                return result;
            }
            catch (Exception ex)
            {
                _errorCoordinator.ReportError($"Order failed: {ex.Message}", ErrorSeverity.High);
                return "ORDER_FAILED";
            }
        }

        public async Task<bool> CancelOrderAsync(string providerName, string orderId, string symbol)
        {
            var tp = await GetTradingProviderAsync(providerName);
            if (tp == null || !tp.IsConnected) return false;
            try
            {
                return await tp.CancelOrderAsync(orderId, symbol);
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
            var tp = await GetTradingProviderAsync(providerName);
            if (tp == null || !tp.IsConnected) return new List<Balance>();
            try { return await tp.GetBalancesAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "GetBalances failed"); return new List<Balance>(); }
        }

        public async Task<List<Position>> GetPositionsAsync(string providerName)
        {
            var tp = await GetTradingProviderAsync(providerName);
            if (tp == null || !tp.IsConnected) return new List<Position>();
            try { return await tp.GetPositionsAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "GetPositions failed"); return new List<Position>(); }
        }

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string providerName, string? symbol = null)
        {
            var tp = await GetTradingProviderAsync(providerName);
            if (tp == null || !tp.IsConnected) return new List<OpenOrder>();
            try { return await tp.GetOpenOrdersAsync(symbol); }
            catch (Exception ex) { _logger.LogWarning(ex, "GetOpenOrders failed"); return new List<OpenOrder>(); }
        }

        public async Task<double> GetMaxLeverageAsync(string providerName)
        {
            var tp = await GetTradingProviderAsync(providerName);
            return tp?.MaxLeverage ?? 1.0;
        }

        public async Task<double> SetLeverageAsync(string providerName, string symbol, double leverage)
        {
            var tp = await GetTradingProviderAsync(providerName);
            if (tp == null || !tp.IsConnected) return 1.0;
            try { return await tp.SetLeverageAsync(symbol, leverage); }
            catch (Exception ex) { _logger.LogWarning(ex, "SetLeverage failed"); return 1.0; }
        }

        public async Task<bool> SupportsTradingAsync(string providerName)
        {
            if (string.IsNullOrEmpty(providerName)) return false;
            var tp = await GetTradingProviderAsync(providerName);
            return tp != null;
        }

        public async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string providerName, string symbol, int depth = 10)
        {
            var provider = await _dataService.GetProviderAsync(providerName);
            if (provider == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
            try { return await provider.GetOrderBookAsync(symbol, depth); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetOrderBook failed for {Symbol}", symbol);
                return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
            }
        }

        public async Task<List<TradeFill>> GetFillsAsync(string providerName, string? symbol = null, int limit = 50)
        {
            // Stub: no provider currently exposes GetFillsAsync. Returns empty list.
            await Task.CompletedTask;
            return new List<TradeFill>();
        }

        public async Task<bool> SupportsMarginTradingAsync(string providerName)
        {
            var tp = await GetTradingProviderAsync(providerName);
            return tp?.SupportsMarginTrading ?? false;
        }
    }
}
