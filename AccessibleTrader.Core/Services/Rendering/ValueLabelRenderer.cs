using System.Collections.Concurrent;
using System.Globalization;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using SkiaSharp;

namespace AccessibleTrader.Core.Services.Rendering
{
    /// <summary>
    /// Draws <see cref="ComponentDisplayType.ValueLabel"/>: a component's value printed as text at
    /// its bar — the faithful look for the DBSI-style indicator, whose bull score sits under each
    /// candle in green, its bear score over it in red, and the newest candle's leader in yellow.
    ///
    /// <para>
    /// <b>Placement.</b> Centred on the bar's slot. A bar-anchored label (<see cref="MarkerAnchor.AboveBar"/>
    /// / <see cref="MarkerAnchor.BelowBar"/>) sits <see cref="GapPx"/> clear of the bar AS DRAWN —
    /// <see cref="RenderContext.Data"/> is the Heikin-Ashi bars on a Heikin-Ashi chart — mapped
    /// through the pane's own y mapping (log scale included). It is kept inside the plot: the plot
    /// starts below the pane's status band, so a label is never drawn into it, and a label that
    /// would leave the plot is slid back in (over its own candle's wick, on its plate) rather than
    /// cut in half by the clip.
    /// </para>
    ///
    /// <para>
    /// <b>Legibility.</b> WCAG 1.4.3: the text is the component's colour mixed toward white (dark
    /// chart) or black (light chart) only until it reaches 4.5:1 against its plate over every
    /// surface the plate can cover — the same derivation <see cref="FormationInk"/> uses, against
    /// the same surfaces — so every theme, including ones that do not exist yet, gets ink that
    /// passes. The plate is the chart background at <see cref="FormationInk.PlateAlpha"/>:
    /// invisible on bare chart, and a wick or a line behind a number becomes a trace, not a
    /// strikethrough. Bold, at <see cref="PreferredPx"/> × density.
    /// </para>
    ///
    /// <para>
    /// <b>Clutter.</b> Two rules. (1) The series parameter <c>labelBars</c>, when present and above
    /// zero, limits the labels to the newest that many bars of the series (DBSI's 25: TradingView's
    /// label limit). (2) The font shrinks until the widest label on screen fits its bar's slot with
    /// a device pixel clear on each side (the plate's side padding gives way first), down to
    /// <see cref="MinPx"/> × density; when even that is too wide, NO label of that component is
    /// drawn. All or nothing, deliberately: thinning to every other bar would show
    /// gaps that read as "no value here", and overlapping numbers read as neither. Zoom in and
    /// they come back; the status line and the speech carry the numbers at any zoom.
    /// </para>
    /// </summary>
    public static class ValueLabelRenderer
    {
        /// <summary>Text size at density 1 when the slot allows it, and the smallest it shrinks to.</summary>
        public const float PreferredPx = 11f, MinPx = 7f;

        /// <summary>Clearance between the bar (high or low) and the label's plate, at density 1.</summary>
        public const float GapPx = 3f;

        /// <summary>The series parameter that limits labels to the newest N bars (see <see cref="ComponentDisplayType.ValueLabel"/>).</summary>
        public const string LabelBarsParameter = "labelBars";

        /// <summary>Plate padding around the text, at density 1.</summary>
        public const float PadPx = 1.5f;

        private static readonly SKTypeface LabelTypeface =
            SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.Default;

        /// <summary>One label as laid out: its bar (absolute), text, plate, baseline origin and ink.</summary>
        public readonly record struct PlacedLabel(int BarIndex, string Text, SKRect Plate, float X, float Baseline, SKColor Ink, bool Highlighted);

        /// <summary>
        /// The text a value prints as: a whole number without decimals ("15", "-5"), anything else
        /// with the status line's decimals. ASCII minus, as TradingView prints it.
        /// </summary>
        public static string Text(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "";
            if (Math.Abs(v - Math.Round(v)) < 1e-9 && Math.Abs(v) < 1e15)
                return ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
            return ChartStatusLine.FormatReading(v);
        }

