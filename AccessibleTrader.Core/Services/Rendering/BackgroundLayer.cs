using SkiaSharp;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Rendering
{
    public class BackgroundLayer : IRenderLayer
    {
        private readonly ThemeService _theme;

        public BackgroundLayer(ThemeService theme)
        {
            _theme = theme;
        }

        public void Render(RenderContext ctx, IEnumerable<ChartSeries> series)
        {
            // Explicitly fill the pane with the theme background so each pane is opaque.
            // This prevents any white compositor bleed-through from the WebView overlay
            // and ensures each pane resets cleanly on every frame.
            using var bgPaint = new SKPaint { Color = _theme.Background, Style = SKPaintStyle.Fill };
            ctx.Canvas.DrawRect(ctx.PaneRect, bgPaint);

            using var gridPaint = new SKPaint { Color = _theme.GridLines.WithAlpha(50), StrokeWidth = 1 * ctx.Density };
            
            // Draw a single horizontal midline as an anchor point
            float yMid = ChartMath.MapY(ctx.Min + (ctx.Max - ctx.Min) / 2, ctx.PaneRect.Top, ctx.PaneRect.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
            ctx.Canvas.DrawLine(ctx.PaneRect.Left, yMid, ctx.PaneRect.Right, yMid, gridPaint);
            
            // Draw pane border
            using var borderPaint = new SKPaint { Color = _theme.GridLines, Style = SKPaintStyle.Stroke, StrokeWidth = 1 * ctx.Density };
            ctx.Canvas.DrawRect(ctx.PaneRect, borderPaint);
        }
    }
}
