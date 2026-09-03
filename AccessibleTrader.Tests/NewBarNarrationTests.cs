using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// The new-bar announcement (<c>AnnounceNewBars</c>): "Close X[, candle pattern]. [chart pattern
/// outcome.] New bar: Open Y".
///
/// <para>
/// Two things landed here on 2026-09-03. A CHART pattern whose story ends on the bar that just
/// closed — a neckline closed through, a triangle aged out intact — is spoken as part of the
/// announcement, in the same words the arrow keys use on that bar. And the candle analyser is
/// handed the closed bar's real predecessors: the store commits the appended bar BEFORE it
/// publishes the event, so <c>Data[^2]</c> is the closed bar itself, and the old
/// <c>prev = Data[^2]</c> tested an engulfing pattern against its own bar.
/// </para>
/// </summary>
public sealed class NewBarNarrationTests
{
    private static readonly List<Ohlcv> Bars = Enumerable.Range(0, 100)
        .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddDays(i), 100 + i, 101 + i, 99 + i, 100.5 + i, 10))
        .ToList();

    private sealed class FixedPatterns : IChartPatternCache
    {
        private readonly IReadOnlyList<ChartPattern> _all;
        public FixedPatterns(params ChartPattern[] all) => _all = all;
        public IReadOnlyList<ChartPattern> For(ChartIdentity identity, IReadOnlyList<Ohlcv>? bars) => _all;
    }

    /// <summary>Records what it was asked and answers "nothing special".</summary>
    private sealed class RecordingAnalyzer : ISdkCandlePatternAnalyzer
    {
        public (Ohlcv Current, Ohlcv? Prev, Ohlcv? Prev2, IReadOnlyList<Ohlcv>? Recent)? Call;
        public CandleAnalysis Analyze(Ohlcv current, Ohlcv? previous = null, Ohlcv? twoBarsAgo = null, IReadOnlyList<Ohlcv>? recent = null)
        {
            Call = (current, previous, twoBarsAgo, recent);
            return new CandleAnalysis
            {
                Direction = CandleDirection.Bullish, Type = CandleType.Normal, Pattern = CandlePattern.None,
                PatternBarCount = 1, BodyPercent = 50, UpperWickPercent = 25, LowerWickPercent = 25,
                ChangePercent = 0, IsReversal = false, IsContinuation = false,
            };
        }
    }

    private static (SpyEventBus Bus, List<string> Spoken, RecordingAnalyzer Analyzer) Build(
        bool describePatterns, params ChartPattern[] patterns)
    {
        var store = new MockWorkspaceStore();
        var bus = new SpyEventBus();
        var speech = new CounterSpeechManager();
        var spoken = new List<string>();
        speech.OnSpeak = t => spoken.Add(t);
        var formatter = new SpeechFormatter();
        var speechRouter = new SpeechFeedbackRouter(speech, formatter, store);
        var analyzer = new RecordingAnalyzer();

        _ = new AccessibilityFeedbackCoordinator(
            store,
            new NavigationFeedbackManager(speechRouter, formatter),
            speechRouter,
            new AudioFeedbackRouter(new MockNavigationSonifier(), new MockEarconService()),
            formatter,
            bus,
            new MockEarconService(),
            analyzer,
            new FixedPatterns(patterns),
            new ChartPatternFocus(),
            new MockAutoNarrationService());

        // The state AS THE STORE PUBLISHES IT: the new bar already appended, so the closed bar
        // is Data[^2].
        store.EmitState(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Bars.ToArray()),
            CurrentDataIndex = Bars.Count - 1,
            AnnounceNewBars = true,
            DescribeChartPatterns = describePatterns,
        });
        return (bus, spoken, analyzer);
    }

    private static ChartPattern ResolvingAt(int bar, ChartPatternState state) => new(
        Kind: ChartPatternKind.DoubleTop,
        State: state,
        StartBarIndex: 40, EndBarIndex: 90, KnownAtIndex: 92,
        TriggerLevel: 150, StartTime: default, EndTime: default,
        CompletedAtIndex: state == ChartPatternState.Completed ? bar : null,
        ExpiresAtIndex: bar,
        BreaksBelow: true, MeasuredTarget: 130);

    private static void CloseBar(SpyEventBus bus) =>
        bus.Publish(new NewBarEvent(Bars[^2], Bars[^1]));   // bar 98 closed, bar 99 opened

    [Fact]
    public void TheCandleAnalyserGetsTheClosedBarsRealPredecessors()
    {
        var (bus, _, analyzer) = Build(describePatterns: false);
        CloseBar(bus);

        var call = analyzer.Call!.Value;
        Assert.Equal(Bars[98], call.Current);
        Assert.Equal(Bars[97], call.Prev);         // was Bars[98]: the closed bar as its own predecessor
        Assert.Equal(Bars[96], call.Prev2);
        Assert.Equal(99, call.Recent!.Count);      // trailing context ends AT the closed bar
        Assert.Equal(Bars[98], call.Recent[^1]);
    }

    [Fact]
    public void AChartPatternConfirmedOnTheClosedBarIsSpokenBetweenCloseAndOpen()
    {
        var (bus, spoken, _) = Build(describePatterns: true, ResolvingAt(98, ChartPatternState.Completed));
        CloseBar(bus);

        string said = Assert.Single(spoken);
        Assert.Equal(
            "Close 198.50. Double top confirmed on this close: closed below the neckline at 150.00, measured target 130.00. New bar: Open 199.00",
            said);
    }

    [Fact]
    public void AChartPatternThatAgedOutOnTheClosedBarIsSpokenAsHavingHeld()
    {
        var (bus, spoken, _) = Build(describePatterns: true, ResolvingAt(98, ChartPatternState.Expired));
        CloseBar(bus);

        string said = Assert.Single(spoken);
        Assert.Contains("Double top ends on this close without confirming — the neckline at 150.00 held.", said);
    }

    [Fact]
    public void APatternResolvingOnSomeOtherBarSaysNothing()
    {
        var (bus, spoken, _) = Build(describePatterns: true, ResolvingAt(97, ChartPatternState.Completed));
        CloseBar(bus);
        Assert.Equal("Close 198.50. New bar: Open 199.00", Assert.Single(spoken));
    }

    [Fact]
    public void WithPatternDescriptionsOff_TheOutcomeIsNotSpoken()
    {
        var (bus, spoken, _) = Build(describePatterns: false, ResolvingAt(98, ChartPatternState.Completed));
        CloseBar(bus);
        Assert.Equal("Close 198.50. New bar: Open 199.00", Assert.Single(spoken));
    }

    [Fact]
    public void ClosedBarIndex_FindsTheBarByDate_AndFallsBackToTheLastBar()
    {
        Assert.Equal(98, AccessibilityFeedbackCoordinator.ClosedBarIndex(Bars, Bars[98]));
        Assert.Equal(99, AccessibilityFeedbackCoordinator.ClosedBarIndex(Bars, Bars[99]));
        // A publisher that fires before appending: the last bar is the closed one.
        var stranger = new Ohlcv(new DateTime(2030, 1, 1), 1, 1, 1, 1, 1);
        Assert.Equal(99, AccessibilityFeedbackCoordinator.ClosedBarIndex(Bars, stranger));
        Assert.Equal(-1, AccessibilityFeedbackCoordinator.ClosedBarIndex(null, stranger));
    }
}
