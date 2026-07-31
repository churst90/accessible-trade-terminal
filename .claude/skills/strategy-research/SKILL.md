---
name: strategy-research
description: Use when testing a trading hypothesis, evaluating a strategy claim from a video/book/paper, or interpreting StrategyLab results. Covers the required controls, the traps that have produced false results in this repo, and the standing findings so old ground is not re-tested.
---

# Testing a trading hypothesis

The job is to find out whether a claim is true, not to make it work. **A well-executed null is the
product.** Most of this repo's research output is nulls, and they are what stop a plausible,
expensive strategy from being traded.

Calibration, from a working quant (Samir Varma, money manager since 1993): a *successful*
out-of-sample R² in professional quant is **0.03–0.04**, where 0 is a coin flip. A "considerable
edge" mostly does not exist. Judge results against that, not against a backtest curve.

## The workflow

1. **State the thesis in one falsifiable sentence** before writing code — what specifically would
   have to be true, and what result would prove it false. If you cannot state the failing outcome,
   you are not testing anything.
2. **Ask what the cheapest thing is that would produce the same result without the claimed
   mechanism.** Build that as a *named control in the output*, not as an afterthought. This step
   has killed four of the last six theses here.
3. Implement as a StrategyLab command (see the `strategy-lab` skill).
4. **Report whatever it says.** Print the controls next to the result, always.

## Required controls

Pick the ones that apply. Each has changed a verdict here.

- **Random-entry / detrend baseline.** What does a coin flip get from the same filter? Without it,
  "longs did better when the filter was on" is a statement about the market, not the filter. This
  turned the 200-MA dip result from a tautology into a real number (signal +0.107R, random +0.007R).
  Varma describes the same control as detrending — subtract the index's average drift and re-run.
- **A cheap alternative that does the same job.** Benchmark any new regime/conditioner signal
  against `close > SMA(200)`. This killed the Trading Cross gate outright.
- **Exposure-matched timing null.** For any partial-exposure rule: same number of days in market,
  chosen as random *contiguous blocks* instead of by the signal. This is the test that carried the
  Trading Cross (p=0.001) when a block-bootstrap could not.
- **Circular-shift null** for two autocorrelated series (e.g. an indicator vs overlapping forward
  returns). Rotate one against the other by a random offset. Shuffling either destroys its own
  serial structure and builds a null far too narrow.
- **Freedman–Lane permutation** for partial correlations: permute the residuals of a~c, not b.
  Permuting b destroys the b~c relationship too, which is not the null being tested.
- **Within-class / demeaned tests.** A pooled cross-sectional correlation can be the asset-class
  label wearing a number.
- **Noise injection** (Varma). Add noise to the input prices; a real edge degrades *slowly*, a
  fitted one collapses. Look for a **plateau, not a peak**, in any parameter sweep.
- **Per-symbol and per-era breakdown.** A pooled number can be one symbol or one regime.

## Traps that have produced false results here

- **Shuffling a strategy's own returns cannot fail.** Order was never the question — the set was
  selected by the signal. Shuffle the *input*.
- **Block-bootstrap surrogates for a partial-exposure rule** have a null median near 0.05, so almost
  anything that beats hold clears them. Use the exposure-matched null instead.
- **Retrospectively-selected features are circular.** If cycle lows / levels / patterns are picked
  by looking back, they will have the claimed spacing *because they were chosen that way*. Define
  them algorithmically with a fixed confirmation lag, then compare against phase-randomized
  surrogates.
- **Signal and conditioner derived from the same series.** Check the overlap *before* interpreting.
  "The filter rejected everything" may be arithmetic, not a finding — a z-gate that opens above +1σ
  can never be open at a dip-buy signal.
- **Full-sample max drawdown as a cross-sectional variable** grows with sample length. Use a rolling
  calendar-year window.
- **The same instrument from two providers** double-counts correlated errors and narrows every
  p-value. Dedupe by symbol.
- **Confirmation lookahead.** Indicators that anchor a level at a pivot only knowable N bars later
  must be shifted right by N in a backtest.
- **A test that shares the code's misunderstanding** passes and is worse than no test.
- **Undefined ≠ false.** A conditioner that is NaN during warmup must be excluded, not counted as
  its "off" state — that silently loads every early observation onto one side.
- **Survivorship.** The snapshot universe is all survivors. Any cross-sectional or long-horizon
  result is biased upward. Say so explicitly.
- **Model the signal + entry + exit** (Varma). A momentum result can be entirely consumed by
  realistic execution. Shorting backtests additionally need hard-to-borrow and cost-to-borrow data.

## Standing findings — do not re-derive

- **Asset-class polarity is real and large.** Crypto trends (VR20 1.150), equities revert (0.820).
  But no single continuous variable reproduces it: drawdown depth and realised volatility
  rank-correlate 0.96 and neither partial clears, and the sign *reverses* inside crypto. Keep the
  hard class fork; do not ship a continuous polarity switch. (`docs/POLARITY_AND_GATE_FINDINGS.md`)
- **Price-derived confluence does not stack.** Eight versions of pure-Cipher confluence
  walk-forwarded to break-even; structure labels were indistinguishable from random. S/R, fibs,
  swing points, candle patterns, market structure and the Cipher oscillators are all transforms of
  one OHLC series — agreement between them is arithmetic, not evidence.
- **Three conditioners tested, one survived.** z-state regime gate: no. Crowding (funding+OI): no
  forward information at any horizon 1–40 bars. `close > SMA(200)`: **yes**, +0.10R/trade over a
  random baseline (p=0.0002), but *only* on mean-reversion entries and *only* in equities.
  (`docs/CROWDING_FINDINGS.md`)
- **The Trading Cross** (z-score momentum) is real but is drawdown-avoidance: 10/10 crypto beat
  hold, 0/3 traditional. (`docs/TRADING_CROSS_FINDINGS.md`)
- **Failed outright:** all four Cosasverdes claims; Cipher SR proximity (lookahead artifact).

## Hypothesis generation from hindsight

Legitimate, with one rule. Labelling the *optimal* entries/exits with perfect foresight and then
asking "was anything visible **before** that moment which identifies them?" is sound hypothesis
generation. It is never validation. If no causal feature predicts the oracle labels, the setup was
random.
