using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Builds and runs a 2-leaf AND-gated strategy on the fly: one entry-trigger leaf (typically
/// a Cipher MarkerFire signal that survived the isolation diagnostic) plus one filter leaf
/// (typically a continuous cross-series value like FEAR_GREED.Sentiment compared against a
/// threshold). Runs the combo walk-forward H1+H2 with reverse-on-signal disabled (the only
/// honest measure of entry edge), and ALSO runs the entry leaf alone for direct comparison.
///
/// This is the workhorse for the orthogonal-information hypothesis: does combining a
/// price-pattern signal with a non-price signal produce edge that the price signal alone
/// doesn't have?
/// </summary>
public static class ComboCommand
{
    public static async Task<int> RunAsync(
        string snapshotPath,
        string entrySignalId,
        string filterSignalId,
        string filterOperator,
        double filterValue,
        int warmupBars)
    {
        if (!File.Exists(snapshotPath))
        {
            Console.Error.WriteLine($"Snapshot not found: {snapshotPath}");
            return 1;
        }

        if (!Enum.TryParse<LeafOperator>(filterOperator, ignoreCase: true, out var op))
        {
            Console.Error.WriteLine($"Unknown leaf operator '{filterOperator}'. Valid: " +
                string.Join(", ", Enum.GetNames(typeof(LeafOperator))));
            return 1;
        }

        var snapshot = SnapshotCommand.Load(snapshotPath);
        var midIdx = snapshot.Bars.Count / 2;
        var midDate = snapshot.Bars[midIdx].Date;
        Console.WriteLine($"Snapshot: {snapshot.Provider} {snapshot.Symbol} {snapshot.Timeframe} ({snapshot.BarCount} bars)");
        Console.WriteLine($"Walk-forward midpoint: {midDate:yyyy-MM-dd}");
        Console.WriteLine();

        Console.WriteLine("Building host (cross-series cache picks up xs_*.json from strategy-lab-data)...");
        var host = LabHost.Build();
        var state = await WorkspaceFactory.BuildAsync(host.Services, snapshot);

        // Sanity check: did the requested filter signal actually populate? If the cross-series
        // cache had no data for it, the indicator's component arrays will be all NaN and
        // every leaf will silently fail. Surface that to the user immediately rather than
        // letting them stare at "0 trades" wondering why.
        VerifyFilterDataPresent(state, filterSignalId);

        var catalog = host.Services.GetRequiredService<ISignalCatalog>();
        var entryDesc = catalog.GetById(entrySignalId);
        var filterDesc = catalog.GetById(filterSignalId);
        if (entryDesc == null) { Console.Error.WriteLine($"Entry signal '{entrySignalId}' not in catalog."); return 1; }
        if (filterDesc == null) { Console.Error.WriteLine($"Filter signal '{filterSignalId}' not in catalog."); return 1; }
        Console.WriteLine($"Entry  : {entryDesc.DisplayLabel} [{entryDesc.Kind}]");
        Console.WriteLine($"Filter : {filterDesc.DisplayLabel} [{filterDesc.Kind}] {op} {filterValue}");
        Console.WriteLine();

        var factory = host.Services.GetRequiredService<IConfigurableStrategyFactory>();
        var backtester = host.Services.GetRequiredService<IStrategyBacktester>();

        // Build both specs: the bare entry and the entry+filter combo. Risk plan held identical
        // so the only variable is the presence of the filter.
        var bareSpec = MakeSpec(
            id: $"combo-bare-{entrySignalId}",
            name: $"BARE: {entryDesc.DisplayLabel}",
            leaves: new[] { MakeFireLeaf("e", entrySignalId) });

        var comboSpec = MakeSpec(
            id: $"combo-and-{entrySignalId}-{filterSignalId}",
            name: $"COMBO: {entryDesc.DisplayLabel} AND {filterDesc.DisplayLabel} {op} {filterValue}",
            leaves: new ConditionNode[]
            {
                MakeFireLeaf("e", entrySignalId),
                MakeNumericLeaf("f", filterSignalId, op, filterValue)
            },
            logic: LogicOperator.And);

        // Run all four cells: bare H1/H2 + combo H1/H2, all no-reverse for honest measurement.
        var bareH1 = await Run(bareSpec, backtester, factory, snapshot, state, snapshot.FirstDate, midDate, warmupBars);
        var bareH2 = await Run(bareSpec, backtester, factory, snapshot, state, midDate, snapshot.LastDate, warmupBars);
        var comboH1 = await Run(comboSpec, backtester, factory, snapshot, state, snapshot.FirstDate, midDate, warmupBars);
        var comboH2 = await Run(comboSpec, backtester, factory, snapshot, state, midDate, snapshot.LastDate, warmupBars);

        Console.WriteLine();
        Console.WriteLine("══════════════════════════════════════════════════════════════════════════");
        Console.WriteLine($"BASELINE (entry signal alone, no reverse-on-signal)");
        Console.WriteLine("══════════════════════════════════════════════════════════════════════════");
        PrintRow("H1", bareH1);
        PrintRow("H2", bareH2);

        Console.WriteLine();
        Console.WriteLine("══════════════════════════════════════════════════════════════════════════");
        Console.WriteLine($"COMBO (entry AND {filterDesc.ComponentName} {op} {filterValue}, no reverse)");
        Console.WriteLine("══════════════════════════════════════════════════════════════════════════");
        PrintRow("H1", comboH1);
        PrintRow("H2", comboH2);

        Console.WriteLine();
        Console.WriteLine("══════════════════════════════════════════════════════════════════════════");
        Console.WriteLine("DELTA (combo − bare)");
        Console.WriteLine("══════════════════════════════════════════════════════════════════════════");
        PrintDelta("H1", bareH1, comboH1);
        PrintDelta("H2", bareH2, comboH2);

        bool comboBetter = comboH1.ExpectancyR > bareH1.ExpectancyR && comboH2.ExpectancyR > bareH2.ExpectancyR;
        bool bothPositive = comboH1.CiLo > 0 && comboH2.CiLo > 0;
        Console.WriteLine();
        if (comboBetter && bothPositive)
            Console.WriteLine("VERDICT: filter IMPROVES expectancy in BOTH halves AND combo CI lo > 0 both halves. ✓");
        else if (bothPositive)
            Console.WriteLine("VERDICT: combo CI lo > 0 both halves but filter doesn't improve over baseline.");
        else if (comboBetter)
            Console.WriteLine("VERDICT: filter improves expectancy but CI straddles zero in at least one half.");
        else
            Console.WriteLine("VERDICT: filter does not help.");

        return 0;
    }

    private static void VerifyFilterDataPresent(WorkspaceState state, string filterSignalId)
    {
        var dot = filterSignalId.IndexOf('.');
        if (dot < 0) return;
        var indicatorCode = filterSignalId.Substring(0, dot);
        var componentName = filterSignalId.Substring(dot + 1);

        var series = state.ActiveSeries.FirstOrDefault(s =>
            string.Equals(s.IndicatorCode, indicatorCode, StringComparison.OrdinalIgnoreCase));
        if (series == null)
        {
            Console.Error.WriteLine($"  WARNING: indicator '{indicatorCode}' not loaded into workspace — filter will never fire.");
            return;
        }
        if (!series.Data.ComponentData.TryGetValue(componentName, out var arr) || arr == null)
        {
            Console.Error.WriteLine($"  WARNING: component '{componentName}' not present on series — filter will never fire.");
            return;
        }
        int nonNan = 0;
        for (int i = 0; i < arr.Length; i++) if (!double.IsNaN(arr[i])) nonNan++;
        Console.WriteLine($"  filter signal data: {nonNan}/{arr.Length} non-NaN ({100.0 * nonNan / arr.Length:0.0}% coverage)");
        if (nonNan == 0)
            Console.Error.WriteLine($"  WARNING: filter component is all NaN — cross-series snapshot may be missing or wrong key.");
    }

    private static ConditionLeaf MakeFireLeaf(string id, string descriptorId) =>
        new(Id: id, SignalDescriptorId: descriptorId, Operator: LeafOperator.Fired, Score: 1.0);

    private static ConditionLeaf MakeNumericLeaf(string id, string descriptorId, LeafOperator op, double value) =>
        new(Id: id, SignalDescriptorId: descriptorId, Operator: op, Value: value, Score: 1.0);

    private static StrategySpec MakeSpec(string id, string name, IEnumerable<ConditionNode> leaves, LogicOperator logic = LogicOperator.Or)
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
            Id: id, Name: name, Description: "Auto-generated combo by StrategyLab.",
            Side: OrderSide.Buy, Conditions: root, Risk: risk,
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
        var (ciLo, _, ciHi) = BootstrapCi.FromResult(result);
        return new RunResult(
            Trades: result.Metrics.TotalSignals,
            WinRate: result.Metrics.WinRate,
            Pnl: result.Metrics.TotalPnL,
            Sharpe: result.Metrics.SharpeRatio,
            ExpectancyR: double.IsNaN(result.Expectancy) ? 0 : result.Expectancy,
            ProfitFactor: double.IsNaN(result.ProfitFactor) ? 0 : result.ProfitFactor,
            CiLo: ciLo,
            CiHi: ciHi);
    }

    private static void PrintRow(string label, RunResult r)
    {
        string ci = (double.IsNaN(r.CiLo) || double.IsNaN(r.CiHi))
            ? "[ n/a ]"
            : $"[{r.CiLo,+6:+0.00;-0.00; 0.00},{r.CiHi,+6:+0.00;-0.00; 0.00}]";
        Console.WriteLine($"  {label}: trades={r.Trades,4}  WR={r.WinRate * 100,5:0.0}%  P&L={r.Pnl,9:+0.00;-0.00; 0.00}  " +
            $"R/tr={r.ExpectancyR,7:+0.000;-0.000; 0.000}  95%CI={ci}  PF={r.ProfitFactor,5:0.00}");
    }

    private static void PrintDelta(string label, RunResult bare, RunResult combo)
    {
        Console.WriteLine($"  {label}: Δtrades={combo.Trades - bare.Trades,+4}  ΔP&L={combo.Pnl - bare.Pnl,+9:0.00}  " +
            $"ΔR/tr={combo.ExpectancyR - bare.ExpectancyR,+8:0.000}  ΔPF={combo.ProfitFactor - bare.ProfitFactor,+6:0.00}");
    }

    private record RunResult(int Trades, double WinRate, double Pnl, double Sharpe, double ExpectancyR, double ProfitFactor, double CiLo = double.NaN, double CiHi = double.NaN);
}
