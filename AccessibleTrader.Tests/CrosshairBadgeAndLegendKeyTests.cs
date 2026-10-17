using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cody's two BTCUSDT 1d screenshots of 2026-10-15</b>, the parts that were the renderer's: a
/// value badge that named nothing, a price line with no badge at all, two legend keys that could
/// not be told apart, and a legend whose text a level line struck through.
///
/// <para>
/// Every one of these was invisible to the suite for the usual reason — the suite asked whether
/// a badge or a key was drawn, never whether a reader could tell what it stood for. The pure
/// selection rules are asserted directly; everything a reader actually SEES is asserted in
/// pixels read back from a real Skia surface, because a rule nobody draws is not a fix.
/// </para>
/// </summary>
public sealed class CrosshairBadgeAndLegendKeyTests
{
    private const int W = 400, H = 300;

    // Cipher B's own colours (CipherBProvider): the histogram and Money Flow are both
    // green-over-red pairs, the Wave Trend lines white and cyan.
    private const string WtHistUp = "#00FF7F", WtHistDown = "#FF2D55";
    private const string MfUp = "#00E676", MfDown = "#FF1744";

    // ── Fixtures ────────────────────────────────────────────────────────────

    private static ThemeService Themes()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings);
    }

    private static (ChartRenderer Renderer, PaneLayoutService Layout) Renderer()
    {
        var layout = new PaneLayoutService();
        var styling = new StylingService(new ComponentRoleMapper(),
            new Core.Services.Audio.SonificationProfileProvider(), new PaneAssignmentService());
        return (new ChartRenderer(Themes(), styling, layout,
            NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>()), layout);
    }

    private static List<Ohlcv> Bars(int n, double open, double close)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, n)
            .Select(i => new Ohlcv(start.AddDays(i), open, Math.Max(open, close) + 1, Math.Min(open, close) - 1, close, 1000))
            .ToList();
    }

    private sealed record Comp(string Name, ComponentDisplayType Type, string Hex, double Value,
        string Down = "", bool Visible = true);

    /// <summary>A series whose every component holds one constant value across <paramref name="n"/> bars.</summary>
    private static ChartSeries Series(string id, string pane, int n, params Comp[] comps)
    {
        var config = new SeriesConfig { Id = id, Name = id, Pane = pane, IndicatorCode = id.ToUpperInvariant() };
        var buffer = new SeriesDataBuffer { SeriesId = id };
        foreach (var c in comps)
        {
            config.Components.Add(new ComponentConfig
            {
                Name = c.Name, DisplayName = c.Name, DisplayType = c.Type,
                ColorHex = c.Hex, ColorHexSecondary = c.Down, IsVisible = c.Visible,
                ColorSource = ColorSource.Value,
            });
            buffer.ComponentData[c.Name] = Enumerable.Repeat(c.Value, n).ToArray();
        }
        return new ChartSeries(config, buffer);
    }

    /// <summary>Cipher B as its provider declares it: hidden anchor waves first, then the
    /// histogram, then the two Wave Trend lines, then Money Flow.</summary>
    private static ChartSeries CipherB(int n = 30, double hist = 55.7, double fast = -40, double slow = -42) =>
        Series("cipherb", "Pane_0", n,
            new Comp("Anchor Wave Fast", ComponentDisplayType.Oscillator, "#78909C", 12, Visible: false),
            new Comp("WT Histogram", ComponentDisplayType.Histogram, WtHistUp, hist, WtHistDown),
            new Comp("Wave Trend Fast", ComponentDisplayType.Oscillator, "#FF00FF", fast),
            new Comp("Wave Trend Slow", ComponentDisplayType.Oscillator, "#00D9FF", slow),
            new Comp("Money Flow", ComponentDisplayType.Histogram, MfUp, 20, MfDown));

    private static SKBitmap RenderFrame(ChartRenderer renderer, IReadOnlyList<ChartSeries> series,
        IReadOnlyList<Ohlcv> data, IReadOnlyDictionary<string, (double Min, double Max)> paneRanges,
        (double Min, double Max) mainRange, string? focusedSeriesId = null, int focusedComponentIndex = -1)
    {
        var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, W, H, data, series, cursorIndex: data.Count - 1,
                viewportStart: 0, viewportLength: data.Count, viewportRange: mainRange,
                paneRanges: paneRanges, rightMarginBars: 0,
                focusedSeriesId: focusedSeriesId, focusedComponentIndex: focusedComponentIndex);
        return bmp;
    }

    private static bool CloseTo(SKColor a, SKColor b, int tolerance = 10) =>
        Math.Abs(a.Red - b.Red) <= tolerance && Math.Abs(a.Green - b.Green) <= tolerance
        && Math.Abs(a.Blue - b.Blue) <= tolerance;

    private static SKColor Hex(string hex) => SKColor.Parse(hex);

    // ── Item 2: whose value the badge shows ────────────────────────────────

    /// <summary>
    /// <b>The badge said 55.70 while both Wave Trend lines sat near −40.</b> It read the FIRST
    /// component in the pane with a value, and Cipher B declares its histogram before its lines.
    /// A sighted reader takes a number on a line's axis for the line. The pane's primary line
    /// wins over a histogram declared before it.
    /// </summary>
    [Fact]
    public void The_badge_reads_the_panes_line_not_the_histogram_declared_before_it()
    {
        var reading = ChartRenderer.CrosshairReadingAt(new[] { CipherB() }, localIndex: 10, viewportStart: 0);

        Assert.NotNull(reading);
        Assert.Equal("Wave Trend Fast", reading!.Value.Component.Name);
        Assert.Equal(-40, reading.Value.Value);
    }

    /// <summary>
    /// A badge cannot label a line that is not on the screen. The old rule did not look at
    /// visibility at all, so Cipher B's hidden anchor wave — declared first — could have been the
    /// number on the axis. Here it is the only LINE with a value, and it is still not read.
    /// </summary>
    [Fact]
    public void The_badge_never_reads_a_hidden_component()
    {
        var s = Series("cipherb", "Pane_0", 30,
            new Comp("Anchor Wave Fast", ComponentDisplayType.Oscillator, "#78909C", 12, Visible: false),
            new Comp("WT Histogram", ComponentDisplayType.Histogram, WtHistUp, 55.7, WtHistDown));

        var reading = ChartRenderer.CrosshairReadingAt(new[] { s }, localIndex: 10, viewportStart: 0);

        Assert.Equal("WT Histogram", reading!.Value.Component.Name);
        Assert.Null(ChartRenderer.CrosshairReadingAt(
            new[] { Series("h", "Pane_0", 30, new Comp("Hidden", ComponentDisplayType.Line, "#FFFFFF", 5, Visible: false)) },
            localIndex: 10, viewportStart: 0));
    }

    /// <summary>
    /// The badge follows the component keyboard navigation is ON, so a sighted companion watching
    /// Cody's screen sees the number his screen reader is saying. When the focus is on something
    /// the badge cannot read — a hidden component, or a series in another pane — it falls back to
    /// the pane's line rather than to nothing.
    /// </summary>
    [Fact]
    public void The_badge_follows_the_component_navigation_is_on_and_falls_back_when_it_cannot()
    {
        var pane = new[] { CipherB() };

        var onHistogram = ChartRenderer.CrosshairReadingAt(pane, 10, 0, focusedSeriesId: "cipherb", focusedComponentIndex: 1);
        Assert.Equal("WT Histogram", onHistogram!.Value.Component.Name);

        var onSlow = ChartRenderer.CrosshairReadingAt(pane, 10, 0, focusedSeriesId: "cipherb", focusedComponentIndex: 3);
        Assert.Equal("Wave Trend Slow", onSlow!.Value.Component.Name);

        var onHidden = ChartRenderer.CrosshairReadingAt(pane, 10, 0, focusedSeriesId: "cipherb", focusedComponentIndex: 0);
        Assert.Equal("Wave Trend Fast", onHidden!.Value.Component.Name);

        var elsewhere = ChartRenderer.CrosshairReadingAt(pane, 10, 0, focusedSeriesId: "rsi-1", focusedComponentIndex: 1);
        Assert.Equal("Wave Trend Fast", elsewhere!.Value.Component.Name);
    }

    /// <summary>
    /// The chip is the colour of what the badge reads, AT that value: a line its stroke colour, a
    /// histogram its up colour above the baseline and its down colour below — the rule the bars
    /// are painted by, so the chip matches the bar the eye is on.
    /// </summary>
    [Fact]
    public void The_badge_chip_is_the_colour_of_what_it_reads()
    {
        var theme = Themes().Current;

        var line = ChartRenderer.CrosshairReadingAt(new[] { CipherB() }, 10, 0)!.Value;
        Assert.Equal(Hex("#FF00FF"), ChartRenderer.ReadingColor(line, theme, null));

        var up = ChartRenderer.CrosshairReadingAt(new[] { CipherB(hist: 30) }, 10, 0, "cipherb", 1)!.Value;
        Assert.Equal(Hex(WtHistUp), ChartRenderer.ReadingColor(up, theme, null));

        var down = ChartRenderer.CrosshairReadingAt(new[] { CipherB(hist: -30) }, 10, 0, "cipherb", 1)!.Value;
        Assert.Equal(Hex(WtHistDown), ChartRenderer.ReadingColor(down, theme, null));
    }

    /// <summary>
    /// In pixels: the oscillator pane's badge sits on the Wave Trend line's row, chipped in the
    /// line's colour. Sampled a few rows off the line itself, so the axis swatch tick for the same
    /// line (3px tall, on the line's row) cannot satisfy it.
    /// </summary>
    [Fact]
    public void The_oscillator_badge_is_drawn_on_the_lines_row_chipped_in_the_lines_colour()
    {
        var (renderer, layout) = Renderer();
        var data = Bars(30, 100, 105);
        var ranges = new Dictionary<string, (double Min, double Max)> { ["Main"] = (95, 115), ["Pane_0"] = (-100, 100) };

        using var bmp = RenderFrame(renderer, new[] { CipherB() }, data, ranges, (95, 115));

        float paneTop = layout.Dividers.Single().DividerFraction * H;
        float paneBottom = H * (1f - layout.AxisHeightFraction);
        float lineY = ChartMath.MapY(-40, paneTop, paneBottom, -100, 100, false);
        int axisLeft = (int)Math.Round(W * (1f - layout.AxisWidthFraction));

        var chip = bmp.GetPixel(axisLeft + 3, (int)Math.Round(lineY + 5));
        Assert.True(CloseTo(chip, Hex("#FF00FF")),
            $"the badge on the Wave Trend Fast row (y={lineY:F0}) has no chip in that line's colour: " +
            $"found {chip} at x={axisLeft + 3}. Without it the number names nothing.");
    }

    /// <summary>
    /// The focus reaches the badge from the REAL frame path — the one both heads draw through —
    /// not only from a test that hands it to the renderer by hand. Navigation on the histogram:
    /// the badge moves to the histogram's row and takes its up colour.
    /// </summary>
    [Fact]
    public void The_frame_renderer_hands_the_navigation_focus_to_the_badge()
    {
        var (renderer, layout) = Renderer();
        var bars = Bars(30, 100, 105);
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = System.Collections.Immutable.ImmutableList.Create(CipherB()),
            CurrentDataIndex = bars.Count - 1,
            ViewportStartIndex = 0,
            ViewportLength = bars.Count,
            RightMarginBars = 0,
            ViewportRange = (95, 115),
            PaneRanges = System.Collections.Immutable.ImmutableDictionary<string, (double Min, double Max)>.Empty
                .Add("Main", (95, 115)).Add("Pane_0", (-100, 100)),
            FocusedSeriesId = "cipherb",
            FocusedComponentIndex = 1,   // WT Histogram
        };

        using var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
            new ChartFrameRenderer(renderer).Render(canvas, W, H, state, 1f);

        float paneTop = layout.Dividers.Single().DividerFraction * H;
        float paneBottom = H * (1f - layout.AxisHeightFraction);
        float histY = ChartMath.MapY(55.7, paneTop, paneBottom, -100, 100, false);
        int axisLeft = (int)Math.Round(W * (1f - layout.AxisWidthFraction));

        var chip = bmp.GetPixel(axisLeft + 3, (int)Math.Round(histY + 5));
        Assert.True(CloseTo(chip, Hex(WtHistUp)),
            $"with navigation on the WT Histogram the badge is not on its row (y={histY:F0}) in its " +
            $"colour: found {chip}. The frame renderer is not passing the focus through.");
    }

    /// <summary>
    /// <b>The price pane's cursor line had no badge.</b> It sits at the close; it now carries the
    /// same badge as the indicator panes, chipped in the candle's direction colour, with the
    /// reading as type beside the chip.
    /// </summary>
    [Fact]
    public void The_price_pane_has_a_value_badge_at_the_close()
    {
        var (renderer, layout) = Renderer();
        var data = Bars(30, 100, 109);   // every bar bullish
        var candles = Series("candles", "Main", 0);

        using var bmp = RenderFrame(renderer, new[] { candles }, data,
            new Dictionary<string, (double Min, double Max)> { ["Main"] = (95, 115) }, (95, 115));

        float mainBottom = H * (1f - layout.AxisHeightFraction);
        float closeY = ChartMath.MapY(109, 0, mainBottom, 95, 115, false);
        int axisLeft = (int)Math.Round(W * (1f - layout.AxisWidthFraction));
        var bullish = StandardRenderers.ApplyColorVision(Themes().Current,
            Themes().Current.CandleBullishBody, Themes().Current.CandleBearishBody).up;

        var chip = bmp.GetPixel(axisLeft + 3, (int)Math.Round(closeY + 5));
        Assert.True(CloseTo(chip, bullish),
            $"no badge chip at the close's row (y={closeY:F0}) in the price axis: found {chip}, " +
            $"wanted the bullish candle colour {bullish}.");

        // And the reading itself: near-white type in the badge, right of the chip, on its rows.
        int ink = 0;
        for (int x = axisLeft + 8; x < W; x++)
            for (int y = (int)closeY - 6; y <= (int)closeY + 4; y++)
                if (bmp.GetPixel(x, y) is var c && c.Red > 170 && c.Green > 170 && c.Blue > 170) ink++;
        Assert.True(ink > 10, $"only {ink} pixels of type on the close's row in the price axis — the badge has no reading");
    }

    /// <summary>
    /// The price axis keeps its gridline labels out from under the new badge, as the indicator
    /// axes always have. The badge's backing is near-opaque, so a label it lands on is not two
    /// numbers overprinted — it is a label with its lower half cut off, which a text-band check
    /// cannot see. So this asks the precise question: the rows just above and just below the
    /// badge, where a label within one line of it would put its glyphs, hold no type.
    ///
    /// <para>
    /// The close (104) sits one unit under the 105 gridline — 8 px on this pane — and the
    /// control render with no cursor proves the 105 label really is drawn into those rows.
    /// </para>
    /// </summary>
    [Fact]
    public void The_price_axis_keeps_its_labels_out_from_under_the_close_badge()
    {
        var data = Bars(30, 101, 104);
        var candles = Series("candles", "Main", 0);
        var ranges = new Dictionary<string, (double Min, double Max)> { ["Main"] = (88.9, 120.9) };

        int TypeInRowsAroundTheBadge(int cursorIndex)
        {
            var (renderer, layout) = Renderer();
            using var bmp = new SKBitmap(W, H);
            using (var canvas = new SKCanvas(bmp))
                renderer.Render(canvas, W, H, data, new[] { candles }, cursorIndex, 0, data.Count,
                    (88.9, 120.9), ranges, rightMarginBars: 0);

            float lineY = ChartMath.MapY(104, 0, H * (1f - layout.AxisHeightFraction), 88.9, 120.9, false);
            int axisLeft = (int)Math.Round(W * (1f - layout.AxisWidthFraction));
            int ink = 0;
            foreach (int y in new[] { -11, -10, -9, 9, 10, 11 }.Select(d => (int)Math.Round(lineY) + d))
                for (int x = axisLeft + 8; x < W; x++)
                    if (bmp.GetPixel(x, y) is var c && c.Red > 170 && c.Green > 170 && c.Blue > 170) ink++;
            return ink;
        }

        Assert.True(TypeInRowsAroundTheBadge(cursorIndex: -1) > 0,
            "with no cursor, no gridline label reaches the rows beside the close — the fixture proves nothing");
        int cut = TypeInRowsAroundTheBadge(cursorIndex: data.Count - 1);
        Assert.True(cut == 0,
            $"{cut} pixels of axis type in the rows just above/below the close's badge: a gridline label " +
            "is drawn within one line of it and the badge is painted over part of it.");
    }

    // ── Item 3: telling bar series apart, and the backing behind the text ────
    //
    // Until 2026-10-17 these asserted the boxed legend's KEYS: a two-column key per histogram in
    // its up and down colours, drawn in pixels. The legend is retired (Cody's decision: TradingView
    // status line): a status line has no key — each plot's reading is printed in the colour the
    // plot is drawn in AT that reading. So the colour properties are ported to the readings, and
    // The_drawn_histogram_key_shows_both_colours is retired with the key it drew: no key is drawn
    // anywhere now (the readings' colours are asserted below, the DOM in StatusLineInChartAreaTests).

    private static ChartStatusLine.Context StatusContext(IReadOnlyList<Ohlcv>? bars = null) =>
        new(Themes().Current, ChartStatusLine.DefaultBackingAlpha, bars, null);

    private static StatusLine LineFor(ChartSeries s, int bar, IReadOnlyList<Ohlcv>? bars = null) =>
        ChartStatusLine.Build(s.Pane, new[] { s }, bar, StatusContext(bars), 400f, 4000f, 17f, t => t.Length * 7f).Lines.Single();

    /// <summary>
    /// <b>"WT Histogram" and "Money Flow" had the same key.</b> Every non-volume bar got one fixed
    /// green, so the two rows were indistinguishable and neither said half its bars are red. A
    /// histogram's reading is printed in its up colour above the baseline and its down colour
    /// below — the rule the bars are painted by — and the two histograms' colours differ.
    /// </summary>
    [Fact]
    public void A_histograms_reading_is_in_its_up_or_down_colour_and_two_histograms_differ()
    {
        StatusValue Reading(ChartSeries s, string label) => LineFor(s, 10).Values.Single(v => v.Label == label);

        Assert.Equal(Hex(WtHistUp), Reading(CipherB(hist: 30), "WT Histogram").Color);
        Assert.Equal(Hex(WtHistDown), Reading(CipherB(hist: -30), "WT Histogram").Color);
        Assert.Equal(Hex(MfUp), Reading(CipherB(), "Money Flow").Color);
        Assert.NotEqual(Reading(CipherB(hist: 30), "WT Histogram").Color, Reading(CipherB(), "Money Flow").Color);
    }

    /// <summary>
    /// Volume keeps the THEME's candle pair, as its bars do — the pair that says "up" in the
    /// price pane says it here too — chosen by the bar's own direction, both halves.
    /// </summary>
    [Fact]
    public void A_volume_reading_takes_the_themes_candle_pair_by_the_bars_direction()
    {
        var theme = Themes().Current;
        var vol = Series("volume", "Volume", 30, new Comp("Volume", ComponentDisplayType.Bar, "#123456", 1000, "#654321"));
        vol.Components[0].Role = ComponentRole.Volume;
        vol.Components[0].ColorSource = ColorSource.PriceAction;
        var (up, down) = StandardRenderers.ApplyColorVision(theme, theme.CandleBullishBody, theme.CandleBearishBody);

        Assert.Equal(up.WithAlpha(255), LineFor(vol, 10, Bars(30, 100, 105)).Values.Single().Color);
        Assert.Equal(down.WithAlpha(255), LineFor(vol, 10, Bars(30, 105, 100)).Values.Single().Color);
    }

    /// <summary>
    /// <b>A level line read as a strikethrough.</b> In focus mode the dashed overbought line ran
    /// straight through "Wave Trend Fast": the legend's backing was 70% opaque, so every line
    /// behind it showed through at 30% of its strength. Ported to the status line's backing, as
    /// the renderer paints it into a snapshot: a full-strength magenta level through the middle
    /// of the first line must not be visible inside the backing — and must be visible beside it,
    /// or the test proves nothing. (The DOM lines use the same backing colour and opacity.)
    /// </summary>
    [Fact]
    public void A_level_line_behind_a_status_line_does_not_show_through_it()
    {
        var (renderer, layout) = Renderer();
        renderer.PaintsStatusLines = true;
        var data = Bars(30, 10, 12);   // the price cursor line sits far below the status lines

        ChartSeries Overlay(double levelValue)
        {
            var s = Series("osc", "Main", 30, new Comp("Wave Trend Fast", ComponentDisplayType.Line, "#00FFFF", double.NaN));
            s.Levels.Add(new LevelConfig { Name = "Overbought", Value = levelValue, ColorHex = "#FF00FF", Thickness = 2f, DashStyle = DashStyle.Solid });
            return s;
        }
        var ranges = new Dictionary<string, (double Min, double Max)> { ["Main"] = (0, 100) };

        // Render once to learn the plot height, then put the level through the first line's
        // centre (block top 4, half a 17px line).
        RenderFrame(renderer, new[] { Overlay(50) }, data, ranges, (0, 100)).Dispose();
        float mainBottom = H * (1f - layout.AxisHeightFraction);
        const float rowCentre = 4f + 8.5f;
        double level = 100.0 * (1.0 - rowCentre / mainBottom);

        using var bmp = RenderFrame(renderer, new[] { Overlay(level) }, data, ranges, (0, 100));

        static bool Magenta(SKColor c) => c.Red - c.Green > 40 && c.Blue - c.Green > 40;

        int plotRight = (int)Math.Round(W * (1f - layout.AxisWidthFraction));
        bool beside = Enumerable.Range(10, 6).Any(y => Magenta(bmp.GetPixel(plotRight - 40, y)));
        Assert.True(beside, "the level line is not drawn beside the status line either — the fixture is wrong");

        // Inside the backing: its left padding, where nothing but the backing is drawn.
        var through = new List<string>();
        for (int x = 5; x <= 7; x++)
            for (int y = 10; y <= 15; y++)
                if (Magenta(bmp.GetPixel(x, y))) through.Add($"({x},{y})={bmp.GetPixel(x, y)}");
        Assert.True(through.Count == 0,
            "the level line shows through the status line's backing, so it strikes through the text: "
            + string.Join(", ", through.Take(4)));
    }
}
