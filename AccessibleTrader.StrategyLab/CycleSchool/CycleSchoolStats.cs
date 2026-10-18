namespace AccessibleTrader.StrategyLab.CycleSchool;

/// <summary>A detector: its calls on an asset, the window it is scored with, and (for N2 and the
/// trade control) its eligible bars grouped by swing low.</summary>
public sealed record Det(string Id, string Window, Func<AssetRun, List<int>> Calls, Func<AssetRun, Dictionary<int, List<int>>>? Elig = null);

/// <summary>Observed scoring of one detector over one class and era (prereg 3.4).</summary>
public sealed class EraStats
{
    public int Calls, Hits, Excluded, LowsInEra, LowsHit;
    public double Years;
    public List<int> Lags = new(), Leads = new();
    public Dictionary<string, (int Calls, int Hits)> PerAsset = new();
    public double Precision => Calls == 0 ? double.NaN : (double)Hits / Calls;
    public double Recall => LowsInEra == 0 ? double.NaN : (double)LowsHit / LowsInEra;
    public double FalsePerYear => Years <= 0 ? double.NaN : (Calls - Hits) / Years;
    public double MedianLag => Lags.Count == 0 ? double.NaN : CycleSchoolCore.Median(Lags.Select(v => (double)v).ToList());
    public double MedianLead => Leads.Count == 0 ? double.NaN : CycleSchoolCore.Median(Leads.Select(v => (double)v).ToList());
    public double ShareLeadPositive => Leads.Count == 0 ? double.NaN : Leads.Count(v => v > 0) / (double)Leads.Count;
}

public sealed record NullResult(double P, double Mean, Dictionary<string, double> PerAssetMean, int Draws, double P95 = double.NaN);

/// <summary>
/// The nulls and the significance rules of prereg 3.5 and 3.6. Every draw is seeded through
/// <see cref="CycleSchoolCore.Rng"/>, so a rerun reproduces every p-value.
/// </summary>
public static class CycleSchoolStats
{
    public static IEnumerable<int> InEra(AssetRun r, IEnumerable<int> calls, Era e) => calls.Where(t => r.EraOf(t) == e);

    public static EraStats Observe(IReadOnlyList<AssetRun> runs, Det d, Era e)
    {
        var s = new EraStats();
        foreach (var r in runs)
        {
            var h = r.Hits(d.Window);
            int calls = 0, hits = 0;
            var scoredCalls = new List<int>();
            foreach (var t in InEra(r, d.Calls(r), e))
            {
                if (h[t] is not { } sc) { s.Excluded++; continue; }
                calls++;
                scoredCalls.Add(t);
                if (!sc.Hit) continue;
                hits++;
                s.Lags.Add(sc.Lag);
                s.Leads.Add(sc.Lead);
            }
            s.Calls += calls; s.Hits += hits;
            s.PerAsset[r.A.Name] = (calls, hits);
            var lows = d.Window.StartsWith("D", StringComparison.Ordinal) ? r.GD : r.GW;
            var w = WindowOf(r, d.Window);
            foreach (var low in lows.Where(l => r.EraOf(l.Low) == e))
            {
                s.LowsInEra++;
                if (scoredCalls.Any(t => low.Low >= t - w.Before && low.Low <= t + w.After)) s.LowsHit++;
            }
            var eraDays = r.B.Where(b => (b.Day < AssetRun.EraCut) == (e == Era.Dev)).Select(b => b.Day).ToList();
            if (eraDays.Count > 1) s.Years += (eraDays[^1].DayNumber - eraDays[0].DayNumber) / 365.25;
        }
        return s;
    }

    public static CycleSchoolCore.HitWindow WindowOf(AssetRun r, string window) => window switch
    {
        "D-post" => new(15, 0),
        "D-alert" => new(5, 20),
        "W-post" => new(15 * r.BarsPerWeek, 0),
        "W-alert" => new(5 * r.BarsPerWeek, 20 * r.BarsPerWeek),
        _ => throw new ArgumentException(window),
    };

    /// <summary>N1: chance timing, one circular shift per asset per draw.</summary>
    public static NullResult N1(IReadOnlyList<AssetRun> runs, Det d, Era e, string test, double observed, int draws = 10_000)
    {
        var prep = runs.Select(r =>
        {
            var h = r.Hits(d.Window);
            var calls = InEra(r, d.Calls(r), e).Where(t => h[t] is not null).ToList();
            return (r, h, calls, range: r.Scorable(d.Window, e));
        }).ToList();
        int ge = 0;
        double sum = 0;
        var perAsset = prep.ToDictionary(p => p.r.A.Name, _ => 0.0);
        var perAssetN = prep.ToDictionary(p => p.r.A.Name, _ => 0);
        var vals = new double[draws];
        for (int k = 0; k < draws; k++)
        {
            int hits = 0, calls = 0;
            foreach (var (r, h, c, range) in prep)
            {
                if (c.Count == 0 || range.A < 0) continue;
                var shifted = CycleSchoolCore.CircularShift(c, range.A, range.Z, CycleSchoolCore.Rng(test, r.A.Name, k));
                int ah = 0, ac = 0;
                foreach (var t in shifted)
                {
                    if (h[t] is not { } sc) continue;
                    ac++;
                    if (sc.Hit) ah++;
                }
                hits += ah; calls += ac;
                if (ac > 0) { perAsset[r.A.Name] += (double)ah / ac; perAssetN[r.A.Name]++; }
            }
            double prec = calls == 0 ? double.NaN : (double)hits / calls;
            vals[k] = prec;
            sum += double.IsNaN(prec) ? 0 : prec;
            if (!double.IsNaN(prec) && prec >= observed) ge++;
        }
        return new NullResult((1.0 + ge) / (1.0 + draws), sum / draws,
            perAsset.ToDictionary(kv => kv.Key, kv => perAssetN[kv.Key] == 0 ? double.NaN : kv.Value / perAssetN[kv.Key]), draws,
            CycleSchoolCore.Quantile(vals, 0.95));
    }

