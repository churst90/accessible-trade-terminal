using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <see cref="ResamplerService"/> — the highest-value missing test file in the data area, and it
/// had none at all.
///
/// <para>
/// It matters more since resampled bars started being persisted: a 1h chart can now be served from
/// disk, so an aggregation error is not a display glitch that a refetch corrects, it is a wrong
/// candle written down and read back. And a wrong candle here is not silent — it is spoken, its
/// direction is sonified, chart formations are detected on it and strategies are backtested and
/// traded from it.
/// </para>
///
/// <para>
/// Every expected number below is hand-derived in the comment beside it. The fixture is built so
/// that Open, High, Low and Close come from four DIFFERENT input bars, which is what makes a
/// swapped or dropped field visible instead of coincidentally right.
/// </para>
/// </summary>
public class ResamplerServiceTests
{
    private static readonly DateTime H0 = new(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc);

    private static Ohlcv Bar(DateTime t, double o, double h, double l, double c, double v)
        => new(t, o, h, l, c, v);

    /// <summary>
    /// One hour of 15-minute bars, deliberately shaped so no two of O/H/L/C come from the same bar:
    /// the open is on the first, the high on the second, the low on the third, the close on the
    /// fourth.
    /// </summary>
    private static List<Ohlcv> OneHourOf15m() => new()
    {
        Bar(H0,                  100, 102,  99, 101,  10),   // Open 100 lives here
        Bar(H0.AddMinutes(15),   101, 110, 100, 105,  20),   // High 110 lives here
        Bar(H0.AddMinutes(30),   105, 106,  90,  95,  30),   // Low   90 lives here
        Bar(H0.AddMinutes(45),    95,  99,  94,  98,  40),   // Close 98 lives here
    };

    // ── The aggregation itself ──────────────────────────────────────────────

    [Fact]
    public void Four_15m_bars_become_one_1h_bar_that_takes_each_field_from_the_right_place()
    {
        var result = new ResamplerService().Resample(OneHourOf15m(), "1h");

        var bar = Assert.Single(result);
        Assert.Equal(100, bar.Open);            // first bar's open
        Assert.Equal(110, bar.High);            // max across all four
        Assert.Equal(90,  bar.Low);             // min across all four
        Assert.Equal(98,  bar.Close);           // last bar's close
        Assert.Equal(100, bar.Volume);          // 10+20+30+40
    }

    /// <summary>
    /// The timestamp convention, and it is the one that is easy to get backwards. The aggregated
    /// bar is stamped with the START of its bucket, not the time of the last bar that went into
    /// it. Stamping it 10:45 would make a 1h series whose bars are 15 minutes late, and every
    /// alignment downstream — the multi-timeframe join, the prepend/append discrimination, the
    /// spoken bar time — reads that stamp.
    /// </summary>
    [Fact]
    public void The_aggregated_bar_is_stamped_with_the_start_of_its_bucket()
    {
        var result = new ResamplerService().Resample(OneHourOf15m(), "1h");

        Assert.Equal(H0, Assert.Single(result).Date);
        Assert.Equal(DateTimeKind.Utc, result[0].Date.Kind);
    }

    /// <summary>
    /// A bucket that has not filled up still produces a bar. Worth pinning rather than assuming:
    /// the alternative design — withhold the bucket until it is complete — is equally defensible,
    /// and the choice is visible to a user, because a partial hour is charted and spoken exactly
    /// like a complete one. Downstream code that needs to know reads the bar's stamp and the clock.
    /// </summary>
    [Fact]
    public void A_bucket_with_only_some_of_its_bars_is_still_published()
    {
        var partial = OneHourOf15m().Take(2).ToList();   // 10:00 and 10:15 only

        var bar = Assert.Single(new ResamplerService().Resample(partial, "1h"));

        Assert.Equal(H0, bar.Date);
        Assert.Equal(100, bar.Open);
        Assert.Equal(110, bar.High);
        Assert.Equal(105, bar.Close);   // the last bar present, not the last bar of the hour
        Assert.Equal(30,  bar.Volume);
    }

    [Fact]
    public void Bars_spanning_two_buckets_produce_two_bars_in_ascending_order()
    {
        var bars = OneHourOf15m();
        bars.Add(Bar(H0.AddHours(1),           98, 99, 97, 97, 5));
        bars.Add(Bar(H0.AddHours(1).AddMinutes(30), 97, 97, 80, 81, 5));

        var result = new ResamplerService().Resample(bars, "1h");

        Assert.Equal(2, result.Count);
        Assert.Equal(H0,             result[0].Date);
        Assert.Equal(H0.AddHours(1), result[1].Date);
        Assert.Equal(98, result[1].Open);
        Assert.Equal(80, result[1].Low);
        Assert.Equal(81, result[1].Close);
        Assert.Equal(10, result[1].Volume);
    }

