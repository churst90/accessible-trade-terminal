using System.Reflection;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Asset characterization harness (round 12, 2026-06-12). Answers the question the
/// strategy research keeps circling: "how do you analyze an asset, and what carries
/// across assets?" For every snapshot it emits a fingerprint:
///
///   • Volatility MATURATION — annualized realized vol over the first third of the
///     asset's history vs the last third, and the ratio. &lt;1 = vol has decayed as the
///     asset matured (BTC, ETH); ≈1 or &gt;1 = still young/expanding (SOL, KAS).
///   • Regime character — mean Hurst, % of bars trending (H&gt;0.55) vs mean-reverting
///     (H&lt;0.45), and % of bars in a bull regime (close &gt; SMA200).
///   • Setup edge, LONG and SHORT — a curated probe of Cipher-B reversal setups run
///     through rolling walk-forward windows, reporting mean per-trade R and the
///     fraction of windows positive (the suite's robustness metric).
///
/// The cross-asset footer sorts the fingerprints so common threads surface: does the
/// long edge track the maturation ratio? Do shorts ever work, and on what kind of
/// asset? This is the input to "build a per-asset strategy" rather than chasing a
/// one-size-fits-all gate.
/// </summary>
public static class AssetProfileCommand
{
    private const int Warmup = 150;
    private const int MinTradesPerWindow = 3;

    public static async Task<int> RunAsync(string snapshotDir, string? only)
    {
        if (!Directory.Exists(snapshotDir))
        {
            Console.Error.WriteLine($"Snapshot dir not found: {snapshotDir}");
            return 1;
        }

        // Price snapshots only — skip the xs_* cross-series economic files.
        var files = Directory.GetFiles(snapshotDir, "*_1d.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();
        if (only != null)
        {
            var terms = only.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().ToLowerInvariant()).ToArray();
            files = files.Where(f => terms.Any(t => Path.GetFileName(f).ToLowerInvariant().Contains(t))).ToList();
        }
        if (files.Count == 0) { Console.Error.WriteLine("No matching snapshots."); return 1; }

        Console.WriteLine($"Profiling {files.Count} asset(s) at 1d. Warmup {Warmup} bars.\n");

        var makeSpec = typeof(FaceBatteryCommand).GetMethod("MakeSpec", BindingFlags.NonPublic | BindingFlags.Static)!;
        var runMethod = typeof(FaceBatteryCommand).GetMethod("Run", BindingFlags.NonPublic | BindingFlags.Static)!;

        var fingerprints = new List<Fingerprint>();

        foreach (var file in files)
        {
            SnapshotFile snapshot;
            try { snapshot = SnapshotCommand.Load(file); }
            catch (Exception ex) { Console.Error.WriteLine($"  ! {Path.GetFileName(file)}: load failed ({ex.Message})"); continue; }

            var bars = snapshot.Bars;
            if (bars.Count < Warmup + 400)
            {
                Console.WriteLine($"  - {snapshot.Symbol}: only {bars.Count} bars, too short to profile, skipped.");
                continue;
            }

            // Adaptive window so younger assets still get ~5-7 windows for a fair %positive.
            int window = Math.Min(1200, (int)((bars.Count - Warmup) * 0.55));
            int step = Math.Max(120, window / 4);

            Console.Write($"  · {snapshot.Symbol,-10} {bars.Count,5} bars  building indicators… ");
            var host = LabHost.Build();
            WorkspaceState state;
            try { state = await WorkspaceFactory.BuildAsync(host.Services, snapshot); }
            catch (Exception ex) { Console.WriteLine($"FAILED ({ex.Message})"); continue; }
            var factory = host.Services.GetRequiredService<IConfigurableStrategyFactory>();
            var backtester = host.Services.GetRequiredService<IStrategyBacktester>();
            Console.WriteLine("done");

            var fp = new Fingerprint { Symbol = snapshot.Symbol, Bars = bars.Count };
            ComputeDescriptiveStats(bars, state, fp);

            // Build the rolling windows once, shared across every probe cell.
            var windows = new List<(DateTime Start, DateTime End)>();
            for (int from = Warmup; from + window <= bars.Count; from += step)
                windows.Add((bars[from].Date, bars[from + window - 1].Date));
            fp.NWindows = windows.Count;

            foreach (var probe in BuildProbes())
            {
                var spec = (StrategySpec)makeSpec.Invoke(null, new object[] { $"prof.{probe.Key}", probe.Key, probe.Root, probe.Side })!;
                var ers = new List<double>();
                int tradeSum = 0, validWindows = 0;
                foreach (var w in windows)
                {
                    var task = (Task)runMethod.Invoke(null, new object?[] { spec, backtester, factory, snapshot, state, w.Start, w.End, Warmup })!;
                    await task.ConfigureAwait(false);
                    // RunResult is a private record; access its members by reflection
                    // (dynamic can't bind to members of a type it can't see).
                    object res = task.GetType().GetProperty("Result")!.GetValue(task)!;
                    var resType = res.GetType();
                    int trades = (int)resType.GetProperty("Trades")!.GetValue(res)!;
                    if (trades < MinTradesPerWindow) continue;
                    validWindows++;
                    tradeSum += trades;
                    ers.Add((double)resType.GetProperty("ExpectancyR")!.GetValue(res)!);
                }
                fp.Probes[probe.Key] = new ProbeResult
                {
                    MeanER = ers.Count > 0 ? ers.Average() : double.NaN,
                    PctPositive = ers.Count > 0 ? (double)ers.Count(e => e > 0) / ers.Count : double.NaN,
                    ValidWindows = validWindows,
                    AvgTrades = validWindows > 0 ? (double)tradeSum / validWindows : 0,
                };
            }

            fingerprints.Add(fp);
            PrintFingerprint(fp);
        }

        PrintCrossAssetThreads(fingerprints);
        return 0;
    }

