using System.Collections.Immutable;
using System.Text.RegularExpressions;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Xunit.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// The spoken contract for arrowing along a DRAWING, as wired in on 2026-09-03:
/// <c>{value}[, {position}][, {relation}].</c>, the series-switch prefix that names the
/// drawing the way the nudge does, and the component name spoken where it changes.
///
/// <para>
/// Every fixture is built the way <c>DrawingInteractionManager</c> builds one — the real
/// calculators, the real <c>IndicatorModelFactory</c>, <c>ExtendRight = true</c> — because a
/// fixture that is not is testing a drawing no user can make.
/// </para>
///
/// <para>
/// The test that matters most is <see cref="Span_Comes_From_Anchor_Dates_Never_From_Array_Length"/>:
/// it truncates the component array so the array's end and the anchors' span disagree, and
/// asserts the sentence follows the anchors. That is the one difference between this contract
/// and the obvious implementation, which would stand a trader at the live edge and announce
/// "past end" about a line running through the bar they are on.
/// </para>
/// </summary>
public sealed class DrawingSpeechContractTests
{
    private readonly ITestOutputHelper _out;
    public DrawingSpeechContractTests(ITestOutputHelper output) => _out = output;

    // ── fixtures ─────────────────────────────────────────────────────────────

    /// <summary>100 hourly bars, close walking 100.5 → 199.5.</summary>
    private static List<Ohlcv> Bars(int n = 100, double scale = 1.0)
    {
        var list = new List<Ohlcv>();
        var t = new DateTime(2026, 6, 1, 9, 30, 0, DateTimeKind.Utc);
        for (int i = 0; i < n; i++)
            list.Add(new Ohlcv(t.AddHours(i), (100 + i) * scale, (101 + i) * scale, (99 + i) * scale, (100.5 + i) * scale, 1000));
        return list;
    }

