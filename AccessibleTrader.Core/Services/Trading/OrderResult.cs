using System;

namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>
    /// Translates the status string an order placement returns into something a person should hear.
    ///
    /// <para>
    /// <b>Why this is shared rather than private to one caller.</b> <c>PlaceOrderAsync</c> answers
    /// with a terse code — <c>ORDER_FAILED:insufficient paper balance</c>,
    /// <c>ORDER_REJECTED_QUANTITY</c> — meant for logs. Every caller has to decide what that means
    /// for the user, and a caller that forgets produces the defect this codebase has already hit
    /// once: the quick-trade executor discarded the return value entirely, so the feature announced
    /// "sent" while placing nothing, with no error anywhere. One translator, used by every path that
    /// places an order, is how that stays fixed.
    /// </para>
    /// </summary>
    public static class OrderResult
    {
        /// <summary>
        /// Turns an order status string into something worth hearing, or <c>null</c> when the order
        /// went through.
        ///
        /// <para>
        /// The codes are terse and mostly meant for logs, so each one is translated into what the
        /// user should do about it. "ORDER_FAILED:insufficient paper balance" is true but unhelpful;
        /// what a person needs to know is that a risk-based size on a tight stop asks for more
        /// notional than the account holds, and that a wider stop is the fix.
        /// </para>
        /// </summary>
        public static string? DescribeFailure(string? result)
        {
            if (string.IsNullOrWhiteSpace(result)) return "The order was not placed — the provider returned nothing.";

            if (result.StartsWith("ORDER_FAILED:", StringComparison.OrdinalIgnoreCase))
            {
                string reason = result.Substring("ORDER_FAILED:".Length).Trim();

                if (reason.Contains("insufficient", StringComparison.OrdinalIgnoreCase))
                    return "Not placed: that position costs more than the account holds. "
                         + "A risk-based size grows as the stop gets tighter, so choose a stop further away.";

                if (reason.Contains("no live price", StringComparison.OrdinalIgnoreCase))
                    return "Not placed: there is no live price for this symbol, so it cannot be filled.";

                return "Not placed: " + reason + ".";
            }

            return result switch
            {
                "ORDER_REJECTED_QUANTITY" => "Not placed: the position size is outside the allowed range.",
                "ORDER_REJECTED_PRICE"    => "Not placed: the limit price was not usable.",
                "ORDER_DUPLICATE_SUPPRESSED" => "Not placed: that looked like a duplicate of an order just sent.",
                "ORDER_FAILED"            => "Not placed: the provider rejected the order.",
                _ => null,   // an order id — it went.
            };
        }
    }
}
