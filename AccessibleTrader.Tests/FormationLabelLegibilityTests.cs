using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// Chart-formation labels and lines a low-vision reader can actually read, placed off the candles.
///
/// <para>
/// <b>Contrast.</b> Cody's 2026-10-17 BTCUSDT 1d screenshot (default theme, Classic) carried
/// "ascending triangle trigger 87,219.40, confirms on an upward breakout" in #2962FF on a navy
/// plate at 10px. Measured before the fix: the text was 3.65:1 on its own plate and 2.46:1 where the
/// plate (then 205/255) covered a candle — under WCAG 1.4.3's 4.5:1 — and the dashed measured target
/// at alpha 110 was 1.63:1, under 1.4.11's 3:1 for a graphic. All twelve built-in themes
/// failed at least one pair (HighContrastDark only its span edges, 2.14:1). The ink is now derived per theme
/// (<see cref="FormationInk"/>).
/// </para>
///
/// <para>
/// <b>Placement.</b> The same day's screenshot had "symmetrical triangle trigger 65,415.69, upward
/// breakout" printed across the August breakout candles: a label always went just above its line,
/// at the line's start. It now goes wherever beside or along its line covers the least candle
/// (<see cref="ChartFormationLayer.PlaceLabel"/>).
/// </para>
/// </summary>
public sealed class FormationLabelLegibilityTests : IDisposable
{
    private readonly List<SKSurface> _surfaces = new();

    public void Dispose()
    {
        foreach (var s in _surfaces) s.Dispose();
        _surfaces.Clear();
    }

    public static IEnumerable<object[]> AllThemes() =>
        Enum.GetValues<ThemeType>().Select(t => new object[] { t });

