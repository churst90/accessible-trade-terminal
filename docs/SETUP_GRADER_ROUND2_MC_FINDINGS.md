# Setup grader round 2, the Market Cipher school: findings

Stage 2 of 2. The plan is `docs/SETUP_GRADER_ROUND2_MC_PREREG.md`, committed (60a94a0d) before any
result existed. The harness is `StrategyLab setup-grader-mc2`
(`AccessibleTrader.StrategyLab/SetupGraderMc2Command.cs` and `SetupGraderMc2Core.cs`), committed
(dcebe57c) before any count or outcome was computed. This section, the counts-only stage, was
committed before the full run.

**Status at this commit: counts only. No outcome has been computed.**

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
