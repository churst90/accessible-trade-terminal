using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    public class GannFanCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.GannFan;

        private static readonly double[] Multipliers = { 8.0, 4.0, 3.0, 2.0, 1.0, 0.5, 0.333, 0.25, 0.125 };
        private static readonly string[] Labels      = { "1x8", "1x4", "1x3", "1x2", "1x1", "2x1", "3x1", "4x1", "8x1" };

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var results = new Dictionary<string, double[]>();
            if (!drawing.AnchorDate1.HasValue || !drawing.AnchorPrice1.HasValue ||
                !drawing.AnchorDate2.HasValue || !drawing.AnchorPrice2.HasValue)
                return results;

            int count = chartData.Count;
            int i1 = DrawingCalculatorHelper.FindIndex(chartData, d => d.Date >= drawing.AnchorDate1.Value);
            int i2 = DrawingCalculatorHelper.FindIndex(chartData, d => d.Date >= drawing.AnchorDate2.Value);
            if (i1 == -1 || i2 == -1 || i1 == i2) return results;

            double baseM = (drawing.AnchorPrice2.Value - drawing.AnchorPrice1.Value) / (i2 - i1);

            for (int mIdx = 0; mIdx < Multipliers.Length; mIdx++)
            {
                double m = baseM * Multipliers[mIdx];
                double b = drawing.AnchorPrice1.Value - (m * i1);
                var lineData = new double[count];
                for (int i = 0; i < count; i++)
                    lineData[i] = (m * i) + b;
                results[Labels[mIdx]] = lineData;
            }
            return results;
        }
    }
}
