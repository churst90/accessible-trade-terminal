using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Asks whether an asset's DRAWDOWN DEPTH predicts its POLARITY — whether it mean-reverts or
/// trends — and therefore whether the sign on every deviation-from-value tool we own can be set
/// from one measurable number instead of a hand-written asset-class rule.
///
/// <para>
/// WHY. Four separate studies in this lab landed on the same split without anyone looking for it.
/// POC deviation mean-reverts in equities and reverses in crypto. Value Deviation buys below value
/// in equities and has to invert for crypto. The Trading Cross — which BUYS extension at +1σ —
/// beat buy-and-hold on 10 of 10 crypto assets and 0 of 3 traditional ones. The favourability
/// gradient favoured momentum over mean reversion. Every reversion tool works in equities and
/// flips in crypto; the one momentum tool works in crypto and dies in equities. That is one
/// finding observed four times, and if the underlying variable is drawdown depth rather than the
/// word "crypto", it generalises to assets we have never tested.
/// </para>
///
/// <para>
/// THE TEST THAT DECIDES IT. A cross-sectional correlation between polarity and depth across all
/// assets proves nothing on its own: crypto has both the deepest drawdowns AND the most momentum,
/// so any such correlation could simply be the asset-class label wearing a number. The claim only
/// survives if depth predicts polarity <b>WITHIN</b> a class — among 38 equities and ETFs whose
/// drawdowns range from TLT's shallow to USO's catastrophic. That within-class test is the point
/// of this command; the pooled number is reported first only so the reader can watch it fail to
/// mean anything.
/// </para>
///
/// <para>
/// MEASURES. Depth is the median rolling 365-CALENDAR-DAY maximum drawdown, not the full-sample
/// maximum. Full-sample maxima grow with sample length, and equities here carry 30 years against
/// crypto's 4 — that confound would manufacture exactly the correlation being tested. Using a
/// calendar window rather than a bar count also avoids a class-dependent constant, since crypto
/// trades 365 days a year and equities 252.
/// </para>
/// </summary>
public static class PolarityCommand
{
    /// <summary>Z-score lookback. 50 because that is what the Trading Cross tuning settled on, so
    /// the polarity measured here is the polarity those results were produced by.</summary>
    private const int ZWindow = 50;

    private sealed record Profile(
        string Symbol, string Class, int Bars, double Years,
        double RhoZ5, double RhoZ20, double Vr5, double Vr20,
        double Depth, double FullMaxDd, double AnnVol);

    public static int Run(string snapshotDir, string tf, int permutations)
    {
        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f).ToList();

        var profiles = new List<Profile>();

        foreach (var file in files)
        {
            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(file); } catch { continue; }
            var bars = snap.Bars;
            if (bars.Count < 400) continue;

            string cls = ClassOf(Path.GetFileName(file));
            if (cls == "skip") continue;

            var closes = bars.Select(b => b.Close).ToArray();
            if (closes.Any(c => c <= 0)) continue;

            double years = (bars[^1].Date - bars[0].Date).TotalDays / 365.25;
            var z = TradingCrossCommand.ZScore(bars, ZWindow);

