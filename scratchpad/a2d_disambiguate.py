#!/usr/bin/env python3
"""Disambiguate the THIN catches from the A2d 26-mutant campaign.

THE TRAP THIS EXISTS FOR. In A2 (2026-08-26) five mutants came back CAUGHT
because ONE unrelated flaky test happened to fire during that mutant's run. The
mutant had nothing to do with it. That is the entire difference between A2's
naive 79% and its true 61% — the single largest correction in this repo's
measurement history. A catch backed by one failing test name is not evidence
until the named test has been shown to fail BECAUSE of the mutant.

METHOD, and both halves are load-bearing:

  1. NEGATIVE CONTROL — run the named test in isolation on the CLEAN tree. It
     must PASS. If it fails clean, the "catch" was noise and the mutant is
     really a survivor.
  2. POSITIVE — apply the mutant, run the same named test in isolation. It must
     FAIL. Isolation matters: a test that only fails when the whole suite runs
     is reporting contention, not the mutant.

A run whose filter matched ZERO tests is recorded as INCONCLUSIVE, never as a
pass. A non-matching filter looks exactly like a green run and has fooled this
repo before — see the sabotage-harness rules.

Scope: every catch with two or fewer failing tests. A catch with 3+ independent
test names is not plausibly one flaky test firing.
"""
import json, os, re, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
RESULTS = os.path.join(REPO, "scratchpad", "a2d_sabotage_results.json")
OUT = os.path.join(REPO, "scratchpad", "a2d_disambiguate_results.json")
INFLIGHT = os.path.join(REPO, "scratchpad", "a2d_disambiguate_inflight.txt")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from a2d_sabotage import MUTANTS  # noqa: E402

BY_ID = {m[0]: m for m in MUTANTS}
THIN_MAX = 2

SUMMARY_RE = re.compile(r'Failed:\s*(\d+),\s*Passed:\s*(\d+).*?Total:\s*(\d+)')


def run(cmd, timeout=1800):
    return subprocess.run(cmd, shell=True, cwd=REPO, capture_output=True, text=True, timeout=timeout)


def build():
    return run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
               "-p:UseRazorSourceGenerator=false -v:q --nologo").returncode == 0


def run_one(test_name):
    """Run a single test by fully-qualified name. Returns (failed, passed, total)."""
    r = run(f'dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj '
            f'-p:UseRazorSourceGenerator=false --no-build --nologo '
            f'--filter "FullyQualifiedName={test_name}"')
    m = SUMMARY_RE.search(r.stdout)
    if not m:
        # "No test matches the given testcase filter" prints no summary line.
        return (-1, -1, 0)
    return int(m.group(1)), int(m.group(2)), int(m.group(3))


def recover():
    if not os.path.exists(INFLIGHT):
        return
    rel = open(INFLIGHT).read().strip()
    if rel:
        print(f"!! recovering {rel} from a killed run", flush=True)
        run(f"git checkout -- {rel!r}")
        os.utime(os.path.join(REPO, rel), None)
    os.remove(INFLIGHT)


def main():
    recover()
    campaign = json.load(open(RESULTS))
    thin = [r for r in campaign
            if r['status'] == 'CAUGHT' and 0 < len(r.get('failing_tests', [])) <= THIN_MAX]
    print(f"{len(thin)} thin catches to disambiguate: "
          f"{', '.join(r['id'] for r in thin)}\n", flush=True)

    out = json.load(open(OUT)) if os.path.exists(OUT) else []
    done = {r['id'] for r in out}

    for rec in thin:
        mid = rec['id']
        if mid in done:
            continue
        _, area, relpath, find, repl, _breaks = BY_ID[mid]
        path = os.path.join(REPO, relpath)
        original = open(path, encoding='utf-8-sig').read()
        t0 = time.time()
        res = {'id': mid, 'area': area, 'tests': rec['failing_tests'], 'checks': []}

        # 1. Negative control — clean tree, each named test must pass.
        build()
        for name in rec['failing_tests']:
            f, p, t = run_one(name)
            res['checks'].append({'phase': 'clean', 'test': name,
                                 'failed': f, 'passed': p, 'total': t})

        # 2. Positive — under the mutant, at least one named test must fail.
        try:
            open(INFLIGHT, 'w').write(relpath)
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original.replace(find, repl))
            build()
            for name in rec['failing_tests']:
                f, p, t = run_one(name)
                res['checks'].append({'phase': 'mutant', 'test': name,
                                     'failed': f, 'passed': p, 'total': t})
        finally:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original)
            os.utime(path, None)
            if os.path.exists(INFLIGHT):
                os.remove(INFLIGHT)

        clean = [c for c in res['checks'] if c['phase'] == 'clean']
        mut = [c for c in res['checks'] if c['phase'] == 'mutant']
        if any(c['total'] == 0 for c in res['checks']):
            res['verdict'] = 'INCONCLUSIVE_FILTER_MATCHED_NOTHING'
        elif any(c['failed'] > 0 for c in clean):
            res['verdict'] = 'SPURIOUS_FAILS_CLEAN_TOO'
        elif any(c['failed'] > 0 for c in mut):
            res['verdict'] = 'CONFIRMED'
        else:
            res['verdict'] = 'SPURIOUS_PASSES_UNDER_MUTANT'
        res['seconds'] = round(time.time() - t0)
        print(f"{mid}: {res['verdict']} ({res['seconds']}s) — {area}", flush=True)
        out.append(res)
        json.dump(out, open(OUT, 'w'), indent=1)

    build()
    print("\n=== disambiguation summary")
    for r in out:
        print(f"  {r['id']} {r['verdict']}")


if __name__ == '__main__':
    main()
