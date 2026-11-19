using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// What Percent and Indexed to 100 put on the price pane: labels and gridlines in scale units,
/// placed at the price each reading marks, with the pixel mapping unchanged. The COMPARE
/// overlay is rebased to its own first visible value and the range holds the rebased line.
/// Regular and Log are the controls: their labels must not move.
/// </summary>
public sealed class PriceScaleRenderTests
{
    private const int W = 400, H = 300;

    private static ThemeService Themes()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings);
    }

    private static StylingService Styling() =>
        new(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());

    private static List<Ohlcv> Bars(int n = 20, double first = 100, double step = 0.5)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>(n);
        for (int i = 0; i < n; i++)
        {
            double c = first + i * step;
            bars.Add(new Ohlcv(start.AddHours(i), c, c + 1, c - 1, c, 1000 + i));
        }
        return bars;
    }

    private static ChartSeries CandleSeries()
    {
        var config = new SeriesConfig { Id = CoreSeriesIds.Candles, Name = "Candles", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "body", DisplayType = ComponentDisplayType.Candle,
            ColorHex = "#123456", ColorHexSecondary = "#654321", IsVisible = true,
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id });
    }

    private static ChartSeries LineSeries(string id, string name, string indicatorCode, string component,
        double[] values, string colour)
    {
        var config = new SeriesConfig { Id = id, Name = name, Pane = "Main", IndicatorCode = indicatorCode };
        config.Components.Add(new ComponentConfig
        {
            Name = component, DisplayName = component, DisplayType = ComponentDisplayType.Line,
            IsVisible = true, IsDrawn = true, ColorHex = colour,
        });
        var buffer = new SeriesDataBuffer { SeriesId = id };
        buffer.ComponentData[component] = values;
        return new ChartSeries(config, buffer);
    }

    private static (ChartRenderer Renderer, IReadOnlyList<Ohlcv> Bars, IReadOnlyList<ChartSeries> Series) Draw(
        PriceScaleMode mode, bool log = false, int cursor = -1, IReadOnlyList<ChartSeries>? extra = null,
        (double Min, double Max)? range = null)
    {
        var bars = Bars();
        var series = new List<ChartSeries> { CandleSeries() };
        if (extra != null) series.AddRange(extra);
        var theme = Themes();
        var renderer = new ChartRenderer(theme, Styling(), new PaneLayoutService(),
            NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        using var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, W, H, bars, series, cursorIndex: cursor,
                viewportStart: 0, viewportLength: bars.Count,
                viewportRange: range ?? (90, 115),
                paneRanges: new Dictionary<string, (double Min, double Max)>(),
                isLogScale: log, rightMarginBars: 0, chartStyle: PriceChartStyle.Candles,
                priceScaleMode: mode);
        return (renderer, bars, series);
    }

    private static ChartRenderer.AxisLabelTick Label(ChartRenderer r, string text) =>
        r.LastMainAxisLabels.Single(t => t.Text == text);

    // ── Axis labels in scale units ────────────────────────────────────────

    [Fact]
    public void Percent_labels_are_percent_strings_at_the_price_each_marks()
    {
        var (r, _, _) = Draw(PriceScaleMode.Percent);
        var (axis, min, max, log, _) = r.LastMainScale;

        var five = Label(r, "5.00%");
        Assert.Equal(105, five.Price, 6);
        Assert.Equal(ChartMath.MapY(105, axis.Top, axis.Bottom, min, max, log), five.Y, 3);
        Assert.Equal(5, five.Reading, 6);
        Assert.Contains(r.LastMainAxisLabels, t => t.Text == "0.00%");
    }

    [Fact]
    public void Indexed_labels_are_index_numbers_at_the_price_each_marks()
    {
        var (r, _, _) = Draw(PriceScaleMode.IndexedTo100);

        var five = Label(r, "105.00");
        Assert.Equal(105, five.Price, 6);
        Assert.Equal(105, five.Reading, 6);
    }

    [Fact]
    public void The_crosshair_badge_and_the_value_labels_read_scale_units()
    {
        // Bar 10 opens at 105 on the base of bar 0's close of 100.
        var overlay = LineSeries("ma", "Average", "SMA", "line",
            Enumerable.Repeat(107.5, 20).ToArray(), "#FFA726");
        var (r, _, _) = Draw(PriceScaleMode.Percent, cursor: 10, extra: new[] { overlay });

        Assert.NotNull(r.LastMainBadge);
        Assert.Equal("5.00%", r.LastMainBadge!.Value.Text);
        Assert.Equal(105, r.LastMainBadge.Value.Value, 6);   // still the raw close, for placement
        Assert.Contains(r.LastMainScale.Labels, l => l.Text == "7.50%");
    }

    [Fact]
    public void Regular_and_log_labels_are_the_old_strings()
    {
        var regular = Draw(PriceScaleMode.Regular).Renderer;
        Assert.Equal(new[] { "90", "95", "100", "105", "110", "115" },
            regular.LastMainAxisLabels.Select(t => t.Text));

        // Log changes where they are drawn, never what they say.
        var log = Draw(PriceScaleMode.Regular, log: true).Renderer;
        Assert.Equal(new[] { "90", "95", "100", "105", "110", "115" },
            log.LastMainAxisLabels.Select(t => t.Text));
    }

    [Fact]
    public void A_negative_percent_label_keeps_the_regular_axis_minus_sign()
    {
        var (r, _, _) = Draw(PriceScaleMode.Percent, range: (90, 115));

        Assert.Contains(r.LastMainAxisLabels, t => t.Text == "-5.00%");
    }

    // ── Gridlines share the label step, in scale units ────────────────────

    [Theory]
    [InlineData(PriceScaleMode.Percent, 100.0)]
    [InlineData(PriceScaleMode.IndexedTo100, 100.0)]
    [InlineData(PriceScaleMode.Percent, 93_000.5)]
    [InlineData(PriceScaleMode.IndexedTo100, 93_000.5)]
    public void Every_labelled_tick_on_a_scale_axis_is_a_whole_step_on_the_grid(PriceScaleMode mode, double baseValue)
    {
        const double min = 90, max = 115;
        var (lo, hi) = ChartMath.ScaleBounds(mode, min, max, baseValue);
        double range = hi - lo;
        double grid = ChartMath.GridStep(range);
        double label = ChartMath.LabelStep(range, 5, grid);
        Assert.True(label >= grid);

        // The renderer and the grid both walk ScaleTicks; this asserts that walk's labels land
        // on grid multiples, which is the shared-step invariant in the new units.
        foreach (var (reading, _) in ChartMath.ScaleTicks(mode, min, max, baseValue, label))
            Assert.True(ChartMath.IsOnLabel(reading, label), $"{reading} is not a label multiple of {label}");
        Assert.Contains(ChartMath.ScaleTicks(mode, min, max, baseValue, grid),
            t => Math.Abs(t.Reading / label - Math.Round(t.Reading / label)) < 1e-9);
    }

    [Fact]
    public void The_major_gridline_is_drawn_on_the_row_the_percent_label_marks()
    {
        // Base 50, pane 55–70: the percent extent is 10–40, so the grid steps by 5 PERCENT and
        // the 15% line sits at 57.5. A grid still stepping in prices would step by 2 and put no
        // line at 57.5 at all — which is the sabotage this test exists for.
        var theme = Themes();
        var bars = Bars(n: 30, first: 50, step: 1);
        var series = new List<ChartSeries> { CandleSeries() };
        var renderer = new ChartRenderer(theme, Styling(), new PaneLayoutService(),
            NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        using var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Black);
            renderer.Render(canvas, W, H, bars, series, cursorIndex: -1, viewportStart: 0,
                viewportLength: bars.Count, viewportRange: (55, 70),
                paneRanges: new Dictionary<string, (double Min, double Max)>(),
                rightMarginBars: 0, priceScaleMode: PriceScaleMode.Percent);
        }

        var tick = Label(renderer, "15.00%");
        Assert.Equal(57.5, tick.Price, 6);
        var major = Blend(theme.Current.GridLine, theme.Current.Background, 160);
        bool found = false;
        for (int dy = -1; dy <= 1 && !found; dy++)
        {
            int y = (int)Math.Round(tick.Y) + dy;
            if (y < 0 || y >= H) continue;
            for (int x = 2; x < W - 70 && !found; x++)
                found = CloseTo(bmp.GetPixel(x, y), major, 4);
        }
        Assert.True(found, $"no major gridline pixel on the row {tick.Y} labelled 15.00%");
    }

    private static SKColor Blend(SKColor fore, SKColor back, byte alpha)
    {
        float a = alpha / 255f;
        return new SKColor(
            (byte)(fore.Red * a + back.Red * (1 - a)),
            (byte)(fore.Green * a + back.Green * (1 - a)),
            (byte)(fore.Blue * a + back.Blue * (1 - a)));
    }

    private static bool CloseTo(SKColor a, SKColor b, int tolerance) =>
        Math.Abs(a.Red - b.Red) <= tolerance && Math.Abs(a.Green - b.Green) <= tolerance
        && Math.Abs(a.Blue - b.Blue) <= tolerance;

    // ── The COMPARE overlay ───────────────────────────────────────────────

    [Fact]
    public void Percent_rebases_the_compare_overlay_to_its_OWN_first_visible_value()
    {
        // The chart starts at 100; the comparison starts at 200 and ends at 240. Rebased it
        // starts at 100 and ends at 120 — the chart's own prices at those bars.
        var compare = LineSeries("compare", "Compare symbol", "COMPARE", "Compare",
            new[] { 200.0, 240.0 }, "#4DD0E1");
        var bars = Bars(n: 2, first: 100, step: 10);
        using var scratch = new SKBitmap(10, 10);
        using var canvas = new SKCanvas(scratch);
        var ctx = new RenderContext(canvas, SKRect.Create(0, 0, 100, 100),
            bars, 0, 2, 90, 130, false, 50, 1f, "Main", -1,
            Themes().Current) { ScaleMode = PriceScaleMode.Percent, ScaleBase = 100 };

        var copy = DataLayer.RebaseCompareForDrawing(compare, ctx);

        Assert.NotNull(copy);
        Assert.Equal(new[] { 100.0, 120.0 }, copy!.GetComponentData("Compare"), new DoubleComparer());
        Assert.Equal(new[] { 200.0, 240.0 }, compare.GetComponentData("Compare"), new DoubleComparer());
    }

    [Fact]
    public void Regular_draws_the_compare_overlay_where_it_did_before()
    {
        var compare = LineSeries("compare", "Compare symbol", "COMPARE", "Compare",
            new[] { 200.0, 240.0 }, "#4DD0E1");
        var bars = Bars(n: 2, first: 100, step: 10);
        using var scratch = new SKBitmap(10, 10);
        using var canvas = new SKCanvas(scratch);
        var ctx = new RenderContext(canvas, SKRect.Create(0, 0, 100, 100),
            bars, 0, 2, 90, 260, false, 50, 1f, "Main", -1,
            Themes().Current) { ScaleMode = PriceScaleMode.Regular, ScaleBase = 100 };

        Assert.Null(DataLayer.RebaseCompareForDrawing(compare, ctx));
    }

    [Fact]
    public void Percent_draws_the_rebased_compare_line_at_the_chart_price_it_maps_to()
    {
        var bars = Bars(n: 2, first: 100, step: 10);
        var compare = LineSeries("compare", "Compare symbol", "COMPARE", "Compare",
            new[] { 200.0, 240.0 }, "#4DD0E1");
        var theme = Themes();
        using var bmp = new SKBitmap(W, H);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        var rect = SKRect.Create(0, 0, W, H);
        var ctx = new RenderContext(canvas, rect, bars, 0, 2, 90, 130, false,
            rect.Width / 2f, 1f, "Main", -1, theme.Current)
        { ScaleMode = PriceScaleMode.Percent, ScaleBase = 100 };

        new DataLayer(Styling()).Render(ctx, new[] { compare });

        int x1 = (int)(0 * ctx.ItemWidth + ctx.ItemWidth / 2);
        float chartFirstY = ChartMath.MapY(100, rect.Top, rect.Bottom, 90, 130, false);
        float rebasedLastY = ChartMath.MapY(120, rect.Top, rect.Bottom, 90, 130, false);
        Assert.True(HasColourNear(bmp, x1, (int)chartFirstY, SKColor.Parse("#4DD0E1")),
            "the comparison did not start at the chart's first close");
        Assert.True(HasColourNear(bmp, (int)(ctx.ItemWidth + ctx.ItemWidth / 2), (int)rebasedLastY,
                SKColor.Parse("#4DD0E1")),
            "the comparison was not drawn at the rebased price 120");
        Assert.False(HasColourNear(bmp, (int)(ctx.ItemWidth + ctx.ItemWidth / 2),
                (int)ChartMath.MapY(240, rect.Top, rect.Bottom, 90, 130, false),
                SKColor.Parse("#4DD0E1")),
            "the comparison was still drawn at its raw 240");
    }

    private static bool HasColourNear(SKBitmap bmp, int x, int y, SKColor colour, int radius = 2)
    {
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            int px = Math.Clamp(x + dx, 0, bmp.Width - 1);
            int py = Math.Clamp(y + dy, 0, bmp.Height - 1);
            if (CloseTo(bmp.GetPixel(px, py), colour, 12)) return true;
        }
        return false;
    }

    private sealed class DoubleComparer : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) < 1e-9;
        public int GetHashCode(double obj) => obj.GetHashCode();
    }
}