        /// <summary>
        /// The same value as SPEECH says it (Alt+Shift+D, the ladder): a whole number as a whole
        /// number, a negative one with "minus" spelled out ("minus 5" — some voices read "-5" as
        /// "dash 5"), anything else in the magnitude-aware price form.
        /// </summary>
        public static string Spoken(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "";
            if (Math.Abs(v - Math.Round(v)) < 1e-9 && Math.Abs(v) < 1e15)
            {
                long n = (long)Math.Round(v);
                return n < 0 ? "minus " + (-n).ToString(CultureInfo.InvariantCulture) : n.ToString(CultureInfo.InvariantCulture);
            }
            return Accessibility.SpeechPriceFormatter.FormatPrice(v);
        }

        /// <summary>
        /// The first absolute bar index that is labelled: the series' <c>labelBars</c> parameter
        /// (above zero) counted back from the newest bar of the series, else 0 — every bar.
        /// </summary>
        public static int FirstLabelledBar(ChartSeries s, int barCount)
        {
            if (s.Parameters.TryGetValue(LabelBarsParameter, out double n) && n >= 1)
                return Math.Max(0, barCount - (int)Math.Round(n));
            return 0;
        }

        /// <summary>
        /// Whether the label at <paramref name="absIndex"/> takes the highlight colour: it is the
        /// newest bar of the series, the component names a sibling, and this value is above the
        /// sibling's there (DBSI's yellow for the side winning the candle still forming).
        /// </summary>
        public static bool IsHighlighted(ComponentConfig comp, ChartSeries s, int absIndex, int barCount)
        {
            if (absIndex != barCount - 1 || string.IsNullOrEmpty(comp.ColorAboveComponent)) return false;
            var mine = s.GetComponentData(comp.Name);
            var other = s.GetComponentData(comp.ColorAboveComponent);
            if (mine == null || other == null || absIndex >= mine.Length || absIndex >= other.Length) return false;
            double a = mine[absIndex], b = other[absIndex];
            return !double.IsNaN(a) && !double.IsNaN(b) && a > b;
        }

        /// <summary>
        /// The colour the component reads in at a bar (opaque, before the legibility lift): the
        /// highlight on a highlighted bar, the primary colour otherwise. The status line's chip
        /// uses this, so the reading on top matches the number on the candle.
        /// </summary>
        public static SKColor RawColour(ComponentConfig comp, ChartSeries s, int absIndex, int barCount)
        {
            string hex = IsHighlighted(comp, s, absIndex, barCount) ? comp.ColorHexSecondary : comp.ColorHex;
            return SKColor.TryParse(hex, out var c) ? c.WithAlpha(255) : SKColors.White;
        }

        private static readonly ConcurrentDictionary<(ChartTheme, SKColor), SKColor> _inkCache = new();

        /// <summary>
        /// <paramref name="colour"/> made legible on <paramref name="theme"/>: mixed toward the
        /// pole only until it is 4.5:1 against the plate over every surface it can land on.
        /// </summary>
        public static SKColor Ink(ChartTheme theme, SKColor colour)
        {
            if (_inkCache.Count > 256) _inkCache.Clear();
            return _inkCache.GetOrAdd((theme, colour.WithAlpha(255)), key =>
            {
                var surfaces = FormationInk.TextSurfaces(key.Item1);
                return FormationInk.Toward(key.Item2, FormationInk.Pole(surfaces), surfaces, Theming.WcagContrast.TextMinimum);
            });
        }

        /// <summary>The plate colour: the chart background at the formation labels' opacity.</summary>
        public static SKColor PlateColour(ChartTheme theme) => theme.Background.WithAlpha(FormationInk.PlateAlpha);

