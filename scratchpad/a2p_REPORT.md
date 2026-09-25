# A2p — mutation campaign over `AccessibleTrader.Sdk`

**Question:** if a plausible single-line bug were introduced into the Sdk, would the green suite notice?
**Answer:** about half the time. Honest rate **24/48 = 50.0%** (raw 26/49 = 53.1%). Before this pass only five Sdk files had ever been mutated. After it, 23 of the 24 non-equivalent survivors are closed by behaviour tests, each seen RED on its mutant and GREEN on the clean tree. W04 is still open. One real production defect turned up in `CryptoAddressValidator` (BIP-350 checksum pairing); it is demonstrated and fixed.

## Frame

- **Scope:** 49 mutants in 24 Sdk files. 22 of those files had never been mutated (only RateLimiter and IndicatorMath had). No site repeats an earlier campaign (a2 / a2d / a2e / fresh). The RateLimiter and IndicatorMath mutants are on lines nobody mutated before.
- **Selection:** weighted toward money and toward what a user hears. That means deposit-address validation, the live WebSocket feed, rate limiting and retry classification, bar and time handling, look-ahead in backtests, and risk and alert defaults. Every mutant is one plausible edit (off-by-one, inverted or dropped condition, wrong operand, dropped call, wrong default). Several restore defects that the source comments say were fixed once: P01, W01, W05, TS1, TS2, MK1, TB1, BB1.
- **Method:** three rsync copies of the tree (no bin/obj/.git), each built and baselined to the full suite first. All three were green at **8208/8208**. One worker per copy took one mutant at a time: apply, build, run the full `AccessibleTrader.Tests` suite, capture the full display names of failing tests, then restore by byte copy plus `touch`. A run whose passed+failed was short of 8208 would have been ABORTED and re-queued. None was.

| Area | Mutants | Caught (raw) | Survived |
|---|---|---|---|
| Trading/CryptoAddressValidator | P01–P06 | P05, P06 | P01, P02, P03, P04 |
| Services/ReconnectingWebSocket | W01–W07 | W01*, W05* | W02, W03, W04, W06, W07 |
| Services/RateLimiter | R01, R02 | R01 | R02 |
| Plugins/ProviderResult, TransportFailure | PR1, PR2, TF1 | PR2, TF1 | PR1 |
| Models/TimeframeUtility | T01–T03 | T01, T02 | T03 |
| Models/TimestampParser, MarketKey | TS1, TS2, MK1 | all | — |
| Models/TimeSeriesBuffer, Collections/CircularBuffer | TB1, TB2, CB1 | TB2, CB1 | TB1 |
| Models/WindowedBars | WB1–WB3 | all | — |
| Models/BarBucketConsolidator, Ohlcv | BB1, BB2, OH1 | all | — |
| Models/ComponentCausality, Plugins/AnalyticsPublicationLag, Models/SymbolFormat | CC1, CC2, AP1, SF1 | CC2, AP1, SF1 | CC1 |
| Wrong defaults (RiskPlan, AlertDefinition, WorkspaceState, BacktestConfig) | D1–D6 | D5 | D1, D2, D3, D4, D6 |
| Services/PluginHostServices | PH1, PH2 | — | PH1, PH2 |
| Theming (ThemePreset, ChartTheme) | TH1, TH2 | both | — |
| Indicators/IndicatorMath | IM1, IM2 | — | IM1, IM2 |

\* caught only by a source scan (see the bookkeeping audit below).

The full table is in `a2p_sabotage_results.json` and `a2p_campaign.log`. Each record holds the file, the exact anchor and replacement, the rationale, and every failing test by full name.

## Numbers

| | |
|---|---|
| Mutants | 49; 0 NO_COMPILE, 0 BAD_ANCHOR, 0 ABORTED |
| Raw | **26/49 = 53.1%** |
| After the false-catch audit | 26/26 catches honest, **no flakes** |
| After the bookkeeping audit | W01 and W05 were caught only by source scans. W01 is scored as a survivor; W05 is EQUIVALENT (below) |
| Equivalent (excluded) | W05 |
| **Honest** | **24/48 = 50.0%** |
| Non-equivalent survivors | 24 (23 raw + W01) — **23 closed, 1 open (W04)** |

