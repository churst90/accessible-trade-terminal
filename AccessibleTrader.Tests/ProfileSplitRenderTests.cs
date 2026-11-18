using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A split profile row is drawn in two parts, buying from the base side.</b> Buying is the
/// theme's candle-bullish colour (blue under the colour-vision-safe override) and selling the
/// bearish, both at the profile's existing opacity. A row with no split still draws in
/// ProfileNormal exactly as before.
/// </summary>
public sealed class ProfileSplitRenderTests
{
    private const int W = 200, H = 200, Bars = 1;
    private const double Lo = 90, Hi = 130;

    private static SKColor OverBlack(SKColor c) => new(
        (byte)(c.Red * c.Alpha / 255),
        (byte)(c.Green * c.Alpha / 255),
        (byte)(c.Blue * c.Alpha / 255));

    private static bool Near(SKColor a, SKColor b, int tolerance = 4) =>
        Math.Abs(a.Red - b.Red) <= tolerance
        && Math.Abs(a.Green - b.Green) <= tolerance
        && Math.Abs(a.Blue - b.Blue) <= tolerance;

    private static SKBitmap Render(ProfileBin bin, ChartTheme theme)
    {
        var cfg = new SeriesConfig { Id = "vp", Name = "vp", Pane = "Main" };
        var series = new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = "vp" }) { IsProfile = true };
        series.ProfileBins = new List<ProfileBin> { bin };

        var bars = Enumerable.Range(0, Bars)
            .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddDays(i), 100, 101, 99, 100, 1))
            .ToList();
        var bmp = new SKBitmap(W, H);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        var rect = SKRect.Create(0, 0, W, H);
        var ctx = new RenderContext(canvas, rect, bars, 0, Bars, Lo, Hi, false, W, 1f,
            "Main", -1, theme);
        new ProfileRenderLayer().RenderBins(ctx, new[] { series });
        return bmp;
    }

    private static ProfileBin SplitBin(double buy = 60, double sell = 40) => new()
    {
        PriceLow = 100, PriceHigh = 110, TotalVolume = 100,
        BuyVolume = buy, SellVolume = sell,
        TpoPeriodCount = 0, IsPOC = false, IsValueArea = true,
    };

    private static SKColor RightPixel(SKBitmap bmp) =>
        bmp.GetPixel(W - 5, (int)ChartMath.MapY(105, 0, H, Lo, Hi, false));

    private static SKColor LeftPixel(SKBitmap bmp) =>
        bmp.GetPixel(W - (int)(W * 0.2) + 5, (int)ChartMath.MapY(105, 0, H, Lo, Hi, false));

    [Fact]
    public void Buying_is_painted_from_the_right_base_and_selling_to_its_left()
    {
        var theme = BaseThemeResolver.Resolve(ThemeType.Classic);
        var (buy, sell) = ProfileRenderLayer.SplitColors(theme);
        using var bmp = Render(SplitBin(), theme);

        Assert.True(Near(RightPixel(bmp), OverBlack(buy)),
            $"the base-side pixel is {RightPixel(bmp)}, expected the buying colour {OverBlack(buy)}");
        Assert.True(Near(LeftPixel(bmp), OverBlack(sell)),
            $"the far-side pixel is {LeftPixel(bmp)}, expected the selling colour {OverBlack(sell)}");
        Assert.False(Near(RightPixel(bmp), LeftPixel(bmp), 2), "the two halves are one colour");
    }

    [Fact]
    public void The_colour_vision_safe_theme_paints_the_theme_s_blue_and_orange()
    {
        var theme = BaseThemeResolver.Resolve(ThemeType.Classic) with { ColorVisionSafe = true };
        var (buy, sell) = ProfileRenderLayer.SplitColors(theme);
        Assert.Equal(StandardRenderers.ColorVisionUp.WithAlpha(ProfileRenderLayer.BinAlpha), buy);
        Assert.Equal(StandardRenderers.ColorVisionDown.WithAlpha(ProfileRenderLayer.BinAlpha), sell);

        using var bmp = Render(SplitBin(), theme);
        Assert.True(Near(RightPixel(bmp), OverBlack(buy)));
        Assert.True(Near(LeftPixel(bmp), OverBlack(sell)));
    }

    [Fact]
    public void A_row_with_no_split_draws_in_one_colour_as_before()
    {
        var theme = BaseThemeResolver.Resolve(ThemeType.Classic);
        using var bmp = Render(SplitBin(buy: 0, sell: 0), theme);
        SKColor expected = OverBlack(ProfileRenderLayer.ThemeNormal(theme));

        Assert.True(Near(RightPixel(bmp), expected),
            $"no-split row base pixel {RightPixel(bmp)}, expected ProfileNormal {expected}");
        Assert.True(Near(LeftPixel(bmp), expected),
            $"no-split row far pixel {LeftPixel(bmp)}, expected ProfileNormal {expected}");
    }
}
