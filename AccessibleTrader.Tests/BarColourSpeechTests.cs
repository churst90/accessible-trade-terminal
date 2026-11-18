using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Bars say their colour as lines do</b> (Cody, 2026-11-18: "consistency"). A volume bar and a
/// histogram are painted by direction on every bar, so the word arrives with the component and
/// then only where the colour's NAME changes — never on every bar of a sweep.
///
/// <para>The colour is <see cref="StandardRenderers.ResolveBarColor"/>/the paint
/// <c>RenderDirectionalBars</c> uses, so a colour rule (MACD's four), a recolouring and volume's
/// candle-direction pair all say what is actually drawn. The same rule as
/// <see cref="MarkerLook.LineSentence"/>: arrival, then only on change, either direction and after
/// a jump.</para>
/// </summary>
public sealed class BarColourSpeechTests
{
    private static IThemeService FixedTheme()
    {
        var theme = BaseThemeResolver.Resolve(ThemeType.Classic) with
        {
            CandleBullishBody = new SKColor(0x00, 0xE6, 0x76),
            CandleBearishBody = new SKColor(0xFF, 0x17, 0x44),
        };
        var service = Substitute.For<IThemeService>();
        service.Current.Returns(theme);
        return service;
    }

    private static ChartSeries One(ComponentConfig comp, double[] data,
        string id = "bars", string pane = "Main")
    {
        var cfg = new SeriesConfig
        {
            Id = id, Name = id, FriendlyName = id, IndicatorCode = id.ToUpperInvariant(),
            Pane = pane, IsVisible = true, IsAutoNarrated = true,
        };
        cfg.Components.Add(comp);
        var buf = new SeriesDataBuffer { SeriesId = id };
        buf.ComponentData[comp.Name] = data;
        return new ChartSeries(cfg, buf);
    }

