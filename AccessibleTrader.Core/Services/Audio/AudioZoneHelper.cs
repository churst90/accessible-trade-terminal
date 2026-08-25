using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Audio
{
    /// <summary>
    /// Shared helper for computing OB/OS zone noise from per-level audio metadata.
    /// Custom providers declare their OB/OS levels via IIndicatorProvider.GetDefaultLevels(),
    /// which SeriesManagementService injects into series.Config.Levels (including ZoneNoiseAmount
    /// and ZoneNoiseType). This method reads those levels at audio-compute time — one source of
    /// truth for all providers.
    ///
    /// A component can restrict which levels it responds to via ComponentConfig.SubscribedLevelNames:
    ///   null  = subscribe to all levels (default)
    ///   empty = subscribe to none — no zone noise for this component
    ///   list  = only the named levels apply (matched by LevelConfig.Name, case-insensitive)
    /// </summary>
    internal static class AudioZoneHelper
    {
        /// <summary>
        /// Returns the zone noise amount and noise type for the given component value.
        /// Scans all visible levels the component subscribes to, and returns the highest
        /// ZoneNoiseAmount for any OB/OS threshold exceeded (OB: val &gt; threshold; OS: val &lt; threshold).
        /// Returns (0f, "pink") when the value is not in any subscribed zone.
        /// </summary>
        internal static (float NoiseAmount, string NoiseType) ComputeZoneNoise(
            ChartSeries series, ComponentConfig comp, double val)
        {
            // Empty subscription list = opt out of all zone noise.
            if (comp.SubscribedLevelNames is { Count: 0 }) return (0f, "pink");

            float maxNoise = 0f;
            string noiseType = "pink";

            foreach (var lc in series.Config.Levels)
            {
                if (!lc.IsVisible || lc.ZoneNoiseAmount <= 0f) continue;

                // If the component has an explicit subscription list, skip levels not in it.
                if (comp.SubscribedLevelNames is { Count: > 0 } &&
                    !comp.SubscribedLevelNames.Contains(lc.Name, StringComparer.OrdinalIgnoreCase))
                    continue;

                string n = lc.Name;
                bool isOb = n.Contains("Overbought", StringComparison.OrdinalIgnoreCase) ||
                            n.Contains("Extreme OB",  StringComparison.OrdinalIgnoreCase);
                bool isOs = n.Contains("Oversold",    StringComparison.OrdinalIgnoreCase) ||
                            n.Contains("Extreme OS",  StringComparison.OrdinalIgnoreCase);

                bool inZone = (isOb && val > lc.Value) || (isOs && val < lc.Value);
                if (inZone && lc.ZoneNoiseAmount > maxNoise)
                {
                    maxNoise  = lc.ZoneNoiseAmount;
                    noiseType = lc.ZoneNoiseType ?? "pink";
                }
            }

            return (maxNoise, noiseType);
        }

        /// <summary>
        /// Returns true when the component subscribes to the named level.
        /// Used by the earcon-crossing check in ISonificationStrategy to gate boundary clicks.
        /// </summary>
        internal static bool ComponentSubscribesTo(ComponentConfig comp, string levelName)
        {
            // null = subscribe to all
            if (comp.SubscribedLevelNames is null) return true;
            // empty = subscribe to none
            if (comp.SubscribedLevelNames.Count == 0) return false;
            return comp.SubscribedLevelNames.Contains(levelName, StringComparer.OrdinalIgnoreCase);
        }
    }
}
