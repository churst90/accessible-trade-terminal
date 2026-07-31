using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Anchored walk-forward on the BTC trend rule — the test that decides whether the parameters
/// belong to the market or to me.
///
/// <para>
/// WHY THE EXISTING NUMBER DOES NOT COUNT. The 50/+1/+0.5 settings in <c>BTC_STRATEGY.md</c> were
/// chosen by sweeping the WHOLE history and picking what worked. Reporting their full-sample return
/// is circular: the sample selected the parameters and then graded them. Even testing those exact
/// settings on a holdout is contaminated, because I picked them having already seen the holdout.
/// </para>
///
/// <para>
/// THE ONLY HONEST FORM. Let each fit window choose its own parameters from a grid, then apply that
/// choice, unchanged, to the block of time immediately after it — data the search never saw. Chain
/// those out-of-sample blocks into one equity curve. Nothing about the future touches the choice.
/// </para>
///
/// <para>
/// THREE SELECTION RULES ARE COMPARED, because how you pick from the grid is itself a hypothesis:
/// best-by-return (the naive thing most people do), the centre of the best plateau (what Varma
/// recommends — "you're not looking for the highest rate of return, you're looking for the most
/// stable"), and a fixed setting held constant throughout. If naive picking collapses out of sample
/// while plateau-picking survives, that difference is the actual finding.
/// </para>
/// </summary>
public static class WalkForwardCommand
{
    private static readonly int[] Windows = { 20, 30, 50, 80, 120, 200, 300 };
    private static readonly double[] Entries = { 0.5, 0.75, 1.0, 1.25, 1.5 };
    private static readonly double[] Exits = { 0.0, 0.25, 0.5, 0.75, 1.0 };

    private sealed record Params(int W, double In, double Out)
    {
        public override string ToString() => $"{W}/{In:0.##}/{Out:0.##}";
    }

