using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
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
/// <b>Cody's 2026-10-17 decision: TradingView status line.</b> The boxed legend (a colour key per
/// component, no values) and the floating hover readout box (date, pointer price, O H L C) are
/// replaced by TradingView's model: per pane, one status line per indicator with its name, its
/// inputs and each visible plot's reading in that plot's colour; the price pane's first line names
/// the chart and gives the bar's O H L C; each plot's newest value is a label on its axis; the
/// crosshair's date is a label on the time axis.
///
/// <para>
/// <b>His condition</b>: "if any information is given in the written status bar area ... it needs
/// to be spoken as the user isn't going to move focus away from the chart to read something".
/// So the status line is a VISUAL MIRROR of readouts the chart already speaks, and the tests here
/// pin that it prints nothing more: the symbol line's readings are exactly O H L C (TradingView's
/// change-from-previous-close is spoken nowhere, so it is left out), and every reading carries the
/// digits Alt+Shift+D speaks for it.
/// </para>
///
/// <para>
/// The old legend's honesty rules (PaneLegendTests, retired with it) carry over and are re-pinned
/// against the status lines: the line budget comes from the pane's height, a pane too short for
/// one line gets none, dropped lines are counted, a marker family collapses.
/// </para>
/// </summary>
public sealed class ChartStatusLineTests
{
    // ── Fixtures ────────────────────────────────────────────────────────────

