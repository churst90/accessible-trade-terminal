using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fixtures;
using Xunit.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// Cipher C's daily tier (<see cref="CipherCEngine.DetectDaily"/>) is the original's own rule,
/// read bar by bar from it in TradingView's bar replay on candles (sixteen assets, 2026-10-22 to
/// 27). The replays cannot be committed, so the engine is held to the study's model instead: its
/// lows, naming bars and drops on the Bitcoin fixture under five settings
/// (<see cref="CipherCDailyStudy"/>, generated from the study's script). The property tests below
/// pin each rule on its own, with the numbers written out here rather than read from the engine:
/// Bitcoin's count of 48, so a window from 47 bars, a 24-bar hold from no earlier than the last
/// low plus 46, and drops only within 16 bars of naming; on the Dow's settings, drops at any time
/// before the low is due.
///
/// <para>
/// On the original's final daily lows over the sixteen assets the model draws 1,202 of its 1,244
/// and 35 it does not (F1 0.969). Held out, never fitted: platinum 0.939, Apple 0.985.
/// </para>
/// </summary>
public class CipherCDailyTests
{
    private readonly ITestOutputHelper _out;
    public CipherCDailyTests(ITestOutputHelper output) => _out = output;

    private static readonly Ohlcv[] Bars = CipherSrSpeechTests.BtcDaily().ToArray();
    private static double[] High => Bars.Select(b => b.High).ToArray();
    private static double[] Low => Bars.Select(b => b.Low).ToArray();
    private static double[] Close => Bars.Select(b => b.Close).ToArray();

    public static IEnumerable<object[]> Runs() => new[]
    {
        new object[] { nameof(CipherCDailyStudy.Bitcoin) }, new object[] { nameof(CipherCDailyStudy.Miners) },
        new object[] { nameof(CipherCDailyStudy.Default) }, new object[] { nameof(CipherCDailyStudy.Dollar) },
        new object[] { nameof(CipherCDailyStudy.Dow) },
    };

    private static CipherCDailyStudy.Run Study(string name) => name switch
    {
        nameof(CipherCDailyStudy.Bitcoin) => CipherCDailyStudy.Bitcoin,
        nameof(CipherCDailyStudy.Miners) => CipherCDailyStudy.Miners,
        nameof(CipherCDailyStudy.Default) => CipherCDailyStudy.Default,
        nameof(CipherCDailyStudy.Dow) => CipherCDailyStudy.Dow,
        _ => CipherCDailyStudy.Dollar,
    };

    private static CipherCEngine.TierRun Run(CipherCDailyStudy.Run s, int seed = -1) =>
        CipherCEngine.DetectDaily(Bars, new CipherCEngine.TierBars(s.Min, s.Min * 2, true), s.Length, s.Smooth, 75, 10, s.PivotBreak, seed,
            s.DropUntilDue);

    private static List<int> Marked(double[] a) => Enumerable.Range(0, a.Length).Where(i => !double.IsNaN(a[i])).ToList();

    /// <summary>The engine draws, names and drops exactly what the study's model does, bar for bar,
    /// starting from its own default: the lowest bar of the first shortest cycle (the first of equal ones).</summary>
    [Theory]
    [MemberData(nameof(Runs))]
    public void The_daily_tier_reproduces_the_study_s_model(string name)
    {
        var s = Study(name);
        int seed = 0, first = (int)Math.Round(s.Min);
        for (int i = 1; i < first; i++) if (Low[i] < Low[seed]) seed = i;
        Assert.Equal(s.Seed, seed);

        var r = Run(s);
        Assert.Equal(s.Lows, r.Lows);
        Assert.Equal(s.Names, Marked(r.Pending).Select(t => (t, r.HeldAt[t])).ToArray());
        Assert.Equal(s.Drops, Marked(r.Cancelled).Select(t => (t, r.CancelledLow[t])).ToArray());
        Assert.Equal(r.Lows, Run(s, s.Seed).Lows);
        _out.WriteLine($"{name}: {s.Lows.Length} lows, {s.Names.Length} named, {s.Drops.Length} dropped");
    }