    private static ChartTheme Theme(ThemeType type)
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        var service = new ThemeService(settings);
        service.SetTheme(type);
        return service.Current;
    }

    private static SKColor Over(SKColor fg, SKColor bg)
    {
        double a = fg.Alpha / 255.0;
        return new SKColor(
            (byte)Math.Round(fg.Red * a + bg.Red * (1 - a)),
            (byte)Math.Round(fg.Green * a + bg.Green * (1 - a)),
            (byte)Math.Round(fg.Blue * a + bg.Blue * (1 - a)));
    }

    // ── Contrast: the colours, every theme ───────────────────────────────────────

    /// <summary>
    /// Every built-in theme: label text at 4.5:1 on its plate wherever the plate lands, and every
    /// line at 3:1 on the chart. The surfaces are listed HERE, not borrowed from
    /// <see cref="FormationInk.TextSurfaces"/>, so shrinking the list there cannot make this pass:
    /// the plate (whatever colour and opacity the layer uses) over either end of the chart, either
    /// candle's body and wick, a doji, and another formation's line.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllThemes))]
    public void EveryTheme_drawsFormationTextAndLinesAboveTheWcagFloors(ThemeType type)
    {
        var theme = Theme(type);
        var ink = FormationInk.For(theme);

        var chart = new[] { theme.Background, theme.BackgroundGradientEnd ?? theme.Background }
            .Select(c => c.WithAlpha(255)).ToArray();
        var under = chart.Concat(new[]
        {
            theme.CandleBullishBody, theme.CandleBearishBody, theme.CandleBullishWick,
            theme.CandleBearishWick, theme.CandleDojiBody, theme.Accent,
        }).Select(c => Over(ink.Plate, c.WithAlpha(255))).ToArray();

        var failures = new List<string>();
        void Check(string what, SKColor fg, SKColor[] surfaces, double floor)
        {
            double worst = surfaces.Min(s => WcagContrast.Ratio(fg, s));
            if (worst < floor) failures.Add($"{what} {WcagContrast.Format(worst)} < {WcagContrast.Format(floor)}");
        }

        Check("label text", ink.Text, under, WcagContrast.TextMinimum);
        Check("target label text", ink.TargetText, under, WcagContrast.TextMinimum);
        Check("trigger line", ink.Trigger, chart, WcagContrast.GraphicsMinimum);
        Check("measured target line", ink.Target, chart, WcagContrast.GraphicsMinimum);
        Check("wedge/triangle edge", ink.Boundary, chart, WcagContrast.GraphicsMinimum);
        Check("span edge", ink.SpanEdge, chart, WcagContrast.GraphicsMinimum);

        Assert.True(failures.Count == 0, $"{type}: {string.Join("; ", failures)}");
    }

    /// <summary>
    /// "The visual weight is the disclaimer": the measured target may never be drawn STRONGER than
    /// the trigger. It is the same ink at no more opacity (and, in the layer, thinner and dashed).
    /// </summary>
    [Theory]
    [MemberData(nameof(AllThemes))]
    public void EveryTheme_keepsTheMeasuredTargetNoHeavierThanTheTrigger(ThemeType type)
    {
        var ink = FormationInk.For(Theme(type));
        Assert.Equal(ink.Trigger.WithAlpha(255), ink.Target.WithAlpha(255));
        Assert.True(ink.Target.Alpha <= ink.Trigger.Alpha, $"{type}: target alpha {ink.Target.Alpha} > trigger {ink.Trigger.Alpha}");
    }

    // ── Contrast: the pixels the layer actually paints, every theme ──────────────

    /// <summary>
    /// The colours above are only worth anything if the layer draws with them. This renders one
    /// formation per theme at density 2 (stems two pixels wide, so the core of a glyph is the full
    /// ink, not an antialiased edge) and reads back the most contrasting pixel in the trigger
    /// label's text and on the measured target's line. Skia's own blending can land a unit off the
    /// composite the palette computed, hence the 0.05 allowance — far inside the gap to the old
    /// Classic values (3.65:1 text, 1.63:1 target).
    /// </summary>
    [Theory]
    [MemberData(nameof(AllThemes))]
    public void EveryTheme_paintsTheLabelAndTargetAtTheFloorsOnScreen(ThemeType type)
    {
        const float d = 2f;
        var theme = Theme(type);
        // Flat bars well above both levels, so the label sits where it always did: above its
        // line, at the line's start. Price 50..150 over 1200 px: 100 is row 600, 80 is row 840.
        var bars = Enumerable.Range(0, 200).Select(i => new Ohlcv
        {
            Date = new DateTime(2024, 1, 1).AddDays(i), Open = 142, High = 145, Low = 140, Close = 143, Volume = 1,
        }).ToList();
        var surface = SKSurface.Create(new SKImageInfo(1600, 1200));
        _surfaces.Add(surface);
        surface.Canvas.Clear(theme.Background);
        var ctx = new RenderContext(surface.Canvas, new SKRect(0, 0, 1600, 1200), bars,
            ViewportStart: 0, ViewportLength: 200, Min: 50, Max: 150, IsLogScale: false,
            ItemWidth: 8f, Density: d, PaneName: "Main", LocalCursorIndex: 50, Theme: theme);
        var p = new ChartPattern(ChartPatternKind.DoubleTop, ChartPatternState.Forming, 10, 60, 65, 100,
            DateTime.Today, DateTime.Today, CompletedAtIndex: null, ExpiresAtIndex: 195, BreaksBelow: true,
            MeasuredTarget: 80, SecondaryLevel: null, TargetReachedAtIndex: null);
        new ChartFormationLayer().Render(ctx, new[] { p });

        using var image = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(image);
        double Best(int x0, int x1, int y0, int y1)
        {
            double best = 1;
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    best = Math.Max(best, WcagContrast.Ratio(bmp.GetPixel(x, y), theme.Background));
            return best;
        }

        // The trigger label: baseline 6px over row 600, 20px font; its left end is just right of
        // the span edge at x 80, so start 20px in.
        double text = Best(100, 380, 578, 592);
        // The measured target, well right of its own label, on its row.
        double target = Best(1200, 1500, 837, 843);

        Assert.True(text >= WcagContrast.TextMinimum - 0.05,
            $"{type}: label text on screen is {WcagContrast.Format(text)}");
        Assert.True(target >= WcagContrast.GraphicsMinimum - 0.05,
            $"{type}: measured target on screen is {WcagContrast.Format(target)}");
    }

    /// <summary>
    /// At density 1 the measured target is one pixel wide, and a 1px line centred ON a pixel
    /// boundary (any price that maps to a whole row) is painted as two rows at half strength —
    /// half of a colour chosen to be 3:1 is about 1.8:1 on Classic. The layer moves it onto the
    /// pixel grid; this reads the brightest pixel on that row.
    /// </summary>
    [Fact]
    public void AOnePixelTargetIsDrawnOnThePixelGridAtFullStrength()
    {
        var theme = Theme(ThemeType.Classic);
        var bars = Enumerable.Range(0, 100).Select(i => new Ohlcv
        {
            Date = new DateTime(2024, 1, 1).AddDays(i), Open = 142, High = 145, Low = 140, Close = 143, Volume = 1,
        }).ToList();
        var surface = SKSurface.Create(new SKImageInfo(800, 600));
        _surfaces.Add(surface);
        surface.Canvas.Clear(theme.Background);
        var ctx = new RenderContext(surface.Canvas, new SKRect(0, 0, 800, 600), bars,
            ViewportStart: 0, ViewportLength: 100, Min: 50, Max: 150, IsLogScale: false,
            ItemWidth: 8f, Density: 1f, PaneName: "Main", LocalCursorIndex: 50, Theme: theme);
        // Target 80 maps to row 420 exactly: a pixel BOUNDARY.
        var p = new ChartPattern(ChartPatternKind.DoubleTop, ChartPatternState.Forming, 10, 60, 65, 100,
            DateTime.Today, DateTime.Today, CompletedAtIndex: null, ExpiresAtIndex: 95, BreaksBelow: true,
            MeasuredTarget: 80, SecondaryLevel: null, TargetReachedAtIndex: null);
        new ChartFormationLayer().Render(ctx, new[] { p });

        using var image = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(image);
        double best = 1;
        for (int x = 600; x <= 740; x++)
            for (int y = 417; y <= 423; y++)
                best = Math.Max(best, WcagContrast.Ratio(bmp.GetPixel(x, y), theme.Background));
        Assert.True(best >= WcagContrast.GraphicsMinimum - 0.05, $"the 1px target line is {WcagContrast.Format(best)} on screen");
    }

    // ── Placement: off the candles ────────────────────────────────────────────────

    public static IEnumerable<object[]> CandlesAroundTheLabel() => new[]
    {
        // Candles just ABOVE the line where its label used to go: the label belongs below.
        new object[] { "above the line", 100.4, 104.0 },
        // Candles straddling the line along its first 34 bars: neither side is clear there, so
        // the label has to move along the line to where it is.
        new object[] { "across the line", 96.0, 104.0 },
    };

    /// <summary>
    /// Bars 12..45 are drawn as magenta blocks spanning their high-low, the rest sit far below;
    /// the trigger at 100 runs from bar 10 to bar 195. Under the old rule the label went above the
    /// line at its start — over those blocks. Every block pixel the label's plate or text painted
    /// over is counted, except the rows the trigger line itself crosses.
    /// </summary>
    [Theory]
    [MemberData(nameof(CandlesAroundTheLabel))]
    public void ALabelIsPlacedOffTheCandlesWhenItsLineHasRoom(string where, double low, double high)
    {
        var theme = Theme(ThemeType.Classic);
        var magenta = new SKColor(255, 0, 255);
        var bars = Enumerable.Range(0, 100).Select(i => i is >= 12 and <= 45
            ? new Ohlcv { Date = new DateTime(2024, 1, 1).AddDays(i), Open = low, Low = low, High = high, Close = high, Volume = 1 }
            : new Ohlcv { Date = new DateTime(2024, 1, 1).AddDays(i), Open = 60, Low = 58, High = 64, Close = 62, Volume = 1 })
            .ToList();
        var surface = SKSurface.Create(new SKImageInfo(800, 600));
        _surfaces.Add(surface);
        var canvas = surface.Canvas;
        canvas.Clear(theme.Background);

        // Price 50..150 over 600 px, 8 px per bar; each "candle" a solid block 6 px wide.
        static float Row(double price) => (float)(600 - (price - 50) / 100.0 * 600);
        using (var paint = new SKPaint { Color = magenta, IsAntialias = false })
            for (int i = 12; i <= 45; i++)
                canvas.DrawRect(new SKRect(i * 8 + 1, Row(high), i * 8 + 7, Row(low)), paint);

        var ctx = new RenderContext(canvas, new SKRect(0, 0, 800, 600), bars,
            ViewportStart: 0, ViewportLength: 100, Min: 50, Max: 150, IsLogScale: false,
            ItemWidth: 8f, Density: 1f, PaneName: "Main", LocalCursorIndex: 50, Theme: theme);
        var p = new ChartPattern(ChartPatternKind.DoubleTop, ChartPatternState.Forming, 10, 60, 65, 100,
            DateTime.Today, DateTime.Today, CompletedAtIndex: null, ExpiresAtIndex: 195, BreaksBelow: true,
            MeasuredTarget: null, SecondaryLevel: null, TargetReachedAtIndex: null);
        new ChartFormationLayer().Render(ctx, new[] { p });

        using var image = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(image);
        int lineRow = (int)Row(100), covered = 0, candle = 0;
        for (int i = 12; i <= 45; i++)
            for (int x = i * 8 + 1; x < i * 8 + 7; x++)
                for (int y = (int)Math.Ceiling(Row(high)); y < (int)Row(low); y++)
                {
                    if (Math.Abs(y - lineRow) <= 2) continue;
                    candle++;
                    var c = bmp.GetPixel(x, y);
                    // Still magenta through the span's 9/255 tint; a plate at 245/255 is not.
                    if (!(c.Red > 180 && c.Blue > 180 && c.Green < 80)) covered++;
                }

        Assert.True(candle > 500, $"precondition: {candle} candle pixels to cover");
        Assert.True(covered == 0, $"candles {where}: the label covered {covered} of {candle} candle pixels");
    }

    /// <summary>
    /// With nothing under it, a label stays exactly where the old rule put it — above its line,
    /// at the line's start — so charts that were fine do not reshuffle.
    /// </summary>
    [Fact]
    public void ALabelWithNothingUnderItStaysAboveTheStartOfItsLine()
    {
        var theme = Theme(ThemeType.Classic);
        var bars = Enumerable.Range(0, 100).Select(i => new Ohlcv
        {
            Date = new DateTime(2024, 1, 1).AddDays(i), Open = 60, Low = 58, High = 64, Close = 62, Volume = 1,
        }).ToList();
        using var surface = SKSurface.Create(new SKImageInfo(800, 600));
        var ctx = new RenderContext(surface.Canvas, new SKRect(0, 0, 800, 600), bars,
            ViewportStart: 0, ViewportLength: 100, Min: 50, Max: 150, IsLogScale: false,
            ItemWidth: 8f, Density: 1f, PaneName: "Main", LocalCursorIndex: 50, Theme: theme);

        var (x, row) = ChartFormationLayer.PlaceLabel(ctx, new List<float>(), y: 300, x: 84, xLineEnd: 700,
            width: 200, fontSize: 10, pad: 3);

        Assert.Equal(84f, x);
        Assert.Equal(297f, row);
    }

    /// <summary>
    /// The cost the placement minimises: a candle's body and wicks inside the rect, in square
    /// pixels, from the bars as the renderer lays them out (viewport-local indexes).
    /// </summary>
    [Fact]
    public void PriceInkUnderCountsTheBodyAndWicksInsideARect()
    {
        var theme = Theme(ThemeType.Classic);
        // One bar at index 5: body 100..110, wicks to 90 and 120. Price 0..600 over 600 px: 1 px per unit.
        var bars = Enumerable.Range(0, 10).Select(i => i == 5
            ? new Ohlcv { Date = DateTime.Today.AddDays(i), Open = 100, Close = 110, Low = 90, High = 120, Volume = 1 }
            : new Ohlcv { Date = DateTime.Today.AddDays(i), Open = 500, Close = 500, Low = 500, High = 500, Volume = 1 })
            .ToList();
        using var surface = SKSurface.Create(new SKImageInfo(100, 600));
        var ctx = new RenderContext(surface.Canvas, new SKRect(0, 0, 100, 600), bars,
            ViewportStart: 0, ViewportLength: 10, Min: 0, Max: 600, IsLogScale: false,
            ItemWidth: 10f, Density: 1f, PaneName: "Main", LocalCursorIndex: 0, Theme: theme);

        // Bar 5's centre is x 55, body 8 px wide (51..59), rows 490..500; wicks 480..490 and 500..510.
        Assert.Equal(0f, ChartFormationLayer.PriceInkUnder(ctx, new SKRect(0, 300, 100, 400)));
        float whole = ChartFormationLayer.PriceInkUnder(ctx, new SKRect(45, 470, 65, 520));
        Assert.Equal(8 * 10 + 10 + 10, whole, 0.01);
    }
}
