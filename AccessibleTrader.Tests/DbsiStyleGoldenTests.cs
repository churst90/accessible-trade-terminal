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
/// labels) are in <see cref="DbsiDigitisedCharts"/>, generated from the study's files, with fifteen
/// later livestream charts no fit saw (<c>Fitted</c> false). Five claims, each against the study
/// rather than against this code:
/// </para>
/// <list type="number">
///   <item>The provider's twelve votes and the integers it prints are the study model's on every
///         labelled bar — the port is exact, not "close".</item>
///   <item>Refitting the digitised numbers from the provider's own terms (its votes, streaks,
///         stretch and WaveTrend run) recovers the shipped constants — so the constants are the fit of these terms,
///         not numbers copied beside different ones.</item>
///   <item>The fidelity the manual quotes holds, in sample and held out one chart at a time.</item>
///   <item>Held out, it beats the twelve equal votes it replaced, refitted on the same folds, and the
///         replaced model's shipped numbers on the fourteen charts those were never fitted to.</item>
///   <item>On the fifteen livestream charts, the shipped constants beat the same model fitted on the
///         twenty without the seven base votes.</item>
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

    /// <summary>The twenty charts the constants were fitted on.</summary>
    private static DbsiDigitisedCharts.Chart[] Fitted => DbsiDigitisedCharts.All.Where(c => c.Fitted).ToArray();

    /// <summary>The fifteen livestream charts (2026-10-23) no fit saw.</summary>
    private static DbsiDigitisedCharts.Chart[] Livestream => DbsiDigitisedCharts.All.Where(c => !c.Fitted).ToArray();

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
    /// settle) on the provider's own terms, on the twenty fitted charts. The three copies of the
    /// 2-bar momentum (two votes and the first base vote) are one column here, and so are the two
    /// of wt1 rising (a vote and the sixth base vote), so it recovers their sums; the study split
    /// each evenly between its copies.
    /// </summary>
    [Fact]
    public void Refitting_the_digitised_numbers_from_the_providers_terms_recovers_the_shipped_constants()
    {
        var p = Fit(Fitted.SelectMany(c => NewRows(c)).ToList());
        var w = DbsiStyleProvider.VoteWeights; var b = DbsiStyleProvider.BaseWeights;
        var shipped = new List<double> { DbsiStyleProvider.BullIntercept, DbsiStyleProvider.BearIntercept, w[0] + w[8] + b[0] };
        shipped.AddRange(Enumerable.Range(1, 11).Where(j => j != 8).Select(j => j == 5 ? w[5] + b[5] : w[j]));
        shipped.Add(DbsiStyleProvider.StreakWeight);
        shipped.Add(DbsiStyleProvider.StretchWeight);
        shipped.AddRange(DbsiStyleProvider.RunWeights);
        shipped.AddRange(new[] { b[1], b[2], b[3], b[4], b[6] });
        Assert.Equal(shipped.Count, p.Length);
        for (int j = 0; j < p.Length; j++)
            Assert.True(Math.Abs(p[j] - shipped[j]) < 1e-6, $"constant {j}: refit {p[j]:R}, shipped {shipped[j]:R}");
        Assert.Equal(w[0], w[8], 9);
        Assert.Equal(w[0], b[0], 9);
        Assert.Equal(w[5], b[5], 9);
    }

    [Fact]
    public void In_sample_fidelity_is_what_the_manual_quotes()
    {
        var (pred, truth) = Pooled(Fitted, c =>
        {
            var r = Run(c); int off = r.Bull.Length - c.Bull.Length;
            return (r.Bull.Skip(off).ToArray(), r.Bear.Skip(off).ToArray());
        });
        var f = Fidelity(pred, truth);
        _out.WriteLine($"in sample: {f}");
        // gen3.py's numbers for this exact model on all twenty charts.
        Assert.Equal(897, f.N);
        Assert.True(Math.Abs(f.Exact - 0.237) < 0.002 && Math.Abs(f.Within1 - 0.541) < 0.002
                    && Math.Abs(f.Within2 - 0.739) < 0.002, f.ToString());
        Assert.True(Math.Abs(f.Rmse - 2.41) < 0.01, f.ToString());
        Assert.Equal(84, f.High);
        Assert.Equal(47, f.HighPrinted);
        Assert.Equal(13, f.FalseHigh);   // the cost the manual states

        // The side in control: on the bars where DBSI's two numbers differ, the larger of ours is DBSI's larger.
        int differ = 0, same = 0;
        for (int j = 0; j + 1 < truth.Count; j += 2)
        {
            if (double.IsNaN(truth[j]) || double.IsNaN(truth[j + 1]) || truth[j] == truth[j + 1]) continue;
            differ++;
            if (pred[j] != pred[j + 1] && pred[j] > pred[j + 1] == truth[j] > truth[j + 1]) same++;
        }
        _out.WriteLine($"side in control: {same} of {differ}");
        Assert.Equal((422, 397), (differ, same));   // 94%, 395 before the base votes
    }

    /// <summary>
    /// Leave one chart out: refit on nineteen charts, score the twentieth, pool. The twelve
    /// conditions were picked on the first six charts, so on those six this is optimistic; on the
    /// other fourteen it is not. gen3.py: exact 24.1%, ±1 52.0%, ±2 71.9%, error 2.52 (without the
    /// base votes 13.0%, 44.0%, 67.1%, 2.82).
    /// </summary>
    [Fact]
    public void Held_out_one_chart_at_a_time_it_keeps_its_fidelity_and_beats_the_model_it_replaced()
    {
        var all = Fitted;
        var newRows = all.ToDictionary(c => c.Key, c => NewRows(c).ToList());
        var oldRows = all.ToDictionary(c => c.Key, c => OldRows(c).ToList());
        var fresh = Loco(all, newRows);
        var equal = Loco(all, oldRows);
        var f = Fidelity(fresh.SelectMany(x => x.Value.pred).ToList(), fresh.SelectMany(x => x.Value.truth).ToList());
        var e = Fidelity(equal.SelectMany(x => x.Value.pred).ToList(), equal.SelectMany(x => x.Value.truth).ToList());
        _out.WriteLine($"held out: {f}");
        _out.WriteLine($"twelve equal votes, refitted the same way: {e}");
        Assert.True(f.Exact >= 0.235 && f.Within1 >= 0.512 && f.Within2 >= 0.715 && f.Rmse <= 2.57, f.ToString());
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
    /// The fifteen livestream charts (2026-10-23: Coinbase and Bitstamp Bitcoin and Ethereum, 1 minute
    /// to 1 day) were never fitted, and the seven base votes were chosen on a Dow grid, not on them.
    /// The shipped constants score them once; the model without the base votes, fitted the same way on
    /// the same twenty, is the bar to beat. gen3.py: 17.4%, 48.5%, 65.3%, 3.08, 17+ 27 of 48 printed
    /// and 8 false; without 14.0%, 40.6%, 61.0%, 3.30, 28 of 48 and 18 false.
    /// </summary>
    [Fact]
    public void On_fifteen_charts_no_fit_saw_the_base_votes_beat_the_model_without_them()
    {
        var (pred, truth) = Pooled(Livestream, c =>
        {
            var r = Run(c); int off = r.Bull.Length - c.Bull.Length;
            return (r.Bull.Skip(off).ToArray(), r.Bear.Skip(off).ToArray());
        });
        var f = Fidelity(pred, truth);
        var p = Fit(Fitted.SelectMany(c => NewRows(c, withBase: false)).ToList());
        var rows = Livestream.SelectMany(c => NewRows(c, withBase: false)).ToList();
        var e = Fidelity(rows.Select(r => Predict(p, r.x)).ToList(), rows.Select(r => r.y).ToList());
        _out.WriteLine($"livestream, shipped: {f}");
        _out.WriteLine($"livestream, without the base votes: {e}");
        Assert.Equal(e.N, f.N);
        Assert.True(Math.Abs(f.Exact - 0.174) < 0.003 && Math.Abs(f.Within1 - 0.485) < 0.003
                    && Math.Abs(f.Within2 - 0.653) < 0.003 && Math.Abs(f.Rmse - 3.08) < 0.01, f.ToString());
        Assert.Equal((27, 8), (f.HighPrinted, f.FalseHigh));
        Assert.True(f.Rmse <= e.Rmse - 0.2 && f.Within2 >= e.Within2 + 0.04 && f.FalseHigh * 2 <= e.FalseHigh,
            $"shipped {f}; without the base votes {e}");
    }

    /// <summary>
    /// The scale the manual states, held true so the manual cannot drift from it: with no votes
    /// and no run both sides sit at the −5 floor, and a long streak keeps them there; a deep cross
    /// whose gap has widened for ten bars, with every vote and base vote on the side and %K at the
    /// far end, prints 34 for the bulls and 33 for the bears — the original's own numbers on these
    /// charts reach 27, and the model's reach 26 where the votes and the run line up.
    /// </summary>
    [Fact]
    public void The_floor_is_minus_5_and_the_run_carries_the_high_scores()
    {
        var run = DbsiStyleProvider.RunWeights;
        double full = DbsiStyleProvider.VoteWeights.Sum() + DbsiStyleProvider.BaseWeights.Sum() + DbsiStyleProvider.StreakWeight
                      + 5 * DbsiStyleProvider.StretchWeight + run[1] + run[3] + DbsiStyleProvider.RunCountCap * run[4];
        Assert.Equal(34, DbsiStyleProvider.Score(DbsiStyleProvider.BullIntercept, full));
        Assert.Equal(33, DbsiStyleProvider.Score(DbsiStyleProvider.BearIntercept, full));
        double streak = DbsiStyleProvider.StreakCap * DbsiStyleProvider.StreakWeight;
        Assert.Equal(-5, DbsiStyleProvider.Score(DbsiStyleProvider.BullIntercept, 0));
        Assert.Equal(-5, DbsiStyleProvider.Score(DbsiStyleProvider.BearIntercept, 0));
        Assert.Equal(-5, DbsiStyleProvider.Score(DbsiStyleProvider.BullIntercept, streak));
        Assert.Equal(-5, DbsiStyleProvider.Score(DbsiStyleProvider.BearIntercept, streak));
        Assert.True(DbsiStyleProvider.StreakWeight < 0, "a long streak counts against the score");
        Assert.True(DbsiStyleProvider.BaseWeights.All(x => x > 0), "every base vote adds to its side");
        // The twelve votes' points shrank once the base votes took their job; one, close above the
        // 2-bar midpoint, now takes a fifth of a point away.
        Assert.True(DbsiStyleProvider.VoteWeights.All(x => x > -0.2), "no vote takes much from its side");
        Assert.True(run[1] > 1 && run[3] > 1 && run[4] > 1, "a deep cross and a widening gap add to the side");
        int top = DbsiDigitisedCharts.All.Max(c => Math.Max(c.Bull.Max(), c.Bear.Max()));
        int truthTop = DbsiDigitisedCharts.All.Max(c => Math.Max(c.TrueBull.Max() ?? -5, c.TrueBear.Max() ?? -5));
        Assert.Equal((27, 26), (truthTop, top));
    }

    /// <summary>
    /// The run on a hand-made WaveTrend: the bull run starts where wt1 crosses above its average
    /// (bar 2, wt1 −65, so a deep cross), stays alive while the gap widens (5 → 7), dies when it
    /// narrows (7 → 4) and stays dead to the end of the run, keeping only its deep flag. The bear run
    /// already going on the first bar is not counted; the next one starts on bar 6 (wt1 −49: neither
    /// deep nor over for bears) and widens on bar 7.
    /// </summary>
    [Fact]
    public void The_WaveTrend_run_starts_on_the_cross_and_dies_the_first_time_the_gap_narrows()
    {
        double[] wt1 = { -70, -80, -65, -55, -50, -48, -49, -60 };
        double[] wt2 = { -60, -70, -70, -62, -54, -50, -47, -50 };
        var bull = DbsiStyleProvider.WaveTrendRun(wt1, wt2, 1);
        Assert.Equal(new double[] { 0, 0, 1, 0, 0, 0, 0, 0 }, bull.Cross);
        Assert.Equal(new double[] { 0, 0, 1, 1, 1, 1, 0, 0 }, bull.Deep);
        Assert.Equal(new double[] { 0, 0, 0, 0, 0, 0, 0, 0 }, bull.Over);
        Assert.Equal(new double[] { 0, 0, 1, 1, 0, 0, 0, 0 }, bull.Alive);
        Assert.Equal(new double[] { 0, 0, 0, 1, 0, 0, 0, 0 }, bull.Count);
        var bear = DbsiStyleProvider.WaveTrendRun(wt1, wt2, -1);
        Assert.Equal(new double[] { 0, 0, 0, 0, 0, 0, 1, 0 }, bear.Cross);
        Assert.Equal(new double[] { 0, 0, 0, 0, 0, 0, 1, 1 }, bear.Alive);
        Assert.Equal(new double[] { 0, 0, 0, 0, 0, 0, 0, 1 }, bear.Count);
        Assert.All(new[] { bear.Deep, bear.Over }, a => Assert.All(a, v => Assert.Equal(0, v)));

        // A bear cross from wt1 +67 is deep for bears; a bull cross from wt1 +64 is over-stretched.
        // Both widen for twelve bars, so the count reaches its ten-bar cap.
        var down = Enumerable.Range(0, 14).Select(i => 70.0 - 3 * i * i).ToArray();
        var r = DbsiStyleProvider.WaveTrendRun(down, down.Select((v, i) => i == 0 ? v - 1 : down[i - 1]).ToArray(), -1);
        Assert.Equal(1, r.Cross[1]); Assert.Equal(1, r.Deep[13]); Assert.Equal(0, r.Over[13]);
        Assert.Equal(9, r.Count[10]); Assert.Equal(DbsiStyleProvider.RunCountCap, r.Count[11]); Assert.Equal(DbsiStyleProvider.RunCountCap, r.Count[13]);
        var up = Enumerable.Range(0, 14).Select(i => 61.0 + 3 * i * i).ToArray();
        r = DbsiStyleProvider.WaveTrendRun(up, up.Select((v, i) => i == 0 ? v + 1 : up[i - 1]).ToArray(), 1);
        Assert.Equal(1, r.Cross[1]); Assert.Equal(1, r.Over[13]); Assert.Equal(0, r.Deep[13]); Assert.Equal(1, r.Alive[13]);
    }

    // ── rows: (features, truth) per labelled side ──────────────────────────────────

    /// <summary>The refit's columns: bull, bear, the 2-bar momentum (all three copies), the other
    /// ten votes, the streak (capped), the stretch, the five WaveTrend run terms, the five base votes
    /// that are not copies of a vote (all unless <paramref name="withBase"/> is false).</summary>
    private static IEnumerable<(double[] x, double y)> NewRows(DbsiDigitisedCharts.Chart c, bool withBase = true)
    {
        var t = DbsiStyleProvider.ScoringTerms(Bars(c).ToArray(), c.HeikinAshi);
        int n = c.Bull.Length, off = t.Close.Length - n;
        for (int k = 0; k < n; k++)
        {
            int i = off + k;
            double kk = double.IsNaN(t.K14[i]) ? 50 : t.K14[i];
            yield return (Row(true, Math.Min(t.BullRun[i], DbsiStyleProvider.StreakCap), Math.Max(0, (50 - kk) / 10), t.BullWave), c.TrueBull[k] ?? double.NaN);
            yield return (Row(false, Math.Min(t.BearRun[i], DbsiStyleProvider.StreakCap), Math.Max(0, (kk - 50) / 10), t.BearWave), c.TrueBear[k] ?? double.NaN);

            double[] Row(bool bull, double streak, double stretch, DbsiStyleProvider.WaveRun w)
            {
                var x = new List<double> { bull ? 1 : 0, bull ? 0 : 1 };
                foreach (int j in Enumerable.Range(0, 12).Where(j => j != 8))
                    x.Add(bull ? (t.Quantities[j][i] > 0 ? 1 : 0) : (t.Quantities[j][i] < 0 ? 1 : 0));
                x.Add(streak); x.Add(stretch);
                x.Add(w.Cross[i]); x.Add(w.Deep[i]); x.Add(w.Over[i]); x.Add(w.Alive[i]); x.Add(w.Count[i]);
                if (withBase)
                    foreach (int j in new[] { 1, 2, 3, 4, 6 }) x.Add((bull ? t.BullBase : t.BearBase)[j][i]);
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
