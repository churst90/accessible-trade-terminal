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
        /// </summary>
        internal static double CalculatePan(int relativeIndex, int viewportWidth)
        {
            if (viewportWidth <= 1) return 0.0;
            return Math.Clamp((2.0 * relativeIndex / (viewportWidth - 1)) - 1.0, -1.0, 1.0);
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
