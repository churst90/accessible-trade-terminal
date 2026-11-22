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

/// <summary>
/// <b>Cipher A's marks over the highest candles are not hidden under the legend</b> (Cody's
/// BTCUSDT 1d screenshot, 2026-11-22: "fix the ... legend issues with cipher a"). The marks stack
/// above the candle (<see cref="StandardRenderers.MarkerY"/>); over a candle at the top of the
/// range the stack rose past the plot's top into the status band, where the DOM legend covers it.
/// The price pane now keeps room between its band and its values for what the marks on screen
/// need, and none when no mark reaches that high.
/// </summary>
public sealed class MarkHeadroomTests
{
    private const int N = 40;
    private static readonly ChartIdentity Btc = new("Spot", "Bitstamp", "BTCUSDT", "1d");

    private static List<Ohlcv> Bars()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, N).Select(i => new Ohlcv(start.AddDays(i), 105, 106, 104, 105.5, 1000)).ToList();
    }

    private static ThemeService Themes()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings);
    }

    private static readonly string[] Symbols =
    {
        CipherAProvider.CompRedX, CipherAProvider.CompRedDiamond, CipherAProvider.CompBloodDiamond,
        CipherAProvider.CompYellowDiamond, CipherAProvider.CompGreenDot, CipherAProvider.CompBlueTriangle,
    };

    /// <summary>Cipher A as the real factory builds it, with <paramref name="on"/> marked on every bar (at the bar's high).</summary>
    private static ChartSeries CipherA(params string[] on)
    {
        var f = new IndicatorModelFactory(new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
            new MockIndicatorPreferencesService());
        var meta = new CipherAProvider().GetIndicators().Single();
        var s = f.CreateSeriesFromMetadata(meta, meta.Name, meta.DefaultPane, new(), null);
        var bars = Bars();
        foreach (var c in s.Components)
            s.Data.ComponentData[c.Name] = bars.Select(b => on.Contains(c.Name) ? b.High : double.NaN).ToArray();
        return s;
    }

    private static (SKBitmap Bmp, PaneLayoutService Layout) Render(ChartSeries cipher, int w = 1000, int h = 610, (double, double)? range = null)
    {
        var layout = new PaneLayoutService();
        var renderer = new ChartRenderer(Themes(), new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
            layout, NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, w, h, Bars(), new[] { cipher }, N - 1, 0, N, range ?? (95, 106),
                new Dictionary<string, (double Min, double Max)>(), density: 1f, rightMarginBars: 0, identity: Btc);
        // Set AT_STATUSBAND_PNG_DIR to keep each render as a PNG for a human to look at.
        var dir = Environment.GetEnvironmentVariable("AT_STATUSBAND_PNG_DIR");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            using var img = SKImage.FromBitmap(bmp);
            using var png = img.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(dir, $"markheadroom_{cipher.Data.ComponentData.Values.Count(d => d.Any(v => !double.IsNaN(v)))}_{range?.Item2 ?? 106}.png"), png.ToArray());
        }
        return (bmp, layout);
    }

    /// <summary>
    /// Three symbols on every candle, the candles' highs at the top of the range: the stack is
    /// drawn whole, as many pixels of its colours as on the same chart with plenty of room
    /// over the candles, and not one pixel of it in the legend's rows. Before 2026-11-22 the stack was cut off at
    /// the plot's top, under the legend: none of it was drawn here.
    /// </summary>
    [Fact]
    public void A_stack_of_marks_over_the_highest_candle_is_drawn_whole_below_the_legend()
    {
        var on = new[] { CipherAProvider.CompRedX, CipherAProvider.CompRedDiamond, CipherAProvider.CompGreenDot };
        (int Ink, List<string> Under) Marks((double, double) range)
        {
            var cipher = CipherA(on);
            var colours = cipher.Components.Where(c => on.Contains(c.Name)).Select(c => SKColor.Parse(c.ColorHex)).ToList();
            bool Mark(SKColor p) => colours.Any(c => Math.Abs(p.Red - c.Red) <= 12 && Math.Abs(p.Green - c.Green) <= 12 && Math.Abs(p.Blue - c.Blue) <= 12);
            var (bmp, layout) = Render(cipher, range: range);
            using (bmp)
            {
                var (line, pad, _) = ChartStatusLine.BandMetrics(1f, 1f);
                int lines = layout.StatusBands["Main"].Lines;
                int blockTop = (int)Math.Floor(pad), blockBottom = (int)Math.Ceiling(pad + lines * line);
                int plotRight = (int)(bmp.Width * (1f - layout.AxisWidthFraction)) - 2;
                int bottom = (int)(bmp.Height * (1f - layout.AxisHeightFraction));
                var under = new List<string>();
                int ink = 0;
                for (int y = 0; y < bottom; y++)
                    for (int x = 1; x < plotRight; x++)
                    {
                        if (!Mark(bmp.GetPixel(x, y))) continue;
                        if (y >= blockTop && y < blockBottom) under.Add($"({x},{y})");
                        ink++;
                    }
                return (ink, under);
            }
        }
        var (roomy, _) = Marks((90, 140));
        var (tight, under) = Marks((90, 106));
        Assert.True(roomy > 2000, $"fixture: Cipher A's marks are not drawn ({roomy} pixels)");
        Assert.True(under.Count == 0,
            $"{under.Count} pixels of Cipher A's marks are in the legend's rows, where the DOM text covers them: {string.Join(" ", under.Take(5))}");
        Assert.True(Math.Abs(tight - roomy) <= roomy * 0.03,
            $"the stack over the highest candles is not drawn whole: {tight} pixels against {roomy} with room over the candles");
    }

    /// <summary>
    /// The room is only kept when a mark needs it: no marks, or marks over candles well below the
    /// top of the range, leave the band exactly the legend's block; the marks at the top get more.
    /// And it is part of the published band, so the pointer reads the values where they are drawn.
    /// </summary>
    [Fact]
    public void The_room_is_kept_only_when_a_mark_reaches_the_top()
    {
        var (line, pad, _) = ChartStatusLine.BandMetrics(1f, 1f);
        float Band(PaneLayoutService l) => l.StatusBands["Main"].HeightFraction * 610;
        float Block(PaneLayoutService l) => pad + l.StatusBands["Main"].Lines * line + pad;

        var (b1, none) = Render(CipherA());
        var (b2, low) = Render(CipherA(Symbols), range: (95, 200));
        var (b3, high) = Render(CipherA(Symbols));
        b1.Dispose(); b2.Dispose(); b3.Dispose();

        Assert.Equal(Block(none), Band(none), 2);
        Assert.Equal(Block(low), Band(low), 2);
        Assert.True(Band(high) > Block(high) + 10, $"no room kept over the highest candles: band {Band(high)}, block {Block(high)}");
        Assert.True(Band(high) - Block(high) <= (610 - 610 * high.AxisHeightFraction) * 0.25f + 0.5f, "more than a quarter of the pane kept");
    }
}

