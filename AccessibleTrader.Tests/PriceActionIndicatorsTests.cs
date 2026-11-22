using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using Skender.Stock.Indicators;

namespace AccessibleTrader.Tests;

/// <summary>
/// Supertrend, ZigZag, Williams Fractals and Pivot Points High/Low (indicator group 1, Cody
/// 2026-11-22). The provider-wide contracts (causality on a prefix and with older bars prepended,
/// metadata, parts left out, every part written) reach these through IndicatorProviderFixture;
/// this file pins what each one computes, when it knows it, and what it says.
/// </summary>
public class PriceActionIndicatorsTests
{
    private static Dictionary<string, double[]> Run(IIndicatorProvider p, string code, IReadOnlyList<Ohlcv> bars,
        Dictionary<string, object>? pars = null)
    {
        var rd = new Dictionary<string, double[]>();
        p.Calculate(code, bars.ToArray().AsSpan(), pars ?? new Dictionary<string, object>(), new IndicatorResultBuffer(rd, bars.Count));
        return rd;
    }

    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Bars from highs and lows; open and close at the midpoint unless given.</summary>
    private static List<Ohlcv> HL(params (double H, double L)[] hl) =>
        hl.Select((x, i) => new Ohlcv(T0.AddHours(i), (x.H + x.L) / 2, x.H, x.L, (x.H + x.L) / 2, 1)).ToList();

    // ── Supertrend ────────────────────────────────────────────────────────────

    /// <summary>
    /// Skender's SuperTrend is an independent implementation of the same rules (ATR band ratchet
    /// around the bar's midpoint). It seeds differently (its first trend comes from the close
    /// against the midpoint, ours is TradingView's "down"), and its ATR skips the first bar, so the
    /// two agree once a flip has passed and the ATR seeds have washed out; from bar 300 on they
    /// must agree everywhere, line and side.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Supertrend_agrees_with_skenders_supertrend(int flavour)
    {
        var bars = CausalityProbeSeries.Bars(flavour, 1200);
        var ours = Run(new SupertrendProvider(), SupertrendProvider.Code, bars);
        var quotes = bars.Select(b => new Quote
        {
            Date = b.Date, Open = (decimal)b.Open, High = (decimal)b.High, Low = (decimal)b.Low,
            Close = (decimal)b.Close, Volume = (decimal)b.Volume,
        }).ToList();
        var theirs = quotes.GetSuperTrend(10, 3).ToList();

        int compared = 0, flips = 0;
        for (int i = 300; i < bars.Count; i++)
        {
            var t = theirs[i];
            Assert.NotNull(t.SuperTrend);
            bool theirsUp = t.LowerBand != null;
            bool oursUp = !double.IsNaN(ours[SupertrendProvider.CompUp][i]);
            Assert.True(theirsUp == oursUp, $"bar {i}: Skender {(theirsUp ? "up" : "down")}, ours {(oursUp ? "up" : "down")}");
            double line = oursUp ? ours[SupertrendProvider.CompUp][i] : ours[SupertrendProvider.CompDown][i];
            Assert.Equal((double)t.SuperTrend!.Value, line, 6);
            compared++;
            if (!double.IsNaN(ours[SupertrendProvider.CompTurnUp][i]) || !double.IsNaN(ours[SupertrendProvider.CompTurnDown][i])) flips++;
        }
        Assert.True(compared > 800);
        Assert.True(flips >= 4, $"only {flips} flips: the comparison never crossed a change of side");
    }

