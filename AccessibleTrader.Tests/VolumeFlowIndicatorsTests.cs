using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// Cumulative volume delta, the Klinger oscillator, price volume trend and relative volume at time
/// (indicator group 3, Cody 2026-11-22). The provider-wide contracts (causality on a prefix and with
/// older bars prepended, metadata, narration routes) reach these through IndicatorProviderFixture;
/// this file pins what each one computes, where its totals start, and what it says.
/// </summary>
public class VolumeFlowIndicatorsTests
{
    private static Dictionary<string, double[]> Run(IIndicatorProvider p, string code, IReadOnlyList<Ohlcv> bars,
        Dictionary<string, object>? pars = null)
    {
        var rd = new Dictionary<string, double[]>();
        p.Calculate(code, bars.ToArray().AsSpan(), pars ?? new Dictionary<string, object>(), new IndicatorResultBuffer(rd, bars.Count));
        return rd;
    }

    private static readonly DateTime Midnight = new(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);   // a Monday

    private static Ohlcv Bar(DateTime t, double open, double close, double volume)
        => new(t, open, Math.Max(open, close) + 1, Math.Min(open, close) - 1, close, volume);

    // ── Cumulative volume delta ───────────────────────────────────────────────

    [Fact]
    public void Cvd_adds_up_candle_signed_volume_and_starts_again_each_day()
    {
        var bars = new List<Ohlcv>
        {
            Bar(Midnight.AddHours(0), 100, 101, 10),   // up candle: +10
            Bar(Midnight.AddHours(1), 101, 100, 4),    // down candle: -4
            Bar(Midnight.AddHours(2), 102, 102, 3),    // doji, above the last close: +3
            Bar(Midnight.AddHours(3), 102, 102, 5),    // doji, level with it: 0
            Bar(Midnight.AddHours(4), 102, 101, 20),   // -20
            Bar(Midnight.AddDays(1), 101, 100, 2),     // a new day: -2, not -13
            Bar(Midnight.AddDays(1).AddHours(1), 100, 103, 7),
        };
        var cvd = Run(new CumulativeVolumeDeltaProvider(), CumulativeVolumeDeltaProvider.Code, bars)[CumulativeVolumeDeltaProvider.CompCvd];
        Assert.Equal(new[] { 10.0, 6, 9, 9, -11, -2, 5 }, cvd);
    }

    [Fact]
    public void Cvd_leaves_a_day_the_chart_starts_partway_through_blank()
    {
        // The chart opens at 05:00: a total from there would not be the day's, and would change when
        // the hours before it load.
        var bars = Enumerable.Range(5, 22).Select(h => Bar(Midnight.AddHours(h), 100, 101, 1)).ToList();
        var cvd = Run(new CumulativeVolumeDeltaProvider(), CumulativeVolumeDeltaProvider.Code, bars)[CumulativeVolumeDeltaProvider.CompCvd];
        for (int i = 0; i < bars.Count; i++)
        {
            if (bars[i].Date.Day == Midnight.Day) Assert.True(double.IsNaN(cvd[i]), $"{bars[i].Date:HH:mm} on the first day has a total");
            else Assert.Equal(bars[i].Date.Hour + 1, cvd[i]);
        }
        Assert.Contains(cvd, v => !double.IsNaN(v));
    }

    [Fact]
    public void Cvd_anchor_none_adds_up_from_the_chart_s_first_bar_and_week_starts_on_monday()
    {
        // Sunday 23:00, then Monday: the week anchor resets between them, None does not.
        var sunday = Midnight.AddHours(-1);
        var bars = Enumerable.Range(0, 4).Select(h => Bar(sunday.AddHours(h), 100, 101, 1)).ToList();
        var none = Run(new CumulativeVolumeDeltaProvider(), CumulativeVolumeDeltaProvider.Code, bars,
            new() { [CumulativeVolumeDeltaProvider.ParamAnchor] = "None" })[CumulativeVolumeDeltaProvider.CompCvd];
        Assert.Equal(new[] { 1.0, 2, 3, 4 }, none);
        var week = Run(new CumulativeVolumeDeltaProvider(), CumulativeVolumeDeltaProvider.Code, bars,
            new() { [CumulativeVolumeDeltaProvider.ParamAnchor] = "Week" })[CumulativeVolumeDeltaProvider.CompCvd];
        Assert.True(double.IsNaN(week[0]));   // Sunday 23:00 is partway through a week
        Assert.Equal(new[] { 1.0, 2, 3 }, week.Skip(1));
    }

