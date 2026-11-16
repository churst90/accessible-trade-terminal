using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Fixtures;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cycle Position's "Low is in" and "Low expected" (2026-10-28; Low expected removed 2026-11-16); weekly and yearly "low is in" (2026-10-28).</b>
///
/// <para>The golden answers are the study's own numbers, computed in Python from the Bitcoin
/// fixture's candles (Fixtures/CycleLowConfidenceReference.cs, every third bar), not the C#: the
/// C# must reproduce them to 1e-9 on every bar the study scores and on no other (the weekly and
/// yearly ones in Fixtures/CycleLowConfidenceTiersReference.cs, from the same candles and Cycle
/// Position's own lows on them). The words are the
/// bands in docs/CYCLE_POSITION_DESIGN.md; what is spoken is what the table shows; fitted on daily
/// charts, they are read off them in the chart's bars, where they were checked unseen.</para>
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
    /// Bitcoin's daily history: the chance is the study's to 1e-9 on every reference bar, and it is
    /// on exactly the bars the study scores, 2,129 held days. (Low expected, pinned here too until
    /// it was removed 2026-11-16, is gone from the fixture.)
    /// </summary>
    [Fact]
    public void The_chance_is_the_studys_on_Bitcoin()
    {
        var r = Btc();
        string key = D + CyclePositionProvider.LowInSuffix;
        var (at, want, got) = (CycleLowConfidenceReference.LowInAt, CycleLowConfidenceReference.LowIn, r[key]);
        Assert.Equal(want.Length, at.Length);
        for (int j = 0; j < at.Length; j++)
            Assert.True(Math.Abs(got[at[j]] - want[j]) < 1e-9, $"{key} on bar {at[j]}: {got[at[j]]}, the study {want[j]}");
        Assert.Equal(2129, Marked(got).Count(t => t <= at[^1]));
        Assert.All(Marked(got), t => Assert.InRange(got[t], 0.0, 1.0));
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

    /// <summary>The words are the bands: below 20% likely to fail, then low, medium (60%), high (80%), very high (95%).</summary>
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

    /// <summary>
    /// What is spoken is what the table shows: on a bar holding a low the state words carry the
    /// table's "Low is in" cell and its reasons; on a bar after a confirmed low with none held
    /// the table shows a dash and nothing is spoken for it. Low expected (a new daily low in the
    /// next few days, right about half the time) was removed 2026-11-16: no row, no words, no line.
    /// </summary>
    [Fact]
    public void The_speech_says_what_the_table_shows()
    {
        var r = Btc();
        Func<string, double[]?> comp = k => r.TryGetValue(k, out var a) ? a : null;
        var lowIn = r[D + CyclePositionProvider.LowInSuffix];
        var start = r[D + CipherCProvider.StartSuffix];
        string Cell(int t, string label) => CyclePositionProvider.TableRows(comp, t).Single(row => row[0].Text == label)[1].Text;

        int heldBar = Marked(lowIn)[^1];
        string word = CycleLowConfidence.Word(lowIn[heldBar]);
        Assert.StartsWith(word + ", ", Cell(heldBar, "Low is in"));
        var spoken = string.Join("; ", CyclePositionProvider.StateWords(comp, heldBar));
        Assert.Contains("low is in: " + Cell(heldBar, "Low is in") + "; about ", spoken);
        Assert.Contains("to its confirmation", spoken);
        Assert.Contains("chance at random of going back to it first", spoken);
        Assert.Matches("(confirming: |no confirmations yet)", spoken);

        int freeBar = Enumerable.Range(0, lowIn.Length).Last(t => double.IsNaN(lowIn[t]) && !double.IsNaN(start[t]));
        Assert.Equal("—", Cell(freeBar, "Low is in"));
        Assert.DoesNotContain(CyclePositionProvider.StateWords(comp, freeBar), w => w.StartsWith("low is in", StringComparison.Ordinal));

        Assert.DoesNotContain(CyclePositionProvider.TableRows(comp, freeBar), row => row[0].Text.Contains("expected", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(r.Keys, k => k.Contains("xpected", StringComparison.Ordinal));
        Assert.DoesNotContain(new CyclePositionProvider().GetIndicators().Single().Components, c => (c.DisplayName ?? c.Name).Contains("expected", StringComparison.OrdinalIgnoreCase));
        for (int t = 0; t < lowIn.Length; t += 7)
            Assert.DoesNotContain(CyclePositionProvider.StateWords(comp, t), w => w.Contains("new daily low in the next", StringComparison.Ordinal));
    }

    /// <summary>
    /// Off a daily chart, Low is in is read only where it was re-run on the time-counted lows and
    /// held (2026-11-16, CYCLE_POSITION_DESIGN section 9.14): on a weekly chart, the weekly reading
    /// (the chart's own lows) and the yearly one; on a 4-hour chart, the weekly reading only. The
    /// tops and half-cycle lows, fitted in daily bars, stay daily-only. Said and tabled in time: the
    /// days to the confirmation in days, the 10-bar average as 10 weeks or 40 hours.
    /// </summary>
    [Fact]
    public void Off_a_daily_chart_only_the_readings_that_held_are_read()
    {
        var daily = CipherSrSpeechTests.BtcDaily();
        var start = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        List<Ohlcv> Stamped(Func<int, DateTime> date) => daily.Select((b, i) => new Ohlcv { Date = date(i), Open = b.Open, High = b.High, Low = b.Low, Close = b.Close, Volume = b.Volume }).ToList();
        var days = Run(Stamped(i => start.AddDays(i)), symbol: null);
        Assert.True(Marked(days[D + CyclePositionProvider.LowInSuffix]).Count > 100, "Low is in hardly read on a daily chart");
        Assert.NotEmpty(Marked(days[D + CyclePositionProvider.TopSuffix]));
        Assert.False(days.ContainsKey(W + CyclePositionProvider.LowInBarSuffix), "a daily chart names its average in days");
        foreach (var (name, r, read, notRead, average) in new[]
                 {
                     ("4-hour", Run(Stamped(i => start.AddHours(4 * i)), symbol: null), new[] { W }, new[] { D, Y }, "40-hour average"),
                     ("weekly", Run(Stamped(i => start.AddDays(7 * i)), symbol: null), new[] { W, Y }, new[] { D }, "10-week average"),
                 })
        {
            foreach (var comp in read)
                Assert.True(r.TryGetValue(comp + CyclePositionProvider.LowInSuffix, out var a) && Marked(a).Count > 100, $"{name}: {comp} not read");
            foreach (var key in notRead.Select(c => c + CyclePositionProvider.LowInSuffix).Concat(new[] { D + CyclePositionProvider.TopSuffix, CyclePositionProvider.CompHalfLow }))
                Assert.True(!r.TryGetValue(key, out var a) || Marked(a).Count == 0, $"{name}: {key}");
            Assert.NotEmpty(Marked(r[CyclePositionProvider.CompWeeklyVeryHigh]));
            Func<string, double[]?> comp2 = k => r.TryGetValue(k, out var a) ? a : null;
            // The days to the confirmation are whole days, and the average is named in time.
            var left = r[W + CyclePositionProvider.LowInLeftSuffix];
            Assert.All(Marked(left), t => Assert.Equal(Math.Round(left[t]), left[t]));
            // In days, not bars: a weekly reading runs at most a daily cycle and a half (48 days on
            // Bitcoin, about 290 four-hour bars) to its confirmation; a weekly chart's up to 11 weeks,
            // said in its trading days (these stamps have no weekends: 5 to the week).
            double most = Marked(left).Max(t => left[t]);
            Assert.True(name == "4-hour" ? most < 150 : most > 30, $"{name}: at most {most} days to a confirmation");
            int said = Enumerable.Range(0, left.Length).Last(t => r[W + CyclePositionProvider.LowInSmaSuffix][t] > 0);
            var words = CyclePositionProvider.LowInWords(k => comp2(k) is { } a ? a[said] : double.NaN, W)!;
            Assert.Contains(average, words);
            Assert.DoesNotContain("10-day", words);
            int last = r[CyclePositionProvider.LengthKey].Length - 1;
            var row = CyclePositionProvider.TableRows(comp2, last).Single(row => row[0].Text == "Low is in");
            Assert.Equal("", row[1].Text);
            Assert.All(CyclePositionProvider.TableRows(comp2, last).Where(row => row[0].Text is "Top so far" or "Half-cycle low"),
                row => Assert.All(row.Skip(1), c => Assert.Equal("", c.Text)));
        }
    }

    /// <summary>
    /// The yearly reading stays off a 4-hour chart (9 of 11 first "very high" calls right when
    /// re-run, 2026-11-16) even where the chart is long enough for yearly lows: Bitcoin's daily
    /// history cut into six 4-hour bars a day, eleven years.
    /// </summary>
    [Fact]
    public void A_long_four_hour_chart_has_no_yearly_reading()
    {
        var daily = CipherSrSpeechTests.BtcDaily();
        var start = new DateTime(2014, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>();
        for (int i = 0; i < daily.Count; i++)
        {
            var b = daily[i];
            for (int k = 0; k < 6; k++)
            {
                double o = b.Open + (b.Close - b.Open) * k / 6, c = b.Open + (b.Close - b.Open) * (k + 1) / 6;
                bars.Add(new Ohlcv { Date = start.AddDays(i).AddHours(4 * k), Open = o, Close = c, Volume = b.Volume / 6,
                    High = k == 2 ? b.High : Math.Max(o, c), Low = k == 3 ? b.Low : Math.Min(o, c) });
            }
        }
        var r = Run(bars, symbol: null);
        Assert.True(r.TryGetValue(W + CyclePositionProvider.LowInSuffix, out var weekly) && Marked(weekly).Count > 100, "the weekly reading is read");
        Assert.True(Marked(r[CipherCProvider.CompYcl]).Count >= 2, "no yearly lows to read: the test proves nothing");
        Assert.True(!r.TryGetValue(Y + CyclePositionProvider.LowInSuffix, out var yearly) || Marked(yearly).Count == 0, "yearly read on a 4-hour chart");
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
        CycleLowConfidence.Reading Yearly(Ohlcv[] data) => CycleLowConfidence.Compute(data, tiers.D, tiers.L,
            tiers.W, tiers.WeeklyGap, tiers.Y, tiers.Years).Yearly;
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
            var dailyLow = r[D + CyclePositionProvider.LowInLowSuffix];
            int t = Marked(chance).Last(i => key == Y || dailyLow[i] != r[key + CyclePositionProvider.LowInLowSuffix][i]);
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

    /// <summary>
    /// The weekly reading of the same low as the daily one, said after it, does not say again what
    /// the daily one has just said (how far above the low, the confirmations): "weekly low is in:
    /// high, 88%, the same low as the daily reading; about 41 days to its weekly confirmation; a 3%
    /// chance at random ...". Arrowed on the weekly window, where the daily one is not said, it is said whole.
    /// </summary>
    [Fact]
    public void A_weekly_reading_of_the_daily_low_is_not_said_twice()
    {
        var r = Btc();
        Func<string, double[]?> comp = k => r.TryGetValue(k, out var a) ? a : null;
        var weekly = r[W + CyclePositionProvider.LowInSuffix];
        int t = Marked(weekly).Last(i => r[D + CyclePositionProvider.LowInLowSuffix][i] == r[W + CyclePositionProvider.LowInLowSuffix][i]);
        string cell = CyclePositionProvider.TableRows(comp, t).Single(row => row[0].Text == "Low is in")[2].Text;
        var state = CyclePositionProvider.StateWords(comp, t);
        var said = Assert.Single(state, w => w.StartsWith("weekly low is in", StringComparison.Ordinal));
        Assert.StartsWith($"weekly low is in: {cell}, the same low as the daily reading; about ", said);
        Assert.Contains("to its weekly confirmation; a ", said);
        Assert.DoesNotContain("above the low", said);
        Assert.DoesNotContain("confirming", said);
        Assert.DoesNotContain("no confirmations", said);
        Assert.Contains(state, w => w.StartsWith("low is in: ", StringComparison.Ordinal) && w.Contains("above the low"));
        var arrowed = new CyclePositionProvider().GetComponentSpeech(W, r[W][t], CipherSrSpeechTests.BtcDaily()[t], r, t);
        Assert.Contains($"weekly low is in: {cell}, for the low of ", arrowed);
        Assert.Contains("above the low", arrowed);
    }
    /// <summary>
    /// The weekly reading's record: on each bar, of the weekly lows confirmed so far, how many had a
    /// "very high" reading of that low on or before their confirmation bar, worked low by low from the
    /// weekly tier (not bar by bar as the app does). Made-up arrays: a call on the confirmation bar
    /// counts, one after does not, nor one of another low; 0.95 is "very high". Where under 60% of at
    /// least 5 lows were called, the weekly reading says so and its table cell says "often missed
    /// here"; 3 of 5 is not under, nor are 4 lows with none called.
    /// </summary>
    [Fact]
    public void The_weekly_record_counts_lows_called_very_high_by_their_confirmation()
    {
        int[] last = { -1, -1, 1, 1, 1, 5, 5, 5, 7, 7 };
        int[] subject = { 1, 1, -1, 5, 5, 5, 7, 9, 9, 9 };
        double[] chance = { 0.5, 0.95, double.NaN, 0.2, 0.3, 0.97, 0.4, 0.99, 0.99, 0.99 };
        var (called, of) = CyclePositionProvider.Record(last, subject, chance);
        Assert.Equal(new double[] { 0, 0, 1, 1, 1, 2, 2, 2, 2, 2 }, called);
        Assert.Equal(new double[] { 0, 0, 1, 1, 1, 2, 2, 2, 3, 3 }, of);

        Func<string, double> At(double c, double m) => k => k == W + CyclePositionProvider.CalledSuffix ? c : k == W + CyclePositionProvider.CalledOfSuffix ? m : double.NaN;
        Assert.True(CyclePositionProvider.OftenMissed(At(2, 5)));
        Assert.False(CyclePositionProvider.OftenMissed(At(3, 5)));
        Assert.False(CyclePositionProvider.OftenMissed(At(0, 4)));

        var bars = CipherSrSpeechTests.BtcDaily().ToArray();
        var r = Btc();
        var tr = CyclePositionProvider.BuildTiers(bars, new Dictionary<string, object> { ["__symbol"] = "BTC/USD" });
        var subj = CycleLowConfidence.WeeklySubject(new CycleLowConfidence.Series(bars), tr.D, tr.W, tr.WeeklyGap);
        var wChance = r[W + CyclePositionProvider.LowInSuffix];
        var lows = tr.W.LastLowAt.Where(a => a >= 0).Distinct()
            .Select(a => (Confirmed: Array.IndexOf(tr.W.LastLowAt, a), Low: a))
            .Select(x => (x.Confirmed, Called: Enumerable.Range(0, x.Confirmed + 1).Any(t => subj[t] == x.Low && wChance[t] >= 0.95))).ToList();
        Assert.True(lows.Count > 20);
        Func<string, double[]?> comp = k => r.TryGetValue(k, out var a) ? a : null;
        int flagged = 0, plain = 0, besideDaily = 0;
        for (int t = 0; t < bars.Length; t++)
        {
            int m = lows.Count(x => x.Confirmed <= t), k = lows.Count(x => x.Confirmed <= t && x.Called);
            Assert.Equal(k, r[W + CyclePositionProvider.CalledSuffix][t]);
            Assert.Equal(m, r[W + CyclePositionProvider.CalledOfSuffix][t]);
            if (double.IsNaN(wChance[t])) continue;
            bool flag = m >= 5 && k < 0.6 * m;
            string note = $"; on this chart weekly lows often come without it: {k} of {m} so far had a very high reading by their confirmation";
            var state = CyclePositionProvider.StateWords(comp, t);
            var said = Assert.Single(state, w => w.StartsWith("weekly low is in", StringComparison.Ordinal));
            string cell = CyclePositionProvider.TableRows(comp, t).Single(row => row[0].Text == "Low is in")[2].Text;
            if (flag) { flagged++; Assert.EndsWith(note, said); Assert.Single(state, w => w.Contains("often come without it")); Assert.EndsWith(", often missed here", cell); if (state.Any(w => w.StartsWith("low is in: ", StringComparison.Ordinal))) besideDaily++; }
            else { plain++; Assert.DoesNotContain(state, w => w.Contains("often come without it")); Assert.DoesNotContain("often missed", cell); }
        }
        Assert.True(flagged > 50 && plain > 50 && besideDaily > 50, $"{flagged} flagged ({besideDaily} beside a daily reading), {plain} not");
    }
    /// <summary>
    /// The "very high" marks are on the first bar each low's daily or weekly reading is very high
    /// (once a low: made-up arrays, where the low read again later is not marked again), at that
    /// bar's low, said with the share and the low; the call lines are the share in percent there
    /// and 0 on every other bar; the share lines are the readings in percent. Worked on Bitcoin from
    /// the tiers' own subjects. The switch hides the marks and changes nothing else; strategies
    /// read the marks as fired signals and every line as a line.
    /// </summary>
    [Fact]
    public void The_very_high_marks_and_lines_are_the_first_very_high_bar_of_each_low()
    {
        var made = new Ohlcv[7];
        for (int t = 0; t < 7; t++) made[t] = new Ohlcv(new DateTime(2024, 1, 1).AddDays(t), 10, 12, 9 + t, 11, 0);
        var reading = new CycleLowConfidence.Reading { Subject = new[] { -1, 2, 2, 2, 5, 5, 2 }, Chance = new[] { 0.99, 0.94, 0.95, 0.99, 0.96, 0.97, 0.99 } };
        double[] mark = CipherCEngine.Nan(7), share = CipherCEngine.Nan(7), line = CipherCEngine.Nan(7);
        CyclePositionProvider.VeryHighCalls(made, reading, mark, share, line);
        Assert.Equal(new[] { double.NaN, double.NaN, 11, double.NaN, 13, double.NaN, double.NaN }, mark);
        Assert.Equal(new[] { double.NaN, double.NaN, 0.95, double.NaN, 0.96, double.NaN, double.NaN }, share);
        Assert.Equal(new double[] { 0, 0, 95, 0, 96, 0, 0 }, line);

        var bars = CipherSrSpeechTests.BtcDaily().ToArray();
        var r = Btc();
        var tr = CyclePositionProvider.BuildTiers(bars, new Dictionary<string, object> { ["__symbol"] = "BTC/USD" });
        var weeklySubject = CycleLowConfidence.WeeklySubject(new CycleLowConfidence.Series(bars), tr.D, tr.W, tr.WeeklyGap);
        var provider = new CyclePositionProvider();
        foreach (var (tier, subject, comp, callLine, shareLine, least) in new[]
                 {
                     (D, tr.D.HeldAt, CyclePositionProvider.CompVeryHigh, CyclePositionProvider.CompCallLine, CyclePositionProvider.CompLowInLine, 50),
                     (W, weeklySubject, CyclePositionProvider.CompWeeklyVeryHigh, CyclePositionProvider.CompWeeklyCallLine, CyclePositionProvider.CompWeeklyLowInLine, 10),
                 })
        {
            var chance = r[tier + CyclePositionProvider.LowInSuffix];
            var first = subject.Select((c, t) => (c, t)).Where(x => x.c >= 0 && chance[x.t] >= 0.95).GroupBy(x => x.c).Select(g => g.Min(x => x.t)).ToHashSet();
            Assert.True(first.Count > least, $"{comp}: {first.Count}");
            for (int t = 0; t < bars.Length; t++)
            {
                Assert.Equal(first.Contains(t) ? bars[t].Low : double.NaN, r[comp][t]);
                Assert.Equal(first.Contains(t) ? chance[t] : double.NaN, r[comp + CyclePositionProvider.ShareSuffix][t]);
                Assert.Equal(first.Contains(t) ? 100 * chance[t] : 0, r[callLine][t]);
                Assert.Equal(100 * chance[t], r[shareLine][t]);
            }
            int at = first.Max();
            Assert.Equal($"{(tier == D ? "Low" : "Weekly low")} is in: very high, {Math.Round(100 * chance[at]):0}%, for the low of {SpeechPriceFormatter.FormatPrice(bars[subject[at]].Low)}.",
                provider.GetComponentSpeech(comp, r[comp][at], bars[at], r, at));
            Assert.Equal(CyclePositionProvider.LowInWords(k => r.TryGetValue(k, out var a) ? a[at] : double.NaN, tier),
                provider.GetComponentSpeech(shareLine, r[shareLine][at], bars[at], r, at));
            Assert.StartsWith($"{(tier == D ? "daily" : "weekly")} very high call, ", provider.GetComponentSpeech(callLine, r[callLine][at], bars[at], r, at));
            Assert.StartsWith("no ", provider.GetComponentSpeech(callLine, 0, bars[at - 1], r, at - 1));
        }
        for (int t = 0; t < bars.Length; t++)
        {
            Assert.Equal(100 * r[Y + CyclePositionProvider.LowInSuffix][t], r[CyclePositionProvider.CompYearlyLowInLine][t]);
        }

        var off = CyclePositionProvider.Compute(bars, new Dictionary<string, object> { ["__symbol"] = "BTC/USD", [CyclePositionProvider.ParamVeryHighMarks] = false });
        foreach (var comp in new[] { CyclePositionProvider.CompVeryHigh, CyclePositionProvider.CompWeeklyVeryHigh, CyclePositionProvider.CompWeeklyHigh })
        {
            var marked = Marked(r[comp]);
            Assert.NotEmpty(marked);
            Assert.All(r[comp + ComponentCompanions.Hide], v => Assert.True(double.IsNaN(v)));
            for (int t = 0; t < bars.Length; t++)
                Assert.Equal(marked.Contains(t) ? ComponentCompanions.HideMarker : double.NaN, off[comp + ComponentCompanions.Hide][t]);
        }
        Assert.Equal(r.Keys.Order(), off.Keys.Order());
        foreach (var key in r.Keys.Where(k => !k.EndsWith(ComponentCompanions.Hide, StringComparison.Ordinal) || !(k.Contains("Very High") || k.StartsWith(CyclePositionProvider.CompWeeklyHigh, StringComparison.Ordinal))))
            Assert.Equal(r[key], off[key]);

        var catalog = new AccessibleTrader.Core.Services.Strategies.SignalCatalog(new[] { provider });
        foreach (var comp in new[] { CyclePositionProvider.CompVeryHigh, CyclePositionProvider.CompWeeklyVeryHigh, CyclePositionProvider.CompWeeklyHigh })
            Assert.Equal(SignalKind.MarkerFire, catalog.GetById($"{CyclePositionProvider.Code}.{comp}")!.Kind);
        foreach (var comp in new[] { CyclePositionProvider.CompLowInLine, CyclePositionProvider.CompWeeklyLowInLine, CyclePositionProvider.CompYearlyLowInLine,
                     CyclePositionProvider.CompCallLine, CyclePositionProvider.CompWeeklyCallLine, CyclePositionProvider.CompWeeklyHighCallLine })
            Assert.Equal(SignalKind.Line, catalog.GetById($"{CyclePositionProvider.Code}.{comp}")!.Kind);
        Assert.Contains(catalog.All, d => d.Id == $"{CyclePositionProvider.Code}.{CyclePositionProvider.CompCallLine}");
    }

    /// <summary>
    /// "High" is the word to act on for the weekly (Cody, 2026-11-16): the weekly high call is on the
    /// first bar each weekly low's reading is at least "high" (80%), once a low; its mark is left off
    /// a bar already "very high" (the very high mark is there) while its line still says the call.
    /// Made-up arrays, then every bar of Bitcoin's weekly reading, the speech, and the order: a low's
    /// high call never comes after its very high one.
    /// </summary>
    [Fact]
    public void The_weekly_high_call_is_the_first_high_bar_of_each_weekly_low()
    {
        var made = new Ohlcv[7];
        for (int t = 0; t < 7; t++) made[t] = new Ohlcv(new DateTime(2024, 1, 1).AddDays(t), 10, 12, 9 + t, 11, 0);
        var reading = new CycleLowConfidence.Reading { Subject = new[] { -1, 2, 2, 2, 5, 5, 2 }, Chance = new[] { 0.99, 0.79, 0.85, 0.99, 0.96, 0.97, 0.99 } };
        double[] mark = CipherCEngine.Nan(7), share = CipherCEngine.Nan(7), line = CipherCEngine.Nan(7);
        CyclePositionProvider.VeryHighCalls(made, reading, mark, share, line, CyclePositionProvider.High);
        Assert.Equal(new[] { double.NaN, double.NaN, 11, double.NaN, double.NaN, double.NaN, double.NaN }, mark);
        Assert.Equal(new[] { double.NaN, double.NaN, 0.85, double.NaN, double.NaN, double.NaN, double.NaN }, share);
        Assert.Equal(new double[] { 0, 0, 85, 0, 96, 0, 0 }, line);
        Assert.Equal(0.80, CyclePositionProvider.High);

        var bars = CipherSrSpeechTests.BtcDaily().ToArray();
        var r = Btc();
        var tr = CyclePositionProvider.BuildTiers(bars, new Dictionary<string, object> { ["__symbol"] = "BTC/USD" });
        var subject = CycleLowConfidence.WeeklySubject(new CycleLowConfidence.Series(bars), tr.D, tr.W, tr.WeeklyGap);
        var chance = r[W + CyclePositionProvider.LowInSuffix];
        var first = subject.Select((c, t) => (c, t)).Where(x => x.c >= 0 && chance[x.t] >= 0.80).GroupBy(x => x.c).ToDictionary(g => g.Key, g => g.Min(x => x.t));
        var calls = first.Values.ToHashSet();
        string comp = CyclePositionProvider.CompWeeklyHigh, callLine = CyclePositionProvider.CompWeeklyHighCallLine;
        int marked = 0;
        for (int t = 0; t < bars.Length; t++)
        {
            bool m = calls.Contains(t) && chance[t] < 0.95;
            marked += m ? 1 : 0;
            Assert.Equal(m ? bars[t].Low : double.NaN, r[comp][t]);
            Assert.Equal(m ? chance[t] : double.NaN, r[comp + CyclePositionProvider.ShareSuffix][t]);
            Assert.Equal(calls.Contains(t) ? 100 * chance[t] : 0, r[callLine][t]);
        }
        Assert.True(marked > 20, $"{marked} marked");
        // More weekly lows get a high call than a very high one, and never a later one.
        var veryHigh = Marked(r[CyclePositionProvider.CompWeeklyVeryHigh]);
        Assert.True(calls.Count > veryHigh.Count, $"{calls.Count} high calls, {veryHigh.Count} very high");
        foreach (int v in veryHigh) Assert.True(first[subject[v]] <= v, $"very high on {v}, high on {first[subject[v]]}");

        var provider = new CyclePositionProvider();
        int at = Marked(r[comp]).Max();
        Assert.Equal($"Weekly low is in: high, {Math.Round(100 * chance[at]):0}%, for the low of {SpeechPriceFormatter.FormatPrice(bars[subject[at]].Low)}.",
            provider.GetComponentSpeech(comp, r[comp][at], bars[at], r, at));
        Assert.Equal($"weekly high call, {Math.Round(100 * chance[at]):0}%", provider.GetComponentSpeech(callLine, r[callLine][at], bars[at], r, at));
        Assert.Equal("no weekly high call on this bar", provider.GetComponentSpeech(callLine, 0, bars[at - 1], r, at - 1));
        Assert.StartsWith("weekly very high call, ", provider.GetComponentSpeech(CyclePositionProvider.CompWeeklyCallLine, 97, bars[at], r, at));
    }
}
