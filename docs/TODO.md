# TODO — Accessible Trading Terminal

This file tracks all known bugs, improvements, and roadmap items. Items are organized by improvement-plan phase. Checked items `[x]` are confirmed complete. Open items `[ ]` are pending.

**The 2.0 plan (tiers, audit grades, what's left) lives in [ROADMAP_2.0.md](ROADMAP_2.0.md).**

---

## Production-readiness audit (2026-08-21) — findings, unfixed

A full read-through of the codebase for production readiness: every area reviewed against the
code rather than against its own comments. Everything marked **VERIFIED** was confirmed by reading
the cited lines during the audit; items without that marker are reported from the review pass and
still want a second look before anyone acts on them.

Nothing was fixed *by the audit* — it was analysis only. Fixes have landed since, in agreed
batches, and each one is marked `[x]` in place with the date and what the twin sweep turned up.
Closed so far: the four paper-trading criticals, the UI/contrast/keyboard batch, the indicator
causality contract, the `FeedbackType.Alert` / master-gain / fixed-precision-price batch, and the
two data-pipeline ship-blockers (the tab-switch tick merge and the unreachable Polly layer).

The single most useful thing this audit found is not any individual bug. It is a **pattern**: a
defect gets fixed at the site where it was reported and not at the two or three structurally
identical sites elsewhere. Nearly every critical below is the second or third instance of
something this project has already found, understood, written a good comment about, and fixed
once. The remedy is the one `0320d8a3` already articulated — *the fix for that is not more care,
it is assertions* — but the assertions have to be **structural** (enumerate every `FeedbackType`,
count `Speak` calls, sweep every price-formatting call site) rather than instance-specific.

The second theme: **comments in this codebase are unusually good, which makes the drifted ones
unusually dangerous.** Several of the criticals below are invisible on review precisely because a
confident, well-written comment directly above the defect asserts the opposite. Those are called
out individually.

### Ship-blockers — paper trading money math — **ALL FOUR FIXED 2026-08-21**

Paper trading IS the hosted product; every logged-in web user touches this code.

**Line-level fix plan for all four: [PAPER_TRADING_FIX_BRIEF.md](PAPER_TRADING_FIX_BRIEF.md)** —
required work order, the settlement-contract change fixes 2–4 depend on, the two twin defects the
audit did not list, and the structural test that stops the class from recurring.

All four are fixed, in the brief's order, each with its named twin sweep. Suite green at 3359.
The two twins the audit had not listed were real and are fixed too: a buy limit above the market
filled at the limit rather than at the market, and three backtest/research fill sites booked a
gapped-through stop at the stop it had skipped. The fill rule now lives in ONE place —
`Core/Services/Trading/BarFill.cs` — and every fill site calls it.

- [x] **FIXED — The taker fee is spent outside the affordability check, so a maximal order lands the
  account at negative cash.** VERIFIED. `PaperTradingProvider.CanFill:673` tests
  `_cash + s.CashDelta + 1e-9 >= 0`; `RecordFill:745` then does `_cash -= fee` with no check.
  Cash 100,000, market buy 1,000 units at 100 → `CanFill` passes at exactly 0, then the 40 fee
  drives `_cash` to **−40**. `GetBalancesAsync` reports a negative balance and every later buy or
  short fails `CanFill` forever — the account is bricked. `LiquidateIfCollateralExhausted` is
  worse: it calls `ApplyFill`/`RecordFill` with no `CanFill` at all, charging a fee out of free
  cash at the moment collateral is exhausted. **The comment at `:660-671` is why nobody saw this**
  — it states "There is exactly one way to be unable to settle: free cash would go negative", and
  the fee is the second way. Fix: fold the fee into `Settlement.CashDelta` so one number is both
  checked and applied.
- [x] **FIXED — A stop order on the wrong side of the market fills at its trigger, minting money from
  nothing.** VERIFIED. `Crossed:521` is direction-blind — `o.Side == OrderSide.Buy ? bar.High >=
  o.Trigger : bar.Low <= o.Trigger` — and the fill price is `o.Trigger` (`:438`). Nothing on the
  resting path validates the trigger against the live price (`ProtectiveLevelValidator` is only
  called when *editing* a protective level on an existing position; `GeneralOrderService`
  validates `Price` but never `TriggerPrice`). Place a buy stop at 50 with the market at 100 and
  it fills at 50 next bar for an instant risk-free 50%. The sell mirror shorts at an
  above-market trigger. Fix: reject a buy stop at or below last price and a sell stop at or above
  it (call the validator that already encodes this rule), or fill an already-crossed stop at the
  live price.
- [x] **FIXED — Protective legs attach only to MARKET entries; a limit or stop entry silently drops
  `StopLoss`, `TakeProfit` and both trailing configs.** VERIFIED. The whole bracket block sits
  inside `if (signal.Type == OrderType.Market)` (`:264-304`). The resting branch reads `StopLoss`
  only as a trigger fallback for a `StopMarket` type; for `OrderType.Limit` the switch is
  `=> signal.Price`, so the stop is discarded. **This is the documented primary quick-trade
  workflow**: `QuickTradeExecutor:64-66` builds exactly `Type: Limit, Price: entry, StopLoss:
  stop` when the user presses `Shift+Enter`, and the position size was computed *from the stop
  distance*. So the flagship "stop first, then entry" flow places a stop-derived size with no
  stop. Mitigating: `VerifyProtectiveOrdersAsync` does speak a High-severity "no stop loss or take
  profit found — the position may be unprotected" after `ProtectionVerifyDelay`, so it is not
  fully silent — but it arrives after the user has already heard "Limit buy sent", and in
  `RiskAtStop` sizing the unprotected position is often several times the account.
  `QuickTradeExecutor`'s own comment says "**The stop travels with the entry, always**".
- [x] **FIXED — Bracket legs are not reduce-only, so a manual close leaves orders that open a brand-new
  opposite position.** VERIFIED. The comment at `:279` says "(reduce-only by nature here)" — true
  before 2.3.0, false now that `Settle` turns a sell-with-no-position into a collateralised short.
  Close a bracketed long from the dashboard's Close button (which does not cancel the legs); the
  stop later fires a sell with no position and **opens a short**, cancels the target, and
  announces "Stop loss hit" for a stop that opened a trade. This is the exact defect the OCO
  comment at `:282-285` claims to have fixed, arriving through a different door. Partial closes
  are worse — the legs still carry the original quantity. `TradeSignal.ReduceOnly` exists in the
  SDK and `PaperTradingProvider` never reads it.

### Ship-blockers — data pipeline — **FIRST TWO FIXED 2026-08-22**

The two closed here are the tab-switch tick merge and the unreachable Polly layer. Both are guarded
by `AccessibleTrader.Tests/PipelineIdentityAndResilienceTests.cs` (19 tests), and every guard in it
was **proven to fail** by reintroducing the defect and watching it go red before being trusted —
including the provider scan, which was checked against the specific false negative that killed two
guards on 2026-08-21: the explanatory comment above each rethrow names `TransportFailure`, so a scan
that read comments would have passed on the prose describing the fix while the code did the
opposite. It strips comments and string literals before scanning, asserts it found 30+ provider
bodies (so losing its targets fails loudly instead of passing vacuously), and both behavioural
guards carry an explicit vacuity check proving they do not simply drop everything.

- [x] **FIXED — A tab switch merges the OLD symbol's live ticks into the NEW tab's buffer, and that path
  can auto-execute a strategy.** VERIFIED. `MarketOrchestrator:251` sets `_dataManager.Identity`,
  a synchronous property whose setter is `_hub.SetFocus(value)` — `_focused` swaps immediately.
  But the orchestrator's subscription is only retargeted at **Step 4** of
  `DataManager.CatchUpFromSnapshotAsync`, *after* an awaited `GapFillAsync` network round-trip.
  For that whole window the still-running pump does `_focused?.ApplyLiveTick(tick)` with the
  outgoing symbol's ticks. `ReplaceLast` corrupts the incoming tab's last bar; `Append` fabricates
  a bar at the wrong symbol's price and raises `LiveAppend` — which is the trigger
  `StrategyEngine.OnFocusedFeedUpdated` uses to evaluate a closed bar and can place a real order.
  **The comment at `MarketFeedHub:465-467` asserts the opposite of what the code does** ("a tab
  switch mid-pump must not deliver the outgoing symbol's ticks… the subscription is retargeted by
  the caller"). Fix is two lines: carry the subscribed identity on the tick and drop it when it
  does not match `_focused.Identity`.

  **Fixed 2026-08-22 as written, but as a TYPE rather than a check.** The pipeline's live channel
  now carries `Core/Models/LiveTick.cs` — `(ChartIdentity Identity, Ohlcv Bar)` — instead of a bare
  `Ohlcv`, stamped by `LiveStreamManager.SubscribeToProvider` from the subscription's OWN parameters
  (captured, not read back off the mutable `_current*` fields, so a tick arriving late from the
  outgoing socket is stamped with the outgoing identity). `MarketFeedHub`'s pump compares and drops.
  Making it a type rather than an `if` is the load-bearing part: a live bar can no longer be routed
  without saying whose it is, so the defect cannot come back through a different door.

  **The twin, one layer up, which the audit did not list.** `DataManager` captures
  `_hub.FocusedFeed`, awaits a fetch, then dispatches `UpdateDataAction` — and that action carries
  no identity, so the store cannot tell that the bars belong to the tab the user just left. The
  tab-switch `CancellationToken` closes most of the window (`ChartFeed` checks it after each fetch)
  but not the last of it. All five dispatch sites now go through `DispatchIfStillFocused`. This is
  not cosmetic: `PaperTradingProvider.OnState` reads exactly the pair (`st.Identity`, last bar of
  `st.Data`) to price positions and fill resting orders, so the previous symbol's close filed under
  the new symbol's name fills orders at a price that symbol never traded at.
- [x] **FIXED — Every Polly retry and circuit breaker in the pipeline is unreachable.** VERIFIED.
  `DataService.FetchOhlcvAsync:320-324` catches `Exception` and returns an empty tuple. That call
  is the body of `HistoricalDataFetcher`'s policy, which is the body of `DataOrchestrator`'s
  policy. No transport exception can reach either, so the retries never retry, the breakers never
  trip, `onBreak`/`onReset` never fire, and `ConnectionStatusEvent(Error)` /
  `DataTrigger.ErrorOccurred` are unreachable from network failure. The only visible failure mode
  is an empty chart. Two carefully-configured, well-commented, entirely decorative Polly stacks.

  **Fixed 2026-08-22 — and the audit understated it by a factor of thirty.** `DataService` was the
  reported site, but the sweep for "what is the class, and where else does it live?" found that
  **every one of the 31 data providers** ended its `FetchOhlcvAsync` in
  `catch (Exception) { report; return empty; }` too. Fixing only `DataService` would have left the
  policy just as unreachable and the fix just as invisible — the audit's own recurrence pattern,
  caught before it recurred.

  - **One definition of the failure class:** `Sdk/Plugins/TransportFailure.IsTransient` — HTTP with
    no status (DNS/TLS/refused), 5xx, 408, 429, socket, IO, timeout — walking inner exceptions,
    because HttpClient's own timeout arrives as a `TaskCanceledException` wrapping a
    `TimeoutException`. A 4xx is deliberately NOT transient: retrying a bad key or an unknown symbol
    cannot help, and announcing it as "network issue" names the wrong problem. Cancellation is not
    transient either — a tab switch is not a network fault. `DataOrchestrator` tests the same
    predicate, so the layer that throws and the layer that handles cannot drift apart.
  - **31 providers rethrow it and swallow the rest.** They still report on their `ErrorStream`
    first, so nothing that was audible became silent.
  - **`DataService` no longer swallows at all.** Its analytics cache read and write are guarded
    individually instead: a corrupt cache file is a LOCAL fault and must not count against a
    network breaker. `HistoricalDataFetcher` got the same treatment for its `OhlcvStore` read and
    write.
  - **One resilience stack, not two.** `HistoricalDataFetcher` had a near-duplicate retry+breaker
    INSIDE `DataOrchestrator`'s. Reviving both would have turned one failed request into four
    against a provider already in trouble — classic retry amplification. Its policy is gone; what
    it had that the orchestrator lacked moved up: `TimeoutException` in the retry set, and the
    429/rate-limit breaker (2 failures / 30 s), which is deliberately NOT wired to
    `ConnectionStatusEvent` because `GeneralOrderService` and `TradingReconciliationCoordinator`
    read `Connected` as "re-hook this broker's order stream and reconcile live positions" — a
    market-data rate limit clearing is not a broker reconnecting.
  - **The user can now HEAR it.** Nothing subscribed to `ConnectionStatusEvent` announces anything
    (both subscribers only act on `Connected`), so `onBreak` also publishes a
    `FeedbackRequestEvent`. `BrokenCircuitException` stays deliberately silent — every fetch during
    the open window lands there, and announcing each would bury the one message that mattered.
  - **Contract tests that encoded the bug were flipped, not deleted.** Six
    `HttpErrorStatus_ReturnsEmpty_NoThrow` tests asserted the swallow. They now assert the throw;
    the Fmp 403 case was renamed `HttpForbidden_ReturnsEmpty_NoThrow` and kept as the counterweight
    proving the 4xx boundary is real.
- [ ] **`OhlcvStore` can write bars that were never real, and being insert-only can never repair
  them.** VERIFIED on both halves. (a) The forming-bar filter is `ToMs(b.Date) + barMs <= nowMs`
  with `"M" => value * 2592000000L, // Approx 30 days` (`TimeframeUtility:46`), so on the 31st of a
  31-day month the *current* month's bar passes as closed and is frozen into history. (b)
  `SaveAsync:150` dedups with `known.Add(ToMs(b.Date))` — existing timestamps are skipped, never
  updated — so a wrong bar written once is served from disk forever. Partial resampled buckets
  reach the same path (`HistoricalDataFetcher:149-159` persists resampled output, and
  `ResamplerService` does not trim incomplete edge buckets), so a scrollback on a resampled
  timeframe writes bars built from a fraction of their period. Fix: use `GetPeriodStart` for the
  closed test, make `SaveAsync` an upsert, and either trim partial buckets or persist native bars
  and resample on read.
- [ ] **`EventBus` has no exception isolation between subscribers.** `Publish` is
  `GetSubject<T>().OnNext(eventData)` — synchronous, on the caller's thread. One throwing handler
  aborts delivery for every subscriber after it and propagates back into the publisher (a live-tick
  thread, a reducer, a Polly callback). The throwing observer is not removed, so it throws again on
  every subsequent publish: one buggy subscriber permanently disables an event type app-wide.
- [ ] **`DataState.LiveStreaming` is unreachable; the pipeline reports `Initializing` while
  streaming.** `StartFocusedLiveCoreAsync` begins with `StopFocusedLiveCoreAsync`, which fires
  `DataTrigger.Reset` → `Initializing`; the table then has no `(Initializing, LiveStreamStarted)`
  or `(Initializing, TickReceived)` case, so it stays there. `Stalled` and `NetworkLagged` are dead
  too, and `Faulted` is unreachable from the common failure path because of the swallow above.
  Blast radius is limited only because `CurrentState`/`StateChanged` have no consumers outside the
  class — which is itself worth knowing.
- [ ] **A mid-bar reconnect overwrites the in-progress bar with a partial one.**
  `LiveStreamManager:151` builds a fresh `BarBucketConsolidator` on every subscribe including every
  watchdog reconnect. For a `TradeDeltas` provider the new bucket starts empty, so the bar's Open
  becomes the reconnect price, High/Low collapse to the post-reconnect range, and Volume counts
  only trades since reconnect — then `ApplyLiveTick` `ReplaceLast`s the correct partial bar away.
- [ ] **`AnalyticsDataResolver` ignores its `asset` parameter**, so requesting ETH returns BTC
  data. `Resolve(string metric, string? asset = null)` never reads `asset`, while the registry
  advertises `{ "BTC", "ETH" }` for `ACTIVE_ADDRESSES`, `TX_COUNT`, `FEES_TOTAL`, `FUNDING_RATE`
  and `OPEN_INTEREST` with BTC-only sources. A cross-series strategy conditioned on ETH is
  silently tested and traded on Bitcoin, and `GetAvailableMetrics` reports ETH as supported.
- [ ] **Live ticks never recalculate indicators.** `DataManager.OnFocusedFeedUpdated` deliberately
  does not raise `DataUpdated` for `LiveAppend`/`LiveReplace`, and `DataOrchestrationService`
  recalculates only on `DataUpdated`, viewport/series changes, `IndicatorUpdatedEvent`, or
  `DataStatus → Ready`. So price moves and every indicator's newest value is stale until something
  else triggers a pass. Documented as known in `KEYED_FEEDS_DESIGN.md`; for a terminal whose whole
  premise is hearing the chart, this is a product-level gap rather than a nitpick.
- [ ] **Zero-price bars are silently deleted from every fetch.**
  `HistoricalDataFetcher.ApplyFinalFilters:199-203` drops any bar with a zero O/H/L/C. Analytics
  and My Data single-value series are carried as `Ohlcv(date, v, v, v, v, 0)`, so any data point
  whose value is exactly 0 vanishes — a zero net flow, a zero-balance day, a rate at 0. Negative
  values survive; zero does not, and the result is an invisible hole rather than a visible break.
- [ ] **`"1y"` datasets are unchartable with a misleading error.** `MyDataProvider` advertises
  `"1y"` and `CsvDataParser.InferTimeframe` assigns it to anything with median spacing ≥ 45 days,
  but the timeframe grammar is `^(\d+)([mhdMw])$`, so `IsValid("1y")` is false and
  `DataOrchestrator` rejects it with *"Invalid timeframe '1y' for My Data"* — which reads as a bug
  in the user's file. Import annual GDP and it appears in every dropdown and never loads.
- [ ] **Two `MyDataStore` instances over one user directory destroy each other's imports.**
  Registered `AddScoped` on the WebHost, so one per *circuit*, not per user. `_datasets` is loaded
  once in the constructor and written back wholesale with `File.WriteAllText`; two tabs means the
  stale list overwrites the fresh one and orphans the CSV. `AtomicFile` exists in the same
  namespace and is not used for either file.

### Ship-blockers — indicator lookahead (the backtest-validity cluster) — **ALL SIX FIXED 2026-08-21**

**Fixed by a contract rather than six patches.** Every component now declares whether its value at a
bar uses later bars (`ComponentCausality` on `IndicatorComponentMetadata`, inheriting from
`IndicatorMetadata` so an indicator usually declares once), `SignalCatalog` publishes only what was
declared `Causal`, and `IndicatorCausalityTests` proves the declaration by running every provider
over `bars.Take(k)` and over the full series and requiring agreement on the shared prefix, sweeping
k over eleven lengths and three synthetic series.

What that turned up beyond the six items below:

- **`PulseProvider.ComputeMtfRsi` / `ComputeMtfRegime` bailed on total series length** — `if (n <
  barsPerWeek * (rsiPeriod + 2)) return;` blanked the whole component on a short chart, including
  bars that had all the weekly history they needed. Now guards parameters only; insufficient history
  was already handled per bar.
- **`SwingStructureAnalyzer` sorted its pivots with an unstable sort.** `raw.Sort(by index)` on a
  list that was already in index order could only reorder TIES — and a bar that is both a pivot high
  and a pivot low (one outside bar engulfing its window) produces two entries at the same index.
  Introsort's pivot choices depend on list length, so the same bar's pair came back in one order at
  240 bars and the other at 400, and `LastSwingHigh` reported two different prices for bar 213
  depending on how much history had been fetched. The sort is gone.
- **`SignalCatalog.Refresh` would throw on a duplicate descriptor ID.** `EmaFillProvider` is an
  empty subclass of `MACloudProvider` kept as a name alias; registering both would have taken the
  app down during DI construction. First registration now wins.
- **A leaf pointing at a refused component now says so.** `ConditionEvaluator` used to return false
  for any descriptor it could not resolve, which is indistinguishable from a market that never met
  the condition. Refused leaves resolve by ID, return false, and record the reason on
  `LastDegradation` (renamed from `LastHtfDegradation`, which now has two callers).
- **The guard's blind spots are pinned, not hidden.** 89 components declared `Causal` produce no
  value on the synthetic series, so nothing verifies their declaration; they are listed by name in
  `IndicatorCausalityTests.NotExercisedByTheseSeries` and the test fails if that list grows OR if an
  entry becomes reachable. Most of the list is the "Ship-blockers — indicator computation" findings
  below (ten Skender indicators resolve to no method at all) and the external-data providers, whose
  cross-series cache is a substitute in tests.

Two things this does NOT cover, both deliberate, both still open:

- [ ] **`ScreenerService.ReadColumns` still reads a component value by descriptor without consulting
  causality.** Screener columns are displayed rather than traded and the condition path is gated, so
  this is not a fake-edge bug — but a column quoting `ICHIMOKU.Chikou Span` to a user deciding what
  to trade is quoting a price from 26 bars after the row's own bar, with nothing saying so. Either
  filter the column list to `catalog.All` (which would silently drop existing saved screeners) or
  keep the column and label it. The same question applies to any future surface that resolves a
  descriptor and reads its data — `ConditionEvaluator.RefusedForCausality` is the pattern to copy.
- [ ] **The next release's `WHATSNEW.md` needs a line about withdrawn leaves.** Some strategies
  built before this change will stop firing, deliberately, and the reason is now reported through
  `ConditionEvaluator.LastDegradation` — but no UI surface reads that property yet, so today the
  user's only clue is the leaf's absence from the builder. Either surface `LastDegradation` or say
  it plainly in the release notes; ideally both.
- **The external-data indicators are declared `Causal` on the strength of reading the code, and two
  findings further down this file say otherwise.** `COINMETRICS` stamps a daily metric at the START
  of the day it summarises (bar D's value derives from bar D's own close — verified empirically at
  3688/4116 days), and `COT_POSITIONING` / `CFTC_COT` ride on a synthesised report+3 release date
  worth up to ~10 weeks. Flipping those declarations to `Lookahead` would remove them from the
  strategy builder and from any StrategyLab command that reads them — a research-workflow decision,
  not a code decision, so it is left as a decision rather than made quietly here. **When the ingest
  fixes land (shift CoinMetrics by one period; source the real `report_publication_date`), the
  declarations become true and nothing needs to change.** Until then they are the one place in the
  contract where a declaration is a claim rather than a proof, and they are named in
  `IndicatorCausalityTests.NotExercisedByTheseSeries` because the test cannot reach them either.

- [x] **`IchimokuProvider` Chikou Span** — declared `Lookahead`; the displaced array is untouched for
  the chart. Its navigation speech now says "Chikou span, close from a later bar, at X" rather than
  quoting a future price as if it belonged to the cursor's bar. It does not name the bar count:
  `Displacement` is a per-series parameter and `GetComponentSpeech` is not given the parameters.
- [x] **`CipherAProvider` divergences** — `DivergenceConfirmLag` added, default ON, matching Cipher B.
  `ShiftMarkersForward` moved to `IndicatorMath` and both providers call it. All three markers
  (Bullish, Bearish, Blood Diamond) are shifted, pinned per-component in `DivergenceConfirmLagTests`.
- [x] **`CipherSRProvider`** — pivot dots stay on the pivot bar and are declared `Lookahead`; the
  carry-forward zone lines step to a level at `pivotBar + PivotBars`, the bar it becomes confirmable,
  and are `Causal`. AutoScale now derives the pivot window from the bar's own position,
  `Clamp((i + 1) / 25, 2, 15)`, instead of from the total loaded bar count.
- [x] **`SwingStructureProvider`** — the class doc's "every component is safe as a strategy leaf" is
  replaced by the true statement; the two markers are declared `Lookahead` and the state and
  carry-forward arrays stay published. Which arrays a strategy may read is now enforced by the
  catalog rather than asserted by a comment.
- [x] **`SwingStructureAnalyzer` pass 2** — a more extreme same-kind pivot now SUPERSEDES the earlier
  one from its own confirmation bar instead of deleting it from history. Pass 3 labels every member
  of a run against the same baseline, so the denoising survives and a run cannot manufacture a
  higher-high sequence out of one move.
- [x] **The vacuous causality test** — `CandlePatternAnalyzerTests.cs:278`'s chained `Take` is
  superseded by `IndicatorCausalityTests`, which does the comparison it was trying to do, for every
  provider rather than one. *(The vacuous test itself is still there — see "Guard tests that do not
  guard".)*

<details>
<summary>Original findings, kept for the reasoning</summary>

**The load-bearing fact for this whole section:** `SignalCatalog.Refresh`
(`Services/Strategies/SignalCatalog.cs:44-61`) does `foreach (var ind in indicators) foreach (var
comp in ind.Components)` and publishes **every component of every provider** as a strategy leaf
`{CODE}.{ComponentName}`. VERIFIED — there is no allowlist and no causality gate. So "is this
component lookahead-safe" is not a chart-cosmetics question for any component in the codebase; it
is a backtest-validity question for all of them.

This project's research discipline has correctly returned *null* on essentially every price-derived
claim it has tested. These are the components that would corrupt exactly that process.

- [ ] **`IchimokuProvider.cs:264-266` — the Chikou Span array is a raw 26-bar future price, exposed
  as a strategy leaf.** VERIFIED: `int bwd = i - displacement; if (bwd >= 0) chikou[bwd] =
  data[i].Close;` — so `chikou[j]` literally holds `close[j+26]`. Correct as a *plotting*
  convention, catastrophic as a *data* convention. `ICHIMOKU.Chikou Span` is a `Line` leaf, so a
  backtest condition `Chikou Span > Close` at bar j evaluates `close[j+26] > close[j]` and returns a
  spectacular, entirely fake edge. It is also spoken during navigation (`:336-337` says
  `$"Chikou span at {price}"`), quoting a price 26 bars ahead of the bar the cursor is on. Fix: keep
  the displaced array for rendering under a `__`-prefixed internal key and either drop the leaf or
  publish the causal form.
- [ ] **`CipherAProvider.cs:400-427` — Cipher A's divergences are still stamped at the pivot bar;
  the divergence-lookahead fix was applied to Cipher B and never back-ported.** The pivot loop reads
  `pivotBars` bars into the future and then writes `bullDiv[curr] = low[curr] - offset`. There is no
  `DivergenceConfirmLag` here — `grep ShiftMarkers|ConfirmLag CipherAProvider.cs` returns nothing —
  unlike `CipherBProvider.cs:1037-1052`. `Bullish Divergence` / `Bearish Divergence` / `Blood
  Diamond` are `MarkerFire` leaves, so a backtest enters at the exact pivot low with 3 bars of
  hindsight. Cipher B's own comment at `:1017-1023` calls this out as inflating "every
  divergence-based backtest". Fix: make `ShiftMarkersForward` shared and give Cipher A the same
  default-ON lag.
- [ ] **`CipherSRProvider.cs:245,299,321` — the historic "Cipher SR backtest lookahead" is still
  live.** `if (isPivotHigh) resistance[i] = data[i].High;` writes at bar `i`, which the class doc at
  `:27-29` admits requires `[i-PivotBars .. i+PivotBars]`; pass 2 then carries the level forward from
  `i+1`. With `AutoScale=1` and 500 bars loaded, `pivotBars = 15`, so the zone line is visible **14
  bars before it exists**. `SwingStructureAnalyzer.cs:179` does this correctly via
  `ConfirmedAtIndex = BarIndex + Span` — copy that.
- [ ] **`SwingStructureProvider.cs:196-201` contradicts its own class doc.** The doc at `:37-40`
  says *"NO LOOKAHEAD: pivots are published at barIndex + Span, never at the pivot bar, so every
  component is safe as a strategy leaf."* The code writes `high[s.BarIndex]` / `low[s.BarIndex]`.
  The inline comment quietly narrows the claim to the state and carry-forward arrays — but
  `SignalCatalog` publishes `SWING_STRUCTURE.SwingHigh`/`.SwingLow` as leaves regardless, so a
  strategy gated on them fires `Span` bars early.
- [ ] **`SwingStructureAnalyzer.cs:141-159` — pass 2 retroactively deletes swings, so the same bar
  gives different answers depending on how much data was loaded after it.** `if (last.IsHigh ==
  p.IsHigh) { … kept[^1] = p; continue; }` — a later same-kind pivot removes the earlier one from
  history. `Analyze(bars[0..35])` can report a structure break at bar 28 that
  `Analyze(bars[0..100])` does not. Information-losing rather than future-peeking, but it means
  panning back reproduces a structure that was never knowable, and it destabilises every
  `ChartPatternDetector` result under a change in loaded bar count. The class doc at `:90-93` claims
  "NO LOOKAHEAD ANYWHERE".
- [ ] **The only causality test in the codebase is vacuous.** VERIFIED —
  `CandlePatternAnalyzerTests.cs:278` reads `bars.Take(i + 6).Take(i + 1).ToList()`. Chained LINQ
  `Take` takes the minimum, so that is exactly `.Take(i + 1)` — the "with hindsight" list **is** the
  "at the time" list, and the test compares a value to itself. `ClassificationOfABarNeverChanges
  WhenLaterBarsArrive` has never been able to fail. The class comment at `:258-260` states precisely
  the invariant the test does not check.

**The single highest-value fix in this audit:** add a causality declaration to
`IndicatorComponentMetadata`, have `SignalCatalog` refuse to publish any component that does not
declare it, and add one parameterised test that runs every provider over `bars.Take(k)` vs `bars`
and asserts equality on the shared prefix. That one test catches Cipher A, Cipher SR, Swing
Structure's markers, Ichimoku's Chikou and the pass-2 revision — five of the six items above — and
prevents the next one.

*(Done, 2026-08-21. It also caught three things this audit did not list — see the summary above.)*

</details>

### Ship-blockers — indicator computation

- [ ] **`IndicatorEngine.cs:127-133` — on a same-bar tick the buffer is handed to the provider
  ZEROED, and every component the provider does not rewrite is pushed to the live chart as 0.0.**
  The carry-forward copy is guarded by `if (kvp.Value.Length < span.Length)`, which is true when a
  new bar appends and **false when the last bar updates in place** — i.e. on every tick inside a
  forming candle. `GetComponentSpan` has already minted a fresh zero-filled array, and `:137-142`
  then harvests `[^1]` from every key including ones the provider never touched. Two confirmed live
  consequences: the non-ratio COMPARE overlay drops to the pane floor on every tick
  (`SymbolCompareProvider.cs:140` writes nothing on that branch), and Cipher S reads
  `pctSpan[i-1]` as 0.0 instead of NaN so an 80th-percentile bar narrates as "Neutral" for the whole
  life of the bar then snaps correct on close. The maintainers already special-cased exactly this
  for core series at `IndicatorOrchestrator.cs:261-266` ("would overwrite the last bar's value with
  0.0, making the Price line invisible") — the general fix was never made. One-line fix:
  `Array.Copy(..., Math.Min(kvp.Value.Length, span.Length))` unconditionally, then NaN-fill the rest.
- [ ] **`IndicatorResultBuffer.cs:65,75` zero-fills where the codebase's own stated convention is
  NaN.** `IndicatorOrchestrator.cs:300` says it explicitly: `Array.Fill(newArr, double.NaN); // NaN
  = no data; prevents zero-valued slots from firing signals`. Any provider that fills part of a
  range leaves genuine-looking 0.0 that renders as a line pinned to zero and sonifies as real data.
  The class doc at `:15-17` claims it turns "silent NaN arrays into loud, immediate errors".
- [ ] **`SkenderCalculationCore.cs:193-201` — ten registered Skender indicators resolve to no method
  and render as silent empty lines, including Bollinger Bands, which ships in the default demo set.**
  The lookup is `m.Name.Equals("Get" + code)`; against Skender.Stock.Indicators 2.5.0 there is no
  `GetBb`, `GetKc`, `GetChandelierExit`, `GetUltOsc`, `GetPpo`, `GetZlema`, `GetTma`, `GetHv`,
  `GetEom` or `GetMom`. `Bb` is shipped by `DemoPolicy.cs:193`. Add a startup assertion that every
  registered `Code` resolves. **CONFIRMED 2026-08-21** by reflecting over Skender 2.5.0 — the ten
  are exactly the ones listed.
- [ ] **NOT IN THE AUDIT — seven more Skender indicators resolve fine and then write to component
  names nobody declared, so they render as silent empty lines too.** VERIFIED 2026-08-21 by
  reflecting over the result types. `SkenderCalculationCore.cs:101-113` writes one span per public
  property of the result object, keyed on the **property name**, so a component whose declared
  `Name` is not a property of that result is never written and a property nobody declared is never
  read. Five of the seven render **nothing at all**, including Stochastic:
  - `Stoch` declares `PercentK` / `PercentD`; `StochResult` exposes `Oscillator`, `Signal`,
    `PercentJ`, `K`, `D`, `J`. Both components blank.
  - `Vortex` declares `Vip` / `Vim`; `VortexResult` exposes `Pvi` / `Nvi`. Both blank.
  - `Chop` declares `ChopIndex`; `ChopResult` exposes `Chop`. Its only component, blank.
  - `UlcerIndex` declares `UlcerIndex`; `UlcerIndexResult` exposes `UI`. Its only component, blank.
  - `Adx` declares `Adl` / `Adh` alongside its working ones; `AdxResult` exposes `Pdi`, `Mdi`,
    `Adx`, `Adxr`. Two components blank. (Note `Adl` here is a *typo-shaped* name — it collides
    conceptually with the Accumulation/Distribution Line, which is a different indicator.)
  - `Adl` declares `Adl3`; `AdlResult` exposes `MoneyFlowMultiplier`, `MoneyFlowVolume`, `Adl`,
    `AdlSma`. One component blank.
  - `Roc` declares `RocP`; `RocResult` exposes `Momentum`, `Roc`, `RocSma`. One component blank.

  This is the same class as the `BarDetailService` dead-code finding below (`"Upper"` vs
  `"UpperBand"`) and wants the same remedy: **one startup assertion that every declared component
  name is a name the calculation actually writes.** That single check would cover both this and the
  ten-missing-methods item above, and it is the only way this class stops recurring — an ordinal
  dictionary lookup that misses is silent by construction. Found by
  `IndicatorCausalityTests.TheBlindSpotsOfThisGuardAreTheOnesWeKnowAbout`, where all seventeen sit
  in the pinned blind-spot list because a component that never produces a value cannot have its
  causality verified either.
- [ ] **`SkenderCalculationCore.cs:172-178` — every Skender indicator's live bar is recomputed from
  a 35–200 bar stump.** No Skender provider declares `RequiresFullRecalcOnTick`, so cumulative and
  long-memory indicators show values a full recalc would never produce: **VWAP** becomes a 35-bar
  VWAP rather than cumulative; **OBV/ADL** restart from zero 35 bars back and collapse by orders of
  magnitude, snapping back on the next full recalc; **EMA(200)** gets exactly 200 bars and Skender
  seeds EMA from the SMA of the first `lookbackPeriods` quotes, so the live EMA200 prints SMA200.
- [ ] **`SkenderCalculationCore.cs:173-178` reads uninitialised `ArrayPool` memory.** The temp buffer
  is sized to `windowSize` but only `slicedData.Length` cells are written, and `[^1]` is index
  `windowSize-1`; the pool is rented without `clearArray`. On a freshly-listed symbol with 60 bars
  and EMA(50), the live value is a recycled double from a previous tenant.
- [ ] **`SkenderCalculationCore.cs:139-157` — the synthetic `__CROSSOVER`/`__SQUEEZE` flags are
  sticky-true on the incremental path** (written on a cross, never cleared, over a dirty pooled
  array). `SkenderDetailFactProvider.cs:112-117` reads them verbatim, so the user hears "Bullish
  crossover!" on every arrow-key press for the rest of the session.
- [ ] **`CoinMetricsProvider.cs:204` — daily metrics are stamped at the START of the day they
  summarise, so bar D receives a value derived from bar D's own close.** Empirically end-of-day:
  the point stamped day D matches day D's close within 0.5% on 3688/4116 days in the shipped
  snapshot. MVRV uses that same close, so `COINMETRICS.MVRV LessThan 1` on bar D reads bar D's
  close. Shift by one period at ingest and pin it.
- [ ] **`Plugins/Analytics/…Cftc/CftcProvider.cs:197` — COT "release date" is a synthesised
  report+3 days.** The shipped gold snapshot carries an unbroken weekly series straight through the
  2018-12-22 → 2019-02-05 CFTC shutdown, none of which was published until the catch-up releases in
  late March — up to ~10 weeks of usable lookahead. Source the real `report_publication_date`.
- [ ] **`OpenInterestProvider.cs:203` and `CrowdingIndexProvider.cs:258` — zero-valued OI rows are
  not filtered.** The guard is `IsNaN` only, and every shipped OI snapshot contains literal zeros
  (2022-03-07 is 0.0 in all eight files). The trader hears "Open interest 0.00 billion dollars",
  then a −4.5e9 and a +4.5e9 delta, both clearing the 2σ spike test — two false spike earcons —
  and the blown-up variance then suppresses every genuine spike for the next 30 bars.

### Indicator correctness — HIGH

- [ ] **`BarDetailService.cs:227-282` — both indicator narrative facts are dead code.**
  `BollingerSqueezeExpansionFact` looks up `"Upper"`/`"Lower"`; the provider declares
  `"UpperBand"`/`"LowerBand"`. `MacdCrossoverFact` looks up `"MACD"`; the provider declares
  `"Macd"`. `ComponentData` is an ordinal `Dictionary`. Both branches are entered and both return
  `""` — ~55 lines with detailed doc comments describing behaviour that cannot occur. Add them to
  `ComponentSpeechKeyTests`, which exists for exactly this failure mode.
- [ ] **`IndicatorContextAnalyzer.cs:85-91` — the Money Flow Wave thresholds are calibrated to a
  scale the provider does not emit**, so it narrates "approaching overbought" on ~85% of bars.
  Registered `-70/-90` with a comment claiming a −100..−60 range; `CipherBProvider.cs:624,1011`
  emits **0-centred ±100**.
- [ ] **`IndicatorContextAnalyzer.cs:197-213` — `DetermineZone` never compares price to a band.**
  For a component named "Upper" it unconditionally returns `AtUpperBand`; `prevValue`, `series`,
  `state` and `dataIndex` are all accepted and never read. Currently masked by the naming bug above,
  so fixing that activates this one.
- [ ] **`CipherSProvider.cs:96-109` speaks unhedged trading instructions** — *"strong DCA
  opportunity"*, *"Trim positions"*, *"Tighten stops"* — spoken on every navigation to a candle,
  backed by nothing but a percentile rank of close in a rolling window.
  `ChartPatternNarrator.cs:34-39` states the house rule: *"No pattern is ever called bullish or
  bearish. The conventional readings … are exactly the claims this project has tested and failed to
  confirm."* The chart-pattern feature refuses the word "bullish"; Cipher S issues imperatives.
  `TopBottomDetectorProvider.cs:485-496` has a milder version. `ValueDeviationProvider.cs:301-304`
  shows the correct form. This is a policy decision that should be made deliberately rather than by
  whichever file was written first.
- [ ] **`CipherSRProvider.cs:448-455` — pivot speech scans FORWARD to report a touch count from the
  future**, so a historical resistance pivot announces "tested 3 times" using bars after the cursor.
  And `:306-317` increments the counter once per **bar** rather than once per approach, so a level
  sitting above price for twenty bars is "tested 20 times" —
  `LevelRespectAnalyzer.cs:68` exists in this same codebase specifically to prevent that
  (*"Counting each bar of a multi-bar consolidation against the line is how naive touch counters
  manufacture significance out of chop"*).
- [ ] **`AnchoredVwapProvider.cs:142-163` anchors to the confirmation bar, not the pivot**, silently
  omitting the first `PivotLookback` bars of volume — a quarter of a 20-bar swing leg at the default
  5. The class doc at `:24` says "since last swing high". It is not.
- [ ] **Skender component names that resolve to nothing or to duplicates.** `Vip`/`Vim` on Vortex
  (result exposes `Pvi`/`Nvi`), `Adl`/`Adh` on ADX, `UlcerIndex` (property is `UI`) — all
  permanently NaN, drawn as labelled, sonified, navigable empty lines. And the mapper's substring
  tiers accidentally duplicate: Stochastic's `PercentK`/`PercentD` draw exactly on top of
  `Oscillator`/`Signal`, doubling the sonification voices for no information; same for
  `RocP`→`Roc` and `Adl3`→`Adl`. `SkenderTrendProvider.cs:152`'s KAMA "Lookback Periods" control is
  inert (the real parameter is `erPeriods`), so dragging it 10→50 redraws an identical line.
- [ ] **`SkenderDetailFactProvider.cs:174` announces the literal string "F3".**
  `$" Slope {slopePct:+F3;-F3}% per bar."` — `"+F3;-F3"` parses as a custom format with
  positive/negative sections where only `0` and `#` are digit placeholders, so it is spoken as
  "Slope plus F three percent per bar" on every MA.
- [ ] **`IndicatorMath.cs:141-151` — `ComputeStochRsi` fabricates 50.0 across the entire RSI
  warmup** (an all-NaN window leaves `range` negative, taking the "flat series" branch), so the
  chart draws a hard flat line and a screen-reader user hears a steady tone and a real number where
  there is no data.
- [ ] **`FundingRateProvider.cs:60` (+ OpenInterest, Crowding — verbatim triplicate) mangles
  USD-quoted symbols.** `if (!clean.Contains("USDT")) clean += "USDT";` turns `BTC/USD` →
  `BTCUSDUSDT`. `DemoPolicy.cs:198` sets `DefaultSymbol => "BTC/USD"`, so on the app's own default
  chart Funding, OI and Crowding are permanently blank. The existing test only covers USDT-quoted
  and bare-base inputs.
- [ ] **`CrossSeriesCache.cs:97,153` — no TTL on success, no negative caching on failure.** A
  session left open overnight freezes funding/OI/FNG/COT at their first fetch while price keeps
  ticking, with no staleness indication in speech; conversely an unresolvable symbol re-attempts a
  blocking `task.Wait(5s)` on every pan, tick and parameter change. The class doc promises "~1-2s
  only the first time".
- [ ] **`CoinMetricsProvider.cs:279` attributes ETH's on-chain metrics to a BTC chart** whenever the
  loaded range predates 2017 — median-close detection maps `> 200 and <= 5000` to "eth", and a
  2013–2017 BTC chart has a median close ≈ $600. No warning fires.
- [ ] **`CotPositioningProvider.cs:281` starts emitting Crowded markers off a 5-sample population**
  (`window.Count >= 5` with a population divisor, where max attainable |z| is 1.789 against a ±1.5
  threshold) while `:309` tells the user *"The series needs about six months of weekly reports
  before the z-score is defined."*
- [ ] **`VolRegimeProvider.cs:121` throws `IndexOutOfRangeException` on an empty bar series** —
  `r[0] = double.NaN` with no `n == 0` guard, where every peer provider has one.
- [ ] **Chart-pattern geometry defects.** `ChartPatternDetector.cs:388-391` — the H&S neckline
  comment says "conservative" and `Math.Max` picks the *least* conservative trough, disagreeing with
  the DoubleTop convention 80 lines above. `:432-436` — the triangle detector pairs highs with lows
  up to 160 bars *before* the high pair, so a January high pair can form a "triangle" with the
  previous November's lows. `:501-503` — the symmetrical triangle silently assumes an upside break,
  contradicting the Rectangle branch's own argument that a two-way shape must not be given a
  direction; one that breaks down is narrated as "ends here without confirming". `:635` — the
  Forming/Expired boundary is off by one.
- [ ] **`ChartPatternNarrator.cs:189-190` — the exact wording defect the class doc at `:199-207`
  claims was fixed is still reachable.** `EdgeWord` tests `barIndex <= p.KnownAtIndex` before
  `>= p.ResolvesAt`, and `CompletedAtIndex == KnownAtIndex` is ordinary, so the terminal still says
  *"Start of double top: price closed below the neckline at 42,100."*

### Ship-blockers — audio and speech

Each of these is a wrong or absent utterance, which for this product is the failure mode that
matters most.

**Three fixed 2026-08-21 (the `FeedbackType.Alert` / master-gain / `:F0` batch)** — see the three
`[x]` entries below. What the twin sweeps turned up, none of it in the audit's 203 findings:

- **`BitstampProvider.PlaceOrderAsync` formatted the LIMIT PRICE with `ToString("F2")`.** Not a
  description of an order — the order. A limit at 0.0363 went to the exchange as "0.04"; anything
  under half a cent went as "0.00". No `InvariantCulture` either, so a comma-decimal machine
  posted "0,04". Every other provider in the tree already did full-precision invariant; Bitstamp
  was the one that did not. Found by the new sweep test, which is the whole argument for writing
  it. Coinbase (`F8`, nine sites) and Alpaca (`qty` at `F4`) had the culture half of the same
  defect and are fixed too.
- **Chart volume was applied twice, so F7 ran backwards.** `ChartCommandManager` pushed
  `ChartVolume` into the engine's *global* master gain **and** it is threaded into every
  chart-sonification path as the per-note `masterVolume` factor. The chart therefore played at
  ChartVolume **squared**: raising the volume from 50% to 60% made it *quieter* (0.50 → 0.36).
  Fixed by deleting the master-gain push — see the decision note in the master-gain item below.
- **`EarconType.Boundary` mapped to `FeedbackType.Navigation`** in `GlobalErrorCoordinator` — a
  boundary asking for the wrong sound, invisible only because `Navigation` had no arm at all.
- **`PlayEarcon` was dead for six more members**, not just `Alert` — the finding below about
  `StateChange`/`Navigation` is the same missing-arm bug and was fixed in the same pass.
- **The AI analyst was fed `F2` OHLC.** `AIAnalystService:212` sent up to 50 bars to the model as
  `O=0.00 H=0.00 L=0.00 C=0.00` for any sub-dollar asset, so the model analysed a flat chart and
  answered confidently about it.

Still open from that sweep, and deliberately not done in it:

- [ ] **Component metadata has no way to say "this value is a price."** The fix above changed the
  price-space components' `SpeechTemplate` defaults to the `{value:price}` token one by one
  (pivots, AVWAP, VWAP, Bollinger, MACD). That is a list someone has to keep correct by reading.
  `SpeechFormatter.ResolveComponentValue` already overrides fixed precision automatically — but
  only for series whose id is `price`/`candles`, so every main-pane *overlay* is outside it. A
  declared flag on `IndicatorComponentMetadata` (or a rule derived from "plotted on the price
  axis") would make the whole class structural instead of enumerated.
- [ ] **Saved workspaces persist `SpeechTemplate`, so existing users keep the old `{value:F2}`**
  on those components. The comment at `SpeechFormatter:747` already anticipates exactly this for
  the price series and overrides it; overlays have no such override. Either migrate stored
  templates or extend the override — the second one is the same work as the item above.

- [x] **`FeedbackType.Alert` is dropped entirely — neither spoken nor earconed.** **FIXED
  2026-08-21.** Arms added for `Alert` (earcon then speech on the Event channel, matching
  `AlertFiredEvent`) and for `SeriesSelection`/`ComponentSelection`/`PointFocus`/`ViewportChange`,
  plus a `default` that logs the unhandled member and still speaks any message rather than
  dropping it. The earcon router got a `default: PlayInfo()` for the same reason — a caller that
  asks for a sound gets a sound. Guarded by `FeedbackTypeCoverageTests`, which enumerates
  `Enum.GetValues<FeedbackType>()` for both routers; proven to fail by deleting each arm.
  Original finding, for the record: the
  enum has eleven members; `AccessibilityFeedbackCoordinator.OnFeedbackRequest` handles six
  (`StateChange`, `Navigation`, `VolumeChange`, `Error`, `Boundary`, `Info`) with **no `default`**.
  `GlobalErrorCoordinator:84` publishes every network-retry notification as `Alert` — *"Connection
  lost to {provider}. Retry {n}. Next attempt in {s} seconds."* — and it is constructed, published
  and discarded. `ConfigurableStrategy:466` publishes `Alert` too. The websocket can drop
  mid-session and the trader is told nothing. This is the third instance of the missing-switch-arm
  class: `FeedbackRouters:167-170` already carries the annotation *"FOUND 2026-07-21: Alert had no
  case — every PlayEarcon(Alert) call was SILENT"*, the earcon router was fixed, the speech switch
  was not. `SeriesSelection`, `ComponentSelection`, `PointFocus` and `ViewportChange` are also
  unhandled. Fix: add the arms, add a `default` that logs the unhandled member, and add a test that
  enumerates `Enum.GetValues<FeedbackType>()`.
- [x] **Any voice command resets master gain to full, so F7-to-zero does not mute.** **FIXED
  2026-08-21.** `AudioEngine` now keeps `_userMasterGain` (the last value anyone asked for) apart
  from `_stopAllFaded` (the flag saying a zero is OURS). The re-arm restores the user's value and
  only after a stop-all fade, so a chosen zero survives every subsequent command. Guarded by
  `MasterGainTests`, proven to fail against the old line.
  **A decision was made here that the audit did not ask for, and it should be read before anyone
  "restores" the old behaviour:** the audit's framing was that a master the user set to silent
  should stay silent, including for earcons. But F7 is documented as *chart* volume, and the
  2026-07-21 mute-tier redesign deliberately separated the tiers — F3 owns chart sonification,
  Shift+F3 owns earcons, and `EarconService.CanPlay` carries the note *"Before 2026-07-21 earcons
  silently died with F3."* Putting every earcon behind a chart-scope control would reintroduce that
  exact bug on a different key, and would silence stop-hit and order-fill cues. So chart volume no
  longer touches the global master gain at all (it was being applied twice anyway — see the squared
  -volume finding above); it scales chart notes through the per-note factor it already threads, and
  earcons answer to Shift+F3. Master gain answers to `StopAll`, and to nothing else.
  Original finding, for the record:
  `AudioEngine:400` — `if (cmd.IsActive && _targetMasterGain == 0.0f) _targetMasterGain = 1.0f;`.
  The line exists to re-arm gain after `StopAll` faded it out, but it cannot tell that condition
  apart from a user-set gain of zero. Take chart volume to 0% with `Shift+F7` (speech confirms
  "0%"), and the next arrow key snaps master gain to 1.0. Navigation notes stay quiet because
  `ChartVolume` also multiplies into `baseVolume`, but **earcons do not** — `EarconService` passes
  fixed literal volumes — so an order fill, a stop hit or a boundary earcon then fires at full
  volume on a master the user set to silent.
- [ ] **Zone-proximity speech overwrites the bar reading on the web head.** VERIFIED as a second
  `Speak` in the same synchronous call stack: `NavigationFeedbackManager:306` speaks the composed
  utterance, then `CheckAndPlayZoneProximity` (called at `:313`) speaks again at `:530-533`. The
  28-line comment at `:248-263` states exactly why this is wrong — on the Blazor head speech is an
  ARIA live-region write, Blazor batches an event handler into one render, so only the final write
  reaches the DOM. On a bar that straddles a zone the user hears **only** "Near support at 0.0831"
  and loses the price, OHLC, volume, pattern and formation clause. Different content per head from
  the same keypress. Fix: return the clauses and append them to `utterance` before the single
  `Speak`. Test: assert exactly one `Speak` per `HandleNavigationFeedback`.
- [x] **Two surviving `:F0` price formats collapse every sub-dollar value to "0".** **FIXED
  2026-08-21**, along with fourteen more sites the audit did not list, found by reading every
  price-space call site rather than only the two reported. Both named sites now route through
  `SpeechPriceFormatter`; the metadata sites (pivot lines, AVWAP from high/low, VWAP, Bollinger
  bands and centreline, all three MACD components) use the `{value:price}` token; the direct
  interpolations (Cipher S/R nearest levels and level readout, Skender band width / VWAP / ATR
  detail facts, Regime's close-minus-SMA, the Ichimoku cloud width in `SpeechFormatter`, the AI
  analyst's OHLC rows) call `FormatPrice`. `PriceFormatScanTests` is the sweep: it fails on a
  fixed `F0`/`F1`/`F2` sitting next to a quote-currency word, and separately on any order field
  serialised without `InvariantCulture`. The second check is what caught Bitstamp.
  Original finding, for the record:
  `NavigationFeedbackManager:112` (coordinate-entry delta — *"Change from anchor: +0"* for every
  KAS/SHIB/PEPE move) and `AutoNarrationService:403-404` (support/resistance **break** — *"Support
  at 0 broken"*, on arguably the most consequential message the narrator produces; the touch,
  approach and cross messages 20-60 lines below all correctly use `SpeechPriceFormatter`). The file
  itself documents this exact bug class at `:526-529`.
- [ ] **Component-context speech reads RAW OHLC under Heikin-Ashi.** `NavigationFeedbackManager`
  carefully HA-transforms the bar and passes it as `pt`, but `SpeechFormatter.GetPointValue:369-389`
  ignores `pt` and returns `series.GetComponentData(name)[i]`, which `ViewportReducer` fills from
  **raw** bars. So with HA on, `upper_wick`/`lower_wick`/`line` all speak raw values while the
  chart, the tone and the detail key show HA. `body` escapes only because `CandleBodyStrategy`
  reads `ctx.Pt`. This is the same defect `35928149` fixed in `BarDetailService` ("the detail key
  described a candle that was not on screen"), still live one layer over. HA candles routinely have
  no shadow where the raw bar has one.
- [ ] **Muting is not absolute for Cloud components.** `NavigationSonifier:352` and
  `AudioSequencer:458` clamp to `Math.Clamp(... , 0.05f, 1f)` — a floor applied *after* every gain
  factor, so component volume 0, series volume 0, chart volume 0 and mute all still leave 5%. Every
  other volume path clamps the normalisation factor and multiplies by `baseVolume`, which correctly
  collapses to zero. `SonifyCloudNavigation` also never checks `comp.IsMuted` at all. Press `M` on
  an Ichimoku Kumo, hear speech confirm "muted", and it keeps sounding.
  `SonificationTimbreTests.AMutedOrHiddenComponentIsSilent` only covers `Candle` — make it a
  `[Theory]` over every display type with a dedicated render path.
- [ ] **`LevelCrossingMonitor` ignores series mute and every volume tier.** `:113` filters on
  `!series.IsVisible` with no `IsMuted` check, unlike every other per-series scan in the layer. The
  tones also bypass `ChartVolume`, series and component volume entirely. Mute a noisy RSI and its
  approach chimes and sustained-zone tones continue at fixed volume.
- [ ] **`PlayNote`'s `delay` parameter is accepted and silently discarded**, so every multi-note
  earcon is a simultaneous chord rather than the sequence its comment describes.
  `NavigationSonifier:419-423` never reads `delay`. `PlayStopHit`'s "low minor-third descent" is a
  simultaneous cluster; `PlayTakeProfitHit`'s "bright major arpeggio up" is a chord;
  `PlayOrderFill`'s "two quick staccato notes then a sustained tone" is two byte-identical D5 calls
  summing into one note. Fill and stop are documented as distinguishable *by shape*; as clusters
  they differ only in pitch content, which is far weaker discrimination mid-session.
  `CrossEarcon.Fire` already has the working shape to copy.
- [ ] **The earcon round-robin overwrites the "dedicated" cross-chirp and level-cue slots.**
  `PlayNote` cycles the full 16-31 range (`& 15`), but `CrossEarcon` reserves 30/31 and
  `EarconPatchPlayer` reserves 26-29. Four consecutive `PlayNote` calls starting at 28 wipe both.
  `CrossEarcon.Fire` staggers its second note by 70 ms, so the window where the direction-carrying
  note can be stolen is 70 ms wide — an up-cross can be heard as a down-cross. Fix: restrict the
  round-robin to 16-25 and document the reservation.
- [ ] **No output limiting anywhere.** `AudioEngine:621-624` writes `leftSum * _masterGain`
  unbounded over up to 128 voices; a noise-textured voice alone peaks at ±2 before `renderVolume`.
  Neither driver clips — `WebHostAudioDriver` pipes float32 straight to `pw-cat`/`pacat`/`aplay`.
  Chart-scope playback with candles, volume bed, price line and three indicators routinely exceeds
  4.0 and hard-clips at the device, producing broadband distortion exactly where the design relies
  on subtle timbral distinctions being legible. Every timbre test asserts on the `AudioPoint`
  struct, not on rendered audio, so none of them notices.
- [ ] **The volume bed and the candle body play the same two pitches.**
  `SonificationProfileProvider:47` declares `BaseFrequency: 330` for Volume with
  `PitchMapping.PriceDirection` — but `CreateAudioPoint` never reads `BaseFrequency` for
  Direction/PriceDirection mappings, it uses `comp.BullishFrequency`/`BearishFrequency`, which
  default to 440/220 for both Volume and Candle. The comment claims 330 "seats it under the candle
  body as its own distinct instrument". Same for Histogram. Worse,
  `SonificationTimbreTests.TheHistogramAndTheVolumeBedAreDistinctInstruments` hardcodes 440/220 for
  both in its helper and never compares `Frequency`, so it passes with the pitches literally
  identical.
- [ ] **`AutoNarrationService:179` re-introduces the F2 bypass the router redesign removed.** A
  call-site `if (!state.IsSpeechEnabled) return;` sits above narration that already passes
  `channel: SpeechChannel.Event`. `FeedbackRouters:16-19` says explicitly: *"the gate lives HERE,
  at the router, not at call sites — per-call-site `IsSpeechEnabled` checks are exactly how the F2
  bypasses crept in."* So F2 (manual-speech mute) also silences event-channel narration — the one
  channel the user deliberately left on.
- [x] **`PlayEarcon(StateChange)` and `PlayEarcon(Navigation)` are silent no-ops.** **FIXED
  2026-08-21** in the `FeedbackType.Alert` pass — same missing-arm defect, one rung down, so it
  was fixed with the same `Enum.GetValues<FeedbackType>()` guard. Original finding:
  `FeedbackRouters:163-173` has arms for `Error`, `Info`, `Alert`, `Boundary` and no `default`.
  `AccessibilityFeedbackCoordinator:144` requests `StateChange` for `OrderCancelledEvent` — with a
  comment two lines above saying *"Cancels were the one order state change that vanished silently
  (2026-07-22 audit)"*. The fix added a call that does nothing. Five of sixteen `EarconType` values
  are dead through the same route.
- [ ] **Hitting the first or last series is a silent no-op.** `NavigationEngine:178-190` clamps and
  returns with no `else`, unlike `NavigateX` which publishes `FeedbackType.Boundary`. Silence is
  indistinguishable from a broken binding — the exact failure the feedback contract forbids, and
  `AccessibilityFeedbackCoordinator:536-552` says so in as many words.
- [ ] **`SoundPatchLibrary` mutates a plain `List<SoundPatch>` that the navigation path enumerates.**
  `GetPatch` runs `FirstOrDefault` on every keypress and every playback bar while the Sound Designer
  calls `AddPatch`/`RemovePatch`/`UpdatePatch`. Save a patch during playback and
  `InvalidOperationException: Collection was modified` throws inside `CreateAudioPoint`, inside
  `SyncNavigationSlots`, inside the `StateStream.Subscribe` handler — and an unhandled exception in
  an Rx `Subscribe` **terminates the subscription**. All navigation sonification stops permanently
  for the session with nothing surfaced. Worst possible failure mode for this product and the
  hardest to diagnose by ear. Fix: `ConcurrentDictionary` (as `SoundPatchRegistry` already uses),
  plus a try/catch around the `SyncNavigationSlots` call that reports through
  `IGlobalErrorCoordinator`.
- [ ] **`AudioEngine.Reset` snaps `_masterGain` to 0, producing a click**, discarding the 20 ms fade
  `StopAll` exists to provide. The comment above it — *"Master gain is written only from the main
  thread and read only in `Read()`"* — is false on both counts (`Read()` writes it at four sites)
  and answers a different question than the one that matters.
- [ ] **Systemic: no invariant-culture pinning for spoken numbers.** No
  `InvariantGlobalization`, no `CultureInfo.DefaultThreadCurrentCulture` anywhere.
  `SpeechPriceFormatter` and `QuantityFormatter` pass `InvariantCulture` correctly; nothing else
  does — including **order fill/stop/TP quantities** (`AccessibilityFeedbackCoordinator:182`),
  every non-price component value (`SpeechFormatter:728`), exact volume (`:703`), percentages, and
  every date/month format. On a de-DE machine the app says "50.25" for the price and "50,25" for
  the RSI in one sentence. Zero tests run under a non-invariant culture.

### Ship-blockers — UI and accessibility

The pattern here is different from the rest of the audit and worth naming separately. The *contract*
enforcement — does the modal publish the right event? — is automated by `ModalContractScanTests` and
holds. The *experience* enforcement — does focus move, is it trapped, is the state announced, is it
readable — is entirely manual and has not held. The tell is
`BlazorTestHarness.cs:164`, which stubs `accessibleTrader.focusElement` to a no-op, making it
impossible for any test to notice a focus bug.

- [x] **FIXED 2026-08-21 — Space cannot activate any button in the application.** VERIFIED. `keyboard.js:107` puts
  `' '` in `trappedKeys`; `:137` excludes only `INPUT`/`TEXTAREA`/`SELECT` from the trap, not
  `BUTTON`; and the chart-focus escape hatch at `:150` is gated on
  `/^[a-zA-Z0-9,.]$/`, which does not match a space. So `e.preventDefault()` at `:154` runs on
  Space keydown for every one of the ~200 buttons in the RCL, cancelling the activation click.
  Enter still works (`Enter` is not trapped), which is why this survived — and NVDA/JAWS in *browse*
  mode synthesize a click rather than a real keypress, so they are unaffected. The people who hit it
  are anyone in focus/forms mode, keyboard-only sighted users, switch access, and voice control,
  for whom Space is the habitual activation key. Also affects `<summary>` disclosures in Help and My
  Data. Fix: add `BUTTON`/`A`/`SUMMARY` and the `role=button|checkbox|switch|menuitem|tab|option|
  treeitem` set to the exclusion test, or drop `' '` and gate playback on `_chartFocused`.
  - **Fixed** by bailing out of the trap before `preventDefault()` when the target is something
    Space actually activates — `BUTTON`, `SUMMARY`, and the `role=` widget set. Scoped to
    *unmodified, unshifted* Space, the exact combination the browser activates on, so Ctrl+Space
    (PlayPause) and Shift+Space (PlaySeries) still fire from anywhere.
  - **`A` was deliberately left out**, against the suggestion above: Space does not activate a
    link in any browser — it scrolls — so excluding links would cost the chart-play shortcut and
    buy no activation. A disabled ARIA widget is likewise left to the shortcut, since it
    activates nothing.
  - **The reason nothing caught this is closed too.** No C# test can observe a JS
    `preventDefault`, so the blind spot was structural. `tools/jstests/keyboard-tests.mjs` loads
    `keyboard.js` into a vm sandbox, fires synthetic keydowns and asserts on both the
    preventDefault and the .NET bridge call; 4 of its 13 tests fail if the guard is removed. It
    runs in CI beside the gesture suite.
- [x] **FIXED 2026-08-21 — Five of the six Settings tabs cannot be reached by keyboard.** VERIFIED.
  `SettingsModal.razor:73-101` implements the WAI-ARIA roving tabindex (`tabindex="@(active ? 0 :
  -1)"`) — correct *only* if the component also handles Left/Right/Home/End. It does not, and
  **no `role="tablist"` in the RCL has any arrow-key handler**: there are 8 tablists, and the 7
  files containing `@onkeydown` are LabelText, SaveWorkspace, CustomScripts, LoadWorkspace,
  SoundDesigner, ChartArea and TabBar — none of them. So Appearance (theme, text size, colour-vision
  palette, hollow candles), Keyboard (the entire rebinding UI), Alerts (SMTP/Telegram/webhook),
  License and About are mouse-only. The settings search box is the sole workaround and it covers
  only the 24 hardcoded registry rows. Fix: add the arrow handler, or drop the roving tabindex so
  all six are plain Tab stops (which is what StrategyModal/Properties/TradingDashboard already do).
  - **Fixed by adding the arrow handler**, not by dropping the roving tabindex — the roving
    tabindex is the correct WAI-ARIA pattern and the markup had already promised it.
  - **Done for all eight tablists, not just Settings.** The audit was right that no modal tablist
    had an arrow handler, but the severity was concentrated: only Settings set a roving tabindex,
    so only Settings was actually unreachable; the other six left every tab a plain Tab stop, and
    `TabBar` was already correct (container `tabindex="0"` + `aria-activedescendant` + its own
    handler). Fixing only Settings would have left seven tablists one attribute away from the same
    bug. The rule now lives in `Core/Services/Accessibility/TablistNavigator.cs`, with
    `ModalBase.NavigateTablistAsync` doing the focus move, and a scan asserts every
    `role="tablist"` in the RCL has an `@onkeydown`.
- [x] **FIXED 2026-08-21 — Pressing Enter to save a workspace kills the session.** VERIFIED.
  `SaveWorkspaceModal.razor:98-104` — `await Task.Run(() => Save())`, and `Save()` calls `Close()`
  → `ModalBase.CloseModal()` → `StateHasChanged()`, which asserts dispatcher affinity and throws
  `InvalidOperationException` off the thread pool. On the WebHost an unhandled exception from an
  `async Task` handler is fatal to the circuit: chart, tabs and unsaved layout all gone, with no
  spoken explanation. The `@onclick` path is fine — **only the Enter key is broken**, i.e. exactly
  the path a keyboard-only user takes. `Save()` is millisecond-scale file I/O; delete the `Task.Run`.
- [x] **FIXED 2026-08-21 — Five components bind `aria-selected` to a bare C# `bool`**, so no tab in them ever reports
  as selected. `StrategyModal` (6 tabs), `WatchlistModal` (3), `LevelReportModal` (2),
  `AssetDossierModal`, `MyDataModal`. `RenderTreeBuilder.AddAttribute(int, string, bool)` on an
  element omits the attribute when false and emits it *valueless* when true; an empty string is not
  a valid `true|false` token, so AT falls back to the role default of `false`. TabBar,
  SettingsModal, PropertiesModal and TradingDashboardModal all use the correct
  `? "true" : "false"` ternary — so this is drift, not ignorance. A screen-reader user in the
  Strategy Manager hears no "selected" on any tab and has no other cue, because the visual cue is a
  background colour. Add a `ModalContractScanTests` assertion that no `aria-` attribute is bound to
  a bare boolean.
- [x] **FIXED 2026-08-21 — Every dialog heading and form label is black ink on a dark panel.** `app.css:425,430,515,534`
  pin `color: #111` on `.modal-content h2`, `.modal-content label`, `.object-tree-item` and
  `.shortcuts-table` against `--bg-surface: #2b2f36` — roughly 1.2:1. The comment 23 lines above at
  `:402` says the fix already happened: *"Dialogs take the theme like everything else. They **were**
  a fixed #f2f2f2 with #111 ink."* The surface moved to `var(--bg-surface)`; these four
  more-specific rules were left behind. Live instances include the API-key profile nickname, the
  drawing-tools list, seven Help tables and the whole Keyboard-rebinding table — and
  `.shortcuts-table` also hardcodes light `th`/`nth-child(even)` backgrounds, so rows alternate
  readable and unreadable. Screen-reader users are unaffected, which is precisely why it survived.
  This is the low-vision half of the audience. Fix: `var(--text-on-surface)`, and add a CSS scan
  test for literal hex colours inside `.modal-content`.
  - **Fixed in both stylesheets** — there are two copies of `app.css` (MAUI client and WebHost) and
    they have already drifted, so the scan checks both. The ink, the rules and BOTH highlight fills
    moved together: recolouring only the text would have swapped one unreadable pairing for another
    (light ink on the pale-blue `#e0eeff` focus fill), which is how a half-done contrast fix passes
    a spot check and fails on the row the user is standing on.
  - **The scan found a fifth site the audit missed:** `.modal-content button.primary` pinned
    `color: #0c0f14` — near-black ink on `var(--accent-color)`. Correct only while the accent stays
    light, and the accent is a theme value, so a dark accent made the label of the most consequential
    button in every dialog unreadable. Fixed properly rather than exempted: `ThemeCssBridge` now
    emits `--text-on-accent`, chosen by luminance exactly as `FocusRingFor` already does, with a
    per-theme test asserting the separation. The default theme renders identically.
- [ ] **The Object Tree labels its buttons with the state they are leaving.**
  `ObjectTreeModal.razor:76,109` render `@(series.IsVisible ? "Show" : "Hide")` — so a *visible*
  series shows a button reading "Show" — and `:83,115` render `@(series.IsMuted ? "Mute" : "Sound")`.
  `aria-pressed` is separately inverted, so a user cross-checking gets a contradictory second signal.
  The buttons have no `aria-label`, so the visible text *is* the accessible name. `IndicatorBar.razor:28`
  gets it right. Also strip the orphan `U+FE0F` variation selectors left inside `"Show️"` and
  `"Delete️"`.
- [ ] **Escape leaves the Sound Designer on screen.** `SoundDesignerModal.razor:554-558` — `Close()`
  sets `_isVisible = false` and publishes, but omits `StateHasChanged()`, and it is reached via
  `InvokeAsync(Close)` which marshals but does not schedule a render. It is the only
  self-implementing modal missing the call. The user hears "Sound designer dialog closed" and the
  boundary earcon, the dialog stays in the DOM and the Tab order, the keyboard-scope gate now
  believes no modal is open so single-letter chart commands fire while focus sits in the patch
  editor, and a second Escape does nothing. Convert it to `@inherits ModalBase` like the other
  twelve.
- [ ] **Every feedback message is spoken twice, and F2 silences only one of the speakers.**
  `StatusBar.razor:72-78` subscribes to `FeedbackRequestEvent` and mirrors every `Message` into a
  second `role="status" aria-live="polite"` region, while `AccessibilityFeedbackCoordinator` already
  routes the same event through the assertive buffer in `MainLayout`. The status bar gates on
  nothing, so after F2 it keeps narrating with no way to stop it. It is a *visual* persistent
  indicator; drop the live-region roles.
- [ ] **The assertive double buffer fails on even-numbered batches.** `MainLayout.razor:186-194`
  flips `_activeBuffer` per `Speak`, so two calls in one dispatcher turn flip 1→2→1 and set the same
  text; Blazor coalesces the renders and the emitted DOM is byte-identical, so nothing announces.
  The comment at `:148-152` claims *"alternating between the two guarantees every message triggers
  an announcement."* It presents as "the app skipped a bar" — the exact failure this design was
  added to eliminate. Fix: append a monotonic invisible token so the text always differs. Also drop
  the redundant `role="status"` paired with `aria-live="assertive"` on both regions.
- [ ] **`role="menu"` popups are counted as open modals but are not covered by the Tab trap.**
  `keyboard.js:65` selects `[role="dialog"]` only, while `ChartContextMenu` and `DrawingContextMenu`
  publish `ModalStateChangedEvent(true, …)`. So chart commands are suppressed but Tab walks straight
  out of the menu into the toolbar *behind* a full-screen transparent overlay that intercepts every
  click. Neither menu implements the arrow-key navigation `role="menu"` requires. Extend the
  selector and add Up/Down/Home/End/Escape.
- [ ] **Keyboard rebinding cannot capture any modifier chord.** `captureNextKey` registers on
  `document` in the capture phase; `keyboard.js` registers on `window`, runs first, and calls
  `stopImmediatePropagation()` for any Ctrl/Alt chord — so the capture listener never fires, *and*
  the chord dispatches as a live command. The user sits in "press a key…" forever while bar replay
  toggles underneath them. Fix: have `captureNextKey` set a flag the main trap checks and returns
  early on, instead of racing two listeners.
- [ ] **The inline stop-loss / take-profit editor opens with `autofocus`, which browsers ignore for
  dynamically inserted elements.** `TradingDashboardModal.razor:864-874` swaps a button for an
  `<input autofocus>` in the same render; nothing calls `FocusAsync()`. The trader presses Enter to
  edit a live stop and their screen reader goes silent — focus is on `<body>`, the `aria-label`
  explaining the flow is never read, and the keydown handler is on an element that never got focus.
  Focus is lost a second time on commit or Escape. This is the moving-a-live-stop flow.
- [ ] **`LoadWorkspaceModal` arrow keys change the highlight silently.** `:96-120` implements a
  roving tabindex and Arrow handling but never moves DOM focus and has no `aria-activedescendant`,
  so the user hears nothing for each press, then Enter loads whichever workspace the invisible
  highlight reached. Same shape in `SaveWorkspaceModal` and `CustomScriptsModal` (where every
  `role="option"` carries `tabindex="0"` with no arrow handling at all).
- [ ] **`OrderBookModal` rows are `@key`ed on price**, so a live depth update destroys and recreates
  the row being read and focus falls to `<body>`. On a liquid pair that is several times a second —
  reading the depth ladder, the modal's entire purpose for a blind trader, is impossible on any
  active market. Key on index and move to a `role="grid"` with arrow-key cell navigation.
- [ ] **`TabBar` nests a `<button class="tab-close">` inside a `<button role="tab">`.** Invalid HTML;
  the parser hoists the inner button out, so the tablist's owned elements become
  `tab, close-button, tab, close-button…` and the close buttons are neither `tab` nor
  `presentation`. Every close button is `tabindex="-1"` with no `aria-activedescendant`, so **there
  is no keyboard route to them at all** — Delete closes only the active tab.
- [ ] **The chart's failure state is visual-only.** `ChartArea.razor:128-149` puts "Connection
  Failed" / "Connection Stalled" and "Please check your network connection and API keys" inside an
  `aria-hidden="true"` overlay. The blind user hears "Data link: Faulted" once, if at all, then the
  chart simply stops changing, while `role="application"` keeps announcing "Press arrow keys to
  navigate bars" over a dead feed.
- [ ] **~15 modals render status text into a `role="status"` region created in the same render as
  its content** — a pattern NVDA and JAWS reliably do not announce. Backtest results, OCO placement,
  import outcomes and the order-book load failure are all affected. The codebase uses the correct
  `role="alert"` form in four places and `AddIndicatorModal:59-62` demonstrates the
  always-present pattern with a comment, so this is inconsistency rather than ignorance.
- [ ] **`TradingDashboardModal.razor:828` is an `async void` timer callback** whose
  `await RefreshBookAsync()` is outside the try/catch. A transient 429 or an unsupported-symbol
  throw faults a task on the thread pool: on MAUI that reaches the unhandled handler and the process
  dies, on the WebHost it kills the circuit — while a blind trader is watching an open position.
- [ ] **`Toolbar.razor:405-431`'s shape-change confirmation is a `role="alertdialog"` that publishes
  no `ModalStateChangedEvent`, moves no focus and is not Escape-closable** — and
  `ModalContractScanTests` scans for `role="dialog"` only, so it is invisible to the scanner. A live
  example of the creative evasion the scanner exists to catch. On MAUI the SkiaSharp canvas is never
  hidden, so it draws *underneath* the chart.
- [ ] **Silent early returns.** `TradingDashboardModal:788` returns before `_isVisible = true` when
  no chart is loaded, so Alt+T produces no dialog, no earcon and no speech; same in
  `PropertiesModal:713` (P with no series selected) and `DrawingContextMenu:88`. There is a house
  rule about exactly this, cited at `DrawingContextMenu:151`, that these three paths miss.
- [ ] **Adding or deleting an alert produces no feedback and destroys focus.**
  `AlertsModal:205-252` mutates and re-renders with no announcement; deleting the focused `<li>`
  drops focus to `<body>`. `ApiKeysModal:409-429` has the same shape and **no confirmation prompt
  before deleting a credential profile**.
- [ ] **Label-in-name violations throughout.** `Toolbar.razor:181-190` pairs
  `<label for="market-select">Market:</label>` with `aria-label="Select market"`; the `aria-label`
  wins, so the accessible name does not contain the visible label. WCAG 2.5.3, and it breaks
  Dragon/Voice Control ("click Market"). Same at three more toolbar sites, throughout
  `TradingDashboardModal`, and in `IndicatorBar`.
- [ ] **MAUI: `MainPage.xaml.cs:61-129` subscribes on every `OnHandlerChanged` and unsubscribes only
  in `OnDisappearing`.** `OnHandlerChanged` also fires on handler *disconnect*, and nothing
  re-subscribes on `OnAppearing` — so a background/foreground cycle on Android or iOS leaves the
  Skia canvas permanently frozen at the last painted frame while the store keeps ticking. Repeated
  handler changes leak subscriptions onto the singleton bus, invalidating the canvas N times per
  state change.
- [ ] **MAUI: the Android hardware-keyboard bridge is dead for navigation keys and double-dispatches
  everything else.** `MainActivity.cs:20` does `e.KeyCode.ToString().Replace("Keycode", "")` — but
  `Android.Views.Keycode` members render as `DpadLeft`, `MoveHome`, `PageUp`, never prefixed, so the
  `Replace` is a no-op and `"DPADLEFT"` matches no shortcut. It also calls `IInputService.ProcessKey`
  **directly**, bypassing `GlobalInputService`'s 50 ms dedupe, while keyboard.js forwards the same
  physical press through the deduped path — so letter keys can fire twice (hide then show = silent
  no-op). `GlobalInputService.NormalizeKey` already has the right mapping table. The iOS/MacCatalyst
  `KeyboardPageHandler.cs:79` has the same un-deduped direct dispatch.
- [ ] **MAUI: `Platforms/Windows/App.xaml.cs:12` marks every WinUI unhandled exception
  `Handled = true`** and continues in an undefined state.
- [ ] **MAUI: `TrayIconService.cs` is the highest compile risk in the tree.** Behind `#if TRAY_ICON`,
  which the csproj turns **on by default** for the Windows TFM, and the only file depending on
  `H.NotifyIcon.Maui` 2.3.0 API shapes that changed between 2.0 and 2.1. Its own header calls it
  EXPERIMENTAL and unverified. Treat a clean Windows build as unproven until CI runs it.
- [ ] **MAUI: `BlazorAudioDriver.cs:205-213` (Android) exits its PCM push loop on
  `PlayState != Playing` with no restart path** — a transient underrun silences all sonification for
  the rest of the session.
- [ ] **Two copies of `app.css`** (763 and 782 lines) differing by exactly two hunks — and the
  divergence proves a fix has already been applied to only one. Every fix, including the `#111`
  contrast bug, must be made twice. Move the shared body into the RCL's `wwwroot`.
- [ ] **`.sr-only` is defined only inside `TradingDashboardModal`'s `<style>` block** but used by
  `StrategyModal` and `Toolbar`. On the WebHost with `AllowTrading == false` that modal is not in
  the render tree, so the class is undefined and three visually-hidden captions render **visibly**.
- [ ] **Three independent modal-open counters** (`keyboard.js`, `CommandDispatcher`,
  `MainPage.xaml.cs`) all driven from the same event, all drifting independently if any modal
  double-publishes — and nothing guards against a second open event while already visible.
- [ ] **`app.css:760`'s touch-target bump targets `.tab-btn`; `TabBar` emits `tab-button`.** The
  44 px minimum for workspace tabs has never applied.
- [ ] **`role="dialog"` markup is copy-pasted 25 times.** `ModalBase` captures the *event* contract
  but not the *markup* contract, which is why the markup drifted into five different `aria-selected`
  encodings, one missing render, and one dialog that escapes the scanner. A `<ModalShell>` render
  fragment plus a shared `<TabStrip>` would make the focus/trap/announce behaviour uniform by
  construction. God-modal status: logic extraction is real and working (`AlertTestSender`,
  `WebhookAlertConfigLoader`, `QuickScreenBuilder`), but markup decomposition has been done exactly
  once — `BuildSetupTab` — and stopped. SettingsModal is 2137 lines, TradingDashboard 1403,
  Properties 1182, Strategy 1127, Watchlist 1056.

### Ship-blockers — trading providers and the SDK order contract

These are the real-money paths on the desktop build. The root cause is one thing, stated at the end
of this block: **the order contract was never documented where a provider author would read it,
never enforced by a type, and never checked by a test** — so sixteen providers each guessed, and the
guesses are invisible until money moves.

- [ ] **`OrderStatus.Triggered` is produced by four providers and consumed by nobody.** VERIFIED —
  the enum is `{ PartialFill, Filled, Cancelled, Rejected, Triggered }` and
  `GeneralOrderService.PublishOrderEvent` has cases for Filled / PartialFill / Rejected / Cancelled
  and **no `Triggered` case and no `default`**. Kraken, Coinbase, Alpaca and InteractiveBrokers all
  use `_ => OrderStatus.Triggered` as their fallback arm, so every venue status those four do not
  recognise — Coinbase `FAILED`, Alpaca `expired`/`replaced`/`stopped`, Kraken `new`/`pending_new` —
  is routed into a value that is silently discarded: no event, no log, no announcement. The trader
  cannot distinguish "still working" from "refused". Fix: delete `Triggered` (the trigger fact
  already lives in `StopTriggered`/`TakeProfitTriggered`), add `New`/`Expired`/`Replaced`/`Unknown`,
  and make the switch exhaustive with a logged default.
- [ ] **Neither order enum has `Expired` or `Replaced`, and the squashes are dangerous.** Schwab maps
  `"REPLACED"` to `Cancelled` — but replaced means the order is **still live under a new id**, so the
  trader hears "cancelled", believes they are flat, re-enters, and is now double-sized with the
  original still resting. MEXC maps protobuf status 5 (`PARTIALLY_FILLED_CANCELED`) to `Cancelled`,
  hiding a live position that was opened before the cancel. Binance maps `EXPIRED` to `Rejected`.
- [ ] **Coinbase announces a freshly-accepted resting limit order as a partial fill of zero.**
  VERIFIED — `CoinbaseProvider.cs:293` is `"OPEN" => OrderStatus.PartialFill`, and Coinbase's `user`
  channel sends `status: "OPEN"` with `filled_size: "0"` the instant an order rests.
  `GeneralOrderService:143` then publishes `OrderPartialFillEvent`. Place a limit order, immediately
  hear "partially filled".
- [ ] **Coinbase parses every number with the ambient culture — 22 sites including fill quantity and
  fill price.** VERIFIED at `:269-271`: `double.TryParse(o["filled_size"]?.ToString(), out …)` with
  no `IFormatProvider`. On de-DE/fr-FR/pt-BR/ru-RU/tr-TR, `TryParse("0.5")` reads `.` as a group
  separator and returns **5.0**. A blind trader fills 0.5 BTC and hears "filled 5 BTC"; every
  Coinbase candle and balance is 10× or 100× wrong on roughly half the world's locales, silently.
  Kraken already does this correctly — copy it. (`f["size"]?.Value<double>()` is safe; it is
  specifically the `JToken → ToString() → double.Parse` pattern that breaks.)
- [ ] **Three providers serialize order quantity and price with the machine's decimal separator.**
  `BitstampProvider.cs:632-638`, `CoinbaseProvider.cs:565-589`, `AlpacaProvider.cs:716`. On any
  comma-decimal locale the terminal cannot place a single order on those venues; a lenient parser
  reads `0,50000000` as `50000000`. Kraken, Tradier, Oanda, KrakenFutures, Gemini and MEXC all pass
  `InvariantCulture` at the equivalent sites — inconsistency, not ignorance, and no test pins it.
  Add a `Wire.Num(double, int)` SDK helper plus a CI grep guard over `Plugins/Providers`.
- [ ] **`BitstampProvider.cs:634,638` rounds every limit price to two decimals** regardless of
  instrument. A limit on XRP/USD at 0.4567 goes out at `0.46`; on a sub-cent pair it becomes `0.00`.
  A wrong order placed, silently, with a real order id returned.
- [ ] **`InteractiveBrokersProvider.cs:589` can place an order against the wrong instrument.**
  `_currentConId ?? await ResolveConIdAsync(signal.Symbol, …)` — `_currentConId` is the *currently
  charted* symbol and is never compared against `signal.Symbol`. Chart AAPL, order MSFT from the
  panel, buy AAPL. No error; a real order id comes back.
- [ ] **`MexcProvider.cs:657` turns a protective stop into an immediate market order.** The futures
  branch is `type = isLimitFamily ? 1 : 5`, and `grep TriggerPrice` across the MEXC plugin returns
  nothing. The spot path guards and refuses; the futures path does not. A stop placed at 90,000 with
  spot at 100,000 sells **now at 100,000** — the trader is flattened instead of protected and the
  terminal reports success. And `:656` maps side as `Buy ? 1 : 3` (open long / **open short**)
  without reading `ReduceOnly`, so a sell-to-close opens an opposing short in hedge mode.
- [ ] **`TradierProvider.cs:815,880` truncates quantity to an int.** `((int)signal.Quantity)` — a
  risk sizer emitting 9.7 shares places 9; a fractional 0.6 becomes **0**, which
  `GeneralOrderService`'s `IsFinitePositive` validation passes. Tradier equities are whole-share
  only, so silent truncation is never the right answer — refuse instead.
- [ ] **Every Schwab order is announced as placed and its fill is never announced.** VERIFIED —
  `SchwabProvider.cs:687` returns the literal `"ORDER_SUBMITTED"` (the real id is in the `Location`
  header, which `SendWithAuthAsync` discards), and `IsErrorSentinel` is a **prefix** test on
  `"ORDER_"`. Schwab declares `SupportsOrderEventStreaming => false` and
  `SupportsOrderStatusQuery => true` precisely so the poller resolves fills — and the poller is
  gated on `!IsErrorSentinel(result)`, so it never starts. The protective-order verification net is
  skipped for the same reason. Nine other providers use the same `?? "ORDER_SUBMITTED"` fallback.
- [ ] **`GeminiProvider.cs:410-415` announces a partially-filled-then-cancelled order as
  "cancelled".** The ternary ladder tests `cancelled` **before** `executed > 0`. Gemini has no native
  market order, so `OrderType.Market` is emulated as an IOC limit — the default order type on this
  venue is the one most likely to partially fill and cancel. The trader owns coins and hears
  "cancelled". This is the exact bug class that shipped once already on Tradier/Schwab.
- [ ] **`catch { return new(); }` on trading reads re-arms the reconciliation incident
  `ProviderResult.cs:8-16` documents as fixed.** `GeneralOrderService:720` classifies failure purely
  by whether the provider *threw*, and `TradingReconciliationCoordinator:155` guards on
  `!positions.IsOk`. Swallowers found in Kraken (4), Coinbase (4), Oanda (5), Binance (4), Bitstamp
  (2), IB (2), TwelveData (2), FMP (3), MEXC (1). A 3-second 502 on a positions fetch reproduces the
  recorded incident verbatim — "announced every position as 'closed while you were away', and then
  overwrote the snapshot with the empty result" — and the guard never fires because nothing threw.
  Gemini and KrakenFutures let exceptions propagate and are the model.
- [ ] **`BinanceProvider.cs:1023` writes the user-data listenKey into the spoken error stream.**
  `$"Binance socket {uri.AbsolutePath} error…"` where `AbsolutePath` is `/ws/<listenKey>`. Any socket
  hiccup publishes a credential granting 60 minutes of read access to order and balance events, and
  `LiveStreamManager` routes it to the error coordinator, which speaks and logs it.
- [ ] **`BinanceProvider.cs:127` — `_isTestnet = tn == "true"` is a case-sensitive compare.** A config
  emitting `"True"` (the .NET `bool.ToString()` default) leaves testnet off, so orders the user
  believes are paper go to the real book. Oanda has the mirror-image defect: `:127-132` has no
  `else`, so once switched to live, a later practice config leaves the live URLs in place.
- [ ] **`BinanceProvider.cs:190-202` never recreates an expired listenKey**, and `:104` keeps
  reporting `SupportsOrderEventStreaming => true` because `_listenKey` is non-empty — so fills stop
  announcing permanently, polling never starts, and nothing says so. Binance is also the only one of
  16 not using `ReconnectingWebSocket`: its hand-rolled 37-line replacement (`:994-1030`) has no
  staleness watchdog, no frame cap, and a bare `catch` that swallows a `KeyNotFoundException` from
  an unexpected `executionReport` variant — dropping a fill with zero trace.
- [ ] **`BinanceProvider` hardcodes the SPOT base URL for cancel, open-orders, fills and depth**
  while `Capabilities` declares `FuturesTrading`. **A futures order placed through this terminal
  cannot be cancelled through this terminal** (`-2011 Unknown order sent` → `return false`), the
  futures open-orders list is always empty, and the futures user-data stream is never opened.
- [x] **PARTLY FIXED 2026-08-21 — Binance and Oanda attach a stop loss only to MARKET entries** and
  silently drop it on limit entries, while both venues accept it. Limit entry with both legs: the
  target attaches, the stop does not, no message, position live and naked. Binance's loud "POSITION
  UNPROTECTED" path is never reached because the attach never runs. IB and Tradier-options drop
  protective legs the same way. (Same defect class as the paper broker's bracket bug above — four
  venues, one shape.)
  - **Oanda and Binance: fixed.** `stopLossOnFill` / the reduce-only `STOP_MARKET` attach now run
    for limit entries as well as market ones. Both also gained the entry-trigger disambiguation the
    paper broker needed — on a stop/TP entry, `StopLoss`/`TakeProfit` is the entry's own trigger and
    must not also become a protective leg at the same price. Binance's trailing attaches were gated
    on `MARKET` by copy-paste and are now ungated (a trailing distance is never an entry trigger).
  - **IB and Tradier-options: made loud, not fixed.** Both now return `ORDER_FAILED` with spoken
    text rather than placing a naked position. Neither builds brackets yet: IBKR needs a
    parent/child OCA structure and Tradier needs OTOCO with option legs. **Those two are still
    open** — see the new item below. The refusal is deliberately scoped to the capabilities each
    provider actually declares; reading a field only in order to refuse it reads to
    `ProviderCapabilityAudit` as evidence the capability is implemented.
- [ ] **IBKR and Tradier-options still cannot attach protective legs at all.** Both declare
  `SupportsStopLoss`/`SupportsTakeProfit`, so the dashboard renders the fields; both now refuse the
  order rather than dropping the legs silently. Real fix: build the IBKR parent/child OCA bracket,
  and extend `PlaceBracketAsync` to emit OTOCO with option legs. Until then the declared capability
  is honest only because the refusal is audible.
- [ ] **`OandaProvider.cs:330-348` fabricates a symbol and a side on cancel** — a cancelled *sell* on
  EUR/USD is announced as a cancelled *buy* on an empty symbol — never reports rejections at all,
  and hardcodes `RemainingQuantity: 0` so partial fills announce as complete. `:590-607` also reports
  short positions with a **positive** quantity, so a 10,000-unit short reads identically to a
  10,000-unit long in every risk calculation and every spoken summary.
- [ ] **`KrakenProvider.cs:1170 vs :1190` — the signed string is not the string sent.** The signature
  is built with `Uri.EscapeDataString` on values only; the body is `FormUrlEncodedContent`, which
  escapes keys too and renders space as `+`. So every bracketed market order (`close[ordertype]`) and
  every multi-word-network deposit lookup (`"Tether USD (TRC20)"`) fails with `EAPI:Invalid
  signature`. `KrakenFuturesAuth` gets this right and says why: *"The SAME encoded string must be
  signed and sent."* **And the test that should catch it normalizes it away** —
  `BrokerParityTests:246` calls `Uri.UnescapeDataString` on the captured body before asserting.
- [ ] **`KrakenProvider.cs:1201-1212` hardcodes a 3-character quote**, so `"BTCUSDT"` becomes
  `"BTCU/SDT"`. The WS accepts the subscribe and never sends data — no error, chart just empty.
  `SymbolFormat.Slashed` exists and is not used. Related: `SymbolFormat.KnownQuotes` is only 8
  entries and returns the whole symbol on a miss, which is why MEXC produces `BTCTUSD` and why Oanda
  cannot use the helper at all (no JPY/AUD/CAD/CHF).
- [ ] **`CryptoAddressValidator.cs:71-84` refuses valid Tron and Lightning deposit addresses.**
  Network matching is `net.Contains(name)`, and **`"TETHER USD (TRC20)"` contains `"ETH"`** — so a
  TRC20 address is sent to the EVM hex validator, fails, and the wallet refuses to display a
  correct address. `"BTC Lightning"` contains `"BTC"`, so `lnbc1…` invoices fail the `1`/`3`/`bc1`
  check. Kraken explicitly issues both.
- [ ] **`CoinbaseProvider.cs:145,685-694` builds the WebSocket JWT from a host string**, producing
  `uri = "GET api.coinbase.com/advanced-trade-ws.coinbase.com"` and no `nonce` claim. The `user`
  channel subscription is rejected server-side — and Coinbase does not override
  `SupportsOrderEventStreaming`, so it defaults `true` and the poller never runs. **Coinbase fills
  are announced by no path at all.**
- [ ] **Six providers leave `SupportsOrderEventStreaming` at its `true` default while their push
  channel can be dead** — Coinbase, Kraken, IB, Bitstamp, Oanda, Alpaca. Kraken's auth socket is
  best-effort and its own catch block reports *"order execution updates won't be delivered"* — while
  the flag still says `true`, so the order service does not poll. The provider knows the stream is
  dead and the contract cannot express it. The flag appears in **none** of the three authoring docs.
- [ ] **20 sites across 5 providers format dates into request URLs with the ambient culture** —
  including the **calendar**. Under th-TH, 2026 renders as 2569; every range request is nonsense,
  the venue returns nothing, and the chart is blank with no error. Tradier ×4, FMP ×12, Schwab ×2,
  Alpaca ×2, Oanda ×2, TwelveData ×2. Same root cause in `TimestampParser.cs:21`, the SDK's *shared*
  parser, which passes `null` (= `CurrentCulture`) and returns `DateTime.MinValue` on failure with
  no failure channel.
- [ ] **`TwelveDataProvider.cs:231` — the Tradier/FMP intraday timezone bug, unfixed.** No
  `&timezone=UTC`, so the venue returns exchange-local wall clock which `AssumeUniversal` then
  declares to be UTC. Every AAPL 5-minute bar sits 4–5 hours out of session, and the candles look
  plausible so nothing signals it. Conversely `FmpProvider.cs:341-346` applies the Eastern-time fix
  to **crypto and forex**, which FMP returns in UTC — the fix created a symmetrical instance of the
  bug it fixed, in the opposite direction, on four of five market types.
- [ ] **`ReconnectingWebSocket` — three defects in the SDK's shared socket.** `SendAsync` is
  documented "safe to call from any thread" and is not (`ClientWebSocket` forbids concurrent sends;
  the heartbeat timer races provider subscribes), and it silently `return`s on a non-open socket so a
  subscribe issued during a reconnect window **vanishes** and the socket ends up healthy and
  subscribed to nothing. `:162-166` gives up permanently after `_maxReconnectAttempts` **without
  invoking `_onDisconnected`**, so after a 15-minute outage the chart looks live and never ticks.
- [ ] **`KrakenFuturesProvider.cs:367` silently turns an IOC request into a GTC order** — it sets
  `triggerSignal` (which selects *which price* triggers a stop) instead of `orderType: "ioc"`, while
  declaring the `TimeInForce` capability so the dashboard renders the control. More broadly,
  `TimeInForce` is a free-form `string?` and seven of ten providers substitute their own hardcoded
  value: a Day order that survives overnight through a gap, or a GTC protective order Schwab kills at
  16:00 leaving the position unhedged.
- [ ] **`InteractiveBrokersProvider.cs:649-668` auto-confirms every IBKR risk warning** — up to 8 in
  a chain, including "price is more than X% away from the market" and "size exceeds…". Announcing
  after the fact is not consent, and for a blind trader that announcement was the only channel.
- [ ] **`PolygonProvider.cs:126-131` hardcodes the 15-minute-delayed WebSocket** while
  `Environment => Live`. A paying subscriber acts on quarter-hour-old prices believing they are live;
  crypto and forex live data never arrive at all (the REST ticker shape is pushed into WS channels
  that use a different shape, and there is no status-frame handler to notice the rejection).
- [ ] **`FinnhubProvider.cs:155` processes only the last trade in each batched frame**
  (`var trade = data.Last!;`) with `LiveTickStyle.CumulativeBars`, so live volume on a busy symbol is
  off by an order of magnitude — and volume is sonified.
- [ ] **Capability flags declared with nothing behind them.** IB declares `L2` and does not implement
  `IOrderBookProvider` at all. Polygon and Alpaca return a subject that is never `OnNext`-ed (worse
  than empty — it never completes). IB and Oanda declare `Leverage` with a `SetLeverageAsync` that
  echoes the requested value without calling anything: the trader sets 4×, reads back "4", and the
  venue was never told. Binance declares `MarginTrading` with zero `/sapi/v1/margin` references.
  `KrakenFuturesProvider.cs:410-411` names this exact anti-pattern — *"the no-op that the capability
  audit exists to catch"* — and `ProviderCapabilities.cs:9-12` states the contract: *"A flag is
  therefore a promise to a user."*
- [ ] **`BaseMarketDataProvider.GetCapability<T>` returns null for everything except market data**,
  and `PROVIDER_AUTHORING.md:114` says it returns `this`. An author implements `ITradingProvider`
  exactly as `SDK_GUIDE.md` §5.2 instructs, forgets the override, and trading is silently invisible
  to the host. Fix is one line: `if (this is T t) return t;`.
- [ ] **`PlaceOrderAsync` returns a bare `string` and the `ORDER_FAILED:` protocol is documented
  nowhere on it** — `grep ORDER_FAILED AccessibleTrader.Sdk` returns one line, on a *different*
  interface. Two disagreeing recognisers exist in Core: `IsErrorSentinel` (prefix) and
  `OrderResult.DescribeFailure` (exact-match list). `DescribeFailure("PROVIDER_NOT_CONFIGURED")`
  returns null — "it went" — so `QuickTradeExecutor` and the dashboard announce success for an order
  that was never sent, on nine providers that return that sentinel. Fix: return a typed
  `OrderPlacement` record.
- [ ] **No `CancellationToken` on `IMarketDataProvider` or `ITradingProvider`**, while
  `IWalletProvider` and `IWithdrawalProvider` take one on every method. A 5000-bar backfill cannot be
  cancelled on symbol switch and its bars race the new symbol's into the same buffer. Adding
  `CancellationToken ct = default` keeps every implementation source-compatible.
- [ ] **`BarBucketConsolidator.cs:52` drops every negative-valued tick** (`tick.Open > 0 && …`), so
  negative funding-rate series freeze at the last positive print with no error — while
  `MarketType.Derivatives` is documented as "funding rates, open interest, basis" and
  `SymbolRenderHints.cs:65` says "funding can be negative".
- [ ] **`docs/PLUGIN_AUTHORING.md` documents `GetDefaultLevels` with the wrong return type in six
  places, and the mismatch compiles.** The doc shows a tuple list; the SDK returns
  `List<LevelDescriptor>`. Because it is a *default interface method*, the tuple version adds an
  unrelated method and the DIM silently wins — so the quick-start ships an indicator whose
  Overbought/Zero/Oversold lines **never appear and never sound**, then falls through to the Skender
  lookup so a custom indicator inherits *RSI's* levels. For a blind user those levels are the
  earcons that convey position in range. The doc's own line 5 claims "All APIs described here are
  taken directly from the current source code."
- [ ] **Four documented provider samples do not compile** (CS8139 — overriding a named tuple with an
  unnamed one), `SDK_GUIDE.md` §5.2 teaches the member-hiding anti-pattern the SDK deliberately
  removed (`SupportsMarginTrading` and friends are non-virtual and flag-derived by design), and the
  sample epoch conversion `new DateTimeOffset(b.Date).ToUnixTimeMilliseconds()` is timezone-dependent
  — it passes on a UTC dev box and shifts the volume pane against the candles for everyone else.
- [ ] **The three authoring docs cover indicators and data providers only.** Verified absent from
  `PROVIDER_AUTHORING.md`: `ITradingProvider`, `PlaceOrderAsync`, `ORDER_FAILED`, `OrderStatus`,
  `ProviderCapabilities`, `IWalletProvider`, `IWithdrawalProvider`, `RestSigning`, `SymbolFormat`,
  `ReconnectingWebSocket`, `SurfaceError`, `SupportsOrderEventStreaming`, `InvariantCulture`.
  **This is the root cause of the provider defects above, not a separate finding.** `SDK_GUIDE.md`
  tells authors to "push every order event onto `OrderUpdateStream`" and never mentions the flag
  that decides whether the app polls — which is exactly why six providers left it defaulted with a
  broken push channel. `LiveTickStyle` appears in no authoring doc despite the SDK saying providers
  "MUST override" it.
- [ ] **SDK helper adoption is near zero where it matters.** `RestSigning`: 1 of 16 (MEXC).
  `SymbolFormat`: 1. `TimestampParser`: 1 (Tradier). `ExchangeTime`: 1 (FMP).
  `ReconnectingWebSocket`: 9 (Binance hand-rolls). `RateLimiter`: 14 (Gemini and KrakenFutures have
  none — Gemini's second-resolution nonce can also outrun the venue's 30-second acceptance window
  under a burst, and the failure is reported to the user as a *credential* problem). The TODO entry
  naming Kraken/Bitstamp/Binance/Coinbase is **confirmed for all four and incomplete** — it omits
  Binance's hand-rolled WebSocket, which is the highest-risk duplication of the set.
- [ ] **`ProviderResult<T>` has zero callers repo-wide**, and `ProviderErrors`/`SurfaceError` has
  zero subscribers — 3 of 16 providers emit through it, the other 13 use 164 raw
  `_errorStream.OnNext` calls, so severity and category never reach the feedback layer.
- [ ] **Provider test coverage is thin exactly where money moves.** `ProviderConformanceTests` is a
  smoke test (name non-empty, `MaxBarsPerRequest > 0`, timeframes parse) over a hardcoded 14 of 16.
  `BrokerParityTests` is 13 tests: Tradier 5, Schwab 4, Kraken 2, Alpaca 1, Coinbase 1, and **zero**
  for IB, Bitstamp, Binance, MEXC, Oanda, Gemini, KrakenFutures, Polygon. Only 5 of 16 providers
  have a dedicated test file. There are **no known-answer HMAC vectors anywhere in the suite**, no
  culture tests, no symbol round-trip tests, no signed-string-equals-sent-string assertion, and no
  test that `PlaceOrderAsync`'s return value reaches the poller. Every critical in this section is
  unguarded.

### Hosted / WebHost — production risks on the live site

- [ ] **The chart freezes exactly when the market is busy.** VERIFIED. `ChartArea.razor:329`
  rate-limits rendering with `.Throttle(TimeSpan.FromMilliseconds(100))`. In Rx.NET `Throttle` is
  *debounce* — it emits only after a 100 ms quiet gap. The trigger fires on every
  `Store.StateStream` emission, and live ticks on Bitstamp/Binance/Kraken/MEXC (all hosted-enabled)
  arrive faster than that during volatility, so the timer keeps resetting and the PNG stops
  updating until the feed goes quiet. The intended operator is `Sample`. The codebase already
  knows the difference: `EventBus.SubscribeCoalesced` uses `Throttle` and documents it as
  "debouncing", `SubscribeSampled` uses `Sample`, and `TactileCanvasCoordinator:171` uses
  `Throttle` correctly and deliberately. README:39 claims "~10 fps". One-word fix.
- [ ] **Hosted background alerts silently do not fire for most alert types.** VERIFIED.
  `LocalBackgroundMonitor.DeriveWatches` filters to `a.ConditionTree == null`, so every Advanced-
  condition alert is excluded from server-side evaluation. Both monitors then evaluate with
  `WorkspaceState.Initial` and an empty indicator dictionary, so in `AlertEvaluator.TryEvaluate`
  the `Indicator` target hits `series == null → return null` and `Poc` hits `NaN → return null`.
  Only Price and Candle targets work. Nothing warns the user, and `USER_MANUAL.md:2023` says
  advanced alerts have "everything else about alerts unchanged — delivery channels, symbol scoping,
  and background tabs". A blind trader believes an alert is watching the market when it is not.
  Either evaluate them properly or refuse to save a server-side alert the server cannot evaluate,
  and say which.
- [ ] **Both background monitors hardcode `"Spot"` as the market sub-type.** VERIFIED —
  `HostedAlertMonitor:182` and `LocalBackgroundMonitor:149`, and the `Watch` record carries no
  market type at all. Alerts on Derivatives, Economic, OnChain or Sentiment — all in
  `HostedMarkets` — request the wrong sub-type; the failure is logged at Debug and swallowed. Same
  bug copy-pasted in two places.
- [ ] **SSRF from the hosted server through both alert channels.** VERIFIED.
  `WebhookAlertChannel.IsValid` accepts any absolute HTTPS URL — no host allow-list, no
  private-IP/loopback block, default redirect-following. `EmailAlertChannel` does
  `new SmtpClient(cfg.Host!, cfg.Port)` with user-supplied host and port, i.e. an arbitrary
  outbound TCP connect usable as a port scanner. Both are reachable by any registered user
  (registration is open, no email confirmation), and delivery success/failure is spoken back,
  giving a boolean oracle. `BuildAlertChannelHttpClient()` deliberately bypasses
  `PluginHostServices.CreateHttpClient` — the allow-listed factory every *provider* is required to
  use. README boasts that all providers route through it; the channels that take a user-supplied
  URL are the exception.
- [ ] **`QuickTradeEquity` is a process-wide static on a multi-user host.** `private static double
  _latest`, written by `GeneralOrderService.GetBalancesAsync` from every circuit. User A's balance
  becomes user B's sizing input, and B can infer A's account size. The doc comment's reasoning
  ("avoids a stale copy per browser tab") was written for the single-user desktop.
- [ ] **`PaperTradingProvider` is `AddScoped` on the WebHost**, so two tabs for one user get two
  independent accounts over one `paper_account.json` with no re-read and no file watch. Whichever
  instance persists last wins, and a trade made in tab A can be overwritten out of existence by a
  trailing-stop update in tab B. The desktop head is `AddSingleton` and unaffected. Fix: a
  process-wide dictionary of accounts keyed by `ICurrentUser.DataKey`.
- [ ] **`PaperTradingProvider` reads `AppDataDirectory` in its constructor**, defeating
  `UserScopedPathService`'s explicit "computed on access, after the circuit handler has set
  `ICurrentUser`" contract. Safe today only because `App.razor` sets `prerender: false`. Re-enable
  prerendering, or resolve the broker from any pre-circuit scope, and every user's paper account
  silently becomes `users/anon/paper_account.json` — one shared account for the whole site.
- [ ] **`AllowCustomScripts` and `AllowApiKeysModal` are enforced only in Razor `@if` markup.**
  `AllowLiveTrading` is properly enforced in `GeneralOrderService`; these two have no service-layer
  check. Not currently exploitable — Blazor Server will not dispatch events to a component that was
  never rendered — but server-side Roslyn is described in `DemoPolicy:203` as "RCE", and one
  refactor is all that stands between a hosted user and it.
- [ ] **A WebHost started with neither `--accounts` nor `--demo` serves `HostMode.Full` to every
  anonymous visitor** — live trading, API-keys modal and custom scripts all on. The difference
  between the hosted product and a fully-trusted local terminal is one absent command-line flag.
  Refuse to start in `Full` when the bound URL is not loopback.
- [ ] **`/diag/journal` is unauthenticated.** Mapped outside the `accountsEnabled` block with no
  `.RequireAuthorization()`, gated only on `--enable-diag` or `IsDevelopment()`. On a hosted
  instance run with that flag it is an anonymous dump of spoken-text history.
- [ ] **`ForgotPassword`'s comment claims rate-limit coverage it does not have.**
  `AuthRateLimitPolicy.IsAuthMutation` matches only `/account/login` and `/account/register` POSTs,
  so `/account/forgotpassword` falls into the general 200-per-10s tier while writing an audit record
  with an attacker-supplied email to `SecurityEventFileSink` — which opens, writes and closes a file
  under a global lock per event, and whose own comment assumes "a couple per minute at most under
  heavy load". Unauthenticated disk-fill and lock contention. `/account/loginwith2fa` and
  `/account/loginwithrecovery` are also outside the auth tier (per-account lockout does cover them).
- [ ] **The owner-seed ignores its `IdentityResult`.** `Program.cs:188` — `await
  users.CreateAsync(seedUser)` with no check, so a failed seed is silent.

### Correctness / duplication debt found by this audit

- [ ] **NOT IN THE AUDIT — `EmaFillProvider` is an empty subclass of `MACloudProvider` kept as a name
  alias, and registering both would have crashed the app at startup.** Found 2026-08-21:
  `SignalCatalog.Refresh` built its ID index with `ToDictionary`, which throws on a duplicate key,
  and both classes answer to `MA_CLOUD.MA Cloud`. Only `MACloudProvider` is registered today, so
  this was latent rather than live. The catalog now takes the first registration instead of
  throwing — a shadowed leaf is a better failure than a dead app — but the alias itself is the real
  debt: delete it and fix the three test references, or keep it and document why. Anything that
  enumerates providers reflectively (`IndicatorCausalityTests` does) has to know to skip it.
- [ ] **`VolumeProfileLevelProvider:61` silently falls back to a known future-leaking profile.**
  `bins ??= series.ProfileBins` — whenever `_backtestCache` is null or inactive (the ctor parameter
  is optional and defaults to null; `ReplayProfiles` can be false), the backtest reads the
  workspace's *current-viewport* profile at every historical bar. There is no refusal and no
  warning. Its own XML doc still calls the leak unconditional and "the most important pending S/R
  correctness item", which is now stale since the replay path exists and is default-on — so a
  reader cannot tell which statement is true. Fix the fallback to return no profile levels during a
  backtest without replay, and rewrite the comment.
- [ ] **Three candle classifiers — and the 2026-08-20 entry understates it on two counts.** That
  entry says "Not user-visible yet"; in fact `BarDetailService.ClassifyBar` (Ctrl+Shift+D) and
  `SpeechFormatter.ClassifyCandleType` (arrow-key navigation) are two live speech paths reachable on
  the same bar, and `SdkCandlePatternAnalyzer` drives `AlertEvaluator` and `CoreIndicatorProvider`.
  A user can set a "Hammer" alert on SDK rules, hear the terminal say "Hammer" on SpeechFormatter
  rules, and have the alert never fire. It is also not two labels but **eight behavioural
  differences including two different threshold values and one whole missing concept**:

  | Rule | `ClassifyBar` | `ClassifyCandleType` | `SdkCandlePatternAnalyzer` |
  |---|---|---|---|
  | `High == Low` | `"Flat"` | `""` | `Doji` |
  | Nothing matched | `"Standard Candle"` | `""` | `Normal` |
  | Marubozu threshold | `> 0.90` | `> 0.90` | **`>= 95.0`** |
  | Marubozu label | `"Marubozu"` | `"Marubozu"` / `"Bearish Marubozu"` (asymmetric) | Bullish/Bearish |
  | Dragonfly wick gate | `lower > 0.6, upper < 0.1` | same | **`> 40`, `< 8`** |
  | Long-legged doji | absent | absent | present |
  | Hammer rule | `body < .30, lower > .60, upper < .10` | same | **wick-vs-body ratios** |
  | Spinning-top wicks | `> 0.25` | same | **`> 15`** |
  | Hanging Man / Inverted Hammer | absent | absent | trend-aware |
  | Multi-bar patterns | none | none | 11, evaluated first |

  The consequential one: **any hammer-shaped bar at the top of an advance is announced as "Hammer"
  (bullish implication) by both speech paths**, while the SDK correctly returns `HangingMan` with
  `Direction = Bearish`. The SDK's own doc at `:186-189` says why that matters — *"Getting it wrong
  does not merely mislabel — it announces the opposite direction to the one the shape implies."*
  Two of the three classifiers still get it wrong. Fix: delete both private methods and route
  `BarDetailService` and `SpeechFormatter` through `ISdkCandlePatternAnalyzer` (both are already
  DI-constructed and the analyzer is already registered). Also route the SDK's hardcoded 50/30/40/
  8/25/15/0.10 constants through `CandlePatternThresholds`, which exists to hold them.
- [ ] **ATR is hand-rolled five times with different warmups.** `ChartPatternDetector.cs:675-699`
  uses a growing `sum/i` average (non-NaN from bar 1); `TopBottomDetectorProvider` and
  `PivotLevelsProvider` emit NaN until bar `period`; plus `IndicatorMath.Atr` and an inlined copy
  inside `IndicatorMath.Adx`. A tolerance expressed in ATR units means different things in different
  files. Related: WaveTrend exists in three copies (Cipher B, Cipher A, and `ValueDeviationProvider`
  with an explicit "no dependency on Cipher B" comment — which then needed its own NaN-tolerant EMA
  to fix a bug the other two still have); EMA/SMA are byte-identical across `MovingAverageHelper`
  and `IndicatorMath` **with contradictory doc comments about the same code**; rolling z-score has
  three implementations with three different warmups; forward-fill has three; MVRV band tables have
  three with three different boundary sets; and `GetInt`/`GetDbl`/`GetBool` are re-declared in ~20
  provider files with genuinely different behaviour (some `Convert.ToDouble`, which throws on a
  non-numeric string; some `TryParse`; only one null-safe).
- [ ] **`SpeechFormatter.GetPointValue`'s fallback still uses pre-rename, case-sensitive names.**
  `c.Contains("Body")`, `"Upper"`, `"Lower"`, `"Open"` — against the current machine ids
  (`upper_wick`, `lower_wick`, `body`, `line`) every one of those is false, so it returns
  `double.NaN`. This is exactly the test commit `35928149` removed from the sonifier, left in place
  here. Currently masked because `ViewportReducer` populates the arrays by `DataMapping` and the
  primary lookup succeeds — but a dropped mapping or a series built outside the reducer path and
  the wick reads "no data" with nothing failing. `ChartMath.GetComponentValue` is the corrected
  version of the same block; call it.
- [ ] **Signal polarity is classified twice and they disagree.** `NavigationSonifier.IsPositiveSignal`
  has a `PitchMapping.Direction` branch that `NavigationFeedbackManager` lacks, so for such a
  component the audio cluster order and the spoken signal order can flip relative to each other —
  while both comments claim they match. Separately, both use
  `Contains("Up", OrdinalIgnoreCase)`, which matches **"Support"**.
- [ ] **Two pan formulas.** `AudioConstants.CalculatePan` computes `k/(N-1)`;
  `LevelCrossingMonitor.ComputePan` computes `(k+0.5)/N`. The `AudioConstants` doc comment
  describes the *second* one while implementing the first, and it is load-bearing documentation for
  the audio/visual lockstep claim.
- [ ] **Order-failure classification exists three times in three non-equivalent forms.**
  `GeneralOrderService.IsErrorSentinel` (prefix match), `OrderResult.DescribeFailure` (explicit
  switch), and `TradingDashboardModal:1195`/`:1299`
  (`StartsWith("ORDER_FAILED") || StartsWith("PROVIDER_NOT")`). The third misses
  `ORDER_REJECTED_QUANTITY`, `ORDER_REJECTED_PRICE`, `ORDER_DUPLICATE_SUPPRESSED` and
  `ORDER_UNCERTAIN`, so a suppressed duplicate announces as "Order placed" — the routine
  screen-reader double-Enter case the dedup gate exists to catch. `OrderResult`'s own doc says
  "One translator, used by every path that places an order, is how that stays fixed"; two of four
  call sites do not use it.
- [ ] **"Order placed" is spoken unconditionally**, including for orders the broker refused.
  `GeneralOrderService:285-286` calls `ReportSuccess` before any sentinel check; every guard after
  it is correctly gated. A paper resting order with no price and no trigger returns a sentinel and
  emits no `OrderUpdate` at all, so the user hears "Order placed" and nothing else, ever.
- [ ] **A forced liquidation is announced as an ordinary fill.** `PaperTradingProvider` builds
  `reason: "LIQUIDATED — the short's collateral was exhausted at 200"` and
  `AccessibilityFeedbackCoordinator.FormatFill` never reads `o.Reason` (it is read only on
  rejections). The user hears "Order filled. Bought 1 BTC/USDT at 200. Loss 100." followed by two
  unexplained "Order cancelled" lines. The entire point of the 2.3.0 short model is inaudible.
- [ ] **Paper order updates are published synchronously while the account lock is held.** `Emit` →
  `_orderUpdates.OnNext` → `EventBus.Publish` runs inline inside `ProcessBar`'s `lock (_lock)`. One
  throwing subscriber aborts the fill loop mid-way — after `_open.Remove` and `ApplyFill` have
  already mutated cash and positions, before `Persist()` — leaving disk disagreeing with memory,
  and takes the paper broker's price feed down for the session.
- [ ] **`_positions`, `_collateral` and `_leverage` use the default case-sensitive comparer** while
  `_lastPrice` and `_exposureIdentity` use `OrdinalIgnoreCase`. Latent today because writers
  uppercase first, but `Load()` restores whatever keys are in the JSON, and a split position would
  give a short whose collateral and quantity live under different keys — one that can never
  liquidate.
- [ ] **Stops and take-profits fill at the trigger even when the bar gapped through it.**
  Deliberately pinned by a test with the comment "v1 simplification", but it is a systematic
  optimistic bias in the one direction that matters, on the 4h and 1d timeframes the demo exposes.
  `StopLimit` and `TakeProfitLimit` also ignore `o.Price` entirely, so a stop-limit silently
  behaves as a stop-market.
- [ ] **Realized P&L excludes fees**, so the spoken result of a trade is wrong by the round-trip
  cost — frequently a large fraction of a scalp's P&L, and wrong in sign near break-even.
- [ ] **`GetBalancesAsync` nets a short against a long in the same base asset**, so a BTC/USDT long
  of 1 and a BTC/USD short of 1 collapse to a single 0 BTC row, which `PortfolioValuationService`
  then filters out — both positions vanish from the portfolio total while `GetPositionsAsync` still
  lists them. Two account views that disagree.
- [ ] **`ChartIdentity` equality is case-sensitive** while provider resolution everywhere else is
  `OrdinalIgnoreCase` (`MarketOrchestrator.EnsureContains` was explicitly made insensitive because
  "the Economic list says 'Fred' while the plugin calls itself 'FRED'"). One casing mismatch gives
  two `ChartFeed`s for one chart, makes `MarketFeeds.IsLive` lie, and defeats the focused-tab guard
  in `BackgroundTabFeedService.Reconcile` — producing exactly the double-feed its comment exists to
  prevent.
- [ ] **`DataCacheService.Add` corrupts its own lookup index on eviction** (every surviving item's
  index shifts, only the new entry is rewritten; `AddRange` calls `RebuildLookup`, `Add` does not).
  Registered as a Singleton in both hosts with **no consumers at all** — latent corruption in dead
  code. Delete it with `BackfillManager`.
- [ ] **Dead second navigation path still exported.** `IAudioFeedbackRouter.SonifySeries` /
  `SonifyComponent` / `SonifyProfile` / `SonifyHeatmap` and `ISonificationManager.SonifySeries` /
  `SonifyComponent` have zero production call sites and all write voice slot 0 — the invariant the
  single-path redesign exists to protect. Delete them, and add a test asserting only
  `SyncNavigationSlots` emits `SetVoice(0, …)`.
- [ ] **`EnvelopeType` and `NoiseType` are free-text strings compared inconsistently.**
  `"Ping"` is matched case-sensitively at `AudioSequencer:90`, `:256` and `AudioEngine:500` and
  case-insensitively at `AudioSequencer:246`. An imported patch with `"ping"` is treated as a marker
  for the NaN guard, as continuous for the voice, and gets duration 0 — a permanent drone on a
  playback slot that never decays. `ImportPatchJson` validates nothing.
- [ ] **`AudioEngine` divides by zero for a Ping voice with `TotalDurationSamples == 0`**, putting
  NaN into the PCM buffer. Reachable through `DecayMs = 0`, a registry patch with 0, or an imported
  patch's `DurationSeconds`. `SetVoice` should reject non-finite `freq`/`vol`/`pan` and clamp
  duration — "NaN must be silent" is currently enforced only upstream in `CreateAudioPoint`, not at
  the engine boundary.

### Documentation drift

- [ ] **The doc-drift guard is RED on main right now.** `python3 scripts/check_doc_drift.py` fails:
  README claims 3227 tests, `--list-tests` reports 3314 (the suite runs 3327). The 2.3.0
  verification doc celebrated this guard going green; it has since gone red again.
- [ ] **The guard checks only the first provider-count claim.** `README_PROVIDER_RE.search(md)`
  matches line 367's "33 … (16 trading + 17 analytics)", which is correct, and never sees line 45's
  "**29 data providers** — 14 in `Plugins/Providers/` … 15 in `Plugins/Analytics/`", which is
  wrong and whose prose list omits Gemini, Kraken Futures, SEC EDGAR and Wikipedia. Widen the regex
  to `findall` and validate every occurrence.
- [ ] **README is two to three releases stale in its most prominent sections.** Line 14 still says
  "latest release: **v2.0.1**"; line 104 heads "Current Status (2026-08-01)" with 2.1.0; line 142
  says "Suite 2109 → 2836" while lines 208/370 say 3227; line 142 says 15 JS gesture tests while
  line 370 says 12; line 208 says "Two GitHub Actions workflows" when there are four (`doc-drift`,
  `plugin-manifest`, `release`, `tests`); line 208 claims "Build across all 4 TFMs: 0 errors, 0
  warnings", which `RELEASE_2.3.0_VERIFICATION.md` item 7 honestly records as false.
- [ ] **README's sandbox platform list omits Linux/bwrap** — the WebHost's own platform. It names
  Windows AppContainer, macOS `sandbox-exec` and Android `isolatedProcess` only, which reads as
  though the hosted Linux deployment has no OS sandbox. It does (`LinuxBwrapLauncher`), and it
  refuses rather than falling back.
- [ ] **`ServiceCollectionExtensions.cs:487-490` (WebHost) describes a silent unsandboxed
  fallback that no longer exists.** The comment says the Linux launcher "falls back to an
  unsandboxed `DefaultProcessLauncher` only if it isn't [installed]". `SandboxPolicy.EnforceOrThrow`
  genuinely refuses, and `ACCESSIBLETRADER_ALLOW_UNSANDBOXED_SCRIPTS=1` is the logged opt-out. A
  wrong security comment is worse than none — someone could "restore" the fallback.
  `FINALIZATION_PLAN.md` §5 item 1 still lists the old behaviour as a to-fix item too.
- [ ] **`USER_MANUAL.md:2023` overpromises hosted alerts** — see the background-alert item above.
- [ ] **No `[2.3.0]` section in CHANGES.md.** Carried from the 2026-08-20 list; still open.
- [ ] **Stale slot-layout comments across the audio layer.** `NavigationSonifier:50-55` says
  "64-voice polyphonic engine", "Slots 8-15: Reserved for future" (they hold patch layers) and
  "Slots 16-31: round-robin" (26-31 are reserved); `AudioSequencer:362` and `:565` say 32-63 and
  64-79 (actual 32-95 and 96-127, and the inline comment three lines below `:565` has the right
  numbers); `AudioEngine:231` says "permanent 64-element array" (it is 128);
  `EarconPatchPlayer:22-24` says "playback (32-63)".
- [ ] **A fourth drifted comment in `SonificationProfileProvider`** (`:69-71`), missed by
  `0320d8a3`'s sweep of three: it says body amplitude scales so "a doji is quiet and a marubozu is
  loud", which is the pre-change behaviour and the direct opposite of both the code and the comment
  three lines below it. Also `:43-45` claims the volume bed's 330 Hz "seats it under the candle
  body" — never used; and the section numbering uses 4, 5 and 7 twice each.
- [ ] **`NavigationFeedbackManager:465-471`** documents an audio proximity tone on slot 2 that the
  method does not produce and that line 524 explicitly disclaims; `:54-61` describes a "3 paths"
  design that `:92-96` and `:231-234` both say was superseded; `:342-348` is an orphaned `///`
  block for a method that moved.
- [ ] **`SoundPatchRegistry`'s `HarmonicAmount`, `HarmonicFreqMultiplier`, `GradientWaveformA/B`
  are documented in synthesis detail and never read by anything.** Both renderers hardcode
  `"triangle"`/`"sawtooth"` instead of consulting the patch.
- [ ] **`PaperTradingProvider:568-584`** carries a stacked second `<summary>` that is a stale copy
  of `CanFill`'s doc, still describing the pre-2.3.0 "cannot sell what you do not hold" rule (also
  a `CS1571` under `/doc`). `DemoPolicy:7-30` has the same double-summary shape.

### Guard tests that do not guard

The suite is genuinely strong — 3,327 cases, 2,383 `Assert.Equal` against only 205
`Assert.NotNull`, and the money paths (order validation, bracket wire payloads, paper fill
semantics, OCO cancellation) are covered better than most production codebases manage. This
section is not about that. It is about a specific, locatable set of tests that **advertise drift
protection in their docstrings and cannot provide it**, because when testing the real thing was
inconvenient — a private method, a plugin DLL, a mock farm — the logic was reimplemented in the
test and the reimplementation was asserted.

Worth saying plainly: in every case the comment is *candid* about what was done ("mirror the same
CAS loop here", "Mirrors the private `DataStateMachine.Transition` switch", "This test mirrors the
exact transform"). Nobody hid anything. But the docstrings then overclaim — *"Any drift in either
direction breaks the test — which is the point"*, *"which is exactly the safety net we want"* — and
those sentences are what a reader trusts. The tests are honest; the prose around them is not.

**Two of this class were closed 2026-08-21** by the causality work, and are recorded here because
they belong to this section even though the audit filed them elsewhere:

- [x] **`CandlePatternAnalyzerTests.ClassificationOfABarNeverChangesWhenLaterBarsArrive`** built its
  "with hindsight" list as `bars.Take(i + 6).Take(i + 1)`. Chained LINQ `Take` takes the minimum, so
  that was exactly `Take(i + 1)` — the same list — and the test compared a value to itself. Fixed,
  and it now catches something real: the analyzer measured the trend that separates a hammer from a
  hanging man from the END of the caller's list, so classifying a historical bar with full history
  loaded announced the opposite direction. Proven by reintroducing it (`Expected: Hammer, Actual:
  HangingMan`).
- [x] **`DivergenceConfirmLagTests`** said outright that the provider-level property was untested
  because a series that trips Cipher B's gates was "too brittle to construct deterministically".
  `IndicatorCausalityTests` now does that comparison empirically for every provider, and Cipher A
  has per-component pins. The stale NOTE is gone.

- [ ] **`ProviderTimeframeContractTests` asserts a hardcoded copy of the timeframe lists and never
  constructs a provider.** VERIFIED — `providerName` is a bare `string` label; the body only calls
  `TimeframeUtility.ToSeconds(tf)` on the strings declared in the test file. If a provider changed
  `"1h"` to `"1H"` — the exact typo the docstring names as the motivating bug — the test's own
  `"1h"` still parses and it passes. "Any drift in either direction breaks the test" is false in
  both directions. 30 rows of green guarding nothing.
  `EveryDeclaredTimeframe_HasNoDuplicates` asserts the test author did not type the same string
  twice. Fix: drive the theory from real `p.NativelySupportedTimeframes`.
- [ ] **`ProviderConformanceTests` calls itself the "universal contract every trading provider must
  satisfy" and enumerates 14 of 16.** VERIFIED — **Gemini and KrakenFutures are absent** from a
  hand-maintained roster with no drift guard, so neither gets the Name/Description/
  MaxBarsPerRequest/timeframe-parse/capability gate. Combined with the item above, **nothing
  anywhere validates Gemini's or KrakenFutures' declared timeframes.** This is the same failure
  `ProviderCapabilityHonestyTests:24-31` already documents being burned by — *"a sweep that does
  not enumerate everything is a spot check wearing a sweep's name"* — and that file fixed itself
  with `AllTradingProvidersAreEnumeratedHere`. Copy that assertion here, or share one roster.
- [ ] **`DataOrchestratorResilienceTests` redeclares the state machine and the Polly config inside
  the test file.** A private `Transition` switch copy backs five tests; a local
  `ConcurrentDictionary` of breakers backs three more. `DataOrchestrator` is never constructed. So
  the breaker test proves Polly keys a dictionary, not that the orchestrator keys breakers per
  provider — and **the named regression the file exists for ("one bad provider blocks all 25 for
  5 s") would not be caught** if someone refactored to a single shared breaker. The comment says
  "If the production switch changes, these tests must change with it" — if the production switch
  changes, nothing happens. Partly rescued by `StateMachineTests`, which drives the real
  orchestrator, but only on the happy path.
- [ ] **`PostAuditRegressionTests.ZeroValueFilter_MirrorsLiveStreamManagerRule`** defines the
  predicate inside the test body. Seven green cases; `LiveStreamManager` never constructed. If the
  real filter started admitting zero-close bars — corrupt ticks poisoning the chart and every
  indicator downstream — they stay green.
- [ ] **`PostAuditRegressionTests.NonceCasLoop_...`** reimplements Kraken's nonce CAS loop in the
  test and asserts the reimplementation. A duplicate nonce means rejected authenticated requests —
  order placement, cancellation and balance reads all fail under concurrency. The stated reason
  ("the real provider lives in a plugin DLL and depends on HttpClient and credentials") is
  contradicted by `BrokerParityTests:227-233`, which constructs a real `KrakenProvider` and swaps
  its `HttpClient` by reflection. The machinery is already in the suite.
- [ ] **`ProviderSymbolNormalisationTests.Coinbase_ProductId_...` asserts `string.Replace`.**
  `input.Replace("/", "-").ToUpperInvariant()` — `CoinbaseProvider` never constructed, four green
  cases asserting the BCL, on a symbol-routing money path. The comment names three real call sites
  (`GetOrderBook`, `SubscribeOrderBook`, `GetOpenOrders`) and exercises none. The Bitstamp test two
  cases below calls the real `BitstampProvider.ToBitstampPair` — that is the right shape.
- [ ] **`StrategyLibraryPolicyTests` scans two of four shipping projects.** `AppSources()` lists
  `AccessibleTrader.Core`, `AccessibleTrader.BlazorClient.Components` and a **nonexistent
  `AccessibleTrader.Maui`** — silently dropped by `.Where(Directory.Exists)`. Missing:
  `AccessibleTrader.BlazorClient/` and `AccessibleTrader.WebHost/`. So all four "no catalogue in
  shipping code" guards are blind to a reintroduction in the WebHost, whose `Program.cs` is the
  composition root and the natural home for exactly the first-launch seeder the guard prevents.
  Enumerate from `AccessibleTrader.slnx` and assert `Directory.Exists` rather than filtering.
- [ ] **`ImportedStrategiesCannotStartThemselves` is a substring match over a whole file, comments
  included** — and the file has a `CodeOnly()` comment-stripper built for exactly this reason that
  this test does not use. Passes with an `IsAutoActivate = true` on the live path as long as the
  false string exists anywhere. Make it behavioural: import a bundle whose spec is
  `IsAutoActivate = true` and assert the library entry comes back false.
- [ ] **The markup half of `WithdrawalReleaseGateTests` passes with the guard inverted.**
  `Assert.Contains("WithdrawalService.Released", keys)` is satisfied by
  `@if (WithdrawalService.Released || _debug)`, and `box > guard` compares *first* occurrence
  indices — textual order, not lexical nesting. The **service** half is genuinely strong
  (`A_closed_gate_refuses_before_the_venue_is_ever_called` proves
  `DidNotReceiveWithAnyArgs().WithdrawAsync`), and that is the assertion that matters. But this is
  the only path that moves money off an exchange; render both components under bUnit with
  `Released` forced each way and assert presence/absence.
- [ ] **`HostileScriptTests` asserts only `Success == false`, never the failure origin, and has no
  positive control in the file.** Six sandbox-escape tests that all stay green under *any* universal
  compile failure — a broken implicit-usings injection (none of the test sources declare their
  `using`s), a Roslyn reference failure, a renamed SDK interface. The docstring claims "rejected
  with a sandbox-origin error". `OutOfProcessScriptingTests:94` has the positive control but runs a
  different path with a real worker binary, while `HostileScriptTests` deliberately uses a bogus
  worker path. Add a benign-script control through the same factory and assert the diagnostic
  prefix.
- [ ] **`HostedAccountsTwoFactorTests.Disable_flow_...` performs the two Identity calls itself**,
  then asserts Identity did what Identity does. If `SecurityModel.OnPostDisableAsync` dropped its
  `ResetAuthenticatorKeyAsync` call — the exact named regression, which revives a leaked TOTP
  secret on re-enrollment — this stays green. `SecurityModel` is the one PageModel with a test
  reference; call the handler.
- [ ] **`ChartFormationLayerTests.PlaceLabel` reimplements the layer's label-placement rule** and
  two tests assert the copy. The stated reason for not exposing the rule is sound; assert on the
  rendered output instead.
- [ ] **`HostedPaperModeTests` never tests the case it exists for** — `HostMode.Hosted` with
  `trading.paperTradingMode` explicitly `false`. Every test leaves the setting at the substitute
  default (null), so "hosted forces paper *regardless of the setting*" is never exercised. All four
  also assert only `SupportsTradingAsync`; nothing asserts an order actually routes to
  `IPaperTradingProvider` and never to the live provider.
- [ ] **`ModalContractScanTests`' `ModalBase` branch `continue`s past the open/close/Escape
  name-agreement check**, so a `ModalBase` inheritor publishing `ModalStateChangedEvent(true,
  "Foo")` / `(false, "Bar")` is never checked. Also `base.OnInitialized()` is a substring test — a
  commented-out call passes. (The `scanned >= 15` anti-vacuity guard is good and correct.)

### Untested code that decides money, security, or research truth

- [ ] **`AccessibleTrader.ScriptSandbox/FrameCodec.cs` and `WorkerDispatcher.cs` have ZERO tests.**
  VERIFIED — zero references anywhere in the suite, though the `.csproj` already has the
  ProjectReference. This is the boundary that parses bytes coming back **from a process running
  untrusted user-compiled code**, and it is the least-tested file in the repo while
  `HostileScriptTests` sits one layer above it asserting those scripts are adversarial. Specifically
  untested: the `while (read < count)` partial-read reassembly loop; big-endian length framing
  round-trip; the `length == 0` and `length > MaxFrameBytes` DoS guards; and
  `var opcode = (Opcode)header[4];` — **any byte casts to the enum with no validation** and falls
  into the dispatcher's switch. `MaxFrameBytes` is 64 MB, so a hostile worker can force a 64 MB
  host allocation per frame, repeatedly. Highest-leverage single test-writing task in the repo.
- [ ] **`AccessibleTrader.StrategyLab/SurrogateTest.cs` has ZERO tests.** VERIFIED — neither
  `SurrogateTest` nor `BlockBootstrap` is referenced anywhere in the suite. Every research verdict
  in this repo rests on `BlockBootstrap`, `PValue`, `ZScore` and `EdgePp`. Meanwhile
  `CatalogueProvenanceTests` rigorously verifies that a control was *claimed* — it asserts
  `Provenance.Controls` contains "random" — and nothing verifies the control *computes correctly*.
  A bug in the block bootstrap (wrong wrap, block length on the wrong axis, correlated resampling)
  systematically biases every "beat random" verdict, including the six specs retired as falsified
  and the one kept as `ControlTested`. Write known-input/known-output tests, plus the p-value
  boundary and the zero-stddev NaN path.
- [ ] **WebHost has no integration test of any kind.** No `WebApplicationFactory`, no `TestServer`,
  no PageModel handler ever invoked. `Login`, `Register`, `ForgotPassword`, `ResetPassword`,
  `LoginWith2fa`, `LoginWithRecovery`, `Logout` and `EnableAuthenticator` are all zero-referenced,
  so the code that calls `SignInManager` and decides lockout/2FA routing never runs in a test.
  Every minimal-API endpoint (`/push/subscribe`, `/diag/journal`, `/alerts/recent/*`) is
  unreachable. `HostedAccountsAuthPolicyTests` asserts the *options objects* — correct and worth
  having, but it proves configuration, not that any request is ever authorized. Nothing proves
  `/alerts/recent` requires a login or that user A cannot dismiss user B's alert.
- [ ] **The `Environment.GetFolderPath` per-user path bug has shipped twice and there is no guard.**
  `WorkspacePerUserIsolationTests` and `IndicatorPrefsPerUserIsolationTests` are both excellent and
  both docstrings describe the *same* defect — a service building its own path from
  `Environment.GetFolderPath(LocalApplicationData)` instead of taking `IPlatformPathService`. The
  tree is currently clean, so a source-scan guard would pass today and cost nothing. The scan
  pattern already exists in `StrategyLibraryPolicyTests`.
- [ ] **Zero tests for:** `Services/AI/` (all four files — network calls carrying user API keys),
  `EmailAlertChannel` and `TelegramAlertChannel` (the two delivery channels without tests;
  `WebhookAlertChannel` has 264 lines), all five level providers plus `LevelService` (they feed
  `ProtectiveLevelValidator` and stop placement), `InputRouter` and `KeyNormalizationService` (the
  keyboard entry point of an audio-first app, with `ShortcutConflictTests` sitting above them),
  `RiskRewardCalculator` and `MeasureToolCalculator` (geometry a user reads a position size off),
  `SkenderCalculationCore` (backs many shipped indicators), and the rendering layer classes.
- [ ] **Every money modal is string-scanned, none is rendered.** `ApiKeysModal`, `WalletModal`,
  `WithdrawModal`, `TradingDashboardModal`, `Toolbar` are source-text-scanned only; 18 further
  components are zero-referenced entirely (`JournalModal`, `MyDataModal`, `CustomScriptsModal`,
  `ThemeEditorModal`, `WatchlistModal`, `ObjectTreeModal`, `LevelReportModal`, `StatusBar`,
  `IndicatorBar` among them).
- [ ] **The bUnit harness makes focus bugs untestable by construction.**
  `BlazorTestHarness.cs:164` stubs `accessibleTrader.focusElement` with `SetupVoid(…, _ => true)`,
  so every test passes regardless of what a modal asks it to focus. **No test anywhere asserts that
  focus lands in a modal on open, that it is trapped, or that it is restored on close** — and the
  Tab trap itself lives in JS with no test at all. Nothing asserts `aria-live` behaviour, nothing
  asserts `aria-selected`/`aria-expanded` *values* (which is why five broken tablists shipped), and
  no test presses Space on a button or drives a tablist, listbox or tree with arrow keys. 31 of 46
  RCL components have no bUnit test, including `MainLayout`, `Toolbar`, `ChartArea`,
  `TradingDashboardModal`, `ObjectTreeModal` and `ConditionTreeEditor` — the last two being the
  product's primary non-visual navigation surfaces. Highest-value additions: a shared assertion that
  every dialog moves focus to its `h2[tabindex="-1"]` on open; an `aria-*` value scan over each
  component's compiled render tree; and a dispose-leak test that renders a component, disposes it,
  publishes every event it subscribes to, and asserts no handler ran.
- [ ] **No culture coverage anywhere.** One `CultureInfo` reference in 57,500 test lines, no test
  sets `DefaultThreadCurrentCulture`, and no `InvariantGlobalization` property in any csproj — so
  the shipped app picks up the OS locale. Under `de-DE`, `double.Parse("50000.5")` yields 500005.
  This is simultaneously a money path (provider JSON parsing, order input) and an accessibility path
  (spoken prices, which several tests pin as exact strings). See the systemic culture item in the
  audio section.
- [ ] **`PluginHostServices.ApiKeys` is process-global mutable state with manual, unenforced
  serialization.** `FakeApiKeyCheckout.Install()` swaps a static; nine classes carry
  `[Collection("ProviderCredentialBridge")]`; enrollment is invisible and only discovered by
  flaking — which the collection file's own comment records happening once and
  `BrokerParityTests:20-25` records happening a second time. Classes that construct real providers
  and are *not* enrolled include `ProviderLiveStreamTests`, `AlpacaBracketTests`,
  `ProviderCapabilityAuditTests`, `MexcProtobufDecodeTests`, `DeribitProviderTests`. Add a
  reflection guard in the shape of `AllTradingProvidersAreEnumeratedHere`.

### Flake risk

None of this makes 12 seconds untrustworthy today; all of it will bite on a loaded CI runner.

- [ ] **Four negative assertions gated on a fixed delay** — inherently racy in the false-green
  direction: `PreferencePersistenceTests:105` (1400 ms then "no write happened"),
  `GeneralOrderServiceTests:513-516` and `:535-540`. Note the asymmetry: the suite's own `WaitFor`
  helper is used for *positive* assertions and fixed delays for negative ones, which is backwards.
- [ ] **`StateMachineTests:60-80` asserts before the thing it tests can have happened.**
  `EmitTick` writes to an unbounded channel drained by a background reader; the assertion runs with
  no synchronization, so it passes because the reader has not been scheduled — it would pass
  identically if the illegal trigger *were* honoured. False green on the only production-code test
  of illegal-transition rejection.
- [ ] **`PreferencePersistenceTests` burns 2.9 s of wall clock**, with 500 ms of margin between a
  1000 ms throttle and a 1500 ms assertion. Use a `TestScheduler`.
- [ ] **`DateTime.Now` in five files** — `UIDiagnosticTests`, `RobustnessTestSuite`,
  `IntegrationDiagnosticTests`, `AudioDiagnosticTests`, `DataCacheTests` (where it is a dictionary
  key). The rest of the suite is disciplined about UTC.
- [ ] **No time abstraction anywhere.** Every timeout, throttle, debounce, poll interval and
  watchdog sweep is tested against the real system clock. `MarketFeedWatchdogTests` and the
  injectable `OrderPollFastInterval` are the right mitigation applied per-service rather than
  systemically. Also `SpyEventBus.SubscribeCoalesced`/`SubscribeSampled` use the default Rx
  scheduler, so any test touching them acquires real wall-clock timing.
- [ ] **`BrokerParityTests.Swap` picks the first `HttpClient` field by reflection.** Field order is
  not guaranteed by the CLR; a provider that gains a second `HttpClient` may get the wrong one
  swapped, and the un-swapped one would attempt a **real network call from a test**
  (`FakeHttpMessageHandler.StrictMode` cannot catch it — that client is not wired to the fake).
  Match by name and assert exactly one candidate.
- [ ] **`GeneralOrderServiceTests.DataOnly_provider_is_skipped_without_error` has no assertion at
  all**, and does not assert the provider was skipped — only that nothing threw.

### Tests that should exist and do not

Ordered by value. Every one of these would have caught something above.

- [ ] **Exactly one `Speak` per keypress** on a bar carrying a formation *and* a cross-series signal
  *and* a zone in range. Catches the zone-proximity overwrite directly; no current test constructs
  that bar or counts calls.
- [ ] **Enumerate `FeedbackType` and `EarconType`** and assert every member either speaks, earcons,
  or is on an explicit documented no-op allow-list. Three separate silent-arm bugs have now shipped.
- [ ] **Buy the whole account** (`Quantity = balance / price`) and assert `Free >= 0` after the fee.
  The only negative-cash test uses a 90% buy.
- [ ] **Wrong-side stops**: a buy stop below the market and a sell stop above it must be refused.
- [ ] **Brackets on a non-market entry**: `TradeSignal(..., OrderType.Limit, Price: 95, StopLoss:
  90)` must produce a resting stop. Every current bracket test uses `OrderType.Market`.
- [ ] **Orphaned legs after a manual close**: close the position, drive price to the stop, assert no
  new position is opened.
- [ ] **The focused pump must not deliver a tick for a different identity.** Set focus to B, push an
  A-priced tick into the orchestrator channel, assert B's buffer is untouched. Fails today.
- [ ] **`ResamplerService` has no test file at all** — bucket alignment, the timestamp convention,
  partial edge buckets, descending input, month/week boundaries, DST. Highest-value missing file in
  the data area now that resampled bars are persisted.
- [ ] **`OhlcvStore` monthly timeframes** (where the forming filter is wrong) and the insert-only
  dedup (a re-fetch with *different* values for an existing timestamp — no test asserts either
  behaviour).
- [ ] **`LiveStreamManager`'s watchdog has no tests at all** — not the connected-but-quiet branch,
  not `MaxReconnectAttempts`, not `AttemptReconnectAsync`, and not the consolidator reset that
  corrupts the in-progress bar.
- [ ] **Replace `DataOrchestratorResilienceTests`' hand-copied `Transition` switch** with a test
  that drives a real `DataOrchestrator`. It currently pins a transcript of the production code and
  passes while `LiveStreaming` is unreachable and the breakers never fire.
  `KeyedFeedsTests.FakeOrchestrator` shows the mock farm is affordable.
- [ ] **Run the speech-formatting suite under `de-DE`.** Zero tests use a non-invariant culture.
- [x] **Sweep every price-formatting call site** for `:F0`/`:F1`/`:F2` on price-space values. Three
  commits have each fixed some and missed others. **DONE 2026-08-21** — `PriceFormatScanTests`.
  Note what it can and cannot do: it matches a fixed format sitting next to a quote-currency word,
  which is the class that recurs, but no source scanner can know that MACD is in price units or
  that a Bollinger band is. Those were found by reading in the same pass. The structural version of
  this — a component declaring that its values are prices — is written up under the audio section.
- [ ] **Mute as a `[Theory]` over every `ComponentDisplayType`** with a dedicated render path —
  Candle, Wick, Cloud, Oscillator, Histogram, Bar, Line, Profile, Heatmap. Currently only Candle.
- [ ] **Heikin-Ashi component-context speech** — HA on, a bar whose raw lower shadow is 19% and
  whose HA lower shadow is 0%, assert the wick speech says zero. `BarDetailContextTests` covers the
  detail key only.
- [ ] **Only `SyncNavigationSlots` writes voice slot 0** (reflection or architecture test).
- [ ] **`PlayNote` never emits `SetVoice` for slots 26-31**; and a rendered-audio assertion that a
  staggered earcon has N distinct onsets.
- [ ] **Render a full Chart-scope bar and assert peak ≤ 1.0** (output headroom).
- [ ] **`SetVoice(durationSec: 0, envelope: "Ping")` produces no NaN** in the buffer.
- [ ] **Per-user isolation of `paper_account.json`**, plus two concurrent scoped providers over one
  directory. `WorkspacePerUserIsolationTests` and `IndicatorPrefsPerUserIsolationTests` exist; the
  paper account has no equivalent.
- [ ] **`ReportSuccess` is not called for a sentinel result**, and the dashboard renders
  `ORDER_DUPLICATE_SUPPRESSED` as an error.
- [ ] **The word "liquidated" reaches speech** on a forced close.
- [ ] **A throwing `OrderUpdate` subscriber does not take down the fill engine**; and a throwing
  `EventBus` subscriber does not stop delivery to the others.
- [ ] **Concurrency on the paper broker** — fill evaluation racing a user cancel; reentrancy from an
  `OrderUpdateStream` subscriber back into `PlaceOrderAsync`. No test in the trading area uses
  `Task.WhenAll` or `Parallel.For`.
- [ ] **Gap-fill overlapping a live tick.** The two operations are covered separately; the in-lock
  re-check at `ChartFeed:140` that makes overlap safe has no test that would fail if deleted.

---

## Open after the 2026-08-20 hosted-tier and audio batch

Everything else in that batch is closed (see [CHANGES.md](CHANGES.md)). These are what was found
and deliberately not fixed, each with the reason.

- [ ] **`trader_local.db` is not under `Accounts__DataRoot`.** It resolves to
  `$XDG_DATA_HOME/AccessibleTrader/`, because the DbContext factory must stay a singleton and so
  cannot read the per-circuit path service. Contents are public market data, so sharing is harmless
  — but two services on one box write to one file unless each gets its own `XDG_DATA_HOME`. Same
  isolation the secret store needs; documented in [SERVER_SETUP.md](SERVER_SETUP.md), not enforced.
- [ ] **`OhlcvStore` has no schema migration path.** `EnsureCreated` will not alter an existing
  database, so any change to `OhlcvEntity` means deleting the file on deploy. It logs at Error when
  the table is unusable, which is the signal — but the deletion is manual.
- [ ] **Server-side state migration for existing hosted installs.** Workspaces, `alerts.json` and
  indicator preferences moved under `users/{id}/`. Anything left in the old shared directory is
  intact on disk and invisible to the app, and background alert monitoring stays quiet until
  `alerts.json` moves. Commands are in [SERVER_SETUP.md](SERVER_SETUP.md); with more than one
  account there is no correct automatic answer, because the shared directory has no record of who
  wrote what.
- [ ] **`BackfillManager` is dead code.** No callers, still registered in both hosts, still has
  passing tests. `OhlcvStore` supersedes it. Deleting a class plus its test file is a judgement
  call, not a cleanup.
- [ ] **The analytics cache key has no user or credential dimension.** Correct today — hosted keys
  are server-seeded and desktop is single-user — but if per-user API keys ever reach hosted, one
  user's paid Glassnode data would be served to another from the shared cache directory.
- [ ] **Three candle classifiers.** `BarDetailService.ClassifyBar`,
  `SpeechFormatter.ClassifyCandleType` and `SdkCandlePatternAnalyzer` each implement the same
  thresholds, and they already disagree: one says "Bearish Marubozu" where another says "Marubozu",
  one returns "Standard Candle" where another returns "". Not user-visible yet. Three copies of one
  rule will drift.
- [ ] **Upper and lower wicks share a waveform.** They are now correctly distinguished by pitch
  (880 / 220 Hz) and each roughens with its own length. Whether they should *also* differ in timbre
  — the way the body already colours direction with a touch of square or triangle — is an open
  design question, not a defect.
- [x] **The rest of the audio surface now has assertions.** `SonificationTimbreTests` pins the
  design rules for the body, the volume bed, oscillators, histograms and the price line, plus the
  cross-cutting ones (muting is absolute, a user patch opts out of the built-in partials, NaN is
  silent). All fifteen passed on first run — the wick path was the broken one — but they exist so
  the next rename breaks a test instead of a user's ears, which is exactly what the wick defects
  did not do. Writing them also caught three comments in `SonificationProfileProvider` that had
  drifted to describe sawtooth partials the code does not use.
- [ ] **`AccessibleTrader.BlazorClient` cannot be built on this machine** (`NETSDK1147:
  maui-android workload not installed`), so its edits in the 2026-08-20 batch are the only changes
  no compiler has checked. They mirror WebHost edits that do compile. Worth a build on a machine
  with the workload before shipping a desktop build.
- [ ] **No `[2.3.0]` section in [CHANGES.md](CHANGES.md).** 2.3.0 documented itself in WHATSNEW and
  its verification doc and never came back to the changelog.

## Terminal / lab split (2026-08-01)

The app ships tools, not opinions. Reasoning in
[STRATEGY_LIBRARY_POLICY.md](STRATEGY_LIBRARY_POLICY.md), mechanics in
[STRATEGY_CATALOGUE.md](STRATEGY_CATALOGUE.md).

- [x] **Per-asset auto-recommendation removed** from all four surfaces plus the
  `AssetClassifier.RecommendV23*` / `GetV23*Preset*` machinery. `Classify()` kept —
  profiling is measurement, the mapping to a named strategy was the opinion.
- [x] **The thirty specs moved to the lab.** `BuiltInStrategySeeds` is gone from Core;
  `StrategyLab/Catalogue/` owns them. `JsonStrategyLibrary` no longer seeds, so a fresh
  install opens empty. Existing installs untouched.
- [x] **Provenance per spec** (`StrategySpec.Provenance`): 1 ControlTested,
  5 WalkForward, 9 InSampleOnly, 7 Untested, 2 Fragile, 6 Falsified.
- [x] **Import path** (`StrategyBundleService`) + Library-tab import form + a real empty
  state; `catalogue list` / `catalogue export` on the lab side.
- [ ] **Bundle export from the terminal.** Import exists; the app can only write the old
  single-spec `.atstrat` into `{AppData}/exports/`, so whole-library transfer between
  machines is one-directional.
- [ ] **Per-spec confirmation before starting an `ExecutionMode.Auto` import.** The count
  is announced at import time; there is no second gate at Start.

## Platform tiers + signal service — designed, not built (2026-08-02)

Design notes in [PLATFORM_AND_SIGNAL_SERVICE.md](PLATFORM_AND_SIGNAL_SERVICE.md).

- **The boundary rule:** anything that MEASURES or DISPLAYS goes in the terminal; anything that
  decides what is TRUE goes in the lab. Conclusions reach users only through an opt-in signal
  service, with their evidence level attached.
- **Chart patterns:** describe freely, score never. Description is an accessibility feature worth
  shipping; prediction is tested and weak (a random line is respected 59% of the time).
- **Theses accumulate with no LLM** — named conditions, re-evaluated daily, with the invalidation
  condition written down before the outcome is known.
- Key status tested 2026-08-02: **CoinMarketCap works** (and covers the crypto vetting scorecard);
  **Nomics is dead** (service shut down); **CoinAPI is out of credits**. Nothing needs buying — SEC
  EDGAR, GDELT and Wikipedia pageviews are free and cover the real gaps.
- [x] `WikipediaPageviewsProvider` — DONE 2026-08-02. Analytics tier, no key, 33 curated tickers
      plus raw-article passthrough, daily and monthly. Two corrections found while building it:
      per-article **hourly is HTTP 400** (the design note said hourly and was wrong), and requests
      reaching before **2015-07-01** 404 the *entire* range rather than clipping, so the window is
      clamped. Three plausible catalogue entries (Binance, Stock market, Tether) resolve as
      Wikipedia articles but 404 from the pageviews API and were left out rather than shipped dead.
- [x] **Opt-in chart-pattern description** — DONE 2026-08-02, and it is the part of the dossier that
      did not need the dossier. `ChartPatternDetector` finds double tops/bottoms, head and shoulders
      (both ways), ascending/descending/symmetrical triangles, rising/falling wedges and bull/bear
      flags, each reported as **Forming** (structure present, trigger not yet hit) or **Completed**.
      Forming reports carry the trigger level, because a pattern announced only on completion cannot
      be acted on. Setting `speech.describeChartPatterns`, default OFF, spoken on X-axis navigation.
      Never says bullish or bearish — a test enforces the banned vocabulary.
- [x] **Dossier modal** (`Alt+I`) — DONE 2026-08-02. Tabbed by QUESTION not by source, spoken
      headline before any table, four explicit empty-states (Ok / NoData / NotApplicable /
      Unavailable) so a blank row is a bug. Crypto runs on CoinGecko + a direct GitHub query;
      equities on SEC EDGAR. `docs/ASSET_DOSSIER.md`.
- [x] **The 11-check crypto scorecard** — DONE 2026-08-02, inside the dossier, sourced from
      CoinGecko + GitHub rather than CMC (CMC would have been a price-page reprint; CoinGecko carries
      developer activity and disclosure links, which is the part that is not on any price page).
- [ ] `CoinMarketCapProvider` — now optional. Only worth it for fields CoinGecko lacks
      (`self_reported_circulating_supply`, `cex_volume_24h` vs `dex_volume_24h`).
- [ ] `SecEdgarProvider`, `GdeltProvider` — larger, gated on the revision-breadth result.
- [ ] Signal service LAST, when there is more than one control-tested edge to serve.

## Company / macro data layer — designed, not built (2026-08-02)

Design notes in [COMPANY_DATA_LAYER.md](COMPANY_DATA_LAYER.md). Nothing is built; the reasoning is
recorded so the decision can be made without re-deriving it.

- The layer serves **two audiences**: automated strategies AND a person reading the data to decide
  for themselves. Discretionary use is first-class, and it changes the design — display everything
  with provenance, score only what has earned it. **If every edge tests null the dossier is still
  worth having**, which is what makes the project justifiable.
- Presentation rule: **time axis → chart** (analytics series + event markers, existing plumbing);
  **snapshot of many facts → modal** (`Alt+I` dossier, spoken headline before any table).
- SEC EDGAR is the anchor and is free; FMP's paid tiers largely resell it. GDELT and Wikipedia
  pageviews are the other two free anchors.
- **LLM in the ingestion pipeline, never the decision loop** — and LLM-derived sentiment can only be
  validated FORWARD, because a model scoring old text already knows what happened next. Invisible
  lookahead, no offset can fix it.
- [x] **Forward crypto-universe recorder is live** (2026-08-02). `StrategyLab record-universe`,
  1,000 assets/day, gzipped into the COMMITTED `universe-archive/` (65 KB/day, 23 MB/year) because
  it is the one artefact that cannot be re-fetched after the fact. **Run it daily** — survivorship
  is the only bias with no retrospective fix.
- [x] **Wikipedia pageviews is live** (2026-08-02). Note the urgency argument was partly wrong: the
  API serves history back to 2015-07-01 on demand, so the daily series is NOT lost by waiting. GDELT
  event counts are the one where delay still costs something.
- [ ] **Start the GDELT recorder.**
- [ ] **First test if the layer is greenlit:** analyst estimate revision breadth, through the
  cross-sectional harness. Strongest documented prior, inputs already free, machinery already exists.

## FMP provider is broken for new keys (found 2026-08-01)

- [x] **Migrate `FmpProvider` + `FmpAnalyticsProvider` to `/stable/`.** DONE 2026-08-02 (commit
  301d1a66). Symbols moved from path segment to query parameter, daily bars are a flat array,
  earnings-surprises folded into `earnings`, sector performance takes one sector plus a range. Both
  providers now route every call through one helper that surfaces plan-gated 402s and legacy 403s to
  the error stream instead of returning an empty series. Contract tests updated, plus one pinning
  that no retired path is ever called. ORIGINAL REPORT: Both target
  `/api/v3` (and `/api/v4`), which FMP retired: **403 `Legacy Endpoint`** for any key
  without a subscription predating 2025-08-31. Keys older than that still work, so this
  fails only for new users — verified against a live key, which succeeds on `/stable/`.
  Paths take the symbol as a query parameter there (`/stable/quote?symbol=AAPL`) and the
  response shapes differ. Endpoint-by-endpoint status (what works, what is 402 on the
  free plan) is tabulated in
  [ANALYTICS_DATA_PROVIDERS.md](ANALYTICS_DATA_PROVIDERS.md#equities--fmp-new-requires-free-api-key).
  Two research consequences: **no macro consensus** (`economic-calendar` is 402), but
  `/stable/earnings` carries `epsActual` + `epsEstimated`, so the surprise-vs-date
  hypothesis is testable on company earnings instead.
- [x] **Is any FMP tier worth buying? No** (verified 2026-08-01). Every paid item has a free
  primary source: fundamentals and 13F and insider trades are SEC EDGAR, which FMP resells;
  EOD prices we already have. The only scarce item was consensus estimates — and **Alpha
  Vantage's free `EARNINGS` endpoint carries actual, estimate and surprise back to 1996**
  (verified on IBM: 122 quarters). 25 requests/day is irrelevant when one request returns a
  full history and the lab works from snapshots.

---

## Post-2.0 polish batch (2026-07-26)

Accessibility fixes surfaced in live use, plus one new provider.

- [x] **Accessible mobile drawing.** Touch-only users can now complete multi-point
  drawings: `PlaceDrawingAnchorEvent` → `DrawingInteractionManager.PlaceAnchorAtCursor`
  drops an anchor at the cursor, driven by a new "Place drawing point" touch button.
- [x] **Touch bar: previous/next series** buttons (Page Up/Down equivalents).
- [x] **Sparse-signal speech.** Marker components with a NaN cell but real data now say
  "N signals in view" / "no signals in view" instead of "no data" (which read as a
  broken series). Truly empty/absent arrays still say "no data". Cipher B unchanged.
- [x] **Gradient chart background** (opt-in, default OFF) in Settings → Colors: second
  bottom colour + `SKShader` linear fill. Plus a cloud-fill crossover gap fix (shared
  interpolated apex at each bull/bear flip).
- [x] **Deribit analytics provider** (keyless): DVOL volatility index (crypto VIX,
  OHLC) + realised volatility for BTC/ETH — the terminal's first crypto-options window.
- [ ] **CoinGlass + extra trading brokers (Tastytrade / Bybit / Kraken Futures /
  Databento).** Deferred — key-gated or money-path APIs that can't be verified
  read-only; revisit when accounts/keys are available to test against.

## Provider system — quality pass (2026-07-24 → 07-25)

Read-only audit of all providers turned into fixes; then the direct-API/SDK work.

- [x] **Provider correctness sweep.** Bitstamp private order stream + fill parse;
  broker `GetOrderStatusAsync` (Tradier/Schwab no longer announce filled orders as
  cancelled); Tradier intraday timestamps + Eastern window; IB order-safety;
  Alpaca crypto symbol + bracket; Coinbase per-request auth; Oanda scrub + range
  fetch; FMP/Polygon/Finnhub data fixes; `RateLimiter` 4xx/cancellation; Kraken
  fills rate-limit; consistent read-path error surfacing.
- [x] **MarketFeedHub multi-live watchdog** — background feeds detect silence,
  announce once, restart (bounded), and surface the provider ErrorStream.
- [x] **MEXC direct-API rewrite** — `CryptoExchange.Net`/`JK.Mexc.Net` removed;
  Protobuf spot WS via build-time codegen; live-verified (charts + title price).
- [x] **SDK sharing** — `RestSigning` (HMAC/query), `SymbolFormat` (pair shaping),
  `ProviderError` + `SurfaceError` (typed error surfacing), plus the
  `PluginDependencyIsolationTests` clash guard, capability-consistency invariants,
  and a `ProviderConformanceTests` universal-contract gate.
- [ ] **Coinbase live candle volume.** Live candles are synthesized from the
  `ticker` channel with volume = 0. The fix is the Advanced-Trade `candles` WS
  channel + `LiveTickStyle.CumulativeBars`, but it's an unverifiable behavior change
  to a working live path (no Coinbase account to test) — deferred as an accepted
  limitation rather than shipped blind.
- [ ] **MEXC live-price responsiveness on thin markets.** The live title price is
  driven by MEXC's spot kline WS, which pushes the forming candle as periodic
  snapshots (sparse for illiquid pairs like TAOUSDT), so after a dip the title can
  sit at that snapshot's close (the lower wick) until the next push. Decode +
  consolidation are correct; the responsive fix is to drive the current bar's close
  from the deals (trades) channel (`spot@public.aggre.deals.v3.api.pb`) like
  Bitstamp does. Accepted limitation for now (Cody, 2026-07-25) — doesn't affect the
  bar structure or order placement.
- [ ] **Migrate the other crypto providers onto the shared REST base.** MEXC uses
  `RestSigning`/`SymbolFormat`; Kraken/Bitstamp/Binance/Coinbase still hand-roll
  their signing + symbol shaping. Migrate opportunistically (each is
  live-verified, so don't retrofit blindly).
- [ ] **`SupportsOrderEventStreaming` honesty for Coinbase.** MEXC/Binance now flip
  it on real private-stream state; Bitstamp deliberately stays `true` (no
  `GetFillsAsync`, so polling would mis-resolve). Coinbase needs the user-channel
  subscription ack tracked (its single socket multiplexes) — plus a Bitstamp
  `GetFillsAsync` if we ever flip it.

---

## Structural debt register (2026-07-16 whole-app assessment)

From the full-codebase quality assessment (agreed with Cody). Ordered by
recommended attack order — ROI over severity. Each item is independently
shippable; none blocks the others.

1. [x] **VoiceParams struct + perceptual audio snapshot tests.** DONE 2026-07-16 (commit 6311d42d). `SetVoice` has 16
   positional params and `AudioPoint` keeps growing; wrong-position bugs are one
   refactor away. Introduce a `VoiceParams` struct (single call-site-compatible
   overload, then migrate callers), and an "audio snapshot" test harness: render
   ~2 s of a voice through AudioEngine, assert per-band RMS — this would have
   mechanically caught the months-long inaudible-noise bug (filters with no
   makeup gain). DO THIS BEFORE the wavetable oscillator lands. (~2-3 d)
2. [x] **Wavetable oscillator + WAV layers** DONE 2026-07-16. (sound plan steps 3-4): single-cycle
   wavetable waveform type in AudioEngine (user WAV / AKWF import → custom
   oscillator timbre at any pitch), one-shot WAV sample layers in the Sound
   Designer for earcons/signals. Lands on top of item 1's clean params. (~3-5 d)
3. [x] **Typed settings + one source of truth.** DONE 2026-07-16 both stages:
   (a) SettingsKeys + IAppSettings, consumers migrated; (b) store-resident
   preferences persist via PreferencePersistenceService (seed at startup,
   write-back on change; F2/F3 mute toggles deliberately session-only).
   Follow-up: hosted multi-user WavetableBank is process-global — per-user
   imports share ids across users; scope it per-user before promoting WAV
   import on the hosted build. Preferences split arbitrarily
   between WorkspaceState (speak timestamps, WASAPI latency) and SettingsManager
   JSON (braille, paper mode, themes); keys are stringly-typed. Stage (a): a
   strongly-typed AppSettings facade over the JSON, all key constants
   consolidated, typo-proof accessors. Stage (b): migrate store-resident
   preferences into it one at a time with compat shims. Each stage shippable. (~3-4 d)
4. [x] **Speech utterance builder.** DONE 2026-07-16 — provider path folded into SpeechFormatter's single precedence list; strategies can decline via null. Today an utterance can come from provider
   GetComponentSpeech, a strategy class, template expansion, or a hardcoded
   branch, with precedence spread across NavigationFeedbackManager +
   SpeechFormatter. Consolidate into one pipeline with a single visible
   precedence list. Existing speech tests protect the behavior. (~2-3 d)
5. [~] **Modal view-models + mandatory ModalBase.** Contract now ENFORCED by ModalContractScanTests (2026-07-16); AlertTestSender extracted as the view-model pattern. Remaining: extract the other big modals' logic opportunistically as they're touched. Pull persistence/test-send
   logic out of the big modals (SettingsModal ~1,300 lines) into view-model
   classes; kills the bUnit timing-flake class and makes ModalBase bypass
   impossible. Incremental, one modal at a time, opportunistic. (~0.5 d/modal)
6. [x] **Shared JS assets.** DONE 2026-07-16 — shared trio moved to the components RCL (_content/ path); host-specific audio.js/webSpeech.js stay in WebHost. BlazorClient and WebHost wwwroot/js are identical
   copies kept in sync by discipline only. Single shared static-assets source
   (project or build-copy step). (~0.5 d)
7. [~] **Chart data pipeline: keyed feeds.** Seam DONE 2026-07-16 (IMarketFeeds; monitors migrated). Full keyed refactor still waits for its trigger: THE structural debt — 7 singletons
   assume one chart identity (DataManager stops the previous stream on start;
   store holds one live state + frozen TabSnapshots; orchestrator/alert/strategy
   evaluate "the" state). Decision 2026-07-16: do NOT big-bang this. Plan:
   (a) introduce an `IMarketFeed`/feed-registry seam now-ish — focused identity
   delegates to the live pipeline, background identities to the existing
   monitors — and migrate consumers to it opportunistically; (b) full keyed
   refactor only behind an explicit trigger: tick-level background evaluation,
   simultaneous multi-chart rendering (DotPad split view), or hosted multi-user
   scale. Until a trigger fires, polling monitors + the one-driver contract are
   correct, tested, and sufficient for the validated daily/4h strategies.

---

## [2026-07-15] — Multi-workspace background monitoring

Full detail in `CHANGES.md` [Unreleased]. Suite 1630 Debug / 1629 Release.

### Shipped

- [x] **Background monitors per inactive tab** — polling fetch + private-state
  indicator recompute + symbol-scoped alert/strategy evaluation; opt-in setting,
  poll cadence setting (floor 10 s), Full-mode only (DemoPolicy gate).
- [x] **One-driver contract** — `ActiveStrategy.Symbol` stamped at start; foreground
  engine skips unfocused symbols, monitors pick them up; null-symbol alerts stay
  focused-chart-only. Background signals announce-only (Auto never places orders).
- [x] **Symbol-prefixed speech** for background alerts + setup events; Symbol threaded
  through Setup* events, sonifier, and the setup→alert bridge.
- [x] **Ctrl+Alt+Shift+M** monitoring status command; reconcile on tab switch/close,
  settings change, and workspace restore; Settings → General fieldset (web UI).
- [x] 12 unit tests (monitor scoping/warmup/prefix, registry gating/reconcile,
  engine skip, sonifier prefix); manual/quickstart/shortcuts docs.

### Follow-ups

- [ ] Keyed-feed DataManager refactor if tick-level background evaluation is ever
  needed (spec'd in the multi-workspace patch; polling is sufficient for the
  validated daily/4h strategies). The Settings fieldset is in the shared
  SettingsModal, so both heads already have the UI.

---

## [2026-07-13] — 1.6.0 positioning & risk release

Full detail in `CHANGES.md` [1.6.0]. Suite 1593/1593.

### Shipped

- [x] **CFTC COT provider** (11 contracts, free Socrata API, release-Friday stamping)
  + **COT Positioning indicator** promoted from the lab (z26, ±1.5σ crowded markers,
  per-asset interpretation) + **FINRA daily short-volume provider** (any US stock).
- [x] **Trend Baseline strategy** — lab walk-forward survivor on all 4 assets tested;
  **v23c Cipher+Trend+COT** seed with honest H1-weakness verdict in its description.
- [x] **Setup announcements speak the full trade plan** (entry/stop/every TP rung);
  armed/entry-reached/dropped stages journaled.
- [x] **Warn-only risk hints**: liquidation-buffer check on leveraged entries; sector-
  stacking note (2%-per-sector) on live review and paper fills. Never blocks.
- [x] **Webhook alert channel** (Discord/Slack/custom JSON, HTTPS-only) + settings UI.
- [x] **Cipher A retired** (IsDeprecated — hidden from Add dialog, saved workspaces
  unaffected); Cipher C reframed as micro-cycle/failure context; Loukas [35,90] window
  validated cross-asset, FY components suppressed on non-BTC charts.
- [x] **StrategyLab de-branded** (battery / rolling-window verbs; zero third-party
  references) + first lab unit tests (BootstrapCi, MarkerSideHelper, snapshot cache).
- [x] Strategy display names rewritten user-readable with [vNN] tags (IDs stable).
- [x] Brand logo everywhere; restart position reconciliation; settings fixes
  (runtime version in About, working background-color override).
- [x] Manual: new Strategy Lab section; positioning indicators, risk hints, webhook,
  full-plan setup speech documented.

### Follow-ups

- [x] **Auth: password-reset flow (option B)** — DONE 2026-07-14 via reviewed patch:
  `--reset-link <email>` CLI mints the token (no Kestrel), ForgotPassword/ResetPassword
  pages with generic messaging, audit events, end-to-end token tests. Option A
  (verify-by-email via msmtp → Postmark/SES) remains the eventual upgrade and the
  only full fix for the residual registration redirect-vs-error oracle.
- [x] **Alerts: custom condition trees (Part D)** DONE 2026-07-17 — — embed ConditionTreeEditor in
  AlertsModal behind an "Advanced condition" toggle; evaluate via the strategy tree
  evaluator when set (~2-3d, spec in the 2026-07-14 alerts-symbol-routing spec).
- [ ] **Auth: HaveIBeenPwned password validator** — k-anonymity range API,
  fail-open, needs outbound-HTTPS egress confirmed from the VPS first (~0.5d).
- [x] **Auth: optional TOTP 2FA** DONE 2026-07-21 — /account/security hub +
  accessible enrollment (copyable grouped key primary, QR convenience) +
  LoginWith2fa/LoginWithRecovery + 10 single-use recovery codes + 6 audit kinds;
  password-confirmed disable/regenerate; key reset on disable. 12 tests against
  the real hosted stack incl. independent RFC 6238 verification. FOLLOW-UP: no
  in-app link to /account/security yet (URL + docs only) — add an account menu
  to the hosted terminal chrome when the hosted UI gets its next pass.
- [ ] **Ops: systemd `UMask=0077`** drop-in on the hosted unit so future files
  (auth.db recreations, security logs) default private (~5 min, not a repo change).
- [ ] FINRA Query API short-interest metric (needs free dev registration — Cody).
- [ ] Tiered RiskPercent by setup quality (2-tier, evidence-based) in RiskPlan.
- [ ] Sector risk governor as an optional *enforcing* mode (default stays warn-only).
- [ ] Pre-registered lab test: wider ladder (3R/6R or trail) for the gated v23 variants.
- [x] Lab GUI tab in the Strategy modal DONE 2026-07-17 (walk-forward windows + battery comparison with the CI survivor gate; parameter sweeps remain lab-only for now).
- [x] **v24 cycle strategy** DONE 2026-07-17 — `builtin.long.v24-cycle-low-reversal`
  (DCL Confirmed within 2 + any v23 Cipher trigger within 8, ATR×3 stop, trail after
  TP1). Lab-validated BTC daily both halves positive (+0.25R/+0.31R, 35/35 trades,
  PF ~2.1), 5-of-6 windows; ETH thin, LTC fails H2, SOL insufficient — shipped as
  BTC-daily with the negatives in the description. Closes the Wave-4 cycle-strategy arc.
- [x] Live order-stream subscription audit DONE 2026-07-21 — GeneralOrderService now
  self-wires per-provider live stream subscriptions on ConnectionStatusEvent(Connected);
  fixed the latent paper double-subscribe and single-slot-drop bugs. Schwab/Tradier
  streams are unfed (no streaming impl) — fills there surface via dashboard refresh.
- [x] **Order-status polling fallback** DONE 2026-07-21 — ITradingProvider gains
  SupportsOrderEventStreaming (default-true DIM; Schwab static false, Tradier dynamic);
  GeneralOrderService watches orders on non-streaming brokers (5s→30s poll, fill lookup
  with lag retries, order-type → trigger semantics, cancelled when no fill record).
  Limitation on record: broker-attached protective legs aren't watched (unknown ids).
- [x] **Tradier account-events stream** DONE 2026-07-21 — websocket
  wss://ws.tradier.com/v1/accounts/events with per-connect session minting; wire-status
  → OrderUpdate mapping pinned by 7 tests. NOT yet verified against a live/sandbox
  Tradier account (no credentials on hand) — flag is dynamic so polling covers if the
  socket can't connect. When Cody gets a Tradier login: place a sandbox order and
  confirm the instant announcement + no double-announce with polling.
- [ ] **Schwab streamer ACCT_ACTIVITY** (~2-3d, riskier) — requires the full Schwab
  WebSocket streamer handshake (streamer info from user-preferences endpoint, login
  frame, ACCT_ACTIVITY subscription + XML payload parsing). Needs Cody's
  developer.schwab.com app credentials (see SERVER_SETUP/API keys flow) and a real
  account to verify. The polling fallback covers the announce gap until this lands.
- [ ] MAUI native window title: add the live price (WebHost browser-tab title has it,
  1s-sampled off DataStream in MainLayout; MainPage.xaml.cs `_titleSub` needs the same
  DataStream sampling — can't build-verify MAUI on Linux, do on Windows).
- [x] PropertiesModal bUnit flake FIXED 2026-07-21 — pre-click Find() raced the
  initial render on starved runners; now WaitForElement/WaitForAssertion before
  every interaction.

---

## [2026-07-22] — Local background monitoring core (SHIPPED)

- [x] **LocalBackgroundMonitor** DONE 2026-07-22 — browserless alert evaluation on
  local WebHosts: watch list derived from Symbol+Provider alerts, 60s polls in a
  DI scope, persistent evaluator (edge state), pauses while circuits are live,
  delivers via paplay sound + notify-send + Orca/spd-say. Opt-in
  monitoring.backgroundLocal. Default beep at sounds/alert.wav until the factory
  sound bank lands (Cody gathering WAVs).
- [ ] **Factory sound bank** — Cody's event WAVs as bundled defaults per earcon key
  + the background monitor's alert.wav; Sound Designer assignment/revert (~0.5d
  once files arrive).

## [2026-07-22] — Touch Explore mode (Wave 3 web-touch item SHIPPED)

- [x] **Web touch Explore mode** DONE 2026-07-22 — TouchNavBar Explore toggle
  (real button, SR-reachable; pass-through gesture then explores the canvas);
  finger slide → TouchExplore events through the existing mouse bridge →
  ChartHoverTracker speaks each bar (value-first, per-bar, Manual channel) +
  always-on pitch tick; crosshair follows; lift re-arms; pinch still zooms;
  drag-to-pan restored on toggle-off. 3 JS + 3 tracker tests.
- [x] **Hosted circuit lifecycle** documented + observable: Blazor disposes a
  closed tab's circuit after ~3 min retention → all scoped feeds/providers
  dispose with the scope; circuit handler logs open/close with active count.

## [2026-07-22] — My Data: CSV import (Wave 2 CSV item SHIPPED)

- [x] **CSV/custom data provider** DONE 2026-07-22 per the design discussion:
  My Data market in the cascade, 3 auto-detected shapes (OHLCV → candles;
  named value columns → per-column line charts; date,label[,value] → event
  markers via the indicator dialog with the label as speech), paste-first
  accessible import dialog (Ctrl+Alt+Shift+I) with templates, per-user
  persistence + quotas, per-symbol DataShape SDK hook, demo-hidden. 26 tests.
- [x] **My Data v2 — overlay path** DONE 2026-07-22 — MyDataSeriesProvider: per-dataset
  "My Data: X" (own pane, per-column components, Normalize-to-100 param), "My Data
  overlay: X" (main pane, rebased to chart close at first alignment = %-compare),
  "My Data ratio: X" (chart÷data, OHLCV only). Forward-fill alignment engine
  (AlignForwardFill) is internal+tested — the symbol-vs-symbol compare item now
  reduces to feeding it fetched second-symbol bars.
- [x] **Symbol compare overlay** DONE 2026-07-22 — SymbolCompareProvider (COMPARE /
  COMPARE_RATIO) via ICrossSeriesCache + the shared AlignForwardFill engine;
  Provider/Market/Symbol string params, __provider/__timeframe hints stamped by the
  orchestrator. 7 tests.
- [x] **OCO UI** DONE 2026-07-22 — TradeSignal.OcoGroupId + paper-broker pair
  enforcement (fill/manual-cancel cancels sibling, persisted) + Trading Dashboard
  OCO-pair section (paper mode only). 5 tests.
- [x] **Live OCO** DONE 2026-07-22 — IOcoTradingProvider capability + service
  routing (native/paper-grouped/refused) + Binance /api/v3/orderList/oco with
  request-shape tests both sides. NOT yet fired against the real exchange; first
  live use should be a tiny pair. Other exchanges (Kraken, Coinbase) as demand asks.
- [ ] **Windows tray VERIFY** — MAUI head close-to-tray (H.NotifyIcon.Maui 2.3.0,
  Restore/Exit menu). The csproj default is now flipped to true
  (`EnableWindowsTrayIcon`), so it builds by default. Remaining: on the next
  Windows session, verify the four steps in TrayIconService.cs (opt out with
  `-p:EnableWindowsTrayIcon=false` if a build must skip it). Package version may
  need adjusting for net10 MAUI.
- [x] **WebHost desktop tray applet** (SHIPPED 2026-07-23) — cross-platform tray
  for the LOCAL Full-mode WebHost: `DesktopTrayService` + `ITrayPlatform`
  (LinuxTrayPlatform StatusNotifier/D-Bus verified; WindowsTrayPlatform
  Shell_NotifyIcon pending a Windows smoke test; MacTrayPlatform actions-only).
  7-item menu, live unread-count label (NewTitle/NewToolTip signals),
  `/alerts/recent` HTML page (Mark-read/Dismiss), `RecentAlertsBuffer` fed by the
  background monitor + per-circuit `InSessionAlertRecorder`, `AlertSnooze`. 19
  unit tests on the platform-agnostic core. Never on the hosted server.
- [ ] **Hosted server-side alerts + Web Push** — evaluate saved alerts server-side
  against shared feeds (rides the shared-connection-pool item) delivering via the
  existing webhook/Telegram/email channels; Web Push (VAPID + service worker) for
  OS notifications with the tab closed. The one remaining "line item" (~3-4d).
- [ ] **xlsx import** — future; needs a spreadsheet-parsing dependency. The
  dialog tells users to export CSV from Excel/LibreOffice meanwhile.

## [2026-07-21] — Cody's UX batch (screened, post-1.9.0)

Shipped immediately (same day): Ctrl+Alt+Shift+L LoadChart command (global, spoken
refusal when the cascade is incomplete, same pre-flight as the button); License tab
corrected MIT → GPL v3 + "Support development" donation section (Cash App $churst90,
PayPal cody@x64mail.com). Verified already-wired: the Applications/Menu key and
Shift+F10 open the right-click context menu (Phase B) — no work needed.

### Mute-tier redesign (the F-key row) (~2-3d total, ship as one coherent change)

The organizing principle: **unshifted F-key = the interactive channel (things you
asked for), Shift+F-key = the ambient channel (things that happen to you).**

- [x] **Mute-tier redesign DONE 2026-07-21** — all seven items shipped as one
  change: SpeechChannel (Manual/Event/OrderEvent/Critical) gating at the router,
  Shift+F2/Shift+F3 tiers with Critical-channel confirmations, order-outcome
  break-through + speech.muteIncludesOrderEvents opt-in, per-alert
  BreakThroughMutes checkbox, F4 braille toggle w/ platform message,
  ContextSummary → Shift+F1. BONUS FIXES found during the build: FeedbackType.Alert
  had no earcon case (Delivery=Earcon alerts were SILENT in-app — now a real
  rising double-tone, patch key "Alert"); earcons no longer die silently with F3
  (own tier now); modal open/close announcements now respect F2.
- [ ] **Shift+F4 dedicated braille display picker modal** — currently opens the
  Settings dialog (braille fieldset). A real picker needs multi-device
  enumeration in the driver layer; do alongside the next Dot Pad hardware
  session.

### Hosted double-speech (Chrome: Orca + browser TTS both speak) (~1d)

- [x] **Speech output setting** DONE 2026-07-22 — first-visit prompt (browser-TTS
  backends only) + Settings → Speech dropdown via optional IBrowserSpeechOutput;
  localStorage per-browser persistence; ScreenReader mode suppresses browser TTS,
  BrowserVoice mode empties the live region (BlazorSpeechManager.LiveRegionEnabled);
  Both stays the pre-choice default. 6 tests. Cody should verify on the live demo
  in Chrome: choose "screen reader" → single voice.

## [2026-07-10] — Phase E test-debt closure (Finalization plan)

Full detail in `CHANGES.md` [Unreleased]. Suite 1447/1447 xunit + 12/12 JS.

### Shipped

- [x] **Provider contract enrollment** — Binance, IBKR, Schwab, Finnhub, TwelveData,
  Fmp fully enrolled in fetch/live-stream contract tests (63 tests). Mexc partial
  (JK.Mexc.Net owns its HttpClient — no test seam without production changes; helpers
  covered; full enrollment rides the per-plugin-dependency rework below).
- [x] **FMP intraday Limit bug FIXED** — kept oldest bars (`Take`) instead of most
  recent (`TakeLast`); found by the enrollment pass, regression-pinned.
- [x] **AlertEvaluator/AlertOrchestrator** first-ever tests (15) — hysteresis,
  warm-up tick, exception isolation, persistence.
- [x] **CommandDispatcher runtime gates** (18 cases) — chart-focus + data gates,
  NAV/playback routing.
- [x] **SettingsManager** (8) — corrupt-file quarantine, nested paths, demo block.
- [x] **ShortcutManager** (9) — rebind eviction pinned (evicted command left unbound
  — UX sharp edge worth a future prompt), corrupt-file fallback.
- [x] **Hosted auth policy pinned to docs** (7) — Identity/lockout/cookie options
  asserted against SERVER_SETUP.md.

### Follow-ups raised by this pass

- [x] ShortcutManager rebind eviction now REPORTS the stranded command; the Settings
  capture handler announces it (2026-07-10). (A full swap/offer UI is still possible
  later but the silent-loss sharp edge is closed.)
- [x] Wired `node tools/jstests/gesture-tests.mjs` into `.github/workflows/tests.yml`
  (2026-07-10).
- [x] Rendering layer test coverage added (ChartMath forward mappings + renderer smoke
  tests, 56 tests) — closes the "largest lightly-tested class" gap (2026-07-10).
- [ ] Mexc full contract enrollment after per-plugin dependency folders land.
- [x] `ResolveBarColor` direct tests DONE 2026-07-21 — made internal (Core already
  has InternalsVisibleTo), 15 tests pin every condition boundary + rule ordering.

---

## [2026-07-09] — Second passes B2 / C2a / D2 (Finalization plan)

Closes most of the deferred items from the Phase B/C/D sections below (their open
checkboxes are superseded by this section). Full detail in `CHANGES.md` [Unreleased].

### Shipped

- [x] **Hit-tester** (`ChartHitTester`, on-demand — no render-path bookkeeping):
  click near an indicator line focuses that series/component before announcing;
  right-click near a component opens the chart menu directly on it. Imprecise-click
  fallbacks preserved throughout.
- [x] **Shift+click range measurement** — spoken bars/dates/high/low/net-change,
  cursor never moves.
- [x] **Magnet snap** (`drawing.magnetSnap`, default OFF, chart-menu toggle) —
  anchors pull to nearest O/H/L/C within 3% of visible range.
- [x] **Hover sonification** (`accessibility.hoverSonification`, default OFF,
  chart-menu toggle) — one soft tick per hovered bar, pitched to close.
- [x] **Settings search** in F12 (20-entry registry; jump + focus).
- [x] **Text size setting** (`appearance.uiScale`, 85–175%) applied at startup + on change.
- [x] **HiDPI chart rendering** at element size × devicePixelRatio (density-scaled;
  safe fallback) — closes the fuzzy-1280×720 item.
- [x] **JS gesture tests** — zero-dependency node runner
  (`node tools/jstests/gesture-tests.mjs`), 12 tests over tap/drag/long-press/
  double-tap/pinch/wheel variants; closes the no-JS-test-infra gap without npm.
- [x] **Verified already working (stale audit items):** playback advances the
  on-screen cursor bar-by-bar (AudioSequencer → NavigateAction); recommended
  strategy is already surfaced (★ + banner in the Library table).
- [x] 12 new xunit tests; full suite 1326/1326 green.

### Still open (the honest remainder)

- [ ] **Native touch layer** — iOS adjustable `UIAccessibilityElement` + rotor
  custom actions, Android `ExploreByTouchHelper`, on-device VoiceOver/TalkBack
  verification. **Gated on macOS + physical devices**; spec in
  PLATFORM_STRATEGY_AND_ROADMAP §4.
- [x] **Speech-template editor UI** — SUPERSEDED: shipped 2026-07-16 as the
  PropertiesModal Speech tab editing ComponentConfig templates directly (no
  ISpeechTemplateService needed).
- [ ] **Play range** — sequencer needs an end-index concept; shift+click summary
  covers the measurement half.
- [ ] Price-axis drag to scale (needs a manual y-range override in state);
  time-axis drag to zoom.
- [ ] "Recent events" visual journal ticker (visual earcons cover the alerting half).

---

## [2026-07-09] — Multi-disability visual accessibility, opt-in (Finalization plan, Phase D)

Phase D of `docs/FINALIZATION_PLAN.md`. Audio-first stays the default presentation —
every visual accommodation is OFF until the user enables it in F12 → Appearance →
Visual accessibility. Full detail in `CHANGES.md` [Unreleased]. Pending Cody's
real-app verification.

### Shipped

- [x] **Visual earcons** (deaf/HoH) — every earcon mirrors to an on-screen badge via
  `EarconVisualEvent` + `VisualEarconOverlay`; same throttle/enable gates as audio;
  one fade per event, replace-not-stack (WCAG 2.3.1 by construction). Default OFF.
- [x] **Color-vision-safe chart colors** — blue-up/orange-down override for candles +
  direction bars (`ChartTheme.ColorVisionSafe`, `StandardRenderers.ApplyColorVision`);
  survives theme switches; live refresh. Default OFF.
- [x] **Hollow up-candles** — direction by shape alone. Default OFF.
- [x] **`prefers-reduced-motion`** honoured app-wide (no toggle needed — OS setting).
- [x] **Coarse-pointer target sizes** — tabs ≥44 px, buttons ≥40 px on touchscreens.
- [x] **Contrast sweep DONE** — all 41 inline `#888`/`#aaa` foreground literals →
  `var(--text-muted)` (closes the WCAG color contrast sweep item below from 2026-06).
- [x] **Help (F1) getting-started section** with QUICKSTART/USER_MANUAL pointers.
- [x] **Audit correction:** AI Analyst already shows its analysis as text — the June
  claim of speech-only output was wrong; do not re-raise.
- [x] 17 new tests; full suite 1314/1314 green.

### Phase D remaining (deferred, tracked)

- [x] Playback visual cursor — VERIFIED WORKING 2026-07-21: AudioSequencer dispatches
  NavigateAction per point → StateStream → ChartArea's 100ms-throttled browser
  re-render draws the crosshair at the new cursor. No overlay needed.
- [x] Settings search — SHIPPED 2026-07-16 (SettingsModal filter registry).
- [x] In-app UI scale setting — SHIPPED 2026-07-16 (appearance.uiScale, applied at boot).
- [x] Speech-template editor UI — SHIPPED 2026-07-16 as the PropertiesModal Speech tab
  (edits ComponentConfig.SpeechTemplate/SignalSpeechTemplate directly; simpler design
  than the ISpeechTemplateService item below, which it supersedes).
- [ ] "Recent events" visual ticker surfacing the Journal ambiently (deaf/HoH).
- [x] "Use Recommended" preset button DONE 2026-07-21 — BuildSetupTab button loads the
  per-asset/timeframe recommended v23 spec (same logic as the strategy list's ★)
  into the editor via LoadFromSpec; side-aware, spoken confirmation/refusals.
- [x] HiDPI chart rendering at devicePixelRatio — SHIPPED 2026-07-16 (ChartArea).

---

## [2026-07-09] — Touch input, web-first (Finalization plan, Phase C, first pass)

Phase C of `docs/FINALIZATION_PLAN.md`. Web layer shipped; native (MAUI) layer specced
in `PLATFORM_STRATEGY_AND_ROADMAP.md` §4 and gated on macOS/device access. Full detail
in `CHANGES.md` [Unreleased]. Pending real-device verification by Cody.

### Shipped (web: hosted terminal, public demo, and the MAUI apps' WebView)

- [x] **Touch gestures** — tap = select + hear bar; drag = pan; pinch = anchored zoom;
  double-tap = jump to live; long-press = context menu. JS state machine synthesizes
  the mouse bridge calls, so all Phase B pipelines + tests cover the .NET side.
- [x] **Screen-reader bar navigator** — real range input; VO/TalkBack flick up/down =
  step through bars via NavigateAction + standard feedback; valuetext "Bar N of M,
  date, close". iOS ~10% flick granularity documented (TalkBack steps 1).
- [x] **Touch toolbar** (coarse-pointer only) — Prev/Next bar, Prev/Next component,
  Play/Stop, Chart menu; ≥48 px targets; keyboard-command routing.
- [x] **Viewport meta WCAG 1.4.4 fix** — pinch-zoom page magnification unblocked in
  the BlazorWebView host page; `touch-action: none` on the chart zone.
- [x] 13 new tests; full suite 1301/1301 green.

### Phase C second pass (native, deferred — needs macOS + physical devices)

- [ ] iOS: `UIAccessibilityElement` with adjustable trait over the canvas
  (per-bar VoiceOver flicks), `accessibilityCustomActions` rotor entries (next
  component/pane, play, trading ticket, tools), magic-tap = play/stop.
- [ ] Android: `ExploreByTouchHelper` virtual nodes (adjustable first; per-pane
  explore-by-touch later — wants Phase B's render-time hit-test index).
- [ ] On-device VoiceOver + TalkBack verification of the shipped web layer inside the
  MAUI WebView; then update USER_MANUAL's "expected but unverified" wording.
- [ ] JS test infra (Vitest) so the gesture state machine gets direct tests (Tier-4).

---

## [2026-07-09] — Mouse interaction completion (Finalization plan, Phase B, first pass)

Phase B of `docs/FINALIZATION_PLAN.md`. Design rule: every mouse action lands in the
same store state the keyboard navigates — speech + sonification identical for both.
Full engineering detail in `CHANGES.md` [Unreleased]. Pending real-app verification
by Cody.

### Shipped

- [x] **Click a bar to hear it** — click on empty chart space moves the keyboard
  cursor to the clicked bar via the Home/End jump pipeline; spoken + sonified like
  arrow navigation; right-margin clicks are no-ops. (Replaces the accidental
  "click speaks viewport range" behaviour.)
- [x] **Shift+scroll / horizontal trackpad swipe pans through time** — honours the
  user's pan granularity, backfills history near the edge; no button-hold (motor win).
- [x] **Double-click jumps to the live edge** (mouse twin of Backslash).
- [x] **Hover crosshair + readout** — bar-snapped hairline + date/price/OHLC readout
  as real DOM text (magnifier/zoom friendly), aria-hidden, never speaks; toggleable
  from the chart menu; hides on mouseleave.
- [x] **Chart-level right-click menu** — Play from here / Jump to latest / crosshair
  toggle / all series listed BY NAME with Focus, Mute, Hide, Properties, Remove (no
  pixel-precise pointing needed — low-vision/tremor win). Keyboard parity via the
  Application key when no drawing is focused (previously an error message).
- [x] **Bug fix:** right-click was dead from idle state — the DrawingInteractionManager
  fast-reject swallowed ContextMenu events, so the v1.4.0 drawing anchor menu only
  worked mid-drawing. Now pinned by regression test.
- [x] **Shared coordinate math** — MapXToIndex / MapYToPrice / PriceToScreenY moved to
  ChartMath with round-trip tests (linear + log + degenerate guards).
- [x] 35 new tests; full suite 1288/1288 green.

### Phase B second pass (deferred, tracked)

- [x] Render-time hit-test index + click-to-focus-series — SHIPPED in the 2026-07-10
  second pass (ChartHitTester).
- [x] Click-drag range MEASURE (spoken range summary) — SHIPPED 2026-07-10. The
  remaining halves are open below: Play-range needs a sequencer end-index; "backtest
  here" needs date-scoped config plumbed from the selection.
- [ ] Price-axis drag to scale; time-axis drag to zoom; double-click axis to reset.
- [x] Magnet/snap mode for drawing anchors — SHIPPED 2026-07-10 (AppSettings.MagnetSnap).
- [x] Quiet hover-sonification mode (default off) — SHIPPED 2026-07-10
  (accessibility.hoverSonification).

---

## [2026-07-09] — Security hardening (Finalization plan, Phase A)

Phase A of `docs/FINALIZATION_PLAN.md` (the five-area finalization audit: mouse, touch,
UX/disabilities, tests, security). Full rationale per item in `CHANGES.md` [Unreleased].
Pending real-app verification by Cody.

### Shipped

- [x] **Sandbox-or-refuse for custom scripts.** Missing OS sandbox primitive (bwrap /
  sandbox-exec / AppContainer) now throws `ScriptSandboxUnavailableException` with an
  install hint instead of silently running the worker unsandboxed. Explicit opt-out
  `ACCESSIBLETRADER_ALLOW_UNSANDBOXED_SCRIPTS=1`, security-event-logged
  (`SecurityEventKind.UnsandboxedScriptOverride`). New `SandboxPolicy` + refusal tests.
- [x] **WebHost response security headers** (`SecurityHeadersPolicy`): CSP,
  X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Permissions-Policy, HSTS on
  HTTPS. Runtime-verified with curl (headers present; page + static assets 200).
- [x] **API-key metadata encrypted at rest** — moved from plaintext `apikeys_meta.json`
  into `ISecureStorageService` with one-time migration (plaintext deleted only after the
  encrypted write succeeds); mutations serialized under the service lock. 9 new tests.
- [x] **Two-tier auth rate limiting** (`AuthRateLimitPolicy`): general 200/10 s per IP
  unchanged; POST `/account/login` + `/account/register` limited to 10 / 5 min per IP.
- [x] **Monotonic clocks** in `LiveStreamManager` watchdog + `EarconService` throttle
  (`Environment.TickCount64` instead of `DateTime.Now` interval math).
- [x] **Bounded live-stream channels** in `LiveStreamManager` + `DataOrchestrator`
  (1024, drop-oldest).
- [x] **Timeframe validation at the data choke point** (`TimeframeUtility.IsValid`,
  mirroring the existing `SymbolValidator` check, all modes). New tests.
- [x] **Tier-1 money-path unit tests** for `GeneralOrderService`, `PaperTradingProvider`,
  `RiskPercentPositionSizer`, `ApiKeyService`.
- [x] **Verifications closed, no code change needed:** no ISession → audit's
  session-fixation claim not applicable; antiforgery tokens auto-emitted (tag helpers);
  circuit hijacking blocked by SameSite=Lax; dp-keys chmod/backup guidance added to
  SERVER_SETUP.md.

### Deliberately deferred (tracked, not forgotten)

- [ ] Plugin trust manifest cryptographic signing (hash-based TOFU fine until
  third-party plugin distribution).
- [ ] Per-user (post-auth) rate limiting.
- [ ] CAPTCHA-alternative / accessible fallback flow on 429 for shared-IP users.

---

## [2026-07-06] — Mouse pan/zoom, unified Trading/Analytics, hosted paper, tab-bar fix

Targeted for the **1.4.0** release. All shipped items build clean with the full test
suite green (1176 tests, 14 new). Pending real-app verification by Cody.

### Shipped

- [x] **Mouse pan/zoom buttons.** New "Chart view" toolbar group — Pan left / Pan
  right / Zoom in / Zoom out (new SVG glyphs) — routed through `IViewportManager`
  like the keyboard commands (left-edge history backfill + spoken viewport range).
  Works on analytics line charts; disabled until data loads.
- [x] **Click-drag-to-pan.** Added to `DrawingInteractionManager`: with no drawing
  tool armed and no anchor handle grabbed, mouse-down grabs the chart and drag scrolls
  it through time (pixel→bar with fractional carry). Window-level `mouseup` fallback in
  both `keyboard.js` copies ends a drag released off-canvas.
- [x] **Trading + Analytics unified.** Removed the Trading/Analytics mode toggle. Market
  dropdown gains an "Analytics" umbrella → Analytics-type dropdown (Economic/OnChain/
  Derivatives/Sentiment) → Provider. `EffectiveMarket` resolves the concrete category;
  `TerminalMode` is derived (kept for persistence), mode-refresh subscription removed.
- [x] **Paper trading forced on for hosted/demo web.** `GeneralOrderService.IsPaperMode`
  now also true when `!DemoPolicy.AllowLiveTrading`, so `--accounts`/`--demo` always route
  to the paper broker (fixes "provider does not support trading" on Alt+T). Cannot be
  turned off by web users; real-money stays desktop-only.
- [x] **New workspace tab appears immediately.** `TabBar` now subscribes to
  `Store.StateStream`, so a tab added from outside its own DOM events (Ctrl+T /
  Alt+Shift+N / "Open in New Tab") re-renders the bar at once.
- [x] **Toolbar tooltip guard.** Confirmed the Trade button (and every toolbar button)
  renders a hover tooltip + accessible label; added `ToolbarIconButtonTests` so a button
  can't ship without help text.

### Tests added

- [x] `DrawingInteractionManagerMouseDispatchTests` — 3 drag-pan cases (pans on empty
  drag, stops after mouse-up, no pan while a drawing tool is armed).
- [x] `HostedPaperModeTests` — hosted/demo force paper; Full honours the opt-in setting.
- [x] `MarketOrchestratorConsolidationTests` — Analytics umbrella grouping, derived mode,
  provider loading keyed on the concrete category.
- [x] `Blazor/TabBarTests` — new tab renders in the bar without a click.
- [x] `Blazor/ToolbarIconButtonTests` — tooltip/aria contract.

---

## [2026-07-05] — Sound system overhaul

Sound Designer, per-component patches, engine polyphony, and playback fixes.
All shipped items build clean with the full test suite green (1124 tests).
Pending real-app (ear) verification by Cody.

### Shipped

- [x] **Sound Designer preview fixed.** Preview called `PlayNote(…, 0f)` where
  `0f` was the *pan* arg, so noise blend and envelope never reached the engine —
  only waveform/length were audible. Added `ISonificationManager.PlayPatch(patch)`
  (one voice per oscillator layer, carries envelope + noise); modal preview and
  earcon overrides route through it.
- [x] **Multi-oscillator patches.** `SoundPatch` now holds `List<OscillatorLayer>`
  (waveform / Level / Freq Ratio / Noise Blend / Noise Colour) + `EffectiveLayers()`
  back-compat. Sound Designer modal rebuilt with Add Oscillator / per-layer rows /
  Mix section. Multi-osc patches render all layers on components (nav + playback).
- [x] **Sound Designer is general-purpose** — patches assignable to earcons OR
  indicator components, not earcons-only.
- [x] **Per-component patch selection (Properties → Sonification).** Sound Patch
  dropdown (built-ins + user patches) + ▶ preview per component; live-linked via
  `CreateAudioPoint`. `ComponentConfig.SoundPatchId` now surfaced in the UI.
- [x] **Green/red directional patches.** `BullishSoundPatchId`/`BearishSoundPatchId`
  on directional components (candles, bars, histograms, polarity-coloured — via
  `IsDirectional`); bull/bear by `close ≥ open` or `value ≥ ColorBaseline`.
- [x] **128-voice engine (was 64).** Replaced the 64-bit dirty-slot mask with a
  flag array. Slot map: nav 0–15, earcons 16–31, playback 32–95, cloud fills 96–127.
- [x] **Cloud/ribbon fill voices fixed.** `FireCloudVoices` wrote slots 64–79 which
  the old 64-voice engine silently dropped (EMA Fill etc. never sonified) — fixed by
  the 128-voice bump (cloud slots moved to 96–127).
- [x] **"Play all" series/components.** Space plays every visible+unmuted series at
  once, Shift+Space all components of the focused series. Replaced the fixed 4×8 slot
  grid with `BuildVoicePlan` packing into the 64-voice budget; muted series excluded.
  Unified single/multi-series playback onto one plan + `RenderComponentVoices`.
- [x] **Pause no longer drones.** Sequencer silences playback/cloud voices (32–127)
  on pause; nav stays live.
- [x] **Web audio crackle reduced.** `audio.js` MAX_LEAD 80→200 ms + 4 ms declick
  fade only at resync seams (no per-buffer AM buzz).
- [x] **OB/OS zone texture.** Zone noise now `Math.Max`(base, zone) instead of
  replace; bounded-oscillator zone amount 0.12 → 0.3.
- [x] **Directional cross earcons (0-line + OB/OS).** A cross now fires a distinct
  two-note chirp — rising (C5→G5) for an up-cross, falling (G5→C5) for a down-cross —
  on dedicated earcon slots 30/31, during BOTH navigation (arrow onto a cross bar,
  either direction) and playback (Space / Shift+Space / Ctrl+Shift+Space).
  `CreateAudioPoint` now surfaces `AudioPoint.CrossDirection` (= `Sign(val - prevVal)`
  under the existing `triggerClick` PlayEarcon/subscription gating, so it covers
  Zero/Midpoint levels too); `NavigationSonifier` and `AudioSequencer` fire the new
  `CrossEarcon` helper. Previously the cross was only a masked phase-reset click.
- [x] **Removed "for demonstration purposes only" footer.**
- [x] **Escape closes form-heavy modals.** `keyboard.js` swallowed Escape while focus was on a
  `<select>`/`<input>`/`<textarea>` (part of the "let form controls type freely" guard), so the
  Sound Designer couldn't be Escaped out of with a field focused. Escape is now exempt from that
  guard in both WebHost + MAUI `keyboard.js` and reaches the dispatcher's close-modal path.
- [x] **Built-in patch preview in Properties.** The ▶ next to a component's Sound Patch dropdown
  now synthesizes a proper Ping-decay bell (base + harmonic + detune) and plays it via
  `PlayPatch`, instead of a single bare tone that was easy to miss.

### Open

- [x] **NU1903 — SQLitePCLRaw native lib advisory (GHSA-2m69-gcr7-jv3q) fixed.** The advisory
  covers the whole `SQLitePCLRaw.lib.e_sqlite3` 2.1.x line (2.1.6 via Core's EF 8.0.2, 2.1.11 via
  the 10.0.5 refs); even the latest EF Core still pulls 2.1.11 transitively. The patched native
  build is `3.50.3` (SQLitePCLRaw realigned its lib version to the bundled SQLite version). Pinned
  `SQLitePCLRaw.lib.e_sqlite3` to `3.50.3` directly in Core / WebHost / BlazorClient — overrides the
  transitive 2.1.x, clears NU1903, and the managed SQLitePCLRaw layer keeps its own 2.1.x version.
  Native SQLite ABI is stable, so no code change needed; `BackfillManagerTests` (real on-disk SQLite:
  `UseSqlite` + `EnsureCreated` + inserts/queries) passes on the new lib. Remove the pin once EF
  Core's SQLite provider ships a non-flagged native lib. (Core's EF Core 8.0.2-vs-10.0.5 version
  mismatch is unrelated hygiene, left as-is — the app already resolves to 10.0.5 at runtime.)
- [ ] **Multi-layer patch preview vs component parity** — verify by ear that the
  same multi-oscillator patch sounds consistent across Sound Designer preview,
  earcons, navigation, and playback.
- [x] **Sound test coverage.** Added `SoundPatchModelTests` (EffectiveLayers/Clone
  back-compat), `SonificationStrategyPatchTests` (registry-vs-user resolution,
  per-colour, PatchLayers, CrossDirection, zone-max, ResolveComponentVoiceCount),
  `SoundPatchLibraryTests` (CRUD, JSON export/import preserving Oscillators, on-disk
  persistence across instances, legacy-patch load, earcon-override round-trip),
  `EarconServiceTests` (patch→PlayPatch vs default→PlayNote routing), a
  `SoundPatchRegistry.GetPatchIds` test, an `AudioEngine` 128-voice high-slot render
  test, and bUnit for SoundDesignerModal (opens, Add Oscillator) + PropertiesModal
  (Sound Patch dropdown present; green/red only for directional components).
- [ ] **`AudioSequencer.BuildVoicePlan`** slot packing / budget-overflow /
  muted-exclusion is still only covered indirectly — it's private and lives in the
  async playback loop, so it needs an integration-style test via a spy driver.
  Lower priority; the slot-planning logic is simple and adjacent paths are tested.

---

## [2026-05-16] — Linux WebHost port

New ASP.NET Core Blazor Server host (`AccessibleTrader.WebHost`) that
brings the terminal to Linux and any browser-reachable platform. MAUI
heads (Windows/macOS/iOS/Android) untouched — RCL changes are
runtime-gated on `IRuntimePlatform.IsBrowserHost`.

### Shipped (phases L1 → L2 + Orca speech)

- [x] **L1 — WebHost project scaffold.** ASP.NET Core (net10.0) referencing
  the RCL + Core + Sdk + ScriptSandbox. 8 platform-shim services
  (`WebHostAppLogger`, `WebHostPathService` XDG-aware, `WebHostRuntimePlatform`,
  `WebHostMainThreadService`, `WebHostSecureStorageService` via DataProtection,
  `WebHostPluginHttpClientFactory`, `WebHostApiKeyCheckoutAdapter`,
  `WebHostAudioDriver` silent stub). Kestrel @ `http://localhost:5145`, auto-
  opens browser via `xdg-open` / `open` / `start`.
- [x] **L1.5 — Plugin wiring.** 14 provider + 12 analytics ProjectReferences
  duplicated from the MAUI csproj. `HashPluginDlls` inline MSBuild task
  emits `plugins_trusted.manifest` after every build, identical pattern to
  the MAUI head.
- [x] **AppStartupService bootstrap.** Lifetime hook in `Program.cs`
  invokes `IAppStartupService.InitializeAsync()` once Kestrel binds,
  mirroring what `MainPage.xaml.cs` does for MAUI. Without it the
  `DataService` stays uninitialised and every `LoadSymbolsAsync` silently
  returns empty.
- [x] **L2 — Browser chart rendering (server-side PNG).** ChartArea.razor
  renders an `<img>` whose `src` is a base64-encoded PNG produced by the
  same `ChartRenderer.Render(SKCanvas, ...)` MAUI uses. Throttled to
  100 ms via a Reactive subject. Triggers on Store.StateStream,
  RedrawEvent, ThemeService.ThemeChanged. Guarded by
  `IRuntimePlatform.IsBrowserHost` so MAUI keeps its native overlay path.
- [x] **Speech via Orca D-Bus.** `WebHostSpeechManager` decorates
  `BlazorSpeechManager`. Backend chosen at startup: Orca's
  `org.gnome.Orca1.Service.PresentMessage` (preferred — respects Orca's
  voice config), then `spd-say` (SpeechDispatcher default voice),
  then browser `SpeechSynthesis` via `BrowserSpeechBridge` + JS interop.
  Interrupt = `spd-say -S` before the new utterance. Verified on
  Fedora 44 + Orca + voxin.
- [x] **Diagnostic endpoint** `/diag/journal` (dev mode or `--enable-diag`):
  returns last 100 journal entries as JSON for triaging the speech
  pipeline.
- [x] **Tactile decision documented.** Linux uses `NullDotPadNative`
  (same path iOS/macCatalyst take). The official Linux Dot Pad SDK is
  v1.0.0 / 20-cell text strip only / no graphic API. Full tactile on
  Linux is blocked on upstream — track Dot Inc shipping a Linux 3.0.0
  SDK.

### Shipped (continued — L3 + L4 partial)

- [x] **L3 — Audio output via PipeWire / PulseAudio / ALSA.**
  `WebHostAudioDriver` rewritten from L1 silent stub. Constructs an
  internal `AudioEngine`; dedicated pump thread pulls float32 frames
  and pipes them into `pw-cat` (preferred) / `pacat` / `aplay`.
  Backend chosen by file-existence probe at startup. Verified on
  Fedora 44 + PipeWire — sonification + earcons play cleanly.
- [x] **L4 (partial) — Drawing-tool keyboard chords remapped.**
  New `WebHostShortcutRemap` swaps `Ctrl+Shift+letter` → `Alt+Shift+letter`
  for every drawing tool + `DetailedPointSummary` (16 bindings) at
  WebHost startup. Firefox reserves several `Ctrl+Shift+*` chords at
  browser chrome and they are not cancellable from page JS. Remap is
  in-memory only; `shortcuts.json` on disk is not modified. MAUI heads
  untouched. See `docs/SHORTCUTS.md` for the per-host chord table.
- [x] **`ChartCommandManager` exception swallows fixed.** Seven
  `Debug.WriteLine` calls in volume/mute/hide/delete/tool/drawing
  event handlers replaced with `ILogger<ChartCommandManager>?.LogError`.
  Surfaces previously-invisible exceptions in the server log; benefits
  both MAUI and WebHost.

### Shipped (L4 — input polish)

- [x] **L4-B — Mouse coordinate mapping verification (2026-05-16).**
  Pinned the browser mouse pipeline with two test files, 6 cases total:
  - `DrawingInteractionManagerMouseDispatchTests` (4 cases) — verifies
    `(x, y, w, h)` → `(date, price)` → anchor placement through
    `HandleMouseEvent`; covers the fast-reject branches (no pending
    drawing, x past right margin, idle click on empty workspace).
  - `MouseHandlerWiringTests` (2 cases) — `GlobalInputService.InitializeAsync`
    calls JS `accessibleTrader.registerMouseHandler` with the
    `"chart-interact-zone"` DOM id; `BlazorInputService.ProcessMouse`
    forwards `(x, y, type, w, h)` unchanged to `MouseEvent` subscribers.
  Uses bUnit's `JSRuntimeMode.Loose` so awaited `InvokeVoidAsync` calls
  auto-complete without per-call `SetVoidResult()`.
- [x] **L4-C — `pointer-events` decision (2026-05-16).** Kept permanent
  `pointer-events: none` on the chart `<img>`. The img is a child of the
  `chart-interact-zone` div, so mouse events naturally fall through to
  the parent where `keyboard.js`'s `registerMouseHandler` is bound; no
  IsDrawing toggle needed. Comment added to `ChartArea.razor` so a
  future reader sees why the property is fixed.
- [x] **`@onkeydown` element-scope fallback retained on `ChartArea`**
  (decision recorded 2026-05-16). The window-level `keyboard.js` is the
  primary path, but screen readers in browse mode dispatch synthetic
  keydowns at element scope only, and unit tests run without the JS
  bridge — see the dedupe comment at `GlobalInputService.cs:25-31`.
  Removing the element binding would silently regress AT users.

### Shipped 2026-05-16 (high-value unit tests for the WebHost work)

Five test files in `AccessibleTrader.Tests/WebHost/`, 25 new cases,
1032/1032 total passing.

- [x] **`WebHostAudioDriverBackendPickerTests`** (6 cases) — pins
  pw-cat → pacat → aplay priority + argument formatting. Uses
  `Func<string,bool>` predicate; no filesystem touches.
- [x] **`WebHostSpeechManagerBackendSelectionTests`** (5 cases) — pins
  Orca → spd-say → browser ladder with the static `SelectBackend`
  picker. No real `gdbus` / `spd-say` invocation.
- [x] **`WebHostSpeechManagerForwardingTests`** (6 cases) — pins
  decorator contract: inner Speak / Silence always called first so
  journal + ARIA wiring stays alive; OnSpeak / IsSpeechEnabled
  property access forwards transparently. Uses the new internal
  ctor that skips OS probes.
- [x] **`WebHostSecureStorageServiceTests`** (6 cases) — roundtrip,
  missing-key, remove, corrupt-blob graceful-fallback,
  path-traversal-resistant filenames, overwrite. Uses
  `EphemeralDataProtectionProvider` + temp directory.
- [x] **`ChartAreaBrowserCanvasBranchTests`** (2 bUnit cases) — pins
  the MAUI-safety contract: `<img>` element is rendered only when
  `IsBrowserHost=true`. MAUI heads (where `IsBrowserHost=false` via
  default-interface impl) never get the WebHost chart surface
  rendered on top of their native overlay.

Supporting refactors (additive, no production-behaviour change):
- `PickPlayer` / `FindOnPath` made internal, take `Func<string,bool>`.
- `WebHostSpeechManager` gained `public enum SpeechBackend`,
  `internal static SelectBackend(...)`, and an `internal` ctor that
  takes pre-computed probe results.
- `InternalsVisibleTo("AccessibleTrader.Tests")` added to WebHost csproj.
- `AccessibleTrader.Tests` gained a `ProjectReference` to WebHost.

### Medium-value tests still pending

- [x] `WebHostPathServiceXdgTests` DONE 2026-07-21 (3 cases incl. the hosted
  explicit-root ctor) — WebHostPathAndLoggerTests.
- [x] `WebHostAppLoggerDedupTests` DONE 2026-07-21 (3 cases) — same file.
- [ ] `WebHostProgramStartupSmokeTests` (1 case) — boots host via
  `WebApplicationFactory<Program>`, `GET /` returns 200, all DI
  singletons resolvable. Defer until L4-B / L4-C ship so the smoke
  test covers them.
- [ ] `DiagJournalEndpointTests` (2 cases) — empty / newest-first.

### Next — L3 retained for traceability: WebHost audio output

Replace `WebHostAudioDriver`'s silent stub with a real audio backend so
sonification (chart-tone navigation) + earcons (modal open/close, alerts,
boundary hits) work on Linux. Two candidate backends, both viable:

- [x] **(L3-A) Server-side audio via PipeWire / PulseAudio / ALSA.**
  Shipped 2026-05-16 (see earlier section).
- [x] **(L3-B) Browser audio via WebAudio.** Shipped 2026-05-17.
  `WebHostAudioDriver` falls back to a `Subject<byte[]>`-backed
  `WebHostBrowserAudioSink` when `PickPlayer` returns null. New
  `BrowserAudioBridge.razor` subscribes per circuit and forwards each
  ~8 KB chunk as base64 via JS interop to `accessibleTrader.audioPush`
  in `wwwroot/js/audio.js`, which schedules them head-to-tail on
  `AudioContext.nextStartTime` (lazy-init on first user gesture).
  Wall-clock pacing in the pump (~23 ms/chunk) replaces the pipe
  back-pressure the local-sink path got from the OS. Verified on
  Windows + Brave. Used `AudioBufferSourceNode` rather than
  `AudioWorkletNode` — the COOP/COEP / SharedArrayBuffer overhead
  wasn't worth it for the modest 50-100 ms latency budget here.
- [x] **(L3 — combined recommendation)** Both backends shipped with the
  runtime-detection pattern the speech ladder uses. PipeWire/Pulse/ALSA
  when available, WebAudio fallback otherwise; the public-website
  demo deploy will get WebAudio automatically.

### Shipped 2026-05-17 — WebHost Windows fixes

- [x] **Static-assets manifest loads everywhere.** Added
  `builder.WebHost.UseStaticWebAssets()` in `Program.cs` so
  `blazor.web.js`, scoped CSS bundles, and `wwwroot/js/*.js` resolve
  without depending on `ASPNETCORE_ENVIRONMENT=Development`.
  (A local `Properties/launchSettings.json` is recommended for
  `dotnet run` parity with `dotnet watch`, but it's gitignored so it
  doesn't ship.)
- [x] **L3-B browser WebAudio** (above) — closes the Linux/Windows
  audio gap.

### WebHost phases — L5 / L6 / L7 / L8 / L9 (L6 docs is the only one still open)

- [x] **L5 — Linux script sandbox.** *(Shipped 2026-06-25.)*
  `LinuxBwrapLauncher : IScriptWorkerLauncher`
  (`AccessibleTrader.Core/Services/Scripting/LinuxBwrapLauncher.cs`) wraps the
  worker in `bwrap --unshare-all --die-with-parent --new-session --ro-bind / /
  --proc /proc --dev /dev --tmpfs /tmp --chdir <workerDir> -- <worker>`.
  `--unshare-all` removes the network namespace (no exfiltration); `--ro-bind / /`
  makes the filesystem read-only (no write/persist/tamper). Wired into
  `RoslynScriptingService.CreateDefaultLauncher()` for `OperatingSystem.IsLinux()`;
  resolves `bwrap` from `/usr/bin` etc. then PATH and **falls back to
  `DefaultProcessLauncher`** (reporting `SandboxApplied=false`) if the
  `bubblewrap` package isn't installed. Arg-shape pinned by
  `LinuxBwrapLauncherTests`. Hardening follow-ups (deferred, not required by the
  threat model): `--tmpfs` over `$HOME`, `--clearenv`, and a `--seccomp` BPF
  whitelist. **Note: install `bubblewrap` on the Linux host to get the sandbox.**
- [x] **L6 — Docs.** PLATFORMS.md has the Linux compat row + tactile-deferred
  decision (verified 2026-07-21). REMAINING (Cody, external): file the upstream
  issue at `dotincorp/dotpad-sdk-guide` requesting Linux 3.0.0 parity.
- [x] **L7 — Demo deploy.** *(Shipped 2026-06-25/26.)* Implemented as a
  central `DemoPolicy` (`AccessibleTrader.Core/Services/DemoPolicy.cs`)
  running the REAL shell (MainLayout) under a curated whitelist, rather than
  a bespoke `/demo/chart` route: it pins providers/symbols/timeframes/
  indicators, hides trading / order-book / scripts / strategies / alerts /
  AI / api-keys / workspace / settings / sound-designer (drawing tools kept
  on), auto-loads Bitstamp · BTC/USD · 1d, and is **enforced at the
  `MarketOrchestrator` data boundary** (not just hidden buttons). Served
  under `/app/` via `UsePathBase` + base href for the nginx reverse-proxy;
  Twelve Data key seeded from `DEMO_TWELVEDATA_APIKEY` (never committed);
  feedless providers run historical-only. A no-op when `--demo` is absent,
  so the full app is unaffected. (Circuit rate-limiter follow-up tracked under
  L9 below.)
- [ ] **L8 — Desktop shell.** SKIPPED per user choice (2026-05-16).
  Browser auto-launch via `xdg-open` is sufficient. Photino / Avalonia
  shells remain available as future options if the UX warrants it.
- [x] **L9 — Multi-user WebHost (per-circuit scoping).** *(Shipped v1.2.0,
  2026-06-26; verified on the live demo.)* Per-user state services
  (`IWorkspaceStore`, `IEventBus`, orchestrators, data pipeline, input/speech/
  audio, stateful indicator/drawing/strategy services, settings) flipped from
  `Singleton` to `AddScoped` in the WebHost, with a curated Singleton allow-list
  for shared/stateless infra; `ValidateScopes`/`ValidateOnBuild` enforce no
  captive dependencies. `PluginLoaderService` caches discovered types once and
  instantiates per-circuit; pipeline init runs per circuit in `MainLayout`;
  `App.razor` renders with `prerender:false`; a `CircuitHandler` re-applies the
  Firefox shortcut remap per circuit; `AppStartupService` init is idempotent.
  MAUI head untouched (stays single-user/Singleton). Full design + phase log in
  `docs/WEBHOST_MULTI_USER_SCOPING.md`.
  - [x] Blazor Server **circuit/IP rate-limiter** for the public site — shipped in the
    hosted terminal (200 req/10 s per IP + nginx `limit_conn`).
  - [ ] **Shared connection pool** keyed by symbol (per-visitor upstream connections are
    intentional for now; nginx caps the demo at 12 concurrent).
- [x] **L10 — Hosted accounts terminal.** *(Shipped v1.3.0, 2026-06-27.)* Self-hosted
  ASP.NET Core Identity (`--accounts`) + per-user persistence (`UserScopedPathService`),
  three-tier `DemoPolicy.HostMode` (Full/Demo/Hosted — hosted = full app minus desktop-only
  scripts/real-trading/keys/AI), production hardening (DataProtection key persistence,
  per-IP rate limiter, forwarded headers, `/terminal` path base, secure cookie, owner-seed).
  Accessible Razor-Pages login/register/logout. Full design in
  `docs/HOSTED_AUTH_PERSISTENCE_DESIGN.md` + `HOSTED_ACCOUNTS_STRATEGY.md`; deploy in
  `docs/SERVER_SETUP.md`.
  - [ ] **Follow-ups:** transactional **email** (confirmation + password reset); tier
    gating of premium indicators by `AppUser.Tier`; move the shared market-data key under
    the data root; the shared symbol-keyed cache pool (above).

### Cosmetic + known issues

- [ ] **Chart pixel density.** Server renders at fixed 1280×720; CSS
  scales the `<img>` to container size. On HiDPI displays the result is
  fuzzy. Read browser `devicePixelRatio` via JS interop and re-render
  at native size. ~half day's work.
- [x] **Binance plugin load failure on WebHost.** *(Fixed 2026-06-24.)* Was
  `Could not load type 'CryptoExchange.Net.Interfaces.IRestClient' from
  assembly 'CryptoExchange.Net, Version=11.1.0.0'` — a version clash with the
  MEXC plugin's `JK.Mexc.Net` (CryptoExchange.Net 11.x) in the shared plugin
  output dir, which displaced Binance's 7.2.0. Binance was rewritten to call the
  REST/WebSocket API directly (no `Binance.Net` / `CryptoExchange.Net`), removing
  the conflict. **UPDATE 2026-07-25: MEXC was also rewritten to a direct API (spot WS
  is Protobuf, decoded from build-time codegen of the official
  `mexcdevelop/websocket-proto` files), so `CryptoExchange.Net` / `Binance.Net` /
  `JK.Mexc.Net` are now GONE from the tree entirely.** A CI guard
  (`PluginDependencyIsolationTests`) fails the build if any two plugins ever resolve
  the same third-party assembly at different versions. Plugins still share one output
  dir, so the general robustness fix (per-plugin dependency folders + load-context
  resolution) remains open — but there is currently nothing to clash.
- [ ] **Drawing-tool mouse interactions in browser.** With the chart
  `<img>` at `pointer-events: none`, clicks fall through to the
  `chart-interact-zone` div which receives the mouse. Confirm
  drawing-tool placement works end-to-end on the WebHost; if pixel
  coordinates don't map cleanly, may need a JS bridge similar to the
  one MAUI uses for the native overlay.

---

## [2026-05-14] — Dot Pad tactile-display backlog

Multi-session work on Dot Pad 2nd-gen integration. Hardware/SDK facts and
driver-level reliability (reset-before-frame + single-send + wait-for-quiet)
are pinned; the random-pin issue from earlier today is resolved. Revised UX
spec adopted with the user 2026-05-14 evening — captures everything below.

Full design notes and the rationale behind each item live in the
`project-dotpad-dev-2026-05-14` memory file; this list is just the work
units.

### Driver-level (shipped, kept here for traceability)

- [x] **8-dot cell packer** — `DotpadTactileDriver.PackViewport` uses
  columnar bit layout `bit = subY + subX*4`, row-major byte order, verified
  via the calibrator tool's bit-order probe. Bit map:
  bit 0 = top-left, bit 4 = top-right, bits 1-3 = down the left column,
  bits 5-7 = down the right column. Effective canvas = 60×40 dots on the
  30×10-cell Dot Pad 2nd gen.
- [x] **Reset-before-each-frame + WaitForQuiet pattern** —
  `DotpadTactileDriver.RenderViewportAsync` always calls
  `DOT_PAD_RESET_DISPLAY` before `DOT_PAD_DISPLAY_DATA` and waits 500 ms of
  callback silence between each step. Resolves stale-pin leak-through that
  manifested as "random missing dots" symptom.
- [x] **Reset-before-each-strip-update** — `RenderBrailleTextAsync` calls
  `DOT_PAD_RESET_BRAILLE_DISPLAY` before each `DISPLAY_BRAILLE_TEXT` so a
  shorter new string doesn't leave stale cells raised past its end.
- [x] **Multi-send-per-frame rejected** — tested; SDK detects unchanged
  buffers (`DOT_ERROR_DISPLAY_DATA_UNCHAGNED`) and fast re-sends either
  no-op or collide with the first send's in-flight per-line transmission.
  Lock-in comment at `DotpadTactileDriver.cs:32-36`.
- [x] **Dispatch by DisplayType, not Role** — Role=PriceAction was
  catching the close-price line and routing it through OHLC-bar rendering.
  Fixed in `TactileCanvasCoordinator.BuildCanvas` — dispatch is now keyed
  on `ComponentDisplayType`. Regression-pinned in
  `DotpadTactileDriverTests`.
- [x] **Calibrator CLI** at `tools/DotPadCalibrator/Program.cs` — standalone
  test harness reusing `WindowsDotPadNative` directly. Tests: clear, fill,
  bit-order probe, cell-index probe, coordinate dot probe, stripe tests,
  diagonal, strip text, key-listen.
- [x] **NullDotPadNative for non-Windows** — `IDotPadNative` interface
  lets the driver no-op cleanly on Android/iOS/macCatalyst, where
  `DotPadSDK-3.0.0.dll` cannot load.

### Revised UX spec — MVP shipped 2026-05-14 evening

All 6 MVP items below shipped in a single work cycle. 46 dotpad tests
pass; 1004/1004 in the full suite, 0 regressions.

- [x] **Bar/candle rendering rework.** `BuildOhlcCanvas` rebuilt with the
  1-pin-body + 1-pin-vertical-gap + 1-pin-wicks layout. Density rule
  `BarColumn(i, N, cols) = (int)((i + 0.5) * cols / N)` placed every bar
  at exactly one column, capped at `N = min(visibleBars, cols)`. No
  aggregation past N — beyond-N viewports show the rightmost N bars.
  `BuildBarsFromBaseline` / `BuildLineCanvas` / `BuildMarkerDots` /
  `BuildFilledArea` all switched to the same density rule. Lines use a
  Bresenham helper to keep the trace continuous between density cols.
- [x] **Splash mode.** New `GraphicTextRenderer.RenderCentered` with a
  Grade-1 ASCII→8-dot table (lowercase a-z + space). Cold-state branch
  in `BuildCanvas` paints "accessible trade terminal ready" centered in
  the canvas. `BuildStripText` returns `"no chart loaded..."`.
  `SafelyRenderGraphic` no longer short-circuits on empty data.
- [x] **Two-pane top/bottom split.** `BuildCanvas` extracted per-series
  dispatch into `BuildSeriesCanvas`, then composes a top/bottom pair at
  50/50. Tactile cycle (`GetTactileCycle`) filters `CoreSeriesIds.Price`
  out — focusing price falls back to candles. PgDn rule: newly-focused
  goes to bottom; series at cycle index `focused-1` goes to top. Index 0
  case: top = focused, bottom = next in cycle (the candles+volume cold
  load).
- [x] **Strip rework.** `BuildStripText(state, bool showXValue)` now has
  three modes — cold (`"no chart loaded..."`), value-only (default), and
  X-value timestamp (`"mar 12 14:30"`) on cursor move. The coordinator
  subscribes pairwise via `Interlocked.Exchange` cursor-tracking; on a
  ←/→ move it switches to X-value mode and schedules a single 1.5s
  `Observable.Timer` to revert. Rapid cursor moves replace the timer
  rather than stack them.
- [x] **F1-F4 handler + pause flag.** Coordinator now injects
  `ISpeechFeedbackRouter` and `ICommandDispatcher`, subscribes to
  `_driver.KeyPressed`, and routes:
  - F1 → speak series friendly name (or "candles" for primary, "no chart
    loaded" cold).
  - F2 → speak focused component DisplayName (falls back to first
    visible component when none focused).
  - F3 → speak `"{symbol} {timeframe} {provider}"` (or "no chart loaded"
    when identity is empty).
  - F4 → toggle `_isPaused` (volatile bool). While paused,
    `SafelyRenderGraphic` returns early; strip keeps updating. Resume
    re-renders the current state immediately. Auto-cleared on workspace
    identity change via a `Skip(1)` subscription on `Identity` changes.
- [x] **Pan key wiring.** `TactileKey.PanLeft` / `PanRight` →
  `ICommandDispatcher.Dispatch(SystemCommand.PanLeft / PanRight)` —
  identical path to `[` / `]` keyboard shortcuts. Chart pans + tactile
  redraws via the existing viewport-change subscription. `TactileKey.PanAll`
  is intentionally unhandled (no spec yet).

### Empirical verification

- [x] Splash text displays on launch (2026-05-15 device session). Letter
  spacing fix shipped same day after the user reported letters running
  together — now 3-col stride, 20 chars per line.
- [x] Cold strip "no chart loaded..." appears on launch (fix shipped
  2026-05-15 after first device session found it missing).
- [x] Candle body + volume bars look solid and clean (2026-05-15 user
  feedback).
- [x] `h` (hide series) triggers a tactile redraw + the hidden pane
  goes blank (fix shipped 2026-05-15).
- [x] F1-F4 speech also writes to the 20-cell strip (fix shipped
  2026-05-15).
- [x] Up/Down component-nav on candles updates the strip value (was
  stuck on Body's Close — fix shipped 2026-05-15: route by
  DataMapping, not Role).
- [ ] PgDn/PgUp cycle skips price-line and produces sensible top/bottom
  pairs on the actual device.
- [ ] F4 pause resumes cleanly after workspace identity change (auto-reset).
- [ ] Pan keys empirically belong to graphic vs strip on the actual device.
  If they belong to the strip, rewire to strip pager logic instead of
  dispatching `SystemCommand.PanLeft` / `PanRight`.
- [ ] Strip X-value timeout (~1.5 s) feels right under live arrow nav.
- [ ] Body+wick+gap structure is tactile-distinguishable at 4 dot rows
  per cell vs at the full 40-row canvas. Adjust gap-width rule if the
  body+wick separation isn't reading.
- [ ] **Dot Pad X on-device verification.** The Dot Pad X is supported (same
  DotPadSDK-3.0.0 ABI as the 2nd-gen, binds without code changes), but the
  on-device confirmation so far is on the 2nd-gen. If the device reports different cell dimensions via
  `DOT_PAD_GET_DISPLAY_INFO`, the rasterisation math falls out
  automatically. If the bit layout differs at all (e.g. different
  generation firmware), the calibrator tool flags it via the
  bit-order probe.

### Out of scope (deferred to future iterations)

- [ ] **3-pane mode.** F-key chord toggle (e.g. F12) cycling 1 / 2 / 3
  pane layouts. 24 / 8 / 8 row splits for candles + 2 oscillators. 8
  pins is the minimum oscillator height per user spec.
- Dynamic per-series height proportions matching on-screen pane heights —
  user signed off on flat 50/50 for MVP and revisiting later only if
  needed.
- Price-line overlay rendered on top of the candles pane (visual chart
  parity). Currently the candles pane shows OHLC only.
- DOT_PAD_BRAILLE_ASCII_DISPLAY path — currently using
  DOT_PAD_BRAILLE_DISPLAY with Grade-2 SDK translation. Switching to
  ASCII-direct only if Grade-2 fails on a chart label.
- BLE connection path — currently serial-only (`DOT_PAD_CONNECT_SERIAL`).
  Not blocking commercial release.

### Empirical verification (after each ship)

- [ ] Splash actually centers in 60×40 with the chosen font table.
- [ ] PgDn/PgUp cycle skips price-line and produces sensible top/bottom
  pairs.
- [ ] F4 pause resumes cleanly after workspace identity change (auto-reset).
- [ ] Pan keys empirically belong to graphic vs strip on the actual device.
- [ ] Strip X-value timeout (~1.5 s) feels right under live arrow nav.

### Out of scope / future

- Dynamic per-series height proportions matching on-screen pane heights —
  user signed off on flat 50/50 for MVP and revisiting later only if
  needed.
- DOT_PAD_BRAILLE_ASCII_DISPLAY path — currently using
  DOT_PAD_BRAILLE_DISPLAY with Grade-2 SDK translation. Switching to
  ASCII-direct only if Grade-2 fails on a chart label.
- BLE connection path — currently serial-only (`DOT_PAD_CONNECT_SERIAL`).
  Not blocking commercial release.

---

## [2026-04-27 evening 19] — Pre-commercial-release health audit backlog

Six-axis audit (architecture / quality / security / accessibility / robustness / docs)
ran over the full ~540-file solution after the Phase 5 + order book v1 + audio
relocation work. Findings consolidated below as a concrete action plan, ordered
small wins → big tasks. Plan-of-action recorded inline; in-progress items will
flip `[~]` and complete items `[x]` as they ship.

### Quick wins (≤30 min each)

- [x] **App-key opens drawing context menu** — shipped 2026-04-27 e19. New
  `SystemCommand.OpenDrawingContextMenu` bound to `ContextMenu` key + `Shift+F10`
  in `ShortcutManager`. Dispatcher (chart-scoped, GLOBAL section so empty-data
  workspaces still work) publishes `OpenDrawingContextMenuEvent` with sentinel
  `double.NaN` coordinates; `DrawingContextMenu.razor` interprets NaN as
  "self-position center-screen and focus the Delete button" via
  `accessibleTrader.focusElement("drawing-ctx-delete")`. `keyboard.js` traps
  the `ContextMenu` key and normalises to `CONTEXTMENU`. Tested via
  `ModalCloseDispatchTests.OpenDrawingContextMenu_*`.
- [x] **SHORTCUTS.md sync** — verified 2026-04-27 e19. The docs-audit agent
  reported Alt+S / Alt+, / Alt+W / Ctrl+Alt+Shift+J / Alt+A as missing;
  they're actually all present in `docs/SHORTCUTS.md` lines 195, 201, 202,
  204, 205. No changes needed.
- [x] **DrawingContextMenu action confirmation feedback** — shipped 2026-04-27
  e19. `DrawingContextMenu.razor` now injects `ISpeechFeedbackRouter` and
  speaks `"{name} deleted."` after `OnDelete` and `"{name} created."` after
  `OnDuplicate`, queued (interrupt:false) so it follows the modal-close
  announcement instead of clipping it. `OnProperties` already triggers the
  PropertiesModal's own announcement, no change needed there.
  `BlazorTestHarness.cs` extended with an `ISpeechFeedbackRouter` substitute
  for future tests that render DrawingContextMenu transitively. 937/937 tests
  passing, 0 errors.
- [~] **WCAG-compliant color tokens.** Recomputed contrast ratios properly
  2026-04-27 e19. `#aaaaaa` on `#121212` is actually ~8.07:1 — passes AA *and*
  AAA on dark surfaces; the audit agent miscalculated. Real failure was
  `--text-muted: #aaa` rendering on the light `#f2f2f2` modal panel where
  contrast is ~2.08:1 (FAILS AA). **Fix shipped:** `app.css` `.modal-content`
  now scope-overrides `--text-muted: #555` (~6.7:1 on the light modal bg).
  HelpModal.razor:24,228 already use `var(--text-muted, #555)` so they pick
  up the fix. **Remaining:** ~30+ inline `color:#888`/`color:#aaa` literals
  across CustomScriptsModal, ConditionTreeEditor, JournalModal,
  ObjectTreeModal, OrderBookModal, PropertiesModal, SaveWorkspaceModal,
  SettingsModal, SoundDesignerModal, StrategyModal, TradingDashboardModal —
  each renders ~3.2:1 (#888) or ~2.1:1 (#aaa) on the light modal bg. Replace
  with `var(--text-muted)` in a follow-up sweep; some nested dark panels
  (TradingDashboardModal `.panel/.side-btn/.book-spread`) will need
  context-aware substitution.
- [x] **Toolbar button label spot-check.** Verified 2026-04-27 e19. Every
  `ToolbarIconButton` invocation in `Toolbar.razor` passes an explicit friendly
  `Label`, optional `AriaLabel`, and shortcut keys only in `Tooltip` (e.g.
  `Tooltip="Open order book (Alt+B)"`). `ToolbarIconButton.razor:18,31` resolve
  `aria-label` and visible text from `Label`/`AriaLabel` exclusively. No leak
  surface; the user's earlier observation that the regression cleared is
  correct.
- [x] **SVG icon visual polish** — verified 2026-04-27 e19. All 27 symbols in
  `IconSprite.razor` use consistent `viewBox="0 0 28 28"`, `fill="none"`,
  `stroke="currentColor"`, `stroke-width="2"`, rounded caps/joins. Every
  `Icon=` reference in `Toolbar.razor` / `IndicatorBar.razor` resolves to a
  defined symbol. CSS variants (`icon-btn-data/-action/-warning/-danger/
  -neutral/-thought`) drive `--btn-color` which the SVG inherits via
  `currentColor`. Focus indication is a 3px outer ring at variant color
  (`box-shadow` on `.icon-btn-glyph`); hover bumps background alpha. Theme
  blending is correct.

### Critical robustness (MUST ship before commercial release)

- [x] **Esc-to-close modals (single fix, all 17 affected)** — shipped
  2026-04-27 e19. New `SystemCommand.CloseModal` + `CloseTopModalEvent(string?
  ModalName)`. The dispatcher tracks a `Stack<string?>` of modal names from
  every `ModalStateChangedEvent` so it can target only the topmost open modal
  (stacked Help-inside-Strategy closes Help first, leaves Strategy open).
  Pressing Escape with a modal open re-routes `CancelDrawing → CloseModal`
  via the modal-trap path; pressing Escape on a chart with no modal still
  fires `CancelDrawingEvent`. `ModalBase` subscribes once to
  `CloseTopModalEvent` and self-closes the 4 ModalBase users (Alerts /
  AIAnalyst / Save / Load workspace) for free. The 14 inline-publish modals
  each got an explicit subscription that filters by their own ModalName.
  Resolves the TODO.md:1294 Phase 5 follow-up. Tested via 4 new
  `ModalCloseDispatchTests` covering single-modal close, stacked-modal
  topmost-only close, and Escape-rerouting semantics.
- [x] **Drawing-tool commands gated to chart area** — verified 2026-04-27 e19.
  Already correctly categorised in `CommandDispatcher.IsChartScopedCommand`
  lines 584-598: DrawTrend, DrawHorizontal, DrawVertical, DrawChannel,
  DrawFibonacci, DrawLabel, DrawFibExtension, DrawRectangle, DrawGannFan,
  DrawRiskReward, DrawAnchoredVwap, DrawMeasure, DrawGannBox, DrawPitchfork,
  DrawAngleFib, plus CancelDrawing, ConfirmCoordinateEntry. Phase 5 sentinel
  test pins each. Added `OpenDrawingContextMenu` to the same chart-scoped
  list this round. No code changes needed for the original drawing tools.
- [x] **Order placement idempotency + verify-by-ClientOid retry** — shipped
  2026-04-27 e19. `GeneralOrderService.PlaceOrderAsync` now (a) auto-generates
  a `ClientOid` (`atc-{8-byte-hex}`) when the caller hasn't supplied one,
  (b) gates duplicate submits via an in-memory `(provider, ClientOid)` map
  with a 30-second TTL — a UI double-click or post-network-flap retry returns
  `ORDER_DUPLICATE_SUPPRESSED` instead of double-firing, (c) on a mid-submit
  exception scans `GetOpenOrdersAsync(symbol)` for a matching qty/symbol/side
  and returns `ORDER_UNCERTAIN:{exchangeOrderId}` so the user is told to
  verify before retrying. Backed by 7 new tests in `OrderSafetyTests`.
- [x] **Order quantity/price sanity bounds** — shipped 2026-04-27 e19.
  `GeneralOrderService.PlaceOrderAsync` rejects qty ≤ 0 / NaN / ±Infinity /
  > 10,000,000 with `ORDER_REJECTED_QUANTITY` and Limit-style orders missing
  a finite positive Price with `ORDER_REJECTED_PRICE` — both paths log and
  surface a user-facing error before the provider sees the payload. 7 new
  tests in `OrderSafetyTests` pin every rejection path.
- [x] **Bare-catch sweep round 2** — shipped 2026-04-27 e19. Replaced 14 silent
  catches across `BinanceProvider` (8 sites), `AlpacaProvider` (4),
  `TradierProvider` (4), `BitstampProvider` (4), `SchwabProvider` (1) with
  `catch (Exception ex) { _errorStream.OnNext($"... ({ex.GetType().Name}): {ex.Message}"); ... }`
  using each provider's existing error-stream Subject. CancelOrder, GetOrderBook,
  GetSymbols, GetBalances, GetPositions, GetOpenOrders, SetLeverage, FetchOhlcv
  all now surface failures to the UI instead of returning empty/`false`/`1.0`
  silently.
- [x] **Fire-and-forget exception logging** — verified 2026-04-27 e19.
  `AlertDeliveryService.cs:45-64` already wraps each `_ = Task.Run` in a
  try/catch with structured logger + security-event recording; the audit was
  wrong about that one. Provider Task.Run sites (Schwab/Oanda/Tradier/Binance)
  already delegate to inner methods that own their own try/catch with
  `_errorStream.OnNext(...)`. Surface verified clean.
- [x] **WebSocket dispose race** — shipped 2026-04-27 e19.
  `ReconnectingWebSocket` now implements `IAsyncDisposable` alongside
  `IDisposable`; both methods capture references to the receive + heartbeat
  loop tasks at `ConnectAsync` time, await them on dispose (sync `Dispose()`
  bounds the wait at 500ms; `DisposeAsync()` waits unbounded), and the
  receive/heartbeat catch handlers exit cleanly when `_disposed` is set so
  no `_onError` noise fires during teardown.
- [x] **`DataManager._cache` mutation race** — shipped 2026-04-27 e19. New
  `_cacheLock` serialises every write to `_cache` across the four mutation
  sites (refresh / catch-up gap-fill / prepend / live-tick). Reads remain
  lock-free (single-field reference reads are atomic on 64-bit). The live
  tick that previously could race against `PrependOlderDataAsync` and
  silently lose its mutation now blocks for at most one prepend snapshot
  computation.
- [x] **Atomic JSON writes** — shipped 2026-04-27 e19. New
  `AccessibleTrader.Core.Services.AtomicFile` (write-temp + Flush(true) +
  rename) replaces 9 `File.WriteAllText`/`WriteAllTextAsync` sites:
  `ConfigService`, `WorkspaceLibraryService` (×2), `StrategyLibraryFacade`,
  `SoundPatchLibrary` (×2), `SettingsManager`, `ShortcutManager`,
  `JsonStrategyLibrary`, `SpeechTemplateService`,
  `IndicatorPreferencesService`, `ApiKeyService`, `FileCacheService`. A
  power loss or process kill mid-write now leaves either the previous valid
  file or the new valid file — never a half-written JSON.
- [x] **Provider timer disposal** — verified 2026-04-27 e19. The audit cited
  Binance/Mexc/InteractiveBrokers timers as undisposed; on inspection
  `BinanceProvider.cs:296-298`, `MexcProvider.cs:288-290`, and
  `InteractiveBrokersProvider.cs:345-347` all already stop+dispose+null in
  their respective `DisconnectAsync` paths. Added a Debug-level breadcrumb
  to the IB tickle's silent catch so wedged sessions are diagnosable.
- [x] **Schwab OAuth `state` parameter** — shipped 2026-04-27 e19.
  `SchwabOAuthService.RunAuthorizationCodeFlowAsync` now generates a fresh
  `Guid.NewGuid("N")` state per flow, includes it on the authorize URL
  (`BuildAuthorizationUrl(state)`), and refuses any callback whose `state`
  query parameter doesn't match — both with a user-facing HTML response
  and an `InvalidOperationException` thrown from the flow. Closes the CSRF
  gap.

### Important security

- [ ] **Sign ScriptWorker.exe + plugin DLLs.** Use a code-signing cert; verify
  via `WinVerifyTrust` before launching the worker. Closes the supply-chain
  vector flagged in SANDBOX_DESIGN.md:240.
- [ ] **Coinbase + remaining-providers credential-checkout migration.**
  `CoinbaseProvider.cs:95-99` still holds `_apiKey`/`_apiSecret` long-lived.
  Audit every provider against `CREDENTIAL_CHECKOUT_MIGRATION.md` and remove
  the fallback fields where checkout is wired.
- [ ] **CPU quota uses sliding window.** `OutOfProcessScriptHost.cs:262-301`
  kills on a single 2s polling spike; legitimate Kalman/EMA bursts get killed
  unfairly. Require sustained ≥3 consecutive intervals over 0.9 fraction.
- [ ] **Sync-over-async cleanup (3 sites).** `StrategyAutoLoader.cs:73-74`
  (low-risk, startup), `OutOfProcessIndicator.cs:56` (deadlock-risk on
  contention), `LiveStreamManager.cs:267` (disconnect path). Convert callers
  to async or run on a `Task.Run` boundary.

### Build-system safeguards

- [ ] **RCL platform-code Roslyn analyzer.** Error on
  `#if WINDOWS|ANDROID|IOS|MACCATALYST` inside any project whose TFM doesn't
  define those symbols. Prevents recurrence of the 2026-04-27 e18
  silent-audio-disable regression.
- [ ] **DI lifetime validator.** Add a startup pass that walks the registered
  service graph and asserts no Singleton-consumes-Scoped or
  Transient-resolved-from-Singleton patterns. Cheap insurance over the
  ~940-line `ServiceCollectionExtensions.cs`.

### Documentation

- [ ] **Customer-facing README rewrite.** Current README is 90% architecture.
  Add an above-the-fold user section: install/run, supported platforms grid,
  feature checklist, screenshots/demo link, license, contributing.
- [ ] **USER_GUIDE coverage gaps.** Add sections for: strategy building,
  alerts/channels, custom Roslyn indicators, AI Analyst, broker setup,
  journal, sound designer. Current guide stops at navigation/sonification.
- [ ] **Sample plugin DLL.** Ship `Plugins/Samples/SimpleRsiProvider.cs` (or
  similar) with comments referenced from `PLUGIN_AUTHORING.md` Section 9.
  First-party plugin authors currently must reverse-engineer from production.
- [ ] **`Tests/README.md`** — fixture conventions, `BlazorTestHarness` recipe,
  mock-setup checklist for the ~80 test files / 937 tests.

### Architectural follow-ups (post-1.0; large)

- [ ] **God-modal split.** `PropertiesModal.razor` (826 lines),
  `SettingsModal.razor` (770), `StrategyModal.razor` (717),
  `TradingDashboardModal.razor` (~25KB) — each tab → its own component with
  its own injection scope. Reduces shared mutable state and merge-conflict
  surface.
- [ ] **`WorkspaceStore` immutable snapshots.** Replace direct property
  mutation with a reducer pattern + `IObservable<State>`. Fixes the
  20-consumers-mutating-shared-state risk; large refactor but high leverage.
- [ ] **Plugin manifest v2.** Version, signature chain, capability declaration
  (`requires_network`/`_credentials`/`_workspace_write`), expiry. Closes the
  "approved-once-approved-forever" gap in the current SHA-256 manifest.
- [ ] **Modal close-on-Esc + open-on-app-key as single dispatcher cases.**
  Centralize the bug surface that the Phase 5 categorization mostly fixed.
- [ ] **Per-strategy timeout override.** `OutOfProcessScriptHost`
  `DefaultCalculateTimeout` (5s) is hardcoded; legitimate slow strategies
  get killed. Surface as a per-strategy spec field.

---

## [2026-04-27 evening 12] — Round 9: closing the v23 investigation backlog (complete)

Final pass through every open follow-up after round 8. Six concrete deliverables
shipped covering the full open-list at the top of TODO.md across rounds 3-7.
790/790 tests passing. Full writeup in `docs/CHANGES.md` 2026-04-27 evening 12 entry.

- [x] **HIGH-CONVICTION secondary tier in `RollingWindowCommand`.** Added `✓ HIGH-CONV`
  flag (`PctPositive ≥ 0.80 AND CiPositiveWindows ≥ 1 AND AvgTrades ≥ 5`) for
  almost-ROBUST cells with naturally low avgTr but very consistent direction.
  Captures the three v23 round-3/4 near-misses without weakening the strict ROBUST
  bar. VERDICT block reports HIGH-CONV separately from ROBUST.
- [x] **OR-CONF promoted to first-class seed** (`builtin.long.v23or-cipherb-orconf`).
  ETH 1d 100% positive / +0.335R / n=25.3; BTC 1d 73% / 7% CI / +0.188R / n=24.3.
  Highest trade count in the v23a/v23p/v23or family. Wired into `GetAllSeeds()`.
- [x] **BTC_STRENGTH alignment-drift logging** in `WorkspaceFactory.ProjectBtcStrength`.
  Emits `aligned X/N exact, meanDrift=Ys, maxDrift=Zs (source=...)` per projection
  so future cross-provider snapshots that introduce drift surface immediately.
- [x] **Three ETH 4h SHORT confluence cells** added to `StrategyBatteryCommand`:
  v23r+CipherA.Sell, v23r+CipherA.Exhaustion, v23r+CipherSR.Resistance (all
  within 5 bars). Available via `rolling-window --filter "v23r-ASELL,..."`.
- [x] **Alpaca forward-pagination fix** in `SnapshotCommand`. Walks forward from
  a 20-years-ago `Since` instead of backward from `Until` for Alpaca; Bitstamp
  and MEXC retain backward-walk. Equity history backfills end-to-end now.
- [x] **`GetRecommendedV23(Long|Short)Spec`** composite-preset accessor in
  `BuiltInStrategySeeds`. Single call returns fully-resolved spec, prefers
  bars-classified route, falls back to symbol heuristic, falls back to bare v23.
  Closes the "Composite v23 weekly preset" backlog item.

### Genuinely-future research (unblocked, not backlog)

- [ ] **AssetClassifier on equity snapshots.** Pull TSLA/AMZN/SPY via the Alpaca
  fix and verify the volatility/liquidity thresholds (currently crypto-calibrated)
  produce sensible classifications. May need a separate equity calibration track.
- [ ] **ETH 4h SHORT confluence empirical run.** Wire the three new cells to a
  fresh rolling-window pass on `mexc_ETH_USDT_4h.json` to see whether any of the
  confirmation signals rescue 47% positive into HIGH-CONV or ROBUST territory.
- [ ] **v23or weekly cross-asset.** OR-gate may broaden coverage on smaller-cap
  altcoins where v23p over-restricts. Run on KAS/TAO/XRP/LTC weekly to see if
  v23or generalizes the way bare v23 does.

---

## [2026-04-27 evening 4] — v23 round-3: smaller-window CI + cross-asset SHORT + alt funding + asset-aware preset (complete)

Round 3 closes out the v23 investigation. Strict-CI sample-size investigation
(window=800), v23r-SHORT cross-asset, two more funding-gate variants
(all dead), and asset-aware preset selector helper. 757/759 tests passing
(+14 new preset tests). Full table in `docs/CHANGES.md` 2026-04-27 evening 4
entry.

- [x] **Smaller window CI investigation** (`rolling-window --window 800`).
  v23r LONG ETH 1d hit **+0.890R / 100% / 7 windows / 29% CI** — strongest
  individual rolling-window result of the entire investigation. Needs 3 CI
  windows for ROBUST; got 2. Closer than ever.
- [x] **v23r LONG BTC 1d window=800** passes CI count gate (~3 of 29) but
  fails 70% positive gate (62%). Two cells, each missing ROBUST by one
  criterion.
- [x] **v23r-SHORT cross-asset rolling-window.** BTC 4h 81%/16/+0.459R, BTC 1d
  75%/4/+0.305R, ETH 1d 100%/2/+0.664R (n=2 weak). ETH 4h FAILS at
  47%/70/-0.009R — does not generalize to ETH 4h.
- [x] **Three funding-gate variants tested, all dead.** v23rf (raw>0),
  v23rf2 (FundingZ>+0.5), v23rf3 (FundingZ>0): all 0 valid windows on
  BTC 4h+1d. Triple-conjunction of bear-regime + bear-cipher + any-positive-
  funding is structurally too restrictive. Negative result documented.
- [x] **`BuiltInStrategySeeds.GetV23LongPresetForAsset(symbol)` helper.**
  Returns recommended seed ID per asset class: BTC/ETH → v23r-Faber,
  XRP/LTC → bare v23, unknown → bare v23. UI flow: BuildSetupTab calls
  on symbol-select → "Use recommended" button loads in one click.
- [x] **14 new tests** in `BuiltInStrategySeedsPresetTests.cs` covering
  multiple symbol formats, asset-class branches, and null/empty handling.

### Open follow-ups for v23 round-4 (closed in round 9, 2026-04-27 evening 12)

- [x] **Strict-CI gate recalibration.** Shipped HIGH-CONV secondary tier
  (`PctPositive ≥ 0.80 AND CiPositiveWindows ≥ 1 AND AvgTrades ≥ 5`) in
  `RollingWindowCommand`. Captures the three near-miss cells without weakening
  the strict ROBUST bar.
- [x] **Wire preset selector into BuildSetupTab UI.** Round 8 wired
  `GetV23LongPresetForBars` (classifier route) + `GetV23LongPresetForAsset`
  (symbol fallback) into `SummaryExport.razor` and `StrategyModal.razor`.
  Round 9 added `GetRecommendedV23(Long|Short)Spec` as a single-call helper.
- [x] **Investigate ETH 4h short failure.** Three confluence cells added in
  `StrategyBatteryCommand`: v23r+CipherA.Sell, v23r+CipherA.Exhaustion,
  v23r+CipherSR.Resistance. Empirical run still pending (data + rolling-window
  pass tracked under "genuinely-future research" at the top of this file).
- [x] **Composite "v23 weekly preset" seed.** Closed via
  `GetRecommendedV23(Long|Short)Spec` accessor that resolves bars-classified
  → symbol-string → bare-default in one call.

---

## [2026-04-27 evening 3] — v23 round-2 rolling-window + cross-asset + v23rf (complete)

Round 2 deeper-validation. Three parallel investigations: rolling-window all
v23 cells across BTC 4h / BTC 1d / ETH 1d, weekly cross-asset on
XRP/SOL/DOGE/LTC (snapshots aggregated from existing dailies), and
v23rf-SHORT funding-gated variant. 743/745 tests passing. Full table in
`docs/CHANGES.md` 2026-04-27 evening 3 entry.

- [x] **Face-rolling v23 cells across 3 BTC/ETH operating points.** v23 LONG
  cleared 100% of windows positive on ETH 1d (+0.362R) and 87% on BTC 1d
  (+0.248R) — the cleanest window-coverage results of the entire
  investigation. None reach strict ROBUST (need ≥3 CIlo>0 windows; capped
  at 1 because avgTr~27 isn't enough for tight CIs).
- [x] **v23r SHORT BTC 4h rolling-window = 81% / 16 / +0.459R** — second-best
  short-side result in the entire suite, after v22-SHORT BTC 4h's ROBUST
  100% / 16 / +0.79R. Cross-mechanism confirmation that BTC 4h shorts have
  real edge in confirmed bear regimes.
- [x] **Weekly cross-asset survey: v23 LONG generalizes across 4 mature
  cryptos.** XRP 1w: 17 trades / +0.342R / +$323 (best by trade count).
  LTC 1w: 13 / +0.224R / +$83. BTC 1w: 6 / +0.770R / +$241. ETH 1w: 13 /
  +0.491R / +$268. SOL/DOGE 1w have insufficient history (4 years post-2022).
- [x] **Weekly snapshot generation** via existing `aggregate --group 7
  --tf 1w` command — XRP/SOL/DOGE/LTC 1w now in `strategy-lab-data/`.
- [x] **`builtin.short.v23rf-cipherb-funding`** seed shipped. Verdict:
  **structurally dead.** 0 valid windows on every TF tested. The conjunction
  "bear regime AND bear cipher AND funding > 0" almost never coincides
  because bear regimes have negative funding. Same shape of failure as
  v22r-SHORT-bear-funded. Kept as a documented negative result.
- [x] **5 new rolling-window cells** in `StrategyBatteryCommand` (v23 LONG/SHORT,
  v23r LONG/SHORT, v23rf SHORT) using shared `V23BullTrigger` /
  `V23BearTrigger` helpers.
- [x] **Faber gate is asset-dependent.** Helps BTC/ETH (Faber filter
  validated), hurts XRP (4× fewer trades, similar R) and LTC (kills it
  entirely from 13 → 1 trade). XRP and LTC weekly should use bare v23
  (no Faber); BTC/ETH should use v23r.

### Open follow-ups for v23 round-3 (all closed by rounds 4-9)

- [x] **Strict-CI gate sample-size investigation.** Round 4 ran window=800 and
  shipped the three near-miss cells. Round 9 added the HIGH-CONV tier as the
  structural answer to "low avgTr, consistent direction" cells.
- [x] **Asset-aware preset selector.** Shipped in round 4 as
  `GetV23LongPresetForAsset(symbol, timeframe)`; round 6 added the behavior-
  driven `GetV23LongPresetForBars` route; round 9 added the consolidated
  `GetRecommendedV23(Long|Short)Spec` accessor.
- [x] **v23r SHORT cross-asset.** Round 3 already covered: BTC 4h 81%/16/+0.459R
  (promising), BTC 1d 75%/4 (n weak), ETH 1d 100%/2 (n weak), ETH 4h FAILS
  47%/70/-0.009R. ETH 4h confluence cells added in round 9 as the next step.
- [x] **v23rf dead-mechanism investigation.** Round 3 tested two additional
  funding-gate variants (`v23rf2` FundingZ>+0.5, `v23rf3` FundingZ>0) — both
  also dead (0 valid windows). Closed as documented negative result; the
  triple-conjunction is structurally too restrictive on BTC.

---

## [2026-04-27 evening 2] — v23 Cipher B Weekly Reversal seed family (complete)

The structural fix to the weekly-aggregation problem v22 ran into earlier in
the day. v22's event detector loses signal to weekly aggregation; Cipher B's
WaveTrend oscillator is itself a smoothing operation, so its OS/OB semantic
survives. Four new seeds (v23 base + v23r Faber-gated, both sides), tested
across BTC / ETH / XRP at 4h / 1d / 1w. Full writeup in `docs/CHANGES.md`
2026-04-27 evening 2 entry.

- [x] **`builtin.long.v23-cipherb-weekly`** — WT Cross Bull / Blue / Bull
  Divergence within 2 + Anchor Wave < 0. ATR×3 stop, 2R/4R ladder.
- [x] **`builtin.short.v23-cipherb-weekly`** — symmetric. ATR×2.5 stop,
  1.5R/3R ladder.
- [x] **`builtin.long.v23r-cipherb-faber`** — v23-LONG + price > SMA200.
- [x] **`builtin.short.v23r-cipherb-faber`** — v23-SHORT + price < SMA200.
- [x] **Cross-TF + cross-asset validation.** v23 base produces positive
  total P&L on every BTC/ETH TF tested (incl. weekly). v23r-LONG ETH 1d
  is the new top long-side candidate at +0.534R / 4-of-6 / n=15. v23-LONG
  BTC 1w produces 6 trades at +0.770R — first weekly-tradeable signal in
  the suite. Shorts remain weak (consistent with the asymmetry thesis).
- [x] **4th asymmetry-thesis update.** TF-quality is monotonically positive
  for OSCILLATOR detectors (Cipher B) but non-monotonic for EVENT detectors
  (v22). The user's "higher TF = more reliable" intuition was correct all
  along — for the detector types whose math survives aggregation.

### Open follow-ups for v23 (all closed by rounds 3-9)

- [x] **Face-rolling on v23r ETH 1d.** Round 3 ran window=800 and got the
  +0.890R / 100% / 7 windows / 29% CI reading — strongest individual face-
  rolling result of the entire investigation.
- [x] **Weekly cross-asset deeper test.** Round 3 covered XRP/SOL/DOGE/LTC 1w.
  XRP and LTC 1w both produced positive ER (XRP 17 trades / +0.342R; LTC 13 /
  +0.224R). SOL and DOGE 1w insufficient history for a stable read.
- [x] **v23 short-side investigation.** Rounds 3, 4, 7, 8 covered exhaustively.
  Three funding-gate variants all dead. v22-distribution-top BTC 4h is the
  only ROBUST short anywhere. Round 9 adds three new confluence cells targeted
  at the ETH 4h failure (Cipher A.Sell, Cipher A.Exhaustion, Cipher SR.Resistance).
- [x] **v23 gate battery cells.** Five v23 cells added in round 2 plus three
  ETH 4h short cells in round 9. Plus v23h, v23p, v23a, v23or follow-up cells
  across rounds 5-9.
- [x] **v23 production deployment list.** Documented in `docs/CHANGES.md`
  round-7 (round 7 final naming pass) and round-9 (final shipped seed library
  table) entries.

---

## [2026-04-27] — Top/Bottom Detector + v22 reversal seeds (complete 2026-04-27)

First indicator built on the explicit "bottoms are events, tops are processes"
asymmetry thesis. Single new provider, four strategy seeds (v22 + v22r long
and short), one new StrategyLab subcommand (`walk-windows`), two new
StrategyBatteryCommand cells. 739/739 tests green. Full writeup in
`docs/CHANGES.md`.

### Final analysis state (session-end, 2026-04-27)

After two days of iteration through walk → walk-windows → rolling-window
methodology layers, the suite has produced **one ROBUST candidate**, **one
marginal candidate**, **one walk-windows-positive-but-rolling-window-can't-evaluate**,
and **two negative results**:

| Spec / Market         | walk-windows         | rolling-window        | Overall verdict                          |
| --------------------- | -------------------- | ------------------- | ---------------------------------------- |
| **v22-SHORT BTC 4h**  | 3/6 + (smeared)      | **✓ ROBUST**        | **Best result of investigation**         |
| v22-SHORT ETH 4h      | 5/6 + (+0.18R)       | marginal (+0.32R)   | Promising, fails strict gate             |
| v22-LONG BTC 4h       | 4/6 + (+0.22R, n=50) | n=0 valid (filtered)| Real walk-windows signal, rolling-window can't see it |
| v22r-LONG (Faber)     | 5/6 + (+1.03R, n=11) | n=0 valid (filtered)| Quality without quantity — too rare      |
| v22r-SHORT (bear+fund)| 0 trades anywhere    | 0 trades anywhere   | Mechanism dead — gate self-defeating     |

**Headline finding:** v22-SHORT on BTC 4h cleared rolling-window's
same-bootstrap-CI gate that validated Faber-Pulse, at 100% positive ER
across 16 valid rolling windows, mean +0.79R, 3 windows pass strict CI>0.
The mechanism does not generalize cross-asset (BTC ROBUST → ETH marginal
→ XRP coin-flip), so this is a candidate BTC-4h-only strategy, not a
portable signal. Selective firing rate (~22% of rolling 9-month windows)
but reliable when it fires.

**Asymmetry-thesis update:** Sharpens, doesn't reverse. The original
"bottoms are events, tops are processes" frame predicted that
distribution detection would be harder. Empirically, distribution
detection is **selective, not constant** — it works only after enough
rally has accumulated, which is itself a regime-conditional state. So
when the signal fires enough to evaluate, it's reliable; the rest of
the time it's dormant. Not "shorts don't work" but "shorts work
selectively in distribution-rich periods, on assets with cleanest
microstructure (BTC perps)."

**Methodology output:** `walk-windows` subcommand caught my own H1/H2
mistake on the same day it shipped, killed an untested hypothesis built
on a calendar-window cherry-pick artifact, and surfaced two signals the
H1/H2 split had averaged into noise. Future strategy validation should
default to `walk-windows` over `walk`.



- [x] **`TopBottomDetectorProvider`** — `TOP_BOTTOM_DETECTOR` indicator with
  Capitulation Confidence (single-bar event score), Distribution Confidence
  (multi-bar accumulator with exp decay), Bottom Confirmed and Top Confirmed
  signal markers. All math z-score / percentile / ATR-relative — same params
  generalise across 1h/4h/1d.
- [x] **8 unit tests** in `TopBottomDetectorProviderTests.cs` including the
  asymmetry property test (capitulation jitter > distribution jitter).
- [x] **DI registration** in both `ServiceCollectionExtensions.AddIndicatorPipeline`
  (live app) and `LabHost.Build` + `WorkspaceFactory.DefaultIndicatorPack`
  (StrategyLab).
- [x] **`builtin.long.v22-capitulation-bottom`** seed — Buy on Bottom
  Confirmed, ATR×2 stop, 1.5R/3R ladder.
- [x] **`builtin.short.v22-distribution-top`** seed — Sell on Top Confirmed,
  ATR×1.5 stop, 1R/2R ladder (v18 short conventions).
- [x] **Walk-forward verdict shipped:** v22-long has real edge on ETH/XRP
  (both walk-forward halves positive on XRP at both 1d and 4h). v22-short
  does not generalise — no asset shows both halves positive at 1d; ETH 4h
  the only one staying positive both halves but at marginal +0.03R/+0.03R.
  Empirically supports the asymmetry thesis: catching events is a tractable
  problem, catching slow processes without a regime gate is not.

### Open follow-ups for v22 (revised after walk-windows analysis)

- [x] **v22 regime-gated long variant** — shipped as
  `builtin.long.v22r-capitulation-faber`. Walk-windows verdict: high
  per-trade R (+1.03R BTC 4h) but trade count collapses to n=11 over 9
  years; quality without quantity. Faber MA gate is too restrictive on
  top of v22's existing bottom-20% gate.
- [x] **v22 regime-gated short variant** — shipped as
  `builtin.short.v22r-distribution-bear-funded`. Walk-windows verdict:
  **mechanism dead** — fires zero times on BTC 1d/4h and ETH 1d/4h
  across all six windows. The conjunction "bar high in top 20% of
  trailing 100-bar window" AND "price below SMA200" is logically rare
  by construction (if price is in a bear regime, the 100-bar high is
  from before the bear). Closed as a negative result.
- [x] **Bootstrap-CI cells for v22 survivors.** Cells added to
  `StrategyBatteryCommand.BuildCells` (`v22 LONG: TBD Bottom Confirmed`
  and `v22 SHORT: TBD Top Confirmed`). Face-rolling verdict on BTC 4h
  + ETH 4h (full writeup in `docs/CHANGES.md`):
  - **v22-SHORT BTC 4h: ROBUST** under rolling-window's same-gate-as-
    Faber-Pulse: 100% of 16 valid rolling windows positive, 3 windows
    pass strict CI>0, mean +0.79R. The strongest short-side result
    anywhere in the suite. Selective (only 16 of 74 rolling windows
    had ≥5 fires) but reliable when it fires.
  - v22-SHORT ETH 4h: marginal (67% windows positive, 13% pass CI,
    mean +0.32R) — fails the 70%/3-CI ROBUST gate.
  - v22-LONG BTC 4h: too rare for rolling-window's n≥5 valid-window
    gate. 0 valid windows of 74. Needs threshold-loosening or larger
    rolling window before it can be face-rolled.
  - v22-LONG ETH 4h: failed (47% windows positive).
- [x] **Loosen v22-LONG to fit rolling-window.** Approached differently
  than originally planned. Rather than tweaking ConfirmThreshold or the
  rolling-window window, the cross-TF survey (2026-04-27 evening) found
  v22-LONG's true sweet spot is **1d, not 4h** — +0.654R / 4-of-6
  walk-windows / n=10 over 14 years, the best result of the entire
  v22 investigation. 4h's +0.22R is the *degraded-by-noise* version of
  the same setup. Face-rolling on 1d isn't yet wired (n=10 over 14
  years would produce few rolling windows of ≥5 fires either), but the
  walk-windows reading is already strong enough to take v22-LONG-1d as
  a deployable candidate alongside v22-SHORT-4h.
- [x] **Cross-instrument validation for v22-SHORT BTC 4h** — complete
  same-day. Face-rolling on every available 4h snapshot:

  | Market   | Valid | ER>0 | CI>0 | Mean ER | Flag           |
  | -------- | :---: | :--: | :--: | :-----: | -------------- |
  | BTC 4h   | 16    | 100% | 19%  | +0.79R  | ✓ ROBUST       |
  | ETH 4h   | 45    | 67%  | 13%  | +0.32R  | marginal       |
  | XRP 4h   | (74)  | 51%  | 7%   | +0.19R  | coin-flip      |
  | DOGE 4h  | (23)  | 57%  | 0%   | +0.12R  | inconclusive   |
  | SOL 4h   | (26)  | 44%  | 0%   | −0.11R  | fails          |

  Held to the three full-9-year 4h snapshots (BTC / ETH / XRP),
  there is a clean BTC ROBUST → ETH marginal → XRP coin-flip
  gradient. The ROBUST flag is BTC-4h-specific. v22-SHORT BTC 4h is
  a candidate BTC-only deployable strategy, not a portable
  mechanism. The shorter SOL / DOGE histories don't add cleanly to
  the comparison.
- [ ] **Default `walk-windows` over `walk` for strategy validation.**
  The H1/H2 split smears regime-conditional signals into noise (proven
  twice this session: it hid v22-LONG BTC 4h's edge AND it failed to
  catch the cherry-picked nature of the BTC 1d v22-SHORT calendar-
  window result). Update strategy-validation docs / future iterations
  to default to walk-windows; reserve H1/H2 for snapshots too short to
  slice further.
- [ ] **ConfirmThreshold sweep** — try 0.5, 0.6, 0.7, 0.8 on v22-LONG
  BTC 4h to see if higher-conviction fires lift per-trade R. Lower
  priority now that we know v22-LONG's actual sweet spot is 1d, not
  4h. 4h is the noisy/degraded operating point.
- [x] **Cross-pane TBD distribution tint** — shipped 2026-04-27.
  `ChartRenderer.RenderTbdDistributionTint` paints `_crossPaneTbdDistribution`
  from any visible series exposing a `Distribution Confidence` component (e.g.
  `TopBottomDetectorProvider`). Threshold ≥ 0.5; alpha scales with confidence
  (max α=32, soft red). Mirrors the Anchor regime tint architecture.

### [2026-04-27 evening] Cross-TF survey + TimeframeAdaptive scaling — complete

Tested the user's "higher TF = more reliable" hypothesis by running
walk-windows on v22-LONG and v22-SHORT across BTC 4h / 1d / 1w. Result:
the TF-quality relationship is itself asymmetric and *non-monotonic* —
the LONG side (single-bar event) peaks at 1d; the SHORT side (multi-bar
process) peaks at 4h. Past 1d, weekly aggregation begins to blur the
single-bar capitulation event into a normal bar (the indicator originally
fired ZERO times on weekly). Shipped `TimeframeAdaptive` parameter so the
indicator stays useful past its sweet spot. 743/745 tests passing
(2 pre-existing flakes on main, unrelated). Full writeup in
`docs/CHANGES.md` 2026-04-27 evening entry.

- [x] **Cross-TF walk-windows survey on BTC** — 4h / 1d / 1w both sides.
  Headline: v22-LONG sweet spot is **1d (+0.654R, 4/6 windows, n=10)**;
  v22-SHORT sweet spot is **4h (already known ROBUST)**. Weekly LONG
  fires 7× across 14 years at +0.199R after gate adaptation; weekly
  SHORT fires only 2× (too rare to evaluate).
- [x] **`TopBottomDetectorProvider.TimeframeAdaptive` parameter (7th)**
  — auto-detects bar interval (median of first 11 deltas, gap-robust)
  and on TFs ≥ 5 days scales `LookbackWindow` by sqrt of TF ratio
  to 1d, drops `meaningfulRangeAtr` from 5.0 → 2.5×ATR (weekly),
  drops `confirm` by 0.10, and relaxes the volume-z / range-z / RSI
  gates inside the capitulation score itself. **No-op for ≤ 1d** to
  preserve the empirically-best 1d result. Default off in metadata
  (preserves all existing tests); `WorkspaceFactory` opts the lab in
  for `TOP_BOTTOM_DETECTOR` so weekly snapshots produce evaluable
  signals.
- [x] **`DetectBarIntervalMinutes` helper** — `internal static`,
  three unit tests covering 1h / 1d / gap-robust median.
- [x] **6 new TBD unit tests** (8 → 14 total): metadata count update,
  bar-interval detection (3 cases), default-off bit-identical compat,
  daily no-op, weekly relaxation.
- [x] **Asymmetry thesis sharpened twice in one day** — first to
  "distribution is selective, not constant" (morning); then to "the
  TF-quality relationship is itself asymmetric and non-monotonic —
  capitulation peaks at 1d, distribution at 4h" (evening). The user's
  "higher TF = more reliable" intuition holds for trend strategies but
  reverses for event strategies past the aggregation point.

### Open follow-ups for the timeframe work

- [ ] **Cross-asset 1d rolling-window for v22-LONG.** Walk-windows says
  BTC 1d is robust at +0.654R; need rolling-window's bootstrap CI gate
  to confirm before promoting to "deployable." Likely needs a smaller
  rolling window (1500 → 800?) since BTC 1d only has ~5,000 bars and
  the signal fires rarely.
- [ ] **Weekly confirmation across other markets.** With adaptation,
  v22-LONG fires 7× / +0.199R on BTC 1w. Run the same on ETH / XRP /
  LTC weekly snapshots to see if the weekly capitulation pattern
  generalizes or is BTC-specific (we know 4h-SHORT is BTC-specific).
- [ ] **Score-component adaptation review.** The current weekly
  relaxation (volZ 1.5→0.8, RSI 30→40) was chosen by inspection.
  Could be tightened or loosened — try a small sweep to find the
  setting that maximizes per-trade R rather than just trade count.

---

## [2026-04-24] — Icon toolbar system (complete 2026-04-24)

Replaced text-only toolbar + indicator bar with a circular-icon system:
25 custom SVG icons as inline sprite symbols, reusable
`ToolbarIconButton.razor` component, six saturated color variants
(data / action / warning / danger / neutral / thought). Icons paired
with labels — never icon-only — for the low-vision audience. 537/537
tests still green.

- [x] **Inline SVG sprite** at `Components/IconSprite.razor` with 25
  rounded-stroke symbols using `stroke="currentColor"`. Injected once
  from `MainLayout`.
- [x] **`ToolbarIconButton` component** with `Icon` / `Label` /
  `Tooltip` / `AriaLabel` / `Variant` / `IsToggleOn` / `Primary` /
  `Disabled` / `OnClick` parameters.
- [x] **Six CSS variants** with single CSS custom property
  `--btn-color`. Hue never shifts on hover / focus / pressed — only
  alpha + ring intensity. Muscle memory preserved.
- [x] **Toolbar groups** (`.toolbar-group`) separate Mode / Chart
  Setup / Analysis / Workspace / Meta clusters with inset vertical
  rules.
- [x] **3 px focus-visible ring at full variant saturation** replaces
  the 1 px dotted default.
- [x] **`Toolbar.razor` + `IndicatorBar.razor`** rewired to use the new
  component end-to-end.

### Composition-layer fixes (follow-ups, same day)

Shipping the icon toolbar required a six-commit bisection across
composition issues that had been latent since the original
`MainPage.xaml` was written — the text-button toolbar was always
painted over by the Skia canvas, but the app was keyboard-driven +
OCR/screen-reader-readable, so the missing pixels went unnoticed.
Fixed as part of this sweep. Full writeup in
`docs/CHANGES.md` "Icon-toolbar composition fixes" entry.

- [x] **`<base href="/">` + SVG `<use href="#id">` fragment-ref bug**
  — added `xlink:href` shim alongside `href` on every `<use>`.
- [x] **Nested string literals in Razor attribute values** —
  extracted to plain C# computed properties in `@code`.
- [x] **`MainPage.xaml` z-order: canvas-on-top with margin** —
  `BlazorWebView` spans the full Grid, `SKCanvasView` is declared
  after it (top layer) but margin-constrained to the middle chart
  region via `Margin="0,185,0,100"` so the toolbar / header / footer
  / indicator bar from the WebView stay visible above and below.
- [x] **`ChartArea.razor` outer div** → `background: transparent`.
  Previously `black`, left over from the canvas-on-top-without-
  margin era where the outer div was never visible.
- [x] **`IsDataReadyToRender()`** simplified to the same condition
  the canvas uses (`state.Data.Count > 0`). Old logic also required
  the orchestrator state to be `LiveStreaming` / `GapFilling`,
  which kept the blackout-overlay visible while the canvas had
  already started drawing bars.
- [x] **Pixel-perfect canvas sizing via JS-interop bounding-rect**
  — shipped 2026-04-24 (post-toolbar sweep). New
  `ICanvasRegionProvider` bridges Blazor (ResizeObserver) to the
  native `SKCanvasView.Margin`. XAML 185/100 values remain as a
  first-paint fallback.

---

## [2026-04-24] — Visual polish + titlebar/Schwab fixes (complete 2026-04-24)

Post-screenshot-review sweep. 537/537 tests still green.

- [x] **Titlebar stale after timeframe change** — `MainPage.xaml.cs`
  tracked only `_lastTitleSymbol`; changing only the timeframe left the
  titlebar stamped with the previous value. Now change-detects on a
  composite `{Symbol}|{Timeframe}|{Provider}` key.
- [x] **Schwab missing from stocks provider dropdown** — Schwab was in
  `.slnx` but not referenced from `BlazorClient.csproj`, so the
  assembly never shipped next to the host. Added the missing
  `<ProjectReference>` between Polygon and Tradier; plugin trust
  manifest auto-bumps 25 → 26 hashes on the next Release build.
- [x] **Pane legend readability** — `RenderPaneLegend` bg α 180 → 225
  and a 1px subtle border so the legend reads cleanly against bright
  candles / histogram.
- [x] **Y-gridline density** — `BackgroundLayer.Render` gained a
  nice-number gridline algorithm (7 minor steps, every 5th line
  major). Round-number anchors ($25k / $50k, ±50 on oscillators) land
  on major lines.
- [x] **Crosshair halo** — `RenderCrosshair` now paints a 5px white
  40α halo under every crisp crosshair segment (vertical + horizontal
  main + per-indicator-pane horizontal). Crosshair visibility survives
  busy backgrounds.
- [x] **Y-axis swatches at current indicator value** — new
  `RenderYAxisSwatches` draws a 4×3 px colored tick on the left edge
  of each pane's Y-axis strip at every visible Line/Area component's
  most-recent value. Walks back up to 20 bars so warmup NaNs don't
  suppress the tick.

---

## [2026-04-24] — Settings-modal Alerts tab (complete 2026-04-24)

Post-sweep phase 2. Closes the UI gap on the SMTP + Telegram channels
shipped earlier same day. 537/537 tests still green, 0 warnings.

- [x] **Alerts tab in `SettingsModal.razor`** — new sibling tab between
  Keyboard and License. SMTP fieldset (host / port / TLS / username /
  password / from / to) + Telegram fieldset (bot token / chat id) with
  per-channel "Send test" buttons that build a stub `AlertFired` and
  invoke `IAlertChannel.SendAsync` via the DI-registered channel list.
  `PersistAlertSettings()` writes each field through
  `ISettingsManager.SetSetting` + `SaveSettings()` on Close (and before
  Test). The existing `LoadEmailAlertConfig` / `LoadTelegramAlertConfig`
  helpers in `ServiceCollectionExtensions` continue reading the same
  key-paths per-send, so saved values take effect on the very next
  fired alert without any service reload.

---

## [2026-04-24] — Tier 3 sweep (complete 2026-04-24)

Six substantive items landed same-day as the Tier 1 + Tier 2 sweep. 537/537
tests still green. See `docs/CHANGES.md` 2026-04-24 Tier 3 entry.

- [x] **BuildSetupTab UI split** — 1145-line monolith decomposed into
  `ConditionTreeEditor.razor` + `RiskPlanEditor.razor` +
  `SummaryExport.razor` siblings under a thin `BuildSetupTab.razor`
  coordinator. Children take `Spec` by `[Parameter]` and mutate in
  place; parent raises `OnSpecReplaced` on structural load/new/import.
- [x] **`IStrategyModalCoordinator` facade** — StrategyModal @inject
  count 10 → 5. Coordinator wraps Engine + Backtester + WarmupAnalyzer
  + Library + Factory + Roslyn with `StartSpec`/`StopSpec`/`RemoveActive`/
  `TogglePause`/`RecommendedWarmup`/`RunBacktestAsync`/
  `CompileAndAddStrategyAsync`. Structured `StrategyCoordinatorResult`
  per call.
- [x] **Voice-slot pooling** — the `OscillatorVoice[]` array was already
  pool-allocated at ctor; the real hot-path allocation was
  `wave.ToLower()` in `SetVoice`. Extracted `ParseWaveform` with
  `StringComparison.OrdinalIgnoreCase` branches — zero allocations on
  the 300-calls/sec playback path.
- [x] **EventBus throttle/coalesce** — new `SubscribeCoalesced<T>` (Rx
  `Throttle`) + `SubscribeSampled<T>` (Rx `Sample`) convenience
  helpers on `IEventBus`. XML docs forbid using them for accessibility
  events.
- [x] **Script worker CPU quota + per-user worker-count cap.**
  `DefaultMaxCpuFraction = 0.9` polls `TotalProcessorTime` delta /
  wall-clock delta every 2 s; sustained > 0.9 triggers kill + security
  event. `DefaultMaxConcurrentWorkers = 16` with atomic counter in
  `StartAsync`/`DisposeAsync`. `IScriptWorkerProcess.TotalProcessorTime`
  added to contract with `GetProcessTimes` P/Invoke in
  `AppContainerScriptWorkerProcess`.
- [x] **SMTP + Telegram alert delivery.** `IAlertChannel` SDK
  interface; `EmailAlertChannel` (System.Net.Mail) +
  `TelegramAlertChannel` (Bot API) in Core; `AlertDeliveryService`
  subscribes to `AlertFiredEvent` and fans out in parallel
  `Task.Run(...)` with per-channel exception logging + security-event
  records. Eagerly resolved in `MainLayout.razor`. Config loads from
  `ISettingsManager` per-send under `alerts.email.*` / `alerts.telegram.*`.

### Deferred this sweep with refreshed rationale

- [x] **DLL plugin strategies + StrategyIndicatorCache integration +
  IStrategyRegistry.GetCatalog extension** — Phase 10-F complete
  2026-04-24. All three sub-items shipped in a single pass; see
  `docs/CHANGES.md` for the full writeup.
  (a) `IStrategyPlugin` SDK contract + `StrategyPluginRegistry` +
  fixture plugin + 7 loader tests (load / scan / idempotent-init /
  unload+reload / trust-policy enforce / missing-dir tolerance / GC).
  (b) `IPluginStrategyIndicatorCache` SDK mirror + host bridge via
  `PluginHostServices.IndicatorCache` + per-bar `Invalidate` in the
  backtester + pinning test that proves stale-cache-value bug is fixed.
  (c) Unified `StrategyRegistry` merges `IStrategyLibrary.All` +
  plugin templates with spec-wins-on-collision semantics + 5 catalog
  tests.
- [x] **Settings-modal Alerts tab UI** — shipped 2026-04-24 (same day).
  New Alerts tab in `SettingsModal.razor` reads + writes the
  `alerts.email.*` / `alerts.telegram.*` key-paths via `ISettingsManager`
  and exposes a "Send test" button per channel that resolves the live
  `IAlertChannel` from DI.

---

## [2026-04-24] — Tier 1 + Tier 2 sweep (complete 2026-04-24)

10 items shipped from the pre-sweep TODO triage. 537/537 tests pass.
See `docs/CHANGES.md` 2026-04-24 entry for per-item detail + rationale.

- [x] **Ctrl+L/R — focused-series-aware refinement.** Focused-trendline
  walks only that drawing; continuous-points components announce
  "no points of interest on {component}" instead of silently falling
  through to all trendlines.
- [x] **Cipher A WT Momentum Gradient queryable descriptor** (Phase 12).
  Hidden Line component registered so strategies can gate on momentum
  strength (0.0..1.0 normalized) via the standard leaf operators.
- [x] **Bollinger squeeze/expansion + MACD crossover narration.**
  Layered after raw component values in `BarDetailService` for
  Ctrl+Shift+D.
- [x] **Volume-Profile POC crossing alerts.** `AlertTarget.Poc` +
  `ILevelService` injection in `AlertEvaluator` resolve the live POC
  per-evaluation and override the stored threshold.
- [x] **Score + Sequence logic operators exposed in BuildSetupTab.**
  Evaluator already implemented both; the UI now surfaces
  `ScoreThreshold` with a max-score hint.
- [x] **MinLevelStrength UI** for `PriceRejectsLevel` /
  `PriceBreaksLevel` operators.
- [x] **Within-N input** now appears for every operator that consumes it
  (`GreaterThanWithin`, `LessThanWithin`, `BetweenWithin`,
  `PercentileBelow`, `PercentileAbove`).
- [x] **Group expand/collapse disclosure** on condition-tree groups.
  Toggles `aria-expanded` + hides children; evaluation unaffected.
- [x] **Future-space drawing anchors.** `DrawingInteractionManager`
  accepts clicks in the right-margin; anchor dates synthesised via
  median inter-bar delta. `DrawingCalculatorHelper.ResolveAnchorIndex`
  projects future dates to synthetic indices so trendlines keep their
  slope math intact.
- [x] **VPVR backtest replay pinning test** (`VpvrBacktestReplayTests` —
  4 tests). Closes the "most important pending S/R correctness" item.

### Deferred with refreshed rationale (2026-04-24)

- [x] **StrategyModal facade (`IStrategyModalCoordinator`)** — shipped
  2026-04-24 Tier 3 sweep. Wraps Engine + Backtester + WarmupAnalyzer +
  Library + Factory + Roslyn; StrategyModal @inject count 10 → 5.
- *Deferred sub-items collapsed into their canonical entries elsewhere in
  this file (divergence line rendering, cross-pane Anchor cloud tint,
  adaptive WT thresholds, three-tier level-crossing earcons, Custom
  Script Roslyn persistence, `ICustomScriptService.RunScriptAsync` full
  pipeline, Pine `line.new`/`label.new` mapping). Live trendline preview
  shipped 2026-04-24 (Mouse UX sweep). Custom Speech Template Editor
  shipped 2026-04-24 in Indicator Properties modal. Suggestion-mode
  metrics tracked separately at line 1096.*

---

## [2026-04-23] — Unit-test gap analysis (triage)

Produced after Week 4 + file-sink ship. Current coverage: **323 tests**
across 32 test files. Biggest uncovered surfaces below. Tier 1 is
in-flight this session; Tier 2/3 remain backlog.

### Tier 1 — highest risk, highest leverage (complete 2026-04-23)

60 new tests across 4 files; 383/383 total. See `docs/CHANGES.md`
2026-04-23 Tier 1 entry.

- [x] **`WorkspaceStore` + 5 reducers** — `WorkspaceStoreTests.cs`
  (28 tests). Covers every action type plus two concurrency stress
  tests. Pins the post-Week-1 `AddLevelAction` immutability fix.
- [x] **`AudioEngine` synthesis hot path** — `AudioEngineSlotAndPanTests.cs`
  (14 tests). Pan arithmetic, ViewportLength invariant, voice-slot
  isolation, envelope triggering, Reset-silences-output. Added
  `InternalsVisibleTo` so tests can reach `internal AudioConstants`.
- [x] **`DataOrchestrator` resilience** — `DataOrchestratorResilienceTests.cs`
  (8 tests). Per-provider Polly breaker isolation + full DataState
  transition-table pin. Reproduces production config without needing
  the mock farm (HistoricalDataFetcher + LiveStreamManager +
  IDbContextFactory).
- [x] **`StrategyBacktester` correctness** — `StrategyBacktesterTests.cs`
  (10 tests). Warmup gate, stop exits (long+short), single TP, 3-rung
  TP ladder with portion correctness, end-of-data close, date-range
  slicing, equity-curve ordering.

### Tier 2 — meaningful risk, moderate leverage (complete 2026-04-23)

55 new tests across 5 files; 438/438 total. See `docs/CHANGES.md`
2026-04-23 Tier 2 entry.

- [x] **`ConditionEvaluator.HtfLastClosedIndexExclusive`** —
  `ConditionEvaluatorHtfTests.cs` (10 tests). Reflection-tests the
  private binary search for the four called-out edge cases (empty /
  before-all / after-all / perfect-alignment) plus main-bar-between-
  HTF-bars. Behavioural tests confirm HTF price + indicator leaf
  paths clip to the exclusive index and that the per-(leafId,
  timeframe) warning dedup surfaces each distinct leaf exactly once
  via a `TraceListener` capture of `Debug.WriteLine`.
- [x] **`NavigationSonifier` + `AudioSequencer`** —
  `NavigationSonifierClusterTests.cs` (12 tests). Drives
  `FireClusterTicksAsync` against a spy `IAudioDriver` to pin the
  tier-ascending-then-positive-first ordering, NaN + focused-component
  + IsZoneLine + non-marker skip rules, the 5-tick cap on slots 3-7,
  and the navigation-vs-playback cross-series gating. Also pins the
  slot-layout contract: `SyncNavigationSlots` stops slots 2-7 before
  firing slot 0, and `PlayNote` round-robins strictly within 16-31.
- [x] **`IndicatorOrchestrator` incremental path** —
  `IndicatorOrchestratorIncrementalTests.cs` (7 tests). Direct
  coverage for the grow-vs-overwrite branch: same-bar tick overwrite,
  first-tick-of-new-bar grow, slow-data-arrival NaN fill for jumped
  bars, unknown-key silent skip, empty-data early return, cancelled
  token short-circuit, and mixed grow+overwrite across two components
  in one series.
- [x] **`BarDetailService` / `IndicatorContextAnalyzer`** —
  `BarDetailContextTests.cs` (14 tests). Candle-path pattern
  classifications (Marubozu, Hammer, Flat) + wick-percent phrasing,
  indicator-path visible-component value listing, hidden + NaN skip.
  Analyzer coverage: RSI OB/OS/Normal+Rising hints, MACD bullish
  crossover detection, BB upper-band branch, NaN-current-value null,
  out-of-range data-index null, unregistered-indicator fallback to
  first visible component.
- [x] **`SpeechFormatter` strategy chain** —
  `SpeechFormatterDispatchTests.cs` (12 tests). One dispatch test
  per strategy plus priority + token-expansion pins: Hidden wins
  over Cloud when a cloud is hidden; Cloud announces direction +
  width + price-position; PhaseName clamps out-of-range phase
  indices; MarkerSignal expands {name}/{price} and returns
  "no data" when the signal doesn't fire; StandardTemplate handles
  the {value:F1} / ValueOnly / NaN paths as the fallback.

### Tier 3 — lower risk / harder to unit-test (complete 2026-04-23)

41 new tests across 3 files; 479/479 total. See `docs/CHANGES.md`
2026-04-23 Tier 3 entry. Blazor-modal item stays deferred — still
needs a new bUnit dependency.

- [x] **Per-provider symbol normalisation** —
  `ProviderSymbolNormalisationTests.cs` (20 tests). Drives
  `BaseMarketDataProvider.CleanSymbol` via a test-only subclass, Kraken
  `FormatPair`/`FormatRestPair` via reflection (new ProjectReference
  to the Kraken plugin), and the inline Coinbase product-id transform
  as a mirrored reference impl. Test csproj now references
  `AccessibleTrader.Plugins.Kraken` so private statics resolve.
- [x] **Pagination bound sweeps** — `PaginationBoundsTests.cs`
  (9 tests). Reflects `HistoricalDataFetcher.ApplyFinalFilters` (every
  fetch path funnels through it). Pins: since/until inclusive
  boundary, zero-price forming-candle drop, partial-zero drop, limit
  TakeLast (not TakeFirst), limit applied AFTER filtering, empty
  input safe, limit > available returns all.
- [x] **`DrawingService` + calculators** —
  `DrawingCalculatorGeometryTests.cs` (12 tests). TrendLine linear fit
  + extrapolation beyond anchor range + missing-anchor early return;
  Channel baseline/upper/median at configured width + 5%-of-anchor
  fallback; FibRetracement standard levels (0/23.6/38.2/50/61.8/78.6/
  100) + inverted-anchor orientation; FibExtension levels including
  161.8%/261.8%; Rectangle normalises corners + NaN outside date range
  + reversed dates swap; HorizontalLine constant fill +
  missing-anchor early return.
- [x] **Blazor modals — bUnit infrastructure + per-modal sweep complete
  (2026-04-27 e16).** RCL extracted (e15); shared `BlazorTestHarness`
  covers ~15 services + JS interop shim; per-modal tests shipped:
  StrategyModal (5), AlertsModal (6), SettingsModal (12), PropertiesModal
  (12), BuildSetupTab (10). 45 new modal tests total; 835/835 passing.
  Future per-touch coverage areas listed in the round-16 changelog entry
  (PropertiesModal sub-editor flows, BuildSetupTab child delegation,
  SettingsModal General tab, etc.) are now per-feature work, not
  infrastructure backlog.

---

## [2026-04-23] — Post-audit 4-week plan

Independent six-subsystem audit on 2026-04-23 produced an overall grade
of **B**. Week 1 is correctness ship-blockers (started immediately);
Weeks 2-4 are ordered by user impact for a blind trader. See
`docs/CHANGES.md` 2026-04-23 entry for the per-subsystem grades and the
full finding list.

### Week 1 — correctness ship-blockers (complete 2026-04-23)

303/303 tests pass across all 4 TFMs. Five of the seven audit
ship-blockers landed; two were refuted on re-read. See
`docs/CHANGES.md` 2026-04-23 Week 1 entry for the full list.

- [x] **1. Bar X-alignment — audit finding refuted on re-read.**
  `StandardRenderers.cs:252,299` — bars use `x = i*barWidth` as the
  left edge of the cell, then `DrawRect(x+spacing, ..., barWidth-2*spacing, ...)`.
  Rectangle center sits at `i*barWidth + barWidth/2 = i*barWidth + halfBar`,
  which matches the line/dot/candle center anchors exactly. The audit
  mistook the variable's meaning; no code change required. Re-verified
  against `AudioConstants.ComputePanWidth` comment at
  `AudioConstants.cs:14` ("bar at local index k sits at visual
  fraction (k + 0.5) / ViewportLength").
- [x] **2. `SeriesReducer` immutability leak.** Fixed. `AddLevel`,
  `UpdateSeriesZoneBands`, `UpdateSeriesParameters` now each clone the
  target series via `ChartSeries.Clone()`, mutate the clone, and
  replace the target in `ActiveSeries` via `Select`. Stale "triggers
  UI bindings" justifications removed — no consumer subscribes to
  `CollectionChanged` on these collections.
- [x] **3. `IndicatorOrchestrator` incremental array bounds — audit
  finding refuted on re-read.** `IndicatorOrchestrator.cs:246-257` —
  the branch `data.Count > arr.Length` routes first-tick-of-new-bar
  to the grow-and-write path; `data.Count == arr.Length` (same-bar
  tick update) goes to `arr[^1] = kvp.Value`, correctly overwriting
  the current bar. The agent mis-read the branch condition. Logic is
  correct as written.
- [x] **4. IPC decoder bounds checks.** Fixed. Added
  `MaxArrayElements = 1_000_000` cap on every decoded `u32` count via
  `CheckCount(raw, field)`. `ByteReader.EnsureAvailable(n)` is called
  before every primitive read. `ReadString` caps the length field at
  `MaxStringBytes = 64 KB`. Malformed frames now throw typed
  `InvalidDataException` at decode time, not OOM.
- [x] **5. `@key` on live `@foreach` tables.** Fixed. `StrategyModal`
  Library/Active/Trades/bt-spec-dropdown all keyed; `BuildSetupTab`
  library dropdown keyed, and recursive condition-tree `<li>` keyed
  by `node.Id`.
- [x] **6. `LiveStreamManager` zero-value filter.** Fixed.
  `LiveStreamManager.cs:135` now requires all four OHLC legs `> 0`
  and `Volume >= 0` (Volume can legitimately be zero on the first
  tick of a new period for thin books / pre-market).
- [x] **7. `KrakenProvider` nonce.** Fixed. Replaced the
  `Increment` + `Exchange` + `Increment` sequence (which had a TOCTOU
  race producing duplicate nonces under concurrent signers) with a
  `CompareExchange` spin loop that atomically moves `_nonceCounter`
  to `max(current+1, now)`.

### Week 2 — accessibility silent-failure sweep (complete 2026-04-23)

303/303 tests pass. Four shipped, two refuted on re-read. See
`docs/CHANGES.md` 2026-04-23 Week 2 entry.

- [x] **Modal open/close earcons.** Fired from `MainLayout.razor` —
  `Info` on open, `Boundary` on close, before speech.
- [x] **F2 speech-toggle earcon.** Emits immediate `Info` earcon
  alongside speech. F3 sonification toggle deliberately omitted (see
  CHANGES for rationale).
- [x] **Order-failure earcons (single-sink fix).** One fix at
  `AccessibilityFeedbackCoordinator.OnFeedbackRequest` Error-case
  covers all 14 trading providers since they all funnel through
  `IGlobalErrorCoordinator.ReportError` → `FeedbackRequestEvent(Error)`.
- [x] **Cloud NaN guard — already present.** `AudioSequencer.cs:399`
  already has `if (double.IsNaN(signedWidth)) return;`.
- [x] **`SpeechFormatter` exception logging.** `ILogger<SpeechFormatter>`
  injected with parameterless fallback ctor for existing tests. Catch
  block logs at Warning with component + series + dataIndex context.
- [x] **Provider silent-catch audit.** Five critical silent catches
  (Binance/MEXC user-data + keep-alive, Coinbase user-update parse,
  Kraken auth WS + message parsers, TwelveData tick parse) now publish
  to `_errorStream`.

### Week 3 — security + correctness hardening (complete 2026-04-23)

303/303 tests pass. All 8 items shipped. See `docs/CHANGES.md`
2026-04-23 Week 3 entry.

- [x] **Gate `ACCESSIBLETRADER_SCRIPT_IN_PROCESS` behind `#if DEBUG`.**
- [x] **Surface `SandboxApplied=false` in startup UI.** Startup
  advisory via `AnnouncementEvent` + `Alert` earcon when the
  registered launcher is `DefaultProcessLauncher`.
- [x] **FRED + TwelveData API keys in URL.** No header auth available;
  instead scrubbed `ex.Message` → `ex.GetType().Name` in all catches
  so URL with key cannot leak through exception messages.
- [x] **Raw `ex.Message` in order-fail strings.** All 10 trading
  providers (Binance, Bitstamp, Alpaca, Tradier, Oanda, Coinbase,
  IBKR, Schwab, MEXC, Kraken) now return `ORDER_FAILED:{type}` +
  publish typed error to `_errorStream`. Schwab's controlled
  `SchwabReauthRequiredException.Message` kept intact.
- [x] **Cipher S detection race.** Per-symbol lock via
  `ConcurrentDictionary<string, object>`.
- [x] **Bearer-token header interpolation.** Tradier / Oanda /
  Coinbase now use `AuthenticationHeaderValue("Bearer", token)`.
  Polygon + Schwab already correct.
- [x] **Binance listen-key cleanup.** `_listenKey` nulled only on
  `StopUserStreamAsync` success; failure publishes to `_errorStream`.
- [x] **`ReconnectingWebSocket` 10 s connect timeout.** Linked CTS
  with `CancelAfter(ConnectTimeout)` bounds the handshake.

### Week 4 — tests + observability (complete 2026-04-23)

316/316 tests pass (303 → 316; 13 new regression tests). All items
shipped except the optional file-sink documentation. See
`docs/CHANGES.md` 2026-04-23 Week 4 entry.

- [x] **Unit tests for fixed bugs.** 13 new regression tests in
  `PostAuditRegressionTests.cs` covering IPC decoder bounds, Kraken
  nonce CAS idempotence under thread contention,
  `LiveStreamManager` zero-value predicate, and
  `ChartSeries.Clone` collection-isolation invariant.
- [x] **Audio-drop row in Journal Modal.** `IAudioDriver` extended
  with `DroppedCommandCount` / `TotalCommandCount` /
  `ResetAudioTelemetry` as default-interface members;
  `JournalModal.razor` renders a live status row at the bottom with
  a Reset button.
- [x] **Per-session HTF degradation warnings.** Replaced the static
  `_htfWarningLogged` bool with a `ConcurrentDictionary<string, byte>`
  keyed by `leafId|timeframe` so each distinct HTF leaf surfaces at
  least once per session.
- [x] **`ProfileService` null diagnostic logging.** Warning-level log
  with series id + code + bar count before the empty-list fallback.
- [x] **`SecurityEventLog` persistent file sink.** Shipped 2026-04-23
  as `SecurityEventFileSink` decorator — daily-rotated JSONL at
  `%LocalAppData%/AccessibleTrader/SecurityEvents/`, opt-out via
  `ACCESSIBLETRADER_SECURITY_EVENT_PERSIST=0`, dir override via
  `ACCESSIBLETRADER_SECURITY_EVENT_DIR`. 7 new tests.

### Deferred (rationale holds)

- [x] BuildSetupTab UI split — shipped 2026-04-24. Three sibling
  components (`ConditionTreeEditor`, `RiskPlanEditor`, `SummaryExport`)
  under a thin parent coordinator.
- [x] StrategyModal facade extraction — shipped 2026-04-24 as
  `IStrategyModalCoordinator`. Injection count 10 → 5.
- [x] SKPaint pooling — shipped 2026-04-24. New `SKPaintPool`
  (`[ThreadStatic]` stack + `RentedPaint` lease) retrofit into every
  per-bar hot path in `StandardRenderers`. Steady-state alloc count
  drops from ~2500/frame to ≈10 on a busy chart.
  Real GC win but needs profiling first to confirm on target devices.

---

## Next sprint — audit backlog closed (only UI split deferred)

Items 1, 2, 3, 4 all shipped on 2026-04-22. Item 5's Core-side extraction
shipped; the UI split into sibling razor components is deliberately
deferred (see rationale below).

### Shipped 2026-04-22 (post-audit work)

- [x] **1. `SpeechFormatter` strategy registry** — `FormatTemplateValue`
  shrank from ~160-line interleaved conditional to a ~15-line dispatcher
  over five `IComponentSpeechStrategy` implementations
  (`HiddenComponent`, `CloudComponent`, `PhaseName`, `MarkerSignal`,
  `StandardTemplate`). Public `ISpeechFormatter` surface unchanged.
- [x] **2. `WorkspaceStore.Reduce` decomposition** —
  `WorkspaceStore.cs` 893 → 277 lines. Five per-domain reducers under
  `Services/Workspace/Reducers/` (`ViewportReducer`, `SeriesReducer`,
  `PlaybackReducer`, `TabReducer`, `DrawingReducer`); top-level `Reduce`
  is a 30-line dispatcher.
- [x] **3. REST-provider silent-failure sweep** — audit of all 26
  providers found 23 already routed errors through `_errorStream`. The
  three stragglers (`PolygonProvider.FetchOhlcvAsync`,
  `PolygonProvider.GetAvailableSymbolsAsync`,
  `FinnhubProvider.GetAvailableSymbolsAsync`) split into typed handlers.
- [x] **4. CI doc-drift guard** — `scripts/check_doc_drift.py` +
  `.github/workflows/doc-drift.yml`. Verifies shortcut bindings /
  plugin-directory count / live test count against `docs/README.md`
  and `docs/SHORTCUTS.md`.
- [x] **5a. Strategy-spec Core services** — `EditableStrategySpec` POCO
  + `StrategySpecValidator` + `StrategySpecNarrator` +
  `StrategyLibraryFacade` (`IStrategyLibraryFacade`) in
  `Core/Services/Strategies/`. 11 new validator tests
  (`StrategySpecValidatorTests`). `BuildSetupTab.razor` rewired to the
  new services — 1373 → 1037 lines (-25%).

### Deferred (conscious choice, not missed)

- [x] **5b. `BuildSetupTab` UI split into sibling components.**
  Shipped 2026-04-24. `ConditionTreeEditor.razor` +
  `RiskPlanEditor.razor` + `SummaryExport.razor` all exist as siblings
  under a thin parent that owns a single `EditableStrategySpec`. The
  ~30 `@onchange` bindings rewrote to the `Spec.X = v` form. Public
  behavior unchanged; 537/537 tests still green.

### When returning to this project
Audit is closed. Next session starts from a clean "post-audit" baseline —
no pending items. Don't redo the audit; findings are in the
`docs/CHANGES.md` 2026-04-22 entries and the memory file
`project_audit_sprint_2026-04-22.md`.

---

## [2026-04-22] — Pre-release hardening sprint (Day 1–4 complete)

Remediation of the 2026-04-22 full-codebase audit. All four clusters (ship-
blockers, accessibility & resilience, strategy correctness, silent-failure
sweep) landed in a single session. **292 / 292 tests pass** across all 4 TFMs.
See `docs/CHANGES.md` 2026-04-22 for the full diff.

- [x] **Polygon API key moved from URL to Authorization header** — `?apiKey=` removed from all REST call sites; `BuildAuthorizedGet` + `GetAuthorizedStringAsync` helpers added.
- [x] **WebSocket heartbeat sends real bytes** — `ReconnectingWebSocket` was passing `count: 0`; fixed to full payload length. Silent catch scoped + logged.
- [x] **`SymbolValidator` added to SDK** — conservative `[A-Za-z0-9_./:-]{1,32}` allow-list, enforced at `DataOrchestrator.FetchOhlcvAsync` + `StartLiveStreamAsync` choke points. 24 new xunit tests.
- [x] **`IndicatorOrchestrator.ValidateBufferKeys` un-gated from `#if DEBUG`** — mismatched buffer keys now log a Warning in Release, not silently blank a component.
- [x] **Modal open/close announced via ARIA live** — `ModalStateChangedEvent` extended with `ModalName`; 17 modals updated; `MainLayout` routes phrases through the existing speech double-buffered live region.
- [x] **Tab trap inside open modals** — `keyboard.js` capture-phase handler keeps Tab / Shift+Tab inside the last visible `[role="dialog"]`. Covers stacked modals via depth counter.
- [x] **Chart-focus gate on single-letter commands** — `_chartFocused` tracked in `keyboard.js`; single ASCII letters without modifier skip the dispatcher when a modal is open. Form-control guard extended to cover `contentEditable`.
- [x] **`LiveStreamManager.StartLiveStreamAsync` idempotency guard** — re-entry with identical `(provider, market, symbol, timeframe)` on an attached provider no-ops instead of tearing down the subscription.
- [x] **Per-provider circuit breaker** — `DataOrchestrator` now keys its Polly breakers by provider id. One dead source no longer blocks every other provider for 5 s. `ConnectionStatusEvent` carries the provider id.

### Day 4 — silent-failure sweep (complete)

- [x] **HTF pre-warm gate** — `ConfigurableStrategy` tracks pre-warm tasks; `OnBar` blocks HTF evaluation until `IsPrewarmComplete` is `true`; a one-shot speech announcement fires on the first blocked evaluation.
- [x] **Pure-pulse entry trigger: refuse save** — `BuildSetupTab.ValidateForSave` blocks `SaveSpec` / `AddToEngine` with a spoken error when a pulse tree has a non-Immediate trigger. Legacy specs still auto-promote with a one-shot Alert announcement.
- [x] **`AIAnalystService` fallback retry** — `AskAsync` and `AnalyseAsync` iterate the full provider list, retrying on empty response or exception; publish a terminal error when every provider is exhausted.
- [x] **`AudioEngine` command-buffer overflow telemetry** — atomic `DroppedCommandCount` / `TotalCommandCount` counters + `CommandDropped` event; `BlazorAudioDriver` records an `AudioCommandDropped` security-event every 10 drops. 4 new xunit tests.

### Deferred architectural refactors

Canonical list lives in the 2026-04-22 "Deferred (rationale holds)" block
higher in this file — duplicates collapsed 2026-04-23. Status:

- [x] **`WorkspaceStore.Reduce` decomposition** — shipped 2026-04-22 as
  5 per-domain reducers under `Services/Workspace/Reducers/`. Tests:
  `WorkspaceStoreTests.cs` (28).
- [x] **`SpeechFormatter` plugin registry** — shipped 2026-04-22 as the
  5-strategy dispatch chain (`HiddenComponent` / `CloudComponent` /
  `PhaseName` / `MarkerSignal` / `StandardTemplate`). Tests:
  `SpeechFormatterDispatchTests.cs` (12).
- [x] **Symbol-normalization common layer (Tier B.1 — 2026-04-23)** —
  `BaseMarketDataProvider.CleanSymbol` is the shared layer. Coinbase's
  five inline sites consolidated into `ToProductId` private helper
  (2026-04-23). Kraken's `FormatPair` / `FormatRestPair` retained
  (WS-vs-REST wire format is genuinely distinct). Pinned by
  `ProviderSymbolNormalisationTests.cs` (20) + `TierBRegressionTests.cs` (5).
- [x] **Timeframe-map common layer (Tier B.2 — 2026-04-23)** — the
  `Models.TimeframeUtility` regex-based parser is the canonical common
  layer. Legacy `Configuration.TimeframeUtility` flagged `[Obsolete]`;
  Bitstamp migrated to the Models version (and now supports `8h`/`2w`/
  arbitrary tokens the legacy switch couldn't). Per-provider wire-format
  mappings (OANDA `H1`, Kraken `60`) remain inline — deferred until a
  second provider needs the same translation table.

### Silent-failure sweep (cross-cutting, schedule after Day 4)

Every silent-failure path flagged by the audit should either emit an earcon
or a terse speech notification. Catalogue: REST `catch { return (empty) }`
blocks in providers, indicator buffer-key mismatches (now logged but not
user-surfaced), strategy leaf auto-promotion (Day 4 item above), narration
seeding race, AI Analyst fallback (Day 4 item), ring-buffer overflow (Day 4
item). Treat this as one sprint of "every drop-event gets a notification."

---

## [2026-04-21] — Viewport + Home/End + audio-visual sync (complete)

User-reported session: Home/End behavior, right-margin consistency,
audio sonification tracking visual bar positions, drawing-tool shortcut
reliability. All fixes landed in a single session; details in
`docs/CHANGES.md` 2026-04-21 entry.

- [x] **Home/End decoupled from scroll logic** — new `SetCursorAction` + reducer helper `CursorOnlyJump` clamps into `[ViewportStartIndex, ViewportStartIndex + visibleCount - 1]` and bypasses `Navigate()` entirely. End can never advance the viewport; future refactors of scroll logic can't accidentally re-couple them.
- [x] **Right-margin rule rewritten to match TradingView** — `ChartRenderer.Render` takes `Take(effectiveWindow)` at live edge, `Take(viewportLength)` when panned back. Margin exists only as the "future space" at live edge. Renderer path passes `state.RightMarginBars` from MainPage + AIAnalystService.
- [x] **`ViewportNavigationService.Navigate` uses `cursorWindow`** — scroll trigger now matches the renderer's visible bar count (effectiveWindow at live edge, ViewportLength when panned back). Arrow-key navigation inside a panned-back viewport stops scrolling prematurely.
- [x] **Live updates no longer jump focus** — `WorkspaceStore.UpdateData` preserves cursor unconditionally; viewport advances only if it was already showing the live edge.
- [x] **Audio pan = visual position, always** — `AudioConstants.ComputePanWidth` returns `ViewportLength` unconditionally; audio stereo position now matches the candle's x-fraction on the canvas. 5 call sites updated.
- [x] **Crosshair upper-bound clamp** — `RenderCrosshair` clamps `localIndex` to `visibleData.Count - 1` instead of returning early. Guarantees crosshair anchors to a real bar; never renders in the margin.
- [x] **Drawing-tool shortcuts fixed** — `keyboard.js` switched to capture phase + `e.stopImmediatePropagation()` on modifier chords; `trappedKeys` list expanded to cover all drawing-tool letters. WebView2 no longer steals Ctrl+Shift+T before our handler.

### Follow-up (deferred — feature, not bug)

- [x] **Allow drawing anchors in future-space** — shipped 2026-04-24.
  `DrawingInteractionManager.HandleMouseEvent` accepts mouse clicks in
  the right-margin zone; anchor dates synthesised via a median
  inter-bar delta projection. `DrawingCalculatorHelper.ResolveAnchorIndex`
  projects future dates to synthetic indices so trendline slope math
  stays intact when one anchor sits past `Data[^1]`. Mouse-side
  `HandleAddDrawing` keyboard path still clamps to real bars — future
  work when keyboard users want to anchor into the margin directly.

### Follow-up (deferred — UX call)

- [~] **Make `RightMarginBars` a fraction of `ViewportLength` rather than absolute count** — currently hardcoded to 20 bars. At ViewportLength=500 (zoomed out), the margin is only 4% of canvas width; at ViewportLength=100 (default), it's 20%. If the goal is "always ~20% visual gap for projections," switch to `RightMarginFraction = 0.20` and compute `RightMarginBars = ceil(ViewportLength * fraction)` on demand. *Deferred* — 20+ call sites read the field; no user pushback motivates the ripple. Re-open when friction surfaces.

---

## [2026-04-19] — Pre-release quality audit (complete)

Full-codebase audit across Core/SDK, 26 plugins, and the Blazor client.
All flagged issues resolved in a single sweep; build green across all
TFMs, 264/264 tests pass.

- [x] **FMP analytics HttpClient bypass** — `FmpAnalyticsProvider.Configure` was using `new HttpClient()` directly, skipping the phase-4 allow-list / response cap / timeout. Now routes through `PluginHostServices.CreateHttpClient` like every other analytics plugin.
- [x] **Blazor modal event-sub leaks** — three modals (`DrawingToolsModal`, `HelpModal`, `AddIndicatorModal`) had `_eventSub` and a `Dispose()` method but no `@implements IDisposable` directive. Blazor was never calling Dispose; each modal open→close leaked one subscription. Fixed.
- [x] **PropertiesModal ARIA tabs** — screen-reader regression. Tabs were missing `aria-controls`; tabpanel was missing `id` and `aria-labelledby`. Added all three plus a dynamic `ActiveTabId` property driving `aria-labelledby="@ActiveTabId"`.
- [x] **Toolbar `async void` handlers** — `OnMarketChanged` / `OnProviderChanged` / `OnSubTypeChanged` converted from `async void` to `async Task` so exceptions propagate to Blazor's error boundary instead of `SynchronizationContext.UnhandledException`.
- [x] **Missing `@key` on live lists** — order-book bid/ask rows and trading-dashboard balances / positions / open-orders tables got `@key` bindings. Focus + input state no longer corrupt when live ticks reorder the list.
- [x] **Sync-over-async deadlock risk (`LiveStreamManager`)** — implemented `IAsyncDisposable`; kept `IDisposable` fallback but wrapped the provider disconnect in `Task.Run` so the captured `SynchronizationContext` can't deadlock the shutdown path.
- [x] **Sync-over-async in `AnalyticsDataResolver`** — added sync `IDataService.IsProviderConfigured(string)` overload (internal impl is already synchronous, no I/O); resolver uses it directly now. Test mocks updated.
- [x] **Binance pagination defensive comment** — the MEXC pagination fix last session flagged a class of API bug (silent "latest-N" degradation on single-bound queries). Binance is unaffected but structurally identical; added a pointer comment at `BinanceProvider.FetchOhlcvAsync` so future maintainers know where to copy MEXC's bound-computation pattern from if the API behavior ever changes.
- [x] **Silent `catch {}` blocks audited** — 6 sites across Schwab and BinanceVision narrowed to specific exception types where safe (`CryptographicException`, `IOException`, `HttpRequestException`, `InvalidDataException`, `JsonException`) and commented-in-place where the broad catch was the correct call. `SpeechFormatter` catch kept broad but with an explicit justification (accessibility path must never stop emitting audio).
- [x] **MainLayout keyboard-init timeout** — added a 10 s `CancellationTokenSource` + `.WaitAsync(ct)` so a hung JS runtime on first render can't trap initialization indefinitely. `OperationCanceledException` caught separately with a distinct log message.
- [x] **Stale TODO removed** — MacCatalyst `AppDelegate.cs` had a "TODO Phase 7: Wire Mac Catalyst keyboard input" comment; `KeyboardPageHandler` already does this. Replaced with a pointer to the real implementation.
- [x] **Trading-provider interface docs** — added `<summary>` on `GetBalancesAsync` / `GetPositionsAsync` / `GetOpenOrdersAsync` / `CancelOrderAsync` in `ITradingProvider`, noting the MEXC-spot "symbol required" quirk on `GetOpenOrdersAsync`.

### Follow-ups (deferred — architectural decisions pending)

Each item below was surfaced by the audit but intentionally not touched
this session because it requires a design call, not cleanup. The
framing, options considered, and the recommendation are recorded so a
future session can act on them without re-deriving the tradeoffs.

#### 1. Symbol-normalization consolidation (crypto providers)

**State:** Coinbase does `/`→`-`, Bitstamp strip-all + lowercase + `usdt`→`usd`, Kraken has a 3-branch heuristic, Oanda does `_`. Each lives inline in its own class.

**Options considered:**
- **A.** `BaseMarketDataProvider.NormalizeSymbol(string)` virtual, each provider overrides.
- **B.** Static `SymbolNormalizer` class in the SDK with named methods (`SlashToDash`, `StripAndLowercase`, etc.) providers compose.
- **C.** Leave it — the rules are actually different enough that a shared API would be 4 special cases in a trench coat.

**Recommendation:** **C.** The providers *look* duplicative, but the normalization rules are genuinely different. Forcing them through one abstraction would save ~12 lines of code at the cost of indirection and a harder-to-read flow at each call site. Revisit only if a 5th crypto provider lands with the same rule as one of the existing four.

#### 2. Timeframe-mapping consolidation (7+ providers)

**State:** Some providers use an exchange SDK's strongly-typed enum (e.g. `JK.Mexc.Net.Enums.KlineInterval`), not strings, so a shared `Dictionary<string,string>` doesn't fit them. (Binance was rewritten to a direct API and now maps timeframes as plain strings — see its `MapInterval`.)

**What is worth extracting:** `TimeframeDuration(string)` returning a `TimeSpan`. `MexcProvider` already has it for pagination math; it's a pure function with no provider-specific flavor.

**Recommendation:** Lift `TimeframeDuration` (or a `TimeframeUtil` static) onto `BaseMarketDataProvider`. Leave the per-provider enum mappings alone. Small, zero-risk win; ~30 min of work.

#### 3. `BuildSetupTab.razor` decomposition (1,330 lines)

**State:** One cohesive component that builds one `StrategySpec` — strategy metadata, condition-tree editor, leaf/group mutation, risk-plan UI, persistence — all sharing `_spec` in-scope.

**The honest question:** is this file actively hurting us, or just intimidating to read? No open bug reports are blocked on its size today.

**Options considered (if decomposing):**
- **Cascading parameter** of `StrategySpec` — simple, couples children to its shape.
- **EventBus** messages for edits — loose, harder to trace.
- **Explicit `[Parameter]` + `EventCallback`** on sub-components — most idiomatic Blazor, most plumbing.

**Recommendation:** hold off unless a feature is about to land here (e.g. copy-from-existing-strategy flow, template library). The split is 4–6 hours of work whose payoff is "the file is shorter." If/when a feature forces movement, decompose first so the new feature has a clean home. Otherwise leave it.

#### 4. `StrategyModal` → `StrategyFacade` (10 injections)

**State:** `StrategyModal.razor` injects `IStrategyEngine`, `IStrategyBacktester`, `IBacktestWarmupAnalyzer`, `IStrategyLibrary`, `IConfigurableStrategyFactory`, `IRoslynScriptingService`, `ISeriesManagementService`, `IWorkspaceStore`, `IEventBus`, `IJSRuntime`.

**Smell or legitimate?** Each of those is a distinct responsibility the modal genuinely has. The facade doesn't remove work — it relocates it.

**Proposed shape:** `IStrategyModalCoordinator` wraps the 6 strategy-specific services (engine + backtester + warmup + library + factory + roslyn). Modal then injects 5 things (facade + series + workspace + eventbus + jsruntime).

**Recommendation:** worth doing, ~2-3 hours. The real benefit isn't reducing the injection count — it's centralizing the "here's how strategy operations coordinate" logic that currently lives scattered across the modal's event handlers. Good candidate to land **before** any `BuildSetupTab` split if that ever happens, since a clean facade makes the decomposition easier.

---

**Suggested execution order if taking these on:**

1. **#2 first** (30 min, zero risk).
2. **#4 second** (2–3 hr, clean refactor with clear testable boundary).
3. **#3 only when a feature demands it** (4–6 hr).
4. **#1 never, unless a 5th provider arrives with a matching rule.**

---

## [2026-04-18] — MEXC provider + decimal precision overhaul + Cipher C fix (complete)

- [x] **MEXC provider plugin** — `Plugins/Providers/AccessibleTrader.Plugins.Mexc` using `JK.Mexc.Net 5.0.1`. Spot + futures klines, order book, user-data stream, full `ITradingProvider` surface (balances, positions, open orders, place/cancel order, set leverage). Registered in `AccessibleTrader.slnx` AND in `AccessibleTrader.BlazorClient.csproj` `<ProjectReference>` (the MAUI app enumerates plugins explicitly). Trusted-plugin manifest auto-bumped 23 → 25 on build.
- [x] **MEXC pagination fix** — `MaxBarsPerRequest` dropped 1000 → 500 (real API cap); `FetchOhlcvAsync` now computes the missing time bound from `limit × bar-duration` when the caller passes only one, because MEXC's spot klines endpoint silently ignores single-bound queries and falls back to "latest 500". Restores the full available history window (e.g. KAS/USDT daily now goes back to the Dec 2024 listing date instead of ~Sept 2025).
- [x] **Price formatters for the UI.** New `AccessibleTrader.BlazorClient/Services/PriceFormatter.cs` (`FormatPrice`, `FormatQuantity`, `FormatPnL`). Applied to `TradingDashboardModal.razor` (live price, spread, open-order price, balance Free, position qty / PnL) and `StrategyModal.razor` (entry/exit/PnL in summary + details panel + per-trade grid). Sub-dollar assets now display with magnitude-adaptive precision instead of `0.04`.
- [x] **Chart Y-axis + crosshair adaptive precision.** `ChartRenderer.RenderYAxis` and `RenderCrosshair` route through new `FormatAxisValue(val, range)` helper. Formula `decimals = clamp(2 − floor(log10(range)), 2, 10)` — KAS-scale ranges get 4–7 decimals, BTC-scale gets 2.
- [x] **Speech-pipeline adaptive precision.** New `AccessibleTrader.Core/Services/Accessibility/SpeechPriceFormatter.cs`. Applied to `SpeechFormatter` (candle / price-line / profile-bin / heatmap / `{value}` template for price series), `AccessibilityFeedbackCoordinator` (new-bar close/open), `NavigationFeedbackManager` (coordinate entry — was `F0`, rounding sub-dollar to 0), `DrawingInteractionManager` (all anchor announcements), `CipherAProvider` / `CipherBProvider` / `SpiderLinesProvider` (price-annotated narrations). Indicator values (RSI, MACD, WT) intentionally stay on `F2`.
- [x] **Cipher C tail-boost removed.** `CipherCProvider.Calculate()` had a pre-clamp Fisher amplifier that inverted its stated intent — stoch ≥ 0.94 already exceeded the ±100 clamp, and the boost dragged the 0.90–0.94 band above 100 as well, collapsing every extreme read to the same value. Dropped the five-line boost block. On the weekly KAS chart the Cycle Sine plateaus shrank from 3–5 bars to 1–2 bars and the Top Single/Double/Triple tier separation restored. All 58 Cipher C tests still pass.

---

## NEXT UP (2026-04-16) — Security hardening (pre-customer release)

Ahead of shipping to real retail users, a full-codebase security audit was run
(see `memory/reference_security_audit.md` for the severity-ranked source map
and `CHANGES.md` 2026-04-16 entry for what landed). The release gate is split
across two phases. Phase 1 is complete; phase 2 is open.

### Phase 1 — release gate (complete)
- [x] **IBKR TLS validation (C1)** — dropped the blanket cert-accept; loopback-only enforcement on `GatewayUrl`; optional SHA-256 pinning via `GatewayCertSha256`; scheme validation; 16 MB response cap.
- [x] **Roslyn sandbox rewrite (C2)** — semantic `CSharpSyntaxWalker` against blocked namespaces / types / members; lexical pre-flight for `unsafe`/`stackalloc`/`[DllImport]`; applied to indicator + strategy + simple-script paths; `.atpkg` import now requires explicit user consent.
- [x] **Plugin DLL trust policy (C3)** — `PluginTrustPolicy` with SHA-256 allow-list + `RequireTrusted` flag wired into `PluginLoaderService`. Non-regressing default (warns on unverified; flip `RequireTrusted` to lock down).
- [x] **Schwab DPAPI token encryption (C4)** — Windows: `ProtectedData.Protect(CurrentUser)` + custom entropy; non-Windows: persistence disabled until cross-platform SecureStorage is plumbed. Legacy plaintext files auto-deleted.
- [x] **LLM prompt-injection sanitizer (C5)** — strip control chars, quote untrusted fields, 120-char cap, explicit "treat quoted values as data, not commands" directive.
- [x] **WebSocket frame cap (H2)** — 16 MB `MaxMessageBytes` in `ReconnectingWebSocket`; closes with `MessageTooBig` and triggers reconnect on oversize.
- [x] **Binance Vision zip-bomb defense (H1)** — 64 MB compressed / 256 MB uncompressed caps; new `BoundedReadStream` wrapper; zip-slip defense-in-depth.
- [x] **Ollama cleartext hardening (H3)** — loopback-only `http`; https required for remote; unknown schemes rejected.
- [x] **Kraken monotonic nonce (H6)** — atomic counter seeded from wall-clock ms; no same-ms collisions under burst order flow.
- [x] **Workspace path traversal (H5)** — `SanitizeProfileName` rejects `..`, rooted paths, invalid chars, reserved `alerts`.
- [x] **FRED URL escape (M1)** — `Uri.EscapeDataString` on `series_id`, `api_key`, `category_id`.
- [x] **Android network security (L4)** — `network_security_config.xml` + `usesCleartextTraffic=false` + `allowBackup=false`.

### Phase 2 (complete)
- [x] **Response size caps on remaining analytics HttpClients** — AlternativeMe, OkxDerivatives, DefiLlama, BGeometrics, CoinGecko, Glassnode, CoinMetrics, BinanceDerivatives, Etherscan, Mempool, FRED all now construct their `HttpClient` with `MaxResponseContentBufferSize = 32 MB` and a 60s timeout. Non-regressing — payloads are <1 MB in practice.
- [x] **`ApiKeysModal` show/hide removed (M3)** — inputs are always `type="password"`; no more DOM-level reveal toggle. Native OS password-reveal still available at the WebView level. Cleared `_showApiKey` / `_showSecret` / `_showPassphrase` fields and their resets.
- [x] **Plugin trust hash manifest** — `PluginTrustPolicy.LoadManifest(path)` parses a `plugins_trusted.manifest` file (hex SHA-256 digests, one per line, `#` comments). Wired into `ServiceCollectionExtensions.AddDataPipeline` to load from `AppContext.BaseDirectory` at startup. `ACCESSIBLETRADER_REQUIRE_TRUSTED_PLUGINS=1` env var flips `RequireTrusted`. Build-time generator ships as `tools/generate-plugin-trust-manifest.{ps1,sh}` (both Windows and POSIX). Hash the manifest after each Release build and ship it alongside the app; unverified DLLs log a warning (or are blocked under the env var).
- [x] **StrategyLab dev CLI size caps** — `BinanceVisionFundingCommand.cs` and `BinanceVisionOiCommand.cs` now use `MaxResponseContentBufferSize` + a local `BoundedStream` zip-bomb guard mirroring the plugin pattern.

### Phase 3 (complete 2026-04-17)
- [x] **Auto-generated plugin trust manifest on Release build** — `GeneratePluginTrustManifest` MSBuild target in `AccessibleTrader.BlazorClient.csproj` uses an inline `RoslynCodeTaskFactory` task to walk `$(OutDir)` after each Release build, hash every `AccessibleTrader.Plugins.*.dll`, and emit `plugins_trusted.manifest` next to the shipped binary. No external scripts required; works on any build agent.
- [x] **Schwab cross-platform SecureStorage via `PluginHostServices`** — new `IPluginSecureStorage` + `PluginHostServices` in `AccessibleTrader.Sdk.Services`. `MauiSecureStorageService` now implements both the Core `ISecureStorageService` and the plugin-facing `IPluginSecureStorage`; DI forwards both to the same singleton. `MauiProgram.CreateMauiApp` sets `PluginHostServices.SecureStorage` after container build. `SchwabOAuthService` now persists refresh tokens via the host bridge on every platform, with DPAPI-on-Windows as a fallback and a migration path from legacy DPAPI files into the bridge.
- [x] **Credential scrub on disconnect (H4 pragmatic)** — new `BaseMarketDataProvider.ScrubCredentials` helper with a best-effort gen-0 GC hint. Wired into `DisconnectAsync` for every trading-funds provider (Binance, Coinbase, Kraken, Bitstamp, Alpaca, Schwab). Drops GC roots so crash dumps post-disconnect don't leak live credentials. True in-place zeroing requires fetch-on-demand — deferred to phase 4.
- [x] **Out-of-process Roslyn sandbox design doc** — new `SANDBOX_DESIGN.md` specs the worker-process IPC contract, per-platform OS sandbox (Windows AppContainer, macOS `sandbox-exec`, Android `isolatedProcess`, Linux seccomp-bpf, iOS deferral), resource quotas, threat-model delta, and 5-week rollout plan. Design only.

### Phase 4 — Track A (complete 2026-04-17)
- [x] **iOS `.atpkg` and script compile refusal (A1)** — `CustomScriptsModal.razor` guards `ImportAtpkgFromFile`, `ImportAtpkgJson`, and `CompileScript` with a `DevicePlatform.iOS` check. Every path into `RoslynScriptingService.CompileIndicatorAsync` is refused outright on iOS; textarea still works for editing.
- [x] **Manifest target runs on every config (A2a)** — dropped the Release-only condition so Debug builds also produce `plugins_trusted.manifest`. Keeps the dev workflow in sync with the new shipping default.
- [x] **`PluginTrustPolicy.RequireTrusted` default flipped to `true` (A2b)** — a missing manifest now refuses every plugin (intentional fail-closed). `ACCESSIBLETRADER_ALLOW_UNVERIFIED_PLUGINS=1` env var bypasses with a loud warning; `ACCESSIBLETRADER_REQUIRE_TRUSTED_PLUGINS=1` kept for back-compat.
- [x] **GitHub Actions workflow `plugin-manifest.yml` (A2c)** — PR + push + tag triggers, Windows Release build, sanity-checks manifest has ≥10 hash entries, uploads as workflow artifact, attaches to GitHub Release on `v*` tags.

### Phase 4 — Track B (complete 2026-04-17)
- [x] **`IApiKeyCheckout` + `PluginHostServices.ApiKeys` (B0+B1)** — SDK interface + host adapter + Kraken canary. Per-request checkout with graceful fallback to Configure-populated fields when the host bridge is null. Best-effort `Array.Clear` on the decoded HMAC secret after signing. Migration recipe in `CREDENTIAL_CHECKOUT_MIGRATION.md`.
- [x] **`IPluginHttpClientFactory` + `PluginHostServices.HttpClientFactory` (B0+B2)** — SDK interface + host adapter + outbound-host allow-list `DelegatingHandler`. All 12 analytics providers migrated to `PluginHostServices.CreateHttpClient(providerId, allowedHosts)`.
- [x] **Remaining trading providers migrated (future)** — Binance / Coinbase / Bitstamp / Alpaca / Schwab / IBKR stay on the phase-3 scrub-on-disconnect pattern. Status matrix + per-provider notes in `CREDENTIAL_CHECKOUT_MIGRATION.md`. Drive migration order by actual user exposure; Coinbase + Bitstamp are the cleanest next canary candidates since they have explicit sign-per-request code like Kraken.

### Phase 4 — Track C (process-boundary landed 2026-04-17)
- [x] **Worker skeleton + stdio IPC (C1)** — new `AccessibleTrader.ScriptSandbox` contract library + `AccessibleTrader.ScriptWorker` console app. Binary frame codec (4-byte length + 1-byte opcode + payload up to 64 MB), opcode enum, tight DTO codec for metadata / CalculateRequest / CalculateResponse. Worker loads assemblies into a collectible ALC; one indicator per worker lifetime.
- [x] **`IScriptWorkerLauncher` abstraction** + `DefaultProcessLauncher` that spawns the worker unsandboxed via `Process.Start`. Per-platform launchers (C2/C3/C4) plug in behind the same interface.
- [x] **Host supervisor (C5)** — `OutOfProcessScriptHost` owns the process handle, serializes stdin writes, streams stderr to the logger, enforces per-call wall-clock timeouts (5 s Calculate / 10 s LoadAssembly), kills the worker on timeout via `Process.Kill(entireProcessTree: true)`, sends `Shutdown` frame with a 1-second grace window on disposal.
- [x] **Rewire `RoslynScriptingService` (C6)** — `CompileIndicatorAsync` returns an `OutOfProcessIndicator` proxy by default; `ACCESSIBLETRADER_SCRIPT_IN_PROCESS=1` opts into the legacy in-process path for breakpoint debugging. `UnloadScript` disposes the out-of-process host cascading to worker kill. Cached-scripts recompile note in CHANGES.
- [x] **Roundtrip integration test** — `OutOfProcessScriptingTests.Roundtrip_TrivialIndicator_EchoesClosePrices` exercises the full Roslyn-compile → worker-spawn → stdio-roundtrip → proxy-Calculate → clean-UnloadScript path. Suite is 258/258 passing.
- [x] **Build wiring** — both new projects added to `AccessibleTrader.slnx`; `BlazorClient.csproj` / `Tests.csproj` reference the worker with `ReferenceOutputAssembly=false`; new `CopyScriptWorker` MSBuild target copies the worker output next to the host binary at build time.

### Phase 4 — Track C follow-ups (OS-level sandboxing, 2026-04-17 complete)
- [x] **Windows AppContainer launcher (C2)** — full `CreateProcessW` +
  `STARTUPINFOEX` + `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` wiring
  with manually-managed inheritable pipes, backed by
  `AppContainerScriptWorkerProcess : IScriptWorkerProcess`. Profile
  management via `userenv.dll` (`CreateAppContainerProfile` /
  `DeriveAppContainerSidFromAppContainerName`); cached SID reused across
  launches. `SandboxApplied` returns `true` on success, `false` with
  `LastCreateProcessError` populated on `ERROR_ACCESS_DENIED` (dev-box
  ACL gap) so dev builds fall back gracefully to the default launcher.
- [x] **macOS / Mac Catalyst sandbox (C3)** — `MacSandboxExecLauncher`
  ships + `AccessibleTrader.ScriptWorker/sandbox-profiles/script-worker.sb`
  deny-default profile.
- [x] **Android `isolatedProcess` (C4)** — `ScriptWorkerService` bound
  service with `[Service(IsolatedProcess=true)]`; `Messenger`-based
  IPC transfers two `ParcelFileDescriptor` pipe ends. Real launcher in
  `AccessibleTrader.BlazorClient/Platforms/Android/AndroidIsolatedProcessLauncher.cs`
  binds the service + hands host-side `FileStream`s over the pipes to
  `OutOfProcessScriptHost`. MAUI wires the platform launcher into DI on
  Android builds; Core-side routing stub throws if mis-wired.
- [x] **Hostile-script smoke tests** — `HostileScriptTests` (6) compile
  indicators attempting `File.ReadAllText` / `HttpClient.GetStringAsync`
  / `Process.Start` / unsafe / `[DllImport]` / `Assembly.LoadFrom` and
  assert `CompileResult.Success == false`. Covers the in-worker Roslyn
  sandbox layer; OS-sandbox layer still needs on-target integration
  tests (run-on-device / run-on-AppContainer harness) but the
  defense-in-depth first line is covered.
- [x] **Resource quotas beyond wall-clock (C5)** — `OutOfProcessScriptHost`
  polls `WorkingSet64` every 2 s, kills on overage (default 256 MB).
- [x] **Track B1 follow-ups** — Bitstamp + Coinbase per-request checkout;
  Alpaca + Binance per-connection-lifecycle; Schwab / IBKR N/A. Status
  matrix in `CREDENTIAL_CHECKOUT_MIGRATION.md`.
- [x] **Cross-TFM build errors** — NETSDK1150 on iOS/Android/macCatalyst
  fixed via `ProjectReference` + `CopyScriptWorker` TFM guards. Inline
  `HashPluginDlls` task's `SHA256.HashData` swapped for
  `SHA256.Create().ComputeHash` so it compiles under every supported
  MSBuild runtime. `GeneratePluginTrustManifest` guarded on non-empty
  `$(OutDir)` for aggregate multi-TFM builds.
- [x] **Warnings** — every CS warning across the solution is addressed.
  Full Release build is 0 warnings / 0 errors.

### Remaining follow-ups (optional, no security impact)
- [ ] **On-device / on-AppContainer integration tests** — a test
  harness that, on the target platform, compiles an indicator which
  reaches for `File.WriteAllText` via a trick the Roslyn sandbox
  misses and asserts the OS sandbox blocks at runtime. Requires CI on
  each platform; today's xunit suite covers the Roslyn layer only.
- [~] **Hot-path credential cache** — per-provider 60s session cache if
  per-request `CheckoutAsync` latency becomes user-visible on Android
  KeyStore. Measure first. **Measurement layer shipped 2026-04-24:**
  `CheckoutLatencyTracker` (per-provider rolling window of 256 samples,
  P50/P95/P99/Max via NIST-handbook interpolation) wired into
  `MauiApiKeyCheckoutAdapter`. Pending: a session of live data on
  Android device + the JournalModal surface to read out the percentiles.
  If sustained P95 stays under 15 ms the item closes as "no cost, no
  fix needed"; over 15 ms green-lights the session-cache implementation.
- [x] **macCatalyst scripting refusal** — shipped 2026-04-24. Rather than
  silently falling through to the in-process path,
  `RoslynScriptingService.CreateDefaultLauncher` now returns a
  `RefusingScriptWorkerLauncher` on macCatalyst that throws
  `ScriptingNotSupportedOnPlatformException` at launch time (same refusal
  as iOS, which joined explicitly here too). Dedicated macCatalyst worker
  packaging remains an open enablement item for a future session if Mac
  desktop users ever demand script support.

### Post-phase-4 polish (2026-04-17, complete)
- [x] **Security event audit log** — `ISecurityEventLog` +
  `SecurityEventLog` ring-buffer impl. Instrumented at AppContainer
  fallback, memory-quota kill, Calculate timeout, Schwab token-cleanup
  failures. Mirrors to `ILogger<T>` at Warning level.
- [x] **Schwab silent `catch {}` closed** — three of five
  `File.Delete` swallows on the explicit scrub path now record
  `TokenCleanupFailed` events.
- [x] **`StrategyBacktester` UTC filenames** — `DateTime.Now` →
  `DateTime.UtcNow` with `Z` suffix.
- [x] **CI test gate** — `.github/workflows/tests.yml` runs the full
  264-test xunit suite on every PR/push.
- [x] **HttpClient factory migration for trading + LLM providers** —
  13 trading providers (minus IBKR and Binance, documented
  exceptions) and both LLM providers now build `HttpClient` via
  `PluginHostServices.CreateHttpClient` with per-provider outbound-
  host allow-lists. WS endpoints stay on `ReconnectingWebSocket` with
  its own 16 MB frame cap.

### Next priorities (broader codebase audit — 2026-04-17)
- [~] **Phase 5 — financial `double` → `decimal` migration.** Every
  money-path record (`Ohlcv`, `OrderUpdate`, `Balance`, `Position`,
  `OrderBookEntry`) uses `double`, which accumulates binary-float
  rounding across ticks, fills, and P&L aggregation. Schema change
  across every provider, every indicator, the backtester, storage
  serialization. Its own dedicated phase.

  **Tier A.5 decision (2026-04-23):** reframed and deferred. Full
  migration would touch 14 trading providers, every `ITradingProvider`
  record, the full StrategyBacktester arithmetic, every position sizer,
  and backtest serialization — multi-day refactor. No reproducible
  bug motivates it right now: float drift per-op is ~1e-15, the
  display layer is now magnitude-aware (2026-04-23 sub-cent fix), and
  Kelly's clamps absorb sub-penny drift. Re-open when the codebase
  moves toward automated live trading with cumulative fill
  accumulation over many sessions — the only scenario where float
  drift is material in practice.
- [x] **Phase 5 — accessibility modal rework** — shipped 2026-04-27 e17.
  Five-part fix per the user's design conversation: SystemCommand Global
  vs ChartScoped categorization in `CommandDispatcher` (with a
  categorization-coverage sentinel test); `_isChartActive` and JS
  `_chartFocused` re-anchored to actual chart-element focus instead of
  modal open/close; Ctrl+Alt+Shift+C now publishes `RequestChartFocusEvent`
  which `ChartArea` consumes via `accessibleTrader.focusElement`, with a
  plain `"Focus on trading chart area."` announcement; modal close
  auto-returns focus to the chart; `OrderBookModal` rewritten with live
  depth-stream subscription via new
  `IOrderExecutionService.SubscribeOrderBookAsync`, 20 levels per side,
  `tabindex="0"` + `aria-label="Bid/Ask price, size"` on every row, and
  the noisy per-refresh `AnnounceDepthChange` speech deleted. 102 new
  tests (95 categorization pins + 7 OrderBookModal bUnit). 937/937 passing,
  0 warnings, 0 errors. Full writeup in `docs/CHANGES.md` 2026-04-27 e17.

- [x] **Esc-doesn't-close-Help (Phase 5 follow-up, surfaced 2026-04-27 e18).**
  Resolved 2026-04-27 e19 via the unified `CloseTopModalEvent` route — see
  the `[2026-04-27 evening 19]` section at the top of this file. Affects all
  17 modals through one dispatcher case + `ModalBase`/inline subscriptions.

- [ ] **Toolbar button labels showing raw shortcut keys (surfaced
  2026-04-27 e18).** User reports buttons rendering as `'t'`, `'a'`,
  `'objects'` instead of friendly names. Likely a `Toolbar.razor` template
  binding raw shortcut-key strings into the visible text or `aria-label`
  where the button's display name should live. Audit `Toolbar.razor` /
  `IconSprite.razor` button definitions and the corresponding
  `ShortcutManager` bindings.

- [ ] **Phase 5 v2: order book large-order detection.** Adaptive
  rolling-median size threshold (default `K = 10×` median of last 60s) plus
  absolute notional floor (default `$25k`) plus rate-limit (one
  announcement per ~4s, batching the largest qualifier in the window).
  Format: `"Large bid 5.2 BTC at 67230"`. Settings UI under a new
  "Order book" section in `SettingsModal`. Placement-only at first;
  large-cancellation / large-fill announcements deferred to v3 if needed.
  Per the user's spec, this layers on top of v1 once v1 is verified in
  app — does not block other Phase 5 work.
- [x] **CPU quota on script worker** — shipped 2026-04-24.
  `DefaultMaxCpuFraction = 0.9`; polls `TotalProcessorTime` delta vs
  wall-clock every 2 s; sustained overage triggers kill + security
  event + descriptive Calculate-side exception.
- [x] **Per-user worker-count limit** — shipped 2026-04-24.
  `DefaultMaxConcurrentWorkers = 16` with atomic counter gate in
  `StartAsync`/`DisposeAsync`. Configurable via `SetMaxConcurrentWorkers`.
- [~] **Provider unit-test coverage** — rounds 1-5b shipped 2026-04-24.
  `ProviderTimeframeContractTests` (31 tests) pins every provider's
  NativelySupportedTimeframes against TimeframeUtility;
  `ProviderSymbolNormalisationTests` covers wire-format transforms;
  `Fakes/FakeHttpMessageHandler` + `Fakes/FakeApiKeyCheckout` shipped as
  fixtures; `ProviderFetchOhlcvTests` (54 tests across Bitstamp / Polygon /
  Tradier / Coinbase / AlternativeMe / Mempool / DefiLlama / OkxDerivatives /
  Glassnode / Etherscan / Fred / BinanceDerivatives / BGeometrics /
  CoinMetrics / **Kraken / Oanda / Alpaca** — round 5 added auth-gated
  paths via `FakeApiKeyCheckout`) drives FetchOhlcvAsync end-to-end via
  reflection-swapped HttpClient. `ProviderLiveStreamTests` (26 tests
  across Bitstamp / Coinbase / Polygon / **Kraken / Finnhub** — round 5b
  added Kraken's split public/auth handlers) reflects into private
  `HandleWebSocketMessage(string)` and asserts on public IObservable
  streams.
  **Remaining:** Binance / MEXC are SDK-managed (callbacks inside the
  SDK — neither HttpClient nor a reflectable parse method is reachable;
  would need an adapter layer); Schwab streamer / IBKR gateway have
  non-standard handler shapes; CoinGecko / BinanceVision / FmpAnalytics
  still need ~3 fetch tests each.
- [x] **Silent `catch {}` sweep (Tier A.1 — 2026-04-23)** — shipped. Upgraded 9 user-facing silent catches to diagnostic `Debug.WriteLine` / `_logger.LogDebug`: `AlertEvaluator` (alert-rule failure), `AIAnalystService` (screenshot encode failure), and 7 provider feed parsers (Alpaca ×2, Finnhub, InteractiveBrokers, OANDA ×2, Polygon). Teardown/Dispose swallows and `OperationCanceledException` swallows retained as legitimate.
  codebase-wide. Most are correct (malformed WS frame, best-effort
  cleanup); the rest should at minimum log. Prioritized by call-path
  impact.

### Phase-4 operating assumptions (confirmed 2026-04-17)
- **Timeline:** open-ended; complete as we go.
- **CI platform:** GitHub Actions.
- **Credential checkout cadence:** default per-request; opt-in 60s session cache for tick-rate hot paths.
- **iOS stance:** full refusal (no consent prompt).
- **Binary size:** no ceiling (worker exe is fine).
- **Cached-script compat:** OK to break on the out-of-process release; ship a recompile note.

### Cross-cutting (2026-04-17)
- [x] **Ichimoku targeted metadata tests** — replaced the stale `GetMetadata_Returns5Components` count assertion with `Components_ContainClassicalFiveLines`, `Components_ExposeHiddenKumoPolarityHelper`, `Components_ExposeVisibleTkCrossMarkers`, and a sentinel `Components_CountMatchesDeclaredContract`. Tests now pass 256/256 (up from 252/253) and encode the actual component contract so regressions name which piece broke instead of just "count changed".

---

## PRIOR (2026-04-11 Evening) — OB/OS fix, strategy cleanup, BinanceVision promotion

### Completed this session
- [x] **OB/OS zone band architecture** — `ZoneBandConfig` extended with `FixedTop`/`FixedBottom`/`IsFixedMode`. `RenderZoneBand` paints full-viewport rectangle in fixed mode. Cipher B refactored to `DefaultZoneBands` (2 bands: OB +53..+100, OS -53..-100). Deleted `CompZoneCeiling`/`CompZoneFloor` phantom components. **The OB/OS shading bug is fixed** — it was trying to do visual work through the data-component pipeline.
- [x] **Strategy cleanup** — 14 dead builders purged (`LegacyCipherLong`, `V3`, `V4Claude`, `V5`–`V12`, `V13ShortBearDivBelowSma200`). File shrank 3339 → ~1100 lines. v13s removal confirmed by fresh walk-forward (BTC 1d -0.132R, BTC 4h -0.439R Sharpe -8.92).
- [x] **v18 Refined Short** — Hidden Bear Continuation + below SMA200 + crowded-long funding. First cross-asset short survivor. DOGE 4h 14T+15T 64–67% WR. BTC 4h H1+H2 both positive. XRP 1d H2 100%.
- [x] **v21 MVRV Capitulation Trilogy** — v16 trilogy + `COINMETRICS.MVRVRegime < 2`. Positive on ETH 4h, XRP 4h, BTC 1d H1.
- [x] **v19 + v20 attempted and deleted** — both failed BTC 4h H2.
- [x] **Walk-forward matrix** — 5 strategies × 5 assets × 2 TF = 50 tests. Results in `AccessibleTrader.StrategyLab/walk_forward_results.json` + session memory.
- [x] **BinanceVision plugin promoted** — `Plugins/Analytics/AccessibleTrader.Plugins.BinanceVision/BinanceVisionProvider.cs` fetches `data.binance.vision` monthly ZIPs (~6 years history, free, no API key). Exposes `{PAIR}USDT_FUNDING` / `{PAIR}USDT_OI` for 8 majors. Funding ×100 normalized at boundary. Registered in `.slnx` + `BlazorClient.csproj`.
- [x] **Core indicators repointed** — `FundingRateProvider`, `OpenInterestProvider`, `CrowdingIndexProvider` all switched from OkxDerivatives (11 days) → BinanceVision (6 years). Live app now has deep free derivatives data.
- [x] **Deep OHLCV snapshots** — BTC/ETH/XRP pulled to 20000 bars (2017 → 2026), SOL/DOGE pulled to Bitstamp history depth (SOL 2022-08, DOGE 2022-12). BTC 4h + ETH/SOL/XRP/DOGE 4h all refreshed.
- [x] **Extended BinanceVision DOGE/ADA/LTC support** — `BinanceVisionFundingCommand.SymbolStartMonths`, `BinanceVisionOiCommand.SymbolStartDates`, and both lab providers' asset-resolution whitelists.

### Open gaps — next session
- [x] **Funding snapshot scale rewrite (2026-04-23)** — 8 files in `strategy-lab-data/` rewritten ×100 via idempotent PowerShell (`ScaleAppliedPercent: true` marker guards re-runs). Threshold-based strategies (v18 `Funding > 0.05`) now fire identically in lab vs live.
- [x] **Asset-aware Core FundingRate / OpenInterest / CrowdingIndex** — shipped 2026-04-23. `IndicatorOrchestrator` stamps `parameters["__symbol"]` from `state.Identity.Symbol` on both full-recalc and tick-update paths. Each provider's new private `BuildRequest`/`BuildRequests` helper derives the cross-series symbol per-call (normalises `/`, `-`, appends `USDT` for bare bases, falls back to BTCUSDT when the hint is absent). Tests: `AssetAwareCrossSeriesTests.cs` (15).
- [ ] **Delete redundant BNVISION_FUNDING / BNVISION_OI lab providers** — now duplicating what Core FUNDING_RATE / OPEN_INTEREST provide. Still referenced by v18/v21 strategy leaves. Once v18/v21 are migrated to `FUNDING_RATE.Funding Rate` leaf, the lab providers can be deleted along with their command files.

### Uncommitted work
- [x] **Commit this session's work** — obsolete; all listed groups long since
  committed in subsequent sessions. Pruned 2026-04-27.

### Strategy work — future
- [ ] **Cross-asset matrix rerun with v18 asset-aware**: once Core providers accept `__symbol`, re-run v18 on ETH/SOL/XRP/DOGE in LIVE mode to verify parity with StrategyLab.
- [ ] **v13/v14/v15 cross-asset walk-forward** — never tested on non-BTC. May reveal additional survivors or confirm BTC-specialist pattern.
- [x] **Divergence line rendering** — already shipped. `StandardRenderers.RenderDot` reads `{Comp}_anchorIdx` + `{Comp}_anchorY` companion arrays and draws a slanted line from the first pivot to the second-pivot diamond when both anchors are non-NaN. `CipherBProvider` populates these arrays for `Bullish Divergence`, `Bearish Divergence`, `Hidden Continuation` (bull + bear). Pruned 2026-04-27.
- [x] **Cross-pane Anchor cloud** — already shipped. `ChartRenderer.RenderAnchorRegimeTint` paints `_crossPaneAnchorPolarity` from any visible series exposing an `Anchor Polarity` component into the Main pane background (faint teal/red, α=22). Pruned 2026-04-27.
- [x] **Schwab UI sign-in button (2026-04-23)** — per-row "Sign in" button added to `ApiKeysModal` for Schwab profiles. Activates the profile, reaches the provider via `IDataService`, invokes `BeginAuthorizationAsync` through reflection (keeps UI off the plugin hard-dep), publishes start/success/failure feedback for screen-reader users.

---

## Prior Session — 2026-04-11 afternoon (Cipher B fidelity + trilogy strategies)

### Completed
- [x] Cipher B full MCB-fidelity rewrite (body/range MF, WT Histogram, K-of-N gold, depth gate, alt divergence detector, anchor suppression, TF-aware gates)
- [x] Visual polish (histogram saturation, anchor cloud opacity, MF sqrt expansion, dot hierarchy)
- [x] v13 / v14 / v15 / v16 / v16s / v17 long/short trilogy strategy seeds
- [x] v12 retired from seeds (Anchor-sign thesis invalidated by the rewrite)
- [x] StrategyLab DI fix (`LabHost.Build()` registers `ILoggerFactory`)
- [x] `DiagnosticCommand --side long|short` flag
- [x] Schwab provider plugin (OAuth2, EQUITY market/limit/stop orders, 120 rpm limiter)

---

## Previously active — Build-Out (2026-04-10)

### Completed Session 1
- [x] BGeometrics plugin — 28 BTC on-chain symbols (MVRV, SOPR, NVT, NUPL, CDD, Hodl Waves, S2F, etc.)
- [x] CoinMetrics live plugin — 117 symbols across 9 assets (MVRV, active addresses, hash rate, exchange flows)
- [x] DefiLlama plugin — DeFi TVL (10 chains, 8 protocols), stablecoin supply (USDT/USDC/DAI/total)
- [x] Mempool plugin — BTC hashrate, difficulty, block fees/rewards/sizes/fee rates
- [x] Etherscan plugin — ETH gas oracle, supply, price, node count
- [x] FMP plugin — Stock/Crypto/Forex/Commodity/Index OHLCV with intraday
- [x] FMP Analytics plugin — fundamentals, ratios, earnings, sector performance, economic calendar
- [x] Full code quality overhaul (SafeFireAndForget, structured logging, disposal, ConfigureAwait, sandbox hardening)

### Completed Session 2
- [x] IAnalyticsDataResolver — 30 metrics, priority-ordered provider resolution, API key awareness
- [x] ApiKeysModal — expanded to 19 providers
- [x] LiveStreamManager auto-reconnect — 5 attempts, tear-down/reconnect/re-subscribe
- [x] InsideCloud operator fix — reads both CloudFillConfig bounds, proper inside evaluation
- [x] Plugin directory restructure — Providers/Analytics/Indicators subdirectories
- [x] Dynamic indicator plugin discovery — scan Plugins/Indicators/ at startup
- [x] PROVIDER_AUTHORING.md — complete data provider authoring guide
- [x] PropertiesModal per-component picker — dropdown filter for 3+ component indicators
- [x] Parameter validation — MinValue/MaxValue/Step on IndicatorParameterMetadata, clamp on edit
- [x] TrailByAtr stop adjustment — Wilder ATR trailing stop in backtester after TP1
- [x] Cloud component architecture — navigable, sonified, speech-announcing, auto-narrating clouds
- [x] MACloudProvider — 6 MA types (EMA/SMA/WMA/HMA/DEMA/TEMA), replaces EmaFillProvider
- [x] MovingAverageHelper — shared utility replacing 3 duplicate Ema() implementations

### Research / Next Session
- [ ] Adaptive WT thresholds for Cipher B — dynamic OB/OS levels based on oscillator's own distribution (percentile-based)
- [ ] Pulse indicator simplification — consider decomposing v1/v2/v3 signal tiers into strategy conditions
- [ ] Phase 12 Session 3 — v9 backtest + thesis validation
- [x] Commit all uncommitted work — obsolete; long since committed across many subsequent sessions. Pruned 2026-04-27.

### Data Landscape Reference (updated)
Free: BGeometrics (BTC 154+ metrics), CoinMetrics Community (9 assets MVRV), DefiLlama (TVL, stablecoins), Mempool.space (BTC mining), Etherscan (ETH gas/supply), OKX public (307 perps funding/OI), Binance public (derivatives), Alternative.me (FNG), CoinGecko (dominance), FRED (macro), FMP free tier (250 req/day).
Paid only: CoinGlass ($29+/mo, no free tier), CryptoQuant ($109+/mo for API), Glassnode (API requires paid plan + add-on), FMP paid ($14-79/mo for higher limits).
Gaps: ETH missing SOPR/NVT/exchange flows (paid only). SOL/AVAX no on-chain metrics free. KAS no data anywhere. TAO derivatives only (OKX).

---

## PHASE 11 — Strategy Composer & Risk-Managed Setups (multi-session)

A user-buildable signal composer that combines indicator components from any registered indicator, evaluates them as an AND/OR/NOT condition tree, gates the result on a reward/risk plan with TP ladders, and announces every step (initial setup, re-confirmation, dropouts) through bells and speech. The output is reviewable in the Journal modal (Ctrl+Alt+Shift+J).

### Session A — Foundation (2026-04-07) — DONE

- [x] **Bell earcons:** `setup_long_bell` (sine + perfect-fifth chord) and `setup_short_bell` (triangle + sub-octave) registered in `SoundPatchRegistry`. `IEarconService.PlaySetupBell(side, reconfirmation)` renders them as one-shot `ISonificationManager.PlayNote` chords.
- [x] **Journal modal (Ctrl+Alt+Shift+J):** `IJournalService` ring buffer (2000 entries) auto-subscribing to `StrategySignalEvent` / `AlertFiredEvent` / `AppErrorEvent`. `BlazorSpeechManager.Speak()` mirrors every TTS phrase. `JournalModal.razor` console-style filterable copyable text view. (Initially Ctrl+J — corrected to Ctrl+Alt+Shift+J 2026-04-07.)
- [x] **Backtester warmup gate:** `BacktestConfig.WarmupBars` (default 200), `BacktestResult.WarmupBars` / `EvaluatedBars`, signals dropped during warmup, modal input + display.
- [x] **Sdk types:** `SignalDescriptor` + `SignalKind`, `ConditionTree` (`ConditionNode`/`ConditionLeaf`/`ConditionGroup`/`LogicOperator`/`LeafOperator`/`ConditionEvaluation`), `RiskPlan` (4 stop sources + 4 Phase-4 stubs / 3 target sources + 5 stubs / TP ladder / sizing modes / `EntryTrigger` / `MinRewardRiskRatio` gate / `ResolvedRiskPlan`), `StrategySpec`. `ConditionLeaf.Timeframe` foundation field for MTF.
- [x] **Core services:** `ISignalCatalog` walks `IIndicatorProvider.GetIndicators()`, `IConditionEvaluator` (no AND short-circuit so per-leaf result map is complete), `IRiskPlanResolver` (Wilder ATR, percent, swing low, fixed; RR multiple, percent, fixed; FixedRiskPercent / FixedRiskCash / FixedQuantity sizing).
- [x] **`ConfigurableStrategy : BaseStrategy`** with the inactive→active→reconfirm→dropout state machine, dropout label resolution via the catalog.
- [x] **`IConfigurableStrategyFactory`** + **`JsonStrategyLibrary`** (System.Text.Json `JsonPolymorphic` discriminator `$kind` for round-trip).
- [x] **Setup events:** `SetupConfirmedEvent` / `SetupReconfirmedEvent` / `SetupDroppedEvent` in Events.cs.
- [x] **`SetupSonifier`** subscribes to all 3, plays bell + speech. Eagerly resolved via MainLayout `@inject`.
- [x] **DI registration** in `ServiceCollectionExtensions.AddBusinessServices`.

### Session B — Multi-timeframe data + adaptive backtester history + entry-armed state machine (2026-04-07) — DONE

- [x] **`IMultiTimeframeDataService` + `MultiTimeframeDataService`** wrapping `IDataOrchestrator.FetchOhlcvAsync` (already cache-backed via SQLite + Polly). In-memory `(provider|symbol|timeframe)` cache with bar-size-proportional TTL. `GetBarsAsync` populates, `GetCachedBars` is the sync hot-path read for the evaluator.
- [x] **HTF leaf routing in `ConditionEvaluator` (price-only subset):** `ConditionLeaf.Timeframe` triggers HTF cached lookup; price comparisons (`GreaterThan` / `LessThan` / `Between` / `CrossesAbove` / `CrossesBelow`) evaluate directly against HTF bars. Indicator-on-HTF computation falls through to active-TF with a one-time warning — needs sync indicator runner or pre-warm cache, deferred to Session C.
- [x] **`IBacktestWarmupAnalyzer` + `BacktestWarmupAnalyzer`** walks `StrategySpec` condition tree, collects unique indicator codes, queries each provider's `GetStabilityWindow`, returns `max × 1.2` (or floor). `ReferencedIndicators` sibling helper.
- [x] **R-multiple metrics on `BacktestResult`:** `AverageR`, `Expectancy`, `ProfitFactor`, `AverageBarsInTrade`, `LongestLosingStreak`. `BacktestTrade` extended with `StopPrice` and `BarsInTrade`. `StrategyBacktester` tracks `openStop` + `openBarIndex` and computes per-trade R = `reward / |entry - stop|`. Speech summary includes Average R when known.
- [x] **Entry-armed state machine in `ConfigurableStrategy`:** new `SetupState` enum (Inactive / Armed / Active). Inactive→Armed when EntryTrigger != Immediate; Armed→Active on trigger fire. `OnPullbackToLevel` / `OnBreakoutOf` / `OnNextNCandleClose` trigger evaluation. No setup expiration. Heartbeat `SetupReconfirmedEvent` while armed.
- [x] **`SetupArmedEvent` + `SetupEntryReachedEvent`** added to `Models/Events.cs`.
- [x] **`IEarconService.PlaySetupArmed` + `PlaySetupEntryReached`** — distinct earcons for the armed-waiting state and the entry-reached state. SetupSonifier subscribes and routes.
- [x] **DI registration** — `IMultiTimeframeDataService` and `IBacktestWarmupAnalyzer` registered as singletons in `AddBusinessServices`.
- [x] **Journal shortcut corrected** to `Ctrl+Alt+Shift+J` from initial `Ctrl+J`.

**Still pending in this scope (deferred to Session C+):**
- [x] **HTF indicator computation (Tier A.2 — 2026-04-23)** — infrastructure (PrewarmIndicatorAsync + GetCachedIndicator + ConfigurableStrategy.Initialize + pre-warm gate) was already wired. Closed the last gap: `MultiTimeframeDataService.PrewarmIndicatorAsync` now calls a new `BuildDefaultParameters` helper when the caller passes an empty parameter dict, looking up `IndicatorMetadata.Parameters` defaults from the indicator provider. Was previously passing an empty dict which made some providers emit all-NaN arrays. Regression pinned by `ConditionEvaluatorHtfTests.cs`.
- [x] **Adaptive warmup auto-apply in StrategyModal (Tier B.3 verified 2026-04-23)** — already shipped. `StrategyModal.razor`'s `AutoWarmup()` wires the "Auto" button; `BuildSetupTab.razor:992` auto-applies `WarmupAnalyzer.RecommendedWarmup(spec)` in the preview flow.
- [x] **Pre-warm of HTF data on strategy add** — shipped in Session C+
  (the infrastructure was already in place; TODO entry was stale).
  `ConfigurableStrategyFactory` optionally injects `IMultiTimeframeDataService`;
  `ConfigurableStrategy.Initialize` collects the unique `(Timeframe, IndicatorCode)`
  pairs from the condition tree and fire-and-forgets `PrewarmIndicatorAsync`
  per pair plus `GetBarsAsync` per unique HTF timeframe. The
  `IsPrewarmComplete` gate blocks `OnBar` evaluation until every prewarm
  task finishes — otherwise NaN reads on unwarmed HTF leaves silently flip
  condition results. Pinning tests added 2026-04-24
  (`ConfigurableStrategyPrewarmTests.cs`, 4 tests): per-pair collapse,
  no-HTF-leaf fast-path, null-MTF tolerance, gate-flips-after-completion.

### Session C — Support / resistance + volume profile as condition + risk sources (2026-04-07) — PARTIAL

- [x] **`ILevelProvider`** abstraction with `PriceLevel` record (NB: named PriceLevel, not LevelDescriptor — name collision with Sdk.Models.LevelDescriptor for indicator default reference levels). `LevelKind` enum: Support / Resistance / Pivot / Poc / Vah / Val / Hvn / Lvn / Vwap / Kijun / KumoTop / KumoBottom.
- [x] **`ILevelService` aggregator** with `GetAllLevels` / `NearestBelow(kindFilter?)` / `NearestAbove(kindFilter?)`.
- [x] **`DrawnHorizontalLevelProvider`** — reads workspace drawings (Horizontal / TrendLine endpoints / Rectangle edges / RiskReward anchors), classifies as Support/Resistance based on current price.
- [x] **`SwingPivotLevelProvider`** — algorithmic swing-high/low detection from raw OHLCV (LookbackBars=5, MaxPivots=12 newest-first). Fallback when nothing else is loaded.
- [x] **`IchimokuLevelProvider`** — exposes Kijun-sen + KumoTop + KumoBottom from the active Ichimoku series.
- [x] **`CipherSrLevelProvider`** — walks the Cipher SR Resistance/Support component arrays for the last 200 bars; recency-weighted strength.
- [x] **Phase-4 stop sources implemented:** `BelowSupport`, `BelowKijun`, `BelowKumo` — all wired through ILevelService. `BelowLvn` still returns null pending VPVR.
- [x] **Phase-4 target source: `NextResistance`** wired through ILevelService.
- [x] **New leaf operators:** `PriceRejectsLevel` (touch + close-away within N bars + tolerance), `PriceBreaksLevel` (open/close straddle a level), `BarClosesAbovePoc` / `BarClosesBelowPoc` (defined, dormant until VPVR provider ships).
- [x] **VPVR / TPO level provider** — `VolumeProfileLevelProvider` walks `series.ProfileBins` (which IS populated eagerly by IndicatorOrchestrator, contrary to earlier belief — not render-time only). Emits POC / VAH / VAL / HVN / LVN with same thresholds as ProfileBinClassifier (HVN: `IsValueArea && volume > mean × 1.3`; LVN: `IsSinglePrint || volume < mean × 0.4`).
- [x] **Phase-4 stop source `BelowLvn`** — wired through `NearestBelow(kindFilter: Lvn)`.
- [x] **Phase-4 target sources `NextHvn` / `Poc` / `Vah`** — wired through nearest-by-kind lookups.
- [x] **`FibExtension` target source** — pure history-derived: lowest low + highest high in last 50 bars, validates impulse direction, projects entry + range × FibLevel.
- [x] **Leaf operators `PriceInsideValueArea` / `PriceOutsideValueArea` / `WickIntoLvn`** — implemented in ConditionEvaluator.
- [x] **Future-leak fix on indicator-derived providers:** `IchimokuLevelProvider` and `CipherSrLevelProvider` now clip component-data scans to `min(history.Count, data.Length)`. Strategy at backtest bar 100 no longer sees Ichimoku/Cipher SR values from bars in the future.
- [x] **Backtester profile-state replay** — `IBacktestProfileCache` + `BacktestProfileCache` ambient cache, `VolumeProfileLevelProvider` reads from cache when active, `StrategyBacktester` recomputes bins per bar via `IProfileService.CalculateVolumeProfile/MarketProfile(historyBuffer)` when `BacktestConfig.ReplayProfiles=true` (default). Cache cleared in try/finally so live evaluations after the run fall through to live `series.ProfileBins`.
- [x] **HTF indicator computation** — `IMultiTimeframeDataService.PrewarmIndicatorAsync` + `GetCachedIndicator`, uses `IIndicatorEngine.CalculateAsync` (one-shot), `ConfigurableStrategy.Initialize` walks tree and fire-and-forgets pre-warm for every unique (Timeframe, IndicatorCode) pair, `ConditionEvaluator` checks the cache first then falls through to price-only HTF path.

### Path A Correctness Pass (2026-04-07) — DONE

- [x] **Fixed `ConditionEvaluator` main-path future-leak** — was reading `data[^1]` from the full series array, surfacing final-bar values at every backtest bar. Now clips reads to `Math.Min(history.Count, data.Length) - 1`. `FiredWithin` and `DirectionChanged` updated with `historyCount` parameter.
- [x] **Fixed `StrategyBacktester` passing `WorkspaceState.Initial` (dummy)** — `IStrategyBacktester.RunAsync` now takes optional `WorkspaceState? state = null`. `StrategyModal.RunBacktestAsync` passes `Store.State`. Without this fix, ConfigurableStrategy backtests were silently broken because they read `state.ActiveSeries` and the dummy state had none.
- [x] **`BacktestConfig.ReplayProfiles`** flag (default true) — gates the per-bar profile recomputation in StrategyBacktester. Set to false for fast iteration on strategies that don't gate on profile levels.
- [x] **`ConfigurableStrategy` ctor + factory** carry `IMultiTimeframeDataService` through. `Initialize` triggers pre-warm.

### Session D — Builder UI in StrategyModal (2026-04-07) — DONE

- [x] **Modal input trap fix** — `CommandDispatcher` subscribes to `ModalStateChangedEvent` with a counter, suppresses chart commands while any modal is open. Allowlist preserves F1 (Help), F2 (toggle speech), F3 (toggle sonification) for accessibility.
- [x] **`BuildSetupTab.razor`** new component, hosted by a "Build Setup" tab in `StrategyModal.razor` (between Add Strategy and Active). Lazy-mounted via `@if (_activeTab == "build")`.
- [x] **ARIA tree** (`role="tree"` + `treeitem` + `aria-level` + `aria-expanded` + `aria-selected`) replacing the rejected nested-list pattern. Each tree item has inline `+ leaf` / `+ group` / `×` buttons.
- [x] **Cascading combo-box leaf editor** below the tree: Indicator → Component → Operator → Value → optional Upper Bound → optional Within-N → Timeframe → Score. Operator dropdown gated by the descriptor's `SignalKind`.
- [x] **Risk plan section** — full UI for all 8 stop sources, TP ladder editor (default 3 rungs), R:R minimum, sizing mode + parameters, notional equity, entry trigger.
- [x] **Save / Load / Add to Engine** via `IStrategyLibrary` + `IConfigurableStrategyFactory` + `IStrategyEngine.AddStrategy`.
- [x] **Preview button** — runs warmup-aware backtester with `ReplayProfiles=false` for fast iteration; results displayed inline (trades, win rate, P&L, avg R, profit factor, max drawdown, warmup/evaluated). Manual trigger rather than auto-debounce-on-edit (cost too high on long charts; the `_previewTimer` field is preserved as a hook for future polish).
- [x] **Read aloud button** — `NarrateSpec()` walks the editable tree and emits a plain-English sentence; speaks via `ISpeechManager.Speak(interrupt: true)`. Mirrors automatically into the journal.
- [x] **Auto-apply `IBacktestWarmupAnalyzer`** in Backtest tab — "Auto" button resolves the spec by name match and sets warmup to the analyzer's recommendation.
- [x] **`CrossesAboveLine` / `CrossesBelowLine` second descriptor refs** — `ConditionLeaf.SecondSignalDescriptorId` added, evaluator implements MA-cross semantics, builder UI conditionally shows a second-component combo box.
- [x] **Export / Import to `.atstrat` files** — `{AppData}/exports/{SafeName}.atstrat`. Import-latest reads the most-recently-modified file.

### Session E — Lifecycle integration (2026-04-07) — DONE

- [x] **Per-restart strategy persistence** via `StrategyAutoLoader` + `StrategySpec.IsAutoActivate` flag. The builder UI's "Add to Engine" sets the flag and persists; on next launch `MainLayout` calls `_autoLoader.LoadAll()` which walks the library, filters by `IsAutoActivate=true`, and re-instantiates each via the factory. Idempotent. Saved-but-not-activated specs remain in the library as templates. (Architectural simplification: per-tab strategy IDs in `TabConfiguration` were considered but rejected for marginal benefit; the field stays as a forward-compat hook.)
- [x] **Distinct entry-armed earcon** — already shipped in Session B as `IEarconService.PlaySetupArmed` (long: 660+990 sine; short: 330+220 triangle), distinct from `PlaySetupBell` (full setup) and `PlaySetupEntryReached` (in-trade). All three subscribed by `SetupSonifier`.
- [x] **AI Analyst "Review my setups today"** — `IAIAnalystService.AskAsync(prompt)` method, new modal button, builds structured prompt from today's journal entries + matching library specs, calls LLM with a setup-review system prompt, displays + speaks the response, mirrors back into the journal as an Info entry for later review.

### Phase 11 Audit Fixes (2026-04-07) — DONE

User reported 0 trades after adding Cipher B + building a strategy. Audit revealed 7 issues; all fixed in one focused session.

- [x] **Backtester honors TP/SL exits** — `StrategySignal` extended with `TpLadder` + `TpClosePortions`. `StrategyBacktester.Run` rewritten with per-bar exit check (stop priority + TP rung loop + breakeven move after TP1). Every backtest before this returned 0% profit because exits were never simulated. Single most important Phase 11 correctness fix.
- [x] **Cipher B catalog/chart mismatch** — `ConditionEvaluator` series lookup is now case-insensitive. `BuildSetupTab` leaf editor warns when the selected indicator isn't loaded on the active chart (yellow alert + `(not on chart)` annotations in the dropdown).
- [x] **Legacy SMA/RSI/Bollinger templates deleted** — three files removed; `BuiltInStrategyRegistry` reduced to empty stub.
- [x] **Library tab** — replaces Add Strategy. Lists `IStrategyLibrary.All` with Start/Stop/Delete actions. Active status column. New methods: StartSpec / StopSpec / DeleteSpec / RemoveExistingInstancesOfSpec helper.
- [x] **Backtest tab uses library specs** — `_btSelectedSpecId` dropdown + `Factory.Create` instead of legacy template selection. AutoWarmup uses the actual selected spec.
- [x] **Active tab Remove clears `IsAutoActivate`** — closes the bug where removed strategies came back on next launch.
- [x] **Warmup label** changed from misleading "Warmup / Evaluated: 579 / 2200" to explicit "Bars used: 2779 total (579 warmup + 2200 evaluated)".
- [x] **Duplicate-add guard** in `BuildSetupTab.AddToEngine` — removes any existing instance with same spec id before adding the new one.

**Still pending (polish, not blocking):**
- [~] **Live mode TP ladder execution** — broker-side bracket order plumbing per provider remains deferred (multi-day per broker: Binance OCO, Coinbase brackets, Schwab OCO, Alpaca brackets, Kraken conditional-close, plus emulation for brokers without native support). Tier B.5 (2026-04-23) shipped a safety warning: `SetupSonifier.OnArmed` now appends "Ladder has N rungs — only the first target fires live until multi-rung bracket support ships" when `TpPrices.Count > 1`. Closes the silent-failure path; multi-rung implementation stays on this list.
- [x] **Active tab metrics for Suggestion-mode strategies** (shipped
  2026-04-24) — `BaseStrategy` now wraps `OnBar` with theoretical-fill
  tracking: each signal with a Stop AND TakeProfit is recorded as a
  theoretical entry at bar close; subsequent bars walk Stop/TP against
  High/Low with stop-priority on same-bar ties (matching
  `StrategyBacktester`). `GetMetrics()` blends real-fill (Auto) +
  theoretical-fill (Suggestion) counters. Subclass contract changed:
  `ComputeSignal` is the new abstract hook (renamed from `OnBar`) —
  only one subclass exists (`ConfigurableStrategy`) and was updated.
  `SuggestionMetricsTests.cs` pins the contract (5 tests).
- [x] **TreeView expand/collapse + arrow-key navigation** — shipped
  2026-04-24. New `wwwroot/js/treeKeyboard.js` auto-wires ArrowUp/Down,
  ArrowRight/Left, Home/End, Enter/Space to every `role="tree"` element.
  Handles both the aria-expanded pattern (ConditionTreeEditor) and the
  `<details><summary>` pattern (ObjectTreeModal). All tree levels emit
  meaningful aria-labels that screen readers announce as a single phrase.
- [ ] **Custom Script tab Roslyn strategy persistence** — Roslyn-compiled strategies still aren't saved as `StrategySpec`s.

---

## PHASE 12 — Cross-Series Indicators & Non-Price Edge (2026-04-08, in progress)

The strategy thesis (see `memory/project_strategy_thesis_2026_04_08.md`) established empirically that 8 versions of pure-Cipher confluence (v2-v8) all walk-forward to break-even because price-derived indicators are auto-correlated. Real edge requires non-price data: funding, open interest, sentiment, on-chain. This phase builds the indicator-side plumbing to bridge those data sources into the strategy system.

### Session 1 — Cross-series foundation + 3 indicators (2026-04-08) — DONE

- [x] **OkxDerivatives plugin** — companion to BinanceDerivatives. Reason: Binance Futures REST is geo-blocked from US/UK/parts of EU (verified empirically). Bybit also CloudFront-blocked. OKX public REST remains reachable. Same `_FUNDING`/`_OI` suffix scheme so future indicators don't care which provider produced the data. Endpoints: `/api/v5/public/funding-rate-history` + `/api/v5/rubik/stat/contracts/open-interest-volume`. Wired in `BlazorClient.csproj` ProjectReferences and `MarketOrchestrator.cs:254`.
- [x] **Cross-series indicator architecture** — first one in the codebase. Pattern: per-provider static cache + background fetch via `Task.Run` fire-and-forget + `SemaphoreSlim` debounce + forward-fill in synchronous Calculate. `GetComponentSpeech` override returns "no data for this bar" on NaN to avoid the literal-template speech bug. Documented in detail in `FundingRateProvider` class comment.
- [x] **`FundingRateProvider`** (`FUNDING_RATE`, sub-pane `Pane_FUNDING`) — line + Extreme Long (≥0.05%/8h) + Extreme Short (≤−0.05%/8h) + Sign Flip dots. Reference levels at ±0.05/±0.01/0. Pagination walk-back: up to 10 pages (~333 days, well past OKX's actual depth) with no-progress guard, partial-page early-stop, dedupe-by-timestamp.
- [x] **`OpenInterestProvider`** (`OPEN_INTEREST`, sub-pane `Pane_OPEN_INTEREST`) — OI Value line + OI Delta histogram (polarity colored) + OI Spike dot (>2σ rolling-30-bar stdev) + OI Divergence dot (5-bar price/OI direction disagree, both moves material). The Divergence component is the most actionable signal — captures rallies-without-positioning (likely fades) and selloffs-without-positioning (capitulation bottoms). Single-page fetch (OKX rubik OI is hard-capped at ~180 bars on 1D, less on finer periods).
- [x] **`FearGreedProvider`** (`FEAR_GREED`, sub-pane `Pane_FEAR_GREED`) — Sentiment line + Extreme Fear (≤20) + Extreme Greed (≥80) + Sentiment Flip dots. Reference levels at 20/40/50/60/80. Single-call fetch (alternative.me serves full history back to 2018 in one response). `GetComponentSpeech` returns categorical labels alongside the raw number.
- [x] **DI registration** in `ServiceCollectionExtensions.cs:152-154`.
- [x] **Memory** — `memory/project_cross_series_indicators_2026_04_08.md` documents the architecture, the gotchas, and the next steps.

### Known limitations / gotchas

- **AddIndicatorModal string-parameter limitation:** `AddIndicatorModal.razor:55-57` hardcodes `<input type="number">` and force-converts every parameter via `IConvertible.ToDouble`. Selecting an indicator with `typeof(string)` parameters throws `InvalidCastException` and breaks the modal catastrophically (only one indicator visible, category dropdown frozen, close button unresponsive). Workaround: all three cross-series indicators expose only numeric parameters, source/symbol hardcoded as constants in Calculate. Multi-asset support (BTC/ETH/SOL) currently means separate indicator codes or fixing the modal first. **See Session 2 below.**
- **OKX history depth:** funding ~3 months, OI ~6 months on 1D (less on finer periods). Deep history requires Coinglass / paid sources.
- **Empty marker navigation:** Ctrl+L/R on a marker component (Extreme Long, Extreme Greed, etc.) only stops at bars where the marker fired. If no markers fired in the visible window, navigation says nothing — that's correct sparse-marker behavior, not a bug.

### Session 2 — Refactor + CrowdingIndex + modal string params + v9 (2026-04-08) — DONE

- [x] **Shared `ICrossSeriesCache` service** — `Core/Services/Indicators/CrossSeriesCache.cs`. `CrossSeriesRequest` record + `ICrossSeriesCache.GetOrFetch` + walk-back pagination + `CrossSeriesForwardFill.Fill` helper. Single singleton in DI. Replaced the per-provider static caches in FundingRate / OpenInterest / FearGreed — each provider lost ~150 lines of fetch boilerplate. Done as the first task per "do it right the first time, not a static fix."
- [x] **`CrowdingIndexProvider`** — first composite cross-series indicator. `crowding = funding_zscore + sign(price_delta) × oi_delta_zscore` over a 30-bar rolling window. The price_dir multiplier flips the OI z-score sign so positive composite always means "longs crowded" and negative always means "shorts crowded" regardless of price direction. Components: Crowding Score line + Long Crowded dot (≥+2σ) + Short Crowded dot (≤−2σ). **First codebase signal that pure-price indicators cannot replicate at any lookback** — combines two exchange-internal datasets that aren't computable from OHLCV.
- [x] **AddIndicatorModal string parameter support** — full plumbing fix. `ISeriesManagementService.RegisterSeriesFromMetadata` signature changed from `Dictionary<string, double>?` to `Dictionary<string, object>?`, with a `FormatParam(object?)` helper handling double / float / int / long / bool / string / IConvertible / null cleanly. AddIndicatorModal.razor now branches the input render on `param.DataType` (text input for string, number input for numerics), `_editParams` is `Dictionary<string, object>`, `InitialEditValue` and `GetNumericDisplay` helpers handle the type-aware path safely. The `InvalidCastException` from `string.ToDouble()` that broke the modal in Session 1 is fixed at the root. Existing callers (`WorkspaceInitializer.cs`) pass null so the change is transparent.
- [x] **v9 strategy spec** — `BuildV9CrossSeriesConfluence` in BuiltInStrategySeeds. ID `builtin.long.v9-cross-series-confluence`. Score budget designed so Cipher leaves max out at 5.0 and the gate is 5.5 — pure-Cipher mathematically cannot fire. Cross-series leaves: Funding Rate < -0.005 (1.5), FNG Sentiment < 25 (1.5), OI Divergence (1.5), Crowding Short Crowded (2.0). Cipher leaves: blue dot (1.0), Cipher A buy (1.0), Cipher C Bottom Triple (1.5), Anchor Wave < -53 (1.5). Same risk plan as v7/v8 (ATR×2 stop, 1.5R/3R ladder, BE after TP1, 0.5% risk) for clean A/B comparison. **Moment of truth for the strategy thesis** — does adding orthogonal non-price data restore edge that v2-v8 couldn't find from price alone?

### Session 3 — v9 backtest + thesis verdict (planned, next session)

- [ ] **Test the refactor + CrowdingIndex end-to-end** — confirm shared cache means no duplicate fetches when multiple cross-series indicators load on the same chart, confirm modal string param branch renders text inputs (smoke test once a string-param indicator is needed), confirm CrowdingIndex line shows up with expected magnitude
- [ ] **Run v9 backtest** on BTC/USDT 1h Bitstamp, recent 30-90 day range (OKX history depth)
- [ ] **Verdict on the strategy thesis** — does v9 produce materially different walk-forward metrics than v7/v8?

### Session 4 — Glassnode + multi-asset (deferred)

- [ ] **`GlassnodeProvider` plugin** — when API key is purchased. Deep history (back to 2019) for funding/OI/sentiment. Same source-name swap pattern: change `Provider = "OkxDerivatives"` to `Provider = "Glassnode"` in each indicator's `CrossSeriesRequest` constant.
- [ ] **Multi-asset support** — once Glassnode is in or v9 thesis validated, expose `Source` and `Symbol` as string parameters on each cross-series indicator (modal already supports this). One indicator code that can target BTC/ETH/SOL via parameter.

### Session 5 — Glassnode plugin (deferred — paid)

- [ ] **`GlassnodeProvider`** — when API key is purchased. Same pattern as OkxDerivatives. Unlocks deep history for funding/OI/on-chain that the free providers cap out on.

---

## Phase 11 — DONE (2026-04-07)

End-to-end complete. The composite signal-composer pipeline ships in 7 sessions:
- **Session A** — Foundation (signal catalog, condition tree, risk plan, ConfigurableStrategy state machine, journal modal, backtester warmup)
- **Session B** — MTF data + R-multiple metrics + entry-armed state machine
- **Session C** — Level providers + S/R-aware stops/targets + level operators
- **Session C Hardening** — VPVR provider + remaining Phase-4 sources + future-leak fix on Ichimoku/CipherSR
- **Path A Correctness Pass** — Main-path future-leak fix + real WorkspaceState in backtester + IBacktestProfileCache + per-bar VPVR replay + HTF indicator pre-warm
- **Session D** — Builder UI (BuildSetupTab) + modal input trap fix
- **Phase 11 Complete pass** — D2 polish (cross-line operators wired, HTF bar pre-warm, read aloud, preview, export/import, 2nd descriptor picker, auto warmup button) + Session E (StrategyAutoLoader, AI Analyst review-my-setups)

---

## PHASE 0 — Zero-Risk Cleanup

- [x] **StatusBar double speech:** Removed `<StatusBar />` from `MainLayout.razor` (done in 2026-03-25 sprint).
- [x] **Documentation overhaul:** README, CHANGES, CODEBASE_KNOWLEDGE_BASE, PLATFORMS, TODO updated (2026-03-26).
- [x] **EventBus vs Rx documented:** Canonical routing decision table written in `CODEBASE_KNOWLEDGE_BASE.md` Section 5.
- [x] **HelpModal + User Guide combined:** `HelpModal.razor` enriched with conceptual User Guide content alongside keyboard reference.
- [x] **Stub annotations:** `BlazorAudioDriver.cs`, `AppDelegate.cs`, `CoinbaseProvider.cs` annotated with `// STUB: ... Phase 5 roadmap.` (2026-03-26).
- [x] **NAudio.Wasapi audit:** Confirmed `BlazorAudioDriver` is the only consumer, Windows-only, `BlazorClient.csproj` `Condition` guard in place. No changes required. Phase 5 removal tracked above.

---

## PHASE 1 — Accessibility Path Bug Fixes

### Bug: Dual Navigation Sonification Path (Double-fire / Click artifacts)
- [x] Identified root cause: `SonificationManager.SyncNavigationSlots` (Path 1, 0.4s) AND `NavigationFeedbackManager.SonifyCurrentContext` → `AudioFeedbackRouter.SonifyComponent` (Path 2, 0.2s) both write to voice slot 0.
- [x] Fix: Removed `SonifyCurrentContext()` call and `_audioRouter.Silence()` call from `NavigationFeedbackManager.HandleNavigationFeedback`. NavigationFeedbackManager now handles SPEECH ONLY.
- [x] Fix: `SonificationManager` is the single authoritative audio path for navigation.

### Note: Audio Glide / ADSR / Default Volume
- [x] `AudioEngine` already has `ENVELOPE_SAMPLES = 220` (~5ms at 44100 Hz) providing attack/release. `continuous: false, 0.4s` in `SyncNavigationSlots` is intentional and correct — keydown repeat ensures seamless audio while held; the note fades naturally when released. No change required.
- [x] `WorkspaceState.Initial.ChartVolume` was already `0.5f`. No change required.

### Bug: No Loading-State Speech Feedback
- [x] `AccessibilityFeedbackCoordinator.OnStateChanged` already announces "Loading history..." on `DataStatus.LoadingHistorical` entry (was present). Verified this covers left-arrow during backfill.
- [x] Added: announce "Ready" on `InitializationStatus` transition from `Loading` → `Ready`.

### Bug: IsInputActive / IsChartFocused Race (Keys silently eaten)
- [x] Added `_isChartActive` gate to `CommandDispatcher`. Subscribes to `ChartFocusEvent` (→ true) and `DeactivateEvent` (→ false, 50ms debounce). Navigation, playback, and drawing commands gated. Global commands (F1–F8, volume, modal opens) bypass. Starts `true` so startup navigation works immediately. `CommandDispatcher` is now `IDisposable`.

---

## PHASE 2 — Data Pipeline Bug Fixes

### Bug: All Indicators Show "No Data" After Add
- [x] Root cause confirmed: `MarketOrchestrator.LoadChartAsync` dispatches `InitializationStatus.Ready` after `RefreshDataAsync()`. Verified this is in place.
- [x] `DataOrchestrationService` subscribes to `IndicatorUpdatedEvent` → `OnDataUpdated(forceFull: true)` for immediate recalculation when a series is added.
- [x] Profile recalculation on viewport change (zoom/pan): StateStream subscription now checks for active profile series and passes `forceFull: true` when any are present. Profiles (VPVR/TPO) re-slice visible bars on every pan/zoom. (2026-03-27 session 2)
- [x] Heatmap order book pipeline fixed: `GetOrderBookAsync` now called before the `needsFull` branch so snapshots accumulate on every tick. `needsFull` excludes profile/heatmap from the "empty data" trigger, breaking the infinite-full-recalc loop that starved the history service. (2026-03-27 session 2)
- [x] Added "No data" fallback in `NavigationFeedbackManager`/`BinnedNavigationStrategy.NavigateY` when focused series bins are empty (was already present per CHANGES.md Phase 2).

### Bug: Historical Data Not Loading on Scroll-Left
- [x] Resolved dual-trigger race: `PrependOlderDataAsync` is owned exclusively by `HistoryBufferCoordinator` via `RequestHistoryEvent`. `DataOrchestrationService.StateStream` subscription does NOT trigger backfill.
- [x] Bitstamp `FetchOhlcvAsync` missing `&end=` parameter — added (2026-03-25 sprint).
- [x] FRED `FetchOhlcvAsync` missing `observation_end` parameter — added (2026-03-25 sprint).
- [x] During `DataStatus.LoadingHistorical`, "Loading history..." announced (AccessibilityFeedbackCoordinator).
- [x] Right-arrow and series-switch allowed to produce feedback normally during historical backfill.

### Bug: Space Plays Only Focused Series (PlaybackScope Not Differentiated)
- [x] `CommandDispatcher.HandlePlayback` correctly dispatches `SetPlaybackAction(true, PlaybackScope.Chart/Series/Component)` per key binding.
- [x] Added `componentFilter` parameter to `IAudioSequencer.StartPlaybackAsync`. `-1` = all components (Series), `n` = specific component (Component). `PlaybackOrchestrator` passes the correct filter.
- [x] Chart scope anchors to `CoreSeriesIds.Candles` starting from `ViewportStartIndex`. Full multi-series layered audio is Phase 5 roadmap.

### Bug: No Audio Feedback at Data Boundaries
- [x] Added `FeedbackType.Boundary` to `FeedbackType` enum.
- [x] `NavigationEngine.NavigateX`: publishes `FeedbackType.Boundary` earcon when `strategy.NavigateX` returns `Success = false` (cursor already at edge).
- [x] `AccessibilityFeedbackCoordinator` handles `Boundary` with earcon-only (no speech per user preference).
- [x] `AudioFeedbackRouter.PlayEarcon` maps `Boundary` → `IEarconService.PlayBoundary()`.

---

## PHASE 3 — Structural Cleanup

### EventBus Rationalization
- [x] All EventBus subscriptions audited. Categorized as modal lifecycle (keep) or data-flow (use AsObservable or direct Rx). Decision documented in CODEBASE_KNOWLEDGE_BASE.md Section 5.
- [x] `NavKeyReleasedEvent` already consumed via `_eventBus.AsObservable<NavKeyReleasedEvent>()` — correct pattern confirmed.
- [x] `IndicatorUpdatedEvent` already consumed via `_eventBus.AsObservable<IndicatorUpdatedEvent>()` — correct pattern confirmed.
- [x] No EventBus subscriptions found that should be migrated to direct Rx streams — existing usage is already appropriate.

### HelpModal + User Guide Consolidation
- [x] `HelpModal.razor` enriched with "Understanding the Soundscape" conceptual section from USER_GUIDE.md.
- [x] Volume Profiles guidance added to HelpModal.
- [x] Drawing tools workflow (sequential anchoring) added to HelpModal.
- [x] Indicator customization guidance added to HelpModal.
- [x] SHORTCUTS.md remains as a standalone reference document (not removed).
- [x] USER_GUIDE.md remains as a standalone reference document (not removed).
- [x] Help button in toolbar retained — opens HelpModal via `OpenHelpEvent` on EventBus.

### NAudio.Wasapi Audit
- [x] Confirmed `BlazorAudioDriver` is the only consumer. Windows-only, `BlazorClient.csproj` `Condition` guard in place. Phase 5 removal tracked in roadmap section.

---

## PHASE 4 — SRP Refactoring

### CommandDispatcher — Chart-Focus Gate + Structural Clarity
- [x] Added `_isChartActive` flag with EventBus subscriptions (done in Phase 1 above).
- [x] Added numbered section comments (1–6), `IsDrawingCommand()` helper, `IDisposable` implementation.
- [x] **`IndicatorCrossingEngine` extracted:** All crossing/scan logic moved from `CommandDispatcher` to `IndicatorCrossingEngine`. `CommandDispatcher` injects it and delegates `HandleCrossJump`. Methods `ScanSignCrossing`/`ScanThresholdCrossing` are now `internal static` on the engine (tested via reflection).
- [ ] Full Command Pattern (extract `ICommandHandler<T>` per domain) — deferred to Phase 5+ if dispatcher grows beyond current size.

### DrawingService — Strategy Pattern Extraction
- [x] **`IDrawingCalculator` interface** created in `Sdk/Interfaces/IDrawingCalculator.cs`.
- [x] **15 calculator classes** created in `Core/Services/Drawing/Calculators/`: `HorizontalLineCalculator`, `VerticalLineCalculator`, `TrendLineCalculator`, `ChannelCalculator`, `FibRetracementCalculator`, `TextLabelCalculator`, `FibExtensionCalculator`, `GannFanCalculator`, `RectangleCalculator`, `RiskRewardCalculator`, `AnchoredVwapCalculator`, `MeasureToolCalculator`, `GannBoxCalculator`, `AndrewsPitchforkCalculator`, `AngleFibCalculator`.
- [x] **`DrawingService` rewritten** as a registry/dispatcher — resolves `IEnumerable<IDrawingCalculator>` from DI and routes by `DrawingType`. New tools can be dropped into `Drawing/Calculators/` without touching `DrawingService`.
- [x] **`DrawingCalculatorHelper`** — shared `FindIndex` / `CalculateLinearPoints` utility used by calculators that need index lookup or linear math.
- [x] All 15 calculators registered in `ServiceCollectionExtensions.AddRenderingServices`.

### SkenderIndicatorProvider — GetDetailFact Extraction
- [x] **`IDetailFactProvider` interface** created in `Sdk/Interfaces/IDetailFactProvider.cs`.
- [x] **`SkenderDetailFactProvider`** created in `Core/Services/Indicators/` — all 10 indicator-fact cases (RSI, BB, MACD, MA, Stochastic, VWAP, ATR, CCI, ADX, generic) extracted verbatim.
- [x] **`SkenderIndicatorProvider`** delegates `GetDetailFact` to `SkenderDetailFactProvider` — the fact logic is now independently testable and library-agnostic.
- [ ] Split `SkenderIndicatorProvider` into `SkenderIndicatorDiscovery` + `SkenderResultMapper` — deferred to Phase 5+ when a second Skender-based provider is added.

### WorkspaceStore — Domain Section Comments
- [x] Added domain-section comment headers to `Reduce()` switch expression (IDENTITY/MODE, DATA, NAVIGATION, PLAYBACK, etc.).
- [x] Added XML doc comment to `Reduce()` explaining delegation pattern.
- [x] Full slice reducer decomposition — shipped 2026-04-22 as 5 per-domain reducers.

---

## [2026-04-05] — Cipher S Algorithm Revamp + Viewport Right Margin

### Cipher S — Algorithm v5 (2026-04-05)
- [x] **High-low channel normalization:** Replaced percentile rank count with `(close - wLow) / (wHigh - wLow) × 100`. Anchors sentiment to the current cycle's own extremes, not multi-year rank table. Eliminates missing cold-color phases (blue/teal/cyan) on secularly trending assets like BTC.
- [x] **5th/95th percentile clipping:** Sort window closes; use 5th/95th percentile index as wLow/wHigh. Prevents flash crashes and thin-volume ATH spikes from compressing all other bars into a narrow mid-band.
- [x] **3-bar EMA smoothing (α = 0.5):** Applied to rawPct before phase mapping. Eliminates single-candle phase flicker without distorting the trend.
- [x] **Incremental tick optimization:** `RequiresFullRecalcOnTick = false`. `UpdateLast()` implemented — recalculates only the last bar; reads `pctSpan[i-1]` from buffer as EMA seed. Per-tick cost reduced from O(n×window) to O(window). Scroll-back correctness preserved — DataOrchestrationService already triggers full recalc on historical prepend.

### Viewport Right Margin (2026-04-05)
- [x] **`RightMarginBars = 20` in `WorkspaceState`:** Added to record, `TabSnapshot`, `Initial`. Default 20 bars of empty future-space reserved on the right of the viewport for trendline projection.
- [x] **`ViewportNavigationService` fully rewritten:** All four methods (`Navigate`, `Pan`, `Zoom`, `ClampViewportToData`) use `effectiveWindow = ViewportLength - RightMarginBars`. `ClampViewportToData` no longer mutates `ViewportLength`. `Zoom` anchors to `lastDataBar` so the margin slot count stays constant.
- [x] **`WorkspaceStore` updated:** `UpdateData`, `JumpToLatestAction`, `ZoomAction`, `SnapshotFromState`, `RestoreSnapshot`, `AddTab` all use/carry `RightMarginBars`.
- [x] **Left-side compression fixed (xOffset removed from `ChartRenderer`):** Removed `float xOffset = rect.Width - (visibleData.Count * itemWidth)` and the corresponding `xOffsetForAxis`. Bar positions now start uniformly from `rect.Left`. Empty space falls naturally to the right of the last bar through the right margin architecture.

**Build: 0 errors, 0 warnings. Tests: 236/236 passing.**

---

## MEDIUM PRIORITY — Future Work

### Drawing Tool Refinement
- [x] **Live Preview for trendline dragging + full mouse UX sweep**
  (shipped 2026-04-24). Click-drag placement creates a preview series on
  MouseDown that follows the cursor on every MouseMove and commits on
  MouseUp. Existing drawings can be repositioned by grabbing their anchor
  handles (10 px hit-test). Right-click opens a floating
  Delete/Duplicate/Properties menu. Scroll-wheel zoom centres on the
  cursor. JS `mousemove`/`wheel` listeners + three new `[JSInvokable]`
  entry points; `WheelZoomAction` + `ViewportReducer.WheelZoom`. 4 new
  pinning tests — 562/562 green.
- [x] Add "Coordinate Entry" mode for accessibility-first drawing creation (keyboard-only placement without cursor). *(Phase I, 2026-03-31)*

### Technical Analysis Polish
- [x] Implement Bollinger Band 'Squeeze' and 'Expansion' logic in `IndicatorContextAnalyzer.GetDetailFact`.
  Shipped 2026-04-24 in `BarDetailService.BollingerSqueezeExpansionFact`
  (20-bar avg width with ±10% thresholds).
- [x] Add MACD crossover facts (Bullish/Bearish crosses) to `BarDetailService`.
  Shipped 2026-04-24 in `BarDetailService.MacdCrossoverFact`.
- [x] Implement Volume-Profile POC-crossing alerts in `AlertEvaluator`.
  Shipped 2026-04-24: `AlertTarget.Poc` + `ILevelService` POC resolution.

### Ctrl+Left/Right Crossing Navigation Redesign
- [x] Generalized to use focused series type (Phase J, 2026-03-31): price/candles → trendline, zero-line oscillators → zero cross, threshold oscillators → OB/OS entry/exit, MA overlays → price/MA cross, %B → band crossing, sparse markers → nearest non-NaN signal.
- [x] Crossing logic extracted to `IndicatorCrossingEngine` (Phase 4-SRP, 2026-04-01) — independently testable, no longer coupled to `CommandDispatcher`.
- [x] Multiple trendlines: use the focused drawing, not "all trendlines."
  Shipped 2026-04-24 in `IndicatorCrossingEngine.DoFocusedTrendlineCrossJump`.

---

## [2026-03-28] — Session Fixes

### Heatmap Arrow Navigation "No Data" (fixed)
- [x] `BinnedNavigationStrategy.NavigateY`: bin count now uses `LastOrDefault(l => l?.Count > 0)?.Count ?? 0` — no longer depends on `CurrentDataIndex` backwards search that fails when cursor is in historical area.
- [x] `NavigationFeedbackManager.FindNearestHeatmapIndex`: falls back to forward search (last live snapshot) if backwards search from `CurrentDataIndex` finds nothing.
- [x] `IndicatorOrchestrator.RecalculateLastAsync`: heatmap `HeatmapData[^1]` is now only overwritten when `lastBarBins.Count > 0`. Previously an empty bids/asks response reset the live snapshot to empty on every tick where order book data was momentarily unavailable, causing subsequent navigation to see all-empty HeatmapData and report "No data".

### Wick Solo Playback & Ping Duration (fixed)
- [x] `AudioSequencer.StartPlaybackAsync` and `StartMultiSeriesPlaybackAsync`: Ping envelopes now receive `durationSeconds = min(0.15, msPerBar × 0.8 / 1000)` instead of `0.0`. This makes wick pings audible and lets them ring out.
- [x] `SonificationProfileProvider`: wick profile reverted to `PitchMapping.None`.
- [x] `DefaultSonificationStrategy.CreateAudioPoint`: upper wick → 880 Hz, lower wick → 220 Hz (fixed tones, `FreqMultiplier` still applied for per-user tuning).

### Alt+C / Alt+L Toggle Speech (fixed)
- [x] `AccessibilityFeedbackCoordinator.OnStateChanged`: announces "Heikin-Ashi candles"/"Standard candles" and "Log scale"/"Linear scale" on state change.
- [x] F2/F3/Alt+C/Alt+L toggle checks all moved before the `IsPlaying` gate.

### Heikin-Ashi Navigation Speech (fixed)
- [x] `NavigationFeedbackManager.HandleNavigationFeedback`: when `state.IsHeikinAshi`, computes HA bar via `ChartMath.CalculateHeikinAshi` for the current index before passing to formatter. Spoken OHLC values now match the visual chart.

### Heikin-Ashi Navigation Sonification (fixed)
- [x] `NavigationSonifier.SyncNavigationSlots`: added `using AccessibleTrader.Core.Services`. When `state.IsHeikinAshi`, computes HA bar via `ChartMath.CalculateHeikinAshi` and uses it as the audio source `navPoint`. `PitchMapping.Direction` (bullish/bearish pitch) now reflects HA candle direction rather than raw bar direction, matching the visual chart and speech output.

### Candle Colors in Properties Dialog (fixed)
- [x] `StandardRenderers.RenderCandles`: body color uses `comp.ColorHex` (bullish) and `comp.ColorHexSecondary` (bearish) from the Candle Body component config.
- [x] `PropertiesModal.razor`: Candle display-type components show Bullish/Bearish color pickers; all others show single Color picker.
- [x] `SettingsModal.razor`: Removed read-only candle color swatches; replaced with note directing users to Properties dialog (Shift+F12).

### Heatmap AND Profile Bin Navigation Speech "No Data" (fixed)
- [x] **Root cause:** `NavigationFeedbackManager.HandleNavigationFeedback` evaluated `isProfile` before `isHeatmap` in the speech-formatting block. `IndicatorModelFactory` sets `IsProfile = true` for heatmap series (`meta.Code == "HEATMAP"`), so heatmaps entered the profile branch, which checked `s.Data.ProfileBins.Count` (always 0 for heatmaps) and spoke "No data". Profiles were separately affected: when profile bins were empty at navigation time the same "No data" path fired.
- [x] **Fix:** Swapped if/else-if order in `NavigationFeedbackManager` — `isHeatmap` is now checked first, matching the already-correct ordering in `BinnedNavigationStrategy`. Heatmaps now correctly enter `FormatHeatmapFeedback`; profiles use `FormatProfileFeedback` as intended.

---

## PHASE 5 — Indicator Pane Robustness & Multi-Instance Indicators (2026-03-28 Session 4)

### Multiple Indicator Instances
- [x] **Multiple instances of same indicator type blocked:** `SeriesManagementService.RegisterSeries` gave every non-core indicator the same `id.ToLowerInvariant()` ID. Second EMA/RSI/etc. hit duplicate guard and silently returned. Fixed: non-core indicators always receive `Guid.NewGuid()` ID; only the four core singletons keep deterministic IDs.

### Indicator Pane Height Robustness
- [x] **Fixed 70/30 height split becomes unreadable with 3+ indicators:** Added `MinIndicatorPaneHeightPx = 80f` floor (density-scaled) to `ChartRenderer`. Main pane clamped to minimum 25% of total height. Bottom panes clip gracefully at canvas edge.

### Crosshair Across All Panes
- [x] **Crosshair vertical line stopped at main pane bottom:** `RenderCrosshair` now draws vertical line across full chart height. Each indicator pane also receives its own horizontal crosshair at the cursor's indicator value (first non-NaN component value, slightly dimmed to distinguish from main pane crosshair).

### Reference Level Source of Truth
- [x] **`IndicatorReferenceLevels` static class:** Single source of truth for all OB/OS/zero/midpoint definitions. Both `SeriesManagementService.InjectDefaultLevels` and `StylingService.GetLevelComponents` delegate here.
- [x] **`ViewportRangeCalculator` expands pane ranges to include level values:** OB=70/OS=30 always on-screen for RSI; zero-line always on-screen for MACD — regardless of where data currently sits.
- [x] **Hidden levels excluded from range expansion:** `IsVisible = false` levels do not expand the pane range.

### Settings & Workspace Persistence
- [x] **Theme persistence wired:** `ThemeService` reads/writes via `ISettingsManager`.
- [x] **Alert persistence wired:** `WorkspaceLibraryService` `SaveAlerts`/`LoadAlerts` via `alerts.json`.
- [x] **Workspace layout persistence wired:** `SeriesManagementService.PersistWorkspace()` saves active series configs; `WorkspaceInitializer` restores on startup.

### Tests
- [x] **`ReferenceLevelTests.cs` (28 tests):** All indicator families, case-insensitivity, level injection via `RegisterSeries`.
- [x] **`BackfillManagerTests.cs` (5 tests):** Queue, persistence, error resilience, cancellation.
- [x] **`ViewportRangeCalculatorTests.cs` (8 tests):** Guard cases, pane range calculation, level expansion, hidden levels, shared pane with two same-type oscillators.

**Build after phase: 0 errors 0 warnings. Tests: 69/69 passing.**

---

## PHASE 5 (PLANNED) — Pane Layout UX & Crosshair Value Labels

### User-Resizable Pane Dividers *(Phase 5 roadmap)*
- [x] **Pane divider drag interaction:** `PaneHeightRatios` in `WorkspaceState` (`ImmutableDictionary<string, float>`). `ResizePaneAction` adjusts ratios, clamped [0.05, 0.60]. `IPaneLayoutService` published by `ChartRenderer` after each render; `ChartArea.razor` renders CSS drag-handle divs and dispatches on `@onmousemove`. `SetPaneHeightRatiosAction` restores from saved workspace.
- [x] **Minimum pane size enforcement during drag:** 80px floor (density-scaled) in `ChartRenderer`; main pane floored at 25% of total height.
- [x] **Persist pane ratios to workspace profile:** `WorkspaceConfiguration.PaneHeightRatios` serialised via `WorkspaceInitializer.SaveWorkspace()`; restored in `InitializeDefaultSeries()`.

### Scrollable Pane Area *(Phase 5 roadmap)*
- [x] **Vertical scroll offset for indicator panes:** `IndicatorPaneScrollIndex` in `WorkspaceState`. `ScrollIndicatorPanesAction`. Alt+Up/Down bound in `ShortcutManager`; handled in `CommandDispatcher`. Speech: "Scroll panes up/down".

### Crosshair Y-Value Labels in Indicator Panes *(Phase 5 roadmap)*
- [x] **Per-pane value label on crosshair:** `ChartRenderer.RenderCrosshair` draws numeric label at right edge of each indicator pane at the crosshair Y position (same font as Y-axis labels).

---

## PHASE 6 — Audio Fidelity, Shortcuts & Indicator Reference Lines

### Audio Playback Glide Fix
- [x] **Playback click artifacts:** `AudioSequencer.StartPlaybackAsync` changed to `continuous: true, duration: 0.0`. AudioEngine glide smooths frequency/volume between bars — no envelope restart click.
- [x] **Candle body sonification identical to bars:** Verified — candle body uses same `SonificationStrategy` path as bars. No separate waveform injected.

### Wick & Candle Playback Fixes (2026-03-27 session 2)
- [x] **Wick ping restart during playback:** `AudioSequencer` now uses `continuous = (envelopeType != "Ping")`. Wick "Ping" envelopes restart on each bar; sustain-enveloped lines glide as before.
- [x] **Candle body too quiet:** `SonificationProfileProvider` changed candle body from `AmplitudeMapping.Size` to `AmplitudeMapping.None`. Body always plays at full `baseVolume`; bullish/bearish pitch preserved via `PitchMapping.Direction`.
- [x] **Null-ref guard:** `series.Pane ?? ""` added in `AudioSequencer.StartPlaybackAsync` pane-range lookup.

### Data Pipeline Fixes (2026-03-27 session 2)
- [x] **Indicator flat-line on historical prepend:** `DataOrchestrationService.OnDataUpdated()` now detects prepend via `_lastFirstBarDate` and sets `forceFull: true`, triggering `RecalculateAllAsync` to re-index all indicator buffers against the new bar range.
- [x] **Profile recalculation on pan/zoom:** StateStream subscription now passes `forceFull: true` when any profile series is active, so VPVR/TPO always re-slice their visible-bar window.
- [x] **Heatmap order book history fix:** `GetOrderBookAsync` moved before the `needsFull` branch. Snapshots accumulate on every tick; `needsFull` no longer includes heatmap/profile in the "empty data" trigger (was causing an infinite full-recalc loop that starved the history service).

### Navigation Note Duration
- [x] **Navigation note duration:** Reduced from `0.4s` to `0.15s` in `NavigationSonifier.SyncNavigationSlots`. Home/End/PgUp/PgDn feel crisp; held-arrow gives rapid staccato.

### Drawing Shortcuts in HelpModal
- [x] **All 15 Ctrl+Shift drawing shortcuts documented:** Added Ctrl+Shift+A/B/E/G/J/M/P/R/W to `HelpModal.razor` shortcut table.
- [x] **Alt+B = Order Book button:** `ShortcutManager`, `CommandDispatcher`, `Toolbar.razor` all wired. `OrderBookModal.razor` created.

### Indicator Reference Lines
- [x] **Auto-inject reference levels on indicator add:** `SeriesManagementService.InjectDefaultLevels` called in `RegisterSeries`. RSI/MFI/STOCH: 30/50/70. MACD/ROC/CCI/etc.: zero-line. AROON: 50. PERCENTB: 0/0.5/1.

### Volume Bar Direction Colors
- [x] **Volume bars colored by price direction:** `StandardRenderers.RenderDirectionalBars` colors green/red per OHLCV bar. `DataLayer` routes `CoreSeriesIds.Volume` series to this method.

### General Bar Coloring Rule (All Indicators)
- [x] **All Bar/Histogram components use directional coloring:** `StandardRenderers.RenderDirectionalBars` generalized to use `comp.ColorSource`. `ColorSource.PriceAction` → candle direction (green/red). `ColorSource.Value` → value sign (positive/negative). No special-casing per indicator — the rule is universal. `DataLayer` removed the `CoreSeriesIds.Volume` special-case; all `Bar`/`Histogram` display types now route to `RenderDirectionalBars`.

### Simultaneous Multi-Series Playback (Space = Chart Scope)
- [x] **Space plays all series simultaneously:** `IAudioSequencer.StartMultiSeriesPlaybackAsync` added. Bar-by-bar loop iterates all visible non-drawing non-profile series. Each series gets up to `SlotsPerSeries = 8` voice slots (`PlaybackSlotOffset + (sIdx × 8) + cIdx`). `PlaybackOrchestrator` Chart scope now calls `StartMultiSeriesPlaybackAsync` (was previously sequential pane playback).
- [x] **Wick / per-component audio in playback:** `ISonificationStrategy.MapComponentToAudio` added. Each component maps its own pitch/amplitude independently. `AudioSequencer` calls `MapComponentToAudio(series, cIdx, ...)` per slot rather than the single `MapToAudio` which always returned the first component's audio.

### Profile Sonification on Arrow Key Navigation
- [x] **Profile sonification fires on bin Up/Down:** `SonificationManager` now includes `binChanged = state.FocusedBinIndex != _currentState.FocusedBinIndex` in the `SyncNavigationSlots` trigger condition.

### Live Bar Intra-Bar Component Array Sync
- [x] **Component arrays updated on intra-bar ticks:** `WorkspaceStore.UpdateData` now has an `else if (!initial && list.Count > 0)` branch that clones the component array and updates only `arr[^1]` for DataMapping fields (Open/High/Low/Close/Volume) when a live tick replaces the last bar without changing bar count.

### Modal Visibility / Chart Canvas Hide-Show
- [x] **Modals now visually appear:** `MainPage.xaml` reverted to original order (BlazorWebView bottom, SKCanvasView top — Skia renders correctly on top). Added `ModalStateChangedEvent(bool IsOpen)` to `Events.cs`. All 11 modals publish this event in `ShowAsync()` and `Close()`. `MainPage.xaml.cs` hides `_chartCanvas` on first modal open and restores it on last close (reference-counted to handle nested modals).

---

## PHASE 6 — Provider Plugin Completion

### Market Type Selection
- [x] **Spot/Futures dropdown:** `MarketOrchestrator` extended with `SelectedSubType`/`AvailableSubTypes`. Toolbar shows conditional sub-type dropdown when `AvailableSubTypes.Count > 1`. `LoadChartAsync` passes `marketKey = "{market}|{subType}"`.

### API Key Infrastructure
- [x] **API key required gating:** `MarketOrchestrator.RefreshSymbolsAsync` places `ApiKeyRequiredSentinel` in symbol list when provider requires key but none configured. Toolbar `Load` button disabled for sentinel value.
- [x] **Alt+K keys wired:** `ApiKeyService` + `TradingDashboardModal` use stored keys. `GeneralOrderService` passes provider name to all calls.

### Live Stream Audit
- [x] **Alpaca:** Switched from 15s REST polling to WebSocket v2 live bars. OrderUpdateStream wired from `trade_updates` WebSocket.
- [x] **Binance OrderUpdateStream:** verified 2026-04-23 — `_listenKey` + `KeepAliveUserStreamAsync` + `StopUserStreamAsync` wired in `BinanceProvider.cs`. See also the `[x]` entry in the Phase 7 block below.
- [x] **Bitstamp OrderUpdateStream:** verified 2026-04-23 — `SubscribePrivateChannelAsync` + `private-my_orders-{pair}` handling shipped; see the `[x]` entry in the Phase 7 block below.

### Binance Plugin
- [x] **Futures PlaceOrderAsync:** Routes to `UsdFuturesApi.Trading.PlaceOrderAsync` when `signal.SubType == "Futures"`. Applies leverage before order. Attaches TP stop as separate order.
- [x] **Binance User Data Stream:** fully shipped per 2026-04-23 re-read — listenKey create/keep-alive/close + `onOrderUpdateMessage` callback produces `OrderUpdate` records (status PartiallyFilled / Filled / Cancelled / Rejected; Stop/TP flags derived from order type) and pushes through `_orderUpdateSubject`.

### Bitstamp Plugin
- [x] HMAC-SHA256 trading fully implemented. WebSocket live trades + order book diff stream.
- [x] Wire `order` channel events → `OrderUpdateStream` — shipped per `SubscribePrivateChannelAsync` in `BitstampProvider.cs`.

### Alpaca Plugin
- [x] WebSocket v2 data stream (stocks + crypto). Trade update WebSocket wired to `OrderUpdateStream`.

### Coinbase Plugin
- [x] JWT auth implemented (ECDsa PEM key, `GenerateJwt`). WebSocket user channel wires order updates. Full trading API implemented (pending live test).

### Polygon Plugin
- [x] WebSocket live feed implemented (`delayed.polygon.io`). Stocks/crypto/forex routing by market prefix.

### FRED Plugin
- [x] REST OHLCV fetch implemented with frequency mapping. Irregular dates handled by FRED API's `frequency` param.

### Trading Dashboard
- [x] **Margin type selector:** Cross/Isolated dropdown shown when `_supportsMargin = true`. `SupportsMarginTradingAsync` added to service.
- [x] **Leverage field:** Shown with max leverage cap when margin supported.
- [x] **Take Profit field:** Added to order entry form.
- [x] **Accessible order book table:** `role="table"` + `<thead>/<tbody>/<th scope="col">` replaces `<div class="book-row">`.
- [x] **Full signal wiring:** `SubmitOrder` passes `SubType`, `MarginType`, `Leverage`, `TakeProfit` in `TradeSignal`.
- [x] **TradeSignal:** `SubType` + `MarginType` fields added to SDK record.

---

## PHASE 7 — Platform Parity & Feature Completion (Roadmap)

### Platform Parity
- [x] **Mac Keyboard Input:** shipped as `KeyboardPageHandler` + `KeyboardViewController` (line 1224 below).
- [x] **Android Audio Output:** shipped as `AudioTrack` PCM-Float push loop (line 1225 below).
- [x] **iOS/Mac Catalyst Audio Output:** shipped as `AVAudioEngine` + `AVAudioSourceNode` (line 1226 below).
- [x] **NAudio.Wasapi Removal** — shipped 2026-04-24. `BlazorAudioDriver`
  now plays Float32 on Windows via a winmm.dll P/Invoke (waveOut with a
  three-buffer round-robin); package reference dropped from
  `BlazorClient.csproj`. Android AudioTrack + iOS/macCatalyst AVAudioEngine
  paths unchanged. User will verify Windows audio in a later session.

### Remaining Provider Gaps
- [x] **Binance User Data Stream:** `StartUserDataStreamAsync` creates listenKey, subscribes via `_socketClient.SpotApi.Account.SubscribeToUserDataUpdatesAsync`, 25-min keepalive timer, cleanup in `DisconnectAsync`.
- [x] **Bitstamp OrderUpdateStream:** `SubscribePrivateChannelAsync` sends HMAC-SHA256 auth for `private-my_orders-{pair}`; `ReceiveLoop` handles `order_changed`/`order_deleted` → `_orderUpdateSubject`.

### Feature Completion
- [x] **Strategy Backtester UI:** `StrategyModal.razor` — Backtest section with capital/commission/slippage inputs, Run button, results grid (trades/win rate/P&L/drawdown/Sharpe), trade log details. `IStrategyBacktester` DI-registered in `ServiceCollectionExtensions`.
- [x] **Custom Speech Template Editor** (shipped 2026-04-24, scope
  corrected) — per-indicator speech templates are now editable in the
  **Indicator Properties modal** (`PropertiesModal.razor`), not the
  Settings modal. The original TODO placed this in `SettingsModal`
  which was the wrong scope: per-indicator templates belong on the
  indicator instance, not app-wide settings. The new **Speech** tab
  edits `ComponentConfig.SpeechTemplate` + `SignalSpeechTemplate`
  directly — fields were already present on the model and already
  consumed by `SpeechFormatter`; only the UI was missing. Reset-to-
  default button restores provider metadata defaults.
  `SpeechTemplateOverrideTests.cs` pins the contract (4 tests).
- [ ] **Multi-Symbol Watchlist:** Extend `WorkspaceState` to hold collection of `ChartState`.

### Platform Parity
- [x] **Mac Keyboard Input:** `KeyboardPageHandler` (custom `PageHandler`) with `KeyboardViewController` override of `PressesBegan`. Uses NSEvent Unicode private-use characters for special keys. Registered in `MauiProgram.cs` via `#if MACCATALYST`.
- [x] **Android Audio Output:** `AudioTrack` PCM-Float push loop on `TaskCreationOptions.LongRunning` thread in `BlazorAudioDriver` under `#if ANDROID`.
- [x] **iOS/Mac Catalyst Audio Output:** `AVAudioEngine` + `AVAudioSourceNode` render callback in `BlazorAudioDriver` under `#if IOS || MACCATALYST`. De-interleaved via `Marshal.Copy`.
- [~] **NAudio.Wasapi Removal:** tracked above in the top Platform Parity section.

### Chart Focus Shortcut
- [x] **Ctrl+Alt+Shift+C:** `SystemCommand.ChartFocus`, `ShortcutManager` binding, `CommandDispatcher` handler publishes `ChartFocusEvent` + `CONTEXT_SUMMARY` feedback. `HelpModal.razor` and `SHORTCUTS.md` updated.

### Performance (from previous Phase 6)
- [ ] **Span-Based Indicator Pipeline:** `ReadOnlySpan<Ohlcv>` + `ArrayPool<double>` in `SkenderIndicatorFactory`.
- [ ] **Full Channels Migration:** `Channel<Ohlcv>` from plugin → `DataManager` for live ticks.
- [x] **Voice Slot Pooling:** shipped 2026-04-24. `OscillatorVoice[]`
  was already pool-allocated at ctor; the real hot-path allocation was
  `wave.ToLower()` in `SetVoice`. Extracted `ParseWaveform` using
  `StringComparison.OrdinalIgnoreCase` — zero allocations on the
  300-calls/sec playback path.
- [x] **EventBus Batch Notifications:** shipped 2026-04-24.
  `SubscribeCoalesced<T>(handler, quietWindow)` (Rx `Throttle`) +
  `SubscribeSampled<T>(handler, window)` (Rx `Sample`) on `IEventBus`.

---

## PHASE 8 — Code Quality & Robustness (from 2026-03-28 Architectural Assessment)

### AudioEngine Thread Safety
- [x] **`StopAll()` and `Reset()` bypass the ring buffer:** Removed direct `_voices[i].*` writes from both methods. All voice mutations now route exclusively through `EnqueueCommand` → ring buffer → `Read()` on the audio callback thread. Master gain is reset directly (single aligned float — no torn read on x86/x64). Voice deactivation happens via the master-gain fade path inside `Read()`, which is the safe write path.

### Platform Stub Enforcement
- [x] **Mac keyboard input not wired — silent failure today:** `AppStartupService.WarnAboutUnimplementedPlatformFeatures` now emits a speech announcement and `LogWarning` under `#if MACCATALYST`.
- [x] **Android/iOS audio not wired:** Same method emits warning under `#if ANDROID` / `#if IOS`.

### Resilience Tests (MISSING COVERAGE)
- [x] **Resilience tests added (`ResilienceTests.cs` — 6 tests):**
  - `FetchOhlcv_WhenNonRetriableExceptionThrown_ShouldReturnEmptyAndFault`
  - `FetchOhlcv_WhenHttpExceptionThrown_ShouldReturnEmptyAndFaultAfterRetry`
  - `FetchOhlcv_WhenCircuitAlreadyOpen_ShouldReturnEmptyAndFaultQuickly`
  - `FetchOhlcv_WhenCancelled_ShouldReturnEmptyCleanly`
  - `FetchOhlcv_OnError_ShouldPublishFeedbackRequestEvent`
  - `FetchOhlcv_WhenSilentAndFails_ShouldNotPublishEventsOrChangeState`

### DI Feature Slices
- [x] **`ServiceCollectionExtensions` refactored into domain slices:** `AddAccessibleTraderServices` now delegates to eight private static helpers — `AddCoreInfrastructure`, `AddDataPipeline`, `AddIndicatorPipeline`, `AddRenderingServices`, `AddBusinessServices`, `AddInputRouting`, `AddAudioServices`, `AddAccessibilityServices`. No runtime change.

### Modal Contract Enforcement
- [x] **`ModalBase.cs` created** — `ModalBase : ComponentBase, IDisposable` provides `ShowModalAsync(headingElementId)` and `CloseModal()` which always publish `ModalStateChangedEvent`. `AlertsModal.razor` migrated as the reference implementation (`@inherits ModalBase`). Remaining 10 modals are functional as-is; migrate them when touching each one (tracked in Phase 7).

---

## PHASE 9 — Known Bug Fixes (from 2026-03-28 Architectural Assessment)

These are confirmed code bugs identified during the architectural review session. They cause silent incorrect behaviour rather than crashes. Fix in priority order.

### AlertEvaluator — Indicator Crossover Alerts Broken
- [x] **Root cause:** `AlertOrchestrator` always passed an empty `previousValues` dict. Fixed: `AlertOrchestrator` now maintains `_previousValues`, populated after each tick from all active indicator component values. Crossover detection now works correctly.

### IndicatorContextAnalyzer — Wrong Component Selection
- [x] **Root cause:** `Analyze()` picked the first visible component rather than the registered definition's `ComponentName`. Fixed: iterates `_defs` to resolve the component by name match first; first-visible is only a fallback.

### IndicatorContextAnalyzer — EvaluateTrendChange Incorrect
- [x] **Root cause:** `EvaluateTrendChange` returned `trend != Flat` on every bar. Fixed: `AlertEvaluator` tracks `_previousTrends` per alert+series key; fires only on actual direction flip.

### BarDetailService — Empty Span Passed to GetDetailFact
- [x] **Root cause:** `ReadOnlySpan<Ohlcv>.Empty` passed to `GetDetailFact`. Fixed: `AnnounceDetails` builds a lookback slice of up to 50 bars and passes it through the call chain.

### TODO.md — Duplicate "Platform Parity" Section
- [x] **Fixed:** Duplicate "Platform Parity" and "Performance" blocks removed from Phase 7. Single canonical copy retained.

### F8 — ToggleMuteSonification Removal
- [x] **Removed:** F8 was documented but never implemented in `SystemCommand` or `ShortcutManager`. References removed from `HelpModal.razor`, `CODEBASE_KNOWLEDGE_BASE.md`, and `keyboard.js` trapped-keys list. F8 now passes through to screen reader / OS.

### ScriptingService — Dead Code (No UI Entry Point)
- [x] **Annotated:** `ScriptingService.cs` class-level `<remarks>` comment added: `STUB: No UI entry point — Phase 10 scripting roadmap. Wire to ScriptEditorModal (Alt+,).`

---

## PHASE 10 — Comprehensive Enhancement Roadmap (from 2026-03-28 Session)

Items ordered by impact. Phases labeled 10-A through 10-G for implementation sequencing.

### First Wave — Already Implemented (2026-03-28)
- [x] **PropertiesModal persistence:** `Apply()` now calls `SeriesManager.PersistWorkspace()` so component appearance/audio changes survive restart.
- [x] **AlertOrchestrator warm-up guard:** `_initialized` flag prevents false-positive crossover alerts on first tick (cold start seeds `_previousValues` without firing).
- [x] **Custom Scripts infrastructure:** `OpenCustomScriptsEvent`, `SystemCommand.OpenCustomScripts`, `Alt+,` shortcut, `CustomScriptsModal.razor`, "Scripts" button in `IndicatorBar.razor`, `ICustomScriptService` interface.
- [x] **Data Export (CSV):** `IDataExportService` + `DataExportService` — viewport-scoped export including all visible indicator components. "Export CSV" button in Settings → General tab. JS `downloadCsv` helper in `keyboard.js`.
- [x] **Settings Profiles (Visual/Audio):** `VisualProfile` / `AudioProfile` classes (`SettingsProfiles.cs`). Export/Import buttons in Settings → General. `IWorkspaceLibraryService` extended with `ExportVisualProfile`, `ExportAudioProfile`, `ImportVisualProfile`, `ImportAudioProfile`.
- [x] **Keyboard tab in SettingsModal:** `ShortcutDisplayBinding` record, `IShortcutManager.GetAllBindings()`, shortcut table rendered in new "Keyboard" tab (General / Appearance / Keyboard / License / About).
- [x] **Zero-value live bar filter:** Binance, Bitstamp, Alpaca WebSocket callbacks now reject frames where all OHLCV values are zero and timestamp is epoch/zero. Dead-asset bars (genuinely zero) are unaffected (timestamp is still valid).
- [x] **BackfillManagerTests race fix:** Wait condition now requires both DB rows saved AND `BACKFILL_COMPLETE` event published before asserting — eliminates flakiness in parallel test runs.

---

### Phase 10-A — Foundation: Persistence, Display Types, Audio Texture ✅ Complete

#### A1: Mute/Volume Persistence ✅
- [x] **`ChartCommandManager`:** `PersistWorkspace()` called after `ToggleMuteAction` (component and series scope), `ToggleHideAction` (both scopes), and all `VolumeChangeEvent` dispatches (component/series/chart scopes).
- [x] **Result:** Mute state, hide state, and F5–F7 volume levels survive app restart.

#### A2: Per-Bar Coloring System ✅
- [x] **`ColorRule` record + `ColorCondition` enum (`Sdk/Models/ColorRule.cs`):** `AboveZero`, `BelowZero`, `Rising`, `Falling`, `AboveLevel`, `BelowLevel` + `string ColorHex` + `double Level`.
- [x] **`ComponentConfig.ColorRules: List<ColorRule>`** — empty by default; first matching rule overrides static `ColorHex` per bar.
- [x] **`StandardRenderers.ResolveBarColor()`** — evaluates rules against current and previous bar value; returns `null` when no rules (zero overhead on existing indicators).
- [x] **`StandardRenderers.RenderLine`** — per-bar colored segments when `ColorRules` non-empty.
- [x] **`StandardRenderers.RenderDirectionalBars`** — per-bar color from rules, falls back to directional logic when no rule matches.
- [x] **`PropertiesModal` Appearance tab:** "Color Rules" section — Add/Remove rules, condition dropdown, color picker per rule, optional Level field for threshold conditions. _(Completed 2026-04-01)_
- [x] **Persistence:** `ColorRules` serialized via `ComponentConfig` → `SeriesConfig` → `workspace.json`.

#### A3: New Display Types ✅
- [x] `ComponentDisplayType.Dot` — `RenderDot`: filled circle at value Y, radius = `Thickness * density`.
- [x] `ComponentDisplayType.Arrow` — `RenderArrow`: up/down triangle, direction from value sign.
- [x] `ComponentDisplayType.StepLine` — `RenderStepLine`: horizontal-then-vertical staircase.
- [x] `ComponentDisplayType.Cloud` — `RenderCloud`: filled polygon between `UpperComponentName` and `LowerComponentName` components; direction runs split into bullish/bearish fills.
- [x] `ComponentDisplayType.Gradient` — `RenderLine` (shared): alpha fill below line to pane zero.
- [x] `Area` display type fill fixed: was bare line; now alpha-60 fill + line on top.
- [x] `DataLayer` switch updated for all new types.

#### A4: IsAreaFill Verification ✅ (partial)
- [x] `Area` display type now renders correctly (fill added in `RenderLine`).
- [x] `Cloud` display type provides the band-fill use case for Bollinger/Keltner (assign `UpperComponentName`/`LowerComponentName` on the cloud component).
- [x] **Area fill sonification (band width → amplitude):** Closed 2026-04-24 as
  "won't do". Rationale: the line value already drives amplitude; a width-
  derived voice duplicates what `DeltaFromPrice` amplitude mapping on a
  derived series provides, and a third voice between two already-sonified
  boundaries breaks the audio=visual invariant. See `docs/CHANGES.md`
  2026-04-24 "Cloud sonification scoping" entry.

#### A5: AudioEngine Noise Oscillator ✅
- [x] `WaveformType.Noise` — pure pink noise via one-pole filter.
- [x] `ComponentConfig.NoiseAmount [0,1]` — blends noise into any waveform. Default 0 = zero overhead.
- [x] `OscillatorVoice.NoiseAmount` / `OscillatorVoice.NoiseState` — per-voice state; persists between samples for smooth texture.
- [x] `AudioEngine.SetVoice(... noiseAmount = 0f)` — optional param; all existing callers unaffected.
- [x] **PropertiesModal Audio tab NoiseAmount slider** — per-component range slider in Sonification tab. _(Completed 2026-04-01)_
- [x] **Bollinger Band noise preset** — closed 2026-04-24 as "won't do". The
  existing `LevelConfig.ZoneNoiseAmount` is the canonical "inside zone" audio
  cue. A band-presence noise layer would play ~95% of the time on Bollinger
  bands (price is almost always inside the band) and become inaudible to the
  user; the only information users need is band-exit, which existing boundary
  earcons + speech already announce.

---

### Phase 10-B — Sound Designer ✅

- [x] **`SoundPatch` model (`Sdk/Models`):** `Id`, `Name`, `Waveform`, `NoiseAmount`, `BaseFrequency`, `FreqMultiplier`, `Volume`, `EnvelopeType`, `DurationSeconds`, `Description`. Serializable. `Clone()` assigns fresh GUID.
- [x] **`ISoundPatchLibrary` (`Core`):** `GetPatches()`, `AddPatch`, `RemovePatch`, `UpdatePatch`, `GetPatch`, `ExportPatchJson`, `ImportPatchJson`, `EarconOverrides`, `SaveEarconOverrides`, `SavePatches`. Persists to `patches.json` + `earcon-settings.json`.
- [x] **`SoundDesignerModal.razor`:** `Alt+W` shortcut → `OpenSoundDesignerEvent`. Patch list (New/Clone/Delete), parameter editor (Waveform/Noise/Freq/Vol/Envelope), Preview, Save, Export JSON, earcon assignment table, Import JSON. ARIA-accessible throughout.
- [x] **Earcon custom waveforms:** `EarconService` injects `ISoundPatchLibrary`; `PlayWithPatchFallback()` checks earcon override before using hardcoded defaults. All eight earcons (Boundary, Info, Error, Success, Retry, NewBar, Connected, Disconnected) are assignable.
- [x] **`.atpkg` sharing format:** Zip containing `source.cs` + `manifest.json` (version, name, author, type). Export via `downloadBlob` JS (binary zip); import via `readFileAsBase64` file picker. Legacy JSON paste-import retained for backward compat. _(Completed 2026-04-01)_
- [x] **Patch persistence:** `ComponentConfig.SoundPatchId` (nullable string) added in Phase 10-A. `ISoundPatchLibrary` resolves at render-time; fallback to component fields if patch not found.

---

### Phase 10-C — Completions & Polish ✅

- [x] **BarDetailService full coverage:** Rich `GetDetailFact` narratives added for Volume (10-bar avg, surge/dry-up, building/declining trend), RSI (divergence hint), MACD (expanding/contracting histogram, zero-line approach), Bollinger Bands (live squeeze/expansion from 20-bar avg width, corrected %B), EMA/SMA/WMA (price-to-MA distance %, per-bar slope %), CCI (zone + direction), ADX (strength label + DI direction). CoreIndicatorProvider handles VOLUME; SkenderIndicatorProvider handles the rest.
- [x] **HelpModal keyboard reference audit:** `HelpModal.razor` now injects `IShortcutManager`. Added live "All Keyboard Shortcuts" section auto-generated from `GetAllBindings()`. Missing shortcuts (Alt+D/J/W/,, Alt+C/L, Ctrl+Shift+D, Shift+F12) added to UI & Settings table. `FormatCommandName()` helper inserts spaces in PascalCase command names.
- [x] **iOS / iPadOS hardware keyboard:** `Platforms/iOS/KeyboardPageHandler.cs` added — mirrors Mac Catalyst `PressesBegan` pattern. Registered in `MauiProgram.cs` under `#if IOS`.
- [x] **Settings import from file:** `readFileAsText` JS interop — `ImportVisualProfileAsync` and `ImportAudioProfileAsync` open native file picker and pass JSON to `IWorkspaceLibraryService`. _(Completed 2026-04-01)_
- [x] **Keyboard remapping UI:** Settings → Keyboard tab shows interactive table with Rebind button per command. `captureNextKey` JS captures next key combo in capture phase (before chart handler). `[JSInvokable] OnKeyCaptured` calls `IShortcutManager.UpdateBinding` + persists immediately. _(Completed 2026-04-01)_
- [x] **Coinbase / Polygon zero-value filter:** Coinbase: price ≤ 0 skipped. Polygon: all-zero OHLC frame skipped. Same pattern as Binance/Bitstamp/Alpaca.
- [x] **`StrategyIndicatorCache`:** `IStrategyIndicatorCache` + `StrategyIndicatorCache` (Core). Caches SMA/EMA/RSI/BollingerBands by `(type, period, data.Count)`. `StrategyEngine` injects it and calls `Invalidate` before each `OnBar` cycle. Registered as singleton.

---

### Phase 10-D — Custom Indicator Platform (Roslyn) ✅

- [x] **`ICustomIndicator` interface (`Sdk`):** `Id`, `DisplayName`, `ComponentNames[]`, `DisplayTypes[]`, `DefaultParameters`, `Calculate(ReadOnlySpan<Ohlcv>, parameters)` returning `double[][]`.
- [x] **`RoslynScriptingService`:** `CSharpCompilation` emit to in-memory DLL. Isolated `AssemblyLoadContext` per script (collectible). Sandbox: Sdk + System.Runtime.* only. `UnloadScript(id)` for cleanup. `ExecuteSimpleAsync` path retained for expression scripts.
- [x] **`CustomScriptsModal.razor` full implementation:** Script list (New/Delete), monospace code editor with ICustomIndicator template placeholder, Compile button → error output, Add to Chart button on success, Export .atpkg download.
- [x] **`.atpkg` format:** JSON payload `{Version, Name, Author, Code}`. Export via download JS interop; import via paste-and-parse in the Import section.
- [x] **`AddCustomIndicator`:** `ISeriesManagementService.AddCustomIndicator(indicator, state)` bridges compiled indicator to the chart's `RegisterSeries` pipeline.
- [ ] **`ICustomScriptService.RunScriptAsync` full pipeline:** Compiled `ICustomIndicator.Calculate` routed through `IndicatorOrchestrator` → results stored in `SeriesDataBuffer`. Currently registers via `RegisterSeries` but doesn't yet wire `Calculate` into the indicator recalc pipeline. Deferred to Phase 10-D.2.

---

### Phase 10-E — PineScript Transpilation ✅

Three-tier pattern-based transpiler (no ANTLR — hand-written regex/pattern approach).

#### Tier 1 — Core Mapping ✅
- [x] **Pattern-based transpiler:** `PineTranspiler` in `Core/PineScript/`. Regex patterns for all common Pine constructs.
- [x] **ta.* mapping:** `ta.sma/ema/rsi/macd/bb/atr/stoch/crossover/crossunder/highest/lowest/stdev` → C# helper arrays.
- [x] **plot() / plotshape():** Component registration. `plotshape` → `ComponentDisplayType.Dot`.
- [x] **Roslyn compile step:** Generated C# fed into `RoslynScriptingService.CompileIndicatorAsync`. Same ICustomIndicator sandbox.
- [x] **Static helpers embedded:** All ta.* equivalents as private static methods in the generated class.

#### Tier 2 — Extended Patterns ✅
- [x] **`var` / `varip`:** Stripped to plain variable declaration.
- [x] **`na` / `nz()` mapping:** `na` → `double.NaN`; `nz(x, d)` → `NzHelper`.
- [~] **Conditional color rules:** `color.new(...)` / ternary color expressions → ColorRule generation. **Detector shipped 2026-04-24** — every `color.new()` call site now emits a warning naming the feature so users know the dynamic coloring fell back to the component default. Mapping to `ColorRule` itself still deferred to the eventual ICustomStrategy host contract.

#### Tier 3 — Stubs ✅
- [x] **`request.security()`:** Replaced with `NanArr(n)` + warning in TranspileResult.Warnings.
- [~] **`line.new()` / `label.new()`:** **Detector shipped 2026-04-24** — every call site emits a `TranspileResult.Warnings` entry naming the feature and pointing to `docs/TODO.md` for the mapping path. Wiring to `DrawingService` itself still requires the `ICustomStrategy` host contract (Phase 10-D.2).
- [~] **`strategy.*` functions:** **Detector shipped 2026-04-24** — `strategy.entry`/`strategy.exit`/`strategy.close` each emit a warning per call site pointing users to the StrategyComposer (BuildSetupTab) for trading logic. Mapping to `TradeSignal` still requires the `ICustomStrategy` host contract.

---

### Phase 10-F — Strategy Platform Extension ✅ (partial)

- [x] **Custom C# Strategy tab:** `StrategyModal.razor` now has a tabbed layout (Add Strategy / Active / Backtest / Custom Script). Custom Script tab: textarea editor, C# template, execution mode, Compile & Add button.
- [x] **`IRoslynScriptingService.CompileStrategyAsync`:** Compiles user C# into `ITradingStrategy` via Roslyn, referencing both `AccessibleTrader.Sdk` and `AccessibleTrader.Core` so `BaseStrategy` is available. Result `CompileStrategyResult(Success, Strategy, Errors[])`. Errors shown inline in editor pane. On success: strategy added to `StrategyEngine`, tab switches to Active.
- [x] **`ConfigurableStrategy` class (`Core/Trading`):** shipped — see
  `AccessibleTrader.Core/Strategies/ConfigurableStrategy.cs`. Serializable
  `StrategySpec` + condition tree; persists via `JsonStrategyLibrary` +
  `strategies.json`.
- [x] **Strategy condition builder UI (StrategyModal):** shipped — see
  `BuildSetupTab.razor` (split into `ConditionTreeEditor` /
  `RiskPlanEditor` / `SummaryExport` in the 2026-04-24 Tier 3 sweep).
- [x] **DLL plugin strategy:** shipped 2026-04-24 Phase 10-F(a) — see
  `IStrategyPlugin` SDK contract + `StrategyPluginRegistry` + fixture
  plugin + 7 loader tests.
- [x] **`StrategyIndicatorCache` integration:** shipped 2026-04-24
  Phase 10-F(b) — SDK bridge `IPluginStrategyIndicatorCache` +
  `PluginHostServices.IndicatorCache` + per-bar `Invalidate` in the
  backtester.
- [x] **`IStrategyRegistry.GetCatalog()` extension:** shipped 2026-04-24
  Phase 10-F(c) — unified `StrategyRegistry` merges
  `IStrategyLibrary.All` + `IStrategyPluginRegistry.Templates` with
  spec-wins-on-ID-collision semantics.

### Phase 10-F2 — Accessible Cipher B ✅ Complete

- [x] **`CipherBProvider` (`Core/Services/Indicators/CipherBProvider.cs`):** Full native C# Market Cipher B replica. Wave Trend (WT1/WT2/WT Fill cloud), MC Money Flow histogram, Blue/Red/Gold signal dots, 4-type divergence detection (regular + hidden bull/bear dots). Parameters: WT1Period, WT2Period, MFPeriod, OBLevel, RSIPeriod, RSIOSLevel, PivotBars.
- [x] **Registered** in `ServiceCollectionExtensions.AddIndicatorPipeline()`.
- [x] **Reference levels:** ±60 (extreme OB/OS dotted), ±53 (OB/OS dashed), 0 (zero line). Injected via `IndicatorReferenceLevels`.
- [x] **StylingService:** Per-component color map — WT1 #00C8FF (blue), WT2 #7FDBFF, WT Fill cloud bullish/bearish, MF green/red, signal dots with distinct colors.
- [x] **PaneAssignmentService:** Category `Multi-Signal`, pane `Pane_CIPHER_B`.
- [x] **Cloud component metadata:** `IndicatorComponentMetadata.UpperComponentName`/`LowerComponentName` added; `IndicatorModelFactory` propagates to `ComponentConfig`. WT Fill links WT1 and WT2.
- [x] **OB/OS noise texturing:** `NavigationSonifier` detects Overbought/Oversold Level components and blends 0.20f noise when value exceeds threshold. `IAudioDriver.SetVoice` gains `noiseAmount` parameter (was only in AudioEngine; now propagated through BlazorAudioDriver).
- [x] **MFI/Chaikin styling fixed:** MFI → `Histogram` display with `ColorBaseline=50`. Chaikin OSC variants → `Histogram` with zero-crossing.
- [x] **`ComponentConfig.ColorBaseline`:** Used by `RenderDirectionalBars` as the green/red split threshold. Persisted in `Clone()` and `IndicatorModelFactory`.
- [x] **`CustomIndicatorRegistry`:** Thread-safe runtime lookup for Roslyn/Pine compiled `ICustomIndicator` instances. `IndicatorEngine` routes to registry before `IIndicatorService`.

---

### Phase 10-G — Indicator Architecture Improvements

- [x] **Self-describing indicator color/style metadata:** Added optional `DefaultColorHex`, `DefaultColorHexSecondary`, `DefaultThickness`, `ColorBaseline`, `DefaultDashStyle`, `DefaultColorSource` + audio hints (`DefaultWaveform`, `DefaultEnvelopeType`, `DefaultNoiseAmount`, `DefaultAmplitudeMapping`, `DefaultPitchMapping`, `DefaultBaseFrequency`) to `IndicatorComponentMetadata`. `IndicatorModelFactory.CreateComponentConfigFromMeta` applies metadata hints first, falls through to `IStylingService` role-based defaults. Migrated `CipherBProvider`, `SpiderLinesProvider`, `EmaFillProvider`, `SkenderIndicatorProvider` (lookup tables for RSI/MFI/Stoch/etc.). `StylingService` is now purely role/type-based — no indicator names.

- [x] **Extended shape vocabulary for component display types:** Added `ComponentDisplayType` values: `TriangleUp`, `TriangleDown` (direction-coded), `Diamond` (divergence), `Square` (discrete event), `Cross` (invalidation). Each has a `StandardRenderers.Render*` method, a `DataLayer` dispatch case, a Ping-envelope sonification profile in `SonificationProfileProvider`, and TTS-friendly strings in `SpeechFormatter.FriendlyTypeName`. `CipherBProvider` signal dots remain `Dot` (required for Ctrl+Left/Right sparse navigation in `CommandDispatcher`); new shapes are available for future providers that don't require dot-based navigation.

- [x] **Oscillator sonification rule:** `SonificationProfileProvider` oscillator/ZeroArea profiles use `AboveWaveform = "triangle"`, `BelowWaveform = "sine"` (rule: triangle above zero, sine below). `IndicatorModelFactory.CreateComponentConfigFromMeta` sets `ReferenceLevel = 0` for Oscillator/ZeroArea types so `DefaultSonificationStrategy` triggers the above/below waveform switch. Dynamic OB/OS noise (0.20f) computed in `CreateAudioPoint` by scanning Level siblings — playback now matches navigation noise behaviour. `AudioPoint` carries `NoiseAmount`; `AudioSequencer` passes it through to `SetVoice`.
- [x] **Indicator preferences service:** `IIndicatorPreferencesService` + `IndicatorPreferencesService` — per-indicator JSON prefs at `%LOCALAPPDATA%\AccessibleTrader\IndicatorPrefs\`. `IndicatorModelFactory.CreateSeriesFromMetadata` applies a 3-layer merge (metadata → workspace state-only → preferences). PropertiesModal "Save as Defaults" button persists current appearance + sonification as preferences. This permanently fixes the "stale workspace silences new audio defaults" problem.
- [x] **Ctrl+Left/Right sparse navigation generalised:** `CommandDispatcher` now recognises all marker display types (`Dot`, `ZeroDot`, `Arrow`, `Diamond`, `TriangleUp`, `TriangleDown`, `Square`, `Cross`) for sparse NaN-scan navigation — not just `Dot`.
- [x] **Dot/Arrow earcon profile:** `SonificationProfileProvider` has explicit `Dot`/`Arrow` case → Ping envelope, `PitchMapping.Direction` 660/220 Hz. Previously fell through to Sustain default.

- [x] **Indicator sub-panes:** `IndicatorComponentMetadata.SubPaneName` + `SubPaneHeightRatio` declare sub-pane membership. `ComponentConfig` carries these through from `IndicatorModelFactory`. `RenderContext.SubPaneFilter` controls which components each pass renders. `ChartRenderer.RenderPane` does multi-pass rendering: main area (top) + sub-pane strips (bottom, clamped 5–40% each). `ViewportRangeCalculator` accumulates per-sub-pane ranges under composite keys (`"PaneName/SubPaneName"`) — also fixes early-exit bug where only the first series per pane was computed. `DataLayer` sub-pane filter gate skips wrong-pass components; cloud fills and levels are main-area-only. `CipherBProvider` Money Flow Wave and Money Flow Dot now declare `SubPaneName = "MF", SubPaneHeightRatio = 0.22f`.
- [x] **Sub-pane follow-up — remove normalization:** ±35 scaling removed from `LaguerreRsi` / `ComputeStochRsi` in `CipherBProvider.Calculate`; ±30 MF normalization removed — raw values now fill their sub-pane naturally (2026-03-30).
- [x] **Sub-pane follow-up — drag-resize + persistence:** Sub-pane height ratio exposed as drag handle (ResizePaneAction pattern). Sub-pane ratios persisted in `WorkspaceConfiguration` using composite key scheme (2026-03-30).

---

### Phase 10-H — Alerts, Multi-Workspace, Drawing Completions

- [x] **Alert delivery channels (moved from Phase 10-G):** service-layer
  shipped 2026-04-24. `IAlertChannel` SDK contract, `EmailAlertChannel`
  (SMTP), `TelegramAlertChannel` (Bot API), `AlertDeliveryService` fan-out
  via `AlertFiredEvent`. Config lives under `alerts.email.*` /
  `alerts.telegram.*` setting keys and loads per-send via
  `ISettingsManager`. Settings-modal **"Alerts" tab shipped 2026-04-24**
  (same day) with per-channel "Send test" buttons that resolve the live
  `IAlertChannel` from DI.
- [x] **Multi-workspace tabs:** `WorkspaceState` extended with `TabSnapshots` + `ActiveTabIndex` + `TabCount`. `TabSnapshot` record freezes per-tab fields. `AddTabAction`, `CloseTabAction`, `SwitchTabAction`, `ToggleNarrationAction` reducer cases in `WorkspaceStore`. `TabBar.razor` renders between Toolbar and chart; hidden when only one tab open. Keyboard: `Ctrl+T` (new), `Ctrl+W` (close), `Ctrl+Tab` / `Ctrl+Shift+Tab` (cycle). `TabSwitchedEvent` published for audio engine stop. TTS announces tab label on switch. 14 tests added (`MultiTabTests.cs`). Build: 0 errors. Tests: 176/176. (2026-04-01)
- [x] **Drawing tool completions:** Audited all 16 registered drawing tools. All anchor counts and sequencing correct. One bug fixed: `GannBoxCalculator` price levels were spanning the entire chart instead of being bounded within the anchor date range — now fills NaN outside [i1,i2] and adds time subdivision points at Gann ratios. AVWAP confirmed correct (recalculated from scratch on each `Calculate()` call, so live bars work naturally). Build: 0 errors. Tests: 176/176. (2026-04-01)
- [x] **`AutoNarrationService`:** `SeriesConfig.IsAutoNarrated` + `ChartSeries.IsAutoNarrated` delegation. `ToggleNarrationAction` in store. `Ctrl+Shift+N` toggles narration for focused series. `AutoNarrationService` subscribes to `IndicatorUpdatedEvent` + `StateStream`; detects new marker signals (non-NaN Dot/Arrow/Diamond/etc.) on closed bars and oscillator zone transitions; announces via `ISpeechFeedbackRouter` (non-interrupting). Seeding prevents retroactive announcements when narration is enabled. "narrating" appended to series state suffix in `NavigationFeedbackManager`. `Ctrl+Shift+D` (existing `BarDetailService`) already reads non-NaN column values for focused series. 10 tests added (`AutoNarrationTests.cs`). Build: 0 errors. Tests: 162/162. (2026-04-01)
- [x] **Three-tier level crossing earcons:** Shipped via `LevelCrossingMonitor` (`AccessibleTrader.Core/Services/Audio/LevelCrossingMonitor.cs`), wired in `SonificationManager` and DI. Tier 1 = approach ping (5% band, amplitude scales with proximity), Tier 2 = crossing (existing `PlayBoundary` path), Tier 3 = single one-shot confirmation tone after `SustainedBarsThreshold` consecutive bars beyond. **Note:** the original spec called for a "looping low-amp background tone" for Tier 3, which would have stepped on the existing OB/OS noise-texturing in `AudioZoneHelper.ComputeZoneNoise`. Implementation deliberately uses a one-shot tone instead so the passive zone-noise texture remains the persistent "still in zone" cue while the Tier 3 tone cleanly marks the threshold-crossing event.
- [x] **Live AI Technical Analyst:** `IAIAnalystService` + `ILLMProvider` plugin contract in Sdk. Providers: `ClaudeProvider` (claude-sonnet-4-6), `OpenAIProvider` (gpt-4o), `OllamaProvider` (local llama3). Priority: Claude → OpenAI → Ollama (first configured key wins; Ollama needs no key). `Ctrl+Alt+Shift+A` → `AIAnalystModal.razor` (auto-triggers on open). Announces "no API key configured" if none found. Builds OHLCV prompt (50 viewport bars) + indicator summary + offscreen PNG snapshot via `SKSurface`. Speech-reads result. Build: 0 errors. Tests: 176/176. (2026-04-01)
- [~] **NAudio.Wasapi removal:** tracked in Platform Parity section.

---

## UPCOMING — Sonification & Audio Engine Improvements

### Phase B — Audio Engine: Bell Synthesis Foundation ✅ Complete (2026-03-31)
- [x] Configurable decay length in ComponentConfig (DecayMs field, nullable) — comp.DecayMs overrides patch default; both applied in AudioSequencer and NavigationSonifier
- [x] SoundPatchId wired to SoundPatchRegistry (built-in patches: sine_bell, triangle_bell, crystal_bell, detuned_pair_bell, gradient_blend) — ISoundPatchRegistry singleton registered in DI
- [x] Bell harmonic content (HarmonicAmount/HarmonicFreqMultiplier fields on SoundPatch record — consumed by AudioSequencer for future AudioEngine integration)
- [x] PlaybackLayer enum on ComponentConfig (Background=60%, Midground=80%, Foreground=100%) — AudioSequencer applies LayerVolume() scale in both StartPlaybackAsync and StartMultiSeriesPlaybackAsync
- [x] Detuned paired bell: AudioSequencer fires two voice commands with configurable ms offset (DetunedOffsetMs via Task.Delay); NavigationSonifier uses Slot 1 for detuned voice

### Phase C — Cipher A: Self-Describing Metadata + Sonification ✅ Complete (2026-03-31)
- [x] All 8 Cipher A components get Default* metadata fields (colors, thickness, SoundPatchId, DecayMs, frequency)
- [x] Gradient blend patch wired for WT Momentum dot (SoundPatchId = "gradient_blend")
- [x] Buy/Sell signals: sine_bell, 880/220 Hz, 380ms decay
- [x] Divergence diamonds: triangle_bell, 660/330 Hz, 280ms decay
- [x] Blood Diamond: triangle_bell, 165 Hz, 500ms decay
- [x] Manipulation/Exhaustion: detuned_pair_bell, 320ms decay
- [x] Component-level contextual speech: UsesGradientSpeech on WT Momentum; SpeechFormatter reads companion _color array and maps WT1 oscillator value to qualitative momentum language (strong/moderate bullish/bearish/neutral). IndicatorComponentMetadata.DefaultSoundPatchId and UsesGradientSpeech fields added; ComponentConfig.UsesGradientSpeech propagated by IndicatorModelFactory.

### Phase D — Cipher B: Sonification Redesign ✅
- [x] Anchor waves: Background layer, triangle (WT1 Anchor) / sine (WT2 Anchor), AmplitudeMapping.None
- [x] WT1: Midground, triangle above / sawtooth below zero, Value pitch, AmplitudeMapping.None
- [x] WT2: Midground, smooth sine throughout, Value pitch, AmplitudeMapping.None
- [x] Trigger Wave: Midground, triangle, DefaultFreqMultiplier=1.3 for snappier "ahead" character
- [x] Money Flow Wave: Midground, sine both sides, 0.08 noise preserved
- [x] MF dot (ZeroDot): sine_bell, 150ms, Direction pitch 600/250 Hz, Midground
- [x] MF Signal Large: sine_bell, 350ms, Direction pitch, Foreground
- [x] MF Signal Small: sine_bell, 160ms, Direction pitch, Foreground
- [x] RSI~/Stoch %K/%D/VWAP~: Background layer, triangle waveform (contextual subdued)
- [x] Oversold Crossover (Blue): sine_bell, 840 Hz, 350ms, Foreground
- [x] Overbought Crossover (Red): sine_bell, 210 Hz, 350ms, Foreground
- [x] Triple Confluence Buy (Gold): dual_tone_bell (440+660 Hz simultaneous chord), 500ms, Foreground
- [x] Bullish Divergence: triangle_bell, 620 Hz, 230ms, Foreground
- [x] Bearish Divergence: triangle_bell, 310 Hz, 230ms, Foreground
- [x] Hidden Bull Continuation: triangle_bell, 520 Hz, 180ms, Foreground
- [x] Hidden Bear Continuation: triangle_bell, 360 Hz, 180ms, Foreground
- [x] Added dual_tone_bell patch in SoundPatchRegistry (220 Hz apart, simultaneous, 500ms decay)
- [x] Added DefaultAboveWaveform, DefaultBelowWaveform, DefaultBullishFrequency, DefaultBearishFrequency, DefaultFreqMultiplier to IndicatorComponentMetadata
- [x] Wired new metadata fields in IndicatorModelFactory.CreateComponentConfigFromMeta

### Phase E — Cipher SR: Sonification Design ✅
- [x] Resistance/Support pivot dots: crystal_bell, 700/330 Hz, 220ms decay, Foreground layer
- [x] Zone lines: contextual hum in NavigationFeedbackManager when price within zone (0.5% tolerance, slot 2, 100ms sine)
- [x] IsZoneLine on ComponentConfig + DefaultIsZoneLine on IndicatorComponentMetadata; wired in IndicatorModelFactory
- [x] INavigationSonifier.PlayZoneProximity(float frequency, bool isResistance) added and implemented

### Phase F — Cluster/Shapes-as-Ticks Navigation
- [x] NavigationSonifier fires N ticks (100ms apart) when bar has N marker shapes
- [x] Significance ordering: structural (SR/divergence) first, action (crossover) second

### Phase G — Speech: Contextual Component Descriptions
- [x] `SignalSpeechTemplate` on `ComponentConfig` + `DefaultSignalSpeechTemplate` on `IndicatorComponentMetadata`
- [x] Cipher A gradient dot qualitative range-aware speech (UsesGradientSpeech, from Phase C)
- [x] Cipher A Buy/Sell/Divergence/BloodDiamond/Manipulation/Exhaustion signal speech templates
- [x] Cipher B Triple Confluence, Oversold/Overbought Crossover, divergence/hidden continuation speech templates
- [x] Cipher B MF Signal Large/Small speech templates
- [x] Cipher SR Resistance/Support pivot dot speech includes zone level value ("Resistance pivot at {price}")
- [x] Multi-signal bar speech sequences in same order as audio ticks (Component context, "Also: ..." prefix)
- [x] SR zone proximity speech: "Near resistance/support at {level}" on zone hum fire

### Phase H — Cloud Sonification Architecture (COMPLETE 2026-03-31)
- [x] CloudFillConfig gains optional CloudSonificationConfig (frequencies, patch, amplitude mode)
- [x] AudioSequencer cloud-aware pass in multi-series playback
- [x] EMA Fill cloud sonification declared in metadata
- [x] CipherB WT Fill cloud sonification declared in metadata
- [x] Cloud voice in dedicated slot range (slots 64-79, separate from component slots 32-63)

### Phase I — Drawing Tools: Coordinate Entry Mode ✓ (2026-03-31)
- [x] Keyboard-only anchor placement mode (navigate to point, Enter to set)
- [x] TTS announces price + timestamp during coordinate entry navigation
- [x] Anchor 1 confirmed → navigation speech includes change-from-anchor delta
- [x] Escape cancels CE mode with speech feedback
- ~~Live Preview for trendline dragging~~ (removed — not planned)

### Phase J — Ctrl+Left/Right Crossing Navigation Redesign
- [x] Context-aware crossing: zero-line for MACD/Momentum, OB/OS for RSI/MFI, band for Bollinger, MA-cross for EMA/SMA

### Phase K — Ichimoku Indicator
- [x] Full Ichimoku implementation (Tenkan, Kijun, Chikou, Senkou A/B)
- [x] Dual Kumo cloud fills (Senkou A/B)
- [x] Future cloud projection (26 periods ahead) handled gracefully in navigation

### Phase L — Test Coverage Expansion
- [x] SoundPatchRegistryTests (7): built-in patch presence, custom registration/replacement, detuned/gradient properties
- [x] PlaybackLayerTests (4): volume multipliers, default layer, factory propagation, clone preservation
- [x] DecayMsTests (4): default null, factory propagation, clone with/without value
- [x] CipherAMetadataTests (13): all 8 components verified for patch ID, frequency, decay, layer, gradient speech
- [x] CipherBMetadataTests (10): Triple Confluence dual-tone, crossover frequencies, divergence patches, Background anchors
- [x] CipherSrMetadataTests (7): crystal bell, zone line flag, factory propagation to ComponentConfig
- [x] IchimokuProviderTests (12): component count, cloud fill, Tenkan/Chikou/Senkou calculations, GetDetailFact, stability window
- [x] CloudSonificationTests (8): backward compat null, Clone preserves Sonification, EMA/Ichimoku/CipherB frequencies
- [x] CrossingNavigationTests (3): zero-line crossing, OB threshold entry, no-crossing returns -1

---

## PHASE 12 — Strategy Research: System Upgrades for Score-Based Confluence (planned, 2026-04-07)

The v2-v6 strategy iteration sprint produced one walked-forward stable strategy (v2) and four failures (v3/v4/v5/v6). The cross-strategy decay pattern + indicator code audit revealed that the system has substantial unused capability and several bugs that prevent the next class of strategies from being built. Phase 12 ships the infrastructure required for v7 (multi-source score-based confluence) and addresses the documented gaps.

**Reference:** `project_strategy_research_2026_04_07.md` in memory + the CHANGES.md entry for the same date document the empirical results that motivate this phase.

### Required system upgrades (in priority order — DO BEFORE building v7)

- [x] **Score-based root operator** — shipped. Evaluator landed in earlier
  session; BuildSetupTab UI (dropdown + threshold input with max-score
  hint) shipped 2026-04-24.

- [x] **Pivot strength filter on level operators** — shipped.
  `ConditionLeaf.MinLevelStrength` + `ConditionEvaluator.FilterByStrength`
  already present; BuildSetupTab UI input shipped 2026-04-24. Touch-count
  filter still deferred — would need a new operator variant rather than
  a parameter, and is only a marginal win over the strength gate.

- [x] **HTF future-leak bug fix in `EvaluateHtfIndicatorLeaf`** — shipped. `ConditionEvaluator.HtfLastClosedIndexExclusive` clips HTF reads via strict-less-than binary search on `history[^1].Date`, and both `EvaluateHtfIndicatorLeaf` and `EvaluateHtfPriceLeaf` honour the exclusive end. Tests: `ConditionEvaluatorHtfTests.cs` (10) including perfect-alignment + before-all + after-all edges.

- [x] **VPVR backtest replay end-to-end verification** — shipped
  2026-04-24. `VpvrBacktestReplayTests` (4 tests) pins the chain:
  cache IsActive/Set/Get/Clear semantics, provider-reads-cache-when-
  active, provider-falls-through-when-inactive, no-profile-series empty
  case. Any future refactor that breaks the cache preference will trip
  these tests.

- [x] **Rolling-window score aggregation** — already shipped as typed
  operator variants (`GreaterThanWithin`, `LessThanWithin`,
  `BetweenWithin`, `PercentileBelow`, `PercentileAbove`). The 2026-04-24
  sweep extended the BuildSetupTab `NeedsWithinN` gate to surface the
  Within-N input for every operator that consumes it.

- [x] **Expose Cipher A WT Momentum gradient as a queryable signal** —
  shipped 2026-04-24 as `CIPHER_A.WT Momentum Gradient` hidden Line
  component. Normalised 0.0..1.0 derivation in `CipherAProvider.Calculate`
  (raw WT1 clamped to ±OBLevel then linear-mapped). Strategies gate via
  the standard leaf operators (`GreaterThan 0.7 = strong overbought`).

### v7 strategy build (AFTER infrastructure is in place)

- [ ] **Build v7 — Score-based multi-source confluence**
  - Single condition tree using the Score root operator
  - Leaves combining: Cipher A/B momentum pulses (score 1.0 each, FiredWithin 5), Cipher A/B divergences (score 2.0, FiredWithin 7), Cipher B gold cross (score 2.0), Cipher SR support with `MinLevelStrength=0.7` (score 1.5), VPVR value area / POC / LVN wick (score 1.0-1.5), HTF Cipher B uptrend (score 1.5, requires HTF bug fix)
  - Threshold ~4.0 — fires when ≥4 points of evidence align
  - Same risk plan as v2 (ATR(14)×2 stop, 1.5R/3R ladder, breakeven after TP1, 0.5% risk) for clean comparison
  - Required indicators on chart: Cipher A, Cipher B, Cipher SR, VPVR. Cipher C optional as additional weighted contributor.
  - Expected on BTC 1d: 35-55 trades over 9 years, WR 60-70%, Avg R 0.50-0.80, PF 2.0-3.0
  - Validation: full backtest first, then walk-forward halves, then ETH 1d cross-symbol

### Bigger systemic improvements (future, lower priority)

- [ ] **Walk-forward parameter optimization (expanding window)** — re-tune Cipher periods every N months using only data prior to that point. Multi-week project. Real-but-careful curve-fit avoidance.
- [ ] **Regime classifier with regime-conditional strategy routing** — classify each bar as trending/ranging/volatile via ADX + ATR percentile + autocorrelation features, route to different strategies. Multi-week project.
- [ ] **Indicator-on-HTF computation** (deferred from Session B) — sync `IIndicatorRunner` so HTF leaves can reference indicators not just price. Currently HTF leaves fall through to active-TF data.
- [ ] **Expand SignalCatalog companion-array support** — generic mechanism for indicators to expose multiple value streams per component (not just `_color` and `_touches`)

### Strategies that should be deleted from the library after v7 lands

- [x] **v4 r1, v6, v3 stale the original cipher author seeds** — verified absent from
  `BuiltInStrategySeeds.cs` (already deleted in an earlier cleanup pass).
  Pruned 2026-04-27.

### Open question (no work item, just documented)

The walk-forward decay pattern across v2-v6 (better in first half than second half across ALL strategies) suggests modern BTC daily is structurally harder to trade with retail momentum signals than early BTC was. Three plausible causes (in order): public signal decay from millions of the original cipher author viewers, market structure maturation (institutional flow + ETF + perpetuals), and inflated early-BTC bull run dynamics. **No code change can fix this** — it's a property of the asset and the indicator family. The honest realistic upper bound on Cipher-based strategies on BTC 1d in current conditions is approximately v2's second-half walk-forward result: PF ~1.5 net of costs, Avg R ~0.4, win rate ~56%. v7 might improve this by ~0.2-0.4 R if the orthogonal-source confluence hypothesis is right; it won't transform it.

---

## COMPLETED — Earlier Phases (Pre-2026-03-26 Session)

- [x] **Universal Skender Discovery:** Robust `IQuote` generic argument detection.
- [x] **Drawing Tool Suite:** Risk/Reward, AVWAP, Pitchfork, Gann Fan/Box, Measure Tool, all 15+ registered.
- [x] **Indicator Categorization:** Intelligent lookup table — Trend/Momentum/Volatility/Volume/Profiles.
- [x] **Zero-Allocation Data Pipeline:** `ComponentConfig.Data` → `double[]` with `double.NaN`.
- [x] **Custom Audio Engine:** Replaced NAudio synthesis with pure C# DSP engine.
- [x] **Platform Migration:** WinUI 3 → .NET 10 MAUI Blazor Hybrid.
- [x] **Professional Drawing Suite:** Risk/Reward, AVWAP, Pitchfork, Gann Fan/Box, Measure Tool.
- [x] **Archetype Injection:** 0/30/70 reference levels auto-injected into oscillator indicators.
- [x] **State Machine Implementation:** DataOrchestrator lifecycle state machine.
- [x] **Exclusive Focus Sonification:** Eliminated sawtooth leakage during distribution navigation.
- [x] **ProfileBinClassifier:** Node classification shared by sonification and speech.
- [x] **Profile/Heatmap Sonification:** Node-type-based pitch system, heatmap sawtooth waveform.
- [x] **Double-announcement fix:** Navigation feedback exclusively via `FeedbackRequestEvent`.
- [x] **F2/F3/F4/F5-F7 wiring:** All function key commands correctly dispatched and announced.
- [x] **NavKeyReleasedEvent chain:** Arrow keyup stops navigation voice immediately.
- [x] **Modal z-index:** `.modal-overlay` at z-index 9999.
- [x] **PrependOlderDataAsync notify:** `NotifyDataUpdate` + `SetDataStatusAction(Ready)` after backfill.
- [x] **SonifyHeatmap null safety:** Guarded `SelectMany` against null inner lists.
- [x] **Series nav shortcuts corrected:** Page Up/Down = series; Up/Down arrows = component.
- [x] **All 21 tests passing** (as of 2026-03-25 sprint).
