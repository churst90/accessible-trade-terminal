using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Theming;
using SkiaSharp;

namespace AccessibleTrader.Core.Services.Rendering
{
    /// <summary>
    /// The colours <see cref="ChartFormationLayer"/> draws in: the theme's accent, made light
    /// (or dark) enough to reach the WCAG floors on that theme's chart.
    ///
    /// <para>
    /// ── Why it is derived, not a theme field ───────────────────────────────────
    /// The layer used to draw everything in <c>theme.Accent</c> at fixed opacities. Measured
    /// 2026-10-17 on Cody's default theme (Classic, accent #2962FF on #131722): the label text was
    /// 3.65:1 on its own plate and 2.46:1 where the plate sat over a candle — under WCAG 1.4.3's
    /// 4.5:1 for 10px text — and the dashed measured target, at alpha 110, was 1.63:1, under
    /// 1.4.11's 3:1 for a graphic. All twelve built-in themes failed at least one pair. The
    /// accent is a BUTTON colour, chosen for white ink on top of it; nothing ever held it against
    /// the chart. A new theme field would be one more thing every theme, and every theme a person
    /// builds, has to get right. Deriving it means every theme, including ones that do not exist
    /// yet, gets ink that passes.
    /// </para>
    ///
    /// <para>
    /// ── How ─────────────────────────────────────────────────────────────────────
    /// Each ink starts as the accent, so the labels and lines stay recognisably the formation's
    /// colour, and is mixed toward white (on a dark chart) or black (on a light one) in 1/32 steps
    /// only until it reaches its floor against EVERY surface it can land on. A theme whose accent
    /// already passes keeps it untouched. The fainter lines are then the same ink at the LEAST
    /// opacity that still reaches 3:1 — never less than the opacity they had — so the measured
    /// target is still visibly weaker than the trigger (thinner, dashed, and as faint as a
    /// visible graphic may be) without dropping below what a low-vision reader can see.
    /// </para>
    /// </summary>
    internal readonly record struct FormationInk(
        SKColor Text,
        SKColor TargetText,
        SKColor Plate,
        SKColor Trigger,
        SKColor Target,
        SKColor Boundary,
        SKColor SpanEdge,
        SKColor SpanFill)
    {
        /// <summary>
        /// The label plate's opacity over the chart: 245 of 255, the status line's
        /// (<see cref="ChartStatusLine.DefaultBackingAlpha"/>). It was 205, which let a candle
        /// under a label show through at 20% and pulled the text's contrast down with it (Classic:
        /// 3.65:1 on bare chart, 2.46:1 over a doji). At 245 a line or wick behind the text is a
        /// trace, not a strikethrough, and the plate is still not a solid patch punched in the chart.
        /// </summary>
        public const byte PlateAlpha = 245;

        /// <summary>The opacities the lines were drawn at before 2026-10-17, kept as floors.</summary>
        public const byte TargetAlphaFloor = 110, BoundaryAlphaFloor = 170, SpanEdgeAlphaFloor = 70,
                          TargetTextAlphaFloor = 150, SpanFillAlpha = 9;

        // The last theme asked about and its ink. The search below is a few thousand ratio
        // computations and the layer asks once per formation per frame, with the theme instance
        // unchanged until the person switches theme. One reference write, so a racing render at
        // worst computes it twice.
        private sealed record Cached(ChartTheme Theme, FormationInk Ink);
        private static Cached? _last;

        /// <summary>The ink for <paramref name="theme"/>.</summary>
        public static FormationInk For(ChartTheme theme)
        {
            var last = _last;
            if (last is not null && ReferenceEquals(last.Theme, theme)) return last.Ink;
            var ink = Compute(theme);
            _last = new Cached(theme, ink);
            return ink;
        }

        private static FormationInk Compute(ChartTheme theme)
        {
            var accent = theme.Accent.WithAlpha(255);
            var lines = LineSurfaces(theme);
            var plates = TextSurfaces(theme);

            var text = Toward(accent, Pole(plates), plates, WcagContrast.TextMinimum);
            var trigger = Toward(accent, Pole(lines), lines, WcagContrast.GraphicsMinimum);

            return new FormationInk(
                Text: text,
                TargetText: Faintest(text, TargetTextAlphaFloor, plates, WcagContrast.TextMinimum),
                Plate: theme.Background.WithAlpha(PlateAlpha),
                Trigger: trigger,
                Target: Faintest(trigger, TargetAlphaFloor, lines, WcagContrast.GraphicsMinimum),
                Boundary: Faintest(trigger, BoundaryAlphaFloor, lines, WcagContrast.GraphicsMinimum),
                SpanEdge: Faintest(trigger, SpanEdgeAlphaFloor, lines, WcagContrast.GraphicsMinimum),
                SpanFill: accent.WithAlpha(SpanFillAlpha));
        }

        /// <summary>
        /// What a LINE sits on: the chart at both ends of its gradient — the same pair
        /// <see cref="ThemeContrastChecks"/> holds the candles to, because a level can be anywhere.
        /// </summary>
        public static SKColor[] LineSurfaces(ChartTheme theme) =>
            new[] { theme.Background.WithAlpha(255), (theme.BackgroundGradientEnd ?? theme.Background).WithAlpha(255) };

        /// <summary>
        /// What a LABEL's text sits on: its plate, composited over everything the plate can cover
        /// — either end of the chart, either candle's body and wick, a doji, and another
        /// formation's line. The worst of these is the ratio the text is held to.
        /// </summary>
        public static SKColor[] TextSurfaces(ChartTheme theme)
        {
            var plate = theme.Background.WithAlpha(PlateAlpha);
            var under = new[]
            {
                theme.Background, theme.BackgroundGradientEnd ?? theme.Background,
                theme.CandleBullishBody, theme.CandleBearishBody,
                theme.CandleBullishWick, theme.CandleBearishWick, theme.CandleDojiBody,
                theme.Accent,
            };
            return under.Select(u => Over(plate, u.WithAlpha(255))).ToArray();
        }

        /// <summary>The worst ratio of <paramref name="ink"/> against any of <paramref name="surfaces"/>.</summary>
        public static double WorstRatio(SKColor ink, IEnumerable<SKColor> surfaces) =>
            surfaces.Min(s => WcagContrast.Ratio(ink, s));

        /// <summary>White or black, whichever stands further from the worst of these surfaces.</summary>
        internal static SKColor Pole(SKColor[] surfaces) =>
            WorstRatio(SKColors.White, surfaces) >= WorstRatio(SKColors.Black, surfaces) ? SKColors.White : SKColors.Black;

        /// <summary>
        /// <paramref name="from"/> mixed toward <paramref name="pole"/> in 1/32 steps, stopping at
        /// the first mix that reaches <paramref name="minimum"/> on every surface; the pole itself
        /// when none does (a theme whose chart is so mid-grey that nothing passes everywhere).
        /// </summary>
        internal static SKColor Toward(SKColor from, SKColor pole, SKColor[] surfaces, double minimum)
        {
            for (int k = 0; k <= 32; k++)
            {
                var c = Mix(from, pole, k / 32.0);
                if (WorstRatio(c, surfaces) >= minimum) return c;
            }
            return pole;
        }

        /// <summary>
        /// <paramref name="ink"/> at the least opacity, no lower than <paramref name="floor"/>,
        /// that reaches <paramref name="minimum"/> on every surface; opaque when none does.
        /// </summary>
        private static SKColor Faintest(SKColor ink, byte floor, SKColor[] surfaces, double minimum)
        {
            for (int a = floor; a < 255; a++)
            {
                var c = ink.WithAlpha((byte)a);
                if (WorstRatio(c, surfaces) >= minimum) return c;
            }
            return ink.WithAlpha(255);
        }

        private static SKColor Mix(SKColor a, SKColor b, double t) => new(
            (byte)Math.Round(a.Red + (b.Red - a.Red) * t),
            (byte)Math.Round(a.Green + (b.Green - a.Green) * t),
            (byte)Math.Round(a.Blue + (b.Blue - a.Blue) * t));

        private static SKColor Over(SKColor fg, SKColor bg)
        {
            double a = fg.Alpha / 255.0;
            return new SKColor(
                (byte)Math.Round(fg.Red * a + bg.Red * (1 - a)),
                (byte)Math.Round(fg.Green * a + bg.Green * (1 - a)),
                (byte)Math.Round(fg.Blue * a + bg.Blue * (1 - a)));
        }
    }
}
