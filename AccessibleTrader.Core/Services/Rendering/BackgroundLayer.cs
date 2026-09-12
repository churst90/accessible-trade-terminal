using SkiaSharp;
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
                // Anchored to the WHOLE chart and clipped to this pane by the DrawRect below.
                // Per-pane anchoring restarted the fade in every pane, so a chart with a volume
                // pane showed a hard seam where the light end began again.
                var span = ctx.GradientRect;
                bgPaint.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(span.Left, span.Top),
                    new SKPoint(span.Left, span.Bottom),
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
            double niceStep = ChartMath.GridStep(range);
            if (niceStep > 0)
            {
                // A line is MAJOR when the y axis puts a label on it. That used to be "every
                // fifth line", which was a guess about where the labels were: ChartRenderer
                // computed its own step from a different target, so on a pane of range 20 the
                // labels landed at 5 and 15 — between lines, never mind between a major and a
                // minor one. Both files now take the step from ChartMath and the bright line is
                // the labelled line by construction.
                double labelStep = ChartMath.LabelStep(
                    range, ChartMath.TargetLabelCount(ctx.PaneRect.Height, ctx.Density), niceStep);

                double firstLine = Math.Ceiling(ctx.Min / niceStep) * niceStep;
                int safety = 0;
                for (double v = firstLine; v <= ctx.Max && safety < 200; v += niceStep, safety++)
                {
                    float y = ChartMath.MapY(v, ctx.PaneRect.Top, ctx.PaneRect.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                    ctx.Canvas.DrawLine(ctx.PaneRect.Left, y, ctx.PaneRect.Right, y,
                        ChartMath.IsOnLabel(v, labelStep) ? gridMajor : gridMinor);
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