    // ── Descriptive stats ─────────────────────────────────────────────────────

    private static void ComputeDescriptiveStats(IReadOnlyList<Ohlcv> bars, WorkspaceState state, Fingerprint fp)
    {
        int n = bars.Count;
        // Log returns.
        var r = new double[n];
        for (int i = 1; i < n; i++)
            r[i] = bars[i - 1].Close > 0 && bars[i].Close > 0 ? Math.Log(bars[i].Close / bars[i - 1].Close) : 0;

        // Annualized realized vol over first third vs last third (daily → ×sqrt(365)).
        int third = n / 3;
        fp.VolEarly = AnnualizedVol(r, 1, third);
        fp.VolLate = AnnualizedVol(r, n - third, n);
        fp.VolMaturationRatio = fp.VolEarly > 0 ? fp.VolLate / fp.VolEarly : double.NaN;

        // Buy & hold drift (annualized log return) and max drawdown.
        double totalLog = 0;
        for (int i = 1; i < n; i++) totalLog += r[i];
        double years = (bars[^1].Date - bars[0].Date).TotalDays / 365.25;
        fp.AnnualDriftPct = years > 0 ? (Math.Exp(totalLog / years) - 1) * 100 : double.NaN;

        double peak = bars[0].Close, maxDd = 0;
        foreach (var b in bars)
        {
            if (b.Close > peak) peak = b.Close;
            double dd = peak > 0 ? (peak - b.Close) / peak : 0;
            if (dd > maxDd) maxDd = dd;
        }
        fp.BuyHoldMaxDdPct = maxDd * 100;

        // Hurst regime character.
        var hurst = ReadComponent(state, "HURST", "Hurst");
        if (hurst != null)
        {
            var valid = hurst.Where(v => !double.IsNaN(v)).ToList();
            if (valid.Count > 0)
            {
                fp.MeanHurst = valid.Average();
                fp.PctTrending = (double)valid.Count(v => v > 0.55) / valid.Count;
                fp.PctMeanReverting = (double)valid.Count(v => v < 0.45) / valid.Count;
            }
        }

        // Bull bias: % bars above SMA200.
        var regime = ReadComponent(state, "REGIME", "AboveSma200");
        if (regime != null)
        {
            var valid = regime.Where(v => !double.IsNaN(v)).ToList();
            if (valid.Count > 0)
                fp.PctBullRegime = (double)valid.Count(v => v > 0) / valid.Count;
        }
    }

    private static double AnnualizedVol(double[] r, int lo, int hi)
    {
        var slice = new List<double>();
        for (int i = Math.Max(1, lo); i < hi && i < r.Length; i++) slice.Add(r[i]);
        if (slice.Count < 2) return double.NaN;
        double mean = slice.Average();
        double var = slice.Select(x => (x - mean) * (x - mean)).Average();
        return Math.Sqrt(var) * Math.Sqrt(365) * 100; // percent
    }

    private static double[]? ReadComponent(WorkspaceState state, string code, string component)
    {
        var series = state.ActiveSeries.FirstOrDefault(s => s.Name == code);
        if (series == null) return null;
        try { return series.GetComponentData(component); }
        catch { return null; }
    }

    // ── Probe cells ───────────────────────────────────────────────────────────

