using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>What a stop or target edit did, in the words the user hears.</summary>
    /// <param name="Ok">False when the position is left with less protection than the user asked for.</param>
    public sealed record ProtectiveEditResult(bool Ok, string Message);

    /// <summary>
    /// Moves a position's stop loss or take profit: place the new order, cancel the old one, and
    /// make sure the OTHER one is still there afterwards.
    ///
    /// <para>
    /// ── Why the other leg is checked ──────────────────────────────────────────────
    /// A stop and target placed together are linked so that one cancels the other: the paper
    /// broker does it with an OCO group, and venues with native OCO do it on the exchange. That
    /// link does not know the difference between "the stop filled" and "the user moved the stop",
    /// so cancelling the old stop took the take profit with it. Found 2026-11-05 on a paper
    /// position opened with both: after a stop edit only the new stop was resting, and nothing
    /// said the target had gone. The venue is not asked whether it will do this; the open orders
    /// are read after the cancel, and a missing leg is placed again or reported.
    /// </para>
    ///
    /// <para>
    /// ── Place, THEN cancel ─────────────────────────────────────────────────────────
    /// Only retire the old protection once the new protection exists: a refused replacement then
    /// leaves the old order in place and says so. Two reduce-only stops are self-correcting
    /// (whichever fires first closes the position); zero stops are silent.
    /// </para>
    /// </summary>
    public static class ProtectiveOrderEditor
    {
        /// <summary>The order that IS this level: reduce-only, opposite side, whole position.</summary>
        /// <remarks>
        /// The level is a TRIGGER, not a bracket. <c>GeneralOrderService.NormaliseTrigger</c> copies
        /// it into StopLoss/TakeProfit for the venues that read only those fields.
        /// </remarks>
        public static TradeSignal SignalFor(string symbol, bool isLong, double quantity,
                                            ProtectiveLevel level, double price, string? clientOid = null) =>
            new(Symbol: symbol,
                Side: isLong ? OrderSide.Sell : OrderSide.Buy,
                Quantity: Math.Abs(quantity),
                Type: level == ProtectiveLevel.StopLoss ? OrderType.StopMarket : OrderType.TakeProfitMarket,
                Price: null,
                TriggerPrice: price,
                ReduceOnly: true,   // protective: never capability-gated (DashboardCapabilityGatingTests)
                ClientOid: clientOid);

        public static string NameOf(ProtectiveLevel level) =>
            level == ProtectiveLevel.StopLoss ? "stop loss" : "take profit";

        /// <param name="existing">The order being replaced, or null when the level is being added.</param>
        /// <param name="otherLeg">The position's other protective order (the target when the stop
        /// is edited), or null when it has none.</param>
        /// <param name="successMessage">What to say when everything went to plan.</param>
        public static async Task<ProtectiveEditResult> ReplaceAsync(
            IOrderExecutionService orders, string provider, string symbol, bool isLong, double quantity,
            ProtectiveLevel level, double price, OpenOrder? existing, OpenOrder? otherLeg, string successMessage)
        {
            string name = NameOf(level);
            var placement = await orders.PlaceOrderAsync(provider, SignalFor(symbol, isLong, quantity, level, price))
                .ConfigureAwait(false);

            // Say what still stands, not only what failed: after a refusal the user needs to know
            // whether they are protected, and "order rejected" alone does not answer that.
            if (!placement.Succeeded)
                return new(false, placement.FailureMessage + (existing != null
                    ? $" Your existing {name} at {SpeechPriceFormatter.FormatPrice(existing.Price)} is still in place."
                    : $" This position has no {name}."));

            if (existing == null) return new(true, successMessage);

            if (!await orders.CancelOrderAsync(provider, existing.Id, symbol).ConfigureAwait(false))
                return new(false, $"{successMessage} But the old {name} at {SpeechPriceFormatter.FormatPrice(existing.Price)} "
                                + "could not be cancelled, so both are resting. Cancel the old one on the Orders tab.");

            if (otherLeg == null) return new(true, successMessage);

            var other = level == ProtectiveLevel.StopLoss ? ProtectiveLevel.TakeProfit : ProtectiveLevel.StopLoss;
            string otherName = NameOf(other);
            string otherAt = SpeechPriceFormatter.FormatPrice(otherLeg.Price);

            var open = await orders.GetOpenOrdersAsync(provider, symbol).ConfigureAwait(false);
            if (!open.IsOk || open.Value == null)
                return new(false, $"{successMessage} Could not check that your {otherName} at {otherAt} is still resting. "
                                + "Check the Orders tab.");
            if (open.Value.Any(o => o.Id == otherLeg.Id))
                return new(true, successMessage);

            // An explicit client id: this is a deliberate second placement of an order that may
            // have been sent seconds ago, not the double-fire the duplicate gate exists to stop.
            string oid = "atc-" + Guid.NewGuid().ToString("N").Substring(0, 16);
            var again = await orders.PlaceOrderAsync(provider,
                SignalFor(symbol, isLong, quantity, other, otherLeg.Price, oid)).ConfigureAwait(false);
            return again.Succeeded
                ? new(true, $"{successMessage} Your {otherName} at {otherAt} was cancelled with the old {name}, "
                          + "and has been placed again.")
                : new(false, $"{successMessage} Your {otherName} at {otherAt} was cancelled with the old {name}, "
                           + $"and could not be placed again: {again.FailureMessage} This position has no {otherName} now.");
        }
    }
}
