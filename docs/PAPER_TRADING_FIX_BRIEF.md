# Implementation brief — the four paper-trading ship-blockers

Written 2026-08-21 so the fix session starts from line-level facts instead of re-reading
`PaperTradingProvider.cs` (942 lines) from scratch. Findings and evidence live in
[TODO.md](TODO.md) under *Ship-blockers — paper trading money math*; this file is the **how**.

Everything below was re-verified against the code on 2026-08-21. Line numbers are pre-fix and
will drift as soon as fix 1 lands — the anchors are the method names.

All four live in `AccessibleTrader.Core/Services/PaperTradingProvider.cs`. Paper trading is the
hosted product: every logged-in web user touches this code.

**Do them in the order below.** Fix 1 moves the settlement contract that fixes 2–4 all sit on, and
fix 3 restructures the block fix 4 patches. Out of order means doing the same merge twice.

---

## Fix 1 — the taker fee is spent outside the affordability check

`CanFill:672` tests `_cash + s.CashDelta + 1e-9 >= 0`. `RecordFill:744` then does `_cash -= fee`
with no check. Cash 100,000, market buy 1,000 @ 100 → `CanFill` passes at exactly 0, the 40 fee
drives `_cash` to **−40**, and every later buy or short fails `CanFill` forever. The account is
bricked and `GetBalancesAsync` reports a negative balance.

The comment at `:664-670` is why nobody saw this: *"There is exactly one way to be unable to settle:
free cash would go negative."* The fee is the second way. **Verify against code, not the comment.**

### The change

`Settle()` returns `Settlement(ClosingQty, OpeningQty, CashDelta, CollateralDelta, WasShort)` at
`:654-658`. Charge the fee there, so one number is both checked and applied:

1. At the end of `Settle`, before constructing the record: `cash -= qty * price * FeeRate;`
   — unconditionally, both directions. A closing fill costs the fee too.
2. Add `double Fee` to the `Settlement` record so the history line can still report it.
3. In `RecordFill`, **delete `_cash -= fee`**. Keep computing `fee` for the `TradeFill` history
   entry only, with a comment saying the charge already happened in `Settle`.

`ApplyFill:713` already calls `Settle` with the identical arguments and comments that it is *"the
SAME settlement the affordability check used, so the two can never disagree"* — that invariant is
what makes this a three-line fix. Do not add a second fee subtraction anywhere.

### Expected fallout

Any existing test that sizes an order to exactly the free cash now correctly gets **rejected**.
That is the fix working, not a regression. Check `PaperTradingProviderTests` for max-size cases and
re-baseline them.

### The liquidation path — decide deliberately

`LiquidateIfCollateralExhausted:459-507` calls `ApplyFill`/`RecordFill` with **no `CanFill` at
all**. After this fix the buy-back's fee flows through `Settle` and can still push `_cash` negative.

