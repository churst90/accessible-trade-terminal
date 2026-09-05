using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// ONE UTTERANCE DESCRIBES A BAR CLOSE — the new-bar announcement and the indicator narration
/// together, in that order, in one breath.
///
/// <para>
/// Reported by Cody, 2026-09-05: <i>"even if narration is on for several series, only the newest
/// candle is actually read out on the print of a new bar. I should be hearing the indicator
/// narration ladder."</i> Reproduced here before it was fixed, and it was TWO defects wearing
/// one symptom:
/// </para>
///
/// <list type="number">
///   <item><b>Two services were speaking about the same moment.</b>
///         <c>AccessibilityFeedbackCoordinator.OnNewBar</c> spoke the instant the store
///         committed the bar; <c>AutoNarrationService</c> spoke on the <c>RedrawEvent</c> that
///         followed the recalculation. On the web head speech is an ARIA live region, so the
///         second write replaces the first before a screen reader announces it — the same defect
///         <c>NavigationFeedbackManager</c>'s "one utterance per bar" composition was written
///         for, arriving on the other route.</item>
///   <item><b>The first bar to close after switching narration on could never speak.</b>
///         <c>_seedBarCounts[id]</c> was the BAR COUNT, and the first bar to close is index
///         <c>count - 1</c> — one below the seed, so the scan skipped it. The one bar a user is
///         listening for when they press N was the one bar that was structurally silent.</item>
/// </list>
/// </summary>
public sealed class NewBarNarrationCompositionTests
{
    private static List<Ohlcv> Bars(int n) => Enumerable.Range(0, n)
        .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddDays(i), 100 + i, 101 + i, 99 + i, 100.5 + i, 10))
        .ToList();

    /// <summary>
    /// The real wiring: coordinator and narrator sharing one router, as DI builds them.
    /// </summary>
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

        /// <summary>Close the last bar and open a new one, the way the store does it: commit the
        /// appended bar, publish NewBarEvent, then the recalculation's RedrawEvent.</summary>
        public List<string> CloseBar(WorkspaceState after, Ohlcv closed, Ohlcv opened)
        {
            Spoken.Clear();
            Store.EmitState(after);
            Bus.Publish(new NewBarEvent(closed, opened));
            Bus.Publish(new RedrawEvent());
            return Spoken.ToList();
        }
    }

    /// <summary>A narrated marker series whose dot is stamped by the recalculation that runs when
    /// the bar closes — so it is absent while that bar is still forming, which is how a live
    /// indicator actually behaves.</summary>
    private static (SeriesConfig Cfg, Func<int, ChartSeries> Build) MarkerSeries(params int[] signalBars)
    {
        var cfg = new SeriesConfig
        {
            Id = "cb", Name = "Cipher B", FriendlyName = "Cipher B", IndicatorCode = "CIPHER_B",
            Pane = "Pane_CIPHER_B", IsAutoNarrated = true, IsVisible = true,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Gold", DisplayName = "Triple Confluence Buy",
            DisplayType = ComponentDisplayType.Dot, IsVisible = true,
            SignalSpeechTemplate = "Triple confluence buy, strong confirmation",
        });

        return (cfg, bars =>
        {
            var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
            var d = new double[bars];
            Array.Fill(d, double.NaN);
            // Stamped only once the bar is behind the live edge.
            foreach (int b in signalBars) if (b < bars - 1) d[b] = 1.0;
            buf.ComponentData["Gold"] = d;
            return new ChartSeries(cfg, buf);
        });
    }

    private static WorkspaceState State(ChartSeries series, int bars) => WorkspaceState.Initial with
    {
        Data = new TimeSeriesBuffer<Ohlcv>(Bars(bars)),
        ActiveSeries = ImmutableList.Create(series),
        CurrentDataIndex = bars - 1,
        InitStatus = InitializationStatus.Ready,
        DataStatus = DataStatus.Ready,
        IsSpeechEnabled = true,
        AnnounceNewBars = true,
    };

    [Fact]
    public void TheCandleAndTheSignalAreOneUtterance_NotTwo()
    {
        var (_, build) = MarkerSeries(99);
        var h = new Harness();
        h.Store.EmitState(State(build(100), 100));   // bar 99 forming; narration seeds here
        h.Bus.Publish(new RedrawEvent());

        var said = h.CloseBar(State(build(101), 101), Bars(101)[99], Bars(101)[100]);

        string one = Assert.Single(said);
        Assert.Contains("Close 199.50", one, StringComparison.Ordinal);
        Assert.Contains("Triple confluence buy", one, StringComparison.OrdinalIgnoreCase);
        // Order: the bar first, then what the indicators made of it.
        Assert.True(one.IndexOf("Close 199.50", StringComparison.Ordinal)
                    < one.IndexOf("Triple confluence", StringComparison.OrdinalIgnoreCase),
            $"the new-bar sentence must lead — got \"{one}\"");
    }

    [Fact]
    public void TheFirstBarToCloseAfterSwitchingNarrationOn_Speaks()
    {
        // The seeding defect. Narration is switched on with bar 99 forming, and bar 99 is the
        // very next thing to close — historical bars are 0..98 and nothing else.
        var (_, build) = MarkerSeries(99);
        var h = new Harness();
        h.Store.EmitState(State(build(100), 100));
        h.Bus.Publish(new RedrawEvent());

        var said = h.CloseBar(State(build(101), 101), Bars(101)[99], Bars(101)[100]);

        Assert.Contains("Triple confluence buy", Assert.Single(said), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ASignalOnABarThatClosedBeforeNarrationWasSwitchedOn_IsNotReplayed()
    {
        // The vacuity partner for the seed fix, and the property the seed exists for: pressing N
        // must not read out the history of the chart.
        var (_, build) = MarkerSeries(50, 97);
        var h = new Harness();
        h.Store.EmitState(State(build(100), 100));
        h.Bus.Publish(new RedrawEvent());

        var said = h.CloseBar(State(build(101), 101), Bars(101)[99], Bars(101)[100]);

        string one = Assert.Single(said);
        Assert.Contains("Close 199.50", one, StringComparison.Ordinal);
        Assert.DoesNotContain("Triple confluence", one, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithNothingNarrating_TheNewBarSentenceIsStillSpoken()
    {
        // The composition must not become a dependency: AnnounceNewBars answers to its own
        // switch, and a chart with no narrated series still says what the bar did.
        var (cfg, build) = MarkerSeries(99);
        cfg.IsAutoNarrated = false;

        var h = new Harness();
        h.Store.EmitState(State(build(100), 100));
        h.Bus.Publish(new RedrawEvent());

        var said = h.CloseBar(State(build(101), 101), Bars(101)[99], Bars(101)[100]);

        Assert.Contains("Close 199.50", Assert.Single(said), StringComparison.Ordinal);
    }

    [Fact]
    public void WithTheNarrationMasterSwitchOff_TheNewBarSentenceIsStillSpoken()
    {
        // The other half: the series is flagged, but "Narrate signals on bar close" is off. The
        // narrator will not run, so the coordinator must speak for itself rather than handing
        // the sentence to something that has already decided to stay quiet.
        var (_, build) = MarkerSeries(99);
        var h = new Harness();
        var seed = State(build(100), 100) with { NarrateSignalsOnBarClose = false };
        h.Store.EmitState(seed);
        h.Bus.Publish(new RedrawEvent());

        var said = h.CloseBar(State(build(101), 101) with { NarrateSignalsOnBarClose = false },
                              Bars(101)[99], Bars(101)[100]);

        string one = Assert.Single(said);
        Assert.Contains("Close 199.50", one, StringComparison.Ordinal);
        Assert.DoesNotContain("Triple confluence", one, StringComparison.OrdinalIgnoreCase);
    }
}
