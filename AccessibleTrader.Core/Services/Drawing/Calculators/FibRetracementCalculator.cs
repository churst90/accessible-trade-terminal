using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    /// <summary>One flat line per level; where they sit and what they are called is
    /// <see cref="FibLevels"/> (TradingView's way round: 0 at the swing end).</summary>
    public class FibRetracementCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.FibRetracement;

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            if (!drawing.AnchorPrice1.HasValue || !drawing.AnchorPrice2.HasValue) return new();
            return FibLevels.Lines(drawing, chartData.Count);
        }
    }
}
