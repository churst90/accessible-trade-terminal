#!/usr/bin/env python3
"""A2h — the SEVENTH mutant set, aimed at the maths this terminal is FOR.

WHY THIS AREA.

`Core/Services/Indicators` is 40+ files and 15,813 lines — tied with
`Services/Accessibility` as the largest body of code in the repo. Across a2, a2b,
a2c/fresh, a2d, a2e, a2f, a2g and a3, mutants have been applied to exactly FIVE
of its files (IndicatorComputability, MovingAverageHelper, PivotLevelsProvider,
RollingQuantile, SwingStructureProvider), all of them small helpers.

The eight largest have had ZERO, ever:

    PulseProvider.cs                  1,553   0
    CipherBProvider.cs                1,381   0
    CipherCProvider.cs                  853   0
    LoukasCyclesProvider.cs             714   0
    TopBottomDetectorProvider.cs        688   0
    CipherSProvider.cs                  648   0
    CipherAProvider.cs                  615   0
    CipherSRProvider.cs                 569   0

That is ~7,000 lines of signal generation — the Market Cipher suite, Pulse, the
cycle and top/bottom detectors — and it is what the user actually trades on.
Four of these files have a single test file naming them at all; three more
providers in the directory (FearGreed, Hurst, AnchoredVwap, SkenderDetailFact)
are named by NONE.

The failure mode here is also the worst-shaped one in the codebase. A wrong
sentence is noticed the moment it is heard. A dot that fires on the wrong bar
sounds exactly like a dot that fires on the right one — it is a correct-sounding
earcon at a false moment, and the only way to find out is to lose money. Three
of the mutants below are LOOK-AHEAD: a marker stamped before the bars that
confirm it exist. Those are invisible live and flattering in a backtest, which
is the combination this repo has been bitten by before (the 2026-06-12
divergence audit, the CipherSR zone line, the MTF forward-fill).

SAMPLING FRAME: no file below appears in a2, a2b, fresh(a2c), a2d, a2e, a2f,
a2g or a3.

METHOD — identical to a2d..a2g so the numbers compare: apply one mutant, build,
run the FULL AccessibleTrader.Tests suite, record whether anything went red and
WHICH tests did, revert, touch. CAUGHT iff some test fails.

THE SEVEN HARNESS RULES (each learned from a run that lied — see the
sabotage-harness-rules note):
  1. `touch` after restoring, or MSBuild keeps the sabotaged binary.
  2. "No test matches the given testcase filter" is a FAILURE, not a pass.
  3. Assert the anchor is UNIQUE before patching. A 0- or 2-occurrence anchor
     records BAD_ANCHOR; an unapplied sabotage is UNVERIFIED, never a result.
  4. Restore from a file copy held in memory here, never `git checkout --`, and
     the tree must be COMMITTED before starting.
  5. DO NOT TOUCH THE REPO — SOURCE *OR DOCS* — WHILE A CAMPAIGN IS RUNNING.
     AboutDialogHonestyTests diffs Directory.Build.props against CHANGES.md and
     WHATSNEW.md, so a mid-run doc edit records FALSE CATCHES.
  6. NEVER add `-v q --nologo` to the `dotnet test` line — quiet verbosity
     suppresses per-test failure names, which removes the false-catch audit.
  7. Print the failing NAMES, not just the status, so a uniform result looks as
     wrong as it is.

Run DETACHED with setsid — a tracked background command is capped at ten minutes
and this takes ~2.5 hours.
"""
import json, os, re, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2h_sabotage_results.json")
INFLIGHT = os.path.join(REPO, "scratchpad", "a2h_sabotage_inflight.txt")

SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+)")

A = "AccessibleTrader.Core/Services/Indicators/"

