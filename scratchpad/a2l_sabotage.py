#!/usr/bin/env python3
"""A2l — the ELEVENTH mutant set: `Core/Services/Strategies`.

WHY THIS AREA.

6,408 lines in 38 files. Across every prior campaign exactly SIX were ever mutated, all in A2d
(BacktestWarmupAnalyzer, BootstrapCi, ConditionEvaluator, RiskPlanResolver,
StrategyPositionManager, TradeRanker). The SAMPLING FRAME here is the other 32: the modal
coordinator, the script-strategy causality probe, the builder's editable spec, its narrator and
validator, the setup sonifier, the bundle importer, the library facade and JSON store, the MTF
data cache, the auto-loader, the signal catalogue, the lab runner, the plugin/strategy
registries, the level service and the five level providers. AssetClassifier is excluded: no
production caller (tests only), so no trader can notice it.

SELECTION RULE: one mutant per decision a trader would notice — a stop placed at the wrong level,
a strategy that runs twice or starts by itself, a look-ahead that stops being refused, a position
sized from the wrong field, a refusal that stops being said.

METHOD — identical to A2j: apply one mutant, build, run the FULL AccessibleTrader.Tests suite,
record CAUGHT/SURVIVED and the failing test NAMES, restore from a FILE COPY, touch, diff.

HARNESS RULES (sabotage-harness-rules.md): touch after restore; unique anchors asserted first;
restore from a file copy (never git checkout); never `-v q` on dotnet test; print names; audit
catches for bookkeeping guards and flakes; check survivors for equivalence; control run at end;
every file diffed byte-identical against its backup at the end.
"""
import filecmp, json, os, re, shutil, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2l_sabotage_results.json")
BACKUP = os.path.join(REPO, "scratchpad", "a2l_backup")
INFLIGHT = os.path.join(REPO, "scratchpad", "a2l_sabotage_inflight.txt")
SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+)")

S = "AccessibleTrader.Core/Services/Strategies/"
L = S + "Levels/"