    /// <summary>
    /// Input order must not change the answer.
    ///
    /// <para>
    /// This was a live defect until 2026-08-26. Open and Close were taken from the first and last
    /// bar SEEN, so descending input — which plenty of venues return, and which
    /// <c>HistoricalDataFetcher</c> passes through untouched — produced every aggregated candle
    /// with its open and close swapped. A down hour reported as an up hour, on a chart that is read
    /// aloud, sonified by direction, and traded from. They are now taken by timestamp.
    /// </para>
    /// </summary>
    [Fact]
    public void Descending_input_produces_exactly_the_same_bar_as_ascending_input()
    {
        var ascending  = OneHourOf15m();
        var descending = OneHourOf15m();
        descending.Reverse();

        var fromAscending  = Assert.Single(new ResamplerService().Resample(ascending,  "1h"));
        var fromDescending = Assert.Single(new ResamplerService().Resample(descending, "1h"));

        Assert.Equal(fromAscending, fromDescending);
        // Named explicitly, because "they are equal" would also hold if both were wrong.
        Assert.Equal(100, fromDescending.Open);
        Assert.Equal(98,  fromDescending.Close);
    }

    [Fact]
    public void Shuffled_input_produces_the_same_bar_too()
    {
        var shuffled = new List<Ohlcv>
        {
            OneHourOf15m()[2], OneHourOf15m()[0], OneHourOf15m()[3], OneHourOf15m()[1],
        };

        var bar = Assert.Single(new ResamplerService().Resample(shuffled, "1h"));

        Assert.Equal(100, bar.Open);
        Assert.Equal(110, bar.High);
        Assert.Equal(90,  bar.Low);
        Assert.Equal(98,  bar.Close);
    }

    // ── Bucket boundaries ───────────────────────────────────────────────────

    [Fact]
    public void The_bar_exactly_on_a_boundary_belongs_to_the_bucket_it_opens()
    {
        var bars = new List<Ohlcv>
        {
            Bar(H0.AddMinutes(45), 95, 99, 94, 98, 1),   // last bar of the 10:00 hour
            Bar(H0.AddHours(1),    98, 98, 98, 98, 1),   // first bar of the 11:00 hour
        };

        var result = new ResamplerService().Resample(bars, "1h");

        Assert.Equal(2, result.Count);
        Assert.Equal(H0,             result[0].Date);
        Assert.Equal(H0.AddHours(1), result[1].Date);
    }

