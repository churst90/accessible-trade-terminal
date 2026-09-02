using System.Globalization;
using AccessibleTrader.Sdk.Theming;
using SkiaSharp;

namespace AccessibleTrader.Core.Services.Theming
{
    /// <summary>
    /// Turns a <see cref="ChartTheme"/> into the CSS custom properties the application chrome
    /// reads, so one theme covers the whole window instead of stopping at the canvas edge.
    ///
    /// <para>
    /// Before this existed, <c>app.css</c> declared a fixed <c>:root</c> block — a dark grey
    /// toolbar and white text, regardless of which theme the chart was using. Switching to the
    /// light theme produced a white chart in a near-black frame. The seam between a themed canvas
    /// and unthemed chrome is the single most unfinished-looking thing about the application, and
    /// no amount of work inside the renderer can fix it, because the toolbars are HTML.
    /// </para>
    ///
    /// <para>
    /// Pure and static on purpose: the mapping is the part worth testing, and it needs no JS
    /// runtime, no DOM and no service graph to test. The host calls
    /// <see cref="BuildCss"/> and hands the result to a one-line JS shim.
    /// </para>
    /// </summary>
    public static class ThemeCssBridge
    {
        /// <summary>
        /// The variables the chrome consumes. Every name here must exist in <c>app.css</c>'s
        /// <c>:root</c> fallback block too, so the app still renders if the bridge never runs
        /// (a JS failure must degrade to the old fixed palette, not to unstyled HTML).
        /// </summary>
        public static IReadOnlyList<string> VariableNames { get; } = new[]
        {
            "--bg-primary",
            "--bg-toolbar",
            "--bg-toolbar-end",
            "--bg-footer",
            "--bg-footer-end",
            "--bg-header",
            "--bg-surface",
            "--text-on-surface",
            "--text-primary",
            "--text-muted",
            "--border-color",
            "--accent-color",
            "--text-on-accent",
            "--btn-neutral",
            "--chart-fade-top",
            "--focus-outline-color",
            "--bullish-color",
            "--bearish-color",
            "--crosshair-color",
        };

        /// <summary>
        /// Maps a theme to <c>{ variable → CSS colour }</c>.
        /// </summary>
        public static IReadOnlyDictionary<string, string> BuildVariables(ChartTheme theme)
        {
            ArgumentNullException.ThrowIfNull(theme);

            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // The window behind everything. Matches the chart's DARK end, so scrollbars and
                // any gap around the canvas read as part of the chart rather than a border.
                ["--bg-primary"]   = Css(theme.ChromeBottomEnd ?? theme.ChromeBottom),

                // Three independent bands. Each carries its own fade, so a theme can make the
                // toolbar, the chart and the footer three unrelated colours, or line the seams up
                // for one continuous window-wide fade. Steel does the latter; nothing requires it.
                ["--bg-toolbar"]     = Css(theme.SurfaceRaised),
                ["--bg-toolbar-end"] = Css(theme.ChromeTopEnd ?? theme.SurfaceRaised),
                ["--bg-footer"]      = Css(theme.ChromeBottom),
                ["--bg-footer-end"]  = Css(theme.ChromeBottomEnd ?? theme.ChromeBottom),
                ["--bg-header"]      = Css(theme.SurfaceRaised),

                // Dialog bodies and panels, with their own ink: a theme may perfectly well pair
                // a dark toolbar with a parchment dialog, and then the two need opposite text.
                ["--bg-surface"]      = Css(theme.SurfaceSunken),
                ["--text-on-surface"] = Css(theme.TextOnDialog),

                ["--text-primary"] = Css(theme.TextPrimary),
                ["--text-muted"]   = Css(theme.TextMuted),
                ["--border-color"] = Css(theme.ChromeBorder),
                ["--accent-color"] = Css(theme.Accent),

                // Ink for text sitting ON the accent — primary buttons, chiefly. This was a
                // literal #0c0f14 in app.css, which is correct only while the accent stays
                // light; the accent is a theme value, so a dark accent made the label of the
                // most important button in every dialog unreadable. Same reasoning as the
                // focus ring below: measure, do not hand-pick per theme.
                ["--text-on-accent"] = Css(InkOn(theme.Accent)),
                ["--btn-neutral"]  = Css(theme.ButtonNeutral),

                // The chart's TOP colour, exposed so the toolbar directly above the canvas can
                // blend into it. This is what makes the window read as one surface rather than a
                // canvas dropped into a frame.
                ["--chart-fade-top"] = Css(theme.Background),

                // The focus ring has to stay visible against whatever the chrome became. Yellow on
                // a light theme is nearly invisible, and a focus ring you cannot see is the same as
                // no keyboard navigation at all for a low-vision user.
                ["--focus-outline-color"] = Css(FocusRingFor(theme)),

                // Up/down colours, so HTML that reports direction (P&L, order side, the trading
                // dashboard) agrees with the candles instead of using its own green and red.
                ["--bullish-color"] = Css(theme.CandleBullishBody),
                ["--bearish-color"] = Css(theme.CandleBearishBody),

                // The HTML hover crosshair, so it agrees with the one Skia draws instead of
                // using its own colour. It was a hardcoded rgba(255,255,255,0.45), which is
                // 1.00:1 on High Contrast Light and 1.02:1 on Paper — the only visual marker of
                // which bar the cursor is on, invisible on exactly the themes a low-vision user
                // picks. Same reasoning as the focus ring: the chrome is themeable, so anything
                // drawn on it has to be measured against the theme, not hand-picked once.
                ["--crosshair-color"] = Css(theme.Crosshair),
            };
        }

