using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Tests the volume-profile version of the channel thesis, which is the one worth testing
/// because it fixes the flaw that killed the hand-drawn version.
///
/// <para>
/// A drawn channel's midline is chosen by the person drawing it, so any edge it shows is
/// inseparable from the choice of anchor — which is exactly why the origin-line families
/// collapsed out of sample. A volume profile's Point of Control is COMPUTED: it is the price
/// where the most volume actually traded over a defined window. Nobody picks it. Deviation from
/// POC, measured in value-area widths, is therefore an anchor-free way to ask the same question:
/// does being stretched away from where business is being done predict a move back toward it?
/// </para>
///
/// <para>
/// ROLLING AND CAUSAL. The profile is rebuilt from a trailing window ending at the current bar,
/// so the POC at bar i uses only bars i-Window..i. Building one profile over the whole series
/// and measuring deviation from it would be a spectacular lookahead — the POC would be computed
/// partly from the very bars whose returns are being predicted.
/// </para>
///
/// <para>
/// The statistic is the same as the favourability study: deciles for shape, plus a Spearman rank
/// correlation on NON-OVERLAPPING samples against a permutation null. A negative correlation
/// means stretched-above kept rising (momentum); a positive one means stretched-below bounced
/// (the rubber-band / mean-reversion thesis).
/// </para>
/// </summary>
public static class PocDeviationCommand
{
    private const int ForwardBars = 20;
    private const int Deciles = 10;
    private const int BinCount = 50;

    private sealed record Sample(double DeviationVa, double ForwardAtr, int BarIndex, string Asset);

    public static Task<int> RunAsync(string snapshotDir, string? only, string tf,
        int window, int permutations)
    {
        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => only == null || Path.GetFileName(f).Contains(only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f).ToList();

        var profiles = new ProfileService();
        var samples = new List<Sample>();

        foreach (var file in files)
        {
            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(file); } catch { continue; }
            var bars = snap.Bars;
            if (bars.Count < window + ForwardBars + 50) continue;

            var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars.ToArray(), 14);

            // Recomputing a 50-bin profile every bar over 5,000 bars is wasteful and the POC
            // barely moves bar to bar, so step the profile and hold it between rebuilds.
            const int RebuildEvery = 5;
            double poc = double.NaN, vaLow = double.NaN, vaHigh = double.NaN;

