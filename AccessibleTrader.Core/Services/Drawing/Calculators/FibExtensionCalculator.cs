using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    /// <summary>One flat line per level, the swing (point 1 to point 2) projected from the third
    /// point; see <see cref="FibLevels"/>.</summary>
    public class FibExtensionCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.FibExtension;

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            if (!drawing.AnchorPrice1.HasValue || !drawing.AnchorPrice2.HasValue || !drawing.AnchorPrice3.HasValue)
                return new();
            return FibLevels.Lines(drawing, chartData.Count);
        }
    }
}
