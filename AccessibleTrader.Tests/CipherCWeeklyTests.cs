using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// Cipher C's weekly tier on a chart with a daily one picks its weekly lows from the daily lows as
/// the original does (<see cref="CipherCEngine.WeeklyFromDaily"/>). The rule was read from the
/// original's own labels in TradingView (trial, 2026-10-23): replayed bar by bar on GDX from
/// October 2023, the Dow from October 2022 and Bitcoin from June 2022, it reproduces every weekly
/// low the original drew and when. That data cannot be committed, so these tests drive the engine
/// with hand-built daily lows, one branch of the rule each, and the expected lows are worked out
/// here by hand from the rule, not read from the engine.
///
/// <para>
/// Every case keeps a weekly low only when the daily low that judges it is 90 bars from the last
/// kept one (GDX and the Dow: shortest weekly cycle 22 weeks, less 4, of 5 bars), and the daily
/// shortest cycle is 22 bars.
/// </para>
/// </summary>
public class CipherCWeeklyTests
{
    private const double Keep = 90, DailyMin = 22;

    /// <summary>The distance the original keeps a weekly low at: 90 bars on GDX and the Dow (22
    /// weeks, 5 bars to the week), 126 on Bitcoin (22 weeks, 7). On GDX anything from 84 to 100
    /// reproduces the original's weekly lows; 83 and 101 do not.</summary>
    [Theory]
    [InlineData(22, 5, 90)]
    [InlineData(22, 7, 126)]
    [InlineData(7, 7, 21)]    // Ethereum
    [InlineData(3, 5, 0)]     // never below nothing
    public void A_weekly_low_is_kept_four_weeks_short_of_the_shortest_weekly_cycle(double weeklyMin, double barsPerWeek, double bars) =>
        Assert.Equal(bars, CipherCEngine.KeepBars(weeklyMin, barsPerWeek));

    /// <summary>A daily run whose lows are (bar, price, bar confirmed); every other bar's low is 1000.</summary>
    private static (CipherCEngine.TierRun Daily, Ohlcv[] Data) Daily(int n, params (int Bar, double Low, int At)[] lows)
    {
        var data = Enumerable.Range(0, n).Select(i => new Ohlcv(new DateTime(2020, 1, 1).AddDays(i), 1000, 1000, 1000, 1000, 0)).ToArray();
        var run = new CipherCEngine.TierRun
        {
            LastLowAt = Enumerable.Repeat(-1, n).ToArray(),
            ClosedUnder = Enumerable.Repeat(-1, n).ToArray(),
            Confirmed = CipherCEngine.Nan(n), Cancelled = CipherCEngine.Nan(n), Updated = CipherCEngine.Nan(n),
            Pending = CipherCEngine.Nan(n), Failed = CipherCEngine.Nan(n),
        };
        foreach (var (bar, low, at) in lows)
        {
            data[bar] = data[bar] with { Low = low };
            run.Confirmed[at] = low;
            for (int t = at; t < n; t++) run.LastLowAt[t] = bar;
            run.Lows.Add(bar); run.ConfirmedAt.Add(at); run.FoundAt.Add(at);
        }
        return (run, data);
    }

    private static CipherCEngine.TierRun Weekly(int n, params (int Bar, double Low, int At)[] lows)
    {
        var (daily, data) = Daily(n, lows);
        return CipherCEngine.WeeklyFromDaily(daily, data, Keep, DailyMin);
    }

    private static int[] Marked(double[] a) => Enumerable.Range(0, a.Length).Where(i => !double.IsNaN(a[i])).ToArray();