    [Fact]
    public void Supertrend_starts_down_ratchets_under_a_rise_and_flips_on_a_close_through_it()
    {
        // Ten flat bars (range 2, so ATR 2 and the bands 6 either side of 100), then a steady rise,
        // then one bar that closes far under the trailing line.
        var bars = new List<Ohlcv>();
        for (int i = 0; i < 10; i++) bars.Add(new Ohlcv(T0.AddHours(i), 100, 101, 99, 100, 1));
        for (int i = 0; i < 20; i++) { double c = 108 + i * 2; bars.Add(new Ohlcv(T0.AddHours(10 + i), c - 1, c + 1, c - 1, c, 1)); }
        bars.Add(new Ohlcv(T0.AddHours(30), 140, 140, 100, 101, 1));
        var r = Run(new SupertrendProvider(), SupertrendProvider.Code, bars);

        Assert.True(double.IsNaN(r[SupertrendProvider.CompDirection][8]));           // no ATR yet
        Assert.Equal(-1, r[SupertrendProvider.CompDirection][9]);                     // TradingView: the first is down
        Assert.Equal(106, r[SupertrendProvider.CompDown][9], 9);                      // 100 + 3 × 2

        int flipUp = Enumerable.Range(10, 20).First(i => r[SupertrendProvider.CompDirection][i] == 1);
        Assert.Equal(r[SupertrendProvider.CompUp][flipUp], r[SupertrendProvider.CompTurnUp][flipUp]);
        for (int i = flipUp + 1; i < 30; i++)
        {
            Assert.True(r[SupertrendProvider.CompUp][i] >= r[SupertrendProvider.CompUp][i - 1], $"bar {i}: the lower band fell in an uptrend");
            Assert.True(bars[i].Low > r[SupertrendProvider.CompUp][i]);
            Assert.True(double.IsNaN(r[SupertrendProvider.CompTurnUp][i]));
        }
        Assert.Equal(-1, r[SupertrendProvider.CompDirection][30]);
        Assert.False(double.IsNaN(r[SupertrendProvider.CompTurnDown][30]));
        Assert.Equal(r[SupertrendProvider.CompDown][30], r[SupertrendProvider.CompTurnDown][30]);
    }

    /// <summary>
    /// Every bar gaps 10 from the last close with a range of 1, so the true range is the gap
    /// (10.5), not the bar (1): a high above the last close going up, a low under it going down.
    /// The first bar's is its range. ATR at bar 9 = (1 + 9 × 10.5) / 10 = 9.55, and the first line
    /// is the upper band, the close + 3 × 9.55.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Supertrend_counts_a_gap_in_the_true_range(int way)
    {
        var bars = Enumerable.Range(0, 10).Select(i =>
        {
            double c = 200 + way * 10 * i;
            return new Ohlcv(T0.AddHours(i), c, c + 0.5, c - 0.5, c, 1);
        }).ToList();
        var r = Run(new SupertrendProvider(), SupertrendProvider.Code, bars);
        Assert.Equal(bars[9].Close + 3 * 9.55, r[SupertrendProvider.CompDown][9], 9);
    }

    [Fact]
    public void Supertrend_says_its_side_and_the_line_as_support_or_resistance()
    {
        var p = new SupertrendProvider();
        var bar = new Ohlcv(T0, 1, 1, 1, 1, 1);
        var none = new Dictionary<string, double[]>();
        Assert.Equal("Supertrend up, support 95000.00.", p.GetComponentSpeech(SupertrendProvider.CompUp, 95000, bar, none, 0));
        Assert.Equal("Supertrend down, resistance 95000.00.", p.GetComponentSpeech(SupertrendProvider.CompDown, 95000, bar, none, 0));
        Assert.Equal("Supertrend turned down, resistance 95000.00.", p.GetComponentSpeech(SupertrendProvider.CompTurnDown, 95000, bar, none, 0));

        var bars = CausalityProbeSeries.Bars(0, 300);
        var r = Run(p, SupertrendProvider.Code, bars);
        int i = 250;
        string fact = p.GetDetailFact(SupertrendProvider.Code, bars.ToArray(), r, i, new());
        bool up = r[SupertrendProvider.CompDirection][i] > 0;
        Assert.StartsWith(up ? "Supertrend up" : "Supertrend down", fact);
        Assert.Contains(up ? "% below the close." : "% above the close.", fact);
    }

    // ── Williams Fractals and Pivot Points High/Low ──────────────────────────

