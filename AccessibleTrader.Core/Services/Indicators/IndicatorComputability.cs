using System;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// The single answer to "can this indicator actually produce a value?".
    ///
    /// <para>
    /// Skender-backed indicators resolve by reflection on <c>"Get" + Code</c>. When the name is
    /// wrong — or the indicator simply is not in the version of Skender we ship — the lookup
    /// returns null, the delegate is never built, and the indicator draws an empty line with no
    /// exception and no log. <c>Ppo</c>, <c>Hv</c>, <c>Tma</c>, <c>Zlema</c> and <c>Eom</c> are in
    /// that state against Skender 2.5.0.
    /// </para>
    ///
    /// <para>
    /// This lived as a private method on <c>IndicatorService</c>, so it filtered the Add Indicator
    /// menu and nothing else. <c>SignalCatalog</c> walks the same providers raw, and therefore went
    /// on offering <c>PPO.Ppo GreaterThan 0</c> as a strategy leaf: the user could not add PPO to a
    /// chart but could build a strategy on it, and the condition was permanently NaN and so
    /// permanently false, with nothing said. Two callers asking the same question of the same
    /// providers must not each hold their own copy of the answer —
    /// <c>SignalCatalogComputabilityTests</c> fails if the two lists disagree.
    /// </para>
    /// </summary>
    internal static class IndicatorComputability
    {
        /// <summary>
        /// Null when the indicator can compute; otherwise a sentence naming why it cannot, phrased
        /// to be spoken to the user (it is used as a strategy-leaf refusal reason).
        /// </summary>
        internal static string? RefusalReason(IIndicatorProvider provider, IndicatorMetadata meta)
        {
            if (provider == null || meta == null) return null;
            if (!provider.GetType().Name.StartsWith("Skender", StringComparison.Ordinal)) return null;
            if (string.IsNullOrEmpty(meta.Code)) return null;
            if (SkenderCalculationCore.CanResolve(meta.Code)) return null;

            return $"the indicator library exposes no Get{SkenderCalculationCore.SkenderMethodName(meta.Code)}, " +
                   "so it can only ever produce empty values";
        }

        internal static bool IsComputable(IIndicatorProvider provider, IndicatorMetadata meta) =>
            RefusalReason(provider, meta) == null;
    }
}
