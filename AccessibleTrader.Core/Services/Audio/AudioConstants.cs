using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Audio
{
    /// <summary>
    /// Shared constants for the audio/sonification pipeline.
    /// </summary>
    internal static class AudioConstants
    {
        /// <summary>
        /// Pan-width denominator for stereo positioning. Always uses <c>ViewportLength</c> so
        /// audio pan tracks the bar's actual x-position on the canvas: a bar at local index
        /// <c>k</c> of a <c>ViewportLength</c>-slot canvas sits at visual fraction
        /// <c>(k + 0.5) / ViewportLength</c>, which <see cref="CalculatePan"/> maps to the
        /// same stereo position. Audio and visual stay in lockstep regardless of whether
        /// the right-margin future-space is visible (at live edge) or absent (panned back).
        /// </summary>
        internal static int ComputePanWidth(WorkspaceState state) =>
            Math.Max(1, state.ViewportLength);

        /// <summary>
        /// Display types whose non-NaN presence on a bar constitutes an active marker signal.
        /// Used by NavigationSonifier (cluster ticks), NavigationFeedbackManager (signal speech),
        /// and SpeechFormatter (SignalSpeechTemplate path).
        /// </summary>
        internal static readonly HashSet<ComponentDisplayType> MarkerDisplayTypes = new()
        {
            ComponentDisplayType.Dot,
            ComponentDisplayType.ZeroDot,
            ComponentDisplayType.Arrow,
            ComponentDisplayType.Diamond,
            ComponentDisplayType.TriangleUp,
            ComponentDisplayType.TriangleDown,
            ComponentDisplayType.Square,
            ComponentDisplayType.Cross,
        };

        /// <summary>
        /// Computes stereo pan position (-1.0 = full left, +1.0 = full right) from a
        /// zero-based relative index within a viewport of the given width.
        /// Returns 0.0 (centre) when viewportWidth &lt;= 1.
        ///
        /// <para>
        /// The renderer is the authority on where a bar *is*: <c>StandardRenderers</c> draws bar
        /// <c>i</c> at <c>(i * barWidth) + halfBar</c> with <c>barWidth = Width / ViewportLength</c>,
        /// i.e. at visual fraction <c>(i + 0.5) / ViewportLength</c>. This maps that fraction onto
        /// [-1, +1], so the pan of a bar is the pan of its pixel. Until 2026-08-25 this computed
        /// <c>2k/(N−1) − 1</c> instead — an edge-to-edge mapping that treats N as a count of
        /// *gaps* rather than of slots — while the doc comment above described the centre-of-slot
        /// form the renderer actually uses, and <c>LevelCrossingMonitor</c> implemented that form
        /// independently. Two formulas, and the lockstep claim documented against the wrong one.
        /// </para>
        /// </summary>
        internal static double CalculatePan(int relativeIndex, int viewportWidth)
        {
            if (viewportWidth <= 1) return 0.0;
            return Math.Clamp((2.0 * (relativeIndex + 0.5) / viewportWidth) - 1.0, -1.0, 1.0);
        }

        /// <summary>
        /// Phase names for CandleColor display type (Cipher S and any future sentiment overlays).
        /// Index 0 = Max Fear … 10 = Max Euphoria.
        /// </summary>
        internal static readonly string[] PhaseNames =
        {
            "Max Fear", "Fear", "Concern", "Caution", "Mild Caution",
            "Neutral", "Mild Greed", "Greed", "High Greed", "Extreme Greed", "Max Euphoria"
        };
    }
}
