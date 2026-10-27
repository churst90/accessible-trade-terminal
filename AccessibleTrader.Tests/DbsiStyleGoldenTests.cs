using System.Globalization;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fixtures;
using Xunit.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The DBSI-style provider replays the study's clean model, bar for bar.</b>
///
/// <para>
/// The 897 digitised Market Cipher DBSI numbers (twenty charts, each with the 100 bars before its
/// labels) are in <see cref="DbsiDigitisedCharts"/>, generated from the study's files, with fifteen
/// later livestream charts (<c>Fitted</c> false) and, for comparison, the numbers the 2026-10-25
/// integer model (<c>Previous</c>, <c>dbsi8/gen8.py</c>) and the replaced linear model printed on every
/// chart. The clean model (<c>dbsi8/clean_dbsi.py</c>) was built on hourly and daily sets of DBSI's own
/// numbers that are not in this repository; the twenty and the fifteen were among the nine main sets
/// every rule had to lose nothing on, so they are not held out.
/// Three claims, each against the study rather than against this code:
/// </para>
/// <list type="number">
///   <item>The integers the provider prints are the study model's on every labelled bar — the port
///         is exact, not "close" (<see cref="DbsiScoreModelParityTests"/> checks every bar, not only the labelled ones).</item>
///   <item>The fidelity the manual quotes holds, on the twenty and on the fifteen.</item>
///   <item>On both, it hits DBSI's exact number, and lands within 1 and within 2, far more often than
///         the previous integer model and the linear model.</item>
/// </list>
/// </summary>
public sealed class DbsiStyleGoldenTests
{
    private readonly ITestOutputHelper _out;
    public DbsiStyleGoldenTests(ITestOutputHelper output) => _out = output;

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

    /// <summary>The twenty charts of the first study.</summary>
    private static DbsiDigitisedCharts.Chart[] Fitted => DbsiDigitisedCharts.All.Where(c => c.Fitted).ToArray();

    /// <summary>The fifteen livestream charts (2026-10-23), read by eye like the twenty.</summary>
    private static DbsiDigitisedCharts.Chart[] Livestream => DbsiDigitisedCharts.All.Where(c => !c.Fitted).ToArray();

    private static DbsiDigitisedCharts.Chart ChartNamed(string key) => DbsiDigitisedCharts.All.Single(c => c.Key == key);

    private static (double[], double[]) Printed(int[] bull, int[] bear) =>
        (bull.Select(v => (double)v).ToArray(), bear.Select(v => (double)v).ToArray());

    [Theory]
    [MemberData(nameof(Charts))]
    public void Printed_scores_are_the_study_models_on_every_digitised_bar(string key)
    {
        var c = ChartNamed(key);
        var r = Run(c);
        int n = c.Bull.Length, off = r.Bull.Length - n;
        var diffs = new List<string>();
        for (int k = 0; k < n; k++)
        {
            int i = off + k;
            if (r.Bull[i] != c.Bull[k] || r.Bear[i] != c.Bear[k])
                diffs.Add($"bar {k}: {r.Bull[i]}/{r.Bear[i]} (study {c.Bull[k]}/{c.Bear[k]})");
        }
        Assert.True(diffs.Count == 0, $"{key} ({(c.HeikinAshi ? "Heikin-Ashi" : "regular")}): {string.Join("; ", diffs)}");
    }

