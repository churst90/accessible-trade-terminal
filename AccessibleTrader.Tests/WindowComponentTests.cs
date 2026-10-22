using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The Window display type, the drawing a component carries, and the fill switch (2026-10-22).</b>
///
/// <para>
/// Cody: a timing window is its own component type — shaded where it is open, its count heard,
/// Ctrl+Left/Right through where it opens and closes. Cipher C's cycle arc came back as the
/// window's drawing (<see cref="ComponentCompanions.Path"/>), named when the user lands on the
/// window and switched off in Appearance, not a component and not a <c>,</c>/<c>.</c> stop.
/// "Fill under the line" is a user choice in Appearance for any component it works on.
/// </para>
///
/// <para>
/// The shading and the drawing on screen are pixel tests in <see cref="BackgroundTintTests"/>;
/// Ctrl+Left/Right through a window's edges is in <see cref="CrossingMessageTests"/>; the saved
/// choices are in <see cref="ComponentPreferenceChoicesTests"/>.
/// </para>
/// </summary>
public sealed class WindowComponentTests
{
    private const int N = 12;

    private static double[] OpenOn(params (int From, int To)[] spans) =>
        Enumerable.Range(0, N).Select(i => spans.Any(s => i >= s.From && i <= s.To) ? (double)(i + 1) : double.NaN).ToArray();