    /// <summary>N2: the same number of calls per asset, each a distinct random swing low (or anchor)
    /// and a uniform call bar among its eligible bars (prereg 3.5).</summary>
    public static NullResult N2(IReadOnlyList<AssetRun> runs, Det d, Era e, string test, double observed, int draws = 10_000)
    {
        if (d.Elig == null) throw new InvalidOperationException($"{d.Id} has no N2 baseline");
        var prep = runs.Select(r =>
        {
            var h = r.Hits(d.Window);
            int k = InEra(r, d.Calls(r), e).Count(t => h[t] is not null);
            var groups = d.Elig(r).Values
                .Select(bars => bars.Where(t => r.EraOf(t) == e && h[t] is not null).ToArray())
                .Where(g => g.Length > 0).ToArray();
            return (r, h, k: Math.Min(k, groups.Length), groups);
        }).ToList();
        int ge = 0;
        double sum = 0;
        var perAsset = prep.ToDictionary(p => p.r.A.Name, _ => 0.0);
        var perAssetN = prep.ToDictionary(p => p.r.A.Name, _ => 0);
        for (int dr = 0; dr < draws; dr++)
        {
            int hits = 0, calls = 0;
            foreach (var (r, h, k, groups) in prep)
            {
                if (k == 0) continue;
                var rng = CycleSchoolCore.Rng(test, r.A.Name, dr);
                var idx = Enumerable.Range(0, groups.Length).ToArray();
                int ah = 0;
                for (int q = 0; q < k; q++)
                {
                    int pick = q + rng.Next(groups.Length - q);
                    (idx[q], idx[pick]) = (idx[pick], idx[q]);
                    var g = groups[idx[q]];
                    int t = g[rng.Next(g.Length)];
                    if (h[t]!.Value.Hit) ah++;
                }
                hits += ah; calls += k;
                perAsset[r.A.Name] += (double)ah / k; perAssetN[r.A.Name]++;
            }
            double prec = calls == 0 ? double.NaN : (double)hits / calls;
            sum += double.IsNaN(prec) ? 0 : prec;
            if (!double.IsNaN(prec) && prec >= observed) ge++;
        }
        return new NullResult((1.0 + ge) / (1.0 + draws), sum / draws,
            perAsset.ToDictionary(kv => kv.Key, kv => perAssetN[kv.Key] == 0 ? double.NaN : kv.Value / perAssetN[kv.Key]), draws);
    }

    /// <summary>
    /// The difference of two detectors' pooled precisions, with a block bootstrap over calendar
    /// years: years are resampled with replacement and every call of a year, on every asset of the
    /// class, moves with it. One-sided p = (1 + resamples with difference ≤ 0) ÷ (1 + draws).
    /// </summary>
    public static (double Diff, double P) YearBootstrap(IReadOnlyList<AssetRun> runs, Det a, Det b, Era e, string test, int draws = 10_000)
    {
        var years = new SortedDictionary<int, (int Ha, int Ca, int Hb, int Cb)>();
        foreach (var r in runs)
        {
            foreach (var bar in r.B.Where(x => (x.Day < AssetRun.EraCut) == (e == Era.Dev)))
                if (!years.ContainsKey(bar.Day.Year)) years[bar.Day.Year] = (0, 0, 0, 0);
            foreach (var (det, isA) in new[] { (a, true), (b, false) })
            {
                var h = r.Hits(det.Window);
                foreach (var t in InEra(r, det.Calls(r), e))
                {
                    if (h[t] is not { } sc) continue;
                    var y = r.B[t].Day.Year;
                    var v = years[y];
                    years[y] = isA ? (v.Ha + (sc.Hit ? 1 : 0), v.Ca + 1, v.Hb, v.Cb) : (v.Ha, v.Ca, v.Hb + (sc.Hit ? 1 : 0), v.Cb + 1);
                }
            }
        }
        var ys = years.Values.ToArray();
        double Diff(IEnumerable<(int Ha, int Ca, int Hb, int Cb)> set)
        {
            int ha = 0, ca = 0, hb = 0, cb = 0;
            foreach (var v in set) { ha += v.Ha; ca += v.Ca; hb += v.Hb; cb += v.Cb; }
            return ca == 0 || cb == 0 ? double.NaN : (double)ha / ca - (double)hb / cb;
        }
        double obs = Diff(ys);
        int le = 0;
        for (int k = 0; k < draws; k++)
        {
            var rng = CycleSchoolCore.Rng(test, "years", k);
            var sample = Enumerable.Range(0, ys.Length).Select(_ => ys[rng.Next(ys.Length)]);
            double dv = Diff(sample);
            if (double.IsNaN(dv) || dv <= 0) le++;
        }
        return (obs, (1.0 + le) / (1.0 + draws));
    }

    /// <summary>The one-sided exact binomial tail P(X ≥ k | n, ½), for the sign test and McNemar.</summary>
    public static double BinomUpper(int k, int n)
    {
        if (n == 0) return 1;
        double p = 0;
        for (int i = k; i <= n; i++) p += Math.Exp(LogChoose(n, i) - n * Math.Log(2));
        return Math.Min(1, p);
    }

    private static double LogChoose(int n, int k)
    {
        double s = 0;
        for (int i = 1; i <= k; i++) s += Math.Log(n - k + i) - Math.Log(i);
        return s;
    }
}
