#!/usr/bin/env python3
"""A2m — prove every new guard RED on the defect it was written for, and GREEN on control.

All SIXTEEN honest survivors (13 from the run, plus M07/M08/M30 whose only "catches" were flakes
— each passed on rerun with the mutant applied — and M23, whose "survival" was a test host abort
that a full rerun showed to be a true survivor). Each was checked for equivalence first; none is.

Plus FLAG: the one production defect the pass found (ChartPatternDetector returned before the flag
scan when a series held fewer than three swings). Its "mutant" is the fix reverted.

Filters name the guarding TESTS, so a red result names the guard and nothing else. Harness rules
as a2m_sabotage.py: file-copy restore + touch + byte compare, "No test matches" is a FAILURE, no
`-v q` on test, rebuild before exiting, then a CONTROL run of every filter on the clean tree.
"""
import filecmp, json, os, re, shutil, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2m_prove_kills_results.json")
BACKUP = os.path.join(REPO, "scratchpad", "a2m_prove_backup")
SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+)")
NO_MATCH = "No test matches the given testcase filter"

A = "AccessibleTrader.Core/Services/Analysis/"
T = "AccessibleTrader.Tests."

KILLS = [
    ("M07", A + "ChartPatternNavigator.cs",
     "            bool had = _focus.Clear(chartKey);",
     "            bool had = _focus.IsPinned(chartKey);",
     [T + "ChartPatternPinNarrationTests.ShiftSemicolonReleasesThePin_SoTheJumpKeysReachEveryFormationAgain"]),

    ("M08", A + "ChartPatternNavigator.cs",
     "            int position = here.FindIndex(p => p.Key.Equals(picked.Key)) + 1;",
     "            int position = here.FindIndex(p => p.Key.Equals(picked.Key));",
     [T + "ChartPatternPinNarrationTests.ThePinAnnouncementCountsFromOne"]),

    ("M11", A + "ChartPatternFocus.cs",
     "            lock (_gate) return _pinned.Remove(chartKey);",
     "            lock (_gate) { bool had = _pinned.Remove(chartKey); _pinned.Clear(); return had; }",
     [T + "ChartPatternPositionTests.ClearingOneChartsPinLeavesAnotherChartsPinInForce"]),

    ("M22", A + "ChartPatternDetector.cs",
     "                int known = Math.Max(b.ConfirmedAtIndex, trough.ConfirmedAtIndex);",
     "                int known = b.BarIndex;",
     [T + "ChartPatternCausalityTests.EveryFormationIsFoundWithOnlyTheBarsUpToItsKnowableBar",
      T + "ChartPatternDetectorTests.NoPatternIsKnowableBeforeItsStructureIsComplete"]),

    ("M23", A + "ChartPatternDetector.cs",
     "                bool through = breakBelow ? bars[i].Close < trigger : bars[i].Close > trigger;",
     "                bool through = breakBelow ? bars[i].Low < trigger : bars[i].High > trigger;",
     [T + "ChartPatternDetectorTests.AConfirmationIsACloseThroughTheTrigger_NeverAWick"]),

    ("M25", A + "ChartPatternDetector.cs",
     "                    (0, +1) => ChartPatternKind.AscendingTriangle,",
     "                    (0, +1) => ChartPatternKind.DescendingTriangle,",
     [T + "ChartPatternDetectorTests.FlatTopWithRisingLows_IsNamedAnAscendingTriangle_AndConfirmsAboveTheTop"]),

    ("M26", A + "ChartPatternDetector.cs",
     "                bool breakBelow = kind is ChartPatternKind.DescendingTriangle\n                                       or ChartPatternKind.RisingWedge;",
     "                bool breakBelow = kind is ChartPatternKind.DescendingTriangle\n                                       or ChartPatternKind.FallingWedge;",
     [T + "ChartPatternDetectorTests.EveryFormationConfirmsOnTheSideItsNameImplies"]),

    ("M30", A + "SwingStructureAnalyzer.cs",
     "                    bool supersedes = p.IsHigh ? p.Price > last.Price : p.Price < last.Price;",
     "                    bool supersedes = p.IsHigh ? p.Price < last.Price : p.Price > last.Price;",
     [T + "SwingStructureTests.ASecondHighWithNoRealLowBetween_OnlyReplacesTheFirstIfItIsHigher"]),

    ("M33", A + "LevelRespectAnalyzer.cs",
     "                    if (bar.Close < lineAtJ - breakDistance)",
     "                    if (bar.Close < lineAtJ + breakDistance)",
     [T + "LevelRespectAnalyzerTests.ACloseJustAboveSupport_IsNotABreak"]),

    ("M35", A + "LevelRespectAnalyzer.cs",
     "            int end = Math.Min(bars.Count - 1, touchIdx + opts.ReactionWindowBars);",
     "            int end = bars.Count - 1;",
     [T + "LevelRespectAnalyzerTests.ARallyAfterTheReactionWindow_IsNotCreditedToTheTouch"]),

    ("M37", A + "MaRespectRanker.cs",
     "                int lastClosed = j - 1;",
     "                int lastClosed = j;",
     [T + "LevelRespectAnalyzerTests.MultiTimeframeMa_ReadsExactlyTheLastClosedWeeks"]),

    ("M38", A + "LevelProvenanceService.cs",
     "            var below = reliable.Where(s => s.CurrentValue < close).Take(topN).ToList();",
     "            var below = reliable.Where(s => s.CurrentValue > close).Take(topN).ToList();",
     [T + "LevelProvenanceServiceTests.ALevelIsNarratedOnTheSideOfPriceItIsActuallyOn"]),

    ("M39", A + "LevelProvenanceService.cs",
     "                    highs[i] = htf[j - 1].High;",
     "                    highs[i] = htf[j].High;",
     [T + "LevelProvenanceServiceTests.ThePriorDayHighIsYesterdays_NotTodaysStillFormingHigh"]),

    ("M41", A + "ValueDeviationAnalyzer.cs",
     "                var (p, h, l) = BuildProfile(bars, i - window, i);",
     "                var (p, h, l) = BuildProfile(bars, i - window + 1, i + 1);",
     [T + "ValueDeviationTests.ABarsReferenceDoesNotDependOnThatBar"]),

    ("M44", A + "AssetDossierService.cs",
     "            double pos = hi > lo ? (last.Close - lo) / (hi - lo) * 100 : 50;",
     "            double pos = hi > lo ? (hi - last.Close) / (hi - lo) * 100 : 50;",
     [T + "AssetDossierTests.PositionInRangeIsMeasuredFromTheBottom"]),

    ("M45", A + "AssetDossierService.cs",
     "            var cutoff = DateTime.UtcNow.AddDays(-90);",
     "            var cutoff = DateTime.UtcNow.AddDays(-900);",
     [T + "AssetDossierTests.FilingCountsReadOnlyTheLast90Days"]),

    # The production defect: the fix reverted — the swing floor gating the flag scan again.
    ("FLAG", A + "ChartPatternDetector.cs",
     "            if (swings.Count >= 3)\n            {",
     "            if (swings.Count < 3) return found;\n            {",
     [T + "ChartPatternDetectorTests.ABullFlagIsFoundInATrendTooCleanToHaveThreeSwings",
      T + "ChartPatternCausalityTests.EveryFormationIsFoundWithOnlyTheBarsUpToItsKnowableBar"]),
]


