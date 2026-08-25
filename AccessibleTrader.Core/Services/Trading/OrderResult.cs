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

                if (reason.Length == 0) return "Not placed: the provider rejected the order.";

                if (reason.Contains("insufficient", StringComparison.OrdinalIgnoreCase))
                    return "Not placed: that position costs more than the account holds. "
                         + "A risk-based size grows as the stop gets tighter, so choose a stop further away.";

                if (reason.Contains("no live price", StringComparison.OrdinalIgnoreCase)
                 || reason.Contains("no price available", StringComparison.OrdinalIgnoreCase))
                    return "Not placed: " + reason + ".";

                return "Not placed: " + reason + ".";
            }

            // ORDER_UNCERTAIN:{id} — the submit threw AND a matching order was found on
            // the exchange afterwards. The switch below fell through to its default arm
            // ("an order id — it went"), so the one code that means "verify before
            // retrying" was the one code every caller treated as a clean success and
            // said nothing about. It is NOT phrased as "not placed": the order most
            // likely IS live, and telling a user it failed is how the same position gets
            // opened twice.
            if (result.StartsWith("ORDER_UNCERTAIN", StringComparison.OrdinalIgnoreCase))
            {
                int idAt = result.IndexOf(':');
                string id = idAt >= 0 ? result[(idAt + 1)..].Trim() : "";
                return "Uncertain: the order was sent, the reply was lost, and a matching order "
                     + (id.Length > 0 ? $"({id}) " : "")
                     + "was found on the exchange. Check your open orders and positions before placing it again.";
            }

            // The PROVIDER_NOT_* family, which carries its reason the same way. These used to be
            // bare codes and now arrive as CODE:reason, so both shapes are handled — a build where
            // one side has been updated and the other has not must still say something usable.
            if (result.StartsWith("PROVIDER_NOT", StringComparison.OrdinalIgnoreCase))
            {
                int colon = result.IndexOf(':');
                string reason = colon >= 0 ? result[(colon + 1)..].Trim() : "";
                return reason.Length > 0
                    ? "Not placed: " + reason + "."
                    : "Not placed: that provider is not available for trading.";
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

        /// <summary>
        /// <see cref="DescribeFailure"/> for a caller that has ALREADY decided this is a failure and
        /// is appending the result to a sentence of its own ("Close failed for BTC/USD. …").
        /// Never null and never empty: a caller in that position must not be left announcing a
        /// dangling half-sentence just because a code arrived with no reason attached.
        /// </summary>
        public static string DescribeFailureOrDefault(string? result) =>
            DescribeFailure(result) ?? "No reason was given.";
    }
}
