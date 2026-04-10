using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies.Levels
{
    /// <summary>
    /// Surfaces every horizontal line and trend-line endpoint that the user has drawn on
    /// the chart as a <see cref="PriceLevel"/>. Lines below current price are emitted
    /// as <see cref="LevelKind.Support"/>; lines above are emitted as
    /// <see cref="LevelKind.Resistance"/>. The user's drawn levels are typically the highest-
    /// priority signal source — they get a strength of 0.8 to outrank algorithmic pivots.
    /// </summary>
    public class DrawnHorizontalLevelProvider : ILevelProvider
    {
        public string SourceId => "drawn";

        public IReadOnlyList<PriceLevel> GetLevels(IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            if (history.Count == 0 || state?.ActiveSeries == null) return System.Array.Empty<PriceLevel>();
            double currentPrice = history[^1].Close;

            var sink = new List<PriceLevel>();
            foreach (var s in state.ActiveSeries)
            {
                if (!s.IsDrawing || s.Drawing == null) continue;
                var d = s.Drawing;
                switch (d.Type)
                {
                    case DrawingType.HorizontalLine:
                        AddLevel(sink, d.AnchorPrice1, currentPrice);
                        break;
                    case DrawingType.TrendLine:
                    case DrawingType.RiskReward:
                        // Both endpoints contribute. RR explicitly carries StopLoss/TakeProfit too,
                        // but for the level service we treat it as two anchored prices.
                        AddLevel(sink, d.AnchorPrice1, currentPrice);
                        AddLevel(sink, d.AnchorPrice2, currentPrice);
                        break;
                    case DrawingType.Rectangle:
                        // Top and bottom edges of a rectangle are the most useful price references.
                        AddLevel(sink, d.AnchorPrice1, currentPrice);
                        AddLevel(sink, d.AnchorPrice2, currentPrice);
                        break;
                }
            }
            return sink;
        }

        private static void AddLevel(List<PriceLevel> sink, double? maybePrice, double currentPrice)
        {
            if (!maybePrice.HasValue) return;
            double price = maybePrice.Value;
            if (double.IsNaN(price) || double.IsInfinity(price) || price <= 0) return;
            var kind = price < currentPrice ? LevelKind.Support : LevelKind.Resistance;
            sink.Add(new PriceLevel(price, kind, Strength: 0.8, Source: "Drawn"));
        }
    }
}
