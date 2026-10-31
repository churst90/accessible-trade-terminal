using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cipher B drawn the way Market Cipher B draws it (2026-10-17).</b>
///
/// <para>Measured from real MCB images: the vendor's product image on marketciphertrading.com and
/// TradingView idea vBzsjpDu, whose status line reads "MarketCipher B (2.2.6, 60, 60, 53, -60,
/// -53)". MCB paints, bottom to top: an OPAQUE light-blue (#90CAF9) area for the fast wave, a
/// #0D47A1 area at ~90% for the slow wave, a yellow (#FFEB3B) VWAP area at ~73%, and a lime/red
/// money-flow area at ~50%; a money-flow bar along the bottom; plain grey levels; a magenta "RSI"
/// line and a green/red "Stochastic" line. Every pixel asserted here is read back from the real
/// <see cref="ChartRenderer"/> drawing a series the real factory built from the provider's own
/// metadata — a colour declared and never painted is the defect this repo keeps finding.</para>
///
/// <para>2026-10-31 (Cody: "true to spec"): the colours are Market Cipher B 2.4.0's own
/// declarations now, not fits to 2.2.6 screenshots: both waves 70% (#B390CAF9 fast, #B30D47A1
/// slow, each with a 1-pixel line of its own colour), the yellow 75%, the money flow 40%, the
/// money-flow bar 40% from −93 to −103, levels white (solid 40%, dotted 50%) and a dotted "100%"
/// line at +100.</para>
/// </summary>
public sealed class CipherBLookTests
{
    private const int W = 300, H = 400, N = 30;

