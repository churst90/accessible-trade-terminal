using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    /// <summary>An Elliott impulse: five waves through six points.</summary>
    public class ElliottImpulseCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.ElliottImpulse;

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var points = DrawingCalculatorHelper.Points(drawing, 6);
            return points == null ? new()
                : DrawingCalculatorHelper.Legs(points, new[] { "Wave 1", "Wave 2", "Wave 3", "Wave 4", "Wave 5" }, chartData);
        }
    }
}
