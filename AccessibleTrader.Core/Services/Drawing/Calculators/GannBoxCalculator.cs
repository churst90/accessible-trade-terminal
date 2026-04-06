using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    public class GannBoxCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.GannBox;

        private static readonly double[] Ratios = { 0, 0.25, 0.382, 0.5, 0.618, 0.75, 1.0 };

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var results = new Dictionary<string, double[]>();
            if (!drawing.AnchorDate1.HasValue || !drawing.AnchorPrice1.HasValue ||
                !drawing.AnchorDate2.HasValue || !drawing.AnchorPrice2.HasValue)
                return results;

            int count = chartData.Count;
            int i1    = DrawingCalculatorHelper.FindIndex(chartData, d => d.Date >= drawing.AnchorDate1.Value);
            int i2    = DrawingCalculatorHelper.FindIndex(chartData, d => d.Date >= drawing.AnchorDate2.Value);
            if (i1 == -1 || i2 == -1 || i1 == i2) return results;

            int iStart = Math.Min(i1, i2);
            int iEnd   = Math.Max(i1, i2);

            double minP = Math.Min(drawing.AnchorPrice1.Value, drawing.AnchorPrice2.Value);
            double maxP = Math.Max(drawing.AnchorPrice1.Value, drawing.AnchorPrice2.Value);
            double diff = maxP - minP;

            // Horizontal price levels — NaN outside the anchor time range
            foreach (var r in Ratios)
            {
                double price = minP + (diff * r);
                var data = new double[count];
                Array.Fill(data, double.NaN);
                for (int i = iStart; i <= iEnd; i++)
                    data[i] = price;
                results[$"Level {r * 100:0.###}%"] = data;
            }

            // Vertical time levels — single-bar point at each Gann time ratio,
            // rendered as the mid-price so the renderer can draw a tick marker
            int timeSpan = iEnd - iStart;
            double midP  = (minP + maxP) / 2.0;
            foreach (var r in Ratios)
            {
                int iT  = iStart + (int)Math.Round(timeSpan * r);
                iT      = Math.Clamp(iT, iStart, iEnd);
                var data = new double[count];
                Array.Fill(data, double.NaN);
                data[iT] = midP;
                results[$"Time {r * 100:0.###}%"] = data;
            }

            return results;
        }
    }
}
