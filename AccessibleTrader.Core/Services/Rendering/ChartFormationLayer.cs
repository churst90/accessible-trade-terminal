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
            for (int rank = 0; rank < onScreen.Count; rank++)
                Draw(ctx, onScreen[rank], firstVisible, lastVisible, takenLabelRows, LabelsEverything(rank));
        }

        /// <summary>
        /// Whether a formation at this dominance rank labels its floor and target as well as its
        /// name. Only the DOMINANT one does: three formations used to produce up to SIX labels
        /// (name + "target" each), which is a stack of text no stagger can make readable — visible
        /// in a screenshot of a real BTC chart carrying an ascending triangle, a symmetrical
        /// triangle and a flag. The others still draw their lines; they just say their name once.
        /// The spoken readout makes the same choice — describe one, count the rest.
        /// </summary>
        internal static bool LabelsEverything(int rank) => rank == 0;

        private static void Draw(RenderContext ctx, ChartPattern p, int firstVisible, int lastVisible,
            List<float> takenLabelRows, bool labelLevels)
        {
            var theme = ctx.Theme;

            // The formation's own span, clamped to the viewport so a shape running off the left
            // edge still draws the part that is visible rather than vanishing.
            float x1 = XFor(ctx, Math.Max(p.StartBarIndex, firstVisible));
            float x2 = XFor(ctx, Math.Min(p.EndBarIndex, lastVisible) + 1);

            // Levels extend PAST the formation, because a trigger only matters for the bars that
            // come after the shape — drawing it only under the shape would put the line everywhere
            // except where it is used. But they STOP where the formation's story stops. They used
            // to run to the right edge forever: Cody's 2026-10-15 BTCUSDT daily screenshot had a
            // March rising wedge's trigger (solid, 75,000) and measured target (dashed, 60,300)
            // still crossing the chart in October, months after price had closed through the one
            // and traded at the other — so a sighted reader could not tell a finished formation
            // from a live one. Now:
            //   trigger → the bar that closed through it, or the bar the formation aged out on
            //             (ChartPattern.ResolvesAt; for a live formation that is its expiry, which
            //             is at or past the last bar, so it still reaches the live edge);
            //   target  → the first bar that traded at it, or the end of its window
            //             (ChartPattern.TargetEndsAt).
            // Both are causal: each end is a bar already on the chart, found scanning forward
            // from the break, never a peek past it.
            float xTriggerEnd = XEndAt(ctx, p.ResolvesAt);
            float xTargetEnd = XEndAt(ctx, p.TargetEndsAt);

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

            // ── A triangle or wedge: its two sloping edges ──────────────────────
            //
            // Cody's 2026-10-15 screenshot showed a "rising wedge" as one flat line at 75,000. A
            // sighted reader expects a wedge to be two converging lines, and the detector had them
            // — it classifies the shape from exactly these two slopes. Drawn across the
            // formation's span, lighter than the trigger: they show the shape, the trigger is the
            // number to watch.
            bool hasEdges = p.UpperBoundary is not null && p.LowerBoundary is not null;
            if (hasEdges)
            {
                using var boundary = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.2f * ctx.Density,
                    Color = new SKColor(theme.Accent.Red, theme.Accent.Green, theme.Accent.Blue, 170),
                };
                DrawBoundary(ctx, p.UpperBoundary!, p.StartBarIndex, p.EndBarIndex, boundary);
                DrawBoundary(ctx, p.LowerBoundary!, p.StartBarIndex, p.EndBarIndex, boundary);
            }

            // Where the levels begin. With the edges drawn, a flat trigger from the formation's
            // first bar would cut straight through the wedge it belongs to; it starts instead
            // where the edge it continues ends — the swing whose price it is — and carries on flat
            // from there to the break. Without edges (tops, bottoms, flags, ranges) it is the
            // formation's first bar, as before.
            float xLevels = hasEdges
                ? Math.Max(x1, XFor(ctx, TriggerPivotBar(p)) + ctx.ItemWidth / 2f)
                : x1;

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
            ctx.Canvas.DrawLine(xLevels, yTrigger, xTriggerEnd, yTrigger, trigger);
            Label(ctx, TriggerLabel(p), xLevels + 4 * ctx.Density, yTrigger, theme.Accent, takenLabelRows);

            // A range has a second boundary and it is every bit as real as the first.
            if (p.SecondaryLevel is double bottom && IsOnScreen(ctx, bottom))
            {
                float yBottom = YFor(ctx, bottom);
                ctx.Canvas.DrawLine(x1, yBottom, xTriggerEnd, yBottom, trigger);
                if (labelLevels)
                    Label(ctx, SecondaryLabel(p, bottom), x1 + 4 * ctx.Density, yBottom, theme.Accent, takenLabelRows);
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
                ctx.Canvas.DrawLine(xLevels, yTarget, xTargetEnd, yTarget, targetPaint);
                // Named, because a chart carrying three formations drew three lines all labelled
                // "measured target" and none of them said whose.
                if (labelLevels)
                    Label(ctx, TargetLabel(p, target), xLevels + 4 * ctx.Density, yTarget,
                          new SKColor(theme.Accent.Red, theme.Accent.Green, theme.Accent.Blue, 150), takenLabelRows);
            }
        }

        // ── What each line is called ─────────────────────────────────────────────
        //
        // The labels used to be the formation's bare name on the solid line and "<name> target" on
        // the dashed one, with no numbers. On Cody's 2026-10-15 screenshot that meant a sighted
        // reader saw "rising wedge" over a flat line at 75,000 and could not tell what the line
        // was — the edge of the wedge? support? — or what the faint dashed one below it meant.
        // Each label now says the line's ROLE, in the same word the speech uses
        // (ChartPatternNarrator.TriggerName: neckline, trigger, flag edge, top), and its price,
        // and the target says "measured" as the speech does. Internal so the tests read these.

        /// <summary>"rising wedge trigger 74,508.12, break below" — the solid line.</summary>
        internal static string TriggerLabel(ChartPattern p)
        {
            string name = ChartPatternNarrator.Name(p.Kind);
            string role = ChartPatternNarrator.TriggerName(p.Kind);

            // "bear flag flag edge": the role repeats the name's last word for flags. Speech keeps
            // the two apart with a comma ("bear flag forming, flag edge …"); a label has no comma
            // to spare, so the repeat goes.
            string lastWord = name[(name.LastIndexOf(' ') + 1)..];
            if (role.StartsWith(lastWord + " ", StringComparison.Ordinal)) role = role[(lastWord.Length + 1)..];

            string text = $"{name} {role} {LabelPrice(p.TriggerLevel)}";

            // A range has not chosen a side — naming one would invent a direction the shape does
            // not have (the speech makes the same refusal). Everything else confirms on one side
            // of its line, and the side is what turns a flat line into "the breakdown level".
            return p.Kind == ChartPatternKind.Rectangle
                ? text
                : $"{text}, break {(p.BreaksBelow ? "below" : "above")}";
        }

        /// <summary>"range bottom 61,200.00" — a range's second solid line.</summary>
        internal static string SecondaryLabel(ChartPattern p, double level) =>
            $"{ChartPatternNarrator.Name(p.Kind)} bottom {LabelPrice(level)}";

        /// <summary>"rising wedge measured target 60,300.00" — the dashed line.</summary>
        internal static string TargetLabel(ChartPattern p, double target) =>
            $"{ChartPatternNarrator.Name(p.Kind)} measured target {LabelPrice(target)}";

        /// <summary>
        /// A price as the labels show it: thousands grouped, as the hover readout groups them
        /// ("82,366.23"), with the same magnitude-scaled decimals the speech uses
        /// (SpeechPriceFormatter: about three significant digits, never fewer than two
        /// decimals), so the number a sighted companion reads off the line is the number Cody
        /// heard, digit for digit.
        /// </summary>
        internal static string LabelPrice(double price)
        {
            if (double.IsNaN(price) || double.IsInfinity(price)) return "no data";
            double abs = Math.Abs(price);
            if (abs == 0) return "0";
            int decimals = Math.Clamp(2 - (int)Math.Floor(Math.Log10(abs)), 2, 10);
            return price.ToString("N" + decimals, System.Globalization.CultureInfo.InvariantCulture);
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
            using var font = new SKFont(SKTypeface.Default, 10f * ctx.Density);
            using var paint = new SKPaint { IsAntialias = true, Color = colour };

            float width = font.MeasureText(text);
            float row = NextLabelRow(ctx, taken, y, x, width);

            // A backing plate, because these labels land on top of candles. In the screenshot that
            // prompted this, "head and shoulders" ran straight across a green candle body and
            // "descending triangle" over a support line — legible only if you already knew what it
            // said. Staggering them fixed labels colliding with EACH OTHER; this fixes them
            // colliding with the chart.
            float pad = 3f * ctx.Density;
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
        internal static float NextLabelRow(RenderContext ctx, List<float> taken, float y,
            float x = float.NaN, float width = 0f)
        {
            float lineHeight = 12f * ctx.Density;
            float row = y - 3 * ctx.Density;

            // Push down past anything already occupying this row — and past the rect the
            // renderer says it will paint over this pane (the legend), when the label's own
            // horizontal extent overlaps it. The legend sits top-left, and so does the label of
            // any formation running off the left edge, which is most of them on a live chart.
            bool Blocked(float r) =>
                taken.Any(t => Math.Abs(t - r) < lineHeight)
                || (ctx.Avoid is SKRect a && !float.IsNaN(x)
                    && x < a.Right && x + width > a.Left
                    && r > a.Top && r - lineHeight < a.Bottom);

            // Never above the pane: a label drawn above the top edge is simply lost. Clamped
            // BEFORE the collision walk, because a label pushed up to the top row lands in the
            // legend's corner — clamping afterwards put it back on top of the thing it had
            // just stepped away from.
            row = Math.Max(row, ctx.PaneRect.Top + lineHeight);
            while (Blocked(row)) row += lineHeight;
            row = Math.Min(row, ctx.PaneRect.Bottom - 2 * ctx.Density);
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
        /// The bar of the swing whose price is the trigger: the last low for a formation that
        /// confirms below, the last high for one that confirms above — the detector's own rule
        /// (<c>trigger = breakBelow ? l2 : h2</c>).
        /// </summary>
        internal static int TriggerPivotBar(ChartPattern p) =>
            p.BreaksBelow ? p.LowerBoundary!.ToBar : p.UpperBoundary!.ToBar;

        /// <summary>
        /// One edge, straight in SCREEN space through its two pivots and extended across the
        /// formation's span. Straight on screen rather than in price, so on a log chart it is the
        /// line a person would rule through the two swings — and both pivots, being real prices,
        /// land exactly where the candles that made them are.
        /// </summary>
        private static void DrawBoundary(RenderContext ctx, FormationLine line, int fromBar, int toBar, SKPaint paint)
        {
            if (line.ToBar == line.FromBar) return;
            float half = ctx.ItemWidth / 2f;
            float xa = XFor(ctx, line.FromBar) + half, ya = YFor(ctx, line.FromPrice);
            float xb = XFor(ctx, line.ToBar) + half, yb = YFor(ctx, line.ToPrice);
            float slope = (yb - ya) / (xb - xa);

            float xs = XFor(ctx, fromBar) + half, xe = XFor(ctx, toBar) + half;
            ctx.Canvas.DrawLine(xs, ya + slope * (xs - xa), xe, ya + slope * (xe - xa), paint);
        }

        /// <summary>
        /// Where a level that ends at <paramref name="barIndex"/> stops: the centre of that bar,
        /// so the line visibly meets the candle that ended it, clamped to the pane so a live
        /// level whose end lies in the future still reaches the right edge.
        /// </summary>
        private static float XEndAt(RenderContext ctx, int barIndex) =>
            Math.Min(ctx.PaneRect.Right, XFor(ctx, barIndex) + ctx.ItemWidth / 2f);

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
