using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Interfaces
{
    /// <summary>
    /// Contract for user-defined indicators compiled via Roslyn scripting (Phase 10-D).
    /// Each implementation computes one or more component output arrays from OHLCV input.
    /// </summary>
    public interface ICustomIndicator
    {
        /// <summary>Unique stable identifier (used as IndicatorCode in ChartSeries).</summary>
        string Id { get; }

        /// <summary>Display name shown in the indicator list and properties dialog.</summary>
        string DisplayName { get; }

        /// <summary>Component output names — one array per component (e.g. "Signal", "Histogram").</summary>
        string[] ComponentNames { get; }

        /// <summary>
        /// Recommended display type for each component (parallel to <see cref="ComponentNames"/>).
        /// If shorter than ComponentNames, the last entry is repeated.
        /// </summary>
        ComponentDisplayType[] DisplayTypes { get; }

        /// <summary>Default parameter values exposed to the Properties dialog.</summary>
        Dictionary<string, double> DefaultParameters { get; }

        /// <summary>
        /// Computes all component output arrays for the supplied OHLCV history.
        /// Each returned array must be the same length as <paramref name="data"/>.
        /// Use <c>double.NaN</c> for warm-up bars where values are not yet available.
        /// </summary>
        /// <param name="data">Full price history (oldest first).</param>
        /// <param name="parameters">Active parameter values (may override <see cref="DefaultParameters"/>).</param>
        /// <returns>One array per component in <see cref="ComponentNames"/> order.</returns>
        double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters);
    }
}
