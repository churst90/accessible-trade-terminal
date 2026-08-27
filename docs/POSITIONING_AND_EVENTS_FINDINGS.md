# COT positioning and event studies — one instructive null, one untestable, one known effect
> **⚠ SUPERSEDED STATISTICS — re-run before quoting (added 2026-08-27).**
>
> Every p-value below was computed with machinery that has since been found wrong, and the
> numbers have NOT been recomputed. Three defects, all fixed in code on 2026-08-27:
>
> 1. **Post-selection p-values.** `XsMomentumCommand` picked the best of 16 grid cells and then
>    ran the permutation test on that cell against a fixed-configuration null. The statistic
>    actually computed is a *maximum over 16*, whose null is much wider — so the p was too small
>    by roughly the effective number of independent cells. The command now reports a
>    max-statistic null alongside the naive one.
> 2. **Overlapping rows treated as exchangeable.** Every permutation test that emits one
>    observation per bar over a multi-bar horizon shuffled rows individually, though consecutive
>    rows share all but one of their forward bars. Effective sample size is nearer `n/horizon`
>    than `n`, so **significance was inflated by roughly √horizon**. The affected commands now
>    block-permute.
> 3. **The survivorship stress could not fail.** `XsMomentumRobustness` applied a uniform drag
>    that did not depend on the ranking, which reduces algebraically to the clean excess times a
>    positive constant. "The edge survives every cell" was arithmetic, not evidence. It now
>    removes names from the universe and re-ranks.
>
> Separately, the sample these numbers were computed on is not recorded — `strategy-lab-data/`
> is gitignored — so a re-run is a re-measurement on possibly different data, not a reproduction.
> Snapshots now carry a `barsSha256` so future results can name their sample.
>
> **Treat every number below as provisional until the commands are re-run.**


Run 2026-07-31. `dotnet run -- events`. 20-bar forward horizon.

## 1. CFTC COT positioning — null, and the way it fails is the point

Net speculator positioning as a % of open interest, rolling 26-week z-score, **lagged 6 days**
(the report is published Friday for the Tuesday of record; a backtest that ignores that trades on
positioning nobody could see). Contrarian claim: extreme net-long precedes falls.

| instrument | net-short quintile | net-long quintile | gap | p |
|---|---|---|---|---|
| **S&P 500** | +0.19 ATR | +1.07 ATR | **−0.88** | **0.0002** |
| Nasdaq | +1.18 ATR | +0.82 ATR | +0.36 | 0.0167 |
| Gold | +0.39 ATR | +0.48 ATR | −0.09 | 0.570 |
| Bitcoin | +1.16 ATR | +0.62 ATR | +0.54 | 0.033 |

Two match the claim, one is null, and **the most significant result of the four runs backwards.**

**The S&P and the Nasdaq are ~90% correlated indices and their COT signals point in opposite
directions**, at p = 0.0002 and p = 0.017 respectively. Both cannot be real. That is not a subtle
statistical argument — it is a direct demonstration that these are sample artifacts, and it is worth
more than any of the individual p-values.

Four instruments were tested. At α = 0.05 you expect 0.2 false positives; three "significant"
results pointing two ways is what noise looks like when you stop counting tests.

**Verdict: positioning data does not carry a usable contrarian signal here.** That now covers both
positioning sources available — exchange funding/OI (`CROWDING_FINDINGS.md`) and regulated COT.

## 2. Bitcoin halvings — untestable, but the shape is worth recording

| halving | −180d | −90d | +90d | +180d | +365d |
|---|---|---|---|---|---|
| 2012-11-28 | −57% | −13% | +155% | +933% | **+8190%** |
| 2016-07-09 | −31% | −35% | −5% | +55% | **+286%** |
| 2020-05-11 | +2% | +20% | +36% | +73% | **+562%** |
| 2024-04-20 | −49% | −36% | +3% | +4% | **+31%** |

Every halving was followed by a positive 365-day return. **n = 4**, and all four sit inside a single
secular bull market — there is no p-value worth quoting and this is reported as description, not
evidence.

The one thing that does stand out is the **decay**: +8190% → +286% → +562% → +31%. Whatever the
halving is worth, it has been worth dramatically less each cycle, which is what you would expect
from an event whose date everyone has known since 2009.

## 3. Calendar structure — the known effect, at known strength

| | turn of month | rest | gap | p |
|---|---|---|---|---|
| SPY (8,427 days) | +0.089%/day | +0.019% | **+0.070%** | **0.031** |
| BTC (5,415 days) | +0.353%/day | +0.109% | +0.244% | 0.099 |

The SPY turn-of-month effect shows up at roughly its documented strength. BTC's is larger but not
significant.

Weekday means — SPY: Mon +0.05%, Tue +0.06%, Wed +0.05%, Thu +0.01%, **Fri −0.01%**.
BTC: **Mon +0.54%**, Tue +0.20%, Wed +0.33%, **Thu −0.16%**, Fri +0.20%, Sat −0.03%, Sun +0.05%.

BTC Monday at +0.54%/day is a large number. It is also **one cell out of fourteen examined across
two assets**, where ~0.7 significant cells are expected by chance. Calendar effects are the most
data-mined patterns in finance and this sample cannot separate a real Monday effect from the
fourteen-cell search that found it.

## What was deliberately not tested

**FOMC and CPI release dates.** They are the obvious event candidates and are not in the snapshot
set. Reconstructing ~160 meeting dates from memory would put fabricated data at the centre of the
result, which is worse than not running it. This is a data-fetch task, and it is the single most
promising untested item in the events family — the dates are free, public, and carry no information
asymmetry about *when*.

## Standing

Of the three families opened in this session, **only on-chain has anything in it**
(`ONCHAIN_FINDINGS.md`). Positioning is now null from two independent sources. Events remain
genuinely untested pending real event dates — the halving and calendar work here is not a fair test
of the family, only of the two pieces that needed no external data.
