using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Interfaces
{
    /// <summary>
    /// Optional interface for indicators that can auto-detect optimal parameter values
    /// from the loaded price history — e.g. cycle-length-adaptive window sizing.
    ///
    /// <see cref="IndicatorOrchestrator"/> calls <see cref="SuggestParameters"/> on every full
    /// recalculation (historical load / backfill). It is NOT called on live-tick incremental
    /// updates — only when the historical dataset has materially changed.
    ///
    /// Guard logic (data milestone threshold and change significance) must be implemented
    /// inside this method so each provider controls its own re-detection cadence.
    ///
    /// Returns null when no change is needed — the orchestrator does nothing.
    /// Returns a parameter override dict when a better value is detected:
    ///   - The orchestrator applies the override to the local parameter dict (for this calculation).
    ///   - The orchestrator also persists the override to the store via UpdateSeriesParametersAction.
    ///   - If <paramref name="notificationMessage"/> is non-null, the orchestrator announces it
    ///     via INotificationHub. Set to null for a silent update (minor refinements).
    /// </summary>
    public interface IAdaptiveIndicatorProvider
    {
        /// <summary>
        /// Inspects <paramref name="data"/> and returns a parameter override dictionary,
        /// or <c>null</c> if no change is warranted.
        /// </summary>
        /// <param name="data">Full loaded price history at the time of this recalculation.</param>
        /// <param name="instrumentCode">Symbol/instrument identifier (e.g. "BTCUSD") for per-asset caching.</param>
        /// <param name="currentParams">Current parameter values (object-boxed doubles, as passed to Calculate).</param>
        /// <param name="notificationMessage">
        /// Speech-ready sentence to announce to the user, or null for a silent update.
        /// </param>
        /// <returns>
        /// Dictionary of parameter name → new value (boxed double), or null if no change is needed.
        /// </returns>
        Dictionary<string, object>? SuggestParameters(
            ReadOnlySpan<Ohlcv> data,
            string instrumentCode,
            Dictionary<string, object> currentParams,
            out string? notificationMessage);
    }
}
