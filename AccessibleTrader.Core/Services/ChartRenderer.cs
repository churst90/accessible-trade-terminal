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

        // Cross-pane Anchor Polarity hand-off. Set at the top of <see cref="Render"/>
        // from the first CipherB-style series that exposes an "Anchor Polarity"
        // component and consumed inside <see cref="RenderPane"/> when the "Main"
        // pane renders. Using a per-frame field (not a layer parameter) avoids
        // threading the value through every IRenderLayer contract for a feature
        // that's only meaningful on the price pane.
        private double[]? _crossPaneAnchorPolarity;
        private double[]? _crossPaneTbdDistribution;

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

        public void Render(SKCanvas canvas, int width, int height, IReadOnlyList<Ohlcv> data, IReadOnlyList<ChartSeries> seriesList, int cursorIndex, int viewportStart, int viewportLength, (double Min, double Max) viewportRange, IReadOnlyDictionary<string, (double Min, double Max)> paneRanges, bool isHeikinAshi = false, bool isLogScale = false, float density = 1.0f, ImmutableDictionary<string, float>? paneHeightRatios = null, int indicatorPaneScrollIndex = 0, int rightMarginBars = 10)
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

                // Find the first visible series exposing an "Anchor Polarity" component
                // (Cipher B and friends). Cross-pane hand-off: the indicator is typically
                // in its own sub-pane; this lets the Main pane tint its background with
                // the HTF regime color so sighted users see bull/bear context while
                // looking at price, not only while looking at the oscillator.
                _crossPaneAnchorPolarity = seriesList
                    .Where(s => s.IsVisible)
                    .Select(s => s.GetComponentData("Anchor Polarity"))
                    .FirstOrDefault(d => d != null && d.Length > 0);

                // Cross-pane TBD distribution confidence tint. Sustained accumulator
                // (TopBottomDetectorProvider.CompDistribution) → sustained visual cue:
                // when distribution ≥ 0.5 the asset is statistically in a multi-bar
                // distribution phase. Painted as a faint red overlay on the Main pane
                // so traders see "topping conditions present" while looking at price,
                // not only while looking at the oscillator pane.
                _crossPaneTbdDistribution = seriesList
                    .Where(s => s.IsVisible)
                    .Select(s => s.GetComponentData("Distribution Confidence"))
                    .FirstOrDefault(d => d != null && d.Length > 0);

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
                var mainAxisRect = new SKRect(width - _axisWidth, currentY, width, currentY + mainPaneHeight);
                // The whole stacked-pane area, so a background gradient fades once across the
                // chart rather than restarting in every pane.
                var chartRect = new SKRect(0, mainPaneRect.Top, width - _axisWidth, height - _axisHeight);

                RenderPane(canvas, mainPaneRect, visibleData, mainSeries, cursorIndex - viewportStart, viewportStart, "Main", mainMin, mainMax, isLogScale, viewportLength, density, chartRect: chartRect);
                RenderYAxis(canvas, mainAxisRect, mainMin, mainMax, isLogScale, density);
                // Legend for main-pane indicator overlays (e.g. Cipher A, Cipher SR).
                // Exclude core series (candles, price line, volume) AND any volume-profile
                // series (VPVR / VPFR / TPO / any .IsProfile series) — those are their own
                // visual element rendered by ProfileRenderLayer, not an overlay that needs
                // a legend entry.
                var mainOverlaySeries = mainSeries
                    .Where(s => !s.IsProfile)
                    .Where(s => s.IndicatorCode?.ToUpperInvariant() is not ("CANDLES" or "PRICE" or "VOLUME" or "HEATMAP"))
                    .ToList();
                if (mainOverlaySeries.Count > 0)
                    RenderPaneLegend(canvas, mainPaneRect, mainOverlaySeries, density);
                // Small colored ticks on the Y-axis at each visible line indicator's
                // most-recent value (WT Fast / Slow / MF etc.). No-op if no line
                // components exist in the pane.
                RenderYAxisSwatches(canvas, mainAxisRect, mainOverlaySeries, mainMin, mainMax, isLogScale, density);
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
                    var indAxisRect = new SKRect(width - _axisWidth, currentY, width, currentY + indicatorPaneHeight);
                    RenderPane(canvas, paneRect, rawVisibleData, paneSeriesList, cursorIndex - viewportStart, viewportStart, group.Key, min, max, false, viewportLength, density, paneRanges, chartRect);
                    RenderYAxis(canvas, indAxisRect, min, max, false, density);
                    RenderPaneLegend(canvas, paneRect, paneSeriesList, density);
                    RenderYAxisSwatches(canvas, indAxisRect, paneSeriesList, min, max, false, density);
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

        private void RenderPane(SKCanvas canvas, SKRect rect, List<Ohlcv> visibleData, List<ChartSeries> series, int localCursorIndex, int viewportStart, string paneName, double min, double max, bool isLogScale, int viewportLength, float density, IReadOnlyDictionary<string, (double Min, double Max)>? allPaneRanges = null, SKRect? chartRect = null)
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
            var ctx = new RenderContext(canvas, adjustedMainRect, visibleData, viewportStart, viewportLength, min, max, isLogScale, itemWidth, density, paneName, localCursorIndex, _theme.Current, ChartRect: chartRect);

            canvas.Save(); canvas.ClipRect(mainRect);
            for (int li = 0; li < _layers.Count; li++)
            {
                _layers[li].Render(ctx, nonProfileSeries);
                // Anchor-regime tint is painted immediately after the BackgroundLayer
                // (index 0) so it sits *under* the candle/data/overlay layers but
                // *over* the pane fill. Only the "Main" pane takes the tint — the
                // indicator pane that owns the Anchor Polarity component already has
                // its own cloud fill (Cipher B "Anchor Fill").
                if (li == 0 && paneName == "Main" && _crossPaneAnchorPolarity != null)
                    RenderAnchorRegimeTint(ctx, _crossPaneAnchorPolarity);
                if (li == 0 && paneName == "Main" && _crossPaneTbdDistribution != null)
                    RenderTbdDistributionTint(ctx, _crossPaneTbdDistribution);
            }
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
                    spMin, spMax, isLogScale, itemWidth, density, paneName, localCursorIndex, _theme.Current, spName, chartRect);

                canvas.Save(); canvas.ClipRect(spRect);
                // Subtle horizontal separator line at the top of the sub-pane strip
                using (var sepPaint = new SKPaint { Color = new SKColor(80, 80, 80, 200), StrokeWidth = 1 * density, Style = SKPaintStyle.Stroke })
                    canvas.DrawLine(spRect.Left, spRect.Top, spRect.Right, spRect.Top, sepPaint);
                foreach (var layer in _layers) layer.Render(spCtx, nonProfileSeries);
                canvas.Restore();

                subY += spHeight;
            }
        }

        /// <summary>
        /// Paints a per-bar regime tint across the main pane's background using
        /// the cross-pane Anchor Polarity array. +1 bar = faint green, -1 bar =
        /// faint red, 0 / NaN = transparent. Kept at very low alpha so the tint
        /// whispers the HTF regime without drowning out candle colors — blind
        /// users still get it via <see cref="Rendering.DataLayer"/>'s
        /// sonification; this is the sighted-companion cue.
        /// </summary>
        private static void RenderAnchorRegimeTint(RenderContext ctx, double[] anchorPolarity)
        {
            const byte tintAlpha = 22; // whisper-quiet — adjust if overwhelming
            var bullColor = new SKColor(0x26, 0xA6, 0x9A, tintAlpha);  // teal
            var bearColor = new SKColor(0xEF, 0x53, 0x50, tintAlpha);  // soft red

            float barWidth = ctx.Width / ctx.ViewportLength;
            using var bullLease = new SKPaint { Color = bullColor, Style = SKPaintStyle.Fill, IsAntialias = false };
            using var bearLease = new SKPaint { Color = bearColor, Style = SKPaintStyle.Fill, IsAntialias = false };

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= anchorPolarity.Length) break;
                double pol = anchorPolarity[dataIdx];
                if (double.IsNaN(pol) || pol == 0) continue;

                float x = i * barWidth;
                var rect = new SKRect(x, ctx.Top, x + barWidth, ctx.Bottom);
                ctx.Canvas.DrawRect(rect, pol > 0 ? bullLease : bearLease);
            }
        }

        /// <summary>
        /// Paints a red tint on bars where TBD distribution confidence ≥ 0.5.
        /// The TBD distribution accumulator is a slow process — sustained values
        /// indicate multi-bar topping conditions, mirroring the architectural
        /// asymmetry that bottoms are events and tops are processes. Alpha scales
        /// with confidence (0.5→0.20×, 1.0→1.00× of the cap) so the visual cue
        /// strengthens as the distribution thesis builds.
        /// </summary>
        private static void RenderTbdDistributionTint(RenderContext ctx, double[] distribution)
        {
            const double Threshold = 0.5;
            const byte MaxTintAlpha = 32; // a touch heavier than Anchor since it's a proper signal, not just regime
            var baseColor = new SKColor(0xEF, 0x53, 0x50, 0); // soft red, alpha set per-bar

            float barWidth = ctx.Width / ctx.ViewportLength;
            using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= distribution.Length) break;
                double dist = distribution[dataIdx];
                if (double.IsNaN(dist) || dist < Threshold) continue;

                // Map [0.5, 1.0] → [0.2, 1.0] of MaxTintAlpha, clamped above 1.0.
                double scale = Math.Clamp((dist - Threshold) / (1.0 - Threshold), 0.0, 1.0);
                byte alpha = (byte)(MaxTintAlpha * (0.2 + 0.8 * scale));
                paint.Color = baseColor.WithAlpha(alpha);

                float x = i * barWidth;
                var rect = new SKRect(x, ctx.Top, x + barWidth, ctx.Bottom);
                ctx.Canvas.DrawRect(rect, paint);
            }
        }

        private void RenderYAxis(SKCanvas canvas, SKRect rect, double min, double max, bool isLogScale, float density)
        {
            // Round-number anchors. Old algorithm labeled at fixed fractions (0,
            // 0.25, 0.5, 0.75, 1.0) of the raw min/max, producing labels like
            // "76227.38" on a 64k-80k BTC view — accurate but useless; traders
            // read in round thousands. Now we pick a nice-number step with the
            // same algorithm BackgroundLayer uses for gridlines, then emit
            // labels at the step boundaries. Label positions align exactly with
            // major gridlines so the chart reads as a coherent grid, not a grid
            // + an unrelated label track.
            double range = max - min;
            if (range <= 0 || double.IsNaN(range) || double.IsInfinity(range)) return;

            // Pick target label density by pane height. Small indicator panes
            // (<100px tall) want ~3 labels; full-size panes want ~5.
            int targetLabelCount = rect.Height < 100 * density ? 3 : 5;
            double roughStep = range / targetLabelCount;
            double stepMag = Math.Pow(10, Math.Floor(Math.Log10(roughStep)));
            double stepFrac = roughStep / stepMag;
            double niceStep;
            if (stepFrac < 1.5) niceStep = 1 * stepMag;
            else if (stepFrac < 3.5) niceStep = 2 * stepMag;
            else if (stepFrac < 7.5) niceStep = 5 * stepMag;
            else niceStep = 10 * stepMag;

            float minLabelSpacing = _textFont.Size + (4 * density);
            float lastLabelY = float.MaxValue;

            double firstLine = Math.Ceiling(min / niceStep) * niceStep;
            int safety = 0;
            for (double v = firstLine; v <= max && safety < 200; v += niceStep, safety++)
            {
                float y = ChartMath.MapY(v, rect.Top, rect.Bottom, min, max, isLogScale);
                if (Math.Abs(y - lastLabelY) < minLabelSpacing) continue;
                lastLabelY = y;
                string label = FormatAxisValue(v, range);
                float lx = rect.Left + (3 * density);
                float textY = Math.Clamp(y + (4 * density),
                    rect.Top + _textFont.Size + (6 * density),
                    rect.Bottom - (3 * density));
                canvas.DrawText(label, lx, textY, SKTextAlign.Left, _textFont, _textPaint);
            }
        }

        /// <summary>
        /// Draws a small colored tick on the left edge of the Y-axis strip at
        /// the current live-bar Y position for every visible line/area component
        /// in the pane. Gives an at-a-glance read of "where does each indicator
        /// currently sit" without having to follow the line visually across a
        /// busy canvas.
        /// </summary>
        private void RenderYAxisSwatches(SKCanvas canvas, SKRect axisRect, List<ChartSeries> paneSeries, double min, double max, bool isLogScale, float density)
        {
            if (paneSeries == null || paneSeries.Count == 0) return;
            float tickW = 4 * density;
            float tickH = 3 * density;
            foreach (var s in paneSeries)
            {
                foreach (var comp in s.Components)
                {
                    if (!comp.IsVisible) continue;
                    if (comp.DisplayType is not (ComponentDisplayType.Line or ComponentDisplayType.Area)) continue;
                    var data = s.GetComponentData(comp.Name);
                    if (data == null || data.Length == 0) continue;
                    // Walk back from last bar until a non-NaN value is found — some
                    // indicators produce NaN tails inside their warm-up region.
                    double? v = null;
                    for (int i = data.Length - 1; i >= 0 && i > data.Length - 20; i--)
                    {
                        if (!double.IsNaN(data[i])) { v = data[i]; break; }
                    }
                    if (!v.HasValue) continue;
                    if (!SKColor.TryParse(comp.ColorHex, out var color)) continue;
                    float y = ChartMath.MapY(v.Value, axisRect.Top, axisRect.Bottom, min, max, isLogScale);
                    if (y < axisRect.Top || y > axisRect.Bottom) continue;
                    using var p = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
                    // SKCanvas.DrawRect's four-float overload is (x, y, WIDTH, HEIGHT), not
                    // (left, top, right, bottom). Passing bounds here made a 4x3px tick into a
                    // rect ~1900px wide and ~y tall — a solid colour block down the price axis
                    // that buried the axis labels behind it.
                    canvas.DrawRect(SKRect.Create(axisRect.Left, y - (tickH / 2f), tickW, tickH), p);
                }
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

            // Adaptive label format driven by the visible span. A 6h chart showing
            // "06:00 06:00 06:00 06:00 06:00" tells the user nothing — every bar
            // is at a multiple of 6h UTC. Pick a format based on the total span:
            //
            //   span < 2 days   → intraday, show "HH:mm" everywhere.
            //   2–60 days       → swing/daily view, show "MM/dd" at each label and
            //                     append " HH:mm" on the bar immediately after
            //                     midnight so time-of-day context isn't lost.
            //   > 60 days       → long-range, show "MMM d" (e.g. "Apr 24").
            //
            // Plus a date-boundary tick: when two adjacent labels straddle midnight
            // on a 2-60 day chart, the later one forces the date prefix so the user
            // always sees where one day ends and the next begins.
            var span = visibleData[^1].Date - visibleData[0].Date;
            string primaryFormat;
            bool markDateBoundaries;
            if (span.TotalDays < 2)
            {
                primaryFormat = "HH:mm";
                markDateBoundaries = true;   // still surface the date at midnight
            }
            else if (span.TotalDays < 60)
            {
                primaryFormat = "MM/dd";
                markDateBoundaries = false;
            }
            else
            {
                primaryFormat = "MMM d";
                markDateBoundaries = false;
            }

            int labelCount = 5;
            float step = rect.Width / labelCount;
            DateTime? prevLabelDate = null;
            for (int i = 0; i <= labelCount; i++)
            {
                float x = rect.Left + (i * step);
                float barX = x - rect.Left;
                int dIdx = (int)(barX / Math.Max(itemWidth, 1f));
                dIdx = Math.Max(0, Math.Min(visibleData.Count - 1, dIdx));
                var d = visibleData[dIdx].Date;

                string label = d.ToString(primaryFormat);
                if (markDateBoundaries && prevLabelDate.HasValue && d.Date != prevLabelDate.Value.Date)
                {
                    // Day rolled over between labels — lead with the date so the
                    // user sees the boundary.
                    label = d.ToString("MM/dd ") + label;
                }
                prevLabelDate = d;

                float textY = rect.Top + _textFont.Size + (6 * density);
                // Rightmost label right-aligns so it doesn't overshoot the axis
                // edge; all others left-align from the tick position. Prevents
                // the final label from clipping against the canvas edge at tight
                // viewport widths.
                SKTextAlign align = (i == labelCount) ? SKTextAlign.Right : SKTextAlign.Left;
                float textX = (i == labelCount) ? rect.Right - (2 * density) : x;
                canvas.DrawText(label, textX, textY, align, _textFont, _textPaint);
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

            // Halo underpaint: a wider, low-alpha line below the crisp crosshair
            // gives the pointer an actual readable contour against busy candles.
            using var haloPaint = new SKPaint { Color = new SKColor(255, 255, 255, 40), StrokeWidth = 5 * density, Style = SKPaintStyle.Stroke };
            using var vPaint    = new SKPaint { Color = SKColors.Gray.WithAlpha(170), StrokeWidth = 1 * density, Style = SKPaintStyle.Stroke };

            // Vertical crosshair spans full chart height (main + all indicator panes)
            canvas.DrawLine(cx, 0, cx, area.Bottom, haloPaint);
            canvas.DrawLine(cx, 0, cx, area.Bottom, vPaint);

            // Horizontal crosshair in main pane (price)
            float cy = ChartMath.MapY(visibleData[localIndex].Close, area.Top, area.Top + mainPaneHeight, min, max, isLogScale);
            canvas.DrawLine(area.Left, cy, area.Right, cy, haloPaint);
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
                canvas.DrawLine(paneRect.Left, iy, paneRect.Right, iy, haloPaint);
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
        /// Renders a small component legend in the top-left corner of a pane.
        ///
        /// <para>
        /// Three rules, all of them learned from one weekly BTC chart that carried Market Structure
        /// and Value Deviation at once:
        /// </para>
        /// <list type="number">
        /// <item>
        /// <b>Size against the pane, not a constant.</b> A fixed nine-row cap is 152px, which on a
        /// price pane sharing space with a volume pane covered a third of the plot and sat on top
        /// of the candles. The cap is now whatever fits in the top ~45% of the pane.
        /// </item>
        /// <item>
        /// <b>Rank before truncating.</b> Taking the first N in series order let one marker-heavy
        /// indicator spend the whole budget, so the candles, the moving average and the levels —
        /// the things a reader actually needs named — never appeared. Price and continuous lines
        /// now outrank markers.
        /// </item>
        /// <item>
        /// <b>Collapse marker families.</b> An indicator whose whole output is a graded set of
        /// marks (six Value Deviation tiers) gets ONE row naming the series and the count, rather
        /// than six rows of near-identical labels.
        /// </item>
        /// </list>
        ///
        /// <para>
        /// When rows still do not fit, the last row says how many were dropped. A legend that
        /// silently shows a subset reads as a complete list of what is on the chart, which is
        /// exactly the wrong thing for it to imply.
        /// </para>
        /// </summary>
        private void RenderPaneLegend(SKCanvas canvas, SKRect paneRect, List<ChartSeries> paneSeries, float density)
        {
            const float KeyPx  = 14f;   // width of the key column — a line stub or a glyph
            const float PadPx  = 6f;
            const float LinePx = 17f;

            float key  = KeyPx  * density;
            float pad  = PadPx  * density;
            float line = LinePx * density;

            var rows = BuildLegendRows(paneSeries, paneRect.Height, line, pad);
            if (rows.Count == 0) return;

            // Hard ceiling on width, independent of what anything calls itself. The row budget
            // already stops the legend growing DOWN into the chart; without this it just grew
            // ACROSS instead, which is the same problem rotated.
            float maxTextPx = Math.Max(60f * density, paneRect.Width * 0.26f);
            for (int i = 0; i < rows.Count; i++)
                rows[i] = rows[i] with { Label = Ellipsize(rows[i].Label, maxTextPx) };

            float maxTextWidth = 0f;
            foreach (var row in rows)
                maxTextWidth = Math.Max(maxTextWidth, _textFont.MeasureText(row.Label));

            float boxW = pad + key + pad + maxTextWidth + pad;
            float boxH = pad + rows.Count * line + pad;
            float bx   = paneRect.Left + pad;
            float by   = paneRect.Top  + pad;

            // Reads as an overlay rather than a dialog parked on the chart: near-opaque so text
            // stays legible over candles, but a hairline border instead of a heavy outline.
            var bgRound = new SKRoundRect(new SKRect(bx, by, bx + boxW, by + boxH), 5 * density);
            using (var bgPaint = new SKPaint { Color = new SKColor(16, 16, 20, 232), Style = SKPaintStyle.Fill })
                canvas.DrawRoundRect(bgRound, bgPaint);
            using (var borderPaint = new SKPaint { Color = new SKColor(255, 255, 255, 34), Style = SKPaintStyle.Stroke, StrokeWidth = 1 * density })
                canvas.DrawRoundRect(bgRound, borderPaint);

            float ey = by + pad;
            foreach (var row in rows)
            {
                DrawLegendKey(canvas, row, bx + pad, ey + line / 2f, key, density);
                canvas.DrawText(row.Label, bx + pad + key + pad, ey + line * 0.72f,
                                SKTextAlign.Left, _textFont, _textPaint);
                ey += line;
            }
        }

        /// <summary>
        /// Draws a row's key so it looks like the thing it stands for: a stroked stub (dashed when
        /// the series is dashed) for lines, the real glyph for a marker, a filled chip for bars and
        /// candles. A uniform coloured square for everything was the inconsistency — a dashed
        /// resistance line and a diamond marker had identical keys, so the legend told you the
        /// colour and nothing else.
        /// </summary>
        private void DrawLegendKey(SKCanvas canvas, LegendRow row, float x, float cy, float key, float density)
        {
            using var lease = SKPaintPool.Rent();
            var paint = lease.Paint;
            paint.IsAntialias = true;

            switch (row.Glyph)
            {
                case LegendGlyph.Line:
                    paint.Color = row.Colors[0];
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = Math.Max(2f * density, row.StrokeWidth * density);
                    paint.StrokeCap = SKStrokeCap.Round;
                    paint.PathEffect = row.Dash switch
                    {
                        DashStyle.Dash    => SKPathEffect.CreateDash(new[] { 4f * density, 3f * density }, 0),
                        DashStyle.Dot     => SKPathEffect.CreateDash(new[] { 1.5f * density, 3f * density }, 0),
                        DashStyle.DashDot => SKPathEffect.CreateDash(new[] { 5f * density, 2.5f * density, 1.5f * density, 2.5f * density }, 0),
                        _                 => null,
                    };
                    canvas.DrawLine(x, cy, x + key, cy, paint);
                    paint.PathEffect?.Dispose();
                    paint.PathEffect = null;
                    break;

                case LegendGlyph.Marker:
                    // Up to three chips, one per distinct colour in the family, so a collapsed row
                    // does not claim a six-colour indicator is one colour.
                    int n = Math.Min(row.Colors.Count, 3);
                    float slot = key / n;
                    float r = Math.Min(slot * 0.42f, 3.2f * density);
                    paint.Style = SKPaintStyle.Fill;
                    for (int i = 0; i < n; i++)
                    {
                        paint.Color = row.Colors[i];
                        DrawGlyph(canvas, row.MarkerShape, x + slot * i + slot / 2f, cy, r, paint);
                    }
                    break;

                default:
                    paint.Color = row.Colors[0];
                    paint.Style = SKPaintStyle.Fill;
                    float h = 7f * density;
                    canvas.DrawRect(SKRect.Create(x + (key - h) / 2f, cy - h / 2f, h, h), paint);
                    break;
            }
        }

        /// <summary>Miniature of a marker shape, so the key matches the chart at a glance.</summary>
        private static void DrawGlyph(SKCanvas canvas, ComponentDisplayType shape, float cx, float cy, float r, SKPaint paint)
        {
            switch (shape)
            {
                case ComponentDisplayType.TriangleUp:
                case ComponentDisplayType.Arrow:
                    using (var path = new SKPath())
                    {
                        path.MoveTo(cx, cy - r); path.LineTo(cx - r, cy + r); path.LineTo(cx + r, cy + r); path.Close();
                        canvas.DrawPath(path, paint);
                    }
                    break;
                case ComponentDisplayType.TriangleDown:
                    using (var path = new SKPath())
                    {
                        path.MoveTo(cx, cy + r); path.LineTo(cx - r, cy - r); path.LineTo(cx + r, cy - r); path.Close();
                        canvas.DrawPath(path, paint);
                    }
                    break;
                case ComponentDisplayType.Diamond:
                    using (var path = new SKPath())
                    {
                        path.MoveTo(cx, cy - r); path.LineTo(cx + r, cy); path.LineTo(cx, cy + r); path.LineTo(cx - r, cy); path.Close();
                        canvas.DrawPath(path, paint);
                    }
                    break;
                case ComponentDisplayType.Square:
                    canvas.DrawRect(SKRect.Create(cx - r, cy - r, r * 2f, r * 2f), paint);
                    break;
                case ComponentDisplayType.Cross:
                    using (var stroke = new SKPaint { Color = paint.Color, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1.4f, r * 0.6f), IsAntialias = true, StrokeCap = SKStrokeCap.Round })
                    {
                        canvas.DrawLine(cx - r, cy - r, cx + r, cy + r, stroke);
                        canvas.DrawLine(cx + r, cy - r, cx - r, cy + r, stroke);
                    }
                    break;
                default:
                    canvas.DrawCircle(cx, cy, r, paint);
                    break;
            }
        }

        /// <summary>Trims a label to fit a pixel budget, ending in an ellipsis when cut.</summary>
        private string Ellipsize(string label, float maxPx)
        {
            if (_textFont.MeasureText(label) <= maxPx) return label;

            for (int len = label.Length - 1; len > 0; len--)
            {
                string candidate = label[..len] + "…";
                if (_textFont.MeasureText(candidate) <= maxPx) return candidate;
            }
            return "…";
        }

        /// <summary>
        /// The indicator's name without its subtitle or its baked-in parameter values.
        ///
        /// <para>
        /// A series is named <c>"{metadata name} {each parameter value}"</c>, so the collapsed
        /// legend row read "Value Deviation (support / resistance zones) 240 5 2 2 1 — 6 marks"
        /// and stretched the legend box across a third of the chart width. The subtitle in
        /// parentheses and the trailing numbers are both noise in a one-line key.
        /// </para>
        ///
        /// <para>
        /// Trailing NUMERIC tokens only — a string parameter is usually the whole point of the
        /// name ("Funding Rate BTC-USDT-SWAP") and is kept.
        /// </para>
        /// </summary>
        internal static string ShortSeriesName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";

            string s = name;
            int paren = s.IndexOf(" (", StringComparison.Ordinal);
            if (paren > 0) s = s[..paren];

            var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            while (parts.Count > 1 && double.TryParse(parts[^1],
                       System.Globalization.NumberStyles.Any,
                       System.Globalization.CultureInfo.InvariantCulture, out _))
                parts.RemoveAt(parts.Count - 1);

            return parts.Count > 0 ? string.Join(' ', parts) : name.Trim();
        }

        /// <summary>How a legend row draws its key, matching how the series draws on the chart.</summary>
        internal enum LegendGlyph
        {
            /// <summary>A stroked stub, dashed to match the series.</summary>
            Line,
            /// <summary>The real marker shape, one chip per distinct colour in the family.</summary>
            Marker,
            /// <summary>A filled chip — candles, bars, histograms, areas.</summary>
            Fill,
        }

        /// <summary>
        /// One line of the legend. Carries enough to draw a key that looks like the thing it stands
        /// for: a uniform coloured square for every row meant a dashed resistance line and a diamond
        /// marker had identical keys, so the legend told you the colour and nothing else.
        /// </summary>
        /// <param name="Rank">0 = base data, 1 = continuous lines, 2 = markers, 3 = the overflow row.</param>
        /// <param name="SeriesName">Owning indicator, used to disambiguate colliding labels.</param>
        internal record LegendRow(
            string Label,
            LegendGlyph Glyph,
            IReadOnlyList<SKColor> Colors,
            int Rank,
            string SeriesName,
            ComponentDisplayType MarkerShape = ComponentDisplayType.Dot,
            DashStyle Dash = DashStyle.Solid,
            float StrokeWidth = 2f);

        /// <summary>
        /// Chooses which legend rows to show, in what order. Separated from drawing because this
        /// is the part that can be WRONG — a legend that names the wrong things, or quietly names
        /// only some of them, misleads without looking broken.
        ///
        /// <para>
        /// Rules, all learned from one weekly BTC chart carrying Market Structure and Value
        /// Deviation at once: size the row budget against the pane rather than a constant; rank
        /// base data → lines → markers before truncating, so one marker-heavy indicator cannot
        /// spend the whole budget and leave the candles unnamed; collapse a marker family to one
        /// row; disambiguate labels that would otherwise read identically; and say how many rows
        /// were dropped, because a legend showing a silent subset reads as a complete list of
        /// what is on the chart.
        /// </para>
        /// </summary>
        /// <param name="paneHeight">Pane height in device pixels; the row budget is derived from it.</param>
        /// <param name="line">Row height in device pixels.</param>
        /// <param name="pad">Box padding in device pixels.</param>
        internal static List<LegendRow> BuildLegendRows(
            List<ChartSeries> paneSeries, float paneHeight, float line, float pad)
        {
            // Absolute ceiling regardless of how tall the pane is — past this the legend stops
            // being a key and becomes a second chart.
            const int HardMaxEntries = 9;

            // Below this a legend row is worth less than the pixels it costs.
            const int MinEntries = 3;

            // Fraction of the pane the legend may occupy before it competes with the data.
            const float MaxPaneFraction = 0.45f;

            // A series contributing at least this many marker components collapses to one row.
            const int CollapseMarkersAt = 3;

            var entries = new List<LegendRow>();
            if (paneSeries == null) return entries;

            foreach (var s in paneSeries)
            {
                string shortName = ShortSeriesName(s.Name);

                // Index of this series' first row, so the collapse below can only ever remove rows
                // this series added. Matching on colour alone would let one indicator's collapse
                // delete an earlier indicator's marker row whenever two colours happened to agree.
                int seriesStart = entries.Count;
                var markerColors = new List<SKColor>();
                var markerShape = ComponentDisplayType.Dot;

                foreach (var comp in s.Components)
                {
                    if (!comp.IsVisible || comp.DisplayType == ComponentDisplayType.Level) continue;

                    // Directional bar/histogram components render green/red by value direction, not
                    // the static ColorHex. Show the up-direction green so the key matches the chart.
                    SKColor color;
                    string label = comp.DisplayName ?? comp.Name;
                    if (comp.DisplayType is ComponentDisplayType.Bar or ComponentDisplayType.Histogram)
                        color = new SKColor(68, 187, 68, 200); // matches RenderDirectionalBars upPaint
                    else if (!SKColor.TryParse(comp.ColorHex, out color))
                        continue;

                    if (IsMarker(comp.DisplayType))
                    {
                        if (markerColors.Count == 0) markerShape = comp.DisplayType;
                        markerColors.Add(color);
                        entries.Add(new LegendRow(label, LegendGlyph.Marker, new[] { color },
                            Rank: 2, SeriesName: shortName, MarkerShape: comp.DisplayType));
                    }
                    else if (IsBaseData(comp.DisplayType))
                    {
                        entries.Add(new LegendRow(label, LegendGlyph.Fill, new[] { color },
                            Rank: 0, SeriesName: shortName));
                    }
                    else
                    {
                        entries.Add(new LegendRow(label, LegendGlyph.Line, new[] { color },
                            Rank: 1, SeriesName: shortName,
                            Dash: comp.DashStyle, StrokeWidth: comp.Thickness));
                    }
                }

                // Collapse this series' marker rows into one. Per series, so an indicator with only
                // one or two marks keeps their real names. The key carries up to three of the
                // family's distinct colours rather than pretending six marks are one colour.
                if (markerColors.Count >= CollapseMarkersAt)
                {
                    for (int k = entries.Count - 1; k >= seriesStart; k--)
                        if (entries[k].Rank == 2) entries.RemoveAt(k);

                    entries.Add(new LegendRow($"{shortName} — {markerColors.Count} marks",
                        LegendGlyph.Marker, markerColors.Distinct().ToList(),
                        Rank: 2, SeriesName: shortName, MarkerShape: markerShape));
                }
            }

            if (entries.Count == 0) return entries;

            // Two indicators can both call a component "Resistance". Side by side those rows read
            // as a duplicate rather than as two different lines, so the owning indicator is named.
            var collisions = entries.GroupBy(e => e.Label).Where(g => g.Count() > 1)
                                    .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
                if (collisions.Contains(entries[i].Label) && !string.IsNullOrEmpty(entries[i].SeriesName))
                    entries[i] = entries[i] with { Label = $"{entries[i].SeriesName} {entries[i].Label}" };

            // How many rows actually fit in the space the legend is allowed to have.
            int fits = (int)((paneHeight * MaxPaneFraction - pad * 2) / Math.Max(line, 1f));
            int maxEntries = Math.Clamp(fits, MinEntries, HardMaxEntries);

            // OrderBy is a documented stable sort, so within a rank the series order the user
            // built is preserved.
            var shown = entries.OrderBy(e => e.Rank).ToList();
            int dropped = 0;
            if (shown.Count > maxEntries)
            {
                // One row is spent saying so, which is worth more than one more colour key:
                // a legend showing a silent subset reads as a complete list of what is on the chart.
                dropped = shown.Count - (maxEntries - 1);
                shown = shown.Take(maxEntries - 1).ToList();
            }

            if (dropped > 0)
                shown.Add(new LegendRow($"+{dropped} more (see the object tree)", LegendGlyph.Fill,
                    new[] { new SKColor(150, 150, 158) }, Rank: 3, SeriesName: ""));

            return shown;
        }

        /// <summary>Discrete per-bar glyphs — the components that identify themselves by shape.</summary>
        internal static bool IsMarker(ComponentDisplayType t) => t is
            ComponentDisplayType.Dot or ComponentDisplayType.ZeroDot or ComponentDisplayType.GradientDot or
            ComponentDisplayType.Diamond or ComponentDisplayType.Square or ComponentDisplayType.Cross or
            ComponentDisplayType.TriangleUp or ComponentDisplayType.TriangleDown or ComponentDisplayType.Arrow;

        /// <summary>The chart's underlying data rather than something drawn over it.</summary>
        internal static bool IsBaseData(ComponentDisplayType t) => t is
            ComponentDisplayType.Candle or ComponentDisplayType.Wick or
            ComponentDisplayType.Bar or ComponentDisplayType.Histogram;

        public void Dispose() { _textPaint.Dispose(); _textFont.Dispose(); }
    }
}
