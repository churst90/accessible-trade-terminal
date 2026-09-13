#!/usr/bin/env python3
"""A2f — the FIFTH mutant set, aimed at the largest never-mutated area in the repo.

WHY THIS AREA.

Reconstructing the file lists from every prior campaign (a2, a2b, a2c/fresh, a2d,
a2e, a3), **66 distinct production files have ever had a mutant applied, out of
660** — about 10% of the tree, and the 73.1%/72.0% catch rate is a property of
that 10%.

`Core/Services/Accessibility` is 49 files and 15,898 lines: tied with
`Services/Indicators` as the largest body of code in the repo. Mutants ever
applied to it: ELEVEN, and all eleven landed on six small helper files
(EarconService, QuantityFormatter, SpeechPriceFormatter, SpeechTimeFormatter,
DotpadTactileDriver) plus four in AccessibilityFeedbackCoordinator.

The five largest files in it have had ZERO mutants, ever:

    DrawingInteractionManager.cs      1,362   0
    AccessibilityFeedbackCoordinator  1,343   4
    SpeechFormatter.cs                1,236   0
    NarrationScanner.cs               1,140   0
    TactileCanvasCoordinator.cs       1,026   0
    NavigationFeedbackManager.cs        780   0
    PlaybackNarration.cs                626   0

That is the speech and narration path. For this application's user it is not a
feature area — it is THE ENTIRE OUTPUT CHANNEL. A defect in the renderer is seen
eventually; a sentence that never fires is indistinguishable from a market that
did nothing. This is the one place in the codebase where a swallowed behaviour
has no compensating channel, which is the same argument that put A1 first.

It is also the area that has changed MOST in the last fortnight (the narration
ladder, the headless ladder, the coherence pass, the routing policy, playback
signals, band-crossing speech) — simultaneously the most recently rewritten and
the least measured, exactly as Input was before A2e.

SAMPLING FRAME: no file below appears in a2, a2b, fresh(a2c), a2d, a2e or a3.

METHOD — identical to a2d_sabotage.py and a2e_sabotage.py so the numbers compare:
apply one mutant, build, run the FULL AccessibleTrader.Tests suite, record whether
anything went red and WHICH tests did, revert, touch. CAUGHT iff some test fails.

THE FIVE HARNESS RULES (each learned from a run that lied — see the
sabotage-harness-rules note):
  1. `touch` after restoring, or MSBuild keeps the sabotaged binary.
  2. "No test matches the given testcase filter" is a FAILURE, not a pass. (Not
     reachable here — this runs the whole suite — but the summary regex is
     checked for a match and a miss records failed=-1 rather than 0.)
  3. Assert the anchor is UNIQUE before patching. A 0- or 2-occurrence anchor
     records BAD_ANCHOR; an unapplied sabotage is UNVERIFIED, never a result.
  4. Restore from a file copy held in memory here, never `git checkout --`, and
     the tree must be COMMITTED before starting.
  5. DO NOT TOUCH THE REPO — SOURCE *OR DOCS* — WHILE A CAMPAIGN IS RUNNING.
     This suite asserts on documentation (AboutDialogHonestyTests reads
     Directory.Build.props and diffs it against CHANGES.md and WHATSNEW.md), so
     a mid-run doc edit records FALSE CATCHES. A2e lost two findings that way.
     CONTAMINATION FILTER: any mutant whose failing_tests list is exactly
     ["...AboutDialogHonestyTests.TheReleaseDocsNameTheVersionThatIsActuallyBuilt"]
     is UNVERIFIED and must be re-run.

Run DETACHED with setsid — a tracked background command is capped at ten minutes
and this takes ~100 minutes. An IN-FLIGHT marker survives SIGKILL so a stale
sabotage is restored at startup.
"""
import json, os, re, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2f_sabotage_results.json")
INFLIGHT = os.path.join(REPO, "scratchpad", "a2f_sabotage_inflight.txt")

SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+)")

A = "AccessibleTrader.Core/Services/Accessibility/"

