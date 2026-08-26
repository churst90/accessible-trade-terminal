#!/usr/bin/env python3
"""Run the full suite N times and record failing test NAMES per run.

A2's lesson: a single unrelated failing test is how a flake inverts a verdict, so
the names matter more than the count. Usage: a3_flakewatch.py <runs> <label>
"""
import subprocess, re, json, sys, os
REPO = "/home/cody/external-rescue/Github/accessible-trade-terminal"
OUT  = os.path.join(REPO, "scratchpad", "a3_flakewatch.json")
FAILED_RE  = re.compile(r'^\s*(?:\[xUnit[^\]]*\]\s*)?(?:Failed|\s+Failed)\s+(\S+)', re.M)
SUMMARY_RE = re.compile(r'Failed:\s*(\d+),\s*Passed:\s*(\d+)')

runs  = int(sys.argv[1]) if len(sys.argv) > 1 else 4
label = sys.argv[2] if len(sys.argv) > 2 else "run"
results = json.load(open(OUT)) if os.path.exists(OUT) else []

for n in range(runs):
    r = subprocess.run(
        "dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
        "-p:UseRazorSourceGenerator=false --no-build --nologo",
        shell=True, cwd=REPO, capture_output=True, text=True, timeout=3600)
    names = sorted(set(FAILED_RE.findall(r.stdout)))
    m = SUMMARY_RE.search(r.stdout)
    rec = {"label": label, "n": n, "rc": r.returncode,
           "failed": int(m.group(1)) if m else None,
           "passed": int(m.group(2)) if m else None,
           "names": names}
    results.append(rec)
    json.dump(results, open(OUT, "w"), indent=1)
    print(json.dumps(rec), flush=True)
