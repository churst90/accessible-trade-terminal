using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Tier 1 step 4 of the validation roadmap. Sweeps a 2-leaf AND combo across the cross product
/// of:
///
///     entry signals (auto-discovered MarkerFire signals from --entry-indicators, default CIPHER_B)
///     × filter operators (LessThan, GreaterThan, …)
///     × filter values (numeric thresholds)
///
/// All cells run no-reverse on H1 and H2 with bootstrap CIs. The output is a single ranked
/// table flagging combos that meet the validation pass criterion: combo CI-lo &gt; 0 in BOTH
/// halves AND combo expectancy improves over the bare entry baseline in both halves AND the
/// trade count is high enough to be a meaningful sample.
///
/// Critical efficiency note: the host + workspace state are built ONCE up front and re-used
/// across every combo cell. Without this, sweeping ~6 entries × 5 thresholds × 2 ops would
/// pay the indicator-warmup cost ~60 times.
/// </summary>
public static class ComboSweepCommand
{
    public static async Task<int> RunAsync(
        string snapshotPath,
        string[] entryIndicators,
        string filterSignalId,
        LeafOperator[] filterOps,
        double[] filterValues,
        int warmupBars,
        int minTradesPerHalf,
        bool baselineRelative = true,
        double? baselineOverride = null)
    {
        if (!File.Exists(snapshotPath))
        {
            Console.Error.WriteLine($"Snapshot not found: {snapshotPath}");
            return 1;
        }

        var snapshot = SnapshotCommand.Load(snapshotPath);
        var midIdx = snapshot.Bars.Count / 2;
        var midDate = snapshot.Bars[midIdx].Date;
        Console.WriteLine($"Snapshot: {snapshot.Provider} {snapshot.Symbol} {snapshot.Timeframe} ({snapshot.BarCount} bars)");
        Console.WriteLine($"H1: {snapshot.FirstDate:yyyy-MM-dd} → {midDate:yyyy-MM-dd}");
        Console.WriteLine($"H2: {midDate:yyyy-MM-dd} → {snapshot.LastDate:yyyy-MM-dd}");
        Console.WriteLine();

        Console.WriteLine("Building host + indicator state (one-time)...");
        var host = LabHost.Build();
        var state = await WorkspaceFactory.BuildAsync(host.Services, snapshot);

        var catalog = host.Services.GetRequiredService<ISignalCatalog>();
        var factory = host.Services.GetRequiredService<IConfigurableStrategyFactory>();
        var backtester = host.Services.GetRequiredService<IStrategyBacktester>();

        var filterDesc = catalog.GetById(filterSignalId);
        if (filterDesc == null)
        {
            Console.Error.WriteLine($"Filter signal '{filterSignalId}' not in catalog.");
            return 1;
        }
        Console.WriteLine($"Filter: {filterDesc.DisplayLabel}");

        // Sanity-check the filter component is populated.
        VerifyFilterDataPresent(state, filterSignalId);

        // Resolve the baseline (visual zero) for this filter component. Many Cipher components
        // are plotted at non-zero Y baselines (e.g. Money Flow Wave anchored at -80) so user
        // thresholds expressed "around zero" need translating to raw catalog values. Auto-detect
        // via median when not overridden — for an oscillator that spends roughly equal time
        // above and below its true zero this is accurate to ~1 unit.
        double baseline = 0.0;
        if (baselineRelative)
        {
            baseline = baselineOverride ?? AutoDetectBaseline(state, filterSignalId);
            Console.WriteLine($"  baseline: {baseline:0.000} ({(baselineOverride.HasValue ? "manual" : "auto/median")})");
            Console.WriteLine($"  thresholds will be translated: raw = baseline + user_value");
        }

        var entries = catalog.All
            .Where(d => d.Kind == SignalKind.MarkerFire)
            .Where(d => entryIndicators.Any(f => string.Equals(f, d.IndicatorCode, StringComparison.OrdinalIgnoreCase)))
            .Where(d => MarkerSideHelper.Classify(d.ComponentName) != null) // skip unclassifiable
            .OrderBy(d => d.IndicatorCode).ThenBy(d => d.ComponentName)
            .ToList();

        Console.WriteLine($"Entries: {entries.Count} MarkerFire signal(s) from {string.Join(",", entryIndicators)}");
        Console.WriteLine($"Sweep cells: {entries.Count} entries × {filterOps.Length} ops × {filterValues.Length} values = " +
                         $"{entries.Count * filterOps.Length * filterValues.Length} combos × 4 backtests each");
        Console.WriteLine();

        // Cache bare baselines per entry — they don't depend on (op, value) so we'd otherwise
        // recompute them ops*values times each.
        var bareCache = new Dictionary<string, (RunResult H1, RunResult H2)>();

        var rows = new List<SweepRow>();
        int progress = 0;
        int total = entries.Count * filterOps.Length * filterValues.Length;

        foreach (var entry in entries)
        {
            var side = MarkerSideHelper.Classify(entry.ComponentName) ?? OrderSide.Buy;

            // Bare baseline for this entry, computed once.
            if (!bareCache.ContainsKey(entry.Id))
            {
                var bareSpec = MakeSpec(
                    id: $"sweep-bare-{entry.Id}",
                    name: $"BARE: {entry.DisplayLabel}",
                    leaves: new ConditionNode[] { MakeFireLeaf("e", entry.Id) },
                    logic: LogicOperator.Or,
                    side: side);
                var bH1 = await Run(bareSpec, backtester, factory, snapshot, state, snapshot.FirstDate, midDate, warmupBars);
                var bH2 = await Run(bareSpec, backtester, factory, snapshot, state, midDate, snapshot.LastDate, warmupBars);
                bareCache[entry.Id] = (bH1, bH2);
            }

            foreach (var op in filterOps)
            foreach (var val in filterValues)
            {
                progress++;
                double rawVal = baselineRelative ? baseline + val : val;
                string sideStr = side == OrderSide.Buy ? "L" : "S";
                Console.Write($"  [{progress,4}/{total}] {sideStr} {entry.IndicatorCode}.{Trim(entry.ComponentName, 22),-22} {op,-11} {val,6:+0.0;-0.0; 0.0}  ");
                try
                {
                    var comboSpec = MakeSpec(
                        id: $"sweep-{entry.Id}-{op}-{val}",
                        name: $"{entry.DisplayLabel} AND {filterDesc.ComponentName} {op} {val}",
                        leaves: new ConditionNode[]
                        {
                            MakeFireLeaf("e", entry.Id),
                            MakeNumericLeaf("f", filterSignalId, op, rawVal)
                        },
                        logic: LogicOperator.And,
                        side: side);

                    var cH1 = await Run(comboSpec, backtester, factory, snapshot, state, snapshot.FirstDate, midDate, warmupBars);
                    var cH2 = await Run(comboSpec, backtester, factory, snapshot, state, midDate, snapshot.LastDate, warmupBars);
                    var (bH1c, bH2c) = bareCache[entry.Id];
                    rows.Add(new SweepRow(entry.IndicatorCode, entry.ComponentName, side, op, val, bH1c, bH2c, cH1, cH2));
                    Console.WriteLine($"H1 R={cH1.ExpectancyR,+6:0.000} CIlo={cH1.CiLo,+6:0.00}  H2 R={cH2.ExpectancyR,+6:0.000} CIlo={cH2.CiLo,+6:0.00}  tr={cH1.Trades}/{cH2.Trades}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: {ex.Message}");
                }
            }
        }

        PrintSummary(rows, filterDesc.ComponentName, minTradesPerHalf);
        return 0;
    }

