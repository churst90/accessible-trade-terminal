using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fixtures;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cycle Position's "Low is in" and "Low expected" (2026-10-28).</b>
///
/// <para>The golden answers are the study's own numbers, computed in Python from the Bitcoin
/// fixture's candles (Fixtures/CycleLowConfidenceReference.cs, every third bar), not the C#: the
/// C# must reproduce them to 1e-9 on every bar the study scores and on no other. The words are the
/// bands in docs/CYCLE_POSITION_DESIGN.md; what is spoken is what the table shows; both are read
/// on daily charts only, where they were fitted.</para>
/// </summary>
public sealed class CycleLowConfidenceTests
{
    private const string D = CipherCProvider.CompDailyWindow;

    private static Dictionary<string, double[]> Run(IReadOnlyList<Ohlcv> bars, string? symbol = "BTC/USD")
    {
        var p = new Dictionary<string, object>();
        if (symbol != null) p["__symbol"] = symbol;
        return CyclePositionProvider.Compute(bars.ToArray(), p);
    }

    private static Dictionary<string, double[]>? _btc;
    private static Dictionary<string, double[]> Btc() => _btc ??= Run(CipherSrSpeechTests.BtcDaily());

    private static List<int> Marked(double[] a) => Enumerable.Range(0, a.Length).Where(i => !double.IsNaN(a[i])).ToList();

