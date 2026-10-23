using System.Globalization;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fixtures;
using Xunit.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The DBSI-style provider replays the refit, bar for bar.</b>
///
/// <para>
/// The 897 digitised Market Cipher DBSI numbers (twenty charts, each with the 100 bars before its
/// labels) are in <see cref="DbsiDigitisedCharts"/>, generated from the study's files. Four claims,
/// each against the study rather than against this code:
/// </para>
/// <list type="number">
///   <item>The provider's twelve votes and the integers it prints are the study model's on every
///         labelled bar — the port is exact, not "close".</item>
///   <item>Refitting the digitised numbers from the provider's own terms (its votes, streaks and
///         stretch) recovers the shipped constants — so the constants are the fit of these terms,
///         not numbers copied beside different ones.</item>
///   <item>The fidelity the manual quotes holds, in sample and held out one chart at a time.</item>
///   <item>Held out, it beats the twelve equal votes it replaced, refitted on the same folds, and the
///         replaced model's shipped numbers on the fourteen charts those were never fitted to.</item>
/// </list>
/// </summary>
public sealed class DbsiStyleGoldenTests
{
    private readonly ITestOutputHelper _out;
    public DbsiStyleGoldenTests(ITestOutputHelper output) => _out = output;

    /// <summary>The six charts the twelve conditions (and the replaced model) were fitted on.</summary>
    private static readonly string[] FirstSix = { "G1D", "G1W", "G3D", "G2D", "V15", "Q30" };

    /// <summary>The replaced model, as it shipped: round(max(−5, a + 1.668 × votes)).</summary>
    private const double OldBull = -4.3320339, OldBear = -4.79048715, OldPerVote = 1.66805928;

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
    /// The study's fit (least squares, refitted on the rows the −5 floor does not bind until they
    /// settle) on the provider's own terms. The two copies of the 2-bar momentum are one column
    /// here, so it recovers their sum; the study split it evenly between them.
    /// </summary>
    [Fact]
    public void Refitting_the_digitised_numbers_from_the_providers_terms_recovers_the_shipped_constants()
    {
        var p = Fit(DbsiDigitisedCharts.All.SelectMany(NewRows).ToList());
        var w = DbsiStyleProvider.VoteWeights;
        var shipped = new List<double> { DbsiStyleProvider.BullIntercept, DbsiStyleProvider.BearIntercept, w[0] + w[8] };
        shipped.AddRange(Enumerable.Range(1, 11).Where(j => j != 8).Select(j => w[j]));
        shipped.Add(DbsiStyleProvider.StreakWeight);
        shipped.Add(DbsiStyleProvider.StretchWeight);
        Assert.Equal(shipped.Count, p.Length);
        for (int j = 0; j < p.Length; j++)
            Assert.True(Math.Abs(p[j] - shipped[j]) < 1e-6, $"constant {j}: refit {p[j]:R}, shipped {shipped[j]:R}");
        Assert.Equal(w[0], w[8], 9);
    }

    [Fact]
    public void In_sample_fidelity_is_what_the_manual_quotes()
    {
        var (pred, truth) = Pooled(DbsiDigitisedCharts.All, c =>
        {
            var r = Run(c); int off = r.Bull.Length - c.Bull.Length;
            return (r.Bull.Skip(off).ToArray(), r.Bear.Skip(off).ToArray());
        });
        var f = Fidelity(pred, truth);
        _out.WriteLine($"in sample: {f}");
        // gen.py's numbers for this exact model on all twenty charts.
        Assert.Equal(897, f.N);
        Assert.True(Math.Abs(f.Exact - 0.167) < 0.002 && Math.Abs(f.Within1 - 0.427) < 0.002
                    && Math.Abs(f.Within2 - 0.608) < 0.002, f.ToString());
        Assert.True(Math.Abs(f.Rmse - 3.30) < 0.01, f.ToString());
        Assert.Equal(84, f.High);
        Assert.True(f.HighPrinted <= 6, f.ToString());   // the shortfall the manual states
    }