    /// <summary>
    /// A daily low is drawn as the weekly candidate; the next daily low keeps it if that one is 90
    /// bars from the kept weekly low, and removes it if not. The first low has nothing to be judged
    /// from and is kept by the next. The original's week count runs from the kept weekly low before
    /// the newest, so it starts once two are kept.
    /// </summary>
    [Theory]
    [InlineData(99, false)]   // 89 bars from the kept low at 10: removed
    [InlineData(100, true)]   // 90: kept
    public void A_candidate_is_kept_only_when_the_next_daily_low_is_far_enough_from_the_last_kept_one(int checkpoint, bool kept)
    {
        var w = Weekly(300,
            (10, 100, 20),            // the first candidate
            (50, 110, 60),            // keeps it
            (70, 105, 80),            // the next candidate
            (checkpoint, 108, 110),   // judges it
            (150, 103, 160),          // a candidate
            (180, 107, 190));         // judges it: far enough from the last kept low either way
        if (kept)
        {
            Assert.Equal(new[] { 10, 70, 150 }, w.Lows);
            Assert.Equal(new[] { 60, 110, 190 }, w.ConfirmedAt);
            Assert.Equal(new[] { 20, 80, 160 }, w.FoundAt);
            Assert.Equal(new[] { 110, 190 }, Marked(w.Confirmed));      // 10, then 70, starts the count
            Assert.Equal(10, w.LastLowAt[189]);
            Assert.Equal(70, w.LastLowAt[190]);
        }
        else
        {
            Assert.Equal(new[] { 10, 150 }, w.Lows);
            Assert.Equal(new[] { 60, 190 }, w.ConfirmedAt);
            Assert.Equal(new[] { 190 }, Marked(w.Confirmed));
            Assert.Equal(-1, w.LastLowAt[189]);
            Assert.Equal(10, w.LastLowAt[190]);
        }
        Assert.Equal(100, w.Confirmed[w.LastLowAt.ToList().IndexOf(10)]);
        Assert.Empty(Marked(w.Cancelled));
    }

    /// <summary>
    /// A candidate under the newest kept weekly low, judged less than 90 bars from it, shows that
    /// kept low was not the weekly low: it is dropped (a cancellation, at its price) and the
    /// candidate judged again from the one before. Being under that one too, the next daily low
    /// decides the "double": the low that judged it is kept as a second weekly low if it is under
    /// the kept low before the candidate, and silently passed over if not. The next confirmation
    /// says which kept low it replaces.
    /// </summary>
    [Theory]
    [InlineData(99, true)]     // under the 100 low the candidate undercut: drawn
    [InlineData(101, false)]   // over it: not drawn
    public void A_kept_low_undercut_too_soon_is_dropped_and_the_low_after_decides_the_double(double checkpointLow, bool drawn)
    {
        var w = Weekly(400,
            (10, 100, 20), (50, 110, 60),     // 10 kept at 60
            (120, 103, 130), (150, 107, 160), // 120 kept at 160: the count runs from 10
            (170, 95, 180),                   // a candidate under 120
            (190, checkpointLow, 200),        // 70 bars from 120: 120 dropped, 170 kept (180 from 10)
            (230, 102, 240),                  // the double
            (260, 99.5, 270), (370, 104, 380)); // a candidate, kept 180 or 200 bars on
        Assert.Equal(103, w.Cancelled[200]);
        Assert.Equal(120, w.CancelledLow[200]);
        Assert.Equal(new[] { 200 }, Marked(w.Cancelled));
        Assert.DoesNotContain(120, w.Lows);
        Assert.Equal(10, w.LastLowAt[200]);   // 10 is still the kept low before the newest (170)
        if (drawn)
        {
            Assert.Equal(new[] { 10, 170, 190, 260 }, w.Lows);
            Assert.Equal(new[] { 60, 200, 240, 380 }, w.ConfirmedAt);
            // 190 kept on 240 moves the count onto 170, and that confirmation replaces 120.
            Assert.Equal(170, w.LastLowAt[240]);
            Assert.Equal(95, w.Confirmed[240]);
            Assert.Equal(103, w.Updated[240]);
            Assert.Equal(190, w.LastLowAt[380]);
            Assert.True(double.IsNaN(w.Updated[380]));
        }
        else
        {
            Assert.Equal(new[] { 10, 170, 260 }, w.Lows);
            Assert.Equal(new[] { 60, 200, 380 }, w.ConfirmedAt);
            Assert.True(double.IsNaN(w.Confirmed[240]));
            Assert.Equal(170, w.LastLowAt[380]);
            Assert.Equal(103, w.Updated[380]);
        }
    }

