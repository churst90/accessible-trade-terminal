using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;
using Xunit.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A line or area is called what the chart draws: "area" when a fill is painted under it,
/// "line" when only its stroke is.</b>
///
/// <para>Cody, 2026-11-17: "I want the speech description to match what is drawn and have it
/// change based on whether there is or is not a fill under the line ... if the waves are
/// technically just filled areas, let's stick with that." Cipher B's waves, money flow and wave
/// gap are filled, and were said as "line".</para>
///
/// <para>The word is checked against the real <see cref="ChartRenderer"/>'s pixels, not against
/// the flags: each cell is drawn once with its values and once empty, and a fill paints thousands
/// of pixels where a 2-pixel stroke paints a few hundred.</para>
/// </summary>
public sealed class AreaWordTests
{
    private readonly ITestOutputHelper _out;
    public AreaWordTests(ITestOutputHelper output) => _out = output;

    private const int W = 340, H = 300, Bars = 30;

    private static ComponentConfig Comp(ComponentDisplayType type) => new()
    {
        Name = "v", DisplayName = "v", DisplayType = type, IsVisible = true, IsEnabled = true,
        ColorHex = "#FF2196F3", ColorHexSecondary = "#FFFF5722", Thickness = 2f, ReferenceLevel = 0.0,
    };

    public static IEnumerable<object[]> Cells() => new[]
    {
        new object[] { "plain line" },
        new object[] { "filled wave (Cipher B's waves)" },
        new object[] { "filled per side (Cipher B's money flow and wave gap)" },
        new object[] { "two-colour filled wave" },
        new object[] { "fill turned off by the user" },
        new object[] { "fill asked for, coloured by rules" },
        new object[] { "fill asked for, coloured by a sibling" },
        new object[] { "Area display type" },
        new object[] { "ZeroArea" },
    };

    private static (ChartSeries Series, ComponentConfig Comp) Build(string cell, bool empty)
    {
        var cfg = new SeriesConfig { Id = "k", Name = cell, FriendlyName = cell, Pane = "Main", IndicatorCode = "K", IsVisible = true };
        var buf = new SeriesDataBuffer { SeriesId = "k" };
        double[] Values(double v) => Enumerable.Range(0, Bars).Select(i => empty && i > 0 ? double.NaN : v).ToArray();
        ComponentConfig c;
        switch (cell)
        {
            case "plain line": c = Comp(ComponentDisplayType.Oscillator); break;
            case "filled wave (Cipher B's waves)": c = Comp(ComponentDisplayType.Oscillator); c.IsAreaFill = true; break;
            case "filled per side (Cipher B's money flow and wave gap)":
                c = Comp(ComponentDisplayType.Oscillator); c.IsAreaFill = true; c.AreaBySign = true; break;
            case "two-colour filled wave":
                c = Comp(ComponentDisplayType.Oscillator); c.IsAreaFill = true; c.UsePolarityColoring = true; break;
            case "fill turned off by the user":
                c = Comp(ComponentDisplayType.Oscillator); c.IsAreaFill = true; c.UserIsAreaFill = false; break;
            case "fill asked for, coloured by rules":
                c = Comp(ComponentDisplayType.Line); c.IsAreaFill = true;
                c.ColorRules.Add(new ColorRule { Condition = ColorCondition.BelowZero, ColorHex = "#FF1744" }); break;
            case "fill asked for, coloured by a sibling":
            {
                c = Comp(ComponentDisplayType.Line); c.IsAreaFill = true; c.ColorAboveComponent = "p";
                var p = Comp(ComponentDisplayType.Line); p.Name = "p"; p.IsVisible = false;
                cfg.Components.Add(p); buf.ComponentData["p"] = Values(-60); break;
            }
            case "Area display type": c = Comp(ComponentDisplayType.Area); break;
            case "ZeroArea": c = Comp(ComponentDisplayType.ZeroArea); break;
            default: throw new ArgumentException(cell);
        }
        cfg.Components.Add(c);
        buf.ComponentData["v"] = Values(-40);
        return (new ChartSeries(cfg, buf), c);
    }

    private static SKBitmap Render(ChartSeries s)
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var renderer = new ChartRenderer(new ThemeService(settings), styling, new PaneLayoutService(),
            NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = Enumerable.Range(0, Bars).Select(i => new Ohlcv(start.AddDays(i), 10, 11, 9, 10, 1)).ToList();
        var bmp = new SKBitmap(W, H);
        using var canvas = new SKCanvas(bmp);
        renderer.Render(canvas, W, H, bars, new[] { s }, cursorIndex: -1, viewportStart: 0, viewportLength: Bars,
            viewportRange: (-100, 100), paneRanges: new Dictionary<string, (double, double)>());
        return bmp;
    }

    private static int Changed(SKBitmap a, SKBitmap b)
    {
        int n = 0;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y)) n++;
        return n;
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void It_is_called_an_area_exactly_when_a_fill_is_painted(string cell)
    {
        var (full, comp) = Build(cell, empty: false);
        var (none, _) = Build(cell, empty: true);
        using var a = Render(full);
        using var b = Render(none);
        int painted = Changed(a, b);
        string said = MarkerLook.DescribeLine(full, comp, 10);
        _out.WriteLine($"{cell}: {painted} pixels painted; said '{said}'");

        Assert.True(painted > 200, $"{cell}: the part drew almost nothing ({painted} pixels), so the cell proves nothing.");
        bool filled = painted > 3000;      // the fill from -40 to 0 is a fifth of the pane; a stroke is ~2 x 300
        Assert.EndsWith(filled ? " area" : " line", said);
    }

    [Fact]
    public void A_wave_filled_per_side_names_the_colour_painted_below_its_split()
    {
        // Two-colour switch off: RenderHistogramArea still paints below the split in the second
        // colour (#FF5722, "red"), so that is the colour said, not the line's own blue.
        var (s, comp) = Build("filled per side (Cipher B's money flow and wave gap)", empty: false);
        Assert.False(comp.UsePolarityColoring);
        Assert.Equal("red area", MarkerLook.DescribeLine(s, comp, 10));
        s.Data.ComponentData["v"] = Enumerable.Repeat(40.0, Bars).ToArray();
        Assert.Equal("blue area", MarkerLook.DescribeLine(s, comp, 10));
    }
}
