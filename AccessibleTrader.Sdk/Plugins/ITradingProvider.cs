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
        // ── StopLoss / TakeProfit mean TWO different things, and which one depends
        //    entirely on Type. This ambiguity has cost real money twice, so read it once.
        //
        //    On an ENTRY (Market, Limit): a BRACKET to attach after the fill. "Buy, and
        //    protect the resulting position here."
        //
        //    On a STOP or TAKE-PROFIT order type: the order's OWN TRIGGER — the same number
        //    as TriggerPrice. Plugins disagree about which field to read (Kraken and Coinbase
        //    read these; Gemini and Binance prefer TriggerPrice), so
        //    GeneralOrderService.NormaliseTrigger fills in whichever is missing from the other
        //    before any signal reaches a plugin. A PLUGIN AUTHOR SHOULD READ
        //    `TriggerPrice ?? StopLoss` (or `?? TakeProfit`) AND WILL THEN BE CORRECT EITHER
        //    WAY; a CALLER should set TriggerPrice and let the normaliser do the rest.
        double?   StopLoss    = null,        // entry: protective leg — stop order: its trigger
        double?   TakeProfit  = null,        // entry: profit leg    — TP order: its trigger
        double?   Leverage    = null,        // Desired leverage multiplier (futures/margin)
        string?   ClientOid   = null,        // Optional client-supplied order ID for tracking
        string?   SubType     = null,        // "Futures" routes to the futures API; null / "Spot" = spot
        string?    MarginType     = null,     // "Isolated" or "Cross" (futures/margin only)
        // The CANONICAL trigger for Stop / Stop-Limit / Take-Profit order types. Prefer this
        // over StopLoss/TakeProfit when constructing such an order; see the note on those.
        double?    TriggerPrice   = null,
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

    /// <summary>
    /// How a position's collateral is held, which decides what can liquidate it.
    ///
    /// <para><see cref="Isolated"/> caps the loss at the collateral posted against
    /// that one position: it liquidates on its own and takes nothing else with it.
    /// <see cref="Cross"/> draws on the whole account, so it survives longer and
    /// then takes every other cross position down with it. Those are different
    /// trades with the same entry, which is why the mode belongs on the row rather
    /// than in a setting somewhere.</para>
    ///
    /// <para><see cref="None"/> is the honest answer for spot, where nothing is
    /// borrowed and there is no collateral to hold either way — NOT a synonym for
    /// "cross". A venue that does not report the mode should say <see cref="None"/>
    /// rather than guess; the dashboard renders it as plain spot and says nothing
    /// about margin, where guessing would print a liquidation story that is not
    /// true of the position.</para>
    /// </summary>
    public enum MarginMode { None, Cross, Isolated }

    /// <summary>An open futures/margin position with live P&amp;L data.
    ///
    /// <para><b><see cref="AveragePrice"/> is PER UNIT, not the position's total cost.</b>
    /// Several venues report the total quote-currency cost instead — Kraken's
    /// <c>cost</c> and Tradier's <c>cost_basis</c> are both totals — and passing one
    /// of those straight in makes a 0.5 BTC position entered at 60,000 report an
    /// average price of 30,000. This number is *spoken* in the positions panel and it
    /// feeds risk math, so divide by <c>Math.Abs(quantity)</c> with a zero guard at
    /// the provider boundary. Binance's <c>entryPrice</c> and Schwab's
    /// <c>averagePrice</c> are already per-unit and need no division.</para>
    /// </summary>
    public record Position(
        string Symbol,
        double Quantity,
        double AveragePrice,
        double MarketValue,
        double UnrealizedPnL,
        double Leverage = 1.0,
        double LiquidationPrice = 0.0,
        MarginMode MarginMode = MarginMode.None
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
        /// Implementations that do not support streaming should return <c>Observable.Empty&lt;T&gt;()</c>.
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
        /// <summary>
        /// Whether this venue has an order-update stream AT ALL — a fact about the exchange,
        /// not about the current connection.
        ///
        /// <para>
        /// <b>This is the "never" that <see cref="SupportsOrderEventStreaming"/> cannot express.</b>
        /// That flag is allowed to be dynamic — Alpaca returns
        /// <c>_tradeStreamListening &amp;&amp; socket.IsConnected</c>, false at rest and true once
        /// its socket comes up — so a caller reading it once cannot tell "this venue has no
        /// stream" from "the stream is not up yet". Treating the second as the first writes a
        /// working venue off forever; treating the first as the second reports a dead feed every
        /// poll about something that will never change. Both mistakes were made on 2026-09-07
        /// before this member existed.
        /// </para>
        ///
        /// <para>
        /// Override to <c>false</c> if the venue offers no order push channel and
        /// <see cref="OrderUpdateStream"/> is a dead subject (Gemini, Kraken Futures, Schwab).
        /// Fills there are resolved by the order-status poller instead, which follows only the
        /// orders this terminal placed — a genuinely narrower guarantee that a user watching
        /// with no session open has to be told about.
        /// </para>
        /// </summary>
        bool ProvidesOrderStream => true;

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
        /// Returns an authoritative status snapshot for a single order by id. Only meaningful
        /// when <see cref="SupportsOrderStatusQuery"/> is true.
        ///
        /// <para>
        /// <b>A transient failure MUST THROW, not return null.</b> Null means "the broker
        /// answered and has no such order"; it does not mean "I could not ask". The contract
        /// used to say null covered transient failure too, and both implementations carry a
        /// comment saying why that was wrong: the order poller counts consecutive failures and
        /// gives up with a spoken warning, so a null read as "still resolving" turned a dead
        /// endpoint into a silent infinite retry — the user waits forever for a fill
        /// announcement that cannot arrive, with nothing said. Let the exception out and the
        /// poller classifies it. A third provider implementing this from the stale doc would
        /// have reintroduced exactly that.
        /// </para>
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
