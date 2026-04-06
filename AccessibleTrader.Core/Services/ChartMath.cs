using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Models;

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
            // Maps candle parts (High, Low, etc.) to their logical names.
            if (series.Id == "price" || series.Id == "candles")
            {
                return component.Name switch
                {
                    "Open" => point.Open,
                    "High" or "Upper Wick" => point.High,
                    "Low" or "Lower Wick" => point.Low,
                    "Close" or "Candle Body" => point.Close,
                    "Volume" => point.Volume,
                    _ => point.Close
                };
            }

            return double.NaN;
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

        /// <summary>
        /// Inversely maps a physical Y-coordinate back to a data value.
        /// </summary>
        public static double InverseMapY(float y, float top, float bottom, double min, double max, bool isLogScale)
        {
            float height = bottom - top;
            if (height <= 0) return min;

            if (isLogScale)
            {
                if (min <= 0) min = 0.00001;
                if (max <= 0) max = 0.00001;
                double logMin = Math.Log(min);
                double logMax = Math.Log(max);
                double logVal = logMin + (double)(bottom - y) / height * (logMax - logMin);
                return Math.Exp(logVal);
            }
            else
            {
                double range = max - min;
                return min + (double)(bottom - y) / height * range;
            }
        }

        /// <summary>
        /// Maps a physical X-coordinate back to a data index within the viewport.
        /// </summary>
        public static int GetIndexFromX(float x, float width, int viewportStart, int viewportLength)
        {
            if (width <= 0 || viewportLength <= 0) return -1;
            float barWidth = width / viewportLength;
            int offset = (int)Math.Floor(x / barWidth);
            return viewportStart + offset;
        }
    }
}
