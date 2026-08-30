#!/usr/bin/env python3
"""Prove each A2d kill actually fails under the mutant it was written for.

A guard test that has never been seen red is not a guard — this repo's standing
rule. For each entry: apply the mutant, build, run ONLY the named tests, and
require that they FAIL. A run whose filter matched zero tests is a FAILURE, not a
pass: a non-matching filter is indistinguishable from green and has fooled this
repo before (see the sabotage-harness rules).

Every mutant here is the ORIGINAL A2d anchor, unchanged — no kill needed the
production line extracted to a seam first, so there is no re-anchoring to record.
"""
import json, os, re, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2d_prove_kills_results.json")
INFLIGHT = os.path.join(REPO, "scratchpad", "a2d_prove_kills_inflight.txt")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from a2d_sabotage import MUTANTS  # noqa: E402

BY_ID = {m[0]: m for m in MUTANTS}

# (mutant id, test filter for the guard written against it)
KILLS = [
    ("D02", "FullyQualifiedName~StrategyLabTests.Compute_PositiveMeanButWideSpread"),
    ("D05", "FullyQualifiedName~ConditionGroupFoldTests"),
    ("D06", "FullyQualifiedName~BacktestWarmupAnalyzerTests"),
    ("D08", "FullyQualifiedName~StrategyPositionManagementTests.A_fill_correction"),
    ("D09", "FullyQualifiedName~RollingQuantileTests"),
    ("D11", "FullyQualifiedName~PivotLevelsProviderTests"),
    ("D20", "FullyQualifiedName~RateLimiterRetryPolicyTests"),
]

SUMMARY_RE = re.compile(r'Failed:\s*(\d+),\s*Passed:\s*(\d+).*?Total:\s*(\d+)')


def run(cmd, timeout=1800):
    return subprocess.run(cmd, shell=True, cwd=REPO, capture_output=True, text=True, timeout=timeout)


def build():
    return run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
               "-p:UseRazorSourceGenerator=false -v:q --nologo").returncode == 0


def run_filter(f):
    r = run(f'dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj '
            f'-p:UseRazorSourceGenerator=false --no-build --nologo --filter "{f}"')
    m = SUMMARY_RE.search(r.stdout)
    if not m:
        return (-1, -1, 0)
    return int(m.group(1)), int(m.group(2)), int(m.group(3))


def recover():
    if not os.path.exists(INFLIGHT):
        return
    rel = open(INFLIGHT).read().strip()
    if rel:
        run(f"git checkout -- {rel!r}")
        os.utime(os.path.join(REPO, rel), None)
    os.remove(INFLIGHT)


def main():
    recover()
    out = []
    build()
    for mid, filt in KILLS:
        _, area, relpath, find, repl, _breaks = BY_ID[mid]
        path = os.path.join(REPO, relpath)
        original = open(path, encoding='utf-8-sig').read()
        t0 = time.time()
        rec = {'id': mid, 'area': area, 'filter': filt}

        # Clean: the guard must pass.
        f, p, t = run_filter(filt)
        rec['clean'] = {'failed': f, 'passed': p, 'total': t}

        try:
            open(INFLIGHT, 'w').write(relpath)
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original.replace(find, repl))
            if not build():
                rec['verdict'] = 'NO_COMPILE'
            else:
                f2, p2, t2 = run_filter(filt)
                rec['mutant'] = {'failed': f2, 'passed': p2, 'total': t2}
                if t == 0 or t2 == 0:
                    rec['verdict'] = 'INCONCLUSIVE_FILTER_MATCHED_NOTHING'
                elif rec['clean']['failed'] != 0:
                    rec['verdict'] = 'GUARD_FAILS_CLEAN'
                elif f2 > 0:
                    rec['verdict'] = 'PROVED_RED'
                else:
                    rec['verdict'] = 'GUARD_DID_NOT_FIRE'
        finally:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original)
            os.utime(path, None)
            if os.path.exists(INFLIGHT):
                os.remove(INFLIGHT)

        rec['seconds'] = round(time.time() - t0)
        print(f"{mid}: {rec['verdict']} — clean {rec['clean']}, "
              f"mutant {rec.get('mutant')} ({rec['seconds']}s)", flush=True)
        out.append(rec)
        json.dump(out, open(OUT, 'w'), indent=1)

    build()
    print("\n=== summary")
    for r in out:
        print(f"  {r['id']} {r['verdict']}")


if __name__ == '__main__':
    main()
