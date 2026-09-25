#!/usr/bin/env python3
"""A2o FALSE-CATCH AUDIT — mandatory because the CPU was shared with three other campaigns.

For every CAUGHT mutant in a2o_sabotage_results.json:
  1. CLEAN: one run of the union of every failing test, on the unmutated tree. A test that
     fails here is a flake (or a pre-existing failure) and cannot be the reason a mutant
     counts as caught.
  2. MUTANT: re-apply that mutant alone and re-run exactly the tests that failed for it.
A catch is HONEST iff at least one of its failing tests fails again under the mutant AND
passes on the clean tree. Anything else is a FLAKE-CATCH and is scored as SURVIVED.

Also re-runs the control's one failure (ChartAreaBarSliderTests...) on the clean tree three
times, since the campaign's control run reported it red.

Harness rules as a2o_sabotage.py: byte backups, touch, unique anchors, no `-v q` on test,
"No test matches" is a failure, rebuild clean before exiting, byte-compare at the end.
"""
import filecmp, json, os, re, shutil, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import a2o_sabotage as S

OUT = os.path.join(S.REPO, "scratchpad", "a2o_audit_results.json")
NO_MATCH = "No test matches the given testcase filter"
CONTROL_FLAKE = "AccessibleTrader.Tests.WebHost.ChartAreaBarSliderTests.Flicking_the_slider_routes_through_the_arrow_key_navigation_pipeline"


def run_filter(names):
    filt = "|".join(f"FullyQualifiedName~{n}" for n in names)
    code, out = S.run("nice -n 10 dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                      f"-p:UseRazorSourceGenerator=false --no-build --filter \"{filt}\"")
    if NO_MATCH in out:
        return None, out
    failed = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
    return failed, out


def main():
    results = json.load(open(S.OUT))
    caught = [r for r in results if r['status'] == 'CAUGHT']
    by_id = {m[0]: m for m in S.MUTANTS}
    os.makedirs(S.BACKUP_DIR, exist_ok=True)
    for rel in sorted({m[2] for m in S.MUTANTS}):
        b = S.backup_path(rel)
        if not os.path.exists(b):
            shutil.copyfile(os.path.join(S.REPO, rel), b)
        assert filecmp.cmp(os.path.join(S.REPO, rel), b, shallow=False), f"{rel} differs from backup before audit"

    audit = {'clean_failures': [], 'control_flake_reruns': [], 'mutants': []}

    ok, log = S.build()
    assert ok, log[-2000:]
    union = sorted({t for r in caught for t in r.get('failing_tests', [])})
    failed, out = run_filter(union)
    assert failed is not None, "clean filter matched nothing"
    audit['clean_failures'] = failed
    print(f"CLEAN run of {len(union)} caught-by tests: {len(failed)} failed: {failed}", flush=True)

    for i in range(3):
        f, _ = run_filter([CONTROL_FLAKE])
        audit['control_flake_reruns'].append(f)
        print(f"control-flake rerun {i+1}: {'FAILED' if f else 'passed'}", flush=True)
    json.dump(audit, open(OUT, 'w'), indent=1)

    for r in caught:
        mid = r['id']
        _, area, relpath, find, repl, _ = by_id[mid]
        path = os.path.join(S.REPO, relpath)
        original = open(path, encoding='utf-8', newline='').read()
        assert original.count(find) == 1, f"{mid} anchor not unique"
        names = r.get('failing_tests', [])
        rec = {'id': mid, 'area': area, 'campaign_failing': names}
        try:
            with open(path, 'w', encoding='utf-8', newline='') as fh:
                fh.write(original.replace(find, repl))
            ok, log = S.build()
            if not ok:
                rec['verdict'] = 'NO_COMPILE'
            else:
                refailed, out = run_filter(names)
                if refailed is None:
                    rec['verdict'] = 'FILTER_MATCHED_NOTHING'
                else:
                    rec['rerun_failing'] = refailed
                    honest = [t for t in refailed if t not in audit['clean_failures']]
                    rec['honest_by'] = honest
                    rec['verdict'] = 'HONEST' if honest else 'FLAKE_CATCH'
        finally:
            shutil.copyfile(S.backup_path(relpath), path)
            os.utime(path, None)
        print(f"{mid}: {rec['verdict']}  {rec.get('honest_by', [])[:3]}", flush=True)
        audit['mutants'].append(rec)
        json.dump(audit, open(OUT, 'w'), indent=1)

    S.build()
    same = all(filecmp.cmp(os.path.join(S.REPO, rel), S.backup_path(rel), shallow=False)
               for rel in {m[2] for m in S.MUTANTS})
    print("restored byte-identical" if same else "TREE NOT RESTORED", flush=True)
    honest = sum(1 for m in audit['mutants'] if m['verdict'] == 'HONEST')
    total = len([r for r in results if r['status'] in ('CAUGHT', 'SURVIVED')])
    print(f"honest catches {honest}/{total} = {100*honest/total:.1f}%", flush=True)


if __name__ == '__main__':
    main()
