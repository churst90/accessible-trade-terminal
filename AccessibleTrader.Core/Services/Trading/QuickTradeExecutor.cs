using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>
    /// Turns a committed quick trade into a real order.
    ///
    /// <para>
    /// Deliberately a separate class from <see cref="QuickTradeService"/>. That one does arithmetic
    /// and speech and must stay reachable from a unit test without any possibility of touching a
    /// broker; this one does nothing but forward. The seam is the event between them, which is also
    /// what lets the whole arming workflow be tested by asserting on a published event rather than
    /// by mocking an exchange.
    /// </para>
    ///
    /// <para>
    /// <b>The stop travels with the entry, always.</b> A quick trade's entire premise is that the
    /// size was derived from the stop distance — a position placed without the stop attached would
    /// have a size justified by a protective order that does not exist. So the stop is set on the
    /// signal itself, which routes it through the provider's native bracket support where there is
    /// one (Alpaca submits entry and stop as a single order) and through
    /// <c>GeneralOrderService</c>'s protective-order verification net where there is not.
    /// </para>
    /// </summary>
    public sealed class QuickTradeExecutor : IDisposable
    {
        private readonly IEventBus _eventBus;
        private readonly IOrderExecutionService _orders;
        private readonly IWorkspaceStore _store;
        private readonly ILogger<QuickTradeExecutor> _logger;
        private readonly IDisposable _sub;

        public QuickTradeExecutor(IEventBus eventBus, IOrderExecutionService orders,
            IWorkspaceStore store, ILogger<QuickTradeExecutor> logger)
        {
            _eventBus = eventBus;
            _orders = orders;
            _store = store;
            _logger = logger;

            _sub = _eventBus.Subscribe<QuickTradeRequestedEvent>(OnRequested);
        }

        private async void OnRequested(QuickTradeRequestedEvent e)
        {
            try
            {
                string provider = _store.State.Identity.Provider ?? "";
                if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(e.Symbol))
                {
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error,
                        "No trading provider for this chart — the quick trade was not placed.", true));
                    return;
                }

                var signal = new TradeSignal(
                    Symbol: e.Symbol,
                    Side: e.IsLong ? OrderSide.Buy : OrderSide.Sell,
                    Quantity: e.Quantity,
                    Type: e.EntryPrice.HasValue ? OrderType.Limit : OrderType.Market,
                    Price: e.EntryPrice,
                    StopLoss: e.StopPrice);

                string result = await _orders.PlaceOrderAsync(provider, signal).ConfigureAwait(false);

                // ── Read the answer ──────────────────────────────────────────────
                //
                // PlaceOrderAsync returns a status string, and this call used to DISCARD it. Every
                // refusal — no price for the symbol, not enough paper balance, a quantity past the
                // sanity ceiling — came back here and was dropped on the floor. The user had already
                // been told the order was sent, so the result was the worst combination available:
                // a confirmed order, no fill, no position, and nothing said. Exactly what the catch
                // block below was written to prevent, defeated by a return value nobody looked at.
                string? failure = OrderResult.DescribeFailure(result);
                if (failure != null)
                {
                    _logger.LogWarning("Quick trade for {Symbol} was not placed: {Result}", e.Symbol, result);
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error, failure, true));
                }
            }
            catch (Exception ex)
            {
                // Never let a quick trade throw into the event bus. The speech has already told the
                // user the order was sent, so a silent failure here would be the worst possible
                // combination — a confirmed order that never existed.
                _logger.LogError(ex, "Quick trade failed for {Symbol}.", e.Symbol);
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error,
                    $"The quick trade for {e.Symbol} failed to place. Check your open orders.", true));
            }
        }

        public void Dispose() => _sub.Dispose();
    }
}
