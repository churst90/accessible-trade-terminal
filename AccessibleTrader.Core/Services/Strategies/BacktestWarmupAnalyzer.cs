using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Default <see cref="IBacktestWarmupAnalyzer"/>. Walks the condition tree, resolves
    /// each leaf's indicator code via <see cref="ISignalCatalog"/>, then asks the matching
    /// <see cref="IIndicatorProvider"/> for its stability window. Returns the max across
    /// all referenced indicators or the floor — whichever is larger.
    ///
    /// We pass an empty parameter dictionary because the strategy spec doesn't currently
    /// carry per-indicator parameter overrides; providers return their default-period
    /// stability window which is what the backtester needs as a floor anyway.
    /// </summary>
    public class BacktestWarmupAnalyzer : IBacktestWarmupAnalyzer
    {
        private readonly ISignalCatalog _catalog;
        private readonly IEnumerable<IIndicatorProvider> _providers;

        public BacktestWarmupAnalyzer(ISignalCatalog catalog, IEnumerable<IIndicatorProvider> providers)
        {
            _catalog = catalog;
            _providers = providers;
        }

        public int RecommendedWarmup(StrategySpec spec, int floor = 50)
        {
            int maxWindow = floor;
            foreach (var code in ReferencedIndicators(spec))
            {
                int window = QueryStabilityWindow(code);
                if (window > maxWindow) maxWindow = window;
            }
            // Apply a 1.2x safety multiplier so we don't sit exactly at the boundary where
            // a single late-converging indicator would still emit unreliable signals.
            return (int)Math.Ceiling(maxWindow * 1.2);
        }

        public IReadOnlyList<string> ReferencedIndicators(StrategySpec spec)
        {
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectCodes(spec.Conditions, codes);
            return codes.ToList();
        }

        private void CollectCodes(ConditionNode node, HashSet<string> sink)
        {
            switch (node)
            {
                case ConditionLeaf leaf:
                {
                    var desc = _catalog.GetById(leaf.SignalDescriptorId);
                    if (desc != null && !string.IsNullOrEmpty(desc.IndicatorCode))
                        sink.Add(desc.IndicatorCode);
                    break;
                }
                case ConditionGroup g:
                {
                    foreach (var c in g.Children) CollectCodes(c, sink);
                    break;
                }
            }
        }

        private int QueryStabilityWindow(string code)
        {
            int max = 0;
            var emptyParams = new Dictionary<string, object>();
            foreach (var provider in _providers)
            {
                List<AccessibleTrader.Sdk.Models.IndicatorMetadata> indicators;
                try { indicators = provider.GetIndicators(); }
                catch { continue; }

                if (!indicators.Any(i => string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase)))
                    continue;

                try
                {
                    int window = provider.GetStabilityWindow(code, emptyParams);
                    if (window > max) max = window;
                }
                catch
                {
                    // Providers may throw on unknown code or empty params — best-effort only.
                }
            }
            return max;
        }
    }
}
