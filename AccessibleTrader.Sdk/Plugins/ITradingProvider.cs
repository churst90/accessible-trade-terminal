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
        string?    PositionSide   = null,     // Hedge mode: "LONG" / "SHORT" / "BOTH"
        string?    OcoGroupId     = null      // One-cancels-other: orders sharing a group id cancel each other on fill (paper broker enforces; exchanges with native OCO may map it)
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

    /// <summary>Resolution state of a single order, from an authoritative
    /// per-order status lookup (see <see cref="ITradingProvider.GetOrderStatusAsync"/>).
    /// <c>Expired</c> and <c>Replaced</c> are distinct terminal states, not
    /// flavours of <c>Cancelled</c>: an expired order timed out (nobody asked),
    /// and a replaced order is STILL LIVE under a new id — mapping it to
    /// <c>Cancelled</c> tells the trader they are flat while the order rests.
    /// A partially-filled-then-terminated order reports its terminal state with
    /// <see cref="OrderStatusSnapshot.FilledQuantity"/> carrying the executed
    /// part; the announcement speaks the fill.</summary>
    public enum PolledOrderState { Working, Filled, PartiallyFilled, Cancelled, Rejected, Expired, Replaced }

    /// <summary>
    /// Authoritative snapshot of one order's status, used by the order-service
    /// poller to resolve a placed order when live streaming is unavailable. This
    /// exists because some brokers' fill records don't carry the placed order id
    /// (Tradier/Schwab), so matching fills-to-order fails and a filled order was
    /// mis-announced as "cancelled". A direct order-by-id lookup avoids the guess.
    /// </summary>
    public record OrderStatusSnapshot(
        PolledOrderState State,
        OrderSide Side,
        string Symbol,
        double FilledQuantity,
        double FilledPrice,
        double RemainingQuantity,
        bool StopTriggered = false,
        bool TakeProfitTriggered = false);

    /// <summary>A completed (filled) trade record.</summary>
    public record TradeFill(
        string    Id,
        string    Symbol,
        OrderSide Side,
        double    Quantity,
        double    Price,
        DateTime  FilledAt,
        double    Fee = 0.0,
        string?   OrderId = null,
        double    RealizedPnL = 0.0   // realized P&L for the closed portion of this fill (quote currency)
    );

    // ── Interface ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Optional capability: exchange-NATIVE one-cancels-other pairs. The
    /// exchange links the two legs server-side, so the cancellation guarantee
    /// holds even if this terminal is offline when a leg fills. Providers that
    /// only declare <see cref="Enums.ProviderCapabilities.OCO"/> without this
    /// interface can NOT place linked pairs through the terminal (the order
    /// service refuses rather than resting two secretly-unlinked orders).
    /// </summary>
    public interface IOcoTradingProvider
    {
        /// <summary>
        /// Places a same-side, same-quantity pair: a LIMIT at
        /// <paramref name="limitPrice"/> and a STOP (market) triggered at
        /// <paramref name="stopTriggerPrice"/>, linked one-cancels-other.
        /// Returns the exchange's pair/list id, or an "ORDER_FAILED:…" sentinel.
        /// </summary>
        Task<string> PlaceOcoPairAsync(string symbol, OrderSide side, double quantity,
            double limitPrice, double stopTriggerPrice);
    }

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

        /// <summary>
        /// False when the broker can attach only ONE protective order to an entry
        /// (stop loss OR take profit, not both) — Kraken's close[] slot is the
        /// known case. The order service prefers the STOP (safety over profit)
        /// and warns the user that the take profit was not attached.
        /// </summary>
        bool SupportsSimultaneousStopAndTarget => true;

        /// <summary>
        /// True (the default) when <see cref="OrderUpdateStream"/> is actually fed by a
        /// broker push channel. Providers whose stream is a dead subject (no streaming
        /// implementation — e.g. Schwab/Tradier v1) MUST override this to false so the
        /// order service knows to fall back to status polling; otherwise fills there
        /// would never announce.
        /// </summary>
        bool SupportsOrderEventStreaming => true;

        /// <summary>
        /// True when the provider implements <see cref="GetOrderStatusAsync"/> with
        /// an authoritative broker order-by-id lookup. When true, the order-service
        /// poller resolves a placed order via that lookup INSTEAD of the
        /// open-orders + fills heuristic — required for brokers whose fill records
        /// don't carry the placed order id (Tradier/Schwab), where the heuristic
        /// would mis-announce a filled order as cancelled.
        /// </summary>
        bool SupportsOrderStatusQuery => false;

        /// <summary>
        /// Returns an authoritative status snapshot for a single order by id, or
        /// null when the provider can't resolve it right now (transient failure)
        /// or doesn't support the lookup. Only meaningful when
        /// <see cref="SupportsOrderStatusQuery"/> is true.
        /// </summary>
        Task<OrderStatusSnapshot?> GetOrderStatusAsync(string orderId, string? symbol = null)
            => Task.FromResult<OrderStatusSnapshot?>(null);

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

        /// <summary>
        /// Recent filled trades for the account (newest first), optionally filtered
        /// by <paramref name="symbol"/>. Default returns empty so existing providers
        /// need no change; the paper broker and history-capable providers override it.
        /// </summary>
        Task<List<TradeFill>> GetFillsAsync(string? symbol = null, int limit = 50)
            => Task.FromResult(new List<TradeFill>());
    }
}