That is arguably correct — the doc comment at `:462-468` is explicitly about ruin being reachable
(*"a paper account that lets you short without ever being bought in … teaches that shorting is free
money with no ruin risk, which is the opposite of the truth"*). But a negative balance is a
**permanent brick** given `CanFill`'s test. Recommendation: allow liquidation to be the one path
that may leave negative cash, comment it as intentional, and confirm the account has a working
reset before shipping it. If no reset exists, clamp `_cash` at 0 on the liquidation path instead.

---

## Fix 2 — a stop on the wrong side of the market mints money

`Crossed:509-528` is direction-blind for stops:

```csharp
OrderType.StopMarket or OrderType.StopLimit
    => o.Side == OrderSide.Buy ? bar.High >= o.Trigger : bar.Low <= o.Trigger,
```

and the fill price at `:438` is `o.Trigger ?? o.Price ?? bar.Close`. Nothing on the resting path
validates the trigger against the live price — `ProtectiveLevelValidator`
(`Core/Services/Trading/ProtectiveLevelValidator.cs`) is only reached when *editing* a protective
level on an existing position, and `GeneralOrderService` validates `Price` but never
`TriggerPrice`. Place a buy stop at 50 with the market at 100 → it fills at 50 next bar. Risk-free
50%. The sell mirror shorts at an above-market trigger.

### The change — do both halves

**Placement guard (primary).** In the resting branch of `PlaceOrderAsync`, after the
`price == null && trigger == null` check at `:315`, for `StopMarket`/`StopLimit`, compare `trigger`
to `PriceFor(symbol, 0)` and reject a buy stop at or below last price, a sell stop at or above it.
Read `ProtectiveLevelValidator` first — it already encodes this rule in spoken-word form; call it
rather than re-deriving the message. Return `ORDER_FAILED:` with that text so it reaches speech.

**Fill-price safety net (defence in depth).** At `:438`, a stop that is already crossed when it is
evaluated must not be rewarded with its trigger price. Fill at the worse of trigger and the bar's
open:

```csharp
double px = o.IsStop && o.Trigger is double t
    ? (o.Side == OrderSide.Buy ? Math.Max(t, bar.Open) : Math.Min(t, bar.Open))
    : o.Trigger ?? o.Price ?? bar.Close;
```

This also models gap-through slippage honestly, which the current code does not. Confirm `Ohlcv`
exposes `Open`.

### The twin — this is the real lesson

The class is **"a resting order fills at its stated level with no regard for where the market
actually is."** The stop case mints money; the limit case is the mirror and loses it. `Crossed:520`:

```csharp
OrderType.Limit => o.Side == OrderSide.Buy ? bar.Low <= o.Price : bar.High >= o.Price,
```

A buy limit at 150 with the market at 100 crosses immediately and fills at **150** — the user is
charged 50 above market for an order that should have filled at 100. Same shape, opposite sign, one
line away, and it was not in the audit's critical list. Fix both in this pass, and grep every other
resting-fill implementation for the same pattern before closing the item.

---

## Fix 3 — protective legs attach only to MARKET entries

The entire bracket block sits inside `if (signal.Type == OrderType.Market)` at `:264-304`. The
resting branch (`:306-324`) reads `StopLoss` only as a *trigger fallback* for a `StopMarket` type;
for `OrderType.Limit` the switch at `:312` is `=> signal.Price`, so `StopLoss`, `TakeProfit`,
`TrailStopMode/Value` and `TrailTpMode/Value/Activation` are all silently discarded.

This is the **documented primary quick-trade workflow**. `QuickTradeExecutor:64-66` builds exactly
`Type: Limit, Price: entry, StopLoss: stop` on `Shift+Enter`, and the position size was computed
*from the stop distance*. So the flagship "stop first, then entry" flow places a stop-derived size
with no stop — in `RiskAtStop` sizing that is often several times the account.
`QuickTradeExecutor`'s own comment says *"The stop travels with the entry, always."*

Mitigating but not sufficient: `VerifyProtectiveOrdersAsync` speaks a High-severity "no stop loss or
take profit found" after `ProtectionVerifyDelay` — but only after the user has already heard
"Limit buy sent".

### The change — this is a restructure, not a patch

1. **Extract** the bracket block at `:286-300` into
   `AttachProtectiveLegs(string symbol, OrderSide entrySide, double qty, double entryPx, BracketSpec spec, string entryOrderId)`.
   Preserve the OCO grouping exactly as-is — the comment at `:280-285` documents a real bug that
   grouping fixed (surviving leg outliving its position), so the group id must still be
   `spec.OcoGroupId ?? "bracket-" + entryOrderId`.
2. **Add** `private sealed record BracketSpec(double? StopLoss, double? TakeProfit, TrailMode? TrailStopMode, double? TrailStopValue, TrailMode? TrailTpMode, double? TrailTpValue, double? TrailTpActivation, string? OcoGroupId)`
   and a nullable `BracketSpec? Bracket` property on `PaperOrder` (`:872-900`).
3. **Populate** it in the resting branch when an *opening* order carries any protective field, and
   pass it through the `PaperOrder` constructor at `:321`.
4. **Fire** it in `ProcessBar` right after `RecordFill:452`, for the just-filled order:
   `if (o.Bracket != null) AttachProtectiveLegs(o.Symbol, o.Side, filledQty, px, o.Bracket, o.Id);`
   Note the anchor for a trailing stop is now the **fill** price, not the signal price — which is
   what `:290` already does for market entries (`trailAnchor: px`).
5. **Persist** it. `PaperDto` (`:902`) must round-trip `Bracket` or every pending bracket is lost on
   restart — which would be a *new* silent-unprotected-position bug wearing the old one's clothes.

### The `StopLoss` ambiguity — resolve it explicitly

The trigger switch at `:308-314` currently reads `signal.StopLoss` as the *entry trigger* for a
`StopMarket`. Once `StopLoss` can also mean "protective leg", that is ambiguous. Rule to implement:
`StopLoss` is the entry trigger only when `TriggerPrice` is null **and** the order is an opening
stop-entry; otherwise it is a protective leg. Better still, make `QuickTradeExecutor` set
`TriggerPrice` explicitly for stop entries so the fallback is never load-bearing.

---

## Fix 4 — bracket legs are not reduce-only

The comment at `:279` says *"(reduce-only by nature here)"* — true before 2.3.0, false now that
`Settle` turns a sell-with-no-position into a collateralised short. Close a bracketed long from the
dashboard's Close button (which does not cancel the legs); the stop later fires a sell with no
position and **opens a short**, cancels the target via `CancelOcoSiblings`, and announces "Stop loss
hit" for a stop that opened a trade. Partial closes are worse — the legs still carry the *original*
quantity.

This is the exact defect the OCO comment at `:282-285` claims to have fixed, arriving through a
different door. `TradeSignal.ReduceOnly` exists in the SDK and `PaperTradingProvider` never reads it.

### The change

1. Add `bool ReduceOnly` to `PaperOrder` (`:872-900`) and to `PaperDto`.
2. Set it `true` on **every** leg produced by `AttachProtectiveLegs`, and honour `signal.ReduceOnly`
   in the resting branch for caller-placed orders.
3. In `ProcessBar`, after `_open.Remove(o)` at `:437` and **before** `CanFill`:
   - look up the current position quantity for `o.Symbol`;
   - if `o.ReduceOnly` and the position is flat, or its sign means this order would *open* rather
     than reduce → `Emit(..., OrderStatus.Cancelled, reason: "the position was already closed")`
     and `continue`. Do not fill.
   - otherwise clamp the fill quantity to `Math.Min(o.Quantity, Math.Abs(positionQty))`. This is
     what fixes partial closes.
4. Use the clamped quantity consistently in `CanFill`, `ApplyFill`, `Emit` and `RecordFill` — a
   clamp applied to only some of the four is how this class of bug reproduces itself.

### The twin sweep — mandatory before closing this item

Per TODO.md `:762` and `:778`: *"Same defect class as the paper broker's bracket bug above — four
venues, one shape."* Oanda has the mirror-image defect at `:127-132`. Grep `ReduceOnly` across
`Plugins/Providers/` and fix **every** venue that builds bracket legs without it. Fixing only the
paper broker is precisely the recurrence pattern the audit named as its most important finding.

---

## Tests

The audit's meta-finding is that instance-specific tests do not stop this class of bug — the
assertions have to be **structural**. Write the last one first; it is the one that holds.

| Test | Guards |
|---|---|
| `MaximalMarketBuy_LeavesCashNonNegative_AfterFee` | fix 1 |
| `RejectsOrderWhoseFeeAloneWouldOverdraw` | fix 1 |
| `Liquidation_DoesNotBrickAccountForFutureOrders` | fix 1, the decision above |
| `BuyStopBelowMarket_IsRejectedAtPlacement` | fix 2 |
| `BuyStopAlreadyCrossed_FillsAtBarOpenNotTrigger` | fix 2 |
| `BuyLimitAboveMarket_DoesNotFillAboveMarket` | fix 2, **the twin** |
| `LimitEntryWithStopLoss_AttachesStopOnFill` | fix 3 |
| `StopEntryWithTakeProfit_AttachesTargetOnFill` | fix 3 |
| `PendingBracketSurvivesPersistRoundTrip` | fix 3 step 5 |
| `BracketLegAfterManualClose_IsCancelledNotFilled` | fix 4 |
| `PartialClose_ClampsLegQuantityToRemaining` | fix 4 |
| **`EveryProtectiveLegIsReduceOnly`** | **structural** — enumerate every leg `AttachProtectiveLegs` can emit and assert all carry `ReduceOnly`, so a leg type added later cannot ship unprotected |

Run `dotnet test --filter PaperTrading` while iterating, then the full suite (3314 tests) before
committing — fix 1 changes a contract that money-path tests elsewhere lean on.

---

## Standing context

- **The meta-finding.** Defects here get fixed at the site where they were reported and not at the
  structurally identical sites elsewhere. After "how do I fix this", the second question is
  **"what is the class, and where else does it live?"** — a grep, not a design review. Fixes 2 and 4
  above each ship with a named sweep for exactly this reason.
- **Comments in this repo are unusually good, which makes the drifted ones dangerous.** Three of
  these four are invisible on review because a confident comment directly above asserts the
  opposite (`:664-670`, `:279`, `:282-285`). Verify against code, never against the comment.
- Full findings, including the 12 other subsections, are in
  [TODO.md](TODO.md) → *Production-readiness audit (2026-08-21)*.
