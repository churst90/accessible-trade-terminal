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

                // Re-index remaining snapshots to fill the gap
                var reindexed = snapshots
                    .RemoveAll(t => t.TabIndex == switchTo || t.TabIndex == tabIndex)
                    .Select((t, i) => t with { TabIndex = i >= switchTo ? i : i })
                    .ToImmutableList();

                return RestoreSnapshot(state, targetSnapshot) with
                {
                    TabSnapshots = reindexed,
                    ActiveTabIndex = switchTo
                };
            }
            else
            {
                // Closing an inactive tab — just remove its snapshot
                var newSnapshots = snapshots.RemoveAll(t => t.TabIndex == tabIndex);
                return state with { TabSnapshots = newSnapshots };
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
