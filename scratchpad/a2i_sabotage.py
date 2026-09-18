#!/usr/bin/env python3
"""A2i — the EIGHTH mutant set, aimed at what is actually on the screen.

WHY THIS AREA, AND WHY NOW.

`Core/Services/Rendering` plus `ChartRenderer.cs` is 3,769 lines and decides every
pixel of the chart. Across eight prior campaigns and the browser audit, mutants
have reached exactly TWO rendering-adjacent files — `ChartHitTester.cs` and
`ThemeCssBridge.cs` — and neither is a renderer. `StandardRenderers.cs` (1,369)
and `ChartRenderer.cs` (1,155), the two files that draw, have had ZERO.

Cody has a presentation within the week and asked for visual stability to be
assured first. That changes the selection rule for this set: the mutants below
are chosen for what a room full of people looking at a projected chart would
NOTICE — geometry that drifts, an axis whose labels float off its gridlines, a
pane drawn under the x-axis strip, a colour that ignores the theme, a marker that
swells to cover the bar it annotates — rather than for even coverage of the file.

`ChartFrameRenderingTests` (22 methods, written 2026-09-12) is the one file that
asserts colours and positions rather than "drew something". It has never been
measured. A2g found two guards there that were true by construction and A2h found
five fixture shapes that made a test vacuous, so "a test exists" is not the
question — "would it go red" is.

SAMPLING FRAME: no file below appears in a2, a2b, fresh(a2c), a2d, a2e, a2f, a2g,
a2h or a3.

METHOD — identical to a2d..a2h so the numbers compare: apply one mutant, build,
run the FULL AccessibleTrader.Tests suite, record whether anything went red and
WHICH tests did, revert, touch. CAUGHT iff some test fails.

HARNESS RULES (each learned from a run that lied — see sabotage-harness-rules):
  1. `touch` after restoring, or MSBuild keeps the sabotaged binary. The prove
     script must also REBUILD before it exits, or a later `--no-build` run tests
     the last mutant (learned in A2h).
  2. "No test matches the given testcase filter" is a FAILURE, not a pass.
  3. Assert the anchor is UNIQUE before patching.
  4. Restore from a file copy, never `git checkout --`; commit before starting.
  5. DO NOT TOUCH THE REPO — SOURCE OR DOCS — WHILE A CAMPAIGN IS RUNNING.
  6. NEVER add `-v q --nologo` to `dotnet test` — it removes the false-catch audit.
  7. Print the failing NAMES, so a uniform result looks as wrong as it is.
  8. AUDIT THE CATCHES FOR BOOKKEEPING GUARDS (new in A2h): a mutant caught only
     by a pinned-list/exemption-hygiene test is NOT honestly caught, because the
     natural repair is to edit the list.

Run DETACHED with setsid; this takes ~2 hours.
"""
import json, os, re, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2i_sabotage_results.json")
INFLIGHT = os.path.join(REPO, "scratchpad", "a2i_sabotage_inflight.txt")
SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+)")

R = "AccessibleTrader.Core/Services/Rendering/"
C = "AccessibleTrader.Core/Services/"

