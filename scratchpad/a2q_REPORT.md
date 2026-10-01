# A2q — mutation campaign over `AccessibleTrader.Core/Services/Accessibility`

**STATUS: PAUSED (2026-10-01 ~00:50) on the coordinator's instruction — test runs were playing
sound, speech and taking the keyboard on Cody's desktop.** This is an interim report; the full
write-up follows when the campaign resumes and finishes.

## What completed (scored, both layers)

Baseline (worktree and all three tree copies, 87a4c257): C# 8,289/8,289, browser 233/233.

| id | mutant | result | layer |
|---|---|---|---|
| C01 | new-bar sentence on Event instead of Narration | CAUGHT | C# |
| C02 | bar-close narration on Event instead of Narration | CAUGHT | C# |
| C03 | forming candle commentary on Event | SURVIVED (C# 0 failed, browser 0/233 failed) | — |
| C04 | forming formation on Chart (loses narration priority) | SURVIVED (both layers) | — |
| C05 | ordinary price alert on Chart (F2) instead of Event | SURVIVED (both layers) | — |
| C06 | "Chart speech off" on Chart | CAUGHT | browser only (C# 0 failed) |
| C08 | "chart failed to load" on Chart | CAUGHT | C# |

None of these catches has been through the false-catch audit yet.

## Interrupted (NOT scored)

C07 (t2), C09 (t1), C10 (t3) were mid-run when the campaign process was killed. Their tree files
were restored from the byte backups (`a2q_restore_trees.py`: "restored byte-identical" against
87a4c257). They are recorded as `INTERRUPTED` in `a2q_sabotage_results.json`, which the harness
treats as not done.

## Not yet run

C11, C12, C15, A01–A07, A09, N01–N06, V01, R01, H01, H02, F01, F02, G01, E01, E02, S01, P01,
D01–D03, DP1, T01, SC1, I01, HC1, HF1, AN1–AN3 (40 mutants).

## Work already done in the worktree (committed, not yet full-suite-verified)

- Production fixes, each demonstrated by a failing test first:
  1. Survey §4 item 3 — DEMONSTRATED and fixed: a scroll-back history load made the narrator
     narrate an old bar as just closed, and re-announce an already-heard signal on the next close
     (`AutoNarrationService.NoteOlderHistoryPrepended`, `NarrationScanner.ShiftIndices` accepts a
     negative shift). `HistoryBackfillNarrationTests`.
  2. Survey §4 item 7 — DEMONSTRATED and fixed: `ChartPaneModel.Panes` used unstable `List.Sort`;
     three panes with Main last swapped the other two. Now `OrderBy`.
     `PaneModelAndTrailingSpeechTests.Three_panes_with_Main_declared_last_...`.
  3. NEW — DEMONSTRATED and fixed: completing a keyboard-placed two-point drawing said
     "Trend line placed, point 3 at …". `DrawingInteractionManager.CompleteDrawing`.
     `DrawingPlacementSpeechTests`.
  4. Survey §4 item 5 (strip half) — DEMONSTRATED and fixed: a reconnected Dot Pad's braille strip
     stayed blank (dedup memory survived the disconnect, in both the coordinator and the driver).
     `BrailleDisplayKeysAndToggleTests`.
- New test files (written ahead for predicted survivors; not yet proved against their mutants):
  ChartKeyboardNavigationTests, HistoryBackfillCoordinatorTests, FiredAlertDeliveryTests,
  MuteToggleConfirmationTests, ErrorAndStatusAnnouncementTests, FormingPatternCommentaryTests,
  DrawingPlacementSpeechTests, OrderEarconShapeTests, BarCloseStampTests,
  ShapeAndDirectionNamingTests, BrailleDisplayKeysAndToggleTests, HistoryBackfillNarrationTests;
  additions to HeadlessChartTests and PaneModelAndTrailingSpeechTests.
- Equivalence noted so far (not yet scored): N06 (Home → `SetCursorAction(0)`) — the reducer's
  `CursorOnlyJump` clamps the target into `[ViewportStartIndex, rightLimit]`, so 0 and
  `ViewportStartIndex` land on the same bar.

## How to resume

1. Merge origin/main into this worktree (the silent-test-run fix), rebuild, re-run
   `scratchpad/a2q_baseline.sh` and update `scratchpad/a2q_baseline.json` if the totals moved.
2. Re-copy the trees **from 87a4c257-plus-main, WITHOUT this branch's tests and fixes** (the
   campaign must score mutants against the pre-existing suite): `python3 scratchpad/a2q_sabotage.py
   --setup` rsyncs the worktree, so first stash nothing — instead run setup from a clean checkout of
   the merge base, or temporarily move the new test files and revert the four fixes in the trees
   after the rsync. (The trees under `~/.cache/a2q-trees/t{1,2,3}` currently hold 87a4c257 exactly.)
3. `python3 -u scratchpad/a2q_sabotage.py > scratchpad/a2q_campaign.log 2>&1` — resumes: completed
   ids are kept, INTERRUPTED and unrun ids are queued.
4. Then the false-catch audit, prove-kills for every survivor, and the control run.
