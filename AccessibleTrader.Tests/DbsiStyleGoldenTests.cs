using System.Globalization;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fixtures;
using Xunit.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The DBSI-style provider replays the integer model, bar for bar.</b>
///
/// <para>
/// The 897 digitised Market Cipher DBSI numbers (twenty charts, each with the 100 bars before its
/// labels) are in <see cref="DbsiDigitisedCharts"/>, generated from the study's files, with fifteen
/// later livestream charts no choice saw (<c>Fitted</c> false) and, for comparison, the numbers the
/// 2026-10-24 integer model (<c>Previous</c>) and the replaced linear model printed on every chart.
/// Four claims, each against the study rather than against this code:
/// </para>
/// <list type="number">
///   <item>The integers the provider prints are the study model's on every labelled bar — the port
///         is exact, not "close".</item>
///   <item>The shipped settings are a local best on the twenty: every setting one step either way
///         scores lower there. The side-specific run settings and the off-run close-up cap were
///         chosen on an hourly set of DBSI's own numbers that is not in this repository (Bitcoin 1-hour
///         Heikin-Ashi, 2026-01 to 09-06, <c>dbsi7/hside.py</c>, <c>hoff2.py</c>, <c>hnb.py</c>); the
///         twenty agree with that choice, they did not make it.</item>
///   <item>The fidelity the manual quotes holds, on the twenty and on the fifteen.</item>
///   <item>On the fifteen, it hits DBSI's exact number more often than the previous model and far
///         more often than the linear model, and lands within 2 more often than either.</item>
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

    private static DbsiStyleProvider.Result Run(DbsiDigitisedCharts.Chart c, DbsiStyleProvider.Model? model = null) =>
        DbsiStyleProvider.Compute(Bars(c).ToArray(), new DbsiStyleProvider.Settings(7, 7, 200, c.HeikinAshi), model);

    public static IEnumerable<object[]> Charts() => DbsiDigitisedCharts.All.Select(c => new object[] { c.Key });

    /// <summary>The twenty charts the settings were chosen on.</summary>
    private static DbsiDigitisedCharts.Chart[] Fitted => DbsiDigitisedCharts.All.Where(c => c.Fitted).ToArray();

    /// <summary>The fifteen livestream charts (2026-10-23) no choice saw.</summary>
    private static DbsiDigitisedCharts.Chart[] Livestream => DbsiDigitisedCharts.All.Where(c => !c.Fitted).ToArray();

    private static DbsiDigitisedCharts.Chart ChartNamed(string key) => DbsiDigitisedCharts.All.Single(c => c.Key == key);

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

    /// <summary>
    /// The streak start and the broken run's close-up points were chosen by trying small whole
    /// numbers on the twenty charts, best by the share within 2 plus the exact share
    /// (<c>dbsi7/intfinal.py</c>); each side's run settings and the off-run close-up cap on the
    /// hourly set (see the class summary). Each setting moved one step either way, through the
    /// provider's own scoring, scores lower on the twenty. The base votes' points came from the Start
    /// grid's integer program, not from a search, and are not varied.
    /// </summary>
    [Fact]
    public void Every_setting_one_step_either_way_scores_lower_on_the_twenty()
    {
        var s = DbsiStyleProvider.Shipped;
        double Objective(DbsiStyleProvider.Model m)
        {
            var f = Fidelity(Pooled(Fitted, c => Labelled(c, Run(c, m))));
            return f.Within2 + f.Exact;
        }
        double shipped = Objective(s);
        var lines = new List<string>();
        foreach (int d in new[] { -1, 1 })
        {
            foreach (var (name, m) in new (string, DbsiStyleProvider.Model)[]
                     {
                         ("bull run level", s with { Bull = s.Bull with { RunLevel = s.Bull.RunLevel + d } }),
                         ("bull deep run level", s with { Bull = s.Bull with { DeepRunLevel = s.Bull.DeepRunLevel + d } }),
                         ("bull alive points", s with { Bull = s.Bull with { AlivePoints = s.Bull.AlivePoints + d } }),
                         ("bear run level", s with { Bear = s.Bear with { RunLevel = s.Bear.RunLevel + d } }),
                         ("bear deep run level", s with { Bear = s.Bear with { DeepRunLevel = s.Bear.DeepRunLevel + d } }),
                         ("bear alive points", s with { Bear = s.Bear with { AlivePoints = s.Bear.AlivePoints + d } }),
                         ("streak start", s with { StreakStart = s.StreakStart + d }),
                         ("close-up points", s with { CloseUpPoints = s.CloseUpPoints + d }),
                         ("off-run close-up cap", s with { OffRunCloseUpCap = s.OffRunCloseUpCap + d }),
                     })
            {
                double v = Objective(m);
                lines.Add($"{name} {d:+0;-0}: {v:F4}");
                Assert.True(v < shipped, $"{name} {d:+0;-0} scores {v:F4}, shipped {shipped:F4}");
            }
        }
        _out.WriteLine($"shipped {shipped:F4}; " + string.Join(", ", lines));
    }

    [Fact]
    public void On_the_twenty_the_fidelity_is_what_the_manual_quotes()
    {
        var (pred, truth) = Pooled(Fitted, c => Labelled(c, Run(c)));
        var f = Fidelity((pred, truth));
        _out.WriteLine($"the twenty: {f}");
        // gen6.py's numbers for this exact model on all twenty charts (the previous model's, gen5.py: 34.6%, 47.4%, 74.8%, 2.58, 37 of 84 and 6 false).
        Assert.Equal(897, f.N);
        Assert.True(Math.Abs(f.Exact - 0.391) < 0.002 && Math.Abs(f.Within1 - 0.541) < 0.002
                    && Math.Abs(f.Within2 - 0.778) < 0.002, f.ToString());
        Assert.True(Math.Abs(f.Rmse - 2.31) < 0.01, f.ToString());
        Assert.Equal((84, 63, 12), (f.High, f.HighPrinted, f.FalseHigh));

        // The side in control: on the bars where DBSI's two numbers differ, the larger of ours is DBSI's larger.
        var (differ, same) = SideInControl(pred, truth);
        _out.WriteLine($"side in control: {same} of {differ}");
        Assert.Equal((422, 395), (differ, same));   // 94%; the previous model 394, the linear model 395
    }

    /// <summary>
    /// The fifteen livestream charts (2026-10-23: Coinbase and Bitstamp Bitcoin and Ethereum, 1 minute
    /// to 1 day) were never part of a choice. The model hits DBSI's exact number more often than the
    /// previous integer model (2026-10-24) and far more often than the linear model it replaced, and
    /// lands within 1 and within 2 more often than either. It prints more of DBSI's own 17+ (35 of 48,
    /// the previous 21), and more where DBSI does not (6, the previous 3, the linear 10). gen6.py: 36.2%,
    /// 50.2%, 78.0%, 2.80; the previous model (gen5.py) 31.2%, 45.2%, 71.5%, 3.07; the linear model
    /// 20.4%, 50.5%, 67.1%, 3.06, 27 of 48.
    /// </summary>
    [Fact]
    public void On_fifteen_charts_no_choice_saw_it_is_closer_than_the_previous_and_linear_models()
    {
        static (double[], double[]) Printed(int[] bull, int[] bear) => (bull.Select(v => (double)v).ToArray(), bear.Select(v => (double)v).ToArray());
        var f = Fidelity(Pooled(Livestream, c => Labelled(c, Run(c))));
        var prev = Fidelity(Pooled(Livestream, c => Printed(c.PreviousBull, c.PreviousBear)));
        var lin = Fidelity(Pooled(Livestream, c => Printed(c.LinearBull, c.LinearBear)));
        _out.WriteLine($"livestream, integer model: {f}");
        _out.WriteLine($"livestream, previous integer model: {prev}");
        _out.WriteLine($"livestream, linear model: {lin}");
        Assert.Equal(lin.N, f.N);
        Assert.Equal(prev.N, f.N);
        Assert.True(Math.Abs(f.Exact - 0.362) < 0.003 && Math.Abs(f.Within1 - 0.502) < 0.003
                    && Math.Abs(f.Within2 - 0.780) < 0.003 && Math.Abs(f.Rmse - 2.80) < 0.01, f.ToString());
        Assert.Equal((48, 35, 6), (f.High, f.HighPrinted, f.FalseHigh));
        Assert.True(Math.Abs(prev.Exact - 0.312) < 0.003 && Math.Abs(prev.Within2 - 0.715) < 0.003 && prev.FalseHigh == 3, prev.ToString());
        Assert.True(Math.Abs(lin.Exact - 0.204) < 0.003 && Math.Abs(lin.Within2 - 0.671) < 0.003 && lin.FalseHigh == 10, lin.ToString());
        Assert.True(f.Exact >= prev.Exact + 0.04 && f.Within1 >= prev.Within1 + 0.04 && f.Within2 >= prev.Within2 + 0.05 && f.Rmse < prev.Rmse,
            $"integer {f}; previous {prev}");
        Assert.True(f.Exact >= lin.Exact + 0.10 && f.Within2 >= lin.Within2 + 0.10 && f.FalseHigh < lin.FalseHigh,
            $"integer {f}; linear {lin}");

        var (differ, same) = SideInControl(Pooled(Livestream, c => Labelled(c, Run(c))));
        var (_, ps) = SideInControl(Pooled(Livestream, c => Printed(c.PreviousBull, c.PreviousBear)));
        var (ld, ls) = SideInControl(Pooled(Livestream, c => Printed(c.LinearBull, c.LinearBear)));
        _out.WriteLine($"side in control: {same} of {differ}, previous {ps}, linear {ls} of {ld}");
        Assert.Equal((262, 243, 242, 243), (differ, same, ps, ls));   // 93%, as the linear model; the previous 242
    }

    /// <summary>
    /// The scale the manual states, held true so the manual cannot drift from it: with no votes
    /// and nothing remembered a side sits at the −5 floor; every base vote and a deep cross whose
    /// gap has widened for ten bars print 31 for bulls and 27 for bears. On the digitised charts
    /// DBSI's highest is 27 (and 27 on 7,127 hourly bars), and the model's is lower.
    /// </summary>
    [Fact]
    public void The_floor_is_minus_5_and_the_top_is_31_for_bulls_and_27_for_bears()
    {
        var m = DbsiStyleProvider.Shipped;
        Assert.Equal(-5, m.Score(1, Votes(0), Wave(0, 0, 0, 0), 0, 0, 0));
        Assert.Equal(8, m.Score(-1, Votes(1), Wave(0, 0, 0, 0), 0, 0, 0));
        Assert.Equal(31, m.Score(1, Votes(1), Wave(1, 1, 1, DbsiStyleProvider.RunCountCap), 99, 1, 0));
        Assert.Equal(27, m.Score(-1, Votes(1), Wave(1, 1, 1, DbsiStyleProvider.RunCountCap), 99, 1, 0));
        // Not alive: a gap widening for twelve bars (counted to ten) and a close up, after a deep cross.
        Assert.Equal(8 + 7 + 9 + 2, m.Score(1, Votes(1), Wave(1, 1, 0, 0), 12, 1, 0));
        Assert.Equal(8 + 2 + 9 + 2, m.Score(-1, Votes(1), Wave(1, 1, 0, 0), 12, 1, 0));
        int top = DbsiDigitisedCharts.All.Max(c => Math.Max(c.Bull.Max(), c.Bear.Max()));
        int truthTop = DbsiDigitisedCharts.All.Max(c => Math.Max(c.TrueBull.Max() ?? -5, c.TrueBear.Max() ?? -5));
        _out.WriteLine($"highest on the charts: DBSI {truthTop}, the model {top}");
        Assert.Equal((27, 27), (truthTop, top));
    }

    private static double[][] Votes(double v) => Enumerable.Range(0, 7).Select(_ => new[] { v }).ToArray();

    private static DbsiStyleProvider.WaveRun Wave(double run, double deep, double alive, double count, double sinceRun = 0) =>
        new(new[] { run }, new[] { deep }, new[] { alive }, new[] { count }, new[] { sinceRun });

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

    /// <summary>A close equal to the previous close is a close up for neither side (strictly beyond, as in the study).</summary>
    [Fact]
    public void An_unchanged_close_is_no_close_up_for_either_side()
    {
        var bars = CausalityProbeSeries.Bars(0, 40).ToArray();
        bars[20] = bars[20] with { Close = bars[19].Close };
        var t = DbsiStyleProvider.ScoringTerms(bars, heikinAshi: false);
        Assert.Equal((0.0, 0.0), (t.BullCloseUp[20], t.BearCloseUp[20]));
        // Every other bar moved, so exactly one side's close is up.
        Assert.All(Enumerable.Range(1, 39).Where(i => i != 20), i => Assert.Equal(1.0, t.BullCloseUp[i] + t.BearCloseUp[i]));
    }

    /// <summary>
    /// Alive, the run's level, the alive points and the count are all; the streak and the close are
    /// ignored. Not alive (a broken run or none), the gap's streak counts from its second bar, up to
    /// ten bars; a close up adds 2 in a broken run, and off a run one point per bar since the run
    /// ended, up to 4.
    /// </summary>
    [Fact]
    public void Only_a_side_whose_run_is_not_alive_scores_the_gap_streak_and_the_close()
    {
        var m = DbsiStyleProvider.Shipped;
        double[][] none = Votes(0);
        // Alive four bars after a normal cross: −5 + 1 + 6 (bulls) or 7 (bears) + 4, whatever the streak and the close.
        Assert.Equal(6, m.Score(1, none, Wave(1, 0, 1, 4), 0, 0, 0));
        Assert.Equal(6, m.Score(1, none, Wave(1, 0, 1, 4), 7, 1, 0));
        Assert.Equal(7, m.Score(-1, none, Wave(1, 0, 1, 4), 7, 1, 0));
        // A deep cross: the bulls' level is 7, the bears' 2.
        Assert.Equal(-5 + 7 + 6 + 4, m.Score(1, none, Wave(1, 1, 1, 4), 0, 0, 0));
        Assert.Equal(-5 + 2 + 7 + 4, m.Score(-1, none, Wave(1, 1, 1, 4), 0, 0, 0));
        // Broken run (normal cross): −5 + 1 + streak − 1 + 2 on a close up, however long ago a run ended.
        Assert.Equal(-4, m.Score(1, none, Wave(1, 0, 0, 0), 1, 0, 0));
        Assert.Equal(-1, m.Score(1, none, Wave(1, 0, 0, 0), 4, 0, 0));
        Assert.Equal(1, m.Score(1, none, Wave(1, 0, 0, 0), 4, 1, 0));
        Assert.Equal(1, m.Score(-1, none, Wave(1, 0, 0, 0, sinceRun: 9), 4, 1, 0));
        // No run: the streak and the close alone, from the floor; the close counts the bars since the run ended, up to 4.
        Assert.Equal(-5, m.Score(1, none, Wave(0, 0, 0, 0), 1, 0, 0));
        Assert.Equal(-5, m.Score(1, none, Wave(0, 0, 0, 0, sinceRun: 0), 1, 1, 0));
        Assert.Equal(-4, m.Score(1, none, Wave(0, 0, 0, 0, sinceRun: 1), 1, 1, 0));
        Assert.Equal(-2, m.Score(-1, none, Wave(0, 0, 0, 0, sinceRun: 3), 1, 1, 0));
        Assert.Equal(-1, m.Score(1, none, Wave(0, 0, 0, 0, sinceRun: 4), 1, 1, 0));
        Assert.Equal(-1, m.Score(-1, none, Wave(0, 0, 0, 0, sinceRun: 40), 1, 1, 0));
        Assert.Equal(-5, m.Score(1, none, Wave(0, 0, 0, 0, sinceRun: 40), 1, 0, 0));
        Assert.Equal(4, m.Score(1, none, Wave(0, 0, 0, 0), 10, 0, 0));
        Assert.Equal(4, m.Score(1, none, Wave(0, 0, 0, 0), 25, 0, 0));
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
        var bull = DbsiStyleProvider.WaveTrendRun(wt1, wt2, 1);
        Assert.Equal(new double[] { 0, 0, 1, 1, 1, 1, 0, 0 }, bull.Run);
        Assert.Equal(new double[] { 0, 0, 1, 1, 1, 1, 0, 0 }, bull.Deep);
        Assert.Equal(new double[] { 0, 0, 1, 1, 0, 0, 0, 0 }, bull.Alive);
        Assert.Equal(new double[] { 0, 0, 0, 1, 0, 0, 0, 0 }, bull.Count);
        // Off the run from bar 6: 0 on the first bar out, then 1; before the first run no count.
        Assert.Equal(new double[] { 0, 0, 0, 0, 0, 0, 0, 1 }, bull.SinceRun);
        var bear = DbsiStyleProvider.WaveTrendRun(wt1, wt2, -1);
        Assert.Equal(new double[] { 0, 0, 0, 0, 0, 0, 1, 1 }, bear.Run);
        Assert.Equal(new double[] { 0, 0, 0, 0, 0, 0, 1, 1 }, bear.Alive);
        Assert.Equal(new double[] { 0, 0, 0, 0, 0, 0, 0, 1 }, bear.Count);
        Assert.All(bear.Deep, v => Assert.Equal(0, v));
        // The uncounted bear run ends on bar 2, and its end starts no count.
        Assert.All(bear.SinceRun, v => Assert.Equal(0, v));

        // A bear cross from wt1 +67 is deep for bears; a bull cross from wt1 +64 is not deep for bulls.
        // Both widen for twelve bars, so the count reaches its ten-bar cap.
        var down = Enumerable.Range(0, 14).Select(i => 70.0 - 3 * i * i).ToArray();
        var r = DbsiStyleProvider.WaveTrendRun(down, down.Select((v, i) => i == 0 ? v - 1 : down[i - 1]).ToArray(), -1);
        Assert.Equal(0, r.Run[0]); Assert.Equal(1, r.Run[1]); Assert.Equal(1, r.Deep[13]);
        Assert.Equal(9, r.Count[10]); Assert.Equal(DbsiStyleProvider.RunCountCap, r.Count[11]); Assert.Equal(DbsiStyleProvider.RunCountCap, r.Count[13]);
        var up = Enumerable.Range(0, 14).Select(i => 61.0 + 3 * i * i).ToArray();
        r = DbsiStyleProvider.WaveTrendRun(up, up.Select((v, i) => i == 0 ? v + 1 : up[i - 1]).ToArray(), 1);
        Assert.Equal(1, r.Run[1]); Assert.Equal(0, r.Deep[13]); Assert.Equal(1, r.Alive[13]);
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

    private static (int differ, int same) SideInControl(List<double> pred, List<double> truth) => SideInControl((pred, truth));

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
