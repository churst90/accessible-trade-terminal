using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Collections.Immutable;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Configuration;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Core.Models;
using DynamicData;
using System.Collections.Generic;
using System.Threading;

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
            Ohlcv? prevPrevPrevBar = (prevDataCount > 2) ? _currentState.Data![^3] : (Ohlcv?)null;
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
                    string label = GetTabLabel(newState.Identity);
                    _eventBus.Publish(new AnnouncementEvent(
                        $"Tab {newState.ActiveTabIndex + 1}: {label}", true));
                }

                // Publish TabSwitchedEvent so audio engine, sonification, and data services
                // can stop playback and trigger a gap-fill catch-up for the restored tab.
                if (isTabSwitchAction)
                {
                    string label = GetTabLabel(newState.Identity);
                    _eventBus.Publish(new TabSwitchedEvent(newState.ActiveTabIndex, label));
                }
            }
        }

        /// <summary>
        /// Pure reducer: takes current state + action → returns new state.
        /// Sections correspond to feature domains. Each domain's actions are
        /// handled in a dedicated block to keep related logic co-located.
        /// NavigationService, VolumeService, and ViewportRangeCalculator own
        /// the domain logic; the reducer just routes and delegates.
        /// </summary>
        private WorkspaceState Reduce(WorkspaceState state, WorkspaceAction action)
        {
            return action switch
            {
                // ── IDENTITY / MODE ─────────────────────────────────────────────
                SetIdentityAction a => state with { Identity = a.Identity },
                ChangeModeAction a => state with { Mode = a.Mode },

                // ── DATA ────────────────────────────────────────────────────────
                UpdateDataAction a => UpdateData(state, a.NewData, a.IsInitialLoad),

                // ── NAVIGATION (cursor + viewport) ──────────────────────────────
                NavigateAction a => _navService.Navigate(state, a.NewIndex),
                NavigateRelativeAction a => _navService.Navigate(state, state.CurrentDataIndex + a.Delta),

                PanAction a => _navService.Pan(state, a.Delta),
                ZoomAction a => _navService.ClampViewportToData(state with { ViewportLength = Math.Clamp(a.NewLength, state.RightMarginBars + 10, 5000) }),

                WorkspacePanEvent a => _navService.Pan(state,
                    Math.Max(1, (int)Math.Round(state.ViewportLength * state.PanningGranularity / 100.0)) * a.Direction),

                WorkspaceZoomEvent a => _navService.Zoom(state, a.Direction),

                JumpToLatestAction => state with
                {
                    CurrentDataIndex = state.Data.Count - 1,
                    ViewportStartIndex = Math.Max(0, state.Data.Count - (state.ViewportLength - state.RightMarginBars))
                },

                AdjustGranularityAction a => state with
                {
                    PanningGranularity = SnapToStep5(Math.Clamp(state.PanningGranularity + a.Delta, 5, 100))
                },

                // ── PLAYBACK ────────────────────────────────────────────────────
                AdjustPlaybackSpeedAction a => state with { PlaybackSpeed = Math.Clamp(state.PlaybackSpeed + a.Delta, 0.1f, 10.0f) },

                // ── ACCESSIBILITY / SETTINGS ─────────────────────────────────────
                ToggleSpeechAction => state with { IsSpeechEnabled = !state.IsSpeechEnabled },
                ToggleSonificationAction => state with { IsSonificationEnabled = !state.IsSonificationEnabled },

                // ── SERIES FOCUS ────────────────────────────────────────────────
                // Selecting a series sets focus to its "primary" component. For most
                // indicators that's component 0, but for series with a Body-role
                // component (Candles) we land on the body so wick-wick-body navigation
                // starts where the user's attention belongs. See GetDefaultComponentIndex.
                SelectSeriesAction a => state with { FocusedSeriesId = a.SeriesId, FocusedComponentIndex = GetDefaultComponentIndex(state, a.SeriesId) },
                SetPrimarySeriesIdAction a => state with { PrimarySeriesId = a.SeriesId },
                SetProviderContextAction a => state with { CurrentDataShape = a.DataShape, SymbolDisplayName = a.SymbolDisplayName },
                SelectComponentAction a => state with { FocusedComponentIndex = a.ComponentIndex },
                SelectBinAction a => state with { FocusedBinIndex = a.BinIndex },

                SetInteractionContextAction a => state with { LastInteractionContext = a.Context },

                // ── SERIES VISIBILITY / AUDIO ────────────────────────────────────
                ToggleMuteAction a => ToggleMute(state, a.SeriesId, GetEffectiveComponentName(state, a.SeriesId, a.ComponentName)),
                ToggleHideAction a => ToggleHide(state, a.SeriesId, GetEffectiveComponentName(state, a.SeriesId, a.ComponentName)),
                ToggleNarrationAction a => ToggleNarration(state, a.SeriesId),

                // ── PLAYBACK STATE ───────────────────────────────────────────────
                SetPlaybackAction a => state with { IsPlaying = a.IsPlaying, PlaybackScope = a.Scope, IsPaused = false },
                TogglePauseAction => state with { IsPaused = !state.IsPaused },

                // ── CHART DISPLAY ────────────────────────────────────────────────
                ToggleHeikinAshiAction => state with { IsHeikinAshi = !state.IsHeikinAshi },
                ToggleLogScaleAction => state with { IsLogScale = !state.IsLogScale },

                // ── SERIES MANAGEMENT ────────────────────────────────────────────
                AddSeriesAction a => AddSeries(state, a.Series),
                RemoveSeriesAction a => RemoveSeries(state, a.SeriesId),
                AddLevelAction a => AddLevel(state, a.SeriesId, a.Level),
                // Bulk: entire series list replaced (startup or full indicator delete)
                UpdateSeriesAction a => state with { ActiveSeries = a.Series },
                // Surgical: calculator finished — preserves Config so F5/F6 volume changes aren't clobbered
                UpdateSeriesDataAction a => state with {
                    ActiveSeries = state.ActiveSeries.Select(s =>
                        s.Id == a.SeriesId ? s.WithData(a.Data) : s).ToImmutableList()
                },
                // Surgical: replaces dynamic zone bands computed by a provider at runtime.
                // Mutates the existing ZoneBands list in place (like AddLevel) then rebuilds the reference.
                UpdateSeriesZoneBandsAction a => UpdateSeriesZoneBands(state, a.SeriesId, a.ZoneBands),
                // Surgical: persists auto-detected parameter overrides (e.g. Cipher S adaptive window).
                UpdateSeriesParametersAction a => UpdateSeriesParameters(state, a.SeriesId, a.Updates),

                // ── INITIALIZATION ───────────────────────────────────────────────
                RequestInitializationStatusAction a => CanTransition(state.InitStatus, a.Status)
                    ? state with { InitStatus = a.Status }
                    : state,
                SetDataStatusAction a => state with { DataStatus = a.Status },

                // ── PANE LAYOUT ──────────────────────────────────────────────────
                ResizePaneAction a => ResizePane(state, a.PaneName, a.Delta),
                ScrollIndicatorPanesAction a => state with
                {
                    IndicatorPaneScrollIndex = Math.Max(0, state.IndicatorPaneScrollIndex + a.Delta)
                },
                SetPaneHeightRatiosAction a => state with { PaneHeightRatios = a.Ratios },

                // ── COORDINATE ENTRY MODE ────────────────────────────────────────
                EnterCoordinateEntryAction a => state with
                {
                    IsCoordinateEntryMode = true,
                    PendingDrawingTool = a.Tool,
                    CoordinateEntryAnchorCount = 0,
                    CoordinateEntryAnchor1Index = -1
                },
                SetCoordinateEntryAnchorAction a when state.CoordinateEntryAnchorCount == 0 => state with
                {
                    CoordinateEntryAnchor1Index = a.DataIndex,
                    CoordinateEntryAnchorCount = 1
                },
                // Second anchor: the CommandDispatcher handles drawing completion as a side-effect;
                // the reducer just resets CE state so the store stays consistent.
                SetCoordinateEntryAnchorAction => state with
                {
                    IsCoordinateEntryMode = false,
                    PendingDrawingTool = null,
                    CoordinateEntryAnchorCount = 0,
                    CoordinateEntryAnchor1Index = -1
                },
                ExitCoordinateEntryAction => state with
                {
                    IsCoordinateEntryMode = false,
                    PendingDrawingTool = null,
                    CoordinateEntryAnchorCount = 0,
                    CoordinateEntryAnchor1Index = -1
                },

                // ── MULTI-TAB ────────────────────────────────────────────────────
                AddTabAction => AddTab(state),
                CloseTabAction a => CloseTab(state, a.TabIndex),
                SwitchTabAction a => SwitchTab(state, a.TargetIndex),

                // ── USER SETTINGS ────────────────────────────────────────────────
                UpdateSettingsAction a => a.Updater(state),

                // ── VOLUME ───────────────────────────────────────────────────────
                AdjustChartVolumeAction a => _volumeService.ApplyChartVolume(state, a),
                AdjustVolumeAction a => _volumeService.Apply(state, a),

                _ => state
            };
        }

        /// <summary>
        /// Resolves the default focused component index when a user or code path selects a
        /// series. For an indicator with a Body-role component (e.g. Candles), the body is
        /// the natural landing spot — the wicks flanking it are secondary. For everything
        /// else we land on index 0. Called by the SelectSeriesAction reducer case.
        /// </summary>
        private static int GetDefaultComponentIndex(WorkspaceState state, string seriesId)
        {
            var s = state.ActiveSeries.FirstOrDefault(x => x.Id == seriesId);
            if (s == null || s.Components.Count == 0) return 0;

            for (int i = 0; i < s.Components.Count; i++)
            {
                if (s.Components[i].Role == ComponentRole.Body)
                    return i;
            }
            return 0;
        }

        private static string? GetEffectiveComponentName(WorkspaceState state, string? actionSeriesId, string? actionCompName)
        {
            if (!string.IsNullOrEmpty(actionCompName)) return actionCompName;
            if (state.LastInteractionContext != InteractionContext.Component) return null;

            var targetId = actionSeriesId ?? state.FocusedSeriesId;
            var s = state.ActiveSeries.FirstOrDefault(x => x.Id == targetId);
            if (s == null) return null;

            int idx = Math.Clamp(state.FocusedComponentIndex, 0, s.Components.Count - 1);
            return s.Components[idx].Name;
        }

        private WorkspaceState UpdateData(WorkspaceState state, TimeSeriesBuffer<Ohlcv> list, bool initial)
        {
            int newIdx = state.CurrentDataIndex;
            int newStart = state.ViewportStartIndex;

            // Effective data slots available in the viewport (total slots minus right margin).
            int effectiveWindow = Math.Max(1, state.ViewportLength - state.RightMarginBars);

            if (initial || newIdx == -1)
            {
                newIdx   = list.Count - 1;
                newStart = Math.Max(0, list.Count - effectiveWindow);
            }
            else if (state.CurrentDataIndex >= state.Data.Count - 1)
            {
                // Cursor was at the live edge — keep it there after an append.
                newIdx   = list.Count - 1;
                newStart = Math.Max(0, list.Count - effectiveWindow);
            }
            else if (list.Count > state.Data.Count
                     && state.Data.Count > 0
                     && list[0].Date < state.Data[0].Date)
            {
                // PREPEND DETECTION: older bars were added to the front.
                // Offset cursor and viewport start so the user stays on the same bar.
                int prependedCount = list.Count - state.Data.Count;
                newIdx   = Math.Clamp(state.CurrentDataIndex   + prependedCount, 0, list.Count - 1);
                newStart = Math.Clamp(state.ViewportStartIndex + prependedCount, 0, Math.Max(0, list.Count - effectiveWindow));
            }

            var updated = state with
            {
                Data = list,
                CurrentDataIndex = newIdx,
                ViewportStartIndex = newStart,
                DataStatus = DataStatus.Ready
            };

            // SYNC VIRTUAL DATA: Price and Volume lines/bars are backed by the main 
            // OHLCV list. We must sync their component data so renderers find it.
            static double ExtractValue(Ohlcv bar, string mapping) => mapping.ToLower() switch
            {
                "open"   => (double)bar.Open,
                "high"   => (double)bar.High,
                "low"    => (double)bar.Low,
                "close"  => (double)bar.Close,
                "volume" => (double)bar.Volume,
                _        => double.NaN
            };

            var syncedSeries = updated.ActiveSeries.Select(s => {
                bool needsSync = s.Components.Any(c => !string.IsNullOrEmpty(c.DataMapping));
                if (!needsSync) return s;

                SeriesDataBuffer? updatedBuffer = null;

                foreach (var c in s.Components)
                {
                    if (!string.IsNullOrEmpty(c.DataMapping))
                    {
                        var currentData = s.GetComponentData(c.Name);
                        if (currentData.Length != list.Count)
                        {
                            if (updatedBuffer == null) updatedBuffer = s.Data.Clone();

                            var compValues = new double[list.Count];
                            bool isIncremental = currentData.Length == list.Count - 1 && !initial;

                            if (isIncremental)
                            {
                                Array.Copy(currentData, compValues, currentData.Length);
                                compValues[^1] = ExtractValue(list[^1], c.DataMapping);
                            }
                            else
                            {
                                for (int i = 0; i < list.Count; i++)
                                    compValues[i] = ExtractValue(list[i], c.DataMapping);
                            }

                            updatedBuffer.ComponentData[c.Name] = compValues;
                        }
                        else if (!initial && list.Count > 0)
                        {
                            // INTRA-BAR REPLACEMENT: count unchanged — the live bar was updated in place.
                            // Sync only the last element so the renderer and speech reflect the current tick.
                            if (updatedBuffer == null) updatedBuffer = s.Data.Clone();
                            var arr = (double[])currentData.Clone();
                            arr[^1] = ExtractValue(list[^1], c.DataMapping);
                            updatedBuffer.ComponentData[c.Name] = arr;
                        }
                    }
                }

                return updatedBuffer != null ? s.WithData(updatedBuffer) : s;
            }).ToImmutableList();

            if (syncedSeries != updated.ActiveSeries) {
                updated = updated with { ActiveSeries = syncedSeries };
            }

            // CRITICAL: Clamp viewport length after every data update.
            return _navService.ClampViewportToData(updated);
        }

        private static int SnapToStep5(int value)
        {
            int snapped = (int)Math.Round(value / 5.0) * 5;
            return Math.Max(5, snapped);
        }

        private WorkspaceState AddSeries(WorkspaceState state, ChartSeries series)
        {
            var newList = state.ActiveSeries.Add(series);
            return state with { ActiveSeries = newList, FocusedSeriesId = series.Id };
        }

        private static WorkspaceState AddLevel(WorkspaceState state, string seriesId, LevelConfig level)
        {
            var target = state.ActiveSeries.FirstOrDefault(s => s.Id == seriesId);
            if (target == null) return state;
            // Mutate the ObservableCollection in place (triggers UI bindings),
            // then rebuild the ImmutableList reference to trigger StateStream notification.
            target.Levels.Add(level);
            return state with { ActiveSeries = state.ActiveSeries.Select(s => s).ToImmutableList() };
        }

        private static WorkspaceState UpdateSeriesZoneBands(WorkspaceState state, string seriesId, IReadOnlyList<ZoneBandConfig> zoneBands)
        {
            var target = state.ActiveSeries.FirstOrDefault(s => s.Id == seriesId);
            if (target == null) return state;
            // Replace the zone bands list in place, then rebuild the ImmutableList reference to trigger StateStream.
            target.ZoneBands.Clear();
            foreach (var band in zoneBands)
                target.ZoneBands.Add(band.Clone());
            return state with { ActiveSeries = state.ActiveSeries.Select(s => s).ToImmutableList() };
        }

        private static WorkspaceState UpdateSeriesParameters(WorkspaceState state, string seriesId, Dictionary<string, double> updates)
        {
            var target = state.ActiveSeries.FirstOrDefault(s => s.Id == seriesId);
            if (target == null) return state;
            // Merge only the supplied keys — leave unaffected parameters unchanged.
            foreach (var kv in updates)
                target.Config.Parameters[kv.Key] = kv.Value;
            return state with { ActiveSeries = state.ActiveSeries.Select(s => s).ToImmutableList() };
        }

        private WorkspaceState RemoveSeries(WorkspaceState state, string id)
        {
            var newList = state.ActiveSeries.RemoveAll(s => s.Id == id);
            string? newFocus = state.FocusedSeriesId == id ? (newList.FirstOrDefault()?.Id ?? "candles") : state.FocusedSeriesId;
            return state with { 
                ActiveSeries = newList, 
                FocusedSeriesId = newFocus,
                LastInteractionContext = InteractionContext.Series,
                FocusedComponentIndex = 0
            };
        }

        private WorkspaceState ToggleMute(WorkspaceState state, string? seriesId, string? compName)
        {
            var targetId = seriesId ?? state.FocusedSeriesId;
            string? changedName = null;
            bool? newMuteState = null;

            var newList = state.ActiveSeries.Select(s =>
            {
                if (s.Id == targetId)
                {
                    var updated = s.Clone();
                    if (string.IsNullOrEmpty(compName))
                    {
                        updated.IsMuted = !updated.IsMuted;
                        changedName = updated.Name;
                        newMuteState = updated.IsMuted;
                    }
                    else
                    {
                        var comp = updated.Components.FirstOrDefault(c => c.Name == compName);
                        if (comp != null)
                        {
                            comp.IsMuted = !comp.IsMuted;
                            changedName = $"{s.Name}: {(string.IsNullOrEmpty(comp.DisplayName) ? comp.Name : comp.DisplayName)}";
                            newMuteState = comp.IsMuted;
                        }
                    }
                    return updated;
                }
                return s;
            }).ToImmutableList();

            if (changedName != null && newMuteState.HasValue)
            {
                _eventBus.Publish(new SeriesStateChangedEvent(changedName, true, newMuteState.Value));
            }

            return state with { ActiveSeries = newList };
        }

        private WorkspaceState ToggleHide(WorkspaceState state, string? id, string? compName)
        {
            var targetId = id ?? state.FocusedSeriesId;
            string? changedName = null;
            bool? newVisibility = null;

            var newList = state.ActiveSeries.Select(s =>
            {
                if (s.Id == targetId)
                {
                    var updated = s.Clone();
                    if (string.IsNullOrEmpty(compName))
                    {
                        updated.IsVisible = !updated.IsVisible;
                        changedName = updated.Name;
                        newVisibility = updated.IsVisible;
                    }
                    else
                    {
                        var comp = updated.Components.FirstOrDefault(c => c.Name == compName);
                        if (comp != null)
                        {
                            comp.IsVisible = !comp.IsVisible;
                            changedName = $"{s.Name}: {(string.IsNullOrEmpty(comp.DisplayName) ? comp.Name : comp.DisplayName)}";
                            newVisibility = comp.IsVisible;
                        }
                    }
                    return updated;
                }
                return s;
            }).ToImmutableList();

            if (changedName != null && newVisibility.HasValue)
            {
                _eventBus.Publish(new SeriesStateChangedEvent(changedName, newVisibility.Value, false));
            }

            return state with { ActiveSeries = newList };
        }

        // ── Tab helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Captures all per-tab fields from a WorkspaceState into a frozen TabSnapshot.
        /// Global settings (volume, speech, playback speed, etc.) are deliberately excluded.
        /// </summary>
        private static TabSnapshot SnapshotFromState(WorkspaceState s) => new TabSnapshot(
            TabIndex: s.ActiveTabIndex,
            Identity: s.Identity,
            Data: s.Data,
            ActiveSeries: s.ActiveSeries,
            FocusedSeriesIndex: s.FocusedSeriesIndex,
            FocusedSeriesId: s.FocusedSeriesId,
            FocusedComponentIndex: s.FocusedComponentIndex,
            FocusedBinIndex: s.FocusedBinIndex,
            CurrentDataIndex: s.CurrentDataIndex,
            ViewportStartIndex: s.ViewportStartIndex,
            ViewportLength: s.ViewportLength,
            RightMarginBars: s.RightMarginBars,
            ViewportRange: s.ViewportRange,
            PaneRanges: s.PaneRanges,
            IsHeikinAshi: s.IsHeikinAshi,
            IsLogScale: s.IsLogScale,
            LastInteractionContext: s.LastInteractionContext,
            PaneHeightRatios: s.PaneHeightRatios,
            IndicatorPaneScrollIndex: s.IndicatorPaneScrollIndex,
            InitStatus: s.InitStatus == InitializationStatus.Loading ? InitializationStatus.Ready : s.InitStatus,
            DataStatus: s.DataStatus == DataStatus.LoadingHistorical ? DataStatus.Ready : s.DataStatus,
            IsCoordinateEntryMode: false, // Always reset CE mode on tab switch
            PendingDrawingTool: null,
            CoordinateEntryAnchorCount: 0,
            CoordinateEntryAnchor1Index: -1,
            PrimarySeriesId: s.PrimarySeriesId,
            CurrentDataShape: s.CurrentDataShape,
            SymbolDisplayName: s.SymbolDisplayName
        );

        /// <summary>Restores all per-tab fields from a TabSnapshot into a WorkspaceState.</summary>
        private static WorkspaceState RestoreSnapshot(WorkspaceState state, TabSnapshot snap) => state with
        {
            Identity = snap.Identity,
            Data = snap.Data,
            ActiveSeries = snap.ActiveSeries,
            FocusedSeriesIndex = snap.FocusedSeriesIndex,
            FocusedSeriesId = snap.FocusedSeriesId,
            FocusedComponentIndex = snap.FocusedComponentIndex,
            FocusedBinIndex = snap.FocusedBinIndex,
            CurrentDataIndex = snap.CurrentDataIndex,
            ViewportStartIndex = snap.ViewportStartIndex,
            ViewportLength = snap.ViewportLength,
            RightMarginBars = snap.RightMarginBars,
            ViewportRange = snap.ViewportRange,
            PaneRanges = snap.PaneRanges,
            IsHeikinAshi = snap.IsHeikinAshi,
            IsLogScale = snap.IsLogScale,
            LastInteractionContext = snap.LastInteractionContext,
            PaneHeightRatios = snap.PaneHeightRatios,
            IndicatorPaneScrollIndex = snap.IndicatorPaneScrollIndex,
            InitStatus = snap.InitStatus,
            DataStatus = snap.DataStatus,
            IsCoordinateEntryMode = false,
            PendingDrawingTool = null,
            CoordinateEntryAnchorCount = 0,
            CoordinateEntryAnchor1Index = -1,
            PrimarySeriesId = snap.PrimarySeriesId,
            CurrentDataShape = snap.CurrentDataShape,
            SymbolDisplayName = snap.SymbolDisplayName
        };

        private static string GetTabLabel(ChartIdentity identity) =>
            string.IsNullOrEmpty(identity.Symbol)   ? "New Tab" :
            string.IsNullOrEmpty(identity.Provider) ? $"{identity.Symbol} {identity.Timeframe}" :
            $"{identity.Symbol} {identity.Timeframe} on {identity.Provider}";

        private WorkspaceState AddTab(WorkspaceState state)
        {
            // Save current tab as snapshot; new tab gets the next sequential index.
            var currentSnapshot = SnapshotFromState(state);
            int newIndex = state.TabCount; // TabCount = snapshots.Count + 1

            var newSnapshots = (state.TabSnapshots ?? ImmutableList<TabSnapshot>.Empty).Add(currentSnapshot);

            // New tab starts from a clean initial chart state with the new index.
            var newTabBase = WorkspaceState.Initial;
            return RestoreSnapshot(state, new TabSnapshot(
                TabIndex: newIndex,
                Identity: ChartIdentity.Empty,
                Data: newTabBase.Data,
                ActiveSeries: newTabBase.ActiveSeries,
                FocusedSeriesIndex: 0,
                FocusedSeriesId: null,
                FocusedComponentIndex: 0,
                FocusedBinIndex: -1,
                CurrentDataIndex: -1,
                ViewportStartIndex: 0,
                ViewportLength: state.ViewportLength,   // carry over viewport preference
                RightMarginBars: state.RightMarginBars, // carry over right margin
                ViewportRange: (0, 0),
                PaneRanges: ImmutableDictionary<string, (double, double)>.Empty,
                IsHeikinAshi: false,
                IsLogScale: false,
                LastInteractionContext: InteractionContext.Series,
                PaneHeightRatios: null,
                IndicatorPaneScrollIndex: 0,
                InitStatus: InitializationStatus.Booting,
                DataStatus: DataStatus.Idle,
                IsCoordinateEntryMode: false,
                PendingDrawingTool: null,
                CoordinateEntryAnchorCount: 0,
                CoordinateEntryAnchor1Index: -1,
                PrimarySeriesId: "candles",
                CurrentDataShape: Sdk.Plugins.ProviderDataShape.Ohlcv,
                SymbolDisplayName: ""
            )) with
            {
                TabSnapshots = newSnapshots,
                ActiveTabIndex = newIndex
            };
        }

        private WorkspaceState SwitchTab(WorkspaceState state, int targetIndex)
        {
            int tabCount = state.TabCount;
            if (targetIndex == state.ActiveTabIndex || targetIndex < 0 || targetIndex >= tabCount) return state;

            var snapshots = state.TabSnapshots ?? ImmutableList<TabSnapshot>.Empty;

            // Find target snapshot
            var targetSnapshot = snapshots.FirstOrDefault(t => t.TabIndex == targetIndex);
            if (targetSnapshot == null) return state; // Target doesn't exist

            // Save current state to snapshot
            var currentSnapshot = SnapshotFromState(state);

            // Update snapshot list: remove target (now active), add current (now inactive)
            var newSnapshots = snapshots
                .RemoveAll(t => t.TabIndex == targetIndex)
                .Add(currentSnapshot);

            return RestoreSnapshot(state, targetSnapshot) with
            {
                TabSnapshots = newSnapshots,
                ActiveTabIndex = targetIndex
            };
        }

        private WorkspaceState CloseTab(WorkspaceState state, int tabIndex)
        {
            int tabCount = state.TabCount;
            if (tabCount <= 1) return state; // Can't close the last tab

            var snapshots = state.TabSnapshots ?? ImmutableList<TabSnapshot>.Empty;

            if (tabIndex == state.ActiveTabIndex)
            {
                // Closing the active tab — switch to an adjacent one first
                int switchTo = tabIndex > 0 ? tabIndex - 1 : 1;
                var newSnapshots = snapshots.RemoveAll(t => t.TabIndex == switchTo || t.TabIndex == tabIndex);

                // Find the tab we're switching to
                var targetSnapshot = snapshots.FirstOrDefault(t => t.TabIndex == switchTo);
                if (targetSnapshot == null) return state;

                // Re-index remaining snapshots to fill the gap
                var reindexed = snapshots
                    .RemoveAll(t => t.TabIndex == switchTo || t.TabIndex == tabIndex)
                    .Select((t, i) => t with { TabIndex = i >= switchTo ? i : i })
                    .ToImmutableList();

                return RestoreSnapshot(state, targetSnapshot) with
                {
                    TabSnapshots = reindexed,
                    ActiveTabIndex = switchTo
                };
            }
            else
            {
                // Closing an inactive tab — just remove its snapshot
                var newSnapshots = snapshots.RemoveAll(t => t.TabIndex == tabIndex);
                return state with { TabSnapshots = newSnapshots };
            }
        }

        private WorkspaceState ToggleNarration(WorkspaceState state, string? seriesId)
        {
            var targetId = seriesId ?? state.FocusedSeriesId;
            string? changedName = null;
            bool? newState = null;

            var newList = state.ActiveSeries.Select(s =>
            {
                if (s.Id == targetId)
                {
                    var updated = s.Clone();
                    updated.IsAutoNarrated = !updated.IsAutoNarrated;
                    changedName = updated.FriendlyName;
                    newState = updated.IsAutoNarrated;
                    return updated;
                }
                return s;
            }).ToImmutableList();

            if (changedName != null && newState.HasValue)
            {
                string msg = newState.Value
                    ? $"{changedName}, narrating"
                    : $"{changedName}, narration off";
                _eventBus.Publish(new AnnouncementEvent(msg, true));
            }

            return state with { ActiveSeries = newList };
        }

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

        private static WorkspaceState ResizePane(WorkspaceState state, string paneName, float delta)
        {
            var ratios = state.PaneHeightRatios ?? ImmutableDictionary<string, float>.Empty;
            ratios.TryGetValue(paneName, out float current);
            // If not yet set, start from a reasonable default (0.15 per indicator pane)
            if (current == 0f) current = 0.15f;
            float newRatio = Math.Clamp(current + delta, 0.05f, 0.60f);
            return state with { PaneHeightRatios = ratios.SetItem(paneName, newRatio) };
        }

        public void Dispose()
        {
            _stateSubject.Dispose();
            _dataSource.Dispose();
            _seriesSource.Dispose();
        }
    }
}
