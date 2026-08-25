using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    public class TrendLineCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.TrendLine;

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var results = new Dictionary<string, double[]>();
            if (!drawing.AnchorDate1.HasValue || !drawing.AnchorPrice1.HasValue ||
                !drawing.AnchorDate2.HasValue || !drawing.AnchorPrice2.HasValue)
                return results;
            results["Line"] = DrawingCalculatorHelper.CalculateLinearPoints(
                drawing.AnchorDate1.Value, drawing.AnchorPrice1.Value,
                drawing.AnchorDate2.Value, drawing.AnchorPrice2.Value,
                drawing.ExtendLeft, drawing.ExtendRight, chartData);
            return results;
        }
    }
}
