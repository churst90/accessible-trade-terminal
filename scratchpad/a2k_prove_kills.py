#!/usr/bin/env python3
"""A2k — prove every honest survivor is now killed by a named test.

Runs against the tree it sits in. For each survivor (19 raw + K35, W01, P02, whose only
'catches' were a modal Tab test that passes with the mutant applied: contention flakes),
applies the campaign's own mutant, runs every node suite, and requires a FAIL. A mutant whose
anchor no longer exists (reconnect.js was rewritten after an accessibility review) is reported
SUPERSEDED, not killed. Restores byte-identical and says so.
"""
import importlib.util, os, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.dirname(HERE)
os.environ.setdefault("A2K_SCRATCH", "/tmp/a2k-prove")
spec = importlib.util.spec_from_file_location("a2k", os.path.join(HERE, "a2k_js_sabotage.py"))
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
SURVIVORS = ["K18","K27","K30","K32","K35","K42","K44","K45","K46","K47","C01","C03",
             "R02","R03","A01","A02","A03","A04","W01","W02","P01","P02"]
SUITES = sorted(f for f in os.listdir(os.path.join(REPO, "tools/jstests")) if f.endswith(".mjs"))
by_id = {x[0]: x for x in m.M}
bad = 0
for mid in SURVIVORS:
    _, desc, f, old, new = by_id[mid]
    path = os.path.join(REPO, f); orig = open(path, encoding="utf-8").read()
    if orig.count(old) != 1:
        print(f"{mid} SUPERSEDED (anchor gone: the code it mutated was replaced)"); continue
    open(path, "w", encoding="utf-8").write(orig.replace(old, new))
    try:
        fails = []
        for s in SUITES:
            r = subprocess.run(["node", f"tools/jstests/{s}"], cwd=REPO, capture_output=True, text=True)
            fails += [f"{s}: {l.strip()}" for l in (r.stdout + r.stderr).splitlines() if l.strip().startswith("FAIL")]
    finally:
        open(path, "w", encoding="utf-8").write(orig)
    ok = open(path, encoding="utf-8").read() == orig
    print(f"{mid} {'KILLED' if fails else 'STILL SURVIVES'} {fails[:1]}{'' if ok else ' !! NOT RESTORED'}")
    bad += (not fails) + (not ok)
print("restored byte-identical" if not bad else f"{bad} problems")
