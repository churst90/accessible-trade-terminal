using System;
using System.Linq;
using System.Text;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Provides deep, context-aware analysis of a single data point (bar).
    /// Used for the detailed summary command (Ctrl+Shift+D).
    /// </summary>
    public class BarDetailService : IBarDetailService
    {
        private readonly IEventBus _eventBus;

        public BarDetailService(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        /// <inheritdoc />
        public void AnnounceDetails(WorkspaceState state)
        {
            if (state.Data == null || state.Data.Count == 0) return;
            
            var seriesId = state.FocusedSeriesId ?? state.PrimarySeriesId;
            var series = state.ActiveSeries.FirstOrDefault(s => s.Id == seriesId);
            if (series == null) return;

            int idx = Math.Clamp(state.CurrentDataIndex, 0, state.Data.Count - 1);
            var bar = state.Data[idx];

            // Build a lookback slice (up to 50 bars before the current index) so GetDetailFact
            // can perform pattern/context analysis on real price data.
            int sliceStart = Math.Max(0, idx - 50);
            var dataSlice  = state.Data.Skip(sliceStart).Take(idx - sliceStart + 1).ToArray();
            string detail  = GetBarDetailFact(series, bar, idx, dataSlice);

            _eventBus.Publish(new AnnouncementEvent(detail, true));
        }

        private string GetBarDetailFact(ChartSeries series, Ohlcv bar, int index, Ohlcv[] recentData)
        {
            var sb = new StringBuilder();
            sb.Append($"{bar.Date:HH:mm}: ");

            // If it's the primary candle series, add candle pattern details
            // Pattern/type details only apply to true OHLCV series. Price-line primary
            // series (analytics providers) deliberately skip this block — a single-value
            // point has no wicks, body, or multi-bar pattern to describe.
            if (series.Id == CoreSeriesIds.Candles || series.IndicatorCode == "CANDLES")
            {
                string trend = bar.Close >= bar.Open ? "Bullish" : "Bearish";
                string type  = ClassifyBar(bar);
                double range = bar.High - bar.Low;
                double body  = Math.Abs(bar.Close - bar.Open);
                double bodyPct = range > 0 ? (body / range) * 100.0 : 0;
                double upperWick = bar.High - Math.Max(bar.Open, bar.Close);
                double lowerWick = Math.Min(bar.Open, bar.Close) - bar.Low;
                double upperPct = range > 0 ? (upperWick / range) * 100.0 : 0;
                double lowerPct = range > 0 ? (lowerWick / range) * 100.0 : 0;

                sb.Append($"{trend} {type}. Body {bodyPct:F0}%, Upper wick {upperPct:F0}%, Lower wick {lowerPct:F0}%. ");
                return sb.ToString().TrimEnd();
            }

            // Ctrl+Shift+D always reads raw component values regardless of indicator type.
            // GetDetailFact is intentionally bypassed here so every indicator reveals its
            // actual numeric column values rather than a condensed narrative summary.
            foreach (var comp in series.Components)
            {
                if (!comp.IsVisible) continue;

                var data = series.GetComponentData(comp.Name);
                if (index < 0 || index >= data.Length) continue;

                double val = data[index];

                if (comp.UsesGradientSpeech)
                {
                    var colorData = series.GetComponentData(comp.Name + "_color");
                    if (colorData != null && index < colorData.Length && !double.IsNaN(colorData[index]))
                        val = colorData[index];
                }

                if (double.IsNaN(val)) continue;

                sb.Append($"{comp.DisplayName ?? comp.Name} {val:F2}, ");
            }

            return sb.ToString().TrimEnd(',', ' ');
        }

        private static string ClassifyBar(Ohlcv bar)
        {
            double range = bar.High - bar.Low;
            if (range <= 0) return "Flat";

            double body       = Math.Abs(bar.Close - bar.Open);
            double bodyPct    = body / range;
            double upperWick  = bar.High - Math.Max(bar.Open, bar.Close);
            double lowerWick  = Math.Min(bar.Open, bar.Close) - bar.Low;
            double upperPct   = upperWick / range;
            double lowerPct   = lowerWick / range;

            if (bodyPct < 0.05)
            {
                if (lowerPct > 0.6 && upperPct < 0.1) return "Dragonfly Doji";
                if (upperPct > 0.6 && lowerPct < 0.1) return "Gravestone Doji";
                return "Doji";
            }
            if (bodyPct > 0.90) return "Marubozu";
            if (bodyPct < 0.30 && lowerPct > 0.60 && upperPct < 0.10) return "Hammer";
            if (bodyPct < 0.30 && upperPct > 0.60 && lowerPct < 0.10) return "Shooting Star";
            if (bodyPct < 0.30 && upperPct > 0.25 && lowerPct > 0.25) return "Spinning Top";

            return "Standard Candle";
        }
    }
}
