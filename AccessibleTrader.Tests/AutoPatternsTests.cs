using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// Auto Fib retracement and auto trend lines (indicator group 6, Cody 2026-11-22). The fixtures are
/// drawn by hand so the swings, pivots and lines can be worked out on paper; the expected values
/// below are those, not the provider's helpers. The provider-wide contracts (causality, metadata,
/// narration routes) reach both through IndicatorProviderFixture.
/// </summary>
public class AutoPatternsTests
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

    private static readonly AutoFibProvider Fib = new();
    private static readonly AutoTrendLinesProvider Trend = new();
    private static readonly DateTime T0 = new(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);

    // ── Auto Fib ──────────────────────────────────────────────────────────────

    /// <summary>Bars a point either side of a middle that rises 100 → 120 (bars 0-10), falls to 105
    /// (bar 20), rises to 125 (bar 30) and falls to 110 (bar 40). At 5 percent the ZigZag's turns are
    /// the low 99.5 at bar 0 (known at bar 2), the high 120.5 at bar 10 (known at bar 14, the first
    /// low at or under 120.5 × 0.95), the low 104.5 at bar 20 (known at bar 23, the first high at or
    /// over 104.5 × 1.05).</summary>
    private static List<Ohlcv> Swings()
    {
        double Mid(int i) => i <= 10 ? 100 + 2 * i : i <= 20 ? 120 - 1.5 * (i - 10)
                           : i <= 30 ? 105 + 2 * (i - 20) : 125 - 1.5 * (i - 30);
        return Enumerable.Range(0, 41).Select(i =>
            new Ohlcv(T0.AddHours(i), Mid(i), Mid(i) + 0.5, Mid(i) - 0.5, Mid(i), 100)).ToList();
    }

    [Fact]
    public void Fib_levels_are_not_drawn_until_a_swing_has_ended()
    {
        var r = Run(Fib, AutoFibProvider.Code, Swings());
        // The high at bar 10 is only known at bar 14: nothing before then could have drawn from it.
        for (int c = 0; c < 14; c++)
            foreach (var l in AutoFibProvider.Levels) Assert.True(double.IsNaN(r[l.Name][c]), $"{l.Name} at {c}");
    }

    [Fact]
    public void Fib_levels_measure_the_swing_up_back_from_its_high()
    {
        var r = Run(Fib, AutoFibProvider.Code, Swings());
        // Swing up 99.5 → 120.5, 21 points: 0 at the high, 100 at the low.
        for (int c = 14; c < 23; c++)
        {
            Assert.Equal(120.5, r["Fib0"][c], 9);
            Assert.Equal(120.5 - 0.236 * 21, r["Fib236"][c], 9);
            Assert.Equal(120.5 - 0.382 * 21, r["Fib382"][c], 9);
            Assert.Equal(110, r["Fib500"][c], 9);
            Assert.Equal(120.5 - 0.618 * 21, r["Fib618"][c], 9);
            Assert.Equal(120.5 - 0.786 * 21, r["Fib786"][c], 9);
            Assert.Equal(99.5, r["Fib100"][c], 9);
        }
    }

    [Fact]
    public void Fib_levels_are_redrawn_on_the_swing_down_once_its_low_is_known()
    {
        var r = Run(Fib, AutoFibProvider.Code, Swings());
        // Swing down 120.5 → 104.5, 16 points, from bar 23: 0 at the low, 38.2 above it.
        Assert.Equal(104.5, r["Fib0"][23], 9);
        Assert.Equal(104.5 + 0.382 * 16, r["Fib382"][23], 9);
        Assert.Equal(104.5 + 0.618 * 16, r["Fib618"][23], 9);
        Assert.Equal(120.5, r["Fib100"][23], 9);
    }

    [Fact]
    public void Each_new_swing_is_marked_once_on_the_bar_it_is_known()
    {
        var r = Run(Fib, AutoFibProvider.Code, Swings());
        var up = Enumerable.Range(0, 41).Where(c => !double.IsNaN(r[AutoFibProvider.CompNewUp][c])).ToList();
        var down = Enumerable.Range(0, 41).Where(c => !double.IsNaN(r[AutoFibProvider.CompNewDown][c])).ToList();
        Assert.Equal(new[] { 14, 34 }, up);      // the high 125.5 at bar 30 is known at the first low ≤ 119.225
        Assert.Equal(new[] { 23 }, down);
        Assert.Equal(Swings()[14].Close, r[AutoFibProvider.CompNewUp][14]);
    }

    [Fact]
    public void Fib_reversal_percent_is_the_setting()
    {
        // At 30 percent no move in the fixture is a swing.
        var r = Run(Fib, AutoFibProvider.Code, Swings(), new() { [AutoFibProvider.ParamPercent] = 30.0 });
        Assert.All(r["Fib500"], v => Assert.True(double.IsNaN(v)));
    }

    [Fact]
    public void Fib_detail_says_how_far_the_close_has_retraced()
    {
        // Bar 17 closes 109.5: (120.5 − 109.5) / 21 = 52.4 percent back.
        Assert.Equal("Last swing up from 99.50 to 120.50, 7 bars since it ended. Close has retraced 52.4 percent, between 50 and 61.8.",
            Detail(Fib, AutoFibProvider.Code, Swings(), 17));
        Assert.StartsWith("Auto Fib: no finished swing of 5 percent yet", Detail(Fib, AutoFibProvider.Code, Swings(), 13));
    }

    [Fact]
    public void Fib_levels_speak_their_name_and_price()
    {
        Assert.Equal("Fib 61.8 107.52.", Fib.GetComponentSpeech("Fib618", 107.522, default, new Dictionary<string, double[]>(), 0));
        Assert.Null(Fib.GetComponentSpeech(AutoFibProvider.CompNewUp, 1, default, new Dictionary<string, double[]>(), 0));
    }

    // ── Auto trend lines ──────────────────────────────────────────────────────

    /// <summary>The highs, for 2-bar pivots: pivot highs 120 at bar 3 (known at bar 5) and 115 at bar 9
    /// (known at bar 11), so a falling line from bar 11 of 5/6 a bar. Bar 16 closes 111, over the
    /// line's 115 − 5/6 × 7 = 109.17. Bars 17-19 climb, so bar 16 is no pivot.</summary>
    private static readonly double[] Highs =
        { 100, 105, 110, 120, 110, 105, 100, 105, 110, 115, 108, 104, 102, 101, 100, 100, 112, 113, 114, 115 };

    private static readonly Dictionary<string, object> TwoBars = new() { [AutoTrendLinesProvider.ParamBars] = 2 };

    /// <summary>The highs above, lows flat (no pivot lows), closes a point under the high.</summary>
    private static List<Ohlcv> Falling()
        => Highs.Select((h, i) => new Ohlcv(T0.AddHours(i), h - 1, h, 90, h - 1, 100)).ToList();

    /// <summary>The same turned upside down: pivot lows 100 at bar 3 and 105 at bar 9, a rising line,
    /// closes a point over the low, bar 16 closing 109 under the line's 110.83.</summary>
    private static List<Ohlcv> Rising()
        => Highs.Select((h, i) => new Ohlcv(T0.AddHours(i), 221 - h, 230, 220 - h, 221 - h, 100)).ToList();

    [Fact]
    public void Resistance_runs_through_two_falling_pivot_highs_from_the_bar_the_second_is_known()
    {
        var r = Run(Trend, AutoTrendLinesProvider.Code, Falling(), TwoBars);
        var res = r[AutoTrendLinesProvider.CompResistance];
        for (int c = 0; c < 11; c++) Assert.True(double.IsNaN(res[c]), $"drawn at {c}, before the pivot at 9 was known");
        for (int c = 11; c <= 16; c++) Assert.Equal(115 - 5.0 / 6 * (c - 9), res[c], 9);
        Assert.All(r[AutoTrendLinesProvider.CompSupport], v => Assert.True(double.IsNaN(v)));
    }

    [Fact]
    public void A_close_through_resistance_is_marked_once_and_ends_the_line()
    {
        var r = Run(Trend, AutoTrendLinesProvider.Code, Falling(), TwoBars);
        var marks = Enumerable.Range(0, Highs.Length).Where(c => !double.IsNaN(r[AutoTrendLinesProvider.CompBrokeUp][c])).ToList();
        Assert.Equal(new[] { 16 }, marks);
        Assert.Equal(111, r[AutoTrendLinesProvider.CompBrokeUp][16]);
        for (int c = 17; c < Highs.Length; c++) Assert.True(double.IsNaN(r[AutoTrendLinesProvider.CompResistance][c]));
    }

    [Fact]
    public void Support_runs_through_two_rising_pivot_lows_and_breaks_on_a_close_under_it()
    {
        var r = Run(Trend, AutoTrendLinesProvider.Code, Rising(), TwoBars);
        var sup = r[AutoTrendLinesProvider.CompSupport];
        for (int c = 0; c < 11; c++) Assert.True(double.IsNaN(sup[c]));
        for (int c = 11; c <= 16; c++) Assert.Equal(105 + 5.0 / 6 * (c - 9), sup[c], 9);
        var marks = Enumerable.Range(0, Highs.Length).Where(c => !double.IsNaN(r[AutoTrendLinesProvider.CompBrokeDown][c])).ToList();
        Assert.Equal(new[] { 16 }, marks);
        Assert.All(r[AutoTrendLinesProvider.CompResistance], v => Assert.True(double.IsNaN(v)));
    }

    [Fact]
    public void Rising_pivot_highs_draw_no_resistance()
    {
        // The second high above the first: a line through them is not resistance.
        var h = (double[])Highs.Clone();
        h[9] = 125; h[8] = 121;
        var bars = h.Select((x, i) => new Ohlcv(T0.AddHours(i), x - 1, x, 90, x - 1, 100)).ToList();
        var r = Run(Trend, AutoTrendLinesProvider.Code, bars, TwoBars);
        Assert.All(r[AutoTrendLinesProvider.CompResistance], v => Assert.True(double.IsNaN(v)));
    }

    [Fact]
    public void Falling_pivot_lows_draw_no_support()
    {
        var bars = Rising();
        bars[9] = bars[9] with { Low = 95, Open = 96, Close = 96 };   // under the first pivot low's 100
        var r = Run(Trend, AutoTrendLinesProvider.Code, bars, TwoBars);
        Assert.All(r[AutoTrendLinesProvider.CompSupport], v => Assert.True(double.IsNaN(v)));
    }

    [Fact]
    public void Pivot_bars_is_the_setting()
    {
        // At the default 10 bars the 20-bar fixture has no pivot at all.
        var r = Run(Trend, AutoTrendLinesProvider.Code, Falling());
        Assert.All(r[AutoTrendLinesProvider.CompResistance], v => Assert.True(double.IsNaN(v)));
    }

    [Fact]
    public void Trend_line_detail_names_the_pivots_the_slope_and_the_close()
    {
        // Bar 13: the line is at 115 − 5/6 × 4 = 111.67, the close 100.
        Assert.Equal("Resistance line falling 0.833 a bar from 120.00 and 115.00, now 111.67; close 11.67 below it. " +
                     "No support line: the last two pivot lows fell, or there are not two yet.",
            Detail(Trend, AutoTrendLinesProvider.Code, Falling(), 13, TwoBars));
        Assert.StartsWith("Resistance line broken 2 bars ago at 109.17",
            Detail(Trend, AutoTrendLinesProvider.Code, Falling(), 18, TwoBars));
    }

    [Fact]
    public void Trend_lines_speak_their_name_and_price()
    {
        Assert.Equal("Trend line support 105.00.",
            Trend.GetComponentSpeech(AutoTrendLinesProvider.CompSupport, 105, default, new Dictionary<string, double[]>(), 0));
    }
}