    [Theory]
    [InlineData(1.0 / 60, CumulativeVolumeDeltaProvider.Anchor.Day)]
    [InlineData(4, CumulativeVolumeDeltaProvider.Anchor.Day)]
    [InlineData(24, CumulativeVolumeDeltaProvider.Anchor.Month)]
    [InlineData(24 * 7, CumulativeVolumeDeltaProvider.Anchor.Year)]
    public void Cvd_auto_anchors_to_the_day_month_or_year_by_the_chart_s_bars(double hoursPerBar, CumulativeVolumeDeltaProvider.Anchor expected)
    {
        var bars = Enumerable.Range(0, 30).Select(i => Bar(Midnight.AddHours(i * hoursPerBar), 100, 101, 1)).ToArray();
        // A weekend gap lengthens one step (to over a day on the intraday charts); the shortest is the bar.
        if (hoursPerBar <= 24) for (int i = 5; i < bars.Length; i++) bars[i] = bars[i] with { Date = bars[i].Date.AddDays(2) };
        Assert.Equal(expected, CumulativeVolumeDeltaProvider.AutoAnchor(bars));
        CumulativeVolumeDeltaProvider.Compute(bars, CumulativeVolumeDeltaProvider.Anchor.Auto, out var resolved);
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData("Day", CumulativeVolumeDeltaProvider.Anchor.Day)]
    [InlineData("1D", CumulativeVolumeDeltaProvider.Anchor.Day)]
    [InlineData("session", CumulativeVolumeDeltaProvider.Anchor.Day)]
    [InlineData("1W", CumulativeVolumeDeltaProvider.Anchor.Week)]
    [InlineData("month", CumulativeVolumeDeltaProvider.Anchor.Month)]
    [InlineData("12M", CumulativeVolumeDeltaProvider.Anchor.Year)]
    [InlineData("none", CumulativeVolumeDeltaProvider.Anchor.None)]
    [InlineData("nonsense", CumulativeVolumeDeltaProvider.Anchor.Auto)]
    public void Cvd_reads_its_anchor_in_words_or_tradingview_s_spellings(string typed, CumulativeVolumeDeltaProvider.Anchor expected)
        => Assert.Equal(expected, CumulativeVolumeDeltaProvider.ReadAnchor(new() { [CumulativeVolumeDeltaProvider.ParamAnchor] = typed }));

    [Fact]
    public void Cvd_says_who_is_ahead_since_when_and_what_this_bar_added()
    {
        var bars = new List<Ohlcv>
        {
            Bar(Midnight, 100, 101, 1_500_000),
            Bar(Midnight.AddHours(1), 101, 100, 300_000),
        };
        var p = new CumulativeVolumeDeltaProvider();
        var r = Run(p, CumulativeVolumeDeltaProvider.Code, bars);
        var rr = r.ToDictionary(kv => kv.Key, kv => kv.Value);
        string detail = p.GetDetailFact(CumulativeVolumeDeltaProvider.Code, bars.ToArray(), rr, 1, new());
        Assert.Equal("Volume delta plus 1.2 million since the day opened: buyers ahead. This bar minus 300,000.", detail);
        Assert.Equal("Volume delta plus 1.2 million, buyers ahead.",
            p.GetComponentSpeech(CumulativeVolumeDeltaProvider.CompCvd, 1_200_000, bars[1], rr, 1));
        Assert.Equal("Volume delta minus 300,000, sellers ahead.",
            p.GetComponentSpeech(CumulativeVolumeDeltaProvider.CompCvd, -300_000, bars[1], rr, 1));

        var late = new List<Ohlcv> { Bar(Midnight.AddHours(9), 100, 101, 1), Bar(Midnight.AddHours(10), 100, 101, 1) };
        var lr = Run(p, CumulativeVolumeDeltaProvider.Code, late);
        Assert.Equal("Volume delta: the chart starts partway through this day, so it has no total yet.",
            p.GetDetailFact(CumulativeVolumeDeltaProvider.Code, late.ToArray(), lr, 1, new()));
    }

    // ── Klinger ───────────────────────────────────────────────────────────────

