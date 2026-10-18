# Setup grader round 2, the Market Cipher school: findings

Run 2026-10-18. Stage 2 of 2. The plan is `docs/SETUP_GRADER_ROUND2_MC_PREREG.md`, committed
(60a94a0d) before any result existed. The harness is `StrategyLab setup-grader-mc2`
(`AccessibleTrader.StrategyLab/SetupGraderMc2Command.cs` and `SetupGraderMc2Core.cs`). The order is
in the history:

1. dcebe57c: the harness and its tests, before any count or outcome was computed.
2. cf0e0386: the counts-only stage, written into this file before the full run.
3. The full run, and the rest of this file.

## The verdict, in the pre-registration's words

**Crypto daily: No edge. Equities daily: No edge.** That holds for all three questions and for the
grades.

Section 6.10's sentences apply to both classes, word for word:

- **A Buy dot, bought at the next open, is no better than a random bar in the same
  higher-timeframe environment.**
- **Waiting for the trigger wave with money flow turning up gives an entry no better than a random
  bar in the same environment.**
- **Waiting is not better than buying the dot.**
- **Neither P1 nor P2 passes, so the Market Cipher entry, as written here, has no edge in either
  class, whatever P3, the grades, the secondary tests or the exits say.**

None of the 6 headline tests passed Holm. None came below 0.05 before Holm either. The smallest raw
p was 0.063, for the trigger-wave entry in equities.

The crypto 4h class (S4, secondary) agrees in words. Both entries have positive point estimates
there, and nothing passes Holm.

**P1 equities is a near-replication of round 1, not fresh evidence.** Round 1's non-vetoed grade C
Buy dots in equities beat a random bar in the same context by +0.115R (Holm 0.011). Here the same
dot has a different context (weekly money flow) and a different veto (all three weekly lines falling
two weeks running). It beat its random bar by only +0.053R: raw p 0.095, Holm 0.376, and 44% of it
was kept under noise. So the round-1 result did not replicate under this round's context and veto.

The exits are the one family where something passed Holm. In equities, the first-bearish-divergence
exit beat its random-exit control for both entries, and the lab ladder did for the dot entry. Section
6.10 says this does not rescue the entries, and it does not.

## The answers, briefly

- **Did waiting beat entering at the dot?** No. Per setup seen, waiting added +0.274R of excess in
  crypto (raw p 0.075, Holm 0.376) and −0.012R in equities (p 0.60). In crypto the held-out sign went
  the other way (−0.150R).
- **Did grade A ("deep green") beat grade C?**
  - Crypto: not testable. There were 12 grade A dots in the development era, and 15 are needed.
  - Equities, dot entry: A beat C by +0.062R of excess, p 0.27, not significant.
  - Equities, trigger entry: A was worse than C, by −0.088R.
  - The grades did not run A > B > C for either entry.
- **The veto, two-sided (S1):** neither direction is significant.
  - Crypto: vetoed dots did better than non-vetoed ones by +0.455R (raw p 0.054, Holm 0.92). That is
    the same direction as round 1. But the held-out sign was about zero (−0.011).
  - Equities: vetoed dots did worse by 0.053R (p 0.27), the direction the sources expect. The
    held-out sign flipped (+0.281).
- **Best exit:**
  - By mean result, the lab ladder (X1) was highest everywhere: +2.0 ATR units for the crypto dot
    entry and +1.0 in equities.
  - By skill over random exits, the first bearish divergence (X4) was best. In equities it passed
    Holm for both entries (Holm 0.008 each). In crypto nothing passed.
- **Held-out agreement:** the held-out era (2023 on) agreed in sign with P1 and P2 in both classes,
  and with P3 in equities. It disagreed with P3 in crypto. Since nothing passed in the development
  era, agreement could not lift any verdict.

## The counts-only stage (section 6.7), before any outcome

Run 1 of the command, `--counts-only`. It builds no R array and computes no outcome. Section 6.7
says these counts may decide only whether a test is confirmatory, Suggestive only or not testable,
and whether the A∪B fallback runs. No threshold, window or definition was changed because of them.

Every setup is a Market Cipher B Buy dot. "Dev" is dots before 2023-01-01, "held-out" is 2023 on.
"Eligible" means the dot has a defined higher-timeframe context, an anchor wave, and at least 51 bars
after it.

### Setups by class, era and grade

Crypto daily, weekly environment, ten assets:

| Grade | Dev | Held-out |
|---|---|---|
| A, deep green | 12 | 11 |
| B, backed | 25 | 39 |
| C, not backed | 33 | 40 |
| V, vetoed | 66 | 81 |
| All eligible | 136 | 171 |

Equities daily, weekly environment, 33 assets:

| Grade | Dev | Held-out |
|---|---|---|
| A | 381 | 57 |
| B | 287 | 19 |
| C | 755 | 55 |
| V | 2,702 | 244 |
| All eligible | 4,125 | 375 |

Crypto 4h, daily environment, seven assets (S4, secondary):

| Grade | Dev | Held-out |
|---|---|---|
| A | 53 | 70 |
| B | 90 | 143 |
| C | 191 | 263 |
| V | 293 | 453 |
| All eligible | 627 | 929 |

### Exclusions