    private static List<Probe> BuildProbes()
    {
        ConditionLeaf Fired(string id, string sig, int within) =>
            new(Id: id, SignalDescriptorId: sig, Operator: LeafOperator.FiredWithin, WithinNBars: within, Score: 1.0);
        ConditionLeaf Lt(string id, string sig, double v) =>
            new(Id: id, SignalDescriptorId: sig, Operator: LeafOperator.LessThan, Value: v, Score: 1.0);
        ConditionLeaf Gt(string id, string sig, double v) =>
            new(Id: id, SignalDescriptorId: sig, Operator: LeafOperator.GreaterThan, Value: v, Score: 1.0);
        ConditionGroup G(string id, LogicOperator op, params ConditionNode[] c) => new(Id: id, Logic: op, Children: c.ToList());

        return new List<Probe>
        {
            // LONG probes.
            new("L:v23", OrderSide.Buy, G("p-v23l", LogicOperator.And,
                G("p-v23l-t", LogicOperator.Or,
                    Fired("p-v23l-wt", "CIPHER_B.WaveTrend Cross Bull", 2),
                    Fired("p-v23l-blue", "CIPHER_B.Oversold Crossover", 2),
                    Fired("p-v23l-div", "CIPHER_B.Bullish Divergence", 2)),
                Lt("p-v23l-anc", "CIPHER_B.Anchor Wave", 0))),
            new("L:div", OrderSide.Buy, G("p-divl", LogicOperator.And,
                Fired("p-divl-d", "CIPHER_B.Bullish Divergence", 2),
                Lt("p-divl-anc", "CIPHER_B.Anchor Wave", 0))),
            new("L:blue", OrderSide.Buy, G("p-bluel", LogicOperator.And,
                Fired("p-bluel-b", "CIPHER_B.Oversold Crossover", 2),
                Lt("p-bluel-anc", "CIPHER_B.Anchor Wave", 0))),

            // SHORT probes (mirror).
            new("S:v23", OrderSide.Sell, G("p-v23s", LogicOperator.And,
                G("p-v23s-t", LogicOperator.Or,
                    Fired("p-v23s-wt", "CIPHER_B.WaveTrend Cross Bear", 2),
                    Fired("p-v23s-red", "CIPHER_B.Overbought Crossover", 2),
                    Fired("p-v23s-div", "CIPHER_B.Bearish Divergence", 2)),
                Gt("p-v23s-anc", "CIPHER_B.Anchor Wave", 0))),
            new("S:div", OrderSide.Sell, G("p-divs", LogicOperator.And,
                Fired("p-divs-d", "CIPHER_B.Bearish Divergence", 2),
                Gt("p-divs-anc", "CIPHER_B.Anchor Wave", 0))),
            new("S:red", OrderSide.Sell, G("p-reds", LogicOperator.And,
                Fired("p-reds-r", "CIPHER_B.Overbought Crossover", 2),
                Gt("p-reds-anc", "CIPHER_B.Anchor Wave", 0))),
        };
    }

    // ── Output ────────────────────────────────────────────────────────────────

    private static void PrintFingerprint(Fingerprint fp)
    {
        Console.WriteLine();
        Console.WriteLine($"    ┌─ {fp.Symbol}  ({fp.Bars} bars, {fp.NWindows} windows) ─────────────────────");
        Console.WriteLine($"    │ Vol maturation : early {fp.VolEarly,5:F0}%  → late {fp.VolLate,5:F0}%   ratio {fp.VolMaturationRatio,4:F2}  ({MaturationLabel(fp.VolMaturationRatio)})");
        Console.WriteLine($"    │ Regime         : meanHurst {fp.MeanHurst,4:F2}  trending {fp.PctTrending,4:P0}  meanRev {fp.PctMeanReverting,4:P0}  bull {fp.PctBullRegime,4:P0}");
        Console.WriteLine($"    │ Buy&hold       : drift {fp.AnnualDriftPct,6:F0}%/yr   maxDD {fp.BuyHoldMaxDdPct,4:F0}%");
        Console.WriteLine($"    │ LONG edge      : v23 {Fmt(fp.Probes.GetValueOrDefault("L:v23"))}  div {Fmt(fp.Probes.GetValueOrDefault("L:div"))}  blue {Fmt(fp.Probes.GetValueOrDefault("L:blue"))}");
        Console.WriteLine($"    │ SHORT edge     : v23 {Fmt(fp.Probes.GetValueOrDefault("S:v23"))}  div {Fmt(fp.Probes.GetValueOrDefault("S:div"))}  red  {Fmt(fp.Probes.GetValueOrDefault("S:red"))}");
        Console.WriteLine($"    └─────────────────────────────────────────────────────────────────────");
    }

    private static string Fmt(ProbeResult? p)
    {
        if (p == null || p.ValidWindows == 0 || double.IsNaN(p.MeanER)) return "  —      ";
        return $"{p.MeanER,+5:F2}R/{p.PctPositive,3:P0}";
    }

    private static string MaturationLabel(double ratio)
    {
        if (double.IsNaN(ratio)) return "?";
        if (ratio < 0.65) return "matured, vol halved+";
        if (ratio < 0.85) return "maturing";
        if (ratio < 1.15) return "stable";
        return "still expanding";
    }