    /// <summary>
    /// TradingView's built-in written out longhand, with its own EMA: the signed volume by typical
    /// price, EMA 34 minus EMA 55, EMA 13 of that. An EMA here seeds from its first value and shows
    /// from its period-th, so the oscillator starts on bar 55 (the first bar has no change) and the
    /// signal on bar 67.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Klinger_is_tradingview_s_formula(int flavour)
    {
        var bars = CausalityProbeSeries.Bars(flavour, 600);
        var r = Run(new KlingerOscillatorProvider(), KlingerOscillatorProvider.Code, bars);

        double Ema(double prev, double x, int len) => double.IsNaN(prev) ? x : prev + 2.0 / (len + 1) * (x - prev);
        double f = double.NaN, s = double.NaN, sig = double.NaN;
        for (int i = 1; i < bars.Count; i++)
        {
            double tp = (bars[i].High + bars[i].Low + bars[i].Close) / 3, tp0 = (bars[i - 1].High + bars[i - 1].Low + bars[i - 1].Close) / 3;
            double sv = tp >= tp0 ? bars[i].Volume : -bars[i].Volume;
            f = Ema(f, sv, 34); s = Ema(s, sv, 55);
            if (i < 55) { Assert.True(double.IsNaN(r[KlingerOscillatorProvider.CompKlinger][i]), $"bar {i} has a Klinger"); continue; }
            double k = f - s;
            Assert.Equal(k, r[KlingerOscillatorProvider.CompKlinger][i], 6);
            sig = Ema(sig, k, 13);
            if (i < 67) Assert.True(double.IsNaN(r[KlingerOscillatorProvider.CompSignal][i]), $"bar {i} has a signal");
            else Assert.Equal(sig, r[KlingerOscillatorProvider.CompSignal][i], 6);
        }
        Assert.True(double.IsNaN(r[KlingerOscillatorProvider.CompKlinger][0]));
    }

    /// <summary>An unchanged typical price counts as up (TradingView's <c>&gt;= 0</c>): on a flat price
    /// with growing volume every bar is up volume, so the fast EMA leads and the oscillator is positive.
    /// Counted as down, it would be negative.</summary>
    [Fact]
    public void Klinger_counts_an_unchanged_typical_price_as_up()
    {
        var bars = Enumerable.Range(0, 80).Select(i => new Ohlcv(Midnight.AddHours(i), 100, 101, 99, 100, 100 + 10 * i)).ToList();
        var k = Run(new KlingerOscillatorProvider(), KlingerOscillatorProvider.Code, bars)[KlingerOscillatorProvider.CompKlinger];
        for (int i = 55; i < bars.Count; i++) Assert.True(k[i] > 0, $"bar {i}: {k[i]}");
    }

    [Fact]
    public void Klinger_marks_each_cross_of_its_signal_once_on_the_bar_that_made_it()
    {
        var bars = CausalityProbeSeries.Bars(0, 900);
        var r = Run(new KlingerOscillatorProvider(), KlingerOscillatorProvider.Code, bars);
        var k = r[KlingerOscillatorProvider.CompKlinger];
        var s = r[KlingerOscillatorProvider.CompSignal];
        int ups = 0, downs = 0;
        for (int i = 1; i < bars.Count; i++)
        {
            double was = k[i - 1] - s[i - 1], now = k[i] - s[i];
            bool up = was <= 0 && now > 0, down = was >= 0 && now < 0;
            Assert.Equal(up, !double.IsNaN(r[KlingerOscillatorProvider.CompCrossUp][i]));
            Assert.Equal(down, !double.IsNaN(r[KlingerOscillatorProvider.CompCrossDown][i]));
            if (up) { ups++; Assert.Equal(k[i], r[KlingerOscillatorProvider.CompCrossUp][i]); }
            if (down) downs++;
        }
        Assert.True(ups >= 5 && downs >= 5, $"{ups} up and {downs} down: the series never exercised the marks");
    }

    [Fact]
    public void Klinger_goes_above_zero_when_volume_comes_in_on_rising_bars_and_says_so()
    {
        // Falling for 80 bars on growing volume (a constant one would leave both EMAs equal and the
        // oscillator at zero), then rising on heavier volume.
        var t0 = Midnight;
        var bars = new List<Ohlcv>();
        for (int i = 0; i < 80; i++) bars.Add(Bar(t0.AddHours(i), 200 - i, 199 - i, 100 + 5 * i));
        for (int i = 0; i < 40; i++) bars.Add(Bar(t0.AddHours(80 + i), 120 + i, 121 + i, 300));
        var p = new KlingerOscillatorProvider();
        var r = Run(p, KlingerOscillatorProvider.Code, bars);
        Assert.True(r[KlingerOscillatorProvider.CompKlinger][79] < 0);
        Assert.True(r[KlingerOscillatorProvider.CompKlinger][119] > 0);
        int cross = Enumerable.Range(0, bars.Count).Single(i => !double.IsNaN(r[KlingerOscillatorProvider.CompCrossUp][i]));
        Assert.InRange(cross, 80, 90);
        string detail = p.GetDetailFact(KlingerOscillatorProvider.Code, bars.ToArray(), r, cross, new());
        Assert.StartsWith("Klinger ", detail);
        Assert.Contains("above its signal", detail);
        Assert.EndsWith(", crossed above it this bar.", detail);
        Assert.Equal("Klinger: not yet established.", p.GetDetailFact(KlingerOscillatorProvider.Code, bars.ToArray(), r, 10, new()));
    }

