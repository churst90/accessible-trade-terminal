using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
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
/// <b>The parts of the picture that nothing was holding.</b>
///
/// <para>
/// The A2i mutant set put 34 mutants through the rendering layer — the first time anything under
/// <c>Core/Services/Rendering</c> or <c>ChartRenderer</c> had ever been mutated — and 25 were
/// caught, which is the highest rate of any campaign in this repo. The nine that survived are what
/// this file is about, and they cluster in a way worth recording: horizontal placement, colour
/// rules, marker sizing, axis maths and pane layout were all well guarded, while <b>candle
/// geometry beyond the x position</b> and <b>the phase-colour overlay</b> were not guarded at all.
/// </para>
///
/// <para>
/// Every assertion here reads real pixels out of a real Skia surface. That is the only reviewer
/// this part of the application has: the sonification reads Open, High, Low and Close directly out
/// of the model and never consults a pixel, so a chart could be drawn wrongly and sound perfectly
/// correct.
/// </para>
///
/// <para>
/// <b>Two of the nine were equivalent mutants and are recorded rather than tested</b> — see
/// <c>TheWickAlignmentIsBeltAndBracesWhileAntiAliasingIsOff</c> below.
/// </para>
/// </summary>
public sealed class ChartVisualStabilityTests
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

    /// <summary>Bars from an explicit OHLC shape — several of these turn on where the close sits
    /// inside the bar, or on a bar having no range at all, which a close-only generator cannot say.</summary>
    private static List<Ohlcv> Bars(int n, Func<int, (double O, double H, double L, double C)> shape)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>(n);
        for (int i = 0; i < n; i++)
        {
            var (o, h, l, c) = shape(i);
            bars.Add(new Ohlcv(start.AddHours(i), o, h, l, c, 1000));
        }
        return bars;
    }

    private static RenderContext Ctx(SKCanvas canvas, IReadOnlyList<Ohlcv> data, ChartTheme theme,
        double min = 90, double max = 115)
    {
        int vlen = Math.Max(1, data.Count);
        var r = SKRect.Create(0, 0, W, H);
        return new RenderContext(canvas, r, data, 0, vlen, min, max, false, r.Width / vlen, 1f, "Main", 0, theme);
    }

    private static SKBitmap Render(Action<SKCanvas> draw, int w = W, int h = H)
    {
        var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp)) { canvas.Clear(SKColors.Black); draw(canvas); }
        return bmp;
    }

    private static bool CloseTo(SKColor a, SKColor b, int tolerance = 6) =>
        Math.Abs(a.Red - b.Red) <= tolerance && Math.Abs(a.Green - b.Green) <= tolerance
        && Math.Abs(a.Blue - b.Blue) <= tolerance;

    private static int CountOf(SKBitmap bmp, SKColor want)
    {
        int n = 0;
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
            if (CloseTo(bmp.GetPixel(x, y), want)) n++;
        return n;
    }

    private static (int MinY, int MaxY) VerticalExtentOf(SKBitmap bmp, SKColor want)
    {
        int minY = int.MaxValue, maxY = -1;
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
            if (CloseTo(bmp.GetPixel(x, y), want)) { if (y < minY) minY = y; if (y > maxY) maxY = y; }
        return (minY, maxY);
    }

    /// <summary>
    /// A candle series whose WICK carries a colour of its own, so body pixels and wick pixels can
    /// be told apart. Without this the two are near-identical theme colours and a test that means
    /// to ask about the body is answered by the wick — which is exactly how the existing doji test
    /// passed while the body was not being drawn at all.
    /// </summary>
    private static ChartSeries CandleSeries(string wickHex = "#FF00FF")
    {
        var config = new SeriesConfig { Id = "price", Name = "price", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "body", DisplayType = ComponentDisplayType.Candle,
            ColorHex = "#123456", ColorHexSecondary = "#654321",
        });
        config.Components.Add(new ComponentConfig
        {
            Name = "wick", DisplayType = ComponentDisplayType.Wick,
            ColorHex = wickHex, IsUserStyled = true, Thickness = 1f,
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = "price" });
    }

    private static readonly SKColor WickPink = new(255, 0, 255);

    // ── A flat bar is still a bar ───────────────────────────────────────────────────

    /// <summary>
    /// <b>A doji has no body height, and the one-pixel minimum is the only thing that draws it.</b>
    ///
    /// <para>
    /// <c>bodyHeight = Math.Max(1, bottom - top)</c>. Drop the <c>Max</c> and every bar whose open
    /// equals its close renders as nothing at all — a gap in the chart where a real bar is. Dojis
    /// are not a curiosity; they are what an indecisive market prints at exactly the turning points
    /// a reader is looking for.
    /// </para>
    ///
    /// <para>
    /// <b>Why this needs its own wick colour.</b> <c>ADojiReadsAsUp</c> already renders a flat bar
    /// and asserts a bullish pixel exists — and it passed with the body suppressed, because the
    /// theme's bullish WICK colour is within tolerance of its bullish BODY colour, so the wick
    /// answered a question about the body. Colouring the wick separately is what makes the
    /// assertion about the thing it names.
    /// </para>
    /// </summary>
    [Fact]
    public void ADojiStillDrawsABody()
    {
        var theme = Theme();
        // Open == Close, with a real high and low so the wick is present and distinguishable.
        var bars = Bars(6, _ => (104.0, 107.0, 101.0, 104.0));

        using var bmp = Render(c => StandardRenderers.RenderCandles(
            Ctx(c, bars, theme), CandleSeries(), new SKPaint()));

        int wickPixels = CountOf(bmp, WickPink);
        Assert.True(wickPixels > 0, "the wick did not draw — the fixture is wrong, not the body");

        int bodyPixels = CountOf(bmp, theme.CandleBullishBody);
        Assert.True(bodyPixels > 0,
            "a flat bar drew no body at all. A doji's open equals its close, so its body height is " +
            "zero and only the one-pixel minimum puts it on the screen.");

        // And the body is a BAR, not a stray pixel: six bars across a 400px canvas give a body
        // several pixels wide each.
        Assert.True(bodyPixels >= 6,
            $"only {bodyPixels} body pixels for six doji bars — the bodies are not being drawn at " +
            "their full width.");
    }

    // ── The wick is the bar's range ─────────────────────────────────────────────────

    /// <summary>
    /// <b>The wick spans high to low, which is the only thing on the chart that shows a bar's full
    /// range.</b>
    ///
    /// <para>
    /// Drawn from open to close instead, it would duplicate the body and the chart would understate
    /// every excursion — and it would disagree with the sonification, which reads High and Low
    /// directly and encodes wick length as grit. The picture and the sound would be telling
    /// different stories about the same bar, with nothing to reconcile them.
    /// </para>
    /// </summary>
    [Fact]
    public void TheWickSpansTheBarsHighAndLowNotItsBody()
    {
        var theme = Theme();
        // A small body with long tails on both sides: body 103-104, range 96-113.
        var bars = Bars(4, _ => (103.0, 113.0, 96.0, 104.0));

        using var bmp = Render(c => StandardRenderers.RenderCandles(
            Ctx(c, bars, theme), CandleSeries(), new SKPaint()));

        var (wickTop, wickBottom) = VerticalExtentOf(bmp, WickPink);
        Assert.True(wickBottom >= 0, "no wick was drawn at all");

        float expectedHighY = ChartMath.MapY(113.0, 0, H, 90, 115, false);
        float expectedLowY = ChartMath.MapY(96.0, 0, H, 90, 115, false);
        float bodyTopY = ChartMath.MapY(104.0, 0, H, 90, 115, false);
        float bodyBottomY = ChartMath.MapY(103.0, 0, H, 90, 115, false);

        Assert.True(Math.Abs(wickTop - expectedHighY) <= 2,
            $"the wick starts at y={wickTop}, but the bar's HIGH maps to y={expectedHighY:F1} " +
            $"(the body's top is y={bodyTopY:F1}). A wick drawn from the body says the bar never " +
            "went there.");
        Assert.True(Math.Abs(wickBottom - expectedLowY) <= 2,
            $"the wick ends at y={wickBottom}, but the bar's LOW maps to y={expectedLowY:F1} " +
            $"(the body's bottom is y={bodyBottomY:F1}).");
    }

    // ── Direction readable by shape ─────────────────────────────────────────────────

    /// <summary>
    /// <b>Hollow-up-candles is an accessibility mode: it makes direction readable without colour
    /// perception at all.</b>
    ///
    /// <para>
    /// An up bar is outlined and a down bar filled, so the two differ in SHAPE. Invert it and shape
    /// now says the opposite of colour — which is worse than not having the mode, because a reader
    /// relying on outline-means-up is being told the wrong thing by the feature that exists to help
    /// them. Nothing tested it.
    /// </para>
    ///
    /// <para>
    /// Asserted by pixel count rather than by looking for an outline: a filled rectangle paints its
    /// whole area, an outlined one only its border, so the up bar must use markedly fewer pixels.
    /// </para>
    /// </summary>
    [Fact]
    public void WithHollowCandlesTheUpBarIsOutlinedAndTheDownBarIsFilled()
    {
        var theme = Theme() with { HollowUpCandles = true };

        // Tall bodies so the fill/outline difference is large and unambiguous.
        var up = Bars(4, _ => (96.0, 113.0, 95.0, 112.0));
        var down = Bars(4, _ => (112.0, 113.0, 95.0, 96.0));

        using var upBmp = Render(c => StandardRenderers.RenderCandles(
            Ctx(c, up, theme), CandleSeries(), new SKPaint()));
        using var downBmp = Render(c => StandardRenderers.RenderCandles(
            Ctx(c, down, theme), CandleSeries(), new SKPaint()));

        int upPixels = CountOf(upBmp, theme.CandleBullishBody);
        int downPixels = CountOf(downBmp, theme.CandleBearishBody);

        Assert.True(upPixels > 0, "the up candles drew nothing");
        Assert.True(downPixels > 0, "the down candles drew nothing");

        Assert.True(upPixels < downPixels / 2,
            $"with hollow-up-candles on, the up bars used {upPixels} pixels and the down bars " +
            $"{downPixels}. An outlined body paints only its border and a filled one its whole " +
            "area, so the up bars must use far fewer. Shape is the channel this mode adds, and " +
            "inverted it contradicts the colour.");
    }

    /// <summary>The control: with the mode OFF, both directions are filled and use comparable areas,
    /// so the assertion above is about the mode and not about the two fixtures differing.</summary>
    [Fact]
    public void WithHollowCandlesOffBothDirectionsAreFilled()
    {
        var theme = Theme();
        var up = Bars(4, _ => (96.0, 113.0, 95.0, 112.0));
        var down = Bars(4, _ => (112.0, 113.0, 95.0, 96.0));

        using var upBmp = Render(c => StandardRenderers.RenderCandles(Ctx(c, up, theme), CandleSeries(), new SKPaint()));
        using var downBmp = Render(c => StandardRenderers.RenderCandles(Ctx(c, down, theme), CandleSeries(), new SKPaint()));

        int upPixels = CountOf(upBmp, theme.CandleBullishBody);
        int downPixels = CountOf(downBmp, theme.CandleBearishBody);

        Assert.True(upPixels > downPixels / 2 && downPixels > upPixels / 2,
            $"with the mode off the two directions painted {upPixels} and {downPixels} pixels — " +
            "they should be comparable, both filled.");
    }

    // ── The sentiment phase overlay ─────────────────────────────────────────────────

    /// <summary>
    /// <b>The whole point of a phase-coloured candle is that its colour IS the phase.</b>
    ///
    /// <para>
    /// Cipher S's sentiment overlay paints each bar from an eleven-step fear-to-euphoria scale. If
    /// that falls back to ordinary bullish/bearish colouring, the overlay silently becomes an
    /// ordinary chart while still claiming to show sentiment — and the user has no way to tell,
    /// because there is nothing else on the screen that says what phase a bar is in.
    /// </para>
    ///
    /// <para>
    /// Grep the test project for <c>phaseData</c> before this file and there is nothing: the whole
    /// branch had never been given a fixture. Both A2i mutants aimed at it survived.
    /// </para>
    /// </summary>
    [Fact]
    public void APhaseColouredCandleTakesItsColourFromThePhase()
    {
        var theme = Theme();
        // Every bar bullish, so an ordinary render would paint them all the bullish colour — and
        // a phase of 9 (Extreme Greed) is nothing like it.
        var bars = Bars(6, _ => (100.0, 110.0, 99.0, 109.0));
        var phase = Enumerable.Repeat(9.0, 6).ToArray();

        using var bmp = Render(c => StandardRenderers.RenderCandles(
            Ctx(c, bars, theme), CandleSeries(), new SKPaint(), phase));

        var expected = StandardRenderers.GetPhaseColor(9);
        Assert.True(CountOf(bmp, expected) > 0,
            $"a candle with phase 9 painted no pixel of that phase's colour ({expected}).");
        Assert.True(CountOf(bmp, theme.CandleBullishBody) == 0,
            "a phase-coloured candle also painted the ordinary bullish body colour — the phase is " +
            "supposed to REPLACE the direction colouring, not sit beside it.");
    }

    /// <summary>
    /// Two different phases must paint two different colours, so "it used a phase colour" cannot be
    /// satisfied by one colour used for everything.
    /// </summary>
    [Fact]
    public void DifferentPhasesPaintDifferentColours()
    {
        var theme = Theme();
        var bars = Bars(6, _ => (100.0, 110.0, 99.0, 109.0));

        using var fear = Render(c => StandardRenderers.RenderCandles(
            Ctx(c, bars, theme), CandleSeries(), new SKPaint(), Enumerable.Repeat(0.0, 6).ToArray()));
        using var greed = Render(c => StandardRenderers.RenderCandles(
            Ctx(c, bars, theme), CandleSeries(), new SKPaint(), Enumerable.Repeat(10.0, 6).ToArray()));

        Assert.True(CountOf(fear, StandardRenderers.GetPhaseColor(0)) > 0, "phase 0 painted no phase colour");
        Assert.True(CountOf(greed, StandardRenderers.GetPhaseColor(10)) > 0, "phase 10 painted no phase colour");
        Assert.True(CountOf(fear, StandardRenderers.GetPhaseColor(10)) == 0,
            "a chart of phase 0 painted the phase 10 colour — the phase index is not being read.");
    }

    /// <summary>
    /// <b>A phase overlay suppresses wicks on purpose.</b>
    ///
    /// <para>
    /// The colour is carrying the message, and a field of wicks over the top of eleven hues is
    /// clutter. The range is still available through sonification and speech, which is the whole
    /// reason it can be dropped from the picture. Nothing tested that it was.
    /// </para>
    /// </summary>
    [Fact]
    public void APhaseOverlayDrawsNoWicks()
    {
        var theme = Theme();
        var bars = Bars(6, _ => (100.0, 113.0, 96.0, 109.0));   // long tails, so a wick would show

        using var withPhase = Render(c => StandardRenderers.RenderCandles(
            Ctx(c, bars, theme), CandleSeries(), new SKPaint(), Enumerable.Repeat(4.0, 6).ToArray()));
        using var without = Render(c => StandardRenderers.RenderCandles(
            Ctx(c, bars, theme), CandleSeries(), new SKPaint()));

        Assert.True(CountOf(without, WickPink) > 0,
            "the same bars drew no wick without a phase overlay — the fixture proves nothing");
        Assert.True(CountOf(withPhase, WickPink) == 0,
            $"a phase overlay drew {CountOf(withPhase, WickPink)} wick pixels. The colour is the " +
            "message under an overlay, and the bar's range is carried by sound instead.");
    }

    // ── The frame: what the y-axis column is for ────────────────────────────────────

    private static ChartSeries LineSeries(string id, string pane, double[] values, string hex = "#00FFFF")
    {
        var config = new SeriesConfig { Id = id, Name = id, Pane = pane, IndicatorCode = id };
        config.Components.Add(new ComponentConfig { Name = id, DisplayType = ComponentDisplayType.Line, ColorHex = hex });
        var buffer = new SeriesDataBuffer { SeriesId = id };
        buffer.ComponentData[id] = values;
        return new ChartSeries(config, buffer);
    }

    /// <summary>A series the frame renderer will actually draw candles for. <c>LineSeries</c> will
    /// not do: a component of display type Line paints a line, and a test looking for candle bodies
    /// in the frame then finds none for a reason that has nothing to do with what it is asking.</summary>
    private static ChartSeries FrameCandleSeries()
    {
        var config = new SeriesConfig { Id = "candles", Name = "candles", Pane = "Main", IndicatorCode = "CANDLES" };
        config.Components.Add(new ComponentConfig
        {
            Name = "body", DisplayType = ComponentDisplayType.Candle,
            ColorHex = "#123456", ColorHexSecondary = "#654321",
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = "candles" });
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
        int w = W, int h = H)
    {
        var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Black);
            renderer.Render(canvas, w, h, data, series, cursorIndex: data.Count - 1,
                viewportStart: 0, viewportLength: data.Count,
                viewportRange: (95, 115), paneRanges: paneRanges, rightMarginBars: 0);
        }
        return bmp;
    }

    /// <summary>
    /// <b>The plot stops where the price axis begins.</b>
    ///
    /// <para>
    /// The renderer lays bars across <c>width - axisWidth</c> and reserves the column on the right
    /// for labels. Draw the pane across the full width instead and the rightmost bars — the ones a
    /// reader looks at first, because they are the most recent — are painted underneath the
    /// numbers. It also puts the renderer at odds with every pointer-to-data mapping, which
    /// subtracts the same strip; that disagreement is the 2026-08-27 defect where a click on the
    /// last candle resolved six bars early.
    /// </para>
    /// </summary>
    [Fact]
    public void TheMainPaneDoesNotPaintUnderTheYAxisColumn()
    {
        var (renderer, layout) = Renderer();
        var data = Bars(30, _ => (100.0, 110.0, 99.0, 109.0));   // all bullish
        var series = new[] { FrameCandleSeries() };

        using var bmp = RenderFrame(renderer, series, data,
            new Dictionary<string, (double Min, double Max)> { ["Main"] = (95, 115) });

        Assert.InRange(layout.AxisWidthFraction, 0.01f, 0.5f);
        int axisLeft = (int)Math.Round(W * (1f - layout.AxisWidthFraction));

        var bullish = Theme().CandleBullishBody;
        var intruders = new List<int>();
        for (int x = axisLeft + 2; x < W; x++)
        for (int y = 0; y < H; y++)
            if (CloseTo(bmp.GetPixel(x, y), bullish)) { intruders.Add(x); break; }

        Assert.True(intruders.Count == 0,
            $"candle bodies were painted in {intruders.Count} columns of the y-axis strip " +
            $"(from x={intruders.FirstOrDefault()}, strip starts at x={axisLeft}). The plot area is " +
            "width minus the axis column, and the pointer mapping already assumes it.");
    }

    /// <summary>The vacuity twin: the candles ARE drawn, to the left of the strip. Without this,
    /// "nothing in the strip" is satisfied by a renderer that drew nothing anywhere.</summary>
    [Fact]
    public void TheMainPaneDoesPaintToTheLeftOfTheYAxisColumn()
    {
        var (renderer, layout) = Renderer();
        var data = Bars(30, _ => (100.0, 110.0, 99.0, 109.0));

        using var bmp = RenderFrame(renderer, new[] { FrameCandleSeries() },
            data, new Dictionary<string, (double Min, double Max)> { ["Main"] = (95, 115) });

        int axisLeft = (int)Math.Round(W * (1f - layout.AxisWidthFraction));
        var bullish = Theme().CandleBullishBody;

        int painted = 0;
        for (int x = 0; x < axisLeft; x++)
        for (int y = 0; y < H; y++)
            if (CloseTo(bmp.GetPixel(x, y), bullish)) { painted++; break; }

        Assert.True(painted > 5, $"only {painted} plot columns carried a candle body");
    }

    // ── Axis labels ────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Axis labels keep a minimum distance from one another, or a short pane becomes a smear.</b>
    ///
    /// <para>
    /// <c>RenderYAxis</c> skips a label that would land within a text-height of the previous one.
    /// Remove that and a 40-pixel indicator pane stacks its numbers on top of each other. This
    /// mutant survived the 48th pass's rendering work as well, so it is now twice confirmed as
    /// unguarded.
    /// </para>
    ///
    /// <para>
    /// Asserted by finding the ROWS of the axis strip that carry label pixels, grouping them into
    /// runs, and requiring consecutive runs to be separated. Reading the glyphs is unnecessary:
    /// the question is whether two labels overlap, and that is a question about rows.
    /// </para>
    /// </summary>
    [Fact]
    public void AxisLabelsNeverCrowdOnTopOfOneAnother()
    {
        var (renderer, layout) = Renderer();
        var data = Bars(30, _ => (100.0, 110.0, 99.0, 109.0));

        // EIGHT indicator panes on a 300px canvas. Measured 2026-09-17: at three or four panes the
        // labels land about twenty pixels apart, comfortably above the minimum, so the spacing
        // guard never fires and a test built on that layout passes with the guard deleted. At eight
        // panes each pane is under thirty pixels and the guard has to do real work — the smallest
        // gap between label rows is 18 with it and 11 without.
        var series = new List<ChartSeries> { FrameCandleSeries() };
        var ranges = new Dictionary<string, (double Min, double Max)> { ["Main"] = (95, 115) };
        for (int i = 0; i < 8; i++)
        {
            series.Add(LineSeries($"p{i}", $"Pane_{i}", Enumerable.Repeat(500.0, 30).ToArray()));
            ranges[$"Pane_{i}"] = (0, 1000);
        }

        using var bmp = RenderFrame(renderer, series, data, ranges);

        int axisLeft = (int)Math.Round(W * (1f - layout.AxisWidthFraction));
        int plotBottom = (int)Math.Round(H * (1f - layout.AxisHeightFraction));

        // Rows of the axis column carrying any non-background pixel are label rows.
        var labelRow = new bool[plotBottom];
        for (int y = 0; y < plotBottom; y++)
        for (int x = axisLeft + 1; x < W; x++)
        {
            var c = bmp.GetPixel(x, y);
            if (c.Red > 40 || c.Green > 40 || c.Blue > 40) { labelRow[y] = true; break; }
        }

        var runs = new List<int>();
        bool inRun = false;
        for (int y = 0; y < plotBottom; y++)
        {
            if (labelRow[y] && !inRun) { runs.Add(y); inRun = true; }
            else if (!labelRow[y]) inRun = false;
        }

        Assert.True(runs.Count >= 4,
            $"only {runs.Count} label rows were found — the fixture drew too few to judge spacing");

        var tooClose = new List<string>();
        for (int i = 1; i < runs.Count; i++)
            if (runs[i] - runs[i - 1] < 14)
                tooClose.Add($"rows at y={runs[i - 1]} and y={runs[i]}, {runs[i] - runs[i - 1]}px apart");

        Assert.True(tooClose.Count == 0,
            $"{tooClose.Count} pairs of axis labels sit closer together than a line of text:\n  " +
            string.Join("\n  ", tooClose.Take(5)) +
            "\nRenderYAxis skips a label that would land within a text-height of the previous one; " +
            "without it a short indicator pane stacks its numbers into a smear.");
    }

    // ── The y-axis swatch ──────────────────────────────────────────────────────────

    /// <summary>
    /// <b>The live-value swatch is a small tick, not a band across the axis.</b>
    ///
    /// <para>
    /// It once was a band: <c>SKCanvas.DrawRect</c>'s four-float overload is
    /// <c>(x, y, width, height)</c>, and passing BOUNDS to it turned a 4x3 pixel tick into a
    /// rectangle roughly nineteen hundred pixels wide, which buried the price labels behind a solid
    /// colour block. The comment in <c>RenderYAxisSwatches</c> records that; nothing held it.
    /// </para>
    ///
    /// <para>
    /// <b>A note on how this was arrived at.</b> The A2i mutant for this rewrote
    /// <c>DrawRect(SKRect.Create(x, y, w, h))</c> as <c>DrawRect(x, y, w, h)</c> — and those are
    /// byte-identical, measured, so that mutant was equivalent and proved nothing. The defect is
    /// real; the mutant was mis-specified. What reintroduces it is passing bounds, and that is what
    /// this test is written against.
    /// </para>
    /// </summary>
    [Fact]
    public void TheYAxisSwatchIsATickAndNotABandAcrossTheAxis()
    {
        var (renderer, layout) = Renderer();
        var data = Bars(30, _ => (100.0, 110.0, 99.0, 109.0));

        // A main-pane overlay line: RenderYAxisSwatches draws a tick for each of these at its
        // latest value. A distinctive colour so its pixels are unambiguous.
        var swatch = new SKColor(0, 255, 0);
        var series = new[]
        {
            FrameCandleSeries(),
            LineSeries("overlay", "Main", Enumerable.Repeat(105.0, 30).ToArray(), "#00FF00"),
        };

        using var bmp = RenderFrame(renderer, series, data,
            new Dictionary<string, (double Min, double Max)> { ["Main"] = (95, 115) });

        int axisLeft = (int)Math.Round(W * (1f - layout.AxisWidthFraction));

        int widest = 0;
        for (int y = 0; y < H; y++)
        {
            int run = 0;
            for (int x = axisLeft; x < W; x++)
            {
                run = CloseTo(bmp.GetPixel(x, y), swatch) ? run + 1 : 0;
                // Inside the column loop, deliberately: taking the maximum after it only ever sees
                // the run still alive at the last column, which for a 4px tick is zero.
                widest = Math.Max(widest, run);
            }
        }

        Assert.True(widest > 0, "no swatch was drawn in the axis column at all — the fixture proves nothing");
        Assert.True(widest <= 8,
            $"the y-axis swatch is {widest} pixels wide. It is a 4-pixel tick marking where a line " +
            "currently sits; anything wider is a block across the axis, and it buries the price labels.");
    }

    // ── Recorded, not tested ───────────────────────────────────────────────────────

    /// <summary>
    /// <b>Two of the nine A2i survivors are equivalent mutants, and this records the measurement so
    /// the next person does not spend the afternoon proving it again.</b>
    ///
    /// <para>
    /// <c>RenderCandles</c> aligns each bar's centre to a half-pixel —
    /// <c>Math.Floor(xRaw) + 0.5f</c> — with a comment explaining that sub-pixel positions made
    /// Skia's anti-aliasing put the one-pixel wick a fraction off the body's centre. Removing the
    /// alignment changes nothing measurable, because <c>SKPaintPool.Rent()</c> calls
    /// <c>Reset()</c> and <c>SKPaint.IsAntialias</c> defaults to FALSE: measured across twenty
    /// sub-pixel offsets, the wick lands in the identical column every time.
    /// </para>
    ///
    /// <para>
    /// The line is kept. It costs nothing and it would matter the day anti-aliasing is switched on
    /// for these paints, which is a plausible future change. But it cannot be proved red today, and
    /// the honest record is that it is belt and braces rather than the belt.
    /// </para>
    ///
    /// <para>
    /// This test pins the PREMISE rather than the alignment: if candle paints ever become
    /// anti-aliased, it fails, and whoever makes that change is told that the alignment has become
    /// load-bearing and now needs a test of its own.
    /// </para>
    /// </summary>
    [Fact]
    public void TheWickAlignmentIsBeltAndBracesWhileAntiAliasingIsOff()
    {
        using var lease = SKPaintPool.Rent();
        Assert.False(lease.Paint.IsAntialias,
            "a pooled paint now arrives anti-aliased. RenderCandles' half-pixel alignment was " +
            "measured as having no observable effect while anti-aliasing is off; with it on the " +
            "alignment becomes load-bearing and needs a test that asserts wick and body share a " +
            "centre column.");
    }
}