- Crypto daily: 114 dots with the weekly context still in warm-up, 13 with fewer than 51 bars after
  them. ADA, SOL, DOGE, KAS and TAO have no development-era setups at all: their weekly context is
  first defined between 2023-07 and 2024-10. Development-era crypto is BTC (56), XRP (28), LTC (20),
  BCH (18) and ETH (14).
- Equities: 193 context in warm-up, 21 too close to the end.
- Crypto 4h: 75 context in warm-up, 1 too close to the end. The development era is BTC 203, ETH 183,
  XRP 229, SOL 9 and KAS 3. BTC and XRP 4h files hold exactly 20,000 bars (the snapshot cap); BTC 4h
  starts 2017-04-30.
- No dot lacked an anchor wave, and no eligible setup had an undefined entry-timeframe ATR or money
  flow.

### The waits (non-vetoed setups, development era)

| Class | Setups | E1 confirmed | Void | Expired | Median wait |
|---|---|---|---|---|---|
| Crypto daily | 70 | 33 | 15 | 22 | 14 bars |
| Equities | 1,423 | 721 | 324 | 378 | 13 bars |
| Crypto 4h | 334 | 158 | 86 | 90 | 14 bars |

- E1h (one candle for the cross to hold) held on 65 of 70 crypto setups, 1,321 of 1,423 equity
  setups and 308 of 334 crypto 4h setups.
- E1m (money flow into the green within 30 bars) confirmed on 36 crypto setups (22 of them already
  green at the dot, so the same trade as E0), 782 equity setups (369 green at the dot) and 174 crypto
  4h setups (72 green at the dot).
- Clusters (non-vetoed dots chained when within 50 bars): crypto 48, equities 995, crypto 4h 200.

### What the counts decide

- **Family P runs confirmatory in both daily classes:** crypto P1 70 trades, P2 33, P3 70 setups
  with 33 confirmed; equities 1,423, 721, and 1,423 with 721.
- **Crypto grades are not testable.** Grade A has 12 E0 trades and 5 E1 trades, below 15, so GA and
  GO for crypto print their counts only. The A∪B fallback is triggered for crypto, for both E0 and
  E1, and is Suggestive whatever its p.
- **Equity grades are confirmatory:** grade A 381 (E0) and 179 (E1), C 755 and 402.
- **Not testable in crypto:** S6 (2 E1 trades with a weekly Buy dot in the last three weeks, against
  31) and S7 (grade A has 12 setups). In equities S6 has 27 against 694, so it is Suggestive only.
- **Held-out sign checks are checkable everywhere except the crypto grades** (held-out crypto grade A
  is 11 for E0 and 8 for E1).
- **Crypto 4h (S4) runs confirmatory:** 334 setups, 158 confirmed.

These counts look adequate for the headline questions. The one thing that looks bad is the same as
round 1: crypto grade A is too rare to test, and development-era crypto is five assets.

No setup was dropped at the outcome stage. Every eligible setup's trades had a defined outcome and
control, so the full run's populations equal the counts above.

## E0, E1 and the matched random bar (Family P)

Development era, non-vetoed setups, net of 15 bps a side. R uses the lab's standard harness: 1 ATR
stop, 2R target, 20 bars. The control is the mean net R of every bar on the same asset, in the same
era, with the same higher-timeframe context state as the trade's signal bar.

Crypto daily:

| Policy | Trades | Mean R | Control | Excess |
|---|---|---|---|---|
| E0, the dot | 70 | −0.056 | +0.030 | −0.086 |
| E1, the trigger | 33 | +0.401 | +0.003 | +0.398 |

Equities daily:

| Policy | Trades | Mean R | Control | Excess |
|---|---|---|---|---|
| E0, the dot | 1,423 | −0.038 | −0.092 | +0.053 |
| E1, the trigger | 721 | +0.011 | −0.071 | +0.081 |

The six tests, Holm inside the family of 6:

| Test | Statistic | Raw p | Holm p |
|---|---|---|---|
| P1 crypto, Ex0 > 0 | −0.086 | 0.719 | 1.000 |
| P2 crypto, Ex1 > 0 | +0.398 | 0.067 | 0.376 |
| P3 crypto, ΔV > 0 | +0.274 | 0.075 | 0.376 |
| P1 equities | +0.053 | 0.095 | 0.376 |
| P2 equities | +0.081 | 0.063 | 0.376 |
| P3 equities | −0.012 | 0.601 | 1.000 |

Gross figures, before costs: crypto E0 −0.012R and E1 +0.453R; equities E0 +0.107R and E1 +0.155R.
Gross excess: crypto −0.096 and +0.399; equities +0.027 and +0.060.

### The four criteria, class by class

| Question | Holm | Held-out | Breadth | Noise kept |
|---|---|---|---|---|
| Crypto P1, dot | no | +0.251, 90 | 2 of 4 | not meaningful |
| Crypto P2, trigger | no | +0.195, 47 | 2 of 2 | 32% |
| Crypto P3, waiting | no | −0.150, 90 | 3 of 4 | 61% |
| Equities P1, dot | no | +0.016, 131 | 17 of 33 | 44% |
| Equities P2, trigger | no | +0.098, 72 | 22 of 30 | 27% |
| Equities P3, waiting | no | +0.038, 131 | 17 of 33 | 99% |

