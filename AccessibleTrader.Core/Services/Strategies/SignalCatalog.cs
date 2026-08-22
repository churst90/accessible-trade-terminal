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
    ///
    /// <para>
    /// It publishes only what has been declared causal. This class is the reason the declaration
    /// exists: it is the one place that turns a chart component into something a strategy can be
    /// built on, and until 2026-08-21 it did that for every component of every provider with no
    /// allowlist and no gate — which is how Ichimoku's Chikou Span, a raw 26-bar future price,
    /// became a leaf a backtest could compare against close.
    /// </para>
    ///
    /// <para>
    /// Refused components stay resolvable through <see cref="GetById"/> and are listed in
    /// <see cref="Excluded"/>. They are withheld from <see cref="All"/> — the pickable list — but
    /// a strategy saved before this gate existed must be able to say why its leaf stopped working,
    /// and "unknown signal" would be a lie.
    /// </para>
    /// </summary>
    public class SignalCatalog : ISignalCatalog
    {
        private readonly IEnumerable<IIndicatorProvider> _providers;
        private List<SignalDescriptor> _all = new();
        private List<SignalDescriptor> _excluded = new();
        private Dictionary<string, string> _refusals = new();
        private Dictionary<string, SignalDescriptor> _byId = new();
        private Dictionary<string, List<SignalDescriptor>> _byIndicator = new();

        public IReadOnlyList<SignalDescriptor> All => _all;

        /// <inheritdoc/>
        public IReadOnlyList<SignalDescriptor> Excluded => _excluded;

        /// <inheritdoc/>
        public string? RefusalReason(string id) =>
            _refusals.TryGetValue(id, out var why) ? why : null;

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
            var refused = new List<SignalDescriptor>();
            var reasons = new Dictionary<string, string>();

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

                        var descriptor = new SignalDescriptor(
                            Id: id,
                            IndicatorCode: ind.Code,
                            ComponentName: comp.Name,
                            Kind: kind,
                            DisplayLabel: label,
                            Causality: CausalityContract.Effective(ind, comp)
                        );

                        string? why = CausalityContract.RefusalReason(ind, comp);
                        if (why == null)
                        {
                            list.Add(descriptor);
                        }
                        else
                        {
                            refused.Add(descriptor);
                            reasons[id] = $"{id} is not available as a strategy signal: {why}.";
                        }
                    }
                }
            }

            _all = list;
            _excluded = refused;
            _refusals = reasons;
            // Both halves are addressable by ID. A refused leaf resolving to null would read as a
            // typo in the strategy spec; resolving to a descriptor that knows it is not causal
            // lets the evaluator refuse it out loud.
            //
            // First registration wins on a duplicate ID rather than throwing. Two providers can
            // legitimately answer to one indicator code — MACloudProvider has a subclass kept as a
            // name alias — and a ToDictionary here turns that into an exception during DI
            // construction, which takes the whole app down over a shadowed strategy leaf.
            _byId = new Dictionary<string, SignalDescriptor>();
            foreach (var d in list.Concat(refused))
                _byId.TryAdd(d.Id, d);
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
