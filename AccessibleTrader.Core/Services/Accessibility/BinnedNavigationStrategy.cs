using System;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Handles navigation for binned series like Volume Profiles and Heatmaps.
    /// X-axis moves across time (bars), Y-axis moves through price bins/levels.
    /// </summary>
    public class BinnedNavigationStrategy : INavigationStrategy
    {
        public BinnedNavigationStrategy()
        {
        }

        /// <inheritdoc />
        public NavigationResult NavigateX(WorkspaceState state, int delta)
        {
            var seriesId = state.FocusedSeriesId ?? "candles";
            var s = state.ActiveSeries.FirstOrDefault(x => x.Id == seriesId);

            if (s != null)
            {
                bool isHeatmap = s.Components.Any(c => c.DisplayType == ComponentDisplayType.Heatmap);
                bool isProfile = s.IsProfile
                    || s.Components.Any(c => c.DisplayType == ComponentDisplayType.Profile
                                          || c.DisplayType == ComponentDisplayType.Distribution);

                // Profiles aggregate volume across ALL time — left/right has no meaning.
                // Return silent failure so the audio engine plays nothing.
                if (isProfile && !isHeatmap)
                    return new NavigationResult(false);
            }

            // Heatmaps have a time axis — navigate normally.
            int newIdx = Math.Clamp(state.CurrentDataIndex + delta, 0, state.Data.Count - 1);
            if (newIdx == state.CurrentDataIndex)
                return new NavigationResult(false);

            return new NavigationResult(true, NewIndex: newIdx, Context: InteractionContext.Component);
        }

        /// <inheritdoc />
        public NavigationResult NavigateY(WorkspaceState state, int delta)
        {
            var seriesId = state.FocusedSeriesId ?? "candles";
            var s = state.ActiveSeries.FirstOrDefault(x => x.Id == seriesId);
            if (s == null) return new NavigationResult(false);

            bool isHeatmap = s.Components.Any(c => c.DisplayType == ComponentDisplayType.Heatmap);
            
            int binCount = 0;
            if (isHeatmap)
            {
                // Use the latest non-empty snapshot regardless of cursor position.
                // Cursor may be in historical area (before live session) — backwards search would fail.
                // All non-empty snapshots share the same bin count, so LastOrDefault is always correct.
                var hd = s.Data.HeatmapData;
                if (hd != null)
                    binCount = hd.LastOrDefault(l => l != null && l.Count > 0)?.Count ?? 0;
            }
            else if (s.IsProfile)
            {
                binCount = s.Data.ProfileBins?.Count ?? 0;
            }

            if (binCount == 0) return new NavigationResult(false, FeedbackType: FeedbackType.Error, FeedbackMessage: "No data");

            int currentBin = state.FocusedBinIndex < 0 ? binCount / 2 : state.FocusedBinIndex;
            // delta > 0 is UP
            int newBin = Math.Clamp(currentBin - delta, 0, binCount - 1);

            if (newBin == currentBin)
                return new NavigationResult(false);

            return new NavigationResult(true,
                NewBinIndex: newBin,
                Context: InteractionContext.Bin);
        }
    }
}
