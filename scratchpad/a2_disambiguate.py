#!/usr/bin/env python3
"""A2 — separate a real catch from a flake that happened to fire.

M07 and M15 were each reported CAUGHT by exactly one test:
StrategyCausalityGateTests.CompileStrategyAsync_loads_a_causal_script, which is
already on record as a full-suite-only flake. Re-apply each mutant and run that
class ALONE — in isolation it passes reliably, so a red here is a real catch and
a green here means the mutant actually survived and the flake masked it.
"""
import json, os, re, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from a2_sabotage import MUTANTS, REPO, build

CASES = [
    ("M07", "StrategyCausalityGateTests"),
    ("M15", "StrategyCausalityGateTests"),
    ("M20", "LinuxBwrapSandboxTests"),
    ("M09", "ChartAreaBarSliderTests"),
    ("M23", "SettingsModalTests"),
]


def run_filter(cls):
    r = subprocess.run(
        f'dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj '
        f'-p:UseRazorSourceGenerator=false --no-build --nologo '
        f'--filter "FullyQualifiedName~{cls}"',
        shell=True, cwd=REPO, capture_output=True, text=True, timeout=1800)
    return r.stdout


by_id = {m[0]: m for m in MUTANTS}
out = []
for mid, cls in CASES:
    _, area, rel, find, repl, breaks = by_id[mid]
    path = os.path.join(REPO, rel)
    original = open(path, encoding='utf-8-sig').read()
    try:
        open(path, 'w', encoding='utf-8').write(original.replace(find, repl))
        ok, log = build()
        if not ok:
            print(f"{mid}: build failed"); continue
        txt = run_filter(cls)
        m = re.search(r'Failed:\s*(\d+),\s*Passed:\s*(\d+)', txt)
        failed = int(m.group(1)) if m else -1
        passed = int(m.group(2)) if m else -1
        names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', txt, re.M)))
        verdict = 'REAL CATCH' if failed > 0 else 'FLAKE — mutant actually SURVIVED'
        print(f"{mid} isolated {cls}: failed={failed} passed={passed} -> {verdict}")
        for n in names:
            print("     ", n)
        out.append({'id': mid, 'class': cls, 'failed': failed, 'passed': passed,
                    'verdict': verdict, 'names': names})
    finally:
        open(path, 'w', encoding='utf-8').write(original)
        os.utime(path, None)

json.dump(out, open(os.path.join(REPO, 'scratchpad', 'a2_disambiguate.json'), 'w'), indent=1)
build()
