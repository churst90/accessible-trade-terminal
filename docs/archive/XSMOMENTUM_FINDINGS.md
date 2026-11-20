# Cross-sectional momentum — the first strong positive
> **RE-RUN 2026-08-27 — the headline SURVIVES the corrections, and two more defects fell out.**
>
> The three statistical defects filed on 2026-08-27 (post-selection p-values, overlapping rows
> treated as exchangeable, a survivorship stress that could not fail) have now been recomputed
> rather than only fixed. **The equity result holds:** p = 0.0069 against a null of the maximum
> over all 16 grid cells, against p = 0.0047 for the old fixed-configuration null — so the
> selection effect is real but small, and the edge clears both. Every number below has been
> re-measured and is current.
>
> Two things the re-run found that the fix itself had not:
>
> 1. **The first max-statistic null was on the wrong scale.** It took the maximum of the raw mean
>    spread across cells, but a hold=90 cell's spread is a 90-day return and a hold=30 cell's is a
>    30-day one — the long-hold cells dominate the maximum whatever the data says. Run that way the
>    grid's hold=30 winner scored p = 0.97: not "no effect", a wrong yardstick. It now studentises
>    each cell by that cell's own null dispersion (Westfall–Young maxT). Dividing by the *observed*
>    sd instead would be the textbook t and is also wrong here, because momentum-sorted thirds are
>    more volatile than random ones — the effect inflates its own denominator.
> 2. **The replacement survivorship stress was still inert.** It removed
>    `round(names × annual ÷ periodsPerYear)` names per rebalance, which for 39 names at 5%/yr over
>    12.2 rebalances a year is `round(0.16) = 0` — every row of the table printed "100% vs clean"
>    and nobody read that as the null result it was. Stochastic rounding fixed it, and the table
>    below is the first one that ever moved.
>
> Sample caveat that has not gone away: `strategy-lab-data/` is gitignored, so this is a
> re-measurement on possibly different data, not a reproduction. Snapshots now carry a `barsSha256`.

> **REPRODUCED 2026-09-04 — cell for cell, and the sample is now pinned.**
>
> `dotnet run --project AccessibleTrader.StrategyLab -- xsmom --universe equity --snapshots
> ./strategy-lab-data`. Every printed statistic matches this document exactly: 1/4, 2/4, 4/4, 4/4
> by lookback; mean excess −9.3%, −3.0%, +53.5%, +150.2%; per-period spread +0.37% with sd 3.62%
> and 124 of 215 periods positive; grid max |z| = 3.52 at look=30; **p = 0.0069** max-statistic and
> 0.0047 fixed-configuration. 11 of 16 cells beat random, 11 of 16 beat the basket.
>
> **That match is itself the evidence the sample did not move**, and the argument is the one this
> lab used in the other direction on 2026-08-01. The permutation routine is seeded `Random(555)`
> and deterministic, so a p CANNOT move unless the data moves — which is how the earlier
> "reproduced exactly … 0.0044 vs 0.0045" was caught as a different sample rather than noise. Here
> nothing moved at all, to the last printed digit, across sixteen cells and every robustness table.
>
> **The snapshots on disk predate `barsSha256` and do not carry one** (they were fetched
> 2026-04-09 through 2026-07-27; the field is written by `SnapshotCommand` from 2026-08-27
> onward). So this run is pinned by a fingerprint computed over the files the loader actually
> selects — `sha256` of `symbol|barCount|sha256(file)` for each of the 39, in symbol order, after
> the loader's own dedupe:
>
> **`1c280cbd45719727fd330b4d80e55b658af61e75419fc3074eb2184e92593ea3`**
>
> Two symbols have snapshots from two providers (QQQ and SPY, yahoo and twelvedata). `Load` keeps
> the longest history per symbol, so both resolve to the yahoo file and neither is ranked twice —
> the double-counting trap is handled, and this was checked rather than assumed.
>
> **One row of the survivorship table deserves reading on its own.** At a 0.5%/year delisting rate
> with total loss and delistings drawn at RANDOM rather than from the bottom half, the excess goes
> **negative** (−13.7%, or −7% of clean). That is the mildest hazard rate in the table, and it is
> the row that reverses the sign. The stress can fail now, and on one plausible setting it does.


Run 2026-07-31, **re-run 2026-08-27**. `dotnet run -- xsmom --universe equity`. 39
equities/ETFs/metals/bonds, common window 2007-11 → 2026-07 (18.7 years), 215 monthly rebalances.

## Why this was the biggest gap

Every prior study in this lab is **time-series**: one asset measured against its own past.
Cross-sectional momentum measures an asset against its **peers** — a different alpha family, and
the most replicated anomaly in academic finance. Samir Varma names it as one of three distinct
momentum types and notes it works "even with unrelated assets thrown in the basket."

Rule: rank the basket by trailing return, hold the top third equal-weighted, rebalance each hold
period. Lookback/skip/hold in **calendar days**, so crypto's 365-day year and the equity 252-day
year need no class-dependent constant.

