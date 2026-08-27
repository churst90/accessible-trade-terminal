using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Centralized utility for common chart-related mathematical calculations.
    /// Ensures consistency across rendering, sonification, and accessibility systems.
    /// </summary>
    public static class ChartMath
    {
        /// <summary>
        /// Calculates the min/max range for a specific series within a viewport.
        /// </summary>
        public static (double Min, double Max) GetSeriesRange(ChartSeries series, int viewportStart, int viewportLength, (double Min, double Max) mainViewportRange)
        {
            // Primary price/candle series always use the shared global viewport range
            if (series.Pane == "Main") return mainViewportRange;

            double min = double.MaxValue, max = double.MinValue;
            bool hasData = false;

            foreach (var comp in series.Components)
            {
                var data = series.GetComponentData(comp.Name);
                if (data == null || data.Length == 0) continue;
                
                int end = Math.Min(viewportStart + viewportLength, data.Length);
                for (int i = viewportStart; i < end; i++)
                {
                    var val = data[i];
                    if (!double.IsNaN(val))
                    {
                        if (val < min) min = val;
                        if (val > max) max = val;
                        hasData = true;
                    }
                }
            }

            if (!hasData || min == double.MaxValue) return (0, 100);

            // VISUAL BUFFER: Add a small 10% margin to the top and bottom of the pane
            // so indicators don't touch the boundaries, improving accessibility legibility.
            if (Math.Abs(max - min) < 0.000001) { min -= 1.0; max += 1.0; }
            double buffer = (max - min) * 0.1;
            return (min - buffer, max + buffer);
        }

        /// <summary>
        /// Retrieves the value of a specific component at an index, falling back to OHLCV data for price series.
        /// Uses snapshots when provided to ensure thread-safety during background playback.
        /// </summary>
        public static double GetPointValue(ChartSeries series, Ohlcv point, int componentIndex, int dataIndex, double[]? componentDataSnapshot = null)
        {
            if (componentIndex < 0 || componentIndex >= series.Components.Count) return double.NaN;
            
            var component = series.Components[componentIndex];
            
            // Check snapshot first
            if (componentDataSnapshot != null && dataIndex >= 0 && dataIndex < componentDataSnapshot.Length)
            {
                return componentDataSnapshot[dataIndex];
            }
            
            // Check live data
            var data = series.GetComponentData(component.Name);
            if (data != null && dataIndex >= 0 && dataIndex < data.Length)
            {
                return data[dataIndex];
            }

            // MAPPING: Fallback for primary price series where components are virtual.
            // Maps candle parts (High, Low, etc.) to their logical names. Accepts both
            // the new snake_case machine names (body/upper_wick/lower_wick/line) and the
            // legacy display-style names (Candle Body/Upper Wick/Lower Wick/Close) so
            // saved workspaces predating the Phase 2 rename still resolve correctly.
            if (series.Id == "price" || series.Id == "candles")
            {
                double mapped = PriceComponentFallback(component.Name, point);
                // An unrecognised component on the price series still has to render somewhere;
                // the close is the least-wrong y for it. Speech does NOT take this branch —
                // saying a number that was never the component's value is worse than silence.
                return double.IsNaN(mapped) ? point.Close : mapped;
            }

            return double.NaN;
        }

        /// <summary>
        /// Maps a price-series component NAME onto the OHLCV field it stands for, for the case
        /// where the series carries no component array of its own (the primary price series'
        /// components are virtual). Returns NaN for a name that is not a candle part.
        ///
        /// <para>
        /// Accepts both the snake_case machine ids (<c>body</c>, <c>upper_wick</c>,
        /// <c>lower_wick</c>, <c>line</c>) and the legacy display-style names
        /// (<c>Candle Body</c>, <c>Upper Wick</c>, …) so workspaces saved before the Phase 2
        /// rename still resolve. Case-insensitive throughout, because the ids reach here from
        /// saved JSON and from provider metadata, neither of which is normalised.
        /// </para>
        ///
        /// <para>
        /// Shared deliberately: <c>SpeechFormatter.GetPointValue</c> carried its own copy that
        /// still tested the PRE-rename names with <c>string.Contains</c>
        /// (<c>c.Contains("Body")</c>, <c>"Upper"</c>, <c>"Lower"</c>, <c>"Open"</c>) — against
        /// the current ids every one of those is false, so it returned NaN and the wick read
        /// "no data" whenever the primary lookup missed.
        /// </para>
        /// </summary>
        public static double PriceComponentFallback(string componentName, Ohlcv point)
        {
            if (string.IsNullOrWhiteSpace(componentName)) return double.NaN;
            string n = componentName.Trim();

            if (Is(n, "Open")) return point.Open;
            if (Is(n, "High", "upper_wick", "Upper Wick")) return point.High;
            if (Is(n, "Low", "lower_wick", "Lower Wick")) return point.Low;
            if (Is(n, "Close", "body", "line", "Candle Body")) return point.Close;
            if (Is(n, "Volume")) return point.Volume;
            return double.NaN;

            static bool Is(string name, params string[] candidates)
            {
                foreach (var c in candidates)
                    if (name.Equals(c, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
        }

        /// <summary>
        /// Transforms standard OHLCV data into Heikin Ashi format.
        /// Formula:
        /// Close = (O+H+L+C)/4
        /// Open = (PrevOpen + PrevClose)/2
        /// High = Max(H, Open, Close)
        /// Low = Min(L, Open, Close)
        /// </summary>
        public static List<Ohlcv> CalculateHeikinAshi(List<Ohlcv> data)
        {
            var haData = new List<Ohlcv>();
            if (!data.Any()) return haData;

            double prevOpen = data[0].Open;
            double prevClose = data[0].Close;

            foreach (var d in data)
            {
                double close = (d.Open + d.High + d.Low + d.Close) / 4;
                double open = (prevOpen + prevClose) / 2;
                double high = Math.Max(d.High, Math.Max(open, close));
                double low = Math.Min(d.Low, Math.Min(open, close));

                haData.Add(new Ohlcv(d.Date, open, high, low, close, d.Volume));
                
                prevOpen = open;
                prevClose = close;
            }
            return haData;
        }

        /// <summary>
        /// Maps a cursor X pixel position to an absolute bar index in the loaded data.
        /// Inverse of the renderer's bar layout: 0 px = ViewportStartIndex, full width =
        /// start + length - 1. The result is NOT clamped to the data range — callers
        /// decide whether right-margin/future indices are meaningful (drawings allow
        /// them; bar selection does not).
        /// </summary>
        public static int MapXToIndex(double x, double width, int startIndex, int length)
        {
            if (width <= 0 || length <= 0) return startIndex;
            double percent = x / width;
            return startIndex + (int)Math.Round(percent * (length - 1));
        }

        /// <summary>
        /// Maps a cursor Y pixel position to a price within the viewport range.
        /// Inverse of <see cref="MapY"/> for the pane spanning [0, height]. Supports
        /// linear and log scales, with the same degenerate-range guards the forward
        /// mapping uses (min forced positive on log scale; max forced above min).
        /// </summary>
        public static double MapYToPrice(double y, double height, double min, double max, bool isLog)
        {
            if (height <= 0) return min;
            double percent = 1.0 - (y / height);
            if (isLog)
            {
                if (min <= 0) min = 0.01;
                if (max <= min) max = min + 1.0;
                return Math.Exp(Math.Log(min) + (percent * (Math.Log(max) - Math.Log(min))));
            }
            return min + (percent * (max - min));
        }

        /// <summary>
        /// Maps a price to a Y pixel position within a pane spanning [0, height] —
        /// the forward companion of <see cref="MapYToPrice"/>, used by anchor-handle
        /// hit-testing and the hover crosshair.
        /// </summary>
        public static double PriceToScreenY(double price, double height, double min, double max, bool isLog)
        {
            if (isLog)
            {
                if (min <= 0) min = 0.01;
                if (max <= min) max = min + 1.0;
                double pct = (Math.Log(price) - Math.Log(min)) / (Math.Log(max) - Math.Log(min));
                return (1.0 - pct) * height;
            }
            if (max <= min) return 0;
            double linearPct = (price - min) / (max - min);
            return (1.0 - linearPct) * height;
        }

        /// <summary>
        /// Maps a numeric data value to a physical Y-coordinate within a bounded area.
        /// Supports both Linear and Logarithmic scaling.
        /// </summary>
        public static float MapY(double value, float top, float bottom, double min, double max, bool isLogScale)
        {
            float height = bottom - top;
            if (height <= 0) return top;

            if (isLogScale)
            {
                // LOG SCALE: Maps price to Log space before projecting to screen.
                if (value <= 0) value = 0.00001; 
                if (min <= 0) min = 0.00001;
                if (max <= 0) max = 0.00001;
                
                if (Math.Abs(max - min) < 0.000001) return top + (height / 2.0f);

                double logVal = Math.Log(value);
                double logMin = Math.Log(min);
                double logMax = Math.Log(max);
                
                return (float)(bottom - ((logVal - logMin) / (logMax - logMin) * height));
            }
            else
            {
                // LINEAR SCALE: Simple percentage-based projection.
                double range = max - min;
                if (range <= 0.000001) return top + (height / 2.0f);
                return (float)(bottom - ((value - min) / range * height));
            }
        }

        // ── Pointer space → plot space ───────────────────────────────────────
        //
        // The renderer does not draw into the whole canvas. A y-axis column of
        // `theme.AxisWidth * density` runs down the right and an x-axis strip of
        // `_axisHeight` along the bottom; bars are laid across what is left. Every mapping
        // from a pixel back to a bar or a price has to subtract the same two strips, and
        // before 2026-08-27 none of them did:
        //
        //   * MapXToIndex was handed the FULL canvas width by DrawingInteractionManager,
        //     ChartHitTester and ChartHoverTracker, so on a 1280 px chart with a 120-bar
        //     viewport a click on the rightmost candle resolved to bar 113 instead of 119.
        //   * MapYToPrice was handed the FULL canvas height, while the renderer maps the
        //     price range into a main pane of `height - axisHeight - Σ indicatorHeights`.
        //     With a volume pane on screen the main pane is roughly 47% of the canvas, so a
        //     click at the visual bottom of the price pane returned Min + 0.53 × (Max − Min).
        //     Every mouse-placed drawing anchor landed at the wrong price.
        //
        // ChartHitTester already resolved pane BANDS correctly from IPaneLayoutService; it
        // simply never applied the same treatment to the horizontal. These helpers are the
        // one place that knows the rule, so the two code paths on a single click can no
        // longer disagree.

        /// <summary>
        /// The plot width — canvas width minus the y-axis column.
        /// <paramref name="axisWidthFraction"/> comes from <c>IPaneLayoutService</c>.
        /// </summary>
        public static double PlotWidth(double canvasWidth, float axisWidthFraction)
            => canvasWidth * (1.0 - Math.Clamp(axisWidthFraction, 0f, 0.5f));

        /// <summary>
        /// The plot height — canvas height minus the x-axis strip.
        /// </summary>
        public static double PlotHeight(double canvasHeight, float axisHeightFraction)
            => canvasHeight * (1.0 - Math.Clamp(axisHeightFraction, 0f, 0.5f));

        /// <summary>
        /// The vertical band occupied by the pane under <paramref name="y"/>, in pixels, given
        /// the rendered dividers. Returns null when the cursor is over the x-axis strip, where
        /// there is no price to report.
        ///
        /// <para>The same band walk <c>ChartHitTester</c> does — hoisted here so the pointer
        /// mappings can use it instead of assuming the price pane owns the whole canvas.</para>
        /// </summary>
        public static (double Top, double Bottom)? PaneBandPx(
            double y,
            double canvasHeight,
            IReadOnlyList<(string BelowPaneName, float DividerFraction)>? dividers,
            float axisHeightFraction)
        {
            if (canvasHeight <= 0) return null;

            double plotBottomFrac = 1.0 - Math.Clamp(axisHeightFraction, 0f, 0.5f);
            double yFrac = y / canvasHeight;
            if (yFrac < 0 || yFrac > plotBottomFrac) return null;

            double bandTopFrac = 0.0;
            double bandBottomFrac = plotBottomFrac;
            if (dividers != null)
            {
                foreach (var (_, frac) in dividers)
                {
                    if (yFrac >= frac) bandTopFrac = frac;
                    else { bandBottomFrac = Math.Min(bandBottomFrac, frac); break; }
                }
            }

            double top = bandTopFrac * canvasHeight;
            double bottom = bandBottomFrac * canvasHeight;
            return bottom > top ? (top, bottom) : null;
        }

        /// <summary>
        /// A cursor Y within the whole canvas mapped to a price in the pane it actually falls
        /// in. Returns <see cref="double.NaN"/> over the x-axis strip.
        /// </summary>
        public static double MapYToPriceInPane(
            double y,
            double canvasHeight,
            IReadOnlyList<(string BelowPaneName, float DividerFraction)>? dividers,
            float axisHeightFraction,
            double min, double max, bool isLog)
        {
            var band = PaneBandPx(y, canvasHeight, dividers, axisHeightFraction);
            if (band == null) return double.NaN;
            return MapYToPrice(y - band.Value.Top, band.Value.Bottom - band.Value.Top, min, max, isLog);
        }

        /// <summary>
        /// A price mapped to a cursor Y within the whole canvas — the forward companion of
        /// <see cref="MapYToPriceInPane"/>, for the MAIN pane. Anchor-handle hit-testing needs
        /// the two to agree or a handle sits where the drawing is not.
        /// </summary>
        public static double PriceToCanvasY(
            double price,
            double canvasHeight,
            IReadOnlyList<(string BelowPaneName, float DividerFraction)>? dividers,
            float axisHeightFraction,
            double min, double max, bool isLog)
        {
            // The main pane runs from the top of the plot to the first divider.
            double plotBottomFrac = 1.0 - Math.Clamp(axisHeightFraction, 0f, 0.5f);
            double bottomFrac = plotBottomFrac;
            if (dividers != null && dividers.Count > 0)
                bottomFrac = Math.Min(bottomFrac, dividers[0].DividerFraction);

            double top = 0.0;
            double bottom = bottomFrac * canvasHeight;
            if (bottom <= top) return 0;

            double y = top + PriceToScreenY(price, bottom - top, min, max, isLog);

            // The divider pixel belongs to the pane BELOW — PaneBandPx (and ChartHitTester,
            // which has always worked this way) resolve it with `yFrac >= frac`. So a price
            // exactly at the main pane's minimum would otherwise map to a Y that the inverse
            // reads as the top of the VOLUME pane, and the forward and inverse mappings would
            // disagree at exactly the bottom edge. That is one pixel, but it is the pixel a
            // drawing anchored at the low of the range sits on, and with a 10 px grab
            // tolerance a handle attributed to the wrong pane is a handle that cannot be
            // picked up. Stay a hair inside the band the price actually belongs to.
            const double edge = 1e-3;
            return Math.Clamp(y, top, bottom - edge);
        }

        // Deleted 2026-08-24: InverseMapY and GetIndexFromX. Both were public, both had
        // ZERO callers anywhere in the solution (including plugins), and both were second
        // implementations of arithmetic that already exists here — MapYToPrice and
        // MapXToIndex. They also disagreed with the live pair on degenerate input, so the
        // real hazard was not the dead weight but a future caller reaching for the wrong
        // one and getting a different answer on a collapsed range or an empty viewport.
        // Use MapYToPrice / MapXToIndex.
    }
}