Held-out is the held-out statistic, then the number of held-out events. Breadth counts assets with at
least 10 development-era trades (or setups, for P3) whose own figure is positive. "Not meaningful"
means the clean statistic is negative, so a retention ratio says nothing.

All six fail criterion 1, so all six are No edge. None is Suggestive, because none has a raw p below
0.05.

## Waiting against not waiting, per setup (section 3.3 and 3.4)

ΔV is the mean over every non-vetoed setup of (E1 excess, or 0 when the setup never confirmed) minus
the E0 excess.

| Class | Setups | ΔV | Raw p | Holm |
|---|---|---|---|---|
| Crypto | 70 | +0.274 | 0.075 | 0.376 |
| Equities | 1,423 | −0.012 | 0.601 | 1.000 |
| Crypto 4h (S4) | 334 | +0.069 | 0.227 | 1.000 |

What sits behind it (development era, descriptive):

- **E0 split by what happened next.** Crypto: −0.094R excess on the 33 setups that went on to
  confirm, −0.079R on the 37 that did not. Equities: +0.013R on the 721 that confirmed, +0.094R on
  the 702 that did not. Knowing at the dot that a setup will confirm would not have helped. This
  split is not a tradable policy anyway.
- **Paired on the confirmed setups,** E1 minus E0 excess: crypto +0.491R (33 setups), equities
  +0.068R (721), crypto 4h +0.192R (158). In the held-out era: crypto −0.223R, equities +0.003R,
  crypto 4h −0.118R.
- **The narrator's "bigger difference, bigger move."** Split the confirmed setups at the median
  depth difference between anchor and trigger troughs (crypto 31.6, equities 27.3 wave points). The
  smaller differences did better in every class: crypto +0.641R against +0.139R, equities +0.099R
  against +0.064R, crypto 4h +0.354R against +0.182R. The claim's direction is not supported.
- **Confirmation rates** (E1, development era): crypto grade A 42%, B 36%, C 58%, V 52%; equities
  A 47%, B 49%, C 53%, V 53%.
- **All Buy dots, vetoed ones included, E0 (the ungraded reference row):** crypto 136 dots,
  +0.135R excess; equities 4,125 dots, +0.018R.

## The grades (Family G) and the ordering

The grade is the weekly context at the dot. Holm is inside the family of 8.

| Test | n | Statistic | Raw p | Holm |
|---|---|---|---|---|
| GA crypto E0 | 12 | not testable | | |
| GO crypto E0 | 12 vs 33 | not testable | | |
| GA crypto E1 | 5 | not testable | | |
| GO crypto E1 | 5 vs 19 | not testable | | |
| GA equities E0 | 381 | +0.081 | 0.139 | 1.000 |
| GO equities E0 | 381 vs 755 | +0.062 | 0.273 | 1.000 |
| GA equities E1 | 179 | +0.048 | 0.355 | 1.000 |
| GO equities E1 | 179 vs 402 | −0.088 | 0.667 | 1.000 |

Point estimates by grade, development era, equities (excess, then mean net R):

| Grade | E0 excess | E0 R | E1 excess | E1 R |
|---|---|---|---|---|
| A | +0.081 | −0.023 | +0.048 | −0.026 |
| B | +0.107 | +0.035 | −0.031 | −0.105 |
| C | +0.019 | −0.074 | +0.136 | +0.067 |
| V | +0.000 | −0.035 | +0.127 | +0.059 |

Neither entry runs A > B > C. With the dot entry, B is on top. With the trigger entry, C and V are on
top and A is third. **Grades: No edge in equities, for both entries.** In crypto they are not
testable, so also No edge.

For the record, crypto's small counts read: E0 excess A −0.161 (12), B +0.125 (25), C −0.219 (33).
E1 excess A +0.975 (5), B −0.214 (9), C +0.535 (19). These are counts too small to test, not
results.

