#!/usr/bin/env python3
"""A2h — prove every new guard RED by reintroducing the exact mutant it was written for.

A test written against a defect you have already fixed is a claim until you have watched it
fail. Each entry re-applies one A2h survivor and runs ONLY the tests that should now catch it;
the run must FAIL, and the failing names are printed so a green-by-accident or a
red-for-the-wrong-reason is visible rather than inferred.

Harness rules (see the sabotage-harness-rules note): touch after restoring; a non-matching
filter is a FAILURE not a pass; assert the anchor is unique; restore from a file copy, never
`git checkout --`; print the failing NAMES.
"""
import json, os, re, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2h_prove_kills_results.json")
SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+)")
NO_MATCH = "No test matches the given testcase filter"

A = "AccessibleTrader.Core/Services/Indicators/"

# (id, file, find, replace, test filter)
KILLS = [
    ("H01", A + "CipherBProvider.cs",
     "                    || !(ancPol[i] < 0 && wt1Anc[i] < -anchorSuppressDepth);",
     "                    || !(ancPol[i] > 0 && wt1Anc[i] > anchorSuppressDepth);",
     "CipherBSignalRulesTests"),

    ("H02", A + "CipherBProvider.cs",
     "                        if (goldCd == 0 && conditionsMet >= goldMinConfluence)",
     "                        if (goldCd == 0 && conditionsMet >= 1)",
     "CipherBSignalRulesTests"),

    ("H04", A + "CipherBProvider.cs",
     "                bool deepEnough = wt1[prev] < -divergenceDepth && wt1[curr] < -divergenceDepth;",
     "                bool deepEnough = wt1[prev] < -divergenceDepth || wt1[curr] < -divergenceDepth;",
     "CipherBSignalRulesTests"),

    ("H05", A + "CipherBProvider.cs",
     "                bullDiv = ShiftMarkersForwardExcept(bullDiv, shallowBull, pivotBars, n);",
     "                bullDiv = ShiftMarkersForward(bullDiv, pivotBars, n);",
     "CipherBSignalRulesTests"),

    ("H06", A + "CipherBProvider.cs",
     "                    if (wt1[j] < wt1[i]) isLow  = false;",
     "                    if (wt1[j] < wt1[i] * 2) isLow  = false;",
     "CipherBSignalRulesTests"),

    ("H07", A + "CipherBProvider.cs",
     "                if (scaled >  MfClamp) scaled =  MfClamp;",
     "                if (scaled >  MfClamp) scaled =  MfClamp * 10;",
     "CipherBSignalRulesTests"),

    ("H08", A + "CipherBProvider.cs",
     "                    adaptiveOs[i] = double.IsNaN(osRaw[i]) ? -obLevel : Math.Min(osRaw[i], -minFloor);",
     "                    adaptiveOs[i] = double.IsNaN(osRaw[i]) ? -obLevel : Math.Max(osRaw[i], -minFloor);",
     "CipherBSignalRulesTests"),

    ("H10", A + "PulseProvider.cs",
     "            if (use < 0 || !buckets[use].Complete) return -1;",
     "            if (use < 0) return -1;",
     "PulseSignalRulesTests"),

    ("H11", A + "PulseProvider.cs",
     "        private static bool AdxPassWithin(int i, int lookback, ReadOnlySpan<double> adx, double minVal)\n"
     "        {\n"
     "            int start = Math.Max(0, i - lookback);",
     "        private static bool AdxPassWithin(int i, int lookback, ReadOnlySpan<double> adx, double minVal)\n"
     "        {\n"
     "            int start = 0;",
     "PulseSignalRulesTests"),

    ("H12", A + "PulseProvider.cs",
     "                if (bullCross && fastSlopeUp && (i - lastBullV2) >= holdDownBars)",
     "                if (bullCross && fastSlopeUp && (i - lastBullV2) >= 0)",
     "PulseSignalRulesTests"),

    ("H13", A + "PulseProvider.cs",
     "                    bool regimeOk = !double.IsNaN(reg[i]) && reg[i] >= 0.5;            // Regime == +1",
     "                    bool regimeOk = !double.IsNaN(reg[i]) && reg[i] >= -0.5;            // Regime == +1",
     "PulseSignalRulesTests"),

    ("H14", A + "PulseProvider.cs",
     "                avgGain = (avgGain * (rsiPeriod - 1) + g) / rsiPeriod;",
     "                avgGain = (avgGain * rsiPeriod + g) / rsiPeriod;",
     "PulseSignalRulesTests"),

    ("H15", A + "PulseProvider.cs",
     "                bool bullCross = prev <  midline && cur >= midline;",
     "                bool bullCross = prev <= midline && cur >= midline;",
     "PulseSignalRulesTests"),
]


