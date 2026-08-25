using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    public class FibRetracementCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.FibRetracement;

        private static readonly double[] Levels = { 0, 0.236, 0.382, 0.5, 0.618, 0.786, 1.0 };

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var results = new Dictionary<string, double[]>();
            if (!drawing.AnchorPrice1.HasValue || !drawing.AnchorPrice2.HasValue) return results;

            int count = chartData.Count;
            double p1   = drawing.AnchorPrice1.Value;
            double diff = p1 - drawing.AnchorPrice2.Value;

            foreach (var level in Levels)
            {
                double price = p1 - (diff * level);
                var data = new double[count];
                Array.Fill(data, price);
                results[$"{level * 100:F1}% Level"] = data;
            }
            return results;
        }
    }
}
