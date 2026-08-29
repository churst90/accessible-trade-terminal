#!/usr/bin/env python3
"""The FRESH 28-mutant campaign — chosen without reference to A2's list.

WHY THIS EXISTS. A2 measured 61% on 2026-08-26; A2b re-ran A2's OWN 28 mutants on
2026-08-28 and measured 89.3%. That second number carries a caveat that makes it
unquotable on its own: much of the test work between the two runs was written in
response to A2's published survivor list, so 89.3% measures how well the suite
defends *the 28 properties it was told about*. It is an upper bound. This run
picks 28 different mutants so the number means "the suite defends the app".

THE SAMPLING FRAME, stated up front because it is the thing that makes or breaks
the comparison. A2's 28 clustered in the money and speech hot paths — trading,
speech formatting, indicator math, theming, the sandbox blocklist. Eight of its
28 lived in four files. This set is deliberately STRATIFIED ACROSS AREAS A2
NEVER TOUCHED, in rough proportion to where the production code actually is:

    Core/Analysis (levels, swings, chart patterns)   7    N03-N09
    Plugins (providers + analytics)                  5    N18-N22   [A2: zero]
    Core data + import                               4    N01,N02,N25,N26
    Core UI state (viewport, tabs)                   3    N12-N14
    Core/Trading managed exits                       2    N10,N11
    Security / outbound network                      2    N16,N17
    WebHost hosted alerting                          2    N23,N24   [A2: one]
    Core audio                                       1    N15
    Core accessibility                               1    N27
    Sdk screening                                    1    N28

Not one of the 28 files A2 mutated appears here, and no A2 mutant is repeated in
a different spelling. The consequence to expect, and it is the point of running
it: this set reaches colder code than A2's did, so a LOWER catch rate here is the
honest reading of the suite, not a regression in it.

Every mutant is a single-line change that compiles and that a user would
experience — no equivalent mutants, no dead-code edits, no comment changes.

METHOD, identical to a2_sabotage.py / a2b_sabotage.py so the numbers compare:
apply one mutant, build, run the FULL suite, record whether anything went red AND
WHICH TESTS DID, revert, touch. CAUGHT iff some test fails.

TWO TRAPS CARRIED IN FROM THE PREVIOUS RUNS:
  * A2 had FIVE mutants come back falsely CAUGHT by one unrelated flaky test
    firing alone — the whole difference between a naive 79% and a true 61%.
    Every single-test catch here must be re-run in isolation before it counts.
  * A killed campaign leaves a sabotaged file in the tree. `finally` does not run
    on SIGKILL, so an IN-FLIGHT marker is written before each edit and any stale
    one is restored from git at startup.

The campaign runs `AccessibleTrader.Tests` only. A kill that lives in
`AccessibleTrader.BrowserTests` still scores as a SURVIVOR here — that is
deliberate and it is what A2/A2b measured too.
"""
import json, os, re, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "fresh_sabotage_results.json")
INFLIGHT = os.path.join(REPO, "scratchpad", "fresh_sabotage_inflight.txt")

