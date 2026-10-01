# A2q — mutation campaign over `AccessibleTrader.Core/Services/Accessibility`

2026-10-01. Branch `worktree-agent-af75206f13ac77413`, based on 87a4c257 and merged with
origin/main 522e02d4 (the silent-test-run fix) after the mid-campaign pause. Nothing under `docs/`
was edited.

## Frame

- **Scope:** 49 files, about 15,900 lines. 26 of them had never been mutated (survey
  `a2q_SURVEY.md`).
- **Mutants:** 82 were drafted and 50 were run (`a2q_mutants.py`, with the reason for each drop).
  Selection was weighted to user harm, to the channel moves the 2026-09-30 F2 decision made
  possible, and to never-mutated files:
  - NavigationEngine, ViewportManager, SeriesNavigationRegistry, NotificationHub,
    HistoryBufferCoordinator, HeadlessChart, HeadlessChartFactory, IndicatorContextAnalyzer,
    SdkCandlePatternAnalyzer.
- **Not mutated:** the survey's dead code. Two NotificationHub mutants (C16, NH1) were dropped
  because `NotifyError`/`NotifyAlert` have no production caller.
- **Scoring:** every mutant was scored against the suite as it stood. The trees were copied from an
  archive export of 522e02d4, holding none of this branch's tests or fixes.
- **Survivors:** every mutant the C# suite missed was also run through the full browser suite
  before being called a survivor.
- **Pause and resume:**
  - The campaign was paused once, because test runs were playing sound on Cody's desktop.
  - Three in-flight mutants were killed, recorded INTERRUPTED and restored byte-identical.
  - After main's silence fix, all 50 were re-run from scratch on 522e02d4. The pre-pause results
    are kept in `a2q_sabotage_results_prepause.json` and agree on all 7 mutants they share.
  - Every tree passed `QuietDesktopTests` 5/5 before any suite ran in it.

### Mutant table, by area

Statuses below are after the audit. "honest" means a true catch; "SURV" means a survivor, closed
by the named test file.

| Area | Mutants | Result |
|---|---|---|
| Channel moves (call sites) | C01, C02, C08 | honest, caught by C# tests |
| | C06 | honest, caught by browser tests only |
| | C03 C04 C05 C07 C09 C10 C11 C12 C15 | SURV |
| Fired alerts (AFC) | A01 speech arm, A02 break-through speech, A03 break-through earcon | SURV |
| Forming-pattern commentary (AFC) | A04 debounce, A05 N gate, A06 change gate, A07 once-per-formation | SURV |
| Toggle confirmation (AFC) | A09 "Sound on/off" inverted | SURV |
| NavigationEngine | N01 edge earcon, N02 backfill direction, N03 clamp, N04 delete candles, N05 component reset | SURV |
| | N06 Home → bar 0 | EQUIVALENT (its catch was a flake) |
| ViewportManager / Registry | V01 pan reversed, R01 profile navigated as points | SURV |
| HistoryBufferCoordinator | H01 exhaustion never reset, H02 "No more history" never said | SURV |
| FeedbackRouters priority | F01 every message takes the slot, F02 4 s floor | SURV |
| GlobalErrorCoordinator | G01 loses "CRITICAL" | SURV |
| EarconService | E01 buy/sell swapped | SURV |
| | E02 stop plays the take-profit | SURV (proxy catch) |
| SpeechTimeFormatter | S01 daily bar "at 00:00" | honest, caught by C# tests |
| ProfileBinClassifier | P01 VAH/VAL swapped | SURV |
| DrawingInteractionManager | D01 next-point prompt, D02 completion point, D03 range direction | SURV |
| Dot Pad | DP1 pan keys reversed, T01 F4 never turns braille on | SURV |
| Analysis | SC1 dragonfly/gravestone, I01 bear cross as bull | SURV |
| Headless | HC1 pre-gap values kept, HF1 parameters left out of the signature | SURV |
| AutoNarrationService | AN1 forming bar scanned, AN2 first redraw as a close, AN3 F2 gate deleted | SURV |

## Numbers

- **Raw:** 7 caught out of 50 = 14.0% (`a2q_sabotage_results.json`).
  - Caught by C# tests: C01, C02, C08, E02, S01.
  - Caught by browser tests only: C06 and N06. No browser run caught any other mutant.
