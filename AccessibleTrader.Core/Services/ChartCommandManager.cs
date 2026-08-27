using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public interface IChartCommandManager
    {
    }

    /// <summary>
    /// Handles chart-level command events published to the <see cref="IEventBus"/>.
    /// </summary>
    public class ChartCommandManager : IChartCommandManager, IDisposable
    {
        private readonly IEventBus _eventBus;
        private readonly IDataManager _dataManager;
        private readonly IDrawingInteractionManager _drawingManager;
        private readonly ISeriesManagementService _seriesManager;
        private readonly System.Reactive.Disposables.CompositeDisposable _subscriptions = new();
        private readonly IWorkspaceStore _store;
        private readonly ILogger<ChartCommandManager>? _logger;

        /// <summary>
        /// Optional so every existing construction site keeps working; a null stack means
        /// edits are not recorded and Ctrl+Z is inert, which is the pre-2026-08-27 behaviour.
        /// </summary>
        private readonly Accessibility.IChartUndoStack? _undo;

        public ChartCommandManager(
            IEventBus eventBus,
            IDataManager dataManager,
            IDrawingInteractionManager drawingManager,
            ISeriesManagementService seriesManager,
            IWorkspaceStore store,
            ILogger<ChartCommandManager>? logger = null,
            Accessibility.IChartUndoStack? undo = null)
        {
            _eventBus = eventBus;
            _dataManager = dataManager;
            _drawingManager = drawingManager;
            _seriesManager = seriesManager;
            _store = store;
            _logger = logger;
            _undo = undo;

            InitializeSubscriptions();
        }

        private void InitializeSubscriptions()
        {
            // ── VOLUME ────────────────────────────────────────────────────────────────
            // Chart-scope volume is a property of the series, and stops there. It used to also
            // be pushed into the audio engine's global master gain, which applied the factor
            // twice and put earcons — which are not chart audio — behind a chart-scope control.
            // That is why this handler holds no ISonificationManager.
            _subscriptions.Add(_eventBus.Subscribe<VolumeChangeEvent>(ev => {
                try
                {
                    var state = _store.State;
                    var seriesId = state.FocusedSeriesId ?? state.PrimarySeriesId;
                    var s = state.ActiveSeries.FirstOrDefault(x => x.Id == seriesId);
                    if (s == null && ev.Scope != "CHART") return;

                    string targetName;
                    string direction = ev.Delta > 0 ? "increased" : "decreased";

                    if (ev.Scope == "COMPONENT")
                    {
                        if (s == null || s.Components.Count == 0) return;
                        var c = s.Components[s.ClampComponent(state.FocusedComponentIndex)];
                        _store.Dispatch(new AdjustVolumeAction(seriesId, c.Name, ev.Delta));
                        targetName = $"{s.FriendlyName}: {(string.IsNullOrEmpty(c.DisplayName) ? c.Name : c.DisplayName)}";
                    }
                    else if (ev.Scope == "SERIES")
                    {
                        if (s == null) return;
                        _store.Dispatch(new AdjustVolumeAction(seriesId, null, ev.Delta));
                        targetName = s.FriendlyName;
                    }
                    else
                    {
                        _store.Dispatch(new AdjustChartVolumeAction("CHART", ev.Delta));
                        targetName = "Chart volume";
                    }

                    var postState = _store.State;
                    float volume;
                    if (ev.Scope == "COMPONENT")
                    {
                        var ps = postState.ActiveSeries.FirstOrDefault(x => x.Id == seriesId);
                        var pc = ps?.Components.Count > 0
                            ? ps.Components[ps.ClampComponent(postState.FocusedComponentIndex)]
                            : null;
                        volume = pc?.Volume ?? 0f;
                    }
                    else if (ev.Scope == "SERIES")
                    {
                        volume = postState.ActiveSeries.FirstOrDefault(x => x.Id == seriesId)?.Volume ?? 0f;
                    }
                    else
                    {
                        // ChartVolume is applied ONCE, where it belongs: it is threaded into every
                        // chart-sonification path as the masterVolume factor (NavigationSonifier,
                        // AudioSequencer), which multiplies it into each note's volume.
                        //
                        // It used to ALSO be pushed into the engine's global master gain, and that
                        // was wrong twice over. The gain applied a second time, so the chart played
                        // at ChartVolume SQUARED — raising the volume from 50% to 60% made the chart
                        // quieter (0.50 → 0.36), which is the opposite of what the key says it does.
                        // And the engine's master gain is global, not chart-scope: driving it from
                        // F7 put every earcon behind a control documented as "chart volume", which
                        // is precisely the failure the 2026-07-21 mute-tier redesign removed for F3
                        // ("earcons silently died with F3"). Earcons answer to Shift+F3; chart notes
                        // answer to F7. Master gain answers to StopAll, and to nothing else.
                        volume = postState.ChartVolume;
                    }

                    // Workspace save is now explicit (Ctrl+Alt+Shift+W) — no auto-persist.
                    _eventBus.Publish(new FeedbackRequestEvent(
                        FeedbackType.VolumeChange, $"{targetName} volume {direction} to {volume:P0}"));
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[ChartCommandManager] VolumeChangeEvent error");
                }
            }));

            // ── MUTE ─────────────────────────────────────────────────────────────────
            _subscriptions.Add(_eventBus.Subscribe<ToggleMuteEvent>(ev => {
                try
                {
                    var state = _store.State;
                    var seriesId = ev.SeriesId ?? state.FocusedSeriesId ?? state.PrimarySeriesId;
                    var s = state.ActiveSeries.FirstOrDefault(x => x.Id == seriesId);
                    if (s == null) return;

                    bool isComponentScope = ev.Scope == "COMPONENT" || (ev.Scope == null && state.LastInteractionContext == InteractionContext.Component);

                    if (isComponentScope && s.Components.Count > 0)
                    {
                        var c = s.Components[s.ClampComponent(state.FocusedComponentIndex)];
                        _store.Dispatch(new ToggleMuteAction(seriesId, c.Name));
                        var newC = _store.State.ActiveSeries.FirstOrDefault(x => x.Id == seriesId)?.Components.ElementAtOrDefault(s.ClampComponent(state.FocusedComponentIndex));
                        bool nowMuted = newC?.IsMuted ?? false;
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange, $"{s.FriendlyName}: {(string.IsNullOrEmpty(c.DisplayName) ? c.Name : c.DisplayName)} {(nowMuted ? "muted" : "unmuted")}"));
                    }
                    else
                    {
                        _store.Dispatch(new ToggleMuteAction(seriesId));
                        bool nowMuted = _store.State.ActiveSeries.FirstOrDefault(x => x.Id == seriesId)?.IsMuted ?? false;
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange, $"{s.FriendlyName} {(nowMuted ? "muted" : "unmuted")}"));
                    }
                    // Workspace save is now explicit (Ctrl+Alt+Shift+W) — no auto-persist.
                    _eventBus.Publish(new RedrawEvent());
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[ChartCommandManager] ToggleMuteEvent error");
                }
            }));

            // ── HIDE ─────────────────────────────────────────────────────────────────
            _subscriptions.Add(_eventBus.Subscribe<ToggleHideEvent>(ev => {
                try
                {
                    var state = _store.State;
                    var seriesId = ev.SeriesId ?? state.FocusedSeriesId ?? state.PrimarySeriesId;
                    var s = state.ActiveSeries.FirstOrDefault(x => x.Id == seriesId);
                    if (s == null) return;

                    bool isComponentScope = ev.Scope == "COMPONENT" || (ev.Scope == null && state.LastInteractionContext == InteractionContext.Component);

                    if (isComponentScope && s.Components.Count > 0)
                    {
                        var c = s.Components[s.ClampComponent(state.FocusedComponentIndex)];
                        _store.Dispatch(new ToggleHideAction(seriesId, c.Name));
                        var newC = _store.State.ActiveSeries.FirstOrDefault(x => x.Id == seriesId)?.Components.ElementAtOrDefault(s.ClampComponent(state.FocusedComponentIndex));
                        bool nowHidden = !(newC?.IsVisible ?? true);
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange, $"{s.FriendlyName}: {(string.IsNullOrEmpty(c.DisplayName) ? c.Name : c.DisplayName)} {(nowHidden ? "hidden" : "visible")}"));
                    }
                    else
                    {
                        _store.Dispatch(new ToggleHideAction(seriesId));
                        bool nowHidden = !(_store.State.ActiveSeries.FirstOrDefault(x => x.Id == seriesId)?.IsVisible ?? true);
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange, $"{s.FriendlyName} {(nowHidden ? "hidden" : "visible")}"));
                    }
                    // Workspace save is now explicit (Ctrl+Alt+Shift+W) — no auto-persist.
                    _eventBus.Publish(new RedrawEvent());
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[ChartCommandManager] ToggleHideEvent error");
                }
            }));

            // ── DELETE ───────────────────────────────────────────────────────────────
            _subscriptions.Add(_eventBus.Subscribe<DeleteSeriesEvent>(ev => {
                try
                {
                    string? id = ev.SeriesId ?? _store.State.FocusedSeriesId;
                    if (string.IsNullOrEmpty(id) || id == CoreSeriesIds.Candles)
                    {
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error, "Cannot delete the candlestick series."));
                        return;
                    }

                    var series = _store.State.ActiveSeries.FirstOrDefault(x => x.Id == id);
                    if (series != null)
                    {
                        _store.Dispatch(new RemoveSeriesAction(id));
                        // Recorded so Ctrl+Z brings it back. Delete has no confirmation step —
                        // a deliberate choice, because a confirmation on every delete is its
                        // own accessibility cost — which only works if the deletion is
                        // reversible. The whole ChartSeries is held rather than a description
                        // of it: a drawing's identity is its Id and its component arrays are
                        // recomputed from its anchors, so anything less restores a different
                        // drawing that merely looks similar.
                        _undo?.Push(new Accessibility.SeriesDeleteUndo(
                            $"Delete {series.Name}",
                            series,
                            restore: s =>
                            {
                                _store.Dispatch(new AddSeriesAction(s));
                                _eventBus.Publish(new RedrawEvent());
                            },
                            remove: seriesId =>
                            {
                                _store.Dispatch(new RemoveSeriesAction(seriesId));
                                _eventBus.Publish(new RedrawEvent());
                            }));

                        // Workspace save is now explicit (Ctrl+Alt+Shift+W) — no auto-persist.
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange, $"{series.Name} deleted"));
                        _eventBus.Publish(new RedrawEvent());
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[ChartCommandManager] DeleteSeriesEvent error");
                }
            }));

            // ── UNDO / REDO ──────────────────────────────────────────────────────────
            //
            // Every branch SPEAKS, including the two that do nothing. Silence on Ctrl+Z is
            // indistinguishable from undo being broken when you cannot see the chart, and the
            // one thing a user needs to know after pressing it is which of the two happened.
            _subscriptions.Add(_eventBus.Subscribe<UndoChartEditEvent>(_ => {
                try
                {
                    if (_undo == null) return;
                    string? what = _undo.NextUndoDescription;
                    if (_undo.Undo())
                        _eventBus.Publish(new FeedbackRequestEvent(
                            FeedbackType.StateChange, $"Undone: {what}."));
                    else
                        _eventBus.Publish(new FeedbackRequestEvent(
                            FeedbackType.StateChange, "Nothing to undo."));
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[ChartCommandManager] UndoChartEditEvent error");
                }
            }));

            _subscriptions.Add(_eventBus.Subscribe<RedoChartEditEvent>(_ => {
                try
                {
                    if (_undo == null) return;
                    string? what = _undo.NextRedoDescription;
                    if (_undo.Redo())
                        _eventBus.Publish(new FeedbackRequestEvent(
                            FeedbackType.StateChange, $"Redone: {what}."));
                    else
                        _eventBus.Publish(new FeedbackRequestEvent(
                            FeedbackType.StateChange, "Nothing to redo."));
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[ChartCommandManager] RedoChartEditEvent error");
                }
            }));

            // The stack is scoped to one chart: an undo must never reach across a symbol or
            // timeframe change into a chart that is no longer on screen.
            _subscriptions.Add(_eventBus.Subscribe<TabSwitchedEvent>(_ => _undo?.Clear()));

            // ── TOOLS ────────────────────────────────────────────────────────────────
            _subscriptions.Add(_eventBus.Subscribe<ToggleToolEvent>(ev => {
                try
                {
                    if (ev.Tool == ToolType.Heatmap)
                    {
                        var heatmap = _store.State.ActiveSeries.FirstOrDefault(s => s.Id == CoreSeriesIds.Heatmap);
                        if (heatmap != null)
                        {
                            _eventBus.Publish(new ToggleHideEvent("SERIES", heatmap.Id));
                        }
                        else
                        {
                            _seriesManager.RegisterSeries(CoreSeriesIds.Heatmap, "Liquidity Heatmap", new List<string> { "Liquidity" });
                            _eventBus.Publish(new AnnouncementEvent("Heatmap added. Note: Only live data will populate heatmap bars."));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[ChartCommandManager] ToggleToolEvent error");
                }
            }));

            // ── DRAWING ──────────────────────────────────────────────────────────────
            _subscriptions.Add(_eventBus.Subscribe<AddDrawingEvent>(ev => {
                try
                {
                    _drawingManager.HandleAddDrawing(ev.DrawingType, _dataManager.Data);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[ChartCommandManager] AddDrawingEvent error");
                }
            }));
            _subscriptions.Add(_eventBus.Subscribe<PlaceDrawingAnchorEvent>(_ => {
                try
                {
                    _drawingManager.PlaceAnchorAtCursor(_dataManager.Data);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[ChartCommandManager] PlaceDrawingAnchorEvent error");
                }
            }));
        }

        public void Dispose() => _subscriptions.Dispose();
    }
}
