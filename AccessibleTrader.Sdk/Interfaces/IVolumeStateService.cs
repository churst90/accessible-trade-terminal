using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Interfaces;

public interface IVolumeStateService
{
    WorkspaceState Apply(WorkspaceState state, AdjustVolumeAction action);
    WorkspaceState ApplyChartVolume(WorkspaceState state, AdjustChartVolumeAction action);
}
