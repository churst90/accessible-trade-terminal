#!/usr/bin/env python3
"""A2p — prove every new guard RED on the mutant it was written for, and GREEN on the clean tree.

Before anything runs, the three tree copies are re-synced from the worktree (rsync, no
bin/obj/.git/scratchpad), so they carry the new tests and any production fix, and every file
a kill touches is byte-backed-up FRESH from that synced tree at apply time (rule 5: a backup
taken before a fix would revert the fix).

For each kill: apply the mutant alone, build, run the guard's filter → must be RED with at
least one failing test NAMED in `expect`; restore (byte copy + touch), rebuild, run the same
filter → must be GREEN. "No test matches the given testcase filter" is a failure. Never
`-v q` on test. Byte-compare every tree against the worktree at the end.

The KILLS list is the survivors (and scan-only / bookkeeping catches) from the campaign and
audit, each with the class-level filter of the test written for it.
"""
import filecmp, json, os, queue, sys, threading
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import a2p_sabotage as S

OUT = os.path.join(S.REPO, "scratchpad", "a2p_prove_kills_results.json")
BY_ID = {m[0]: m for m in S.MUTANTS}

# (mutant id, test filter [FullyQualifiedName~...], substring every counted red test must contain)
T = "AccessibleTrader.Tests."
KILLS = [
    ("P01", T + "CryptoAddressValidatorTests.A_bitcoin_token_on_an_evm_chain_is_checked_as_an_evm_address", "A_bitcoin_token_on_an_evm_chain"),
    ("P02", T + "CryptoAddressValidatorTests.A_real_taproot_address_verifies", "A_real_taproot_address_verifies"),
    ("P03", T + "CryptoAddressValidatorTests.An_evm_address_one_character_too_long_is_rejected", "one_character_too_long"),
    ("P04", T + "CryptoAddressValidatorTests.A_bitcoin_address_returned_for_a_tron_deposit_is_rejected", "tron_deposit"),
    # W01 and W05 were caught in the campaign only by SOURCE SCANS (spelling / call-site pins);
    # scored as not honestly caught, and closed with behavioural tests against a real socket.
    ("W01", T + "ReconnectingWebSocketBehaviourTests.A_feed_that_gives_up_reconnecting_reports_itself_disconnected", "gives_up"),
    ("W02", T + "ReconnectingWebSocketBehaviourTests.A_reconnect_that_succeeds_refills_the_reconnect_budget", "refills"),
    ("W03", T + "ReconnectingWebSocketBehaviourTests.The_heartbeat_sends_the_configured_keepalive_payload", "keepalive"),
    # W05: this run (see a2p_prove_kills_results.json) showed the behavioural test GREEN WITH the
    # mutant — .NET 10's managed WebSocket serialises overlapping sends itself — so W05 is scored
    # EQUIVALENT and the test was REMOVED afterwards (never seen red). Re-running this entry now
    # reports FILTER_MATCHED_NOTHING, which is correct.
    ("W05", T + "ReconnectingWebSocketBehaviourTests.Overlapping_sends_are_all_delivered", "Overlapping_sends"),
    ("W06", T + "ReconnectingWebSocketBehaviourTests.A_message_larger_than_the_cap_is_refused_even_when_it_arrives_in_pieces", "larger_than_the_cap"),
    ("W07", T + "ReconnectingWebSocketBehaviourTests.Connecting_again_while_the_old_loop_is_backing_off_leaves_one_loop_on_one_socket", "Connecting_again"),
    ("R02", T + "RateLimiterWindowTests.After_waiting_for_a_new_window_the_next_request_in_it_goes_straight_through", "After_waiting"),
    ("PR1", T + "ProviderRefusalClassificationTests.A_typed_401_or_403_is_reported_as_a_permission_problem", "403"),
    ("T03", T + "ResampleBaseTimeframeTests.The_coarsest_native_interval_that_divides_the_target_is_chosen", "coarsest"),
    ("TB1", T + "TimeSeriesBufferImmutabilityTests.A_buffer_already_handed_out_keeps_its_last_bar_when_the_live_tick_replaces_it", "handed_out"),
    ("CC1", T + "CausalityPublicationContractTests.Only_a_declared_causal_component_is_publishable_and_both_questions_agree", "Undeclared"),
    ("D1", T + "RiskPlanDefaultsTests.A_setup_that_targets_no_more_than_it_risks_is_refused_by_default", "no_more_than_it_risks"),
    ("D2", T + "RiskPlanDefaultsTests.Default_sizing_risks_half_a_percent_of_equity", "half_a_percent"),
    ("D3", T + "AlertEvaluatorTests.A_repeating_alert_given_no_cooldown_does_not_repeat_on_the_next_poll", "no_cooldown"),
    ("D4", T + "SetupAlertBridgeTests.A_setup_announcement_stays_in_the_ambient_tier_the_mutes_silence", "ambient_tier"),
    ("D6", T + "BacktestDefaultCommissionTests.A_round_trip_on_a_flat_market_costs_a_tenth_of_a_percent_each_way", "tenth_of_a_percent"),
    ("PH1", T + "PluginHostFallbackHttpClientTests.A_provider_asking_for_a_short_timeout_gives_up_at_that_timeout", "short_timeout"),
    ("PH2", T + "PluginHostFallbackHttpClientTests.A_response_larger_than_the_provider_cap_is_refused", "provider_cap"),
    ("IM1", T + "AverageTrueRangeTests.Atr_seeds_on_the_mean_of_the_first_full_period_then_smooths_wilder_style", "Atr_seeds"),
    ("IM2", T + "AverageTrueRangeTests.A_gap_down_bar_reaches_back_to_the_previous_close", "gap_down"),
    # The production fix (BIP-350 witness-version/checksum pairing) reverted — its guard must go red.
    ("FIX1", T + "CryptoAddressValidatorTests.A_segwit_v0_address_carrying_the_taproot_checksum_is_rejected", "taproot_checksum"),
]
KILLS = [(k, f"FullyQualifiedName~{flt}", e) for k, flt, e in KILLS]

