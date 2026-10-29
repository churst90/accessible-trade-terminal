using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fixtures;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The C# score is the study's clean model, bit for bit, on every bar.</b> <see cref="DbsiCleanScores"/>
/// holds <c>dbsi8/clean_dbsi.py</c>'s bull and bear scores on every bar of the 35 digitised charts
/// (warm-up bars null), computed on the same candles the provider reads (Heikin-Ashi where the chart
/// is). A float detail the port gets wrong — a sum in the wrong order, a NaN compared the wrong way, a
/// pin that does not compare equal — flips a rule on some bar and shows here.
/// </summary>
public sealed class DbsiScoreModelParityTests
{
    public static IEnumerable<object[]> Charts() => DbsiDigitisedCharts.All.Select(c => new object[] { c.Key });

    [Theory]
    [MemberData(nameof(Charts))]
    public void Every_bar_matches_the_study_model(string key)
    {
        var c = DbsiDigitisedCharts.All.Single(x => x.Key == key);
        var r = DbsiStyleProvider.Compute(DbsiStyleGoldenTests.Bars(c).ToArray(), new DbsiStyleProvider.Settings(7, 7, 200, c.HeikinAshi));
        var (bull, bear) = DbsiCleanScores.All[key];
        Assert.Equal(bull.Length, r.Bull.Length);
        var diffs = new List<string>();
        for (int i = 0; i < bull.Length; i++)
        {
            static string S(double v) => double.IsNaN(v) ? "-" : v.ToString(System.Globalization.CultureInfo.InvariantCulture);
            double eb = bull[i] ?? double.NaN, er = bear[i] ?? double.NaN;
            bool same = (double.IsNaN(eb) ? double.IsNaN(r.Bull[i]) : r.Bull[i] == eb) && (double.IsNaN(er) ? double.IsNaN(r.Bear[i]) : r.Bear[i] == er);
            if (!same) diffs.Add($"bar {i}: {S(r.Bull[i])}/{S(r.Bear[i])} (study {S(eb)}/{S(er)})");
        }
        Assert.True(diffs.Count == 0, $"{key}: {diffs.Count} bars differ: {string.Join("; ", diffs.Take(12))}");
    }

    /// <summary>
    /// Rules the 35 charts never reach fire on <see cref="DbsiCoverageCharts"/>' windows of real candles: the bear
    /// EMA(5)/EMA(11) cross's lengths, its off-run bar and the zero-line bonus read at the run's own cross bar (gold
    /// 2004 and the rest; the 35 charts catch these too), the bear cross term's 36.5 and the wide-gap 17.34
    /// edges (Dow), the bull Q value carried through a run of 1-2 bars but not 3 (Dow 1967, gold 1949) and only
    /// when a Q bar came just before the run (Dow 1904), and the near-zero cross bound 5.6 (Dow 1953, Bitcoin hourly Heikin-Ashi), and the hidden arm's edges: a bull Q bar
    /// while it is down (gold 1967), the override's d2 &gt;= 0, the armed pinned top, the EMA state on the wide-gap terms
    /// and the 2-bar term's kept ARM (Dow, gold), and a bear Q bar with the hidden arm down or on a Q's first bar with V0 off,
    /// and the bulls' deep-run flag read on the bar a run ends and carried through later runs (gold 1980, Dow 1948). The generator refuses to write the fixture
    /// unless breaking each rule changes a score on a window.
    /// </summary>
    public static IEnumerable<object[]> CoverageWindows() => DbsiCoverageCharts.All.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(CoverageWindows))]
    public void Every_bar_of_a_coverage_window_matches_the_study_model(string key)
    {
        var (_, text, bull, bear) = DbsiCoverageCharts.All[key];
        var bars = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                var f = line.Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                return new Ohlcv(DateTimeOffset.FromUnixTimeSeconds((long)f[0]).UtcDateTime, f[1], f[2], f[3], f[4], f[5]);
            }).ToArray();
        var r = DbsiStyleProvider.Compute(bars, new DbsiStyleProvider.Settings(7, 7, 200, false));
        Assert.Equal(bull.Length, r.Bull.Length);
        var diffs = new List<string>();
        for (int i = 0; i < r.Bull.Length; i++)
        {
            double eb = bull[i] ?? double.NaN, er = bear[i] ?? double.NaN;
            bool same = (double.IsNaN(eb) ? double.IsNaN(r.Bull[i]) : r.Bull[i] == eb) && (double.IsNaN(er) ? double.IsNaN(r.Bear[i]) : r.Bear[i] == er);
            if (!same) diffs.Add($"bar {i}: {r.Bull[i]}/{r.Bear[i]} (study {eb}/{er})");
        }
        Assert.True(diffs.Count == 0, $"{key}: {diffs.Count} bars differ: {string.Join("; ", diffs.Take(12))}");
    }
}