def run(cmd, timeout=3600):
    p = subprocess.run(cmd, cwd=REPO, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def build():
    code, out = run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                    "-p:UseRazorSourceGenerator=false -v q --nologo")
    return code == 0, out


def test_names(names):
    filt = "|".join(f"FullyQualifiedName~{n}" for n in names)
    code, out = run("dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                    f'-p:UseRazorSourceGenerator=false --no-build --filter "{filt}"')
    if NO_MATCH in out:
        return None, out
    m = SUMMARY_RE.search(out)
    failed = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
    return (int(m.group(1)) if m else -1, int(m.group(2)) if m else -1, failed), out


def bpath(rel):
    return os.path.join(BACKUP, rel.replace("/", "__"))


def main():
    try:
        os.nice(10)
    except OSError:
        pass
    os.makedirs(BACKUP, exist_ok=True)
    for rel in {k[1] for k in KILLS}:
        shutil.copyfile(os.path.join(REPO, rel), bpath(rel))

    only = [a for a in sys.argv[1:] if not a.startswith("-")] or None
    results = []

    for kid, rel, find, repl, names in KILLS:
        if only and kid not in only:
            continue
        path = os.path.join(REPO, rel)
        original = open(path, encoding='utf-8-sig').read()
        n = original.count(find)
        rec = {'id': kid, 'file': rel, 'tests': names, 'occurrences': n}
        if n != 1:
            rec['status'] = 'BAD_ANCHOR'
            results.append(rec); print(f"{kid}: BAD ANCHOR ({n})", flush=True); continue
        t0 = time.time()
        try:
            open(path, 'w', encoding='utf-8').write(original.replace(find, repl))
            ok, log = build()
            if not ok:
                rec['status'] = 'NO_COMPILE'; print(f"{kid}: DID NOT COMPILE\n{log[-800:]}", flush=True)
            else:
                res, out = test_names(names)
                if res is None:
                    rec['status'] = 'FILTER_MATCHED_NOTHING'
                    print(f"{kid}: FILTER MATCHED NOTHING — a FAILURE", flush=True)
                else:
                    f, p, failed = res
                    rec.update(failed=f, passed=p, failing_tests=failed)
                    rec['status'] = 'UNPARSED' if f < 0 else 'RED' if f > 0 else 'STILL_GREEN'
                    print(f"{kid}: {rec['status']} failed={f} passed={p} ({time.time()-t0:.0f}s)", flush=True)
                    for t in failed:
                        print(f"      {t}", flush=True)
        finally:
            shutil.copyfile(bpath(rel), path)
            os.utime(path, None)
            rec['restored_identical'] = filecmp.cmp(path, bpath(rel), shallow=False)
        results.append(rec)
        json.dump(results, open(OUT, 'w'), indent=1)

    # CONTROL: every guard, clean tree.
    build()
    allnames = sorted({n for k in KILLS for n in k[4]})
    res, out = test_names(allnames)
    if res is None:
        ctrl = "FILTER MATCHED NOTHING"
    else:
        f, p, failed = res
        ctrl = f"failed={f} passed={p} {failed}"
    print(f"\n=== CONTROL (clean tree, all {len(allnames)} guards): {ctrl}", flush=True)
    results.append({'id': 'CONTROL', 'status': ctrl})
    json.dump(results, open(OUT, 'w'), indent=1)

    red = sum(1 for r in results if r.get('status') == 'RED')
    print(f"\n{red}/{len([r for r in results if r['id'] != 'CONTROL'])} proved RED")
    same = all(filecmp.cmp(os.path.join(REPO, rel), bpath(rel), shallow=False) for rel in {k[1] for k in KILLS})
    print("restored byte-identical" if same else "TREE NOT RESTORED")
    shutil.rmtree(BACKUP)


if __name__ == '__main__':
    main()