**The A∪B fallback (crypto only, Suggestive whatever its p, outside Family G's Holm):** for E0, A∪B
minus C was +0.252R (37 against 33, raw p 0.247). For E1 it was not testable (14 against 19).

## The secondary family (Family S, 19 tests, Holm inside)

| Test | n | Statistic | Raw p | Holm |
|---|---|---|---|---|
| S1 crypto, veto, two-sided | 66 vs 70 | +0.455 | 0.054 | 0.925 |
| S1 equities, veto, two-sided | 2,702 vs 1,423 | −0.053 | 0.270 | 1.000 |
| S2a crypto, Ex1h > 0 | 65 | +0.152 | 0.260 | 1.000 |
| S2b crypto, ΔV E1h > 0 | 70 | +0.227 | 0.042 | 0.749 |
| S2a equities | 1,321 | +0.047 | 0.147 | 1.000 |
| S2b equities | 1,423 | −0.009 | 0.605 | 1.000 |
| S3a crypto, Ex1m > 0 | 36 | +0.000 | 0.520 | 1.000 |
| S3b crypto, ΔV E1m > 0 | 70 | +0.086 | 0.212 | 1.000 |
| S3a equities | 782 | +0.051 | 0.150 | 1.000 |
| S3b equities | 1,423 | −0.025 | 0.744 | 1.000 |
| S4 P1, crypto 4h | 334 | +0.057 | 0.214 | 1.000 |
| S4 P2, crypto 4h | 158 | +0.268 | 0.014 | 0.274 |
| S4 P3, crypto 4h | 334 | +0.069 | 0.227 | 1.000 |
| S5 crypto, two-sided | 70 | +0.155 | 0.665 | 1.000 |
| S5 equities, two-sided | 1,423 | −0.049 | 0.508 | 1.000 |
| S6 crypto | 2 vs 31 | not testable | | |
| S6 equities | 27 vs 694 | +0.229 | 0.115 | 1.000 |
| S7 crypto | 33 vs 12 | not testable | | |
| S7 equities | 755 vs 381 | +0.112 | 0.203 | 1.000 |

What they say:

- **S1, the veto.** In crypto, vetoed dots beat non-vetoed ones on the point estimate. That is the
  same direction as round 1, and section 6.10 asks the write-up to say whether the veto is wrong. It
  cannot be called wrong: the result is not significant (Holm 0.925), and the held-out difference is
  about zero (−0.011R on 81 against 90). Equities point the way the sources expect, also not
  significant, and the held-out difference flipped (+0.281R).
- **S2, wait a candle.** No edge. Crypto's ΔV had a raw p of 0.042 before Holm, which is a lead at
  most, and its held-out sign flipped (−0.036R).
- **S3, wait for green money flow.** No edge in either class.
- **S4, crypto 4h.** P2's raw p was 0.014 (+0.268R on 158 trades); Holm 0.274. Its held-out sign
  agreed (+0.088R on 254). P3's held-out sign flipped. In words, the 4h class supports the daily
  verdict: positive point estimates for the trigger entry, and nothing that passes.
- **S5, the cost of waiting under the school's own plan** (its swing-low stop and the money-flow
  exit, in ATR units, two-sided): crypto +0.155, equities −0.049. Neither is significant, and both
  held-out signs flipped. Casper's warning that waiting worsens the trade is not shown, and neither
  is its opposite.
- **S6, a weekly Buy dot in the last three weeks.** Not testable in crypto (2 trades). In equities it
  is Suggestive only (27 trades) and not significant.
- **S7, Casper's regime rule** (waiting helps more in grade C than in grade A). Not testable in
  crypto. In equities: +0.112R, p 0.203, not significant.

## The exits against random exits (Family X, 16 tests, Holm inside)

Each exit is run on the same entries. Its control keeps each entry and that exit's initial stop, and
draws a random exit bar from the exit's own holding times, 2,000 times.

- X1 is the lab ladder on a 3 ATR stop.
- X2 to X5 use the school's own stop: the anchor's swing low less 0.25 ATR, at least 0.5 ATR below
  the entry.
- X2 exits at a bear wave cross, X3 when money flow curves over in the green, X4 at the first
  bearish divergence, and X5 is the 20-bar time exit (reported, not tested).

Mean net R, the random-exit mean, and the Holm p. X5 is not tested.

Crypto, the dot (E0), 70 entries:

| Exit | Mean R | Random | Holm |
|---|---|---|---|
| X1 ladder | +0.682 | +0.375 | 0.90 |
| X2 bear cross | −0.027 | +0.054 | 1.00 |
| X3 money flow curls | +0.231 | +0.191 | 1.00 |
| X4 first divergence | +0.659 | +0.369 | 0.46 |
| X5 20 bars | +0.269 | +0.169 | |

Crypto, the trigger (E1), 33 entries:

| Exit | Mean R | Random | Holm |
|---|---|---|---|
| X1 | +0.710 | +0.497 | 0.90 |
| X2 | +0.264 | +0.158 | 0.90 |
| X3 | +0.296 | +0.406 | 1.00 |
| X4 | +1.067 | +0.611 | 0.71 |
| X5 | +0.481 | +0.397 | |

Equities, the dot (E0), 1,423 entries:

| Exit | Mean R | Random | Holm |
|---|---|---|---|
| X1 | +0.336 | +0.225 | **0.042** |
| X2 | +0.006 | −0.017 | 0.86 |
| X3 | +0.052 | +0.023 | 0.90 |
| X4 | +0.223 | +0.083 | **0.008** |
| X5 | +0.111 | +0.039 | |

Equities, the trigger (E1), 721 entries:

| Exit | Mean R | Random | Holm |
|---|---|---|---|
| X1 | +0.281 | +0.158 | 0.059 |
| X2 | +0.015 | −0.028 | 0.17 |
| X3 | +0.125 | +0.065 | 0.36 |
| X4 | +0.284 | +0.164 | **0.008** |
| X5 | +0.152 | +0.087 | |

In ATR units, which compare across stops: X1 +2.05 (crypto E0), +2.13 (crypto E1), +1.01 (equities
E0), +0.84 (equities E1). X4: +0.90, +2.25, +0.36, +0.39. X2: −0.07, +0.57, −0.00, −0.08. X3: +0.22,
+0.80, +0.11, +0.12. X5: +0.21, +0.99, +0.15, +0.17. Mean holds: X1 55 to 82 bars, X4 23 to 29,
X3 12 to 18, X2 5 to 7.

What this says:

- **In crypto, no exit beat its random exits.**
- **In equities, the first bearish divergence beat random exits for both entries,** and the ladder
  did for the dot entry. This is against the pre-registered prior: the ledger predicted no mechanical
  exit beats random, and a signal exit with skill only in crypto.
