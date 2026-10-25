# The Market Cipher tests re-run on the exact Ciphers: findings

The plan is `docs/EXACT_CIPHERS_RERUN_PREREG.md`, committed (a95f53bc) before any outcome on the
exact indicators existed. This file is written stage by stage; each counts-only section was written
before the full run that follows it.

## Step 1: the harness tests

`SetupGraderTests`, `SetupGraderMc2Tests`, `SetupGraderMc3Tests` and `LabCipherProfileTests`: 25
passed, 0 failed, at a95f53bc, with no edits and no re-pins.

## Step 2: round 1, counts only (before its full run)

- **Inputs:** the same 52 files. Their sha256 hashes equal the 50 that round 2's findings list.
  Round 1's findings list no hashes, and the other two files are TLT and BTC funding.
- **Cipher B profile:** NEW (Market Cipher B defaults), as on 10-18. The differences are the exact
  Buy dot, RSI line and money flow (plan, section 1).

Long Buy dots by grade, old (10-18) → exact:

| Class | Era | A | B | C | V | All |
|---|---|---|---|---|---|---|
| Crypto daily | Dev | 0 → 0 | 4 → 3 | 44 → 34 | 105 → 94 | 153 → 131 |
| Crypto daily | Held-out | 0 → 0 | 17 → 10 | 31 → 30 | 101 → 81 | 149 → 121 |
| Equities daily | Dev | 2 → 1 | 90 → 37 | 1,426 → 920 | 2,641 → 2,421 | 4,159 → 3,379 |
| Equities daily | Held-out | 0 → 1 | 14 → 9 | 144 → 92 | 228 → 234 | 386 → 336 |

- **There are fewer Buy dots: 19% fewer in development-era equities.** A cross up with the fast
  wave below −60 is rarer than one with the slow wave below −60.
- **Grade A is still essentially absent** (0 crypto, 1 equities in development). So round 1's P1
  and P4 are again not testable, by its section 7.4.
- **Grade B halved in equities** (90 → 37). B needs the money flow and the higher-timeframe money
  flow rising, and the exact money flow is a different line.

## Step 3: round 2, counts only (before its full run)

The same 50 inputs, 0 changed. Non-vetoed setups and trigger (E1) trades, development era, old (10-18)
→ exact:

| Class | Eligible | A | B | C | V | Non-vetoed | E1 confirmed |
|---|---|---|---|---|---|---|---|
| Crypto daily | 136 → 120 | 12 → 14 | 25 → 12 | 33 → 33 | 66 → 61 | 70 → 59 | 33 → 28 |
| Equities daily | 4,125 → 3,357 | 381 → 250 | 287 → 190 | 755 → 736 | 2,702 → 2,181 | 1,423 → 1,176 | 721 → 586 |
| Crypto 4h (S4) | 627 → 545 | 53 → 50 | 90 → 52 | 191 → 158 | 293 → 285 | 334 → 260 | 158 → 131 |

Held out (2023 on), non-vetoed and E1: crypto 90 → 61 and 47 → 35; equities 131 → 90 and 72 → 46;
crypto 4h 929 → 809 eligible.

