using System;
using AccessibleTrader.Sdk.Theming;
using SkiaSharp;

namespace AccessibleTrader.Core.Services.Theming
{
    /// <summary>
    /// Collapses a theme's three independent bands — toolbar, chart, footer — into slices of one
    /// continuous top-to-bottom fade.
    ///
    /// <para>
    /// Lining the seams up by hand is what a theme author does (Steel Gray does exactly that), but
    /// it is fiddly and it is not something a user editing two colour pickers should have to get
    /// right. This makes "one fade across the whole window" a switch: pick the colour at the very
    /// top and the colour at the very bottom, and every band takes the value the fade has reached
    /// where that band sits.
    /// </para>
    ///
    /// <para>
    /// The stops are NOMINAL. The colour of a band has to be decided before layout — Skia paints
    /// the canvas from <see cref="ChartTheme.Background"/> and CSS paints the chrome from custom
    /// properties, and neither knows the other's height at that moment. So these are the
    /// proportions the window actually has in normal use, not measurements. A user who resizes
    /// into an extreme aspect ratio gets a fade that is very slightly off rather than a seam,
    /// because adjacent bands still share their boundary value — which is the property that
    /// matters and the one a hand-tuned palette usually gets wrong.
    /// </para>
    /// </summary>
    public static class UnifiedGradient
    {
        /// <summary>Where the toolbar band ends and the chart begins, as a fraction of window height.</summary>
        public const double ChartTopStop = 0.26;

        /// <summary>Where the chart ends and the footer band begins.</summary>
        public const double ChartBottomStop = 0.88;

        /// <summary>
        /// Returns <paramref name="theme"/> with all three bands rewritten as slices of a single
        /// fade from <paramref name="top"/> to <paramref name="bottom"/>.
        /// </summary>
        public static ChartTheme Apply(ChartTheme theme, SKColor top, SKColor bottom)
        {
            ArgumentNullException.ThrowIfNull(theme);

            SKColor chartTop    = Lerp(top, bottom, ChartTopStop);
            SKColor chartBottom = Lerp(top, bottom, ChartBottomStop);

            return theme with
            {
                // Toolbar band: the very top of the window down to where the canvas starts.
                SurfaceRaised = top,
                ChromeTopEnd  = chartTop,

                // The canvas picks up exactly where the toolbar left off and hands off exactly
                // where the footer begins. Shared boundary values are what removes the seams.
                Background            = chartTop,
                BackgroundGradientEnd = chartBottom,

                ChromeBottom    = chartBottom,
                ChromeBottomEnd = bottom,
            };
        }

        /// <summary>Linear interpolation between two colours, per channel, alpha included.</summary>
        public static SKColor Lerp(SKColor a, SKColor b, double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            return new SKColor(
                (byte)Math.Round(a.Red   + (b.Red   - a.Red)   * t),
                (byte)Math.Round(a.Green + (b.Green - a.Green) * t),
                (byte)Math.Round(a.Blue  + (b.Blue  - a.Blue)  * t),
                (byte)Math.Round(a.Alpha + (b.Alpha - a.Alpha) * t));
        }
    }
}
