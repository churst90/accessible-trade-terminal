using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Tests Samir Varma's two specific claims about volume — the one input he calls "the key" and the
/// reason he says institutions moved to dark pools ("they don't want you to know what the volume is").
///
/// <para>
/// CLAIM 1 — CONFIRMATION. "When this stock goes up, does volume increase as it goes up and
/// decrease as it goes down? That suggests there's some upside left to the stock." Operationalised
/// as the correlation between daily returns and volume over a trailing window: positive means
/// volume arrives on up-days, negative means it arrives on down-days.
/// </para>
///
/// <para>
/// CLAIM 2 — CAPITULATION. "The stock is falling and then all of a sudden there's like a 20 times
/// volume day. Okay, that sounds like somebody puked it out. It may be time to buy it."
/// Operationalised as a volume multiple of the trailing median, during an established decline.
/// </para>
///
/// <para>
/// WHY VOLUME AND NOT ANOTHER OSCILLATOR. Everything this lab has tested and rejected as a
/// conditioner was a transform of price. Volume is a second observable — how much changed hands,
/// not what it changed hands at. It is the last input in the candidate set that is not price
/// wearing a different hat, which makes it the one worth the effort even at a low prior.
/// </para>
///
/// <para>
/// THE CONTROLS. Every reading is measured against RANDOM ENTRIES on the same bars, because a
/// filter that only says "the market went up afterwards" will lift any long. And the capitulation
/// test additionally reports the decline-only baseline: buying any dip and buying a dip with a
/// volume spike are different claims, and only the difference belongs to volume.
/// </para>
///
/// <para>
/// SCOPE: crypto exchange volume is self-reported and wash-trading is common; Bitstamp is a real
/// order book but the numbers are still one venue's. Equity volume from Yahoo is consolidated tape
/// and far more trustworthy. Results are reported per class for exactly that reason.
/// </para>
/// </summary>
public static class VolumeCommand
{
    private const int HorizonBars = 20;
    private const int TrailWindow = 60;

    private sealed record Obs(string Symbol, string Class, double VolCorr, double VolMult,
        double Trend, double TrendLong, double FwdRet, double FwdAtr);

    public static int Run(string snapshotDir, string tf, int permutations)
    {
        var obs = new List<Obs>();
        var covered = new List<string>();

        foreach (var file in Directory.GetFiles(snapshotDir, $"*_{tf}.json")
                     .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f))
        {
            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(file); } catch { continue; }
            var bars = snap.Bars;
            if (bars.Count < 400) continue;

            string cls = LabSnapshots.AssetClass(Path.GetFileName(file));
            if (cls == "skip") continue;

            // A series with no volume feed reports zeros. Counting those as "low volume" would put
            // a whole symbol in one bucket and quietly bias every pooled number.
            int withVol = bars.Count(b => b.Volume > 0);
            if (withVol < bars.Count * 0.9) { Console.WriteLine($"  ! {snap.Symbol}: only {withVol}/{bars.Count} bars have volume — skipped"); continue; }

