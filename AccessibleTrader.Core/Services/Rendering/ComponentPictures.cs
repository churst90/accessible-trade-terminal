using AccessibleTrader.Sdk.Models;
using SkiaSharp;

namespace AccessibleTrader.Core.Services.Rendering
{
    /// <summary>
    /// <b>The drawings a component carries beside its marks</b> — boxes, full-height dotted lines,
    /// small signs under a marker, and marks stacked under a sibling's on the same bar. Built for
    /// Cipher C's look (Cody, 2026-10-27: <i>"please add all remaining visual elements from the
    /// original ... Remove our visuals and replace with the original"</i>), but read from
    /// <see cref="ComponentConfig"/> knobs and <see cref="ComponentCompanions"/> arrays, so any
    /// indicator can use them. Pictures only: none is a stop, a sound or a reading; what each one
    /// shows is said by its component's speech.
    /// </summary>
    internal static class ComponentPictures
    {
        // ── Boxes and lines (pass 0, under the candles) ─────────────────────────

        /// <summary>
        /// Every <see cref="ComponentCompanions.BoxEnd"/> box of <paramref name="comp"/> that
        /// reaches the screen, filled in its colour at its <see cref="ComponentConfig.BoxAlpha"/>.
        /// Edges sit on bar centres. The whole history is scanned: a box written on an earlier
        /// bar (or on the last bar, running into the future) can cover the visible ones.
        /// </summary>
        public static void RenderBoxes(RenderContext ctx, ChartSeries series, ComponentConfig comp)
        {
            if (comp.BoxAlpha <= 0 || ctx.ViewportLength <= 0) return;
            var end = series.GetComponentData(comp.Name + ComponentCompanions.BoxEnd);
            if (end.Length == 0) return;
            var start = series.GetComponentData(comp.Name + ComponentCompanions.BoxStart);
            var top = series.GetComponentData(comp.Name + ComponentCompanions.BoxTop);
            var bottom = series.GetComponentData(comp.Name + ComponentCompanions.BoxBottom);
            float barWidth = ctx.Width / ctx.ViewportLength, halfBar = barWidth / 2f;
            int first = ctx.ViewportStart, last = ctx.ViewportStart + ctx.ViewportLength - 1;
            using var lease = SKPaintPool.Rent();
            var p = lease.Paint;
            p.Color = StandardRenderers.StaticColor(comp).WithAlpha((byte)Math.Clamp(comp.BoxAlpha, 0, 255));
            p.Style = SKPaintStyle.Fill;
            p.IsAntialias = false;
            for (int k = 0; k < end.Length; k++)
            {
                if (double.IsNaN(end[k])) continue;
                double s = At(start, k, 0);
                double left = k + Math.Min(s, end[k]), right = k + Math.Max(s, end[k]);
                if (right < first || left > last) continue;
                float x1 = (float)((left - first) * barWidth + halfBar);
                float x2 = (float)((right - first) * barWidth + halfBar);
                double hi = At(top, k, double.NaN), lo = At(bottom, k, double.NaN);
                float y1 = double.IsNaN(hi) ? ctx.Top : ChartMath.MapY(hi, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                float y2 = double.IsNaN(lo) ? ctx.Bottom : ChartMath.MapY(lo, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                var rect = new SKRect(Math.Max(x1, 0), Math.Max(Math.Min(y1, y2), ctx.Top), Math.Min(x2, ctx.Width), Math.Min(Math.Max(y1, y2), ctx.Bottom));
                if (rect.Width <= 0 || rect.Height <= 0) continue;
                ctx.Canvas.DrawRect(rect, p);
            }
        }

        /// <summary>
        /// A dotted vertical line through the whole pane at every visible bar
        /// <paramref name="comp"/> has a value on, in its colour at its
        /// <see cref="ComponentConfig.MarkLineAlpha"/>, one device pixel wide.
        /// </summary>
        public static void RenderMarkLines(RenderContext ctx, ChartSeries series, ComponentConfig comp)
        {
            if (comp.MarkLineAlpha <= 0 || ctx.ViewportLength <= 0) return;
            var data = series.GetComponentData(comp.Name);
            if (data.Length == 0) return;
            float barWidth = ctx.Width / ctx.ViewportLength, halfBar = barWidth / 2f, d = ctx.Density;
            using var dash = SKPathEffect.CreateDash(new[] { 1.5f * d, 2.5f * d }, 0);
            using var lease = SKPaintPool.Rent();
            var p = lease.Paint;
            p.Color = StandardRenderers.StaticColor(comp).WithAlpha((byte)Math.Clamp(comp.MarkLineAlpha, 0, 255));
            p.Style = SKPaintStyle.Stroke;
            p.StrokeWidth = d;
            p.PathEffect = dash;
            p.IsAntialias = false;
            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int k = ctx.ViewportStart + i;
                if (k >= data.Length) break;
                if (double.IsNaN(data[k])) continue;
                float x = i * barWidth + halfBar;
                ctx.Canvas.DrawLine(x, ctx.Top, x, ctx.Bottom, p);
            }
            p.PathEffect = null;
        }

        private static double At(double[] a, int k, double fallback) =>
            k < a.Length && !double.IsNaN(a[k]) ? a[k] : fallback;

        // ── Marker geometry ─────────────────────────────────────────────────────

        /// <summary>A marker's triangle height, as its renderer sizes it.</summary>
        public static float TriangleHeight(RenderContext ctx, ComponentConfig comp) =>
            StandardRenderers.ClampMarkerExtent(Math.Max(comp.Thickness * 3f, 6f) * ctx.Density, ctx);

        /// <summary>The side of one badge (a sign under the ring) and the gap between rows.</summary>
        public static float BadgeSide(float triangleHeight) => triangleHeight * 0.9f;
        public static float BadgeGap(float triangleHeight) => triangleHeight * 0.15f;

        /// <summary>How many rows of signs a <see cref="ComponentCompanions.Badge"/> value draws.</summary>
        public static int BadgeRows(double bits)
        {
            if (double.IsNaN(bits) || bits <= 0) return 0;
            int b = (int)bits, rows = 0;
            for (int bit = 1; bit <= ComponentCompanions.BadgeCross; bit <<= 1) if ((b & bit) != 0) rows++;
            return rows;
        }

        /// <summary>
        /// The word a <see cref="ComponentCompanions.BadgeWord"/> value picks, or null. A word
        /// written <c>Start@#FF9800</c> is printed in white on a plate of that colour; the others in
        /// the marker's colour.
        /// </summary>
        public static string? BadgeWord(ComponentConfig comp, double index) => WordEntry(comp, index).Word;

        /// <summary>The plate colour of the word <paramref name="index"/> picks, if it has one.</summary>
        public static SKColor? BadgePlate(ComponentConfig comp, double index) => WordEntry(comp, index).Plate;

        private static (string? Word, SKColor? Plate) WordEntry(ComponentConfig comp, double index)
        {
            if (double.IsNaN(index) || string.IsNullOrEmpty(comp.BadgeWords)) return (null, null);
            var words = comp.BadgeWords.Split('|');
            int k = (int)Math.Round(index);
            if (k < 0 || k >= words.Length || words[k].Length == 0) return (null, null);
            string entry = words[k];
            int at = entry.IndexOf('@');
            if (at < 0) return (entry, null);
            return (at > 0 ? entry[..at] : null, SKColor.TryParse(entry[(at + 1)..], out var c) ? c : null);
        }

        /// <summary>The badge word's text size for a marker of this triangle height.</summary>
        public static float WordPx(RenderContext ctx, float triangleHeight) => Math.Max(3f * ctx.Density, triangleHeight * 0.75f);

        /// <summary>
        /// Where a marker's base sits on a bar (the y its triangle stands on): beyond the far edge
        /// of the sibling it stacks under (<see cref="ComponentConfig.MarkerStacksUnder"/>) when
        /// that sibling is drawn and marks the bar too, else its own anchored place. Null when
        /// <paramref name="comp"/> has neither a value nor a <see cref="ComponentCompanions.Mark"/> there.
        /// </summary>
        public static float? BaseY(RenderContext ctx, ChartSeries series, ComponentConfig comp, int i, int depth = 0)
        {
            int k = ctx.ViewportStart + i;
            double v = MarkedValue(series, comp, k);
            if (double.IsNaN(v)) return null;
            float h = TriangleHeight(ctx, comp), gap = BadgeGap(h);
            float own = StandardRenderers.ResolveMarkerY(ctx, comp, i, v, h);
            var sibling = Sibling(series, comp);
            if (sibling == null || depth > 4) return own;
            float? edge = FarEdge(ctx, series, sibling, i, depth + 1);
            if (edge == null) return own;
            return comp.MarkerAnchor == MarkerAnchor.AboveBar
                ? Math.Min(own, edge.Value - gap)
                : Math.Max(own, edge.Value + gap + h);
        }

        /// <summary>
        /// The far edge of a ringed marker on a bar, away from the bar: past its ring, its signs
        /// and its word. Null when it has no value there.
        /// </summary>
        public static float? FarEdge(RenderContext ctx, ChartSeries series, ComponentConfig comp, int i, int depth = 0)
        {
            if (!comp.IsVisible || comp.IsEnabled == false || !comp.IsDrawn) return null;
            float? y = BaseY(ctx, series, comp, i, depth);
            if (y == null) return null;
            float h = TriangleHeight(ctx, comp);
            int k = ctx.ViewportStart + i;
            float reach = BelowRing(ctx, series, comp, k, h);
            return comp.MarkerAnchor == MarkerAnchor.AboveBar ? y.Value - 2f * h - 0.45f * h - reach : y.Value + 1.45f * h + reach;
        }

        /// <summary>
        /// What a marker is drawn at on data bar <paramref name="k"/>: the component's value, else
        /// its <see cref="ComponentCompanions.Mark"/>; NaN when neither is set.
        /// </summary>
        public static double MarkedValue(ChartSeries series, ComponentConfig comp, int k)
        {
            var data = series.GetComponentData(comp.Name);
            if (k < 0) return double.NaN;
            if (k < data.Length && !double.IsNaN(data[k])) return data[k];
            var mark = series.GetComponentData(comp.Name + ComponentCompanions.Mark);
            return k < mark.Length ? mark[k] : double.NaN;
        }

        /// <summary>How far a marker's signs and word reach past its ring.</summary>
        private static float BelowRing(RenderContext ctx, ChartSeries series, ComponentConfig comp, int k, float h)
        {
            var bits = series.GetComponentData(comp.Name + ComponentCompanions.Badge);
            int rows = BadgeRows(k < bits.Length ? bits[k] : double.NaN);
            float reach = rows * (BadgeSide(h) + BadgeGap(h));
            var words = series.GetComponentData(comp.Name + ComponentCompanions.BadgeWord);
            if (BadgeWord(comp, k < words.Length ? words[k] : double.NaN) != null) reach += BadgeGap(h) + WordPx(ctx, h);
            return reach;
        }

        private static ComponentConfig? Sibling(ChartSeries series, ComponentConfig comp)
        {
            if (string.IsNullOrEmpty(comp.MarkerStacksUnder)) return null;
            foreach (var c in series.Components)
                if (c.Name == comp.MarkerStacksUnder && !ReferenceEquals(c, comp)) return c;
            return null;
        }

        // ── Badges ───────────────────────────────────────────────────────────────

        private static readonly SKColor CheckGreen = SKColor.Parse("#43A047");
        private static readonly SKColor RefreshBlue = SKColor.Parse("#1E88E5");
        private static readonly SKColor CrossRed = SKColor.Parse("#E53935");

        /// <summary>
        /// The signs of one bar, one per row, from <paramref name="edge"/> away from the bar
        /// (<paramref name="down"/> = down the screen): a clock, a refresh box, a check box, a
        /// cross, in that order; then the word. Returns the far edge reached.
        /// </summary>
        public static float DrawBadges(RenderContext ctx, ComponentConfig comp, float x, float edge, bool down, float h,
            double bits, string? word, SKColor color, SKColor? plate = null)
        {
            float side = BadgeSide(h), gap = BadgeGap(h), half = side / 2f;
            int b = double.IsNaN(bits) ? 0 : (int)bits;
            float at = edge;
            foreach (int bit in new[] { ComponentCompanions.BadgeClock, ComponentCompanions.BadgeRefresh, ComponentCompanions.BadgeCheck, ComponentCompanions.BadgeCross })
            {
                if ((b & bit) == 0) continue;
                float cy = down ? at + gap + half : at - gap - half;
                switch (bit)
                {
                    case ComponentCompanions.BadgeClock: DrawClock(ctx.Canvas, x, cy, half, color, ctx.Density); break;
                    case ComponentCompanions.BadgeRefresh: DrawRefresh(ctx.Canvas, x, cy, half, ctx.Density); break;
                    case ComponentCompanions.BadgeCheck: DrawCheck(ctx.Canvas, x, cy, half, ctx.Density); break;
                    default: DrawCross(ctx.Canvas, x, cy, half, ctx.Density); break;
                }
                at = down ? cy + half : cy - half;
            }
            if (word != null)
            {
                using var font = new SKFont(WordTypeface, WordPx(ctx, h));
                var m = font.Metrics;
                float textH = -m.Ascent + m.Descent;
                float baseline = down ? at + gap - m.Ascent : at - gap - m.Descent;
                using var lease = SKPaintPool.Rent();
                var p = lease.Paint;
                p.Style = SKPaintStyle.Fill; p.IsAntialias = true;
                if (plate is { } fill)
                {
                    float w = font.MeasureText(word), pad = textH * 0.25f;
                    p.Color = fill;
                    ctx.Canvas.DrawRoundRect(new SKRect(x - w / 2f - pad, baseline + m.Ascent - pad * 0.5f, x + w / 2f + pad, baseline + m.Descent + pad * 0.5f), pad, pad, p);
                }
                p.Color = plate != null ? SKColors.White : color;
                ctx.Canvas.DrawText(word, x, baseline, SKTextAlign.Center, font, p);
                at = down ? at + gap + textH : at - gap - textH;
            }
            return at;
        }

        private static readonly SKTypeface WordTypeface =
            SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.Default;

        private static SKPaint Fill(SKPaint p, SKColor c) { p.Color = c; p.Style = SKPaintStyle.Fill; p.IsAntialias = true; return p; }

        private static SKPaint Stroke(SKPaint p, SKColor c, float w)
        {
            p.Color = c; p.Style = SKPaintStyle.Stroke; p.StrokeWidth = w; p.IsAntialias = true;
            p.StrokeCap = SKStrokeCap.Round; p.StrokeJoin = SKStrokeJoin.Round;
            return p;
        }

        /// <summary>A green rounded box with a white check: confirmed.</summary>
        private static void DrawCheck(SKCanvas canvas, float x, float y, float half, float d)
        {
            using var a = SKPaintPool.Rent(); using var b = SKPaintPool.Rent();
            float r = Math.Min(half * 0.32f, 4f * d);
            canvas.DrawRoundRect(new SKRect(x - half, y - half, x + half, y + half), r, r, Fill(a.Paint, CheckGreen));
            using var check = new SKPath();
            check.MoveTo(x - half * 0.45f, y);
            check.LineTo(x - half * 0.12f, y + half * 0.32f);
            check.LineTo(x + half * 0.48f, y - half * 0.36f);
            canvas.DrawPath(check, Stroke(b.Paint, SKColors.White, Math.Max(d, half * 0.24f)));
        }

        /// <summary>A blue rounded box with two white circling arrows: after a cancellation.</summary>
        private static void DrawRefresh(SKCanvas canvas, float x, float y, float half, float d)
        {
            using var a = SKPaintPool.Rent(); using var b = SKPaintPool.Rent();
            float r = Math.Min(half * 0.32f, 4f * d);
            canvas.DrawRoundRect(new SKRect(x - half, y - half, x + half, y + half), r, r, Fill(a.Paint, RefreshBlue));
            var p = Stroke(b.Paint, SKColors.White, Math.Max(d, half * 0.2f));
            float orbit = half * 0.52f, headLen = half * 0.4f, headHalf = half * 0.22f;
            foreach (float startDeg in new[] { 200f, 20f })
            {
                const float sweep = 140f;
                var oval = new SKRect(x - orbit, y - orbit, x + orbit, y + orbit);
                using var arc = new SKPath();
                arc.ArcTo(oval, startDeg, sweep, true);
                canvas.DrawPath(arc, p);
                float e = (startDeg + sweep) * MathF.PI / 180f;
                float ex = x + MathF.Cos(e) * orbit, ey = y + MathF.Sin(e) * orbit;
                float tx = -MathF.Sin(e), ty = MathF.Cos(e), nx = -ty, ny = tx;
                float bx = ex - tx * headLen, by = ey - ty * headLen;
                using var head = new SKPath();
                head.MoveTo(bx + nx * headHalf, by + ny * headHalf);
                head.LineTo(ex, ey);
                head.LineTo(bx - nx * headHalf, by - ny * headHalf);
                canvas.DrawPath(head, p);
            }
        }

        /// <summary>A clock face in the marker's colour with its hands at nine: waiting.</summary>
        private static void DrawClock(SKCanvas canvas, float x, float y, float half, SKColor color, float d)
        {
            using var a = SKPaintPool.Rent(); using var b = SKPaintPool.Rent();
            canvas.DrawCircle(x, y, half * 0.9f, Stroke(a.Paint, color, Math.Max(d, half * 0.16f)));
            var hands = Stroke(b.Paint, color, Math.Max(d, half * 0.16f));
            canvas.DrawLine(x, y, x, y - half * 0.6f, hands);
            canvas.DrawLine(x, y, x - half * 0.45f, y, hands);
        }

        /// <summary>A red cross: cancelled.</summary>
        private static void DrawCross(SKCanvas canvas, float x, float y, float half, float d)
        {
            using var a = SKPaintPool.Rent();
            var p = Stroke(a.Paint, CrossRed, Math.Max(1.5f * d, half * 0.32f));
            float arm = half * 0.7f;
            canvas.DrawLine(x - arm, y - arm, x + arm, y + arm, p);
            canvas.DrawLine(x + arm, y - arm, x - arm, y + arm, p);
        }
    }
}
