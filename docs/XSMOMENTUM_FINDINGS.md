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
