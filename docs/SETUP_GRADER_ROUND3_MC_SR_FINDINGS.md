# Setup grader round 3, Market Cipher with Cipher SR location: findings

Run 2026-10-18. Stage 2 of 2. The plan is `docs/SETUP_GRADER_ROUND3_MC_SR_PREREG.md`, committed
(97def0cc) before any result existed. The harness is `StrategyLab setup-grader-mc3`
(`AccessibleTrader.StrategyLab/SetupGraderMc3Command.cs` and `SetupGraderMc3Core.cs`). The order is
in the history:

1. f8a2be88: the harness and its tests, before any count or outcome was computed.
2. 0353080e: the counts-only stage, written into this file before the full run. That section is
   below, unchanged.
3. The full run, a one-line reporting fix and its rerun, and the rest of this file.

## The verdict, in the pre-registration's words

**Crypto 4h (the headline): No edge. Crypto daily: No edge. Equities daily: No edge.** None of the
29 round-3 tests passed the cross-round Holm, and none passed the placebo gate. Four tests are
Suggestive (a raw p below 0.05 that did not survive the cross-round Holm), and none of them is a
headline test.

Section 7.5's sentences apply to all three classes, word for word:

- **At Cipher SR support, a Buy dot bought at the next open is no better than a random bar that is
  also at Cipher SR support, in the same environment.**
- **At Cipher SR support, the trigger-wave entry is no better than a random bar that is also at
  support, in the same environment.**
- **Setups at Cipher SR support do no better than setups away from it** (for the dot and for the
  trigger).
- **Being at Cipher SR support, by itself, is no better than a random bar in the same
  environment.**
- **Neither L2-E0 nor L2-E1 passes, so the Market Cipher method as written here (environment, then
  Cipher SR support, then the dot or the trigger) has no edge in that class, whatever L1, L3,
  Family A or the exits say.**

That holds for crypto 4h, so section 7.5's last sentence applies:

**Across three pre-registered rounds, no Market Cipher B entry rule tested in this lab has beaten a
matched random entry.**

Where the point estimates point the wrong way, section 7.5 asks the write-up to say so:

- **In crypto 4h, setups at support did worse than setups away from it:** by 0.225R for the dot and
  0.109R for the trigger. In equities the gap was about zero (−0.003R and −0.040R). This is not a
  "buy away from levels" rule; that would need its own pre-registration.
- **Random bars at Cipher SR support did worse than random bars in general,** in all three classes
  and both eras (crypto 4h −0.095R over 5,197 bars). Placebo lines 3 to 6 ATR away did better than
  the real levels in crypto.

The method verdict (section 7.4) is No edge for both entries in every class, because L2 is not
Supported anywhere.

## The answers, briefly

- **Did round 2's counts reproduce?** Yes, exactly: 334 setups and 158 trigger trades in crypto 4h,
  70 and 33 in crypto daily, 1,423 and 721 in equities.
- **Does being at Cipher SR support help on its own (L3)?** No. A random bar at support did worse
  than a random bar in the same environment: −0.095R in crypto 4h (raw p 1.00), −0.043R in crypto
  daily (p 0.83), −0.004R in equities (p 0.73). The held-out era is negative in all three too.
- **Do setups at support beat setups away from it (L1)?** No. Crypto 4h: −0.225R (dot) and −0.109R
  (trigger). Equities: −0.003R and −0.040R. Crypto daily's dot was +0.448R (25 against 45, raw p
  0.089), but its held-out sign flipped (−0.529R) and it kept 6% under noise.
- **Does the dot or the trigger add anything at support (L2)?** Not detectably.
  - The trigger in crypto 4h is the largest headline number: +0.253R over a random bar that is also
    at support, on 53 trades, raw p 0.091, held-out +0.143R on 92 (p 0.12), 95% kept under noise,
    positive on all 3 assets. It fails the cross-round Holm (1.00) and does not reach p 0.05 even
    before correction, so it is No edge, not Suggestive.
  - The dot in crypto 4h: +0.026R (p 0.32). Equities: +0.058R (dot) and +0.032R (trigger). Crypto
    daily: +0.163R and +0.502R on 25 and 21 trades, not significant.
