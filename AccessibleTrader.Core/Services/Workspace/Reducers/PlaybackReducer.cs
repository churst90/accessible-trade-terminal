using System;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Workspace.Reducers
{
    /// <summary>
    /// Reduces playback + accessibility + chart-display toggles.
    /// All of these are simple flag flips or bounded-delta increments — no
    /// collaborator services needed. Grouped together because they all live
    /// on the accessibility/presentation axis.
    /// </summary>
    internal static class PlaybackReducer
    {
        public static WorkspaceState Reduce(WorkspaceState state, WorkspaceAction action) => action switch
        {
            // Playback
            AdjustPlaybackSpeedAction a => state with { PlaybackSpeed = Math.Clamp(state.PlaybackSpeed + a.Delta, 0.1f, 10.0f) },
            SetPlaybackAction a         => state with { IsPlaying = a.IsPlaying, PlaybackScope = a.Scope, IsPaused = false },
            TogglePauseAction           => state with { IsPaused = !state.IsPaused },

            // Accessibility toggles
            ToggleSpeechAction          => state with { IsSpeechEnabled = !state.IsSpeechEnabled },
            ToggleSonificationAction    => state with { IsSonificationEnabled = !state.IsSonificationEnabled },

            // Chart display toggles
            ToggleHeikinAshiAction      => state with { IsHeikinAshi = !state.IsHeikinAshi },
            ToggleLogScaleAction        => state with { IsLogScale = !state.IsLogScale },

            _ => state
        };
    }
}
