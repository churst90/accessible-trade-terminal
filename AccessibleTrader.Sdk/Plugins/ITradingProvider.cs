using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Sdk.Plugins
{
    // ── Order types ──────────────────────────────────────────────────────────────

    public enum OrderSide { Buy, Sell }

    /// <summary>Supported order execution types.</summary>
    public enum OrderType
    {
        Market,
        Limit,
        StopMarket,     // Stop-loss as market order once trigger price is hit
        StopLimit,      // Stop-loss as limit order once trigger price is hit
        TakeProfitMarket,
        TakeProfitLimit,
    }

    /// <summary>How a trailing-stop distance is interpreted.</summary>
    public enum TrailMode { Amount, Percent, CallbackRate }

    // ── Signal / data records ─────────────────────────────────────────────────

    /// <summary>
    /// Encapsulates everything needed to place a trading order, including optional
    /// stop-loss and take-profit levels for risk management.
    /// </summary>
    public record TradeSignal(
        string    Symbol,
        OrderSide Side,
        double    Quantity,
        OrderType Type        = OrderType.Market,
        double?   Price       = null,        // Limit price (required for Limit orders)
        double?   StopLoss    = null,        // Price at which to exit at a loss
        double?   TakeProfit  = null,        // Price at which to lock in profit
        double?   Leverage    = null,        // Desired leverage multiplier (futures/margin)
        string?   ClientOid   = null,        // Optional client-supplied order ID for tracking
        string?   SubType     = null,        // "Futures" routes to the futures API; null / "Spot" = spot
        string?    MarginType     = null,     // "Isolated" or "Cross" (futures/margin only)
        double?    TriggerPrice   = null,     // Trigger price for Stop / Stop-Limit / Take-Profit order types
        TrailMode? TrailStopMode  = null,     // Trailing stop: how TrailStopValue is read
        double?    TrailStopValue = null,     // Trailing stop distance (amount / percent / callback rate)
        TrailMode? TrailTpMode    = null,     // Trailing take-profit: how TrailTpValue is read
        double?    TrailTpValue   = null,     // Trailing take-profit distance
        double?    TrailTpActivation = null,  // Price at which the trailing take-profit arms and starts trailing
        string?    TimeInForce    = null,     // GTC / IOC / FOK / Day / GTD
        bool       ReduceOnly     = false,    // Futures: order may only reduce a position
        bool       PostOnly       = false,    // Limit orders: maker-only
        string?    PositionSide   = null      // Hedge mode: "LONG" / "SHORT" / "BOTH"
    );

    public record Balance(string Asset, double Free, double Locked);

    /// <summary>An open futures/margin position with live P&amp;L data.</summary>
    public record Position(
        string Symbol,
        double Quantity,
        double AveragePrice,
        double MarketValue,
        double UnrealizedPnL,
        double Leverage = 1.0,
        double LiquidationPrice = 0.0
    );

    public record OpenOrder(
        string    Id,
        string    Symbol,
        OrderSide Side,
        OrderType Type,
        double    Quantity,
        double    Price,
        string    Status,
        double?   StopLoss   = null,
        double?   TakeProfit = null
    );

    /// <summary>A completed (filled) trade record.</summary>
    public record TradeFill(
        string    Id,
        string    Symbol,
        OrderSide Side,
        double    Quantity,
        double    Price,
        DateTime  FilledAt,
        double    Fee = 0.0,
        string?   OrderId = null
    );

    // ── Interface ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Optional capability interface for plugins that support live trading.
    /// Query via <c>plugin.GetCapability&lt;ITradingProvider&gt;()</c>.
    /// </summary>
    public interface ITradingProvider : IProviderPlugin
    {
        /// <summary>True when the trading connection is live and authenticated.</summary>
        bool IsConnected { get; }

        /// <summary>Whether this provider supports margin / leverage trading.</summary>
        bool SupportsMarginTrading { get; }

        /// <summary>Whether this provider supports futures contracts.</summary>
        bool SupportsFuturesTrading { get; }

        /// <summary>Maximum leverage multiplier available on this exchange.</summary>
        double MaxLeverage { get; }

        /// <summary>
        /// Observable stream of order status updates (fills, cancels, stops, etc.).
        /// Emits <see cref="OrderUpdate"/> records whenever the broker pushes an update.
        /// Implementations that do not support streaming should return <see cref="Observable.Empty{T}"/>.
        /// </summary>
        IObservable<OrderUpdate> OrderUpdateStream { get; }

        // Account queries

        /// <summary>
        /// Fetches every non-zero balance held on the account. Implementations should
        /// include both free and locked portions per asset; zero balances are typically
        /// filtered by the caller.
        /// </summary>
        Task<List<Balance>>   GetBalancesAsync();

        /// <summary>
        /// Returns every open futures / margin position. Spot-only accounts should
        /// return an empty list. P&amp;L fields are exchange-reported when available and
        /// computed from mark price otherwise.
        /// </summary>
        Task<List<Position>>  GetPositionsAsync();

        /// <summary>
        /// Returns every open (unfilled) order. When <paramref name="symbol"/> is
        /// supplied, the scope is restricted to that symbol; some exchanges
        /// (e.g. MEXC spot) require a non-null symbol and will return an empty list
        /// when called with null.
        /// </summary>
        Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null);

        // Order management

        /// <summary>
        /// Places an order. Implementations should honour StopLoss and TakeProfit fields
        /// on <paramref name="signal"/> if <see cref="SupportsMarginTrading"/> is true.
        /// </summary>
        Task<string> PlaceOrderAsync(TradeSignal signal);

        /// <summary>
        /// Cancels an open order. The <paramref name="symbol"/> parameter is required by
        /// most crypto exchanges; equity brokers typically ignore it. Returns true on
        /// success, false if the order was already filled / cancelled / unknown.
        /// </summary>
        Task<bool>   CancelOrderAsync(string orderId, string symbol);

        /// <summary>
        /// Sets or updates the leverage for a symbol (futures/margin only).
        /// Returns the actual leverage applied by the exchange.
        /// </summary>
        Task<double> SetLeverageAsync(string symbol, double leverage);
    }
}
