using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

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
        private readonly ISpeechManager _speechManager;
        private readonly ISonificationManager _sonificationManager;
        private readonly ISeriesManagementService _seriesManager;
        private readonly System.Reactive.Disposables.CompositeDisposable _subscriptions = new();
        private readonly IWorkspaceStore _store;

        public ChartCommandManager(
            IEventBus eventBus,
            IDataManager dataManager,
            IDrawingInteractionManager drawingManager,
            ISpeechManager speechManager,
            ISonificationManager sonificationManager,
            ISeriesManagementService seriesManager,
            IWorkspaceStore store)
        {
            _eventBus = eventBus;
            _dataManager = dataManager;
            _drawingManager = drawingManager;
            _speechManager = speechManager;
            _sonificationManager = sonificationManager;
            _seriesManager = seriesManager;
            _store = store;

            InitializeSubscriptions();
        }

        private void InitializeSubscriptions()
        {
            // ── VOLUME ────────────────────────────────────────────────────────────────
            _subscriptions.Add(_eventBus.Subscribe<VolumeChangeEvent>(ev => {
                try
                {
                    var state = _store.State;
                    var seriesId = state.FocusedSeriesId ?? CoreSeriesIds.Candles;
                    var s = state.ActiveSeries.FirstOrDefault(x => x.Id == seriesId);
                    if (s == null && ev.Scope != "CHART") return;

                    string targetName;
                    string direction = ev.Delta > 0 ? "increased" : "decreased";

                    if (ev.Scope == "COMPONENT")
                    {
                        if (s == null || s.Components.Count == 0) return;
                        var c = s.Components[Math.Clamp(state.FocusedComponentIndex, 0, s.Components.Count - 1)];
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
                            ? ps.Components[Math.Clamp(postState.FocusedComponentIndex, 0, ps.Components.Count - 1)]
                            : null;
                        volume = pc?.Volume ?? 0f;
                    }
                    else if (ev.Scope == "SERIES")
                    {
                        volume = postState.ActiveSeries.FirstOrDefault(x => x.Id == seriesId)?.Volume ?? 0f;
                    }
                    else
                    {
                        volume = postState.ChartVolume;
                        _sonificationManager.SetMasterVolume(volume);
                    }

                    _seriesManager.PersistWorkspace();
                    _eventBus.Publish(new FeedbackRequestEvent(
                        FeedbackType.VolumeChange, $"{targetName} volume {direction} to {volume:P0}"));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ChartCommandManager] VolumeChangeEvent error: {ex.Message}");
                }
            }));

            // ── MUTE ─────────────────────────────────────────────────────────────────
            _subscriptions.Add(_eventBus.Subscribe<ToggleMuteEvent>(ev => {
                try
                {
                    var state = _store.State;
                    var seriesId = ev.SeriesId ?? state.FocusedSeriesId ?? CoreSeriesIds.Candles;
                    var s = state.ActiveSeries.FirstOrDefault(x => x.Id == seriesId);
                    if (s == null) return;

                    bool isComponentScope = ev.Scope == "COMPONENT" || (ev.Scope == null && state.LastInteractionContext == InteractionContext.Component);

                    if (isComponentScope && s.Components.Count > 0)
                    {
                        var c = s.Components[Math.Clamp(state.FocusedComponentIndex, 0, s.Components.Count - 1)];
                        _store.Dispatch(new ToggleMuteAction(seriesId, c.Name));
                        var newC = _store.State.ActiveSeries.FirstOrDefault(x => x.Id == seriesId)?.Components.ElementAtOrDefault(Math.Clamp(state.FocusedComponentIndex, 0, s.Components.Count - 1));
                        bool nowMuted = newC?.IsMuted ?? false;
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange, $"{s.FriendlyName}: {(string.IsNullOrEmpty(c.DisplayName) ? c.Name : c.DisplayName)} {(nowMuted ? "muted" : "unmuted")}"));
                    }
                    else
                    {
                        _store.Dispatch(new ToggleMuteAction(seriesId));
                        bool nowMuted = _store.State.ActiveSeries.FirstOrDefault(x => x.Id == seriesId)?.IsMuted ?? false;
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange, $"{s.FriendlyName} {(nowMuted ? "muted" : "unmuted")}"));
                    }
                    _seriesManager.PersistWorkspace();
                    _eventBus.Publish(new RedrawEvent());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ChartCommandManager] ToggleMuteEvent error: {ex.Message}");
                }
            }));

            // ── HIDE ─────────────────────────────────────────────────────────────────
            _subscriptions.Add(_eventBus.Subscribe<ToggleHideEvent>(ev => {
                try
                {
                    var state = _store.State;
                    var seriesId = ev.SeriesId ?? state.FocusedSeriesId ?? CoreSeriesIds.Candles;
                    var s = state.ActiveSeries.FirstOrDefault(x => x.Id == seriesId);
                    if (s == null) return;

                    bool isComponentScope = ev.Scope == "COMPONENT" || (ev.Scope == null && state.LastInteractionContext == InteractionContext.Component);

                    if (isComponentScope && s.Components.Count > 0)
                    {
                        var c = s.Components[Math.Clamp(state.FocusedComponentIndex, 0, s.Components.Count - 1)];
                        _store.Dispatch(new ToggleHideAction(seriesId, c.Name));
                        var newC = _store.State.ActiveSeries.FirstOrDefault(x => x.Id == seriesId)?.Components.ElementAtOrDefault(Math.Clamp(state.FocusedComponentIndex, 0, s.Components.Count - 1));
                        bool nowHidden = !(newC?.IsVisible ?? true);
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange, $"{s.FriendlyName}: {(string.IsNullOrEmpty(c.DisplayName) ? c.Name : c.DisplayName)} {(nowHidden ? "hidden" : "visible")}"));
                    }
                    else
                    {
                        _store.Dispatch(new ToggleHideAction(seriesId));
                        bool nowHidden = !(_store.State.ActiveSeries.FirstOrDefault(x => x.Id == seriesId)?.IsVisible ?? true);
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange, $"{s.FriendlyName} {(nowHidden ? "hidden" : "visible")}"));
                    }
                    _seriesManager.PersistWorkspace();
                    _eventBus.Publish(new RedrawEvent());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ChartCommandManager] ToggleHideEvent error: {ex.Message}");
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
                        _seriesManager.PersistWorkspace();
                        _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange, $"{series.Name} deleted"));
                        _eventBus.Publish(new RedrawEvent());
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ChartCommandManager] DeleteSeriesEvent error: {ex.Message}");
                }
            }));

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
                    System.Diagnostics.Debug.WriteLine($"[ChartCommandManager] ToggleToolEvent error: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine($"[ChartCommandManager] AddDrawingEvent error: {ex.Message}");
                }
            }));
        }

        public void Dispose() => _subscriptions.Dispose();
    }
}