    /// <summary>
    /// A daily low under the candidate takes its place: drawn and kept at once if 90 bars from the
    /// kept low, not drawn if nearer (the original draws nothing for it). The daily low after a
    /// replacement does nothing — unless it is lower still and a full daily cycle (22 bars) after
    /// it, when it is judged as the low after a new weekly low is (the double above).
    /// </summary>
    [Theory]
    [InlineData(225, true)]    // 25 bars after the replacement: judged, and kept by the next low
    [InlineData(221, false)]   // 21: nothing; the next low is a new candidate
    public void A_lower_daily_low_replaces_the_candidate_and_a_lower_one_a_daily_cycle_later_is_judged(int laterBar, bool judged)
    {
        var w = Weekly(400,
            (10, 100, 20), (50, 110, 60),     // 10 kept
            (120, 103, 130), (150, 107, 160), // 120 kept
            (170, 101, 180),                  // a candidate
            (200, 98, 210),                   // lower: replaces it; under 120 80 bars on, so 120 is
                                              // dropped, and 200 is 190 from 10, so kept at once
            (laterBar, 96, laterBar + 10),
            (260, 97, 270));
        Assert.Equal(103, w.Cancelled[210]);
        Assert.Equal(210, w.ConfirmedAt[w.Lows.IndexOf(200)]);
        Assert.DoesNotContain(170, w.Lows);
        if (judged)
        {
            Assert.Equal(new[] { 10, 200, laterBar }, w.Lows);
            Assert.Equal(270, w.ConfirmedAt[^1]);
            Assert.Equal(200, w.LastLowAt[270]);
            Assert.Equal(103, w.Updated[270]);
        }
        else
        {
            Assert.Equal(new[] { 10, 200, 260 }, w.Lows);
            Assert.Equal(-1, w.ConfirmedAt[^1]);   // the live candidate, drawn on 270 and not kept yet
            Assert.Equal(270, w.FoundAt[^1]);
            Assert.Equal(10, w.LastLowAt[^1]);
        }
    }

    /// <summary>
    /// Ether's weekly lows have no "double" (<see cref="CyclePreset.WeeklyDoubles"/>): after a
    /// lower weekly low the next daily low is a new candidate, not the judge of a second weekly
    /// low, and a lower low a daily cycle after a replacement is not judged either. The two
    /// scenarios above, with doubles off.
    /// </summary>
    [Fact]
    public void Without_doubles_the_low_after_a_lower_weekly_low_is_a_new_candidate()
    {
        var (d1, data1) = Daily(400,
            (10, 100, 20), (50, 110, 60), (120, 103, 130), (150, 107, 160),
            (170, 95, 180), (190, 99, 200), (230, 102, 240), (260, 99.5, 270), (370, 104, 380));
        var w1 = CipherCEngine.WeeklyFromDaily(d1, data1, Keep, DailyMin, doubles: false);
        Assert.Equal(new[] { 10, 170, 260 }, w1.Lows);          // with doubles 10, 170, 190, 260
        Assert.Equal(new[] { 60, 200, 270 }, w1.ConfirmedAt);   // 230 is the candidate 260 replaces, kept at once

        var (d2, data2) = Daily(400,
            (10, 100, 20), (50, 110, 60), (120, 103, 130), (150, 107, 160),
            (170, 101, 180), (200, 98, 210), (225, 96, 235), (260, 97, 270));
        var w2 = CipherCEngine.WeeklyFromDaily(d2, data2, Keep, DailyMin, doubles: false);
        Assert.Equal(new[] { 10, 200, 260 }, w2.Lows);          // with doubles 10, 200, 225
        Assert.Equal(-1, w2.ConfirmedAt[^1]);
    }

    /// <summary>
    /// A replacement nearer than 90 bars to the kept low is not drawn, and the candidate it
    /// replaced is gone too: the next daily low after it starts afresh.
    /// </summary>
    [Theory]
    [InlineData(90, false)]    // 80 bars from the kept low at 10
    [InlineData(100, true)]    // 90
    public void A_replacement_too_near_the_kept_low_is_not_drawn(int replacement, bool kept)
    {
        var w = Weekly(300,
            (10, 100, 20), (50, 110, 60),     // 10 kept
            (70, 105, 80),                    // a candidate
            (replacement, 102, replacement + 10),
            (150, 104, 160),                  // after a replacement: nothing
            (200, 106, 210));                 // the next candidate
        Assert.Equal(kept ? new[] { 10, 100, 200 } : new[] { 10, 200 }, w.Lows);
        Assert.Equal(kept ? new[] { 60, 110, -1 } : new[] { 60, -1 }, w.ConfirmedAt);
    }