# Not campaign mutants: sabotages of THIS session's production fix, proved the same way.
EXTRA = {
    "FIX1": ("revert the BIP-350 version/checksum pairing check",
             "AccessibleTrader.Sdk/Trading/CryptoAddressValidator.cs",
             "            if ((data[0] == 0) != (checksum == 1))\n",
             "            if (false)\n", ""),
}
BY_ID.update({k: (k,) + v for k, v in EXTRA.items()})


def sync_trees():
    for i in range(1, S.N_TREES + 1):
        t = S.tree_path(i)
        code, out = S.run(f"rsync -a --exclude bin/ --exclude obj/ --exclude .git "
                          f"--exclude scratchpad/ ./ {t}/", S.REPO)
        assert code == 0, out
        assert S.verify(t), f"t{i} anchors"


def main():
    only = [a for a in sys.argv[1:] if not a.startswith("-")] or None
    sync_trees()
    results = []
    q = queue.Queue()
    for k in KILLS:
        if not only or k[0] in only:
            q.put(k)
    lock = threading.Lock()

    def worker(i):
        t = S.tree_path(i)
        while True:
            try:
                kid, filt, expect = q.get_nowait()
            except queue.Empty:
                return
            _, area, rel, find, repl, _ = BY_ID[kid]
            rec = {'id': kid, 'area': area, 'filter': filt, 'tree': f"t{i}"}
            m = S.apply_run_restore(i, kid, rel, find, repl, filt=filt)
            red_named = [n for n in m.get('failing', []) if expect in n]
            rec.update(mutant_status=m['status'], mutant_failed=m.get('failed'), mutant_passed=m.get('passed'),
                       mutant_failing=m.get('failing'), mutant_messages=m.get('messages'),
                       mutant_no_match=m.get('no_match'))
            ok, log = S.build(t)
            assert ok, log[-1500:]
            _, out = S.test(t, filt)
            c = S.parse(out)
            rec.update(control_failed=c['failed'], control_passed=c['passed'],
                       control_failing=c['failing'], control_no_match=c['no_match'])
            proved = (m['status'] == 'CAUGHT' and red_named and not m.get('no_match')
                      and c['failed'] == 0 and c['passed'] > 0 and not c['no_match'])
            rec['status'] = 'PROVED' if proved else 'NOT_PROVED'
            with lock:
                results.append(rec)
                json.dump(sorted(results, key=lambda r: r['id']), open(OUT, 'w'), indent=1)
            print(f"{kid}: {rec['status']}  mutant failed={m.get('failed')} passed={m.get('passed')}  "
                  f"control failed={c['failed']} passed={c['passed']}  {filt}", flush=True)
            for n in red_named[:4]:
                print(f"      RED: {n}", flush=True)

    ths = [threading.Thread(target=worker, args=(i,)) for i in range(1, S.N_TREES + 1)]
    for th in ths: th.start()
    for th in ths: th.join()

    same = True
    for i in range(1, S.N_TREES + 1):
        S.build(S.tree_path(i))
        for rel in sorted({BY_ID[k[0]][2] for k in KILLS}):
            if not filecmp.cmp(os.path.join(S.tree_path(i), rel), os.path.join(S.REPO, rel), shallow=False):
                same = False; print(f"t{i}: {rel} NOT RESTORED")
    proved = sum(1 for r in results if r['status'] == 'PROVED')
    print(f"\n{proved}/{len(results)} proved RED on the mutant and GREEN on the clean tree")
    print("restored byte-identical" if same else "TREE NOT RESTORED")


if __name__ == '__main__':
    main()
