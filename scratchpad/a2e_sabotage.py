#!/usr/bin/env python3
"""A2e — the FOURTH mutant set, aimed at two areas that have NEVER been mutated.

WHY THESE TWO AREAS, and why a targeted set rather than a fourth broad one.

Reconstructing the file lists from a2b/a2d/a2_disambiguate: across all three prior
campaigns, **39 distinct production files have ever had a mutant applied, out of
660 production .cs files**. The 73.1% catch rate is real but it is measured over
~6% of the tree. A fourth BROAD set re-measures the same statistic; a fourth
TARGETED set closes an area that has no measurement at all. The 2026-08-30 entry
already reached that conclusion ("a fourth run is worth less than closing an area
that has no tests at all") — this applies it.

The two areas, and why these two of the seven never-mutated ones:

  * Core/Services/Input (6 files) — EVERY KEYSTROKE IN THE APPLICATION. The
    dispatcher's modal gate and chart-focus gate, the crossing engine, key
    normalisation, the modal stack, reference-level placement. Zero mutants ever.
    It is also the area rewritten on 2026-09-06 (the 0 key by role, the crossing
    engine by role), so it is simultaneously the most recently changed and the
    least measured.
  * Core/Services/Alerts (10 files) + AlertEvaluator — the thing that wakes a
    trader who is not looking at the screen. Zero mutants ever. It is also the
    foundation the background-monitor expansion will be built on, so a survivor
    found now is a survivor found before the blast radius triples.

SAMPLING FRAME: none of these files appears in A2, A2b, A2c or A2d.

METHOD — identical to a2d_sabotage.py so the numbers compare: apply one mutant,
build, run the FULL AccessibleTrader.Tests suite, record whether anything went red
and WHICH tests did, revert, touch. CAUGHT iff some test fails.

THE FOUR HARNESS RULES (each learned from a run that lied — see the
sabotage-harness-rules note):
  1. `touch` after restoring, or MSBuild keeps the sabotaged binary.
  2. "No test matches the given testcase filter" is a FAILURE, not a pass. (Not
     reachable here — this runs the whole suite — but the summary regex is
     checked for a match and a miss records failed=-1 rather than 0.)
  3. Assert the anchor is UNIQUE before patching. A 0- or 2-occurrence anchor
     records BAD_ANCHOR; an unapplied sabotage is UNVERIFIED, never a result.
  4. Restore from a file copy held in memory here, never `git checkout --`, and
     the tree must be COMMITTED before starting.

RULE 5, LEARNED DURING THIS VERY RUN (2026-09-06): DO NOT TOUCH THE REPO WHILE A
CAMPAIGN IS RUNNING. Mid-run I bumped <Version> to 2.9.0 and rewrote WHATSNEW.md
for the release. This repo has a doc-honesty test — AboutDialogHonestyTests.
TheReleaseDocsNameTheVersionThatIsActuallyBuilt — that reads Directory.Build.props
and diffs it against CHANGES.md and WHATSNEW.md. It went red, and E03 and E04 came
back CAUGHT with that single test as their only failure: two FALSE CATCHES, the
exact trap A2 hit with a flake (five falsely caught, naive rate 79% vs honest 61%).
THE CONTAMINATION FILTER: any mutant whose failing_tests list is exactly
["...AboutDialogHonestyTests.TheReleaseDocsNameTheVersionThatIsActuallyBuilt"] is
UNVERIFIED and must be re-run. The general form of the rule is worse than "don't
edit code": a campaign's baseline includes the DOCS, because this suite asserts on
them. The tree must be quiet — source, docs and version — from launch to control run.

Run DETACHED with setsid — a tracked background command is capped at ten minutes
and this takes over an hour. An IN-FLIGHT marker survives SIGKILL so a stale
sabotage is restored from git at startup.
"""
import json, os, re, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2e_sabotage_results.json")
INFLIGHT = os.path.join(REPO, "scratchpad", "a2e_sabotage_inflight.txt")

SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+)")

