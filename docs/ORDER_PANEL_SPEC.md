# Accessible Order Panel — Design Spec

**Status:** Partially implemented (updated 2026-06-25). **Shipped:** labels/ARIA
account tabs with full columns, large BUY/SELL buttons, all order types
(Market/Limit/Stop-Market/Stop-Limit/TP-Market/TP-Limit) + Trigger Price,
close/flatten button, risk-based sizing, **trailing stop** and **trailing
take-profit** (activation), time-in-force, post-only, reduce-only, position-side,
realized-P&L + trailing-hit announcements (paper), capability-gated trailing
controls (`GetCapabilitiesAsync` → `ProviderCapabilities`), a **History tab** (paper
fill log with realized P&L + a simulated 0.04% taker fee), and a **pre-submit
spoken review + Confirm/Cancel** for live orders. **Remaining:** real-provider fills/
fees/realized-P&L (paper-backed today); alerts→speech verification; Alt+B order-book
smart announcements; spot-side reduce-only/position-side (futures-only today). The
full target design follows; treat unshipped items as the roadmap. Written 2026-06-24.

Related: [[trading-order-panel-gaps]] memory, `docs/codebase-audit-2026-06` items
(order-fill announcements, bracket atomicity, TP-ladder).

---

## 1. Goal

Turn the order ticket (`AccessibleTrader.BlazorClient.Components/TradingDashboardModal.razor`)
into a professional, **fully accessible** order panel: every field carries a real
`<label>`, only controls the active provider actually supports are shown, fees use
real fee data when the provider exposes it, positions/closed-trades/working-orders
each live under their own clickable tab, and **every order event is spoken aloud the
moment it happens** — including realized profit or loss when a position closes.

The work is deliberately phased (Section 11) because it spans the UI, the SDK
trading contract, the order service, the feedback coordinator, and all 14 trading
provider plugins, and because order-flow changes must be verified against a running
build before shipping.

## 2. What already exists (baseline)

From the current code — much of the plumbing is present and merely unsurfaced:

- **`OrderType`** (`AccessibleTrader.Sdk/Plugins/ITradingProvider.cs`) already defines
  `Market, Limit, StopMarket, StopLimit, TakeProfitMarket, TakeProfitLimit`. The UI
  dropdown only offers Market and Limit.
- **`ProviderCapabilities`** (`AccessibleTrader.Sdk/Enums/ProviderCapabilities.cs`)
  already flags `L2, Shorting, OCO, TrailingStop, MarketDepth, Leverage, Brackets`.
  The UI does not yet gate on these.
- **`TradeSignal`** carries `Type, Price, StopLoss, TakeProfit, Leverage, SubType,
  MarginType`. No trailing, trigger price, time-in-force, reduce-only, post-only, or
  position-side fields.
- **`Position`** carries `AveragePrice, MarketValue, UnrealizedPnL, Leverage,
  LiquidationPrice`. The UI shows only symbol, quantity, unrealized P&L.
- **`OpenOrder`** carries `Side, Type, Quantity, Price, Status, StopLoss, TakeProfit`.
- **`TradeFill`** already carries a real `Fee` and `FilledAt`. There is **no**
  `ITradingProvider` method that returns fills/closed trades, so there is no
  closed-positions/history source today.
- **`OrderUpdate`** (`AccessibleTrader.Sdk/Trading/OrderUpdate.cs`) carries
  `FilledQuantity, FilledPrice, RemainingQuantity, Status, StopTriggered,
  TakeProfitTriggered`. No fee and **no realized P&L** — needed to announce profit
  or loss on close.
- **Existing order speech** (`AccessibleTrader.Core/Services/Accessibility/AccessibilityFeedbackCoordinator.cs`):
  "Order filled…", "Partial fill…", "Stop loss hit…", "Take profit hit…", "Order
  rejected for {symbol}." Earcon-first, interrupts, never gated, mirrored to Journal.
  Missing: order-placed-OK, order-canceled-OK, position-closed-with-P&L, trailing-hit.

## 3. Design principles

1. **Label everything.** Every control has a visible `<label for=…>` and, where the
   visible text is terse, an `aria-label`. No control relies on placeholder text or
   visual position alone for its meaning.
2. **Capability-gated.** A field appears only when the active provider advertises the
   matching `ProviderCapabilities` flag (or `ITradingProvider` boolean). A spot-only
   provider never renders leverage, margin, reduce-only, or trailing controls.
