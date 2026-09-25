#!/usr/bin/env python3
"""A2m — the mutant set over `Core/Services/Analysis`.

WHY THIS AREA.

4,269 lines in 17 files and NOT ONE has ever been mutated (census in the 61st-pass memory).
It is also where the 61st pass's pin-key defect lived (`ChartPatternNavigator`): the layer
that decides WHICH BAR a formation is on, WHERE the comma/period keys stop, WHAT a pin does
to them, which side of price a level is on and how often it has held, and what the dossier
says about an asset before a trader buys it. None of it can be checked by looking.

SELECTION RULE (as A2j): one mutant per decision a blind trader would notice — a formation
found on the wrong bar, a jump key that skips one or stays put, a pinned formation that will
not release, a respect/dossier figure computed from the wrong window, a level on the wrong
side, a replay that reveals the wrong bar. Several are LOOKAHEADS (a value computed from bars
that were not yet closed), which are the sharpest defects here because they make a backtest
or a narration look better than reality and nothing on screen shows it.

SAMPLING FRAME: every file below is in Core/Services/Analysis; none appears in any earlier
a2*/a3 results file.

METHOD — identical to A2d..A2j so the numbers compare: apply one mutant, build, run the FULL
AccessibleTrader.Tests suite, record whether anything went red and WHICH tests did, restore,
touch. CAUGHT iff some test fails.

HARNESS RULES (sabotage-harness-rules):
  1. `touch` after restoring, or MSBuild keeps the sabotaged binary; REBUILD before exiting.
  2. "No test matches the given testcase filter" is a FAILURE (prove script).
  3. Assert the anchor is UNIQUE before patching (`--verify`, and again per mutant).
  4. Restore from a FILE COPY (scratchpad/a2m_backup/), never `git checkout --`.
     Unlike the A2j template, an interrupted run is recovered from that copy too.
  5. Nothing else edits the repo while this runs.
  6. NEVER `-v q --nologo` on `dotnet test` — failing names are the false-catch audit.
  7. Print the failing NAMES.
  8. Bookkeeping-guard catches are audited separately (A2h).
  9. Survivors are checked for equivalence before a test is written.
The whole run is niced (os.nice(10)); three other campaigns share the CPU.
"""
import filecmp, json, os, re, shutil, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2m_sabotage_results.json")
INFLIGHT = os.path.join(REPO, "scratchpad", "a2m_sabotage_inflight.txt")
BACKUP = os.path.join(REPO, "scratchpad", "a2m_backup")
SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+)")
BASELINE_TOTAL = 8088   # scratchpad/a2m_baseline.log, clean tree, 2026-09-24

A = "AccessibleTrader.Core/Services/Analysis/"