**What the counts decide (round 2's section 6.7, as written):**
- **Crypto P2 drops from confirmatory to "Suggestive only".** It has 28 E1 trades, and 30 are needed.
  This is the one change of status. Crypto P1 (59) and P3 (59 setups, 28 confirmed) stay confirmatory.
- **Crypto grades stay not testable.** Grade A has 14 E0 trades and 4 E1 trades, so the A∪B fallback
  is triggered again.
- **Equities stay confirmatory throughout.** P1 1,176, P2 586; grade A 250 E0 and 118 E1.
- **Crypto 4h (S4) stays confirmatory.** 260 setups, 131 confirmed.
- **Round 3's plumbing check will need these numbers.** Its development-era (setups, E1) become crypto
  4h (260, 131), crypto daily (59, 28) and equities (1,176, 586).

## Step 4: round 1, full run

Run once, with no crash or rerun. Inputs: 52 files hashed before and after, 0 changed.

**Verdict, by round 1's own rules: No edge in both classes, as on 10-18.** Grade A, the hypothesis,
is again not testable: crypto has 0 triggers and equities 1, and 15 are needed. So P1 and P4 do not
run. The other cells are context.

| Test | Old (10-18) | Exact |
|---|---|---|
| P3-equities, grade C ExC > 0 | +0.115R, p 0.0014, Holm 0.011 | +0.121R on 920, p 0.0061, Holm 0.0488 (still passes) |
| S1-equities, C beats vetoed | p 0.0024, Holm 0.029 (passes) | +0.121R, p 0.0127, Holm 0.1524 (**no longer passes**) |
| P3-crypto | p 0.765 | +0.168R on 34, p 0.286 |
| P2-equities, grade B | p 0.476 | −0.145R on 37, p 0.705 |
| Exits (10 tests) | none pass | none pass (smallest X2-equities p 0.095) |
| Macro (5 tests) | none pass | none pass (smallest M2-crypto p 0.216) |
| A∪B fallback, equities | p 0.53 / 0.84 | −0.171R, p 0.753 / −0.292R, p 0.943 |

- **The one Holm pass stays: grade C Buy dots in equities beat a matched random entry, +0.121R.** It
  sits just under the line (Holm 0.0488). Round 1's rules give it no verdict of its own, because
  grade C is the reference grade, not the hypothesis. Round 2 already found that this did not
  replicate under its own context and veto.
- **Held out (2023 on), descriptive:** equities grade C +0.428R on 92 triggers.
- **Descriptive only:** the dot with the close above its 200-day average (R0): equities development
  +0.187R, held out +0.331R.

## Step 5: round 2, full run

Run once, with no crash or rerun. Inputs: 50 files hashed before and after, 0 changed.

**Verdict, by round 2's section 6.10: crypto daily No edge, equities daily No edge, as on 10-18.**
Neither P1 nor P2 passes Holm in either class. The crypto 4h class (S4) agrees in words.

| Test | Old stat, p, Holm | Exact stat, n, p, Holm | Old → exact verdict |
|---|---|---|---|
| P1-crypto, dot | −0.086, 0.719, 1.000 | +0.022, 59, 0.614, 1.000 | No edge → No edge |
| P2-crypto, trigger | +0.398, 0.067, 0.376 | +0.302, 28, 0.129, 0.518 (Suggestive only by count) | No edge → No edge |
| P3-crypto, waiting | +0.274, 0.075, 0.376 | +0.122, 59, 0.285, 0.854 | No edge → No edge |
| P1-equities, dot | +0.053, 0.095, 0.376 | −0.006, 1,176, 0.547, 1.000 | No edge → No edge |
| P2-equities, trigger | +0.081, 0.063, 0.376 | +0.118, 586, **0.0241**, 0.145 | No edge → **Suggestive** |
| P3-equities, waiting | −0.012, 0.601, 1.000 | +0.065, 1,176, 0.092, 0.462 | No edge → No edge |

**The one change: the trigger-wave entry in equities is now "Suggestive".** That word is round 2's,
section 12: a raw p below 0.05 that fails Holm.
- Its four criteria: Holm no (0.145); held-out +0.133R on 46, yes; breadth 16 of 26 assets, yes;
  noise kept 38%, under the 50% needed.
- Section 6.10 still says the entry has no edge in this class.
- It is the same test that came closest on 10-18 (p 0.063). The exact indicators moved it under
  0.05, not under Holm.

**The grades still do nothing.**
- Equities, dot entry: grade A excess +0.049 (p 0.29); A minus C +0.085 (p 0.31).
- Equities, trigger entry: grade A excess +0.028 (p 0.46); A minus C −0.106 (p 0.77).
- No A > B > C ordering in either entry.
- Crypto grades are not testable (A has 14 E0 and 4 E1 trades). The A∪B fallback gives +0.399 at
  p 0.33, Suggestive only by count.

**Secondary (Family S): nothing passes Holm.** The smallest raw p are S6-equities 0.062 (HTF Buy dot,
20 against 566, Suggestive only by count) and S4-P1-crypto4h 0.080.
- Old leads gone: S2b-crypto 0.042 → 0.123; S4-P2-crypto4h 0.014 → 0.212.

**Exits (Family X): two pass Holm in equities, as on 10-18.**
- X4-E0-equities, the lab ladder after the dot: +0.148R against random +0.009R, Holm 0.008. It
  passed on 10-18 too (Holm 0.008).
- X3-E0-equities, the first bearish divergence after the dot: +0.099R against −0.018R, Holm 0.0225.
  On 10-18 it did not pass (0.90).
- No longer passing: X4-E1-equities (0.008 → 0.565) and X1-E0-equities (0.042 → 0.112).
- Section 6.10: a good exit does not rescue an entry with no edge.

**Held-out signs:** every P and G test with a development statistic agrees in sign except P3-crypto
(+0.122 → −0.172). That includes equities grade A, +0.248 (E0, 33) and +0.408 (E1, 17).

### Predictions so far (section 5 of the plan)

- "All four verdicts stay No edge": right for rounds 1 and 2. One headline test, P2-equities, moved
  from No edge to Suggestive, which is not an edge.
- "Grade counts move by tens of percent, not collapse": right. Development-era equities grade B
  fell 58% in round 1 (90 → 37), which is at the top of that range. Non-vetoed setups fell 16 to
  22% in round 2.

## Step 6: round 3, the switch and its counts (before its full run)

**The switch** is commit 6c49ef1d, made before any round-3 run on the exact indicators.
- `--priors exact` selects round 2's re-run development-era counts for the plumbing check. It also
  selects rounds 1 and 2's re-run raw p for the cross-round Holm.
- The test IDs are the same as before: 39 for round 1 and 51 for round 2. A new test pins them.
- One difference from the 10-18 table: round 2's 16 exit tests enter at their printed raw p, not at
  1. Round 3's section 7.2 says earlier rounds enter "with the raw p-values printed in their
  findings". On 10-18 only their Holm p was printed. The re-run prints the raw ones, listed here:

  | Exit test | Crypto E0 | Crypto E1 | Equities E0 | Equities E1 |
  |---|---|---|---|---|
  | X1 | 0.0555 | 0.4488 | 0.0080 | 0.0445 |
  | X2 | 0.9585 | 0.3583 | 0.1244 | 0.3008 |
  | X3 | 0.4508 | 0.9500 | 0.0015 | 0.1824 |
  | X4 | 0.0490 | 0.1399 | 0.0005 | 0.0435 |

  Every other round 1 and 2 p is in the step 4 and 5 output. Not-testable tests enter at 1.

**Without the switch, round 3 stopped with exit 4,** as section 3 of the plan predicted. Round 2's
10-18 counts (334/158, 70/33, 1,423/721) did not reproduce.

**With the switch, the plumbing check matched exactly:** 260/131, 59/28, 1,176/586. Inputs: 50
files, 0 changed.

**Development-era setups at Cipher SR support, old (10-18) → exact:**

| Class | E0 at support | E1 at support | L3 random bars at support |
|---|---|---|---|
| Crypto 4h | 128 of 334 (38%) → 41 of 260 (16%) | 53 of 158 → 36 of 131 (27%) | 5,197 → 3,268 |
| Crypto daily | 25 of 70 → 13 of 59 | 21 of 33 → 10 of 28 | 1,216 → 814 |
| Equities | 435 of 1,423 (31%) → 144 of 1,176 (12%) | 202 of 721 → 103 of 586 (18%) | 29,243 → 19,797 |

- **The exact Cipher SR puts far fewer setups at support.** Its level is a 2-bar pivot that becomes
  live three bars later and steps. The old replica had a level near most lows.
- **Testability changes (section 5):**
  - Crypto 4h: every headline test is still confirmatory and checkable (L1, L2, L3, XR/X4 for both
    entries). A-E0 becomes not testable (29 against 12).
  - **Crypto daily: L1 and L2 for both entries become not testable** (13 and 10 at support; they
    were Suggestive only). Only L3 is left.
  - **Equities: the held-out check is lost for almost everything.** Only 9 dot setups and 2 trigger
    setups are at support from 2023 on. Development stays confirmatory.
- Equities held-out E1 is flagged for an extreme share (4.3% at support). Section 5 says nothing
  changes because of it.

## Step 7: the Cipher A battery, full run

Run once, with no crash or rerun. Seeds and draws are as on 10-17: 2,000 block permutations, block 20,
horizon 20 bars, entry at the next open. The inputs are the same 8 series.

**Verdict, by its own rule (the family-wise max-|t| null at 0.05): No edge, as on 10-17.** No cell
survives in either family.

| Family | 10-17 (61caa0d4) | Exact |
|---|---|---|
| Signal by signal: cells attempted / tested / Cipher A | 480 / 441 / 266 | 352 / 314 / 186 |
| Cipher A cells with raw p ≤ 0.05 | 20 (about 13 expected) | 15 (about 9 expected) |
| Cells surviving the family-wise null | 0 (best pFW 0.38) | 0 (best pFW 0.11) |
| Combinations: attempted / tested | 96 / 70 | 96 / 67 |
| Combination cells with raw p ≤ 0.05 | 7 (about 4 expected) | 6 (about 3 expected) |
| Cells surviving the family-wise null | 0 (best pFW 0.47) | 0 (best pFW 0.15) |

- **Why the signal family is smaller:** two things in it no longer exist. These are code removals,
  not choices made in this re-run.
  - The G6 "DC window" gate went with Loukas Cycle Detection (9e63291d, 10-22).
  - The Cipher B gold dot was removed (c47ad6b3, 10-22).
  - So 12 signals × 5 gates became 11 × 4, on each of the 8 assets.
- **Best cells,** all on a handful of trades and none surviving the family:
  - Cipher B Green Dot, gold, Faber gate: 13 events, 92% hit, +3.31% excess, pFW 0.11. The same cell
    is K2's best combination (pFW 0.15).
  - Cipher A Yellow X short under VR<1: ETH 8 events, BTC 12, gold 15; pFW 0.49 to 0.56.
- **This is the dip-filter result again, as on 10-17.** On 10-17 the family's top cell was gold under
  Faber, at pFW 0.38.
- **K1 (B green dot + ribbon up) has 0 events on every asset.** It had fewer than 5 on 10-17. With the
  exact Buy dot (fast wave below −60), a dot never fires while EMA 11 is above EMA 33. I checked the
  ribbon indexing: `RibbonLine(1)` is EMA 11 and `RibbonLine(7)` is EMA 33, unchanged since the
  ninth line was added. So K2 is every dot.
- **The overlap structure holds.** Red Diamond, Blood Diamond and Yellow X are 100% within a bar of
  a Cipher B bear wave cross; Yellow Diamond is within a bar of a bull cross 76% of the time (77% on
  10-17).

## Step 8: round 3, full run (`--priors exact`)

Run once, with no crash or rerun. Inputs: 50 files, re-hashed after the run, 0 changed.

**Verdict, in the pre-registration's words: crypto 4h (the headline) No edge, crypto daily No edge,
equities daily No edge, as on 10-18.** None of the 29 round-3 tests passes the cross-round Holm,
and none passes the placebo gate except L1-E1 crypto 4h, which fails everything else. **No round-3
test is Suggestive:** the smallest raw p is 0.054 (L2-E1 crypto 4h). On 10-18 four were
Suggestive: A-E0 crypto 4h, X4-E0 crypto 4h and the two equities X4s.

| Headline test (crypto 4h) | 10-18 stat, raw p | Exact stat, raw p | Exact held-out (n, stat, p) |
|---|---|---|---|
| L1-E0, at support vs not | −0.225R (128 vs 206), 0.93; held-out 184, −0.118 | −0.395R (41 vs 219), 0.95 | 60, +0.347, 0.024: does not agree (dev not above 0) |
| L1-E1 | −0.109R (53 vs 105), 0.75; held-out 92, +0.082 | +0.266R (36 vs 95), 0.21 | 53, +0.303, 0.073: agrees |
| L2-E0, vs random at-support bars | +0.026R (128), 0.32; held-out 184, +0.053 | −0.081R (41), 0.62 | 60, +0.506, 0.004: does not agree (dev not above 0) |
| L2-E1 | +0.253R (53), 0.091; held-out 92, +0.143 | +0.365R (36), 0.054 | 53, +0.296, 0.11: does not agree (p above 0.10) |
| L3, at-support random vs all random | −0.095R (5,197 bars), 1.00 | −0.117R (3,268), 0.9999 | 4,982, −0.005, 0.61 |

- **The trigger at support (L2-E1) is again the one headline test that looks like something.**
  - Development: +0.365R on 36, raw p 0.054. The held-out era: +0.296R on 53, p 0.11.
  - Noise kept 42%. The cross-round Holm is 1.00.
  - It is the same picture as on 10-18 (+0.253R, p 0.091), on fewer trades: No edge, and not
    Suggestive.
- **L1-E1 changed sign,** −0.109R → +0.266R. It is still not significant (p 0.21), and noise keeps
  −28%.

- **Crypto daily:** all 8 L1, L2 and exit tests are not testable (13 dot and 10 trigger setups at
  support, under 15). L3 is confirmatory and No edge (−0.133R, p 0.99).
- **Equities:** every test is confirmatory in development and No edge (p 0.09 to 0.92). Every
  held-out check is lost (9 and 2 at support), as forecast in step 6.
- **Family A (older vs newer levels)** is not testable in crypto 4h now (29 vs 12 and 5 vs 31). On
  10-18, A-E0 was the round's best lead (+0.567R, p 0.038).
- **Exits:** X4 beats its random exits in development in every class, but not significantly
  (p 0.09 to 0.15). It agrees in the held-out era in crypto 4h (p 0.054 and 0.041). On 10-18, X4-E0
  crypto 4h had p 0.0041.
- **The one oddity, which decides nothing.** Crypto 4h dot setups at support did worse than those
  away from support in development (−0.395R), and better in the held-out era (+0.347R, p 0.024; L2
  +0.506R, p 0.004).
  - The sign flips between eras, so by the plan's rule it is not agreement.
  - It is not a lead under any rule written before the run.
- **The cross-round Holm**, over 119 tests with the exact round 1 and 2 p's:
  - **Nothing in any round survives.**
  - Round 2's exits now enter at their raw p, instead of at 1 as on 10-18, where round 2's findings
    printed only Holm p's. So the closest call is R2 X4-E0 equities: raw 0.0005, cross-round Holm
    0.0595.
  - 0.0005 is the floor that round 2's 2,000 random books allow (1 ÷ 2,001). The test could not have
    gone lower, and the first Holm step needs 0.00042. So round 2's exits could never pass this
    correction, whatever the data.
  - Then R2 X3-E0 equities: raw 0.0015, Holm 0.177. Then R1 P3-equities: raw 0.0061, Holm 0.71.
- **Section 7.5's sentences hold in all three classes, word for word.** Dots and triggers at Cipher
  SR support do no better than random at-support bars. Being at support is no better than a random
  bar. The method has no edge as written.
  - In crypto 4h E0 and in equities E0 and E1, at-support setups did worse than setups away from
    support. The plan's own note says that is not a "buy away from levels" rule without its own
    pre-registration.

## Step 9: exploratory, the Ciphers on Heikin-Ashi candles (section 6)

The switch is `--cipher-candles ha` (82b8adb7). It stamps the Heikin-Ashi chart parameter on
Cipher A, Cipher B and Cipher SR only. Entries, stops, ATR, the higher-timeframe context and fills
stay on the real bars. Rounds 1 and 3 refuse it: round 1, asked with `--counts-only`, stopped with
exit 2. Round 2 and the battery each ran once with it, with exit 0; inputs re-hashed, 0 changed.
**Nothing here is a verdict.** Section 6 allows only "worth its own pre-registration on new data" or
"nothing".

**Round 2 on Heikin-Ashi:** every headline reads No edge, by the run's own criteria, in both classes.

| Test (development era) | Exact, candles | Exact, Heikin-Ashi | Heikin-Ashi held-out |
|---|---|---|---|
| P1 crypto, dot at next open | +0.022 (59), p 0.61 | +0.151 (68), p 0.19 | −0.073 (70) |
| P2 crypto, trigger entry | +0.302 (28), p 0.13 | +0.196 (34), p 0.19 | −0.022 (36) |
| P1 equities | −0.006 (1,176), p 0.55 | −0.010 (1,322), p 0.61 | +0.059 (107) |
| P2 equities | +0.118 (586), p 0.024 | +0.082 (662), p 0.084 | +0.566 (56) |
| S4-P2 crypto 4h, trigger entry | +0.094 (131), p 0.21 | **+0.338 (141), p 0.0024, Holm in S 0.046** | **+0.037 (233)** |

- **The one number that moves is the crypto 4h trigger entry.**
  - It was +0.268R on 158 (p 0.014) on 10-18's replicas, +0.094R on the exact Ciphers on candles,
    and +0.338R on Heikin-Ashi.
  - It does not carry into 2023 on: +0.037R on 233 trades, against +0.338R before.
  - So this run says **nothing** about it: a development-era number that vanishes in the held-out
    era is what a lucky cut looks like.
  - It is also the third version of the same entry that this lab has looked at, so its p is not
    worth its face value.
  - If it is ever tested again, it needs its own pre-registration, on 4h bars after 2026-10-18 only.
- **Grades:** nothing (GA/GO p 0.16 to 0.59). Crypto grade A E0 now has 19 setups, which is
  Suggestive only by count, at p 0.26.
- **Exits:** the equities E0 exits beat their random exits as on candles, still at the floor p. X3
  is Holm 0.008; X1 is Holm 0.038, newly; X4 is Holm 0.11, no longer. It is the same exit result on
  the same trades, already seen twice.

**The battery on Heikin-Ashi:** nothing.

- 352 cells attempted, 315 tested, 187 Cipher-A-dependent.
- 14 of them at raw p ≤ 0.05, with about 9 expected by chance. 0 survive the family-wise null; the
  best pFW is 0.37 (gold Cipher B Green Dot under Faber, 14 events). On candles: 15, 0, best 0.11.
- Combinations: 5 of 67 at p ≤ 0.05, 0 surviving, best pFW 0.27.
  - That best cell is a NEGATIVE one: K4, a Blue Triangle with a green dot within 5 bars, on gold,
    5 events.

**Heikin-Ashi does not rescue the Ciphers.** It is worth a pre-registration only if the crypto 4h
trigger result shows up on bars this lab has not used.

## Total tries

| # | Command | Stage | Result |
|---|---|---|---|
| 1 | `setup-grader` | `--counts-only` | recorded (step 2) |
| 2 | `setup-grader` | full | No edge (step 4) |
| 3 | `setup-grader-mc2` | `--counts-only` | recorded (step 3) |
| 4 | `setup-grader-mc2` | full | No edge (step 5) |
| 5 | `setup-grader-mc3` | full, without `--priors exact` | exit 4, as planned (step 6) |
| 6 | `setup-grader-mc3 --priors exact` | `--counts-only` | recorded (step 6) |
| 7 | `setup-grader-mc3 --priors exact` | full | No edge (step 8) |
| 8 | `cipher-a-battery` | full | No edge (step 7) |
| 9 | `setup-grader --cipher-candles ha` | `--counts-only` | refused, exit 2; computes nothing |
| 10 | `setup-grader-mc2 --cipher-candles ha` | full, exploratory | nothing (step 9) |
| 11 | `cipher-a-battery --cipher-candles ha` | full, exploratory | nothing (step 9) |

No run crashed and none was rerun. Nothing was run that is not listed here.

## The predictions (section 5), checked

- **"All four verdicts stay No edge": right.**
  - Round 1, round 2, round 3 and the battery all read No edge on the exact Ciphers.
  - Nothing in any of them passes its own correction.
  - Round 3's cross-round Holm passes nothing across all three rounds.
- **"The setup counts change by tens of percent, not collapse": right for rounds 1 and 2**
  (steps 2 and 3).
- **"Round 3's support will be different levels, still No edge": right on the verdict, short on
  the size.**
  - I did not predict how much fewer setups would sit at support: crypto 4h E0 fell from 128 to 41,
    and equities E0 from 435 to 144.
  - Crypto daily's location tests became not testable, and equities lost every held-out check.
  - So round 3's No edge is partly a no-power result in those classes, and it says so.
