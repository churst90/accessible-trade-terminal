using System;
using System.Linq;
using System.Threading;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Core.Services.Accessibility;
using System.Collections.Generic;

namespace AccessibleTrader.Core.Services.Input
{
    public interface ICommandDispatcher
    {
        /// <summary>
        /// Dispatches a resolved <see cref="SystemCommand"/> through the appropriate handler.
        /// Priority order:
        ///   1. Global UI and state commands (settings, speech toggle, etc.) — always handled.
        ///   2. Chart-focus gate — navigation/drawing commands are suppressed when the chart div
        ///      is not active (e.g., a modal has focus). A 50 ms debounce prevents the race where
        ///      a keydown event arrives just before the Blazor onblur fires.
        ///   3. Data validation gate — navigation and playback commands are blocked when no chart is loaded.
        ///   4. Navigation and viewport commands — routed to <see cref="INavigationEngine"/>.
        ///   5. Playback engine commands — start/stop/pause/speed.
        ///   6. Volume and drawing tool commands — published via <see cref="IEventBus"/>.
        /// </summary>
        void Dispatch(SystemCommand command);
        void SetChartActive(bool active);
    }

    public class CommandDispatcher : ICommandDispatcher, IDisposable
    {
        private readonly IEventBus _eventBus;
        private readonly INavigationEngine _navEngine;
        private readonly IWorkspaceStore _store;
        private readonly IBarDetailService _barDetailService;
        private readonly IndicatorCrossingEngine _crossingEngine;
        private readonly IDisposable _focusSub;
        private readonly IDisposable _blurSub;
        private readonly IDisposable _modalSub;

        // Chart focus gate: every chart-scoped command (navigation, viewport, playback,
        // drawing, indicator toggle, properties, detail summary, etc.) requires the chart
        // element to actually have keyboard focus. Starts FALSE so commands don't fire
        // before the user has put focus into the chart — the app launches with focus on
        // the WebView's banner heading, not the chart. ChartArea publishes ChartFocusEvent
        // on @onfocus and DeactivateEvent on @onblur, which flip the flag. Debounce on
        // deactivate prevents the race where a JS keydown callback arrives milliseconds
        // before the Blazor @onblur fires (e.g. focus moves to a toolbar button via Tab).
        // Global commands (F-keys, modal opens, accessibility toggles, volume controls,
        // tab management, workspace management) are NOT gated — see IsChartScopedCommand.
        private volatile bool _isChartActive = false;
        private Timer? _deactivateDebounce;
        private const int DEACTIVATE_DEBOUNCE_MS = 50;

        // Modal stack counter. Modals can stack (Strategy modal opens Help modal etc.) so we
        // count opens and decrements on closes; the gate stays closed as long as count > 0.
        // Without this, arrow keys pressed inside a modal leak through the global JS keyboard
        // bridge into chart navigation — the user reported this as "modals don't trap input."
        // Modals already publish ModalStateChangedEvent on open/close; we just subscribe here.
        private int _openModalCount;

        // Modal name stack. Parallel to _openModalCount but tracks the ModalName of each
        // open modal so SystemCommand.CloseModal can target only the topmost. Without this,
        // an Escape press in a stacked Help-inside-Strategy scenario would close both modals
        // at once, dumping the user out of Strategy unexpectedly.
        private readonly Stack<string?> _modalStack = new();
        private readonly object _modalStackLock = new();

        public CommandDispatcher(
            IEventBus eventBus,
            INavigationEngine navEngine,
            IWorkspaceStore store,
            IBarDetailService barDetailService,
            IndicatorCrossingEngine crossingEngine)
        {
            _eventBus         = eventBus;
            _navEngine        = navEngine;
            _store            = store;
            _barDetailService = barDetailService;
            _crossingEngine   = crossingEngine;

            _focusSub = _eventBus.AsObservable<ChartFocusEvent>()
                .Subscribe(_ => SetChartActive(true));
            _blurSub = _eventBus.AsObservable<DeactivateEvent>()
                .Subscribe(_ =>
                {
                    // Debounce: let any in-flight keydown event finish processing before gating.
                    _deactivateDebounce?.Dispose();
                    _deactivateDebounce = new Timer(
                        _ => _isChartActive = false,
                        null, DEACTIVATE_DEBOUNCE_MS, Timeout.Infinite);
                });

            // Modal input trap: when ANY modal is open, suppress every chart command so the
            // user's keystrokes belong to the modal rather than leaking into chart navigation.
            // Modals already publish ModalStateChangedEvent on open/close — we just count.
            //
            // Phase 5 keyboard scope: when the LAST modal closes, publish
            // RequestChartFocusEvent so focus returns to the chart automatically. Without
            // this, focus lands wherever Blazor / the browser default puts it (often the
            // body), which is bad UX for screen-reader users — they'd have to Tab to find
            // the chart again. The user's own modal-close speech ("X dialog closed") is
            // already in flight, so we don't add a separate announcement here.
            _modalSub = _eventBus.AsObservable<ModalStateChangedEvent>()
                .Subscribe(e =>
                {
                    if (e.IsOpen)
                    {
                        System.Threading.Interlocked.Increment(ref _openModalCount);
                        lock (_modalStackLock) { _modalStack.Push(e.ModalName); }
                    }
                    else
                    {
                        System.Threading.Interlocked.Decrement(ref _openModalCount);
                        lock (_modalStackLock)
                        {
                            // Pop the matching name. Most-recent-first removal handles the common
                            // case of LIFO close order; the linear scan covers the rare case
                            // where modals close out of order (e.g. a parent forcibly closes a
                            // child via API rather than user gesture).
                            if (_modalStack.Count > 0 && _modalStack.Peek() == e.ModalName)
                            {
                                _modalStack.Pop();
                            }
                            else
                            {
                                var remaining = new Stack<string?>();
                                bool removed = false;
                                while (_modalStack.Count > 0)
                                {
                                    var n = _modalStack.Pop();
                                    if (!removed && n == e.ModalName) { removed = true; continue; }
                                    remaining.Push(n);
                                }
                                while (remaining.Count > 0) _modalStack.Push(remaining.Pop());
                            }
                        }
                    }
                    if (_openModalCount < 0) _openModalCount = 0;

                    if (!e.IsOpen && _openModalCount == 0)
                        _eventBus.Publish(new RequestChartFocusEvent());
                });
        }