MUTANTS = [
    # ── StrategyModalCoordinator ─────────────────────────────────────────────
    ("L01", "Start runs a second copy of a spec already running",
     S + "StrategyModalCoordinator.cs",
     "                RemoveExistingInstancesOfSpec(specId);\n\n                var strategy = _factory.Create(spec);",
     "\n                var strategy = _factory.Create(spec);",
     "pressing Start on a running spec adds a twin: two instances on the same bars, every entry doubled"),

    ("L02", "Stop leaves the spec armed to auto-start on restart",
     S + "StrategyModalCoordinator.cs",
     "                    _library.Upsert(spec with { IsAutoActivate = false });\n                return StrategyCoordinatorResult.Ok(\"Stopped.\");",
     "                    { }\n                return StrategyCoordinatorResult.Ok(\"Stopped.\");",
     "the user stops a strategy, restarts the app, and it is trading again"),

    ("L03", "Pause and resume swap",
     S + "StrategyModalCoordinator.cs",
     "_engine.PauseStrategy(instanceId, !currentlyPaused);",
     "_engine.PauseStrategy(instanceId, currentlyPaused);",
     "pressing Pause on a running strategy leaves it running while the UI says 'Paused.'"),

    ("L04", "a compiled script strategy is not re-armed after restart",
     S + "StrategyModalCoordinator.cs",
     "                        IsAutoActivate: true,\n                        RoslynSource: code);",
     "                        IsAutoActivate: false,\n                        RoslynSource: code);",
     "the script the user added is saved but silently absent after the next launch"),

    # ── ScriptStrategyCausalityProbe ─────────────────────────────────────────
    ("L05", "a stop that reads the future is not part of the order fingerprint",
     S + "ScriptStrategyCausalityProbe.cs",
     "            Num(sb, s.StopLoss); sb.Append('|');",
     "            sb.Append('|');",
     "a script whose stop is placed from state.Data[i+1] loads — the entry is honest, the stop is clairvoyant"),

    ("L06", "the suffix check stops ignoring the warmup of the shorter run",
     S + "ScriptStrategyCausalityProbe.cs",
     "var dates = shortRun.Dates.Skip(SuffixWarmup).ToList();",
     "var dates = shortRun.Dates.ToList();",
     "an honest stateful strategy (an EMA cross) is refused as 'anchored to the array start'"),

    ("L07", "an order present in one run and absent in the other is not a difference",
     S + "ScriptStrategyCausalityProbe.cs",
     "                if (string.Equals(x, y, StringComparison.Ordinal)) continue;",
     "                if (x is null || y is null || string.Equals(x, y, StringComparison.Ordinal)) continue;",
     "the commonest shape of look-ahead — a trade that only exists when the future is loaded — passes"),

    ("L08", "findings are reported but the strategy still loads",
     S + "ScriptStrategyCausalityProbe.cs",
     "return new ScriptStrategyCausalityReport(id, Refused: true, findings, notes, signalsSeen);",
     "return new ScriptStrategyCausalityReport(id, Refused: false, findings, notes, signalsSeen);",
     "a look-ahead script is described and then allowed to trade"),

    ("L09", "the prefix (look-ahead) comparison checks nothing",
     S + "ScriptStrategyCausalityProbe.cs",
     "                    var diff = FirstDifference(shortRun, whole, shortRun.Dates, skip: 0);",
     "                    var diff = FirstDifference(shortRun, whole, shortRun.Dates, skip: shortRun.Dates.Count);",
     "state.Data[i + 1].Close compiles, runs and backtests like genius — and loads"),

    # ── EditableStrategySpec ─────────────────────────────────────────────────
    ("L10", "editing a saved strategy saves a duplicate under a new id",
     S + "EditableStrategySpec.cs",
     "string id = string.IsNullOrEmpty(LoadedId) ? Guid.NewGuid().ToString(\"N\") : LoadedId;",
     "string id = Guid.NewGuid().ToString(\"N\");",
     "every Save of an edited spec adds another copy; the running one keeps the old rules"),

    ("L11", "fixed quantity and cash risk swap on save",
     S + "EditableStrategySpec.cs",
     "new PositionSizing(SizingMode, RiskPercent, FixedQuantity, RiskCash)",
     "new PositionSizing(SizingMode, RiskPercent, RiskCash, FixedQuantity)",
     "a fixed-quantity strategy of 1 contract trades 100 (the cash-risk default)"),

    ("L12", "the stop buffer is lost when a spec is loaded for editing",
     S + "EditableStrategySpec.cs",
     "            StopBuffer = spec.Risk.Stop.BufferTicks;\n",
     "",
     "open, change the name, save: the stop now sits exactly on the swing low, no buffer"),

    ("L13", "the level-strength gate is dropped on save",
     S + "EditableStrategySpec.cs",
     "                                n.MinLevelStrength);",
     "                                0.0);",
     "a 'rejects a strong level' leaf trades every weak pivot too"),

    # ── StrategySpecNarrator ─────────────────────────────────────────────────
    ("L14", "the narration says Long for a short setup",
     S + "StrategySpecNarrator.cs",
     "sb.Append(spec.Side == OrderSide.Buy ? \"Long \" : \"Short \");",
     "sb.Append(spec.Side != OrderSide.Buy ? \"Long \" : \"Short \");",
     "the by-ear verification before saving names the wrong side"),

    ("L15", "the narrated risk is a hundred times too small",
     S + "StrategySpecNarrator.cs",
     "sb.Append(\"Risking \").Append((spec.RiskPercent * 100).ToString(\"F2\"))",
     "sb.Append(\"Risking \").Append((spec.RiskPercent).ToString(\"F2\"))",
     "0.5% risk is read as 'Risking 0.01 percent'"),

    # ── StrategySpecValidator ────────────────────────────────────────────────
    ("L16", "the save gate refuses the Immediate trigger instead of the deferred one",
     S + "StrategySpecValidator.cs",
     "if (IsPurePulseTree(spec.Root) && spec.EntryKind != EntryTriggerKind.Immediate)",
     "if (IsPurePulseTree(spec.Root) && spec.EntryKind == EntryTriggerKind.Immediate)",
     "a pulse tree with a pullback trigger (which can never fire) saves; the valid one is refused"),

    ("L17", "one pulse child makes the whole tree 'pure pulse'",
     S + "StrategySpecValidator.cs",
     "                    if (!IsPurePulseTree(c)) return false;\n                return true;",
     "                    if (IsPurePulseTree(c)) return true;\n                return false;",
     "a cross AND an RSI gate is refused a deferred trigger although the RSI gate keeps it armed"),

    # ── SetupSonifier ────────────────────────────────────────────────────────
    ("L18", "a dropout says the setup is still active when it was invalidated",
     S + "SetupSonifier.cs",
     "string suffix = e.SetupStillActive ? \"Setup still active.\" : \"Setup invalidated.\";",
     "string suffix = e.SetupStillActive ? \"Setup invalidated.\" : \"Setup still active.\";",
     "the trader keeps waiting on a dead setup, or abandons a live one"),

    ("L19", "a single-target setup is told it has a ladder",
     S + "SetupSonifier.cs",
     "            plan.TpPrices.Count > 1",
     "            plan.TpPrices.Count > 0",
     "'Ladder has 1 rungs' — the execution caveat spoken where there is no ladder"),

    ("L20", "an Immediate confirmation never says how its ladder executes",
     S + "SetupSonifier.cs",
     "_speech.Speak(Prefix(e.Symbol) + e.Rationale + LadderNote(e.ResolvedPlan), interrupt: false);",
     "_speech.Speak(Prefix(e.Symbol) + e.Rationale, interrupt: false);",
     "restores the fixed defect: the setups most likely in Auto mode never hear the terminal runs the rungs"),

    # ── StrategyBundle ───────────────────────────────────────────────────────
    ("L21", "an imported spec keeps the file's auto-activate flag",
     S + "StrategyBundle.cs",
     "var safe = spec with { IsAutoActivate = false, UpdatedUtc = DateTime.UtcNow };",
     "var safe = spec with { IsAutoActivate = spec.IsAutoActivate, UpdatedUtc = DateTime.UtcNow };",
     "importing a file starts trading on the next launch"),

    ("L22", "an import overwrites the user's edited spec with the same id",
     S + "StrategyBundle.cs",
     "                if (library.GetById(spec.Id) != null)\n",
     "                if (library.GetById(spec.Id) == null && false)\n",
     "re-importing the catalogue silently reverts every edit the user made"),

    ("L23", "a bundle carrying C# source is imported",
     S + "StrategyBundle.cs",
     "                if (!string.IsNullOrWhiteSpace(spec.RoslynSource))\n                {\n                    rejected.Add",
     "                if (string.IsNullOrWhiteSpace(spec.RoslynSource) && false)\n                {\n                    rejected.Add",
     "a strategy file is a route for running someone else's code"),

    # ── StrategyLibraryFacade ────────────────────────────────────────────────
    ("L24", "Add to Engine runs the edited spec beside the old one",
     S + "StrategyLibraryFacade.cs",
     "                foreach (var id in existing)\n                    _engine.RemoveStrategy(id);",
     "                foreach (var id in existing)\n                    { }",
     "edit + re-add: two copies side by side, old and new rules both trading"),

    ("L25", "Save stops running the validator",
     S + "StrategyLibraryFacade.cs",
     "        public StrategyLibraryResult Save(EditableStrategySpec spec)\n        {\n            var validationError = StrategySpecValidator.ValidateForSave(spec);\n            if (validationError != null) return StrategyLibraryResult.Error(validationError);",
     "        public StrategyLibraryResult Save(EditableStrategySpec spec)\n        {\n            var validationError = StrategySpecValidator.ValidateForSave(spec);",
     "a spec whose trigger can never fire is saved, and the refusal is never said"),

    # ── MultiTimeframeDataService ────────────────────────────────────────────
    ("L26", "a short cached HTF series is served when more bars were asked for",
     S + "MultiTimeframeDataService.cs",
     "                && existing.Bars.Count >= count)",
     "                )",
     "a 200-bar HTF leaf reads a 50-bar cache: the higher-timeframe indicator is NaN, the leaf false"),

    ("L27", "an HTF indicator is computed with no parameters",
     S + "MultiTimeframeDataService.cs",
     "var effectiveParams = (parameters == null || parameters.Count == 0)",
     "var effectiveParams = (parameters == null)",
     "the documented all-NaN pathological compute: the HTF leaf is silently false forever"),

    # ── StrategyAutoLoader ───────────────────────────────────────────────────
    ("L28", "every saved template starts at launch",
     S + "StrategyAutoLoader.cs",
     "                if (!spec.IsAutoActivate) continue;\n",
     "",
     "specs kept as templates begin trading on restart"),

    ("L29", "a second LoadAll registers every strategy again",
     S + "StrategyAutoLoader.cs",
     "            if (_hasLoaded) return;\n",
     "",
     "a re-render of MainLayout doubles every armed strategy"),

    ("L30", "restart reconciliation never asks the venues",
     S + "StrategyAutoLoader.cs",
     "                    await _positions.ReconcileAsync().ConfigureAwait(false);",
     "                    await System.Threading.Tasks.Task.CompletedTask;",
     "a position closed at the broker while the app was down is still managed; a stop fires against nothing"),

    # ── SignalCatalog ────────────────────────────────────────────────────────
    ("L31", "a component that reads the future is offered as a strategy leaf",
     S + "SignalCatalog.cs",
     "string? why = notComputable ?? CausalityContract.RefusalReason(ind, comp);",
     "string? why = notComputable;",
     "Ichimoku's Chikou Span is back in the picker: a backtest compares close to a price 26 bars ahead"),

    ("L32", "a refused leaf no longer resolves, so it reads as a typo",
     S + "SignalCatalog.cs",
     "            foreach (var d in list.Concat(refused))",
     "            foreach (var d in list)",
     "an old spec's Chikou leaf goes silently dead as 'unknown signal' instead of saying why"),

    # ── LabRunner ────────────────────────────────────────────────────────────
    ("L33", "a strategy that works in ONE half is called a survivor",
     S + "LabRunner.cs",
     "&& ciLo1 > 0 && ciLo2 > 0;",
     "&& (ciLo1 > 0 || ciLo2 > 0);",
     "the era-robustness gate passes regime-lucky strategies"),

    ("L34", "the minimum sample is only checked in the first half",
     S + "LabRunner.cs",
     "bool survivor = n1 >= MinTradesPerHalf && n2 >= MinTradesPerHalf",
     "bool survivor = n1 >= MinTradesPerHalf",
     "two trades in the second half — a CI that is noise — earns SURVIVOR"),

    # ── JsonStrategyLibrary ──────────────────────────────────────────────────
    ("L35", "updating the FIRST spec in the library appends a copy",
     S + "JsonStrategyLibrary.cs",
     "if (idx >= 0) _specs[idx] = spec;",
     "if (idx > 0) _specs[idx] = spec;",
     "the first strategy the user ever saved duplicates on every edit"),

    ("L36", "a corrupt library file is not moved aside",
     S + "JsonStrategyLibrary.cs",
     "                CorruptFileQuarantine.MoveAside(_filepath, ex);\n",
     "",
     "the next save overwrites the user's whole library with an empty list"),

    # ── LevelService ─────────────────────────────────────────────────────────
    ("L37", "the stop goes to the FARTHEST support below, not the nearest",
     S + "LevelService.cs",
     "                double d = price - lvl.Price;\n                if (d < bestDist) { bestDist = d; best = lvl; }",
     "                double d = price - lvl.Price;\n                if (d > bestDist || double.IsPositiveInfinity(bestDist)) { bestDist = d; best = lvl; }",
     "a BelowSupport stop lands at the oldest pivot on the chart; position size collapses"),

    ("L38", "the level-kind filter is ignored below price",
     S + "LevelService.cs",
     "                if (lvl.Price >= price) continue;\n                if (kindFilter.HasValue && lvl.Kind != kindFilter.Value) continue;\n",
     "                if (lvl.Price >= price) continue;\n",
     "a BelowKumo stop resolves to a swing low or a drawn line instead of the cloud"),

    # ── Level providers ──────────────────────────────────────────────────────
    ("L39", "Cipher SR pivots are visible before they are confirmed",
     L + "CipherSrLevelProvider.cs",
     "int upTo = System.Math.Max(0, history.Count - lag);",
     "int upTo = System.Math.Max(0, history.Count);",
     "restores the +0.739R phantom edge: a backtest reads a low that needed future bars to confirm"),

    ("L40", "a drawn horizontal line is not a level",
     L + "DrawnHorizontalLevelProvider.cs",
     "                    case DrawingType.HorizontalLine:\n                        AddLevel(sink, d.AnchorPrice1, currentPrice);\n                        break;",
     "                    case DrawingType.HorizontalLine:\n                        break;",
     "the user's own support line — the highest-priority source — is ignored by stops and targets"),

    ("L41", "Ichimoku levels read bars that have not happened",
     L + "IchimokuLevelProvider.cs",
     "int upTo = System.Math.Max(0, history.Count);",
     "int upTo = int.MaxValue;",
     "in a backtest the Kijun stop is the Kijun of the last bar on the chart"),

    ("L42", "Kumo top and bottom swap",
     L + "IchimokuLevelProvider.cs",
     "double top = System.Math.Max(senkouA, senkouB);\n                double bot = System.Math.Min(senkouA, senkouB);",
     "double top = System.Math.Min(senkouA, senkouB);\n                double bot = System.Math.Max(senkouA, senkouB);",
     "a BelowKumo stop is placed at the cloud's TOP — inside the cloud, one wiggle from triggering"),

    ("L43", "a swing high is emitted as support",
     L + "SwingPivotLevelProvider.cs",
     "sink.Add(new PriceLevel(bar.High, LevelKind.Resistance,",
     "sink.Add(new PriceLevel(bar.High, LevelKind.Support,",
     "a NextResistance target resolves to nothing, a support-filtered stop to a high above price"),

    ("L44", "backtests read the live (current-viewport) volume profile",
     L + "VolumeProfileLevelProvider.cs",
     "if (_backtestCache != null && _backtestCache.IsActive)",
     "if (_backtestCache != null && !_backtestCache.IsActive)",
     "restores the profile future leak: a backtest's POC is computed from bars it has not reached"),
]