        /// <summary>
        /// The variables as a CSS declaration block, ready to drop into a <c>:root</c> rule or be
        /// applied one property at a time from JS.
        /// </summary>
        public static string BuildCss(ChartTheme theme) =>
            string.Join("", BuildVariables(theme).Select(kv => $"{kv.Key}:{kv.Value};"));

        /// <summary>
        /// A focus ring that stays visible on this theme's chrome.
        ///
        /// <para>
        /// The old ring was a fixed <c>#ffff00</c>. That is excellent on black and close to
        /// invisible on the light theme's #EEE toolbar. Rather than pick per theme by hand — which
        /// is a thing to forget when a theme is added — this measures the chrome's luminance and
        /// picks the high-contrast end: yellow on dark surfaces, deep blue on light ones.
        /// </para>
        /// </summary>
        public static SKColor FocusRingFor(ChartTheme theme) =>
            Luminance(theme.SurfaceRaised) > 0.5 ? new SKColor(0, 32, 176) : new SKColor(255, 255, 0);

        /// <summary>
        /// Readable ink for text drawn on top of <paramref name="surface"/> — near-black on a
        /// light one, white on a dark one.
        ///
        /// <para>
        /// The near-black end is <c>#0c0f14</c> rather than pure black because that is the value
        /// primary buttons already used, so the default (light) accent renders exactly as it
        /// did. What changes is the dark-accent case, which used to be near-black on near-black.
        /// </para>
        /// </summary>
        public static SKColor InkOn(SKColor surface) =>
            Luminance(surface) > 0.5 ? new SKColor(12, 15, 20) : new SKColor(255, 255, 255);

        /// <summary>Relative luminance, 0 (black) to 1 (white). sRGB coefficients, no gamma —
        /// enough to answer "is this surface light or dark", which is all it is used for.</summary>
        public static double Luminance(SKColor c) =>
            (0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue) / 255.0;

        /// <summary>
        /// A CSS colour. Opaque colours use <c>#rrggbb</c>; anything translucent uses
        /// <c>rgba()</c>, because <c>#rrggbbaa</c> is not understood by every WebView the app
        /// runs inside.
        /// </summary>
        public static string Css(SKColor c) =>
            c.Alpha == 255
                ? $"#{c.Red:x2}{c.Green:x2}{c.Blue:x2}"
                : string.Format(CultureInfo.InvariantCulture, "rgba({0},{1},{2},{3:0.###})",
                    c.Red, c.Green, c.Blue, c.Alpha / 255.0);
    }
}