    public static int Run(string snapshotDir, string only, string tf, int folds, int permutations)
    {
        var f = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(x => !Path.GetFileName(x).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(x => Path.GetFileName(x).Contains(only, StringComparison.OrdinalIgnoreCase));
        if (f == null) { Console.WriteLine($"No snapshot for {only} {tf}"); return 1; }

        var bars = SnapshotCommand.Load(f).Bars;
        if (bars.Count < 1200) { Console.WriteLine("Too few bars for a walk-forward."); return 1; }

        Console.WriteLine();
        Console.WriteLine($"===== ANCHORED WALK-FORWARD — {only} {tf} =====");
        Console.WriteLine($"{bars.Count:N0} bars, {bars[0].Date:yyyy-MM} → {bars[^1].Date:yyyy-MM}. " +
                          $"Grid = {Windows.Length}×{Entries.Length}×{Exits.Length} = {Windows.Length * Entries.Length * Exits.Length} combos.");
        Console.WriteLine("Each fit window picks its own parameters; they are then applied unchanged to the");
        Console.WriteLine("block that follows. The search never sees the block it is graded on.");
        Console.WriteLine();

        // Anchored: fit always starts at bar 0 and grows; each OOS block follows its fit window.
        int firstFit = (int)(bars.Count * 0.40);
        int step = (bars.Count - firstFit) / folds;
        if (step < 150) { Console.WriteLine("Folds too small — reduce --folds."); return 1; }

        var modes = new[] { "best", "plateau", "fixed", "random" };
        var curves = new Dictionary<string, List<double>>();
        var chosen = new Dictionary<string, List<Params>>();
        foreach (var m in modes) { curves[m] = new List<double>(); chosen[m] = new List<Params>(); }
        var holdReturns = new List<double>();

        Console.WriteLine($"  {"fold",4} {"fit ends",10} {"oos window",22} {"best pick",14} {"plateau pick",14} {"oos: best",10} {"plateau",9} {"fixed",8} {"hold",8} {"randparam",9}");

        for (int k = 0; k < folds; k++)
        {
            int fitEnd = firstFit + k * step;
            int oosEnd = Math.Min(fitEnd + step, bars.Count - 1);
            if (oosEnd - fitEnd < 100) break;

            var scores = new Dictionary<Params, double>();
            foreach (int w in Windows)
                foreach (double inZ in Entries)
                    foreach (double outZ in Exits)
                    {
                        if (outZ >= inZ) continue;      // an exit at or above the entry never holds
                        var p = new Params(w, inZ, outZ);
                        scores[p] = LogReturn(bars, p, w + 2, fitEnd);
                    }
            if (scores.Count == 0) break;

            var best = scores.OrderByDescending(kv => kv.Value).First().Key;
            var plateau = PlateauPick(scores);
            var fixedP = new Params(50, 1.0, 0.5);

            // THE CONTROL THAT SETTLES WHETHER FITTING ADDS ANYTHING. Pick parameters at random
            // from the same grid, ignoring the fit window entirely. If a coin does as well as the
            // search, the search is not extracting information — the out-of-sample return is just
            // "trend-following in crypto works a bit", and the optimiser is decoration.
            var rng = new Random(4200 + k);
            var valid = scores.Keys.ToList();
            var randP = valid[rng.Next(valid.Count)];

            double oosBest = LogReturn(bars, best, fitEnd, oosEnd, warmFrom: fitEnd - best.W - 2);
            double oosPlat = LogReturn(bars, plateau, fitEnd, oosEnd, warmFrom: fitEnd - plateau.W - 2);
            double oosFix = LogReturn(bars, fixedP, fitEnd, oosEnd, warmFrom: fitEnd - fixedP.W - 2);
            double oosRand = 0;
            for (int r = 0; r < 200; r++)
            {
                var rp = valid[rng.Next(valid.Count)];
                oosRand += LogReturn(bars, rp, fitEnd, oosEnd, warmFrom: fitEnd - rp.W - 2);
            }
            oosRand /= 200;                       // average random pick, not one lucky draw
            double oosHold = Math.Log(bars[oosEnd].Close / bars[fitEnd].Close);

            curves["best"].Add(oosBest); curves["plateau"].Add(oosPlat);
            curves["fixed"].Add(oosFix); curves["random"].Add(oosRand);
            chosen["best"].Add(best); chosen["plateau"].Add(plateau);
            chosen["fixed"].Add(fixedP); chosen["random"].Add(randP);
            holdReturns.Add(oosHold);

            Console.WriteLine($"  {k + 1,4} {bars[fitEnd].Date,10:yyyy-MM} " +
                              $"{bars[fitEnd].Date:yyyy-MM} → {bars[oosEnd].Date:yyyy-MM}      " +
                              $"{best,14} {plateau,14} " +
                              $"{Math.Exp(oosBest),9:0.00}× {Math.Exp(oosPlat),8:0.00}× {Math.Exp(oosFix),7:0.00}× {Math.Exp(oosHold),7:0.00}×" +
                              $" {Math.Exp(oosRand),8:0.00}×");
        }

        Console.WriteLine();
        Summary(curves, chosen, holdReturns);
        Stability(chosen);
        return 0;
    }

    /// <summary>
    /// Compounded out-of-sample result per selection rule, against buy-and-hold over exactly the same
    /// blocks. This is the only number in the lab that was never fitted on the data it is scored on.
    /// </summary>
    private static void Summary(Dictionary<string, List<double>> curves,
        Dictionary<string, List<Params>> chosen, List<double> hold)
    {
        Console.WriteLine("  ── compounded out-of-sample ──");
        double h = Math.Exp(hold.Sum());
        foreach (var (name, c) in curves)
        {
            double total = Math.Exp(c.Sum());
            int beat = c.Where((v, i) => v > hold[i]).Count();
            Console.WriteLine($"     {name,-9} {total,9:0.00}×   beat hold in {beat}/{c.Count} folds   " +
                              $"vs hold {total / h,6:0.00}×");
        }
        Console.WriteLine($"     {"buy&hold",-9} {h,9:0.00}×");
        Console.WriteLine();
        Console.WriteLine("     'fixed' is 50/1/0.5 — the setting chosen by sweeping the WHOLE history, which");
        Console.WriteLine("     includes every out-of-sample block above. It is NOT an out-of-sample result and");
        Console.WriteLine("     must not be read as one; it is the in-sample number wearing a walk-forward costume.");
        Console.WriteLine("     'random' is the average of 200 random grid picks per fold — what the optimiser has");
        Console.WriteLine("     to beat before the fitting can be said to do anything.");
        Console.WriteLine();
    }

    /// <summary>
    /// Do the chosen parameters stay put? A selection rule that jumps around the grid between folds
    /// is fitting noise even when its out-of-sample number happens to come out positive.
    /// </summary>
    private static void Stability(Dictionary<string, List<Params>> chosen)
    {
        Console.WriteLine("  ── parameter stability across folds ──");
        foreach (var (name, ps) in chosen)
        {
            if (name == "fixed") continue;
            var ws = ps.Select(p => (double)p.W).ToList();
            var ins = ps.Select(p => p.In).ToList();
            Console.WriteLine($"     {name,-9} windows [{string.Join(", ", ps.Select(p => p.W))}]   " +
                              $"entries [{string.Join(", ", ps.Select(p => p.In.ToString("0.##")))}]   " +
                              $"distinct picks {ps.Distinct().Count()}/{ps.Count}");
        }
        Console.WriteLine("     Jumping between folds means the grid search is reading noise, whatever the");
        Console.WriteLine("     out-of-sample total says.");
        Console.WriteLine();
    }

    /// <summary>
    /// The centre of the best-performing neighbourhood rather than the single best cell. Each cell is
    /// scored as the mean of itself and its immediate grid neighbours, so an isolated spike loses to
    /// a broad plateau — Varma's "you're looking for the most stable rate of return, not the highest".
    /// </summary>
    private static Params PlateauPick(Dictionary<Params, double> scores)
    {
        Params best = scores.Keys.First();
        double bestScore = double.MinValue;

        foreach (var (p, _) in scores)
        {
            var neigh = new List<double>();
            foreach (int w in Windows.Where(w => Math.Abs(Array.IndexOf(Windows, w) - Array.IndexOf(Windows, p.W)) <= 1))
                foreach (double i in Entries.Where(e => Math.Abs(Array.IndexOf(Entries, e) - Array.IndexOf(Entries, p.In)) <= 1))
                    foreach (double o in Exits.Where(x => Math.Abs(Array.IndexOf(Exits, x) - Array.IndexOf(Exits, p.Out)) <= 1))
                        if (scores.TryGetValue(new Params(w, i, o), out double v)) neigh.Add(v);

            if (neigh.Count < 4) continue;             // demand a real neighbourhood, not an edge cell
            double s = neigh.Average();
            if (s > bestScore) { bestScore = s; best = p; }
        }
        return best;
    }

    /// <summary>
    /// Log return of the long/flat rule over [from, to). The state machine is warmed from
    /// <paramref name="warmFrom"/> so it enters the window with the position it would really have
    /// held, but equity only accrues from <paramref name="from"/> — warming uses only prior data.
    /// </summary>
    private static double LogReturn(IReadOnlyList<Ohlcv> bars, Params p, int from, int to, int? warmFrom = null)
    {
        var z = TradingCrossCommand.ZScore(bars, p.W);
        int start = Math.Max(p.W + 2, warmFrom ?? from);
        double acc = 0;
        bool inMkt = false;

        for (int i = start; i < to && i < bars.Count - 1; i++)
        {
            if (!double.IsNaN(z[i]) && !double.IsNaN(z[i - 1]))
            {
                if (!inMkt && z[i - 1] <= p.In && z[i] > p.In) inMkt = true;
                else if (inMkt && z[i - 1] >= p.Out && z[i] < p.Out) inMkt = false;
            }
            if (i >= from && inMkt && bars[i].Close > 0 && bars[i + 1].Close > 0)
                acc += Math.Log(bars[i + 1].Close / bars[i].Close);
        }
        return acc;
    }
}
