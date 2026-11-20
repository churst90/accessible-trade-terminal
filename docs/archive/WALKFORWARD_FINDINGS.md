# Walk-forward on the BTC trend rule — the family works, the optimisation does not

Run 2026-07-31. `dotnet run -- walkfwd --only BTC_USDT --tf 1d --folds 6`. Anchored walk-forward,
175-combo grid (window × entry × exit), fit window always starts at bar 0 and grows; each choice is
applied unchanged to the block immediately after it.

## Why the previous number did not count

The 50/+1/+0.5 settings in `BTC_STRATEGY.md` were chosen by sweeping the whole history. Reporting
their full-sample 6× is circular — the sample selected the parameters and then graded them.

## Result

| fold | OOS window | fitted pick | best | plateau | fixed* | **random params** | hold |
|---|---|---|---|---|---|---|---|
| 1 | 2017-07 → 2019-01 | 20/1.25/0.5 | 2.58× | 2.12× | 2.13× | 2.61× | 1.30× |
| 2 | 2019-01 → 2020-07 | 20/1.25/0.25 | 2.62× | 2.40× | 3.49× | 1.85× | 2.58× |
| 3 | 2020-07 → 2022-01 | 20/1.25/0.25 | 3.22× | 2.67× | 5.81× | 4.02× | 5.17× |
| 4 | 2022-01 → 2023-06 | 20/1.25/0.5 | 0.81× | 0.86× | 1.14× | 1.05× | 0.63× |
| 5 | 2023-06 → 2024-12 | 20/1.25/0.5 | 2.02× | 1.83× | 2.42× | 2.26× | 3.31× |
| 6 | 2024-12 → 2026-06 | 80/0.75/0.5 | 0.96× | 0.96× | 0.88× | 0.90× | 0.63× |

**Compounded out-of-sample:**

| selection rule | total | vs hold | beat hold |
|---|---|---|---|
| best-by-return (fitted) | 34.22× | **1.48×** | 4/6 |
| plateau centre (fitted) | 20.57× | **0.89×** | 3/6 |
| **random parameters** | **41.69×** | **1.80×** | 3/6 |
| buy & hold | 23.11× | 1.00× | — |
| *fixed 50/1/0.5* | *104.73×* | *4.53×* | *5/6* |

\* **`fixed` is contaminated and must not be read as out-of-sample.** 50/1/0.5 was chosen by
sweeping the whole history, which contains every OOS block above. It is the in-sample number wearing
a walk-forward costume, and it is in the table only to show how large that distortion is: 4.53×
against an honest 1.48×.

## The finding: a random parameter pick beats the optimiser

**41.69× for random parameters against 34.22× for best-by-return and 20.57× for plateau-centre.**
Averaged over 200 random grid picks per fold, so it is not one lucky draw.

The grid search is reading noise. And the stability table makes that sharper rather than softer —
best-by-return chose window 20 in five of six folds and entry 1.25 in five of six. **The pick was
stable and still worse than random.** Stability of a fitted parameter is not evidence that the
parameter is right; it can just mean the noise has a persistent shape.

The plateau rule — the one Varma explicitly recommends ("you're looking for the most stable rate of
return, not the highest") — did **worst of all**, underperforming buy-and-hold at 0.89×. Six folds
is a small sample and this should not be treated as a refutation of the method in general, but it
gets no support here.

## What survives

**The family works; the tuning does not.** Random parameters still returned **1.80× hold** out of
sample. Any reasonable trend-following parameterisation of Bitcoin beat holding across these six
blocks — which is consistent with everything else in this lab about crypto trending
(`POLARITY_AND_GATE_FINDINGS.md`, variance ratio 1.150).

So the honest claim is not "z(50), +1/+0.5 returns 6× hold". It is:

> **Trend-following Bitcoin beats holding it by roughly 1.5–1.8× out of sample, at about 40%
> exposure and roughly half the drawdown, and the specific parameters do not matter — attempting to
> optimise them made it worse.**

That is a much smaller claim and a much more durable one.

## What this changes about `BTC_STRATEGY.md`

The 6× headline there is in-sample and should be read as ~1.5–1.8×. The mechanism section stands
(crypto trends, asymmetric entry/exit, the payoff is the avoided drawdown). The recommendation to
trade the daily stands, because the daily's low turnover is what makes it cost-immune. **The
recommendation to use 50/1/0.5 specifically does not stand** — pick something reasonable in the
middle of the grid and stop tuning.

## Caveats

- Six folds on one asset. The random-beats-fitted result is clear but rests on six blocks.
- Anchored (expanding) fit only; a rolling fixed-length fit might behave differently.
- Costs are not modelled here — but the fitted picks favour window 20, which trades far more than
  window 50, so including costs would widen the gap against the optimiser rather than narrow it.
- No transaction-cost or slippage model in the OOS blocks.
