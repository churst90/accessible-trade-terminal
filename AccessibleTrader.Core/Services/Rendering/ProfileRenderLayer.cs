using SkiaSharp;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Rendering
{
    /// <summary>
    /// Draws volume-profile bins for any profile series assigned to this pane.
    ///
    /// Deliberately dependency-free (2026-08-24): it previously took an
    /// <c>IProfileService</c>, a <c>ThemeService</c> and an <c>IAppLogger</c>, assigned all
    /// three to fields, and used none of them — the theme it draws with comes from
    /// <c>ctx.Theme</c> on the render context, and the bins are already on the series by the
    /// time this runs. Carrying them made the layer look like it computed profiles and
    /// logged failures; it does neither. Removing them also let <c>ChartRenderer</c> stop
    /// taking an <c>IProfileService</c> it only ever forwarded here.
    /// </summary>
    public class ProfileRenderLayer : IRenderLayer
    {
        public void Render(RenderContext ctx, IEnumerable<ChartSeries> series)
        {
            foreach (var s in series)
            {
                if (!s.IsVisible || !s.IsProfile) continue;
                // Allow profiles to render in their assigned pane (usually 'Main')
                if (s.Pane != ctx.PaneName && !(string.IsNullOrEmpty(s.Pane) && ctx.PaneName == "Main")) continue;

                var bins = s.ProfileBins;
                if (bins == null || !bins.Any()) continue;

                RenderProfile(ctx, bins, s);
            }
        }

        private void RenderProfile(RenderContext ctx, List<ProfileBin> bins, ChartSeries series)
        {
            var theme = ctx.Theme;
            float profileWidth = ctx.Width * 0.2f; // Profiles take up 20% of pane width
            float maxVolume = (float)bins.Max(b => b.TotalVolume);
            if (maxVolume <= 0) return;

            using var paint = new SKPaint
            {
                Color = theme.ProfileNormal.WithAlpha(100),
                Style = SKPaintStyle.Fill
            };

            using var pocPaint = new SKPaint
            {
                Color = theme.ProfilePOC,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2 * ctx.Density
            };

            foreach (var bin in bins)
            {
                float yTop = ChartMath.MapY(bin.PriceHigh, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                float yBottom = ChartMath.MapY(bin.PriceLow, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                float binWidth = (float)(bin.TotalVolume / maxVolume) * profileWidth;

                // Right-aligned profile
                ctx.Canvas.DrawRect(ctx.Width - binWidth, Math.Min(yTop, yBottom), binWidth, Math.Abs(yBottom - yTop), paint);

                if (bin.IsPOC)
                {
                    float yPOC = (yTop + yBottom) / 2.0f;
                    ctx.Canvas.DrawLine(ctx.Width - profileWidth, yPOC, ctx.Width, yPOC, pocPaint);
                }
            }
        }
    }
}
