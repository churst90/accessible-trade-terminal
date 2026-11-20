# Fibonacci, Gann and confluence — tested against controls that could kill them

Run 2026-07-31. `dotnet run -- fib --only BTC_USDT`. BTC across 4h / 1d / 2d / 1w,
**~355,000 level tests**. Swings found by a pivot rule with a fixed span; a pivot at bar *p* is only
knowable at *p + span*, so every level goes live with the same delay a real trader has.

**Respected** = after a touch (within 0.25 ATR), price travels 1 ATR back the way it came within 10
bars without closing 1 ATR through.

## Three controls, because levels are the easiest thing in TA to confirm by accident

1. **Random levels** at the same density — draw enough lines and every price is near one.
2. **Placebo ratios** (0.11, 0.29, 0.44, 0.55, 0.71, 0.87) on the same swings — isolates the
   *ratios* from support/resistance existing at all.
3. **A density control on confluence** — fib levels cluster where price has already spent time.

## Result: the Fibonacci ratios do nothing

| timeframe | fib | placebo | random | fib − placebo | fib − random |
|---|---|---|---|---|---|
| 4h | 59.2% | 59.1% | 59.0% | +0.1% (p=0.49) | +0.2% (p=0.12) |
| 1d | 58.8% | 58.5% | 58.8% | +0.2% (p=0.58) | **0.0%** (p=0.99) |
| 2d | 57.8% | 58.2% | 57.7% | −0.5% (p=0.55) | +0.1% (p=0.91) |
| 1w | 58.3% | 56.7% | 57.1% | +1.6% (p=0.57) | +1.2% (p=0.67) |

Nothing anywhere, on any timeframe, on 355k tests.

**The number that explains the folklore: a RANDOM horizontal line is respected 59% of the time.**
"Levels hold about 60% of the time" is true — and true of *any* line. It is a property of the
measurement geometry (you are at the edge of a move when you touch a level from one side), not
evidence that the level did anything. Every fib level anyone has ever pointed at held ~60% of the
time, and so would a line drawn with a ruler and a blindfold.

## Confluence: mostly a description of ranges

| overlapping levels | fib respected | random respected |
|---|---|---|
| 1 | 55.0% (n=2,946) | 56.0% (n=4,123) |
| 2 | 58.8% (n=4,342) | — |
| 3+ | 59.3% (n=18,191) | 58.6% (n=9,697) |
| **gap 3+ vs 1** | **+4.4%** (p=0.0002) | **+2.6%** (p=0.0072) |

The fib confluence effect is real *and* **random lines reproduce 2.6 of the 4.4 points**. Crowded
zones hold better whether the lines are Fibonacci or arbitrary, because a region where levels pile
up is a region where price has already ranged — and ranges keep ranging.

Residual after the control: **+1.8%**, fib-specific, small, and with no significance test on the
difference-of-differences. Not something to build on.

## Gann fans: worse than random

Fan lines from confirmed pivot lows at slopes 0.25/0.5/1/2/4 ATR per bar, against random levels
with the *same one-bar lifetime* (the first version of this control let random levels live forever
and therefore got 2.4M tests against Gann's 2,330 — an unfair comparison that had to be fixed):

```
gann    56.4% (n=2,330)
random  61.0% (n=  961)
gann − random  −4.6%   p = 0.0172
```

Method note that limits any Gann conclusion: the 1×1 angle assumes one unit of price per unit of
time, and **the units are unspecifiable**. The scaling *is* the method. ATR-at-the-pivot is the
least arbitrary choice available, but it is a choice, and a different one gives different lines.

## The one real finding here is about volume, not levels

| volume at the test | respected |
|---|---|
| Q1 (0.0–0.8× median) | **61.0%** |
| Q2 (0.8–1.1×) | 59.9% |
| Q3 (1.1–1.7×) | 57.8% |
| Q4 (1.7–16.6×) | **56.4%** |

**Monotone across all four quartiles. Q1 vs Q4 = +4.6%, p = 0.0002**, ~6,370 tests per bucket.

**A level tested on high volume breaks more often.** Volume at a level is a *break* signal, not a
defence — the opposite of how confluence is usually taught ("heavy volume defended the level").
This is consistent with `VOLUME_FINDINGS.md`, where volume confirmed direction in crypto rather than
marking exhaustion.

Note what it is *not*: it does not make the level predictive. It says that given a touch, volume
tells you which way it resolves — and the effect is 4.6 percentage points, so it shifts a coin flip,
it does not replace one.

## Caveats

- BTC only. Should be repeated on equities before generalising.
- One span (10), one tolerance (0.25 ATR), one horizon (10 bars). The null is so flat that a
  parameter sweep is unlikely to rescue fibs, but the volume result deserves one.
- Gann's ATR-unit choice is a free parameter, as above.
- Many tests were run; the volume result is the only one that is both large and monotone.