# (id, area, file, find, replace, what-it-breaks)
MUTANTS = [
    # ── Core/Services/Input: the keyboard, never mutated before ──────────────
    ("E01", "modal gate lets chart keys through",
     "AccessibleTrader.Core/Services/Input/CommandDispatcher.cs",
     "                if (!allowedWhileModalOpen)",
     "                if (allowedWhileModalOpen)",
     "every chart command fires while a dialog is open, and Escape stops closing it"),

    ("E02", "chart-focus gate inverted",
     "AccessibleTrader.Core/Services/Input/CommandDispatcher.cs",
     "            if (!_isChartActive && IsChartScopedCommand(command) && !nudgeUnderObjectTree)",
     "            if (_isChartActive && IsChartScopedCommand(command) && !nudgeUnderObjectTree)",
     "arrow keys work ONLY when the chart does not have focus — typing in a text box navigates the chart"),

    ("E03", "F1 no longer escapes a modal",
     "AccessibleTrader.Core/Services/Input/CommandDispatcher.cs",
     "                    command == SystemCommand.OpenHelp;              // F1 — help is always reachable",
     "                    command == SystemCommand.None;                  // F1 — help is always reachable",
     "Help is unreachable from inside any dialog — the one key a stuck user reaches for"),

    ("E04", "level placement ignores the focused component's neutral",
     "AccessibleTrader.Core/Services/Input/CommandDispatcher.cs",
     "                    if (compIdx >= 0 && compIdx < focused.Components.Count)\n"
     "                        paneNeutral = focused.Components[compIdx].ReferenceLevel;",
     "                    if (compIdx >= 0 && compIdx < focused.Components.Count)\n"
     "                        paneNeutral = null;",
     "the 0 key ignores the component under the cursor and takes the first one on the pane"),

    ("E05", "price pane loses its zero refusal",
     "AccessibleTrader.Core/Services/Input/ReferenceLevelPlacement.cs",
     "            if (!double.IsFinite(cursorPrice) || cursorPrice <= 0)",
     "            if (!double.IsFinite(cursorPrice))",
     "a level at 0 on the price pane again — the defect this whole class exists to stop"),

    ("E06", "removal tolerance widened a hundredfold",
     "AccessibleTrader.Core/Services/Input/ReferenceLevelPlacement.cs",
     "        internal const double RemoveTolerance = 0.0025;",
     "        internal const double RemoveTolerance = 0.25;",
     "pressing 0 anywhere within 25% of a level deletes it — a level placed on a different bar"),

    ("E07", "midline role no longer inferred",
     "AccessibleTrader.Sdk/Models/LevelConfig.cs",
     '                    n.Equals("Midpoint", StringComparison.OrdinalIgnoreCase) ||',
     '                    n.Equals("Midpointt", StringComparison.OrdinalIgnoreCase) ||',
     "RSI's Midpoint at 50 stops being a midline: Ctrl+Left/Right cannot reach it again"),

    ("E08", "overbought/oversold direction inference dropped",
     "AccessibleTrader.Sdk/Models/LevelConfig.cs",
     "                    return LevelCrossDirection.Above;",
     "                    return LevelCrossDirection.Both;",
     "an overbought line reports crossings from below too — every RSI dip re-announces 70"),

    ("E09", "crossing engine picks the wrong jump direction",
     "AccessibleTrader.Core/Services/Input/IndicatorCrossingEngine.cs",
     "                bool better = found < 0 || (jumpRight ? idx < found : idx > found);",
     "                bool better = found < 0 || (jumpRight ? idx > found : idx < found);",
     "Ctrl+Right jumps to the FARTHEST crossing, skipping every one in between"),

    ("E10", "sparse-signal jump loses its NaN test",
     "AccessibleTrader.Core/Services/Input/IndicatorCrossingEngine.cs",
     "            bool hasNaN = compData != null && compData.Any(double.IsNaN);",
     "            bool hasNaN = compData != null;",
     "a continuous line is treated as a sparse marker — Ctrl+Left/Right lands on any bar"),

    ("E11", "modal stack re-open duplicates",
     "AccessibleTrader.Core/Services/Input/ModalStack.cs",
     "                    if (already >= 0) _open.RemoveAt(already);",
     "                    if (already > 0) _open.RemoveAt(already);",
     "re-opening the FIRST modal on the stack duplicates it — Escape needs two presses"),

    ("E12", "modal close ignores 'not open'",
     "AccessibleTrader.Core/Services/Input/ModalStack.cs",
     "                    changed = idx >= 0;",
     "                    changed = idx >= -1;",
     "a close for a modal that was never open fires a stack change and RemoveAt(-1) throws"),

    ("E13", "key normalisation strips the wrong prefix",
     "AccessibleTrader.Core/Services/Input/KeyNormalizationService.cs",
     '            if (cleanKey.StartsWith("KEY_")) cleanKey = cleanKey.Substring(4);',
     '            if (cleanKey.StartsWith("KEY_")) cleanKey = cleanKey.Substring(3);',
     "every KEY_-prefixed key normalises with a leading underscore and matches no binding"),

    ("E14", "blank key no longer short-circuits",
     "AccessibleTrader.Core/Services/Input/KeyNormalizationService.cs",
     "            if (string.IsNullOrWhiteSpace(key)) return string.Empty;",
     "            if (key == null) return string.Empty;",
     "a whitespace key falls through the map and is normalised to a non-empty token"),

    # ── AlertEvaluator: the firing rules ─────────────────────────────────────
    ("E15", "crossing fires without crossing",
     "AccessibleTrader.Core/Services/AlertEvaluator.cs",
     "                AlertCondition.CrossesAbove   => !double.IsNaN(prevValue) && prevValue < (alert.Threshold ?? 0) && currentValue >= (alert.Threshold ?? 0),",
     "                AlertCondition.CrossesAbove   => !double.IsNaN(prevValue) && currentValue >= (alert.Threshold ?? 0),",
     "a price ALREADY above the level fires on every bar — the level-vs-crossing distinction is gone"),

    ("E16", "duplicate-per-bar suppression removed",
     "AccessibleTrader.Core/Services/AlertEvaluator.cs",
     "            if (triggered && alreadyFiredThisBar) triggered = false;",
     "            if (triggered && !alreadyFiredThisBar) triggered = false;",
     "the exact 59-duplicate-emails-per-crossing bug, inverted: it fires ONLY on repeats"),

    ("E17", "cooldown ignored on repeat",
     "AccessibleTrader.Core/Services/AlertEvaluator.cs",
     "                && DateTime.UtcNow - last >= alert.Cooldown)",
     "                && DateTime.UtcNow - last >= TimeSpan.Zero)",
     "RepeatIfStillActive re-fires every poll regardless of the user's cooldown"),

    ("E18", "tree alert edge state lost",
     "AccessibleTrader.Core/Services/AlertEvaluator.cs",
     "            bool fire = eval.OverallTrue\n"
     "                && (!prev.WasTrue",
     "            bool fire = eval.OverallTrue\n"
     "                && (prev.WasTrue",
     "a condition-tree alert fires only while it was ALREADY true — never on the transition"),

    ("E19", "inactive alerts evaluated",
     "AccessibleTrader.Core/Services/AlertEvaluator.cs",
     "                if (!alert.IsActive) continue;",
     "                if (alert.IsActive) continue;",
     "disabled alerts fire and enabled ones never do"),

    ("E20", "direction-change reads one side only",
     "AccessibleTrader.Core/Services/AlertEvaluator.cs",
     "            return curBull != prevBull;",
     "            return curBull;",
     "a 'changes direction' alert fires on every bullish bar instead of on the flip"),

    # ── Core/Services/Alerts: watchability and delivery ──────────────────────
    ("E21", "condition trees declared background-watchable",
     "AccessibleTrader.Core/Services/Alerts/BackgroundWatchability.cs",
     "            if (a.ConditionTree != null)\n"
     '                return "advanced condition trees need the chart\'s indicator pipeline";',
     "            if (a.ConditionTree == null)\n"
     '                return "advanced condition trees need the chart\'s indicator pipeline";',
     "the monitor promises to watch tree alerts it cannot evaluate, and refuses simple ones"),

    ("E22", "alerts with no symbol pass the watchability gate",
     "AccessibleTrader.Core/Services/Alerts/BackgroundWatchability.cs",
     "            if (string.IsNullOrWhiteSpace(a.Symbol) || string.IsNullOrWhiteSpace(a.Provider))",
     "            if (string.IsNullOrWhiteSpace(a.Symbol) && string.IsNullOrWhiteSpace(a.Provider))",
     "an alert with a provider and no symbol is watched — the monitor fetches nothing, silently"),

    ("E23", "zone alert with no zone accepted",
     "AccessibleTrader.Core/Services/Alerts/BackgroundWatchability.cs",
     "                if (a.Zone == null)",
     "                if (a.Zone != null)",
     "a zone alert is rejected when it HAS a zone and accepted when it does not"),

    ("E24", "unconfigured channels dispatched to",
     "AccessibleTrader.Core/Services/Alerts/AlertDeliveryService.cs",
     "            if (!ch.IsConfigured) continue;",
     "            if (ch.IsConfigured) continue;",
     "every configured channel is skipped and every unconfigured one is called"),

    ("E25", "outbound guard fails OPEN on an unknown family",
     "AccessibleTrader.Core/Services/Alerts/OutboundNetworkGuard.cs",
     "            return false; // unknown family — fail closed",
     "            return true; // unknown family — fail closed",
     "an address family the guard does not understand is treated as public — SSRF"),

    ("E26", "loopback allowed as a webhook target",
     "AccessibleTrader.Core/Services/Alerts/OutboundNetworkGuard.cs",
     "            if (IPAddress.IsLoopback(ip)) return false;",
     "            if (IPAddress.IsLoopback(ip)) return true;",
     "a webhook can point at 127.0.0.1 and reach services on the user's own machine"),

    ("E27", "any-private-address check weakened to all",
     "AccessibleTrader.Core/Services/Alerts/OutboundNetworkGuard.cs",
     "            if (resolved.Length == 0 || resolved.Any(a => !IsPublic(a)))",
     "            if (resolved.Length == 0 || resolved.All(a => !IsPublic(a)))",
     "a hostname resolving to one public and one private address passes — DNS rebinding"),
]


