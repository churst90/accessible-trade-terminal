using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    public class RobustnessTestSuite
    {
        [Fact]
        public async Task RapidNavigation_ShouldNotExceedEmissionCount()
        {
            // Arrange
            var bus = new SpyEventBus();
            var mainThread = new MockMainThreadService();
            var speech = new CounterSpeechManager();
            var sonify = new MockNavigationSonifier();
            var store = new MockWorkspaceStore();

            var ohlcvData = new TimeSeriesBuffer<Ohlcv>(
                new Ohlcv(DateTime.UtcNow, 100, 110, 90, 105, 1000),
                new Ohlcv(DateTime.UtcNow.AddHours(1), 105, 115, 95, 110, 1200),
                new Ohlcv(DateTime.UtcNow.AddHours(2), 110, 120, 100, 115, 1400));

            var formatter = new SpeechFormatter();
            var speechRouter = new SpeechFeedbackRouter(speech, formatter, store);
            var audioRouter = new AudioFeedbackRouter(sonify, new MockEarconService());
            var navMgr = new NavigationFeedbackManager(speechRouter, formatter, bus, sonify, new MockIndicatorEngine());

            var coordinator = new AccessibilityFeedbackCoordinator(
                store,
                navMgr,
                speechRouter,
                audioRouter,
                formatter,
                bus,
                new MockEarconService(),
                new SdkCandlePatternAnalyzer(),
                new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())), new ChartPatternFocus(),
                new MockAutoNarrationService());

            // Prime
            var initialState = WorkspaceState.Initial with { Data = ohlcvData, CurrentDataIndex = 0 };
            store.EmitState(initialState);
            await Task.Delay(50);
            mainThread.RunAll();

            int speechBefore = speech.SpeakCalls;

            // Act: Emit 3 rapid state changes via store
            for (int i = 0; i < 3; i++)
            {
                store.EmitState(initialState with { CurrentDataIndex = i });
                await Task.Delay(10);
            }

            // Wait for debounce cooling
            await Task.Delay(150);
            mainThread.RunAll();

            int totalSpeechCalls = speech.SpeakCalls - speechBefore;

            // Assert: Speech calls should not exceed emission count (no infinite loops)
            Assert.True(totalSpeechCalls <= 3,
                $"Expected debounced speech (<=3 calls) for 3 rapid navigations, but got {totalSpeechCalls}");
        }
    }
}




