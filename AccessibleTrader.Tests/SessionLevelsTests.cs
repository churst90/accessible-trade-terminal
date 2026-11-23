using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// Previous day, week and month levels, and the session high, low and opening range (indicator
/// group 5, Cody 2026-11-22). Each value is worked out here longhand from the bars, not through the
/// provider's own helpers. The provider-wide contracts (causality, metadata, narration routes) reach
/// both through IndicatorProviderFixture.
/// </summary>
public class SessionLevelsTests
{
    private static Dictionary<string, double[]> Run(IIndicatorProvider p, string code, IReadOnlyList<Ohlcv> bars,
        Dictionary<string, object>? pars = null)
    {
        var rd = new Dictionary<string, double[]>();
        p.Calculate(code, bars.ToArray().AsSpan(), pars ?? new Dictionary<string, object>(), new IndicatorResultBuffer(rd, bars.Count));
        return rd;
    }

    private static string Detail(IIndicatorProvider p, string code, IReadOnlyList<Ohlcv> bars, int index,
        Dictionary<string, object>? pars = null)
    {
        pars ??= new Dictionary<string, object>();
        return p.GetDetailFact(code, bars.ToArray().AsSpan(), Run(p, code, bars, pars), index, pars);
    }

    private static readonly PreviousPeriodLevelsProvider Prev = new();
    private static readonly SessionRangeProvider Session = new();

    /// <summary>Bars every <paramref name="step"/> from <paramref name="t0"/>, wandering so that no two
    /// days share a high, a low or a close.</summary>
    private static List<Ohlcv> Bars(DateTime t0, TimeSpan step, int count)
        => Enumerable.Range(0, count).Select(i =>
        {
            double c = 100 + 10 * Math.Sin(i * 0.37) + i * 0.05;
            return new Ohlcv(t0 + step * i, c - 0.3, c + 0.5 + (i % 5) * 0.1, c - 0.5 - (i % 3) * 0.1, c, 100);
        }).ToList();

    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    /// <summary>The high, low and close of the bars in [from, to).</summary>
    private static (double H, double L, double C) Hlc(IReadOnlyList<Ohlcv> bars, DateTime from, DateTime to)
    {
        var span = bars.Where(b => b.Date >= from && b.Date < to).ToList();
        Assert.NotEmpty(span);
        return (span.Max(b => b.High), span.Min(b => b.Low), span[^1].Close);
    }

    // ── Previous day, week and month ──────────────────────────────────────────

    [Fact]
    public void Each_day_carries_the_whole_previous_days_high_low_and_close()
    {
        var t0 = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var bars = Bars(t0, Hour, 24 * 4);
        var r = Run(Prev, PreviousPeriodLevelsProvider.Code, bars);
        for (int day = 2; day < 4; day++)
        {
            var (h, l, c) = Hlc(bars, t0.AddDays(day - 1), t0.AddDays(day));
            for (int i = day * 24; i < day * 24 + 24; i++)
            {
                Assert.Equal(h, r["PDH"][i], 9);
                Assert.Equal(l, r["PDL"][i], 9);
                Assert.Equal(c, r["PDC"][i], 9);
            }
        }
    }

    [Fact]
    public void A_day_that_began_on_the_charts_first_bar_is_not_a_previous_day()
    {
        // Day 0 starts on bar 0, and the chart cannot know it had no bars before that, so day 1 has
        // no previous day: a level from part of a day would be a wrong price spoken as a level.
        var t0 = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var bars = Bars(t0, Hour, 24 * 3);
        var r = Run(Prev, PreviousPeriodLevelsProvider.Code, bars);
        for (int i = 0; i < 48; i++) Assert.True(double.IsNaN(r["PDH"][i]), $"bar {i}");
        Assert.False(double.IsNaN(r["PDH"][48]));
        Assert.Contains("needs a whole day of bars", Detail(Prev, PreviousPeriodLevelsProvider.Code, bars, 30));
    }

    [Fact]
    public void The_pivot_point_is_built_from_the_same_previous_day()
    {
        var t0 = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var bars = Bars(t0, Hour, 24 * 4);
        var prev = Run(Prev, PreviousPeriodLevelsProvider.Code, bars);
        var piv = Run(new PivotLevelsProvider(), PivotLevelsProvider.Code, bars, new() { ["Period"] = "Daily" });
        for (int i = 48; i < bars.Count; i++)
            Assert.Equal((prev["PDH"][i] + prev["PDL"][i] + prev["PDC"][i]) / 3, piv[PivotLevelsProvider.CompPP][i], 9);
    }

