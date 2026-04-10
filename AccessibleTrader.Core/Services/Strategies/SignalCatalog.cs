using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Default <see cref="ISignalCatalog"/> implementation. Walks every registered
    /// <c>IIndicatorProvider</c> at startup, derives the <see cref="SignalKind"/> from
    /// each component's <c>DisplayType</c>, and caches the resulting flat list.
    /// </summary>
    public class SignalCatalog : ISignalCatalog
    {
        private readonly IEnumerable<IIndicatorProvider> _providers;
        private List<SignalDescriptor> _all = new();
        private Dictionary<string, SignalDescriptor> _byId = new();
        private Dictionary<string, List<SignalDescriptor>> _byIndicator = new();

        public IReadOnlyList<SignalDescriptor> All => _all;

        public SignalCatalog(IEnumerable<IIndicatorProvider> providers)
        {
            _providers = providers;
            Refresh();
        }

        public SignalDescriptor? GetById(string id) =>
            _byId.TryGetValue(id, out var d) ? d : null;

        public IReadOnlyList<SignalDescriptor> GetForIndicator(string indicatorCode) =>
            _byIndicator.TryGetValue(indicatorCode, out var list) ? list : (IReadOnlyList<SignalDescriptor>)System.Array.Empty<SignalDescriptor>();

        public void Refresh()
        {
            var list = new List<SignalDescriptor>();
            foreach (var provider in _providers)
            {
                List<IndicatorMetadata> indicators;
                try { indicators = provider.GetIndicators(); }
                catch { continue; }

                foreach (var ind in indicators)
                {
                    foreach (var comp in ind.Components)
                    {
                        // Build a stable ID. {INDICATOR_CODE}.{ComponentName} survives renames
                        // of friendly labels because the underlying registry code rarely changes.
                        string id = $"{ind.Code}.{comp.Name}";
                        var kind = ClassifyKind(comp.DisplayType.ToString());
                        string label = $"{ind.Name} — {comp.DisplayName ?? comp.Name}";

                        list.Add(new SignalDescriptor(
                            Id: id,
                            IndicatorCode: ind.Code,
                            ComponentName: comp.Name,
                            Kind: kind,
                            DisplayLabel: label
                        ));
                    }
                }
            }

            _all = list;
            _byId = list.ToDictionary(d => d.Id);
            _byIndicator = list.GroupBy(d => d.IndicatorCode)
                               .ToDictionary(g => g.Key, g => g.ToList());
        }

        /// <summary>
        /// Map a component's display type string to a <see cref="SignalKind"/>.
        /// Marker shapes (Dot, Diamond, Cross, Arrow) become <see cref="SignalKind.MarkerFire"/>;
        /// numeric line/area types fall into <see cref="SignalKind.Line"/> or
        /// <see cref="SignalKind.Oscillator"/> depending on whether they live in a sub-pane.
        /// </summary>
        private static SignalKind ClassifyKind(string? displayType)
        {
            if (string.IsNullOrEmpty(displayType)) return SignalKind.Line;
            string t = displayType.Trim();
            if (t.Equals("Dot", System.StringComparison.OrdinalIgnoreCase) ||
                t.Equals("ZeroDot", System.StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Arrow", System.StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Diamond", System.StringComparison.OrdinalIgnoreCase) ||
                t.Equals("TriangleUp", System.StringComparison.OrdinalIgnoreCase) ||
                t.Equals("TriangleDown", System.StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Square", System.StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Cross", System.StringComparison.OrdinalIgnoreCase))
                return SignalKind.MarkerFire;

            if (t.Equals("Cloud", System.StringComparison.OrdinalIgnoreCase) ||
                t.Equals("AreaFill", System.StringComparison.OrdinalIgnoreCase))
                return SignalKind.Cloud;

            return SignalKind.Line;
        }
    }
}
