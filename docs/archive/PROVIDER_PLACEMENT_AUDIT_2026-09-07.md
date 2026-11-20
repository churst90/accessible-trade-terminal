# Provider placement audit — every trading plugin read against the SDK contract

**Written 2026-09-07**, the same day as `PROVIDER_CONFORMANCE_SCOPE.md`, as the first step of the
work that document recommends. Twelve plugins were read line by line (three parallel readers, each
producing a line-cited fact sheet; the seams and payload shapes below were then used to build
`AccessibleTrader.Tests/ProviderOrderConformanceRigs.cs`). **Everything here is either quoted
from the code or marked UNVERIFIED.** Line numbers are as of commit `b18327f5`; they drift.

Two corrections to the scope document, found by measurement:

- **The Kraken Futures demo is DEAD.** `demo-futures.kraken.com` answers every path with a 301 to
  a Cloudflare marketing page (measured today), and the plugin itself has said so since
  `ce77da2a` (2026-08-05): a Paper profile throws `DemoDecommissionedMessage` before any request.
  The scope document's "✅ full demo environment" was wrong, and its roadmap item 4 (use the demo
  for the order stream) has nowhere to go.
- **Binance's SPOT testnet is geo-blocked from here** (`testnet.binance.vision` → HTTP 451); the
  futures testnet answers (`testnet.binancefuture.com` → 200) but account creation is blocked.

Practice hosts re-measured today: Tradier sandbox 200, Coinbase Exchange sandbox 200 (note: that is
the OLD Exchange API's sandbox and does not serve the Advanced Trade `/api/v3/brokerage/*` paths
this plugin uses — UNVERIFIED), OANDA practice 401 (= needs a key), Gemini sandbox 200, Alpaca
paper 401 (= needs a key).

## The pattern, restated with the count

| Plugin | Reads `TriggerPrice` for a stop? | Environment key | Practice host | Brackets on an entry |
|---|---|---|---|---|
| Alpaca | **no** — `StopLoss` only; a stop with no `StopLoss` is sent as `market` | `== "Live"` case-sensitive, one-way | paper-api | yes (`order_class`) |
| Binance spot | `TriggerPrice ?? StopLoss` | `Testnet` only, default LIVE | testnet.binance.vision | **dropped silently** |
| Binance futures | `TriggerPrice ?? StopLoss ?? TakeProfit` | same | testnet.binancefuture.com | separate reduce-only orders — **and a duplicate leg when both spellings are set** |
| Bitstamp | never — stop types go out as a price-less limit | none | none | none |
| Coinbase | **no** — `StopLoss` only | none | none | dropped |
| Gemini | `TriggerPrice ?? StopLoss` | `== "Paper"` (missing → LIVE) | sandbox | dropped |
| IBKR | **no** — `StopLoss` only (TP types read `TriggerPrice ?? TakeProfit`) | none (gateway login) | none | rows — **duplicate TP leg when both spellings are set** |
| Kraken spot | **never reads `TriggerPrice` anywhere** | none | none | `close[]`, Market entries only |
| Kraken Futures | `TriggerPrice`, then `StopLoss` for stop types; **never `TakeProfit`** | `== "Paper"` → refuses (demo dead) | none | dropped |
| MEXC | never (stop types refused on both branches) | none | none | futures: `stopLossPrice`/`takeProfitPrice`; spot: dropped |
| OANDA | **no** — `StopLoss` only | `== "live"` ci, both branches | fxpractice | `*OnFill` |
| Schwab | **no** — `StopLoss` only | none | none | TRIGGER + OCO children |
| Tradier | **no** — `StopLoss` only (TP types read `TriggerPrice` first) | `!= "Live"` → sandbox, one-way | sandbox | OTO/OTOCO |

**Eight of twelve read the trigger from one field only**, and the field differs by plugin. The
service-level `NormaliseTrigger` hides this from the app's own callers; any other caller (a
strategy plugin, a script, a test) hits it directly.

## Per-plugin findings

Severity words: **DEFECT** = wrong order reaches the venue or a rejection is reported as success;
**GAP** = a supported thing is refused or dropped silently; **UNVERIFIED** = plausible from the
venue's published docs, not measured.

### Alpaca (`AlpacaProvider.cs`)
- **DEFECT** `TakeProfitMarket` / `TakeProfitLimit` have no branch and fall to `type=market`
  (768–787): a take-profit becomes an immediate market order, while `SupportsTakeProfit` is true.
- **DEFECT** a `StopMarket` whose `StopLoss` is null (only `TriggerPrice` set) degrades to
  `market` — a stop that fires now. Same shape as MEXC's type-5 incident.
- `time_in_force` hard-coded `gtc`; `TimeInForce`, `PostOnly`, trailing fields unread.
- `Environment` compared case-sensitively to `"Live"` and only ever flips toward live (131–132);
  `Environment` property reports Paper regardless (77).
- Quantities below 1e-4 serialise as `"1E-05"` (measured on this box). UNVERIFIED whether Alpaca
  parses that.

