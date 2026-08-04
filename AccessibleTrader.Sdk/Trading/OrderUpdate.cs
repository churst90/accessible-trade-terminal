using System;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Sdk.Trading;

public enum OrderStatus { PartialFill, Filled, Cancelled, Rejected, Triggered }

public record OrderUpdate(
    string OrderId,
    string Symbol,
    OrderSide Side,
    double FilledQuantity,
    double FilledPrice,
    double RemainingQuantity,
    OrderStatus Status,
    bool StopTriggered,
    bool TakeProfitTriggered,
    DateTime Timestamp,
    // Realized profit/loss for the closed portion of this fill, in the quote
    // currency. Null when the fill opens/adds to a position (nothing realized)
    // or when the provider doesn't report it. Consumed by the speech layer to
    // announce "Profit/Loss X" on a closing fill.
    double? RealizedPnL = null,
    // True when this fill came from a trailing stop / trailing take-profit, so
    // the speech layer can announce "Trailing stop hit" / "Trailing take profit
    // hit" instead of the fixed-level wording.
    bool Trailing = false,
    // Why a Rejected or Cancelled update happened, in words meant to be spoken.
    // An order that declines to fill has to say why: a resting order that simply
    // vanishes from the book is indistinguishable from one that was never placed.
    // Null on fills and on cancels the user asked for.
    string? Reason = null
);