    [Fact]
    public void A_week_is_Monday_to_Sunday_across_New_Year()
    {
        // ISO week 1 of 2025 runs Mon 30 Dec 2024 to Sun 5 Jan 2025. Every bar in it carries the week
        // before (23 to 29 Dec); the week after carries it.
        var t0 = new DateTime(2024, 12, 16, 0, 0, 0, DateTimeKind.Utc);   // a Monday
        var bars = Bars(t0, Hour, 24 * 28);
        var r = Run(Prev, PreviousPeriodLevelsProvider.Code, bars);
        var w52 = Hlc(bars, new DateTime(2024, 12, 23, 0, 0, 0, DateTimeKind.Utc), new DateTime(2024, 12, 30, 0, 0, 0, DateTimeKind.Utc));
        var w1 = Hlc(bars, new DateTime(2024, 12, 30, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 6, 0, 0, 0, DateTimeKind.Utc));
        for (int i = 0; i < bars.Count; i++)
        {
            var d = bars[i].Date;
            if (d >= new DateTime(2024, 12, 30) && d < new DateTime(2025, 1, 6))
                Assert.True(w52.H == r["PWH"][i] && w52.L == r["PWL"][i] && w52.C == r["PWC"][i], $"week 1 bar {d:u}");
            else if (d >= new DateTime(2025, 1, 6) && d < new DateTime(2025, 1, 13))
                Assert.True(w1.H == r["PWH"][i] && w1.L == r["PWL"][i] && w1.C == r["PWC"][i], $"week 2 bar {d:u}");
        }
    }

    [Fact]
    public void A_month_carries_the_whole_previous_month()
    {
        var t0 = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc);
        var bars = Bars(t0, TimeSpan.FromHours(4), 6 * 75);   // to early April
        var r = Run(Prev, PreviousPeriodLevelsProvider.Code, bars);
        var feb = Hlc(bars, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        for (int i = 0; i < bars.Count; i++)
        {
            var d = bars[i].Date;
            if (d < new DateTime(2026, 3, 1)) Assert.True(double.IsNaN(r["PMH"][i]), $"{d:u}: January began before the chart");
            else if (d < new DateTime(2026, 4, 1))
                Assert.True(feb.H == r["PMH"][i] && feb.L == r["PML"][i] && feb.C == r["PMC"][i], $"{d:u}");
        }
    }

    [Fact]
    public void Only_the_days_lines_are_on_by_default()
    {
        var on = Prev.GetIndicators()[0].Components.Where(c => c.DefaultIsEnabled != false).Select(c => c.Name);
        Assert.Equal(new[] { "PDH", "PDL", "PDC", PreviousPeriodLevelsProvider.CompBrokeHigh, PreviousPeriodLevelsProvider.CompBrokeLow }, on);
    }

    [Fact]
    public void A_close_above_the_previous_days_high_is_marked_once_a_day()
    {
        // Day 1: highs to 110. Day 2: closes 105, 111 (break), 112 (no second mark), 108.
        // Day 3: the previous day is day 2 (high 112.5); 113 breaks it again.
        var t0 = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>();
        void Add(int day, int hour, double c, double h) => bars.Add(new Ohlcv(t0.AddDays(day).AddHours(hour), c, h, c - 2, c, 1));
        Add(0, 0, 100, 101);
        Add(1, 0, 100, 110); Add(1, 6, 104, 105);
        Add(2, 0, 105, 106); Add(2, 6, 111, 111.5); Add(2, 12, 112, 112.5); Add(2, 18, 108, 109);
        Add(3, 0, 109, 110); Add(3, 6, 113, 113.5);
        var r = Run(Prev, PreviousPeriodLevelsProvider.Code, bars);
        var up = r[PreviousPeriodLevelsProvider.CompBrokeHigh];
        Assert.Equal(new[] { 4, 8 }, Enumerable.Range(0, bars.Count).Where(i => !double.IsNaN(up[i])));
        Assert.Equal(111, up[4]);
        Assert.Equal(112.5, r["PDH"][8]);
        string said = Detail(Prev, PreviousPeriodLevelsProvider.Code, bars, 4);
        Assert.Contains("Previous day high 110", said);
        Assert.Contains("above its high", said);
        Assert.Contains("Closed above the previous day's high this bar.", said);
    }

