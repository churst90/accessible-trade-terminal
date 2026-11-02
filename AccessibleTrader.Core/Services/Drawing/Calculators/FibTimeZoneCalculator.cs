using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    /// <summary>
    /// Fibonacci time zones, TradingView's: the bars from the first point to the second are one
    /// unit, and a full-height line stands at 0, 1, 2, 3, 5, 8, 13, 21, 34, 55 and 89 units from
    /// the first point. Each zone is its own line, so it can be reached and named ("time zone
    /// 13"). A zone past the last loaded bar has no bar to stand on and is not drawn yet.
    /// </summary>
    public class FibTimeZoneCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.FibTimeZone;

        public static readonly int[] Zones = { 0, 1, 2, 3, 5, 8, 13, 21, 34, 55, 89 };

        public static string Key(int zone) => $"Time zone {zone}";

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var results = new Dictionary<string, double[]>();
            if (!drawing.AnchorDate1.HasValue || !drawing.AnchorDate2.HasValue) return results;
            int i1 = DrawingCalculatorHelper.ResolveAnchorIndex(chartData, drawing.AnchorDate1.Value);
            int i2 = DrawingCalculatorHelper.ResolveAnchorIndex(chartData, drawing.AnchorDate2.Value);
            if (i1 < 0 || i2 < 0) return results;
            int unit = Math.Abs(i2 - i1);
            if (unit == 0) return results;
            int start = Math.Min(i1, i2);
            foreach (int z in Zones)
            {
                var line = new double[chartData.Count];
                Array.Fill(line, double.NaN);
                int bar = start + z * unit;
                if (bar < chartData.Count) line[bar] = chartData[bar].Close;
                results[Key(z)] = line;
            }
            return results;
        }
    }
}