    private static IDrawingService RealDrawingService()
    {
        var calcType = typeof(IDrawingCalculator);
        var calcs = typeof(DrawingService).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && calcType.IsAssignableFrom(t))
            .Select(t => (IDrawingCalculator)Activator.CreateInstance(t)!)
            .ToList();
        return new DrawingService(calcs);
    }

    private sealed class CapturingRouter : ISpeechFeedbackRouter
    {
        public List<string> Said = new();
        public void Speak(string message, bool interrupt = true, SpeechChannel channel = SpeechChannel.Manual) => Said.Add(message);
        public void SpeakPoint(WorkspaceState s, WorkspaceState? p, ChartSeries se, Ohlcv pt, string prefix = "") { }
        public void SpeakProfile(WorkspaceState s, WorkspaceState? p, ChartSeries se, int b, string prefix = "") { }
        public void SpeakHeatmap(WorkspaceState s, WorkspaceState? p, ChartSeries se, int d, int b, string prefix = "") { }
    }

    private static ChartSeries BuildLikeProduction(DrawingType type, string friendly, DrawingData d, List<Ohlcv> bars, int ordinal = 1)
    {
        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var factory = new IndicatorModelFactory(styling, new MockIndicatorPreferencesService());
        var cfg = new SeriesConfig { Id = $"draw-{ordinal}", Name = $"{friendly} ({ordinal})", FriendlyName = $"{type} Drawing", Pane = "Main" };
        var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
        foreach (var kv in RealDrawingService().CalculateDrawingData(d, bars))
        {
            var comp = factory.CreateComponentConfig(type.ToString(), kv.Key);
            cfg.Components.Add(comp);
            buf.ComponentData[comp.Name] = kv.Value;
        }
        return new ChartSeries(cfg, buf) { Drawing = d };
    }

    private static WorkspaceState StateAt(List<Ohlcv> bars, ChartSeries series, int index, int component = 0, string order = "HeaderValue") =>
        WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = index,
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
            FocusedComponentIndex = component,
            PrimarySeriesId = "candles",
            ReadColumnHeaders = true,
            SpeechOrder = order,
            SpeakTimestamps = false,
            LastInteractionContext = InteractionContext.Component,
            ViewportStartIndex = 0,
            ViewportLength = bars.Count,
        };

    private static DrawingData TrendLine(List<Ohlcv> bars, int a = 30, int b = 70, double? p1 = null, double? p2 = null) => new()
    {
        Type = DrawingType.TrendLine,
        AnchorDate1 = bars[a].Date, AnchorPrice1 = p1 ?? bars[a].Close,
        AnchorDate2 = bars[b].Date, AnchorPrice2 = p2 ?? bars[b].Close,
        ExtendRight = true,        // as DrawingInteractionManager places one
    };

    /// <summary>Arrow onto <paramref name="index"/> with the series already focused: the
    /// per-bar sentence alone, no switch prefix.</summary>
    private static string Arrow(List<Ohlcv> bars, ChartSeries series, int index, int component = 0, string order = "HeaderValue")
    {
        var router = new CapturingRouter();
        var nav = new NavigationFeedbackManager(router, new SpeechFormatter());
        nav.HandleNavigationFeedback(StateAt(bars, series, index, component, order), isXMove: true, isYMove: false, prefixMessage: "NAV_SERIES_NEXT");
        nav.HandleNavigationFeedback(StateAt(bars, series, index, component, order), isXMove: true, isYMove: false, prefixMessage: "NAV_MOVE");
        return router.Said[1];
    }

    // ── §1 the per-bar sentence ───────────────────────────────────────────────

    [Fact]
    public void Between_The_Anchors_Is_Value_And_Relation_Only()
    {
        var bars = Bars();
        var series = BuildLikeProduction(DrawingType.TrendLine, "Trend line", TrendLine(bars), bars);
        string said = Arrow(bars, series, 50);
        _out.WriteLine(said);
        // The line runs through the closes, so price sits ON it: "at", to the spoken precision.
        Assert.Equal("150.50, price on it.", said);
        Assert.DoesNotContain("line", said, StringComparison.OrdinalIgnoreCase);   // no name, no type word
    }

    [Fact]
    public void On_An_Anchor_Bar_The_Slot_Name_Is_Spoken_With_The_Word_Anchor()
    {
        var bars = Bars();
        // Anchors above the closes so the relation is unambiguous.
        var series = BuildLikeProduction(DrawingType.TrendLine, "Trend line", TrendLine(bars, p1: 140, p2: 180), bars);
        string start = Arrow(bars, series, 30);
        string end = Arrow(bars, series, 70);
        _out.WriteLine(start); _out.WriteLine(end);
        Assert.Equal("140.00, at start anchor, price below.", start);
        Assert.Equal("180.00, at end anchor, price below.", end);
    }

    [Fact]
    public void Past_The_End_With_A_Value_Says_Past_End_And_Keeps_The_Value_First()
    {
        var bars = Bars();
        var series = BuildLikeProduction(DrawingType.TrendLine, "Trend line", TrendLine(bars, p1: 140, p2: 180), bars);
        string said = Arrow(bars, series, 99);   // ExtendRight: the line has a value here
        _out.WriteLine(said);
        Assert.StartsWith("209.00, past end, price ", said);
    }

    [Fact]
    public void Before_The_Start_With_No_Value_Is_The_Position_Word_And_A_Bar_Count()
    {
        var bars = Bars();
        var series = BuildLikeProduction(DrawingType.TrendLine, "Trend line", TrendLine(bars), bars);
        Assert.Equal("Before start, 20 bars.", Arrow(bars, series, 10));
        Assert.Equal("Before start, 1 bar.", Arrow(bars, series, 29));   // singular
    }

    [Fact]
    public void A_Rectangle_Past_Its_Span_Counts_Bars_Back_To_The_Drawn_Edge()
    {
        var bars = Bars();
        var d = new DrawingData
        {
            Type = DrawingType.Rectangle,
            AnchorDate1 = bars[30].Date, AnchorPrice1 = bars[70].Close,
            AnchorDate2 = bars[70].Date, AnchorPrice2 = bars[30].Close,
        };
        var series = BuildLikeProduction(DrawingType.Rectangle, "Rectangle", d, bars);
        Assert.Equal("Past end, 10 bars.", Arrow(bars, series, 80));
    }

    [Fact]
    public void A_Cross_Replaces_The_Plain_Side_And_Never_Adds_A_Clause()
    {
        var bars = Bars();
        // A flat line at 150.0: the closes walk 100.5 → 199.5, so bar 49 closes 149.5 (below)
        // and bar 50 closes 150.5 (above). Bar 50 is the cross.
        var series = BuildLikeProduction(DrawingType.TrendLine, "Trend line", TrendLine(bars, p1: 150, p2: 150), bars);
        string before = Arrow(bars, series, 49);
        string cross = Arrow(bars, series, 50);
        string after = Arrow(bars, series, 51);
        _out.WriteLine(before); _out.WriteLine(cross); _out.WriteLine(after);
        Assert.Equal("150.00, price below.", before);
        Assert.Equal("150.00, price crossed above.", cross);
        Assert.Equal("150.00, price above.", after);
    }

    [Fact]
    public void A_Sub_Cent_Drawing_Speaks_With_Price_Precision_Not_F2()
    {
        // KAS-scale bars: closes around 0.036. The fallback strategy said "0.04" here because a
        // drawing's series id is neither "price" nor "candles".
        var bars = Bars(scale: 0.00025);
        var series = BuildLikeProduction(DrawingType.TrendLine, "Trend line", TrendLine(bars), bars);
        string said = Arrow(bars, series, 50);
        _out.WriteLine(said);
        Assert.DoesNotContain("0.04", said);
        Assert.StartsWith(SpeechPriceFormatter.FormatPrice(bars[50].Close), said);
    }

    [Fact]
    public void ValueOnly_Returns_The_Number_Alone()
    {
        var bars = Bars();
        var series = BuildLikeProduction(DrawingType.TrendLine, "Trend line", TrendLine(bars, p1: 140, p2: 180), bars);
        Assert.Equal("140.00", Arrow(bars, series, 30, order: "ValueOnly"));
    }

    [Fact]
    public void A_Hidden_Level_Is_Named_Once_By_Its_Own_Sentence()
    {
        // HiddenComponentStrategy runs first and names the level, with the dispatcher's state
        // word in front of it ("Hidden. 61.8% Level"); the drawing prefixes must not name the
        // same object a second way in the same breath. Case-insensitive since 2026-09-04: the
        // qualifier moved to the front of the sentence, so it is capitalised now — what is
        // pinned is that the hidden state is CONVEYED, not where the word sits.
        var bars = Bars();
        var d = new DrawingData { Type = DrawingType.FibRetracement, AnchorPrice1 = 100, AnchorPrice2 = 200 };
        var series = BuildLikeProduction(DrawingType.FibRetracement, "Fibonacci retracement", d, bars);
        series.Components[1].IsVisible = false;
        var router = new CapturingRouter();
        var nav = new NavigationFeedbackManager(router, new SpeechFormatter());
        nav.HandleNavigationFeedback(StateAt(bars, series, 50, component: 1), true, false, "NAV_SERIES_NEXT");
        nav.HandleNavigationFeedback(StateAt(bars, series, 50, component: 0), false, true, "NAV_MOVE");
        nav.HandleNavigationFeedback(StateAt(bars, series, 50, component: 1), false, true, "NAV_MOVE");
        foreach (var s in router.Said) _out.WriteLine(s);
        Assert.StartsWith($"Fibonacci retracement 1. {series.Components.Count} components. ", router.Said[0]);
        Assert.Contains("hidden", router.Said[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, Regex.Matches(router.Said[2], "23.6").Count);   // once, in its own sentence
    }

    /// <summary>
    /// THE design error a naive implementation makes. The array is cut to 60 entries while the
    /// anchors still say 30 → 70 with the line projected right. Bar 65 is inside the anchors'
    /// span and off the end of the array; bar 80 is past the end anchor and off the array too.
    /// If the span were the array's length both would say "past end" — and bar 65 would be a
    /// confident false statement about a line that runs through it.
    /// </summary>
    [Fact]
    public void Span_Comes_From_Anchor_Dates_Never_From_Array_Length()
    {
        var bars = Bars();
        var series = BuildLikeProduction(DrawingType.TrendLine, "Trend line", TrendLine(bars), bars);
        foreach (var key in series.Data.ComponentData.Keys.ToList())
            series.Data.ComponentData[key] = series.Data.ComponentData[key].Take(60).ToArray();

        string inside = Arrow(bars, series, 65);
        string past = Arrow(bars, series, 80);
        _out.WriteLine(inside); _out.WriteLine(past);
        Assert.Equal("Not yet calculated.", inside);
        Assert.Equal("Past end, 10 bars.", past);
    }

    // ── §3/§4 naming: the switch prefix and the component change ─────────────

    [Fact]
    public void Switching_Onto_A_One_Component_Drawing_Names_It_Like_The_Nudge_And_Drops_The_Count()
    {
        var bars = Bars();
        var series = BuildLikeProduction(DrawingType.TrendLine, "Trend line", TrendLine(bars), bars, ordinal: 2);
        var router = new CapturingRouter();
        var nav = new NavigationFeedbackManager(router, new SpeechFormatter());
        nav.HandleNavigationFeedback(StateAt(bars, series, 50), true, false, "NAV_SERIES_NEXT");
        _out.WriteLine(router.Said[0]);
        Assert.StartsWith("Trend line 2. ", router.Said[0]);
        Assert.DoesNotContain("(2)", router.Said[0]);
        Assert.DoesNotContain("component", router.Said[0]);
        // One drawing, one spoken name: the switch and the nudge readback agree.
        Assert.Equal("Trend line 2", DrawingInteractionManager.SpokenName(series));
    }

    [Fact]
    public void Switching_Onto_A_Fib_Names_The_Level_About_To_Be_Read()
    {
        var bars = Bars();
        var d = new DrawingData { Type = DrawingType.FibRetracement, AnchorPrice1 = 100, AnchorPrice2 = 200 };
        var series = BuildLikeProduction(DrawingType.FibRetracement, "Fibonacci retracement", d, bars);
        _out.WriteLine(string.Join(" | ", series.Components.Select(c => c.DisplayName)));
        var router = new CapturingRouter();
        var nav = new NavigationFeedbackManager(router, new SpeechFormatter());
        nav.HandleNavigationFeedback(StateAt(bars, series, 50), true, false, "NAV_SERIES_NEXT");
        _out.WriteLine(router.Said[0]);
        Assert.StartsWith($"Fibonacci retracement 1. {series.Components.Count} components, reading 0%. ", router.Said[0]);
        Assert.DoesNotContain("0.0%", router.Said[0]);
        Assert.DoesNotContain("Level", router.Said[0]);
    }

    [Fact]
    public void Ctrl_Down_Between_Fib_Levels_Speaks_The_Level_Once_Then_Bars_Speak_Values_Only()
    {
        var bars = Bars();
        var d = new DrawingData { Type = DrawingType.FibRetracement, AnchorPrice1 = 100, AnchorPrice2 = 200 };
        var series = BuildLikeProduction(DrawingType.FibRetracement, "Fibonacci retracement", d, bars);
        var router = new CapturingRouter();
        var nav = new NavigationFeedbackManager(router, new SpeechFormatter());
        nav.HandleNavigationFeedback(StateAt(bars, series, 50, component: 0), true, false, "NAV_SERIES_NEXT");
        nav.HandleNavigationFeedback(StateAt(bars, series, 50, component: 1), false, true, "NAV_MOVE");
        nav.HandleNavigationFeedback(StateAt(bars, series, 51, component: 1), true, false, "NAV_MOVE");
        foreach (var s in router.Said) _out.WriteLine(s);
        string expectedLevel = DrawingSpeech.SpokenComponentName(series.Components, series.Components[1]);
        Assert.StartsWith(expectedLevel + ". ", router.Said[1]);
        Assert.DoesNotContain(expectedLevel, router.Said[2]);   // constant across the sweep: not repeated
        Assert.DoesNotContain("Level", router.Said[2]);
    }

    [Fact]
    public void A_Text_Label_Still_Reads_Its_Wording_Not_A_Price()
    {
        // The label strategy is first in the list for a reason; the drawing strategy must not
        // reach it. A label's component array holds the anchor CLOSE, which is the one thing
        // about a label that carries no information.
        var bars = Bars();
        var d = new DrawingData { Type = DrawingType.TextLabel, AnchorDate1 = bars[40].Date, AnchorPrice1 = bars[40].Close, Text = "sold half here" };
        var series = BuildLikeProduction(DrawingType.TextLabel, "Text label", d, bars);
        string said = Arrow(bars, series, 40);
        _out.WriteLine(said);
        Assert.Contains("sold half here", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("140.50", said);
    }
}