- **Did anything beat the placebo levels?** No. Every L1 and L3 test failed the gate, and in crypto
  L3's real value was below the placebo median. In crypto daily, L1's gate was close to unpassable,
  as the counts-only stage warned: a level moved 3 to 6 ATR rarely sits under a setup's low, so most
  draws had no setup at "support". The one real statistic above its placebo 95th percentile (crypto
  daily's dot, +0.448R) had only 403 usable draws, under the 500 required.
- **Best exit:** the first bearish divergence (X4), again. It had the highest mean in every population
  and beat its random exits in crypto 4h for the dot entry (+0.506R against +0.063R, raw p 0.004,
  the smallest p of round 3), but the cross-round Holm was 0.48 and the held-out era kept under half
  of it. Cipher SR's own exit (XR, the next resistance) lost to its random exits in five of six
  populations.
- **Held-out agreement:** no test met the held-out criterion that also had a positive development
  statistic, apart from L2-E0 in equities (+0.261R on 47, p 0.086), whose development result was
  not significant and kept 1% under noise.
- **Suggestive (leads, not edges):** older levels beat newer ones for the dot in crypto 4h (A-E0,
  +0.567R on 23 against 105, raw p 0.038, held-out not checkable); and the X4 exit in crypto 4h
  (dot entry) and in equities (both entries). The equities X4 results are round 2's, already seen,
  re-tested on a subset of the same trades. They are not new evidence.

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

No setup was dropped at the outcome stage, so the full run's populations equal the counts above.

## The headline: crypto 4h (Family L, 5 tests)

Development era, non-vetoed setups, net of 15 bps a side. R uses round 2's harness: entry at the
next open, 1 ATR stop, 2R target, 20 bars.

- **Context excess** (round 2's control) is a trade's R minus the mean R of every bar on the same
  asset, era and higher-timeframe context state as its signal bar.
- **Location excess** (L2's control) is a trade's R minus the mean R of the bars in that same
  stratum that are also at support.

What sits behind the tests:

| Crypto 4h, dev | n | Mean R | Context excess | Location excess |
|---|---|---|---|---|
| E0 at support | 128 | −0.226 | −0.081 | +0.026 |
| E0 not at support | 206 | +0.003 | +0.144 | |
| E1 at support | 53 | +0.005 | +0.195 | +0.253 |
| E1 not at support | 105 | +0.137 | +0.304 | |

A random bar at support in the same environment is a worse bar to buy than a random bar in that
environment. So an at-support setup can beat its location control (L2 positive) while losing to the
setups away from support (L1 negative).

The five tests. "Family Holm" is the within-family Holm, printed for comparison with rounds 1 and 2;
it decides nothing. "Cross Holm" is the Holm over all 119 tests of rounds 1 to 3, which decides.

| Test | n | Statistic | Raw p | Family Holm | Cross Holm |
|---|---|---|---|---|---|
| L1-E0 | 128 vs 206 | −0.225 | 0.925 | 1.00 | 1.00 |
| L1-E1 | 53 vs 105 | −0.109 | 0.746 | 1.00 | 1.00 |
| L2-E0 | 128 | +0.026 | 0.318 | 1.00 | 1.00 |
| L2-E1 | 53 | +0.253 | 0.091 | 0.45 | 1.00 |
| L3 | 5,197 bars | −0.095 | 1.000 | 1.00 | 1.00 |

Gross statistics, before costs: L1 −0.233 and −0.105, L2 −0.004 and +0.241, L3 −0.089. Costs do
not change the picture.

The criteria, test by test:

| Test | Effect +0.05 | Held-out | Breadth | Noise kept | Placebo |
|---|---|---|---|---|---|
| L1-E0 | no | −0.118 (184), p 0.83 | 1 of 3 | n/a | fails |
| L1-E1 | no | +0.082 (92), p 0.39 | 1 of 3 | n/a | fails |
| L2-E0 | no | +0.053 (184), p 0.23 | 2 of 3 | 258% | not used |
| L2-E1 | yes | +0.143 (92), p 0.12 | 3 of 3 | 95% | not used |
| L3 | no | −0.024 (7,244), p 0.86 | 0 of 4 | n/a | fails |

"n/a" for noise means the clean statistic is not above zero, so a retention ratio says nothing.

**The trigger at support (L2-E1) is the one headline test that looks like something:** +0.253R on 53
trades, positive on BTC, ETH and XRP, 95% kept under noise, and the held-out era agrees in sign and
keeps more than half the size (+0.143R on 92). But its raw p is 0.091 in development and 0.12 in the
held-out era, and it fails the cross-round Holm at 1.00. It is No edge, and it is not even Suggestive,
because Suggestive needs a raw p below 0.05. It is round 2's crypto 4h trigger result (+0.268R on
158, raw p 0.014) cut down to its at-support third, and it looks the same size. The location did
not sharpen it.

Per asset, development era:

| Asset | E0 at / not | L1-E0 | L2-E0 | E1 at / not | L1-E1 | L2-E1 | L3 |
|---|---|---|---|---|---|---|---|
| BTC | 47 / 57 | +0.095 | +0.089 | 21 / 23 | −0.424 | +0.321 | −0.071 |
| ETH | 33 / 67 | −0.373 | −0.115 | 11 / 40 | +0.004 | +0.396 | −0.152 |
| XRP | 46 / 77 | −0.382 | +0.078 | 20 / 38 | −0.020 | +0.150 | −0.067 |
| SOL | 2 / 5 | −0.687 | −0.333 | 1 / 4 | −0.986 | −0.675 | −0.416 |

L3's at-support bars: BTC 1,783, ETH 1,447, XRP 1,919, SOL 41, KAS 7. DOGE and TAO have no
development-era setups or bars with a defined context.

## The secondary classes (Family LS, 10 tests)

Crypto daily, weekly environment:

| Test | n | Statistic | Raw p | Cross Holm | Held-out |
|---|---|---|---|---|---|
| L1-E0 | 25 vs 45 | +0.448 | 0.089 | 1.00 | −0.529 (37) |
| L1-E1 | 21 vs 12 | not testable | | 1.00 | |
| L2-E0 | 25 | +0.163 | 0.188 | 1.00 | +0.001 (37) |
| L2-E1 | 21 | +0.502 | 0.237 | 1.00 | not checkable (13) |
| L3 | 1,216 bars | −0.043 | 0.834 | 1.00 | −0.073 (1,583) |

L1-E1 had only 12 triggers away from support (+0.109R, a count, not a result). Noise kept 6% of
L1-E0, a negative share of L2-E0, and 20% of L2-E1. Crypto daily's L1-E0 is the largest location
number in the study, and it reverses in the held-out era.

Equities daily, weekly environment:

| Test | n | Statistic | Raw p | Cross Holm | Held-out |
|---|---|---|---|---|---|
| L1-E0 | 435 vs 988 | −0.003 | 0.458 | 1.00 | +0.276 (47) |
| L1-E1 | 202 vs 519 | −0.040 | 0.582 | 1.00 | not checkable (18) |
| L2-E0 | 435 | +0.058 | 0.296 | 1.00 | +0.261 (47), p 0.086 |
| L2-E1 | 202 | +0.032 | 0.468 | 1.00 | not checkable (18) |
| L3 | 29,243 bars | −0.004 | 0.726 | 1.00 | −0.045 (2,908) |

Equities' dot at support beat a random bar at support by +0.058R (the same size as round 2's dot
against a random bar in the environment, +0.053R). It was not significant, it kept 1% under noise,
and breadth was 11 of 20 assets. Equities L3 is flat: being at support changed nothing.

**Where our Cipher SR is closest to the real one (crypto 4h), the location layer has no edge.**
Neither secondary class shows anything that passes, so section 7.4's sentence about a secondary
class being Supported does not arise.

## The older-level split (Family A)

Crypto 4h, tested. Location excess of at-support setups whose level's pivot bar is before the
anchor wave's bear cross ("older") minus those whose level was made by the setup's own move
("newer"):

| Test | n | Statistic | Raw p | Family Holm | Cross Holm |
|---|---|---|---|---|---|
| A-E0 | 23 vs 105 | +0.567 | 0.038 | 0.077 | 1.00 |
| A-E1 | 0 vs 53 | not testable | | | 1.00 |

A-E0 is **Suggestive**: a raw p below 0.05, 23 older levels (Suggestive only by count), the cross-round
Holm failed, and the held-out check is not checkable (29 older levels, one short of 30; their
difference was −0.030R). Noise kept 100%. Section 7.5 forbids promoting it to the headline. It is a
lead: in crypto 4h, a dot at a support that was on the chart before the down move began did better
than a dot at the anchor's own fresh low, on 23 cases.

Reported, not tested, in the secondary classes (location excess, older against newer):

- Crypto daily, E0: 2 older (+0.183) against 23 newer (+0.162). E1: no older level.
- **Equities, E0: 109 older (−0.167) against 326 newer (+0.133). The opposite direction from crypto
  4h**, on four times as many older levels.
- Equities, E1: 12 older (+0.700) against 190 newer (−0.010).

## The exits on the at-support trades (Family X, 12 tests)

School stop (round 2's: the anchor's swing low less 0.25 ATR, at least 0.5 ATR below the entry).

- **XR** exits at the next Cipher SR resistance above the entry: the resistance live at the previous
  bar's close, filled at that level or at the open if the bar opens above it.
- **X4** exits at the close of the first bar with a bearish divergence.
- **X5** is the 20-bar time exit, reported, not tested.

Each exit is compared with 20,000 random books that keep the entries and the stop and draw the exit
bar from that exit's own holding times. The tested statistic is the exit's mean R minus the random
books' mean.

| Population | Exit | Mean R | Random | Raw p | Cross Holm |
|---|---|---|---|---|---|
| Crypto 4h E0 (128) | XR | −0.142 | −0.083 | 0.754 | 1.00 |
| | X4 | +0.506 | +0.063 | 0.004 | 0.48 |
| Crypto 4h E1 (53) | XR | −0.064 | +0.191 | 0.954 | 1.00 |
| | X4 | +1.394 | +0.853 | 0.061 | 1.00 |
| Crypto daily E0 (25) | XR | +0.018 | +0.314 | 0.915 | 1.00 |
| | X4 | +0.616 | +0.494 | 0.332 | 1.00 |
| Crypto daily E1 (21) | XR | +0.203 | +0.359 | 0.722 | 1.00 |
| | X4 | +1.423 | +0.726 | 0.084 | 1.00 |
| Equities E0 (435) | XR | −0.006 | −0.027 | 0.285 | 1.00 |
| | X4 | +0.125 | +0.032 | 0.049 | 1.00 |
| Equities E1 (202) | XR | −0.068 | −0.005 | 0.913 | 1.00 |
| | X4 | +0.221 | +0.071 | 0.019 | 1.00 |

- **X5, the 20-bar time exit:** crypto 4h −0.004R (E0) and +0.428R (E1); crypto daily +0.598R and
  +0.677R; equities +0.068R and +0.015R.
- **In ATR units:**
  - X4: crypto 4h +0.82 (E0) and +2.01 (E1), against random +0.15 and +1.17; equities +0.37 and
    +0.29.
  - XR: crypto 4h −0.09 and −0.12.
- **Mean holds:** XR 7 to 11 bars, X4 18 to 33.

What this says:

- **Cipher SR's own exit has no skill.** XR exits after about 8 bars on average, and it lost to its
  random exits in five of six populations. Section 1.3's "the purple dotted lines represent exits"
  does not hold for our replica, on these trades.
- **The first bearish divergence is again the best exit.** It had the highest mean R in every
  population. In crypto 4h with the dot entry it beat its random books by +0.443R. That was round 3's
  smallest raw p (0.004), which would have passed the within-family Holm (0.049). But the cross-round
  Holm was 0.48, and the held-out era kept +0.127R on 184 (under half, p 0.14). **Suggestive.**
- **In equities, X4 is round 2's result, already seen,** re-tested on a subset of the same trades
  (raw p 0.049 and 0.019, Suggestive). Section 7.5 forbids reading it as new evidence, and it is not.
- **A good exit does not rescue the entries** (section 7.5).

## The placebo gate (section 4.4)

1,000 draws. Each draw moved every support level, for its whole life, by 3 to 6 times the ATR of the
bar it became live, up or down at random. Then L1 and L3 were recomputed on the moved levels, with
the outcomes unchanged. The gate passes when the real statistic is above the 95th percentile of the
usable draws, with at least 500 usable.

| Test | Real | Placebo median | 95th pct | Usable | Gate |
|---|---|---|---|---|---|
| L1-E0 crypto 4h | −0.225 | −0.306 | +0.587 | 1,000 | fails |
| L1-E1 crypto 4h | −0.109 | −0.922 | +1.727 | 566 | fails |
| L3 crypto 4h | −0.095 | +0.030 | +0.143 | 1,000 | fails |
| L1-E0 crypto daily | +0.448 | −0.938 | −0.306 | 403 | fails |
| L1-E1 crypto daily | +0.109 | −0.104 | −0.104 | 167 | fails |
| L3 crypto daily | −0.043 | +0.061 | +0.273 | 1,000 | fails |
| L1-E0 equities | −0.003 | −0.358 | +0.015 | 1,000 | fails |
| L1-E1 equities | −0.040 | +0.056 | +0.961 | 998 | fails |
| L3 equities | −0.004 | −0.020 | +0.017 | 1,000 | fails |

- **Nothing beat the placebo levels.**
- In crypto, **L3 on the real levels was below the placebo median.** A random bar near a line 3 to 6
  ATR from the real support did better than a random bar at the real support: 95% of crypto 4h's
  draws and 79% of crypto daily's were at or above the real value.
- The counts-only stage warned that L1's gate might be close to unpassable, because a moved level is
  rarely under a setup's low. That held for crypto daily (403 and 167 usable draws, under 500). It
  did not hold for crypto 4h's dots or for equities, where enough draws had setups at a moved level.
- **Crypto daily's dot is the one real L1 statistic above its placebo 95th percentile.** It still
  fails, on the 500-draw rule, and it also failed every other criterion.
- Section 7.5's sentence for "the placebo gate fails while L3 passes" does not arise, because L3
  passed nowhere.

## The held-out era (2023 on), criterion 3

Section 7.4 asks four things of the held-out era:

- at least 30 held-out events;
- the same sign as the development statistic;
- at least half its size;
- a one-sided p of 0.10 or less when the same test is run on the held-out era alone (its own 20,000
  draws).

The held-out era was read once, after every development test. Only tests whose development
statistic is positive can agree:

| Test | Dev | Held-out (n) | Held-out p | Agrees |
|---|---|---|---|---|
| L2-E0 crypto 4h | +0.026 | +0.053 (184) | 0.229 | no, p |
| L2-E1 crypto 4h | +0.253 | +0.143 (92) | 0.119 | no, p |
| L1-E0 crypto daily | +0.448 | −0.529 (37) | 0.883 | no, sign |
| L2-E0 crypto daily | +0.163 | +0.001 (37) | 0.565 | no, size and p |
| L2-E0 equities | +0.058 | +0.261 (47) | 0.086 | **yes** |
| A-E0 crypto 4h | +0.567 | −0.030 (29) | 0.608 | not checkable |
| X4-E0 crypto 4h | +0.443 | +0.127 (184) | 0.140 | no, size and p |
| X4-E1 crypto 4h | +0.540 | +0.064 (92) | 0.251 | no, size and p |
| X4-E0 crypto daily | +0.122 | −0.149 (37) | 0.896 | no, sign |
| XR-E0 equities | +0.021 | +0.067 (47) | 0.331 | no, p |
| X4-E0 equities | +0.093 | +0.146 (47) | 0.214 | no, p |

Not checkable, under 30 held-out events: every crypto daily E1 test (13), every equities E1 test
(18), A-E0 (29) and A-E1 (1). Every test with a development statistic at or below zero fails
criterion 3 by definition. Among them, L1-E0 equities (held-out +0.276, p 0.044) and XR-E0 crypto
daily (+0.124, p 0.081) were positive in the held-out era; see deviation 7 for how the first run
mislabelled them.

**The one agreement is L2-E0 in equities.** Its development result was +0.058R, p 0.30 (not
significant), and it kept 1% under noise, so the agreement changes nothing.

## Noise retention (section 7.3)

Each series was perturbed with round 2's gap-keeping function (`SetupGraderMc2Core.PerturbKeepGap`),
at 25% of its own volatility, seeds 0, 1 and 2 (each offset by the symbol's stable seed).
`GateCommand.Perturb` was not used. Then everything was rebuilt on the noisy bars:

- the higher-timeframe bars;
- Cipher B on both timeframes;
- the waves, setups and contexts;
- **Cipher SR, so the noisy bars have their own levels;**
- S\*, L, the strata and the controls.

A test pins the Cipher SR rebuild (see "Reproduce").

| Statistic | Clean | Noisy mean | Kept |
|---|---|---|---|
| L2-E0 crypto 4h | +0.026 | +0.067 | 258% |
| L2-E1 crypto 4h | +0.253 | +0.240 | 95% |
| A-E0 crypto 4h | +0.567 | +0.567 | 100% |
| L1-E0 crypto daily | +0.448 | +0.029 | 6% |
| L2-E0 crypto daily | +0.163 | −0.111 | below 0 |
| L2-E1 crypto daily | +0.502 | +0.098 | 20% |
| L2-E0 equities | +0.058 | +0.001 | 1% |
| L2-E1 equities | +0.032 | +0.039 | 124% |

The other statistics had a clean value at or below zero. Per seed, crypto 4h L2-E1 was +0.213,
+0.307 and +0.201, and A-E0 was +0.316, +0.905 and +0.480. **The crypto 4h trigger at support is
the one statistic that is both sizeable and stable under noise.** It still fails significance.

## The cross-round Holm (section 7.2)

One list of 119 pre-registered tests: round 1's 39, round 2's 51 and round 3's 29. Rounds 1 and 2
enter at the raw p printed in their findings. A test that was not testable enters at 1, and so does
a test whose findings print no raw p.

- **No round-3 test passes.** The smallest round-3 raw p was 0.0041 (X4-E0 crypto 4h), with
  cross-round Holm 0.48. The plain Bonferroni line is 0.05 ÷ 119 = 0.00042, and 20,000 draws allow a
  p as small as 0.00005, so every test could have passed.
- **The 6 smallest raw p over all three rounds:**
  - round 1, equities non-vetoed grade C dots: 0.0014;
  - round 1, equities veto: 0.0024;
  - round 3, X4-E0 crypto 4h: 0.0041;
  - round 2, crypto 4h trigger entry: 0.014;
  - round 3, X4-E1 equities: 0.019;
  - round 3, A-E0 crypto 4h: 0.038.

**The re-reading of rounds 1 and 2** (section 4.6; not a test):

- **Round 1's two Holm-significant equity results no longer pass:**
  - the non-vetoed grade C dot, raw 0.0014: cross-round Holm 0.17;
  - the veto, raw 0.0024: cross-round Holm 0.28.
- **Round 2's three Holm-significant equity exits** (X4 for both entries, Holm 0.008; the ladder for
  the dot, Holm 0.042) entered at p = 1, because round 2's findings print only their Holm p. Even at
  the smallest p round 2's 2,000 random books allow (1 ÷ 2,001 = 0.0005), the first step here needs
  0.00042, so none of them could pass either.
- **So no result in any of the three rounds survives one correction over all three.**

## Verdicts, test by test

| Class | Supported | Fragile | Suggestive | No edge |
|---|---|---|---|---|
| Crypto 4h | none | none | A-E0, X4-E0 | 9 |
| Crypto daily | none | none | none | 9 |
| Equities | none | none | X4-E0, X4-E1 | 7 |

- The No edge counts include the tests that were not testable: L1-E1 crypto daily and A-E1 crypto
  4h.
- **Method verdicts (section 7.4): No edge for both entries in every class.** L2 is not Supported
  anywhere.
- The at-support trades' context excess is printed for the record:

| Class | E0 dev | E0 held-out | E1 dev | E1 held-out |
|---|---|---|---|---|
| Crypto 4h | −0.081 (128) | +0.018 (184) | +0.195 (53) | +0.140 (92) |
| Crypto daily | +0.202 (25) | −0.060 (37) | +0.437 (21) | −0.049 (13) |
| Equities | +0.051 (435) | +0.193 (47) | +0.053 (202) | −0.287 (18) |

## Deviations and literal readings

All but number 7 were fixed in the harness commit (f8a2be88), before any count or outcome existed.

1. **Undefined locations.** A position (the dot for E0, the trigger for E1) whose S\* is undefined
   (no support yet) or whose ATR is undefined is excluded from every location population and
   counted. None was. L3's rows are bars with a defined context, R and L, and section 4.1's stratum
   mean "over all its bars" is over those rows. No bar had an undefined L, so this changed nothing.
2. **Eras.** A setup's era is its dot's date (round 2). A trade's control stratum uses its signal
   bar's era (round 2, deviation 1). L3's bars carry their own date's era.
3. **L1 and Family A permute labels within each asset,** one era at a time, so within asset × era.
4. **Exits.**
   - The tested statistic is the exit's mean R minus its random books' mean, the skill the test
     measures. The p is section 4.5's: (1 + books at or above the exit's mean) ÷ 20,001.
   - Breadth for an exit is per asset (10 entries or more): the asset's mean R minus its own random
     mean.
   - Section 7.3 pre-registers noise runs for Families L, LS and A only. So criterion 5 cannot be met
     for an exit, and an exit meeting criteria 1 to 4 would be at most Fragile. This did not arise.
