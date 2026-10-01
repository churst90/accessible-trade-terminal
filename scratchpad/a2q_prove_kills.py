#!/usr/bin/env python3
"""A2q — prove every new guard RED on the mutant it was written for, and GREEN on the clean tree.

Covers every survivor AND every test written ahead of the campaign for a predicted survivor
(the coordinator asked for both), plus sabotages of this branch's four production fixes.

Before anything runs, the three tree copies are re-synced from the WORKTREE (rsync, no
bin/obj/.git/scratchpad) so they carry the new tests and the fixes, and each tree must pass
QuietDesktopTests 5/5 before anything else runs in it. Every file a kill touches is byte-backed-up
FRESH from that synced tree at apply time (rule 5: a backup taken before a fix would revert it).

For each kill: apply the mutant alone, build, run the guard's filter → must be RED with at least
one failing test whose name contains `expect`; restore (byte copy + touch), rebuild, run the same
filter → must be GREEN. "No test matches the given testcase filter" is a failure. Never `-v q` on
test. Byte-compare every touched file against the worktree at the end.
"""
import json, os, queue, sys, threading
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import a2q_sabotage as S
from a2q_mutants import ALL_MUTANTS

OUT = os.path.join(S.REPO, "scratchpad", "a2q_prove_kills_results.json")
BY_ID = {m[0]: m for m in ALL_MUTANTS}
A = "AccessibleTrader.Core/Services/Accessibility/"

# Mutants re-anchored on this branch's FIXED code (the campaign anchor no longer exists there),
# and sabotages of the four production fixes.
EXTRA = {
    "D02f": ("completion names the wrong point (D02, on the fixed code: every index one lower)",
             A + "DrawingInteractionManager.cs",
             "int lastIndex = _anchorDate2 == null ? 0 : IsThreePoint(placedType) ? 2 : 1;",
             "int lastIndex = _anchorDate2 == null ? 0 : IsThreePoint(placedType) ? 1 : 0;", ""),
    "FIX1": ("revert: older history prepended is not noticed by the narrator",
             A + "AutoNarrationService.cs",
             "            NoteOlderHistoryPrepended(state);\n", "", ""),
    "FIX2": ("revert: Main-first pane order by the unstable List.Sort again",
             A + "ChartPaneModel.cs",
             "            return order\n                .OrderBy(k => k.Equals(MainPaneKey, StringComparison.OrdinalIgnoreCase) ? 0 : 1)\n",
             "            order.Sort((a, b) =>\n            {\n"
             "                bool aMain = a.Equals(MainPaneKey, StringComparison.OrdinalIgnoreCase);\n"
             "                bool bMain = b.Equals(MainPaneKey, StringComparison.OrdinalIgnoreCase);\n"
             "                if (aMain == bMain) return 0;\n                return aMain ? -1 : 1;\n            });\n"
             "            return order\n", ""),
    "FIX3": ("revert: the completed drawing's last point index",
             A + "DrawingInteractionManager.cs",
             "int lastIndex = _anchorDate2 == null ? 0 : IsThreePoint(placedType) ? 2 : 1;",
             "int lastIndex = _anchorDate2 == null ? 1 : 2;", ""),
    "FIX4a": ("revert: coordinator keeps its strip dedup memory across a reconnect",
              A + "TactileCanvasCoordinator.cs",
              "                    _lastStripText = null;\n", "", ""),
    "FIX4b": ("revert: driver keeps its strip dedup memory across a reconnect",
              A + "Dotpad/DotpadTactileDriver.cs",
              "            _lastBrailleText = null;\n", "", ""),
}
BY_ID.update({k: (k,) + v for k, v in EXTRA.items()})

