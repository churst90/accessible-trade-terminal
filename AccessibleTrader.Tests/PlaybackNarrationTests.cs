using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Playback was a speech-free island (accessibility audit 2026-09-01, §3.10). The
    /// coordinator's playback gate returned before any announcement on the strength of a
    /// comment crediting a class that has no speech router, so a Space press produced tones and
    /// not one word: no start, no pause, no resume, no stop, no finish, and the speed key was
    /// announced only while nothing was playing. These tests drive the real coordinator through
    /// the store transitions the dispatcher and the sequencer produce and assert the sentences.
    ///
    /// Each was proven red first against the pre-fix coordinator (the gate above the speed
    /// announcement; no playback block at all).
    /// </summary>
    public class PlaybackNarrationTests
    {
        // ── Fixture ─────────────────────────────────────────────────────────────

        /// <summary>Records interrupt and channel too — the landmarks must be NON-interrupting,
        /// and the shared SpySpeechRouter drops that.</summary>
        private sealed class RecordingSpeech : ISpeechFeedbackRouter
        {
            public List<(string Text, bool Interrupt, SpeechChannel Channel)> Calls { get; } = new();
            public IEnumerable<string> Texts => Calls.Select(c => c.Text);

            public void Speak(string message, bool interrupt = true, SpeechChannel channel = SpeechChannel.Manual)
                => Calls.Add((message, interrupt, channel));
            public void SpeakPoint(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, Ohlcv point, string prefix = "") { }
            public void SpeakProfile(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, int binIndex, string prefix = "") { }
            public void SpeakHeatmap(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, int dataIndex, int binIndex, string prefix = "") { }
        }

        private sealed class RecordingAudio : IAudioFeedbackRouter
        {
            public bool IsSonificationEnabled { get; set; } = true;
            public List<FeedbackType> Earcons { get; } = new();
            public void PlayEarcon(FeedbackType type, ErrorSeverity severity = ErrorSeverity.Medium) => Earcons.Add(type);
            public void Silence() { }
        }

        private sealed class Harness
        {
            public MockWorkspaceStore Store { get; } = new();
            public RecordingSpeech Speech { get; } = new();
            public RecordingAudio Audio { get; } = new();
            public SpyEventBus Bus { get; } = new();
            public AccessibilityFeedbackCoordinator Coordinator { get; }

            public Harness(WorkspaceState initial)
            {
                Store.EmitState(initial);
                Coordinator = new AccessibilityFeedbackCoordinator(
                    Store,
                    new MockNavManager(),
                    Speech,
                    Audio,
                    new SpeechFormatter(),
                    Bus,
                    new MockEarconService(),
                    new SdkCandlePatternAnalyzer(),
                    new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
                    new ChartPatternFocus(),
                    new MockAutoNarrationService());
            }

            /// <summary>Apply a state and forget what was spoken getting there.</summary>
            public void Settle(WorkspaceState s) { Store.EmitState(s); Speech.Calls.Clear(); }
        }

        /// <summary>Daily bars at 12:00 UTC from 1 January 2024 — noon so the user's zone cannot
        /// move a bar across midnight and change the spoken date.</summary>
        private static TimeSeriesBuffer<Ohlcv> DailyBars(int count, DateTime? from = null)
        {
            var start = from ?? new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            return new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, count)
                .Select(i => new Ohlcv(start.AddDays(i), 100 + i, 101 + i, 99 + i, 100 + i, 1000))
                .ToList());
        }

        private static ChartSeries Series(string id, string name, params string[] components)
        {
            var config = new SeriesConfig { Id = id, IndicatorCode = id, Name = name, FriendlyName = name };
            foreach (var c in components)
                config.Components.Add(new ComponentConfig { Name = c, DisplayName = c, IsVisible = true });
            var data = new SeriesDataBuffer { SeriesId = id };
            foreach (var c in components) data.ComponentData[c] = new double[100];
            return new ChartSeries(config, data);
        }

        private static WorkspaceState Loaded(int bars = 100, int cursor = 10, int viewportStart = 40)
        {
            var candles = Series("candles", "Price", "Body");
            var ema = Series("ema", "EMA 20", "EMA");
            return WorkspaceState.Initial with
            {
                Data = DailyBars(bars),
                ActiveSeries = ImmutableList.Create(candles, ema),
                PrimarySeriesId = "candles",
                FocusedSeriesId = "ema",
                FocusedComponentIndex = 0,
                CurrentDataIndex = cursor,
                ViewportStartIndex = viewportStart,
                ViewportLength = 50,
                Identity = new ChartIdentity("Spot", "Test", "BTC/USD", "1d"),
                InitStatus = InitializationStatus.Ready,
                IsPlaying = false,
                IsPaused = false,
                PlaybackScope = PlaybackScope.Chart,
                PlaybackSpeed = 1.0f,
            };
        }

        private static WorkspaceState Playing(WorkspaceState s, PlaybackScope scope = PlaybackScope.Chart)
            => s with { IsPlaying = true, PlaybackScope = scope, IsPaused = false };

        // ── Start ──────────────────────────────────────────────────────────────

        [Fact]
        public void Start_ChartScope_NamesTheScope_TheFirstBar_AndHowManyBars()
        {
            var h = new Harness(Loaded());

            h.Store.EmitState(Playing(h.Store.State));

            // Chart scope starts at the viewport's left edge: bar 40 = 10 February 2024, 60 to go.
            var call = Assert.Single(h.Speech.Calls);
            Assert.Equal("Playing chart from February 10 2024, 60 bars.", call.Text);
            Assert.True(call.Interrupt);
        }

        [Fact]
        public void Start_SeriesScope_NamesTheFocusedSeries_FromTheCursor()
        {
            var h = new Harness(Loaded(cursor: 10));

            h.Store.EmitState(Playing(h.Store.State, PlaybackScope.Series));

            Assert.Equal("Playing EMA 20 from January 11 2024, 90 bars.", Assert.Single(h.Speech.Texts));
        }

        [Fact]
        public void Start_ComponentScope_OnAOneComponentSeries_DoesNotStutterTheName()
        {
            // "EMA 20 EMA", "RSI RSI", "VWAP VWAP" — the series name already says it.
            var h = new Harness(Loaded(cursor: 98));

            h.Store.EmitState(Playing(h.Store.State, PlaybackScope.Component));

            Assert.Equal("Playing EMA 20 from April 8 2024, 2 bars.", Assert.Single(h.Speech.Texts));
        }

        [Fact]
        public void Start_ComponentScope_OnAMultiComponentSeries_NamesTheComponent()
        {
            var bands = Series("bb", "Bollinger Bands", "Upper", "Middle", "Lower");
            var s = Loaded(cursor: 98) with
            {
                ActiveSeries = ImmutableList.Create(bands),
                PrimarySeriesId = "bb", FocusedSeriesId = "bb", FocusedComponentIndex = 2,
            };
            var h = new Harness(s);

            h.Store.EmitState(Playing(h.Store.State, PlaybackScope.Component));

            Assert.Equal("Playing Bollinger Bands Lower from April 8 2024, 2 bars.", Assert.Single(h.Speech.Texts));
        }

        [Fact]
        public void Start_WithAnUnplayablePlan_SpeaksTheRefusal_NotAStart()
        {
            // The dispatcher refuses before dispatching; any other SetPlaybackAction(true)
            // caller lands here, and silence here is the original defect back again.
            var s = Loaded();
            foreach (var x in s.ActiveSeries) x.IsMuted = true;
            var h = new Harness(s);

            h.Store.EmitState(Playing(h.Store.State));

            Assert.Equal(PlaybackPlan.EverySeriesMutedReason, Assert.Single(h.Speech.Texts));
        }

        [Fact]
        public void Start_SeriesWithNoFriendlyName_FallsBackToItsName()
        {
            var s = Loaded();
            ((SeriesConfig)s.ActiveSeries[1].Config).FriendlyName = "";
            var h = new Harness(s);

            h.Store.EmitState(Playing(h.Store.State, PlaybackScope.Series));

            Assert.StartsWith("Playing EMA 20 from", Assert.Single(h.Speech.Texts));
        }

        [Fact]
        public void Start_IntradayBars_SpeaksTheTimeOfDayToo()
        {
            var hourly = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, 48)
                .Select(i => new Ohlcv(new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i), 1, 2, 0, 1, 1))
                .ToList());
            var s = Loaded() with { Data = hourly, Identity = new ChartIdentity("Spot", "Test", "BTC/USD", "1h"), ViewportStartIndex = 0 };
            var h = new Harness(s);

            h.Store.EmitState(Playing(h.Store.State));

            string expected = SpeechTimeFormatter.Format(hourly[0].Date, SpeechTimeFormatter.DateTimeFormat);
            Assert.Equal($"Playing chart from {expected}, 48 bars.", Assert.Single(h.Speech.Texts));
        }

        // ── Speed — the audit's concrete symptom ──────────────────────────────

        [Fact]
        public void SpeedChange_DuringPlayback_IsSpoken()
        {
            // Shift+= is only useful while something is playing, and that was the one time it
            // said nothing: the announcement sat below the IsPlaying gate.
            var h = new Harness(Loaded());
            h.Settle(Playing(h.Store.State));

            h.Store.EmitState(h.Store.State with { PlaybackSpeed = 1.5f });

            Assert.Equal("Playback speed: 1.5x", Assert.Single(h.Speech.Texts));
        }

        [Fact]
        public void SpeedChange_WhileIdle_IsStillSpoken()
        {
            var h = new Harness(Loaded());

            h.Store.EmitState(h.Store.State with { PlaybackSpeed = 0.9f });

            Assert.Equal("Playback speed: 0.9x", Assert.Single(h.Speech.Texts));
        }

        // ── Pause / resume ─────────────────────────────────────────────────────

        [Fact]
        public void Pause_SaysWhereTheCursorParked_AndResume_IsOneWord()
        {
            var h = new Harness(Loaded());
            h.Settle(Playing(h.Store.State) with { CurrentDataIndex = 55 });

            h.Store.EmitState(h.Store.State with { IsPaused = true });
            Assert.Equal("Paused at February 25 2024.", Assert.Single(h.Speech.Texts));

            h.Speech.Calls.Clear();
            h.Store.EmitState(h.Store.State with { IsPaused = false });
            Assert.Equal("Resumed.", Assert.Single(h.Speech.Texts));
        }

        // ── Stop / finish ──────────────────────────────────────────────────────

        [Fact]
        public void Stop_BeforeTheLastBar_SaysStopped_AtTheCursor()
        {
            var h = new Harness(Loaded());
            h.Settle(Playing(h.Store.State) with { CurrentDataIndex = 70 });

            h.Store.EmitState(h.Store.State with { IsPlaying = false });

            Assert.Equal("Playback stopped at March 11 2024.", Assert.Single(h.Speech.Texts));
        }

        [Fact]
        public void EndingOnTheLastBar_SaysFinished_WithoutInterrupting_AndWithAnEarcon()
        {
            // The sequencer walks the cursor to Count - 1 and then SonificationManager
            // dispatches SetPlaybackAction(false): that is what "the whole range sounded"
            // looks like from the store, and it must not sound like a stop or a crash. Nobody
            // pressed a key, so it queues behind a short plan's start sentence rather than
            // clipping it, and the earcon marks the end of the stream even under F2.
            var h = new Harness(Loaded(bars: 100));
            h.Settle(Playing(h.Store.State) with { CurrentDataIndex = 99 });

            h.Store.EmitState(h.Store.State with { IsPlaying = false });

            var call = Assert.Single(h.Speech.Calls);
            Assert.Equal("Playback finished at April 9 2024.", call.Text);
            Assert.False(call.Interrupt);
            Assert.Equal(FeedbackType.Boundary, Assert.Single(h.Audio.Earcons));
        }

        [Fact]
        public void Stop_BeforeTheEnd_Interrupts_AndPlaysNoEarcon()
        {
            var h = new Harness(Loaded());
            h.Settle(Playing(h.Store.State) with { CurrentDataIndex = 70 });

            h.Store.EmitState(h.Store.State with { IsPlaying = false });

            Assert.True(Assert.Single(h.Speech.Calls).Interrupt);
            Assert.Empty(h.Audio.Earcons);
        }

        [Fact]
        public void Stop_FromPaused_IsOneSentence_NotResumedThenStopped()
        {
            var h = new Harness(Loaded());
            h.Settle(Playing(h.Store.State) with { CurrentDataIndex = 70, IsPaused = true });

            // SetPlaybackAction(false) clears IsPaused in the same reduction.
            h.Store.EmitState(h.Store.State with { IsPlaying = false, IsPaused = false });

            Assert.Equal("Playback stopped at March 11 2024.", Assert.Single(h.Speech.Texts));
        }

        // ── Landmarks while running ────────────────────────────────────────────

        [Fact]
        public void Landmark_DailyBars_SpeaksEachNewMonth_WithoutInterrupting()
        {
            var h = new Harness(Loaded());
            h.Settle(Playing(h.Store.State) with { CurrentDataIndex = 29 }); // 30 January

            h.Store.EmitState(h.Store.State with { CurrentDataIndex = 30 }); // 31 January
            Assert.Empty(h.Speech.Calls);

            h.Store.EmitState(h.Store.State with { CurrentDataIndex = 31 }); // 1 February
            var call = Assert.Single(h.Speech.Calls);
            Assert.Equal("February 2024", call.Text);
            Assert.False(call.Interrupt, "a landmark must not clip the utterance before it");
        }

        [Fact]
        public void Landmark_IsNotSpokenOnTheFirstBarAfterStart()
        {
            // The start sentence already named the first bar; the first NavigateAction after it
            // compares against wherever the cursor was BEFORE playback, which is not a step.
            var h = new Harness(Loaded(cursor: 5, viewportStart: 40));
            h.Settle(Playing(h.Store.State));

            h.Store.EmitState(h.Store.State with { CurrentDataIndex = 40 });

            Assert.Empty(h.Speech.Calls);
        }

        [Fact]
        public void Landmark_SeriesScope_SpeaksOnTheFirstRealStep()
        {
            // Series and component scope start AT the cursor: the sequencer's first
            // NavigateAction moves nothing, so the step that follows is a real step and the
            // month it crosses into must be spoken. A flag cleared only by "the index moved"
            // treated that real step as the start jump and swallowed it.
            var h = new Harness(Loaded(cursor: 30)); // 31 January
            h.Settle(Playing(h.Store.State, PlaybackScope.Series));

            h.Store.EmitState(h.Store.State with { CurrentDataIndex = 30 }); // the no-op first tick
            h.Store.EmitState(h.Store.State with { CurrentDataIndex = 31 }); // 1 February

            Assert.Equal("February 2024", Assert.Single(h.Speech.Texts));
        }

        [Fact]
        public void Landmark_IsNotSpokenWhilePaused()
        {
            var h = new Harness(Loaded());
            h.Settle(Playing(h.Store.State) with { CurrentDataIndex = 30, IsPaused = true });

            // A user arrowing across the month boundary while parked is navigation, not playback.
            h.Store.EmitState(h.Store.State with { CurrentDataIndex = 31 });

            Assert.Empty(h.Speech.Calls);
        }

        // ── The gate itself must survive ───────────────────────────────────────

        [Fact]
        public void NavigationAndViewportFeedback_StayGated_DuringPlayback()
        {
            // The sequencer moves the cursor ten times a second. A viewport description or a
            // mute confirmation per tick would bury the tones; only the playback block speaks.
            var h = new Harness(Loaded());
            h.Settle(Playing(h.Store.State) with { CurrentDataIndex = 41 });

            h.Store.EmitState(h.Store.State with { CurrentDataIndex = 42, ViewportStartIndex = 41 });

            Assert.Empty(h.Speech.Calls);
        }

        [Fact]
        public void TabSwitch_AwayFromAPlayingTab_SaysNothingAboutPlayback()
        {
            // The tab label is announced by the store's dispatch; on the web head a second
            // live-region write in the same batch would replace it. The tones stopping and the
            // tab name are the whole story.
            var h = new Harness(Loaded());
            h.Settle(Playing(h.Store.State) with { CurrentDataIndex = 70, PlaybackSpeed = 2.0f });

            h.Store.EmitState(h.Store.State with { ActiveTabIndex = h.Store.State.ActiveTabIndex + 1, IsPlaying = false, PlaybackSpeed = 1.0f });

            Assert.Empty(h.Speech.Calls);
        }

        // ── Pure rules ─────────────────────────────────────────────────────────

        [Theory]
        [InlineData(60, 1.0f, PlaybackNarration.LandmarkUnit.Hour)]     // 1m: 60 bars an hour
        [InlineData(300, 1.0f, PlaybackNarration.LandmarkUnit.Day)]     // 5m: 12 an hour is too chatty
        [InlineData(3600, 1.0f, PlaybackNarration.LandmarkUnit.Day)]    // 1h: 24 a day
        [InlineData(14400, 1.0f, PlaybackNarration.LandmarkUnit.Month)] // 4h: 6 a day is too chatty
        [InlineData(86400, 1.0f, PlaybackNarration.LandmarkUnit.Month)] // 1d: ~30 a month
        [InlineData(86400, 10.0f, PlaybackNarration.LandmarkUnit.Year)] // 1d at 10x: 200 needed
        [InlineData(604800, 1.0f, PlaybackNarration.LandmarkUnit.Year)] // 1w
        [InlineData(2592000, 1.0f, PlaybackNarration.LandmarkUnit.Year)]// 1M: the ceiling
        [InlineData(3600, 0.1f, PlaybackNarration.LandmarkUnit.Day)]    // 1h at 0.1x: one bar a second
        public void UnitFor_PicksTheFinestUnitThatKeepsLandmarksTwoSecondsApart(int barSeconds, float speed, PlaybackNarration.LandmarkUnit expected)
            => Assert.Equal(expected, PlaybackNarration.UnitFor(barSeconds, speed));

        [Fact]
        public void Landmark_SpeaksOnlyWhenTheUnitBoundaryIsCrossed()
        {
            // Local-kind stamps so the assertion does not depend on the box's zone.
            var a = new DateTime(2024, 12, 31, 23, 30, 0, DateTimeKind.Local);
            var b = new DateTime(2025, 1, 1, 0, 15, 0, DateTimeKind.Local);
            var c = new DateTime(2025, 1, 1, 0, 45, 0, DateTimeKind.Local);

            Assert.Equal("00:15", PlaybackNarration.Landmark(a, b, PlaybackNarration.LandmarkUnit.Hour));
            Assert.Null(PlaybackNarration.Landmark(b, c, PlaybackNarration.LandmarkUnit.Hour));
            Assert.Equal("January 1", PlaybackNarration.Landmark(a, b, PlaybackNarration.LandmarkUnit.Day));
            Assert.Equal("January 2025", PlaybackNarration.Landmark(a, b, PlaybackNarration.LandmarkUnit.Month));
            Assert.Equal("2025", PlaybackNarration.Landmark(a, b, PlaybackNarration.LandmarkUnit.Year));
            Assert.Null(PlaybackNarration.Landmark(b, c, PlaybackNarration.LandmarkUnit.Year));
        }

        [Fact]
        public void BarSeconds_FallsBackToTheBarSpacing_WhenTheTimeframeDoesNotParse()
        {
            var s = Loaded() with { Identity = new ChartIdentity("Spot", "Test", "BTC/USD", "weird") };
            Assert.Equal(86400, PlaybackNarration.BarSeconds(s));

            var s2 = Loaded() with { Identity = new ChartIdentity("Spot", "Test", "BTC/USD", "4h") };
            Assert.Equal(14400, PlaybackNarration.BarSeconds(s2));
        }

        // ── One plan for the sound, the refusal and the sentence ───────────────

        [Fact]
        public void Plan_ChartScope_PlaysVisibleUnmutedSeries_FromTheViewportEdge()
        {
            var s = Loaded();
            var muted = s.ActiveSeries[1];
            muted.IsMuted = true;

            var plan = PlaybackPlan.Resolve(s, PlaybackScope.Chart);

            Assert.True(plan.IsPlayable);
            Assert.Equal(new[] { "candles" }, plan.Series.Select(x => x.Id));
            Assert.Equal(40, plan.StartIndex);
            Assert.Equal(-1, plan.ComponentFilter);
        }

        [Fact]
        public void Plan_SeriesAndComponentScope_StartAtTheCursor_AndPinTheComponent()
        {
            var s = Loaded(cursor: 17);

            var series = PlaybackPlan.Resolve(s, PlaybackScope.Series);
            Assert.Equal("ema", Assert.Single(series.Series).Id);
            Assert.Equal(17, series.StartIndex);
            Assert.Equal(-1, series.ComponentFilter);

            var comp = PlaybackPlan.Resolve(s with { FocusedComponentIndex = 7 }, PlaybackScope.Component);
            Assert.Equal(0, comp.ComponentFilter); // clamped into the one-component series
        }

        [Fact]
        public void Plan_RefusesWithAReason_WhenNothingWouldPlay()
        {
            var s = Loaded();
            foreach (var x in s.ActiveSeries) x.IsMuted = true;
            Assert.Equal(PlaybackPlan.EverySeriesMutedReason, PlaybackPlan.Resolve(s, PlaybackScope.Chart).RefusalReason);

            var empty = s with { ActiveSeries = ImmutableList<ChartSeries>.Empty };
            Assert.Equal(PlaybackPlan.NoSeriesReason, PlaybackPlan.Resolve(empty, PlaybackScope.Chart).RefusalReason);
            Assert.Equal(PlaybackPlan.NoSeriesReason, PlaybackPlan.Resolve(empty, PlaybackScope.Series).RefusalReason);

            var noData = s with { Data = new TimeSeriesBuffer<Ohlcv>() };
            Assert.Equal(PlaybackPlan.NoDataReason, PlaybackPlan.Resolve(noData, PlaybackScope.Chart).RefusalReason);
        }

        [Fact]
        public void Dispatcher_RefusesToStart_AndSaysWhy_WhenEverySeriesIsMuted()
        {
            // Before: SetPlaybackAction(true) was dispatched, the orchestrator found nothing to
            // play and returned, and the store said "playing" over silence — with the gate
            // engaged, so the arrow keys went quiet until the next Space "stopped" it.
            var s = Loaded();
            foreach (var x in s.ActiveSeries) x.IsMuted = true;
            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            store.EmitState(s);
            var dispatcher = new CommandDispatcher(bus, Substitute.For<INavigationEngine>(), store,
                Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));
            dispatcher.SetChartActive(true);

            dispatcher.Dispatch(SystemCommand.PlayChart);

            Assert.Empty(store.DispatchedActions);
            var feedback = Assert.Single(bus.Log.OfType<FeedbackRequestEvent>());
            Assert.Equal(FeedbackType.Boundary, feedback.Type);
            Assert.Equal(PlaybackPlan.EverySeriesMutedReason, feedback.Message);
        }

        [Theory]
        [InlineData(SystemCommand.PlayPause)]
        [InlineData(SystemCommand.PlayStop)]
        public void Dispatcher_PauseOrStop_WithNothingPlaying_SaysSo_AndDispatchesNothing(SystemCommand cmd)
        {
            // Ctrl+Space when idle used to flip IsPaused with IsPlaying false — silent, and it
            // turned the NEXT Space into a silent "stop". Shift+Escape when idle was silent.
            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            store.EmitState(Loaded());
            var dispatcher = new CommandDispatcher(bus, Substitute.For<INavigationEngine>(), store,
                Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));
            dispatcher.SetChartActive(true);

            dispatcher.Dispatch(cmd);

            Assert.Empty(store.DispatchedActions);
            var feedback = Assert.Single(bus.Log.OfType<FeedbackRequestEvent>());
            Assert.Equal(FeedbackType.Boundary, feedback.Type);
            Assert.Equal(CommandDispatcher.NothingIsPlaying, feedback.Message);
        }

        [Fact]
        public void Orchestrator_PlaysExactlyThePlan_TheSentenceWasBuiltFrom()
        {
            // If the orchestrator ever grew its own selection rule again, the announcement
            // could name a series the sequencer skipped. Assert it hands the sequencer the
            // plan's series and start bar.
            var s = Loaded();
            s.ActiveSeries[1].IsMuted = true;
            var sequencer = Substitute.For<IAudioSequencer>();
            var orchestrator = new PlaybackOrchestrator(sequencer, Substitute.For<IAudioDriver>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PlaybackOrchestrator>.Instance);

            orchestrator.StartPlayback(Playing(s));

            var plan = PlaybackPlan.Resolve(s, PlaybackScope.Chart);
            sequencer.Received(1).StartMultiSeriesPlaybackAsync(
                Arg.Is<IReadOnlyList<ChartSeries>>(l => l.Select(x => x.Id).SequenceEqual(plan.Series.Select(x => x.Id))),
                Arg.Any<List<Ohlcv>>(), plan.StartIndex, Arg.Any<CancellationToken>());
        }
    }
}
