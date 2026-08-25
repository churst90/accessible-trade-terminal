using System.Collections.Immutable;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Workspace.Reducers
{
    /// <summary>
    /// Reduces viewport / navigation actions: cursor position, pan/zoom,
    /// Home/End, live-edge jump, granularity, and the fresh-data sync that
    /// runs on every <see cref="UpdateDataAction"/>.
    ///
    /// All state computation is pure apart from the <see cref="IViewportNavigationService"/>
    /// calls, which own the pan/zoom arithmetic and viewport clamping.
    /// </summary>
    internal static class ViewportReducer
    {
        public static WorkspaceState Reduce(
            WorkspaceState state,
            WorkspaceAction action,
            IViewportNavigationService navService) => action switch
        {
            UpdateDataAction a          => UpdateData(state, a.NewData, a.IsInitialLoad, navService),
            NavigateAction a            => navService.Navigate(state, a.NewIndex),
            NavigateRelativeAction a    => navService.Navigate(state, state.CurrentDataIndex + a.Delta),
            SetCursorAction a           => CursorOnlyJump(state, a.NewIndex),
            PanAction a                 => navService.Pan(state, a.Delta),
            ZoomAction a                => navService.ClampViewportToData(
                                              state with { ViewportLength = Math.Clamp(a.NewLength, state.RightMarginBars + 10, 5000) }),
            WheelZoomAction a           => WheelZoom(state, a, navService),
            WorkspacePanEvent a         => navService.Pan(state,
                                              Math.Max(1, (int)Math.Round(state.ViewportLength * state.PanningGranularity / 100.0)) * a.Direction),
            WorkspaceZoomEvent a        => navService.Zoom(state, a.Direction),
            JumpToLatestAction          => state with
            {
                CurrentDataIndex = state.Data.Count - 1,
                ViewportStartIndex = Math.Max(0, state.Data.Count - (state.ViewportLength - state.RightMarginBars))
            },
            AdjustGranularityAction a   => state with
            {
                PanningGranularity = SnapToStep5(Math.Clamp(state.PanningGranularity + a.Delta, 5, 100))
            },
            _ => state
        };

        /// <summary>
        /// Handles live bar appends / intra-bar updates / historical prepends from
        /// an <see cref="UpdateDataAction"/>. Preserves the user's focus and only
        /// advances the viewport when it was already at the live edge.
        /// </summary>
        private static WorkspaceState UpdateData(
            WorkspaceState state,
            TimeSeriesBuffer<Ohlcv> list,
            bool initial,
            IViewportNavigationService navService)
        {
            int newIdx = state.CurrentDataIndex;
            int newStart = state.ViewportStartIndex;

            // Effective data slots available in the viewport (total slots minus right margin).
            int effectiveWindow = Math.Max(1, state.ViewportLength - state.RightMarginBars);

            if (initial || newIdx == -1)
            {
                // Fresh load: show the most recent bars with cursor on the live edge.
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
            else
            {
                // APPEND or intra-bar update. Preserve user focus — do NOT move the
                // cursor based on live data arrivals; only an explicit JumpToLatest
                // command should do that. Advance the viewport only if it was already
                // showing the live edge (so live watchers keep up), never otherwise.
                int prevLastVisible     = state.ViewportStartIndex + effectiveWindow - 1;
                bool viewportWasAtLive  = prevLastVisible >= state.Data.Count - 1;
                bool isAppend           = list.Count > state.Data.Count;

                if (viewportWasAtLive && isAppend)
                    newStart = Math.Max(0, list.Count - effectiveWindow);

                // list.Count == 0 would make this clamp throw (min 0 > max -1). No caller
                // dispatches an empty update today, but the cost of being wrong about that is
                // an exception inside Dispatch.
                newIdx = list.Count == 0 ? 0 : Math.Clamp(newIdx, 0, list.Count - 1);
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
            var syncedSeries = SyncMappedComponentData(updated.ActiveSeries, list, initial);
            if (syncedSeries != updated.ActiveSeries)
                updated = updated with { ActiveSeries = syncedSeries };

            // CRITICAL: Clamp viewport length after every data update.
            return navService.ClampViewportToData(updated);
        }

        private static ImmutableList<ChartSeries> SyncMappedComponentData(
            ImmutableList<ChartSeries> series,
            TimeSeriesBuffer<Ohlcv> list,
            bool initial)
        {
            static double ExtractValue(Ohlcv bar, string mapping) => mapping.ToLower() switch
            {
                "open"   => (double)bar.Open,
                "high"   => (double)bar.High,
                "low"    => (double)bar.Low,
                "close"  => (double)bar.Close,
                "volume" => (double)bar.Volume,
                _        => double.NaN
            };

            return series.Select(s =>
            {
                bool needsSync = s.Components.Any(c => !string.IsNullOrEmpty(c.DataMapping));
                if (!needsSync) return s;

                SeriesDataBuffer? updatedBuffer = null;

                foreach (var c in s.Components)
                {
                    if (string.IsNullOrEmpty(c.DataMapping)) continue;

                    var currentData = s.GetComponentData(c.Name);
                    if (currentData.Length != list.Count)
                    {
                        updatedBuffer ??= s.Data.Clone();

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
                        updatedBuffer ??= s.Data.Clone();
                        var arr = (double[])currentData.Clone();
                        arr[^1] = ExtractValue(list[^1], c.DataMapping);
                        updatedBuffer.ComponentData[c.Name] = arr;
                    }
                }

                return updatedBuffer != null ? s.WithData(updatedBuffer) : s;
            }).ToImmutableList();
        }

        /// <summary>
        /// Home/End semantics: move the cursor within the currently visible data, never
        /// scroll. Clamps target into <c>[ViewportStartIndex, ViewportStartIndex + visibleCount - 1]</c>
        /// and further clamps to the last real data index, so the cursor can never land
        /// in the right-margin future-space or past the end of data.
        /// </summary>
        private static WorkspaceState CursorOnlyJump(WorkspaceState state, int target)
        {
            if (state.Data == null || state.Data.Count == 0) return state;

            int effectiveWindow      = Math.Max(1, state.ViewportLength - state.RightMarginBars);
            int barsAvailableToRight = Math.Max(0, state.Data.Count - state.ViewportStartIndex);
            bool atLiveEdge          = barsAvailableToRight <= effectiveWindow;
            int visibleCount         = atLiveEdge ? effectiveWindow : state.ViewportLength;

            int maxVisibleIdx = state.ViewportStartIndex + visibleCount - 1;
            int dataMax       = state.Data.Count - 1;
            int rightLimit    = Math.Min(maxVisibleIdx, dataMax);

            int clamped = Math.Clamp(target, state.ViewportStartIndex, rightLimit);
            if (clamped == state.CurrentDataIndex) return state;
            return state with { CurrentDataIndex = clamped };
        }

        private static int SnapToStep5(int value)
        {
            int snapped = (int)Math.Round(value / 5.0) * 5;
            return Math.Max(5, snapped);
        }

        /// <summary>
        /// Scroll-wheel zoom centred on a cursor fraction. Computes the absolute bar
        /// index under the cursor BEFORE zoom, applies a 10% multiplicative length
        /// change, then repositions ViewportStartIndex so that same absolute bar remains
        /// at the same screen fraction. Direction +1 = shrink (zoom in); -1 = grow.
        /// </summary>
        private static WorkspaceState WheelZoom(
            WorkspaceState state,
            WheelZoomAction a,
            IViewportNavigationService navService)
        {
            double frac = Math.Clamp(a.AnchorFraction, 0.0, 1.0);
            double anchorBar = state.ViewportStartIndex + frac * state.ViewportLength;

            // 10% per wheel notch matches the feel of TradingView / MT5. Multiplicative
            // so repeated scrolls don't slow down as the viewport shrinks.
            double factor = a.Direction > 0 ? 1.0 / 1.10 : 1.10;
            int newLength = Math.Clamp(
                (int)Math.Round(state.ViewportLength * factor),
                state.RightMarginBars + 10,
                5000);

            int newStart = (int)Math.Round(anchorBar - frac * newLength);
            if (newStart < 0) newStart = 0;

            var zoomed = state with
            {
                ViewportLength = newLength,
                ViewportStartIndex = newStart,
            };
            return navService.ClampViewportToData(zoomed);
        }
    }
}
