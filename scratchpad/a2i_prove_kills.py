#!/usr/bin/env python3
"""A2i — prove every new guard RED by reintroducing the defect it was written for.

TWO OF THE NINE SURVIVORS ARE ABSENT, and each for a measured reason:

  I05 — RenderCandles' half-pixel alignment (Math.Floor(xRaw) + 0.5f). Removing it changes
        NOTHING observable: SKPaintPool.Rent() calls Reset() and SKPaint.IsAntialias defaults to
        false, so across twenty sub-pixel offsets the wick lands in the identical column every
        time. Equivalent while anti-aliasing is off. The line is kept and the PREMISE is pinned
        instead (TheWickAlignmentIsBeltAndBracesWhileAntiAliasingIsOff), so the day these paints
        become anti-aliased the alignment is flagged as newly load-bearing.

  I21 as originally specified — rewriting DrawRect(SKRect.Create(x,y,w,h)) as
        DrawRect(x,y,w,h). Measured byte-identical: the four-float overload IS (x,y,w,h). That
        mutant was MIS-SPECIFIED, not the defect absent. The historical bug was passing BOUNDS to
        the overload, and that is what I21-bounds below re-applies.

Harness rules as ever, plus A2h's: REBUILD before exiting, or a later `--no-build` run tests the
last mutant against a restored tree.
"""
import json, os, re, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2i_prove_kills_results.json")
SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+)")
NO_MATCH = "No test matches the given testcase filter"

R = "AccessibleTrader.Core/Services/Rendering/"
C = "AccessibleTrader.Core/Services/"
F = "ChartVisualStabilityTests"

KILLS = [
    ("I03", R + "StandardRenderers.cs",
     "                float bodyHeight = Math.Max(1, bottom - top);",
     "                float bodyHeight = bottom - top;", F),

    ("I04", R + "StandardRenderers.cs",
     "                    ctx.Canvas.DrawLine(x, yHigh, x, yLow, wickPaint);",
     "                    ctx.Canvas.DrawLine(x, yOpen, x, yClose, wickPaint);", F),

    ("I08", R + "StandardRenderers.cs",
     "                bool drawHollow = hollowUp && !hasPhaseOverride && bar.Close >= bar.Open;",
     "                bool drawHollow = hollowUp && !hasPhaseOverride && bar.Close < bar.Open;", F),

    ("I18", C + "ChartRenderer.cs",
     "                if (Math.Abs(y - lastLabelY) < minLabelSpacing) continue;",
     "                if (Math.Abs(y - lastLabelY) < 0) continue;", F),

    # The REAL defect: bounds passed to the (x, y, width, height) overload.
    ("I21-bounds", C + "ChartRenderer.cs",
     "                    canvas.DrawRect(SKRect.Create(axisRect.Left, y - (tickH / 2f), tickW, tickH), p);",
     "                    canvas.DrawRect(axisRect.Left, y - (tickH / 2f), axisRect.Left + tickW, y + (tickH / 2f), p);", F),

    ("I24", C + "ChartRenderer.cs",
     "                var mainPaneRect = new SKRect(0, currentY, width - _axisWidth, currentY + mainPaneHeight);",
     "                var mainPaneRect = new SKRect(0, currentY, width, currentY + mainPaneHeight);", F),

    ("I33", R + "StandardRenderers.cs",
     "                        bodyColor = GetPhaseColor((int)Math.Round(phaseData[absIdx]));",
     "                        bodyColor = bar.Close >= bar.Open ? bullish : bearish;", F),

    ("I34", R + "StandardRenderers.cs",
     "                if (!hasPhaseOverride)\n                {\n                    using var wickLease = SKPaintPool.Rent();",
     "                if (true)\n                {\n                    using var wickLease = SKPaintPool.Rent();", F),
]


def run(cmd, timeout=1800):
    p = subprocess.run(cmd, cwd=REPO, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def main():
    only = [a for a in sys.argv[1:] if a.startswith("I")] or None
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

    # A2h's lesson: restore the source AND the binary.
    run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
        "-p:UseRazorSourceGenerator=false -v q --nologo")

    print("\n=== summary")
    for r in results:
        print(f"  {r['id']:>11} {r['status']:>22}  {r['filter']}")
    red = sum(1 for r in results if r['status'] == 'RED')
    print(f"\n{red}/{len(results)} proved RED")
    code, diff = run("git diff --stat -- AccessibleTrader.Core/")
    print("restored byte-identical" if not diff.strip() else f"TREE NOT RESTORED:\n{diff}")


if __name__ == '__main__':
    main()