    [Fact]
    public void On_the_twenty_the_fidelity_is_what_the_manual_quotes()
    {
        var f = Fidelity(Pooled(Fitted, c => Labelled(c, Run(c))));
        var prev = Fidelity(Pooled(Fitted, c => Printed(c.PreviousBull, c.PreviousBear)));
        var lin = Fidelity(Pooled(Fitted, c => Printed(c.LinearBull, c.LinearBear)));
        _out.WriteLine($"the twenty, clean model: {f}");
        _out.WriteLine($"the twenty, previous integer model: {prev}");
        _out.WriteLine($"the twenty, linear model: {lin}");
        Assert.Equal(897, f.N);
        Assert.True(Math.Abs(f.Exact - 0.848) < 0.002 && Math.Abs(f.Within1 - 0.883) < 0.002
                    && Math.Abs(f.Within2 - 0.933) < 0.002 && Math.Abs(f.Rmse - 1.26) < 0.01, f.ToString());
        Assert.Equal((84, 77, 5), (f.High, f.HighPrinted, f.FalseHigh));
        // gen8.py, the previous shipped model: 55.1%, 66.7%, 80.4%, 2.16, 65 of 84 and 12 false.
        Assert.True(Math.Abs(prev.Exact - 0.551) < 0.002 && Math.Abs(prev.Within2 - 0.804) < 0.002 && prev.FalseHigh == 12, prev.ToString());
        Assert.True(f.Exact >= prev.Exact + 0.10 && f.Within1 > prev.Within1 && f.Within2 > prev.Within2 && f.Rmse < prev.Rmse,
            $"clean {f}; previous {prev}");

        // The side in control: on the bars where DBSI's two numbers differ, the larger of ours is DBSI's larger.
        var (differ, same) = SideInControl(Pooled(Fitted, c => Labelled(c, Run(c))));
        var (_, ps) = SideInControl(Pooled(Fitted, c => Printed(c.PreviousBull, c.PreviousBear)));
        _out.WriteLine($"side in control: {same} of {differ}, previous {ps}");
        Assert.Equal((422, 418, 399), (differ, same, ps));   // 99%; gen8 95%
    }

    [Fact]
    public void On_the_fifteen_livestream_charts_it_is_closer_than_the_previous_and_linear_models()
    {
        var f = Fidelity(Pooled(Livestream, c => Labelled(c, Run(c))));
        var prev = Fidelity(Pooled(Livestream, c => Printed(c.PreviousBull, c.PreviousBear)));
        var lin = Fidelity(Pooled(Livestream, c => Printed(c.LinearBull, c.LinearBear)));
        _out.WriteLine($"livestream, clean model: {f}");
        _out.WriteLine($"livestream, previous integer model: {prev}");
        _out.WriteLine($"livestream, linear model: {lin}");
        Assert.Equal(lin.N, f.N);
        Assert.Equal(prev.N, f.N);
        Assert.True(Math.Abs(f.Exact - 0.837) < 0.003 && Math.Abs(f.Within1 - 0.862) < 0.003
                    && Math.Abs(f.Within2 - 0.896) < 0.003 && Math.Abs(f.Rmse - 2.26) < 0.01, f.ToString());
        Assert.Equal((48, 45, 3), (f.High, f.HighPrinted, f.FalseHigh));
        // gen8.py: 48.5%, 58.7%, 78.1%, 2.80, 38 of 48 and 6 false; the linear model 20.4%, 67.1% within 2, 10 false.
        Assert.True(Math.Abs(prev.Exact - 0.485) < 0.003 && Math.Abs(prev.Within2 - 0.781) < 0.003 && prev.FalseHigh == 6, prev.ToString());
        Assert.True(Math.Abs(lin.Exact - 0.204) < 0.003 && Math.Abs(lin.Within2 - 0.671) < 0.003 && lin.FalseHigh == 10, lin.ToString());
        Assert.True(f.Exact >= prev.Exact + 0.10 && f.Within1 > prev.Within1 && f.Within2 > prev.Within2 && f.Rmse < prev.Rmse,
            $"clean {f}; previous {prev}");
        Assert.True(f.Exact >= lin.Exact + 0.30 && f.Within2 >= lin.Within2 + 0.10 && f.FalseHigh < lin.FalseHigh,
            $"clean {f}; linear {lin}");

        var (differ, same) = SideInControl(Pooled(Livestream, c => Labelled(c, Run(c))));
        var (_, ps) = SideInControl(Pooled(Livestream, c => Printed(c.PreviousBull, c.PreviousBear)));
        var (ld, ls) = SideInControl(Pooled(Livestream, c => Printed(c.LinearBull, c.LinearBear)));
        _out.WriteLine($"side in control: {same} of {differ}, previous {ps}, linear {ls} of {ld}");
        Assert.Equal((262, 256, 247, 243), (differ, same, ps, ls));   // 98%; gen8 and the linear model 93-94%
    }

