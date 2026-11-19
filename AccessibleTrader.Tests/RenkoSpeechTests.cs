using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Charts;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// What a Renko chart says: a brick's direction and two edges, then the REAL window it formed
/// in. No candle shape, no pattern and no wick percentages — a brick has none.
/// </summary>
public sealed class RenkoSpeechTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ChartIdentity Id = new("Spot", "Bitstamp", "BTC/USD", "1h");

    private static (WorkspaceState State, BrickSpan Last, Ohlcv LastBrick) BrickState()
    {
        double[] closes = { 100, 110, 120, 125 };
        var bars = closes.Select((c, i) => new Ohlcv(Start.AddHours(i), c - 1, c + 1, c - 2, c, 100)).ToList();
        var time = new TimeSeriesBuffer<Ohlcv>(bars);
        var built = BrickChartBuilder.Build(bars, 10, TimeSpan.FromHours(1));
        var config = new SeriesConfig { Id = "candles", IndicatorCode = "candles", Name = "Price" };
        config.Components.Add(new ComponentConfig
        {
            Name = "body", DisplayName = "Body", IsVisible = true,
            DisplayType = ComponentDisplayType.Candle, Role = ComponentRole.Body, DataMapping = "close",
        });

        var state = WorkspaceState.Initial with
        {
            Identity = Id,
            Data = built.Bricks,
            TimeBars = time,
            BrickSpans = built.Spans,
            BrickBoxSize = 10,
            Renko = new RenkoSettings(RenkoBoxMode.Fixed, 10),
            ChartStyle = PriceChartStyle.Renko,
            ActiveSeries = ImmutableList.Create(new ChartSeries(config, new SeriesDataBuffer { SeriesId = "candles" })),
            FocusedSeriesId = "candles",
            CurrentDataIndex = built.Bricks.Count - 1,
            ViewportStartIndex = 0,
            ViewportLength = 5,
            LastInteractionContext = InteractionContext.Series,
            SpeakTimestamps = true,
            TimestampReadLocation = "Along X Axis",
            ReadColumnHeaders = false,
            SpeechOrder = "ValueOnly",
            InitStatus = InitializationStatus.Ready,
        };

        return (state, built.Spans[^1], built.Bricks[^1]);
    }

    private static string Speak(WorkspaceState state)
    {
        var spy = new SpySpeechRouter();
        new NavigationFeedbackManager(spy, new SpeechFormatter()) { IsSpeechEnabled = true }
            .HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");
        Assert.NotEmpty(spy.SpokenTexts);
        return string.Join(" ", spy.SpokenTexts);
    }

    private static string Window(BrickSpan span)
        => SpeechTimeFormatter.FormatBrickRange(span.Start, span.End, 3600);

    [Fact]
    public void An_up_brick_reads_its_two_edges_and_its_real_window()
    {
        var (state, span, brick) = BrickState();

        string spoken = Speak(state);

        Assert.Contains("Up brick", spoken);
        Assert.Contains(SpeechPriceFormatter.FormatPrice(brick.Open), spoken);
        Assert.Contains(SpeechPriceFormatter.FormatPrice(brick.Close), spoken);
        Assert.Contains(Window(span), spoken);
        Assert.DoesNotContain("Body", spoken);
        Assert.DoesNotContain("wick", spoken, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_Renko_readout_never_says_only_the_bricks_completion_Date()
    {
        var (state, span, brick) = BrickState();

        string spoken = Speak(state);
        string onlyDate = SpeechTimeFormatter.FormatBarClock(brick.Date, 3600);

        // The brick's Date is only its COMPLETION bar's open time. The readout must name the
        // window's two ends, which is a longer sentence than that one clock reading.
        Assert.Contains(SpeechTimeFormatter.FormatBar(span.Start, 3600, SpeechTimeFormatter.BrickDateTimeFormat), spoken);
        Assert.Contains(SpeechTimeFormatter.FormatBar(span.End, 3600, SpeechTimeFormatter.BrickDateTimeFormat), spoken);
        Assert.NotEqual(onlyDate, Window(span));
    }

    [Fact]
    public void The_change_sentence_names_the_frozen_box_and_its_source()
    {
        var (state, _, _) = BrickState();

        Assert.Equal("Renko chart, fixed box 10.00.", BrickChartSpeech.ChangeSentence(state));

        var atr = state with { Renko = new RenkoSettings(), BrickBoxSize = 512.3 };
        Assert.Equal("Renko chart, box 512.30 from ATR 14.", BrickChartSpeech.ChangeSentence(atr));
    }

    [Fact]
    public void The_orientation_sentence_says_Renko_and_the_box()
    {
        var (state, _, _) = BrickState();

        string described = ChartLayoutDescriber.Describe(state);

        Assert.Contains("Renko chart, fixed box 10.00.", described);
        Assert.Contains("bricks in view", described);
    }

    private static (CommandDispatcher Dispatcher, MockWorkspaceStore Store, SpyEventBus Bus) Dispatcher(
        WorkspaceState state)
    {
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        store.EmitState(state);
        var dispatcher = new CommandDispatcher(
            bus, Substitute.For<INavigationEngine>(), store,
            Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));
        return (dispatcher, store, bus);
    }

    private static WorkspaceState BarsState(int count, Func<int, double> close)
        => WorkspaceState.Initial with
        {
            Identity = Id,
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, count)
                .Select(i => new Ohlcv(Start.AddHours(i), close(i) - 1, close(i) + 1, close(i) - 1, close(i), 10))),
            CurrentDataIndex = count - 1,
            ViewportLength = 30,
            InitStatus = InitializationStatus.Ready,
        };

    [Fact]
    public void Too_little_history_speaks_the_ATR_refusal_and_changes_nothing()
    {
        var (dispatcher, store, bus) = Dispatcher(BarsState(9, i => 100 + i));

        dispatcher.ShowRenko(new RenkoSettings());

        Assert.Contains(bus.Log.OfType<FeedbackRequestEvent>(),
            e => e.Message == "Renko needs at least 16 bars for ATR 14; this chart has 9.");
        Assert.Empty(store.DispatchedActions.OfType<SetChartStyleAction>());
        Assert.Equal(PriceChartStyle.Candles, store.State.ChartStyle);
    }

    [Fact]
    public void A_non_positive_box_and_a_flat_chart_each_speak_their_own_refusal()
    {
        var (zero, _, zeroBus) = Dispatcher(BarsState(30, i => 100 + i));
        zero.ShowRenko(new RenkoSettings(RenkoBoxMode.Fixed, 0));
        Assert.Contains(zeroBus.Log.OfType<FeedbackRequestEvent>(),
            e => e.Message == "A Renko box must be a positive price.");

        var (flat, _, flatBus) = Dispatcher(BarsState(30, _ => 100));
        flat.ShowRenko(new RenkoSettings(RenkoBoxMode.Fixed, 10));
        Assert.Contains(flatBus.Log.OfType<FeedbackRequestEvent>(),
            e => e.Message != null && e.Message.StartsWith("No Renko bricks: price has not moved one box of "));
    }

    [Fact]
    public void A_box_that_would_draw_over_the_cap_says_so()
    {
        var bars = Enumerable.Range(0, 30)
            .Select(i => new Ohlcv(Start.AddHours(i), 100, 100, 100, i == 29 ? 300_000 : 100, 10))
            .ToList();
        var state = WorkspaceState.Initial with
        {
            Identity = Id,
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = bars.Count - 1,
            ViewportLength = 30,
            InitStatus = InitializationStatus.Ready,
        };
        var (dispatcher, _, bus) = Dispatcher(state);

        dispatcher.ShowRenko(new RenkoSettings(RenkoBoxMode.Fixed, 0.01));

        Assert.Contains(bus.Log.OfType<FeedbackRequestEvent>(),
            e => e.Message != null && e.Message.StartsWith("A box of 0.0100 would draw over 20,000 bricks."));
    }

    [Fact]
    public void Choosing_the_same_Renko_and_settings_again_says_already()
    {
        var (state, _, _) = BrickState();
        var store = new MockWorkspaceStore();
        store.EmitState(state);
        var bus = new SpyEventBus();

        Assert.False(RenkoChoice.TryApply(store, bus, state.Renko));

        Assert.Contains(bus.Log.OfType<FeedbackRequestEvent>(),
            e => e.Message == "Already a Renko chart.");
        Assert.Empty(store.DispatchedActions);
    }

    [Fact]
    public void Brick_settings_on_a_non_brick_chart_says_choose_RENKO_first()
    {
        var (dispatcher, _, bus) = Dispatcher(BarsState(30, i => 100 + i));

        dispatcher.DispatchFromGoBar(SystemCommand.ShowBrickSettings);

        Assert.Contains(bus.Log.OfType<FeedbackRequestEvent>(),
            e => e.Message == "Brick settings are for Renko charts. Choose RENKO first.");
        Assert.DoesNotContain(bus.Log.OfType<OpenChartTypeSettingsEvent>(), _ => true);
    }

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
    public void A_new_brick_is_spoken_as_a_brick_not_a_new_time_bar()
    {
        var (state, _, brick) = BrickState();
        var (coordinator, store, speech, bus) = Coordinator();
        using (coordinator)
        {
            store.EmitState(state);
            speech.Utterances.Clear();

            bus.Publish(new NewBarEvent(state.Data[^2], brick));

            Assert.NotEmpty(speech.Utterances);
            Assert.Equal(
                $"New up brick, {SpeechPriceFormatter.FormatPrice(brick.Open)} to {SpeechPriceFormatter.FormatPrice(brick.Close)}.",
                speech.Utterances[^1].Text);
        }
    }

    [Fact]
    public void A_settings_change_on_a_Renko_tab_speaks_the_new_box()
    {
        var (state, _, _) = BrickState();
        var (coordinator, store, speech, bus) = Coordinator();
        using (coordinator)
        {
            store.EmitState(state);
            speech.Utterances.Clear();

            // The reducer change is what the announcer reads: new settings and the box they froze.
            store.EmitState(state with
            {
                Renko = new RenkoSettings(RenkoBoxMode.Fixed, 20),
                BrickBoxSize = 20,
            });

            Assert.Contains(speech.Utterances, u => u.Text == "Renko chart, fixed box 20.00.");
        }
    }
}
