using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Core.Services.Rendering;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public class ChartRenderer : IDisposable
    {
        private readonly ThemeService _theme;
        private readonly IStylingService _styling;
        private readonly ILogger<ChartRenderer> _logger;
        private readonly IAppLogger _appLogger;
        private readonly IPaneLayoutService _paneLayout;
        private readonly SKPaint _textPaint = new SKPaint { IsAntialias = true };
        private readonly SKFont _textFont;
        private float _axisWidth = 60;
        private float _axisHeight = 30;

        private readonly List<IRenderLayer> _layers;
        private readonly ProfileRenderLayer _profileLayer;

        public ChartRenderer(ThemeService theme, IStylingService styling, IProfileService profileService, IPaneLayoutService paneLayout, ILogger<ChartRenderer> logger, IAppLogger appLogger)
        {
            _theme = theme;
            _styling = styling;
            _paneLayout = paneLayout;
            _logger = logger;
            _appLogger = appLogger;
            
            var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
            _textFont = new SKFont(typeface, _theme.Current.AxisFontSize);

            _layers = new List<IRenderLayer>
            {
                new BackgroundLayer(_theme),
                new DataLayer(_styling),
                new OverlayLayer(_theme)
            };

            _profileLayer = new ProfileRenderLayer(profileService, _theme, appLogger);
        }

        public void Render(SKCanvas canvas, int width, int height, IReadOnlyList<Ohlcv> data, IReadOnlyList<ChartSeries> seriesList, int cursorIndex, int viewportStart, int viewportLength, (double Min, double Max) viewportRange, IReadOnlyDictionary<string, (double Min, double Max)> paneRanges, bool isHeikinAshi = false, bool isLogScale = false, float density = 1.0f, ImmutableDictionary<string, float>? paneHeightRatios = null, int indicatorPaneScrollIndex = 0, int rightMarginBars = 20)
        {
            try
            {
                canvas.Clear(SKColors.Black);

                if (data == null || data.Count == 0 || seriesList == null || viewportLength <= 0)
                {
                    return;
                }

                _axisWidth = _theme.Current.AxisWidth * density;
                _axisHeight = _theme.Current.AxisHeight * density;
                _textFont.Size = _theme.Current.AxisFontSize * density;

                // rawVisibleData is always the untransformed OHLCV slice.
                // visibleData is HA-transformed when isHeikinAshi — used only for the main pane
                // (candle rendering). All indicator/volume panes receive rawVisibleData so that
                // PriceAction directional coloring (e.g. volume bars) always uses real close/open.
                //
                // RIGHT-MARGIN RULE (matches TradingView-style behavior):
                //   • At the live edge — reserve `rightMarginBars` empty slots on the right for
                //     trendline projections into the future.
                //   • Panning back into history — fill the whole viewport with data; no gap.
                // We detect "at live edge" by asking: does the last real data bar fall inside
                // (or past) the last effective slot? If so, we're at the edge and reserve the
                // margin. Otherwise the viewport is panned back and data fills all slots.
                int effectiveWindow = Math.Max(1, viewportLength - rightMarginBars);
                int lastEffectiveDataIdx = viewportStart + effectiveWindow - 1;
                bool atLiveEdge = (data.Count - 1) <= lastEffectiveDataIdx;
                int takeCount = atLiveEdge ? effectiveWindow : viewportLength;
                var rawVisibleData = data.Skip(viewportStart).Take(takeCount).ToList();
                if (!rawVisibleData.Any())
                {
                    canvas.Clear(_theme.Background);
                    return;
                }

                var visibleData = isHeikinAshi
                    ? ChartMath.CalculateHeikinAshi(rawVisibleData)
                    : rawVisibleData;
                _textPaint.Color = _theme.Current.AxisText;
                
                canvas.Clear(_theme.Background);

                var mainSeries = seriesList.Where(s => s.Pane == "Main" && s.IsVisible).ToList();
                var allIndicatorGroups = seriesList.Where(s => s.Pane != "Main" && s.IsVisible).GroupBy(s => s.Pane).ToList();
                // Apply scroll offset — skip hidden panes, clamp to valid range.
                int clampedScroll = Math.Clamp(indicatorPaneScrollIndex, 0, Math.Max(0, allIndicatorGroups.Count - 1));
                var indicatorSeries = allIndicatorGroups.Skip(clampedScroll).ToList();

                float totalPaneHeight = height - _axisHeight;
                if (totalPaneHeight <= 0) return;

                const float MinIndicatorPaneHeightPx = 80f;
                float minIndicatorPaneHeight = MinIndicatorPaneHeightPx * density;

                // Compute per-pane heights: use stored ratio when present, otherwise equal-weight split.
                // Equal-weight: totalHeight / (1 + numIndicatorPanes) — each pane gets the same vertical space.
                float equalShare = indicatorSeries.Count > 0
                    ? totalPaneHeight / (1f + indicatorSeries.Count)
                    : totalPaneHeight;

                float[] indHeights = new float[indicatorSeries.Count];
                float usedByIndicators = 0f;
                for (int pi = 0; pi < indicatorSeries.Count; pi++)
                {
                    string paneName = indicatorSeries[pi].Key;
                    float ph;
                    if (paneHeightRatios != null && paneHeightRatios.TryGetValue(paneName, out float ratio))
                        ph = Math.Max(ratio * totalPaneHeight, minIndicatorPaneHeight);
                    else
                        ph = Math.Max(equalShare, minIndicatorPaneHeight);
                    indHeights[pi] = ph;
                    usedByIndicators += ph;
                }

                float mainPaneHeight = indicatorSeries.Any()
                    ? Math.Max(totalPaneHeight - usedByIndicators, totalPaneHeight * 0.25f)
                    : totalPaneHeight;

                // Guard against overflow: when saved pane ratios are large and the 25% main-pane
                // floor kicks in, the sum of all pane heights can exceed totalPaneHeight, pushing
                // the last indicator pane (typically Cipher B) off-screen or into the X-axis strip.
                // Scale all indicator panes down proportionally so everything fits within the canvas.
                if (indicatorSeries.Any() && mainPaneHeight + usedByIndicators > totalPaneHeight)
                {
                    float available = totalPaneHeight - mainPaneHeight;
                    if (available > 0f && usedByIndicators > available)
                    {
                        float scale = available / usedByIndicators;
                        float crowdedMin = 30f * density; // tighter floor when many panes compete
                        for (int pi = 0; pi < indHeights.Length; pi++)
                            indHeights[pi] = Math.Max(indHeights[pi] * scale, crowdedMin);
                        usedByIndicators = indHeights.Sum();
                        // Re-evaluate main pane after rebalance (lower floor to 15% in crowded layouts).
                        mainPaneHeight = Math.Max(totalPaneHeight - usedByIndicators, totalPaneHeight * 0.15f);
                    }
                }
                float currentY = 0;

                double mainMin = viewportRange.Min;
                double mainMax = viewportRange.Max;

                var mainPaneRect = new SKRect(0, currentY, width - _axisWidth, currentY + mainPaneHeight);
                RenderPane(canvas, mainPaneRect, visibleData, mainSeries, cursorIndex - viewportStart, viewportStart, "Main", mainMin, mainMax, isLogScale, viewportLength, density);
                RenderYAxis(canvas, new SKRect(width - _axisWidth, currentY, width, currentY + mainPaneHeight), mainMin, mainMax, isLogScale, density);
                // Legend for main-pane indicator overlays (e.g. Cipher A, Cipher SR).
                // Exclude core series (candles, price line, volume) so they don't pollute the legend.
                var mainOverlaySeries = mainSeries
                    .Where(s => s.IndicatorCode?.ToUpperInvariant() is not ("CANDLES" or "PRICE" or "VOLUME" or "HEATMAP"))
                    .ToList();
                if (mainOverlaySeries.Count > 0)
                    RenderPaneLegend(canvas, mainPaneRect, mainOverlaySeries, density);
                currentY += mainPaneHeight;

                float itemWidthForAxis = (width - _axisWidth) / Math.Max(1, viewportLength);

                var indicatorPaneInfos = new List<(SKRect Rect, double Min, double Max, List<ChartSeries> Series)>();
                var dividers = new List<(string BelowPaneName, float DividerFraction)>();

                for (int pi = 0; pi < indicatorSeries.Count; pi++)
                {
                    var group = indicatorSeries[pi];
                    float indicatorPaneHeight = indHeights[pi];

                    // Record the divider that sits ABOVE this indicator pane.
                    dividers.Add((group.Key, currentY / height));

                    var paneRect = new SKRect(0, currentY, width - _axisWidth, currentY + indicatorPaneHeight);

                    double min = 0, max = 100;
                    if (paneRanges.TryGetValue(group.Key, out var range))
                    {
                        min = range.Min;
                        max = range.Max;
                    }

                    var paneSeriesList = group.ToList();
                    // Indicator panes always use raw (non-HA) data so PriceAction coloring (e.g. volume)
                    // reflects real open/close direction, not the HA-transformed direction.
                    // Pass allPaneRanges so sub-panes can look up their composite-keyed ranges.
                    RenderPane(canvas, paneRect, rawVisibleData, paneSeriesList, cursorIndex - viewportStart, viewportStart, group.Key, min, max, false, viewportLength, density, paneRanges);
                    RenderYAxis(canvas, new SKRect(width - _axisWidth, currentY, width, currentY + indicatorPaneHeight), min, max, false, density);
                    RenderPaneLegend(canvas, paneRect, paneSeriesList, density);
                    indicatorPaneInfos.Add((paneRect, min, max, paneSeriesList));
                    currentY += indicatorPaneHeight;
                }

                // Update shared layout service so ChartArea.razor can position drag handles.
                _paneLayout.Update(dividers, _axisHeight / height);

                // Separator lines: vertical between chart area and Y-axis column; horizontal above X-axis strip.
                using var sepPaint = new SKPaint { Color = _theme.Current.GridLine.WithAlpha(160), StrokeWidth = 1 * density, Style = SKPaintStyle.Stroke };
                canvas.DrawLine(width - _axisWidth, 0, width - _axisWidth, height - _axisHeight, sepPaint);
                canvas.DrawLine(0, height - _axisHeight, width - _axisWidth, height - _axisHeight, sepPaint);

                RenderXAxis(canvas, new SKRect(0, height - _axisHeight, width - _axisWidth, height), visibleData, itemWidthForAxis, density);
                RenderCrosshair(canvas, new SKRect(0, 0, width - _axisWidth, totalPaneHeight), visibleData, cursorIndex - viewportStart, mainMin, mainMax, isLogScale, itemWidthForAxis, density, mainPaneHeight, indicatorPaneInfos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChartRenderer.Render failed");
                _appLogger.LogError("ChartRenderer.Render failed", nameof(ChartRenderer), ex);
            }
        }

        private static bool IsHeatmapSeries(ChartSeries s) =>
            s.Components.Any(c => c.DisplayType == ComponentDisplayType.Heatmap);

        private void RenderPane(SKCanvas canvas, SKRect rect, List<Ohlcv> visibleData, List<ChartSeries> series, int localCursorIndex, int viewportStart, string paneName, double min, double max, bool isLogScale, int viewportLength, float density, IReadOnlyDictionary<string, (double Min, double Max)>? allPaneRanges = null)
        {
            if (viewportLength <= 0) return;

            int renderCount = Math.Max(1, viewportLength);
            float itemWidth = rect.Width / renderCount;

            var nonProfileSeries = series.Where(s => !s.IsProfile || IsHeatmapSeries(s)).ToList();
            var profileSeries    = series.Where(s =>  s.IsProfile && s.IsVisible && !IsHeatmapSeries(s)).ToList();

            // ── Detect sub-panes from component metadata ─────────────────────
            // Collect unique sub-pane names in declaration order.
            var subPaneInfo = new List<(string Name, float HeightRatio)>();
            var seenSubPanes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in series)
                foreach (var comp in s.Components)
                    if (!string.IsNullOrEmpty(comp.SubPaneName) && seenSubPanes.Add(comp.SubPaneName!))
                        subPaneInfo.Add((comp.SubPaneName!, Math.Clamp(comp.SubPaneHeightRatio ?? 0.22f, 0.05f, 0.40f)));

            float totalSubRatio   = Math.Min(subPaneInfo.Sum(sp => sp.HeightRatio), 0.70f);
            float mainAreaHeight  = subPaneInfo.Count > 0
                ? Math.Max(rect.Height * (1f - totalSubRatio), rect.Height * 0.30f)
                : rect.Height;

            // ── Main area pass ────────────────────────────────────────────────
            var mainRect         = new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + mainAreaHeight);
            var adjustedMainRect = new SKRect(rect.Left, mainRect.Top, rect.Right, mainRect.Bottom);
            var ctx = new RenderContext(canvas, adjustedMainRect, visibleData, viewportStart, viewportLength, min, max, isLogScale, itemWidth, density, paneName, localCursorIndex, _theme.Current);

            canvas.Save(); canvas.ClipRect(mainRect);
            foreach (var layer in _layers) layer.Render(ctx, nonProfileSeries);
            if (profileSeries.Any()) _profileLayer.Render(ctx, profileSeries);
            canvas.Restore();

            // ── Sub-pane passes ───────────────────────────────────────────────
            float subY = rect.Top + mainAreaHeight;
            foreach (var (spName, ratio) in subPaneInfo)
            {
                float spHeight = rect.Height * ratio;
                var spRect         = new SKRect(rect.Left, subY, rect.Right, subY + spHeight);
                var adjustedSpRect = new SKRect(rect.Left, spRect.Top, rect.Right, spRect.Bottom);

                string spKey    = $"{paneName}/{spName}";
                double spMin    = -1.0, spMax = 1.0;
                if (allPaneRanges != null && allPaneRanges.TryGetValue(spKey, out var spRange))
                {
                    spMin = spRange.Min;
                    spMax = spRange.Max;
                }

                var spCtx = new RenderContext(canvas, adjustedSpRect, visibleData, viewportStart, viewportLength,
                    spMin, spMax, isLogScale, itemWidth, density, paneName, localCursorIndex, _theme.Current, spName);

                canvas.Save(); canvas.ClipRect(spRect);
                // Subtle horizontal separator line at the top of the sub-pane strip
                using (var sepPaint = new SKPaint { Color = new SKColor(80, 80, 80, 200), StrokeWidth = 1 * density, Style = SKPaintStyle.Stroke })
                    canvas.DrawLine(spRect.Left, spRect.Top, spRect.Right, spRect.Top, sepPaint);
                foreach (var layer in _layers) layer.Render(spCtx, nonProfileSeries);
                canvas.Restore();

                subY += spHeight;
            }
        }

        private void RenderYAxis(SKCanvas canvas, SKRect rect, double min, double max, bool isLogScale, float density)
        {
            // Use fewer labels for small indicator panes to prevent crowding.
            double[] anchors = rect.Height < 100 * density
                ? new[] { 0.0, 0.5, 1.0 }
                : new[] { 0.0, 0.25, 0.5, 0.75, 1.0 };
            float minLabelSpacing = _textFont.Size + (4 * density);
            float lastLabelY = float.MaxValue;
            foreach (var a in anchors)
            {
                double val = min + (max - min) * a;
                float y = rect.Bottom - (float)(a * rect.Height);
                // Skip labels that are too close to the previous one.
                if (Math.Abs(y - lastLabelY) < minLabelSpacing) continue;
                lastLabelY = y;
                string label = FormatAxisValue(val, max - min);
                float lx = rect.Left + (3 * density);
                // Clamp so baseline never falls below the pane bottom or above the cap-height boundary.
                float textY = Math.Clamp(y + (4 * density),
                    rect.Top + _textFont.Size + (2 * density),
                    rect.Bottom - (3 * density));
                canvas.DrawText(label, lx, textY, SKTextAlign.Left, _textFont, _textPaint);
            }
        }

        // Range-aware axis label formatter. A flat F2/F4 choice collapses to
        // "0.0000" for assets whose visible range is tiny (e.g. early KAS ticks
        // around $0.00003). Pick decimal count from the range magnitude so
        // labels always carry ~2 significant digits beyond the range scale.
        private static string FormatAxisValue(double val, double range)
        {
            double absRange = Math.Abs(range);
            int decimals;
            if (absRange == 0 || double.IsNaN(absRange) || double.IsInfinity(absRange))
                decimals = 2;
            else
                decimals = Math.Clamp(2 - (int)Math.Floor(Math.Log10(absRange)), 2, 10);
            return val.ToString("F" + decimals);
        }

        private void RenderXAxis(SKCanvas canvas, SKRect rect, List<Ohlcv> visibleData, float itemWidth, float density)
        {
            if (!visibleData.Any()) return;
            int labelCount = 5;
            float step = rect.Width / labelCount;
            for (int i = 0; i <= labelCount; i++)
            {
                float x = rect.Left + (i * step);
                float barX = x - rect.Left;
                int dIdx = (int)(barX / Math.Max(itemWidth, 1f));
                dIdx = Math.Max(0, Math.Min(visibleData.Count - 1, dIdx));
                // Position baseline in the upper portion of the axis strip so text never clips the canvas edge.
                float textY = rect.Top + _textFont.Size + (6 * density);
                canvas.DrawText(visibleData[dIdx].Date.ToString("HH:mm"), x, textY, SKTextAlign.Left, _textFont, _textPaint);
            }
        }

        private void RenderCrosshair(SKCanvas canvas, SKRect area, List<Ohlcv> visibleData, int localIndex, double min, double max, bool isLogScale, float itemWidth, float density, float mainPaneHeight, List<(SKRect Rect, double Min, double Max, List<ChartSeries> Series)> indicatorPanes)
        {
            if (visibleData.Count == 0) return;
            // Upper-bound clamp: never draw the crosshair past the last real data bar,
            // even if the cursor temporarily points into the right-margin future-space.
            // The crosshair labels the bar under focus; that bar is always at
            // localIndex ∈ [0, visibleData.Count - 1].
            if (localIndex < 0) return;
            if (localIndex >= visibleData.Count) localIndex = visibleData.Count - 1;
            float cx = area.Left + (localIndex * itemWidth) + (itemWidth / 2);

            using var vPaint = new SKPaint { Color = SKColors.Gray.WithAlpha(150), StrokeWidth = 1 * density, Style = SKPaintStyle.Stroke };

            // Vertical crosshair spans full chart height (main + all indicator panes)
            canvas.DrawLine(cx, 0, cx, area.Bottom, vPaint);

            // Horizontal crosshair in main pane (price)
            float cy = ChartMath.MapY(visibleData[localIndex].Close, area.Top, area.Top + mainPaneHeight, min, max, isLogScale);
            canvas.DrawLine(area.Left, cy, area.Right, cy, vPaint);

            // Horizontal crosshair in each indicator pane at the cursor's indicator value
            using var indPaint = new SKPaint { Color = SKColors.Gray.WithAlpha(100), StrokeWidth = 1 * density, Style = SKPaintStyle.Stroke };
            using var labelBgPaint = new SKPaint { Color = new SKColor(40, 40, 40, 210), Style = SKPaintStyle.Fill };
            foreach (var (paneRect, paneMin, paneMax, paneSeries) in indicatorPanes)
            {
                // Find the first non-NaN component value at localIndex
                double? val = null;
                foreach (var s in paneSeries)
                {
                    foreach (var comp in s.Components)
                    {
                        var data = s.GetComponentData(comp.Name);
                        if (data != null && localIndex < data.Length && !double.IsNaN(data[localIndex]))
                        {
                            val = data[localIndex];
                            break;
                        }
                    }
                    if (val.HasValue) break;
                }

                if (!val.HasValue) continue;
                float iy = ChartMath.MapY(val.Value, paneRect.Top, paneRect.Bottom, paneMin, paneMax, false);
                canvas.DrawLine(paneRect.Left, iy, paneRect.Right, iy, indPaint);

                // Y-value label at the right edge of the pane (matches RenderYAxis style)
                string label = FormatAxisValue(val.Value, paneMax - paneMin);
                float labelW = _textFont.MeasureText(label);
                float labelH = _textFont.Size + (4 * density);
                float lx = paneRect.Right + (2 * density);
                float ly = iy - (labelH / 2);
                canvas.DrawRect(new SKRect(lx, ly, lx + labelW + (6 * density), ly + labelH), labelBgPaint);
                canvas.DrawText(label, lx + (3 * density), iy + (4 * density), SKTextAlign.Left, _textFont, _textPaint);
            }
        }

        /// <summary>
        /// Renders a small component legend in the top-left corner of an indicator pane.
        /// Shows a color swatch + name for each visible non-level component and cloud fill.
        /// </summary>
        private void RenderPaneLegend(SKCanvas canvas, SKRect paneRect, List<ChartSeries> paneSeries, float density)
        {
            const float SwatchPx   = 8f;
            const float PadPx      = 4f;
            const float LinePx     = 16f;
            const int   MaxEntries = 9;

            float swatch = SwatchPx * density;
            float pad    = PadPx   * density;
            float line   = LinePx  * density;

            var entries = new List<(SKColor Color, string Label)>();

            foreach (var s in paneSeries)
            {
                foreach (var comp in s.Components)
                {
                    if (!comp.IsVisible || comp.DisplayType == ComponentDisplayType.Level) continue;

                    // Directional bar/histogram components render green/red based on value direction,
                    // not the static ColorHex. Show the up-direction green so the swatch matches
                    // what is actually rendered, with a "↕" suffix to signal dynamic coloring.
                    SKColor displayColor;
                    string label = comp.DisplayName ?? comp.Name;
                    if (comp.DisplayType is ComponentDisplayType.Bar or ComponentDisplayType.Histogram)
                    {
                        displayColor = new SKColor(68, 187, 68, 200); // matches RenderDirectionalBars upPaint
                        // No suffix appended — the directional green swatch is sufficient indication.
                    }
                    else if (!SKColor.TryParse(comp.ColorHex, out displayColor))
                        continue;

                    entries.Add((displayColor, label));
                    if (entries.Count >= MaxEntries) break;
                }

                if (entries.Count >= MaxEntries) break;
            }

            if (entries.Count == 0) return;

            // Measure max label width
            float maxTextWidth = 0f;
            foreach (var (_, label) in entries)
                maxTextWidth = Math.Max(maxTextWidth, _textFont.MeasureText(label));

            float boxW = pad + swatch + pad + maxTextWidth + pad;
            float boxH = pad + entries.Count * line + pad;
            float bx   = paneRect.Left + pad * 2;
            float by   = paneRect.Top  + pad * 2;

            using var bgPaint = new SKPaint { Color = new SKColor(20, 20, 20, 180), Style = SKPaintStyle.Fill };
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(bx, by, bx + boxW, by + boxH), 3 * density), bgPaint);

            float ey = by + pad;
            foreach (var (color, label) in entries)
            {
                float sy = ey + (line - swatch) / 2f;
                using var swatchPaint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
                canvas.DrawRect(bx + pad, sy, bx + pad + swatch, sy + swatch, swatchPaint);

                float tx = bx + pad + swatch + pad;
                float ty = ey + line * 0.75f;
                canvas.DrawText(label, tx, ty, SKTextAlign.Left, _textFont, _textPaint);
                ey += line;
            }
        }

        public void Dispose() { _textPaint.Dispose(); _textFont.Dispose(); }
    }
}
