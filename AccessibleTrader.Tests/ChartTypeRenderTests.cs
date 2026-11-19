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
/// What the Line and Area chart types put on the canvas: one line through the RAW closes in
/// <see cref="ChartTheme.PriceChartLine"/>, a fill under it for Area and nothing under it for
/// Line, no candle bodies, no white Price-series line on top, and a price axis fitted to the
/// closes rather than to highs and lows.
/// </summary>
public sealed class ChartTypeRenderTests
{
    private const int W = 400, H = 300;

    private static ChartTheme Theme()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings).Current;
    }

    private static ThemeService Themes()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings);
    }

    private static StylingService Styling() =>
        new(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());

    private static List<Ohlcv> Bars(int n, Func<int, (double Open, double Close)> shape)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>(n);
        for (int i = 0; i < n; i++)
        {
            var (o, c) = shape(i);
            bars.Add(new Ohlcv(start.AddHours(i), o, Math.Max(o, c) + 4, Math.Min(o, c) - 4, c, 1000 + i));
        }
        return bars;
    }

    private static RenderContext Ctx(SKCanvas canvas, IReadOnlyList<Ohlcv> data, ChartTheme theme,
        PriceChartStyle style, double min = 80, double max = 125)
    {
        var r = SKRect.Create(0, 0, W, H);
        return new RenderContext(canvas, r, data, 0, data.Count, min, max, false,
            r.Width / data.Count, 1f, "Main", 0, theme) { ChartStyle = style };
    }

    private static SKBitmap Render(Action<SKCanvas> draw)
    {
        var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp)) { canvas.Clear(SKColors.Black); draw(canvas); }
        return bmp;
    }

    private static ChartSeries CandleSeries()
    {
        var config = new SeriesConfig { Id = CoreSeriesIds.Candles, Name = "Candles", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "body", DisplayType = ComponentDisplayType.Candle,
            ColorHex = "#123456", ColorHexSecondary = "#654321",
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id });
    }

    private static ChartSeries PriceSeries(string mapping)
    {
        var config = new SeriesConfig { Id = CoreSeriesIds.Price, Name = "Price", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "line", DisplayName = "Price", IsVisible = true,
            DisplayType = ComponentDisplayType.Line, DataMapping = mapping, ColorHex = "#FFFFFF",
        });
        var data = new SeriesDataBuffer { SeriesId = config.Id };
        return new ChartSeries(config, data);
    }

    private static void Fill(ChartSeries s, IReadOnlyList<Ohlcv> bars)
    {
        foreach (var c in s.Components)
        {
            double[] col = c.DataMapping switch
            {
                "high" => bars.Select(b => b.High).ToArray(),
                "low" => bars.Select(b => b.Low).ToArray(),
                "close" => bars.Select(b => b.Close).ToArray(),
                _ => Array.Empty<double>(),
            };
            s.Data.ComponentData[c.Name] = col;
        }
    }

    private static bool CloseTo(SKColor a, SKColor b, int tolerance = 8) =>
        Math.Abs(a.Red - b.Red) <= tolerance && Math.Abs(a.Green - b.Green) <= tolerance
        && Math.Abs(a.Blue - b.Blue) <= tolerance;

    private static bool HasColourNear(SKBitmap bmp, int x, int y, SKColor colour, int radius = 2)
    {
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            int px = Math.Clamp(x + dx, 0, bmp.Width - 1);
            int py = Math.Clamp(y + dy, 0, bmp.Height - 1);
            if (CloseTo(bmp.GetPixel(px, py), colour)) return true;
        }
        return false;
    }

    private static int XOf(int i, int count) => (int)(i * (W / (float)count) + (W / (float)count) / 2f);

    [Fact]
    public void On_line_a_pixel_on_the_close_line_is_the_chart_type_colour()
    {
        var theme = Theme();
        var bars = Bars(12, i => (100, 100 + i));
        using var bmp = Render(c => StandardRenderers.RenderPriceLine(
            Ctx(c, bars, theme, PriceChartStyle.Line), area: false));

        int i = 6;
        int x = XOf(i, bars.Count);
        int y = (int)ChartMath.MapY(bars[i].Close, 0, H, 80, 125, false);

        Assert.True(HasColourNear(bmp, x, y, theme.PriceChartLine),
            $"no {theme.PriceChartLine} pixel near ({x},{y})");
    }

    [Fact]
    public void On_line_there_is_no_candle_body_between_open_and_close()
    {
        var theme = Theme();
        // A tall body: open 90, close 115. The line is at the close; the old body would fill
        // the whole gap at every bar.
        var bars = Bars(12, _ => (90, 115));
        using var bmp = Render(c => StandardRenderers.RenderPriceLine(
            Ctx(c, bars, theme, PriceChartStyle.Line, min: 80, max: 125), area: false));

        int x = XOf(5, bars.Count);
        int yOpen = (int)ChartMath.MapY(90, 0, H, 80, 125, false);
        Assert.True(CloseTo(bmp.GetPixel(x, yOpen), SKColors.Black),
            $"a candle body was drawn on the Line chart at ({x},{yOpen}): {bmp.GetPixel(x, yOpen)}");
    }

    [Fact]
    public void On_area_the_fill_runs_from_the_line_to_the_pane_floor()
    {
        var theme = Theme();
        var bars = Bars(12, _ => (100, 110));
        using var bmp = Render(c => StandardRenderers.RenderPriceLine(
            Ctx(c, bars, theme, PriceChartStyle.Area, min: 80, max: 125), area: true));

        int x = XOf(5, bars.Count);
        int yLine = (int)ChartMath.MapY(110, 0, H, 80, 125, false);
        int below = Math.Min(H - 1, yLine + 40);
        int above = Math.Max(0, yLine - 40);

        var belowPixel = bmp.GetPixel(x, below);
        var abovePixel = bmp.GetPixel(x, above);

        Assert.False(CloseTo(belowPixel, SKColors.Black),
            $"nothing was filled under the line at ({x},{below}): {belowPixel}");
        Assert.True(CloseTo(abovePixel, SKColors.Black),
            $"the fill leaked above the line at ({x},{above}): {abovePixel}");
    }

    [Fact]
    public void The_layer_draws_the_line_chart_type_as_a_line_and_not_as_candles()
    {
        var theme = Theme();
        var bars = Bars(12, _ => (90, 115));
        var candles = CandleSeries();
        Fill(candles, bars);

        using var bmp = Render(c => new DataLayer(Styling()).Render(
            Ctx(c, bars, theme, PriceChartStyle.Line, min: 80, max: 125), new[] { candles }));

        int x = XOf(5, bars.Count);
        int yClose = (int)ChartMath.MapY(115, 0, H, 80, 125, false);
        int yOpen = (int)ChartMath.MapY(90, 0, H, 80, 125, false);

        Assert.True(HasColourNear(bmp, x, yClose, theme.PriceChartLine),
            $"the Line chart type drew no line at the close near ({x},{yClose})");
        Assert.True(CloseTo(bmp.GetPixel(x, yOpen), SKColors.Black),
            $"a candle body was drawn on the Line chart at ({x},{yOpen}): {bmp.GetPixel(x, yOpen)}");
    }

    [Fact]
    public void On_line_the_white_price_series_is_not_drawn_when_it_maps_the_close()
    {
        var theme = Theme();
        var bars = Bars(12, i => (100, 100 + i));
        var candles = CandleSeries();
        var price = PriceSeries("close");
        Fill(candles, bars);
        Fill(price, bars);

        using var bmp = Render(c => new DataLayer(Styling()).Render(
            Ctx(c, bars, theme, PriceChartStyle.Line), new[] { candles, price }));

        Assert.False(HasWhite(bmp), "the Price series drew its white close line over the Line chart");
    }

    [Fact]
    public void On_line_a_price_series_mapped_to_high_is_still_drawn()
    {
        var theme = Theme();
        var bars = Bars(12, i => (100, 100 + i));
        var candles = CandleSeries();
        var price = PriceSeries("high");
        Fill(candles, bars);
        Fill(price, bars);

        using var bmp = Render(c => new DataLayer(Styling()).Render(
            Ctx(c, bars, theme, PriceChartStyle.Line), new[] { candles, price }));

        Assert.True(HasWhite(bmp), "a Price series mapped to high is a different line and must draw");
    }

    [Fact]
    public void On_an_analytics_chart_with_no_candles_the_Price_line_still_draws_whatever_the_style()
    {
        var bars = Bars(12, i => (100, 100 + i));
        var price = PriceSeries("close");
        Fill(price, bars);

        // The tab kept a Line style across a shape change; there is no candle series here, so
        // the renderer must fall back to drawing the one line the analytics chart has.
        var renderer = new ChartRenderer(Themes(), Styling(), new PaneLayoutService(),
            NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        using var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, W, H, bars, new[] { price }, cursorIndex: -1,
                viewportStart: 0, viewportLength: bars.Count, viewportRange: (80, 125),
                paneRanges: new Dictionary<string, (double Min, double Max)>(), rightMarginBars: 0,
                chartStyle: PriceChartStyle.Line);

        Assert.True(HasWhite(bmp), "the analytics chart's only line vanished");
    }

    private static bool HasWhite(SKBitmap bmp)
    {
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            var c = bmp.GetPixel(x, y);
            if (c.Red > 190 && c.Green > 190 && c.Blue > 190) return true;
        }
        return false;
    }

    // ── Price-axis autoscale ──────────────────────────────────────────────

    private static WorkspaceState RangeState(PriceChartStyle style)
    {
        var bars = Bars(10, i => (100, 110 + i));
        return WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ChartStyle = style,
            ViewportLength = bars.Count,
            ViewportStartIndex = 0,
        };
    }

    [Fact]
    public void Line_and_area_autoscale_from_the_closes_not_the_highs_and_lows()
    {
        var state = RangeState(PriceChartStyle.Line);
        var result = new ViewportRangeCalculator().Calculate(state);

        double closeMin = state.Data.Min(b => (double)b.Close);
        double closeMax = state.Data.Max(b => (double)b.Close);
        double span = closeMax - closeMin;

        Assert.Equal(closeMin - span * 0.05, result.MainRange.Min, 6);
        Assert.Equal(closeMax + span * 0.05, result.MainRange.Max, 6);
    }

    [Fact]
    public void Candles_autoscale_from_highs_and_lows_as_before()
    {
        var state = RangeState(PriceChartStyle.Candles);
        var result = new ViewportRangeCalculator().Calculate(state);

        double low = state.Data.Min(b => (double)b.Low);
        double high = state.Data.Max(b => (double)b.High);
        double span = high - low;

        Assert.Equal(low - span * 0.05, result.MainRange.Min, 6);
        Assert.Equal(high + span * 0.05, result.MainRange.Max, 6);
    }
}