5. **The placebo gate.**
   - The 95th percentile is nearest-rank, and the real statistic must be strictly above it.
   - A level whose ATR at the bar it became live is undefined gets no placebo level.
   - L3's placebo uses the real stratum means, since the rows do not change.
   - Seeds: `StableSeed.From("setup-grader-mc3-placebo|<draw>|<asset>")`. The offset is drawn when the
     level becomes live, u first and then the sign.
6. **The cross-round list.**
   - Round-3 tests that were not testable enter at p = 1, as rounds 1 and 2's do.
   - Round 2's 16 exit tests enter at p = 1, because its findings print only their Holm p.
   - Round 1's 39 are: primary 8, secondary 12, macro 5, exits 10, and the fallback 4.
   - Round 2's 51 are: P 6, G 8, S 19, X 16, and the fallback 2.
   - The p-values are the ones printed, to the printed precision. The code is
     `SetupGraderMc3Command.EarlierRounds`.
7. **A reporting bug, fixed after run 2 (the only change after an outcome was seen).** The held-out
   check tested the held-out statistic's sign against zero, not against the development statistic.
   So run 2 printed "agrees" for L1-E0 equities and XR-E0 crypto daily, whose development statistics
   were negative (−0.003 and −0.297). The fix requires a positive development statistic. Run 3 is the
   rerun. Diffed against run 2, only the held-out check's wording changed, on 13 lines; every number
   is identical. No verdict depended on it: both tests fail criterion 2 and the cross-round Holm.
