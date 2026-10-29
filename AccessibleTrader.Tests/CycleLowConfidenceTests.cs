using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fixtures;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cycle Position's "Low is in" and "Low expected" (2026-10-28); weekly and yearly "low is in" (2026-10-28).</b>
///
/// <para>The golden answers are the study's own numbers, computed in Python from the Bitcoin
/// fixture's candles (Fixtures/CycleLowConfidenceReference.cs, every third bar), not the C#: the
/// C# must reproduce them to 1e-9 on every bar the study scores and on no other (the weekly and
/// yearly ones in Fixtures/CycleLowConfidenceTiersReference.cs, from the same candles and Cycle
/// Position's own lows on them). The words are the
/// bands in docs/CYCLE_POSITION_DESIGN.md; what is spoken is what the table shows; both are read
/// on daily charts only, where they were fitted.</para>
/// </summary>
public sealed class CycleLowConfidenceTests
{
    private const string D = CipherCProvider.CompDailyWindow, W = CipherCProvider.CompWeeklyWindow, Y = CipherCProvider.CompYearlyWindow;

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
        Assert.DoesNotContain(CyclePositionProvider.StateWords(comp, freeBar), w => w.StartsWith("low is in", StringComparison.Ordinal));
        string exp = CycleLowConfidence.ExpectedWord(expected[freeBar]);
        int k = (int)r[D + CyclePositionProvider.ExpectedDaysSuffix][freeBar];
        Assert.Equal($"{exp}, {k} days", Cell(freeBar, "Low expected"));
        Assert.Contains($"a new daily low in the next {k} days: {exp}, ", free);
    }

    /// <summary>Off a daily chart no chance is read, spoken or shown: they were fitted on daily charts.</summary>
    [Fact]
    public void Off_a_daily_chart_neither_is_read()
    {
        var daily = CipherSrSpeechTests.BtcDaily();
        var start = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = daily.Select((b, i) => new Ohlcv { Date = start.AddHours(4 * i), Open = b.Open, High = b.High, Low = b.Low, Close = b.Close, Volume = b.Volume }).ToList();
        var r = Run(bars, symbol: null);
        Assert.Equal(1, r[CipherCProvider.InBarsKey][^1]);
        foreach (var key in new[] { D + CyclePositionProvider.LowInSuffix, D + CyclePositionProvider.ExpectedSuffix, W + CyclePositionProvider.LowInSuffix, Y + CyclePositionProvider.LowInSuffix })
            Assert.True(!r.TryGetValue(key, out var a) || Marked(a).Count == 0, $"{key} read on a 4-hour chart");
        Func<string, double[]?> comp = k => r.TryGetValue(k, out var a) ? a : null;
        int t = bars.Count - 1;
        var spoken = string.Join("; ", CyclePositionProvider.StateWords(comp, t));
        Assert.DoesNotContain("low is in", spoken);
        Assert.DoesNotContain("a new daily low in the next", spoken);
        var rows = CyclePositionProvider.TableRows(comp, t);
        Assert.All(rows.Where(row => row[0].Text is "Low is in" or "Low expected"), row => Assert.All(row.Skip(1), cell => Assert.Equal("", cell.Text)));
    }

    /// <summary>
    /// Bitcoin's daily history: the weekly and yearly chances are the study's to 1e-9 on every
    /// reference bar (among them 2012-06-26, whose close is exactly its 10-day average: the average
    /// is summed in the study's order), and they are read on exactly the bars the study reads:
    /// 4,085 with a weekly low to read, 1,266 with a yearly one.
    /// </summary>
    [Fact]
    public void Weekly_and_yearly_chances_are_the_studys_on_Bitcoin()
    {
        var r = Btc();
        foreach (var (key, at, want, read) in new[]
                 {
                     (W + CyclePositionProvider.LowInSuffix, CycleLowConfidenceTiersReference.WeeklyAt, CycleLowConfidenceTiersReference.Weekly, 4085),
                     (Y + CyclePositionProvider.LowInSuffix, CycleLowConfidenceTiersReference.YearlyAt, CycleLowConfidenceTiersReference.Yearly, 1266),
                 })
        {
            var got = r[key];
            Assert.Equal(want.Length, at.Length);
            for (int j = 0; j < at.Length; j++)
                Assert.True(Math.Abs(got[at[j]] - want[j]) < 1e-9, $"{key} on bar {at[j]}: {got[at[j]]}, the study {want[j]}");
            Assert.Equal(read, Marked(got).Count);
            Assert.Equal(at[^1], Marked(got)[^1]);
            Assert.All(Marked(got), t => Assert.InRange(got[t], 0.0, 1.0));
        }
    }

    /// <summary>
    /// The weekly and yearly readings read the lows that become the weekly and yearly lows: on
    /// Bitcoin each of its 40 confirmed weekly lows and 3 yearly lows found from the price is read,
    /// by its price, on some bar before it is confirmed.
    /// </summary>
    [Fact]
    public void Every_weekly_and_yearly_low_is_read_before_it_is_confirmed()
    {
        var bars = CipherSrSpeechTests.BtcDaily().ToArray();
        var r = Btc();
        var tiers = CyclePositionProvider.BuildTiers(bars, new Dictionary<string, object> { ["__symbol"] = "BTC/USD" });
        foreach (var (comp, run, count) in new[] { (W, tiers.W, 40), (Y, tiers.Y, 3) })
        {
            var chance = r[comp + CyclePositionProvider.LowInSuffix];
            var low = r[comp + CyclePositionProvider.LowInLowSuffix];
            var lows = Enumerable.Range(0, run.Lows.Count).Where(i => run.ConfirmedAt[i] >= 0).Select(i => (Bar: run.Lows[i], At: run.ConfirmedAt[i])).ToList();
            Assert.Equal(count, lows.Count);
            foreach (var (bar, at) in lows)
                Assert.True(Enumerable.Range(0, at).Any(t => !double.IsNaN(chance[t]) && low[t] == bars[bar].Low),
                    $"{comp}: the low of {bars[bar].Date:yyyy-MM-dd} is never read before its confirmation on {bars[at].Date:yyyy-MM-dd}");
        }
    }

    /// <summary>
    /// The weekly low read, worked by hand (gap 3 bars, the newest weekly low on bar 0): a held
    /// daily low inside the gap only when under it (bar 1, not bar 2); one past the gap with no
    /// candidate (bar 3); the candidate while the held low is above it (bar 4); a held low past the
    /// gap under the candidate (bar 5), but not one level with it (bar 6: the candidate stays);
    /// nothing before a weekly low.
    /// </summary>
    [Fact]
    public void The_weekly_low_read_worked_by_hand()
    {
        double[] lows = { 10, 8, 11, 7, 9, 6, 7 };
        var bars = lows.Select((l, i) => new Ohlcv(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i), l + 1, l + 2, l, l + 1, 0)).ToArray();
        var d = new CipherCEngine.TierRun { HeldAt = new[] { -1, 1, 2, 3, 4, 5, 6 } };
        var w = new CipherCEngine.TierRun { NewestAt = new[] { -1, 0, 0, 0, 0, 0, 0 }, HeldAt = new[] { -1, 0, 0, 0, 3, 3, 3 } };
        var subject = CycleLowConfidence.WeeklySubject(new CycleLowConfidence.Series(bars), d, w, 3);
        Assert.Equal(new[] { -1, 1, -1, 3, 3, 5, 3 }, subject);
    }

    /// <summary>
    /// The yearly low read, worked by hand (one bar a day, a radius of 2 days, no yearly low yet, so
    /// read from bar 2): bar 2 is level with bar 1 inside its radius, so it is not a low (the first of
    /// equal lows is); bar 3 is; bar 6, level with it, does not drop it (only a low strictly under
    /// does), and bar 7 does.
    /// </summary>
    [Fact]
    public void The_yearly_low_read_worked_by_hand()
    {
        double[] lows = { 5, 4, 4, 3, 6, 7, 3, 2, 8 };
        double[] day = Enumerable.Range(0, lows.Length).Select(i => 20000.0 + i).ToArray();
        var subject = CycleLowConfidence.YearlySubject(day, lows, Enumerable.Repeat(-1, lows.Length).ToArray(), radius: 2, gap: 3);
        Assert.Equal(new[] { -1, -1, -1, 3, 3, 3, 3, 7, 7 }, subject);
    }

    /// <summary>
    /// The yearly reading counts calendar days, not hours: Bitcoin's candles moved to 13:00 and 14:00
    /// on alternate days (as a session's hour moves with daylight saving) read exactly as at midnight.
    /// </summary>
    [Fact]
    public void The_yearly_reading_counts_days_not_hours()
    {
        var bars = CipherSrSpeechTests.BtcDaily().ToArray();
        var moved = bars.Select((b, i) => new Ohlcv(b.Date.AddHours(i % 2 == 0 ? 13 : 14), b.Open, b.High, b.Low, b.Close, b.Volume)).ToArray();
        var tiers = CyclePositionProvider.BuildTiers(bars, new Dictionary<string, object> { ["__symbol"] = "BTC/USD" });
        var r = Btc();
        CycleLowConfidence.Reading Yearly(Ohlcv[] data) => CycleLowConfidence.Compute(data, tiers.D, tiers.L,
            r[D + CipherCProvider.StartSuffix], r[D + CipherCProvider.EndSuffix], tiers.W, tiers.WeeklyGap, tiers.Y, tiers.Years).Yearly;
        var (atMidnight, atHours) = (Yearly(bars), Yearly(moved));
        Assert.Equal(1266, Marked(atMidnight.Chance).Count);
        Assert.Equal(atMidnight.Chance, atHours.Chance);
        Assert.Equal(atMidnight.Left, atHours.Left);
    }

    /// <summary>
    /// A yearly cycle too short to run (0.01 years) reads no yearly low: no yearly chance, an empty
    /// yearly cell (not a dash) and nothing said, while the weekly reading carries on.
    /// </summary>
    [Fact]
    public void No_yearly_cycle_no_yearly_reading()
    {
        var p = new Dictionary<string, object> { ["__symbol"] = "BTC/USD", [CyclePositionProvider.ParamYearlyCycle] = 0.01 };
        var r = CyclePositionProvider.Compute(CipherSrSpeechTests.BtcDaily().ToArray(), p);
        Assert.False(r.ContainsKey(Y + CyclePositionProvider.LowInSuffix));
        Assert.NotEmpty(Marked(r[W + CyclePositionProvider.LowInSuffix]));
        Func<string, double[]?> comp = k => r.TryGetValue(k, out var a) ? a : null;
        foreach (int t in new[] { Marked(r[W + CyclePositionProvider.LowInSuffix])[^1], r[D].Length - 1 })
        {
            Assert.Equal("", CyclePositionProvider.TableRows(comp, t).Single(row => row[0].Text == "Low is in")[3].Text);
            Assert.DoesNotContain(CyclePositionProvider.StateWords(comp, t), w => w.StartsWith("yearly low is in", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// What is spoken is what the table shows, for the weekly and yearly lows too: in the state
    /// words and in the window's own speech, "weekly low is in: " the table's weekly cell, the low's
    /// price, its reasons and confirmations; the yearly reading names no confirmations; on a bar
    /// with no weekly low to read the cell is a dash and nothing is said.
    /// </summary>
    [Fact]
    public void The_weekly_and_yearly_speech_says_what_the_table_shows()
    {
        var r = Btc();
        Func<string, double[]?> comp = k => r.TryGetValue(k, out var a) ? a : null;
        var provider = new CyclePositionProvider();
        var bars = CipherSrSpeechTests.BtcDaily();
        string Cell(int t, int column) => CyclePositionProvider.TableRows(comp, t).Single(row => row[0].Text == "Low is in")[column].Text;
        foreach (var (key, column, name) in new[] { (W, 2, "weekly"), (Y, 3, "yearly") })
        {
            var chance = r[key + CyclePositionProvider.LowInSuffix];
            int t = Marked(chance)[^1];
            string cell = Cell(t, column);
            Assert.Equal($"{CycleLowConfidence.Word(chance[t])}, {Math.Round(100 * chance[t]):0}%", cell);
            string want = $"{name} low is in: {cell}, for the low of {SpeechPriceFormatter.FormatPrice(r[key + CyclePositionProvider.LowInLowSuffix][t])}; about ";
            var state = CyclePositionProvider.StateWords(comp, t);
            var said = Assert.Single(state, w => w.StartsWith(name + " low is in", StringComparison.Ordinal));
            Assert.StartsWith(want, said);
            Assert.Contains("chance at random of going back to it first", said);
            if (key == Y) Assert.DoesNotContain("confirm", said[said.IndexOf("; about", StringComparison.Ordinal)..].Replace("to its confirmation", ""));
            else Assert.Matches("(confirming: |no confirmations yet)", said);
            var arrowed = provider.GetComponentSpeech(key, r[key][t], bars[t], r, t);
            Assert.NotNull(arrowed);
            Assert.EndsWith(said, arrowed);

            int none = Enumerable.Range(0, chance.Length).Last(i => double.IsNaN(chance[i]) && !double.IsNaN(r[key + CipherCProvider.StartSuffix][i]));
            Assert.Equal("—", Cell(none, column));
            Assert.DoesNotContain(CyclePositionProvider.StateWords(comp, none), w => w.StartsWith(name + " low is in", StringComparison.Ordinal));
        }
    }
}
