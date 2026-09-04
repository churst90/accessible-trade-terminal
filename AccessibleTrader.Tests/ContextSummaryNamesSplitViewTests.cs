using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// Shift+F1 is the orientation key — the one a disoriented user presses to ask "where am I?" —
/// and with split view on it answered as if the second chart on the canvas were not there
/// (Cody, 2026-09-04).
///
/// <para>The secondary pane is a READ-ONLY reference view: keyboard, speech, sonification and
/// trading all stay on the active tab. That is a reasonable design and an invisible one. Split
/// view announces itself when you toggle it, but a toggle is heard once and this key is how you
/// ask again — so the summary now says the split is on, which chart is in the other half, and
/// which of the two the keys are driving.</para>
/// </summary>
public class ContextSummaryNamesSplitViewTests
{
    private static TabSnapshot Snapshot(int index, string symbol, string timeframe)
    {
        var bars = Enumerable.Range(0, 10)
            .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddDays(i), 100, 101, 99, 100, 10))
            .ToList();
        return new TabSnapshot(
            TabIndex: index,
            Identity: new ChartIdentity("Spot", "Binance", symbol, timeframe),
            Data: new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries: ImmutableList<ChartSeries>.Empty,
            FocusedSeriesIndex: 0, FocusedSeriesId: null, FocusedComponentIndex: 0,
            FocusedBinIndex: -1, CurrentDataIndex: 9, ViewportStartIndex: 0, ViewportLength: 10,
            RightMarginBars: 10, ViewportRange: (99, 101),
            PaneRanges: ImmutableDictionary<string, (double, double)>.Empty,
            IsHeikinAshi: false, IsLogScale: false,
            LastInteractionContext: InteractionContext.Series,
            PaneHeightRatios: null, IndicatorPaneScrollIndex: 0,
            InitStatus: InitializationStatus.Ready, DataStatus: DataStatus.Ready,
            IsCoordinateEntryMode: false, PendingDrawingTool: null,
            CoordinateEntryAnchorCount: 0, CoordinateEntryAnchor1Index: -1);
    }

    private static (SpyEventBus Bus, SpySpeechRouter Speech, SplitViewCoordinator Split) Build()
    {
        var bus = new SpyEventBus();
        var speech = new SpySpeechRouter();
        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Spot", "Binance", "BTC/USD", "1h"),
            ActiveTabIndex = 0,
            TabSnapshots = ImmutableList.Create(Snapshot(1, "ETH/USD", "4h")),
        });

        var split = new SplitViewCoordinator(renderer: null);
        _ = new AccessibilityFeedbackCoordinator(
            store, new MockNavManager(), speech, new MockAudioRouter(), new SpeechFormatter(),
            bus, new MockEarconService(), new SdkCandlePatternAnalyzer(),
            new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
            new ChartPatternFocus(), new MockAutoNarrationService(),
            splitView: split);
        return (bus, speech, split);
    }

    [Fact]
    public void WithSplitViewOff_TheSummarySaysNothingAboutIt()
    {
        // The negative first, because it is the one a "always mention split view" implementation
        // would fail and an "already correct" reading would not notice.
        var (bus, speech, _) = Build();

        bus.Publish(new ContextSummaryRequestEvent());

        Assert.DoesNotContain("Split view", Assert.Single(speech.SpokenTexts));
    }

    [Fact]
    public void WithSplitViewOn_TheSummaryNamesTheOtherChartAndWhoOwnsTheKeyboard()
    {
        var (bus, speech, split) = Build();
        split.Toggle(new MockWorkspaceStore().State with
        {
            ActiveTabIndex = 0,
            TabSnapshots = ImmutableList.Create(Snapshot(1, "ETH/USD", "4h")),
        });
        speech.SpokenTexts.Clear();

        bus.Publish(new ContextSummaryRequestEvent());

        string said = Assert.Single(speech.SpokenTexts);
        Assert.Contains("Split view on", said);
        Assert.Contains("ETH/USD 4h", said);
        Assert.Contains("reference only", said);
        Assert.Contains("the keyboard is on this chart", said);
        // The active chart is still the FIRST thing said: split view is context, not the answer.
        Assert.StartsWith("BTC/USD", said);
    }
}
