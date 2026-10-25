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
