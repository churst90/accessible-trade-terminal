#!/usr/bin/env python3
"""A2d — the THIRD independently-chosen mutant set (campaign run 2026-08-30).

WHY THIS EXISTS. A2 (2026-08-26) measured 61%; A2b re-ran A2's own mutants and
measured 89.3% — an upper bound, because the intervening test work was written
against A2's published survivor list. A2c (2026-08-29) picked 28 fresh mutants and
measured 67.9%, and all nine survivors were killed the same day. So the 67.9%
number is now stale in exactly the way 89.3% was: the suite has since been taught
those nine properties. This set is chosen blind of BOTH previous lists.

THE SAMPLING FRAME, stated up front because the comparison turns on it.
  * A2's 28 clustered in money and speech hot paths (trading, speech formatting,
    indicator math, theming, the sandbox blocklist).
  * A2c's 28 covered analysis (levels/swings/patterns), providers, OHLCV storage,
    CSV import, viewport/tabs, managed exits, the outbound network guard, hosted
    alerting, audio, and screening.
  * THIS set touches none of the 47 files those two mutated. It goes to the
    strategy-execution and research stack (risk plans, condition evaluation,
    position management, bootstrap CIs, permutation p-values), the drawing
    calculators, the Dot Pad tactile driver, the plugin SDK's shared services,
    per-user path scoping in the WebHost, and session autosave.

    Core/Services/Strategies (risk, conditions, positions)  9    D01-D08, D25
    Sdk/Services (shared plugin infrastructure)             4    D17,D19,D20,D24
    Core/Services/Indicators (untouched providers)          3    D09-D11
    Core/Services/Drawing/Calculators                       2    D13,D14
    StrategyLab statistics                                  2    D15,D16
    WebHost per-user scoping + tray                         2    D18,D22
    Core/Services/Screening                                 1    D21
    Core/Services/Accessibility/Dotpad                      1    D23
    Core/Services/Workspace                                 1    D26
    Core/Services/Strategies (trade ranking)                1    D12

Every mutant is a single-line change that compiles and that a user would
experience — no equivalent mutants, no dead-code edits, no comment changes.

METHOD, identical to a2_sabotage.py / a2b_sabotage.py / fresh_sabotage.py so the
numbers compare: apply one mutant, build, run the FULL suite, record whether
anything went red AND WHICH TESTS DID, revert, touch. CAUGHT iff some test fails.

TRAPS CARRIED IN FROM THE PREVIOUS RUNS:
  * A2 had FIVE mutants come back falsely CAUGHT by one unrelated flaky test
    firing alone. Every single-test catch here must be re-run in isolation, both
    directions, before it counts.
  * A killed campaign leaves a sabotaged file in the tree. `finally` does not run
    on SIGKILL, so an IN-FLIGHT marker is written before each edit and any stale
    one is restored from git at startup.
  * Run this DETACHED with setsid. A tracked background command is capped at ten
    minutes; this takes over an hour.

The campaign runs `AccessibleTrader.Tests` only. A kill that lives in
`AccessibleTrader.BrowserTests` still scores as a SURVIVOR here — deliberate, and
what A2/A2b/A2c measured too.
"""
import json, os, re, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2d_sabotage_results.json")
INFLIGHT = os.path.join(REPO, "scratchpad", "a2d_sabotage_inflight.txt")