    /// <summary>
    /// The floor the manual states: a side never prints below −5. The top on the digitised charts: DBSI's
    /// highest is 27 (and 27 on every hourly and daily set of the study), and so is the model's.
    /// </summary>
    [Fact]
    public void The_floor_is_minus_5_and_the_highest_printed_matches_DBSI()
    {
        Assert.Equal(-5, DbsiScoreModel.Floor);
        var all = DbsiDigitisedCharts.All.Select(Run).ToArray();
        double low = all.Min(r => Math.Min(r.Bull.Where(v => !double.IsNaN(v)).Min(), r.Bear.Where(v => !double.IsNaN(v)).Min()));
        Assert.Equal(-5, low);
        int top = DbsiDigitisedCharts.All.Max(c => Math.Max(c.Bull.Max(), c.Bear.Max()));
        int truthTop = DbsiDigitisedCharts.All.Max(c => Math.Max(c.TrueBull.Max() ?? -5, c.TrueBear.Max() ?? -5));
        _out.WriteLine($"highest on the charts: DBSI {truthTop}, the model {top}");
        Assert.Equal((27, 27), (truthTop, top));
    }

    /// <summary>
    /// Scores start once the gap's change is defined: wt1 from bar 27 (two 9-bar EMAs, then a
    /// 12-bar one), its 3-bar average from bar 29, the change from bar 30.
    /// </summary>
    [Fact]
    public void Scores_start_on_bar_30()
    {
        var bars = CausalityProbeSeries.Bars(0, 120).ToArray();
        foreach (bool ha in new[] { false, true })
        {
            var r = DbsiStyleProvider.Compute(bars, new DbsiStyleProvider.Settings(7, 7, 200, ha));
            Assert.Equal(30, Array.FindIndex(r.Bull, v => !double.IsNaN(v)));
            Assert.Equal(30, Array.FindIndex(r.Bear, v => !double.IsNaN(v)));
            Assert.All(r.Bull.Skip(30), v => Assert.False(double.IsNaN(v)));
        }
    }

    /// <summary>
    /// The model's sums are numpy's (pairwise), not a running total: a rule that compares a moving average
    /// with a threshold or with its own last value flips on some bar when the last bits differ. Probes whose
    /// running total and pairwise sum disagree: nine values (8 partial sums, then 1 left over) and 132
    /// values (split in halves of 64 and 68).
    /// </summary>
    [Fact]
    public void Sums_are_pairwise_like_numpy()
    {
        double[] nine = { 1e16, 1, 1, 1, 1, 1, 1, 1, -1e16 };
        Assert.Equal(6.0, DbsiScoreModel.Sum(nine, 0, nine.Length));      // a running total gives 7
        var many = new[] { 1e16 }.Concat(Enumerable.Repeat(1.0, 130)).Append(-1e16).ToArray();
        Assert.Equal(124.0, DbsiScoreModel.Sum(many, 0, many.Length));    // a running total gives 130
        Assert.Equal(3.0, DbsiScoreModel.Sum(new double[] { 9, 1, 1, 1, 9 }, 1, 3));
    }