MUTANTS = [

    # ── Candle geometry: where a bar is and how wide it is ───────────────────
    ("I01", "every candle sits half a bar left of where it belongs",
     R + "StandardRenderers.cs",
     "                float xRaw = (i * barWidth) + halfBar;",
     "                float xRaw = i * barWidth;",
     "the bar centre loses its half-bar offset, so the whole series shifts left by half a slot "
     "against the axis, the crosshair, the cluster pan and every marker that computes its own x"),

    ("I02", "candle bodies touch, so the chart reads as a solid block",
     R + "StandardRenderers.cs",
     "                float bodyHalfWidth = (float)Math.Floor(barWidth * 0.4f);",
     "                float bodyHalfWidth = (float)Math.Floor(barWidth * 0.5f);",
     "the 0.4 factor is what leaves a gap between adjacent bodies; at 0.5 they abut and a dense "
     "chart projected on a wall stops resolving into individual candles"),

    ("I03", "a doji disappears",
     R + "StandardRenderers.cs",
     "                float bodyHeight = Math.Max(1, bottom - top);",
     "                float bodyHeight = bottom - top;",
     "a bar whose open equals its close has zero body height and vanishes entirely — the minimum "
     "of one pixel is the only thing that draws it"),

    ("I04", "the wick is drawn from the open and close rather than the high and low",
     R + "StandardRenderers.cs",
     "                    ctx.Canvas.DrawLine(x, yHigh, x, yLow, wickPaint);",
     "                    ctx.Canvas.DrawLine(x, yOpen, x, yClose, wickPaint);",
     "the wick stops showing the bar's range at all, so the chart understates every excursion — "
     "and the sonification, which reads High and Low directly, disagrees with the picture"),

    ("I05", "the pixel alignment that keeps wick and body concentric is dropped",
     R + "StandardRenderers.cs",
     "                float x = (float)Math.Floor(xRaw) + 0.5f;",
     "                float x = xRaw;",
     "sub-pixel positions return and Skia's anti-aliasing puts the 1px wick a fraction off the "
     "body centre on thin-body candles — the asymmetry this line was added to remove"),

    # ── Colour: who owns it ──────────────────────────────────────────────────
    ("I06", "the theme stops owning candle colour",
     R + "StandardRenderers.cs",
     "            if (bodyComp is { IsUserStyled: true })",
     "            if (bodyComp is not null)",
     "the component's metadata ColorHex — a hardcoded TradingView teal and salmon — overrides the "
     "theme again, so every theme repaints the chrome and leaves the candles alone, which is the "
     "defect the IsUserStyled test was introduced to end"),

    ("I07", "colour-vision-safe mode stops applying",
     R + "StandardRenderers.cs",
     "            => theme.ColorVisionSafe ? (ColorVisionUp, ColorVisionDown) : (up, down);",
     "            => (up, down);",
     "an accessibility setting a user switched ON does nothing: red/green candles come back for "
     "someone who cannot tell them apart"),

    ("I08", "hollow up-candles fill in",
     R + "StandardRenderers.cs",
     "                bool drawHollow = hollowUp && !hasPhaseOverride && bar.Close >= bar.Open;",
     "                bool drawHollow = hollowUp && !hasPhaseOverride && bar.Close < bar.Open;",
     "the accessibility mode that makes direction readable by SHAPE inverts: down bars are "
     "outlined and up bars filled, so shape now says the opposite of colour"),

    ("I09", "the two-colour split lands on the wrong side of its baseline",
     R + "StandardRenderers.cs",
     "            if (value >= comp.ColorBaseline) return above;",
     "            if (value <= comp.ColorBaseline) return above;",
     "MFI above its midline paints in the BELOW colour — the exact complaint that produced this "
     "code ('I thought it was red below and green above'), inverted"),

    ("I10", "a component that never asked for a split gets one",
     R + "StandardRenderers.cs",
     "            comp.UsePolarityColoring && !string.IsNullOrEmpty(comp.ColorHexSecondary);",
     "            !string.IsNullOrEmpty(comp.ColorHexSecondary);",
     "every component carrying a secondary colour — most of them, for other reasons — starts "
     "drawing in two colours split at a baseline it never declared"),

    # ── Fills ────────────────────────────────────────────────────────────────
    ("I11", "the opt-in area fill becomes opt-out",
     R + "StandardRenderers.cs",
     "            comp.IsAreaFill\n"
     "            || comp.DisplayType is ComponentDisplayType.Area or ComponentDisplayType.Gradient;",
     "            comp.DisplayType is ComponentDisplayType.Line or ComponentDisplayType.Oscillator\n"
     "            || comp.DisplayType is ComponentDisplayType.Area or ComponentDisplayType.Gradient;",
     "the renderer goes back to deciding from the display type, so about twenty-five components "
     "that declare IsAreaFill=false start drawing a fill — Cody's explicit call was opt-in"),

    ("I12", "the cloud crossover is placed at the wrong point between two bars",
     R + "StandardRenderers.cs",
     "            double t = Math.Abs(denom) < 1e-12 ? 0.5 : d1 / denom;",
     "            double t = Math.Abs(denom) < 1e-12 ? 0.5 : d2 / denom;",
     "the interpolation that finds where two cloud edges cross uses the wrong end's width, so the "
     "bull/bear colour changes at the wrong x and the fill shows a notch at every crossover"),

    # ── Markers ──────────────────────────────────────────────────────────────
    ("I13", "a marker can swell to cover the bar it annotates",
     R + "StandardRenderers.cs",
     "            float ceiling  = Math.Max(floor, barWidth * 1.8f);",
     "            float ceiling  = Math.Max(floor, barWidth * 18f);",
     "the clamp that keeps a glyph proportional to the bar spacing widens tenfold, so a "
     "high-Thickness marker becomes a blob covering ten bars of chart"),

    ("I14", "a marker shrinks below visibility at wide zoom",
     R + "StandardRenderers.cs",
     "            float floor    = 6f * ctx.Density;",
     "            float floor    = 0f;",
     "the minimum glyph size goes, so on a zoomed-out chart every signal marker collapses to a "
     "sub-pixel speck — present in the data, invisible on the screen"),

    ("I15", "a bar-anchored marker is drawn at its raw value instead",
     R + "StandardRenderers.cs",
     "            if (comp.MarkerAnchor == MarkerAnchor.Value || i < 0 || i >= ctx.Data.Count)",
     "            if (comp.MarkerAnchor != MarkerAnchor.Value || i < 0 || i >= ctx.Data.Count)",
     "BelowBar/AboveBar markers stop following the DISPLAYED bar, so in Heikin-Ashi they float "
     "away from the candle they describe and disagree with speech and audio, which follow it"),

    ("I16", "the above-bar and below-bar anchors swap",
     R + "StandardRenderers.cs",
     "            double anchor = comp.MarkerAnchor == MarkerAnchor.BelowBar ? bar.Low - pad : bar.High + pad;",
     "            double anchor = comp.MarkerAnchor == MarkerAnchor.BelowBar ? bar.High + pad : bar.Low - pad;",
     "buy markers sit above the bar and sell markers below it — the single most-read visual "
     "convention on the chart, reversed"),

    # ── The Y axis ───────────────────────────────────────────────────────────
    ("I17", "axis labels stop landing on gridlines",
     C + "ChartMath.cs",
     "            int multiple = (int)Math.Round(wanted / gridStep, MidpointRounding.AwayFromZero);\n"
     "            return Math.Max(1, multiple) * gridStep;",
     "            return wanted;",
     "the label step stops being a whole multiple of the gridline step, so labels float between "
     "lines — the defect this function was extracted to fix, and the first thing a reviewer "
     "notices on a projected chart"),

    ("I18", "axis labels crowd on top of one another",
     C + "ChartRenderer.cs",
     "                if (Math.Abs(y - lastLabelY) < minLabelSpacing) continue;",
     "                if (Math.Abs(y - lastLabelY) < 0) continue;",
     "the minimum spacing between labels goes, so a short indicator pane stacks its numbers into "
     "an unreadable smear. This mutant SURVIVED the 48th pass's rendering set and is still open"),

    ("I19", "the nice-number axis picks arbitrary steps",
     C + "ChartMath.cs",
     "            if (fraction < 1.5) return 1 * magnitude;",
     "            if (fraction < 1.5) return 1.37 * magnitude;",
     "the 1/2/5/10 ladder that makes an axis read in round numbers is broken, so a BTC chart "
     "labels at 76227.38 again — accurate and useless"),

    ("I20", "small panes ask for as many labels as large ones",
     C + "ChartMath.cs",
     "            => paneHeightPx < 100 * density ? 3 : 5;",
     "            => 5;",
     "a 40px indicator pane asks for five labels it has no room for, which the spacing guard then "
     "drops unevenly — the labels that survive are no longer evenly spaced"),

    ("I21", "the y-axis swatch becomes a block across the axis",
     C + "ChartRenderer.cs",
     "                    canvas.DrawRect(SKRect.Create(axisRect.Left, y - (tickH / 2f), tickW, tickH), p);",
     "                    canvas.DrawRect(axisRect.Left, y - (tickH / 2f), tickW, tickH, p);",
     "SKCanvas.DrawRect's four-float overload is (x, y, WIDTH, HEIGHT) — this is the exact "
     "confusion recorded in the comment above it, which turned a 4x3 tick into a block burying "
     "the axis labels"),

    # ── Pane layout ──────────────────────────────────────────────────────────
    ("I22", "a pane is drawn underneath the x-axis strip",
     C + "ChartRenderer.cs",
     "                float totalPaneHeight = height - _axisHeight;",
     "                float totalPaneHeight = height;",
     "the panes are laid out as though the x-axis strip were not there, so the bottom pane runs "
     "under it — the 2026-09-12 defect where eight panes put the last one off the screen"),

    ("I23", "the overflow fit is not applied, so the last pane loses its height",
     C + "ChartRenderer.cs",
     "                        for (int pi = 0; pi < indHeights.Length; pi++) indHeights[pi] *= fit;",
     "                        for (int pi = 0; pi < indHeights.Length; pi++) indHeights[pi] *= 1f;",
     "when saved ratios plus the main pane's floor exceed the canvas, the excess is supposed to be "
     "shared across the indicator panes; unshared it all lands on the LAST one, which is drawn "
     "off the bottom. Every oscillator has its own pane since 2026-09-11, so this is ordinary"),

    ("I24", "the y-axis column is not subtracted from the plot width",
     C + "ChartRenderer.cs",
     "                var mainPaneRect = new SKRect(0, currentY, width - _axisWidth, currentY + mainPaneHeight);",
     "                var mainPaneRect = new SKRect(0, currentY, width, currentY + mainPaneHeight);",
     "the main pane is drawn under the price axis, so the rightmost bars are hidden behind the "
     "labels and every pointer-to-bar mapping — which subtracts the same strip — is off"),

    ("I25", "the layout service is told the wrong axis fractions",
     C + "ChartRenderer.cs",
     "                _paneLayout.Update(dividers, _axisHeight / height, _axisWidth / width);",
     "                _paneLayout.Update(dividers, 0f, 0f);",
     "every pointer-to-data mapping goes through these two numbers; at zero the click-to-select, "
     "the hover readout, Shift+click measurement and every drawing anchor drift across the chart, "
     "which is the 2026-08-27 six-bars-out defect restored"),

    ("I26", "the divider fractions are reported against the wrong denominator",
     C + "ChartRenderer.cs",
     "                    dividers.Add((group.Key, currentY / height));",
     "                    dividers.Add((group.Key, currentY / totalPaneHeight));",
     "the drag handles that resize panes are positioned from these fractions, so every handle "
     "sits slightly below its divider and dragging one grabs the wrong pane near the bottom"),

    # ── Mapping value to pixel ───────────────────────────────────────────────
    ("I27", "the price axis is upside down",
     C + "ChartMath.cs",
     "                return (float)(bottom - ((value - min) / range * height));",
     "                return (float)(top + ((value - min) / range * height));",
     "higher prices map to lower pixels: the entire chart renders vertically mirrored, and every "
     "drawing, marker and crosshair with it"),

    ("I28", "a degenerate pane range divides by zero instead of centring",
     C + "ChartMath.cs",
     "                if (range <= 0.000001) return top + (height / 2.0f);",
     "                if (range < 0) return top + (height / 2.0f);",
     "a pane whose min equals its max — a flat series, or one component still in warmup — divides "
     "by zero and writes NaN into a coordinate, which Skia turns into nothing drawn at all"),

    ("I29", "log scale and linear scale swap",
     C + "ChartMath.cs",
     "            if (isLogScale)\n            {\n                // LOG SCALE: Maps price to Log space before projecting to screen.",
     "            if (!isLogScale)\n            {\n                // LOG SCALE: Maps price to Log space before projecting to screen.",
     "the log-scale switch inverts, so a linear chart is drawn logarithmically and vice versa — "
     "visible immediately on any long-range crypto chart"),

    # ── Per-bar colour rules ─────────────────────────────────────────────────
    ("I30", "a colour rule matches on the wrong side of its level",
     R + "StandardRenderers.cs",
     "                    ColorCondition.AboveLevel => val > rule.Level,",
     "                    ColorCondition.AboveLevel => val < rule.Level,",
     "every component using a level-based colour rule paints the two regions the wrong way round"),

    ("I31", "a rising bar is coloured as falling",
     R + "StandardRenderers.cs",
     "                    ColorCondition.Rising     => val > prev,",
     "                    ColorCondition.Rising     => val < prev,",
     "the direction rules invert, so a rising histogram is painted in the falling colour"),

    ("I32", "the first bar of a series is compared against nothing",
     R + "StandardRenderers.cs",
     "            double prev = (dataIdx > 0 && !double.IsNaN(data[dataIdx - 1])) ? data[dataIdx - 1] : val;",
     "            double prev = (dataIdx > 0 && !double.IsNaN(data[dataIdx - 1])) ? data[dataIdx - 1] : 0.0;",
     "the warmup fallback goes from 'compare against itself, so neither rising nor falling' to "
     "'compare against zero', which paints the first visible bar of every negative series as "
     "falling and every positive one as rising"),

    # ── Phase-coloured candles: the branch with NO fixture ───────────────────
    ("I33", "a phase-coloured candle ignores the phase",
     R + "StandardRenderers.cs",
     "                        bodyColor = GetPhaseColor((int)Math.Round(phaseData[absIdx]));",
     "                        bodyColor = bar.Close >= bar.Open ? bullish : bearish;",
     "Cipher S's sentiment overlay — the whole point of which is that the candle's colour IS the "
     "phase — falls back to ordinary up/down colouring. Grep the test project for `phaseData`: "
     "nothing. This branch has never had a fixture"),

    ("I34", "a phase overlay draws wicks it is supposed to suppress",
     R + "StandardRenderers.cs",
     "                if (!hasPhaseOverride)\n                {\n                    using var wickLease = SKPaintPool.Rent();",
     "                if (true)\n                {\n                    using var wickLease = SKPaintPool.Rent();",
     "wicks return under a phase overlay and clutter the colour view the overlay exists to give"),
]


