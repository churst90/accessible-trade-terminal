using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Interfaces
{
    /// <summary>
    /// Defines a zero-allocation buffer for indicator calculation results.
    /// Indicators write their component values directly into these spans to avoid
    /// dictionary and array allocations during high-frequency updates.
    /// </summary>
    public interface IIndicatorResultBuffer
    {
        /// <summary>
        /// Retrieves a writeable span for a specific component.
        /// </summary>
        Span<double> GetComponentSpan(string componentName);

        /// <summary>
        /// Sets a single value at the specified index for a component.
        /// Used for incremental updates (UpdateLast).
        /// </summary>
        void SetValue(string componentName, int index, double value);

        /// <summary>
        /// Writes dynamic zone bands computed from bar data.
        /// Called by providers like Cipher SR that compute S/R zones at runtime.
        /// IndicatorOrchestrator reads this after Calculate() and applies it to series.Config.ZoneBands.
        /// Pass an empty list to clear all dynamic zones for this indicator.
        /// </summary>
        void WriteZoneBands(string indicatorCode, List<ZoneBandConfig> zoneBands);

        /// <summary>
        /// Returns the zone bands written by <see cref="WriteZoneBands"/> for the given indicator code.
        /// Returns an empty list if none were written.
        /// </summary>
        IReadOnlyList<ZoneBandConfig> ReadZoneBands(string indicatorCode);
    }

    /// <summary>
    /// Defines the contract for an indicator math engine adapter.
    /// Optimized for .NET 10 high-performance streaming with zero-allocation mandates.
    /// </summary>
    public interface IIndicatorProvider
    {
        /// <summary>
        /// Gets the human-readable name of the indicator provider.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Retrieves the metadata for all indicators supported by this provider.
        /// </summary>
        List<IndicatorMetadata> GetIndicators();

        /// <summary>
        /// Calculates the full dataset for an indicator.
        /// PERF: Uses ReadOnlySpan for input and IIndicatorResultBuffer for zero-allocation output.
        /// </summary>
        void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer);

        /// <summary>
        /// Incremental update for the FINAL bar only.
        /// PERF: Avoids re-calculating the entire history.
        /// </summary>
        void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer);
        
        /// <summary>
        /// Returns the stabilization window (warmup period) for the indicator.
        /// </summary>
        int GetStabilityWindow(string code, Dictionary<string, object> parameters);

        /// <summary>
        /// Provides high-detail textual description of the indicator's state at a specific bar.
        /// </summary>
        string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters);

        /// <summary>
        /// Returns a concise, context-aware speech string for a specific component at the given bar.
        /// Called during navigation when the focused component belongs to this indicator's series.
        /// Return null to fall through to the generic speech template.
        /// </summary>
        /// <param name="componentName">Display name of the focused component.</param>
        /// <param name="value">Component value at dataIndex (may be NaN).</param>
        /// <param name="bar">OHLCV bar at dataIndex.</param>
        /// <param name="allComponentData">All component data arrays for this series, keyed by component DisplayName.</param>
        /// <param name="dataIndex">Absolute data index being described.</param>
        string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
            => null; // default: fall through to generic template

        /// <summary>
        /// Returns the default reference level lines for the indicator identified by <paramref name="code"/>.
        /// Each entry specifies the level name, value, color hex, dash style, and audio behavior
        /// (earcon thresholds, OB/OS zone noise amount and type).
        ///
        /// <para>
        /// The <paramref name="code"/> parameter is intentional: a single provider class may handle
        /// multiple indicator codes (e.g. <c>SkenderBoundedOscillatorProvider</c> handles RSI, Stoch,
        /// MFI, CCI, etc. in one class).  Switch on <paramref name="code"/> — after normalising to
        /// upper-case via <c>ToUpperInvariant()</c> — to return the correct level set per indicator.
        /// </para>
        ///
        /// <para>
        /// Single-indicator providers that always return the same set of levels may safely ignore
        /// <paramref name="code"/> and return a constant list.
        /// </para>
        ///
        /// <para>
        /// The orchestrator always passes the same code that was used to locate this provider via
        /// <see cref="GetIndicators"/>, already normalised to the casing used in
        /// <see cref="IndicatorMetadata.Code"/>.  <c>SeriesManagementService</c> further upper-cases
        /// it before the call, so a <c>ToUpperInvariant()</c> switch is the recommended pattern.
        /// </para>
        ///
        /// Return an empty list if the indicator has no reference lines.
        /// </summary>
        /// <param name="code">
        /// The indicator code as registered in <see cref="GetIndicators"/> (e.g. "RSI", "MACD").
        /// Upper-cased by the caller; safe to use directly in a switch expression.
        /// </param>
        List<LevelDescriptor> GetDefaultLevels(string code)
            => new();
    }
}
