using System;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public interface IDataManager
    {
        TimeSeriesBuffer<Ohlcv> Data { get; }
        ChartIdentity Identity { get; set; }
        Task RefreshDataAsync(CancellationToken ct = default);
        /// <summary>
        /// Restores the data cache from a saved tab snapshot and performs an incremental
        /// gap-fill of any bars that arrived while the tab was inactive.
        /// Unlike <see cref="RefreshDataAsync"/>, this preserves the full scrollback history
        /// from the snapshot instead of discarding it with a fresh 200-bar fetch.
        /// Falls back to <see cref="RefreshDataAsync"/> if the snapshot is empty.
        /// </summary>
        Task CatchUpFromSnapshotAsync(TimeSeriesBuffer<Ohlcv> snapshotData, CancellationToken ct = default);
        Task PrependOlderDataAsync();
        Task StartLiveUpdates();
        Task StopLiveUpdatesAsync();
        Task<(System.Collections.Generic.List<OrderBookEntry> Bids, System.Collections.Generic.List<OrderBookEntry> Asks)> GetOrderBookAsync();
        event Action? DataUpdated;
        event Action<string>? ErrorOccurred;
    }

    /// <summary>
    /// The focused-feed binder. Since the keyed-feeds refactor
    /// (docs/KEYED_FEEDS_DESIGN.md) the buffer lifecycle lives in per-identity
    /// <see cref="ChartFeed"/>s inside the <see cref="IMarketFeedHub"/>; this class
    /// binds whichever feed holds FOCUS to the workspace store — dispatching data
    /// updates, zoom/navigate on initial load, and the LoadingHistorical status
    /// around scrollback prepends — and keeps the legacy IDataManager surface
    /// stable for existing consumers. New multi-chart code should take
    /// IMarketFeedHub or IMarketFeeds, not this interface.
    /// </summary>
    public class DataManager : IDataManager, IDisposable
    {
        private readonly IMarketFeedHub _hub;
        private readonly IWorkspaceStore _store;
        private readonly IEventBus _eventBus;
        private readonly ILogger<DataManager> _logger;
        private readonly IServiceProvider _serviceProvider;

        public TimeSeriesBuffer<Ohlcv> Data => _hub.FocusedFeed?.Bars ?? TimeSeriesBuffer<Ohlcv>.Empty;

        public ChartIdentity Identity
        {
            get => _hub.FocusedFeed?.Identity ?? ChartIdentity.Empty;
            set => _hub.SetFocus(value);
        }

        public event Action? DataUpdated;
        public event Action<string>? ErrorOccurred;

        public DataManager(
            IMarketFeedHub hub,
            IWorkspaceStore store,
            IEventBus eventBus,
            ILogger<DataManager> logger,
            IServiceProvider serviceProvider)
        {
            _hub = hub;
            _store = store;
            _eventBus = eventBus;
            _logger = logger;
            _serviceProvider = serviceProvider;

            _hub.FocusedFeedUpdated += OnFocusedFeedUpdated;
        }

        // Live ticks reach the store through this path; the operation methods below
        // dispatch their own results inline (matching the original pipeline's exact
        // ordering), so only the live kinds are handled here. DataUpdated is NOT
        // raised for live ticks — that long-standing gap and its Phase C fix are
        // documented in docs/KEYED_FEEDS_DESIGN.md.
        private void OnFocusedFeedUpdated(ChartFeed feed, FeedUpdateKind kind)
        {
            if (kind is FeedUpdateKind.LiveAppend or FeedUpdateKind.LiveReplace)
            {
                var bars = feed.Bars;
                // Diagnostic (Debug) for "the title price sometimes stops updating": these lines
                // mean the FOCUSED feed IS pushing live closes to the store. If they keep coming
                // while the browser title stays stale, the gap is downstream (render/title); if
                // they STOP while price still moves, the focused live subscription stalled or a
                // tab handoff routed ticks to the background path instead of the focused one.
                if (bars.Count > 0)
                    _logger.LogDebug("DataManager: focused {Kind} {Symbol} close {Close}.",
                        kind, feed.Identity.Symbol, bars[bars.Count - 1].Close);
                _store.Dispatch(new UpdateDataAction(bars, false));
            }
        }

        public async Task RefreshDataAsync(CancellationToken ct = default)
        {
            var feed = _hub.FocusedFeed;
            if (feed == null || string.IsNullOrEmpty(feed.Identity.Symbol)) return;

            try
            {
                if (!await feed.RefreshAsync(ct).ConfigureAwait(false)) return;

                _store.Dispatch(new UpdateDataAction(feed.Bars, IsInitialLoad: true));
                _store.Dispatch(new ZoomAction(100));
                _store.Dispatch(new NavigateAction(feed.Bars.Count - 1));

                DataUpdated?.Invoke();

                SafeFireAndForget.Run(StartLiveUpdates, _logger, "StartLiveUpdates");
            }
            catch (OperationCanceledException) { /* Superseded by a newer tab-switch refresh — no-op */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Refresh failed for {Symbol}.", feed.Identity.Symbol);
                ErrorOccurred?.Invoke(ex.Message);
            }
        }

        public async Task CatchUpFromSnapshotAsync(TimeSeriesBuffer<Ohlcv> snapshotData, CancellationToken ct = default)
        {
            if (snapshotData.Count == 0)
            {
                await RefreshDataAsync(ct).ConfigureAwait(false);
                return;
            }

            var feed = _hub.FocusedFeed;
            if (feed == null || string.IsNullOrEmpty(feed.Identity.Symbol)) return;

            try
            {
                // Keyed-feeds fast path: the target identity's feed may still be WARM —
                // it was this tab's buffer when the tab lost focus, and a background
                // live subscription (or a recent visit) kept it current. Bind it
                // instantly instead of restoring the older snapshot. Both guards
                // matter: same-or-fresher at the live edge AND at least the
                // snapshot's scrollback (a feed live-started from empty has fresh
                // bars but no history — it must NOT replace the snapshot).
                var warm = feed.Bars;
                bool feedIsWarm = warm.Count > 0
                    && warm[^1].Date >= snapshotData[^1].Date
                    && warm[0].Date <= snapshotData[0].Date;

                if (feedIsWarm)
                {
                    _logger.LogInformation(
                        "DataManager: warm feed hit for {Symbol} — {Count} bars bound with no snapshot restore.",
                        feed.Identity.Symbol, warm.Count);
                    _store.Dispatch(new UpdateDataAction(warm, IsInitialLoad: false));
                    DataUpdated?.Invoke();

                    ct.ThrowIfCancellationRequested();

                    // Gap-fill EVEN when the feed is live-subscribed: the handoff
                    // from an independent subscription to the focused pump can
                    // miss a bar close, and a live feed's last bar may be stale
                    // by one final-close update. One fetch per switch is the
                    // pre-refactor cost; the instant bind above already happened.
                    if (await feed.GapFillAsync(ct).ConfigureAwait(false))
                    {
                        _store.Dispatch(new UpdateDataAction(feed.Bars, IsInitialLoad: false));
                        DataUpdated?.Invoke();
                    }
                }
                else
                {
                    _logger.LogInformation("DataManager: Restoring {Count} bars from snapshot for {Symbol}.",
                        snapshotData.Count, feed.Identity.Symbol);

                    // Step 1 — Restore snapshot immediately so the store sees the full history
                    // without waiting for a network round-trip. IsInitialLoad=false so the
                    // snapshot-restored cursor/viewport from WorkspaceState is preserved.
                    feed.RestoreSnapshot(snapshotData);
                    _store.Dispatch(new UpdateDataAction(feed.Bars, IsInitialLoad: false));
                    DataUpdated?.Invoke();

                    ct.ThrowIfCancellationRequested();

                    // Step 2 — Gap-fill: append only the bars that arrived while the tab
                    // was inactive, without touching the preserved scrollback.
                    if (await feed.GapFillAsync(ct).ConfigureAwait(false))
                    {
                        _store.Dispatch(new UpdateDataAction(feed.Bars, IsInitialLoad: false));
                        DataUpdated?.Invoke();
                    }
                }

                // Step 3 — Force a full indicator recalculation so component arrays reflect
                // the restored + gap-filled dataset. Uses a sentinel ID since the handler
                // ignores the series ID and just triggers forceFull=true.
                _eventBus.Publish(new IndicatorUpdatedEvent("__catchup__"));

                // Step 4 — Start live updates to keep the tab current going forward.
                SafeFireAndForget.Run(StartLiveUpdates, _logger, "StartLiveUpdates");
            }
            catch (OperationCanceledException) { /* Superseded by a newer tab-switch — no-op */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CatchUp failed for {Symbol}.", feed.Identity.Symbol);
                // Fall back to a full refresh so the tab is never left in a broken
                // state — honoring ct so a SUPERSEDING tab switch can cancel the
                // fallback instead of having it clobber the next tab's data.
                try { await RefreshDataAsync(ct).ConfigureAwait(false); }
                catch (Exception ex2) { _logger.LogWarning(ex2, "CatchUpFromSnapshotAsync fallback refresh failed"); }
            }
        }

        public async Task PrependOlderDataAsync()
        {
            var feed = _hub.FocusedFeed;
            if (feed == null || string.IsNullOrEmpty(feed.Identity.Symbol)) return;

            bool started = false;
            try
            {
                int prepended = await feed.PrependOlderAsync(onStarted: () =>
                {
                    started = true;
                    _store.Dispatch(new SetDataStatusAction(DataStatus.LoadingHistorical));
                }).ConfigureAwait(false);

                if (prepended > 0)
                {
                    _store.Dispatch(new UpdateDataAction(feed.Bars, IsInitialLoad: false));
                    DataUpdated?.Invoke();
                }
            }
            finally
            {
                // Always reset status after a backfill attempt (success, empty, or
                // error) — but only when THIS call flipped it: a drop-if-busy skip
                // must not clear the status owned by the in-flight prepend.
                if (started && _store.State.DataStatus == DataStatus.LoadingHistorical)
                    _store.Dispatch(new SetDataStatusAction(DataStatus.Ready));
            }
        }

        public Task StartLiveUpdates() => _hub.StartFocusedLiveAsync();

        public Task StopLiveUpdatesAsync() => _hub.StopFocusedLiveAsync();

        public async Task<(System.Collections.Generic.List<OrderBookEntry> Bids, System.Collections.Generic.List<OrderBookEntry> Asks)> GetOrderBookAsync()
        {
            var dataService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<IDataService>(_serviceProvider);
            if (dataService == null) return (new(), new());
            return await dataService.GetOrderBookAsync(Identity.Provider, Identity.Symbol).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _hub.FocusedFeedUpdated -= OnFocusedFeedUpdated;
        }
    }
}
