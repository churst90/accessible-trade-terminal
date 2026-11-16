using SkiaSharp;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;

namespace AccessibleTrader.Core.Services.Rendering
{
    /// <summary>
    /// Market Cipher B's own colours, swapped at draw time on a theme that cannot show them (Cody,
    /// 2026-11-16, taking the recommendation): its translucent white lines turn grey
    /// (<see cref="CipherBProvider.LevelGrey"/>) on a light chart, and its translucent waves are
    /// drawn opaque where neither wave reaches 3:1 at MCB's 70% (Steel Gray's mid-grey, 2.995:1).
    ///
    /// <para>
    /// Only a colour still at MCB's value is swapped: a colour the user chose is drawn as chosen,
    /// and nothing is written back, so saved charts keep MCB's colours and look as MCB on every
    /// other theme. The test is the chart's background, not the theme's name, so a custom theme
    /// built from Paper or Steel Gray is treated as its background is.
    /// </para>
    /// </summary>
    public static class ThemeAdaptedColours
    {
        /// <summary>A light chart: its background's relative luminance above one half
        /// (Paper 0.96, High Contrast Light 1; the darkest built-in, Steel Gray, 0.08).</summary>
        public static bool IsLight(ChartTheme theme) => WcagContrast.RelativeLuminance(theme.Background) > 0.5;

        /// <summary>The colour a level is drawn in on <paramref name="theme"/>.</summary>
        public static string Level(string hex, ChartTheme theme) =>
            IsLight(theme) && (Same(hex, CipherBProvider.SolidWhite) || Same(hex, CipherBProvider.DottedWhite))
                ? CipherBProvider.LevelGrey
                : hex;

        /// <summary>
        /// True when neither MCB wave reaches <see cref="WcagContrast.GraphicsMinimum"/> against
        /// the chart at MCB's opacity: the fast wave alone, nor the slow one over it (the
        /// silhouette <c>CipherBLookTests</c> checks on every theme).
        /// </summary>
        public static bool WavesOpaque(ChartTheme theme)
        {
            var bg = theme.Background;
            var fast = SKColor.Parse(CipherBProvider.FastWaveHex);
            var slow = SKColor.Parse(CipherBProvider.SlowWaveHex);
            double alone = WcagContrast.Ratio(fast, bg);
            double body = WcagContrast.Ratio(WcagContrast.Over(slow, WcagContrast.Over(fast, bg)), bg);
            return Math.Max(alone, body) < WcagContrast.GraphicsMinimum;
        }

        /// <summary>
        /// The component as it is drawn on <paramref name="theme"/>: itself, or a copy whose MCB
        /// wave colours are opaque. The copy is for this frame's paint only.
        /// </summary>
        public static ComponentConfig Component(ComponentConfig comp, ChartTheme theme)
        {
            if (!IsWave(comp.ColorHex) && !IsWave(comp.FillColorHex)) return comp;
            if (!WavesOpaque(theme)) return comp;
            var drawn = comp.Clone();
            if (IsWave(drawn.ColorHex)) drawn.ColorHex = Opaque(drawn.ColorHex!);
            if (IsWave(drawn.FillColorHex)) drawn.FillColorHex = Opaque(drawn.FillColorHex!);
            return drawn;
        }

        private static bool IsWave(string? hex) =>
            Same(hex, CipherBProvider.FastWaveHex) || Same(hex, CipherBProvider.SlowWaveHex);

        /// <summary>"#B390CAF9" → "#FF90CAF9".</summary>
        private static string Opaque(string hex) => "#FF" + hex[3..];

        private static bool Same(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
