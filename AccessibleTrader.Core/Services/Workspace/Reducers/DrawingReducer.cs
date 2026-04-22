using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Workspace.Reducers
{
    /// <summary>
    /// Reduces coordinate-entry drawing-placement actions. Two-anchor
    /// placements are handled by leaving the actual drawing creation as a
    /// CommandDispatcher side-effect — this reducer's job is only to keep
    /// the coordinate-entry state machine consistent.
    /// </summary>
    internal static class DrawingReducer
    {
        public static WorkspaceState Reduce(WorkspaceState state, WorkspaceAction action) => action switch
        {
            EnterCoordinateEntryAction a => state with
            {
                IsCoordinateEntryMode = true,
                PendingDrawingTool = a.Tool,
                CoordinateEntryAnchorCount = 0,
                CoordinateEntryAnchor1Index = -1
            },
            SetCoordinateEntryAnchorAction a when state.CoordinateEntryAnchorCount == 0 => state with
            {
                CoordinateEntryAnchor1Index = a.DataIndex,
                CoordinateEntryAnchorCount = 1
            },
            // Second anchor: the CommandDispatcher handles drawing completion as a side-effect;
            // the reducer just resets CE state so the store stays consistent.
            SetCoordinateEntryAnchorAction => state with
            {
                IsCoordinateEntryMode = false,
                PendingDrawingTool = null,
                CoordinateEntryAnchorCount = 0,
                CoordinateEntryAnchor1Index = -1
            },
            ExitCoordinateEntryAction => state with
            {
                IsCoordinateEntryMode = false,
                PendingDrawingTool = null,
                CoordinateEntryAnchorCount = 0,
                CoordinateEntryAnchor1Index = -1
            },
            _ => state
        };
    }
}
