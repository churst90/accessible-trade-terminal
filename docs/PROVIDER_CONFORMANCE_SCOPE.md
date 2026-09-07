# The provider layer — why it keeps breaking, and the one thing that would stop it

**Written 2026-09-07** after a day in which five defects were found in the provider/order path,
three of them by pointing the app at a real venue for the first time. Every line below is either
measured or cites the file it was read from, so a later reader can check rather than trust.

**The question this answers, in Cody's words:** *"I'm confused as to whether we've got systematic
issues with the way we interface with providers/orderbooks/trading… I can't tell if it is patch
work or legitimate fixes at the source."*

**The short answer: the bugs are systematic, the fixes have been per-bug, and the gap between
those two facts is this document.**

---

## 1. The evidence that it is systematic

This is not a new pattern. `AccessibleTrader.Tests/OrderContractShipBlockerTests.cs` records four
earlier ones in its own header:

- **IBKR** reused the conId of the *charted* symbol for any order — chart AAPL, order MSFT from
  the panel, buy AAPL, real order id returned.
- **MEXC** turned a protective stop into an immediate market order, and mapped side as
  `Buy ? 1 : 3` without reading `ReduceOnly`, so a sell-to-close opened an opposing short.
- **Tradier** truncated quantity with `(int)`: 9.7 shares placed 9, 0.6 placed 0.
- **Schwab** discarded the `Location` header carrying the only copy of the order id and returned
  the literal `"ORDER_SUBMITTED"`, which prefix-matches the error sentinel. Every fill was silent.

And on 2026-09-06/07:

- The stop **trigger** travelled in a field half the fleet does not read.
- The **environment** field reached no plugin at all; then reached them in four different
  vocabularies; **Tradier's could never match, so every Tradier profile signed against the live
  broker**, and Binance read a different key nobody supplied, so a Paper Binance profile signed
  against **live Binance**.
- `SupportsOrderEventStreaming` could not distinguish *"this venue has no stream"* from *"the
  stream is not up yet"*, and a fix written that morning got it wrong in both directions within
  four hours.

**Same class every time: the wrong value in the wrong field of an outgoing request, failing
silently.** Nine instances across eight venues is not eight coincidences.

## 2. The root cause, precisely

Three properties of the SDK contract, each of which lets a plugin be wrong without anything
noticing.

| Weakness | What it produced |
|---|---|
| `TradeSignal` has **two** fields that can mean "the trigger" (`TriggerPrice`, `StopLoss`) | Kraken and Coinbase read one, Gemini and Binance the other; a caller picking either is wrong somewhere |
| `Configure(Dictionary<string,string>)` is **stringly-typed with no documented keys** | four vocabularies for "practice mode"; two of them failed toward real money |
| `SupportsOrderEventStreaming` conflates a **static** capability with a **live** connection state | a venue written off permanently, or a dead feed announced forever |

And the reason none of it was caught: **there is no shared test that every plugin must pass.**
Coverage is per-plugin and written *after* each bug. Five of twelve trading plugins — Binance,
Coinbase, Bitstamp, Oanda, InteractiveBrokers — have no test file at all.

## 3. What was changed at the source on 2026-09-07 (done)

These are contract fixes, not patches: one canonical answer that every caller inherits.

- **`GeneralOrderService.NormaliseTrigger`** — the single chokepoint every order passes through
  now fills in whichever trigger spelling is missing, so all twelve plugins receive the same
  number however the caller wrote it. Never invents a trigger, never overwrites one, identity on
  entries.
- **`DataService.CredentialFor`** — one builder for a credential. `Environment` normalised to
  exactly `Live` or `Paper` (never the empty string a legacy profile holds), `Testnet` derived
  from it, **fail-safe polarity**: anything not explicitly `Live` is practice.
- **`ProviderConfigKeys` (new, SDK)** — the dictionary keys and their two legal environment
  values, named and documented, with `IsLive()` so a plugin cannot get the polarity backwards.
- **`ITradingProvider.ProvidesOrderStream` (new, SDK)** — the STATIC "this venue has no push
  channel", separate from the dynamic `SupportsOrderEventStreaming`. Declared `false` on Gemini,
  Kraken Futures and Schwab. The headless watch now says the limitation once instead of
  escalating it forever, and no longer writes off a venue whose socket is merely not up yet.
- **`TradeSignal` is now documented** where the ambiguity lives: what `StopLoss`/`TakeProfit` mean
  on an entry versus on a stop order, and the instruction that a plugin author should read
  `TriggerPrice ?? StopLoss`.

**One thing in this list is still patchwork and is called out as such:** Tradier was taught to
accept `"Paper"`. That fixes Tradier; the contract fix is `ProviderConfigKeys`, and no plugin yet
uses it.

