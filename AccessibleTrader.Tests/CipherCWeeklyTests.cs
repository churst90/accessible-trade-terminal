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
        Assert.Equal(120, w.ClosedUnder[200]);
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
                Assert.Equal(whole.ClosedUnder[t], part.ClosedUnder[t]);
                foreach (var (a, b) in new[] { (whole.Confirmed, part.Confirmed), (whole.Cancelled, part.Cancelled), (whole.Updated, part.Updated) })
                    Assert.Equal(a[t], b[t]);
            }
        }
    }
}
