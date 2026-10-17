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

        // Sixty slots for twenty bars: the right two thirds of the plot hold no data. The cursor
        // is on bar 5, not the last: since 2026-10-17 the cursor's date is a label on this axis
        // centred on its bar (TradingView's model), and on the LAST bar half of it rightly hangs
        // past the bar it names. This test is about the tick labels, which name slots.
        using var bmp = RenderFrame(renderer, series, data, ranges, (95, 115), viewportLength: 60, cursorIndex: 5);

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

    // ── The status lines: never taller than their pane ──────────────────────

    // The boxed legend these two tests were written for was retired on 2026-10-17 (Cody's
    // decision: TradingView status line). The property they pin carries over to the status lines
    // unchanged, so they are ported, not dropped: the block is budgeted by ChartStatusLine.Build.

    private static ChartSeries StatusSeries(string name, params (string Comp, ComponentDisplayType Type, double Value)[] comps)
    {
        var s = new ChartSeries();
        s.Config.Name = name;
        foreach (var (comp, type, value) in comps)
        {
            s.Components.Add(new ComponentConfig { Name = comp, DisplayName = comp, DisplayType = type, ColorHex = "#00FFFF" });
            s.Data.ComponentData[comp] = Enumerable.Repeat(value, 10).ToArray();
        }
        return s;
    }

    private static PaneStatus StatusIn(float paneHeight, params ChartSeries[] series) =>
        ChartStatusLine.Build("Pane_0", series, 5,
            new ChartStatusLine.Context(new ThemeService(Substitute.For<ISettingsManager>()).Current, ChartStatusLine.DefaultBackingAlpha, null, null),
            paneHeight, 2000f, ChartStatusLine.LinePx, t => t.Length * 7f);

    /// <summary>
    /// The row budget had a floor of three rows regardless of pane height, on the argument that a
    /// cramped key beats none. At eight panes a pane is about 55px and three rows plus padding is
    /// 63px, so the legend ran out of the bottom of its own pane and across the next pane's
    /// divider — the photograph shows "Signal" cut in half by the ADX pane's top edge. A key
    /// that covers the thing it is a key to is not a key. Same rule for the status lines.
    /// </summary>
    [Theory]
    [InlineData(20f)]
    [InlineData(40f)]
    [InlineData(55f)]
    [InlineData(120f)]
    public void TheStatusBlockFitsInsideItsPane_HoweverShortThePane(float paneHeight)
    {
        float line = ChartStatusLine.LinePx, pad = ChartStatusLine.PadPx;
        var lines = StatusIn(paneHeight,
            StatusSeries("MACD", ("Histogram", ComponentDisplayType.Histogram, 1), ("Macd", ComponentDisplayType.Line, 2)),
            StatusSeries("RSI", ("RSI", ComponentDisplayType.Line, 50)),
            StatusSeries("CCI", ("CCI", ComponentDisplayType.Line, 10)),
            StatusSeries("ADX", ("ADX", ComponentDisplayType.Line, 20))).Lines;

        // A pane that cannot hold a single line (25px) gets none at all; every taller one gets
        // a block that fits.
        if (paneHeight >= pad + line + pad) Assert.NotEmpty(lines);
        if (lines.Count == 0) return;
        float blockHeight = pad + lines.Count * line + pad;
        Assert.True(blockHeight <= paneHeight,
            $"{lines.Count} lines make a {blockHeight}px block inside a {paneHeight}px pane");
    }

    /// <summary>
    /// When only one line fits, that line still names something — the first indicator, with the
    /// count of what it stands in for — rather than being spent entirely on "+2 more".
    /// </summary>
    [Fact]
    public void ASingleLineStatusBlockNamesTheFirstIndicatorAndTheRest()
    {
        var lines = StatusIn(40f,
            StatusSeries("MACD", ("Macd", ComponentDisplayType.Line, 2)),
            StatusSeries("RSI", ("RSI", ComponentDisplayType.Line, 50)),
            StatusSeries("CCI", ("CCI", ComponentDisplayType.Line, 10))).Lines;

        var only = Assert.Single(lines);
        Assert.Equal("MACD", only.Title);
        Assert.Contains("+2 more", only.Note);
    }

    // ── The y axis: a label also has to fit the strip it is drawn in ─────────

    /// <summary>
    /// <b>Every other check in this file compares labels with each OTHER. This one compares a
    /// label with the EDGE it is drawn at,</b> which is the measurement the axis never had.
    ///
    /// <para>
    /// Visible in the site screenshots either side of v2.12.0, so not a regression — just never
    /// looked for: on a TSLA daily the volume pane's gridline labels ran to and past the right
    /// edge of the canvas while the price pane's "440.00" stopped five pixels short. The strip
    /// is a fixed width and nine digits do not fit in it, and nine-digit volume is the COMMON
    /// case for a US large cap, not an outlier. The 2.11.0 axis pass measured labels against
    /// each other and against the crosshair badge; nothing measured them against the canvas.
    /// </para>
    ///
    /// <para>
    /// The pane range here is a real one — Tesla trades around 120 million shares a day.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(140_000_000.0, "nine-digit US large-cap share volume")]
    [InlineData(3_200_000_000.0, "ten-digit volume, an index or a penny stock")]
    [InlineData(1_400.0, "an ordinary four-digit range, which must be untouched")]
    public void NoYAxisLabelRunsPastTheRightEdgeOfTheCanvas(double paneMax, string what)
    {
        var (renderer, layout) = Renderer();
        var data = Bars(30, i => (100 + i % 4, 103 + i % 4));
        var series = new List<ChartSeries>
        {
            IndicatorSeries("candles", "Main", Array.Empty<double>()),
            IndicatorSeries("vol", "Pane_0", Enumerable.Repeat(paneMax * 0.8, 30).ToArray()),
        };
        var ranges = new Dictionary<string, (double Min, double Max)>
        {
            ["Main"] = (95.0, 110.0),
            ["Pane_0"] = (0, paneMax),
        };

        using var bmp = RenderFrame(renderer, series, data, ranges, (95.0, 110.0));

        int axisLeft = (int)((1f - layout.AxisWidthFraction) * W);
        int axisBottom = (int)((1f - layout.AxisHeightFraction) * H);

        // The rightmost column holding type anywhere in the strip. A label that overflows is
        // CLIPPED by the canvas, so the symptom is type running into the final columns.
        int rightmost = -1;
        for (int x = axisLeft; x < W; x++)
            for (int y = 0; y < axisBottom; y++)
                if (IsTextPixel(bmp.GetPixel(x, y))) { rightmost = Math.Max(rightmost, x); break; }

        Assert.True(rightmost >= 0, $"no axis type rendered at all for {what} — the test proves nothing");
        Assert.True(rightmost < W - 1,
            $"axis type reaches x={rightmost} on a {W}px canvas for {what}: the label is running off "
          + "the right edge and the digits past it are simply not shown. Either the strip has to "
          + "hold the number or the number has to be said shorter.");
    }
}