8. **Unregistered descriptive rows.** The descriptive block printed outcome rows that section 4.6
   did not ask for:
   - at-support setups split by grade;
   - the "tested" subset. Section 4.6 asks for counts only there.

   These 48 rows (4 per class, era and policy) are exploratory. They are counted below, not quoted
   here, and not used for anything.
9. **Section 7.5's "L1 the wrong way" sentence** is printed whenever an L1 statistic is below zero.
   That includes equities' −0.003R, which is about zero.
10. **Seeds.** Tests use `StableSeed.From("setup-grader-mc3|<test id>|<asset>")`. The held-out runs
    add `|held` to the test id. The exits use the class in place of the asset.
11. **The data path** was passed as a relative path (`../../../strategy-lab-data`). The command
    prints the absolute path it resolved, which is
    `/home/cody/external-rescue/Github/accessible-trade-terminal/strategy-lab-data`. The tool sandbox
    refused the absolute form in that command line.
12. **Section 9's test 2** (truncation) was proven red by S\* reading the next bar, and by the level's
    age reading the next bar. A third sabotage, the older label reading the anchor wave's end, could
    not turn it red. Round 2's eligibility rule already requires 51 bars after the dot, so the
    harness never sees a setup whose anchor end is missing. The older-label test catches that
    sabotage instead.