MUTANTS = [

    # ── ChartPatternNavigator: comma / period / semicolon ────────────────────
    ("M01", "the next-formation key can land on the bar it started from",
     A + "ChartPatternNavigator.cs",
     "                ? edges.Where(e => e > from).DefaultIfEmpty(-1).Min()",
     "                ? edges.Where(e => e >= from).DefaultIfEmpty(-1).Min()",
     "standing on a formation edge, period re-lands on the same bar and re-reads it: the key "
     "appears dead exactly where the user is most likely to press it"),

    ("M02", "the previous-formation key can land on the bar it started from",
     A + "ChartPatternNavigator.cs",
     "                : edges.Where(e => e < from).DefaultIfEmpty(-1).Max();",
     "                : edges.Where(e => e <= from).DefaultIfEmpty(-1).Max();",
     "comma on an edge stays put — walking backwards through formations stalls at the first stop"),

    ("M03", "an unresolved formation's notional expiry beyond the data becomes a stop",
     A + "ChartPatternNavigator.cs",
     "                if (end > p.KnownAtIndex && end < barCount) set.Add(end);",
     "                if (end > p.KnownAtIndex) set.Add(end);",
     "a formation still open at the right edge offers a bar that does not exist; the jump "
     "dispatches a navigation past the last bar — an event invented"),

    ("M04", "the formation START edges are dropped",
     A + "ChartPatternNavigator.cs",
     "                if (p.KnownAtIndex >= 0 && p.KnownAtIndex < barCount) set.Add(p.KnownAtIndex);",
     "",
     "only resolutions are stops; the bar a formation first became knowable — half of the "
     "story the jump keys exist to tell — is skipped"),

    ("M05", "a pin no longer scopes the jump keys (the reported defect restored)",
     A + "ChartPatternNavigator.cs",
     "            var scope = pinned != null ? new[] { pinned } : all;",
     "            var scope = all;",
     "'leading with ascending triangle' then, one keypress later, 'double bottom confirmed here'"),

    ("M06", "the jump keys move silently with formation description off",
     A + "ChartPatternNavigator.cs",
     "            if (!state.DescribeChartPatterns)\n            {\n                _eventBus.Publish(new FeedbackRequestEvent(\n",
     "            if (false)\n            {\n                _eventBus.Publish(new FeedbackRequestEvent(\n",
     "the cursor teleports and nothing says why — the user has lost their place and cannot "
     "tell whether the key worked"),

    ("M07", "Shift+semicolon says 'cleared' and leaves the pin in force",
     A + "ChartPatternNavigator.cs",
     "            bool had = _focus.Clear(chartKey);",
     "            bool had = _focus.IsPinned(chartKey);",
     "the pinned formation will not release: the jump keys stay confined to its two edges and "
     "the user is stuck between them (the 61st-pass report, reached by a different route)"),

    ("M08", "the pin announcement counts from zero",
     A + "ChartPatternNavigator.cs",
     "            int position = here.FindIndex(p => p.Key.Equals(picked.Key)) + 1;",
     "            int position = here.FindIndex(p => p.Key.Equals(picked.Key));",
     "'Leading with rising wedge, 0 of 3' — the position a user uses to know how many more "
     "semicolon presses remain is off by one"),

    # ── ChartPatternFocus ────────────────────────────────────────────────────
    ("M09", "a pin never reorders the readout",
     A + "ChartPatternFocus.cs",
     "                if (i <= 0) return ranked;",
     "                if (i <= ranked.Count) return ranked;",
     "semicolon announces 'leading with bull flag' and the next arrow key still leads with the "
     "largest formation — the pin changes nothing the user hears"),

    ("M10", "cycling stops at the last formation instead of wrapping",
     A + "ChartPatternFocus.cs",
     "                int next = (current + 1) % ranked.Count;",
     "                int next = Math.Min(current + 1, ranked.Count - 1);",
     "semicolon walks to the smallest formation and then sticks there; the largest can never be "
     "re-pinned without clearing first"),

    ("M11", "clearing the pin on one chart clears it on every chart",
     A + "ChartPatternFocus.cs",
     "            lock (_gate) return _pinned.Remove(chartKey);",
     "            lock (_gate) { bool had = _pinned.Remove(chartKey); _pinned.Clear(); return had; }",
     "Shift+semicolon on the BTC tab silently drops the formation pinned on the ETH tab"),

    # ── ChartPatternCache ────────────────────────────────────────────────────
    ("M12", "the cache key forgets the timeframe",
     A + "ChartPatternCache.cs",
     "            => $\"{id.Market}|{id.Provider}|{id.Symbol}|{id.Timeframe}\";",
     "            => $\"{id.Market}|{id.Provider}|{id.Symbol}\";",
     "the 1h and 4h charts of one symbol share an entry; with equal bar counts and a shared "
     "last timestamp the second chart describes the first chart's formations"),

    # ── ChartPatternNarrator: what the arrow keys say ────────────────────────
    ("M13", "the bar a formation became knowable is called 'Inside' rather than 'Start of'",
     A + "ChartPatternNarrator.cs",
     "            if (barIndex <= p.KnownAtIndex) return \"Start of\";",
     "            if (barIndex < p.KnownAtIndex) return \"Start of\";",
     "the jump key lands on the start edge and the announcement says the user is already inside"),

    ("M14", "the resolution bar is called 'Inside' rather than 'End of'",
     A + "ChartPatternNarrator.cs",
     "            if (barIndex >= p.ResolvesAt) return \"End of\";",
     "            if (barIndex > p.ResolvesAt) return \"End of\";",
     "landing on the break bar does not say the story ended there"),

    ("M15", "the confirmation bar itself still reads 'forming'",
     A + "ChartPatternNarrator.cs",
     "                return barIndex >= done ? p : p with { State = ChartPatternState.Forming };",
     "                return barIndex > done ? p : p with { State = ChartPatternState.Forming };",
     "a formation found on the wrong bar: the confirmation is announced one bar late"),

    ("M16", "an expired formation reads 'did not confirm' before it expired",
     A + "ChartPatternNarrator.cs",
     "                return barIndex >= p.RelevanceEndsAt ? p : p with { State = ChartPatternState.Forming };",
     "                return barIndex >= p.KnownAtIndex ? p : p with { State = ChartPatternState.Forming };",
     "lookahead in the narration: panning back, every bar of a live formation already says it "
     "failed — information from the future"),

    ("M17", "the dominance ranking puts resolved formations ahead of live ones",
     A + "ChartPatternNarrator.cs",
     "                .OrderBy(p => p.State == ChartPatternState.Forming ? 0 : 1)",
     "                .OrderBy(p => p.State == ChartPatternState.Forming ? 1 : 0)",
     "the readout leads with a formation that is already over instead of the one still in play"),

    ("M18", "a formation is described from its first bar, before it was knowable",
     A + "ChartPatternNarrator.cs",
     "            => all.Where(p => barIndex >= p.KnownAtIndex && barIndex <= p.ResolvesAt)",
     "            => all.Where(p => barIndex >= p.StartBarIndex && barIndex <= p.ResolvesAt)",
     "lookahead: arrowing through history announces a double top at its first peak, bars "
     "before the second peak existed"),

    ("M19", "the resolution bar drops out of the formation's lifetime",
     A + "ChartPatternNarrator.cs",
     "            => all.Where(p => barIndex >= p.KnownAtIndex && barIndex <= p.ResolvesAt)",
     "            => all.Where(p => barIndex >= p.KnownAtIndex && barIndex < p.ResolvesAt)",
     "the break bar — the bar the period key lands on — says nothing about the formation"),

    ("M20", "the break side is spoken backwards",
     A + "ChartPatternNarrator.cs",
     "            string side = p.BreaksBelow ? \"below\" : \"above\";",
     "            string side = p.BreaksBelow ? \"above\" : \"below\";",
     "'Double top: price closed above the neckline' — a directional claim inverted"),

    ("M21", "'inside a larger X' names the widest container, not the tightest",
     A + "ChartPatternNarrator.cs",
     "                .OrderBy(c => c.EndBarIndex - c.StartBarIndex)\n                .FirstOrDefault();",
     "                .OrderByDescending(c => c.EndBarIndex - c.StartBarIndex)\n                .FirstOrDefault();",
     "a flag inside a triangle inside a range is said to be inside the range"),

    # ── ChartPatternDetector ─────────────────────────────────────────────────
    ("M22", "a double top is known at its second peak, before the peak is confirmed",
     A + "ChartPatternDetector.cs",
     "                int known = Math.Max(b.ConfirmedAtIndex, trough.ConfirmedAtIndex);",
     "                int known = b.BarIndex;",
     "lookahead: the formation is announced on the bar of the peak, Span bars before anyone "
     "could know it was a peak"),

    ("M23", "a wick confirms a formation, not a close",
     A + "ChartPatternDetector.cs",
     "                bool through = breakBelow ? bars[i].Close < trigger : bars[i].Close > trigger;",
     "                bool through = breakBelow ? bars[i].Low < trigger : bars[i].High > trigger;",
     "a stop-hunt wick through the neckline is announced as the break, bars before the real one"),

    ("M24", "confirmation is searched from bar 0, not from when the formation was known",
     A + "ChartPatternDetector.cs",
     "            int last = Math.Min(expiresAt, bars.Count - 1);\n            for (int i = knownAt; i <= last; i++)\n            {\n                bool through",
     "            int last = Math.Min(expiresAt, bars.Count - 1);\n            for (int i = 0; i <= last; i++)\n            {\n                bool through",
     "a close beyond the trigger BEFORE the formation existed completes it — a break found on "
     "the wrong bar, in the past"),

    ("M25", "flat top + rising lows is named a descending triangle",
     A + "ChartPatternDetector.cs",
     "                    (0, +1) => ChartPatternKind.AscendingTriangle,",
     "                    (0, +1) => ChartPatternKind.DescendingTriangle,",
     "the shape's name — and with it the side it is expected to break — inverted"),

    ("M26", "a falling wedge is expected to break down",
     A + "ChartPatternDetector.cs",
     "                bool breakBelow = kind is ChartPatternKind.DescendingTriangle\n                                       or ChartPatternKind.RisingWedge;",
     "                bool breakBelow = kind is ChartPatternKind.DescendingTriangle\n                                       or ChartPatternKind.FallingWedge;",
     "trigger level on the wrong side for both wedges"),

    ("M27", "a range broken to the downside is reported as broken upward",
     A + "ChartPatternDetector.cs",
     "                if (bars[i].Close < bottom) return (ChartPatternState.Completed, i, true);",
     "                if (bars[i].Close < bottom) return (ChartPatternState.Completed, i, false);",
     "'Range broken: price closed above the top' on a breakdown, with the target above"),

    ("M28", "a flag's measured target is projected the wrong way",
     A + "ChartPatternDetector.cs",
     "                    double target = trigger + pole;",
     "                    double target = trigger - pole;",
     "a bull flag's target is spoken below the flag"),

    # ── SwingStructureAnalyzer ───────────────────────────────────────────────
    ("M29", "a swing is confirmed on its own bar (no Span lag)",
     A + "SwingStructureAnalyzer.cs",
     "                int confirmed = p.Index + opts.Span;",
     "                int confirmed = p.Index;",
     "lookahead: a pivot needs Span bars AFTER it; claiming it on its own bar feeds every "
     "formation, structure state and backtest information from the future"),

    ("M30", "a less extreme same-kind pivot supersedes a more extreme one",
     A + "SwingStructureAnalyzer.cs",
     "                    bool supersedes = p.IsHigh ? p.Price > last.Price : p.Price < last.Price;",
     "                    bool supersedes = p.IsHigh ? p.Price < last.Price : p.Price > last.Price;",
     "the carried swing high drops to a lower high inside the same move"),

    ("M31", "higher lows are called lower lows",
     A + "SwingStructureAnalyzer.cs",
     "                        : p.Price > prevLow ? SwingLabel.HigherLow : SwingLabel.LowerLow;",
     "                        : p.Price > prevLow ? SwingLabel.LowerLow : SwingLabel.HigherLow;",
     "an uptrend is described as a downtrend by its lows"),

    # ── LevelRespectAnalyzer / RespectModels ─────────────────────────────────
    ("M32", "a touch's side is judged by the bar that pierced the line",
     A + "LevelRespectAnalyzer.cs",
     "                double prevClose = bars[i - 1].Close;",
     "                double prevClose = bars[i].Close;",
     "a support test that closed below the line is counted as a resistance test"),

    ("M33", "a support is 'broken' by any close under line + break distance",
     A + "LevelRespectAnalyzer.cs",
     "                    if (bar.Close < lineAtJ - breakDistance)",
     "                    if (bar.Close < lineAtJ + breakDistance)",
     "every support touch that closes near the line is counted as a break — hold rate collapses"),

    ("M34", "support holds and resistance holds are swapped",
     A + "LevelRespectAnalyzer.cs",
     "                touches.Count(t => t.Held && t.FromAbove),",
     "                touches.Count(t => t.Held && !t.FromAbove),",
     "'held as support 7 times' for a line that held as resistance 7 times"),

    ("M35", "the reaction window is unbounded",
     A + "LevelRespectAnalyzer.cs",
     "            int end = Math.Min(bars.Count - 1, touchIdx + opts.ReactionWindowBars);",
     "            int end = bars.Count - 1;",
     "a respect figure computed from the wrong window: a move 200 bars later counts as the "
     "touch's bounce"),

    ("M36", "the hold-rate score drops its add-4 smoothing",
     A + "RespectModels.cs",
     "                double shrunk = (Holds + 2.0) / (Touches + 4.0);",
     "                double shrunk = (double)Holds / Touches;",
     "a line touched once and held once outranks a line with 18 holds in 20"),

    ("M37", "a higher-timeframe MA reads its still-forming bucket",
     A + "MaRespectRanker.cs",
     "                int lastClosed = j - 1;",
     "                int lastClosed = j;",
     "lookahead: every bar of the day sees that day's closing EMA value"),

    # ── LevelProvenanceService ───────────────────────────────────────────────
    ("M38", "levels above price are narrated as below",
     A + "LevelProvenanceService.cs",
     "            var below = reliable.Where(s => s.CurrentValue < close).Take(topN).ToList();",
     "            var below = reliable.Where(s => s.CurrentValue > close).Take(topN).ToList();",
     "'below: 200 EMA at 64,000' with price at 60,000 — a level on the wrong side"),

    ("M39", "the prior-period high is the CURRENT period's high",
     A + "LevelProvenanceService.cs",
     "                    highs[i] = htf[j - 1].High;",
     "                    highs[i] = htf[j].High;",
     "lookahead: 'prior day high' is today's eventual high, known from today's first bar"),

    # ── LevelPolarity ────────────────────────────────────────────────────────
    ("M40", "a level exactly at price is called support",
     A + "LevelPolarity.cs",
     "        public static bool IsResistance(double level, double referencePrice) => level >= referencePrice;",
     "        public static bool IsResistance(double level, double referencePrice) => level > referencePrice;",
     "the documented tie-break (ON the price = resistance) flips; drawn levels and the zone "
     "announcement change a case that was previously right"),

    # ── ValueDeviationAnalyzer ───────────────────────────────────────────────
    ("M41", "a bar's value profile includes the bar itself",
     A + "ValueDeviationAnalyzer.cs",
     "                var (p, h, l) = BuildProfile(bars, i - window, i);",
     "                var (p, h, l) = BuildProfile(bars, i - window + 1, i + 1);",
     "lookahead: the value area a bar is measured against already contains that bar's range"),

    # ── ReplayService ────────────────────────────────────────────────────────
    ("M42", "replay starts one bar short of the cursor",
     A + "ReplayService.cs",
     "                _revealed = Math.Clamp(startIndex + 1, MinRevealed, _history.Count - 1);",
     "                _revealed = Math.Clamp(startIndex, MinRevealed, _history.Count - 1);",
     "the bar the user was standing on when they started replay is hidden"),

    ("M43", "the cursor parks one past the newest revealed bar",
     A + "ReplayService.cs",
     "            _store.Dispatch(new NavigateAction(revealed - 1));",
     "            _store.Dispatch(new NavigateAction(revealed));",
     "after F9 the readout describes a bar that is not revealed (or nothing at all)"),

    # ── AssetDossierService ──────────────────────────────────────────────────
    ("M44", "'position in loaded range' is measured from the top",
     A + "AssetDossierService.cs",
     "            double pos = hi > lo ? (last.Close - lo) / (hi - lo) * 100 : 50;",
     "            double pos = hi > lo ? (hi - last.Close) / (hi - lo) * 100 : 50;",
     "an asset at its high is '0% of the way up'"),

    ("M45", "the 90-day filing window is a 900-day window",
     A + "AssetDossierService.cs",
     "            var cutoff = DateTime.UtcNow.AddDays(-90);",
     "            var cutoff = DateTime.UtcNow.AddDays(-900);",
     "a dossier figure from the wrong window: 'insider filings, last 90 days' counts three "
     "years, and 'none' — the informative case — is never said"),
]


