using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    public class IntegrationDiagnosticTests
    {
        [Fact]
        public async Task System_ShouldRespondToNavigationFeedbackEvents()
        {
            // Arrange
            // Navigation feedback is now driven exclusively by FeedbackRequestEvent (not by OnStateChanged).
            // This prevents the double-announcement race where the second call interrupts the first mid-sentence.
            var eventBus = new SpyEventBus();
            var mainThread = new MockMainThreadService();
            var speech = new CounterSpeechManager();
            var sonify = new MockNavigationSonifier();
            var store = new MockWorkspaceStore();

            var ohlcvData = new TimeSeriesBuffer<Ohlcv>(
                new Ohlcv(DateTime.UtcNow, 100, 110, 90, 105, 1000),
                new Ohlcv(DateTime.UtcNow.AddHours(1), 105, 115, 95, 110, 1200));

            var formatter = new SpeechFormatter();
            var speechRouter = new SpeechFeedbackRouter(speech, formatter, store);
            var audioRouter = new AudioFeedbackRouter(sonify, new MockEarconService());
            var navMgr = new MockNavManager();

            // Prime the store state so the coordinator has data.
            var initialState = WorkspaceState.Initial with {
                Data = ohlcvData,
                CurrentDataIndex = 1,
                DataStatus = DataStatus.Ready,
                InitStatus = InitializationStatus.Ready
            };
            store.EmitState(initialState);

            var coordinator = new AccessibilityFeedbackCoordinator(
                store,
                navMgr,
                speechRouter,
                audioRouter,
                formatter,
                eventBus,
                new MockEarconService(),
                new SdkCandlePatternAnalyzer(),
                new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())), new ChartPatternFocus(),
                new MockAutoNarrationService());

            // Act: NavigationEngine publishes FeedbackRequestEvent(Navigation) after each move.
            // This is the single authoritative path for navigation feedback.
            eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, null, true, IsXMove: true));
            await Task.Delay(50);
            mainThread.RunAll();

            // Assert: The coordinator forwarded the event to the nav manager.
            Assert.True(navMgr.HandleNavigationCalls > 0,
                $"Expected HandleNavigationFeedback to be called via FeedbackRequestEvent, but got {navMgr.HandleNavigationCalls} calls");
        }
    }
}