# (id, area, file, find, replace, what-it-breaks)
MUTANTS = [
    # ── Core: stored data and CSV import ────────────────────────────────────
    ("N01", "ohlcv cache completeness",
     "AccessibleTrader.Core/Services/OhlcvStore.cs",
     "                if (rows.Count < limit) return new List<Ohlcv>();",
     "                if (rows.Count < 0) return new List<Ohlcv>();",
     "a partial cache hit is served as if it were the full requested range"),

    ("N02", "unclosed bar persisted",
     "AccessibleTrader.Core/Services/OhlcvStore.cs",
     "            var closed = bars.Where(b => ToMs(TimeframeUtility.GetPeriodEnd(b.Date, timeframe)) <= nowMs).ToList();",
     "            var closed = bars.Where(b => ToMs(b.Date) <= nowMs).ToList();",
     "the still-forming bar is written to the store as final"),

    ("N25", "CSV import sanity",
     "AccessibleTrader.Core/Services/MyData/CsvDataParser.cs",
     "                if (high < low)",
     "                if (high < double.NegativeInfinity)",
     "an imported bar whose high is below its low is accepted silently"),

    ("N26", "CSV import row cap",
     "AccessibleTrader.Core/Services/MyData/CsvDataParser.cs",
     "            if (lines.Count - 1 > MaxRows)",
     "            if (lines.Count - 1 > int.MaxValue)",
     "an oversized CSV is imported with no bound"),

    # ── Core/Analysis: the structural claims the app speaks ─────────────────
    ("N03", "support versus resistance",
     "AccessibleTrader.Core/Services/Analysis/LevelPolarity.cs",
     "        public static bool IsResistance(double level, double referencePrice) => level >= referencePrice;",
     "        public static bool IsResistance(double level, double referencePrice) => level <= referencePrice;",
     "every level is announced as the opposite structural level"),

    ("N04", "level touch separation",
     "AccessibleTrader.Core/Services/Analysis/LevelRespectAnalyzer.cs",
     "                if (lastCountedBar >= 0 && i - lastCountedBar < opts.MinSeparationBars) continue;",
     "                if (lastCountedBar >= 0 && i - lastCountedBar < 0) continue;",
     "one long touch is counted as many, inflating the respect score"),

    ("N05", "level break detection",
     "AccessibleTrader.Core/Services/Analysis/LevelRespectAnalyzer.cs",
     "                    if (bar.Close < lineAtJ - breakDistance)",
     "                    if (bar.Close > lineAtJ - breakDistance)",
     "a level approached from above is reported broken on every touch"),

    ("N06", "swing high strictness",
     "AccessibleTrader.Core/Services/Analysis/SwingStructureAnalyzer.cs",
     "                    if (bars[j].High >= bars[i].High) isHigh = false;",
     "                    if (bars[j].High > bars[i].High) isHigh = false;",
     "a flat top registers two swing highs — ties no longer disqualify"),

    ("N07", "swing noise filter",
     "AccessibleTrader.Core/Services/Analysis/SwingStructureAnalyzer.cs",
     "                if (Math.Abs(p.Price - last.Price) < a * opts.MinSwingAtr) continue;",
     "                if (Math.Abs(p.Price - last.Price) < 0) continue;",
     "market structure is reported from every wiggle — the ATR filter is off"),

    # The width and tolerance guards each appear several times in this file (once
    # per pattern family), so both anchors carry the lines above them. N08 targets
    # head-and-shoulders, N09 the double top — deliberately different families.
    ("N08", "head-and-shoulders width bound",
     "AccessibleTrader.Core/Services/Analysis/ChartPatternDetector.cs",
     "                int width = right.BarIndex - left.BarIndex;\n"
     "                if (width < o.MinPatternBars || width > o.MaxPatternBars) continue;",
     "                int width = right.BarIndex - left.BarIndex;\n"
     "                if (width < o.MinPatternBars) continue;",
     "an unboundedly wide formation is reported as head-and-shoulders"),

    ("N09", "double top tolerance",
     "AccessibleTrader.Core/Services/Analysis/ChartPatternDetector.cs",
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
     "two highs at any distance apart are called a double top"),

    # ── Core/Trading: managed exits ─────────────────────────────────────────
    ("N10", "short stop never triggers",
     "AccessibleTrader.Core/Services/Trading/ManagedExitRules.cs",
     "            positionSide == OrderSide.Buy ? bar.Low <= stop : bar.High >= stop;",
     "            positionSide == OrderSide.Buy ? bar.Low <= stop : bar.High <= stop;",
     "a short position's stop never fires on a rally"),

    ("N11", "trailing stop ratchets the wrong way",
     "AccessibleTrader.Core/Services/Trading/ManagedExitRules.cs",
     "            if (positionSide == OrderSide.Buy  && newStop > currentStop) return newStop;",
     "            if (positionSide == OrderSide.Buy  && newStop < currentStop) return newStop;",
     "a long's trailing stop moves DOWN, widening risk as price rises"),

    # ── Core: chart viewport and workspace tabs ─────────────────────────────
    ("N12", "cursor target not clamped",
     "AccessibleTrader.Core/Services/ViewportNavigationService.cs",
     "        int newIdx = Math.Clamp(targetIndex, 0, state.Data.Count - 1);",
     "        int newIdx = targetIndex;",
     "an out-of-range navigation target puts the cursor outside the data"),

    ("N13", "the last tab can be closed",
     "AccessibleTrader.Core/Services/Workspace/Reducers/TabReducer.cs",
     "            if (tabCount <= 1) return state; // Can't close the last tab",
     "            if (tabCount <= 0) return state; // Can't close the last tab",
     "closing the only tab leaves the workspace with no tab at all"),

    ("N14", "tab index shift after close",
     "AccessibleTrader.Core/Services/Workspace/Reducers/TabReducer.cs",
     "        private static int ShiftDownPast(int index, int closed) => index > closed ? index - 1 : index;",
     "        private static int ShiftDownPast(int index, int closed) => index;",
     "after a close, every later tab keeps its old index and snapshots bind to the wrong tab"),

    # ── Core: audio ─────────────────────────────────────────────────────────
    ("N15", "output limiter disabled",
     "AccessibleTrader.Core/Services/Audio/AudioEngine.cs",
     "                float required = framePeak > LimiterCeiling ? LimiterCeiling / framePeak : 1.0f;",
     "                float required = 1.0f;",
     "chart-scope audio clips again — an ordinary 18-voice layout peaks at 5.5x full scale"),

    # ── Core: accessibility ─────────────────────────────────────────────────
    ("N27", "unspecified-kind stamp spoken raw",
     "AccessibleTrader.Core/Services/Accessibility/SpeechTimeFormatter.cs",
     "            _ => DateTime.SpecifyKind(stamp, DateTimeKind.Utc).ToLocalTime(),",
     "            _ => stamp,",
     "a bar with unspecified kind is spoken in UTC while the chart shows local"),

    # ── Security: outbound network ──────────────────────────────────────────
    ("N16", "loopback is public",
     "AccessibleTrader.Core/Services/Alerts/OutboundNetworkGuard.cs",
     "            if (IPAddress.IsLoopback(ip)) return false;",
     "            if (IPAddress.IsLoopback(ip)) return true;",
     "an alert channel can be pointed at 127.0.0.1 — SSRF into the server's own loopback"),

    ("N17", "one private A record passes",
     "AccessibleTrader.Core/Services/Alerts/OutboundNetworkGuard.cs",
     "            if (resolved.Length == 0 || resolved.Any(a => !IsPublic(a)))",
     "            if (resolved.Length == 0)",
     "DNS rebinding — a host with one private address among public ones resolves through"),

    # ── Plugins: providers and analytics (A2 mutated none) ──────────────────
    ("N18", "kraken legacy pair codes",
     "Plugins/Providers/AccessibleTrader.Plugins.Kraken/KrakenProvider.cs",
     "            if (p.Length == 8 && (p[0] == 'X' || p[0] == 'Z') && (p[4] == 'X' || p[4] == 'Z'))",
     "            if (p.Length == 9 && (p[0] == 'X' || p[0] == 'Z') && (p[4] == 'X' || p[4] == 'Z'))",
     "XXBTZUSD no longer folds to BTCUSD — History is empty for BTC/USD again"),

    ("N19", "kraken asset normalisation",
     "Plugins/Providers/AccessibleTrader.Plugins.Kraken/KrakenProvider.cs",
     "            return a switch { \"BTC\" => \"XBT\", \"DOGE\" => \"XDG\", _ => a };",
     "            return a;",
     "balance queries ask Kraken for an asset code the venue does not use"),

    ("N20", "twelvedata key escaping",
     "Plugins/Providers/AccessibleTrader.Plugins.TwelveData/TwelveDataProvider.cs",
     "        private string KeyParam => Uri.EscapeDataString(_apiKey ?? string.Empty);",
     "        private string KeyParam => _apiKey ?? string.Empty;",
     "a key containing '&' is truncated at the ampersand"),

    ("N21", "finnhub key escaping",
     "Plugins/Providers/AccessibleTrader.Plugins.Finnhub/FinnhubProvider.cs",
     "        private string KeyParam => Uri.EscapeDataString(_apiKey ?? string.Empty);",
     "        private string KeyParam => _apiKey ?? string.Empty;",
     "same defect in a second provider — does the guard generalise, or is it per-provider?"),

    ("N22", "etherscan look-ahead guard",
     "Plugins/Analytics/AccessibleTrader.Plugins.Etherscan/EtherscanProvider.cs",
     "                        DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime < DateTime.UtcNow.AddMinutes(-1))",
     "                        DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime < DateTime.UtcNow.AddDays(1))",
     "a historical request is answered with today's reading — a 24-hour look-ahead"),

    # ── WebHost: hosted alerting ────────────────────────────────────────────
    ("N23", "dead feed never reported",
     "AccessibleTrader.WebHost/Services/HostedAlertMonitor.cs",
     "            if (n < FeedFailuresBeforeReporting) return;",
     "            if (n < int.MaxValue) return;",
     "a dead data feed is never reported — alerts silently stop evaluating"),

    ("N24", "alert evaluated on one bar",
     "AccessibleTrader.WebHost/Services/HostedAlertMonitor.cs",
     "                if (bars.Count < 2) continue;",
     "                if (bars.Count < 0) continue;",
     "a crossing is evaluated with no previous bar to cross from"),

    # ── Sdk: screening ──────────────────────────────────────────────────────
    ("N28", "unevaluated rows counted as matches",
     "AccessibleTrader.Sdk/Screening/ScreenerSpec.cs",
     "            foreach (var r in Rows) if (r is { Status: ScreenerRowStatus.Evaluated, Matched: true }) n++;",
     "            foreach (var r in Rows) if (r.Matched) n++;",
     "a row the screener could not evaluate is reported as a match"),
]


