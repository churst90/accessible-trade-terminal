using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    /// <summary>The XABCD (harmonic) pattern's four legs; see <see cref="PatternReading"/>.</summary>
    public class XabcdPatternCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.XabcdPattern;

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var points = DrawingCalculatorHelper.Points(drawing, 5);
            return points == null ? new() : DrawingCalculatorHelper.Legs(points, new[] { "XA", "AB", "BC", "CD" }, chartData);
        }
    }
}
