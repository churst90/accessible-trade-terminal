using System.Collections.Immutable;
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
/// <b>What actually reaches the pixels.</b>
///
/// <para>
/// The Skia path carried about 190 test methods before this file and not one of them asserted a
/// colour, a position or a layout — <c>StandardRenderersSmokeTests</c> states its own rule up
/// front: "drew something / drew something DIFFERENT, never exact colours". That is the right
/// call for robustness, and it leaves every question of the form "is the up candle the up
/// colour" unanswered. <c>PaneLayoutService</c>, <c>ChartRenderer.RenderYAxis</c>, the pane-height
/// allocator and eight of the nine marker shapes were named by no test at all, and nothing under
/// <c>Core/Services/Rendering</c> has ever been mutated.
/// </para>
///
/// <para>
/// These are the assertions a sighted reviewer makes by glancing at the chart, written down.
/// Cody cannot make them — the sonification reads Close and Open directly and never consults a
/// pixel — so for this part of the app the test suite is the only reviewer there is.
/// </para>
/// </summary>
public sealed class ChartFrameRenderingTests
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

    private static RenderContext Ctx(SKCanvas canvas, IReadOnlyList<Ohlcv> data, ChartTheme theme,
        int viewportStart = 0, int? viewportLength = null, double min = 90, double max = 115,
        SKRect? rect = null)
    {
        int vlen = viewportLength ?? Math.Max(1, data.Count);
        var r = rect ?? SKRect.Create(0, 0, W, H);
        return new RenderContext(canvas, r, data, viewportStart, vlen, min, max, false,
            r.Width / vlen, 1f, "Main", 0, theme);
    }

    private static SKBitmap Render(Action<SKCanvas> draw, int w = W, int h = H)
    {
        var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp)) { canvas.Clear(SKColors.Black); draw(canvas); }
        return bmp;
    }

    /// <summary>Every distinct colour in the bitmap and how many pixels carry it.</summary>
    private static Dictionary<SKColor, int> Histogram(SKBitmap bmp)
    {
        var counts = new Dictionary<SKColor, int>();
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
        {
            var c = bmp.GetPixel(x, y);
            counts[c] = counts.TryGetValue(c, out int n) ? n + 1 : 1;
        }
        return counts;
    }

    private static bool CloseTo(SKColor a, SKColor b, int tolerance = 6) =>
        Math.Abs(a.Red - b.Red) <= tolerance && Math.Abs(a.Green - b.Green) <= tolerance
        && Math.Abs(a.Blue - b.Blue) <= tolerance;

    // ── Candle bodies wear the theme's colours ───────────────────────────────

    private static ChartSeries CandleSeries()
    {
        var config = new SeriesConfig { Id = "price", Name = "price", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "body", DisplayType = ComponentDisplayType.Candle,
            ColorHex = "#123456", ColorHexSecondary = "#654321",   // metadata colours, NOT user-styled
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = "price" });
    }

    /// <summary>
    /// An up candle is painted in <c>Theme.CandleBullishBody</c>. The component's own ColorHex is
    /// a hardcoded TradingView teal from indicator metadata, and while the renderer read it
    /// unconditionally the theme could repaint the background, the grid, the axes and the whole
    /// application chrome while the one element people look at stayed teal.
    /// </summary>
    [Fact]
    public void AnUpCandlesBody_IsTheThemesBullishColour()
    {
        var theme = Theme();
        using var bmp = Render(c => StandardRenderers.RenderCandles(
            Ctx(c, Bars(8, i => (100, 108)), theme), CandleSeries(), new SKPaint()));

        var body = Histogram(bmp).Where(kv => kv.Key != SKColors.Black)
            .OrderByDescending(kv => kv.Value).First().Key;
        Assert.True(CloseTo(body, theme.CandleBullishBody),
            $"up bodies painted {body}, theme says {theme.CandleBullishBody}");
    }

    /// <summary>And a down candle the bearish one — the comparison is Close >= Open, not the reverse.</summary>
    [Fact]
    public void ADownCandlesBody_IsTheThemesBearishColour()
    {
        var theme = Theme();
        using var bmp = Render(c => StandardRenderers.RenderCandles(
            Ctx(c, Bars(8, i => (108, 100)), theme), CandleSeries(), new SKPaint()));

        var body = Histogram(bmp).Where(kv => kv.Key != SKColors.Black)
            .OrderByDescending(kv => kv.Value).First().Key;
        Assert.True(CloseTo(body, theme.CandleBearishBody),
            $"down bodies painted {body}, theme says {theme.CandleBearishBody}");
    }

    /// <summary>
    /// A doji — Close exactly equal to Open — reads as UP. The boundary has to be pinned because
    /// both a strict &gt; and a flipped comparison paint a chart that looks fine until you notice
    /// every flat bar is red.
    /// </summary>
    [Fact]
    public void ADojiReadsAsUp()
    {
        var theme = Theme();
        using var bmp = Render(c => StandardRenderers.RenderCandles(
            Ctx(c, Bars(8, i => (104, 104)), theme), CandleSeries(), new SKPaint()));

        var painted = Histogram(bmp);
        Assert.True(painted.Keys.Any(c => CloseTo(c, theme.CandleBullishBody)),
            "a flat bar painted no bullish pixel at all");
        Assert.False(painted.Keys.Any(c => CloseTo(c, theme.CandleBearishBody)),
            "a flat bar painted a bearish body — the comparison is Close >= Open");
    }

    // ── Volume follows the candle ────────────────────────────────────────────

    private static ChartSeries VolumeSeries(double[] values)
    {
        var config = new SeriesConfig { Id = "volume", Name = "Volume", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "volume", DisplayType = ComponentDisplayType.Histogram,
            Role = ComponentRole.Volume, ColorSource = ColorSource.PriceAction,
            ColorHex = "#123456", ColorHexSecondary = "#654321",
        });
        var buffer = new SeriesDataBuffer { SeriesId = "volume" };
        buffer.ComponentData["volume"] = values;
        return new ChartSeries(config, buffer);
    }

    /// <summary>
    /// A volume bar takes the candle's colour, so the two panes agree about what "up" looks like.
    /// Any other directional bar — a MACD histogram, a money-flow bar — keeps its own palette,
    /// because it is not price direction and colouring it like a candle would say it was.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AVolumeBar_TakesTheCandlesColour(bool up)
    {
        var theme = Theme();
        var bars = Bars(8, i => up ? (100, 108) : (108, 100));
        var series = VolumeSeries(Enumerable.Repeat(1000.0, 8).ToArray());

        using var bmp = Render(c => StandardRenderers.RenderDirectionalBars(
            Ctx(c, bars, theme, min: 0, max: 1200), series, series.Components[0]));

        var painted = Histogram(bmp).Where(kv => kv.Key != SKColors.Black)
            .OrderByDescending(kv => kv.Value).First().Key;
        var expected = up ? theme.CandleBullishBody : theme.CandleBearishBody;
        // Bars are drawn at alpha 180 over black, so compare the blended result.
        Assert.True(CloseTo(painted, Blend(expected, 180), 12),
            $"volume painted {painted}, expected the candle colour {expected} at alpha 180");
    }

    private static SKColor Blend(SKColor over, byte alpha)
    {
        float a = alpha / 255f;
        return new SKColor((byte)(over.Red * a), (byte)(over.Green * a), (byte)(over.Blue * a));
    }

    /// <summary>
    /// The direction of a price-following bar is read from the VIEWPORT SLICE, not from the
    /// absolute bar index. <c>ctx.Data</c> has already been sliced to the viewport, so indexing it
    /// with <c>ViewportStart + i</c> runs off the end — and the bounds check then answers "not up"
    /// for every bar, painting an entire scrolled-back volume pane red.
    /// </summary>
    [Fact]
    public void ScrolledBack_VolumeStillReadsTheBarsUnderIt()
    {
        var theme = Theme();
        var slice = Bars(8, i => (100, 108));                       // the viewport slice: all UP
        var series = VolumeSeries(new double[58].Select((_, i) => 1000.0).ToArray());

        using var bmp = Render(c => StandardRenderers.RenderDirectionalBars(
            Ctx(c, slice, theme, viewportStart: 50, viewportLength: 8, min: 0, max: 1200),
            series, series.Components[0]));

        var painted = Histogram(bmp).Where(kv => kv.Key != SKColors.Black)
            .OrderByDescending(kv => kv.Value).First().Key;
        Assert.True(CloseTo(painted, Blend(theme.CandleBullishBody, 180), 12),
            $"scrolled back, volume painted {painted} for eight up bars");
    }

    // ── Markers sit off the bar, on the side they claim ──────────────────────

    /// <summary>
    /// A BelowBar marker's y is BELOW the bar's low on screen — larger y, since the canvas grows
    /// downward — and an AboveBar marker's is above the high. Swapping the two draws every buy
    /// arrow over the candle it belongs under, which is the kind of defect that survives a green
    /// suite forever because nothing but an eye can see it.
    /// </summary>
    [Fact]
    public void MarkersSitOnTheSideTheyClaim()
    {
        var theme = Theme();
        var bars = Bars(4, i => (100, 104));
        using var surface = new SKCanvas(new SKBitmap(W, H));
        var ctx = Ctx(surface, bars, theme);

        var below = new ComponentConfig { Name = "m", MarkerAnchor = MarkerAnchor.BelowBar };
        var above = new ComponentConfig { Name = "m", MarkerAnchor = MarkerAnchor.AboveBar };
        var onValue = new ComponentConfig { Name = "m", MarkerAnchor = MarkerAnchor.Value };

        float yLow = ChartMath.MapY(bars[0].Low, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, false);
        float yHigh = ChartMath.MapY(bars[0].High, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, false);

        Assert.True(StandardRenderers.ResolveMarkerY(ctx, below, 0, 102) > yLow, "BelowBar must sit under the low");
        Assert.True(StandardRenderers.ResolveMarkerY(ctx, above, 0, 102) < yHigh, "AboveBar must sit over the high");
        Assert.Equal(ChartMath.MapY(102, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, false),
                     StandardRenderers.ResolveMarkerY(ctx, onValue, 0, 102), 3);
    }

    /// <summary>
    /// A doji has no range to pad with, so the gap comes off the price instead. Without that floor
    /// the marker lands exactly on the bar and the anchor means nothing.
    /// </summary>
    [Fact]
    public void AMarkerOnADojiStillClearsTheBar()
    {
        var theme = Theme();
        var flat = new List<Ohlcv> { new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100, 100, 100, 100, 1) };
        using var surface = new SKCanvas(new SKBitmap(W, H));
        var ctx = Ctx(surface, flat, theme, viewportLength: 1);

        float yLow = ChartMath.MapY(100, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, false);
        float marker = StandardRenderers.ResolveMarkerY(ctx, new ComponentConfig { Name = "m", MarkerAnchor = MarkerAnchor.BelowBar }, 0, 100);
        Assert.True(marker > yLow, "a doji's marker still has to clear it");
    }

    // ── The fill is a property, and it is opt-in ─────────────────────────────

    private static ChartSeries FilledLine(double[] values, bool fill)
    {
        var config = new SeriesConfig { Id = "l", Name = "L", Pane = "Pane_L", IndicatorCode = "L" };
        config.Components.Add(new ComponentConfig
        {
            Name = "L", DisplayType = ComponentDisplayType.Line, IsVisible = true,
            ColorHex = "#00FF00", IsAreaFill = fill, Thickness = 2f,
        });
        var buffer = new SeriesDataBuffer { SeriesId = "l" };
        buffer.ComponentData["L"] = values;
        return new ChartSeries(config, buffer);
    }

    private static int PaintedPixels(ChartSeries series, ChartTheme theme)
    {
        using var bmp = Render(c => StandardRenderers.RenderLine(
            Ctx(c, Bars(20, i => (100, 102)), theme, min: 0, max: 100),
            series, series.Components[0], new SKPaint { Color = SKColors.Lime, StrokeWidth = 2f }));
        return Histogram(bmp).Where(kv => kv.Key != SKColors.Black).Sum(kv => kv.Value);
    }

    /// <summary>
    /// <c>IsAreaFill</c> is the one way to ask for a fill, and it is read. Every provider set it,
    /// the factory stored it, <c>Clone</c> copied it and the workspace saved it — and the renderer
    /// computed its own answer from the display type instead, so about twenty-five components
    /// carried <c>IsAreaFill = true</c> and none of them has ever drawn a fill.
    /// </summary>
    [Fact]
    public void ALineThatAsksForAFill_GetsOne()
    {
        var theme = Theme();
        var values = Enumerable.Range(0, 20).Select(i => 60.0 + 10 * Math.Sin(i / 3.0)).ToArray();

        int filled = PaintedPixels(FilledLine(values, fill: true), theme);
        int plain   = PaintedPixels(FilledLine(values, fill: false), theme);

        Assert.True(filled > plain * 3,
            $"a filled line painted {filled} pixels against a plain line's {plain} — the fill is not being drawn");
    }

    /// <summary>
    /// And it is OPT-IN. Cody's call, asked directly: the default for an oscillator is off, so
    /// nothing on screen changes until a component says it wants one.
    /// </summary>
    [Fact]
    public void TheFleetDefaultsToNoFill()
    {
        var factory = new IndicatorModelFactory(
            new StylingService(new ComponentRoleMapper(),
                new Core.Services.Audio.SonificationProfileProvider(), new PaneAssignmentService()),
            new Mocks.MockIndicatorPreferencesService());

        var filled = new List<string>();
        foreach (var provider in IndicatorProviderFixture.AllProviders())
        foreach (var meta in provider.GetIndicators())
        {
            var series = factory.CreateSeriesFromMetadata(meta, meta.Name, PaneAssignmentService.PaneFor(meta),
                new List<(string, string)>(), null, null);
            foreach (var c in series.Components)
                if (c.IsAreaFill) filled.Add($"{meta.Code}.{c.Name}");
        }
        Assert.True(filled.Count == 0,
            "a fill is opt-in and nothing has opted in yet:\n  " + string.Join("\n  ", filled));
    }

    // ── A two-colour line reads its own declaration ──────────────────────────

    private static ChartSeries PolaritySeries(double[] values, double baseline, bool polarity)
    {
        var config = new SeriesConfig { Id = "mfi", Name = "MFI", Pane = "Pane_Mfi", IndicatorCode = "Mfi" };
        config.Components.Add(new ComponentConfig
        {
            Name = "Mfi", DisplayType = ComponentDisplayType.Oscillator, IsVisible = true,
            ColorHex = "#00FF00", ColorHexSecondary = "#FF0000",
            ColorSource = ColorSource.Value, ColorBaseline = baseline,
            ReferenceLevel = baseline, UsePolarityColoring = polarity, IsAreaFill = true, Thickness = 2f,
        });
        var buffer = new SeriesDataBuffer { SeriesId = "mfi" };
        buffer.ComponentData["Mfi"] = values;
        return new ChartSeries(config, buffer);
    }

    /// <summary>
    /// Cody, 2026-09-12, on MFI: <i>"I thought it was red below and green above the midline."</i>
    /// So did its provider — MFI declares a teal primary, a red secondary, <c>ColorSource.Value</c>
    /// and <c>ColorBaseline = 50</c>, four fields that say exactly that. It drew solid teal,
    /// because a component of display type Oscillator or Line goes through <c>RenderLine</c>,
    /// which painted every point with the primary colour and read none of the other three.
    /// </summary>
    [Fact]
    public void ATwoColourOscillator_IsTheSecondColourBelowItsBaseline()
    {
        var theme = Theme();
        // 20 bars falling from 80 to 20 across a midline at 50: half above, half below.
        var values = Enumerable.Range(0, 20).Select(i => 80.0 - i * 60.0 / 19).ToArray();
        var series = PolaritySeries(values, baseline: 50, polarity: true);

        using var bmp = Render(c => StandardRenderers.RenderLine(
            Ctx(c, Bars(20, i => (100, 102)), theme, min: 0, max: 100),
            series, series.Components[0], new SKPaint { Color = SKColors.Lime, StrokeWidth = 2f }));

        var painted = Histogram(bmp);
        Assert.True(painted.Keys.Any(c => c.Red > 100 && c.Green < 60), "nothing was painted in the below-baseline colour");
        Assert.True(painted.Keys.Any(c => c.Green > 100 && c.Red < 60), "nothing was painted in the above-baseline colour");
    }

    /// <summary>
    /// And the colour changes at the BASELINE, not at the next bar. The two halves must sit on
    /// opposite sides of the midline's y — a split that lags by a bar puts a whole day of the
    /// wrong colour on a daily chart.
    /// </summary>
    [Fact]
    public void TheColourChangesAtTheBaseline()
    {
        var theme = Theme();
        var values = Enumerable.Range(0, 20).Select(i => 80.0 - i * 60.0 / 19).ToArray();
        var series = PolaritySeries(values, baseline: 50, polarity: true);

        using var bmp = Render(c => StandardRenderers.RenderLine(
            Ctx(c, Bars(20, i => (100, 102)), theme, min: 0, max: 100),
            series, series.Components[0], new SKPaint { Color = SKColors.Lime, StrokeWidth = 2f }));

        int midlineY = (int)ChartMath.MapY(50, 0, H, 0, 100, false);
        int redAbove = 0, greenBelow = 0;
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            var c = bmp.GetPixel(x, y);
            bool red = c.Red > 100 && c.Green < 60;
            bool green = c.Green > 100 && c.Red < 60;
            // y grows downward, so "above the midline" is a SMALLER y.
            if (red && y < midlineY - 3) redAbove++;
            if (green && y > midlineY + 3) greenBelow++;
        }
        Assert.Equal(0, redAbove);
        Assert.Equal(0, greenBelow);
    }

    /// <summary>
    /// A component that did NOT ask for the split keeps one colour. Every bounded oscillator but
    /// MFI is in that group, and the change must not reach them.
    /// </summary>
    [Fact]
    public void AComponentThatDidNotAskForASplit_StaysOneColour()
    {
        var theme = Theme();
        var values = Enumerable.Range(0, 20).Select(i => 80.0 - i * 60.0 / 19).ToArray();
        var series = PolaritySeries(values, baseline: 50, polarity: false);

        using var bmp = Render(c => StandardRenderers.RenderLine(
            Ctx(c, Bars(20, i => (100, 102)), theme, min: 0, max: 100),
            series, series.Components[0], new SKPaint { Color = SKColors.Lime, StrokeWidth = 2f }));

        Assert.DoesNotContain(Histogram(bmp).Keys, c => c.Red > 100 && c.Green < 60);
    }

    /// <summary>
    /// A baseline of zero on a strictly positive oscillator is a split at a value it never
    /// reaches, so the line stays one colour. This is why turning the feature on is invisible for
    /// RSI, Stochastic and the Ultimate Oscillator: their baseline is 0 and their floor is 0.
    /// </summary>
    [Fact]
    public void ABaselineTheValuesNeverReach_ChangesNothing()
    {
        var theme = Theme();
        var values = Enumerable.Range(0, 20).Select(i => 80.0 - i * 60.0 / 19).ToArray();
        var series = PolaritySeries(values, baseline: 0, polarity: true);

        using var bmp = Render(c => StandardRenderers.RenderLine(
            Ctx(c, Bars(20, i => (100, 102)), theme, min: 0, max: 100),
            series, series.Components[0], new SKPaint { Color = SKColors.Lime, StrokeWidth = 2f }));

        Assert.DoesNotContain(Histogram(bmp).Keys, c => c.Red > 100 && c.Green < 60);
    }

    // ── The frame: panes, dividers and the axis strip ────────────────────────

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
        var themes = Themes();
        var layout = new PaneLayoutService();
        var styling = new StylingService(new ComponentRoleMapper(),
            new Core.Services.Audio.SonificationProfileProvider(), new PaneAssignmentService());
        return (new ChartRenderer(themes, styling, layout,
            NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>()), layout);
    }

    private static SKBitmap RenderFrame(ChartRenderer renderer, IReadOnlyList<ChartSeries> series,
        IReadOnlyList<Ohlcv> data, IReadOnlyDictionary<string, (double Min, double Max)> paneRanges)
    {
        var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, W, H, data, series, cursorIndex: data.Count - 1,
                viewportStart: 0, viewportLength: data.Count,
                viewportRange: (95, 115), paneRanges: paneRanges, rightMarginBars: 0);
        return bmp;
    }

    /// <summary>
    /// Three indicator panes on a 300px canvas: the 60px indicator floor binds, the price pane
    /// takes what is left (about 36% of the plot rather than a quarter), and every divider lands
    /// above the x-axis strip, because a divider inside the strip is a drag handle for a pane
    /// that is not there.
    ///
    /// <para>
    /// Until 2026-09-22 this pinned four EQUAL quarters — the signature of the crowded path
    /// discarding the price pane's weight, which was the layout the renderer really produced
    /// and the one a screenshot of Cody's maximised window showed. The floor is 60 now and the
    /// crowded path keeps the weight, so the expectation is stated from the floor rule rather
    /// than from the arithmetic that hid the defect.
    /// </para>
    /// </summary>
    [Fact]
    public void ThreeIndicatorPanes_GiveThePriceMoreThanAQuarter_AllDividersAboveTheAxisStrip()
    {
        var (renderer, layout) = Renderer();
        var data = Bars(40, i => (100 + i % 3, 102 + i % 3));
        var series = new List<ChartSeries>
        {
            IndicatorSeries("candles", "Main", Array.Empty<double>()),
            IndicatorSeries("a", "Pane_A", Enumerable.Repeat(50.0, 40).ToArray()),
            IndicatorSeries("b", "Pane_B", Enumerable.Repeat(50.0, 40).ToArray()),
            IndicatorSeries("c", "Pane_C", Enumerable.Repeat(50.0, 40).ToArray()),
        };
        var ranges = new Dictionary<string, (double Min, double Max)>
        {
            ["Main"] = (95, 115), ["Pane_A"] = (0, 100), ["Pane_B"] = (0, 100), ["Pane_C"] = (0, 100),
        };

        using var _ = RenderFrame(renderer, series, data, ranges);

        Assert.Equal(3, layout.Dividers.Count);
        Assert.Equal(new[] { "Pane_A", "Pane_B", "Pane_C" }, layout.Dividers.Select(d => d.BelowPaneName));

        float plotFraction = 1f - layout.AxisHeightFraction;
        // The floor as a fraction of the canvas. The renderer scales it by the density it
        // derives from the canvas, so the tolerance is loose; the SHAPE is the assertion.
        float indicator = 60f / H;
        for (int i = 0; i < 3; i++)
        {
            float expected = plotFraction - (3 - i) * indicator;
            Assert.True(Math.Abs(layout.Dividers[i].DividerFraction - expected) < 0.03f,
                $"divider {i} at {layout.Dividers[i].DividerFraction}, expected about {expected}");
            Assert.True(layout.Dividers[i].DividerFraction < plotFraction,
                $"divider {i} is inside the x-axis strip");
        }

        // The signature to look for on screen: the price pane (above the first divider) is
        // clearly taller than an indicator pane (between two dividers). Four equal panes is
        // the old crowded-path layout and would fail here.
        float price = layout.Dividers[0].DividerFraction;
        float pane = layout.Dividers[2].DividerFraction - layout.Dividers[1].DividerFraction;
        Assert.True(price > pane * 1.2f,
            $"the price pane ({price:F3}) is not clearly taller than an indicator pane ({pane:F3}); the weight is not reaching the screen");
    }

    /// <summary>
    /// The layout service is what every pointer-to-data mapping subtracts, so the two strips have
    /// to be reported as a real fraction of the canvas — never zero, never the whole thing. A zero
    /// here silently moves every click, crosshair readout and drawing anchor.
    /// </summary>
    [Fact]
    public void TheAxisStripsAreReportedAsRealFractions()
    {
        var (renderer, layout) = Renderer();
        var data = Bars(20, i => (100, 102));
        using var _ = RenderFrame(renderer, new[] { IndicatorSeries("candles", "Main", Array.Empty<double>()) },
            data, new Dictionary<string, (double, double)> { ["Main"] = (95, 115) });

        Assert.InRange(layout.AxisHeightFraction, 0.01f, 0.5f);
        Assert.InRange(layout.AxisWidthFraction, 0.01f, 0.5f);
    }

    /// <summary>
    /// A frame with one indicator pane paints in BOTH bands — the price pane and the indicator
    /// band below it. Dropping the <c>currentY += mainPaneHeight</c> that advances between them
    /// stacks every pane on the price pane and leaves the bottom of the canvas empty, which no
    /// "drew something" assertion notices.
    /// </summary>
    [Fact]
    public void AnIndicatorPaneIsPaintedBelowThePricePane()
    {
        var (renderer, layout) = Renderer();
        var data = Bars(30, i => (100 + i % 4, 103 + i % 4));
        var series = new[]
        {
            IndicatorSeries("candles", "Main", Array.Empty<double>()),
            IndicatorSeries("rsi", "Pane_Rsi", Enumerable.Range(0, 30).Select(i => 30.0 + i).ToArray()),
        };
        using var bmp = RenderFrame(renderer, series, data,
            new Dictionary<string, (double Min, double Max)> { ["Main"] = (95, 115), ["Pane_Rsi"] = (0, 100) });

        Assert.Single(layout.Dividers);
        int dividerY = (int)(layout.Dividers[0].DividerFraction * H);
        int axisTop = (int)((1f - layout.AxisHeightFraction) * H);

        Assert.True(PaintedRows(bmp, 0, dividerY) > 0, "the price pane painted nothing");
        Assert.True(PaintedRows(bmp, dividerY + 2, axisTop - 2) > 0, "the indicator band painted nothing");
    }

    private static int PaintedRows(SKBitmap bmp, int fromY, int toY)
    {
        int rows = 0;
        for (int y = Math.Max(0, fromY); y < Math.Min(bmp.Height, toY); y++)
            for (int x = 0; x < bmp.Width; x++)
                if (bmp.GetPixel(x, y) != SKColors.Black) { rows++; break; }
        return rows;
    }

    /// <summary>
    /// The y-axis column carries text. It is the only place a price appears on screen, so an empty
    /// strip is a chart with no numbers on it — and every "drew something" assertion in the suite
    /// stays green because the plot area is full of candles.
    /// </summary>
    [Fact]
    public void TheYAxisColumnCarriesLabels()
    {
        var (renderer, layout) = Renderer();
        var data = Bars(30, i => (100 + i % 4, 103 + i % 4));
        using var bmp = RenderFrame(renderer, new[] { IndicatorSeries("candles", "Main", Array.Empty<double>()) },
            data, new Dictionary<string, (double Min, double Max)> { ["Main"] = (95, 115) });

        int axisLeft = (int)((1f - layout.AxisWidthFraction) * W);
        int painted = 0;
        for (int y = 0; y < (int)((1f - layout.AxisHeightFraction) * H); y++)
            for (int x = axisLeft + 2; x < W; x++)
                if (bmp.GetPixel(x, y) != SKColors.Black) painted++;
        Assert.True(painted > 20, $"the y-axis column has {painted} painted pixels — no labels reached it");
    }

    // ── The grid: the bright line is the labelled line ───────────────────────

    /// <summary>
    /// A gridline that carries a label is drawn brighter than one that does not. The grid used to
    /// mark "every fifth line" major, which was a GUESS about where the labels were — and the
    /// guess was wrong for every range where the two files' steps disagreed. Now both take the
    /// step from ChartMath and the bright line is the labelled one by construction; this is the
    /// test that says so in pixels.
    /// </summary>
    [Fact]
    public void TheBrightGridlineIsTheLabelledOne()
    {
        var themes = Themes();
        var theme = themes.Current;
        var layer = new BackgroundLayer(themes);
        var rect = SKRect.Create(0, 0, W, 200);

        // Range 20 over 200px: the grid steps by 2, the labels by 4.
        const double min = 0, max = 20;
        using var bmp = new SKBitmap(W, 200);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Black);
            layer.Render(new RenderContext(canvas, rect, Bars(10, i => (100, 102)), 0, 10,
                min, max, false, W / 10f, 1f, "Pane_X", 0, theme), Array.Empty<ChartSeries>());
        }

        double grid = ChartMath.GridStep(max - min);
        double label = ChartMath.LabelStep(max - min, ChartMath.TargetLabelCount(rect.Height, 1f), grid);

        // Sample a labelled value and an unlabelled one, both of which ARE gridlines.
        double labelled = label * 2;                  // 8 on this pane
        double unlabelled = labelled + grid;          // 10 — a line, but not a label
        Assert.False(ChartMath.IsOnLabel(unlabelled, label), "fixture picked two labelled values");

        int brightAtLabel = Brightness(bmp, RowFor(labelled, rect, min, max));
        int brightAtPlain = Brightness(bmp, RowFor(unlabelled, rect, min, max));

        Assert.True(brightAtLabel > brightAtPlain,
            $"the labelled line at {labelled} ({brightAtLabel}) is no brighter than the plain one at {unlabelled} ({brightAtPlain})");
    }

    private static int RowFor(double value, SKRect rect, double min, double max)
        => (int)Math.Round(ChartMath.MapY(value, rect.Top, rect.Bottom, min, max, false));

    /// <summary>Total luminance across a row, ignoring the pane's own background.</summary>
    private static int Brightness(SKBitmap bmp, int y)
    {
        int total = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            int row = Math.Clamp(y + dy, 0, bmp.Height - 1);
            for (int x = 4; x < bmp.Width - 4; x++)
            {
                var c = bmp.GetPixel(x, row);
                total += c.Red + c.Green + c.Blue;
            }
        }
        return total;
    }

    /// <summary>
    /// The pane is outlined. It is the only thing separating one pane's plot area from the next
    /// one's, and with several indicator panes stacked an unbordered chart reads as one tall
    /// smear of lines.
    /// </summary>
    [Fact]
    public void ThePaneIsOutlined()
    {
        var themes = Themes();
        var layer = new BackgroundLayer(themes);
        var rect = SKRect.Create(0, 0, W, 200);
        using var bmp = new SKBitmap(W, 200);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Black);
            layer.Render(new RenderContext(canvas, rect, Bars(10, i => (100, 102)), 0, 10,
                0, 20, false, W / 10f, 1f, "Pane_X", 0, themes.Current), Array.Empty<ChartSeries>());
        }

        // The left column of the pane must differ from the pane's own fill.
        var fill = bmp.GetPixel(W / 2, 100);
        Assert.NotEqual(fill, bmp.GetPixel(0, 100));
    }

    // ── Candles do not run into each other ───────────────────────────────────

    /// <summary>
    /// Adjacent candle bodies leave a gap. The body is drawn at a fraction of the bar width for
    /// exactly this reason, and widening it merges every candle into a solid block — a chart that
    /// still "draws something", still "draws something different" when a mode changes, and is
    /// unreadable.
    /// </summary>
    [Fact]
    public void AdjacentCandleBodiesLeaveAGapBetweenThem()
    {
        var theme = Theme();
        const int bars = 8;
        using var bmp = Render(c => StandardRenderers.RenderCandles(
            Ctx(c, Bars(bars, i => (100, 108)), theme), CandleSeries(), new SKPaint()));

        // Walk the row through the middle of the bodies and count runs of background.
        int midY = (int)ChartMath.MapY(104, 0, H, 90, 115, false);
        int gaps = 0;
        bool inGap = false;
        for (int x = 0; x < W; x++)
        {
            bool background = bmp.GetPixel(x, midY) == SKColors.Black;
            if (background && !inGap) { gaps++; inGap = true; }
            else if (!background) inGap = false;
        }
        // bars bodies → at least bars-1 gaps between them (plus the margins at either end).
        Assert.True(gaps >= bars - 1, $"only {gaps} background runs across {bars} candles — the bodies are touching");
    }

    // ── No pane is squeezed out of existence ─────────────────────────────────

    /// <summary>
    /// <b>No pane is squeezed out by the ones above it.</b>
    ///
    /// <para>
    /// The allocator hands the indicator panes whatever the price pane is not using, then
    /// re-raises the price pane to a 15% floor without re-checking that it all still fits. Panes
    /// are laid out top-down by accumulating heights, so the whole overflow landed on the LAST
    /// indicator pane and it was drawn underneath the x-axis strip. Nine panes on a 300px canvas
    /// gave the bottom one 11 visible pixels against a documented 30px floor.
    /// </para>
    ///
    /// <para>
    /// Cody navigates panes with Alt+PageUp/PageDown, and since every oscillator got a pane of its
    /// own (2026-09-11) eight indicators is an ordinary chart, not an exotic one — so this was a
    /// key that walked to a pane which was not on the screen. The property is that the panes
    /// SHARE the shortfall: when they genuinely do not fit, every pane is equally small, and none
    /// of them is invisible.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryPaneKeepsAUsableHeight_HoweverManyThereAre()
    {
        var (renderer, layout) = Renderer();
        var data = Bars(30, i => (100 + i % 4, 103 + i % 4));
        var series = new List<ChartSeries> { IndicatorSeries("candles", "Main", Array.Empty<double>()) };
        var ranges = new Dictionary<string, (double Min, double Max)> { ["Main"] = (95, 115) };
        for (int i = 0; i < 8; i++)
        {
            series.Add(IndicatorSeries($"i{i}", $"Pane_{i}", Enumerable.Repeat(50.0, 30).ToArray()));
            ranges[$"Pane_{i}"] = (0, 100);
        }

        using var _ = RenderFrame(renderer, series, data, ranges);

        Assert.Equal(8, layout.Dividers.Count);
        var edges = layout.Dividers.Select(d => d.DividerFraction).ToList();
        edges.Add(1f - layout.AxisHeightFraction);

        var heights = Enumerable.Range(0, edges.Count - 1)
            .Select(i => (edges[i + 1] - edges[i]) * H).ToList();
        float mean = heights.Average();

        for (int i = 0; i < heights.Count; i++)
            Assert.True(heights[i] > mean * 0.8f,
                $"pane {i} got {heights[i]:F1}px against a mean of {mean:F1} — it is carrying the shortfall alone");

        // The LAST pane named, because it is the one the overflow always landed on: the stack is
        // laid out top-down, so whatever the arithmetic overspends is taken from the bottom.
        Assert.True(heights[^1] > mean * 0.8f,
            $"the bottom pane got {heights[^1]:F1}px against a mean of {mean:F1} — it is under the axis strip");
        Assert.True(edges[0] * H > 20f, "the price pane was squeezed out");
    }

    /// <summary>
    /// A frame renders with no data, no series, and a zero-length viewport without throwing. It
    /// runs on every tick of every chart, so a throw here is not a blank pane — it is the whole
    /// application.
    /// </summary>
    [Theory]
    [InlineData(0, 10)]
    [InlineData(20, 0)]
    public void ADegenerateFrameDoesNotThrow(int barCount, int viewportLength)
    {
        var (renderer, _) = Renderer();
        var data = Bars(barCount, i => (100, 102));
        using var bmp = new SKBitmap(W, H);
        using var canvas = new SKCanvas(bmp);
        renderer.Render(canvas, W, H, data, new[] { IndicatorSeries("candles", "Main", Array.Empty<double>()) },
            0, 0, viewportLength, (95, 115),
            new Dictionary<string, (double Min, double Max)> { ["Main"] = (95, 115) });
    }
}
