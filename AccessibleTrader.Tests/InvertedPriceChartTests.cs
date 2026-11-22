using System.Collections.Immutable;
using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.GoBar;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Theming;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// THE PRICE PANE UPSIDE DOWN (Cody, 2026-11-22: "invert chart, 'i'. if invert chart applies to
/// only the main pane, scope it to just that"; then "pitch flip too: yes" and "drawing and alerts
/// set to real prices: yes").
///
/// <para>
/// What turns over: the price pane's picture, its axis, the pointer's reading of it, and the
/// pitch, which follows the picture as it does for the log scale. What does not: indicator panes,
/// words, prices, colours, and anything stored (drawings and alerts keep real prices). Each layer
/// is pinned here, and the callers with it, because a mapping with a switch nobody passes is the
/// shape every scale bug in this codebase has had.
/// </para>
/// </summary>
public sealed class InvertedPriceChartTests
{
    private const double Lo = 100, Hi = 200;

    // ── The key, the Go bar, the sentence ─────────────────────────────────

    [Fact]
    public void I_is_bound_bare_and_the_Alt_I_dossier_is_untouched()
    {
        var mgr = new ShortcutManager(new FixedPaths(TestTemp.NewDir("invert-key")));

        Assert.Equal(SystemCommand.TogglePriceInvert, mgr.GetCommand("I", shift: false, ctrl: false, alt: false));
        Assert.Equal(SystemCommand.OpenAssetDossier, mgr.GetCommand("I", shift: false, ctrl: false, alt: true));
    }

    [Theory]
    [InlineData("INVERT")]
    [InlineData("invert chart")]
    [InlineData("upside down")]
    [InlineData("Invert price pane")]
    public void The_Go_bar_reaches_it_by_its_own_words(string text)
    {
        var top = GoBarMatcher.Match(text, GoBarRegistry.All).FirstOrDefault()?.Entry;
        Assert.Equal(SystemCommand.TogglePriceInvert, top?.Command);
    }

