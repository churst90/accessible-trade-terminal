using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Workspace
{
    /// <summary>
    /// THE data-access seam for multi-chart consumers (debt item 7, stage "seam").
    ///
    /// Ask for bars by identity and don't care how they arrive: the FOCUSED chart's
    /// identity answers instantly from the live store (no network); any other
    /// identity fetches through the provider's rate-limited REST path — the same
    /// technique the background monitors use.
    ///
    /// Why this exists: seven singletons assume "one chart identity", and the full
    /// keyed-feed refactor is deliberately deferred behind a trigger (tick-level
    /// background evaluation, split-view rendering, hosted scale — see
    /// docs/TODO.md). Every consumer that binds to THIS interface instead of the
    /// DataManager/DataService singletons makes that eventual refactor a swap
    /// behind an interface rather than a hunt through call sites. New multi-chart
    /// features should take IMarketFeeds, not IDataService.
    /// </summary>
    public interface IMarketFeeds
    {
        /// <summary>True when this identity is the focused chart (bars are live in the store).</summary>
        bool IsLive(ChartIdentity identity);

        /// <summary>
        /// Bars for the identity, newest last: the focused chart's buffer verbatim
        /// (up to <paramref name="maxBars"/> most recent), or a provider fetch for
        /// any other identity. Empty list when nothing is available.
        /// </summary>
        Task<IReadOnlyList<Ohlcv>> GetBarsAsync(ChartIdentity identity, int maxBars, CancellationToken ct = default);
    }

    public sealed class MarketFeeds : IMarketFeeds
    {
        private readonly IWorkspaceStore _store;
        private readonly IDataService _dataService;
        private readonly Services.Feeds.IMarketFeedHub? _hub;

        public MarketFeeds(IWorkspaceStore store, IDataService dataService,
            Services.Feeds.IMarketFeedHub? hub = null)
        {
            _store = store;
            _dataService = dataService;
            _hub = hub;
        }

        public bool IsLive(ChartIdentity identity)
            => identity == _store.State.Identity
               || (_hub?.IsFeedLive(identity) ?? false);

        public async Task<IReadOnlyList<Ohlcv>> GetBarsAsync(ChartIdentity identity, int maxBars, CancellationToken ct = default)
        {
            if (maxBars <= 0) return Array.Empty<Ohlcv>();

            if (identity == _store.State.Identity)
            {
                var data = _store.State.Data;
                if (data == null || data.Count == 0) return Array.Empty<Ohlcv>();
                return Slice(data, maxBars);
            }

            // Keyed-feeds fast path: a live-subscribed background feed is
            // tick-current — serve its buffer and skip the REST round-trip
            // entirely. Background monitors on multi-sub providers evaluate on
            // fresh bars at zero request cost.
            if (_hub != null && _hub.IsFeedLive(identity))
            {
                var feed = _hub.TryGetFeed(identity);
                if (feed != null && feed.Bars.Count > 0)
                    return Slice(feed.Bars, maxBars);
            }

            ct.ThrowIfCancellationRequested();
            var (bars, _) = await _dataService.FetchOhlcvAsync(
                identity.Provider,
                new MarketDataRequest(identity.Market, identity.Symbol, identity.Timeframe, maxBars))
                .ConfigureAwait(false);
            return (IReadOnlyList<Ohlcv>?)bars ?? Array.Empty<Ohlcv>();
        }

        private static List<Ohlcv> Slice(TimeSeriesBuffer<Ohlcv> data, int maxBars)
        {
            int take = Math.Min(maxBars, data.Count);
            var slice = new List<Ohlcv>(take);
            for (int i = data.Count - take; i < data.Count; i++) slice.Add(data[i]);
            return slice;
        }
    }
}
