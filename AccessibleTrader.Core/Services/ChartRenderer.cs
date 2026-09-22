using AccessibleTrader.Sdk.Theming;
using SkiaSharp;
using System.Collections.Immutable;
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

        /// <summary>
        /// Chart formations, supplied per frame by the caller rather than detected here.
        ///
        /// <para>
        /// The renderer only ever sees the VISIBLE slice of bars, and detecting on a moving window
        /// would produce different shapes at every scroll position — a formation that appears and
        /// disappears as you pan is worse than no drawing at all. The caller holds the whole series
        /// and the cache, so it passes the answer in.
        /// </para>
        /// </summary>
        private readonly ChartFormationLayer _formationLayer = new();
        private IReadOnlyList<Analysis.ChartPattern>? _frameFormations;

        // Cross-pane Anchor Polarity hand-off. Set at the top of <see cref="Render"/>
        // from the first CipherB-style series that exposes an "Anchor Polarity"
        // component and consumed inside <see cref="RenderPane"/> when the "Main"
        // pane renders. Using a per-frame field (not a layer parameter) avoids
        // threading the value through every IRenderLayer contract for a feature
        // that's only meaningful on the price pane.
        private double[]? _crossPaneAnchorPolarity;
        private double[]? _crossPaneTbdDistribution;

        /// <summary>
        /// One axis typeface for the process.
        ///
        /// <para>
        /// It used to be constructed per renderer and never disposed, and the hosted head builds a
        /// renderer per Blazor circuit — so every browser connection leaked a native SkTypeface for
        /// the life of the process. It cannot be disposed here either: <see cref="Dispose"/> is
        /// deliberately empty because an in-flight frame can still be holding the font (see the
        /// long note on that method), and the same reasoning applies to the face the font is built
        /// from. A typeface is immutable and shareable, which is what makes "one for the process"
        /// both correct and the answer to the leak.
        /// </para>
        /// </summary>
        private static readonly SKTypeface AxisTypeface =
            SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.Default;

        public ChartRenderer(ThemeService theme, IStylingService styling, IPaneLayoutService paneLayout, ILogger<ChartRenderer> logger, IAppLogger appLogger)
        {
            _theme = theme;
            _styling = styling;
            _paneLayout = paneLayout;
            _logger = logger;
            _appLogger = appLogger;
            
            _textFont = new SKFont(AxisTypeface, _theme.Current.AxisFontSize);

            _layers = new List<IRenderLayer>
            {
                new BackgroundLayer(_theme),
                new DataLayer(_styling),
                new OverlayLayer()
            };

            _profileLayer = new ProfileRenderLayer();
        }


        /// <summary>How much taller the price pane is than one indicator pane when nothing has
        /// been resized by hand.
        ///
        /// <para>
        /// Until 2026-09-21 this was effectively 1: the split was
        /// <c>totalPaneHeight / (1 + indicatorCount)</c> with the comment "each pane gets the
        /// same vertical space", so a chart with Volume and nothing else gave the CANDLES half
        /// the window and the volume bars the other half. Measured on a real screenshot from the
        /// Windows head: price 171px, volume 171px. No charting convention does that, and the
        /// price pane is the one carrying the candles, the price line and every overlay.
        /// </para>
        ///
        /// <para>
        /// <b>2, not 3, and the floors are why.</b> At weight 3 a single Volume pane lands on
        /// 86px against its own 80px minimum — technically legal and visibly cramped. At 2 the
        /// price pane takes two thirds with one indicator and a half with two, and no indicator
        /// pane is pushed below its floor until three are open, which is where the floor governs
        /// anyway. Since 2026-09-22 the weight is consulted in the CROWDED path too: when the
        /// floors do not fit, the price pane keeps 2 of (2 + count) rather than dropping to the
        /// 25% floor, so a deep stack still shows the price pane as the largest.
        /// </para>
        /// </summary>
        internal const float DefaultMainPaneWeight = 2f;

        /// <summary>The heights a stack of panes gets, given the space and how many there are.</summary>
        internal readonly record struct PaneAllocation(float MainHeight, float[] IndicatorHeights);

        /// <summary>
        /// <b>Every pane's height, as a pure function.</b> Extracted from <c>Render</c> on
        /// 2026-09-21 so it could be tested: the three-stage negotiation below (weighted share,
        /// rebalance, final fit) had accumulated two bug-fix comments and zero tests, and the
        /// defects those comments describe — a pane drawn under the x-axis strip, a pane pushed
        /// off the canvas entirely — are invisible to everything except a screenshot.
        /// </summary>
        internal static PaneAllocation AllocatePaneHeights(
            float totalPaneHeight,
            IReadOnlyList<string> indicatorPaneNames,
            IReadOnlyDictionary<string, float>? paneHeightRatios,
            float density,
            float mainPaneWeight = DefaultMainPaneWeight)
        {
            // 60, down from 80 on 2026-09-22. A 60 CSS px oscillator pane is legible — the
            // screenshot probe's RSI at 65px reads fine — and 80 was the number that flattened
            // Cody's maximised window: three indicator panes at 80 needed 240 of the ~279 CSS px
            // the chart had, so the crowded path below handed every pane an equal quarter and
            // the price pane's 2:1 weight never reached the screen. The crowded floor is 30.
            const float MinIndicatorPaneHeightPx = 60f;
            float minIndicatorPaneHeight = MinIndicatorPaneHeightPx * density;
            int count = indicatorPaneNames.Count;

            if (count == 0) return new PaneAllocation(totalPaneHeight, Array.Empty<float>());

            // A WEIGHTED share: the price pane counts as mainPaneWeight panes, each indicator as
            // one. A hand-saved ratio still wins outright — the user resized it on purpose.
            float share = totalPaneHeight / (mainPaneWeight + count);

            float[] indHeights = new float[count];
            float usedByIndicators = 0f;
            for (int pi = 0; pi < count; pi++)
            {
                float ph = paneHeightRatios != null && paneHeightRatios.TryGetValue(indicatorPaneNames[pi], out float ratio)
                    ? Math.Max(ratio * totalPaneHeight, minIndicatorPaneHeight)
                    : Math.Max(share, minIndicatorPaneHeight);
                indHeights[pi] = ph;
                usedByIndicators += ph;
            }

            float mainPaneHeight = Math.Max(totalPaneHeight - usedByIndicators, totalPaneHeight * 0.25f);

            // CROWDED: the indicator floors plus the price pane's 25% floor do not fit. Until
            // 2026-09-22 this branch scaled the indicators into whatever was left of a main pane
            // pinned at exactly 25%, so the WEIGHT WAS DISCARDED at the moment it mattered most —
            // with three indicator panes on a maximised window the price pane landed on the same
            // height as each indicator, and a screenshot of it read as "the weight is broken".
            // It was not; it was never consulted here. Now the price pane keeps its weighted
            // share (2 of 2+count: 40% with three panes, 25% with six), floored at 15% when the
            // stack is very deep, and the indicators share the rest in proportion to what they
            // had, each no smaller than the crowded floor. Saved pane ratios are scaled like the
            // rest — the user resized the pane relative to a canvas that has since shrunk.
            // Overflow after the crowded floor engages is the FINAL FIT's job, below.
            if (mainPaneHeight + usedByIndicators > totalPaneHeight)
            {
                float weighted = totalPaneHeight * mainPaneWeight / (mainPaneWeight + count);
                mainPaneHeight = Math.Max(weighted, totalPaneHeight * 0.15f);
                float available = totalPaneHeight - mainPaneHeight;
                if (available > 0f && usedByIndicators > available)
                {
                    float scale = available / usedByIndicators;
                    float crowdedMin = 30f * density; // tighter floor when many panes compete
                    for (int pi = 0; pi < indHeights.Length; pi++)
                        indHeights[pi] = Math.Max(indHeights[pi] * scale, crowdedMin);
                    usedByIndicators = indHeights.Sum();
                }
            }

            // FINAL FIT, and it is not redundant with the rebalance above.
            //
            // That block hands the indicator panes whatever the main pane is not using, then
            // re-raises the main pane to its 15% floor — WITHOUT re-checking that everything
            // still fits. When the floor engages, it takes its share from a total that had
            // already been spent, and the sum exceeds the canvas by exactly that much. The
            // renderer lays panes out top-down by accumulating heights, so the whole overflow
            // lands on the LAST indicator pane, which is drawn underneath the x-axis strip.
            //
            // Nine panes on a 300px canvas is the demonstration: main 42 + eight at the 30px
            // crowded floor is 282 against 280 available, and the bottom pane loses the
            // difference plus the strip's own height — about half of it. That is not exotic
            // any more: since every oscillator got a pane of its own (2026-09-11) a chart with
            // eight indicators is an ordinary chart, and Alt+PageDown walks to a pane that is
            // not on the screen.
            //
            // The main pane is at its floor by this point, so the indicators are what gives.
            // No floor on this pass: at some density the panes genuinely do not fit, and the
            // honest answer is every pane equally small, not one pane invisible.
            if (mainPaneHeight + usedByIndicators > totalPaneHeight)
            {
                float excess = mainPaneHeight + usedByIndicators - totalPaneHeight;
                if (usedByIndicators > excess)
                {
                    float fit = (usedByIndicators - excess) / usedByIndicators;
                    for (int pi = 0; pi < indHeights.Length; pi++) indHeights[pi] *= fit;
                }
            }

            return new PaneAllocation(mainPaneHeight, indHeights);
        }

        public void Render(SKCanvas canvas, int width, int height, IReadOnlyList<Ohlcv> data, IReadOnlyList<ChartSeries> seriesList, int cursorIndex, int viewportStart, int viewportLength, (double Min, double Max) viewportRange, IReadOnlyDictionary<string, (double Min, double Max)> paneRanges, bool isHeikinAshi = false, bool isLogScale = false, float density = 1.0f, ImmutableDictionary<string, float>? paneHeightRatios = null, int rightMarginBars = 10, IReadOnlyList<Analysis.ChartPattern>? formations = null)
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
                _frameFormations = formations;
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
                // Every indicator pane is drawn. There used to be a scroll offset here —
                // Alt+Up / Alt+Down skipped the first N pane groups — and this line was the ONLY
                // thing in the codebase that read it. It moved nothing else: navigation, speech
                // and sonification never saw the offset, so every series stayed reachable and
                // every key still worked, and the only effect was that a pane the user could
                // still hear and edit was not on the screen. That is a scroll bar for a viewport
                // a blind user does not have, announcing itself as "Scroll panes up" — a sentence
                // with no information in it. Retired with split view.
                var indicatorSeries = seriesList.Where(s => s.Pane != "Main" && s.IsVisible)
                    .GroupBy(s => s.Pane).ToList();

                float totalPaneHeight = height - _axisHeight;
                if (totalPaneHeight <= 0) return;

                var paneNames = indicatorSeries.Select(g => g.Key).ToList();
                var alloc = AllocatePaneHeights(totalPaneHeight, paneNames, paneHeightRatios, density);
                float mainPaneHeight = alloc.MainHeight;
                float[] indHeights = alloc.IndicatorHeights;

                float currentY = 0;

                double mainMin = viewportRange.Min;
                double mainMax = viewportRange.Max;

                var mainPaneRect = new SKRect(0, currentY, width - _axisWidth, currentY + mainPaneHeight);
                var mainAxisRect = new SKRect(width - _axisWidth, currentY, width, currentY + mainPaneHeight);
                // The whole stacked-pane area, so a background gradient fades once across the
                // chart rather than restarting in every pane.
                var chartRect = new SKRect(0, mainPaneRect.Top, width - _axisWidth, height - _axisHeight);

                // Legend for main-pane indicator overlays (e.g. Cipher A, Cipher SR).
                // Exclude core series (candles, price line, volume) AND any volume-profile
                // series (VPVR / VPFR / TPO / any .IsProfile series) — those are their own
                // visual element rendered by ProfileRenderLayer, not an overlay that needs
                // a legend entry.
                var mainOverlaySeries = mainSeries
                    .Where(s => !s.IsProfile)
                    .Where(s => s.IndicatorCode?.ToUpperInvariant() is not ("CANDLES" or "PRICE" or "VOLUME" or "HEATMAP"))
                    .ToList();
                // Measured BEFORE the pane draws, because the formation layer places its labels
                // inside the pane and the legend is painted on top of it afterwards. Until
                // 2026-09-22 the layer did not know the legend existed, so "ascending triangle"
                // staggered its labels straight underneath the box and the box covered them —
                // visible in a screenshot, invisible to every test.
                SKRect? legendRect = mainOverlaySeries.Count > 0
                    ? MeasureLegendBox(mainPaneRect, mainOverlaySeries, density)
                    : null;

                RenderPane(canvas, mainPaneRect, visibleData, mainSeries, cursorIndex - viewportStart, viewportStart, "Main", mainMin, mainMax, isLogScale, viewportLength, density, chartRect: chartRect, avoid: legendRect);
                RenderYAxis(canvas, mainAxisRect, mainMin, mainMax, isLogScale, density);
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
                    // Where RenderCrosshair will put this pane's value badge — same clamp, same
                    // lookup — so the axis can keep its labels out from under it.
                    float? badgeTextY = null;
                    int badgeIndex = Math.Min(cursorIndex - viewportStart, visibleData.Count - 1);
                    if (badgeIndex >= 0 && CrosshairValueAt(paneSeriesList, badgeIndex, viewportStart) is { } badgeValue)
                        badgeTextY = ChartMath.MapY(badgeValue, paneRect.Top, paneRect.Bottom, min, max, false) + (4 * density);
                    RenderYAxis(canvas, indAxisRect, min, max, false, density, badgeTextY);
                    RenderPaneLegend(canvas, paneRect, paneSeriesList, density);
                    RenderYAxisSwatches(canvas, indAxisRect, paneSeriesList, min, max, false, density);
                    indicatorPaneInfos.Add((paneRect, min, max, paneSeriesList));
                    currentY += indicatorPaneHeight;
                }

                // Update shared layout service so ChartArea.razor can position drag handles.
                _paneLayout.Update(dividers, _axisHeight / height, _axisWidth / width);

                // Separator lines: vertical between chart area and Y-axis column; horizontal above X-axis strip.
                using var sepPaint = new SKPaint { Color = _theme.Current.GridLine.WithAlpha(160), StrokeWidth = 1 * density, Style = SKPaintStyle.Stroke };
                canvas.DrawLine(width - _axisWidth, 0, width - _axisWidth, height - _axisHeight, sepPaint);
                canvas.DrawLine(0, height - _axisHeight, width - _axisWidth, height - _axisHeight, sepPaint);

                RenderXAxis(canvas, new SKRect(0, height - _axisHeight, width - _axisWidth, height), visibleData, itemWidthForAxis, density);
                RenderCrosshair(canvas, new SKRect(0, 0, width - _axisWidth, totalPaneHeight), visibleData, cursorIndex - viewportStart, viewportStart, mainMin, mainMax, isLogScale, itemWidthForAxis, density, mainPaneHeight, indicatorPaneInfos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChartRenderer.Render failed");
                _appLogger.LogError("ChartRenderer.Render failed", nameof(ChartRenderer), ex);
            }
        }

        private static bool IsHeatmapSeries(ChartSeries s) =>
            s.Components.Any(c => c.DisplayType == ComponentDisplayType.Heatmap);

        private void RenderPane(SKCanvas canvas, SKRect rect, List<Ohlcv> visibleData, List<ChartSeries> series, int localCursorIndex, int viewportStart, string paneName, double min, double max, bool isLogScale, int viewportLength, float density, IReadOnlyDictionary<string, (double Min, double Max)>? allPaneRanges = null, SKRect? chartRect = null, SKRect? avoid = null)
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
            var ctx = new RenderContext(canvas, adjustedMainRect, visibleData, viewportStart, viewportLength, min, max, isLogScale, itemWidth, density, paneName, localCursorIndex, _theme.Current, ChartRect: chartRect, Avoid: avoid);

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
            // Formations draw AFTER every data layer so their levels sit on top of the candles
            // rather than under them — a trigger line hidden behind a wick communicates nothing.
            if (paneName == "Main" && _frameFormations is { Count: > 0 })
                _formationLayer.Render(ctx, _frameFormations);

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

        /// <param name="avoidTextY">
        /// Baseline of the crosshair's value badge in this strip, when there is one. The badge is
        /// painted last, over whatever label sits nearest — so the axis leaves that label out
        /// rather than have two numbers share the same rows.
        /// </param>
        private void RenderYAxis(SKCanvas canvas, SKRect rect, double min, double max, bool isLogScale, float density, float? avoidTextY = null)
        {
            // Round-number anchors. The old algorithm labelled at fixed fractions (0, 0.25, 0.5,
            // 0.75, 1.0) of the raw min/max, producing labels like "76227.38" on a 64k–80k BTC
            // view — accurate but useless; traders read in round thousands.
            //
            // The step is now ChartMath's, and it is a whole multiple of the GRIDLINE step, so a
            // label always lands on a line. This file used to run its own copy of the nice-number
            // algorithm against a different target (range/5, where BackgroundLayer used range/7)
            // and the comment here claimed they aligned. On a pane of range 20 the grid stepped
            // by 2 and the labels by 5, so 5 and 15 floated between lines. See ChartMath.NiceStep.
            double range = max - min;
            if (range <= 0 || double.IsNaN(range) || double.IsInfinity(range)) return;

            double niceStep = ChartMath.LabelStep(
                range, ChartMath.TargetLabelCount(rect.Height, density), ChartMath.GridStep(range));
            if (niceStep <= 0) return;

            float minLabelSpacing = _textFont.Size + (4 * density);
            float lastTextY = float.MaxValue;

            double firstLine = Math.Ceiling(min / niceStep) * niceStep;
            int safety = 0;
            for (double v = firstLine; v <= max && safety < 200; v += niceStep, safety++)
            {
                float y = ChartMath.MapY(v, rect.Top, rect.Bottom, min, max, isLogScale);
                // The spacing test is on the BASELINE THE TEXT IS DRAWN AT, after the clamp that
                // keeps a label inside its strip — not on the raw gridline position. The clamp
                // can move a top label down by most of a line height, and it used to do so after
                // the check had already passed: on a log-scale price pane "120.00" cleared
                // "115.00" by 23px on the gridline and then landed 9px above it on the page.
                float textY = Math.Clamp(y + (4 * density),
                    rect.Top + _textFont.Size + (6 * density),
                    rect.Bottom - (3 * density));
                if (Math.Abs(textY - lastTextY) < minLabelSpacing) continue;
                if (avoidTextY.HasValue && Math.Abs(textY - avoidTextY.Value) < minLabelSpacing) continue;
                lastTextY = textY;
                float lx = rect.Left + (3 * density);
                // MEASURED AGAINST THE EDGE IT IS DRAWN AT, which is the check this axis never
                // had. The strip is a fixed width, so a nine-digit volume label ("120000000.00")
                // simply ran off the right of the canvas while the price pane's "440.00" sat
                // comfortably inside. Everything else here — the spacing test, the clamp, the
                // crosshair-badge avoidance — compares labels with each OTHER.
                string label = FitAxisLabel(ChartMath.FormatAxisValue(v, range), v,
                                            rect.Right - lx - (3 * density));
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
            var (primaryFormat, markDateBoundaries) =
                ChartMath.XAxisFormat(visibleData[^1].Date - visibleData[0].Date);

            // A label is a claim that a bar sits above it. The slots are laid at fixed fractions
            // of the strip, and a slot to the right of the last bar used to clamp its bar index
            // to the last one — so a chart zoomed in past its final bar read "07/19 07/19 07/19"
            // across a region with nothing in it. Slots with no bar under them are skipped, and
            // the last bar gets its own label, right-aligned at the bar, so the end of the data
            // is always named exactly once.
            int labelCount = 5;
            float step = rect.Width / labelCount;
            float textY = rect.Top + _textFont.Size + (6 * density);
            DateTime? prevLabelDate = null;
            string? prevLabel = null;
            float lastLabelRight = float.MinValue;
            int lastLabelledIndex = -1;
            for (int i = 0; i < labelCount; i++)
            {
                float x = rect.Left + (i * step);
                float barX = x - rect.Left;
                int dIdx = (int)(barX / Math.Max(itemWidth, 1f));
                if (dIdx >= visibleData.Count) break;
                dIdx = Math.Max(0, dIdx);
                var d = visibleData[dIdx].Date;

                string label = d.ToString(primaryFormat);
                if (markDateBoundaries && prevLabelDate.HasValue && d.Date != prevLabelDate.Value.Date)
                {
                    // Day rolled over between labels — lead with the date so the
                    // user sees the boundary.
                    label = d.ToString("MM/dd ") + label;
                }
                prevLabelDate = d;
                prevLabel = label;
                lastLabelledIndex = dIdx;

                canvas.DrawText(label, x, textY, SKTextAlign.Left, _textFont, _textPaint);
                lastLabelRight = x + _textFont.MeasureText(label);
            }

            // The final bar, right-aligned at its own right edge (or the strip's, whichever is
            // nearer), unless a slot already named it or there is no room beside the last label.
            int lastIndex = visibleData.Count - 1;
            if (lastIndex > lastLabelledIndex)
            {
                float lastBarRight = Math.Min(rect.Right, rect.Left + (lastIndex + 1) * itemWidth);
                var d = visibleData[lastIndex].Date;
                string label = d.ToString(primaryFormat);
                if (markDateBoundaries && prevLabelDate.HasValue && d.Date != prevLabelDate.Value.Date)
                    label = d.ToString("MM/dd ") + label;
                float labelLeft = lastBarRight - (2 * density) - _textFont.MeasureText(label);
                if (label != prevLabel && labelLeft > lastLabelRight + (8 * density))
                    canvas.DrawText(label, lastBarRight - (2 * density), textY, SKTextAlign.Right, _textFont, _textPaint);
            }
        }

        // (An orphaned second <summary> sat here — RenderCrosshair's, left behind when that method
        // moved above this one. Two doc comments on one member: the compiler takes the last and
        // silently drops the first, so the tooltip described a method that was not this one.)
        /// <summary>
        /// The first non-NaN component reading in an indicator pane at the cursor's bar, or
        /// null when nothing in the pane has a value there.
        ///
        /// <para><paramref name="localIndex"/> is VIEWPORT-LOCAL and component arrays are
        /// ABSOLUTE, so this is where the two index spaces meet. The crosshair label used to
        /// read <c>data[localIndex]</c> straight: pan back to <c>ViewportStartIndex = 500</c>,
        /// put the cursor on bar 560, and the RSI pane's line and its number came from
        /// <c>rsi[60]</c> — a reading from five hundred bars ago, rendered as the current one.
        /// Every other renderer path indexes components as <c>ViewportStart + i</c>.</para>
        ///
        /// <para>Internal rather than private so it can be tested against real component data.
        /// The rest of <c>RenderCrosshair</c> is canvas work; this is the arithmetic that was
        /// wrong.</para>
        /// </summary>
        internal static double? CrosshairValueAt(
            IReadOnlyList<ChartSeries> paneSeries, int localIndex, int viewportStart)
        {
            int absIndex = localIndex + viewportStart;
            if (absIndex < 0) return null;

            foreach (var s in paneSeries)
            {
                foreach (var comp in s.Components)
                {
                    var data = s.GetComponentData(comp.Name);
                    if (data != null && absIndex < data.Length && !double.IsNaN(data[absIndex]))
                        return data[absIndex];
                }
            }
            return null;
        }

        private void RenderCrosshair(SKCanvas canvas, SKRect area, List<Ohlcv> visibleData, int localIndex, int viewportStart, double min, double max, bool isLogScale, float itemWidth, float density, float mainPaneHeight, List<(SKRect Rect, double Min, double Max, List<ChartSeries> Series)> indicatorPanes)
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
                double? val = CrosshairValueAt(paneSeries, localIndex, viewportStart);

                if (!val.HasValue) continue;
                float iy = ChartMath.MapY(val.Value, paneRect.Top, paneRect.Bottom, paneMin, paneMax, false);
                canvas.DrawLine(paneRect.Left, iy, paneRect.Right, iy, haloPaint);
                canvas.DrawLine(paneRect.Left, iy, paneRect.Right, iy, indPaint);

                // Y-value label at the right edge of the pane (matches RenderYAxis style, and
                // that includes fitting the strip: this badge is drawn INTO the axis strip too,
                // so a nine-digit volume reading overflowed the canvas here as well.)
                string label = FitAxisLabel(ChartMath.FormatAxisValue(val.Value, paneMax - paneMin),
                                            val.Value, _axisWidth - (8 * density));
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
        private const float LegendKeyPx  = 14f;   // width of the key column — a line stub or a glyph
        private const float LegendPadPx  = 6f;
        private const float LegendLinePx = 17f;

        /// <summary>
        /// The legend's rows and the box they occupy — ONE computation, shared by the pass that
        /// reserves the space and the pass that paints it, so the rect the formation layer avoids
        /// is the rect the legend actually draws.
        /// </summary>
        private (List<LegendRow> Rows, SKRect Box) MeasureLegend(SKRect paneRect, List<ChartSeries> paneSeries, float density)
        {
            float key  = LegendKeyPx  * density;
            float pad  = LegendPadPx  * density;
            float line = LegendLinePx * density;

            var rows = BuildLegendRows(paneSeries, paneRect.Height, line, pad, _theme.Current);
            if (rows.Count == 0) return (rows, SKRect.Empty);

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
            return (rows, new SKRect(bx, by, bx + boxW, by + boxH));
        }

        /// <summary>Where the pane's legend will be painted, or null when it has no rows.</summary>
        private SKRect? MeasureLegendBox(SKRect paneRect, List<ChartSeries> paneSeries, float density)
        {
            var (rows, box) = MeasureLegend(paneRect, paneSeries, density);
            return rows.Count == 0 ? null : box;
        }

        private void RenderPaneLegend(SKCanvas canvas, SKRect paneRect, List<ChartSeries> paneSeries, float density)
        {
            float key  = LegendKeyPx  * density;
            float pad  = LegendPadPx  * density;
            float line = LegendLinePx * density;

            var (rows, box) = MeasureLegend(paneRect, paneSeries, density);
            if (rows.Count == 0) return;

            float boxW = box.Width;
            float boxH = box.Height;
            float bx   = box.Left;
            float by   = box.Top;

            // Reads as an overlay rather than a dialog parked on the chart: near-opaque so text
            // stays legible over candles, but a hairline border instead of a heavy outline.
            //
            // Derived from the theme's dialog surface rather than a fixed near-black. On a
            // lighter theme a hardcoded #101014 box reads as a hole punched in the chart.
            // Let the chart show through. At near-full opacity this was a solid dark slab in the
            // corner — the one element that still read as parked ON the chart rather than part of
            // it. The border does the containing work instead, which is what an overlay is
            // supposed to look like. Text stays legible because the fill is still the dominant
            // layer and the ink is near-white.
            var surface = _theme.Current.SurfaceSunken;
            var bgRound = new SKRoundRect(new SKRect(bx, by, bx + boxW, by + boxH), 5 * density);
            using (var bgPaint = new SKPaint { Color = surface.WithAlpha(178), Style = SKPaintStyle.Fill })
                canvas.DrawRoundRect(bgRound, bgPaint);
            using (var borderPaint = new SKPaint { Color = _theme.Current.ChromeBorder.WithAlpha(140), Style = SKPaintStyle.Stroke, StrokeWidth = 1 * density })
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

        /// <summary>
        /// The axis label that fits the strip: the full number where it fits, and the spoken
        /// short form (<c>120M</c>) where it does not.
        ///
        /// <para>
        /// Not <see cref="Ellipsize"/>, and the difference matters: a truncated number is a
        /// WRONG number. "120000000.00" cut to "1200000…" reads as a different quantity, where
        /// "120M" reads as the same one. Ellipsis is right for a name and never for a value.
        /// </para>
        ///
        /// <para>
        /// The abbreviation is chosen by MEASUREMENT rather than by a magnitude threshold, so a
        /// theme with a wider axis, a larger display density, or a smaller font each move the
        /// point at which it kicks in — and a price axis, which never reaches the widths that
        /// trip it, is never touched.
        /// </para>
        /// </summary>
        private string FitAxisLabel(string full, double value, float maxPx)
        {
            if (maxPx <= 0 || _textFont.MeasureText(full) <= maxPx) return full;

            string compact = ChartMath.FormatAxisValueCompact(value);
            // Only if it actually helps. On a pathologically narrow strip neither fits, and the
            // full number is the more honest thing to overflow with.
            return _textFont.MeasureText(compact) < _textFont.MeasureText(full) ? compact : full;
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
            List<ChartSeries> paneSeries, float paneHeight, float line, float pad,
            ChartTheme? theme = null)
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
                    {
                        // Directional bars paint green/red by value direction rather than by a
                        // static hex, so the key shows the UP colour — and for volume that is now
                        // the theme's bullish candle, not a fixed green.
                        bool followsPrice = comp.Role is ComponentRole.Volume or ComponentRole.PriceAction;
                        color = followsPrice && theme != null
                            ? theme.CandleBullishBody.WithAlpha(200)
                            : new SKColor(68, 187, 68, 200);
                    }
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
            // The three-row floor applies only when the pane can physically hold three rows. At
            // eight panes a pane is about 55px and three rows plus padding is 63px, so the floor
            // was drawing the legend out of the bottom of its own pane and across the next pane's
            // divider — a key that covers the thing it is a key to. A pane that cannot hold even
            // one row gets no legend; the object tree still names everything.
            int holds = (int)((paneHeight - pad * 2) / Math.Max(line, 1f));
            if (holds < 1) return new List<LegendRow>();
            int maxEntries = Math.Clamp(fits, Math.Min(MinEntries, holds), HardMaxEntries);

            // OrderBy is a documented stable sort, so within a rank the series order the user
            // built is preserved.
            var shown = entries.OrderBy(e => e.Rank).ToList();
            int dropped = 0;
            if (shown.Count > maxEntries && maxEntries == 1)
            {
                // One row only: name the leading entry and count the rest on the same row, rather
                // than spend the whole legend on "+3 more" and name nothing.
                int rest = shown.Count - 1;
                shown = new List<LegendRow> { shown[0] with { Label = $"{shown[0].Label.Trim()} +{rest} more" } };
            }
            else if (shown.Count > maxEntries)
            {
                // One row is spent saying so, which is worth more than one more colour key:
                // a legend showing a silent subset reads as a complete list of what is on the chart.
                dropped = shown.Count - (maxEntries - 1);
                shown = shown.Take(maxEntries - 1).ToList();
            }

            if (dropped > 0)
                shown.Add(new LegendRow($"+{dropped} more (see the object tree)", LegendGlyph.Fill,
                    new[] { theme?.TextMuted ?? new SKColor(150, 150, 158) }, Rank: 3, SeriesName: ""));

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

        /// <summary>
        /// DELIBERATELY EMPTY, AND THAT IS THE FIX FOR A CRASH. Do not "restore" the two
        /// Dispose calls that used to be here, and do not replace them with a <c>_disposed</c>
        /// flag — both reintroduce the fault, and the flag version reintroduces it silently.
        ///
        /// <para><b>THE CRASH.</b> The hosted heads took <b>20 SIGSEGVs in 31 days</b>, roughly
        /// twice a day, every one of them at or within seconds of <c>Browser circuit closed</c>,
        /// and all 16 captured faults byte-identical: <c>signo 11 code 0001 addr 0x8</c>. A null
        /// dereference at offset 8, deterministically, for a month.</para>
        ///
        /// <para><b>WHY THE ADDRESS NAMED THE LINE.</b> SkiaSharp 3.x zeroes
        /// <c>SKObject.Handle</c> on <c>Dispose()</c> and adds no managed guard, so any later
        /// call passes NULL straight into native Skia — and each property faults at its own
        /// field offset. Measured across 43 disposed-object operations, each in its own process:
        /// only <c>SKFont.Size</c> (<c>SkFont::fSize</c>) and <c>SKPaint.Shader</c>
        /// (<c>SkPaint::fShader</c>) land on <c>0x8</c>. <c>SKPaint.Color</c> gives 0x30,
        /// <c>DrawText</c> with a disposed paint 0x28, a disposed canvas 0x0. That collapses a
        /// month of crashes to one statement.</para>
        ///
        /// <para><b>THE STATEMENT.</b> <c>_textFont.Size = ...</c> near the top of
        /// <see cref="Render"/> — the FIRST native touch in every frame, which is why all 16
        /// faults are identical rather than scattered. <c>ChartRenderer</c> is
        /// <c>AddScoped</c>, so the circuit scope disposes it at teardown, while
        /// <c>ChartArea.razor</c> draws each frame inside a bare <c>Task.Run</c> that its own
        /// <c>Dispose</c> neither tracks, cancels nor awaits. Circuit closes, container frees
        /// the font, a parked frame resumes on a thread-pool thread, and the first thing it does
        /// is write to a freed <c>SkFont</c>. Reproduced against this very class: not disposed
        /// survives; disposed-then-frame gives <c>addr 0x8</c>; the <c>Task.Run</c> race gives
        /// <c>addr 0x8</c>; disposing ONLY <c>_textFont</c> gives <c>addr 0x8</c>; disposing only
        /// <c>_textPaint</c> gives <c>addr 0x30</c>.</para>
        ///
        /// <para><b>WHY A <c>_disposed</c> GUARD IS NOT THE FIX.</b> It closes only the
        /// sequential window. The in-flight case shows a frame can already be past the guard
        /// when <c>Dispose()</c> runs — so the guard converts a reliable twice-a-day crash into
        /// a rare one, which is strictly worse to diagnose.</para>
        ///
        /// <para><b>WHY EMPTY IS CORRECT, not merely quiet.</b> <c>SKPaint</c> and <c>SKFont</c>
        /// carry SkiaSharp's own finalizer, and a finalizer cannot run while the object is
        /// reachable. An in-flight render holds a strong reference to this renderer, which holds
        /// these two fields — so the handles are reclaimed exactly when no frame can still be
        /// using them, which is the property a hand-written Dispose here cannot express. The
        /// cost is one small paint and one small font per circuit reclaimed at GC rather than at
        /// teardown. Nothing calls this by hand; the container is the only caller.</para>
        ///
        /// <para><b>THE REAL FIX, NOT ATTEMPTED HERE.</b> Make <c>_textPaint</c> and
        /// <c>_textFont</c> per-frame <c>using</c> locals threaded through the draw helpers.
        /// Then this type owns no native state and the question cannot arise. That is a
        /// fourteen-site change on the render path with no way to verify the output visually
        /// from here, so it is recorded rather than rushed — see docs/TODO.md.</para>
        ///
        /// <para><b>The two adjacent defects named here are CLOSED (2026-09-12).</b> The
        /// constructor's leaked <c>SKTypeface</c> is now one static shared face; and
        /// <c>AIAnalystService</c> no longer renders its snapshot through this shared instance —
        /// it builds one of its own, so it can neither retune this font's size underneath a
        /// browser frame drawing at a different density nor publish an 800×480 layout to the
        /// <c>IPaneLayoutService</c> that every pointer-to-bar mapping reads.</para>
        /// </summary>
        public void Dispose() { }
    }
}
