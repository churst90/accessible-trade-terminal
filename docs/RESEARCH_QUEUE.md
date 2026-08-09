# Research queue — specced, not started

Written 2026-08-08, out of the Benjamin Cowen / trader-interview batch
(`docs/CYCLE_FINDINGS.md` addendum, `docs/FORECAST_LEDGER.md`).

Each task below is specced to be picked up cold: the hypothesis, the exact metric, the controls it
must beat, what counts as done, and which files to touch. **The controls are not optional and are
not an afterthought — they are the deliverable.** A well-executed null closes a task.

Ordered by value. Task 1 is the only genuinely new idea here.

---

# Task 1 — `StrategyLab absorption`: does volume-without-range mark a defended level?

**Registry:** edge `absorption-volume-per-range`, currently `Untested`.
**Origin:** Cody, 2026-08-06 — *"once their wall is getting eaten through they just add more at that
level so their entire order gets filled."*

## The hypothesis, stated so it can fail

A large participant working an order refills a resting bid as it is consumed (this is literally what
an iceberg/reserve order does). If that is happening, the bar shows **high volume and a small
range** — effort without result. The claim is that such bars mark a level being defended, and that
price subsequently moves **away from that level in the defender's direction**.

**It fails if:** forward returns after high-absorption bars are indistinguishable from the control,
or the effect disappears once trailing volume is matched, or it is the same number as Amihud.

## The metric

For each bar `i`:

```
absorption(i) = volume(i) / trueRange(i)
```

Normalise it — raw absorption is dominated by the price level and by volume's own trend. Use a
**rolling rank within a trailing 252-bar window**, which is scale-free and needs no distributional
assumption. Bars where the window is incomplete are **excluded, not treated as low absorption**
(undefined ≠ false — that trap is in the research skill and has bitten this repo).

Direction is not in the metric. Take the defender's side from the bar's close position within its
range: close in the upper third → absorption on the bid (bullish); lower third → on the offer
(bearish); middle third → **no signal, and count how many bars that discards**.

## The three controls, in the order that will kill it fastest

1. **Amihud.** `absorption` is the reciprocal of the Amihud illiquidity ratio (`|return| / volume`),
   which is a *priced liquidity risk premium* in the literature — not a timing signal. Compute
   Amihud on the same rows and report both. **If the two rank-correlate above ~0.9 and Amihud
   quintiles produce the same spread, the task is done and the answer is "this is Amihud".**
2. **Plain trailing volume.** The cheap alternative that does the same job. Volume already carries
   forward information in crypto and *reverses* in equities
   (`docs/VOLUME_FINDINGS.md`). Absorption must beat a trailing-volume quintile split on the same
   bars, **within each asset class separately**. Pooling crypto and equities here will produce a
   number that is the asset-class label wearing a statistic.
3. **Random-entry baseline.** Same count of entries, same asset, same period, drawn at random. The
   standard harness: enter next open, 1×ATR(14) risk, 2R target, 20-bar horizon, R-multiple outcome.
   Average the control over ≥400 draws — a single random book is a sample from a very wide
   distribution, not a baseline.

## Also check before interpreting

- **Overlap with the POC.** The volume point of control *is* a level where volume concentrated, and
  `poc-mean-reversion-equities` is already a measured edge. Report what fraction of absorption bars
  sit within 0.5×ATR of the POC. If it is most of them, this is POC mean reversion rediscovered.
- **Overlap with trailing return.** Any "non-price" input here has to be checked against momentum
  before it is believed. Crowding claimed orthogonality and correlated 0.19; volume correlates
  0.43–0.59.
- **Report the discard rate** for middle-third closes and for warmup. Silent truncation reads as
  full coverage.

## Done when

`AccessibleTrader.StrategyLab/AbsorptionCommand.cs` exists, is wired into `Program.cs`'s switch,
prints the controls beside the result and a `── VERDICT ──` block that states the conclusion in
words **including if it is null**; `docs/ABSORPTION_FINDINGS.md` is written; the
`absorption-volume-per-range` edge is updated from `Untested` to a measured verdict with its
controls named and `breadth` filled in. Note `EdgeRegistryTests` **requires** every new
`*_FINDINGS.md` to have an edge record — that is already satisfied by updating the queued edge, but
the `source` field must be set to the new doc.

Data is all present: `bitstamp_*` and `mexc_*` for crypto, `yahoo_*`/`twelvedata_*` for equities.
5-minute bars exist for SPY/QQQ/IWM/DIA/AAPL if a finer-grained pass is wanted afterwards — but run
the daily version first, because the daily one is the one with enough history to mean anything.

**Realistic expectation:** the honest prior is an out-of-sample R² of 0.03–0.04 for a *successful*
quant signal. This is more likely to be Amihud than an edge. That is a fine outcome and should be
written up as one.

---

# Task 2 — Refresh the snapshots and resolve the forecast ledger