### Binance (`BinanceProvider.cs`)
- **DEFECT (futures)** `slIsEntryTrigger` requires `TriggerPrice == null` (938). After
  `NormaliseTrigger` fills BOTH fields on a stop order, the entry goes out AND a second
  reduce-only `STOP_MARKET` is attached at the same price.
- **GAP** spot entries drop `StopLoss`/`TakeProfit` silently while `Capabilities` declares
  `Brackets`.
- `Environment` unread; only `Testnet` is honoured, and a missing `Testnet` means mainnet.
- Futures protective legs always send `reduceOnly=true` and never `positionSide`; the plugin's own
  comment says hedge mode rejects `reduceOnly`. UNVERIFIED.
- `SetLeverageAsync` swallows every failure to `1.0`, so any leverage-endpoint failure is spoken as
  "the account is still at 1 times".
- Spot has `/api/v3/order/test` (a real validation, nothing placed). Unused.

### Bitstamp (`BitstampProvider.cs`)
- **GAP** stop and take-profit types are not refused locally; they leave as a price-less limit
  order and the venue's error is what the user hears (655–673).
- No practice venue and no `Environment` read: a Paper profile signs against live Bitstamp.
- `client_order_id`, `ioc_order`/`fok_order` are venue-supported and unread.

### Coinbase (`CoinbaseProvider.cs`)
- **DEFECT** a rejected order is reported as submitted. Coinbase answers HTTP 200 with
  `{"success":false,"error_response":{…}}`; the plugin checks only the status (660) and turns the
  missing `success_response` into `"ORDER_SUBMITTED"` (662). The status-blind class closed
  fleet-wide on 2026-08-31, still open here.
- `StopMarket` is emulated as a stop-limit with a ±5% limit (626): a gap through the trigger of
  more than 5% never fills, silently.
- `stop_direction` is derived from `Side` alone (628, 641).
- No practice venue, host inlined nine times, `Environment` unread.
- `/api/v3/brokerage/orders/preview` exists and is unused.

### Gemini (`GeminiProvider.cs`)
- Reads `TriggerPrice ?? StopLoss` correctly (337). Refuses stop-market / take-profit by name.
- `Environment == "Paper"` → sandbox; MISSING → live (130). Wrong polarity under the contract.
- Entry `StopLoss`/`TakeProfit` dropped silently; `TakeProfit` is never read in the file.
- A failed `/v1/symbols/details` read returns increment 0 and sends an unrounded price (517).

### Interactive Brokers (`InteractiveBrokersProvider.cs`)
- **DEFECT** `tpIsEntryTrigger` requires `TriggerPrice == null` (676–677); after the normaliser
  fills both, a second `LMT` child rests at the entry's own trigger — the case the comment above
  it says must not happen.
- Stop types read `StopLoss` only (641–648); a `StopMarket` with only `TriggerPrice` leaves as
  `STP` with NO `auxPrice`.
- `tif` hard-coded `GTC`; `Limit` without a price is sent as `LMT` with no `price`.
- `ORDER_FAILED:{ex.GetType().Name}` discards the reason; `[]` → `"ORDER_SUBMITTED"`.
- Gateway confirmation prompts are auto-confirmed up to 8 deep, announced not asked.
- `/iserver/account/{id}/orders/whatif` (preview) exists and is unused.

### Kraken spot (`KrakenProvider.cs`)
- `TriggerPrice` is never read anywhere in the file. A `StopMarket` with only `TriggerPrice` is
  sent as `ordertype=stop-loss` with NO `price`.
- **GAP** `close[]` brackets are attached on Market entries only (857–866); a Limit entry with a
  stop gets no leg and no message.
- Leverage `(int)Math.Clamp(x, 2, 5)`: 1.5× becomes 2× (851).
- `timeinforce` and `oflags=post` are venue-supported and unread.
- `AddOrder validate=true` is the venue's dry run. Unused. Note a validated order returns no
  `txid`, so the current code would report it as `ORDER_SUBMITTED`.
- Test hazard: `PluginHostServices.ApiKeys`, when non-null, preempts `Configure` (1216–1224).

### Kraken Futures (`KrakenFuturesProvider.cs`)
- **DEFECT (field)** `TimeInForce == "IOC"` is sent as `triggerSignal=last` (381–383).
  `triggerSignal` is the trigger price SOURCE for stops; IOC is spelled `orderType=ioc`.
- `take_profit` orders read `TriggerPrice` only — never `TakeProfit` (369–371).
- `limitPrice` is attached whenever `Price > 0`, including on `mkt` and on a `StopMarket`
  (which then silently becomes a stop-limit, since both map to `stp`).
- `ClientOid` unread though the venue has `cliOrdId`.
- Demo venue dead (above). `Environment` branches on `"Paper"`; missing → live.

### MEXC (`MexcProvider.cs`, `MexcRestApi.cs`)
- **DEFECT (latent)** the timestamp-error retry matches `body.Contains("-1021") ||
  body.Contains("700003")` on ANY response including a 2xx (`MexcRestApi.cs:121–127`). An order
  id containing `700003` re-sends the same order.
