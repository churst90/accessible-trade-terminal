using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    public class ChannelCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.Channel;

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var results = new Dictionary<string, double[]>();
            if (!drawing.AnchorDate1.HasValue || !drawing.AnchorPrice1.HasValue ||
                !drawing.AnchorDate2.HasValue || !drawing.AnchorPrice2.HasValue)
                return results;

            int count = chartData.Count;
            var baseLine = DrawingCalculatorHelper.CalculateLinearPoints(
                drawing.AnchorDate1.Value, drawing.AnchorPrice1.Value,
                drawing.AnchorDate2.Value, drawing.AnchorPrice2.Value,
                drawing.ExtendLeft, drawing.ExtendRight, chartData);

            results["Lower Bound"] = baseLine;

            double width = drawing.ChannelWidth == 0 ? drawing.AnchorPrice1.Value * 0.05 : drawing.ChannelWidth;
            var upperArr  = new double[count];
            var medianArr = new double[count];
            for (int i = 0; i < count; i++)
            {
                upperArr[i]  = double.IsNaN(baseLine[i]) ? double.NaN : baseLine[i] + width;
                medianArr[i] = double.IsNaN(baseLine[i]) ? double.NaN : baseLine[i] + (width / 2);
            }
            results["Upper Bound"] = upperArr;
            results["Median"]      = medianArr;
            return results;
        }
    }
}
