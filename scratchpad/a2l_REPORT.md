# A2l — mutation campaign over `AccessibleTrader.Core/Services/Strategies` (2026-09-24)

Written here rather than in `docs/` so it doesn't conflict with the three campaigns running in parallel.
TODO/CHANGES still need an entry when this branch is merged. See "For the docs" at the end.

## The numbers

| | |
|---|---|
| Mutants | **44** (L01–L44), one per decision a trader would notice |
| Caught (naive) | 24 / 44 = 54.5 % |
| **Caught (honest, after the false-catch audit)** | **23 / 44 = 52.3 %** |
| False catches | 1: **L15**. Its only failing test was `WebHost.ChartAreaBarSliderTests.Flicking_the_slider…`, which passed when rerun with the mutant applied. That was a flake caused by CPU contention. |
| Flaky co-failures on honest catches | L08 (`SettingsModalTests.SettingsModal_OpenedOnATab…`) and L36 (`ProfileNarrationTests.With_no_browser…`). Both passed on rerun. Each of those mutants also had a test that really caught it. |
| Bookkeeping-only catches (the A2h pathology) | none. Every honest catch is by a test that names the behaviour. |
| Equivalent survivors | none. All 21 real survivors were non-equivalent. |
| Survivors closed | **21 / 21 proved RED on their mutant and GREEN on control** (`a2l_prove_kills.py`) |
| Suite | 8,088 → **8,120** (+32 test cases in 30 methods), full run green on the final tree |
| Control | green (8,088) at the end of the campaign; all 19 mutated files restored byte-identical |

Series for comparison (disjoint file sets): 73.1 / 72.0 / 69.2 / 62.2 / 10.5 / 73.5 / 82.0 / **52.3**.

**Sampling frame.** 32 of the directory's 38 files had never been mutated. A2d touched the other six: BacktestWarmupAnalyzer, BootstrapCi,
ConditionEvaluator, RiskPlanResolver, StrategyPositionManager and TradeRanker. The 44 mutants cover 19 of the 32. `AssetClassifier` was
deliberately left out because nothing in production calls it (only tests do), so no trader could notice a mutant there.

## The result in one sentence

**Everything that decides a PRICE was well guarded, and so was everything that REFUSES. The layer that turns a button press into a running
strategy had no guard at all.**

- Level providers and the level service: 8/8 caught. Stops at the farthest support, a Kumo top/bottom swap, Cipher SR and Ichimoku look-ahead,
  the profile future leak, a swing high emitted as support.
- Bundle import: 3/3. Signal catalogue: 2/2. The causality probe's refusals: 4/4. The validator: 2/2.
- `StrategyModalCoordinator`, `StrategyLibraryFacade`, `StrategyAutoLoader` (apart from reconciliation), `EditableStrategySpec`,
  `StrategySpecNarrator` and `MultiTimeframeDataService` were at **0 of 18**. None of them had a test file that constructed the class.
  The Blazor modal tests substitute the coordinator, so the modal's wiring is tested and the coordinator's behaviour is not.

## Survivors and how each was closed