- `Environment` unread; no practice venue; a Paper profile trades live MEXC.
- Spot drops `StopLoss`/`TakeProfit` on entries silently; the refusal text for stop TYPES says the
  levels "attach to a Futures entry" but a spot entry carrying them reports plain success.
- `price` is sent on a Market (type 5) futures order when `Price` is set (707). UNVERIFIED.
- Spot has `/api/v3/order/test` (Binance-style). Unused.

### OANDA (`OandaProvider.cs`)
- **UNVERIFIED DEFECT** `orderCancelTransaction` is never read (944–948): a 201 for an order
  created then instantly cancelled (FOK unfillable, insufficient margin) returns the create id as
  a placed order. Same class as Gemini's `is_cancelled`.
- Stop types read `StopLoss` only; `TakeProfitMarket`/`Limit` refused although the venue has
  `MARKET_IF_TOUCHED` (which the plugin already reads BACK as `TakeProfitMarket` at 1010).
- The returned id for a filled market order is the fill TRANSACTION id, not the order id.
- `ClientOid` unread (venue: `clientExtensions.id`); `TimeInForce` unread.

### Schwab (`SchwabProvider.cs`)
- **UNVERIFIED DEFECT** every bracket sends `duration: "GTC"` (969, 987). Schwab's published
  enum is `GOOD_TILL_CANCEL`; `"GTC"` is not in it. If the venue rejects the spelling, every
  bracketed Schwab order fails — and `BrokerParityTests.cs:165` PINS `"GTC"`. `/previewOrder`
  would settle this without money.
- **UNVERIFIED DEFECT** option legs carry `BUY`/`SELL` instructions; Schwab's option vocabulary
  is `BUY_TO_OPEN` etc. — the defect Tradier fixed on 2026-08-23. The option symbol is also
  passed raw where Schwab expects its padded 21-character form.
- A venue rejection's reason is discarded: the 400 body is inside the exception message but only
  `ex.GetType().Name` reaches the sentinel (729–733). The user hears "HttpRequestException".
- Stop types read `StopLoss` only; TP types refused although a resting `LIMIT` is the equity
  take-profit (Tradier does exactly this).
- No whole-share guard on quantity. UNVERIFIED what Schwab does with 9.7.

### Tradier (`TradierProvider.cs`)
- **UNVERIFIED CONTRADICTION** 1048–1051 says "Tradier REJECTS gtc on market orders" and forces
  `day`; 1140–1143 sends `duration=gtc` for every bracket including a market entry. One of the two
  is wrong. `preview=true` on the free sandbox would settle it.
- Stop types read `StopLoss` only (1076, 1080) while TP types read `TriggerPrice` first (1086).
- The base URL's DEFAULT is live and `Configure` only ever flips toward sandbox.
- `preview=true` (validation, nothing placed) and `tag` (client id) are venue-supported and unused.

## What the conformance suite asserts (see `ProviderOrderConformanceTests.cs`)

One theory per property, one row per venue rig, fourteen rigs (Binance and MEXC have a spot and a
futures branch each). A capability a venue lacks is asserted as a REFUSAL before any request, never
skipped. The properties are the nine in the scope document's §4 plus three found while reading:
"both spellings of a trigger produce ONE order", "a take-profit never leaves as a market order",
and "Configure without an Environment is practice, not live".

## First run of the suite, before any plugin was touched: 33 of 141 rows red

| Property | Red on |
|---|---|
| Stop with only `TriggerPrice` is a stop at that trigger | Alpaca (left as MARKET), Coinbase, IBKR (no `auxPrice`), Kraken (no `price`), OANDA, Schwab, Tradier (refused as unsupported) |
| Stop with only `StopLoss` is the same order | the same seven (consequence of the above) |
| Stop with BOTH spellings leaves exactly one order | Binance futures (entry + a duplicate reduce-only stop) |
| Take-profit with only `TriggerPrice` rests at the trigger | Alpaca (left as MARKET), Kraken (no `price`), OANDA and Schwab (refused) |
| Take-profit with BOTH spellings leaves exactly one order | Binance futures and IBKR (duplicate leg), Alpaca, OANDA, Schwab |
| Stop / take-profit types refused where the venue has none | Bitstamp (sent as a price-less limit, reported as placed) |
| Venue rejection is a failure, not an order id | Coinbase (HTTP 200 `success:false` → `ORDER_SUBMITTED`) |
| `Environment=Paper` signs against the practice host | Binance spot and futures (`Environment` unread) |
| `Configure` without an Environment is practice, not live | Binance ×2, Gemini, Tradier (default host is LIVE) |

Green on the first run, worth recording because they were the scope document's other predictions:
the limit payload (symbol, side, exact fractional quantity, price) on all fourteen; Tradier's
whole-share refusal; the order id round trip on all fourteen including Schwab's `Location`
header; reduce-only on Binance futures, MEXC futures and Kraken Futures; the order's symbol rather
than the chart's on all fourteen including IBKR's conId; Live routing on all fourteen; Kraken
Futures refusing a Paper credential outright.