    /// <summary>
    /// Leave one chart out: refit on nineteen charts, score the twentieth, pool. The twelve
    /// conditions were picked on the first six charts, so on those six this is optimistic; on the
    /// other fourteen it is not. gen.py: exact 16.7%, ±1 42.4%, ±2 60.1%, error 3.40.
    /// </summary>
    [Fact]
    public void Held_out_one_chart_at_a_time_it_keeps_its_fidelity_and_beats_the_model_it_replaced()
    {
        var all = DbsiDigitisedCharts.All;
        var newRows = all.ToDictionary(c => c.Key, c => NewRows(c).ToList());
        var oldRows = all.ToDictionary(c => c.Key, c => OldRows(c).ToList());
        var fresh = Loco(all, newRows);
        var equal = Loco(all, oldRows);
        var f = Fidelity(fresh.SelectMany(x => x.Value.pred).ToList(), fresh.SelectMany(x => x.Value.truth).ToList());
        var e = Fidelity(equal.SelectMany(x => x.Value.pred).ToList(), equal.SelectMany(x => x.Value.truth).ToList());
        _out.WriteLine($"held out: {f}");
        _out.WriteLine($"twelve equal votes, refitted the same way: {e}");
        Assert.True(f.Exact >= 0.16 && f.Within1 >= 0.415 && f.Within2 >= 0.595 && f.Rmse <= 3.45, f.ToString());
        Assert.True(f.Rmse <= e.Rmse - 0.2 && f.Within1 >= e.Within1 + 0.05, $"new {f}; equal votes {e}");

        // The replaced model's shipped numbers never saw these fourteen charts.
        int better = 0; var lines = new List<string>();
        foreach (var c in all.Where(c => !FirstSix.Contains(c.Key)))
        {
            var r = Run(c); int off = r.Bull.Length - c.Bull.Length;
            var old = new List<double>(); var truth = new List<double>();
            for (int k = 0; k < c.Bull.Length; k++)
            {
                old.Add(Math.Round(Math.Max(-5, OldBull + OldPerVote * r.BullVotes[off + k]), MidpointRounding.ToEven));
                old.Add(Math.Round(Math.Max(-5, OldBear + OldPerVote * r.BearVotes[off + k]), MidpointRounding.ToEven));
                truth.Add(c.TrueBull[k] ?? double.NaN); truth.Add(c.TrueBear[k] ?? double.NaN);
            }
            double rn = Fidelity(fresh[c.Key].pred, fresh[c.Key].truth).Rmse, ro = Fidelity(old, truth).Rmse;
            if (rn < ro) better++;
            lines.Add($"{c.Key} {rn:F2} vs {ro:F2}");
        }
        _out.WriteLine("error, held out vs the replaced model as shipped: " + string.Join(", ", lines));
        Assert.True(better >= 13, $"closer on only {better} of 14 charts: {string.Join(", ", lines)}");
    }

    /// <summary>
    /// The scale the manual states, held true so the manual cannot drift from it: the most either
    /// side can print (every vote, the shortest streak a full house allows, %K at the far end) is
    /// 19, and with no votes both sides print the −5 floor.
    /// </summary>
    [Fact]
    public void The_scale_tops_out_at_19_and_both_floors_are_minus_5()
    {
        double full = DbsiStyleProvider.VoteWeights.Sum() + DbsiStyleProvider.StreakWeight + 5 * DbsiStyleProvider.StretchWeight;
        Assert.Equal(19, DbsiStyleProvider.Score(DbsiStyleProvider.BullIntercept, full));
        Assert.Equal(19, DbsiStyleProvider.Score(DbsiStyleProvider.BearIntercept, full));
        Assert.Equal(-5, DbsiStyleProvider.Score(DbsiStyleProvider.BullIntercept, 0));
        Assert.Equal(-5, DbsiStyleProvider.Score(DbsiStyleProvider.BearIntercept, 0));
        Assert.True(DbsiStyleProvider.StreakWeight < 0, "a long streak counts against the score");
        Assert.True(DbsiStyleProvider.VoteWeights.All(x => x > 0), "every vote adds to its side");
    }

    // ── rows: (features, truth) per labelled side ──────────────────────────────────

    /// <summary>The refit's columns: bull, bear, the 2-bar momentum (both copies), the other
    /// ten votes, the streak (capped), the stretch.</summary>
    private static IEnumerable<(double[] x, double y)> NewRows(DbsiDigitisedCharts.Chart c)
    {
        var t = DbsiStyleProvider.ScoringTerms(Bars(c).ToArray(), c.HeikinAshi);
        int n = c.Bull.Length, off = t.Close.Length - n;
        for (int k = 0; k < n; k++)
        {
            int i = off + k;
            double kk = double.IsNaN(t.K14[i]) ? 50 : t.K14[i];
            yield return (Row(true, Math.Min(t.BullRun[i], DbsiStyleProvider.StreakCap), Math.Max(0, (50 - kk) / 10)), c.TrueBull[k] ?? double.NaN);
            yield return (Row(false, Math.Min(t.BearRun[i], DbsiStyleProvider.StreakCap), Math.Max(0, (kk - 50) / 10)), c.TrueBear[k] ?? double.NaN);

            double[] Row(bool bull, double streak, double stretch)
            {
                var x = new List<double> { bull ? 1 : 0, bull ? 0 : 1 };
                foreach (int j in Enumerable.Range(0, 12).Where(j => j != 8))
                    x.Add(bull ? (t.Quantities[j][i] > 0 ? 1 : 0) : (t.Quantities[j][i] < 0 ? 1 : 0));
                x.Add(streak); x.Add(stretch);
                return x.ToArray();
            }
        }
    }

