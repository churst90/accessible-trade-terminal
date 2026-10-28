using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cipher C's bars past the last one are the ones the chart would print (2026-10-28).</b>
/// Read from the original's band ends past the last bar on 21 assets: the chart steps on at its
/// session's time of day in its own zone (UTC for crypto, US-Eastern with daylight saving for
/// New York stocks and the Sunday-stamped futures sessions), and skips the US holidays it skipped
/// the last time they fell on a weekday. Future daily ends 56 -> 70 of 70, weekly 37 -> 115 of 128.
/// </summary>
public class CipherCFutureClockTests
{
    private static readonly TimeZoneInfo Et = ExchangeTime.UsEastern;

    /// <summary>One bar a session from <paramref name="from"/> to <paramref name="to"/>, stamped at
    /// <paramref name="local"/> US-Eastern on the weekday before the session when <paramref name="sundayStamped"/>,
    /// skipping the sessions <paramref name="closed"/> says.</summary>
    private static Ohlcv[] Sessions(DateOnly from, DateOnly to, TimeSpan local, Func<DateOnly, bool> closed, bool sundayStamped = false)
    {
        var bars = new List<Ohlcv>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || closed(d)) continue;
            var day = sundayStamped ? d.AddDays(-1) : d;
            var at = DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue) + local, DateTimeKind.Unspecified);
            bars.Add(new Ohlcv(TimeZoneInfo.ConvertTimeToUtc(at, Et), 100, 101, 99, 100, 1000));
        }
        return bars.ToArray();
    }

    private static bool UsHoliday(DateOnly d)
    {
        for (int h = 0; h < CipherCFutureClock.HolidayNames.Length; h++)
        {
            if (h == 5 && d.Year < 2022) continue;   // Juneteenth: closed from 2022
            if (CipherCFutureClock.Holiday(h, d.Year) == d) return true;
        }
        return false;
    }

    /// <summary>New York stocks open at 09:30 US-Eastern: 13:30 UTC in summer, 14:30 in winter, past the last bar too.</summary>
    [Fact]
    public void A_stock_chart_steps_on_at_its_open_with_daylight_saving()
    {
        var bars = Sessions(new DateOnly(2025, 6, 2), new DateOnly(2026, 10, 23), new TimeSpan(9, 30, 0), UsHoliday);
        var clock = new CipherCFutureClock(bars, everyDay: false);
        Assert.Same(Et, clock.Zone);
        var next = clock.Future(10).ToList();
        Assert.Equal(new DateTime(2026, 10, 26, 13, 30, 0), next[0]);           // Monday, summer time
        Assert.Equal(new DateTime(2026, 11, 2, 14, 30, 0), next[5]);            // after the clocks go back
        Assert.All(next, t => Assert.Equal(new TimeSpan(9, 30, 0), TimeZoneInfo.ConvertTimeFromUtc(t, Et).TimeOfDay));
        // A target at 14:00 UTC: the same day's bar in winter, the next day's in summer.
        int last = bars.Length - 1;
        Assert.Equal(last + 2, clock.BarOnOrAfter(new DateTime(2026, 10, 26, 14, 0, 0), last));
        Assert.Equal(last + 6, clock.BarOnOrAfter(new DateTime(2026, 11, 2, 14, 0, 0), last));
    }

    /// <summary>The US holidays the chart skipped last time are skipped past the last bar:
    /// Thanksgiving and Christmas 2026 are not bars; a chart that traded them steps through them.</summary>
    [Fact]
    public void Holidays_the_chart_skipped_are_skipped_past_the_last_bar()
    {
        var bars = Sessions(new DateOnly(2019, 1, 2), new DateOnly(2026, 11, 20), new TimeSpan(9, 30, 0), UsHoliday);
        var clock = new CipherCFutureClock(bars, everyDay: false);
        Assert.Equal(CipherCFutureClock.HolidayNames, clock.Observed);
        var days = clock.Future(30).Select(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(t, Et))).ToList();
        Assert.DoesNotContain(new DateOnly(2026, 11, 26), days);
        Assert.DoesNotContain(new DateOnly(2026, 12, 25), days);
        Assert.Contains(new DateOnly(2026, 11, 27), days);

        var open = Sessions(new DateOnly(2019, 1, 2), new DateOnly(2026, 11, 20), new TimeSpan(9, 30, 0), _ => false);
        var openClock = new CipherCFutureClock(open, everyDay: false);
        Assert.Empty(openClock.Observed);
        Assert.Contains(new DateOnly(2026, 11, 26), openClock.Future(30).Select(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(t, Et))));
    }

    /// <summary>Only the newest weekday occurrence counts: the Dow traded Juneteenth 2021 and closed
    /// on 20 June 2022, so a chart to the end of 2022 skips it in 2023.</summary>
    [Fact]
    public void A_holiday_is_read_from_its_newest_weekday_occurrence()
    {
        var bars = Sessions(new DateOnly(2019, 1, 2), new DateOnly(2022, 12, 30), new TimeSpan(9, 30, 0), UsHoliday);
        Assert.Contains(bars, b => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(b.Date, Et)) == new DateOnly(2021, 6, 18));
        var clock = new CipherCFutureClock(bars, everyDay: false);
        Assert.Contains("Juneteenth", clock.Observed);
        var days = clock.Future(130).Select(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(t, Et))).ToList();
        Assert.DoesNotContain(new DateOnly(2023, 6, 19), days);
        Assert.Contains(new DateOnly(2023, 6, 20), days);
    }

    /// <summary>A futures-style session opens at 18:00 US-Eastern the evening before and is stamped
    /// then: Monday's bar on the Sunday, none on Friday. Past a Thursday-evening bar the next is
    /// Sunday evening's, and the holiday the session skipped (Good Friday) is still skipped.</summary>
    [Fact]
    public void A_session_stamped_the_evening_before_steps_on_from_Sunday()
    {
        var bars = Sessions(new DateOnly(2024, 1, 2), new DateOnly(2026, 10, 23), new TimeSpan(18, 0, 0),
            d => CipherCFutureClock.Holiday(3, d.Year) == d, sundayStamped: true);
        Assert.Contains(bars, b => TimeZoneInfo.ConvertTimeFromUtc(b.Date, Et).DayOfWeek == DayOfWeek.Sunday);
        Assert.DoesNotContain(bars, b => TimeZoneInfo.ConvertTimeFromUtc(b.Date, Et).DayOfWeek == DayOfWeek.Friday);
        var clock = new CipherCFutureClock(bars, everyDay: false);
        Assert.Same(Et, clock.Zone);
        Assert.Equal(new[] { "Good Friday" }, clock.Observed);
        var next = clock.Future(3).Select(t => TimeZoneInfo.ConvertTimeFromUtc(t, Et)).ToList();
        Assert.Equal(new DateTime(2026, 10, 25, 18, 0, 0), next[0]);           // Sunday evening: Monday's session
        Assert.Equal(new DateTime(2026, 10, 26, 18, 0, 0), next[1]);
        // Good Friday 2027 is 26 March: no bar on Thursday evening the 25th.
        var spring = clock.Future(150).Select(t => TimeZoneInfo.ConvertTimeFromUtc(t, Et).Date).ToList();
        Assert.DoesNotContain(new DateTime(2027, 3, 25), spring);
        Assert.Contains(new DateTime(2027, 3, 24), spring);
    }

    /// <summary>Crypto is stamped at midnight UTC every day: past the last bar, every day at midnight, no holidays.</summary>
    [Fact]
    public void A_crypto_chart_steps_on_every_day_in_UTC()
    {
        var bars = Enumerable.Range(0, 400).Select(i => new Ohlcv(new DateTime(2025, 9, 1).AddDays(i), 100, 101, 99, 100, 1000)).ToArray();
        var clock = new CipherCFutureClock(bars, everyDay: true);
        Assert.Same(TimeZoneInfo.Utc, clock.Zone);
        Assert.Empty(clock.Observed);
        Assert.Equal(Enumerable.Range(1, 60).Select(i => bars[^1].Date.AddDays(i)), clock.Future(60));
    }

    /// <summary>Bars at no one time of day in either zone step on at the last bar's time on weekdays.</summary>
    [Fact]
    public void A_chart_with_no_session_time_steps_on_at_the_last_bars_time_on_weekdays()
    {
        var bars = new List<Ohlcv>();
        var d = new DateTime(2025, 1, 6);
        for (int i = 0; bars.Count < 300; i++, d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                bars.Add(new Ohlcv(d.AddMinutes(37 * (bars.Count % 11)), 100, 101, 99, 100, 1000));
        var clock = new CipherCFutureClock(bars.ToArray(), everyDay: false);
        Assert.Null(clock.Zone);
        Assert.Empty(clock.Observed);
        var next = clock.Future(6).ToList();
        Assert.All(next, t => Assert.Equal(bars[^1].Date.TimeOfDay, t.TimeOfDay));
        Assert.DoesNotContain(next, t => t.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
    }
}
