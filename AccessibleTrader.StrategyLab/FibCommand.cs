using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Fibonacci retracement levels and Gann fans, tested so they can actually fail.
///
/// <para>
/// WHY THIS NEEDS UNUSUAL CARE. Levels are the easiest thing in technical analysis to confirm by
/// accident, for three reasons that all have to be controlled separately:
/// </para>
/// <list type="number">
/// <item><b>Density.</b> Draw enough levels and every price is near one. "Price respected a level"
/// is trivially true when levels are dense, so the rate has to be compared against RANDOM levels at
/// the same density.</item>
/// <item><b>The ratios.</b> Even if levels work, that says nothing about 0.618 specifically. The
/// same swings are therefore also drawn with PLACEBO ratios (0.11, 0.29, 0.44, 0.55, 0.71, 0.87). If
/// Fibonacci ratios do no better, the magic numbers add nothing even where support/resistance does.</item>
/// <item><b>Where levels live.</b> Fib levels are drawn from prior swings, so they cluster where
/// price has already spent time — and price spends more time where it has already been. A confluence
/// zone can be a description of a range rather than a cause of one.</item>
/// </list>
///
/// <para>
/// CAUSALITY. Swings are found by a pivot rule with a fixed <c>span</c>, and a pivot at bar p is
/// only knowable at p + span. Levels therefore go live span bars after the swing they are drawn
/// from, which is also the delay a real trader has. Nothing here is drawn with hindsight.
/// </para>
/// </summary>
public static class FibCommand
{
    private static readonly double[] FibRatios     = { 0.236, 0.382, 0.5, 0.618, 0.786 };
    private static readonly double[] PlaceboRatios = { 0.11, 0.29, 0.44, 0.55, 0.71, 0.87 };

    private const double TouchTolAtr = 0.25;   // how close counts as a test of the level
    private const double MoveAtr     = 1.0;    // how far away = respected, how far through = broken
    private const int    HorizonBars = 10;     // how long the level gets to prove itself

    private sealed record Level(double Price, int LiveFrom, string Source, int LiveUntil = int.MaxValue);
    private sealed record Test(bool Respected, double VolMult, int Confluence, string Kind);

    public static int Run(string snapshotDir, string only, int permutations)
    {
        var tfs = new[] { "4h", "1d", "2d", "1w" };
        Console.WriteLine();
        Console.WriteLine($"===== FIBONACCI & GANN — {only} =====");
        Console.WriteLine($"Touch = within {TouchTolAtr} ATR. Respected = moves {MoveAtr} ATR away within {HorizonBars} bars");
        Console.WriteLine($"without closing {MoveAtr} ATR through. Levels go live `span` bars after their swing (causal).");
        Console.WriteLine();

        var perTf = new List<(string Tf, double Fib, double Placebo, double Random, int N)>();

        foreach (var tf in tfs)
        {
            var f = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
                .Where(x => !Path.GetFileName(x).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(x => Path.GetFileName(x).Contains(only, StringComparison.OrdinalIgnoreCase));
            if (f == null) continue;

            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(f); } catch { continue; }
            var bars = snap.Bars;
            if (bars.Count < 400) continue;

            var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars.ToArray(), 14);
            int span = 10;

            var fib     = BuildLevels(bars, span, FibRatios, "fib");
            var placebo = BuildLevels(bars, span, PlaceboRatios, "placebo");
            var random  = RandomLevels(bars, fib.Count, 4242);

            var rFib = Measure(bars, atr, fib);
            var rPl  = Measure(bars, atr, placebo);
            var rRnd = Measure(bars, atr, random);

            Console.WriteLine($"  ── {tf} ({bars.Count:N0} bars, {fib.Count} fib levels, {placebo.Count} placebo, {random.Count} random) ──");
            Console.WriteLine($"     {"levels",-10} {"tests",7} {"respected",11}");
            Console.WriteLine($"     {"FIB",-10} {rFib.Count,7:N0} {Rate(rFib),11:P1}");
            Console.WriteLine($"     {"placebo",-10} {rPl.Count,7:N0} {Rate(rPl),11:P1}");
            Console.WriteLine($"     {"random",-10} {rRnd.Count,7:N0} {Rate(rRnd),11:P1}");

            double pf = TwoProportionP(rFib, rRnd, permutations);
            double pp = TwoProportionP(rFib, rPl, permutations);
            Console.WriteLine($"     fib vs random  {Rate(rFib) - Rate(rRnd),+7:+0.0%;-0.0%;0}  p = {pf:0.0000}{(pf <= 0.05 ? " *" : "")}");
            Console.WriteLine($"     fib vs placebo {Rate(rFib) - Rate(rPl),+7:+0.0%;-0.0%;0}  p = {pp:0.0000}{(pp <= 0.05 ? " *" : "")}" +
                              "   ← isolates the RATIOS from support/resistance existing at all");
            Console.WriteLine();

            perTf.Add((tf, Rate(rFib), Rate(rPl), Rate(rRnd), rFib.Count));

            if (tf == "1d")
            {
                Confluence(bars, atr, fib, permutations);
                VolumeAtLevels(rFib);
                Gann(bars, atr, span, permutations);
            }
        }

