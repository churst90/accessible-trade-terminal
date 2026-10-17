using AccessibleTrader.Sdk.Models;
using SkiaSharp;

namespace AccessibleTrader.Core.Services.Rendering
{
    /// <summary>
    /// <b>Any component can shade the background behind each bar from its own value.</b>
    ///
    /// <para>
    /// Until 2026-10-17 the renderer found its two tints by component NAME: Cipher B's
    /// "Anchor Polarity" (teal/red on the price chart by sign) and Top/Bottom Detector's
    /// "Distribution Confidence" (red on the price chart at 0.5 and over, deepening to 1.0). Cody:
    /// <i>"shouldn't this, if tinting is all it does, be a property in the indicator properties?
    /// which means shading/tinting should be something any indicator can do."</i> Both are now
    /// declarations (<see cref="IndicatorComponentMetadata.DefaultBackgroundTint"/> and its four
    /// companions), any component can be given one in Properties, and this class is the one
    /// painter.
    /// </para>
    ///
    /// <para>
    /// <b>The rule, per bar:</b> NaN paints nothing; a value at or above the component's tint
    /// midline (<see cref="ComponentConfig.TintMidline"/>, else its
    /// <see cref="ComponentConfig.ColorBaseline"/> — the same <c>&gt;=</c> its bar and polarity
    /// colours split on) takes the above colour, below takes the below colour; a colour with
    /// alpha 0 paints nothing; with <see cref="ComponentConfig.TintFullAt"/> set the alpha ramps
    /// from 20% at the midline to 100% at that value. The arithmetic is the old Distribution
    /// tint's, operation for operation, so its frames are byte-identical.
    /// </para>
    ///
    /// <para>
    /// Painted straight after the background layer — under the grid, the data and the overlays,
    /// over the pane fill — where both old tints were. A series that is hidden paints nothing; a
    /// HIDDEN COMPONENT still tints, because the tint is its own switch (see
    /// <see cref="BackgroundTint"/>). Visual only: nothing here is spoken or sounded.
    /// </para>
    /// </summary>
    public static class BackgroundTintPainter
    {
        /// <summary>The strength of a tint that borrows the component's own colours (0x16, the
        /// old Anchor Polarity tint's): a whisper, so candles keep their colour on top of it.</summary>
        public const byte BorrowedColourAlpha = 22;

        /// <summary>Every price-chart tint on the chart, whichever pane its component is drawn in.
        /// Call for the price pane's main area only.</summary>
        public static void PaintPriceChartTints(RenderContext ctx, IEnumerable<ChartSeries> allSeries)
        {
            foreach (var s in allSeries)
            {
                if (!s.IsVisible) continue;
                foreach (var c in s.Components)
                    if (c.BackgroundTintInEffect() == BackgroundTint.PriceChart) Paint(ctx, s, c);
            }
        }

        /// <summary>The "this pane" tints of the series drawn in this pane — in its main area when
        /// <see cref="RenderContext.SubPaneFilter"/> is null, else in that sub-pane strip.</summary>
        public static void PaintPaneTints(RenderContext ctx, IEnumerable<ChartSeries> paneSeries)
        {
            foreach (var s in paneSeries)
            {
                if (!s.IsVisible) continue;
                foreach (var c in s.Components)
                {
                    if (c.BackgroundTintInEffect() != BackgroundTint.ThisPane) continue;
                    bool inSubPane = !string.IsNullOrEmpty(c.SubPaneName);
                    if (ctx.SubPaneFilter == null ? inSubPane
                        : !string.Equals(c.SubPaneName, ctx.SubPaneFilter, StringComparison.Ordinal)) continue;
                    Paint(ctx, s, c);
                }
            }
        }

        /// <summary>The colour a tint side resolves to: the declared one, else the component's own
        /// colour at <see cref="BorrowedColourAlpha"/>. Unparseable = nothing.</summary>
        public static SKColor Resolve(string declared, string own)
        {
            if (!string.IsNullOrWhiteSpace(declared))
                return SKColor.TryParse(declared, out var d) ? d : SKColors.Transparent;
            return SKColor.TryParse(own, out var o) ? o.WithAlpha(BorrowedColourAlpha) : SKColors.Transparent;
        }

        private static void Paint(RenderContext ctx, ChartSeries series, ComponentConfig comp)
        {
            var values = series.GetComponentData(comp.Name);
            if (values == null || values.Length == 0 || ctx.ViewportLength <= 0) return;

            var above = Resolve(comp.TintColorAboveHex, comp.ColorHex);
            var below = Resolve(comp.TintColorBelowHex, comp.ColorHexSecondary);
            if (above.Alpha == 0 && below.Alpha == 0) return;

            double mid = comp.TintMidline ?? comp.ColorBaseline;
            double? fullAt = comp.TintFullAt;
            double span = fullAt is double f ? Math.Abs(f - mid) : 0;

            float barWidth = ctx.Width / ctx.ViewportLength;
            using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= values.Length) break;
                double v = values[dataIdx];
                if (double.IsNaN(v)) continue;

                var colour = v >= mid ? above : below;
                if (colour.Alpha == 0) continue;

                byte alpha = colour.Alpha;
                if (fullAt.HasValue)
                {
                    // 20% at the midline → 100% at TintFullAt, clamped beyond it.
                    double scale = span > 0 ? Math.Clamp(Math.Abs(v - mid) / span, 0.0, 1.0) : 1.0;
                    alpha = (byte)(colour.Alpha * (0.2 + 0.8 * scale));
                }
                paint.Color = colour.WithAlpha(alpha);

                float x = ctx.PaneRect.Left + i * barWidth;
                ctx.Canvas.DrawRect(new SKRect(x, ctx.Top, x + barWidth, ctx.Bottom), paint);
            }
        }
    }
}
