# The lab, redesigned — from a strategy tester to an edge engine

Proposal, 2026-08-01. Companion to [ALPHA_LEDGER.md](ALPHA_LEDGER.md) (what we know) and
[STRATEGY_CATALOGUE.md](STRATEGY_CATALOGUE.md) (what we hold).

## What the lab is for, stated plainly

> Collate data across markets, measure relationships, find edges, and **direct attention** —
> "reversal ~55% likely here", who it affects, how big it is — rather than emitting buy and sell.

Three constraints are fixed and shape everything below:

1. **Deterministic code at runtime. No LLM in the trading loop.** An LLM may help *design* a test;
   it may never be a step in producing a live score.
2. **Accessibility first.** Every output is a screen-reader-first structure — a spoken sentence with
   a walkable table behind it, not a chart with numbers in it.
3. **Nothing scores until it has beaten a control.** The provenance discipline the terminal now
   enforces on strategies applies to edges too.

## The honest diagnosis first

The lab today is a **strategy tester**: it answers "does spec X work on snapshot Y". That has been
the right tool and it has produced nineteen recorded verdicts. But it has three structural limits:

- **Findings are prose.** Nineteen verdicts live in fifteen markdown files. No program can read
  them, so nothing compounds — every study re-derives the frontier by hand.
- **The unit of work is a strategy, not an edge.** A strategy bundles an entry, gates, a stop and a
  ladder. When it fails you cannot tell which part failed; when it works you cannot reuse the part
  that worked. Every genuine finding in the ledger is an *edge* — a measured relationship — and
  every failure that cost the most time was a *strategy* that stacked four of them.
- **There is no asset model.** `asset-profile` fingerprints an asset, but the fingerprint is not
  persisted, not versioned, and nothing consumes it. The single most robust structural finding in
  this project is that **asset character decides which tool family applies** — and that finding is
  currently applied by hand.

Everything below follows from those three.

---

## The architecture

Six layers. Each is useful alone; each feeds the next.

### L1 — The data lake (extend what exists)

`strategy-lab-data/` snapshots already give reproducible, offline, versioned bars. Extend to a
per-asset **dossier directory** holding every series we can get for that asset: price at several
timeframes, derived series (returns, realised vol, volume), and whatever third-party data applies —
on-chain for crypto, fundamentals and earnings for equities, positioning, macro.

Rules that matter more than the schema: **every series carries its as-of date and its source**, and
**nothing is fetched during a study**. A study reads the lake; a separate refresh writes it. That is
what makes a re-run in six months an honest re-test rather than a different experiment.

### L2 — The asset profile (the "model" per asset)

A persisted, versioned fingerprint per asset, recomputed on refresh, each field with a confidence
interval and the sample it came from:

- volatility level and **maturation ratio** (early vs late realised vol — the one cross-asset
  discriminator that actually varied: 0.51–1.37)
- trend-versus-revert (variance ratio), and time spent in each regime
- liquidity and depth (the variable polarity turned out to be a proxy for)
- event sensitivity: does this asset move on FOMC, on earnings, on unlocks?
- **which ledger edges apply**, computed rather than assumed

Hurst is deliberately absent — it read 0.57–0.60 on all seventeen combinations tested and
discriminates nothing.

The profile is the object his "run different scenarios" question needs: scenarios are the profile
plus a hypothetical state.

### L3 — The edge registry (the keystone — build this first)

`edges.json`: the ledger as **data**. One record per tested claim:

```jsonc
{
  "id": "xs-momentum-equities",
  "claim": "Rank a universe by trailing return; the top tercile outperforms the bottom.",
  "scope":  { "assetClass": "equities", "horizonBars": 30, "universeMin": 20 },
  "effect": { "measure": "top-minus-bottom", "value": 0.0037, "unit": "per 30d", "p": 0.0045 },
  "controls": ["monotone-in-lookback", "noise-injection-86pct", "sign-flip-at-1m"],
  "evidence": "ControlTested",
  "lastMeasured": "2026-07-31",
  "decay": [ /* effect size re-measured on each refresh */ ]
}
```

