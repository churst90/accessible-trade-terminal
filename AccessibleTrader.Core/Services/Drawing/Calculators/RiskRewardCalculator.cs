using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    public class RiskRewardCalculator : IDrawingCalculator
    {
        public DrawingType DrawingType => DrawingType.RiskReward;

        public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            var results = new Dictionary<string, double[]>();
            if (!drawing.AnchorPrice1.HasValue || !drawing.AnchorPrice2.HasValue || !drawing.AnchorPrice3.HasValue)
                return results;

            int count  = chartData.Count;
            double entry  = drawing.AnchorPrice1.Value;
            double stop   = drawing.AnchorPrice2.Value;
            double target = drawing.AnchorPrice3.Value;

            var entryData  = new double[count]; Array.Fill(entryData,  entry);
            var stopData   = new double[count]; Array.Fill(stopData,   stop);
            var targetData = new double[count]; Array.Fill(targetData, target);

            results["Entry"]       = entryData;
            results["Stop Loss"]   = stopData;
            results["Take Profit"] = targetData;

            double risk   = Math.Abs(entry - stop);
            double reward = Math.Abs(target - entry);
            drawing.RiskRewardRatio = risk > 0 ? reward / risk : 0;
            return results;
        }
    }
}
