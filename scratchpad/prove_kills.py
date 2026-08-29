#!/usr/bin/env python3
"""Prove each new guard actually fails under the mutant it was written for.

A guard test that has never been seen red is not a guard — this repo's standing
rule, and the reason every kill in the 2026-08-28 survivor pass was demonstrated
rather than asserted. For each entry: apply the mutant, build, run ONLY the named
tests, and require that they FAIL. A run whose filter matched zero tests is a
FAILURE, not a pass — a non-matching filter is indistinguishable from green and
has fooled this repo before.

RE-ANCHORING. Three of the nine kills needed the guard extracted to a named seam
before it could be called at all (the loops that held them need a whole DI stack
to reach). Where that happened the mutant is applied to the line that INHERITED
the job, which is the same regression at the same site; the substitution is
recorded in the entry's `note` rather than left implicit.
"""
import json, os, re, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "prove_kills_results.json")
INFLIGHT = os.path.join(REPO, "scratchpad", "prove_kills_inflight.txt")

# (id, file, find, replace, test-filter, note)
KILLS = [
    ("N23", "AccessibleTrader.WebHost/Services/HostedAlertMonitor.cs",
     "            if (n < FeedFailuresBeforeReporting) return;",
     "            if (n < int.MaxValue) return;",
     "FullyQualifiedName~HostedAlertMonitorTests",
     "original anchor, unchanged"),

    ("N24", "AccessibleTrader.WebHost/Services/HostedAlertMonitor.cs",
     "        internal static bool HasComparableBars(IReadOnlyList<Ohlcv>? bars) => bars is { Count: >= 2 };",
     "        internal static bool HasComparableBars(IReadOnlyList<Ohlcv>? bars) => bars is { Count: >= 0 };",
     "FullyQualifiedName~HostedAlertMonitorTests",
     "re-anchored: `if (bars.Count < 2) continue;` was extracted to HasComparableBars"),

    ("N06", "AccessibleTrader.Core/Services/Analysis/SwingStructureAnalyzer.cs",
     "                    if (bars[j].High >= bars[i].High) isHigh = false;",
     "                    if (bars[j].High > bars[i].High) isHigh = false;",
     "FullyQualifiedName~SwingStructureTests",
     "original anchor, unchanged"),

    ("N07", "AccessibleTrader.Core/Services/Analysis/SwingStructureAnalyzer.cs",
     "                if (Math.Abs(p.Price - last.Price) < a * opts.MinSwingAtr) continue;",
     "                if (Math.Abs(p.Price - last.Price) < 0) continue;",
     "FullyQualifiedName~SwingStructureTests",
     "original anchor, unchanged"),

    ("N08", "AccessibleTrader.Core/Services/Analysis/ChartPatternDetector.cs",
     "                int width = right.BarIndex - left.BarIndex;\n"
     "                if (width < o.MinPatternBars || width > o.MaxPatternBars) continue;",
     "                int width = right.BarIndex - left.BarIndex;\n"
     "                if (width < o.MinPatternBars) continue;",
     "FullyQualifiedName~ChartPatternDetectorTests",
     "original anchor, unchanged"),

    ("N09", "AccessibleTrader.Core/Services/Analysis/ChartPatternDetector.cs",
     "                var a = highs[i - 1];\n"
     "                var b = highs[i];\n"
     "                int width = b.BarIndex - a.BarIndex;\n"
     "                if (width < o.MinPatternBars || width > o.MaxPatternBars) continue;\n"
     "\n"
     "                double tol = atr[b.BarIndex] * o.ToleranceAtr;\n"
     "                if (tol <= 0 || Math.Abs(a.Price - b.Price) > tol) continue;",
     "                var a = highs[i - 1];\n"
     "                var b = highs[i];\n"
     "                int width = b.BarIndex - a.BarIndex;\n"
     "                if (width < o.MinPatternBars || width > o.MaxPatternBars) continue;\n"
     "\n"
     "                double tol = atr[b.BarIndex] * o.ToleranceAtr;\n"
     "                if (tol <= 0) continue;",
     "FullyQualifiedName~ChartPatternDetectorTests",
     "original anchor, re-scoped to the double-TOP arm by its trailing line"),

    ("N26", "AccessibleTrader.Core/Services/MyData/CsvDataParser.cs",
     "            if (lines.Count - 1 > MaxRows)",
     "            if (lines.Count - 1 > int.MaxValue)",
     "FullyQualifiedName~MyDataTests",
     "original anchor, unchanged"),

    ("N13", "AccessibleTrader.Core/Services/Workspace/Reducers/TabReducer.cs",
     "            if (tabCount <= 1) return state; // Can't close the last tab",
     "            if (tabCount <= 0) return state; // Can't close the last tab",
     "FullyQualifiedName~WorkspaceStoreTests",
     "original anchor, unchanged"),

    ("N28", "AccessibleTrader.Sdk/Screening/ScreenerSpec.cs",
     "            foreach (var r in Rows) if (r is { Status: ScreenerRowStatus.Evaluated, Matched: true }) n++;",
     "            foreach (var r in Rows) if (r.Matched) n++;",
     "FullyQualifiedName~ScreenerServiceTests",
     "original anchor, unchanged"),
]

