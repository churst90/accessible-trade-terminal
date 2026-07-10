using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>One resolved hit: the component nearest the cursor.</summary>
    public sealed record ChartHit(
        string SeriesId,
        string SeriesName,
        int ComponentIndex,
        string ComponentName,
        double DistancePx);

    /// <summary>
    /// Phase B second pass: maps a cursor position to the chart component under
    /// it. Rather than instrumenting the renderer to record geometry, the hit is
    /// computed on demand from the same inputs the renderer draws from — pane
    /// bands (IPaneLayoutService divider fractions), pane value ranges
    /// (WorkspaceState.PaneRanges / ViewportRange) and the component data
    /// arrays — so no per-frame bookkeeping is added to the render path and the
    /// math stays consistent with ChartMath.
    ///
    /// Used by: click-to-focus (clicking near an indicator line moves keyboard
    /// focus to it, so speech follows what the user pointed at) and the chart
    /// context menu (right-click near a component opens directly on it). Both
    /// keep their no-precision-required fallbacks: a miss still selects the bar
    /// / opens the generic menu, so shaky pointing never fails outright.
    /// </summary>
    public static class ChartHitTester
    {
        /// <summary>Vertical grab distance. Slightly generous — indicator lines are ~2 px.</summary>
        public const double TolerancePx = 12.0;

        /// <param name="dividers">Rendered pane dividers (fractions of total height), from IPaneLayoutService.</param>
        /// <param name="axisHeightFraction">Bottom x-axis strip fraction, from IPaneLayoutService.</param>
        public static ChartHit? HitTest(
            WorkspaceState state,
            IReadOnlyList<(string BelowPaneName, float DividerFraction)> dividers,
            float axisHeightFraction,
            double x, double y, double width, double height,
            double tolerancePx = TolerancePx)
        {
            if (state.Data == null || state.Data.Count == 0 || width <= 0 || height <= 0) return null;

            int barIndex = ChartMath.MapXToIndex(x, width, state.ViewportStartIndex, state.ViewportLength);
            if (barIndex < 0 || barIndex >= state.Data.Count) return null;

            // ── Resolve the pane band under the cursor ──────────────────────
            // Bands stack: Main from 0 to the first divider; each divider's
            // BelowPaneName runs from that divider to the next (or to the top of
            // the x-axis strip). No dividers → Main owns the whole plot area.
            double plotBottomFrac = 1.0 - Math.Clamp(axisHeightFraction, 0f, 0.5f);
            double yFrac = y / height;
            if (yFrac < 0 || yFrac > plotBottomFrac) return null; // over the x-axis strip

            string paneName = "Main";
            double bandTopFrac = 0.0;
            double bandBottomFrac = plotBottomFrac;
            if (dividers != null)
            {
                foreach (var (belowPane, frac) in dividers)
                {
                    if (yFrac >= frac)
                    {
                        paneName = belowPane;
                        bandTopFrac = frac;
                    }
                    else
                    {
                        bandBottomFrac = Math.Min(bandBottomFrac, frac);
                        break;
                    }
                }
            }

            float paneTopPx = (float)(bandTopFrac * height);
            float paneBottomPx = (float)(bandBottomFrac * height);
            if (paneBottomPx <= paneTopPx) return null;

            bool isMain = string.Equals(paneName, "Main", StringComparison.OrdinalIgnoreCase);
            (double Min, double Max) range;
            if (isMain)
            {
                range = state.ViewportRange;
            }
            else if (!state.PaneRanges.TryGetValue(paneName, out range))
            {
                return null; // pane not rendered yet — nothing reliable to hit
            }
            bool isLog = isMain && state.IsLogScale;

            // ── Nearest visible component in that pane at that bar ───────────
            ChartHit? best = null;
            foreach (var series in state.ActiveSeries)
            {
                if (series.IsDrawing) continue; // drawings have their own anchor-handle interactions
                if (!series.IsVisible) continue;
                if (!string.Equals(series.Pane ?? "Main", paneName, StringComparison.OrdinalIgnoreCase)) continue;

                for (int ci = 0; ci < series.Components.Count; ci++)
                {
                    var comp = series.Components[ci];
                    var data = series.GetComponentData(comp.Name);
                    if (data == null || barIndex >= data.Length) continue;
                    double value = data[barIndex];
                    if (double.IsNaN(value) || double.IsInfinity(value)) continue;

                    float compY = ChartMath.MapY(value, paneTopPx, paneBottomPx, range.Min, range.Max, isLog);
                    double dist = Math.Abs(compY - y);
                    if (dist <= tolerancePx && (best == null || dist < best.DistancePx))
                    {
                        best = new ChartHit(series.Id, series.FriendlyName, ci,
                            string.IsNullOrEmpty(comp.DisplayName) ? comp.Name : comp.DisplayName, dist);
                    }
                }
            }
            return best;
        }
    }
}