# (id, area, file, find, replace, what-it-breaks)
MUTANTS = [

    # ── SpeechFormatter.cs — every navigation readout in the app ─────────────
    ("F01", "a hidden component stops saying so on an X-scan",
     A + "SpeechFormatter.cs",
     "                bool sayState = isYMove || !comp.IsVisible;",
     "                bool sayState = isYMove;",
     "sweeping left/right across a HIDDEN component reads bare values as though it were on screen — "
     "the exact half of the 2026-09-04 report that said a hidden component must say so on every move"),

    ("F02", "timestamps speak when the user set the read location to None",
     A + "SpeechFormatter.cs",
     '                else if (state.TimestampReadLocation == "None") shouldSpeakTimestamp = false;',
     '                else if (state.TimestampReadLocation == "None") shouldSpeakTimestamp = true;',
     "a setting turned off is ignored: every arrow press prepends a timestamp the user switched off"),

    ("F03", "the date stops being spoken on the day it changes",
     A + "SpeechFormatter.cs",
     "            return dayChanged || state.SpeakDateOnEveryBar",
     "            return dayChanged && state.SpeakDateOnEveryBar",
     "scrolling across midnight never announces the new date unless the user opted into it on "
     "every bar — the 2.7.0 'date only when it changes' behaviour silently becomes 'date never'"),

    ("F04", "an empty series drops the prefix it was the only emitter of",
     A + "SpeechFormatter.cs",
     '                    return string.IsNullOrEmpty(prefixMessage) ? "" : prefixMessage.TrimEnd();',
     '                    return "";',
     "the early return that was widened on purpose closes again: a series with no components "
     "swallows the caller's prefix, which is the only thing that would have been said"),

    # ── VisibilityStateSpeech.cs — the 2026-09-04 hide+mute report ───────────
    ("F05", "hidden AND muted collapses back to one word",
     A + "VisibilityStateSpeech.cs",
     '            (false, true) => "hidden and muted",',
     '            (false, true) => "hidden",',
     "the defect this class was written for, reintroduced: a component needing TWO keys names one, "
     "so the user presses h, is still silent, and is further from what they wanted"),

    ("F06", "a muted component claims to be audible",
     A + "VisibilityStateSpeech.cs",
     '            (true, true) => "muted",',
     '            (true, true) => "",',
     "visible-but-muted reads as ordinary — the terminal reporting a state it is not in"),

    # ── SeriesNarrationScope.cs — one rule for who may speak ─────────────────
    ("F07", "a muted series still narrates",
     A + "SeriesNarrationScope.cs",
     "            => series.IsAutoNarrated && series.IsVisible && !series.IsMuted;",
     "            => series.IsAutoNarrated && series.IsVisible;",
     "M stops silencing a series for narration while it still silences its tone — the switch "
     "half-works, which is worse than not working"),

    ("F08", "component selection inverted",
     A + "SeriesNarrationScope.cs",
     "               && (!HasComponentSelection(series) || component.IsAutoNarrated);",
     "               && (HasComponentSelection(series) || component.IsAutoNarrated);",
     "when NO component has been singled out, none of them narrate — N on a whole series goes dead"),

    # ── ScanUtterance.cs — the one-utterance-per-scan contract ───────────────
    ("F09", "the clause cap is gone",
     A + "ScanUtterance.cs",
     "        private const int MaxClauses = 5;",
     "        private const int MaxClauses = 50;",
     "a busy bar close becomes a 40-second uninterruptible sentence — the cap is the whole reason "
     "the eight-Speak-calls-per-scan defect stayed fixed"),

    ("F10", "approach suppression inverted",
     A + "ScanUtterance.cs",
     "                .Where(c => c.Tier != TierApproach || !_approachSuppressed.Contains(c.Key))",
     "                .Where(c => c.Tier != TierApproach || _approachSuppressed.Contains(c.Key))",
     "only the CONTRADICTORY approach clauses survive: 'price crossed above R1 at 103.50, "
     "approaching R1 at 103.50' is exactly what SuppressApproachFor exists to stop"),

    ("F11", "the rarest signal is dropped and the commonest kept",
     A + "ScanUtterance.cs",
     "                .OrderBy(c => c.Tier).ThenBy(c => c.Order)",
     "                .OrderByDescending(c => c.Tier).ThenBy(c => c.Order)",
     "tier order reversed, so the cap keeps TierReading and discards TierBreak — the 21st pass "
     "defect (the gold dot dropped from every playback) arriving through the other path"),

    # ── NarrationScanner.cs — what gets said at a bar close ──────────────────
    ("F12", "an undeclared level subscription narrates nothing",
     A + "NarrationScanner.cs",
     "            if (comp.SubscribedLevelNames is not { } subs) return true;",
     "            if (comp.SubscribedLevelNames is not { } subs) return false;",
     "null means 'no declaration', which must mean every level. Flipped, every component that "
     "never declared a subscription goes silent on all of its levels"),

    ("F13", "an explicitly empty subscription narrates everything",
     A + "NarrationScanner.cs",
     "            if (subs.Count == 0) return false;",
     "            if (subs.Count == 0) return true;",
     "an empty list is a deliberate 'none' — flipped, an Oscillator narrates RSI's 50 and every "
     "other line on a scale it does not live on"),

    ("F14", "everything is approaching everything",
     A + "NarrationScanner.cs",
     "                bool nowNear = distPct <= 0.5;",
     "                bool nowNear = distPct <= 50.0;",
     "the proximity threshold is 100x, so every zone line announces an approach on every bar"),

    ("F15", "a level touch re-announces on every bar",
     A + "NarrationScanner.cs",
     "                    if (_lastTouchCount.TryGetValue(tcKey, out int lastTouches) && currentTouches > lastTouches)",
     "                    if (_lastTouchCount.TryGetValue(tcKey, out int lastTouches) && currentTouches >= lastTouches)",
     "'Price tested resistance, tested 3 times' repeats every bar for as long as the count holds"),

    ("F16", "a named level loses its number",
     A + "NarrationScanner.cs",
     '            return Math.Abs(level.Value) < 1e-9 && name.Contains("zero")',
     '            return Math.Abs(level.Value) < 1e-9 || name.Contains("zero")',
     "'overbought, 70' becomes 'overbought' for every level that happens to sit at zero, and any "
     "level named 'zero' anywhere loses its value — a crossing with no price in it"),

    # ── PlaybackNarration.cs — speech while the chart plays ──────────────────
    ("F17", "the narrate-during-playback switch is inverted",
     A + "PlaybackNarration.cs",
     '            if (!state.NarrateDuringPlayback) return "";',
     '            if (state.NarrateDuringPlayback) return "";',
     "turning narration ON during playback is what silences it"),

    ("F18", "the live edge is never reached",
     A + "PlaybackNarration.cs",
     "            && state.CurrentDataIndex >= state.Data.Count - 1;",
     "            && state.CurrentDataIndex > state.Data.Count - 1;",
     "an off-by-one that can never be true: playback never recognises that it has arrived at the "
     "live bar, so whatever that answer gates never happens"),

    ("F19", "the landmark unit is chosen backwards",
     A + "PlaybackNarration.cs",
     "            if (3600.0 / bar >= minBars) return LandmarkUnit.Hour;",
     "            if (3600.0 / bar <= minBars) return LandmarkUnit.Hour;",
     "a daily chart announces an hourly landmark on every bar; a one-minute chart announces none"),

    ("F20", "playback speaks no signals at all",
     A + "PlaybackNarration.cs",
     "        private const int MaxSignalClauses = 2;",
     "        private const int MaxSignalClauses = 0;",
     "the whole point of playback for a blind user — hearing what fired as the bars go by — "
     "returns nothing, silently and with no error"),

    # ── NavigationFeedbackManager.cs — what is said on every move ────────────
    ("F21", "every bar is inside every zone",
     A + "NavigationFeedbackManager.cs",
     "        private const double ZoneProximityPct = 0.005;",
     "        private const double ZoneProximityPct = 0.5;",
     "the 0.5% proximity window becomes 50%: every zone reads as active on every bar"),

    ("F22", "the component name is announced only when it did NOT change",
     A + "NavigationFeedbackManager.cs",
     "                    && s.ClampComponent(_previousState.FocusedComponentIndex) != currCompIdx)",
     "                    && s.ClampComponent(_previousState.FocusedComponentIndex) == currCompIdx)",
     "moving Up/Down between a drawing's anchors stops naming the anchor you arrived at, and "
     "names it when you did not move — the 'every drawing point says what it is' work, undone"),

    # ── ChartHitTester.cs — click-to-select, never mutated ───────────────────
    ("F23", "the hit tolerance is 12x tighter",
     A + "ChartHitTester.cs",
     "        public const double TolerancePx = 12.0;",
     "        public const double TolerancePx = 1.0;",
     "click-to-select requires pixel-exact aim — the interaction is nominally present and "
     "practically unusable, which is the failure mode hardest to notice from a test"),

    ("F24", "the hit tester picks the farthest series under the cursor",
     A + "ChartHitTester.cs",
     "                    if (dist <= tolerancePx && (best == null || dist < best.DistancePx))",
     "                    if (dist <= tolerancePx && (best == null || dist > best.DistancePx))",
     "with two lines in range it selects the one further from the cursor — a selection rule is "
     "only under test when at least two candidates compete (A2e's E09, other file)"),

    ("F25", "clicks on the x-axis strip select a series",
     A + "ChartHitTester.cs",
     "            if (yFrac < 0 || yFrac > plotBottomFrac) return null; // over the x-axis strip",
     "            if (yFrac < 0 || yFrac > 1.0) return null; // over the x-axis strip",
     "the strip is no longer excluded, so a click below the last pane reports a hit in it"),

    # ── GlobalErrorCoordinator.cs — the error SPEECH channel ─────────────────
    ("F26", "a High-severity error no longer interrupts",
     A + "GlobalErrorCoordinator.cs",
     "            bool interrupt = ev.Severity >= ErrorSeverity.High;",
     "            bool interrupt = ev.Severity > ErrorSeverity.High;",
     "a High error queues behind whatever is being read instead of cutting in — for a blind user "
     "an error that waits its turn is an error delivered after the decision it was about"),
]


