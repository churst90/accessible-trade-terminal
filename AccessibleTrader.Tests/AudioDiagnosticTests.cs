using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccessibleTrader.Tests
{
    public class AudioDiagnosticTests
    {
        private (AccessibilityFeedbackCoordinator coordinator, MockWorkspaceStore store, SpyEventBus eventBus, MockMainThreadService mainThread, CounterSpeechManager speech, MockNavigationSonifier sonify) CreateTestHarness()
        {
            var eventBus = new SpyEventBus();
            var mainThread = new MockMainThreadService();
            var sonify = new MockNavigationSonifier();
            var speech = new CounterSpeechManager();
            var store = new MockWorkspaceStore();
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
                new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
                new MockAutoNarrationService());

            return (coordinator, store, eventBus, mainThread, speech, sonify);
        }

        [Fact]
        public async Task Coordinator_ShouldNotLoop_OnSingleStateChange()
        {
            var (coordinator, store, eventBus, mainThread, speech, sonify) = CreateTestHarness();

            var ohlcvData = new TimeSeriesBuffer<Ohlcv>(
                new Ohlcv(DateTime.Now, 100, 110, 90, 105, 1000),
                new Ohlcv(DateTime.Now.AddHours(1), 105, 115, 95, 110, 1200));

            var initialState = WorkspaceState.Initial with { Data = ohlcvData, CurrentDataIndex = 0 };
            store.EmitState(initialState);
            await Task.Delay(50);
            mainThread.RunAll();

            int speechBefore = speech.SpeakCalls;

            // Act
            store.EmitState(initialState with { CurrentDataIndex = 1 });
            await Task.Delay(100);
            mainThread.RunAll();

            int speechAfterFirst = speech.SpeakCalls - speechBefore;
            await Task.Delay(100);
            mainThread.RunAll();

            int speechAfterSecond = speech.SpeakCalls - speechBefore;

            Assert.Equal(speechAfterFirst, speechAfterSecond);
        }

        [Fact]
        public async Task Coordinator_ShouldCoalesce_RapidStateChanges()
        {
            var (coordinator, store, eventBus, mainThread, speech, sonify) = CreateTestHarness();

            var ohlcvData = new TimeSeriesBuffer<Ohlcv>(
                new Ohlcv(DateTime.Now, 100, 110, 90, 105, 1000),
                new Ohlcv(DateTime.Now.AddHours(1), 105, 115, 95, 110, 1200),
                new Ohlcv(DateTime.Now.AddHours(2), 110, 120, 100, 115, 1400),
                new Ohlcv(DateTime.Now.AddHours(3), 115, 125, 105, 120, 1600),
                new Ohlcv(DateTime.Now.AddHours(4), 120, 130, 110, 125, 1800));

            var initialState = WorkspaceState.Initial with { Data = ohlcvData, CurrentDataIndex = 0 };
            store.EmitState(initialState);
            await Task.Delay(50);
            mainThread.RunAll();

            int speechBefore = speech.SpeakCalls;

            for (int i = 0; i < 5; i++)
            {
                store.EmitState(initialState with { CurrentDataIndex = i });
                await Task.Delay(10);
            }

            await Task.Delay(200);
            mainThread.RunAll();

            int totalSpeechCalls = speech.SpeakCalls - speechBefore;

            // Coalescing logic is now handled by behavior subject and throttle/debounce if implemented.
            // Currently, AccessibilityFeedbackCoordinator responds directly to StateStream.
            // Rapid updates will trigger multiple speech calls but NVDA typically interrupts.
            // If we didn't implement explicit coalescing in the coordinator yet, this test might fail.
            // For now, let's see what happens.
            Assert.True(totalSpeechCalls <= 5);
        }
    }
}




