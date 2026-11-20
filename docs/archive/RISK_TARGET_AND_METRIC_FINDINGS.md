# Targets, streaks, sizing, and the 0–1 risk metric — four claims tested

Run 2026-08-01, continuing the queue from `APPROACH_AND_WEEKLY_FINDINGS.md`. Three claims come from
the trader's-triangle interview; the fourth is the rainbow risk metric.

```
dotnet run --project AccessibleTrader.StrategyLab -- targets     --snapshots strategy-lab-data --tf 1d
dotnet run --project AccessibleTrader.StrategyLab -- mtf-size    --snapshots strategy-lab-data --tf 1d
dotnet run --project AccessibleTrader.StrategyLab -- risk-metric --snapshots strategy-lab-data --tf 1d
```

The entry is held fixed for the first three: the z-score trend cross used in every exit study here,
risk = 1 ATR(14), no overlapping positions. Only the thing being tested varies.

---

## 1. "A fixed 1:3 target accumulates the most profit"

**Falsified in both asset classes**, and the shape of the distribution says why before any target is
tested.

| | crypto (366 trades) | equities (8,596 trades) |
|---|---|---|
| win rate | 35.2% | 32.3% |
| median trade | **−0.751R** | **−0.774R** |
| **top 10% of trades carry** | **101% of total R** | **245% of total R** |
| top 1% carry | 50% | 59% |

The median trade loses. Everything is in the tail — in equities the top decile carries 245% of the
total, meaning the other 90% collectively *lose* money. Any rule that caps the tail is cutting the
only part that pays.

Target sweep, path-dependent (a target counts as hit if price reached +kR before −1R; the stop is
checked first within a bar, the pessimistic assumption):

| target | crypto R/trade | equities R/trade |
|---|---|---|
| 1:1 | −0.315 | −0.232 |
| 1:2 | −0.058 | +0.014 |
| **1:3** | **+0.155** | **+0.134** |
| 1:4 | +0.340 | +0.209 |
| 1:6 | +0.639 | +0.284 |
| 1:8 | +0.801 | +0.312 |
| **no target — signal exit** | **+3.265** | **+0.321** |

Monotone all the way up, and letting the signal decide beats every fixed target. 1:3 captures about
**5%** of the available R in crypto and **42%** in equities. This extends `EXIT_FINDINGS` — the fat
right tail is not a crypto peculiarity, it is present in equities too and if anything is fatter.

**The honest limit:** his market is intraday FX with 15-pip stops, which we have no data for. What
this shows is that the tail exists in both asset classes we *can* measure, on daily bars, which
shifts the burden onto the claim rather than settling it. The path to settling it is FX intraday
data, not more analysis of this data.

## 2. "Stop after two losses in a row"

**No statistical basis.** If trade outcomes are serially independent the rule cannot change
expectancy at all — only variance — so the autocorrelation was measured first:

| | lag-1 autocorrelation of win/loss | p vs shuffle |
|---|---|---|
| crypto | −0.035 | 0.530 |
| equities | −0.040 | 0.999 |

Slightly *negative* if anything: a loss is marginally more likely to be followed by a win. Losses do
not clump.

That makes stopping after two losses a **behavioural** rule, not a statistical one — which is a
perfectly good reason to keep it (it caps the damage a tilting human can do in an afternoon) and a
dishonest reason to claim an edge from it.

## 3. "Size up when the timeframes agree"

**Null — and the overlap check is the finding.**

The claim needed a conditional-expectancy test: bucket the same entry's trades by how many
higher-timeframe conditions agreed, compare per-trade R. Three conditions, all knowable at entry:
last completed weekly candle bullish, close above the 50-bar mean, close above the 200-bar mean.

**How often is each condition true at an entry, against its base rate on all bars?**

| condition | crypto: at entries / all bars | equities: at entries / all bars |
|---|---|---|
| weekly bullish | 57.6% / 36.4% | 54.6% / 39.5% |
| **above 50-bar mean** | **100.0%** / 23.2% | **100.0%** / 30.0% |
| above 200-bar mean | 59.4% / 33.6% | 79.3% / 48.3% |