def run(cmd, timeout=1800):
    p = subprocess.run(cmd, cwd=REPO, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def main():
    only = [a for a in sys.argv[1:] if a.startswith("H")] or None
    results = json.load(open(OUT)) if os.path.exists(OUT) else []
    done = {r['id'] for r in results}

    for kid, relpath, find, repl, filt in KILLS:
        if (only and kid not in only) or (not only and kid in done):
            continue
        path = os.path.join(REPO, relpath)
        original = open(path, encoding='utf-8-sig').read()
        n = original.count(find)
        rec = {'id': kid, 'file': relpath, 'filter': filt, 'occurrences': n}
        if n != 1:
            rec['status'] = 'BAD_ANCHOR'
            results.append(rec)
            print(f"{kid}: BAD ANCHOR ({n}) — {relpath}", flush=True)
            continue
        t0 = time.time()
        try:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original.replace(find, repl))
            code, log = run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                            "-p:UseRazorSourceGenerator=false -v q --nologo")
            if code != 0:
                rec['status'] = 'NO_COMPILE'
                print(f"{kid}: DID NOT COMPILE\n{log[-600:]}", flush=True)
            else:
                _, out = run("dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                             f"-p:UseRazorSourceGenerator=false --no-build --filter FullyQualifiedName~{filt}")
                if NO_MATCH in out:
                    rec['status'] = 'FILTER_MATCHED_NOTHING'
                    print(f"{kid}: FILTER MATCHED NOTHING ({filt}) — this is a FAILURE", flush=True)
                else:
                    m = SUMMARY_RE.search(out)
                    rec['failed'] = int(m.group(1)) if m else -1
                    rec['passed'] = int(m.group(2)) if m else -1
                    names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
                    rec['failing_tests'] = names[:20]
                    rec['status'] = ('UNPARSED' if rec['failed'] < 0
                                     else 'RED' if rec['failed'] > 0 else 'STILL_GREEN')
                    print(f"{kid}: {rec['status']} failed={rec['failed']} passed={rec['passed']} "
                          f"({time.time()-t0:.0f}s)", flush=True)
                    for t in names[:6]:
                        print(f"      {t}", flush=True)
        finally:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original)
            os.utime(path, None)
        results = [r for r in results if r['id'] != kid] + [rec]
        json.dump(results, open(OUT, 'w'), indent=1)

    # Rule 1, the other half: the loop restores the SOURCE but the binary on disk is still the
    # last mutant's. A later `dotnet test --no-build` would then run against sabotaged code and
    # report a failure in the restored tree. Rebuild before leaving.
    run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
        "-p:UseRazorSourceGenerator=false -v q --nologo")

    print("\n=== summary")
    for r in sorted(results, key=lambda x: x['id']):
        print(f"  {r['id']} {r['status']:>22}  {r['filter']}")
    red = sum(1 for r in results if r['status'] == 'RED')
    print(f"\n{red}/{len(results)} proved RED")
    code, diff = run("git diff --stat -- AccessibleTrader.Core/Services/Indicators/")
    print("restored byte-identical" if not diff.strip() else f"TREE NOT RESTORED:\n{diff}")


if __name__ == '__main__':
    main()