    [Fact]
    public void A_fractal_looks_past_a_flat_top_and_a_strict_pivot_does_not()
    {
        // Highs 1 2 5 5 3 2 1: the second 5 is a fractal high (the first is passed over); neither
        // is a pivot high with one bar each side, because each has an equal neighbour.
        var bars = HL((1, 0), (2, 0.5), (5, 1), (5, 1), (3, 0.5), (2, 0.4), (1, 0.3));
        var f = Run(new WilliamsFractalsProvider(), WilliamsFractalsProvider.Code, bars);
        Assert.Equal(5, f[WilliamsFractalsProvider.CompHigh][3]);
        Assert.True(double.IsNaN(f[WilliamsFractalsProvider.CompHigh][2]));

        var p = Run(new PivotPointsHighLowProvider(), PivotPointsHighLowProvider.Code, bars,
            new() { [PivotPointsHighLowProvider.ParamLeft] = 1, [PivotPointsHighLowProvider.ParamRight] = 1 });
        Assert.True(double.IsNaN(p[PivotPointsHighLowProvider.CompHigh][2]));
        Assert.True(double.IsNaN(p[PivotPointsHighLowProvider.CompHigh][3]));
    }

    [Fact]
    public void An_earlier_higher_high_cancels_a_fractal()
    {
        // The 5 at bar 3 beats the two highs after it and the two lowest before it, but the 6
        // right before it is higher: not a fractal (the 6 is).
        var bars = HL((1, 0), (0.5, 0), (6, 1), (5, 1), (3, 1), (2, 1));
        var f = Run(new WilliamsFractalsProvider(), WilliamsFractalsProvider.Code, bars);
        Assert.Equal(6, f[WilliamsFractalsProvider.CompHigh][2]);
        Assert.True(double.IsNaN(f[WilliamsFractalsProvider.CompHigh][3]));
    }

    [Fact]
    public void A_fractal_low_needs_two_higher_lows_each_side()
    {
        // The 2.5 at bar 4 is under the lows either side of it but has only one bar after it, so it
        // is a fractal only with one bar each side.
        var bars = HL((10, 5), (10, 4), (10, 2), (10, 3), (10, 2.5), (10, 4));
        var f = Run(new WilliamsFractalsProvider(), WilliamsFractalsProvider.Code, bars);
        Assert.Equal(2, f[WilliamsFractalsProvider.CompLow][2]);
        Assert.Equal(1, f[WilliamsFractalsProvider.CompLow].Count(v => !double.IsNaN(v)));
        var one = Run(new WilliamsFractalsProvider(), WilliamsFractalsProvider.Code, bars, new() { [WilliamsFractalsProvider.ParamSpan] = 1 });
        Assert.Equal(2.5, one[WilliamsFractalsProvider.CompLow][4]);
    }

    /// <summary>
    /// The mark is drawn on its own bar, but the "last" level only takes the price from the bar that
    /// confirmed it: span bars later for a fractal, right bars later for a pivot. A strategy reading
    /// the level must not see it any earlier.
    /// </summary>
    [Fact]
    public void The_last_high_takes_the_price_only_once_the_bars_after_it_are_in()
    {
        var bars = HL((1, 0.5), (2, 1), (9, 1), (3, 1), (2, 1), (2.5, 1), (2.2, 1), (2.1, 1));
        var f = Run(new WilliamsFractalsProvider(), WilliamsFractalsProvider.Code, bars);
        Assert.Equal(9, f[WilliamsFractalsProvider.CompHigh][2]);
        Assert.True(double.IsNaN(f[WilliamsFractalsProvider.CompLastHigh][3]));
        Assert.Equal(9, f[WilliamsFractalsProvider.CompLastHigh][4]);

        var p = Run(new PivotPointsHighLowProvider(), PivotPointsHighLowProvider.Code, bars,
            new() { [PivotPointsHighLowProvider.ParamLeft] = 2, [PivotPointsHighLowProvider.ParamRight] = 4 });
        Assert.Equal(9, p[PivotPointsHighLowProvider.CompHigh][2]);
        Assert.True(double.IsNaN(p[PivotPointsHighLowProvider.CompLastHigh][5]));
        Assert.Equal(9, p[PivotPointsHighLowProvider.CompLastHigh][6]);
    }

    [Fact]
    public void Pivot_high_low_defaults_to_ten_bars_each_side()
    {
        // One high 12 bars in, everything else lower: a pivot with 10 each side once 22 bars exist.
        var hl = Enumerable.Range(0, 23).Select(i => (H: i == 12 ? 50.0 : 10 + (i % 3), L: 5.0 + (i % 2))).ToArray();
        var bars = HL(hl);
        var p = Run(new PivotPointsHighLowProvider(), PivotPointsHighLowProvider.Code, bars);
        Assert.Equal(50, p[PivotPointsHighLowProvider.CompHigh][12]);
        Assert.Equal(50, p[PivotPointsHighLowProvider.CompLastHigh][22]);
        Assert.True(double.IsNaN(p[PivotPointsHighLowProvider.CompLastHigh][21]));
    }