## Runs and variants counted

- **Runs of the command: 3.** Every run hashed its inputs before and after.
  - Run 1: `--counts-only`, which computed no outcome.
  - Run 2: the full run.
  - Run 3: the bug-fix rerun of deviation 7, identical in every number.
- **Pre-registered tests: 29.** Computed 27; not testable 2 (L1-E1 crypto daily, A-E1 crypto 4h).
- **Pre-registered descriptive runs, counted toward variantsTried (30):**
  - the counts-only run (1);
  - the placebo run (1);
  - the noise rebuilds, 3 seeds × 3 classes (9);
  - X5 in 6 populations (6);
  - the Family A split in the two secondary classes (4);
  - L3 in context V, per class (3);
  - the at-support trades' context excess for the method verdict (6).
- **Beyond the budget:**
  - the bug-fix rerun (1);
  - the 48 unregistered descriptive rows of deviation 8.
- **variantsTried for the edge record: 29 + 30 + 1 + 48 = 108.**
- No band, level, window, definition, era cut or asset subset was changed.

## Data proof

The study read 50 input files, read-only, from
`/home/cody/external-rescue/Github/accessible-trade-terminal/strategy-lab-data`. They are the same
50 files as round 2. Every file's sha256 was taken before and after each of the three runs: 0
changed, all three runs saw the same hashes, and every hash equals the one round 2's findings list.
Nothing was fetched.

