using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// What the chart's navigation keys DO, through the real objects: <see cref="NavigationEngine"/>,
/// <see cref="ViewportManager"/> and <see cref="SeriesNavigationRegistry"/> over the real
/// <see cref="WorkspaceStore"/> and its reducers, with the real
/// <see cref="AccessibilityFeedbackCoordinator"/> turning their feedback into speech and earcons.
///
/// <para>
/// A2q (2026-10-01). Before this file none of those three classes had a direct test: every one
/// of the thirteen test files that touched navigation substituted them, and only one browser
/// test reached the engine at all. The campaign's survivors here were each a key a blind user
/// presses hundreds of times a session — the edge of the chart, walking left into history,
/// Page Down at the bottom, Delete on the candles, panning, Up/Down on a volume profile.
/// </para>
/// </summary>
public sealed class ChartKeyboardNavigationTests
{
    /// <summary>Every earcon the coordinator asked for, by name.</summary>
    private sealed class RecordingEarcons : IEarconService
    {
        public List<string> Played { get; } = new();
        public void PlayError(ErrorSeverity severity) => Played.Add("Error");
        public void PlaySuccess() => Played.Add("Success");
        public void PlayRetry() => Played.Add("Retry");
        public void PlayConnectionState(ConnectionState state) => Played.Add("Connection");
        public void PlayBoundary() => Played.Add("Boundary");
        public void PlayInfo() => Played.Add("Info");
        public void PlayNewBar() => Played.Add("NewBar");
        public void PlayAlert(bool breakThroughMutes = false) => Played.Add("Alert");
        public void PlaySetupBell(OrderSide side, bool reconfirmation) => Played.Add("SetupBell");
        public void PlaySetupArmed(OrderSide side) => Played.Add("SetupArmed");
        public void PlaySetupEntryReached(OrderSide side) => Played.Add("SetupEntry");
        public void PlayOrderFill(OrderSide side) => Played.Add("OrderFill");
        public void PlayStopHit() => Played.Add("StopHit");
        public void PlayTakeProfitHit() => Played.Add("TakeProfit");
    }

    private sealed class Harness
    {
        public SpyEventBus Bus { get; } = new();
        public WorkspaceStore Store { get; }
        public RecordingEarcons Earcons { get; } = new();
        public List<string> Spoken { get; } = new();
        private readonly MockMainThreadService _main = new();
        private readonly NavigationEngine _engine;

        public Harness(int bars = 300, Func<int, Ohlcv>? bar = null)
        {
            Store = new WorkspaceStore(Bus, new ViewportRangeCalculator(),
                new ViewportNavigationService(), new VolumeStateService());
            var data = Enumerable.Range(0, bars)
                .Select(bar ?? (i => new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
                    100 + i % 7, 102 + i % 7, 98 + i % 7, 101 + i % 7, 1000)))
                .ToList();
            Store.Dispatch(new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(data), IsInitialLoad: true));
            // The primary series, as MarketOrchestrator adds it on a load.
            AddSeries("candles", "Candles", "Main", "Close");

            _engine = new NavigationEngine(Bus, Store, new ViewportManager(Bus, Store, _main), _main,
                new SeriesNavigationRegistry(new PointNavigationStrategy(), new BinnedNavigationStrategy()));