def run(cmd, cwd=REPO, timeout=5400):
    p = subprocess.run(cmd, cwd=cwd, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def build():
    code, out = run("nice -n 10 dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                    "-p:UseRazorSourceGenerator=false -v q --nologo")
    return code == 0, out


def test():
    # NEVER -v q here (rule 6): the per-test "Failed <name>" lines are the audit.
    return run("nice -n 10 dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
               "-p:UseRazorSourceGenerator=false --no-build")


def files():
    return sorted({m[2] for m in MUTANTS})


def backup_all():
    for rel in files():
        dst = os.path.join(BACKUP, rel)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        if not os.path.exists(dst):
            shutil.copy2(os.path.join(REPO, rel), dst)


def restore(rel):
    src = os.path.join(BACKUP, rel)
    dst = os.path.join(REPO, rel)
    shutil.copyfile(src, dst)
    os.utime(dst, None)   # rule 1: newer than the sabotaged build output


def recover_inflight():
    if not os.path.exists(INFLIGHT):
        return
    rel = open(INFLIGHT).read().strip()
    if rel:
        restore(rel)
        print(f"recovered stale sabotage in {rel} from file copy", flush=True)
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


def check_restored():
    bad = [rel for rel in files()
           if not filecmp.cmp(os.path.join(REPO, rel), os.path.join(BACKUP, rel), shallow=False)]
    if bad:
        print("TREE NOT RESTORED: " + ", ".join(bad), flush=True)
    else:
        print(f"restored byte-identical ({len(files())} files)", flush=True)
    return not bad


def main():
    if "--verify" in sys.argv:
        sys.exit(0 if verify() else 1)
    if not verify():
        sys.exit(1)

    backup_all()
    recover_inflight()
    only = [a for a in sys.argv[1:] if re.fullmatch(r"L\d\d", a)] or None
    results = json.load(open(OUT)) if os.path.exists(OUT) else []
    done = {r['id'] for r in results}

    for mid, area, relpath, find, repl, breaks in MUTANTS:
        if (only and mid not in only) or mid in done:
            continue
        path = os.path.join(REPO, relpath)
        original = open(os.path.join(BACKUP, relpath), encoding='utf-8-sig').read()
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
                if rec['status'] == 'UNPARSED':
                    rec['log'] = out[-2000:]
                print(f"{mid}: {rec['status']} failed={rec['failed']} ({time.time()-t0:.0f}s) — {area}", flush=True)
                if names:
                    print("      " + "; ".join(names[:6]), flush=True)
        finally:
            restore(relpath)
            if os.path.exists(INFLIGHT):
                os.remove(INFLIGHT)
        rec['seconds'] = round(time.time() - t0)
        results.append(rec); json.dump(results, open(OUT, 'w'), indent=1)

    check_restored()
    ok, _ = build()
    code, out = test()
    m = SUMMARY_RE.search(out)
    names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
    print(f"\n=== CONTROL (nothing sabotaged, build ok={ok}): {m.group(0) if m else 'UNPARSED'} {names}", flush=True)

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