        /// <summary>
        /// Lays out one component's labels for the frame, without drawing: what
        /// <see cref="Render"/> paints and what tests read. Empty when nothing is labelled, the
        /// component has no data, or the slot is too narrow (see the class comment).
        /// </summary>
        public static IReadOnlyList<PlacedLabel> Layout(RenderContext ctx, ChartSeries s, ComponentConfig comp, out float fontPx)
        {
            fontPx = 0f;
            var result = new List<PlacedLabel>();
            var data = s.GetComponentData(comp.Name);
            if (data == null || data.Length == 0 || ctx.Data == null || ctx.Data.Count == 0) return result;

            float slot = ctx.ItemWidth > 0 ? ctx.ItemWidth : ctx.Width / Math.Max(1, ctx.ViewportLength);
            int barCount = data.Length;
            int first = FirstLabelledBar(s, barCount);
            int visible = Math.Min(ctx.ViewportLength, ctx.Data.Count);

            var items = new List<(int I, int Abs, string Text)>();
            for (int i = 0; i < visible; i++)
            {
                int abs = ctx.ViewportStart + i;
                if (abs < first || abs >= barCount) continue;
                string t = Text(data[abs]);
                if (t.Length == 0) continue;
                items.Add((i, abs, t));
            }
            if (items.Count == 0) return result;

            // The font: as large as preferred, shrunk to the slot, never below the minimum.
            float d = ctx.Density;
            float pad = PadPx * d, gap = GapPx * d;
            using var font = new SKFont(LabelTypeface, PreferredPx * d);
            float widest = items.Max(it => font.MeasureText(it.Text));
            // The text may use the whole slot but one device pixel per side, so two neighbours'
            // numbers never touch; the plate's side padding gives way first (below).
            float fits = slot - 2 * d;
            float size = PreferredPx * d;
            if (widest > fits)
            {
                size = PreferredPx * d * fits / widest;
                if (size < MinPx * d) return result;   // too narrow at any legible size: none at all
                font.Size = size;
            }
            fontPx = size;
            var metrics = font.Metrics;
            float ascent = -metrics.Ascent, descent = metrics.Descent;
            float textHeight = ascent + descent;

            var plateColour = PlateColour(ctx.Theme);
            foreach (var (i, abs, t) in items)
            {
                float cx = ctx.PaneRect.Left + i * slot + slot / 2f;
                float w = font.MeasureText(t);
                float plateH = textHeight + 2 * pad;

                float top;
                if (comp.MarkerAnchor == MarkerAnchor.Value)
                {
                    float y = ChartMath.MapY(data[abs], ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                    top = y - plateH / 2f;
                }
                else
                {
                    var bar = ctx.Data[i];
                    if (comp.MarkerAnchor == MarkerAnchor.BelowBar)
                        top = ChartMath.MapY(bar.Low, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale) + gap;
                    else
                        top = ChartMath.MapY(bar.High, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale) - gap - plateH;
                }
                // Inside the plot, never into the status band above it or past the pane's foot.
                top = Math.Clamp(top, ctx.Top, Math.Max(ctx.Top, ctx.Bottom - plateH));

                float padX = Math.Clamp((slot - w) / 2f - d, 0f, pad);
                var plate = new SKRect(cx - w / 2f - padX, top, cx + w / 2f + padX, top + plateH);
                bool hi = IsHighlighted(comp, s, abs, barCount);
                var ink = Ink(ctx.Theme, RawColour(comp, s, abs, barCount));
                result.Add(new PlacedLabel(abs, t, plate, cx, top + pad + ascent, ink, hi));
            }
            return result;
        }

        /// <summary>
        /// Every value-label plate the pane's visible, drawn ValueLabel components put on screen this
        /// frame — what the formation layer keeps its own labels off.
        /// </summary>
        public static IReadOnlyList<SKRect> Plates(RenderContext ctx, IEnumerable<ChartSeries> series)
        {
            var plates = new List<SKRect>();
            foreach (var s in series)
            {
                if (!s.IsVisible) continue;
                foreach (var c in s.Components)
                {
                    if (c.DisplayType != ComponentDisplayType.ValueLabel || !c.IsVisible || !c.IsEnabled || !c.IsDrawn) continue;
                    if (!string.IsNullOrEmpty(c.SubPaneName)) continue;
                    foreach (var l in Layout(ctx, s, c, out _)) plates.Add(l.Plate);
                }
            }
            return plates;
        }

        public static void Render(RenderContext ctx, ChartSeries s, ComponentConfig comp)
        {
            var labels = Layout(ctx, s, comp, out float fontPx);
            if (labels.Count == 0) return;

            using var font = new SKFont(LabelTypeface, fontPx);
            using var plate = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = PlateColour(ctx.Theme) };
            using var text = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            float r = 2f * ctx.Density;
            foreach (var l in labels)
            {
                ctx.Canvas.DrawRoundRect(l.Plate, r, r, plate);
                text.Color = l.Ink;
                ctx.Canvas.DrawText(l.Text, l.X, l.Baseline, SKTextAlign.Center, font, text);
            }
        }
    }
}
