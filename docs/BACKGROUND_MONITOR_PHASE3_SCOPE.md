# Background monitor Phase 3 — new bars, and the alerts nothing can watch

**Status: SCOPE. Nothing in section 3 is built.** Written 2026-09-08 against `9a0d5940`,
suite 7,223 + 4 (the measurements in section 1 are committed tests, not readings).

Phase 3 is the last phase of `docs/BACKGROUND_MONITOR_SCOPE.md`. Phases 0, 1 and 2 are on
`main` and unreleased. That document's Phase 3 section is three bullets written from a read of
the code; this document replaces it, because two of the three bullets turned out to describe
the problem wrongly.

**The goal, restated:** *"keep the terminal running, close the browser and still receive
notifications … order fills, order closes, alerts, new bar notifications."* Order events landed
in Phase 2. What remains is **new bars** and **the alerts `BackgroundWatchability` refuses**.

---

## 1. Findings — measured, not read

Every claim in this section is pinned by a test that passes today. The two files are
`AccessibleTrader.Tests/NewBarReachTests.cs` and the Phase 3 section at the end of
`AccessibleTrader.Tests/WebHost/HeadlessSessionTests.cs`. They are written to go RED when
Phase 3 lands, which is how the work will be known to have happened.

### F1. New bars reach ONE chart — the focused one — and that is true with the browser OPEN

`docs/BACKGROUND_MONITOR_SCOPE.md`'s table says new bars work with the browser open (✅) and
fail only with it closed. That is a claim about a subscriber. Nothing had measured the
publisher, and the publisher is narrower than the table:

- There is exactly **one** publisher of `NewBarEvent` in the whole codebase —
  `WorkspaceStore.cs:231`, gated on `isLiveDataAction`, i.e. an `UpdateDataAction` with
  `IsInitialLoad: false` (`WorkspaceStore.cs:116`).
- The only thing that dispatches one from a live tick is `DataManager.OnFocusedFeedUpdated`
  (`DataManager.cs:79-107`), subscribed to `IMarketFeedHub.FocusedFeedUpdated`.
- `MarketFeedHub.OnFeedUpdated` (`MarketFeedHub.cs:194-198`) raises that event only when
  `ReferenceEquals(feed, _focused)`. Its own doc comment says so: *"Raised for buffer changes
  on the FOCUSED feed only."*

So the reach of the entire new-bar feature — toast, speech, narration — is the focused chart.
Meanwhile `BackgroundTabFeedService` deliberately keeps up to `MaxLiveBackgroundFeeds` (8)
**non-focused tabs on live subscriptions** (`BackgroundTabFeedService.cs:31`). Those feeds tick,
their buffers grow, their bars close, and nothing announces any of it.

**A user who opened four charts to watch four markets is told about bar closes on one of them.**
This is the same shape as Phase 2's row-3 correction: a ✅ in the "browser open" column that
described a subscriber rather than a delivery.

> `NewBarReachTests.AFocusedChart_closingABar_publishes_NewBarEvent` (the positive control),
> `ALiveBackgroundChart_closingABar_publishes_NOTHING`,
> `TheFeedHub_raises_FocusedFeedUpdated_for_the_focused_feed_only`.

### F2. The headless new-bar subscriber is wired to an event that cannot occur headless

`HeadlessSession.cs:214-224` force-creates a `DesktopNotificationService` carrying
`DesktopNotificationCategories.NewBars`, with a comment explaining the mask. It is a
subscriber. Per F1 the only publisher is the store's live-data path, and **the headless monitor
never dispatches into a store at all**: `LocalBackgroundMonitor.cs:143-163` fetches bars
straight off the provider and evaluates them as method arguments.

Five polls, five bar closes as far as the provider is concerned, zero `NewBarEvent`.

This is Phase 2's headline one layer up. Phase 2 found *a method with tests and no production
caller is a feature that does not exist*; here it is **a subscriber with a mask, a comment and
no producer**. The mask made it look deliberate.

> `HeadlessSessionTests.No_NewBarEvent_is_published_headless_however_many_bars_close`, whose
> second assertion checks the poll DID fire its alert — without it the test would pass against
> a monitor that did nothing at all.

### F3. The headless state is empty, so four of the five watchability refusals are self-inflicted

`LocalBackgroundMonitor.cs:160` builds `WorkspaceState.Initial with { SymbolDisplayName = … }`.
`WorkspaceState.Initial` has **no `Data`** and **no `ActiveSeries`**. The bars go in as the
`newBar`/`previousBar` arguments only. Consequences, read against `AlertEvaluator.cs`:

