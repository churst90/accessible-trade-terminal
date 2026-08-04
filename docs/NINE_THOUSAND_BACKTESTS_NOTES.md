# "9,000 backtests" — Brendan, and what it is and is not worth

Source: <https://www.youtube.com/watch?v=nLQhKkjkuWI> · analysed 2026-08-03 · transcript 4,143 words.

Presenter: Brendan — maths/econ at UCLA, three years investment banking at Raymond James, two years
building trading systems. Sells an AI-trading community; the video's closing third is a funnel for
it. That is not disqualifying, but it is context for how the results are framed.

**Short verdict: this does not do our work for us.** Its headline conclusion is one this project has
already measured more carefully, its central robustness test is one that cannot fail, and its
universe quietly guarantees the answer it reports. Two things in it are genuinely worth taking.

---

## What he actually did

| | |
|---|---|
| Scope | 9,000 backtests, 30 assets, 15 years, **daily bars only** |
| Universe | Major ETFs (SPY/QQQ), sector ETFs, gold, oil, bonds, **BTC and ETH**, large caps (AAPL, NVDA) |
| Method | Walk-forward: tune on older data, test on newer |
| Funnel | Sharpe > 0.5 out-of-sample → **1,218**; max drawdown < 35%; in-sample-vs-out overfit filter; minimum trade count → **524**; then assets with 10+ years of history → **478** |
| Families | Trend, mean reversion, momentum, breakout, volume, volatility, pattern |

**Headline claims**

1. **Mean reversion is the only family positive on average.** 64% of the 478 survivors.
2. **Only 44%** of strategies that looked strong in-sample stayed strong out-of-sample.
3. RSI mean reversion survived on **20 different tickers**; Keltner reversion on 18.
4. Trend works but is *situational* — the single best survivor was in fact a trend strategy
   (Turtle on AAPL, score 1.18).
5. Single-asset momentum scored **≈ zero**; **cross-sectional momentum scored "way better"**.
6. Proposed build order: base signal → risk and sizing → uncorrelated signals → regime switch (HMM).

---

## Where it agrees with us, independently

**Cross-sectional momentum beats single-asset momentum.** This is our strongest result
(`XSMOMENTUM_FINDINGS.md`: equities 8/8, monotone in lookback, per-period spread p = 0.0045) and our
time-series verdict is "the family works, the tuning does not" (`WALKFORWARD_FINDINGS.md`). He
reaches the same ordering from different data and a different code path. **Independent agreement on
direction is worth something** — though he reports no number for the cross-sectional arm, so it is a
claim rather than a measurement.

**Mean reversion dominates in an equity-heavy universe.** Consistent with the polarity fork
(`POLARITY_AND_GATE_FINDINGS.md`: equities VR20 0.82, crypto 1.15) and with POC mean reversion being
real in equities at ~5 days.

---

## Where it is wrong, or cannot support what it claims

### 1. The universe guarantees the headline

Of 30 assets, roughly 28 are equities, sector ETFs, bonds or commodities and **two are crypto**. Our
polarity finding is that equities revert and crypto trends — measured five independent ways and the
most robust result in this project.

So "mean reversion is the only family that survives" is **what that universe was always going to
say**. Run the same 9,000 tests on 30 crypto assets and the conclusion inverts. He has BTC and ETH in
the sample and **never breaks results out by asset class** — which is the single most informative cut
available to him and the one that would have caught this.

The finding is real *for that universe*. The generalisation to "mean reversion is the one that
broadly holds up" is an artifact of what was in the basket.

### 2. The robustness test cannot fail

> *"We took every surviving strategy's trades and then reshuffled them 500 times."*

This is verbatim the trap recorded in our own research skill: **shuffling a strategy's own returns
cannot fail, because order was never the question — the trade set was selected by the signal.** A
strategy that cherry-picked fifty lucky trades still looks fine when those same fifty trades are
reshuffled. The correct move is to shuffle the *input* and re-derive the trades.

To be fair to it, the test is not useless — it is a legitimate **path-dependency / drawdown-realism**
check, and it did catch something real (dual-momentum on NVDA showing 61% drawdown once resequenced,
meaning the clean equity curve depended on the exact order history happened to take). But it is
presented as evidence the survivors have an edge, and it cannot be that.

### 3. No random arm anywhere, which makes the funnel uninterpretable

**524 of 9,000 survived. How many would survive by chance?** Nothing in the video answers this, and
without it the number carries no information. At a Sharpe > 0.5 out-of-sample threshold over 9,000
tests, a substantial number of false survivors is the *expected* outcome. He mentions "multiple test
corrections" as a line in a prompt and never reports one.

This is the control that has changed the most verdicts here. Our BTC walk-forward found **random
parameter picks beat the optimiser out of sample** (1.80× vs 1.48×) — without that arm, the fitted
result reads as a success instead of a failure to beat a coin flip.

### 4. Other missing controls

- **No exposure-matched null.** Mean reversion is a partial-exposure rule; beating hold on Sharpe
  while out of the market half the time is a different claim, and block-bootstrap-style surrogates
  have a null median near 0.05, so almost anything clears them.
- **No per-era breakdown.** Fifteen years pooled; our regime work shows 2013–2020 was the calmest
  stretch in fifty years, so pooling hides a great deal.
- **Sharpe as the primary filter** rewards low volatility rather than edge, and our exit work found
  the return distribution is fat-tailed enough that mean/variance measures mislead (BTC trend trades
  average +8.15R at a 47% win rate).

---

## What is worth taking

**1. Breadth as an explicit filter.** "RSI reversion survived on 20 unrelated tickers" is a good
robustness idea and stronger than any single p-value — it is our *"check correlated instruments
against each other"* control, generalised. We do per-symbol breakdowns but have never formalised
**breadth count** as a pass/fail criterion. Cheap to add to the edge registry as a field: on how many
independent instruments did this survive?

**2. The in-sample→out-of-sample survival rate as a calibration number.** **44%** of good-looking
strategies stayed good. That belongs beside Narang's "successful out-of-sample R² is 0.03–0.04" as a
number to judge our own results against. It is also a decent public answer to "why does my backtest
stop working".

**3. The layering framework** — base signal, then sizing, then uncorrelated signals, then regime — is
sensible and matches how our own ledger is organised. Unremarkable but correct.

---

## What we should NOT take

- **The regime/HMM layer as a fix for momentum.** He proposes detecting regime and switching between
  momentum and mean reversion. We have tested the adjacent claim: a z-state regime gate was **null**,
  crowding carried **no forward information at any horizon**, and the one conditioner that survived
  (`close > SMA(200)`) later **failed noise injection**. A hidden Markov model is a more elaborate
  version of the same idea and would need the same controls before it means anything.
- **The survivor list itself.** 524 strategies selected by six filters with no random arm is a
  candidate list, not a result.

---

## The honest bottom line

He built a competent **pipeline** and reported it clearly. The pipeline's outputs are not evidence at
the standard this project uses, for one structural reason: **every filter he applied removes bad
strategies, and none of them tests whether the survivors are distinguishable from luck.** A funnel
that only narrows cannot tell you what the noise floor is.

Where his conclusions overlap ours they are a welcome independent check. Where they extend beyond
ours — "mean reversion broadly holds up" — the universe composition explains the result without any
edge being present.

Cross-references: `ALPHA_LEDGER.md` · `XSMOMENTUM_FINDINGS.md` · `POLARITY_AND_GATE_FINDINGS.md` ·
`WALKFORWARD_FINDINGS.md` · `.claude/skills/strategy-research`.