- **Why that matters little here:** section 6.10 says a good exit does not rescue the entries.
- **Why it is not a timing artefact:** the `Bearish Divergence` marker is stamped on the bar where
  the divergence can first be known. Cipher B's `DivergenceConfirmLag` defaults on, and
  `IndicatorCausalityTests` (a truncation check on every component declared causal) guards it.
- **A likely reason:** the divergence exit sells into strength (a new price high with weaker
  momentum). The lab's standing finding is that equities mean-revert over days, and this fits it.
  It was not tested as its own claim, and should not be treated as an edge without its own
  pre-registration.

## The held-out era, signs only

Read once, after every development test. "Agrees" means the held-out statistic has the sign the
claim needs (or, for two-sided S1 and S5, the development sign).

| Test | Dev | Held-out (n) | Agrees |
|---|---|---|---|
| P1 crypto | −0.086 | +0.251 (90) | yes |
| P2 crypto | +0.398 | +0.195 (47) | yes |
| P3 crypto | +0.274 | −0.150 (90) | no |
| P1 equities | +0.053 | +0.016 (131) | yes |
| P2 equities | +0.081 | +0.098 (72) | yes |
| P3 equities | −0.012 | +0.038 (131) | yes |
| GA equities E0 | +0.081 | +0.165 (57) | yes |
| GO equities E0 | +0.062 | +0.369 (55) | yes |
| GA equities E1 | +0.048 | +0.425 (34) | yes |
| GO equities E1 | −0.088 | +0.549 (25) | yes |
| S1 crypto | +0.455 | −0.011 (81) | no |
| S1 equities | −0.053 | +0.281 (131) | no |
| S4 P2, crypto 4h | +0.268 | +0.088 (254) | yes |
| S4 P3, crypto 4h | +0.069 | −0.044 (476) | no |

The crypto grades have held-out counts of 11 (E0) and 8 (E1). They were not testable in the
development era, so their held-out numbers (+0.600 and +0.589 for grade A) answer nothing.

The rest of the secondary family's held-out signs are printed in the run's output. S2b crypto, S3b
crypto, S2a equities, S2b equities and both S5 flipped. S6's held-out groups had 1 and 2 trades, so
they were not checkable.

## Noise retention (section 6.4)

Each daily series was perturbed at 25% of its own close-to-close volatility, with seeds 0, 1 and 2,
each offset by the symbol's stable seed. Then everything was rebuilt: weekly bars, Cipher B on both
timeframes, waves, setups, contexts and controls.

**The two noise functions:**

- **`GateCommand.Perturb`, the shared one, was not used or edited.** It sets every bar's open equal
  to its close. A unit test asserts that it still does.
- **The new `SetupGraderMc2Core.PerturbKeepGap`** gives the closes exactly the same noisy path, with
  the same random draws. It then sets each open to the previous noisy close times the bar's own
  opening gap (open ÷ previous close). The high and low keep their ratios to the close and are
  widened if needed to contain the open and the close. A test pins the gap, the shared close path,
  and the high and low.

Development-era statistics, clean and the mean over the three noisy seeds:

| Statistic | Clean | Noisy mean | Kept |
|---|---|---|---|
| Crypto Ex0 | −0.086 | −0.112 | n/a, clean below 0 |
| Crypto Ex1 | +0.398 | +0.129 | 32% |
| Crypto ΔV | +0.274 | +0.166 | 61% |
| Equities Ex0 | +0.053 | +0.024 | 44% |
| Equities Ex1 | +0.081 | +0.022 | 27% |
| Equities ΔV | −0.012 | −0.012 | n/a, clean below 0 |
| Equities ExA, E0 | +0.081 | +0.017 | 22% |
| Equities ExA, E1 | +0.048 | +0.083 | 175% |

Per seed, crypto Ex1 was +0.112, +0.369 and −0.094, and equities Ex1 was +0.053, +0.056 and
−0.042. Both trigger-wave entries, the two largest point estimates in the study, keep under a third
of their size under noise. The harness also computed crypto's grade statistics under noise. Grade A
was not testable there, so those numbers are not used.

## Per-asset results (breadth)

Development era, non-vetoed. Assets with fewer than 10 trades are listed but do not count for
breadth.

Crypto daily:

| Asset | E0 n | Ex0 | E1 n | Ex1 | ΔV |
|---|---|---|---|---|---|
| BTC | 25 | −0.429 | 14 | +0.165 | +0.521 |
| XRP | 20 | +0.054 | 10 | +0.300 | +0.096 |
| LTC | 10 | +0.454 | 5 | +0.605 | −0.152 |
| BCH | 10 | −0.063 | 2 | +0.578 | +0.179 |
| ETH | 5 | −0.060 | 2 | +1.816 | +0.786 |

Crypto has no development-era setups on ADA, SOL, DOGE, KAS or TAO.

Equities: the dot entry was positive on 17 of 33 assets, the trigger entry on 22 of 30 (assets with
at least 10 trigger trades), and waiting on 17 of 33. The full per-asset table is in the run's
output, under "per asset".

