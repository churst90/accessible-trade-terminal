using System;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Phase E test-debt for CommandDispatcher. ModalCloseDispatchTests covers the
    /// modal trap and Phase5KeyboardScopeTests covers the static scope categorization;
    /// these tests cover the RUNTIME behaviour of the two remaining gates —
    /// chart-focus gating and the loaded-data validation gate — plus the playback
    /// command routing into workspace actions.
    /// </summary>
    public class CommandDispatcherGatingTests
    {
        private static Ohlcv Bar(double close, int minute) => new(
            new DateTime(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc),
            close - 1, close + 1, close - 2, close, 1000);

        private static WorkspaceState LoadedState() => WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(
                Enumerable.Range(0, 5).Select(i => Bar(100 + i, i)).ToList()),
            CurrentDataIndex = 4,
        };

        private static (CommandDispatcher dispatcher, SpyEventBus bus,
                        MockWorkspaceStore store, INavigationEngine nav)
            Build(WorkspaceState? state = null)
        {
            var bus = new SpyEventBus();
            var nav = Substitute.For<INavigationEngine>();
            var store = new MockWorkspaceStore();
            if (state != null) store.EmitState(state);
            var barDetail = Substitute.For<IBarDetailService>();
            var crossing = new IndicatorCrossingEngine(store, bus);
            var dispatcher = new CommandDispatcher(bus, nav, store, barDetail, crossing);
            return (dispatcher, bus, store, nav);
        }

        // ── Chart-focus gate ────────────────────────────────────────────────

        [Fact]
        public void ChartScopedCommand_IsSilentlySuppressed_BeforeChartEverHadFocus()
        {
            // The gate starts CLOSED: the app launches with focus on the banner
            // heading, so a stray arrow key must not move the chart cursor. The
            // suppression is silent — no error speech — because the keystroke
            // rightfully belongs to whatever DOES have focus.
            var (dispatcher, bus, _, nav) = Build(LoadedState());

            dispatcher.Dispatch(SystemCommand.NavRight);

            nav.DidNotReceive().ProcessNavigation(Arg.Any<string>());
            Assert.Empty(bus.Log.OfType<FeedbackRequestEvent>());
        }

        [Theory]
        [InlineData(SystemCommand.NavRight, "NAV_RIGHT")]
        [InlineData(SystemCommand.NavLeft, "NAV_LEFT")]
        [InlineData(SystemCommand.ZoomIn, "VIEW_ZOOM_IN")]
        [InlineData(SystemCommand.PanRight, "VIEW_PAN_RIGHT")]
        public void ChartScopedNavCommand_RoutesToNavigationEngine_WhenChartFocused(
            SystemCommand cmd, string expectedNavString)
        {
            var (dispatcher, _, _, nav) = Build(LoadedState());
            dispatcher.SetChartActive(true);

            dispatcher.Dispatch(cmd);

            nav.Received(1).ProcessNavigation(expectedNavString);
        }

        [Fact]
        public void GlobalCommand_Fires_EvenWithoutChartFocus()
        {
            // Modal opens are global: the user must be able to press F12 for settings
            // from any focus location (toolbar, banner, tab bar).
            var (dispatcher, bus, _, _) = Build(); // empty workspace, chart never focused

            dispatcher.Dispatch(SystemCommand.OpenSettings);

            Assert.Single(bus.Log.OfType<OpenSettingsEvent>());
        }

        [Fact]
        public void ChartFocusEvent_OnBus_OpensTheGate()
        {
            // ChartArea publishes ChartFocusEvent from its @onfocus handler; the
            // dispatcher must pick it up from the bus without an explicit SetChartActive.
            var (dispatcher, bus, _, nav) = Build(LoadedState());

            bus.Publish(new ChartFocusEvent());
            dispatcher.Dispatch(SystemCommand.NavRight);

            nav.Received(1).ProcessNavigation("NAV_RIGHT");
        }

        // ── Data-validation gate ────────────────────────────────────────────

        [Fact]
        public void NavigationOnEmptyWorkspace_AnnouncesNoChartLoaded_AndDoesNotNavigate()
        {
            var (dispatcher, bus, _, nav) = Build(); // WorkspaceState.Initial: no data
            dispatcher.SetChartActive(true);

            dispatcher.Dispatch(SystemCommand.NavRight);

            nav.DidNotReceive().ProcessNavigation(Arg.Any<string>());
            var feedback = Assert.Single(bus.Log.OfType<FeedbackRequestEvent>());
            Assert.Equal(FeedbackType.Error, feedback.Type);
            Assert.Equal("No chart loaded.", feedback.Message);
        }

        [Fact]
        public void PlaybackOnEmptyWorkspace_AnnouncesNoChartLoaded_AndDispatchesNothing()
        {
            var (dispatcher, bus, store, _) = Build();
            dispatcher.SetChartActive(true);

            dispatcher.Dispatch(SystemCommand.PlayChart);

            Assert.Empty(store.DispatchedActions);
            var feedback = Assert.Single(bus.Log.OfType<FeedbackRequestEvent>());
            Assert.Equal(FeedbackType.Error, feedback.Type);
            Assert.Equal("No chart loaded.", feedback.Message);
        }

        [Fact]
        public void GlobalCommand_BypassesTheDataGate_OnEmptyWorkspace()
        {
            // Volume keys must keep working before any chart is loaded — a blind user
            // adjusts speech/sonification volume first, loads data second.
            var (dispatcher, bus, _, _) = Build();

            dispatcher.Dispatch(SystemCommand.VolChartUp);

            var ev = Assert.Single(bus.Log.OfType<VolumeChangeEvent>());
            Assert.Equal("CHART", ev.Scope);
        }

        // ── Playback routing ────────────────────────────────────────────────

        [Theory]
        [InlineData(SystemCommand.PlayChart, PlaybackScope.Chart)]
        [InlineData(SystemCommand.PlaySeries, PlaybackScope.Series)]
        [InlineData(SystemCommand.PlayComponent, PlaybackScope.Component)]
        public void PlayCommand_StartsPlaybackWithMatchingScope_WhenIdle(
            SystemCommand cmd, PlaybackScope expectedScope)
        {
            var (dispatcher, _, store, _) = Build(LoadedState());
            dispatcher.SetChartActive(true);

            dispatcher.Dispatch(cmd);

            var action = Assert.Single(store.DispatchedActions.OfType<SetPlaybackAction>());
            Assert.True(action.IsPlaying);
            Assert.Equal(expectedScope, action.Scope);
        }

        [Fact]
        public void PlayChart_SecondPress_StopsPlayback_EvenWhilePaused()
        {
            // Space is a start-or-stop toggle with no hanging intermediate state;
            // Ctrl+Space (PlayPause) is the only pause/resume key.
            var (dispatcher, _, store, _) = Build(LoadedState() with { IsPlaying = true });
            dispatcher.SetChartActive(true);
            dispatcher.Dispatch(SystemCommand.PlayChart);

            var stop = Assert.Single(store.DispatchedActions.OfType<SetPlaybackAction>());
            Assert.False(stop.IsPlaying);

            // Paused (not playing) also counts as "active" — second press stops.
            var (dispatcher2, _, store2, _) = Build(LoadedState() with { IsPaused = true });
            dispatcher2.SetChartActive(true);
            dispatcher2.Dispatch(SystemCommand.PlayChart);

            var stop2 = Assert.Single(store2.DispatchedActions.OfType<SetPlaybackAction>());
            Assert.False(stop2.IsPlaying);
        }

        [Fact]
        public void PlayPause_DispatchesTogglePauseAction()
        {
            var (dispatcher, _, store, _) = Build(LoadedState() with { IsPlaying = true });
            dispatcher.SetChartActive(true);

            dispatcher.Dispatch(SystemCommand.PlayPause);

            Assert.Single(store.DispatchedActions.OfType<TogglePauseAction>());
        }

        [Fact]
        public void PlayStop_StopsUsingTheCurrentPlaybackScope()
        {
            // Shift+Escape force-stops whatever is playing; it must respect the scope
            // the playback was started with rather than resetting to Chart.
            var (dispatcher, _, store, _) = Build(LoadedState() with
            {
                IsPlaying = true,
                PlaybackScope = PlaybackScope.Series,
            });
            dispatcher.SetChartActive(true);

            dispatcher.Dispatch(SystemCommand.PlayStop);

            var action = Assert.Single(store.DispatchedActions.OfType<SetPlaybackAction>());
            Assert.False(action.IsPlaying);
            Assert.Equal(PlaybackScope.Series, action.Scope);
        }

        [Theory]
        [InlineData(SystemCommand.PlaySpeedUp, 0.1f)]
        [InlineData(SystemCommand.PlaySpeedDown, -0.1f)]
        public void PlaySpeed_AdjustsByTenthSteps(SystemCommand cmd, float expectedDelta)
        {
            var (dispatcher, _, store, _) = Build(LoadedState());
            dispatcher.SetChartActive(true);

            dispatcher.Dispatch(cmd);

            var action = Assert.Single(store.DispatchedActions.OfType<AdjustPlaybackSpeedAction>());
            Assert.Equal(expectedDelta, action.Delta, 3);
        }
    }
}