/// <summary>
/// The renderer publishes where it drew each time-axis tick label (<see cref="IPaneLayoutService.TimeAxisLabels"/>),
/// which ChartArea covers under the hover date: each published span is a label on the image, ink
/// in the axis strip across it, and blank axis just past its ends.
/// </summary>
public sealed class TimeAxisLabelPublishingTests
{
    [Fact]
    public void Each_published_span_is_a_label_drawn_on_the_axis()
    {
        const int w = 1000, h = 610, n = 120;
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = Enumerable.Range(0, n).Select(i => new Ohlcv(start.AddDays(i), 100, 106, 99, 105, 1000)).ToList();
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        var layout = new PaneLayoutService();
        var renderer = new ChartRenderer(new ThemeService(settings),
            new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
            layout, NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, w, h, bars, Array.Empty<ChartSeries>(), -1, 0, n, (95, 110),
                new Dictionary<string, (double Min, double Max)>(), density: 1f, rightMarginBars: 0);

        var labels = layout.TimeAxisLabels;
        Assert.True(labels.Count >= 3, $"only {labels.Count} labels published");
        float plotW = w * (1f - layout.AxisWidthFraction);
        int top = (int)(h * (1f - layout.AxisHeightFraction)) + 2;
        var bg = bmp.GetPixel(1, h - 2);
        int Ink(int x0, int x1)
        {
            int ink = 0;
            for (int x = Math.Max(0, x0); x < Math.Min((int)plotW, x1); x++)
                for (int y = top; y < h; y++)
                    if (bmp.GetPixel(x, y) != bg) ink++;
            return ink;
        }
        foreach (var (l, r) in labels)
        {
            Assert.InRange(l, 0f, 1f);
            Assert.InRange(r, l, 1f);
            int lx = (int)Math.Round(l * plotW), rx = (int)Math.Round(r * plotW);
            Assert.True(Ink(lx + 1, rx - 1) > 20, $"no label drawn at {lx}-{rx}");
            Assert.True(Ink(lx - 6, lx - 2) + Ink(rx + 2, rx + 6) == 0, $"the label at {lx}-{rx} reaches past its published span");
        }
    }
}
