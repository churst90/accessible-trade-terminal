# Camel / Bob Lucas / Charles Nana cycles — the period is the detector, the translation might not be

Run 2026-07-31. `dotnet run -- cycles`. BTC, ETH, SPY, QQQ on daily bars.

## The claims

Bitcoin runs a **60-day** daily cycle low-to-low ±10% (54–66d); the S&P **40 days** (36–44d). Lows
land inside the band **80% of the time**. Where the cycle *high* falls decides direction —
right-translated bullish, left-translated bearish. A **cycle failure** (undercutting the prior
cycle low) means more downside.

## The trap, and how it was avoided

In the tutorials, cycle lows are marked by looking back at a finished chart. Pick the lows knowing
the outcome and they *will* be 54–66 days apart, because that is how they were chosen. The 80%
figure then measures the selection, not the market.

So lows here are found by a fixed algorithm — a pivot low that is the lowest of `span` bars either
side, knowable only `span` bars later, which is also the delay the tutorials' own confirmation step
("wait for a swing low, trendline break or MA cross") imposes in practice.

**The control:** every statistic is recomputed on 200 surrogate series built by shuffling the log
returns and rebuilding the price path. Same return distribution, same volatility, every trace of
periodicity destroyed.

## Periodicity: dead

| asset | span | real mean gap | **surrogate mean** | real in-band | **surrogate in-band** |
|---|---|---|---|---|---|
| BTC | 20 | 64.7d | **61.7d** | 10% | **16%** |
| ETH | 20 | 55.7d | **60.2d** | 16% | **16%** |
| SPY | 8 | 38.9d | **36.2d** | 13% | **15%** |
| QQQ | 8 | 38.7d | **36.1d** | 14% | **15%** |

**A shuffled random walk reproduces the cycle length on every asset — and lands inside the claimed
timing band more often than the real data does on three of four.**

The mean gap is a near-linear function of the detector's span and nothing else. On BTC:
span 5 → 15.9d, 8 → 25.0d, 10 → 31.7d, 12 → 38.6d, 15 → 48.7d, 20 → 64.7d, 25 → 75.3d. You can
produce any "cycle length" you like by choosing the span, and the surrogate tracks it the whole way.

And the headline number does not survive at all: **lows land in the claimed band 10–17% of the time
at best, not 80%.**

## Translation: the part that survives the trap

Translation — did the high come late or early in the swing — is a momentum statement that does not
need the fixed period to be real. Tested separately for exactly that reason. Forward 20-bar return
measured *after* the cycle completes plus the detector's confirmation lag:

| asset | right-translated | left-translated | gap | p |
|---|---|---|---|---|
| **BTC** | +6.16% (n=41) | −1.40% (n=38) | **+7.56%** | **0.030** |
| ETH | +1.67% (n=28) | −7.60% (n=27) | +9.27% | 0.098 |
| SPY | +0.35% (n=199) | +1.29% (n=112) | −0.94% | 0.073 |
| QQQ | +0.30% (n=153) | +0.32% (n=101) | −0.02% | 0.984 |

**Both crypto assets positive, both equities not.** That is the asset-class polarity for the fifth
time — translation is a momentum measure, and momentum works in the trending class.

**Do not over-read the BTC result.** Eight tests were run (4 assets × 2 claims); at α = 0.05 you
expect 0.4 false positives, and a Bonferroni-corrected threshold would be 0.006. p = 0.030 on 79
cycles does not clear that. It is *consistent with* a finding established elsewhere on much larger
samples, which is the only reason it is worth recording at all.

## Cycle failure: nothing

| asset | failed − held | p |
|---|---|---|
| BTC | −3.49% | 0.342 |
| ETH | −2.48% | 0.660 |
| QQQ | −0.89% | 0.309 |
| SPY | +0.28% (backwards) | 0.604 |

Three of four point the claimed direction, none is close to significant.

## Verdict

**The 60-day/40-day cycle is a property of the swing detector, not of the market.** "It is cycles
doing cycle things" is a description of how humans see charts. Any pivot-finding rule produces
regularly-spaced lows on random data, and this one produces the claimed spacing on random data more
reliably than on real data.

What is left is **translation**, which is momentum with a cycle vocabulary — and which behaves
exactly like every other momentum measure tested here: positive in crypto, absent in equities. If
you want that edge, `xsmom` and the Trading Cross measure it directly, on far larger samples,
without the cycle scaffolding.

