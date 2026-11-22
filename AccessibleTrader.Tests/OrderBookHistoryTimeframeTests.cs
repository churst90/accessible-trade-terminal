using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// The heatmap's history gives each bar the book seen while that bar was open, on any
/// timeframe. It used to look for a book up to one minute after the bar's opening time, so on an
/// hour or day chart no bar ever had one and the heatmap was empty (audit, archive 13159; fixed
/// 2026-11-22).
/// </summary>
public sealed class OrderBookHistoryTimeframeTests
{
    private static readonly DateTime T0 = new(2026, 11, 22, 14, 0, 0, DateTimeKind.Utc);

    private static List<OrderBookEntry> Book(double price) => new() { new OrderBookEntry(price, 1) };

    private static IReadOnlyList<Ohlcv> Bars(TimeSpan step, int count) =>
        Enumerable.Range(0, count).Select(i => new Ohlcv(T0 + step * i, 1, 1, 1, 1, 1)).ToList();

    [Fact]
    public void An_hour_bar_gets_the_last_book_taken_during_its_hour()
    {
        var history = new OrderBookHistoryService();
        history.AddSnapshot("BTC/USD", Book(100), Book(101), T0.AddMinutes(10));
        history.AddSnapshot("BTC/USD", Book(102), Book(103), T0.AddMinutes(37));
        history.AddSnapshot("BTC/USD", Book(104), Book(105), T0.AddMinutes(75)); // the next hour

        var got = history.GetHistory("BTC/USD", Bars(TimeSpan.FromHours(1), 3));

        Assert.Equal(102, got[0].Bids.Single().Price);
        Assert.Equal(104, got[1].Bids.Single().Price);
        Assert.Empty(got[2].Bids); // the forming bar: nothing taken since 16:00
    }

    [Fact]
    public void A_day_bar_gets_a_book_taken_in_the_afternoon()
    {
        var history = new OrderBookHistoryService();
        history.AddSnapshot("BTC/USD", Book(100), Book(101), T0.AddDays(1).AddHours(5));

        var got = history.GetHistory("BTC/USD", Bars(TimeSpan.FromDays(1), 2));

        Assert.Empty(got[0].Bids);
        Assert.Equal(100, got[1].Bids.Single().Price);
    }

    [Fact]
    public void A_bar_with_no_book_of_its_own_does_not_borrow_an_older_one()
    {
        // A gap (a dropped feed) is empty on the heatmap, not the last book seen before it.
        var history = new OrderBookHistoryService();
        history.AddSnapshot("BTC/USD", Book(100), Book(101), T0.AddSeconds(30));

        var got = history.GetHistory("BTC/USD", Bars(TimeSpan.FromMinutes(1), 3));

        Assert.Equal(100, got[0].Bids.Single().Price);
        Assert.Empty(got[1].Bids);
        Assert.Empty(got[2].Bids);
    }

    [Fact]
    public void The_one_minute_chart_still_works()
    {
        var history = new OrderBookHistoryService();
        history.AddSnapshot("BTC/USD", Book(100), Book(101), T0.AddSeconds(20));
        history.AddSnapshot("BTC/USD", Book(102), Book(103), T0.AddSeconds(80));

        var got = history.GetHistory("BTC/USD", Bars(TimeSpan.FromMinutes(1), 2));

        Assert.Equal(100, got[0].Bids.Single().Price);
        Assert.Equal(102, got[1].Bids.Single().Price);
    }
}
