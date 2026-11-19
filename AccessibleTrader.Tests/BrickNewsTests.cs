using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Charts;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>What a Renko chart nobody is looking at says when a time bar closes.</summary>
public sealed class BrickNewsTests
{
    private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ChartIdentity Eth = new("Crypto", "Kraken", "ETH/USD", "1h");
    private static List<Ohlcv> Bars(params double[] closes) =>
        closes.Select((c, i) => new Ohlcv(T0.AddHours(i), c, c, c, c, 1)).ToList();
    private static string P(double v) => SpeechPriceFormatter.FormatPrice(v);

    [Fact]
    public void The_bricks_are_the_ones_the_closed_bar_completed()
    {
        // 100 → 112 → 134, then the forming bar at 180. Bar 2 (134) completes 110-120 and 120-130.
        var news = BrickNews.CompletedAtClose(Bars(100, 112, 134, 180), 10, Eth);

        Assert.Equal(2, news.Count);
        Assert.Equal(new[] { 110.0, 120.0 }, news.Select(n => n.Brick.Open));
        Assert.All(news, n => Assert.Equal(2, n.Span.LastBar));
        Assert.Equal($"ETH/USD 1h: 2 new up bricks, {P(110)} to {P(130)}.", BrickNews.Sentence(Eth, news));
    }

    [Fact]
    public void The_forming_bar_makes_no_news()
    {
        Assert.Empty(BrickNews.CompletedAtClose(Bars(100, 104, 180), 10, Eth));
    }

    [Fact]
    public void A_turn_is_one_down_brick_and_the_ladder_follows()
    {
        // Up to 130, then a close at 105: the reversal brick 120 down to 110.
        var news = BrickNews.CompletedAtClose(Bars(100, 131, 105, 105), 10, Eth);

        var one = Assert.Single(news);
        Assert.False(one.Span.Up);
        Assert.Equal($"ETH/USD 1h: new down brick, {P(120)} to {P(110)}. RSI crossed 50.",
            BrickNews.Sentence(Eth, news, "RSI crossed 50."));
    }

    [Fact]
    public void The_frozen_box_wins_over_the_settings()
    {
        Assert.Equal(25, BrickNews.BoxFor(Bars(100, 110), new RenkoSettings(RenkoBoxMode.Fixed, 10), 25));
        Assert.Equal(10, BrickNews.BoxFor(Bars(100, 110), new RenkoSettings(RenkoBoxMode.Fixed, 10), 0));
        Assert.Equal(0, BrickNews.BoxFor(Bars(100, 110), new RenkoSettings(RenkoBoxMode.Atr), 0));   // too few bars for ATR
    }

    [Fact]
    public void Too_few_bars_or_no_box_is_silence()
    {
        Assert.Empty(BrickNews.CompletedAtClose(Bars(100), 10, Eth));
        Assert.Empty(BrickNews.CompletedAtClose(Bars(100, 200, 300), 0, Eth));
    }
}
