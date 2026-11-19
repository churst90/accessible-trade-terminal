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
            // The price scale is ONE choice to the user — Regular, Logarithmic, Percent,
            // Indexed to 100 — so the two fields never disagree. Turning log ON is the
            // Logarithmic entry and puts the mode back to Regular; turning it off leaves the
            // mode alone (it can only have been Regular while log was on). A non-Regular mode
            // is an affine relabel of the regular scale and is never logarithmic.
            ToggleLogScaleAction        => state with
            {
                IsLogScale = !state.IsLogScale,
                ScaleMode = !state.IsLogScale ? PriceScaleMode.Regular : state.ScaleMode,
            },
            SetPriceScaleAction a       => state with
            {
                ScaleMode = a.Mode,
                IsLogScale = a.Mode == PriceScaleMode.Regular && a.Log,
            },

            _ => state
        };
    }
}
