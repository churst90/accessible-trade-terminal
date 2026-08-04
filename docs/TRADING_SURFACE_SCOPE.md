# Trading surface — capability audit and scope

Written 2026-08-04, after a hands-on paper-trading session that found nine defects in one evening.
**Research and scope only. Nothing here has been built.**

The question behind it: what would it take for the terminal's trading surface to be as capable as the
exchanges it connects to, and to be *honest* about what it can and cannot do on each one.

---

## 0. The principle this is designed around

> *"We shouldn't be forcing the user to adhere to good trading practices if they don't want to, only
> advise passively, or maybe not at all… we shouldn't get in the way of the trader."*

Adopted, and it has a sharp edge worth stating: **a terminal that refuses a legal order because it
disapproves is broken.** Advice is fine when it is passive and one sentence. Refusal is reserved for
things that are *impossible* (no price, no connection) or *incoherent* (a stop that would fire on
submission).

This already resolved one live disagreement. Quick trade sized positions by *risk at the stop*, so
moving the stop changed the position size — defensible risk management, and not what was asked for:

> *"When I set 0.5% of the account, I'm simply just quickly setting my position size, that's it.
> Where I put my stop shouldn't have anything to do with changing that amount."*

**Settled: position value is the default sizing mode** (commit `f0165a87`). The percentage buys that
much, the stop protects it, and the two are independent. Risk-at-stop remains available for anyone who
wants it. This is the pattern for everything below — the exchange's behaviour is the default, our
opinions are opt-in.

---

## 1. Where we actually are

### 1.1 The contract is far ahead of the implementations

`TradeSignal` already carries **23 fields**: leverage, margin type, position side (hedge mode),
reduce-only, post-only, time-in-force, trigger price, trailing stop *and* trailing take-profit with
three trail modes, OCO group id, and a spot/futures sub-type router.

That is a genuinely well-designed order contract. **The gap is not the contract — it is that almost
nothing consumes it.**

### 1.2 Capability flags exist and are almost entirely unused

`ProviderCapabilities` has 7 flags. Consulted in the UI:

| Flag | Used in any `.razor` |
|---|---|
| TrailingStop | **1** |
| L2, Shorting, OCO, MarketDepth, Leverage, Brackets | **0** |

So the dashboard shows the same controls for every provider. On Coinbase (spot, no leverage, no OCO)
the user sees the same surface as on Binance Futures. **This is the "buttons that do what they say"
problem in its most concrete form**, and it is the single highest-value fix in this document because
it is cheap and it removes a whole class of silent disappointment.

### 1.3 Declared capabilities per provider, as the code stands

| Provider | Declared | Margin | Futures | Notes from the source |
|---|---|---|---|---|
| **Binance** | L2, MarketDepth, TrailingStop, +more | — | **yes** | Fullest implementation, 1,089 lines |
| **Kraken** | L2, MarketDepth, Leverage, + | — | no | Leverage declared, `SetLeverageAsync` real |
| **Mexc** | L2, MarketDepth, Leverage | — | **yes** | Leverage declared, real setter |
| **InteractiveBrokers** | L2, Shorting, Leverage | — | **yes** | Real setter |
| **Oanda** | Leverage, Shorting, TrailingStop | — | no | Real setter; FX margin model |
| **Alpaca** | L2, Brackets | — | no | `SetLeverageAsync` is a no-op |
| **Schwab** | Brackets | false | false | `SetLeverageAsync` no-op |
| **Tradier** | Brackets | false | false | `SetLeverageAsync` no-op |
| **Coinbase** | L2 | — | no | `GetPositionsAsync` returns empty; leverage no-op |
| **Bitstamp** | L2, MarketDepth | — | no | `GetPositionsAsync` returns empty; leverage no-op |

Data-only (no `ITradingProvider`): Finnhub, FMP, Polygon, TwelveData.

**Caveat, stated plainly:** this table is what the *code declares*, read from source. It is not a
verified statement about what each venue's API offers — see §5.

