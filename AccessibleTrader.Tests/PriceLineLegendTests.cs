using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
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
/// The white line that hugs the closes on the price pane, and the legend that did not name it.
///
/// <para>
/// Cody's 2026-10-15 screenshots (BTCUSDT 1d, focus mode and normal) show a thin white line
/// running through every close, on top of the candles, and the price pane's legend listing only
/// "Market Structure — 4 marks" (or, in focus mode, no legend at all). A sighted companion asked
/// "what is the white line?" had nothing on screen to answer with.
/// </para>
///
/// <para>
/// It is the <b>Price</b> series (<see cref="CoreSeriesIds.Price"/>, indicator code PRICE): a core
/// series seeded on EVERY OHLCV chart alongside Candles and Volume
/// (<c>WorkspaceInitializer.ResolveDesiredCoreStack</c>), one Line component mapped to the close,
/// default colour #FFFFFF (<c>CoreIndicatorProvider</c>). It is not an accidental duplicate draw:
/// it is a navigable series with its own speech ("Price. line. 82,366.") and sonification, so
/// dropping it would change what Cody hears. What was wrong is that the price-pane legend
/// filtered it out alongside Candles, Volume and the heatmap — a line drawn over the candles in
/// white with no key. The first test below demonstrates the line on a rendered frame; the second
/// pins that the legend now names it.
/// </para>
/// </summary>
public sealed class PriceLineLegendTests
{
    private const int W = 600, H = 400;

