#!/usr/bin/env python3
"""A2l FALSE-CATCH AUDIT — mandatory because four campaigns share the CPU.

For every CAUGHT mutant in a2l_sabotage_results.json:
  1. re-apply the mutant, build, and re-run ONLY the tests that failed in the campaign;
  2. record which of them fail AGAIN (a test that passes on rerun was a flake, not a catch).
Then, once, on the CLEAN tree, run the union of every failing test; any that fails there is a
pre-existing / flaky failure and cannot count as a catch for anyone.

A mutant is HONESTLY CAUGHT iff at least one of its tests fails on the rerun with the mutant AND
passes on the clean tree. Bookkeeping guards (pinned lists) are flagged by hand in the report.

Harness rules: file-copy restore + touch; filter-matched-nothing is a FAILURE; never -v q on test.
"""
import filecmp, json, os, re, shutil, subprocess, sys

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
sys.path.insert(0, os.path.join(REPO, "scratchpad"))
import a2l_sabotage as H  # noqa: E402

RES = os.path.join(REPO, "scratchpad", "a2l_sabotage_results.json")
OUT = os.path.join(REPO, "scratchpad", "a2l_audit_results.json")
NO_MATCH = "No test matches the given testcase filter"
FAILED_RE = re.compile(r'^\s*Failed\s+([A-Za-z0-9_.]+)', re.M)


def run_filter(names):
    filt = "|".join(f"FullyQualifiedName~{n}" for n in names)
    code, out = H.run("nice -n 10 dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                      f"-p:UseRazorSourceGenerator=false --no-build --filter \"{filt}\"")
    if NO_MATCH in out:
        return None, out
    m = H.SUMMARY_RE.search(out)
    if not m:
        return None, out
    return sorted(set(FAILED_RE.findall(out))), out


def main():
    results = json.load(open(RES))
    mutants = {m[0]: m for m in H.MUTANTS}
    caught = [r for r in results if r['status'] == 'CAUGHT']
    H.backup_all()
    audit = []
    for r in caught:
        mid, area, rel, find, repl, _ = mutants[r['id']]
        path = os.path.join(REPO, rel)
        original = open(os.path.join(H.BACKUP, rel), encoding='utf-8-sig').read()
        assert original.count(find) == 1, mid
        try:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original.replace(find, repl))
            ok, log = H.build()
            if not ok:
                audit.append({'id': mid, 'status': 'NO_COMPILE'}); continue
            again, out = run_filter(r['failing_tests'])
        finally:
            H.restore(rel)
        rec = {'id': mid, 'campaign_failing': r['failing_tests'],
               'rerun_failing': again, 'filter_ok': again is not None}
        print(f"{mid}: rerun with mutant -> {again}", flush=True)
        audit.append(rec)
        json.dump(audit, open(OUT, 'w'), indent=1)

    H.check_restored()
    ok, _ = H.build()
    union = sorted({t for r in caught for t in r['failing_tests']})
    clean, out = run_filter(union)
    print(f"\nCLEAN TREE (build ok={ok}), {len(union)} tests: failing = {clean}", flush=True)

    print("\n=== verdict")
    honest = 0
    for rec in audit:
        if 'rerun_failing' not in rec:
            print(f"  {rec['id']} {rec['status']}"); continue
        real = [t for t in (rec['rerun_failing'] or []) if t not in (clean or [])]
        flakes = [t for t in rec['campaign_failing'] if t not in (rec['rerun_failing'] or [])]
        rec['honest_tests'] = real
        rec['flakes'] = flakes
        rec['verdict'] = 'HONEST' if real and rec['filter_ok'] else 'FALSE_CATCH'
        honest += rec['verdict'] == 'HONEST'
        print(f"  {rec['id']} {rec['verdict']:>12}  real={real}  flakes={flakes}")
    json.dump({'audit': audit, 'clean_failing': clean, 'union': union}, open(OUT, 'w'), indent=1)
    print(f"\nhonest catches {honest}/{len(results)}")


if __name__ == '__main__':
    main()