3. **Speak every order event** the instant it occurs — earcon then interrupting
   speech, never gated by speech/sonification/playback toggles — and mirror it to the
   Journal. This already holds for fills; extend it to placed/canceled/closed/trailing.
4. **Spoken pre-submit review for live orders.** Replace the silent submit with a
   spoken summary plus an explicit confirm step when the active profile is Live.
5. **Real fees when available, estimated otherwise, never invented.** Use
   `TradeFill.Fee`; pre-trade, use the provider's maker/taker rate if exposed; if no
   fee data exists, say nothing about fees rather than guess.

## 4. Order ticket — field spec

Each row: visible label → control → options/format → validation → capability gate →
`TradeSignal` mapping. Fields render top-to-bottom in this Tab order.

| # | Label | Control | Options / format | Validation | Gated by | Maps to |
|---|-------|---------|------------------|------------|----------|---------|
| 1 | Side | Buy/Sell toggle buttons | Buy, Sell | one required | always | `Side` |
| 2 | Position side | segmented / dropdown | One-way, Long, Short | hedge mode only | `Shorting` + futures | new `PositionSide` |
| 3 | Order type | dropdown | Market, Limit, Stop-Market, Stop-Limit, Take-Profit-Market, Take-Profit-Limit | required | always (subset by cap) | `Type` |
| 4 | Trigger price | number | absolute price | required for Stop*/TP* types | type-dependent | new `TriggerPrice` |
| 5 | Limit price | number | absolute price | required for Limit/Stop-Limit/TP-Limit | type-dependent | `Price` |
| 6 | Quantity | number + unit toggle | base units / quote (notional) | finite, >0, ≤ max | always | `Quantity` (convert notional→base) |
| 7 | Size as % of balance | slider/number | 0–100% | optional | always | computes `Quantity` |
| 8 | Risk-based size | number (% equity) | derive qty from stop distance | needs Stop Loss set | always | computes `Quantity` |
| 9 | Stop Loss | number + basis toggle | price / % / offset | optional | `Brackets` or `TrailingStop` | `StopLoss` |
| 10 | Trailing stop | enable + trail input | amount / % / callback-rate / ATR-mult | optional | `TrailingStop` | new `TrailingStop*` |
| 11 | Take Profit | number + basis toggle | price / % / offset | optional | `Brackets` | `TakeProfit` |
| 12 | Trailing take-profit | enable + trail input | amount / % / callback-rate | optional | `TrailingStop` (+TP) | new `TrailingTakeProfit*` |
| 13 | Time in force | dropdown | GTC, IOC, FOK, Day, GTD(+expiry) | required for Limit | new `Tif` cap | new `TimeInForce` |
| 14 | Post-only | checkbox | maker-only | Limit only | new `PostOnly` cap | new `PostOnly` |
| 15 | Reduce-only | checkbox | — | futures only | `Leverage`/futures | new `ReduceOnly` |
| 16 | Margin type | dropdown | Cross, Isolated | — | `Leverage` | `MarginType` |
| 17 | Leverage | number | 1…MaxLeverage | within bounds | `Leverage` | `Leverage` |
| 18 | Estimate (read-only) | spoken + text | est. cost, fee, margin, liq. price | — | when data available | — |
| 19 | Submit | button | "Submit Buy Order" / "Submit Sell Order" | enabled when valid | always | calls `PlaceOrderAsync` |

Notes:
- Items 7 and 8 are **accessibility sizing helpers** — they compute `Quantity`; only
  the resolved base quantity is sent. Risk-based size = `(equity × risk%) ÷ |entry −
  stop|`, rounded to the symbol's lot step.
- Item 18 is a read-only preview that is **spoken on demand and as part of the
  pre-submit review** (Section 7).

## 5. Data-model changes

**`TradeSignal`** — add: `OrderType` already sufficient; add `double? TriggerPrice`,
`string? PositionSide`, `string? TimeInForce`, `DateTime? GoodTillDate`,
`bool PostOnly = false`, `bool ReduceOnly = false`, and a trailing block —
`TrailMode? TrailStopMode`, `double? TrailStopValue`, `TrailMode? TrailTpMode`,
`double? TrailTpValue` (where `enum TrailMode { Amount, Percent, CallbackRate, Atr }`).