    private static (CommandDispatcher Dispatcher, MockWorkspaceStore Store, SpyEventBus Bus) Dispatcher(bool inverted)
    {
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        // A chart command: it needs a chart loaded to get past the no-data gate.
        store.EmitState(WorkspaceState.Initial with { IsPriceInverted = inverted, Data = new TimeSeriesBuffer<Ohlcv>(Bars()) });
        var dispatcher = new CommandDispatcher(bus, Substitute.For<INavigationEngine>(), store,
            Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));
        return (dispatcher, store, bus);
    }

    [Theory]
    [InlineData(false, CommandDispatcher.InvertedSentence)]
    [InlineData(true, CommandDispatcher.UprightSentence)]
    public void The_command_turns_the_pane_over_and_says_which_way_it_now_is(bool from, string expected)
    {
        var (dispatcher, store, bus) = Dispatcher(from);

        dispatcher.DispatchFromGoBar(SystemCommand.TogglePriceInvert);

        Assert.Single(store.DispatchedActions.OfType<TogglePriceInvertAction>());
        var said = Assert.Single(bus.Log.OfType<FeedbackRequestEvent>());
        Assert.Equal(expected, said.Message);
        Assert.Equal(SpeechChannel.Interface, said.Channel);
    }

    [Fact]
    public void The_inverted_sentence_tells_the_ear_what_changed_and_what_did_not()
    {
        Assert.StartsWith("Price pane inverted.", CommandDispatcher.InvertedSentence);
        Assert.Contains("sound lower", CommandDispatcher.InvertedSentence);
        Assert.Contains("read as they are", CommandDispatcher.InvertedSentence);
    }

    // ── State: per tab, saved, restored ───────────────────────────────────

    private static WorkspaceState Reduce(WorkspaceState initial, WorkspaceAction action)
    {
        using var store = new WorkspaceStore(new SpyEventBus(), new MockViewportRangeCalculator(),
            new MockViewportNavigationService(), new MockVolumeStateService());
        store.Dispatch(new UpdateSettingsAction(_ => initial));
        store.Dispatch(action);
        return store.State;
    }

    private static WorkspaceState TabState(bool inverted = false) => WorkspaceState.Initial with
    {
        Identity = new ChartIdentity("Spot", "MEXC", "BTCUSDT", "1h"),
        IsPriceInverted = inverted,
        ActiveTabIndex = 0,
        TabSnapshots = ImmutableList<TabSnapshot>.Empty,
    };

    [Fact]
    public void The_toggle_flips_and_the_set_action_sets()
    {
        Assert.True(Reduce(TabState(), new TogglePriceInvertAction()).IsPriceInverted);
        Assert.False(Reduce(TabState(true), new TogglePriceInvertAction()).IsPriceInverted);
        Assert.True(Reduce(TabState(), new SetPriceInvertedAction(true)).IsPriceInverted);
        Assert.False(Reduce(TabState(true), new SetPriceInvertedAction(false)).IsPriceInverted);
    }

    [Fact]
    public void Each_tab_keeps_its_own_way_up_across_switches()
    {
        var second = Reduce(TabState(inverted: true), new AddTabAction());
        Assert.False(second.IsPriceInverted);   // a new tab starts upright

        var back = Reduce(second, new SwitchTabAction(0));
        Assert.True(back.IsPriceInverted);
        Assert.False(Reduce(back, new SwitchTabAction(1)).IsPriceInverted);
    }

    // Both ways round: the active tab is written from the state and the others from their
    // snapshots, two different lines, and a test with only one of them inverted proves one.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void It_round_trips_through_the_workspace_file(bool activeInverted)
    {
        string dir = TestTemp.NewDir("att-invert-");
        try
        {
            var withTwo = Reduce(TabState(), new AddTabAction());   // tab 1 active, tab 0 a snapshot
            var source = withTwo with
            {
                IsPriceInverted = activeInverted,
                TabSnapshots = withTwo.TabSnapshots!.Select(t => t with { IsPriceInverted = !activeInverted }).ToImmutableList(),
            };
            var lib = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths())
                { LibraryDirectoryOverride = dir };
            var store = new MockWorkspaceStore();
            store.EmitState(source);
            lib.SaveWorkspaceProfile("inv", store);

            var loaded = lib.LoadProfile("inv")!;
            Assert.Equal(activeInverted, loaded.Tabs[1].IsPriceInverted);
            Assert.Equal(!activeInverted, loaded.Tabs[0].IsPriceInverted);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Restore_turns_the_pane_over_only_when_the_tab_was_saved_that_way(bool saved, int expectedActions)
    {
        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial);
        var initializer = new WorkspaceInitializer(
            Substitute.For<ISeriesManagementService>(), store, Substitute.For<IIndicatorService>());
        var config = new WorkspaceConfiguration
        {
            Tabs = { new TabConfiguration { Provider = "MEXC", Symbol = "BTCUSDT", Timeframe = "1h", Market = "Spot", IsPriceInverted = saved } }
        };

        initializer.RestoreWorkspace(config);

        var actions = store.DispatchedActions.OfType<SetPriceInvertedAction>().ToList();
        Assert.Equal(expectedActions, actions.Count);
        if (saved) Assert.True(actions[0].Inverted);
    }

    // ── The maths: a mirror, all four directions agreeing ─────────────────

    [Theory]
    [InlineData(100, false)]
    [InlineData(130, false)]
    [InlineData(175, true)]
    [InlineData(200, true)]
    public void Inverted_Y_is_the_upright_Y_mirrored_and_reads_back_to_the_same_price(double price, bool log)
    {
        float up = ChartMath.MapY(price, 10f, 410f, Lo, Hi, log);
        float down = ChartMath.MapY(price, 10f, 410f, Lo, Hi, log, inverted: true);
        Assert.Equal(10f + 410f - up, down, 3);

        double back = ChartMath.MapYToPrice(down - 10f, 400, Lo, Hi, log, inverted: true);
        Assert.Equal(price, back, 3);
        Assert.Equal(down - 10f, ChartMath.PriceToScreenY(price, 400, Lo, Hi, log, inverted: true), 3);

        // The pitch's position is the picture's: 1 at the top of the pane, whichever price is there.
        double pos = ChartMath.NormalizedPosition(price, Lo, Hi, log, inverted: true);
        Assert.Equal(1.0 - (down - 10f) / 400.0, pos, 4);
    }

    [Fact]
    public void Upside_down_the_high_is_at_the_bottom()
    {
        Assert.Equal(410f, ChartMath.MapY(Hi, 10f, 410f, Lo, Hi, false, inverted: true), 3);
        Assert.Equal(10f, ChartMath.MapY(Lo, 10f, 410f, Lo, Hi, false, inverted: true), 3);
    }

    [Fact]
    public void The_canvas_mappings_the_pointer_uses_agree_upside_down()
    {
        var dividers = Array.Empty<(string, float)>();
        foreach (double price in new[] { 110.0, 190.0 })
        {
            double y = ChartMath.PriceToCanvasY(price, 720, dividers, 0f, Lo, Hi, false, inverted: true);
            double upright = ChartMath.PriceToCanvasY(price, 720, dividers, 0f, Lo, Hi, false);
            Assert.NotEqual(upright, y, 1);
            Assert.Equal(price, ChartMath.MapYToPriceInPane(y, 720, dividers, 0f, Lo, Hi, false, inverted: true), 3);
        }
    }

    [Theory]
    [InlineData("Main", true)]
    [InlineData(null, true)]
    [InlineData("Pane_RSI", false)]
    [InlineData("Volume", false)]
    public void Only_the_price_pane_is_ever_inverted(string? pane, bool expected)
    {
        Assert.Equal(expected, ChartMath.IsInvertedFor(WorkspaceState.Initial with { IsPriceInverted = true }, pane));
        Assert.False(ChartMath.IsInvertedFor(WorkspaceState.Initial, pane));
    }

    // ── The pitch ─────────────────────────────────────────────────────────

    private static ComponentConfig PriceLine() => new()
    {
        Name = "close", DisplayName = "Close", DisplayType = ComponentDisplayType.Line,
        Role = ComponentRole.PriceAction, DataMapping = "close",
        IsVisible = true, IsEnabled = true, Volume = 1f,
        PitchMapping = PitchMapping.Value, BaseFrequency = 440, FreqMultiplier = 1.0,
        AmplitudeMapping = AmplitudeMapping.None, Waveform = "sine", EnvelopeType = "Sustain",
    };

    private static ChartSeries Series(string pane, ComponentConfig comp, double value, int bars)
    {
        var cfg = new SeriesConfig { Id = "price", Name = "Price", FriendlyName = "Price", IndicatorCode = "PRICE",
            Pane = pane, IsVisible = true, Volume = 1f };
        cfg.Components.Add(comp);
        var data = new SeriesDataBuffer { SeriesId = "price" };
        data.ComponentData[comp.Name] = Enumerable.Repeat(value, bars).ToArray();
        return new ChartSeries(cfg, data);
    }

    private static Ohlcv Bar(double close) =>
        new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), close, close + 1, close - 1, close, 1000);

    // 125 is a quarter of the way up 100-200: 200 * 5^0.25 = 299.07 Hz upright; upside down it is
    // drawn three quarters of the way up, 200 * 5^0.75 = 668.74 Hz.
    private const double QuarterHz = 299.07, ThreeQuarterHz = 668.74;

    [Fact]
    public void A_price_drawn_high_sounds_high_whichever_way_up()
    {
        var strategy = new DefaultSonificationStrategy(new SoundPatchRegistry());
        var series = Series("Main", PriceLine(), 125, 1);

        var up = strategy.CreateAudioPoint(series, PriceLine(), 125, Bar(125), 0, 10, (Lo, Hi), 1f);
        var down = strategy.CreateAudioPoint(series, PriceLine(), 125, Bar(125), 0, 10, (Lo, Hi), 1f, isInverted: true);

        Assert.InRange(up.Frequency, QuarterHz - 1, QuarterHz + 1);
        Assert.InRange(down.Frequency, ThreeQuarterHz - 1, ThreeQuarterHz + 1);
    }

    [Fact]
    public void The_wick_tones_trade_places_with_the_wicks()
    {
        var strategy = new DefaultSonificationStrategy(new SoundPatchRegistry());
        var upper = new ComponentConfig { Name = "wick_upper", DisplayName = "Upper wick", Role = ComponentRole.Wick,
            DisplayType = ComponentDisplayType.Wick, IsVisible = true, IsEnabled = true, Volume = 1f, FreqMultiplier = 1.0,
            Waveform = "sine", EnvelopeType = "Sustain" };
        var series = Series("Main", upper, 150, 1);

        double upright = strategy.CreateAudioPoint(series, upper, 150, Bar(150), 0, 10, (Lo, Hi), 1f).Frequency;
        double inverted = strategy.CreateAudioPoint(series, upper, 150, Bar(150), 0, 10, (Lo, Hi), 1f, isInverted: true).Frequency;

        Assert.Equal(880.0, upright, 3);
        Assert.Equal(220.0, inverted, 3);
    }

    private sealed class SpyDriver : IAudioDriver
    {
        public List<(int Slot, double Frequency, float Volume)> Calls { get; } = new();
        public int SampleRate => 44100;
        public int Channels => 2;
        public event Action<int>? PointReached { add { } remove { } }
        public void SetVoice(int slot, double frequency, float volume, float pan, string waveform,
            bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain",
            bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f,
            float sawMix = 0f, float triangleMix = 0f, float subSawMix = 0f)
        {
            lock (Calls) Calls.Add((slot, frequency, volume));
        }
        public void StopVoice(int slot) { }
        public void StopAll() { }
        public void Reset() { }
        public void SetMasterGain(float gain) { }
        public void Pause() { }
        public void Resume() { }
        public List<double> Audible() { lock (Calls) return Calls.Where(c => c.Volume > 0f).Select(c => c.Frequency).ToList(); }
    }

    private const int BarCount = 8;

    private static List<Ohlcv> Bars() =>
        Enumerable.Range(0, BarCount).Select(i => new Ohlcv(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i), 125, 126, 124, 125, 1000)).ToList();

    private static WorkspaceState SoundState(ChartSeries series, bool inverted) => WorkspaceState.Initial with
    {
        Data = new TimeSeriesBuffer<Ohlcv>(Bars()),
        ActiveSeries = ImmutableList.Create(series),
        FocusedSeriesId = series.Id,
        FocusedComponentIndex = 0,
        CurrentDataIndex = BarCount - 1,
        ViewportStartIndex = 0,
        ViewportLength = BarCount,
        ViewportRange = (Lo, Hi),
        PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty.Add("Main", (Lo, Hi)).Add("Pane_X", (Lo, Hi)),
        IsPriceInverted = inverted,
        ChartVolume = 1f,
        PlaybackSpeed = 1.0f,
    };

    private static double ArrowPitch(string pane, bool inverted)
    {
        var driver = new SpyDriver();
        var sonifier = new NavigationSonifier(driver, new DefaultSonificationStrategy(new SoundPatchRegistry()), new SoundPatchRegistry());
        sonifier.SyncNavigationSlots(SoundState(Series(pane, PriceLine(), 125, BarCount), inverted));
        return driver.Audible().Last();
    }

    [Fact]
    public void Arrowing_the_price_pane_hears_it_upside_down_and_an_indicator_pane_does_not()
    {
        Assert.InRange(ArrowPitch("Main", inverted: false), QuarterHz - 1, QuarterHz + 1);
        Assert.InRange(ArrowPitch("Main", inverted: true), ThreeQuarterHz - 1, ThreeQuarterHz + 1);
        Assert.InRange(ArrowPitch("Pane_X", inverted: true), QuarterHz - 1, QuarterHz + 1);
    }

    private static async Task<List<double>> PlaybackPitches(string pane, bool inverted)
    {
        var series = Series(pane, PriceLine(), 125, BarCount);
        var store = new MockWorkspaceStore();
        store.EmitState(SoundState(series, inverted));
        var driver = new SpyDriver();
        var sequencer = new AudioSequencer(driver, new DefaultSonificationStrategy(new SoundPatchRegistry()),
            store, new SoundPatchRegistry(), NullLogger<AudioSequencer>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await sequencer.StartMultiSeriesPlaybackAsync(new[] { series }, Bars(), BarCount - 1, cts.Token);
        return driver.Audible();
    }

    [Fact]
    public async Task Playback_hears_the_price_pane_upside_down_and_an_indicator_pane_upright()
    {
        var main = await PlaybackPitches("Main", inverted: true);
        var other = await PlaybackPitches("Pane_X", inverted: true);
        Assert.NotEmpty(main);
        Assert.NotEmpty(other);
        Assert.All(main, f => Assert.InRange(f, ThreeQuarterHz - 1, ThreeQuarterHz + 1));
        Assert.All(other, f => Assert.InRange(f, QuarterHz - 1, QuarterHz + 1));
    }

    // ── The pointer ───────────────────────────────────────────────────────

    [Fact]
    public void The_hit_tester_finds_a_line_where_it_is_drawn_upside_down()
    {
        var cfg = new SeriesConfig { Id = "ema", Name = "EMA", FriendlyName = "EMA", Pane = "Main", IsVisible = true };
        cfg.Components.Add(new ComponentConfig { Name = "Line", IsVisible = true });
        var buf = new SeriesDataBuffer { SeriesId = "ema" };
        buf.ComponentData["Line"] = Enumerable.Repeat(180.0, BarCount).ToArray();
        var state = SoundState(new ChartSeries(cfg, buf), inverted: true);

        float drawnAt = ChartMath.MapY(180, 0, 720, Lo, Hi, false, inverted: true);   // near the bottom
        float uprightAt = ChartMath.MapY(180, 0, 720, Lo, Hi, false);                 // near the top

        var hit = ChartHitTester.HitTest(state, Array.Empty<(string, float)>(), 0f, 0f, x: 640, y: drawnAt, width: 1280, height: 720);
        Assert.Equal("ema", hit?.SeriesId);
        var miss = ChartHitTester.HitTest(state, Array.Empty<(string, float)>(), 0f, 0f, x: 640, y: uprightAt, width: 1280, height: 720);
        Assert.NotEqual("ema", miss?.SeriesId);
    }

    private sealed class StubDrawingService : IDrawingService
    {
        public Dictionary<string, double[]> CalculateDrawingData(DrawingData drawing, IReadOnlyList<Ohlcv> chartData) => new();
    }

    private static double ClickedPrice(bool inverted, double y)
    {
        var bus = new SpyEventBus();
        var store = new WorkspaceStore(bus, new ViewportRangeCalculator(), new ViewportNavigationService(), new VolumeStateService());
        var bars = Enumerable.Range(0, 200).Select(i => new Ohlcv(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i), 100 + i * 0.1, 101 + i * 0.1, 99 + i * 0.1, 100 + i * 0.1, 1000)).ToList();
        store.Dispatch(new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(bars), IsInitialLoad: true));
        store.Dispatch(new SetPriceInvertedAction(inverted));
        var input = new BlazorInputService();
        var manager = new DrawingInteractionManager(bus, new StubDrawingService(), store,
            new IndicatorModelFactory(new MockStylingService(), new MockIndicatorPreferencesService()), input);
        manager.HandleAddDrawing("TrendLine", bars);
        input.ProcessMouse(x: 640, y: y, type: "MouseDown", width: 1280, height: 720);
        var line = store.State.ActiveSeries.Single(s => s.IsDrawing && s.Drawing?.Type == DrawingType.TrendLine);
        return line.Drawing!.AnchorPrice2!.Value;
    }

    [Fact]
    public void A_point_placed_by_mouse_holds_the_price_drawn_where_it_was_clicked()
    {
        // Near the top of an inverted pane is where the LOW prices are drawn, so that is the
        // price stored: the same one an upright click near the bottom stores.
        double invertedTop = ClickedPrice(inverted: true, y: 100);
        double uprightBottom = ClickedPrice(inverted: false, y: 620);
        double uprightTop = ClickedPrice(inverted: false, y: 100);

        Assert.Equal(uprightBottom, invertedTop, 6);
        Assert.True(uprightTop > invertedTop + 1, $"top upright {uprightTop}, top inverted {invertedTop}");
    }

    // ── The picture ───────────────────────────────────────────────────────

    private static ThemeService Themes()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings);
    }

    private static ChartRenderer Draw(bool inverted)
    {
        var renderer = new ChartRenderer(Themes(),
            new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
            new PaneLayoutService(), NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        var candles = new SeriesConfig { Id = CoreSeriesIds.Candles, Name = "Candles", Pane = "Main" };
        candles.Components.Add(new ComponentConfig { Name = "body", DisplayType = ComponentDisplayType.Candle, IsVisible = true });
        var bars = Enumerable.Range(0, 20).Select(i => new Ohlcv(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i), 120 + i, 121 + i, 119 + i, 120 + i, 1000)).ToList();
        using var bmp = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, 400, 300, bars, new List<ChartSeries> { new(candles, new SeriesDataBuffer { SeriesId = candles.Id }) },
                cursorIndex: 10, viewportStart: 0, viewportLength: bars.Count, viewportRange: (Lo, Hi),
                paneRanges: new Dictionary<string, (double Min, double Max)>(), rightMarginBars: 0,
                isPriceInverted: inverted);
        return renderer;
    }

    [Fact]
    public void The_axis_is_drawn_upside_down_with_its_labels_on_the_prices_they_mark()
    {
        var upright = Draw(inverted: false).LastMainAxisLabels.OrderBy(t => t.Price).ToList();
        var inverted = Draw(inverted: true).LastMainAxisLabels.OrderBy(t => t.Price).ToList();
        Assert.True(upright.Count >= 2 && inverted.Count >= 2);

        // Upright the higher price is higher on screen (smaller Y); upside down, lower.
        Assert.True(upright[^1].Y < upright[0].Y);
        Assert.True(inverted[^1].Y > inverted[0].Y);
    }

    [Fact]
    public void The_crosshair_badge_sits_on_the_close_as_drawn()
    {
        var r = Draw(inverted: true);
        var (axis, min, max, log, _) = r.LastMainScale;
        var badge = r.LastMainBadge!.Value;
        Assert.Equal(ChartMath.MapY(badge.Value, axis.Top, axis.Bottom, min, max, log, inverted: true), badge.LineY, 1);
    }

    [Fact]
    public void A_mark_above_the_bar_stays_above_the_candle_on_screen()
    {
        using var bmp = new SKBitmap(10, 10);
        using var canvas = new SKCanvas(bmp);
        var bar = new Ohlcv(DateTime.UnixEpoch, 150, 160, 140, 150, 1);
        var ctx = new RenderContext(canvas, new SKRect(0, 0, 100, 400), new List<Ohlcv> { bar }, 0, 1, Lo, Hi, false,
            100f, 1f, "Main", -1, Themes().Current) { IsInverted = true };
        var above = new ComponentConfig { Name = "a", MarkerAnchor = MarkerAnchor.AboveBar };
        var below = new ComponentConfig { Name = "b", MarkerAnchor = MarkerAnchor.BelowBar };

        float candleTop = Math.Min(ChartMath.MapY(140, 0, 400, Lo, Hi, false, true), ChartMath.MapY(160, 0, 400, Lo, Hi, false, true));
        float candleFoot = Math.Max(ChartMath.MapY(140, 0, 400, Lo, Hi, false, true), ChartMath.MapY(160, 0, 400, Lo, Hi, false, true));

        Assert.True(StandardRenderers.ResolveMarkerY(ctx, above, 0, double.NaN) < candleTop);
        Assert.True(StandardRenderers.ResolveMarkerY(ctx, below, 0, double.NaN) > candleFoot);
    }

    // ── The words ─────────────────────────────────────────────────────────

    [Fact]
    public void The_layout_description_says_so_and_an_upright_one_does_not()
    {
        var series = Series("Main", PriceLine(), 125, BarCount);
        var state = SoundState(series, inverted: true) with { PrimarySeriesId = series.Id };

        Assert.Contains("Inverted: 200.00 at the bottom, 100.00 at the top", ChartLayoutDescriber.Describe(state, "BTCUSDT", "1h"));
        Assert.DoesNotContain("Inverted", ChartLayoutDescriber.Describe(state with { IsPriceInverted = false }, "BTCUSDT", "1h"));
    }

    private sealed class FixedPaths : IPlatformPathService
    {
        public FixedPaths(string dir) { AppDataDirectory = dir; CacheDirectory = dir; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }
}
