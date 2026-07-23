using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Workspace.Reducers;
using DynamicData;

namespace AccessibleTrader.Core.Services
{
    public interface IWorkspaceStore
    {
        WorkspaceState State { get; }
        IObservable<WorkspaceState> StateStream { get; }
        void Dispatch(WorkspaceAction action);

        IObservable<IChangeSet<Ohlcv, DateTime>> DataStream { get; }
        IObservable<IChangeSet<ChartSeries, string>> SeriesStream { get; }
    }

    public class WorkspaceStore : IWorkspaceStore, IDisposable
    {
        private readonly BehaviorSubject<WorkspaceState> _stateSubject = new(WorkspaceState.Initial);
        private readonly object _lock = new();
        private readonly IEventBus _eventBus;
        private readonly IViewportRangeCalculator _rangeCalculator;
        private readonly IViewportNavigationService _navService;
        private readonly IVolumeStateService _volumeService;
        private volatile WorkspaceState _currentState = WorkspaceState.Initial;

        private readonly SourceCache<Ohlcv, DateTime> _dataSource = new(x => x.Date);
        private readonly SourceCache<ChartSeries, string> _seriesSource = new(x => x.Id);

        public WorkspaceState State => _currentState;
        public IObservable<WorkspaceState> StateStream => _stateSubject.AsObservable();

        public IObservable<IChangeSet<Ohlcv, DateTime>> DataStream => _dataSource.Connect();
        public IObservable<IChangeSet<ChartSeries, string>> SeriesStream => _seriesSource.Connect();

        public WorkspaceStore(
            IEventBus eventBus,
            IViewportRangeCalculator rangeCalculator,
            IViewportNavigationService navService,
            IVolumeStateService volumeService)
        {
            _eventBus = eventBus;
            _rangeCalculator = rangeCalculator;
            _navService = navService;
            _volumeService = volumeService;
            // Removed redundant Connect().Subscribe() calls that caused circular state updates.
            // Cache synchronization is now handled surgically within the Dispatch method.
        }

        public void Dispatch(WorkspaceAction action)
        {
            WorkspaceState? newState = null;
            bool seriesListChanged = false;
            bool dataChanged = false;

            // Capture pre-reducer data for live-bar event detection (outside lock is fine — snapshot only).
            int prevDataCount = _currentState.Data?.Count ?? 0;
            Ohlcv? prevLastBar  = (prevDataCount > 0) ? _currentState.Data![^1] : (Ohlcv?)null;
            Ohlcv? prevPrevBar  = (prevDataCount > 1) ? _currentState.Data![^2] : (Ohlcv?)null;
            bool isLiveDataAction = action is UpdateDataAction uda && !uda.IsInitialLoad;
            bool isTabSwitchAction = action is SwitchTabAction or AddTabAction;

            lock (_lock)
            {
                var candidate = Reduce(_currentState, action);

                // Auto-calculate ViewportRange if viewport parameters or data changed
                if (candidate.Data != _currentState.Data ||
                    candidate.ViewportStartIndex != _currentState.ViewportStartIndex ||
                    candidate.ViewportLength != _currentState.ViewportLength ||
                    candidate.ActiveSeries != _currentState.ActiveSeries)
                {
                    var result = _rangeCalculator.Calculate(candidate);
                    candidate = candidate with
                    {
                        ViewportRange = result.MainRange,
                        PaneRanges = result.PaneRanges
                    };
                }

                if (candidate != _currentState)
                {
                    seriesListChanged = candidate.ActiveSeries != _currentState.ActiveSeries;
                    dataChanged = candidate.Data != _currentState.Data;

                    _currentState = candidate;
                    newState = candidate;
                }
            }

            // NOTIFY OUTSIDE THE LOCK
            // This prevents deadlocks and infinite recursion where a subscriber
            // might try to Dispatch again synchronously.
            if (newState != null)
            {
                // Sync DynamicData caches surgically.
                // These triggers downstream structural change events (SeriesStream/DataStream).
                if (seriesListChanged)
                {
                    _seriesSource.Edit(updater =>
                    {
                        // SURGICAL UPDATE: Identify the specific change to prevent massive diffing on launch.
                        if (action is AddSeriesAction add)
                        {
                            updater.AddOrUpdate(add.Series);
                        }
                        else if (action is RemoveSeriesAction rem)
                        {
                            updater.RemoveKey(rem.SeriesId);
                        }
                        else
                        {
                            // Fallback for bulk updates (e.g. initial load or full recalc)
                            updater.Load(newState.ActiveSeries);
                        }
                    });
                }

                if (dataChanged)
                {
                    _dataSource.Edit(updater =>
                    {
                        // For data, we usually append or load full history.
                        if (action is UpdateDataAction up && !up.IsInitialLoad)
                        {
                            updater.AddOrUpdate(up.NewData);
                        }
                        else
                        {
                            updater.Load(newState.Data);
                        }
                    });
                }

                _stateSubject.OnNext(newState);

                // Publish live-bar events AFTER state is committed (outside the lock is ideal,
                // but we need newState here; EventBus.Publish is non-blocking so this is safe).
                if (isLiveDataAction && dataChanged)
                {
                    int newCount = newState.Data?.Count ?? 0;
                    if (newCount > prevDataCount && prevDataCount > 0 && newState.Data != null)
                    {
                        // A new bar opened — prevLastBar just closed.
                        _eventBus.Publish(new NewBarEvent(prevLastBar!.Value, newState.Data[^1]));
                    }
                    else if (newCount == prevDataCount && newCount > 0 && newState.Data != null)
                    {
                        // Intra-bar: same bar count, last bar replaced in place.
                        _eventBus.Publish(new IntraBarUpdateEvent(
                            newState.Data[^1],
                            prevLastBar,
                            prevPrevBar));
                    }
                }

                // Announce the newly active tab identity for switch and close operations.
                // Done outside the lock so speech subscribers can safely dispatch back.
                if (action is SwitchTabAction or CloseTabAction)
                {
                    string label = TabReducer.GetTabLabel(newState.Identity);
                    _eventBus.Publish(new AnnouncementEvent(
                        $"Tab {newState.ActiveTabIndex + 1}: {label}", true));
                }

                // Publish TabSwitchedEvent so audio engine, sonification, and data services
                // can stop playback and trigger a gap-fill catch-up for the restored tab.
                if (isTabSwitchAction)
                {
                    string label = TabReducer.GetTabLabel(newState.Identity);
                    _eventBus.Publish(new TabSwitchedEvent(newState.ActiveTabIndex, label));
                }
            }
        }

