using AccessibleTrader.Core.Services.Analysis;
using SkiaSharp;

namespace AccessibleTrader.Core.Services.Rendering
{
    /// <summary>
    /// Draws chart formations — the span they occupy, the level that confirms them, and the
    /// conventional measured target.
    ///
    /// <para>
    /// ── Who this is for ────────────────────────────────────────────────────────
    /// The blind user already has the whole formation by ear: name, state, trigger, target,
    /// containment. This layer exists for <b>everyone else looking at the same screen</b> — a
    /// low-vision user, a sighted trading partner, a screenshot in a bug report. Until now the
    /// spoken description and the picture were describing the same chart with no visible link
    /// between them, so a sighted person could not check what the terminal had said.
    /// </para>
    ///
    /// <para>
    /// ── The honesty problem, and how the drawing solves it ─────────────────────
    /// Speech can hedge; a line cannot. "Measured target 39,400 <i>if it breaks</i>" is careful,
    /// but a bold line at 39,400 reads as <i>target</i> — the drawing quietly asserts what the
    /// wording was at pains not to. So the two levels are drawn with deliberately different
    /// weight:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>The trigger is solid.</b> It is a real price that really is where the formation
    ///         confirms — the same status as any support line.</item>
    ///   <item><b>The measured target is faint and dashed</b>, and labelled "measured". It is
    ///         arithmetic on the formation's height, it is a convention, and this project has never
    ///         tested it. The visual weight is the disclaimer.</item>
    /// </list>
    ///
    /// <para>
    /// ── Off by default ─────────────────────────────────────────────────────────
    /// Settings → Appearance. A chart carrying five formations at once becomes unreadable, and the
    /// audience for this layer is the secondary one.
    /// </para>
    /// </summary>
    public sealed class ChartFormationLayer
    {
        /// <summary>
        /// How many formations to draw at once. A region can satisfy four definitions and drawing
        /// all of them produces a thicket that hides the price it is describing — the same reason
        /// the spoken readout describes one and counts the rest.
        /// </summary>
        internal const int MaxDrawn = 3;

        public void Render(RenderContext ctx, IReadOnlyList<ChartPattern> formations)
        {
            if (formations.Count == 0) return;
            // Formations describe price, so they belong on the price pane and nowhere else.
            if (!string.Equals(ctx.PaneName, "Main", StringComparison.OrdinalIgnoreCase)) return;

            int firstVisible = ctx.ViewportStart;
            int lastVisible = ctx.ViewportStart + ctx.ViewportLength - 1;

            // Only formations whose span overlaps what is on screen, ranked so the one drawn most
            // prominently is the one the readout would have led with.
            var onScreen = ChartPatternNarrator.ByDominance(
                    formations.Where(p => p.EndBarIndex >= firstVisible && p.StartBarIndex <= lastVisible))
                .Take(MaxDrawn)
                .ToList();

            // Labels are placed with knowledge of the ones already drawn, so three formations whose
            // triggers sit at nearly the same price do not overprint into an unreadable smear —
            // which is exactly what the first version produced on a real BTC chart.
            var takenLabelRows = new List<float>();
            foreach (var p in onScreen) Draw(ctx, p, firstVisible, lastVisible, takenLabelRows);
        }

        private static void Draw(RenderContext ctx, ChartPattern p, int firstVisible, int lastVisible,
            List<float> takenLabelRows)
        {
            var theme = ctx.Theme;

            // The formation's own span, clamped to the viewport so a shape running off the left
            // edge still draws the part that is visible rather than vanishing.
            float x1 = XFor(ctx, Math.Max(p.StartBarIndex, firstVisible));
            float x2 = XFor(ctx, Math.Min(p.EndBarIndex, lastVisible) + 1);

            // Levels extend PAST the formation to the right edge, because a trigger only matters
            // for the bars that come after the shape — drawing it only under the shape would put
            // the line everywhere except where it is used.
            float xEnd = ctx.PaneRect.Right;

            // A formation can span a third of the chart, and three of them at alpha 22 turned the
            // whole pane into bands of colour with the price action swimming underneath — visible in
            // a screenshot of a real KAS daily chart. The span is context, not the subject: it is
            // barely-there fill plus a marked edge at each end, so you can see WHERE the shape
            // begins and ends without the shading competing with the candles.
            using var span = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = new SKColor(theme.Accent.Red, theme.Accent.Green, theme.Accent.Blue, 9),
            };
            ctx.Canvas.DrawRect(new SKRect(x1, ctx.PaneRect.Top, x2, ctx.PaneRect.Bottom), span);