def run(cmd, timeout=3600):
    return subprocess.run(cmd, shell=True, cwd=REPO, capture_output=True, text=True, timeout=timeout)


def build():
    r = run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
            "-p:UseRazorSourceGenerator=false -v:q --nologo")
    return r.returncode == 0, r.stdout[-4000:] + r.stderr[-2000:]


def test():
    r = run("dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
            "-p:UseRazorSourceGenerator=false --no-build --nologo")
    return r.returncode, r.stdout


SUMMARY_RE = re.compile(r'Failed:\s*(\d+),\s*Passed:\s*(\d+)')


def recover_inflight():
    """`finally` does not run on SIGKILL. Restore anything a killed run left dirty."""
    if not os.path.exists(INFLIGHT):
        return
    rel = open(INFLIGHT).read().strip()
    if rel:
        print(f"!! recovering sabotaged file from a killed run: {rel}", flush=True)
        run(f"git checkout -- {rel!r}")
        os.utime(os.path.join(REPO, rel), None)
    os.remove(INFLIGHT)


def verify():
    """Anchor check only — no build, no tests. Every find must appear EXACTLY once."""
    bad = 0
    for mid, area, relpath, find, repl, breaks in MUTANTS:
        path = os.path.join(REPO, relpath)
        if not os.path.exists(path):
            print(f"{mid}: MISSING FILE {relpath}")
            bad += 1
            continue
        n = open(path, encoding='utf-8-sig').read().count(find)
        if n != 1:
            print(f"{mid}: {n} occurrences in {relpath}\n     {find!r}")
            bad += 1
    print(f"\n{len(MUTANTS)} mutants, {bad} bad anchors")
    return bad


