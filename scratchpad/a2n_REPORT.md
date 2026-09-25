# A2n — mutation campaign over `Core/Services/Trading` and `Core/Services/Scripting`

2026-09-24. Scripts: `scratchpad/a2n_sabotage.py` (45 mutants), `scratchpad/a2n_false_catch_audit.py`,
`scratchpad/a2n_prove_kills.py` (21/21 RED, control green). Results: `a2n_sabotage_results.json`,
`a2n_false_catch_audit.json`, `a2n_prove_kills_results.json`.

Method identical to A2j: one mutant, build, FULL `AccessibleTrader.Tests` run (not `-v q`), failing
names recorded, restore from a file copy + touch, byte-identity checked at the end. Baseline green
(8,088). Campaign control green (8,088), every file restored byte-identical. After this pass the
suite is 8,112, all green (3 m 39 s).

## Numbers

| Area | Mutants | Naive caught | Honest caught | Honest rate |
|---|---|---|---|---|
| Trading | 24 | 15 | 15 | **62.5%** |
| Scripting | 21 | 11 | 9 | **42.9%** |
| Both | 45 | 26 | 24 | **53.3%** |

Series so far: 73.1 / 72.0 / 69.2 / 62.2 / 10.5 / 73.5 / 82.0 / **53.3**. Scripting is the
second-lowest area measured, and it is the script sandbox.

### False-catch audit (mandatory, shared CPU)

Every catch was re-run with the mutant applied (its failing tests only) and on the clean tree.

- **S04 is a FLAKE.** Its only "catcher" was
  `Blazor.OrderTicketErrorStateTests.Pressing_the_refused_Submit_sends_nothing_and_says_why_out_loud`,
  which passed on rerun with the mutant applied. That test also failed beside T19's real catch, so it
  flaked in 2 of 47 full runs. Nothing to do with the sandbox. **This is a known-flaky test to fix separately.**
- **S21 is COLLATERAL, not a catch.** Leaking every worker slot exhausts the 16-worker cap part-way
  through the suite, and whichever real-worker tests run after that fail. None of the six re-failed in
  isolation, where fewer than 16 workers start. No test named the rule, and whether it went red
  depended on how many workers the rest of the suite happened to start. I counted it as a survivor.
- The other 24 catches are real: each re-failed with the mutant applied and passed on the clean tree
  (54 names re-run, 0 failures). No catch came only from a bookkeeping or pinned-list guard.
- No mutant hung the suite. The harness had a 20-minute process-group kill for that case.

## Survivors and how each was closed (all proved RED on the mutant, GREEN on control)

**Trading (9 survivors)**

- **T08/T09/T10/T11: `QuickTradeExecutor` had no test at all.** Nothing in 8,088 tests constructed
  one. These four mutants all survived: a long sent as a SELL, a limit sent to MARKET, the stop never
  sent, and a refusal dropped after "sent" was spoken. That last one is the exact defect described in
  the executor's own comment. Closed by the new `QuickTradeExecutorOrderTests`, which has 6 tests.
- **T03** (`Succeeded` counts `Uncertain`): not equivalent. It silences the "check your open orders
  before placing it again" sentence, and a retry after that is how a position gets doubled. Closed in
  `QuickTradeExecutorOrderTests.An_uncertain_quick_trade_tells_the_user_to_check_before_retrying`.
- **T15** (a limit priced at the last close instead of the cursor bar): every fixture had the two
  prices equal. Closed in `QuickTradeTests.ALimitIsPricedAtTheBarUnderTheCursorAndSizedForThatEntry`.
- **T18** (a signed stop distance): every risk-sized short was refused, and the short test pinned
  direction only. Closed in `QuickTradeTests.AShortIsSizedFromItsStopDistanceExactlyAsALongIs`.
- **T20** (a late balance re-arms over a trade already set up): closed in
  `QuickTradeEquityFetchTests.ALateBalanceDoesNotReArmOverATradeTheUserHasSinceSetUp`.
- **T36** (ladder `CloseQuantity` loses its cap): portions that sum past 1 flip the position. Every
  ladder in the suite summed to exactly 1. Closed in
  `StrategyPositionManagementTests.A_ladder_whose_portions_overshoot_never_sells_more_than_is_held`.

**Scripting (10 survivors + S04 + S21)**

