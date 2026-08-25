using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// Phase F test-debt: the void SKCanvas renderers in StandardRenderers are hard to
/// assert on directly, so these tests take a behavioural approach against a REAL
/// SKSurface/SKBitmap:
///   (a) robustness — the renderers must not throw on empty, single-bar, NaN-laced,
///       log-scale, or short-viewport data (they run on every frame, so a throw is a
///       hard crash of the whole chart);
///   (b) mode observability — enabling an accessibility mode (hollow-up candles,
///       color-vision-safe palette) must actually change the rendered pixels, proving
///       the toggle reaches the draw path rather than silently no-op'ing.
///
/// Assertions stay behavioural (drew something / drew something DIFFERENT), never exact
/// colours, so they don't turn into pixel-diff brittleness on Skia version bumps.
/// </summary>
public class StandardRenderersSmokeTests
{
    private const int W = 200, H = 200;

    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static ChartTheme DefaultTheme()
    {
        // Real ThemeService with a substitute settings source → both accessibility
        // overrides OFF by default (mirrors VisualAccessibilityTests' fixture).
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings).Current;
    }

    private static List<Ohlcv> MakeBars(int n, bool alternating = true)
    {
        var bars = new List<Ohlcv>(n);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < n; i++)
        {
            // Alternating up/down candles so both bullish and bearish branches run.
            double open = 100 + (i % 5);
            double close = alternating
                ? (i % 2 == 0 ? open + 2 : open - 2)
                : open + 2;
            double high = Math.Max(open, close) + 1;
            double low = Math.Min(open, close) - 1;
            bars.Add(new Ohlcv(start.AddMinutes(i), open, high, low, close, 1000 + i));
        }
        return bars;
    }

    private static ChartSeries CandleSeries(int barCount)
    {
        var config = new SeriesConfig { Id = "price", Name = "price", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "body",
            DisplayType = ComponentDisplayType.Candle,
            ColorHex = "#00FF00",
            ColorHexSecondary = "#FF0000",
        });
        config.Components.Add(new ComponentConfig
        {
            Name = "upper_wick",
            DisplayType = ComponentDisplayType.Wick,
            ColorHex = "#888888",
        });
        // Candle series pulls OHLC from ctx.Data, not the buffer.
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = "price" });
    }

    private static ChartSeries LineSeries(string comp, double[] data, string pane = "sub")
    {
        var config = new SeriesConfig { Id = comp, Name = comp, Pane = pane };
        config.Components.Add(new ComponentConfig { Name = comp, DisplayType = ComponentDisplayType.Line, ColorHex = "#00FFFF" });
        var buffer = new SeriesDataBuffer { SeriesId = comp };
        buffer.ComponentData[comp] = data;
        return new ChartSeries(config, buffer);
    }

    private static RenderContext Ctx(
        SKCanvas canvas, IReadOnlyList<Ohlcv> data, ChartTheme theme,
        int viewportStart = 0, int? viewportLength = null,
        double min = 90, double max = 115, bool log = false)
    {
        int vlen = viewportLength ?? Math.Max(1, data.Count);
        return new RenderContext(
            Canvas: canvas,
            PaneRect: SKRect.Create(0, 0, W, H),
            Data: data,
            ViewportStart: viewportStart,
            ViewportLength: vlen,
            Min: min,
            Max: max,
            IsLogScale: log,
            ItemWidth: (float)W / vlen,
            Density: 1f,
            PaneName: "Main",
            LocalCursorIndex: 0,
            Theme: theme);
    }

    /// <summary>Renders into a fresh bitmap and returns it (caller disposes).</summary>
    private static SKBitmap Render(Action<SKCanvas> draw)
    {
        var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Black);
            draw(canvas);
        }
        return bmp;
    }

    private static int NonBlackPixelCount(SKBitmap bmp)
    {
        int count = 0;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
                if (bmp.GetPixel(x, y) != SKColors.Black) count++;
        return count;
    }

    private static bool PixelsDiffer(SKBitmap a, SKBitmap b)
    {
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y)) return true;
        return false;
    }

    // ── RenderCandles: robustness ───────────────────────────────────────────

    [Fact]
    public void RenderCandles_NormalData_DrawsSomething()
    {
        var theme = DefaultTheme();
        var bars = MakeBars(20);
        var series = CandleSeries(20);
        using var bmp = Render(c =>
            StandardRenderers.RenderCandles(Ctx(c, bars, theme), series, new SKPaint()));
        Assert.True(NonBlackPixelCount(bmp) > 0, "candles should paint pixels");
    }

    [Fact]
    public void RenderCandles_EmptyData_DoesNotThrow_DrawsNothing()
    {
        var theme = DefaultTheme();
        var series = CandleSeries(0);
        using var bmp = Render(c =>
            StandardRenderers.RenderCandles(Ctx(c, new List<Ohlcv>(), theme, viewportLength: 1), series, new SKPaint()));
        Assert.Equal(0, NonBlackPixelCount(bmp));
    }

    [Fact]
    public void RenderCandles_SingleBar_DoesNotThrow()
    {
        var theme = DefaultTheme();
        var bars = MakeBars(1);
        var series = CandleSeries(1);
        using var bmp = Render(c =>
            StandardRenderers.RenderCandles(Ctx(c, bars, theme), series, new SKPaint()));
        Assert.True(NonBlackPixelCount(bmp) > 0);
    }

    [Fact]
    public void RenderCandles_NaNLacedData_DoesNotThrow()
    {
        var theme = DefaultTheme();
        var bars = MakeBars(10);
        // Poison a couple of bars with NaN OHLC — MapY guards should absorb it.
        bars[3] = new Ohlcv(bars[3].Date, double.NaN, double.NaN, double.NaN, double.NaN, 0);
        bars[7] = new Ohlcv(bars[7].Date, 100, double.NaN, 99, double.NaN, 0);
        var series = CandleSeries(10);
        var ex = Record.Exception(() =>
        {
            using var bmp = Render(c =>
                StandardRenderers.RenderCandles(Ctx(c, bars, theme), series, new SKPaint()));
        });
        Assert.Null(ex);
    }

    [Fact]
    public void RenderCandles_LogScale_DoesNotThrow_DrawsSomething()
    {
        var theme = DefaultTheme();
        var bars = MakeBars(15);
        var series = CandleSeries(15);
        using var bmp = Render(c =>
            StandardRenderers.RenderCandles(Ctx(c, bars, theme, min: 1, max: 1000, log: true), series, new SKPaint()));
        Assert.True(NonBlackPixelCount(bmp) > 0);
    }

    [Fact]
    public void RenderCandles_ViewportLongerThanData_DoesNotThrow()
    {
        // ViewportLength > data.Count exercises the `i >= data.Count` break guard.
        var theme = DefaultTheme();
        var bars = MakeBars(5);
        var series = CandleSeries(5);
        var ex = Record.Exception(() =>
        {
            using var bmp = Render(c =>
                StandardRenderers.RenderCandles(Ctx(c, bars, theme, viewportLength: 40), series, new SKPaint()));
        });
        Assert.Null(ex);
    }

    // ── RenderCandles: accessibility modes change output ────────────────────

    [Fact]
    public void RenderCandles_HollowUpMode_ProducesDifferentPixels_ThanFilled()
    {
        // Hollow-up-candles outlines the up-bodies instead of filling them, so the
        // interior pixels of an up-candle differ between the two modes.
        var baseTheme = DefaultTheme();
        var hollowTheme = baseTheme with { HollowUpCandles = true };
        var bars = MakeBars(20);
        var series = CandleSeries(20);

        using var filled = Render(c =>
            StandardRenderers.RenderCandles(Ctx(c, bars, baseTheme), series, new SKPaint()));
        using var hollow = Render(c =>
            StandardRenderers.RenderCandles(Ctx(c, bars, hollowTheme), series, new SKPaint()));

        Assert.True(PixelsDiffer(filled, hollow),
            "hollow-up mode must change the rendered candles");
    }

    [Fact]
    public void RenderCandles_ColorVisionSafeMode_ProducesDifferentPixels()
    {
        // Color-vision-safe swaps the green/red up/down pair for blue/orange, so the
        // painted body colours (hence pixels) differ from the default palette.
        var baseTheme = DefaultTheme();
        var cvTheme = baseTheme with { ColorVisionSafe = true };
        var bars = MakeBars(20);
        var series = CandleSeries(20);

        using var normal = Render(c =>
            StandardRenderers.RenderCandles(Ctx(c, bars, baseTheme), series, new SKPaint()));
        using var colorVision = Render(c =>
            StandardRenderers.RenderCandles(Ctx(c, bars, cvTheme), series, new SKPaint()));

        Assert.True(PixelsDiffer(normal, colorVision),
            "color-vision-safe mode must change the rendered candle colours");
    }

    // ── RenderDirectionalBars ───────────────────────────────────────────────

    private static ChartSeries BarSeries(string comp, double[] data, ColorSource source)
    {
        var config = new SeriesConfig { Id = comp, Name = comp, Pane = "sub" };
        config.Components.Add(new ComponentConfig
        {
            Name = comp,
            DisplayType = ComponentDisplayType.Bar,
            ColorSource = source,
            ColorHex = "#00CC00",
            ColorHexSecondary = "#CC0000",
        });
        var buffer = new SeriesDataBuffer { SeriesId = comp };
        buffer.ComponentData[comp] = data;
        return new ChartSeries(config, buffer);
    }

    [Fact]
    public void RenderDirectionalBars_ValueSource_DrawsSomething()
    {
        var theme = DefaultTheme();
        var data = new[] { 2.0, -3.0, 5.0, -1.0, 4.0 };
        var series = BarSeries("macd", data, ColorSource.Value);
        var bars = MakeBars(5);
        using var bmp = Render(c =>
            StandardRenderers.RenderDirectionalBars(Ctx(c, bars, theme, min: -10, max: 10), series, series.Components[0]));
        Assert.True(NonBlackPixelCount(bmp) > 0);
    }

    [Fact]
    public void RenderDirectionalBars_PriceActionSource_DoesNotThrow()
    {
        var theme = DefaultTheme();
        var data = new[] { 1.0, 1.0, 1.0, 1.0, 1.0 };
        var series = BarSeries("vol", data, ColorSource.PriceAction);
        var bars = MakeBars(5);
        var ex = Record.Exception(() =>
        {
            using var bmp = Render(c =>
                StandardRenderers.RenderDirectionalBars(Ctx(c, bars, theme, min: 0, max: 2), series, series.Components[0]));
        });
        Assert.Null(ex);
    }

    [Fact]
    public void RenderDirectionalBars_EmptyAndNaN_DoNotThrow()
    {
        var theme = DefaultTheme();
        var bars = MakeBars(4);

        var empty = BarSeries("e", Array.Empty<double>(), ColorSource.Value);
        var nan = BarSeries("n", new[] { double.NaN, 1.0, double.NaN, -1.0 }, ColorSource.Value);

        var ex = Record.Exception(() =>
        {
            using var b1 = Render(c => StandardRenderers.RenderDirectionalBars(
                Ctx(c, bars, theme, viewportLength: 4, min: -2, max: 2), empty, empty.Components[0]));
            using var b2 = Render(c => StandardRenderers.RenderDirectionalBars(
                Ctx(c, bars, theme, min: -2, max: 2), nan, nan.Components[0]));
        });
        Assert.Null(ex);
    }

    [Fact]
    public void RenderDirectionalBars_ColorVisionSafe_ChangesPixels()
    {
        var baseTheme = DefaultTheme();
        var cvTheme = baseTheme with { ColorVisionSafe = true };
        var data = new[] { 2.0, -3.0, 5.0, -1.0, 4.0 };
        var series = BarSeries("macd", data, ColorSource.Value);
        var bars = MakeBars(5);

        using var normal = Render(c => StandardRenderers.RenderDirectionalBars(
            Ctx(c, bars, baseTheme, min: -10, max: 10), series, series.Components[0]));
        using var cv = Render(c => StandardRenderers.RenderDirectionalBars(
            Ctx(c, bars, cvTheme, min: -10, max: 10), series, series.Components[0]));

        Assert.True(PixelsDiffer(normal, cv),
            "color-vision-safe must recolour directional bars");
    }

    // ── RenderLine ───────────────────────────────────────────────────────────

    [Fact]
    public void RenderLine_NormalData_DrawsSomething()
    {
        var theme = DefaultTheme();
        var data = new[] { 95.0, 100.0, 105.0, 102.0, 108.0 };
        var series = LineSeries("ema", data);
        var bars = MakeBars(5);
        using var bmp = Render(c =>
        {
            using var paint = new SKPaint { Color = SKColors.Cyan, StrokeWidth = 2, Style = SKPaintStyle.Stroke };
            StandardRenderers.RenderLine(Ctx(c, bars, theme, min: 90, max: 115), series, series.Components[0], paint);
        });
        Assert.True(NonBlackPixelCount(bmp) > 0);
    }

    [Fact]
    public void RenderLine_EmptyData_DoesNotThrow_DrawsNothing()
    {
        var theme = DefaultTheme();
        var series = LineSeries("ema", Array.Empty<double>());
        var bars = MakeBars(5);
        using var bmp = Render(c =>
        {
            using var paint = new SKPaint { Color = SKColors.Cyan, StrokeWidth = 2, Style = SKPaintStyle.Stroke };
            StandardRenderers.RenderLine(Ctx(c, bars, theme, viewportLength: 5), series, series.Components[0], paint);
        });
        Assert.Equal(0, NonBlackPixelCount(bmp));
    }

    [Fact]
    public void RenderLine_NaNGaps_DoNotThrow()
    {
        var theme = DefaultTheme();
        var data = new[] { 95.0, double.NaN, 105.0, double.NaN, double.NaN, 108.0 };
        var series = LineSeries("ema", data);
        var bars = MakeBars(6);
        var ex = Record.Exception(() =>
        {
            using var bmp = Render(c =>
            {
                using var paint = new SKPaint { Color = SKColors.Cyan, StrokeWidth = 2, Style = SKPaintStyle.Stroke };
                StandardRenderers.RenderLine(Ctx(c, bars, theme, min: 90, max: 115), series, series.Components[0], paint);
            });
        });
        Assert.Null(ex);
    }

    [Fact]
    public void RenderLine_SingleValue_DoesNotThrow()
    {
        var theme = DefaultTheme();
        var series = LineSeries("ema", new[] { 100.0 });
        var bars = MakeBars(1);
        var ex = Record.Exception(() =>
        {
            using var bmp = Render(c =>
            {
                using var paint = new SKPaint { Color = SKColors.Cyan, StrokeWidth = 2, Style = SKPaintStyle.Stroke };
                StandardRenderers.RenderLine(Ctx(c, bars, theme, min: 90, max: 115), series, series.Components[0], paint);
            });
        });
        Assert.Null(ex);
    }

    [Fact]
    public void RenderLine_AreaFill_LogScale_DoesNotThrow()
    {
        var theme = DefaultTheme();
        var config = new SeriesConfig { Id = "area", Name = "area", Pane = "sub" };
        config.Components.Add(new ComponentConfig { Name = "area", DisplayType = ComponentDisplayType.Area, ColorHex = "#00FFFF" });
        var buffer = new SeriesDataBuffer { SeriesId = "area" };
        buffer.ComponentData["area"] = new[] { 10.0, 50.0, 30.0, 80.0, 20.0 };
        var series = new ChartSeries(config, buffer);
        var bars = MakeBars(5);
        var ex = Record.Exception(() =>
        {
            using var bmp = Render(c =>
            {
                using var paint = new SKPaint { Color = SKColors.Cyan, StrokeWidth = 2, Style = SKPaintStyle.Stroke };
                StandardRenderers.RenderLine(Ctx(c, bars, theme, min: 1, max: 100, log: true), series, series.Components[0], paint);
            });
        });
        Assert.Null(ex);
    }

    // ── GetPhaseColor ─────────────────────────────────────────────────────────

    [Fact]
    public void GetPhaseColor_ClampsOutOfRangeIndices_ToTheEnds()
    {
        // Below 0 → the phase-0 (Max Fear) colour; above 10 → phase-10 (Max Euphoria).
        Assert.Equal(StandardRenderers.GetPhaseColor(0), StandardRenderers.GetPhaseColor(-5));
        Assert.Equal(StandardRenderers.GetPhaseColor(10), StandardRenderers.GetPhaseColor(999));
    }

    [Fact]
    public void GetPhaseColor_DistinctColoursAcrossThePhaseBand()
    {
        // The 11 phases are meant to be visually distinct; sanity-check the extremes
        // and midpoint differ so the legend isn't accidentally collapsed.
        var fear = StandardRenderers.GetPhaseColor(0);
        var neutral = StandardRenderers.GetPhaseColor(5);
        var euphoria = StandardRenderers.GetPhaseColor(10);
        Assert.NotEqual(fear, neutral);
        Assert.NotEqual(neutral, euphoria);
        Assert.NotEqual(fear, euphoria);
    }
}
