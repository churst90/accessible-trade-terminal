using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    /// <summary>The ABCD pattern's three legs. What they make is said by
    /// <see cref="PatternReading"/>.</summary>
    public class AbcdPatternCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.AbcdPattern;

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var points = DrawingCalculatorHelper.Points(drawing, 4);
            return points == null ? new() : DrawingCalculatorHelper.Legs(points, new[] { "AB", "BC", "CD" }, chartData);
        }
    }
}
