using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Charts;
using AccessibleTrader.Core.Services.GoBar;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// Range bars and line break, the two chart types built off the clock beside Renko: their
/// builders, the one settings record they share with Renko, switching between the three, and
/// what each is called and says.
/// </summary>
public sealed class BuiltChartTypesTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ChartIdentity Id = new("Spot", "Bitstamp", "BTC/USD", "1h");
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private static Ohlcv Bar(int i, double open, double high, double low, double close, double volume = 10)
        => new(Start.AddHours(i), open, high, low, close, volume);

    private static List<Ohlcv> Closes(params double[] closes)
        => closes.Select((c, i) => Bar(i, c, c, c, c)).ToList();

    // ---- Range bar builder ------------------------------------------------------------------

    [Fact]
    public void Every_completed_range_bar_spans_exactly_the_range_and_the_forming_one_is_left_out()
    {
        var built = RangeBarBuilder.Build(new[] { Bar(0, 100, 130, 100, 130) }, 10, Hour);

        Assert.True(built.Ok);
        Assert.Equal(2, built.Bricks.Count);   // 100-110 and 110-120; 120-130 has not broken out yet
        Assert.All(built.Bricks, b => Assert.Equal(10, b.High - b.Low, 9));
        Assert.Equal(new[] { 110d, 120d }, built.Bricks.Select(b => b.Close));
        Assert.All(built.Spans, s => Assert.True(s.Up));
    }

    [Fact]
    public void A_range_bar_keeps_the_wick_price_traded_through()
    {
        // The first bar dips to 95 before the second carries price past 95 + 20.
        var built = RangeBarBuilder.Build(new[]
        {
            Bar(0, 100, 105, 95, 100),
            Bar(1, 100, 120, 100, 118),
        }, 20, Hour);

        var bar = Assert.Single(built.Bricks);
        Assert.Equal(100, bar.Open);
        Assert.Equal(95, bar.Low);
        Assert.Equal(115, bar.High);
        Assert.Equal(115, bar.Close);
        Assert.Equal(new BrickSpan(Start, Start.AddHours(2), 0, 1, true), built.Spans[0]);
    }

    [Fact]
    public void A_gap_of_three_ranges_makes_three_range_bars()
    {
        var built = RangeBarBuilder.Build(new[] { Bar(0, 100, 100, 100, 100), Bar(1, 135, 135, 135, 135) }, 10, Hour);

        Assert.Equal(3, built.Bricks.Count);
        Assert.Equal(new[] { 110d, 120d, 130d }, built.Bricks.Select(b => b.Close));
    }

    [Fact]
    public void A_down_bar_walks_its_high_before_its_low()
    {
        // Open 100, high 108, low 90, close 92 with a range of 15: the high comes first, so the
        // bar breaks DOWN at 108 - 15 = 93, not up.
        var built = RangeBarBuilder.Build(new[] { Bar(0, 100, 108, 90, 92) }, 15, Hour);

        var bar = Assert.Single(built.Bricks);
        Assert.False(built.Spans[0].Up);
        Assert.Equal(108, bar.High);
        Assert.Equal(93, bar.Close);
    }

    [Fact]
    public void A_non_positive_range_is_refused()
    {
        var built = RangeBarBuilder.Build(Closes(100, 110), 0, Hour);

        Assert.False(built.Ok);
        Assert.Equal("A range bar's range must be a positive price.", built.Refusal);
    }

    // ---- Line break builder -----------------------------------------------------------------

    [Fact]
    public void Three_line_break_turns_only_past_the_low_of_the_last_three_blocks()
    {
        var built = LineBreakBuilder.Build(Closes(100, 110, 120, 130, 125, 105, 95), 3, Hour);

        Assert.Equal(4, built.Bricks.Count);
        Assert.Equal(new[] { true, true, true, false }, built.Spans.Select(s => s.Up));
        var turn = built.Bricks[^1];
        Assert.Equal(120, turn.Open);   // from the last block's open
        Assert.Equal(95, turn.Close);
        Assert.Equal(new BrickSpan(Start.AddHours(4), Start.AddHours(7), 4, 6, false), built.Spans[^1]);
    }

    [Fact]
    public void One_line_break_turns_as_soon_as_the_last_block_is_broken()
    {
        var built = LineBreakBuilder.Build(Closes(100, 110, 120, 130, 115), 1, Hour);

        Assert.Equal(4, built.Bricks.Count);
        Assert.False(built.Spans[^1].Up);
        Assert.Equal(120, built.Bricks[^1].Open);
        Assert.Equal(115, built.Bricks[^1].Close);
    }

    [Fact]
    public void A_line_break_block_is_as_tall_as_the_move_that_made_it()
    {
        var built = LineBreakBuilder.Build(Closes(100, 101, 150), 3, Hour);

        Assert.Equal(new[] { (100d, 101d), (101d, 150d) }, built.Bricks.Select(b => (b.Open, b.Close)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void A_line_count_outside_1_to_50_is_refused(int lines)
    {
        var built = LineBreakBuilder.Build(Closes(100, 110), lines, Hour);

        Assert.False(built.Ok);
        Assert.Equal("Line break takes 1 to 50 lines.", built.Refusal);
    }

    // ---- One settings record, three types ---------------------------------------------------

    private static readonly RenkoSettings Mixed = new(RenkoBoxMode.Fixed, 500, 21)
    {
        RangeMode = RenkoBoxMode.Fixed, RangeSize = 20, RangeAtrLength = 9, LineBreakLines = 3,
    };

    [Fact]
    public void Merge_takes_only_the_chosen_types_own_fields()
    {
        var range = BuiltCharts.Merge(PriceChartStyle.RangeBars, Mixed,
            new RenkoSettings(RangeMode: RenkoBoxMode.Atr, RangeAtrLength: 30));
        Assert.Equal(Mixed with { RangeMode = RenkoBoxMode.Atr, RangeSize = 0, RangeAtrLength = 30 }, range);

        var lines = BuiltCharts.Merge(PriceChartStyle.LineBreak, Mixed, new RenkoSettings(LineBreakLines: 2));
        Assert.Equal(Mixed with { LineBreakLines = 2 }, lines);

        var renko = BuiltCharts.Merge(PriceChartStyle.Renko, Mixed, new RenkoSettings(RenkoBoxMode.Fixed, 7, 14));
        Assert.Equal(Mixed with { BoxSize = 7, AtrLength = 14 }, renko);
    }

    [Fact]
    public void SameFor_compares_only_the_shown_types_own_fields()
    {
        var otherRange = Mixed with { RangeSize = 99 };

        Assert.True(BuiltCharts.SameFor(PriceChartStyle.Renko, Mixed, otherRange));
        Assert.True(BuiltCharts.SameFor(PriceChartStyle.LineBreak, Mixed, otherRange));
        Assert.False(BuiltCharts.SameFor(PriceChartStyle.RangeBars, Mixed, otherRange));
        Assert.False(BuiltCharts.SameFor(PriceChartStyle.LineBreak, Mixed, Mixed with { LineBreakLines = 4 }));
    }

    [Fact]
    public void Range_bars_and_line_break_are_built_off_the_clock_and_only_line_break_is_a_block()
    {
        Assert.True(PriceChartStyle.RangeBars.IsBrickStyle());
        Assert.True(PriceChartStyle.LineBreak.IsBrickStyle());
        Assert.False(PriceChartStyle.RangeBars.IsBlockStyle());
        Assert.True(PriceChartStyle.LineBreak.IsBlockStyle());
        Assert.True(PriceChartStyle.Renko.IsBlockStyle());
    }

    // ---- The reducers: switching between the three -------------------------------------------

    private static List<Ohlcv> Rising(int count = 40, double step = 10)
        => Enumerable.Range(0, count)
            .Select(i => Bar(i, 100 + i * step, 101 + i * step, 99 + i * step, 100 + i * step, 1000))
            .ToList();

    private static WorkspaceState BarsState(RenkoSettings? settings = null) => WorkspaceState.Initial with
    {
        Identity = Id,
        Data = new TimeSeriesBuffer<Ohlcv>(Rising()),
        Renko = settings ?? Mixed with { BoxSize = 10, RangeSize = 25 },
        CurrentDataIndex = 39,
        ViewportLength = 30,
        InitStatus = InitializationStatus.Ready,
    };

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
    public void Switching_Renko_to_range_bars_to_line_break_freezes_each_types_own_size()
    {
        var initial = BarsState();

        var renko = Reduce(initial, new SetChartStyleAction(PriceChartStyle.Renko, false));
        Assert.Equal(10, renko.BrickBoxSize);

        var range = Reduce(initial,
            new SetChartStyleAction(PriceChartStyle.Renko, false),
            new SetChartStyleAction(PriceChartStyle.RangeBars, false));
        Assert.Equal(PriceChartStyle.RangeBars, range.ChartStyle);
        Assert.Equal(25, range.BrickBoxSize);   // the range, not Renko's stale box of 10
        Assert.NotEmpty(range.Data);
        Assert.All(range.Data, b => Assert.Equal(25, b.High - b.Low, 9));
        Assert.Equal(40, range.TimeBars!.Count);

        var lines = Reduce(initial,
            new SetChartStyleAction(PriceChartStyle.RangeBars, false),
            new SetChartStyleAction(PriceChartStyle.LineBreak, false));
        Assert.Equal(PriceChartStyle.LineBreak, lines.ChartStyle);
        Assert.Equal(3, lines.BrickBoxSize);    // line break's frozen value is its line count
        Assert.Equal(39, lines.Data.Count);     // one block per rising close after the first
        Assert.Equal(lines.Data.Count, lines.BrickSpans.Length);

        var back = Reduce(initial,
            new SetChartStyleAction(PriceChartStyle.LineBreak, false),
            new SetChartStyleAction(PriceChartStyle.Candles, false));
        Assert.Equal(40, back.Data.Count);
        Assert.Equal(0, back.BrickBoxSize);
        Assert.True(back.BrickSpans.IsDefaultOrEmpty);
    }

    [Fact]
    public void A_line_count_change_on_a_line_break_tab_rebuilds_its_blocks()
    {
        var bars = Closes(100, 110, 120, 130, 115);
        var initial = BarsState() with { Data = new TimeSeriesBuffer<Ohlcv>(bars), CurrentDataIndex = 4 };

        var three = Reduce(initial, new SetChartStyleAction(PriceChartStyle.LineBreak, false));
        var one = Reduce(initial,
            new SetChartStyleAction(PriceChartStyle.LineBreak, false),
            new SetRenkoSettingsAction(initial.Renko with { LineBreakLines = 1 }));

        Assert.Equal(3, three.Data.Count);
        Assert.Equal(4, one.Data.Count);
        Assert.Equal(1, one.BrickBoxSize);
    }

    // ---- The price series: named after the type, and its parts -------------------------------

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

    private static WorkspaceState BuiltState(PriceChartStyle style, IReadOnlyList<Ohlcv> bars, RenkoSettings settings)
    {
        Assert.True(BuiltCharts.TryResolveBox(style, bars, settings, out double box, out _));
        var built = BuiltCharts.Build(style, bars, settings, box, Hour);
        Assert.NotEmpty(built.Bricks);
        var state = WorkspaceState.Initial with
        {
            Identity = Id,
            Data = built.Bricks,
            TimeBars = new TimeSeriesBuffer<Ohlcv>(bars),
            BrickSpans = built.Spans,
            BrickBoxSize = box,
            Renko = settings,
            ChartStyle = style,
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

    private static WorkspaceState LineBreakState()
        => BuiltState(PriceChartStyle.LineBreak,
            Closes(100, 110, 120, 125).Select((b, i) => b with { High = b.Close + 1, Low = b.Close - 2 }).ToList(),
            new RenkoSettings(LineBreakLines: 3));

    private static WorkspaceState RangeState()
        => BuiltState(PriceChartStyle.RangeBars,
            new List<Ohlcv> { Bar(0, 100, 105, 95, 100), Bar(1, 100, 120, 100, 118), Bar(2, 118, 140, 117, 139) },
            new RenkoSettings(RangeMode: RenkoBoxMode.Fixed, RangeSize: 20));

    private static string Speak(WorkspaceState state)
    {
        var spy = new SpySpeechRouter();
        new NavigationFeedbackManager(spy, new SpeechFormatter()) { IsSpeechEnabled = true }
            .HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");
        Assert.NotEmpty(spy.SpokenTexts);
        return string.Join(" ", spy.SpokenTexts);
    }

    [Fact]
    public void The_price_series_is_named_after_range_bars_and_line_break()
    {
        Assert.Equal("Range bars", RangeState().ActiveSeries[0].Config.Name);
        Assert.Equal("Line break", LineBreakState().ActiveSeries[0].Config.Name);

        // Both names are chart-type names, so leaving the type renames the series again.
        var lines = LineBreakState().ActiveSeries[0];
        Assert.Equal("Renko", PriceSeriesShape.Apply(lines, PriceChartStyle.Renko).Config.Name);
        var range = RangeState().ActiveSeries[0];
        Assert.Equal("Line break", PriceSeriesShape.Apply(range, PriceChartStyle.LineBreak).Config.Name);
    }

    [Fact]
    public void A_line_break_chart_has_one_Block_part_read_by_the_template()
    {
        var state = LineBreakState();

        var block = Assert.Single(state.ActiveSeries[0].Components);
        Assert.Equal("block", block.Name);
        Assert.Equal("Block", block.DisplayName);
        Assert.Equal(ComponentDisplayType.Brick, block.DisplayType);

        var last = state.Data[^1];
        var span = state.BrickSpans[^1];
        string spoken = Speak(state);
        string edges = $"{SpeechPriceFormatter.FormatPrice(last.Open)} to {SpeechPriceFormatter.FormatPrice(last.Close)}";
        Assert.Contains($"Block. Up. {edges}. {SpeechTimeFormatter.FormatBrickRange(span.Start, span.End, 3600)}.", spoken);
        Assert.DoesNotContain("wick", spoken, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("brick", spoken, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_range_bar_keeps_its_three_candle_parts_and_reads_as_a_candle_with_its_window()
    {
        var state = RangeState();

        Assert.Equal(3, state.ActiveSeries[0].Components.Count);
        Assert.DoesNotContain(state.ActiveSeries[0].Components, c => c.DisplayType == ComponentDisplayType.Brick);

        var span = state.BrickSpans[^1];
        string spoken = Speak(state with { FocusedComponentIndex = 1 });
        Assert.DoesNotContain("brick", spoken, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(SpeechTimeFormatter.FormatBrickRange(span.Start, span.End, 3600), spoken);
    }

    [Fact]
    public void Each_type_says_its_own_change_sentence()
    {
        Assert.Equal("Line break chart, 3 lines.", BrickChartSpeech.ChangeSentence(LineBreakState()));
        Assert.Equal("Line break chart, 1 line.", BrickChartSpeech.ChangeSentence(
            LineBreakState() with { Renko = new RenkoSettings(LineBreakLines: 1) }));
        Assert.Equal("Range bars chart, fixed range 20.00.", BrickChartSpeech.ChangeSentence(RangeState()));
        Assert.Equal("Range bars chart, range 20.00 from ATR 9.", BrickChartSpeech.ChangeSentence(
            RangeState() with { Renko = new RenkoSettings(RangeMode: RenkoBoxMode.Atr, RangeAtrLength: 9) }));
    }

    [Fact]
    public void The_background_news_names_blocks_and_range_bars()
    {
        var state = LineBreakState();
        var two = state.Data.Skip(1).Zip(state.BrickSpans.Skip(1)).Select(p => (p.First, p.Second)).ToList();

        Assert.Equal("BTC/USD 1h: new block", BrickNews.Title("BTC/USD", "1h", PriceChartStyle.LineBreak));
        Assert.Equal("BTC/USD: new range bar", BrickNews.Title("BTC/USD", "", PriceChartStyle.RangeBars));
        Assert.StartsWith("2 new up blocks, ", BrickNews.Summary(two, PriceChartStyle.LineBreak));
        Assert.StartsWith("new up range bar, ", BrickNews.Summary(two.Take(1).ToList(), PriceChartStyle.RangeBars));
    }

    [Fact]
    public void A_background_line_break_tab_hears_the_block_its_own_line_count_makes()
    {
        // The newest bar is still forming and is left out; the close before it, 115, breaks the
        // last block on ONE line but not on three.
        var bars = Closes(100, 110, 120, 130, 115, 999);

        var one = BrickNews.CompletedAtClose(bars, 1, Id, PriceChartStyle.LineBreak, new RenkoSettings(LineBreakLines: 1));
        var three = BrickNews.CompletedAtClose(bars, 3, Id, PriceChartStyle.LineBreak, new RenkoSettings(LineBreakLines: 3));

        var block = Assert.Single(one);
        Assert.False(block.Span.Up);
        Assert.Equal((120d, 115d), (block.Brick.Open, block.Brick.Close));
        Assert.Empty(three);
        Assert.Equal(3, BrickNews.BoxFor(bars, new RenkoSettings(LineBreakLines: 3), 0, PriceChartStyle.LineBreak));
    }

    [Fact]
    public void A_background_range_bar_tab_hears_the_range_bars_the_last_close_completed()
    {
        var bars = new List<Ohlcv> { Bar(0, 100, 100, 100, 100), Bar(1, 100, 125, 100, 125), Bar(2, 125, 125, 125, 125) };

        var news = BrickNews.CompletedAtClose(bars, 10, Id, PriceChartStyle.RangeBars);

        Assert.Equal(new[] { 110d, 120d }, news.Select(n => n.Brick.Close));
        Assert.Equal("BTC/USD 1h: 2 new up range bars, 100.00 to 120.00.",
            BrickNews.Sentence(Id, news, style: PriceChartStyle.RangeBars));
    }

    // ---- What the announcer says -------------------------------------------------------------

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
    public void A_new_line_break_block_is_spoken_as_a_block()
    {
        var state = LineBreakState();
        var (coordinator, store, speech, bus) = Coordinator();
        using (coordinator)
        {
            store.EmitState(state);
            speech.Utterances.Clear();

            bus.Publish(new NewBarEvent(state.Data[^2], state.Data[^1]));

            var last = state.Data[^1];
            Assert.Equal(
                $"New up block, {SpeechPriceFormatter.FormatPrice(last.Open)} to {SpeechPriceFormatter.FormatPrice(last.Close)}.",
                speech.Utterances[^1].Text);
        }
    }

    [Fact]
    public void Choosing_range_bars_from_Renko_says_the_range_bars_sentence_once_and_never_Renko()
    {
        var renko = BuiltState(PriceChartStyle.Renko, Rising(), Mixed with { BoxSize = 10, RangeSize = 25 });
        var range = BuiltState(PriceChartStyle.RangeBars, Rising(), Mixed with { BoxSize = 10, RangeSize = 25 });
        var (coordinator, store, speech, _) = Coordinator();
        using (coordinator)
        {
            store.EmitState(renko);
            speech.Utterances.Clear();

            store.EmitState(range);

            Assert.Single(speech.Utterances, u => u.Text.Contains("Range bars chart, fixed range 25.00."));
            Assert.DoesNotContain(speech.Utterances, u => u.Text.Contains("Renko chart"));
        }
    }

    // ---- The Go bar and the command ----------------------------------------------------------

    [Theory]
    [InlineData("500", RenkoBoxMode.Fixed, 500, 14)]
    [InlineData("ATR 20", RenkoBoxMode.Atr, 0, 20)]
    [InlineData("ATR", RenkoBoxMode.Atr, 0, 14)]
    public void RANGEBAR_takes_a_range_or_ATR_and_a_length(string argument, RenkoBoxMode mode, double size, int atr)
    {
        Assert.True(GoBarParser.TryParseRange(argument, out var settings));
        Assert.Equal(mode, settings.RangeMode);
        Assert.Equal(size, settings.RangeSize);
        Assert.Equal(atr, settings.RangeAtrLength);
    }

    [Theory]
    [InlineData("3", true, 3)]
    [InlineData("1", true, 1)]
    [InlineData("50", true, 50)]
    [InlineData("0", false, 0)]
    [InlineData("51", false, 0)]
    [InlineData("2.5", false, 0)]
    [InlineData("-3", false, 0)]
    public void LINEBREAK_takes_a_whole_line_count_from_1_to_50(string argument, bool ok, int lines)
    {
        Assert.Equal(ok, GoBarParser.TryParseLineBreak(argument, out var settings));
        if (ok) Assert.Equal(lines, settings.LineBreakLines);
    }

    [Fact]
    public void A_bad_argument_says_each_types_own_sentence()
    {
        Assert.Equal(BrickChartSpeech.RangeBadArgument, GoBarParser.Parse("RANGEBAR banana").Refusal);
        Assert.Equal(BrickChartSpeech.LineBreakBadArgument, GoBarParser.Parse("LINEBREAK 0").Refusal);
    }

    [Theory]
    [InlineData("RANGEBAR", SystemCommand.ShowRangeBarsChart)]
    [InlineData("range bars", SystemCommand.ShowRangeBarsChart)]
    [InlineData("LBREAK", SystemCommand.ShowLineBreakChart)]
    [InlineData("line break", SystemCommand.ShowLineBreakChart)]
    [InlineData("three line break", SystemCommand.ShowLineBreakChart)]
    [InlineData("LINE", SystemCommand.ShowLineChart)]
    [InlineData("MEAS", SystemCommand.DrawMeasure)]
    public void Each_command_is_reachable_by_its_own_words(string typed, SystemCommand expected)
    {
        var top = GoBarMatcher.Match(typed, GoBarRegistry.All).FirstOrDefault();

        Assert.Equal(expected, top?.Entry.Command);
    }

    [Theory]
    [InlineData("RANGEBAR 500", PriceChartStyle.RangeBars)]
    [InlineData("LINEBREAK 2", PriceChartStyle.LineBreak)]
    public void Suggest_makes_an_action_for_the_typed_type(string typed, PriceChartStyle style)
    {
        var bus = new EventBus();
        using var stack = new ModalStack(bus);
        var service = new GoBarService(Substitute.For<ICommandDispatcher>(), Substitute.For<IShortcutManager>(), bus, stack);

        // The typed setting leads; the bare command may follow it.
        var option = service.Suggest(typed).Options[0];
        var action = Assert.IsType<GoBarRenkoAction>(option.Action);

        Assert.Equal(style, action.Style);
        if (style == PriceChartStyle.RangeBars) Assert.Equal(500, action.Settings.RangeSize);
        else Assert.Equal(2, action.Settings.LineBreakLines);
    }

    [Fact]
    public void ShowBuiltChart_keeps_the_tabs_Renko_box_when_it_sets_the_range()
    {
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        store.EmitState(BarsState(Mixed));
        var dispatcher = new CommandDispatcher(
            bus, Substitute.For<INavigationEngine>(), store,
            Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));

        dispatcher.ShowBuiltChart(PriceChartStyle.RangeBars,
            new RenkoSettings(RangeMode: RenkoBoxMode.Fixed, RangeSize: 30));

        var set = Assert.Single(store.DispatchedActions.OfType<SetRenkoSettingsAction>());
        // The range's own three fields come from what was typed (its ATR length the default
        // 14, as RENKO 500 gives the box); the Renko box and the line count stay the tab's.
        Assert.Equal(Mixed with { RangeSize = 30, RangeAtrLength = 14 }, set.Settings);
        Assert.Equal(PriceChartStyle.RangeBars,
            Assert.Single(store.DispatchedActions.OfType<SetChartStyleAction>()).Style);
    }

    [Fact]
    public void The_same_line_break_again_says_already_and_changes_nothing()
    {
        var state = LineBreakState();
        var store = new MockWorkspaceStore();
        store.EmitState(state);
        var bus = new SpyEventBus();

        Assert.False(RenkoChoice.TryApply(store, bus, PriceChartStyle.LineBreak, state.Renko));

        Assert.Contains(bus.Log.OfType<FeedbackRequestEvent>(), e => e.Message == "Already a line break chart.");
        Assert.Empty(store.DispatchedActions);
    }

    // ---- Saved with the workspace ------------------------------------------------------------

    [Fact]
    public void Line_break_and_the_range_settings_round_trip_through_the_workspace_file()
    {
        string dir = TestTemp.NewDir("att-builtcharts-");
        try
        {
            var settings = Mixed with { RangeMode = RenkoBoxMode.Atr, RangeAtrLength = 30, LineBreakLines = 5 };
            var state = BarsState(settings) with { ChartStyle = PriceChartStyle.LineBreak, BrickBoxSize = 5 };
            var withSnapshot = state with
            {
                TabSnapshots = ImmutableList.Create(new TabSnapshot(
                    0, state.Identity, state.Data, state.ActiveSeries,
                    0, null, 0, -1, 0, 0, 100, 10, (0, 0),
                    ImmutableDictionary<string, (double, double)>.Empty,
                    false, false, InteractionContext.Series, null,
                    InitializationStatus.Ready, DataStatus.Ready, false, null, 0, -1)
                {
                    ChartStyle = PriceChartStyle.LineBreak,
                    Renko = settings,
                    BrickBoxSize = 5,
                }),
            };
            var store = new MockWorkspaceStore();
            store.EmitState(withSnapshot);
            var lib = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths())
            { LibraryDirectoryOverride = dir };
            lib.SaveWorkspaceProfile("lines", store);

            var loaded = lib.LoadProfile("lines")!;

            Assert.Equal(PriceChartStyle.LineBreak, loaded.Tabs[0].ChartStyle);
            Assert.Equal(settings, loaded.Tabs[0].Renko);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
