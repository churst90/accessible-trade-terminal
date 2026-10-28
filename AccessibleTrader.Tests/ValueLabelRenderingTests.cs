using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// The DBSI-style chart through the real renderer: the series the real factory builds from the
/// provider's metadata, a candle series, the real range calculator, the real ChartRenderer.
/// </summary>
internal static class DbsiChartHarness
{
    public static ThemeService Themes(ThemeType? type = null)
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        var t = new ThemeService(settings);
        if (type is { } tt) t.SetTheme(tt);
        return t;
    }

    public static StylingService Styling() =>
        new(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());

    public static ChartSeries Candles()
    {
        var cfg = new SeriesConfig { Id = CoreSeriesIds.Candles, Name = "Candles", Pane = "Main", IsVisible = true, Volume = 1f };
        ComponentConfig C(string n, ComponentDisplayType t, ComponentRole r) =>
            new() { Name = n, DisplayName = n, DisplayType = t, Role = r, IsVisible = true, Volume = 1f };
        cfg.Components.Add(C("body", ComponentDisplayType.Candle, ComponentRole.Body));
        cfg.Components.Add(C("upper_wick", ComponentDisplayType.Wick, ComponentRole.Wick));
        cfg.Components.Add(C("lower_wick", ComponentDisplayType.Wick, ComponentRole.Wick));
        return new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = CoreSeriesIds.Candles });
    }

    /// <summary>The series as the factory builds it, its data the provider's own over <paramref name="bars"/>.</summary>
    public static ChartSeries Dbsi(IReadOnlyList<Ohlcv> bars, int labelBars = DbsiStyleProvider.DefaultLabelBars, bool heikinAshi = true)
    {
        var meta = new DbsiStyleProvider().GetIndicators()[0];
        var factory = new IndicatorModelFactory(Styling(), new MockIndicatorPreferencesService());
        var p = new List<(string, string)>
        {
            (DbsiStyleProvider.ParamLongEntry, "7"), (DbsiStyleProvider.ParamShortEntry, "7"),
            (DbsiStyleProvider.ParamTrendLength, "200"), (DbsiStyleProvider.ParamHeikinAshi, heikinAshi ? "true" : "false"),
            (DbsiStyleProvider.ParamLabelBars, labelBars.ToString()),
        };
        var s = factory.CreateSeriesFromMetadata(meta, meta.Name, "Main", p, null);
        var r = DbsiStyleProvider.Compute(bars.ToArray(), DbsiStyleProvider.Settings.From(s.ParametersAsObjects()));
        Put(s, r);
        return s;
    }

    public static void Put(ChartSeries s, DbsiStyleProvider.Result r)
    {
        s.Data.ComponentData[DbsiStyleProvider.CompBull] = r.Bull;
        s.Data.ComponentData[DbsiStyleProvider.CompBear] = r.Bear;
        s.Data.ComponentData[DbsiStyleProvider.CompTrend] = r.Trend;
        s.Data.ComponentData[DbsiStyleProvider.CompBullsTakeControl] = r.BullsTakeControl;
        s.Data.ComponentData[DbsiStyleProvider.CompBearsTakeControl] = r.BearsTakeControl;
        s.Data.ComponentData[DbsiStyleProvider.CompLongEntry] = r.LongEntry;
        s.Data.ComponentData[DbsiStyleProvider.CompShortEntry] = r.ShortEntry;
        s.Data.ComponentData[DbsiStyleProvider.CompControl] = r.Control;
        s.Data.ComponentData[DbsiStyleProvider.CompTrendSource] = r.Source;
    }

    public static (double Min, double Max) MainRange(IReadOnlyList<Ohlcv> bars, IEnumerable<ChartSeries> series, int start, int length) =>
        new ViewportRangeCalculator().Calculate(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars.ToList()),
            ActiveSeries = series.ToImmutableList(),
            ViewportStartIndex = start,
            ViewportLength = length,
        }).PaneRanges["Main"];

    public sealed record Frame(SKBitmap Bmp, ChartTheme Theme, float PlotTop, float PlotBottom, float PlotLeft, float Slot,
        double Min, double Max, bool Log, int Start)
    {
        public float X(int absBar) => PlotLeft + (absBar - Start) * Slot + Slot / 2f;
        public float Y(double price) => ChartMath.MapY(price, PlotTop, PlotBottom, Min, Max, Log);
    }

    public static Frame Render(IReadOnlyList<Ohlcv> bars, IReadOnlyList<ChartSeries> series, int w, int h, float density,
        int start = 0, int? length = null, bool heikinAshi = false, bool log = false, ThemeType? theme = null, string? dump = null,
        IReadOnlyList<Core.Services.Analysis.ChartPattern>? formations = null, (double Min, double Max)? range = null)
    {
        int len = length ?? bars.Count - start;
        var themes = Themes(theme);
        var layout = new PaneLayoutService();
        var renderer = new ChartRenderer(themes, Styling(), layout, NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        var (min, max) = range ?? MainRange(bars, series, start, len);
        var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, w, h, bars, series, cursorIndex: bars.Count - 1, viewportStart: start, viewportLength: len,
                viewportRange: (min, max), paneRanges: new Dictionary<string, (double, double)>(), isHeikinAshi: heikinAshi,
                isLogScale: log, density: density, rightMarginBars: 0, formations: formations,
                identity: new ChartIdentity("Spot", "Coinbase", "BTCUSD", "1d"));
        float axisH = themes.Current.AxisHeight * density, axisW = themes.Current.AxisWidth * density;
        float plotTop = StatusBandGeometry.PlotTop(layout, "Main", 0f, h);
        var f = new Frame(bmp, themes.Current, plotTop, h - axisH, 0f, (w - axisW) / len, min, max, log, start);
        if (dump != null && Environment.GetEnvironmentVariable("AT_DBSI_PNG_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            using var img = SKImage.FromBitmap(bmp);
            using var png = img.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(dir, dump + ".png"), png.ToArray());
        }
        return f;
    }

    private static Dictionary<string, object> ParametersAsObjects(this ChartSeries s) =>
        s.Parameters.ToDictionary(k => k.Key, v => (object)v.Value);
}

