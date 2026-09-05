using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// Cody, 2026-09-05: "if a series or component is hidden, it should be excluded from the
/// narration, both playback and new bars."
///
/// <para>
/// The rule already held for the marker, overlay-cross, zone-line and cloud scans on bar close
/// (each goes through <see cref="SeriesNarrationScope.ComponentNarrates"/>, which requires the
/// SERIES to be visible and unmuted, after its own component check) and for playback. It did
/// NOT hold on the oscillator path, in two ways. A hidden oscillator COMPONENT still narrated its
/// zone transitions, because that path applied the narration selection and not the visibility
/// rule the other four sites apply first. And an analyser context whose component name matched
/// nothing on the series skipped the scope check altogether — so a hidden SERIES narrated
/// "entered overbought" from a component the user could not even find in the Object Tree.
/// </para>
///
/// <para>
/// Both routes are pinned here, hidden and muted, so the two cannot drift apart again.
/// </para>
/// </summary>
public class HiddenSeriesNarrationTests
{
    // ── Bar close ────────────────────────────────────────────────────────────

    private sealed class CapturingSpeechRouter : ISpeechFeedbackRouter
    {
        public List<string> Spoken { get; } = new();
        public void Speak(string message, bool interrupt = false, SpeechChannel channel = SpeechChannel.Manual) => Spoken.Add(message);
        public void SpeakPoint(WorkspaceState s, WorkspaceState? p, ChartSeries ser, Ohlcv pt, string pfx = "") { }
        public void SpeakProfile(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int bin, string pfx = "") { }
        public void SpeakHeatmap(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int di, int bin, string pfx = "") { }
    }

    private sealed class StubContextAnalyzer : IIndicatorContextAnalyzer
    {
        public IndicatorContext? FixedContext { get; set; }
        public void RegisterDefinition(IndicatorContextDefinition def) { }
        public bool HasZoneThresholds(string indicatorCode, string componentName) => false;
        public IndicatorContext? Analyze(ChartSeries series, WorkspaceState state) => FixedContext;
        public IEnumerable<IndicatorContext> AnalyzeAll(ChartSeries series, WorkspaceState state)
            => FixedContext != null ? new[] { FixedContext } : Enumerable.Empty<IndicatorContext>();
    }

    private static Ohlcv Bar(int i) => new(new DateTime(2026, 1, 1).AddMinutes(i), 100, 110, 95, 105, 1000);
    private static TimeSeriesBuffer<Ohlcv> Bars(int count) => new(Enumerable.Range(0, count).Select(Bar));

