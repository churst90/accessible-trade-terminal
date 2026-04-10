using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Walks every <see cref="SignalKind.MarkerFire"/> signal exposed by the requested indicators
/// and runs a single-leaf "raw signal" strategy against the snapshot in walk-forward mode,
/// both with and without reverse-on-signal. The output is a sorted summary table that lets
/// us answer one specific question:
///
///     "Does any Cipher signal — measured in complete isolation, with no protective exit
///      cycling — produce positive expectancy on either half of the data?"
///
/// The reason this matters: the StrategyLab harness's first finding (project_strategylab_harness
/// memory file) was that v11's H2 profitability collapsed when reverse-on-signal was disabled.
/// That implies the *blue dot specifically* has no edge of its own. This diagnostic generalises
/// the question — maybe ANOTHER Cipher signal does. The honest answer (positive or negative)
/// changes the strategy roadmap.
///
/// Risk plan is held constant across all signals (ATR(14)x2 stop, 1.5R/3R TP ladder, BE after
/// TP1, 0.5% risk) so the only variable is the entry signal.
/// </summary>
public static class DiagnosticCommand
{
    public static async Task<int> RunAsync(
        string snapshotPath,
        string[] indicatorFilter,
        int warmupBars)
    {
        if (!File.Exists(snapshotPath))
        {
            Console.Error.WriteLine($"Snapshot not found: {snapshotPath}");
            return 1;
        }

        var snapshot = SnapshotCommand.Load(snapshotPath);
        Console.WriteLine($"Snapshot: {snapshot.Provider} {snapshot.Symbol} {snapshot.Timeframe} ({snapshot.BarCount} bars)");

        var midIdx = snapshot.Bars.Count / 2;
        var midDate = snapshot.Bars[midIdx].Date;
        Console.WriteLine($"Walk-forward midpoint: {midDate:yyyy-MM-dd}");
        Console.WriteLine($"  H1: {snapshot.FirstDate:yyyy-MM-dd} → {midDate:yyyy-MM-dd}");
        Console.WriteLine($"  H2: {midDate:yyyy-MM-dd} → {snapshot.LastDate:yyyy-MM-dd}");
        Console.WriteLine();

        Console.WriteLine("Building host + computing indicators (one-time)...");
        var host = LabHost.Build();
        var state = await WorkspaceFactory.BuildAsync(host.Services, snapshot);

        // Find every MarkerFire signal from the requested indicators. Iterating the catalog
        // (not a hardcoded list) means a future Cipher revision that adds a new marker
        // automatically gets tested without code changes here.
        var catalog = host.Services.GetRequiredService<ISignalCatalog>();
        var markers = catalog.All
            .Where(d => d.Kind == SignalKind.MarkerFire)
            .Where(d => indicatorFilter.Length == 0 ||
                        indicatorFilter.Any(f => string.Equals(f, d.IndicatorCode, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(d => d.IndicatorCode).ThenBy(d => d.ComponentName)
            .ToList();

        Console.WriteLine();
        Console.WriteLine($"Testing {markers.Count} marker signals as single-leaf long strategies, 4 modes each:");
        Console.WriteLine("  H1+rev   = first half, reverse-on-signal allowed (production behavior)");
        Console.WriteLine("  H1-rev   = first half, reverse-on-signal DISABLED (raw entry edge)");
        Console.WriteLine("  H2+rev   = second half, reverse-on-signal allowed");
        Console.WriteLine("  H2-rev   = second half, reverse-on-signal DISABLED");
        Console.WriteLine();

        var factory = host.Services.GetRequiredService<IConfigurableStrategyFactory>();
        var backtester = host.Services.GetRequiredService<IStrategyBacktester>();

        var rows = new List<DiagRow>();
        foreach (var sig in markers)
        {
            Console.WriteLine($"  {sig.Id} ...");

            var spec = MakeSingleLeafSpec(sig.Id, sig.DisplayLabel);

            // Each backtester run gets a fresh ConfigurableStrategy instance so internal state
            // (setup state machine, indicator caches the strategy holds) doesn't leak between
            // runs. The factory creates a new instance per call.
            var h1r = await Run(spec, backtester, factory, snapshot, state,
                                snapshot.FirstDate, midDate, warmupBars, allowReverse: true);
            var h1n = await Run(spec, backtester, factory, snapshot, state,
                                snapshot.FirstDate, midDate, warmupBars, allowReverse: false);
            var h2r = await Run(spec, backtester, factory, snapshot, state,
                                midDate, snapshot.LastDate, warmupBars, allowReverse: true);
            var h2n = await Run(spec, backtester, factory, snapshot, state,
                                midDate, snapshot.LastDate, warmupBars, allowReverse: false);

            rows.Add(new DiagRow(sig.IndicatorCode, sig.ComponentName, h1r, h1n, h2r, h2n));
        }

        PrintSummary(rows);
        return 0;
    }

    private static StrategySpec MakeSingleLeafSpec(string descriptorId, string label)
    {
        var leaf = new ConditionLeaf(
            Id: $"diag-{descriptorId}",
            SignalDescriptorId: descriptorId,
            Operator: LeafOperator.Fired,
            Score: 1.0);

        // Single-child OR root: matches v11's structure exactly so behavior is comparable.
        var root = new ConditionGroup(
            Id: "diag-root",
            Logic: LogicOperator.Or,
            Children: new List<ConditionNode> { leaf });

        var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 2.0);
        var tpLadder = new List<TpLadderRung>
        {
            new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 1.5, ClosePortion: 0.50),
            new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 3.0, ClosePortion: 0.50),
        };
        var risk = new RiskPlan(
            Stop: stop,
            TpLadder: tpLadder,
            Sizing: new PositionSizing(SizingMode.FixedRiskPercent, RiskPercent: 0.005),
            Entry: new EntryTrigger(EntryTriggerKind.Immediate),
            MinRewardRiskRatio: 1.5,
            StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
            NotionalEquity: 10000.0);

        return new StrategySpec(
            Id: $"diag.long.{descriptorId}",
            Name: $"DIAG: {label}",
            Description: "Single-leaf diagnostic strategy auto-generated by StrategyLab.",
            Side: OrderSide.Buy,
            Conditions: root,
            Risk: risk,
            ExecutionMode: StrategyExecutionMode.Suggestion,
            CreatedUtc: DateTime.UtcNow,
            UpdatedUtc: DateTime.UtcNow,
            IsAutoActivate: false);
    }