    [Fact]
    public void A_close_below_the_previous_days_low_is_marked_too()
    {
        // Day 1's low is 95. Day 2 closes 95 (on it), 94 (below: marked), 93 (below again: not).
        var t0 = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>
        {
            new(t0, 100, 101, 99, 100, 1),
            new(t0.AddDays(1), 100, 101, 95, 100, 1),
            new(t0.AddDays(2), 100, 101, 94.8, 95, 1),          // on the low, not below it: no mark
            new(t0.AddDays(2).AddHours(1), 97, 98, 93, 94, 1),
            new(t0.AddDays(2).AddHours(2), 94, 95, 92, 93, 1),
        };
        var down = Run(Prev, PreviousPeriodLevelsProvider.Code, bars)[PreviousPeriodLevelsProvider.CompBrokeLow];
        Assert.Equal(new[] { 3 }, Enumerable.Range(0, bars.Count).Where(i => !double.IsNaN(down[i])));
        Assert.Contains("below its low", Detail(Prev, PreviousPeriodLevelsProvider.Code, bars, 3));
    }

    [Fact]
    public void The_lines_speak_their_names()
    {
        var bar = new Ohlcv(DateTime.UtcNow, 1, 1, 1, 1, 1);
        var none = new Dictionary<string, double[]>();
        Assert.Equal("Previous day high 105.00.", Prev.GetComponentSpeech("PDH", 105, bar, none, 0));
        Assert.Equal("Previous month close 99.50.", Prev.GetComponentSpeech("PMC", 99.5, bar, none, 0));
        Assert.Equal("Opening range low 98.25.", Session.GetComponentSpeech(SessionRangeProvider.CompOrLow, 98.25, bar, none, 0));
    }

    // ── Session high, low and opening range ───────────────────────────────────

    private static readonly TimeSpan Quarter = TimeSpan.FromMinutes(15);

    [Fact]
    public void The_session_high_and_low_run_from_the_days_first_bar()
    {
        var t0 = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var bars = Bars(t0, Quarter, 96 * 3);
        var r = Run(Session, SessionRangeProvider.Code, bars);
        for (int i = 0; i < 96; i++) Assert.True(double.IsNaN(r[SessionRangeProvider.CompHigh][i]), $"day 0 bar {i}");
        for (int day = 1; day < 3; day++)
            for (int i = day * 96; i < day * 96 + 96; i++)
            {
                var so = bars.Skip(day * 96).Take(i - day * 96 + 1).ToList();
                Assert.Equal(so.Max(b => b.High), r[SessionRangeProvider.CompHigh][i], 9);
                Assert.Equal(so.Min(b => b.Low), r[SessionRangeProvider.CompLow][i], 9);
            }
    }

    [Theory, InlineData(30), InlineData(60)]
    public void The_opening_range_is_the_first_minutes_and_draws_once_they_are_over(int minutes)
    {
        var t0 = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var bars = Bars(t0, Quarter, 96 * 3);
        var r = Run(Session, SessionRangeProvider.Code, bars, new() { [SessionRangeProvider.ParamMinutes] = minutes });
        int k = minutes / 15;
        for (int day = 1; day < 3; day++)
        {
            int s = day * 96;
            var range = bars.Skip(s).Take(k).ToList();
            for (int i = s; i < s + k; i++) Assert.True(double.IsNaN(r[SessionRangeProvider.CompOrHigh][i]), $"bar {i} still forming");
            for (int i = s + k; i < s + 96; i++)
            {
                Assert.Equal(range.Max(b => b.High), r[SessionRangeProvider.CompOrHigh][i], 9);
                Assert.Equal(range.Min(b => b.Low), r[SessionRangeProvider.CompOrLow][i], 9);
            }
        }
        Assert.Contains($"Opening range still forming, the first {minutes} minutes.", Detail(Session, SessionRangeProvider.Code, bars, 96,
            new() { [SessionRangeProvider.ParamMinutes] = minutes }));
    }

