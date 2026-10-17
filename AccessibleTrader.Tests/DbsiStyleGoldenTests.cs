using System.Globalization;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fixtures;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The DBSI-style provider replays the feasibility study, bar for bar.</b>
///
/// <para>
/// The 143 digitised Market Cipher DBSI candles (six charts, 24 bars each, a few labels hidden)
/// and each chart's own bars are in <see cref="DbsiDigitisedCharts"/>, generated from the study's
/// files. Three claims, each against the study rather than against this code:
/// </para>
/// <list type="number">
///   <item>The provider's twelve votes and the integers it prints are the study model's, on every
///         one of the 144 bars — the port is exact, not "close".</item>
///   <item>Refitting <c>bull = a + b·votes</c>, <c>bear = c + b·votes</c> to the digitised numbers
///         FROM THE PROVIDER'S OWN VOTES recovers the shipped constants (−4.3320339, 1.66805928,
///         −4.79048715) — so the constants are the fit of these votes, not numbers copied beside
///         a different vote.</item>
///   <item>The fidelity the manual will quote holds: in sample, r 0.92/0.93, error 3.1 points, side
///         in control 94%; held out (one chart at a time, the twelve conditions fixed), r ≥ 0.93,
///         error ≤ 3.2, control ≥ 92%. The study's stricter number (conditions re-chosen inside
///         each fold: r 0.91/0.93, 3.5, 91%) cannot be computed from twelve conditions and is quoted,
///         not re-derived.</item>
/// </list>
/// </summary>
public sealed class DbsiStyleGoldenTests
{
    internal static List<Ohlcv> Bars(DbsiDigitisedCharts.Chart c) =>
        c.Bars.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                var f = line.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                return new Ohlcv(DateTimeOffset.FromUnixTimeSeconds((long)f[0]).UtcDateTime, f[1], f[2], f[3], f[4], f[5]);
            }).ToList();

    private static DbsiStyleProvider.Result Run(DbsiDigitisedCharts.Chart c) =>
        DbsiStyleProvider.Compute(Bars(c).ToArray(), new DbsiStyleProvider.Settings(7, 7, 200, c.HeikinAshi));

    public static IEnumerable<object[]> Charts() => DbsiDigitisedCharts.All.Select(c => new object[] { c.Key });

    private static DbsiDigitisedCharts.Chart ChartNamed(string key) => DbsiDigitisedCharts.All.Single(c => c.Key == key);

    [Theory]
    [MemberData(nameof(Charts))]
    public void Votes_and_printed_scores_are_the_study_models_on_every_digitised_bar(string key)
    {
        var c = ChartNamed(key);
        var r = Run(c);
        int n = c.Bull.Length, off = r.Bull.Length - n;
        var diffs = new List<string>();
        for (int k = 0; k < n; k++)
        {
            int i = off + k;
            if (r.BullVotes[i] != c.BullVotes[k] || r.BearVotes[i] != c.BearVotes[k]
                || r.Bull[i] != c.Bull[k] || r.Bear[i] != c.Bear[k])
                diffs.Add($"bar {k}: votes {r.BullVotes[i]}/{r.BearVotes[i]} (study {c.BullVotes[k]}/{c.BearVotes[k]}), " +
                          $"scores {r.Bull[i]}/{r.Bear[i]} (study {c.Bull[k]}/{c.Bear[k]})");
        }
        Assert.True(diffs.Count == 0, $"{key} ({(c.HeikinAshi ? "Heikin-Ashi" : "regular")}): {string.Join("; ", diffs)}");
    }

    /// <summary>
    /// Ordinary least squares with a shared slope on the provider's votes. The −5 floor never binds
    /// (the intercepts are above −5 and votes are never negative), so this IS the study's
    /// constrained fit, and must land on its numbers to the digits the provider carries.
    /// </summary>
    [Fact]
    public void Refitting_the_digitised_numbers_from_the_providers_votes_recovers_the_shipped_constants()
    {
        // Normal equations for rows bull: [1, 0, v], bear: [0, 1, v].
        double[,] ata = new double[3, 3]; double[] atb = new double[3];
        foreach (var c in DbsiDigitisedCharts.All)
        {
            var r = Run(c);
            int off = r.Bull.Length - c.Bull.Length;
            for (int k = 0; k < c.Bull.Length; k++)
            {
                if (c.TrueBull[k] is int tb) Add(new[] { 1.0, 0, r.BullVotes[off + k] }, tb);
                if (c.TrueBear[k] is int tr) Add(new[] { 0, 1.0, r.BearVotes[off + k] }, tr);
            }
        }
        var p = Solve3(ata, atb);
        Assert.Equal(DbsiStyleProvider.BullIntercept, p[0], 6);
        Assert.Equal(DbsiStyleProvider.BearIntercept, p[1], 6);
        Assert.Equal(DbsiStyleProvider.PointsPerVote, p[2], 6);

        void Add(double[] row, double y)
        {
            for (int a = 0; a < 3; a++) { atb[a] += row[a] * y; for (int b = 0; b < 3; b++) ata[a, b] += row[a] * row[b]; }
        }
    }

    [Fact]
    public void In_sample_fidelity_is_what_the_manual_quotes()
    {
        var (pb, pr, tb, tr) = Pooled(c => Run(c));
        double rb = Corr(pb, tb), rr = Corr(pr, tr);
        double rmse = Rmse(pb.Concat(pr), tb.Concat(tr));
        double control = ControlAgreement(pb, pr, tb, tr);
        var report = $"r bull {rb:F3}, bear {rr:F3}, RMSE {rmse:F3}, control {control:P1}";
        // The study's numbers for this exact model (fidelity.py, integers, 143 bars pooled):
        // r 0.923 / 0.932, RMSE 3.098, control 94.3%.
        Assert.True(Math.Abs(rb - 0.923) < 0.002 && Math.Abs(rr - 0.932) < 0.002, report);
        Assert.True(Math.Abs(rmse - 3.098) < 0.01, report);
        Assert.True(Math.Abs(control - 0.943) < 0.008, report);
    }

    /// <summary>
    /// Leave one chart out: refit the three constants on the other five charts' votes, score the
    /// sixth, average over the six. With the conditions fixed this is optimistic next to the
    /// study's nested figure (they were chosen on all six), which is why the asserted floors sit
    /// between the two: r ≥ 0.93, RMSE ≤ 3.2, control ≥ 92% (computed 0.936/0.939, 3.14, 93.0%).
    /// </summary>
    [Fact]
    public void Held_out_one_chart_at_a_time_it_keeps_the_studys_fidelity()
    {
        var all = DbsiDigitisedCharts.All;
        var runs = all.ToDictionary(c => c.Key, c => Run(c));
        double rbSum = 0, rrSum = 0, rmseSum = 0, ctlSum = 0;
        foreach (var held in all)
        {
            double[,] ata = new double[3, 3]; double[] atb = new double[3];
            foreach (var c in all.Where(c => c != held))
            {
                var r = runs[c.Key]; int off = r.Bull.Length - c.Bull.Length;
                for (int k = 0; k < c.Bull.Length; k++)
                {
                    if (c.TrueBull[k] is int tb) Acc(ata, atb, new[] { 1.0, 0, r.BullVotes[off + k] }, tb);
                    if (c.TrueBear[k] is int tr) Acc(ata, atb, new[] { 0, 1.0, r.BearVotes[off + k] }, tr);
                }
            }
            var p = Solve3(ata, atb);
            var rh = runs[held.Key]; int o = rh.Bull.Length - held.Bull.Length;
            var pb = new List<double>(); var pr = new List<double>(); var tbs = new List<double>(); var trs = new List<double>();
            for (int k = 0; k < held.Bull.Length; k++)
            {
                pb.Add(Math.Round(Math.Max(-5, p[0] + p[2] * rh.BullVotes[o + k]), MidpointRounding.ToEven));
                pr.Add(Math.Round(Math.Max(-5, p[1] + p[2] * rh.BearVotes[o + k]), MidpointRounding.ToEven));
                tbs.Add(held.TrueBull[k] ?? double.NaN); trs.Add(held.TrueBear[k] ?? double.NaN);
            }
            rbSum += Corr(pb, tbs); rrSum += Corr(pr, trs);
            rmseSum += Rmse(pb.Concat(pr), tbs.Concat(trs));
            ctlSum += ControlAgreement(pb, pr, tbs, trs);
        }
        double rb = rbSum / all.Length, rr = rrSum / all.Length, rmse = rmseSum / all.Length, ctl = ctlSum / all.Length;
        var report = $"held out: r bull {rb:F3}, bear {rr:F3}, RMSE {rmse:F3}, control {ctl:P1}";
        Assert.True(rb >= 0.93 && rr >= 0.93, report);
        Assert.True(rmse <= 3.2, report);
        Assert.True(ctl >= 0.92, report);
    }

    /// <summary>
    /// The shortfall the manual states, held true so the manual cannot drift from it: twelve
    /// votes top out at 16 for bulls and 15 for bears, and bulls' floor prints −4, not −5.
    /// </summary>
    [Fact]
    public void The_scale_tops_out_at_16_and_15_and_bulls_floor_is_minus_4()
    {
        Assert.Equal(16, DbsiStyleProvider.Score(DbsiStyleProvider.BullIntercept, 12));
        Assert.Equal(15, DbsiStyleProvider.Score(DbsiStyleProvider.BearIntercept, 12));
        Assert.Equal(-4, DbsiStyleProvider.Score(DbsiStyleProvider.BullIntercept, 0));
        Assert.Equal(-5, DbsiStyleProvider.Score(DbsiStyleProvider.BearIntercept, 0));
    }

    // ── helpers ────────────────────────────────────────────────────────────────────

    private static (List<double> pb, List<double> pr, List<double> tb, List<double> tr) Pooled(
        Func<DbsiDigitisedCharts.Chart, DbsiStyleProvider.Result> run)
    {
        var pb = new List<double>(); var pr = new List<double>(); var tb = new List<double>(); var tr = new List<double>();
        foreach (var c in DbsiDigitisedCharts.All)
        {
            var r = run(c); int off = r.Bull.Length - c.Bull.Length;
            for (int k = 0; k < c.Bull.Length; k++)
            {
                pb.Add(r.Bull[off + k]); pr.Add(r.Bear[off + k]);
                tb.Add(c.TrueBull[k] ?? double.NaN); tr.Add(c.TrueBear[k] ?? double.NaN);
            }
        }
        return (pb, pr, tb, tr);
    }

    private static void Acc(double[,] ata, double[] atb, double[] row, double y)
    {
        for (int a = 0; a < 3; a++) { atb[a] += row[a] * y; for (int b = 0; b < 3; b++) ata[a, b] += row[a] * row[b]; }
    }

    private static double[] Solve3(double[,] m, double[] v)
    {
        var a = (double[,])m.Clone(); var b = (double[])v.Clone();
        for (int col = 0; col < 3; col++)
        {
            int piv = Enumerable.Range(col, 3 - col).OrderByDescending(r => Math.Abs(a[r, col])).First();
            for (int k = 0; k < 3; k++) (a[col, k], a[piv, k]) = (a[piv, k], a[col, k]);
            (b[col], b[piv]) = (b[piv], b[col]);
            for (int r = 0; r < 3; r++)
            {
                if (r == col) continue;
                double f = a[r, col] / a[col, col];
                for (int k = 0; k < 3; k++) a[r, k] -= f * a[col, k];
                b[r] -= f * b[col];
            }
        }
        return new[] { b[0] / a[0, 0], b[1] / a[1, 1], b[2] / a[2, 2] };
    }

    private static double Corr(IList<double> x, IList<double> y)
    {
        var pairs = x.Zip(y).Where(p => !double.IsNaN(p.First) && !double.IsNaN(p.Second)).ToList();
        double mx = pairs.Average(p => p.First), my = pairs.Average(p => p.Second);
        double sxy = pairs.Sum(p => (p.First - mx) * (p.Second - my));
        double sxx = pairs.Sum(p => (p.First - mx) * (p.First - mx)), syy = pairs.Sum(p => (p.Second - my) * (p.Second - my));
        return sxy / Math.Sqrt(sxx * syy);
    }

    private static double Rmse(IEnumerable<double> p, IEnumerable<double> t)
    {
        var e = p.Zip(t).Where(z => !double.IsNaN(z.Second)).Select(z => z.First - z.Second).ToList();
        return Math.Sqrt(e.Average(x => x * x));
    }

    /// <summary>Share of bars, with both numbers legible and not tied in DBSI, whose leader matches.</summary>
    private static double ControlAgreement(IList<double> pb, IList<double> pr, IList<double> tb, IList<double> tr)
    {
        int ok = 0, n = 0;
        for (int i = 0; i < pb.Count; i++)
        {
            if (double.IsNaN(tb[i]) || double.IsNaN(tr[i]) || tb[i] == tr[i]) continue;
            n++;
            if (Math.Sign(pb[i] - pr[i]) == Math.Sign(tb[i] - tr[i])) ok++;
        }
        return (double)ok / n;
    }
}
