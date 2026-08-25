using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Verifies multi-tab state management: add/switch/close tabs, snapshot
    /// save/restore, global settings persistence across switches, and
    /// correct tab-count computation.
    /// </summary>
    public class MultiTabTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────────

        private static WorkspaceState State(ChartIdentity identity) =>
            WorkspaceState.Initial with
            {
                Identity = identity,
                Data = new TimeSeriesBuffer<Ohlcv>(
                    new Ohlcv(System.DateTime.UtcNow, 100, 110, 95, 105, 1000)),
                ActiveTabIndex = 0,
                TabSnapshots = ImmutableList<TabSnapshot>.Empty
            };

        private static ChartIdentity BTC  => new("Spot",   "Binance",   "BTCUSDT", "1h");
        private static ChartIdentity ETH  => new("Spot",   "Binance",   "ETHUSDT", "1h");
        private static ChartIdentity SOL  => new("Spot",   "Binance",   "SOLUSDT", "4h");

        // ── 1. Initial state: single tab, TabCount = 1 ─────────────────────────

        [Fact]
        public void InitialState_HasSingleTab()
        {
            var state = WorkspaceState.Initial;
            Assert.Equal(0, state.ActiveTabIndex);
            Assert.Equal(1, state.TabCount);
            Assert.NotNull(state.TabSnapshots);
            Assert.Empty(state.TabSnapshots!);
        }

        // ── 2. AddTabAction: TabCount increases, new tab is empty ──────────────

        [Fact]
        public void AddTab_IncreasesTabCount()
        {
            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();

            var btcState = State(BTC);
            store.EmitState(btcState);
            Assert.Equal(1, store.State.TabCount);

            store.Dispatch(new AddTabAction());
            // MockWorkspaceStore just records action — we test the reducer directly:
            var after = ReduceDirectly(btcState, new AddTabAction());
            Assert.Equal(2, after.TabCount);
            Assert.Equal(1, after.ActiveTabIndex); // Switched to new tab
        }

        [Fact]
        public void AddTab_NewTabIsEmpty()
        {
            var after = ReduceDirectly(State(BTC), new AddTabAction());
            // New tab (index 1) is the active state — Identity should be empty
            Assert.Equal("", after.Identity.Symbol);
            Assert.Equal(1, after.ActiveTabIndex);
        }

        [Fact]
        public void AddTab_OldTabSavedAsSnapshot()
        {
            var btcState = State(BTC);
            var after = ReduceDirectly(btcState, new AddTabAction());

            // Old tab (index 0) should now be in TabSnapshots
            Assert.NotNull(after.TabSnapshots);
            var snap = after.TabSnapshots!.FirstOrDefault(t => t.TabIndex == 0);
            Assert.NotNull(snap);
            Assert.Equal("BTCUSDT", snap!.Identity.Symbol);
        }

        // ── 3. SwitchTabAction: saves current, restores target ─────────────────

        [Fact]
        public void SwitchTab_RestoresTargetIdentity()
        {
            // Tab 0: BTC. Add tab, switch back.
            var state0 = State(BTC);
            var state1 = ReduceDirectly(state0, new AddTabAction()); // now on tab 1 (empty)

            // Switch back to tab 0
            var state2 = ReduceDirectly(state1, new SwitchTabAction(0));
            Assert.Equal("BTCUSDT", state2.Identity.Symbol);
            Assert.Equal(0, state2.ActiveTabIndex);
        }

        [Fact]
        public void SwitchTab_SavesCurrentTabToSnapshots()
        {
            var state0 = State(BTC);
            var state1 = ReduceDirectly(state0, new AddTabAction());

            // Tab 1 is empty; switch to tab 0
            var state2 = ReduceDirectly(state1, new SwitchTabAction(0));

            // Tab 1 should now be in snapshots
            Assert.NotNull(state2.TabSnapshots);
            var snap1 = state2.TabSnapshots!.FirstOrDefault(t => t.TabIndex == 1);
            Assert.NotNull(snap1);
            Assert.Equal("", snap1!.Identity.Symbol); // Tab 1 was empty
        }

        [Fact]
        public void SwitchTab_SameIndex_NoChange()
        {
            var state = State(BTC);
            var after = ReduceDirectly(state, new SwitchTabAction(0)); // switch to same
            Assert.Equal(state.Identity, after.Identity); // No change
            Assert.Equal(0, after.ActiveTabIndex);
        }

        [Fact]
        public void SwitchTab_OutOfRange_NoChange()
        {
            var state = State(BTC); // 1 tab only
            var after = ReduceDirectly(state, new SwitchTabAction(5));
            Assert.Equal(state.Identity, after.Identity);
        }

        // ── 4. Three tabs: round-trip integrity ───────────────────────────────

        [Fact]
        public void ThreeTabs_EachStoresIndependentIdentity()
        {
            var s0 = State(BTC);
            // Add tab 1 (ETH would need to be set after add, but for this test use snapshot injection)
            var s1 = ReduceDirectly(s0, new AddTabAction());
            // Simulate setting ETH on tab 1 by directly modifying identity
            var s1Eth = s1 with { Identity = ETH };

            // Add tab 2
            var s2 = ReduceDirectly(s1Eth, new AddTabAction());
            var s2Sol = s2 with { Identity = SOL };

            // Now we have: active=tab2(SOL), snapshots=[tab0(BTC), tab1(ETH)]
            Assert.Equal("SOLUSDT", s2Sol.Identity.Symbol);
            Assert.Equal(3, s2Sol.TabCount);

            // Switch to tab 0
            var atTab0 = ReduceDirectly(s2Sol, new SwitchTabAction(0));
            Assert.Equal("BTCUSDT", atTab0.Identity.Symbol);

            // Switch to tab 1
            var atTab1 = ReduceDirectly(atTab0, new SwitchTabAction(1));
            Assert.Equal("ETHUSDT", atTab1.Identity.Symbol);
        }

        // ── 5. CloseTabAction ─────────────────────────────────────────────────

        [Fact]
        public void CloseTab_LastTab_NoChange()
        {
            var state = State(BTC);
            var after = ReduceDirectly(state, new CloseTabAction(0));
            Assert.Equal(1, after.TabCount); // Can't close last tab
        }

        [Fact]
        public void CloseInactiveTab_RemovesSnapshot()
        {
            var s0 = State(BTC);
            var s1 = ReduceDirectly(s0, new AddTabAction()); // tab 1 active, tab 0 in snapshots
            var after = ReduceDirectly(s1, new CloseTabAction(0)); // close tab 0 (inactive)

            // Only 1 tab remains
            Assert.Equal(1, after.TabCount);
            Assert.NotNull(after.TabSnapshots);
            Assert.Empty(after.TabSnapshots!);
            Assert.Equal(1, after.ActiveTabIndex); // Tab 1 is still active
        }

        [Fact]
        public void CloseActiveTab_SwitchesToAdjacentTab()
        {
            var s0 = State(BTC);
            var s1 = ReduceDirectly(s0, new AddTabAction()); // tab 1 active

            // Close the active tab (1) — should switch to tab 0
            var after = ReduceDirectly(s1, new CloseTabAction(1));
            Assert.Equal(1, after.TabCount);
            Assert.Equal(0, after.ActiveTabIndex);
            Assert.Equal("BTCUSDT", after.Identity.Symbol);
        }

        // ── 6. Global settings survive tab switch ─────────────────────────────

        [Fact]
        public void GlobalVolume_PersistedAcrossTabSwitch()
        {
            var state = WorkspaceState.Initial with
            {
                Identity = BTC,
                ChartVolume = 0.75f,
                IsSpeechEnabled = false,
                TabSnapshots = ImmutableList<TabSnapshot>.Empty
            };

            var afterAdd = ReduceDirectly(state, new AddTabAction());
            var afterSwitch = ReduceDirectly(afterAdd, new SwitchTabAction(0));

            // Global settings survive across tab operations
            Assert.Equal(0.75f, afterSwitch.ChartVolume);
            Assert.False(afterSwitch.IsSpeechEnabled);
        }

        // ── 7. TabCount computed property ─────────────────────────────────────

        [Fact]
        public void TabCount_IsSnapshotCountPlusOne()
        {
            var state = WorkspaceState.Initial with
            {
                TabSnapshots = ImmutableList.Create(
                    new TabSnapshot(1, ChartIdentity.Empty,
                        TimeSeriesBuffer<Ohlcv>.Empty,
                        ImmutableList<ChartSeries>.Empty,
                        0, null, 0, -1, -1, 0, 100, 20, (0, 0),
                        ImmutableDictionary<string, (double, double)>.Empty,
                        false, false, InteractionContext.Series, null, 0,
                        InitializationStatus.Booting, DataStatus.Idle,
                        false, null, 0, -1))
            };
            Assert.Equal(2, state.TabCount); // 1 active + 1 snapshot
        }

        // ── Helper: run a WorkspaceAction through the real reducer ────────────

        /// <summary>
        /// Constructs a WorkspaceStore with minimal mock dependencies and dispatches
        /// a single action, returning the resulting state. Used to test reducer logic
        /// without the full DI container.
        /// </summary>
        private static WorkspaceState ReduceDirectly(WorkspaceState initial, WorkspaceAction action)
        {
            var bus = new SpyEventBus();

            // Minimal mock implementations for store constructor
            var rangeCalc = new MockViewportRangeCalculator();
            var navSvc    = new MockViewportNavigationService();
            var volSvc    = new MockVolumeStateService();

            var store = new WorkspaceStore(bus, rangeCalc, navSvc, volSvc);
            // Prime the store with our initial state via a direct UpdateSettings dispatch
            store.Dispatch(new UpdateSettingsAction(_ => initial));
            store.Dispatch(action);
            return store.State;
        }
    }

    // ── Minimal test doubles for WorkspaceStore constructor ──────────────────────

    internal class MockViewportRangeCalculator : IViewportRangeCalculator
    {
        public ViewportRangeResult Calculate(WorkspaceState state) =>
            new ViewportRangeResult(state.ViewportRange, state.PaneRanges);
    }

    internal class MockViewportNavigationService : IViewportNavigationService
    {
        public WorkspaceState Navigate(WorkspaceState state, int newIndex) => state with { CurrentDataIndex = newIndex };
        public WorkspaceState Pan(WorkspaceState state, int delta) => state;
        public WorkspaceState Zoom(WorkspaceState state, string direction) => state;
        public WorkspaceState ClampViewportToData(WorkspaceState state) => state;
    }

    internal class MockVolumeStateService : IVolumeStateService
    {
        public WorkspaceState Apply(WorkspaceState state, AdjustVolumeAction action) => state;
        public WorkspaceState ApplyChartVolume(WorkspaceState state, AdjustChartVolumeAction action) => state;
    }
}