        /// <summary>
        /// Top-level dispatcher: routes each <see cref="WorkspaceAction"/> to the
        /// reducer that owns its domain. Single-line identity / mode / init /
        /// settings / volume actions are inlined because they delegate to a
        /// service or are one-shot field assignments — splitting them into
        /// their own file would add overhead without benefit.
        /// </summary>
        private WorkspaceState Reduce(WorkspaceState state, WorkspaceAction action) => action switch
        {
            // ── Viewport / navigation ────────────────────────────────────────
            UpdateDataAction
                or NavigateAction or NavigateRelativeAction
                or SetCursorAction
                or PanAction or ZoomAction
                or WorkspacePanEvent or WorkspaceZoomEvent
                or JumpToLatestAction
                or AdjustGranularityAction
                => ViewportReducer.Reduce(state, action, _navService),

            // ── Series (management + focus + visibility/audio) ───────────────
            SelectSeriesAction or SetPrimarySeriesIdAction or SelectComponentAction
                or SelectBinAction or SetInteractionContextAction
                or ToggleMuteAction or ToggleHideAction or ToggleNarrationAction
                or AddSeriesAction or RemoveSeriesAction or AddLevelAction
                or UpdateSeriesAction or UpdateSeriesDataAction
                or UpdateSeriesZoneBandsAction or UpdateSeriesParametersAction
                => SeriesReducer.Reduce(state, action, _eventBus),

            // ── Playback + accessibility + chart display ─────────────────────
            AdjustPlaybackSpeedAction
                or SetPlaybackAction or TogglePauseAction
                or ToggleSpeechAction or ToggleSonificationAction
                // Shift+F2 / Shift+F3: the reducer cases and their spoken
                // confirmations existed but the actions were MISSING from this
                // routing list — they fell through to "unhandled" and the
                // shortcuts silently did nothing (found live 2026-07-23).
                or ToggleEventSpeechAction or ToggleEarconsAction
                or ToggleHeikinAshiAction or ToggleLogScaleAction
                => PlaybackReducer.Reduce(state, action),

            // ── Tabs + pane layout ───────────────────────────────────────────
            AddTabAction or CloseTabAction or SwitchTabAction
                or ResizePaneAction or ScrollIndicatorPanesAction
                or SetPaneHeightRatiosAction
                => TabReducer.Reduce(state, action),

            // ── Drawing (coordinate entry) ───────────────────────────────────
            EnterCoordinateEntryAction or SetCoordinateEntryAnchorAction
                or ExitCoordinateEntryAction
                => DrawingReducer.Reduce(state, action),

            // ── Volume (fully owned by IVolumeStateService) ──────────────────
            AdjustChartVolumeAction a => _volumeService.ApplyChartVolume(state, a),
            AdjustVolumeAction a      => _volumeService.Apply(state, a),

            // ── Identity / mode / provider context (trivial projections) ─────
            SetIdentityAction a        => state with { Identity = a.Identity },
            ChangeModeAction a         => state with { Mode = a.Mode },
            SetProviderContextAction a => state with { CurrentDataShape = a.DataShape, SymbolDisplayName = a.SymbolDisplayName },

            // ── Init / data status (bounded state machine) ───────────────────
            RequestInitializationStatusAction a => CanTransition(state.InitStatus, a.Status)
                ? state with { InitStatus = a.Status }
                : state,
            SetDataStatusAction a => state with { DataStatus = a.Status },

            // ── User settings (caller-supplied projection) ───────────────────
            UpdateSettingsAction a => a.Updater(state),

            _ => state
        };

        /// <summary>
        /// Gate on the InitializationStatus state machine. Error and Resetting
        /// can be entered from any state; other transitions follow the
        /// Booting → Loading → Ready → (Re-)Loading arrow.
        /// </summary>
        private static bool CanTransition(InitializationStatus current, InitializationStatus target)
        {
            if (current == target) return true;
            if (target == InitializationStatus.Error) return true; // Error can happen anytime
            if (target == InitializationStatus.Resetting) return true; // Reset can happen anytime

            return current switch
            {
                InitializationStatus.Booting => target == InitializationStatus.Loading || target == InitializationStatus.Ready,
                InitializationStatus.Loading => target == InitializationStatus.Ready,
                InitializationStatus.Resetting => target == InitializationStatus.Loading,
                InitializationStatus.Ready => target == InitializationStatus.Loading, // Re-loading new symbol
                _ => false
            };
        }

        public void Dispose()
        {
            _stateSubject.Dispose();
            _dataSource.Dispose();
            _seriesSource.Dispose();
        }
    }
}
