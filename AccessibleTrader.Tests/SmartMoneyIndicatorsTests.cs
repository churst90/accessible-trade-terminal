using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// Fair value gaps, order blocks, equal highs and lows with sweeps, and premium and discount
/// (indicator group 2, Cody 2026-11-22 and 11-23). The rules are the ones pre-registered in
/// <c>docs/archive/SMC_PREREG.md</c>, which the lab grades. The provider-wide contracts (causality,
/// metadata, every part written) reach these through IndicatorProviderFixture; this file pins what
/// each one draws, from which bar, and what it says.
/// </summary>
public class SmartMoneyIndicatorsTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Dictionary<string, double[]> Run(IIndicatorProvider p, string code, IReadOnlyList<Ohlcv> bars,
        Dictionary<string, object>? pars = null)
    {
        var rd = new Dictionary<string, double[]>();
        p.Calculate(code, bars.ToArray().AsSpan(), pars ?? new Dictionary<string, object>(), new IndicatorResultBuffer(rd, bars.Count));
        return rd;
    }

    private static string Detail(IIndicatorProvider p, string code, IReadOnlyList<Ohlcv> bars, Dictionary<string, double[]> r,
        int i, Dictionary<string, object>? pars = null)
        => p.GetDetailFact(code, bars.ToArray().AsSpan(), r, i, pars ?? new Dictionary<string, object>());

    /// <summary>Bars from highs and lows, open and close at the midpoint.</summary>
    private static List<Ohlcv> HL(params (double H, double L)[] hl) =>
        hl.Select((x, i) => new Ohlcv(T0.AddHours(i), (x.H + x.L) / 2, x.H, x.L, (x.H + x.L) / 2, 1)).ToList();

    private static List<Ohlcv> OHLC(params (double O, double H, double L, double C)[] b) =>
        b.Select((x, i) => new Ohlcv(T0.AddHours(i), x.O, x.H, x.L, x.C, 1)).ToList();

    /// <summary>Twenty flat bars, 99 to 101, so the ATR is 2 before anything happens.</summary>
    private static List<(double, double)> Flat() => Enumerable.Repeat((101.0, 99.0), 20).ToList();

    private static readonly Dictionary<string, object> Span2 = new() { ["swingBars"] = 2 };

    // ── Fair value gaps ───────────────────────────────────────────────────────

    /// <summary>
    /// Bar 22's low (104) is above bar 20's high (101): a gap up from 101 to 104, known at bar 22.
    /// It stays drawn while closes stay at or above 101 and ends on the first close below.
    /// </summary>
    private static List<Ohlcv> GapBars()
    {
        var hl = Flat();
        hl.AddRange(new[] { (101.0, 99.0), (106.0, 100.0), (108.0, 104.0), (108.0, 103.0), (104.0, 100.0), (102.0, 99.0), (103.0, 99.0) });
        return HL(hl.ToArray());
    }

    [Fact]
    public void A_gap_up_is_drawn_from_the_first_bars_high_to_the_third_bars_low_from_the_third_bar_on()
    {
        var bars = GapBars();
        var r = Run(new FairValueGapsProvider(), FairValueGapsProvider.Code, bars);
        var top = r[FairValueGapsProvider.CompUpTop];
        var bottom = r[FairValueGapsProvider.CompUpBottom];
        var mark = r[FairValueGapsProvider.CompUpMark];

        Assert.True(double.IsNaN(top[21]), "drawn before the third bar closed");
        Assert.Equal(104, top[22]);
        Assert.Equal(101, bottom[22]);
        Assert.Equal(104, mark[22]);
        Assert.Single(mark, v => !double.IsNaN(v));
        // Bar 24 closes at 102, inside: still drawn. Bar 25 closes at 100.5, under 101: gone.
        Assert.Equal(104, top[24]);
        Assert.True(double.IsNaN(top[25]), "still drawn after a close below its bottom");
        Assert.True(double.IsNaN(top[26]));
    }

    [Fact]
    public void A_gap_smaller_than_the_minimum_is_not_marked()
    {
        var bars = GapBars();
        // The gap is 3 and the ATR about 2.5: 1.5 ATR is too much to ask, 1.0 is not.
        var big = Run(new FairValueGapsProvider(), FairValueGapsProvider.Code, bars, new() { [FairValueGapsProvider.ParamMin] = 1.5 });
        Assert.All(big[FairValueGapsProvider.CompUpMark], v => Assert.True(double.IsNaN(v)));
        var small = Run(new FairValueGapsProvider(), FairValueGapsProvider.Code, bars, new() { [FairValueGapsProvider.ParamMin] = 1.0 });
        Assert.Equal(104, small[FairValueGapsProvider.CompUpMark][22]);
    }

    [Fact]
    public void A_gap_down_mirrors_it()
    {
        var hl = Flat();
        hl.AddRange(new[] { (101.0, 99.0), (100.0, 94.0), (96.0, 92.0), (97.0, 93.0), (102.0, 99.0) });
        var bars = HL(hl.ToArray());
        var r = Run(new FairValueGapsProvider(), FairValueGapsProvider.Code, bars);
        Assert.Equal(99, r[FairValueGapsProvider.CompDownTop][22]);
        Assert.Equal(96, r[FairValueGapsProvider.CompDownBottom][22]);
        Assert.Equal(96, r[FairValueGapsProvider.CompDownMark][22]);
        Assert.Equal(99, r[FairValueGapsProvider.CompDownTop][23]);
        // Bar 24 closes at 100.5, above 99.
        Assert.True(double.IsNaN(r[FairValueGapsProvider.CompDownTop][24]));
    }

    [Fact]
    public void The_gap_detail_says_it_opened_and_where_the_open_gaps_are()
    {
        var bars = GapBars();
        var p = new FairValueGapsProvider();
        var r = Run(p, FairValueGapsProvider.Code, bars);
        string at = Detail(p, FairValueGapsProvider.Code, bars, r, 22);
        Assert.Contains("a gap up opened on this bar", at);
        Assert.Contains("gap up 101.00 to 104.00", at);
        Assert.Contains("no gap down open", at);
        string later = Detail(p, FairValueGapsProvider.Code, bars, r, 23);
        Assert.DoesNotContain("opened on this bar", later);
        Assert.Contains("gap up 101.00 to 104.00", later);
        Assert.Equal("Fair value gap up, top 104.00.",
            p.GetComponentSpeech(FairValueGapsProvider.CompUpMark, 104, bars[22], r, 22));
        Assert.Equal("Open gap up, 101.00 to 104.00.",
            p.GetComponentSpeech(FairValueGapsProvider.CompUpTop, 104, bars[23], r, 23));
    }

    // ── Order blocks ──────────────────────────────────────────────────────────

    /// <summary>
    /// With two bars each side: a swing high of 105 at bar 2 (known at bar 4) and a swing low of 98
    /// at bar 4 (known at bar 6). Bar 6 closes at 106, through 105: a break of structure up, and the
    /// last down candle before it is bar 4 (98 to 102). Bar 7 closes back under 105 and bar 8 above it
    /// again, which is not a second break. Bar 9 closes at 96, under the block and under the swing
    /// low: the bullish block ends and a bearish one starts at bar 8, the last up candle.
    /// </summary>
    private static List<Ohlcv> BlockBars() => OHLC(
        (100, 101, 99, 100), (100, 102, 99, 101), (101, 105, 100, 104), (104, 104, 100, 101),
        (101, 102, 98, 99), (99, 103, 99, 102), (102, 107, 101, 106), (106, 106, 103, 104),
        (104, 107, 103, 106), (106, 106, 95, 96));

    [Fact]
    public void A_break_of_the_swing_high_makes_the_last_down_candle_a_bullish_block()
    {
        var bars = BlockBars();
        var r = Run(new OrderBlocksProvider(), OrderBlocksProvider.Code, bars, Span2);
        var top = r[OrderBlocksProvider.CompBullTop];
        var bottom = r[OrderBlocksProvider.CompBullBottom];
        var mark = r[OrderBlocksProvider.CompBullMark];

        Assert.True(double.IsNaN(top[5]));
        Assert.Equal(102, top[6]);
        Assert.Equal(98, bottom[6]);
        Assert.Equal(102, mark[6]);
        // Bar 8 recloses above 105: the swing high already broke, so no second block (bar 7 would be it).
        Assert.True(double.IsNaN(mark[8]), "the same swing high broke twice");
        Assert.Equal(102, top[8]);
        Assert.True(double.IsNaN(top[9]), "still drawn after a close below the block");
    }

    [Fact]
    public void A_break_of_the_swing_low_makes_the_last_up_candle_a_bearish_block()
    {
        var bars = BlockBars();
        var r = Run(new OrderBlocksProvider(), OrderBlocksProvider.Code, bars, Span2);
        Assert.Equal(107, r[OrderBlocksProvider.CompBearTop][9]);
        Assert.Equal(103, r[OrderBlocksProvider.CompBearBottom][9]);
        Assert.Equal(103, r[OrderBlocksProvider.CompBearMark][9]);
        Assert.Single(r[OrderBlocksProvider.CompBearMark], v => !double.IsNaN(v));
    }

    [Fact]
    public void The_block_detail_says_the_bar_broke_structure()
    {
        var bars = BlockBars();
        var p = new OrderBlocksProvider();
        var r = Run(p, OrderBlocksProvider.Code, bars, Span2);
        string at = Detail(p, OrderBlocksProvider.Code, bars, r, 6, Span2);
        Assert.Contains("this bar broke a swing high and set a bullish block", at);
        Assert.Contains("bullish block 98.00 to 102.00", at);
        Assert.Contains("no bearish block open", at);
        Assert.Equal("New bullish order block, top 102.00.",
            p.GetComponentSpeech(OrderBlocksProvider.CompBullMark, 102, bars[6], r, 6));
    }

    // ── Equal highs and lows, sweeps ─────────────────────────────────────────

    /// <summary>
    /// After the flat stretch, a swing high of 110 at bar 22 and one of 110.1 at bar 26, with
    /// nothing above 110.1 between them: equal highs at 110.1, known at bar 28. Bar 26 itself trades
    /// over 110 and closes at 108.05, back under it: a sweep of the bar 22 high. Bar 30 trades over
    /// 110.1; its close decides sweep (109) or breakout (110.5). Bar 32 trades over both again and
    /// closes at 110, under 110.1: not a sweep, because each level is taken only once.
    /// </summary>
    private static List<Ohlcv> EqualBars(double lastClose)
    {
        var hl = Flat();
        hl.AddRange(new[] { (103.0, 99.0), (106.0, 102.0), (110.0, 105.0), (107.0, 103.0), (105.0, 101.0), (107.0, 103.0),
                            (110.1, 106.0), (107.0, 103.0), (105.0, 101.0), (108.0, 104.0), (111.0, 107.0), (108.0, 104.0),
                            (112.0, 108.0) });
        var bars = HL(hl.ToArray());
        var b = bars[30];
        bars[30] = new Ohlcv(b.Date, b.Open, b.High, b.Low, lastClose, b.Volume);
        return bars;
    }

    [Fact]
    public void Two_swing_highs_at_one_price_are_drawn_from_the_second_ones_confirmation_until_price_trades_above()
    {
        var bars = EqualBars(109);
        var r = Run(new LiquidityProvider(), LiquidityProvider.Code, bars, Span2);
        var eq = r[LiquidityProvider.CompEqualHigh];
        Assert.True(double.IsNaN(eq[27]), "drawn before the second high was confirmed");
        Assert.Equal(110.1, eq[28], 9);
        Assert.Equal(110.1, eq[30], 9);
        Assert.True(double.IsNaN(eq[31]), "still drawn after a bar traded above it");
    }

    [Fact]
    public void Highs_further_apart_than_the_tolerance_are_not_equal()
    {
        var bars = EqualBars(109);
        var r = Run(new LiquidityProvider(), LiquidityProvider.Code, bars,
            new() { ["swingBars"] = 2, [LiquidityProvider.ParamTolerance] = 0.0 });
        Assert.All(r[LiquidityProvider.CompEqualHigh], v => Assert.True(double.IsNaN(v)));
    }

    /// <summary>
    /// One bar each side: swing highs of 110 (bar 21) and 110.1 (bar 28). Between them two bars tie
    /// at the top, so neither is a swing high of its own; at 110.5 they are above the pair and spoil
    /// it, at 109.5 they are not.
    /// </summary>
    [Theory]
    [InlineData(110.5, false)]
    [InlineData(109.5, true)]
    public void A_high_between_the_two_above_them_spoils_the_pair(double between, bool equal)
    {
        var hl = Flat();
        hl.AddRange(new[] { (106.0, 102.0), (110.0, 105.0), (107.0, 103.0), (108.0, 104.0), (between, 106.0), (between, 106.0),
                            (108.0, 104.0), (107.0, 103.0), (110.1, 105.0), (107.0, 103.0), (105.0, 101.0) });
        var bars = HL(hl.ToArray());
        var r = Run(new LiquidityProvider(), LiquidityProvider.Code, bars, new() { ["swingBars"] = 1 });
        var eq = r[LiquidityProvider.CompEqualHigh];
        if (equal) Assert.Equal(110.1, eq[29], 9);
        else Assert.True(double.IsNaN(eq[29]), "a pair with a higher bar between them was called equal");
    }

    [Fact]
    public void A_sweep_is_the_first_bar_through_a_swing_high_that_closes_back_under_it()
    {
        var bars = EqualBars(109);
        var r = Run(new LiquidityProvider(), LiquidityProvider.Code, bars, Span2);
        var swept = r[LiquidityProvider.CompSweepHigh];
        Assert.Equal(110, swept[26]);
        Assert.Equal(110.1, swept[30], 9);
        // Bar 32 is not a second sweep of 110.1: each level is taken once.
        Assert.True(double.IsNaN(swept[32]), "a level already taken was swept again");
        Assert.Equal(2, swept.Count(v => !double.IsNaN(v)));
    }

    [Fact]
    public void A_bar_that_closes_above_the_high_it_took_is_a_breakout_not_a_sweep()
    {
        var bars = EqualBars(110.5);
        var r = Run(new LiquidityProvider(), LiquidityProvider.Code, bars, Span2);
        Assert.True(double.IsNaN(r[LiquidityProvider.CompSweepHigh][30]), "a breakout was marked as a sweep");
        Assert.Equal(110, r[LiquidityProvider.CompSweepHigh][26]);
    }

    [Fact]
    public void Equal_lows_and_low_sweeps_mirror_them()
    {
        // The same bars turned upside down around 200.
        var up = EqualBars(109);
        var bars = up.Select(b => new Ohlcv(b.Date, 200 - b.Open, 200 - b.Low, 200 - b.High, 200 - b.Close, b.Volume)).ToList();
        var r = Run(new LiquidityProvider(), LiquidityProvider.Code, bars, Span2);
        Assert.Equal(89.9, r[LiquidityProvider.CompEqualLow][28], 9);
        Assert.True(double.IsNaN(r[LiquidityProvider.CompEqualLow][31]));
        Assert.Equal(90, r[LiquidityProvider.CompSweepLow][26]);
        Assert.Equal(89.9, r[LiquidityProvider.CompSweepLow][30], 9);
    }

    [Fact]
    public void The_liquidity_detail_says_the_sweep_and_the_open_equal_levels()
    {
        var bars = EqualBars(109);
        var p = new LiquidityProvider();
        var r = Run(p, LiquidityProvider.Code, bars, Span2);
        string at = Detail(p, LiquidityProvider.Code, bars, r, 30, Span2);
        Assert.Contains("this bar swept the high 110.10 and closed back below", at);
        Assert.Contains("equal highs 110.10", at);
        string before = Detail(p, LiquidityProvider.Code, bars, r, 29, Span2);
        Assert.DoesNotContain("swept", before);
        Assert.Contains("equal highs 110.10, 3.9% above", before);
        Assert.Equal("Swept the high 110.10, closed back below.",
            p.GetComponentSpeech(LiquidityProvider.CompSweepHigh, 110.1, bars[30], r, 30));
    }

    // ── Premium and discount ─────────────────────────────────────────────────

    /// <summary>
    /// Two bars each side: a swing high of 110 at bar 2 (known at bar 4) and a swing low of 100 at bar
    /// 5 (known at bar 7). The range is 100 to 110, the middle 105. Bar 7 closes at 102 (discount),
    /// bar 8 at 108 (premium).
    /// </summary>
    private static List<Ohlcv> RangeBars() => HL(
        (102, 98), (105, 100), (110, 104), (108, 103), (106, 101), (104, 100), (105, 101), (103, 101), (109, 107));

    [Fact]
    public void The_range_runs_from_the_last_swing_low_to_the_last_swing_high_once_both_are_known()
    {
        var bars = RangeBars();
        var r = Run(new PremiumDiscountProvider(), PremiumDiscountProvider.Code, bars, Span2);
        Assert.True(double.IsNaN(r[PremiumDiscountProvider.CompMid][6]), "drawn before the swing low was known");
        Assert.Equal(110, r[PremiumDiscountProvider.CompHigh][7]);
        Assert.Equal(100, r[PremiumDiscountProvider.CompLow][7]);
        Assert.Equal(105, r[PremiumDiscountProvider.CompMid][7]);
        Assert.Equal(-1, r[PremiumDiscountProvider.CompZone][7]);
        Assert.Equal(1, r[PremiumDiscountProvider.CompZone][8]);
        // The bar the close moves into each zone is marked, at the equilibrium, once.
        Assert.Equal(105, r[PremiumDiscountProvider.CompIntoDiscount][7]);
        Assert.Equal(105, r[PremiumDiscountProvider.CompIntoPremium][8]);
        Assert.Single(r[PremiumDiscountProvider.CompIntoDiscount], v => !double.IsNaN(v));
        Assert.Single(r[PremiumDiscountProvider.CompIntoPremium], v => !double.IsNaN(v));
    }

    [Fact]
    public void Staying_in_a_zone_is_marked_only_on_the_bar_it_was_entered()
    {
        var hl = RangeBars().Select(b => (b.High, b.Low)).ToList();
        hl.Add((108.5, 106.5));   // bar 9: still premium, close 107.5
        var bars = HL(hl.ToArray());
        var r = Run(new PremiumDiscountProvider(), PremiumDiscountProvider.Code, bars, Span2);
        Assert.Equal(1, r[PremiumDiscountProvider.CompZone][9]);
        Assert.True(double.IsNaN(r[PremiumDiscountProvider.CompIntoPremium][9]), "marked again while it stayed in premium");
    }

    [Fact]
    public void The_structure_follows_the_last_break()
    {
        var bars = BlockBars();
        var r = Run(new PremiumDiscountProvider(), PremiumDiscountProvider.Code, bars, Span2);
        var s = r[PremiumDiscountProvider.CompStructure];
        Assert.Equal(new double[] { 0, 0, 0, 0, 0, 0, 1, 1, 1, -1 }, s);
        // Bar 6 closes at 106, over the range (98 to 105): neither premium nor discount. Bar 7, 104: premium.
        Assert.Equal(0, r[PremiumDiscountProvider.CompZone][6]);
        Assert.Equal(1, r[PremiumDiscountProvider.CompZone][7]);
    }

    [Fact]
    public void The_premium_detail_says_where_in_the_range_the_close_is()
    {
        var bars = RangeBars();
        var p = new PremiumDiscountProvider();
        var r = Run(p, PremiumDiscountProvider.Code, bars, Span2);
        Assert.Equal("Premium and discount: moved into discount, 20% of the way up; range 100.00 to 110.00, equilibrium 105.00. No break of structure yet.",
            Detail(p, PremiumDiscountProvider.Code, bars, r, 7, Span2));
        Assert.StartsWith("Premium and discount: no range yet", Detail(p, PremiumDiscountProvider.Code, bars, r, 6, Span2));
        Assert.Equal("Equilibrium 105.00, price in premium.",
            p.GetComponentSpeech(PremiumDiscountProvider.CompMid, 105, bars[8], r, 8));
    }

    // ── The strategy builder ────────────────────────────────────────────────────────────────

    [Fact]
    public void The_lab_found_no_information_so_no_part_is_offered_to_strategies_and_each_says_why()
    {
        // Prereg section 7: a concept's levels reach the strategy builder only if it has information.
        var catalog = new SignalCatalog(new IIndicatorProvider[]
            { new FairValueGapsProvider(), new OrderBlocksProvider(), new LiquidityProvider(), new PremiumDiscountProvider() });
        Assert.Empty(catalog.All);
        Assert.Equal(6 + 6 + 4 + 7, catalog.Excluded.Count);
        foreach (var d in catalog.Excluded)
            Assert.Contains("found no information", catalog.RefusalReason(d.Id));
    }
}