    [Fact]
    public void Pivot_high_low_looks_ten_bars_back_by_default()
    {
        // A 45 at bar 30 is the highest of the 5 bars before it but not of the 10: the 50 at 22 is.
        var hl = Enumerable.Range(0, 45).Select(i => (H: i == 22 ? 50.0 : i == 30 ? 45.0 : 10 + (i % 3), L: 5.0 + (i % 2))).ToArray();
        var p = Run(new PivotPointsHighLowProvider(), PivotPointsHighLowProvider.Code, HL(hl));
        Assert.Equal(50, p[PivotPointsHighLowProvider.CompHigh][22]);
        Assert.True(double.IsNaN(p[PivotPointsHighLowProvider.CompHigh][30]));
    }

    [Fact]
    public void Fractal_and_pivot_marks_say_their_price_and_the_detail_says_when_they_were_known()
    {
        var bar = new Ohlcv(T0, 1, 1, 1, 1, 1);
        var none = new Dictionary<string, double[]>();
        Assert.Equal("Fractal high 95000.00.", new WilliamsFractalsProvider().GetComponentSpeech(WilliamsFractalsProvider.CompHigh, 95000, bar, none, 0));
        Assert.Equal("Pivot low 1.25.", new PivotPointsHighLowProvider().GetComponentSpeech(PivotPointsHighLowProvider.CompLow, 1.25, bar, none, 0));

        var bars = HL((1, 0.5), (2, 1), (9, 1), (3, 1), (2, 1), (2.5, 1), (2.2, 1), (2.1, 1));
        var r = Run(new WilliamsFractalsProvider(), WilliamsFractalsProvider.Code, bars);
        string at = new WilliamsFractalsProvider().GetDetailFact(WilliamsFractalsProvider.Code, bars.ToArray(), r, 2, new());
        Assert.Contains("Fractal high here, 9.00, known 2 bars later.", at);
        string later = new WilliamsFractalsProvider().GetDetailFact(WilliamsFractalsProvider.Code, bars.ToArray(), r, 7, new());
        Assert.Contains("Last fractal high 9.00", later);
    }

    // ── ZigZag ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 100 up to 110, down to 99, up to 106: at 5%, 110 is a turn high confirmed at the first low at
    /// or under 104.5, 99 a turn low confirmed at the first high at or over 103.95, and the line from
    /// 106 on is the last leg, still moving.
    /// </summary>
    private static List<Ohlcv> ZigZagBars() => HL(
        (100, 99.5), (104, 101), (110, 105), (107, 105), (105, 104),   // 0-4: the high at 2; bar 4's low 104 confirms it
        (103, 101), (101, 99), (102, 100), (104, 101), (106, 103));     // 5-9: the low 99 at 6; bar 8's high 104 confirms it

    [Fact]
    public void ZigZag_turns_are_drawn_on_their_bar_and_known_at_the_bar_that_moved_five_percent()
    {
        var bars = ZigZagBars();
        var (turns, tail, tailPrice) = ZigZagProvider.Compute(bars.ToArray(), 5);
        Assert.Equal(new[] { (0, 99.5, false, 2), (2, 110.0, true, 4), (6, 99.0, false, 8) },
            turns.Select(t => (t.Index, t.Price, t.IsHigh, t.ConfirmedAt)).ToArray());
        Assert.Equal(9, tail);
        Assert.Equal(106, tailPrice);

        var r = Run(new ZigZagProvider(), ZigZagProvider.Code, bars);
        Assert.Equal(110, r[ZigZagProvider.CompTurnHigh][2]);
        Assert.Equal(99, r[ZigZagProvider.CompTurnLow][6]);
        Assert.True(double.IsNaN(r[ZigZagProvider.CompLastHigh][3]));   // not known at bar 3
        Assert.Equal(110, r[ZigZagProvider.CompLastHigh][4]);
        Assert.Equal(99.5, r[ZigZagProvider.CompLastLow][7]);           // the low at 6 is not known until 8
        Assert.Equal(99, r[ZigZagProvider.CompLastLow][8]);
        // The line runs straight between turns and on to the newest extreme.
        Assert.Equal(110 + (99 - 110) * 2 / 4.0, r[ZigZagProvider.CompLine][4], 9);
        Assert.Equal(106, r[ZigZagProvider.CompLine][9], 9);
    }

