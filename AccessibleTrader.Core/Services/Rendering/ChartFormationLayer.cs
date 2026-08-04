using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
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

            foreach (var p in onScreen) Draw(ctx, p, firstVisible, lastVisible);
        }

        private static void Draw(RenderContext ctx, ChartPattern p, int firstVisible, int lastVisible)
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

            using var span = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = new SKColor(theme.Accent.Red, theme.Accent.Green, theme.Accent.Blue, 22),
            };
            ctx.Canvas.DrawRect(new SKRect(x1, ctx.PaneRect.Top, x2, ctx.PaneRect.Bottom), span);

            // ── The trigger: solid, because it is a real price ──────────────────
            float yTrigger = YFor(ctx, p.TriggerLevel);
            using var trigger = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.6f * ctx.Density,
                Color = theme.Accent,
            };
            ctx.Canvas.DrawLine(x1, yTrigger, xEnd, yTrigger, trigger);
            Label(ctx, $"{ChartPatternNarrator.Name(p.Kind)} · trigger", x1 + 4 * ctx.Density, yTrigger, theme.Accent);

            // A range has a second boundary and it is every bit as real as the first.
            if (p.SecondaryLevel is double bottom)
            {
                float yBottom = YFor(ctx, bottom);
                ctx.Canvas.DrawLine(x1, yBottom, xEnd, yBottom, trigger);
                Label(ctx, "range · bottom", x1 + 4 * ctx.Density, yBottom, theme.Accent);
            }

            // ── The measured target: faint, dashed, and labelled as a convention ─
            //
            // Never drawn for a formation that did not confirm — there is no break to project from,
            // and a target line hanging under a shape that never triggered is an assertion about
            // something that did not happen.
            if (p.MeasuredTarget is double target && target > 0 && p.State != ChartPatternState.Expired)
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
                Label(ctx, "measured target (untested)", x1 + 4 * ctx.Density, yTarget,
                      new SKColor(theme.Accent.Red, theme.Accent.Green, theme.Accent.Blue, 150));
            }
        }

        private static void Label(RenderContext ctx, string text, float x, float y, SKColor colour)
        {
            using var font = new SKFont(SKTypeface.Default, 10f * ctx.Density);
            using var paint = new SKPaint { IsAntialias = true, Color = colour };
            // Above the line, so the label never sits on top of the price it is annotating.
            ctx.Canvas.DrawText(text, x, y - 3 * ctx.Density, SKTextAlign.Left, font, paint);
        }

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
