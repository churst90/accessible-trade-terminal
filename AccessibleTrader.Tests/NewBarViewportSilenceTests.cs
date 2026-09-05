using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// Cody, 2026-09-05: "when a new bar announcement comes in, I don't hear the viewport
/// announcement too — 'viewing 100 bars from x to y' — then the new bar data."
///
/// <para>
/// The mechanism: when the viewport is showing the live edge, <c>ViewportReducer.UpdateData</c>
/// advances <c>ViewportStartIndex</c> by one on every appended bar so the newest bar stays in
/// view, and leaves the cursor where it was. The coordinator's viewport policy read that as a
/// PAN — start moved, cursor did not — and spoke the whole range, with interrupt, on every bar
/// close. The feed scrolling the chart is not the user moving it.
/// </para>
/// </summary>
public sealed class NewBarViewportSilenceTests
{
    private static List<Ohlcv> Bars(int n) => Enumerable.Range(0, n)
        .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddHours(i), 100 + i, 101 + i, 99 + i, 100.5 + i, 10))
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
    }

    private static WorkspaceState State(int bars, int viewportStart, int cursor) => WorkspaceState.Initial with
    {
        Data = new TimeSeriesBuffer<Ohlcv>(Bars(bars)),
        ActiveSeries = ImmutableList<ChartSeries>.Empty,
        CurrentDataIndex = cursor,
        ViewportStartIndex = viewportStart,
        ViewportLength = 100,
        InitStatus = InitializationStatus.Ready,
        DataStatus = DataStatus.Ready,
        IsSpeechEnabled = true,
        AnnounceNewBars = true,
    };

    [Fact]
    public void ALiveBarThatScrollsTheViewport_DoesNotSpeakTheRange()
    {
        var h = new Harness();
        h.Store.EmitState(State(bars: 100, viewportStart: 0, cursor: 50));
        h.Spoken.Clear();

        // What the reducer produces for one appended bar with the viewport at the live edge:
        // one more bar, the window slid by one, the cursor untouched.
        h.Store.EmitState(State(bars: 101, viewportStart: 1, cursor: 50));
        h.Bus.Publish(new NewBarEvent(Bars(101)[99], Bars(101)[100]));

        Assert.DoesNotContain(h.Spoken, s => s.StartsWith("Viewing ", StringComparison.Ordinal));
        Assert.Contains(h.Spoken, s => s.Contains("Close ", StringComparison.Ordinal));
    }

    [Fact]
    public void APan_StillSpeaksTheRange_TheVacuityFloor()
    {
        // Same shape minus the new bar: the user moved the window, and that IS worth a sentence.
        var h = new Harness();
        h.Store.EmitState(State(bars: 100, viewportStart: 0, cursor: 50));
        h.Spoken.Clear();

        h.Store.EmitState(State(bars: 100, viewportStart: 1, cursor: 50));

        Assert.Contains(h.Spoken, s => s.StartsWith("Viewing ", StringComparison.Ordinal));
    }

    [Fact]
    public void OlderHistoryArriving_DoesNotSpeakTheRangeEither()
    {
        // A prepend shifts the start by the number of bars added so the user stays on the same
        // bar. Nothing the user sees moved, so nothing to say.
        var h = new Harness();
        h.Store.EmitState(State(bars: 100, viewportStart: 40, cursor: 60));
        h.Spoken.Clear();

        h.Store.EmitState(State(bars: 150, viewportStart: 90, cursor: 110));

        Assert.DoesNotContain(h.Spoken, s => s.StartsWith("Viewing ", StringComparison.Ordinal));
    }
}
