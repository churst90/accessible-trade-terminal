using AccessibleTrader.Core.Services.Charts;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// The pure Renko builder: close-based traditional bricks on a floor-anchored grid.
/// Every test here is arithmetic, not a chart — the point of the builder being pure is that
/// these can pin the trading definition without a store, a feed or a window.
/// </summary>
public sealed class RenkoBuilderTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static List<Ohlcv> Bars(params double[] closes)
        => closes.Select((c, i) => new Ohlcv(Start.AddHours(i), c, c + 1, c - 1, c, 10)).ToList();

    private static BrickBuildResult Build(IReadOnlyList<Ohlcv> bars, double box)
        => BrickChartBuilder.Build(bars, box, TimeSpan.FromHours(1));

    [Fact]
    public void Rising_by_exactly_three_boxes_makes_three_up_bricks()
    {
        var built = Build(Bars(100, 105, 110, 120, 130), box: 10);

        Assert.True(built.Ok);
        Assert.Equal(3, built.Bricks.Count);
        Assert.All(built.Spans, s => Assert.True(s.Up));
        Assert.Equal(new[] { 100d, 110d, 120d }, built.Bricks.Select(b => b.Open));
        Assert.Equal(new[] { 110d, 120d, 130d }, built.Bricks.Select(b => b.Close));
    }

    [Fact]
    public void A_one_box_pullback_after_an_uptrend_makes_no_brick()
    {
        var built = Build(Bars(100, 110, 120, 130, 120), box: 10);

        Assert.Equal(3, built.Bricks.Count);
        Assert.DoesNotContain(built.Spans, s => !s.Up);
    }

    [Fact]
    public void A_two_box_drop_makes_one_down_brick()
    {
        var built = Build(Bars(100, 110, 120, 130, 110), box: 10);

        Assert.Equal(4, built.Bricks.Count);
        var down = built.Bricks[^1];
        Assert.False(built.Spans[^1].Up);
        Assert.Equal(120, down.Open);
        Assert.Equal(110, down.Close);
    }

    [Fact]
    public void A_bar_moving_four_boxes_makes_four_bricks_sharing_its_date_and_volume_once()
    {
        var bars = Bars(100, 140);
        bars[1] = bars[1] with { Volume = 7 };
        var built = Build(bars, box: 10);

        Assert.Equal(4, built.Bricks.Count);
        Assert.All(built.Bricks, b => Assert.Equal(bars[1].Date, b.Date));
        Assert.Equal(4, built.Spans.Length);
        Assert.Equal(17, built.Bricks[0].Volume);   // the two bars up to the completion bar
        Assert.All(built.Bricks.Skip(1), b => Assert.Equal(0, b.Volume));
    }

    [Fact]
    public void The_first_brick_goes_either_way_after_one_box_from_the_anchor()
    {
        var up = Build(Bars(100, 111), box: 10);
        Assert.Single(up.Bricks);
        Assert.True(up.Spans[0].Up);

        var down = Build(Bars(100, 89), box: 10);
        Assert.Single(down.Bricks);
        Assert.False(down.Spans[0].Up);
    }

    [Fact]
    public void Every_edge_is_a_whole_multiple_of_the_box()
    {
        var built = Build(Bars(103.7, 112, 128, 121), box: 10);

        Assert.NotEmpty(built.Bricks);
        foreach (var b in built.Bricks)
        {
            Assert.Equal(0, b.Open % 10, 9);
            Assert.Equal(0, b.Close % 10, 9);
            Assert.Equal(0, b.High % 10, 9);
            Assert.Equal(0, b.Low % 10, 9);
            Assert.Equal(Math.Max(b.Open, b.Close), b.High);
            Assert.Equal(Math.Min(b.Open, b.Close), b.Low);
        }
    }

    [Fact]
    public void Prepending_older_bars_leaves_the_same_tail_of_bricks()
    {
        var original = Bars(103.7, 112, 108, 126, 119, 131);
        var tail = Build(original, box: 10);

        // A different first close on purpose: the grid must come from its FLOOR multiple, or
        // exactly this prepend lands the tail bricks on a different set of edges.
        var older = Enumerable.Range(0, 50)
            .Select(i => new Ohlcv(Start.AddHours(-50 + i), 106.5, 107, 106, 106.5, 0))
            .ToList();
        older.AddRange(original.Select(b => b with { Volume = 0 }));
        var prepended = Build(older, box: 10);

        Assert.Equal(tail.Bricks.Count + 0, prepended.Bricks.Count);
        var tailBricks = prepended.Bricks.Skip(prepended.Bricks.Count - tail.Bricks.Count).ToList();
        for (int i = 0; i < tail.Bricks.Count; i++)
        {
            Assert.Equal(tail.Bricks[i].Open, tailBricks[i].Open);
            Assert.Equal(tail.Bricks[i].Close, tailBricks[i].Close);
            Assert.Equal(tail.Bricks[i].Date, tailBricks[i].Date);
        }
    }

    [Fact]
    public void A_span_starts_after_the_previous_bricks_completion_bar_and_ends_when_this_one_closed()
    {
        // Brick 1 completes on bar 1; brick 2 then runs from bar 2 and completes on bar 7.
        var bars = Bars(100, 111, 111, 112, 115, 117, 119, 121);
        var built = Build(bars, box: 10);

        Assert.Equal(2, built.Bricks.Count);
        var second = built.Spans[1];
        Assert.Equal(2, second.FirstBar);
        Assert.Equal(7, second.LastBar);
        Assert.Equal(bars[2].Date, second.Start);
        Assert.Equal(bars[7].Date.AddHours(1), second.End);

        var first = built.Spans[0];
        Assert.Equal(0, first.FirstBar);
        Assert.Equal(1, first.LastBar);
        Assert.Equal(bars[0].Date, first.Start);
        Assert.Equal(bars[1].Date.AddHours(1), first.End);
    }

    [Fact]
    public void Two_bricks_inside_one_bar_share_the_end_and_the_second_starts_in_that_bar()
    {
        var bars = Bars(100, 125);
        var built = Build(bars, box: 10);

        Assert.Equal(2, built.Bricks.Count);
        Assert.Equal(bars[1].Date.AddHours(1), built.Spans[0].End);
        Assert.Equal(built.Spans[0].End, built.Spans[1].End);
        Assert.Equal(0, built.Spans[0].FirstBar);
        Assert.Equal(1, built.Spans[1].FirstBar);
        Assert.Equal(1, built.Spans[1].LastBar);
    }

    [Fact]
    public void Over_the_cap_is_a_refusal()
    {
        var built = BrickChartBuilder.Build(Bars(100, 300_000), box: 1, TimeSpan.FromHours(1), maxBricks: 20_000);

        Assert.False(built.Ok);
        Assert.Contains("over 20,000 bricks", built.Refusal);
    }

    [Fact]
    public void A_flat_series_makes_no_bricks_and_no_refusal()
    {
        var built = Build(Bars(Enumerable.Repeat(100d, 30).ToArray()), box: 10);

        Assert.True(built.Ok);
        Assert.Empty(built.Bricks);
        Assert.Empty(built.Spans);
    }

    [Fact]
    public void ATR_comes_from_the_closed_bars_at_the_last_closed_one()
    {
        var bars = new List<Ohlcv>();
        for (int i = 0; i < 25; i++)
        {
            double b = 100 + Math.Sin(i / 2.0) * 5;
            bars.Add(new Ohlcv(Start.AddHours(i), b, b + 2 + i % 3, b - 2, b + (i % 2 == 0 ? 1 : -1), 10));
        }

        var closed = bars.Take(bars.Count - 1).ToArray();
        double expected = IndicatorMath.Atr(closed, 14)[^1];

        Assert.True(BrickChartBuilder.TryResolveBox(bars, new RenkoSettings(), out double box, out _));
        Assert.Equal(expected, box, 10);
    }

    [Fact]
    public void Too_little_history_for_the_ATR_is_the_fixed_sentence()
    {
        var bars = Bars(Enumerable.Range(0, 9).Select(i => 100d + i).ToArray());

        Assert.False(BrickChartBuilder.TryResolveBox(bars, new RenkoSettings(), out _, out string? refusal));
        Assert.Equal("Renko needs at least 16 bars for ATR 14; this chart has 9.", refusal);
    }

    [Fact]
    public void An_unusable_box_is_refused_and_no_brick_is_refused_at_the_plan_stage()
    {
        Assert.False(BrickChartBuilder.TryResolveBox(
            Bars(100, 110), new RenkoSettings(RenkoBoxMode.Fixed, 0), out _, out string? boxRefusal));
        Assert.Equal("A Renko box must be a positive price.", boxRefusal);

        Assert.False(BrickChartBuilder.TryPlan(
            Bars(100, 101), new RenkoSettings(RenkoBoxMode.Fixed, 10), TimeSpan.FromHours(1),
            out _, out string? emptyRefusal));
        Assert.StartsWith("No Renko bricks: price has not moved one box of ", emptyRefusal);
    }
}
