using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Playback speaks SIGNALS now, not only time.
    ///
    /// <para>
    /// Until 2026-09-04 the only words playback produced while it ran were calendar landmarks —
    /// "February 2024" as the tones crossed a month. Cody's ask was specific and so is the
    /// answer: "hearing signals also… not RSI crossings or anything like that. So not everything
    /// spoken, just important events." What speaks is therefore <c>ScanUtterance.TierSignal</c>
    /// and nothing below it: marker components carrying a <c>SignalSpeechTemplate</c>, on series
    /// the user flagged with Ctrl+Alt+Shift+N, plus a chart pattern that resolves on the bar.
    /// </para>
    ///
    /// <para>
    /// The two rules that make it usable rather than noise are asserted here as hard as the
    /// content is: <b>one utterance per step</b> (the live-region rule — on the web head only the
    /// last write to the region in a render batch survives, and playback writes ten times a
    /// second), and a <b>bar-distance rate limit</b> that drops rather than queues.
    /// </para>
    /// </summary>
    public class PlaybackSignalSpeechTests
    {
        // ── Fixture ─────────────────────────────────────────────────────────────

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
            public void PlayEarcon(FeedbackType type, ErrorSeverity severity = ErrorSeverity.Medium) { }
            public void Silence() { }
        }

        private sealed class Harness
        {
            public MockWorkspaceStore Store { get; } = new();
            public RecordingSpeech Speech { get; } = new();

            public Harness(WorkspaceState initial)
            {
                Store.EmitState(initial);
                _ = new AccessibilityFeedbackCoordinator(
                    Store,
                    new MockNavManager(),
                    Speech,
                    new RecordingAudio(),
                    new SpeechFormatter(),
                    new SpyEventBus(),
                    new MockEarconService(),
                    new SdkCandlePatternAnalyzer(),
                    new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
                    new ChartPatternFocus(),
                    new MockAutoNarrationService());
            }

            /// <summary>Apply a state and forget what was spoken getting there.</summary>
            public void Settle(WorkspaceState s) { Store.EmitState(s); Speech.Calls.Clear(); }

            /// <summary>Step the playback cursor one bar and return what that step spoke.</summary>
            public List<string> Step(int to)
            {
                Speech.Calls.Clear();
                Store.EmitState(Store.State with { CurrentDataIndex = to });
                return Speech.Texts.ToList();
            }
        }

        /// <summary>Daily bars at noon UTC from 1 January 2024, so no time zone can move a bar
        /// across midnight and change the spoken date.</summary>
        private static TimeSeriesBuffer<Ohlcv> DailyBars(int count)
        {
            var start = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            return new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, count)
                .Select(i => new Ohlcv(start.AddDays(i), 100 + i, 101 + i, 99 + i, 100 + i, 1000)));
        }

        /// <summary>
        /// A marker series with a signal on each of <paramref name="signalBars"/>.
        /// </summary>
        private static ChartSeries MarkerSeries(
            int bars, IEnumerable<int> signalBars,
            bool narrated = true, bool visible = true, bool muted = false,
            string? template = "{name} at {price}",
            ComponentDisplayType display = ComponentDisplayType.Dot)
        {
            var cfg = new SeriesConfig
            {
                Id = "cipher",
                IndicatorCode = "CIPHER_B",
                Name = "CipherB",
                FriendlyName = "Cipher B",
                IsAutoNarrated = narrated,
                IsVisible = true,
                IsMuted = false,
            };
            cfg.Components.Add(new ComponentConfig
            {
                Name = "Signal",
                DisplayName = "Bull signal",
                DisplayType = display,
                IsVisible = visible,
                IsMuted = muted,
                SignalSpeechTemplate = template,
            });

            var data = new double[bars];
            Array.Fill(data, double.NaN);
            foreach (int i in signalBars) data[i] = 1.0;

            var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
            buf.ComponentData["Signal"] = data;
            return new ChartSeries(cfg, buf);
        }

        /// <summary>
        /// A chart already playing, cursor parked on <paramref name="cursor"/>, with the first
        /// jump already consumed — every emission after this is a real step.
        /// </summary>
        private static WorkspaceState Playing(ChartSeries series, int cursor, int bars = 120,
            bool narratePlayback = true, float speed = 1.0f)
            => WorkspaceState.Initial with
            {
                Data = DailyBars(bars),
                ActiveSeries = ImmutableList.Create(series),
                PrimarySeriesId = "cipher",
                FocusedSeriesId = "cipher",
                CurrentDataIndex = cursor,
                ViewportStartIndex = cursor,
                ViewportLength = 50,
                Identity = new ChartIdentity("Spot", "Test", "BTC/USD", "1d"),
                InitStatus = InitializationStatus.Ready,
                IsPlaying = true,
                IsPaused = false,
                PlaybackScope = PlaybackScope.Series,
                PlaybackSpeed = speed,
                NarrateDuringPlayback = narratePlayback,
            };

        private static Harness Running(ChartSeries series, int cursor, int bars = 120,
            bool narratePlayback = true, float speed = 1.0f)
        {
            var state = Playing(series, cursor, bars, narratePlayback, speed);
            // Series scope starts AT the cursor, so the sequencer's first NavigateAction moves
            // nothing; emitting the not-playing state first and then the playing one reproduces
            // the real start transition, including the start sentence we then discard.
            var h = new Harness(state with { IsPlaying = false });
            h.Settle(state);
            return h;
        }

        // ── Content: which components speak ─────────────────────────────────────

        [Fact]
        public void ASignalOnTheBarSteppedOnto_IsSpoken_AsItself()
        {
            var h = Running(MarkerSeries(120, new[] { 41 }), cursor: 40);

            var spoken = Assert.Single(h.Step(41));

            // NO SERIES PREFIX with one clause. Cody, 2026-09-04: "during playback only the
            // signal itself should be read, not prefixed with everything". The prefix exists to
            // stop two clauses in one breath being heard as one indicator's; with one clause
            // there is nothing to confuse it with, and at ten bars a second a fixed phrase ahead
            // of every signal is the loudest thing in the stream carrying the least information.
            // The disambiguating case is still guarded — see the two-series tests below.
            Assert.Equal("Bull signal at 141.00.", spoken);
        }

        [Fact]
        public void TwoSeriesFiringOnOneBar_AreIntroducedByTheirComponents_NeverTheirSeries()
        {
            // Until 2026-09-05 this was the case the SERIES prefix was written for: two clauses in
            // one breath from two indicators each carried their series' name. Cody: "hearing only
            // the component name before the signal is all that is needed, not the series name as
            // the user probably knows what they enabled for narration". Narration is opt-in per
            // series — which series speak is a fact the listener chose — and the component is the
            // fact they are waiting for. Here both templates already name their component
            // ("{name} at {price}"), so nothing is added and nothing is said about the series.
            // CHART scope, not series scope: since 2026-09-04 narration is scoped exactly the
            // way the tones are, so a SERIES-scoped playback of Cipher B cannot say anything
            // about Cipher SR. The scoping itself is pinned by PlaybackNarrationScopeTests.
            var state = Playing(MarkerSeries(120, new[] { 41 }), cursor: 40)
                        with { PlaybackScope = PlaybackScope.Chart };

            var cfg = new SeriesConfig
            {
                Id = "cipher_sr", IndicatorCode = "CIPHER_SR",
                Name = "CipherSR", FriendlyName = "Cipher SR",
                IsAutoNarrated = true, IsVisible = true, IsMuted = false,
            };
            cfg.Components.Add(new ComponentConfig
            {
                Name = "Signal", DisplayName = "Support test",
                DisplayType = ComponentDisplayType.Dot, IsVisible = true,
                SignalSpeechTemplate = "{name} at {price}",
            });
            var arr2 = new double[120];
            Array.Fill(arr2, double.NaN);
            arr2[41] = 1.0;
            var buf2 = new SeriesDataBuffer { SeriesId = cfg.Id };
            buf2.ComponentData["Signal"] = arr2;
            var second = new ChartSeries(cfg, buf2);

            state = state with { ActiveSeries = state.ActiveSeries.Add(second) };

            var h = new Harness(state with { IsPlaying = false });
            h.Settle(state);

            string spoken = Assert.Single(h.Step(41));
            Assert.Equal("Bull signal at 141.00. Support test at 141.00.", spoken);
            Assert.DoesNotContain("Cipher", spoken);
        }

        [Fact]
        public void ATemplateThatDoesNotNameItsComponent_IsIntroducedByIt()
        {
            // The other half of the rule. A template that does not say which marker fired —
            // Cipher B's "Wave cross up {value}" is the shipped example — gets its component
            // in front, because that is the fact the listener is waiting for. The match is
            // case-insensitive and anywhere in the clause, so "{name} at {price}" templates
            // and hand-written ones that mention the component mid-sentence are left alone.
            var series = MarkerSeries(120, new[] { 41 });
            series.Config.Components[0].DisplayName = "WaveTrend Cross Bull";
            series.Config.Components[0].SignalSpeechTemplate = "Wave cross up {value}";

            var h = Running(series, cursor: 40);

            string spoken = Assert.Single(h.Step(41));
            Assert.Equal("WaveTrend Cross Bull: Wave cross up 1.0.", spoken);
        }

        [Fact]
        public void TwoCOMPONENTSOfOneSeries_AreEachTheirOwnClause_AndTheSeriesIsNotNamed()
        {
            // One source, two things to say about it. Each clause names its own component and
            // the series is not named at all — the prefix that used to sit here ("Cipher B: …")
            // was a fixed phrase repeated ahead of every signal for the length of a playback
            // run, carrying nothing the listener had not chosen themselves.
            var series = MarkerSeries(120, new[] { 41 });
            var extra = new ComponentConfig
            {
                Name = "Second", DisplayName = "Bear signal",
                DisplayType = ComponentDisplayType.Dot, IsVisible = true,
                SignalSpeechTemplate = "{name} at {price}",
            };
            series.Config.Components.Add(extra);
            var arr = new double[120];
            Array.Fill(arr, double.NaN);
            arr[41] = 1.0;
            series.Data.ComponentData["Second"] = arr;

            var h = Running(series, cursor: 40);

            string spoken = Assert.Single(h.Step(41));
            Assert.Equal("Bull signal at 141.00. Bear signal at 141.00.", spoken);
        }

        [Fact]
        public void ASignalOnABarThatIsNotSteppedOnto_SaysNothing()
        {
            // The vacuity partner for every "was spoken" case above and below: the fixture's
            // signal is real, the playback is real, and the only difference is the bar.
            var h = Running(MarkerSeries(120, new[] { 41 }), cursor: 40);

            Assert.Empty(h.Step(42));
        }

        [Fact]
        public void ASeriesTheUserDidNotFlagWithControlAltShiftN_StaysSilent()
        {
            // N picks WHAT speaks; the Narration tab picks WHEN. The earlier design scanned
            // every active visible series, which would have made playback the one place in the
            // terminal where a series nobody asked to hear from starts talking.
            var h = Running(MarkerSeries(120, new[] { 41 }, narrated: false), cursor: 40);

            Assert.Empty(h.Step(41));
        }

        [Fact]
        public void AMutedComponent_StaysSilent()
        {
            // It produces no tone during playback. A component the user silenced must not be
            // the only thing that speaks — the rule NavigationFeedbackManager's cross-series
            // scan already applies.
            var h = Running(MarkerSeries(120, new[] { 41 }, muted: true), cursor: 40);

            Assert.Empty(h.Step(41));
        }

        [Fact]
        public void AHiddenComponent_StaysSilent()
        {
            var h = Running(MarkerSeries(120, new[] { 41 }, visible: false), cursor: 40);

            Assert.Empty(h.Step(41));
        }

        [Fact]
        public void AComponentWithNoSignalTemplate_StaysSilent()
        {
            // A marker with no template is a dot the indicator author never gave words to.
            // Inventing "Signal at 141.00" for it is how playback would end up narrating every
            // pivot on the chart.
            var h = Running(MarkerSeries(120, new[] { 41 }, template: null), cursor: 40);

            Assert.Empty(h.Step(41));
        }

        [Fact]
        public void ALineComponent_IsNotAMarker_AndStaysSilent()
        {
            // Tier is decided by the component's display type, not by whether it has a value:
            // an EMA has a number on every bar and would speak on every bar.
            var h = Running(MarkerSeries(120, new[] { 41 }, display: ComponentDisplayType.Line), cursor: 40);

            Assert.Empty(h.Step(41));
        }

        // ── One utterance per step ──────────────────────────────────────────────

        [Fact]
        public void ALandmarkAndASignalOnTheSameStep_AreONEUtterance()
        {
            // The guard the design named. Two Speak calls 100 ms apart is one sentence on the
            // desktop head and, on the web head, one of them silently overwriting the other.
            // Bar 31 is 1 February 2024 — a month boundary on daily bars.
            var h = Running(MarkerSeries(120, new[] { 31 }), cursor: 30);

            var call = Assert.Single(StepCalls(h, 31));

            Assert.Equal("February 2024. Bull signal at 131.00.", call.Text);
            Assert.False(call.Interrupt, "playback narration must never clip the utterance before it");
        }

        private static List<(string Text, bool Interrupt, SpeechChannel Channel)> StepCalls(Harness h, int to)
        {
            h.Speech.Calls.Clear();
            h.Store.EmitState(h.Store.State with { CurrentDataIndex = to });
            return h.Speech.Calls.ToList();
        }

        // ── The rate limit ──────────────────────────────────────────────────────

        [Fact]
        public void MinBarsBetweenSignals_ScalesWithPlaybackSpeed()
        {
            // Two seconds' worth of bars, the same window the landmark cadence uses: 10 bars a
            // second at 1x. Faster playback covers more bars in those two seconds, so the words
            // have to thin out with the tones rather than pile up behind them.
            Assert.Equal(20, PlaybackNarration.MinBarsBetweenSignals(1.0f));
            Assert.Equal(80, PlaybackNarration.MinBarsBetweenSignals(4.0f));
            Assert.Equal(2, PlaybackNarration.MinBarsBetweenSignals(0.1f));
        }

        [Fact]
        public void ASecondSignalInsideTheWindow_IsDropped_NotQueued()
        {
            var h = Running(MarkerSeries(120, new[] { 41, 45 }), cursor: 40);

            Assert.Single(h.Step(41));
            for (int i = 42; i <= 45; i++)
            {
                var said = h.Step(i);
                Assert.True(said.Count == 0, $"bar {i} spoke \"{string.Join(" | ", said)}\" inside the 20-bar window");
            }
        }

        [Fact]
        public void ASignalBeyondTheWindow_Speaks()
        {
            // The vacuity partner for the drop above: without it, a build that never speaks a
            // playback signal at all passes that test.
            var h = Running(MarkerSeries(120, new[] { 41, 61 }), cursor: 40);

            Assert.Single(h.Step(41));
            for (int i = 42; i < 61; i++) h.Step(i);

            Assert.Equal("Bull signal at 161.00.", Assert.Single(h.Step(61)));
        }

        [Fact]
        public void ALandmarkIsNeverRateLimitedAwayByARecentSignal()
        {
            // The landmark is the only thing that says WHERE IN TIME the tones are, and it is
            // already sparse by construction. Bar 31 carries a signal AND is 1 February; bar 62
            // is 2 March, four bars inside the signal window that bar 31 opened... except that
            // the window governs signals only.
            var h = Running(MarkerSeries(120, new[] { 31, 45 }), cursor: 30);

            Assert.Single(h.Step(31));                       // "February 2024. Cipher B: ..."
            for (int i = 32; i < 60; i++) h.Step(i);         // includes bar 45's dropped signal

            Assert.Equal("March 2024", Assert.Single(h.Step(60)));
        }

        // ── The switch ──────────────────────────────────────────────────────────

        [Fact]
        public void NarrateDuringPlayback_Off_SilencesBothSignalsAndLandmarks()
        {
            // OFF is the capability nobody had before this release: playback as tones and
            // nothing else. It covers landmarks too — a user who asks for no narration during
            // playback and still hears "February 2024" was not given what the switch says.
            var h = Running(MarkerSeries(120, new[] { 31 }), cursor: 30, narratePlayback: false);

            Assert.Empty(h.Step(31));
        }

        [Fact]
        public void TurningTimeLandmarksOff_KeepsTheSignalsAndStopsTheCalendar()
        {
            // Cody's ask, 2026-09-04: a switch of its own on the Narration tab. The landmark and
            // the signals answer different questions — WHERE IN TIME the tones are, versus WHAT
            // the indicators printed — and wanting the second is not wanting the first read out
            // every few seconds for the length of a run.
            var series = MarkerSeries(120, new[] { 41 });
            var state = Playing(series, cursor: 40) with { SpeakPlaybackLandmarks = false };
            var h = new Harness(state with { IsPlaying = false });
            h.Settle(state);

            Assert.Equal("Bull signal at 141.00.", Assert.Single(h.Step(41)));
        }

        [Fact]
        public void WithTimeLandmarksOff_ABareBoundaryCrossingSaysNothing()
        {
            // The other half. A step that crosses a month boundary with no signal on it is
            // exactly the utterance the switch exists to remove; without this the test above
            // passes on a chart where the landmark never fired anyway.
            var series = MarkerSeries(120, Array.Empty<int>());
            var state = Playing(series, cursor: 59) with { SpeakPlaybackLandmarks = false };
            var h = new Harness(state with { IsPlaying = false });
            h.Settle(state);

            Assert.Empty(h.Step(60));
        }

        [Fact]
        public void TheLandmarkSwitchIsSubordinateToTheMasterOne()
        {
            // Leaving landmarks ON while narration is off must not resurrect them. The master
            // switch is checked first by the caller; this pins that the two compose the way the
            // hint text in Settings claims.
            var series = MarkerSeries(120, Array.Empty<int>());
            var state = Playing(series, cursor: 59, narratePlayback: false)
                with { SpeakPlaybackLandmarks = true };
            var h = new Harness(state with { IsPlaying = false });
            h.Settle(state);

            Assert.Empty(h.Step(60));
        }

        [Fact]
        public void NarrateDuringPlayback_Off_LeavesTheStopConfirmationAlone()
        {
            // The switch governs narration, not the confirmations that answer a keypress.
            // Turning it into a global playback mute would take the end-of-playback sentence
            // with it, and the end of a tone stream is otherwise indistinguishable from a crash.
            var h = Running(MarkerSeries(120, new[] { 31 }), cursor: 30, narratePlayback: false);

            h.Speech.Calls.Clear();
            h.Store.EmitState(h.Store.State with { IsPlaying = false, CurrentDataIndex = 31 });

            Assert.Contains("Playback stopped", Assert.Single(h.Speech.Texts));
        }
    }
}
