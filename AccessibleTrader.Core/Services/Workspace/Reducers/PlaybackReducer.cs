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
            SetPlaybackSpeedAction a    => state with { PlaybackSpeed = Math.Clamp(a.Speed, 0.1f, 10.0f) },
            SetPlaybackAction a         => state with { IsPlaying = a.IsPlaying, PlaybackScope = a.Scope, IsPaused = false },
            TogglePauseAction           => state with { IsPaused = !state.IsPaused },

            // Accessibility toggles
            ToggleSpeechAction          => state with { IsSpeechEnabled = !state.IsSpeechEnabled },
            ToggleSonificationAction    => state with { IsSonificationEnabled = !state.IsSonificationEnabled },
            ToggleEventSpeechAction     => state with { IsEventSpeechEnabled = !state.IsEventSpeechEnabled },
            ToggleEarconsAction         => state with { IsEarconsEnabled = !state.IsEarconsEnabled },

            // Chart display toggles
            //
            // Turning Heikin-Ashi ON is a choice of the CANDLES chart under the transform, so
            // it puts the style back to Candles: the user sees one chart type, not a flag
            // crossed with one. Line and Area are raw-close displays and can never carry it.
            ToggleHeikinAshiAction      => state with
            {
                IsHeikinAshi = !state.IsHeikinAshi,
                ChartStyle = !state.IsHeikinAshi ? PriceChartStyle.Candles : state.ChartStyle,
            },
            SetChartStyleAction a       => state with
            {
                ChartStyle = a.Style,
                // Line and Area always draw the raw close, so they can never carry the
                // transform; Heikin-Ashi only exists on the candles chart.
                IsHeikinAshi = a.Style == PriceChartStyle.Candles && a.HeikinAshi,
            },
            ToggleLogScaleAction        => state with { IsLogScale = !state.IsLogScale },

            _ => state
        };
    }
}
