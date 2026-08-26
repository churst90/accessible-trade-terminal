using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Core.Services
{
    public interface IOrderExecutionService
    {
        // ── Order execution ────────────────────────────────────────────────────

        /// <summary>
        /// Submits an order and says what became of it.
        ///
        /// <para>
        /// Returns a typed <see cref="OrderPlacement"/>, never a status string. The provider-level
        /// <c>ITradingProvider.PlaceOrderAsync</c> still answers in the documented string protocol
        /// (31 implementations depend on it); this method is where that string is recognised, once,
        /// so that no caller above it has to guess which prefixes mean failure. Three callers used
        /// to guess, they disagreed, and each disagreement was something a blind trader heard as a
        /// confirmation of an order nobody sent.
        /// </para>
        ///
        /// <para>
        /// <b>Callers: branch on <see cref="OrderPlacement.Succeeded"/>, and handle
        /// <see cref="OrderPlacement.NeedsVerification"/> separately from a refusal.</b>
        /// </para>
        /// </summary>
        Task<OrderPlacement> PlaceOrderAsync(string provider, TradeSignal signal);
        Task<bool>   CancelOrderAsync(string provider, string orderId, string symbol);

        /// <summary>
        /// True when a LINKED one-cancels-other pair can actually be placed on the
        /// effective broker: the paper simulator (terminal-enforced pairing) or an
        /// exchange implementing <see cref="Sdk.Plugins.IOcoTradingProvider"/>
        /// (exchange-enforced). Providers that merely declare the OCO capability
        /// flag without either mechanism return false — the UI must not offer a
        /// pairing promise nobody keeps.
        /// </summary>
        Task<bool> SupportsOcoPairsAsync(string provider);

        /// <summary>
        /// Places a same-side/same-quantity OCO pair: LIMIT at
        /// <paramref name="limitPrice"/> + STOP triggered at
        /// <paramref name="stopTriggerPrice"/>. Native on exchanges that support
        /// it; terminal-grouped on paper (with rollback if the second leg fails —
        /// never half a pair). Returns (success, spoken-ready message).
        /// </summary>
        Task<(bool Ok, string Message)> PlaceOcoPairAsync(string provider, string symbol,
            Sdk.Plugins.OrderSide side, double quantity, double limitPrice, double stopTriggerPrice);

        // ── Account data (requires ITradingProvider) ───────────────────────────
        /// <summary>All asset balances for the given provider, or why they could not be read.</summary>
        Task<ProviderResult<List<Balance>>>   GetBalancesAsync(string provider);

        /// <summary>
        /// Open positions, or WHY there are none — a spot-only venue has no positions
        /// concept, which differs from "you have none" and differs again from a failed
        /// fetch. Callers that write state from this MUST refuse on anything but Ok.
        /// </summary>
        Task<ProviderResult<List<Position>>>  GetPositionsAsync(string provider);

        /// <summary>Open orders, optionally filtered by symbol, or why they could not be read.</summary>
        Task<ProviderResult<List<OpenOrder>>> GetOpenOrdersAsync(string provider, string? symbol = null);

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

        /// <summary>Recent filled trades for the account, or why they could not be read.</summary>
        Task<ProviderResult<List<TradeFill>>> GetFillsAsync(string provider, string? symbol = null, int limit = 50);

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
