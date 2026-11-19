using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// Saying the price-scale change: one Interface sentence from the coordinator that already
/// speaks the log toggle, the "already" answers, the unusable-base refusal, and the orientation
/// sentence that carries the base when the user pans (panning itself stays silent).
/// </summary>
public sealed class PriceScaleAnnouncementTests
{
    private static Ohlcv Bar(int i, double close) =>
        new(new DateTime(2026, 11, 3, 14, 0, 0, DateTimeKind.Utc).AddHours(i),
            close, close + 4, close - 4, close, 1000);

    private static WorkspaceState State(PriceScaleMode mode = PriceScaleMode.Regular,
        bool log = false, double firstClose = 100) =>
        WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "Bitstamp", "BTC/USD", "1h"),
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, 5).Select(i => Bar(i, firstClose + i))),
            ActiveSeries = ImmutableList.Create(new ChartSeries(
                new SeriesConfig { Id = "candles", Name = "Candles", Pane = "Main" },
                new SeriesDataBuffer { SeriesId = "candles" })),
            ViewportStartIndex = 0,
            ViewportLength = 5,
            ScaleMode = mode,
            IsLogScale = log,
        };

    private static (AccessibilityFeedbackCoordinator Coordinator, MockWorkspaceStore Store, CounterSpeechManager Speech) Build()
    {
        var bus = new SpyEventBus();
        var sonify = new MockNavigationSonifier();
        var speech = new CounterSpeechManager();
        var store = new MockWorkspaceStore();
        var formatter = new SpeechFormatter();
        var speechRouter = new SpeechFeedbackRouter(speech, formatter, store);
        var audioRouter = new AudioFeedbackRouter(sonify, new MockEarconService());
        var navMgr = new NavigationFeedbackManager(speechRouter, formatter);
        var coordinator = new AccessibilityFeedbackCoordinator(
            store, navMgr, speechRouter, audioRouter, formatter, bus, new MockEarconService(),
            new SdkCandlePatternAnalyzer(),
            new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())), new ChartPatternFocus(),
            new MockAutoNarrationService());
        return (coordinator, store, speech);
    }

    private static string Announce(WorkspaceState from, WorkspaceState to)
    {
        var (coordinator, store, speech) = Build();
        using (coordinator)
        {
            store.EmitState(from);
            speech.Utterances.Clear();

            store.EmitState(to);

            Assert.NotEmpty(speech.Utterances);
            return speech.Utterances[^1].Text;
        }
    }

    [Fact]
    public void Choosing_percent_says_where_zero_is()
    {
        string said = Announce(State(), State(PriceScaleMode.Percent));

        Assert.Equal("Percent scale. Zero is 100.00, the first close on screen.", said);
    }

    [Fact]
    public void Choosing_indexed_says_where_100_is()
    {
        string said = Announce(State(), State(PriceScaleMode.IndexedTo100));

        Assert.Equal("Indexed to 100. 100 is 100.00, the first close on screen.", said);
    }

    [Fact]
    public void Choosing_percent_from_a_log_chart_says_percent_not_regular()
    {
        string said = Announce(State(log: true), State(PriceScaleMode.Percent));

        Assert.Equal("Percent scale. Zero is 100.00, the first close on screen.", said);
    }

    [Fact]
    public void Choosing_regular_replaces_linear_scale()
    {
        Assert.Equal("Regular scale.", Announce(State(PriceScaleMode.Percent), State()));
    }

    [Fact]
    public void Turning_log_on_from_percent_says_only_log_scale()
    {
        Assert.Equal("Log scale.", Announce(State(PriceScaleMode.Percent), State(log: true)));
    }

    [Fact]
    public void Turning_log_off_says_regular_scale()
    {
        Assert.Equal("Regular scale.", Announce(State(log: true), State()));
    }

    [Fact]
    public void An_unusable_base_refuses_and_says_why()
    {
        string said = Announce(State(), State(PriceScaleMode.Percent, firstClose: 0));

        Assert.Equal("Percent scale needs a positive first close; showing the regular scale.", said);
    }

    [Fact]
    public void An_unusable_indexed_base_says_it_its_own_way()
    {
        string said = Announce(State(), State(PriceScaleMode.IndexedTo100, firstClose: 0));

        Assert.Equal("Indexed to 100 needs a positive first close; showing the regular scale.", said);
    }

    [Fact]
    public void Panning_changes_the_base_but_says_nothing()
    {
        var (coordinator, store, speech) = Build();
        using (coordinator)
        {
            store.EmitState(State(PriceScaleMode.Percent));
            speech.Utterances.Clear();

            store.EmitState(store.State with { ViewportStartIndex = 3 });

            Assert.DoesNotContain(speech.Utterances,
                u => u.Text.Contains("scale", StringComparison.OrdinalIgnoreCase));
        }
    }

    // ── Orientation ───────────────────────────────────────────────────────

    private static WorkspaceState PricesState(PriceScaleMode mode)
    {
        var start = new DateTime(2026, 11, 3, 14, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>
        {
            new(start, 93_000.5, 93_100, 92_900, 93_000.5, 1000),
            new(start.AddHours(1), 93_500, 93_600, 93_400, 93_500, 1000),
        };
        var config = new SeriesConfig { Id = "candles", Name = "Candles", Pane = "Main" };
        return WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "Bitstamp", "BTC/USD", "1h"),
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(new ChartSeries(config, new SeriesDataBuffer { SeriesId = "candles" })),
            ViewportStartIndex = 0,
            ViewportLength = 2,
            ViewportRange = (92_000, 94_000),
            ScaleMode = mode,
        };
    }

    [Fact]
    public void The_describer_says_the_percent_base_and_which_bar_it_came_from()
    {
        var state = PricesState(PriceScaleMode.Percent);
        string when = SpeechTimeFormatter.FormatBar(state.Data![0].Date, 3600, "d MMMM HH:mm");

        string said = ChartLayoutDescriber.Describe(state, "BTC/USD", "1h");

        Assert.Contains($"percent scale", said);
        Assert.Contains($"Percent scale from the first close on screen, 93000.50 on {when}.", said);
    }

    [Fact]
    public void The_describer_says_the_indexed_base()
    {
        var state = PricesState(PriceScaleMode.IndexedTo100);
        string when = SpeechTimeFormatter.FormatBar(state.Data![0].Date, 3600, "d MMMM HH:mm");

        string said = ChartLayoutDescriber.Describe(state, "BTC/USD", "1h");

        Assert.Contains($"Indexed to 100 from the first close on screen, 93000.50 on {when}.", said);
    }

    [Fact]
    public void The_pane_describer_carries_the_base_too()
    {
        var state = PricesState(PriceScaleMode.Percent);

        string said = ChartLayoutDescriber.DescribePane(state, "1h");

        Assert.Contains("Percent scale from the first close on screen,", said);
    }

    [Fact]
    public void A_regular_chart_has_no_scale_sentence()
    {
        string said = ChartLayoutDescriber.Describe(PricesState(PriceScaleMode.Regular), "BTC/USD", "1h");

        Assert.DoesNotContain("Percent scale from", said);
        Assert.DoesNotContain("Indexed to 100 from", said);
    }
}
