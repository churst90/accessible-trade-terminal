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
        /// Causality of each component, parallel to <see cref="ComponentNames"/>. If shorter than
        /// ComponentNames the last entry is repeated; if empty every component is
        /// <see cref="ComponentCausality.Undeclared"/>, which is the default and is deliberately
        /// unusable — an undeclared component is drawn but never offered to a strategy.
        ///
        /// <para>
        /// Built-in providers declare this on their metadata and <c>IndicatorCausalityTests</c>
        /// proves the declaration. A script cannot be covered by a test that does not know it
        /// exists, so for scripts the proof runs at registration instead: the compiled instance is
        /// swept over prefixes and suffixes of a synthetic series and the answer it actually gives
        /// is compared against the answer it claims here. Declaring <see cref="ComponentCausality.Causal"/>
        /// on a component that reads ahead does not get it published — it gets the claim refused.
        /// </para>
        ///
        /// <para>
        /// Declared as a default interface member so that indicators written before this existed,
        /// and Pine ports the transpiler emits, keep compiling. They get Undeclared, which is the
        /// honest answer for code that has never said.
        /// </para>
        /// </summary>
        ComponentCausality[] Causality => Array.Empty<ComponentCausality>();

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
