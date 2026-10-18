# Setup grader round 3, Market Cipher with Cipher SR location: findings

Run 2026-10-18. Stage 2 of 2. The plan is `docs/SETUP_GRADER_ROUND3_MC_SR_PREREG.md`, committed
(97def0cc) before any result existed. The harness is `StrategyLab setup-grader-mc3`
(`AccessibleTrader.StrategyLab/SetupGraderMc3Command.cs` and `SetupGraderMc3Core.cs`). The order is
in the history:

1. f8a2be88: the harness and its tests, before any count or outcome was computed.
2. The counts-only stage, written into this file before the full run (this commit).
3. The full run, and the rest of this file.

## The counts-only stage (section 7.6), before any outcome

Run 1 of the command, `--counts-only`. It builds no R array and computes no outcome. Section 7.6
says these counts may decide only what section 5 already says: confirmatory, Suggestive only, not
testable, or not checkable. No threshold, band, window or definition was changed because of them.

"Dev" is setups whose dot is before 2023-01-01; "held-out" is 2023 on. A setup is "at support" when
its signal bar (the dot for E0, the trigger for E1) reached within half an ATR above the Cipher SR
support that was live at the previous bar's close, and closed above it.

### The plumbing check: round 2 reproduced

Round 3 calls round 2's own code to build the setups, so this checks the plumbing, not a copy.
Development-era non-vetoed setups and E1 trades:

| Class | Setups | Round 2 | E1 trades | Round 2 |
|---|---|---|---|---|
| Crypto 4h | 334 | 334 | 158 | 158 |
| Crypto daily | 70 | 70 | 33 | 33 |
| Equities | 1,423 | 1,423 | 721 | 721 |

All six match exactly. Every one of the 50 input files had the same sha256 as round 2's findings
list, before and after the run.

### At support or not, development era

Every setup had a defined Cipher SR support at its signal bar (no setup was excluded for "no level
yet"). The reasons a setup is not at support are "low more than 0.5 ATR above the level" and "closed
at or below the level".

| Class, entry | At | Not | Share at | Too far | Closed below |
|---|---|---|---|---|---|
| Crypto 4h, E0 | 128 | 206 | 38% | 112 | 94 |
| Crypto 4h, E1 | 53 | 105 | 34% | 93 | 12 |
| Crypto daily, E0 | 25 | 45 | 36% | 29 | 16 |
| Crypto daily, E1 | 21 | 12 | 64% | 11 | 1 |
| Equities, E0 | 435 | 988 | 31% | 428 | 560 |
| Equities, E1 | 202 | 519 | 28% | 427 | 92 |

No share is below 10% or above 90%, so the half-ATR band does filter, unlike round 1's location.

Held-out era, at support: crypto 4h E0 184 of 476, E1 92 of 254; crypto daily E0 37 of 90, E1 13
of 47; equities E0 47 of 131, E1 18 of 72.

By grade, development era, at support (and not):

| Class, entry | A | B | C |
|---|---|---|---|
| Crypto 4h, E0 | 12 (41) | 42 (48) | 74 (117) |
| Crypto 4h, E1 | 12 (13) | 12 (30) | 29 (62) |
| Equities, E0 | 98 (283) | 96 (191) | 241 (514) |
| Equities, E1 | 45 (134) | 37 (103) | 120 (282) |

Crypto daily's grades are 3, 7 and 15 at support for E0, and 3, 7 and 11 for E1.

### The level at at-support setups

The prediction in section 1.4 holds: the support at a setup is almost always young.

| Class, entry | Older | Newer | Median age | Quartiles |
|---|---|---|---|---|
| Crypto 4h, E0 | 23 | 105 | 3 bars | 1 to 4 |
| Crypto 4h, E1 | 0 | 53 | 5 bars | 2 to 7 |
| Crypto daily, E0 | 2 | 23 | 2 bars | 1 to 3 |
| Crypto daily, E1 | 0 | 21 | 3 bars | 1 to 4 |
| Equities, E0 | 109 | 326 | 3 bars | 2 to 5 |
| Equities, E1 | 12 | 190 | 5 bars | 2 to 9 |

"Older" means the level's pivot bar is before the bear cross where the anchor wave began. **No
crypto trigger at support used an older level.** At the trigger, the support is the low the anchor
wave itself made, which is Casper's "one touch ... retest".

Other counts (development era, all non-vetoed setups):

- Setups whose signal bar is a replacement bar (a new support confirmed that bar): crypto 4h E0 82
  (37 of them at support), E1 37 (16); crypto daily E0 10 (2), E1 10 (5); equities E0 375 (126), E1
  170 (72). On those bars the old level is the one tested, as section 2.3 says, and the harness
  checks on every bar that this old level equals Cipher SR's own `Support Zone_replaced`.
- At-support setups that are also a "test" in our Cipher SR's own sense (low at or below the level,
  close above it): crypto 4h E0 58, E1 19; crypto daily E0 8, E1 4; equities E0 215, E1 87.

