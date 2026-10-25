using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// Whether a chart's market trades every day decides how many bars Cipher C's weeks and years are.
/// The original counts gold's and the dollar index's weeks as five bars, although TradingView dates
/// a fifth of their bars on a Sunday (UTC), and only crypto's as seven: read from its own "weeks
/// since the last weekly low" against its own labels on sixteen assets, 2026-10-23. A bar on a
/// Saturday or Sunday once meant "trades every day", so one stray weekend bar made gold's weeks
/// seven bars long, and twelvedata's gold (short Saturday and Sunday bars since 2024) flipped the
/// clock as the last 500 bars moved across them.
/// </summary>
public class CipherCClockTests
{
    private static readonly DateTime Start = new(2018, 1, 1);   // a Monday

    /// <summary>A bar with this range, on a gentle wave so the series is not flat.</summary>
    private static Ohlcv Bar(DateTime d, int i, double range)
    {
        double c = 100 + 10 * Math.Sin(i / 15.0);
        return new Ohlcv(d, c, c + range / 2, c - range / 2, c, 1000);
    }

    /// <summary>Daily bars over <paramref name="days"/> calendar days; <paramref name="range"/>
    /// says each day's range, or null for no bar that day.</summary>
    private static Ohlcv[] Daily(int days, Func<DateTime, double?> range, int hour = 0)
    {
        var bars = new List<Ohlcv>();
        for (int i = 0; i < days; i++)
        {
            var d = Start.AddDays(i).AddHours(hour);
            if (range(d) is double r) bars.Add(Bar(d, i, r));
        }
        return bars.ToArray();
    }

    private static bool Weekend(DateTime d) => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    [Fact]
    public void Bitcoin_trades_every_day()
    {
        var clock = CipherCEngine.MeasureBars(CipherSrSpeechTests.BtcDaily().ToArray());
        Assert.True(clock.TradesEveryDay);
        Assert.Equal(7, clock.DaysPerWeek);
    }

    /// <summary>The case the old rule got wrong: one Saturday in 500 bars.</summary>
    [Fact]
    public void One_stray_weekend_bar_does_not_make_a_market_trade_every_day()
    {
        var stray = Start.AddDays(1400);
        while (stray.DayOfWeek != DayOfWeek.Saturday) stray = stray.AddDays(1);
        var bars = Daily(1500, d => d == stray ? 2.0 : Weekend(d) ? null : 2.0);
        Assert.Contains(bars.TakeLast(500), b => b.Date.DayOfWeek == DayOfWeek.Saturday);

        var clock = CipherCEngine.MeasureBars(bars);
        Assert.False(clock.TradesEveryDay);
        // Gold's 20 to 28 weeks are 100 to 140 bars, as the original counts them.
        var tier = CipherCEngine.Tier(20 * clock.DaysPerWeek, 28 * clock.DaysPerWeek, clock.DaysPerWeek, clock);
        Assert.Equal(100, tier.MinBars, 6);
        Assert.Equal(140, tier.MaxBars, 6);
    }

    /// <summary>A stock exchange's occasional weekend session (a make-up day, a special auction) is
    /// a full bar, but one weekend in ten is not a market that trades every day.</summary>
    [Fact]
    public void Rare_full_weekend_sessions_do_not_make_a_market_trade_every_day()
    {
        var bars = Daily(1500, d => !Weekend(d) ? 2.0 : (d - Start).Days / 7 % 10 == 0 ? 2.0 : null);
        Assert.True(bars.TakeLast(500).Count(b => Weekend(b.Date)) >= 10);
        Assert.False(CipherCEngine.MeasureBars(bars).TradesEveryDay);
    }

    /// <summary>twelvedata's gold since 2024: a bar every Saturday at about a sixth of a weekday's
    /// range, and on Sundays one at a fiftieth. Gold does not trade then.</summary>
    [Fact]
    public void Short_weekend_bars_from_a_market_that_is_shut_do_not_make_it_trade_every_day()
    {
        var bars = Daily(1500, d => d.DayOfWeek switch
        {
            DayOfWeek.Saturday => 0.33,
            DayOfWeek.Sunday => 0.04,
            _ => 2.0,
        });
        Assert.False(CipherCEngine.MeasureBars(bars).TradesEveryDay);
    }