    [Fact]
    public void A_session_first_seen_after_its_range_ended_has_no_opening_range()
    {
        // Day 1's bars start at 02:00, an exchange gap: its high and low are real, its first 30
        // minutes are not on the chart.
        var t0 = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var bars = Bars(t0, Quarter, 96).Concat(Bars(t0.AddDays(1).AddHours(2), Quarter, 40)).ToList();
        var r = Run(Session, SessionRangeProvider.Code, bars);
        Assert.False(double.IsNaN(r[SessionRangeProvider.CompHigh][100]));
        Assert.All(Enumerable.Range(96, 40), i => Assert.True(double.IsNaN(r[SessionRangeProvider.CompOrHigh][i])));
        Assert.Contains("No opening range", Detail(Session, SessionRangeProvider.Code, bars, 110));
    }

    [Fact]
    public void A_break_of_the_opening_range_is_marked_once_each_way_a_session()
    {
        var t0 = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv> { new(t0.AddHours(12), 100, 101, 99, 100, 1) };
        void Add(int min, double c) => bars.Add(new Ohlcv(t0.AddDays(1).AddMinutes(min), c, c + 0.5, c - 0.5, c, 1));
        Add(0, 100); Add(15, 101);          // range: high 101.5, low 99.5
        Add(30, 101); Add(45, 102);         // 102 > 101.5: up (bar 4)
        Add(60, 103); Add(75, 99);          // 103 again above: no mark; 99 < 99.5: down (bar 6)
        Add(90, 98); Add(105, 102);         // neither again
        var r = Run(Session, SessionRangeProvider.Code, bars);
        Assert.Equal(new[] { 4 }, Enumerable.Range(0, bars.Count).Where(i => !double.IsNaN(r[SessionRangeProvider.CompBreakUp][i])));
        Assert.Equal(new[] { 6 }, Enumerable.Range(0, bars.Count).Where(i => !double.IsNaN(r[SessionRangeProvider.CompBreakDown][i])));
        Assert.Equal(102, r[SessionRangeProvider.CompBreakUp][4]);
        string said = Detail(Session, SessionRangeProvider.Code, bars, 4);
        Assert.Equal("Session high 102.50, low 99.50. Opening range high 101.50, low 99.50; close 0.500 above it. Broke above the opening range this bar.", said);
        Assert.Contains("Broke above the opening range this bar.", said);
    }

    [Theory]
    [InlineData(2026, 1, 13, 14, 30)]   // EST: 9:30 New York is 14:30 UTC
    [InlineData(2026, 7, 14, 13, 30)]   // EDT: 13:30 UTC
    public void The_New_York_session_is_half_past_nine_to_four_there(int y, int m, int d, int startHour, int startMin)
    {
        var open = new DateTime(y, m, d, startHour, startMin, 0, DateTimeKind.Utc);
        // The day before's session, so this one is not the chart's first, then 8:30 to 17:00 New York.
        var bars = Bars(open.AddDays(-1), Quarter, 4).Concat(Bars(open.AddHours(-1), Quarter, 34)).ToList();
        var r = Run(Session, SessionRangeProvider.Code, bars, new() { [SessionRangeProvider.ParamSession] = SessionRangeProvider.SessionNewYork });
        var hi = r[SessionRangeProvider.CompHigh];
        for (int i = 4; i < bars.Count; i++)
        {
            var et = bars[i].Date - open;   // time since 9:30 New York
            bool inSession = et >= TimeSpan.Zero && et < TimeSpan.FromHours(6.5);
            Assert.True(inSession != double.IsNaN(hi[i]), $"{bars[i].Date:u} in session {inSession}");
        }
        int first = bars.FindIndex(b => b.Date == open);
        Assert.Equal(bars[first].High, hi[first], 9);
        Assert.Equal(Math.Max(bars[first].High, bars[first + 1].High), r[SessionRangeProvider.CompOrHigh][first + 2], 9);
        Assert.Equal("Outside the New York session.", Detail(Session, SessionRangeProvider.Code, bars, 5,
            new() { [SessionRangeProvider.ParamSession] = SessionRangeProvider.SessionNewYork }));
    }

    [Fact]
    public void Hourly_bars_take_the_first_bar_as_a_thirty_minute_range()
    {
        // A bar that opens inside the range belongs to it; on hourly bars that is the first bar.
        var t0 = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var bars = Bars(t0, Hour, 72);
        var r = Run(Session, SessionRangeProvider.Code, bars);
        Assert.True(double.IsNaN(r[SessionRangeProvider.CompOrHigh][24]));
        Assert.Equal(bars[24].High, r[SessionRangeProvider.CompOrHigh][25], 9);
        Assert.Equal(bars[24].Low, r[SessionRangeProvider.CompOrLow][47], 9);
    }
}
