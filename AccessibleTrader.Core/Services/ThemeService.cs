using System;
using System.Collections.Immutable;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Theming;

namespace AccessibleTrader.Core.Services
{
    public class ThemeService : IThemeService
    {
        private readonly ISettingsManager _settings;

        public ChartTheme Current { get; private set; }
        public event EventHandler<ChartTheme>? ThemeChanged;

        // ── Legacy properties (used by existing ChartRenderer/BackgroundLayer code) ──────────────
        // These delegate to Current so there is a single source of truth.
        public SKColor Background => Current.Background;
        public SKColor? BackgroundGradientEnd => Current.BackgroundGradientEnd;
        public SKColor GridLines   => Current.GridLine;
        public SKColor Text        => Current.AxisText;
        public SKColor Cursor      => Current.Crosshair;
        public SKColor CandleBullish => Current.CandleBullishBody;
        public SKColor CandleBearish => Current.CandleBearishBody;
        public SKColor CandleWick    => Current.CandleBullishWick;
        public SKColor LineSeries    => Current.IndicatorPalette.Count > 0 ? Current.IndicatorPalette[0] : SKColors.White;
        public SKColor EmaSeries     => Current.IndicatorPalette.Count > 1 ? Current.IndicatorPalette[1] : SKColors.HotPink;
        public SKColor SmaSeries     => Current.IndicatorPalette.Count > 2 ? Current.IndicatorPalette[2] : SKColors.Orange;
        public SKColor RsiSeries     => Current.IndicatorPalette.Count > 3 ? Current.IndicatorPalette[3] : SKColors.Cyan;
        public SKColor Volume        => Current.VolumeBullish;
        // ────────────────────────────────────────────────────────────────────────────────────────

        private const string ThemeSettingKey = "ui.theme";

        // Visual accessibility overrides (Phase D). Both default OFF: the terminal
        // presents audio-first; visual accommodations are opt-in per user.
        public const string ColorVisionSafeKey = SettingsKeys.ColorVisionSafe;
        public const string HollowUpCandlesKey = SettingsKeys.HollowUpCandles;

        // Optional user override of the theme's chart background ("#RRGGBB").
        // Empty/absent means "use the theme's own background". Applies across
        // theme switches until cleared from Settings > Appearance.
        public const string BackgroundOverrideKey = SettingsKeys.BackgroundColor;

        public ThemeService(ISettingsManager settings)
        {
            _settings = settings;
            // Restore previously-saved theme; fall back to HighContrastDark if not set.
            var saved = _settings.GetSetting(ThemeSettingKey)?.ToString();
            var type = Enum.TryParse<ThemeType>(saved, out var parsed) ? parsed : ThemeType.SteelGray;
            Current = WithAccessibilityOverrides(BuildTheme(type));
        }

        public void SetTheme(ThemeType theme)
        {
            Current = WithAccessibilityOverrides(BuildTheme(theme));
            ThemeChanged?.Invoke(this, Current);
            // Persist immediately so the choice survives restart.
            _settings.SetSetting(ThemeSettingKey, theme.ToString());
            _settings.SaveSettings();
        }

        // Keep old API for code that calls ApplyTheme(ThemeType).
        public void ApplyTheme(ThemeType theme) => SetTheme(theme);

        /// <summary>
        /// Re-reads the visual-accessibility settings and re-fires ThemeChanged so
        /// the chart repaints. Called by the Settings dialog after toggling
        /// color-vision-safe colors or hollow candles.
        /// </summary>
        public void RefreshAccessibilityOverrides()
        {
            Current = WithAccessibilityOverrides(BuildTheme(Current.ThemeType));
            ThemeChanged?.Invoke(this, Current);
        }

        private ChartTheme WithAccessibilityOverrides(ChartTheme theme)
        {
            bool colorVision = _settings.GetSetting(ColorVisionSafeKey)?.Value<bool?>() ?? false;
            bool hollow      = _settings.GetSetting(HollowUpCandlesKey)?.Value<bool?>() ?? false;
            if (colorVision || hollow)
                theme = theme with { ColorVisionSafe = colorVision, HollowUpCandles = hollow };

            var bgOverride = _settings.GetSetting(BackgroundOverrideKey)?.ToString();
            if (!string.IsNullOrWhiteSpace(bgOverride) && SKColor.TryParse(bgOverride, out var bg))
                theme = theme with { Background = bg };

            // Optional gradient: Background (top) → BackgroundColor2 (bottom). Opt-in.
            bool gradient = _settings.GetSetting(SettingsKeys.BackgroundGradient)?.Value<bool?>() ?? false;
            var bg2Override = _settings.GetSetting(SettingsKeys.BackgroundColor2)?.ToString();
            if (gradient && !string.IsNullOrWhiteSpace(bg2Override) && SKColor.TryParse(bg2Override, out var bg2))
                theme = theme with { BackgroundGradientEnd = bg2 };

            return theme;
        }

