using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Charts;
using AccessibleTrader.Core.Services.GoBar;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Alerts;
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
/// Point and figure, the fifth chart type built off the clock: columns of X going up and O going
/// down, one mark per box, a column turning only when a close comes back the reversal count of
/// boxes. The builder, and the chart drawing, saying, sounding and alerting each column's boxes
/// alike.
/// </summary>
public sealed class PointFigureChartTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ChartIdentity Id = new("Spot", "Bitstamp", "BTC/USD", "1h");
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private static Ohlcv Bar(int i, double close, double volume = 10)
        => new(Start.AddHours(i), close, close + 1, close - 1, close, volume);

    private static List<Ohlcv> Closes(params double[] closes)
        => closes.Select((c, i) => Bar(i, c, 10 + i)).ToList();

    /// <summary>Box 1, reversal 3: X 100→103 (101.2 draws it, 103.7 grows it), 102 and 100.9 are
    /// under three boxes back, 99.9 turns it (O 103→100), 104 turns it back (X 100→104), 105
    /// grows it.</summary>
    private static readonly double[] Swing = { 100, 100.5, 101.2, 103.7, 102, 100.9, 99.9, 104, 105 };

    private static BrickBuildResult Build(IReadOnlyList<Ohlcv> bars, double box = 1, int reversal = 3,
        int max = BrickChartBuilder.MaxBricks)
        => PointFigureBuilder.Build(bars, box, reversal, Hour, max);

    private static (double Open, double Close)[] Edges(BrickBuildResult built)
        => built.Bricks.Select(b => (b.Open, b.Close)).ToArray();

    // ---- The builder ------------------------------------------------------------------------

    [Fact]
    public void Columns_grow_box_by_box_and_turn_only_on_the_reversal_count()
    {
        var built = Build(Closes(Swing));

        Assert.Null(built.Refusal);
        // Open is the level a column moved FROM; its boxes are the whole boxes to its Close.
        Assert.Equal(new[] { (100d, 103d), (103d, 100d), (100d, 105d) }, Edges(built));
        Assert.Equal(new[] { 3, 3, 5 }, built.Bricks.Select(c => PointFigureColumns.Boxes(c, 1)));
        Assert.Equal(new[] { true, false, true }, built.Spans.Select(s => s.Up));
    }

    [Fact]
    public void A_column_is_dated_by_the_close_that_started_it_and_spans_the_bars_since_the_column_before()
    {
        var built = Build(Closes(Swing));

        Assert.Equal(new[] { Start.AddHours(2), Start.AddHours(6), Start.AddHours(7) }, built.Bricks.Select(c => c.Date));
        Assert.Equal(new[] { (0, 3), (4, 6), (7, 8) }, built.Spans.Select(s => (s.FirstBar, s.LastBar)));
        Assert.Equal(Start.AddHours(9), built.Spans[2].End);
        // Volume is the window's: bars 4, 5 and 6 are 14 + 15 + 16.
        Assert.Equal(45, built.Bricks[1].Volume);
        // High and low are the column's two edges.
        Assert.Equal((103d, 100d), (built.Bricks[1].High, built.Bricks[1].Low));
    }

    [Fact]
    public void The_reversal_count_decides_when_a_column_turns()
    {
        // Reversal 1: 102 is one box back from 103 and turns the X column at once.
        // (It then falls on to 100 with 100.9 and 99.9.)
        var one = Build(Closes(Swing), reversal: 1);
        Assert.Equal((103d, 100d), Edges(one)[1]);
        Assert.Equal(Start.AddHours(4), one.Bricks[1].Date);
        Assert.Equal(Start.AddHours(6), Build(Closes(Swing)).Bricks[1].Date);

        // Reversal 4: 99.9 is only three boxes back from 103, so the first column never turns.
        var four = Build(Closes(Swing), reversal: 4);
        Assert.Equal(new[] { (100d, 105d) }, Edges(four));
    }

    [Fact]
    public void A_growing_column_keeps_its_date_and_open_and_the_count_moves_only_on_a_turn()
    {
        var before = Build(Closes(Swing[..^1]));     // the newest column at 104
        var after = Build(Closes(Swing));            // ... and at 105

        Assert.Equal(before.Bricks.Count, after.Bricks.Count);
        Assert.Equal((before.Bricks[^1].Date, before.Bricks[^1].Open), (after.Bricks[^1].Date, after.Bricks[^1].Open));
        Assert.Equal((104d, 105d), (before.Bricks[^1].Close, after.Bricks[^1].Close));
    }

    [Fact]
    public void The_first_column_may_go_down_and_a_one_box_column_keeps_its_direction()
    {
        var built = Build(Closes(100, 99.5, 98.6));

        var column = Assert.Single(built.Bricks);
        Assert.Equal((100d, 99d), (column.Open, column.Close));
        Assert.False(built.Spans[0].Up);
        Assert.Equal("O, 1 box", PointFigureColumns.Words(column, 1));
    }

    [Fact]
    public void A_close_on_a_box_level_reaches_it_despite_floating_point()
    {
        // 300 / 0.1 is 2999.9999999999995 in floating point; 300 is still on the 3000th level.
        var built = Build(Closes(299.9, 300), box: 0.1);

        var column = Assert.Single(built.Bricks);
        Assert.Equal(1, PointFigureColumns.Boxes(column, 0.1));
    }

    [Fact]
    public void A_zero_box_a_reversal_outside_1_to_20_and_too_many_columns_are_refused()
    {
        Assert.Equal("Point and figure needs a box size above zero.", Build(Closes(Swing), box: 0).Refusal);
        Assert.Equal("Point and figure takes a reversal of 1 to 20 boxes.", Build(Closes(Swing), reversal: 0).Refusal);
        Assert.Equal("Point and figure takes a reversal of 1 to 20 boxes.", Build(Closes(Swing), reversal: 21).Refusal);
        Assert.Equal("Point and figure would draw over 2 columns.", Build(Closes(Swing), max: 2).Refusal);
        Assert.Null(Build(Closes(Swing), max: 3).Refusal);
    }

    // ---- One settings record, five types ----------------------------------------------------

    private static readonly RenkoSettings Mixed = new(RenkoBoxMode.Fixed, 500, 21)
    {
        RangeMode = RenkoBoxMode.Fixed, RangeSize = 20, RangeAtrLength = 9, LineBreakLines = 3,
        KagiMode = RenkoBoxMode.Fixed, KagiSize = 10, KagiAtrLength = 7,
        PnfMode = RenkoBoxMode.Fixed, PnfSize = 1, PnfAtrLength = 5, PnfReversal = 3,
    };

    [Fact]
    public void Point_and_figure_is_built_off_the_clock_with_its_own_fields()
    {
        Assert.True(PriceChartStyle.PointAndFigure.IsBrickStyle());
        Assert.True(PriceChartStyle.PointAndFigure.IsBlockStyle());
        Assert.Equal("Point and figure", BuiltCharts.Name(PriceChartStyle.PointAndFigure));
        Assert.Equal("column", BuiltCharts.Noun(PriceChartStyle.PointAndFigure));
        Assert.Equal("columns", BuiltCharts.Noun(PriceChartStyle.PointAndFigure, 2));

        var pnf = BuiltCharts.Merge(PriceChartStyle.PointAndFigure, Mixed,
            new RenkoSettings(PnfMode: RenkoBoxMode.Atr, PnfAtrLength: 30, PnfReversal: 2));
        Assert.Equal(Mixed with { PnfMode = RenkoBoxMode.Atr, PnfSize = 0, PnfAtrLength = 30, PnfReversal = 2 }, pnf);

        Assert.True(BuiltCharts.SameFor(PriceChartStyle.PointAndFigure, Mixed, Mixed with { KagiSize = 1 }));
        Assert.False(BuiltCharts.SameFor(PriceChartStyle.PointAndFigure, Mixed, Mixed with { PnfSize = 2 }));
        Assert.False(BuiltCharts.SameFor(PriceChartStyle.PointAndFigure, Mixed, Mixed with { PnfReversal = 2 }));

        Assert.True(BuiltCharts.TryResolveBox(PriceChartStyle.PointAndFigure, Closes(Swing), Mixed, out double box, out _));
        Assert.Equal(1, box);
        Assert.False(BuiltCharts.TryResolveBox(PriceChartStyle.PointAndFigure, Closes(Swing),
            Mixed with { PnfReversal = 0 }, out _, out string? refusal));
        Assert.Equal(PointFigureBuilder.ReversalRefusal, refusal);
        Assert.False(BuiltCharts.TryResolveBox(PriceChartStyle.PointAndFigure, Closes(Swing),
            Mixed with { PnfSize = 0 }, out _, out refusal));
        Assert.Equal("A point and figure box must be a positive price.", refusal);
    }

    [Fact]
    public void Too_big_a_box_says_no_columns()
    {
        var state = WorkspaceState.Initial with
        {
            Identity = Id, Data = new TimeSeriesBuffer<Ohlcv>(Closes(Swing)), CurrentDataIndex = 8,
            InitStatus = InitializationStatus.Ready,
        };

        Assert.False(BrickChartSpeech.TryPlan(state, Mixed with { PnfSize = 50 }, out _, out string? refusal,
            PriceChartStyle.PointAndFigure));
        Assert.Equal("No point and figure columns: price has not moved one box of 50.00 in the loaded bars.", refusal);
    }

    private static WorkspaceState Reduce(WorkspaceState initial, params WorkspaceAction[] actions)
    {
        using var store = new WorkspaceStore(
            new SpyEventBus(), new MockViewportRangeCalculator(),
            new MockViewportNavigationService(), new MockVolumeStateService());
        store.Dispatch(new UpdateSettingsAction(_ => initial));
        foreach (var action in actions) store.Dispatch(action);
        return store.State;
    }

    [Fact]
    public void Choosing_point_and_figure_freezes_its_own_box_and_draws_the_columns()
    {
        var initial = WorkspaceState.Initial with
        {
            Identity = Id,
            Data = new TimeSeriesBuffer<Ohlcv>(Closes(Swing)),
            Renko = Mixed,
            CurrentDataIndex = 8,
            ViewportLength = 30,
            InitStatus = InitializationStatus.Ready,
        };

        var pnf = Reduce(initial,
            new SetChartStyleAction(PriceChartStyle.Renko, false),
            new SetChartStyleAction(PriceChartStyle.PointAndFigure, false));

        Assert.Equal(PriceChartStyle.PointAndFigure, pnf.ChartStyle);
        Assert.Equal(1, pnf.BrickBoxSize);   // its own box, not Renko's 500
        Assert.Equal(3, pnf.Data.Count);
        Assert.Equal(9, pnf.TimeBars!.Count);
    }

    // ---- The price series and what it says --------------------------------------------------

    private static SeriesConfig Candles()
    {
        var config = new SeriesConfig { Id = "candles", IndicatorCode = "candles", Name = "Candles" };
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

    private static WorkspaceState PnfState(double[]? closes = null)
    {
        var bars = Closes(closes ?? Swing);
        var settings = new RenkoSettings(PnfMode: RenkoBoxMode.Fixed, PnfSize: 1, PnfReversal: 3);
        Assert.True(BuiltCharts.TryResolveBox(PriceChartStyle.PointAndFigure, bars, settings, out double box, out _));
        var built = BuiltCharts.Build(PriceChartStyle.PointAndFigure, bars, settings, box, Hour);
        var state = WorkspaceState.Initial with
        {
            Identity = Id,
            Data = built.Bricks,
            TimeBars = new TimeSeriesBuffer<Ohlcv>(bars),
            BrickSpans = built.Spans,
            BrickBoxSize = box,
            Renko = settings,
            ChartStyle = PriceChartStyle.PointAndFigure,
            ActiveSeries = ImmutableList.Create(new ChartSeries(Candles(), new SeriesDataBuffer { SeriesId = "candles" })),
            FocusedSeriesId = "candles",
            CurrentDataIndex = built.Bricks.Count - 1,
            ViewportStartIndex = 0,
            ViewportLength = 5,
            LastInteractionContext = InteractionContext.Component,
            SpeakTimestamps = true,
            TimestampReadLocation = "Along X Axis",
            ReadColumnHeaders = true,
            SpeechOrder = "HeaderValue",
            InitStatus = InitializationStatus.Ready,
        };
        return PriceSeriesShape.Apply(state);
    }

    private static string Speak(WorkspaceState state)
    {
        var spy = new SpySpeechRouter();
        new NavigationFeedbackManager(spy, new SpeechFormatter()) { IsSpeechEnabled = true }
            .HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");
        Assert.NotEmpty(spy.SpokenTexts);
        return string.Join(" ", spy.SpokenTexts);
    }

    [Fact]
    public void The_price_series_is_named_point_and_figure_with_one_column_part()
    {
        var series = PnfState().ActiveSeries[0];

        Assert.Equal("Point and figure", series.Config.Name);
        var part = Assert.Single(series.Components);
        Assert.Equal(PriceSeriesShape.PnfPart, part.Name);
        Assert.Equal("Column", part.DisplayName);
        Assert.Equal(ComponentDisplayType.Brick, part.DisplayType);
    }

    [Fact]
    public void The_price_series_holds_each_columns_boxes_and_follows_the_columns()
    {
        var state = PnfState();

        Assert.Equal(new[] { 3d, 3d, 5d }, state.ActiveSeries[0].Data.ComponentData[PriceSeriesShape.PnfBoxesKey]);
        // The same columns again change nothing, so the store's "did anything change" holds.
        Assert.Same(state, PriceSeriesShape.Apply(state));

        // The newest column one box shorter brings its own count.
        var bars = Closes(Swing[..^1]);
        var built = BuiltCharts.Build(PriceChartStyle.PointAndFigure, bars, state.Renko, 1, Hour);
        var next = PriceSeriesShape.Apply(state with { Data = built.Bricks, BrickSpans = built.Spans });
        Assert.Equal(new[] { 3d, 3d, 4d }, next.ActiveSeries[0].Data.ComponentData[PriceSeriesShape.PnfBoxesKey]);

        // Only a point and figure chart carries the boxes.
        var renko = PriceSeriesShape.Apply(state with
        {
            ChartStyle = PriceChartStyle.Renko,
            ActiveSeries = ImmutableList.Create(new ChartSeries(Candles(), new SeriesDataBuffer { SeriesId = "candles" })),
        });
        Assert.False(renko.ActiveSeries[0].Data.ComponentData.ContainsKey(PriceSeriesShape.PnfBoxesKey));
    }

    [Fact]
    public void Arrowing_onto_a_column_says_its_mark_and_boxes_as_drawn()
    {
        var state = PnfState();

        Assert.Contains("Column. Up, X, 5 boxes. 100.00 to 105.00.", Speak(state));
        Assert.Contains("Column. Down, O, 3 boxes. 103.00 to 100.00.", Speak(state with { CurrentDataIndex = 1 }));
    }

    [Fact]
    public void The_brick_summary_carries_the_boxes_only_on_point_and_figure()
    {
        var state = PnfState();

        Assert.Equal("Up column, X, 5 boxes, 100.00 to 105.00",
            BrickChartSpeech.BrickSummary(state.Data[2], state.BrickSpans[2], PriceChartStyle.PointAndFigure,
                state.Data, 2, state.BrickBoxSize));
        Assert.Equal("Up brick, 100.00 to 105.00",
            BrickChartSpeech.BrickSummary(state.Data[2], state.BrickSpans[2], PriceChartStyle.Renko,
                state.Data, 2, state.BrickBoxSize));
    }

    [Fact]
    public void The_change_sentence_names_the_box_and_the_reversal()
    {
        Assert.Equal("Point and figure chart, fixed box 1.00, reversal 3 boxes.", BrickChartSpeech.ChangeSentence(PnfState()));
        var atr = PnfState() with { Renko = new RenkoSettings(PnfMode: RenkoBoxMode.Atr, PnfAtrLength: 9, PnfReversal: 1) };
        Assert.Equal("Point and figure chart, box 1.00 from ATR 9, reversal 1 box.", BrickChartSpeech.ChangeSentence(atr));
    }

    [Fact]
    public void The_symbol_line_shows_the_columns_mark_and_boxes()
    {
        var state = PnfState();
        var theme = Theme();
        var ctx = new ChartStatusLine.Context(theme, ChartStatusLine.DefaultBackingAlpha, state.Data, Id,
            ChartStyle: PriceChartStyle.PointAndFigure,
            PnfBoxes: ChartStatusLine.PnfBoxesOf(state.ActiveSeries, PriceChartStyle.PointAndFigure));
        var line = ChartStatusLine.Build("Main", Array.Empty<ChartSeries>(), 1, ctx, 400f, 4000f,
            ChartStatusLine.LinePx, t => t.Length * 7f).Lines[0];

        Assert.Equal("Down column, O, 3 boxes", line.Values[0].Label);
    }

    // ---- Live: a new column, and a column growing box by box --------------------------------

    private static (AccessibilityFeedbackCoordinator Coordinator, MockWorkspaceStore Store, CounterSpeechManager Speech, SpyEventBus Bus) Coordinator()
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
        return (coordinator, store, speech, bus);
    }

    [Fact]
    public void A_new_column_is_spoken_with_its_mark_and_boxes()
    {
        var state = PnfState(Swing[..^1]);   // the newest column, X 100→104, has just turned
        var (coordinator, store, speech, bus) = Coordinator();
        using (coordinator)
        {
            store.EmitState(state);
            speech.Utterances.Clear();

            bus.Publish(new NewBarEvent(state.Data[^2], state.Data[^1]));

            Assert.Equal("New up column, X, 4 boxes, 100.00 to 104.00.", speech.Utterances[^1].Text);
        }
    }

    [Fact]
    public void A_growing_column_says_each_new_box_once()
    {
        var at104 = PnfState(Swing[..^1]);   // the newest column, X 100→104, has just turned
        var at105 = PnfState();              // the same column a box higher
        var (coordinator, store, speech, bus) = Coordinator();
        using (coordinator)
        {
            var at103 = PnfState(Swing[..7].Append(103.5).ToArray());   // the same column a box lower
            Assert.Equal((at104.Data[^1].Date, at104.Data[^1].Open), (at103.Data[^1].Date, at103.Data[^1].Open));

            store.EmitState(at104);
            speech.Utterances.Clear();
            bus.Publish(new NewBarEvent(at104.Data[^2], at104.Data[^1]));   // said at 4 boxes
            bus.Publish(new IntraBarUpdateEvent(at104.Data[^1], at104.Data[^1]));   // nothing new
            store.EmitState(at103);
            bus.Publish(new IntraBarUpdateEvent(at103.Data[^1], at104.Data[^1]));   // wavered down a box
            store.EmitState(at104);
            bus.Publish(new IntraBarUpdateEvent(at104.Data[^1], at103.Data[^1]));   // and back: already said
            Assert.Equal(new[] { "New up column, X, 4 boxes, 100.00 to 104.00." }, speech.Utterances.Select(u => u.Text));
            speech.Utterances.Clear();

            store.EmitState(at105);
            bus.Publish(new IntraBarUpdateEvent(at105.Data[^1], at104.Data[^1]));   // grew to 5
            store.EmitState(at104);
            bus.Publish(new IntraBarUpdateEvent(at104.Data[^1], at105.Data[^1]));   // took the box back
            store.EmitState(at105);
            bus.Publish(new IntraBarUpdateEvent(at105.Data[^1], at104.Data[^1]));   // and gave it again

            Assert.Equal(new[] { "X column, 5 boxes, 105.00." }, speech.Utterances.Select(u => u.Text));
        }
    }

    [Fact]
    public void Growth_is_news_only_for_a_new_box_on_the_same_column()
    {
        var columns = PnfState().Data;
        var now = columns[^1];

        Assert.Equal("X column, 5 boxes, 105.00.", BrickChartSpeech.PnfGrowNews(columns, now with { Close = 104 }, 1, 0));
        Assert.Null(BrickChartSpeech.PnfGrowNews(columns, now with { Close = 104 }, 1, 5));         // already said
        Assert.Null(BrickChartSpeech.PnfGrowNews(columns, now, 1, 0));                              // no new box
        Assert.Null(BrickChartSpeech.PnfGrowNews(columns, now with { Close = 104, Open = 101 }, 1, 0)); // another column
        Assert.Null(BrickChartSpeech.PnfGrowNews(columns, null, 1, 0));
    }

    // ---- The browser-closed monitor --------------------------------------------------------

    [Fact]
    public void A_background_tab_hears_a_column_turn_and_grow_but_not_a_close_inside_it()
    {
        // The last entry is the bar that has just opened; the one before it closed.
        var turned = BrickNews.CompletedAtClose(Closes(Swing[..7].Append(999).ToArray()), 1, Id, PriceChartStyle.PointAndFigure,
            Mixed);
        var grew = BrickNews.CompletedAtClose(Closes(Swing.Append(999).ToArray()), 1, Id, PriceChartStyle.PointAndFigure, Mixed);
        var inside = BrickNews.CompletedAtClose(Closes(Swing[..5].Append(999).ToArray()), 1, Id, PriceChartStyle.PointAndFigure,
            Mixed);

        Assert.Equal((103d, 100d), (Assert.Single(turned).Brick.Open, turned[0].Brick.Close));
        Assert.Equal((100d, 105d), (Assert.Single(grew).Brick.Open, grew[0].Brick.Close));
        Assert.Empty(inside);

        Assert.Equal("BTC/USD 1h: up column, X, 5 boxes, 100.00 to 105.00.",
            BrickNews.Sentence(Id, grew, style: PriceChartStyle.PointAndFigure, box: 1));
        Assert.Equal("BTC/USD 1h: column moved", BrickNews.Title("BTC/USD", "1h", PriceChartStyle.PointAndFigure));
    }

    // ---- New-bar alerts follow a column as it grows ----------------------------------------

    [Fact]
    public void A_new_up_column_alert_fires_on_a_turn_and_not_on_growth()
    {
        var ev = new AlertEvaluator(Substitute.For<ISdkCandlePatternAnalyzer>(), Substitute.For<IIndicatorContextAnalyzer>());
        var alert = new AlertDefinition
        {
            Id = "p", Name = "PnF", Target = AlertTarget.Candle, Condition = AlertCondition.NewUpBar,
            Delivery = AlertDelivery.Speech,
        };
        List<AlertFired> Eval(WorkspaceState s) =>
            ev.EvaluateAlerts(new[] { alert }, s, s.Data[^1], s.Data[^2], new Dictionary<string, double>()).ToList();

        Assert.Empty(Eval(PnfState(Swing[..7])));                    // seeds: X, then O
        Assert.Single(Eval(PnfState(Swing[..8])));                   // turned up: new X column
        Assert.Empty(Eval(PnfState(Swing)));                         // the same column, grown
    }

    // ---- The Go bar and the command ----------------------------------------------------------

    [Theory]
    [InlineData("500", RenkoBoxMode.Fixed, 500, 14, 3)]
    [InlineData("500 2", RenkoBoxMode.Fixed, 500, 14, 2)]
    [InlineData("ATR", RenkoBoxMode.Atr, 0, 14, 3)]
    [InlineData("ATR 20", RenkoBoxMode.Atr, 0, 20, 3)]
    [InlineData("atr 20 1", RenkoBoxMode.Atr, 0, 20, 1)]
    public void PNF_takes_a_box_or_ATR_then_a_reversal(string argument, RenkoBoxMode mode, double size, int atr, int reversal)
    {
        Assert.True(GoBarParser.TryParsePointFigure(argument, out var settings));
        Assert.Equal((mode, size, atr, reversal),
            (settings.PnfMode, settings.PnfSize, settings.PnfAtrLength, settings.PnfReversal));
        Assert.Equal(new RenkoSettings().BoxSize, settings.BoxSize);   // Renko's own fields untouched
    }

    [Theory]
    [InlineData("banana")]
    [InlineData("500 0")]
    [InlineData("500 21")]
    [InlineData("500 2 3")]
    [InlineData("ATR 20 3 1")]
    [InlineData("500 2.5")]
    public void A_bad_PNF_argument_says_point_and_figures_own_sentence(string argument)
    {
        Assert.False(GoBarParser.TryParsePointFigure(argument, out _));
        Assert.Equal(BrickChartSpeech.PnfBadArgument, GoBarParser.Parse("PNF " + argument).Refusal);
    }

    [Theory]
    [InlineData("PNF")]
    [InlineData("point and figure")]
    [InlineData("p&f")]
    public void Point_and_figure_is_reachable_by_its_own_words(string typed)
    {
        var top = GoBarMatcher.Match(typed, GoBarRegistry.All).FirstOrDefault();

        Assert.Equal(SystemCommand.ShowPointFigureChart, top?.Entry.Command);
    }

    [Theory]
    [InlineData("PNF 500 2", "Point and figure chart, fixed box 500.00, reversal 2 boxes", "PNF 500 2")]
    [InlineData("PF ATR 20", "Point and figure chart, ATR 20, reversal 3 boxes", "PNF ATR 20 3")]
    public void PNF_makes_a_point_and_figure_action(string typed, string name, string canonical)
    {
        var bus = new EventBus();
        using var stack = new ModalStack(bus);
        var service = new GoBarService(Substitute.For<ICommandDispatcher>(), Substitute.For<IShortcutManager>(), bus, stack);

        var option = service.Suggest(typed).Options[0];
        var action = Assert.IsType<GoBarRenkoAction>(option.Action);

        Assert.Equal(PriceChartStyle.PointAndFigure, action.Style);
        Assert.Equal(name, option.Name);
        Assert.Equal(canonical, option.CanonicalText);
    }

    [Fact]
    public void The_same_point_and_figure_again_says_already_and_changes_nothing()
    {
        var state = PnfState();
        var store = new MockWorkspaceStore();
        store.EmitState(state);
        var bus = new SpyEventBus();

        Assert.False(RenkoChoice.TryApply(store, bus, PriceChartStyle.PointAndFigure, state.Renko));

        Assert.Contains(bus.Log.OfType<FeedbackRequestEvent>(), e => e.Message == "Already a point and figure chart.");
        Assert.Empty(store.DispatchedActions);
    }

    [Fact]
    public void ShowPointFigureChart_shows_point_and_figure()
    {
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            Identity = Id, Data = new TimeSeriesBuffer<Ohlcv>(Closes(Swing)), Renko = Mixed,
            CurrentDataIndex = 8, InitStatus = InitializationStatus.Ready,
        });
        var dispatcher = new CommandDispatcher(
            bus, Substitute.For<INavigationEngine>(), store,
            Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));

        bus.Publish(new ChartFocusEvent());
        dispatcher.Dispatch(SystemCommand.ShowPointFigureChart);

        Assert.Equal(PriceChartStyle.PointAndFigure,
            Assert.Single(store.DispatchedActions.OfType<SetChartStyleAction>()).Style);
    }

    // ---- The drawing ------------------------------------------------------------------------

    private const int W = 300, H = 300;

    private static ChartTheme Theme()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings).Current;
    }

    private static bool CloseTo(SKColor a, SKColor b, int tolerance = 12) =>
        Math.Abs(a.Red - b.Red) <= tolerance && Math.Abs(a.Green - b.Green) <= tolerance
        && Math.Abs(a.Blue - b.Blue) <= tolerance;

    private static int Count(SKBitmap bmp, SKRect area, SKColor colour)
    {
        int n = 0;
        for (int x = (int)area.Left; x < (int)area.Right; x++)
            for (int y = (int)area.Top; y < (int)area.Bottom; y++)
                if (CloseTo(bmp.GetPixel(x, y), colour)) n++;
        return n;
    }

    private static SKBitmap Draw(WorkspaceState state, double min, double max)
    {
        var columns = state.Data;
        var bmp = new SKBitmap(W, H);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        var r = SKRect.Create(0, 0, W, H);
        var ctx = new RenderContext(canvas, r, columns, 0, columns.Count, min, max, false,
            r.Width / columns.Count, 1f, "Main", 0, Theme())
        { ChartStyle = PriceChartStyle.PointAndFigure };
        StandardRenderers.RenderPointFigure(ctx, state.ActiveSeries[0]);
        return bmp;
    }

    [Fact]
    public void Each_column_is_drawn_as_its_boxes_X_up_and_O_down()
    {
        var state = PnfState();
        var theme = Theme();
        using var bmp = Draw(state, 99, 106);   // seven boxes over 300 pixels: 42 a box

        int Y(double price) => (int)Math.Round(H - (price - 99) / 7 * H);
        var up = theme.CandleBullishBody;
        var down = theme.CandleBearishBody;

        // Column 0 (x 0..100): X marks from 100 to 103 in the up colour; none above or below.
        Assert.True(Count(bmp, new SKRect(10, Y(103) + 2, 90, Y(100) - 2), up) > 100);
        Assert.Equal(0, Count(bmp, new SKRect(0, 0, 100, Y(103) - 2), up));
        Assert.Equal(0, Count(bmp, new SKRect(0, 0, 100, H), down));
        // Column 1 (x 100..200): O marks in the down colour.
        Assert.True(Count(bmp, new SKRect(110, Y(103) + 2, 190, Y(100) - 2), down) > 100);
        Assert.Equal(0, Count(bmp, new SKRect(100, 0, 200, H), up));

        // An X crosses at the middle of its box; an O is hollow there.
        int mid = Y(101.5);
        Assert.True(CloseTo(bmp.GetPixel(50, mid), up) || CloseTo(bmp.GetPixel(50, mid - 1), up));
        Assert.False(CloseTo(bmp.GetPixel(150, mid), down));
        Assert.True(Count(bmp, new SKRect(110, mid - 1, 125, mid + 2), down) > 0);   // the O's side
    }

    [Fact]
    public void A_box_too_small_to_mark_draws_the_column_as_a_solid_bar()
    {
        var state = PnfState();
        var theme = Theme();
        using var bmp = Draw(state, 50, 150);   // a box is 3 pixels: under the 4 a mark needs

        // Column 2 rises 100 → 105: x 210..290, y 150..135, filled through, box edges and all.
        int Y(double price) => (int)Math.Round(H - (price - 50) / 100 * H);
        var inside = new SKRect(215, Y(105) + 1, 285, Y(100) - 1);
        int area = (int)(inside.Width * inside.Height);
        int filled = Count(bmp, inside, theme.CandleBullishBody);
        Assert.True(filled >= area * 95 / 100, $"{filled} of {area} pixels filled");
    }

    // ---- Boxes heard (Cody's rule: what is drawn is heard) -----------------------------------

    private static AudioPoint Sound(WorkspaceState state, int index)
    {
        var series = state.ActiveSeries[0];
        var part = Assert.Single(series.Components);
        var point = state.Data[index];
        return new AccessibleTrader.Core.Services.Audio.DefaultSonificationStrategy(new AccessibleTrader.Core.Services.Audio.SoundPatchRegistry())
            .CreateAudioPoint(series, part, point.Close, point, 0, 10, (90, 130), 1f, dataIndex: index);
    }

    [Fact]
    public void A_taller_column_sounds_heavier()
    {
        var state = PnfState();
        var three = Sound(state, 0);   // X, 3 boxes
        var five = Sound(state, 2);    // X, 5 boxes
        var one = Sound(PnfState(new double[] { 100, 101.2 }), 0);   // X, 1 box

        Assert.Equal(0f, one.SubSawMix);
        Assert.True(five.SubSawMix > three.SubSawMix && three.SubSawMix > 0f,
            $"sub-saw 1 box {one.SubSawMix}, 3 boxes {three.SubSawMix}, 5 boxes {five.SubSawMix}");
        // Direction still colours it as a brick's does.
        Assert.Equal(three.SquareMix, five.SquareMix);
        Assert.True(Sound(state, 1).TriangleMix > five.TriangleMix);
    }
}