T = "AccessibleTrader.Tests."
KILLS = [
    ("C03", "FormingPatternCommentaryTests.F2_silences_the_forming_commentary"),
    ("C04", "FormingPatternCommentaryTests.An_arrow_key_does_not_cut_off_a_forming_formation_mid_sentence"),
    ("C05", "FiredAlertDeliveryTests.F2_muting_the_chart_does_not_silence_an_alert"),
    ("C06", "MuteToggleConfirmationTests.F2_turning_chart_speech_off_is_confirmed_out_loud"),
    ("C07", "MuteToggleConfirmationTests.Shift_F2_muting_alerts_and_events_is_confirmed_out_loud"),
    ("C09", "ChartKeyboardNavigationTests.Delete_on_an_indicator_removes_it_and_says_so_even_with_chart_speech_off"),
    ("C10", "ErrorAndStatusAnnouncementTests.A_margin_warning_is_heard_with_alerts_and_events_muted"),
    ("C11","ErrorAndStatusAnnouncementTests.A_strategy_order_placed_is_heard_with_chart_speech_off"),
    ("C12", "ErrorAndStatusAnnouncementTests.Connection_lost_is_heard_with_chart_speech_off"),
    ("C15", "ErrorAndStatusAnnouncementTests.An_indicator_advisory_is_silenced_by_Shift_F2"),
    ("A01", "FiredAlertDeliveryTests.An_alert_set_to_speech_is_spoken"),
    ("A02", "FiredAlertDeliveryTests.A_break_through_alert_is_spoken_through_Shift_F2"),
    ("A03", "FiredAlertDeliveryTests.A_break_through_alert_is_sounded_with_earcons_muted"),
    ("A04", "FormingPatternCommentaryTests.A_second_pattern_within_five_seconds_waits"),
    ("A05", "FormingPatternCommentaryTests.Without_N_on_the_candles_there_is_no_running_commentary"),
    ("A06", "FormingPatternCommentaryTests.The_same_forming_pattern_is_not_said_again_after_the_debounce"),
    ("A07", "FormingPatternCommentaryTests.A_forming_formation_is_announced_once_not_every_five_seconds"),
    ("A09", "MuteToggleConfirmationTests.F3_says_sound_off_when_it_turns_sound_off_and_on_when_on"),
    ("N01", "ChartKeyboardNavigationTests.Right_on_the_last_bar_plays_the_boundary_earcon"),
    ("N02", "ChartKeyboardNavigationTests.Walking_left_near_the_start_of_the_loaded_data_asks_for_older_history"),
    ("N03", "ChartKeyboardNavigationTests.Page_Down_on_the_bottom_series_stays_there"),
    ("N04", "ChartKeyboardNavigationTests.Delete_on_the_candles_refuses_and_says_why"),
    ("N05", "ChartKeyboardNavigationTests.Moving_to_another_series_starts_Up_and_Down_at_its_first_component"),
    ("V01", "ChartKeyboardNavigationTests.Pan_left_shows_earlier_bars_and_pan_right_shows_later_ones"),
    ("R01", "ChartKeyboardNavigationTests.Up_and_Down_on_a_volume_profile_walk_its_price_bins"),
    ("H01", "HistoryBackfillCoordinatorTests.Loading_another_chart_lets_backfill_work_again_after_no_more_history"),
    ("H02", "HistoryBackfillCoordinatorTests.When_the_provider_has_nothing_older_the_user_is_told_so"),
    ("F01", "SpeechPriorityTimingTests.Key_repeat_does_not_cut_off_an_order_rejection_on_the_second_or_third_press"),
    ("F02", "SpeechPriorityTimingTests.A_short_alert_stops_holding_off_the_arrow_keys_once_it_has_been_said"),
    ("G01", "ErrorAndStatusAnnouncementTests.A_critical_error_is_announced_as_critical"),
    ("E01", "OrderEarconShapeTests.A_buy_fill_rises"),
    ("E02", "OrderEarconShapeTests.A_stop_loss_descends"),
    ("S01", "BarCloseStampTests.A_daily_bar_closes_on_a_date_not_at_a_clock_time"),
    ("P01", "ShapeAndDirectionNamingTests.The_highest_value_area_bin_is_called_value_area_high_and_the_lowest_value_area_low"),
    ("D01", "DrawingPlacementSpeechTests.After_the_stop_loss_is_set_risk_reward_asks_for_the_take_profit"),
    ("D02f", "DrawingPlacementSpeechTests.Completing_risk_reward_names_the_take_profit_as_the_last_point"),
    ("D03", "DrawingPlacementSpeechTests.Measuring_from_the_cursor_to_a_later_higher_bar_says_up"),
    ("DP1", "BrailleDisplayKeysAndToggleTests.The_Dot_Pads_left_panning_key_pans_the_chart_left"),
    ("T01", "BrailleDisplayKeysAndToggleTests.F4_with_braille_off_turns_it_on_and_says_so"),
    ("SC1", "ShapeAndDirectionNamingTests.A_doji_with_a_long_lower_shadow_and_no_upper_one_is_a_dragonfly"),
    ("I01", "ShapeAndDirectionNamingTests.A_MACD_line_falling_through_its_signal_is_narrated_as_a_bearish_crossover"),
    ("HC1", "HeadlessChartTests.After_a_reseed_the_alerts_have_no_pre_gap_value_to_compare_against"),
    ("HF1", "HeadlessChartTests.The_signature_changes_when_an_indicators_period_is_edited"),
    ("AN1", "FormingBarNarrationTests.A_signal_flickering_on_the_forming_bar_is_not_announced_until_the_bar_closes"),
    ("FIX1","HistoryBackfillNarrationTests.A_signal_already_heard_is_not_said_again_after_older_history_loads"),
    ("FIX2", "PaneModelAndTrailingSpeechTests.Three_panes_with_Main_declared_last_keep_the_other_two_in_first_appearance_order"),
    ("FIX3", "DrawingPlacementSpeechTests.Completing_a_trend_line_names_its_second_point"),
    ("FIX4a", "BrailleDisplayKeysAndToggleTests.After_braille_is_switched_off_and_on_again_the_strip_is_sent_again"),
    ("FIX4b", "BrailleDisplayKeysAndToggleTests.A_replugged_display_is_sent_the_strip_text_again"),
]
# (id, exact test filter, substring every counted red test must contain = the method name)
KILLS = [(k, f"FullyQualifiedName~{T}{t}", t.split(".")[-1]) for k, t in KILLS]