    /// <summary>The same once the weekend bars start part way through: every cut of the series
    /// keeps the same clock, so a replay agrees with the whole run.</summary>
    [Fact]
    public void The_clock_does_not_flip_when_short_weekend_bars_start_part_way_through()
    {
        var bars = Daily(1500, d => !Weekend(d) ? 2.0 : d < Start.AddDays(900) ? null
                                    : d.DayOfWeek == DayOfWeek.Saturday ? 0.33 : 0.04);
        for (int cut = 300; cut <= bars.Length; cut += 20)
            Assert.False(CipherCEngine.MeasureBars(bars.AsSpan(0, cut)).TradesEveryDay,
                $"the clock says gold trades every day at bar {cut} ({bars[cut - 1].Date:yyyy-MM-dd})");
    }

    /// <summary>The Dow's four-hour chart on TradingView: two bars a session, 13:30 and 17:30 UTC.
    /// Half the gaps are the 20-hour night, so the median gap is one; the chart once read as daily
    /// bars, a day a bar.</summary>
    [Fact]
    public void Two_bars_a_session_are_two_bars_a_day()
    {
        var bars = new List<Ohlcv>();
        for (int i = 0; i < 700; i++)
        {
            var d = Start.AddDays(i);
            if (Weekend(d)) continue;
            bars.Add(Bar(d.AddHours(13.5), bars.Count, 2.0));
            bars.Add(Bar(d.AddHours(17.5), bars.Count, 2.0));
        }
        var clock = CipherCEngine.MeasureBars(bars.ToArray());
        Assert.False(clock.TradesEveryDay);
        Assert.Equal(2, clock.BarsPerDay, 6);
    }

    /// <summary>And a stock's daily bars stay one a day.</summary>
    [Fact]
    public void One_bar_a_session_is_a_daily_chart()
    {
        var clock = CipherCEngine.MeasureBars(Daily(1500, d => Weekend(d) ? null : 2.0, hour: 14));
        Assert.False(clock.TradesEveryDay);
        Assert.Equal(1, clock.BarsPerDay, 6);
    }

    /// <summary>TradingView's gold and dollar index: five bars a week, Monday's dated Sunday 22:00 UTC.</summary>
    [Fact]
    public void Five_sessions_a_week_dated_from_Sunday_evening_are_a_five_day_week()
    {
        var bars = Daily(1500, d => d.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday ? null : 2.0, hour: 22);
        Assert.Contains(bars, b => b.Date.DayOfWeek == DayOfWeek.Sunday);
        var clock = CipherCEngine.MeasureBars(bars);
        Assert.False(clock.TradesEveryDay);
        Assert.Equal(1, clock.BarsPerDay, 6);
    }

    /// <summary>The mirror: a source east of the exchange dates each session the next day, Tuesday
    /// to Saturday. Saturdays alone are not a weekend traded.</summary>
    [Fact]
    public void Five_sessions_a_week_dated_Tuesday_to_Saturday_are_a_five_day_week()
    {
        var bars = Daily(1500, d => d.DayOfWeek is DayOfWeek.Sunday or DayOfWeek.Monday ? null : 2.0, hour: 4);
        Assert.Contains(bars, b => b.Date.DayOfWeek == DayOfWeek.Saturday);
        Assert.False(CipherCEngine.MeasureBars(bars).TradesEveryDay);
    }