        private static ChartTheme BuildTheme(ThemeType type) => type switch
        {
            ThemeType.SteelGray         => SteelGray(),
            ThemeType.HighContrastDark  => HighContrastDark(),
            ThemeType.HighContrastLight => HighContrastLight(),
            ThemeType.SoftDark          => SoftDark(),
            ThemeType.Solarized         => Solarized(),
            ThemeType.Braille           => BrailleOptimized(),
            _                           => SteelGray()
        };

        /// <summary>
        /// The default. Cool neutral greys with a chart that fades UPWARD into the toolbar, so the
        /// window reads as one surface rather than a canvas dropped into a frame.
        ///
        /// <para>
        /// The gradient runs light at the top to dark at the bottom, and the top end is matched to
        /// <see cref="ChartTheme.SurfaceRaised"/> so the seam between chart and toolbar disappears.
        /// It is deliberately shallow — a wide swing would wash out candles at the top of the pane,
        /// and price at the top of the range is exactly where the eye is.
        /// </para>
        ///
        /// <para>
        /// Candles are #77FF77 / #DD0000 rather than the usual muted teal-and-salmon. The bright
        /// green carries against grey where a mid-tone green does not, and the deep red stays
        /// distinguishable for the common red-green deficiencies by being much darker than the
        /// green rather than merely a different hue. Volume uses the same pair at partial alpha so
        /// the two panes agree about what "up" looks like.
        /// </para>
        /// </summary>
        private static ChartTheme SteelGray() => new()
        {
            ThemeType            = ThemeType.SteelGray,
            // The window is ONE vertical fade, light at the top and dark at the bottom, and the
            // chart is the middle slice of it. These two are that slice — not a fade of their own.
            Background           = new SKColor(0x5C, 0x61, 0x69),   // where the chart meets the tab bar
            BackgroundGradientEnd = new SKColor(0x3B, 0x3E, 0x44),  // where it meets the indicator bar
            // Lifted well clear of the background: at the old value the grid was within a few
            // units of the chart behind it and effectively invisible on the lighter upper half.
            GridLine             = new SKColor(0x7A, 0x80, 0x8A),
            GridLineMinor        = new SKColor(0x68, 0x6D, 0x76),
            AxisText             = new SKColor(0xE8, 0xEC, 0xF2),
            AxisLine             = new SKColor(0x8A, 0x91, 0x9C),
            Crosshair            = new SKColor(255, 214, 92),
            CandleBullishBody    = new SKColor(0x77, 0xFF, 0x77),
            CandleBearishBody    = new SKColor(0xDD, 0x00, 0x00),
            CandleBullishWick    = new SKColor(0x9C, 0xFF, 0x9C),
            CandleBearishWick    = new SKColor(0xF2, 0x3B, 0x3B),
            CandleDojiBody       = new SKColor(0xE2, 0xE7, 0xEE),
            VolumeBullish        = new SKColor(0x77, 0xFF, 0x77, 130),
            VolumeBearish        = new SKColor(0xDD, 0x00, 0x00, 130),
            IndicatorPalette     = ImmutableList.Create(
                new SKColor(240, 244, 250), new SKColor(255, 138, 190), new SKColor(255, 176, 74),
                new SKColor(112, 214, 255), new SKColor(255, 226, 112), new SKColor(206, 148, 255),
                new SKColor(126, 190, 255), new SKColor(255, 158, 110), new SKColor(150, 232, 174),
                new SKColor(255, 122, 122), new SKColor(160, 205, 236), new SKColor(232, 230, 186)),
            ProfilePOC           = new SKColor(255, 196, 84),
            ProfileValueArea     = new SKColor(255, 196, 84, 64),
            ProfileSinglePrint   = new SKColor(126, 158, 255, 110),
            ProfileNormal        = new SKColor(160, 168, 182, 92),
            ProfileSeparator     = new SKColor(122, 128, 138),
            DrawingLine          = new SKColor(255, 214, 92),
            DrawingHandle        = new SKColor(248, 250, 253),
            SelectionHighlight   = new SKColor(255, 214, 92, 56),
            AxisFontSize         = 12f,
            LegendFontSize       = 11f,
            ProfileLetterFontSize = 10f,
            AxisWidth            = 60f,
            AxisHeight           = 40f,
            ProfileWidthFraction = 0.20f,
            ProfileSeparatorWidth = 2f,
            // The two ends of the window fade. Toolbars sit at the top and take SurfaceRaised;
            // the indicator bar sits at the bottom and takes SurfaceSunken; the chart's own two
            // colours above are the values the fade has reached where the canvas starts and ends.
            //
            // SurfaceRaised is #6B7079 rather than the #888888 that was asked for, and the reason
            // is measurable: #888888 under near-white text is a 3.3:1 contrast ratio, below the
            // 4.5:1 needed for body text; #6B7079 reaches about 4.4:1. The alternative that keeps
            // #888888 is to flip the chrome to DARK ink, which is a coherent "brushed steel panel"
            // look but recolours every toolbar label and icon variant — a deliberate choice rather
            // than something to slip in.
            SurfaceRaised        = new SKColor(0x6B, 0x70, 0x79),
            SurfaceSunken        = new SKColor(0x33, 0x35, 0x3A),
            TextPrimary          = new SKColor(0xF5, 0xF7, 0xFA),
            TextMuted            = new SKColor(0xD2, 0xD8, 0xE0),
            ChromeBorder         = new SKColor(0x8A, 0x91, 0x9C),
            Accent               = new SKColor(0x8F, 0xC2, 0xFF),
            ButtonNeutral        = new SKColor(0xEC, 0xF0, 0xF6),
        };

