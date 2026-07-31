# Crowding as a conditioner — a clean null, and a defect in the indicator

Run 2026-07-30. `dotnet run -- crowding`. 7 crypto perpetuals with funding + open-interest
feeds (ADA, BTC, DOGE, ETH, LTC, SOL, XRP), 2020-11 → 2026-05, 10,655 bar observations.

## Why this and not another confluence stack

S/R, Fibonacci, swing points, candle patterns, market structure and the Cipher oscillators are all
deterministic transforms of one OHLC series. Agreement between them is arithmetic, not evidence.
This repo already found that twice — `CrowdingIndexProvider`'s own notes record eight versions of
pure-Cipher confluence walk-forwarding to break-even "because price-derived indicators are
auto-correlated", and `GateCommand` found a z-score gate was *structurally incapable* of being open
at a dip-buy signal.

Crowding was the one candidate that is not a price transform: funding rate (what leveraged traders
pay to hold a side) plus open-interest change (how many of them there are). Rishi Narang's taxonomy
calls this "technical sentiment" and says it can be traded directly or used as a conditioner on
trend and reversion. That was the hypothesis.

**The prediction that could fail:** crowding is signed so positive = longs piled in. Long entries
should be worse when it is high (consensus trade, squeeze risk) and better when deeply negative.

## Result: nothing, at any horizon

| horizon | spearman | p | bottom decile | top decile | spread (ATR) | symbols negative |
|---|---|---|---|---|---|---|
| 1 bar | −0.0081 | 0.457 | +0.02 | +0.07 | −0.05 | 4/7 |
| 3 bars | −0.0047 | 0.714 | 0 | +0.18 | −0.18 | 4/7 |
| 5 bars | −0.0085 | 0.561 | +0.02 | +0.27 | −0.25 | 4/7 |
| 10 bars | −0.0058 | 0.742 | +0.03 | +0.57 | −0.54 | 4/7 |
| 20 bars | +0.0080 | 0.687 | +0.22 | +1.06 | −0.85 | 3/7 |
| 40 bars | +0.0011 | 0.953 | +0.56 | +1.69 | −1.13 | 4/7 |

Every correlation is inside ±0.009. Every p is above 0.45. Symbols split like coin flips.
Horizons 1–40 were tested precisely so that a null at 20 could not be mistaken for a null
everywhere — funding settles every eight hours and a squeeze resolves in days.

p-values use a **circular-shift null**: the crowding series is rotated against the returns by a
random offset. Overlapping forward windows make consecutive observations heavily dependent, and
shuffling either series would destroy its own autocorrelation and build a null far too narrow.
Rotating keeps both intact and randomises only their alignment.

### The spread runs the wrong way

The decile spread is **negative at every horizon** — the most long-crowded decile has the *highest*
forward returns, by 1.13 ATRs at 40 bars. That is the opposite of fade-the-crowd. The rank
correlation is still zero, so the relationship is not monotone; it lives in the extremes.

The provider's documented levels fare no better. At its own ±2 thresholds, on unconditioned bars:
crowding ≤ −2 returns **−0.075R** and ≥ +2 returns **+0.065R** — backwards from "crowding ≥ +2 →
reversal-down probability elevated", though at p = 0.43 the honest reading is "no support" rather
than "reversed".

### As a conditioner it fails both controls

| signal | n | crowding lift | excess over random | vs 200-MA lift |
|---|---|---|---|---|
| trend-long (20-bar breakout) | 541 | +0.033R | **+0.004R** | +0.064R |
| revert-long (RSI-30 bounce) | 118 | −0.073R | **−0.102R** | — |
| *random entries* | *1,524* | *+0.029R* | — | *−0.016R* |

Random entries get +0.029R from the same filter, so neither signal's lift is its own. The 200-bar
moving average — one line of arithmetic, price-only — beats crowding on the trend arm.

These arms are underpowered (118 reversion trades). **The all-bars test is not**, and it is the one
that carries the verdict.

## The defect: it is not as non-price as documented

`CrowdingIndexProvider` is built on `funding_z + sign(close[i] − close[i−1]) × oi_delta_z`, and its
docstring justifies it as something "pure-price strategies cannot replicate from any combination of
Cipher/RSI/MACD, no matter how cleverly weighted."

Measured:

| | spearman with crowding |
|---|---|
| trailing 1-bar return | +0.011 |
| **trailing 5-bar return** | **+0.194** |
| **trailing 20-bar return** | **+0.197** |
| *forward return, any horizon* | *±0.009* |

It rank-correlates ~0.19 with where price has just been and ~0.00 with where price is going —
funding runs positive through sustained rallies, so a high reading substantially means "price went
up recently". It is a backward-looking description of a move that already happened. The orthogonality
that justified preferring it over an oscillator is partly not there, and the predictive content is
not there at all.

**This does not mean the indicator is broken** — as a *description* of positioning it does what it
says. It means the documented trading interpretation is unsupported, and any strategy leafing on
those ±2 levels is leafing on an untested claim.

## Scope and caveats

- 7 symbols, all crypto perpetuals. Open-interest history begins 2021-12, so the window is
  effectively one crypto cycle. A conditioner that only works across cycles would not show here.
- Only the composite index was tested. Raw funding and raw OI separately, COT positioning, and
  Fear & Greed are all in the snapshot set and untested.
- Long entries only.

## What this closes

Three conditioners have now been tested against the same two controls: a z-score regime state
(`GateCommand`), a 200-bar moving average, and crowding. **Only the moving average survived**, and
only on mean-reversion entries in equities — see `POLARITY_AND_GATE_FINDINGS.md`.

The proposed architecture of stacking S/R + fibs + structure + Cipher + crowding has no support
here at any layer: five of those inputs are the same data re-drawn, and the sixth — the only one
carrying genuinely new information — does not predict returns.
