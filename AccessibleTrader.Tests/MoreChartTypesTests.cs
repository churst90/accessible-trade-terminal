using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Charts;
using AccessibleTrader.Core.Services.GoBar;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using AccessibleTrader.Tests.Mocks;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// The six chart types that draw the chart's own time bars another way (Cody, 2026-11-20):
/// bars, hollow candles, baseline, high-low, HLC area and volume candles. Each is drawn, heard,
/// said and shown on the status line alike — Cody's rule that "a totally blind keyboard/speech
/// user can get the same information that a sighted person can".
/// </summary>
public sealed class MoreChartTypesTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ChartIdentity Id = new("Spot", "Bitstamp", "BTC/USD", "1h");

    /// <summary>
    /// Bar 1 closes under its open and under the close before (filled red); bar 2 closes over
    /// both (hollow green); bar 3 closes under its open but over the close before (filled
    /// GREEN, which a plain candle would draw red); bar 9's volume is four times the rest.
    /// </summary>
    private static readonly Ohlcv[] Sample =
    {
        new(Start, 100, 103, 99, 102, 10),
        new(Start.AddHours(1), 102, 104, 100, 101, 10),
        new(Start.AddHours(2), 101, 106, 100, 103, 10),
        new(Start.AddHours(3), 106, 107, 103, 104, 10),
        new(Start.AddHours(4), 104, 105, 103, 104.5, 10),
        new(Start.AddHours(5), 104.5, 106, 104, 105, 10),
        new(Start.AddHours(6), 105, 106, 104, 105.5, 10),
        new(Start.AddHours(7), 105.5, 107, 105, 106, 10),
        new(Start.AddHours(8), 106, 107, 105, 106.5, 10),
        new(Start.AddHours(9), 106.5, 112, 98, 108, 40),
    };

    private static SeriesConfig Candles()
    {
        var config = new SeriesConfig { Id = "candles", IndicatorCode = "candles", Name = "Candles", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "upper_wick", DisplayName = "Upper Wick", IsVisible = true,
            DisplayType = ComponentDisplayType.Wick, Role = ComponentRole.PriceAction, DataMapping = "high",
        });
        config.Components.Add(new ComponentConfig
        {
            Name = "body", DisplayName = "Body", IsVisible = true,
            DisplayType = ComponentDisplayType.Candle, Role = ComponentRole.Body, DataMapping = "close",
        });
        config.Components.Add(new ComponentConfig
        {
            Name = "lower_wick", DisplayName = "Lower Wick", IsVisible = true,
            DisplayType = ComponentDisplayType.Wick, Role = ComponentRole.PriceAction, DataMapping = "low",
        });
        return config;
    }

    private static WorkspaceState State(PriceChartStyle style, IReadOnlyList<Ohlcv>? bars = null,
        double basePrice = 0, int at = -1, int viewportStart = 0)
    {
        bars ??= Sample;
        var data = new SeriesDataBuffer { SeriesId = "candles" };
        data.ComponentData["body"] = bars.Select(b => b.Close).ToArray();
        data.ComponentData["upper_wick"] = bars.Select(b => b.High).ToArray();
        data.ComponentData["lower_wick"] = bars.Select(b => b.Low).ToArray();
        var state = WorkspaceState.Initial with
        {
            Identity = Id,
            Data = new TimeSeriesBuffer<Ohlcv>(bars.ToList()),
            Renko = new RenkoSettings(BaselinePrice: basePrice),
            ChartStyle = style,
            ActiveSeries = ImmutableList.Create(new ChartSeries(Candles(), data)),
            FocusedSeriesId = "candles",
            CurrentDataIndex = at < 0 ? bars.Count - 1 : at,
            ViewportStartIndex = viewportStart,
            ViewportLength = bars.Count,
            LastInteractionContext = InteractionContext.Component,
            SpeakTimestamps = false,
            ReadColumnHeaders = true,
            SpeechOrder = "HeaderValue",
            InitStatus = InitializationStatus.Ready,
        };
        return PriceSeriesShape.Apply(state);
    }

    private static ChartSeries Price(WorkspaceState state) => state.ActiveSeries[0];

    private static ComponentConfig Part(WorkspaceState state, string name) =>
        Price(state).Components.Single(c => c.Name == name);

    // ---- The price series takes each type's shape --------------------------------------------

    [Fact]
    public void Each_type_names_the_price_series_and_gives_it_the_parts_it_draws()
    {
        var bars = State(PriceChartStyle.Bars);
        Assert.Equal("Bars", Price(bars).Config.Name);
        Assert.Equal(PriceSeriesShape.BarPart, Assert.Single(Price(bars).Components).Name);
        Assert.Equal("Bar", Price(bars).Components[0].DisplayName);

        var highLow = State(PriceChartStyle.HighLow);
        var hl = Assert.Single(Price(highLow).Components);
        Assert.Equal((PriceSeriesShape.HighLowPart, "High-low bar", "hl2"), (hl.Name, hl.DisplayName, hl.DataMapping));
        Assert.Equal(new[] { 101d, 102d }, Price(highLow).Data.ComponentData[PriceSeriesShape.HighLowPart][..2]);

        var baseline = State(PriceChartStyle.Baseline);
        Assert.Equal(PriceSeriesShape.BaselinePart, Assert.Single(Price(baseline).Components).Name);

        var hlc = State(PriceChartStyle.HlcArea);
        Assert.Equal(new[] { PriceSeriesShape.HlcHighPart, PriceSeriesShape.HlcClosePart, PriceSeriesShape.HlcLowPart },
            Price(hlc).Components.Select(c => c.Name));
        Assert.Equal(103, Price(hlc).Data.ComponentData[PriceSeriesShape.HlcHighPart][0]);
        Assert.Equal(99, Price(hlc).Data.ComponentData[PriceSeriesShape.HlcLowPart][0]);
        Assert.Equal(PriceSeriesShape.HlcClosePart, Price(hlc).Components[hlc.FocusedComponentIndex].Name);

        // Hollow and volume candles are candles: the three candle parts, named for the type.
        Assert.Equal("Hollow candles", Price(State(PriceChartStyle.HollowCandles)).Config.Name);
        Assert.Equal("Volume candles", Price(State(PriceChartStyle.VolumeCandles)).Config.Name);
        Assert.Equal(3, Price(State(PriceChartStyle.VolumeCandles)).Components.Count);
    }

    [Fact]
    public void Only_the_type_that_needs_a_hidden_value_carries_it()
    {
        var hollow = Price(State(PriceChartStyle.HollowCandles)).Data.ComponentData;
        Assert.Equal(new[] { 1d, 0d, 1d, 1d }, hollow[PriceSeriesShape.HollowGreenKey][..4]);
        Assert.False(hollow.ContainsKey(PriceSeriesShape.VolumeKey));

        var volume = Price(State(PriceChartStyle.VolumeCandles)).Data.ComponentData;
        Assert.Equal(40, volume[PriceSeriesShape.VolumeKey][9]);
        Assert.False(volume.ContainsKey(PriceSeriesShape.HollowGreenKey));

        var candles = Price(State(PriceChartStyle.Candles)).Data.ComponentData;
        Assert.DoesNotContain(candles.Keys, k => k.StartsWith("__", StringComparison.Ordinal));

        // Back to candles, the hollow colours go: the candle parts would otherwise still sound hollow.
        var back = PriceSeriesShape.Apply(State(PriceChartStyle.HollowCandles) with { ChartStyle = PriceChartStyle.Candles });
        Assert.False(Price(back).Data.ComponentData.ContainsKey(PriceSeriesShape.HollowGreenKey));
    }

    [Fact]
    public void The_baseline_base_is_the_chosen_price_or_the_first_close_in_view()
    {
        Assert.Equal(102, PriceSeriesShape.BaselineBase(Price(State(PriceChartStyle.Baseline)), 0));
        Assert.Equal(103, PriceSeriesShape.BaselineBase(Price(State(PriceChartStyle.Baseline)), 2));
        Assert.Equal(104, PriceSeriesShape.BaselineBase(Price(State(PriceChartStyle.Baseline, basePrice: 104)), 2));
        Assert.True(double.IsNaN(PriceSeriesShape.BaselineBase(Price(State(PriceChartStyle.Line)), 0)));
    }

    // ---- Heard -------------------------------------------------------------------------------

    private static AudioPoint Sound(WorkspaceState state, string part, int index, double? prev = null, int relativeIndex = -1)
    {
        var series = Price(state);
        var comp = Part(state, part);
        var bar = state.Data[index];
        double value = series.Data.ComponentData[part][index];
        return new DefaultSonificationStrategy(new SoundPatchRegistry())
            .CreateAudioPoint(series, comp, value, bar, relativeIndex < 0 ? index : relativeIndex, state.Data.Count,
                (95, 115), 1f, prevVal: prev, dataIndex: index);
    }

    [Fact]
    public void A_bar_sounds_thinner_than_a_candle_and_keeps_its_direction()
    {
        var bars = State(PriceChartStyle.Bars);
        var candles = State(PriceChartStyle.Candles);

        var up = Sound(bars, PriceSeriesShape.BarPart, 2);
        var down = Sound(bars, PriceSeriesShape.BarPart, 1);
        Assert.True(up.SquareMix < Sound(candles, "body", 2).SquareMix, "a bar is a thin stroke, not a filled body");
        Assert.True(up.SquareMix > down.SquareMix);
        Assert.True(down.TriangleMix > up.TriangleMix);
        Assert.True(up.SubSawMix > 0f);
    }

    [Fact]
    public void A_taller_high_low_bar_sounds_heavier()
    {
        var state = State(PriceChartStyle.HighLow);

        var narrow = Sound(state, PriceSeriesShape.HighLowPart, 4);   // 103 to 105
        var wide = Sound(state, PriceSeriesShape.HighLowPart, 9);     // 98 to 112
        Assert.True(wide.SubSawMix > narrow.SubSawMix, $"{narrow.SubSawMix} vs {wide.SubSawMix}");
    }

    [Fact]
    public void The_baseline_has_a_tone_each_side_of_the_base_and_clicks_on_crossing_it()
    {
        var state = State(PriceChartStyle.Baseline, basePrice: 104);

        var below = Sound(state, PriceSeriesShape.BaselinePart, 2, prev: 101);   // 103: under 104
        var above = Sound(state, PriceSeriesShape.BaselinePart, 5, prev: 104.5); // 105: over it
        Assert.True(below.TriangleMix > above.TriangleMix);
        Assert.True(above.SquareMix > below.SquareMix);
        Assert.False(below.TriggerClick);
        Assert.False(above.TriggerClick);

        var crossUp = Sound(state, PriceSeriesShape.BaselinePart, 4, prev: 103.5);   // 103.5 → 104.5
        Assert.True(crossUp.TriggerClick);
        Assert.Equal(1, crossUp.CrossDirection);
        var crossDown = Sound(state, PriceSeriesShape.BaselinePart, 1, prev: 105);
        Assert.True(crossDown.TriggerClick);
        Assert.Equal(-1, crossDown.CrossDirection);
    }

    [Fact]
    public void The_baseline_base_follows_the_view_when_no_price_is_chosen()
    {
        // Bar 1 closed at 101. With the view from bar 0 the base is bar 0's 102, so bar 1 is
        // below it; with the view from bar 1 the base is bar 1's own close, so it is not.
        var fromStart = Sound(State(PriceChartStyle.Baseline), PriceSeriesShape.BaselinePart, 1, relativeIndex: 1);
        var fromBar1 = Sound(State(PriceChartStyle.Baseline, viewportStart: 1), PriceSeriesShape.BaselinePart, 1, relativeIndex: 0);
        Assert.True(fromStart.TriangleMix > fromBar1.TriangleMix);
        Assert.True(fromBar1.SquareMix > fromStart.SquareMix);
    }

    [Fact]
    public void A_hollow_candle_sounds_its_colour_not_its_body_direction()
    {
        var hollow = State(PriceChartStyle.HollowCandles);
        var candles = State(PriceChartStyle.Candles);

        // Bar 3 closed under its open but over the close before: red as a candle, green hollow.
        Assert.True(Sound(hollow, "body", 3).SquareMix > Sound(candles, "body", 3).SquareMix);
        Assert.Equal(Sound(hollow, "body", 2).SquareMix, Sound(hollow, "body", 3).SquareMix);
        // And the pitch still says the body went down.
        Assert.Equal(Sound(candles, "body", 3).Frequency, Sound(hollow, "body", 3).Frequency);
    }

    [Fact]
    public void A_heavier_volume_candle_sounds_heavier()
    {
        var state = State(PriceChartStyle.VolumeCandles);

        var light = Sound(state, "body", 8);
        var heavy = Sound(state, "body", 9);
        Assert.True(heavy.SubSawMix > light.SubSawMix, $"{light.SubSawMix} vs {heavy.SubSawMix}");
    }

    // ---- Said --------------------------------------------------------------------------------

    private static string Speak(WorkspaceState state)
    {
        var spy = new SpySpeechRouter();
        new NavigationFeedbackManager(spy, new SpeechFormatter()) { IsSpeechEnabled = true }
            .HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");
        Assert.NotEmpty(spy.SpokenTexts);
        return string.Join(" ", spy.SpokenTexts);
    }

    [Fact]
    public void A_bar_says_its_four_prices_and_a_high_low_bar_its_range()
    {
        Assert.Contains("Bar. Up. Open 101.00, high 106.00, low 100.00, close 103.00.",
            Speak(State(PriceChartStyle.Bars, at: 2)));
        Assert.Contains("High-low bar. High 112.00, low 98.00, range 14.00.",
            Speak(State(PriceChartStyle.HighLow, at: 9)));
    }

    [Fact]
    public void The_baseline_says_which_side_of_the_base_the_close_is()
    {
        Assert.Contains("105.50, 1.44 percent above the base, 104.00",
            Speak(State(PriceChartStyle.Baseline, basePrice: 104, at: 6)));
        Assert.Contains("below the base, 104.00", Speak(State(PriceChartStyle.Baseline, basePrice: 104, at: 2)));
        Assert.Contains("at the base, 104.00", Speak(State(PriceChartStyle.Baseline, basePrice: 104, at: 3)));
    }

    [Fact]
    public void A_hollow_candle_says_its_colour_and_fill()
    {
        Assert.Contains("Filled green", Speak(State(PriceChartStyle.HollowCandles, at: 3)));
        Assert.Contains("Hollow green", Speak(State(PriceChartStyle.HollowCandles, at: 2)));
        Assert.Contains("Filled red", Speak(State(PriceChartStyle.HollowCandles, at: 1)));
        Assert.DoesNotContain("green", Speak(State(PriceChartStyle.Candles, at: 3)));
    }

    [Fact]
    public void A_volume_candle_says_when_its_volume_stands_out()
    {
        Assert.Contains("Heavy volume, 4.0 times the 9-bar average", Speak(State(PriceChartStyle.VolumeCandles, at: 9)));
        Assert.DoesNotContain("volume, ", Speak(State(PriceChartStyle.VolumeCandles, at: 8)));
        Assert.DoesNotContain("Heavy volume", Speak(State(PriceChartStyle.Candles, at: 9)));
    }

    [Fact]
    public void Volume_words_need_five_bars_before_and_mark_only_heavy_or_light()
    {
        Assert.Null(ChartTypeWords.Volume(Sample, 4));
        Assert.Equal("Heavy volume, 4.0 times the 9-bar average", ChartTypeWords.Volume(Sample, 9));
        var light = Sample.ToArray();
        light[9] = light[9] with { Volume = 4 };
        Assert.Equal("Light volume, 0.4 times the 9-bar average", ChartTypeWords.Volume(light, 9));
        light[9] = light[9] with { Volume = 12 };
        Assert.Null(ChartTypeWords.Volume(light, 9));
    }

    [Fact]
    public void On_the_series_each_type_reads_as_it_is_drawn()
    {
        // Page Up / Page Down onto the price series reads the bar as a whole, not one part.
        string Series(PriceChartStyle style, int at, double basePrice = 0) =>
            Speak(State(style, basePrice: basePrice, at: at) with { LastInteractionContext = InteractionContext.Series });

        Assert.Contains("Open 101.00, high 106.00, low 100.00, close 103.00", Series(PriceChartStyle.Bars, 2));
        Assert.Contains("High 112.00, low 98.00, range 14.00", Series(PriceChartStyle.HighLow, 9));
        Assert.Contains("1.44 percent above the base, 104.00", Series(PriceChartStyle.Baseline, 6, 104));
        Assert.Contains("Close 108.00. High 112.00. Low 98.00.", Series(PriceChartStyle.HlcArea, 9));
        Assert.Contains("Filled green. ", Series(PriceChartStyle.HollowCandles, 3));
        Assert.Contains("Heavy volume, 4.0 times the 9-bar average.", Series(PriceChartStyle.VolumeCandles, 9));
        Assert.DoesNotContain("Heavy volume", Series(PriceChartStyle.Candles, 9));
        Assert.DoesNotContain("green", Series(PriceChartStyle.Candles, 3));
    }

    [Theory]
    [InlineData(PriceChartStyle.Bars, "Bar chart.")]
    [InlineData(PriceChartStyle.HollowCandles, "Hollow candle chart. Colour against the previous close, hollow when the close is above the open.")]
    [InlineData(PriceChartStyle.HighLow, "High-low chart.")]
    [InlineData(PriceChartStyle.HlcArea, "HLC area chart.")]
    [InlineData(PriceChartStyle.VolumeCandles, "Volume candle chart. Wider candles for more volume.")]
    public void Each_type_has_its_change_sentence(PriceChartStyle style, string sentence)
    {
        Assert.Equal(sentence, ChartTypeWords.ChangeSentence(State(style)));
    }

    [Fact]
    public void The_baseline_change_sentence_names_its_base()
    {
        Assert.Equal("Baseline chart, base at the first close in view.", ChartTypeWords.ChangeSentence(State(PriceChartStyle.Baseline)));
        Assert.Equal("Baseline chart, base 104.00.", ChartTypeWords.ChangeSentence(State(PriceChartStyle.Baseline, basePrice: 104)));
    }

    private static (AccessibilityFeedbackCoordinator Coordinator, MockWorkspaceStore Store, CounterSpeechManager Speech) Coordinator()
    {
        var bus = new SpyEventBus();
        var speech = new CounterSpeechManager();
        var store = new MockWorkspaceStore();
        var formatter = new SpeechFormatter();
        var speechRouter = new SpeechFeedbackRouter(speech, formatter, store);
        var coordinator = new AccessibilityFeedbackCoordinator(
            store, new NavigationFeedbackManager(speechRouter, formatter), speechRouter,
            new AudioFeedbackRouter(new MockNavigationSonifier(), new MockEarconService()),
            formatter, bus, new MockEarconService(), new SdkCandlePatternAnalyzer(),
            new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
            new ChartPatternFocus(), new MockAutoNarrationService());
        return (coordinator, store, speech);
    }

    [Fact]
    public void Choosing_a_type_and_moving_the_base_are_each_said_once()
    {
        var (coordinator, store, speech) = Coordinator();
        using (coordinator)
        {
            var candles = State(PriceChartStyle.Candles);
            store.EmitState(candles);
            speech.Utterances.Clear();

            store.EmitState(State(PriceChartStyle.Bars));
            Assert.Equal("Bar chart.", speech.Utterances[^1].Text);

            store.EmitState(State(PriceChartStyle.Baseline));
            Assert.Equal("Baseline chart, base at the first close in view.", speech.Utterances[^1].Text);

            store.EmitState(State(PriceChartStyle.Baseline, basePrice: 104));
            Assert.Equal("Baseline chart, base 104.00.", speech.Utterances[^1].Text);

            int count = speech.Utterances.Count;
            store.EmitState(State(PriceChartStyle.Baseline, basePrice: 104) with { CurrentDataIndex = 3 });
            Assert.DoesNotContain(speech.Utterances.Skip(count), u => u.Text.StartsWith("Baseline chart", StringComparison.Ordinal));
        }
    }

    // ---- Drawn -------------------------------------------------------------------------------

    private const int W = 400, H = 300;

    private static ChartTheme Theme()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings).Current;
    }

    private static StylingService Styling() =>
        new(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());

    private static bool CloseTo(SKColor a, SKColor b, int tolerance = 10) =>
        Math.Abs(a.Red - b.Red) <= tolerance && Math.Abs(a.Green - b.Green) <= tolerance
        && Math.Abs(a.Blue - b.Blue) <= tolerance;

    private static int Count(SKBitmap bmp, SKRectI area, SKColor colour, int tolerance = 10)
    {
        int n = 0;
        for (int x = Math.Max(0, area.Left); x < Math.Min(bmp.Width, area.Right); x++)
            for (int y = Math.Max(0, area.Top); y < Math.Min(bmp.Height, area.Bottom); y++)
                if (CloseTo(bmp.GetPixel(x, y), colour, tolerance)) n++;
        return n;
    }

    private const double Min = 95, Max = 115;

    private static int Y(double price) => (int)Math.Round(ChartMath.MapY(price, 0, H, Min, Max, false));

    private static float Slot => W / (float)Sample.Length;

    private static int Mid(int i) => (int)Math.Floor(i * Slot + Slot / 2f);

    /// <summary>The whole main-pane drawing of <paramref name="state"/>, through the data layer.</summary>
    private static SKBitmap Draw(WorkspaceState state, ChartTheme theme)
    {
        var bmp = new SKBitmap(W, H);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        var r = SKRect.Create(0, 0, W, H);
        var ctx = new RenderContext(canvas, r, state.Data, state.ViewportStartIndex, state.Data.Count, Min, Max, false,
            r.Width / state.Data.Count, 1f, "Main", 0, theme) { ChartStyle = state.ChartStyle };
        new DataLayer(Styling()).Render(ctx, state.ActiveSeries);
        return bmp;
    }

    [Fact]
    public void A_bar_is_a_stroke_with_the_open_ticked_left_and_the_close_right()
    {
        var theme = Theme();
        using var bmp = Draw(State(PriceChartStyle.Bars), theme);
        var up = theme.CandleBullishBody;

        // Bar 2: open 101 left of the stroke, close 103 right of it; no body between them.
        int x = Mid(2);
        Assert.True(Count(bmp, new SKRectI(x - 12, Y(101) - 1, x - 2, Y(101) + 2), up) > 0, "no open tick on the left");
        Assert.True(Count(bmp, new SKRectI(x + 2, Y(103) - 1, x + 12, Y(103) + 2), up) > 0, "no close tick on the right");
        Assert.Equal(0, Count(bmp, new SKRectI(x + 3, Y(102) - 1, x + 12, Y(102) + 1), up));
        Assert.Equal(0, Count(bmp, new SKRectI(x - 12, Y(103) - 1, x - 3, Y(103) + 1), up));
    }

    [Fact]
    public void A_high_low_bar_is_a_solid_block_from_low_to_high()
    {
        var theme = Theme();
        using var bmp = Draw(State(PriceChartStyle.HighLow), theme);

        int x = Mid(9);   // 98 to 112
        Assert.True(Count(bmp, new SKRectI(x - 3, Y(111), x + 3, Y(99)), theme.CandleBullishBody) > 6 * (Y(99) - Y(111)) * 9 / 10);
        Assert.Equal(0, Count(bmp, new SKRectI(x - 3, Y(114), x + 3, Y(113)), theme.CandleBullishBody));
    }

    [Fact]
    public void A_hollow_candle_is_coloured_by_the_previous_close_and_hollow_when_it_rose()
    {
        var theme = Theme();
        using var bmp = Draw(State(PriceChartStyle.HollowCandles), theme);
        var up = theme.CandleBullishBody;

        // Bar 3 (open 106, close 104, previous close 103): filled, and GREEN.
        Assert.True(Count(bmp, new SKRectI(Mid(3) - 4, Y(105.6), Mid(3) + 5, Y(104.4)), up) > 20);
        Assert.Equal(0, Count(bmp, new SKRectI(Mid(3) - 4, Y(105.6), Mid(3) + 5, Y(104.4)), theme.CandleBearishBody));
        // Bar 2 (open 101, close 103): green and hollow — the middle of its body is empty.
        Assert.True(Count(bmp, new SKRectI(Mid(2) - 10, Y(103) - 1, Mid(2) + 11, Y(101) + 1), up) > 10);
        Assert.True(CloseTo(bmp.GetPixel(Mid(2) + 4, Y(102)), SKColors.Black, 30),
            $"the hollow candle's body is filled: {bmp.GetPixel(Mid(2) + 4, Y(102))}");
    }

    [Fact]
    public void A_heavier_volume_candle_is_wider()
    {
        var theme = Theme();
        using var bmp = Draw(State(PriceChartStyle.VolumeCandles), theme);

        int Width(int i, double price)
        {
            int y = Y(price), n = 0;
            for (int x = (int)(i * Slot); x < (int)((i + 1) * Slot); x++)
                if (CloseTo(bmp.GetPixel(x, y), theme.CandleBullishBody, 30)) n++;
            return n;
        }

        int heavy = Width(9, 107.5), light = Width(8, 106.25);
        Assert.True(heavy >= 3 * light && light > 0, $"heavy {heavy} px, light {light} px");
    }

    [Fact]
    public void The_baseline_is_green_above_the_base_and_red_below()
    {
        var theme = Theme();
        using var bmp = Draw(State(PriceChartStyle.Baseline, basePrice: 104), theme);

        // Between bars 7 (106) and 8 (106.5): above the base, green; between 1 (101) and 2 (103): red.
        int xa = (Mid(7) + Mid(8)) / 2, xb = (Mid(1) + Mid(2)) / 2;
        Assert.True(Count(bmp, new SKRectI(xa - 1, Y(106.25) - 3, xa + 2, Y(106.25) + 4), theme.CandleBullishBody) > 0);
        Assert.Equal(0, Count(bmp, new SKRectI(xa - 1, Y(106.25) - 3, xa + 2, Y(106.25) + 4), theme.CandleBearishBody));
        Assert.True(Count(bmp, new SKRectI(xb - 1, Y(102) - 3, xb + 2, Y(102) + 4), theme.CandleBearishBody) > 0);
        Assert.Equal(0, Count(bmp, new SKRectI(xb - 1, Y(102) - 3, xb + 2, Y(102) + 4), theme.CandleBullishBody));
        // The shade under the green line stops at the base.
        Assert.True(CloseTo(bmp.GetPixel(xa, Y(103)), SKColors.Black, 6) || !CloseTo(bmp.GetPixel(xa, Y(105)), SKColors.Black, 6));
        Assert.True(CloseTo(bmp.GetPixel(xa, Y(100)), SKColors.Black, 6), $"shade below the base: {bmp.GetPixel(xa, Y(100))}");
    }

    [Fact]
    public void The_HLC_area_shades_high_to_close_and_close_to_low()
    {
        var theme = Theme();
        using var bmp = Draw(State(PriceChartStyle.HlcArea), theme);

        // Between bars 5 and 6: high ≈ 106, close ≈ 105.25, low ≈ 104.
        int x = (Mid(5) + Mid(6)) / 2;
        var upper = bmp.GetPixel(x, Y(105.7));
        var lower = bmp.GetPixel(x, Y(104.5));
        Assert.False(CloseTo(upper, SKColors.Black, 6), $"nothing shaded between the high and the close: {upper}");
        Assert.False(CloseTo(lower, SKColors.Black, 6), $"nothing shaded between the close and the low: {lower}");
        Assert.True(upper.Green > upper.Red, $"the upper band is not the up colour: {upper}");
        Assert.True(lower.Red > lower.Green, $"the lower band is not the down colour: {lower}");
        Assert.True(CloseTo(bmp.GetPixel(x, Y(108)), SKColors.Black, 6));
        Assert.True(CloseTo(bmp.GetPixel(x, Y(101)), SKColors.Black, 6));
    }

    // ---- The status line ---------------------------------------------------------------------

    private static StatusLine Symbol(WorkspaceState state, int bar, ChartTheme theme)
    {
        var ctx = new ChartStatusLine.Context(theme, ChartStatusLine.DefaultBackingAlpha, state.Data, Id,
            ChartStyle: state.ChartStyle,
            BaselineBase: ChartStatusLine.BaselineBaseOf(state.ActiveSeries, state.ChartStyle, state.ViewportStartIndex));
        return ChartStatusLine.Build("Main", Array.Empty<ChartSeries>(), bar, ctx, 400f, 4000f,
            ChartStatusLine.LinePx, t => t.Length * 7f).Lines[0];
    }

    [Fact]
    public void The_symbol_line_shows_what_each_type_draws()
    {
        var theme = Theme();

        Assert.Equal(new[] { "H", "L", "Range" }, Symbol(State(PriceChartStyle.HighLow), 9, theme).Values.Select(v => v.Label));
        Assert.Equal("14.00", Symbol(State(PriceChartStyle.HighLow), 9, theme).Values[2].Text);
        Assert.Equal(new[] { "H", "L", "C" }, Symbol(State(PriceChartStyle.HlcArea), 9, theme).Values.Select(v => v.Label));
        Assert.Equal(new[] { "O", "H", "L", "C", "Vol" }, Symbol(State(PriceChartStyle.VolumeCandles), 9, theme).Values.Select(v => v.Label));
        Assert.Equal(new[] { "O", "H", "L", "C" }, Symbol(State(PriceChartStyle.Bars), 9, theme).Values.Select(v => v.Label));

        var baseline = Symbol(State(PriceChartStyle.Baseline, basePrice: 104), 2, theme).Values;
        Assert.Equal(new[] { "C", "Base" }, baseline.Select(v => v.Label));
        Assert.Equal("104.00", baseline[1].Text);
    }

    [Fact]
    public void The_symbol_line_colours_match_the_drawing()
    {
        var theme = Theme();
        SKColor Ink(WorkspaceState state, int bar) => Symbol(state, bar, theme).Values[^1].Color;

        // Hollow bar 3 is green (over the close before) though it closed under its open.
        var hollow = State(PriceChartStyle.HollowCandles);
        var candles = State(PriceChartStyle.Candles);
        Assert.NotEqual(Ink(candles, 3), Ink(hollow, 3));
        Assert.Equal(Ink(candles, 2), Ink(hollow, 3));

        // The baseline's close is in its side's colour.
        var baseline = State(PriceChartStyle.Baseline, basePrice: 104);
        Assert.Equal(Ink(candles, 2), Symbol(baseline, 6, theme).Values[0].Color);
        Assert.Equal(Ink(candles, 3), Symbol(baseline, 2, theme).Values[0].Color);
    }

    // ---- Chosen: commands, the Go bar ---------------------------------------------------------

    private static (CommandDispatcher Dispatcher, MockWorkspaceStore Store, SpyEventBus Bus) Dispatcher(WorkspaceState state)
    {
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        store.EmitState(state);
        var dispatcher = new CommandDispatcher(
            bus, Substitute.For<INavigationEngine>(), store,
            Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));
        bus.Publish(new ChartFocusEvent());
        return (dispatcher, store, bus);
    }

    [Theory]
    [InlineData(SystemCommand.ShowBarsChart, PriceChartStyle.Bars, "Already a bar chart.")]
    [InlineData(SystemCommand.ShowHollowCandleChart, PriceChartStyle.HollowCandles, "Already a hollow candle chart.")]
    [InlineData(SystemCommand.ShowHighLowChart, PriceChartStyle.HighLow, "Already a high-low chart.")]
    [InlineData(SystemCommand.ShowHlcAreaChart, PriceChartStyle.HlcArea, "Already an HLC area chart.")]
    [InlineData(SystemCommand.ShowVolumeCandleChart, PriceChartStyle.VolumeCandles, "Already a volume candle chart.")]
    [InlineData(SystemCommand.ShowBaselineChart, PriceChartStyle.Baseline, "Already a baseline chart, base at the first close in view.")]
    public void Each_command_shows_its_type_and_says_already_when_it_is(SystemCommand command, PriceChartStyle style, string already)
    {
        var (dispatcher, store, _) = Dispatcher(State(PriceChartStyle.Candles));
        dispatcher.Dispatch(command);
        Assert.Equal(style, Assert.Single(store.DispatchedActions.OfType<SetChartStyleAction>()).Style);

        var (again, sameStore, bus) = Dispatcher(State(style));
        again.Dispatch(command);
        Assert.Empty(sameStore.DispatchedActions);
        Assert.Contains(bus.Log.OfType<FeedbackRequestEvent>(), e => e.Message == already);
    }

    [Fact]
    public void A_base_price_is_stored_before_the_type_and_a_new_base_on_a_baseline_chart_only_moves_the_base()
    {
        var (dispatcher, store, _) = Dispatcher(State(PriceChartStyle.Candles));
        dispatcher.ShowBaseline(95000);
        Assert.Equal(95000, Assert.IsType<SetRenkoSettingsAction>(store.DispatchedActions[0]).Settings.BaselinePrice);
        Assert.Equal(PriceChartStyle.Baseline, Assert.IsType<SetChartStyleAction>(store.DispatchedActions[1]).Style);

        var (onBaseline, baselineStore, _) = Dispatcher(State(PriceChartStyle.Baseline, basePrice: 104));
        onBaseline.ShowBaseline(105);
        Assert.Equal(105, Assert.Single(baselineStore.DispatchedActions.OfType<SetRenkoSettingsAction>()).Settings.BaselinePrice);
        Assert.Empty(baselineStore.DispatchedActions.OfType<SetChartStyleAction>());

        var (same, sameStore, bus) = Dispatcher(State(PriceChartStyle.Baseline, basePrice: 104));
        same.ShowBaseline(104);
        Assert.Empty(sameStore.DispatchedActions);
        Assert.Contains(bus.Log.OfType<FeedbackRequestEvent>(), e => e.Message == "Already a baseline chart, base 104.00.");
    }

    [Theory]
    [InlineData("BARS", SystemCommand.ShowBarsChart)]
    [InlineData("ohlc bars", SystemCommand.ShowBarsChart)]
    [InlineData("HOLLOW", SystemCommand.ShowHollowCandleChart)]
    [InlineData("hollow candles", SystemCommand.ShowHollowCandleChart)]
    [InlineData("BASELINE", SystemCommand.ShowBaselineChart)]
    [InlineData("HILO", SystemCommand.ShowHighLowChart)]
    [InlineData("high low chart", SystemCommand.ShowHighLowChart)]
    [InlineData("HLC", SystemCommand.ShowHlcAreaChart)]
    [InlineData("VCANDLE", SystemCommand.ShowVolumeCandleChart)]
    [InlineData("volume candles", SystemCommand.ShowVolumeCandleChart)]
    public void Each_type_is_reachable_by_its_own_words(string typed, SystemCommand command)
    {
        Assert.Equal(command, GoBarMatcher.Match(typed, GoBarRegistry.All).FirstOrDefault()?.Entry.Command);
    }

    [Fact]
    public void BASELINE_with_a_price_makes_a_baseline_action_and_a_bad_price_is_refused()
    {
        var bus = new EventBus();
        using var stack = new ModalStack(bus);
        var dispatcher = Substitute.For<ICommandDispatcher>();
        var service = new GoBarService(dispatcher, Substitute.For<IShortcutManager>(), bus, stack);

        var option = service.Suggest("BASELINE 95000").Options[0];
        Assert.Equal(95000, Assert.IsType<GoBarBaselineAction>(option.Action).BasePrice);
        Assert.Equal("Baseline chart, base 95000.00", option.Name);
        Assert.Equal("BASELINE 95000", option.CanonicalText);

        Assert.Equal("A base must be a price above zero, like BASELINE 95000.", GoBarParser.Parse("BASELINE banana").Refusal);
        Assert.Equal("A base must be a price above zero, like BASELINE 95000.", GoBarParser.Parse("BASELINE -5").Refusal);
    }
}
