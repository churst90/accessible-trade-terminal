using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    public class HorizontalLineCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.HorizontalLine;

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var results = new Dictionary<string, double[]>();
            if (!drawing.AnchorPrice1.HasValue) return results;
            var data = new double[chartData.Count];
            Array.Fill(data, drawing.AnchorPrice1.Value);
            results["Line"] = data;
            return results;
        }
    }
}
