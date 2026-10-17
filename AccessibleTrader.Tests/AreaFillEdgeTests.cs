using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A filled area ends at the last bar that has a value — and starts at the first.</b>
///
/// <para>Cody's 2026-10-17 screenshot (BTCUSDT 1d, viewport at the live edge, ten empty slots of
/// future space on the right): Cipher B's dark-blue slow-wave area ran PAST the newest bar into the
/// future space as a triangle, sloping from its last value (about −40) back up to zero at the far
/// right edge of the pane; the light-blue fast wave did the same. The area path closed itself at
/// the pane's last SLOT, not at the last BAR — and at the first slot rather than the first bar, so
/// a warm-up gap or a NaN hole in the middle was bridged by a slanted edge too.</para>
///
/// <para>Every pixel here is read from the real <see cref="ChartRenderer"/>, at the live edge
/// with a right margin, and compared with the same chart drawn with the component holding no
/// values at all: wherever the component has no data, it must have painted nothing.</para>
/// </summary>
public sealed class AreaFillEdgeTests
{
    private const int W = 340, H = 300;
    private const int Bars = 30, Margin = 10, Slots = Bars + Margin;

    // Warm-up NaNs at 0..4, a hole at 14..16, values everywhere else.
    private static bool HasValue(int i) => i >= 5 && !(i >= 14 && i <= 16);