def run(cmd, cwd=REPO, timeout=3600):
    p = subprocess.run(cmd, cwd=cwd, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def build():
    code, out = run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                    "-p:UseRazorSourceGenerator=false -v q --nologo")
    return code == 0, out


def test():
    return run("dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
               "-p:UseRazorSourceGenerator=false --no-build")


def recover_inflight():
    if not os.path.exists(INFLIGHT):
        return
    rel = open(INFLIGHT).read().strip()
    if rel:
        run(f"git checkout -- {rel}")
        print(f"recovered stale sabotage in {rel}", flush=True)
    os.remove(INFLIGHT)


def verify():
    ok = True
    for mid, area, relpath, find, repl, _ in MUTANTS:
        path = os.path.join(REPO, relpath)
        if not os.path.exists(path):
            print(f"{mid}: MISSING FILE {relpath}"); ok = False; continue
        src = open(path, encoding='utf-8-sig').read()
        n = src.count(find)
        if n != 1:
            print(f"{mid}: {n} occurrences (need exactly 1) in {relpath}\n    {find[:120]!r}")
            ok = False
        if find == repl:
            print(f"{mid}: EQUIVALENT — find == replace"); ok = False
    print(f"all {len(MUTANTS)} anchors unique" if ok else "ANCHOR CHECK FAILED")
    return ok


