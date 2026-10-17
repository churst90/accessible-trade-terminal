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
            // Every colour below comes from FormationInk: the accent, lifted only as far as this
            // theme's chart needs for the text to reach 4.5:1 and the lines 3:1 (see there).
            var ink = FormationInk.For(ctx.Theme);

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
                Color = ink.SpanFill,
            };
            ctx.Canvas.DrawRect(new SKRect(x1, ctx.PaneRect.Top, x2, ctx.PaneRect.Bottom), span);

            // The span's two edges reach 3:1 like every other line here (at alpha 70 they were
            // 1.34:1 on Classic), but DOTTED: two full-height solid lines per formation at that
            // contrast would compete with the candles the way a loud grid does, and these mark
            // where the shape begins and ends — context, not a level to read a price off. Each
            // dot is the colour that passes; the gaps are what keep them quiet.
            using var dots = SKPathEffect.CreateDash(new[] { 2f * ctx.Density, 4f * ctx.Density }, 0);
            using var edge = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f * ctx.Density,
                PathEffect = dots,
                Color = ink.SpanEdge,
            };
            float e1 = Crisp(ctx, x1), e2 = Crisp(ctx, x2);
            ctx.Canvas.DrawLine(e1, ctx.PaneRect.Top, e1, ctx.PaneRect.Bottom, edge);
            ctx.Canvas.DrawLine(e2, ctx.PaneRect.Top, e2, ctx.PaneRect.Bottom, edge);

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
                    Color = ink.Boundary,
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
                Color = ink.Trigger,
            };
            ctx.Canvas.DrawLine(xLevels, yTrigger, xTriggerEnd, yTrigger, trigger);
            Label(ctx, TriggerLabel(p), xLevels + 4 * ctx.Density, xTriggerEnd, yTrigger, ink.Text, ink.Plate, takenLabelRows);

            // A range has a second boundary and it is every bit as real as the first.
            if (p.SecondaryLevel is double bottom && IsOnScreen(ctx, bottom))
            {
                float yBottom = YFor(ctx, bottom);
                ctx.Canvas.DrawLine(x1, yBottom, xTriggerEnd, yBottom, trigger);
                if (labelLevels)
                    Label(ctx, SecondaryLabel(p, bottom), x1 + 4 * ctx.Density, xTriggerEnd, yBottom, ink.Text, ink.Plate, takenLabelRows);
            }

            // ── The measured target: faint, dashed, and labelled as a convention ─
            //
            // Never drawn for a formation that did not confirm — there is no break to project from,
            // and a target line hanging under a shape that never triggered is an assertion about
            // something that did not happen.
            if (p.MeasuredTarget is double target && target > 0
                && p.State != ChartPatternState.Expired && IsOnScreen(ctx, target))
            {
                // On the pixel grid: a 1px line centred between two rows is drawn as two rows at
                // half strength, and half of a 3:1 colour is not 3:1 on screen.
                float yTarget = Crisp(ctx, YFor(ctx, target));
                using var dash = SKPathEffect.CreateDash(new[] { 6f * ctx.Density, 6f * ctx.Density }, 0);
                using var targetPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1f * ctx.Density,
                    PathEffect = dash,
                    Color = ink.Target,
                };
                ctx.Canvas.DrawLine(xLevels, yTarget, xTargetEnd, yTarget, targetPaint);
                // Named, because a chart carrying three formations drew three lines all labelled
                // "measured target" and none of them said whose.
                if (labelLevels)
                    Label(ctx, TargetLabel(p, target), xLevels + 4 * ctx.Density, xTargetEnd, yTarget,
                          ink.TargetText, ink.Plate, takenLabelRows);
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

        /// <summary>"rising wedge trigger 74,508.12, downward breakout" — the solid line.</summary>
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
            // Worded in Bulkowski's terms (Cody, 2026-10-17): "downward breakout" once a close went
            // through, "confirms on a downward breakout" while it has not — the label must not say
            // a break happened that has not — and "held" when it aged out unbroken.
            if (p.Kind == ChartPatternKind.Rectangle) return text;
            return p.State switch
            {
                ChartPatternState.Completed => $"{text}, {ChartPatternNarrator.BreakoutName(p)}",
                ChartPatternState.Expired   => $"{text}, held",
                // "an upward", "a downward" — Cody's 2026-10-17 screenshot read "confirms on a upward".
                _                           => $"{text}, confirms on {(p.BreaksBelow ? "a" : "an")} {ChartPatternNarrator.BreakoutName(p)}",
            };
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
        /// A label beside its line, placed where it covers the least price, nudged clear of labels
        /// already placed and kept inside the pane.
        ///
        /// <para>
        /// The first version drew every label at its own line's y with no awareness of the others.
        /// On a real chart carrying three formations whose triggers sat within a few hundred dollars
        /// of each other, all three labels landed on the same pixel row and overprinted into an
        /// illegible smear — and any label near the top of the range was drawn above the pane
        /// entirely. Both were obvious in a screenshot and invisible to a test that only asks
        /// whether drawing throws. Where it goes along and beside the line is
        /// <see cref="PlaceLabel"/>.
        /// </para>
        /// </summary>
        private static void Label(RenderContext ctx, string text, float x, float xLineEnd, float y,
            SKColor colour, SKColor plateColour, List<float> taken)
        {
            using var font = new SKFont(SKTypeface.Default, 10f * ctx.Density);
            using var paint = new SKPaint { IsAntialias = true, Color = colour };

            float pad = 3f * ctx.Density;
            (text, x) = FitInPane(text, x, ctx.PaneRect, pad, t => font.MeasureText(t));
            float width = font.MeasureText(text);
            (x, float row) = PlaceLabel(ctx, taken, y, x, xLineEnd, width, font.Size, pad);

            // A backing plate, because a label can still land on candles when there is nowhere
            // clear to put it. In the screenshot that prompted this, "head and shoulders" ran
            // straight across a green candle body and "descending triangle" over a support line —
            // legible only if you already knew what it said. Staggering them fixed labels colliding
            // with EACH OTHER; this fixes them colliding with the chart.
            var plate = PlateRect(x, row, width, font.Size, pad);

            using var platePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = plateColour,
            };
            ctx.Canvas.DrawRoundRect(plate, 2f * ctx.Density, 2f * ctx.Density, platePaint);

            ctx.Canvas.DrawText(text, x, row, SKTextAlign.Left, font, paint);
        }

        /// <summary>The plate behind a label whose text starts at <paramref name="x"/> on baseline <paramref name="row"/>.</summary>
        internal static SKRect PlateRect(float x, float row, float width, float fontSize, float pad) =>
            new(x - pad, row - fontSize, x + width + pad, row + pad);

        // What a placement costs beyond the price it covers, in square device pixels. Displacement
        // is per pixel the label moved from where it asked to be (a stagger past another label or
        // the legend) — about four wicks' worth per row, so a label leaves its line's side only to
        // get clear of real candles. The other two only break ties: above before below, the line's
        // start before further along it.
        private const float DisplacementPerPx = 4f, BelowTieBreak = 1f, SlideTieBreak = 0.5f;
        private const int SlideSteps = 8;

        /// <summary>
        /// Where a label goes: the text's left x and its baseline. Appends the chosen row to
        /// <paramref name="taken"/>.
        ///
        /// <para>
        /// It used to be "just above the line, at the line's start", and on Cody's 2026-10-17
        /// BTCUSDT daily chart "symmetrical triangle trigger 65,415.69, upward breakout" sat on top
        /// of the August breakout candles — a triangle's trigger starts at the swing that made it,
        /// which is right where price is about to break through it. Now there are 18 candidates:
        /// above and below the line, each at nine points from the line's start to where the label
        /// would still end on the line (or, on a line shorter than the label, to the line's
        /// middle, so the label still starts on it). Each is staggered past taken rows and the legend exactly
        /// as before (<see cref="NextLabelRow"/>'s walk), and scored by the area of candle body and
        /// wick its plate would cover (<see cref="PriceInkUnder"/>), plus a cost for being pushed
        /// off its line and a large one for covering its OWN line. The cheapest wins; ties go to
        /// above, then to the line's start — which is the old placement, so a label with nothing
        /// under it does not move. Deterministic, and about 18 × the bars under one label.
        /// </para>
        /// </summary>
        internal static (float X, float Row) PlaceLabel(RenderContext ctx, List<float> taken, float y,
            float x, float xLineEnd, float width, float fontSize, float pad)
        {
            float d = ctx.Density;
            // Furthest right the label may start: still ending on its line — or, for a line shorter
            // than its label (a trigger that broke a few bars after the swing), starting in the
            // line's first half — and always inside the pane.
            float endsOnLine = xLineEnd - pad - width;
            float startsInFirstHalf = x + (xLineEnd - x) / 2f;
            float xMax = Math.Min(Math.Max(endsOnLine, startsInFirstHalf), ctx.PaneRect.Right - pad - width);

            float bestCost = float.MaxValue;
            (float X, float Row) best = (x, float.NaN);
            for (int side = 0; side < 2; side++)
            {
                bool below = side == 1;
                // Above: the plate's bottom on the line (as before). Below: its top 2px under it.
                float want = below ? y + fontSize + 2 * d : y - 3 * d;
                // A label that cannot sit below without being clamped back up over its line does not try.
                if (below && want > ctx.PaneRect.Bottom - 2 * d) continue;

                for (int k = 0; k <= SlideSteps; k++)
                {
                    if (k > 0 && !(xMax > x)) break;
                    float cx = k == 0 ? x : x + (xMax - x) * k / SlideSteps;
                    float row = WalkRow(ctx, taken, want, cx, width);
                    var plate = PlateRect(cx, row, width, fontSize, pad);

                    float cost = PriceInkUnder(ctx, plate)
                        + DisplacementPerPx * d * Math.Abs(row - want)
                        + (below ? BelowTieBreak : 0f) + SlideTieBreak * k;
                    // Covering its own line hides the very thing it names.
                    if (plate.Top < y - d && plate.Bottom > y + d) cost += plate.Width * plate.Height;

                    if (cost < bestCost) { bestCost = cost; best = (cx, row); }
                }
            }

            taken.Add(best.Row);
            return best;
        }

        /// <summary>
        /// How much candle a rect would cover, in square pixels: the overlap of
        /// <paramref name="r"/> (grown by a pixel, so a label touching a wick counts) with every
        /// visible bar's body and wicks, laid out as the candle renderer lays them out — bodies 80%
        /// of the bar's width, a 1px wick — plus any <see cref="RenderContext.TextObstacles"/> it overlaps. Reads <see cref="RenderContext.Data"/>, which is the
        /// bars as drawn (Heikin-Ashi when the chart is), indexed from the viewport's first bar.
        /// </summary>
        internal static float PriceInkUnder(RenderContext ctx, SKRect r)
        {
            float w = ctx.ItemWidth;
            if (!(w > 0)) return 0f;
            r.Inflate(ctx.Density, ctx.Density);

            var data = ctx.Data;
            int n = Math.Min(ctx.ViewportLength, data.Count);
            int i0 = Math.Max(0, (int)Math.Floor((r.Left - ctx.PaneRect.Left) / w) - 1);
            int i1 = Math.Min(n - 1, (int)Math.Ceiling((r.Right - ctx.PaneRect.Left) / w) + 1);
            float half = Math.Max(0.5f, MathF.Floor(w * 0.4f));

            float area = 0f;
            for (int i = i0; i <= i1; i++)
            {
                var b = data[i];
                float cx = ctx.PaneRect.Left + i * w + w / 2f;
                float yHigh = YFor(ctx, (double)b.High), yLow = YFor(ctx, (double)b.Low);
                float yOpen = YFor(ctx, (double)b.Open), yClose = YFor(ctx, (double)b.Close);
                float top = Math.Min(yOpen, yClose), bottom = Math.Max(Math.Max(yOpen, yClose), top + 1f);

                area += Overlap(r, cx - half, top, cx + half, bottom);
                area += Overlap(r, cx - 0.5f, yHigh, cx + 0.5f, top);
                area += Overlap(r, cx - 0.5f, bottom, cx + 0.5f, yLow);
            }
            // Numbers already printed on the candles (value labels) count as covered ground too:
            // a formation label over "15" hides the number a sighted reader checks against what
            // was spoken.
            if (ctx.TextObstacles is { Count: > 0 } obstacles)
                foreach (var o in obstacles) area += Overlap(r, o.Left, o.Top, o.Right, o.Bottom);
            return area;
        }

        private static float Overlap(SKRect r, float left, float top, float right, float bottom)
        {
            float w = Math.Min(r.Right, right) - Math.Max(r.Left, left);
            float h = Math.Min(r.Bottom, bottom) - Math.Max(r.Top, top);
            return w > 0 && h > 0 ? w * h : 0f;
        }

        /// <summary>
        /// A 1-device-pixel line's coordinate, moved onto the pixel grid. Drawn between two rows,
        /// antialiasing paints both at half strength, and half of a colour chosen to reach 3:1
        /// does not.
        /// </summary>
        private static float Crisp(RenderContext ctx, float v) =>
            ((int)MathF.Round(ctx.Density) % 2 == 1) ? MathF.Floor(v) + 0.5f : MathF.Round(v);

        /// <summary>
        /// Keeps a label — plate included — inside the pane. A label starts at its line's left end,
        /// so a formation near the right edge ran its text off the plot and under the price axis
        /// (Cody's 2026-10-17 screenshot: "ascending triangle trigger 87,219.40, confirms on a upward
        /// bre…"). It slides LEFT until it fits, and only a label longer than the whole pane is
        /// shortened, with an ellipsis, so the words are never cut mid-glyph by the axis.
        /// </summary>
        internal static (string Text, float X) FitInPane(string text, float x, SKRect pane, float pad, Func<string, float> measure)
        {
            float room = pane.Width - 2 * pad;
            if (measure(text) > room)
            {
                string t = text;
                while (t.Length > 1 && measure(t + "…") > room) t = t[..^1];
                text = t.TrimEnd(' ', ',') + "…";
            }
            float w = measure(text);
            if (x + w + pad > pane.Right) x = pane.Right - pad - w;
            if (x - pad < pane.Left) x = pane.Left + pad;
            return (text, x);
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
            float row = WalkRow(ctx, taken, y - 3 * ctx.Density, x, width);
            taken.Add(row);
            return row;
        }

        /// <summary>
        /// The stagger: from the row a label asks for, down past every row already taken and the
        /// legend, clamped inside the pane. Shared by <see cref="NextLabelRow"/> and every
        /// candidate <see cref="PlaceLabel"/> weighs; appends nothing.
        /// </summary>
        private static float WalkRow(RenderContext ctx, List<float> taken, float row, float x, float width)
        {
            float lineHeight = 12f * ctx.Density;

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
