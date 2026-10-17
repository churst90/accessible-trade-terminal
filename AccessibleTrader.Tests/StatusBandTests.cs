using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>Where a rendered pane's PLOT starts: below its status band.</summary>
internal static class StatusBandGeometry
{
    /// <summary>
    /// The plot top of <paramref name="pane"/> in pixels, from the band the renderer published:
    /// what every value in the pane is mapped from (2026-10-17). The pane's own top when it has
    /// no band.
    /// </summary>
    public static float PlotTop(IPaneLayoutService layout, string pane, float paneTopPx, int canvasHeight)
        => layout.StatusBands.TryGetValue(pane, out var band) ? paneTopPx + band.HeightFraction * canvasHeight : paneTopPx;
}

/// <summary>
/// <b>Each pane's plot starts below its status lines</b> (Cody, 2026-10-17, a BTCUSDT 1d chart with
/// Cipher B: "address the status line as well the best recommended way that looks good and
/// clean"). The status lines are DOM text over the chart image, ~96% opaque, at the top left of
/// each pane — and they sat ON the plot, so Cipher B's 0–100 RSI and Stochastic, which run near the
/// top of its −100…+100 pane, and the waves' peaks disappeared behind the block. Now every pane
/// keeps a STATUS BAND at its top exactly as tall as the block shown there and maps its values
/// into what is left; a pane that shows no status line keeps nothing.
///
/// <para>
/// The band is ONE computation shared by both painters: the renderer measures it in the DOM's
/// units (<see cref="ChartStatusLine.BandMetrics"/>: the line height grows with the text size, the
/// pad does not), publishes it per pane (<see cref="IPaneLayoutService.StatusBands"/>), and
/// ChartArea gives each pane's block no more lines than its band holds. Everything that maps a
/// value to a pixel or back reads the same band: the renderer's layers and axis, the hover
/// readout, the hit tester and the drawing tools.
/// </para>
/// </summary>
public sealed class StatusBandTests
{
    private const int N = 40;
    private const string CipherPane = "Pane_CIPHER_B";