**Not a research task — a bookkeeping task with a deadline.** `docs/FORECAST_LEDGER.md` has ~25 open
dated calls and the first review date is **2026-09-01** (it resolves B2, "August 2026 is red for
BTC"). Then **2026-10-01**, then **2027-01-05** for the bulk.

## The blocker is snapshot freshness, not feed coverage

Current cutoffs: crypto **2026-06-15**, SPY/gold **2026-07-09/10**, on-chain **2026-04-07**, TLT
**2026-07-24**. Nothing after those dates is verified — **including the July BTC low and rally,
which are currently taken from Cowen's own on-screen figures.** Those two are the basis of two of
the six "resolved correct" calls, so they need independent confirmation before the 6/6 record is
quoted anywhere.

## What to run

```bash
cd AccessibleTrader.StrategyLab
dotnet run -- snapshot        # price data
dotnet run -- coinmetrics     # MVRV / market cap / supply — community tier, no API key
```

## The one feed genuinely missing

A **dollar index** (E5: "DXY to 105–106"), and the **10-year yield level** (first half of E4). FRED
covers both — `DTWEXBGS` and `DGS10` — and the FRED path already exists in `MacroEventCommand` for
`events_*.json`, so this is a small addition rather than a new integration. Until it exists, E5
resolves **"unscoreable"** and is recorded as such. Do not drop it: a recorder that cannot tell
"not yet" from "not there" writes holes and calls them data.

## Derivations needed for B5/B6

Both are already computable from feeds in the snapshot — an earlier draft wrongly called them
blocked:

```
realized cap   = market cap / MVRV
realized price = realized cap / supply
MVRV Z-score   = (market cap − realized cap) / stdev(market cap)
```

from `xs_coinmetrics_btc_capmvrvcur_1d.json`, `_capmrktcurusd_`, `_splycur_`. At the 2026-04-07
cutoff realized price computes to **~$54,100**, which matches Cowen's on-screen "~$53k" and is a
good sanity check that the derivation is right before trusting it.

## Done when

Each open row in the ledger carries a resolution and a date, **including the ones he got right**,
and the "base rate" column is filled for every scored row. Then update the
`cowen-forecast-record` memory with the running score.

---

# Task 3 — Make the risk-arming keys say what a losing streak costs

**Source:** Rajan Dhall, 400 logged one-trade-per-day sessions — **eight consecutive losses occurred
four separate times**. Plan for **7–14 losers in a row**. His own sizing follows from it: a 20%
drawdown budget ÷ 30 trades = **0.5–0.75% per trade**.

## What this is *not*

It is **not** a change to the presets. `CommandDispatcher.cs:530-532` arms 0.5% / 1% / 2%, and 0.5%
is already the Dhall-consistent option. Silently re-tuning defaults would be a worse change than
none, and this project's own rule is that the app ships tools, not opinions.

## What it is

The keys currently announce the cash at risk for *one* trade. They should also announce **what the
armed size implies in the tail**, because that is the number a trader cannot see and the one that
ends accounts. This is squarely the silent-feedback principle already established here: a key that
acts must say what it costs, not just what it does.

`QuickTradeService.Arm(...)` currently says:

> "Armed 2 percent, $400 at risk. …"

Proposed addition — one clause, with the streak length stated so the assumption is auditable:

> "Armed 2 percent, $400 at risk. An eight-loss streak would cost about 15 percent."

Compounding, not multiplication: `1 − 0.98^8 = 14.9%`, not 16%. Dhall's own point is that people
model compounding on the way up and forget it on the way down.

**Also flag:** `QuickTradeService.MaxRiskPercent` is **10.0** (`QuickTradeService.cs:316`). An
eight-loss streak at 10% is −57%, which needs a 133% gain to recover. The cap is not obviously
wrong — it is a cap, not a default — but it should be a deliberate decision rather than an
unexamined one, and it is Cody's call, not mine.

## Done when

The announcement includes the streak clause; a test pins the arithmetic (compounding, not
multiplication) and the wording; `docs/SIZING_AND_PYRAMIDING_NOTES.md` records where the 7–14 figure
came from so it is not mistaken later for something this project measured. **We did not measure it —
it is one practitioner's log of 400 trades**, and that provenance has to survive.

---

# Explicitly not building: a halving-cycle chart overlay

The halving→top interval (525 / 546 / 535 days, sd 9d, 0 of 400 surrogates that tight) is the
strongest cycle result this project has produced, and it is recorded as **`Fragile`** — see
`docs/CYCLE_FINDINGS.md`.

It should not ship as an indicator, for reasons that no further work can fix:

- **n = 3**, and the 2012 halving does not fit at all.
- **The halving is perfectly collinear with the US election cycle** — halvings land in 2012, 2016,
  2020, 2024, always election years — so the mechanism is unidentified.
- **Breadth 1 and unimprovable.** No other instrument has a halving.
- **The fourth point is provisional**: the 2025-10-06 top is only −51% down, and a new all-time high
  above $124,728 retroactively deletes it.

Shipping it would give it a credibility the evidence does not support, and only `ControlTested`
edges are allowed to score. **If it is ever built, it belongs in the lab as a `cycles`-style
command, not on a chart.**
