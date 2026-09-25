#!/usr/bin/env python3
"""A2n — prove every new guard RED on the mutant it was written for, GREEN on control.

Survivors of the campaign, plus the two catches the false-catch audit threw out:
  S04 — "caught" only by Blazor.OrderTicketErrorStateTests.Pressing_the_refused_Submit..., which
        passed on rerun with the mutant applied (a flake; it also appeared beside T19's real catch).
  S21 — "caught" only by COLLATERAL: leaking every slot exhausts the 16-worker cap part-way through
        the suite and whichever real-worker tests run after that fail. None of the six re-failed in
        isolation (fewer than 16 workers). No test named the rule, and the catch depended on how
        many workers the rest of the suite happens to start.

Anchors are the campaign's own (imported), except S28: the Mac launcher gained a seam in this
pass, so its refusal decision is now `_allowUnsandboxed ?? SandboxPolicy.AllowUnsandboxedFallback`
and the mutant replaces that expression with `true` — the same "never refuse" mutant.

Filters name ONE test class each so a red result names the guard that caught it.
Harness rules as ever: unique anchor, restore from a file copy, touch, REBUILD before exiting,
"No test matches" is a failure, print the failing names.
"""
import json, os, re, shutil, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import a2n_sabotage as H

OUT = os.path.join(H.REPO, "scratchpad", "a2n_prove_kills_results.json")
NO_MATCH = "No test matches the given testcase filter"
BACKUP = os.path.join(H.REPO, "scratchpad", "a2n_prove_backup")

M = {m[0]: m for m in H.MUTANTS}
M["S28"] = ("S28", "scripting", H.S + "MacSandboxExecLauncher.cs",
            "                _allowUnsandboxed ?? SandboxPolicy.AllowUnsandboxedFallback,\n                details: missing,",
            "                true,\n                details: missing,",
            "macOS: a missing sandbox-exec/profile silently downgrades")

KILLS = [
    ("T03", "QuickTradeExecutorOrderTests"),
    ("T08", "QuickTradeExecutorOrderTests"),
    ("T09", "QuickTradeExecutorOrderTests"),
    ("T10", "QuickTradeExecutorOrderTests"),
    ("T11", "QuickTradeExecutorOrderTests"),
    ("T15", "QuickTradeTests"),
    ("T18", "QuickTradeTests"),
    ("T20", "QuickTradeEquityFetchTests"),
    ("T36", "StrategyPositionManagementTests"),
    ("S03", "SandboxRefusalPolicyTests"),
    ("S04", "SandboxRefusalPolicyTests"),
    ("S14", "ScriptHostSupervisionTests"),
    ("S15", "ScriptHostSupervisionTests"),
    ("S16", "ScriptHostSupervisionTests"),
    ("S18", "ScriptHostSupervisionTests"),
    ("S19", "ScriptHostSupervisionTests"),
    ("S20", "ScriptHostSupervisionTests"),
    ("S21", "ScriptHostSupervisionTests"),
    ("S26", "SandboxRefusalPolicyTests"),
    ("S27", "SandboxRefusalPolicyTests"),
    ("S28", "SandboxRefusalPolicyTests"),
]


def bpath(rel):
    return os.path.join(BACKUP, rel.replace("/", "__"))


def run_filter(filt):
    code, out = H.run("nice -n 10 dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                      f"-p:UseRazorSourceGenerator=false --no-build --filter FullyQualifiedName~{filt}",
                      timeout=1800)
    if NO_MATCH in out:
        return 'FILTER_MATCHED_NOTHING', -1, -1, []
    m = H.SUMMARY_RE.search(out)
    f = int(m.group(1)) if m else -1
    p = int(m.group(2)) if m else -1
    names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
    return ('UNPARSED' if f < 0 else 'RED' if f > 0 else 'STILL_GREEN'), f, p, names


def main():
    only = [a for a in sys.argv[1:] if re.match(r"^[TS]\d\d$", a)] or None
    os.makedirs(BACKUP, exist_ok=True)
    files = sorted({M[k][2] for k, _ in KILLS})
    for rel in files:
        shutil.copyfile(os.path.join(H.REPO, rel), bpath(rel))

    ok = True
    for kid, _ in KILLS:
        _, _, rel, find, repl, _ = M[kid]
        n = open(os.path.join(H.REPO, rel), 'rb').read().decode('utf-8').count(find)
        if n != 1:
            print(f"{kid}: BAD ANCHOR ({n}) in {rel}"); ok = False
    if not ok:
        sys.exit(1)

    results = []
    for kid, filt in KILLS:
        if only and kid not in only:
            continue
        _, _, rel, find, repl, breaks = M[kid]
        path = os.path.join(H.REPO, rel)
        original = open(path, 'rb').read().decode('utf-8')
        rec = {'id': kid, 'filter': filt, 'breaks': breaks}
        t0 = time.time()
        try:
            open(path, 'wb').write(original.replace(find, repl).encode('utf-8'))
            built, log = H.build()
            if not built:
                rec['status'] = 'NO_COMPILE'
                print(f"{kid}: DID NOT COMPILE\n{log[-600:]}", flush=True)
            else:
                rec['status'], rec['failed'], rec['passed'], rec['failing_tests'] = run_filter(filt)
                print(f"{kid}: {rec['status']} failed={rec['failed']} passed={rec['passed']} "
                      f"({time.time()-t0:.0f}s)", flush=True)
                for t in rec['failing_tests'][:6]:
                    print(f"      {t}", flush=True)
        finally:
            shutil.copyfile(bpath(rel), path)
            os.utime(path, None)
        results.append(rec)
        json.dump(results, open(OUT, 'w'), indent=1)

    # CONTROL: nothing sabotaged, every filter green.
    built, _ = H.build()
    assert built, "control build failed"
    control = {}
    for filt in sorted({f for _, f in KILLS}):
        st, f, p, names = run_filter(filt)
        control[filt] = {'status': st, 'failed': f, 'passed': p, 'failing': names}
        print(f"CONTROL {filt}: {'GREEN' if st == 'STILL_GREEN' else st} passed={p} {names}", flush=True)
    json.dump({'kills': results, 'control': control}, open(OUT, 'w'), indent=1)

    print("\n=== summary")
    for r in results:
        print(f"  {r['id']:>4} {r['status']:>22}  {r['filter']}")
    red = sum(1 for r in results if r['status'] == 'RED')
    print(f"\n{red}/{len(results)} proved RED")
    same = all(open(os.path.join(H.REPO, rel), 'rb').read() == open(bpath(rel), 'rb').read() for rel in files)
    print("restored byte-identical" if same else "TREE NOT RESTORED")


if __name__ == '__main__':
    main()
