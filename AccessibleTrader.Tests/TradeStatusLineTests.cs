using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Tests;

/// <summary>The words the trade status bar shows for a position (2026-11-04); see <see cref="TradeStatusLine"/>.</summary>
public sealed class TradeStatusLineTests
{
    private static readonly TradingAccount Kraken = new("KrakenFutures", "Kraken Futures", "krakenfutures", IsPaper: false);

    [Fact]
    public void Cody_s_example_reads_as_he_wrote_it()
    {
        // 0.1 BTC isolated 1x from 50,000, now 57,150: 715 up on 5,000 put up is 14.3%.
        var p = new Position("BTCUSDT", 0.1, 50_000, 5_715, 715, 1, 0, MarginMode.Isolated);
        var item = TradeStatusLine.Describe(new AccountPosition(Kraken, p), "percent");
        Assert.Equal("BTCUSDT Kraken Futures isolated 1x long, up 14.3%", item.Text);
        Assert.Equal(1, item.Sign);
    }

    [Fact]
    public void The_percent_is_of_the_margin_so_leverage_multiplies_the_price_move()
    {
        // A 1% price move on 10x is 10% of the margin.
        var p = new Position("ETHUSDT", 2, 3_000, 6_060, 60, 10, 2_700, MarginMode.Cross);
        Assert.Equal(10.0, TradeStatusLine.ReturnPercent(p)!.Value, 9);
    }

    [Theory]
    [InlineData("amount", "ETHUSDT Kraken Futures long, down 1,234.50")]
    [InlineData("both", "ETHUSDT Kraken Futures long, down 41.2%, 1,234.50")]
    public void The_amount_can_be_shown_instead_or_as_well(string figure, string expected)
    {
        var p = new Position("ETHUSDT", 1, 3_000, 1_765.5, -1_234.5);
        Assert.Equal(expected, TradeStatusLine.Describe(new AccountPosition(Kraken, p), figure).Text);
    }

    [Fact]
    public void No_entry_price_falls_back_to_the_amount_and_nothing_is_flat_is_flat()
    {
        var noEntry = new Position("SOLUSD", 3, 0, 450, 12.5);
        Assert.Null(TradeStatusLine.ReturnPercent(noEntry));
        Assert.Equal("SOLUSD Kraken Futures long, up 12.50", TradeStatusLine.Describe(new AccountPosition(Kraken, noEntry), "percent").Text);

        var flat = new Position("SOLUSD", -3, 150, 450, 0.001);
        var item = TradeStatusLine.Describe(new AccountPosition(Kraken, flat), "percent");
        Assert.Equal("SOLUSD Kraken Futures short, flat", item.Text);
        Assert.Equal(0, item.Sign);
    }
}