### False-catch audit (`a2p_audit.py` → `a2p_audit_results.json`, `a2p_audit.log`)

For each of the 26 caught mutants I re-applied the mutant on its own, rebuilt, and re-ran its failing tests (method-level filter, results compared by full display name). Then I restored, rebuilt, and ran the same filter on the clean tree. All 26 went red again under the mutant and green on the clean tree. The clean runs had no failures, and no test failed only in the campaign run. **No flakes seen.**

### Bookkeeping and proxy audit (by reading what caught each mutant)

- **W01** (the give-up path no longer calls `_onDisconnected`) was caught only by `ReconnectingWebSocketContractTests.The_give_up_path_invokes_on_disconnected`. That test is a text scan asserting the string `_onDisconnected?.Invoke` appears between the give-up `if` and its `return`. It pins a spelling: `if (false) _onDisconnected?.Invoke();` passes it. **Scored as a survivor**, and closed with a behaviour test.
- **W05** (public `SendAsync` writes to `_ws` directly, bypassing `_sendLock`) was caught only by the scan `There_is_exactly_one_socket_write_and_it_holds_the_send_lock`. I wrote a behaviour test for it: eight 2 MB sends in flight against a server that holds off reading. With the mutant applied it stayed **GREEN** (`a2p_prove_kills_results.json`, W05 NOT_PROVED): all eight messages arrived and nothing threw. On .NET 10 the managed WebSocket serialises concurrent sends itself. So the "second SendAsync throws" failure the class doc describes did not happen here, and **W05 is EQUIVALENT on this runtime**. It is excluded from the denominator, and the never-red test was removed. *Measured on Linux only; not checked on Windows.*
- Every other catch was made by a test that asserts user-visible behaviour: address verdicts, bar and volume values, WCAG ratios, spoken refusals, retry decisions. D5 was caught by `WorkspaceStoreTests.InitialState_MatchesWorkspaceStateInitial`, which asserts `IsSpeechEnabled` directly, and by `UIDiagnosticTests`, which asserts that speech happens. Both count as behaviour.

## Survivors and how each was closed

Every closing test is in `a2p_prove_kills_results.json` / `a2p_prove_kills.log`: **24/25 proved** RED with the mutant applied alone and GREEN on the clean tree. The 25th entry is W05, which proves the equivalence above.

