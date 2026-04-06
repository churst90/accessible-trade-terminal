using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    public class AndrewsPitchforkCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.AndrewsPitchfork;

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var results = new Dictionary<string, double[]>();
            if (!drawing.AnchorDate1.HasValue || !drawing.AnchorPrice1.HasValue ||
                !drawing.AnchorDate2.HasValue || !drawing.AnchorPrice2.HasValue ||
                !drawing.AnchorDate3.HasValue || !drawing.AnchorPrice3.HasValue)
                return results;

            int count = chartData.Count;
            int i1 = DrawingCalculatorHelper.FindIndex(chartData, d => d.Date >= drawing.AnchorDate1.Value);
            int i2 = DrawingCalculatorHelper.FindIndex(chartData, d => d.Date >= drawing.AnchorDate2.Value);
            int i3 = DrawingCalculatorHelper.FindIndex(chartData, d => d.Date >= drawing.AnchorDate3.Value);
            if (i1 == -1 || i2 == -1 || i3 == -1) return results;

            double midP = (drawing.AnchorPrice2.Value + drawing.AnchorPrice3.Value) / 2.0;
            int midI    = (i2 + i3) / 2;

            results["Median"] = DrawingCalculatorHelper.CalculateLinearPoints(
                drawing.AnchorDate1.Value, drawing.AnchorPrice1.Value,
                chartData[Math.Clamp(midI, 0, count - 1)].Date, midP,
                false, true, chartData);

            double m = (midP - drawing.AnchorPrice1.Value) / (midI - i1);
            var upper = new double[count];
            var lower = new double[count];
            for (int i = 0; i < count; i++)
            {
                upper[i] = i < i2 ? double.NaN : drawing.AnchorPrice2.Value + (m * (i - i2));
                lower[i] = i < i3 ? double.NaN : drawing.AnchorPrice3.Value + (m * (i - i3));
            }
            results["Upper"] = upper;
            results["Lower"] = lower;
            return results;
        }
    }
}
