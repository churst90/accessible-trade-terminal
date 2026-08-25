using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Enumerates every signal source the user can select in the strategy builder.
    /// Built once at startup by walking <c>IIndicatorProvider.GetIndicators()</c> for every
    /// registered provider and emitting one <see cref="SignalDescriptor"/> per
    /// <c>IndicatorComponentMetadata</c>. Stable IDs (<c>{indicatorCode}.{componentName}</c>)
    /// let strategies persist forward across indicator updates.
    /// </summary>
    public interface ISignalCatalog
    {
        /// <summary>
        /// Every signal descriptor a strategy may be built on — those whose component declared
        /// <see cref="AccessibleTrader.Sdk.Models.ComponentCausality.Causal"/>. Order is
        /// provider-then-indicator-then-component.
        /// </summary>
        IReadOnlyList<SignalDescriptor> All { get; }

        /// <summary>
        /// The components that exist but are withheld from <see cref="All"/> — undeclared, or
        /// declared look-ahead. Present so the refusal is inspectable rather than a silent absence;
        /// <see cref="RefusalReason"/> gives the sentence for a given ID.
        /// </summary>
        /// <remarks>
        /// Defaulted to empty so a hand-written stub that hands out a fixed list of descriptors
        /// does not have to model a gate it never applies. <see cref="SignalCatalog"/> overrides it.
        /// </remarks>
        IReadOnlyList<SignalDescriptor> Excluded => System.Array.Empty<SignalDescriptor>();

        /// <summary>
        /// Why the given signal ID is unavailable, or null when it is available (or unknown).
        /// </summary>
        string? RefusalReason(string id) => null;

        /// <summary>Look up a descriptor by its stable ID. Returns null if no match.</summary>
        SignalDescriptor? GetById(string id);

        /// <summary>All descriptors belonging to a single indicator (e.g. all Cipher A components).</summary>
        IReadOnlyList<SignalDescriptor> GetForIndicator(string indicatorCode);

        /// <summary>Force a refresh — call when a new indicator provider is registered at runtime.</summary>
        void Refresh();
    }
}