| Mutant | Closing test | What it pins |
|---|---|---|
| P01 | `CryptoAddressValidatorTests.A_bitcoin_token_on_an_evm_chain_is_checked_as_an_evm_address` (WBTC (ERC20), cbBTC (Base)) | Raw-substring routing sent a correct 0x deposit address to the Bitcoin check |
| P02 | `…A_real_taproot_address_verifies` (BIP-86 and BIP-350 vectors, checked with the BIP-350 reference decoder) | bc1p addresses were hidden |
| P03 | `…An_evm_address_one_character_too_long_is_rejected` | A 43-char 0x string was shown as valid |
| P04 | `…A_bitcoin_address_returned_for_a_tron_deposit_is_rejected` (plus `A_real_tron_address_verifies`) | A BTC address was VERIFIED for a TRC20 deposit |
| W01 | `ReconnectingWebSocketBehaviourTests.A_feed_that_gives_up_reconnecting_reports_itself_disconnected` | A dead feed that still says Connected |
| W02 | `…A_reconnect_that_succeeds_refills_the_reconnect_budget` | Reconnect budget counted across the whole session |
| W03 | `…The_heartbeat_sends_the_configured_keepalive_payload` | MEXC `{"method":"PING"}` override |
| W06 | `…A_message_larger_than_the_cap_is_refused_even_when_it_arrives_in_pieces` (16 MB + 1) | Per-frame, not per-message, OOM guard |
| W07 | `…Connecting_again_while_the_old_loop_is_backing_off_leaves_one_loop_on_one_socket` | A second receive loop on the new socket |
| R02 | `RateLimiterWindowTests.After_waiting_for_a_new_window_the_next_request_in_it_goes_straight_through` | Throughput collapse after saturation. No stopwatch: a free slot completes synchronously |
| PR1 | `ProviderRefusalClassificationTests.A_typed_401_or_403_is_reported_as_a_permission_problem` (+ a 503 → Failed test) | A 403 heard as "failed" |
| T03 | `ResampleBaseTimeframeTests.The_coarsest_native_interval_that_divides_the_target_is_chosen` (4 cases) | 4h built from 1m bars |
| TB1 | `TimeSeriesBufferImmutabilityTests.A_buffer_already_handed_out_keeps_its_last_bar_…` | Torn-bar shared write |
| CC1 | `CausalityPublicationContractTests.Only_a_declared_causal_component_is_publishable_and_both_questions_agree` | Undeclared leaf published. **No in-repo caller**: public SDK surface only |
| D1 | `RiskPlanDefaultsTests.A_setup_that_targets_no_more_than_it_risks_is_refused_by_default` | 1:1 setup accepted |
| D2 | `RiskPlanDefaultsTests.Default_sizing_risks_half_a_percent_of_equity` (10,000 × 0.5% / 1.00 = 50 units) | 10× position size |
| D3 | `AlertEvaluatorTests.A_repeating_alert_given_no_cooldown_does_not_repeat_on_the_next_poll` | Re-speaks every poll |
| D4 | `SetupAlertBridgeTests.A_setup_announcement_stays_in_the_ambient_tier_the_mutes_silence` | Mutes silence nothing |
| D6 | `BacktestDefaultCommissionTests.A_round_trip_on_a_flat_market_costs_a_tenth_of_a_percent_each_way` (equity 10,000 − 0.10 − 0.10) | StrategyLab combo, combo-sweep and diagnostic take the default |
| PH1 | `PluginHostFallbackHttpClientTests.A_provider_asking_for_a_short_timeout_gives_up_at_that_timeout` (real loopback server) | 300 ms asked, 60 s taken |
| PH2 | `…A_response_larger_than_the_provider_cap_is_refused` | Unbounded body on the no-bridge path |
| IM1 | `AverageTrueRangeTests.Atr_seeds_on_the_mean_of_the_first_full_period_then_smooths_wilder_style` (hand-worked 1.5, 5/3) | ATR biased low |
| IM2 | `…A_gap_down_bar_reaches_back_to_the_previous_close` (+ gap-up mirror) | Gap-down TR understated |

**Not closed: W04** ("heartbeat declares death after four failures, not three"). To reach it, `SendAsync` has to throw on a socket whose `State` stays Open, three times in a row, while the receive loop does not notice. I could not build that against a real `ClientWebSocket`: an aborted peer makes the receive loop null the socket first. The mutant is not equivalent (it adds 30 s to half-open detection at the default interval), but closing it needs a production seam (an injectable heartbeat send) or reflection into `_ws`. That is left for Cody (below). Also *unverified*: whether this failure counter can trip at all in production, since a failed send may leave the socket in a state the receive loop acts on first.

## Production defect found — demonstrated and fixed

**`CryptoAddressValidator.Bech32` accepted either checksum for any witness version.** BIP-350 requires version 0 to carry bech32 and versions 1 and up to carry bech32m. BIP-350's own invalid vector `bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kemeawh` (a v0 address with the bech32m checksum) came back **VERIFIED**. I confirmed with the reference decoder that it is v0 + bech32m.

- Demonstrated first: `CryptoAddressValidatorTests.A_segwit_v0_address_carrying_the_taproot_checksum_is_rejected` was red on the pre-fix code ("Assert.Equal() Failure: Values differ": Verified returned where Malformed was expected).
- Fix (4 lines, `AccessibleTrader.Sdk/Trading/CryptoAddressValidator.cs`): after the existing checksum check, `if ((data[0] == 0) != (checksum == 1)) return Malformed("the checksum type does not match the address version …")`. The P02 anchor line is untouched.
- Proved: FIX1 in `a2p_prove_kills_results.json`. With the new check replaced by `if (false)`, the guard goes RED; on the clean tree it is GREEN.
- Harm was low: BIP-350 wallets refuse such an address, so funds are unlikely to be sent. But the terminal *claimed* a verification that the spec says fails, which is the thing its own doc calls worse than admitting the limit.

