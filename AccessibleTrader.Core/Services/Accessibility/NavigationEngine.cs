using System;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public interface INavigationEngine
    {
        void ProcessNavigation(string command);
    }

    /// <summary>
    /// Translates raw UI navigation commands into state changes.
    /// 
    /// SIGNAL FLOW:
    /// 1. InputService (Keyboard) detects a key press and publishes a string command (e.g., "NAV_LEFT").
    /// 2. NavigationEngine intercepts the command, calculates the new cursor/viewport position.
    /// 3. It dispatches a Redux-style Action (e.g., NavigateAction) to the IWorkspaceStore.
    /// 4. The Store updates, triggering the AccessibilityFeedbackCoordinator to speak/sonify the new state.
    /// </summary>
    public class NavigationEngine : INavigationEngine
    {
        private const int HistoryBackfillThreshold = 50;

        private readonly IEventBus _eventBus;
        private readonly IWorkspaceStore _store;
        private readonly IViewportManager _viewportManager;
        private readonly IMainThreadService _mainThread;
        private readonly ISeriesNavigationRegistry _navRegistry;

        public NavigationEngine(
            IEventBus eventBus, 
            IWorkspaceStore store, 
            IViewportManager viewportManager, 
            IMainThreadService mainThread,
            ISeriesNavigationRegistry navRegistry)
        {
            _eventBus = eventBus;
            _store = store;
            _viewportManager = viewportManager;
            _mainThread = mainThread;
            _navRegistry = navRegistry;
        }

        public void ProcessNavigation(string command)
        {
            _mainThread.InvokeOnMainThread(() => {
                var state = _store.State;
                if (state.Data == null || !state.Data.Any())
                {
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error, "No chart loaded.", true));
                    return;
                }

                var focusedSeries = state.ActiveSeries.FirstOrDefault(s => s.Id == (state.FocusedSeriesId ?? "candles"));
                var strategy = _navRegistry.GetStrategy(focusedSeries);

                switch (command)
                {
                    case "NAV_LEFT": NavigateX(strategy, -1); break;
                    case "NAV_RIGHT": NavigateX(strategy, 1); break;

                    case "NAV_HOME":
                        _store.Dispatch(new NavigateAction(state.ViewportStartIndex));
                        _store.Dispatch(new SetInteractionContextAction(InteractionContext.Component));
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "", true, IsXMove: true, IsJump: true));
                        break;
                    case "NAV_END":
                        _store.Dispatch(new NavigateAction(
                            Math.Min(state.ViewportStartIndex + state.ViewportLength - 1, state.Data.Count - 1)));
                        _store.Dispatch(new SetInteractionContextAction(InteractionContext.Component));
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "", true, IsXMove: true, IsJump: true));
                        break;

                    case "NAV_LIVE":
                        _store.Dispatch(new JumpToLatestAction());
                        _store.Dispatch(new SetInteractionContextAction(InteractionContext.Component));
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "", true, IsXMove: true, IsJump: true));
                        break;

                    case "NAV_COMP_UP": NavigateY(strategy, -1); break;
                    case "NAV_COMP_DOWN": NavigateY(strategy, 1); break;

                    case "NAV_SERIES_PREV": NavigateSeries(-1); break;
                    case "NAV_SERIES_NEXT": NavigateSeries(1); break;
                    
                    case "VIEW_PAN_LEFT": _viewportManager.HandlePan(-1, state.Data.ToList()); break;
                    case "VIEW_PAN_RIGHT": _viewportManager.HandlePan(1, state.Data.ToList()); break;
                    case "VIEW_ZOOM_IN": _viewportManager.HandleZoom("IN", state.Data.ToList()); break;
                    case "VIEW_ZOOM_OUT": _viewportManager.HandleZoom("OUT", state.Data.ToList()); break;

                    case "VIEW_GRAN_UP":   
                        _store.Dispatch(new AdjustGranularityAction(5));  
                        break;
                    case "VIEW_GRAN_DOWN": 
                        _store.Dispatch(new AdjustGranularityAction(-5)); 
                        break;

                    case "TOGGLE_MUTE": _eventBus.Publish(new ToggleMuteEvent()); break;
                    case "TOGGLE_HIDE": _eventBus.Publish(new ToggleHideEvent()); break;
                    case "REMOVE_FOCUSED_SERIES":
                        string? id = state.FocusedSeriesId;
                        if (!string.IsNullOrEmpty(id) && id != "candles")
                        {
                            var s = state.ActiveSeries.FirstOrDefault(x => x.Id == id);
                            if (s != null)
                            {
                                _store.Dispatch(new RemoveSeriesAction(id));
                                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange, $"Removed {s.Name}"));
                                _eventBus.Publish(new RedrawEvent());
                            }
                        }
                        else
                        {
                            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error, "Cannot remove primary candlestick series"));
                        }
                        break;
                }
            });
        }

        private void NavigateX(INavigationStrategy strategy, int delta)
        {
            var state = _store.State;
            if (delta < 0 && state.CurrentDataIndex < HistoryBackfillThreshold)
            {
                _eventBus.Publish(new RequestHistoryEvent());
            }

            var result = strategy.NavigateX(state, delta);
            if (result.Success)
            {
                if (result.NewIndex >= 0) _store.Dispatch(new NavigateAction(result.NewIndex));
                _store.Dispatch(new SetInteractionContextAction(result.Context));
                _eventBus.Publish(new FeedbackRequestEvent(result.FeedbackType, result.FeedbackMessage, true, IsXMove: true));
            }
            else
            {
                // At data boundary: play earcon only — no spoken phrase per user preference.
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Boundary, null, false));
            }
        }

        private void NavigateY(INavigationStrategy strategy, int delta)
        {
            var result = strategy.NavigateY(_store.State, delta);
            if (result.Success)
            {
                if (result.NewComponentIndex >= 0) _store.Dispatch(new SelectComponentAction(result.NewComponentIndex));
                if (result.NewBinIndex >= 0) _store.Dispatch(new SelectBinAction(result.NewBinIndex));

                _store.Dispatch(new SetInteractionContextAction(result.Context));
                _eventBus.Publish(new FeedbackRequestEvent(result.FeedbackType, result.FeedbackMessage, true, IsYMove: true));
            }
            else if (!string.IsNullOrEmpty(result.FeedbackMessage))
            {
                // Speak error/boundary messages even when navigation didn't change state.
                _eventBus.Publish(new FeedbackRequestEvent(result.FeedbackType, result.FeedbackMessage, true, IsYMove: true));
            }
        }

        private void NavigateSeries(int delta)
        {
            var state = _store.State;
            var all = state.ActiveSeries;
            if (!all.Any()) return;

            var focusedId = state.FocusedSeriesId ?? "candles";
            var currentSeries = all.FirstOrDefault(x => x.Id == focusedId);
            int currentIndex = currentSeries != null ? all.IndexOf(currentSeries) : 0;

            int newIndex = Math.Clamp(currentIndex + delta, 0, all.Count - 1);
            if (newIndex != currentIndex)
            {
                var series = all[newIndex];
                _store.Dispatch(new SelectSeriesAction(series.Id));
                // Reset to component 0 so the next UP/DOWN cycle starts from the first
                // component of the newly focused series, not a stale index from the previous one.
                _store.Dispatch(new SelectComponentAction(0));
                _store.Dispatch(new SetInteractionContextAction(InteractionContext.Series));

                // TRIGGER FEEDBACK — series name is spoken via NavigationFeedbackManager's series-switch announcement
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, null, true));
            }
        }
    }
}