using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A background tint is a component property, not a name the renderer knows (2026-10-17).</b>
///
/// <para>Cody: <i>"anchor polarity... shouldn't this, if tinting is all it does, be a property in
/// the indicator properties? which means shading/tinting should be something any indicator can
/// do"</i>. Until then <c>ChartRenderer</c> found two tints by component name — Cipher B's
/// "Anchor Polarity" and Top/Bottom Detector's "Distribution Confidence", both on the price chart
/// — and that evening's stopgap gated the first on the component being shown. Now every tint is a
/// declaration (<see cref="IndicatorComponentMetadata.DefaultBackgroundTint"/>) the user can
/// change per chart in Properties, painted by <see cref="BackgroundTintPainter"/>.</para>
///
/// <para>Every frame here is the REAL <see cref="ChartRenderer"/> drawing series the REAL factory
/// built from the provider's own metadata, and every "tinted" pixel is checked against Skia's own
/// composite of the declared colour over the same pixel of a reference frame whose tint is off —
/// so the assertion is exact without knowing the theme, the gradient or the layout.</para>
/// </summary>
public sealed class BackgroundTintTests
{
    private const int W = 400, H = 500, N = 40;

    private static ThemeService Themes()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings);
    }

    private static StylingService Styling() =>
        new(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());

    private static IndicatorModelFactory Factory() => new(Styling(), new MockIndicatorPreferencesService());

    /// <summary>Candles between about 91 and 101 on a 90–130 price axis, so the upper half of the
    /// price pane is open background in every column.</summary>
    private static List<Ohlcv> Bars()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, N).Select(i =>
        {
            double o = 96 + 3 * Math.Sin(i / 3.0), c = 96 + 3 * Math.Sin((i + 1) / 3.0);
            return new Ohlcv(start.AddHours(i), o, Math.Max(o, c) + 1, Math.Min(o, c) - 1, c, 1000 + i);
        }).ToList();
    }

    private static ChartSeries Candles()
    {
        var config = new SeriesConfig { Id = "price", Name = "price", IndicatorCode = "CANDLES", Pane = "Main" };
        config.Components.Add(new ComponentConfig { Name = "body", DisplayType = ComponentDisplayType.Candle, ColorHex = "#26A69A", ColorHexSecondary = "#EF5350" });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = "price" });
    }

    /// <summary>A series exactly as the factory builds it from the provider, in its declared pane,
    /// every component NaN except the ones given.</summary>
    private static ChartSeries FromProvider(IIndicatorProvider provider, string code, Dictionary<string, double[]> values)
    {
        var meta = provider.GetIndicators().Single(m => m.Code == code);
        var s = Factory().CreateSeriesFromMetadata(meta, meta.Name, meta.DefaultPane, new List<(string, string)>(), null);
        foreach (var c in s.Components)
            s.Data.ComponentData[c.Name] = values.TryGetValue(c.Name, out var v) ? v : Enumerable.Repeat(double.NaN, N).ToArray();
        return s;
    }

    /// <summary>+1 and -1 in runs of five bars; never 0, the midline.</summary>
    private static readonly double[] Polarity = Enumerable.Range(0, N).Select(i => (i / 5) % 2 == 0 ? 1.0 : -1.0).ToArray();
    private static readonly double[] AllNaN = Enumerable.Repeat(double.NaN, N).ToArray();

    /// <summary>Cipher B with Anchor Polarity's data. Anchor Polarity is left out by default
    /// (2026-10-23); <paramref name="included"/> includes it under Parts and hides its line with H,
    /// as a user who wants only its tint does.</summary>
    private static ChartSeries CipherB(double[] polarity, BackgroundTint? userTint = null, bool included = true)
    {
        var s = FromProvider(new CipherBProvider(), "CIPHER_B", new() { [CipherBProvider.CompAnchorPolarity] = polarity });
        s.Data.ComponentData[CipherBProvider.CompAnchorPolarity] = polarity;
        if (!included) return s;
        var order = new CipherBProvider().GetIndicators()[0].Components.Select(c => c.Name).ToList();
        Assert.True(ComponentInclusion.SetIncluded(s.Config, CipherBProvider.CompAnchorPolarity, true, order));
        var anchor = s.Components.Single(c => c.Name == CipherBProvider.CompAnchorPolarity);
        anchor.IsVisible = false;
        anchor.UserBackgroundTint = userTint;
        return s;
    }

    /// <summary>Distribution Confidence across both sides of 0.5, with NaN gaps, an exact 0.5 and
    /// a value past 1.0 (the ramp's clamp).</summary>
    private static readonly double[] Distribution = Enumerable.Range(0, N).Select(i =>
        i % 9 == 4 ? double.NaN : i == 20 ? 0.5 : i == 21 ? 0.4999 : i * 1.3 / N).ToArray();

    private static ChartSeries Tbd(BackgroundTint? userTint = null)
    {
        var s = FromProvider(new TopBottomDetectorProvider(), TopBottomDetectorProvider.Code,
            new() { [TopBottomDetectorProvider.CompDistribution] = Distribution });
        s.Components.Single(c => c.Name == TopBottomDetectorProvider.CompDistribution).UserBackgroundTint = userTint;
        return s;
    }

    private static readonly double[] Histogram = Enumerable.Range(0, N).Select(i => (i / 4) % 2 == 0 ? 1.0 : -1.0).ToArray();

    private static ChartSeries Macd(BackgroundTint? histogramTint = null)
    {
        var s = FromProvider(new SkenderZeroCrossProvider(), "Macd", new() { ["Histogram"] = Histogram });
        s.Components.Single(c => c.Name == "Histogram").UserBackgroundTint = histogramTint;
        return s;
    }

    private sealed record Frame(SKBitmap Bmp, float PlotWidth, float PlotBottom, IReadOnlyList<(string Pane, float Top)> Panes,
        IReadOnlyDictionary<string, float> Bands) : IDisposable
    {
        public void Dispose() => Bmp.Dispose();

        /// <summary>The whole pane, status band included: the price pane is everything above the
        /// first divider.</summary>
        private (int Top, int Bottom) Whole(string pane)
        {
            if (pane == "Main") return (0, (int)(Panes.Count > 0 ? Panes[0].Top : PlotBottom));
            int i = Panes.ToList().FindIndex(p => p.Pane == pane);
            Assert.True(i >= 0, $"no pane named {pane} in this frame");
            float bottom = i + 1 < Panes.Count ? Panes[i + 1].Top : PlotBottom;
            return ((int)Math.Ceiling(Panes[i].Top), (int)bottom);
        }

        /// <summary>The pane's status band: the rows above its plot that its status lines sit in.</summary>
        public (int Top, int Bottom) Band(string pane)
        {
            var (top, _) = Whole(pane);
            return (top, top + (int)Math.Floor(Bands.TryGetValue(pane, out var b) ? b : 0f));
        }

        /// <summary>The pane's PLOT: below its status band, which a tint never paints.</summary>
        public (int Top, int Bottom) Rows(string pane)
        {
            var (top, bottom) = Whole(pane);
            return (top + (int)Math.Ceiling(Bands.TryGetValue(pane, out var b) ? b : 0f), bottom);
        }

        /// <summary>The pixel columns strictly inside bar <paramref name="bar"/>'s slot.</summary>
        public (int X0, int X1) Columns(int bar)
        {
            float bw = PlotWidth / N;
            return ((int)Math.Ceiling(bar * bw) + 1, (int)Math.Floor((bar + 1) * bw) - 1);
        }
    }

    private static Frame Render(params ChartSeries[] series)
    {
        var themes = Themes();
        var layout = new PaneLayoutService();
        var renderer = new ChartRenderer(themes, Styling(), layout, NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, W, H, Bars(), series, cursorIndex: -1, viewportStart: 0, viewportLength: N,
                viewportRange: (90, 130),
                paneRanges: new Dictionary<string, (double, double)>
                {
                    ["Pane_TBD"] = (0, 1.3), ["Pane_CIPHER_B"] = (-100, 100), ["Pane_Macd"] = (-10, 10),
                },
                rightMarginBars: 0,
                // A symbol, so the price pane has a status line and therefore a status band.
                identity: new ChartIdentity("Spot", "Bitstamp", "BTCUSDT", "1d"));
        return new Frame(bmp, W - themes.Current.AxisWidth, H - themes.Current.AxisHeight,
            layout.Dividers.Select(d => (d.BelowPaneName, d.DividerFraction * H)).ToList(),
            layout.StatusBands.ToDictionary(kv => kv.Key, kv => kv.Value.HeightFraction * H));
    }

    /// <summary>What Skia paints for <paramref name="tint"/> over <paramref name="under"/> — the
    /// renderer's own operation (a non-antialiased fill), so the expectation is exact.</summary>
    private static SKColor Over(SKColor tint, SKColor under)
    {
        using var px = new SKBitmap(1, 1);
        using (var c = new SKCanvas(px))
        {
            c.Clear(under);
            using var paint = new SKPaint { Color = tint, Style = SKPaintStyle.Fill, IsAntialias = false };
            c.DrawRect(new SKRect(0, 0, 1, 1), paint);
        }
        return px.GetPixel(0, 0);
    }

    /// <summary>
    /// The share of bar <paramref name="bar"/>'s pixels in rows <paramref name="rows"/> that are
    /// exactly <paramref name="tint"/> over the reference frame. Not 100%: a gridline or a level
    /// drawn over the tint is a pixel the tint is under, not one it is missing from.
    /// </summary>
    private static double TintedShare(Frame tinted, Frame reference, int bar, (int Top, int Bottom) rows, SKColor tint)
    {
        var (x0, x1) = tinted.Columns(bar);
        int hit = 0, all = 0;
        for (int x = x0; x <= x1; x++)
        for (int y = rows.Top; y < rows.Bottom; y++)
        {
            all++;
            if (tinted.Bmp.GetPixel(x, y) == Over(tint, reference.Bmp.GetPixel(x, y))) hit++;
        }
        Assert.True(all > 50, $"bar {bar} has only {all} pixels in rows {rows} — the region is wrong, not the tint");
        return (double)hit / all;
    }

    /// <summary>Every pixel of the rows in the plot is the reference frame's.</summary>
    private static void Untouched(Frame frame, Frame reference, (int Top, int Bottom) rows, string what)
    {
        int differing = 0;
        for (int y = rows.Top; y < rows.Bottom; y++)
        for (int x = 0; x < (int)frame.PlotWidth; x++)
            if (frame.Bmp.GetPixel(x, y) != reference.Bmp.GetPixel(x, y)) differing++;
        Assert.True(differing == 0, $"{what}: {differing} pixels differ from the frame with no tint.");
    }

    /// <summary>
    /// A tint is painted in the PLOT, like the data — never in the status band above it, where the
    /// pane's status lines are read (2026-10-17: each pane's plot starts below its status-line
    /// block). The band must exist for this to mean anything.
    /// </summary>
    private static void StatusBandIsClear(Frame f, Frame reference, string pane)
    {
        var band = f.Band(pane);
        Assert.True(band.Bottom - band.Top >= 8, $"{pane} has a {band.Bottom - band.Top}px status band — the check is vacuous");
        Untouched(f, reference, band, pane + " status band");
    }

    /// <summary>The upper part of the price pane, where no candle reaches (prices 130 down to ~112).</summary>
    private static (int Top, int Bottom) OpenPriceRows(Frame f)
    {
        var (top, bottom) = f.Rows("Main");
        return (top + 4, top + (int)((bottom - top) * 0.40));
    }

    /// <summary>The middle third of an indicator pane, clear of Cipher B's OB/OS zone bands.</summary>
    private static (int Top, int Bottom) MiddleRows(Frame f, string pane)
    {
        var (top, bottom) = f.Rows(pane);
        return (top + (int)((bottom - top) * 0.36), top + (int)((bottom - top) * 0.64));
    }

    private static readonly SKColor AnchorBull = SKColor.Parse("#1626A69A"), AnchorBear = SKColor.Parse("#16EF5350");

    // ── Cipher C: a timing window tints from its companion ──────────────────────────

    /// <summary>
    /// Cipher C's Daily Timing Window holds the days since the last low (1 to 40 here), which is what is heard,
    /// and tints the price chart from its <see cref="ComponentCompanions.Tint"/> companion: green on
    /// bars 20 to 29, inside the window, and nothing elsewhere, once switched on (it is off by
    /// default, as the original). Tinted by its own value instead, every bar from day 1 would be green.
    /// </summary>
    [Fact]
    public void Cipher_C_tints_only_the_bars_inside_its_timing_window()
    {
        var count = Enumerable.Range(1, N).Select(i => (double)i).ToArray();
        var inside = Enumerable.Range(0, N).Select(i => i is >= 20 and <= 29 ? 1.0 : 0.0).ToArray();
        var series = FromProvider(new CipherCProvider(), CipherCProvider.Code, new() { [CipherCProvider.CompDailyWindow] = count });
        series.Data.ComponentData[CipherCProvider.CompDailyWindow + ComponentCompanions.Tint] = inside;
        var window = series.Components.Single(c => c.Name == CipherCProvider.CompDailyWindow);
        // Off by default since the original's look (d82c20a6); the user switches it on.
        Assert.Equal(BackgroundTint.Off, window.BackgroundTintInEffect());
        window.UserBackgroundTint = BackgroundTint.PriceChart;
        Assert.Same(series.Data.ComponentData[CipherCProvider.CompDailyWindow + ComponentCompanions.Tint], BackgroundTintPainter.TintSource(series, window));

        using var f = Render(Candles(), series);
        using var reference = Render(Candles(), FromProvider(new CipherCProvider(), CipherCProvider.Code, new()));
        var rows = OpenPriceRows(f);
        var green = SKColor.Parse("#184CAF50");   // the original's window green (d82c20a6)
        foreach (int bar in new[] { 20, 25, 29 })
            Assert.True(TintedShare(f, reference, bar, rows, green) > 0.9, $"bar {bar}, inside the window, is not tinted");
        foreach (int bar in new[] { 0, 10, 19, 30, 39 })
            Assert.True(TintedShare(f, reference, bar, rows, green) < 0.01, $"bar {bar}, outside the window, is tinted");
        StatusBandIsClear(f, reference, "Main");
    }

    // ── A Window component: shaded where open, its count never drawn, its picture drawn ─

    private static readonly SKColor WindowGreen = SKColor.Parse("#3000E676");

    /// <summary>A <see cref="ComponentDisplayType.Window"/> on the price chart, declared inline as
    /// any plugin would; values at 115, inside the open rows, so a line through them would show.</summary>
    private static ChartSeries Window(double[] values, double[]? path = null, BackgroundTint tint = BackgroundTint.PriceChart,
        bool? drawPath = null)
    {
        var config = new SeriesConfig { Id = "win", Name = "win", IndicatorCode = "WIN", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "Season", DisplayName = "Season", DisplayType = ComponentDisplayType.Window, IsVisible = true,
            ColorHex = "#D4A72C", Thickness = 2, BackgroundTint = tint, TintColorAboveHex = "#3000E676",
            PathName = "arc", UserDrawPath = drawPath,
        });
        var s = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "win" });
        s.Data.ComponentData["Season"] = values;
        if (path != null) s.Data.ComponentData["Season" + ComponentCompanions.Path] = path;
        return s;
    }

    private static readonly double[] OpenTenToNineteen = Enumerable.Range(0, N).Select(i => i is >= 10 and <= 19 ? 115.0 : double.NaN).ToArray();

    private static int Differing(Frame frame, Frame reference, (int Top, int Bottom) rows)
    {
        int n = 0;
        for (int y = rows.Top; y < rows.Bottom; y++)
        for (int x = 0; x < (int)frame.PlotWidth; x++)
            if (frame.Bmp.GetPixel(x, y) != reference.Bmp.GetPixel(x, y)) n++;
        return n;
    }

    /// <summary>
    /// With no tint companion, a window is shaded on the bars it has a value on, evenly, in its
    /// tint colour — and on no other bar.
    /// </summary>
    [Fact]
    public void A_window_is_shaded_where_it_has_a_value()
    {
        using var f = Render(Candles(), Window(OpenTenToNineteen));
        using var reference = Render(Candles(), Window(AllNaN));
        var rows = OpenPriceRows(f);
        foreach (int bar in new[] { 10, 15, 19 })
            Assert.True(TintedShare(f, reference, bar, rows, WindowGreen) > 0.9, $"bar {bar}, inside the window, is not shaded");
        foreach (int bar in new[] { 0, 9, 20, 39 })
            Assert.True(TintedShare(f, reference, bar, rows, WindowGreen) < 0.01, $"bar {bar}, outside the window, is shaded");
        StatusBandIsClear(f, reference, "Main");
    }

    /// <summary>A window's value is a count to hear, not a position: with its shading off it draws nothing.</summary>
    [Fact]
    public void A_window_draws_no_line_through_its_count()
    {
        using var f = Render(Candles(), Window(OpenTenToNineteen, tint: BackgroundTint.Off));
        using var reference = Render(Candles(), Window(AllNaN, tint: BackgroundTint.Off));
        Untouched(f, reference, f.Rows("Main"), "price pane");
    }

    /// <summary>
    /// The window's picture (<see cref="ComponentCompanions.Path"/>) is drawn at its prices, and
    /// "Draw the arc" off in Appearance takes it away.
    /// </summary>
    [Fact]
    public void A_windows_drawing_is_drawn_and_can_be_switched_off()
    {
        var path = Enumerable.Range(0, N).Select(i => 120.0 + 5 * Math.Sin(i / 6.0)).ToArray();
        using var reference = Render(Candles(), Window(AllNaN, tint: BackgroundTint.Off));

        using (var drawn = Render(Candles(), Window(AllNaN, path, BackgroundTint.Off)))
        {
            int changed = Differing(drawn, reference, OpenPriceRows(drawn));
            Assert.True(changed > (int)drawn.PlotWidth, $"only {changed} pixels changed: the drawing is not across the chart");
        }

        using var off = Render(Candles(), Window(AllNaN, path, BackgroundTint.Off, drawPath: false));
        Untouched(off, reference, off.Rows("Main"), "price pane with the drawing switched off");
    }

    // ── Cipher B: Anchor Polarity ───────────────────────────────────────────────────

    /// <summary>
    /// Like Market Cipher B, nothing is painted behind the price chart by default — and nothing
    /// behind Cipher B's own pane either. The reference has no polarity data at all, so any tint
    /// shows up as a difference.
    /// </summary>
    [Fact]
    public void Cipher_B_tints_nothing_by_default()
    {
        var series = CipherB(Polarity, included: false);
        // Left out by default, still computed (strategies read it); and its own default tint is Off,
        // so including it changes nothing either.
        var anchor = series.Config.ExcludedComponents.Single(c => c.Name == CipherBProvider.CompAnchorPolarity);
        Assert.Equal(BackgroundTint.Off, anchor.BackgroundTintInEffect());

        using var f = Render(Candles(), series);
        using var reference = Render(Candles(), CipherB(AllNaN, included: false));
        Untouched(f, reference, f.Rows("Main"), "price pane");
        Untouched(f, reference, f.Rows("Pane_CIPHER_B"), "Cipher B pane");
    }

    /// <summary>
    /// Set to Price chart, Anchor Polarity shades the price chart by its sign in the colours the
    /// old hardcoded tint used — WHILE THE LINE STAYS HIDDEN. The stopgap tied the tint to the
    /// line's visibility; the tint is its own switch now.
    /// </summary>
    [Fact]
    public void Anchor_polarity_set_to_price_chart_shades_the_price_chart_by_its_sign_with_the_line_still_hidden()
    {
        var series = CipherB(Polarity, BackgroundTint.PriceChart);
        Assert.False(series.Components.Single(c => c.Name == CipherBProvider.CompAnchorPolarity).IsVisible);

        using var f = Render(Candles(), series);
        using var reference = Render(Candles(), CipherB(Polarity));
        var rows = OpenPriceRows(f);
        foreach (int bar in new[] { 2, 7, 12, 17, 33, 37 })
        {
            var want = Polarity[bar] > 0 ? AnchorBull : AnchorBear;
            double share = TintedShare(f, reference, bar, rows, want);
            Assert.True(share > 0.8, $"bar {bar} (polarity {Polarity[bar]}): only {share:P0} of the price pane is {want} over the background");
        }
        Untouched(f, reference, f.Rows("Pane_CIPHER_B"), "Cipher B pane (the tint went to the price chart)");
        StatusBandIsClear(f, reference, "Main");
    }

    /// <summary>Set to This pane, the shading moves to Cipher B's own pane and the price chart is clean.</summary>
    [Fact]
    public void Anchor_polarity_set_to_this_pane_shades_cipher_b_pane_and_not_the_price_chart()
    {
        using var f = Render(Candles(), CipherB(Polarity, BackgroundTint.ThisPane));
        using var reference = Render(Candles(), CipherB(Polarity));
        Untouched(f, reference, f.Rows("Main"), "price pane");
        StatusBandIsClear(f, reference, "Pane_CIPHER_B");
        var rows = MiddleRows(f, "Pane_CIPHER_B");
        foreach (int bar in new[] { 2, 7, 12, 17 })
        {
            var want = Polarity[bar] > 0 ? AnchorBull : AnchorBear;
            double share = TintedShare(f, reference, bar, rows, want);
            Assert.True(share > 0.8, $"bar {bar}: only {share:P0} of the Cipher B pane is {want} over the background");
        }
    }

    // ── Top/Bottom Detector: Distribution Confidence ────────────────────────────────

    /// <summary>
    /// The look Top/Bottom Detector has had since 2026-04-27, now from its declaration: soft red
    /// on the price chart behind every bar at 0.5 and over, at 20% of 0x20 at 0.5 rising to all
    /// of it at 1.0 (and clamped past it), nothing below 0.5 or on a NaN bar, nothing in its own
    /// pane. The alpha below is the old <c>RenderTbdDistributionTint</c>'s formula written out as
    /// the specification. (The refactor was also checked frame for frame: four whole frames
    /// rendered before and after were byte-identical.)
    /// </summary>
    [Fact]
    public void Top_bottom_detector_shades_the_price_chart_exactly_as_it_always_has()
    {
        var series = Tbd();
        Assert.Equal(BackgroundTint.PriceChart,
            series.Components.Single(c => c.Name == TopBottomDetectorProvider.CompDistribution).BackgroundTintInEffect());

        using var f = Render(Candles(), series);
        using var reference = Render(Candles(), Tbd(BackgroundTint.Off));
        var rows = OpenPriceRows(f);
        var (top, bottom) = f.Rows("Main");
        int tintedBars = 0;
        for (int bar = 0; bar < N; bar++)
        {
            double d = Distribution[bar];
            if (double.IsNaN(d) || d < 0.5)
            {
                var (x0, x1) = f.Columns(bar);
                for (int x = x0; x <= x1; x++)
                for (int y = top; y < bottom; y++)
                    Assert.True(f.Bmp.GetPixel(x, y) == reference.Bmp.GetPixel(x, y),
                        $"bar {bar} (distribution {d}) is shaded at ({x},{y}); nothing below 0.5 is.");
                continue;
            }
            double scale = Math.Clamp((d - 0.5) / (1.0 - 0.5), 0.0, 1.0);
            var want = new SKColor(0xEF, 0x53, 0x50, (byte)(32 * (0.2 + 0.8 * scale)));
            double share = TintedShare(f, reference, bar, rows, want);
            Assert.True(share > 0.8, $"bar {bar} (distribution {d:F3}): only {share:P0} of the price pane is {want} over the background");
            tintedBars++;
        }
        Assert.True(tintedBars >= 15, $"only {tintedBars} bars were at or above 0.5 — the fixture no longer tests the ramp");
        Untouched(f, reference, f.Rows("Pane_TBD"), "Top/Bottom Detector pane");
        StatusBandIsClear(f, reference, "Main");
    }

    /// <summary>A hidden SERIES paints nothing, tint or no tint (the old code's rule too).</summary>
    [Fact]
    public void A_hidden_series_tints_nothing()
    {
        var hidden = Tbd();
        hidden.Config.IsVisible = false;
        var hiddenOff = Tbd(BackgroundTint.Off);
        hiddenOff.Config.IsVisible = false;
        using var f = Render(Candles(), hidden);
        using var reference = Render(Candles(), hiddenOff);
        Untouched(f, reference, f.Rows("Main"), "price pane under a hidden Top/Bottom Detector");
    }

    // ── Any indicator ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Any component can be given a tint. MACD declares none; its histogram set to This pane
    /// shades the MACD pane in the histogram's own two colours (at the borrowed 0x16 strength):
    /// teal where it is at or above its ColorBaseline of 0, red below. The price chart is clean.
    /// </summary>
    [Fact]
    public void Any_component_can_be_given_a_tint_here_the_macd_histogram_in_its_own_pane()
    {
        var macd = Macd();
        Assert.Equal(BackgroundTint.Off, macd.Components.Single(c => c.Name == "Histogram").BackgroundTintInEffect());

        using var f = Render(Candles(), Macd(BackgroundTint.ThisPane));
        using var reference = Render(Candles(), macd);
        Untouched(f, reference, f.Rows("Main"), "price pane");
        var (top, bottom) = f.Rows("Pane_Macd");
        var rows = (top + 4, top + (int)((bottom - top) * 0.30));   // above the ±1 histogram bars
        foreach (int bar in new[] { 1, 5, 9, 14, 18 })
        {
            var want = (Histogram[bar] >= 0 ? SKColor.Parse("#26A69A") : SKColor.Parse("#EF5350"))
                .WithAlpha(BackgroundTintPainter.BorrowedColourAlpha);
            double share = TintedShare(f, reference, bar, rows, want);
            Assert.True(share > 0.8, $"bar {bar} (histogram {Histogram[bar]}): only {share:P0} of the MACD pane is {want} over the background");
        }
    }

    // ── Restore ─────────────────────────────────────────────────────────────────────

    private static ChartSeries Restore(SeriesConfig saved, IIndicatorProvider provider, string code)
    {
        var meta = provider.GetIndicators().Single(m => m.Code == code);
        var providers = new List<IIndicatorProvider> { provider };
        var engine = new IndicatorEngine(
            new IndicatorService(providers, NullLogger<IndicatorService>.Instance),
            new CustomIndicatorRegistry(), providers);
        // Through the save format first: the workspace file is Newtonsoft JSON.
        var fromDisk = JsonConvert.DeserializeObject<SeriesConfig>(JsonConvert.SerializeObject(saved))!;
        return SeriesManagementService.MaterializeSaved(fromDisk, meta, Factory(),
            Array.Empty<IReadOnlyDictionary<string, object>>(), engine, new MockIndicatorPreferencesService());
    }

    /// <summary>
    /// A chart whose user never chose a tint comes back following the indicator, whatever the
    /// file says the indicator's default used to be: this save carries a stale
    /// <c>"BackgroundTint": 0</c> (Off) and no user choice — the shape of a save made before a
    /// provider changed its default, and (minus the stale field) of every save from before tints
    /// were declarations. Top/Bottom Detector's Distribution Confidence tints the price chart.
    /// </summary>
    [Fact]
    public void A_saved_chart_whose_user_never_chose_a_tint_follows_the_indicator()
    {
        var oldSave = JsonConvert.DeserializeObject<SeriesConfig>(
            "{\"Id\":\"tbd-1\",\"Pane\":\"Pane_TBD\"," +
            "\"Components\":[{\"Name\":\"Distribution Confidence\",\"IsVisible\":true,\"BackgroundTint\":0}]}")!;
        oldSave.IndicatorCode = TopBottomDetectorProvider.Code;
        Assert.Null(oldSave.Components.Single().UserBackgroundTint);

        var restored = Restore(oldSave, new TopBottomDetectorProvider(), TopBottomDetectorProvider.Code);
        var dist = restored.Components.Single(c => c.Name == TopBottomDetectorProvider.CompDistribution);
        Assert.Equal(BackgroundTint.PriceChart, dist.BackgroundTintInEffect());
    }

    /// <summary>The user's choice comes back as made — Off on a component whose indicator tints,
    /// Price chart on one whose indicator does not. Anchor Polarity, hidden by default when this
    /// was saved and left out by default since 2026-10-23, stays included: its tint is the user's
    /// and a left-out part tints nothing (ProviderDefaultVisibility.HiddenDefaultIsNowLeftOut).</summary>
    [Fact]
    public void A_users_tint_choice_survives_a_restore()
    {
        var tbd = new SeriesConfig { Id = "tbd-2", IndicatorCode = TopBottomDetectorProvider.Code, Pane = "Pane_TBD" };
        tbd.Components.Add(new ComponentConfig { Name = TopBottomDetectorProvider.CompDistribution, BackgroundTint = BackgroundTint.PriceChart, UserBackgroundTint = BackgroundTint.Off });
        var restoredTbd = Restore(tbd, new TopBottomDetectorProvider(), TopBottomDetectorProvider.Code);
        Assert.Equal(BackgroundTint.Off,
            restoredTbd.Components.Single(c => c.Name == TopBottomDetectorProvider.CompDistribution).BackgroundTintInEffect());

        var cb = new SeriesConfig { Id = "cb-1", IndicatorCode = "CIPHER_B", Pane = "Pane_CIPHER_B" };
        cb.Components.Add(new ComponentConfig { Name = CipherBProvider.CompAnchorPolarity, IsVisible = false, UserBackgroundTint = BackgroundTint.PriceChart });
        var restoredCb = Restore(cb, new CipherBProvider(), "CIPHER_B");
        var anchor = restoredCb.Components.Single(c => c.Name == CipherBProvider.CompAnchorPolarity);
        Assert.Equal(BackgroundTint.PriceChart, anchor.BackgroundTintInEffect());
        Assert.Equal(BackgroundTint.Off, anchor.BackgroundTint);   // the indicator's own default is untouched
    }
}