Crypto 4h, development era: Ex0 BTC −0.030 (104), ETH −0.013 (100), XRP +0.228 (123). Ex1 BTC
+0.521 (44), ETH +0.209 (51), XRP +0.125 (58).

## Deviations and literal readings

All of these were fixed in the harness commit (dcebe57c), before any count or outcome existed.

1. **The control's era is the signal bar's era.** A trade's control and permutation stratum use the
   era of the bar it enters from, since that is the bar the control describes. The setup's era
   (development or held-out) is its dot's date, as section 6.2 says. The two differ only for a dot in
   the last 30 bars of 2022 whose wait ends in 2023.
2. **Thickness median.** With an even number of values, the median is the mean of the two middle
   ones. The window is the trailing 104 completed higher bars, h included, counting defined values
   only, with at least 26.
3. **Extra exclusions:** an undefined entry-timeframe ATR or money flow at the dot, and, at the
   outcome stage, a trade with no defined outcome or control. Neither excluded anything.
4. **Crosses and dots come from the indicator's own components:** `WaveTrend Cross Bull`,
   `WaveTrend Cross Bear` and `Oversold Crossover` (the shipped MCB Buy dot). Their rules are section
   2.2's.
5. **The search window.** E1 looks for the anchor's end and the trigger only up to bar i + 30, since
   the trigger must come after the anchor ends and by i + 30. A setup still open at i + 30 is
   "expired".
6. **E1h and E1m have no void rule,** because section 2.4 gives them none.
7. **The S6 window** is the newest completed weekly bar at the dot and the two before it.
8. **The exit rules.** X4 reads only the regular `Bearish Divergence` component, not the hidden one.
   As in round 1: "from the entry bar on" includes the entry bar's close; the 60-bar cap and X5's 20
   bars end at the close of bar s + 60 and s + 20; a stop the bar opens through fills at the open.
   The X3 exit used inside S5 is the same rule.
