using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    public class UIDiagnosticTests
    {
        [Fact]
        public async Task UI_Feedback_ShouldTriggerOnStateChange()
        {
            // Arrange
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
            var navMgr = new NavigationFeedbackManager(speechRouter, formatter, eventBus, sonify, new MockIndicatorEngine());

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

            // Prime with initial state
            var initialState = WorkspaceState.Initial with { Data = ohlcvData, CurrentDataIndex = -1 };
            store.EmitState(initialState);
            await Task.Delay(50);
            mainThread.RunAll();

            // Act: Publish a state change event through the event bus
            eventBus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange, Message: "Test State"));
            await Task.Delay(100);
            mainThread.RunAll();

            // Assert: The coordinator should forward state change messages
            // (Actually, AccessibilityFeedbackCoordinator subscribes to FeedbackRequestEvent and speaks the message)
            Assert.Equal("Test State", speech.LastSpokenText);
        }
    }
}




