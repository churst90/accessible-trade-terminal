using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    public class AnchoredVwapCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.AnchoredVwap;

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var results = new Dictionary<string, double[]>();
            if (!drawing.AnchorDate1.HasValue) return results;

            int count    = chartData.Count;
            int startIdx = DrawingCalculatorHelper.FindIndex(chartData, d => d.Date >= drawing.AnchorDate1.Value);
            if (startIdx == -1) return results;

            var vwapData = new double[count];
            Array.Fill(vwapData, double.NaN);
            double cumPv  = 0;
            double cumVol = 0;
            for (int i = startIdx; i < count; i++)
            {
                var d = chartData[i];
                cumPv  += (double)((d.High + d.Low + d.Close) / 3.0) * (double)d.Volume;
                cumVol += (double)d.Volume;
                vwapData[i] = cumVol > 0 ? cumPv / cumVol : double.NaN;
            }
            results["AVWAP"] = vwapData;
            return results;
        }
    }
}
