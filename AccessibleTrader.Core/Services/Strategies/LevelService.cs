using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Default <see cref="ILevelService"/>. Iterates every registered <see cref="ILevelProvider"/>
    /// once per query and merges their levels. The aggregator does no caching — providers are
    /// expected to be cheap (they read from already-computed indicator data or workspace
    /// drawings) and the cost of two iterations per bar is negligible compared to indicator
    /// computation cost.
    /// </summary>
    public class LevelService : ILevelService
    {
        private readonly IEnumerable<ILevelProvider> _providers;

        public LevelService(IEnumerable<ILevelProvider> providers)
        {
            _providers = providers;
        }

        public IReadOnlyList<PriceLevel> GetAllLevels(IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            var sink = new List<PriceLevel>();
            foreach (var provider in _providers)
            {
                IReadOnlyList<PriceLevel> levels;
                try { levels = provider.GetLevels(history, state); }
                catch { continue; }
                if (levels != null && levels.Count > 0)
                    sink.AddRange(levels);
            }
            return sink;
        }

        public PriceLevel? NearestBelow(double price, IReadOnlyList<Ohlcv> history, WorkspaceState state, LevelKind? kindFilter = null)
        {
            var all = GetAllLevels(history, state);
            PriceLevel? best = null;
            double bestDist = double.PositiveInfinity;
            foreach (var lvl in all)
            {
                if (lvl.Price >= price) continue;
                if (kindFilter.HasValue && lvl.Kind != kindFilter.Value) continue;
                double d = price - lvl.Price;
                if (d < bestDist) { bestDist = d; best = lvl; }
            }
            return best;
        }

        public PriceLevel? NearestAbove(double price, IReadOnlyList<Ohlcv> history, WorkspaceState state, LevelKind? kindFilter = null)
        {
            var all = GetAllLevels(history, state);
            PriceLevel? best = null;
            double bestDist = double.PositiveInfinity;
            foreach (var lvl in all)
            {
                if (lvl.Price <= price) continue;
                if (kindFilter.HasValue && lvl.Kind != kindFilter.Value) continue;
                double d = lvl.Price - price;
                if (d < bestDist) { bestDist = d; best = lvl; }
            }
            return best;
        }
    }
}
