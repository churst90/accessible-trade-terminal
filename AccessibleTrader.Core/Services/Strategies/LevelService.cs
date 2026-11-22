using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using Microsoft.Extensions.Logging;

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
        private readonly ILogger<LevelService>? _logger;

        // Per thread: the resolver reads it straight after its own query on the same thread,
        // and two strategies (or a backtest beside a live one) querying at once must not see
        // each other's failure.
        [ThreadStatic] private static string? t_lastFailure;

        // What has been logged, so a source failing on every bar logs once, not every bar.
        private readonly HashSet<string> _logged = new();

        public LevelService(IEnumerable<ILevelProvider> providers, ILogger<LevelService>? logger = null)
        {
            _providers = providers;
            _logger = logger;
        }

        public string? LastFailure => t_lastFailure;

        public IReadOnlyList<PriceLevel> GetAllLevels(IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            var sink = new List<PriceLevel>();
            List<string>? failed = null;
            foreach (var provider in _providers)
            {
                IReadOnlyList<PriceLevel> levels;
                // Skipped, so the other sources still count, but no longer silently: this was
                // `catch { continue; }`, and a stop "below support" whose support source threw
                // looked exactly like a market with no support (audit, archive 12682).
                try { levels = provider.GetLevels(history, state); }
                catch (Exception ex)
                {
                    string what = $"the {SourceName(provider.SourceId)} levels ({ex.Message})";
                    (failed ??= new()).Add(what);
                    bool first;
                    lock (_logged) first = _logged.Add(provider.SourceId + "\n" + ex.GetType().FullName + "\n" + ex.Message);
                    if (first) _logger?.LogWarning(ex, "Level source {Source} failed; its levels are left out.", provider.SourceId);
                    continue;
                }
                if (levels != null && levels.Count > 0)
                    sink.AddRange(levels);
            }
            t_lastFailure = failed == null ? null : string.Join(" and ", failed);
            return sink;
        }

        /// <summary>The spoken name of a level source.</summary>
        internal static string SourceName(string sourceId) => sourceId switch
        {
            "cipher_sr" => "Cipher SR",
            "swing"     => "swing point",
            "drawn"     => "drawn line",
            "ichimoku"  => "Ichimoku",
            "profile"   => "volume profile",
            _           => sourceId,
        };

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
