using AccessibleTrader.Sdk.Theming;
using SkiaSharp;

namespace AccessibleTrader.Core.Services.Theming
{
    /// <summary>
    /// The colour pairs a theme must keep readable, measured with <see cref="WcagContrast"/>.
    ///
    /// <para>
    /// One list, two readers: the theme editor shows these to a person building a theme and
    /// refuses to save while a text pair fails, and <c>ThemeCoverageTests</c> runs the same list
    /// over every built-in. That is what makes the editor's "a preset is always safe" claim
    /// true rather than hopeful — the test and the editor cannot disagree about what "safe"
    /// means because they do not each have a copy of it.
    /// </para>
    ///
    /// <para>
    /// Text pairs are held to WCAG 1.4.3's 4.5:1, against both ends of any band that fades.
    /// Candles and the crosshair are held to WCAG 1.4.11's 3:1 for graphical objects, against
    /// BOTH ends of the chart gradient because a candle can sit anywhere on it. The gridline check runs the other way — a grid that reaches the graphics
    /// floor competes with the price data — and is advice, not a rule.
    /// </para>
    /// </summary>
    public static class ThemeContrastChecks
    {
        public enum Severity
        {
            /// <summary>Body text below 4.5:1. The editor will not save this.</summary>
            Blocking,
            /// <summary>A graphic below 3:1, or a grid above it. Reported, not enforced.</summary>
            Advisory,
        }

        /// <summary>One measured pair.</summary>
        /// <param name="Key">Stable identifier, for tests and exemptions.</param>
        /// <param name="What">The sentence shown to a person, with the ratio in it.</param>
        /// <param name="Threshold">The floor the ratio must reach — or, when <paramref name="AtMost"/>, the ceiling it must stay under.</param>
        /// <param name="AtMost">True for the one inverted check (a gridline that must NOT reach the graphics floor).</param>
        /// <param name="Passes">Whether the pair is on the right side of <paramref name="Threshold"/>.</param>
        /// <param name="FieldKey">The <see cref="ThemeFields"/> key of the colour most likely to move, so the editor can point at it.</param>
        public sealed record Finding(string Key, Severity Severity, string What, double Ratio, double Threshold,
                                     bool AtMost, bool Passes, string FieldKey);

        public const string GridKey = "grid";

        /// <summary>Every pair, measured. Filter on <see cref="Finding.Passes"/> for the failures.</summary>
        public static IReadOnlyList<Finding> Measure(ChartTheme theme)
        {
            var chartTop = theme.Background;
            var chartBottom = theme.BackgroundGradientEnd ?? theme.Background;
            var toolbarBottom = theme.ChromeTopEnd ?? theme.SurfaceRaised;
            var footerBottom = theme.ChromeBottomEnd ?? theme.ChromeBottom;
            // app.css scopes --text-muted inside .modal-content to the dialog ink at 68% alpha, so
            // a dialog's hint text is NOT theme.TextMuted — it is TextOnDialog seen through glass.
            var dialogHint = theme.TextOnDialog.WithAlpha(173);
            var list = new List<Finding>();

            void Text(string key, string field, string subject, string surface, SKColor fg, SKColor bg) =>
                list.Add(Make(key, field, Severity.Blocking, subject, surface, fg, bg, WcagContrast.TextMinimum));

            void Graphic(string key, string field, string subject, string surface, SKColor fg, SKColor bg) =>
                list.Add(Make(key, field, Severity.Advisory, subject, surface, fg, bg, WcagContrast.GraphicsMinimum));

            Text("toolbarText",       "textPrimary", "Toolbar text",     "the toolbar",              theme.TextPrimary,  theme.SurfaceRaised);
            Text("toolbarTextBottom", "textPrimary", "Toolbar text",     "the bottom of the toolbar", theme.TextPrimary, toolbarBottom);
            Text("dialogText",        "dialogText",  "Dialog text",      "the dialog background",    theme.TextOnDialog, theme.SurfaceSunken);
            Text("secondaryText",     "dialogText",  "Dialog hint text", "the dialog background",    dialogHint,         theme.SurfaceSunken);
            Text("tabLabels",         "textMuted",   "Tab label text",   "the dialog background",    theme.TextMuted,    theme.SurfaceSunken);
            Text("footerText",        "textMuted",   "Footer text",      "the footer",               theme.TextMuted,    footerBottom);
            Text("axisTextTop",       "axisText",    "Axis text",        "the top of the chart",     theme.AxisText,     chartTop);
            Text("axisTextBottom",    "axisText",    "Axis text",        "the bottom of the chart",  theme.AxisText,     chartBottom);
            Text("accentInk",         "accent",      "Primary button text", "the accent",            ThemeCssBridge.InkOn(theme.Accent), theme.Accent);

            Graphic("bullTop",        "bullBody",    "The rising candle",  "the top of the chart",    theme.CandleBullishBody, chartTop);
            Graphic("bullBottom",     "bullBody",    "The rising candle",  "the bottom of the chart", theme.CandleBullishBody, chartBottom);
            Graphic("bearTop",        "bearBody",    "The falling candle", "the top of the chart",    theme.CandleBearishBody, chartTop);
            Graphic("bearBottom",     "bearBody",    "The falling candle", "the bottom of the chart", theme.CandleBearishBody, chartBottom);
            Graphic("crosshairTop",   "crosshair",   "The crosshair",      "the top of the chart",    theme.Crosshair, chartTop);
            Graphic("crosshairBottom","crosshair",   "The crosshair",      "the bottom of the chart", theme.Crosshair, chartBottom);
            Graphic("focusRing",      "topBar",      "The focus ring",     "the toolbar",             ThemeCssBridge.FocusRingFor(theme), theme.SurfaceRaised);

            // The opposite failure: a grid so loud it competes with the price data.
            double grid = WcagContrast.Ratio(theme.GridLine, chartTop);
            list.Add(new Finding(GridKey, Severity.Advisory,
                $"Gridlines stand out as much as the price data ({WcagContrast.Format(grid)} against the chart; " +
                $"under {WcagContrast.Format(WcagContrast.GraphicsMinimum)} keeps them in the background).",
                grid, WcagContrast.GraphicsMinimum, AtMost: true, Passes: grid < WcagContrast.GraphicsMinimum,
                FieldKey: "gridMajor"));

            return list;
        }

        /// <summary>The failures only, blocking first.</summary>
        public static IReadOnlyList<Finding> Failures(ChartTheme theme) =>
            Measure(theme).Where(f => !f.Passes).OrderBy(f => f.Severity).ToList();

        private static Finding Make(string key, string field, Severity severity, string subject, string surface,
                                    SKColor fg, SKColor bg, double minimum)
        {
            double ratio = WcagContrast.Ratio(fg, bg);
            string verb = ratio >= minimum ? "is" : "is only";
            string what = $"{subject} {verb} {WcagContrast.Format(ratio)} against {surface}; " +
                          $"{WcagContrast.Format(minimum)} is needed.";
            // The button's ink is picked by the app, not the person; the only colour they can
            // move is the accent itself.
            if (key == "accentInk" && ratio < minimum) what += " Change the Accent colour.";
            return new Finding(key, severity, what, ratio, minimum, AtMost: false, Passes: ratio >= minimum, FieldKey: field);
        }
    }
}
