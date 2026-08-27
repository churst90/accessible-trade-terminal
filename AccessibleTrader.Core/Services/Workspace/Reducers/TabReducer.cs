using System.Collections.Immutable;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Workspace.Reducers
{
    /// <summary>
    /// Reduces multi-tab workspace switching + per-tab pane layout.
    /// Tab switching captures the current per-tab fields into a
    /// <see cref="TabSnapshot"/> and restores the target tab's snapshot into
    /// the live state. Global settings (volume, speech toggles, playback
    /// speed, etc.) deliberately stay outside the snapshot and persist
    /// across tab switches.
    /// </summary>
    internal static class TabReducer
    {
        public static WorkspaceState Reduce(WorkspaceState state, WorkspaceAction action) => action switch
        {
            // Multi-tab
            AddTabAction            => AddTab(state),
            CloseTabAction a        => CloseTab(state, a.TabIndex),
            SwitchTabAction a       => SwitchTab(state, a.TargetIndex),

            // Pane layout
            ResizePaneAction a      => ResizePane(state, a.PaneName, a.Delta),
            ScrollIndicatorPanesAction a => state with
            {
                IndicatorPaneScrollIndex = Math.Max(0, state.IndicatorPaneScrollIndex + a.Delta)
            },
            SetPaneHeightRatiosAction a  => state with { PaneHeightRatios = a.Ratios },

            _ => state
        };

        /// <summary>
        /// Public label formatter — used by WorkspaceStore.Dispatch to build the
        /// announcement for SwitchTab/CloseTab actions after the reducer returns.
        /// </summary>
        public static string GetTabLabel(ChartIdentity identity) =>
            string.IsNullOrEmpty(identity.Symbol)   ? "New Tab" :
            string.IsNullOrEmpty(identity.Provider) ? $"{identity.Symbol} {identity.Timeframe}" :
            $"{identity.Symbol} {identity.Timeframe} on {identity.Provider}";

        /// <summary>
        /// Captures all per-tab fields from a WorkspaceState into a frozen TabSnapshot.
        /// Global settings (volume, speech, playback speed, etc.) are deliberately excluded.
        /// </summary>
        private static TabSnapshot SnapshotFromState(WorkspaceState s) => new TabSnapshot(
            TabIndex: s.ActiveTabIndex,
            Identity: s.Identity,
            Data: s.Data,
            ActiveSeries: s.ActiveSeries,
            FocusedSeriesIndex: s.FocusedSeriesIndex,
            FocusedSeriesId: s.FocusedSeriesId,
            FocusedComponentIndex: s.FocusedComponentIndex,
            FocusedBinIndex: s.FocusedBinIndex,
            CurrentDataIndex: s.CurrentDataIndex,
            ViewportStartIndex: s.ViewportStartIndex,
            ViewportLength: s.ViewportLength,
            RightMarginBars: s.RightMarginBars,
            ViewportRange: s.ViewportRange,
            PaneRanges: s.PaneRanges,
            IsHeikinAshi: s.IsHeikinAshi,
            IsLogScale: s.IsLogScale,
            LastInteractionContext: s.LastInteractionContext,
            PaneHeightRatios: s.PaneHeightRatios,
            IndicatorPaneScrollIndex: s.IndicatorPaneScrollIndex,
            InitStatus: s.InitStatus == InitializationStatus.Loading ? InitializationStatus.Ready : s.InitStatus,
            DataStatus: s.DataStatus == DataStatus.LoadingHistorical ? DataStatus.Ready : s.DataStatus,
            IsCoordinateEntryMode: false, // Always reset CE mode on tab switch
            PendingDrawingTool: null,
            CoordinateEntryAnchorCount: 0,
            CoordinateEntryAnchor1Index: -1,
            PrimarySeriesId: s.PrimarySeriesId,
            CurrentDataShape: s.CurrentDataShape,
            SymbolDisplayName: s.SymbolDisplayName
        );

        /// <summary>Restores all per-tab fields from a TabSnapshot into a WorkspaceState.</summary>
        private static WorkspaceState RestoreSnapshot(WorkspaceState state, TabSnapshot snap) => state with
        {
            Identity = snap.Identity,
            Data = snap.Data,
            ActiveSeries = snap.ActiveSeries,
            FocusedSeriesIndex = snap.FocusedSeriesIndex,
            FocusedSeriesId = snap.FocusedSeriesId,
            FocusedComponentIndex = snap.FocusedComponentIndex,
            FocusedBinIndex = snap.FocusedBinIndex,
            CurrentDataIndex = snap.CurrentDataIndex,
            ViewportStartIndex = snap.ViewportStartIndex,
            ViewportLength = snap.ViewportLength,
            RightMarginBars = snap.RightMarginBars,
            ViewportRange = snap.ViewportRange,
            PaneRanges = snap.PaneRanges,
            IsHeikinAshi = snap.IsHeikinAshi,
            IsLogScale = snap.IsLogScale,
            LastInteractionContext = snap.LastInteractionContext,
            PaneHeightRatios = snap.PaneHeightRatios,
            IndicatorPaneScrollIndex = snap.IndicatorPaneScrollIndex,
            InitStatus = snap.InitStatus,
            DataStatus = snap.DataStatus,
            IsCoordinateEntryMode = false,
            PendingDrawingTool = null,
            CoordinateEntryAnchorCount = 0,
            CoordinateEntryAnchor1Index = -1,
            PrimarySeriesId = snap.PrimarySeriesId,
            CurrentDataShape = snap.CurrentDataShape,
            SymbolDisplayName = snap.SymbolDisplayName
        };

        private static WorkspaceState AddTab(WorkspaceState state)
        {
            // Save current tab as snapshot; new tab gets the next sequential index.
            var currentSnapshot = SnapshotFromState(state);
            int newIndex = state.TabCount; // TabCount = snapshots.Count + 1

            var newSnapshots = (state.TabSnapshots ?? ImmutableList<TabSnapshot>.Empty).Add(currentSnapshot);

            // New tab starts from a clean initial chart state with the new index.
            var newTabBase = WorkspaceState.Initial;
            return RestoreSnapshot(state, new TabSnapshot(
                TabIndex: newIndex,
                Identity: ChartIdentity.Empty,
                Data: newTabBase.Data,
                ActiveSeries: newTabBase.ActiveSeries,
                FocusedSeriesIndex: 0,
                FocusedSeriesId: null,
                FocusedComponentIndex: 0,
                FocusedBinIndex: -1,
                CurrentDataIndex: -1,
                ViewportStartIndex: 0,
                ViewportLength: state.ViewportLength,   // carry over viewport preference
                RightMarginBars: state.RightMarginBars, // carry over right margin
                ViewportRange: (0, 0),
                PaneRanges: ImmutableDictionary<string, (double, double)>.Empty,
                IsHeikinAshi: false,
                IsLogScale: false,
                LastInteractionContext: InteractionContext.Series,
                PaneHeightRatios: null,
                IndicatorPaneScrollIndex: 0,
                InitStatus: InitializationStatus.Booting,
                DataStatus: DataStatus.Idle,
                IsCoordinateEntryMode: false,
                PendingDrawingTool: null,
                CoordinateEntryAnchorCount: 0,
                CoordinateEntryAnchor1Index: -1,
                PrimarySeriesId: "candles",
                CurrentDataShape: Sdk.Plugins.ProviderDataShape.Ohlcv,
                SymbolDisplayName: ""
            )) with
            {
                TabSnapshots = newSnapshots,
                ActiveTabIndex = newIndex
            };
        }

        private static WorkspaceState SwitchTab(WorkspaceState state, int targetIndex)
        {
            int tabCount = state.TabCount;
            if (targetIndex == state.ActiveTabIndex || targetIndex < 0 || targetIndex >= tabCount) return state;

            var snapshots = state.TabSnapshots ?? ImmutableList<TabSnapshot>.Empty;

            // Find target snapshot
            var targetSnapshot = snapshots.FirstOrDefault(t => t.TabIndex == targetIndex);
            if (targetSnapshot == null) return state; // Target doesn't exist

            // Save current state to snapshot
            var currentSnapshot = SnapshotFromState(state);

            // Update snapshot list: remove target (now active), add current (now inactive)
            var newSnapshots = snapshots
                .RemoveAll(t => t.TabIndex == targetIndex)
                .Add(currentSnapshot);

            return RestoreSnapshot(state, targetSnapshot) with
            {
                TabSnapshots = newSnapshots,
                ActiveTabIndex = targetIndex
            };
        }

        /// <summary>
        /// <paramref name="index"/> after the tab at <paramref name="closed"/> is removed:
        /// everything above it moves down one, everything below is untouched. Tab indices are
        /// a dense 0..TabCount-1 range and this is the only thing that keeps them dense.
        /// </summary>
        private static int ShiftDownPast(int index, int closed) => index > closed ? index - 1 : index;

        private static WorkspaceState CloseTab(WorkspaceState state, int tabIndex)
        {
            int tabCount = state.TabCount;
            if (tabCount <= 1) return state; // Can't close the last tab

            var snapshots = state.TabSnapshots ?? ImmutableList<TabSnapshot>.Empty;

            if (tabIndex == state.ActiveTabIndex)
            {
                // Closing the active tab — switch to an adjacent one first
                int switchTo = tabIndex > 0 ? tabIndex - 1 : 1;

                // Find the tab we're switching to
                var targetSnapshot = snapshots.FirstOrDefault(t => t.TabIndex == switchTo);
                if (targetSnapshot == null) return state;

                // Close the gap by RANK, never by list position.
                //
                // This used to be
                //   .Select((t, i) => t with { TabIndex = i >= switchTo ? i : i })
                // whose two ternary arms are both `i`, so it always assigned the
                // ENUMERATION POSITION. The snapshot list is not kept sorted by TabIndex —
                // SwitchTab appends the outgoing tab to the end — so position is unrelated to
                // identity. With four tabs: AddTab x3 gives snapshots [0,1,2] active 3;
                // SwitchTab(0) gives snapshots [1,2,3] active 0; CloseTab(0) then produced
                // reindexed [snap2 -> 0, snap3 -> 1] with ActiveTabIndex 1, i.e. live indices
                // {0, 1(active), 1} for a TabCount of 3. Index 2 did not exist, so
                // TabBar.GetAllTabs rendered two tabs while claiming three, SwitchTabAction(2)
                // found no snapshot and returned state unchanged, and old tab 3 — its symbol,
                // its indicators, its drawings — was unreachable for the rest of the session.
                // A subsequent CloseTab(1) then dropped two tabs at once.
                //
                // Closing index N means every index above N shifts down by one. That is the
                // whole rule, and it is a function of the tab's own index, not of where it
                // happens to sit in the list.
                var reindexed = snapshots
                    .RemoveAll(t => t.TabIndex == switchTo || t.TabIndex == tabIndex)
                    .Select(t => t with { TabIndex = ShiftDownPast(t.TabIndex, tabIndex) })
                    .ToImmutableList();

                return RestoreSnapshot(state, targetSnapshot) with
                {
                    TabSnapshots = reindexed,
                    ActiveTabIndex = ShiftDownPast(switchTo, tabIndex)
                };
            }
            else
            {
                // Closing an inactive tab. The same renumbering applies — this branch used to
                // drop the snapshot and leave every higher index where it was, so closing tab
                // 1 of four left live indices {0, 2, 3(active)} for a TabCount of 3 and the
                // ACTIVE tab's own index was past the end of what the tab bar would render.
                // Not filed with the active-tab case above; same defect, same fix.
                var newSnapshots = snapshots
                    .RemoveAll(t => t.TabIndex == tabIndex)
                    .Select(t => t with { TabIndex = ShiftDownPast(t.TabIndex, tabIndex) })
                    .ToImmutableList();

                return state with
                {
                    TabSnapshots = newSnapshots,
                    ActiveTabIndex = ShiftDownPast(state.ActiveTabIndex, tabIndex)
                };
            }
        }

        private static WorkspaceState ResizePane(WorkspaceState state, string paneName, float delta)
        {
            var ratios = state.PaneHeightRatios ?? ImmutableDictionary<string, float>.Empty;
            ratios.TryGetValue(paneName, out float current);
            // If not yet set, start from a reasonable default (0.15 per indicator pane)
            if (current == 0f) current = 0.15f;
            float newRatio = Math.Clamp(current + delta, 0.05f, 0.60f);
            return state with { PaneHeightRatios = ratios.SetItem(paneName, newRatio) };
        }
    }
}