    private static void PrintSummary(List<SweepRow> rows, string filterName, int minTradesPerHalf)
    {
        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine($"COMBO SWEEP — entry AND ({filterName} ?op ?val), no-reverse, bootstrap CIs");
        Console.WriteLine("════════════════════════════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine($"Min trades/half for survivor flag: {minTradesPerHalf}");
        Console.WriteLine();

        // Rank by min(H1 CIlo, H2 CIlo) descending — the most pessimistic-honest view of edge.
        var ranked = rows
            .OrderByDescending(r => Math.Min(SafeNum(r.ComboH1.CiLo), SafeNum(r.ComboH2.CiLo)))
            .ToList();

        Console.WriteLine($"{"Entry",-32} {"Op",-11} {"Val",6} {"H1 tr",6} {"H1 R",8} {"H1 CIlo",8} {"H2 tr",6} {"H2 R",8} {"H2 CIlo",8} {"Δbare",10} flags");
        Console.WriteLine(new string('─', 130));
        foreach (var r in ranked)
        {
            bool ciLoBoth = r.ComboH1.CiLo > 0 && r.ComboH2.CiLo > 0;
            bool tradesEnough = r.ComboH1.Trades >= minTradesPerHalf && r.ComboH2.Trades >= minTradesPerHalf;
            bool improvesOverBare = r.ComboH1.ExpectancyR > r.BareH1.ExpectancyR && r.ComboH2.ExpectancyR > r.BareH2.ExpectancyR;
            bool survivor = ciLoBoth && tradesEnough && improvesOverBare;
            string flag = survivor ? "★ SURVIVOR"
                          : (ciLoBoth && tradesEnough) ? "ci-ok"
                          : (improvesOverBare && tradesEnough) ? "imp-only"
                          : "";
            double dH1 = r.ComboH1.ExpectancyR - r.BareH1.ExpectancyR;
            double dH2 = r.ComboH2.ExpectancyR - r.BareH2.ExpectancyR;
            string entryLabel = $"{(r.Side == OrderSide.Buy ? "L" : "S")} {r.IndicatorCode}.{Trim(r.ComponentName, 20)}";
            Console.WriteLine(
                $"{entryLabel,-32} {r.Op,-11} {r.Val,6:+0.0;-0.0; 0.0} " +
                $"{r.ComboH1.Trades,6} {r.ComboH1.ExpectancyR,8:+0.000;-0.000; 0.000} {r.ComboH1.CiLo,8:+0.00;-0.00; 0.00} " +
                $"{r.ComboH2.Trades,6} {r.ComboH2.ExpectancyR,8:+0.000;-0.000; 0.000} {r.ComboH2.CiLo,8:+0.00;-0.00; 0.00} " +
                $"Δ{dH1,+5:0.00}/{dH2,+5:0.00} {flag}");
        }

        Console.WriteLine();
        var survivors = ranked.Where(r =>
            r.ComboH1.CiLo > 0 && r.ComboH2.CiLo > 0
            && r.ComboH1.Trades >= minTradesPerHalf && r.ComboH2.Trades >= minTradesPerHalf
            && r.ComboH1.ExpectancyR > r.BareH1.ExpectancyR && r.ComboH2.ExpectancyR > r.BareH2.ExpectancyR).ToList();
        Console.WriteLine($"SURVIVORS: {survivors.Count} of {rows.Count} combos passed all three gates.");
        foreach (var s in survivors)
        {
            Console.WriteLine($"  ★ {s.IndicatorCode}.{s.ComponentName} AND {filterName} {s.Op} {s.Val}");
            Console.WriteLine($"      H1: {s.ComboH1.Trades} trades, R={s.ComboH1.ExpectancyR:+0.000} CI=[{s.ComboH1.CiLo:+0.000},{s.ComboH1.CiHi:+0.000}], baseline R={s.BareH1.ExpectancyR:+0.000}");
            Console.WriteLine($"      H2: {s.ComboH2.Trades} trades, R={s.ComboH2.ExpectancyR:+0.000} CI=[{s.ComboH2.CiLo:+0.000},{s.ComboH2.CiHi:+0.000}], baseline R={s.BareH2.ExpectancyR:+0.000}");
        }
    }

    private static double SafeNum(double d) => double.IsNaN(d) ? double.NegativeInfinity : d;

    private static void VerifyFilterDataPresent(WorkspaceState state, string filterSignalId)
    {
        var dot = filterSignalId.IndexOf('.');
        if (dot < 0) return;
        var indicatorCode = filterSignalId.Substring(0, dot);
        var componentName = filterSignalId.Substring(dot + 1);
        var series = state.ActiveSeries.FirstOrDefault(s =>
            string.Equals(s.IndicatorCode, indicatorCode, StringComparison.OrdinalIgnoreCase));
        if (series == null) { Console.Error.WriteLine($"  WARNING: indicator '{indicatorCode}' not loaded — filter never fires."); return; }
        if (!series.Data.ComponentData.TryGetValue(componentName, out var arr) || arr == null)
        { Console.Error.WriteLine($"  WARNING: component '{componentName}' not present — filter never fires."); return; }
        var vals = new List<double>(arr.Length);
        for (int i = 0; i < arr.Length; i++) if (!double.IsNaN(arr[i])) vals.Add(arr[i]);
        Console.WriteLine($"  filter coverage: {vals.Count}/{arr.Length} non-NaN ({100.0 * vals.Count / arr.Length:0.0}%)");
        if (vals.Count > 0)
        {
            vals.Sort();
            double Pct(double p) => vals[(int)Math.Clamp(Math.Floor(p * (vals.Count - 1)), 0, vals.Count - 1)];
            Console.WriteLine($"  filter scale: min={vals[0]:0.000} p10={Pct(0.10):0.000} p25={Pct(0.25):0.000} median={Pct(0.50):0.000} p75={Pct(0.75):0.000} p90={Pct(0.90):0.000} max={vals[^1]:0.000}");
        }
    }

    private static ConditionLeaf MakeFireLeaf(string id, string descriptorId) =>
        new(Id: id, SignalDescriptorId: descriptorId, Operator: LeafOperator.Fired, Score: 1.0);

    private static ConditionLeaf MakeNumericLeaf(string id, string descriptorId, LeafOperator op, double value) =>
        new(Id: id, SignalDescriptorId: descriptorId, Operator: op, Value: value, Score: 1.0);

    private static double AutoDetectBaseline(WorkspaceState state, string filterSignalId)
    {
        var dot = filterSignalId.IndexOf('.');
        if (dot < 0) return 0.0;
        var indicatorCode = filterSignalId.Substring(0, dot);
        var componentName = filterSignalId.Substring(dot + 1);
        var series = state.ActiveSeries.FirstOrDefault(s =>
            string.Equals(s.IndicatorCode, indicatorCode, StringComparison.OrdinalIgnoreCase));
        if (series == null) return 0.0;
        if (!series.Data.ComponentData.TryGetValue(componentName, out var arr) || arr == null) return 0.0;
        var vals = new List<double>(arr.Length);
        for (int i = 0; i < arr.Length; i++) if (!double.IsNaN(arr[i])) vals.Add(arr[i]);
        if (vals.Count == 0) return 0.0;
        vals.Sort();
        return vals[vals.Count / 2];
    }

    private static StrategySpec MakeSpec(string id, string name, IEnumerable<ConditionNode> leaves, LogicOperator logic, OrderSide side = OrderSide.Buy)
    {
        var root = new ConditionGroup(Id: $"{id}-root", Logic: logic, Children: leaves.ToList());
        var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 2.0);
        var tpLadder = new List<TpLadderRung>
        {
            new(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 1.5, ClosePortion: 0.50),
            new(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 3.0, ClosePortion: 0.50),
        };
        var risk = new RiskPlan(
            Stop: stop, TpLadder: tpLadder,
            Sizing: new PositionSizing(SizingMode.FixedRiskPercent, RiskPercent: 0.005),
            Entry: new EntryTrigger(EntryTriggerKind.Immediate),
            MinRewardRiskRatio: 1.5,
            StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
            NotionalEquity: 10000.0);
        return new StrategySpec(
            Id: id, Name: name, Description: "Auto-generated combo-sweep cell.",
            Side: side, Conditions: root, Risk: risk,
            ExecutionMode: StrategyExecutionMode.Suggestion,
            CreatedUtc: DateTime.UtcNow, UpdatedUtc: DateTime.UtcNow,
            IsAutoActivate: false);
    }

    private static async Task<RunResult> Run(
        StrategySpec spec, IStrategyBacktester backtester, IConfigurableStrategyFactory factory,
        SnapshotFile snapshot, WorkspaceState state, DateTime start, DateTime end, int warmup)
    {
        var config = new BacktestConfig(
            WarmupBars: warmup, ReplayProfiles: false,
            StartDate: start, EndDate: end, AllowReverseOnSignal: false);
        var strat = factory.Create(spec);
        var result = await backtester.RunAsync(strat, snapshot.Bars, config, state);
        var (lo, _, hi) = BootstrapCi.FromResult(result);
        return new RunResult(
            Trades: result.Metrics.TotalSignals,
            ExpectancyR: double.IsNaN(result.Expectancy) ? 0 : result.Expectancy,
            Pnl: result.Metrics.TotalPnL,
            CiLo: lo, CiHi: hi);
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s.Substring(0, n);

    private record RunResult(int Trades, double ExpectancyR, double Pnl, double CiLo, double CiHi);
    private record SweepRow(string IndicatorCode, string ComponentName, OrderSide Side, LeafOperator Op, double Val, RunResult BareH1, RunResult BareH2, RunResult ComboH1, RunResult ComboH2);
}
