using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public interface IViewportManager
    {
        void HandleZoom(string direction, IReadOnlyList<Ohlcv> data);
        void HandlePan(int direction, IReadOnlyList<Ohlcv> data);
        void EnsureVisible(int index);
        void AnnounceViewport(IReadOnlyList<Ohlcv> data);
        string GetRichViewportDescription(IReadOnlyList<Ohlcv> data, int startIndex, int length);
    }

    public class ViewportManager : IViewportManager
    {
        /// <summary>
        /// When the viewport left edge is within this many bars of the data start,
        /// a proactive history backfill is requested. Must match the value used in
        /// <see cref="NavigationEngine"/> so both pan and cursor navigation trigger
        /// history loads at the same threshold.
        /// </summary>
        private const int HistoryBackfillThreshold = 50;

        private readonly IEventBus _eventBus;
        private readonly IWorkspaceStore _store;
        private readonly IMainThreadService _mainThread;

        public ViewportManager(IEventBus eventBus, IWorkspaceStore store, IMainThreadService mainThread)
        {
            _eventBus = eventBus;
            _store = store;
            _mainThread = mainThread;
        }

        public string GetRichViewportDescription(IReadOnlyList<Ohlcv> data, int startIndex, int length)
        {
            if (data == null || !data.Any()) return "No data loaded.";

            int actualLength = Math.Min(length, data.Count - startIndex);
            if (actualLength <= 0) return "Empty viewport.";

            var start = data[startIndex].Date;
            var end = data[startIndex + actualLength - 1].Date;

            return $"Viewing {actualLength} bars from {start.ToString("MMMM dd, yyyy", CultureInfo.InvariantCulture)} to {end.ToString("MMMM dd, yyyy", CultureInfo.InvariantCulture)}";
        }

        public void HandleZoom(string direction, IReadOnlyList<Ohlcv> data)
        {
            _mainThread.InvokeOnMainThread(() => {
                _store.Dispatch(new WorkspaceZoomEvent(direction));
                // Silence generic message to allow descriptive feedback from Coordinator
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.ViewportChange, ""));
            });
        }

        public void HandlePan(int direction, IReadOnlyList<Ohlcv> data)
        {
            _mainThread.InvokeOnMainThread(() => {
                var state = _store.State;
                
                // PROACTIVE BACKFILL: If panning left and near the data edge, trigger history fetch
                if (direction < 0 && state.ViewportStartIndex < HistoryBackfillThreshold)
                {
                    _eventBus.Publish(new RequestHistoryEvent());
                }

                _store.Dispatch(new WorkspacePanEvent(direction));
                // Silence generic message to allow descriptive feedback from Coordinator
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.ViewportChange, ""));
            });
        }

        public void EnsureVisible(int index)
        {
            _mainThread.InvokeOnMainThread(() => {
                var state = _store.State;
                if (index < state.ViewportStartIndex)
                {
                    _store.Dispatch(new PanAction(index - state.ViewportStartIndex));
                }
                else if (index >= state.ViewportStartIndex + state.ViewportLength)
                {
                    _store.Dispatch(new PanAction(index - (state.ViewportStartIndex + state.ViewportLength - 1)));
                }
            });
        }

        public void AnnounceViewport(IReadOnlyList<Ohlcv> data)
        {
            var state = _store.State;
            var msg = GetRichViewportDescription(data, state.ViewportStartIndex, state.ViewportLength);
            _eventBus.Publish(new AnnouncementEvent(msg));
        }
    }
}