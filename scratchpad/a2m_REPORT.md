# A2m — mutation campaign over `AccessibleTrader.Core/Services/Analysis`

2026-09-24. 4,269 lines in 17 files, none mutated before this campaign. The method matched A2d–A2j so the rates
compare: apply one mutant, build, run the FULL `AccessibleTrader.Tests` suite (not the browser
suite), record CAUGHT/SURVIVED with failing test names, restore from a file copy, touch, compare the file byte for byte.
Every step was niced, with three other campaigns sharing the CPU.

Scripts and data, all in `scratchpad/`:
- the campaign harness: `a2m_sabotage.py` and `a2m_sabotage_results.json`;
- the false-catch audit: `a2m_audit.py` and `a2m_audit_results.json`;
- the kill proofs for the survivors: `a2m_prove_kills.py` and `a2m_prove_kills_results.json`.

## Numbers

| | |
|---|---|
| Baseline (clean) | 8,088 passed, 0 failed |
| Mutants | 45 (all anchors verified unique; none equivalent) |
| Naive catch rate | 32/45 = 71.1% |
| **Honest catch rate (after audit)** | **29/45 = 64.4%** |
| Control after campaign | 8,088 passed; every file restored byte-identical |
| Suite after this pass | **8,111** passed (+23 tests), 0 failed |
| Prove-kills | 17/17 RED on their mutant, all 24 guard cases GREEN on control |

The series now reads **73.1 / 72.0 / 69.2 / 62.2 / 10.5 / 73.5 / 82.0 / 64.4** over eight
disjoint file sets.

### The false-catch audit changed three results

Every CAUGHT mutant was re-applied and **only its failing tests** were re-run, then the union of
all failing tests was run on the clean tree:

- **M07** (Shift+semicolon says "cleared" and keeps the pin): "caught" only by
  `ProfileNarrationTests.With_no_browser_the_profile_ladder_speaks_the_poc_cross_at_the_close`,
  which passed on rerun with the mutant in place. The catch was a flake, so M07 is a survivor.
- **M08** (the pin announcement counts from zero): "caught" only by
  `Blazor.OrderTicketErrorStateTests.The_rule_nobody_guesses_is_now_stated`, a bUnit test, which
  also passed on rerun. It is a flake, so M08 is a survivor.
- **M30** (a less extreme same-kind pivot supersedes a more extreme one): "caught" only by
  `WebHost.ChartAreaBarSliderTests.Flicking_the_slider_routes_through_the_arrow_key_navigation_pipeline`,
  which **fails on the clean tree too** under load, and passed on rerun with the mutant. It is a flake, so
  M30 is a survivor.
- M19 kept 4 real catches. Its fifth (the same ProfileNarration flake) did not reproduce.
- **M23 was mis-recorded the other way.** The test host ABORTED at 4,634 of 8,088 tests and printed
  `Failed: 0, Passed: 4634`, which the harness parsed as SURVIVED. A full rerun with the mutant
  passed all 8,088, so the abort came from load, not from the mutant, and M23 is a genuine survivor.
  The harness now marks any run short of the baseline total as `ABORTED` (harness rule 10 in
  `a2m_sabotage.py`).

No catch came only from a bookkeeping or pinned-list guard (the A2h pathology). Every remaining catch
is by a test that names the behaviour.

## Survivors (16) and how each was closed

Each survivor was checked for equivalence first, and none was equivalent. Each was then closed
by a test proved RED on its mutant and GREEN on control.

| id | what a blind trader would notice | why the suite could not see it | closed by |
|---|---|---|---|
| M07 | Shift+; announces "cleared", the pin stays, and the jump keys stay locked to two edges | clearing was only tested on `ChartPatternFocus` directly, never through the navigator | `ChartPatternPinNarrationTests.ShiftSemicolonReleasesThePin_SoTheJumpKeysReachEveryFormationAgain` |
| M08 | "Leading with range, 0 of 3" | the nesting test asserted only "of 3" | `ChartPatternPinNarrationTests.ThePinAnnouncementCountsFromOne` |
| M11 | clearing the pin on the BTC tab drops the one pinned on the TAO tab | the scope test pinned only one chart | `ChartPatternPositionTests.ClearingOneChartsPinLeavesAnotherChartsPinInForce` |
| M22 | a double top announced on its second peak, 5 bars before the peak was knowable (lookahead) | the causality test cuts at only 3 bars (700/950/1200). `NoPatternIsKnowableBeforeItsStructureIsComplete` says in its doc that a pattern "AT its final pivot" is lookahead, then asserts `>= EndBarIndex`, which permits it | new `ChartPatternCausalityTests.EveryFormationIsFoundWithOnlyTheBarsUpToItsKnowableBar` (every distinct knowable bar), and the old test tightened to `>= EndBarIndex + Span` for swing-built kinds |
| M23 | a stop-hunt wick through the neckline announced as the confirmation | the completion test checked only "at or after knowable" and "close != trigger" | `ChartPatternDetectorTests.AConfirmationIsACloseThroughTheTrigger_NeverAWick` |
| M25 | flat top + rising lows named a *descending* triangle | the fixture test accepted any triangle kind | `ChartPatternDetectorTests.FlatTopWithRisingLows_IsNamedAnAscendingTriangle_AndConfirmsAboveTheTop` |
| M26 | wedges confirm on the wrong side | no fixture asserted a wedge's side | `ChartPatternDetectorTests.EveryFormationConfirmsOnTheSideItsNameImplies` (over the 4 probe series, asserting both wedges occur) |
| M30 | the carried swing high drops to a lower high inside one move | the only same-kind test checks alternation, and its fixture never produces two same-kind pivots | `SwingStructureTests.ASecondHighWithNoRealLowBetween_OnlyReplacesTheFirstIfItIsHigher` (both directions; asserts the dip really was filtered) |
| M33 | every close near support counts as a "break", and the hold rate collapses | fixtures closed only far above or far below the line | `LevelRespectAnalyzerTests.ACloseJustAboveSupport_IsNotABreak` |
| M35 | a rally 15 bars later is credited as the touch's bounce | no fixture reacted outside the window | `LevelRespectAnalyzerTests.ARallyAfterTheReactionWindow_IsNotCreditedToTheTouch` |
| M37 | the weekly MA on a daily chart reads the unfinished week (lookahead) | the threshold `< 150` is too loose: the leak gives (100+100+200)/3 = 133 | `LevelRespectAnalyzerTests.MultiTimeframeMa_ReadsExactlyTheLastClosedWeeks` (exact values) |
| M38 | "below: prior 1w high at 64,000" with price at 60,000 | `LevelProvenanceService` had **no test file at all**, and the modal's bUnit harness substitutes it | new `LevelProvenanceServiceTests.ALevelIsNarratedOnTheSideOfPriceItIsActuallyOn` |
| M39 | "prior day high" is today's unfinished high (lookahead) | same as M38 | `LevelProvenanceServiceTests.ThePriorDayHighIsYesterdays_NotTodaysStillFormingHigh` |
| M41 | a bar's value profile includes the bar itself, so a spike reads as less stretched than it is | the causality test only appends later bars, and the flat warmup fixture is degenerate (NaN everywhere) | `ValueDeviationTests.ABarsReferenceDoesNotDependOnThatBar` (with a vacuity floor) |
| M44 | an asset at its high reads "0% of the way up" | no test read the number | `AssetDossierTests.PositionInRangeIsMeasuredFromTheBottom` (3 cases) |
| M45 | "insider filings, last 90 days" counts three years | the only equity fixture uses absolute dates, all inside any window | `AssetDossierTests.FilingCountsReadOnlyTheLast90Days` (dates relative to now) |

