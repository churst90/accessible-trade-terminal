using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Rendering
{
    public class OverlayLayer : IRenderLayer
    {
        private readonly ThemeService _themeService;

        public OverlayLayer(ThemeService themeService)
        {
            _themeService = themeService;
        }

        public void Render(RenderContext ctx, IEnumerable<ChartSeries> series)
        {
            if (ctx.PaneName != "Main") return;

            var theme = ctx.Theme;
            
            // ── CROSSHAIR ───────────────────────────────────────────────────────────
            if (ctx.LocalCursorIndex >= 0 && ctx.LocalCursorIndex < ctx.Data.Count)
            {
                float x = (ctx.LocalCursorIndex * ctx.ItemWidth) + (ctx.ItemWidth / 2);
                float y = ChartMath.MapY(ctx.Data[ctx.LocalCursorIndex].Close, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);

                using var paint = new SKPaint
                {
                    Color = theme.Crosshair.WithAlpha(150),
                    StrokeWidth = 1 * ctx.Density,
                    Style = SKPaintStyle.Stroke,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 4 * ctx.Density, 4 * ctx.Density }, 0)
                };

                ctx.Canvas.DrawLine(x, ctx.Top, x, ctx.Bottom, paint);
                ctx.Canvas.DrawLine(0, y, ctx.Width, y, paint);
            }
        }
    }
}
