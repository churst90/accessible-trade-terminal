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

            // The COMPARE overlay is the one line whose VALUES the scale mode changes: in Percent
            // and Indexed it is redrawn at chartBase × v / vFirst, its own first visible value,
            // so the two symbols start at the same point and drift apart. Swap in a drawing copy
            // here, once, and every renderer below maps the rebased price with unchanged maths.
            // The state's own series (speech, status line, legends) keep raw prices.
            if (ctx.EffectiveScaleMode != PriceScaleMode.Regular)
                seriesList = seriesList.Select(s => RebaseCompareForDrawing(s, ctx) ?? s).ToList();

            // ── Pre-scan: detect any active CandleColor overlay so candle rendering can apply
            //             per-bar sentiment phase colors and suppress wicks.
            double[]? candlePhaseData = null;
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
            {
                foreach (var s in seriesList)
                {
                    if (!s.IsVisible) continue;
                    string sPane = string.IsNullOrEmpty(s.Pane) ? "Main" : s.Pane;
                    if (sPane != ctx.PaneName) continue;

                    // Zone bands first (widest, most background-like fills).
                    foreach (var band in s.Config.ZoneBands)
                    {
                        if (!band.IsVisible || !SignShown(s, band)) continue;
                        StandardRenderers.RenderZoneBand(ctx, s, band);
                    }

                    // Cloud fills second (Ichimoku, EMA Fill, etc.).
                    foreach (var fill in s.Config.CloudFills)
                    {
                        if (!DrawsFill(s.Config, fill)) continue;
                        StandardRenderers.RenderCloudFill(ctx, s, fill);
                    }

                    // A component's boxes and full-height lines last (Cipher C's translation
                    // boxes, live timing windows and dotted lines at its lows and tops).
                    foreach (var comp in s.Components)
                    {
                        if (!comp.IsVisible || comp.IsEnabled == false || !comp.IsDrawn) continue;
                        if (comp.BoxAlpha > 0) ComponentPictures.RenderBoxes(ctx, s, comp);
                        if (comp.MarkLineAlpha > 0) ComponentPictures.RenderMarkLines(ctx, s, comp);
                    }
                }
            }

            foreach (var s in seriesList)
            {
                if (!s.IsVisible) continue;

                // Allow series with empty/null pane to render in 'Main'
                string sPane = string.IsNullOrEmpty(s.Pane) ? "Main" : s.Pane;
                if (sPane != ctx.PaneName) continue;

                // The Price series is the identical close line in white, drawn over the chart
                // type's own line. On Line and Area it would hide the blue entirely, so its
                // line is skipped there — speech, navigation, sonification and its status line
                // are untouched, and its reference levels are still drawn below. A Price whose
                // mapping is not the close (say, high) is a different line and still draws.
                if (IsClosePriceLineHiddenByChartStyle(s, ctx))
                {
                    RenderLevels(ctx, s);
                    continue;
                }

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
                foreach (var declared in InDrawOrder(s.Components))
                {
                    if (!declared.IsVisible || declared.IsEnabled == false) continue;
                    // Heard, not painted (ComponentConfig.IsDrawn): its data, speech and narration
                    // stay; the chart draws nothing for it.
                    if (!declared.IsDrawn) continue;
                    // Market Cipher B's waves opaque where the theme cannot show them translucent.
                    var comp = ThemeAdaptedColours.Component(declared, ctx.Theme);

                    using var paint = _stylingService.GetPaint(comp, ctx.Density);

                    if (s.Id == CoreSeriesIds.Candles && comp.Name == "body")
                    {
                        // Candles body renders as real OHLCV candlesticks. Analytics
                        // providers (single-value) no longer reach this branch: after the
                        // Phase 4 refactor they seed the PRICE series instead of CANDLES,
                        // so there's nothing degenerate to detect or re-route here.
                        //
                        // Line and Area are the same series DRAWN differently: one line through
                        // the raw closes, plus the fill for Area. No bodies, no wicks, and the
                        // phase colouring (a property of candles) has nothing to colour.
                        if (ctx.ChartStyle is PriceChartStyle.Line or PriceChartStyle.Area)
                            StandardRenderers.RenderPriceLine(ctx, area: ctx.ChartStyle == PriceChartStyle.Area);
                        else
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
                                // A filled wave painted as a filled histogram is (Cipher B's
                                // Money Flow and Wave Gap), one area per side.
                                if (comp.AreaBySign && comp.IsAreaFillInEffect())
                                    StandardRenderers.RenderDirectionalBars(ctx, s, comp);
                                else
                                    StandardRenderers.RenderLine(ctx, s, comp, paint);
                                break;
                            case ComponentDisplayType.Bar:
                            case ComponentDisplayType.Histogram:
                                // All bars are directionally colored: candle-direction (ColorSource.PriceAction,
                                // e.g. volume) or value-sign (ColorSource.Value, e.g. MACD histogram).
                                // The renderer selects the correct rule from comp.ColorSource.
                                // A wave shown to the screen reader as a histogram keeps the
                                // wave's picture (AreaBySign): unfilled, it is the wave's line.
                                if (comp.AreaBySign && !comp.IsAreaFillInEffect())
                                    StandardRenderers.RenderLine(ctx, s, comp, paint);
                                else
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
                            case ComponentDisplayType.Window:
                                // Shaded by BackgroundTintPainter where it is open; its value is
                                // a count to hear, not a position. Its picture, if any, is below.
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
                        // A marker's printed number (Cipher C's 49D beside a daily low), drawn
                        // after the marker, to its right.
                        if (ValueLabelRenderer.HasCompanionLabel(s, comp))
                            ValueLabelRenderer.Render(ctx, s, comp);
                        // A picture the component carries (Cipher C's cycle wave on its window).
                        if (comp.DrawsPathInEffect())
                        {
                            if (!string.IsNullOrEmpty(comp.PathColorHex) && SKColor.TryParse(comp.PathColorHex, out var pathColour))
                                paint.Color = pathColour;
                            StandardRenderers.RenderPath(ctx, s, comp, paint);
                        }
                    }
                }

                // 3. LEVELS. Cloud fills and zone bands are already rendered in Pass 0 above.
                RenderLevels(ctx, s);
            }
        }

        /// <summary>
        /// A drawing copy of a COMPARE overlay with its values rebased onto this chart's price
        /// axis for the scale mode, or null when this is not the overlay / it has no visible
        /// value to rebase from. The ORIGINAL series is untouched, so speech and the status line
        /// keep prices and the sync path never sees a transformed array.
        /// </summary>
        internal static ChartSeries? RebaseCompareForDrawing(ChartSeries s, RenderContext ctx)
        {
            if (ctx.EffectiveScaleMode == PriceScaleMode.Regular) return null;
            if (!string.Equals(s.IndicatorCode, Indicators.SymbolCompareProvider.OverlayCode, StringComparison.OrdinalIgnoreCase))
                return null;
            if (!string.Equals(string.IsNullOrEmpty(s.Pane) ? "Main" : s.Pane, "Main", StringComparison.Ordinal))
                return null;

            var original = s.GetComponentData(Indicators.SymbolCompareProvider.CompLine);
            if (original.Length == 0) return null;
            double first = ChartMath.FirstVisibleValue(original, ctx.ViewportStart, ctx.ViewportLength);
            if (!ChartMath.BaseUsable(first) || !ChartMath.BaseUsable(ctx.ScaleBase)) return null;

            var copy = s.WithData(s.Data.Clone());
            var data = copy.GetComponentData(Indicators.SymbolCompareProvider.CompLine);
            for (int i = 0; i < data.Length; i++)
            {
                if (double.IsNaN(data[i])) continue;
                data[i] = ChartMath.RebaseCompareValue(ctx.ScaleBase, data[i], first);
            }
            return copy;
        }

        /// <summary>A series' reference levels, in its pane's coordinates.</summary>
        private static void RenderLevels(RenderContext ctx, ChartSeries s)
        {
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
                    Color      = SKColor.Parse(ThemeAdaptedColours.Level(lvl.ColorHex, ctx.Theme)),
                    StrokeWidth = lvl.Thickness,
                    Style      = SKPaintStyle.Stroke,
                    PathEffect = pathEffect
                };
                ctx.Canvas.DrawLine(0, y, ctx.Width, y, lvlPaint);
                pathEffect?.Dispose();
            }
        }

        /// <summary>
        /// The Price series is a second, identical line through the closes (white by default).
        /// Line and Area already draw that line in the chart type's colour, so on those types
        /// the Price series' ink is skipped when every visible part of it is mapped to the
        /// close. Anything else on it — a high mapping, a level, a component — still draws.
        /// </summary>
        internal static bool IsClosePriceLineHiddenByChartStyle(ChartSeries s, RenderContext ctx)
        {
            if (ctx.PaneName != "Main") return false;
            if (ctx.ChartStyle is not (PriceChartStyle.Line or PriceChartStyle.Area)) return false;
            if (s.Id != CoreSeriesIds.Price) return false;

            var mappings = s.Components
                .Where(c => c.IsVisible && c.IsDrawn)
                .Select(c => c.DataMapping)
                .ToList();
            return mappings.Count > 0
                && mappings.All(m => string.Equals(m, "close", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The components in paint order: ascending <see cref="ComponentConfig.DrawOrder"/>,
        /// declared order within a tie. Every component has DrawOrder 0 unless its provider says
        /// otherwise, so for everything else this IS the declared order.</summary>
        /// <summary>A cloud fill is drawn when it is visible and neither of its edges is a part the
        /// user left out (SeriesConfig.ExcludedComponents): the band goes with its edge.</summary>
        /// <summary>
        /// A band coloured by a component's sign (Cipher B's Money Flow Bar) is part of that
        /// component's picture: hidden, undrawn, switched off or left out, the band goes with it.
        /// Cody, 2026-10-30: "hiding the money flow should hide both".
        /// </summary>
        internal static bool SignShown(ChartSeries s, ZoneBandConfig band) =>
            string.IsNullOrEmpty(band.SignComponentName)
            || s.Components.FirstOrDefault(c => c.Name == band.SignComponentName) is { IsVisible: true, IsDrawn: true } c
               && c.IsEnabled != false;

        internal static bool DrawsFill(SeriesConfig config, CloudFillConfig fill) =>
            fill.IsVisible && !config.IsExcluded(fill.UpperComponentName) && !config.IsExcluded(fill.LowerComponentName);

        internal static IEnumerable<ComponentConfig> InDrawOrder(IReadOnlyList<ComponentConfig> components)
        {
            bool any = false;
            for (int i = 0; i < components.Count; i++) if (components[i].DrawOrder != 0) { any = true; break; }
            if (!any) return components;
            return components.Select((c, i) => (c, i)).OrderBy(t => t.c.DrawOrder).ThenBy(t => t.i).Select(t => t.c);
        }
    }
}