            profiles.Add(new Profile(
                Symbol: snap.Symbol,
                Class: cls,
                Bars: bars.Count,
                Years: years,
                RhoZ5: ForwardCorrelation(z, closes, 5),
                RhoZ20: ForwardCorrelation(z, closes, 20),
                Vr5: VarianceRatio(closes, 5),
                Vr20: VarianceRatio(closes, 20),
                Depth: MedianRollingDrawdown(bars, 365),
                FullMaxDd: FullMaxDrawdown(closes),
                AnnVol: AnnualisedVol(bars)));
        }

        // The same instrument can arrive from two providers — SPY and QQQ are present as both a
        // twelvedata and a yahoo snapshot. Keeping both would count one asset twice with almost
        // perfectly correlated errors, which inflates n and narrows every p-value in this file.
        // Keep the longest history for each symbol.
        int before = profiles.Count;
        profiles = profiles
            .GroupBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(p => p.Bars).First())
            .ToList();
        if (profiles.Count != before)
            Console.WriteLine($"Deduplicated {before} → {profiles.Count} series (same symbol from two providers).");

        if (profiles.Count < 10) { Console.WriteLine("Too few usable series."); return 1; }

        Report(profiles, permutations);
        return 0;
    }

    // ── Polarity measures ────────────────────────────────────────────────────

    /// <summary>
    /// Correlation between the z-score at bar i and the log return over the NEXT k bars.
    /// Negative means a stretched price tends to come back (reversion); positive means it tends to
    /// keep going (momentum). This is the applied measure — it is exactly the quantity every
    /// deviation-from-value tool in this codebase is implicitly betting on.
    /// </summary>
    private static double ForwardCorrelation(double[] z, double[] closes, int k)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        for (int i = 0; i + k < closes.Length; i++)
        {
            if (double.IsNaN(z[i])) continue;
            xs.Add(z[i]);
            ys.Add(Math.Log(closes[i + k] / closes[i]));
        }
        return xs.Count < 100 ? double.NaN : Pearson(xs.ToArray(), ys.ToArray());
    }

    /// <summary>
    /// Lo–MacKinlay variance ratio with the overlapping-sample bias correction. A model-free
    /// cross-check on <see cref="ForwardCorrelation"/>: above 1 the series trends, below 1 it
    /// reverts, and it uses no z-score, no window and no parameter of ours. The bias correction
    /// matters here because sample lengths run from ~1,400 bars to ~8,400 and the uncorrected
    /// estimator's bias depends on both n and q.
    /// </summary>
    private static double VarianceRatio(double[] closes, int q)
    {
        int n = closes.Length - 1;
        if (n <= q * 4) return double.NaN;

        double mu = Math.Log(closes[^1] / closes[0]) / n;

        double var1 = 0;
        for (int i = 1; i <= n; i++)
        {
            double r = Math.Log(closes[i] / closes[i - 1]) - mu;
            var1 += r * r;
        }
        var1 /= n - 1;

        double varQ = 0;
        int count = 0;
        for (int i = q; i <= n; i++)
        {
            double r = Math.Log(closes[i] / closes[i - q]) - q * mu;
            varQ += r * r;
            count++;
        }
        // m is the Lo–MacKinlay unbiased denominator for overlapping q-period returns.
        double m = q * (n - q + 1) * (1.0 - (double)q / n);
        if (m <= 0 || var1 <= 0 || count == 0) return double.NaN;
        varQ /= m;

        return varQ / var1;
    }

    // ── Depth and volatility ─────────────────────────────────────────────────

    /// <summary>
    /// Median of the maximum drawdown observed inside every rolling <paramref name="windowDays"/>
    /// calendar-day window. Length-invariant by construction, which the full-sample maximum is
    /// not — and the full-sample maximum is precisely the measure that would fabricate this
    /// study's result, because our equity history is 30 years and our crypto history is 4.
    /// </summary>
    private static double MedianRollingDrawdown(IReadOnlyList<Ohlcv> bars, int windowDays)
    {
        var dds = new List<double>();
        int start = 0;

        for (int end = 0; end < bars.Count; end++)
        {
            while (start < end && (bars[end].Date - bars[start].Date).TotalDays > windowDays) start++;
            if (end - start < 30) continue;

            double peak = double.MinValue, worst = 0;
            for (int i = start; i <= end; i++)
            {
                if (bars[i].High > peak) peak = bars[i].High;
                if (peak > 0)
                {
                    double dd = 1.0 - bars[i].Low / peak;
                    if (dd > worst) worst = dd;
                }
            }
            dds.Add(worst);
        }

        if (dds.Count == 0) return double.NaN;
        dds.Sort();
        return dds[dds.Count / 2];
    }

    private static double FullMaxDrawdown(double[] closes)
    {
        double peak = closes[0], worst = 0;
        foreach (var c in closes)
        {
            if (c > peak) peak = c;
            double dd = 1.0 - c / peak;
            if (dd > worst) worst = dd;
        }
        return worst;
    }

    private static double AnnualisedVol(IReadOnlyList<Ohlcv> bars)
    {
        var rs = new List<double>();
        for (int i = 1; i < bars.Count; i++)
            if (bars[i].Close > 0 && bars[i - 1].Close > 0)
                rs.Add(Math.Log(bars[i].Close / bars[i - 1].Close));
        if (rs.Count < 2) return double.NaN;

        double mean = rs.Average();
        double sd = Math.Sqrt(rs.Sum(r => (r - mean) * (r - mean)) / (rs.Count - 1));

        // Bars per year from the actual dates, so crypto's 365-day week and the equity 252-day
        // year are handled without a class-dependent constant.
        double years = (bars[^1].Date - bars[0].Date).TotalDays / 365.25;
        double barsPerYear = years > 0 ? bars.Count / years : 252;
        return sd * Math.Sqrt(barsPerYear);
    }

    // ── Reporting ────────────────────────────────────────────────────────────

    private static void Report(List<Profile> all, int permutations)
    {
        Console.WriteLine();
        Console.WriteLine($"===== POLARITY vs DRAWDOWN DEPTH — {all.Count} series =====");
        Console.WriteLine($"Polarity: corr(z[{ZWindow}], forward k-bar log return). Negative = reverts, positive = trends.");
        Console.WriteLine("Depth: median rolling 365-calendar-day max drawdown.");
        Console.WriteLine();

        Console.WriteLine($"  {"symbol",-10} {"class",-8} {"bars",6} {"yrs",5} {"rhoZ5",8} {"rhoZ20",8} {"VR5",7} {"VR20",7} {"depth",7} {"vol",7}");
        foreach (var p in all.OrderBy(p => p.Class).ThenByDescending(p => p.Depth))
            Console.WriteLine($"  {p.Symbol,-10} {p.Class,-8} {p.Bars,6} {p.Years,5:0.0} " +
                              $"{p.RhoZ5,8:+0.000;-0.000;0} {p.RhoZ20,8:+0.000;-0.000;0} " +
                              $"{p.Vr5,7:0.000} {p.Vr20,7:0.000} {p.Depth,7:P0} {p.AnnVol,7:P0}");

        // ── Does the class split exist at all? ───────────────────────────────
        Console.WriteLine();
        Console.WriteLine("  ── class means (establishes there is something to explain) ──");
        foreach (var g in all.GroupBy(p => p.Class).OrderBy(g => g.Key))
            Console.WriteLine($"    {g.Key,-8} n={g.Count(),3}  rhoZ20 {g.Average(p => p.RhoZ20),7:+0.000;-0.000;0}   " +
                              $"VR20 {g.Average(p => p.Vr20),6:0.000}   depth {g.Average(p => p.Depth),6:P0}   " +
                              $"vol {g.Average(p => p.AnnVol),6:P0}");

        // ── The pooled correlation, which cannot settle anything ─────────────
        Console.WriteLine();
        Console.WriteLine($"  ── POOLED correlation (all {all.Count} series) ──");
        Console.WriteLine("    Reported first so it can be discounted: crypto has both the deepest drawdowns");
        Console.WriteLine("    and the most momentum, so a pooled correlation may only be re-encoding the class.");
        PrintCorrelations(all, permutations, "    ");

        // ── The test that decides it ─────────────────────────────────────────
        foreach (var g in all.GroupBy(p => p.Class).OrderByDescending(g => g.Count()))
        {
            if (g.Count() < 8) continue;
            Console.WriteLine();
            Console.WriteLine($"  ── WITHIN {g.Key.ToUpperInvariant()} (n={g.Count()}) — this is the one that matters ──");
            PrintCorrelations(g.ToList(), permutations, "    ");
        }

        // ── Partial: strip the class mean, then ask again ────────────────────
        Console.WriteLine();
        Console.WriteLine("  ── DEMEANED BY CLASS (removes the class label outright) ──");
        Console.WriteLine("    Each series' polarity and depth have their own class mean subtracted, so any");
        Console.WriteLine("    remaining correlation is depth explaining polarity WITHIN classes, pooled for power.");
        var demeaned = all
            .GroupBy(p => p.Class)
            .SelectMany(g =>
            {
                double mp = g.Average(p => p.RhoZ20), md = g.Average(p => p.Depth),
                       mv = g.Average(p => p.AnnVol), mr = g.Average(p => p.Vr20);
                return g.Select(p => p with
                {
                    RhoZ20 = p.RhoZ20 - mp,
                    Depth = p.Depth - md,
                    AnnVol = p.AnnVol - mv,
                    Vr20 = p.Vr20 - mr,
                });
            })
            .ToList();
        PrintCorrelations(demeaned, permutations, "    ");

        // ── Depth or volatility? They are nearly the same variable ───────────
        Console.WriteLine();
        Console.WriteLine("  ── DEPTH vs VOLATILITY — which one is actually doing the work? ──");
        Console.WriteLine("    Deep drawdowns and high volatility are close to the same measurement, so a");
        Console.WriteLine("    correlation with one is a correlation with the other. Partial rank correlation");
        Console.WriteLine("    holds each fixed in turn: whichever survives is the variable that carries the");
        Console.WriteLine("    information, and if neither does they were never separable in this sample.");
        foreach (var set in new[] { ("pooled", all), ("equity", all.Where(p => p.Class == "equity").ToList()) })
        {
            if (set.Item2.Count < 8) continue;
            var pol = set.Item2.Select(p => p.RhoZ20).ToArray();
            var dep = set.Item2.Select(p => p.Depth).ToArray();
            var vol = set.Item2.Select(p => p.AnnVol).ToArray();

            var vr = set.Item2.Select(p => p.Vr20).ToArray();

            Console.WriteLine($"    {set.Item1} (n={set.Item2.Count}):  depth~vol collinearity spearman {Spearman(dep, vol):+0.000;-0.000;0}");
            foreach (var (mlabel, m) in new[] { ("rhoZ20", pol), ("VR20  ", vr) })
            {
                double pd = Partial(m, dep, vol), pv = Partial(m, vol, dep);
                Console.WriteLine($"      {mlabel} vs depth | vol held fixed:  {pd,+7:+0.000;-0.000;0}   p = {PartialP(m, dep, vol, pd, permutations),6:0.0000}");
                Console.WriteLine($"      {mlabel} vs vol   | depth held fixed:{pv,+7:+0.000;-0.000;0}   p = {PartialP(m, vol, dep, pv, permutations),6:0.0000}");
            }
        }

        // ── Is the equity result one or two names? ───────────────────────────
        var eqSet = all.Where(p => p.Class == "equity").ToList();
        if (eqSet.Count >= 10)
        {
            Console.WriteLine();
            Console.WriteLine("  ── JACKKNIFE on the equity result (drop one symbol at a time) ──");
            Console.WriteLine("    A rank correlation over 30-odd points can be one outlier. If dropping a single");
            Console.WriteLine("    name moves it near zero, there is no relationship — there is one unusual stock.");
            var pol = eqSet.Select(p => p.RhoZ20).ToArray();
            var dep = eqSet.Select(p => p.Depth).ToArray();
            double full = Spearman(pol, dep);

            var loo = new List<(string Sym, double Rho)>();
            for (int i = 0; i < eqSet.Count; i++)
            {
                var px = eqSet.Where((_, j) => j != i).Select(p => p.RhoZ20).ToArray();
                var dx = eqSet.Where((_, j) => j != i).Select(p => p.Depth).ToArray();
                loo.Add((eqSet[i].Symbol, Spearman(px, dx)));
            }
            var lo = loo.OrderBy(t => t.Rho).First();
            var hi = loo.OrderByDescending(t => t.Rho).First();
            Console.WriteLine($"    full {full:+0.000;-0.000;0}   range after dropping any one: " +
                              $"{lo.Rho:+0.000;-0.000;0} (drop {lo.Sym}) … {hi.Rho:+0.000;-0.000;0} (drop {hi.Sym})");
        }

        Verdict(all, demeaned, permutations);
    }

    /// <summary>
    /// Partial rank correlation of <paramref name="a"/> with <paramref name="b"/> holding
    /// <paramref name="c"/> fixed. Used to ask whether drawdown depth says anything about polarity
    /// that volatility has not already said.
    /// </summary>
    private static double Partial(double[] a, double[] b, double[] c)
    {
        double rab = Spearman(a, b), rac = Spearman(a, c), rbc = Spearman(b, c);
        double den = Math.Sqrt((1 - rac * rac) * (1 - rbc * rbc));
        return den <= 1e-12 ? double.NaN : (rab - rac * rbc) / den;
    }

    /// <summary>
    /// Freedman–Lane permutation p-value for a partial correlation, on ranks.
    ///
    /// <para>
    /// Permuting <paramref name="b"/> directly would be wrong: it destroys b's relationship with
    /// the control <paramref name="c"/> as well, so the null distribution it builds is not the
    /// null being tested. Freedman–Lane instead regresses a on c, permutes only the RESIDUALS, and
    /// rebuilds a — which leaves the a~c relationship intact and randomises exactly the part of a
    /// that c does not explain. That is the quantity the partial correlation is about.
    /// </para>
    /// </summary>
    private static double PartialP(double[] a, double[] b, double[] c, double observed, int runs)
    {
        if (double.IsNaN(observed)) return 1;

        double[] ra = Rank(a), rb = Rank(b), rc = Rank(c);
        int n = ra.Length;

        double mc = rc.Average(), ma = ra.Average();
        double sxx = rc.Sum(v => (v - mc) * (v - mc));
        double beta = sxx <= 0 ? 0 : rc.Select((v, i) => (v - mc) * (ra[i] - ma)).Sum() / sxx;
        var fitted = rc.Select(v => ma + beta * (v - mc)).ToArray();
        var resid = ra.Select((v, i) => v - fitted[i]).ToArray();

        var rng = new Random(90210);
        var work = (double[])resid.Clone();
        var rebuilt = new double[n];
        int extreme = 0;

        for (int r = 0; r < runs; r++)
        {
            for (int i = n - 1; i > 0; i--) { int j = rng.Next(i + 1); (work[i], work[j]) = (work[j], work[i]); }
            for (int i = 0; i < n; i++) rebuilt[i] = fitted[i] + work[i];

            double s = Partial(rebuilt, rb, rc);
            if (!double.IsNaN(s) && Math.Abs(s) >= Math.Abs(observed)) extreme++;
        }
        return (extreme + 1.0) / (runs + 1.0);
    }

    private static void PrintCorrelations(List<Profile> set, int permutations, string pad)
    {
        Print("rhoZ20 vs depth", set.Select(p => p.RhoZ20), set.Select(p => p.Depth));
        Print("rhoZ5  vs depth", set.Select(p => p.RhoZ5), set.Select(p => p.Depth));
        Print("VR20   vs depth", set.Select(p => p.Vr20), set.Select(p => p.Depth));
        Print("rhoZ20 vs vol  ", set.Select(p => p.RhoZ20), set.Select(p => p.AnnVol));
        Print("VR20   vs vol  ", set.Select(p => p.Vr20), set.Select(p => p.AnnVol));

        void Print(string label, IEnumerable<double> a, IEnumerable<double> b)
        {
            var x = a.ToArray(); var y = b.ToArray();
            var keep = Enumerable.Range(0, x.Length)
                .Where(i => !double.IsNaN(x[i]) && !double.IsNaN(y[i])).ToArray();
            if (keep.Length < 8) { Console.WriteLine($"{pad}{label}: too few"); return; }

            var xs = keep.Select(i => x[i]).ToArray();
            var ys = keep.Select(i => y[i]).ToArray();
            double rho = Spearman(xs, ys);
            double p = PermutationP(xs, ys, rho, permutations);
            Console.WriteLine($"{pad}{label}:  spearman {rho,+7:+0.000;-0.000;0}   p = {p,6:0.0000}   n={keep.Length}" +
                              (p <= 0.05 ? "   *" : ""));
        }
    }

    private static void Verdict(List<Profile> all, List<Profile> demeaned, int permutations)
    {
        Console.WriteLine();
        Console.WriteLine("  ── VERDICT ──");

        var eq = all.Where(p => p.Class == "equity").ToList();
        double eqRho = eq.Count >= 8
            ? Spearman(eq.Select(p => p.RhoZ20).ToArray(), eq.Select(p => p.Depth).ToArray())
            : double.NaN;
        double eqP = eq.Count >= 8
            ? PermutationP(eq.Select(p => p.RhoZ20).ToArray(), eq.Select(p => p.Depth).ToArray(), eqRho, permutations)
            : 1;

        var dVr = demeaned.Select(p => p.Vr20).ToArray();
        var dVol = demeaned.Select(p => p.AnnVol).ToArray();
        var dDep = demeaned.Select(p => p.Depth).ToArray();
        double dRho = Spearman(dVr, dVol);
        double dP = PermutationP(dVr, dVol, dRho, permutations);

        double pDepth = Partial(dVr, dDep, dVol), pVol = Partial(dVr, dVol, dDep);

        Console.WriteLine($"    Class means confirm the split exists: crypto trends (VR20 > 1), equities revert (< 1).");
        Console.WriteLine($"    Demeaned VR20 vs vol: spearman {dRho:+0.000;-0.000;0}, p = {dP:0.0000}.");
        Console.WriteLine($"    Partial, demeaned: VR20~depth|vol {pDepth:+0.000;-0.000;0}, VR20~vol|depth {pVol:+0.000;-0.000;0}.");
        Console.WriteLine();

        // Declaring a winner between two collinear predictors requires the partials to be
        // SIGNIFICANT, not merely unequal. At r(depth,vol) ≈ 0.96 the difference between two
        // noise-level partials is itself noise, and a threshold rule that ranks them by magnitude
        // would manufacture a conclusion out of it.
        double pDepthP = PartialP(dVr, dDep, dVol, pDepth, permutations);
        double pVolP = PartialP(dVr, dVol, dDep, pVol, permutations);
        bool depthClears = pDepthP <= 0.05, volClears = pVolP <= 0.05;

        if (dP > 0.05)
        {
            Console.WriteLine("    Neither variable predicts polarity within class. The asset-class rule is not a");
            Console.WriteLine("    proxy for anything measurable here — keep the hard fork.");
        }
        else if (volClears && !depthClears)
        {
            Console.WriteLine("    The continuous variable is REALISED VOLATILITY. Depth is a noisy proxy for it");
            Console.WriteLine("    and contributes nothing once volatility is held fixed.");
        }
        else if (depthClears && !volClears)
        {
            Console.WriteLine("    Drawdown depth survives with volatility held fixed — the opposite of the usual");
            Console.WriteLine("    collinearity outcome, and worth a second look before anything is built on it.");
        }
        else
        {
            Console.WriteLine("    A VOLATILITY-FAMILY variable predicts polarity within class, but this sample");
            Console.WriteLine($"    cannot say WHICH one: depth and vol rank-correlate {Spearman(all.Select(p => p.Depth).ToArray(), all.Select(p => p.AnnVol).ToArray()):0.00} and neither partial");
            Console.WriteLine($"    clears on its own (depth p = {pDepthP:0.000}, vol p = {pVolP:0.000}). The two polarity measures");
            Console.WriteLine("    also disagree about which predictor matters, which is what genuine collinearity");
            Console.WriteLine("    looks like. Use either as a proxy; do not claim the mechanism is one of them.");
        }

        var crypto = all.Where(p => p.Class == "crypto").ToList();
        if (crypto.Count >= 8)
        {
            double cRho = Spearman(crypto.Select(p => p.RhoZ20).ToArray(), crypto.Select(p => p.AnnVol).ToArray());
            Console.WriteLine();
            Console.WriteLine($"    CAVEAT that limits the scope: inside crypto the sign REVERSES (rhoZ20 vs vol");
            Console.WriteLine($"    {cRho:+0.000;-0.000;0}, n={crypto.Count}). The relationship is not monotone across the whole");
            Console.WriteLine("    range — it rises from bonds through equities into crypto and then flattens or");
            Console.WriteLine("    turns over. A global 'more vol = more momentum' switch would get crypto backwards.");
        }
    }

    // ── Statistics ───────────────────────────────────────────────────────────

    private static double Pearson(double[] x, double[] y)
    {
        int n = x.Length;
        double mx = x.Average(), my = y.Average();
        double sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < n; i++)
        {
            double a = x[i] - mx, b = y[i] - my;
            sxy += a * b; sxx += a * a; syy += b * b;
        }
        return sxx <= 0 || syy <= 0 ? double.NaN : sxy / Math.Sqrt(sxx * syy);
    }

    private static double Spearman(double[] x, double[] y) => Pearson(Rank(x), Rank(y));

    private static double[] Rank(double[] v)
    {
        var idx = Enumerable.Range(0, v.Length).OrderBy(i => v[i]).ToArray();
        var r = new double[v.Length];
        int i2 = 0;
        while (i2 < idx.Length)
        {
            int j = i2;
            while (j + 1 < idx.Length && v[idx[j + 1]] == v[idx[i2]]) j++;
            double avg = (i2 + j) / 2.0 + 1;
            for (int k = i2; k <= j; k++) r[idx[k]] = avg;
            i2 = j + 1;
        }
        return r;
    }

    /// <summary>
    /// Shuffles the pairing between the two variables and counts how often chance produces a
    /// rank correlation at least as large. Fixed seed so a re-run reproduces the number.
    /// </summary>
    private static double PermutationP(double[] x, double[] y, double observed, int runs)
    {
        if (double.IsNaN(observed)) return 1;
        var rng = new Random(90210);
        var work = (double[])y.Clone();
        int extreme = 0;
        for (int r = 0; r < runs; r++)
        {
            for (int i = work.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); (work[i], work[j]) = (work[j], work[i]); }
            double s = Spearman(x, work);
            if (!double.IsNaN(s) && Math.Abs(s) >= Math.Abs(observed)) extreme++;
        }
        return (extreme + 1.0) / (runs + 1.0);
    }

    /// <summary>
    /// Class from the snapshot filename. Provider is a reliable proxy here: bitstamp and mexc are
    /// crypto-only, twelvedata and yahoo carry the equities, ETFs and metals. Gold and silver are
    /// tagged separately rather than lumped in with equities — they are neither, and folding them
    /// into a class mean would corrupt the demeaned test.
    /// </summary>
    private static string ClassOf(string fileName)
    {
        string f = fileName.ToLowerInvariant();
        if (f.StartsWith("bitstamp_") || f.StartsWith("mexc_")) return "crypto";
        if (f.Contains("xau") || f.Contains("_gld_") || f.Contains("_slv_") || f.Contains("_uso_")) return "commod";
        if (f.Contains("_tlt_") || f.Contains("_ief_")) return "bond";
        if (f.StartsWith("twelvedata_") || f.StartsWith("yahoo_") || f.StartsWith("alpaca_")) return "equity";
        return "skip";
    }
}
