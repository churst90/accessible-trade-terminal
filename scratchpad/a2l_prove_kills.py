#!/usr/bin/env python3
"""A2l — prove every new guard RED on the mutant it was written for, and GREEN on the clean tree.

Every survivor (after the false-catch audit) is here; none was equivalent. The mutants are taken
from a2l_sabotage.MUTANTS by id so the text proved is the text the campaign ran. Filters name
ONE test class (or one test) each, so a red result names the guard that caught it.

Harness rules: file-copy restore + touch; "No test matches" is a FAILURE; never -v q on test;
REBUILD before exiting (A2h) so a later --no-build run does not test the last mutant; print the
failing names so a uniform result looks as wrong as it is (A2f's inverted-success trap).
"""
import json, os, re, shutil, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
sys.path.insert(0, os.path.join(REPO, "scratchpad"))
import a2l_sabotage as H  # noqa: E402

OUT = os.path.join(REPO, "scratchpad", "a2l_prove_kills_results.json")
NO_MATCH = "No test matches the given testcase filter"
FAILED_RE = re.compile(r'^\s*Failed\s+([A-Za-z0-9_.]+)', re.M)

LIFE = "AccessibleTrader.Tests.StrategyLifecycleTests"
RT = "AccessibleTrader.Tests.EditableStrategySpecRoundTripTests"

KILLS = [
    ("L01", LIFE),
    ("L02", LIFE),
    ("L03", LIFE),
    ("L04", LIFE),
    ("L05", "AccessibleTrader.Tests.ScriptStrategyCausalityFingerprintTests"),
    ("L10", RT),
    ("L11", RT),
    ("L12", RT),
    ("L13", RT),
    ("L14", "AccessibleTrader.Tests.StrategySpecNarratorTests"),
    ("L15", "AccessibleTrader.Tests.StrategySpecNarratorTests"),
    ("L18", "AccessibleTrader.Tests.SetupSonifierSpeechTests"),
    ("L20", "AccessibleTrader.Tests.SetupSonifierSpeechTests"),
    ("L24", LIFE),
    ("L25", LIFE),
    ("L26", "AccessibleTrader.Tests.MultiTimeframeDataServiceTests"),
    ("L27", "AccessibleTrader.Tests.MultiTimeframeDataServiceTests"),
    ("L28", LIFE),
    ("L29", LIFE),
    ("L34", "AccessibleTrader.Tests.LabRunnerTests.Compare_ASecondHalfWithTooFewTrades"),
    ("L35", LIFE),
]


def run_filter(filt):
    code, out = H.run("nice -n 10 dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                      f"-p:UseRazorSourceGenerator=false --no-build --filter \"FullyQualifiedName~{filt}\"")
    if NO_MATCH in out:
        return 'FILTER_MATCHED_NOTHING', [], out
    m = H.SUMMARY_RE.search(out)
    if not m:
        return 'UNPARSED', [], out
    names = sorted(set(FAILED_RE.findall(out)))
    return ('RED' if int(m.group(1)) > 0 else 'GREEN'), names, out


def main():
    only = [a for a in sys.argv[1:] if re.fullmatch(r"L\d\d", a)] or None
    mutants = {m[0]: m for m in H.MUTANTS}
    # A FRESH backup of the tree as it is NOW. The campaign's a2l_backup holds the files as they
    # were BEFORE this pass's production fixes; restoring from it would silently revert them
    # (sabotage-harness-rules, rule 4).
    H.BACKUP = os.path.join(REPO, "scratchpad", "a2l_prove_backup")
    shutil.rmtree(H.BACKUP, ignore_errors=True)
    H.backup_all()
    results = []
    for mid, filt in KILLS:
        if only and mid not in only:
            continue
        _, area, rel, find, repl, _ = mutants[mid]
        path = os.path.join(REPO, rel)
        original = open(os.path.join(H.BACKUP, rel), encoding='utf-8-sig').read()
        rec = {'id': mid, 'area': area, 'filter': filt}
        if original.count(find) != 1:
            rec['status'] = 'BAD_ANCHOR'
            print(f"{mid}: BAD ANCHOR", flush=True)
            results.append(rec); continue
        t0 = time.time()
        try:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original.replace(find, repl))
            ok, log = H.build()
            if not ok:
                rec['status'] = 'NO_COMPILE'; rec['log'] = log[-800:]
                print(f"{mid}: DID NOT COMPILE\n{log[-800:]}", flush=True)
            else:
                status, names, out = run_filter(filt)
                rec['status'] = status if status != 'GREEN' else 'STILL_GREEN'
                rec['failing_tests'] = names
                print(f"{mid}: {rec['status']} ({time.time()-t0:.0f}s) — {area}", flush=True)
                for n in names[:6]:
                    print(f"      {n}", flush=True)
        finally:
            H.restore(rel)
        results.append(rec)
        json.dump(results, open(OUT, 'w'), indent=1)

    H.check_restored()
    ok, _ = H.build()
    filters = sorted({f for _, f in KILLS})
    control = {}
    for f in filters:
        status, names, _ = run_filter(f)
        control[f] = (status, names)
        print(f"CONTROL {f}: {status} {names}", flush=True)
    json.dump({'kills': results, 'control': control}, open(OUT, 'w'), indent=1)

    red = sum(1 for r in results if r['status'] == 'RED')
    green_ctrl = all(s == 'GREEN' for s, _ in control.values())
    print(f"\n{red}/{len(results)} proved RED; control all GREEN = {green_ctrl}")


if __name__ == '__main__':
    main()