def run(cmd, cwd=REPO, timeout=3600):
    p = subprocess.run(cmd, cwd=cwd, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def build():
    code, out = run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                    "-p:UseRazorSourceGenerator=false -v q --nologo")
    return code == 0, out


def test(extra=""):
    return run("dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
               "-p:UseRazorSourceGenerator=false --no-build " + extra)


def failing_names(out):
    return sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))


def backup_path(relpath):
    return os.path.join(BACKUP, relpath.replace("/", "__"))


def restore(relpath):
    path = os.path.join(REPO, relpath)
    shutil.copyfile(backup_path(relpath), path)
    os.utime(path, None)
    same = filecmp.cmp(path, backup_path(relpath), shallow=False)
    if not same:
        print(f"!!! {relpath} NOT byte-identical after restore", flush=True)
    return same


def recover_inflight():
    if not os.path.exists(INFLIGHT):
        return
    rel = open(INFLIGHT).read().strip()
    if rel and os.path.exists(backup_path(rel)):
        restore(rel)
        print(f"recovered stale sabotage in {rel} from its file copy", flush=True)
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
    ids = [m[0] for m in MUTANTS]
    if len(ids) != len(set(ids)):
        print("DUPLICATE MUTANT IDS"); ok = False
    print(f"all {len(MUTANTS)} anchors unique" if ok else "ANCHOR CHECK FAILED")
    return ok