    private static ThemeService Themes()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings);
    }

    private static StylingService Styling() =>
        new(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());

    /// <summary>The core series exactly as a fresh OHLCV chart builds them — from the real metadata.</summary>
    private static ChartSeries Core(string code, string id)
    {
        var meta = new CoreIndicatorProvider().GetIndicators().Single(m => m.Code == code);
        var factory = new IndicatorModelFactory(Styling(), new MockIndicatorPreferencesService());
        return factory.CreateSeriesFromMetadata(meta, meta.Name, PaneAssignmentService.PaneFor(meta),
            new List<(string, string)>(), null, id);
    }

    /// <summary>A gentle zig-zag of daily bars, so the close line has a shape to follow.</summary>
    private static List<Ohlcv> Bars(int n = 60)
    {
        var start = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>(n);
        for (int i = 0; i < n; i++)
        {
            double close = 100 + 12 * Math.Sin(i / 4.0);
            double open = close + (i % 2 == 0 ? -3 : 3);
            bars.Add(new Ohlcv(start.AddDays(i), open, Math.Max(open, close) + 2, Math.Min(open, close) - 2, close, 1000));
        }
        return bars;
    }

    /// <summary>Feeds each component the column its DataMapping names, as the store's sync does.</summary>
    private static void Fill(ChartSeries s, IReadOnlyList<Ohlcv> bars)
    {
        foreach (var c in s.Components)
        {
            double[] col = c.DataMapping switch
            {
                "high" => bars.Select(b => b.High).ToArray(),
                "low" => bars.Select(b => b.Low).ToArray(),
                "close" => bars.Select(b => b.Close).ToArray(),
                "volume" => bars.Select(b => b.Volume).ToArray(),
                _ => Array.Empty<double>(),
            };
            s.Data.ComponentData[c.Name] = col;
        }
    }

    private static SKBitmap Render(IReadOnlyList<ChartSeries> series, IReadOnlyList<Ohlcv> bars)
    {
        var renderer = new ChartRenderer(Themes(), Styling(), new PaneLayoutService(),
            NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, W, H, bars, series, cursorIndex: -1,
                viewportStart: 0, viewportLength: bars.Count, viewportRange: (80, 120),
                paneRanges: new Dictionary<string, (double Min, double Max)>(), rightMarginBars: 0);
        return bmp;
    }

    private static bool NearWhite(SKColor c) => c.Red > 190 && c.Green > 190 && c.Blue > 190;

    /// <summary>
    /// Bar columns (each bar's centre, ±1px) carrying a near-white pixel anywhere in the price
    /// pane. A polyline through the closes lights nearly every one. Columns under the top-left
    /// legend corner are skipped, because once the legend names the line its label is white too.
    /// </summary>
    private static int ColumnsWithWhite(SKBitmap bmp, IReadOnlyList<Ohlcv> bars)
    {
        float plotRight = W - Themes().Current.AxisWidth;   // the renderer reserves exactly this for the price axis
        float barW = plotRight / bars.Count;

        int hits = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            int x = (int)(i * barW + barW / 2f);
            if (x < 130) continue;
            bool white = false;
            for (int y = 0; y < (int)(H * 0.8f) && !white; y++)
                for (int dx = -1; dx <= 1 && !white; dx++)
                    white = NearWhite(bmp.GetPixel(Math.Clamp(x + dx, 0, W - 1), y));
            if (white) hits++;
        }
        return hits;
    }

    /// <summary>
    /// DEMONSTRATES the defect's source rather than guessing it: the same frame rendered with
    /// the default core series, then without the Price series. With Price, a near-white line runs
    /// through the bar columns; without it, none do. So the white line is the Price series, drawn
    /// in its metadata default #FFFFFF — not the crosshair (no cursor here), not a wick, not an
    /// overlay indicator.
    /// </summary>
    [Fact]
    public void TheWhiteLineThroughTheClosesIsThePriceSeries()
    {
        var bars = Bars();
        var candles = Core("CANDLES", CoreSeriesIds.Candles);
        var price = Core("PRICE", CoreSeriesIds.Price);
        Fill(candles, bars);
        Fill(price, bars);

        Assert.Equal("#FFFFFF", price.Components.Single().ColorHex, ignoreCase: true);
        Assert.Equal(ComponentDisplayType.Line, price.Components.Single().DisplayType);

        using var with = Render(new[] { candles, price }, bars);
        using var without = Render(new[] { candles }, bars);

        int withWhite = ColumnsWithWhite(with, bars);
        int withoutWhite = ColumnsWithWhite(without, bars);

        Assert.True(withWhite >= 40,
            $"only {withWhite} bar columns carry white with the Price series on — the line is not where expected");
        Assert.True(withoutWhite == 0,
            $"{withoutWhite} bar columns carry white with the Price series OFF — something else draws white too");
    }

    /// <summary>
    /// The property: every line drawn on the price pane has a status line naming it, its reading
    /// in the colour it is drawn in. The Price series is the line in question; Candles stay
    /// unlisted (the symbol line carries the bar) and Volume lives in its own pane.
    ///
    /// <para>Was ThePricePaneLegendNamesThePriceLine, against the boxed legend's rows; ported when
    /// the legend was retired for the status lines (Cody's 2026-10-17 decision: TradingView status
    /// line). Same property, new carrier.</para>
    /// </summary>
    [Fact]
    public void ThePricePaneStatusLinesNameThePriceLine()
    {
        var bars = Bars();
        var mainSeries = new List<ChartSeries>
        {
            Core("CANDLES", CoreSeriesIds.Candles),
            Core("PRICE", CoreSeriesIds.Price),
        };
        foreach (var s in mainSeries) Fill(s, bars);

        var theme = new ThemeService(Substitute.For<ISettingsManager>()).Current;
        var status = ChartStatusLine.Build("Main", ChartStatusLine.MainPaneSeries(mainSeries), 30,
            new ChartStatusLine.Context(theme, ChartStatusLine.DefaultBackingAlpha, bars, null),
            400f, 2000f, ChartStatusLine.LinePx, t => t.Length * 7f);

        var drawnLines = mainSeries
            .Where(s => s.IsVisible)
            .SelectMany(s => s.Components.Select(c => (s, c)))
            .Where(t => t.c.IsVisible && t.c.IsEnabled != false
                        && t.c.DisplayType is ComponentDisplayType.Line or ComponentDisplayType.Area)
            .ToList();
        Assert.NotEmpty(drawnLines);   // a vacuous pass would otherwise be possible

        foreach (var (series, c) in drawnLines)
        {
            Assert.True(SKColor.TryParse(c.ColorHex, out var colour));
            var line = status.Lines.SingleOrDefault(l => l.SeriesId == series.Id);
            Assert.True(line != null && line.Title.Contains("Price"),
                $"the price pane draws '{c.DisplayName}' in {c.ColorHex} and no status line names it. " +
                $"Lines: {string.Join(", ", status.Lines.Select(l => l.Title))}");
            Assert.Contains(line!.Values, v => v.Color == colour.WithAlpha(255) && v.Text != "—");
        }
    }
}