    private static void PrintCrossAssetThreads(List<Fingerprint> fps)
    {
        if (fps.Count == 0) return;
        Console.WriteLine();
        Console.WriteLine("══════════════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine("CROSS-ASSET THREADS");
        Console.WriteLine("══════════════════════════════════════════════════════════════════════════════════════");

        Console.WriteLine("\nSorted by volatility maturation (most-matured first):");
        Console.WriteLine($"  {"Asset",-8} {"ratio",6} {"Hurst",6} {"bull%",6}   {"L:v23",9} {"L:div",9} {"L:blue",9}   {"S:v23",9} {"S:div",9} {"S:red",9}");
        foreach (var fp in fps.OrderBy(f => double.IsNaN(f.VolMaturationRatio) ? 99 : f.VolMaturationRatio))
        {
            Console.WriteLine($"  {fp.Symbol,-8} {fp.VolMaturationRatio,6:F2} {fp.MeanHurst,6:F2} {fp.PctBullRegime,6:P0}   " +
                $"{Fmt(fp.Probes.GetValueOrDefault("L:v23")),9} {Fmt(fp.Probes.GetValueOrDefault("L:div")),9} {Fmt(fp.Probes.GetValueOrDefault("L:blue")),9}   " +
                $"{Fmt(fp.Probes.GetValueOrDefault("S:v23")),9} {Fmt(fp.Probes.GetValueOrDefault("S:div")),9} {Fmt(fp.Probes.GetValueOrDefault("S:red")),9}");
        }

        // Aggregate thread tests: does long edge correlate with anything?
        Console.WriteLine("\nThread tests (mean over assets where the probe had ≥3 valid windows):");
        ThreadLine(fps, "L:v23"); ThreadLine(fps, "L:div"); ThreadLine(fps, "L:blue");
        ThreadLine(fps, "S:v23"); ThreadLine(fps, "S:div"); ThreadLine(fps, "S:red");

        // Correlation of long-divergence edge vs maturation ratio (the headline question).
        var paired = fps.Where(f => f.Probes.TryGetValue("L:div", out var p) && p.ValidWindows >= 3 && !double.IsNaN(f.VolMaturationRatio))
            .Select(f => (x: f.VolMaturationRatio, y: f.Probes["L:div"].MeanER)).ToList();
        if (paired.Count >= 3)
        {
            double corr = Pearson(paired.Select(p => p.x).ToArray(), paired.Select(p => p.y).ToArray());
            Console.WriteLine($"\n  corr(vol-maturation-ratio, L:div meanER) = {corr,5:F2}  over {paired.Count} assets");
            Console.WriteLine("    (positive ⇒ divergence-long pays MORE on still-expanding assets; negative ⇒ pays more on matured ones)");
        }
    }

    private static void ThreadLine(List<Fingerprint> fps, string key)
    {
        var vals = fps.Where(f => f.Probes.TryGetValue(key, out var p) && p.ValidWindows >= 3 && !double.IsNaN(p.MeanER))
            .Select(f => f.Probes[key]).ToList();
        if (vals.Count == 0) { Console.WriteLine($"  {key,-7}: no asset had a usable sample."); return; }
        double meanER = vals.Average(v => v.MeanER);
        double meanPos = vals.Average(v => v.PctPositive);
        int strong = vals.Count(v => v.MeanER > 0.20 && v.PctPositive >= 0.70);
        Console.WriteLine($"  {key,-7}: {vals.Count,2} assets usable, mean {meanER,+5:F2}R, mean {meanPos,4:P0} windows positive, {strong} asset(s) strong (>0.20R & ≥70%)");
    }

    private static double Pearson(double[] x, double[] y)
    {
        int n = x.Length;
        double mx = x.Average(), my = y.Average();
        double sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < n; i++)
        {
            sxy += (x[i] - mx) * (y[i] - my);
            sxx += (x[i] - mx) * (x[i] - mx);
            syy += (y[i] - my) * (y[i] - my);
        }
        return sxx > 0 && syy > 0 ? sxy / Math.Sqrt(sxx * syy) : double.NaN;
    }

    // ── Types ─────────────────────────────────────────────────────────────────

    private sealed record Probe(string Key, OrderSide Side, ConditionGroup Root);

    private sealed class ProbeResult
    {
        public double MeanER;
        public double PctPositive;
        public int ValidWindows;
        public double AvgTrades;
    }

    private sealed class Fingerprint
    {
        public string Symbol = "";
        public int Bars;
        public int NWindows;
        public double VolEarly, VolLate, VolMaturationRatio;
        public double AnnualDriftPct, BuyHoldMaxDdPct;
        public double MeanHurst, PctTrending, PctMeanReverting, PctBullRegime;
        public Dictionary<string, ProbeResult> Probes = new();
    }
}