SUMMARY_RE = re.compile(r'Failed:\s*(\d+),\s*Passed:\s*(\d+).*?Total:\s*(\d+)')


def run(cmd, timeout=1800):
    return subprocess.run(cmd, shell=True, cwd=REPO, capture_output=True, text=True, timeout=timeout)


def build():
    r = run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
            "-p:UseRazorSourceGenerator=false -v:q --nologo")
    return r.returncode == 0, (r.stdout + r.stderr)[-1200:]


def tests(flt):
    r = run(f'dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj '
            f'-p:UseRazorSourceGenerator=false --no-build --nologo --filter "{flt}"')
    m = SUMMARY_RE.search(r.stdout)
    if not m:
        return (-1, -1, 0, r.stdout[-800:])
    names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', r.stdout, re.M)))
    return int(m.group(1)), int(m.group(2)), int(m.group(3)), names


def recover():
    if not os.path.exists(INFLIGHT):
        return
    rel = open(INFLIGHT).read().strip()
    if rel:
        print(f"!! recovering {rel}", flush=True)
        run(f"git checkout -- {rel!r}")
        os.utime(os.path.join(REPO, rel), None)
    os.remove(INFLIGHT)


def main():
    recover()
    only = [a for a in sys.argv[1:] if a.startswith("N")] or None
    out = json.load(open(OUT)) if os.path.exists(OUT) else []
    done = {r['id'] for r in out}

    for mid, relpath, find, repl, flt, note in KILLS:
        if (only and mid not in only) or mid in done:
            continue
        path = os.path.join(REPO, relpath)
        original = open(path, encoding='utf-8-sig').read()
        n = original.count(find)
        rec = {'id': mid, 'file': relpath, 'filter': flt, 'note': note, 'occurrences': n}
        if n != 1:
            rec['verdict'] = 'BAD_ANCHOR'
            print(f"{mid}: BAD ANCHOR ({n} occurrences)", flush=True)
            out.append(rec)
            json.dump(out, open(OUT, 'w'), indent=1)
            continue
        t0 = time.time()
        try:
            open(INFLIGHT, 'w').write(relpath)
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original.replace(find, repl))
            ok, log = build()
            if not ok:
                rec['verdict'] = 'NO_COMPILE'
                rec['log'] = log
            else:
                f, p, t, names = tests(flt)
                rec.update({'failed': f, 'passed': p, 'total': t,
                            'failing_tests': names if isinstance(names, list) else []})
                if t == 0:
                    rec['verdict'] = 'FILTER_MATCHED_NOTHING'
                elif f > 0:
                    rec['verdict'] = 'KILLED'
                else:
                    rec['verdict'] = 'MUTANT_STILL_SURVIVES'
        finally:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original)
            os.utime(path, None)
            if os.path.exists(INFLIGHT):
                os.remove(INFLIGHT)
        rec['seconds'] = round(time.time() - t0)
        print(f"{mid}: {rec['verdict']} failed={rec.get('failed')} of {rec.get('total')} "
              f"({rec['seconds']}s)", flush=True)
        if rec.get('failing_tests'):
            print("      " + "; ".join(rec['failing_tests'][:5]), flush=True)
        out.append(rec)
        json.dump(out, open(OUT, 'w'), indent=1)

    build()
    print("\n=== proof summary")
    for r in out:
        print(f"  {r['id']} {r['verdict']}")


if __name__ == '__main__':
    main()