## The control

"The top-ranked third went up" is near-guaranteed in a universe that rose, and says nothing about
ranking. Every configuration is measured against a **random-selection portfolio** — same name count,
same dates, same eligible set, drawn by coin. **Averaged over 400 independent random books**, because
a single draw over 18 years is a sample from a very wide distribution, not a baseline. (The first
run used one draw and produced a random column swinging from +79% to +476%; that was noise, not a
control.)

## Result — equities

| lookback | beats random | mean excess over random |
|---|---|---|
| 30d | 1/4 | −9.3% |
| 90d | 2/4 | −3.0% |
| **180d** | **4/4** | **+53.5%** |
| **365d** | **4/4** | **+150.2%** |

**Monotone in lookback**, and the sign flip is exactly where the literature puts it: one-month
horizons carry short-term **reversal**, not momentum. Judging this on a flat grid count would be
wrong — it would count a confirmed prediction as evidence against the hypothesis.

Per-period test at lookback 365 / hold 30, permuting the **rank labels within each rebalance date**
so each name keeps its own realised return and the market's behaviour on that date is held fixed:

```
mean top−bottom spread per 30d period: +0.37%   sd 3.62%   positive 124/215   z vs null 2.82
grid max |z| = 3.52 at look=30 skip=0 hold=30
p = 0.0069  (max-statistic null over 16 grid cells, studentised by each cell's null)  *
p = 0.0047  (fixed-configuration null — POST-SELECTION, shown for contrast)
```

**Read the first p, not the second.** The cell tested was chosen as the grid's maximum, so its own
null is too narrow; the honest reference is the distribution of the maximum over all 16 cells. The
gap between 0.0047 and 0.0069 is the size of the selection effect here, and it is small — sixteen
cells over four lookbacks are heavily correlated, so the grid is nothing like sixteen independent
tries. **variantsTried = 16.**

Volatility-normalised ranking (return ÷ realised vol) gives the same answer slightly stronger:
365d 4/4.

Note the shape: only **58% of periods are positive**. The edge is in magnitude, not frequency —
the trend-follower's profile Narang describes, wrong often but right big.

## Result — crypto and the mixed universe: null

| universe | 365d configs beating random | per-period p |
|---|---|---|
| crypto (10 names) | 0/4 | 0.66 |
| all 49, vol-normalised | 0/4 | 0.34 |

**Crypto** is a null, but with 10 names a tercile is 3 names — underpowered rather than disproven.

**The mixed universe is the more interesting null.** Vol-normalisation was added specifically to
rescue it — ranking raw returns across a mixed basket is close to ranking by volatility, so the top
third fills with crypto regardless of momentum. Normalising did not rescue it. That is consistent
with `POLARITY_AND_GATE_FINDINGS.md`: you cannot rank a trending asset class against a reverting
one and expect the ranking to mean anything. **Keep the universes separate.**

## Caveats that bound the result

- **Survivorship.** Every symbol still trades today. The names that would have ranked worst are
  precisely the ones that stopped existing. This is the study most damaged by that bias — treat the
  spread as an upper bound. The random control absorbs part of it (it draws from the same biased
  universe), which is the second reason to read the excess rather than the raw return.
- **No transaction costs.** 215 rebalances of a 13-name book is real turnover.
- **One common window** set by the shortest history (2007-11). It contains the GFC, the 2010s bull,
  COVID, and 2022 — reasonable regime coverage, but it is one path.
- Long-only top third. The long-short spread is reported but shorting needs hard-to-borrow and
  cost-to-borrow data to be honest.

## Standing

This is the second surviving result in the lab, alongside the 200-day-MA dip filter — and it is the
stronger of the two. Both are in equities. Both are small, robust, and unglamorous.

---

# Robustness pass — 2026-07-31

Run on the best configuration (lookback 365, skip 0, hold 30; 215 rebalances). Four tests, in
descending order of how likely each was to kill the result.

## 1. Transaction costs — passes comfortably

**Average one-way turnover is only 17% per rebalance.** A 365-day lookback produces a sticky top
third, which is the whole reason costs do not bite.

| bps/side | momentum | basket | excess |
|---|---|---|---|
| 0 | +509.9% | +316.2% | +193.8% |
| **5** | +487.5% | +314.6% | **+172.9%** |
| **10** | +465.9% | +313.1% | **+152.8%** |
| 25 | +405.8% | +308.5% | +97.3% |
| 50 | +319.4% | +300.9% | +18.5% |

**Break-even ≈ 46 bps/side.** US large-cap retail commission is ~0 and spread on these names is
1–3 bps, so the realistic rows are 5–10, where roughly 80% of the gross edge survives.

Note the benchmark changed for this test. Against a random-selection book both sides churn, but a
random book redrawn monthly is not what anyone would do instead — the realistic alternative is
holding the whole basket at near-zero turnover. **Momentum vs equal-weight-all net of costs is a
harder test than the headline one, and it passes.**