        private static ChartTheme HighContrastDark() => new()
        {
            ThemeType            = ThemeType.HighContrastDark,
            Background           = SKColors.Black,
            GridLine             = new SKColor(40, 40, 40),
            GridLineMinor        = new SKColor(25, 25, 25),
            AxisText             = SKColors.White,
            AxisLine             = new SKColor(80, 80, 80),
            Crosshair            = SKColors.Yellow,
            CandleBullishBody    = SKColors.White,
            CandleBearishBody    = SKColors.Red,
            CandleBullishWick    = SKColors.White,
            CandleBearishWick    = SKColors.White,
            CandleDojiBody       = SKColors.Gray,
            VolumeBullish        = new SKColor(0, 180, 0, 128),
            VolumeBearish        = new SKColor(180, 0, 0, 128),
            IndicatorPalette     = ImmutableList.Create(
                SKColors.White, SKColors.HotPink, SKColors.Orange, SKColors.Cyan,
                SKColors.Yellow, SKColors.Magenta, new SKColor(100, 200, 255),
                new SKColor(255, 165, 0), new SKColor(144, 238, 144),
                new SKColor(255, 99, 71), new SKColor(173, 216, 230), new SKColor(250, 250, 210)),
            ProfilePOC           = SKColors.Orange,
            ProfileValueArea     = new SKColor(255, 165, 0, 80),
            ProfileSinglePrint   = new SKColor(100, 100, 255, 128),
            ProfileNormal        = new SKColor(100, 100, 100, 100),
            ProfileSeparator     = new SKColor(60, 60, 60),
            DrawingLine          = SKColors.Yellow,
            DrawingHandle        = SKColors.White,
            SelectionHighlight   = new SKColor(255, 255, 0, 60),
            AxisFontSize         = 12f,
            LegendFontSize       = 11f,
            ProfileLetterFontSize = 10f,
            AxisWidth            = 60f,
            AxisHeight           = 40f,
            ProfileWidthFraction = 0.20f,
            ProfileSeparatorWidth = 2f,
            SurfaceRaised        = new SKColor(20, 20, 20),
            SurfaceSunken        = SKColors.Black,
            TextPrimary          = SKColors.White,
            TextMuted            = new SKColor(190, 190, 190),
            ChromeBorder         = new SKColor(110, 110, 110),
            Accent               = new SKColor(255, 255, 0),
            ButtonNeutral        = SKColors.White,
        };

