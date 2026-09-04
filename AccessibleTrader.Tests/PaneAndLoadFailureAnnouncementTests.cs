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
        // ── Alt+PageUp/PageDown: the PANE walk, and what it fixed ──────────────

        [Fact]
        public void PaneNavigation_FromCandles_ReachesTheIndicatorPane()
        {
            // THE BUG, as Cody hit it: "pressing alt pg up/down says 'no subpanes in candles'".
            //
            // The old walk built its pane list from series.Components — one series' declared
            // sub-panes — while the RENDERER groups by ChartSeries.Pane across the whole series
            // list. With the cursor on the candles, which declare no sub-pane, the key announced
            // "No sub-panes in Candles" and moved nothing, while the chart in front of the user
            // had a whole second pane on it.
            var (dispatcher, bus, store) = Build();
            dispatcher.SetChartActive(true);

            dispatcher.Dispatch(SystemCommand.NavPaneNext);

            Assert.Contains(store.DispatchedActions.OfType<SelectSeriesAction>(), a => a.SeriesId == "cipher");
            Assert.DoesNotContain(bus.Log.OfType<FeedbackRequestEvent>(),
                e => (e.Message ?? "").Contains("sub-pane", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void PaneNavigation_DoesNotCarryItsOwnPaneLabel()
        {
            // Two independent components each announced the pane on the same keypress:
            // CommandDispatcher built one from the raw SubPaneName ("MF pane") and shipped it as
            // the feedback prefix, while NavigationFeedbackManager detected the same transition
            // and announced its own, resolved from a component's friendlier DisplayName
            // ("Money Flow pane"). The user heard both, under two different names.
            //
            // The transition announcement belongs to the manager — it is the only one of the two
            // that can tell a pane CHANGE from a move within a pane — so the dispatcher must ship
            // no label of its own.
            var (dispatcher, bus, _) = Build();
            dispatcher.SetChartActive(true);

            dispatcher.Dispatch(SystemCommand.NavPaneNext);

            var nav = bus.Log.OfType<FeedbackRequestEvent>()
                             .Where(e => e.Type == FeedbackType.Navigation)
                             .ToList();

            Assert.Single(nav);
            Assert.True(string.IsNullOrEmpty(nav[0].Message),
                $"Pane navigation shipped a pane label of its own (\"{nav[0].Message}\"), "
                + "which NavigationFeedbackManager will then say again under a different name.");
        }

        [Fact]
        public void PaneNavigation_ClampsAtTheBottom_WithABoundaryEarcon()
        {
            // Settled once for all the traversal keys: they clamp rather than wrap. A silent jump
            // from the bottom of the chart back to the top is the one outcome a user who cannot
            // see the move has no way to detect.
            var (dispatcher, bus, store) = Build();
            dispatcher.SetChartActive(true);

            dispatcher.Dispatch(SystemCommand.NavPaneNext);   // Main → Cipher
            store.EmitState(store.State with { FocusedSeriesId = "cipher" });
            bus.Log.Clear();
            dispatcher.Dispatch(SystemCommand.NavPaneNext);   // nothing below it

            Assert.DoesNotContain(store.DispatchedActions.OfType<SelectSeriesAction>()
                                       .Skip(1), a => a.SeriesId == "candles");
            Assert.Contains(bus.Log.OfType<FeedbackRequestEvent>(), e => e.Type == FeedbackType.Boundary);
        }

        [Fact]
        public void IntraPaneNavigation_WalksAcrossSeriesInThePane()
        {
            // The other half of the same mismatch. A sub-pane is DRAWN from every series in the
            // pane but was WALKED within one, so Ctrl+Down on the candles could never reach
            // Price — two series against the same Y axis, in the same band, one drawn on top of
            // the other, and no key that got from one to the other.
            var (dispatcher, _, store) = Build();
            dispatcher.SetChartActive(true);

            dispatcher.Dispatch(SystemCommand.NavComponentInPaneNext);

            Assert.Contains(store.DispatchedActions.OfType<SelectSeriesAction>(), a => a.SeriesId == "price");
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
        /// <summary>
        /// A chart shaped like the one the bug was found on: TWO series sharing the Main pane
        /// (candles and a price overlay — the pair Ctrl+Up/Down could not walk between), and a
        /// Cipher-style indicator in a pane of its own carrying a sub-pane strip.
        /// </summary>
        private static (CommandDispatcher Dispatcher, SpyEventBus Bus, MockWorkspaceStore Store) Build()
        {
            var candlesCfg = new SeriesConfig { Id = "candles", Name = "Candles", FriendlyName = "Candles", Pane = "Main" };
            candlesCfg.Components.Add(new ComponentConfig { Name = "Close", DisplayName = "Close", IsVisible = true });
            var candlesBuf = new SeriesDataBuffer { SeriesId = "candles" };
            candlesBuf.ComponentData["Close"] = new[] { 100.0, 101.0 };

            var priceCfg = new SeriesConfig { Id = "price", Name = "Price", FriendlyName = "Price", Pane = "Main" };
            priceCfg.Components.Add(new ComponentConfig { Name = "Line", DisplayName = "Line", IsVisible = true });
            var priceBuf = new SeriesDataBuffer { SeriesId = "price" };
            priceBuf.ComponentData["Line"] = new[] { 100.5, 101.5 };

            var cipherCfg = new SeriesConfig
            {
                Id = "cipher", Name = "CipherB", FriendlyName = "Cipher B",
                IndicatorCode = "CIPHER_B", Pane = "Pane_CIPHER_B"
            };
            cipherCfg.Components.Add(new ComponentConfig
            {
                Name = "Wave", DisplayName = "Wave", IsVisible = true, SubPaneName = null
            });
            cipherCfg.Components.Add(new ComponentConfig
            {
                Name = "MFW", DisplayName = "Money Flow Wave", IsVisible = true, SubPaneName = "MF"
            });
            var cipherBuf = new SeriesDataBuffer { SeriesId = "cipher" };
            cipherBuf.ComponentData["Wave"] = new[] { 1.0, 2.0 };
            cipherBuf.ComponentData["MFW"] = new[] { 3.0, 4.0 };

            var state = WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(new List<Ohlcv>
                {
                    new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 99, 101, 98, 100, 1000),
                    new(new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc), 100, 102, 99, 101, 1000),
                }),
                CurrentDataIndex = 1,
                ActiveSeries = ImmutableList.Create(
                    new ChartSeries(candlesCfg, candlesBuf),
                    new ChartSeries(priceCfg, priceBuf),
                    new ChartSeries(cipherCfg, cipherBuf)),
                FocusedSeriesId = "candles",
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