    private static ThemeService Themes()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings);
    }

    private static StylingService Styling() =>
        new(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());

    private static SKBitmap Render(ChartSeries s, ThemeService themes, bool empty = false)
    {
        var renderer = new ChartRenderer(themes, Styling(), new PaneLayoutService(),
            NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = Enumerable.Range(0, Bars).Select(i => new Ohlcv(start.AddDays(i), 10, 11, 9, 10, 1)).ToList();
        var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, W, H, bars, new[] { s }, cursorIndex: -1, viewportStart: 0, viewportLength: Slots,
                viewportRange: (-100, 100), paneRanges: new Dictionary<string, (double, double)>(),
                rightMarginBars: Margin);
        if (!empty) Dump(bmp, s.Name);
        return bmp;
    }

    /// <summary>Set AT_AREA_PNG_DIR to keep each render as a PNG for a human to look at.</summary>
    private static void Dump(SKBitmap bmp, string name)
    {
        var dir = Environment.GetEnvironmentVariable("AT_AREA_PNG_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        using var img = SKImage.FromBitmap(bmp);
        using var png = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(dir, $"{string.Concat(name.Split(Path.GetInvalidFileNameChars()))}.png"), png.ToArray());
    }

    private static float PaneWidth(ThemeService t) => W - t.Current.AxisWidth;
    private static float BarWidth(ThemeService t) => PaneWidth(t) / Slots;
    private static int Y(double v, ThemeService t) =>
        (int)Math.Round(ChartMath.MapY(v, 0, H - t.Current.AxisHeight, -100, 100, false));

    /// <summary>Pixels (x, y) inside the price pane in <paramref name="x0"/>..<paramref name="x1"/>
    /// that differ between the two renders.</summary>
    private static List<string> Differences(SKBitmap a, SKBitmap b, int x0, int x1, ThemeService t)
    {
        var diffs = new List<string>();
        int bottom = H - (int)t.Current.AxisHeight - 1;
        for (int x = Math.Max(0, x0); x <= Math.Min((int)PaneWidth(t) - 1, x1); x++)
            for (int y = 0; y < bottom; y++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y))
                    diffs.Add($"({x},{y}) {a.GetPixel(x, y)} vs {b.GetPixel(x, y)}");
        return diffs;
    }

    private static void Blank(SKBitmap with, SKBitmap without, int x0, int x1, ThemeService t, string where)
    {
        var d = Differences(with, without, x0, x1, t);
        Assert.True(d.Count == 0,
            $"{where} (x {x0}..{x1}) must be empty, but {d.Count} pixels were painted, e.g. {string.Join("; ", d.Take(4))}");
    }

    // ── The fleet: every kind of filled area the renderer draws ─────────────────────

    public static IEnumerable<object[]> Kinds() => new[]
    {
        new object[] { "line area (IsAreaFill — Cipher B's waves)" },
        new object[] { "legacy Area display type" },
        new object[] { "two-colour line area" },
        new object[] { "histogram area (Cipher B's VWAP and money flow)" },
        new object[] { "ZeroArea" },
        new object[] { "cloud fill" },
        new object[] { "S/R zone band" },
        new object[] { "money-flow sign band" },
    };

    /// <summary>The series a kind is drawn from, its one data component holding
    /// <paramref name="value"/> where <see cref="HasValue"/> says so (or nothing at all when
    /// <paramref name="empty"/>), and where inside the pane its fill is expected.</summary>
    private static (ChartSeries Series, double FillProbe) Build(string kind, bool empty)
    {
        var cfg = new SeriesConfig { Id = "k", Name = kind, FriendlyName = kind, Pane = "Main", IndicatorCode = "K", IsVisible = true };
        var buf = new SeriesDataBuffer { SeriesId = "k" };
        double[] Values(double v) => Enumerable.Range(0, Bars).Select(i => !empty && HasValue(i) ? v : double.NaN).ToArray();
        ComponentConfig Comp(string name, ComponentDisplayType type) => new()
        {
            Name = name, DisplayName = name, DisplayType = type, IsVisible = true, IsEnabled = true,
            ColorHex = "#FF2196F3", ColorHexSecondary = "#FFFF5722", Thickness = 2f, ReferenceLevel = 0.0,
        };

        double probe;
        switch (kind)
        {
            case "line area (IsAreaFill — Cipher B's waves)":
            {
                var c = Comp("v", ComponentDisplayType.Oscillator); c.IsAreaFill = true; c.FillColorHex = "#E60D47A1";
                cfg.Components.Add(c); buf.ComponentData["v"] = Values(-40); probe = -20; break;
            }
            case "legacy Area display type":
                cfg.Components.Add(Comp("v", ComponentDisplayType.Area)); buf.ComponentData["v"] = Values(-40); probe = -20; break;
            case "two-colour line area":
            {
                var c = Comp("v", ComponentDisplayType.Oscillator); c.IsAreaFill = true; c.UsePolarityColoring = true;
                cfg.Components.Add(c); buf.ComponentData["v"] = Values(-40); probe = -20; break;
            }
            case "histogram area (Cipher B's VWAP and money flow)":
            {
                var c = Comp("v", ComponentDisplayType.Histogram); c.IsAreaFill = true;
                cfg.Components.Add(c); buf.ComponentData["v"] = Values(-40); probe = -20; break;
            }
            case "ZeroArea":
                cfg.Components.Add(Comp("v", ComponentDisplayType.ZeroArea)); buf.ComponentData["v"] = Values(-40); probe = -20; break;
            case "cloud fill":
            {
                // The edges are hidden so only the fill can paint.
                var up = Comp("u", ComponentDisplayType.Line); up.IsVisible = false;
                var lo = Comp("l", ComponentDisplayType.Line); lo.IsVisible = false;
                cfg.Components.Add(up); cfg.Components.Add(lo);
                buf.ComponentData["u"] = Values(40); buf.ComponentData["l"] = Values(-40);
                cfg.CloudFills.Add(new CloudFillConfig
                {
                    UpperComponentName = "u", LowerComponentName = "l",
                    BullishColorHex = "#882196F3", BearishColorHex = "#88FF5722", DisplayName = "Fill", IsVisible = true,
                });
                probe = 0; break;
            }
            case "S/R zone band":
            {
                var c = Comp("v", ComponentDisplayType.Line); c.IsVisible = false;
                cfg.Components.Add(c); buf.ComponentData["v"] = Values(50);
                cfg.ZoneBands.Add(new ZoneBandConfig { ComponentName = "v", BandWidthPct = 20f, ColorHex = "#882196F3", DisplayName = "Zone", IsVisible = true });
                probe = 52; break;
            }
            case "money-flow sign band":
            {
                var c = Comp("v", ComponentDisplayType.Line); c.IsVisible = false;
                cfg.Components.Add(c); buf.ComponentData["v"] = Values(-40);
                cfg.ZoneBands.Add(new ZoneBandConfig
                {
                    DisplayName = "Bar", ColorHex = "#882196F3", NegativeColorHex = "#88FF5722",
                    SignComponentName = "v", FixedTop = -80, FixedBottom = -100, IsVisible = true,
                });
                probe = -90; break;
            }
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        return (new ChartSeries(cfg, buf), probe);
    }

    /// <summary>
    /// The future space, the warm-up and the hole are all empty, and the runs that DO have values
    /// are filled. The last clause is not decoration: the line area used to leave its first run
    /// unclosed, which Skia fills along the line's own chord — nothing at all for a flat line —
    /// and the cloud's last run lost its whole lower edge at the live edge.
    /// </summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void An_area_paints_only_where_its_component_has_values(string kind)
    {
        var t = Themes();
        var (with, probe) = Build(kind, empty: false);
        var (without, _) = Build(kind, empty: true);
        using var a = Render(with, t);
        using var b = Render(without, t, empty: true);

        float bw = BarWidth(t);
        int SlotLeft(int i) => (int)Math.Floor(i * bw);
        int SlotRight(int i) => (int)Math.Ceiling((i + 1) * bw);

        // The future space: every column after the newest bar's slot.
        Blank(a, b, SlotRight(Bars - 1) + 1, W, t, $"{kind}: the future space right of the newest bar");
        // The warm-up: every column before the first bar with a value.
        Blank(a, b, 0, SlotLeft(5) - 2, t, $"{kind}: the warm-up left of the first value");
        // The hole: between the last bar before it and the first after it.
        Blank(a, b, SlotRight(13) + 1, SlotLeft(17) - 2, t, $"{kind}: the NaN hole");

        // And both runs that have values are filled (between the value and the baseline).
        foreach (int bar in new[] { 9, 24 })
        {
            int x = (int)((bar + 0.5f) * bw), y = Y(probe, t);
            Assert.True(a.GetPixel(x, y) != b.GetPixel(x, y),
                $"{kind}: bar {bar}'s run is not filled at ({x},{y}) — the pixel is {a.GetPixel(x, y)}, as on the empty chart.");
        }
    }

    // ── Cipher B itself, as the factory builds it ───────────────────────────────────

    /// <summary>
    /// The screenshot itself: Cipher B from the provider's metadata, the waves near −40/−35 up to
    /// the newest bar, ten slots of future space. Nothing of the waves may reach into it.
    /// </summary>
    [Fact]
    public void Cipher_Bs_waves_stop_at_the_newest_bar()
    {
        var t = Themes();
        ChartSeries CipherB(bool empty)
        {
            var provider = new CipherBProvider();
            var factory = new IndicatorModelFactory(Styling(), new MockIndicatorPreferencesService());
            var s = factory.CreateSeriesFromMetadata(provider.GetIndicators()[0], "Cipher B", "Main",
                new List<(string, string)>(), null);
            foreach (var c in s.Components)
                s.Data.ComponentData[c.Name] = Enumerable.Repeat(double.NaN, Bars).ToArray();
            if (!empty)
            {
                s.Data.ComponentData[CipherBProvider.CompWT1] = Enumerable.Range(0, Bars).Select(i => -40.0 + i % 3).ToArray();
                s.Data.ComponentData[CipherBProvider.CompWT2] = Enumerable.Range(0, Bars).Select(i => -35.0).ToArray();
            }
            return s;
        }

        using var a = Render(CipherB(empty: false), t);
        using var b = Render(CipherB(empty: true), t, empty: true);
        int futureStart = (int)Math.Ceiling(Bars * BarWidth(t)) + 1;
        Blank(a, b, futureStart, W, t, "Cipher B's waves in the future space");
        // …and the waves are there, up to the newest bar.
        int x = (int)((Bars - 1 + 0.5f) * BarWidth(t)), y = Y(-20, t);
        Assert.True(a.GetPixel(x, y) != b.GetPixel(x, y), $"the waves are not drawn at the newest bar ({x},{y})");
    }
}
