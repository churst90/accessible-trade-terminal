using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// Linear regression channel, Chande Kroll Stop, McGinley Dynamic, Envelopes and the Moving Average
/// Ribbon (indicator group 4, second half, Cody 2026-11-22). Each is checked against the formula
/// written out longhand here, a bar at a time, not against a second call into the same helpers. The
/// provider-wide contracts (causality, metadata, narration routes) reach them through
/// IndicatorProviderFixture.
/// </summary>
public class TrendOverlaysTests
{
    private static Dictionary<string, double[]> Run(IIndicatorProvider p, string code, IReadOnlyList<Ohlcv> bars,
        Dictionary<string, object>? pars = null)
    {
        var rd = new Dictionary<string, double[]>();
        p.Calculate(code, bars.ToArray().AsSpan(), pars ?? new Dictionary<string, object>(), new IndicatorResultBuffer(rd, bars.Count));
        return rd;
    }

    private static readonly DateTime T0 = new(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);

    private static List<Ohlcv> Closes(params double[] closes)
        => closes.Select((c, i) => new Ohlcv(T0.AddHours(i), c, c + 1, c - 1, c, 100)).ToList();

    private static string Detail(IIndicatorProvider p, string code, IReadOnlyList<Ohlcv> bars, int index,
        Dictionary<string, object>? pars = null)
    {
        pars ??= new Dictionary<string, object>();
        var r = Run(p, code, bars, pars);
        return p.GetDetailFact(code, bars.ToArray().AsSpan(), r, index, pars);
    }

    private static void Same(double expected, double actual, string what)
    {
        if (double.IsNaN(expected)) Assert.True(double.IsNaN(actual), $"{what}: expected no value, got {actual}");
        else Assert.True(Math.Abs(expected - actual) <= 1e-9 * Math.Max(1, Math.Abs(expected)), $"{what}: expected {expected}, got {actual}");
    }

    private static double Sma(IReadOnlyList<Ohlcv> b, int i, int len)
    {
        if (i < len - 1) return double.NaN;
        double s = 0;
        for (int j = i - len + 1; j <= i; j++) s += b[j].Close;
        return s / len;
    }

    // ── Linear regression channel ─────────────────────────────────────────────

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void Regression_channel_is_a_least_squares_line_with_residual_deviation_bands(int flavour)
    {
        var bars = CausalityProbeSeries.Bars(flavour, 300);
        const int len = 30;
        var r = Run(new LinearRegressionChannelProvider(), LinearRegressionChannelProvider.Code, bars,
            new() { [LinearRegressionChannelProvider.ParamLength] = len, [LinearRegressionChannelProvider.ParamDev] = 2.0 });
        for (int i = 0; i < bars.Count; i++)
        {
            if (i < len - 1)
            {
                Assert.True(double.IsNaN(r[LinearRegressionChannelProvider.CompCentre][i]), $"bar {i} drawn early");
                continue;
            }
            // Fit by the means: slope = Σ(x − x̄)(y − ȳ) / Σ(x − x̄)².
            double mx = (len - 1) / 2.0, my = 0;
            for (int x = 0; x < len; x++) my += bars[i - len + 1 + x].Close;
            my /= len;
            double cxy = 0, cxx = 0, cyy = 0;
            for (int x = 0; x < len; x++)
            {
                double dy = bars[i - len + 1 + x].Close - my;
                cxy += (x - mx) * dy; cxx += (x - mx) * (x - mx); cyy += dy * dy;
            }
            double slope = cxy / cxx, icpt = my - slope * mx;
            double ss = 0;
            for (int x = 0; x < len; x++) { double e = bars[i - len + 1 + x].Close - (icpt + slope * x); ss += e * e; }
            double sd = Math.Sqrt(ss / (len - 1)), end = icpt + slope * (len - 1);
            double rr = cyy == 0 ? 0 : Math.Abs(cxy) / Math.Sqrt(cxx * cyy);
            Assert.True(Math.Abs(end - r[LinearRegressionChannelProvider.CompCentre][i]) < 1e-7, $"centre, bar {i}");
            Assert.True(Math.Abs(end + 2 * sd - r[LinearRegressionChannelProvider.CompUpper][i]) < 1e-6, $"upper, bar {i}");
            Assert.True(Math.Abs(end - 2 * sd - r[LinearRegressionChannelProvider.CompLower][i]) < 1e-6, $"lower, bar {i}");
            Assert.True(Math.Abs(slope - r[LinearRegressionChannelProvider.CompSlope][i]) < 1e-9, $"slope, bar {i}");
            Assert.True(Math.Abs(rr - r[LinearRegressionChannelProvider.CompFit][i]) < 1e-9, $"fit R, bar {i}");
        }
    }

