using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Interfaces;

public interface IViewportNavigationService
{
    WorkspaceState Navigate(WorkspaceState state, int targetIndex);
    WorkspaceState Pan(WorkspaceState state, int delta);
    WorkspaceState Zoom(WorkspaceState state, string direction);
    WorkspaceState ClampViewportToData(WorkspaceState state);
}
