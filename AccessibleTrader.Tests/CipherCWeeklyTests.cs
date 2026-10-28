using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// Cipher C's weekly tier on a chart with a daily one picks its weekly lows from the daily lows as
/// the original does: <see cref="CipherCEngine.WeeklyFromDaily"/> everywhere but the metals,
/// <see cref="CipherCEngine.WeeklyByWeeks"/> on Gold, Silver and the miners. The rules were read
/// from the original's own labels in TradingView (trial, 2026-10-23 to 10-27): its candle replays
/// and whole-history drawings on nineteen assets. That data cannot be committed, so these tests
/// drive the engine with hand-built daily lows, one branch of a rule each, and the expected lows
/// are worked out here by hand from the rule, not read from the engine.
///
/// <para>
/// The shortest daily cycle is 22 bars, so a far "double" needs 1.25 × 22 = 27.5 bars. The metal
/// cases use the gaps of a table whose weekly cycle ends at 28 weeks of 5 bars: a new weekly low
/// 20 weeks (100 bars) from the newest, a double 14 weeks (70 bars).
/// </para>
/// </summary>
public class CipherCWeeklyTests
{
    private const double DailyMin = 22, NewBars = 100, DoubleBars = 70;

    [Fact]
    public void The_rules_constants_are_the_ones_read_from_the_original()
    {
        Assert.Equal(1.25, CipherCEngine.FarDoubleCycles);
        Assert.Equal(16, CipherCEngine.DoubleMinBars);
        Assert.Equal(8, CipherCEngine.WeeksShortOfMax);
        Assert.Equal(14, CipherCEngine.DoubleWeeksShortOfMax);
    }

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

    private static CipherCEngine.TierRun Weekly(int n, params (int Bar, double Low, int At)[] lows) => Weekly(n, true, lows);

    private static CipherCEngine.TierRun Weekly(int n, bool farDoubles, params (int Bar, double Low, int At)[] lows)
    {
        var (daily, data) = Daily(n, lows);
        return CipherCEngine.WeeklyFromDaily(daily, data, DailyMin, farDoubles);
    }

    private static CipherCEngine.TierRun Metal(int n, params (int Bar, double Low, int At)[] lows) => Metal(n, NewBars, DoubleBars, lows);

    private static CipherCEngine.TierRun Metal(int n, double newBars, double doubleBars, params (int Bar, double Low, int At)[] lows)
        => Metal(n, newBars, doubleBars, false, lows);

    private static CipherCEngine.TierRun Metal(int n, double newBars, double doubleBars, bool underNewest, params (int Bar, double Low, int At)[] lows)
    {
        var (daily, data) = Daily(n, lows);
        return CipherCEngine.WeeklyByWeeks(daily, data, newBars, doubleBars, underNewest);
    }

    /// <summary>The weekly tier of a daily run that starts on <paramref name="seed"/> (the original's
    /// Start bar), whose low there is <paramref name="seedLow"/>.</summary>
    private static CipherCEngine.TierRun Seeded(bool metal, int seed, double seedLow, params (int Bar, double Low, int At)[] lows)
    {
        var (daily, data) = Daily(300, lows);
        data[seed] = data[seed] with { Low = seedLow };
        daily.Start = seed;
        return metal ? CipherCEngine.WeeklyByWeeks(daily, data, NewBars, DoubleBars, seed: seed)
                     : CipherCEngine.WeeklyFromDaily(daily, data, DailyMin, seed: seed);
    }

    /// <summary>
    /// <b>The weekly tier starts on the original's Start bar (2026-10-28).</b> The Start is the
    /// weekly low the first weekly cycle runs from, drawn nowhere: the first daily low after it is
    /// not a weekly low by itself, the second is the candidate, as after any weekly low. Read from
    /// the original's first weekly low after its Start label on the eleven tuned daily charts that
    /// have one: ours now matches on all eleven (before, on two). It is the candidate on nine; on
    /// Gold a double after the candidate was undercut, on Coinbase a far double from its Start.
    /// The week count runs from the Start once the first weekly low is made, and the Start is no
    /// confirmation of its own. Weekly lows end to end on daily charts .907 → .935 (.918 before the
    /// dated Start), on weekly charts .776 → .835, on 4-hour charts .795 → .799.
    /// </summary>
    [Fact]
    public void The_weekly_tier_runs_from_the_start_bar_drawn_nowhere()
    {
        (int, double, int)[] lows = { (10, 100, 20), (50, 110, 60), (80, 120, 90) };
        var w = Seeded(false, 5, 200, lows);
        Assert.Equal(new[] { 50 }, w.Lows);
        Assert.Equal(new[] { 60 }, w.FoundAt);
        Assert.Equal(new[] { 90 }, w.ConfirmedAt);
        Assert.Empty(Marked(w.Confirmed));
        Assert.Equal(-1, w.LastLowAt[89]);
        Assert.Equal(5, w.LastLowAt[90]);
        Assert.Equal(5, w.NewestAt[5]);

        // Without a Start the first daily low is the first weekly low.
        Assert.Equal(new[] { 10, 80 }, Weekly(300, lows).Lows);
    }

