using System.Collections.Generic;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Fetches and caches OHLCV bars for a different timeframe than the chart's active TF.
    /// The composite-strategy condition evaluator uses this to resolve leaves whose
    /// <c>Timeframe</c> field is set (multi-timeframe strategies — e.g. "1h trend up
    /// AND 5m Cipher A buy" becomes two leaves with different Timeframe values).
    ///
    /// Source-of-truth for OHLCV is whatever <see cref="IDataOrchestrator.FetchOhlcvAsync"/>
    /// returns — the orchestrator is already wired through <see cref="IDataCacheService"/>
    /// (SQLite cache) and Polly resilience, so calling it respects API limits and the
    /// on-disk backfill cache automatically. This service adds an in-memory layer on top
    /// keyed by (provider, symbol, timeframe) with a short TTL appropriate to the bar size,
    /// so multiple condition leaves on the same HTF only fetch once per evaluation pass.
    /// </summary>
    public interface IMultiTimeframeDataService
    {
        /// <summary>
        /// Returns up to <paramref name="count"/> recent bars for the given symbol on the
        /// requested higher timeframe. Cached in memory; falls through to
        /// <see cref="IDataOrchestrator.FetchOhlcvAsync"/> on miss / expiry. Returns an
        /// empty list if no provider can serve the request.
        /// </summary>
        /// <param name="market">Market identifier (Spot, Futures, etc.).</param>
        /// <param name="provider">Provider name (e.g. "binance", "alpaca").</param>
        /// <param name="symbol">Symbol (e.g. "BTC/USDT", "AAPL").</param>
        /// <param name="timeframe">Higher timeframe code (e.g. "1h", "4h", "1d").</param>
        /// <param name="count">Maximum number of bars to return.</param>
        Task<IReadOnlyList<Ohlcv>> GetBarsAsync(
            string market, string provider, string symbol, string timeframe, int count);

        /// <summary>
        /// Synchronous accessor for already-cached HTF bars. Used by the condition
        /// evaluator on its hot path — if the cache is cold, returns an empty list and
        /// the next call to <see cref="GetBarsAsync"/> populates it.
        /// </summary>
        IReadOnlyList<Ohlcv> GetCachedBars(string provider, string symbol, string timeframe);

        /// <summary>Drops the cache. Tests + strategy library reload use this.</summary>
        void Clear();

        /// <summary>
        /// Pre-warms an HTF indicator computation cache: fetches the HTF bars (via the same path
        /// as <see cref="GetBarsAsync"/>) and runs <see cref="Indicators.IIndicatorEngine.CalculateAsync"/>
        /// against them, storing the per-component result arrays for sync read by
        /// <see cref="GetCachedIndicator"/>. Called fire-and-forget from
        /// <c>ConfigurableStrategy.Initialize</c> for every unique (Timeframe, IndicatorCode) pair
        /// in the spec's condition tree, so the synchronous evaluator can resolve indicator-based
        /// HTF leaves on the hot path without awaiting.
        /// </summary>
        Task PrewarmIndicatorAsync(
            string market, string provider, string symbol, string timeframe,
            string indicatorCode, System.Collections.Generic.Dictionary<string, object> parameters, int count);

        /// <summary>
        /// Synchronous accessor for an already-pre-warmed HTF indicator. Returns the per-component
        /// result dictionary (component name → values, NaN where not applicable) or null if no
        /// cache entry exists yet. The hot path read used by <c>ConditionEvaluator</c>.
        /// </summary>
        System.Collections.Generic.Dictionary<string, double[]>? GetCachedIndicator(
            string provider, string symbol, string timeframe, string indicatorCode);
    }
}