    /// <summary>The replaced model's columns: bull, bear, the vote count.</summary>
    private static IEnumerable<(double[] x, double y)> OldRows(DbsiDigitisedCharts.Chart c)
    {
        var r = Run(c);
        int n = c.Bull.Length, off = r.Bull.Length - n;
        for (int k = 0; k < n; k++)
        {
            yield return (new double[] { 1, 0, r.BullVotes[off + k] }, c.TrueBull[k] ?? double.NaN);
            yield return (new double[] { 0, 1, r.BearVotes[off + k] }, c.TrueBear[k] ?? double.NaN);
        }
    }

    private static Dictionary<string, (List<double> pred, List<double> truth)> Loco(
        DbsiDigitisedCharts.Chart[] all, Dictionary<string, List<(double[] x, double y)>> rows)
    {
        var res = new Dictionary<string, (List<double>, List<double>)>();
        foreach (var held in all)
        {
            var p = Fit(all.Where(c => c != held).SelectMany(c => rows[c.Key]).ToList());
            var hr = rows[held.Key];
            res[held.Key] = (hr.Select(r => Predict(p, r.x)).ToList(), hr.Select(r => r.y).ToList());
        }
        return res;
    }

    private static double Predict(double[] p, double[] x) =>
        Math.Round(Math.Max(-5, p.Zip(x).Sum(z => z.First * z.Second)), MidpointRounding.ToEven);

    /// <summary>The study's hinge-aware least squares (fitlib.fit): fit the rows above the floor,
    /// then the rows above it in truth or prediction, eight times.</summary>
    private static double[] Fit(List<(double[] x, double y)> rows)
    {
        rows = rows.Where(r => !double.IsNaN(r.y)).ToList();
        var use = rows.Select(r => r.y > -5).ToArray();
        double[] p = Array.Empty<double>();
        for (int it = 0; it < 8; it++)
        {
            int m = rows[0].x.Length;
            var ata = new double[m, m]; var atb = new double[m];
            for (int r = 0; r < rows.Count; r++)
            {
                if (!use[r]) continue;
                var x = rows[r].x;
                for (int a = 0; a < m; a++) { atb[a] += x[a] * rows[r].y; for (int b = 0; b < m; b++) ata[a, b] += x[a] * x[b]; }
            }
            p = Solve(ata, atb);
            for (int r = 0; r < rows.Count; r++) use[r] = rows[r].y > -5 || p.Zip(rows[r].x).Sum(z => z.First * z.Second) > -5;
        }
        return p;
    }

    private static double[] Solve(double[,] m, double[] v)
    {
        int n = v.Length;
        var a = (double[,])m.Clone(); var b = (double[])v.Clone();
        for (int col = 0; col < n; col++)
        {
            int piv = Enumerable.Range(col, n - col).OrderByDescending(r => Math.Abs(a[r, col])).First();
            for (int k = 0; k < n; k++) (a[col, k], a[piv, k]) = (a[piv, k], a[col, k]);
            (b[col], b[piv]) = (b[piv], b[col]);
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double f = a[r, col] / a[col, col];
                for (int k = 0; k < n; k++) a[r, k] -= f * a[col, k];
                b[r] -= f * b[col];
            }
        }
        return Enumerable.Range(0, n).Select(i => b[i] / a[i, i]).ToArray();
    }

    // ── scoring ────────────────────────────────────────────────────────────────────

    private static (List<double> pred, List<double> truth) Pooled(
        IEnumerable<DbsiDigitisedCharts.Chart> charts, Func<DbsiDigitisedCharts.Chart, (double[] bull, double[] bear)> run)
    {
        var pred = new List<double>(); var truth = new List<double>();
        foreach (var c in charts)
        {
            var (b, r) = run(c);
            for (int k = 0; k < c.Bull.Length; k++)
            {
                pred.Add(b[k]); truth.Add(c.TrueBull[k] ?? double.NaN);
                pred.Add(r[k]); truth.Add(c.TrueBear[k] ?? double.NaN);
            }
        }
        return (pred, truth);
    }

    private sealed record Closeness(int N, double Exact, double Within1, double Within2, double Rmse, int High, int HighPrinted, int FalseHigh)
    {
        public override string ToString() =>
            $"{N} values: exact {Exact:P1}, ±1 {Within1:P1}, ±2 {Within2:P1}, error {Rmse:F2}; " +
            $"17+: {HighPrinted} of {High} printed, {FalseHigh} false";
    }

    private static Closeness Fidelity(IList<double> pred, IList<double> truth)
    {
        var e = pred.Zip(truth).Where(z => !double.IsNaN(z.Second)).ToList();
        double Share(Func<double, bool> ok) => (double)e.Count(z => ok(Math.Abs(z.First - z.Second))) / e.Count;
        return new Closeness(e.Count, Share(d => d == 0), Share(d => d <= 1), Share(d => d <= 2),
            Math.Sqrt(e.Average(z => (z.First - z.Second) * (z.First - z.Second))),
            e.Count(z => z.Second >= 17), e.Count(z => z.Second >= 17 && z.First >= 17), e.Count(z => z.Second < 17 && z.First >= 17));
    }
}
