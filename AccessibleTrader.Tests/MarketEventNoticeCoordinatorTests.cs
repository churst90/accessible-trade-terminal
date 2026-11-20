using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The live notice reaches the trader on the alerts tier: speech on
/// <see cref="SpeechChannel.Event"/> (Shift+F2 mutes it, F2 does not), non-interrupting, and the
/// same market-event bell as the bar.
/// </summary>
public class MarketEventNoticeCoordinatorTests
{
    private sealed class RecordingSpeechRouter : ISpeechFeedbackRouter
    {
        public readonly List<(string Text, bool Interrupt, SpeechChannel Channel)> Spoken = new();

        public void Speak(string message, bool interrupt = true, SpeechChannel channel = SpeechChannel.Chart)
            => Spoken.Add((message, interrupt, channel));

        public void SpeakPoint(WorkspaceState state, WorkspaceState? previousState, ChartSeries series,
            Ohlcv point, string prefix = "") { }
        public void SpeakProfile(WorkspaceState state, WorkspaceState? previousState, ChartSeries series,
            int binIndex, string prefix = "") { }
        public void SpeakHeatmap(WorkspaceState state, WorkspaceState? previousState, ChartSeries series,
            int dataIndex, int binIndex, string prefix = "") { }
    }

    [Fact]
    public void ANoticeIsSpokenOnTheEventChannelWithoutInterruptingAndRingsTheBell()
    {
        var store = new MockWorkspaceStore();
        var bus = new SpyEventBus();
        var speech = new RecordingSpeechRouter();
        var formatter = new SpeechFormatter();
        var nav = Substitute.For<INavigationSonifier>();

        using var coordinator = new AccessibilityFeedbackCoordinator(
            store,
            new NavigationFeedbackManager(speech, formatter),
            speech,
            new AudioFeedbackRouter(nav, new MockEarconService()),
            formatter,
            bus,
            new MockEarconService(),
            new SdkCandlePatternAnalyzer(),
            new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
            new ChartPatternFocus(),
            new MockAutoNarrationService(),
            navigationSonifier: nav);
        store.EmitState(WorkspaceState.Initial);

        bus.Publish(new MarketEventNoticeEvent(
            "CPI in 15 minutes.", MarketEventImportance.High, "cpi-2026-03-11"));

        var said = Assert.Single(speech.Spoken);
        Assert.Equal("CPI in 15 minutes.", said.Text);
        Assert.Equal(SpeechChannel.Event, said.Channel);
        Assert.False(said.Interrupt);
        nav.Received(1).PlayMarketEventBell(
            Arg.Any<WorkspaceState>(), MarketEventImportance.High);
    }

    [Fact]
    public void AHeadlineIsSpokenOnTheEventChannelWithItsOwnSoundNotTheBell()
    {
        var store = new MockWorkspaceStore();
        var bus = new SpyEventBus();
        var speech = new RecordingSpeechRouter();
        var formatter = new SpeechFormatter();
        var nav = Substitute.For<INavigationSonifier>();

        using var coordinator = new AccessibilityFeedbackCoordinator(
            store,
            new NavigationFeedbackManager(speech, formatter),
            speech,
            new AudioFeedbackRouter(nav, new MockEarconService()),
            formatter,
            bus,
            new MockEarconService(),
            new SdkCandlePatternAnalyzer(),
            new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
            new ChartPatternFocus(),
            new MockAutoNarrationService(),
            navigationSonifier: nav);
        store.EmitState(WorkspaceState.Initial);

        bus.Publish(new HeadlineEvent(
            "AAPL filed an 8-K: earnings results.", MarketEventImportance.High, "edgar|r", "AAPL"));

        var said = Assert.Single(speech.Spoken);
        Assert.Equal("AAPL filed an 8-K: earnings results.", said.Text);
        Assert.Equal(SpeechChannel.Event, said.Channel);
        Assert.False(said.Interrupt);
        nav.Received(1).PlayHeadlineSound(Arg.Any<WorkspaceState>(), MarketEventImportance.High);
        nav.DidNotReceive().PlayMarketEventBell(Arg.Any<WorkspaceState>(), Arg.Any<MarketEventImportance>());
    }
}
