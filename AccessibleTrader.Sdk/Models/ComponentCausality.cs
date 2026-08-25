using System;

namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Whether a component's value at bar <c>j</c> is derived only from bars at or before <c>j</c>.
    ///
    /// <para>
    /// This exists because <c>SignalCatalog</c> publishes EVERY component of EVERY provider as a
    /// strategy leaf <c>{CODE}.{ComponentName}</c>. There is no allowlist. So "is this component
    /// look-ahead-safe" is never a chart-cosmetics question — it is a backtest-validity question
    /// for all of them. A component that plots a future value is a correct plotting convention and
    /// a catastrophic data convention: a condition on it returns a spectacular, entirely fake edge.
    /// </para>
    ///
    /// <para>
    /// The declaration is a claim, not a proof. <c>IndicatorCausalityTests</c> is the proof: it runs
    /// every provider over <c>bars.Take(k)</c> and over the full series and requires every component
    /// declared <see cref="Causal"/> to agree on the shared prefix.
    /// </para>
    /// </summary>
    public enum ComponentCausality
    {
        /// <summary>
        /// Nobody has said. The default, and deliberately unusable: <c>SignalCatalog</c> refuses to
        /// publish it, so a new component is invisible to the strategy builder until its author
        /// makes a decision. Silence is the one answer that cannot be wrong by accident.
        /// </summary>
        Undeclared = 0,

        /// <summary>
        /// The value at bar j uses only bars ≤ j — including the parameters it was computed with.
        /// A window derived from <c>data.Length</c> breaks this even when the maths is causal,
        /// because bar j then answers differently depending on how much history happened to be
        /// loaded after it. Safe as a strategy leaf.
        /// </summary>
        Causal,

        /// <summary>
        /// The value at bar j depends on bars after j, and that is intended — a displaced plot
        /// (Ichimoku's Chikou Span), a marker stamped at the pivot bar it describes rather than at
        /// the bar the pivot was confirmable, or a centred moving average. Legitimate on a chart
        /// and in navigation speech, which are both a look at history. Never published as a
        /// strategy leaf: a backtest reading it trades on information that did not exist yet.
        /// </summary>
        Lookahead,
    }

    /// <summary>
    /// Resolves the causality that actually applies to a component: its own declaration when it has
    /// one, otherwise its indicator's. Indicators declare once and components override only where
    /// they differ, which is the common shape — a wholly causal oscillator says so on one line, and
    /// Ichimoku says <c>Causal</c> once and marks the single displaced span.
    /// </summary>
    public static class CausalityContract
    {
        /// <summary>The causality in force for <paramref name="component"/> within <paramref name="indicator"/>.</summary>
        public static ComponentCausality Effective(IndicatorMetadata indicator, IndicatorComponentMetadata component)
        {
            ArgumentNullException.ThrowIfNull(indicator);
            ArgumentNullException.ThrowIfNull(component);
            return component.Causality ?? indicator.Causality;
        }

        /// <summary>
        /// True when the component may be offered to the strategy builder as a leaf.
        /// Only <see cref="ComponentCausality.Causal"/> qualifies — <see cref="ComponentCausality.Undeclared"/>
        /// is refused rather than assumed, because the assumption is what produces the fake edge.
        /// </summary>
        public static bool IsPublishable(IndicatorMetadata indicator, IndicatorComponentMetadata component) =>
            Effective(indicator, component) == ComponentCausality.Causal;

        /// <summary>
        /// The causality a scripted indicator declared for the component at
        /// <paramref name="componentIndex"/>. Follows the same "shorter array repeats its last
        /// entry" rule as <c>ICustomIndicator.DisplayTypes</c>, and an empty array means the script
        /// has said nothing about any of them.
        /// </summary>
        public static ComponentCausality Declared(ComponentCausality[]? declared, int componentIndex)
        {
            if (declared == null || declared.Length == 0 || componentIndex < 0)
                return ComponentCausality.Undeclared;
            return declared[Math.Min(componentIndex, declared.Length - 1)];
        }

        /// <summary>
        /// Why a component was withheld from the catalog, phrased for a log line or a test failure.
        /// Returns null when it was published.
        /// </summary>
        public static string? RefusalReason(IndicatorMetadata indicator, IndicatorComponentMetadata component) =>
            Effective(indicator, component) switch
            {
                ComponentCausality.Causal => null,
                ComponentCausality.Lookahead =>
                    "declared Lookahead — its value at a bar depends on later bars, so a strategy " +
                    "condition on it would read the future",
                _ =>
                    "declares no causality — set Causality on the component or on its IndicatorMetadata. " +
                    "A component is publishable only once someone has established that its value at a " +
                    "bar uses no later bar",
            };
    }
}