def main():
    if "--verify" in sys.argv:
        sys.exit(1 if verify() else 0)

    recover_inflight()
    only = [a for a in sys.argv[1:] if a.startswith("N")] or None
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
                print(f"{mid}: DID NOT COMPILE", flush=True)
            else:
                code, out = test()
                m = SUMMARY_RE.search(out)
                rec['failed'] = int(m.group(1)) if m else -1
                rec['passed'] = int(m.group(2)) if m else -1
                names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
                rec['failing_tests'] = names[:40]
                rec['status'] = 'CAUGHT' if rec['failed'] > 0 else 'SURVIVED'
                print(f"{mid}: {rec['status']} failed={rec['failed']} "
                      f"({time.time()-t0:.0f}s) — {area}", flush=True)
                if names:
                    print("      " + "; ".join(names[:6]), flush=True)
        finally:
            # Restore, then TOUCH — MSBuild keeps the sabotaged binary otherwise.
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original)
            os.utime(path, None)
            if os.path.exists(INFLIGHT):
                os.remove(INFLIGHT)
        rec['seconds'] = round(time.time() - t0)
        results.append(rec)
        json.dump(results, open(OUT, 'w'), indent=1)

    build()
    print("\n=== summary")
    for r in results:
        print(f"  {r['id']} {r['status']:>10}  {r['area']}")


if __name__ == '__main__':
    main()