    [Fact]
    public void Regression_channel_on_a_straight_line_is_the_line_with_no_width()
    {
        var bars = Closes(Enumerable.Range(0, 150).Select(i => 100.0 + 2 * i).ToArray());
        var r = Run(new LinearRegressionChannelProvider(), LinearRegressionChannelProvider.Code, bars);
        Assert.True(double.IsNaN(r[LinearRegressionChannelProvider.CompCentre][98]));
        Assert.Equal(bars[149].Close, r[LinearRegressionChannelProvider.CompCentre][149], 6);
        Assert.Equal(2.0, r[LinearRegressionChannelProvider.CompSlope][149], 9);
        Assert.Equal(1.0, r[LinearRegressionChannelProvider.CompFit][149], 9);
        Assert.Equal(r[LinearRegressionChannelProvider.CompCentre][149], r[LinearRegressionChannelProvider.CompUpper][149], 6);
    }

    [Fact]
    public void Regression_channel_marks_the_close_breaking_out_of_the_top_and_says_where_it_is()
    {
        // A gentle rise with a wobble, then one bar far above it.
        var closes = Enumerable.Range(0, 40).Select(i => 100.0 + i + (i % 2 == 0 ? 1 : -1)).Append(200.0).ToArray();
        var bars = Closes(closes);
        var pars = new Dictionary<string, object> { [LinearRegressionChannelProvider.ParamLength] = 20 };
        var r = Run(new LinearRegressionChannelProvider(), LinearRegressionChannelProvider.Code, bars, pars);
        Assert.Equal(200.0, r[LinearRegressionChannelProvider.CompAbove][40]);
        Assert.All(Enumerable.Range(0, 40), i => Assert.True(double.IsNaN(r[LinearRegressionChannelProvider.CompAbove][i])));

        var d = Detail(new LinearRegressionChannelProvider(), LinearRegressionChannelProvider.Code, bars, 39, pars);
        Assert.StartsWith("Regression channel over 20 bars: centre ", d);
        Assert.Contains("rising", d);
        Assert.Contains(", fit R ", d);
        Assert.Contains("deviations", d);
        Assert.Contains("Closed above the channel this bar.",
            Detail(new LinearRegressionChannelProvider(), LinearRegressionChannelProvider.Code, bars, 40, pars));
    }

    // ── Chande Kroll Stop ──────────────────────────────────────────────────────

    /// <summary>The probe series open each bar inside the last one's range, so their true range is
    /// always the high minus the low. These gap: each bar opens 6 to 14 away from the last close.</summary>
    private static List<Ohlcv> Gapped(int count)
        => Closes(Enumerable.Range(0, count).Select(i => 200.0 + (i % 7) * 6 * (i % 2 == 0 ? 1 : -1) + i * 0.5).ToArray());

