using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    public class TextLabelCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.TextLabel;

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var results = new Dictionary<string, double[]>();
            if (!drawing.AnchorDate1.HasValue || !drawing.AnchorPrice1.HasValue) return results;
            var data = new double[chartData.Count];
            Array.Fill(data, double.NaN);
            int index = DrawingCalculatorHelper.FindIndex(chartData, d => d.Date >= drawing.AnchorDate1.Value);
            if (index != -1) data[index] = drawing.AnchorPrice1.Value;
            results["Label"] = data;
            return results;
        }
    }
}