    // ── Bitcoin, counted from 48 ──

    private static CipherCEngine.TierRun Bitcoin() => Run(CipherCDailyStudy.Bitcoin);

    /// <summary>The DSS, written out here: an EMA of the stochastic of the close, the same of that
    /// line's own stochastic, and a 5-bar EMA of the result as the trigger; 50 where a window is
    /// not full or flat, each EMA starting from its first input.</summary>
    private static (double[] D, double[] Trigger) Dss(int length, int smooth)
    {
        static double[] Ema(double[] x, int len)
        {
            var o = (double[])x.Clone();
            for (int i = 1; i < o.Length; i++) o[i] = o[i - 1] + 2.0 / (len + 1) * (x[i] - o[i - 1]);
            return o;
        }
        static double[] Stoch(double[] x, double[] hi, double[] lo, int len) => Enumerable.Range(0, x.Length).Select(i =>
        {
            if (i + 1 < len) return 50.0;
            double h = hi.Skip(i + 1 - len).Take(len).Max(), l = lo.Skip(i + 1 - len).Take(len).Min();
            return h > l ? (x[i] - l) / (h - l) * 100 : 50.0;
        }).ToArray();
        var s = Ema(Stoch(Close, High, Low, length), smooth);
        var d = Ema(Stoch(s, s, s, length), smooth);
        return (d, Ema(d, 5));
    }

    [Fact]
    public void The_dss_matches_one_written_out_here()
    {
        var (d, tr) = CipherCEngine.DoubleSmoothedStochastic(High, Low, Close, 14, 14);
        var (d2, tr2) = Dss(14, 14);
        for (int i = 0; i < d.Length; i++)
        {
            Assert.Equal(d2[i], d[i], 9);
            Assert.Equal(tr2[i], tr[i], 9);
        }
    }

    /// <summary>A flat window, and one not yet full, reads 50, so flat prices give a DSS of 50 throughout.</summary>
    [Fact]
    public void A_flat_or_short_stochastic_window_reads_50()
    {
        var flat = Enumerable.Repeat(100.0, 40).ToArray();
        var (d, tr) = CipherCEngine.DoubleSmoothedStochastic(flat, flat, flat, 10, 9);
        Assert.All(d, v => Assert.Equal(50, v, 12));
        Assert.All(tr, v => Assert.Equal(50, v, 12));
        var rising = Enumerable.Range(0, 40).Select(i => 100.0 + i).ToArray();
        var (d2, _) = CipherCEngine.DoubleSmoothedStochastic(rising, rising, rising, 10, 9);
        Assert.Equal(50, d2[0], 12);
    }

    /// <summary>The count starts from the FIRST of equal lowest bars in the first shortest cycle.</summary>
    [Fact]
    public void The_count_starts_from_the_first_of_equal_lows()
    {
        // Two equal lows at bars 3 and 6 of the first 10 on a wave: counted from 3 the first low
        // is named on bar 13 (bar 6 itself), counted from 6 on bar 19 (bar 14).
        var bars = Enumerable.Range(0, 80).Select(i =>
        {
            double l = i is 3 or 6 ? 80 : 100 + 8 * Math.Sin(i / 3.0);
            return new Ohlcv(new DateTime(2024, 1, 1).AddDays(i), l + 1, l + 3, l, l + 2, 0);
        }).ToArray();
        var tier = new CipherCEngine.TierBars(10, 20, true);
        var byDefault = CipherCEngine.DetectDaily(bars, tier, 10, 9, 75, 10, false);
        var from3 = CipherCEngine.DetectDaily(bars, tier, 10, 9, 75, 10, false, 3);
        var from6 = CipherCEngine.DetectDaily(bars, tier, 10, 9, 75, 10, false, 6);
        Assert.NotEqual(Marked(from3.Pending), Marked(from6.Pending));
        Assert.Equal(Marked(from3.Pending), Marked(byDefault.Pending));
    }

