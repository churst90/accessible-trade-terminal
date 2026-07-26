using System;
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
            // and ensures each pane resets cleanly on every frame. When a gradient end
            // color is set (opt-in), fill with a vertical Background→end linear gradient.
            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill };
            var gradientEnd = _theme.BackgroundGradientEnd;
            if (gradientEnd is { } end)
            {
                bgPaint.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(ctx.PaneRect.Left, ctx.PaneRect.Top),
                    new SKPoint(ctx.PaneRect.Left, ctx.PaneRect.Bottom),
                    new[] { _theme.Background, end },
                    null, SKShaderTileMode.Clamp);
            }
            else
            {
                bgPaint.Color = _theme.Background;
            }
            ctx.Canvas.DrawRect(ctx.PaneRect, bgPaint);
            bgPaint.Shader?.Dispose();

            // Minor + major gridlines at "nice number" intervals. Every 5th line is
            // drawn brighter so the eye finds round-number anchors ($25k, $50k, etc.)
            // without the grid turning into a wall. Alphas bumped 2026-04-24 in two
            // passes after screenshot reviews: 35/90 → 60/140 → 80/160. The theme's
            // GridLines color is already muted so the per-paint alpha carries most
            // of the weight; 80 on minor still reads as a whisper, not a hard grid.
            using var gridMinor = new SKPaint { Color = _theme.GridLines.WithAlpha(80),  StrokeWidth = 1 * ctx.Density };
            using var gridMajor = new SKPaint { Color = _theme.GridLines.WithAlpha(160), StrokeWidth = 1 * ctx.Density };

            double range = ctx.Max - ctx.Min;
            if (range > 0 && !double.IsNaN(range) && !double.IsInfinity(range))
            {
                double roughStep = range / 7.0;
                double stepMag = Math.Pow(10, Math.Floor(Math.Log10(roughStep)));
                double stepFrac = roughStep / stepMag;
                double niceStep;
                if (stepFrac < 1.5) niceStep = 1 * stepMag;
                else if (stepFrac < 3.5) niceStep = 2 * stepMag;
                else if (stepFrac < 7.5) niceStep = 5 * stepMag;
                else niceStep = 10 * stepMag;

                double firstLine = Math.Ceiling(ctx.Min / niceStep) * niceStep;
                int safety = 0;
                for (double v = firstLine; v <= ctx.Max && safety < 200; v += niceStep, safety++)
                {
                    float y = ChartMath.MapY(v, ctx.PaneRect.Top, ctx.PaneRect.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                    // Major line whenever v is an integer multiple of niceStep * 5.
                    double idx = v / niceStep;
                    bool isMajor = Math.Abs(Math.Round(idx / 5.0) * 5.0 - idx) < 0.0001;
                    ctx.Canvas.DrawLine(ctx.PaneRect.Left, y, ctx.PaneRect.Right, y, isMajor ? gridMajor : gridMinor);
                }
            }
            else
            {
                // Fallback: single midline when the range is degenerate.
                float yMid = ChartMath.MapY(ctx.Min + (ctx.Max - ctx.Min) / 2, ctx.PaneRect.Top, ctx.PaneRect.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                ctx.Canvas.DrawLine(ctx.PaneRect.Left, yMid, ctx.PaneRect.Right, yMid, gridMinor);
            }

            // Draw pane border
            using var borderPaint = new SKPaint { Color = _theme.GridLines, Style = SKPaintStyle.Stroke, StrokeWidth = 1 * ctx.Density };
            ctx.Canvas.DrawRect(ctx.PaneRect, borderPaint);
        }
    }
}
