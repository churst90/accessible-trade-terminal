using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    /// <summary>
    /// Head and shoulders: the outline through its five points, and the NECKLINE through the two
    /// neckline points, running on to the right — the line the pattern is traded off, so the one
    /// that chirps when price closes through it.
    /// </summary>
    public class HeadAndShouldersCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.HeadAndShoulders;

        public const string Neckline = "Neckline";

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var points = DrawingCalculatorHelper.Points(drawing, 5);
            if (points == null) return new();
            var results = DrawingCalculatorHelper.Legs(points,
                new[] { "Left shoulder", "Head, left side", "Head, right side", "Right shoulder" }, chartData);
            results[Neckline] = DrawingCalculatorHelper.CalculateLinearPoints(points[1].Date, points[1].Price,
                points[3].Date, points[3].Price, false, true, chartData);
            return results;
        }
    }
}