        /// <summary>
        /// True when at least one modal is currently open. Used by <see cref="Dispatch"/> to
        /// suppress every chart command so the modal owns the keyboard. The modal handles its
        /// own internal navigation (Tab between fields, Up/Down inside lists or trees, Escape
        /// to close) via standard Blazor / browser keydown semantics — none of which goes
        /// through this dispatcher.
        /// </summary>
        public bool IsAnyModalOpen => _openModalCount > 0;

        public void SetChartActive(bool active)
        {
            _deactivateDebounce?.Dispose();
            _deactivateDebounce = null;
            _isChartActive = active;
        }

        public void Dispose()
        {
            _focusSub.Dispose();
            _blurSub.Dispose();
            _modalSub.Dispose();
            _deactivateDebounce?.Dispose();
        }

        public void Dispatch(SystemCommand command)
        {
            if (command == SystemCommand.None) return;

            // Modal input trap: when any modal is open, every chart command is suppressed so
            // the user's keystrokes are owned by the modal. Tab / Shift+Tab / arrow keys inside
            // forms are handled by Blazor and the browser without going through this dispatcher.
            // The few global commands that should still work while a modal is open are
            // explicitly allowlisted below.
            //
            // Escape special case: the keyboard binding `Escape → CancelDrawing` is still in
            // place, but when a modal is open we re-route Escape to CloseModal (publishes
            // CloseTopModalEvent) so every modal closes by Escape via a single dispatcher path.
            // Closes the audit gap where each modal had to re-implement its own Escape
            // handler — and HelpModal's silently failed on 2026-04-27 e18.
            if (_openModalCount > 0)
            {
                if (command == SystemCommand.CancelDrawing)
                    command = SystemCommand.CloseModal;

                bool allowedWhileModalOpen =
                    command == SystemCommand.CloseModal         ||  // Escape — close topmost modal
                    command == SystemCommand.ToggleSpeech       ||  // F2 — global accessibility toggle
                    command == SystemCommand.ToggleSonification ||  // F3 — same
                    command == SystemCommand.ToggleEventSpeech  ||  // Shift+F2 — same family
                    command == SystemCommand.ToggleEarcons      ||  // Shift+F3 — same family
                    command == SystemCommand.ToggleBraille      ||  // F4 — same family
                    command == SystemCommand.OpenHelp;              // F1 — help is always reachable
                if (!allowedWhileModalOpen) return;
            }

            // Chart-focus gate: chart-scoped commands (navigation, viewport, playback,
            // drawing, indicator toggle, OpenProperties, detail summary, etc.) only fire
            // when the chart element actually has keyboard focus. Global commands (F-keys,
            // modal opens, accessibility toggles, volume controls, tab/workspace management)
            // bypass this gate so the user can drive the app from any focus location.
            if (!_isChartActive && IsChartScopedCommand(command)) return;

            // Sub-pane navigation — needs chart data; focus gate already handled above.
            if (command == SystemCommand.NavSubPaneNext || command == SystemCommand.NavSubPanePrev)
            {
                if (_store.State.Data == null || !_store.State.Data.Any())
                {
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error, "No chart loaded.", true));
                    return;
                }
                HandleSubPaneNavigation(command == SystemCommand.NavSubPaneNext ? 1 : -1);
                return;
            }