    private static List<Ohlcv> Bars(int n)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, n)
            .Select(i => new Ohlcv(start.AddDays(i), 100, 101, 99, 100, 1000))
            .ToList();
    }

    private static List<Ohlcv> DirectionBars(int n, bool rising)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, n)
            .Select(i => rising
                ? new Ohlcv(start.AddDays(i), 100, 102, 99, 101, 1000)
                : new Ohlcv(start.AddDays(i), 101, 102, 99, 100, 1000))
            .ToList();
    }

    private static WorkspaceState NavState(int index, bool describe, ChartSeries series, List<Ohlcv> bars) =>
        WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
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

    private static List<string> Visit(ChartSeries series, List<Ohlcv> bars, bool describe, IThemeService? theme,
        params int[] path)
    {
        var spy = new SpySpeechRouter();
        var formatter = theme is null
            ? new SpeechFormatter()
            : new SpeechFormatter(NullLogger<SpeechFormatter>.Instance, themeService: theme);
        var nav = new NavigationFeedbackManager(spy, formatter) { IsSpeechEnabled = true };
        var said = new List<string>();
        for (int i = 0; i < path.Length; i++)
        {
            var state = NavState(path[i], describe, series, bars);
            nav.HandleNavigationFeedback(state, isXMove: i > 0, isYMove: i == 0, prefixMessage: "");
            said.Add(spy.SpokenTexts.Count > 0 ? spy.SpokenTexts[^1] : "");
            spy.SpokenTexts.Clear();
        }
        return said;
    }

    // ── MACD's four colours ────────────────────────────────────────────────────

    private static ComponentConfig FourShadeHistogram()
    {
        var comp = new ComponentConfig
        {
            Name = "Histogram", DisplayName = "Histogram", DisplayType = ComponentDisplayType.Histogram,
            Role = ComponentRole.Histogram, IsVisible = true, IsEnabled = true,
            ColorHex = "#FFFFFFFF", ColorHexSecondary = "#FFFFFFFF",
            ColorSource = ColorSource.Value, ReferenceLevel = 0.0, Thickness = 1f,
        };
        comp.ColorRules.Add(new ColorRule { Condition = ColorCondition.AboveLevel, Level = 50, ColorHex = "#FF00E676" });  // green
        comp.ColorRules.Add(new ColorRule { Condition = ColorCondition.BelowLevel, Level = -50, ColorHex = "#FFFF1744" }); // red
        comp.ColorRules.Add(new ColorRule { Condition = ColorCondition.AboveZero, ColorHex = "#FF2196F3" });               // blue
        comp.ColorRules.Add(new ColorRule { Condition = ColorCondition.BelowZero, ColorHex = "#FFFFD700" });               // yellow
        return comp;
    }

    [Fact]
    public void A_four_colour_histogram_says_each_shade_on_arrival_and_only_on_change_going_right()
    {
        var comp = FourShadeHistogram();
        var values = new[] { 10.0, 10, -10, -10, 60, -60 };
        var series = One(comp, values);
        var said = Visit(series, Bars(values.Length), describe: true, theme: null, 1, 2, 3, 4, 5);

        Assert.Contains("Blue bar.", said[0]);
        Assert.Contains("Yellow bar.", said[1]);
        Assert.DoesNotContain("bar.", said[2]);
        Assert.Contains("Green bar.", said[3]);
        Assert.Contains("Red bar.", said[4]);
    }

    [Fact]
    public void A_four_colour_histogram_says_each_shade_going_left_too()
    {
        var comp = FourShadeHistogram();
        var values = new[] { 10.0, 10, -10, -10, 60, -60 };
        var series = One(comp, values);
        var said = Visit(series, Bars(values.Length), describe: true, theme: null, 5, 4, 3, 2, 1, 0);

        Assert.Contains("Red bar.", said[0]);      // arriving at the component
        Assert.Contains("Green bar.", said[1]);    // change
        Assert.Contains("Yellow bar.", said[2]);   // change
        Assert.DoesNotContain("bar.", said[3]);    // yellow again
        Assert.Contains("Blue bar.", said[4]);     // change
        Assert.DoesNotContain("bar.", said[5]);    // blue again
    }

    [Fact]
    public void A_jump_says_the_colour_only_when_it_lands_on_a_different_one()
    {
        var comp = FourShadeHistogram();
        var values = new[] { 10.0, 10, -10, -10, 60, -60 };
        var series = One(comp, values);
        var said = Visit(series, Bars(values.Length), describe: true, theme: null, 4, 1, 2, 3, 5, 0);

        Assert.Contains("Green bar.", said[0]);    // arriving
        Assert.Contains("Blue bar.", said[1]);     // green to blue
        Assert.Contains("Yellow bar.", said[2]);   // blue to yellow
        Assert.DoesNotContain("bar.", said[3]);    // yellow to yellow
        Assert.Contains("Red bar.", said[4]);      // yellow to red
        Assert.Contains("Blue bar.", said[5]);     // red to blue
    }

    [Fact]
    public void With_looks_off_a_bar_says_no_colour()
    {
        var comp = FourShadeHistogram();
        var values = new[] { 10.0, 10, -10, -10, 60, -60 };
        var series = One(comp, values);
        var said = Visit(series, Bars(values.Length), describe: false, theme: null, 1, 2, 3, 4, 5);

        Assert.All(said, s => Assert.DoesNotContain("bar.", s));
    }

    // ── Volume: the candle-direction colours the volume renderer uses ──────────

    private static ComponentConfig VolumeBar() => new()
    {
        Name = "Volume", DisplayName = "Volume", DisplayType = ComponentDisplayType.Bar,
        Role = ComponentRole.Volume, IsVisible = true, IsEnabled = true,
        ColorHex = "#26A69A", ColorHexSecondary = "#EF5350",
        ColorSource = ColorSource.PriceAction, Thickness = 1f,
    };

    [Fact]
    public void Volume_says_green_on_an_up_candle_and_red_on_a_down_one()
    {
        var comp = VolumeBar();
        var series = One(comp, new[] { 1000.0, 1000, 1000, 1000, 1000, 1000 });
        var bars = new List<Ohlcv>
        {
            new(new DateTime(2026, 1, 1), 100, 101, 99, 101, 1000),   // up
            new(new DateTime(2026, 1, 2), 101, 102, 99, 100, 1000),   // down
            new(new DateTime(2026, 1, 3), 101, 102, 99, 100, 1000),   // down
            new(new DateTime(2026, 1, 4), 100, 101, 99, 101, 1000),   // up
            new(new DateTime(2026, 1, 5), 100, 101, 99, 101, 1000),   // up
            new(new DateTime(2026, 1, 6), 101, 102, 99, 100, 1000),   // down
        };
        var said = Visit(series, bars, describe: true, theme: FixedTheme(), 0, 1, 2, 3, 4, 5);

        _ = string.Join(" | ", said);
        Assert.Contains("Green bar.", said[0]);
        Assert.Contains("Red bar.", said[1]);
        Assert.DoesNotContain("bar.", said[2]);
        Assert.Contains("Green bar.", said[3]);
        Assert.DoesNotContain("bar.", said[4]);
        Assert.Contains("Red bar.", said[5]);
    }

    [Fact]
    public void A_recoloured_histogram_says_its_new_colour()
    {
        var comp = new ComponentConfig
        {
            Name = "Histogram", DisplayName = "Histogram", DisplayType = ComponentDisplayType.Histogram,
            Role = ComponentRole.Histogram, IsVisible = true, IsEnabled = true,
            ColorHex = "#FFFFFFFF", ColorHexSecondary = "#FFFFFFFF", Thickness = 1f,
            ColorSource = ColorSource.Value, ReferenceLevel = 0.0,
        };
        var series = One(comp, new[] { 10.0 });
        var theme = FixedTheme().Current;
        Assert.Equal("white bar", MarkerLook.DescribeBar(theme, series, comp, 0, null));

        comp.ColorHex = "#FFFF1744";
        Assert.Equal("red bar", MarkerLook.DescribeBar(theme, series, comp, 0, null));
    }

    [Fact]
    public void A_bar_that_is_not_drawn_says_nothing()
    {
        var comp = VolumeBar();
        var series = One(comp, new[] { double.NaN });
        Assert.Equal("", MarkerLook.DescribeBar(FixedTheme().Current, series, comp, 0, null));
        Assert.Equal("", MarkerLook.DescribeBar(FixedTheme().Current, series, comp, -1, null));

        comp.IsVisible = false;
        var visible = One(comp, new[] { 1000.0 });
        Assert.Equal("", MarkerLook.DescribeBar(FixedTheme().Current, visible, comp, 0, null));
    }
}