def sync_trees():
    for i in range(1, S.N_TREES + 1):
        t = S.tree_path(i)
        code, out = S.run(f"rsync -a --delete --exclude bin/ --exclude obj/ --exclude .git "
                          f"--exclude scratchpad/ ./ {t}/", S.REPO)
        assert code == 0, out
        os.makedirs(os.path.join(t, "scratchpad"), exist_ok=True)
        ok, log = S.build(t)
        assert ok, log[-1500:]
        q = S.parse(S.test(t, S.QUIET_FILTER)[1])
        assert q['failed'] == 0 and q['passed'] == S.QUIET_EXPECTED and not q['no_match'], \
            f"t{i}: QuietDesktopTests {q} — refusing to run anything in this tree"
        print(f"t{i}: synced from the worktree, QuietDesktopTests {q['passed']}/{q['total']}", flush=True)


def main():
    only = [a for a in sys.argv[1:] if not a.startswith("-")] or None
    kills = [k for k in KILLS if not only or k[0] in only]
    bad = []
    for kid, _, _ in kills:
        _, _, rel, find, _, _ = BY_ID[kid]
        n = open(os.path.join(S.REPO, rel), encoding='utf-8', newline='').read().count(find)
        if n != 1:
            bad.append((kid, n))
    assert not bad, f"anchors not unique in the worktree: {bad}"
    sync_trees()

    results = json.load(open(OUT)) if (only and os.path.exists(OUT)) else []
    results = [r for r in results if r['id'] not in {k[0] for k in kills}]
    q = queue.Queue()
    for k in kills:
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
            m = S.apply_run_restore(i, rel, find, repl, filt=filt, browser_on_survival=False)
            cs = m.get('csharp', {})
            red_named = [n for n in cs.get('failing', []) if expect in n]
            rec.update(mutant_status=m['status'], mutant_failed=cs.get('failed'), mutant_passed=cs.get('passed'),
                       mutant_failing=cs.get('failing'), mutant_messages=cs.get('messages'),
                       mutant_no_match=cs.get('no_match'), mutant_log=m.get('log'))
            ok, log = S.build(t)
            assert ok, log[-1500:]
            c = S.parse(S.test(t, filt)[1])
            rec.update(control_failed=c['failed'], control_passed=c['passed'],
                       control_failing=c['failing'], control_no_match=c['no_match'])
            proved = (m['status'] == 'CAUGHT' and bool(red_named) and not cs.get('no_match')
                      and c['failed'] == 0 and c['passed'] > 0 and not c['no_match'])
            rec['status'] = 'PROVED' if proved else 'NOT_PROVED'
            with lock:
                results.append(rec)
                json.dump(sorted(results, key=lambda r: r['id']), open(OUT, 'w'), indent=1)
            print(f"{kid}: {rec['status']}  mutant {m['status']} failed={cs.get('failed')} passed={cs.get('passed')}  "
                  f"control failed={c['failed']} passed={c['passed']}  {expect}", flush=True)
            for n in red_named[:3]:
                print(f"      RED: {n}", flush=True)
                print(f"           {cs.get('messages', {}).get(n, '')[:200]}", flush=True)

    ths = [threading.Thread(target=worker, args=(i,)) for i in range(1, S.N_TREES + 1)]
    for th in ths: th.start()
    for th in ths: th.join()

    same = True
    for i in range(1, S.N_TREES + 1):
        for rel in sorted({BY_ID[k[0]][2] for k in kills}):
            a = open(os.path.join(S.tree_path(i), rel), 'rb').read()
            if a != open(os.path.join(S.REPO, rel), 'rb').read():
                same = False; print(f"t{i}: {rel} NOT RESTORED")
    proved = sum(1 for r in results if r['status'] == 'PROVED')
    print(f"\n{proved}/{len(results)} proved RED on the mutant and GREEN on the clean tree")
    print("restored byte-identical" if same else "TREE NOT RESTORED")


if __name__ == '__main__':
    main()