    /// <summary>
    /// Weeks align to Monday 00:00 UTC — not to the first bar seen, and not to Sunday.
    /// 2026-03-10 is a Tuesday, so its week opens on Monday 2026-03-09.
    /// </summary>
    [Fact]
    public void Weekly_buckets_start_on_Monday_at_midnight_UTC()
    {
        var monday = new DateTime(2026, 3, 9, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(DayOfWeek.Monday, monday.DayOfWeek);

        var bars = new List<Ohlcv>
        {
            Bar(new DateTime(2026, 3,  9,  6, 0, 0, DateTimeKind.Utc), 10, 12,  9, 11, 1),
            Bar(new DateTime(2026, 3, 13, 18, 0, 0, DateTimeKind.Utc), 11, 15, 11, 14, 1),  // Friday
            Bar(new DateTime(2026, 3, 15, 23, 0, 0, DateTimeKind.Utc), 14, 14, 13, 13, 1),  // Sunday
            Bar(new DateTime(2026, 3, 16,  1, 0, 0, DateTimeKind.Utc), 13, 20, 13, 19, 1),  // next Monday
        };

        var result = new ResamplerService().Resample(bars, "1w");

        Assert.Equal(2, result.Count);
        Assert.Equal(monday, result[0].Date);
        Assert.Equal(DayOfWeek.Monday, result[1].Date.DayOfWeek);
        // Sunday belongs to the week that opened on the Monday before it, not to the next one.
        Assert.Equal(3, result[0].Volume);
        Assert.Equal(1, result[1].Volume);
        Assert.Equal(15, result[0].High);
        Assert.Equal(13, result[0].Close);
    }

    /// <summary>
    /// Monthly buckets land on the 1st at midnight. A month is not a fixed number of milliseconds,
    /// so this is the one timeframe that cannot be done by modulo arithmetic on the epoch —
    /// February would drag every later bucket earlier.
    /// </summary>
    [Fact]
    public void Monthly_buckets_start_on_the_first_of_the_month()
    {
        var bars = new List<Ohlcv>
        {
            Bar(new DateTime(2026, 2,  1,  0, 0, 0, DateTimeKind.Utc), 10, 11,  9, 10, 1),
            Bar(new DateTime(2026, 2, 28, 23, 0, 0, DateTimeKind.Utc), 10, 12,  8, 12, 1),
            Bar(new DateTime(2026, 3,  1,  0, 0, 0, DateTimeKind.Utc), 12, 13, 12, 13, 1),
            Bar(new DateTime(2026, 3, 31, 23, 0, 0, DateTimeKind.Utc), 13, 20, 13, 19, 1),
        };

        var result = new ResamplerService().Resample(bars, "1M");

        Assert.Equal(2, result.Count);
        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), result[0].Date);
        Assert.Equal(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), result[1].Date);
        Assert.Equal(8,  result[0].Low);
        Assert.Equal(12, result[0].Close);
        Assert.Equal(20, result[1].High);
    }

    /// <summary>
    /// A multi-month timeframe aligns to the quarter, not to a rolling three months from whenever
    /// the data happens to begin: <c>(month-1)/3*3+1</c> maps Jan–Mar to January, Apr–Jun to April.
    /// A May bar arriving first must not open a "May–July" bucket.
    /// </summary>
    [Fact]
    public void Three_month_buckets_align_to_the_calendar_quarter_not_to_the_first_bar()
    {
        var bars = new List<Ohlcv>
        {
            Bar(new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc), 10, 11,  9, 10, 1),
            Bar(new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc), 10, 14,  9, 13, 1),
            Bar(new DateTime(2026, 7,  2, 0, 0, 0, DateTimeKind.Utc), 13, 15, 12, 14, 1),
        };

        var result = new ResamplerService().Resample(bars, "3M");

        Assert.Equal(2, result.Count);
        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), result[0].Date);
        Assert.Equal(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), result[1].Date);
        Assert.Equal(2, result[0].Volume);   // May and June together
        Assert.Equal(1, result[1].Volume);
    }

    /// <summary>
    /// Daily buckets are epoch-modulo, so they are UTC midnights and a clock change is a non-event.
    ///
    /// <para>
    /// 2026-03-29 is when European clocks go forward. Bars either side of it must still bucket on
    /// UTC midnight and each day must still be one bucket — a resampler that reached for local time
    /// anywhere would produce a 23-hour day here and a 25-hour one in October, and the symptom
    /// (one day's bars split across two buckets, or two days merged) is exactly the kind that only
    /// appears twice a year.
    /// </para>
    /// </summary>
    [Fact]
    public void A_daylight_saving_change_does_not_move_a_daily_bucket()
    {
        var bars = new List<Ohlcv>
        {
            Bar(new DateTime(2026, 3, 28, 23, 30, 0, DateTimeKind.Utc), 10, 11,  9, 10, 1),
            Bar(new DateTime(2026, 3, 29,  0, 30, 0, DateTimeKind.Utc), 10, 12, 10, 11, 1),  // after the change
            Bar(new DateTime(2026, 3, 29, 23, 30, 0, DateTimeKind.Utc), 11, 13, 11, 12, 1),
            Bar(new DateTime(2026, 3, 30,  0, 30, 0, DateTimeKind.Utc), 12, 14, 12, 13, 1),
        };

        var result = new ResamplerService().Resample(bars, "1d");

        Assert.Equal(3, result.Count);
        Assert.Equal(new DateTime(2026, 3, 28, 0, 0, 0, DateTimeKind.Utc), result[0].Date);
        Assert.Equal(new DateTime(2026, 3, 29, 0, 0, 0, DateTimeKind.Utc), result[1].Date);
        Assert.Equal(new DateTime(2026, 3, 30, 0, 0, 0, DateTimeKind.Utc), result[2].Date);
        Assert.Equal(2, result[1].Volume);   // both of the 29th's bars, and only those
    }

    // ── Degenerate input ────────────────────────────────────────────────────

    [Fact]
    public void An_empty_or_null_series_resamples_to_an_empty_series()
    {
        var svc = new ResamplerService();

        Assert.Empty(svc.Resample(new List<Ohlcv>(), "1h"));
        Assert.Empty(svc.Resample(null!, "1h"));
    }

    /// <summary>
    /// 1m is the storage granularity, so resampling to it is a copy — but it must be a copy, not
    /// the caller's own list handed back. <c>HistoricalDataFetcher</c> filters the result in place
    /// afterwards, and doing that to the provider's list would mutate data another caller is
    /// holding.
    /// </summary>
    [Fact]
    public void Resampling_to_the_native_timeframe_returns_an_equal_but_separate_list()
    {
        var input = OneHourOf15m();

        var result = new ResamplerService().Resample(input, "1m");

        Assert.NotSame(input, result);
        Assert.Equal(input, result);
        result.Clear();
        Assert.Equal(4, input.Count);
    }

    // ── GetBucketStart, the other half of the interface ─────────────────────

    [Fact]
    public void GetBucketStart_agrees_with_where_Resample_puts_the_bar()
    {
        var svc = new ResamplerService();
        var t = H0.AddMinutes(37);

        long bucket = svc.GetBucketStart(t, "1h");

        Assert.Equal(new DateTimeOffset(H0).ToUnixTimeMilliseconds(), bucket);
        var bar = Assert.Single(svc.Resample(new List<Ohlcv> { Bar(t, 1, 1, 1, 1, 1) }, "1h"));
        Assert.Equal(bucket, new DateTimeOffset(bar.Date).ToUnixTimeMilliseconds());
    }
}