**`OrderUpdate`** — add `double Fee`, `double? RealizedPnL`, `string? PnLCurrency`,
and `bool TrailingTriggered` (distinct from `StopTriggered`). RealizedPnL is required
to announce profit/loss on close.

**`ITradingProvider`** — add:
- `ProviderCapabilities Capabilities { get; }` (so the UI can gate generically rather
  than via the three existing booleans).
- `Task<List<TradeFill>> GetTradeHistoryAsync(string? symbol = null, int limit = 100);`
  — source for the Closed/History tab and realized-P&L pairing.
- `Task<(double maker, double taker)?> GetFeeRatesAsync(string symbol);` — pre-trade
  fee estimate; returns null when the exchange does not expose it.
- `Task<bool> ClosePositionAsync(string symbol, double? quantity = null);` — close or
  partially close (null = full flatten). Default implementation may submit an opposing
  reduce-only market order.

**New record** (optional, if exchanges give round-trip P&L directly):
`record ClosedTrade(string Symbol, OrderSide Side, double Quantity, double EntryPrice,
double ExitPrice, double RealizedPnL, double Fee, DateTime ClosedAt);`

## 6. Provider capability model & per-provider matrix

The panel reads `Capabilities` (Section 5) and shows fields accordingly. Add three
new flags to `ProviderCapabilities`: `ReduceOnly = 1 << 7`, `PostOnly = 1 << 8`,
`TimeInForce = 1 << 9`. Each of the 14 trading providers must (a) declare its true
capability set and (b) implement the mapping for any field it advertises. Fill in:

| Provider | Leverage | Trailing | OCO/Brackets | ReduceOnly | PostOnly | TIF | Fee rates | Trade history |
|----------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| Binance (spot/futures) | ? | ? | ? | ? | ? | ? | ? | ? |
| Bitstamp | … | | | | | | | |
| Coinbase | … | | | | | | | |
| Alpaca | … | | | | | | | |
| Kraken | … | | | | | | | |
| Oanda | … | | | | | | | |
| Polygon (data only) | n/a | | | | | | | |
| Tradier | … | | | | | | | |
| TwelveData (data) | n/a | | | | | | | |
| InteractiveBrokers | … | | | | | | | |
| FMP (data) | n/a | | | | | | | |
| Schwab | … | | | | | | | |
| MEXC (spot/futures) | … | | | | | | | |
| Finnhub (data) | n/a | | | | | | | |

A provider that advertises a capability it cannot actually fulfil must fail loudly at
order time, not silently drop the field.

## 7. Pre-submit spoken review (live safety)

When the active profile is **Live**, Submit does not fire immediately. It first speaks
a one-line summary and waits for an explicit confirm (Enter / a "Confirm" button;
Escape cancels):

> "Confirm: Buy 0.5 BTCUSDT, market. Stop 42,180, target 43,400. Estimated cost
> 21,250 USDT, fee 8.50, liquidation 40,900. Press Enter to confirm or Escape to
> cancel."

Paper profiles may submit directly (configurable). The estimate line omits any value
the provider cannot supply rather than printing zeros.

## 8. Account panel — tabs

Three (plus Balances) clickable tabs; selecting a tab shows that table only. Each
table has real column headers (`<th scope="col">`), each row is focusable with an
`aria-label` summarizing it, and per-row actions are real buttons. Switching tabs
speaks the tab name and row count, e.g. "Positions, 2 open." Tables update live where
the provider streams; otherwise a Refresh button is offered.

- **Positions** (open): Symbol, Quantity, Avg price, Mark/Market value, Unrealized
  P&L, Leverage, Liq. price. Row aria-label: "BTCUSDT, long 0.5, avg 42,500,
  unrealized profit 320." Per-row **Close** button (and **Close half**).
- **History** (closed trades): Symbol, Side, Quantity, Entry, Exit, Realized P&L,
  Fee, Closed-at. Sourced from `GetTradeHistoryAsync`. Row aria-label: "BTCUSDT, sold
  0.5, profit 640, closed 14:32."
- **Orders** (working/unfilled): Symbol, Side, Type, Quantity, Price/Trigger, Status,
  attached SL/TP. Per-row **Cancel** button. Row aria-label: "Limit buy 0.5 BTCUSDT
  at 42,500, working."