## Decisions for Cody

1. **W04 / the heartbeat failure counter:** add a small internal seam so the "3 failed pings → reconnect" rule can be tested, or accept it as untested. My unverified suspicion is that it rarely or never trips in practice.
2. **ReconnectingWebSocket's `_sendLock` doc** says ClientWebSocket throws on an overlapping `SendAsync`. On .NET 10 / Linux it serialised instead (W05 evidence). The lock is harmless defence in depth. The doc may be stale, and whether Windows behaves the same is unverified. I made no change.
3. **`CausalityContract.IsPublishable`** has no caller in the repo; `SignalCatalog` uses `RefusalReason`. It is now pinned to agree with `RefusalReason`. Deleting it or keeping it as plugin API is your call.

## Unverified

- The W05 equivalence on Windows (it was only run on Linux .NET 10).
- Whether the heartbeat counter (W04) is reachable in production.
- Validator scope: it checks the Tron `T` prefix but not the 0x41 version byte. I noticed this but did not mutate or test it.

## Tests added (suite 8208 → **8243**, +35 cases)

| File | Cases |
|---|---|
| `AccessibleTrader.Tests/CryptoAddressValidatorTests.cs` (extended) | +8 (evm-chain BTC tokens ×2, taproot ×2, v0/bech32m, 43-char EVM, real Tron, BTC-for-Tron) |
| `AccessibleTrader.Tests/ReconnectingWebSocketBehaviourTests.cs` (new; real WebSocket server on loopback) | 5 |
| `AccessibleTrader.Tests/RateLimiterWindowTests.cs` (new) | 1 |
| `AccessibleTrader.Tests/SdkBehaviourGuardTests.cs` (new: refusal classification 3, causality contract 3, buffer immutability 1, resample base 4, ATR/TR 3, risk-plan defaults 2, backtest default commission 1) | 17 |
| `AccessibleTrader.Tests/SetupAlertBridgeTests.cs` (extended) | +1 |
| `AccessibleTrader.Tests/AlertEvaluatorTests.cs` (extended) | +1 |
| `AccessibleTrader.Tests/PluginHostFallbackHttpClientTests.cs` (new; `[Collection("ProviderCredentialBridge")]`, restores the global) | 2 |

No test computes its expected value with the production formula or reads a production constant. The defaults tests derive their values from the documented contract (0.5% of equity, R:R gate above 1:1, 0.1% per side, ambient alerts), worked by hand in comments. Every socket or HTTP wait is on a positive signal with a 20 s ceiling. None asserts that something did not happen within a short window.

## Harness and results files (`scratchpad/`)

- `a2p_sabotage.py`: the mutant list (anchor, replacement, rationale), `--verify`, `--setup` (three tree copies plus baselines), the campaign, and `--control`.
- `a2p_audit.py`: the false-catch audit. `a2p_prove_kills.py`: re-syncs the trees from the worktree, takes a fresh backup at apply time, then RED/GREEN for each kill.
- Results: `a2p_baseline.json` (3 × 8208 green), `a2p_sabotage_results.json`, `a2p_campaign.log`, `a2p_control.json`, `a2p_audit_results.json`, `a2p_audit.log`, `a2p_prove_kills_results.json`, `a2p_prove_kills.log`, `a2p_final_suite.log`.

## Final state

- Campaign control (nothing sabotaged, all three trees): **8208/8208 green, restored byte-identical** (`a2p_control.json`). The audit and prove-kills runs also ended with "restored byte-identical".
- **Final full suite on the final worktree tree (fix + new tests): `Passed! - Failed: 0, Passed: 8243, Skipped: 0, Total: 8243`** (`a2p_final_suite.log`).
- No file under `docs/` was touched.