    /// <summary>The error function the touch chance is read through, against its known values.</summary>
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5204998778130465)]
    [InlineData(1.0, 0.8427007929497149)]
    [InlineData(2.0, 0.9953222650189527)]
    [InlineData(3.0, 0.9999779095030014)]
    [InlineData(-1.0, -0.8427007929497149)]
    [InlineData(7.0, 1.0)]
    public void The_error_function_is_right(double x, double erf)
    {
        Assert.Equal(erf, CycleLowConfidence.Erf(x), 12);
    }

    /// <summary>
    /// Bitcoin's daily history: both chances are the study's to 1e-9 on every reference bar, and
    /// they are on exactly the bars the study scores: 2,129 held days; for Low expected every day
    /// from bar 109 to 5,337, and then on to the last bar, past the study's last real low (which
    /// it needs as its label; the chart reads on).
    /// </summary>
    [Fact]
    public void Both_chances_are_the_studys_on_Bitcoin()
    {
        var r = Btc();
        foreach (var (key, at, want, studied) in new[]
                 {
                     (D + CyclePositionProvider.LowInSuffix, CycleLowConfidenceReference.LowInAt, CycleLowConfidenceReference.LowIn, 2129),
                     (D + CyclePositionProvider.ExpectedSuffix, CycleLowConfidenceReference.ExpectedAt, CycleLowConfidenceReference.Expected, 5229),
                 })
        {
            var got = r[key];
            Assert.Equal(want.Length, at.Length);
            for (int j = 0; j < at.Length; j++)
                Assert.True(Math.Abs(got[at[j]] - want[j]) < 1e-9, $"{key} on bar {at[j]}: {got[at[j]]}, the study {want[j]}");
            Assert.Equal(studied, Marked(got).Count(t => t <= at[^1]));
            Assert.All(Marked(got), t => Assert.InRange(got[t], 0.0, 1.0));
        }
        var expected = r[D + CyclePositionProvider.ExpectedSuffix];
        Assert.Equal(Enumerable.Range(109, expected.Length - 109), Marked(expected));
    }

    /// <summary>
    /// "Low is in" is read only while a daily low is held and still standing: on every bar it is
    /// on, the days since the unconfirmed low are counted; a low already dropped is not read.
    /// </summary>
    [Fact]
    public void Low_is_in_is_read_only_while_a_low_is_held()
    {
        var r = Btc();
        var lowIn = r[D + CyclePositionProvider.LowInSuffix];
        var held = r[CipherCProvider.HeldDaysKey];
        Assert.All(Marked(lowIn), t => Assert.True(held[t] >= 0, $"Low is in on bar {t} with no low held"));
        // Bars where the held count carries a low that was already undercut and dropped: not read.
        int carried = Enumerable.Range(0, held.Length).Count(t => held[t] >= 0 && double.IsNaN(lowIn[t]));
        Assert.True(carried > 0, "no carried low on Bitcoin: the dropped-low case is not checked");
    }

    /// <summary>The words are the bands: Low is in below 20% likely to fail, then low, medium (60%), high (80%), very high (95%); Low expected unlikely, possible (15%), likely (35%).</summary>
    [Theory]
    [InlineData(0.0, "likely to fail")]
    [InlineData(0.1999, "likely to fail")]
    [InlineData(0.2, "low")]
    [InlineData(0.5999, "low")]
    [InlineData(0.6, "medium")]
    [InlineData(0.8, "high")]
    [InlineData(0.9499, "high")]
    [InlineData(0.95, "very high")]
    [InlineData(1.0, "very high")]
    public void Low_is_in_words_are_the_bands(double p, string word) => Assert.Equal(word, CycleLowConfidence.Word(p));

    [Theory]
    [InlineData(0.0, "unlikely")]
    [InlineData(0.1499, "unlikely")]
    [InlineData(0.15, "possible")]
    [InlineData(0.35, "likely")]
    [InlineData(1.0, "likely")]
    public void Low_expected_words_are_the_bands(double p, string word) => Assert.Equal(word, CycleLowConfidence.ExpectedWord(p));

    /// <summary>
    /// What is spoken is what the table shows: on a bar holding a low the state words carry the
    /// table's "Low is in" cell and its reasons; on a bar after a confirmed low with none held
    /// the table shows a dash and nothing is spoken for it; Low expected is spoken with its days.
    /// </summary>
    [Fact]
    public void The_speech_says_what_the_table_shows()
    {
        var r = Btc();
        Func<string, double[]?> comp = k => r.TryGetValue(k, out var a) ? a : null;
        var lowIn = r[D + CyclePositionProvider.LowInSuffix];
        var expected = r[D + CyclePositionProvider.ExpectedSuffix];
        string Cell(int t, string label) => CyclePositionProvider.TableRows(comp, t).Single(row => row[0].Text == label)[1].Text;

        int heldBar = Marked(lowIn)[^1];
        string word = CycleLowConfidence.Word(lowIn[heldBar]);
        Assert.StartsWith(word + ", ", Cell(heldBar, "Low is in"));
        var spoken = string.Join("; ", CyclePositionProvider.StateWords(comp, heldBar));
        Assert.Contains("low is in: " + Cell(heldBar, "Low is in") + "; about ", spoken);
        Assert.Contains("to its confirmation", spoken);
        Assert.Contains("chance at random of going back to it first", spoken);
        Assert.Matches("(confirming: |no confirmations yet)", spoken);

        int freeBar = Marked(expected).Last(t => double.IsNaN(lowIn[t]));
        Assert.Equal("—", Cell(freeBar, "Low is in"));
        var free = string.Join("; ", CyclePositionProvider.StateWords(comp, freeBar));
        Assert.DoesNotContain("low is in", free);
        string exp = CycleLowConfidence.ExpectedWord(expected[freeBar]);
        int k = (int)r[D + CyclePositionProvider.ExpectedDaysSuffix][freeBar];
        Assert.Equal($"{exp}, {k} days", Cell(freeBar, "Low expected"));
        Assert.Contains($"a new daily low in the next {k} days: {exp}, ", free);
    }

    /// <summary>Off a daily chart neither chance is read, spoken or shown: they were fitted on daily charts.</summary>
    [Fact]
    public void Off_a_daily_chart_neither_is_read()
    {
        var daily = CipherSrSpeechTests.BtcDaily();
        var start = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = daily.Select((b, i) => new Ohlcv { Date = start.AddHours(4 * i), Open = b.Open, High = b.High, Low = b.Low, Close = b.Close, Volume = b.Volume }).ToList();
        var r = Run(bars, symbol: null);
        Assert.Equal(1, r[CipherCProvider.InBarsKey][^1]);
        foreach (var key in new[] { CyclePositionProvider.LowInSuffix, CyclePositionProvider.ExpectedSuffix })
            Assert.True(!r.TryGetValue(D + key, out var a) || Marked(a).Count == 0, $"{key} read on a 4-hour chart");
        Func<string, double[]?> comp = k => r.TryGetValue(k, out var a) ? a : null;
        int t = bars.Count - 1;
        var spoken = string.Join("; ", CyclePositionProvider.StateWords(comp, t));
        Assert.DoesNotContain("low is in", spoken);
        Assert.DoesNotContain("a new daily low in the next", spoken);
        var rows = CyclePositionProvider.TableRows(comp, t);
        Assert.All(rows.Where(row => row[0].Text is "Low is in" or "Low expected"), row => Assert.Equal("", row[1].Text));
    }
}