    /// <summary>Without the pivot break every low is named on a bar whose DSS is above its trigger
    /// and between 10 and 75, at least 47 bars after the last low; and the window does not wait:
    /// on most cycles the bar it opens or soon after names one.</summary>
    [Fact]
    public void Bitcoin_names_a_low_only_on_a_rising_dss_inside_the_window()
    {
        var r = Bitcoin();
        var (d, tr) = Dss(14, 14);
        var names = Marked(r.Pending);
        Assert.True(names.Count > 100, $"only {names.Count} lows named");
        int atWindow = 0;
        foreach (int t in names)
        {
            Assert.True(d[t] > tr[t] && d[t] > 10 && d[t] < 75, $"bar {t} named a low with DSS {d[t]:F2}, trigger {tr[t]:F2}");
            int last = r.LastLowAt[t - 1] >= 0 ? r.LastLowAt[t - 1] : CipherCDailyStudy.Bitcoin.Seed;
            Assert.True(t - last >= 47, $"bar {t} named a low {t - last} bars after the last low (bar {last})");
            if (t - last <= 48) atWindow++;
        }
        _out.WriteLine($"{names.Count} named, {atWindow} within a bar of the window opening");
        Assert.True(atWindow >= 5, $"only {atWindow} named as the window opened; the 47 is untested");
    }

    /// <summary>A low is confirmed exactly 24 bars after its hold begins: at the low when the low is
    /// at least 46 bars after the last, else at the bar before the one that named it.</summary>
    [Fact]
    public void Bitcoin_confirms_exactly_24_bars_after_the_hold_begins()
    {
        var r = Bitcoin();
        int fromNaming = 0;
        for (int k = 0; k < r.Lows.Count; k++)
        {
            int last = k == 0 ? CipherCDailyStudy.Bitcoin.Seed : r.Lows[k - 1];
            int origin = r.Lows[k] >= last + 46 ? r.Lows[k] : r.FoundAt[k] - 1;
            if (origin != r.Lows[k]) fromNaming++;
            Assert.Equal(origin + 24, r.ConfirmedAt[k]);
        }
        _out.WriteLine($"{r.Lows.Count} lows, {fromNaming} held from the bar before naming");
        Assert.InRange(fromNaming, 5, r.Lows.Count - 5);
    }

    /// <summary>A bar under the pending low drops it only within 16 bars of naming it (48 / 3), and
    /// not on the bar it comes due; after that the low stands, and some do.</summary>
    [Fact]
    public void Bitcoin_drops_a_pending_low_only_soon_after_naming_it()
    {
        var r = Bitcoin();
        var low = Low;
        var names = Marked(r.Pending);
        int locked = 0, dueUnder = 0;
        for (int t = 1; t < low.Length; t++)
        {
            int held = r.HeldAt[t - 1];
            if (held < 0) continue;
            int named = names.Last(b => b < t);
            bool under = low[t] < low[held];
            bool due = !double.IsNaN(r.Confirmed[t]);
            bool dropped = !double.IsNaN(r.Cancelled[t]);
            Assert.Equal(under && !due && t - named < 16, dropped);
            if (under && !due && t - named >= 16) locked++;
            if (under && due) dueUnder++;
        }
        _out.WriteLine($"{Marked(r.Cancelled).Count} dropped, {locked} undercuts after 16 bars kept, {dueUnder} confirmed on an undercut");
        Assert.True(locked >= 1, "no low kept through a late undercut; the 16 is untested");
        Assert.True(dueUnder >= 1, "no low confirmed on a bar that undercut it; confirm-before-drop is untested");
    }