    /// <summary>
    /// Nothing the weekly tier says on a bar changes when later bars arrive: it acts only on the
    /// daily confirmations up to that bar. Checked on every cut of the case that drops a kept low.
    /// </summary>
    [Fact]
    public void The_weekly_tier_says_on_each_bar_only_what_it_knew_then()
    {
        var lows = new (int Bar, double Low, int At)[]
        {
            (10, 100, 20), (50, 110, 60), (120, 103, 130), (150, 107, 160), (170, 95, 180),
            (190, 99, 200), (230, 102, 240), (260, 99.5, 270), (370, 104, 380),
        };
        var whole = Weekly(400, lows);
        Assert.NotEmpty(Marked(whole.Updated));
        for (int cut = 1; cut <= 400; cut++)
        {
            var part = Weekly(cut, lows.Where(l => l.At < cut).ToArray());
            for (int t = 0; t < cut; t++)
            {
                Assert.Equal(whole.LastLowAt[t], part.LastLowAt[t]);
                Assert.Equal(whole.CancelledLow[t], part.CancelledLow[t]);
                foreach (var (a, b) in new[] { (whole.Confirmed, part.Confirmed), (whole.Cancelled, part.Cancelled), (whole.Updated, part.Updated) })
                    Assert.Equal(a[t], b[t]);
            }
        }
    }

    // The weekly tier by counting daily lows (CipherCEngine.WeeklyFromTwoDaily): the dollar, the
    // Dow, the S&P and the Nasdaq. Gap 40 = two daily cycles of 22 less two bars each.
    private const double Gap = 2 * (DailyMin - 2);

    private static CipherCEngine.TierRun ByTwo(int n, params (int Bar, double Low, int At)[] lows)
    {
        var (daily, data) = Daily(n, lows);
        return CipherCEngine.WeeklyFromTwoDaily(daily, data, Gap);
    }

    /// <summary>
    /// The first daily low after a new weekly low takes its place when it is lower, however near;
    /// otherwise the weekly low stays. A low that took the place is never moved itself.
    /// </summary>
    [Theory]
    [InlineData(95, new[] { 30 })]    // lower: takes the place
    [InlineData(105, new[] { 10 })]   // higher: the weekly low stays
    public void The_first_daily_low_after_a_new_weekly_low_takes_its_place_only_when_lower(double low, int[] expected)
    {
        var run = ByTwo(120, (10, 100, 20), (30, low, 40));
        Assert.Equal(expected, run.Lows);
        Assert.Equal(40, run.ConfirmedAt[0]);
    }

    [Fact]
    public void A_daily_low_that_took_a_weekly_lows_place_is_not_moved_by_the_next_lower_one()
    {
        var run = ByTwo(120, (10, 100, 20), (30, 95, 40), (45, 90, 55));
        Assert.Equal(new[] { 30 }, run.Lows);
    }

    /// <summary>
    /// From the second daily low after the newest weekly low, a new one goes on the lowest of
    /// them that is at least the gap after it; until one is, it waits for the next.
    /// </summary>
    [Theory]
    [InlineData(50, new[] { 10, 50 })]       // 40 bars after: a new weekly low
    [InlineData(49, new[] { 10, 80 })]       // 39: waits, and the next one is it
    public void A_new_weekly_low_needs_the_gap_from_the_newest(int second, int[] expected)
    {
        var run = ByTwo(160, (10, 100, 20), (30, 110, 40), (second, 104, second + 10), (80, 108, 90));
        Assert.Equal(expected, run.Lows);
    }

    /// <summary>
    /// The new weekly low can be an earlier daily low than the one just confirmed: the lowest of
    /// those far enough, drawn on the bar the later one is confirmed and fixed at once.
    /// </summary>
    [Fact]
    public void A_new_weekly_low_goes_on_the_lowest_far_enough_daily_low_even_an_earlier_one()
    {
        var run = ByTwo(160, (10, 100, 20), (52, 104, 62), (70, 108, 80));
        Assert.Equal(new[] { 10, 52 }, run.Lows);
        Assert.Equal(80, run.FoundAt[1]);
        Assert.Equal(80, run.ConfirmedAt[1]);
    }

