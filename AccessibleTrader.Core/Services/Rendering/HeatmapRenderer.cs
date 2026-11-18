using SkiaSharp;
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

                    byte alpha = CellAlpha(series, bin);

                    using var binPaint = new SKPaint
                    {
                        Color = basePaint.Color.WithAlpha(alpha),
                        Style = SKPaintStyle.Fill
                    };

                    ctx.Canvas.DrawRect(x, Math.Min(yTop, yBottom), barWidth, Math.Abs(yBottom - yTop), binPaint);
                }
            }
        }

        /// <summary>
        /// The alpha the heatmap paints a cell with: volume against the normalised 0–100 scale,
        /// times the series' own depth. The ONE calculation the renderer and speech share, so a
        /// shade word cannot drift from the picture. A NaN volume is empty, not a crash.
        /// </summary>
        internal static byte CellAlpha(ChartSeries series, ProfileBin bin)
        {
            double alpha = (bin.TotalVolume / 100.0) * 255 * series.Volume;
            return double.IsNaN(alpha) ? (byte)0 : (byte)Math.Clamp(alpha, 0, 255);
        }

        /// <summary>
        /// "dark", "medium" or "light" from the alpha: the top, middle and bottom third of the
        /// 0–255 range. Empty when nothing is painted (alpha 0), so an empty cell says nothing.
        /// </summary>
        internal static string CellShade(ChartSeries series, ProfileBin bin) => ShadeWord(CellAlpha(series, bin));

        internal static string ShadeWord(byte alpha) => alpha == 0 ? ""
            : alpha <= 85 ? "light"
            : alpha <= 170 ? "medium"
            : "dark";
    }
}
