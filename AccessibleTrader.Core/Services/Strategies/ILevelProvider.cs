using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Surfaces price levels (support, resistance, pivots, profile nodes, indicator lines)
    /// to the composite-strategy pipeline. Implementations are registered via DI as
    /// <c>IEnumerable&lt;ILevelProvider&gt;</c> and aggregated by <see cref="ILevelService"/>.
    /// Each provider owns one source of levels — drawn horizontal lines, swing pivots,
    /// Cipher SR pivots, Ichimoku Kijun/Kumo, VPVR POC/HVN/LVN, etc. Adding a new level
    /// source means dropping a new provider class into <c>Core/Services/Strategies/Levels/</c>
    /// and registering it; nothing else has to change.
    /// </summary>
    public interface ILevelProvider
    {
        /// <summary>Stable identifier used in level <c>Source</c> labels (e.g. "drawn", "swing", "ichimoku").</summary>
        string SourceId { get; }

        /// <summary>
        /// Returns all levels currently visible from this provider given the workspace state and
        /// recent bar history. The list may be empty (provider has no levels for the current chart).
        /// </summary>
        IReadOnlyList<PriceLevel> GetLevels(IReadOnlyList<Ohlcv> history, WorkspaceState state);
    }

    /// <summary>
    /// Aggregator over every registered <see cref="ILevelProvider"/>. Provides convenience
    /// queries used by <c>RiskPlanResolver</c> and <c>ConditionEvaluator</c>: nearest level
    /// above/below a reference price, optionally filtered by <see cref="LevelKind"/>.
    /// </summary>
    public interface ILevelService
    {
        /// <summary>All levels from all providers, unsorted.</summary>
        IReadOnlyList<PriceLevel> GetAllLevels(IReadOnlyList<Ohlcv> history, WorkspaceState state);

        /// <summary>
        /// Nearest level strictly below <paramref name="price"/>. When <paramref name="kindFilter"/>
        /// is null, all kinds are considered (returning the nearest support-like level).
        /// Returns null when no level qualifies.
        /// </summary>
        PriceLevel? NearestBelow(double price, IReadOnlyList<Ohlcv> history, WorkspaceState state, LevelKind? kindFilter = null);

        /// <summary>
        /// Nearest level strictly above <paramref name="price"/>. Same filtering rules as
        /// <see cref="NearestBelow"/>.
        /// </summary>
        PriceLevel? NearestAbove(double price, IReadOnlyList<Ohlcv> history, WorkspaceState state, LevelKind? kindFilter = null);
    }
}