## 9. Spoken announcements — exact strings

All are earcon-first, interrupting, never gated, and mirrored to the Journal. Braces
are runtime values. Order IDs are never spoken. P&L is spoken as the word "Profit" or
"Loss" followed by the absolute amount and currency, optionally the percent.

| Event | Announcement |
|-------|--------------|
| Order placed (market) | "Order placed. {Buy\|Sell} {qty} {symbol}, market." |
| Order placed (limit) | "Order placed. {Buy\|Sell} {qty} {symbol}, limit {price}." |
| Order placed (stop) | "Order placed. {Buy\|Sell} {qty} {symbol}, stop {trigger}." |
| Partial fill | "Partial fill. {Bought\|Sold} {qty} {symbol} at {price}. {remaining} remaining." |
| Order filled | "Order filled. {Bought\|Sold} {qty} {symbol} at {price}." |
| Order canceled | "Order canceled. {Buy\|Sell} {qty} {symbol}." |
| Order rejected | "Order rejected for {symbol}.{ reason}" |
| Stop-loss hit | "Stop loss hit. Sold {qty} {symbol} at {price}. {Profit\|Loss} {amount} {ccy}." |
| Take-profit hit | "Take profit hit. Sold {qty} {symbol} at {price}. {Profit\|Loss} {amount} {ccy}." |
| Trailing stop hit | "Trailing stop hit. Sold {qty} {symbol} at {price}. {Profit\|Loss} {amount} {ccy}." |
| Trailing TP hit | "Trailing take profit hit. Sold {qty} {symbol} at {price}. {Profit\|Loss} {amount} {ccy}." |
| Position closed | "Position closed. {symbol}. {Profit\|Loss} {amount} {ccy}{, {pct} percent}." |
| Leverage set | "Leverage set to {n} times for {symbol}." |

P&L computation: prefer the exchange's reported realized P&L (`OrderUpdate.RealizedPnL`
/ `ClosedTrade.RealizedPnL`); otherwise compute `(exit − entry) × qty × dir` and
subtract known fees. Never announce a P&L the data cannot support — fall back to the
fill-only string (current behavior) if realized P&L is unavailable.

## 10. Alerts spoken aloud

Price and indicator alerts must speak on fire through the same always-on channel (not
only appear in the Journal). Target string: "Alert: {message}" with an alert earcon.
Verify the alerts subsystem (`Alt+J`, `IAlertChannel`/`IAlertOrchestrator`) routes a
fired alert to `AccessibilityFeedbackCoordinator`; if it only journals today, wire the
speech path. (Confirm against current code — see open questions.)

## 11. Implementation phases

Each phase is independently shippable and testable against a running build.

1. **Surface + label (UI-only, low risk).** Expose the existing `OrderType` options;
   show `AveragePrice`/`MarketValue`/`LiquidationPrice` columns; add `<label>`/aria to
   every existing field; split the account area into Positions / Orders tabs with
   accessible tables and per-row Cancel. No model change.
2. **Capability gating.** Add `Capabilities` to `ITradingProvider`; gate every field;
   declare each provider's real capability set.
3. **Announcements.** Add order-placed / canceled / position-closed speech; add `Fee`
   and `RealizedPnL` to `OrderUpdate`; compute/relay P&L; confirm alert speech.
4. **History tab + fees.** Add `GetTradeHistoryAsync` + `GetFeeRatesAsync`; build the
   History tab; show real fees; pre-submit estimate line.
5. **Close/flatten + risk sizing.** `ClosePositionAsync` + per-row Close; size-as-%
   and risk-based sizing helpers.
6. **Trailing + TIF + flags.** Extend `TradeSignal`; add trailing/TIF/post-only/
   reduce-only controls and per-provider mapping; pre-submit spoken review for live.

## 12. Open questions

- Does any provider return round-trip realized P&L directly, or must we always pair
  fills to compute it? (Affects whether `ClosedTrade` or fill-pairing is the source.)
- Do fired alerts currently reach the speech router, or only the Journal? (Section 10.)
- Is "bracket atomicity" from the June audit the 2-second post-submit verification
  scan, or was true OCO intended? (Section 9 P&L + Brackets capability depend on this.)
- Per-exchange trailing semantics differ (callback-rate vs absolute trail vs activation
  price); the `TrailMode` enum must cover each provider's real parameterization.
