using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Theming;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// What the first screenshots of the rendered chart showed (2026-09-18), pinned as tests.
///
/// <para>
/// The A2i campaign left the rendering layer the best-covered in the repo and nobody had looked
/// at it. Nine states of the real app were photographed through the browser harness the next
/// day, and three things were wrong in almost every picture: y-axis labels drawn on top of each
/// other, the crosshair's value badge drawn on top of a gridline label, and the x axis repeating
/// the last bar's date across the empty space to the right of it when zoomed in. The legend
/// also spilled out of its pane at eight panes.
/// </para>
///
/// <para>
/// Each of these was invisible to the suite because the suite asked "did a label reach the
/// strip?" and never "can it be read?". The tests here ask the second question in pixels: a
/// run of text rows in an axis strip is never taller than one line of type.
/// </para>
/// </summary>
public sealed class AxisAndLegendCollisionTests
{
    private const int W = 400, H = 300;
    private const float AxisFont = 12f;   // every built-in theme's AxisFontSize

    private static ThemeService Themes()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings);
    }

    private static List<Ohlcv> Bars(int n, Func<int, (double Open, double Close)> shape)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>(n);
        for (int i = 0; i < n; i++)
        {
            var (o, c) = shape(i);
            bars.Add(new Ohlcv(start.AddHours(i), o, Math.Max(o, c) + 1, Math.Min(o, c) - 1, c, 1000 + i));
        }
        return bars;
    }

    private static ChartSeries IndicatorSeries(string id, string pane, double[] values)
    {
        var config = new SeriesConfig { Id = id, Name = id, Pane = pane, IndicatorCode = id };
        config.Components.Add(new ComponentConfig { Name = id, DisplayType = ComponentDisplayType.Line, ColorHex = "#00FFFF" });
        var buffer = new SeriesDataBuffer { SeriesId = id };
        buffer.ComponentData[id] = values;
        return new ChartSeries(config, buffer);
    }

    private static (ChartRenderer Renderer, PaneLayoutService Layout) Renderer()
    {
        var layout = new PaneLayoutService();
        var styling = new StylingService(new ComponentRoleMapper(),
            new Core.Services.Audio.SonificationProfileProvider(), new PaneAssignmentService());
        return (new ChartRenderer(Themes(), styling, layout,
            NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>()), layout);
    }

    private static SKBitmap RenderFrame(ChartRenderer renderer, IReadOnlyList<ChartSeries> series,
        IReadOnlyList<Ohlcv> data, IReadOnlyDictionary<string, (double Min, double Max)> paneRanges,
        (double Min, double Max) mainRange, bool isLogScale = false, int? viewportLength = null,
        int? cursorIndex = null)
    {
        var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, W, H, data, series, cursorIndex: cursorIndex ?? data.Count - 1,
                viewportStart: 0, viewportLength: viewportLength ?? data.Count,
                viewportRange: mainRange, paneRanges: paneRanges, isLogScale: isLogScale, rightMarginBars: 0);
        return bmp;
    }

    /// <summary>Axis type is near-white on every theme; nothing else in the strip is.</summary>
    private static bool IsTextPixel(SKColor c) => c.Red > 170 && c.Green > 170 && c.Blue > 170;

    /// <summary>
    /// The heights of every vertical run of rows holding text, scanning a column band of the
    /// bitmap. Two labels drawn over each other merge into one run taller than a line of type.
    /// The scan starts past the swatch ticks on the strip's left edge, which are coloured, not
    /// white, but are excluded anyway so the test is about type.
    /// </summary>
    private static List<(int Top, int Height)> TextBands(SKBitmap bmp, int left, int right, int top, int bottom)
    {
        var bands = new List<(int, int)>();
        int runStart = -1;
        for (int y = top; y <= bottom; y++)
        {
            bool text = false;
            if (y < bottom)
                for (int x = left; x < right && !text; x++)
                    text = IsTextPixel(bmp.GetPixel(x, y));
            if (text && runStart < 0) runStart = y;
            if (!text && runStart >= 0) { bands.Add((runStart, y - runStart)); runStart = -1; }
        }
        return bands;
    }

    // ── The y axis: one label per line of type ─────────────────────────────

    /// <summary>
    /// Eight indicator panes on a 300px canvas, cursor on the last bar, log scale on the price
    /// pane. In the photographs this layout drew "120.00" over "115.00" at the top of the price
    /// pane and "90.00" over "60.00" at the top of a short oscillator pane — the spacing check
    /// compared the labels' RAW positions, and then clamped the top label DOWN into the pane so
    /// it would not clip, straight onto the label below it. And in every indicator pane the
    /// crosshair's value badge was painted over whichever gridline label was nearest, because
    /// the axis was drawn first and never told where the badge would go.
    /// </summary>
    [Fact]
    public void NoTwoYAxisLabelsShareTheSameRows_EvenAtEightPanesOnLogScale()
    {
        var (renderer, layout) = Renderer();
        var data = Bars(30, i => (100 + i % 4, 103 + i % 4));
        var series = new List<ChartSeries> { IndicatorSeries("candles", "Main", Array.Empty<double>()) };
        var ranges = new Dictionary<string, (double Min, double Max)> { ["Main"] = (88.9, 120.9) };
        for (int i = 0; i < 8; i++)
        {
            // Cursor value sits just under the pane's top label so the badge lands ON it.
            series.Add(IndicatorSeries($"i{i}", $"Pane_{i}", Enumerable.Repeat(97.0 - i, 30).ToArray()));
            ranges[$"Pane_{i}"] = (0, 100);
        }

        using var bmp = RenderFrame(renderer, series, data, ranges, (88.9, 120.9), isLogScale: true);

        int axisLeft = (int)((1f - layout.AxisWidthFraction) * W);
        int axisBottom = (int)((1f - layout.AxisHeightFraction) * H);
        var bands = TextBands(bmp, axisLeft + 8, W, 0, axisBottom);

        Assert.True(bands.Count >= 6, $"only {bands.Count} text bands in the y-axis strip — the labels did not reach it");
        foreach (var (top, height) in bands)
            Assert.True(height <= AxisFont,
                $"a run of text rows {height}px tall starts at y={top} in the y-axis strip; one line of "
                + $"{AxisFont}px type is at most {AxisFont}px, so two labels are drawn over each other there");
    }

    /// <summary>
    /// The same property with the price pane alone and tall, on both scales, so a regression in
    /// the ordinary one-pane chart is named as such rather than hiding behind the eight-pane case.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoTwoYAxisLabelsShareTheSameRows_OnTheBarePricePane(bool isLog)
    {
        var (renderer, layout) = Renderer();
        var data = Bars(30, i => (100 + i % 4, 103 + i % 4));
        var series = new[] { IndicatorSeries("candles", "Main", Array.Empty<double>()) };
        var ranges = new Dictionary<string, (double Min, double Max)> { ["Main"] = (88.9, 120.9) };

        using var bmp = RenderFrame(renderer, series, data, ranges, (88.9, 120.9), isLogScale: isLog);

        int axisLeft = (int)((1f - layout.AxisWidthFraction) * W);
        int axisBottom = (int)((1f - layout.AxisHeightFraction) * H);
        var bands = TextBands(bmp, axisLeft + 8, W, 0, axisBottom);

        Assert.True(bands.Count >= 3, $"only {bands.Count} text bands in the y-axis strip");
        foreach (var (top, height) in bands)
            Assert.True(height <= AxisFont, $"overlapping labels: a {height}px run of text at y={top}");
    }

    // ── The x axis: no label for a bar that is not there ───────────────────

    /// <summary>
    /// Zoomed in past the last bar, the viewport is mostly empty space to the right of the data.
    /// The axis laid its labels at fixed fractions of the strip and clamped each one's bar index
    /// to the last bar, so the empty region read "07/19 07/19 07/19". A date on the axis is a
    /// claim that a bar sits above it. Where there is no bar there is no label.
    /// </summary>
    [Fact]
    public void TheXAxisLabelsNothingToTheRightOfTheLastBar()
    {
        var (renderer, layout) = Renderer();
        var data = Bars(20, i => (100 + i % 4, 103 + i % 4));
        var series = new[] { IndicatorSeries("candles", "Main", Array.Empty<double>()) };
        var ranges = new Dictionary<string, (double Min, double Max)> { ["Main"] = (95, 115) };

        // Sixty slots for twenty bars: the right two thirds of the plot hold no data.
        using var bmp = RenderFrame(renderer, series, data, ranges, (95, 115), viewportLength: 60);

        int plotRight = (int)((1f - layout.AxisWidthFraction) * W);
        int axisTop = (int)((1f - layout.AxisHeightFraction) * H);
        int lastBarRight = (int)Math.Ceiling(plotRight * (20.0 / 60.0));

        int textLeftOfData = 0, textRightOfData = 0;
        for (int y = axisTop + 1; y < H; y++)
            for (int x = 0; x < plotRight; x++)
                if (IsTextPixel(bmp.GetPixel(x, y)))
                {
                    // A label's ink can start at its tick and run right, so allow one label's
                    // width past the last bar before calling it "over the empty region".
                    if (x < lastBarRight + 45) textLeftOfData++; else textRightOfData++;
                }

        Assert.True(textLeftOfData > 20, "the x axis drew no labels under the data at all");
        Assert.Equal(0, textRightOfData);
    }

    // ── The legend: never taller than its pane ─────────────────────────────

    /// <summary>
    /// The row budget had a floor of three rows regardless of pane height, on the argument that a
    /// cramped key beats none. At eight panes a pane is about 55px and three rows plus padding is
    /// 63px, so the legend ran out of the bottom of its own pane and across the next pane's
    /// divider — the photograph shows "Signal" cut in half by the ADX pane's top edge. A key
    /// that covers the thing it is a key to is not a key.
    /// </summary>
    [Theory]
    [InlineData(20f)]
    [InlineData(40f)]
    [InlineData(55f)]
    [InlineData(120f)]
    public void TheLegendFitsInsideItsPane_HoweverShortThePane(float paneHeight)
    {
        const float Line = 17f, Pad = 6f;
        var s = new ChartSeries();
        s.Config.Name = "MACD";
        s.Components.Add(new ComponentConfig { Name = "Histogram", DisplayType = ComponentDisplayType.Histogram, ColorHex = "#00FF00" });
        s.Components.Add(new ComponentConfig { Name = "Macd", DisplayType = ComponentDisplayType.Line, ColorHex = "#00FFFF" });
        s.Components.Add(new ComponentConfig { Name = "Signal", DisplayType = ComponentDisplayType.Line, ColorHex = "#FFAA00" });

        var rows = ChartRenderer.BuildLegendRows(new List<ChartSeries> { s }, paneHeight, Line, Pad);

        // A pane that cannot hold a single row (29px) gets no legend at all; every taller one
        // gets one that fits.
        if (paneHeight >= Pad + Line + Pad) Assert.NotEmpty(rows);
        if (rows.Count == 0) return;
        float boxHeight = Pad + rows.Count * Line + Pad;
        Assert.True(boxHeight <= paneHeight,
            $"{rows.Count} rows make a {boxHeight}px legend inside a {paneHeight}px pane");
    }

    /// <summary>
    /// When only one row fits, that row still names something — the first entry, with the count
    /// of what it stands in for — rather than being spent entirely on "+3 more".
    /// </summary>
    [Fact]
    public void ASingleRowLegendNamesTheFirstEntryAndTheRest()
    {
        const float Line = 17f, Pad = 6f;
        var s = new ChartSeries();
        s.Config.Name = "MACD";
        s.Components.Add(new ComponentConfig { Name = "Macd", DisplayType = ComponentDisplayType.Line, ColorHex = "#00FFFF" });
        s.Components.Add(new ComponentConfig { Name = "Signal", DisplayType = ComponentDisplayType.Line, ColorHex = "#FFAA00" });
        s.Components.Add(new ComponentConfig { Name = "Histogram", DisplayType = ComponentDisplayType.Histogram, ColorHex = "#00FF00" });

        var rows = ChartRenderer.BuildLegendRows(new List<ChartSeries> { s }, 40f, Line, Pad);

        var row = Assert.Single(rows);
        Assert.False(row.Label.StartsWith('+'), $"the only row names nothing: '{row.Label}'");
        Assert.Contains("+2 more", row.Label);
        Assert.DoesNotContain("  ", row.Label);
    }
}
