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

        // Deleted 2026-08-24: InverseMapY and GetIndexFromX. Both were public, both had
        // ZERO callers anywhere in the solution (including plugins), and both were second
        // implementations of arithmetic that already exists here — MapYToPrice and
        // MapXToIndex. They also disagreed with the live pair on degenerate input, so the
        // real hazard was not the dead weight but a future caller reaching for the wrong
        // one and getting a different answer on a collapsed range or an empty viewport.
        // Use MapYToPrice / MapXToIndex.
    }
}