## 4. The thing that would actually stop this — a provider conformance suite

**This is the recommended next piece of work, and it needs no keys, no accounts, no VPN.**

Every defect above except one is a **payload-construction** bug: the wrong value in the wrong
field of an HTTP request. Those are catchable with a fake `HttpMessageHandler` that captures the
outgoing request and asserts its body — no venue involved.

One suite, ~30 assertions, run as a `[Theory]` across all twelve plugins:

| Assertion | The bug it would have caught |
|---|---|
| Stop order with only `TriggerPrice` → body carries a stop price | the trigger disagreement |
| Stop order with only `StopLoss` → the same body | ditto, other direction |
| `Configure` with `Environment=Paper` → base URL is the practice host | Tradier, Binance, Gemini |
| `Configure` with `Environment=Live` → base URL is the live host | the polarity |
| Quantity `0.6` → body says `0.6` | Tradier's `(int)` truncation |
| Reduce-only sell → the venue's reduce-only flag is set | MEXC's hedge-mode reversal |
| Order id round-trips out of the response | Schwab's `Location` header |
| Symbol in the body is the ORDER's, not the chart's | IBKR's conId reuse |
| Stop order type is a stop, not a market | MEXC's type 5 |

**Coverage estimate: eight of the nine known defects.** The ninth (the order stream) needs a real
venue, and no reachable venue provides one — see §5.

Two supporting pieces worth building with it:

1. **Golden request files.** Capture each plugin's outgoing body once, review it against the
   venue's published docs, and commit it. A diff then means "the payload changed", which is
   exactly the event nobody currently notices.
2. **Use the venues' own dry-run endpoints.** Kraken spot's `AddOrder` takes `validate=true` —
   it checks a real order against the real venue and never places it. Binance offers
   `/api/v3/order/test`. **No plugin in this repo uses either**, and they are the only way to
   validate order construction against a live venue with no risk and no sandbox.

## 5. Test environments, measured 2026-09-07 from a US connection

| Venue | Practice environment | Status |
|---|---|---|
| Gemini | `api.sandbox.gemini.com` | ✅ working, key stored. Spot only; refuses `StopMarket`/`TakeProfit` by name; **no order stream**; sandbox book is empty and its candles are flat synthetic bars |
| Alpaca | `paper-api.alpaca.markets` | ✅ working, key stored. Stream capability is **dynamic** (false until its socket connects) |
| Tradier | `sandbox.tradier.com` | ✅ reachable, free signup. **Only usable at all as of today's fix** |
| Coinbase | `api-public.sandbox.exchange.coinbase.com` | ✅ reachable |
| OANDA | `api-fxpractice.oanda.com` | ✅ reachable (401 = needs a key) |
| Kraken **Futures** | `demo-futures.kraken.com` | ✅ full demo environment |
| Kraken **spot** | UAT **by request only** — contact their API team | ❌ not self-serve. Use `validate=true` instead |
| Binance | futures testnet API answers, but **account creation is geo-blocked** to binance.us, which has no futures testnet | ❌ unavailable without a VPN; registering from a restricted location is a terms problem, not a technical one |

**The consequence worth carrying forward: no venue reachable from this machine delivers a live
order stream.** That is the foundation of background monitor Phases 1–3 (`HeadlessSession`,
`CircuitOrderCoverage`, the re-subscribe-after-a-dead-socket logic) and it has never been
exercised against a real one. It is the largest measured hole in the codebase.

**Best available second venue: Kraken Futures demo.** It is the only self-serve environment left
that offers futures, shorts, leverage, stop-market orders and a real order stream — every gap
Gemini and Alpaca could not reach.

## 6. Suggested order of work

1. **The conformance suite** (§4). No credentials. Retires eight of nine known defect classes and
   covers the five plugins with no tests at all.
2. **Adopt `ProviderConfigKeys` across the twelve plugins.** Mechanical, and it turns the Tradier
   patch into a contract.
3. **Wire the dry-run endpoints** (`validate=true`, `/order/test`) into the conformance run for
   the venues that have them — real venue, no order, no sandbox.
4. **Kraken Futures demo**, for the order stream and the margin path.
5. Only then, background monitor Phase 3.

## 7. What this document does NOT claim

- The conformance suite does not exist yet. Section 4 is a design, not a record.
- No plugin has been read line-by-line against its venue's published API docs. The nine defects
  were found by measurement and by accident, not by an audit, so **the true count is unknown and
  is certainly higher.**
- Nothing has FILLED at a real venue. Gemini's sandbox book is empty; Alpaca was not driven to a
  fill.
- `AAPL 1h → 0 bars` from Alpaca on 2026-09-07 is unexplained — probably the request shape or
  market hours, but it was not chased and should not be assumed benign.