    /// <summary>
    /// On the metals a candidate judged not lower is a weekly low however near the Start it is (the
    /// Start is as far as it needs to be): GDX, GDXJ, Silver and the junior silver miners each draw
    /// one more of the original's weekly lows. A daily low that undercuts the candidate is not: near
    /// the Start and not under it, it is only the reference low, as near any weekly low (Gold's
    /// first after its Start is such a case, 2 of its weekly lows lost when it is).
    /// </summary>
    [Theory]
    [InlineData(120, true)]
    [InlineData(105, false)]
    public void On_the_metals_the_start_is_far_enough_for_a_candidate_made_a_weekly_low(double judge, bool made)
    {
        var w = Seeded(true, 5, 50, (10, 100, 20), (30, 110, 40), (45, judge, 55));
        Assert.Equal(made ? new[] { 30 } : Array.Empty<int>(), w.Lows);
        Assert.Equal(made ? 5 : -1, w.LastLowAt[299]);
        Assert.Equal(made ? 30 : 5, w.NewestAt[299]);
    }

    private static int[] Marked(double[] a) => Enumerable.Range(0, a.Length).Where(i => !double.IsNaN(a[i])).ToArray();

    // ── Everywhere but the metals ──

    /// <summary>
    /// The first daily low is a weekly low. The second daily low after the newest weekly low is
    /// drawn at once as the candidate; the next daily low judges it. One under it cancels it and is
    /// itself a weekly low, here two bars on; one not under it (equal included) makes the candidate
    /// a weekly low. The week count runs from the weekly low before the newest.
    /// </summary>
    [Theory]
    [InlineData(104, true)]
    [InlineData(105, false)]
    public void The_next_daily_low_cancels_the_candidate_when_under_it_and_makes_it_a_weekly_low_when_not(double judge, bool undercut)
    {
        var w = Weekly(300,
            (10, 100, 20),       // the first: a weekly low
            (50, 110, 60),       // the first after it
            (80, 105, 90),       // the second: the candidate, drawn on 90
            (82, judge, 120));   // judges it
        Assert.Equal(100, w.Confirmed[120]);   // the count starts from 10 once a second is made
        Assert.Equal(new[] { 120 }, Marked(w.Confirmed));
        Assert.Equal(-1, w.LastLowAt[119]);
        Assert.Equal(10, w.LastLowAt[120]);
        if (undercut)
        {
            Assert.Equal(new[] { 10, 82 }, w.Lows);
            Assert.Equal(new[] { 20, 120 }, w.ConfirmedAt);
            Assert.Equal(new[] { 20, 120 }, w.FoundAt);
            Assert.Equal(new[] { 120 }, Marked(w.Cancelled));
            Assert.Equal(105, w.Cancelled[120]);
            Assert.Equal(80, w.CancelledLow[120]);
            Assert.Equal(105, w.Updated[120]);
        }
        else
        {
            Assert.Equal(new[] { 10, 80 }, w.Lows);
            Assert.Equal(new[] { 20, 120 }, w.ConfirmedAt);
            Assert.Equal(new[] { 20, 90 }, w.FoundAt);
            Assert.Empty(Marked(w.Cancelled));
            Assert.True(double.IsNaN(w.Updated[120]));
        }
    }

    /// <summary>
    /// The live candidate is among the lows, drawn and not yet a weekly low (-1).
    /// </summary>
    [Fact]
    public void The_live_candidate_is_drawn_and_not_yet_a_weekly_low()
    {
        var w = Weekly(200, (10, 100, 20), (50, 110, 60), (80, 105, 90));
        Assert.Equal(new[] { 10, 80 }, w.Lows);
        Assert.Equal(new[] { 20, 90 }, w.FoundAt);
        Assert.Equal(new[] { 20, -1 }, w.ConfirmedAt);
        Assert.Empty(Marked(w.Confirmed));
    }

