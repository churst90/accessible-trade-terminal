using System;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public interface IHistoryBufferCoordinator
    {
    }

    public class HistoryBufferCoordinator : IHistoryBufferCoordinator, IDisposable
    {
        private readonly IEventBus _eventBus;
        private readonly IDataManager _dataManager;
        private readonly IWorkspaceStore _store;
        private readonly IGlobalErrorCoordinator _errorCoordinator;

        private bool _isFetching = false;
        private bool _historyExhausted = false;
        private readonly SemaphoreSlim _fetchLock = new SemaphoreSlim(1, 1);
        private readonly IDisposable _requestHistorySub;
        private readonly IDisposable _stateSub;
        private InitializationStatus _lastInitStatus = InitializationStatus.Booting;

        public HistoryBufferCoordinator(
            IEventBus eventBus,
            IDataManager dataManager,
            IWorkspaceStore store,
            IGlobalErrorCoordinator errorCoordinator)
        {
            _eventBus = eventBus;
            _dataManager = dataManager;
            _store = store;
            _errorCoordinator = errorCoordinator;

            _requestHistorySub = _eventBus.Subscribe<RequestHistoryEvent>(OnRequestHistory);

            // Reset exhaustion flag whenever a new initial data load occurs
            // (user loads a different symbol/timeframe). Without this, pressing
            // left after the first "no more history" on any chart permanently
            // disables backfill for the rest of the session.
            _stateSub = store.StateStream.Subscribe(state => {
                if (state.InitStatus == InitializationStatus.Ready &&
                    _lastInitStatus != InitializationStatus.Ready)
                {
                    _historyExhausted = false;
                }
                _lastInitStatus = state.InitStatus;
            });
        }

        private async void OnRequestHistory(RequestHistoryEvent e)
        {
            try
            {
                if (_isFetching || _historyExhausted) return;

                await _fetchLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_isFetching || _historyExhausted) return;
                    _isFetching = true;

                    int oldDataCount = _dataManager.Data.Count;
                    if (oldDataCount == 0) return;

                    await _dataManager.PrependOlderDataAsync().ConfigureAwait(false);

                    int newDataCount = _dataManager.Data.Count;
                    int addedCount = newDataCount - oldDataCount;

                    if (addedCount > 0)
                    {
                        _eventBus.Publish(new RedrawEvent());
                    }
                    else
                    {
                        _historyExhausted = true;
                        _store.Dispatch(new SetDataStatusAction(DataStatus.Ready));
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info, "No more history available.", true));
                    }
                }
                finally
                {
                    _isFetching = false;
                    _fetchLock.Release();
                }
            }
            catch (Exception ex)
            {
                _errorCoordinator.ReportError(
                    $"History load failed: {ex.Message}",
                    ErrorSeverity.Medium,
                    ErrorCategory.Provider);
            }
        }

        public void Dispose()
        {
            _requestHistorySub?.Dispose();
            _stateSub?.Dispose();
        }
    }
}