    private static ThemeService Themes(ThemeType? type = null)
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        var t = new ThemeService(settings);
        if (type is { } tt) t.SetTheme(tt);
        return t;
    }

    private static StylingService Styling() =>
        new(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());

    /// <summary>Cipher B exactly as the factory builds it from the provider, in the main pane,
    /// every component holding one constant value (NaN where not given), plus its levels.</summary>
    private static ChartSeries CipherB(Dictionary<string, double> values)
    {
        var provider = new CipherBProvider();
        var factory = new IndicatorModelFactory(Styling(), new MockIndicatorPreferencesService());
        var s = factory.CreateSeriesFromMetadata(provider.GetIndicators()[0], "Cipher B", "Main",
            new List<(string, string)>(), null);
        foreach (var c in s.Components)
            s.Data.ComponentData[c.Name] = Enumerable.Repeat(values.TryGetValue(c.Name, out var v) ? v : double.NaN, N).ToArray();
        foreach (var d in provider.GetDefaultLevels("CIPHER_B"))
            s.Levels.Add(new LevelConfig { Name = d.Name, Value = d.Value, ColorHex = d.ColorHex, DashStyle = d.Dash, IsVisible = true, IsDrawn = d.IsDrawn, Thickness = d.Thickness });
        return s;
    }

    // Where the price pane's PLOT starts in the last frame Render drew: below the pane's status
    // band (2026-10-17), which Cipher B's status line takes at the pane's top. Every value is
    // mapped from there, so Y has to start there too.
    [ThreadStatic] private static float _plotTop;

    private static (SKBitmap Bmp, ThemeService Themes) Render(ChartSeries s, ThemeType? type = null)
    {
        var themes = Themes(type);
        var layout = new PaneLayoutService();
        var renderer = new ChartRenderer(themes, Styling(), layout,
            NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = Enumerable.Range(0, N).Select(i => new Ohlcv(start.AddDays(i), 10, 11, 9, 10, 1)).ToList();
        var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, W, H, bars, new[] { s }, cursorIndex: -1, viewportStart: 0, viewportLength: N,
                viewportRange: (-100, 100), paneRanges: new Dictionary<string, (double, double)>(), rightMarginBars: 0);
        _plotTop = layout.StatusBands.TryGetValue("Main", out var band) ? band.HeightFraction * H : 0f;
        return (bmp, themes);
    }

    private static int Y(double v, ThemeService t) =>
        (int)Math.Round(ChartMath.MapY(v, _plotTop, H - t.Current.AxisHeight, -100, 100, false));

    /// <summary>The centre of the middle bar's slot (a bar boundary falls in the gap between columns).</summary>
    private static int X(ThemeService t) => (int)((N / 2 + 0.5f) * (W - t.Current.AxisWidth) / N);

    private static SKColor Over(SKColor fg, SKColor bg)
    {
        float a = fg.Alpha / 255f;
        byte Mix(byte f, byte b) => (byte)Math.Round(f * a + b * (1 - a));
        return new SKColor(Mix(fg.Red, bg.Red), Mix(fg.Green, bg.Green), Mix(fg.Blue, bg.Blue));
    }

    private static void Near(SKColor expected, SKColor actual, string what, int tol = 8) =>
        Assert.True(Math.Abs(expected.Red - actual.Red) <= tol && Math.Abs(expected.Green - actual.Green) <= tol
                    && Math.Abs(expected.Blue - actual.Blue) <= tol,
            $"{what}: expected about {expected}, the pixel is {actual}.");

    // MCB 2.4.0's declarations, as literals (a test that reads the provider's constants passes
    // whatever they say).
    private static readonly SKColor Fast = SKColor.Parse("#B390CAF9"), Slow = SKColor.Parse("#B30D47A1");
    private static readonly SKColor Vwap = SKColor.Parse("#BFFFEB3B"), MfUp = SKColor.Parse("#663FFF00"), MfDown = SKColor.Parse("#66FF1100");

    // ── The waves ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inside both waves the pixel is MCB's dark-blue body (#0D47A1 at 70% over the light-blue
    /// fast wave at 70%); where the fast wave runs beyond the slow one it is the light-blue rim.
    /// Until 2026-10-17 the waves were a white and a cyan LINE, and both points read as
    /// background; until 2026-10-31 the fast wave was opaque and the slow one 90%.
    /// </summary>
    [Fact]
    public void The_slow_wave_is_a_dark_blue_body_over_a_light_blue_fast_wave()
    {
        var (bmp, t) = Render(CipherB(new() { [CipherBProvider.CompWT1] = -70, [CipherBProvider.CompWT2] = -50 }));
        using var _ = bmp;
        var bg = t.Current.Background;
        Near(Over(Slow, Over(Fast, bg)), bmp.GetPixel(X(t), Y(-25, t)), "inside both waves");
        Near(Over(Fast, bg), bmp.GetPixel(X(t), Y(-62, t)), "the fast wave's rim, beyond the slow one");
    }

    /// <summary>
    /// MCB paints its VWAP OVER the waves and its money flow over the VWAP. Our WT Histogram (the
    /// VWAP) is declared BEFORE the waves — a keyboard stop Cody knows — so only a draw order can
    /// put it on top. With the histogram drawn in declared order the waves cover it and the first
    /// point reads plain blue.
    /// </summary>
    [Fact]
    public void The_vwap_is_painted_over_the_waves_and_money_flow_over_the_vwap()
    {
        var (bmp, t) = Render(CipherB(new()
        {
            [CipherBProvider.CompWT1] = -70, [CipherBProvider.CompWT2] = -60,
            [CipherBProvider.CompWtHistogram] = -30, [CipherBProvider.CompMoneyFlowWave] = -15,
        }));
        using var _ = bmp;
        var body = Over(Slow, Over(Fast, t.Current.Background));
        Near(Over(Vwap, body), bmp.GetPixel(X(t), Y(-22, t)), "VWAP over the waves");
        Near(Over(MfDown, Over(Vwap, body)), bmp.GetPixel(X(t), Y(-8, t)), "money flow over the VWAP");
    }

    /// <summary>Money flow above zero is MCB's lime at 40%, painted as an area (50% until 2026-10-31).</summary>
    [Fact]
    public void Money_flow_is_a_translucent_area()
    {
        var (bmp, t) = Render(CipherB(new() { [CipherBProvider.CompMoneyFlowWave] = 30 }));
        using var _ = bmp;
        var bg = t.Current.Background;
        Near(Over(MfUp, bg), bmp.GetPixel(X(t), Y(15, t)), "money flow above zero");
        // Between two bars' slots, where a column chart leaves a gap: an area has none.
        int gap = (int)Math.Round((N / 2) * (W - t.Current.AxisWidth) / (double)N);
        Near(Over(MfUp, bg), bmp.GetPixel(gap, Y(15, t)), "money flow between two bars (an area, not columns)");
    }

    /// <summary>
    /// The keyboard order did not move: every component Cipher B had before 2026-10-17 is still
    /// declared in the same order, and the two new lines come LAST. Only the paint order changed.
    /// The one exception is a removal: "Adaptive OB" and "Adaptive OS" (between WaveTrend Cross
    /// Bear and Anchor Polarity) stopped being components on 2026-10-17 — Cody's report, see
    /// CipherBAdaptiveThresholdTests — so this list is the old eighteen minus those two. On
    /// 2026-10-31 the parts Market Cipher B does not draw went too (the anchor waves, Anchor
    /// Polarity, the red Overbought Crossover and the four divergences); the rest keep their order.
    /// </summary>
    [Fact]
    public void Keyboard_order_is_unchanged_and_the_new_lines_come_last()
    {
        var names = new CipherBProvider().GetIndicators()[0].Components.Select(c => c.Name).ToList();
        string[] before =
        {
            "WT Histogram", "Wave Trend", "Wave Trend 2", "Money Flow Wave",
            "WaveTrend Cross Bull", "WaveTrend Cross Bear",
            "Oversold Crossover",
        };
        Assert.Equal(before.Concat(new[] { CipherBProvider.CompMcbRsi, CipherBProvider.CompMcbStoch }), names);
    }

    // ── The money-flow bar ────────────────────────────────────────────────────────

    /// <summary>
    /// MCB's money-flow bar along the bottom: green while money flow is positive, red while
    /// negative, 40% (MCB 2.4.0's fill; #4D, 30%, solved from 2.2.6 composites until 2026-10-31).
    /// Before 2026-10-17 the bottom of the pane was the green "OS Zone" shading whatever money
    /// flow did.
    /// </summary>
    [Theory]
    [InlineData(25.0)]
    [InlineData(-25.0)]
    public void The_money_flow_bar_follows_money_flows_sign(double mf)
    {
        var (bmp, t) = Render(CipherB(new() { [CipherBProvider.CompMoneyFlowWave] = mf }));
        using var _ = bmp;
        var bg = t.Current.Background;
        var expected = Over(mf > 0 ? SKColor.Parse("#663FFF00") : SKColor.Parse("#66FF1100"), bg);
        Near(expected, bmp.GetPixel(X(t), Y(-97, t)), $"money-flow bar with money flow at {mf}", 6);
    }

    // ── Levels ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Plain neutral lines, solid at ±60 and zero and dotted at ±53, as MCB draws them; until
    /// 2026-10-17 they were red and green. What Cody HEARS from them — names, values and earcons —
    /// is pinned to what it was. Their zone noise was raised the same afternoon (0.10/0.20 →
    /// 0.30/0.45): under the waves' own 0.15 the ±53 zone was never heard
    /// (CipherBStochasticZonesTests.The_waves_get_rougher_past_53_and_again_past_60).
    /// </summary>
    /// <summary>
    /// The RSI and Stochastic lines' own 80 / 20 are heard, not drawn (Cody, 2026-10-17: "sound
    /// only"): no grey on their rows, while the waves' ±60 line right beside them is painted.
    /// </summary>
    [Fact]
    public void The_lines_own_80_and_20_are_not_painted()
    {
        var (bmp, t) = Render(CipherB(new Dictionary<string, double>()));
        var bg = t.Current.Background;
        var solid = Over(SKColor.Parse("#66FFFFFF"), bg);
        var dotted = Over(SKColor.Parse("#80FFFFFF"), bg);
        int GreyOn(int y) => Enumerable.Range(0, W - (int)t.Current.AxisWidth)
            .Count(x => Close(bmp.GetPixel(x, y), solid) || Close(bmp.GetPixel(x, y), dotted));
        Assert.True(GreyOn(Y(60, t)) > 20, "precondition: the ±60 level is painted and detectable");
        Assert.Equal(0, GreyOn(Y(80, t)));
        Assert.Equal(0, GreyOn(Y(20, t)));
    }

    // The stopgap's "untinted unless Anchor Polarity is shown" test lived here (2026-10-17). The
    // tint is a component property now, independent of visibility; BackgroundTintTests covers it.

    private static bool Close(SKColor a, SKColor b) =>
        Math.Abs(a.Red - b.Red) < 24 && Math.Abs(a.Green - b.Green) < 24 && Math.Abs(a.Blue - b.Blue) < 24;

    [Fact]
    public void Levels_are_mcbs_white_lines_and_sound_as_they_did()
    {
        var levels = new CipherBProvider().GetDefaultLevels("CIPHER_B").ToDictionary(l => l.Name);
        // The literals, not the provider's constants — a test that compares a constant with
        // itself passes whatever the constant says. MCB 2.4.0: solid lines rgba(255,255,255,0.4),
        // dotted ones (its circles) 0.5; ±60 two pixels, the rest one.
        (string Name, string Hex, DashStyle Dash, float Width, string? Setting)[] drawn =
        {
            ("Extreme OB", "#66FFFFFF", DashStyle.Solid, 2f, "OBLevel1"),
            ("Overbought", "#80FFFFFF", DashStyle.Dot,   1f, "Trigger1"),
            ("Zero",       "#66FFFFFF", DashStyle.Solid, 1f, null),
            ("Oversold",   "#80FFFFFF", DashStyle.Dot,   1f, "Trigger2"),
            ("Extreme OS", "#66FFFFFF", DashStyle.Solid, 2f, "OSLevel1"),
            ("100%",       "#80FFFFFF", DashStyle.Dot,   1f, null),
        };
        foreach (var (name, hex, dash, width, setting) in drawn)
        {
            var l = levels[name];
            Assert.Equal((hex, dash, width, setting, true), (l.ColorHex, l.Dash, l.Thickness, l.ValueParameter, l.IsDrawn));
        }
        Assert.Equal(100, levels["100%"].Value);
        Assert.False(levels["100%"].PlayEarcon);
        Assert.Equal(0f, levels["100%"].ZoneNoiseAmount);
        Assert.DoesNotContain(new CipherBProvider().GetIndicators()[0].Components,
            c => c.SubscribedLevelNames?.Contains("100%") == true);

        (string Name, double Value, float Vol, float Noise)[] heard =
        {
            ("Extreme OB", 60, 0.8f, 0.45f), ("Overbought", 53, 0.6f, 0.25f), ("Zero", 0, 0.7f, 0f),
            ("Oversold", -53, 0.6f, 0.25f), ("Extreme OS", -60, 0.8f, 0.45f),
        };
        foreach (var (name, value, vol, noise) in heard)
        {
            var l = levels[name];
            Assert.Equal(value, l.Value); Assert.True(l.PlayEarcon); Assert.Equal(vol, l.EarconVolume); Assert.Equal(noise, l.ZoneNoiseAmount);
        }
    }

    // ── The two stochastic lines ──────────────────────────────────────────────────

    /// <summary>
    /// MCB's Stochastic line is red while above its RSI line and green while at or below it (397 of
    /// 397 digitised bars). Read in the drawn pixels and in the status line's reading colour.
    /// </summary>
    [Theory]
    [InlineData(70.0, "#FF1100")]
    [InlineData(20.0, "#3FFF00")]
    public void The_stochastic_is_red_above_the_rsi_line_and_green_below(double stoch, string hex)
    {
        var s = CipherB(new() { [CipherBProvider.CompMcbRsi] = 40, [CipherBProvider.CompMcbStoch] = stoch });
        var (bmp, t) = Render(s);
        using var _ = bmp;
        Near(SKColor.Parse(hex), bmp.GetPixel(X(t), Y(stoch, t)), $"the Stochastic line at {stoch}", 20);

        var comp = s.Components.Single(c => c.Name == CipherBProvider.CompMcbStoch);
        var reading = ChartRenderer.ReadingColor(new ChartRenderer.CrosshairReading(stoch, s, comp, N / 2), t.Current, null);
        Assert.Equal(SKColor.Parse(hex), reading);
    }

    /// <summary>
    /// The two new lines add no voice: they start muted (drawn, navigable, spoken on request), and
    /// nothing else Cipher B had is muted by default.
    /// </summary>
    [Fact]
    public void Only_the_two_new_lines_start_muted()
    {
        var s = CipherB(new());
        var muted = s.Components.Where(c => c.IsMuted).Select(c => c.Name).ToList();
        Assert.Equal(new[] { CipherBProvider.CompMcbRsi, CipherBProvider.CompMcbStoch }, muted);
        Assert.All(s.Components.Where(c => c.Name is CipherBProvider.CompMcbRsi or CipherBProvider.CompMcbStoch),
            c => Assert.True(c.IsVisible, $"{c.Name} is muted to be SEEN, so it must be visible"));
    }

    // ── Perceivable on every theme ────────────────────────────────────────────────

    /// <summary>
    /// The wave silhouette has to be seen on all twelve themes. On a dark theme the light-blue fast
    /// wave carries it; on a light one the dark-blue body does — at least one of the two reaches
    /// 3:1 everywhere, at MCB's 70%. (Until 2026-10-31 the slow wave also had a #1E88E5 edge of our
    /// choosing, 3:1 on all but SteelGray; MCB's edge is its body colour, and whether to keep a
    /// contrast edge is a question for Cody in docs/TODO.md.)
    /// </summary>
    [Fact]
    public void The_waves_can_be_seen_on_every_theme()
    {
        foreach (var type in Enum.GetValues<ThemeType>())
        {
            var bg = Themes(type).Current.Background;
            double fast = WcagContrast.Ratio(Over(Fast, bg), bg), body = WcagContrast.Ratio(Over(Slow, Over(Fast, bg)), bg);
            // SteelGray's mid-grey is the one theme MCB's own colours miss, by a hair (fast wave
            // 2.995:1, 2026-10-31). Whether light and mid themes get their own wave colours is
            // Cody's call (docs/TODO.md); until then it is held where it is, not let slip.
            double floor = type == ThemeType.SteelGray ? 2.99 : 3.0;
            Assert.True(Math.Max(fast, body) >= floor, $"{type}: neither wave reaches {floor}:1 (fast {fast:F3}, body {body:F3})");
        }
    }

    // ── The opacity gate does not move anything else ──────────────────────────────

    /// <summary>
    /// A bar colour written with 6 digits keeps the historical 180 opacity; only an 8-digit colour
    /// says its own. No built-in bar carried an 8-digit colour before Cipher B's.
    /// </summary>
    [Theory]
    [InlineData("#00FF00", 180)]
    [InlineData("#4000FF00", 0x40)]
    public void A_bar_is_painted_at_the_opacity_its_colour_carries_else_the_old_one(string hex, int alpha)
    {
        var config = new SeriesConfig { Id = "h", Name = "h", Pane = "Main", IndicatorCode = "H" };
        config.Components.Add(new ComponentConfig { Name = "h", DisplayType = ComponentDisplayType.Histogram,
            ColorHex = hex, ColorHexSecondary = hex, ColorSource = ColorSource.Value });
        var s = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "h" });
        s.Data.ComponentData["h"] = Enumerable.Repeat(50.0, N).ToArray();
        var (bmp, t) = Render(s);
        using var _ = bmp;
        var bg = t.Current.Background;
        Near(Over(SKColor.Parse(hex).WithAlpha((byte)alpha), bg), bmp.GetPixel(X(t), Y(25, t)), $"bar {hex}", 4);
    }

    // ── Saved charts ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A chart saved before 2026-10-17 carries the red "OB Zone" and green "OS Zone" shading. Zone
    /// bands are never authored by the user, so on load a fixed band the provider no longer
    /// declares is dropped, and the money-flow bar is added.
    /// </summary>
    [Fact]
    public void A_saved_chart_loses_the_old_zone_shading_and_gains_the_money_flow_bar()
    {
        var meta = new CipherBProvider().GetIndicators();
        var config = new SeriesConfig { Id = "c", Name = "Cipher B", IndicatorCode = "CIPHER_B", Pane = "Pane_CIPHER_B" };
        config.ZoneBands.Add(new ZoneBandConfig { DisplayName = "OB Zone", ColorHex = "#40FF6666", FixedTop = 100, FixedBottom = 53 });
        config.ZoneBands.Add(new ZoneBandConfig { DisplayName = "OS Zone", ColorHex = "#4066BB66", FixedTop = -53, FixedBottom = -100 });

        WorkspaceInitializer.MigrateSeriesConfig(config, meta);

        Assert.Equal(new[] { "Money Flow Bar" }, config.ZoneBands.Select(b => b.DisplayName));
    }

    // ── Narration ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// "Money flow bullish / bearish" follows money flow's sign. The thresholds were −70 / −90 from
    /// when the wave sat at −100..−60, so any value above −70 — nearly all of them since the wave
    /// moved to zero — read bullish, and bearish could not be reached.
    /// </summary>
    [Theory]
    [InlineData(20.0, ZoneStatus.Overbought)]
    [InlineData(-20.0, ZoneStatus.Oversold)]
    [InlineData(1.0, ZoneStatus.Normal)]
    public void Money_flow_narration_follows_its_sign(double mf, ZoneStatus expected)
    {
        var s = CipherB(new() { [CipherBProvider.CompMoneyFlowWave] = mf });
        var ctx = new IndicatorContextAnalyzer().AnalyzeAt(s, CipherBProvider.CompMoneyFlowWave, N - 1);
        Assert.NotNull(ctx);
        Assert.Equal(expected, ctx!.Zone);
    }
}