def run(cmd, cwd=REPO, timeout=1800):
    p = subprocess.run(cmd, cwd=cwd, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, p.stdout + p.stderr


def build():
    code, out = run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                    "-p:UseRazorSourceGenerator=false -v q --nologo")
    return code == 0, out


def test():
    return run("dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
               "-p:UseRazorSourceGenerator=false --no-build")


def recover_inflight():
    """Rule 4's companion: `finally` does not run on SIGKILL. A stale marker means a
    sabotaged file is still on disk, and every later result would be measured against it."""
    if not os.path.exists(INFLIGHT):
        return
    rel = open(INFLIGHT).read().strip()
    print(f"!! stale in-flight sabotage in {rel} — restoring from git", flush=True)
    run(f"git checkout -- {rel}")
    os.utime(os.path.join(REPO, rel), None)
    os.remove(INFLIGHT)


def verify():
    """Rule 3, run BEFORE the campaign: every anchor must appear exactly once."""
    bad = 0
    for mid, area, relpath, find, repl, breaks in MUTANTS:
        path = os.path.join(REPO, relpath)
        if not os.path.exists(path):
            print(f"{mid}: MISSING FILE {relpath}")
            bad += 1
            continue
        n = open(path, encoding='utf-8-sig').read().count(find)
        if n != 1:
            print(f"{mid}: {n} occurrences in {relpath}\n     {find!r}")
            bad += 1
    print(f"\n{len(MUTANTS)} mutants, {bad} bad anchors")
    return bad


