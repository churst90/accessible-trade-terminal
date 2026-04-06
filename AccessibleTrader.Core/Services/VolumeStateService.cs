using System;
using System.Collections.Immutable;
using System.Linq;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services;

/// <summary>
/// Handles all volume adjustment reducer logic for the WorkspaceStore.
/// Supports chart-level, series-level, and component-level volume changes.
/// </summary>
public sealed class VolumeStateService : IVolumeStateService
{
    /// <summary>Apply a series or component volume adjustment.</summary>
    public WorkspaceState Apply(WorkspaceState state, AdjustVolumeAction action)
    {
        var targetId = action.SeriesId ?? state.FocusedSeriesId;
        var newList = state.ActiveSeries.Select(s =>
        {
            if (s.Id != targetId) return s;
            var updated = s.Clone();

            if (!string.IsNullOrEmpty(action.ComponentName))
            {
                foreach (var comp in updated.Components)
                {
                    if (comp.Name.Equals(action.ComponentName, StringComparison.OrdinalIgnoreCase))
                    {
                        comp.Volume = Math.Clamp(comp.Volume + action.Delta, 0.0f, 1.0f);
                        break;
                    }
                }
            }
            else
            {
                updated.Volume = Math.Clamp(updated.Volume + action.Delta, 0.0f, 1.0f);
            }

            return updated;
        }).ToImmutableList();

        return state with { ActiveSeries = newList };
    }

    /// <summary>Apply a chart-level (master) volume change.</summary>
    public WorkspaceState ApplyChartVolume(WorkspaceState state, AdjustChartVolumeAction action)
    {
        return state with { ChartVolume = Math.Clamp(state.ChartVolume + action.Delta, 0.0f, 1.0f) };
    }
}
