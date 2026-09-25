#!/usr/bin/env python3
"""A2m — the FALSE-CATCH AUDIT, mandatory because the CPU is shared with three other campaigns.

For every CAUGHT mutant in a2m_sabotage_results.json:
  1. re-apply the mutant, rebuild, and re-run ONLY the tests that failed in the campaign. They must
     fail again (a catch that passes on rerun is a flake);
  2. then, on the CLEAN tree, run the union of every failing test once. Any that fails there too is
     a flake, and a mutant whose ONLY catches are flakes is not caught.
Also re-runs M23 (the test host ABORTED at 4,634 of 8,088 tests, which the first harness recorded as
a survivor) with the full suite, keeping the log tail, so the abort can be read rather than guessed.

Harness rules as a2m_sabotage.py: file-copy restore + touch + byte compare; no `-v q` on test;
"No test matches" is a failure; rebuild before exiting.
"""
import filecmp, json, os, re, shutil, subprocess, sys, time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import a2m_sabotage as S

REPO = S.REPO
OUT = os.path.join(REPO, "scratchpad", "a2m_audit_results.json")
NO_MATCH = "No test matches the given testcase filter"


def filt(names):
    return "|".join(f"FullyQualifiedName~{n}" for n in names)


def run_filtered(names):
    code, out = S.test(f'--filter "{filt(names)}"')
    if NO_MATCH in out:
        return None, out
    return S.failing_names(out), out


def apply(mid):
    m = next(x for x in S.MUTANTS if x[0] == mid)
    _, _, rel, find, repl, _ = m
    path = os.path.join(REPO, rel)
    src = open(path, encoding='utf-8-sig').read()
    assert src.count(find) == 1, f"{mid}: anchor not unique"
    open(path, 'w', encoding='utf-8').write(src.replace(find, repl))
    return rel


def main():
    try:
        os.nice(10)
    except OSError:
        pass
    results = json.load(open(S.OUT))
    caught = [r for r in results if r['status'] == 'CAUGHT']
    audit = {'mutant_reruns': {}, 'clean': {}, 'm23': None}

    for r in caught:
        mid = r['id']
        rel = apply(mid)
        try:
            ok, log = S.build()
            if not ok:
                audit['mutant_reruns'][mid] = {'status': 'NO_COMPILE'}
                continue
            failed, out = run_filtered(r['failing_tests'])
            if failed is None:
                audit['mutant_reruns'][mid] = {'status': 'FILTER_MATCHED_NOTHING'}
                print(f"{mid}: FILTER MATCHED NOTHING", flush=True)
                continue
            again = sorted(set(failed))
            audit['mutant_reruns'][mid] = {'campaign': r['failing_tests'], 'rerun_failed': again}
            print(f"{mid}: rerun with mutant — {len(again)}/{len(r['failing_tests'])} fail again: "
                  + "; ".join(n.split('.')[-1] for n in again), flush=True)
        finally:
            S.restore(rel)
        json.dump(audit, open(OUT, 'w'), indent=1)

    # Clean tree: every test that failed anywhere in the campaign, once.
    S.build()
    union = sorted({n for r in caught for n in r['failing_tests']})
    failed, out = run_filtered(union)
    audit['clean'] = {'ran': union, 'failed_on_clean': failed}
    print(f"\nCLEAN TREE: {len(union)} tests, failing on clean: {failed}", flush=True)
    json.dump(audit, open(OUT, 'w'), indent=1)

    # M23 — the abort.
    rel = apply("M23")
    try:
        ok, log = S.build()
        code, out = S.test()
        m = S.SUMMARY_RE.search(out)
        audit['m23'] = {'summary': m.group(0) if m else 'UNPARSED', 'exit': code,
                        'failing': S.failing_names(out), 'tail': out[-6000:]}
        print(f"\nM23 full run: {audit['m23']['summary']} exit={code}", flush=True)
        for n in audit['m23']['failing'][:20]:
            print("   FAILED", n, flush=True)
    finally:
        S.restore("AccessibleTrader.Core/Services/Analysis/ChartPatternDetector.cs")
    json.dump(audit, open(OUT, 'w'), indent=1)

    S.build()
    same = all(filecmp.cmp(os.path.join(REPO, rel), S.backup_path(rel), shallow=False)
               for rel in {m[2] for m in S.MUTANTS})
    code, diff = S.run("git diff --stat -- AccessibleTrader.Core/")
    print("restored byte-identical" if same and not diff.strip() else f"TREE NOT RESTORED:\n{diff}")


if __name__ == '__main__':
    main()