def run(cmd, cwd=REPO, timeout=2400):
    p = subprocess.run(cmd, cwd=cwd, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def build():
    code, out = run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                    "-p:UseRazorSourceGenerator=false -v q --nologo")
    return code == 0, out


def test():
    # NO `-v q --nologo` HERE, and that is the whole point. A2f's first pass added them and
    # every failing_tests list came back EMPTY, because quiet verbosity suppresses the per-test
    # failure lines. That silently removed the false-catch audit — the check that turned A2's
    # naive 79% into an honest 61% and A2e's 74.1% into 72.0%. A mutant "caught" by one flaky
    # test firing alone is indistinguishable from a real catch without these names.
    return run("dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
               "-p:UseRazorSourceGenerator=false --no-build")


def recover_inflight():
    """Rule 4/5: a SIGKILLed run leaves a sabotage on disk. Restore it from git —
    this is the ONE place git is the right answer, because the tree was committed
    before launch and the marker names a file we know we corrupted."""
    if not os.path.exists(INFLIGHT):
        return
    rel = open(INFLIGHT).read().strip()
    if rel:
        run(f"git checkout -- {rel}")
        print(f"recovered stale sabotage in {rel}", flush=True)
    os.remove(INFLIGHT)


def verify():
    """Rule 3, applied to the WHOLE SET before a minute of compute is spent."""
    ok = True
    for mid, area, relpath, find, repl, _ in MUTANTS:
        path = os.path.join(REPO, relpath)
        if not os.path.exists(path):
            print(f"{mid}: MISSING FILE {relpath}")
            ok = False
            continue
        src = open(path, encoding='utf-8-sig').read()
        n = src.count(find)
        if n != 1:
            print(f"{mid}: {n} occurrences (need exactly 1) in {relpath}\n    {find[:100]!r}")
            ok = False
        if find == repl:
            print(f"{mid}: EQUIVALENT — find == replace")
            ok = False
    print("all anchors unique" if ok else "ANCHOR CHECK FAILED")
    return ok


def main():
    if "--verify" in sys.argv:
        sys.exit(0 if verify() else 1)

    recover_inflight()
    only = [a for a in sys.argv[1:] if a.startswith("F")] or None
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
                rec['status'] = ('UNPARSED' if rec['failed'] < 0
                                 else 'CAUGHT' if rec['failed'] > 0 else 'SURVIVED')
                # Rule 5 contamination filter.
                if names and all('AboutDialogHonestyTests' in x for x in names):
                    rec['status'] = 'UNVERIFIED_DOC_CONTAMINATION'
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

    # THE CONTROL RUN: nothing sabotaged, everything must be green.
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
