using SkiaSharp;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Rendering
{
    // Dependency-free by design: every colour this layer draws with comes off the
    // RenderContext's theme snapshot, not from ThemeService. It used to take the service in
    // its constructor and never read it, which made it look like it could observe theme
    // changes independently of a render pass. It cannot, and does not need to.
    public class OverlayLayer : IRenderLayer
    {
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

            RenderTextLabels(ctx, series);
        }

        /// <summary>
        /// Draws the text of every Text Label drawing beside its anchor.
        ///
        /// <para>
        /// The Text Label tool placed an anchor and rendered a marker, and its text was never
        /// drawn, never spoken and — because nothing could set it — never non-empty. It was a
        /// point marker wearing the name of a text tool. This is the visible half; the speech
        /// half lives in the drawing's series name so it is announced on navigation, and the
        /// entry half is the prompt shown when the tool is used.
        /// </para>
        /// </summary>
        private static void RenderTextLabels(RenderContext ctx, IEnumerable<ChartSeries> series)
        {
            var theme = ctx.Theme;

            using var font = new SKFont(SKTypeface.Default, 12 * ctx.Density);
            using var textPaint = new SKPaint { Color = theme.AxisText, IsAntialias = true };
            using var plate = new SKPaint { Color = theme.SurfaceSunken.WithAlpha(210), Style = SKPaintStyle.Fill };
            using var edge = new SKPaint
            {
                Color = theme.DrawingLine.WithAlpha(180),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1 * ctx.Density,
                IsAntialias = true,
            };

            foreach (var s in series)
            {
                if (s.Drawing is not { Type: DrawingType.TextLabel } drawing) continue;
                if (!s.IsVisible || string.IsNullOrWhiteSpace(drawing.Text)) continue;

                var data = s.GetComponentData("Label");
                if (data == null || data.Length == 0) continue;

                for (int i = 0; i < ctx.ViewportLength; i++)
                {
                    int idx = ctx.ViewportStart + i;
                    if (idx >= data.Length) break;
                    if (double.IsNaN(data[idx])) continue;

                    float x = (i * ctx.ItemWidth) + (ctx.ItemWidth / 2f);
                    float y = ChartMath.MapY(data[idx], ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);

                    float w = font.MeasureText(drawing.Text);
                    float pad = 4 * ctx.Density;
                    float h = font.Size + pad * 2;

                    // Offset above the anchor so the text does not sit on the price it marks, and
                    // flipped below when that would push it off the top of the pane.
                    float top = y - h - (6 * ctx.Density);
                    if (top < ctx.Top) top = y + (6 * ctx.Density);

                    var box = new SKRect(x, top, x + w + pad * 2, top + h);
                    ctx.Canvas.DrawRoundRect(new SKRoundRect(box, 3 * ctx.Density), plate);
                    ctx.Canvas.DrawRoundRect(new SKRoundRect(box, 3 * ctx.Density), edge);
                    ctx.Canvas.DrawText(drawing.Text, box.Left + pad, box.Bottom - pad,
                        SKTextAlign.Left, font, textPaint);
                }
            }
        }
    }
}