        private static ChartTheme HighContrastLight() => new()
        {
            ThemeType            = ThemeType.HighContrastLight,
            Background           = SKColors.White,
            GridLine             = new SKColor(200, 200, 200),
            GridLineMinor        = new SKColor(230, 230, 230),
            AxisText             = SKColors.Black,
            AxisLine             = new SKColor(100, 100, 100),
            Crosshair            = new SKColor(0, 0, 200),
            CandleBullishBody    = new SKColor(0, 140, 0),
            CandleBearishBody    = new SKColor(200, 0, 0),
            CandleBullishWick    = SKColors.Black,
            CandleBearishWick    = SKColors.Black,
            CandleDojiBody       = new SKColor(100, 100, 100),
            VolumeBullish        = new SKColor(0, 140, 0, 128),
            VolumeBearish        = new SKColor(200, 0, 0, 128),
            IndicatorPalette     = ImmutableList.Create(
                SKColors.Black, new SKColor(180, 0, 120), new SKColor(200, 80, 0),
                new SKColor(0, 0, 200), new SKColor(150, 100, 0), new SKColor(100, 0, 150),
                new SKColor(0, 100, 180), new SKColor(180, 60, 0), new SKColor(0, 120, 60),
                new SKColor(180, 30, 30), new SKColor(30, 80, 150), new SKColor(100, 100, 0)),
            ProfilePOC           = new SKColor(180, 80, 0),
            ProfileValueArea     = new SKColor(180, 120, 0, 60),
            ProfileSinglePrint   = new SKColor(0, 0, 200, 80),
            ProfileNormal        = new SKColor(150, 150, 150, 80),
            ProfileSeparator     = new SKColor(180, 180, 180),
            DrawingLine          = new SKColor(0, 0, 180),
            DrawingHandle        = SKColors.Black,
            SelectionHighlight   = new SKColor(0, 0, 255, 40),
            AxisFontSize         = 12f,
            LegendFontSize       = 11f,
            ProfileLetterFontSize = 10f,
            AxisWidth            = 60f,
            AxisHeight           = 40f,
            ProfileWidthFraction = 0.20f,
            ProfileSeparatorWidth = 2f,
            SurfaceRaised        = new SKColor(238, 238, 238),
            SurfaceSunken        = SKColors.White,
            TextPrimary          = SKColors.Black,
            TextMuted            = new SKColor(70, 70, 70),
            ChromeBorder         = new SKColor(120, 120, 120),
            Accent               = new SKColor(0, 0, 200),
            ButtonNeutral        = new SKColor(30, 30, 30),
        };

        private static ChartTheme SoftDark() => new()
        {
            ThemeType            = ThemeType.SoftDark,
            Background           = new SKColor(18, 20, 28),
            GridLine             = new SKColor(45, 50, 65),
            GridLineMinor        = new SKColor(30, 33, 45),
            AxisText             = new SKColor(180, 185, 200),
            AxisLine             = new SKColor(60, 65, 80),
            Crosshair            = new SKColor(120, 180, 255),
            CandleBullishBody    = new SKColor(70, 190, 100),
            CandleBearishBody    = new SKColor(200, 70, 80),
            CandleBullishWick    = new SKColor(100, 210, 130),
            CandleBearishWick    = new SKColor(220, 100, 110),
            CandleDojiBody       = new SKColor(140, 145, 160),
            VolumeBullish        = new SKColor(70, 190, 100, 100),
            VolumeBearish        = new SKColor(200, 70, 80, 100),
            IndicatorPalette     = ImmutableList.Create(
                new SKColor(120, 180, 255), new SKColor(255, 140, 180), new SKColor(255, 180, 80),
                new SKColor(100, 220, 200), new SKColor(200, 160, 255), new SKColor(150, 230, 120),
                new SKColor(255, 200, 100), new SKColor(100, 200, 250), new SKColor(250, 150, 100),
                new SKColor(180, 255, 180), new SKColor(255, 180, 255), new SKColor(180, 230, 255)),
            ProfilePOC           = new SKColor(255, 180, 50),
            ProfileValueArea     = new SKColor(255, 180, 50, 50),
            ProfileSinglePrint   = new SKColor(80, 160, 255, 100),
            ProfileNormal        = new SKColor(80, 90, 110, 100),
            ProfileSeparator     = new SKColor(45, 50, 65),
            DrawingLine          = new SKColor(120, 180, 255),
            DrawingHandle        = new SKColor(200, 210, 230),
            SelectionHighlight   = new SKColor(100, 160, 255, 40),
            AxisFontSize         = 12f,
            LegendFontSize       = 11f,
            ProfileLetterFontSize = 10f,
            AxisWidth            = 60f,
            AxisHeight           = 40f,
            ProfileWidthFraction = 0.20f,
            ProfileSeparatorWidth = 2f,
            SurfaceRaised        = new SKColor(30, 33, 44),
            SurfaceSunken        = new SKColor(22, 24, 33),
            TextPrimary          = new SKColor(226, 230, 240),
            TextMuted            = new SKColor(150, 156, 172),
            ChromeBorder         = new SKColor(58, 63, 80),
            Accent               = new SKColor(96, 165, 250),
            ButtonNeutral        = new SKColor(186, 194, 212),
        };

