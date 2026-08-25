using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    public class AngleFibCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.AngleFib;

        private static readonly double[] Angles = { 0.236, 0.382, 0.5, 0.618, 0.786, 1.0 };

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var results = new Dictionary<string, double[]>();
            if (!drawing.AnchorDate1.HasValue || !drawing.AnchorPrice1.HasValue ||
                !drawing.AnchorDate2.HasValue || !drawing.AnchorPrice2.HasValue)
                return results;

            int count = chartData.Count;
            int i1 = DrawingCalculatorHelper.FindIndex(chartData, d => d.Date >= drawing.AnchorDate1.Value);
            int i2 = DrawingCalculatorHelper.FindIndex(chartData, d => d.Date >= drawing.AnchorDate2.Value);
            if (i1 == -1 || i2 == -1) return results;

            double baseM = (drawing.AnchorPrice2.Value - drawing.AnchorPrice1.Value) / (i2 - i1);

            foreach (var a in Angles)
            {
                double m = baseM * a;
                double b = drawing.AnchorPrice1.Value - (m * i1);
                var lineData = new double[count];
                for (int i = 0; i < count; i++) lineData[i] = (m * i) + b;
                results[$"Fib Angle {a}"] = lineData;
            }
            return results;
        }
    }
}
