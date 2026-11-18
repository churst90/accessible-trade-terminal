using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The word is what is drawn.</b> Every bar and heatmap colour word is checked against the
/// pixels the real <see cref="DataLayer"/> and <see cref="HeatmapRenderer"/> paint, not against
/// the flags that asked for the colour: a rule colour, a recolour and volume's candle-direction
/// pair all go through the same resolver speech calls.
/// </summary>
public sealed class BarAndHeatmapColourAgreementTests
{
    private const int W = 120, H = 120;

    private static ChartTheme Theme()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<Newtonsoft.Json.Linq.JToken?>())
            .Returns((Newtonsoft.Json.Linq.JToken?)null);
        return new ThemeService(settings).Current;
    }

    private static ChartSeries One(ComponentConfig comp, double[] data, string id = "x")
    {
        var cfg = new SeriesConfig { Id = id, Name = id, Pane = "Main", IsVisible = true };
        cfg.Components.Add(comp);
        var buf = new SeriesDataBuffer { SeriesId = id };
        buf.ComponentData[comp.Name] = data;
        return new ChartSeries(cfg, buf);
    }

    private static (SKColor Colour, int Pixels) PaintedBar(ChartSeries series, ChartTheme theme, Ohlcv bar)
    {
        using var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Black);
            var r = SKRect.Create(0, 0, W, H);
            var ctx = new RenderContext(canvas, r, new[] { bar }, 0, 1, -100, 100, false,
                W, 1f, "Main", 0, theme);
            var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(),
                new PaneAssignmentService());
            new DataLayer(styling).Render(ctx, new[] { series });
        }

        var counts = new Dictionary<SKColor, int>();
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            var p = bmp.GetPixel(x, y);
            if (p.Red < 6 && p.Green < 6 && p.Blue < 6) continue;
            counts[p] = counts.GetValueOrDefault(p) + 1;
        }
        if (counts.Count == 0) return (SKColors.Black, 0);
        var top = counts.MaxBy(kv => kv.Value);
        return (top.Key, top.Value);
    }

    private static List<(string Name, ChartSeries Series, ComponentConfig Comp, Ohlcv Bar)> BarCases()
    {
        var up = new Ohlcv(new DateTime(2026, 1, 1), 100, 101, 99, 101, 1000);
        var down = new Ohlcv(new DateTime(2026, 1, 2), 101, 102, 99, 100, 1000);
        var cases = new List<(string, ChartSeries, ComponentConfig, Ohlcv)>();

        var green = new ComponentConfig
        {
            Name = "Histogram", DisplayType = ComponentDisplayType.Histogram, Role = ComponentRole.Histogram,
            IsVisible = true, IsEnabled = true, ColorHex = "#FF00E676", ColorHexSecondary = "#FFFF1744",
            ColorSource = ColorSource.Value, ReferenceLevel = 0.0, Thickness = 1f,
        };
        var red = new ComponentConfig
        {
            Name = "Histogram", DisplayType = ComponentDisplayType.Histogram, Role = ComponentRole.Histogram,
            IsVisible = true, IsEnabled = true, ColorHex = "#FF00E676", ColorHexSecondary = "#FFFF1744",
            ColorSource = ColorSource.Value, ReferenceLevel = 0.0, Thickness = 1f,
        };
        var ruled = new ComponentConfig
        {
            Name = "Histogram", DisplayType = ComponentDisplayType.Histogram, Role = ComponentRole.Histogram,
            IsVisible = true, IsEnabled = true, ColorHex = "#FFFFFFFF", ColorHexSecondary = "#FFFFFFFF",
            ColorSource = ColorSource.Value, ReferenceLevel = 0.0, Thickness = 1f,
        };
        ruled.ColorRules.Add(new ColorRule { Condition = ColorCondition.AboveZero, ColorHex = "#FF2196F3" });
        ruled.ColorRules.Add(new ColorRule { Condition = ColorCondition.BelowZero, ColorHex = "#FFFFD700" });

        var volumeUp = new ComponentConfig
        {
            Name = "Volume", DisplayType = ComponentDisplayType.Bar, Role = ComponentRole.Volume,
            IsVisible = true, IsEnabled = true, ColorHex = "#FF00E676", ColorHexSecondary = "#FFFF1744",
            ColorSource = ColorSource.PriceAction, Thickness = 1f,
        };
        var volumeDown = new ComponentConfig
        {
            Name = "Volume", DisplayType = ComponentDisplayType.Bar, Role = ComponentRole.Volume,
            IsVisible = true, IsEnabled = true, ColorHex = "#FF00E676", ColorHexSecondary = "#FFFF1744",
            ColorSource = ColorSource.PriceAction, Thickness = 1f,
        };

        cases.Add(("plain up", One(green, new[] { 50.0 }), green, up));
        cases.Add(("plain down", One(red, new[] { -50.0 }), red, up));
        cases.Add(("colour rule above", One(ruled, new[] { 50.0 }), ruled, up));
        cases.Add(("colour rule below", One(ruled, new[] { -50.0 }), ruled, up));
        cases.Add(("volume up candle", One(volumeUp, new[] { 50.0 }), volumeUp, up));
        cases.Add(("volume down candle", One(volumeDown, new[] { 50.0 }), volumeDown, down));
        return cases;
    }

    [Fact]
    public void Every_bar_colour_word_is_the_colour_painted()
    {
        var theme = Theme();
        foreach (var (name, series, comp, bar) in BarCases())
        {
            var (painted, pixels) = PaintedBar(series, theme, bar);
            Assert.True(pixels > 100, $"{name}: almost nothing was painted ({pixels} px)");
            string said = MarkerLook.DescribeBar(theme, series, comp, 0, bar);
            Assert.Equal($"{MarkerLook.ColorName(painted)} bar", said);
        }
    }

    [Fact]
    public void A_recoloured_histogram_agrees_with_its_new_pixels()
    {
        var theme = Theme();
        var comp = new ComponentConfig
        {
            Name = "Histogram", DisplayType = ComponentDisplayType.Histogram, Role = ComponentRole.Histogram,
            IsVisible = true, IsEnabled = true, ColorHex = "#FF00E676", ColorHexSecondary = "#FFFF1744",
            ColorSource = ColorSource.Value, ReferenceLevel = 0.0, Thickness = 1f,
        };
        var series = One(comp, new[] { 50.0 });
        var bar = new Ohlcv(new DateTime(2026, 1, 1), 100, 101, 99, 101, 1000);

        var (first, _) = PaintedBar(series, theme, bar);
        Assert.Equal($"{MarkerLook.ColorName(first)} bar", MarkerLook.DescribeBar(theme, series, comp, 0, bar));

        comp.ColorHex = "#FF9C27B0";
        var (second, _) = PaintedBar(series, theme, bar);
        Assert.NotEqual(first, second);
        Assert.Equal($"{MarkerLook.ColorName(second)} bar", MarkerLook.DescribeBar(theme, series, comp, 0, bar));
    }

    private static int PaintedRed(ChartSeries series, ChartTheme theme)
    {
        using var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Black);
            var r = SKRect.Create(0, 0, W, H);
            var bars = new[] { new Ohlcv(new DateTime(2026, 1, 1), 100, 101, 99, 100, 1) };
            var ctx = new RenderContext(canvas, r, bars, 0, 1, 90, 130, false,
                W, 1f, "Main", 0, theme);
            new HeatmapRenderer().Render(ctx, series);
        }
        return bmp.GetPixel(W / 2, (int)ChartMath.MapY(105, 0, H, 90, 130, false)).Red;
    }

    private static ChartSeries Heatmap(double volume)
    {
        var cfg = new SeriesConfig { Id = "hm", Name = "hm", Pane = "Main", IsVisible = true, Volume = 1f };
        cfg.Components.Add(new ComponentConfig { Name = "hm", DisplayType = ComponentDisplayType.Heatmap, IsVisible = true });
        var buf = new SeriesDataBuffer
        {
            SeriesId = "hm",
            HeatmapData = new List<List<ProfileBin>>
            {
                new()
                {
                    new ProfileBin
                    {
                        PriceLow = 100, PriceHigh = 110, TotalVolume = volume,
                        TpoPeriodCount = 0, IsPOC = false, IsValueArea = false,
                    },
                },
            },
        };
        return new ChartSeries(cfg, buf);
    }

    [Theory]
    [InlineData(10.0)]   // alpha 25: bottom third
    [InlineData(50.0)]   // alpha 127: middle third
    [InlineData(90.0)]   // alpha 229: top third
    [InlineData(0.0)]    // nothing painted
    public void Every_heatmap_shade_word_is_the_alpha_painted(double volume)
    {
        var theme = Theme();
        var series = Heatmap(volume);
        int red = PaintedRed(series, theme);
        string said = HeatmapRenderer.CellShade(series, series.HeatmapData[0][0]);
        Assert.Equal(HeatmapRenderer.ShadeWord((byte)red), said);
        if (volume == 0) Assert.Equal("", said);
    }
}