- **False-catch audit** (`a2q_audit.py` / `a2q_audit_results.json`): each catch's failing tests
  were re-run with the mutant applied alone, then on the clean tree.
  - Six catches were real.
  - N06 was a FLAKE (details below).
- **Proxy audit:** E02 was caught only by `EarconServiceTests.Sequence_earcons_stagger_their_notes_instead_of_playing_a_chord`,
  through its note-COUNT assertion (3 notes vs 4).
  - Nothing tells a stop loss from a take profit by shape. A stop swapped for any rising
    three-note phrase passes.
  - The test's natural repair is to change the expected count.
  - Scored a survivor.
- **Equivalence** (`a2q_equivalence.json`): one mutant.
  - N06 changes `SetCursorAction(ViewportStartIndex)` to `SetCursorAction(0)`.
  - `ViewportReducer.CursorOnlyJump` clamps the target to `[ViewportStartIndex, rightLimit]`, so
    both land on the same bar. Excluded from the denominator.
- **Honest rate:** 5 / 49 = **10.2%** (C01, C02, C06, C08, S01).
- **Survivors:** 44 non-equivalent (43 raw plus E02). All 44 are closed and proved (see below).

## False catches (flakes)

**N06 → `AccessibleTrader.BrowserTests.ModalBrowserContractTests.Tab_never_escapes_an_open_dialog(routeName: "AIAnalystModal via toolbar")`**

- Full failure message from the campaign run:
  > AIAnalystModal reports 2 focusable controls but Tab only ever reached 1 of them. Either the trap is pinning focus, or this test is proving nothing — both are worth failing over.