| Refusal in `BackgroundWatchability.WhyUnwatchable` | Why it actually fails |
|---|---|
| `ConditionTree != null` | `AlertEvaluator.cs:126` returns null when `state.Data` is empty |
| `Target == Indicator` | `:181` looks in `state.ActiveSeries`, which is empty |
| `Target == Poc` | needs the volume profile, computed from `state.Data` |
| `TrendChange` / `EntersZone` / `ExitsZone` | `:319`, `:335` — same `state.ActiveSeries` |
| no explicit symbol + provider | genuinely unfixable; there is nothing to fetch |

Four of the five are one cause: **the state handed to the evaluator is blank.** The refusal
list is honest about the effect and wrong about the reason, and the reason is what decides
whether it can shrink.

Two further gaps on the same line, both currently harmless and both blocking in Phase 3:

- **`Limit: 3`** (`LocalBackgroundMonitor.cs:147`). An RSI(14) needs 15 bars, an EMA(200) needs
  200. The in-session load is 200 (`ChartFeed.RefreshAsync`).
- **`new Dictionary<string, double>()`** (`:163`) — a fresh, empty `previousValues` on every
  poll. Crossover detection for indicator alerts reads `previousValues[$"{code}.{component}"]`
  (`:192`); with an empty map it is `double.NaN` forever. Harmless today only because indicator
  alerts are refused before they reach it.

### F4. `IIndicatorEngine` is store-free, and that is the seam Phase 3 needs

`IIndicatorEngine.CalculateAsync(code, data, parameters, ct)` returns
`Dictionary<string, double[]>` with no store, no workspace, no circuit
(`Services/Indicators/IndicatorEngine.cs:8`). `IndicatorOrchestrator`, by contrast, dispatches
`UpdateSeriesDataAction` into `IWorkspaceStore` and is therefore chart-shaped.

This matters because **`WorkspaceState` is a single-chart record** — one `Identity`, one `Data`,
one `ActiveSeries` (`Sdk/Models/WorkspaceState.cs:85-88`). A headless session watching six
symbols cannot hold six charts in one store. Any design that routes Phase 3 "through the real
store" either needs one store per watched symbol, or has to rotate one store's focus symbol by
symbol between evaluations. Neither is necessary: the engine underneath is already store-free.

`IBacktestWarmupAnalyzer` (`Services/Strategies/BacktestWarmupAnalyzer.cs`) already computes how
many bars a set of referenced indicators needs. Reuse it for the fetch limit rather than
guessing a number.

---

## 2. What this means for the design

The scope document's Phase 3 bullet says *"Phase 1 gives the headless scope the whole indicator
pipeline"*. That is true of the DI scope and false of the data path: the pipeline is there, but
it is fed by a store that headless never writes to. **The work is not "turn on the pipeline",
it is "give the evaluator a populated state".**

Two candidate designs, and the recommendation:

**(A) Drive the real store headless.** Set the headless store's identity/data per watched
symbol, run `IndicatorOrchestrator`, let `AlertOrchestrator` and `NewBarEvent` fall out of the
existing wiring for free.
*Against:* one store is one chart, so this is serial rotation across symbols — every poll
rewrites the whole workspace state N times, `AlertOrchestrator` fires off a state stream that is
now lying about what the user has open, and `DataManager`'s focus model has no headless meaning.
It also puts the store's ~40-field state, and every reducer, on the hot path of a background
poll. **Rejected.**

**(B) Compose the state the evaluator needs, per symbol, without the store.** The monitor
already owns a per-symbol loop. Give it, per watched symbol: enough bars (warmup-derived),
`ActiveSeries` computed through `IIndicatorEngine`, and a persisted `previousValues` map. Then
`WhyUnwatchable` shrinks to its one honest entry, and a bar close is a fact the monitor can
observe directly — it fetched bar N-1 last poll and bar N this one — rather than an event it
has to receive. **Recommended.**

Design (B) also keeps the Phase 1 and Phase 2 invariants intact: one delivery owner per event,
coverage asked at delivery time, and **headless REPORTS, it never ACTS**.

---

## 3. The work

### D1. A bar close the monitor can see, per watched symbol

The monitor polls every 60 s and re-fetches. A new bar is "the newest bar's timestamp is later
than the newest one I saw last poll for this symbol+timeframe" — no event, no store. Keep the
last-seen timestamp per watch key, exactly as the persistent evaluator keeps crossover state
across polls today.

- First poll for a symbol **seeds** and announces nothing. (Otherwise starting the terminal
  announces a bar close on every watched symbol.)
- Missing a poll must not announce N bars. One announcement per symbol per poll, naming the
  bar that closed.

### D2. Gate new-bar announcements harder headless than in-session

