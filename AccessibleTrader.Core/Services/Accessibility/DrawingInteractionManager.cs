using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public interface IDrawingInteractionManager
    {
        void HandleAddDrawing(string type, IReadOnlyList<Ohlcv> chartData);
        void HandleMouseEvent(double x, double y, string type, double width, double height);
    }

    /// <summary>
    /// Manages user interactions for placing drawings on the chart.
    /// Implements a two or three click state machine for multi-point drawings.
    /// Single-point drawings complete immediately.
    /// </summary>
    public class DrawingInteractionManager : IDrawingInteractionManager, IDisposable
    {
        private readonly IEventBus _eventBus;
        private readonly IDrawingService _drawingService;
        private readonly IWorkspaceStore _store;
        private readonly IIndicatorModelFactory _modelFactory;
        private readonly IInputService _inputService;
        private readonly System.Reactive.Disposables.CompositeDisposable _subs = new();

        private DrawingType _pendingDrawingType = DrawingType.None;
        private DateTime? _anchorDate1;
        private double? _anchorPrice1;
        private DateTime? _anchorDate2;
        private double? _anchorPrice2;

        // ── Click-drag placement state ───────────────────────────────────────
        // A "preview" drawing series is added to the store immediately on the first
        // MouseDown so the user sees the line follow the cursor as they drag. MouseMove
        // updates the preview's second anchor and recomputes its component arrays;
        // MouseUp commits (preview IS the final drawing; no series swap needed). Click-
        // click users (release without moving) bypass the commit and enter a legacy
        // second-click flow that still uses the preview for feedback on subsequent moves.
        private string? _previewSeriesId;
        private bool _isMouseDragging;
        private double _dragStartX;
        private double _dragStartY;
        // Pixel threshold below which MouseUp is treated as "click, not drag" — leaves
        // click-click placement working for users who don't want drag. 5 px covers
        // accidental hand tremor on mouse-down.
        private const double DragDeadZonePixels = 5.0;

        // ── Viewport pan-drag state ──────────────────────────────────────────
        // Click-drag on empty chart space — no drawing tool selected AND no anchor
        // handle under the cursor — scrolls the viewport through time, like grabbing
        // the chart and sliding it. Resolved in pixel space so it keeps working when
        // the cursor moves outside the data range mid-drag. Independent of the
        // placement and edit-drag flows above; the three are mutually exclusive.
        private bool _isPanning;
        private double _panLastX;
        private double _panAccumBars;   // carried fractional bars between moves
        // Total absolute pixel travel across the drag. A "pan" that never leaves the
        // click dead zone is a plain click — and a click on empty chart space selects
        // the bar under the cursor (same pipeline as arrow-key navigation, so speech
        // and sonification fire identically for mouse and keyboard users).
        private double _panTravelPixels;
        // When a leftward pan brings the viewport start within this many bars of the
        // data edge, request a history backfill — mirrors ViewportManager.HandlePan.
        private const int PanHistoryBackfillThreshold = 50;

        // ── Edit-drag state (reposition existing drawing endpoints) ──────────
        // On MouseDown over an existing drawing's anchor handle, we enter edit-drag
        // mode: subsequent MouseMove events update that specific anchor on the hit
        // series; MouseUp commits. Independent of the placement state above — a
        // drawing can be in "edit" mode only when no new drawing is being placed.
        private string? _editSeriesId;
        private int _editAnchorSlot;   // 1, 2, or 3
        // Anchor-handle hit-test tolerance in pixels. 10 px matches the rendered
        // anchor handle radius with a small overshoot for easier grabbing.
        private const double AnchorHitToleranceSquared = 10.0 * 10.0;

        public DrawingInteractionManager(
            IEventBus eventBus,
            IDrawingService drawingService,
            IWorkspaceStore store,
            IIndicatorModelFactory modelFactory,
            IInputService inputService)
        {
            _eventBus = eventBus;
            _drawingService = drawingService;
            _store = store;
            _modelFactory = modelFactory;
            _inputService = inputService;

            _subs.Add(_eventBus.Subscribe<CancelDrawingEvent>(_ => CancelPendingDrawing()));
            _inputService.MouseEvent += HandleMouseEvent;
        }

        public void HandleMouseEvent(double x, double y, string type, double width, double height)
        {
            // Fast-reject events that have no drawing context. MouseMove with an active
            // preview OR an active edit-drag is allowed even when _pendingDrawingType is
            // None, because a two-point drawing's preview (or a handle-drag edit) outlives
            // the pending flag until MouseUp commits. An in-progress pan-drag likewise
            // keeps MouseMove/MouseUp flowing so it can complete. ContextMenu must always
            // pass: right-click works from idle (it opens the drawing menu on an anchor
            // hit, or the chart-level menu on a miss).
            if (_pendingDrawingType == DrawingType.None
                && _previewSeriesId == null
                && _editSeriesId == null
                && !_isPanning
                && type != "MouseDown"
                && type != "ContextMenu") return;

            // Active pan drag is resolved in pure pixel space, ahead of the anchor
            // coordinate mapping below (which rejects cursor positions outside the data
            // range and would otherwise stall a pan near the chart edges).
            if (_isPanning)
            {
                if (type == "MouseMove") { UpdatePan(x, width); return; }
                if (type == "MouseUp")   { EndPan(x, width); return; }
            }

            var state = _store.State;
            if (state.Data == null || state.Data.Count == 0) return;

            // Map screen coordinates to Price/Date using actual viewport dimensions
            double price = MapYToPrice(y, height, state.ViewportRange.Min, state.ViewportRange.Max, state.IsLogScale);
            int dataIndex = MapXToIndex(x, width, state.ViewportStartIndex, state.ViewportLength);

            // Future-space anchor allowance. A drawing anchor may land in the 20-bar right
            // margin (the reserved projection slot) for drawings like trendlines that need a
            // forward anchor point. For indices at or past state.Data.Count we synthesise a
            // date by extrapolating from the last real bar's timeframe cadence — the anchor
            // date is stored on DrawingData and survives persistence via the same record path
            // as any other anchor. Indices beyond Data.Count + RightMarginBars are out of
            // bounds and rejected as before.
            int maxFutureIndex = state.Data.Count + System.Math.Max(0, state.RightMarginBars) - 1;
            if (dataIndex < 0 || dataIndex > maxFutureIndex) return;

            System.DateTime anchorDate;
            if (dataIndex < state.Data.Count)
            {
                anchorDate = state.Data[dataIndex].Date;
            }
            else
            {
                anchorDate = ProjectFutureDate(state.Data, dataIndex - (state.Data.Count - 1));
            }

            switch (type)
            {
                case "MouseDown":
                    HandleMouseDown(anchorDate, price, x, y, width, height);
                    break;
                case "MouseMove":
                    HandleMouseMove(anchorDate, price);
                    break;
                case "MouseUp":
                    HandleMouseUp(anchorDate, price, x, y);
                    break;
                case "ContextMenu":
                    HandleContextMenu(x, y, width, height);
                    break;
            }
        }

        /// <summary>
        /// Right-click on the chart: hit-test drawings' anchor handles. If a drawing is
        /// hit, publish <see cref="OpenDrawingContextMenuEvent"/> so the floating menu
        /// component can show itself anchored at the cursor. Misses are no-ops (the JS
        /// already suppressed the browser menu).
        /// </summary>
        private void HandleContextMenu(double x, double y, double width, double height)
        {
            var state = _store.State;
            if (state.Data == null || state.Data.Count == 0) return;

            string? bestSeriesId = null;
            double bestDistSq = AnchorHitToleranceSquared;

            foreach (var s in state.ActiveSeries)
            {
                if (!s.IsDrawing || s.Drawing == null) continue;
                var anchors = new (DateTime? d, double? p)[]
                {
                    (s.Drawing.AnchorDate1, s.Drawing.AnchorPrice1),
                    (s.Drawing.AnchorDate2, s.Drawing.AnchorPrice2),
                    (s.Drawing.AnchorDate3, s.Drawing.AnchorPrice3),
                };
                foreach (var (d, p) in anchors)
                {
                    if (d == null && p == null) continue;
                    double ax = d.HasValue ? DateToScreenX(d.Value, state, width) : x;
                    double ay = p.HasValue ? PriceToScreenY(p.Value, state, height) : y;
                    double dx = ax - x, dy = ay - y;
                    double distSq = dx * dx + dy * dy;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestSeriesId = s.Id;
                    }
                }
            }

            if (bestSeriesId != null)
            {
                _eventBus.Publish(new OpenDrawingContextMenuEvent(bestSeriesId, x, y));
                return;
            }

            // No drawing anchor under the cursor → chart-level context menu. Carries the
            // bar index under the cursor so "Play from here" can start playback at the
            // right-clicked bar; -1 when the click landed in the empty right margin.
            int barIndex = ChartMath.MapXToIndex(x, width, state.ViewportStartIndex, state.ViewportLength);
            if (barIndex < 0 || barIndex >= state.Data.Count) barIndex = -1;
            _eventBus.Publish(new OpenChartContextMenuEvent(x, y, barIndex));
        }

        private void HandleMouseDown(DateTime date, double price, double x, double y, double width, double height)
        {
            _dragStartX = x;
            _dragStartY = y;

            // No pending placement → try to grab an existing drawing's anchor handle.
            // Only when nothing else is in flight; otherwise the user is mid-placement
            // and the click belongs to that flow.
            if (_pendingDrawingType == DrawingType.None && _previewSeriesId == null)
            {
                if (TryBeginEditDrag(x, y, width, height))
                {
                    _isMouseDragging = true;
                    return;
                }
                // No drawing tool armed and no handle grabbed → grab the chart itself
                // and pan the viewport for the duration of this drag.
                BeginPan(x);
                return;
            }

            _isMouseDragging = true;

            if (_anchorDate1 == null)
            {
                // First click — materialise the anchor and start a preview series.
                HandleDrawingStep(date, price);
                // For two-point drawings, also seed a preview at (anchor1, anchor1) so
                // MouseMove has something to update. Single-point drawings complete in
                // HandleDrawingStep and return here with no active _pendingDrawingType.
                if (_pendingDrawingType != DrawingType.None && _previewSeriesId == null)
                {
                    TryStartPreview(date, price);
                }
            }
            else
            {
                // Legacy second click — commit without waiting for a mouse-up. Preserves
                // click-click idiom for users who don't drag.
                HandleDrawingStep(date, price);
            }
        }

        private void HandleMouseMove(DateTime date, double price)
        {
            // Edit-drag of an existing drawing's anchor takes priority over placement
            // preview — the two flows never run simultaneously.
            if (_editSeriesId != null)
            {
                UpdateEditDrag(date, price);
                return;
            }
            // Preview tracking only runs while a preview series exists. Works both during
            // an active drag and between the two clicks of a legacy click-click flow.
            if (_previewSeriesId == null) return;
            UpdatePreview(date, price);
        }

        private void HandleMouseUp(DateTime date, double price, double x, double y)
        {
            if (!_isMouseDragging) return;
            _isMouseDragging = false;

            // Edit-drag release: commit the anchor edit. The series is already mutated
            // in place; we only need to announce and clear state.
            if (_editSeriesId != null)
            {
                CommitEditDrag(date, price);
                return;
            }

            if (_pendingDrawingType == DrawingType.None) return;

            double dx = x - _dragStartX;
            double dy = y - _dragStartY;
            double moved = Math.Sqrt(dx * dx + dy * dy);
            if (moved < DragDeadZonePixels)
            {
                // Treated as click, not drag — leave pending state intact for a second click.
                return;
            }

            // Drag commit: the preview series is already in the store with the final
            // anchor 2 position. For two-point drawings we don't need CompleteDrawing to
            // recreate anything; we just promote the preview to a committed drawing and
            // clear the state machine. For three-point drawings (FibExtension, etc.) the
            // drag fills anchor 2; the user still needs a separate click for anchor 3,
            // so fall through to legacy HandleDrawingStep.
            bool isThreePoint = _pendingDrawingType is
                DrawingType.FibExtension or
                DrawingType.RiskReward or
                DrawingType.AndrewsPitchfork;

            if (!isThreePoint)
            {
                PromotePreviewToCommitted(date, price);
            }
            else
            {
                // Record anchor 2 via the legacy path; preview keeps tracking for anchor 3.
                HandleDrawingStep(date, price);
            }
        }

        // ── Viewport pan-drag ────────────────────────────────────────────────

        /// <summary>Begin a click-drag pan from the given cursor X (pixels).</summary>
        private void BeginPan(double x)
        {
            _isPanning = true;
            _panLastX = x;
            _panAccumBars = 0;
            _panTravelPixels = 0;
        }

        /// <summary>
        /// Advance an in-progress pan. Converts the pixel travel since the last move into
        /// whole-bar viewport steps (carrying the fractional remainder so slow drags still
        /// accumulate). Dragging right pulls the chart right, revealing older bars — a
        /// decrease in ViewportStartIndex, hence the negated delta.
        /// </summary>
        private void UpdatePan(double x, double width)
        {
            if (width <= 0) { _panLastX = x; return; }
            var state = _store.State;
            if (state.Data == null || state.Data.Count == 0) { _panLastX = x; return; }

            double dxPixels = x - _panLastX;
            _panLastX = x;
            _panTravelPixels += Math.Abs(dxPixels);

            double barsPerPixel = state.ViewportLength / width;
            _panAccumBars += dxPixels * barsPerPixel;
            int whole = (int)_panAccumBars;
            if (whole == 0) return;
            _panAccumBars -= whole;

            // Panning left (whole > 0 → viewport start decreases) toward the data edge:
            // proactively backfill history so the drag can keep going, like the keys do.
            if (whole > 0 && state.ViewportStartIndex < PanHistoryBackfillThreshold)
                _eventBus.Publish(new RequestHistoryEvent());

            _store.Dispatch(new PanAction(-whole));
        }

        /// <summary>
        /// End a pan-drag. A drag that never left the click dead zone is a plain click:
        /// select the bar under the cursor through the exact pipeline the arrow keys use
        /// (SetCursor + Navigation feedback), so a sighted user's click and a keyboard
        /// user's arrows produce identical speech + sonification — and the keyboard
        /// cursor lands where the mouse pointed, letting a low-vision user click roughly
        /// then fine-tune with arrows. A real drag speaks the new viewport range instead.
        /// </summary>
        private void EndPan(double x, double width)
        {
            _isPanning = false;
            _panAccumBars = 0;
            bool wasClick = _panTravelPixels < DragDeadZonePixels;
            _panTravelPixels = 0;

            if (wasClick)
            {
                SelectBarAtPixel(x, width);
                return;
            }
            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.ViewportChange, ""));
        }

        /// <summary>
        /// Move the keyboard cursor to the bar under pixel <paramref name="x"/> and fire
        /// the standard navigation feedback (speech + sonification). Clicks on the empty
        /// right margin / future space are no-ops — there is no bar there to hear.
        /// </summary>
        private void SelectBarAtPixel(double x, double width)
        {
            var state = _store.State;
            if (state.Data == null || state.Data.Count == 0 || width <= 0) return;

            int index = ChartMath.MapXToIndex(x, width, state.ViewportStartIndex, state.ViewportLength);
            if (index < 0 || index >= state.Data.Count) return;

            _store.Dispatch(new SetCursorAction(index));
            _store.Dispatch(new SetInteractionContextAction(InteractionContext.Component));
            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "", true, IsXMove: true, IsJump: true));
        }

        /// <summary>
        /// Walk every drawing series in the workspace and find the nearest anchor handle
        /// within <see cref="AnchorHitToleranceSquared"/> pixels of the cursor. If one is
        /// found, enter edit-drag mode: subsequent MouseMove events update that anchor on
        /// that series. Returns true when a handle was grabbed. Called from MouseDown when
        /// no placement flow is active.
        /// </summary>
        private bool TryBeginEditDrag(double x, double y, double width, double height)
        {
            var state = _store.State;
            if (state.Data == null || state.Data.Count == 0) return false;

            string? bestSeriesId = null;
            int bestSlot = 0;
            double bestDistSq = AnchorHitToleranceSquared;

            foreach (var s in state.ActiveSeries)
            {
                if (!s.IsDrawing || s.Drawing == null) continue;

                // Each anchor slot maps its own (date?, price?) to screen (x, y). Slots
                // with only one axis (HorizontalLine = price only, VerticalLine = date
                // only) use the cursor's other coordinate so the handle is still grabbable.
                // Missing slots (most drawings have only 2 anchors) are skipped.
                var anchors = new (DateTime? d, double? p, int slot)[]
                {
                    (s.Drawing.AnchorDate1, s.Drawing.AnchorPrice1, 1),
                    (s.Drawing.AnchorDate2, s.Drawing.AnchorPrice2, 2),
                    (s.Drawing.AnchorDate3, s.Drawing.AnchorPrice3, 3),
                };

                foreach (var (d, p, slot) in anchors)
                {
                    if (d == null && p == null) continue;
                    double anchorX = d.HasValue ? DateToScreenX(d.Value, state, width) : x;
                    double anchorY = p.HasValue ? PriceToScreenY(p.Value, state, height) : y;
                    double dx = anchorX - x;
                    double dy = anchorY - y;
                    double distSq = dx * dx + dy * dy;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestSeriesId = s.Id;
                        bestSlot = slot;
                    }
                }
            }

            if (bestSeriesId == null) return false;

            _editSeriesId = bestSeriesId;
            _editAnchorSlot = bestSlot;
            return true;
        }

        /// <summary>
        /// MouseMove during an active edit-drag: write the new cursor position into the
        /// selected anchor slot, recompute component arrays, publish RedrawEvent. Handles
        /// HorizontalLine and VerticalLine specially — those drawings have only one
        /// meaningful axis per anchor.
        /// </summary>
        private void UpdateEditDrag(DateTime date, double price)
        {
            if (_editSeriesId == null) return;
            var series = _store.State.ActiveSeries.FirstOrDefault(s => s.Id == _editSeriesId);
            if (series?.Drawing == null) return;

            switch (_editAnchorSlot)
            {
                case 1:
                    if (series.Drawing.Type != DrawingType.HorizontalLine)
                        series.Drawing.AnchorDate1 = date;
                    if (series.Drawing.Type != DrawingType.VerticalLine)
                        series.Drawing.AnchorPrice1 = price;
                    break;
                case 2:
                    series.Drawing.AnchorDate2 = date;
                    series.Drawing.AnchorPrice2 = price;
                    break;
                case 3:
                    series.Drawing.AnchorDate3 = date;
                    series.Drawing.AnchorPrice3 = price;
                    break;
            }

            var results = _drawingService.CalculateDrawingData(series.Drawing, _store.State.Data.ToList());
            foreach (var kvp in results)
            {
                series.Data.ComponentData[kvp.Key] = kvp.Value;
            }
            _eventBus.Publish(new RedrawEvent());
        }

        /// <summary>
        /// MouseUp during an active edit-drag: commit the final anchor position and
        /// clear edit state. Series mutation is already persisted via
        /// <see cref="SeriesManagementService"/>'s normal workspace-persistence hook.
        /// </summary>
        private void CommitEditDrag(DateTime date, double price)
        {
            if (_editSeriesId == null) return;
            UpdateEditDrag(date, price);
            var series = _store.State.ActiveSeries.FirstOrDefault(s => s.Id == _editSeriesId);
            string label = series?.FriendlyName ?? "Drawing";
            _eventBus.Publish(new AnnouncementEvent(
                $"{label} anchor {_editAnchorSlot} moved to {SpeechPriceFormatter.FormatPrice(price)}."));
            _editSeriesId = null;
            _editAnchorSlot = 0;
        }

        private static double DateToScreenX(DateTime date, WorkspaceState state, double width)
        {
            if (state.Data == null || state.Data.Count == 0 || state.ViewportLength <= 0) return 0;
            // Binary-search the closest bar by date, then map to screen x via bar fraction.
            int idx = FindNearestBarIndex(state.Data, date);
            double frac = (idx - state.ViewportStartIndex) / (double)state.ViewportLength;
            return frac * width;
        }

        private static int FindNearestBarIndex(IReadOnlyList<Ohlcv> data, DateTime target)
        {
            int lo = 0, hi = data.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (data[mid].Date < target) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private static double PriceToScreenY(double price, WorkspaceState state, double height)
            => ChartMath.PriceToScreenY(price, height, state.ViewportRange.Min, state.ViewportRange.Max, state.IsLogScale);

        /// <summary>
        /// Create a preview drawing series seeded with anchor1 on both ends so the user
        /// immediately sees "something" at the cursor. Later MouseMove updates replace
        /// anchor 2 with the live cursor position.
        /// </summary>
        private void TryStartPreview(DateTime anchor1Date, double anchor1Price)
        {
            // Single-point drawings never need a preview.
            if (_pendingDrawingType is DrawingType.HorizontalLine
                or DrawingType.VerticalLine
                or DrawingType.TextLabel
                or DrawingType.AnchoredVwap) return;

            var preview = new DrawingData
            {
                Type = _pendingDrawingType,
                AnchorDate1 = anchor1Date,
                AnchorPrice1 = anchor1Price,
                AnchorDate2 = anchor1Date,
                AnchorPrice2 = anchor1Price,
                ExtendRight = true,
            };
            string name = FriendlyName(_pendingDrawingType);
            _previewSeriesId = CreateDrawingSeries(name, preview, _store.State.Data.ToList());
        }

        /// <summary>
        /// Update the preview series' second (or third, if we're past anchor 2) anchor
        /// to the current cursor position and recompute component arrays so the renderer
        /// shows the line tracking the mouse. Mutates the existing ChartSeries in place
        /// — ActiveSeries is an immutable list but its members are mutable records.
        /// </summary>
        private void UpdatePreview(DateTime cursorDate, double cursorPrice)
        {
            if (_previewSeriesId == null) return;
            var series = _store.State.ActiveSeries.FirstOrDefault(s => s.Id == _previewSeriesId);
            if (series?.Drawing == null) return;

            if (_anchorDate2 == null)
            {
                series.Drawing.AnchorDate2 = cursorDate;
                series.Drawing.AnchorPrice2 = cursorPrice;
            }
            else
            {
                // Three-point preview between anchors 2 and 3.
                series.Drawing.AnchorDate3 = cursorDate;
                series.Drawing.AnchorPrice3 = cursorPrice;
            }

            var results = _drawingService.CalculateDrawingData(series.Drawing, _store.State.Data.ToList());
            foreach (var kvp in results)
            {
                series.Data.ComponentData[kvp.Key] = kvp.Value;
            }
            _eventBus.Publish(new RedrawEvent());
        }

        /// <summary>
        /// Drag-release commit. The preview series stays in the workspace; we just
        /// finalise its anchors and clear the placement state machine. No new series
        /// is created — the preview IS the committed drawing.
        /// </summary>
        private void PromotePreviewToCommitted(DateTime finalDate, double finalPrice)
        {
            if (_previewSeriesId == null) return;
            var series = _store.State.ActiveSeries.FirstOrDefault(s => s.Id == _previewSeriesId);
            if (series?.Drawing != null)
            {
                series.Drawing.AnchorDate2 = finalDate;
                series.Drawing.AnchorPrice2 = finalPrice;
                var results = _drawingService.CalculateDrawingData(series.Drawing, _store.State.Data.ToList());
                foreach (var kvp in results)
                {
                    series.Data.ComponentData[kvp.Key] = kvp.Value;
                }
            }

            string label = FriendlyName(_pendingDrawingType);
            double fromPrice = _anchorPrice1 ?? finalPrice;
            string feedback = Math.Abs(finalPrice - fromPrice) > 0.001
                ? $"{label} placed from {SpeechPriceFormatter.FormatPrice(fromPrice)} to {SpeechPriceFormatter.FormatPrice(finalPrice)}."
                : $"{label} placed at {SpeechPriceFormatter.FormatPrice(finalPrice)}.";
            _eventBus.Publish(new AnnouncementEvent(feedback));
            _eventBus.Publish(new RedrawEvent());

            _previewSeriesId = null;
            _pendingDrawingType = DrawingType.None;
            _anchorDate1 = null; _anchorPrice1 = null;
            _anchorDate2 = null; _anchorPrice2 = null;
        }

        /// <summary>
        /// Project a synthetic date offset forward from the last real bar by <paramref name="offsetBars"/>
        /// steps, inferring the timeframe from the median inter-bar delta across the tail of
        /// the loaded history. Using a tail-median avoids overreacting to gaps in historical
        /// data (e.g. stock weekends, session boundaries) that would make a naive last-two-bar
        /// delta misleading. Falls back to a 1-minute step when fewer than 3 bars are loaded.
        /// </summary>
        internal static System.DateTime ProjectFutureDate(System.Collections.Generic.IReadOnlyList<Ohlcv> data, int offsetBars)
        {
            if (data == null || data.Count == 0) return System.DateTime.UtcNow;
            if (offsetBars <= 0) return data[^1].Date;

            System.TimeSpan step;
            if (data.Count >= 3)
            {
                // Sample up to 8 most-recent inter-bar deltas; take the median.
                int samples = System.Math.Min(8, data.Count - 1);
                var deltas = new System.TimeSpan[samples];
                for (int i = 0; i < samples; i++)
                {
                    int hi = data.Count - 1 - i;
                    deltas[i] = data[hi].Date - data[hi - 1].Date;
                }
                System.Array.Sort(deltas);
                step = deltas[samples / 2];
            }
            else
            {
                step = data[^1].Date - data[0].Date;
            }
            if (step.Ticks <= 0) step = System.TimeSpan.FromMinutes(1);

            return data[^1].Date + System.TimeSpan.FromTicks(step.Ticks * offsetBars);
        }

        // Pointer↔chart coordinate mapping lives in ChartMath so the hover crosshair,
        // click-select, and drawing placement all share one tested implementation.
        private static double MapYToPrice(double y, double height, double min, double max, bool isLog)
            => ChartMath.MapYToPrice(y, height, min, max, isLog);

        private static int MapXToIndex(double x, double width, int startIndex, int length)
            => ChartMath.MapXToIndex(x, width, startIndex, length);

        private void HandleDrawingStep(DateTime date, double price)
        {
            if (_pendingDrawingType == DrawingType.None) return;
            string label = FriendlyName(_pendingDrawingType);

            if (_anchorDate1 == null)
            {
                _anchorDate1 = date;
                _anchorPrice1 = price;

                if (_pendingDrawingType == DrawingType.AnchoredVwap)
                {
                    CompleteDrawing(date, price);
                }
                else
                {
                    string ts = date.ToString("t");
                    _eventBus.Publish(new AnnouncementEvent(
                        $"{label}: anchor 1 set at {SpeechPriceFormatter.FormatPrice(price)}, {ts}. Navigate to next point and press the shortcut again."));
                }
            }
            else if (_anchorDate2 == null)
            {
                bool needsDifferentBar = _pendingDrawingType == DrawingType.TrendLine ||
                                        _pendingDrawingType == DrawingType.Channel ||
                                        _pendingDrawingType == DrawingType.FibRetracement ||
                                        _pendingDrawingType == DrawingType.AndrewsPitchfork;

                if (needsDifferentBar && date == _anchorDate1)
                {
                    _eventBus.Publish(new AnnouncementEvent("Please select a different bar for the second point."));
                    return;
                }

                _anchorDate2 = date;
                _anchorPrice2 = price;

                bool isThreePoint = _pendingDrawingType == DrawingType.FibExtension ||
                                    _pendingDrawingType == DrawingType.RiskReward ||
                                    _pendingDrawingType == DrawingType.AndrewsPitchfork;

                if (!isThreePoint)
                {
                    CompleteDrawing(date, price);
                }
                else
                {
                    string ts = date.ToString("t");
                    string msg = _pendingDrawingType switch {
                        DrawingType.RiskReward       => $"Risk/reward: entry at {SpeechPriceFormatter.FormatPrice(price)}, {ts}. Navigate to stop loss and press the shortcut again.",
                        DrawingType.AndrewsPitchfork => $"Pitchfork: median line at {SpeechPriceFormatter.FormatPrice(price)}, {ts}. Navigate to swing point and press the shortcut again.",
                        _                            => $"{label}: anchor 2 at {SpeechPriceFormatter.FormatPrice(price)}, {ts}. Navigate to anchor 3 and press the shortcut again."
                    };
                    _eventBus.Publish(new AnnouncementEvent(msg));
                }
            }
            else
            {
                CompleteDrawing(date, price);
            }
        }

        private void CompleteDrawing(DateTime dateFinal, double priceFinal)
        {
            // If a live preview exists, it's already the committed drawing — just
            // finalise the anchor(s) we have and clear state. Otherwise fall back to
            // creating a fresh series (legacy click-click path for drawings that never
            // passed through MouseDown, e.g. Ctrl+Shift+T after keyboard navigation).
            if (_previewSeriesId != null)
            {
                var series = _store.State.ActiveSeries.FirstOrDefault(s => s.Id == _previewSeriesId);
                if (series?.Drawing != null)
                {
                    series.Drawing.AnchorDate3 = dateFinal;
                    series.Drawing.AnchorPrice3 = priceFinal;
                    var results = _drawingService.CalculateDrawingData(series.Drawing, _store.State.Data.ToList());
                    foreach (var kvp in results)
                    {
                        series.Data.ComponentData[kvp.Key] = kvp.Value;
                    }
                    _eventBus.Publish(new RedrawEvent());
                }
            }
            else
            {
                var d = new DrawingData
                {
                    Type = _pendingDrawingType,
                    AnchorDate1 = _anchorDate1,
                    AnchorPrice1 = _anchorPrice1,
                    AnchorDate2 = _anchorDate2,
                    AnchorPrice2 = _anchorPrice2,
                    AnchorDate3 = dateFinal,
                    AnchorPrice3 = priceFinal,
                    ExtendRight = true
                };
                CreateDrawingSeries(_pendingDrawingType.ToString(), d, _store.State.Data.ToList());
            }

            string label = FriendlyName(_pendingDrawingType);
            double fromPrice = _anchorPrice1 ?? priceFinal;
            string feedback = Math.Abs(priceFinal - fromPrice) > 0.001
                ? $"{label} placed from {SpeechPriceFormatter.FormatPrice(fromPrice)} to {SpeechPriceFormatter.FormatPrice(priceFinal)}."
                : $"{label} placed at {SpeechPriceFormatter.FormatPrice(priceFinal)}.";
            _eventBus.Publish(new AnnouncementEvent(feedback));

            _previewSeriesId = null;
            _pendingDrawingType = DrawingType.None;
            _anchorDate1 = null; _anchorPrice1 = null;
            _anchorDate2 = null; _anchorPrice2 = null;
        }

        public void HandleAddDrawing(string type, IReadOnlyList<Ohlcv> chartData)
        {
            if (chartData == null || !chartData.Any()) return;

            var state = _store.State;
            var pt = chartData[Math.Clamp(state.CurrentDataIndex, 0, chartData.Count - 1)];

            var dType = type switch
            {
                "Horizontal" => DrawingType.HorizontalLine,
                "Vertical" => DrawingType.VerticalLine,
                "TrendLine" => DrawingType.TrendLine,
                "Channel" => DrawingType.Channel,
                "FibRetracement" => DrawingType.FibRetracement,
                "FibExtension" => DrawingType.FibExtension,
                "Rectangle" => DrawingType.Rectangle,
                "GannFan" => DrawingType.GannFan,
                "RiskReward" => DrawingType.RiskReward,
                "AnchoredVwap" => DrawingType.AnchoredVwap,
                "Measure" => DrawingType.MeasureTool,
                "GannBox" => DrawingType.GannBox,
                "Pitchfork" => DrawingType.AndrewsPitchfork,
                "AngleFib" => DrawingType.AngleFib,
                "TextLabel" => DrawingType.TextLabel,
                _ => DrawingType.None
            };

            if (dType == DrawingType.HorizontalLine)
            {
                CreateDrawingSeries("Horizontal", new DrawingData { Type = DrawingType.HorizontalLine, AnchorPrice1 = pt.Close }, chartData);
                _eventBus.Publish(new AnnouncementEvent($"Horizontal line added at {SpeechPriceFormatter.FormatPrice(pt.Close)}"));
            }
            else if (dType == DrawingType.VerticalLine)
            {
                CreateDrawingSeries("Vertical", new DrawingData { Type = DrawingType.VerticalLine, AnchorDate1 = pt.Date }, chartData);
                _eventBus.Publish(new AnnouncementEvent($"Vertical line added at {pt.Date:MMMM dd, HH:mm}"));
            }
            else if (dType == DrawingType.TextLabel)
            {
                CreateDrawingSeries("Label", new DrawingData { Type = DrawingType.TextLabel, AnchorDate1 = pt.Date, AnchorPrice1 = pt.Close }, chartData);
                _eventBus.Publish(new AnnouncementEvent($"Text label pinned at {SpeechPriceFormatter.FormatPrice(pt.Close)}"));
            }
            else if (dType != DrawingType.None)
            {
                if (_pendingDrawingType != dType)
                {
                    // If a different drawing was in progress, cancel it silently before starting the new one.
                    if (_pendingDrawingType != DrawingType.None)
                        _eventBus.Publish(new AnnouncementEvent($"{FriendlyName(_pendingDrawingType)} cancelled."));

                    _pendingDrawingType = dType;
                    _anchorDate1 = null;
                    _anchorPrice1 = null;
                    _anchorDate2 = null;
                    _anchorPrice2 = null;
                }

                HandleDrawingStep(pt.Date, pt.Close);
            }
        }

        private static string FriendlyName(DrawingType t) => t switch
        {
            DrawingType.TrendLine        => "Trend line",
            DrawingType.HorizontalLine   => "Horizontal line",
            DrawingType.VerticalLine     => "Vertical line",
            DrawingType.Channel          => "Channel",
            DrawingType.FibRetracement   => "Fibonacci retracement",
            DrawingType.FibExtension     => "Fibonacci extension",
            DrawingType.Rectangle        => "Rectangle",
            DrawingType.GannFan          => "Gann fan",
            DrawingType.RiskReward       => "Risk/reward",
            DrawingType.AnchoredVwap     => "Anchored VWAP",
            DrawingType.MeasureTool      => "Measure",
            DrawingType.GannBox          => "Gann box",
            DrawingType.AndrewsPitchfork => "Andrews pitchfork",
            DrawingType.AngleFib         => "Angle Fibonacci",
            DrawingType.TextLabel        => "Text label",
            _                            => t.ToString()
        };

        private void CancelPendingDrawing()
        {
            if (_pendingDrawingType == DrawingType.None && _previewSeriesId == null) return;

            string label = FriendlyName(_pendingDrawingType);

            // Remove the preview series so the half-drawn line disappears from the chart.
            if (_previewSeriesId != null)
            {
                _store.Dispatch(new RemoveSeriesAction(_previewSeriesId));
                _previewSeriesId = null;
            }

            _pendingDrawingType = DrawingType.None;
            _anchorDate1 = null;
            _anchorPrice1 = null;
            _anchorDate2 = null;
            _anchorPrice2 = null;
            _isMouseDragging = false;
            _eventBus.Publish(new AnnouncementEvent($"{label} cancelled."));
            _eventBus.Publish(new RedrawEvent());
        }

        private string CreateDrawingSeries(string name, DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            string seriesId = Guid.NewGuid().ToString();
            var config = new SeriesConfig
            {
                Id = seriesId,
                Name = $"{name} ({_store.State.ActiveSeries.Count(x => x.IsDrawing) + 1})",
                FriendlyName = $"{name} Drawing",
                Pane = "Main"
            };

            var dataBuffer = new SeriesDataBuffer { SeriesId = seriesId };
            var drawingResults = _drawingService.CalculateDrawingData(drawing, chartData);
            
            foreach (var kvp in drawingResults)
            {
                var comp = _modelFactory.CreateComponentConfig(name, kvp.Key);
                config.Components.Add(comp);
                dataBuffer.ComponentData[comp.Name] = kvp.Value;
            }

            var series = new ChartSeries(config, dataBuffer)
            {
                Drawing = drawing
            };

            _store.Dispatch(new AddSeriesAction(series));
            _eventBus.Publish(new RedrawEvent());
            return seriesId;
        }

        public void Dispose()
        {
            _subs.Dispose();
            _inputService.MouseEvent -= HandleMouseEvent;
        }
    }
}
