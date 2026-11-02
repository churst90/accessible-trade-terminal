using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    /// <summary>
    /// A triangle from four alternating points: the upper side through the two highs, the lower
    /// side through the two lows, each from its first point on to the right, stopping where the
    /// two meet (the apex) when they meet. Which kind it is is said by
    /// <see cref="PatternReading"/>.
    /// </summary>
    public class TrianglePatternCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.TrianglePattern;

        public const string Upper = "Upper side", Lower = "Lower side";

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var points = DrawingCalculatorHelper.Points(drawing, 4);
            if (points == null) return new();
            bool aHigh = points[0].Price > points[1].Price;
            var (h1, h2) = aHigh ? (points[0], points[2]) : (points[1], points[3]);
            var (l1, l2) = aHigh ? (points[1], points[3]) : (points[0], points[2]);
            var upper = DrawingCalculatorHelper.CalculateLinearPoints(h1.Date, h1.Price, h2.Date, h2.Price, false, true, chartData);
            var lower = DrawingCalculatorHelper.CalculateLinearPoints(l1.Date, l1.Price, l2.Date, l2.Price, false, true, chartData);
            // Past the apex the sides have crossed: the triangle is over.
            int last = Math.Max(DrawingCalculatorHelper.ResolveAnchorIndex(chartData, h2.Date),
                                DrawingCalculatorHelper.ResolveAnchorIndex(chartData, l2.Date));
            for (int i = Math.Max(0, last); i < chartData.Count; i++)
                if (!double.IsNaN(upper[i]) && !double.IsNaN(lower[i]) && upper[i] < lower[i])
                {
                    for (int j = i; j < chartData.Count; j++) { upper[j] = double.NaN; lower[j] = double.NaN; }
                    break;
                }
            return new Dictionary<string, double[]> { [Upper] = upper, [Lower] = lower };
        }
    }
}