| ID | What survived | Closed by |
|---|---|---|
| L01 | Start on a running spec adds a twin: every entry doubled | `StrategyLifecycleTests.Starting_a_spec_that_is_already_running_leaves_exactly_one_instance` |
| L02 | Stop leaves `IsAutoActivate` set: the stopped strategy trades again after a restart | `…Stopping_a_spec_removes_it_and_disarms_it_for_the_next_launch` (includes a Reload from disk) |
| L03 | Pause/Resume swapped; the UI says "Paused." while it keeps trading | `…Pause_pauses_a_running_strategy_and_resume_resumes_it` |
| L04 | A compiled script is saved un-armed and is gone after a restart | `…A_compiled_script_is_saved_armed_and_running_under_its_library_id` |
| L05 | The order fingerprint drops StopLoss: a script whose stop reads the future loads | `ScriptStrategyCausalityFingerprintTests` (stop taken from `state.Data[^1]`, the ordinary "current bar" bug), with an honest-stop control |
| L10 | Every save of an edited spec creates a new id, so the strategy is duplicated | `EditableStrategySpecRoundTripTests.Saving_an_opened_strategy_keeps_its_id…` |
| L11 | FixedQuantity and RiskCash swapped: a 1-contract strategy trades 100 | `…Fixed_quantity_and_cash_risk_keep_their_meaning` |
| L12 | Stop buffer dropped on load | `…The_stop_survives_a_load_and_save_including_its_buffer…` |
| L13 | Level-strength gate dropped on save | `…The_conditions_survive_a_load_and_save_including_the_level_strength_gate` |
| L14 | The by-ear read-back says "Long" for a short setup | `StrategySpecNarratorTests.A_short_setup_is_narrated_as_short…` |
| L15 | (false catch) risk narrated 100× too small | `StrategySpecNarratorTests.The_risk_per_trade_is_spoken_as_a_percent_of_equity` |
| L18 | A dropout says "still active" for a setup that is invalidated | `SetupSonifierSpeechTests` (both polarities) |
| L20 | The Immediate/pulse confirmation path drops the ladder note. This was a fixed defect coming back. | `SetupSonifierSpeechTests.An_immediate_confirmation_with_a_ladder…` (TierB covers only the ARMED path) |
| L24 | Add to Engine runs the edited spec beside the old copy | `StrategyLifecycleTests.Re_adding_an_edited_spec_replaces_the_running_copy…` |
| L25 | Save skips the validator | `…Save_refuses_a_spec_whose_entry_trigger_can_never_fire_and_writes_nothing` |
| L26 | A shorter cached HTF series is served when more bars were asked for, so the HTF leaf is NaN (false) forever | `MultiTimeframeDataServiceTests.A_cached_series_shorter_than_the_request_is_refetched_not_served` (with a positive cache control) |
| L27 | An HTF indicator is computed with no parameters, so its output is all NaN | `…An_htf_indicator_asked_for_with_no_parameters_is_computed_with_its_declared_defaults` |
| L28 | Every saved template starts at launch | `StrategyLifecycleTests.At_launch_only_the_armed_specs_start…` |
| L29 | A second `LoadAllAsync` registers everything twice | same test (second half) |
| L34 | The minimum trade count is only checked in the first half, so 3 trades in H2 still earns SURVIVOR | `LabRunnerTests.Compare_ASecondHalfWithTooFewTrades_IsNotASurvivor…` (the existing "thin" fixture is thin in BOTH halves) |
| L35 | Upserting the FIRST spec in the library appends a copy | `…The_library_replaces_a_spec_by_id_wherever_it_sits_in_the_list_including_first` plus the save-in-place tests |

## Real defects found (each demonstrated RED before it was fixed; see `a2l_defect_demo_before_fix.txt`)

1. **`EditableStrategySpec.cs:101` hard-coded `StopAdjust = MoveToBreakeven`** in `ToSpec`, and `LoadFromSpec` never read it. The research
   catalogue ships specs with `StopAdjustOnTp1.TrailByAtr`. For v24 cycle-low the catalogue's own comment says "the ATR trail after TP1 is
   the real exit". Opening such a spec in the builder and saving it, even just to rename it, silently replaced the trail with breakeven.
   While the survivor for L12 was being closed, the same round-trip test found **`:82` and `:90` dropping `IndicatorCode`/`ComponentName`**
   from the stop and from each TP rung. That strips the indicator a BelowComponent stop or an AtComponent target resolves against.
   Fix: carry `StopAdjust`, `StopIndicatorCode`/`StopComponentName`, and the rung `IndicatorCode`/`ComponentName` through load and save.
   The builder UI is unchanged: there is still no picker, but the values are no longer destroyed.
2. **`StrategyLibraryFacade.cs:99` (Add to Engine) started the strategy with no `specId`.** `StartSpec` and the auto-loader both pass it.
   This matters for two reasons:
   - `StrategyPositionManager.OpenPosition` records `active.SpecId`, and `Adopt` matches on it after a restart. A position opened by a
     builder-added strategy therefore comes back as an orphan. The manager logs "cannot be re-adopted after a restart" at open time. After
     that restart the auto-loader rebuilds the strategy flat, with the spec id, beside the orphaned position.
   - `WorkspaceLibraryService` saves only instances that have a SpecId, so a workspace save silently dropped the strategy.

   Fix: pass `specId: s.Id`.
