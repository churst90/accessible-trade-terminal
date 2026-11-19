using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Charts;
using AccessibleTrader.Core.Services.GoBar;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using AccessibleTrader.Tests.Mocks;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// Kagi, the fourth chart type built off the clock: one line per swing that grows while price
/// goes its way and turns on a reversal amount; thick above the last shoulder, thin below the
/// last waist. The builder, the thick and thin rules, and the chart drawing, saying and alerting
/// them alike.
/// </summary>
public sealed class KagiChartTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ChartIdentity Id = new("Spot", "Bitstamp", "BTC/USD", "1h");
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private static Ohlcv Bar(int i, double close, double volume = 10)
        => new(Start.AddHours(i), close, close + 1, close - 1, close, volume);

    private static List<Ohlcv> Closes(params double[] closes)
        => closes.Select((c, i) => Bar(i, c, 10 + i)).ToList();

    /// <summary>Reversal 10: up 100→120, down 120→95 (turns thin at 100), up 95→125 (turns
    /// thick at 120).</summary>
    private static readonly double[] Swing = { 100, 120, 105, 95, 110, 125 };

    private static BrickBuildResult Build(IReadOnlyList<Ohlcv> bars, double reversal = 10, int max = BrickChartBuilder.MaxBricks)
        => KagiBuilder.Build(bars, reversal, Hour, max);

    // ---- The builder ------------------------------------------------------------------------

    [Fact]
    public void The_first_line_waits_for_a_reversal_amount_then_grows_and_turns_only_on_one()
    {
        // 105 is under a reversal from 100; 112 draws the first line; 120 grows it; 113 is
        // neither further nor a reversal back; 110 turns it; 125 turns it again.
        var built = Build(Closes(100, 105, 112, 120, 113, 110, 125));

        Assert.Null(built.Refusal);
        Assert.Equal(new[] { (100d, 120d), (120d, 110d), (110d, 125d) },
            built.Bricks.Select(b => (b.Open, b.Close)));
        Assert.Equal(new[] { true, false, true }, built.Spans.Select(s => s.Up));
        // Highs and lows are the line's two ends.
        Assert.Equal((120d, 100d), (built.Bricks[0].High, built.Bricks[0].Low));
    }

    [Fact]
    public void A_line_is_dated_by_the_bar_that_turned_it_and_spans_the_bars_since_the_line_before()
    {
        var bars = Closes(100, 105, 112, 120, 113, 110, 125);
        var built = Build(bars);

        Assert.Equal(new[] { Start.AddHours(2), Start.AddHours(5), Start.AddHours(6) },
            built.Bricks.Select(b => b.Date));
        Assert.Equal(new[] { (0, 3), (4, 5), (6, 6) }, built.Spans.Select(s => (s.FirstBar, s.LastBar)));
        Assert.Equal(Start.AddHours(4), built.Spans[1].Start);
        Assert.Equal(Start.AddHours(6), built.Spans[1].End);
        // Volume is the window's: bars 4 and 5.
        Assert.Equal(bars[4].Volume + bars[5].Volume, built.Bricks[1].Volume);
    }

    [Fact]
    public void A_growing_line_keeps_its_date_and_the_line_count_moves_only_on_a_turn()
    {
        var before = Build(Closes(100, 120, 105));
        var after = Build(Closes(100, 120, 105, 95));

        Assert.Equal(before.Bricks.Count, after.Bricks.Count);
        Assert.Equal(before.Bricks[^1].Date, after.Bricks[^1].Date);
        Assert.Equal(before.Bricks[^1].Open, after.Bricks[^1].Open);
        Assert.Equal(95, after.Bricks[^1].Close);
    }

    [Fact]
    public void A_reversal_amount_of_zero_and_too_many_lines_are_refused()
    {
        Assert.Equal("Kagi needs a reversal amount above zero.", Build(Closes(Swing), 0).Refusal);
        Assert.Equal("Kagi would draw over 2 lines.", Build(Closes(Swing), 10, max: 2).Refusal);
    }

    // ---- Thick and thin ---------------------------------------------------------------------

    [Fact]
    public void A_line_turns_thin_below_the_last_waist_and_thick_above_the_last_shoulder()
    {
        var lines = Build(Closes(Swing)).Bricks;
        var looks = KagiLines.All(lines);

        Assert.Equal(new KagiLines.Look(true, true, double.NaN), looks[0]);
        Assert.Equal(new KagiLines.Look(true, false, 100), looks[1]);
        Assert.Equal(new KagiLines.Look(false, true, 120), looks[2]);
        Assert.Equal(new[] { "thick", "turns thin at 100.00", "turns thick at 120.00" },
            looks.Select(KagiLines.Words));
        Assert.Equal(looks[2], KagiLines.At(lines, 2));
    }

    [Fact]
    public void A_line_that_does_not_pass_the_line_before_keeps_the_thickness_it_started_with()
    {
        // Down 120→105 stays above the first line's start, 100: still thick.
        var lines = Build(Closes(100, 120, 105)).Bricks;
        Assert.Equal("thick", KagiLines.Words(KagiLines.At(lines, 1)));

        // A first line going down is thin.
        var down = Build(Closes(100, 85)).Bricks;
        Assert.Equal("thin", KagiLines.Words(KagiLines.At(down, 0)));
    }

    // ---- One settings record, four types ----------------------------------------------------

    private static readonly RenkoSettings Mixed = new(RenkoBoxMode.Fixed, 500, 21)
    {
        RangeMode = RenkoBoxMode.Fixed, RangeSize = 20, RangeAtrLength = 9, LineBreakLines = 3,
        KagiMode = RenkoBoxMode.Fixed, KagiSize = 10, KagiAtrLength = 7,
    };

    [Fact]
    public void Kagi_is_built_off_the_clock_and_drawn_as_one_part_with_its_own_fields()
    {
        Assert.True(PriceChartStyle.Kagi.IsBrickStyle());
        Assert.True(PriceChartStyle.Kagi.IsBlockStyle());
        Assert.Equal("Kagi", BuiltCharts.Name(PriceChartStyle.Kagi));
        Assert.Equal("Kagi line", BuiltCharts.Noun(PriceChartStyle.Kagi));
        Assert.Equal("Kagi lines", BuiltCharts.Noun(PriceChartStyle.Kagi, 2));

        var kagi = BuiltCharts.Merge(PriceChartStyle.Kagi, Mixed,
            new RenkoSettings(KagiMode: RenkoBoxMode.Atr, KagiAtrLength: 30));
        Assert.Equal(Mixed with { KagiMode = RenkoBoxMode.Atr, KagiSize = 0, KagiAtrLength = 30 }, kagi);

        Assert.True(BuiltCharts.SameFor(PriceChartStyle.Kagi, Mixed, Mixed with { BoxSize = 1 }));
        Assert.False(BuiltCharts.SameFor(PriceChartStyle.Kagi, Mixed, Mixed with { KagiSize = 11 }));
        Assert.False(BuiltCharts.SameFor(PriceChartStyle.Renko, Mixed, Mixed with { BoxSize = 1 }));

        Assert.True(BuiltCharts.TryResolveBox(PriceChartStyle.Kagi, Closes(Swing), Mixed, out double size, out _));
        Assert.Equal(10, size);
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
    public void Choosing_Kagi_freezes_its_own_reversal_amount_and_draws_the_lines()
    {
        var initial = WorkspaceState.Initial with
        {
            Identity = Id,
            Data = new TimeSeriesBuffer<Ohlcv>(Closes(Swing)),
            Renko = Mixed,
            CurrentDataIndex = 5,
            ViewportLength = 30,
            InitStatus = InitializationStatus.Ready,
        };

        var kagi = Reduce(initial,
            new SetChartStyleAction(PriceChartStyle.Renko, false),
            new SetChartStyleAction(PriceChartStyle.Kagi, false));

        Assert.Equal(PriceChartStyle.Kagi, kagi.ChartStyle);
        Assert.Equal(10, kagi.BrickBoxSize);   // the Kagi amount, not Renko's 500
        Assert.Equal(3, kagi.Data.Count);
        Assert.Equal(6, kagi.TimeBars!.Count);
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

    private static WorkspaceState KagiState(double[]? closes = null)
    {
        var bars = Closes(closes ?? Swing);
        var settings = new RenkoSettings(KagiMode: RenkoBoxMode.Fixed, KagiSize: 10);
        Assert.True(BuiltCharts.TryResolveBox(PriceChartStyle.Kagi, bars, settings, out double box, out _));
        var built = BuiltCharts.Build(PriceChartStyle.Kagi, bars, settings, box, Hour);
        var state = WorkspaceState.Initial with
        {
            Identity = Id,
            Data = built.Bricks,
            TimeBars = new TimeSeriesBuffer<Ohlcv>(bars),
            BrickSpans = built.Spans,
            BrickBoxSize = box,
            Renko = settings,
            ChartStyle = PriceChartStyle.Kagi,
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
    public void The_price_series_is_named_Kagi_with_one_Kagi_line_part()
    {
        var series = KagiState().ActiveSeries[0];

        Assert.Equal("Kagi", series.Config.Name);
        var part = Assert.Single(series.Components);
        Assert.Equal(PriceSeriesShape.KagiPart, part.Name);
        Assert.Equal("Kagi line", part.DisplayName);
        Assert.Equal(ComponentDisplayType.Brick, part.DisplayType);
    }

    [Fact]
    public void Arrowing_onto_a_line_says_its_thickness_as_drawn()
    {
        var state = KagiState();

        Assert.Contains("Kagi line. Up, turns thick at 120.00. 95.00 to 125.00.", Speak(state));
        Assert.Contains("Kagi line. Down, turns thin at 100.00. 120.00 to 95.00.",
            Speak(state with { CurrentDataIndex = 1 }));
        Assert.Contains("Kagi line. Up, thick. 100.00 to 120.00.", Speak(state with { CurrentDataIndex = 0 }));
    }

    [Fact]
    public void The_brick_summary_carries_the_thickness_only_on_Kagi()
    {
        var state = KagiState();

        Assert.Equal("Up Kagi line, turns thick at 120.00, 95.00 to 125.00",
            BrickChartSpeech.BrickSummary(state.Data[2], state.BrickSpans[2], PriceChartStyle.Kagi, state.Data, 2));
        Assert.Equal("Up brick, 95.00 to 125.00",
            BrickChartSpeech.BrickSummary(state.Data[2], state.BrickSpans[2], PriceChartStyle.Renko, state.Data, 2));
    }

    [Fact]
    public void The_change_sentence_names_the_reversal_amount()
    {
        Assert.Equal("Kagi chart, fixed reversal amount 10.00.", BrickChartSpeech.ChangeSentence(KagiState()));
        var atr = KagiState() with { Renko = new RenkoSettings(KagiMode: RenkoBoxMode.Atr, KagiAtrLength: 9) };
        Assert.Equal("Kagi chart, reversal amount 10.00 from ATR 9.", BrickChartSpeech.ChangeSentence(atr));
    }

    // ---- Live: a new line, and a line turning as it grows ------------------------------------

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
    public void A_new_Kagi_line_is_spoken_with_its_thickness()
    {
        var state = KagiState();
        var (coordinator, store, speech, bus) = Coordinator();
        using (coordinator)
        {
            store.EmitState(state);
            speech.Utterances.Clear();

            bus.Publish(new NewBarEvent(state.Data[^2], state.Data[^1]));

            Assert.Equal("New up Kagi line, turns thick at 120.00, 95.00 to 125.00.", speech.Utterances[^1].Text);
        }
    }

    [Fact]
    public void A_growing_line_says_once_when_it_turns_thick()
    {
        var state = KagiState();                       // the newest line has reached 125
        var earlier = state.Data[^1] with { Close = 115, High = 115 };  // the same line at 115
        var (coordinator, store, speech, bus) = Coordinator();
        using (coordinator)
        {
            store.EmitState(state);
            speech.Utterances.Clear();

            bus.Publish(new IntraBarUpdateEvent(state.Data[^1], earlier));
            bus.Publish(new IntraBarUpdateEvent(state.Data[^1], earlier));   // wavered back and through again

            Assert.Single(speech.Utterances, u => u.Text == "Kagi turns thick at 120.00.");
            Assert.DoesNotContain(speech.Utterances, u => u.Text.Contains("pattern", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void A_turn_is_news_only_when_this_update_passed_it_on_the_same_line()
    {
        var lines = KagiState().Data;
        var now = lines[^1];

        Assert.Equal("Kagi turns thick at 120.00.", BrickChartSpeech.KagiTurnNews(lines, now with { Close = 118 }));
        Assert.Null(BrickChartSpeech.KagiTurnNews(lines, now with { Close = 121 }));               // already past
        Assert.Null(BrickChartSpeech.KagiTurnNews(lines, now with { Close = 118, Open = 90 }));   // another line
        Assert.Null(BrickChartSpeech.KagiTurnNews(lines, null));
        // A line that has not turned has nothing to say.
        var first = KagiState(new double[] { 100, 120, 105 }).Data;
        Assert.Null(BrickChartSpeech.KagiTurnNews(first, first[^1] with { Close = 108 }));
    }

    // ---- The browser-closed monitor --------------------------------------------------------

    [Fact]
    public void A_background_Kagi_tab_hears_a_turn_but_not_a_line_growing()
    {
        // The last entry is the bar that has just opened; the one before it closed.
        var turned = BrickNews.CompletedAtClose(Closes(100, 120, 105, 999), 10, Id, PriceChartStyle.Kagi);
        var grew = BrickNews.CompletedAtClose(Closes(100, 120, 105, 95, 999), 10, Id, PriceChartStyle.Kagi);

        var line = Assert.Single(turned);
        Assert.Equal((120d, 105d), (line.Brick.Open, line.Brick.Close));
        Assert.False(line.Span.Up);
        Assert.Empty(grew);
        Assert.Equal("BTC/USD 1h: new down Kagi line, 120.00 to 105.00.",
            BrickNews.Sentence(Id, turned, style: PriceChartStyle.Kagi));
    }

    // ---- New-bar alerts follow a line as it grows ------------------------------------------

    private static WorkspaceState Lines(params (int Hour, double Open, double Close)[] lines)
    {
        var drawn = lines.Select(l => new Ohlcv(Start.AddHours(l.Hour), l.Open,
            Math.Max(l.Open, l.Close), Math.Min(l.Open, l.Close), l.Close, 10)).ToArray();
        var spans = drawn.Select((b, i) => new BrickSpan(b.Date, b.Date.AddHours(1), i, i, b.Close > b.Open))
            .ToImmutableArray();
        return WorkspaceState.Initial with
        {
            Identity = Id,
            SymbolDisplayName = "BTC/USD",
            ChartStyle = PriceChartStyle.Kagi,
            Data = new TimeSeriesBuffer<Ohlcv>(drawn),
            TimeBars = new TimeSeriesBuffer<Ohlcv>(Closes(100, 120, 105)),
            BrickSpans = spans,
            BrickBoxSize = 10,
            CurrentDataIndex = drawn.Length - 1,
            InitStatus = InitializationStatus.Ready,
        };
    }

    [Fact]
    public void A_new_up_line_alert_still_fires_after_the_line_before_it_grew()
    {
        var ev = new AlertEvaluator(Substitute.For<ISdkCandlePatternAnalyzer>(), Substitute.For<IIndicatorContextAnalyzer>());
        var alert = new AlertDefinition
        {
            Id = "k", Name = "Kagi", Target = AlertTarget.Candle, Condition = AlertCondition.NewUpBar,
            Delivery = AlertDelivery.Speech,
        };
        List<AlertFired> Eval(WorkspaceState s) =>
            ev.EvaluateAlerts(new[] { alert }, s, s.Data[^1], s.Data[^2], new Dictionary<string, double>()).ToList();

        Assert.Empty(Eval(Lines((1, 100, 120), (2, 120, 105))));                // seeds
        Assert.Empty(Eval(Lines((1, 100, 120), (2, 120, 95))));                 // the same line, grown
        var fired = Assert.Single(Eval(Lines((1, 100, 120), (2, 120, 90), (4, 90, 102))));

        Assert.Equal("Kagi: new up Kagi line, 90 to 102.", fired.SpeechText);
    }

    // ---- The Go bar and the command ----------------------------------------------------------

    [Theory]
    [InlineData("500", RenkoBoxMode.Fixed, 500, 14)]
    [InlineData("ATR 20", RenkoBoxMode.Atr, 0, 20)]
    [InlineData("ATR", RenkoBoxMode.Atr, 0, 14)]
    public void KAGI_takes_a_reversal_amount_or_ATR_and_a_length(string argument, RenkoBoxMode mode, double size, int atr)
    {
        Assert.True(GoBarParser.TryParseKagi(argument, out var settings));
        Assert.Equal((mode, size, atr), (settings.KagiMode, settings.KagiSize, settings.KagiAtrLength));
        Assert.Equal(new RenkoSettings().BoxSize, settings.BoxSize);   // Renko's own fields untouched
    }

    [Fact]
    public void A_bad_Kagi_argument_says_Kagis_own_sentence()
    {
        Assert.Equal(BrickChartSpeech.KagiBadArgument, GoBarParser.Parse("KAGI banana").Refusal);
    }

    [Theory]
    [InlineData("KAGI")]
    [InlineData("kagi chart")]
    public void Kagi_is_reachable_by_its_own_words(string typed)
    {
        var top = GoBarMatcher.Match(typed, GoBarRegistry.All).FirstOrDefault();

        Assert.Equal(SystemCommand.ShowKagiChart, top?.Entry.Command);
    }

    [Fact]
    public void KAGI_500_makes_a_Kagi_action_with_that_amount()
    {
        var bus = new EventBus();
        using var stack = new ModalStack(bus);
        var service = new GoBarService(Substitute.For<ICommandDispatcher>(), Substitute.For<IShortcutManager>(), bus, stack);

        var option = service.Suggest("KAGI 500").Options[0];
        var action = Assert.IsType<GoBarRenkoAction>(option.Action);

        Assert.Equal(PriceChartStyle.Kagi, action.Style);
        Assert.Equal(500, action.Settings.KagiSize);
        Assert.Equal("Kagi chart, fixed reversal amount 500.00", option.Name);
    }

    [Fact]
    public void The_same_Kagi_again_says_already_and_changes_nothing()
    {
        var state = KagiState();
        var store = new MockWorkspaceStore();
        store.EmitState(state);
        var bus = new SpyEventBus();

        Assert.False(RenkoChoice.TryApply(store, bus, PriceChartStyle.Kagi, state.Renko));

        Assert.Contains(bus.Log.OfType<FeedbackRequestEvent>(), e => e.Message == "Already a Kagi chart.");
        Assert.Empty(store.DispatchedActions);
    }

    [Fact]
    public void ShowKagiChart_shows_Kagi()
    {
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            Identity = Id, Data = new TimeSeriesBuffer<Ohlcv>(Closes(Swing)), Renko = Mixed,
            CurrentDataIndex = 5, InitStatus = InitializationStatus.Ready,
        });
        var dispatcher = new CommandDispatcher(
            bus, Substitute.For<INavigationEngine>(), store,
            Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));

        bus.Publish(new ChartFocusEvent());
        dispatcher.Dispatch(SystemCommand.ShowKagiChart);

        Assert.Equal(PriceChartStyle.Kagi,
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

    /// <summary>How many pixels of the colour are in the row across x - 4 .. x + 4.</summary>
    private static int WidthAt(SKBitmap bmp, int x, int y, SKColor colour)
    {
        int n = 0;
        for (int dx = -4; dx <= 4; dx++)
            if (CloseTo(bmp.GetPixel(Math.Clamp(x + dx, 0, W - 1), y), colour)) n++;
        return n;
    }

    [Fact]
    public void Each_line_is_drawn_thick_or_thin_and_split_where_it_turns()
    {
        var state = KagiState();
        var lines = state.Data;
        var theme = Theme();
        var series = state.ActiveSeries[0];

        using var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Black);
            var r = SKRect.Create(0, 0, W, H);
            var ctx = new RenderContext(canvas, r, lines, 0, lines.Count, 80, 130, false,
                r.Width / lines.Count, 2f, "Main", 0, theme)
            { ChartStyle = PriceChartStyle.Kagi, KagiLooks = KagiLines.All(lines) };
            StandardRenderers.RenderKagi(ctx, series);
        }

        int Y(double price) => (int)Math.Round(H - (price - 80) / 50 * H);
        var thick = theme.CandleBullishBody;
        var thin = theme.CandleBearishBody;
        int x1 = 150, x2 = 250;   // lines 1 and 2, three lines across 300

        // Density 2: thick is 6 pixels across, thin 2.5.
        // Line 1 falls 120 → 95: thick down to 100, thin below.
        Assert.True(WidthAt(bmp, x1, Y(110), thick) >= 5);
        Assert.Equal(0, WidthAt(bmp, x1, Y(110), thin));
        Assert.True(WidthAt(bmp, x1, Y(97), thin) is >= 2 and <= 3);
        Assert.Equal(0, WidthAt(bmp, x1, Y(97), thick));
        // Line 2 rises 95 → 125: thin up to 120, thick above.
        Assert.True(WidthAt(bmp, x2, Y(110), thin) is >= 2 and <= 3);
        Assert.Equal(0, WidthAt(bmp, x2, Y(110), thick));
        Assert.True(WidthAt(bmp, x2, Y(123), thick) >= 5);
        // The shoulder from line 0 to line 1 at 120 is thick, the waist from 1 to 2 at 95 thin.
        Assert.True(CloseTo(bmp.GetPixel(100, Y(120)), thick));
        Assert.True(CloseTo(bmp.GetPixel(200, Y(95)), thin));
    }
}