            using var edge = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f * ctx.Density,
                Color = new SKColor(theme.Accent.Red, theme.Accent.Green, theme.Accent.Blue, 70),
            };
            ctx.Canvas.DrawLine(x1, ctx.PaneRect.Top, x1, ctx.PaneRect.Bottom, edge);
            ctx.Canvas.DrawLine(x2, ctx.PaneRect.Top, x2, ctx.PaneRect.Bottom, edge);

            // ── The trigger: solid, because it is a real price ──────────────────
            //
            // Skipped entirely when the level is off the visible price range. A line clamped to the
            // top or bottom edge does not say "this level is off screen", it says "this level is
            // HERE" — and on a chart whose scale has been stretched by something else, a trigger
            // from a formation years old would be drawn as though it were current price.
            if (!IsOnScreen(ctx, p.TriggerLevel)) return;

            float yTrigger = YFor(ctx, p.TriggerLevel);
            using var trigger = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.6f * ctx.Density,
                Color = theme.Accent,
            };
            ctx.Canvas.DrawLine(x1, yTrigger, xEnd, yTrigger, trigger);
            Label(ctx, ChartPatternNarrator.Name(p.Kind), x1 + 4 * ctx.Density, yTrigger, theme.Accent, takenLabelRows);

            // A range has a second boundary and it is every bit as real as the first.
            if (p.SecondaryLevel is double bottom && IsOnScreen(ctx, bottom))
            {
                float yBottom = YFor(ctx, bottom);
                ctx.Canvas.DrawLine(x1, yBottom, xEnd, yBottom, trigger);
                Label(ctx, $"{ChartPatternNarrator.Name(p.Kind)} floor", x1 + 4 * ctx.Density, yBottom, theme.Accent, takenLabelRows);
            }

            // ── The measured target: faint, dashed, and labelled as a convention ─
            //
            // Never drawn for a formation that did not confirm — there is no break to project from,
            // and a target line hanging under a shape that never triggered is an assertion about
            // something that did not happen.
            if (p.MeasuredTarget is double target && target > 0
                && p.State != ChartPatternState.Expired && IsOnScreen(ctx, target))
            {
                float yTarget = YFor(ctx, target);
                using var dash = SKPathEffect.CreateDash(new[] { 6f * ctx.Density, 6f * ctx.Density }, 0);
                using var targetPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1f * ctx.Density,
                    PathEffect = dash,
                    Color = new SKColor(theme.Accent.Red, theme.Accent.Green, theme.Accent.Blue, 110),
                };
                ctx.Canvas.DrawLine(x1, yTarget, xEnd, yTarget, targetPaint);
                // Named, because a chart carrying three formations drew three lines all labelled
                // "measured target" and none of them said whose.
                Label(ctx, $"{ChartPatternNarrator.Name(p.Kind)} target", x1 + 4 * ctx.Density, yTarget,
                      new SKColor(theme.Accent.Red, theme.Accent.Green, theme.Accent.Blue, 150), takenLabelRows);
            }
        }

        /// <summary>
        /// A label above its line, nudged clear of labels already placed and kept inside the pane.
        ///
        /// <para>
        /// The first version drew every label at its own line's y with no awareness of the others.
        /// On a real chart carrying three formations whose triggers sat within a few hundred dollars
        /// of each other, all three labels landed on the same pixel row and overprinted into an
        /// illegible smear — and any label near the top of the range was drawn above the pane
        /// entirely. Both were obvious in a screenshot and invisible to a test that only asks
        /// whether drawing throws.
        /// </para>
        /// </summary>
        private static void Label(RenderContext ctx, string text, float x, float y, SKColor colour,
            List<float> taken)
        {
            float row = NextLabelRow(ctx, taken, y);

            using var font = new SKFont(SKTypeface.Default, 10f * ctx.Density);
            using var paint = new SKPaint { IsAntialias = true, Color = colour };

            // A backing plate, because these labels land on top of candles. In the screenshot that
            // prompted this, "head and shoulders" ran straight across a green candle body and
            // "descending triangle" over a support line — legible only if you already knew what it
            // said. Staggering them fixed labels colliding with EACH OTHER; this fixes them
            // colliding with the chart.
            float pad = 3f * ctx.Density;
            float width = font.MeasureText(text);
            var plate = new SKRect(x - pad, row - font.Size, x + width + pad, row + pad);

            using var platePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = new SKColor(ctx.Theme.Background.Red, ctx.Theme.Background.Green,
                                    ctx.Theme.Background.Blue, 205),
            };
            ctx.Canvas.DrawRoundRect(plate, 2f * ctx.Density, 2f * ctx.Density, platePaint);

            ctx.Canvas.DrawText(text, x, row, SKTextAlign.Left, font, paint);
        }

        /// <summary>
        /// Where the next label goes: above its own line, nudged clear of every row already taken,
        /// and clamped inside the pane. Appends the chosen row to <paramref name="taken"/>.
        ///
        /// <para>
        /// <c>internal</c> rather than private purely so the tests can assert THIS rule instead of
        /// a copy of it. <c>ChartFormationLayerTests</c> used to reimplement these six lines
        /// in the test file and assert the reimplementation, on the reasoning that exposing the
        /// rule would invite a caller to depend on it — sound as far as it goes, but the result was
        /// two tests that could not fail no matter what the layer did. Internal keeps it off the
        /// public surface while letting the assertion reach the real thing.
        /// </para>
        /// </summary>
        internal static float NextLabelRow(RenderContext ctx, List<float> taken, float y)
        {
            float lineHeight = 12f * ctx.Density;
            float row = y - 3 * ctx.Density;

            // Push down past anything already occupying this row.
            while (taken.Any(t => Math.Abs(t - row) < lineHeight)) row += lineHeight;

            // Never outside the pane: a label drawn above the top edge is simply lost.
            row = Math.Clamp(row, ctx.PaneRect.Top + lineHeight, ctx.PaneRect.Bottom - 2 * ctx.Density);
            taken.Add(row);
            return row;
        }

        /// <summary>
        /// Whether a price is inside the pane's visible range. Anything outside is not drawn at all
        /// rather than clamped to an edge — a clamped line asserts a level is somewhere it is not.
        /// </summary>
        private static bool IsOnScreen(RenderContext ctx, double price) =>
            price >= ctx.Min && price <= ctx.Max;

        private static float XFor(RenderContext ctx, int barIndex) =>
            ctx.PaneRect.Left + (barIndex - ctx.ViewportStart) * ctx.ItemWidth;

        /// <summary>
        /// Price to pixels, honouring log scale — a target drawn with linear maths on a log chart
        /// lands somewhere that is not the price it claims to be.
        /// </summary>
        private static float YFor(RenderContext ctx, double price)
        {
            double min = ctx.Min, max = ctx.Max;
            if (ctx.IsLogScale && price > 0 && min > 0 && max > 0)
            {
                double lo = Math.Log(min), hi = Math.Log(max);
                double t = hi > lo ? (Math.Log(price) - lo) / (hi - lo) : 0;
                return ctx.PaneRect.Bottom - (float)(t * ctx.PaneRect.Height);
            }

            double range = max - min;
            double frac = range > 0 ? (price - min) / range : 0;
            return ctx.PaneRect.Bottom - (float)(frac * ctx.PaneRect.Height);
        }
    }
}
