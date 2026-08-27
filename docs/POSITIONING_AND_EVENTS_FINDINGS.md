# COT positioning and event studies — one instructive null, one untestable, one known effect
> **RE-RUN 2026-08-27 — the COT verdict stands, and the argument that carried it is gone.**
>
> Recomputed with block permutation over the overlapping 20-bar forward rows. **All four COT
> p-values collapsed into nothing:** S&P 0.0002 → 0.124, Nasdaq 0.017 → 0.509, Gold 0.570 → 0.839,
> Bitcoin 0.033 → 0.910. The conclusion — positioning carries no usable contrarian signal — is
> unchanged and now rests on plain absence rather than on a contradiction.
>
> **That costs this doc its sharpest argument.** The old write-up made its case from the S&P and
> the Nasdaq, two ~90%-correlated indices, giving opposite signals at p = 0.0002 and p = 0.017:
> "both cannot be real" was a demonstration that beat any single p-value. After the correction
> neither is significant, so there is no contradiction left to point at — just four null cells.
> The argument was right and it was built on numbers that were wrong, which is a thing worth
> knowing about how convincing an argument can feel.
>
> **A confound in this particular re-run, stated because it cannot be undone:** the COT archives
> were re-downloaded in the same session (`cftc-cot` overwrites `xs_cftc_*.json` in place), so the
> series now run to 2026-08-18 rather than the original 2026-07-31 cut. The S&P and Nasdaq quintile
> means are byte-identical to the old ones and only their p-values moved; the Gold and Bitcoin
> quintile MEANS moved too (Gold +0.39/+0.48 → +0.26/+0.13, BTC +1.16/+0.62 → +0.59/+0.69), so
> those two rows are part re-measurement and part correction and cannot be attributed cleanly.
>
> The calendar section moved the other way: SPY turn-of-month p = 0.031 → **0.0045**. The halving
> table is descriptive (n = 4) and unaffected. variantsTried = 4 for the COT edge.


Run 2026-07-31, **re-run 2026-08-27**. `dotnet run -- events`. 20-bar forward horizon.

## 1. CFTC COT positioning — null, and the way it fails is the point

Net speculator positioning as a % of open interest, rolling 26-week z-score, **lagged 6 days**
(the report is published Friday for the Tuesday of record; a backtest that ignores that trades on
positioning nobody could see). Contrarian claim: extreme net-long precedes falls.

| instrument | net-short quintile | net-long quintile | gap | p | *(p before the overlap fix)* |
|---|---|---|---|---|---|
| S&P 500 | +0.19 ATR | +1.07 ATR | −0.88 | 0.124 | *0.0002* |
| Nasdaq | +1.18 ATR | +0.82 ATR | +0.36 | 0.509 | *0.0167* |
| Gold | +0.26 ATR | +0.13 ATR | +0.13 | 0.839 | *0.570* |
| Bitcoin | +0.59 ATR | +0.69 ATR | −0.11 | 0.910 | *0.033* |

**Nothing here is significant.** Two of the four gaps still point the way the contrarian claim
needs and two do not, at sample sizes of 3,000–5,000 overlapping observations, which is what a
family of coin flips looks like.

The gaps are 20-bar forward returns emitted one per bar, so consecutive rows share 19 of their 20
bars. Shuffling them individually treats them as exchangeable when the effective sample size is
nearer `n/20`; block permutation puts the confidence back where the data can support it, and there
was never much. The S&P's effect size did not change at all — only how sure the arithmetic was
allowed to sound.

**What this re-run cost the doc is its best argument.** The original made its case from the S&P and
the Nasdaq — ~90%-correlated indices giving opposite signals at p = 0.0002 and p = 0.017 — where
"both cannot be real" is a direct demonstration of a sample artifact and worth more than any single
p-value. That demonstration is gone with the p-values that made it, and the same verdict now rests
on four unremarkable nulls. The reasoning was sound and the inputs were wrong.

Four instruments were tested. At α = 0.05 you expect 0.2 false positives; before the correction
there were three "significant" results pointing two ways, and after it there are none.

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
| SPY (8,427 days) | +0.089%/day | +0.019% | **+0.070%** | **0.0045** |
| BTC (5,415 days) | +0.353%/day | +0.109% | +0.244% | 0.111 |

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