3. **`StrategyModalCoordinator.cs:224` (compiled scripts) had the same omission.** The spec id was computed after `AddStrategy`. Fix: decide
   the id first and pass it.

## Observed, NOT fixed, NOT demonstrated (listed so they aren't lost)

- `EditableStrategySpec.ToSpec` still hard-codes `ExecutionMode = Suggestion` and leaves `IsAutoActivate` false. Loading an Auto-mode or
  armed spec in the builder and pressing **Save** (not Add to Engine) therefore demotes it to Suggestion and disarms it for the next launch.
  The Suggestion demotion may be a deliberate safety choice; the silent disarm looks less deliberate. Cody should decide. `Provenance` and
  `CreatedUtc` are also reset on that path.
- Add to Engine and StartSpec de-duplicate by calling `RemoveStrategy` on the old instance, and `StrategyEngine.RemoveStrategy` calls
  `_positions.Forget(instanceId)`. Re-adding an edited spec while its strategy holds an open position may therefore forget that position,
  even though the broker still holds it. **UNVERIFIED.** No test was written for it; it needs an end-to-end fixture with a real engine and
  position manager.

## Lessons

- **"No test file constructs the class" is a better predictor than line count.** Every one of the 18 orchestration survivors lived in a
  class whose only test coverage was through a substitute of its interface. A2j's sentence applies with the nouns changed: *what this layer
  computes is guarded; what it DOES when a button is pressed was not.*
- **The fixture was too comfortable again** (A2i's shape), in two places:
  - LabRunner's "thin" spec is thin in both halves, so it cannot separate `n1 && n2` from `n1`.
  - Every look-ahead strategy in the causality-gate tests leaks through its ENTRY, so the stop's place in the fingerprint was never
    exercised.
- **Restoring from the campaign's backup after fixing production would have reverted the fixes.** `a2l_backup/` holds the pre-fix files.
  The prove-kills script therefore takes its own fresh backup (`a2l_prove_backup/`) before sabotaging. This is rule 4 in a new place: a
  backup is a snapshot of one moment, and a harness run later must not trust it.
- Under 4-way CPU contention, 1 of 24 catches was a pure flake and 2 more carried flaky passengers. That is roughly the same rate A2j saw,
  and it is why the audit is not optional.

## Files

- Harness: `a2l_sabotage.py` (campaign), `a2l_audit.py` (false-catch audit), `a2l_prove_kills.py` (proof), `a2l_run_new.sh`, `a2l_baseline.sh`
- Results: `a2l_sabotage_results.json`, `a2l_sabotage_log.txt`, `a2l_audit_results.json`, `a2l_audit_log.txt`, `a2l_prove_kills_results.json`,
  `a2l_prove_kills_log.txt`, `a2l_defect_demo_before_fix.txt`
- Tests (new): `StrategyLifecycleTests.cs`, `EditableStrategySpecRoundTripTests.cs`, `StrategySpecNarratorTests.cs`, `SetupSonifierSpeechTests.cs`,
  `MultiTimeframeDataServiceTests.cs`, `ScriptStrategyCausalityFingerprintTests.cs`; (extended) `LabRunnerTests.cs`
- Production: `EditableStrategySpec.cs`, `StrategyLibraryFacade.cs`, `StrategyModalCoordinator.cs`

## For the docs (to apply on merge; not edited here)

- CHANGES: record the three fixes above, with their caveats. None of them was verified in the running app, and the builder still cannot
  EDIT StopAdjust or a component stop; the fix only preserves them.
- TODO: add the two "observed, not fixed" items. Put the next A2 target after Strategies: Analysis (4,269 / 17, never mutated), then
  Trading and Scripting. Those are running now in other worktrees.
- README test count: 8,120 cases on this branch before merge.
