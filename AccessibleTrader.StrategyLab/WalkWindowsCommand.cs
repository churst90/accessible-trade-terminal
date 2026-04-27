using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Core.Services.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// N-window chronological walk-forward — replaces the brittle H1/H2 split for
/// regime-conditional strategies.
///
/// Discovered 2026-04-27 during v22 short-side analysis: the half-and-half
/// split obscures regime-conditional edge because each half contains a
/// mixture of bull / bear / range periods. v22-short showed −0.39R H2 on a
/// single-split, but a 6-window decomposition revealed a clean +0.99R / +0.98R
/// pattern across distribution-and-bear windows that was being averaged away
/// against a −1.00R 2017-bull window. Future strategy validation should
/// always run this command first, fall back to the half-split only when
/// snapshots are too short to slice further.
///
/// Usage:
///   StrategyLab walk-windows --snapshot &lt;path&gt; --spec &lt;id&gt;
///                            [--windows 6] [--warmup 200] [--no-reverse]
///
/// Builds the DI host + indicator workspace once (heavy step); runs the
/// backtester N times with different StartDate / EndDate filters; prints a
/// single per-window summary table at the end. Each window covers
/// (lastDate - firstDate) / N of the snapshot's date span.
/// </summary>
public static class WalkWindowsCommand
{
    public sealed record WindowResult(
        DateTime Start,
        DateTime End,
        int Trades,
        double AvgR,
        double WinRate,
        double ProfitFactor,
        double MaxDrawdown,
        int EvaluatedBars);

    public static async Task<int> RunAsync(
        string snapshotPath,
        string specId,
        int windows,
        int warmupBars,
        bool noReverse)
    {
        if (!File.Exists(snapshotPath))
        {
            Console.Error.WriteLine($"Snapshot not found: {snapshotPath}");
            return 1;
        }
        if (windows < 2)
        {
            Console.Error.WriteLine("--windows must be ≥ 2");
            return 1;
        }

        var snapshot = SnapshotCommand.Load(snapshotPath);
        Console.WriteLine($"Snapshot: {snapshot.Provider} {snapshot.Symbol} {snapshot.Timeframe} ({snapshot.BarCount} bars, {snapshot.FirstDate:yyyy-MM-dd} → {snapshot.LastDate:yyyy-MM-dd})");
        Console.WriteLine($"Windows:  {windows}, Warmup: {warmupBars} bars per window");
        Console.WriteLine();

        var spec = LabHost.FindBuiltInSpec(specId);
        if (spec == null)
        {
            Console.Error.WriteLine($"Strategy spec id '{specId}' not found.");
            return 2;
        }
        Console.WriteLine($"Strategy: {spec.Name}  ({spec.Side})");

        Console.WriteLine("Building host + computing indicators (one-time)...");
        var host = LabHost.Build();
        var state = await WorkspaceFactory.BuildAsync(host.Services, snapshot);
        var factory = host.Services.GetRequiredService<IConfigurableStrategyFactory>();
        var backtester = host.Services.GetRequiredService<IStrategyBacktester>();
        Console.WriteLine();

        // Slice the date span into N equal windows. Inclusive of firstDate at
        // window 0 start and lastDate at window N-1 end; intermediate boundaries
        // are exact (no overlap, no gap).
        var span = snapshot.LastDate - snapshot.FirstDate;
        var step = TimeSpan.FromTicks(span.Ticks / windows);
        var results = new List<WindowResult>(windows);

        for (int i = 0; i < windows; i++)
        {
            var winStart = snapshot.FirstDate + TimeSpan.FromTicks(step.Ticks * i);
            var winEnd   = i == windows - 1
                ? snapshot.LastDate
                : snapshot.FirstDate + TimeSpan.FromTicks(step.Ticks * (i + 1));

            var strategy = factory.Create(spec);
            var config = new BacktestConfig(
                StartingCapital: 10000.0,
                CommissionRate: 0.001,
                SlippagePercent: 0.0005,
                WarmupBars: warmupBars,
                ReplayProfiles: false,
                PositionSizer: null,
                StartDate: winStart,
                EndDate: winEnd,
                AllowReverseOnSignal: !noReverse);

            var r = await backtester.RunAsync(strategy, snapshot.Bars, config, state);
            var m = r.Metrics;
            results.Add(new WindowResult(
                Start: winStart,
                End: winEnd,
                Trades: m.TotalSignals,
                AvgR: r.AverageR,
                WinRate: m.WinRate,
                ProfitFactor: r.ProfitFactor,
                MaxDrawdown: m.MaxDrawdown,
                EvaluatedBars: r.EvaluatedBars));
        }

        // Summary table.
        Console.WriteLine("─── PER-WINDOW RESULTS ─────────────────────────────────────────");
        Console.WriteLine("  Window                               n     avgR     WR     PF       maxDD");
        foreach (var w in results)
        {
            string avgR  = double.IsNaN(w.AvgR) ? "  n/a " : w.AvgR.ToString("+0.000;-0.000; 0.000");
            string wr    = w.Trades > 0 ? (w.WinRate * 100).ToString("0.0") + "%" : " n/a ";
            string pf    = double.IsNaN(w.ProfitFactor) ? "n/a"
                          : double.IsInfinity(w.ProfitFactor) ? "∞"
                          : w.ProfitFactor.ToString("0.00");
            string mdd   = (w.MaxDrawdown * 100).ToString("0.00") + "%";
            Console.WriteLine(
                $"  {w.Start:yyyy-MM-dd}..{w.End:yyyy-MM-dd}  " +
                $"{w.Trades,4}  {avgR,7}  {wr,6}  {pf,6}  {mdd,7}");
        }
        Console.WriteLine();

        // Aggregate signal across windows.
        int positiveWindows = results.Count(w => w.Trades > 0 && !double.IsNaN(w.AvgR) && w.AvgR > 0);
        int tradedWindows   = results.Count(w => w.Trades > 0);
        int totalTrades     = results.Sum(w => w.Trades);
        double meanR = tradedWindows > 0
            ? results.Where(w => w.Trades > 0 && !double.IsNaN(w.AvgR)).Average(w => w.AvgR)
            : double.NaN;

        Console.WriteLine("─── AGGREGATE ──────────────────────────────────────────────────");
        Console.WriteLine($"  Windows traded:           {tradedWindows} / {windows}");
        Console.WriteLine($"  Windows positive avgR:    {positiveWindows} / {tradedWindows}");
        Console.WriteLine($"  Total trades:             {totalTrades}");
        Console.WriteLine($"  Mean avgR across windows: {(double.IsNaN(meanR) ? "n/a" : meanR.ToString("+0.000;-0.000; 0.000"))}");
        Console.WriteLine("────────────────────────────────────────────────────────────────");

        return 0;
    }
}