### 1.4 The paper broker is spot-only, and says otherwise

`PaperTradingProvider` declares `Leverage | Brackets | Shorting | OCO`. Its own comment is honest:

> *"leverage is recorded and reported but not used"*

So it stores a leverage number, reports it back on positions, and funds every position from cash at
1×. A `$134,000` notional on a `$100,000` account is refused rather than opened on 2× margin. The
capability flag is a lie the code tells about itself, and it is exactly the kind of thing §1.2's fix
would surface.

---

## 2. Scope: leverage and perpetuals

### 2.1 The three instrument models, and why they are not one feature

**Spot.** You exchange one asset for another. Position = what you hold. P&L in the quote. This is all
we model today.

**Linear perpetual** (`BTCUSDT` perp, USDT-margined). Notional in the base, margin and P&L in the
quote. Position can exceed cash by the leverage multiple. Introduces: margin requirement, maintenance
margin, liquidation price, funding payments every 8h, and reduce-only orders.

**Inverse perpetual** (`BTCUSD` perp, BTC-margined). **P&L is denominated in the base asset.** Every
formula inverts: contracts are quoted in USD but settled in BTC, so `PnL = contracts × (1/entry −
1/exit)`. It is not a variation on linear — it is a different arithmetic, and a codebase that
retrofits it into linear code will get signs and units wrong in ways that only show up with money on
the line.

### 2.2 What the paper broker needs

This is the honest core of the estimate, because paper is where it must work first.

1. **Margin accounting.** Cash becomes: free margin, used margin, maintenance margin. A position
   consumes `notional / leverage` rather than `notional`.
2. **Liquidation.** Price at which used margin is exhausted. Must be computed per position, reported
   on `Position.LiquidationPrice` (the field exists and is always `0` today), and *enforced* — a paper
   account that never liquidates teaches a lesson that will be expensive later.
3. **Isolated vs cross.** `MarginType` is already on the signal and ignored. Isolated caps loss at the
   position's margin; cross draws on the whole balance. Different liquidation maths.
4. **Funding.** Perps pay/charge every 8 hours. Modelling it matters for anything held overnight, and
   ignoring it silently overstates every long in a positive-funding regime.
5. **Hedge mode.** `PositionSide` is on the signal and ignored. Some venues let you hold long and
   short in one symbol simultaneously.
6. **Inverse settlement.** Base-asset P&L, as above.

**Estimate:** items 1–3 are the bulk and are tractable — call it a solid piece of work with a
substantial test suite, since every number here is one that decides money. Items 4–6 are each
independently sized and can follow. **Item 6 (inverse) should be last and should be its own class,
not a flag on the existing one.**

### 2.3 What the contract needs

Mostly nothing — which is the good news. `TradeSignal` already carries leverage, margin type,
position side and reduce-only. The gaps are on the *reporting* side:

```csharp
// Position gains:
double MarginUsed          // what this position ties up
double MaintenanceMargin   // where liquidation starts approaching
string MarginMode          // "Isolated" / "Cross"
double FundingPaid         // cumulative, for perps

// Balance gains (or a new AccountSummary record):
double FreeMargin
double UsedMargin
double MarginLevel         // equity / used margin — the number that predicts a margin call
```

Plus `ProviderCapabilities` flags that do not exist yet: `LinearPerpetual`, `InversePerpetual`,
`HedgeMode`, `IsolatedMargin`, `Funding`, `ReduceOnly`, `PostOnly`, `TimeInForce`.

**This is a breaking change to a plugin contract**, so it wants a single deliberate pass across all
ten trading plugins rather than a drip. Default-valued record parameters keep it source-compatible.

---

## 3. Scope: making the dashboard tell the truth

This is the cheapest high-value work in the document.

**The rule:** every control the dashboard renders must correspond to a capability the connected
provider actually has, and where a capability is missing the control should be *absent or disabled
with a reason* — never present and inert.

