using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Core.Services
{
    public interface IOrderExecutionService
    {
        // ── Order execution ────────────────────────────────────────────────────
        Task<string> PlaceOrderAsync(string provider, TradeSignal signal);
        Task<bool>   CancelOrderAsync(string provider, string orderId, string symbol);

        // ── Account data (requires ITradingProvider) ───────────────────────────
        /// <summary>Returns all asset balances for the given provider. Empty list if unsupported.</summary>
        Task<List<Balance>>   GetBalancesAsync(string provider);

        /// <summary>Returns all open positions for the given provider. Empty list if unsupported or spot-only.</summary>
        Task<List<Position>>  GetPositionsAsync(string provider);

        /// <summary>Returns open orders, optionally filtered by symbol.</summary>
        Task<List<OpenOrder>> GetOpenOrdersAsync(string provider, string? symbol = null);

        /// <summary>Returns the maximum leverage multiplier supported by the provider (1.0 if unsupported).</summary>
        Task<double> GetMaxLeverageAsync(string provider);

        /// <summary>Sets leverage for a symbol on the provider and returns the actual leverage applied.</summary>
        Task<double> SetLeverageAsync(string provider, string symbol, double leverage);

        /// <summary>
        /// Returns true if the named provider implements <see cref="ITradingProvider"/> and is
        /// therefore capable of executing orders.  Data-only providers (e.g. FRED, Bitstamp read-only)
        /// return false and the Trading Dashboard should not open for them.
        /// </summary>
        Task<bool> SupportsTradingAsync(string provider);

        /// <summary>Returns the current order book depth for <paramref name="symbol"/>.</summary>
        Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string provider, string symbol, int depth = 10);

        /// <summary>
        /// Returns a live observable stream of order-book updates from
        /// <paramref name="symbol"/> on <paramref name="provider"/>, or null if the
        /// provider does not implement <see cref="IOrderBookProvider"/> (most analytics
        /// providers, FRED, etc.). Modal subscribers should fall back to repeated
        /// <see cref="GetOrderBookAsync"/> polling — or simply leave the snapshot
        /// static — when this returns null.
        /// </summary>
        Task<IObservable<OrderBookUpdate>?> SubscribeOrderBookAsync(string provider, string symbol);

        /// <summary>Returns recent filled trades for the account on the given provider.</summary>
        Task<List<TradeFill>> GetFillsAsync(string provider, string? symbol = null, int limit = 50);

        /// <summary>Returns true when the provider supports margin / leverage trading (Isolated or Cross).</summary>
        Task<bool> SupportsMarginTradingAsync(string provider);

        /// <summary>
        /// Returns the trading capability flags of the (effective) provider — the
        /// paper broker when paper mode is on, otherwise the named provider. The
        /// order panel uses these to show only the controls the provider supports
        /// (e.g. trailing stops, leverage, brackets). <see cref="ProviderCapabilities.None"/>
        /// for data-only providers.
        /// </summary>
        Task<ProviderCapabilities> GetCapabilitiesAsync(string provider);
    }
}