- Re-run three times with the mutant applied alone, and three times clean: it passed all six times.
- The mutant (Home's cursor target) cannot reach the AI Analyst dialog's Tab order.
- This is the intermittent Tab-trap flake, caught here with its message, on 522e02d4 with the
  silence fix, under three concurrent suites.
- The harness kept only the message's first line (up to 400 characters) and no stack trace.
  The whole message is a single sentence, so nothing is lost.
- **Not fixed and not root-caused.**

No other flakes were seen: every honest catch was red again with its mutant and green clean.

## Survivors and how each was closed

Each closing test names the behaviour, uses real objects wherever possible, and was proved RED
with its mutant and GREEN clean. The proofs are in `a2q_prove_kills.py` and
`a2q_prove_kills_results.json`: **51/51 proved**. That count covers:

- the 44 survivors;
- the honest catches C06 and S01, which the new tests also kill;
- D02, re-anchored on the fixed code as D02f;
- 5 sabotages of the four production fixes (FIX4 has two halves).

The JSON holds 52 records because the E02 kill appears twice; both copies are PROVED.

| Survivors | Closing test file (behaviour) |
|---|---|
| C03 C04 A04 A05 A06 A07 | `FormingPatternCommentaryTests`: F2 silences forming commentary, Shift+F2 does not; narration's priority survives an arrow press; N on the candles is required; a 5 s debounce; no repeat of the same pattern; a formation is announced once. Two tests wait out the real debounce (about 5.3 s each); there is no clock seam. |
| C05 A01 A02 A03 | `FiredAlertDeliveryTests`: speech, earcon and both deliveries; F2 does not mute an alert, Shift+F2 does; break-through pierces both mutes, words and sound. |
| C06 C07 A09 | `MuteToggleConfirmationTests`: "Chart speech off" and "Alerts and events muted" are heard; F3 says the right word. |
| C09 N01–N05 V01 R01 | `ChartKeyboardNavigationTests`: real `WorkspaceStore` and reducers, real engine, viewport manager, registry and coordinator. Covers the edge earcon, history request direction, Page Down clamp, Delete on candles and on an indicator (with F2 on), first component after a switch (real candle roles), pan direction, and profile/heatmap strategies. |
| C10 C11 C12 C15 G01 | `ErrorAndStatusAnnouncementTests`: a margin warning pierces Shift+F2; a strategy "order placed" and "Connection lost" are heard with F2 on; the indicator advisory obeys Shift+F2; a critical error says CRITICAL. |
| H01 H02 | `HistoryBackfillCoordinatorTests`: "No more history available." is said; no re-asking; loading another chart resets exhaustion. |
| F01 F02 | `SpeechPriorityTimingTests`: real clock. Key-repeat does not cut off a rejection on the 2nd or 3rd press; a short alert stops holding the arrows once said; the 4 s ceiling. |
| E01 E02 | `OrderEarconShapeTests`: buy rises, sell falls, in separate registers; a stop descends, a take profit climbs. |
| S01 | `BarCloseStampTests`: a daily close says a date; an hourly close says a time. |
| P01 SC1 I01 | `ShapeAndDirectionNamingTests`: VAH/VAL, dragonfly/gravestone, MACD bearish vs bullish cross through the real narrator. |
| D01 D02 D03 | `DrawingPlacementSpeechTests`: R/R asks for the take profit after the stop; completion names the last point; shift-click range says up/down. |
| DP1 T01 | `BrailleDisplayKeysAndToggleTests`: the real `DotpadTactileDriver` over a substituted native layer, so `DotPadKeyCode.PanningLeft` pans left; F4 turns braille on, then off. |
| HC1 HF1 | `HeadlessChartTests` (+3 tests): no pre-gap values after a reseed (with its partner); the signature changes when a period is edited. |
| AN1 AN2 AN3 | `FormingBarNarrationTests`: a flickering signal on the forming bar is not narrated until the close; a bar that closed before the first redraw is announced at that redraw; a signal printed during F2 is heard after unmuting. |

## Production defects

Each was demonstrated by a failing test first and fixed on this branch. Each fix's guard was
proved RED when the fix is reverted (FIX1–FIX4b).

1. **A scroll-back history load re-narrated old bars.** This was survey §4 item 3.
   - The defect:
     - The backfill's redraw took the jump in bar count for a bar close and narrated a bar deep in
       history as just closed.
     - The next real close re-announced the signal the user had already heard, under its new
       index.
   - The fix: `AutoNarrationService.NoteOlderHistoryPrepended` detects older bars arriving in
     front (same chart identity, earlier first bar, the old first bar found in the new data) and
     calls `NarrationScanner.ShiftIndices(-added)`, which now accepts a negative shift.
   - Guarded by `HistoryBackfillNarrationTests` (3 tests, including a vacuity partner).
2. **Page order swapped with three panes and Main declared last.** This was survey §4 item 7.
   - `ChartPaneModel.Panes` used the unstable `List.Sort`. With exactly three panes and Main last,
     .NET's three-element sorting network swapped the other two.
   - Page Up/Down then walked them in the wrong order, and Shift+F1 gave the wrong "n of m".
   - Fix: a stable `OrderBy`. Guarded by a `PaneModelAndTrailingSpeechTests` test.
3. **New: a keyboard-placed two-point drawing finished as "point 3".**
   - "Trend line placed, point 3 at …" — `_anchorDate2 == null ? 1 : 2` was always 2 by then.
   - Fix: the last point is the 2nd for two-point tools and the 3rd for three-point ones.
   - Guarded by `DrawingPlacementSpeechTests`.
   - The campaign's D02 mutant on the original code had in fact turned this sentence correct.
4. **A Dot Pad that reconnects showed a blank braille strip.** This is the strip half of survey
   §4 item 5.
   - Both the coordinator's and the driver's "same text as last time" memory survived a
     disconnect, so the catch-up strip after a reconnect was skipped as a repeat.
   - Fix: both memories are cleared on connect.
   - Guarded by two `BrailleDisplayKeysAndToggleTests` tests: a settings toggle, and an unplug
     with a real driver.

## Survey §4 status

| Item | Status |
|---|---|
| 1, 2 | Fixed before A2q (87a4c257). |
| 3 (backfill re-narration) | **Demonstrated and fixed** (defect 1 above). |
| 4 (F2 policy) | Decided by Cody. AN3 now pins the gate's observable effect. |
| 5 (Dot Pad) | Strip blank after reconnect: **demonstrated and fixed** (defect 4). Values below 0.001 show "0": confirmed by reading (`FormatValue` uses "0.###"), **not demonstrated by a test**. `dotpad.log` appended on every host: **unverified**. |
| 6 (Drawing: drag preview unreachable, sloppy click on 3-point, `IsLocked` unenforced) | **Unverified.** Not touched. A different drawing defect was found and fixed (defect 3). |
| 7 (unstable pane sort) | **Demonstrated and fixed** (defect 2). |
| 8 (design questions) | **Unverified.** These are questions, not defects (see Decisions). |

## Decisions for Cody

1. **Which component Page Up/Down lands on (N05).**
   - `NavigationEngine` resets to component 0 after a series switch. On the candles that is the
     Upper Wick.
   - The series reducer's own default for `SelectSeriesAction` is the Body (the close), which the
     engine then overrides.
   - The new test pins the engine's documented rule (component 0). If you would rather land on
     the Body, delete the engine's `SelectComponentAction(0)` and flip that one test.
2. **The narrator's F2 gate (AN3).**
   - Pinned behaviour: a signal that printed while F2 was on is heard at the first close after
     unmuting, within the 20-bar look-back.
   - The source comment calls that catch-up only "arguably" worth hearing.
   - If you want no catch-up, say so: the test flips and the gate's purpose shrinks to saving
     work.
3. **Defect-1 fix scope.**
   - A prepend is recognised by "same chart identity and an earlier first bar that still contains
     the old first bar".
   - A full reload of the same chart that returns more history passes that test, and is also
     treated as a prepend. That is correct as long as bar dates are stable.
4. **Survey §4 item 8, still open:**
   - Binned "No data" is routed as an Error (High earcon, Critical speech).
   - `ReportError(Low)` maps to Info, which F2 now mutes.
   - The coordinator's Error arm ignores severity and interrupt.
5. **The Tab-trap flake.** Its failure message is now captured (above). It still needs a root
   cause.

## Tests added

- 14 new test files and 2 extended files, 81 tests in all.
  - New files: BarCloseStampTests 2, BrailleDisplayKeysAndToggleTests 6,
    ChartKeyboardNavigationTests 13, DrawingPlacementSpeechTests 5,
    ErrorAndStatusAnnouncementTests 8, FiredAlertDeliveryTests 8, FormingBarNarrationTests 3,
    FormingPatternCommentaryTests 8, HistoryBackfillCoordinatorTests 4,
    HistoryBackfillNarrationTests 3, MuteToggleConfirmationTests 4, OrderEarconShapeTests 5,
    ShapeAndDirectionNamingTests 5, SpeechPriorityTimingTests 3.
  - Extended: HeadlessChartTests +3, PaneModelAndTrailingSpeechTests +1.
- **C#:** 8,294 → **8,375** (all pass).
- **Browser:** 233 → 233 (232 pass, 1 skipped by design: the AT-SPI probe). No browser tests
  were added.

## Harness and result files (`scratchpad/`)

- **Scripts:**
  - `a2q_mutants.py`, `a2q_sabotage.py` (campaign; `--setup`, `--control`, `--summary`)
  - `a2q_audit.py`, `a2q_prove_kills.py`, `a2q_final_suites.sh`
  - `a2q_restore_trees.py`, `a2q_mark_interrupted.py` (pause handling)
  - `a2q_wait.py`
- **Results:**
  - `a2q_baseline.json`, `a2q_tree_baseline.json` (pre-pause: `*_prepause.json`)
  - `a2q_sabotage_results.json`, `a2q_audit_results.json`, `a2q_equivalence.json`
  - `a2q_prove_kills_results.json`, `a2q_control.json`
- **Logs:** `a2q_campaign.log`, `a2q_audit.log`, `a2q_prove_kills.log`, `a2q_final_*.log`,
  `a2q_control.log`. `.log` files are gitignored; only the ones force-added are in the commit
  history.

## Final results

- **Final tree** (this worktree: main + tests + four fixes):
  - QuietDesktopTests 5/5.
  - C# **8,375 / 8,375 passed**.
  - Browser **232 passed, 1 skipped, 0 failed of 233**.
- **Control run** (the three trees, re-synced from the final worktree, unmutated):
  - QuietDesktopTests 5/5 in each tree.
  - Each of t1, t2 and t3: C# 8,375/8,375 passed; browser 232 passed, 1 skipped, 0 failed of 233.
  - Each tree was byte-compared against the worktree with an rsync checksum dry-run: **restored
    byte-identical** (all three trees).
- **Earlier restores, also checked:**
  - The campaign trees were restored against 522e02d4 after every mutant.
  - After the audit: "restored byte-identical".
  - After the pause kill: `a2q_restore_trees.py` → "restored byte-identical".
- **Not verified:** the new tests' behaviour on Windows or MAUI heads; the Dot Pad fixes against
  real hardware (substituted native layer only); and any of the speech wording by ear.
