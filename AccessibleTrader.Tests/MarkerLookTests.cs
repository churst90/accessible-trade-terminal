using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;
using Xunit.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>"Say what signals look like"</b>: a signal is said with what it looks like on the chart,
/// "Buy. Drawn as a large green dot.", so it can be matched to what a sighted trader calls it.
///
/// <para>
/// Cody, 2026-10-22: <i>"then have the descriptors been added, if not, let's work on this"</i>.
/// The oracle for the colour is the PIXEL the chart paints: every marker shape is drawn through
/// the real <see cref="DataLayer"/> and the colour named must be the name of the colour that
/// covers the most of the marker. A colour rule, a Money Flow dot's sign and a gradient dot each
/// repaint the marker per bar, and a description read from the declared colour would get those
/// wrong.
/// </para>
/// </summary>
public class MarkerLookTests
{
    private readonly ITestOutputHelper _out;
    public MarkerLookTests(ITestOutputHelper output) => _out = output;

    // ── Colour names ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("#00E676", "green")]
    [InlineData("#3FFF00", "green")]
    [InlineData("#FF1744", "red")]
    [InlineData("#26C6DA", "cyan")]
    [InlineData("#FF9800", "orange")]
    [InlineData("#FFD700", "yellow")]
    [InlineData("#2196F3", "blue")]
    [InlineData("#FFFFFF", "white")]
    [InlineData("#000000", "black")]
    [InlineData("#808080", "gray")]
    [InlineData("#8B0000", "dark red")]
    [InlineData("#9C27B0", "purple")]
    [InlineData("#E91E63", "pink")]
    [InlineData("#FF00FF", "magenta")]
    [InlineData("#8B4513", "brown")]
    [InlineData("#78909C", "gray")]        // Cipher C's pending box: a blue gray, gray to the eye
    [InlineData("#CCCCCC", "light gray")]
    [InlineData("#EF9A9A", "light red")]
    [InlineData("#FF3D00", "red")]         // the gradient dot's overbought end
    [InlineData("#FF6D00", "orange")]      // Cipher C's Cycle Failed X
    public void Colours_are_named_as_people_name_them(string hex, string expected)
    {
        Assert.Equal(expected, MarkerLook.ColorName(SKColor.Parse(hex)));
    }

    // ── What is described ─────────────────────────────────────────────────────────

    private static ChartSeries One(ComponentConfig comp, double[] data, string pane = "Main", string id = "sig")
    {
        var cfg = new SeriesConfig
        {
            Id = id, Name = id, FriendlyName = id, IndicatorCode = id.ToUpperInvariant(), Pane = pane,
            IsVisible = true, IsAutoNarrated = true,
        };
        cfg.Components.Add(comp);
        var buf = new SeriesDataBuffer { SeriesId = id };
        buf.ComponentData[comp.Name] = data;
        return new ChartSeries(cfg, buf);
    }

    private static ComponentConfig Marker(ComponentDisplayType type, string hex = "#00E676", float thickness = 3f,
        string name = "Buy", string? template = "Buy.") => new()
    {
        Name = name, DisplayName = name, DisplayType = type, Role = ComponentRole.Signal,
        IsVisible = true, ColorHex = hex, Thickness = thickness, SignalSpeechTemplate = template,
    };

    [Fact]
    public void A_big_green_dot_is_a_large_green_dot()
    {
        var comp = Marker(ComponentDisplayType.Dot, thickness: 5f);
        Assert.Equal("Drawn as a large green dot.", MarkerLook.Sentence(One(comp, new[] { 50.0 }), comp, 0));
    }

    [Theory]
    [InlineData(1.5f, "small green dot")]
    [InlineData(3f, "green dot")]
    [InlineData(3.5f, "green dot")]
    [InlineData(5f, "large green dot")]
    public void Size_follows_the_thickness(float thickness, string expected)
    {
        var comp = Marker(ComponentDisplayType.Dot, thickness: thickness);
        Assert.Equal(expected, MarkerLook.Describe(One(comp, new[] { 50.0 }), comp, 0));
    }

    [Fact]
    public void A_colour_rule_is_what_is_said_not_the_declared_colour()
    {
        var comp = Marker(ComponentDisplayType.Dot, hex: "#FFFFFF");
        comp.ColorRules.Add(new ColorRule { Condition = ColorCondition.BelowZero, ColorHex = "#FF1744" });
        var s = One(comp, new[] { 3.0, -3.0 });
        Assert.Equal("white dot", MarkerLook.Describe(s, comp, 0));
        Assert.Equal("red dot", MarkerLook.Describe(s, comp, 1));
    }

