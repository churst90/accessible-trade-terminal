#!/usr/bin/env python3
"""Prove the browser harness fails when the bug it exists for is put back.

Same discipline as scratchpad/a2_sabotage.py: apply ONE change, rebuild, run, revert,
os.utime so MSBuild does not keep the sabotaged binary.
"""
import subprocess, os, sys, json

REPO = "/home/cody/external-rescue/Github/accessible-trade-terminal"
FILTER = 'FullyQualifiedName~ModalBrowserContractTests.Opening_a_dialog_puts_focus'

MUTANTS = [
    ("S1 focusElement retry reverted (the Alt+T fix)",
     "AccessibleTrader.BlazorClient.Components/wwwroot/js/keyboard.js",
     """        const attempt = function () {
            const el = document.getElementById(elementId);
            if (el) { el.focus(); return; }
            if (--framesLeft <= 0) return;""",
     """        const attempt = function () {
            const el = document.getElementById(elementId);
            if (el) { el.focus(); return; }
            if (true) return;"""),

    ("S2 TradingDashboardModal's own focus call deleted",
     "AccessibleTrader.BlazorClient.Components/TradingDashboardModal.razor",
     '        try { await JSRuntime.InvokeVoidAsync("accessibleTrader.focusElement", "trade-title"); } catch { }',
     '        // sabotage: focus call removed'),
]


def run(cmd, timeout=2400):
    return subprocess.run(cmd, shell=True, cwd=REPO, capture_output=True, text=True, timeout=timeout)


results = []
for name, rel, find, repl in MUTANTS:
    path = os.path.join(REPO, rel)
    orig = open(path, encoding="utf-8").read()
    if orig.count(find) != 1:
        results.append({"mutant": name, "status": f"ANCHOR NOT UNIQUE ({orig.count(find)})"})
        print(results[-1], flush=True)
        continue
    open(path, "w", encoding="utf-8").write(orig.replace(find, repl))
    os.utime(path, None)
    try:
        b = run("dotnet build AccessibleTrader.BrowserTests/AccessibleTrader.BrowserTests.csproj "
                "-p:UseRazorSourceGenerator=false -v:q --nologo")
        if b.returncode != 0:
            results.append({"mutant": name, "status": "BUILD FAILED", "out": b.stdout[-800:]})
        else:
            r = run(f'dotnet test AccessibleTrader.BrowserTests/AccessibleTrader.BrowserTests.csproj '
                    f'-p:UseRazorSourceGenerator=false --no-build --nologo --filter "{FILTER}"')
            tail = [l.strip() for l in r.stdout.splitlines() if "Failed!" in l or "Passed!" in l]
            failed = [l.strip() for l in r.stdout.splitlines() if "[FAIL]" in l]
            results.append({"mutant": name,
                            "caught": r.returncode != 0,
                            "summary": tail,
                            "failing": failed[:8]})
    finally:
        open(path, "w", encoding="utf-8").write(orig)
        os.utime(path, None)
    print(json.dumps(results[-1], indent=1), flush=True)

json.dump(results, open(os.path.join(REPO, "scratchpad", "a3_sabotage_results.json"), "w"), indent=1)
