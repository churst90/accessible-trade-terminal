using System.Collections.Generic;
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
        /// <summary>All known signal descriptors. Order is provider-then-indicator-then-component.</summary>
        IReadOnlyList<SignalDescriptor> All { get; }

        /// <summary>Look up a descriptor by its stable ID. Returns null if no match.</summary>
        SignalDescriptor? GetById(string id);

        /// <summary>All descriptors belonging to a single indicator (e.g. all Cipher A components).</summary>
        IReadOnlyList<SignalDescriptor> GetForIndicator(string indicatorCode);

        /// <summary>Force a refresh — call when a new indicator provider is registered at runtime.</summary>
        void Refresh();
    }
}