    /// <summary>On the Dow's settings (and the dollar's, silver's and Ether's) any bar under the pending
    /// low before it comes due drops it, however long after naming; never on the bar it comes due.
    /// Some of those drops come after d / 3 (12 bars), and with the d / 3 drop the lows differ.</summary>
    [Fact]
    public void The_dow_drops_a_pending_low_on_any_undercut_before_it_is_due()
    {
        var s = CipherCDailyStudy.Dow;
        Assert.True(s.DropUntilDue);
        var r = Run(s);
        var low = Low;
        var names = Marked(r.Pending);
        int late = 0, dueUnder = 0;
        for (int t = 1; t < low.Length; t++)
        {
            int held = r.HeldAt[t - 1];
            if (held < 0) continue;
            int named = names.Last(b => b < t);
            bool under = low[t] < low[held];
            bool due = !double.IsNaN(r.Confirmed[t]);
            bool dropped = !double.IsNaN(r.Cancelled[t]);
            Assert.Equal(under && !due, dropped);
            if (dropped && t - named >= 12) late++;
            if (under && due) dueUnder++;
        }
        _out.WriteLine($"{Marked(r.Cancelled).Count} dropped, {late} of them 12 or more bars after naming, {dueUnder} confirmed on an undercut");
        Assert.True(late >= 1, "no drop later than d / 3; the flag is untested");
        Assert.True(dueUnder >= 1, "no low confirmed on a bar that undercut it; confirm-before-drop is untested");
        Assert.NotEqual(s.Lows, Run(s with { DropUntilDue = false }).Lows);
    }

    /// <summary>The bar the count starts from is not a low: nothing is drawn or said until the first
    /// real confirmation, and no close under it fails a cycle.</summary>
    [Fact]
    public void The_starting_low_is_not_drawn()
    {
        var r = Bitcoin();
        int seed = CipherCDailyStudy.Bitcoin.Seed;
        Assert.DoesNotContain(seed, r.Lows);
        Assert.All(Enumerable.Range(0, r.ConfirmedAt[0]), t => Assert.Equal(-1, r.LastLowAt[t]));
        Assert.Equal(r.Lows[0], r.LastLowAt[r.ConfirmedAt[0]]);
        Assert.All(Enumerable.Range(0, r.ConfirmedAt[0] + 1), t => Assert.True(double.IsNaN(r.Failed[t])));
        Assert.Contains(Enumerable.Range(seed + 1, r.ConfirmedAt[0] - seed), t => Close[t] < Low[seed]);
    }

    /// <summary>The pivot break names lows the DSS does not: on the miners' settings some naming bars
    /// fail the DSS gate, and turning the break off changes the lows.</summary>
    [Fact]
    public void The_pivot_break_names_lows_the_dss_does_not()
    {
        var s = CipherCDailyStudy.Miners;
        var (d, tr) = CipherCEngine.DoubleSmoothedStochastic(High, Low, Close, 8, 9);
        int byBreak = s.Names.Count(n => !(d[n.Bar] > tr[n.Bar] && d[n.Bar] > 10 && d[n.Bar] < 75));
        _out.WriteLine($"{byBreak} of {s.Names.Length} named by the break alone");
        Assert.True(byBreak >= 10);
        Assert.NotEqual(s.Lows, Run(s with { PivotBreak = false }).Lows);
    }

    /// <summary>The DSS settings and the pivot break per preset, as read from the original.</summary>
    [Fact]
    public void The_presets_carry_the_original_s_dss_and_pivot_break()
    {
        var want = new Dictionary<string, (int, int)>
        {
            ["BTC"] = (14, 14), ["COIN"] = (14, 9), ["MSTR"] = (14, 9), ["ETH"] = (9, 9),
            ["GDX"] = (8, 9), ["GOLD"] = (8, 9), ["DXY"] = (8, 9),
        };
        foreach (var p in CipherCEngine.Presets)
        {
            Assert.Equal(want.TryGetValue(p.Key, out var v) ? v : (10, 9), (p.DssLength, p.DssSmooth));
            Assert.Equal(p.Key is "GDX" or "GDXJ" or "GOLD" or "SILVER" or "SILJ" or "DXY", p.PivotBreak);
            Assert.Equal(p.Key is "DJI" or "DXY" or "SILVER" or "ETH", p.DropUntilDue);
        }
        Assert.Equal(32, CipherCEngine.Preset("SPX").CountedDailyMin);
        var f = CipherCEngine.Fallback(true);
        Assert.Equal((32.0, 10, 9, false, false), (f.CountedDailyMin, f.DssLength, f.DssSmooth, f.PivotBreak, f.DropUntilDue));
    }