    [Theory, InlineData(0), InlineData(1), InlineData(2), InlineData(-1)]
    public void Chande_kroll_is_the_extreme_of_the_atr_stops_over_the_stop_length(int flavour)
    {
        var bars = flavour < 0 ? Gapped(120) : CausalityProbeSeries.Bars(flavour, 300);
        const int p = 10, q = 9;
        const double x = 1.0;
        var r = Run(new ChandeKrollStopProvider(), ChandeKrollStopProvider.Code, bars);

        // Wilder's ATR, seeded with the plain average of the first p true ranges.
        var atr = new double[bars.Count];
        double sum = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            double tr = i == 0 ? bars[i].High - bars[i].Low
                : new[] { bars[i].High - bars[i].Low, Math.Abs(bars[i].High - bars[i - 1].Close), Math.Abs(bars[i].Low - bars[i - 1].Close) }.Max();
            if (i < p) { sum += tr; atr[i] = i == p - 1 ? sum / p : double.NaN; }
            else atr[i] = (atr[i - 1] * (p - 1) + tr) / p;
        }
        double FirstLong(int i) => i < p - 1 ? double.NaN
            : Enumerable.Range(i - p + 1, p).Max(j => bars[j].High) - x * atr[i];
        double FirstShort(int i) => i < p - 1 ? double.NaN
            : Enumerable.Range(i - p + 1, p).Min(j => bars[j].Low) + x * atr[i];
        for (int i = 0; i < bars.Count; i++)
        {
            double lng = i < p - 1 + q - 1 ? double.NaN : Enumerable.Range(i - q + 1, q).Max(FirstLong);
            double sht = i < p - 1 + q - 1 ? double.NaN : Enumerable.Range(i - q + 1, q).Min(FirstShort);
            Same(lng, r[ChandeKrollStopProvider.CompLong][i], $"long stop, bar {i}");
            Same(sht, r[ChandeKrollStopProvider.CompShort][i], $"short stop, bar {i}");
        }
        Assert.False(double.IsNaN(r[ChandeKrollStopProvider.CompLong][17]));
    }

    [Fact]
    public void Chande_kroll_marks_the_close_falling_through_the_long_stop()
    {
        var closes = Enumerable.Range(0, 40).Select(i => 100.0 + i).Append(80.0).ToArray();
        var bars = Closes(closes);
        var r = Run(new ChandeKrollStopProvider(), ChandeKrollStopProvider.Code, bars);
        Assert.Equal(80.0, r[ChandeKrollStopProvider.CompBelowLong][40]);
        Assert.Equal(1, r[ChandeKrollStopProvider.CompBelowLong].Count(v => !double.IsNaN(v)));
        var d = Detail(new ChandeKrollStopProvider(), ChandeKrollStopProvider.Code, bars, 40);
        Assert.StartsWith("Long stop ", d);
        Assert.Contains("below it. Short stop ", d);
        Assert.Contains("Closed below the long stop this bar.", d);
    }

    // ── McGinley Dynamic ───────────────────────────────────────────────────────

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void McGinley_follows_the_dynamic_formula_from_an_ema_seed(int flavour)
    {
        var bars = CausalityProbeSeries.Bars(flavour, 400);
        const int len = 14;
        var r = Run(new McGinleyDynamicProvider(), McGinleyDynamicProvider.Code, bars);
        double ema = double.NaN, mg = double.NaN;
        for (int i = 0; i < bars.Count; i++)
        {
            double c = bars[i].Close;
            ema = double.IsNaN(ema) ? c : ema + 2.0 / (len + 1) * (c - ema);
            double emaShown = i < len - 1 ? double.NaN : ema;
            if (double.IsNaN(mg)) mg = emaShown;
            else
            {
                double k = 1.0 / (len * Math.Pow(c / mg, 4));
                mg += (c - mg) * Math.Min(1.0, k);
            }
            Same(mg, r[McGinleyDynamicProvider.CompLine][i], $"McGinley, bar {i}");
        }
        Assert.True(double.IsNaN(r[McGinleyDynamicProvider.CompLine][12]));
        Assert.All(r[McGinleyDynamicProvider.CompLine].Skip(13), v => Assert.True(double.IsFinite(v)));
    }

    [Fact]
    public void McGinley_after_a_crash_goes_to_the_close_instead_of_diverging()
    {
        // Uncapped, a 70% fall makes the step 1 / (14 · 0.3⁴) ≈ 8.8 times the gap: the line lands
        // far below zero and the next bars run it to minus infinity.
        var closes = Enumerable.Repeat(100.0, 30).Append(30.0).Concat(Enumerable.Repeat(31.0, 10)).ToArray();
        var r = Run(new McGinleyDynamicProvider(), McGinleyDynamicProvider.Code, Closes(closes));
        Assert.Equal(30.0, r[McGinleyDynamicProvider.CompLine][30], 9);
        Assert.All(r[McGinleyDynamicProvider.CompLine].Skip(13), v => Assert.True(v > 0 && double.IsFinite(v)));
    }

    [Fact]
    public void McGinley_marks_the_close_crossing_it_and_says_so()
    {
        var closes = Enumerable.Repeat(100.0, 20).Concat(Enumerable.Range(1, 5).Select(i => 100.0 - i)).Append(110.0).ToArray();
        var bars = Closes(closes);
        var r = Run(new McGinleyDynamicProvider(), McGinleyDynamicProvider.Code, bars);
        Assert.False(double.IsNaN(r[McGinleyDynamicProvider.CompCrossDown][20]));
        Assert.Equal(110.0, r[McGinleyDynamicProvider.CompCrossUp][25]);
        var d = Detail(new McGinleyDynamicProvider(), McGinleyDynamicProvider.Code, bars, 25);
        Assert.StartsWith("McGinley ", d);
        Assert.Contains("above it. Crossed above it this bar.", d);
        Assert.Equal("McGinley Dynamic: needs 14 bars.", Detail(new McGinleyDynamicProvider(), McGinleyDynamicProvider.Code, bars, 5));
    }

    // ── Envelopes ──────────────────────────────────────────────────────────────

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void Envelopes_are_the_average_ten_percent_up_and_down(int flavour)
    {
        var bars = CausalityProbeSeries.Bars(flavour, 200);
        var r = Run(new EnvelopesProvider(), EnvelopesProvider.Code, bars);
        for (int i = 0; i < bars.Count; i++)
        {
            double b = Sma(bars, i, 20);
            Same(b, r[EnvelopesProvider.CompBasis][i], $"basis, bar {i}");
            Same(b * 1.1, r[EnvelopesProvider.CompUpper][i], $"upper, bar {i}");
            Same(b * 0.9, r[EnvelopesProvider.CompLower][i], $"lower, bar {i}");
        }
    }

    [Fact]
    public void Envelopes_take_an_exponential_average_and_another_percent()
    {
        var bars = CausalityProbeSeries.Bars(1, 200);
        var r = Run(new EnvelopesProvider(), EnvelopesProvider.Code, bars,
            new() { [EnvelopesProvider.ParamType] = "ema", [EnvelopesProvider.ParamPercent] = 2.5, [EnvelopesProvider.ParamLength] = 10 });
        double ema = double.NaN;
        for (int i = 0; i < bars.Count; i++)
        {
            ema = double.IsNaN(ema) ? bars[i].Close : ema + 2.0 / 11 * (bars[i].Close - ema);
            double shown = i < 9 ? double.NaN : ema;
            Same(shown, r[EnvelopesProvider.CompBasis][i], $"basis, bar {i}");
            Same(shown * 1.025, r[EnvelopesProvider.CompUpper][i], $"upper, bar {i}");
        }
    }

    [Fact]
    public void Envelopes_say_how_far_up_the_band_the_close_is_and_mark_a_breakout()
    {
        var closes = Enumerable.Repeat(100.0, 20).Append(105.0).Append(120.0).ToArray();
        var bars = Closes(closes);
        var r = Run(new EnvelopesProvider(), EnvelopesProvider.Code, bars);
        Assert.Equal(120.0, r[EnvelopesProvider.CompAbove][21]);
        Assert.True(double.IsNaN(r[EnvelopesProvider.CompAbove][20]));
        // Bar 20: basis 100.25, so the band is 90.225 to 110.275 and 105 sits 74 percent of the way up.
        Assert.Contains("Close inside, 74 percent of the way up.", Detail(new EnvelopesProvider(), EnvelopesProvider.Code, bars, 20));
        var d = Detail(new EnvelopesProvider(), EnvelopesProvider.Code, bars, 21);
        Assert.Contains("above the upper line. Broke out of the top this bar.", d);
    }

    // ── Moving average ribbon ─────────────────────────────────────────────────

    [Fact]
    public void Ribbon_is_four_simple_averages_of_the_close()
    {
        var bars = CausalityProbeSeries.Bars(2, 400);
        var r = Run(new MovingAverageRibbonProvider(), MovingAverageRibbonProvider.Code, bars);
        int[] lens = { 20, 50, 100, 200 };
        for (int k = 0; k < 4; k++)
            for (int i = 0; i < bars.Count; i++)
                Same(Sma(bars, i, lens[k]), r[MovingAverageRibbonProvider.Lines[k]][i], $"average {k + 1}, bar {i}");
    }

    [Fact]
    public void Ribbon_marks_the_bar_it_first_stacks_up_by_length_whatever_slot_each_length_is_in()
    {
        var bars = Closes(Enumerable.Range(0, 260).Select(i => 100.0 + i).ToArray());
        var r = Run(new MovingAverageRibbonProvider(), MovingAverageRibbonProvider.Code, bars);
        // Rising: the 20 is above the 50 above the 100 above the 200 as soon as the 200 exists.
        Assert.Equal(bars[199].Close, r[MovingAverageRibbonProvider.CompStackedUp][199]);
        Assert.Equal(1, r[MovingAverageRibbonProvider.CompStackedUp].Count(v => !double.IsNaN(v)));

        var reversed = new Dictionary<string, object>
        {
            [MovingAverageRibbonProvider.LengthParams[0]] = 200, [MovingAverageRibbonProvider.LengthParams[1]] = 100,
            [MovingAverageRibbonProvider.LengthParams[2]] = 50, [MovingAverageRibbonProvider.LengthParams[3]] = 20,
        };
        var rr = Run(new MovingAverageRibbonProvider(), MovingAverageRibbonProvider.Code, bars, reversed);
        Assert.Equal(bars[199].Close, rr[MovingAverageRibbonProvider.CompStackedUp][199]);
        Assert.True(rr[MovingAverageRibbonProvider.CompStackedDown].All(double.IsNaN));

        var falling = Closes(Enumerable.Range(0, 260).Select(i => 400.0 - i).ToArray());
        var rf = Run(new MovingAverageRibbonProvider(), MovingAverageRibbonProvider.Code, falling);
        Assert.Equal(falling[199].Close, rf[MovingAverageRibbonProvider.CompStackedDown][199]);
    }

    [Fact]
    public void Ribbon_says_its_order_where_the_close_is_and_what_is_not_drawn_yet()
    {
        var bars = Closes(Enumerable.Range(0, 260).Select(i => 100.0 + i).ToArray());
        var p = new MovingAverageRibbonProvider();
        Assert.Equal("Ribbon stacked up: from the top 20, 50, 100, 200. Close above all four.",
            Detail(p, MovingAverageRibbonProvider.Code, bars, 230));
        Assert.Equal("Ribbon stacked up: from the top 20, 50, 100, 200. Close above all four. Stacked up this bar.",
            Detail(p, MovingAverageRibbonProvider.Code, bars, 199));
        var early = Detail(p, MovingAverageRibbonProvider.Code, bars, 120);
        Assert.StartsWith("Ribbon: from the top 20, 50, 100", early);
        Assert.Contains("Not yet drawn: the 200-bar", early);

        // A dip under the two short averages, still above the long ones: 20 at 348.5, 50 at
        // 334.7, 100 at 310.1, 200 at 260.3, close 320.
        var dip = Closes(Enumerable.Range(0, 260).Select(i => 100.0 + i).Append(320.0).ToArray());
        Assert.Contains("Close between the 50 and the 100",
            Detail(p, MovingAverageRibbonProvider.Code, dip, 260));
    }
}
