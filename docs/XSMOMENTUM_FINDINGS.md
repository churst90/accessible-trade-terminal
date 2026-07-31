# Cross-sectional momentum — the first strong positive

Run 2026-07-31. `dotnet run -- xsmom --universe equity`. 39 equities/ETFs/metals/bonds,
common window 2007-11 → 2026-07 (18.7 years), 215 monthly rebalances.

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
mean top−bottom spread per 30d period: +0.37%   sd 3.62%   positive 124/215   p = 0.0045
```

Volatility-normalised ranking (return ÷ realised vol) gives the same answer slightly stronger:
365d 4/4, p = 0.0029.

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

| annual rate | shock | in top | excess | vs clean |
|---|---|---|---|---|
| 0.5% | −100% | 0% | +228.9% | 118% |
| 0.5% | −100% | 33% | +177.9% | 92% |
| **2.0%** | **−100%** | **0%** | **+317.7%** | **164%** |
| **2.0%** | **−100%** | **33%** | **+137.6%** | **71%** |
| 5.0% | −100% | 33% | +82.2% | 42% |

**The edge survives every cell.** And the asymmetry is not the obvious one: delisting is
concentrated in losers, which a long-top-third book is by construction not holding, so at the
optimistic bound survivorship is *understating* this comparison rather than inflating it.

An earlier version of this stress modelled "two phantom names losing 20% every rebalance", which
compounded across 215 months into a basket down 99%. That is not delisting, it is a monthly
catastrophe — the parameterisation was wrong and the numbers meaningless. Recorded because the
failure mode (a per-period rate that is only sane when converted to an annual one) is easy to repeat.

## Standing

Cross-sectional momentum in equities now survives costs, eras, noise injection and a survivorship
stress. **It is the only result in this lab that has been through a full robustness pass, and it
passed all four.**

Still not done: the long-short spread remains biased the usual way and is not traded here; the
universe is 39 large, liquid names and says nothing about small caps; and no live forward test has
been run.
