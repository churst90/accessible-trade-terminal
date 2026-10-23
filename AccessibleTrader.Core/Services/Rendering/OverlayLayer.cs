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
        ///
        /// <para>
        /// Placement is <see cref="LayoutTextLabels"/>. Until 2026-10-21 a label's plate simply
        /// started at its anchor and ran right, so a label on one of the newest bars, or any long
        /// one, ran under the price axis and was cut off mid-word; two labels on the same bar
        /// printed over each other; and a character the default face lacks (a check mark, any
        /// CJK) drew as an empty box.
        /// </para>
        /// </summary>
        private static void RenderTextLabels(RenderContext ctx, IEnumerable<ChartSeries> series)
        {
            var theme = ctx.Theme;
            var placed = LayoutTextLabels(ctx, series);
            if (placed.Count == 0) return;

            using var font = new SKFont(LabelTypeface, LabelFontPx * ctx.Density);
            using var textPaint = new SKPaint { Color = theme.AxisText, IsAntialias = true };
            using var plate = new SKPaint { Color = theme.SurfaceSunken.WithAlpha(210), Style = SKPaintStyle.Fill };
            using var edge = new SKPaint
            {
                Color = theme.DrawingLine.WithAlpha(180),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1 * ctx.Density,
                IsAntialias = true,
            };
            float pad = LabelPadPx * ctx.Density;

            foreach (var label in placed)
            {
                // A plate that was slid or stacked no longer starts at its bar, so the anchor is
                // marked and tied to the plate.
                if (label.Moved)
                {
                    var b = label.Box;
                    float lx = Math.Clamp(label.AnchorX, b.Left, b.Right);
                    float ly = Math.Clamp(label.AnchorY, b.Top, b.Bottom);
                    ctx.Canvas.DrawLine(label.AnchorX, label.AnchorY, lx, ly, edge);
                    using var dot = new SKPaint { Color = edge.Color, Style = SKPaintStyle.Fill, IsAntialias = true };
                    ctx.Canvas.DrawCircle(label.AnchorX, label.AnchorY, 2.5f * ctx.Density, dot);
                }
                ctx.Canvas.DrawRoundRect(new SKRoundRect(label.Box, 3 * ctx.Density), plate);
                ctx.Canvas.DrawRoundRect(new SKRoundRect(label.Box, 3 * ctx.Density), edge);
                float x = label.Box.Left + pad, baseline = label.Box.Bottom - pad;
                foreach (var (run, face) in GlyphRuns(label.Text))
                {
                    using var runFont = face == LabelTypeface ? null : new SKFont(face, font.Size);
                    var f = runFont ?? font;
                    ctx.Canvas.DrawText(run, x, baseline, SKTextAlign.Left, f, textPaint);
                    x += f.MeasureText(run);
                }
            }
        }

        internal const float LabelFontPx = 12f;
        internal const float LabelPadPx = 4f;
        private const float LabelGapPx = 6f;

        private static readonly SKTypeface LabelTypeface = SKTypeface.Default;

        /// <summary>One label as it will be drawn: the text (possibly shortened) and its plate.</summary>
        internal readonly record struct PlacedTextLabel(string Text, SKRect Box, float AnchorX, float AnchorY, bool Moved);

        /// <summary>
        /// Where each visible label's plate goes.
        ///
        /// <list type="bullet">
        /// <item>Above its anchor, offset so the text does not sit on the price it marks; below it
        /// when above would leave the pane; and always inside the pane top to bottom.</item>
        /// <item>Starting at the anchor and running right, but slid left as far as needed to end
        /// at the pane's right edge, so the newest bars' labels stay whole. A label wider than the
        /// pane is shortened with an ellipsis.</item>
        /// <item>Off every plate already placed: a label that would cover another moves to just
        /// below it (above it when below has no room), so two labels on one bar are both read.</item>
        /// </list>
        ///
        /// </summary>
        internal static List<PlacedTextLabel> LayoutTextLabels(RenderContext ctx, IEnumerable<ChartSeries> series)
        {
            var placed = new List<PlacedTextLabel>();

            using var font = new SKFont(LabelTypeface, LabelFontPx * ctx.Density);
            float pad = LabelPadPx * ctx.Density, gap = LabelGapPx * ctx.Density;
            float h = font.Size + pad * 2;
            var pane = ctx.PaneRect;

            foreach (var s in series)
            {
                if (s.Drawing is not { Type: DrawingType.TextLabel } drawing) continue;
                if (!s.IsVisible || string.IsNullOrWhiteSpace(drawing.Text)) continue;

                var data = s.GetComponentData("Label");
                if (data == null || data.Length == 0) continue;

                // The prompt is one line; a saved workspace edited by hand need not be.
                string text = OneLine(drawing.Text);

                for (int i = 0; i < ctx.ViewportLength; i++)
                {
                    int idx = ctx.ViewportStart + i;
                    if (idx >= data.Length) break;
                    if (double.IsNaN(data[idx])) continue;

                    float x = pane.Left + (i * ctx.ItemWidth) + (ctx.ItemWidth / 2f);
                    float y = ChartMath.MapY(data[idx], ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);

                    string shown = Fit(text, font, pane.Width - pad * 2);
                    float w = MeasureRuns(shown, font) + pad * 2;

                    float left = Math.Max(pane.Left, Math.Min(x, pane.Right - w));
                    float top = y - h - gap;
                    if (top < pane.Top) top = y + gap;
                    top = Math.Clamp(top, pane.Top, Math.Max(pane.Top, pane.Bottom - h));

                    var box = SKRect.Create(left, top, w, h);
                    var home = box;
                    for (int tries = 0; tries <= placed.Count; tries++)
                    {
                        int hit = placed.FindIndex(p => p.Box.IntersectsWith(box));
                        if (hit < 0) break;
                        var other = placed[hit].Box;
                        float below = other.Bottom + 2 * ctx.Density, above = other.Top - 2 * ctx.Density - h;
                        if (below + h <= pane.Bottom) box = SKRect.Create(left, below, w, h);
                        else if (above >= pane.Top) box = SKRect.Create(left, above, w, h);
                        else break; // no room either side: overlap rather than leave the pane
                    }

                    placed.Add(new PlacedTextLabel(shown, box, x, y, Moved: box.Left != x || box.Top != home.Top));
                }
            }
            return placed;
        }

        private static string OneLine(string text) =>
            string.Join(' ', text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        /// <summary>The text, or as much of it as fits <paramref name="maxWidth"/> followed by an ellipsis.</summary>
        private static string Fit(string text, SKFont font, float maxWidth)
        {
            if (MeasureRuns(text, font) <= maxWidth) return text;
            var runes = text.EnumerateRunes().ToList();
            for (int n = runes.Count - 1; n > 0; n--)
            {
                string cut = string.Concat(runes.Take(n).Select(r => r.ToString())).TrimEnd() + "\u2026";
                if (MeasureRuns(cut, font) <= maxWidth) return cut;
            }
            return "\u2026";
        }

        private static float MeasureRuns(string text, SKFont font)
        {
            float w = 0;
            foreach (var (run, face) in GlyphRuns(text))
            {
                if (face == LabelTypeface) { w += font.MeasureText(run); continue; }
                using var f = new SKFont(face, font.Size);
                w += f.MeasureText(run);
            }
            return w;
        }

        // Per character, and one instance per family so neighbouring characters from the same
        // fallback face stay one run. Bounded by what users type.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SKTypeface> FallbackFaces = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SKTypeface> FallbackFamilies = new();

        /// <summary>
        /// The text split into runs that one face can draw: the label face where it has the
        /// character, otherwise whatever installed face the font manager finds for it (a check
        /// mark, CJK), otherwise the label face's missing-glyph box, as before.
        /// </summary>
        internal static List<(string Run, SKTypeface Face)> GlyphRuns(string text)
        {
            var runs = new List<(string, SKTypeface)>();
            var sb = new System.Text.StringBuilder();
            SKTypeface? current = null;
            foreach (var rune in text.EnumerateRunes())
            {
                var face = FaceFor(rune.Value);
                if (current != null && face != current)
                {
                    runs.Add((sb.ToString(), current));
                    sb.Clear();
                }
                current = face;
                sb.Append(rune.ToString());
            }
            if (current != null) runs.Add((sb.ToString(), current));
            return runs;
        }

        private static SKTypeface FaceFor(int codepoint)
        {
            if (codepoint < 0x80 || LabelTypeface.ContainsGlyph(codepoint)) return LabelTypeface;
            return FallbackFaces.GetOrAdd(codepoint, cp =>
                SKFontManager.Default.MatchCharacter(cp) is { } found && found.ContainsGlyph(cp)
                    ? FallbackFamilies.GetOrAdd(found.FamilyName, found)
                    : LabelTypeface);
        }
    }
}
