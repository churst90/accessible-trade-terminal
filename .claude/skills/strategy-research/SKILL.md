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
- **Noise injection** (Varma). Perturb every log return by gaussian noise scaled to the series' own
  daily volatility, rebuild the path, re-run — several draws per level. A real edge degrades
  *gradually*; a fit keyed to the exact path collapses at the first perturbation. Cross-sectional
  momentum retained 86% at 25% noise and 62% at 50%. Also look for a **plateau, not a peak**, in any
  parameter sweep.
- **Costs behave differently for relative and absolute claims.** A *lift* (gated vs ungated) is
  nearly cost-invariant because costs hit both arms and cancel; the *absolute* return of the thing
  being filtered is not. Report both — the dip filter's lift survived 10 bps while its absolute
  return more than halved.
- **Convert per-trade costs into R before judging.** Risking one ATR means the position is
  `1 ÷ (ATR÷price)` times the risk unit — at a 1.71% ATR, a 10 bps round trip costs 0.117R, which
  can be the entire edge. Basis points against notional are not basis points against R.
- **Costs change the benchmark, not just the number.** Against a random book both sides churn, but
  nobody redraws a random portfolio monthly — the realistic alternative is holding the basket at
  near-zero turnover. Report turnover, compute the break-even cost, and re-benchmark against the
  thing a person would actually do instead.
- **Express any assumed rate ANNUALLY before believing it.** A survivorship stress modelled as
  "2 names lose 20% per rebalance" compounded into a basket down 99% over 215 months. Converted to
  an annual rate it was obviously absurd. Any per-period hazard should be sanity-checked in the
  units the real world quotes it in.
- **Per-symbol and per-era breakdown.** A pooled number can be one symbol or one regime.
- **Average the control, don't sample it.** A single random draw over a long window is a sample from
  a very wide distribution, not a baseline. The first cross-sectional run used one random book and
  produced a control swinging from +79% to +476%; averaging 400 books made it stable at ~210%.
- **Surrogate the DETECTOR, not just the signal.** When a claim depends on features a rule finds
  (swing lows, pivots, levels), run the same rule on return-shuffled random walks. If the surrogate
  reproduces the feature, the feature belongs to the detector.
- **Check correlated instruments against each other.** Two ~90%-correlated indices giving opposite
  significant answers is proof of a sample artifact, and it is more convincing than any p-value.
  Where a signal should generalise, test somewhere it must agree.
- **Never reconstruct event dates from memory.** If FOMC/CPI/earnings dates are not in the dataset,
  fetch them or skip the test. Fabricated data at the centre of a result is worse than no result.
- **Count your tests.** Running 4 assets × 2 claims and reporting the one p = 0.03 is a false
  positive waiting to happen — at α = 0.05 you expect 0.4 of them. Say how many tests were run and
  what a corrected threshold would be.

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
- **A signal correlated with trailing return may just be momentum renamed.** Check it, then re-test
  inside trailing-return buckets. The crowding index claimed orthogonality to price and correlated
  0.19 with trailing returns; the volume signal correlates 0.43–0.59 and only *survived* that check
  in crypto. Always run it before believing a new "non-price" input.
- **Judging a grid on a flat count can reject a confirmed prediction.** If the literature says short
  lookbacks should fail, their failing is evidence *for* the hypothesis. Read the structure across
  the parameter axis, not the tally.

## Standing findings — do not re-derive

- **Asset-class polarity is real and large.** Crypto trends (VR20 1.150), equities revert (0.820).
  But no single continuous variable reproduces it: drawdown depth and realised volatility
  rank-correlate 0.96 and neither partial clears, and the sign *reverses* inside crypto. Keep the
  hard class fork; do not ship a continuous polarity switch. (`docs/POLARITY_AND_GATE_FINDINGS.md`)
- **Price-derived confluence does not stack.** Eight versions of pure-Cipher confluence
  walk-forwarded to break-even; structure labels were indistinguishable from random. S/R, fibs,
  swing points, candle patterns, market structure and the Cipher oscillators are all transforms of
  one OHLC series — agreement between them is arithmetic, not evidence.
- **Three conditioners tested, one survived — and it later failed its robustness pass.** z-state
  regime gate: no. Crowding (funding+OI): no forward information at any horizon. `close > SMA(200)`
  on mean-reversion entries in equities: +0.10R/trade over a random baseline (p=0.0002), but it
  **collapses under noise injection** (21% retained at 25% noise vs 86% for cross-sectional
  momentum), one era shows nothing, and survivorship biases it the flattering way. A sensible
  default, not an edge. (`docs/CROWDING_FINDINGS.md`, `docs/POLARITY_AND_GATE_FINDINGS.md`)
- **The Trading Cross** (z-score momentum) is real but is drawdown-avoidance: 10/10 crypto beat
  hold, 0/3 traditional. (`docs/TRADING_CROSS_FINDINGS.md`)
- **Cross-sectional momentum WORKS in equities — the strongest result here.** Rank 39 names by
  trailing return, hold the top third: beats a random-selection portfolio 8/8 at 180–365d lookbacks,
  monotone in lookback, per-period spread p = 0.0045. Null in crypto (underpowered, 10 names) and
  null in a mixed universe even vol-normalised — never rank a trending class against a reverting
  one. Not yet costed or delisting-adjusted. (`docs/XSMOMENTUM_FINDINGS.md`)
- **Volume confirms in crypto, reverses in equities.** Trailing return/volume correlation, top-minus-
  bottom quintile: crypto +1.26 ATR (p=0.0002, survives inside every trend tercile), equity −0.19 ATR
  (p=0.0002). "20× volume day = capitulation = buy" is rejected and runs backwards in crypto and
  commodities. (`docs/VOLUME_FINDINGS.md`)
- **The 60d/40d cycle is a swing-detector artifact.** Return-shuffled surrogates reproduce the cycle
  length on every asset and land in the claimed timing band more often than real data. Mean gap is
  near-linear in the detector's span. Translation (high late vs early) is momentum in cycle
  vocabulary and splits crypto/equity like everything else. (`docs/CYCLE_FINDINGS.md`)
- **On-chain value metrics beat their price baselines** — the first non-price family with anything
  in it. MVRV is monotone across z quintiles (−1.11 ATR, p=0.0002) while its matched price/SMA
  baseline predicts nothing (0.00, p=0.986) despite correlating 0.752 with it. NVT likewise. High
  MVRV predicts HIGHER returns — the folklore's "expensive = sell" imports a mean-reversion
  assumption crypto does not satisfy. Not robustness-passed. (`docs/ONCHAIN_FINDINGS.md`)
- **Positioning is null from both available sources.** Exchange funding/OI and regulated CFTC COT.
  On COT, the S&P and Nasdaq — ~90% correlated indices — gave *opposite* signals at p=0.0002 and
  p=0.017. Stop testing positioning. (`docs/POSITIONING_AND_EVENTS_FINDINGS.md`)
- **The asset-class polarity has now been measured five independent ways** — POC deviation, Value
  Deviation, the Trading Cross, volume, and cycle translation. It is the most robust finding here.
- **Failed outright:** all four Cosasverdes claims; Cipher SR proximity (lookahead artifact).

## Hypothesis generation from hindsight

Legitimate, with one rule. Labelling the *optimal* entries/exits with perfect foresight and then
asking "was anything visible **before** that moment which identifies them?" is sound hypothesis
generation. It is never validation. If no causal feature predicts the oracle labels, the setup was
random.
