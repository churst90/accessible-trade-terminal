using SkiaSharp;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Rendering
{
    public class DataLayer : IRenderLayer
    {
        private readonly IStylingService _stylingService;
        private readonly HeatmapRenderer _heatmapRenderer;

        public DataLayer(IStylingService stylingService)
        {
            _stylingService = stylingService;
            _heatmapRenderer = new HeatmapRenderer();
        }

        public void Render(RenderContext ctx, IEnumerable<ChartSeries> series)
        {
            // Materialise once so we can iterate multiple times.
            var seriesList = series as IReadOnlyList<ChartSeries> ?? series.ToList();

            // ── Pre-scan: detect any active CandleColor overlay so candle rendering can apply
            //             per-bar sentiment phase colors and suppress wicks.
            //             Only applies in the main-pane pass (SubPaneFilter == null).
            double[]? candlePhaseData = null;
            if (ctx.SubPaneFilter == null)
            {
                foreach (var os in seriesList)
                {
                    if (!os.IsVisible) continue;
                    string osPane = string.IsNullOrEmpty(os.Pane) ? "Main" : os.Pane;
                    if (osPane != ctx.PaneName) continue;
                    var phaseComp = os.Components.FirstOrDefault(c =>
                        c.DisplayType == ComponentDisplayType.CandleColor &&
                        c.IsVisible && c.IsEnabled != false);
                    if (phaseComp != null)
                    {
                        candlePhaseData = os.GetComponentData(phaseComp.Name);
                        break;
                    }
                }
            }

            // ── Pass 0: BACKGROUND FILLS — zone bands and cloud fills rendered first so they
            //            always sit behind candles and all other indicator components.
            // Sub-pane pass skips these (they are main-area-only).
            if (ctx.SubPaneFilter == null)
            {
                foreach (var s in seriesList)
                {
                    if (!s.IsVisible) continue;
                    string sPane = string.IsNullOrEmpty(s.Pane) ? "Main" : s.Pane;
                    if (sPane != ctx.PaneName) continue;

                    // Zone bands first (widest, most background-like fills).
                    foreach (var band in s.Config.ZoneBands)
                    {
                        if (!band.IsVisible) continue;
                        StandardRenderers.RenderZoneBand(ctx, s, band);
                    }

                    // Cloud fills second (Ichimoku, WT Fill, EMA Fill, etc.).
                    foreach (var fill in s.Config.CloudFills)
                    {
                        if (!fill.IsVisible) continue;
                        StandardRenderers.RenderCloudFill(ctx, s, fill);
                    }
                }
            }

            foreach (var s in seriesList)
            {
                if (!s.IsVisible) continue;

                // Allow series with empty/null pane to render in 'Main'
                string sPane = string.IsNullOrEmpty(s.Pane) ? "Main" : s.Pane;
                if (sPane != ctx.PaneName) continue;

                // 1. HEATMAP (Special Property)
                if (s.Components.Any(c => c.DisplayType == ComponentDisplayType.Heatmap))
                {
                    _heatmapRenderer.Render(ctx, s);
                    continue;
                }

                // 2. STANDARD COMPONENTS
                // Painted in DrawOrder (stable: ties keep the declared order). The declared order
                // is the keyboard's and the speech's; DrawOrder only moves what is painted over
                // what. Cipher B declares its WT Histogram before the waves (a navigation stop Cody
                // knows) but Market Cipher B paints that "VWAP" area OVER the waves.
                foreach (var comp in InDrawOrder(s.Components))
                {
                    if (!comp.IsVisible || comp.IsEnabled == false) continue;
                    // Heard, not painted (ComponentConfig.IsDrawn): its data, speech and narration
                    // stay; the chart draws nothing for it.
                    if (!comp.IsDrawn) continue;

                    // Sub-pane filter: null = main-area pass (skip sub-pane components);
                    //                  string = sub-pane pass (skip components not in that strip).
                    bool isSubPaneComp = !string.IsNullOrEmpty(comp.SubPaneName);
                    if (ctx.SubPaneFilter == null && isSubPaneComp) continue;
                    if (ctx.SubPaneFilter != null && comp.SubPaneName != ctx.SubPaneFilter) continue;

                    using var paint = _stylingService.GetPaint(comp, ctx.Density);

                    if (s.Id == CoreSeriesIds.Candles && comp.Name == "body")
                    {
                        // Candles body renders as real OHLCV candlesticks. Analytics
                        // providers (single-value) no longer reach this branch: after the
                        // Phase 4 refactor they seed the PRICE series instead of CANDLES,
                        // so there's nothing degenerate to detect or re-route here.
                        StandardRenderers.RenderCandles(ctx, s, paint, candlePhaseData);
                    }
                    else
                    {
                        switch (comp.DisplayType)
                        {
                            case ComponentDisplayType.Line:
                            case ComponentDisplayType.Area:
                            case ComponentDisplayType.Oscillator:
                            case ComponentDisplayType.Gradient:
                                StandardRenderers.RenderLine(ctx, s, comp, paint);
                                break;
                            case ComponentDisplayType.Bar:
                            case ComponentDisplayType.Histogram:
                                // All bars are directionally colored: candle-direction (ColorSource.PriceAction,
                                // e.g. volume) or value-sign (ColorSource.Value, e.g. MACD histogram).
                                // The renderer selects the correct rule from comp.ColorSource.
                                StandardRenderers.RenderDirectionalBars(ctx, s, comp);
                                break;
                            case ComponentDisplayType.Dot:
                                StandardRenderers.RenderDot(ctx, s, comp, paint);
                                break;
                            case ComponentDisplayType.ZeroDot:
                                StandardRenderers.RenderZeroDot(ctx, s, comp);
                                break;
                            case ComponentDisplayType.ZeroArea:
                                StandardRenderers.RenderZeroArea(ctx, s, comp);
                                break;
                            case ComponentDisplayType.Arrow:
                                StandardRenderers.RenderArrow(ctx, s, comp, paint);
                                break;
                            case ComponentDisplayType.StepLine:
                                StandardRenderers.RenderStepLine(ctx, s, comp, paint);
                                break;
                            case ComponentDisplayType.TriangleUp:
                                StandardRenderers.RenderTriangleUp(ctx, s, comp, paint);
                                break;
                            case ComponentDisplayType.TriangleUpRing:
                                StandardRenderers.RenderTriangleUpRing(ctx, s, comp, paint);
                                break;
                            case ComponentDisplayType.TriangleDown:
                                StandardRenderers.RenderTriangleDown(ctx, s, comp, paint);
                                break;
                            case ComponentDisplayType.Diamond:
                                StandardRenderers.RenderDiamond(ctx, s, comp, paint);
                                break;
                            case ComponentDisplayType.Square:
                                StandardRenderers.RenderSquare(ctx, s, comp, paint);
                                break;
                            case ComponentDisplayType.CheckBox:
                                StandardRenderers.RenderCheckBox(ctx, s, comp, paint);
                                break;
                            case ComponentDisplayType.RefreshBox:
                                StandardRenderers.RenderRefreshBox(ctx, s, comp, paint);
                                break;
                            case ComponentDisplayType.Cross:
                                StandardRenderers.RenderCross(ctx, s, comp, paint);
                                break;
                            case ComponentDisplayType.GradientDot:
                                StandardRenderers.RenderGradientDot(ctx, s, comp);
                                break;
                            case ComponentDisplayType.ValueLabel:
                                ValueLabelRenderer.Render(ctx, s, comp);
                                break;
                            case ComponentDisplayType.CandleColor:
                                // Handled by the pre-scan above: the phase array is passed to
                                // RenderCandles which applies it to the main candle series.
                                // No per-component rendering needed here.
                                break;
                            default:
                                // Intentional no-op: unknown display types are silently skipped
                                // for forward compatibility (e.g. new types added by plugins).
                                break;
                        }
                        // A marker's printed number (Cipher C's 49D over a daily low), drawn
                        // after the marker on the other side of the bar.
                        if (ValueLabelRenderer.HasCompanionLabel(s, comp))
                            ValueLabelRenderer.Render(ctx, s, comp);
                    }
                }

                // 3. LEVELS — main-area-only (no sub-pane pass renders them).
                // Cloud fills and zone bands are already rendered in Pass 0 above.
                if (ctx.SubPaneFilter != null) continue;

                foreach (var lvl in s.Levels)
                {
                    if (!lvl.IsVisible || !lvl.IsDrawn) continue;   // an undrawn level is heard, not painted

                    float y = ChartMath.MapY(lvl.Value, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                    var pathEffect = lvl.DashStyle switch
                    {
                        DashStyle.Dash    => SKPathEffect.CreateDash(new[] { 8f, 4f }, 0),
                        DashStyle.Dot     => SKPathEffect.CreateDash(new[] { 2f, 4f }, 0),
                        DashStyle.DashDot => SKPathEffect.CreateDash(new[] { 8f, 4f, 2f, 4f }, 0),
                        _                 => null
                    };
                    using var lvlPaint = new SKPaint
                    {
                        Color      = SKColor.Parse(lvl.ColorHex),
                        StrokeWidth = lvl.Thickness,
                        Style      = SKPaintStyle.Stroke,
                        PathEffect = pathEffect
                    };
                    ctx.Canvas.DrawLine(0, y, ctx.Width, y, lvlPaint);
                    pathEffect?.Dispose();
                }
            }
        }

        /// <summary>The components in paint order: ascending <see cref="ComponentConfig.DrawOrder"/>,
        /// declared order within a tie. Every component has DrawOrder 0 unless its provider says
        /// otherwise, so for everything else this IS the declared order.</summary>
        internal static IEnumerable<ComponentConfig> InDrawOrder(IReadOnlyList<ComponentConfig> components)
        {
            bool any = false;
            for (int i = 0; i < components.Count; i++) if (components[i].DrawOrder != 0) { any = true; break; }
            if (!any) return components;
            return components.Select((c, i) => (c, i)).OrderBy(t => t.c.DrawOrder).ThenBy(t => t.i).Select(t => t.c);
        }
    }
}