    /// <summary>The provider runs the preset's daily tier: Bitcoin from 48 with its own DSS, the
    /// miners' 22 trading days (× 7/5 on a chart that trades every day) with the break.</summary>
    [Theory]
    [InlineData("BTC")]
    [InlineData("GDX")]
    [InlineData("DJI")]
    public void The_provider_draws_the_preset_s_daily_tier(string key)
    {
        var preset = CipherCEngine.Preset(key);
        var r = CipherCProvider.Compute(Bars, new Dictionary<string, object> { [CipherCProvider.ParamPreset] = key });
        double scale = preset.CalendarDays ? 1 : 7 / 5.0;
        var tier = CipherCEngine.Tier(preset.CountedDailyMin * scale, preset.DailyMax * scale, 1, CipherCEngine.MeasureBars(Bars));
        var d = CipherCEngine.DetectDaily(Bars, tier, preset.DssLength, preset.DssSmooth, 75, 10, preset.PivotBreak, dropUntilDue: preset.DropUntilDue);
        Assert.True(d.Lows.Count > 30);
        Assert.Equal(Marked(d.Confirmed), Marked(r[CipherCProvider.CompDclConfirmed]));
        Assert.NotEqual(Marked(d.Confirmed), Marked(CipherCEngine.DetectDaily(Bars, tier, 10, 9, 75, 10, !preset.PivotBreak).Confirmed));
        if (preset.DropUntilDue)
            Assert.NotEqual(Marked(d.Confirmed), Marked(CipherCEngine.DetectDaily(Bars, tier, preset.DssLength, preset.DssSmooth, 75, 10, preset.PivotBreak).Confirmed));
    }

    /// <summary>The original's "Faster Confirmation" is the swing-high break switched on: on Bitcoin
    /// (which has no break) its lows become the break's, read off the original's drawings on the
    /// Dow and Bitcoin; on the miners (which always break) nothing moves.</summary>
    [Theory]
    [InlineData("BTC")]
    [InlineData("GDX")]
    public void Faster_confirmation_switches_the_swing_high_break_on(string key)
    {
        var preset = CipherCEngine.Preset(key);
        var off = CipherCProvider.Compute(Bars, new Dictionary<string, object> { [CipherCProvider.ParamPreset] = key });
        var on = CipherCProvider.Compute(Bars, new Dictionary<string, object>
            { [CipherCProvider.ParamPreset] = key, [CipherCProvider.ParamFasterConfirmation] = true });
        double scale = preset.CalendarDays ? 1 : 7 / 5.0;
        var tier = CipherCEngine.Tier(preset.CountedDailyMin * scale, preset.DailyMax * scale, 1, CipherCEngine.MeasureBars(Bars));
        var broken = CipherCEngine.DetectDaily(Bars, tier, preset.DssLength, preset.DssSmooth, 75, 10, true);
        Assert.Equal(Marked(broken.Confirmed), Marked(on[CipherCProvider.CompDclConfirmed]));
        if (preset.PivotBreak) Assert.Equal(Marked(off[CipherCProvider.CompDclConfirmed]), Marked(on[CipherCProvider.CompDclConfirmed]));
        else Assert.NotEqual(Marked(off[CipherCProvider.CompDclConfirmed]), Marked(on[CipherCProvider.CompDclConfirmed]));
    }

