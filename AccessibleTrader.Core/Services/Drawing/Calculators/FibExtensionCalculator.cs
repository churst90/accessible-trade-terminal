using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    public class FibExtensionCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.FibExtension;

        private static readonly double[] ExtLevels = { 0, 0.236, 0.382, 0.5, 0.618, 0.786, 1.0, 1.618, 2.618 };

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var results = new Dictionary<string, double[]>();
            if (!drawing.AnchorPrice1.HasValue || !drawing.AnchorPrice2.HasValue || !drawing.AnchorPrice3.HasValue)
                return results;

            int count = chartData.Count;
            double move = drawing.AnchorPrice2.Value - drawing.AnchorPrice1.Value;
            double p3   = drawing.AnchorPrice3.Value;

            foreach (var lvl in ExtLevels)
            {
                double price = p3 + (move * lvl);
                var data = new double[count];
                Array.Fill(data, price);
                results[$"{lvl * 100:F1}% Ext"] = data;
            }
            return results;
        }
    }
}