Three things fall out of this that are not available today:

- **Decay is tracked, not discovered.** FOMC drift lost 70% of its effect post-2015 and we found
  that by accident. A registry re-measures every edge on every data refresh and plots the series. An
  edge that fades gets demoted automatically.
- **Orthogonality becomes measurable.** With edges as objects you can compute the correlation matrix
  *between their signals* — and the single most repeated failure in this project (stacking four
  correlated Cipher components and calling it confluence) becomes a number the tool refuses.
- **Nulls are first-class.** Nineteen recorded negatives stop the next hopeful re-run, which is
  worth more hours than any positive in the table.

### L4 — The scoring engine (deterministic, the "fingerprint detector")

For an asset and a moment: which registered edges are **in scope** (per the asset profile), which
are **active** (per current state), and what does that combine to.

- Only `ControlTested` edges contribute to a score. `WalkForward` shows as context. `Untested` and
  `Falsified` never score — exactly the rule the terminal now enforces on strategies.
- Weight by measured effect size and by **1 − |correlation|** with already-counted edges, so
  stacking correlated signals adds nothing. This is the mechanical answer to confluence stacking.
- Output is a component breakdown, not a number: *"score 62 of 100 — cross-sectional rank in the top
  tercile (+0.37%/30d, control-tested) and above the 200-day (regime, weak) — but volume is
  uninformative on this asset class and the mean-reversion edge does not apply here."*

### L5 — Action alerts

Not buy/sell. The alert says **what changed about the conditions**, and what that means for
attention:

> "BTC has entered the regime where its strongest measured edge applies (trend continuation,
> ~+0.4R, control-tested) and where its mean-reversion edge does not. Two of three conditions for
> the exit rule are met. Attention warranted."

Scenarios ride on the same engine: *"if the daily closes below 91,200 the profile flips from
trending to compressed, and three of the four active edges stop applying."*

### L6 — The claim intake pipeline (how a video becomes a row)

This is his "how do we do this through code alone" question. The answer is that we do not — we split
it at the right seam:

| stage | who | why |
|---|---|---|
| transcript → candidate claims | LLM, offline | reading and paraphrasing is what it is good at |
| claim → **falsifiable predicate** (data, horizon, universe, the number that would refute it) | human + LLM, offline | this is the step that has caught most bad claims |
| predicate → test with its named control | **deterministic code** | reproducible, re-runnable, no model in the loop |
| result → ledger row | code | including the negatives |

The existing `trading-video-analysis` and `strategy-research` skills already cover the first two
stages. What is missing is the last two being *one command* rather than a bespoke study each time.

---

## The crypto / qualitative side

The 24-point vetting guide and DexScreener are a different kind of input, and they need honest
framing:

**Split the 24 points into three buckets.**

- **Machine-collectable and testable** (~9 points): tokenomics and unlock schedule, holder
  concentration, exchange listings, pair age and liquidity depth, volume distribution across venues,
  market-cap rank trajectory, social growth rate, hack history, chain the token is built on. These
  become a **dossier** with a deterministic score.
- **Machine-collectable, not testable** (~6): team, VC backers, partnerships, GitHub activity. Real
  facts with no clean forward-return test — they belong in the dossier as displayed context, never
  in a score.
- **Judgement** (~9): whitepaper quality, use-case credibility, the bear narrative, the trial run.
  These are LLM-assisted *checklists* the user answers, stored as notes.

**Then test the machine part exactly the way we tested everything else.** Compute the dossier score
across a universe, sort by it, and measure the forward return spread — the cross-sectional harness
that produced our best result already does this. My honest prediction: it works as a **veto**
(excluding low-liquidity, high-concentration, unlock-cliff tokens avoids losses) and **not** as a
timing signal. That prediction is cheap to test and worth the day it costs, because a veto that
works is genuinely valuable and nobody has to believe a narrative for it to pay.

