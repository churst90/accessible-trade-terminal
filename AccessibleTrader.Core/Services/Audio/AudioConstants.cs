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
    }
}