        private static ChartTheme Solarized() => new()
        {
            ThemeType            = ThemeType.Solarized,
            Background           = new SKColor(0, 43, 54),     // base03
            GridLine             = new SKColor(7, 54, 66),      // base02
            GridLineMinor        = new SKColor(0, 43, 54),
            AxisText             = new SKColor(131, 148, 150),  // base0
            AxisLine             = new SKColor(88, 110, 117),   // base01
            Crosshair            = new SKColor(38, 139, 210),   // blue
            CandleBullishBody    = new SKColor(133, 153, 0),    // green
            CandleBearishBody    = new SKColor(220, 50, 47),    // red
            CandleBullishWick    = new SKColor(147, 161, 161),  // base1
            CandleBearishWick    = new SKColor(147, 161, 161),
            CandleDojiBody       = new SKColor(101, 123, 131),  // base00
            VolumeBullish        = new SKColor(133, 153, 0, 120),
            VolumeBearish        = new SKColor(220, 50, 47, 120),
            IndicatorPalette     = ImmutableList.Create(
                new SKColor(38, 139, 210), new SKColor(211, 54, 130), new SKColor(181, 137, 0),
                new SKColor(42, 161, 152), new SKColor(108, 113, 196), new SKColor(133, 153, 0),
                new SKColor(203, 75, 22), new SKColor(147, 161, 161), new SKColor(101, 123, 131),
                new SKColor(88, 110, 117), new SKColor(131, 148, 150), new SKColor(253, 246, 227)),
            ProfilePOC           = new SKColor(181, 137, 0),
            ProfileValueArea     = new SKColor(181, 137, 0, 50),
            ProfileSinglePrint   = new SKColor(38, 139, 210, 80),
            ProfileNormal        = new SKColor(88, 110, 117, 80),
            ProfileSeparator     = new SKColor(7, 54, 66),
            DrawingLine          = new SKColor(38, 139, 210),
            DrawingHandle        = new SKColor(147, 161, 161),
            SelectionHighlight   = new SKColor(38, 139, 210, 40),
            AxisFontSize         = 12f,
            LegendFontSize       = 11f,
            ProfileLetterFontSize = 10f,
            AxisWidth            = 60f,
            AxisHeight           = 40f,
            ProfileWidthFraction = 0.20f,
            ProfileSeparatorWidth = 2f,
        };

        private static ChartTheme BrailleOptimized() => new()
        {
            ThemeType            = ThemeType.Braille,
            Background           = SKColors.Black,
            GridLine             = new SKColor(60, 60, 60),
            GridLineMinor        = new SKColor(40, 40, 40),
            AxisText             = SKColors.White,
            AxisLine             = new SKColor(80, 80, 80),
            Crosshair            = SKColors.White,
            // High-contrast, audio-friendly: shape matters more than colour for Braille users.
            // Colours chosen to be distinguishable by the low-vision sighted assistant
            // and to have high luminance contrast against black.
            CandleBullishBody    = SKColors.White,
            CandleBearishBody    = new SKColor(255, 64, 64),
            CandleBullishWick    = new SKColor(200, 255, 200),
            CandleBearishWick    = new SKColor(255, 200, 200),
            CandleDojiBody       = new SKColor(200, 200, 200),
            VolumeBullish        = new SKColor(0, 255, 128, 160),
            VolumeBearish        = new SKColor(255, 64, 64, 160),
            IndicatorPalette     = ImmutableList.Create(
                SKColors.White, new SKColor(255, 255, 0), new SKColor(0, 200, 255),
                new SKColor(255, 128, 0), new SKColor(200, 0, 255), new SKColor(0, 255, 128),
                new SKColor(255, 0, 128), new SKColor(128, 255, 0), new SKColor(0, 128, 255),
                new SKColor(255, 200, 0), new SKColor(0, 255, 200), new SKColor(200, 255, 0)),
            ProfilePOC           = new SKColor(255, 255, 0),
            ProfileValueArea     = new SKColor(255, 255, 0, 60),
            ProfileSinglePrint   = new SKColor(0, 200, 255, 100),
            ProfileNormal        = new SKColor(120, 120, 120, 100),
            ProfileSeparator     = new SKColor(80, 80, 80),
            DrawingLine          = new SKColor(255, 255, 0),
            DrawingHandle        = SKColors.White,
            SelectionHighlight   = new SKColor(255, 255, 0, 50),
            AxisFontSize         = 14f,   // larger for low vision
            LegendFontSize       = 13f,
            ProfileLetterFontSize = 12f,
            AxisWidth            = 70f,
            AxisHeight           = 35f,
            ProfileWidthFraction = 0.20f,
            ProfileSeparatorWidth = 2f,
        };
    }
}
