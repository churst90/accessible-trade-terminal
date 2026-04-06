using SkiaSharp;
using System;
using System.Linq;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Rendering
{
    public class HeatmapRenderer : IComponentRenderer
    {
        public void Render(RenderContext ctx, ChartSeries series)
        {
            if (series.HeatmapData == null || series.HeatmapData.Count == 0) return;

            float barWidth = ctx.Width / ctx.ViewportLength;
            var basePaint = new SKPaint { Color = SKColors.Orange };

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= series.HeatmapData.Count) break;

                var barBins = series.HeatmapData[dataIdx];
                if (barBins == null || !barBins.Any()) continue;

                float x = i * barWidth;

                foreach (var bin in barBins)
                {
                    float yTop = ChartMath.MapY(bin.PriceHigh, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                    float yBottom = ChartMath.MapY(bin.PriceLow, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);

                    // Ensure at least 1 pixel height for depth levels
                    if (Math.Abs(yBottom - yTop) < 1) yBottom = yTop + 1;

                    double volumeMultiplier = series.Volume;
                    byte alpha = (byte)Math.Clamp((bin.TotalVolume / 100.0) * 255 * volumeMultiplier, 0, 255);
                    
                    using var binPaint = new SKPaint
                    {
                        Color = basePaint.Color.WithAlpha(alpha),
                        Style = SKPaintStyle.Fill
                    };

                    ctx.Canvas.DrawRect(x, Math.Min(yTop, yBottom), barWidth, Math.Abs(yBottom - yTop), binPaint);
                }
            }
        }
    }
}
