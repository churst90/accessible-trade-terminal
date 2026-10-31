using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fixtures;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cipher B's settings are Market Cipher B 2.4.0's inputs, each doing what it does there
/// (2026-10-31).</b>
///
/// <para>Cody: "all options found in the trading view version also in our version, true to spec".
/// MCB offers six: Use Current Chart Resolution, a Different Timeframe, Over Bought Level 1,
/// Trigger 1, Over Sold Level 1 and Trigger 2. Each was changed on TradingView to see what it
/// moves: the four numbers are the four lines; Over Sold Level 1 is also the buy dot's threshold,
/// and Trigger 2 also places the money-flow bar (Trigger 2 − 40 … − 50); with the resolution box
/// off, everything is computed on the other timeframe and each value steps on the chart bar that
/// finishes its period. These tests pin those properties, the binding that makes one number drive
/// both line and calculation, and the migration of a chart saved with the old settings.</para>
/// </summary>
public sealed class CipherBMcbInputsTests
{
    private static readonly IndicatorModelFactory Factory = new(
        new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
        new MockIndicatorPreferencesService());

    private static ChartSeries Restore(SeriesConfig saved)
    {
        var providers = new List<IIndicatorProvider> { new CipherBProvider() };
        var engine = new IndicatorEngine(new IndicatorService(providers, NullLogger<IndicatorService>.Instance),
            new CustomIndicatorRegistry(), providers);
        var meta = new CipherBProvider().GetIndicators();
        WorkspaceInitializer.MigrateSeriesConfig(saved, meta);
        return SeriesManagementService.MaterializeSaved(saved, meta[0], Factory,
            Array.Empty<IReadOnlyDictionary<string, object>>(), engine, new MockIndicatorPreferencesService());
    }

    private static SeriesConfig Saved(params (string Key, double Value)[] p)
    {
        var c = new SeriesConfig { Id = "cb-1", Name = "Cipher B", IndicatorCode = "CIPHER_B", Pane = "Pane_CIPHER_B" };
        foreach (var (k, v) in p) c.Parameters[k] = v;
        return c;
    }

    private static LevelConfig L(ChartSeries s, string name) => s.Levels.Single(l => l.Name == name);
    private static ZoneBandConfig Bar(ChartSeries s) => s.Config.ZoneBands.Single(b => b.DisplayName == "Money Flow Bar");

    private static Dictionary<string, double[]> Run(IReadOnlyList<Ohlcv> bars, params (string Key, object Value)[] p)
    {
        var pars = new Dictionary<string, object>();
        foreach (var (k, v) in p) pars[k] = v;
        var results = new Dictionary<string, double[]>();
        new CipherBProvider().Calculate("CIPHER_B", bars.ToArray(), pars, new IndicatorResultBuffer(results, bars.Count));
        return results;
    }