            for (int i = window; i < bars.Count - ForwardBars; i++)
            {
                if ((i - window) % RebuildEvery == 0)
                {
                    var slice = new List<Ohlcv>(window);
                    for (int k = i - window; k < i; k++) slice.Add(bars[k]);

                    var bins = profiles.CalculateVolumeProfile(slice, BinCount);
                    if (bins.Count == 0) { poc = double.NaN; continue; }

                    var pocBin = bins.FirstOrDefault(b => b.IsPOC);
                    poc = pocBin?.PriceMid ?? double.NaN;

                    var va = bins.Where(b => b.IsValueArea).ToList();
                    vaLow = va.Count > 0 ? va.Min(b => b.PriceLow) : double.NaN;
                    vaHigh = va.Count > 0 ? va.Max(b => b.PriceHigh) : double.NaN;
                }

                if (double.IsNaN(poc) || double.IsNaN(vaLow) || double.IsNaN(vaHigh)) continue;
                double vaWidth = vaHigh - vaLow;
                if (vaWidth <= 0) continue;

                double a = atr[i];
                if (double.IsNaN(a) || a <= 0) continue;

                // Deviation in value-area widths: 0 = at POC, +1 = one value area above it.
                double deviation = (bars[i].Close - poc) / vaWidth;
                double fwd = (bars[i + ForwardBars].Close - bars[i].Close) / a;
                samples.Add(new Sample(deviation, fwd, i, snap.Symbol));
            }
        }

        Report(samples, tf, window, permutations);
        return Task.FromResult(0);
    }

    private static void Report(List<Sample> samples, string tf, int window, int permutations)
    {
        Console.WriteLine();
        Console.WriteLine($"===== POC DEVIATION ({tf}, {window}-bar rolling profile) — {samples.Count:N0} bars =====");
        Console.WriteLine("Deviation measured in value-area widths from a CAUSAL rolling POC.");
        Console.WriteLine("Rubber-band thesis predicts POSITIVE correlation (stretched below → bounce).");
        Console.WriteLine();

        if (samples.Count < 500) { Console.WriteLine("Too few samples."); return; }

        var ordered = samples.OrderBy(s => s.DeviationVa).ToList();
        int per = ordered.Count / Deciles;

        Console.WriteLine($"  {"decile",7} {"deviation (VA)",18} {"n",7} {"mean fwd ATR",14} {"win%",7}");
        for (int d = 0; d < Deciles; d++)
        {
            var g = ordered.Skip(d * per).Take(d == Deciles - 1 ? int.MaxValue : per).ToList();
            if (g.Count == 0) continue;
            Console.WriteLine($"  {d + 1,7} {g.Min(x => x.DeviationVa),8:+0.00;-0.00} to {g.Max(x => x.DeviationVa),6:+0.00;-0.00}   {g.Count,7} " +
                              $"{g.Average(x => x.ForwardAtr),14:+0.0000;-0.0000;0} {g.Count(x => x.ForwardAtr > 0) / (double)g.Count,6:P0}");
        }

        var independent = samples.Where(s => s.BarIndex % ForwardBars == 0).ToList();
        var devs = independent.Select(x => x.DeviationVa).ToArray();
        var fwds = independent.Select(x => x.ForwardAtr).ToArray();
        double rho = Spearman(devs, fwds);

        var rng = new Random(31337);
        int extreme = 0;
        for (int p = 0; p < permutations; p++)
        {
            var shuffled = (double[])fwds.Clone();
            for (int k = shuffled.Length - 1; k > 0; k--)
            {
                int j = rng.Next(k + 1);
                (shuffled[k], shuffled[j]) = (shuffled[j], shuffled[k]);
            }
            if (Math.Abs(Spearman(devs, shuffled)) >= Math.Abs(rho)) extreme++;
        }
        double pValue = (extreme + 1.0) / (permutations + 1.0);

        Console.WriteLine();
        Console.WriteLine($"  Non-overlapping samples: {independent.Count:N0}");
        Console.WriteLine($"  Spearman(deviation, forward return) = {rho:+0.0000;-0.0000;0}, permutation p = {pValue:0.0000}");
        if (pValue > 0.05)
            Console.WriteLine("  → no reliable relationship: distance from POC does not predict the next move.");
        else if (rho < 0)
            Console.WriteLine("  → MEAN REVERSION: stretched above fell back, stretched below bounced. Rubber band holds.");
        else
            Console.WriteLine("  → MOMENTUM: stretched above kept rising. Rubber band runs BACKWARDS.");

        // The tails are what a scaling plan actually trades, so report them explicitly rather
        // than letting a whole-sample correlation hide what happens at the extremes.
        var far = ordered.Where(s => Math.Abs(s.DeviationVa) >= 1.0).ToList();
        var below = far.Where(s => s.DeviationVa <= -1.0).ToList();
        var above = far.Where(s => s.DeviationVa >= 1.0).ToList();
        Console.WriteLine();
        Console.WriteLine("  Tails (a full value area or more from POC):");
        if (below.Count > 30)
            Console.WriteLine($"    below POC: n={below.Count,6}  mean fwd {below.Average(x => x.ForwardAtr),8:+0.0000;-0.0000;0} ATR  win {below.Count(x => x.ForwardAtr > 0) / (double)below.Count:P0}");
        if (above.Count > 30)
            Console.WriteLine($"    above POC: n={above.Count,6}  mean fwd {above.Average(x => x.ForwardAtr),8:+0.0000;-0.0000;0} ATR  win {above.Count(x => x.ForwardAtr > 0) / (double)above.Count:P0}");
    }

    private static double Spearman(double[] a, double[] b)
    {
        if (a.Length < 3) return double.NaN;
        var ra = Rank(a); var rb = Rank(b);
        double ma = ra.Average(), mb = rb.Average();
        double num = 0, da = 0, db = 0;
        for (int i = 0; i < ra.Length; i++)
        {
            double x = ra[i] - ma, y = rb[i] - mb;
            num += x * y; da += x * x; db += y * y;
        }
        return da <= 0 || db <= 0 ? 0 : num / Math.Sqrt(da * db);
    }

    private static double[] Rank(double[] v)
    {
        var idx = Enumerable.Range(0, v.Length).OrderBy(i => v[i]).ToArray();
        var r = new double[v.Length];
        for (int k = 0; k < idx.Length; k++) r[idx[k]] = k;
        return r;
    }
}