Concretely:
- Leverage selector: only when `Leverage` is declared **and** `MaxLeverage > 1`.
- OCO panel: only when `SupportsOcoPairsAsync` is true.
- Trailing fields: already gated (the one that works) — keep as the model.
- Short side: disable the sell side into a short when `Shorting` is absent and there is nothing to
  sell.
- Reduce-only / post-only / TIF: only where declared.
- Margin mode: only where isolated/cross is real.
- Positions tab: on a spot-only provider, positions *are* balances; say so rather than showing an
  empty table (this is Coinbase and Bitstamp today, and an empty Positions tab reads as a bug).

**And a self-audit worth having:** a test that fails when the dashboard references a capability flag
no provider declares, or renders a control for a capability it never checks. The same shape as
`EventSubscriberRegistrationTests` — it catches the class, not the instance.

---

## 4. Scope: "every button does what it says"

The session's defects were all of one family — something existed, looked right, and did nothing:

- a key bound to a chord no keyboard can produce (shifted digits)
- a binding spelled differently on each side of the comparison (`ENTER` / `RETURN`)
- a checkbox wired to a code path that ignored it (level earcons)
- a return value nobody read (`ORDER_FAILED`)
- **a service never registered, so its events went nowhere** (`QuickTradeExecutor` — the whole quick
  trade feature, which had never placed an order)
- a component never placed on a page (the `PAPER` badge)

Three of the six are now guarded by tests that check the *class* of defect. The remaining audit
worth doing, in one deliberate pass:

1. **Every button in the trading dashboard** — click it, confirm the effect reaches a provider.
2. **Every declared capability** — exercise it against paper and one live sandbox.
3. **Every `SystemCommand`** — `ShortcutReachabilityTests` proves they resolve; nothing proves the
   dispatcher branch does anything. A "every command has a handler" test is straightforward and would
   have caught the executor bug from a different angle.

---

## 5. Per-provider research — what still needs verifying

**This section is deliberately unfinished, and I want to be clear about why.** Writing down what each
exchange's API offers from memory would produce a confident, plausible, partly-wrong document — and a
wrong capability table is worse than none, because it would be used to plan work. The repository's own
research discipline applies here: *never reconstruct facts you can fetch.*

What each entry needs before it is worth trusting: current API docs read, endpoints named, and the
delta against our implementation listed.

| Provider | What to check |
|---|---|
| **Binance** | Spot vs USD-M vs COIN-M split; which our plugin talks to; hedge mode; funding endpoints |
| **Mexc** | Futures API separate from spot; leverage and margin-mode endpoints; contract specs |
| **Kraken** | Kraken Futures is a distinct API from spot; margin trading on spot is a third thing |
| **Coinbase** | Advanced Trade vs the old Pro API; whether perps are reachable on the key type we use |
| **Interactive Brokers** | Enormous surface — options, futures, FX, bonds; margin model; what the gateway exposes |
| **Oanda** | FX margin; trailing stop; the `units` model (signed, no separate side) |
| **Alpaca** | Options now available; crypto; fractional shares; PDT rules; bracket + OTO/OCO classes |
| **Schwab / Tradier** | Options chains and multi-leg; both are equity/options brokers, not crypto |
| **Bitstamp** | Confirm read-only in practice, or wire up its trading endpoints |

### 5.1 Corrected priority — and why it matters more than it looks

An earlier draft of this document recommended starting with **Mexc and Binance**. That was wrong, and
wrong in an instructive way. The maintainer is **US-based and uses neither**:

- **Binance** — not available to US retail (Binance.US is a separate, much smaller venue).
- **Mexc** — *"their platform is inaccessible, which is what this terminal addresses."* Worth sitting
  with: the exchange whose API is most capable is the one whose UI made the terminal necessary. The
  API is still worth supporting; it just is not where verification starts.

**The real priority list, for a US trader:**