    // ── Price volume trend ────────────────────────────────────────────────────

    [Fact]
    public void Pvt_adds_each_bar_s_volume_times_its_percentage_change()
    {
        var bars = new List<Ohlcv>
        {
            Bar(Midnight, 100, 100, 1),
            Bar(Midnight.AddHours(1), 100, 110, 10),   // +10%: +1
            Bar(Midnight.AddHours(2), 110, 99, 20),    // -10%: -2
            Bar(Midnight.AddHours(3), 99, 99.99, 100), // +1%: +1, where OBV would add all 100
        };
        var p = new PriceVolumeTrendProvider();
        var r = Run(p, PriceVolumeTrendProvider.Code, bars);
        var pvt = r[PriceVolumeTrendProvider.CompPvt];
        Assert.True(double.IsNaN(pvt[0]));
        Assert.Equal(1.0, pvt[1], 9);
        Assert.Equal(-1.0, pvt[2], 9);
        Assert.Equal(0.0, pvt[3], 9);
        Assert.Equal("PVT minus 1.00.", p.GetComponentSpeech(PriceVolumeTrendProvider.CompPvt, -1, bars[2], r, 2));
    }

    [Fact]
    public void Pvt_says_how_far_it_moved_over_the_last_twenty_bars()
    {
        var bars = Enumerable.Range(0, 30).Select(i => Bar(Midnight.AddHours(i), 100 * Math.Pow(1.01, i), 100 * Math.Pow(1.01, i + 1), 1000)).ToList();
        // Each close is 1% above the one before: +10 a bar, 200 over twenty.
        var p = new PriceVolumeTrendProvider();
        var r = Run(p, PriceVolumeTrendProvider.Code, bars);
        Assert.Equal("PVT plus 290.00, up 200.00 over the last 20 bars.",
            p.GetDetailFact(PriceVolumeTrendProvider.Code, bars.ToArray(), r, 29, new()));
        Assert.Equal("PVT plus 50.00.", p.GetDetailFact(PriceVolumeTrendProvider.Code, bars.ToArray(), r, 5, new()));
    }

    // ── Relative volume at time ───────────────────────────────────────────────

    /// <summary>Hourly bars over <paramref name="days"/> days from midnight; 10:00 trades
    /// <paramref name="tenOClock"/>(day), every other hour 50.</summary>
    private static List<Ohlcv> Days(int days, Func<int, double> tenOClock, Func<int, int, bool>? skip = null)
    {
        var bars = new List<Ohlcv>();
        for (int d = 0; d < days; d++)
            for (int h = 0; h < 24; h++)
            {
                if (skip?.Invoke(d, h) == true) continue;
                bars.Add(Bar(Midnight.AddDays(d).AddHours(h), 100, 101, h == 10 ? tenOClock(d) : 50));
            }
        return bars;
    }

    private static double At(List<Ohlcv> bars, double[] values, int day, int hour)
        => values[bars.FindIndex(b => b.Date == Midnight.AddDays(day).AddHours(hour))];

    [Fact]
    public void RelVol_regular_compares_a_bar_with_the_same_time_on_the_last_ten_days()
    {
        // 10:00 is busy every day (200) and busier on day 11 (600). Against the bars just before, every
        // 10:00 would look unusual; against other days' 10:00, only day 11's does.
        var bars = Days(12, d => d == 11 ? 600 : 200);
        var r = Run(new RelativeVolumeAtTimeProvider(), RelativeVolumeAtTimeProvider.Code, bars,
            new() { [RelativeVolumeAtTimeProvider.ParamMode] = "Regular" })[RelativeVolumeAtTimeProvider.CompRatio];
        for (int d = 0; d < 10; d++) Assert.True(double.IsNaN(At(bars, r, d, 10)), $"day {d} has a ratio with under ten days behind it");
        Assert.Equal(1.0, At(bars, r, 10, 10), 9);
        Assert.Equal(3.0, At(bars, r, 11, 10), 9);
        Assert.Equal(1.0, At(bars, r, 11, 11), 9);
    }