    [Fact]
    public void A_zero_dot_is_coloured_by_its_sign()
    {
        var comp = Marker(ComponentDisplayType.ZeroDot, hex: "#FFFFFF");
        var s = One(comp, new[] { 2.0, -2.0 });
        Assert.Equal("green dot", MarkerLook.Describe(s, comp, 0));
        Assert.Equal("red dot", MarkerLook.Describe(s, comp, 1));
    }

    [Fact]
    public void An_arrow_points_the_way_its_sign_does()
    {
        var comp = Marker(ComponentDisplayType.Arrow, hex: "#FF9800");
        var s = One(comp, new[] { 1.0, -1.0 });
        Assert.Equal("an orange up arrow", MarkerLook.Sentence(s, comp, 0)[9..^1]);
        Assert.Equal("orange down arrow", MarkerLook.Describe(s, comp, 1));
    }

    [Theory]
    [InlineData(MarkerAnchor.BelowBar, "green up triangle below the candle")]
    [InlineData(MarkerAnchor.AboveBar, "green up triangle above the candle")]
    [InlineData(MarkerAnchor.Value, "green up triangle")]
    public void A_marker_pinned_to_the_candle_says_where(MarkerAnchor anchor, string expected)
    {
        var comp = Marker(ComponentDisplayType.TriangleUp);
        comp.MarkerAnchor = anchor;
        Assert.Equal(expected, MarkerLook.Describe(One(comp, new[] { 50.0 }), comp, 0));
    }

    [Fact]
    public void A_ring_says_its_letter()
    {
        var comp = Marker(ComponentDisplayType.TriangleUpRing, hex: "#FFD700");
        comp.MarkerText = "B";
        Assert.Equal("yellow up triangle with a ring marked B", MarkerLook.Describe(One(comp, new[] { 50.0 }), comp, 0));
    }

    [Fact]
    public void Nothing_drawn_nothing_said()
    {
        var dot = Marker(ComponentDisplayType.Dot);
        Assert.Equal("", MarkerLook.Sentence(One(dot, new[] { double.NaN }), dot, 0));
        Assert.Equal("", MarkerLook.Sentence(One(dot, new[] { 50.0 }), dot, 5));

        var hidden = Marker(ComponentDisplayType.Dot);
        hidden.IsVisible = false;
        Assert.Equal("", MarkerLook.Sentence(One(hidden, new[] { 50.0 }), hidden, 0));

        // A line is not a marker: it has no "look" on one bar.
        var line = Marker(ComponentDisplayType.Line);
        Assert.Equal("", MarkerLook.Sentence(One(line, new[] { 50.0 }), line, 0));
    }

    // ── The colour said is the colour painted ─────────────────────────────────────

    private const int W = 120, H = 120;