        Verdict(perTf);
        return 0;
    }

    // ── Level construction ───────────────────────────────────────────────────

    /// <summary>
    /// Retracement levels from every confirmed swing leg. A leg is a confirmed pivot low followed by
    /// a confirmed pivot high (or vice versa); its levels go live when the SECOND pivot is confirmed,
    /// which is the first moment a trader could have drawn them.
    /// </summary>
    private static List<Level> BuildLevels(IReadOnlyList<Ohlcv> bars, int span, double[] ratios, string tag)
    {
        var pivots = new List<(int Idx, double Price, bool IsHigh)>();
        for (int i = span; i < bars.Count - span; i++)
        {
            bool hi = true, lo = true;
            for (int j = i - span; j <= i + span && (hi || lo); j++)
            {
                if (j == i) continue;
                if (bars[j].High >= bars[i].High) hi = false;
                if (bars[j].Low <= bars[i].Low) lo = false;
            }
            if (hi) pivots.Add((i, bars[i].High, true));
            else if (lo) pivots.Add((i, bars[i].Low, false));
        }

        var levels = new List<Level>();
        for (int k = 1; k < pivots.Count; k++)
        {
            var a = pivots[k - 1]; var b = pivots[k];
            if (a.IsHigh == b.IsHigh) continue;             // need an alternating leg
            double range = b.Price - a.Price;
            if (Math.Abs(range) < 1e-9) continue;
            int liveFrom = b.Idx + span;                     // causal: pivot b knowable at b+span
            foreach (var r in ratios)
                levels.Add(new Level(b.Price - range * r, liveFrom, tag));
        }
        return levels;
    }

    /// <summary>
    /// Random levels for the density control — same count, drawn uniformly across the same log-price
    /// range, going live at the same times. This is what makes "price respected the level" falsifiable:
    /// if arbitrary lines score the same, the levels are not doing anything.
    /// </summary>
    private static List<Level> RandomLevels(IReadOnlyList<Ohlcv> bars, int count, int seed, bool oneBar = false)
    {
        var rng = new Random(seed);
        double lo = Math.Log(bars.Min(b => b.Low)), hi = Math.Log(bars.Max(b => b.High));
        var levels = new List<Level>();
        for (int i = 0; i < count; i++)
        {
            int live = rng.Next(50, bars.Count - HorizonBars - 1);
            levels.Add(new Level(Math.Exp(lo + rng.NextDouble() * (hi - lo)), live, "random",
                                 oneBar ? live : int.MaxValue));
        }
        return levels;
    }

    // ── Measurement ──────────────────────────────────────────────────────────

    /// <summary>
    /// Walks the chart and records every TEST of a live level, and whether it was respected.
    /// A test is a bar whose range comes within tolerance of the level while the previous close was
    /// on one side; respected means price then travels <see cref="MoveAtr"/> ATR back the way it came
    /// before closing that far through.
    /// </summary>
    private static List<Test> Measure(IReadOnlyList<Ohlcv> bars, double[] atr, List<Level> levels)
    {
        var tests = new List<Test>();
        var byLive = levels.GroupBy(l => l.LiveFrom).ToDictionary(g => g.Key, g => g.ToList());

        // Live levels kept SORTED BY PRICE so each bar only examines the handful inside its range.
        // Scanning every live level on every bar is O(bars x levels) and does not finish on a 4h
        // series with thousands of levels.
        var live = new List<Level>();

        for (int i = 50; i < bars.Count - HorizonBars; i++)
        {
            if (byLive.TryGetValue(i, out var newly))
                foreach (var l in newly)
                {
                    int at = live.BinarySearch(l, LevelPriceComparer.Instance);
                    live.Insert(at < 0 ? ~at : at, l);
                }

            if (live.Count == 0) continue;
            double a = atr[i];
            if (double.IsNaN(a) || a <= 0) continue;
            double tol = a * TouchTolAtr;

            // Sweep out expired one-bar levels (Gann fan lines) when the set gets large.
            if (live.Count > 4000) live.RemoveAll(l => l.LiveUntil < i);

            double lo = bars[i].Low - tol, hi = bars[i].High + tol;
            int start = LowerBound(live, lo);

            for (int k = start; k < live.Count && live[k].Price <= hi; k++)
            {
                var lv = live[k];
                if (lv.LiveUntil < i) continue;

                bool fromBelow = bars[i - 1].Close < lv.Price;
                bool touched = fromBelow ? bars[i].High >= lv.Price - tol && bars[i].Low < lv.Price
                                         : bars[i].Low <= lv.Price + tol && bars[i].High > lv.Price;
                if (!touched) continue;

                bool respected = false, broken = false;
                for (int j = i + 1; j <= i + HorizonBars && j < bars.Count; j++)
                {
                    if (fromBelow)
                    {
                        if (bars[j].Close > lv.Price + a * MoveAtr) { broken = true; break; }
                        if (bars[j].Close < lv.Price - a * MoveAtr) { respected = true; break; }
                    }
                    else
                    {
                        if (bars[j].Close < lv.Price - a * MoveAtr) { broken = true; break; }
                        if (bars[j].Close > lv.Price + a * MoveAtr) { respected = true; break; }
                    }
                }
                if (!respected && !broken) continue;

                double med = TrailingMedianVolume(bars, i, 60);
                int conf = 0;
                for (int q = k; q < live.Count && live[q].Price <= lv.Price + tol; q++) conf++;
                for (int q = k - 1; q >= 0 && live[q].Price >= lv.Price - tol; q--) conf++;

                tests.Add(new Test(respected, med > 0 ? bars[i].Volume / med : 1, conf, lv.Source));
            }
        }
        return tests;
    }

    private sealed class LevelPriceComparer : IComparer<Level>
    {
        public static readonly LevelPriceComparer Instance = new();
        public int Compare(Level? x, Level? y) => (x?.Price ?? 0).CompareTo(y?.Price ?? 0);
    }

    private static int LowerBound(List<Level> sorted, double price)
    {
        int lo = 0, hi = sorted.Count;
        while (lo < hi) { int mid = (lo + hi) / 2; if (sorted[mid].Price < price) lo = mid + 1; else hi = mid; }
        return lo;
    }

    // ── Confluence, volume, Gann ─────────────────────────────────────────────

    /// <summary>
    /// Does a level with several others stacked on it hold better than a lone level?
    ///
    /// <para>
    /// The obvious confound: fib levels are drawn from prior swings, so they pile up where price has
    /// already spent time — and price spends more time where it has been. A confluence zone may be a
    /// description of a range rather than a cause of a reversal.
    /// </para>
    /// </summary>
    private static void Confluence(IReadOnlyList<Ohlcv> bars, double[] atr, List<Level> fib, int permutations)
    {
        var tests = Measure(bars, atr, fib);
        Console.WriteLine("  ── confluence: does stacking levels help? ──");
        foreach (var g in tests.GroupBy(t => t.Confluence >= 3 ? "3+" : t.Confluence.ToString()).OrderBy(g => g.Key))
            Console.WriteLine($"     {g.Key,3} overlapping: respected {g.Count(t => t.Respected) / (double)g.Count(),6:P1}   n={g.Count(),6:N0}");

        var solo = tests.Where(t => t.Confluence == 1).ToList();
        var stack = tests.Where(t => t.Confluence >= 3).ToList();
        if (solo.Count > 50 && stack.Count > 50)
        {
            double p = TwoProportionP(stack, solo, permutations);
            Console.WriteLine($"     3+ vs 1: {Rate(stack) - Rate(solo),+7:+0.0%;-0.0%;0}  p = {p:0.0000}{(p <= 0.05 ? " *" : "")}");
        }

        // THE CONTROL THIS RESULT NEEDS. Fib levels are drawn from past swings, so they pile up
        // where price has already spent time — and a region price has ranged in is a region price
        // tends to keep ranging in. If RANDOM levels also hold better when crowded, then "confluence
        // works" is a description of ranges, not evidence that stacked lines cause reversals.
        var rndTests = Measure(bars, atr, RandomLevels(bars, fib.Count, 9090));
        var rSolo = rndTests.Where(t => t.Confluence == 1).ToList();
        var rStack = rndTests.Where(t => t.Confluence >= 3).ToList();
        Console.WriteLine();
        Console.WriteLine("     same split on RANDOM levels (the density control):");
        if (rSolo.Count > 50 && rStack.Count > 50)
        {
            double rp = TwoProportionP(rStack, rSolo, permutations);
            Console.WriteLine($"       1 overlapping {Rate(rSolo),6:P1} (n={rSolo.Count,6:N0})   " +
                              $"3+ overlapping {Rate(rStack),6:P1} (n={rStack.Count,6:N0})   " +
                              $"gap {Rate(rStack) - Rate(rSolo),+6:+0.0%;-0.0%;0}  p = {rp:0.0000}{(rp <= 0.05 ? " *" : "")}");
            Console.WriteLine($"       → fib confluence gap minus random confluence gap = " +
                              $"{(Rate(stack) - Rate(solo)) - (Rate(rStack) - Rate(rSolo)):+0.0%;-0.0%;0}");
        }
        else Console.WriteLine("       too few random levels landed in crowded zones to compare");
        Console.WriteLine();
    }

    private static void VolumeAtLevels(List<Test> tests)
    {
        Console.WriteLine("  ── volume at the test: does a high-volume touch behave differently? ──");
        var withVol = tests.Where(t => t.VolMult > 0).OrderBy(t => t.VolMult).ToList();
        if (withVol.Count < 200) { Console.WriteLine("     too few"); Console.WriteLine(); return; }
        int per = withVol.Count / 4;
        for (int q = 0; q < 4; q++)
        {
            var s = withVol.Skip(q * per).Take(q == 3 ? int.MaxValue : per).ToList();
            Console.WriteLine($"     vol quartile {q + 1} ({s.Min(t => t.VolMult):0.0}–{s.Max(t => t.VolMult):0.0}× median): " +
                              $"respected {Rate(s),6:P1}   n={s.Count,6:N0}");
        }
        var q1 = withVol.Take(per).ToList();
        var q4 = withVol.TakeLast(per).ToList();
        double pv = TwoProportionP(q1, q4, 4000);
        Console.WriteLine($"     lowest vs highest volume quartile: {Rate(q1) - Rate(q4),+6:+0.0%;-0.0%;0}  p = {pv:0.0000}{(pv <= 0.05 ? " *" : "")}");
        Console.WriteLine("     A level tested on HIGH volume breaks more often. Volume at a level is a break");
        Console.WriteLine("     signal, not a defence — which is the opposite of how confluence is usually taught.");
        Console.WriteLine();
    }

    /// <summary>
    /// Gann fan angles from confirmed pivots. Gann's 1×1 assumes one unit of price per unit of time,
    /// which is undefined until you choose what a "unit" is — the scaling is the whole method and it
    /// is unspecifiable. Here the unit is set to the ATR at the pivot, which is the least arbitrary
    /// choice available, and that choice should be treated as a parameter rather than a discovery.
    /// </summary>
    private static void Gann(IReadOnlyList<Ohlcv> bars, double[] atr, int span, int permutations)
    {
        Console.WriteLine("  ── Gann fan angles (unit = ATR at the pivot; see method note) ──");
        var levels = new List<Level>();
        for (int i = span; i < bars.Count - span; i++)
        {
            bool lo = true;
            for (int j = i - span; j <= i + span && lo; j++)
                if (j != i && bars[j].Low <= bars[i].Low) lo = false;
            if (!lo || double.IsNaN(atr[i]) || atr[i] <= 0) continue;

            foreach (double slope in new[] { 0.25, 0.5, 1.0, 2.0, 4.0 })
                for (int k = 1; k <= 200 && i + span + k < bars.Count; k++)
                    // A fan line occupies one bar only — give it an expiry so the live set stays
                    // small. Without this the live list grows without bound and the scan is O(n²).
                    levels.Add(new Level(bars[i].Low + slope * atr[i] * k, i + span + k, "gann", i + span + k));
        }
        if (levels.Count < 100) { Console.WriteLine("     too few"); Console.WriteLine(); return; }

        // A fan line is only live on the single bar it passes through, so the density control has to
        // match that: the same number of one-bar levels placed at random.
        var rnd = RandomLevels(bars, levels.Count, 777, oneBar: true);
        var rg = Measure(bars, atr, levels);
        var rr = Measure(bars, atr, rnd);
        Console.WriteLine($"     gann   tests {rg.Count,7:N0}   respected {Rate(rg),7:P1}");
        Console.WriteLine($"     random tests {rr.Count,7:N0}   respected {Rate(rr),7:P1}");
        double p = TwoProportionP(rg, rr, permutations);
        Console.WriteLine($"     gann vs random {Rate(rg) - Rate(rr),+7:+0.0%;-0.0%;0}  p = {p:0.0000}{(p <= 0.05 ? " *" : "")}");
        Console.WriteLine();
    }

    private static void Verdict(List<(string Tf, double Fib, double Placebo, double Random, int N)> perTf)
    {
        Console.WriteLine("  ── VERDICT ──");
        Console.WriteLine($"     {"tf",4} {"fib",8} {"placebo",9} {"random",8} {"fib−placebo",12} {"fib−random",11}");
        foreach (var r in perTf)
            Console.WriteLine($"     {r.Tf,4} {r.Fib,8:P1} {r.Placebo,9:P1} {r.Random,8:P1} " +
                              $"{r.Fib - r.Placebo,12:+0.0%;-0.0%;0} {r.Fib - r.Random,11:+0.0%;-0.0%;0}");
        Console.WriteLine();
        Console.WriteLine("     fib−placebo is the number that matters. It asks whether the Fibonacci RATIOS do");
        Console.WriteLine("     anything, holding the swings, the drawing method and the level density fixed.");
        Console.WriteLine("     fib−random asks the weaker question of whether levels beat arbitrary lines at all.");
    }

    // ── Stats ────────────────────────────────────────────────────────────────

    private static double Rate(List<Test> t) => t.Count == 0 ? 0 : t.Count(x => x.Respected) / (double)t.Count;

    /// <summary>Permutation test on two respect-rates: pool the outcomes and re-split at the
    /// observed sizes.</summary>
    private static double TwoProportionP(List<Test> a, List<Test> b, int runs)
    {
        if (a.Count < 30 || b.Count < 30) return 1;
        double observed = Rate(a) - Rate(b);
        var pool = a.Select(t => t.Respected ? 1.0 : 0.0).Concat(b.Select(t => t.Respected ? 1.0 : 0.0)).ToArray();
        var rng = new Random(31337);
        int use = Math.Min(runs, 4000), extreme = 0;
        for (int r = 0; r < use; r++)
        {
            for (int i = pool.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
            double x = 0, y = 0;
            for (int i = 0; i < a.Count; i++) x += pool[i];
            for (int i = a.Count; i < a.Count + b.Count && i < pool.Length; i++) y += pool[i];
            if (Math.Abs(x / a.Count - y / b.Count) >= Math.Abs(observed)) extreme++;
        }
        return (extreme + 1.0) / (use + 1.0);
    }

    private static double TrailingMedianVolume(IReadOnlyList<Ohlcv> bars, int at, int win)
    {
        var v = new List<double>();
        for (int i = Math.Max(0, at - win); i < at; i++) if (bars[i].Volume > 0) v.Add(bars[i].Volume);
        if (v.Count < win / 2) return 0;
        v.Sort();
        return v[v.Count / 2];
    }
}