            var speech = new CounterSpeechManager { OnSpeak = t => Spoken.Add(t) };
            var formatter = new SpeechFormatter();
            var router = new SpeechFeedbackRouter(speech, formatter, Store);
            _ = new AccessibilityFeedbackCoordinator(
                Store, new NavigationFeedbackManager(router, formatter), router,
                new AudioFeedbackRouter(new MockNavigationSonifier(), Earcons),
                formatter, Bus, Earcons, new SdkCandlePatternAnalyzer(),
                new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
                new ChartPatternFocus(), new MockAutoNarrationService());
        }

        /// <summary>One key press, run to completion the way the UI thread would.</summary>
        public void Press(string command)
        {
            Spoken.Clear();
            Earcons.Played.Clear();
            Bus.Log.Clear();
            _engine.ProcessNavigation(command);
            for (int i = 0; i < 3; i++) _main.RunAll();   // the viewport manager posts again
        }

        public ChartSeries AddSeries(string id, string name, string pane, params string[] components)
        {
            var cfg = new SeriesConfig { Id = id, Name = name, FriendlyName = name, Pane = pane, IsVisible = true, Volume = 1f };
            var buf = new SeriesDataBuffer { SeriesId = id };
            int n = Store.State.Data.Count;
            foreach (var c in components)
            {
                cfg.Components.Add(new ComponentConfig
                {
                    Name = c, DisplayName = c, DisplayType = ComponentDisplayType.Line,
                    IsVisible = true, IsEnabled = true, Volume = 1f,
                });
                buf.ComponentData[c] = Enumerable.Range(0, n).Select(i => 50.0 + i % 5).ToArray();
            }
            var s = new ChartSeries(cfg, buf);
            Store.Dispatch(new AddSeriesAction(s));
            return s;
        }

        public bool HistoryWasRequested => Bus.Log.OfType<RequestHistoryEvent>().Any();
    }

    // ── The edge of the chart ────────────────────────────────────────────────

    [Fact]
    public void Right_on_the_last_bar_plays_the_boundary_earcon()
    {
        // "At data boundary: play earcon only." Without it the key does nothing at all, and a
        // user who cannot see the cursor cannot tell the end of the chart from a dead key.
        var h = new Harness();
        h.Press("NAV_END");
        int last = h.Store.State.CurrentDataIndex;
        Assert.Equal(h.Store.State.Data.Count - 1, last);

        h.Press("NAV_RIGHT");

        Assert.Equal(last, h.Store.State.CurrentDataIndex);
        Assert.Contains("Boundary", h.Earcons.Played);
    }

    [Fact]
    public void Right_from_a_bar_that_is_not_the_last_moves_without_the_boundary_earcon()
    {
        // The partner: an engine that played the boundary earcon on every press would pass the
        // test above.
        var h = new Harness();
        h.Press("NAV_END");
        h.Press("NAV_LEFT");
        int before = h.Store.State.CurrentDataIndex;

        h.Press("NAV_RIGHT");

        Assert.Equal(before + 1, h.Store.State.CurrentDataIndex);
        Assert.DoesNotContain("Boundary", h.Earcons.Played);
    }

    // ── Walking left into history ────────────────────────────────────────────

    [Fact]
    public void Walking_left_near_the_start_of_the_loaded_data_asks_for_older_history()
    {
        var h = new Harness();
        h.Store.Dispatch(new NavigateAction(10));

        h.Press("NAV_LEFT");

        Assert.True(h.HistoryWasRequested,
            "Left at bar 10 of 300 did not ask for older history: walking left reaches the start of "
          + "what was loaded and stops there for good");
    }

    /// <summary>A slow climb on a Renko chart with a box of 1: a brick every ten real bars, the
    /// way a point and figure column or a Renko brick on a daily chart covers weeks.</summary>
    private static Harness BuiltChart()
    {
        var h = new Harness(300, i => new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
            100 + i * 0.1, 100.05 + i * 0.1, 99.95 + i * 0.1, 100 + i * 0.1, 1000));
        h.Store.Dispatch(new SetRenkoSettingsAction(new RenkoSettings(Mode: RenkoBoxMode.Fixed, BoxSize: 1)));
        h.Store.Dispatch(new SetChartStyleAction(PriceChartStyle.Renko, false));
        Assert.InRange(h.Store.State.Data.Count, 20, 40);
        return h;
    }

    [Fact]
    public void On_a_built_chart_walking_left_asks_for_history_only_near_the_start_of_the_REAL_bars()
    {
        // Cody, 2026-11-20, on a daily point and figure chart: "it loaded all the chart history
        // ... and yet I'm only viewing 58 bars". "Near the start" was 50 DRAWN bars, which is
        // years of daily bars there, so nearly every Left fetched more until it was all in.
        var h = BuiltChart();
        h.Store.Dispatch(new NavigateAction(10));
        Assert.True(h.Store.State.RealBarIndex(10) >= 50, "brick 10 should start well past the 50th real bar");

        h.Press("NAV_LEFT");

        Assert.False(h.HistoryWasRequested,
            "brick 10 starts about 100 real bars in: older history is not near yet");
    }

    [Fact]
    public void On_a_built_chart_walking_left_from_the_first_bricks_still_asks_for_history()
    {
        var h = BuiltChart();
        h.Store.Dispatch(new NavigateAction(2));

        h.Press("NAV_LEFT");

        Assert.True(h.HistoryWasRequested, "the first bricks are the first real bars: older ones are wanted");
    }

    [Theory]
    [InlineData(12, false)]   // the view starts about 120 real bars in
    [InlineData(1, true)]     // the view starts in the first real bars
    public void On_a_built_chart_panning_left_asks_for_history_only_near_the_start_of_the_REAL_bars(int viewStart, bool asks)
    {
        var h = BuiltChart();
        h.Store.Dispatch(new ZoomAction(10));
        h.Store.Dispatch(new PanAction(viewStart - h.Store.State.ViewportStartIndex));
        Assert.Equal(viewStart, h.Store.State.ViewportStartIndex);

        h.Press("VIEW_PAN_LEFT");

        Assert.Equal(asks, h.HistoryWasRequested);
    }

    [Fact]
    public void Walking_right_near_the_start_does_not_ask_for_history()
    {
        // Backfill is for the direction the user is going. Fetching older bars on every Right
        // near the start is a provider request per keypress for data nobody is walking toward.
        var h = new Harness();
        h.Store.Dispatch(new NavigateAction(10));

        h.Press("NAV_RIGHT");

        Assert.False(h.HistoryWasRequested);
    }

    // ── Panning ──────────────────────────────────────────────────────────────

    [Fact]
    public void Pan_left_shows_earlier_bars_and_pan_right_shows_later_ones()
    {
        var h = new Harness();
        h.Store.Dispatch(new PanAction(-100));   // off the live edge, so both directions can move
        int start = h.Store.State.ViewportStartIndex;

        h.Press("VIEW_PAN_LEFT");
        int afterLeft = h.Store.State.ViewportStartIndex;
        h.Press("VIEW_PAN_RIGHT");
        h.Press("VIEW_PAN_RIGHT");
        int afterRight = h.Store.State.ViewportStartIndex;

        Assert.True(afterLeft < start, $"pan LEFT moved the window from bar {start} to bar {afterLeft}");
        Assert.True(afterRight > afterLeft, $"pan RIGHT moved the window from bar {afterLeft} to bar {afterRight}");
    }

    // ── Page Up / Page Down ──────────────────────────────────────────────────

    [Fact]
    public void Page_Down_on_the_bottom_series_stays_there()
    {
        // Settled for pane and strip navigation alike: wrapping silently teleports a user who
        // cannot see the jump. From the bottom of the chart, Page Down must not land on the
        // candles at the top.
        var h = new Harness();
        h.AddSeries("rsi", "RSI 14", "Pane_RSI", "RSI");
        h.AddSeries("macd", "MACD", "Pane_MACD", "MACD", "Signal", "Histogram");
        h.Store.Dispatch(new SelectSeriesAction("macd"));

        h.Press("NAV_SERIES_NEXT");

        Assert.Equal("macd", h.Store.State.FocusedSeriesId);
    }

    [Fact]
    public void Page_Down_moves_to_the_next_series_down_the_chart()
    {
        // The partner for the clamp: an engine that never moved would pass the test above.
        var h = new Harness();
        h.AddSeries("rsi", "RSI 14", "Pane_RSI", "RSI");
        h.AddSeries("macd", "MACD", "Pane_MACD", "MACD", "Signal", "Histogram");
        h.Store.Dispatch(new SelectSeriesAction("rsi"));

        h.Press("NAV_SERIES_NEXT");

        Assert.Equal("macd", h.Store.State.FocusedSeriesId);
    }

    [Fact]
    public void Moving_to_another_series_starts_Up_and_Down_at_its_first_component()
    {
        // The cursor stood on MACD's third line. Page Up onto RSI, which has ONE line: the
        // component index must be RSI's first, not MACD's third carried over to a series that
        // has no third.
        var h = new Harness();
        h.AddSeries("rsi", "RSI 14", "Pane_RSI", "RSI");
        h.AddSeries("macd", "MACD", "Pane_MACD", "MACD", "Signal", "Histogram");
        h.Store.Dispatch(new SelectSeriesAction("macd"));
        h.Store.Dispatch(new SelectComponentAction(2));
        Assert.Equal(2, h.Store.State.FocusedComponentIndex);

        h.Press("NAV_SERIES_PREV");

        Assert.Equal("rsi", h.Store.State.FocusedSeriesId);
        Assert.Equal(0, h.Store.State.FocusedComponentIndex);
    }

    [Fact]
    public void Page_Up_onto_the_candles_starts_at_their_first_component_not_the_body()
    {
        // The engine's documented rule: after a series switch the Up/Down cycle starts at the
        // FIRST component. The candles are the one series where that differs from the series
        // reducer's own default (the Body), because their components are, top to bottom, upper
        // wick, body, lower wick (CoreIndicatorProvider). A2q's N05 deleted the engine's reset
        // and the cursor landed on the body instead; nothing noticed, because no test used the
        // real candle roles. The two rules disagree: see "Decisions for Cody" in the A2q report.
        var h = new Harness();
        var cfg = h.Store.State.ActiveSeries.First(s => s.Id == "candles").Config;
        cfg.Components.Clear();
        foreach (var (name, role) in new[] { ("upper_wick", ComponentRole.PriceAction), ("body", ComponentRole.Body), ("lower_wick", ComponentRole.PriceAction) })
            cfg.Components.Add(new ComponentConfig { Name = name, DisplayName = name, Role = role, IsVisible = true, IsEnabled = true });
        h.AddSeries("rsi", "RSI 14", "Pane_RSI", "RSI");
        h.Store.Dispatch(new SelectSeriesAction("rsi"));

        h.Press("NAV_SERIES_PREV");

        Assert.Equal("candles", h.Store.State.FocusedSeriesId);
        Assert.Equal(0, h.Store.State.FocusedComponentIndex);
    }

    /// <summary>A Line chart: candles, the Price line on the close, an average, and RSI below.</summary>
    private static Harness LineChart()
    {
        var h = new Harness();
        h.AddSeries("price", "Price", "Main", "line").Config.Components[0].DataMapping = "close";
        h.AddSeries("ma", "Average", "Main", "line");
        h.AddSeries("rsi", "RSI 14", "Pane_RSI", "RSI");
        h.Store.Dispatch(new SelectSeriesAction("candles"));
        h.Store.Dispatch(new SetChartStyleAction(PriceChartStyle.Line, false));
        return h;
    }

    [Fact]
    public void On_a_line_chart_Page_Up_from_the_average_stops_on_the_close_line_not_the_Price_line()
    {
        // Cody, 2026-11-19: on a Line chart the price series IS the close line; the separate
        // Price line repeats it, is not drawn there, and is not a stop.
        var h = LineChart();
        Assert.Equal("candles", h.Store.State.FocusedSeriesId);   // the price series kept focus
        h.Store.Dispatch(new SelectSeriesAction("ma"));

        h.Press("NAV_SERIES_PREV");
        Assert.Equal("candles", h.Store.State.FocusedSeriesId);
        Assert.Contains(h.Spoken, t => t.Contains("Close"));      // the landing is heard as the line

        h.Press("NAV_SERIES_PREV");                                // the top of the chart: clamps
        Assert.Equal("candles", h.Store.State.FocusedSeriesId);
        Assert.Empty(h.Spoken);
    }

    [Fact]
    public void On_a_line_chart_Page_Down_walks_past_the_Price_line()
    {
        var h = LineChart();

        h.Press("NAV_SERIES_NEXT");
        Assert.Equal("ma", h.Store.State.FocusedSeriesId);
        h.Press("NAV_SERIES_NEXT");
        Assert.Equal("rsi", h.Store.State.FocusedSeriesId);
    }

    [Fact]
    public void Back_on_candles_Page_Up_reaches_the_candles_again()
    {
        var h = LineChart();
        h.Store.Dispatch(new SetChartStyleAction(PriceChartStyle.Candles, false));
        Assert.Equal("candles", h.Store.State.FocusedSeriesId);
        Assert.Equal("Close", h.Store.State.ActiveSeries.Single(s => s.Id == "candles").Components.Single().Name);   // its own part back
        h.Store.Dispatch(new SelectSeriesAction("price"));
        Assert.Equal("price", h.Store.State.FocusedSeriesId);     // a stop again on candles

        h.Press("NAV_SERIES_PREV");

        Assert.Equal("candles", h.Store.State.FocusedSeriesId);
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_on_the_candles_refuses_and_says_why()
    {
        var h = new Harness();
        h.Store.Dispatch(new SelectSeriesAction("candles"));
        Assert.Contains(h.Store.State.ActiveSeries, s => s.Id == "candles");

        h.Press("REMOVE_FOCUSED_SERIES");

        Assert.Contains(h.Store.State.ActiveSeries, s => s.Id == "candles");
        Assert.Contains(h.Spoken, s => s.Contains("Cannot remove", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Delete_on_an_indicator_removes_it_and_says_so_even_with_chart_speech_off()
    {
        // "Removed RSI 14" confirms a change the user made, so it is the INTERFACE speaking, not
        // the chart, and F2 (the chart's mute) must not swallow it: a series that vanishes in
        // silence is a series the user does not know they deleted.
        var h = new Harness();
        h.AddSeries("rsi", "RSI 14", "Pane_RSI", "RSI");
        h.Store.Dispatch(new SelectSeriesAction("rsi"));
        h.Store.Dispatch(new ToggleSpeechAction());
        Assert.False(h.Store.State.IsSpeechEnabled);

        h.Press("REMOVE_FOCUSED_SERIES");

        Assert.DoesNotContain(h.Store.State.ActiveSeries, s => s.Id == "rsi");
        Assert.Contains(h.Spoken, s => s.Contains("Removed RSI 14", StringComparison.Ordinal));
    }

    // ── Which strategy a series is navigated with ────────────────────────────

    [Fact]
    public void Up_and_Down_on_a_volume_profile_walk_its_price_bins()
    {
        // A profile is volume AT PRICE: Up and Down move through its bins. Navigated as points,
        // Up/Down would walk its components instead and the bins would be unreachable.
        var registry = new SeriesNavigationRegistry(new PointNavigationStrategy(), new BinnedNavigationStrategy());
        var cfg = new SeriesConfig { Id = "vp", Name = "Volume Profile" };
        cfg.Components.Add(new ComponentConfig { Name = "Volume", DisplayType = ComponentDisplayType.Line });

        Assert.IsType<BinnedNavigationStrategy>(registry.GetStrategy(new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = "vp" }) { IsProfile = true }));
    }

    [Fact]
    public void Up_and_Down_on_a_heatmap_walk_its_bins_and_on_a_line_its_components()
    {
        var registry = new SeriesNavigationRegistry(new PointNavigationStrategy(), new BinnedNavigationStrategy());
        var heat = new SeriesConfig { Id = "hm", Name = "Liquidity" };
        heat.Components.Add(new ComponentConfig { Name = "Heat", DisplayType = ComponentDisplayType.Heatmap });
        var line = new SeriesConfig { Id = "ema", Name = "EMA" };
        line.Components.Add(new ComponentConfig { Name = "EMA", DisplayType = ComponentDisplayType.Line });

        Assert.IsType<BinnedNavigationStrategy>(registry.GetStrategy(new ChartSeries(heat, new SeriesDataBuffer())));
        Assert.IsType<PointNavigationStrategy>(registry.GetStrategy(new ChartSeries(line, new SeriesDataBuffer())));
        Assert.IsType<PointNavigationStrategy>(registry.GetStrategy(null));
    }
}