# (id, area, file, find, replace, what-it-breaks)
MUTANTS = [

    # ── CipherBProvider.cs — 1,381 lines, zero mutants ever ─────────────────
    ("H01", "anchor suppression blocks the trend it should follow",
     A + "CipherBProvider.cs",
     "                    || !(ancPol[i] < 0 && wt1Anc[i] < -anchorSuppressDepth);",
     "                    || !(ancPol[i] > 0 && wt1Anc[i] > anchorSuppressDepth);",
     "the counter-trend trap filter inverts: blue dots are suppressed when the higher-timeframe "
     "anchor is strongly BULLISH and allowed when it is strongly bearish — the filter now removes "
     "exactly the entries it exists to keep"),

    ("H02", "the gold dot fires on one condition instead of three",
     A + "CipherBProvider.cs",
     "                        if (goldCd == 0 && conditionsMet >= goldMinConfluence)",
     "                        if (goldCd == 0 && conditionsMet >= 1)",
     "gold is the rarest and most important marker on this indicator and its whole meaning is "
     "CONFLUENCE. At K=1 it fires on nearly every blue dot, so the one signal that says 'several "
     "things agree' starts saying 'something happened'"),

    ("H03", "the N-bar confirmation requires the opposite of what it confirms",
     A + "CipherBProvider.cs",
     "                        if (double.IsNaN(wt1[i - k]) || wt1[i - k] > osHere) { sustained = false; break; }",
     "                        if (double.IsNaN(wt1[i - k]) || wt1[i - k] < osHere) { sustained = false; break; }",
     "ConfirmBars exists so a blue dot needs WT to have been BELOW oversold for the preceding "
     "bars; inverted, it demands WT was above it, so the confirmation gate passes exactly when the "
     "setup did not happen"),

    ("H04", "one shallow pivot is deep enough for a divergence",
     A + "CipherBProvider.cs",
     "                bool deepEnough = wt1[prev] < -divergenceDepth && wt1[curr] < -divergenceDepth;",
     "                bool deepEnough = wt1[prev] < -divergenceDepth || wt1[curr] < -divergenceDepth;",
     "the depth gate is what separates a divergence from two shallow wiggles with price drift; "
     "with OR, any pair where either leg happens to be deep qualifies"),

    ("H05", "LOOK-AHEAD: shallow divergences are shifted to a bar where they did not happen",
     A + "CipherBProvider.cs",
     "                bullDiv = ShiftMarkersForwardExcept(bullDiv, shallowBull, pivotBars, n);",
     "                bullDiv = ShiftMarkersForward(bullDiv, pivotBars, n);",
     "the documented exemption removed. The shallow detector stamps at the WT CROSSOVER bar and is "
     "already causal; shifting it moves every shallow bull divergence — a strategy leaf, an earcon "
     "and a spoken marker — pivotBars LATER than the market event it describes"),

    ("H06", "a WT pivot no longer needs to be a local extreme",
     A + "CipherBProvider.cs",
     "                    if (wt1[j] < wt1[i]) isLow  = false;",
     "                    if (wt1[j] < wt1[i] * 2) isLow  = false;",
     "the pivot test stops being 'lowest in the window' and becomes an arbitrary comparison "
     "against a doubled value, which for negative WT values inverts across zero — every "
     "divergence in the indicator is built on these pivots"),

    ("H07", "the Money Flow wave leaves its pane",
     A + "CipherBProvider.cs",
     "                if (scaled >  MfClamp) scaled =  MfClamp;",
     "                if (scaled >  MfClamp) scaled =  MfClamp * 10;",
     "the clamp that keeps the MF wave inside the WT pane's +/-100 bounds stops clamping, so on "
     "extreme-body runs the wave rescales the whole pane and every other component in it flattens "
     "— the auto-fit axis is shared"),

    ("H08", "the adaptive oversold floor becomes a ceiling",
     A + "CipherBProvider.cs",
     "                    adaptiveOs[i] = double.IsNaN(osRaw[i]) ? -obLevel : Math.Min(osRaw[i], -minFloor);",
     "                    adaptiveOs[i] = double.IsNaN(osRaw[i]) ? -obLevel : Math.Max(osRaw[i], -minFloor);",
     "MinThresholdFloor exists to stop the adaptive threshold collapsing toward zero in a "
     "compressed regime, which is when whipsaw is worst; Max instead of Min pins it AT the floor "
     "and the adaptation stops happening in the one regime it was added for"),

    # ── PulseProvider.cs — 1,553 lines, zero mutants ever ───────────────────
    ("H09", "LOOK-AHEAD: a daily bar reads a week that has not closed",
     A + "PulseProvider.cs",
     "            int use = (buckets[b].Complete && i >= buckets[b].LastBar) ? b : b - 1;",
     "            int use = b;",
     "the whole point of CompletedBucketAt is that at bar i the most recent weekly value available "
     "is the last one whose bucket has CLOSED. Taking the current bucket unconditionally feeds "
     "every daily bar a weekly RSI and regime computed from bars later in the same week"),

    ("H10", "LOOK-AHEAD: an unfinished bucket is forward-filled anyway",
     A + "PulseProvider.cs",
     "            if (use < 0 || !buckets[use].Complete) return -1;",
     "            if (use < 0) return -1;",
     "the second half of the same guard: a bucket straddling a data gap, or the trailing running "
     "one, has a close that is not yet known, and this lets its value be carried onto bars before "
     "it exists"),

    ("H11", "the ADX lookback window becomes the whole chart",
     A + "PulseProvider.cs",
     "        private static bool AdxPassWithin(int i, int lookback, ReadOnlySpan<double> adx, double minVal)\n"
     "        {\n"
     "            int start = Math.Max(0, i - lookback);",
     "        private static bool AdxPassWithin(int i, int lookback, ReadOnlySpan<double> adx, double minVal)\n"
     "        {\n"
     "            int start = 0;",
     "'ADX cleared the gate at some point in the last N bars' becomes 'at any point in recorded "
     "history', so the trend-strength gate passes permanently after the first strong trend the "
     "series ever had"),

    ("H12", "the hold-down between markers stops holding",
     A + "PulseProvider.cs",
     "                if (bullCross && fastSlopeUp && (i - lastBullV2) >= holdDownBars)",
     "                if (bullCross && fastSlopeUp && (i - lastBullV2) >= 0)",
     "holdDownBars exists so one choppy midline region does not emit a burst of long triggers; "
     "without it the same cycle fires repeatedly and the cluster earcon becomes a rattle"),

    ("H13", "a long entry is allowed in a bear regime",
     A + "PulseProvider.cs",
     "                    bool regimeOk = !double.IsNaN(reg[i]) && reg[i] >= 0.5;            // Regime == +1",
     "                    bool regimeOk = !double.IsNaN(reg[i]) && reg[i] >= -0.5;            // Regime == +1",
     "regime is one of only three orthogonal gates left on the v2 long after the 2026-04-09 "
     "walk-forward stripped the others; widened to >= -0.5 it admits the neutral AND bull states, "
     "so the gate that says 'price structure agrees' stops disagreeing with anything but a hard bear"),

    ("H14", "Wilder smoothing on the weekly RSI becomes a different average",
     A + "PulseProvider.cs",
     "                avgGain = (avgGain * (rsiPeriod - 1) + g) / rsiPeriod;",
     "                avgGain = (avgGain * rsiPeriod + g) / rsiPeriod;",
     "the multi-timeframe anchor RSI stops being an RSI: the recursion no longer decays, so "
     "avgGain grows without bound and the weekly anchor drifts to 100 and stays there"),

    ("H15", "the midline cross fires while sitting on the midline",
     A + "PulseProvider.cs",
     "                bool bullCross = prev <  midline && cur >= midline;",
     "                bool bullCross = prev <= midline && cur >= midline;",
     "a bar that opens and closes exactly at the midline is now a cross, so a flat series pinned "
     "at the midline emits a bull cross on every bar"),

    # ── CipherAProvider.cs — 615 lines, zero mutants ever ───────────────────
    ("H16", "the oversold state no longer has to be sustained",
     A + "CipherAProvider.cs",
     "                bool sustainedOs = wt1[i] < -obLevel && !double.IsNaN(wt1[i - 1]) && wt1[i - 1] < -obLevel;",
     "                bool sustainedOs = wt1[i] < -obLevel;",
     "a single bar dipping past the oversold line now counts as a sustained oversold condition, so "
     "the markers gated on it fire on one-bar spikes"),

    # ── CipherSRProvider.cs — 569 lines, zero mutants ever ──────────────────
    ("H17", "LOOK-AHEAD: the support/resistance zone appears before its pivot exists",
     A + "CipherSRProvider.cs",
     "                if (isPivotHigh) { resistance[i] = data[i].High; resConfirmed[i + pb] = data[i].High; }",
     "                if (isPivotHigh) { resistance[i] = data[i].High; resConfirmed[i] = data[i].High; }",
     "a pivot at bar i needs bars i+1..i+pb to BE a pivot; confirming at i draws the zone line up "
     "to 15 bars before it exists and a strategy comparing price to that level reads a number "
     "derived from bars it has not seen — the exact defect fixed on 2026-08-21"),

    ("H18", "a resistance level breaks on a touch instead of a convincing close past it",
     A + "CipherSRProvider.cs",
     "                if (!newRes && !double.IsNaN(lastRes) && data[i].Close > lastRes * (1.0 + breakPct))",
     "                if (!newRes && !double.IsNaN(lastRes) && data[i].Close > lastRes * (1.0 - breakPct))",
     "the break threshold moves to the wrong side of the level, so a close still BELOW resistance "
     "invalidates it — every zone is destroyed by the first bar that approaches it"),

    ("H19", "a pivot high tolerates an equal high beside it",
     A + "CipherSRProvider.cs",
     "                    if (data[i].High <= data[i - k].High || data[i].High <= data[i + k].High)",
     "                    if (data[i].High < data[i - k].High || data[i].High < data[i + k].High)",
     "'strictly greatest in the window' becomes 'greatest or equal', so a flat top or a doubled "
     "high emits two adjacent resistance pivots instead of none"),

    # ── TopBottomDetectorProvider.cs — 688 lines, zero mutants ever ─────────
    ("H20", "sideways noise pattern-matches as a top",
     A + "TopBottomDetectorProvider.cs",
     "                    meaningfulRange = winRange > 0 && atr[i] > 0 && winRange / atr[i] >= meaningfulRangeAtr;",
     "                    meaningfulRange = winRange > 0 && atr[i] > 0 && winRange / atr[i] <= meaningfulRangeAtr;",
     "the gate inverts: distribution evidence is now admitted ONLY in flat chop and blocked during "
     "a real rally, which is precisely backwards from the comment that says why the gate exists"),

    ("H21", "'at the top of the range' means at the bottom of it",
     A + "TopBottomDetectorProvider.cs",
     "                        atTop    = (winHigh - data[i].High) / winRange <= 0.20;",
     "                        atTop    = (winHigh - data[i].High) / winRange >= 0.20;",
     "the positional gate on every distribution stream inverts, so a top is detected when price "
     "sits in the lower four-fifths of its own trailing range"),

    ("H22", "a capitulation bottom re-fires on every bar of the same event",
     A + "TopBottomDetectorProvider.cs",
     "                        if (double.IsNaN(prevCap) || prevCap < confirm)",
     "                        if (true)",
     "the rising-edge test is what makes this a marker rather than a state: without it a "
     "capitulation lasting eight bars stamps eight bottoms, and the earcon fires eight times for "
     "one event"),

    # ── CipherSProvider.cs — 648 lines, zero mutants ever ───────────────────
    ("H23", "the cycle window is re-detected on every recalculation",
     A + "CipherSProvider.cs",
     "                if (_detectionCache.TryGetValue(key, out var cached) && n < (int)(cached.dataCount * 1.5))",
     "                if (_detectionCache.TryGetValue(key, out var cached) && n < cached.dataCount)",
     "the 1.5x staleness margin is what stops the auto-detector re-running (and re-announcing) on "
     "every tick; at a bare inequality any growth in the series re-detects, so the chart narrates "
     "a new cycle window continuously"),

    ("H24", "every trivial change in the detected cycle is announced",
     A + "CipherSProvider.cs",
     "                Math.Abs(suggested - lastDetected) / (double)lastDetected > 0.15;",
     "                Math.Abs(suggested - lastDetected) / (double)lastDetected > 0.0;",
     "the significance threshold is what keeps this from speaking on noise; at 0 a one-bar shift "
     "in the median trough interval produces a full spoken sentence"),

    # ── IchimokuProvider.cs — 432 lines, zero mutants ever ──────────────────
    ("H25", "LOOK-AHEAD: the Chikou span is plotted forward instead of back",
     A + "IchimokuProvider.cs",
     "                int bwd = i - displacement;\n                if (bwd >= 0)\n                    chikou[bwd] = data[i].Close;",
     "                int bwd = i + displacement;\n                if (bwd < n)\n                    chikou[bwd] = data[i].Close;",
     "Chikou is the close plotted DISPLACEMENT bars BEHIND — that is the entire definition, and it "
     "is why it can be compared to past price. Plotted forward, every bar shows a close from 26 "
     "bars in the future: a look-ahead that reads as a beautifully predictive line"),

    ("H26", "the Tenkan/Kijun cross loses its confirmation bar",
     A + "IchimokuProvider.cs",
     "                if (crossUpPrior && stillUp) tkBull[i] = kijun[i];",
     "                if (crossUpPrior) tkBull[i] = kijun[i];",
     "the 2-bar confirmation is dropped, so a cross that immediately reverses still stamps a "
     "bullish marker"),

    # ── AnchoredVwapProvider.cs — 245 lines, named by NO test ───────────────
    ("H27", "the anchor pivot stops being a pivot",
     A + "AnchoredVwapProvider.cs",
     "                        if (data[pIdx + k].High >= pH) isHigh = false;",
     "                        if (data[pIdx + k].High >= pH) { }",
     "a swing high is only a swing high because the bars AFTER it are lower; checking only the "
     "bars before turns every new high into an anchor, so the VWAP re-anchors on the way up and "
     "the level the user is measuring against never settles"),

    ("H28", "the VWAP's typical price drops the close",
     A + "AnchoredVwapProvider.cs",
     "                double typical = (data[i].High + data[i].Low + data[i].Close) / 3.0;",
     "                double typical = (data[i].High + data[i].Low) / 2.0;",
     "HLC3 is the definition of the price a VWAP is volume-weighting; the midpoint ignores where "
     "the bar actually closed, so the level drifts away from the traded average it claims to be"),

    # ── HurstExponentProvider.cs — 266 lines, named by NO test ──────────────
    ("H29", "the Hurst exponent is computed on price differences instead of log returns",
     A + "HurstExponentProvider.cs",
     "                    ret[i] = Math.Log(data[i].Close / data[i - 1].Close);",
     "                    ret[i] = data[i].Close - data[i - 1].Close;",
     "rescaled-range analysis is scale-invariant only on log returns; on raw differences the "
     "exponent becomes a function of the asset's price level, so the same series at a different "
     "denomination reports a different regime"),

    # ── MACloudProvider.cs — 269 lines ──────────────────────────────────────
    ("H30", "the cloud is 'expanding' on any increase at all",
     A + "MACloudProvider.cs",
     "                if (was > 0 && now > was * 1.02) parts.Add(\"expanding\");",
     "                if (was > 0 && now > was) parts.Add(\"expanding\");",
     "the 2% deadband is what stops the spoken description flipping between expanding and "
     "contracting on rounding; without it the sentence changes almost every bar"),

    # ── FearGreedProvider.cs — 267 lines, named by NO test ──────────────────
    ("H31", "a sentiment flip is announced without a previous side",
     A + "FearGreedProvider.cs",
     "                if (previousSide != 0 && side != 0 && side != previousSide)",
     "                if (side != 0 && side != previousSide)",
     "the first non-neutral reading on a freshly loaded chart now stamps a FLIP, so every chart "
     "opens by announcing a sentiment change that did not occur"),

    ("H32", "fear and greed swap sides",
     A + "FearGreedProvider.cs",
     "                if (v <= fearLevel)  fearSpan[i]  = v;\n                if (v >= greedLevel) greedSpan[i] = v;",
     "                if (v >= fearLevel)  fearSpan[i]  = v;\n                if (v <= greedLevel) greedSpan[i] = v;",
     "extreme fear reads as greed and vice versa on the one indicator whose entire output is which "
     "of those two it is"),

    # ── SkenderDetailFactProvider.cs — 347 lines, named by NO test ──────────
    ("H33", "the spoken divergence hint says the opposite",
     A + "SkenderDetailFactProvider.cs",
     "                            if (rsiUp && !priceUp)  divergence = \" Bullish divergence hint.\";",
     "                            if (rsiUp && !priceUp)  divergence = \" Bearish divergence hint.\";",
     "RSI rising while price falls is the textbook BULLISH divergence; this is a sentence the user "
     "hears on the detail key, and it now names the wrong direction"),

    # ── IndicatorEngine / shared infrastructure ─────────────────────────────
    ("H34", "the moving-average dispatch falls through to one type",
     A + "MovingAverageHelper.cs",
     "                \"HMA\"  => Hma(source, period),",
     "                \"HMA\"  => Sma(source, period),",
     "MACloudProvider offers six MA types and the user picks one per component; HMA silently "
     "becomes SMA, so a setting that names a different calculation produces the original one"),

    # ── CrossSeriesCache.cs ─────────────────────────────────────────────────
    ("H35", "LOOK-AHEAD: external data is back-filled onto bars before it was published",
     A + "CrossSeriesCache.cs",
     "                // First tick is later than this bar — leave NaN.\n"
     "                if (ticks[tickIdx].Ts > barTs) continue;",
     "                // First tick is later than this bar — leave NaN.",
     "CrossSeriesForwardFill is the one forward-fill every external series flows through — "
     "sentiment, COT positioning, funding, open interest, crowding. Without this guard the FIRST "
     "tick's value is written onto every bar before it, so a chart shows a Fear and Greed reading "
     "on dates that predate the reading"),

    # ── SwingStructureProvider.cs — previously mutated, one new site ────────
    ("H36", "a flat stretch mints two intermediate cycle lows",
     A + "LoukasCyclesProvider.cs",
     "                                    if (q >= 0 && dclLows[q] <= dclLows[^1]) isIcl = false;",
     "                                    if (q >= 0 && dclLows[q] < dclLows[^1]) isIcl = false;",
     "the tie rule inverts: an equal prior daily-cycle low no longer holds the title, so one flat "
     "stretch produces two ICLs and the intermediate-cycle count — which every downstream day "
     "count is measured from — goes wrong for the rest of the chart"),

    # ── LoukasCyclesProvider.cs — 714 lines, zero mutants ever ──────────────
    ("H37", "a daily cycle low no longer has to be a low",
     A + "LoukasCyclesProvider.cs",
     "                        if (data[j].Low <= kLow) { isLocalMin = false; break; }",
     "                        if (data[j].Low <  kLow) { isLocalMin = false; break; }",
     "the symmetric strict-< local minimum is the ONLY selectivity this detector has (the comment "
     "records two windowed-lowest filters that were tried and removed); allowing ties means a flat "
     "base emits a DCL on each of its bars"),

    # ── CipherCProvider.cs — 853 lines, zero mutants ever ───────────────────
    ("H38", "an incomplete down-cycle is classified as a completed one",
     A + "CipherCProvider.cs",
     "                        if (!reachedOS)",
     "                        if (reachedOS)",
     "reachedOS is the memory of whether the down-swing actually got to oversold, and it is what "
     "separates a bull-continuation shallow trough from a genuine cycle bottom. Inverted, every "
     "completed cycle is reported as a failed one and vice versa — two different markers, two "
     "different earcons, swapped"),
]


