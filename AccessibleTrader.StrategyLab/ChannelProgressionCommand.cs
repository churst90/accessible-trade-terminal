using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Tests the channel-progression rule: "if one line breaks, price is destined for the next one."
///
/// <para>
/// This is a DIFFERENT claim from the origin-line geometry, and it survives that geometry being
/// wrong. It says nothing about where the lines are — only that once price clears one level of an
/// equally-spaced ladder, it tends to reach the next level before falling back to the previous one.
/// That is a pure statement about continuation versus mean reversion, and it can be tested on an
/// ARBITRARILY anchored grid.
/// </para>
///
/// <para>
/// THE BASELINE IS NOT 50%. Having just crossed a line, price sits slightly beyond it and is
/// already marginally closer to the next line than the previous one, so a driftless random walk
/// continues more than half the time for purely mechanical reasons. Comparing against 50% would
/// "prove" the rule on random data. Every result here is measured against block-bootstrap
/// surrogates of the same series, which inherit that mechanical bias along with the drift and the
/// volatility clustering.
/// </para>
/// </summary>
public static class ChannelProgressionCommand
{
    /// <summary>Grid spacings tested, as fractions of a price doubling in log10 space.</summary>
    private static readonly double[] SpacingDoublings = { 0.05, 0.10, 0.20, 0.35, 0.50 };

    public static Task<int> RunAsync(string snapshotDir, string? only, string tf, int surrogates)
    {
        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => only == null || Path.GetFileName(f).Contains(only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f).ToList();

        if (files.Count == 0) { Console.Error.WriteLine($"No {tf} snapshots."); return Task.FromResult(1); }

        Console.WriteLine($"===== CHANNEL PROGRESSION ({tf}) =====");
        Console.WriteLine("After price closes through a grid line, does it reach the NEXT line before");
        Console.WriteLine("returning to the previous one? Compared against block-bootstrap surrogates,");
        Console.WriteLine("which carry the same mechanical continuation bias.");
        Console.WriteLine();
        Console.WriteLine($"  {"asset",-14} {"spacing",8} {"events",7} {"real%",6} {"surr%",6} {"edge",7} {"p",6}");

        var all = new List<(string Asset, double Spacing, int N, double Real, double Surr, double P)>();

        foreach (var file in files)
        {
            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(file); } catch { continue; }
            var bars = snap.Bars;
            if (bars.Count < 500) continue;

            foreach (double doublings in SpacingDoublings)
            {
                double spacing = Math.Log10(2) * doublings;
                var real = Continuation(bars, spacing);
                if (real.N < 30) continue;

                var rng = new Random(StableSeed.From(snap.Symbol + doublings) % 100000);
                var surrRates = new List<double>();
                int beaten = 0;
                for (int s = 0; s < surrogates; s++)
                {
                    var sb = SurrogateTest.BlockBootstrap(bars, rng);
                    var sr = Continuation(sb, spacing);
                    if (sr.N < 20) continue;
                    surrRates.Add(sr.Rate);
                    if (sr.Rate >= real.Rate) beaten++;
                }
                if (surrRates.Count == 0) continue;

                double mean = surrRates.Average();
                double p = (beaten + 1.0) / (surrRates.Count + 1.0);
                all.Add((snap.Symbol, doublings, real.N, real.Rate, mean, p));

                Console.WriteLine($"  {snap.Symbol,-14} {doublings,8:0.00} {real.N,7} {real.Rate * 100,5:0.0}% " +
                                  $"{mean * 100,5:0.0}% {(real.Rate - mean) * 100,6:+0.0;-0.0;0}pp {p,6:0.000}");
            }
        }

        if (all.Count > 0)
        {
            int sig = all.Count(a => a.P <= 0.05);
            int pos = all.Count(a => a.Real > a.Surr);
            Console.WriteLine();
            Console.WriteLine($"  VERDICT: {pos}/{all.Count} cells had a positive edge; {sig}/{all.Count} significant");
            Console.WriteLine($"  at p<=0.05 (expected ~{all.Count * 0.05:0.0}). Mean real {all.Average(a => a.Real) * 100:0.0}% " +
                              $"vs surrogate {all.Average(a => a.Surr) * 100:0.0}%, mean edge {all.Average(a => a.Real - a.Surr) * 100:+0.0;-0.0;0} pp.");
            Console.WriteLine();
            Console.WriteLine("  By spacing:");
            foreach (var g in all.GroupBy(a => a.Spacing).OrderBy(g => g.Key))
                Console.WriteLine($"    {g.Key:0.00} doublings: real {g.Average(a => a.Real) * 100:0.0}% vs surr " +
                                  $"{g.Average(a => a.Surr) * 100:0.0}%, edge {g.Average(a => a.Real - a.Surr) * 100:+0.0;-0.0;0}pp, " +
                                  $"sig {g.Count(a => a.P <= 0.05)}/{g.Count()}");
        }

        return Task.FromResult(0);
    }

    /// <summary>
    /// Walks the series over a log-spaced grid. Each time a close moves into a new cell, the
    /// subsequent path decides the outcome: reaching the NEXT line in the direction of travel is
    /// continuation; falling back a full cell the other way is reversion. Unresolved runs at the
    /// end of the data are discarded rather than guessed.
    /// </summary>
    private static (int N, double Rate) Continuation(IReadOnlyList<Ohlcv> bars, double spacing)
    {
        if (spacing <= 0 || bars.Count < 2) return (0, double.NaN);

        int cont = 0, rev = 0;
        double anchor = Math.Log10(Math.Max(bars[0].Close, 1e-9));

        int Cell(double price) =>
            (int)Math.Floor((Math.Log10(Math.Max(price, 1e-9)) - anchor) / spacing);

        int prevCell = Cell(bars[0].Close);

        for (int i = 1; i < bars.Count; i++)
        {
            int cell = Cell(bars[i].Close);
            if (cell == prevCell) continue;

            int dir = Math.Sign(cell - prevCell);

            // The race: the next line in the direction of travel, versus one full cell back the
            // other way. Using the just-crossed line as the "back" boundary would make a single
            // tick of noise count as reversion.
            double nextLine = anchor + (dir > 0 ? cell + 1 : cell) * spacing;
            double backLine = anchor + (dir > 0 ? cell - 1 : cell + 2) * spacing;

            bool resolved = false;
            for (int j = i + 1; j < bars.Count; j++)
            {
                double hi = Math.Log10(Math.Max(bars[j].High, 1e-9));
                double lo = Math.Log10(Math.Max(bars[j].Low, 1e-9));

                bool hitNext = dir > 0 ? hi >= nextLine : lo <= nextLine;
                bool hitBack = dir > 0 ? lo <= backLine : hi >= backLine;

                // A bar that spans both is ambiguous; count it against the rule rather than for it.
                if (hitNext && hitBack) { resolved = true; rev++; break; }
                if (hitNext) { resolved = true; cont++; break; }
                if (hitBack) { resolved = true; rev++; break; }
            }
            if (!resolved) break;

            prevCell = cell;
        }

        int n = cont + rev;
        return (n, n > 0 ? (double)cont / n : double.NaN);
    }
}
