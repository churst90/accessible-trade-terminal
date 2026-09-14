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
# H16 and H29 are EQUIVALENT MUTANTS and are deliberately absent, each established by measurement
# rather than assumed:
#
#   H16 — Cipher A's `sustainedOs` conjunct. Over 4 flavours x 3,000 bars the mutant produces the
#         IDENTICAL 466 signals: a cross UP means the wave turned up, so if it is still below the
#         oversold line while rising, the previous bar was lower still. The second conjunct is
#         implied by the crossover it is joined to.
#   H29 — Hurst on raw differences instead of log returns. Median exponent 0.571 against 0.575 over
#         1,100 values on a geometric walk spanning a 66x price range. Rescaled-range analysis
#         divides a range by a standard deviation computed on the SAME window, so it is
#         dimensionless and window-local: changing the units of the return series does not move it.
#
# Both keep a test (the sustained-state property, and the 0.5 calibration on a geometric walk)
# because each states a real contract and would catch a future change that decoupled them — they
# simply cannot be proved red by their own mutant.
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

    ("H03", A + "CipherBProvider.cs",
     "                        if (double.IsNaN(wt1[i - k]) || wt1[i - k] > osHere) { sustained = false; break; }",
     "                        if (double.IsNaN(wt1[i - k]) || wt1[i - k] < osHere) { sustained = false; break; }",
     "CipherBSignalRulesTests"),

    ("H17", A + "CipherSRProvider.cs",
     "                if (isPivotHigh) { resistance[i] = data[i].High; resConfirmed[i + pb] = data[i].High; }",
     "                if (isPivotHigh) { resistance[i] = data[i].High; resConfirmed[i] = data[i].High; }",
     "CipherSrZoneRulesTests"),

    ("H18", A + "CipherSRProvider.cs",
     "                if (!newRes && !double.IsNaN(lastRes) && data[i].Close > lastRes * (1.0 + breakPct))",
     "                if (!newRes && !double.IsNaN(lastRes) && data[i].Close > lastRes * (1.0 - breakPct))",
     "CipherSrZoneRulesTests"),

    ("H19", A + "CipherSRProvider.cs",
     "                    if (data[i].High <= data[i - k].High || data[i].High <= data[i + k].High)",
     "                    if (data[i].High < data[i - k].High || data[i].High < data[i + k].High)",
     "CipherSrZoneRulesTests"),

    ("H21", A + "TopBottomDetectorProvider.cs",
     "                        atTop    = (winHigh - data[i].High) / winRange <= 0.20;",
     "                        atTop    = (winHigh - data[i].High) / winRange >= 0.20;",
     "IndicatorMarkerAndCacheRulesTests"),

    ("H22", A + "TopBottomDetectorProvider.cs",
     "                        if (double.IsNaN(prevCap) || prevCap < confirm)",
     "                        if (true)",
     "IndicatorMarkerAndCacheRulesTests"),

    ("H23", A + "CipherSProvider.cs",
     "                if (_detectionCache.TryGetValue(key, out var cached) && n < (int)(cached.dataCount * 1.5))",
     "                if (_detectionCache.TryGetValue(key, out var cached) && n < cached.dataCount)",
     "IndicatorMarkerAndCacheRulesTests"),

    ("H24", A + "CipherSProvider.cs",
     "                Math.Abs(suggested - lastDetected) / (double)lastDetected > 0.15;",
     "                Math.Abs(suggested - lastDetected) / (double)lastDetected > 0.0;",
     "IndicatorMarkerAndCacheRulesTests"),

    ("H26", A + "IchimokuProvider.cs",
     "                if (crossUpPrior && stillUp) tkBull[i] = kijun[i];",
     "                if (crossUpPrior) tkBull[i] = kijun[i];",
     "IndicatorDefinitionRulesTests"),

    ("H27", A + "AnchoredVwapProvider.cs",
     "                        if (data[pIdx + k].High >= pH) isHigh = false;",
     "                        if (data[pIdx + k].High >= pH) { }",
     "IndicatorDefinitionRulesTests"),

    ("H28", A + "AnchoredVwapProvider.cs",
     "                double typical = (data[i].High + data[i].Low + data[i].Close) / 3.0;",
     "                double typical = (data[i].High + data[i].Low) / 2.0;",
     "IndicatorDefinitionRulesTests"),

    ("H30", A + "MACloudProvider.cs",
     "                if (was > 0 && now > was * 1.02) parts.Add(\"expanding\");",
     "                if (was > 0 && now > was) parts.Add(\"expanding\");",
     "IndicatorDefinitionRulesTests"),

    ("H31", A + "FearGreedProvider.cs",
     "                if (previousSide != 0 && side != 0 && side != previousSide)",
     "                if (side != 0 && side != previousSide)",
     "IndicatorDefinitionRulesTests"),

    ("H32", A + "FearGreedProvider.cs",
     "                if (v <= fearLevel)  fearSpan[i]  = v;\n                if (v >= greedLevel) greedSpan[i] = v;",
     "                if (v >= fearLevel)  fearSpan[i]  = v;\n                if (v <= greedLevel) greedSpan[i] = v;",
     "IndicatorDefinitionRulesTests"),

    ("H33", A + "SkenderDetailFactProvider.cs",
     "                            if (rsiUp && !priceUp)  divergence = \" Bullish divergence hint.\";",
     "                            if (rsiUp && !priceUp)  divergence = \" Bearish divergence hint.\";",
     "IndicatorDefinitionRulesTests"),

    ("H34", A + "MovingAverageHelper.cs",
     "                \"HMA\"  => Hma(source, period),",
     "                \"HMA\"  => Sma(source, period),",
     "IndicatorDefinitionRulesTests"),

    ("H35", A + "CrossSeriesCache.cs",
     "                // First tick is later than this bar — leave NaN.\n"
     "                if (ticks[tickIdx].Ts > barTs) continue;",
     "                // First tick is later than this bar — leave NaN.",
     "IndicatorDefinitionRulesTests"),

    ("H37", A + "LoukasCyclesProvider.cs",
     "                        if (data[j].Low <= kLow) { isLocalMin = false; break; }",
     "                        if (data[j].Low <  kLow) { isLocalMin = false; break; }",
     "IndicatorDefinitionRulesTests"),

    ("H36", A + "LoukasCyclesProvider.cs",
     "                                    if (q >= 0 && dclLows[q] <= dclLows[^1]) isIcl = false;",
     "                                    if (q >= 0 && dclLows[q] < dclLows[^1]) isIcl = false;",
     "IndicatorDefinitionRulesTests"),
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
