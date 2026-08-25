using System;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Handles standard time-series navigation: X-axis moves across time, 
    /// Y-axis moves through specific series components (e.g. Open, High, Low, Close).
    /// </summary>
    public class PointNavigationStrategy : INavigationStrategy
    {
        /// <inheritdoc />
        public NavigationResult NavigateX(WorkspaceState state, int delta)
        {
            // There is no bar to move to on an empty chart, and Math.Clamp would THROW here
            // (min 0 > max -1) rather than refuse. Every sibling strategy already guards this;
            // this one did not, and an empty chart is now ordinary — a load that returned no
            // bars clears Data rather than leaving the previous symbol's on screen.
            if (state.Data == null || state.Data.Count == 0) return new NavigationResult(false);

            int newIdx = Math.Clamp(state.CurrentDataIndex + delta, 0, state.Data.Count - 1);
            if (newIdx == state.CurrentDataIndex) 
                return new NavigationResult(false);

            return new NavigationResult(true, NewIndex: newIdx, Context: state.LastInteractionContext);
        }

        /// <inheritdoc />
        public NavigationResult NavigateY(WorkspaceState state, int delta)
        {
            var seriesId = state.FocusedSeriesId ?? "candles";
            var s = state.ActiveSeries.FirstOrDefault(x => x.Id == seriesId);
            if (s == null || s.Components.Count == 0) return new NavigationResult(false);

            // Visit every component including hidden ones — SpeechFormatter announces hidden components
            // as "[Name]: hidden" so the user always knows where they are during Y navigation.
            int newComp = s.ClampComponent(state.FocusedComponentIndex + delta);
            if (newComp == state.FocusedComponentIndex) return new NavigationResult(false);
            return new NavigationResult(true, NewComponentIndex: newComp, Context: InteractionContext.Component);
        }
    }
}