    private static async Task<RunResult> Run(
        StrategySpec spec,
        IStrategyBacktester backtester,
        IConfigurableStrategyFactory factory,
        SnapshotFile snapshot,
        WorkspaceState state,
        DateTime start, DateTime end,
        int warmupBars,
        bool allowReverse)
    {
        var config = new BacktestConfig(
            WarmupBars: warmupBars,
            ReplayProfiles: false,
            StartDate: start,
            EndDate: end,
            AllowReverseOnSignal: allowReverse);
        var strat = factory.Create(spec);
        var result = await backtester.RunAsync(strat, snapshot.Bars, config, state);
        var (ciLo, _, ciHi) = BootstrapCi.FromResult(result);
        return new RunResult(
            Trades: result.Metrics.TotalSignals,
            WinRate: result.Metrics.WinRate,
            Pnl: result.Metrics.TotalPnL,
            Sharpe: result.Metrics.SharpeRatio,
            ExpectancyR: double.IsNaN(result.Expectancy) ? 0.0 : result.Expectancy,
            ProfitFactor: double.IsNaN(result.ProfitFactor) ? 0.0 : result.ProfitFactor,
            CiLo: ciLo,
            CiHi: ciHi);
    }

    private static void PrintSummary(List<DiagRow> rows)
    {
        // Two passes: rank by H2-no-reverse expectancy (the most honest measure of entry edge)
        // descending, but also flag any signal whose H1-no-reverse AND H2-no-reverse are both
        // positive — the only walk-forward survivors.
        var ranked = rows
            .OrderByDescending(r => r.H2NoRev.ExpectancyR)
            .ToList();

        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine("CIPHER SIGNAL ISOLATION — RAW ENTRY EDGE (no reverse-on-signal)");
        Console.WriteLine("════════════════════════════════════════════════════════════════════════════════════════");
        // CI columns: 95% bootstrap CI on R-multiple expectancy. Survivor now requires the
        // LOWER bound to be > 0 in both halves — much stronger than a positive point estimate.
        Console.WriteLine($"{"Indicator",-9} {"Signal",-30} {"H1 tr",6} {"H1 R/tr",9} {"H1 95% CI",18} {"H2 tr",6} {"H2 R/tr",9} {"H2 95% CI",18} {"survivor?",10}");
        Console.WriteLine(new string('─', 118));
        foreach (var r in ranked)
        {
            bool survivor = r.H1NoRev.CiLo > 0 && r.H2NoRev.CiLo > 0
                          && r.H1NoRev.Trades >= 5 && r.H2NoRev.Trades >= 5;
            string flag = survivor ? "  YES ✓" : "";
            Console.WriteLine(
                $"{r.IndicatorCode,-9} {Trim(r.ComponentName, 30),-30} " +
                $"{r.H1NoRev.Trades,6} {r.H1NoRev.ExpectancyR,9:+0.000;-0.000; 0.000} {FmtCi(r.H1NoRev),18} " +
                $"{r.H2NoRev.Trades,6} {r.H2NoRev.ExpectancyR,9:+0.000;-0.000; 0.000} {FmtCi(r.H2NoRev),18} " +
                $"{flag,10}");
        }

        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine("STRUCTURAL EDGE (reverse-on-signal allowed) — for comparison");
        Console.WriteLine("════════════════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine($"{"Indicator",-9} {"Signal",-30} {"H1 tr",6} {"H1 P&L",10} {"H1 R/tr",9} {"H2 tr",6} {"H2 P&L",10} {"H2 R/tr",9}");
        Console.WriteLine(new string('─', 92));
        var rankedRev = rows.OrderByDescending(r => r.H2Rev.Pnl).ToList();
        foreach (var r in rankedRev)
        {
            Console.WriteLine(
                $"{r.IndicatorCode,-9} {Trim(r.ComponentName, 30),-30} " +
                $"{r.H1Rev.Trades,6} {r.H1Rev.Pnl,10:+0.0;-0.0; 0.0} {r.H1Rev.ExpectancyR,9:+0.000;-0.000; 0.000} " +
                $"{r.H2Rev.Trades,6} {r.H2Rev.Pnl,10:+0.0;-0.0; 0.0} {r.H2Rev.ExpectancyR,9:+0.000;-0.000; 0.000}");
        }

        Console.WriteLine();
        var survivors = ranked.Where(r =>
            r.H1NoRev.CiLo > 0 && r.H2NoRev.CiLo > 0
            && r.H1NoRev.Trades >= 5 && r.H2NoRev.Trades >= 5).ToList();
        if (survivors.Count == 0)
        {
            Console.WriteLine("VERDICT: No Cipher signal has a 95% CI lower bound > 0 on BOTH halves.");
            Console.WriteLine("         No signal in this set has a statistically reliable raw entry edge.");
            Console.WriteLine("         (Point-positive but CI-straddles-zero signals are sampling noise.)");
        }
        else
        {
            Console.WriteLine($"VERDICT: {survivors.Count} signal(s) survive walk-forward with CI lo > 0 on both halves:");
            foreach (var s in survivors)
                Console.WriteLine($"  - {s.IndicatorCode}.{s.ComponentName}: " +
                    $"H1 +{s.H1NoRev.ExpectancyR:0.000}R [{s.H1NoRev.CiLo:+0.000;-0.000},{s.H1NoRev.CiHi:+0.000;-0.000}], " +
                    $"H2 +{s.H2NoRev.ExpectancyR:0.000}R [{s.H2NoRev.CiLo:+0.000;-0.000},{s.H2NoRev.CiHi:+0.000;-0.000}]");
        }
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s.Substring(0, n);

    private static string FmtCi(RunResult r)
    {
        if (double.IsNaN(r.CiLo) || double.IsNaN(r.CiHi)) return "[ n/a ]";
        return $"[{r.CiLo,+6:+0.00;-0.00; 0.00},{r.CiHi,+6:+0.00;-0.00; 0.00}]";
    }

    private record RunResult(int Trades, double WinRate, double Pnl, double Sharpe, double ExpectancyR, double ProfitFactor, double CiLo = double.NaN, double CiHi = double.NaN);
    private record DiagRow(string IndicatorCode, string ComponentName, RunResult H1Rev, RunResult H1NoRev, RunResult H2Rev, RunResult H2NoRev);
}