    private static ChartTheme Theme()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings).Current;
    }

    /// <summary>The colour covering the most pixels that are not the black background.</summary>
    private static (SKColor Colour, int Pixels) Painted(ChartSeries series, int bar)
    {
        var bars = Enumerable.Range(0, series.GetComponentData(series.Components[0].Name)!.Length)
            .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddDays(i), 0, 10, -10, 0, 1000)).ToList();
        using var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Black);
            var r = SKRect.Create(0, 0, W, H);
            var ctx = new RenderContext(canvas, r, bars, bar, 1, -100, 100, false, r.Width, 1f, "Main", 0, Theme());
            var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
            new DataLayer(styling).Render(ctx, new[] { series });
        }
        var counts = new Dictionary<SKColor, int>();
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            var p = bmp.GetPixel(x, y);
            if (p.Red < 6 && p.Green < 6 && p.Blue < 6) continue;
            counts[p] = counts.GetValueOrDefault(p) + 1;
        }
        if (counts.Count == 0) return (SKColors.Black, 0);
        var top = counts.MaxBy(kv => kv.Value);
        return (top.Key, top.Value);
    }

    public static IEnumerable<object[]> EveryMarker() => new[]
    {
        new object[] { ComponentDisplayType.Dot, "#00E676", 50.0 },
        new object[] { ComponentDisplayType.ZeroDot, "#FFFFFF", 5.0 },
        new object[] { ComponentDisplayType.ZeroDot, "#FFFFFF", -5.0 },
        new object[] { ComponentDisplayType.Arrow, "#FF9800", -5.0 },
        new object[] { ComponentDisplayType.Diamond, "#26C6DA", 50.0 },
        new object[] { ComponentDisplayType.TriangleUp, "#3FFF00", 50.0 },
        new object[] { ComponentDisplayType.TriangleUpRing, "#FFD700", 50.0 },
        new object[] { ComponentDisplayType.TriangleDown, "#FF1744", 50.0 },
        new object[] { ComponentDisplayType.Square, "#9C27B0", 50.0 },
        new object[] { ComponentDisplayType.CheckBox, "#2196F3", 50.0 },
        new object[] { ComponentDisplayType.RefreshBox, "#E91E63", 50.0 },
        new object[] { ComponentDisplayType.Cross, "#FF9800", 50.0 },
    };

    [Theory]
    [MemberData(nameof(EveryMarker))]
    public void The_colour_said_is_the_colour_painted(ComponentDisplayType type, string hex, double value)
    {
        var comp = Marker(type, hex: hex, thickness: 8f);
        var s = One(comp, new[] { value });
        AssertSaidMatchesPainted(s, comp, 0);
    }

    [Fact]
    public void Every_marker_shape_is_in_the_pixel_sweep()
    {
        // A shape added to MarkerDisplayTypes without a row above would be described from a
        // colour nobody checked against the picture.
        var swept = EveryMarker().Select(r => (ComponentDisplayType)r[0]).ToHashSet();
        Assert.Empty(AudioConstants.MarkerDisplayTypes.Except(swept));
    }

    [Fact]
    public void A_colour_rule_is_painted_and_said_alike()
    {
        var comp = Marker(ComponentDisplayType.Dot, hex: "#FFFFFF", thickness: 8f);
        comp.ColorRules.Add(new ColorRule { Condition = ColorCondition.BelowZero, ColorHex = "#FF1744" });
        var s = One(comp, new[] { 30.0, -30.0 });
        AssertSaidMatchesPainted(s, comp, 0);
        AssertSaidMatchesPainted(s, comp, 1);
        Assert.Contains("red", MarkerLook.Describe(s, comp, 1));
    }

    [Theory]
    [InlineData(80.0, "red")]
    [InlineData(0.0, "gray")]
    [InlineData(-80.0, "green")]   // #00E676 at full strength
    public void A_gradient_dot_is_painted_and_said_alike(double companion, string expected)
    {
        var comp = Marker(ComponentDisplayType.Dot, hex: "#FFFFFF", thickness: 8f);
        var s = One(comp, new[] { 30.0 });
        s.Data.ComponentData["Buy_color"] = new[] { companion };
        AssertSaidMatchesPainted(s, comp, 0);
        _out.WriteLine(MarkerLook.Describe(s, comp, 0));
        Assert.Contains(expected, MarkerLook.Describe(s, comp, 0));
    }

    private void AssertSaidMatchesPainted(ChartSeries s, ComponentConfig comp, int bar)
    {
        var (colour, pixels) = Painted(s, bar);
        Assert.True(pixels > 20, $"{comp.DisplayType}: almost nothing was painted ({pixels} px); the sweep is not drawing");
        string said = MarkerLook.Describe(s, comp, bar);
        _out.WriteLine($"{comp.DisplayType} bar {bar}: painted {colour} = {MarkerLook.ColorName(colour)}; said '{said}'");
        Assert.StartsWith($"large {MarkerLook.ColorName(colour)} ", said);
    }

    // ── Each place a signal is said ───────────────────────────────────────────────

    private static TimeSeriesBuffer<Ohlcv> Bars(int n) => new(Enumerable.Range(0, n)
        .Select(i => new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i), 100, 101, 99, 100, 1000)));

    private static WorkspaceState NavState(int index, bool describe, string focusedId, params ChartSeries[] series) =>
        WorkspaceState.Initial with
        {
            Data = Bars(3),
            ActiveSeries = series.ToImmutableList(),
            FocusedSeriesId = focusedId,
            FocusedComponentIndex = 0,
            CurrentDataIndex = index,
            InitStatus = InitializationStatus.Ready,
            LastInteractionContext = InteractionContext.Component,
            SpeakTimestamps = false,
            TimestampReadLocation = "None",
            ReadColumnHeaders = false,
            SpeechOrder = "ValueOnly",
            DescribeSignalLooks = describe,
        };

    private static string Navigate(WorkspaceState state)
    {
        var spy = new SpySpeechRouter();
        new NavigationFeedbackManager(spy, new SpeechFormatter()) { IsSpeechEnabled = true }
            .HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");
        return string.Join(" | ", spy.SpokenTexts);
    }

    private static ChartSeries BigBuy() =>
        One(Marker(ComponentDisplayType.Dot, thickness: 5f), new[] { double.NaN, 99.0, double.NaN });

    [Fact]
    public void Arrowing_onto_a_focused_signal_says_its_look_when_asked()
    {
        var s = BigBuy();
        string on = Navigate(NavState(1, describe: true, s.Id, s));
        _out.WriteLine(on);
        Assert.Contains("Drawn as a large green dot.", on);
        Assert.Contains("Buy", on);
        Assert.True(on.IndexOf("Buy", StringComparison.Ordinal) < on.IndexOf("Drawn as", StringComparison.Ordinal),
            $"the look came before the signal: '{on}'");

        string off = Navigate(NavState(1, describe: false, s.Id, s));
        Assert.DoesNotContain("Drawn as", off);
        Assert.Contains("Buy", off);
    }

    [Fact]
    public void A_series_summary_says_each_signal_with_its_look()
    {
        // Series context reads every component of the indicator in one sentence each; the
        // look rides with the signal and the join does not double the full stop.
        var s = BigBuy();
        var state = NavState(1, describe: true, s.Id, s) with { LastInteractionContext = InteractionContext.Series };
        string on = Navigate(state);
        _out.WriteLine(on);
        Assert.Contains("Buy. Drawn as a large green dot. ", on);
        Assert.DoesNotContain("..", on);

        Assert.DoesNotContain("Drawn as", Navigate(state with { DescribeSignalLooks = false }));
    }

    [Fact]
    public void A_bar_with_no_signal_has_no_look()
    {
        var s = BigBuy();
        Assert.DoesNotContain("Drawn as", Navigate(NavState(0, describe: true, s.Id, s)));
    }

    [Fact]
    public void Another_series_signal_on_the_bar_says_its_look()
    {
        var focus = One(Marker(ComponentDisplayType.Line, name: "Line", template: null), new[] { 1.0, 2.0, 3.0 }, id: "line");
        var sell = Marker(ComponentDisplayType.TriangleDown, hex: "#FF1744", name: "Sell", template: "Sell.");
        sell.MarkerAnchor = MarkerAnchor.AboveBar;
        var other = One(sell, new[] { double.NaN, 101.0, double.NaN }, id: "sells");

        string on = Navigate(NavState(1, describe: true, focus.Id, focus, other));
        _out.WriteLine(on);
        Assert.Contains("Sell. Drawn as a red down triangle above the candle", on);

        string off = Navigate(NavState(1, describe: false, focus.Id, focus, other));
        Assert.Contains("Sell", off);
        Assert.DoesNotContain("Drawn as", off);
    }

    [Fact]
    public void Playback_says_the_look_with_the_signal()
    {
        var s = One(Marker(ComponentDisplayType.Diamond, hex: "#26C6DA"), new[] { double.NaN, 99.0, double.NaN });
        var state = WorkspaceState.Initial with
        {
            Data = Bars(3),
            ActiveSeries = ImmutableList.Create(s),
            PrimarySeriesId = s.Id,
            FocusedSeriesId = s.Id,
            CurrentDataIndex = 1,
            ViewportStartIndex = 0,
            ViewportLength = 3,
            NarrateDuringPlayback = true,
            IsPlaying = true,
            DescribeSignalLooks = true,
        };
        string? on = PlaybackNarration.SignalsForStep(state, 1, null);
        _out.WriteLine(on);
        Assert.Contains("Buy. Drawn as a cyan diamond.", on);

        string? off = PlaybackNarration.SignalsForStep(state with { DescribeSignalLooks = false }, 1, null);
        Assert.Contains("Buy", off);
        Assert.DoesNotContain("Drawn as", off);
    }

    [Fact]
    public void A_signal_narrated_at_the_close_says_its_look_after_its_date()
    {
        string? Scan(bool describe)
        {
            var low = Marker(ComponentDisplayType.TriangleUp, name: "Low", template: "Cycle low.");
            low.IsHindsight = true;
            low.MarkerAnchor = MarkerAnchor.BelowBar;
            var data = Enumerable.Repeat(double.NaN, 50).ToArray();
            var s = One(low, data, id: "cyc");
            WorkspaceState State(int n) => WorkspaceState.Initial with
            {
                Data = Bars(n),
                Identity = WorkspaceState.Initial.Identity with { Timeframe = "1d" },
                ActiveSeries = ImmutableList.Create(s),
                CurrentDataIndex = n - 1,
                InitStatus = InitializationStatus.Ready,
                DataStatus = DataStatus.Ready,
                IsSpeechEnabled = true,
                DescribeSignalLooks = describe,
            };
            var scanner = new NarrationScanner(new IndicatorContextAnalyzer());
            scanner.Seed(s, State(40));
            data[35] = 99;
            return scanner.ScanAll(new[] { s }, State(46), 44, isBarClose: true);
        }

        string? on = Scan(true);
        _out.WriteLine(on);
        string date = PlaybackNarration.DateText(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(35), 86400);
        Assert.Contains($"Cycle low, on {date}. Drawn as a green up triangle below the candle.", on);

        string? off = Scan(false);
        Assert.Contains($"Cycle low, on {date}", off);
        Assert.DoesNotContain("Drawn as", off);
    }
}
