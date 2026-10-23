using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Arrowing left and right is heard for the component in focus and nothing else.
    ///
    /// <para>
    /// Cody, 2026-10-22: <i>"when left and right arrowing in an indicator with many series, it seems
    /// like I hear sonifications from components other than the one I'm navigating..and this makes
    /// it kind of hard to hear what I'm supposed to be listening for on a particular component some
    /// of them i think may be rsi crosses or something".</i> Two things made those sounds:
    /// </para>
    /// <list type="bullet">
    ///   <item>The level cues (the 1,400 Hz approach ping and the 220 Hz "held beyond" tone) were
    ///         worked out for EVERY visible indicator, each from its first line, whatever had
    ///         focus. Arrowing Cipher B's money flow rang for its WaveTrend nearing ±60, and for an
    ///         RSI in another pane nearing 70.</item>
    ///   <item>Cluster ticks: every other marker of the focused indicator on the bar (Cipher B's
    ///         dots and crosses) ticked after the focused component's own tone.</item>
    /// </list>
    /// <para>
    /// The level cues now follow the focused component, against the levels it answers to: the same
    /// rule the crossing chirp already used. Cluster ticks are a setting, off unless switched on.
    /// Lines you drew are still heard whatever has focus. Cody asked for that on 2026-10-09, and a
    /// line drawn on price is crossed by price, not by an indicator component.
    /// </para>
    /// </summary>
    public class ArrowingHearsOnlyTheFocusedComponentTests
    {
        private static ChartSeries Series(string id, (string name, ComponentDisplayType type, string[]? subs)[] comps,
            Dictionary<string, double[]> data, params (string name, double value)[] levels)
        {
            var cfg = new SeriesConfig { Id = id, Name = id, IndicatorCode = id.ToUpperInvariant(), Pane = id };
            foreach (var (name, type, subs) in comps)
                cfg.Components.Add(new ComponentConfig
                {
                    Name = name, DisplayName = name, IsVisible = true,
                    Role = ComponentRole.Signal, DisplayType = type, SubscribedLevelNames = subs,
                });
            foreach (var (name, value) in levels)
                cfg.Levels.Add(new LevelConfig { Name = name, Value = value, PlayEarcon = true, EarconVolume = 1f, IsVisible = true });
            var buf = new SeriesDataBuffer { SeriesId = id };
            foreach (var kv in data) buf.ComponentData[kv.Key] = kv.Value;
            return new ChartSeries(cfg, buf);
        }

        /// <summary>RSI 50 then 67: the second bar is inside the 5% band under 70.</summary>
        private static ChartSeries Rsi() => Series("rsi",
            new[] { ("Value", ComponentDisplayType.Line, (string[]?)null) },
            new() { ["Value"] = new double[] { 50, 67 } },
            ("Overbought", 70), ("Oversold", 30));

        /// <summary>A two-line oscillator in one pane, like Cipher B: the WaveTrend answers to the
        /// ±60 lines and nears +60 on bar 1; the money flow answers to none of them and sits at 58
        /// (inside +60's band too, which is why the subscription has to be read).</summary>
        private static ChartSeries TwoLine() => Series("cipher",
            new[]
            {
                ("WT1", ComponentDisplayType.Oscillator, (string[]?)new[] { "Overbought", "Oversold" }),
                ("MF", ComponentDisplayType.ZeroArea, (string[]?)Array.Empty<string>()),
            },
            new() { ["WT1"] = new double[] { 0, 58 }, ["MF"] = new double[] { 0, 58 } },
            ("Overbought", 60), ("Oversold", -60));

        private static WorkspaceState At(int index, string focusedId, int focusedComp, params ChartSeries[] series)
        {
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var bars = Enumerable.Range(0, 2).Select(i => new Ohlcv(start.AddMinutes(i), 100, 100, 100, 100, 0)).ToList();
            return WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(bars),
                ActiveSeries = series.ToImmutableList(),
                CurrentDataIndex = index,
                ViewportStartIndex = 0,
                ViewportLength = 2,
                FocusedSeriesId = focusedId,
                FocusedComponentIndex = focusedComp,
            };
        }

        private static int Notes(INavigationSonifier nav) =>
            nav.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(INavigationSonifier.PlayNote));

        [Fact]
        public void AnRsiInAnotherPane_IsSilent_WhileArrowingADifferentIndicator()
        {
            var nav = Substitute.For<INavigationSonifier>();
            var mon = new LevelCrossingMonitor(nav);
            var rsi = Rsi();
            var other = TwoLine();

            mon.OnBarNavigated(At(1, other.Id, 1, rsi, other));

            Assert.True(Notes(nav) == 0,
                "Arrowing the money flow played a level cue. The RSI in another pane nearing 70 " +
                "rang its approach ping although the RSI is not what is being navigated.");
        }

        [Fact]
        public void ASiblingLine_IsSilent_WhileArrowingAComponentThatAnswersToNoLevel()
        {
            var nav = Substitute.For<INavigationSonifier>();
            var mon = new LevelCrossingMonitor(nav);

            mon.OnBarNavigated(At(1, "cipher", 1, TwoLine()));

            Assert.True(Notes(nav) == 0,
                "Arrowing the money flow (index 1) played the WaveTrend's approach ping for +60. " +
                "The cue followed the indicator's first line, not the component in focus.");
        }

        [Fact]
        public void TheFocusedLine_StillRingsItsOwnApproach()
        {
            // The positive control: without it the two tests above pass on a monitor that never
            // plays anything.
            var nav = Substitute.For<INavigationSonifier>();
            new LevelCrossingMonitor(nav).OnBarNavigated(At(1, "cipher", 0, TwoLine()));
            Assert.Equal(1, Notes(nav));

            var nav2 = Substitute.For<INavigationSonifier>();
            var rsi = Rsi();
            new LevelCrossingMonitor(nav2).OnBarNavigated(At(1, rsi.Id, 0, rsi, TwoLine()));
            Assert.Equal(1, Notes(nav2));
        }

        // ── Cluster ticks ─────────────────────────────────────────────────────────────

        private static (SonificationManager mgr, MockWorkspaceStore store, INavigationSonifier nav)
            Manager(IAppSettings? settings)
        {
            var store = new MockWorkspaceStore();
            var nav = Substitute.For<INavigationSonifier>();
            nav.FireClusterTicksAsync(Arg.Any<WorkspaceState>(), Arg.Any<int>(), Arg.Any<string>(),
                    Arg.Any<int>(), Arg.Any<bool>())
                .Returns(Task.CompletedTask);
            var mainThread = Substitute.For<IMainThreadService>();
            mainThread.When(m => m.InvokeOnMainThread(Arg.Any<Action>()))
                      .Do(ci => ci.Arg<Action>().Invoke());
            var mgr = new SonificationManager(Substitute.For<IPlaybackOrchestrator>(), nav, store,
                mainThread, new SpyEventBus(), levelCrossing: null, settings: settings);
            return (mgr, store, nav);
        }

        private static WorkspaceState Bar(int i) => WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, 6)
                .Select(k => new Ohlcv(new DateTime(2026, 1, 1, 0, k, 0, DateTimeKind.Utc), 100, 101, 99, 100, 1000)).ToList()),
            CurrentDataIndex = i,
            FocusedSeriesId = "series-1",
            FocusedComponentIndex = 2,
            InitStatus = InitializationStatus.Ready,
        };

        private static int ClusterCalls(INavigationSonifier nav) =>
            nav.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(INavigationSonifier.FireClusterTicksAsync));

        [Theory]
        [InlineData(false)] // no settings service at all (a minimal host)
        [InlineData(true)]  // a settings service that has never had the box ticked
        public void OtherMarkers_DoNotTick_UnlessTheSettingIsOn(bool withSettings)
        {
            var settings = withSettings ? Substitute.For<IAppSettings>() : null;
            var (mgr, store, nav) = Manager(settings);
            using (mgr)
            {
                store.EmitState(Bar(1));
                store.EmitState(Bar(2));
                // Navigation ran: the focused component's own voice was asked for.
                Assert.True(SpinWait.SpinUntil(() =>
                    nav.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(INavigationSonifier.SyncNavigationSlots)) >= 2, 1000));
                Thread.Sleep(50);
                Assert.True(ClusterCalls(nav) == 0,
                    "Arrowing onto a bar ticked the indicator's other markers with the setting off. " +
                    "Those are the sounds from 'components other than the one I'm navigating'.");
            }
        }

        [Fact]
        public void OtherMarkers_Tick_WhenTheSettingIsOn()
        {
            var settings = Substitute.For<IAppSettings>();
            settings.TickOtherSignalsWhileArrowing.Returns(true);
            var (mgr, store, nav) = Manager(settings);
            using (mgr)
            {
                store.EmitState(Bar(1));
                store.EmitState(Bar(2));
                Assert.True(SpinWait.SpinUntil(() => ClusterCalls(nav) > 0, 1000),
                    "The setting is on and arrowing never asked for the other markers' ticks.");
                nav.Received().FireClusterTicksAsync(Arg.Any<WorkspaceState>(), 2, "series-1", 2, false);
            }
        }
    }
}