def main():
    if "--verify" in sys.argv:
        sys.exit(0 if verify() else 1)

    recover_inflight()
    only = [a for a in sys.argv[1:] if a.startswith("I")] or None
    results = json.load(open(OUT)) if os.path.exists(OUT) else []
    done = {r['id'] for r in results}

    for mid, area, relpath, find, repl, breaks in MUTANTS:
        if (only and mid not in only) or mid in done:
            continue
        path = os.path.join(REPO, relpath)
        original = open(path, encoding='utf-8-sig').read()
        n = original.count(find)
        rec = {'id': mid, 'area': area, 'file': relpath, 'breaks': breaks, 'occurrences': n}
        if n != 1:
            rec['status'] = 'BAD_ANCHOR'
            results.append(rec); json.dump(results, open(OUT, 'w'), indent=1)
            print(f"{mid}: BAD ANCHOR ({n}) — {relpath}", flush=True); continue
        t0 = time.time()
        try:
            open(INFLIGHT, 'w').write(relpath)
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original.replace(find, repl))
            ok, log = build()
            if not ok:
                rec['status'] = 'NO_COMPILE'; rec['log'] = log[-1500:]
                print(f"{mid}: DID NOT COMPILE — {area}", flush=True)
            else:
                code, out = test()
                m = SUMMARY_RE.search(out)
                rec['failed'] = int(m.group(1)) if m else -1
                rec['passed'] = int(m.group(2)) if m else -1
                names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
                rec['failing_tests'] = names[:40]
                rec['status'] = ('UNPARSED' if rec['failed'] < 0
                                 else 'CAUGHT' if rec['failed'] > 0 else 'SURVIVED')
                if names and all('AboutDialogHonestyTests' in x for x in names):
                    rec['status'] = 'UNVERIFIED_DOC_CONTAMINATION'
                print(f"{mid}: {rec['status']} failed={rec['failed']} ({time.time()-t0:.0f}s) — {area}", flush=True)
                if names:
                    print("      " + "; ".join(names[:6]), flush=True)
        finally:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original)
            os.utime(path, None)
            if os.path.exists(INFLIGHT):
                os.remove(INFLIGHT)
        rec['seconds'] = round(time.time() - t0)
        results.append(rec); json.dump(results, open(OUT, 'w'), indent=1)

    build()
    code, out = test()
    m = SUMMARY_RE.search(out)
    print(f"\n=== CONTROL (nothing sabotaged): {m.group(0) if m else 'UNPARSED'}", flush=True)

    print("\n=== summary")
    for r in results:
        print(f"  {r['id']} {r['status']:>10}  {r['area']}")
    surv = [r['id'] for r in results if r['status'] == 'SURVIVED']
    caught = sum(1 for r in results if r['status'] == 'CAUGHT')
    total = caught + len(surv)
    if total:
        print(f"\ncatch rate {caught}/{total} = {100*caught/total:.1f}%   survivors: {surv}")


if __name__ == '__main__':
    main()