    /// <summary>
    /// The unconfirmed weekly low (HeldAt, which the table's unconfirmed week count runs from) is the
    /// newest low drawn: the newest weekly low until a candidate is drawn, then the candidate, and
    /// after it is judged whichever low the judging made a weekly low. Read from the original's table
    /// on the sixteen tuned daily replays.
    /// </summary>
    [Theory]
    [InlineData(104, 82)]
    [InlineData(105, 80)]
    public void The_unconfirmed_weekly_low_is_the_newest_drawn_the_candidate_included(double judge, int after)
    {
        var w = Weekly(300, (10, 100, 20), (50, 110, 60), (80, 105, 90), (82, judge, 120));
        Assert.Equal(-1, w.HeldAt[19]);
        Assert.Equal(10, w.HeldAt[20]);
        Assert.Equal(10, w.HeldAt[89]);
        Assert.Equal(80, w.HeldAt[90]);
        Assert.Equal(80, w.HeldAt[119]);
        Assert.Equal(after, w.HeldAt[120]);
        Assert.Equal(after, w.HeldAt[299]);
    }

    /// <summary>
    /// A far "double": the first daily low after a weekly low is a weekly low of its own when it is
    /// under that weekly low at least 1.25 daily cycles (27.5 bars) after it and the second daily
    /// low is not under it. The second then counts as the first after it, so the third is the
    /// candidate. Otherwise the second is the candidate, and the third makes it a weekly low.
    /// </summary>
    [Theory]
    [InlineData(38, 97, true)]    // 28 bars on, the second not under it: a double
    [InlineData(37, 97, false)]   // 27 bars on
    [InlineData(38, 94, false)]   // the second under it
    public void A_first_daily_low_well_under_the_weekly_low_and_far_from_it_is_a_double(int first, double second, bool doubled)
    {
        var w = Weekly(300, (10, 100, 20), (first, 95, first + 10), (60, second, 70), (90, 99, 100));
        if (doubled)
        {
            Assert.Equal(new[] { 10, first, 90 }, w.Lows);
            Assert.Equal(new[] { 20, 70, -1 }, w.ConfirmedAt);
            Assert.Equal(100, w.Confirmed[70]);
        }
        else
        {
            Assert.Equal(new[] { 10, 60 }, w.Lows);
            Assert.Equal(new[] { 20, 100 }, w.ConfirmedAt);
            Assert.Equal(new[] { 100 }, Marked(w.Confirmed));
        }
    }

    /// <summary>Ether has no far double (<see cref="CyclePreset.WeeklyDoubles"/> false): the case
    /// above that doubles draws the second daily low as the candidate instead.</summary>
    [Fact]
    public void Ether_has_no_far_double()
    {
        var w = Weekly(300, false, (10, 100, 20), (38, 95, 48), (60, 97, 70), (90, 99, 100));
        Assert.Equal(new[] { 10, 60 }, w.Lows);
        Assert.Equal(new[] { 20, 100 }, w.ConfirmedAt);
    }

    /// <summary>
    /// A deep double: after a candidate is made a weekly low, the first daily low after it (which
    /// judged it, so is not under it) is a weekly low too when it is under the daily low before the
    /// candidate, at least 16 bars after it, and the second is not under the first.
    /// </summary>
    [Theory]
    [InlineData(70, 105, true)]    // 20 bars on, under 110
    [InlineData(70, 110, false)]   // level with the daily low before: not under it
    [InlineData(66, 105, true)]    // 16 bars on
    [InlineData(65, 105, false)]   // 15 bars on
    public void A_first_daily_low_after_a_candidate_made_a_weekly_low_and_under_the_daily_low_before_it_is_a_double(int first, double firstLow, bool doubled)
    {
        var w = Weekly(300,
            (10, 100, 20),
            (30, 110, 40),                    // the daily low before the candidate
            (50, 100, 60),                    // the candidate
            (first, firstLow, first + 10),    // not under it: 50 is a weekly low, this the first after it
            (90, 112, 100));                  // the second, not under the first
        Assert.Equal(100, w.Confirmed[first + 10]);
        if (doubled)
        {
            Assert.Equal(new[] { 10, 50, first }, w.Lows);
            Assert.Equal(new[] { 20, first + 10, 100 }, w.ConfirmedAt);
            Assert.Equal(50, w.LastLowAt[100]);
            Assert.Equal(100, w.Confirmed[100]);
        }
        else
        {
            Assert.Equal(new[] { 10, 50, 90 }, w.Lows);
            Assert.Equal(new[] { 20, first + 10, -1 }, w.ConfirmedAt);
            Assert.True(double.IsNaN(w.Confirmed[100]));
        }
    }

