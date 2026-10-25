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
