using System;
using System.Collections.Generic;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    public class MeasureToolCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.MeasureTool;

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var results = new Dictionary<string, double[]>();
            if (!drawing.AnchorDate1.HasValue || !drawing.AnchorPrice1.HasValue ||
                !drawing.AnchorDate2.HasValue || !drawing.AnchorPrice2.HasValue)
                return results;

            int i1 = DrawingCalculatorHelper.FindIndex(chartData, d => d.Date >= drawing.AnchorDate1.Value);
            int i2 = DrawingCalculatorHelper.FindIndex(chartData, d => d.Date >= drawing.AnchorDate2.Value);
            if (i1 == -1 || i2 == -1) return results;

            int barDist     = Math.Abs(i2 - i1);
            double priceDist = drawing.AnchorPrice2.Value - drawing.AnchorPrice1.Value;
            double pct       = (priceDist / drawing.AnchorPrice1.Value) * 100.0;
            // Magnitude-aware price formatting so SHIB/PEPE/KAS measure distances
            // don't collapse to "0.00". The percent is left at F2 — percent values
            // above 1% carry plenty of precision; below 1% the user is usually
            // more interested in the price distance anyway.
            drawing.MeasureResult = $"{SpeechPriceFormatter.FormatPrice(priceDist)} ({pct:F2}%), {barDist} bars";

            results["Measure"] = DrawingCalculatorHelper.CalculateLinearPoints(
                drawing.AnchorDate1.Value, drawing.AnchorPrice1.Value,
                drawing.AnchorDate2.Value, drawing.AnchorPrice2.Value,
                false, false, chartData);
            return results;
        }
    }
}