```
5a81a70c38200b77ac226babf673b6923683c51f2cc894a68a627850c7685799  bitstamp_BTC_USDT_4h.json
1743ca59b0dfd9fb47d16a2ada6119ede7af21a79dd45bc98df68d5c7f6bd1ed  bitstamp_ETH_USDT_4h.json
368b93e4f361bdddd0354f1e04a7c98cd9c040e476bd8fe583fabc66a933818d  bitstamp_XRP_USDT_4h.json
4197343d33611c242c9789281d4fb908995a9a83a8076d7171912e1fad1fe2b7  bitstamp_SOL_USDT_4h.json
f2bfa98a494046bfe09a8b1ed934dae112ca9e1096cd63fe8e8a1a286e6f6f9e  bitstamp_DOGE_USDT_4h.json
a5374f754be9793cba98e2b02cc6ec29d1553465adb5a48ce91cf44afec026a8  mexc_KAS_USDT_4h.json
4c058c382d783f4c22b4eddf074c00f477a94479205a11e2f989ea0f74e7b9ee  mexc_TAO_USDT_4h.json
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
```

## Lab problems found

- **The placebo gate for L1 is weak where setups are few.** A level moved 3 to 6 ATR away is rarely
  under a setup's low. In crypto daily, 60% to 83% of the draws had no setup at "support" and were
  dropped. The counts-only stage showed this before any outcome. A future plan that gates a
  group comparison on placebo levels should check the placebo label counts when it freezes the plan.