def main():
    try:
        os.nice(10)
    except OSError:
        pass

    if "--verify" in sys.argv:
        sys.exit(0 if verify() else 1)

    if "--baseline" in sys.argv:
        ok, log = build()
        print("BUILD", "OK" if ok else "FAILED\n" + log[-2000:], flush=True)
        code, out = test()
        open(os.path.join(REPO, "scratchpad", "a2m_baseline.log"), "w").write(out)
        m = SUMMARY_RE.search(out)
        print(f"BASELINE: {m.group(0) if m else 'UNPARSED'} exit={code}")
        for n in failing_names(out):
            print("   FAILED", n)
        return

    os.makedirs(BACKUP, exist_ok=True)
    recover_inflight()
    if not verify():
        sys.exit(1)

    # File copies of every file any mutant touches, taken ONCE from the clean tree.
    for rel in sorted({m[2] for m in MUTANTS}):
        bp = backup_path(rel)
        if not os.path.exists(bp):
            shutil.copyfile(os.path.join(REPO, rel), bp)

    only = [a for a in sys.argv[1:] if re.match(r"^M\d+$", a)] or None
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
                names = failing_names(out)
                rec['failing_tests'] = names[:60]
                rec['status'] = ('UNPARSED' if rec['failed'] < 0
                                 else 'CAUGHT' if rec['failed'] > 0 else 'SURVIVED')
                # HARNESS RULE 10 (learned on M23 in this campaign): a test host that ABORTS prints
                # a summary for the tests it reached — "Failed: 0, Passed: 4634" — and that parses
                # as a survivor. Anything short of the baseline total is an abort, and an abort is
                # something the suite noticed, so it is recorded separately and audited by hand.
                if rec['failed'] >= 0 and rec['failed'] + rec['passed'] < BASELINE_TOTAL:
                    rec['status'] = 'ABORTED'
                    rec['log_tail'] = out[-4000:]
                print(f"{mid}: {rec['status']} failed={rec['failed']} ({time.time()-t0:.0f}s) — {area}", flush=True)
                if names:
                    print("      " + "; ".join(names[:6]), flush=True)
        finally:
            rec['restored_identical'] = restore(relpath)
            if os.path.exists(INFLIGHT):
                os.remove(INFLIGHT)
        rec['seconds'] = round(time.time() - t0)
        results.append(rec); json.dump(results, open(OUT, 'w'), indent=1)

    build()
    code, out = test()
    m = SUMMARY_RE.search(out)
    ctrl = m.group(0) if m else 'UNPARSED'
    print(f"\n=== CONTROL (nothing sabotaged): {ctrl}", flush=True)
    for n in failing_names(out):
        print("   CONTROL FAILED", n, flush=True)

    all_same = all(filecmp.cmp(os.path.join(REPO, rel), backup_path(rel), shallow=False)
                   for rel in {m[2] for m in MUTANTS})
    code, diff = run("git diff --stat -- AccessibleTrader.Core/")
    print("restored byte-identical" if all_same and not diff.strip()
          else f"TREE NOT RESTORED:\n{diff}", flush=True)

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