    [Fact]
    public void A_bigger_reversal_setting_ignores_the_smaller_swing()
    {
        var (turns, _, _) = ZigZagProvider.Compute(ZigZagBars().ToArray(), 12);
        Assert.Empty(turns);
    }

    [Fact]
    public void ZigZag_says_the_leg_a_bar_is_on_and_that_the_last_one_is_not_confirmed()
    {
        var bars = ZigZagBars();
        var p = new ZigZagProvider();
        var r = Run(p, ZigZagProvider.Code, bars);
        var bar = bars[0];
        Assert.Equal("ZigZag falling leg, 110.00 to 99.00, down 10.0%.", p.GetComponentSpeech(ZigZagProvider.CompLine, r[ZigZagProvider.CompLine][4], bar, r, 4));
        Assert.Equal("ZigZag rising leg from 99.00, still moving, not confirmed.", p.GetComponentSpeech(ZigZagProvider.CompLine, r[ZigZagProvider.CompLine][9], bar, r, 9));
        Assert.Equal("ZigZag high 110.00, from 99.50, up 10.6%.", p.GetComponentSpeech(ZigZagProvider.CompTurnHigh, 110, bar, r, 2));
    }

    /// <summary>
    /// The detail is what was known at that bar: at bar 7 the high 110 is confirmed and the low 99
    /// at bar 6 is not (bar 8 confirms it), so it is still "falling since the high".
    /// </summary>
    [Fact]
    public void ZigZag_detail_says_only_the_turns_confirmed_by_that_bar()
    {
        var bars = ZigZagBars();
        var p = new ZigZagProvider();
        var r = Run(p, ZigZagProvider.Code, bars);
        Assert.Equal("ZigZag: falling since the confirmed high 110.00, down 8.2% to the close. The low before it, 99.50.",
            p.GetDetailFact(ZigZagProvider.Code, bars.ToArray(), r, 7, new()));
        Assert.Equal("ZigZag: rising since the confirmed low 99.00, up 3.5% to the close. The high before it, 110.00.",
            p.GetDetailFact(ZigZagProvider.Code, bars.ToArray(), r, 8, new()));
        Assert.Equal("ZigZag: no turn confirmed yet.", p.GetDetailFact(ZigZagProvider.Code, bars.ToArray(), r, 1, new()));
    }

    // ── Wiring ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The desktop and the web app each register their providers in their own list, and an
    /// indicator missing from one is simply absent from that app's picker. Every provider class in
    /// Core has to be in both.
    /// </summary>
    [Fact]
    public void Every_core_indicator_is_registered_in_the_desktop_and_the_web_app()
    {
        string root = RepoRoot();
        var hosts = new[]
        {
            Path.Combine(root, "AccessibleTrader.BlazorClient", "ServiceCollectionExtensions.cs"),
            Path.Combine(root, "AccessibleTrader.WebHost", "ServiceCollectionExtensions.cs"),
        };
        foreach (var h in hosts) Assert.True(File.Exists(h), h);

        var core = typeof(SupertrendProvider).Assembly.GetTypes()
            .Where(t => typeof(IIndicatorProvider).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface && t.IsPublic)
            .Select(t => t.Name).ToList();
        Assert.True(core.Count > 30, $"only {core.Count} provider types found");
        Assert.Contains(nameof(ZigZagProvider), core);

        var missing = hosts.SelectMany(h =>
        {
            string text = File.ReadAllText(h);
            return core.Where(c => !text.Contains($"IIndicatorProvider, {c}>")).Select(c => $"{c} in {Path.GetFileName(Path.GetDirectoryName(h))}");
        }).ToList();
        Assert.True(missing.Count == 0, "Not registered: " + string.Join(", ", missing));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.sln")) && !Directory.Exists(Path.Combine(dir.FullName, "AccessibleTrader.WebHost")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
