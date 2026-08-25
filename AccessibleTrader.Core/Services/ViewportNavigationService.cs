using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services;

/// <summary>
/// Handles all viewport navigation logic: pan, zoom, navigate, and viewport clamping.
/// Extracted from WorkspaceStore to keep the reducer thin.
///
/// DESIGN: ViewportLength represents TOTAL canvas slots including the right margin
/// (RightMarginBars). The last real data bar always lands at slot
/// (ViewportLength - RightMarginBars - 1). Slots beyond that are empty future space
/// for trendline projection. The effective data window is always:
///   effectiveWindow = ViewportLength - RightMarginBars  (minimum 1)
/// </summary>
public sealed class ViewportNavigationService : IViewportNavigationService
{
    /// <summary>Navigate cursor to an absolute data index, scrolling viewport if needed.</summary>
    public WorkspaceState Navigate(WorkspaceState state, int targetIndex)
    {
        if (state.Data.Count == 0) return state;
        int newIdx = Math.Clamp(targetIndex, 0, state.Data.Count - 1);
        int effectiveWindow = Math.Max(1, state.ViewportLength - state.RightMarginBars);

        // Cursor travel range must match what the renderer shows. If the viewport is at
        // the live edge, only the first `effectiveWindow` slots hold data (the rest is
        // reserved right-margin future-space). If panned back into history, every slot
        // holds data, so the full `ViewportLength` is legal cursor territory.
        int barsAvailableToRight = Math.Max(0, state.Data.Count - state.ViewportStartIndex);
        bool atLiveEdge = barsAvailableToRight <= effectiveWindow;
        int cursorWindow = atLiveEdge ? effectiveWindow : state.ViewportLength;

        int newStart = state.ViewportStartIndex;
        if (newIdx < state.ViewportStartIndex)
            newStart = newIdx;
        else if (newIdx >= state.ViewportStartIndex + cursorWindow)
        {
            // User navigated past the visible area — scroll forward by the minimum needed.
            // Anchor the cursor inside the new visible area; the viewport-start cap below
            // (maxStart) will stop us at the live edge, preserving the right-margin gap.
            newStart = newIdx - cursorWindow + 1;
        }

        int maxStart = Math.Max(0, state.Data.Count - effectiveWindow);
        newStart = Math.Clamp(newStart, 0, maxStart);

        if (newIdx == state.CurrentDataIndex && newStart == state.ViewportStartIndex)
            return state;

        return state with { CurrentDataIndex = newIdx, ViewportStartIndex = newStart };
    }

    /// <summary>Pan viewport by <paramref name="delta"/> bars, keeping cursor unchanged.</summary>
    public WorkspaceState Pan(WorkspaceState state, int delta)
    {
        int effectiveWindow = Math.Max(1, state.ViewportLength - state.RightMarginBars);
        int maxStart = Math.Max(0, state.Data.Count - effectiveWindow);
        int newStart = Math.Clamp(state.ViewportStartIndex + delta, 0, maxStart);

        if (newStart == state.ViewportStartIndex)
            return state;

        return state with { ViewportStartIndex = newStart };
    }

    /// <summary>
    /// Zoom viewport by ~20% of its current length.
    /// Anchors to the last visible data bar so the right margin stays consistent.
    /// Allows zooming all the way out to show all data plus the right margin.
    /// </summary>
    public WorkspaceState Zoom(WorkspaceState state, string direction)
    {
        int step = Math.Max(1, (int)(state.ViewportLength * 0.2));
        // Maximum: show all data + right margin. Minimum: RightMarginBars + 10 real bars.
        int maxLength = (state.Data != null && state.Data.Count > 0)
            ? state.Data.Count + state.RightMarginBars
            : 5000 + state.RightMarginBars;
        int minLength = state.RightMarginBars + 10;
        int newLength = direction == "IN"
            ? Math.Clamp(state.ViewportLength - step, minLength, maxLength)
            : Math.Clamp(state.ViewportLength + step, minLength, maxLength);

        // Anchor to the last visible data bar so the right margin slot count stays constant.
        int effectiveWindow    = Math.Max(1, state.ViewportLength    - state.RightMarginBars);
        int newEffectiveWindow = Math.Max(1, newLength               - state.RightMarginBars);
        int lastDataBar        = state.ViewportStartIndex + effectiveWindow - 1;
        int dataMax            = (state.Data != null && state.Data.Count > 0) ? state.Data.Count - 1 : 0;
        int newStart           = Math.Clamp(lastDataBar - newEffectiveWindow + 1, 0, dataMax);

        return state with { ViewportLength = newLength, ViewportStartIndex = newStart };
    }

    /// <summary>
    /// Adjusts ViewportStartIndex so the viewport never points past available data.
    /// ViewportLength is NOT changed — it legitimately exceeds Data.Count by RightMarginBars.
    /// </summary>
    public WorkspaceState ClampViewportToData(WorkspaceState state)
    {
        if (state.Data.Count == 0) return state;

        int effectiveWindow = Math.Max(1, Math.Min(state.Data.Count, state.ViewportLength - state.RightMarginBars));
        int maxStart        = Math.Max(0, state.Data.Count - effectiveWindow);
        int clampedStart    = Math.Clamp(state.ViewportStartIndex, 0, maxStart);

        // ViewportLength is intentionally NOT clamped to Data.Count — the extra slots
        // are the right margin (future space). Only the start index is corrected.
        return state with { ViewportStartIndex = clampedStart };
    }
}