    /// <summary>
    /// Both weekend days must turn up most weeks. A source that dates every session a day late
    /// (Tuesday to Saturday) and has the odd full Sunday session (one week in ten) has a Saturday
    /// every week, and its Sundays are full bars, so only how often Sundays come gives it away;
    /// likewise Sunday-to-Thursday dating with the odd Saturday.
    /// </summary>
    [Theory]
    [InlineData(DayOfWeek.Saturday, DayOfWeek.Sunday)]
    [InlineData(DayOfWeek.Sunday, DayOfWeek.Saturday)]
    public void One_weekend_day_every_week_and_the_other_seldom_is_not_trading_every_day(DayOfWeek every, DayOfWeek seldom)
    {
        var shifted = every == DayOfWeek.Saturday
            ? new[] { DayOfWeek.Sunday, DayOfWeek.Monday }     // Tuesday to Saturday
            : new[] { DayOfWeek.Friday, DayOfWeek.Saturday };  // Sunday to Thursday
        var bars = Daily(1500, d => d.DayOfWeek == seldom ? ((d - Start).Days / 7 % 10 == 0 ? 2.0 : null)
                                    : shifted.Contains(d.DayOfWeek) ? null : 2.0);
        Assert.True(bars.TakeLast(500).Count(b => b.Date.DayOfWeek == every) >= 60);
        Assert.True(bars.TakeLast(500).Count(b => b.Date.DayOfWeek == seldom) >= 5);
        Assert.False(CipherCEngine.MeasureBars(bars).TradesEveryDay);
    }

    /// <summary>A coin whose weekends are quiet still trades them: the lab's quietest stretch had
    /// weekend bars at 0.37 of a weekday's range on daily bars.</summary>
    [Fact]
    public void A_market_with_quiet_weekends_still_trades_every_day()
    {
        var bars = Daily(1500, d => Weekend(d) ? 0.7 : 2.0);   // 0.35 of a weekday
        Assert.True(CipherCEngine.MeasureBars(bars).TradesEveryDay);
    }

    /// <summary>
    /// Bitstamp's Bitcoin in 2011: a bar every day, but on most weekends nothing traded, so the bar
    /// is flat (high equals low). A flat bar says nothing about whether the market was open; the
    /// weekend bars that did trade are real sessions. Counting the flat ones made the first 300
    /// bars a five-day market and the rest seven, so every cut must agree.
    /// </summary>
    [Fact]
    public void Weekends_with_mostly_flat_bars_still_trade_every_day_from_the_first_bars()
    {
        var bars = Daily(700, d => Weekend(d) ? ((d - Start).Days % 5 < 3 ? 0.0 : 1.0)   // three in five flat
                                   : (d - Start).Days % 7 == 2 ? 0.0 : 2.0);            // the odd flat weekday
        Assert.True(bars.Count(b => Weekend(b.Date) && b.High == b.Low) * 2 > bars.Count(b => Weekend(b.Date)));
        for (int cut = 100; cut <= bars.Length; cut += 10)
            Assert.True(CipherCEngine.MeasureBars(bars.AsSpan(0, cut)).TradesEveryDay,
                $"the clock says a market that trades every day does not, at bar {cut} ({bars[cut - 1].Date:yyyy-MM-dd})");
    }

    /// <summary>The other side of leaving flat bars out: a source that fills a shut market's
    /// weekends with flat bars has no weekend sessions at all.</summary>
    [Fact]
    public void Flat_weekend_filler_bars_do_not_make_a_market_trade_every_day()
    {
        var bars = Daily(1500, d => Weekend(d) ? 0.0 : 2.0);
        Assert.True(bars.TakeLast(500).Count(b => Weekend(b.Date)) >= 100);
        Assert.False(CipherCEngine.MeasureBars(bars).TradesEveryDay);
    }

    [Fact]
    public void Two_day_bars_are_judged_by_how_many_fall_on_a_weekend()
    {
        var everyDay = Enumerable.Range(0, 800).Select(i => Bar(Start.AddDays(2 * i), i, 2.0)).ToArray();
        Assert.True(CipherCEngine.MeasureBars(everyDay).TradesEveryDay);

        // A stock's two-day bars: every other trading day, never a weekend.
        var days = Daily(2400, d => Weekend(d) ? null : 2.0);
        var stock = days.Where((_, i) => i % 2 == 0).ToArray();
        Assert.False(CipherCEngine.MeasureBars(stock).TradesEveryDay);
    }
}