            var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars.ToArray(), 14);

            for (int i = TrailWindow; i < bars.Count - HorizonBars; i++)
            {
                if (double.IsNaN(atr[i]) || atr[i] <= 0 || bars[i].Close <= 0) continue;

                // Return/volume correlation over the trailing window — claim 1.
                double corr = ReturnVolumeCorrelation(bars, i, TrailWindow);
                if (double.IsNaN(corr)) continue;

                // Volume as a multiple of the trailing MEDIAN, not mean: a single 20x day would
                // drag a mean up and make itself look ordinary.
                double med = TrailingMedianVolume(bars, i, TrailWindow);
                if (med <= 0) continue;
                double mult = bars[i].Volume / med;

                // Trailing trend, so "during a decline" is defined rather than assumed.
                double trend = Math.Log(bars[i].Close / bars[i - 20].Close);

                // Trailing return over the SAME window the correlation is measured on — needed to
                // ask whether the volume signal is anything more than "price has been going up".
                double trendLong = Math.Log(bars[i].Close / bars[i - TrailWindow].Close);

                obs.Add(new Obs(snap.Symbol, cls, corr, mult, trend, trendLong,
                    Math.Log(bars[i + HorizonBars].Close / bars[i].Close),
                    (bars[i + HorizonBars].Close - bars[i].Close) / atr[i]));
            }

            covered.Add(snap.Symbol);
        }

        if (obs.Count < 2000) { Console.WriteLine($"Too few observations ({obs.Count})."); return 1; }

        Console.WriteLine();
        Console.WriteLine($"===== VOLUME — {obs.Count:N0} observations over {covered.Count} symbols =====");
        Console.WriteLine($"Forward horizon {HorizonBars} bars. Trailing window {TrailWindow} bars.");
        Console.WriteLine();

        foreach (var g in obs.GroupBy(o => o.Class).OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"  ══════ {g.Key.ToUpperInvariant()} ({g.Select(o => o.Symbol).Distinct().Count()} symbols, {g.Count():N0} obs) ══════");
            Claim1(g.ToList(), permutations);
            IsClaim1JustMomentum(g.ToList(), permutations);
            Claim2(g.ToList(), permutations);
        }

        Verdict(obs, permutations);
        return 0;
    }

    /// <summary>
    /// CLAIM 1: volume arriving on up-days implies upside left. Bucketed by the trailing
    /// return/volume correlation, with the pooled mean as the random-entry equivalent — every bar
    /// is in exactly one bucket, so the all-bars average IS what a coin flip earns here.
    /// </summary>
    private static void Claim1(List<Obs> set, int permutations)
    {
        Console.WriteLine("    ── claim 1: volume rising into up-moves ⇒ upside left ──");
        var sorted = set.OrderBy(o => o.VolCorr).ToList();
        int per = sorted.Count / 5;
        double baseline = set.Average(o => o.FwdAtr);

        for (int q = 0; q < 5; q++)
        {
            var slice = sorted.Skip(q * per).Take(q == 4 ? int.MaxValue : per).ToList();
            Console.WriteLine($"      quintile {q + 1} (corr {slice.Min(o => o.VolCorr),+5:+0.00;-0.00;0}…{slice.Max(o => o.VolCorr),+5:+0.00;-0.00;0}): " +
                              $"fwd {slice.Average(o => o.FwdAtr),+6:+0.00;-0.00;0} ATR   n={slice.Count,6:N0}");
        }

        var top = sorted.TakeLast(per).ToList();
        var bot = sorted.Take(per).ToList();
        double gap = top.Average(o => o.FwdAtr) - bot.Average(o => o.FwdAtr);
        double p = PermutationP(set.Select(o => o.FwdAtr).ToArray(), top.Count, bot.Count, gap, permutations);
        Console.WriteLine($"      top − bottom quintile: {gap,+6:+0.00;-0.00;0} ATR   p = {p:0.0000}" +
                          (p <= 0.05 ? "  *" : "") + $"   (all-bars baseline {baseline:+0.00;-0.00;0} ATR)");
        Console.WriteLine();
    }

    /// <summary>
    /// Is claim 1 anything more than time-series momentum?
    ///
    /// <para>
    /// Volume arriving on up-days is close to a description of an uptrend: in a rally, the up-days
    /// are the high-volume days, so the return/volume correlation rises WITH the trailing return.
    /// If that is all it is, then "volume confirms the move" is this lab's existing momentum result
    /// arriving under a new name — exactly the failure mode found in the crowding index, whose
    /// docstring claimed orthogonality to price while rank-correlating 0.19 with trailing returns.
    /// </para>
    ///
    /// <para>
    /// The test splits the sample into trailing-return terciles and re-measures the volume gap
    /// INSIDE each one. Holding the trend roughly fixed, does volume still separate? If the gap
    /// survives in all three, volume carries its own information. If it collapses, it does not.
    /// </para>
    /// </summary>
    private static void IsClaim1JustMomentum(List<Obs> set, int permutations)
    {
        Console.WriteLine("    ── is claim 1 just momentum wearing a volume hat? ──");

        double rho = Correlation(set.Select(o => o.VolCorr).ToArray(), set.Select(o => o.TrendLong).ToArray());
        Console.WriteLine($"      corr(volume-signal, trailing {TrailWindow}-bar return) = {rho:+0.000;-0.000;0}");

        var byTrend = set.OrderBy(o => o.TrendLong).ToList();
        int third = byTrend.Count / 3;
        string[] names = { "falling", "flat   ", "rising " };
        for (int t = 0; t < 3; t++)
        {
            var bucket = byTrend.Skip(t * third).Take(t == 2 ? int.MaxValue : third)
                                .OrderBy(o => o.VolCorr).ToList();
            int per = bucket.Count / 5;
            if (per < 50) { Console.WriteLine($"      {names[t]}: too few"); continue; }
            double gap = bucket.TakeLast(per).Average(o => o.FwdAtr) - bucket.Take(per).Average(o => o.FwdAtr);
            double p = PermutationP(bucket.Select(o => o.FwdAtr).ToArray(), per, per, gap, permutations);
            Console.WriteLine($"      trend {names[t]}: volume top−bottom quintile {gap,+6:+0.00;-0.00;0} ATR   " +
                              $"p = {p:0.0000}" + (p <= 0.05 ? "  *" : "") + $"   n={bucket.Count,6:N0}");
        }
        Console.WriteLine("      Surviving in all three buckets = volume's own information.");
        Console.WriteLine("      Collapsing = it was the trend all along.");
        Console.WriteLine();
    }

    private static double Correlation(double[] x, double[] y)
    {
        double mx = x.Average(), my = y.Average();
        double sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double a = x[i] - mx, b = y[i] - my;
            sxy += a * b; sxx += a * a; syy += b * b;
        }
        return sxx <= 0 || syy <= 0 ? double.NaN : sxy / Math.Sqrt(sxx * syy);
    }

    /// <summary>
    /// CLAIM 2: a huge volume day during a decline is capitulation and may be buyable.
    ///
    /// <para>
    /// The control that matters is the DECLINE-ONLY baseline, not the all-bars one. Buying any dip
    /// and buying a dip that came with a volume spike are different claims, and only the gap between
    /// them belongs to volume. Reporting against all bars would credit volume with whatever
    /// dip-buying earns on its own.
    /// </para>
    /// </summary>
    private static void Claim2(List<Obs> set, int permutations)
    {
        Console.WriteLine("    ── claim 2: volume spike during a decline ⇒ capitulation ──");
        var declines = set.Where(o => o.Trend < 0).ToList();
        if (declines.Count < 500) { Console.WriteLine("      too few declining bars"); Console.WriteLine(); return; }

        double declineBase = declines.Average(o => o.FwdAtr);
        Console.WriteLine($"      any declining bar (the control): {declineBase,+6:+0.00;-0.00;0} ATR   n={declines.Count:N0}");

        foreach (double th in new[] { 2.0, 3.0, 5.0, 10.0, 20.0 })
        {
            var spike = declines.Where(o => o.VolMult >= th).ToList();
            if (spike.Count < 30) { Console.WriteLine($"      ≥{th,4:0.0}× median volume: only {spike.Count} — too few"); continue; }
            var rest = declines.Where(o => o.VolMult < th).ToList();
            double gap = spike.Average(o => o.FwdAtr) - rest.Average(o => o.FwdAtr);
            double p = PermutationP(declines.Select(o => o.FwdAtr).ToArray(), spike.Count, rest.Count, gap, permutations);
            Console.WriteLine($"      ≥{th,4:0.0}× median volume: {spike.Average(o => o.FwdAtr),+6:+0.00;-0.00;0} ATR (n={spike.Count,5:N0})   " +
                              $"excess over any-decline {gap,+6:+0.00;-0.00;0} ATR   p = {p:0.0000}" + (p <= 0.05 ? "  *" : ""));
        }
        Console.WriteLine();
    }

    private static void Verdict(List<Obs> all, int permutations)
    {
        Console.WriteLine("  ── VERDICT ──");
        foreach (var g in all.GroupBy(o => o.Class).OrderByDescending(g => g.Count()))
        {
            var set = g.ToList();
            var sorted = set.OrderBy(o => o.VolCorr).ToList();
            int per = sorted.Count / 5;
            double c1 = sorted.TakeLast(per).Average(o => o.FwdAtr) - sorted.Take(per).Average(o => o.FwdAtr);
            double p1 = PermutationP(set.Select(o => o.FwdAtr).ToArray(), per, per, c1, permutations);

            var dec = set.Where(o => o.Trend < 0).ToList();
            var spike = dec.Where(o => o.VolMult >= 5).ToList();
            string c2 = spike.Count >= 30
                ? $"{spike.Average(o => o.FwdAtr) - dec.Where(o => o.VolMult < 5).Average(o => o.FwdAtr):+0.00;-0.00;0} ATR excess"
                : "too few spikes";

            Console.WriteLine($"    {g.Key,-8} claim 1 {c1,+6:+0.00;-0.00;0} ATR (p={p1:0.000})   claim 2 (≥5×) {c2}");
        }
        Console.WriteLine();
        Console.WriteLine("    Volume is the last non-price input in the candidate set. If it fails here the");
        Console.WriteLine("    conclusion is not 'volume is useless' — it is that DAILY BAR volume, the only");
        Console.WriteLine("    resolution in these snapshots, carries no forward information. Varma's own claim");
        Console.WriteLine("    is about reading order flow and level 2, which daily bars cannot represent, and");
        Console.WriteLine("    he says explicitly that the readable part is migrating to dark pools.");
    }

    // ── Measures ─────────────────────────────────────────────────────────────

    private static double ReturnVolumeCorrelation(IReadOnlyList<Ohlcv> bars, int at, int window)
    {
        var r = new List<double>(); var v = new List<double>();
        for (int i = at - window + 1; i <= at; i++)
        {
            if (i < 1 || bars[i].Close <= 0 || bars[i - 1].Close <= 0 || bars[i].Volume <= 0) continue;
            r.Add(Math.Log(bars[i].Close / bars[i - 1].Close));
            v.Add(Math.Log(bars[i].Volume));
        }
        if (r.Count < window / 2) return double.NaN;

        double mr = r.Average(), mv = v.Average();
        double srv = 0, srr = 0, svv = 0;
        for (int i = 0; i < r.Count; i++)
        {
            double a = r[i] - mr, b = v[i] - mv;
            srv += a * b; srr += a * a; svv += b * b;
        }
        return srr <= 0 || svv <= 0 ? double.NaN : srv / Math.Sqrt(srr * svv);
    }

    private static double TrailingMedianVolume(IReadOnlyList<Ohlcv> bars, int at, int window)
    {
        var v = new List<double>();
        for (int i = at - window; i < at; i++) if (bars[i].Volume > 0) v.Add(bars[i].Volume);
        if (v.Count < window / 2) return 0;
        v.Sort();
        return v[v.Count / 2];
    }

    /// <summary>
    /// Two-sample permutation test — see <see cref="LabStats.PermutationP"/>. The seed lives here,
    /// not in the shared helper, because it is this command's research parameter.
    /// Capped at 4,000 permutations: this command runs the test inside a loop over
    /// many buckets, and the full count would dominate its runtime.
    /// </summary>
    private static double PermutationP(double[] pool, int nA, int nB, double observed, int runs) =>
        LabStats.PermutationP(pool, nA, nB, observed, runs, seed: 8181, cap: 4_000);
}
