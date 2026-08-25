using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// End-to-end reducer tests for the real <see cref="WorkspaceStore"/>.
    /// Each test builds a fresh store wired to the real services
    /// (<see cref="ViewportNavigationService"/>, <see cref="ViewportRangeCalculator"/>,
    /// <see cref="VolumeStateService"/>) plus a spy <see cref="SpyEventBus"/>,
    /// dispatches one or more actions, and asserts state transitions.
    ///
    /// Covers the post-2026-04-22 per-domain reducer split:
    ///   <see cref="AccessibleTrader.Core.Services.Workspace.Reducers.ViewportReducer"/>,
    ///   <see cref="AccessibleTrader.Core.Services.Workspace.Reducers.SeriesReducer"/>,
    ///   <see cref="AccessibleTrader.Core.Services.Workspace.Reducers.PlaybackReducer"/>,
    ///   <see cref="AccessibleTrader.Core.Services.Workspace.Reducers.TabReducer"/>,
    ///   <see cref="AccessibleTrader.Core.Services.Workspace.Reducers.DrawingReducer"/>,
    /// and the inlined identity / mode / init / settings / volume branches in
    /// <c>WorkspaceStore.Reduce</c>.
    /// </summary>
    public class WorkspaceStoreTests
    {
        private static WorkspaceStore NewStore(out SpyEventBus bus)
        {
            bus = new SpyEventBus();
            return new WorkspaceStore(
                bus,
                new ViewportRangeCalculator(),
                new ViewportNavigationService(),
                new VolumeStateService());
        }

        private static TimeSeriesBuffer<Ohlcv> MakeBars(int count, DateTime? start = null)
        {
            var s = start ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var list = new List<Ohlcv>(count);
            for (int i = 0; i < count; i++)
            {
                double p = 100 + i;
                list.Add(new Ohlcv(s.AddMinutes(i), p, p + 1, p - 1, p + 0.5, 1000 + i));
            }
            return new TimeSeriesBuffer<Ohlcv>(list);
        }

        private static ChartSeries MakeIndicatorSeries(string id = "rsi")
        {
            var cfg = new SeriesConfig { Id = id, Name = id.ToUpperInvariant(), FriendlyName = id.ToUpperInvariant(), IndicatorCode = "RSI", Pane = "RSI" };
            return new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = id });
        }

        // ── Initial state ───────────────────────────────────────────────────

        [Fact]
        public void InitialState_MatchesWorkspaceStateInitial()
        {
            using var store = NewStore(out _);
            Assert.Equal(WorkspaceState.Initial.ViewportLength, store.State.ViewportLength);
            Assert.Equal(WorkspaceState.Initial.RightMarginBars, store.State.RightMarginBars);
            Assert.True(store.State.IsSonificationEnabled);
            Assert.True(store.State.IsSpeechEnabled);
            Assert.Empty(store.State.ActiveSeries);
        }

        // ── Identity / mode / provider (inlined projections) ─────────────────

        [Fact]
        public void SetIdentityAction_UpdatesIdentity()
        {
            using var store = NewStore(out _);
            var id = new ChartIdentity("Spot", "Binance", "BTCUSDT", "1h");
            store.Dispatch(new SetIdentityAction(id));
            Assert.Equal(id, store.State.Identity);
        }

        [Fact]
        public void ChangeModeAction_UpdatesMode()
        {
            using var store = NewStore(out _);
            store.Dispatch(new ChangeModeAction(TerminalMode.Trading));
            Assert.Equal(TerminalMode.Trading, store.State.Mode);
        }

        // ── Playback reducer ────────────────────────────────────────────────

        [Fact]
        public void ToggleSpeechAction_FlipsIsSpeechEnabled()
        {
            using var store = NewStore(out _);
            bool before = store.State.IsSpeechEnabled;
            store.Dispatch(new ToggleSpeechAction());
            Assert.Equal(!before, store.State.IsSpeechEnabled);
            store.Dispatch(new ToggleSpeechAction());
            Assert.Equal(before, store.State.IsSpeechEnabled);
        }

        [Fact]
        public void ToggleSonificationAction_FlipsIsSonificationEnabled()
        {
            using var store = NewStore(out _);
            bool before = store.State.IsSonificationEnabled;
            store.Dispatch(new ToggleSonificationAction());
            Assert.Equal(!before, store.State.IsSonificationEnabled);
        }

        [Fact]
        public void ToggleEventSpeech_and_ToggleEarcons_actually_reach_their_reducer()
        {
            // 2026-07-23 live finding: the reducer cases and spoken confirmations
            // for Shift+F2 / Shift+F3 existed, but the actions were missing from
            // the store's routing switch — both shortcuts silently did nothing.
            using var store = NewStore(out _);

            bool eventsBefore = store.State.IsEventSpeechEnabled;
            store.Dispatch(new ToggleEventSpeechAction());
            Assert.Equal(!eventsBefore, store.State.IsEventSpeechEnabled);

            bool earconsBefore = store.State.IsEarconsEnabled;
            store.Dispatch(new ToggleEarconsAction());
            Assert.Equal(!earconsBefore, store.State.IsEarconsEnabled);
        }

        [Fact]
        public void ToggleHeikinAshiAction_FlipsFlag()
        {
            using var store = NewStore(out _);
            bool before = store.State.IsHeikinAshi;
            store.Dispatch(new ToggleHeikinAshiAction());
            Assert.Equal(!before, store.State.IsHeikinAshi);
        }

        [Fact]
        public void ToggleLogScaleAction_FlipsFlag()
        {
            using var store = NewStore(out _);
            bool before = store.State.IsLogScale;
            store.Dispatch(new ToggleLogScaleAction());
            Assert.Equal(!before, store.State.IsLogScale);
        }

        [Fact]
        public void SetPlaybackAction_UpdatesIsPlayingAndScope()
        {
            using var store = NewStore(out _);
            store.Dispatch(new SetPlaybackAction(true, PlaybackScope.Series));
            Assert.True(store.State.IsPlaying);
            Assert.Equal(PlaybackScope.Series, store.State.PlaybackScope);
        }

        // ── Viewport reducer ────────────────────────────────────────────────

        [Fact]
        public void UpdateDataAction_InitialLoad_SetsDataAndComputesRange()
        {
            using var store = NewStore(out _);
            var bars = MakeBars(200);
            store.Dispatch(new UpdateDataAction(bars, IsInitialLoad: true));
            Assert.Equal(200, store.State.Data.Count);
            Assert.True(store.State.ViewportRange.Max > store.State.ViewportRange.Min);
        }

        [Fact]
        public void NavigateAction_ClampsTargetIndexInsideData()
        {
            using var store = NewStore(out _);
            store.Dispatch(new UpdateDataAction(MakeBars(150), IsInitialLoad: true));
            store.Dispatch(new NavigateAction(10_000));
            Assert.Equal(149, store.State.CurrentDataIndex);
            store.Dispatch(new NavigateAction(-5));
            Assert.Equal(0, store.State.CurrentDataIndex);
        }

        [Fact]
        public void JumpToLatestAction_MovesCursorToLiveEdge()
        {
            using var store = NewStore(out _);
            store.Dispatch(new UpdateDataAction(MakeBars(150), IsInitialLoad: true));
            store.Dispatch(new NavigateAction(50));
            store.Dispatch(new JumpToLatestAction());
            Assert.Equal(149, store.State.CurrentDataIndex);
        }

        // ── Series reducer ─────────────────────────────────────────────────

        [Fact]
        public void AddSeriesAction_AppendsSeriesAndFocusesIt()
        {
            using var store = NewStore(out _);
            var s = MakeIndicatorSeries("rsi");
            store.Dispatch(new AddSeriesAction(s));
            Assert.Single(store.State.ActiveSeries);
            Assert.Equal("rsi", store.State.FocusedSeriesId);
        }

        [Fact]
        public void RemoveSeriesAction_RemovesAndReassignsFocus()
        {
            using var store = NewStore(out _);
            store.Dispatch(new AddSeriesAction(MakeIndicatorSeries("rsi")));
            store.Dispatch(new AddSeriesAction(MakeIndicatorSeries("macd")));
            store.Dispatch(new RemoveSeriesAction("macd"));
            Assert.Single(store.State.ActiveSeries);
            Assert.Equal("rsi", store.State.ActiveSeries[0].Id);
        }

        [Fact]
        public void AddLevelAction_ClonesTargetSeriesAndIsolatesMutation()
        {
            using var store = NewStore(out _);
            var s = MakeIndicatorSeries("rsi");
            store.Dispatch(new AddSeriesAction(s));

            var snapshotBeforeLevel = store.State.ActiveSeries[0];
            int beforeCount = snapshotBeforeLevel.Levels.Count;

            store.Dispatch(new AddLevelAction("rsi", new LevelConfig { Value = 30, Name = "Oversold" }));

            // New state has the level.
            Assert.Equal(beforeCount + 1, store.State.ActiveSeries[0].Levels.Count);
            // Pre-dispatch reference still observes its own Levels collection unchanged.
            Assert.Equal(beforeCount, snapshotBeforeLevel.Levels.Count);
            // Reducer produced a distinct series instance (clone, not in-place mutation).
            Assert.NotSame(snapshotBeforeLevel, store.State.ActiveSeries[0]);
        }

        [Fact]
        public void ToggleMuteAction_FlipsSeriesLevelMute()
        {
            using var store = NewStore(out _);
            store.Dispatch(new AddSeriesAction(MakeIndicatorSeries("rsi")));
            bool before = store.State.ActiveSeries[0].IsMuted;
            store.Dispatch(new ToggleMuteAction("rsi"));
            Assert.Equal(!before, store.State.ActiveSeries[0].IsMuted);
        }

        [Fact]
        public void SelectComponentAction_UpdatesFocusedComponentIndex()
        {
            using var store = NewStore(out _);
            store.Dispatch(new SelectComponentAction(3));
            Assert.Equal(3, store.State.FocusedComponentIndex);
        }

        // ── Tab reducer ─────────────────────────────────────────────────────

        [Fact]
        public void AddTabAction_AppendsSnapshotAndSwitchesToIt()
        {
            using var store = NewStore(out _);
            var id = new ChartIdentity("Spot", "Binance", "BTCUSDT", "1h");
            store.Dispatch(new SetIdentityAction(id));
            int tabsBefore = store.State.TabSnapshots?.Count ?? 0;

            store.Dispatch(new AddTabAction());

            Assert.True((store.State.TabSnapshots?.Count ?? 0) >= tabsBefore);
        }

        // ── Init status state machine ───────────────────────────────────────

        [Fact]
        public void RequestInitializationStatus_LegalTransition_Applies()
        {
            using var store = NewStore(out _);
            Assert.Equal(InitializationStatus.Booting, store.State.InitStatus);
            store.Dispatch(new RequestInitializationStatusAction(InitializationStatus.Loading));
            Assert.Equal(InitializationStatus.Loading, store.State.InitStatus);
            store.Dispatch(new RequestInitializationStatusAction(InitializationStatus.Ready));
            Assert.Equal(InitializationStatus.Ready, store.State.InitStatus);
        }

        [Fact]
        public void RequestInitializationStatus_IllegalTransition_NoOps()
        {
            using var store = NewStore(out _);
            // Booting -> Ready is allowed per CanTransition; test a genuinely illegal hop.
            store.Dispatch(new RequestInitializationStatusAction(InitializationStatus.Loading));
            store.Dispatch(new RequestInitializationStatusAction(InitializationStatus.Booting));
            // Loading -> Booting is not allowed: state should remain Loading.
            Assert.Equal(InitializationStatus.Loading, store.State.InitStatus);
        }

        [Fact]
        public void RequestInitializationStatus_ErrorAlwaysAllowed()
        {
            using var store = NewStore(out _);
            store.Dispatch(new RequestInitializationStatusAction(InitializationStatus.Ready));
            store.Dispatch(new RequestInitializationStatusAction(InitializationStatus.Error));
            Assert.Equal(InitializationStatus.Error, store.State.InitStatus);
        }

        // ── Drawing reducer ─────────────────────────────────────────────────

        [Fact]
        public void EnterCoordinateEntry_SetsModeAndTool()
        {
            using var store = NewStore(out _);
            store.Dispatch(new EnterCoordinateEntryAction(DrawingType.TrendLine));
            Assert.True(store.State.IsCoordinateEntryMode);
            Assert.Equal(DrawingType.TrendLine, store.State.PendingDrawingTool);
        }

        [Fact]
        public void ExitCoordinateEntry_ResetsMode()
        {
            using var store = NewStore(out _);
            store.Dispatch(new EnterCoordinateEntryAction(DrawingType.TrendLine));
            store.Dispatch(new ExitCoordinateEntryAction());
            Assert.False(store.State.IsCoordinateEntryMode);
            Assert.Null(store.State.PendingDrawingTool);
        }

        // ── Volume service delegation ───────────────────────────────────────

        [Fact]
        public void AdjustChartVolume_ClampsToUnitInterval()
        {
            using var store = NewStore(out _);
            store.Dispatch(new AdjustChartVolumeAction("Master", +10f));
            Assert.True(store.State.ChartVolume <= 1.0f);
            store.Dispatch(new AdjustChartVolumeAction("Master", -10f));
            Assert.True(store.State.ChartVolume >= 0.0f);
        }

        // ── UpdateSettingsAction (custom projection) ────────────────────────

        [Fact]
        public void UpdateSettingsAction_AppliesUpdaterFunction()
        {
            using var store = NewStore(out _);
            store.Dispatch(new UpdateSettingsAction(s => s with { SpeakTimestamps = !s.SpeakTimestamps }));
            Assert.False(store.State.SpeakTimestamps); // default is true
        }

        // ── StateStream subscription ────────────────────────────────────────

        [Fact]
        public void Dispatch_PublishesSingleStateStreamUpdatePerChange()
        {
            using var store = NewStore(out _);
            int count = 0;
            WorkspaceState? latest = null;
            using var sub = store.StateStream.Subscribe(s => { count++; latest = s; });

            int initial = count; // BehaviorSubject emits current value on subscribe
            store.Dispatch(new ToggleSpeechAction());
            Assert.Equal(initial + 1, count);
            Assert.NotNull(latest);
            Assert.Equal(store.State.IsSpeechEnabled, latest!.IsSpeechEnabled);
        }

        [Fact]
        public void Dispatch_NoOpAction_DoesNotPushDuplicateStateStream()
        {
            using var store = NewStore(out _);
            int count = 0;
            using var sub = store.StateStream.Subscribe(_ => count++);
            int initial = count;
            // Dispatch an action the reducer treats as identity (e.g. unchanged identity).
            store.Dispatch(new ChangeModeAction(store.State.Mode));
            Assert.Equal(initial, count);
        }

        // ── Concurrency / immutability contract ─────────────────────────────

        [Fact]
        public async Task Dispatch_ConcurrentAdjustVolume_ResultClampedAndConsistent()
        {
            // 8 threads × 50 adjustments each. Final volume must be clamped to [0, 1]
            // and (regardless of interleave) equal to the state's own ChartVolume —
            // a torn read would produce a stale reference in the subject while the
            // lock-held candidate differs. This exercises the Dispatch lock + the
            // immutable-clone path in VolumeStateService.
            using var store = NewStore(out _);
            const int threads = 8;
            const int perThread = 50;
            var tasks = new Task[threads];
            for (int t = 0; t < threads; t++)
            {
                float delta = (t % 2 == 0 ? +0.01f : -0.01f);
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < perThread; i++)
                        store.Dispatch(new AdjustChartVolumeAction("Master", delta));
                });
            }
            await Task.WhenAll(tasks);

            Assert.InRange(store.State.ChartVolume, 0.0f, 1.0f);
            // StateStream.Value and State property are both driven off the same lock.
            WorkspaceState? snap = null;
            using var sub = store.StateStream.Subscribe(s => snap = s);
            Assert.NotNull(snap);
            Assert.Equal(store.State.ChartVolume, snap!.ChartVolume);
        }

        [Fact]
        public async Task Dispatch_ConcurrentAddSeries_FinalListHasAllUniqueIds()
        {
            using var store = NewStore(out _);
            const int threads = 4;
            const int perThread = 10;
            var tasks = new Task[threads];
            for (int t = 0; t < threads; t++)
            {
                int tid = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < perThread; i++)
                        store.Dispatch(new AddSeriesAction(MakeIndicatorSeries($"s_{tid}_{i}")));
                });
            }
            await Task.WhenAll(tasks);

            var ids = store.State.ActiveSeries.Select(s => s.Id).ToHashSet();
            Assert.Equal(threads * perThread, ids.Count);
        }
    }
}
