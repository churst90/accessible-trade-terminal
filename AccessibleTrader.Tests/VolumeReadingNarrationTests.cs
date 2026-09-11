using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Workspace.Reducers;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// N on a VOLUME pane reads the closed bar's volume as the last clause of the bar-close ladder —
/// and only there. Cody, 2026-09-11: <i>"pressing n should enable narration which is spoken on
/// new bar closes as part of the indicator ladder but not during playback narration, these are
/// 2 different things, only signals should read during playback."</i>
///
/// <para>
/// The previous pass made the N confirmation on Volume SAY it had nothing to narrate. This pass
/// gives it something: a series whose only narratable content is a bar-type component reads
/// that value at each close. These tests run the real coordinator + narrator wiring, the way
/// <c>NewBarNarrationCompositionTests</c> does, with the real Volume shape — a
/// <see cref="ComponentDisplayType.Bar"/>, which is what <c>CoreIndicatorProvider</c> declares
/// and what the previous pass's fixture got wrong.
/// </para>
/// </summary>
public sealed class VolumeReadingNarrationTests
{
    private static List<Ohlcv> Bars(int n, double volumeStep = 1000) => Enumerable.Range(0, n)
        .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddMinutes(i), 100 + i, 101 + i, 99 + i, 100.5 + i, volumeStep * (i + 1)))
        .ToList();

    private sealed class Harness
    {
        public MockWorkspaceStore Store { get; } = new();
        public SpyEventBus Bus { get; } = new();
        public List<string> Spoken { get; } = new();

        public Harness()
        {
            var speech = new CounterSpeechManager();
            speech.OnSpeak = t => Spoken.Add(t);
            var formatter = new SpeechFormatter();
            var router = new SpeechFeedbackRouter(speech, formatter, Store);

            var narrator = new AutoNarrationService(Store, Bus, router, new IndicatorContextAnalyzer());
            _ = new AccessibilityFeedbackCoordinator(
                Store, new NavigationFeedbackManager(router, formatter), router,
                new AudioFeedbackRouter(new MockNavigationSonifier(), new MockEarconService()),
                formatter, Bus, new MockEarconService(), new SdkCandlePatternAnalyzer(),
                new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
                new ChartPatternFocus(), narrator);
        }

        public List<string> CloseBar(WorkspaceState after, Ohlcv closed, Ohlcv opened)
        {
            Spoken.Clear();
            Store.EmitState(after);
            Bus.Publish(new NewBarEvent(closed, opened));
            Bus.Publish(new RedrawEvent());
            return Spoken.ToList();
        }
    }

    /// <summary>The real Volume series: one Bar component mapped to the bars' volume.</summary>
    private static ChartSeries Volume(IReadOnlyList<Ohlcv> bars, bool narrated = true)
    {
        var cfg = new SeriesConfig
        {
            Id = CoreSeriesIds.Volume, Name = "Volume", FriendlyName = "Volume", IndicatorCode = "VOLUME",
            Pane = "Volume", IsAutoNarrated = narrated, IsVisible = true,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Volume", DisplayName = "Volume", DisplayType = ComponentDisplayType.Bar,
            Role = ComponentRole.Volume, DataMapping = "volume", IsVisible = true,
        });
        var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
        buf.ComponentData["Volume"] = bars.Select(b => b.Volume).ToArray();
        return new ChartSeries(cfg, buf);
    }

    private static WorkspaceState State(IReadOnlyList<Ohlcv> bars, ChartSeries series) => WorkspaceState.Initial with
    {
        Data = new TimeSeriesBuffer<Ohlcv>(bars.ToList()),
        ActiveSeries = ImmutableList.Create(series),
        CurrentDataIndex = bars.Count - 1,
        InitStatus = InitializationStatus.Ready,
        DataStatus = DataStatus.Ready,
        IsSpeechEnabled = true,
        AnnounceNewBars = true,
    };

    [Fact]
    public void ABarClose_readsTheClosedBarsVolume_asTheLastClauseOfTheOneUtterance()
    {
        var h = new Harness();
        var b100 = Bars(100);
        h.Store.EmitState(State(b100, Volume(b100)));   // bar 99 forming; narration seeds here
        h.Bus.Publish(new RedrawEvent());

        var b101 = Bars(101);
        var said = h.CloseBar(State(b101, Volume(b101)), b101[99], b101[100]);

        string one = Assert.Single(said);
        // Bar 99's volume is 100,000 — the CLOSED bar, not the one that just opened (101,000).
        Assert.Contains("Volume 100,000", one, StringComparison.Ordinal);
        Assert.DoesNotContain("101,000", one, StringComparison.Ordinal);
        // One breath: the close first, then the reading.
        Assert.Contains("Close 199.50", one, StringComparison.Ordinal);
        Assert.True(one.IndexOf("Close 199.50", StringComparison.Ordinal)
                    < one.IndexOf("Volume 100,000", StringComparison.Ordinal), one);
    }

    [Fact]
    public void AnIntraBarTick_doesNotReadTheVolume()
    {
        // The forming bar's volume changes on every tick. Reading it would be the running
        // commentary the bar-close rule exists to prevent — and it is not a closed number yet.
        var h = new Harness();
        var b100 = Bars(100);
        h.Store.EmitState(State(b100, Volume(b100)));
        h.Bus.Publish(new RedrawEvent());
        h.Spoken.Clear();

        // Same bar count, new values: an intra-bar recalculation.
        var ticked = Bars(100, volumeStep: 1500);
        h.Store.EmitState(State(ticked, Volume(ticked)));
        h.Bus.Publish(new RedrawEvent());

        Assert.Empty(h.Spoken);
    }

    [Fact]
    public void WithNarrationOff_theBarCloseIsStillAnnounced_withoutTheReading()
    {
        var h = new Harness();
        var b100 = Bars(100);
        h.Store.EmitState(State(b100, Volume(b100, narrated: false)));
        h.Bus.Publish(new RedrawEvent());

        var b101 = Bars(101);
        var said = h.CloseBar(State(b101, Volume(b101, narrated: false)), b101[99], b101[100]);

        string one = Assert.Single(said);
        Assert.Contains("Close 199.50", one, StringComparison.Ordinal);
        Assert.DoesNotContain("Volume 100,000", one, StringComparison.Ordinal);
    }

    [Fact]
    public void Playback_speaksSignalsOnly_neverTheReading()
    {
        // "only signals should read during playback." The playback route considers marker
        // components with a signal template and nothing else, so a narrated Volume contributes
        // no clause to any step.
        var b100 = Bars(100);
        var state = State(b100, Volume(b100));

        for (int bar = 0; bar < 100; bar++)
            Assert.Null(PlaybackNarration.SignalsForStep(state, bar));
    }

    [Fact]
    public void ALargeVolume_isSpokenAsAWord_notALetter()
    {
        var h = new Harness();
        var b100 = Bars(100, volumeStep: 50_000);   // bar 99 = 5,000,000
        h.Store.EmitState(State(b100, Volume(b100)));
        h.Bus.Publish(new RedrawEvent());

        var b101 = Bars(101, volumeStep: 50_000);
        var said = h.CloseBar(State(b101, Volume(b101)), b101[99], b101[100]);

        Assert.Contains("Volume 5 million", Assert.Single(said), StringComparison.Ordinal);
    }

    [Fact]
    public void ALevelOnTheVolumePane_isCrossed_byTheRealBarComponent()
    {
        // The previous pass's advice — "press 0 to add a reference level and its crossings will
        // speak" — was proved against a Histogram fixture, and PrimaryReading accepted Line and
        // Histogram only. On the real Bar component nothing could ever cross the level.
        static ChartSeries WithLevel(IReadOnlyList<Ohlcv> bars)
        {
            var v = Volume(bars);
            v.Levels.Add(new LevelConfig { Name = "Level 1", Value = 100_500, IsVisible = true });
            return v;
        }

        var h = new Harness();
        var b100 = Bars(100);
        h.Store.EmitState(State(b100, WithLevel(b100)));   // seeded BELOW: bar 99 = 100,000
        h.Bus.Publish(new RedrawEvent());

        var b101 = Bars(101);
        h.CloseBar(State(b101, WithLevel(b101)), b101[99], b101[100]);   // 100,000 closes, still below
        var b102 = Bars(102);
        var said = h.CloseBar(State(b102, WithLevel(b102)), b102[100], b102[101]);   // 101,000 closes: above

        string one = Assert.Single(said);
        Assert.Contains("crossed above level 1", one, StringComparison.OrdinalIgnoreCase);
        // ...and the reading still follows the crossing rather than being replaced by it.
        Assert.Contains("Volume 101,000", one, StringComparison.Ordinal);
    }

    [Fact]
    public void PressingN_onVolume_saysWhatNarratingWillDo()
    {
        var b100 = Bars(100);
        var state = State(b100, Volume(b100, narrated: false));
        var bus = new SpyEventBus();

        SeriesReducer.Reduce(state, new ToggleNarrationAction(CoreSeriesIds.Volume), bus);

        var said = Assert.Single(bus.Log.OfType<AnnouncementEvent>()).Message;
        Assert.StartsWith("Volume, narrating.", said, StringComparison.Ordinal);
        Assert.Contains("bar close", said, StringComparison.OrdinalIgnoreCase);
        // The previous wording, now false, must not survive.
        Assert.DoesNotContain("no signals", said, StringComparison.OrdinalIgnoreCase);
    }
}
