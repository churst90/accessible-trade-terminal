#!/usr/bin/env python3
"""A2n FALSE-CATCH AUDIT (mandatory: the CPU is shared with three other campaigns, and this repo
has a known bwrap/script-worker flake class).

For every CAUGHT mutant in a2n_sabotage_results.json:
  1. re-apply the mutant, build, and re-run ONLY the tests that failed — each must fail again;
  2. on the CLEAN tree, run the same tests — each must pass.
A catch whose failing tests pass on the rerun (mutant applied), or fail on the clean tree, is a
FLAKE, not a catch. A catch whose only failing tests are unrelated to the mutated behaviour is
flagged for a human read (bookkeeping / collateral).

Restores from the same file copy as the campaign (scratchpad/a2n_backup), touches, and checks
byte-identity at the end.
"""
import json, os, re, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import a2n_sabotage as H

OUT = os.path.join(H.REPO, "scratchpad", "a2n_false_catch_audit.json")
NO_MATCH = "No test matches the given testcase filter"


def run_filtered(names):
    filt = "|".join(f"FullyQualifiedName={n}" for n in names)
    code, out = H.run("nice -n 10 dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                      f"-p:UseRazorSourceGenerator=false --no-build --filter \"{filt}\"", timeout=1800)
    if NO_MATCH in out:
        return None, [], out
    failed = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
    m = H.SUMMARY_RE.search(out)
    return (int(m.group(1)) if m else -1), failed, out


def main():
    results = json.load(open(H.OUT))
    mut = {m[0]: m for m in H.MUTANTS}
    H.snapshot_all()
    audit = []
    caught = [r for r in results if r['status'] == 'CAUGHT']

    for r in caught:
        mid, area, rel, find, repl, _ = mut[r['id']]
        names = r['failing_tests']
        path = os.path.join(H.REPO, rel)
        original = open(path, 'rb').read().decode('utf-8')
        assert original.count(find) == 1, mid
        rec = {'id': mid, 'names': names}
        try:
            open(path, 'wb').write(original.replace(find, repl).encode('utf-8'))
            ok, log = H.build()
            if not ok:
                rec['mutant_rerun'] = 'NO_COMPILE'
            else:
                nfail, failed, out = run_filtered(names)
                rec['mutant_rerun_failed'] = failed
                rec['mutant_rerun'] = ('FILTER_MATCHED_NOTHING' if nfail is None else
                                       f"{nfail} failed")
                rec['did_not_refail'] = sorted(set(names) - set(failed))
        finally:
            H.restore(rel)
        print(f"{mid}: mutant rerun {rec.get('mutant_rerun')} ; did not re-fail: {rec.get('did_not_refail')}", flush=True)
        audit.append(rec)
        json.dump(audit, open(OUT, 'w'), indent=1)

    # Clean tree: every name that failed anywhere must pass.
    ok, _ = H.build()
    assert ok, "clean build failed"
    all_names = sorted({n for r in caught for n in r['failing_tests']})
    nfail, failed, out = run_filtered(all_names)
    print(f"\nCLEAN TREE: {len(all_names)} tests re-run, {nfail} failed: {failed}", flush=True)
    for rec in audit:
        rec['fails_on_clean'] = sorted(set(rec['names']) & set(failed))
    json.dump(audit, open(OUT, 'w'), indent=1)

    print("\n=== verdicts")
    for rec in audit:
        real = sorted(set(rec.get('mutant_rerun_failed', [])) - set(rec['fails_on_clean']))
        rec['verdict'] = 'REAL' if real else 'FLAKE'
        rec['real_catchers'] = real
        print(f"  {rec['id']}: {rec['verdict']}  catchers={real}  flaky={rec.get('did_not_refail')}")
    json.dump(audit, open(OUT, 'w'), indent=1)
    H.verify_restored()


if __name__ == '__main__':
    main()