| Order | Provider | Why |
|---|---|---|
| 1 | **Alpaca** | Already verified end-to-end against paper this cycle; equities + crypto + options |
| 2 | **Coinbase** | US crypto; currently our weakest plugin (`GetPositionsAsync` returns empty) |
| 3 | **Kraken** | US crypto with real margin; already declares `Leverage` with a working setter |
| 4 | **Schwab / Tradier** | US equities and options |
| 5 | **Interactive Brokers** | The widest surface — futures, FX, options, bonds |

### 5.2 The constraint that reshapes the leverage work

**For a US trader, most crypto leverage is simply not reachable**, and this is a structural fact
rather than an integration gap. Offshore perpetual venues do not serve US retail. So the leverage that
is actually available here comes from:

- **Equities margin** — Reg T territory: roughly 2:1 overnight, more intraday for pattern-day-trader
  accounts. Alpaca, Schwab, Tradier, IBKR.
- **Futures** — CME and friends, via IBKR. Genuine leverage, entirely different contract model.
- **Crypto margin where a US venue offers it** — Kraken, for eligible customers.

**This inverts §2's ordering.** Inverse perpetuals were scoped as the hardest and last piece; for this
maintainer they may be *unreachable in practice* and worth deferring indefinitely. The work that pays
is **equities margin (buying power, Reg T, PDT rules) and futures contract handling** — which the
document had not scoped at all, because it was written assuming crypto perps.

**Verify before building.** Margin rules are regulatory, they change, and they differ per broker and
per account type. Do not encode a number like "2:1" from this document — read the broker's current
docs and, better, ask the account's own buying-power endpoint, which every one of these brokers
exposes and which is authoritative in a way a hardcoded rule never is.

---

## 5.3 Decided: leverage is in scope for paper AND live

Confirmed by the maintainer. Two consequences worth writing down now:

**The spot/futures switch must be visible where the trade is made.** Exchanges put a market-type
selector right on the ticket, and `TradeSignal.SubType` already exists to carry it (`"Spot"` /
`"Futures"`) — it is simply never set from the dashboard. This is the same shape as every other
finding here: the contract can express it, nothing offers it.

It belongs beside the symbol, not buried in settings, because it changes what every other control on
the ticket means: leverage, margin mode, reduce-only and hedge-mode position side are all
futures-only, and the same symbol can price differently on the two books. It should only appear where
the provider actually has both.

**Live leverage raises the verification bar.** Paper margin arithmetic being wrong costs a lesson;
live margin arithmetic being wrong costs the account, and liquidation is the specific number that must
never be optimistic. Recommendation: **derive buying power and margin requirement from the broker's
own account endpoint wherever one exists, rather than computing it.** Compute only where no endpoint
exists, and label those figures as estimates in the UI.

---

## 5.4 Options — flagged as genuinely interesting, not yet scoped

The maintainer wants to think about it. Recording why it is a bigger step than it looks, so the
thinking has something to push against:

An option is not an instrument with a price — it is a **contract with five coordinates**
(underlying, expiry, strike, call/put, and its multiplier, usually 100). Nothing in the terminal's
current model can express that. `ChartIdentity` carries a symbol string; `TradeSignal` carries a
symbol and a quantity. So this is not a provider feature to switch on — it is a new instrument type
running through identity, charting, the order ticket, positions and P&L.

What makes it worth doing anyway: **options are unusually well suited to an audio-first terminal.**
The decision surface is numeric and structured — strike ladders, expiry dates, greeks, spreads between
legs — and a screen-reader user navigating a chain by keyboard is arguably *better served* than
someone squinting at a grid of hundreds of cells. The chain is a table, and tables read well.

Availability on our existing plugins: **Alpaca, Schwab and Tradier** all offer options; Tradier in
particular is known for a straightforward options API. That is three US brokers already in the
codebase, which is a real head start.

Realistic first slice, if it goes ahead: **read-only chains** — fetch, navigate, and speak an option
chain with no trading at all. It would prove the identity model, the navigation and the narration
before any order path exists, and it is independently useful for a discretionary trader.

---