    /// <summary>
    /// The daily tier starts from the chart's first bars, so older bars arriving in front can move
    /// its first lows; it agrees again within 300 bars on every probe series. The weekly tier picked
    /// from those lows counts daily lows from the newest weekly low, so a different start can leave
    /// it a daily low out of step until an undercut or a double brings it back: on the probe series
    /// (five weekly lows in 1,400 bars) that can outlast the series (exempt from the 700-bar prepend
    /// guard in <see cref="IndicatorCausalityTests"/>). On real bars it comes back: on Bitcoin's
    /// 5,416 days, cut by 17 to 500 bars, on four presets' rules, by bar 1,200 (2026-10-27: 1,188).
    /// </summary>
    [Fact]
    public void A_prepend_changes_the_daily_lows_briefly_and_the_weekly_chain_for_longer()
    {
        string[] daily = { CipherCProvider.CompDclConfirmed, CipherCProvider.CompDailyWindow };
        string[] weekly = { CipherCProvider.CompWclConfirmed, CipherCProvider.CompWeeklyWindow };
        static int LastDiff(Dictionary<string, double[]> part, Dictionary<string, double[]> whole, string c, int k) =>
            Enumerable.Range(0, part[c].Length)
                .LastOrDefault(j => !(double.IsNaN(part[c][j]) && double.IsNaN(whole[c][j + k])) && part[c][j] != whole[c][j + k], -1 - k) + k;
        int latestWeekly = -1;
        foreach (int flavour in CausalityProbeSeries.AllFlavours)
        {
            var full = CausalityProbeSeries.Bars(flavour, 1400).ToArray();
            var whole = CipherCProvider.Compute(full, new Dictionary<string, object>());
            foreach (int k in new[] { 17, 40, 91, 140 })
            {
                var part = CipherCProvider.Compute(full.Skip(k).ToArray(), new Dictionary<string, object>());
                foreach (var c in daily) Assert.True(LastDiff(part, whole, c, k) < 300, $"series {flavour}, {k} prepended: {c} differs on bar {LastDiff(part, whole, c, k)}");
                foreach (var c in weekly) latestWeekly = Math.Max(latestWeekly, LastDiff(part, whole, c, k));
            }
        }
        _out.WriteLine($"on the probe series the weekly chain agrees again after bar {latestWeekly}");
        Assert.True(latestWeekly >= 700, "the weekly chain never outlasted the 700-bar guard; the exemption is not needed");

        int latestReal = -1;
        foreach (var key in new[] { "BTC", "DJI", "GOLD", "ETH" })
        {
            var prm = new Dictionary<string, object> { [CipherCProvider.ParamPreset] = key };
            var whole = CipherCProvider.Compute(Bars, prm);
            foreach (int k in new[] { 17, 40, 91, 140, 300, 500 })
            {
                var part = CipherCProvider.Compute(Bars.Skip(k).ToArray(), prm);
                foreach (var c in weekly)
                {
                    int last = LastDiff(part, whole, c, k);
                    Assert.True(last < 1200, $"{key}, {k} bars cut: {c} differs on bar {last}");
                    latestReal = Math.Max(latestReal, last);
                }
            }
        }
        _out.WriteLine($"on Bitcoin's bars the weekly chain agrees again after bar {latestReal}");
        Assert.True(latestReal > 700);
    }

    // ── The weekly tier on a daily chart is picked from these daily lows ──