**Related:** an earlier study in this lab found a "~22–33 bar cycle window" that was *universal
across unrelated assets*. That should have been the tell — a universal period across unrelated
markets is a detector signature, not a market property.

---

# Addendum, 2026-08-06 — the Bitcoin four-year cycle (Benjamin Cowen)

A second cycle claim from a different source, tested with the same discipline. Source: ~22 Benjamin
Cowen videos, 2026-06-25 to 2026-08-06. Data: `bitstamp_BTC_USDT_1d.json` (2011-08-18 → 2026-06-15),
`twelvedata_SPY_1d.json` (2006-08 → 2026-07). Scripts were ad-hoc; the method is stated in full
below so it can be re-run or promoted to a `StrategyLab` command.

## The three claims

1. **Cycle timing.** BTC tops ~1,050–1,069 days after the prior cycle low (three cycles, "within a
   week of each other"), and bottoms ~1,432–1,436 days after it. Therefore the 2026 low is due
   late September to December 2026.
2. **Midterm-year seasonality.** In US midterm years (2014, 2018, 2022, 2026) July is green for
   BTC and August/September are red.
3. **The S&P's midterm-year correction.** The last three midterm years each had a 10–20% S&P
   drawdown starting mid-August to late September, and BTC's cycle bottom forms inside it.

## Claim 1 — the cycle. Split verdict: the TOP timing is real, the BOTTOM timing is not

**The low-anchored version is retrospective selection**, the same trap as the Camel cycles. Making
Cowen's definition algorithmic — a cycle top is an all-time high followed by a ≥50% drawdown; the
cycle low is the minimum before the next all-time high — the full set of intervals is:

| measure | detected values (days) |
|---|---|
| low → next top | 537, 151, **1067**, 850, 111, **1050** |
| low → next low | 625, 557, **1431**, 948, 489, 1293 |

His numbers are *in there* (1067, 1050, 1431) and they are not invented. But so are 111, 151 and
489. Getting his figures requires deciding in advance which lows are "cycle" lows, and that decision
is made looking backwards. **P(6 spacings drawn from the surrogate cluster at least as tightly as
the real 6) = 0.608** — the real spacings are not unusually regular.

**But anchoring on the HALVING instead removes the circularity entirely.** Halving dates are
exogenous, pre-scheduled and knowable years ahead. Re-running the same top detector from each
halving:

| halving | cycle top | days | price |
|---|---|---|---|
| 2012-11-28 | 2013-04-09 | **132** | $229 |
| 2016-07-09 | 2017-12-16 | **525** | $19,188 |
| 2020-05-11 | 2021-11-08 | **546** | $67,559 |
| 2024-04-19 | 2025-10-06 | **535** | $124,728 (drawdown only −51% so far → *unconfirmed*) |

**The last three land 525, 546, 535 — a 21-day spread, sd 9 days, on a ~1,460-day cycle.** It is a
plateau not a peak: identical for every drawdown threshold from 0.55 to 0.75. And it beats the
control decisively — **0 of 400 shuffled-return surrogates, run through the same detector from the
same anchors, cluster that tightly (P < 0.0025)**, against a surrogate median sd of 367 days.

This is the strongest cycle result in the project. It is also **not enough to act on**, for four
reasons that no amount of further computation can fix:

- **n = 3.** Three observations. The first halving (132d) does not fit at all.
- **The halving is perfectly collinear with the US election cycle.** Halvings fall in 2012, 2016,
  2020, 2024 — always election years. "Top ≈ 535d after the halving" and "top ≈ Q4 of the year after
  a US election" are the same statement in this sample and cannot be separated. Cowen says both
  cycles coexist; he is right that they might, and the data cannot tell them apart.
- **Breadth 1.** One asset, one history. The repo's standard control — check a correlated instrument
  that must agree — has no candidate.
- **The fourth point is provisional.** At a 0.60 threshold the 2025-10-06 top is *unconfirmed*: the
  drawdown is ~51%, not ≥60%. A new ATH above $124,728 retroactively deletes it.

**The BOTTOM timing — the thing Cowen is actually forecasting — is much weaker.** Halving → cycle
low: **220, 889, 924** days. Two of three cluster; the first is nowhere near. Projecting the two
that do cluster onto the 2024-04-19 halving gives lows on **2026-09-25** and **2026-10-30** — which
does bracket his "late September through October" call. But that is n = 2.

**Verdict: `Fragile`.** Real, survives its surrogate, and cannot be strengthened. The top interval
is worth knowing; the bottom interval is two data points.

## Claim 2 — midterm-year seasonality. Falsified as stated: it is the YEAR, not the month

BTC monthly returns, midterm years:

| year | Jan | Feb | Mar | Apr | May | Jun | Jul | Aug | Sep | Oct | Nov | Dec |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 2014 | +9.7 | −31.3 | −17.6 | −1.3 | +39.9 | +2.1 | **−9.2** | −17.7 | −18.4 | −13.6 | +11.5 | −14.9 |
| 2018 | −26.9 | +1.6 | −32.8 | +33.4 | −18.9 | −14.8 | **+21.0** | −9.2 | −6.0 | −4.5 | −37.0 | −7.0 |
| 2022 | −16.7 | +12.2 | +5.4 | −17.3 | −15.6 | −37.3 | **+17.1** | −14.0 | −3.2 | +5.5 | −16.2 | −3.7 |
| 2026 | −10.1 | −14.8 | +1.8 | +11.9 | −3.6 | −10.4 | — | — | — | — | — | — |

August and September are 0-for-6 in midterm years. That looks strong until you build the control the
claim needs — **the same months in all years**:

| month | midterm mean | all-years (2012–25) mean | all-years green |
|---|---|---|---|
| Jul | **+9.6%** | +10.0% | 10/14 |
| Aug | **−13.6%** | +1.6% (median **−6.5%**) | 5/14 |
| Sep | **−9.2%** | −1.4% (the only negative-mean month) | 6/14 |

**July's strength is not a midterm-year property — July is the same in every year, and it is only
2-for-3 in midterms (2014 was −9.2%).** August and September are already BTC's two weakest months
unconditionally, so the midterm conditioning adds nothing identifiable at n = 3.

And the deeper problem: across all 42 midterm-year month cells, **13 are green (31%)**, against
~60% in the unconditioned sample. **Midterm years are simply down years for BTC.** "August and
September are red in midterm years" is largely a restatement of "midterm years are red" — the same
tautology trap recorded in `docs/TRANSLATION_FINDINGS.md`, where a conditioner and an outcome turn
out to be the same variable.

**Verdict: `Falsified` as a month-level claim.** The year-level claim (midterm years are bad for
BTC) is what the data actually shows, and it is n = 3 and confounded with the halving cycle.

## Claim 3 — the S&P midterm correction is close to an unconditional base rate

Deepest peak-to-trough drawdown per SPY calendar year, and the date of the peak:

| year | maxDD | peak | midterm |
|---|---|---|---|
| 2010 | −17.2% | Apr 26 | ✔ (does **not** fit — peak in April) |
| 2014 | −9.9% | **Sep 19** | ✔ fits |
| 2018 | −20.5% | **Sep 20** | ✔ fits |
| 2022 | −27.5% | Jan 4 | ✔ (his −19% Aug 16 leg is the *second* correction) |
| 2023 | −10.9% | Jul 27 | ✘ non-midterm, fits the pattern anyway |
| 2024 | −9.7% | Jul 16 | ✘ non-midterm, fits the pattern anyway |
| 2021 | −6.1% | Sep 2 | ✘ non-midterm, fits the pattern anyway |

Cowen's own precision is real — he quotes Sept 19 and Sept 21 and both are correct to the day. But
**11 of 19 years in the sample had a 9–25% drawdown**, and roughly a third of all years peak in
July–October. "A 10–20% S&P correction starting August–September" is near the unconditional base
rate for the index. It is not a wrong prediction; it is a prediction that costs nothing to make.

## What this addendum changes

Nothing in the standing findings. It adds one `Fragile` edge (halving-anchored top timing) and one
`Falsified` edge (midterm-year monthly seasonality) to `Catalogue/edges.json`, and it establishes
the reusable point below.

**The methodological keeper: re-anchor a cycle claim on an exogenous, pre-scheduled event and the
circularity disappears.** Camel cycles and Cowen's low-to-low counts both fail because the analyst
picks the anchors. The same claim measured from the halving — a date fixed by protocol years in
advance — is testable, and it passed. Before dismissing the next cycle claim, ask whether it has an
exogenous anchor available. Before accepting one, check whether the anchor is collinear with
something else on the same period (here: the US election cycle, fatally).