            // Intra-pane component navigation — cycles components within the focused component's pane.
            if (command == SystemCommand.NavComponentInPaneNext || command == SystemCommand.NavComponentInPanePrev)
            {
                if (_store.State.Data == null || !_store.State.Data.Any())
                {
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error, "No chart loaded.", true));
                    return;
                }
                HandleIntraPaneNavigation(command == SystemCommand.NavComponentInPaneNext ? 1 : -1);
                return;
            }

            // 1. GLOBAL UI & STATE (Always work)
            switch (command)
            {
                case SystemCommand.OpenSettings: _eventBus.Publish(new OpenSettingsEvent()); return;
                case SystemCommand.OpenObjectTree: _eventBus.Publish(new OpenObjectTreeEvent()); return;
                case SystemCommand.OpenTradingDashboard: _eventBus.Publish(new OpenTradingDashboardEvent()); return;
                case SystemCommand.OpenOrderBook: _eventBus.Publish(new OpenOrderBookEvent()); return;
                case SystemCommand.OpenHelp: _eventBus.Publish(new OpenHelpEvent()); return;
                case SystemCommand.OpenApiKeys: _eventBus.Publish(new OpenApiKeysEvent()); return;
                case SystemCommand.OpenAlerts: _eventBus.Publish(new OpenAlertsEvent()); return;
                case SystemCommand.OpenIndicators: _eventBus.Publish(new OpenAddIndicatorEvent()); return;
                case SystemCommand.OpenDrawingTools: _eventBus.Publish(new OpenDrawingToolsEvent()); return;
                case SystemCommand.OpenStrategies: _eventBus.Publish(new OpenStrategiesEvent()); return;
                case SystemCommand.OpenCustomScripts: _eventBus.Publish(new OpenCustomScriptsEvent()); return;
                case SystemCommand.OpenSoundDesigner: _eventBus.Publish(new OpenSoundDesignerEvent()); return;
                case SystemCommand.OpenAIAnalyst: _eventBus.Publish(new OpenAIAnalystEvent()); return;
                case SystemCommand.OpenJournal: _eventBus.Publish(new OpenJournalEvent()); return;
                case SystemCommand.OpenMyData: _eventBus.Publish(new OpenMyDataEvent()); return;
                case SystemCommand.OpenWatchlist: _eventBus.Publish(new OpenWatchlistEvent()); return;
                case SystemCommand.OpenLevelReport: _eventBus.Publish(new OpenLevelReportEvent()); return;

                // Application/Menu key + Shift+F10: open the right-click context menu on
                // the focused drawing — keyboard parity with mouse right-click. Sentinel
                // NaN coordinates tell DrawingContextMenu to self-position rather than
                // anchor at a cursor location. Lives in the GLOBAL section because the
                // command operates on existing series state, not bar data — placing it
                // below the data-validation gate would block it on empty workspaces.
                // Chart-focus is still required (categorised as ChartScoped above).
                case SystemCommand.OpenDrawingContextMenu:
                {
                    // A focused drawing gets the drawing menu; anything else gets the
                    // chart-level menu (the same one mouse right-click opens on empty
                    // chart space), carrying the current cursor bar for "Play from here".
                    var focusedId = _store.State.FocusedSeriesId;
                    var focused = string.IsNullOrEmpty(focusedId)
                        ? null
                        : _store.State.ActiveSeries.FirstOrDefault(s => s.Id == focusedId);
                    if (focused != null && focused.IsDrawing)
                    {
                        _eventBus.Publish(new OpenDrawingContextMenuEvent(focused.Id, double.NaN, double.NaN));
                        return;
                    }
                    _eventBus.Publish(new OpenChartContextMenuEvent(
                        double.NaN, double.NaN, _store.State.CurrentDataIndex));
                    return;
                }
                case SystemCommand.CloseModal:
                {
                    // Peek the topmost modal name from the stack and target only that one.
                    // Each modal subscribes to CloseTopModalEvent and self-closes when the
                    // ModalName matches its own — stacked modals close one-at-a-time.
                    string? top;
                    lock (_modalStackLock)
                    {
                        top = _modalStack.Count > 0 ? _modalStack.Peek() : null;
                    }
                    _eventBus.Publish(new CloseTopModalEvent(top));
                    return;
                }
                case SystemCommand.SaveWorkspace: _eventBus.Publish(new OpenSaveWorkspaceEvent()); return;
                case SystemCommand.LoadWorkspace: _eventBus.Publish(new OpenLoadWorkspaceEvent()); return;
                case SystemCommand.LoadChart: _eventBus.Publish(new LoadChartRequestedEvent()); return;
                case SystemCommand.OpenProperties: _eventBus.Publish(new OpenPropertiesEvent()); return;
                case SystemCommand.AddReferenceLevel:
                {
                    // Add a zero-line to the focused indicator series (not a freehand drawing).
                    // Drawings are for price-anchored objects; reference levels belong on the series itself.
                    var focusedId = _store.State.FocusedSeriesId ?? string.Empty;
                    var focused = _store.State.ActiveSeries.FirstOrDefault(s => s.Id == focusedId);
                    if (focused != null && !focused.IsDrawing)
                    {
                        var level = new LevelConfig
                        {
                            Name = "Zero",
                            Value = 0,
                            ColorHex = "#888888",
                            DashStyle = DashStyle.Dash,
                            IsVisible = true
                        };
                        _store.Dispatch(new AddLevelAction(focusedId, level));
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info, "Zero line added", true));
                    }
                    return;
                }
                case SystemCommand.ToggleSpeech: _store.Dispatch(new ToggleSpeechAction()); return;
                case SystemCommand.ToggleSonification: _store.Dispatch(new ToggleSonificationAction()); return;
                case SystemCommand.ToggleEventSpeech: _store.Dispatch(new ToggleEventSpeechAction()); return;
                case SystemCommand.ToggleEarcons: _store.Dispatch(new ToggleEarconsAction()); return;
                case SystemCommand.ToggleBraille: _eventBus.Publish(new BrailleToggleRequestedEvent()); return;
                // Interim: braille device settings live in the Settings dialog; a
                // dedicated picker modal is TODO (needs multi-device enumeration).
                case SystemCommand.OpenBrailleSettings: _eventBus.Publish(new OpenSettingsEvent()); return;
                case SystemCommand.ToggleNarration: // Ctrl+Alt+Shift+N — global, no focus gate
                {
                    var seriesId = _store.State.FocusedSeriesId;
                    if (string.IsNullOrEmpty(seriesId))
                    {
                        // Fall back to the first non-drawing series if nothing is focused
                        var first = _store.State.ActiveSeries.FirstOrDefault(s => !s.IsDrawing);
                        seriesId = first?.Id;
                    }
                    if (!string.IsNullOrEmpty(seriesId))
                        _store.Dispatch(new ToggleNarrationAction(seriesId));
                    else
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error, "No series to toggle narration.", true));
                    return;
                }

                // Multi-tab — always available regardless of chart focus
                case SystemCommand.AddTab:
                    _store.Dispatch(new AddTabAction());
                    return;
                case SystemCommand.CloseTab:
                    _store.Dispatch(new CloseTabAction(_store.State.ActiveTabIndex));
                    return;
                case SystemCommand.SwitchTabNext:
                {
                    int next = (_store.State.ActiveTabIndex + 1) % _store.State.TabCount;
                    _store.Dispatch(new SwitchTabAction(next));
                    return;
                }
                case SystemCommand.SwitchTabPrev:
                {
                    int prev = (_store.State.ActiveTabIndex - 1 + _store.State.TabCount) % _store.State.TabCount;
                    _store.Dispatch(new SwitchTabAction(prev));
                    return;
                }
                case SystemCommand.FocusTabBar:
                    // Ctrl+Tab/Ctrl+Number are browser-reserved on the web; this asks the
                    // TabBar to move keyboard focus onto the switcher bar so the user can
                    // drive it with arrows/Home/End/number row, Delete (close), and Insert
                    // (new tab). The bar always renders (even with one tab), so this is
                    // always actionable — e.g. press Insert to open a second tab.
                    _eventBus.Publish(new FocusTabBarEvent());
                    return;
                case SystemCommand.ContextSummary: _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info, "CONTEXT_SUMMARY", true)); return;
                case SystemCommand.MonitoringStatus: _eventBus.Publish(new AnnounceMonitoringStatusEvent()); return;
                case SystemCommand.ChartFocus:
                    // Ask ChartArea to programmatically focus the chart element. The
                    // resulting native focus event will fire ChartFocusEvent as a side
                    // effect, which flips _isChartActive. Don't publish ChartFocusEvent
                    // here — that would mark the gate active even if the focus call
                    // failed (e.g. JS bridge not yet initialised).
                    _eventBus.Publish(new RequestChartFocusEvent());
                    _eventBus.Publish(new FeedbackRequestEvent(
                        FeedbackType.Info,
                        "Focus on trading chart area.",
                        Interrupt: true));
                    return;
                case SystemCommand.ScrollPanesUp:
                    _store.Dispatch(new ScrollIndicatorPanesAction(-1));
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "Scroll panes up", true));
                    return;
                case SystemCommand.ScrollPanesDown:
                    _store.Dispatch(new ScrollIndicatorPanesAction(1));
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "Scroll panes down", true));
                    return;
                // Series focus cycling — works regardless of chart data so the user can
                // always navigate to a series and then use H/M/volume keys on it.
                case SystemCommand.SelectNextSeries:
                {
                    var series = _store.State.ActiveSeries;
                    if (series.Count > 0)
                    {
                        int cur = series.IndexOf(series.FirstOrDefault(s => s.Id == _store.State.FocusedSeriesId)!);
                        int next = (cur + 1) % series.Count;
                        _store.Dispatch(new SelectSeriesAction(series[next].Id));
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, series[next].FriendlyName, true));
                    }
                    return;
                }
                case SystemCommand.SelectPrevSeries:
                {
                    var series = _store.State.ActiveSeries;
                    if (series.Count > 0)
                    {
                        int cur = series.IndexOf(series.FirstOrDefault(s => s.Id == _store.State.FocusedSeriesId)!);
                        int prev = (cur - 1 + series.Count) % series.Count;
                        _store.Dispatch(new SelectSeriesAction(series[prev].Id));
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, series[prev].FriendlyName, true));
                    }
                    return;
                }
                // Volume controls always work regardless of whether chart data is loaded.
                case SystemCommand.VolCompUp:   _eventBus.Publish(new VolumeChangeEvent("COMPONENT",  0.1f)); return;
                case SystemCommand.VolCompDown: _eventBus.Publish(new VolumeChangeEvent("COMPONENT", -0.1f)); return;
                case SystemCommand.VolSeriesUp:   _eventBus.Publish(new VolumeChangeEvent("SERIES",  0.1f)); return;
                case SystemCommand.VolSeriesDown: _eventBus.Publish(new VolumeChangeEvent("SERIES", -0.1f)); return;
                case SystemCommand.VolChartUp:   _eventBus.Publish(new VolumeChangeEvent("CHART",  0.1f)); return;
                case SystemCommand.VolChartDown: _eventBus.Publish(new VolumeChangeEvent("CHART", -0.1f)); return;
            }

            // 2. DATA VALIDATION (Chart commands require a loaded chart).
            //    Chart-focus gate already ran at the top — anything that reaches this
            //    point either has chart focus or is a global command.
            if (_store.State.Data == null || !_store.State.Data.Any())
            {
                // If the user tries to navigate or interact with an empty chart, announce it.
                if (IsNavigationCommand(command) || IsPlaybackCommand(command))
                {
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error, "No chart loaded.", true));
                }
                return;
            }

            // 3. NAVIGATION & VIEWPORT
            if (IsNavigationCommand(command))
            {
                // NavLeftJump and NavRightJump require custom crossing-detection logic.
                if (command == SystemCommand.NavLeftJump || command == SystemCommand.NavRightJump)
                {
                    _crossingEngine.HandleCrossJump(command);
                    return;
                }

                string navTarget = MapCommandToNavString(command);
                if (!string.IsNullOrEmpty(navTarget))
                {
                    _navEngine.ProcessNavigation(navTarget);
                }
                return;
            }

            // 4. PLAYBACK ENGINE
            if (IsPlaybackCommand(command))
            {
                HandlePlayback(command);
                return;
            }

            // 5. SETTINGS & VISUAL TOGGLES
            switch (command)
            {
                case SystemCommand.ToggleHeikinAshi: _store.Dispatch(new ToggleHeikinAshiAction()); break;
                case SystemCommand.ToggleLogScale: _store.Dispatch(new ToggleLogScaleAction()); break;
                case SystemCommand.ToggleHeatmap:
                    _eventBus.Publish(new ToggleToolEvent(ToolType.Heatmap));
                    break;
                
                // Route through EventBus so ChartCommandManager handles speech + redraw.
                // We resolve the scope based on the last interaction context (Navigation)
                case SystemCommand.ToggleIndicatorVisibility: // H
                    _eventBus.Publish(new ToggleHideEvent(_store.State.LastInteractionContext == InteractionContext.Component ? "COMPONENT" : "SERIES")); 
                    break;
                case SystemCommand.ToggleIndicatorAudio: // M
                    _eventBus.Publish(new ToggleMuteEvent(_store.State.LastInteractionContext == InteractionContext.Component ? "COMPONENT" : "SERIES"));
                    break;
                // Delete key: remove focused indicator series (ChartCommandManager guards against "candles").
                case SystemCommand.RemoveSelectedSeries: _eventBus.Publish(new DeleteSeriesEvent()); break;

                // Escape: cancel any in-progress drawing placement.
                case SystemCommand.CancelDrawing:
                    _eventBus.Publish(new CancelDrawingEvent());
                    break;

                // Ctrl+Shift+D: detailed candle pattern analysis at the current cursor position.
                case SystemCommand.DetailedPointSummary:
                {
                    var state = _store.State;
                    if (state.Data == null || !state.Data.Any()) break;
                    _barDetailService.AnnounceDetails(state);
                    break;
                }

                // Drawing shortcuts: press once to set anchor 1, press again for anchor 2
                // (and a third time for three-point tools: FibExtension, RiskReward, Pitchfork).
                // DrawingInteractionManager owns the per-tool anchor state machine.
                case SystemCommand.DrawTrend:
                case SystemCommand.DrawHorizontal:
                case SystemCommand.DrawVertical:
                case SystemCommand.DrawChannel:
                case SystemCommand.DrawFibonacci:
                case SystemCommand.DrawLabel:
                case SystemCommand.DrawFibExtension:
                case SystemCommand.DrawRectangle:
                case SystemCommand.DrawGannFan:
                case SystemCommand.DrawRiskReward:
                case SystemCommand.DrawAnchoredVwap:
                case SystemCommand.DrawMeasure:
                case SystemCommand.DrawGannBox:
                case SystemCommand.DrawPitchfork:
                case SystemCommand.DrawAngleFib:
                {
                    string typeName = MapDrawingTypeToString(MapDrawCommandToType(command));
                    if (!string.IsNullOrEmpty(typeName))
                        _eventBus.Publish(new AddDrawingEvent(typeName));
                    break;
                }
            }
        }

        private void HandleSubPaneNavigation(int direction)
        {
            var state = _store.State;
            var seriesId = state.FocusedSeriesId ?? "candles";
            var series = state.ActiveSeries.FirstOrDefault(s => s.Id == seriesId);
            if (series == null) return;

            // Collect ordered list of distinct pane names (null = main pane, then sub-panes in first-appearance order).
            var paneOrder = new List<string?>();
            paneOrder.Add(null); // main pane always first
            foreach (var comp in series.Components)
            {
                if (!string.IsNullOrEmpty(comp.SubPaneName) && !paneOrder.Contains(comp.SubPaneName))
                    paneOrder.Add(comp.SubPaneName);
            }

            if (paneOrder.Count <= 1)
            {
                // Only main pane — no sub-panes exist.
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info, $"No sub-panes in {series.FriendlyName}.", true));
                return;
            }

            // Determine current pane from focused component.
            int focusedIdx = Math.Clamp(state.FocusedComponentIndex, 0, series.Components.Count - 1);
            string? currentPane = series.Components[focusedIdx].SubPaneName;
            int currentPaneIdx = paneOrder.IndexOf(currentPane);
            if (currentPaneIdx < 0) currentPaneIdx = 0;

            // Advance to next/prev pane (wrapping).
            int targetPaneIdx = (currentPaneIdx + direction + paneOrder.Count) % paneOrder.Count;
            string? targetPane = paneOrder[targetPaneIdx];

            // Find first component in the target pane.
            int newCompIdx = -1;
            for (int i = 0; i < series.Components.Count; i++)
            {
                bool match = targetPane == null
                    ? string.IsNullOrEmpty(series.Components[i].SubPaneName)
                    : series.Components[i].SubPaneName == targetPane;
                if (match) { newCompIdx = i; break; }
            }
            if (newCompIdx < 0) return;

            string paneLabel = GetPaneDisplayLabel(targetPane, series);

            _store.Dispatch(new SelectComponentAction(newCompIdx));
            _store.Dispatch(new SetInteractionContextAction(InteractionContext.Component));
            // Publish IsYMove feedback so NavigationFeedbackManager speaks component name/type/value.
            // The prefix message carries the pane label so it is prepended to the component speech.
            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, paneLabel + ". ", true, IsYMove: true));
        }

        /// <summary>
        /// Cycles focus through components that share the same sub-pane (or main pane) as the
        /// currently focused component. Wraps at both ends. Fires IsYMove feedback so
        /// NavigationFeedbackManager announces the new component's name, type, and value.
        ///
        /// For indicators with no sub-panes (e.g. Cipher B — all components in the main pane),
        /// this cycles through all components, behaving identically to Up/Down but with wrapping.
        /// For indicators with sub-panes, it restricts movement to the current pane only.
        /// </summary>
        private void HandleIntraPaneNavigation(int direction)
        {
            var state = _store.State;
            var seriesId = state.FocusedSeriesId ?? "candles";
            var series = state.ActiveSeries.FirstOrDefault(s => s.Id == seriesId);
            if (series == null || series.Components.Count == 0) return;

            int focusedIdx = Math.Clamp(state.FocusedComponentIndex, 0, series.Components.Count - 1);
            string? currentPane = series.Components[focusedIdx].SubPaneName;

            // Collect indices of all components in the same pane, in order.
            var paneIndices = new List<int>();
            for (int i = 0; i < series.Components.Count; i++)
            {
                bool samePane = string.Equals(
                    series.Components[i].SubPaneName, currentPane,
                    StringComparison.OrdinalIgnoreCase);
                if (samePane) paneIndices.Add(i);
            }

            if (paneIndices.Count == 0) return;

            // Find position of focused component within pane-filtered list and advance with wrap.
            int posInPane = paneIndices.IndexOf(focusedIdx);
            if (posInPane < 0) posInPane = 0;
            int newPos = (posInPane + direction + paneIndices.Count) % paneIndices.Count;
            int newCompIdx = paneIndices[newPos];

            _store.Dispatch(new SelectComponentAction(newCompIdx));
            _store.Dispatch(new SetInteractionContextAction(InteractionContext.Component));
            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "", true, IsYMove: true));
        }

        /// <summary>
        /// Returns a human-readable display label for a sub-pane name.
        /// null/empty → "Main pane"; any other value → the SubPaneName appended with " pane".
        /// </summary>
        internal static string GetPaneDisplayLabel(string? subPaneName, ChartSeries series)
        {
            if (string.IsNullOrEmpty(subPaneName)) return "Main pane";
            return subPaneName + " pane";
        }

        /// <summary>
        /// Returns true for commands that should only fire while the chart element has
        /// keyboard focus. The complementary set — global commands — fire regardless of
        /// focus location so the user can drive the app from anywhere (open modals,
        /// toggle speech, change volume, switch tabs, save workspace, focus the chart
        /// itself via Ctrl+Alt+Shift+C, etc.).
        ///
        /// Per the Phase 5 keyboard-scope rule: F-keys are global EXCEPT Shift+F12 which
        /// binds to OpenProperties (chart-scoped because it operates on the focused
        /// series). Everything not bound to an F-key is chart-scoped.
        /// </summary>
        internal static bool IsChartScopedCommand(SystemCommand c)
        {
            // Navigation, viewport, playback, drawing — already covered by the existing
            // category helpers; reuse them directly.
            switch (c)
            {
                case SystemCommand.NavLeft:
                case SystemCommand.NavRight:
                case SystemCommand.NavUp:
                case SystemCommand.NavDown:
                case SystemCommand.NavHome:
                case SystemCommand.NavEnd:
                case SystemCommand.NavPageUp:
                case SystemCommand.NavPageDown:
                case SystemCommand.NavLeftJump:
                case SystemCommand.NavRightJump:
                case SystemCommand.JumpToLatest:
                case SystemCommand.NavSubPaneNext:
                case SystemCommand.NavSubPanePrev:
                case SystemCommand.NavComponentInPaneNext:
                case SystemCommand.NavComponentInPanePrev:
                case SystemCommand.SelectNextSeries:
                case SystemCommand.SelectPrevSeries:
                case SystemCommand.ZoomIn:
                case SystemCommand.ZoomOut:
                case SystemCommand.PanLeft:
                case SystemCommand.PanRight:
                case SystemCommand.GranularityUp:
                case SystemCommand.GranularityDown:
                case SystemCommand.ScrollPanesUp:
                case SystemCommand.ScrollPanesDown:
                case SystemCommand.PlayChart:
                case SystemCommand.PlaySeries:
                case SystemCommand.PlayComponent:
                case SystemCommand.PlayPause:
                case SystemCommand.PlayStop:
                case SystemCommand.PlaySpeedUp:
                case SystemCommand.PlaySpeedDown:
                case SystemCommand.ToggleHeikinAshi:
                case SystemCommand.ToggleLogScale:
                case SystemCommand.ToggleHeatmap:
                case SystemCommand.ToggleIndicatorVisibility:
                case SystemCommand.ToggleIndicatorAudio:
                case SystemCommand.ToggleNarration:
                case SystemCommand.AddReferenceLevel:
                case SystemCommand.OpenProperties:        // Shift+F12 — the F-key exception
                case SystemCommand.RemoveSelectedSeries:
                case SystemCommand.DetailedPointSummary:
                case SystemCommand.CancelDrawing:
                case SystemCommand.ConfirmCoordinateEntry:
                case SystemCommand.OpenDrawingContextMenu:
                case SystemCommand.DrawTrend:
                case SystemCommand.DrawHorizontal:
                case SystemCommand.DrawVertical:
                case SystemCommand.DrawChannel:
                case SystemCommand.DrawFibonacci:
                case SystemCommand.DrawLabel:
                case SystemCommand.DrawFibExtension:
                case SystemCommand.DrawRectangle:
                case SystemCommand.DrawGannFan:
                case SystemCommand.DrawRiskReward:
                case SystemCommand.DrawAnchoredVwap:
                case SystemCommand.DrawMeasure:
                case SystemCommand.DrawGannBox:
                case SystemCommand.DrawPitchfork:
                case SystemCommand.DrawAngleFib:
                    return true;

                // Everything else (modal opens, accessibility toggles, volume, tabs,
                // workspace, ContextSummary, ChartFocus, data-source changes, None)
                // is global.
                default:
                    return false;
            }
        }

        private bool IsNavigationCommand(SystemCommand c)
        {
            return c >= SystemCommand.NavLeft && c <= SystemCommand.GranularityDown;
        }

        private bool IsPlaybackCommand(SystemCommand c)
        {
            return c >= SystemCommand.PlayChart && c <= SystemCommand.PlaySpeedDown;
        }

        private bool IsDrawingCommand(SystemCommand c)
        {
            return c == SystemCommand.DrawTrend || c == SystemCommand.DrawHorizontal ||
                   c == SystemCommand.DrawVertical || c == SystemCommand.DrawChannel ||
                   c == SystemCommand.DrawFibonacci || c == SystemCommand.DrawLabel ||
                   c == SystemCommand.DrawFibExtension || c == SystemCommand.DrawRectangle ||
                   c == SystemCommand.DrawGannFan || c == SystemCommand.DrawRiskReward ||
                   c == SystemCommand.DrawAnchoredVwap || c == SystemCommand.DrawMeasure ||
                   c == SystemCommand.DrawGannBox || c == SystemCommand.DrawPitchfork ||
                   c == SystemCommand.DrawAngleFib || c == SystemCommand.CancelDrawing;
        }

        private static DrawingType MapDrawCommandToType(SystemCommand c) => c switch
        {
            SystemCommand.DrawTrend        => DrawingType.TrendLine,
            SystemCommand.DrawHorizontal   => DrawingType.HorizontalLine,
            SystemCommand.DrawVertical     => DrawingType.VerticalLine,
            SystemCommand.DrawChannel      => DrawingType.Channel,
            SystemCommand.DrawFibonacci    => DrawingType.FibRetracement,
            SystemCommand.DrawLabel        => DrawingType.TextLabel,
            SystemCommand.DrawFibExtension => DrawingType.FibExtension,
            SystemCommand.DrawRectangle    => DrawingType.Rectangle,
            SystemCommand.DrawGannFan      => DrawingType.GannFan,
            SystemCommand.DrawRiskReward   => DrawingType.RiskReward,
            SystemCommand.DrawAnchoredVwap => DrawingType.AnchoredVwap,
            SystemCommand.DrawMeasure      => DrawingType.MeasureTool,
            SystemCommand.DrawGannBox      => DrawingType.GannBox,
            SystemCommand.DrawPitchfork    => DrawingType.AndrewsPitchfork,
            SystemCommand.DrawAngleFib     => DrawingType.AngleFib,
            _                              => DrawingType.None
        };

        private static string MapDrawingTypeToString(DrawingType t) => t switch
        {
            DrawingType.TrendLine        => "TrendLine",
            DrawingType.HorizontalLine   => "Horizontal",
            DrawingType.VerticalLine     => "Vertical",
            DrawingType.Channel          => "Channel",
            DrawingType.FibRetracement   => "FibRetracement",
            DrawingType.TextLabel        => "TextLabel",
            DrawingType.FibExtension     => "FibExtension",
            DrawingType.Rectangle        => "Rectangle",
            DrawingType.GannFan          => "GannFan",
            DrawingType.RiskReward       => "RiskReward",
            DrawingType.AnchoredVwap     => "AnchoredVwap",
            DrawingType.MeasureTool      => "Measure",
            DrawingType.GannBox          => "GannBox",
            DrawingType.AndrewsPitchfork => "Pitchfork",
            DrawingType.AngleFib         => "AngleFib",
            _                            => string.Empty
        };

        private string MapCommandToNavString(SystemCommand c)
        {
            return c switch
            {
                SystemCommand.NavLeft => "NAV_LEFT",
                SystemCommand.NavRight => "NAV_RIGHT",
                SystemCommand.NavUp => "NAV_COMP_UP",
                SystemCommand.NavDown => "NAV_COMP_DOWN",
                SystemCommand.NavHome => "NAV_HOME",
                SystemCommand.NavEnd => "NAV_END",
                SystemCommand.NavPageUp => "NAV_SERIES_PREV",
                SystemCommand.NavPageDown => "NAV_SERIES_NEXT",
                SystemCommand.ZoomIn => "VIEW_ZOOM_IN",
                SystemCommand.ZoomOut => "VIEW_ZOOM_OUT",
                SystemCommand.PanLeft => "VIEW_PAN_LEFT",
                SystemCommand.PanRight => "VIEW_PAN_RIGHT",
                SystemCommand.JumpToLatest => "NAV_LIVE",
                SystemCommand.GranularityUp => "VIEW_GRAN_UP",
                SystemCommand.GranularityDown => "VIEW_GRAN_DOWN",
                _ => string.Empty
            };
        }

        private void HandlePlayback(SystemCommand command)
        {
            switch (command)
            {
                case SystemCommand.PlayChart:
                    TogglePlayback(PlaybackScope.Chart);
                    break;
                case SystemCommand.PlaySeries:
                    TogglePlayback(PlaybackScope.Series);
                    break;
                case SystemCommand.PlayComponent:
                    TogglePlayback(PlaybackScope.Component);
                    break;
                case SystemCommand.PlayPause:
                    _store.Dispatch(new TogglePauseAction());
                    break;
                case SystemCommand.PlayStop:
                    _store.Dispatch(new SetPlaybackAction(false, _store.State.PlaybackScope));
                    break;
                case SystemCommand.PlaySpeedUp:
                    _store.Dispatch(new AdjustPlaybackSpeedAction(0.1f));
                    break;
                case SystemCommand.PlaySpeedDown:
                    _store.Dispatch(new AdjustPlaybackSpeedAction(-0.1f));
                    break;
            }
        }

        private void TogglePlayback(PlaybackScope scope)
        {
            // Second press always stops, regardless of scope match or pause state.
            // Ctrl+Space is the explicit pause/resume key; plain Space/Shift+Space/Ctrl+Shift+Space
            // are start-or-stop toggles with no intermediate hanging state.
            if (_store.State.IsPlaying || _store.State.IsPaused)
                _store.Dispatch(new SetPlaybackAction(false, scope));
            else
                _store.Dispatch(new SetPlaybackAction(true, scope));
        }
    }
}