    /// <summary>On a daily chart the weekly tier is picked from the daily lows
    /// (<see cref="CipherCWeeklyTests"/>): on Gold, Silver and the miners by weeks from the newest
    /// weekly low (8 and 14 weeks short of the table's longest weekly cycle, 28 or 26 weeks of 7
    /// bars here), everywhere else by <see cref="CipherCEngine.WeeklyFromDaily"/>, with the far
    /// "double" off only on Ether. Each preset's weekly lows are its rule's on its own daily lows,
    /// and not the other rule's.</summary>
    [Theory]
    [InlineData("BTC", false, 0, 0, true)]
    [InlineData("DJI", false, 0, 0, true)]
    [InlineData("ETH", false, 0, 0, false)]
    [InlineData("GOLD", true, 20 * 7, 14 * 7, true)]
    [InlineData("SILVER", true, 20 * 7, 14 * 7, true)]
    [InlineData("GDX", true, 18 * 7, 12 * 7, true)]
    [InlineData("SILJ", true, 18 * 7, 12 * 7, true)]
    public void Each_preset_picks_its_weekly_lows_from_its_daily_lows_by_its_rule(string key, bool byWeeks, double newBars, double doubleBars, bool farDoubles)
    {
        var r = CipherCProvider.Compute(Bars, new Dictionary<string, object> { [CipherCProvider.ParamPreset] = key });
        var preset = CipherCEngine.Preset(key);
        Assert.Equal(byWeeks, preset.WeeklyByWeeks);
        Assert.Equal(farDoubles, preset.WeeklyDoubles);
        double scale = preset.CalendarDays ? 1 : 7 / 5.0;
        var daily = CipherCEngine.Tier(preset.CountedDailyMin * scale, preset.DailyMax * scale, 1, CipherCEngine.MeasureBars(Bars));
        var d = CipherCEngine.DetectDaily(Bars, daily, preset.DssLength, preset.DssSmooth, 75, 10, preset.PivotBreak, dropUntilDue: preset.DropUntilDue);
        Assert.Equal(Marked(d.Confirmed), Marked(r[CipherCProvider.CompDclConfirmed]));

        var byDaily = Marked(CipherCEngine.WeeklyFromDaily(d, Bars, daily.MinBars, farDoubles).Confirmed);
        var weeks = Marked(CipherCEngine.WeeklyByWeeks(d, Bars, byWeeks ? newBars : 20 * 7, byWeeks ? doubleBars : 14 * 7).Confirmed);
        var mine = byWeeks ? weeks : byDaily;
        Assert.NotEmpty(mine);
        Assert.Equal(mine, Marked(r[CipherCProvider.CompWclConfirmed]));
        Assert.NotEqual(byDaily, weeks);
    }

    [Fact]
    public void Only_the_metals_pick_weekly_lows_by_weeks_and_only_ether_has_no_far_double()
    {
        var metals = new[] { "GOLD", "SILVER", "GDX", "GDXJ", "SILJ" };
        Assert.All(CipherCEngine.Presets, p => Assert.Equal(metals.Contains(p.Key), p.WeeklyByWeeks));
        Assert.All(CipherCEngine.Presets, p => Assert.Equal(p.Key != "ETH", p.WeeklyDoubles));
    }

    /// <summary>The far double, the metals' new-low margin and their double each move the weekly lows on real bars.</summary>
    [Fact]
    public void The_far_double_and_the_metal_margins_matter_on_real_bars()
    {
        var btc = CipherCEngine.Preset("BTC");
        var daily = CipherCEngine.Tier(btc.CountedDailyMin, btc.DailyMax, 1, CipherCEngine.MeasureBars(Bars));
        var d = CipherCEngine.DetectDaily(Bars, daily, btc.DssLength, btc.DssSmooth, 75, 10, btc.PivotBreak);
        Assert.NotEqual(CipherCEngine.WeeklyFromDaily(d, Bars, daily.MinBars, true).Lows,
                        CipherCEngine.WeeklyFromDaily(d, Bars, daily.MinBars, false).Lows);

        var gold = CipherCEngine.Preset("GOLD");
        var gd = CipherCEngine.Tier(gold.CountedDailyMin * 7 / 5.0, gold.DailyMax * 7 / 5.0, 1, CipherCEngine.MeasureBars(Bars));
        var g = CipherCEngine.DetectDaily(Bars, gd, gold.DssLength, gold.DssSmooth, 75, 10, gold.PivotBreak, dropUntilDue: gold.DropUntilDue);
        var lows = CipherCEngine.WeeklyByWeeks(g, Bars, 140, 98).Lows;
        Assert.NotEqual(lows, CipherCEngine.WeeklyByWeeks(g, Bars, 140 + 7, 98).Lows);
        Assert.NotEqual(lows, CipherCEngine.WeeklyByWeeks(g, Bars, 140 - 7, 98).Lows);
        // The double's margin moves nothing here between 0 and 16 weeks; without the double gold's lows move.
        Assert.NotEqual(lows, CipherCEngine.WeeklyByWeeks(g, Bars, 140, double.PositiveInfinity).Lows);
    }
}