- **A held-out "same sign" check must compare with the development sign,** not with zero (deviation
  7). The fix is in the harness. A future harness should reuse `HeldAgrees` from
  `SetupGraderMc3Command`.
- **The level at a Market Cipher setup is almost always fresh.** The median age is 2 to 5 bars, and
  no crypto trigger at support used a level older than its anchor wave. On our Cipher SR, "at a key
  level" mostly means "at the swing low this setup just made". That is how Market Cipher SR works
  (section 1.4). It also means the location layer cannot do what Casper's planned, older levels do,
  except in the 23 crypto 4h dots of Family A.
- **One correction over three rounds removes everything.** Nothing in rounds 1 to 3 survives a Holm
  over all 119 tests. Rounds 1 and 2 each found something that passed their own family's Holm. A
  fourth Market Cipher round on the same data would start with that budget already spent.

## Scope and caveats

- **Every result is about our replica of Cipher SR, on our replica of Cipher B.** Our Cipher SR
  matches the real one on 62.5% of bars on its held-out frame, and computes on the chart's own bars,
  not on 15-minute bars. Section 7.5 forbids blaming a null on either, and forbids crediting the paid
  script with anything.
- **The primary class was chosen knowing round 2's crypto 4h result.** The cross-round Holm exists
  partly because of that. Nothing passed it anyway.
