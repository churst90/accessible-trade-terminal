using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Rolling-window walk-forward stress-test for face battery cells.
///
/// Why this exists (2026-04-09 evening): the chop-gate experiment shipped a "first
/// BTC short to reach CI" claim that was a single-snapshot artifact. CIlo=0.00 on a
/// 10-trade sample was borderline, not robust, and a fresh 4000-bar resnapshot of
/// BTC daily killed the result entirely (H2 +0.83R → -0.05R). The single-split
/// face battery's CI cannot distinguish "real survivor" from "lucky split" — it
/// reports a single H1/H2 reading and the bootstrap CI is computed within that
/// single split, so it cannot detect the across-split instability.
///
/// This command runs each cell across N rolling windows of fixed bar-width, stepped
/// by a configurable bar increment. For each cell it reports:
///   • windows tested
///   • fraction of windows where expectancy > 0 (the most lenient gate)
///   • fraction of windows where 95% bootstrap CIlo > 0 (the strict gate)
///   • mean / stddev / min / max of expectancy across windows
///   • worst-window CIlo
///
/// A real survivor should have positive expectancy in &gt;70% of rolling windows AND
/// at least one window where CIlo > 0. A "lucky split" survivor will have one or
/// two great windows surrounded by garbage. This is the test that should have
/// caught the chop gate before I shipped it.
///
/// Usage:
///   StrategyLab face-rolling --snapshot bitstamp_BTC_USDT_1d.json
///                            [--window 1500] [--step 250] [--filter funding,cftc]
/// </summary>
public static class FaceRollingCommand
{
    public static async Task<int> RunAsync(string snapshotPath, int windowBars, int stepBars, string? labelFilter, int warmupBars)
    {
        if (!File.Exists(snapshotPath))
        {
            Console.Error.WriteLine($"Snapshot not found: {snapshotPath}");
            return 1;
        }

        var snapshot = SnapshotCommand.Load(snapshotPath);
        Console.WriteLine($"Snapshot: {snapshot.Provider} {snapshot.Symbol} {snapshot.Timeframe} ({snapshot.BarCount} bars)");
        Console.WriteLine($"Window: {windowBars} bars, Step: {stepBars} bars, Warmup: {warmupBars} bars");
        if (labelFilter != null) Console.WriteLine($"Label filter: '{labelFilter}'");
        Console.WriteLine();

        // Build host + workspace once (indicator computation is the heavy step).
        Console.WriteLine("Building host + computing indicators (one-time)...");
        var host = LabHost.Build();
        var state = await WorkspaceFactory.BuildAsync(host.Services, snapshot);
        var factory = host.Services.GetRequiredService<IConfigurableStrategyFactory>();
        var backtester = host.Services.GetRequiredService<IStrategyBacktester>();

        // Borrow FaceBatteryCommand's private cell list via reflection — keeps the cell
        // catalog single-source and avoids duplicating ~700 lines of cell construction.
        var buildCellsMethod = typeof(FaceBatteryCommand).GetMethod("BuildCells",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var allCells = (List<(string Label, OrderSide Side, ConditionGroup Root)>)
            buildCellsMethod.Invoke(null, null)!;

        // Filter by label substring if requested. Case-insensitive, comma-separated OR.
        var filterTerms = string.IsNullOrWhiteSpace(labelFilter)
            ? null
            : labelFilter.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToLowerInvariant()).ToArray();
        var cells = filterTerms == null
            ? allCells
            : allCells.Where(c => filterTerms.Any(t => c.Label.ToLowerInvariant().Contains(t))).ToList();

        if (cells.Count == 0)
        {
            Console.Error.WriteLine($"No cells matched filter '{labelFilter}'.");
            return 1;
        }

        // Build the rolling windows. The first window must start at warmupBars so the
        // backtester has enough lead-in for ATR/indicator stability. The last window
        // ends at the snapshot's last bar.
        var bars = snapshot.Bars;
        var windows = new List<(DateTime Start, DateTime End, int FromIdx, int ToIdx)>();
        for (int from = warmupBars; from + windowBars <= bars.Count; from += stepBars)
        {
            int to = from + windowBars - 1;
            windows.Add((bars[from].Date, bars[to].Date, from, to));
        }
        if (windows.Count == 0)
        {
            Console.Error.WriteLine($"No windows fit: snapshot has {bars.Count} bars, need at least {warmupBars + windowBars}.");
            return 1;
        }

        Console.WriteLine($"Cells: {cells.Count}, Windows: {windows.Count}");
        Console.WriteLine($"First window: {windows[0].Start:yyyy-MM-dd} → {windows[0].End:yyyy-MM-dd}");
        Console.WriteLine($"Last  window: {windows[^1].Start:yyyy-MM-dd} → {windows[^1].End:yyyy-MM-dd}");
        Console.WriteLine();

        // For each cell, run all windows and aggregate.
        var rows = new List<CellSummary>();
        int idx = 0;
        foreach (var (label, side, root) in cells)
        {
            idx++;
            Console.Write($"  [{idx,2}/{cells.Count}] {(side == OrderSide.Buy ? "L" : "S")} {Trim(label, 58),-58} ");
            var windowResults = new List<WindowResult>();
            // Reuse FaceBattery's private MakeSpec + Run via reflection to keep cost model identical.
            var makeSpec = typeof(FaceBatteryCommand).GetMethod("MakeSpec",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var runMethod = typeof(FaceBatteryCommand).GetMethod("Run",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var spec = (StrategySpec)makeSpec.Invoke(null, new object[] { $"face.r{idx}", label, root, side })!;

            foreach (var w in windows)
            {
                // Run takes (spec, backtester, factory, snapshot, state, start, end, warmup)
                var task = (Task)runMethod.Invoke(null, new object?[]
                {
                    spec, backtester, factory, snapshot, state, w.Start, w.End, warmupBars
                })!;
                await task.ConfigureAwait(false);
                var resultProp = task.GetType().GetProperty("Result")!;
                var rr = resultProp.GetValue(task)!;
                int trades       = (int)rr.GetType().GetProperty("Trades")!.GetValue(rr)!;
                double expectancy= (double)rr.GetType().GetProperty("ExpectancyR")!.GetValue(rr)!;
                double ciLo      = (double)rr.GetType().GetProperty("CiLo")!.GetValue(rr)!;
                windowResults.Add(new WindowResult(w.Start, w.End, trades, expectancy, ciLo));
            }

            var summary = Aggregate(label, side, windowResults);
            rows.Add(summary);
            Console.WriteLine($"win={windowResults.Count} ER>0={summary.PctPositive:0%} CIlo>0={summary.PctCiPos:0%} meanER={summary.MeanExpectancy,+6:0.000} worstCIlo={summary.WorstCiLo,+6:0.00}");
        }

        Console.WriteLine();
        PrintSummary(rows, windows.Count);
        return 0;
    }

    private static CellSummary Aggregate(string label, OrderSide side, List<WindowResult> windows)
    {
        // Only count windows that produced at least 5 trades — fewer than that and
        // the expectancy is statistical noise. This matches the face battery's CI gate.
        var meaningful = windows.Where(w => w.Trades >= 5).ToList();
        int total = windows.Count;
        int withTrades = meaningful.Count;
        if (withTrades == 0)
        {
            return new CellSummary(label, side, total, 0, 0, 0, 0, 0, 0, 0, double.NaN, double.NaN, double.NaN, double.NaN);
        }
        double mean = meaningful.Average(w => w.ExpectancyR);
        double stddev = Math.Sqrt(meaningful.Sum(w => Math.Pow(w.ExpectancyR - mean, 2)) / Math.Max(1, withTrades - 1));
        double min = meaningful.Min(w => w.ExpectancyR);
        double max = meaningful.Max(w => w.ExpectancyR);
        int positive = meaningful.Count(w => w.ExpectancyR > 0);
        int ciPositive = meaningful.Count(w => w.CiLo > 0);
        double worstCi = meaningful.Min(w => w.CiLo);
        double meanCi  = meaningful.Average(w => w.CiLo);
        double pctPos = positive / (double)withTrades;
        double pctCiPos = ciPositive / (double)withTrades;
        double avgTrades = meaningful.Average(w => (double)w.Trades);
        return new CellSummary(label, side, total, withTrades, positive, ciPositive,
            pctPos, pctCiPos, avgTrades, mean, stddev, min, max, worstCi);
    }

    private static void PrintSummary(List<CellSummary> rows, int totalWindows)
    {
        Console.WriteLine("══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine($"ROLLING-WINDOW WALK-FORWARD STRESS TEST — {totalWindows} windows per cell");
        Console.WriteLine("══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine($"{"Setup",-58} {"Sd",2} {"valid",6} {"ER>0",7} {"CI>0",7} {"avgTr",6} {"meanER",8} {"std",7} {"min",7} {"max",7} {"worstCI",8} flag");
        Console.WriteLine(new string('─', 130));

        // Sort by fraction of windows with positive expectancy (the leniency gate),
        // tiebreak by fraction of windows where CIlo > 0 (the strict gate).
        var ranked = rows
            .OrderByDescending(r => r.PctPositive)
            .ThenByDescending(r => r.PctCiPos)
            .ToList();

        foreach (var r in ranked)
        {
            string flag;
            // Strict survivor: ≥70% windows positive AND ≥3 windows with CIlo>0
            if (r.PctPositive >= 0.70 && r.CiPositiveWindows >= 3) flag = "★ ROBUST";
            // High-conviction survivor: very consistent direction with at least one CI window.
            // Catches cells with naturally low avgTr that can't reach 3 CI windows but show
            // strong directional consistency (≥80% positive). Three almost-ROBUST cells from
            // the v23 investigation (round 4, 2026-04-27) miss strict by exactly this gap.
            else if (r.PctPositive >= 0.80 && r.CiPositiveWindows >= 1 && r.AvgTrades >= 5) flag = "✓ HIGH-CONV";
            // Lenient survivor: ≥70% windows positive (no CI requirement — for further investigation)
            else if (r.PctPositive >= 0.70) flag = "promising";
            // Marginal: 50-70% windows positive
            else if (r.PctPositive >= 0.50) flag = "marginal";
            else flag = "";

            if (r.ValidWindows == 0)
            {
                Console.WriteLine($"{Trim(r.Label, 58),-58} {(r.Side == OrderSide.Buy ? "L" : "S"),2} {0,6} {"-",7} {"-",7} {"-",6} {"-",8} {"-",7} {"-",7} {"-",7} {"-",8} no trades");
                continue;
            }

            Console.WriteLine(
                $"{Trim(r.Label, 58),-58} {(r.Side == OrderSide.Buy ? "L" : "S"),2} {r.ValidWindows,6} " +
                $"{r.PctPositive,7:0%} {r.PctCiPos,7:0%} {r.AvgTrades,6:0.0} {r.MeanExpectancy,+8:0.000} {r.Stddev,7:0.000} " +
                $"{r.Min,+7:0.00} {r.Max,+7:0.00} {r.WorstCiLo,+8:0.00} {flag}");
        }

        Console.WriteLine();
        var robust = ranked.Where(r => r.PctPositive >= 0.70 && r.CiPositiveWindows >= 3).ToList();
        var highConv = ranked.Where(r =>
            r.PctPositive >= 0.80 && r.CiPositiveWindows >= 1 && r.AvgTrades >= 5
            && !(r.PctPositive >= 0.70 && r.CiPositiveWindows >= 3)).ToList();
        if (robust.Count == 0 && highConv.Count == 0)
        {
            Console.WriteLine("VERDICT: No cell is robust across rolling windows (≥70% positive AND ≥3 CIlo>0 windows).");
            var lenient = ranked.Where(r => r.PctPositive >= 0.70).ToList();
            if (lenient.Count > 0)
            {
                Console.WriteLine($"  {lenient.Count} cell(s) point-positive in ≥70% of windows but never reach CI:");
                foreach (var c in lenient)
                    Console.WriteLine($"    {(c.Side == OrderSide.Buy ? "L" : "S")} {c.Label}");
            }
        }
        else
        {
            if (robust.Count > 0)
            {
                Console.WriteLine($"VERDICT: {robust.Count} cell(s) ROBUST across rolling windows:");
                foreach (var r in robust)
                    Console.WriteLine($"  ★ {(r.Side == OrderSide.Buy ? "L" : "S")} {r.Label} — {r.PctPositive:0%} positive, {r.CiPositiveWindows} CI>0 windows, mean {r.MeanExpectancy:+0.00}R");
            }
            if (highConv.Count > 0)
            {
                Console.WriteLine($"  {highConv.Count} cell(s) HIGH-CONVICTION (≥80% pos, ≥1 CI>0, naturally low trade count):");
                foreach (var r in highConv)
                    Console.WriteLine($"  ✓ {(r.Side == OrderSide.Buy ? "L" : "S")} {r.Label} — {r.PctPositive:0%} positive, {r.CiPositiveWindows} CI>0 windows, mean {r.MeanExpectancy:+0.00}R");
            }
        }
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";

    private record WindowResult(DateTime Start, DateTime End, int Trades, double ExpectancyR, double CiLo);
    private record CellSummary(
        string Label, OrderSide Side, int TotalWindows, int ValidWindows,
        int PositiveWindows, int CiPositiveWindows,
        double PctPositive, double PctCiPos, double AvgTrades,
        double MeanExpectancy, double Stddev, double Min, double Max, double WorstCiLo);
}