    /// <summary>A weekly low made by undercutting a candidate is not one made from a candidate: the
    /// deep double does not follow it, though 70 is under the daily low before 52 (and the one before
    /// the last candidate made a weekly low, 20), 18 bars on.</summary>
    [Fact]
    public void No_deep_double_follows_a_weekly_low_made_by_an_undercut()
    {
        var w = Weekly(300,
            (10, 100, 20), (20, 120, 30),
            (30, 115, 40),    // the candidate
            (40, 116, 50),    // not under it: 30 is a weekly low; 10 bars on, too near for a double
            (50, 118, 60),    // the next candidate
            (52, 97, 70),     // under it: cancelled, 52 is a weekly low
            (70, 98, 80), (90, 99, 100));
        Assert.Equal(new[] { 10, 30, 52, 90 }, w.Lows);
        Assert.Equal(new[] { 20, 50, 70, -1 }, w.ConfirmedAt);
    }

    // ── Gold, Silver and the miners ──

    /// <summary>
    /// The metals judge the same candidate by distance. Not undercut by the next daily low, it is a
    /// new weekly low 100 bars or more from the newest; nearer, it takes the newest's place when
    /// under it (cancelling it); else it is gone, not cancelled.
    /// </summary>
    [Theory]
    [InlineData(110, 105, new[] { 10, 110 }, new[] { 20, 140 })]   // 100 bars on: a new weekly low
    [InlineData(100, 95, new[] { 100 }, new[] { 130 })]           // 90, under 100: in its place
    [InlineData(100, 105, new[] { 10 }, new[] { 20 })]            // 90, not under: gone
    public void A_metal_candidate_is_a_new_weekly_low_far_enough_and_otherwise_replaces_only_a_higher_one(int bar, double low, int[] lows, int[] at)
    {
        var w = Metal(300, (10, 100, 20), (30, 110, 40), (bar, low, bar + 10), (bar + 20, 108, bar + 30));
        Assert.Equal(lows, w.Lows);
        Assert.Equal(at, w.ConfirmedAt);
        if (lows.Length == 1 && lows[0] != 10)
        {
            Assert.Equal(100, w.Cancelled[130]);
            Assert.Equal(10, w.CancelledLow[130]);
        }
        else Assert.Empty(Marked(w.Cancelled));
        if (lows.Length == 2) Assert.Equal(100, w.Confirmed[140]);
    }

    /// <summary>
    /// A metal candidate undercut by the next daily low is cancelled, and the lower low is judged
    /// in its place: a new weekly low 100 bars on, in the newest's place when nearer and under it
    /// (the cancellation then carries the newest), else only the new reference, drawn nowhere.
    /// </summary>
    [Theory]
    [InlineData(110, 104, new[] { 10, 110 }, 106, 60)]
    [InlineData(90, 99, new[] { 90 }, 100, 10)]
    [InlineData(90, 104, new[] { 10 }, 106, 60)]
    public void A_metal_candidate_undercut_is_cancelled_and_the_lower_low_judged_by_distance(int bar, double low, int[] lows, double cancelled, int cancelledLow)
    {
        var w = Metal(300, (10, 100, 20), (30, 110, 40), (60, 106, 70), (bar, low, bar + 10));
        Assert.Equal(lows, w.Lows);
        Assert.Equal(cancelled, w.Cancelled[bar + 10]);
        Assert.Equal(cancelledLow, w.CancelledLow[bar + 10]);
    }

    /// <summary>
    /// On the metals the unconfirmed weekly low can be one drawn nowhere: a candidate that went (too
    /// near the newest weekly low and not under it), or an undercut that became only the reference.
    /// The original's table counts its unconfirmed weeks from those, as from a drawn one.
    /// </summary>
    [Fact]
    public void A_metal_low_drawn_nowhere_is_the_unconfirmed_weekly_low()
    {
        var gone = Metal(300, (10, 100, 20), (30, 110, 40), (100, 105, 110), (120, 108, 130));
        Assert.Equal(new[] { 10 }, gone.Lows);
        Assert.Equal(10, gone.HeldAt[109]);
        Assert.Equal(100, gone.HeldAt[110]);   // the candidate, drawn
        Assert.Equal(100, gone.HeldAt[130]);   // gone, still counted from
        Assert.Equal(100, gone.HeldAt[299]);

        var reference = Metal(300, (10, 100, 20), (30, 110, 40), (60, 106, 70), (90, 104, 100));
        Assert.Equal(new[] { 10 }, reference.Lows);
        Assert.Equal(60, reference.HeldAt[99]);
        Assert.Equal(90, reference.HeldAt[100]);   // the undercut, only the reference
    }

