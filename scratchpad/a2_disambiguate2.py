#!/usr/bin/env python3
"""A2 — second disambiguation pass.

M20, M27 and M28 each came back CAUGHT by exactly one test:
LinuxBwrapSandboxTests.A_script_cannot_read_the_hosts_environment, the known
bwrap env-canary flake, which has nothing to do with a shortcut table or with
backtest commission. Re-apply each mutant and run the classes that WOULD catch
it if it were covered.
"""
import json, os, re, subprocess, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from a2_sabotage import MUTANTS, REPO, build

CASES = [
    ("M27", "Backtest"),
    ("M28", "Backtest"),
    ("M27", "LabRunnerTests"),
    ("M28", "StrategyPositionManagementTests"),
    ("M20", "Shortcut"),
]


def run_filter(f):
    r = subprocess.run(
        f'dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj '
        f'-p:UseRazorSourceGenerator=false --no-build --nologo '
        f'--filter "FullyQualifiedName~{f}"',
        shell=True, cwd=REPO, capture_output=True, text=True, timeout=1800)
    return r.stdout


by_id = {m[0]: m for m in MUTANTS}
out = []
cur = None
for mid, filt in CASES:
    _, area, rel, find, repl, breaks = by_id[mid]
    path = os.path.join(REPO, rel)
    original = open(path, encoding='utf-8-sig').read()
    try:
        open(path, 'w', encoding='utf-8').write(original.replace(find, repl))
        ok, _ = build()
        if not ok:
            print(f"{mid}/{filt}: build failed", flush=True)
            continue
        txt = run_filter(filt)
        m = re.search(r'Failed:\s*(\d+),\s*Passed:\s*(\d+)', txt)
        failed = int(m.group(1)) if m else -1
        passed = int(m.group(2)) if m else -1
        print(f"{mid} + filter '{filt}': failed={failed} passed={passed}", flush=True)
        out.append({'id': mid, 'filter': filt, 'failed': failed, 'passed': passed})
    finally:
        open(path, 'w', encoding='utf-8').write(original)
        os.utime(path, None)

json.dump(out, open(os.path.join(REPO, 'scratchpad', 'a2_disambiguate2.json'), 'w'), indent=1)
build()
