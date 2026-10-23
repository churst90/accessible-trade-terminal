using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>An old save that names a sub-pane strip still loads, and draws in the pane</b> (2026-10-22).
/// The strip (<c>SubPaneName</c>, <c>SubPaneHeightRatio</c>) was removed once Loukas Cycle
/// Detection's "FY" stopped being its last user. Workspace files written before that carry both
/// fields in their component JSON. The loader must ignore them, not refuse the file, and the
/// component must be drawn in the pane's main area on the pane's own range, as every other
/// component is. If the fields ever started to mean something again (a strip, or a skipped
/// component), the line would move or vanish and this goes red.
/// </summary>
public sealed class SavedSubPaneNameTests
{
    private const int W = 800, H = 600, N = 40;
    private const string Pane = "Pane_OLD";
    private static readonly SKColor Magenta = SKColor.Parse("#FF00FF");

    /// <summary>A component as an old save wrote it, strip fields and all.</summary>
    private const string SavedJson = """
{
  "Id": "old", "Name": "Old Cycles", "Pane": "Pane_OLD", "IndicatorCode": "OLD",
  "Components": [
    {"Name": "FY", "DisplayName": "FY", "DisplayType": 0, "ColorHex": "#FF00FF", "IsVisible": true,
     "Thickness": 2.0, "SubPaneName": "FY", "SubPaneHeightRatio": 0.22}
  ]
}
""";

    private static ChartSeries Loaded()
    {
        var config = JsonConvert.DeserializeObject<SeriesConfig>(SavedJson)!;
        var buffer = new SeriesDataBuffer { SeriesId = "old" };
        buffer.ComponentData["FY"] = Enumerable.Repeat(35.0, N).ToArray();
        return new ChartSeries(config, buffer);
    }

    private static (SKBitmap Bmp, PaneLayoutService Layout) Render()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        var layout = new PaneLayoutService();
        var renderer = new ChartRenderer(new ThemeService(settings),
            new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
            layout, NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = Enumerable.Range(0, N).Select(i => new Ohlcv(start.AddDays(i), 100, 106, 99, 105, 1000)).ToList();
        var ranges = new Dictionary<string, (double Min, double Max)> { ["Main"] = (95, 110), [Pane] = (0, 70) };
        var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, W, H, bars, new[] { Loaded() }, -1, 0, N, (95, 110), ranges,
                rightMarginBars: 0, identity: new ChartIdentity("Spot", "Bitstamp", "BTCUSDT", "1d"));
        return (bmp, layout);
    }

    private static bool Near(SKColor a, SKColor b, int tol = 40) =>
        Math.Abs(a.Red - b.Red) <= tol && Math.Abs(a.Green - b.Green) <= tol && Math.Abs(a.Blue - b.Blue) <= tol;

    [Fact]
    public void A_saved_component_with_the_strip_fields_loads_and_keeps_its_other_fields()
    {
        var config = JsonConvert.DeserializeObject<SeriesConfig>(SavedJson)!;

        var comp = Assert.Single(config.Components);
        Assert.Equal("FY", comp.Name);
        Assert.Equal("#FF00FF", comp.ColorHex);
        Assert.Equal(2f, comp.Thickness);
        Assert.True(comp.IsVisible);
    }

    /// <summary>
    /// The line is at 35 on a 0 to 70 pane: the middle of the pane's plot. In the old strip layout
    /// (22% of the plot, along the bottom, on its own range) it would have been drawn in the
    /// bottom fifth of the pane.
    /// </summary>
    [Fact]
    public void A_saved_component_with_the_strip_fields_is_drawn_in_the_panes_main_area()
    {
        var (bmp, layout) = Render();
        using var _ = bmp;
        int x = 300;   // a column inside the plot, well left of the axis
        var rows = Enumerable.Range(0, H).Where(y => Near(bmp.GetPixel(x, y), Magenta)).ToList();
        Assert.True(rows.Count > 0, "the component with the old strip fields is not drawn at all");

        // The pane's plot, as the renderer published it; the line is the middle of it.
        var area = ChartMath.PlotAreaPx(Pane, H, layout.Dividers, layout.AxisHeightFraction, layout.StatusBands);
        Assert.NotNull(area);
        double expected = ChartMath.MapY(35, (float)area!.Value.Top, (float)area.Value.Bottom, 0, 70, false);
        double row = rows.Average();
        Assert.True(Math.Abs(row - expected) <= 4,
            $"the line is on row {row:F1}; 35 on the pane's 0-70 range is row {expected:F1}");
    }
}