## 2. Eras — consistent, unlike everything else tested here

| era | momentum | basket | periods beating basket |
|---|---|---|---|
| 2008-11 → 2013-02 | +58.5% | +60.3% | 29/53 |
| 2013-03 → 2017-06 | +34.1% | +23.4% | 31/53 |
| 2017-07 → 2021-10 | +66.6% | +46.0% | 31/53 |
| 2021-11 → 2026-06 | +72.1% | +44.1% | 33/56 |

Wins in three of four, and the loss is a rounding error (58.5 vs 60.3). The period win rate sits at
55–59% in every era. Compare the Trading Cross, which lost in three of five.

## 3. Noise injection — degrades gradually, as a real edge should

Varma's test: perturb every log return by gaussian noise scaled to that series' own daily
volatility, rebuild the path, re-run. Five draws per level.

| noise | edge/period | vs clean |
|---|---|---|
| 0% | +0.178% | 100% |
| 25% | +0.153% | 86% |
| 50% | +0.111% | 62% |
| 100% | +0.066% | 37% |
| 200% | −0.115% | −65% |

**Graceful decay, no cliff.** A curve-fit keyed to the exact price path collapses at the first
perturbation. This retains 86% under noise a quarter the size of daily volatility and only breaks
when the injected noise is twice the real thing, which is expected.

## 4. Survivorship — stressed, since it cannot be fixed

The universe is all survivors and no delisting data exists for it. Stressed instead under stated
assumptions, parameterised as an **annual delisting rate** — the only form in which the assumption
can be checked against reality. Large-cap delisting for cause runs roughly 0.5–2%/year; 5% is a
deliberately pessimistic bound.

The parameter that decides the answer is **what share of the vanished names were in the top third
when they died**: 0% if they were all slow decliners, 33% if death was as likely for winners
(sudden fraud — Enron, Wirecard).

Re-measured 2026-08-27. Names that die are **removed from the ranking** from that rebalance on,
not merely charged a fee — so unlike both earlier versions this stress can change the sign. The
`from bottom` column is the share of deaths drawn from the bottom half of the trailing-return
ranking: 100% is the harshest realistic assumption (delisting is concentrated in losers, which a
long-top-third book is by construction not holding) and 50% is delisting-at-random.

| annual rate | shock | from bottom | momentum | basket | excess | vs clean |
|---|---|---|---|---|---|---|
| — | — | — | +509.9% | +316.2% | +193.8% | 100% |
| 0.5% | −50% | 100% | +512.9% | +316.5% | +196.3% | 101% |
| 0.5% | −50% | 50% | +476.6% | +309.1% | +167.5% | 86% |
| 0.5% | −100% | 100% | +512.9% | +110.7% | +402.2% | 208% |
| **0.5%** | **−100%** | **50%** | **+93.2%** | **+106.9%** | **−13.7%** | **−7%** |
| 2.0% | −50% | 100% | +553.7% | +291.6% | +262.0% | 135% |
| 2.0% | −100% | 50% | +86.8% | −91.7% | +178.5% | 92% |
| 5.0% | −50% | 50% | +470.6% | +212.0% | +258.6% | 133% |
| 5.0% | −100% | 50% | −74.3% | −100.0% | +25.7% | 13% |

**The edge survives every bottom-biased cell and dies in one random-total-loss cell** — 0.5%/yr
with a −100% shock drawn at random takes the excess to −13.7%. That is the cell to look at, and
it is also the one whose assumption is least like reality: delisting-at-random with total loss
takes the whole *basket* to −100% at the higher rates too, so it describes a world in which
nothing here is tradeable rather than a world in which momentum specifically fails. Bottom-biased
delisting — the realistic direction — *helps* a top-third book, which is the asymmetry worth
remembering.

Two earlier versions of this stress were incapable of failing, for two different reasons, and both
printed reassuring tables. The first modelled "two phantom names losing 20% every rebalance", which
compounded across 215 months into a basket down 99% — not delisting, a monthly catastrophe — and
applied it as a uniform drag that reduces algebraically to the clean excess times a positive
constant. The second removed names properly but computed how many with `Math.Round`, which for
39 names at 5%/yr over 12.2 rebalances a year is `round(0.16) = 0`: it printed "100% vs clean"
in all thirteen rows. The durable lesson is that a stress table whose last column is 100%
everywhere is not a pass, it is a report that nothing was applied.

## Standing

Cross-sectional momentum in equities survives costs, eras, noise injection, a selection-corrected
permutation null (p = 0.0069 over 16 grid cells) and a survivorship stress that is finally capable
of failing. **It is the only result in this lab that has been through a full robustness pass**, and
it passes all of it except one cell of the survivorship stress whose assumption — random
delisting to zero — also destroys the benchmark it is being compared against.

Still not done: the long-short spread remains biased the usual way and is not traded here; the
universe is 39 large, liquid names and says nothing about small caps; and no live forward test has
been run.
