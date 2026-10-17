using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A replaced level is drawn as two levels, with no stroke between them.</b>
///
/// <para>
/// Cipher SR's zone line now holds a value on the bar a new level takes over (it used to be
/// empty there, and the narrator called every replacement a break — CipherSrSpeechTests). An
/// unbroken line through that bar would be drawn as a slanted stroke from the old level to the
/// new one: a price path that never existed. The provider marks the bar in
/// <c>"{zone}_replaced"</c> and the line renderer lifts the pen there. Read from the real
/// <see cref="ChartRenderer"/>: between the last bar of the old level and the first of the new,
/// at heights strictly between the two, nothing may be painted.
/// </para>
/// </summary>
public sealed class CipherSrZoneLineRenderTests
{
    private const int W = 340, H = 300, Bars = 30;
    private const double Old = -40, New = 60;
    private const int Step = 15;   // first bar of the new level

    private static ThemeService Themes()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings);
    }

    private static StylingService Styling() =>
        new(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());

    private static float _plotTop;

    private static SKBitmap Render(bool drawLine, ThemeService themes)
    {
        var cfg = new SeriesConfig { Id = "sr", Name = "Cipher SR", FriendlyName = "Cipher SR", Pane = "Main", IndicatorCode = "CIPHER_SR", IsVisible = true };
        var buf = new SeriesDataBuffer { SeriesId = "sr" };
        cfg.Components.Add(new ComponentConfig
        {
            Name = CipherSrProvider.CompResistanceLine, DisplayName = CipherSrProvider.CompResistanceLine,
            DisplayType = ComponentDisplayType.Line, IsVisible = true, IsEnabled = true, IsZoneLine = true,
            ColorHex = "#FFCE93D8", Thickness = 2f,
        });
        buf.ComponentData[CipherSrProvider.CompResistanceLine] = Enumerable.Range(0, Bars)
            .Select(i => !drawLine ? double.NaN : i < Step ? Old : New).ToArray();
        buf.ComponentData[CipherSrProvider.CompResistanceReplaced] = Enumerable.Range(0, Bars)
            .Select(i => i == Step ? Old : double.NaN).ToArray();
        var series = new ChartSeries(cfg, buf);

        var layout = new PaneLayoutService();
        var renderer = new ChartRenderer(themes, Styling(), layout, NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = Enumerable.Range(0, Bars).Select(i => new Ohlcv(start.AddDays(i), 10, 11, 9, 10, 1)).ToList();
        var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, W, H, bars, new[] { series }, cursorIndex: -1, viewportStart: 0, viewportLength: Bars,
                viewportRange: (-100, 100), paneRanges: new Dictionary<string, (double, double)>(), rightMarginBars: 0);
        _plotTop = layout.StatusBands.TryGetValue("Main", out var band) ? band.HeightFraction * H : 0f;
        return bmp;
    }

    [Fact]
    public void TheBarALevelIsReplacedOnIsNotJoinedToTheOldLevel()
    {
        var themes = Themes();
        using var with = Render(drawLine: true, themes);
        using var without = Render(drawLine: false, themes);

        float pane = W - themes.Current.AxisWidth;
        float bw = pane / Bars;
        int Y(double v) => (int)Math.Round(ChartMath.MapY(v, _plotTop, H - themes.Current.AxisHeight, -100, 100, false));
        int xOld = (int)((Step - 1) * bw + bw / 2), xNew = (int)(Step * bw + bw / 2);
        int yTop = Y(New) + 4, yBottom = Y(Old) - 4;   // strictly between the two levels

        var painted = new List<string>();
        for (int x = xOld + 1; x < xNew; x++)
            for (int y = yTop; y <= yBottom; y++)
                if (with.GetPixel(x, y) != without.GetPixel(x, y)) painted.Add($"({x},{y})");

        // Vacuity: the two levels themselves are drawn.
        bool oldDrawn = Enumerable.Range(Y(Old) - 2, 5).Any(y => with.GetPixel(xOld - 2, y) != without.GetPixel(xOld - 2, y));
        bool newDrawn = Enumerable.Range(Y(New) - 2, 5).Any(y => with.GetPixel(xNew + 2, y) != without.GetPixel(xNew + 2, y));
        Assert.True(oldDrawn && newDrawn, $"the levels were not drawn (old {oldDrawn}, new {newDrawn}) — the probe misses the line");

        Assert.True(painted.Count == 0,
            $"{painted.Count} pixels painted between the old level ({Old}) and its replacement ({New}) across the " +
            $"bar the level was replaced on, e.g. {string.Join(" ", painted.Take(6))} — a slanted stroke joins two levels " +
            "that were never connected by price.");
    }
}
