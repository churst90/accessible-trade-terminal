using System;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Audio
{
    /// <summary>
    /// Classifies marker component signals into significance tiers for cluster tick ordering.
    /// Tier 1 (highest) = structural SR/divergence in the main pane.
    /// Tier 2 = divergence or hidden markers anywhere.
    /// Tier 3 = crossover / signal action events.
    /// Tier 4 (lowest) = everything else.
    /// </summary>
    internal static class SignalTierClassifier
    {
        internal static int GetTier(ComponentConfig comp, ChartSeries series)
        {
            string dn = comp.DisplayName ?? comp.Name;

            bool isMainPane = string.IsNullOrEmpty(series.Pane) ||
                              series.Pane.Equals("Main", StringComparison.OrdinalIgnoreCase) ||
                              series.Pane.Equals("Price", StringComparison.OrdinalIgnoreCase);

            if (isMainPane &&
                (comp.DisplayType == ComponentDisplayType.Diamond || comp.DisplayType == ComponentDisplayType.Cross) &&
                (dn.Contains("Divergence", StringComparison.OrdinalIgnoreCase) ||
                 dn.Contains("SR", StringComparison.OrdinalIgnoreCase)))
                return 1;

            if (dn.Contains("Divergence", StringComparison.OrdinalIgnoreCase) ||
                dn.Contains("Hidden", StringComparison.OrdinalIgnoreCase))
                return 2;

            if (dn.Contains("Signal", StringComparison.OrdinalIgnoreCase) ||
                dn.Contains("Buy", StringComparison.OrdinalIgnoreCase) ||
                dn.Contains("Sell", StringComparison.OrdinalIgnoreCase) ||
                dn.Contains("Cross", StringComparison.OrdinalIgnoreCase) ||
                dn.Contains("Manipulation", StringComparison.OrdinalIgnoreCase) ||
                dn.Contains("Exhaustion", StringComparison.OrdinalIgnoreCase))
                return 3;

            return 4;
        }
    }
}