A z-score cross upward *mechanically* puts price above its own 50-bar mean. That condition is true at
**every single entry** and therefore cannot discriminate between them — "3 of 3" is really "2 of 2".
This is the tautology trap in its purest form, and printing the overlap before the results is what
catches it.

With that understood, the results:

| agreement | crypto R/trade | equities R/trade |
|---|---|---|
| 1 of 3 | +1.494 (n=50) | +0.313 (n=716) |
| 2 of 3 | +4.286 (n=174) | +0.308 (n=4,150) |
| 3 of 3 | +2.259 (n=106) | +0.349 (n=3,586) |
| **full − weak** | **+0.765 R, p = 0.682** | **+0.037 R, p = 0.324** |

Equities are flat to three decimal places. Crypto is non-monotone — the *middle* bucket is best,
which is what noise looks like. Neither difference survives a random-subset null.

**The honest limit:** this tests one entry on daily bars; his process is 5-minute entries under
weekly/daily/30-minute structure. This is a different instantiation of the idea, so it does not
refute his process. What it does say is that the *mechanism* — more agreement, better expectancy —
does not appear where we can measure it, and that anyone testing it must check the overlap first or
they will measure arithmetic.

**This was the first measurement this lab has ever made about position sizing.** The corner is no
longer empty; the first claim in it is a null.

## 4. The 0–1 "risk metric"

Metric = rank of log(price / 200-bar mean) within its own history, scaled 0–1. Forward horizon 90
bars. 51 instruments.

| | low decile | high decile | spread | monotone |
|---|---|---|---|---|
| **crypto** — expanding (honest) | +0.4% | −2.7% | **3.0 pts** | 5/9 steps |
| crypto — full-sample (lookahead) | −0.1% | −7.0% | **6.9 pts** | 4/9 |
| crypto — raw log(price/MA) | −1.1% | −3.8% | 2.6 pts | 4/9 |
| **equities** — expanding | +2.7% | +3.5% | **−0.8 pts** | 2/9 |
| equities — full-sample | +3.8% | +3.1% | 0.7 pts | 3/9 |
| equities — raw | +3.7% | +4.6% | −0.9 pts | 2/9 |

**Three findings, in order of how much they should change what you do.**

**The lookahead premium is 2.3×.** Ranking today's extension against the *whole* series — which is
what a published rainbow chart does — gives a 6.9-point spread in crypto. Ranking it against prior
data only, which is all a trader can actually do, gives 3.0. More than half of the picture's apparent
power comes from knowing the future.

**The 0–1 normalisation adds almost nothing over its own raw input:** 3.0 points versus 2.6 for the
plain log distance from the moving average. This is the same control that killed the on-chain
valuation metrics, and it lands the same way — the transform is presentation, not information.

**Equities are a null with the wrong sign** (−0.8 points: the "high risk" decile has *higher* forward
returns), and only 2 of 9 decile steps are monotone. That is the asset-class polarity finding
reappearing: in equities, extension continues rather than reverting at this horizon.

What remains is a weak crypto signal that is mostly just "price is far above its 200-day average",
measured across three cycles. Worth having as context; not worth a rainbow.

## 5. Sweep and reclaim

Setup: two consecutive same-side swing pivots within 0.35 ATR of each other (confirmed, no
lookahead), breached by 0.10 ATR, then a **close** back inside within 3 bars. Short on highs,
mirrored for lows. Outcome is a fixed 20-bar forward move in R, deliberately — an exit rule here
would be testing the exit.

| | sweep + reclaim | breach only | random floor | reclaim − breach |
|---|---|---|---|---|
| **crypto 1d** | +0.117 R (n=241) | −0.637 R | −0.137 R | **+0.754 R** |
| **crypto 4h** | −0.406 R (n=968) | −0.477 R | **+0.069 R** | +0.070 R |
| **equities 1d** | −0.054 R (n=5,844) | −0.028 R | −0.084 R | **−0.026 R** |

