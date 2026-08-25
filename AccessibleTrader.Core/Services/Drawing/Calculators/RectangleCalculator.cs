using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    public class RectangleCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.Rectangle;

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var results = new Dictionary<string, double[]>();
            if (!drawing.AnchorDate1.HasValue || !drawing.AnchorDate2.HasValue ||
                !drawing.AnchorPrice1.HasValue || !drawing.AnchorPrice2.HasValue)
                return results;

            int count = chartData.Count;
            int start = DrawingCalculatorHelper.FindIndex(chartData, d => d.Date >= drawing.AnchorDate1.Value);
            int end   = DrawingCalculatorHelper.FindIndex(chartData, d => d.Date >= drawing.AnchorDate2.Value);
            if (start == -1) start = 0;
            if (end   == -1) end   = count - 1;
            if (start > end) (start, end) = (end, start);

            double top    = Math.Max(drawing.AnchorPrice1.Value, drawing.AnchorPrice2.Value);
            double bottom = Math.Min(drawing.AnchorPrice1.Value, drawing.AnchorPrice2.Value);

            var topData    = new double[count];
            var bottomData = new double[count];
            Array.Fill(topData,    double.NaN);
            Array.Fill(bottomData, double.NaN);

            for (int i = start; i <= end; i++)
            {
                topData[i]    = top;
                bottomData[i] = bottom;
            }
            results["Top"]    = topData;
            results["Bottom"] = bottomData;
            return results;
        }
    }
}
