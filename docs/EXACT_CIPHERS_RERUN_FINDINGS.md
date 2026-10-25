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