- **S14/S15/S16/S18/S19/S20/S21: none of the supervisor's rules had ever been asked to act.** The
  rules are the concurrency cap, the per-call deadline, the kill on timeout, the memory quota, the CPU
  quota, refusing an out-of-protocol reply, and releasing a slot on dispose. Every host test drives a
  well-behaved script. Closed by the new `ScriptHostSupervisionTests`, which has 8 tests. Seven use a
  fake worker over an in-memory, cancellable pipe, so they run without a process or bwrap and do not
  depend on machine load. The eighth drives a REAL worker stuck in `Thread.Sleep(Infinite)` and
  proves the deadline interrupts a read from a child's stdout. It does, so there is no transport
  defect. A mutant that removes a deadline now fails a test instead of hanging the suite
  (`FailsWithin`).
- **S04** (the shipped constructor's env-var path): the existing tests only ever went through the
  seam that INJECTS the decision. **S03** (no security event under the override): nothing checked for
  one. **S27**: iOS/macCatalyst refusal. **S26**: interface default `SandboxApplied`. All four are
  closed by the new `SandboxRefusalPolicyTests`, which has 5 methods and 6 cases.
- **S28** (macOS: a missing `sandbox-exec` never refuses): the Mac refusal cannot run on Linux and
  the launcher had no seam. **Production change:** `MacSandboxExecLauncher` gained an internal
  constructor with the same shape as `LinuxBwrapLauncher`'s (platform flag, `sandbox-exec` path,
  override decision). The public constructor passes the real values, so behaviour is unchanged. The
  mutant was re-expressed on the new expression (`_allowUnsandboxed ?? …` → `true`) and proved RED.

## Real defects

**None were demonstrated in production behaviour.** Every survivor was a missing guard, not a bug.
Findings for Cody:

1. **`IScriptWorkerLauncher.SandboxApplied` has no reader.** Its doc says the flag exists so that
   "surfacing the degradation to the user" is possible, but nothing in Core, WebHost or BlazorClient
   reads it. S26 therefore changes nothing a user can observe today, so it is effectively equivalent.
   It is pinned now as the documented contract, but the user-facing surface it promises does not exist.
2. **Escape during an in-flight balance fetch does not cancel it.** This is UNVERIFIED and there is
   no test for it. `Disarm()` while the state is Idle says "Nothing was armed". When the fetch then
   lands, `if (State.Stage == Idle) Arm(...)` arms anyway, so the user's cancel is ignored. Low harm,
   because arming does not place an order, but it is behaviour a user would call a bug. Decide before
   anyone "fixes" it.
3. **The Windows AppContainer refusal is still untestable here.** The Windows mutant (S29) was
   written, then dropped to keep the campaign within 35–45 mutants, so it was never run. The launcher
   has no seam, so it is unverified on Linux.
4. **The flaky test above** (`OrderTicketErrorStateTests.Pressing_the_refused_Submit…`).

## Tests added: 24 cases, 8,088 → 8,112

- New: `AccessibleTrader.Tests/QuickTradeExecutorOrderTests.cs` (6),
  `ScriptHostSupervisionTests.cs` (8, `[Collection("ScriptWorker")]`),
  `SandboxRefusalPolicyTests.cs` (6, `[Collection("ProviderCredentialBridge")]` because it swaps the
  global `PluginHostServices.SecurityEvents`; it retries only when a WebHost start-up replaced the
  global).
- Extended: `QuickTradeTests.cs` (+2), `QuickTradeEquityFetchTests.cs` (+1),
  `StrategyPositionManagementTests.cs` (+1).
- The new classes were run 5× in a row with no failures.

## Lessons

- **The layer that turns an event into an order was the unguarded one.** Both halves of quick trade
  looked covered, because the BUILDER of the event had thorough tests. The CONSUMER, which is a
  30-line class, had none. When one side of a publish/subscribe pair is tested, check the other side
  has tests too.
- **A supervisor that has only watched well-behaved children is untested.** Seven of the Scripting
  survivors were rules that act only on misbehaviour, and no fixture misbehaved. A fake transport made
  each rule a deterministic, 1–3 s test. One real-worker test keeps the fake honest about the transport.
- **A catch by resource exhaustion is not a catch.** S21 went red in the full suite only because
  enough other tests started workers to use up the cap. The false-catch audit caught this: it re-runs
  the failing tests in isolation, where the effect disappears.
- **A seam used by every test leaves the shipped path untested.** The Linux refusal tests all
  injected `allowUnsandboxed`, so the `?? SandboxPolicy.AllowUnsandboxedFallback` branch that
  production takes had never run.
