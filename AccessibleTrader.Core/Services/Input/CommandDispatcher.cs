using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Core.Services.Accessibility;

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
        private readonly Analysis.ChartPatternNavigator? _patternNavigator;
        private readonly Trading.IQuickTradeService? _quickTrade;
        private readonly IDisposable _focusSub;
        private readonly IDisposable _blurSub;

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

        // THE ordered modal stack — see ModalStack. It used to be a private counter plus a
        // private Stack<string?> here, while keyboard.js's Tab trap had its own, different idea
        // of "top" (DOM order). Now there is one stack, owned by DI, read here for Escape and
        // for the input trap, and pushed to the browser by MainLayout for the Tab trap.
        //
        // Without the trap, arrow keys pressed inside a modal leak through the global JS
        // keyboard bridge into chart navigation — the user reported this as "modals don't trap
        // input." Modals publish ModalStateChangedEvent on open/close; the stack subscribes.
        private readonly ModalStack _modalStack;
        private readonly bool _ownsModalStack;

        public CommandDispatcher(
            IEventBus eventBus,
            INavigationEngine navEngine,
            IWorkspaceStore store,
            IBarDetailService barDetailService,
            IndicatorCrossingEngine crossingEngine,
            Analysis.ChartPatternNavigator? patternNavigator = null,
            Trading.IQuickTradeService? quickTrade = null,
            ModalStack? modalStack = null)
        {
            _eventBus         = eventBus;
            _navEngine        = navEngine;
            _store            = store;
            _barDetailService = barDetailService;
            _crossingEngine   = crossingEngine;
            // Optional so the many five-argument constructions in the test suite keep working;
            // DI always supplies it. When absent the comma/period keys report that there is
            // nothing to navigate rather than throwing.
            _patternNavigator = patternNavigator;
            _quickTrade = quickTrade;

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
            //
            // DI supplies the one shared ModalStack (scoped per circuit on the web host, a
            // singleton in the MAUI head — the same lifetime as this dispatcher in each). The
            // five-argument constructions in the test suite get a private one fed by the same
            // bus, so their ModalStateChangedEvent publishes still drive Escape routing.
            _ownsModalStack = modalStack == null;
            _modalStack = modalStack ?? new ModalStack(eventBus);

            // Phase 5 keyboard scope: when the LAST modal closes, publish
            // RequestChartFocusEvent so focus returns to the chart automatically. Without
            // this, focus lands wherever Blazor / the browser default puts it (often the
            // body), which is bad UX for screen-reader users — they'd have to Tab to find
            // the chart again. The user's own modal-close speech ("X dialog closed") is
            // already in flight, so we don't add a separate announcement here.
            //
            // A close that leaves OTHER modals open is the browser's to handle: keyboard.js
            // puts focus back where it was in the dialog beneath (it recorded that element
            // when the closing modal opened). Listening to the stack's own Changed event, not
            // the bus, means the count read here is the count AFTER the stack applied the
            // event, whatever order the bus dispatches its subscribers in.
            _modalStack.Changed += OnModalStackChanged;
        }

        private void OnModalStackChanged(ModalStackChange change)
        {
            _spokenNudgeRefusal = null;   // a different dialog on top is a different refusal
            if (!change.IsOpen && change.Stack.Count == 0)
                _eventBus.Publish(new RequestChartFocusEvent());
        }

        /// <summary>
        /// True when at least one modal is currently open. Used by <see cref="Dispatch"/> to
        /// suppress every chart command so the modal owns the keyboard. The modal handles its
        /// own internal navigation (Tab between fields, Up/Down inside lists or trees, Escape
        /// to close) via standard Blazor / browser keydown semantics — none of which goes
        /// through this dispatcher.
        /// </summary>
        public bool IsAnyModalOpen => _modalStack.IsAnyOpen;

        public void SetChartActive(bool active)
        {
            _deactivateDebounce?.Dispose();
            _deactivateDebounce = null;
            if (_isChartActive != active) _spokenNudgeRefusal = null;
            _isChartActive = active;
        }

        public void Dispose()
        {
            _focusSub.Dispose();
            _blurSub.Dispose();
            _modalStack.Changed -= OnModalStackChanged;
            if (_ownsModalStack) _modalStack.Dispose();
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
            // The six anchor-nudge chords are allowed under the OBJECT TREE and nowhere else
            // modal. The tree is where a drawing is focused, so refusing the nudge there would
            // block the natural sequence — find it in the tree, then move it — and describe the
            // app's model inside-out. Under an EDITING dialog (Properties holds the very same
            // anchor coordinates in its fields) the nudge is refused, aloud, so the dialog never
            // shows a number the chart has already moved away from. The chart-focus gate is
            // skipped for the same command in the same situation: focus is in the tree by
            // construction, and that is the point.
            bool nudgeUnderObjectTree = IsAnchorNudgeCommand(command) && _modalStack.IsAnyOpen
                && NudgeAllowedUnder(_modalStack.Top);

            // Under the tree, the manager's own "no drawing focused" refusal would name Page Up
            // and Page Down — chart keys the tree does not honour, and a remedy naming the wrong
            // key is worse than no remedy. Since 2026-09-03 the tree is selection-follows-focus
            // (the APG default for a single-select tree): arrowing onto a series row focuses that
            // series on the chart, so the remedy is the arrow key.
            if (nudgeUnderObjectTree && !(FocusedSeries()?.IsDrawing ?? false))
            {
                RefuseNudge("tree:no-drawing", "Focus a drawing first. Arrow to its row in the tree.");
                return;
            }

            if (_modalStack.IsAnyOpen && !nudgeUnderObjectTree)
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
                if (!allowedWhileModalOpen)
                {
                    if (IsAnchorNudgeCommand(command))
                        RefuseNudge("modal:" + _modalStack.Top,
                            $"Not while {SpokenModalName(_modalStack.Top)} is open. Escape closes it.");
                    return;
                }
            }

            // Chart-focus gate: chart-scoped commands (navigation, viewport, playback,
            // drawing, indicator toggle, OpenProperties, detail summary, etc.) only fire
            // when the chart element actually has keyboard focus. Global commands (F-keys,
            // modal opens, accessibility toggles, volume controls, tab/workspace management)
            // bypass this gate so the user can drive the app from any focus location.
            //
            // Silent for every command but the nudge, and that silence is deliberate: an arrow
            // key with focus on a toolbar button belongs to the button. The nudge chords are
            // different — nothing else answers to Shift+Arrow on a button or the page body, so
            // dropping them without a sound made the whole feature read as "the commands don't
            // work" (reported from real use). keyboard.js releases a shifted arrow to any form
            // control before it gets here, so a text field never reaches this refusal.
            if (!_isChartActive && IsChartScopedCommand(command) && !nudgeUnderObjectTree)
            {
                if (IsAnchorNudgeCommand(command))
                    RefuseNudge("focus", "The chart does not have focus. Control Alt Shift C returns to the chart.");
                return;
            }

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
                case SystemCommand.OpenAssetDossier: _eventBus.Publish(new OpenAssetDossierEvent()); return;
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

                // ── Orientation and recovery ──────────────────────────────
                // "What am I looking at?" — the question a sighted user answers by glancing at
                // the screen, and the one every other spoken message assumes you already have.
                case SystemCommand.SpeakChartLayout:
                    _eventBus.Publish(new AnnouncementEvent(
                        ChartLayoutDescriber.Describe(_store.State, _store.State.SymbolDisplayName), true));
                    return;

                // The escape hatch for the single-key H and M toggles: hide a few components
                // across a few indicators and there is otherwise no practical way to find them
                // again, which makes those toggles a one-way door.
                case SystemCommand.ShowAllComponents:
                    _store.Dispatch(new RestoreAllComponentsAction(Unhide: true));
                    return;
                case SystemCommand.UnmuteAllComponents:
                    _store.Dispatch(new RestoreAllComponentsAction(Unhide: false));
                    return;
                case SystemCommand.ReplayToggle: _eventBus.Publish(new ReplayCommandEvent(ReplayCommand.Toggle)); return;
                case SystemCommand.ReplayStepForward: _eventBus.Publish(new ReplayCommandEvent(ReplayCommand.StepForward)); return;
                case SystemCommand.ReplayStepBack: _eventBus.Publish(new ReplayCommandEvent(ReplayCommand.StepBack)); return;
                case SystemCommand.ReplayPlayPause: _eventBus.Publish(new ReplayCommandEvent(ReplayCommand.PlayPause)); return;
                case SystemCommand.SplitViewToggle: _eventBus.Publish(new SplitViewCommandEvent(SplitViewCommand.Toggle)); return;
                case SystemCommand.SplitViewCycle: _eventBus.Publish(new SplitViewCommandEvent(SplitViewCommand.CycleSecondary)); return;
                case SystemCommand.SplitViewOrientation: _eventBus.Publish(new SplitViewCommandEvent(SplitViewCommand.ToggleOrientation)); return;

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
                    _eventBus.Publish(new CloseTopModalEvent(_modalStack.Top));
                    return;
                }
                case SystemCommand.SaveWorkspace: _eventBus.Publish(new OpenSaveWorkspaceEvent()); return;
                case SystemCommand.LoadWorkspace: _eventBus.Publish(new OpenLoadWorkspaceEvent()); return;
                case SystemCommand.LoadChart: _eventBus.Publish(new LoadChartRequestedEvent()); return;
                case SystemCommand.OpenProperties: _eventBus.Publish(new OpenPropertiesEvent()); return;
                case SystemCommand.AddReferenceLevel:
                {
                    // Add a reference level to the focused indicator series (not a freehand drawing).
                    // Drawings are for price-anchored objects; reference levels belong on the series itself.
                    //
                    // WHERE the level goes depends on the pane, and getting that wrong is not cosmetic:
                    // this used to add a level at literal zero regardless, and on the price series that
                    // dragged the whole y-axis down to the origin — permanently, because levels persist
                    // in the workspace. See ReferenceLevelPlacement for the full account.
                    var focusedId = _store.State.FocusedSeriesId ?? string.Empty;
                    var focused = _store.State.ActiveSeries.FirstOrDefault(s => s.Id == focusedId);
                    if (focused == null || focused.IsDrawing)
                    {
                        // Never silently: to a screen-reader user this is indistinguishable from an
                        // unbound key.
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error,
                            "Focus a series first — a reference level belongs to a series.", true));
                        return;
                    }

                    var bars = _store.State.Data;
                    int cursor = _store.State.CurrentDataIndex;
                    double cursorPrice = bars != null && cursor >= 0 && cursor < bars.Count
                        ? (double)bars[cursor].Close
                        : double.NaN;

                    // The key toggles. Pressing it where one of your own levels already sits removes
                    // that level — which is the only way to remove one from the keyboard, and until
                    // 2026-08-04 there was no way to remove one at all.
                    var doomed = ReferenceLevelPlacement.FindRemovable(focused.Levels, focused.Pane, cursorPrice);
                    if (doomed != null)
                    {
                        _store.Dispatch(new RemoveLevelAction(focusedId, doomed.Name));
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info,
                            $"{doomed.Name} removed.", true));
                        return;
                    }

                    var level = ReferenceLevelPlacement.For(
                        focused.Pane, cursorPrice, focused.Levels, out string levelReason);
                    if (level == null)
                    {
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error, levelReason, true));
                        return;
                    }

                    _store.Dispatch(new AddLevelAction(focusedId, level));
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info, levelReason, true));
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
                case SystemCommand.ContextSummary:
                    // Its own event, not FeedbackRequestEvent with a "CONTEXT_SUMMARY" token in
                    // the message field. The coordinator understood the token; the status bar,
                    // which mirrors every feedback message, did not — so Shift+F1 displayed and
                    // (while that strip was live) spoke the sentinel. See ContextSummaryRequestEvent.
                    _eventBus.Publish(new ContextSummaryRequestEvent());
                    return;
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
                else if (IsAnchorNudgeCommand(command))
                {
                    // Boundary, like every other refusal of a nudge: the key was understood
                    // and has nowhere to go. Error would play the failure earcon and speak on
                    // the channel F2 cannot mute, for a keypress that failed nothing.
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Boundary, "No chart loaded.", true));
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

                // ── Quick trade ──────────────────────────────────────────────
                //
                // Enter is the delicate one. Shift+Enter and Ctrl+Enter are ordinary chords a
                // user may well press for other reasons, so when nothing is armed they are
                // passed straight through as if unbound rather than answered. Announcing
                // "nothing armed" on every stray Ctrl+Enter would be its own kind of noise.
                if (command is SystemCommand.QuickArmRisk1 or SystemCommand.QuickArmRisk2
                            or SystemCommand.QuickArmRisk3 or SystemCommand.QuickSetStop
                            or SystemCommand.QuickPlaceLimit or SystemCommand.QuickPlaceMarket
                            or SystemCommand.QuickDisarm or SystemCommand.QuickArmStatus)
                {
                    if (_quickTrade == null) return;

                    bool isEnter = command is SystemCommand.QuickPlaceLimit or SystemCommand.QuickPlaceMarket;
                    if (isEnter && _quickTrade.State.Stage == Trading.QuickTradeStage.Idle) return;

                    switch (command)
                    {
                        case SystemCommand.QuickArmRisk1: _quickTrade.Arm(0.5); break;
                        case SystemCommand.QuickArmRisk2: _quickTrade.Arm(1.0); break;
                        case SystemCommand.QuickArmRisk3: _quickTrade.Arm(2.0); break;
                        case SystemCommand.QuickSetStop:  _quickTrade.SetStopAtCursor(); break;
                        case SystemCommand.QuickPlaceLimit:  _quickTrade.Place(market: false); break;
                        case SystemCommand.QuickPlaceMarket: _quickTrade.Place(market: true); break;
                        case SystemCommand.QuickDisarm:   _quickTrade.Disarm(); break;
                        case SystemCommand.QuickArmStatus: _quickTrade.Announce(); break;
                    }
                    return;
                }

                // Comma / period: step between chart-formation edges.
                // Semicolon: choose which overlapping formation leads the readout.
                if (command is SystemCommand.NavPatternPrev or SystemCommand.NavPatternNext
                            or SystemCommand.CyclePatternFocus or SystemCommand.ClearPatternFocus)
                {
                    if (_patternNavigator == null)
                    {
                        _eventBus.Publish(new FeedbackRequestEvent(
                            FeedbackType.Boundary, "Chart formation navigation is unavailable."));
                    }
                    else if (command == SystemCommand.CyclePatternFocus) _patternNavigator.CycleFocus();
                    else if (command == SystemCommand.ClearPatternFocus) _patternNavigator.ClearFocus();
                    else _patternNavigator.Jump(command);
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
                case SystemCommand.UndoChartEdit: _eventBus.Publish(new UndoChartEditEvent()); break;
                case SystemCommand.RedoChartEdit: _eventBus.Publish(new RedoChartEditEvent()); break;

                // Keyboard nudge for drawing anchors. Chart-scoped, so a modal or an unfocused
                // chart drops them here like every other chart edit; DrawingInteractionManager
                // owns the target, the step sizes, the readback and the undo coalescing.
                case SystemCommand.NudgeAnchorEarlier: _eventBus.Publish(new NudgeDrawingAnchorEvent(AnchorNudgeDirection.Earlier)); break;
                case SystemCommand.NudgeAnchorLater:   _eventBus.Publish(new NudgeDrawingAnchorEvent(AnchorNudgeDirection.Later)); break;
                case SystemCommand.NudgeAnchorUp:      _eventBus.Publish(new NudgeDrawingAnchorEvent(AnchorNudgeDirection.Up)); break;
                case SystemCommand.NudgeAnchorDown:    _eventBus.Publish(new NudgeDrawingAnchorEvent(AnchorNudgeDirection.Down)); break;
                case SystemCommand.CycleDrawingAnchor: _eventBus.Publish(new CycleDrawingAnchorEvent()); break;
                case SystemCommand.SnapAnchorToBar:    _eventBus.Publish(new SnapDrawingAnchorEvent()); break;

                // Escape: cancel whatever placement is in progress.
                //
                // An armed quick trade takes precedence over a half-placed drawing, because it is
                // the one that can cost money if it is forgotten. Escape is the key everyone
                // reaches for to mean "stop", so it has to reach the most consequential pending
                // thing first.
                case SystemCommand.CancelDrawing:
                    if (_quickTrade != null && _quickTrade.State.Stage != Trading.QuickTradeStage.Idle)
                    {
                        _quickTrade.Disarm();
                        break;
                    }
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
            int focusedIdx = series.ClampComponent(state.FocusedComponentIndex);
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

            _store.Dispatch(new SelectComponentAction(newCompIdx));
            _store.Dispatch(new SetInteractionContextAction(InteractionContext.Component));
            // Publish IsYMove feedback so NavigationFeedbackManager speaks component name/type/value.
            //
            // The prefix is EMPTY, and deliberately so. This used to carry a pane label built here
            // from the raw SubPaneName ("MF pane"), while NavigationFeedbackManager's own
            // pane-transition block independently detected the same move and prepended ITS label,
            // resolved from a component's friendlier DisplayName ("Money Flow pane"). Both fired,
            // so Ctrl+PageUp on a Cipher-style indicator said the pane twice under two different
            // names: "Money Flow pane. MF pane. Money Flow Wave. …". One announcement, one name,
            // and it belongs to the manager — which is the only one of the two that can tell a
            // transition from a move within a pane.
            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "", true, IsYMove: true));
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

            int focusedIdx = series.ClampComponent(state.FocusedComponentIndex);
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
                // Bare comma and period MUST be chart-scoped: they are ordinary printable
                // characters, and a global binding would swallow them inside every text box in
                // the application.
                case SystemCommand.NavPatternPrev:
                case SystemCommand.NavPatternNext:
                case SystemCommand.CyclePatternFocus:
                case SystemCommand.ClearPatternFocus:
                // Quick trade is chart-scoped: the stop and the limit both come from the bar
                // under the cursor, so these mean nothing without chart focus — and Shift+Enter
                // must stay available to every form control in the application.
                case SystemCommand.QuickArmRisk1:
                case SystemCommand.QuickArmRisk2:
                case SystemCommand.QuickArmRisk3:
                case SystemCommand.QuickSetStop:
                case SystemCommand.QuickPlaceLimit:
                case SystemCommand.QuickPlaceMarket:
                case SystemCommand.QuickDisarm:
                case SystemCommand.QuickArmStatus:
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
                // Undo/redo are chart-scoped for the same reason Delete is: they act on the
                // chart's own edits, and Ctrl+Z must stay available to every text box in the
                // application when the chart does not have focus.
                case SystemCommand.UndoChartEdit:
                case SystemCommand.RedoChartEdit:
                case SystemCommand.NudgeAnchorEarlier:
                case SystemCommand.NudgeAnchorLater:
                case SystemCommand.NudgeAnchorUp:
                case SystemCommand.NudgeAnchorDown:
                case SystemCommand.CycleDrawingAnchor:
                case SystemCommand.SnapAnchorToBar:
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

        /// <summary>The six keyboard-nudge chords. Listed so an empty chart answers them with
        /// "No chart loaded." like a navigation key, rather than the silence every other chart
        /// edit gets — to a speech user silence and an unbound key are the same thing.</summary>
        private static bool IsAnchorNudgeCommand(SystemCommand c) => c is
            SystemCommand.NudgeAnchorEarlier or SystemCommand.NudgeAnchorLater or
            SystemCommand.NudgeAnchorUp or SystemCommand.NudgeAnchorDown or
            SystemCommand.CycleDrawingAnchor or SystemCommand.SnapAnchorToBar;

        /// <summary>The <c>data-modal-name</c> of the one dialog the nudge works under.</summary>
        internal const string ObjectTreeModalName = "Object tree";

        /// <summary>
        /// Whether the nudge may run with this modal on top of the stack. A decision on the top
        /// modal's NAME, not a blanket flag — the first use of the ordered modal stack to draw a
        /// distinction rather than to close things in order. The tree is a focus-and-inspection
        /// dialog: nudging the chart under it changes nothing the tree shows as an editable value.
        /// </summary>
        internal static bool NudgeAllowedUnder(string? topModalName) =>
            string.Equals(topModalName, ObjectTreeModalName, StringComparison.OrdinalIgnoreCase);

        // Which refusal has already been SPOKEN for the current situation. A held chord
        // arrives at ~15 accepted presses a second; the boundary earcon answers every one of
        // them and the sentence is spoken once, then not again until the situation changes —
        // the chart gains or loses focus, or the modal stack changes. Without this the refusal
        // would be the narrator's eight-utterances-per-scan flood in a new place.
        private string? _spokenNudgeRefusal;

        /// <summary>
        /// Boundary tier, never Error. The key was understood and cannot act right now, which is
        /// the definition of a boundary; Error would speak on the channel F2 cannot mute, and a
        /// user working in silence who leans on the chord with focus in the toolbar would get an
        /// unmutable sentence fifteen times a second. Boundary rides Manual, which F2 silences.
        /// </summary>
        private void RefuseNudge(string situation, string sentence)
        {
            bool first = _spokenNudgeRefusal != situation;
            _spokenNudgeRefusal = situation;
            // A Boundary with a message speaks it and plays the earcon; with null it is the
            // earcon alone — the viewport-edge idiom the feedback coordinator already has.
            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Boundary, first ? sentence : null, true));
        }

        private ChartSeries? FocusedSeries()
        {
            var state = _store.State;
            return state.ActiveSeries.FirstOrDefault(s => s.Id == state.FocusedSeriesId);
        }

        /// <summary>"LabelText" → "Label Text": a CamelCase modal name is one word to a
        /// synthesiser. Names that already contain spaces pass through unchanged.</summary>
        internal static string SpokenModalName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "a dialog";
            return System.Text.RegularExpressions.Regex.Replace(name, @"(?<=[a-z])(?=[A-Z])", " ");
        }

        private bool IsNavigationCommand(SystemCommand c)
        {
            return c >= SystemCommand.NavLeft && c <= SystemCommand.GranularityDown;
        }

        private bool IsPlaybackCommand(SystemCommand c)
        {
            return c >= SystemCommand.PlayChart && c <= SystemCommand.PlaySpeedDown;
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
                // Ctrl+Space and Shift+Escape with nothing playing used to be silent keys —
                // and Ctrl+Space was worse than silent: TogglePauseAction flipped IsPaused with
                // IsPlaying false, so the NEXT Space read as a stop and was silent too. Two dead
                // presses in a row on the feature that had just learned to speak.
                case SystemCommand.PlayPause:
                    if (!_store.State.IsPlaying)
                    {
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Boundary, NothingIsPlaying, true));
                        break;
                    }
                    _store.Dispatch(new TogglePauseAction());
                    break;
                case SystemCommand.PlayStop:
                    if (!_store.State.IsPlaying)
                    {
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Boundary, NothingIsPlaying, true));
                        break;
                    }
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

        internal const string NothingIsPlaying = "Nothing is playing.";

        private void TogglePlayback(PlaybackScope scope)
        {
            // Second press always stops, regardless of scope match or pause state.
            // Ctrl+Space is the explicit pause/resume key; plain Space/Shift+Space/Ctrl+Shift+Space
            // are start-or-stop toggles with no intermediate hanging state.
            if (_store.State.IsPlaying || _store.State.IsPaused)
            {
                _store.Dispatch(new SetPlaybackAction(false, scope));
                return;
            }

            // Refuse BEFORE dispatching when nothing would play. The orchestrator used to
            // discover this on its own and return without a sound, leaving IsPlaying true in
            // the store: the coordinator's playback gate stayed engaged, every navigation key
            // went quiet, and the next Space "stopped" a playback that had never started. Now
            // that a start is announced, dispatching first would also say "Playing chart"
            // over silence. Boundary, like the other "why did that key do nothing" messages.
            var plan = Audio.PlaybackPlan.Resolve(_store.State, scope);
            if (!plan.IsPlayable)
            {
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Boundary,
                    plan.RefusalReason ?? Audio.PlaybackPlan.NoSeriesReason, true));
                return;
            }

            _store.Dispatch(new SetPlaybackAction(true, scope));
        }
    }
}