Carried from the scope document and unchanged: a one-minute chart is a toast a minute, and the
MATE daemon queues them. Decide between a **minimum timeframe** and a **digest**; see section 5.
`DesktopNotificationCategories.NewBars` already exists on the headless
`DesktopNotificationService` and stays the delivery switch — but note that once D1 exists the
monitor could deliver through `IDesktopAlertPresenter` the way it delivers alerts. **One owner
only.** Pick one and write down why, the way Phase 1 wrote down the alert mask.

### D3. Close F1 — background tabs, browser OPEN

Separate from headless and independently shippable. A live background feed that closes a bar
currently announces nothing to anyone. The fix is not to make `FocusedFeedUpdated` fire for
non-focused feeds — that event's contract is correct and `DataManager` binds the focused feed to
the store on purpose. It is a second, explicitly non-focused path from `ChartFeed.Updated` to a
new-bar announcement that names its symbol.

**Every announcement it produces must name the symbol**, because it is by definition about a
chart the user is not looking at. Phase 1's lesson applies verbatim: `AlertEvaluator` leaves
`AlertFired.Symbol` null and only `AlertOrchestrator` stamps it — *a field populated by one of
two producers is a field the other silently omits.*

### D4. Populate the state, and shrink `WhyUnwatchable` in the same commit

1. Fetch warmup-many bars instead of 3, via `IBacktestWarmupAnalyzer` over the indicators the
   symbol's alerts reference. Cap it; a headless poll is not a chart load.
2. Compute `ActiveSeries` through `IIndicatorEngine` for those indicators only.
3. Persist `previousValues` per watch key across polls.
4. Put the fetched bars into `state.Data`.
5. **Delete the refusal lines that no longer apply, in the same commit**, and leave the ones
   that still do. The scope document is explicit about this and it is the part most likely to
   be skipped: a list claiming a limit that no longer applies teaches the next reader to stop
   trusting the list.
6. `BackgroundWatchability` is shared with the alerts UI, which tells the user at creation time
   which alerts cannot be watched in the background. **That wording changes when the list
   changes** — it is a user-facing accessibility surface, not just an internal predicate.

### D5. The doubling hazard, for the third phase running

Phase 1: two subscribers = a lost utterance; two sessions = a doubled one. Phase 2 added
`CircuitOrderCoverage`. Phase 3 adds a third event class, so:

- Every delivery test is written **twice** — with a circuit open and with none — asserting
  exactly one delivery in each.
- New bars are per SYMBOL, so the coverage question is `CircuitAlertCoverage`-shaped, not
  `CircuitOrderCoverage`-shaped. A symbol on screen is announced in-session; every other watched
  symbol is the monitor's.
- D3 and D1 must not both announce the same bar when a browser is open on that symbol.

---

## 4. Suggested order

1. **D3** (background tabs, browser open) — independently shippable, no lifetime work, and it
   fixes a live in-session defect that has nothing to do with the browser being closed.
2. **D1 + D2** (headless new bars) — the phase's headline.
3. **D4** (watchability) — the largest, and the one whose blast radius reaches the alerts UI.

D4 could be split out entirely if Phase 3 needs to ship sooner; D1–D3 close the goal statement's
"new bar notifications" and D4 closes "alerts" more completely than Phase 1 did.

---

## 5. Decisions this needs from Cody

1. **How should headless new bars be rate-limited?** A minimum timeframe (e.g. nothing under
   15 m announces headless) is predictable and explainable; a digest ("4 bars closed on 3
   symbols") is complete but arrives late and reads awkwardly. **Recommendation: minimum
   timeframe, user-settable, defaulting to 15 m** — it is the option a user can reason about
   before it happens.
2. **Should D3 (background-tab bar closes, browser open) announce by speech, or toast only?**
   Speech is the terminal's primary channel, but a bar close on a chart you are not looking at
   interrupting the chart you are is the kind of thing that gets a feature turned off.
   **Recommendation: toast + earcon by default, speech opt-in.**
3. **Does D4 ship in Phase 3 or as its own pass?** It is the item most likely to double the
   session count.

---

## 6. What Phase 3 will NOT prove

Stated now so it is not discovered at the end. The standing rule is to demonstrate the defect or
mark it unverified.

- Nothing here is heard. Section 1's findings are unit tests; the delivery they describe has
  still never been heard through `spd-say` with Orca running. That is unchanged from Phases 0–2
  and is now three phases deep.
- The unverified list in `docs/BACKGROUND_MONITOR_SCOPE.md` §6 is untouched by this phase:
  Windows toast, macOS commands, minimize-to-tray, the tray icon with a screen reader, and
  Phase 2's live exchange socket.
- Fetching warmup-many bars per watched symbol per poll raises the request rate against every
  venue the user has alerts on. Phase 2 already noted `HasOpenWorkAsync`'s two calls per keyed
  venue per poll are within published limits **on paper, never measured against a real venue**.
  Phase 3 adds to that number and inherits the same caveat.
