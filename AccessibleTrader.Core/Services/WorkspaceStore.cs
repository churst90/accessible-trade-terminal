using System.Reactive.Linq;
using System.Reactive.Subjects;
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
        private readonly DeferredEventBus _deferredBus;
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
            _deferredBus = new DeferredEventBus(eventBus);
            _rangeCalculator = rangeCalculator;
            _navService = navService;
            _volumeService = volumeService;
            // Removed redundant Connect().Subscribe() calls that caused circular state updates.
            // Cache synchronization is now handled surgically within the Dispatch method.
        }

        /// <summary>
        /// The bus handed to reducers that publish from inside <c>Reduce</c>.
        ///
        /// <para><b>What went wrong.</b> <c>WorkspaceStore.Dispatch</c> carried the comment
        /// "EventBus.Publish is non-blocking so this is safe". It is not:
        /// <c>EventBus.Publish</c> is <c>GetSubject&lt;T&gt;().OnNext(eventData)</c> over a
        /// plain <c>Subject&lt;T&gt;</c>, fully synchronous on the caller's thread. Four
        /// <c>SeriesReducer</c> paths — <c>RestoreAll</c>, <c>ToggleMute</c>,
        /// <c>ToggleHide</c>, <c>ToggleNarration</c> — publish from inside <c>Reduce</c>,
        /// which runs inside <c>lock (_lock)</c> and <b>before</b> <c>_currentState</c> is
        /// assigned. <c>lock</c> is re-entrant, so a subscriber that dispatched synchronously
        /// re-entered <c>Dispatch</c>, computed from the <i>pre-commit</i> state, committed,
        /// notified — and was then overwritten when the outer dispatch assigned its own
        /// candidate. <b>The nested update was silently lost.</b> The comment further up
        /// claiming notifications happen outside the lock specifically to prevent this was
        /// true of the tab announcements and false of the reducer-level publishes.</para>
        ///
        /// <para><b>What this does.</b> Captures anything a reducer publishes during
        /// <c>Reduce</c> and replays it after the commit, on the same thread, in order. The
        /// reducers keep their signatures and their announcements — a reducer is still the
        /// only place that knows <i>what</i> to say — they simply no longer decide
        /// <i>when</i>. Everything else on <see cref="IEventBus"/> passes straight through,
        /// because a reducer has no business subscribing to anything.</para>
        /// </summary>
        private sealed class DeferredEventBus : IEventBus
        {
            private readonly IEventBus _inner;
            private readonly List<Action> _pending = new();

            public DeferredEventBus(IEventBus inner) => _inner = inner;

            public void Publish<T>(T eventData) => _pending.Add(() => _inner.Publish(eventData));

            /// <summary>Replays and clears. Called after the commit, never inside it.</summary>
            public void Drain()
            {
                if (_pending.Count == 0) return;
                var toSend = _pending.ToArray();
                _pending.Clear();
                foreach (var send in toSend) send();
            }

            /// <summary>Discards without publishing — used when a reduce produced no state
            /// change, so the announcements describe something that did not happen.</summary>
            public void Discard() => _pending.Clear();

            public IDisposable Subscribe<T>(Action<T> handler) => _inner.Subscribe(handler);
            public IObservable<T> AsObservable<T>() => _inner.AsObservable<T>();
            public IDisposable SubscribeCoalesced<T>(Action<T> handler, TimeSpan quietWindow)
                => _inner.SubscribeCoalesced(handler, quietWindow);
            public IDisposable SubscribeSampled<T>(Action<T> handler, TimeSpan window)
                => _inner.SubscribeSampled(handler, window);
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
                else
                {
                    // Nothing changed, so anything a reducer queued describes something that
                    // did not happen. Saying it anyway is worse than saying nothing.
                    _deferredBus.Discard();
                }

                // NOTIFY AFTER THE COMMIT, STILL UNDER THE LOCK.
                //
                // This block used to sit outside the lock, on the grounds that a subscriber
                // dispatching synchronously would otherwise deadlock. It cannot: `lock` is
                // re-entrant on the same thread, and the commit above has already happened,
                // so a re-entrant dispatch reduces from the NEW state, commits, and publishes
                // — in order, and without being overwritten afterwards.
                //
                // What the old arrangement did cost was ORDERING. `_currentState = candidate`
                // was inside the lock while `_seriesSource.Edit`, `_dataSource.Edit` and
                // `_stateSubject.OnNext` were outside it, so two concurrent dispatchers (the
                // live-tick thread and the UI thread — WorkspaceStoreTests treats concurrent
                // dispatch as supported) could commit S1 then S2 and publish S2 then S1. The
                // BehaviorSubject's retained value then ended up STALE relative to `State`,
                // every late subscriber received it, and the `updater.Load(newState.ActiveSeries)`
                // fallback could resurrect a series the newer state had removed. Neither
                // concurrency test asserted anything about stream ordering — they read
                // `store.State`, which was the half that was already safe.
                //
                // Serialising commit and publication under one lock is what makes the stream
                // order and the commit order the same order.
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

                // Whatever the reducers queued during Reduce, replayed now that the state
                // they describe is the committed one. See DeferredEventBus.
                _deferredBus.Drain();

                // Publish live-bar events AFTER state is committed.
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
                // WheelZoomAction has a reducer case in ViewportReducer and is
                // dispatched by GlobalInputService.OnWheel, but was MISSING from
                // this routing list — every mouse-wheel zoom fell through to the
                // "unhandled" arm and silently did nothing (found 2026-07-23,
                // same class as the Shift+F2/F3 dead-action bug).
                or WheelZoomAction
                or WorkspacePanEvent or WorkspaceZoomEvent
                or JumpToLatestAction
                or AdjustGranularityAction
                => ViewportReducer.Reduce(state, action, _navService),

            // ── Series (management + focus + visibility/audio) ───────────────
            SelectSeriesAction or SetPrimarySeriesIdAction or SelectComponentAction
                or SelectBinAction or SetInteractionContextAction
                or ToggleMuteAction or ToggleHideAction or ToggleNarrationAction
                // Shift+H / Shift+M: the THIRD action to be implemented in
                // SeriesReducer, dispatched from CommandDispatcher, and left out of
                // this list (found 2026-08-24, after WheelZoomAction above and
                // ToggleEventSpeech/ToggleEarcons below). This one is the documented
                // escape hatch that un-hides / un-mutes everything at once, so its
                // absence made H and M a one-way door for a screen-reader user — and
                // because RestoreAll'''s own announcement lives inside the reducer, the
                // keypress was completely silent. ActionRoutingReachabilityTests now
                // enumerates every WorkspaceAction subtype so there cannot be a fourth.
                or RestoreAllComponentsAction
                // Shift+H / Shift+M: the THIRD action to be implemented in
                // SeriesReducer, dispatched from CommandDispatcher, and left out of
                // this list (found 2026-08-24, after WheelZoomAction above and
                // ToggleEventSpeech/ToggleEarcons below). This one is the documented
                // escape hatch that un-hides / un-mutes everything at once, so its
                // absence made H and M a one-way door for a screen-reader user — and
                // because RestoreAll's own announcement lives inside the reducer, the
                // keypress was completely silent. ActionRoutingReachabilityTests now
                // enumerates every WorkspaceAction subtype so there cannot be a fourth.
                or AddSeriesAction or RemoveSeriesAction or AddLevelAction or RemoveLevelAction
                or UpdateSeriesAction or UpdateSeriesDataAction
                or UpdateSeriesZoneBandsAction or UpdateSeriesParametersAction
                // The DEFERRED bus, not the real one. SeriesReducer publishes announcements
                // from inside Reduce (RestoreAll, ToggleMute, ToggleHide, ToggleNarration) and
                // Reduce runs under `lock (_lock)` — see DeferredEventBus for what that cost.
                => SeriesReducer.Reduce(state, action, _deferredBus),

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

            // ── Identity / mode / provider context ───────────────────────────
            //
            // Setting the identity CLEARS the data. It used to be a one-field projection, and
            // that is how a chart came to show the wrong market: `LoadChartAsync` dispatches
            // this, awaits the fetch, and dispatches Ready — but a fetch that comes back empty
            // returns early without dispatching anything (an open circuit, a 200 OK with no
            // bars for a delisted ticker, a symbol outside the plan). The title, the toolbar and
            // Identity then all said ETH/USD while Data was still BTC/USD's 200 bars, status
            // said Ready, and nothing was spoken. `PaperTradingProvider.OnState` prices
            // positions and fills resting orders from exactly that (Identity, last bar) pair.
            //
            // Data belongs to an identity, so it cannot outlive one. The three dispatch sites
            // are the chart load and the two workspace-restore sites, all of which either load
            // data next or have none yet; tab switch and resume restore their snapshots by a
            // different action and are unaffected.
            SetIdentityAction a        => state with
            {
                Identity = a.Identity,
                Data = new TimeSeriesBuffer<Ohlcv>(),
                CurrentDataIndex = 0,
                ViewportStartIndex = 0,
            },
            ChangeModeAction a         => state with { Mode = a.Mode },
            SetProviderContextAction a => state with { CurrentDataShape = a.DataShape, SymbolDisplayName = a.SymbolDisplayName },

            // ── Init / data status (bounded state machine) ───────────────────
            RequestInitializationStatusAction a => CanTransition(state.InitStatus, a.Status)
                ? state with { InitStatus = a.Status }
                : state,
            SetDataStatusAction a => state with { DataStatus = a.Status },

            // A tick is the ONLY thing that can clear Stale — a status that only ever goes one
            // way is a status nobody can trust the second time.
            LiveTickObservedAction t => state with
            {
                LastTickUtc = t.AtUtc,
                DataStatus = state.DataStatus == DataStatus.Stale ? DataStatus.Ready : state.DataStatus,
            },

            MarkFeedStaleAction => state with { DataStatus = DataStatus.Stale },
            SetReplayModeAction a => state with { IsReplaying = a.Active },

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