    /// <summary>An RSI-shaped series: one oscillator component, named the way the analyser names it.</summary>
    private static SeriesConfig Rsi(string componentName = "RSI")
    {
        var cfg = new SeriesConfig
        {
            Id = "rsi", Name = "RSI", FriendlyName = "RSI", IndicatorCode = "RSI",
            IsAutoNarrated = true, IsVisible = true, IsMuted = false,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = componentName, DisplayName = componentName,
            DisplayType = ComponentDisplayType.Oscillator, IsVisible = true,
        });
        return cfg;
    }

    private static ChartSeries WithBars(SeriesConfig cfg, int bars)
    {
        var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
        var d = new double[bars];
        Array.Fill(d, 50.0);
        buf.ComponentData[cfg.Components[0].Name] = d;
        return new ChartSeries(cfg, buf);
    }

    private static WorkspaceState State(ChartSeries series, int bars) => WorkspaceState.Initial with
    {
        Data = Bars(bars),
        ActiveSeries = ImmutableList.Create(series),
        FocusedSeriesId = series.Id,
        CurrentDataIndex = bars - 1,
        InitStatus = InitializationStatus.Ready,
        DataStatus = DataStatus.Ready,
        IsSpeechEnabled = true,
    };

    /// <summary>
    /// Drives the same three-step sequence as <c>AutoNarrationTests.OscillatorEntersOverbought</c>:
    /// seed Normal, close one bar still Normal, then close a bar that has entered Overbought.
    /// Returns everything spoken on the last step.
    /// </summary>
    private static List<string> CloseIntoOverbought(SeriesConfig cfg)
    {
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        var router = new CapturingSpeechRouter();
        var analyzer = new StubContextAnalyzer
        {
            FixedContext = new IndicatorContext
            {
                IndicatorCode = "RSI", ComponentName = "RSI", CurrentValue = 72,
                Trend = TrendDirection.Rising, TrendBars = 1,
                Zone = ZoneStatus.Normal, Crossover = CrossoverStatus.None, NarrativeHint = "",
            },
        };
        _ = new AutoNarrationService(store, bus, router, analyzer);

        store.EmitState(State(WithBars(cfg, 1), 1));
        bus.Publish(new RedrawEvent());
        store.EmitState(State(WithBars(cfg, 2), 2));
        bus.Publish(new RedrawEvent());

        analyzer.FixedContext = analyzer.FixedContext! with { Zone = ZoneStatus.Overbought };
        router.Spoken.Clear();
        store.EmitState(State(WithBars(cfg, 3), 3));
        bus.Publish(new RedrawEvent());
        return router.Spoken;
    }

    [Fact]
    public void AVisibleOscillator_NarratesTheTransition_TheVacuityFloor()
    {
        // If this were silent, every case below would pass for the wrong reason.
        var said = CloseIntoOverbought(Rsi());
        Assert.Contains(said, s => s.Contains("overbought", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false, false)] // hidden
    [InlineData(true, true)]   // muted
    public void AHiddenOrMutedSeries_DoesNotNarrateAnOscillatorTransitionOnBarClose(bool visible, bool muted)
    {
        var cfg = Rsi();
        cfg.IsVisible = visible;
        cfg.IsMuted = muted;
        Assert.Empty(CloseIntoOverbought(cfg));
    }

    /// <summary>
    /// The second half of the defect: the analyser names a component the series does not have
    /// (which is what every one of the shipped definitions does for a series whose component was
    /// renamed). That used to bypass the scope check entirely.
    /// </summary>
    [Fact]
    public void AHiddenSeries_IsSilentEvenWhenTheAnalyserNamesAComponentItDoesNotHave()
    {
        var cfg = Rsi(componentName: "Line"); // the analyser will say "RSI"
        cfg.IsVisible = false;
        Assert.Empty(CloseIntoOverbought(cfg));
    }

    [Theory]
    [InlineData(false, false)] // hidden
    [InlineData(true, true)]   // muted
    public void AHiddenOrMutedComponent_DoesNotNarrateItsOscillatorTransition(bool visible, bool muted)
    {
        // H on the RSI line itself, series left visible. The four other scan sites already
        // skip such a component; the oscillator path did not.
        var cfg = Rsi();
        cfg.Components[0].IsVisible = visible;
        cfg.Components[0].IsMuted = muted;
        Assert.Empty(CloseIntoOverbought(cfg));
    }

    // ── Playback ─────────────────────────────────────────────────────────────

    private static ChartSeries Marker(string id, string template, bool visible = true, bool muted = false,
                                      bool componentVisible = true)
    {
        var cfg = new SeriesConfig
        {
            Id = id, Name = id, FriendlyName = id, IndicatorCode = id.ToUpperInvariant(),
            IsAutoNarrated = true, IsVisible = visible, IsMuted = muted,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Signal", DisplayName = "Signal", IsVisible = componentVisible,
            DisplayType = ComponentDisplayType.Dot, SignalSpeechTemplate = template,
        });
        var arr = new double[20];
        Array.Fill(arr, double.NaN);
        arr[5] = 1.0;
        var buf = new SeriesDataBuffer { SeriesId = id };
        buf.ComponentData["Signal"] = arr;
        return new ChartSeries(cfg, buf);
    }

    private static string? PlaybackStep(params ChartSeries[] series)
    {
        var state = WorkspaceState.Initial with
        {
            Data = Bars(20),
            ActiveSeries = ImmutableList.CreateRange(series),
            PrimarySeriesId = series[0].Id,
            FocusedSeriesId = series[0].Id,
            CurrentDataIndex = 5,
            ViewportStartIndex = 0,
            ViewportLength = 20,
            NarrateDuringPlayback = true,
            IsPlaying = true,
            PlaybackScope = PlaybackScope.Chart,
        };
        var plan = PlaybackPlan.Resolve(state, PlaybackScope.Chart);
        return PlaybackNarration.SignalsForStep(state, 5, plan);
    }

    [Fact]
    public void Playback_SpeaksTheVisibleSeriesAndNotTheHiddenOne()
    {
        string? spoken = PlaybackStep(
            Marker("alpha", "alpha signal"),
            Marker("beta", "beta signal", visible: false));
        Assert.NotNull(spoken);
        Assert.Contains("alpha signal", spoken);
        Assert.DoesNotContain("beta signal", spoken);
    }

    [Fact]
    public void Playback_SkipsAMutedSeriesAndAHiddenComponent()
    {
        string? spoken = PlaybackStep(
            Marker("alpha", "alpha signal"),
            Marker("beta", "beta signal", muted: true),
            Marker("gamma", "gamma signal", componentVisible: false));
        Assert.NotNull(spoken);
        Assert.Contains("alpha signal", spoken);
        Assert.DoesNotContain("beta signal", spoken);
        Assert.DoesNotContain("gamma signal", spoken);
    }
}
