#!/usr/bin/env python3
"""A2j — prove every new guard RED by reintroducing the defect it was written for.

All NINE survivors are here; none was equivalent, and each was checked before a test was
written (the A2h/A2i rule). The three narration-gate survivors needed a fixture the existing
suite did not have, and in two cases the reason the old fixtures could not see the mutant is
itself recorded in the new test's doc comment:

  J16 — the seed floor is covered for any marker that had ALREADY PRINTED when narration was
        switched on, because the seed records a last-pivot index per marker as well. The gap is
        a component that was still NaN everywhere at seed time — a pivot indicator in warmup —
        which is what the new fixture builds.
  J18 — the first-sighting rule is likewise seeded for any overlay that HAS a value at seed
        time; the seed skips a NaN one deliberately. So the reachable case is an EMA still
        warming up, which is every EMA for its first N bars.
  J13 — the two existing phase tests use 5.0 and 42.0, both whole, so round and cast agree.

Filters name ONE test class each so a red result names the guard that caught it.
Harness rules as ever, plus A2h's: REBUILD before exiting.
"""
import json, os, re, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2j_prove_kills_results.json")
SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+)")
NO_MATCH = "No test matches the given testcase filter"

A = "AccessibleTrader.Core/Services/Accessibility/"

KILLS = [
    ("J01", A + "SpeechFormatter.cs",
     "            bool readsRawBar = seriesId == CoreSeriesIds.Price;",
     "            bool readsRawBar = false;", "HeikinAshiSpeechTests"),

    ("J13", A + "SpeechFormatter.cs",
     "            int phaseIdx = Math.Clamp((int)Math.Round(ctx.Value), 0, AudioConstants.PhaseNames.Length - 1);",
     "            int phaseIdx = Math.Clamp((int)ctx.Value, 0, AudioConstants.PhaseNames.Length - 1);",
     "SpeechFormatterDispatchTests"),

    ("J16", A + "NarrationScanner.cs",
     "                int scanFrom = Math.Max(seedCount, closedBound - PivotConfirmWindow);",
     "                int scanFrom = Math.Min(seedCount, closedBound - PivotConfirmWindow);",
     "NarrationScanWindowTests"),

    ("J17", A + "NarrationScanner.cs",
     "            if (!isBarClose) return;",
     "            if (false) return;", "NarrationScanWindowTests"),

    ("J18", A + "NarrationScanner.cs",
     "                if (_lastPriceAboveOverlay.TryGetValue(key, out bool wasAbove) && wasAbove != nowAbove)",
     "                if (!_lastPriceAboveOverlay.TryGetValue(key, out bool wasAbove) || wasAbove != nowAbove)",
     "NarrationScanWindowTests"),

    ("J35", A + "DrawingSpeech.cs",
     "            bool at = SpeechPriceFormatter.FormatPrice(close) == SpeechPriceFormatter.FormatPrice(drawingValue);",
     "            bool at = close == drawingValue;", "DrawingSpeechContractTests"),

    ("J39", A + "BinnedNavigationStrategy.cs",
     "            int newBin = Math.Clamp(currentBin - delta, 0, binCount - 1);",
     "            int newBin = Math.Clamp(currentBin + delta, 0, binCount - 1);",
     "BinnedNavigationStrategyTests"),

    ("J40", A + "BinnedNavigationStrategy.cs",
     "                if (isProfile && !isHeatmap)\n                    return new NavigationResult(false);",
     "                if (isProfile && isHeatmap)\n                    return new NavigationResult(false);",
     "BinnedNavigationStrategyTests"),

    ("J48", A + "BarDetailService.cs",
     "            if (pct < -0.10) return \"band squeezing, low volatility\";\n            if (pct >  0.10) return \"band expanding, volatility rising\";",
     "            if (pct > -0.10) return \"band squeezing, low volatility\";\n            if (pct <  0.10) return \"band expanding, volatility rising\";",
     "BarDetailContextTests"),
]


def run(cmd, timeout=1800):
    p = subprocess.run(cmd, cwd=REPO, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def main():
    only = [a for a in sys.argv[1:] if a.startswith("J")] or None
    results = []

    for kid, relpath, find, repl, filt in KILLS:
        if only and kid not in only:
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
                    print(f"{kid}: FILTER MATCHED NOTHING — a FAILURE", flush=True)
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
        results.append(rec)
        json.dump(results, open(OUT, 'w'), indent=1)

    run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
        "-p:UseRazorSourceGenerator=false -v q --nologo")

    print("\n=== summary")
    for r in results:
        print(f"  {r['id']:>5} {r['status']:>22}  {r['filter']}")
    red = sum(1 for r in results if r['status'] == 'RED')
    print(f"\n{red}/{len(results)} proved RED")
    code, diff = run("git diff --stat -- AccessibleTrader.Core/")
    print("restored byte-identical" if not diff.strip() else f"TREE NOT RESTORED:\n{diff}")


if __name__ == '__main__':
    main()