## 5.5 Wallets, addresses and transfers — thinking-out-loud, captured

The suggestion: a toolbar "Wallet" button opening deposit addresses when the provider is a crypto
exchange, plus an at-a-glance portfolio snapshot per provider.

The maintainer half-withdrew it — *"the balances tab kind of does this"* — and that is right for
**balances**. But two parts are genuinely missing and worth separating:

1. **Deposit addresses are not balances.** *"I'd need to get money from one wallet to the next"* is a
   real workflow the terminal cannot help with at all today. Most crypto venues expose deposit
   addresses over API.
2. **A portfolio snapshot across a provider** — total value, allocation, day change — is a different
   question from a table of per-asset rows, and it is the one you actually want at a glance.

**A security note that should gate any address work.** Displaying a deposit address makes the terminal
part of the trust path for moving money, and address substitution is the classic attack — malware
that swaps a copied address for its own. If this is built: read addresses only from the authenticated
API (never store or cache them), show the full address rather than a truncation, and **speak it in
grouped chunks** so a screen-reader user can actually verify it rather than being told a blur. Do not
add a "copy" affordance without the address being verifiable first.

**Withdrawals should stay out of scope.** An accessible terminal that can move funds off an exchange
is a much larger security surface than one that can only trade, and the value added is small — the
transfer happens once, the trading happens daily.

---

## 6. Suggested order of work

Ordered by value per unit of risk, not by size. Revised after §5.1–5.3.

1. **Capability-gated dashboard** (§3). Cheap, removes a whole class of silent disappointment, and it
   *surfaces* every lie the capability flags currently tell — including the paper broker's.
2. **Command/button audit + the "every command has a handler" test** (§4). Cheap, and this session
   proved the failure mode is real and invisible.
3. **Spot/futures market-type selector on the ticket** (§5.3). `SubType` already exists; nothing sets
   it. Small, and it is the visible half of everything below.
4. **Contract extension for margin reporting** (§2.3). One deliberate breaking pass over ten plugins.
5. **Paper margin accounting: margin, liquidation, isolated/cross** (§2.2 items 1–3). The substantial
   piece; everything downstream depends on it being right.
6. **Equities margin and buying power, Alpaca first** (§5.2). Displaced crypto perps as the leverage
   that is actually reachable from the US. Prefer the broker's own buying-power endpoint over
   computing Reg T rules ourselves.
7. **Provider verification pass** in the §5.1 order — Alpaca, Coinbase, Kraken, Schwab/Tradier, IBKR.
8. **Read-only options chains** (§5.4), if the maintainer wants to pursue it.
9. **Futures contracts via IBKR** (§5.2).
10. **Funding, hedge mode, inverse perpetuals** (§2.2 items 4–6). Deferred, possibly indefinitely —
    these serve offshore crypto perps, which a US trader largely cannot reach.

## 7. Open questions — answered 2026-08-04

1. ~~Is real-money leverage in scope?~~ **Yes, paper and live both**, with the spot/futures switch
   visible on the ticket. See §5.3.
2. ~~Which venues actually matter?~~ **US venues.** Not Binance, not Mexc. See §5.1 — and note that
   Mexc's inaccessibility is the reason this terminal exists, which makes it a target for support
   rather than for verification.
3. ~~Options?~~ **Interesting, under consideration.** Scoped enough to think against in §5.4;
   read-only chains are the recommended first slice.

Still open:

4. **Does a portfolio snapshot per provider (total value, allocation, day change) earn a place**, or
   is the Balances tab enough? §5.5.
5. **Are deposit addresses worth the trust-path cost?** §5.5 argues for addresses without withdrawals,
   and for spoken verification in grouped chunks before any copy affordance.

## Cross-references

`docs/CHANGES.md` (2026-08-04 entries) · `docs/RELEASE_2.2.0_VERIFICATION.md` ·
`AccessibleTrader.Sdk/Plugins/ITradingProvider.cs` · `AccessibleTrader.Core/Services/PaperTradingProvider.cs`
