using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.StrategyLab.Catalogue;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Loads a snapshot, builds a workspace with the default indicator pack, instantiates a built-in
/// strategy spec via <see cref="IConfigurableStrategyFactory"/>, runs the backtester, and prints
/// metrics. Walk-forward window is supplied via optional --start / --end (UTC dates).
///
/// Validation goal for the first version: running v11 with the same window we used in the live
/// app (4h Bitstamp BTC/USDT, ~2024-06 → 2025-04) should produce trade counts and metrics
/// that match the live-app CSV (37 trades H1, 27 trades H2 — see project_v11_diagnostic_session
/// memory file). Any divergence indicates a wiring bug between the harness and production.
/// </summary>
public static class RunCommand
{
    public static async Task<int> RunAsync(
        string snapshotPath,
        string specId,
        DateTime? start,
        DateTime? end,
        int warmupBars,
        bool noReverse = false)
    {
        if (!File.Exists(snapshotPath))
        {
            Console.Error.WriteLine($"Snapshot not found: {snapshotPath}");
            return 1;
        }

        Console.WriteLine($"Loading snapshot {snapshotPath}");
        var snapshot = SnapshotCommand.Load(snapshotPath);
        Console.WriteLine($"  {snapshot.Provider} {snapshot.Symbol} {snapshot.Timeframe} — {snapshot.BarCount} bars [{snapshot.FirstDate:yyyy-MM-dd} → {snapshot.LastDate:yyyy-MM-dd}]");

        Console.WriteLine("Building DI host...");
        var host = LabHost.Build();

        Console.WriteLine("Computing indicators...");
        var state = await WorkspaceFactory.BuildAsync(host.Services, snapshot);

        var spec = LabHost.FindBuiltInSpec(specId);
        if (spec == null)
        {
            Console.Error.WriteLine($"Strategy spec id '{specId}' is not in the catalogue.");
            Console.Error.WriteLine("Available spec ids (or run `StrategyLab catalogue list --verbose`):");
            foreach (var s in StrategyCatalogue.AllSpecs())
                Console.Error.WriteLine($"  {s.Id}  ({s.Name})");
            return 2;
        }

        Console.WriteLine($"Strategy: {spec.Name}");
        Console.WriteLine($"  id   = {spec.Id}");
        Console.WriteLine($"  side = {spec.Side}");
        // What we already know, printed BEFORE the numbers. A run that reproduces a known
        // in-sample result is not new evidence, and it is easy to forget that by the time the
        // metrics appear.
        if (spec.Provenance != null)
            Console.WriteLine($"  known = {spec.Provenance.Evidence} · controls so far: {spec.Provenance.Controls}");

        var factory = host.Services.GetRequiredService<IConfigurableStrategyFactory>();
        var strategy = factory.Create(spec);

        var config = new BacktestConfig(
            StartingCapital: 10000.0,
            CommissionRate: 0.001,
            SlippagePercent: 0.0005,
            WarmupBars: warmupBars,
            ReplayProfiles: false, // no profile service registered
            PositionSizer: null,
            StartDate: start,
            EndDate: end,
            AllowReverseOnSignal: !noReverse);

        Console.WriteLine($"Backtest window: {(start?.ToString("yyyy-MM-dd") ?? "(start)")} → {(end?.ToString("yyyy-MM-dd") ?? "(end)")}");
        Console.WriteLine($"  warmup bars = {warmupBars}");
        if (noReverse) Console.WriteLine($"  reverse-on-signal = DISABLED");

        var backtester = host.Services.GetRequiredService<IStrategyBacktester>();
        var result = await backtester.RunAsync(strategy, snapshot.Bars, config, state);

        var m = result.Metrics;
        Console.WriteLine();
        Console.WriteLine("─── RESULTS ────────────────────────────────────────────");
        Console.WriteLine($"  Trades:        {m.TotalSignals}");
        Console.WriteLine($"  Win rate:      {m.WinRate * 100:0.0}%   ({m.WinningTrades} W / {m.TotalSignals - m.WinningTrades} L)");
        Console.WriteLine($"  Total P&L:     {m.TotalPnL:0.00}");
        Console.WriteLine($"  Max drawdown:  {m.MaxDrawdown * 100:0.00}%");
        Console.WriteLine($"  Sharpe:        {m.SharpeRatio:0.000}");
        if (!double.IsNaN(result.AverageR))
            Console.WriteLine($"  Avg R:         {result.AverageR:0.000}");
        if (!double.IsNaN(result.Expectancy))
            Console.WriteLine($"  Expectancy:    {result.Expectancy:0.000} R/trade");
        if (!double.IsNaN(result.ProfitFactor))
            Console.WriteLine($"  Profit factor: {result.ProfitFactor:0.000}");
        Console.WriteLine($"  Avg bars/trade:{result.AverageBarsInTrade:0.0}");
        Console.WriteLine($"  Longest losing streak: {result.LongestLosingStreak}");
        Console.WriteLine($"  Warmup bars:   {result.WarmupBars}");
        Console.WriteLine($"  Evaluated bars:{result.EvaluatedBars}");
        Console.WriteLine("────────────────────────────────────────────────────────");

        // The backtester writes a per-trade CSV to %TEMP% with feature snapshots. Surface the
        // newest one to the user — that's the diagnostic data we feed back into our analysis.
        TryReportLatestCsv();
        return 0;
    }

    /// <summary>
    /// Splits a snapshot at its midpoint and runs the strategy on each half. Saves a round
    /// trip — the user gets H1 and H2 metrics in one CLI call, formatted as a side-by-side
    /// summary so the walk-forward signal is obvious at a glance. The split point is the bar
    /// at index <c>BarCount / 2</c>; both halves get the same warmup so settling is fair.
    /// </summary>
    public static async Task<int> WalkAsync(
        string snapshotPath,
        string specId,
        int warmupBars,
        bool noReverse)
    {
        if (!File.Exists(snapshotPath))
        {
            Console.Error.WriteLine($"Snapshot not found: {snapshotPath}");
            return 1;
        }
        var snapshot = SnapshotCommand.Load(snapshotPath);
        var midIdx = snapshot.Bars.Count / 2;
        var midDate = snapshot.Bars[midIdx].Date;

        Console.WriteLine($"Walk-forward split for {snapshot.Symbol} {snapshot.Timeframe}:");
        Console.WriteLine($"  H1: {snapshot.FirstDate:yyyy-MM-dd} → {midDate:yyyy-MM-dd}  ({midIdx} bars)");
        Console.WriteLine($"  H2: {midDate:yyyy-MM-dd} → {snapshot.LastDate:yyyy-MM-dd}  ({snapshot.BarCount - midIdx} bars)");
        Console.WriteLine();

        Console.WriteLine("─── H1 ──────────────────────────────────────────────────");
        var h1 = await RunAsync(snapshotPath, specId, snapshot.FirstDate, midDate, warmupBars, noReverse);
        if (h1 != 0) return h1;

        Console.WriteLine();
        Console.WriteLine("─── H2 ──────────────────────────────────────────────────");
        var h2 = await RunAsync(snapshotPath, specId, midDate, snapshot.LastDate, warmupBars, noReverse);
        return h2;
    }

    private static void TryReportLatestCsv()
    {
        try
        {
            var tmp = Path.GetTempPath();
            var newest = new DirectoryInfo(tmp)
                .EnumerateFiles("accessible-trader-backtest-*.csv")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (newest != null)
                Console.WriteLine($"  CSV: {newest.FullName}");
        }
        catch { /* best-effort */ }
    }
}
