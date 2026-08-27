using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Two announcements that had the opposite problems: one said too much, in two voices, and
    /// the other could be silenced entirely.
    /// </summary>
    public class PaneAndLoadFailureAnnouncementTests
    {
        // ── Ctrl+PageUp/PageDown names the pane ONCE ───────────────────────────

        [Fact]
        public void SubPaneNavigation_DoesNotCarryItsOwnPaneLabel()
        {
            // Two independent components each announced the pane on the same keypress:
            // CommandDispatcher built one from the raw SubPaneName ("MF pane") and shipped it as
            // the feedback prefix, while NavigationFeedbackManager detected the same transition
            // and prepended its own, resolved from a component's friendlier DisplayName
            // ("Money Flow pane"). On a Cipher-style indicator the user heard
            // "Money Flow pane. MF pane. Money Flow Wave. …".
            //
            // The transition announcement belongs to the manager — it is the only one of the two
            // that can tell a pane CHANGE from a move within a pane — so the dispatcher's prefix
            // must be empty.
            var (dispatcher, bus, _) = Build();
            dispatcher.SetChartActive(true);

            dispatcher.Dispatch(SystemCommand.NavSubPaneNext);

            var nav = bus.Log.OfType<FeedbackRequestEvent>()
                             .Where(e => e.Type == FeedbackType.Navigation && e.IsYMove)
                             .ToList();

            Assert.Single(nav);
            Assert.True(string.IsNullOrEmpty(nav[0].Message),
                $"Sub-pane navigation shipped a pane label of its own (\"{nav[0].Message}\"), "
                + "which NavigationFeedbackManager will then say again under a different name.");
        }

        [Fact]
        public void SubPaneNavigation_StillMovesTheFocusedComponent()
        {
            // Vacuity guard: emptying the message would be trivially satisfiable by making the
            // command do nothing at all. The move itself still has to happen.
            var (dispatcher, _, store) = Build();
            dispatcher.SetChartActive(true);

            dispatcher.Dispatch(SystemCommand.NavSubPaneNext);

            Assert.Contains(store.DispatchedActions.OfType<SelectComponentAction>(), a => a.ComponentIndex != 0);
        }

        // ── A chart that fails to load cannot be silenced by F2 ────────────────

        [Fact]
        public void AChartLoadFailure_IsAnnouncedOnTheCriticalChannel_WithAnEarcon()
        {
            // This was the one failure in AccessibilityFeedbackCoordinator still on the default
            // Manual channel, which F2 silences — so for a user who had muted manual speech a
            // chart failing to load was completely silent, with no earcon either. Every other
            // failure in the class routes to Critical precisely because the feedback contract
            // forbids a silent failure.
            var (coord, speech, audio, store) = BuildCoordinator();

            store.EmitState(WorkspaceState.Initial with { InitStatus = InitializationStatus.Loading });
            store.EmitState(WorkspaceState.Initial with { InitStatus = InitializationStatus.Error });

            var failure = speech.Utterances.LastOrDefault(u => u.Text.Contains("failed to load"));
            Assert.NotNull(failure);
            Assert.Equal(SpeechChannel.Critical, failure!.Channel);
            audio.Received().PlayEarcon(FeedbackType.Error, Arg.Any<ErrorSeverity>());
        }

        // ── Fixtures ───────────────────────────────────────────────────────────

        /// <summary>
        /// A two-sub-pane indicator focused on the first pane, so NavSubPaneNext has somewhere
        /// to go. The pane keys are deliberately terse ("MF") and the display names friendly
        /// ("Money Flow Wave") — the exact shape that produced the doubled announcement.
        /// </summary>
        private static (CommandDispatcher Dispatcher, SpyEventBus Bus, MockWorkspaceStore Store) Build()
        {
            var cfg = new SeriesConfig
            {
                Id = "cipher", Name = "CipherB", FriendlyName = "Cipher B",
                IndicatorCode = "CIPHER_B", Pane = "Main"
            };
            cfg.Components.Add(new ComponentConfig
            {
                Name = "Wave", DisplayName = "Wave", IsVisible = true, SubPaneName = null
            });
            cfg.Components.Add(new ComponentConfig
            {
                Name = "MFW", DisplayName = "Money Flow Wave", IsVisible = true, SubPaneName = "MF"
            });

            var buf = new SeriesDataBuffer { SeriesId = "cipher" };
            buf.ComponentData["Wave"] = new[] { 1.0, 2.0 };
            buf.ComponentData["MFW"] = new[] { 3.0, 4.0 };
            var series = new ChartSeries(cfg, buf);

            var state = WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(new List<Ohlcv>
                {
                    new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 99, 101, 98, 100, 1000),
                    new(new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc), 100, 102, 99, 101, 1000),
                }),
                CurrentDataIndex = 1,
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = series.Id,
                FocusedComponentIndex = 0,
            };

            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            store.EmitState(state);

            var dispatcher = new CommandDispatcher(
                bus, Substitute.For<INavigationEngine>(), store,
                Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));

            return (dispatcher, bus, store);
        }

        private sealed record Utterance(string Text, bool Interrupt, SpeechChannel Channel);

        private sealed class ChannelRecordingRouter : ISpeechFeedbackRouter
        {
            public List<Utterance> Utterances { get; } = new();
            public void Speak(string message, bool interrupt = false, SpeechChannel channel = SpeechChannel.Manual)
                => Utterances.Add(new Utterance(message, interrupt, channel));
            public void SpeakPoint(WorkspaceState s, WorkspaceState? p, ChartSeries ser, Ohlcv pt, string pfx = "") { }
            public void SpeakProfile(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int bin, string pfx = "") { }
            public void SpeakHeatmap(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int di, int bin, string pfx = "") { }
        }

        /// <summary>
        /// The coordinator wired to a router that RECORDS the channel. The suite's usual
        /// <c>SpySpeechRouter</c> discards it, which would make the assertion below pass no
        /// matter which channel the failure went out on — the exact shape of a vacuous test.
        /// </summary>
        private static (AccessibilityFeedbackCoordinator Coord, ChannelRecordingRouter Speech,
                        IAudioFeedbackRouter Audio, MockWorkspaceStore Store) BuildCoordinator()
        {
            var speech = new ChannelRecordingRouter();
            var audio = Substitute.For<IAudioFeedbackRouter>();
            var store = new MockWorkspaceStore();

            var coord = new AccessibilityFeedbackCoordinator(
                store,
                new MockNavManager(),
                speech,
                audio,
                new SpeechFormatter(),
                new SpyEventBus(),
                new MockEarconService(),
                new SdkCandlePatternAnalyzer(),
                new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
                new ChartPatternFocus(),
                new MockAutoNarrationService());

            return (coord, speech, audio, store);
        }
    }
}