/// <summary>
/// <b>Numbers on the candles</b> (<see cref="ComponentDisplayType.ValueLabel"/>), as Market Cipher
/// DBSI prints them: the bull score under each candle in green, the bear score over it in red,
/// the newest candle's leader in yellow, only the newest 25 candles. Every assertion reads pixels
/// the real <see cref="ChartRenderer"/> painted for a series the real factory built.
/// </summary>
public sealed class ValueLabelRenderingTests
{
    private const int N = 40;
    private const float D = 2f;
    private const int W = 1600, H = 900;

    /// <summary>Forty swinging candles around 100, a new high or low every few bars.</summary>
    private static List<Ohlcv> Bars(int n = N, double basePrice = 100)
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, n).Select(i =>
        {
            double mid = basePrice * (1 + 0.06 * Math.Sin(i * 0.45));
            double o = mid - basePrice * 0.01 * Math.Cos(i * 1.3), c = mid + basePrice * 0.01 * Math.Cos(i * 1.3);
            return new Ohlcv(t.AddDays(i), o, Math.Max(o, c) + basePrice * 0.012, Math.Min(o, c) - basePrice * 0.012, c, 1000);
        }).ToList();
    }

    /// <summary>A DBSI series holding fixed scores: bulls 12, bears −3 on every bar (bulls lead).</summary>
    private static ChartSeries Fixed(IReadOnlyList<Ohlcv> bars, double bull = 12, double bear = -3, int labelBars = 25)
    {
        var s = DbsiChartHarness.Dbsi(bars, labelBars);
        s.Data.ComponentData[DbsiStyleProvider.CompBull] = Enumerable.Repeat(bull, bars.Count).ToArray();
        s.Data.ComponentData[DbsiStyleProvider.CompBear] = Enumerable.Repeat(bear, bars.Count).ToArray();
        return s;
    }

    private static bool Near(SKColor a, SKColor b, int tol = 14) =>
        Math.Abs(a.Red - b.Red) <= tol && Math.Abs(a.Green - b.Green) <= tol && Math.Abs(a.Blue - b.Blue) <= tol;

    /// <summary>Every pixel in the frame within <paramref name="tol"/> of <paramref name="ink"/>.</summary>
    private static List<(int X, int Y)> InkPixels(SKBitmap bmp, SKColor ink, int x0 = 0, int x1 = int.MaxValue, int y0 = 0, int y1 = int.MaxValue, int tol = 14)
    {
        var hits = new List<(int, int)>();
        for (int y = Math.Max(0, y0); y < Math.Min(bmp.Height, y1); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(bmp.Width, x1); x++)
                if (Near(bmp.GetPixel(x, y), ink, tol)) hits.Add((x, y));
        return hits;
    }

    private static SKColor BullInk(ChartTheme t) => ValueLabelRenderer.Ink(t, SKColor.Parse(DbsiStyleProvider.BullHex));
    private static SKColor BearInk(ChartTheme t) => ValueLabelRenderer.Ink(t, SKColor.Parse(DbsiStyleProvider.BearHex));
    private static SKColor LiveInk(ChartTheme t) => ValueLabelRenderer.Ink(t, SKColor.Parse(DbsiStyleProvider.LiveHex));

    private static void SetMarkerText(ComponentConfig component, string? text)
    {
        var property = typeof(ComponentConfig).GetProperty("MarkerText");
        Assert.NotNull(property);
        property!.SetValue(component, text);
    }

    // ── Placement ────────────────────────────────────────────────────────────────

    /// <summary>
    /// For one labelled candle: green text only inside its own slot and below its low, red text
    /// only inside its slot and above its high, each within a label's height of the candle.
    /// </summary>
    [Fact]
    public void Bulls_print_under_the_low_and_bears_over_the_high_centred_on_their_own_candle()
    {
        var bars = Bars();
        var s = Fixed(bars);
        var f = DbsiChartHarness.Render(bars, new[] { DbsiChartHarness.Candles(), s }, W, H, D, dump: "placement");
        using var _ = f.Bmp;
        int bar = 30;
        float x = f.X(bar), half = f.Slot / 2f;
        float yLow = f.Y(bars[bar].Low), yHigh = f.Y(bars[bar].High);
        float labelH = (ValueLabelRenderer.PreferredPx + 2 * ValueLabelRenderer.PadPx) * D;

        var bull = InkPixels(f.Bmp, BullInk(f.Theme), (int)(x - half), (int)(x + half));
        var bear = InkPixels(f.Bmp, BearInk(f.Theme), (int)(x - half), (int)(x + half));
        Assert.NotEmpty(bull);
        Assert.NotEmpty(bear);
        Assert.All(bull, p => Assert.InRange(p.Y, yLow, yLow + ValueLabelRenderer.GapPx * D + labelH + 1));
        Assert.All(bear, p => Assert.InRange(p.Y, yHigh - ValueLabelRenderer.GapPx * D - labelH - 1, yHigh));
        // Centred: the text's ink straddles the candle's centre line.
        Assert.True(bull.Min(p => p.X) < x && bull.Max(p => p.X) > x, "bull label not centred on its candle");
        Assert.True(bear.Min(p => p.X) < x && bear.Max(p => p.X) > x, "bear label not centred on its candle");
    }

    /// <summary>
    /// Heikin-Ashi: the label follows the candle AS DRAWN. In a gapping rally a Heikin-Ashi
    /// candle's open (the average of the previous HA candle) sits far below the raw bar, so the
    /// drawn low is well under the raw low: the bull label sits under the DRAWN low, where a
    /// raw-low placement would have printed it over the HA candle's body.
    /// </summary>
    [Fact]
    public void On_a_Heikin_Ashi_chart_labels_follow_the_drawn_candle_not_the_raw_one()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = Enumerable.Range(0, N).Select(i =>
        {
            double o = 100 + i * 10;
            return new Ohlcv(t.AddDays(i), o, o + 1.5, o - 0.5, o + 1, 1000);
        }).ToList();
        var s = Fixed(bars);
        var f = DbsiChartHarness.Render(bars, new[] { DbsiChartHarness.Candles(), s }, W, H, D, heikinAshi: true, dump: "heikin_ashi");
        using var _ = f.Bmp;
        var ha = ChartMath.CalculateHeikinAshi(bars.ToList());
        int bar = 32;
        float x = f.X(bar), half = f.Slot / 2f;
        float yHaLow = f.Y(ha[bar].Low), yRawLow = f.Y(bars[bar].Low);
        Assert.True(yHaLow - yRawLow > 20, "fixture: the HA low must be drawn clearly below the raw low");
        var bull = InkPixels(f.Bmp, BullInk(f.Theme), (int)(x - half), (int)(x + half));
        Assert.NotEmpty(bull);
        Assert.True(bull.Min(p => p.Y) > yHaLow,
            $"label top {bull.Min(p => p.Y)} should sit under the drawn (HA) low {yHaLow:F0}, not by the raw low {yRawLow:F0}");
    }

    /// <summary>Log scale: the gap is measured from the low as the LOG axis maps it.</summary>
    [Fact]
    public void On_a_log_chart_the_label_sits_under_the_low_as_the_log_axis_maps_it()
    {
        var bars = Bars(N, 100).Select((b, i) => i < 20 ? b with { Open = b.Open * 30, High = b.High * 30, Low = b.Low * 30, Close = b.Close * 30 } : b).ToList();
        var s = Fixed(bars);
        var f = DbsiChartHarness.Render(bars, new[] { DbsiChartHarness.Candles(), s }, W, H, D, log: true, dump: "log");
        using var _ = f.Bmp;
        int bar = 34;
        float x = f.X(bar), half = f.Slot / 2f, yLow = f.Y(bars[bar].Low);
        float linear = ChartMath.MapY(bars[bar].Low, f.PlotTop, f.PlotBottom, f.Min, f.Max, false);
        Assert.True(Math.Abs(linear - yLow) > 40, "fixture: log and linear must disagree about this low");
        var bull = InkPixels(f.Bmp, BullInk(f.Theme), (int)(x - half), (int)(x + half));
        Assert.NotEmpty(bull);
        Assert.InRange(bull.Min(p => p.Y), yLow, yLow + (ValueLabelRenderer.GapPx + 2 * ValueLabelRenderer.PadPx + 4) * D);
    }

    // ── Colour ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// The newest bar's leader is printed in yellow (DBSI's live-candle colour); its loser keeps its
    /// own colour; and the bar before the newest — also led by bulls — stays green.
    /// </summary>
    [Fact]
    public void The_newest_candles_leader_is_yellow_and_every_other_label_keeps_its_colour()
    {
        var bars = Bars();
        var s = Fixed(bars);
        var f = DbsiChartHarness.Render(bars, new[] { DbsiChartHarness.Candles(), s }, W, H, D, dump: "live_yellow");
        using var _ = f.Bmp;
        SlotHas(f, N - 1, LiveInk(f.Theme), true, "newest bar's leading bull label in yellow");
        SlotHas(f, N - 1, BullInk(f.Theme), false, "newest bar's bull label must not stay green");
        SlotHas(f, N - 1, BearInk(f.Theme), true, "newest bar's losing bear label keeps red");
        SlotHas(f, N - 2, BullInk(f.Theme), true, "the bar before keeps green");
        SlotHas(f, N - 2, LiveInk(f.Theme), false, "the bar before is not highlighted");
    }

    /// <summary>When bears lead on the newest bar it is the BEAR label that turns yellow.</summary>
    [Fact]
    public void When_bears_lead_the_newest_candle_the_bear_label_is_the_yellow_one()
    {
        var bars = Bars();
        var s = Fixed(bars, bull: -4, bear: 13);
        var f = DbsiChartHarness.Render(bars, new[] { DbsiChartHarness.Candles(), s }, W, H, D);
        using var _ = f.Bmp;
        SlotHas(f, N - 1, LiveInk(f.Theme), true, "bear label in yellow");
        SlotHas(f, N - 1, BullInk(f.Theme), true, "bull label stays green");
        SlotHas(f, N - 1, BearInk(f.Theme), false, "bear label is not red on the newest bar");
    }

    private static void SlotHas(DbsiChartHarness.Frame f, int bar, SKColor ink, bool expected, string what)
    {
        float x = f.X(bar), half = f.Slot / 2f;
        bool any = InkPixels(f.Bmp, ink, (int)(x - half), (int)(x + half), (int)f.PlotTop, (int)f.PlotBottom).Count > 0;
        Assert.True(any == expected, what);
    }

    // ── How many ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// "Label the newest N bars" = 25 (DBSI's): bar N−25 carries numbers, bar N−26 none. At 0 every
    /// bar is labelled.
    /// </summary>
    [Theory]
    [InlineData(25)]
    [InlineData(0)]
    public void Only_the_newest_N_bars_are_labelled(int labelBars)
    {
        var bars = Bars();
        var s = Fixed(bars, labelBars: labelBars);
        var f = DbsiChartHarness.Render(bars, new[] { DbsiChartHarness.Candles(), s }, W, H, D);
        using var _ = f.Bmp;
        SlotHas(f, N - 25, BullInk(f.Theme), true, "the 25th-newest bar is labelled");
        SlotHas(f, N - 26, BullInk(f.Theme), labelBars == 0, labelBars == 0 ? "every bar is labelled at 0" : "the 26th-newest bar is not");
        SlotHas(f, 3, BearInk(f.Theme), labelBars == 0, "an old bar");
    }

    /// <summary>
    /// The narrow-slot rule, at the edge: 300 bars across 1600 px is a 5 px slot at density 2 —
    /// no number fits at the minimum size, so none is drawn at all (not even one, overlapping); the
    /// same chart zoomed to 40 bars draws them. The rule itself: a label is laid out only when its
    /// plate fits inside one slot.
    /// </summary>
    [Fact]
    public void When_the_slot_is_too_narrow_no_label_is_drawn_and_zooming_in_brings_them_back()
    {
        var bars = Bars(300);
        var s = Fixed(bars, labelBars: 0);
        var wide = DbsiChartHarness.Render(bars, new[] { DbsiChartHarness.Candles(), s }, W, H, D, start: 260, length: 40);
        using (wide.Bmp) Assert.NotEmpty(InkPixels(wide.Bmp, BullInk(wide.Theme), 0, W, (int)wide.PlotTop, (int)wide.PlotBottom));
        var narrow = DbsiChartHarness.Render(bars, new[] { DbsiChartHarness.Candles(), s }, W, H, D, start: 0, length: 300, dump: "narrow");
        using (narrow.Bmp)
        {
            Assert.True(narrow.Slot < 8 * D, $"fixture: slot {narrow.Slot}");
            Assert.Empty(InkPixels(narrow.Bmp, BullInk(narrow.Theme), 0, W, (int)narrow.PlotTop, (int)narrow.PlotBottom));
            Assert.Empty(InkPixels(narrow.Bmp, BearInk(narrow.Theme), 0, W, (int)narrow.PlotTop, (int)narrow.PlotBottom));
        }
    }

    // ── The status band and the axis ─────────────────────────────────────────────

    /// <summary>
    /// The candle at the very top of the range (the axis ends at its high, as an axis with no
    /// headroom would): its bear label has no room above it inside the plot. It is slid back in
    /// (on its plate, over the wick) and drawn whole — not cut off by the plot's edge, and never
    /// in the status band, whose rows carry no label ink at all.
    /// </summary>
    [Fact]
    public void A_label_with_no_room_above_the_top_candle_stays_inside_the_plot_never_in_the_status_band()
    {
        var bars = Bars();
        // The top candle: by far the highest high, at the newest-but-two bar.
        int top = N - 3;
        bars[top] = bars[top] with { High = bars.Max(b => b.High) * 1.08 };
        var s = Fixed(bars, bull: 12, bear: 15);
        var f = DbsiChartHarness.Render(bars, new[] { DbsiChartHarness.Candles(), s }, W, H, D, dump: "top_candle",
            range: (bars.Min(b => b.Low) * 0.98, bars[top].High));
        using var _ = f.Bmp;
        Assert.True(f.PlotTop > 10, "fixture: the chart must carry a status band");
        float x = f.X(top), half = f.Slot / 2f;
        var bear = InkPixels(f.Bmp, BearInk(f.Theme), (int)(x - half), (int)(x + half));
        Assert.True(bear.Count > 20, $"the top candle's label is cut off: {bear.Count} ink pixels");
        Assert.True(bear.Max(p => p.Y) - bear.Min(p => p.Y) >= ValueLabelRenderer.PreferredPx * D * 0.6, "the top candle's label is only partly drawn");
        Assert.All(bear, p => Assert.True(p.Y >= f.PlotTop, $"label ink at row {p.Y}, in the status band (plot starts {f.PlotTop})"));
        // And nothing of any label anywhere in the band.
        foreach (var ink in new[] { BullInk(f.Theme), BearInk(f.Theme), LiveInk(f.Theme) })
            Assert.Empty(InkPixels(f.Bmp, ink, 0, (int)(W - f.Theme.AxisWidth * D), 0, (int)f.PlotTop - 1));
    }

    /// <summary>
    /// A bar-anchored number is not a price. On a $20 stock DBSI-style's 16 and −5 sit inside a
    /// plausible-price window, and before the exclusion they stretched the axis to −5: candles
    /// squashed into a strip. The price pane's range is the candles' range with the indicator on.
    /// </summary>
    [Fact]
    public void Bar_anchored_scores_never_stretch_the_price_axis()
    {
        var bars = Bars(N, 20);
        var with = DbsiChartHarness.MainRange(bars, new[] { DbsiChartHarness.Candles(), Fixed(bars, bull: 16, bear: -5) }, 0, N);
        var without = DbsiChartHarness.MainRange(bars, new[] { DbsiChartHarness.Candles() }, 0, N);
        // The trend line is a price and may widen the axis; the scores may not drag it toward −5..16.
        Assert.True(with.Min > 10, $"axis floor {with.Min} — the scores stretched it (candles alone: {without.Min})");
    }

    // ── Legibility, every theme ──────────────────────────────────────────────────

    public static IEnumerable<object[]> AllThemes() => Enum.GetValues<ThemeType>().Select(t => new object[] { t });

    /// <summary>
    /// WCAG 1.4.3 on every built-in theme: green, red and yellow ink at 4.5:1 on the plate over
    /// every surface it can cover (listed here, not borrowed from the renderer), and the painted
    /// pixels: the most contrasting text pixel of each label against the plate behind it.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllThemes))]
    public void Every_theme_prints_the_numbers_at_4_5_to_1(ThemeType type)
    {
        var theme = DbsiChartHarness.Themes(type).Current;
        var plate = ValueLabelRenderer.PlateColour(theme);
        SKColor Over(SKColor fg, SKColor bg)
        {
            double a = fg.Alpha / 255.0;
            return new((byte)Math.Round(fg.Red * a + bg.Red * (1 - a)), (byte)Math.Round(fg.Green * a + bg.Green * (1 - a)), (byte)Math.Round(fg.Blue * a + bg.Blue * (1 - a)));
        }
        var under = new[]
        {
            theme.Background, theme.BackgroundGradientEnd ?? theme.Background, theme.CandleBullishBody, theme.CandleBearishBody,
            theme.CandleBullishWick, theme.CandleBearishWick, theme.CandleDojiBody,
        }.Select(c => Over(plate, c.WithAlpha(255))).ToArray();
        var failures = new List<string>();
        foreach (var (name, ink) in new[] { ("bull", BullInk(theme)), ("bear", BearInk(theme)), ("live", LiveInk(theme)) })
        {
            double worst = under.Min(u => WcagContrast.Ratio(ink, u));
            if (worst < WcagContrast.TextMinimum) failures.Add($"{name} {worst:F2}:1");
        }
        Assert.True(failures.Count == 0, $"{type}: {string.Join(", ", failures)}");

        // The pixels: the strongest text pixel in the newest bar's slot reaches 4.5:1 on the plate.
        var bars = Bars();
        var f = DbsiChartHarness.Render(bars, new[] { DbsiChartHarness.Candles(), Fixed(bars) }, W, H, D, theme: type, dump: $"theme_{type}");
        using var _ = f.Bmp;
        foreach (var (bar, ink) in new[] { (N - 2, BullInk(theme)), (N - 2, BearInk(theme)), (N - 1, LiveInk(theme)) })
        {
            float x = f.X(bar), half = f.Slot / 2f;
            var px = InkPixels(f.Bmp, ink, (int)(x - half), (int)(x + half), (int)f.PlotTop, (int)f.PlotBottom, tol: 40)
                .Select(p => f.Bmp.GetPixel(p.X, p.Y)).ToList();
            Assert.True(px.Count > 0, $"{type}: no label pixels near {ink} at bar {bar}");
            double best = px.Max(c => WcagContrast.Ratio(c, Over(plate, theme.Background.WithAlpha(255))));
            Assert.True(best >= WcagContrast.TextMinimum - 0.05, $"{type}: painted text reaches only {best:F2}:1 at bar {bar}");
        }
    }

    // ── A label companion beside a marker (2026-10-22; beside it 2026-10-28) ────────

    /// <summary>
    /// Cipher C's cycle length is no longer a hidden component of its own: it is the daily low's
    /// <see cref="ComponentCompanions.Label"/> companion, printed with the low's letter (49D)
    /// beside its triangle, to its right, as the original prints it — under the low, since the
    /// triangle is. Read from the real renderer's pixels against the same chart without the companion.
    /// </summary>
    [Fact]
    public void A_label_companion_prints_49D_beside_a_low_triangle()
    {
        var bars = Bars();
        const int at = 20;
        ChartSeries Cc(bool label)
        {
            var meta = new CipherCProvider().GetIndicators()[0];
            var factory = new IndicatorModelFactory(DbsiChartHarness.Styling(), new MockIndicatorPreferencesService());
            var s = factory.CreateSeriesFromMetadata(meta, meta.Name, "Main", new List<(string, string)>(), null);
            foreach (var c in s.Components) s.Data.ComponentData[c.Name] = Enumerable.Repeat(double.NaN, bars.Count).ToArray();
            s.Data.ComponentData[CipherCProvider.CompDcl][at] = bars[at].Low;
            if (label)
            {
                var l = Enumerable.Repeat(double.NaN, bars.Count).ToArray();
                l[at] = 49;
                s.Data.ComponentData[CipherCProvider.CompDcl + ComponentCompanions.Label] = l;
            }
            return s;
        }
        var with = Cc(label: true);
        var dcl = with.Components.Single(c => c.Name == CipherCProvider.CompDcl);
        Assert.Equal(MarkerAnchor.BelowBar, dcl.MarkerAnchor);
        Assert.Equal(MarkerAnchor.BelowBar, ValueLabelRenderer.LabelAnchor(dcl));
        Assert.True(ValueLabelRenderer.HasCompanionLabel(with, dcl));
        Assert.False(ValueLabelRenderer.HasCompanionLabel(Cc(label: false), dcl));

        var range = DbsiChartHarness.MainRange(bars, new[] { DbsiChartHarness.Candles() }, 0, bars.Count);
        var f = DbsiChartHarness.Render(bars, new[] { DbsiChartHarness.Candles(), with }, W, H, D, range: range);
        var plain = DbsiChartHarness.Render(bars, new[] { DbsiChartHarness.Candles(), Cc(label: false) }, W, H, D, range: range);
        var ink = ValueLabelRenderer.Ink(f.Theme, SKColor.Parse(CipherCProvider.DclHex));
        int x0 = (int)(f.X(at) + f.Slot), x1 = (int)(f.X(at) + 6 * f.Slot);
        int low = (int)f.Y(bars[at].Low);
        int beside = InkPixels(f.Bmp, ink, x0, x1, low, (int)f.PlotBottom).Count;
        Assert.True(beside > 20, $"only {beside} label pixels right of the triangle");
        Assert.Empty(InkPixels(plain.Bmp, ink, x0, x1, low, (int)f.PlotBottom));
        Assert.Empty(InkPixels(f.Bmp, ink, (int)(f.X(at) - 2 * f.Slot), (int)(f.X(at) + 2 * f.Slot), (int)f.PlotTop, (int)f.Y(bars[at].High)));

        // The layout says the same: one plate, "49D", right of the bar, under the low.
        using var surface = SKSurface.Create(new SKImageInfo(W, H));
        var ctx = new RenderContext(surface.Canvas, new SKRect(0, f.PlotTop, W - f.Theme.AxisWidth * D, f.PlotBottom), bars,
            ViewportStart: 0, ViewportLength: bars.Count, Min: range.Min, Max: range.Max, IsLogScale: false,
            ItemWidth: f.Slot, Density: D, PaneName: "Main", LocalCursorIndex: -1, Theme: f.Theme);
        var placed = Assert.Single(ValueLabelRenderer.Layout(ctx, with, dcl, out _));
        Assert.Equal("49D", placed.Text);
        Assert.True(placed.Plate.Left > f.X(at), $"plate left {placed.Plate.Left} not right of the bar at {f.X(at)}");
        Assert.True(placed.Plate.Top >= low, $"plate top {placed.Plate.Top} over the low at {low}");
        Assert.Contains(ValueLabelRenderer.Plates(ctx, new[] { with }), p => p == placed.Plate);
    }

    // ── The status line ──────────────────────────────────────────────────────────

    [Fact]
    public void MarkerText_suffix_prints_49D_in_layout_and_status_but_is_not_spoken()
    {
        var bars = Bars(1);
        using var surface = SKSurface.Create(new SKImageInfo(400, 200));
        var theme = DbsiChartHarness.Themes().Current;
        var series = new ChartSeries();
        series.Config.Name = "Cycle Count";
        var comp = new ComponentConfig
        {
            Name = "DCL Count", DisplayName = "DCL Count", DisplayType = ComponentDisplayType.ValueLabel,
            ColorHex = "#00E676", IsVisible = true,
        };
        SetMarkerText(comp, "D");
        series.Components.Add(comp);
        series.Data.ComponentData[comp.Name] = new[] { 49.0 };

        var ctx = new RenderContext(surface.Canvas, new SKRect(0, 0, 400, 200), bars,
            ViewportStart: 0, ViewportLength: 1, Min: 0, Max: 100, IsLogScale: false,
            ItemWidth: 400f, Density: 1f, PaneName: "Main", LocalCursorIndex: 0, Theme: theme);
        var layout = ValueLabelRenderer.Layout(ctx, series, comp, out _);
        Assert.Equal("49D", Assert.Single(layout).Text);

        var status = ChartStatusLine.SeriesLine(series, 0, theme, ChartStatusLine.Backing(theme, 245), bars);
        Assert.NotNull(status);
        Assert.Contains(status!.Values, value => value.Text == "49D");
        Assert.Equal("49", ValueLabelRenderer.Spoken(49));
        Assert.DoesNotContain("D", ValueLabelRenderer.Spoken(49));
    }

    [Fact]
    public void MarkerText_suffix_is_included_when_value_labels_are_sized_to_the_bar_slot()
    {
        var bars = Bars(1);
        using var surface = SKSurface.Create(new SKImageInfo(400, 200));
        var theme = DbsiChartHarness.Themes().Current;
        var series = new ChartSeries();
        var plain = new ComponentConfig { Name = "Plain", DisplayType = ComponentDisplayType.ValueLabel };
        var suffixed = new ComponentConfig { Name = "Suffixed", DisplayType = ComponentDisplayType.ValueLabel };
        SetMarkerText(suffixed, "-A-VERY-WIDE-SUFFIX");
        series.Data.ComponentData[plain.Name] = new[] { 49.0 };
        series.Data.ComponentData[suffixed.Name] = new[] { 49.0 };
        var ctx = new RenderContext(surface.Canvas, new SKRect(0, 0, 400, 200), bars,
            ViewportStart: 0, ViewportLength: 1, Min: 0, Max: 100, IsLogScale: false,
            ItemWidth: 1f, Density: 1f, PaneName: "Main", LocalCursorIndex: 0, Theme: theme);

        bool foundSlotWhereOnlyThePlainNumberFits = false;
        for (float slot = 1f; slot <= 160f; slot += 0.5f)
        {
            var sized = ctx with { ItemWidth = slot };
            bool plainFits = ValueLabelRenderer.Layout(sized, series, plain, out _).Count == 1;
            bool suffixFits = ValueLabelRenderer.Layout(sized, series, suffixed, out _).Count == 1;
            if (plainFits && !suffixFits)
            {
                foundSlotWhereOnlyThePlainNumberFits = true;
                break;
            }
        }

        Assert.True(foundSlotWhereOnlyThePlainNumberFits,
            "The suffix must contribute to width measurement so an over-wide label drops out.");
    }

    /// <summary>
    /// A count (Cipher C's timing window: the cycle's day number) is printed and said as a whole
    /// number, "47", not as a price, "47.00" — on the status line and in Alt+Shift+D's values.
    /// </summary>
    [Fact]
    public void A_count_component_is_a_whole_number_on_the_status_line()
    {
        var bars = Bars(1);
        var theme = DbsiChartHarness.Themes().Current;
        var meta = new CipherCProvider().GetIndicators()[0];
        var factory = new IndicatorModelFactory(DbsiChartHarness.Styling(), new MockIndicatorPreferencesService());
        var s = factory.CreateSeriesFromMetadata(meta, meta.Name, "Main", new List<(string, string)>(), null);
        foreach (var c in s.Components) s.Data.ComponentData[c.Name] = new[] { double.NaN };
        s.Data.ComponentData[CipherCProvider.CompDailyWindow] = new[] { 47.0 };
        s.Data.ComponentData[CipherCProvider.CompSma] = new[] { 47.0 };

        var status = ChartStatusLine.SeriesLine(s, 0, theme, ChartStatusLine.Backing(theme, 245), bars);
        Assert.NotNull(status);
        var texts = status!.Values.Select(v => v.Text).ToList();
        Assert.True(texts.Contains("47"), string.Join(" | ", texts));   // the day count
        Assert.Contains("47.00", texts);    // the moving average, a price, beside it
    }

    /// <summary>"Bulls 12  Bears -3": whole numbers as whole numbers, each in its label's colour.</summary>
    [Fact]
    public void The_status_line_prints_the_two_scores_as_whole_numbers()
    {
        var bars = Bars();
        var s = Fixed(bars);
        var theme = DbsiChartHarness.Themes().Current;
        var line = ChartStatusLine.SeriesLine(s, 30, theme, ChartStatusLine.Backing(theme, 245), bars);
        Assert.NotNull(line);
        var texts = line!.Values.Select(v => $"{v.Label} {v.Text}".Trim()).ToList();
        Assert.Contains("Bulls 12", texts);
        Assert.Contains("Bears -3", texts);
        Assert.DoesNotContain(texts, t => t.StartsWith("Control") || t.StartsWith("Trend source"));
    }

    // ── Heard, not painted ───────────────────────────────────────────────────────

    /// <summary>
    /// The hand-over and entry-score markers are narrated but never painted (ComponentConfig.IsDrawn
    /// false), as DBSI draws nothing for them: the frame with them on is pixel-identical to the
    /// frame with them hidden — on a real chart where they fire in view.
    /// </summary>
    [Fact]
    public void The_hand_over_and_entry_markers_are_never_painted()
    {
        var bars = DbsiStyleGoldenTests.Bars(Fixtures.DbsiDigitisedCharts.All[0]);
        var on = DbsiChartHarness.Dbsi(bars, heikinAshi: false);
        var events = new[] { DbsiStyleProvider.CompBullsTakeControl, DbsiStyleProvider.CompBearsTakeControl,
                             DbsiStyleProvider.CompLongEntry, DbsiStyleProvider.CompShortEntry };
        int fired = events.Sum(e => on.GetComponentData(e).Skip(60).Count(v => !double.IsNaN(v)));
        Assert.True(fired >= 5, $"fixture: only {fired} events in view");
        var off = DbsiChartHarness.Dbsi(bars, heikinAshi: false);
        foreach (var c in off.Components.Where(c => events.Contains(c.Name))) c.IsVisible = false;

        var a = DbsiChartHarness.Render(bars, new[] { DbsiChartHarness.Candles(), on }, 1000, 600, 1f, start: 60, length: 40);
        var b = DbsiChartHarness.Render(bars, new[] { DbsiChartHarness.Candles(), off }, 1000, 600, 1f, start: 60, length: 40);
        using (a.Bmp) using (b.Bmp)
        {
            int diff = 0;
            for (int y = 0; y < 600; y++) for (int x = 0; x < 1000; x++) if (a.Bmp.GetPixel(x, y) != b.Bmp.GetPixel(x, y)) diff++;
            Assert.True(diff == 0, $"{diff} pixels differ: an event marker was painted");
        }
    }

    // ── Formation labels keep off the numbers ────────────────────────────────────

    /// <summary>
    /// A formation label whose usual spot (above its line, at the line's start) is where a value
    /// label's plate sits moves off it: the plate is covered ground to the placement scorer, like
    /// candle ink. Without the obstacle it stays put (FormationLabelLegibilityTests' own case).
    /// </summary>
    [Fact]
    public void A_formation_label_moves_off_a_printed_score()
    {
        var theme = DbsiChartHarness.Themes().Current;
        var bars = Enumerable.Range(0, 100).Select(i => new Ohlcv(new DateTime(2024, 1, 1).AddDays(i), 60, 64, 58, 62, 1)).ToList();
        using var surface = SKSurface.Create(new SKImageInfo(800, 600));
        var ctx = new RenderContext(surface.Canvas, new SKRect(0, 0, 800, 600), bars,
            ViewportStart: 0, ViewportLength: 100, Min: 50, Max: 150, IsLogScale: false,
            ItemWidth: 8f, Density: 1f, PaneName: "Main", LocalCursorIndex: 50, Theme: theme);
        var free = ChartFormationLayer.PlaceLabel(ctx, new List<float>(), y: 300, x: 84, xLineEnd: 700, width: 200, fontSize: 10, pad: 3);
        Assert.Equal((84f, 297f), free);

        var score = new SKRect(150, 286, 162, 299);   // a "15" plate right where the label would print
        var blocked = ChartFormationLayer.PlaceLabel(ctx with { TextObstacles = new[] { score } }, new List<float>(),
            y: 300, x: 84, xLineEnd: 700, width: 200, fontSize: 10, pad: 3);
        var plate = ChartFormationLayer.PlateRect(blocked.X, blocked.Row, 200, 10, 3);
        Assert.False(plate.IntersectsWith(score), $"the formation label at {blocked} still covers the score at {score}");
    }
}
