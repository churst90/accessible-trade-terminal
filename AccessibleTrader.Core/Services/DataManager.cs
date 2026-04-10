using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public interface IDataManager
    {
        TimeSeriesBuffer<Ohlcv> Data { get; }
        ChartIdentity Identity { get; set; }
        IObservable<TimeSeriesBuffer<Ohlcv>> DataStream { get; }
        IObservable<TimeSeriesBuffer<Ohlcv>> InitialLoadStream { get; }
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
        Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync();
        event Action? DataUpdated;
        event Action<string>? ErrorOccurred;
    }

    /// <summary>
    /// Manages the chart's OHLCV data lifecycle.
    /// PHASE 3: Implements strict 200/100/200 pagination and respects provider rate limits.
    /// </summary>
    public class DataManager : IDataManager, IDisposable
    {
        private const int MaxBarsInCache = 5000;
        private readonly IDataOrchestrator _orchestrator;
        private readonly IWorkspaceStore _store;
        private readonly IEventBus _eventBus;
        private readonly ILogger<DataManager> _logger;
        private readonly IServiceProvider _serviceProvider;
        private TimeSeriesBuffer<Ohlcv> _cache = TimeSeriesBuffer<Ohlcv>.Empty;
        private readonly Subject<TimeSeriesBuffer<Ohlcv>> _dataStream = new();
        private readonly Subject<TimeSeriesBuffer<Ohlcv>> _initialLoadStream = new();
        private CancellationTokenSource? _liveCts;
        private readonly SemaphoreSlim _prependLock = new(1, 1);

        public TimeSeriesBuffer<Ohlcv> Data => _cache;
        public ChartIdentity Identity { get; set; } = ChartIdentity.Empty;
        public IObservable<TimeSeriesBuffer<Ohlcv>> DataStream => _dataStream.AsObservable();
        public IObservable<TimeSeriesBuffer<Ohlcv>> InitialLoadStream => _initialLoadStream.AsObservable();

        public event Action? DataUpdated;
        public event Action<string>? ErrorOccurred;

        public DataManager(
            IDataOrchestrator orchestrator,
            IWorkspaceStore store,
            IEventBus eventBus,
            ILogger<DataManager> logger,
            IServiceProvider serviceProvider)
        {
            _orchestrator = orchestrator;
            _store = store;
            _eventBus = eventBus;
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task RefreshDataAsync(CancellationToken ct = default)
        {
            try
            {
                var identity = Identity;
                if (string.IsNullOrEmpty(identity.Symbol)) return;

                _logger.LogInformation("DataManager: Refreshing data for {Symbol} (Initial Load: 200 bars).", identity.Symbol);

                var newData = await _orchestrator.FetchOhlcvAsync(identity.Market, identity.Provider, identity.Symbol, identity.Timeframe, limit: 200).ConfigureAwait(false);

                // Bail out if a newer tab-switch refresh has superseded this one.
                ct.ThrowIfCancellationRequested();

                if (newData != null && newData.Any())
                {
                    _cache = new TimeSeriesBuffer<Ohlcv>(newData);

                    _store.Dispatch(new UpdateDataAction(_cache, IsInitialLoad: true));
                    _store.Dispatch(new ZoomAction(100));
                    _store.Dispatch(new NavigateAction(_cache.Count - 1));

                    NotifyDataUpdate(isInitialLoad: true);
                    _initialLoadStream.OnNext(_cache);

                    SafeFireAndForget.Run(StartLiveUpdates, _logger, "StartLiveUpdates");
                }
            }
            catch (OperationCanceledException) { /* Superseded by a newer tab-switch refresh — no-op */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Refresh failed for {Symbol}.", Identity.Symbol);
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

            try
            {
                var identity = Identity;
                if (string.IsNullOrEmpty(identity.Symbol)) return;

                _logger.LogInformation("DataManager: Restoring {Count} bars from snapshot for {Symbol}.", snapshotData.Count, identity.Symbol);

                // Step 1 — Restore snapshot immediately so the store sees the full history
                // without waiting for a network round-trip. IsInitialLoad=false so the
                // snapshot-restored cursor/viewport from WorkspaceState is preserved.
                _cache = snapshotData;
                _store.Dispatch(new UpdateDataAction(_cache, IsInitialLoad: false));
                NotifyDataUpdate(isInitialLoad: false);

                ct.ThrowIfCancellationRequested();

                // Step 2 — Gap-fill: fetch recent bars and merge only those newer than
                // the last bar in the snapshot. This catches up on bars that arrived
                // while the tab was inactive without touching the preserved scrollback.
                var lastKnownDate = snapshotData[^1].Date;
                var recent = await _orchestrator.FetchOhlcvAsync(
                    identity.Market, identity.Provider, identity.Symbol, identity.Timeframe, limit: 200).ConfigureAwait(false);

                ct.ThrowIfCancellationRequested();

                if (recent != null && recent.Any())
                {
                    var gapBars = recent
                        .Where(b => b.Date > lastKnownDate)
                        .OrderBy(b => b.Date)
                        .ToList();

                    if (gapBars.Any())
                    {
                        foreach (var bar in gapBars)
                        {
                            _cache = _cache.Append(bar);
                            if (_cache.Count > MaxBarsInCache)
                                _cache = _cache.RemoveFirst();
                        }
                        _logger.LogInformation("DataManager: Gap-filled {Count} bars for {Symbol}.", gapBars.Count, identity.Symbol);
                    }
                    else
                    {
                        // No new bars — but update the live bar in case it changed intra-bar.
                        var latest = recent.Last();
                        if (latest.Date == lastKnownDate)
                            _cache = _cache.ReplaceLast(latest);
                    }

                    _store.Dispatch(new UpdateDataAction(_cache, IsInitialLoad: false));
                    NotifyDataUpdate(isInitialLoad: false);
                }

                // Step 3 — Force a full indicator recalculation so component arrays reflect
                // the restored + gap-filled dataset. Uses a sentinal ID since the handler
                // ignores the series ID and just triggers forceFull=true.
                _eventBus.Publish(new IndicatorUpdatedEvent("__catchup__"));

                // Step 4 — Start live updates to keep the tab current going forward.
                SafeFireAndForget.Run(StartLiveUpdates, _logger, "StartLiveUpdates");
            }
            catch (OperationCanceledException) { /* Superseded by a newer tab-switch — no-op */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CatchUp failed for {Symbol}.", Identity.Symbol);
                // Fall back to a full refresh so the tab is never left in a broken state.
                try { await RefreshDataAsync(default).ConfigureAwait(false); } catch (Exception ex2) { _logger.LogWarning(ex2, "CatchUpFromSnapshotAsync fallback refresh failed"); }
            }
        }

        public async Task PrependOlderDataAsync()
        {
            // SCROLLBACK CONCURRENCY — Option 1 (Drop-if-busy):
            // If a prepend is already in progress, silently discard this request.
            // The SemaphoreSlim zero-timeout try is the authoritative guard.
            //
            // Option 2 (Timer debounce — not implemented):
            //   Introduce a 250 ms debounce timer at the keyboard/action dispatch layer.
            //   Each Left/Home keypress resets the timer; one prepend fires when the user pauses.
            //   Best UX for rapid keyboard scrolling — batches input without dropping intent.
            //   Implement by wrapping the PrependOlderDataAsync call site with a
            //   CancellableDebounce<T> helper that cancels the previous pending Task.Delay.
            //
            // Option 3 (Replace-queued — not implemented):
            //   Allow exactly one queued request behind an in-flight prepend.
            //   A second request while one is running sets a _pendingPrepend flag; subsequent
            //   requests are dropped (no unbounded queue). When the in-flight prepend completes
            //   it checks the flag and fires one more prepend before returning.
            //   Ensures the most-recent scroll intent always executes without pileup.
            if (string.IsNullOrEmpty(Identity.Symbol)) return;
            if (!await _prependLock.WaitAsync(0).ConfigureAwait(false)) return;
            try
            {

                _store.Dispatch(new SetDataStatusAction(DataStatus.LoadingHistorical));

                if (_cache.Count == 0) return;
                var firstBar = _cache[0];

                long since = new DateTimeOffset(firstBar.Date).ToUnixTimeMilliseconds();
                
                _logger.LogInformation("DataManager: Prepending 200 bars before {Date}.", firstBar.Date);
                
                var olderData = await _orchestrator.FetchOhlcvAsync(
                    Identity.Market, Identity.Provider, Identity.Symbol, Identity.Timeframe,
                    limit: 200, until: since - 1).ConfigureAwait(false);

                if (olderData != null && olderData.Any())
                {
                    var uniqueOlder = olderData.Where(o => o.Date < firstBar.Date).ToList();
                    if (uniqueOlder.Any())
                    {
                        _cache = _cache.PrependRange(uniqueOlder);
                        while (_cache.Count > MaxBarsInCache)
                            _cache = _cache.RemoveLast();
                        _store.Dispatch(new UpdateDataAction(_cache, IsInitialLoad: false));
                        _logger.LogInformation("Prepended {Count} bars.", uniqueOlder.Count);
                        NotifyDataUpdate(false);
                    }
                }
            }
            finally
            {

                _prependLock.Release();
                // Always reset status after a backfill attempt (success, empty, or error).
                if (_store.State.DataStatus == DataStatus.LoadingHistorical)
                    _store.Dispatch(new SetDataStatusAction(DataStatus.Ready));
            }
        }

        private void NotifyDataUpdate(bool isInitialLoad)
        {
            DataUpdated?.Invoke();
            _dataStream.OnNext(Data);
        }

                public async Task StartLiveUpdates()
        {
            await StopLiveUpdatesAsync().ConfigureAwait(false);
            _liveCts = new CancellationTokenSource();
            var token = _liveCts.Token;

            SafeFireAndForget.Run(async () =>
            {
                await foreach (var tick in _orchestrator.LiveStream.ReadAllAsync(token))
                {
                    // Drop ticks that arrive while historical backfill is in progress.
                    // The prepend operation replaces _cache wholesale; merging a live tick
                    // during that window would corrupt the bar sequence.
                    if (_store.State.DataStatus == DataStatus.LoadingHistorical)
                        continue;

                    var lastBar = _cache.Count > 0 ? _cache[_cache.Count - 1] : default;
                    if (_cache.Count == 0 || tick.Date > lastBar.Date) {
                        _cache = _cache.Append(tick);
                        if (_cache.Count > 2000) _cache = _cache.RemoveFirst();
                    } else {
                        _cache = _cache.ReplaceLast(tick);
                    }
                    _store.Dispatch(new UpdateDataAction(_cache, false));
                }
            }, _logger, "LiveUpdatesLoop");

            await _orchestrator.StartLiveStreamAsync(Identity.Market, Identity.Provider, Identity.Symbol, Identity.Timeframe).ConfigureAwait(false);
        }

        public async Task StopLiveUpdatesAsync()
        {
            _liveCts?.Cancel(); _liveCts?.Dispose();
            _liveCts = null;
            await _orchestrator.StopLiveStreamAsync().ConfigureAwait(false);
        }

        public async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync()
        {
            var dataService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<IDataService>(_serviceProvider);
            if (dataService == null) return (new(), new());
            return await dataService.GetOrderBookAsync(Identity.Provider, Identity.Symbol).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _liveCts?.Cancel(); _liveCts?.Dispose();
            _prependLock.Dispose();
        }
    }
}