    /// <summary>
    /// The confirmed weekly low (the one the week count runs from) is the one before the newest. It
    /// moves at a daily confirmation, but not on the bar a new weekly low is drawn on its own daily
    /// low: then it waits for the next daily confirmation. That new weekly low can still move until
    /// then (-1).
    /// </summary>
    [Fact]
    public void The_confirmed_weekly_low_moves_at_the_daily_confirmation_after_a_new_one_is_drawn()
    {
        var lows = new (int Bar, double Low, int At)[] { (10, 100, 20), (30, 110, 40), (60, 108, 70), (90, 112, 100) };
        var before = ByTwo(99, lows.Where(l => l.At < 99).ToArray());
        Assert.Equal(new[] { 10, 60 }, before.Lows);
        Assert.Equal(-1, before.ConfirmedAt[1]);
        var run = ByTwo(120, lows);
        Assert.Equal(-1, run.LastLowAt[99]);
        Assert.Equal(10, run.LastLowAt[100]);
        Assert.Equal(new[] { 100 }, Marked(run.Confirmed));
        Assert.Equal(100, run.ConfirmedAt[1]);
    }

    [Fact]
    public void The_weekly_tier_by_daily_lows_says_on_each_bar_only_what_it_knew_then()
    {
        var lows = new (int Bar, double Low, int At)[]
        {
            (10, 100, 20), (30, 95, 40), (50, 110, 60), (75, 104, 85), (100, 108, 110), (130, 90, 140),
            (150, 99, 160), (170, 96, 180), (215, 102, 225), (240, 101, 250), (280, 94, 290),
        };
        var whole = ByTwo(320, lows);
        Assert.True(whole.Lows.Count >= 4);
        for (int cut = 1; cut <= 320; cut++)
        {
            var part = ByTwo(cut, lows.Where(l => l.At < cut).ToArray());
            for (int t = 0; t < cut; t++)
            {
                Assert.Equal(whole.LastLowAt[t], part.LastLowAt[t]);
                Assert.Equal(whole.Confirmed[t], part.Confirmed[t]);
            }
        }
    }

    // The weekly tier on Gold, Silver and the miners (CipherCEngine.WeeklyFromNextDaily): a candidate
    // judged at the next daily low. Gaps for a table shortest daily cycle of 22: 4.5, 4 and 6 of it.
    private const double NextGap = 4.5 * DailyMin, NextLowerGap = 4 * DailyMin, NextFar = 6 * DailyMin;

    // Daily lows confirmed after the last bar are left out: the series ends before them.
    private static CipherCEngine.TierRun ByNext(int n, params (int Bar, double Low, int At)[] lows)
    {
        var (daily, data) = Daily(n, lows.Where(l => l.At < n).ToArray());
        return CipherCEngine.WeeklyFromNextDaily(daily, data, NextGap, NextLowerGap, NextFar);
    }

    /// <summary>
    /// After a weekly low the next daily low draws nothing; the one after is drawn as the
    /// candidate, and the next daily low judges it: not lower, it is a weekly low when 99 bars or
    /// more from the newest (bar 109 from bar 10), and gone when not (108). Kept, it is fixed on the
    /// judging bar and the weekly low before it becomes the confirmed one there.
    /// </summary>
    [Theory]
    [InlineData(109, new[] { 10, 109 })]
    [InlineData(108, new[] { 10 })]
    public void A_candidate_is_a_weekly_low_when_far_enough_and_the_next_daily_low_is_not_lower(int bar, int[] expected)
    {
        var lows = new (int Bar, double Low, int At)[] { (10, 100, 20), (40, 110, 50), (bar, 104, 125), (140, 108, 150) };
        var drawn = ByNext(149, lows.Where(l => l.At < 149).ToArray());
        Assert.Equal(new[] { 10, bar }, drawn.Lows);
        Assert.Equal(-1, drawn.ConfirmedAt[1]);
        var run = ByNext(170, lows);
        Assert.Equal(expected, run.Lows);
        Assert.Equal(expected.Length == 2 ? new[] { 150 } : Array.Empty<int>(), Marked(run.Confirmed));
        if (expected.Length == 2) { Assert.Equal(125, run.FoundAt[1]); Assert.Equal(150, run.ConfirmedAt[1]); Assert.Equal(10, run.LastLowAt[150]); }
    }