    [Fact]
    public void RelVol_cumulative_compares_the_day_so_far_with_the_usual_by_this_time()
    {
        // Day 11's 10:00 has 600 against 200: by 10:00 the day has 10·50 + 600 = 1100 against the usual
        // 10·50 + 200 = 700, and by 11:00 1150 against 750.
        var bars = Days(12, d => d == 11 ? 600 : 200);
        var r = Run(new RelativeVolumeAtTimeProvider(), RelativeVolumeAtTimeProvider.Code, bars)[RelativeVolumeAtTimeProvider.CompRatio];
        Assert.Equal(1100.0 / 700, At(bars, r, 11, 10), 9);
        Assert.Equal(1150.0 / 750, At(bars, r, 11, 11), 9);
        Assert.Equal(1.0, At(bars, r, 11, 9), 9);
    }

    [Fact]
    public void RelVol_passes_over_a_day_with_no_bar_at_that_time_and_leaves_out_a_partial_first_day()
    {
        // Day 5 has no 10:00 bar: the average for day 11 is days 0..4 and 6..10 at 200... and day 0 is
        // the chart's first day only if it starts at midnight, so starting at 03:00 pushes it out too.
        var gap = Days(12, d => d == 11 ? 400 : 200, (d, h) => d == 5 && h == 10);
        var r = Run(new RelativeVolumeAtTimeProvider(), RelativeVolumeAtTimeProvider.Code, gap,
            new() { [RelativeVolumeAtTimeProvider.ParamMode] = "Regular" })[RelativeVolumeAtTimeProvider.CompRatio];
        Assert.True(double.IsNaN(At(gap, r, 10, 10)), "day 10 had nine days with a 10:00 bar, not ten");
        Assert.Equal(2.0, At(gap, r, 11, 10), 9);

        var late = Days(12, d => d == 0 ? 10_000 : d == 11 ? 400 : 200, (d, h) => d == 0 && h < 3);
        var lr = Run(new RelativeVolumeAtTimeProvider(), RelativeVolumeAtTimeProvider.Code, late,
            new() { [RelativeVolumeAtTimeProvider.ParamMode] = "Regular" })[RelativeVolumeAtTimeProvider.CompRatio];
        Assert.True(double.IsNaN(At(late, lr, 0, 12)));
        Assert.True(double.IsNaN(At(late, lr, 10, 10)), "the partial first day counted towards day 10");
        Assert.Equal(2.0, At(late, lr, 11, 10), 9);   // days 1..10, none of them day 0's 10,000
    }

    [Fact]
    public void RelVol_on_a_daily_chart_compares_with_the_bars_before()
    {
        var bars = Enumerable.Range(0, 15).Select(i => Bar(Midnight.AddDays(i), 100, 101, i == 12 ? 500 : 100)).ToList();
        var p = new RelativeVolumeAtTimeProvider();
        var r = Run(p, RelativeVolumeAtTimeProvider.Code, bars);
        var v = r[RelativeVolumeAtTimeProvider.CompRatio];
        Assert.True(double.IsNaN(v[9]));
        Assert.Equal(1.0, v[10], 9);
        Assert.Equal(5.0, v[12], 9);
        Assert.Equal(100.0 / 140, v[13], 9);   // day 12's 500 is in the ten before day 13
        Assert.Equal("Relative volume 5.00 times the average of the 10 bars before.",
            p.GetDetailFact(RelativeVolumeAtTimeProvider.Code, bars.ToArray(), r, 12, new()));
    }

    [Fact]
    public void RelVol_says_what_it_compared_with()
    {
        var bars = Days(12, d => d == 11 ? 600 : 200);
        var p = new RelativeVolumeAtTimeProvider();
        var r = Run(p, RelativeVolumeAtTimeProvider.Code, bars);
        int i = bars.FindIndex(b => b.Date == Midnight.AddDays(11).AddHours(10));
        Assert.Equal("Relative volume 1.57 times the usual volume by 10:00 over the last 10 days, counting today so far.",
            p.GetDetailFact(RelativeVolumeAtTimeProvider.Code, bars.ToArray(), r, i, new()));
        var reg = new Dictionary<string, object> { [RelativeVolumeAtTimeProvider.ParamMode] = "Regular" };
        var rr = Run(p, RelativeVolumeAtTimeProvider.Code, bars, reg);
        Assert.Equal("Relative volume 3.00 times the usual 10:00 bar over the last 10 days.",
            p.GetDetailFact(RelativeVolumeAtTimeProvider.Code, bars.ToArray(), rr, i, reg));
        Assert.Equal("Relative volume: needs 10 earlier days with a bar at 10:00.",
            p.GetDetailFact(RelativeVolumeAtTimeProvider.Code, bars.ToArray(), r, 10, new()));
        Assert.Equal("Relative volume 3.00 times usual.",
            p.GetComponentSpeech(RelativeVolumeAtTimeProvider.CompRatio, 3, bars[i], rr, i));
    }
}
