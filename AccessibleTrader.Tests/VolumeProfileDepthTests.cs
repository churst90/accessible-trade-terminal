using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The volume profile sits under the candles; its POC line sits over them.</b>
///
/// <para>Cody's 2026-10-15 BTCUSDT 1d screenshots: Volume Profile (Visible Range) painted a
/// translucent grey slab, right-aligned and 20% of the pane wide, over the newest three months
/// of candles — the part of the chart a trader most needs to see. <c>ChartRenderer.RenderPane</c>
/// ran the profile layer after every data layer and the formations. The bars are now painted on
/// the pane fill, before the candles, and the point-of-control line after them.</para>
///
/// <para>Measured in pixels through the real <see cref="ChartRenderer"/>: where a profile bar
/// and the newest candle's body overlap, the pixel is the candle's own colour, not a blend.
/// Every built-in theme, because a theme decides both colours.</para>
/// </summary>
public sealed class VolumeProfileDepthTests
{
    private const int W = 400, H = 300, Bars = 40;
    private const double Lo = 90, Hi = 130;

    private static ThemeService Themes(ThemeType type)
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        var themes = new ThemeService(settings);
        themes.SetTheme(type);
        return themes;
    }

    public static IEnumerable<object[]> AllThemes() =>
        Enum.GetValues<ThemeType>().Select(t => new object[] { t });

    /// <summary>
    /// Falling candles (always filled — a hollow-up theme setting cannot leave a see-through body)
    /// with the body between 100 and 120 and the wicks between 97 and 123.
    /// </summary>
    private static List<Ohlcv> FallingBars()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, Bars)
            .Select(i => new Ohlcv(start.AddDays(i), 120, 123, 97, 100, 1000))
            .ToList();
    }

    private static ChartSeries Candles()
    {
        var config = new SeriesConfig { Id = CoreSeriesIds.Candles, Name = "Candles", Pane = "Main", IndicatorCode = "CANDLES" };
        config.Components.Add(new ComponentConfig { Name = "body", DisplayType = ComponentDisplayType.Candle });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = CoreSeriesIds.Candles });
    }

    /// <summary>
    /// One full-volume bin over the candle bodies (102–118), one below every wick (91–95) so the
    /// profile is provably on screen, and a small POC bin at 109–111.
    /// </summary>
    private static ChartSeries Profile()
    {
        var config = new SeriesConfig { Id = "vpvr", Name = "Volume Profile (Visible Range)", Pane = "Main" };
        var series = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "vpvr" }) { IsProfile = true };
        series.ProfileBins = new List<ProfileBin>
        {
            new() { PriceLow = 102, PriceHigh = 118, TotalVolume = 100, TpoPeriodCount = 1, IsPOC = false, IsValueArea = true },
            new() { PriceLow = 91,  PriceHigh = 95,  TotalVolume = 100, TpoPeriodCount = 1, IsPOC = false, IsValueArea = false },
            new() { PriceLow = 109, PriceHigh = 111, TotalVolume = 5,   TpoPeriodCount = 1, IsPOC = true, IsValueArea = true },
        };
        return series;
    }

    private static (SKBitmap Bitmap, float PlotWidth) Render(ThemeService themes, bool withProfile)
    {
        var styling = new StylingService(new ComponentRoleMapper(),
            new Core.Services.Audio.SonificationProfileProvider(), new PaneAssignmentService());
        var renderer = new ChartRenderer(themes, styling, new PaneLayoutService(),
            NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        var series = withProfile ? new[] { Candles(), Profile() } : new[] { Candles() };
        var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
            // Keyboard cursor on the OLDEST bar, well away from the profile strip, so its
            // crosshair cannot be what a pixel in the strip shows.
            renderer.Render(canvas, W, H, FallingBars(), series, cursorIndex: 0,
                viewportStart: 0, viewportLength: Bars, viewportRange: (Lo, Hi),
                paneRanges: new Dictionary<string, (double Min, double Max)>(), rightMarginBars: 0);
        return (bmp, W - themes.Current.AxisWidth);
    }

    /// <summary>Centre of the newest candle's slot: inside the profile strip (the right 20%).</summary>
    private static int NewestCandleX(float plotWidth) => (int)((Bars - 1 + 0.5f) * plotWidth / Bars);

    private static int YOf(double price, SKBitmap bmp, ThemeService themes)
    {
        // The main pane is the only pane; it ends where the x-axis strip starts.
        float bottom = H - themes.Current.AxisHeight;
        return (int)Math.Round(ChartMath.MapY(price, 0, bottom, Lo, Hi, false));
    }

    private static bool Near(SKColor a, SKColor b, int tolerance = 3) =>
        Math.Abs(a.Red - b.Red) <= tolerance && Math.Abs(a.Green - b.Green) <= tolerance
        && Math.Abs(a.Blue - b.Blue) <= tolerance;

    [Theory]
    [MemberData(nameof(AllThemes))]
    public void Where_the_profile_overlaps_the_newest_candle_the_candle_shows_its_own_colour(ThemeType type)
    {
        var themes = Themes(type);
        var (with, plotWidth) = Render(themes, withProfile: true);
        var (without, _) = Render(themes, withProfile: false);
        using var _w = with; using var _o = without;

        int x = NewestCandleX(plotWidth);
        Assert.True(x > plotWidth * 0.8f, $"x {x} is not inside the profile strip (plot {plotWidth}).");

        // Vacuity: the profile is on screen. Below every wick, in the strip, the frame with the
        // profile differs from the frame without it.
        int yBelow = YOf(93, with, themes);
        Assert.False(Near(with.GetPixel(x, yBelow), without.GetPixel(x, yBelow), 0),
            $"{type}: the profile bin at 91–95 left the strip's pixel at ({x},{yBelow}) unchanged; nothing is being tested.");

        // The candle body at 105 (inside the full-volume bin, clear of the POC at 110) shows the
        // same colour it has with no profile at all.
        int yBody = YOf(105, with, themes);
        SKColor bare = without.GetPixel(x, yBody), drawn = with.GetPixel(x, yBody);
        Assert.True(Near(bare, themes.Current.CandleBearishBody, 6),
            $"{type}: the pixel at ({x},{yBody}) is {bare} without a profile, not the falling candle's " +
            $"{themes.Current.CandleBearishBody}; the probe is not on a candle body.");
        Assert.True(Near(drawn, bare),
            $"{type}: with Volume Profile on, the newest candle's body reads {drawn} instead of its own {bare}: " +
            "the profile is painted over the candles (Cody's 2026-10-15 screenshots).");
    }

    [Theory]
    [MemberData(nameof(AllThemes))]
    public void The_point_of_control_line_is_drawn_over_the_candles(ThemeType type)
    {
        // The POC is a level traders read against the candles, like a formation's trigger line.
        var themes = Themes(type);
        var (with, plotWidth) = Render(themes, withProfile: true);
        using var _w = with;

        int x = NewestCandleX(plotWidth);
        int yPoc = YOf(110, with, themes);
        // The 2px line straddles its centre row; either row may carry the full colour.
        bool onTop = Near(with.GetPixel(x, yPoc), themes.Current.ProfilePOC, 6)
                     || Near(with.GetPixel(x, yPoc - 1), themes.Current.ProfilePOC, 6);
        Assert.True(onTop,
            $"{type}: at the POC price on the newest candle ({x},{yPoc}) the pixel is {with.GetPixel(x, yPoc)}, " +
            $"not the POC colour {themes.Current.ProfilePOC}: the candle is drawn over the POC line.");
    }

    /// <summary>
    /// <b>Under the candles the profile is the surface the newest candles sit on</b>, so its
    /// opacity comes straight out of their contrast. At the old opacity (100 of 255) a falling
    /// candle on Blackout went from 4.07:1 against the chart to 2.38:1 against the profile. The
    /// floor here: over the profile a candle still clears the 3:1 graphics floor or keeps at least
    /// 70% of the contrast it has on the bare chart, and the profile is still distinguishable from
    /// the chart (1.15:1) — a faint shape, not nothing. Measured from rendered pixels, every theme.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllThemes))]
    public void The_profile_stays_visible_without_costing_the_candles_their_contrast(ThemeType type)
    {
        var themes = Themes(type);
        var (with, plotWidth) = Render(themes, withProfile: true);
        var (without, _) = Render(themes, withProfile: false);
        using var _w = with; using var _o = without;

        int x = NewestCandleX(plotWidth);
        // Below every wick, in the strip: the chart's own surface, and the profile over it.
        int yBelow = YOf(93, with, themes);
        SKColor chart = without.GetPixel(x, yBelow), profile = with.GetPixel(x, yBelow);

        double shows = WcagContrast.Ratio(profile, chart);
        Assert.True(shows >= 1.15,
            $"{type}: the profile ({profile}) is {WcagContrast.Format(shows)} against the chart ({chart}); it has vanished.");

        foreach (var (name, candle) in new[] { ("rising", themes.Current.CandleBullishBody), ("falling", themes.Current.CandleBearishBody) })
        {
            // Steel Gray's falling candle is under the 3:1 floor on the bare chart already, a
            // recorded product decision (ThemeCoverageTests.SteelGray_fallingCandleIsBelowTheGraphicsFloor_recordedNotFixed);
            // the same exemption applies here and goes with it.
            if (type == ThemeType.SteelGray && name == "falling") continue;
            double bare = WcagContrast.Ratio(candle, chart), over = WcagContrast.Ratio(candle, profile);
            // Still at the graphics floor, or at most 30% of its contrast gone.
            Assert.True(over >= WcagContrast.GraphicsMinimum || over >= bare * 0.70,
                $"{type}: the {name} candle is {WcagContrast.Format(bare)} against the chart but only " +
                $"{WcagContrast.Format(over)} against the volume profile under it — {(1 - over / bare):P0} of its contrast gone.");
        }
    }
}
