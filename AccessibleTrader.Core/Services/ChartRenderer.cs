using AccessibleTrader.Sdk.Theming;
using SkiaSharp;
using System.Collections.Immutable;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Theming;
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

        // The frame's whole series list, for the price-chart background tints: a component in
        // ANY pane may tint the price chart (Rendering.BackgroundTintPainter), and RenderPane
        // only sees its own pane's series. A per-frame field rather than a layer parameter, so the
        // IRenderLayer contract is not widened for the one pane that needs it.
        private IReadOnlyList<ChartSeries> _frameSeries = Array.Empty<ChartSeries>();

        /// <summary>
        /// The price pane's axis value labels as the last frame placed them, with the strip and
        /// scale they were placed on — so a test can measure how far each label sits from its own
        /// value (Cipher A's ribbon pushed tags ~100 px off their lines, 2026-10-18). Read-only
        /// diagnostics; nothing in the app reads it.
        /// </summary>
        internal (SKRect Axis, double Min, double Max, bool Log, IReadOnlyList<ScaleLabel> Labels) LastMainScale { get; private set; }

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

        /// <param name="focusedSeriesId">The series keyboard navigation is on, so an indicator
        /// pane's crosshair badge can read the component the user is on (see
        /// <see cref="CrosshairReadingAt"/>). Null reads each pane's primary line.</param>
        /// <param name="focusedComponentIndex">The component within that series.</param>
        /// <param name="identity">The chart's symbol, timeframe and provider, for the price pane's
        /// status line: measured here so formation labels stay out from under it, and painted
        /// when <see cref="PaintsStatusLines"/> is on.</param>
        /// <param name="statusTextScale">Settings → Appearance → Text size as a fraction (1 = 100%):
        /// the status lines are DOM text in rem, so the band each pane keeps for them grows with
        /// it (<see cref="ChartStatusLine.BandMetrics"/>).</param>
        public void Render(SKCanvas canvas, int width, int height, IReadOnlyList<Ohlcv> data, IReadOnlyList<ChartSeries> seriesList, int cursorIndex, int viewportStart, int viewportLength, (double Min, double Max) viewportRange, IReadOnlyDictionary<string, (double Min, double Max)> paneRanges, bool isHeikinAshi = false, bool isLogScale = false, float density = 1.0f, ImmutableDictionary<string, float>? paneHeightRatios = null, int rightMarginBars = 10, IReadOnlyList<Analysis.ChartPattern>? formations = null, string? focusedSeriesId = null, int focusedComponentIndex = -1, ChartIdentity? identity = null, float statusTextScale = 1f)
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

                _frameFormations = formations;
                // Background tints are declared per component now (BackgroundTintPainter), not
                // found here by name: Cipher B's "Anchor Polarity" and Top/Bottom Detector's
                // "Distribution Confidence" were the two hardcoded ones until 2026-10-17.
                _frameSeries = seriesList;

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

                var mainOverlaySeries = MainPaneLegendSeries(mainSeries);

                // The bar the crosshair labels: RenderCrosshair's own clamp (never past the last
                // real bar), computed ONCE so every pane's badge, every status line and every
                // axis that keeps its labels out from under one are reading the same bar.
                int crossIndex = Math.Min(cursorIndex - viewportStart, visibleData.Count - 1);

                // Each pane's STATUS LINES (Cody's 2026-10-17 decision: TradingView status line).
                // On the live chart they are DOM text drawn by ChartArea over this image, but they
                // are measured here, BEFORE each pane draws, because the pane's PLOT starts below
                // them: every pane keeps a STATUS BAND at its top exactly as tall as the block
                // shown there (none when it shows none), and maps its values into what is left.
                // Before that the block sat on the plot, and Cipher B's 0–100 RSI and Stochastic,
                // which run near the top of its −100…+100 pane, and the waves' peaks disappeared
                // behind it (Cody, 2026-10-17, BTCUSDT 1d). TradingView keeps a top margin for the
                // same reason; this one is the block's own height, at the text size the DOM uses.
                // The line COUNT does not depend on the bar (a plot with no reading keeps its place
                // as "—"), so the band is the same whichever bar the DOM reads.
                float textScale = ChartStatusLine.ClampTextScale(statusTextScale);
                using var statusFont = new SKFont(AxisTypeface, ChartStatusLine.FontPx * textScale * density);
                var statusCtx = new ChartStatusLine.Context(_theme.Current, StatusBackingAlpha, data, identity, isHeikinAshi);
                int statusBar = crossIndex >= 0 ? viewportStart + crossIndex : Math.Clamp(cursorIndex, 0, data.Count - 1);
                var statusBands = new Dictionary<string, PaneStatusBand>(StringComparer.Ordinal);
                var (mainStatus, mainBand) = StatusBandFor("Main", mainOverlaySeries, statusBar, statusCtx, mainPaneRect, density, textScale, statusFont);
                statusBands["Main"] = new PaneStatusBand(mainStatus.Lines.Count, mainBand / height);
                var mainPlotRect = PlotRect(mainPaneRect, mainBand);
                // The price pane's own values are drawn above its sub-pane strips, if it has any;
                // its axis, badge and value labels map over that same area (Cody, 2026-10-17).
                var (mainArea, mainStrips) = SubPaneLayout(mainPlotRect, mainSeries);
                var subPaneStrips = new Dictionary<string, IReadOnlyList<SubPaneStrip>>(StringComparer.Ordinal);
                if (mainStrips.Count > 0) subPaneStrips["Main"] = Published(mainStrips, height);
                var mainAxisPlotRect = new SKRect(mainAxisRect.Left, mainArea.Top, mainAxisRect.Right, mainArea.Bottom);
                // The formation layer still keeps its labels out of the block; with the band the
                // block is above the plot, so this only matters to a label placed above it.
                SKRect? legendRect = mainStatus is { Lines.Count: > 0 } ms
                    ? StatusBlockRect(mainPaneRect, ms, density, textScale, statusFont)
                    : null;

                // The price pane's cursor line sits at the close and had NO badge (Cody's
                // 2026-10-15 screenshots): a line across the candles with nothing saying what it
                // marks. It gets the same badge as the indicator panes, chipped in the candle's
                // own direction colour.
                CrosshairBadge? mainBadge = null;
                if (crossIndex >= 0)
                {
                    var bar = visibleData[crossIndex];
                    var (bull, bear) = StandardRenderers.ApplyColorVision(
                        _theme.Current, _theme.Current.CandleBullishBody, _theme.Current.CandleBearishBody);
                    float closeY = ChartMath.MapY(bar.Close, mainArea.Top, mainArea.Bottom, mainMin, mainMax, isLogScale);
                    mainBadge = new CrosshairBadge(bar.Close, closeY, bar.Close >= bar.Open ? bull : bear, mainMax - mainMin,
                        BadgeCentre(closeY, mainArea.Top, mainArea.Bottom, ScaleLabelHeight(density)));
                }

                RenderPane(canvas, mainPaneRect, visibleData, mainSeries, cursorIndex - viewportStart, viewportStart, "Main", mainMin, mainMax, isLogScale, viewportLength, density, allPaneRanges: paneRanges, chartRect: chartRect, avoid: legendRect, statusBand: mainBand);
                // Each visible plot's newest value as a label on the axis, in the plot's colour
                // (TradingView's "labels on the price scale"), placed before the axis draws so the
                // gridline labels keep out from under them as they keep out from under the badge.
                // On the axis beside the PLOT: the strip spans the whole pane, its scale only the
                // part the values are mapped into.
                var mainScale = PlaceScaleLabels(ScaleLabelsFor(mainOverlaySeries, data.Count, _theme.Current),
                    mainAxisPlotRect, mainMin, mainMax, isLogScale, density, mainBadge, preferred: null);
                LastMainScale = (mainAxisPlotRect, mainMin, mainMax, isLogScale, mainScale);
                RenderYAxis(canvas, mainAxisPlotRect, mainMin, mainMax, isLogScale, density, AvoidRows(mainBadge, mainScale, density));
                RenderScaleLabels(canvas, mainAxisPlotRect, mainScale, mainMax - mainMin, density);
                RenderStripAxes(canvas, "Main", mainStrips, mainAxisRect.Left, mainAxisRect.Right, mainSeries, paneRanges, isLogScale, data.Count, density);
                if (PaintsStatusLines && crossIndex >= 0 && mainStatus.Lines.Count > 0)
                    PaintStatusBlock(canvas, mainPaneRect, mainStatus, density, textScale, statusFont);
                currentY += mainPaneHeight;

                float itemWidthForAxis = (width - _axisWidth) / Math.Max(1, viewportLength);

                var indicatorPaneInfos = new List<(SKRect Rect, CrosshairBadge? Badge)>();
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
                    // This pane's status band, as the price pane's above: its plot starts below it.
                    var (paneStatus, paneBand) = StatusBandFor(group.Key, paneSeriesList, statusBar, statusCtx, paneRect, density, textScale, statusFont);
                    statusBands[group.Key] = new PaneStatusBand(paneStatus.Lines.Count, paneBand / height);
                    var plotRect = PlotRect(paneRect, paneBand);
                    var (paneArea, paneStrips) = SubPaneLayout(plotRect, paneSeriesList);
                    if (paneStrips.Count > 0) subPaneStrips[group.Key] = Published(paneStrips, height);
                    // Indicator panes always use raw (non-HA) data so PriceAction coloring (e.g. volume)
                    // reflects real open/close direction, not the HA-transformed direction.
                    // Pass allPaneRanges so sub-panes can look up their composite-keyed ranges.
                    // The pane's own range is on the axis beside the area above its strips.
                    var indAxisRect = new SKRect(width - _axisWidth, paneArea.Top, width, paneArea.Bottom);
                    RenderPane(canvas, paneRect, rawVisibleData, paneSeriesList, cursorIndex - viewportStart, viewportStart, group.Key, min, max, false, viewportLength, density, paneRanges, chartRect, statusBand: paneBand);
                    // This pane's value badge, decided ONCE: the axis keeps its labels out from
                    // under it and RenderCrosshair draws it, from the same object. They used to
                    // be two copies of the same lookup, kept in step by a comment.
                    CrosshairBadge? badge = null;
                    (string, string)? preferred = null;
                    if (crossIndex >= 0
                        && CrosshairReadingAt(paneSeriesList, crossIndex, viewportStart, focusedSeriesId, focusedComponentIndex) is { } reading)
                    {
                        float lineY = ChartMath.MapY(reading.Value, paneArea.Top, paneArea.Bottom, min, max, false);
                        badge = new CrosshairBadge(reading.Value, lineY,
                            ReadingColor(reading, _theme.Current, rawVisibleData[crossIndex]), max - min,
                            BadgeCentre(lineY, paneArea.Top, paneArea.Bottom, ScaleLabelHeight(density)));
                        // The plot the badge reads — navigation's, else the pane's primary line —
                        // keeps its axis label first when the strip cannot hold them all.
                        preferred = (reading.Series.Id, reading.Component.Name);
                    }
                    var scale = PlaceScaleLabels(ScaleLabelsFor(paneSeriesList, data.Count, _theme.Current),
                        indAxisRect, min, max, false, density, badge, preferred);
                    RenderYAxis(canvas, indAxisRect, min, max, false, density, AvoidRows(badge, scale, density));
                    RenderScaleLabels(canvas, indAxisRect, scale, max - min, density);
                    RenderStripAxes(canvas, group.Key, paneStrips, width - _axisWidth, width, paneSeriesList, paneRanges, false, data.Count, density);
                    if (PaintsStatusLines && crossIndex >= 0 && paneStatus.Lines.Count > 0)
                        PaintStatusBlock(canvas, paneRect, paneStatus, density, textScale, statusFont);
                    indicatorPaneInfos.Add((paneRect, badge));
                    currentY += indicatorPaneHeight;
                }

                // Update shared layout service so ChartArea.razor can position drag handles and,
                // from the same dividers and bands, each pane's status lines — and so every
                // pointer-to-value mapping starts each pane's plot below its band, as drawn here.
                _paneLayout.Update(dividers, _axisHeight / height, _axisWidth / width, statusBands, subPaneStrips);

                // Separator lines: vertical between chart area and Y-axis column; horizontal above X-axis strip.
                using var sepPaint = new SKPaint { Color = _theme.Current.GridLine.WithAlpha(160), StrokeWidth = 1 * density, Style = SKPaintStyle.Stroke };
                canvas.DrawLine(width - _axisWidth, 0, width - _axisWidth, height - _axisHeight, sepPaint);
                canvas.DrawLine(0, height - _axisHeight, width - _axisWidth, height - _axisHeight, sepPaint);

                // The cursor bar's date as a label ON THE TIME AXIS (TradingView's model; it used to
                // be in the hover readout box only, and only for the mouse). Measured first so the
                // axis leaves out the tick labels it would cut in half.
                var xAxisRect = new SKRect(0, height - _axisHeight, width - _axisWidth, height);
                var dateBadge = crossIndex >= 0 ? CursorDateBadge(xAxisRect, visibleData, crossIndex, itemWidthForAxis, density) : null;
                RenderXAxis(canvas, xAxisRect, visibleData, itemWidthForAxis, density, dateBadge?.Rect);
                if (dateBadge is { } db) DrawDateBadge(canvas, db, density);
                RenderCrosshair(canvas, new SKRect(0, 0, width - _axisWidth, totalPaneHeight), visibleData, cursorIndex - viewportStart, itemWidthForAxis, density, mainPaneRect, mainBadge, indicatorPaneInfos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChartRenderer.Render failed");
                _appLogger.LogError("ChartRenderer.Render failed", nameof(ChartRenderer), ex);
            }
        }

        private static bool IsHeatmapSeries(ChartSeries s) =>
            s.Components.Any(c => c.DisplayType == ComponentDisplayType.Heatmap);

        /// <param name="rect">The whole pane.</param>
        /// <param name="statusBand">The height of the pane's status band (<see cref="StatusBandFor"/>):
        /// the pane's background and border fill the whole pane, its values are mapped and clipped
        /// to what is below the band.</param>
        private void RenderPane(SKCanvas canvas, SKRect rect, List<Ohlcv> visibleData, List<ChartSeries> series, int localCursorIndex, int viewportStart, string paneName, double min, double max, bool isLogScale, int viewportLength, float density, IReadOnlyDictionary<string, (double Min, double Max)>? allPaneRanges = null, SKRect? chartRect = null, SKRect? avoid = null, float statusBand = 0f)
        {
            if (viewportLength <= 0) return;
            var paneRect = rect;
            rect = PlotRect(rect, statusBand);

            int renderCount = Math.Max(1, viewportLength);
            float itemWidth = rect.Width / renderCount;

            var nonProfileSeries = series.Where(s => !s.IsProfile || IsHeatmapSeries(s)).ToList();
            var profileSeries    = series.Where(s =>  s.IsProfile && s.IsVisible && !IsHeatmapSeries(s)).ToList();

            // ── Sub-pane strips, from component metadata (SubPaneLayout, shared with the axis
            // and published for the pointer mappings) ─────────────────────────
            var (mainRect, strips) = SubPaneLayout(rect, series);

            // ── Main area pass ────────────────────────────────────────────────
            var adjustedMainRect = new SKRect(rect.Left, mainRect.Top, rect.Right, mainRect.Bottom);
            // The frame the background fills and borders: the plot plus the status band above it,
            // so the band reads as the top of the pane, not a gap between two.
            var frameRect        = new SKRect(rect.Left, paneRect.Top, rect.Right, mainRect.Bottom);
            var ctx = new RenderContext(canvas, adjustedMainRect, visibleData, viewportStart, viewportLength, min, max, isLogScale, itemWidth, density, paneName, localCursorIndex, _theme.Current, ChartRect: chartRect, Avoid: avoid,
                FrameRect: statusBand > 0 ? frameRect : null);

            canvas.Save(); canvas.ClipRect(frameRect);
            for (int li = 0; li < _layers.Count; li++)
            {
                // The background has filled the whole frame; everything after it — the data, its
                // markers, fills and labels — is held to the plot, so nothing reaches the band.
                if (li == 1) canvas.ClipRect(mainRect);
                _layers[li].Render(ctx, nonProfileSeries);
                // Background tints are painted immediately after the BackgroundLayer (index 0),
                // so they sit UNDER the grid, candle, data and overlay layers but OVER the pane
                // fill: the price-chart tints of every series on the chart, then this pane's own.
                // They span ctx.PaneRect — the PLOT, like the data — never the status band above
                // it, even though the clip is still the whole frame at this point.
                if (li == 0)
                {
                    if (paneName == "Main") BackgroundTintPainter.PaintPriceChartTints(ctx, _frameSeries);
                    BackgroundTintPainter.PaintPaneTints(ctx, series);
                }
                // The volume profile's bars go UNDER the candles, on the pane fill like the tints.
                // They were painted after everything, and in Cody's 2026-10-15 screenshots the
                // strip (20% of the pane, right-aligned) covered the newest three months of
                // candles. The POC line is drawn below, over the candles.
                if (li == 0 && profileSeries.Count > 0)
                    _profileLayer.RenderBins(ctx, profileSeries);
            }
            // Formations draw AFTER every data layer so their levels sit on top of the candles
            // rather than under them — a trigger line hidden behind a wick communicates nothing.
            // ...and place their labels off the numbers the value labels printed (DBSI-style's
            // scores), as they keep off candle ink.
            if (paneName == "Main" && _frameFormations is { Count: > 0 })
                _formationLayer.Render(ctx with { TextObstacles = ValueLabelRenderer.Plates(ctx, nonProfileSeries) }, _frameFormations);

            // The POC is a level, read against the candles for the same reason as a formation's.
            if (profileSeries.Count > 0) _profileLayer.RenderPoc(ctx, profileSeries);
            canvas.Restore();

            // ── Sub-pane passes ───────────────────────────────────────────────
            foreach (var (spName, spRect) in strips)
            {
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
                for (int li = 0; li < _layers.Count; li++)
                {
                    _layers[li].Render(spCtx, nonProfileSeries);
                    // A sub-pane component's "this pane" tint fills its strip, at the same depth.
                    if (li == 0) BackgroundTintPainter.PaintPaneTints(spCtx, series);
                }
                canvas.Restore();
            }
        }

        /// <summary>One sub-pane strip as laid out: its name and the rect it is drawn in.</summary>
        internal readonly record struct SubPaneArea(string Name, SKRect Rect);

        /// <summary>
        /// <b>Where a pane's sub-pane strips go, as a pure function</b> — the one layout the
        /// drawing, the axis, the crosshair badge and (through
        /// <see cref="IPaneLayoutService.SubPaneStrips"/>) every pointer mapping use (Cody,
        /// 2026-10-17). A component with a <c>SubPaneName</c> (Loukas Cycles' "FY") is drawn in a
        /// strip along the bottom of the pane's plot, one strip per name in declaration order,
        /// each <c>SubPaneHeightRatio</c> of the plot (0.22 when unset, clamped 5–40%); the
        /// strips together take at most 70% and the pane's own components the area above.
        ///
        /// <para>
        /// Extracted from <c>RenderPane</c>, where it was computed and used only for drawing:
        /// the axis, badge, hover readout and hit tester all mapped the pane's range over the
        /// whole plot, strip included, so every one of them disagreed with the pixels. When the
        /// ratios add up to more than 70% the strips now share the 70% in proportion; they used
        /// to keep their full ratios and run off the bottom of the pane.
        /// </para>
        /// </summary>
        internal static (SKRect Main, List<SubPaneArea> Strips) SubPaneLayout(SKRect plot, IEnumerable<ChartSeries> series)
        {
            var info = new List<(string Name, float Ratio)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in series)
                foreach (var comp in s.Components)
                    if (!string.IsNullOrEmpty(comp.SubPaneName) && seen.Add(comp.SubPaneName!))
                        info.Add((comp.SubPaneName!, Math.Clamp(comp.SubPaneHeightRatio ?? 0.22f, 0.05f, 0.40f)));
            var strips = new List<SubPaneArea>();
            if (info.Count == 0) return (plot, strips);

            float sum = info.Sum(i => i.Ratio);
            float mainHeight = plot.Height * (1f - Math.Min(sum, 0.70f));
            float stripSpace = plot.Height - mainHeight;
            float y = plot.Top + mainHeight;
            foreach (var (name, ratio) in info)
            {
                float h = stripSpace * ratio / sum;
                strips.Add(new SubPaneArea(name, new SKRect(plot.Left, y, plot.Right, y + h)));
                y += h;
            }
            return (new SKRect(plot.Left, plot.Top, plot.Right, plot.Top + mainHeight), strips);
        }

        /// <summary>A pane's strips as <see cref="IPaneLayoutService"/> publishes them: fractions of the canvas height.</summary>
        private static IReadOnlyList<SubPaneStrip> Published(List<SubPaneArea> strips, int height)
            => strips.Select(s => new SubPaneStrip(s.Name, s.Rect.Top / height, s.Rect.Bottom / height)).ToList();

        /// <summary>
        /// Each sub-pane strip's own scale on the axis beside it: gridline labels and its plots'
        /// newest-value labels, on the strip's range (<c>"Pane/Sub"</c>, −1–1 when unset, as the
        /// strip is drawn). The strip had no scale of its own; its rows carried the pane's range,
        /// stretched (Cody, 2026-10-17).
        /// </summary>
        private void RenderStripAxes(SKCanvas canvas, string paneName, List<SubPaneArea> strips, float axisLeft, float axisRight,
            IReadOnlyList<ChartSeries> series, IReadOnlyDictionary<string, (double Min, double Max)> ranges, bool isLog, int barCount, float density)
        {
            foreach (var (name, rect) in strips)
            {
                var (min, max) = ranges.TryGetValue($"{paneName}/{name}", out var r) ? r : (-1.0, 1.0);
                var axis = new SKRect(axisLeft, rect.Top, axisRight, rect.Bottom);
                var labels = PlaceScaleLabels(ScaleLabelsFor(series, barCount, _theme.Current, name), axis, min, max, isLog, density, null, null);
                RenderYAxis(canvas, axis, min, max, isLog, density, AvoidRows(null, labels, density));
                RenderScaleLabels(canvas, axis, labels, max - min, density);
            }
        }

        /// <param name="avoidTextYs">
        /// Baselines of what is painted over this strip afterwards: the crosshair's value badge and
        /// each plot's value label. Each is painted over whatever gridline label sits nearest — so
        /// the axis leaves that label out rather than have two numbers share the same rows.
        /// </param>
        private void RenderYAxis(SKCanvas canvas, SKRect rect, double min, double max, bool isLogScale, float density, IReadOnlyList<float>? avoidTextYs = null)
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
            // A strip shorter than one label has no room for any: the band a label may sit in is
            // inverted, and Math.Clamp THROWS on an inverted band rather than picking a side. A
            // 5-line status block over a short sub-pane strip (Loukas cycles under a price pane)
            // did exactly that, and the exception failed the whole frame — "chart render failed"
            // on every tab carrying one (Cody, 2026-10-21, the Camel cycles workspace).
            float labelTop = rect.Top + _textFont.Size + (6 * density);
            float labelBottom = rect.Bottom - (3 * density);
            if (labelBottom < labelTop) return;

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
                float textY = Math.Clamp(y + (4 * density), labelTop, labelBottom);
                if (Math.Abs(textY - lastTextY) < minLabelSpacing) continue;
                if (avoidTextYs != null && avoidTextYs.Any(a => Math.Abs(textY - a) < minLabelSpacing)) continue;
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
        /// One plot's value label on its pane's axis: the plot's newest value, filled in the
        /// plot's colour at that value, with black or white text, whichever contrasts more.
        /// </summary>
        /// <param name="Tier">0 for a line, 1 for bars — lines keep their label when the strip
        /// is too short for all of them.</param>
        /// <param name="Y">Where the label is centred once placed; NaN until then.</param>
        internal readonly record struct ScaleLabel(double Value, SKColor Fill, int Tier, string Series, string Component, float Y = float.NaN)
        {
            /// <summary>The text colour: black or white, whichever reads better on the fill.</summary>
            public SKColor Ink => WcagContrast.MostContrasting(Fill.WithAlpha(255), SKColors.Black, SKColors.White);
        }

        /// <summary>
        /// The value labels a pane's axis carries — TradingView's "labels on the price scale"
        /// (Cody's 2026-10-17 decision: TradingView status line). One per visible line or bar plot,
        /// at its NEWEST value, the bar the chart is live on; the status line carries the value at
        /// the cursor and the crosshair badge marks it on the axis. Replaces the 4x3 px swatch
        /// ticks this axis had (lines and areas only: a colour with no number). Never a hidden,
        /// disabled or sub-pane component — the crosshair badge's rule (<see cref="ReadingTier"/>):
        /// a sub-pane strip's values are on that strip's range, not this axis — and never one whose
        /// axis label is switched off (<see cref="ComponentConfig.ShowAxisLabelInEffect"/>).
        /// </summary>
        /// <param name="subPane">A sub-pane strip's name: its components' labels instead, for the strip's own axis.</param>
        internal static List<ScaleLabel> ScaleLabelsFor(IReadOnlyList<ChartSeries> paneSeries, int barCount, ChartTheme theme, string? subPane = null)
        {
            var labels = new List<ScaleLabel>();
            foreach (var s in paneSeries)
            {
                if (s.IsDrawing || !s.IsVisible) continue;
                foreach (var comp in s.Components)
                {
                    if (!comp.IsDrawn) continue; // It remains heard, but has nothing on the axis to label.
                    int tier = ReadingTier(comp, subPane);
                    if (tier is not (0 or 1)) continue;
                    // The component's own switch (ComponentConfig.ShowAxisLabel, the user's choice
                    // first): off for a line in a crowd, such as Cipher A's ribbon, whose eight
                    // labels within a few percent of price stacked the axis and pushed every tag
                    // away from its line (Cody's BTCUSDT 1D screenshot, 2026-10-18).
                    if (!comp.ShowAxisLabelInEffect()) continue;
                    var data = s.GetComponentData(comp.Name);
                    if (data == null || data.Length == 0) continue;
                    // Walk back from the newest bar to a value: some indicators end in NaN
                    // inside their warm-up region, or one bar short of the price data.
                    int last = Math.Min(data.Length, Math.Max(barCount, 1)) - 1;
                    for (int i = last; i >= 0 && i > last - 20; i--)
                    {
                        if (double.IsNaN(data[i])) continue;
                        var fill = ReadingColor(new CrosshairReading(data[i], s, comp, i), theme, null);
                        labels.Add(new ScaleLabel(data[i], fill, tier, s.Id, comp.Name));
                        break;
                    }
                }
            }
            return labels;
        }

        /// <summary>
        /// Places the labels on the strip at their values, never overlapping each other, the
        /// crosshair badge, or the strip's ends. A label whose value is off the pane's range is
        /// left out (the swatches were too).
        ///
        /// <para>
        /// <b>Two flaws in the first version (the coordinator's review of the 2026-10-17
        /// screenshots).</b> The labels were stacked among themselves only, and the crosshair
        /// badge — drawn last — sat on top of whichever landed at the cursor's value: at
        /// 1000x610 it hid most of the top "123.44", which read as clipped. And in an eight-pane
        /// chart a 29 px pane holds one label: the stack pushed the rest out past the strip's
        /// end, under the badge and into the next pane's axis ("93.1" over "93.1").
        /// </para>
        ///
        /// <para>
        /// So the badge is an obstacle the labels are placed around, a label that would only
        /// repeat the badge (same value at the same place — the price line's newest close while
        /// the cursor is on the newest bar) is dropped, and labels are placed one at a time in
        /// priority order — the plot navigation is on (or the pane's primary line, which the
        /// badge reads), then lines, then bars, then declaration order — each at the free spot
        /// nearest its value inside the strip. One that has no free spot is left out rather than
        /// overprinted. Finally the placed labels are put back in value order on the spots they
        /// took, so the stack still reads top to bottom as the values do.
        /// </para>
        /// </summary>
        private List<ScaleLabel> PlaceScaleLabels(List<ScaleLabel> labels, SKRect axisRect, double min, double max, bool isLog,
            float density, CrosshairBadge? badge, (string Series, string Component)? preferred)
            => StackScaleLabels(labels, axisRect, min, max, isLog, ScaleLabelHeight(density),
                badge?.Centre, badge?.Value, preferred);

        private float ScaleLabelHeight(float density) => _textFont.Size + (4 * density);

        /// <param name="reservedY">The crosshair badge's centre on this strip, an obstacle; null when there is none.</param>
        /// <param name="reservedValue">The badge's value: a label with the same value at the badge's place repeats it and is dropped.</param>
        /// <param name="preferred">The plot whose label is kept first: the one navigation is on, or the pane's primary line.</param>
        internal static List<ScaleLabel> StackScaleLabels(List<ScaleLabel> labels, SKRect axisRect, double min, double max, bool isLog,
            float labelH, float? reservedY = null, double? reservedValue = null, (string Series, string Component)? preferred = null)
        {
            const float Eps = 0.01f;
            float lo = axisRect.Top + labelH / 2f, hi = axisRect.Bottom - labelH / 2f;
            if (hi < lo - Eps) return new List<ScaleLabel>();

            var onPane = new List<(ScaleLabel L, int Index)>();
            for (int i = 0; i < labels.Count; i++)
            {
                var l = labels[i];
                float y = ChartMath.MapY(l.Value, axisRect.Top, axisRect.Bottom, min, max, isLog);
                if (float.IsNaN(y) || y < axisRect.Top || y > axisRect.Bottom) continue;
                if (reservedY is { } r && reservedValue is { } rv
                    && Math.Abs(l.Value - rv) <= 1e-9 * Math.Max(1.0, Math.Abs(rv)) && Math.Abs(y - r) < labelH / 2f)
                    continue;   // the badge already prints this number here
                onPane.Add((l with { Y = y }, i));
            }

            var occupied = new List<float>();
            if (reservedY is { } ry) occupied.Add(Math.Clamp(ry, lo, Math.Max(lo, hi)));

            var placed = new List<ScaleLabel>();
            foreach (var (l, _) in onPane
                .OrderBy(t => preferred is { } p && t.L.Series == p.Series && t.L.Component == p.Component ? 0 : 1)
                .ThenBy(t => t.L.Tier).ThenBy(t => t.Index))
            {
                float want = Math.Clamp(l.Y, lo, Math.Max(lo, hi));
                bool Free(float c) => c >= lo - Eps && c <= hi + Eps && occupied.All(o => Math.Abs(c - o) >= labelH - Eps);
                float? best = null;
                foreach (float c in occupied.SelectMany(o => new[] { o - labelH, o + labelH }).Prepend(want))
                    if (Free(c) && (best is not { } b || Math.Abs(c - want) < Math.Abs(b - want)))
                        best = c;
                if (best is not { } spot) continue;   // no room: left out, never overprinted
                occupied.Add(spot);
                placed.Add(l with { Y = spot });
            }

            // Back into value order on the spots taken: highest value on the highest spot.
            var spots = placed.Select(p => p.Y).OrderBy(y => y).ToList();
            var byValue = placed.OrderByDescending(p => isLog && p.Value > 0 ? Math.Log(p.Value) : p.Value).ToList();
            if (max < min) byValue.Reverse();
            return byValue.Select((p, i) => p with { Y = spots[i] }).ToList();
        }

        /// <summary>
        /// Where a crosshair badge is drawn: at its value, but never past either end of its pane —
        /// a reading near the pane's top or bottom used to hang half into the next pane's axis.
        /// </summary>
        internal static float BadgeCentre(float lineY, float paneTop, float paneBottom, float labelH)
        {
            float lo = paneTop + labelH / 2f, hi = paneBottom - labelH / 2f;
            return hi < lo ? (paneTop + paneBottom) / 2f : Math.Clamp(lineY, lo, hi);
        }
        /// <summary>The text baselines of the badge and the value labels, for the axis to keep its gridline labels clear of.</summary>
        private static List<float> AvoidRows(CrosshairBadge? badge, List<ScaleLabel> labels, float density)
        {
            var rows = labels.Select(l => l.Y + (4 * density)).ToList();
            if (badge is { } b) rows.Add(b.TextY(density));
            return rows;
        }

        private void RenderScaleLabels(SKCanvas canvas, SKRect axisRect, List<ScaleLabel> labels, double range, float density)
        {
            if (labels.Count == 0) return;
            float labelH = ScaleLabelHeight(density);
            float lx = axisRect.Left + (2 * density);
            float pad = 3 * density;
            using var fill = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
            using var ink = new SKPaint { IsAntialias = true };
            foreach (var l in labels)
            {
                string text = FitAxisLabel(ChartMath.FormatAxisReading(l.Value, range), l.Value,
                                           axisRect.Right - lx - (2 * pad));
                float w = _textFont.MeasureText(text) + 2 * pad;
                fill.Color = l.Fill.WithAlpha(255);
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(lx, l.Y - labelH / 2f, lx + w, l.Y + labelH / 2f), 2 * density), fill);
                ink.Color = l.Ink;
                canvas.DrawText(text, lx + pad, l.Y + (4 * density), SKTextAlign.Left, _textFont, ink);
            }
        }

        /// <summary>The cursor bar's date label on the time axis: its text and the rect it covers.</summary>
        internal readonly record struct DateBadge(string Text, SKRect Rect, float TextX, float TextY);

        /// <summary>
        /// The date of the bar under the keyboard cursor, as a label on the time axis centred on
        /// the crosshair and kept inside the strip — TradingView's model. The date used to be in
        /// the hover readout box only, so only for the mouse. Same text as the hover's, by the
        /// rule every spoken readout uses (<see cref="ChartMath.FormatCrosshairDate"/>).
        /// </summary>
        private DateBadge? CursorDateBadge(SKRect xAxisRect, List<Ohlcv> visibleData, int localIndex, float itemWidth, float density)
        {
            if (localIndex < 0 || localIndex >= visibleData.Count) return null;
            string text = ChartMath.FormatCrosshairDate(visibleData[localIndex].Date, ChartMath.BarSecondsOf(visibleData));
            float pad = 5 * density;
            float w = _textFont.MeasureText(text) + 2 * pad;
            float cx = xAxisRect.Left + (localIndex * itemWidth) + (itemWidth / 2);
            float left = Math.Clamp(cx - w / 2f, xAxisRect.Left, Math.Max(xAxisRect.Left, xAxisRect.Right - w));
            float top = xAxisRect.Top + (2 * density);
            float bottom = Math.Min(xAxisRect.Bottom - (2 * density), top + _textFont.Size + (8 * density));
            return new DateBadge(text, new SKRect(left, top, left + w, bottom), left + pad, xAxisRect.Top + _textFont.Size + (6 * density));
        }

        private void DrawDateBadge(SKCanvas canvas, DateBadge badge, float density)
        {
            using var bg = new SKPaint { Color = new SKColor(40, 40, 40, 235), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(new SKRoundRect(badge.Rect, 3 * density), bg);
            using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawText(badge.Text, badge.TextX, badge.TextY, SKTextAlign.Left, _textFont, ink);
        }
        /// <param name="avoid">The cursor's date label, painted over the strip afterwards: a tick
        /// label it would cut in half is left out.</param>
        private void RenderXAxis(SKCanvas canvas, SKRect rect, List<Ohlcv> visibleData, float itemWidth, float density, SKRect? avoid = null)
        {
            bool Clear(float left, float right) =>
                avoid is not { } av || right < av.Left - (4 * density) || left > av.Right + (4 * density);
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

            // The axis names bars by the same rule as every spoken readout
            // (SpeechTimeFormatter.ToBarDisplay): a daily-or-coarser bar by its own date,
            // an intraday bar by the user's clock. It used to print the raw UTC stamp, so an
            // intraday axis in Chicago read five hours ahead of the arrow keys, and what a
            // sighted helper read off the screen was not what the screen reader said.
            int barSeconds = ChartMath.BarSecondsOf(visibleData);
            DateTime Shown(DateTime stamp) => Accessibility.SpeechTimeFormatter.ToBarDisplay(stamp, barSeconds);

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
                var d = Shown(visibleData[dIdx].Date);

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

                float labelRight = x + _textFont.MeasureText(label);
                if (Clear(x, labelRight))
                    canvas.DrawText(label, x, textY, SKTextAlign.Left, _textFont, _textPaint);
                lastLabelRight = labelRight;
            }

            // The final bar, right-aligned at its own right edge (or the strip's, whichever is
            // nearer), unless a slot already named it or there is no room beside the last label.
            int lastIndex = visibleData.Count - 1;
            if (lastIndex > lastLabelledIndex)
            {
                float lastBarRight = Math.Min(rect.Right, rect.Left + (lastIndex + 1) * itemWidth);
                var d = Shown(visibleData[lastIndex].Date);
                string label = d.ToString(primaryFormat);
                if (markDateBoundaries && prevLabelDate.HasValue && d.Date != prevLabelDate.Value.Date)
                    label = d.ToString("MM/dd ") + label;
                float labelLeft = lastBarRight - (2 * density) - _textFont.MeasureText(label);
                if (label != prevLabel && labelLeft > lastLabelRight + (8 * density)
                    && Clear(labelLeft, lastBarRight - (2 * density)))
                    canvas.DrawText(label, lastBarRight - (2 * density), textY, SKTextAlign.Right, _textFont, _textPaint);
            }
        }

        // (An orphaned second <summary> sat here — RenderCrosshair's, left behind when that method
        // moved above this one. Two doc comments on one member: the compiler takes the last and
        // silently drops the first, so the tooltip described a method that was not this one.)
        /// <summary>
        /// One pane's crosshair value badge: the value, where its line is drawn, the colour of
        /// the thing it reads, and the pane range that decides its decimals.
        ///
        /// <para>
        /// A value of its own so the axis (which keeps its labels out from under the badge) and
        /// the crosshair (which draws it) are handed the SAME badge. They used to be two copies
        /// of one lookup in two methods, kept in step by a comment.
        /// </para>
        /// </summary>
        /// <param name="LabelY">Where the badge is drawn: <see cref="BadgeCentre"/>, its value's row kept
        /// inside its pane. NaN draws it on <paramref name="LineY"/>.</param>
        internal readonly record struct CrosshairBadge(double Value, float LineY, SKColor Chip, double Range, float LabelY = float.NaN)
        {
            /// <summary>The badge's vertical centre as drawn.</summary>
            public float Centre => float.IsNaN(LabelY) ? LineY : LabelY;

            /// <summary>The baseline the badge's text is drawn at.</summary>
            public float TextY(float density) => Centre + (4 * density);
        }

        /// <summary>The component an indicator pane's crosshair badge reads, and its value there.</summary>
        internal readonly record struct CrosshairReading(
            double Value, ChartSeries Series, ComponentConfig Component, int AbsIndex);

        /// <summary>The value an indicator pane's crosshair badge shows. See <see cref="CrosshairReadingAt"/>.</summary>
        internal static double? CrosshairValueAt(
            IReadOnlyList<ChartSeries> paneSeries, int localIndex, int viewportStart)
            => CrosshairReadingAt(paneSeries, localIndex, viewportStart)?.Value;

        /// <summary>
        /// WHOSE value an indicator pane's crosshair badge shows at the cursor's bar, or null when
        /// nothing drawn in the pane has a value there.
        ///
        /// <para>
        /// <b>The badge named nothing (Cody's 2026-10-15 screenshots).</b> It used to read the
        /// FIRST component in the pane with a value — declaration order, hidden components
        /// included. Cipher B declares two hidden anchor waves and its histogram ahead of the
        /// Wave Trend lines, so the badge said 55.70 while both lines sat near −40 — and a
        /// sighted reader takes a number on the line's axis for the line. Now, in order:
        /// </para>
        /// <list type="number">
        /// <item>The component keyboard navigation is ON, when it is in this pane and drawn here
        /// with a value: the badge follows what Cody is reading, so a sighted companion watching
        /// his screen sees the same number his screen reader says.</item>
        /// <item>Otherwise the pane's first continuous LINE (line, oscillator, area, step line):
        /// the trace a reader's eye follows to the axis.</item>
        /// <item>Then a bar or histogram (a volume pane has nothing else), then anything else
        /// drawn with a value.</item>
        /// </list>
        /// <para>
        /// Never a hidden or disabled component — the badge cannot label a line that is not on
        /// the screen — and never one drawn in a sub-pane strip, whose values map against that
        /// strip's own range, not the pane's. The badge also carries the component's colour as a
        /// chip (<see cref="ReadingColor"/>), so it says whose value it is as well as what.
        /// </para>
        ///
        /// <para><paramref name="localIndex"/> is VIEWPORT-LOCAL and component arrays are
        /// ABSOLUTE, so this is where the two index spaces meet. The crosshair label used to
        /// read <c>data[localIndex]</c> straight: pan back to <c>ViewportStartIndex = 500</c>,
        /// put the cursor on bar 560, and the RSI pane's line and its number came from
        /// <c>rsi[60]</c> — a reading from five hundred bars ago, rendered as the current one.
        /// Every other renderer path indexes components as <c>ViewportStart + i</c>.</para>
        /// </summary>
        internal static CrosshairReading? CrosshairReadingAt(
            IReadOnlyList<ChartSeries> paneSeries, int localIndex, int viewportStart,
            string? focusedSeriesId = null, int focusedComponentIndex = -1)
        {
            int absIndex = localIndex + viewportStart;
            if (absIndex < 0) return null;

            CrosshairReading? Read(ChartSeries s, ComponentConfig comp)
            {
                if (ReadingTier(comp) < 0) return null;
                var data = s.GetComponentData(comp.Name);
                return data != null && absIndex < data.Length && !double.IsNaN(data[absIndex])
                    ? new CrosshairReading(data[absIndex], s, comp, absIndex)
                    : null;
            }

            if (focusedSeriesId != null && focusedComponentIndex >= 0)
            {
                foreach (var s in paneSeries)
                {
                    if (s.Id != focusedSeriesId) continue;
                    int ci = s.ClampComponent(focusedComponentIndex);
                    if (ci >= 0 && Read(s, s.Components[ci]) is { } focused) return focused;
                }
            }

            for (int tier = 0; tier <= 2; tier++)
                foreach (var s in paneSeries)
                    foreach (var comp in s.Components)
                        if (ReadingTier(comp) == tier && Read(s, comp) is { } reading)
                            return reading;
            return null;
        }

        /// <summary>
        /// How readable a component's value is as "the pane's value": 0 for a continuous line,
        /// 1 for bars, 2 for markers and levels, −1 for what the badge must not read at all —
        /// hidden, disabled, in a sub-pane strip, or a type with no value of its own on this axis.
        /// </summary>
        /// <param name="subPane">Null for the pane's own area (a component in a sub-pane strip
        /// reads −1 there); a strip's name for that strip, where only its components read.</param>
        private static int ReadingTier(ComponentConfig comp, string? subPane = null)
        {
            if (!comp.IsVisible || !comp.IsEnabled) return -1;
            if (!string.Equals(comp.SubPaneName ?? "", subPane ?? "", StringComparison.Ordinal)) return -1;
            return TypeTier(comp);
        }

        /// <summary>
        /// Whether a component is of a kind that carries a value label on its axis at all — a line
        /// or a bar plot (<see cref="ScaleLabelsFor"/>'s tiers 0 and 1) — whatever its visibility
        /// and its <see cref="ComponentConfig.ShowAxisLabel"/> switch. The Properties dialog offers
        /// the switch only where it does something.
        /// </summary>
        public static bool CanHaveAxisLabel(ComponentConfig comp) => TypeTier(comp) is 0 or 1;

        /// <summary><see cref="ReadingTier"/> by kind alone.</summary>
        private static int TypeTier(ComponentConfig comp)
        {
            return comp.DisplayType switch
            {
                ComponentDisplayType.Line or ComponentDisplayType.Oscillator or ComponentDisplayType.Area
                    or ComponentDisplayType.Gradient or ComponentDisplayType.StepLine => 0,
                ComponentDisplayType.Bar or ComponentDisplayType.Histogram
                    or ComponentDisplayType.ZeroArea => 1,
                ComponentDisplayType.Cloud or ComponentDisplayType.CandleColor or ComponentDisplayType.Heatmap
                    or ComponentDisplayType.Profile or ComponentDisplayType.Distribution => -1,
                // A window's value is a count to hear, not a position.
                ComponentDisplayType.Window => -1,
                // A number printed at the bar is not a position on this axis.
                ComponentDisplayType.ValueLabel when comp.MarkerAnchor != MarkerAnchor.Value => -1,
                _ => 2,
            };
        }

        /// <summary>
        /// The colour the reading's component is drawn in AT that value, for the badge's chip —
        /// so the chip matches the stroke or bar the eye is following, and the legend row for it.
        /// A bar takes its up or down colour from the same rule the bars are painted by
        /// (<see cref="StandardRenderers.DirectionalBarColors"/>); a polarity-split line takes the
        /// side of its baseline it is on. Opaque, because a chip is a key, not a fill.
        /// </summary>
        internal static SKColor ReadingColor(CrosshairReading r, ChartTheme theme, Ohlcv? bar)
        {
            var comp = r.Component;
            var data = r.Series.GetComponentData(comp.Name);

            if (StandardRenderers.ResolveBarColor(comp, data, r.AbsIndex) is { } ruled)
                return ruled.WithAlpha(255);

            if (comp.DisplayType == ComponentDisplayType.ValueLabel)
                return Rendering.ValueLabelRenderer.RawColour(comp, r.Series, r.AbsIndex, data?.Length ?? 0);

            if (comp.DisplayType is ComponentDisplayType.Bar or ComponentDisplayType.Histogram)
            {
                var (up, down) = StandardRenderers.DirectionalBarColors(theme, comp);
                bool isUp = comp.ColorSource == ColorSource.PriceAction
                    ? bar is not { } b || b.Close >= b.Open
                    : r.Value >= comp.ColorBaseline;
                return (isUp ? up : down).WithAlpha(255);
            }

            // A line coloured against a sibling (Cipher B's Stochastic, red above its RSI line)
            // reads in the colour its line is drawn in at this bar.
            if (StandardRenderers.ColorAgainstSibling(comp, r.Series, r.AbsIndex, r.Value) is { } sibling)
                return sibling.WithAlpha(255);

            if (!SKColor.TryParse(comp.ColorHex, out var color)) color = SKColors.White;
            if (StandardRenderers.WantsPolaritySplit(comp))
                color = StandardRenderers.PolarityColor(comp, r.Value, color);
            return color.WithAlpha(255);
        }

        private void RenderCrosshair(SKCanvas canvas, SKRect area, List<Ohlcv> visibleData, int localIndex, float itemWidth, float density, SKRect mainPaneRect, CrosshairBadge? mainBadge, List<(SKRect Rect, CrosshairBadge? Badge)> indicatorPanes)
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

            using var labelBgPaint = new SKPaint { Color = new SKColor(40, 40, 40, 210), Style = SKPaintStyle.Fill };

            // Horizontal crosshair in main pane (price), at the close — with its badge.
            if (mainBadge is { } mb)
            {
                canvas.DrawLine(area.Left, mb.LineY, area.Right, mb.LineY, haloPaint);
                canvas.DrawLine(area.Left, mb.LineY, area.Right, mb.LineY, vPaint);
                DrawCrosshairBadge(canvas, mainPaneRect.Right, mb, density, labelBgPaint);
            }

            // Horizontal crosshair in each indicator pane at the cursor's indicator value
            using var indPaint = new SKPaint { Color = SKColors.Gray.WithAlpha(100), StrokeWidth = 1 * density, Style = SKPaintStyle.Stroke };
            foreach (var (paneRect, badge) in indicatorPanes)
            {
                if (badge is not { } b) continue;
                canvas.DrawLine(paneRect.Left, b.LineY, paneRect.Right, b.LineY, haloPaint);
                canvas.DrawLine(paneRect.Left, b.LineY, paneRect.Right, b.LineY, indPaint);
                DrawCrosshairBadge(canvas, paneRect.Right, b, density, labelBgPaint);
            }
        }

        /// <summary>Width of the colour chip at the left of a crosshair badge, at density 1.</summary>
        internal const float BadgeChipPx = 3f;

        /// <summary>
        /// A value badge at the right edge of a pane, in the axis strip: a chip in the colour of
        /// the thing it reads, then the reading.
        ///
        /// <para>
        /// The CHIP is what makes it say whose value it is. The strip is 60 px wide, so a name does
        /// not fit beside a five-digit reading; a colour does, and it is the same colour as the
        /// line or bar the eye is following and as that component's legend key (Cody's 2026-10-15
        /// screenshots: an unlabelled 55.70 beside two lines near −40).
        /// </para>
        ///
        /// <para>
        /// Fits the strip like RenderYAxis's labels do — this badge is drawn INTO the axis strip
        /// too, so a nine-digit volume reading overflowed the canvas here as well — with the chip
        /// and its gap taken out of the budget first.
        /// </para>
        /// </summary>
        private void DrawCrosshairBadge(SKCanvas canvas, float paneRight, CrosshairBadge badge, float density, SKPaint bgPaint)
        {
            float chipW = BadgeChipPx * density;
            float gap = 2 * density;
            float pad = 3 * density;
            float lx = paneRight + (2 * density);

            string label = FitAxisLabel(ChartMath.FormatAxisReading(badge.Value, badge.Range), badge.Value,
                                        _axisWidth - (2 * density) - chipW - gap - pad);
            float labelW = _textFont.MeasureText(label);
            float labelH = _textFont.Size + (4 * density);
            float ly = badge.Centre - (labelH / 2);

            canvas.DrawRect(new SKRect(lx, ly, lx + chipW + gap + labelW + pad, ly + labelH), bgPaint);
            using (var chipPaint = new SKPaint { Color = badge.Chip, Style = SKPaintStyle.Fill })
                canvas.DrawRect(new SKRect(lx, ly, lx + chipW, ly + labelH), chipPaint);
            canvas.DrawText(label, lx + chipW + gap, badge.TextY(density), SKTextAlign.Left, _textFont, _textPaint);
        }

        /// <summary>
        /// Whether this renderer paints the panes' status lines into the image itself.
        ///
        /// <para>
        /// <b>Off for the live chart.</b> Cody's 2026-10-17 decision: TradingView status line. The
        /// boxed legend this renderer used to paint at each pane's top left is retired; on the live
        /// chart (both heads) ChartArea draws the status lines as real DOM text over the image,
        /// which a magnifier, browser zoom, the app's text size and user CSS can reach and canvas
        /// pixels cannot. On for an off-screen snapshot nothing overlays (the AI analyst's chart
        /// image), so the picture still says what its lines are. Same model either way
        /// (<see cref="ChartStatusLine"/>).
        /// </para>
        /// </summary>
        public bool PaintsStatusLines { get; set; }

        /// <summary>The status lines' backing opacity when this renderer paints them, and when it measures the price pane's block.</summary>
        public byte StatusBackingAlpha { get; set; } = ChartStatusLine.DefaultBackingAlpha;

        /// <summary>
        /// A pane's status block at <paramref name="absIndex"/> and the STATUS BAND it takes at the
        /// pane's top, in device pixels: the lines the pane has room for once its plot keeps its
        /// floor (<see cref="ChartStatusLine.PaneLineBudget"/>), and the band they need
        /// (<see cref="ChartStatusLine.BandHeight"/>) — 0 when the pane shows none. Measured in
        /// the DOM's units scaled to this bitmap (<see cref="ChartStatusLine.BandMetrics"/>), so
        /// the band is the block ChartArea draws there, at every text size.
        /// </summary>
        internal static (PaneStatus Status, float Band) StatusBandFor(string paneName, IReadOnlyList<ChartSeries> paneSeries, int absIndex,
            ChartStatusLine.Context ctx, SKRect paneRect, float density, float textScale, SKFont font)
        {
            var (line, pad, minPlot) = ChartStatusLine.BandMetrics(density, textScale);
            int budget = ChartStatusLine.PaneLineBudget(paneRect.Height, line, pad, minPlot);
            var status = ChartStatusLine.Build(paneName, paneSeries, absIndex, ctx,
                paneRect.Height, paneRect.Width * ChartStatusLine.MaxWidthFraction,
                line, t => font.MeasureText(t), budget);
            return (status, ChartStatusLine.BandHeight(status.Lines.Count, line, pad));
        }

        /// <summary>The part of a pane its values are mapped into: everything below its status band.</summary>
        internal static SKRect PlotRect(SKRect paneRect, float statusBand)
            => statusBand <= 0 ? paneRect
                : new SKRect(paneRect.Left, Math.Min(paneRect.Top + statusBand, paneRect.Bottom - 1), paneRect.Right, paneRect.Bottom);

        /// <summary>
        /// The rect a pane's status block covers, measured with the same pieces and gaps as
        /// <see cref="PaintStatusBlock"/> paints — what the formation layer keeps its labels out of.
        /// </summary>
        private static SKRect StatusBlockRect(SKRect paneRect, PaneStatus status, float density, float textScale, SKFont font)
        {
            var (line, pad, _) = ChartStatusLine.BandMetrics(density, textScale);
            float widest = status.Lines.Select(l => StatusLineWidth(l, font, density)).DefaultIfEmpty(0).Max();
            return new SKRect(paneRect.Left + pad, paneRect.Top + pad,
                paneRect.Left + pad + widest, paneRect.Top + pad + status.Lines.Count * line);
        }

        private static float StatusLineWidth(StatusLine l, SKFont font, float density)
        {
            float gap = font.MeasureText("M") * 0.8f, pad = ChartStatusLine.PadPx * density;
            float w = pad + font.MeasureText(l.Title);
            if (l.Inputs.Length > 0) w += gap + font.MeasureText(l.Inputs);
            foreach (var v in l.Values)
                w += gap + (v.Label.Length > 0 ? font.MeasureText(v.Label + " ") : 0) + font.MeasureText(v.Text)
                     + (v.Chip ? font.MeasureText("M") : 0);
            if (l.HiddenValues > 0) w += gap + font.MeasureText($"+{l.HiddenValues}");
            if (l.Note.Length > 0) w += gap + font.MeasureText(l.Note);
            return w + pad;
        }

        /// <summary>
        /// Paints a pane's status lines into the image: one line per indicator at the pane's top
        /// left, each on its own backing strip, the name and inputs in the theme's ink, each
        /// reading in its plot's colour (or the ink beside a chip of that colour, when the colour
        /// is too faint on the backing). The DOM painter in ChartArea draws the same model.
        /// </summary>
        private void PaintStatusBlock(SKCanvas canvas, SKRect paneRect, PaneStatus status, float density, float textScale, SKFont font)
        {
            var theme = _theme.Current;
            var (line, pad, _) = ChartStatusLine.BandMetrics(density, textScale);
            float gap = font.MeasureText("M") * 0.8f;
            var inkColor = ChartStatusLine.Ink(theme);
            var muted = inkColor.WithAlpha(190);
            using var bg = new SKPaint { Color = theme.SurfaceSunken.WithAlpha(StatusBackingAlpha), Style = SKPaintStyle.Fill, IsAntialias = true };
            using var ink = new SKPaint { IsAntialias = true };
            using var chip = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };

            float y = paneRect.Top + pad;
            foreach (var l in status.Lines)
            {
                float x = paneRect.Left + pad;
                float w = StatusLineWidth(l, font, density);
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + w, y + line - density), 3 * density), bg);
                float baseline = y + line * 0.72f;
                x += pad;

                void Text(string t, SKColor c)
                {
                    ink.Color = c;
                    canvas.DrawText(t, x, baseline, SKTextAlign.Left, font, ink);
                    x += font.MeasureText(t);
                }

                Text(l.Title, inkColor);
                if (l.Inputs.Length > 0) { x += gap; Text(l.Inputs, muted); }
                foreach (var v in l.Values)
                {
                    x += gap;
                    if (v.Label.Length > 0) Text(v.Label + " ", muted);
                    if (v.Chip)
                    {
                        float s = font.Size * 0.6f;
                        chip.Color = v.Color;
                        canvas.DrawRect(SKRect.Create(x, baseline - s, s, s), chip);
                        x += font.MeasureText("M");
                    }
                    Text(v.Text, v.Chip ? inkColor : v.Color);
                }
                if (l.HiddenValues > 0) { x += gap; Text($"+{l.HiddenValues}", muted); }
                if (l.Note.Length > 0) { x += gap; Text(l.Note, muted); }
                y += line;
            }
        }

        /// <summary>
        /// The price-pane series that get a status line (they had a row in the retired legend) and
        /// a value label on the price axis.
        ///
        /// <para>
        /// Left out: the candles and the volume (they ARE the chart — a key reading "Candles"
        /// names nothing a reader could miss), the liquidity heatmap, and every volume-profile
        /// series (VPVR / VPFR / TPO / any <c>IsProfile</c>), which is its own visual element
        /// drawn by ProfileRenderLayer rather than an overlay.
        /// </para>
        ///
        /// <para>
        /// <b>The Price series is NOT left out, and used to be.</b> On an OHLCV chart it is a white
        /// line through every close, drawn on top of the candles (CoreIndicatorProvider: #FFFFFF,
        /// seeded on every OHLCV chart by WorkspaceInitializer). Cody's 2026-10-15 screenshots show
        /// it plainly, with a legend reading only "Market Structure — 4 marks" — and in focus mode
        /// no legend at all — so a sighted companion asking "what is the white line?" had nothing
        /// on screen to answer with. It is a real, navigable, spoken series, so the fix is to name
        /// it, not to stop drawing it (pinned by PriceLineLegendTests).
        /// </para>
        /// </summary>
        internal static List<ChartSeries> MainPaneLegendSeries(IEnumerable<ChartSeries> mainSeries) =>
            mainSeries
                .Where(s => !s.IsProfile)
                .Where(s => s.IndicatorCode?.ToUpperInvariant() is not ("CANDLES" or "VOLUME" or "HEATMAP"))
                .ToList();

        /// <summary>
        /// The axis label that fits the strip: the full number where it fits, and the spoken
        /// short form (<c>120M</c>) where it does not.
        ///
        /// <para>
        /// Not an ellipsis (the retired legend's Ellipsize), and the difference matters: a truncated number is a
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

        /// <summary>Discrete per-bar glyphs — the components that identify themselves by shape.</summary>
        internal static bool IsMarker(ComponentDisplayType t) => t is
            ComponentDisplayType.Dot or ComponentDisplayType.ZeroDot or ComponentDisplayType.GradientDot or
            ComponentDisplayType.Diamond or ComponentDisplayType.Square or ComponentDisplayType.Cross or
            ComponentDisplayType.CheckBox or ComponentDisplayType.RefreshBox or
            ComponentDisplayType.TriangleUp or ComponentDisplayType.TriangleUpRing or
            ComponentDisplayType.TriangleDown or ComponentDisplayType.Arrow;

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
