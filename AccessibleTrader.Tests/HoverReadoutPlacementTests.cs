using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The hover readout box stays off the bars the pointer is on.</b>
///
/// <para>Cody's 2026-10-15 BTCUSDT 1d screenshots, normal and focus mode: the box sat fixed at
/// the top right of the chart (<c>top:6px; right:72px</c>), over the newest candles, their highs
/// and the "ascending triangle" label — the live edge, which is where a pointer usually is. It now
/// goes to the top of the plot on the half AWAY from the hairline, capped at that half's width,
/// and on the left it sits beside or under the price pane's legend, never on it.</para>
///
/// <para>These drive the real <see cref="ChartHoverTracker"/> with pointer events and read the
/// placement it produces, taking the box at its WIDEST (its <c>max-width</c>) and, where a
/// height is needed, four lines of text. The real DOM is checked in Chromium by
/// <c>HoverReadoutStaysOffTheHairlineBrowserTests</c>.</para>
/// </summary>
public sealed class HoverReadoutPlacementTests
{
    private const float AxisW = 0.05f, AxisH = 0.05f;
    // Four lines of 0.85rem at line-height 1.5, plus padding and border: a generous box.
    private const double BoxHeight = 4 * 1.5 * 13.6 + 10;

    private sealed class Harness
    {
        public WorkspaceStore Store { get; }
        public BlazorInputService Input { get; } = new();
        public SpyEventBus Bus { get; } = new();
        public PaneLayoutService PaneLayout { get; } = new();
        public double W { get; }
        public double H { get; }