# (id, area, file, find, replace, what-it-breaks)
MUTANTS = [
    # ── Core/Services/Strategies: the research gate ─────────────────────────
    ("D01", "R multiple sign for shorts",
     "AccessibleTrader.Core/Services/Strategies/BootstrapCi.cs",
     "        return t.Side == OrderSide.Sell ? -raw : raw;",
     "        return raw;",
     "every short trade's R is reported with the wrong sign — losing shorts count as winners"),

    ("D02", "bootstrap CI lower bound",
     "AccessibleTrader.Core/Services/Strategies/BootstrapCi.cs",
     "        int loIdx = (int)Math.Floor(0.025 * (iterations - 1));",
     "        int loIdx = (int)Math.Floor(0.5 * (iterations - 1));",
     "the 95% CI lower bound is the median — the survivor gate becomes 'mean > 0'"),

    ("D03", "reward:risk gate inverted",
     "AccessibleTrader.Core/Services/Strategies/RiskPlanResolver.cs",
     "            if (rr < plan.MinRewardRiskRatio) return null;",
     "            if (rr > plan.MinRewardRiskRatio) return null;",
     "only trades that MEET the minimum reward:risk are refused; the rest are taken"),

    ("D04", "over-allocated exit ladder",
     "AccessibleTrader.Core/Services/Strategies/RiskPlanResolver.cs",
     "            if (portionSum > 1.0)",
     "            if (portionSum > 100.0)",
     "a ladder summing to 150% of the position is passed through unnormalised"),

    ("D05", "scored condition group threshold",
     "AccessibleTrader.Core/Services/Strategies/ConditionEvaluator.cs",
     "                        return subtreeScore >= group.ScoreThreshold.Value;",
     "                        return subtreeScore >= 0;",
     "a Score group fires at any score — the user's threshold is ignored"),

    ("D06", "backtest warmup window",
     "AccessibleTrader.Core/Services/Strategies/BacktestWarmupAnalyzer.cs",
     "                if (window > maxWindow) maxWindow = window;",
     "                if (window > maxWindow * 1000) maxWindow = window;",
     "warmup falls back to the floor — a 200-period indicator signals from bar 60"),

    ("D07", "re-signal while already open",
     "AccessibleTrader.Core/Services/Strategies/StrategyPositionManager.cs",
     "                if (open.Side == signal.Side)",
     "                if (open.Side != signal.Side)",
     "a same-side re-signal opens a SECOND position; an opposite signal is swallowed"),

    ("D08", "breakeven stop before TP1",
     "AccessibleTrader.Core/Services/Strategies/StrategyPositionManager.cs",
     "                    if (p.FirstTargetFilled && p.StopAdjust == StopAdjustOnTp1.MoveToBreakeven)",
     "                    if (p.StopAdjust == StopAdjustOnTp1.MoveToBreakeven)",
     "the stop is yanked to entry on the entry fill, before the first target is hit"),

    ("D25", "higher-timeframe look-ahead",
     "AccessibleTrader.Core/Services/Strategies/ConditionEvaluator.cs",
     "                if (htfBars[mid].Date < mainBarDate) lo = mid + 1;",
     "                if (htfBars[mid].Date <= mainBarDate) lo = mid + 1;",
     "the still-forming HTF bar is visible to every multi-timeframe leaf — look-ahead"),

    ("D12", "trade rank pivot alignment",
     "AccessibleTrader.Core/Services/Strategies/TradeRanker.cs",
     "                double zoneAlign = ctx.Side == OrderSide.Buy ? -ctx.PivotZone : ctx.PivotZone;",
     "                double zoneAlign = ctx.Side == OrderSide.Buy ? ctx.PivotZone : -ctx.PivotZone;",
     "a long at resistance scores highest — the confidence score is anti-correlated"),

    # ── Core/Services/Indicators: providers A2/A2c never touched ────────────
    ("D09", "rolling quantile warmup",
     "AccessibleTrader.Core/Services/Indicators/RollingQuantile.cs",
     "                if (count < warmupMin) continue;",
     "                if (count < 1) continue;",
     "adaptive thresholds emit from the first bar, computed from one sample"),

    ("D10", "uncomputable indicator refusal",
     "AccessibleTrader.Core/Services/Indicators/IndicatorComputability.cs",
     '            if (!provider.GetType().Name.StartsWith("Skender", StringComparison.Ordinal)) return null;',
     '            if (!provider.GetType().Name.StartsWith("Skenderr", StringComparison.Ordinal)) return null;',
     "no indicator is ever checked — PPO/HV/TMA are offered again and are permanently NaN"),

    ("D11", "pivot zone conjunction",
     "AccessibleTrader.Core/Services/Indicators/PivotLevelsProvider.cs",
     "                            zoneSpan[j] = atR && !atS ?  1.0\n"
     "                                        : atS && !atR ? -1.0",
     "                            zoneSpan[j] = atR ?  1.0\n"
     "                                        : atS ? -1.0",
     "a bar inside both a resistance and a support zone is announced as resistance"),

    # ── Core/Services/Drawing/Calculators ───────────────────────────────────
    ("D13", "risk:reward ratio inverted",
     "AccessibleTrader.Core/Services/Drawing/Calculators/RiskRewardCalculator.cs",
     "            drawing.RiskRewardRatio = risk > 0 ? reward / risk : 0;",
     "            drawing.RiskRewardRatio = reward > 0 ? risk / reward : 0;",
     "a 3:1 setup is announced as 0.33:1 — the ratio the user hears is the reciprocal"),

    ("D14", "fib retracement direction",
     "AccessibleTrader.Core/Services/Drawing/Calculators/FibRetracementCalculator.cs",
     "                double price = p1 - (diff * level);",
     "                double price = p1 + (diff * level);",
     "every fib level is mirrored to the wrong side of the first anchor"),

    # ── StrategyLab statistics ──────────────────────────────────────────────
    ("D15", "permutation test becomes one-tailed",
     "AccessibleTrader.StrategyLab/LabStats.cs",
     "            if (System.Math.Abs(a / nA - b / nB) >= System.Math.Abs(observed)) extreme++;",
     "            if (System.Math.Abs(a / nA - b / nB) >= observed) extreme++;",
     "a negative observed effect makes every permutation extreme — p collapses"),

    ("D16", "block permutation minimum blocks",
     "AccessibleTrader.StrategyLab/LabStats.cs",
     "        if (blocks.Count < 4) return 1.0;",
     "        if (blocks.Count < 0) return 1.0;",
     "a p-value is reported from one or two blocks — autocorrelation is unaccounted for"),

    # ── Sdk/Services: shared plugin infrastructure ──────────────────────────
    ("D17", "symbol path traversal",
     "AccessibleTrader.Sdk/Services/SymbolValidator.cs",
     '            if (symbol.Contains("..", StringComparison.Ordinal)) return false;',
     '            if (symbol.Contains("...", StringComparison.Ordinal)) return false;',
     "a symbol containing '..' is accepted and reaches cache path construction"),

    ("D19", "rate limit window cap",
     "AccessibleTrader.Sdk/Services/RateLimiter.cs",
     "                if (_requestCount >= _maxRequestsPerWindow)",
     "                if (_requestCount >= _maxRequestsPerWindow * 1000)",
     "the per-window request cap never applies — the venue bans the key"),

    ("D20", "4xx responses retried",
     "AccessibleTrader.Sdk/Services/RateLimiter.cs",
     "                if (code >= 400 && code < 500)",
     "                if (code >= 400 && code < 400)",
     "a 401 or 403 is retried with backoff instead of failing fast"),

    ("D24", "signed query parameters unescaped",
     "AccessibleTrader.Sdk/Services/RestSigning.cs",
     '            string.Join("&", parameters.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));',
     '            string.Join("&", parameters.Select(kv => $"{kv.Key}={kv.Value}"));',
     "a parameter containing '&' or '+' signs one message and sends another"),

    # ── WebHost: per-user scoping and the tray ──────────────────────────────
    ("D18", "per-user directory sanitisation",
     "AccessibleTrader.WebHost/Services/UserScopedPathService.cs",
     "            var clean = new string((s ?? \"anon\").Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());",
     "            var clean = new string((s ?? \"anon\").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '/').ToArray());",
     "a crafted data key escapes its own users/ directory into another account's"),

    ("D22", "tray unread alert count",
     "AccessibleTrader.WebHost/Services/RecentAlertsBuffer.cs",
     "            get { lock (_lock) return _items.Count(i => i.State == RecentAlertState.Unread); }",
     "            get { lock (_lock) return _items.Count(i => i.State != RecentAlertState.Unread); }",
     "the tray badge counts the alerts already read — unread alerts read as zero"),

    # ── Core/Services/Screening ─────────────────────────────────────────────
    ("D21", "screener insufficient history",
     "AccessibleTrader.Core/Services/Screening/ScreenerService.cs",
     "                if (bars == null || bars.Count < 2)",
     "                if (bars == null)",
     "a symbol with one bar is evaluated instead of reported InsufficientHistory"),

    # ── Core/Services/Accessibility/Dotpad ──────────────────────────────────
    ("D23", "braille cell bit order",
     "AccessibleTrader.Core/Services/Accessibility/Dotpad/DotpadTactileDriver.cs",
     "                    int bit = subY + (subX * DotsPerCellHeight); // left column = bits 0..3, right column = bits 4..7",
     "                    int bit = subX + (subY * DotsPerCellWidth); // left column = bits 0..3, right column = bits 4..7",
     "every tactile cell is transposed — the Dot Pad renders a scrambled chart"),

    # ── Core/Services/Workspace ─────────────────────────────────────────────
    ("D26", "autosave prune keeps the wrong slot",
     "AccessibleTrader.Core/Services/Workspace/SessionAutosaveService.cs",
     "                if (stale.Name == _slotName) continue;",
     "                if (stale.Name != _slotName) continue;",
     "the prune deletes THIS session's slot and keeps every stale one"),
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
    only = [a for a in sys.argv[1:] if a.startswith("D")] or None
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