    private static (ChartSeries Series, ComponentConfig Comp) Window(double[] values, double[]? path = null, bool? drawPath = null)
    {
        var comp = new ComponentConfig
        {
            Name = "Season", DisplayName = "Season", DisplayType = ComponentDisplayType.Window,
            PathName = "cycle arc", UserDrawPath = drawPath, IsVisible = true,
        };
        var config = new SeriesConfig { Id = "win", Name = "Win", FriendlyName = "Win", Pane = "Main" };
        config.Components.Add(comp);
        var series = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "win" });
        series.Data.ComponentData["Season"] = values;
        if (path != null) series.Data.ComponentData["Season" + ComponentCompanions.Path] = path;
        return (series, comp);
    }

    private static double[] Path(double price) => Enumerable.Repeat(price, N).ToArray();

    // ── Where a window is open ─────────────────────────────────────────────────────

    /// <summary>
    /// With no tint companion a window is open wherever it has a value; with one, wherever the
    /// companion is at or above its midline. The edges are the bars it opens on and the first
    /// bar no longer open.
    /// </summary>
    [Fact]
    public void A_window_is_open_where_it_has_a_value_or_where_its_tint_companion_says()
    {
        var (series, comp) = Window(OpenOn((3, 6), (9, 10)));
        var mask = WindowSpans.OpenMask(series, comp);
        Assert.Equal(new[] { 3, 4, 5, 6, 9, 10 }, Enumerable.Range(0, N).Where(i => mask[i] == 1));

        var edges = WindowSpans.Edges(mask);
        Assert.Equal(new[] { 3, 7, 9, 11 }, Enumerable.Range(0, N).Where(i => !double.IsNaN(edges[i])));
        Assert.Equal(new double[] { 1, 0, 1, 0 }, new[] { edges[3], edges[7], edges[9], edges[11] });

        // A count on every bar, open only where the tint companion is at or above the declared
        // midline (Cipher C: 1 inside, 0 outside, midline 0.5).
        var (tinted, tComp) = Window(Enumerable.Range(1, N).Select(i => (double)i).ToArray());
        tComp.TintMidline = 0.5;
        tinted.Data.ComponentData["Season" + ComponentCompanions.Tint] =
            Enumerable.Range(0, N).Select(i => i is >= 5 and <= 7 ? 1.0 : 0.0).ToArray();
        var tMask = WindowSpans.OpenMask(tinted, tComp);
        Assert.Equal(new[] { 5, 6, 7 }, Enumerable.Range(0, N).Where(i => tMask[i] == 1));
    }

    // ── What the user hears on landing ─────────────────────────────────────────────

    /// <summary>
    /// Landing on a window names its drawing when there is one on the chart and it is drawn —
    /// both the short type label and the speech template's {type} — and only then.
    /// </summary>
    [Fact]
    public void Landing_on_a_window_names_its_drawing_only_when_it_is_drawn()
    {
        var (withArc, comp) = Window(OpenOn((3, 6)), Path(100));
        Assert.Equal("Window, with the cycle arc", ProviderSpeechStrategy.ComponentTypeLabel(comp, withArc));
        Assert.Equal("window, with the cycle arc", SpeechFormatter.SpokenType(comp, withArc));

        var (switchedOff, offComp) = Window(OpenOn((3, 6)), Path(100), drawPath: false);
        Assert.Equal("Window", ProviderSpeechStrategy.ComponentTypeLabel(offComp, switchedOff));
        Assert.Equal("window", SpeechFormatter.SpokenType(offComp, switchedOff));

        // Cipher C's weekly window on a chart with a daily cycle: the array is there, all NaN.
        var (empty, emptyComp) = Window(OpenOn((3, 6)), Path(double.NaN));
        Assert.Equal("Window", ProviderSpeechStrategy.ComponentTypeLabel(emptyComp, empty));
        Assert.Equal("window", SpeechFormatter.SpokenType(emptyComp, empty));
    }

    // ── The fitted range ───────────────────────────────────────────────────────────

    /// <summary>
    /// A window's count is not a price, so it never moves the axis; its drawing is at prices, so
    /// it does — an arc under the candles is not cut off — unless it is switched off.
    /// </summary>
    [Fact]
    public void The_fitted_range_takes_the_drawing_and_not_the_count()
    {
        var (series, comp) = Window(OpenOn((0, N - 1)), Path(80));
        Assert.Equal(new[] { "Season" + ComponentCompanions.Path },
            ViewportRangeCalculator.DrawnArrays(series, comp).Select(a => ReferenceEquals(a, series.GetComponentData("Season")) ? "Season" : "Season" + ComponentCompanions.Path));

        Assert.True(Range(series).Min <= 80, "the drawing at 80, under candles at 91+, is outside the fitted range");

        comp.UserDrawPath = false;
        Assert.Empty(ViewportRangeCalculator.DrawnArrays(series, comp));
        Assert.True(Range(series).Min > 85, "a switched-off drawing still widens the range");
    }

    private static (double Min, double Max) Range(ChartSeries series)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = Enumerable.Range(0, N).Select(i => new Ohlcv(start.AddDays(i), 95, 101, 91, 96, 1000)).ToList();
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(series),
            ViewportStartIndex = 0,
            ViewportLength = N,
        };
        return new ViewportRangeCalculator().Calculate(state).MainRange;
    }

    // ── The Dot Pad ────────────────────────────────────────────────────────────────

    /// <summary>
    /// On the Dot Pad a window is raised where it is open — what the screen shades — not a
    /// line through its count.
    /// </summary>
    [Fact]
    public void On_the_dot_pad_a_window_is_raised_where_it_is_open()
    {
        var (series, comp) = Window(OpenOn((3, 6)));
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, N).Select(i => new Ohlcv(start.AddDays(i), 1, 1, 1, 1, 0))),
            ActiveSeries = ImmutableList.Create(series),
            ViewportStartIndex = 0,
            ViewportLength = N,
            PrimarySeriesId = "candles",
        };
        const int cols = N * 3, rows = 8;
        var canvas = TactileCanvasCoordinator.BuildSeriesCanvas(series, comp, state, cols, rows);

        // One column per bar: four raised (bars 3 to 6), each from the floor up; nothing else.
        var raised = Enumerable.Range(0, cols).Select(c => Enumerable.Range(0, rows).Count(r => canvas[c, r])).ToArray();
        Assert.Equal(4, raised.Count(n => n > 0));
        Assert.All(raised.Where(n => n > 0), n => Assert.Equal(rows, n));
        int first = Array.FindIndex(raised, n => n > 0), last = Array.FindLastIndex(raised, n => n > 0);
        Assert.True(first > cols * 3 / N - 1 && last < cols * 7 / N + 1, $"raised columns {first}..{last} are not bars 3 to 6");
    }

    // ── Fill under the line ────────────────────────────────────────────────────────

    /// <summary>The user's fill choice wins over the indicator's, both ways.</summary>
    [Fact]
    public void The_users_fill_choice_wins_over_the_indicators()
    {
        var line = new ComponentConfig { DisplayType = ComponentDisplayType.Line };
        Assert.False(StandardRenderers.IsFilled(line));
        line.UserIsAreaFill = true;
        Assert.True(StandardRenderers.IsFilled(line));

        var declared = new ComponentConfig { DisplayType = ComponentDisplayType.Histogram, IsAreaFill = true };
        Assert.True(StandardRenderers.IsFilled(declared));
        declared.UserIsAreaFill = false;
        Assert.False(StandardRenderers.IsFilled(declared));
    }

    /// <summary>
    /// The switch is offered only where filling works: a plain line or a value-coloured histogram.
    /// A level, a line coloured by rules or by another component, a candle-coloured bar and a
    /// window are drawn piece by piece, or not as a line at all.
    /// </summary>
    [Theory]
    [InlineData(ComponentDisplayType.Line, true)]
    [InlineData(ComponentDisplayType.Oscillator, true)]
    [InlineData(ComponentDisplayType.Histogram, true)]
    [InlineData(ComponentDisplayType.Bar, true)]
    [InlineData(ComponentDisplayType.Window, false)]
    [InlineData(ComponentDisplayType.Dot, false)]
    [InlineData(ComponentDisplayType.Area, false)]
    public void The_fill_switch_is_offered_where_it_works(ComponentDisplayType type, bool offered) =>
        Assert.Equal(offered, StandardRenderers.CanHaveAreaFill(new ComponentConfig { DisplayType = type }));

    [Fact]
    public void The_fill_switch_is_not_offered_on_a_line_drawn_piece_by_piece()
    {
        Assert.False(StandardRenderers.CanHaveAreaFill(new ComponentConfig { DisplayType = ComponentDisplayType.Line, IsZoneLine = true }));
        Assert.False(StandardRenderers.CanHaveAreaFill(new ComponentConfig { DisplayType = ComponentDisplayType.Line, ColorAboveComponent = "Signal" }));
        Assert.False(StandardRenderers.CanHaveAreaFill(new ComponentConfig
        {
            DisplayType = ComponentDisplayType.Line,
            ColorRules = new List<ColorRule> { new() { Condition = ColorCondition.AboveLevel, Level = 70, ColorHex = "#FF0000" } },
        }));
        Assert.False(StandardRenderers.CanHaveAreaFill(new ComponentConfig { DisplayType = ComponentDisplayType.Histogram, ColorSource = ColorSource.PriceAction }));
    }
}