## One real production defect, demonstrated first, then fixed

**`AccessibleTrader.Core/Services/Analysis/ChartPatternDetector.cs:275` (before the fix):
`if (swings.Count < 3) return found;` returned BEFORE `Flags(...)`, and the flag scan does not
use swings at all.**

The M22 guard exposed it: on all four probe series, bull flags that the full chart said were
knowable at bar k were **absent** from a detection over bars 0..k. Two consequences:

- **A clean trend never hears its flag.** An impulse with a shallow drift is the market with the
  fewest swings, and the bull flag is the formation defined by exactly that. With fewer than three
  swings on the whole chart, no flag is ever reported.
- **Flags appear after the fact.** A flag at bar 100 was not reported to someone watching at bar 100,
  because the chart then had fewer than three swings. Once later swings existed, panning back
  announced it as though it had always been there. The class doc promises this does not happen.

Demonstrated RED first by `ChartPatternDetectorTests.ABullFlagIsFoundInATrendTooCleanToHaveThreeSwings`
(the result was an empty collection) and by the new causality theory (4/4 series red). The fix is
minimal: the three-swing floor now gates only the swing-built detectors, and `Flags` runs
unconditionally (lines 283–294). It was proved again as kill `FLAG` (fix reverted → 5 red).

The ChartPatternCache floor (30 bars) remains a legitimate warmup, and the new theory skips
knowable bars under it.

Also fixed a comment in the same file (no behaviour change). The comment above `breakBelow` named ascending triangles and **rising**
wedges as the upper-edge pair, the opposite of the code for wedges. The code matches
convention: rising wedges break down and falling wedges break up.

## Lessons

1. **Guards over this directory ask "is it found?" and rarely "is it found on the right bar, with the
   right name, on the right side?"** Most survivors (M08, M22, M23, M25, M26, M38, M39, M44) were
   a name, a side, a number or a bar index inside a result the suite already checked for presence.
2. **A lookahead test that samples the timeline at a few points only sees lookaheads longer than its
   gaps.** M22 (5 bars) passed a causality test that cut at 700, 950 and 1200. The fix is to
   check at every knowable bar. That costs one detection per distinct bar, which is under a second.
   It immediately found the flag defect, which had been sitting in the gaps too.
3. **Threshold assertions made loose to tolerate warmup also tolerate the leak.** M37's `< 150`
   could not separate 100 (honest) from 133 (leaked). Assert the value the rule produces.
4. **A test whose doc and assertion disagree is a survivor waiting to happen.**
   `NoPatternIsKnowableBeforeItsStructureIsComplete` described the M22 defect and asserted a
   bound that allows it.
5. **Under CPU contention the audit is essential.** Three of the 32 raw catches (9%) were flakes from
   unrelated Blazor and WebHost timing tests, and one "survivor" was a test-host abort. Without
   failing-test names, the rate would have read 71.1% instead of 64.4%.
6. **Harness rule 10 (new): compare passed+failed against the baseline total.** An aborted host
   prints a summary line for the tests it reached, and that line parses as a clean pass.
7. A service with no test file (`LevelProvenanceService`) hid behind a component test that
   substitutes its interface. Both of its survivors were lookahead or side-of-price errors in
   a report a trader acts on.

## Not verified

- The flag fix changes what the terminal announces on trending charts: more bull and bear flags now
  appear on charts with fewer than three swings. It has been checked by tests, not by ear or on a live chart.
- M23's original abort is attributed to load because a full rerun passed. The abort's cause was not
  otherwise diagnosed.
- `docs/` was deliberately not edited (other campaigns are running). TODO/CHANGES entries for this
  pass, including the flag fix and the suite count 8,088 → 8,111, still need to be written when
  merging.
