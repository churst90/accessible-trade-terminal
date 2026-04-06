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

        // Chart focus gate: navigation and drawing commands require the chart to be active.
        // Starts true so keyboard navigation works immediately on app start without requiring
        // an explicit click on the chart div. Debounce on deactivate prevents the race where
        // the JS keydown callback arrives milliseconds before the Blazor onblur fires.
        private volatile bool _isChartActive = true;
        private Timer? _deactivateDebounce;
        private const int DEACTIVATE_DEBOUNCE_MS = 50;

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
        }

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
            _deactivateDebounce?.Dispose();
        }

        public void Dispatch(SystemCommand command)
        {
            if (command == SystemCommand.None) return;

            // Sub-pane navigation — needs chart data and focus gate, handled after those gates.
            if (command == SystemCommand.NavSubPaneNext || command == SystemCommand.NavSubPanePrev)
            {
                if (!_isChartActive) return;
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
                if (!_isChartActive) return;
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
                case SystemCommand.ContextSummary: _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info, "CONTEXT_SUMMARY", true)); return;
                case SystemCommand.ChartFocus:
                    _eventBus.Publish(new ChartFocusEvent());
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info, "CONTEXT_SUMMARY", true));
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

            // 2. CHART-FOCUS GATE (Navigation/drawing require chart to be active)
            if (!_isChartActive && (IsNavigationCommand(command) || IsPlaybackCommand(command) || IsDrawingCommand(command)))
                return;

            // 3. DATA VALIDATION (Chart commands require a loaded chart)
            if (_store.State.Data == null || !_store.State.Data.Any())
            {
                // If the user tries to navigate or interact with an empty chart, announce it.
                if (IsNavigationCommand(command) || IsPlaybackCommand(command))
                {
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error, "No chart loaded.", true));
                }
                return;
            }

            // 4. NAVIGATION & VIEWPORT
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

            // 5. PLAYBACK ENGINE
            if (IsPlaybackCommand(command))
            {
                HandlePlayback(command);
                return;
            }

            // 6. SETTINGS & VISUAL TOGGLES
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