# Weekly engulfing, and how price approaches a level — two claims tested

Run 2026-08-01. Both claims come from the trader's-triangle interview
(`https://www.youtube.com/watch?v=Nc2t6A99mPA`), queued as edges the same day and tested here.

```
dotnet run --project AccessibleTrader.StrategyLab -- weekly-persistence --snapshots strategy-lab-data
dotnet run --project AccessibleTrader.StrategyLab -- approach --snapshots strategy-lab-data --tf 1d
```

---

## 1. "A bullish engulfing weekly candle means next week is more likely up"

49 instruments, **77,331 weeks**. 10 crypto (native weekly), 39 equities and ETFs (daily aggregated
to ISO weeks, final partial week dropped). SPY and QQQ deduplicated across providers.

**Two controls.** A **random-week null** — the same number of weeks drawn at random from the same
series, 5,000 times — because the honest comparison is the asset's own unconditional up-rate, not
50%. And **the cheap alternative**: "last week simply closed up". Engulfing is a momentum pattern
with extra conditions bolted on; if plain up-weeks predict as well, the pattern is decoration.

### Result: the claim is a null

| | bullish engulfing lift | plain up-week lift | pooled p | instruments positive | era split |
|---|---|---|---|---|---|
| **equities** (39) | **−2.31 pts** | −1.17 pts | 0.995 | 11 / 39 | H1 −2.7 · H2 −1.4 |
| **crypto** (10) | +3.21 pts | −0.20 pts | 0.184 | 5 / 10 | **H1 +10.4 · H2 −1.8** |

"Lift" is the conditional up-rate minus that instrument's own unconditional up-rate.

**Equities: dead.** Negative lift, 0 of 39 instruments significant where ~2 were expected by chance,
pooled p = 0.995. **Crypto: an era artifact.** The +3.21 looks encouraging until the split — the
entire effect is in the first half (+10.4) and gone in the second (−1.8), which is the signature
this project has learned to distrust. 5 of 10 positive is a coin flip.

Note also that the *cheap alternative* is flat everywhere. There is no simple weekly momentum here
either, so nothing was lost by the pattern failing to beat it.

### The mirror image did better than the claim

Testing the symmetric case was free, and it is the only thing in this study that survived anything:

| crypto, **bearish** engulfing → down week | |
|---|---|
| lift | **+8.30 pts** |
| its cheap alternative (plain down week) | +0.04 pts |
| pooled p (random-week null) | **0.013** |
| era split | H1 +5.2 · H2 **+12.0** (stronger recently) |

The structure is doing the work, not momentum — the plain down-week control is flat. And unlike the
bullish arm it holds in both halves and strengthens in the recent one.

**Three reasons this is "interesting", not "true".** Four pooled tests were run (two classes × two
directions), so the Bonferroni threshold is 0.0125 and **0.013 sits just outside it**. The ten crypto
instruments are not ten independent samples — they move together, which narrows the pooled null
dishonestly. And the total is 209 signals. It needs an independent test on assets not in this set
before it means anything, with a null that preserves cross-asset correlation.

The asymmetry is at least consistent with what this project keeps finding: in crypto the two
directions are not mirrors of each other.

---

## 2. "How price approaches a level predicts whether the level holds"

Two halves, tested separately: one-way momentum into a level should make it break, and price
loitering near a level should make it break.

51 instruments on daily bars, **22,898 real-level touches**. Levels are confirmed swing pivots — a
pivot at bar *p* is only knowable at *p + 10*, and cannot be touched before then, so no lookahead.
Touch = close within 0.25 ATR. Respected = travels 1 ATR back the way it came within 10 bars without
first closing 1 ATR through. One touch per level.

**The control is the whole test: matched random horizontal lines**, same count, drawn uniformly in
*log* price (uniform in raw price on an asset that went 100× would put every line above the history
and never get touched), eligible on the same schedule.

### Result: both halves are nulls, and real levels are no better than random lines

Baseline: real levels hold **46.2%**, random lines **46.7%** — a difference of **−0.5 points**. That
independently reproduces the fib study's central finding under a completely different definition of
"respected": *a line's respect rate is a property of the measurement geometry, not of the line.*

| approach efficiency | real n | real hold | random hold | real − random | p |
|---|---|---|---|---|---|
| chop (<0.25) | 7,022 | 44.8% | 45.5% | −0.8 | 0.507 |
| middle | 9,313 | 46.1% | 46.4% | −0.3 | 0.698 |
| one-way (>0.5) | 6,563 | 47.9% | 47.4% | +0.5 | 0.582 |

| bars loitering near the level | real n | real hold | random hold | real − random | p |
|---|---|---|---|---|---|
| clean (0–1) | 11,565 | 47.5% | 48.1% | −0.6 | 0.428 |
| some (2–4) | 10,700 | 45.0% | 45.1% | −0.1 | 0.918 |
| loitering (5+) | 633 | 42.7% | 43.8% | −1.2 | 0.708 |

Real levels *do* hold less often after loitering (47.5% → 42.7%) — and **random lines do the same
thing**, so the conditioning is describing the measurement rather than the level. This is precisely
the trap the fib confluence result fell into.

### The 4h arm, and why it does not rescue it

On 4h (7 crypto instruments, 5,488 touches) three of six buckets clear p < 0.05: chop +6.3
(p = 0.029), one-way −3.5 (p = 0.035), loitering-some −4.1 (p = 0.020). The one-way sign is the
direction the claim predicts — real levels holding *worse* than random lines when momentum runs into
them.

It is still most likely noise. The signs are inconsistent across buckets of the same conditioning,
the daily sample has four times the data and shows nothing, and seven correlated crypto instruments
on one timeframe is the weakest evidence base in this document. Recorded, not believed.

---

## A methodology bug worth keeping

The first version of both commands seeded the random control with `string.GetHashCode()`. **.NET
randomises string hash codes per process**, so the control resampled on every run: the same bucket
printed **−5.6 points** and then **−1.8 points** on two consecutive runs of identical code, and the
first number would have been reported as a near-finding. Fixed with a fixed FNV-1a seed; both
commands now reproduce byte-for-byte.

A p-value that moves between runs is not a p-value. Any control drawing randomness must be seeded
from something stable, and the cheapest way to catch it is to **run the study twice and diff**.