    private static ThemeService Themes(ThemeType? type = null)
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        var service = new ThemeService(settings);
        if (type is { } t) service.SetTheme(t);
        return service;
    }

    private static ChartTheme Theme => Themes().Current;

    private static List<Ohlcv> Bars(int n = 40)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // Every bar distinct, alternating up and down.
        return Enumerable.Range(0, n).Select(i =>
        {
            double o = 80_000 + i * 100, c = i % 2 == 0 ? o + 50 : o - 50;
            return new Ohlcv(start.AddDays(i), o, Math.Max(o, c) + 25, Math.Min(o, c) - 25, c, 1000 + i);
        }).ToList();
    }

    private sealed record Comp(string Name, ComponentDisplayType Type, string Hex, Func<int, double> Value,
        bool Visible = true, string Down = "");

    private static ChartSeries Series(string id, string name, string pane, int n, params Comp[] comps)
    {
        var config = new SeriesConfig { Id = id, Name = name, Pane = pane, IndicatorCode = id.ToUpperInvariant() };
        var buffer = new SeriesDataBuffer { SeriesId = id };
        foreach (var c in comps)
        {
            config.Components.Add(new ComponentConfig
            {
                Name = c.Name, DisplayName = c.Name, DisplayType = c.Type, ColorHex = c.Hex,
                ColorHexSecondary = c.Down, IsVisible = c.Visible, ColorSource = ColorSource.Value,
            });
            buffer.ComponentData[c.Name] = Enumerable.Range(0, n).Select(c.Value).ToArray();
        }
        return new ChartSeries(config, buffer);
    }

    private static readonly ChartIdentity Btc = new("Spot", "Bitstamp", "BTCUSDT", "1d");

    private static ChartStatusLine.Context Ctx(IReadOnlyList<Ohlcv>? bars = null, ChartIdentity? id = null, ChartTheme? theme = null)
        => new(theme ?? Theme, ChartStatusLine.DefaultBackingAlpha, bars, id);

    private static PaneStatus Build(string pane, IReadOnlyList<ChartSeries> series, int bar, ChartStatusLine.Context ctx,
        float height = 400f, float width = 4000f)
        => ChartStatusLine.Build(pane, series, bar, ctx, height, width, ChartStatusLine.LinePx, t => t.Length * 7f);

    // ── The price pane's symbol line ────────────────────────────────────────

    /// <summary>
    /// The price pane's first line names the chart and gives the O H L C of the bar it reads — the
    /// bar passed in, as an index into the chart's bars, so two bars give two different lines.
    /// </summary>
    [Fact]
    public void The_symbol_line_leads_the_price_pane_with_the_bars_open_high_low_and_close()
    {
        var bars = Bars();
        foreach (int i in new[] { 10, 25 })
        {
            var first = Build("Main", Array.Empty<ChartSeries>(), i, Ctx(bars, Btc)).Lines[0];
            Assert.Equal(StatusLineKind.Symbol, first.Kind);
            Assert.Equal("BTCUSDT · 1d · Bitstamp", first.Title);
            Assert.Equal(
                new[] { bars[i].Open, bars[i].High, bars[i].Low, bars[i].Close }.Select(ChartStatusLine.FormatReading),
                first.Values.Select(v => v.Text));
        }
    }

    /// <summary>
    /// <b>The parity guard.</b> TradingView prints the change from the previous close after the
    /// O H L C. No bar readout here speaks that change (SdkCandlePatternAnalyzer computes it and
    /// nothing reads it), so by Cody's condition it is not printed: the symbol line's readings are
    /// the four Page Up onto Candles then Left/Right speaks — "Close X. Open X. High X. Low X." —
    /// and nothing else. Pinned as the SET of labels, so a fifth reading of any spelling fails.
    /// </summary>
    [Fact]
    public void The_symbol_line_prints_only_what_the_candles_reading_speaks()
    {
        var first = Build("Main", Array.Empty<ChartSeries>(), 20, Ctx(Bars(), Btc)).Lines[0];
        Assert.Equal(new[] { "O", "H", "L", "C" }, first.Values.Select(v => v.Label));
    }

    /// <summary>
    /// <b>On a Heikin Ashi chart the line gives the candle as drawn</b> — what the candles' spoken
    /// reading gives (NavigationFeedbackManager reads <see cref="ChartMath.BarAsDrawn"/>), not the
    /// raw bar under it. Printing the raw bar would show four numbers the ear never hears.
    /// </summary>
    [Fact]
    public void On_a_Heikin_Ashi_chart_the_symbol_line_gives_the_candle_as_drawn_as_speech_does()
    {
        var bars = Bars();
        var ctx = new ChartStatusLine.Context(Theme, ChartStatusLine.DefaultBackingAlpha, bars, Btc, IsHeikinAshi: true);
        var ha = ChartMath.BarAsDrawn(bars, 20, isHeikinAshi: true);
        Assert.NotEqual(bars[20].Open, ha.Open);   // vacuity: HA differs from raw here

        var first = Build("Main", Array.Empty<ChartSeries>(), 20, ctx).Lines[0];
        Assert.Equal(new[] { ha.Open, ha.High, ha.Low, ha.Close }.Select(ChartStatusLine.FormatReading),
            first.Values.Select(v => v.Text));
    }

    /// <summary>The O H L C take the bar's direction colour, by the rule the candles are painted with.</summary>
    [Fact]
    public void The_symbol_lines_prices_are_in_the_candles_direction_colour()
    {
        var bars = Bars();
        var (bull, bear) = StandardRenderers.ApplyColorVision(Theme, Theme.CandleBullishBody, Theme.CandleBearishBody);
        Assert.All(Build("Main", Array.Empty<ChartSeries>(), 10, Ctx(bars, Btc)).Lines[0].Values,
            v => Assert.Equal(bull.WithAlpha(255), v.Color));   // bar 10 closes up
        Assert.All(Build("Main", Array.Empty<ChartSeries>(), 11, Ctx(bars, Btc)).Lines[0].Values,
            v => Assert.Equal(bear.WithAlpha(255), v.Color));   // bar 11 closes down
    }

    [Fact]
    public void An_indicator_pane_has_no_symbol_line()
    {
        var rsi = Series("rsi", "RSI", "Pane_RSI", 40, new Comp("RSI", ComponentDisplayType.Line, "#7E57C2", i => 50 + i));
        var lines = Build("Pane_RSI", new[] { rsi }, 10, Ctx(Bars(), Btc)).Lines;
        Assert.DoesNotContain(lines, l => l.Kind == StatusLineKind.Symbol);
    }

    // ── An indicator's line ─────────────────────────────────────────────────

    /// <summary>
    /// One line per indicator: its series name (the one Page Up/Down speaks) split into the name
    /// and its inputs, then each visible plot's reading AT the bar. A one-plot indicator is named
    /// by its line; a multi-plot one names each reading.
    /// </summary>
    [Fact]
    public void An_indicator_line_names_it_and_gives_each_plots_reading_at_the_bar()
    {
        var bb = Series("bb", "Bollinger Bands 20 2", "Main", 40,
            new Comp("Upper", ComponentDisplayType.Line, "#2196F3", i => 82_000 + i),
            new Comp("Basis", ComponentDisplayType.Line, "#FF9800", i => 81_000 + i),
            new Comp("Lower", ComponentDisplayType.Line, "#2196F3", i => 80_000 + i));
        var rsi = Series("rsi", "RSI 14", "Pane_RSI", 40, new Comp("RSI", ComponentDisplayType.Line, "#7E57C2", i => 40 + i * 0.5));

        var b = Build("Main", new[] { bb }, 12, Ctx()).Lines.Single();
        Assert.Equal(("Bollinger Bands", "20 2"), (b.Title, b.Inputs));
        Assert.Equal(new[] { "Upper", "Basis", "Lower" }, b.Values.Select(v => v.Label));
        Assert.Equal(new[] { "82,012.00", "81,012.00", "80,012.00" }, b.Values.Select(v => v.Text));

        var r = Build("Pane_RSI", new[] { rsi }, 12, Ctx()).Lines.Single();
        Assert.Equal(("RSI", "14"), (r.Title, r.Inputs));
        Assert.Equal("", r.Values.Single().Label);
        Assert.Equal("46.00", r.Values.Single().Text);
    }

    /// <summary>
    /// <b>The same digits the ear gets.</b> Alt+Shift+D speaks each visible plot as
    /// "{name} {SpeechPriceFormatter value}"; the status line prints that value with thousands
    /// grouped and nothing else changed — so a magnifier user and the screen reader agree to the
    /// last digit, including a sub-cent token and a MACD near zero (which the arrow keys' F2 would
    /// call "0.00").
    /// </summary>
    [Theory]
    [InlineData(82_366.2345)]
    [InlineData(55.7)]
    [InlineData(-40.123)]
    [InlineData(0.0012345)]
    [InlineData(0.00003051)]
    [InlineData(1_234_567.891)]
    public void A_reading_has_the_digits_Alt_Shift_D_speaks_for_it(double v)
    {
        Assert.Equal(SpeechPriceFormatter.FormatPrice(v), ChartStatusLine.FormatReading(v).Replace(",", ""));
    }

    /// <summary>The volume pane's reading is the volume bar's spoken one: exact, grouped, no fake ".00".</summary>
    [Theory]
    [InlineData(12_345.0, "12,345")]
    [InlineData(12_345.678, "12,345.68")]
    [InlineData(120_000_000.0, "120,000,000")]
    public void A_volume_reading_is_the_volume_bars_spoken_form(double v, string expected)
    {
        var vol = Series("volume", "Volume", "Volume", 5, new Comp("Volume", ComponentDisplayType.Bar, "#26A69A", _ => v));
        vol.Components[0].Role = ComponentRole.Volume;
        Assert.Equal(expected, Build("Volume", new[] { vol }, 2, Ctx(Bars(5))).Lines.Single().Values.Single().Text);
    }

    /// <summary>
    /// A hidden plot is not on the screen, so it has no reading in the line (TradingView: hidden
    /// plots are not shown); a reference level is a constant drawn as its own line; a disabled
    /// component draws nothing.
    /// </summary>
    [Fact]
    public void Hidden_and_disabled_plots_and_levels_have_no_reading()
    {
        var s = Series("cipherb", "Cipher B", "Pane_0", 40,
            new Comp("Anchor Wave Fast", ComponentDisplayType.Oscillator, "#78909C", _ => 12, Visible: false),
            new Comp("Wave Trend Fast", ComponentDisplayType.Oscillator, "#FFFFFF", _ => -40),
            new Comp("Overbought", ComponentDisplayType.Level, "#FF0000", _ => 60),
            new Comp("Off", ComponentDisplayType.Line, "#00FF00", _ => 1));
        s.Components[3].IsEnabled = false;

        var line = Build("Pane_0", new[] { s }, 10, Ctx()).Lines.Single();
        Assert.Equal(new[] { "-40.00" }, line.Values.Select(v => v.Text));
    }

    /// <summary>
    /// A plot with no value at this bar (warm-up) keeps its place with a dash, so the readings do
    /// not shuffle along the line as the cursor moves.
    /// </summary>
    [Fact]
    public void A_plot_with_no_value_at_the_bar_keeps_its_place()
    {
        var macd = Series("macd", "MACD", "Pane_0", 40,
            new Comp("MACD", ComponentDisplayType.Line, "#2196F3", i => i < 26 ? double.NaN : 1.5),
            new Comp("Signal", ComponentDisplayType.Line, "#FF9800", i => i < 30 ? double.NaN : 1.2));
        var early = Build("Pane_0", new[] { macd }, 5, Ctx()).Lines.Single();
        Assert.Equal(new[] { "MACD", "Signal" }, early.Values.Select(v => v.Label));
        Assert.Equal(new[] { "—", "—" }, early.Values.Select(v => v.Text));
    }

    /// <summary>
    /// <b>The histogram's reading is in its colour AT that reading</b> — up above the baseline,
    /// down below, the bar's own rule — so the value names the bar the eye is on.
    /// </summary>
    [Fact]
    public void A_histograms_reading_takes_the_colour_its_bar_is_drawn_in()
    {
        var s = Series("h", "Histo", "Pane_0", 40,
            new Comp("Hist", ComponentDisplayType.Histogram, "#00FF7F", i => i % 2 == 0 ? 30 : -30, Down: "#FF2D55"));
        Assert.Equal(SKColor.Parse("#00FF7F"), Build("Pane_0", new[] { s }, 10, Ctx()).Lines[0].Values[0].Color);
        Assert.Equal(SKColor.Parse("#FF2D55"), Build("Pane_0", new[] { s }, 11, Ctx()).Lines[0].Values[0].Color);
    }

    /// <summary>
    /// Two indicators both calling a plot "Resistance" read as a duplicate in a flat legend, which
    /// prefixed the owner's name. A status line is per indicator, so its title tells them apart.
    /// </summary>
    [Fact]
    public void Two_indicators_same_named_plots_are_told_apart_by_their_lines()
    {
        var a = Series("a", "Cipher SR 5 1", "Main", 40, new Comp("Resistance", ComponentDisplayType.StepLine, "#FF0000", _ => 1));
        var b = Series("b", "Value Deviation 240", "Main", 40, new Comp("Resistance", ComponentDisplayType.StepLine, "#FF8800", _ => 2));
        var lines = Build("Main", new[] { a, b }, 3, Ctx()).Lines;
        Assert.Equal(new[] { "Cipher SR", "Value Deviation" }, lines.Select(l => l.Title));
    }

    [Fact]
    public void A_drawing_has_no_status_line()
    {
        var drawing = new ChartSeries { Drawing = new DrawingData { Type = DrawingType.HorizontalLine } };
        drawing.Config.Name = "Horizontal line";
        drawing.Components.Add(new ComponentConfig { Name = "Line", DisplayType = ComponentDisplayType.Line, ColorHex = "#FFFFFF" });
        Assert.Empty(Build("Main", new[] { drawing }, 3, Ctx()).Lines);
        Assert.Empty(ChartStatusLine.MainPaneSeries(new[] { drawing }));
    }

    // ── Markers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A mark is there or it is not, so its name is its reading, and it is listed only at a bar
    /// where it fires. One or two marks keep their real names.
    /// </summary>
    [Fact]
    public void A_mark_is_listed_by_name_at_a_bar_where_it_fires()
    {
        var ms = Series("ms", "Market Structure", "Main", 40,
            new Comp("Swing High", ComponentDisplayType.TriangleDown, "#EF5350", i => i == 7 ? 100 : double.NaN),
            new Comp("Swing Low", ComponentDisplayType.TriangleUp, "#26A69A", i => i == 9 ? 90 : double.NaN));

        Assert.Equal(new[] { "Swing High" }, Build("Main", new[] { ms }, 7, Ctx()).Lines.Single().Values.Select(v => v.Text));
        Assert.Empty(Build("Main", new[] { ms }, 8, Ctx()).Lines.Single().Values);
        Assert.Equal("", Build("Main", new[] { ms }, 8, Ctx()).Lines.Single().Note);
    }

    /// <summary>
    /// <b>A marker family collapses</b> (three or more marker components): the line lists the marks
    /// that fire at this bar and counts the KINDS — "4 mark types", never "4 marks", which Cody's
    /// 2026-10-15 screenshot read over a chart showing about forty.
    /// </summary>
    [Fact]
    public void A_marker_family_collapses_to_its_firing_marks_and_the_count_of_mark_types()
    {
        var ms = Series("ms", "Market Structure (HH/HL/LH/LL) 5 1", "Main", 40,
            new Comp("Swing High", ComponentDisplayType.TriangleDown, "#EF5350", i => i == 7 ? 100 : double.NaN),
            new Comp("Swing Low", ComponentDisplayType.TriangleUp, "#26A69A", _ => double.NaN),
            new Comp("Break of Structure", ComponentDisplayType.Diamond, "#FFFFFF", i => i == 7 ? 95 : double.NaN),
            new Comp("Change of Character", ComponentDisplayType.Cross, "#FFFF00", _ => double.NaN));

        var line = Build("Main", new[] { ms }, 7, Ctx()).Lines.Single();
        Assert.Equal("Market Structure", line.Title);
        Assert.Equal(new[] { "Swing High", "Break of Structure" }, line.Values.Select(v => v.Text));
        Assert.Matches(@"\b4\s+mark\s+types\b", line.Note);
        Assert.DoesNotMatch(@"\b\d+\s+marks?\b(?!\s+types)", line.Note);
    }

    [Fact]
    public void Every_glyph_shape_the_marker_renderers_handle_is_a_marker_and_continuous_shapes_are_not()
    {
        // Moved from PaneLegendTests with the legend: if a shape is drawn as a glyph but not
        // classified, a marker family stops collapsing and floods the line.
        foreach (var t in new[]
        {
            ComponentDisplayType.Dot, ComponentDisplayType.ZeroDot, ComponentDisplayType.GradientDot,
            ComponentDisplayType.Diamond, ComponentDisplayType.Square, ComponentDisplayType.Cross,
            ComponentDisplayType.TriangleUp, ComponentDisplayType.TriangleDown, ComponentDisplayType.Arrow,
        })
            Assert.True(ChartRenderer.IsMarker(t), $"{t} is drawn as a glyph but is not classified as a marker.");
        foreach (var t in new[]
        {
            ComponentDisplayType.Line, ComponentDisplayType.StepLine, ComponentDisplayType.Cloud,
            ComponentDisplayType.Area, ComponentDisplayType.Gradient,
        })
            Assert.False(ChartRenderer.IsMarker(t), $"{t} is continuous and must not collapse as a marker.");
    }

    // ── The budget, and honesty about what does not fit ─────────────────────

    private static ChartSeries[] ManyIndicators(int n) => Enumerable.Range(0, n)
        .Select(i => Series($"s{i}", $"Indicator {(char)('A' + i)}", "Pane_0", 40,
            new Comp("V", ComponentDisplayType.Line, "#FFFFFF", _ => i)))
        .ToArray();

    [Fact]
    public void The_line_budget_comes_from_the_pane_height_and_never_exceeds_nine()
    {
        var series = ManyIndicators(20);
        int Short() => Build("Pane_0", series, 3, Ctx(), height: 215f).Lines.Count;
        int Tall() => Build("Pane_0", series, 3, Ctx(), height: 400f).Lines.Count;
        Assert.True(Short() < Tall(), "a short pane must show fewer lines than a tall one");
        Assert.Equal(9, Build("Pane_0", series, 3, Ctx(), height: 5000f).Lines.Count);
        Assert.Equal(0, ChartStatusLine.LineBudget(20f, 17f, 4f));
        Assert.Equal(3, ChartStatusLine.LineBudget(120f, 17f, 4f));   // the floor, where three fit
    }

    /// <summary>
    /// A block showing a silent subset reads as a complete list of what is on the chart, so the
    /// last line counts every line that did not fit — and is absent when everything fits.
    /// </summary>
    [Fact]
    public void Dropped_lines_are_counted_on_a_last_line_and_only_when_some_are_dropped()
    {
        var lines = Build("Pane_0", ManyIndicators(20), 3, Ctx(), height: 5000f).Lines;
        Assert.Equal(StatusLineKind.More, lines[^1].Kind);
        Assert.Equal($"+{20 - (lines.Count - 1)} more (see the object tree)", lines[^1].Title);

        var few = Build("Pane_0", ManyIndicators(2), 3, Ctx(), height: 5000f).Lines;
        Assert.DoesNotContain(few, l => l.Kind == StatusLineKind.More);
    }

    /// <summary>
    /// A line too wide for its pane drops readings from the end and says how many, rather than
    /// running across the plot or silently cutting them.
    /// </summary>
    [Fact]
    public void Readings_that_do_not_fit_the_width_are_counted_not_silently_cut()
    {
        var wide = Series("w", "Wide", "Pane_0", 40, Enumerable.Range(0, 12)
            .Select(i => new Comp($"Plot {i}", ComponentDisplayType.Line, "#FFFFFF", _ => 1000 + i)).ToArray());

        var line = Build("Pane_0", new[] { wide }, 3, Ctx(), width: 300f).Lines.Single();
        Assert.True(line.HiddenValues > 0, "the fixture fits — it proves nothing");
        Assert.Equal(12, line.Values.Count + line.HiddenValues);
        Assert.Equal(new[] { "Plot 0", "Plot 1" }.Take(line.Values.Count), line.Values.Select(v => v.Label).Take(2));
    }

    // ── Which bar ───────────────────────────────────────────────────────────

    /// <summary>
    /// The hovered bar wins only while the pointer moved more recently than the keyboard cursor.
    /// Following the hover alone would repeat 2026-10-15: Cody drives the chart from the keyboard
    /// with the mouse parked on it, and a line following the parked mouse prints a bar he is not
    /// hearing.
    /// </summary>
    [Fact]
    public void The_active_bar_is_the_hover_only_while_it_leads()
    {
        Assert.Equal(12, ChartStatusLine.ActiveBar(cursorIndex: 30, hoverIndex: 12, hoverIsNewer: true, barCount: 40));
        Assert.Equal(30, ChartStatusLine.ActiveBar(cursorIndex: 30, hoverIndex: 12, hoverIsNewer: false, barCount: 40));
        Assert.Equal(30, ChartStatusLine.ActiveBar(cursorIndex: 30, hoverIndex: null, hoverIsNewer: true, barCount: 40));
        Assert.Equal(39, ChartStatusLine.ActiveBar(cursorIndex: 55, hoverIndex: null, hoverIsNewer: false, barCount: 40));
    }

    // ── Contrast ────────────────────────────────────────────────────────────

    public static IEnumerable<object[]> AllThemes() => Enum.GetValues<ThemeType>().Select(t => new object[] { t });

    /// <summary>
    /// WCAG 1.4.3 on every theme: a reading printed in its plot's colour is at least 4.5:1 on the
    /// line's backing; a colour that is not gets the theme's ink with a chip of the colour instead.
    /// Fed the colours the shipped indicators actually use, dark and light, so both branches run.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllThemes))]
    public void Every_reading_is_legible_on_its_backing_or_falls_back_to_the_ink(ThemeType type)
    {
        var theme = Themes(type).Current;
        var backing = ChartStatusLine.Backing(theme, ChartStatusLine.DefaultBackingAlpha);
        var hexes = new[] { "#FFFFFF", "#000000", "#00FF7F", "#FF2D55", "#2196F3", "#7E57C2", "#78909C", "#FFEB3B", "#0D47A1", "#1B5E20" };
        var s = Series("p", "Palette", "Pane_0", 5, hexes.Select((h, i) => new Comp($"P{i}", ComponentDisplayType.Line, h, _ => 1.0 + i)).ToArray());

        var values = Build("Pane_0", new[] { s }, 2, Ctx(theme: theme)).Lines.Single().Values;
        Assert.Equal(hexes.Length, values.Count);
        foreach (var v in values)
        {
            var shownIn = v.Chip ? ChartStatusLine.Ink(theme) : v.Color;
            double ratio = WcagContrast.Ratio(shownIn, backing);
            Assert.True(ratio >= WcagContrast.TextMinimum,
                $"{type}: reading {v.Label} is printed in {shownIn} at {WcagContrast.Format(ratio)} on the backing (chip: {v.Chip})");
        }
        Assert.Contains(values, v => v.Chip);    // vacuity: the fallback ran
        Assert.Contains(values, v => !v.Chip);   // and so did the plain path
    }

    // ── The renderer ────────────────────────────────────────────────────────

    private const int W = 400, H = 300;

    private static (ChartRenderer Renderer, PaneLayoutService Layout) Renderer()
    {
        var layout = new PaneLayoutService();
        var styling = new StylingService(new ComponentRoleMapper(),
            new Core.Services.Audio.SonificationProfileProvider(), new PaneAssignmentService());
        return (new ChartRenderer(Themes(), styling, layout,
            NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>()), layout);
    }

    private static SKBitmap Render(ChartRenderer renderer, IReadOnlyList<ChartSeries> series, IReadOnlyList<Ohlcv> data,
        IReadOnlyDictionary<string, (double Min, double Max)> ranges, (double Min, double Max) main, int? cursor = null)
    {
        var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, W, H, data, series, cursor ?? data.Count - 1, 0, data.Count, main, ranges,
                rightMarginBars: 0, identity: Btc);
        return bmp;
    }

    private static List<Ohlcv> FlatBars(int n, double open, double close)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, n)
            .Select(i => new Ohlcv(start.AddDays(i), open, Math.Max(open, close) + 1, Math.Min(open, close) - 1, close, 1000))
            .ToList();
    }

    private static int Differences(SKBitmap a, SKBitmap b, SKRectI r)
    {
        int d = 0;
        for (int x = r.Left; x < r.Right; x++)
            for (int y = r.Top; y < r.Bottom; y++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y)) d++;
        return d;
    }

    /// <summary>
    /// <b>The live chart's image carries no legend.</b> The status lines are DOM text over it, so a
    /// legend painted into the image as well would be a second copy under the first, unreachable by
    /// a magnifier. An overlay with no data draws nothing of its own, so the top left of the price
    /// pane must be pixel-identical with and without it.
    /// </summary>
    [Fact]
    public void The_live_renderer_paints_no_legend_into_the_image()
    {
        var data = FlatBars(30, 10, 12);
        var ranges = new Dictionary<string, (double Min, double Max)> { ["Main"] = (0, 100) };
        var overlay = Series("osc", "Overlay 14", "Main", 30, new Comp("Line", ComponentDisplayType.Line, "#00FFFF", _ => double.NaN));

        var (renderer, _) = Renderer();
        using var without = Render(renderer, Array.Empty<ChartSeries>(), data, ranges, (0, 100));
        using var with = Render(renderer, new[] { overlay }, data, ranges, (0, 100));

        Assert.Equal(0, Differences(without, with, new SKRectI(0, 0, 200, 60)));
    }

    /// <summary>
    /// An off-screen snapshot (the AI analyst's chart image) has nothing over it, so there the
    /// renderer paints the status lines itself — or the picture would name none of its lines.
    /// </summary>
    [Fact]
    public void A_snapshot_renderer_paints_the_status_lines()
    {
        var data = FlatBars(30, 10, 12);
        var ranges = new Dictionary<string, (double Min, double Max)> { ["Main"] = (0, 100) };
        var overlay = Series("osc", "Overlay 14", "Main", 30, new Comp("Line", ComponentDisplayType.Line, "#00FFFF", _ => double.NaN));

        var (renderer, _) = Renderer();
        renderer.PaintsStatusLines = true;
        using var without = Render(renderer, Array.Empty<ChartSeries>(), data, ranges, (0, 100));
        using var with = Render(renderer, new[] { overlay }, data, ranges, (0, 100));

        Assert.True(Differences(without, with, new SKRectI(0, 0, 200, 60)) > 50,
            "the snapshot renderer drew no status line for the overlay");
    }

    /// <summary>
    /// <b>Labels on the price scale.</b> Each visible line gets a label on its pane's axis at its
    /// newest value, filled in the line's colour — where the swatch tick used to be a 4x3 px colour
    /// with no number. Sampled on the label's row in the strip, a few pixels in from its left edge.
    /// </summary>
    [Fact]
    public void A_line_has_a_label_on_its_axis_at_its_newest_value_in_its_colour()
    {
        var data = FlatBars(30, 100, 105);
        var ranges = new Dictionary<string, (double Min, double Max)> { ["Main"] = (95, 115), ["Pane_0"] = (0, 100) };
        var rsi = Series("rsi", "RSI 14", "Pane_0", 30, new Comp("RSI", ComponentDisplayType.Line, "#FF00FF", i => i < 29 ? 50 : 30));

        var (renderer, layout) = Renderer();
        // The cursor is on an older bar, so the crosshair badge (at 50) is nowhere near the newest value (30).
        using var bmp = Render(renderer, new[] { rsi }, data, ranges, (95, 115), cursor: 5);

        float paneTop = layout.Dividers.Single().DividerFraction * H;
        float paneBottom = H * (1f - layout.AxisHeightFraction);
        float y = ChartMath.MapY(30, paneTop, paneBottom, 0, 100, false);
        int axisLeft = (int)Math.Round(W * (1f - layout.AxisWidthFraction));
        var fill = bmp.GetPixel(axisLeft + 4, (int)Math.Round(y) - 5);
        Assert.True(Math.Abs(fill.Red - 255) < 12 && fill.Green < 12 && Math.Abs(fill.Blue - 255) < 12,
            $"no magenta value label on the RSI axis at its newest value (y={y:F0}): found {fill}");
    }

    [Fact]
    public void A_hidden_line_has_no_axis_label()
    {
        var s = Series("rsi", "RSI", "Pane_0", 30,
            new Comp("Shown", ComponentDisplayType.Line, "#FF00FF", _ => 30),
            new Comp("Hidden", ComponentDisplayType.Line, "#00FF00", _ => 70, Visible: false));
        var labels = ChartRenderer.ScaleLabelsFor(new[] { s }, 30, Theme);
        Assert.Equal(new[] { "Shown" }, labels.Select(l => l.Component));
    }

    /// <summary>
    /// Labels whose values sit close together are stacked, not overprinted: TradingView's
    /// de-collision. Three lines a pixel apart become three labels a label-height apart, inside the
    /// strip, in the same order as their values.
    /// </summary>
    [Fact]
    public void Axis_labels_that_would_overlap_are_stacked_apart_in_order()
    {
        var labels = new List<ChartRenderer.ScaleLabel>
        {
            new(50.0, SKColors.Red, 0, "a", "A"),
            new(50.2, SKColors.Green, 0, "b", "B"),
            new(49.9, SKColors.Blue, 0, "c", "C"),
        };
        const float h = 16f;
        var placed = ChartRenderer.StackScaleLabels(labels, new SKRect(0, 0, 60, 200), 0, 100, false, h);

        Assert.Equal(3, placed.Count);
        var ys = placed.Select(p => p.Y).ToList();
        for (int i = 1; i < ys.Count; i++)
            Assert.True(ys[i] - ys[i - 1] >= h - 0.01f, $"labels overlap: {string.Join(", ", ys)}");
        // Ordered top to bottom by value, highest first.
        Assert.Equal(new[] { "B", "A", "C" }, placed.Select(p => p.Component));
        Assert.All(ys, y => Assert.InRange(y, h / 2f, 200 - h / 2f));
    }

    /// <summary>
    /// The same three labels at the TOP of the range: pushed apart downwards, none above the
    /// strip. (The labels mid-range can be stacked from either end; at an edge only the pass that
    /// works away from that edge keeps them on the strip.)
    /// </summary>
    [Theory]
    [InlineData(99.9, 100.0, 99.8)]
    [InlineData(0.1, 0.0, 0.2)]
    public void Axis_labels_crowded_at_an_edge_are_stacked_onto_the_strip(double a, double b, double c)
    {
        var labels = new List<ChartRenderer.ScaleLabel>
        {
            new(a, SKColors.Red, 0, "a", "A"), new(b, SKColors.Green, 0, "b", "B"), new(c, SKColors.Blue, 0, "c", "C"),
        };
        const float h = 16f;
        var placed = ChartRenderer.StackScaleLabels(labels, new SKRect(0, 0, 60, 200), 0, 100, false, h);
        var ys = placed.Select(p => p.Y).OrderBy(y => y).ToList();
        Assert.Equal(3, ys.Count);
        for (int i = 1; i < ys.Count; i++)
            Assert.True(ys[i] - ys[i - 1] >= h - 0.01f, $"labels overlap: {string.Join(", ", ys)}");
        Assert.All(ys, y => Assert.InRange(y, h / 2f, 200 - h / 2f));
    }

    /// <summary>
    /// <b>The keyboard cursor's date is a label on the time axis</b>, centred on the crosshair —
    /// the date used to be in the hover readout box only, so only for the mouse. Pinned as the
    /// badge's dark backing under the crosshair in the axis strip, and absent with no cursor.
    /// </summary>
    [Fact]
    public void The_cursor_bars_date_is_a_label_on_the_time_axis_under_the_crosshair()
    {
        var data = FlatBars(30, 100, 105);
        var ranges = new Dictionary<string, (double Min, double Max)> { ["Main"] = (95, 115) };
        var (renderer, layout) = Renderer();
        int axisTop = 0;

        int Dark(SKBitmap bmp, int cx)
        {
            int n = 0;
            for (int x = cx - 3; x <= cx + 3; x++)
                for (int y = axisTop + 3; y < axisTop + 6; y++)
                    if (bmp.GetPixel(x, y) is var c && Math.Abs(c.Red - 40) < 14 && Math.Abs(c.Green - 40) < 14 && Math.Abs(c.Blue - 40) < 14) n++;
            return n;
        }

        using var bmp = Render(renderer, Array.Empty<ChartSeries>(), data, ranges, (95, 115), cursor: 12);
        // The strip's top is known once a frame has published the layout.
        axisTop = (int)Math.Round(H * (1f - layout.AxisHeightFraction));
        float plotW = W * (1f - layout.AxisWidthFraction);
        int cx = (int)Math.Round((12 + 0.5f) * plotW / 30);
        Assert.True(Dark(bmp, cx) > 12, $"no date label under the crosshair at x={cx} on the time axis: " +
            string.Join(" ", Enumerable.Range(axisTop, 8).Select(y => bmp.GetPixel(cx, y).ToString())));

        using var none = Render(renderer, Array.Empty<ChartSeries>(), data, ranges, (95, 115), cursor: -1);
        Assert.True(Dark(none, cx) == 0, "the fixture finds a dark badge with no cursor — it proves nothing");
    }
}