- **Development-era crypto 4h is three assets** (BTC, ETH and XRP). Breadth means 2 of 3.
- **Survivor snapshots:** every asset here still trades.
- **Longs only, support only.** Resistance appears only as the XR exit.
- **The leads** are A-E0 (older levels, 23 dots), X4 in crypto 4h, and the crypto 4h trigger at
  support (+0.253R, 95% kept under noise, raw p 0.091, held-out +0.143R). Each would need its own
  pre-registration on data this lab has not used, and none of them is a Market Cipher entry edge.

## Reproduce

```bash
cd AccessibleTrader.StrategyLab
dotnet build -v q --nologo
dotnet run --no-build -- setup-grader-mc3 --snapshots /absolute/path/to/strategy-lab-data
# --counts-only stops after the plumbing check, the counts and what they decide, before any outcome.
```

The run takes about 10 minutes, most of it the noise rebuilds and the 20,000-draw tests. It stops,
with exit code 4, if round 2's development-era counts do not reproduce.

**Tests:** `SetupGraderMc3Tests` (8). Each was seen red with its defect put back, in a scratch copy
that was then restored byte-identical (cp, break, build, run the filter, red, cp back, touch,
`diff -q`):

| Test | Sabotage that turned it red |
|---|---|
| On a replacement bar the old level is the one tested | S\* read at bar t (and the in-code replaced-level check, which also fires on its own) |
| The location at bar t is unchanged when later bars are removed | S\* read at t+1; the level's age read at t+1 |
| The location control: same context and at support, read at the signal bar | strata without L; E1's location read at the dot |
| XR never fills at a resistance not live when its bar opened | R\* read from the next bar |
| The noise rebuild recomputes Cipher SR on the noisy bars | the clean levels reused on the noisy bars |
| A placebo offset is fixed for a level's life, drawn where it became live | offset rescaled by each bar's ATR; u drawn from 0 to 3 |
| The older label compares the level's pivot bar with b(i) | compared with the dot; compared with the anchor's end |
| Round 3 reproduces round 2's counts through round 2's code | vetoed setups counted |

The oracles in these tests recompute from Cipher SR's raw arrays and the bars, not from the
harness's own derived arrays. Rounds 1 and 2's files were not edited, so `setup-grader` and
`setup-grader-mc2` are unchanged, and their tests still pass.
