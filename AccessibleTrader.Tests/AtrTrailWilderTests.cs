using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Tests;

/// <summary>
/// The ATR trailing stop measures ATR the way the chart does — Wilder's smoothing — so the
/// trail sits at the distance the ATR pane reads out and the strategy's stop was sized from.
/// Until 2026-11-20 the trail used a plain mean of the last <c>period</c> true ranges, a
/// different number for the same bars (code review; Cody: "make them consistent").
/// </summary>
public class AtrTrailWilderTests
{
    private const int Period = 14;
    private const double Multiple = 2.0;

    /// <summary>
    /// Calm bars, then a burst of wide ones. The plain mean of the last 14 true ranges and
    /// Wilder's smoothed average come apart on a series like this, which is what lets the
    /// tests below tell the two averages apart at all.
    /// </summary>
    private static List<Ohlcv> Bars()
    {
        var bars = new List<Ohlcv>();
        double price = 100;
        for (int i = 0; i < 40; i++)
        {
            double range = i < 20 ? 1.0 : 4.0 + (i % 3);
            price += i < 20 ? 0.1 : 0.8;
            bars.Add(new Ohlcv(new DateTime(2026, 1, 1, 0, i, 0, DateTimeKind.Utc),
                price - 0.2, price + range / 2, price - range / 2, price, 1000));
        }
        return bars;
    }

    private static double PlainMeanAtr(IReadOnlyList<Ohlcv> bars, int at, int period)
    {
        double sum = 0;
        for (int a = at - period + 1; a <= at; a++)
        {
            double tr = bars[a].High - bars[a].Low;
            if (a > 0)
            {
                tr = Math.Max(tr, Math.Abs(bars[a].High - bars[a - 1].Close));
                tr = Math.Max(tr, Math.Abs(bars[a].Low  - bars[a - 1].Close));
            }
            sum += tr;
        }
        return sum / period;
    }

    [Theory]
    [InlineData(OrderSide.Buy)]
    [InlineData(OrderSide.Sell)]
    public void The_trail_sits_at_the_charts_wilder_atr(OrderSide side)
    {
        var bars = Bars();
        int at = 30;
        double wilder = IndicatorMath.Atr(bars.ToArray(), Period)[at];
        double plain = PlainMeanAtr(bars, at, Period);

        // Vacuity control: on these bars the two averages really differ, by more than the
        // tolerance below, so a trail still on the plain mean cannot pass.
        Assert.True(Math.Abs(wilder - plain) * Multiple > 0.1,
            $"Wilder {wilder} and the plain mean {plain} are too close to tell apart.");

        double farAway = side == OrderSide.Buy ? 0 : 1_000_000;
        double stop = ManagedExitRules.AtrTrailStop(bars, at, Period, Multiple, side, farAway);

        double expected = side == OrderSide.Buy
            ? bars[at].Close - Multiple * wilder
            : bars[at].Close + Multiple * wilder;
        Assert.Equal(expected, stop, 9);
    }

    /// <summary>
    /// The backtester reads a series worked out once; the live manager computes the value bar
    /// by bar. Both must land on the same stop at every bar, or the replay and the live trail
    /// drift apart again.
    /// </summary>
    [Fact]
    public void The_replays_series_and_the_live_bar_by_bar_trail_agree_at_every_bar()
    {
        var bars = Bars();
        var series = ManagedExitRules.TrailAtrSeries(bars, Period);
        for (int at = 0; at < bars.Count; at++)
        {
            double live = ManagedExitRules.AtrTrailStop(bars, at, Period, Multiple, OrderSide.Buy, 0);
            double replay = ManagedExitRules.AtrTrailStop(series[at], bars[at].Close, Multiple, OrderSide.Buy, 0);
            Assert.Equal(replay, live, 9);
        }
    }

    /// <summary>Wilder's average needs <c>period</c> bars before it exists; until then the
    /// stop stays where it was, exactly as the old gate did.</summary>
    [Fact]
    public void Before_period_bars_the_stop_is_left_alone()
    {
        var bars = Bars();
        for (int at = 0; at < Period; at++)
            Assert.Equal(50.0, ManagedExitRules.AtrTrailStop(bars, at, Period, Multiple, OrderSide.Buy, 50.0));
        Assert.NotEqual(50.0, ManagedExitRules.AtrTrailStop(bars, Period, Period, Multiple, OrderSide.Buy, 50.0));
    }

    /// <summary>
    /// A bar that comes later must not move the trail worked out for an earlier one — the live
    /// manager passes its whole history, and the stop at bar 25 is what bar 25 knew.
    /// </summary>
    [Fact]
    public void Later_bars_do_not_move_an_earlier_trail()
    {
        var bars = Bars();
        double withLater = ManagedExitRules.AtrTrailStop(bars, 25, Period, Multiple, OrderSide.Buy, 0);
        double without = ManagedExitRules.AtrTrailStop(bars.Take(26).ToList(), 25, Period, Multiple, OrderSide.Buy, 0);
        Assert.Equal(without, withLater, 12);
    }
}