    /// <summary>
    /// A metal double: the first daily low after the reference, under it with the second not under
    /// the first, is a weekly low 70 bars or more from the newest; the reference need not be a
    /// weekly low (here an undercut that was too near to be one). Nearer, the second is the candidate.
    /// </summary>
    [Theory]
    [InlineData(80, true)]
    [InlineData(79, false)]
    public void A_metal_double_needs_seventy_bars(int first, bool doubled)
    {
        var w = Metal(300, (10, 100, 20), (first, 95, first + 10), (first + 20, 97, first + 30));
        Assert.Equal(doubled ? new[] { 10, first } : new[] { 10, first + 20 }, w.Lows);
        Assert.Equal(doubled ? new[] { 20, first + 30 } : new[] { 20, -1 }, w.ConfirmedAt);
    }

    [Fact]
    public void A_metal_double_can_follow_a_reference_that_is_no_weekly_low()
    {
        var w = Metal(300, (10, 100, 20), (30, 110, 40), (60, 106, 70), (90, 104, 100), (120, 103, 130), (140, 105, 150));
        Assert.Equal(new[] { 10, 120 }, w.Lows);   // 90 was the reference, 120 under it and 110 from 10
        Assert.Equal(new[] { 20, 150 }, w.ConfirmedAt);
    }

    /// <summary>
    /// On the miners a metal double also needs the first daily low under the newest weekly low.
    /// The case above with 120 at 103 (over the weekly low at 10, 100) is no double there: 140 is
    /// drawn as the candidate; at 100, level with it, neither. At 99 it is one. Read from the
    /// original's drawings on the three miners: four of their five doubles fit (one, junior gold
    /// miners, was drawn over it); on Gold the condition scores lower, so Gold and Silver keep the
    /// plain double.
    /// </summary>
    [Theory]
    [InlineData(103, false, true)]
    [InlineData(103, true, false)]
    [InlineData(100, true, false)]
    [InlineData(99, true, true)]
    public void A_miner_double_must_be_under_the_newest_weekly_low(double firstLow, bool underNewest, bool doubled)
    {
        var w = Metal(300, NewBars, DoubleBars, underNewest,
            (10, 100, 20), (30, 110, 40), (60, 106, 70), (90, 104, 100), (120, firstLow, 130), (140, 105, 150));
        Assert.Equal(doubled ? new[] { 10, 120 } : new[] { 10, 140 }, w.Lows);
        Assert.Equal(doubled ? new[] { 20, 150 } : new[] { 20, -1 }, w.ConfirmedAt);
    }

    // ── Both ──

    /// <summary>
    /// Nothing the weekly tier says on a bar changes when later bars arrive: it acts only on the
    /// daily confirmations up to that bar. Checked on every cut of a case that cancels, replaces
    /// and doubles, under both rules (the metals' with half the gaps, so the case makes weekly lows).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_weekly_tier_says_on_each_bar_only_what_it_knew_then(bool metal)
    {
        var lows = new (int Bar, double Low, int At)[]
        {
            (10, 100, 20), (30, 110, 40), (60, 106, 70), (90, 99, 100), (120, 97, 130), (140, 98, 150),
            (170, 96, 180), (200, 101, 210), (230, 95, 240), (250, 99, 260), (290, 102, 300), (330, 94, 340),
            (360, 98, 370), (390, 100, 400),
        };
        CipherCEngine.TierRun Run(int n, (int, double, int)[] l) => metal ? Metal(n, NewBars / 2, DoubleBars / 2, l) : Weekly(n, l);
        var whole = Run(420, lows);
        Assert.NotEmpty(Marked(whole.Cancelled));
        Assert.NotEmpty(Marked(whole.Updated));
        Assert.True(Marked(whole.Confirmed).Length >= 3);
        for (int cut = 1; cut <= 420; cut++)
        {
            var part = Run(cut, lows.Where(l => l.At < cut).ToArray());
            for (int t = 0; t < cut; t++)
            {
                Assert.Equal(whole.LastLowAt[t], part.LastLowAt[t]);
                Assert.Equal(whole.CancelledLow[t], part.CancelledLow[t]);
                Assert.Equal(whole.HeldAt[t], part.HeldAt[t]);
                foreach (var (a, b) in new[] { (whole.Confirmed, part.Confirmed), (whole.Cancelled, part.Cancelled), (whole.Updated, part.Updated) })
                    Assert.Equal(a[t], b[t]);
            }
        }
    }
}
