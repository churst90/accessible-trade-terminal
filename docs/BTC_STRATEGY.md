# A Bitcoin strategy from verified parts — and it is one ingredient, not two

> **SUPERSEDED IN PART, 2026-07-31.** The walk-forward in `WALKFORWARD_FINDINGS.md` shows the 6×
> headline below is IN-SAMPLE: the 50/+1/+0.5 parameters were chosen by sweeping this same history.
> Honest out-of-sample is **~1.5–1.8× hold**, and a RANDOM parameter pick beat the optimiser.
> The mechanism and the "trade the daily" recommendation stand; the specific parameter values do not.

Run 2026-07-31. `dotnet run -- btcstrat --only BTC_USDT`.

## The ingredient list

For crypto, exactly two things have survived their own controls in this lab:

1. **Trend.** Crypto trends — measured five independent ways (variance ratio 1.150 vs 0.820 for
   equities). The Trading Cross z-state cleared an exposure-matched timing null at p = 0.001.
2. **The volume–return correlation.** Top-minus-bottom quintile +1.26 ATR (p = 0.0002), and it
   survived *inside every trailing-return tercile* — the only input found that adds information
   beyond trend in crypto.

Everything else commonly proposed has been tested here and failed on crypto: Fibonacci and Gann
score identically to random lines, market-structure labels are indistinguishable from random,
Cipher SR proximity was a lookahead artifact, cycles are a swing-detector artifact, crowding and COT
carry no forward information, and the RSI dip-buy worked only in equities and failed noise injection
even there.

## The result: trend alone

Rule: rolling z-score of log close over 50 bars; **long when z crosses above +1σ, flat when it
crosses below +0.5σ.** Signals read at bar *i*, filled at bar *i+1*'s close.

| tf | strategy | buy & hold | exposure | strategy maxDD | hold maxDD | trades | p (exposure-matched) |
|---|---|---|---|---|---|---|---|
| 4h | 53.4× | 19.8× | 37% | **38%** | 84% | 331 | **0.0010** |
| **1d** | **3,376×** | **560×** | 41% | **53%** | 85% | **64** | **0.0005** |
| 2d | 212× | 206× | 44% | **51%** | 83% | 31 | **0.0115** |

Beats hold on 4h and 1d, ties on 2d, and **clears the exposure-matched null on all three** — so it
is picking better days, not merely being invested less.

**Drawdown is roughly halved on every timeframe.** That is the mechanism, and it is the same one
`TRADING_CROSS_FINDINGS.md` identified: this is loss avoidance, not return capture.

### Costs — the daily is the sweet spot

| bps/side | 4h | 1d | 2d |
|---|---|---|---|
| 0 | 53.4× | 3,376× | 212× |
| 10 | 27.6× | 2,971× | 199× |
| 25 | 10.2× | **2,451×** | 181× |

64 trades in 13 years makes the daily almost cost-immune. The 4h version does 331 trades and loses
80% of its edge by 25 bps — it is the same rule but a different business.

### Eras — the daily wins all three

| era (1d) | trend | hold |
|---|---|---|
| 2013-04 → 2017-09 | **49.9×** | 39.2× |
| 2017-09 → 2022-01 | **27.8×** | 8.0× |
| 2022-01 → 2026-06 | **2.4×** | 1.8× |

4h wins two of three; 2d wins one of three. Only the daily is clean.

## A presentation bug that inverted the answer

The first run measured buy-and-hold from bar 0 while the strategy warmed up for 610 bars. On BTC
that gap covers roughly $10 → $1,000 — a stretch the strategy was never allowed to trade. Hold
therefore read 6,046× against the strategy's 3,376×, and the rule looked like a loser. Measured over
the same window hold is **560×** and the rule beats it **6×**. One line of code, opposite conclusion.

## Volume does not earn its place

Per-bar-in-market log return, trend alone vs trend + volume filter:

| tf | trend | trend + volume | verdict |
|---|---|---|---|
| 4h | +0.00054 | +0.00045 | worse |
| 1d | +0.00368 | +0.00366 | no change |
| 2d | +0.00451 | +0.00556 | better (30 trades — noise) |

The volume signal cuts exposure from ~41% to ~14% and does not improve the rate. One timeframe out
of three shows a gain, and it is the one with the fewest trades.

**This is the third time a genuine conditional relationship has failed to convert into a rule** —
after MVRV and NVT. The pattern is now established enough to be a rule of its own: *"bars with
property X had better forward returns" is an exposure statement. Only beating an exposure-matched
null makes it a timing statement.* The volume result is real as a description and useless as a
filter, because it correlates 0.43–0.59 with trailing returns — once trend is in the book the
incremental information is already spent.

## Why it works

1. **Crypto trends.** Extension predicts continuation, not reversion. The rule buys strength, which
   is the correct polarity for the asset class.
2. **The asymmetry is the whole design.** Entry demands +1σ of proof; exit needs only a fall to
   +0.5σ. Slow in, fast out. That is a loss-avoidance function, not a return-maximising one.
3. **The payoff comes from the drawdowns it misses.** Bitcoin's buy-and-hold drawdown is 85%.
   Cutting that to 53% leaves capital to compound. This is why the same rule fails on equities
   (0.23× on SPY) — a 50% drawdown is not deep enough for the protection to be worth 60% time out.

## What this is not

- **Not validated out of sample.** Every number is in-sample over the full history. No forward test.
- **One asset, one parameter set** — though the Trading Cross work found the same parameters beat
  hold on 10 of 10 crypto assets, which is meaningful cross-sectional support.
- **Not a "works on all timeframes" result.** It works on the daily, degrades on 4h once costs are
  real, and ties hold on 2d.
- **It underperforms hold in strong bull runs** and earns its total in the bears. Expect to look
  wrong for long stretches.

## Recommendation

Trade the **daily**. z(50), long above +1σ, flat below +0.5σ. Roughly 5 trades a year, 41% time in
market. Expect ~6× hold over a full cycle with about 60% of the drawdown — and expect to
underperform visibly during the parabolic phase of any bull market.

Do not add volume, fibs, structure or oscillators to it. Each has been tested here and each either
adds nothing or subtracts.