    /// <summary>Weekday daily bars from Monday 2024-01-01 (a stock's calendar), a seeded random walk.</summary>
    private static List<Ohlcv> WeekdayBars(int count)
    {
        var rng = new Random(31);
        var bars = new List<Ohlcv>(count);
        double close = 100;
        for (var d = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc); bars.Count < count; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            double open = close;
            close = Math.Max(1, open * (1 + (rng.NextDouble() - 0.5) * 0.06));
            double high = Math.Max(open, close) * (1 + rng.NextDouble() * 0.01);
            double low  = Math.Min(open, close) * (1 - rng.NextDouble() * 0.01);
            bars.Add(new Ohlcv(d, open, high, low, close, 1000 + rng.Next(1000)));
        }
        return bars;
    }

    // ── One number, one meaning ───────────────────────────────────────────────────

    [Fact]
    public void A_bound_level_takes_its_setting_and_a_bound_band_moves_by_it_keeping_its_height()
    {
        var levels = new List<LevelConfig>
        {
            new() { Name = "Bound", Value = 1, ValueParameter = "P" },
            new() { Name = "Fixed", Value = 5 },
            new() { Name = "Unset", Value = 9, ValueParameter = "Missing" },
        };
        var bands = new List<ZoneBandConfig>
        {
            new() { DisplayName = "Bar", FixedTop = -93, FixedBottom = -103, AnchorParameter = "T", AnchorValue = -53 },
        };
        var p = new Dictionary<string, double> { ["P"] = 7, ["T"] = -30 };

        ParameterBinding.Apply(levels, bands, p);
        Assert.Equal(7, levels[0].Value);
        Assert.Equal(5, levels[1].Value);
        Assert.Equal(9, levels[2].Value);
        Assert.Equal((-70.0, -80.0, -30.0), (bands[0].FixedTop, bands[0].FixedBottom, bands[0].AnchorValue));

        // Idempotent: applying again (every load, every Apply) moves nothing further.
        ParameterBinding.Apply(levels, bands, p);
        Assert.Equal(7, levels[0].Value);
        Assert.Equal((-70.0, -80.0, -30.0), (bands[0].FixedTop, bands[0].FixedBottom, bands[0].AnchorValue));

        // An unset (NaN) setting moves nothing.
        ParameterBinding.Apply(levels, bands, new Dictionary<string, double> { ["P"] = double.NaN, ["T"] = double.NaN });
        Assert.Equal(7, levels[0].Value);
        Assert.Equal(-70.0, bands[0].FixedTop);
    }

    [Fact]
    public void Cipher_Bs_four_lines_and_its_money_flow_bar_are_where_its_settings_put_them()
    {
        var fresh = Restore(Saved());
        Assert.Equal((60.0, 53.0, -53.0, -60.0),
            (L(fresh, "Extreme OB").Value, L(fresh, "Overbought").Value, L(fresh, "Oversold").Value, L(fresh, "Extreme OS").Value));
        Assert.Equal((-93.0, -103.0), (Bar(fresh).FixedTop, Bar(fresh).FixedBottom));

        var moved = Restore(Saved((CipherBProvider.ParamOBLevel1, 70), (CipherBProvider.ParamTrigger1, 45),
                                  (CipherBProvider.ParamOSLevel1, -40), (CipherBProvider.ParamTrigger2, -30)));
        Assert.Equal((70.0, 45.0, -30.0, -40.0),
            (L(moved, "Extreme OB").Value, L(moved, "Overbought").Value, L(moved, "Oversold").Value, L(moved, "Extreme OS").Value));
        // MCB's bar is Trigger 2 − 40 … − 50: at Trigger 2 = −30 TradingView drew it at −70 … −80.
        Assert.Equal((-70.0, -80.0), (Bar(moved).FixedTop, Bar(moved).FixedBottom));
        Assert.Equal(0.0, L(moved, "Zero").Value);
        Assert.Equal(100.0, L(moved, "100%").Value);
    }

    /// <summary>
    /// The buy dot is a cross up with the FAST wave below Over Sold Level 1, whatever that is set
    /// to: on TradingView, Over Sold Level 1 at −40 drew every dot it drew at −60 and more.
    /// </summary>
    [Theory]
    [InlineData(-60.0)]
    [InlineData(-40.0)]
    [InlineData(-20.0)]
    public void The_buy_dot_threshold_is_Over_Sold_Level_1(double level)
    {
        var bars = WeekdayBars(1500);
        var r = Run(bars, (CipherBProvider.ParamOSLevel1, level));
        var fast = r[CipherBProvider.CompWT1]; var up = r[CipherBProvider.CompCrossBull]; var buy = r[CipherBProvider.CompBlue];
        int dots = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            bool expected = !double.IsNaN(up[i]) && fast[i] < level;
            Assert.True(expected == !double.IsNaN(buy[i]), $"bar {i}: cross up {!double.IsNaN(up[i])}, fast {fast[i]:F2}, level {level}");
            if (expected) dots++;
        }
        Assert.True(dots > 0, $"no buy dots below {level}: the theory proves nothing");
        if (level > -60) Assert.True(dots > Fired(Run(bars)[CipherBProvider.CompBlue]), "a higher threshold must add dots");
    }

    private static int Fired(double[] a) => a.Count(v => !double.IsNaN(v));

    // ── A chart saved with the old settings ───────────────────────────────────────

    [Fact]
    public void Codys_saved_cipher_b_opens_with_mcbs_six_settings_and_nothing_else()
    {
        var saved = JsonConvert.DeserializeObject<SeriesConfig>(CodysCipherBSave.Json)!;
        Assert.True(saved.Parameters.ContainsKey("WT1Period"));
        Assert.True(saved.Parameters.ContainsKey("BuyDotLevel"));

        var meta = new CipherBProvider().GetIndicators();
        WorkspaceInitializer.MigrateSeriesConfig(saved, meta);

        var declared = meta[0].Parameters.Select(p => p.Name).OrderBy(n => n).ToList();
        Assert.Equal(declared, saved.Parameters.Keys.Concat(saved.StringParameters.Keys).OrderBy(n => n).ToList());
        Assert.Equal(1, saved.Parameters[CipherBProvider.ParamUseChartTimeframe]);
        Assert.Equal("60", saved.StringParameters[CipherBProvider.ParamOtherTimeframe]);
        Assert.Equal(60, saved.Parameters[CipherBProvider.ParamOBLevel1]);
        Assert.Equal(53, saved.Parameters[CipherBProvider.ParamTrigger1]);
        Assert.Equal(-60, saved.Parameters[CipherBProvider.ParamOSLevel1]);   // his Buy Dot Level 60
        Assert.Equal(-53, saved.Parameters[CipherBProvider.ParamTrigger2]);
    }

    /// <summary>The old Buy Dot Level (how deep below zero) carries over when it was moved.</summary>
    [Fact]
    public void A_moved_buy_dot_level_becomes_over_sold_level_1()
    {
        var s = Restore(Saved(("BuyDotLevel", 45)));
        Assert.Equal(-45, s.Config.Parameters[CipherBProvider.ParamOSLevel1]);
        Assert.Equal(-45, L(s, "Extreme OS").Value);
        Assert.False(s.Config.Parameters.ContainsKey("BuyDotLevel"));
    }

    /// <summary>
    /// Cody's own save, whose lines were saved before they were settings: restored with a moved
    /// Buy Dot Level and a Trigger 2 set, its −60 and −53 lines and its money-flow bar go where
    /// the settings say, and its lines take MCB's white and the ±60 lines its 2 pixels.
    /// </summary>
    [Fact]
    public void Codys_saved_lines_follow_the_settings_and_take_mcbs_look()
    {
        var saved = JsonConvert.DeserializeObject<SeriesConfig>(CodysCipherBSave.Json)!;
        Assert.Contains(saved.Levels, l => l.Name == "Extreme OS" && l.Value == -60 && l.ValueParameter == null);
        saved.Parameters["BuyDotLevel"] = 45;
        saved.Parameters[CipherBProvider.ParamTrigger2] = -30;

        var s = Restore(saved);
        Assert.Equal(-45, L(s, "Extreme OS").Value);
        Assert.Equal(-30, L(s, "Oversold").Value);
        Assert.Equal((-70.0, -80.0), (Bar(s).FixedTop, Bar(s).FixedBottom));
        Assert.Equal(60, L(s, "Extreme OB").Value);
        foreach (var name in new[] { "Extreme OB", "Extreme OS" })
            Assert.Equal((CipherBProvider.SolidWhite, 2f), (L(s, name).ColorHex, L(s, name).Thickness));
        foreach (var name in new[] { "Overbought", "Oversold" })
            Assert.Equal((CipherBProvider.DottedWhite, 1f), (L(s, name).ColorHex, L(s, name).Thickness));
    }

    /// <summary>A new Cipher B's ±60 lines are 2 pixels and the rest 1, and a width the user chose
    /// on a saved line is kept.</summary>
    [Fact]
    public void The_extreme_lines_are_two_pixels_and_a_users_width_stays()
    {
        var fresh = Restore(Saved());
        Assert.Equal(2f, L(fresh, "Extreme OB").Thickness);
        Assert.Equal(2f, L(fresh, "Extreme OS").Thickness);
        Assert.All(fresh.Levels.Where(l => l.Name is not ("Extreme OB" or "Extreme OS")), l => Assert.Equal(1f, l.Thickness));

        var saved = JsonConvert.DeserializeObject<SeriesConfig>(CodysCipherBSave.Json)!;
        saved.Levels.Single(l => l.Name == "Extreme OB").Thickness = 3f;
        var s = Restore(saved);
        Assert.Equal(3f, L(s, "Extreme OB").Thickness);
        Assert.Equal(2f, L(s, "Extreme OS").Thickness);
    }

    /// <summary>
    /// A line the user moved before the lines were settings keeps its place, and its setting takes
    /// its value: otherwise the setting's default would move the line back on load.
    /// </summary>
    [Fact]
    public void A_line_moved_before_the_lines_were_settings_stays_and_gives_its_setting_its_value()
    {
        var saved = Saved();
        saved.Levels.Add(new LevelConfig { Name = "Overbought", Value = 55, IsVisible = true, ColorHex = "#8C8C96", DashStyle = DashStyle.Dot });
        saved.Levels.Add(new LevelConfig { Name = "Oversold", Value = -53, IsVisible = true, ColorHex = "#8C8C96", DashStyle = DashStyle.Dot });
        var s = Restore(saved);
        Assert.Equal(55, L(s, "Overbought").Value);
        Assert.Equal(55, s.Config.Parameters[CipherBProvider.ParamTrigger1]);
        Assert.Equal(-53, s.Config.Parameters[CipherBProvider.ParamTrigger2]);
    }

    // ── Use Current Chart Resolution / Different Timeframe ────────────────────────

    [Theory]
    [InlineData("60", "1h")]
    [InlineData("240", "4h")]
    [InlineData("1440", "1d")]
    [InlineData("15", "15m")]
    [InlineData("90", "90m")]
    [InlineData("D", "1d")]
    [InlineData("3D", "3d")]
    [InlineData("W", "1w")]
    [InlineData("2W", "2w")]
    [InlineData("M", "1M")]
    [InlineData("1h", "1h")]
    [InlineData(" 240 ", "4h")]
    [InlineData("", null)]
    [InlineData("0", null)]
    [InlineData("30S", null)]
    [InlineData("0D", null)]
    [InlineData("weekly", null)]
    public void Tradingviews_timeframe_text_is_read_as_an_app_timeframe(string text, string? token) =>
        Assert.Equal(token, CipherBProvider.ToTimeframeToken(text));

    [Fact]
    public void Only_a_longer_timeframe_with_the_box_off_is_used()
    {
        var bars = WeekdayBars(30).ToArray();
        string? Tf(params (string, object)[] p)
        {
            var d = new Dictionary<string, object>();
            foreach (var (k, v) in p) d[k] = v;
            return CipherBProvider.HigherTimeframe(bars, d);
        }
        const string Box = CipherBProvider.ParamUseChartTimeframe, Other = CipherBProvider.ParamOtherTimeframe;
        Assert.Null(Tf());                                                        // the box is on by default
        Assert.Null(Tf((Box, 1.0), (Other, "W"), ("__timeframe", "1d")));
        Assert.Equal("1w", Tf((Box, 0.0), (Other, "W"), ("__timeframe", "1d")));
        Assert.Null(Tf((Box, 0.0), (Other, "D"), ("__timeframe", "1d")));          // not longer
        Assert.Null(Tf((Box, 0.0), (Other, "60"), ("__timeframe", "1d")));         // shorter
        Assert.Equal("1w", Tf((Box, 0.0), (Other, "W")));                          // no stamp: the bars' spacing
        Assert.Null(Tf((Box, 0.0), (Other, "nonsense"), ("__timeframe", "1d")));
    }

    /// <summary>
    /// With the box off and "W" on a daily chart, every value is the weekly calculation's, shown
    /// on the last daily bar of its week; the days before show the week before, and the newest bar
    /// the week still forming. That is what MCB 2.4.0 plotted on TVC:DJI 1D (2026-10-31).
    /// </summary>
    [Fact]
    public void Off_the_charts_resolution_each_value_steps_on_the_bar_that_finishes_its_period()
    {
        var days = WeekdayBars(1203);   // ends mid-week: the newest week is still forming
        Assert.NotEqual(DayOfWeek.Friday, days[^1].Date.DayOfWeek);
        var r = Run(days, (CipherBProvider.ParamUseChartTimeframe, 0.0), (CipherBProvider.ParamOtherTimeframe, "W"), ("__timeframe", "1d"));
        var weeks = new ResamplerService().Resample(days.ToList(), "1w");
        var w = Run(weeks);

        int steps = 0;
        string[] values = { CipherBProvider.CompWT1, CipherBProvider.CompWT2, CipherBProvider.CompMoneyFlowWave,
                            CipherBProvider.CompMcbRsi, CipherBProvider.CompMcbStoch };
        for (int i = 0; i < days.Count; i++)
        {
            var start = TimeframeUtility.GetPeriodStart(days[i].Date, "1w");
            int k = weeks.FindIndex(b => b.Date == start);
            Assert.True(k >= 0);
            bool last = i == days.Count - 1 || TimeframeUtility.GetPeriodStart(days[i + 1].Date, "1w") != start;
            int src = last ? k : k - 1;
            foreach (var c in values)
            {
                double expected = src < 0 ? double.NaN : w[c][src];
                Assert.True(expected.Equals(r[c][i]), $"{c} on {days[i].Date:yyyy-MM-dd}: {r[c][i]} vs week {src}'s {expected}");
            }
            if (last) steps++;
        }
        Assert.Equal(weeks.Count, steps);

        // The dots come from the stepped waves, so they fall only on a week's last bar.
        int dots = 0;
        foreach (var c in new[] { CipherBProvider.CompCrossBull, CipherBProvider.CompCrossBear })
            for (int i = 0; i < days.Count - 1; i++)
                if (!double.IsNaN(r[c][i]))
                {
                    dots++;
                    Assert.NotEqual(TimeframeUtility.GetPeriodStart(days[i].Date, "1w"), TimeframeUtility.GetPeriodStart(days[i + 1].Date, "1w"));
                }
        Assert.True(dots > 0);
    }

    [Fact]
    public void A_timeframe_no_longer_than_the_charts_gives_the_charts_own_values()
    {
        var days = WeekdayBars(600);
        var own = Run(days);
        foreach (var other in new[] { "60", "D", "1440" })
        {
            var r = Run(days, (CipherBProvider.ParamUseChartTimeframe, 0.0), (CipherBProvider.ParamOtherTimeframe, other), ("__timeframe", "1d"));
            foreach (var (k, v) in own) Assert.Equal(v, r[k]);
        }
    }

    [Fact]
    public void Warm_up_is_counted_in_the_other_timeframes_bars()
    {
        var p = new CipherBProvider();
        int own = p.GetStabilityWindow("CIPHER_B", new Dictionary<string, object>());
        Assert.Equal(225, own);
        Assert.Equal(own * 7, p.GetStabilityWindow("CIPHER_B", new Dictionary<string, object>
            { [CipherBProvider.ParamUseChartTimeframe] = 0.0, [CipherBProvider.ParamOtherTimeframe] = "W", ["__timeframe"] = "1d" }));
        Assert.Equal(own, p.GetStabilityWindow("CIPHER_B", new Dictionary<string, object>
            { [CipherBProvider.ParamUseChartTimeframe] = 0.0, [CipherBProvider.ParamOtherTimeframe] = "60", ["__timeframe"] = "1d" }));
    }

    /// <summary>
    /// "Different Timeframe" is text ("60", "W"). A numeric-looking one must stay text all the way
    /// to the calculation: stored as a number, the provider would read no timeframe at all.
    /// </summary>
    [Fact]
    public void A_numeric_looking_timeframe_stays_text()
    {
        var meta = new CipherBProvider().GetIndicators()[0];
        var list = SeriesManagementService.DefaultParameterList(meta);
        var series = Factory.CreateSeriesFromMetadata(meta, meta.Name, "Pane_X", list, componentOverrides: null);
        Assert.Equal("60", series.Config.StringParameters[CipherBProvider.ParamOtherTimeframe]);
        Assert.False(series.Config.Parameters.ContainsKey(CipherBProvider.ParamOtherTimeframe));
    }
}
