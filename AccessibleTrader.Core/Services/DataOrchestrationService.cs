using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public interface IDataOrchestrationService
    {
        Task RecalculateAllAsync(IReadOnlyList<Ohlcv> data, CancellationToken token);
        Task RecalculateLastAsync(IReadOnlyList<Ohlcv> data, CancellationToken token, List<OrderBookEntry>? bids = null, List<OrderBookEntry>? asks = null);
    }

    /// <summary>
    /// Orchestrates the flow of data between DataManager, IndicatorOrchestrator, and the Store.
    /// PHASE 4: Integrates OrderBookHistoryService for honest heatmap reconstruction.
    /// </summary>
    public class DataOrchestrationService : IDataOrchestrationService, IDisposable
    {
        private readonly IWorkspaceStore _store;
        private readonly IDataManager _dataManager;
        private readonly IIndicatorOrchestrator _indicatorOrchestrator;
        private readonly IOrderBookHistoryService _bookHistory;
        private readonly IEventBus _eventBus;
        private readonly ILogger<DataOrchestrationService> _logger;
        private readonly CompositeDisposable _subscriptions = new();
        private readonly SemaphoreSlim _calcLock = new(1, 1);
        private CancellationTokenSource? _tickCts;
        private DateTime? _lastFirstBarDate;
        // Set to true when a recalculation trigger is suppressed during LoadingHistorical.
        // Cleared and a single full recalc fires when DataStatus transitions to Ready.
        private volatile bool _pendingRecalcAfterLoad;

        public DataOrchestrationService(
            IWorkspaceStore store,
            IDataManager dataManager,
            IIndicatorOrchestrator indicatorOrchestrator,
            IOrderBookHistoryService bookHistory,
            IEventBus eventBus,
            ILogger<DataOrchestrationService> logger)
        {
            _store = store;
            _dataManager = dataManager;
            _indicatorOrchestrator = indicatorOrchestrator;
            _bookHistory = bookHistory;
            _eventBus = eventBus;
            _logger = logger;

            _dataManager.DataUpdated += OnDataUpdated;

            // Viewport/series-change trigger — recalculate when viewport moves or series list changes.
            // When profiles (VPVR/TPO) are active, viewport changes require a full recalculation
            // because those indicators are computed over the currently-visible bar range.
            _subscriptions.Add(_store.StateStream
                .Select(s => new { s.ViewportStartIndex, s.ViewportLength, s.InitStatus, SeriesIds = s.ActiveSeries.Select(x => x.Id).ToList() })
                .DistinctUntilChanged(x => $"{x.ViewportStartIndex}-{x.ViewportLength}-{x.InitStatus}-{string.Join(",", x.SeriesIds)}")
                .Subscribe(x => {
                    if (x.InitStatus == InitializationStatus.Ready)
                    {
                        bool hasProfiles = _store.State.ActiveSeries.Any(s => s.IsProfile && !s.IsDrawing);
                        OnDataUpdated(forceFull: hasProfiles);
                    }
                }));

            // Direct trigger when a series is added/updated — forces full recalculation so the
            // new indicator is calculated immediately without waiting for a viewport change.
            _subscriptions.Add(_eventBus.AsObservable<IndicatorUpdatedEvent>()
                .Subscribe(_ => OnDataUpdated(forceFull: true)));

            // Deferred full recalculation: when loading/resampling completes (DataStatus → Ready),
            // fire the single recalculation that was suppressed during the loading phase.
            _subscriptions.Add(_store.StateStream
                .Select(s => s.DataStatus)
                .DistinctUntilChanged()
                .Where(status => status == DataStatus.Ready)
                .Subscribe(_ => {
                    if (_pendingRecalcAfterLoad)
                    {
                        _pendingRecalcAfterLoad = false;
                        OnDataUpdated(forceFull: true);
                    }
                }));

            // Reset first-bar tracking on every tab switch so the prepend-detection
            // heuristic doesn't fire a spurious full-recalc on the first data update
            // after switching back to a tab that has a different historical range.
            _subscriptions.Add(_eventBus.AsObservable<TabSwitchedEvent>()
                .Subscribe(_ => _lastFirstBarDate = null));
        }

        private void OnDataUpdated()
        {
            // Detect historical prepend: new data arrives with an earlier first-bar date.
            // Prepend invalidates all indicator buffers (they were indexed against the old start),
            // so we must force a full recalculation instead of an incremental last-bar update.
            var data = _dataManager.Data;
            bool isPrepend = _lastFirstBarDate.HasValue
                && data != null && data.Count > 0
                && data[0].Date < _lastFirstBarDate.Value;

            if (data != null && data.Count > 0)
                _lastFirstBarDate = data[0].Date;

            OnDataUpdated(forceFull: isPrepend);
        }

        private void OnDataUpdated(bool forceFull)
        {
            if (_store.State.InitStatus == InitializationStatus.Booting) return;

            // Suppress per-bar recalculations while the data pipeline is still loading or resampling.
            // This prevents dozens of racing full recalculations as bars trickle in (especially on
            // resampled timeframes like 1W where Bitstamp emits bars slowly over several seconds).
            // A single deferred recalculation fires when DataStatus transitions to Ready.
            if (_store.State.DataStatus == DataStatus.LoadingHistorical)
            {
                // Cancel any recalculation that is in-flight or queued from a previous prepend.
                // Without this, a recalc spawned by prepend N can race against prepend N+1
                // rewriting _cache, producing component arrays sized for the wrong dataset.
                // The _calcLock ensures the cancelled task finishes before the deferred recalc
                // (triggered on DataStatus → Ready) acquires the lock and runs cleanly.
                _tickCts?.Cancel();
                _pendingRecalcAfterLoad = true;
                return;
            }

            _tickCts?.Cancel();
            _tickCts = new CancellationTokenSource();
            var token = _tickCts.Token;

            _ = Task.Run(async () => {
                try
                {
                    var data = _dataManager.Data;
                    if (data == null || !data.Any()) return;

                    var activeSeries = _store.State.ActiveSeries;

                    // Fetch the live order book snapshot on every tick.
                    // This ensures the heatmap history service accumulates data regardless of
                    // whether the code path takes a full or incremental recalculation route.
                    var book = await _dataManager.GetOrderBookAsync();

                    // A full recalc is needed when: forced, or any non-profile indicator series
                    // has no component data yet (newly added indicator, fresh market load).
                    // Profile/heatmap series rely on the history service and RecalculateAllAsync,
                    // so they are excluded from the "empty data" trigger below.
                    bool needsFull = forceFull || activeSeries.Any(s =>
                        !string.IsNullOrEmpty(s.IndicatorCode) &&
                        !s.IsProfile &&
                        s.Data.ComponentData.Count == 0);

                    if (needsFull)
                    {
                        // Add snapshot to history before the full recalc so the history query
                        // inside RecalculateAllAsync includes the current live state.
                        if (book.Bids?.Count > 0 || book.Asks?.Count > 0)
                            _bookHistory.AddSnapshot(_dataManager.Identity.Symbol, book.Bids ?? new(), book.Asks ?? new());
                        await RecalculateAllAsync(data, token);
                    }
                    else
                    {
                        // RecalculateLastAsync handles its own AddSnapshot internally.
                        await RecalculateLastAsync(data, token, book.Bids, book.Asks);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DataOrchestrationService tick processing failed");
                }
            }, token);
        }

        public async Task RecalculateAllAsync(IReadOnlyList<Ohlcv> data, CancellationToken token)
        {
            await _calcLock.WaitAsync(token);
            try
            {
                var activeSeries = _store.State.ActiveSeries.ToList();
                if (!activeSeries.Any()) return;

                // PHASE 4: Retrieve historical order books for heatmap reconstruction
                var history = _bookHistory.GetHistory(_dataManager.Identity.Symbol, data);

                await _indicatorOrchestrator.RecalculateAllAsync(data, activeSeries, token, history);
                _eventBus.Publish(new RedrawEvent());
            }
            catch (OperationCanceledException) { }
            finally
            {
                _calcLock.Release();
            }
        }

        public async Task RecalculateLastAsync(IReadOnlyList<Ohlcv> data, CancellationToken token, List<OrderBookEntry>? bids = null, List<OrderBookEntry>? asks = null)
        {
            await _calcLock.WaitAsync(token);
            try
            {
                var activeSeries = _store.State.ActiveSeries.ToList();
                if (!activeSeries.Any()) return;

                // PHASE 4: Cache the live snapshot
                if (bids != null && asks != null)
                {
                    _bookHistory.AddSnapshot(_dataManager.Identity.Symbol, bids, asks);
                }

                await _indicatorOrchestrator.RecalculateLastAsync(data, activeSeries, token, bids, asks);
                _eventBus.Publish(new RedrawEvent());
            }
            catch (OperationCanceledException) { }
            finally
            {
                _calcLock.Release();
            }
        }

        public void Dispose()
        {
            _tickCts?.Cancel();
            _tickCts?.Dispose();
            _subscriptions.Dispose();
            _calcLock.Dispose();
            _dataManager.DataUpdated -= OnDataUpdated;
        }
    }
}