**DexScreener** fits as a liquidity and hazard feed: pair age, liquidity depth, volume/liquidity
ratio, buy/sell imbalance, boost activity. Two cautions to build in from the start: the universe is
**survivorship-poisoned** (dead pairs vanish), so any backtest needs a point-in-time universe
snapshot taken forward, not a current list read backward; and volume there is substantially wash.

**On narrative.** Code cannot read a narrative, but it can measure a narrative's *footprint*:
co-movement clusters (which tokens now move together that did not six months ago), rank migration,
volume share shifting between sectors. "A narrative exists" is best operationalised as "these N
assets have become newly correlated while their sector's volume share is rising" — that is a
measurable object, and it answers his "who does it affect" question directly.

---

## Is this what quant funds do?

Broadly, yes — and it is worth being precise about where the gap actually is, because it is not
where people assume.

**What we already do that they do:** a library of independently tested signals; controls and null
arms; walk-forward; decay awareness; separation of research from execution.

**What they do that we do not, in order of how much it would change results:**

1. **Portfolio construction.** They combine many small edges across many assets; the edge per
   position is tiny and the *portfolio* is the product. We test one asset at a time. This is the
   single biggest gap, and cross-sectional momentum — our best result — is already a portfolio
   signal we are currently using as a single-asset one.
2. **Transaction-cost and capacity modelling.** An edge of 0.37% per 30 days survives costs at
   monthly rebalance and does not survive at daily. We have never modelled this properly.
3. **Signal orthogonality as a first-class metric.** Covered by L3 above.
4. **Systematic decay monitoring.** Covered by L3 above.

What they have that we cannot match — colocation, tick data, teams — is irrelevant at this horizon.
Nothing in the ledger is a speed edge.

---

## Sequence — what to do, in order

**Step 1 · Prune the catalogue (half a day).** Now that provenance is recorded, act on it. Proposed
disposition of the thirty:

- **Delete (6):** the four Cipher-SR trilogies (v16 long/short, v17, v21) plus the two symmetric
  reversal shorts recorded as negative-expectancy (v23 short, v23r short). Their premises are
  falsified and their entry stacks cannot be honestly backtested on a repainting provider. The
  *record* stays in the ledger; the code goes.
- **Demote to `Untested` and strip the numbers from their descriptions (9):** every cell promoted
  out of the 89-cell battery (v23p, v23h ×2, v23a, v23or, faber-pulse, bare-bull-pulse, v14, v23c).
  Their quoted numbers are maxima over 89 draws and read as results.
- **Keep as reference (5):** trend baseline (the benchmark everything must beat), v18 refined short,
  v13, pulse-v2, v24.
- **Leave alone (10):** the untested ones, honestly labelled.

**Step 2 · Build the edge registry (2–3 days).** `edges.json` + a `lab edges` command: list, show
one with its history, and `re-measure` to run each edge's own test against current data and append
to its decay series. Seed it from the ledger above. **This is the keystone** — L2, L4 and L5 all
read it, and it is the thing that makes research compound instead of accumulate.

**Step 3 · Asset profile v2 (2 days).** Persist the fingerprint per asset, versioned, with intervals;
add `which edges apply` as a computed field. Reuse `asset-profile`.

**Step 4 · The cost model (1 day, small and overdue).** Turnover, spread and slippage per edge at
each rebalance frequency. Cheap, and it will change which edges are worth anything.

**Step 5 · The scoring engine + action alerts (3–4 days).** Deterministic, component-broken-out,
screen-reader-first. Only control-tested edges score.

**Step 6 · Claim intake as one command (2 days).** `lab claim --predicate <file>` → runs the test
with its named control → appends a ledger row, positive or negative.

**Step 7 · The crypto dossier (3–4 days).** DexScreener + the nine machine-collectable vetting
points, point-in-time universe snapshots from day one, then the cross-sectional test of whether the
dossier score predicts anything. Expect a veto, not a signal.

**What I would do first if only one thing gets done:** Step 2. Nineteen verdicts that no program can
read is the actual bottleneck — not a shortage of ideas, and not a shortage of data.
