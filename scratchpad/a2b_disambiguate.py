#!/usr/bin/env python3
"""A2b — the second pass, which is the one that makes the number true.

A2's sharpest procedural finding: FIVE mutants came back CAUGHT by exactly one
failing test, and in each case that test had nothing to do with the mutation —
it was a flake firing alone. Re-running each against the flaky test in
isolation turned all five green, so all five had in fact SURVIVED. Without this
pass A2 would have reported 79% instead of 61%.

So: every CAUGHT verdict resting on a small number of failing tests is re-tried
here. The mutant is re-applied, the suite is rebuilt, and ONLY the named tests
are run — three times, because the failure mode being ruled out is a test that
fails intermittently for its own reasons.

A test only keeps its catch if it fails WITH the mutant applied on every one of
the three isolated runs. A test that passes even once under the mutant did not
catch it; if no named test survives that check, the mutant is reclassified
SURVIVED.

Run after `a2b_sabotage.py`. Writes `a2b_disambiguate_results.json`.
"""
import json, os, re, subprocess, sys, time, importlib.util

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
IN = os.path.join(REPO, "scratchpad", "a2b_sabotage_results.json")
OUT = os.path.join(REPO, "scratchpad", "a2b_disambiguate_results.json")

# Any CAUGHT mutant with at most this many distinct failing tests gets re-checked.
# A2's spurious catches were all single-test; 2 is a deliberate margin.
THRESHOLD = int(os.environ.get("A2B_THRESHOLD", "2"))
RUNS = int(os.environ.get("A2B_RUNS", "3"))

_spec = importlib.util.spec_from_file_location(
    "a2b", os.path.join(REPO, "scratchpad", "a2b_sabotage.py"))
_a2b = importlib.util.module_from_spec(_spec)
_saved_argv, sys.argv = sys.argv, ["a2b_import_only", "--none"]
_spec.loader.exec_module(_a2b)
sys.argv = _saved_argv

BY_ID = {m[0]: m for m in _a2b.MUTANTS}
SUMMARY_RE = re.compile(r'Failed:\s*(\d+),\s*Passed:\s*(\d+)')


def run(cmd, timeout=3600):
    return subprocess.run(cmd, shell=True, cwd=REPO, capture_output=True, text=True, timeout=timeout)


def build():
    r = run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
            "-p:UseRazorSourceGenerator=false -v:q --nologo")
    return r.returncode == 0


def test_filtered(fqn):
    """Run exactly one test by fully-qualified name. Returns (failed, passed)."""
    r = run("dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
            f"-p:UseRazorSourceGenerator=false --no-build --nologo --filter 'FullyQualifiedName~{fqn}'")
    m = SUMMARY_RE.search(r.stdout)
    if not m:
        # A filter that matches nothing is a FAILURE of the check, not a pass.
        return (-1, -1)
    return (int(m.group(1)), int(m.group(2)))


def main():
    results = json.load(open(IN))
    out = []
    if os.path.exists(OUT):
        out = json.load(open(OUT))
    done = {r['id'] for r in out}

    todo = [r for r in results
            if r.get('status') == 'CAUGHT'
            and 0 < len(r.get('failing_tests', [])) <= THRESHOLD
            and r['id'] not in done]

    print(f"{len(todo)} CAUGHT verdicts rest on <= {THRESHOLD} tests — re-checking each\n", flush=True)

    for rec in todo:
        mid = rec['id']
        _, area, relpath, find, repl, breaks = BY_ID[mid]
        path = os.path.join(REPO, relpath)
        original = open(path, encoding='utf-8-sig').read()
        if original.count(find) != 1:
            print(f"{mid}: BAD ANCHOR on re-check", flush=True)
            continue

        entry = {'id': mid, 'area': area, 'named': rec['failing_tests'], 'per_test': {}}
        t0 = time.time()
        try:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original.replace(find, repl))
            if not build():
                entry['status'] = 'NO_COMPILE'
            else:
                genuine = []
                for fqn in rec['failing_tests']:
                    runs = [test_filtered(fqn) for _ in range(RUNS)]
                    entry['per_test'][fqn] = runs
                    # Kept only if it fails under the mutant on EVERY run.
                    if all(f > 0 for f, _ in runs):
                        genuine.append(fqn)
                    elif any(f < 0 for f, _ in runs):
                        entry.setdefault('notes', []).append(f"{fqn}: filter matched nothing")
                entry['genuine'] = genuine
                entry['status'] = 'CAUGHT' if genuine else 'SURVIVED'
        finally:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original)
            os.utime(path, None)

        entry['seconds'] = round(time.time() - t0)
        verdict = ("holds" if entry['status'] == 'CAUGHT'
                   else "SPURIOUS — reclassified SURVIVED")
        print(f"{mid}: {verdict}  ({entry['seconds']}s)  {area}", flush=True)
        for fqn, runs in entry['per_test'].items():
            print(f"      {fqn}: " + ", ".join(f"F{f}/P{p}" for f, p in runs), flush=True)
        out.append(entry)
        json.dump(out, open(OUT, 'w'), indent=1)

    build()
    print("\n=== disambiguation summary")
    for r in out:
        print(f"  {r['id']} {r['status']:>10}  {r['area']}")


if __name__ == '__main__':
    main()
