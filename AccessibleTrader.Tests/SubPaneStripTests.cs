using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A sub-pane strip is read where it is drawn</b> (Cody, 2026-10-17). A component with a
/// <c>SubPaneName</c> — Loukas Cycles' "FY" day count and phase, 0–1460 days against the pane's
/// 0–70 — is drawn in a strip along the bottom of its pane, on its own range
/// (<c>"Pane/Sub"</c> in PaneRanges), and the pane's other components are drawn in the area above
/// it. Everything that turned a value into a row or a row back into a value assumed one range
/// over the whole plot: the axis labels and the crosshair badge stretched the pane's range over
/// the strip too, the hover readout read the strip with the pane's range, and the hit tester
/// looked for the strip's line at 1000 on a 0–70 scale, off the canvas. These pin each mapper
/// against the pixels the renderer actually drew.
/// </summary>
public sealed class SubPaneStripTests
{
    private const int W = 800, H = 600, N = 40;
    private const string Pane = "Pane_LC";
    private static readonly SKColor Dc = SKColor.Parse("#00FF00"), Fy = SKColor.Parse("#FF00FF");

    private static ThemeService Themes()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings);
    }

    private static List<Ohlcv> Bars()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, N).Select(i => new Ohlcv(start.AddDays(i), 100, 106, 99, 105, 1000)).ToList();
    }

    /// <summary>Loukas Cycles' shape: a day count in the pane, a four-year day count in an "FY" strip.</summary>
    private static ChartSeries Cycles(double dc = 35, double fy = 1000)
    {
        var config = new SeriesConfig { Id = "lc", Name = "Loukas Cycles", Pane = Pane, IndicatorCode = "LC" };
        var buffer = new SeriesDataBuffer { SeriesId = "lc" };
        config.Components.Add(new ComponentConfig { Name = "DC", DisplayName = "DC", DisplayType = ComponentDisplayType.Line, ColorHex = "#00FF00", IsVisible = true, Thickness = 2f });
        config.Components.Add(new ComponentConfig { Name = "FY", DisplayName = "FY", DisplayType = ComponentDisplayType.Line, ColorHex = "#FF00FF", IsVisible = true, Thickness = 2f,
            SubPaneName = "FY", SubPaneHeightRatio = 0.3f });
        buffer.ComponentData["DC"] = Enumerable.Repeat(dc, N).ToArray();
        buffer.ComponentData["FY"] = Enumerable.Repeat(fy, N).ToArray();
        return new ChartSeries(config, buffer);
    }

    private static readonly Dictionary<string, (double Min, double Max)> Ranges = new()
    {
        ["Main"] = (95, 110), [Pane] = (0, 70), [$"{Pane}/FY"] = (0, 1460),
    };

    private static (SKBitmap Bmp, PaneLayoutService Layout) Render(int cursor = -1)
    {
        var layout = new PaneLayoutService();
        var renderer = new ChartRenderer(Themes(),
            new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
            layout, NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, W, H, Bars(), new[] { Cycles() }, cursor, 0, N, (95, 110), Ranges,
                rightMarginBars: 0, identity: new ChartIdentity("Spot", "Bitstamp", "BTCUSDT", "1d"));
        // Set AT_SUBPANE_PNG_DIR to keep the render as a PNG for a human to look at.
        if (Environment.GetEnvironmentVariable("AT_SUBPANE_PNG_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            using var img = SKImage.FromBitmap(bmp);
            using var png = img.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(dir, $"subpane_cursor{cursor}.png"), png.ToArray());
        }
        return (bmp, layout);
    }

    private static bool Near(SKColor a, SKColor b, int tol = 40) =>
        Math.Abs(a.Red - b.Red) <= tol && Math.Abs(a.Green - b.Green) <= tol && Math.Abs(a.Blue - b.Blue) <= tol;

    /// <summary>The centre row of the pixels of <paramref name="c"/> in column <paramref name="x"/>, between two rows.</summary>
    private static double RowOf(SKBitmap bmp, SKColor c, int x, int from = 0, int to = H)
    {
        var rows = Enumerable.Range(from, to - from).Where(y => Near(bmp.GetPixel(x, y), c)).ToList();
        Assert.True(rows.Count > 0, $"nothing in {c} in column {x} between rows {from} and {to}");
        return rows.Average();
    }

    private static WorkspaceState State() => WorkspaceState.Initial with
    {
        Data = new TimeSeriesBuffer<Ohlcv>(Bars()),
        ActiveSeries = System.Collections.Immutable.ImmutableList.Create(Cycles()),
        ViewportStartIndex = 0, ViewportLength = N, ViewportRange = (95, 110),
        PaneRanges = System.Collections.Immutable.ImmutableDictionary.CreateRange(Ranges),
    };

    // ── The axis ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A strip too short for one axis label draws none, and the frame survives. The label band
    /// (one text line below the strip's top, three pixels above its bottom) inverts once the strip
    /// is shorter than that, and <c>Math.Clamp</c> throws on an inverted band: the Camel cycles
    /// workspace's Loukas tabs failed every frame with "'770.375' cannot be greater than 764"
    /// (Cody, 2026-10-21). Render swallows and logs the exception, so the logger is the witness;
    /// every canvas height from a phone-landscape sliver up is tried, so the test does not depend
    /// on which height the layout happens to make too short.
    /// </summary>
    [Fact]
    public void A_strip_too_short_for_a_label_does_not_fail_the_frame()
    {
        var failures = new List<int>();
        for (int h = 80; h <= 400; h += 4)
        {
            var appLogger = Substitute.For<IAppLogger>();
            var renderer = new ChartRenderer(Themes(),
                new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
                new PaneLayoutService(), NullLogger<ChartRenderer>.Instance, appLogger);
            using var bmp = new SKBitmap(W, h);
            using (var canvas = new SKCanvas(bmp))
                renderer.Render(canvas, W, h, Bars(), new[] { Cycles() }, -1, 0, N, (95, 110), Ranges,
                    rightMarginBars: 0, identity: new ChartIdentity("Spot", "Bitstamp", "BTCUSDT", "1d"));
            if (appLogger.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IAppLogger.LogError)))
                failures.Add(h);
        }
        Assert.True(failures.Count == 0, $"Render failed at canvas heights {string.Join(", ", failures)}");
    }

    /// <summary>
    /// Each plot's newest-value label sits on the row its line is drawn on: the pane's line on the
    /// pane's axis, mapped over the area above the strip; the strip's line on the strip's part of
    /// the axis, mapped over the strip on the strip's range.
    /// </summary>
    [Fact]
    public void Each_plots_axis_label_is_on_the_row_its_line_is_drawn_on()
    {
        var (bmp, layout) = Render();
        using var _ = bmp;
        int axisLabelX = (int)Math.Round(W * (1f - layout.AxisWidthFraction)) + 20;
        double dcLine = RowOf(bmp, Dc, 300), fyLine = RowOf(bmp, Fy, 300);

        double dcLabel = RowOf(bmp, Dc, axisLabelX);
        Assert.True(Math.Abs(dcLabel - dcLine) <= 2, $"the DC label is centred on row {dcLabel:F1}; its line is drawn on {dcLine:F1}");
        double fyLabel = RowOf(bmp, Fy, axisLabelX);
        Assert.True(Math.Abs(fyLabel - fyLine) <= 2, $"the FY label is centred on row {fyLabel:F1}; its line is drawn on {fyLine:F1}");
    }

    /// <summary>The crosshair badge reads the pane's line and sits on its row, not on a row the pane's range would have over the strip too.</summary>
    [Fact]
    public void The_crosshair_badge_is_on_the_row_of_the_line_it_reads()
    {
        var (bmp, layout) = Render(cursor: 10);
        using var _ = bmp;
        int chipX = (int)Math.Round(W * (1f - layout.AxisWidthFraction)) + 3;
        // The plot's line, away from the crosshair's vertical.
        double dcLine = RowOf(bmp, Dc, 600);
        double chip = RowOf(bmp, Dc, chipX);
        Assert.True(Math.Abs(chip - dcLine) <= 2, $"the badge's chip is centred on row {chip:F1}; the DC line it reads is drawn on {dcLine:F1}");
    }

    // ── The pointer ──────────────────────────────────────────────────────────

    /// <summary>The hover readout on each drawn line reads that line's value, on its own range.</summary>
    [Fact]
    public void The_hover_reads_each_line_on_its_own_range_where_it_is_drawn()
    {
        var (bmp, layout) = Render();
        using var _ = bmp;
        double dcLine = RowOf(bmp, Dc, 300), fyLine = RowOf(bmp, Fy, 300);

        var store = Substitute.For<IWorkspaceStore>();
        store.State.Returns(State());
        store.StateStream.Returns(System.Reactive.Linq.Observable.Empty<WorkspaceState>());
        var input = new BlazorInputService();
        using var tracker = new ChartHoverTracker(input, store, paneLayout: layout);

        double Read(double y)
        {
            input.ProcessMouse(300, y, "MouseMove", W, H);
            return double.Parse(tracker.Current!.PriceText!.Replace(",", ""), System.Globalization.CultureInfo.InvariantCulture);
        }
        double dc = Read(dcLine);
        Assert.True(Math.Abs(dc - 35) < 1, $"the pointer on the DC line reads {dc}, not 35");
        double fy = Read(fyLine);
        Assert.True(Math.Abs(fy - 1000) < 30, $"the pointer on the FY line reads {fy}, not 1000 on the strip's 0–1460");
    }

    /// <summary>A click on each drawn line hits that component.</summary>
    [Fact]
    public void A_click_on_each_drawn_line_hits_that_component()
    {
        var (bmp, layout) = Render();
        using var _ = bmp;
        double dcLine = RowOf(bmp, Dc, 300), fyLine = RowOf(bmp, Fy, 300);
        var state = State();

        ChartHit? At(double y) => ChartHitTester.HitTest(state, layout.Dividers, layout.AxisHeightFraction, layout.AxisWidthFraction,
            300, y, W, H, statusBands: layout.StatusBands, subPaneStrips: layout.SubPaneStrips);
        Assert.Equal("DC", At(dcLine)?.ComponentName);
        Assert.Equal("FY", At(fyLine)?.ComponentName);
    }
}
