using SkiaSharp;

namespace AccessibleTrader.Core.Services.Theming
{
    /// <summary>
    /// The WCAG 2.x contrast ratio, implemented once so that every place the app judges "can
    /// this be read" measures the same thing.
    ///
    /// <para>
    /// Before this existed the app had three proxies and no ratio: a gamma-less luminance
    /// (<see cref="ThemeCssBridge.Luminance"/>), luminance deltas against hand-picked thresholds
    /// in the theme tests, and squared Euclidean RGB distance in the theme editor. None of them
    /// is a contrast ratio, and the Euclidean one waved through <c>#0000ff</c> on <c>#000000</c>
    /// (distance 65,025 against a 12,000 threshold) which is 2.44:1 — below every WCAG floor.
    /// </para>
    ///
    /// <para>
    /// The formula is the one in WCAG 2.2 §1.4.3: each sRGB channel is linearised (values at or
    /// below 0.04045 divide by 12.92, the rest go through <c>((c + 0.055) / 1.055) ^ 2.4</c>),
    /// relative luminance is <c>0.2126 R + 0.7152 G + 0.0722 B</c>, and the ratio is
    /// <c>(L1 + 0.05) / (L2 + 0.05)</c> with the lighter colour on top, so it runs from 1 (the
    /// same colour) to 21 (black on white). A translucent foreground is composited over the
    /// background first, because that is the colour the eye actually sees.
    /// </para>
    /// </summary>
    public static class WcagContrast
    {
        /// <summary>WCAG 1.4.3 (AA): body text needs at least this against its background.</summary>
        public const double TextMinimum = 4.5;

        /// <summary>WCAG 1.4.3 (AA): large text (18pt, or 14pt bold) needs at least this.</summary>
        public const double LargeTextMinimum = 3.0;

        /// <summary>
        /// WCAG 1.4.11 (AA): a graphical object or UI-component boundary — a candle, a focus
        /// ring, a crosshair — needs at least this against what it sits on.
        /// </summary>
        public const double GraphicsMinimum = 3.0;

        /// <summary>
        /// Relative luminance, 0 (black) to 1 (white), with the sRGB transfer curve applied.
        /// Alpha is ignored; composite first if it matters.
        /// </summary>
        public static double RelativeLuminance(SKColor c) =>
            0.2126 * Linear(c.Red) + 0.7152 * Linear(c.Green) + 0.0722 * Linear(c.Blue);

        /// <summary>
        /// The contrast ratio of <paramref name="foreground"/> drawn over
        /// <paramref name="background"/>, 1 to 21. A translucent foreground is composited over
        /// the background before measuring; a translucent background is treated as opaque,
        /// since nothing here knows what is behind it.
        /// </summary>
        public static double Ratio(SKColor foreground, SKColor background)
        {
            var fg = foreground.Alpha == 255 ? foreground : Over(foreground, background);
            double l1 = RelativeLuminance(fg);
            double l2 = RelativeLuminance(background);
            if (l1 < l2) (l1, l2) = (l2, l1);
            return (l1 + 0.05) / (l2 + 0.05);
        }

        /// <summary>True when the pair reaches <paramref name="minimum"/>.</summary>
        public static bool Passes(SKColor foreground, SKColor background, double minimum) =>
            Ratio(foreground, background) >= minimum;

        /// <summary>
        /// Of the candidates, the one with the highest ratio against <paramref name="background"/>.
        /// Used to pick a focus ring or button ink per theme rather than by hand.
        /// </summary>
        public static SKColor MostContrasting(SKColor background, params SKColor[] candidates)
        {
            if (candidates.Length == 0) throw new ArgumentException("At least one candidate is needed.", nameof(candidates));
            var best = candidates[0];
            double bestRatio = Ratio(best, background);
            for (int i = 1; i < candidates.Length; i++)
            {
                double r = Ratio(candidates[i], background);
                if (r > bestRatio) { best = candidates[i]; bestRatio = r; }
            }
            return best;
        }

        /// <summary>The ratio formatted the way the guidelines write it: "4.52:1".</summary>
        public static string Format(double ratio) =>
            ratio.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ":1";

        private static double Linear(byte channel)
        {
            double c = channel / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        private static SKColor Over(SKColor fg, SKColor bg)
        {
            double a = fg.Alpha / 255.0;
            return new SKColor(
                (byte)Math.Round(fg.Red   * a + bg.Red   * (1 - a)),
                (byte)Math.Round(fg.Green * a + bg.Green * (1 - a)),
                (byte)Math.Round(fg.Blue  * a + bg.Blue  * (1 - a)));
        }
    }
}