        public Harness(double w, double h, (float, float, float, float)? legend)
        {
            W = w; H = h;
            Store = new WorkspaceStore(Bus, new ViewportRangeCalculator(),
                new ViewportNavigationService(), new VolumeStateService());
            var bars = new List<Ohlcv>();
            for (int i = 0; i < 300; i++)
            {
                double c = 100 + i;
                bars.Add(new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i), c - 1, c + 3, c - 3, c, 1000));
            }
            Store.Dispatch(new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(bars), IsInitialLoad: true));
            PaneLayout.Update(Array.Empty<(string, float)>(), AxisH, AxisW, legend);
        }

        public double PlotWidth => ChartMath.PlotWidth(W, AxisW);

        public ChartHoverTracker Tracker() => new(Input, Store, null, null, null, PaneLayout, Bus);

        public ChartHoverInfo HoverAt(ChartHoverTracker t, double x)
            => TryHoverAt(t, x) ?? throw new Xunit.Sdk.XunitException($"No readout for a pointer at x {x:F0}.");

        /// <summary>Null over the empty slots right of the newest bar (the live edge's margin).</summary>
        public ChartHoverInfo? TryHoverAt(ChartHoverTracker t, double x)
        {
            Input.ProcessMouse(x, H * 0.3, "MouseMove", W, H);
            return t.Current;
        }

        /// <summary>The hairline x of bar <paramref name="index"/>, the way the tracker draws it.</summary>
        public double BarX(int index)
        {
            var s = Store.State;
            return (index - s.ViewportStartIndex) / (double)(s.ViewportLength - 1) * PlotWidth;
        }
    }

    /// <summary>The box at its widest: (left, top, right, bottom) in element pixels.</summary>
    private static (double L, double T, double R, double B) WidestBox(ChartHoverInfo info, double w)
    {
        var p = info.Readout;
        double left = p.AtLeft ? p.Inset : w - p.Inset - p.MaxWidth;
        return (left, p.Top, left + p.MaxWidth, p.Top + BoxHeight);
    }

    // Element sizes: the 1280x720 render, a 1000x610 window's chart (about 1000x430 under the
    // toolbars), and a cramped one.
    public static TheoryData<double, double, string> Layouts()
    {
        var data = new TheoryData<double, double, string>();
        foreach (var (w, h) in new[] { (1280.0, 720.0), (1000.0, 430.0), (700.0, 360.0) })
            foreach (var legend in Legends.Keys)
                data.Add(w, h, legend);
        return data;
    }

    /// <summary>
    /// Legends the renderer can publish (fractions of the canvas): none; one or two rows; a long
    /// label at the renderer's 26% text cap; and nine rows, the most it draws, near its 45%
    /// height cap.
    /// </summary>
    private static readonly Dictionary<string, (float, float, float, float)?> Legends = new()
    {
        ["no legend"] = null,
        ["small legend"] = (0.005f, 0.008f, 0.14f, 0.07f),
        ["wide legend"] = (0.005f, 0.008f, 0.32f, 0.07f),
        ["tall legend"] = (0.005f, 0.008f, 0.20f, 0.42f),
    };

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Hovering_the_newest_bars_leaves_the_box_clear_of_them(double w, double h, string legend)
    {
        var hs = new Harness(w, h, Legends[legend]);
        using var t = hs.Tracker();
        int last = hs.Store.State.Data.Count - 1;
        const int Newest = 10;
        // Left edge of the oldest of the newest ten bars' slots.
        double slot = hs.PlotWidth / (hs.Store.State.ViewportLength - 1);
        double newestLeft = hs.BarX(last - Newest + 1) - slot / 2;
        Assert.True(newestLeft > hs.PlotWidth / 2,
            $"Vacuity: the newest {Newest} bars start at x {newestLeft:F0}, not in the right half of a {hs.PlotWidth:F0}px plot.");

        for (int i = last - Newest + 1; i <= last; i++)
        {
            var info = hs.HoverAt(t, hs.BarX(i));
            var box = WidestBox(info, w);
            Assert.True(box.R < newestLeft,
                $"{w}x{h}, {legend}: hovering bar {i} (hairline x {info.BarX:F0}) the readout box spans x " +
                $"{box.L:F0}–{box.R:F0}, over the newest {Newest} bars (from x {newestLeft:F0}). " +
                "It covered the newest candles in Cody's 2026-10-15 screenshots.");
        }
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Wherever_the_pointer_is_the_box_stays_in_the_plot_and_off_the_hairline(double w, double h, string legend)
    {
        var hs = new Harness(w, h, Legends[legend]);
        using var t = hs.Tracker();
        double mainBottom = h * (1 - AxisH);
        bool sawLeft = false, sawRight = false;
        for (double x = 1; x < hs.PlotWidth; x += 7)
        {
            if (hs.TryHoverAt(t, x) is not { } info) continue;
            var box = WidestBox(info, w);
            sawLeft |= info.Readout.AtLeft;
            sawRight |= !info.Readout.AtLeft;
            string where = $"{w}x{h}, {legend}, pointer x {x:F0} (hairline {info.BarX:F0}): box {box.L:F0},{box.T:F0}–{box.R:F0},{box.B:F0}";
            Assert.False(info.BarX >= box.L - 1 && info.BarX <= box.R + 1, $"{where} covers the hairline.");
            Assert.True(box.L >= 0 && box.R <= hs.PlotWidth, $"{where} leaves the plot (0–{hs.PlotWidth:F0}) or covers the price axis.");
            Assert.True(box.T >= 0 && box.B <= mainBottom, $"{where} leaves the price pane (bottom {mainBottom:F0}).");
        }
        Assert.True(sawLeft && sawRight, "Vacuity: the sweep never put the box on both sides.");
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void The_box_never_covers_the_price_pane_legend(double w, double h, string legend)
    {
        if (Legends[legend] is not { } lg) return;   // nothing to cover
        var hs = new Harness(w, h, lg);
        using var t = hs.Tracker();
        var (ll, lt, lr, lb) = (lg.Item1 * w, lg.Item2 * h, lg.Item3 * w, lg.Item4 * h);
        bool sawLeft = false;
        for (double x = 1; x < hs.PlotWidth; x += 7)
        {
            if (hs.TryHoverAt(t, x) is not { } info) continue;
            sawLeft |= info.Readout.AtLeft;
            var box = WidestBox(info, w);
            bool overlaps = box.L < lr && box.R > ll && box.T < lb && box.B > lt;
            Assert.False(overlaps,
                $"{w}x{h}, {legend}, pointer x {x:F0}: the readout box {box.L:F0},{box.T:F0}–{box.R:F0},{box.B:F0} " +
                $"covers the price pane's legend {ll:F0},{lt:F0}–{lr:F0},{lb:F0}.");
        }
        Assert.True(sawLeft, "Vacuity: the box never went to the legend's side.");
    }

    /// <summary>
    /// The rule above is only as good as the rect it is given. The renderer paints the legend into
    /// the chart image and publishes where through <c>IPaneLayoutService.MainLegendFraction</c>;
    /// here every pixel of legend text in the price pane's upper-left must fall inside the
    /// published rect. Drawn with an overlay that has no values, so the legend is the only thing
    /// in the pane with near-white ink (the axes sit outside the region scanned).
    /// </summary>
    [Fact]
    public void The_renderer_publishes_where_it_paints_the_price_pane_legend()
    {
        const int w = 900, h = 500;
        var settings = NSubstitute.Substitute.For<ISettingsManager>();
        var layout = new PaneLayoutService();
        var styling = new StylingService(new ComponentRoleMapper(),
            new Core.Services.Audio.SonificationProfileProvider(), new PaneAssignmentService());
        var renderer = new ChartRenderer(new ThemeService(settings), styling, layout,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ChartRenderer>.Instance,
            NSubstitute.Substitute.For<AccessibleTrader.Sdk.Logging.IAppLogger>());

        var config = new SeriesConfig { Id = "ma", Name = "A Moving Average With A Long Name", Pane = "Main", IndicatorCode = "SMA" };
        config.Components.Add(new ComponentConfig { Name = "A Moving Average With A Long Name", DisplayName = "A Moving Average With A Long Name", DisplayType = ComponentDisplayType.Line, ColorHex = "#00FFFF" });
        var buffer = new SeriesDataBuffer { SeriesId = "ma" };
        buffer.ComponentData[config.Components[0].Name] = Enumerable.Repeat(double.NaN, 60).ToArray();
        var overlay = new ChartSeries(config, buffer);
        var bars = Enumerable.Range(0, 60)
            .Select(i => new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i), 100, 101, 99, 100, 1000))
            .ToList();

        using var bmp = new SkiaSharp.SKBitmap(w, h);
        using (var canvas = new SkiaSharp.SKCanvas(bmp))
            renderer.Render(canvas, w, h, bars, new[] { overlay }, cursorIndex: -1, viewportStart: 0,
                viewportLength: 60, viewportRange: (90, 110), paneRanges: new Dictionary<string, (double Min, double Max)>(),
                rightMarginBars: 0);

        Assert.True(layout.MainLegendFraction is not null, "The renderer drew a price-pane legend and published no rect for it.");
        var (fl, ft, fr, fb) = layout.MainLegendFraction!.Value;
        double l = fl * w, t = ft * h, r = fr * w, b = fb * h;

        int inside = 0;
        var outside = new List<string>();
        for (int y = 0; y < h / 2; y++)
            for (int x = 0; x < w / 2; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Red <= 170 || c.Green <= 170 || c.Blue <= 170) continue;   // not text ink
                if (x >= l - 1 && x <= r + 1 && y >= t - 1 && y <= b + 1) inside++;
                else if (outside.Count < 5) outside.Add($"({x},{y})");
            }
        Assert.True(inside > 20, $"Vacuity: only {inside} legend text pixels inside the published rect {l:F0},{t:F0}–{r:F0},{b:F0}.");
        Assert.True(outside.Count == 0,
            $"Legend text painted outside the published rect {l:F0},{t:F0}–{r:F0},{b:F0}, at {string.Join(", ", outside)}: " +
            "the hover readout would be placed on top of it.");
    }

    [Fact]
    public void A_legend_appearing_under_a_still_mouse_moves_the_box_out_from_under_it()
    {
        // The renderer publishes the legend on every frame; an indicator added while the mouse is
        // parked on the newest bars must push the box down at once, not at the next mouse move.
        var hs = new Harness(1280, 720, legend: null);
        using var t = hs.Tracker();
        var before = hs.HoverAt(t, hs.BarX(hs.Store.State.Data.Count - 1));
        Assert.True(before.Readout.AtLeft, "Vacuity: the box is not on the legend's side.");

        (float, float, float, float) lg = (0.005f, 0.008f, 0.30f, 0.20f);
        hs.PaneLayout.Update(Array.Empty<(string, float)>(), AxisH, AxisW, lg);
        var box = WidestBox(t.Current!, 1280);
        Assert.True(box.T >= lg.Item4 * 720 || box.L >= lg.Item3 * 1280,
            $"With a legend now at the top left (to x {lg.Item3 * 1280:F0}, y {lg.Item4 * 720:F0}) the still " +
            $"pointer's readout box is at {box.L:F0},{box.T:F0}, on top of it.");
    }
}