9. **The random-exit p** is (1 + random books at or above the exit's mean) ÷ 2,001. That is the
   share with the observed book counted, and it is what section 6.5's "about 0.0005" smallest p
   implies.
10. **Minimum counts.**
    - P3 and the ΔV halves of S2, S3 and S4 need 30 setups and 15 confirmed to be confirmatory. With
      fewer than 15 confirmed the test runs as Suggestive only; below 15 setups it is not testable.
    - Group tests (GO, S1, S6, S7, the fallback) need 15 in each group, and are Suggestive only with
      15 to 29.
    - Exit tests need 15 entries.
11. **The A∪B fallback** is triggered per class and entry when that entry's development-era grade A
    trades number fewer than 30. It is the ordering A∪B against C. It is Suggestive whatever its p,
    and it is not in Family G's Holm.
12. **"Suggestive"** means a raw p below 0.05, and a result not Supported or Fragile because Holm
    failed, the count is 15 to 29, or the held-out check is not checkable. A Holm-significant result
    whose held-out sign or breadth failed would be No edge. This case did not arise.
13. **Noise seeds** are 0, 1 and 2 plus the symbol's stable seed mod 1000, as round 1 and the gate
    command offset them, so assets do not share one noise path.
14. **Two-sided held-out check.** For S1 and S5, "agrees" means the same sign as the development
    estimate.
15. **Held-out counts.** For a group difference, the held-out count is the smaller group's.
16. **Higher-timeframe bars** are round 1's: crypto weeks from Monday 00:00 UTC, complete at the
    next Monday; equity weeks complete on their last trading day; 4h-to-daily by UTC day. The last
    bucket is kept only when the data shows it finished.
17. **Shared code.** Round 1's `Ladder` and `StopAndRule` gained overloads that take any bar series
    and ATR array. Round 1 now calls these overloads with unchanged arithmetic, and its tests pass.
    `LabelPerm` gained a seed prefix and a two-sided option; round 1's defaults are unchanged.

## Runs and variants counted

- **Runs of the command: 2.** Run 1 was `--counts-only`, which computed no outcome. Run 2 was the
  full run. No rerun, no bug-fix run, and no other variant was made.
- **One code reading after the full run,** with no run: checking where the `Bearish Divergence`
  marker is stamped, to rule out a timing artefact in X4. It changed nothing.
- **Pre-registered tests: 49. Computed 43, not testable 6.**
  - Family P: 6 of 6 computed.
  - Family G: 4 of 8 (equities only; crypto's four grade tests were not testable).
  - Family S: 17 of 19 (S6 and S7 crypto were not testable).
  - Family X: 16 of 16.
  - Holm used the full family sizes (6, 8, 19 and 16), even with members not testable.
- **Fallback tests:** 2. Crypto E0 was computed; crypto E1 was not testable.
- **Pre-registered descriptive runs, counted toward variantsTried:**
  - the counts-only run (1);
  - six noise rebuilds (3 seeds, 2 classes);
  - section 3.4's six descriptive rows in each of 3 classes (18): the E0 split two ways, the paired
    difference, the two depth halves, and the reference row;
  - X5 in 4 populations (4).

  Gross figures, ATR units and per-asset tables are columns of these rows, and are not counted
  separately.
- **variantsTried for the edge record: 49 + 2 + 1 + 6 + 18 + 4 = 80.**
- **Variants beyond the budget: none.** No window, threshold, definition, era cut or asset subset
  was changed.

## Data proof

The study read 50 input files, read-only, from
`/home/cody/external-rescue/Github/accessible-trade-terminal/strategy-lab-data`. Every file's sha256
was taken before and after each of the two runs: 0 changed, and both runs saw the same hashes.
Nothing was fetched.

```
7e4df0795b9ffe098e964736d15d680a4c18d8cc82f3c2fd7bf4ade484efcf0f  bitstamp_BTC_USDT_1d.json
b26e92b295239c07e6b0e7c3738b025b2004b5d2805c7840bf272916aab1844e  bitstamp_ETH_USDT_1d.json
12e2b09a7b162482e2486030ededb222413077d107de71d02f1f504447b4d932  bitstamp_LTC_USDT_1d.json
0ff6f1e1f7a43644b506379f994550706e93f19bd9dc47a8920b6888ce59214f  bitstamp_XRP_USDT_1d.json
7f7ba6e6e1668054ef80f3d80724d06c023a9129b263ad9a8042e563683b2b35  bitstamp_BCH_USDT_1d.json
90e77f8054f512cceb96eb718887dc506141f0b30facc9f3645520dcc11ea9ff  bitstamp_ADA_USDT_1d.json
9ba5831ba6ba1e2cba552ac20e22555a17513314a61dd3ab0dc40d77bdd41fdf  bitstamp_SOL_USDT_1d.json
05bec010f7f7ff1a136ecb6607fb308c296f304cb2d3f24b85ead52d06396783  bitstamp_DOGE_USDT_1d.json
a98e5bfcf28fb668f35505110522f5a1efd92a5953907ea28382f32b6409dcf7  mexc_KAS_USDT_1d.json
1651c18a292799ea6a43694c8b42fe13f943bb6911a240c94f69343758796f9d  mexc_TAO_USDT_1d.json
36991f521cf90ef9c84f83503b06aa3f7167be3c78f96792a9279b325605eeff  yahoo_AAPL_1d.json
22f8cfcaeb0aa411de8d545dc33e77c4f77daad5ed9ad03c37e812df94acd1a2  yahoo_CAT_1d.json
7179ecc06aaca985a0379f442d8f01b93f5cb2d14f60b9a319a24b44ebef1779  yahoo_CVX_1d.json
62392f10dc023ee201e0b5ed62bf228fe06dc9598ada5e521e009facc6fda224  yahoo_DIA_1d.json
72107fb61318c86cf66892a8101842bf940126f212fdb9855eb59e8afe31ad9f  yahoo_EEM_1d.json
868f8e9d3c4d7cbf2b7044c2107a543d5fd07f34b9d8aecc62d64f8e81d128f4  yahoo_EFA_1d.json
1599960f0ac0e7298e0601ce78a9dc8fda9cbfc143fdda28b8dd894b756beae4  yahoo_FXI_1d.json
ce0c6347c2c7ad6b1efdfa8c4178c8ae43c69b84a726adb71f0388eb877f25b6  yahoo_IBM_1d.json
e79308e83f7452baaa32a1af42d0bdeaf7f48ba445f52f29751af4ad3404ac5c  yahoo_IWM_1d.json
458279c8e30ed2ed967f0560a41ab3911e9a4324714a5ddfe7f7e0cc25a9a3a2  yahoo_JNJ_1d.json
cff45a0a427f31bd29cd8a7e867d7cfa549e157306f7a0198a4f7d503a962f22  yahoo_JPM_1d.json
d80d20fb483a4dac3b1d434a7efe619a6104e2e747a8939c8087d4cd0e900648  yahoo_KO_1d.json
0a214b632d46f959ee59f324a22c7eb9f740c8eda4e59467fab2f280b282171c  yahoo_MCD_1d.json
c55394903b88f7cff67f44c87d1e0f67d4517c695b36f27e7b74025339e98c7d  yahoo_MMM_1d.json
20a6ae11022ed690e2c62ffee2a3c68d078e58e1f82ffea5337f31997f552dc7  yahoo_MSFT_1d.json
0eeb5a1f666cdcfcdf2d24bbcf249be9a0f8ff118478e7de97965765a645cfe6  yahoo_PFE_1d.json
f634c928a4e6b2a50d3e91ea9fd93ac0efe42aa4ff8ff33aab221fbc381036c5  yahoo_PG_1d.json
62e92e6c591b9f888d9626ef95ec23d4deb09788bf78eb511f7395fe72f5cf68  yahoo_QQQ_1d.json
9e4a2182278103ab6762515d452ab74e177b1380b9e0e8fcfd40fea889d16693  yahoo_SPY_1d.json
3776bb2da7166014f2f462fadb1614e33e5ae7c920e05851428545a025a316e9  yahoo_T_1d.json
180566d7f28928ecd3d1eb452d8293689a7e63b338a9b0bf6ea8232ac67f6c74  yahoo_VTI_1d.json
c6ba9fc911fafcb9decb5f0e0c1d85293c06e822d59ad890b3b5528fd3e0497d  yahoo_VZ_1d.json
2f22f0f70143f5d53210294a74a4152cfde1987d97b417da5672c96198e6e10c  yahoo_WMT_1d.json
f8062fd8a92906ae63f411f25745f0485f08f939ec37a06361b8721973833f66  yahoo_XLB_1d.json
b5b4e9f9f19010e7973ee2ef4c3fb45483384d90c61768439632a914b9e76a1f  yahoo_XLE_1d.json
6d23ab3d549411c72f5ff364669d7ccd46897796abdedfd1bf79821548143c7f  yahoo_XLF_1d.json
0c2353a8ddb74cf4278c9246988f872c133e79ea8183420c09a23bb383fec24e  yahoo_XLI_1d.json
1e02620002ac3064e2103f34285bfd3ae30bdc9a09f10b9f8b91fde8b96e03e0  yahoo_XLK_1d.json
c063840e9e21be74ab4cee7cf2002e690746b5214624fb0341637610f1e7671a  yahoo_XLP_1d.json
ebf13843424dbb8de6ef00fb3139ca856c24562cc2caf2bf8a439f3484f6e40a  yahoo_XLU_1d.json
90484cec945d1d62c5651ca450200f34da2761a736e33fc760529fa9f2f9b616  yahoo_XLV_1d.json
1ac1b5ba8a96498ff2002208b0452d14f970588d8619d843a12baf8d18345f98  yahoo_XLY_1d.json
c247f297a3db58f7c791720e286e63f8cf16802bc5448c6017970568e7261491  yahoo_XOM_1d.json
5a81a70c38200b77ac226babf673b6923683c51f2cc894a68a627850c7685799  bitstamp_BTC_USDT_4h.json
1743ca59b0dfd9fb47d16a2ada6119ede7af21a79dd45bc98df68d5c7f6bd1ed  bitstamp_ETH_USDT_4h.json
368b93e4f361bdddd0354f1e04a7c98cd9c040e476bd8fe583fabc66a933818d  bitstamp_XRP_USDT_4h.json
4197343d33611c242c9789281d4fb908995a9a83a8076d7171912e1fad1fe2b7  bitstamp_SOL_USDT_4h.json
f2bfa98a494046bfe09a8b1ed934dae112ca9e1096cd63fe8e8a1a286e6f6f9e  bitstamp_DOGE_USDT_4h.json
a5374f754be9793cba98e2b02cc6ec29d1553465adb5a48ce91cf44afec026a8  mexc_KAS_USDT_4h.json
4c058c382d783f4c22b4eddf074c00f477a94479205a11e2f989ea0f74e7b9ee  mexc_TAO_USDT_4h.json
```

## Lab problems found

- **A look-ahead guard can miss a mutant that changes nothing on its data.** Sabotage proofs for the
  truncation test:
  - Making the thickness window read one weekly bar ahead did not turn it red. One extra value in a
    104-value median flipped no context on the synthetic series.
  - Making grade B read the next weekly bar did turn it red, and so did making the money-flow wait
    read the next bar.

  The guard still holds the property: nothing at bar t changes when later bars are removed. But a
  sabotage that changes nothing cannot prove it.
- **The guard on the matched control first went red through its own vacuity check.** With the strata
  keyed without the context, every trade looked like it shared one context. A second sabotage,
  reading the context at the dot instead of at the signal bar, went red on the property itself.
- **Crypto grade A starves again,** at 12 development-era dots. This time it is not because the
  grade's conditions exclude each other. The weekly money flow needs about 86 weeks of warm-up, so
  only five crypto assets have development-era setups at all.

## Scope and caveats

- **Every result is about our replica of Market Cipher B** (section 1.2). Casper and Geo say the
  paid script's money flow is different. The pre-registration forbids blaming a null on the replica,
  and it forbids crediting a pass to the paid script.
- **Survivor snapshots:** every asset here still trades.
- **Development-era crypto is five assets and 70 non-vetoed setups.** The crypto trigger-wave entry
  (+0.398R on 33 trades) is the largest point estimate in the study. It did not pass Holm, kept 32%
  under noise, and is a reading of 33 trades on five correlated assets.
- **The held-out era** is mostly a crypto bull market and a strong equity tape. It was used for signs
  only.
- **Longs only.** The mirror short was not tested.
- **The equities exit result is real, but it is not part of the question.** It should get its own
  pre-registration before anyone builds on it.

## Reproduce

```bash
cd AccessibleTrader.StrategyLab
dotnet build -v q --nologo
dotnet run --no-build -- setup-grader-mc2 --snapshots /absolute/path/to/strategy-lab-data
# --counts-only stops after the counts and what they decide, before any outcome.
```

**Tests:** `SetupGraderMc2Tests` (8). Each was seen red with its defect put back, in a scratch copy
that was then restored byte-identical:

| Test | Sabotage that turned it red |
|---|---|
| The trigger-wave entry never fills before its trigger bar has closed | trigger stamped one bar early |
| Nothing at bar t changes when later bars are removed | grade B, or the money-flow wait, reads the next bar |
| The context at a bar reads only completed weekly bars | context reads the week in progress |
| The anchor trough includes the wave's lag after the dot | trough stops at the dot |
| A deeper wave voids the setup; a higher cross with money flow falling is skipped | void rule removed; money-flow rule removed |
| The new perturbation keeps each bar's opening gap | open set equal to the close |
| Every control comes from the trade's own context, read at its signal bar | strata without context; context read at the dot |
| R is per position from the initial risk after the stop moves | R divided by the stop in force |