def run(cmd, cwd=REPO, timeout=3600):
    p = subprocess.run(cmd, cwd=cwd, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def build():
    code, out = run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                    "-p:UseRazorSourceGenerator=false -v q --nologo")
    return code == 0, out


def test():
    # Rule 6: NO `-v q --nologo` here.
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
            print(f"{mid}: MISSING FILE {relpath}")
            ok = False
            continue
        src = open(path, encoding='utf-8-sig').read()
        n = src.count(find)
        if n != 1:
            print(f"{mid}: {n} occurrences (need exactly 1) in {relpath}\n    {find[:120]!r}")
            ok = False
        if find == repl:
            print(f"{mid}: EQUIVALENT — find == replace")
            ok = False
    print(f"all {len(MUTANTS)} anchors unique" if ok else "ANCHOR CHECK FAILED")
    return ok


def main():
    if "--verify" in sys.argv:
        sys.exit(0 if verify() else 1)

    recover_inflight()
    only = [a for a in sys.argv[1:] if a.startswith("H")] or None
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
            results.append(rec)
            json.dump(results, open(OUT, 'w'), indent=1)
            print(f"{mid}: BAD ANCHOR ({n} occurrences) — {relpath}", flush=True)
            continue
        t0 = time.time()
        try:
            open(INFLIGHT, 'w').write(relpath)
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original.replace(find, repl))
            ok, log = build()
            if not ok:
                rec['status'] = 'NO_COMPILE'
                rec['log'] = log[-1500:]
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
                print(f"{mid}: {rec['status']} failed={rec['failed']} "
                      f"({time.time()-t0:.0f}s) — {area}", flush=True)
                if names:
                    print("      " + "; ".join(names[:6]), flush=True)
        finally:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original)
            os.utime(path, None)
            if os.path.exists(INFLIGHT):
                os.remove(INFLIGHT)
        rec['seconds'] = round(time.time() - t0)
        results.append(rec)
        json.dump(results, open(OUT, 'w'), indent=1)

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