    /// <summary>
    /// The run on a hand-made WaveTrend: the bull run starts where wt1 crosses above its average
    /// (bar 2, wt1 −65, so a deep cross), stays alive while the gap widens (5 → 7), dies when it
    /// narrows (7 → 4) and stays dead to the end of the run, keeping only its deep flag. The bear run
    /// already going on the first bar is not counted; the next one starts on bar 6 (wt1 −49: not
    /// deep for bears) and widens on bar 7.
    /// </summary>
    [Fact]
    public void The_WaveTrend_run_starts_on_the_cross_and_dies_the_first_time_the_gap_narrows()
    {
        double[] wt1 = { -70, -80, -65, -55, -50, -48, -49, -60 };
        double[] wt2 = { -60, -70, -70, -62, -54, -50, -47, -50 };
        static bool[] B(params int[] v) => v.Select(x => x != 0).ToArray();
        var bull = DbsiScoreModel.WaveRun(wt1, wt2, 1);
        Assert.Equal(B(0, 0, 1, 1, 1, 1, 0, 0), bull.Run);
        Assert.Equal(B(0, 0, 1, 1, 1, 1, 0, 0), bull.Deep);
        Assert.Equal(B(0, 0, 1, 1, 0, 0, 0, 0), bull.Alive);
        Assert.Equal(new[] { 0, 0, 0, 1, 0, 0, 0, 0 }, bull.Count);
        // Off the run from bar 6: 0 on the first bar out, then 1; before the first run no count.
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0, 0, 1 }, bull.Since);
        var bear = DbsiScoreModel.WaveRun(wt1, wt2, -1);
        Assert.Equal(B(0, 0, 0, 0, 0, 0, 1, 1), bear.Run);
        Assert.Equal(B(0, 0, 0, 0, 0, 0, 1, 1), bear.Alive);
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0, 0, 1 }, bear.Count);
        Assert.All(bear.Deep, Assert.False);
        // The uncounted bear run ends on bar 2, and its end starts no count.
        Assert.All(bear.Since, v => Assert.Equal(0, v));

        // A bear cross from wt1 +67 is deep for bears; a bull cross from wt1 +64 is not deep for bulls.
        // Both widen for twelve bars, so the count reaches its ten-bar cap.
        var down = Enumerable.Range(0, 14).Select(i => 70.0 - 3 * i * i).ToArray();
        var r = DbsiScoreModel.WaveRun(down, down.Select((v, i) => i == 0 ? v - 1 : down[i - 1]).ToArray(), -1);
        Assert.False(r.Run[0]); Assert.True(r.Run[1]); Assert.True(r.Deep[13]);
        Assert.Equal(9, r.Count[10]); Assert.Equal(DbsiScoreModel.RunCountCap, r.Count[11]); Assert.Equal(DbsiScoreModel.RunCountCap, r.Count[13]);
        var up = Enumerable.Range(0, 14).Select(i => 61.0 + 3 * i * i).ToArray();
        r = DbsiScoreModel.WaveRun(up, up.Select((v, i) => i == 0 ? v + 1 : up[i - 1]).ToArray(), 1);
        Assert.True(r.Run[1]); Assert.False(r.Deep[13]); Assert.True(r.Alive[13]);
    }

    // ── scoring ────────────────────────────────────────────────────────────────────

    private static (double[] bull, double[] bear) Labelled(DbsiDigitisedCharts.Chart c, DbsiStyleProvider.Result r)
    {
        int off = r.Bull.Length - c.Bull.Length;
        return (r.Bull.Skip(off).ToArray(), r.Bear.Skip(off).ToArray());
    }

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

    /// <summary>On the bars where DBSI's two numbers differ: how many, and on how many ours lean the same way.</summary>
    private static (int differ, int same) SideInControl((List<double> pred, List<double> truth) p)
    {
        var (pred, truth) = p;
        int differ = 0, same = 0;
        for (int j = 0; j + 1 < truth.Count; j += 2)
        {
            if (double.IsNaN(truth[j]) || double.IsNaN(truth[j + 1]) || truth[j] == truth[j + 1]) continue;
            differ++;
            if (pred[j] != pred[j + 1] && pred[j] > pred[j + 1] == truth[j] > truth[j + 1]) same++;
        }
        return (differ, same);
    }

    private sealed record Closeness(int N, double Exact, double Within1, double Within2, double Rmse, int High, int HighPrinted, int FalseHigh)
    {
        public override string ToString() =>
            $"{N} values: exact {Exact:P1}, ±1 {Within1:P1}, ±2 {Within2:P1}, error {Rmse:F2}; " +
            $"17+: {HighPrinted} of {High} printed, {FalseHigh} false";
    }

    private static Closeness Fidelity((List<double> pred, List<double> truth) p)
    {
        var e = p.pred.Zip(p.truth).Where(z => !double.IsNaN(z.Second)).ToList();
        double Share(Func<double, bool> ok) => (double)e.Count(z => ok(Math.Abs(z.First - z.Second))) / e.Count;
        return new Closeness(e.Count, Share(d => d == 0), Share(d => d <= 1), Share(d => d <= 2),
            Math.Sqrt(e.Average(z => (z.First - z.Second) * (z.First - z.Second))),
            e.Count(z => z.Second >= 17), e.Count(z => z.Second >= 17 && z.First >= 17), e.Count(z => z.Second < 17 && z.First >= 17));
    }
}
