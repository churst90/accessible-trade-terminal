using System;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Sdk.Trading;

/// <summary>
/// What a venue said happened to an order. Every member is CONSUMED by
/// GeneralOrderService.PublishOrderEvent — an exhaustive switch with a logged
/// default — so a status a provider produces can never be silently discarded.
/// (The old <c>Triggered</c> member was exactly that: four providers used it as
/// their fallback arm and the order service had no case for it, so every venue
/// status those providers didn't recognise vanished — no event, no log. The
/// trigger fact lives in <see cref="OrderUpdate.StopTriggered"/> /
/// <see cref="OrderUpdate.TakeProfitTriggered"/>, never in this enum.)
///
/// Mapping rules for provider authors:
/// - <c>New</c>: the venue accepted the order and it is (or is about to be)
///   working — "new", "accepted", "pending_new", a stop that just triggered
///   into the book. Logged, not announced: placement was already announced.
/// - <c>Expired</c>: the order left the book because its time-in-force ran out
///   (IOC/FOK remainder, day-order at the close). NOT <c>Cancelled</c> (nobody
///   asked for it) and NOT <c>Rejected</c> (the venue did accept it).
/// - <c>Replaced</c>: the order was modified and is STILL LIVE under a new id.
///   Mapping this to <c>Cancelled</c> tells the trader they are flat while the
///   order still rests — they re-enter and are double-sized.
/// - <c>Unknown</c>: anything you don't recognise. Put the raw venue word in
///   <see cref="OrderUpdate.Reason"/> so the log names it.
/// A partially-filled-then-terminated order maps to its terminal state
/// (<c>Cancelled</c>/<c>Expired</c>) with <see cref="OrderUpdate.FilledQuantity"/>
/// carrying the executed part — the announcement speaks the fill.
/// </summary>
public enum OrderStatus { PartialFill, Filled, Cancelled, Rejected, New, Expired, Replaced, Unknown }

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