    /// <summary>A candidate too near but lower than the newest weekly low takes its place.</summary>
    [Fact]
    public void A_near_candidate_lower_than_the_weekly_low_takes_its_place()
    {
        var run = ByNext(130, (10, 100, 20), (40, 110, 50), (70, 96, 80), (100, 104, 110));
        Assert.Equal(new[] { 70 }, run.Lows);
        Assert.Equal(100, run.Cancelled[110]);
        Assert.Equal(10, run.CancelledLow[110]);
    }

    /// <summary>
    /// A next daily low lower than the candidate replaces it: a weekly low itself when 88 bars or
    /// more from the newest (98 from 10), nothing when not (97), and in the newest one's place when
    /// lower than it.
    /// </summary>
    [Theory]
    [InlineData(98, 102, new[] { 10, 98 })]
    [InlineData(97, 102, new[] { 10 })]
    [InlineData(97, 95, new[] { 97 })]
    public void A_lower_next_daily_low_is_judged_on_its_own_gap(int bar, double low, int[] expected)
    {
        var run = ByNext(130, (10, 100, 20), (40, 110, 50), (70, 108, 80), (bar, low, 107));
        Assert.Equal(expected, run.Lows);
    }

    /// <summary>
    /// After a judgement the next daily low draws no candidate when the judging low undercut the
    /// candidate, or the candidate was more than 132 bars (6 daily cycles) from the weekly low;
    /// otherwise it does.
    /// </summary>
    [Theory]
    [InlineData(102, 175, 160)]   // undercut: 130 is passed over, 160 drawn
    [InlineData(109, 165, 130)]   // not: 130 drawn
    public void A_judging_low_that_undercut_the_candidate_passes_over_the_next(double judge, int n, int drawn)
    {
        var run = ByNext(n, (10, 100, 20), (40, 110, 50), (70, 108, 80), (97, judge, 107), (130, 112, 140), (160, 114, 170));
        Assert.Contains(drawn, run.Lows);
        Assert.Equal(-1, run.ConfirmedAt[run.Lows.IndexOf(drawn)]);
        Assert.DoesNotContain(drawn == 160 ? 130 : 160, run.Lows);
    }

    [Theory]
    [InlineData(143, new[] { 10, 143 })]        // 133 bars from the weekly low: 200 is passed over
    [InlineData(142, new[] { 10, 142, 200 })]   // 132: 200 drawn
    public void A_far_candidate_passes_over_the_next(int cand, int[] expected)
    {
        var run = ByNext(220, (10, 100, 20), (40, 110, 50), (cand, 108, 160), (170, 109, 180), (200, 112, 210), (230, 114, 240));
        Assert.Equal(expected, run.Lows);
    }

    [Fact]
    public void The_weekly_tier_by_the_next_daily_low_says_on_each_bar_only_what_it_knew_then()
    {
        var lows = new (int Bar, double Low, int At)[]
        {
            (10, 100, 20), (30, 95, 40), (50, 110, 60), (75, 104, 85), (100, 108, 110), (130, 90, 140),
            (150, 99, 160), (170, 96, 180), (215, 102, 225), (240, 101, 250), (280, 94, 290), (330, 97, 340),
        };
        var whole = ByNext(360, lows);
        Assert.True(whole.Lows.Count >= 3);
        for (int cut = 1; cut <= 360; cut++)
        {
            var part = ByNext(cut, lows.Where(l => l.At < cut).ToArray());
            for (int t = 0; t < cut; t++)
            {
                Assert.Equal(whole.LastLowAt[t], part.LastLowAt[t]);
                Assert.Equal(whole.CancelledLow[t], part.CancelledLow[t]);
                foreach (var (a, b) in new[] { (whole.Confirmed, part.Confirmed), (whole.Cancelled, part.Cancelled), (whole.Updated, part.Updated) })
                    Assert.Equal(a[t], b[t]);
            }
        }
    }
}