    private static ThemeService Themes()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings);
    }

    private static StylingService Styling() =>
        new(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());

    private static readonly ChartIdentity Btc = new("Spot", "Bitstamp", "BTCUSDT", "1d");

    private static List<Ohlcv> Bars()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, N).Select(i => new Ohlcv(start.AddDays(i), 100, 106, 99, 105, 1000)).ToList();
    }

    /// <summary>Cipher B exactly as the factory builds it, in its own pane, each component holding one constant value (NaN where not given).</summary>
    private static ChartSeries CipherB(Dictionary<string, double> values)
    {
        var provider = new CipherBProvider();
        var factory = new IndicatorModelFactory(Styling(), new MockIndicatorPreferencesService());
        var s = factory.CreateSeriesFromMetadata(provider.GetIndicators()[0], "Cipher B", CipherPane,
            new List<(string, string)>(), null);
        foreach (var c in s.Components)
            s.Data.ComponentData[c.Name] = Enumerable.Repeat(values.TryGetValue(c.Name, out var v) ? v : double.NaN, N).ToArray();
        return s;
    }

    private static ChartSeries Line(string id, string name, string pane, string hex, double value, bool visible = true)
    {
        var config = new SeriesConfig { Id = id, Name = name, Pane = pane, IndicatorCode = id.ToUpperInvariant() };
        var buffer = new SeriesDataBuffer { SeriesId = id };
        config.Components.Add(new ComponentConfig
        {
            Name = "V", DisplayName = "V", DisplayType = ComponentDisplayType.Line, ColorHex = hex,
            IsVisible = visible, ColorSource = ColorSource.Value, Thickness = 2f,
        });
        buffer.ComponentData["V"] = Enumerable.Repeat(value, N).ToArray();
        return new ChartSeries(config, buffer);
    }

    private static (ChartRenderer Renderer, PaneLayoutService Layout) Renderer()
    {
        var layout = new PaneLayoutService();
        return (new ChartRenderer(Themes(), Styling(), layout, NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>()), layout);
    }

    private static SKBitmap Render(ChartRenderer renderer, int w, int h, IReadOnlyList<ChartSeries> series,
        IReadOnlyDictionary<string, (double Min, double Max)> ranges, float density = 1f, float textScale = 1f, int cursor = N - 1)
    {
        var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, w, h, Bars(), series, cursor, 0, N, (95, 110), ranges,
                density: density, rightMarginBars: 0, identity: Btc, statusTextScale: textScale);
        Dump(bmp, $"{w}x{h}_{series.Count}_{textScale}");
        return bmp;
    }

    /// <summary>Set AT_STATUSBAND_PNG_DIR to keep each render as a PNG for a human to look at.</summary>
    private static void Dump(SKBitmap bmp, string name)
    {
        var dir = Environment.GetEnvironmentVariable("AT_STATUSBAND_PNG_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        using var img = SKImage.FromBitmap(bmp);
        using var png = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(dir, $"statusband_{name}.png"), png.ToArray());
    }

    // Magenta (Cipher B's RSI is #E600E6), including a 1 px line's antialiased edge and the
    // crosshair's translucent halo over it; nothing else on these charts is red AND blue over green.
    private static bool Magentaish(SKColor c) => c.Red - c.Green > 80 && c.Blue - c.Green > 80;

    // ── The plot is below the block ──────────────────────────────────────────

    /// <summary>
    /// <b>Cody's chart.</b> Cipher B's RSI at 95 — 97.5% of the way up its −100…+100 pane, so on
    /// the old layout its line ran through the rows of the pane's status line. Through the real
    /// renderer: not one RSI-coloured pixel in any row of the block, across the whole plot width;
    /// and the line IS drawn, just below it (or the test would pass on a chart that drew nothing).
    /// At 100% text and density 1, and at 175% text on a density-2 bitmap.
    /// </summary>
    [Theory]
    [InlineData(1f, 1f)]
    [InlineData(1.75f, 2f)]
    public void Cipher_Bs_RSI_near_the_top_of_its_pane_is_drawn_below_the_status_line_not_under_it(float textScale, float density)
    {
        int w = (int)(1000 * density), h = (int)(610 * density);
        var cipher = CipherB(new()
        {
            [CipherBProvider.CompMcbRsi] = 95, [CipherBProvider.CompMcbStoch] = 30,
            [CipherBProvider.CompWT1] = 70, [CipherBProvider.CompWT2] = 62,
        });
        var ranges = new Dictionary<string, (double Min, double Max)> { ["Main"] = (95, 110), [CipherPane] = (-100, 100) };
        var (renderer, layout) = Renderer();
        using var bmp = Render(renderer, w, h, new[] { cipher }, ranges, density, textScale);

        float paneTop = layout.Dividers.Single(d => d.BelowPaneName == CipherPane).DividerFraction * h;
        Assert.True(layout.StatusBands.TryGetValue(CipherPane, out var band) && band.Lines == 1,
            $"Cipher B's pane has no one-line status band: {string.Join(", ", layout.StatusBands)}");
        var (line, pad, _) = ChartStatusLine.BandMetrics(density, textScale);
        int blockTop = (int)Math.Floor(paneTop + pad), blockBottom = (int)Math.Ceiling(paneTop + pad + band.Lines * line);
        int plotRight = (int)(w * (1f - layout.AxisWidthFraction)) - 2;

        var under = new List<string>();
        for (int y = blockTop; y < blockBottom; y++)
            for (int x = 1; x < plotRight; x++)
                if (Magentaish(bmp.GetPixel(x, y))) under.Add($"({x},{y})");
        Assert.True(under.Count == 0,
            $"{under.Count} pixels of Cipher B's RSI are inside its status block (rows {blockTop}–{blockBottom}), " +
            $"where the DOM text covers them: {string.Join(" ", under.Take(5))}");

        // Vacuity: the RSI line is drawn — where the plot maps 95, below the block.
        float plotTop = paneTop + band.HeightFraction * h;
        float paneBottom = layout.Dividers.Count > 1 ? layout.Dividers[1].DividerFraction * h : h * (1f - layout.AxisHeightFraction);
        int rsiY = (int)Math.Round(ChartMath.MapY(95, plotTop, paneBottom, -100, 100, false));
        int drawn = 0;
        for (int y = rsiY - (int)(3 * density); y <= rsiY + (int)(3 * density); y++)
            for (int x = 1; x < plotRight; x++)
                if (Magentaish(bmp.GetPixel(x, y))) drawn++;
        Assert.True(drawn > plotRight / 2, $"the RSI line is not drawn at y {rsiY}, below the block: {drawn} pixels");
        Assert.True(rsiY - (int)(2 * density) >= blockBottom, $"the RSI at 95 maps to y {rsiY}, not below the block ending at {blockBottom}");
    }

    // ── The band IS the block ────────────────────────────────────────────────

    /// <summary>
    /// The band each pane keeps is the height of the block ChartArea draws in it, at 100% and at
    /// 175% text, on a density-1 and a density-2 bitmap: pad, the lines at the DOM's line height
    /// (which grows with the text size), pad. The DOM's model, handed the published bands, shows
    /// exactly as many lines in each pane as the band was made for — and a bigger text size makes
    /// a bigger band.
    /// </summary>
    [Theory]
    [InlineData(1.0f, 1f)]
    [InlineData(1.75f, 1f)]
    [InlineData(1.75f, 2f)]
    public void The_band_each_pane_keeps_is_the_block_the_DOM_draws_there(float textScale, float density)
    {
        int w = (int)(1000 * density), h = (int)(610 * density);
        var cipher = CipherB(new() { [CipherBProvider.CompMcbRsi] = 50, [CipherBProvider.CompWT1] = 10 });
        var rsi = Line("rsi", "RSI 14", "Pane_RSI", "#7E57C2", 55);
        var ema = Line("ema", "EMA 20", "Main", "#FFA000", 102);
        var series = new[] { ema, cipher, rsi };
        var ranges = new Dictionary<string, (double Min, double Max)> { ["Main"] = (95, 110), [CipherPane] = (-100, 100), ["Pane_RSI"] = (0, 100) };
        var (renderer, layout) = Renderer();
        Render(renderer, w, h, series, ranges, density, textScale).Dispose();

        var (line, pad, _) = ChartStatusLine.BandMetrics(density, textScale);
        Assert.Equal(new[] { "Main", CipherPane, "Pane_RSI" }.OrderBy(x => x), layout.StatusBands.Keys.OrderBy(x => x));
        foreach (var (pane, band) in layout.StatusBands)
        {
            Assert.True(band.Lines >= 1, $"{pane} shows no status line at {textScale:P0} — the fixture proves nothing");
            Assert.Equal(pad + band.Lines * line + pad, band.HeightFraction * h, 2);
        }
        Assert.Equal(2, layout.StatusBands["Main"].Lines);   // the symbol line and the EMA

        // The DOM side, in CSS px, from what the renderer published.
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Bars()),
            ActiveSeries = System.Collections.Immutable.ImmutableList.Create<ChartSeries>(series),
            Identity = Btc,
        };
        double cssW = w / density, cssH = h / density;
        var placed = ChartStatusLine.BuildPanes(state, layout.Dividers, layout.AxisHeightFraction, layout.AxisWidthFraction,
            N - 1, ChartStatusLine.DefaultBackingAlpha, Themes().Current, cssW, cssH, textScale, layout.StatusBands);
        Assert.Equal(3, placed.Count);
        foreach (var p in placed)
        {
            var band = layout.StatusBands[p.Status.PaneName];
            Assert.Equal(band.Lines, p.Status.Lines.Count);
            // ChartArea: the block at the pane's top + PadPx, each line LinePx × text size; the band
            // leaves PadPx under it.
            double cssBlockBottom = ChartStatusLine.PadPx + p.Status.Lines.Count * ChartStatusLine.LinePx * textScale;
            Assert.Equal(cssBlockBottom + ChartStatusLine.PadPx, band.HeightFraction * cssH, 2);
        }

        if (textScale > 1f)
        {
            var (r100, l100) = Renderer();
            Render(r100, w, h, series, ranges, density, 1f).Dispose();
            Assert.True(layout.StatusBands["Pane_RSI"].HeightFraction > l100.StatusBands["Pane_RSI"].HeightFraction,
                "the band does not grow with the text size");
        }
    }

    /// <summary>
    /// The DOM's line height and font are the model's: ChartArea sizes the block
    /// <c>font-size:0.75rem; line-height:1.4167</c> — 12 px and 17 px at 100% (the root font is
    /// 16 px × the text size) — which is what <see cref="ChartStatusLine.LinePx"/> and
    /// <see cref="ChartStatusLine.FontPx"/> say and what the band is counted in.
    /// </summary>
    [Fact]
    public void The_models_line_height_is_the_line_height_ChartArea_sets()
    {
        Assert.Equal(0.75f * 16f, ChartStatusLine.FontPx, 3);
        Assert.Equal(1.4167f * ChartStatusLine.FontPx, ChartStatusLine.LinePx, 2);
    }

    // ── A pane with no status line keeps its whole height ────────────────────

    /// <summary>
    /// A pane that shows no status line reserves nothing: one whose only plot is hidden (nothing to
    /// read), and — at eight panes, about 30 px each — every pane too short to keep its plot's floor
    /// under a line. Its plot starts at its top, and a line at its maximum is drawn at its top.
    /// </summary>
    [Fact]
    public void A_pane_with_no_status_line_reserves_nothing()
    {
        var hidden = Line("h", "Hidden 5", "Pane_H", "#00FF00", 50, visible: false);
        hidden.Components[0].IsVisible = false;
        var top = Line("top", "Top 1", "Pane_T", "#FF00FF", 100);   // at its pane's maximum
        var ranges = new Dictionary<string, (double Min, double Max)> { ["Main"] = (95, 110), ["Pane_H"] = (0, 100), ["Pane_T"] = (0, 100) };
        var (renderer, layout) = Renderer();
        using (Render(renderer, 600, 600, new[] { hidden, top }, ranges)) { }

        Assert.Equal(new PaneStatusBand(0, 0f), layout.StatusBands["Pane_H"]);
        Assert.True(layout.StatusBands["Pane_T"].Lines == 1, "the control pane shows no status line — the fixture proves nothing");

        // Eight crowded panes on a 300 px canvas: none of them can keep a 30 px plot under a line.
        var crowd = Enumerable.Range(0, 8).Select(i => Line($"p{i}", $"P{i}", $"Pane_{i}", "#FF00FF", 100)).ToList();
        var crowdRanges = Enumerable.Range(0, 8).ToDictionary(i => $"Pane_{i}", _ => (0.0, 100.0));
        crowdRanges["Main"] = (95, 110);
        var (r8, l8) = Renderer();
        using var bmp = Render(r8, 400, 300, crowd, crowdRanges);
        for (int i = 0; i < 8; i++)
            Assert.Equal(new PaneStatusBand(0, 0f), l8.StatusBands[$"Pane_{i}"]);

        // The line at the maximum sits on the pane's top row, as before the band existed.
        int paneTop = (int)Math.Round(l8.Dividers[3].DividerFraction * 300);
        bool atTop = Enumerable.Range(paneTop - 1, 4).Any(y => Magentaish(bmp.GetPixel(100, y)));
        Assert.True(atTop, $"a line at the maximum of a pane with no band is not on its top row (y {paneTop})");

        // And the DOM shows no block where there is no band.
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Bars()),
            ActiveSeries = System.Collections.Immutable.ImmutableList.Create<ChartSeries>(crowd.ToArray()),
        };
        var placed = ChartStatusLine.BuildPanes(state, l8.Dividers, l8.AxisHeightFraction, l8.AxisWidthFraction,
            N - 1, ChartStatusLine.DefaultBackingAlpha, Themes().Current, 400, 300, 1.0, l8.StatusBands);
        Assert.DoesNotContain(placed, p => p.Status.PaneName.StartsWith("Pane_", StringComparison.Ordinal));
    }

    /// <summary>
    /// A short pane gives up LINES rather than its plot, and says so: three indicators in a 100 px
    /// pane used to get three lines (a 59 px block) over a 41 px plot — the three-line floor
    /// overruling the block's 45% cap. Now the plot keeps at least half the pane and 30 px, and the
    /// lines that do not fit are counted on the last one.
    /// </summary>
    [Fact]
    public void A_short_pane_shows_fewer_lines_and_counts_the_rest_rather_than_crush_its_plot()
    {
        float line = ChartStatusLine.LinePx, pad = ChartStatusLine.PadPx;
        Assert.Equal(3, ChartStatusLine.LineBudget(100f, line, pad));   // the old budget: the three-line floor
        int n = ChartStatusLine.PaneLineBudget(100f, line, pad, ChartStatusLine.MinPlotPx);
        Assert.Equal(2, n);
        Assert.True(100f - ChartStatusLine.BandHeight(n, line, pad) >= 50f);

        var three = Enumerable.Range(0, 3).Select(i => Line($"s{i}", $"Ind {(char)('A' + i)}", "Pane_0", "#FFFFFF", i)).ToArray();
        var ctx = new ChartStatusLine.Context(Themes().Current, ChartStatusLine.DefaultBackingAlpha, null, null);
        var lines = ChartStatusLine.Build("Pane_0", three, 3, ctx, 100f, 4000f, line, t => t.Length * 7f).Lines;
        Assert.Equal(2, lines.Count);
        Assert.Equal("+2 more (see the object tree)", lines[^1].Title);
    }

    // ── Pointer and pixel agree ──────────────────────────────────────────────

    /// <summary>
    /// <b>The pointer reads what is drawn under it.</b> The RSI line is found in the rendered
    /// pixels; the pointer is put on it. The hover readout's value is the line's value, a click
    /// there hits the line, and a drawing anchored at that price is placed back on the same row.
    /// At 175% text, where the band is about 34 px: a mapping that ignored it would read about ten
    /// RSI points low and miss the line by more than the grab distance.
    /// </summary>
    [Fact]
    public void The_hover_value_hit_test_and_anchor_row_agree_with_the_drawn_line()
    {
        const int w = 1000, h = 610;
        var rsi = Line("rsi", "RSI 14", "Pane_RSI", "#00FF00", 70);
        var ranges = new Dictionary<string, (double Min, double Max)> { ["Main"] = (95, 110), ["Pane_RSI"] = (0, 100) };
        var (renderer, layout) = Renderer();
        // No keyboard cursor, so no crosshair line is drawn over the RSI line it reads.
        using var bmp = Render(renderer, w, h, new[] { rsi }, ranges, textScale: 1.75f, cursor: -1);
        Assert.True(layout.StatusBands["Pane_RSI"].HeightFraction * h > 25, "the RSI pane has no band to get wrong — the fixture proves nothing");

        // Where the line is drawn: the centre of the green rows in one column.
        int x = 300;
        var rows = Enumerable.Range(0, h).Where(y => bmp.GetPixel(x, y) is var c && c.Green > 200 && c.Red < 60 && c.Blue < 60).ToList();
        Assert.NotEmpty(rows);
        double lineY = rows.Average();

        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Bars()),
            ActiveSeries = System.Collections.Immutable.ImmutableList.Create(rsi),
            ViewportStartIndex = 0, ViewportLength = N, ViewportRange = (95, 110),
            PaneRanges = System.Collections.Immutable.ImmutableDictionary<string, (double Min, double Max)>.Empty
                .Add("Main", (95, 110)).Add("Pane_RSI", (0, 100)),
        };
        var store = Substitute.For<IWorkspaceStore>();
        store.State.Returns(state);
        store.StateStream.Returns(System.Reactive.Linq.Observable.Empty<WorkspaceState>());
        var input = new BlazorInputService();
        using var tracker = new ChartHoverTracker(input, store, paneLayout: layout);
        input.ProcessMouse(x, lineY, "MouseMove", w, h);
        double hovered = double.Parse(tracker.Current!.PriceText!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(Math.Abs(hovered - 70) < 1.0, $"the pointer on the drawn RSI line (y {lineY:F1}) reads {hovered}, not 70");

        var hit = ChartHitTester.HitTest(state, layout.Dividers, layout.AxisHeightFraction, layout.AxisWidthFraction,
            x, lineY, w, h, statusBands: layout.StatusBands);
        Assert.True(hit?.SeriesId == "rsi", $"a click on the drawn RSI line hits {hit?.SeriesId ?? "nothing"}");

        // The drawing tools' pair, in the price pane: a price → the canvas row → the same price.
        double priceY = ChartMath.PriceToCanvasY(108, h, layout.Dividers, layout.AxisHeightFraction, 95, 110, false, layout.StatusBands);
        double back = ChartMath.MapYToPriceInPane(priceY, h, layout.Dividers, layout.AxisHeightFraction, 95, 110, false, layout.StatusBands);
        Assert.Equal(108, back, 6);
        float mainPlotTop = StatusBandGeometry.PlotTop(layout, "Main", 0, h);
        Assert.True(mainPlotTop > 25, "the price pane has no band — the round trip proves nothing");
        Assert.Equal(ChartMath.MapY(108, mainPlotTop, layout.Dividers[0].DividerFraction * h, 95, 110, false), priceY, 2);
    }
}