### Random bars at support (the L2 and L3 strata)

Development era, bars whose 20-bar outcome the harness can define. Context V is reported, not tested.

| Class | A | B | C | V |
|---|---|---|---|---|
| Crypto 4h | 15% of 7,692 | 17% of 12,736 | 22% of 8,598 | 21% of 6,859 |
| Crypto daily | 12% of 1,848 | 15% of 3,178 | 22% of 2,345 | 23% of 1,876 |
| Equities | 12% of 96,045 | 10% of 76,916 | 17% of 55,206 | 21% of 54,799 |

L3's at-support bars in contexts A to C: crypto 4h 5,197 (held-out 7,244), crypto daily 1,216
(1,583), equities 29,243 (2,908). No bar had an undefined level.

### What the counts decide

Section 5's rules: 30 or more development-era events is confirmatory, 15 to 29 is Suggestive only,
below 15 is not testable. The held-out check needs 30.

Crypto 4h (primary):

| Test | Development | Held-out |
|---|---|---|
| L1-E0 | 128 vs 206, confirmatory | 184 vs 292, checkable |
| L1-E1 | 53 vs 105, confirmatory | 92 vs 162, checkable |
| L2-E0 | 128, confirmatory | 184, checkable |
| L2-E1 | 53, confirmatory | 92, checkable |
| L3 | 5,197 bars, confirmatory | 7,244, checkable |
| A-E0 | 23 vs 105, Suggestive only | 29 vs 155, not checkable |
| A-E1 | 0 vs 53, not testable | 1 vs 91, not checkable |
| XR, X4 E0 | 128 entries, confirmatory | 184, checkable |
| XR, X4 E1 | 53 entries, confirmatory | 92, checkable |

Crypto daily (secondary):

| Test | Development | Held-out |
|---|---|---|
| L1-E0 | 25 vs 45, Suggestive only | 37 vs 53, checkable |
| L1-E1 | 21 vs 12, not testable | 13 vs 34, not checkable |
| L2-E0 | 25, Suggestive only | 37, checkable |
| L2-E1 | 21, Suggestive only | 13, not checkable |
| L3 | 1,216 bars, confirmatory | 1,583, checkable |
| XR, X4 E0 | 25 entries, Suggestive only | 37, checkable |
| XR, X4 E1 | 21 entries, Suggestive only | 13, not checkable |

Equities (secondary):

| Test | Development | Held-out |
|---|---|---|
| L1-E0 | 435 vs 988, confirmatory | 47 vs 84, checkable |
| L1-E1 | 202 vs 519, confirmatory | 18 vs 54, not checkable |
| L2-E0 | 435, confirmatory | 47, checkable |
| L2-E1 | 202, confirmatory | 18, not checkable |
| L3 | 29,243 bars, confirmatory | 2,908, checkable |
| XR, X4 E0 | 435 entries, confirmatory | 47, checkable |
| XR, X4 E1 | 202 entries, confirmatory | 18, not checkable |

So, before any outcome:

- **Crypto 4h's five headline tests are all confirmatory and checkable.**
- **Family A is weak by construction.** A-E1 is not testable in crypto 4h: no trigger at support
  used an older level. A-E0 is Suggestive only (23 older levels), and its held-out check is not
  checkable (29 older levels, one short of 30).
- **Crypto daily is as thin as section 4.2 predicted:** L1-E1 is not testable, L1-E0 and both L2
  tests are Suggestive only, and only L3 is confirmatory.
- **Equities E1 can be at most Suggestive:** its held-out era has 18 trades at support, under 30,
  so L1-E1, L2-E1 and both E1 exits are capped at Suggestive.

### The placebo labels of the first draw (labels only)

One draw (draw 0) of shifted levels, counted on the development-era setups and bars, with no
outcome:

| Class | E0 at placebo support | E1 at placebo support | Bars (A-C) |
|---|---|---|---|
| Crypto 4h | 7 of 334 | 0 of 158 | 977 |
| Crypto daily | 0 of 70 | 0 of 33 | 231 |
| Equities | 36 of 1,423 | 10 of 721 | 8,725 |

**This looks bad for the placebo gate on L1, and it is written down here before any outcome.** A
level moved 3 to 6 ATR away from a fresh swing low is almost never within half an ATR of a setup's
low with the close above it. Section 4.4 says a draw with an empty group is dropped and counted, and
that the gate needs at least 500 usable draws. If most draws are dropped (as draw 0 already is for
crypto E1 and crypto daily E0), **L1 cannot pass the gate, and so cannot be Supported or Fragile,
in those classes.** For L3 the placebo labels are plentiful (977, 231 and 8,725 bars), so the gate
there is a real comparison. Nothing was changed because of this.