**Not supported.** The one arm that looks like the claim — crypto daily, +0.754 R for waiting — is
the smallest sample, and it is contradicted by both larger ones: on crypto 4h *both* real arms
underperform random entries, and on equities the difference has the wrong sign at a fortieth of the
size.

Even taking the crypto-daily arm at face value, read what it is made of: the reclaim arm scores
+0.117 against a −0.137 random floor, while the breach-only arm scores −0.637. Almost all of the
+0.754 is the breach arm being *bad*, not the reclaim being good. Waiting for the close back inside
avoids a poor entry rather than creating a good one — and "don't short a crypto breakout" is
something this project already knows from the polarity finding.

---

## The shared lesson

Three of these four claims are structurally the same mistake: a real pattern is observed, and the
thing that would produce the same pattern *without* the claimed mechanism is never built. The tail
makes any target look good relative to a smaller target. The z-cross makes "above the 50-bar mean"
look like agreement. Full-sample ranking makes any extension metric look prescient.

In each case the control took a few lines and reversed the conclusion.

---

## 6. The ladder — always in the trend's direction

Run 2026-08-02. `dotnet run -- ladder --snapshots strategy-lab-data --tf 1d`

A click is a fraction of ATR, so rung spacing breathes with volatility. From a reference price, N
clicks in one direction opens a position that way; a K-click trailing stop rides behind it; a
stop-out resets the reference and the ladder starts counting again. So it is always in the market
when price is moving, and it reverses freely.

**51 instruments, daily. It loses money nearly everywhere.**

| | ladder | long-only ladder | buy & hold | random parameters |
|---|---|---|---|---|
| **crypto** (median) | **0.61×** | 2.39× | 1.68× | 0.75× |
| **equity** (median) | **0.06×** | 0.41× | 7.00× | 0.48× |

Beats hold on 3/10 crypto and **1/41** equities. Beats its own random-parameter arm on 4/10 and
1/41 — so the chosen numbers carry no information, which is the same result the walk-forward study
found for trend parameters generally.

**The always-in part is what breaks it.** Long-only is four times better in crypto (2.39× vs 0.61×)
and seven times better in equities. The short side helps on **1 of 10** crypto instruments and 2 of
41 equities. That is the sixth independent time a symmetric short has failed in this project.

### It is not the trading costs

The obvious defence is that it churns — 460 round trips at the default rung width — so the sweep
below removes costs entirely and widens the rungs:

| click | crypto (with cost) | crypto (free) | equity (with cost) | equity (free) | median trades |
|---|---|---|---|---|---|
| 0.5 ATR | 0.61× | 0.71× | 0.06× | 0.15× | 460 |
| 1.0 ATR | 0.76× | 0.80× | 0.24× | 0.35× | 168 |
| 2.0 ATR | 0.41× | 0.42× | 0.40× | 0.45× | 52 |
| 4.0 ATR | 0.75× | 0.75× | 1.67× | 1.70× | 14 |

**With zero costs it still loses at every rung width.** The cost column and the free column are
within a few points of each other everywhere, and by 4 ATR they are identical because there is
almost nothing left to charge. Widening the rungs improves equities (0.06× → 1.67×) purely by
trading less and thereby converging on holding — and 1.67× is still a quarter of the 7.00× that
doing nothing returned.

**Verdict: the structure is the problem, not the frequency.** A stop-and-reverse ladder pays the
trailing stop's cost on every oscillation and collects the trend premium only on the leg that runs.
In a market that trends up over decades, the short legs are a tax and the long legs are a worse
version of holding.

**What survives from the idea.** The long-only variant in crypto (2.39× median, beating hold's
1.68×) is a trend follower with a trailing stop, which is the family the registry already records as
control-tested. The ladder's genuinely new ingredient — *always* being in the market, in both
directions — is the part that fails.
