using System.Globalization;
using System.Text.Json;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Renders real bars with drawings placed the way the drawing tools place them, to PNG.</b>
///
/// <para>
/// For research pictures (the trend line study, 2026-10-21): the drawings are ordinary
/// <see cref="DrawingData"/> run through the real <see cref="DrawingService"/> calculators and the
/// model factory's component configs, exactly as <c>DrawingInteractionManager.CreateDrawingSeries</c>
/// does, and painted by the real <see cref="ChartRenderer"/>. Nothing is drawn by special code.
/// </para>
///
/// <para>
/// Does nothing unless <c>AT_DRAWING_SPEC</c> names a JSON file, so it never writes in CI. The
/// spec: <c>{"charts":[{"out":"x.png","width":1600,"height":900,"density":1.5,"log":false,
/// "theme":"Blackout","bars":[["2024-07-01",o,h,l,c],...],"drawings":[{"type":"TrendLine",
/// "d1":"2024-07-16","p1":5669,"d2":"2024-07-24","p2":5560,"extendRight":true,"color":"#FF1744"},
/// {"type":"TextLabel","d1":"2024-08-05","p1":5100,"text":"cycle low"}]}]}</c>.
/// </para>
/// </summary>
public sealed class DrawingPictureTool
{
    [Fact]
    public void Drawing_pictures_are_written_only_when_a_spec_is_given()
    {
        string? specPath = Environment.GetEnvironmentVariable("AT_DRAWING_SPEC");
        if (string.IsNullOrWhiteSpace(specPath)) return;

        using var doc = JsonDocument.Parse(File.ReadAllText(specPath));
        foreach (var chart in doc.RootElement.GetProperty("charts").EnumerateArray())
            RenderChart(chart);
    }

    private static readonly string[] DateFormats = { "yyyy-MM-dd", "yyyy-MM-dd HH:mm" };

    internal static DateTime Date(JsonElement e) =>
        DateTime.SpecifyKind(DateTime.ParseExact(e.GetString()!, DateFormats, CultureInfo.InvariantCulture), DateTimeKind.Utc);

    internal static double? Num(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    internal static string? Str(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static bool Flag(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>One drawing from a spec entry, named and configured as the drawing tools do it
    /// (<c>DrawingInteractionManager.CreateDrawingSeries</c>; a text label is renamed
    /// "Label: text" once its text is typed). Shared with <see cref="WorkspaceDrawingTool"/>.</summary>
    internal static (SeriesConfig Config, SeriesDataBuffer Buffer, DrawingData Drawing) BuildDrawing(
        JsonElement d, int n, List<Ohlcv> bars, IndicatorModelFactory factory, DrawingService drawingService)
    {
        var type = Enum.Parse<DrawingType>(d.GetProperty("type").GetString()!);
        var drawing = new DrawingData
        {
            Type = type,
            AnchorDate1 = d.TryGetProperty("d1", out var d1) ? Date(d1) : null,
            AnchorPrice1 = Num(d, "p1"),
            AnchorDate2 = d.TryGetProperty("d2", out var d2) ? Date(d2) : null,
            AnchorPrice2 = Num(d, "p2"),
            Text = Str(d, "text") ?? "",
            ExtendLeft = Flag(d, "extendLeft"),
            ExtendRight = Flag(d, "extendRight"),
        };
        // The names the drawing tools give (DrawingInteractionManager).
        string name = type switch
        {
            DrawingType.TextLabel => "Label",
            DrawingType.HorizontalLine => "Horizontal",
            DrawingType.VerticalLine => "Vertical",
            _ => type.ToString(),
        };
        string id = Guid.NewGuid().ToString();
        string spoken = type == DrawingType.TextLabel ? $"Label: {drawing.Text}"
            : $"{DrawingInteractionManager.FriendlyName(type)} ({n})";
        var config = new SeriesConfig { Id = id, Name = spoken, FriendlyName = $"{name} Drawing", Pane = "Main" };
        var buffer = new SeriesDataBuffer { SeriesId = id };
        foreach (var kvp in drawingService.CalculateDrawingData(drawing, bars))
        {
            var comp = factory.CreateComponentConfig(name, kvp.Key);
            // What the user can set in Properties: colour and thickness.
            if (Str(d, "color") is { } hex) { comp.ColorHex = hex; comp.IsUserStyled = true; }
            if (Num(d, "thickness") is { } th) comp.Thickness = (float)th;
            config.Components.Add(comp);
            buffer.ComponentData[comp.Name] = kvp.Value;
        }
        return (config, buffer, drawing);
    }

    internal static DrawingService RealDrawingService()
    {
        var calcType = typeof(IDrawingCalculator);
        return new DrawingService(typeof(DrawingService).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && calcType.IsAssignableFrom(t))
            .Select(t => (IDrawingCalculator)Activator.CreateInstance(t)!).ToList());
    }

    internal static List<Ohlcv> Bars(JsonElement parent) => parent.GetProperty("bars").EnumerateArray()
        .Select(b => new Ohlcv(Date(b[0]), b[1].GetDouble(), b[2].GetDouble(), b[3].GetDouble(), b[4].GetDouble(), 0))
        .ToList();

    private static void RenderChart(JsonElement chart)
    {
        int width = chart.GetProperty("width").GetInt32(), height = chart.GetProperty("height").GetInt32();
        float density = (float)(Num(chart, "density") ?? 1.0);
        bool log = Flag(chart, "log");

        var bars = Bars(chart);

        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        var themes = new ThemeService(settings);
        if (Str(chart, "theme") is { } themeName) themes.SetTheme(Enum.Parse<ThemeType>(themeName));

        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var factory = new IndicatorModelFactory(styling, new MockIndicatorPreferencesService());
        var drawingService = RealDrawingService();

        var price = new SeriesConfig { Id = CoreSeriesIds.Candles, Name = "Candles", Pane = "Main" };
        price.Components.Add(new ComponentConfig { Name = "body", DisplayType = ComponentDisplayType.Candle });
        price.Components.Add(new ComponentConfig { Name = "wick", DisplayType = ComponentDisplayType.Wick, Thickness = 1f });
        var series = new List<ChartSeries> { new(price, new SeriesDataBuffer { SeriesId = CoreSeriesIds.Candles }) };

        int n = 0;
        foreach (var d in chart.GetProperty("drawings").EnumerateArray())
        {
            var (config, buffer, drawing) = BuildDrawing(d, ++n, bars, factory, drawingService);
            series.Add(new ChartSeries(config, buffer) { Drawing = drawing });
        }

        double lo = bars.Min(b => b.Low), hi = bars.Max(b => b.High);
        double min = Num(chart, "min") ?? (log ? lo / 1.03 : lo - (hi - lo) * 0.04);
        double max = Num(chart, "max") ?? (log ? hi * 1.03 : hi + (hi - lo) * 0.04);
        int rightMargin = (int)(Num(chart, "rightMarginBars") ?? 8);

        var renderer = new ChartRenderer(themes, styling, new PaneLayoutService(), NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
            renderer.Render(canvas, width, height, bars, series, cursorIndex: -1, viewportStart: 0,
                viewportLength: bars.Count + rightMargin, viewportRange: (min, max),
                paneRanges: new Dictionary<string, (double, double)>(), isLogScale: log, density: density,
                rightMarginBars: rightMargin);

        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(chart.GetProperty("out").GetString()!, png.ToArray());
    }
}