def main():
    if "--verify" in sys.argv:
        sys.exit(1 if verify() else 0)

    recover_inflight()
    only = [a for a in sys.argv[1:] if a.startswith("E")] or None
    results = json.load(open(OUT)) if os.path.exists(OUT) else []
    done = {r['id'] for r in results}

    for mid, area, relpath, find, repl, breaks in MUTANTS:
        if (only and mid not in only) or mid in done:
            continue
        path = os.path.join(REPO, relpath)
        original = open(path, encoding='utf-8-sig').read()
        n = original.count(find)
        rec = {'id': mid, 'area': area, 'file': relpath, 'breaks': breaks, 'occurrences': n}
        if n != 1:
            rec['status'] = 'BAD_ANCHOR'
            results.append(rec)
            json.dump(results, open(OUT, 'w'), indent=1)
            print(f"{mid}: BAD ANCHOR ({n} occurrences) — {relpath}", flush=True)
            continue
        t0 = time.time()
        try:
            open(INFLIGHT, 'w').write(relpath)
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original.replace(find, repl))
            ok, log = build()
            if not ok:
                rec['status'] = 'NO_COMPILE'
                rec['log'] = log[-1500:]
                print(f"{mid}: DID NOT COMPILE — {area}", flush=True)
            else:
                code, out = test()
                m = SUMMARY_RE.search(out)
                rec['failed'] = int(m.group(1)) if m else -1
                rec['passed'] = int(m.group(2)) if m else -1
                names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
                rec['failing_tests'] = names[:40]
                # Rule 2: a summary line that did not parse is not a pass.
                rec['status'] = ('UNPARSED' if rec['failed'] < 0
                                 else 'CAUGHT' if rec['failed'] > 0 else 'SURVIVED')
                print(f"{mid}: {rec['status']} failed={rec['failed']} "
                      f"({time.time()-t0:.0f}s) — {area}", flush=True)
                if names:
                    print("      " + "; ".join(names[:6]), flush=True)
        finally:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original)
            os.utime(path, None)          # Rule 1
            if os.path.exists(INFLIGHT):
                os.remove(INFLIGHT)
        rec['seconds'] = round(time.time() - t0)
        results.append(rec)
        json.dump(results, open(OUT, 'w'), indent=1)

    # THE CONTROL RUN: nothing sabotaged, everything must be green. A harness that
    # left the tree broken must not be mistaken for a clean pass.
    build()
    code, out = test()
    m = SUMMARY_RE.search(out)
    print(f"\n=== CONTROL (nothing sabotaged): {m.group(0) if m else 'UNPARSED'}", flush=True)

    print("\n=== summary")
    for r in results:
        print(f"  {r['id']} {r['status']:>10}  {r['area']}")
    surv = [r['id'] for r in results if r['status'] == 'SURVIVED']
    caught = sum(1 for r in results if r['status'] == 'CAUGHT')
    total = caught + len(surv)
    if total:
        print(f"\ncatch rate {caught}/{total} = {100*caught/total:.1f}%   survivors: {surv}")


if __name__ == '__main__':
    main()
