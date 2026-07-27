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

        /// <summary>
        /// Optional so the service still constructs in hosts and tests that have no theme storage.
        /// A missing library simply means no custom themes exist, which is also the state of a
        /// fresh install.
        /// </summary>
        private readonly Theming.IThemeLibrary? _themes;

        public ThemeService(ISettingsManager settings, Theming.IThemeLibrary? themes = null)
        {
            _settings = settings;
            _themes = themes;
            // Restore previously-saved theme; fall back to HighContrastDark if not set.
            var saved = _settings.GetSetting(ThemeSettingKey)?.ToString();
            var type = Enum.TryParse<ThemeType>(saved, out var parsed) ? parsed : ThemeType.SteelGray;
            Current = WithAccessibilityOverrides(BuildTheme(type));
        }

        /// <summary>
        /// Switches to one of the user's own themes. Its base built-in is applied first, then its
        /// overrides, then every appearance preference as usual.
        /// </summary>
        public void SetCustomTheme(Sdk.Theming.ThemePreset preset)
        {
            ArgumentNullException.ThrowIfNull(preset);
            _settings.SetSetting(SettingsKeys.CustomThemeId, preset.Id);
            _settings.SetSetting(ThemeSettingKey, preset.BasedOn.ToString());
            _settings.SaveSettings();
            Current = WithAccessibilityOverrides(BuildTheme(preset.BasedOn));
            ThemeChanged?.Invoke(this, Current);
        }

        public void SetTheme(ThemeType theme)
        {
            // Choosing a built-in leaves any custom theme behind — otherwise its overrides would
            // silently follow you onto every theme you tried afterwards, and the picker would
            // appear not to work.
            _settings.SetSetting(SettingsKeys.CustomThemeId, string.Empty);
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
            // A user's own theme is applied FIRST, so everything below — the unified gradient,
            // the background override, the up/down pair — still layers on top of it exactly as it
            // would over a built-in. A custom theme is a starting point, not a final word.
            var customId = _settings.GetSetting(SettingsKeys.CustomThemeId)?.ToString();
            if (!string.IsNullOrWhiteSpace(customId) && _themes?.GetById(customId!) is { } preset)
                theme = preset.ApplyTo(theme);

            bool colorVision = _settings.GetSetting(ColorVisionSafeKey)?.Value<bool?>() ?? false;
            bool hollow      = _settings.GetSetting(HollowUpCandlesKey)?.Value<bool?>() ?? false;
            if (colorVision || hollow)
                theme = theme with { ColorVisionSafe = colorVision, HollowUpCandles = hollow };

            // One fade across the whole window, if asked for. Applied BEFORE the individual
            // background overrides below so a user who has also set a specific chart background
            // still wins on that one band — the more specific preference beats the broader one.
            bool unified = _settings.GetSetting(SettingsKeys.UnifiedGradient)?.Value<bool?>() ?? false;
            if (unified)
            {
                var topRaw = _settings.GetSetting(SettingsKeys.UnifiedGradientTop)?.ToString();
                var botRaw = _settings.GetSetting(SettingsKeys.UnifiedGradientBottom)?.ToString();

                // Absent ends default to the theme's own extremes, so ticking the box alone
                // smooths whatever the theme already had rather than demanding two colours first.
                var top = !string.IsNullOrWhiteSpace(topRaw) && SKColor.TryParse(topRaw, out var t1)
                    ? t1 : theme.SurfaceRaised;
                var bottom = !string.IsNullOrWhiteSpace(botRaw) && SKColor.TryParse(botRaw, out var b1)
                    ? b1 : (theme.ChromeBottomEnd ?? theme.ChromeBottom);

                theme = Theming.UnifiedGradient.Apply(theme, top, bottom);
            }

            var bgOverride = _settings.GetSetting(BackgroundOverrideKey)?.ToString();
            if (!string.IsNullOrWhiteSpace(bgOverride) && SKColor.TryParse(bgOverride, out var bg))
                theme = theme with { Background = bg };

            // Optional gradient: Background (top) → BackgroundColor2 (bottom). Opt-in.
            bool gradient = _settings.GetSetting(SettingsKeys.BackgroundGradient)?.Value<bool?>() ?? false;
            var bg2Override = _settings.GetSetting(SettingsKeys.BackgroundColor2)?.ToString();
            if (gradient && !string.IsNullOrWhiteSpace(bg2Override) && SKColor.TryParse(bg2Override, out var bg2))
                theme = theme with { BackgroundGradientEnd = bg2 };

            // Up/down colours are an app-level preference layered over the theme, for the same
            // reason the background override is: which colour means "up" is a habit a trader
            // carries between themes, and it should not change under them when they try a new
            // look. Absent leaves the theme's own pair — which is how High Contrast Dark keeps
            // its deliberate white-on-red scheme.
            var bull = _settings.GetSetting(SettingsKeys.BullishColor)?.ToString();
            if (!string.IsNullOrWhiteSpace(bull) && SKColor.TryParse(bull, out var bullColor))
                theme = theme with
                {
                    CandleBullishBody = bullColor,
                    CandleBullishWick = bullColor,
                    VolumeBullish = bullColor.WithAlpha(theme.VolumeBullish.Alpha),
                };

            var bear = _settings.GetSetting(SettingsKeys.BearishColor)?.ToString();
            if (!string.IsNullOrWhiteSpace(bear) && SKColor.TryParse(bear, out var bearColor))
                theme = theme with
                {
                    CandleBearishBody = bearColor,
                    CandleBearishWick = bearColor,
                    VolumeBearish = bearColor.WithAlpha(theme.VolumeBearish.Alpha),
                };

            return theme;
        }

        private static ChartTheme BuildTheme(ThemeType type) => type switch
        {
            ThemeType.SteelGray         => SteelGray(),
            ThemeType.Blackout          => Blackout(),
            ThemeType.Classic           => Classic(),
            ThemeType.AmberCrt          => AmberCrt(),
            ThemeType.Walnut            => Walnut(),
            ThemeType.Paper             => Paper(),
            ThemeType.MidnightBlue      => MidnightBlue(),
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
            Background           = new SKColor(0x4E, 0x54, 0x5E),   // where the chart meets the tab bar
            BackgroundGradientEnd = new SKColor(0x22, 0x25, 0x2A),  // where it meets the indicator bar
            // Lifted well clear of the background: at the old value the grid was within a few
            // units of the chart behind it and effectively invisible on the lighter upper half.
            GridLine             = new SKColor(0x6E, 0x75, 0x80),
            GridLineMinor        = new SKColor(0x5B, 0x61, 0x6B),
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
            // Toolbar band: its own fade, ending exactly where the chart begins so the seam
            // vanishes. Steel chooses continuity; a theme is free not to.
            SurfaceRaised        = new SKColor(0x67, 0x6E, 0x79),
            ChromeTopEnd         = new SKColor(0x4E, 0x54, 0x5E),
            // Footer band: picks up where the chart's fade left off and carries on down.
            ChromeBottom         = new SKColor(0x22, 0x25, 0x2A),
            ChromeBottomEnd      = new SKColor(0x17, 0x19, 0x1D),
            SurfaceSunken        = new SKColor(0x2B, 0x2F, 0x36),
            TextOnDialog         = new SKColor(0xEC, 0xF0, 0xF6),
            TextPrimary          = new SKColor(0xF7, 0xF9, 0xFC),
            TextMuted            = new SKColor(0xD4, 0xDA, 0xE3),
            ChromeBorder         = new SKColor(0x86, 0x8E, 0x9A),
            Accent               = new SKColor(0x8F, 0xC2, 0xFF),
            ButtonNeutral        = new SKColor(0xEE, 0xF2, 0xF7),
        };

        /// <summary>
        /// Black everywhere, white text, dark-grey dialogs.
        ///
        /// <para>
        /// Distinct from High Contrast Dark, which is an accessibility instrument: that one uses
        /// WHITE candle bodies and a maximally loud palette because legibility outranks looks.
        /// Blackout keeps normal green/red price action and a restrained palette — it is a
        /// preference (OLED panels, low light, anyone who finds a lit background tiring), not an
        /// accommodation. Both exist because conflating them serves neither.
        /// </para>
        /// </summary>
        private static ChartTheme Blackout() => new()
        {
            ThemeType            = ThemeType.Blackout,
            Background           = SKColors.Black,
            BackgroundGradientEnd = null,      // flat: a gradient is a lit background by degrees
            GridLine             = new SKColor(0x2E, 0x2E, 0x2E),
            GridLineMinor        = new SKColor(0x1D, 0x1D, 0x1D),
            AxisText             = new SKColor(0xE8, 0xE8, 0xE8),
            AxisLine             = new SKColor(0x55, 0x55, 0x55),
            Crosshair            = new SKColor(0xFF, 0xD5, 0x4F),
            CandleBullishBody    = new SKColor(0x77, 0xFF, 0x77),
            CandleBearishBody    = new SKColor(0xDD, 0x00, 0x00),
            CandleBullishWick    = new SKColor(0x9C, 0xFF, 0x9C),
            CandleBearishWick    = new SKColor(0xF2, 0x3B, 0x3B),
            CandleDojiBody       = new SKColor(0xCC, 0xCC, 0xCC),
            VolumeBullish        = new SKColor(0x77, 0xFF, 0x77, 120),
            VolumeBearish        = new SKColor(0xDD, 0x00, 0x00, 120),
            IndicatorPalette     = ImmutableList.Create(
                SKColors.White, new SKColor(255, 138, 190), new SKColor(255, 176, 74),
                new SKColor(112, 214, 255), new SKColor(255, 226, 112), new SKColor(206, 148, 255),
                new SKColor(126, 190, 255), new SKColor(255, 158, 110), new SKColor(150, 232, 174),
                new SKColor(255, 122, 122), new SKColor(160, 205, 236), new SKColor(232, 230, 186)),
            ProfilePOC           = new SKColor(0xFF, 0xB3, 0x00),
            ProfileValueArea     = new SKColor(0xFF, 0xB3, 0x00, 56),
            ProfileSinglePrint   = new SKColor(0x7E, 0x9E, 0xFF, 110),
            ProfileNormal        = new SKColor(0x88, 0x88, 0x88, 90),
            ProfileSeparator     = new SKColor(0x44, 0x44, 0x44),
            DrawingLine          = new SKColor(0xFF, 0xD5, 0x4F),
            DrawingHandle        = SKColors.White,
            SelectionHighlight   = new SKColor(0xFF, 0xD5, 0x4F, 56),
            AxisFontSize         = 12f,
            LegendFontSize       = 11f,
            ProfileLetterFontSize = 10f,
            AxisWidth            = 60f,
            AxisHeight           = 40f,
            ProfileWidthFraction = 0.20f,
            ProfileSeparatorWidth = 2f,
            SurfaceRaised        = new SKColor(0x0A, 0x0A, 0x0A),
            ChromeTopEnd         = SKColors.Black,
            ChromeBottom         = SKColors.Black,
            ChromeBottomEnd      = new SKColor(0x0A, 0x0A, 0x0A),
            // Dialogs lift OFF the black rather than melting into it — a modal has to read as a
            // separate surface, and on a black background only lightness can say so.
            SurfaceSunken        = new SKColor(0x24, 0x24, 0x24),
            TextOnDialog         = new SKColor(0xF2, 0xF2, 0xF2),
            TextPrimary          = SKColors.White,
            TextMuted            = new SKColor(0xB4, 0xB4, 0xB4),
            ChromeBorder         = new SKColor(0x4A, 0x4A, 0x4A),
            Accent               = new SKColor(0x6C, 0xB4, 0xFF),
            ButtonNeutral        = new SKColor(0xE6, 0xE6, 0xE6),
        };

        /// <summary>
        /// The dark navy-and-teal scheme most charting sites use, so someone arriving from another
        /// platform can start from something their eye already knows and change one thing at a time.
        ///
        /// <para>
        /// It is the only theme that keeps the teal/salmon candle pair, and that is the point of
        /// it. Everywhere else the default is #77FF77 / #DD0000, which separates by BRIGHTNESS as
        /// well as hue and so survives red-green deficiency; teal and salmon are close in
        /// luminance and do not. Familiarity is a real benefit and worth offering — it is just not
        /// worth making the default.
        /// </para>
        /// </summary>
        private static ChartTheme Classic() => new()
        {
            ThemeType            = ThemeType.Classic,
            Background           = new SKColor(0x13, 0x17, 0x22),
            BackgroundGradientEnd = null,
            GridLine             = new SKColor(0x2A, 0x2E, 0x39),
            GridLineMinor        = new SKColor(0x1E, 0x22, 0x2C),
            AxisText             = new SKColor(0xD1, 0xD4, 0xDC),
            AxisLine             = new SKColor(0x43, 0x48, 0x55),
            Crosshair            = new SKColor(0x9D, 0xA5, 0xB4),
            CandleBullishBody    = new SKColor(0x26, 0xA6, 0x9A),
            CandleBearishBody    = new SKColor(0xEF, 0x53, 0x50),
            CandleBullishWick    = new SKColor(0x26, 0xA6, 0x9A),
            CandleBearishWick    = new SKColor(0xEF, 0x53, 0x50),
            CandleDojiBody       = new SKColor(0xB2, 0xB5, 0xBE),
            VolumeBullish        = new SKColor(0x26, 0xA6, 0x9A, 120),
            VolumeBearish        = new SKColor(0xEF, 0x53, 0x50, 120),
            IndicatorPalette     = ImmutableList.Create(
                new SKColor(0xD1, 0xD4, 0xDC), new SKColor(0x29, 0x62, 0xFF), new SKColor(0xFF, 0x98, 0x00),
                new SKColor(0x00, 0xBC, 0xD4), new SKColor(0xFF, 0xEB, 0x3B), new SKColor(0x9C, 0x27, 0xB0),
                new SKColor(0x21, 0x96, 0xF3), new SKColor(0xFF, 0x57, 0x22), new SKColor(0x4C, 0xAF, 0x50),
                new SKColor(0xE9, 0x1E, 0x63), new SKColor(0x03, 0xA9, 0xF4), new SKColor(0xCD, 0xDC, 0x39)),
            ProfilePOC           = new SKColor(0xFF, 0x98, 0x00),
            ProfileValueArea     = new SKColor(0xFF, 0x98, 0x00, 56),
            ProfileSinglePrint   = new SKColor(0x29, 0x62, 0xFF, 110),
            ProfileNormal        = new SKColor(0x78, 0x7B, 0x86, 90),
            ProfileSeparator     = new SKColor(0x36, 0x3A, 0x45),
            DrawingLine          = new SKColor(0x29, 0x62, 0xFF),
            DrawingHandle        = new SKColor(0xD1, 0xD4, 0xDC),
            SelectionHighlight   = new SKColor(0x29, 0x62, 0xFF, 48),
            AxisFontSize         = 12f,
            LegendFontSize       = 11f,
            ProfileLetterFontSize = 10f,
            AxisWidth            = 60f,
            AxisHeight           = 40f,
            ProfileWidthFraction = 0.20f,
            ProfileSeparatorWidth = 2f,
            SurfaceRaised        = new SKColor(0x1E, 0x22, 0x2D),
            ChromeTopEnd         = new SKColor(0x18, 0x1C, 0x26),
            ChromeBottom         = new SKColor(0x13, 0x17, 0x22),
            ChromeBottomEnd      = new SKColor(0x10, 0x13, 0x1C),
            SurfaceSunken        = new SKColor(0x1E, 0x22, 0x2D),
            TextOnDialog         = new SKColor(0xD1, 0xD4, 0xDC),
            TextPrimary          = new SKColor(0xD1, 0xD4, 0xDC),
            TextMuted            = new SKColor(0x9D, 0xA5, 0xB4),
            ChromeBorder         = new SKColor(0x43, 0x48, 0x55),
            Accent               = new SKColor(0x29, 0x62, 0xFF),
            ButtonNeutral        = new SKColor(0xD1, 0xD4, 0xDC),
        };

        /// <summary>
        /// Amber phosphor on near-black. Amber-on-dark is a genuinely restful pairing — it was
        /// chosen for 1970s terminals because it reads for hours without strain, not only because
        /// it looked futuristic.
        ///
        /// <para>
        /// Candles stay green and red rather than going monochrome amber. A period-accurate
        /// single-phosphor chart would be unreadable, and this is a theme, not a museum piece —
        /// the amber is the INSTRUMENT and price action is the signal.
        /// </para>
        /// </summary>
        private static ChartTheme AmberCrt() => new()
        {
            ThemeType            = ThemeType.AmberCrt,
            Background = new SKColor(0x0D, 0x0B, 0x06),
            BackgroundGradientEnd = new SKColor(0x07, 0x06, 0x03),
            GridLine = new SKColor(0x4A, 0x33, 0x08),
            GridLineMinor = new SKColor(0x2E, 0x20, 0x05),
            AxisText = new SKColor(0xFF, 0xB0, 0x00),
            AxisLine = new SKColor(0x8A, 0x5F, 0x0A),
            Crosshair = new SKColor(0xFF, 0xD2, 0x66),
            CandleBullishBody = new SKColor(0x77, 0xFF, 0x77),
            CandleBearishBody = new SKColor(0xDD, 0x00, 0x00),
            CandleBullishWick = new SKColor(0x9C, 0xFF, 0x9C),
            CandleBearishWick = new SKColor(0xF2, 0x3B, 0x3B),
            CandleDojiBody = new SKColor(0xFF, 0xB0, 0x00),
            VolumeBullish = new SKColor(0x77, 0xFF, 0x77, 110),
            VolumeBearish = new SKColor(0xDD, 0x00, 0x00, 110),
            IndicatorPalette = ImmutableList.Create(
                new SKColor(0xFF, 0xB0, 0x00), new SKColor(0xFF, 0xD2, 0x66), new SKColor(0xFF, 0x8C, 0x1A),
                new SKColor(0x7E, 0xE8, 0xFF), new SKColor(0xFF, 0xE9, 0x9C), new SKColor(0xD6, 0x9C, 0xFF),
                new SKColor(0x9C, 0xC8, 0xFF), new SKColor(0xFF, 0x9E, 0x6E), new SKColor(0x96, 0xE8, 0xAE),
                new SKColor(0xFF, 0x7A, 0x7A), new SKColor(0xA0, 0xCD, 0xEC), new SKColor(0xE8, 0xE6, 0xBA)),
            ProfilePOC = new SKColor(0xFF, 0xD2, 0x66),
            ProfileValueArea = new SKColor(0xFF, 0xB0, 0x00, 52),
            ProfileSinglePrint = new SKColor(0x7E, 0xE8, 0xFF, 100),
            ProfileNormal = new SKColor(0x8A, 0x6F, 0x30, 90),
            ProfileSeparator = new SKColor(0x4A, 0x33, 0x08),
            DrawingLine = new SKColor(0xFF, 0xD2, 0x66),
            DrawingHandle = new SKColor(0xFF, 0xE9, 0x9C),
            SelectionHighlight = new SKColor(0xFF, 0xB0, 0x00, 52),
            SurfaceRaised = new SKColor(0x1A, 0x14, 0x08),
            ChromeTopEnd = new SKColor(0x0D, 0x0B, 0x06),
            ChromeBottom = new SKColor(0x07, 0x06, 0x03),
            ChromeBottomEnd = new SKColor(0x12, 0x0E, 0x06),
            SurfaceSunken = new SKColor(0x1F, 0x18, 0x0A),
            TextOnDialog = new SKColor(0xFF, 0xC5, 0x3D),
            TextPrimary = new SKColor(0xFF, 0xC5, 0x3D),
            TextMuted = new SKColor(0xC4, 0x8C, 0x28),
            ChromeBorder = new SKColor(0x7A, 0x54, 0x10),
            Accent = new SKColor(0xFF, 0xB0, 0x00),
            ButtonNeutral = new SKColor(0xFF, 0xC5, 0x3D),
            AxisFontSize         = 12f,
            LegendFontSize       = 11f,
            ProfileLetterFontSize = 10f,
            AxisWidth            = 60f,
            AxisHeight           = 40f,
            ProfileWidthFraction = 0.20f,
            ProfileSeparatorWidth = 2f,
        };

        /// <summary>
        /// Warm browns and brass — the wood is the FRAME, not the chart.
        ///
        /// <para>
        /// The chrome and dialogs carry the grain (a brown gradient reads as wood without needing
        /// a texture), while the chart itself stays deep and near-neutral. Brown price action on a
        /// brown background would be unreadable, and the point of a wood theme is the cabinet the
        /// instrument sits in, not the instrument.
        /// </para>
        /// </summary>
        private static ChartTheme Walnut() => new()
        {
            ThemeType            = ThemeType.Walnut,
            Background = new SKColor(0x2A, 0x20, 0x1A),
            BackgroundGradientEnd = new SKColor(0x18, 0x11, 0x0D),
            GridLine = new SKColor(0x54, 0x42, 0x34),
            GridLineMinor = new SKColor(0x3B, 0x2E, 0x24),
            AxisText = new SKColor(0xF0, 0xE3, 0xD2),
            AxisLine = new SKColor(0x7A, 0x60, 0x4A),
            Crosshair = new SKColor(0xC9, 0xA2, 0x27),
            CandleBullishBody = new SKColor(0x77, 0xFF, 0x77),
            CandleBearishBody = new SKColor(0xDD, 0x00, 0x00),
            CandleBullishWick = new SKColor(0x9C, 0xFF, 0x9C),
            CandleBearishWick = new SKColor(0xF2, 0x3B, 0x3B),
            CandleDojiBody = new SKColor(0xEC, 0xDE, 0xCB),
            VolumeBullish = new SKColor(0x77, 0xFF, 0x77, 120),
            VolumeBearish = new SKColor(0xDD, 0x00, 0x00, 120),
            IndicatorPalette = ImmutableList.Create(
                new SKColor(0xF0, 0xE3, 0xD2), new SKColor(0xC9, 0xA2, 0x27), new SKColor(0xE0, 0x92, 0x4A),
                new SKColor(0x8F, 0xC9, 0xD6), new SKColor(0xF2, 0xD7, 0x7E), new SKColor(0xC3, 0x9B, 0xD6),
                new SKColor(0x9C, 0xBE, 0xDA), new SKColor(0xE8, 0x8B, 0x5E), new SKColor(0x9E, 0xC9, 0xA0),
                new SKColor(0xE0, 0x84, 0x84), new SKColor(0xAD, 0xC4, 0xD4), new SKColor(0xDC, 0xD2, 0xA8)),
            ProfilePOC = new SKColor(0xC9, 0xA2, 0x27),
            ProfileValueArea = new SKColor(0xC9, 0xA2, 0x27, 56),
            ProfileSinglePrint = new SKColor(0x8F, 0xC9, 0xD6, 100),
            ProfileNormal = new SKColor(0x8A, 0x74, 0x5E, 90),
            ProfileSeparator = new SKColor(0x54, 0x42, 0x34),
            DrawingLine = new SKColor(0xC9, 0xA2, 0x27),
            DrawingHandle = new SKColor(0xF5, 0xEB, 0xDD),
            SelectionHighlight = new SKColor(0xC9, 0xA2, 0x27, 52),
            SurfaceRaised = new SKColor(0x5A, 0x40, 0x32),
            ChromeTopEnd = new SKColor(0x3E, 0x2C, 0x22),
            ChromeBottom = new SKColor(0x18, 0x11, 0x0D),
            ChromeBottomEnd = new SKColor(0x2E, 0x21, 0x19),
            SurfaceSunken = new SKColor(0x3A, 0x2A, 0x20),
            TextOnDialog = new SKColor(0xF3, 0xE7, 0xD8),
            TextPrimary = new SKColor(0xF3, 0xE7, 0xD8),
            TextMuted = new SKColor(0xD2, 0xBE, 0xA4),
            ChromeBorder = new SKColor(0x8A, 0x6C, 0x52),
            Accent = new SKColor(0xE8, 0xC1, 0x4A),
            ButtonNeutral = new SKColor(0xF3, 0xE7, 0xD8),
            AxisFontSize         = 12f,
            LegendFontSize       = 11f,
            ProfileLetterFontSize = 10f,
            AxisWidth            = 60f,
            AxisHeight           = 40f,
            ProfileWidthFraction = 0.20f,
            ProfileSeparatorWidth = 2f,
        };

        /// <summary>
        /// A real light theme: warm off-white, near-black ink, muted candles. For daylight, for a
        /// projector, and for printing a chart.
        ///
        /// <para>
        /// Distinct from High Contrast Light, which maximises separation for low vision. This one
        /// is comfortable rather than loud — the candles are DARKENED versions of the usual pair,
        /// because #77FF77 on off-white is nearly invisible. A light theme is not a dark theme
        /// with the background swapped; every foreground has to move too.
        /// </para>
        /// </summary>
        private static ChartTheme Paper() => new()
        {
            ThemeType            = ThemeType.Paper,
            Background = new SKColor(0xFD, 0xFB, 0xF6),
            BackgroundGradientEnd = new SKColor(0xF3, 0xEF, 0xE6),
            GridLine = new SKColor(0xD8, 0xD2, 0xC6),
            GridLineMinor = new SKColor(0xE8, 0xE3, 0xD9),
            AxisText = new SKColor(0x2A, 0x27, 0x22),
            AxisLine = new SKColor(0xA8, 0xA1, 0x94),
            Crosshair = new SKColor(0x1A, 0x4C, 0xA8),
            // On a light background both candles must be DARK enough to see, which squeezes the
            // brightness gap between them — the pair first drawn here differed by 0.21 in
            // luminance and so stopped carrying direction in greyscale. Widened by lifting the
            // green and deepening the red, rather than by brightening the green alone, which
            // would have cost contrast against the off-white.
            CandleBullishBody = new SKColor(0x15, 0xA0, 0x38),
            CandleBearishBody = new SKColor(0x9E, 0x05, 0x05),
            CandleBullishWick = new SKColor(0x0F, 0x7C, 0x2B),
            CandleBearishWick = new SKColor(0x7A, 0x03, 0x03),
            CandleDojiBody = new SKColor(0x5A, 0x55, 0x4C),
            VolumeBullish = new SKColor(0x15, 0xA0, 0x38, 110),
            VolumeBearish = new SKColor(0x9E, 0x05, 0x05, 110),
            IndicatorPalette = ImmutableList.Create(
                new SKColor(0x24, 0x21, 0x1C), new SKColor(0xB0, 0x1E, 0x6B), new SKColor(0xB5, 0x5A, 0x00),
                new SKColor(0x0A, 0x5F, 0x8A), new SKColor(0x8A, 0x6A, 0x00), new SKColor(0x6A, 0x2A, 0x9E),
                new SKColor(0x14, 0x56, 0x9E), new SKColor(0xA8, 0x3B, 0x0A), new SKColor(0x1A, 0x6E, 0x3E),
                new SKColor(0xA8, 0x22, 0x22), new SKColor(0x22, 0x50, 0x86), new SKColor(0x6E, 0x60, 0x0A)),
            ProfilePOC = new SKColor(0xB5, 0x5A, 0x00),
            ProfileValueArea = new SKColor(0xB5, 0x5A, 0x00, 46),
            ProfileSinglePrint = new SKColor(0x1A, 0x4C, 0xA8, 70),
            ProfileNormal = new SKColor(0x9A, 0x93, 0x86, 80),
            ProfileSeparator = new SKColor(0xC6, 0xC0, 0xB4),
            DrawingLine = new SKColor(0x1A, 0x4C, 0xA8),
            DrawingHandle = new SKColor(0x24, 0x21, 0x1C),
            SelectionHighlight = new SKColor(0x1A, 0x4C, 0xA8, 40),
            SurfaceRaised = new SKColor(0xEC, 0xE7, 0xDC),
            ChromeTopEnd = new SKColor(0xF6, 0xF2, 0xEA),
            ChromeBottom = new SKColor(0xF3, 0xEF, 0xE6),
            ChromeBottomEnd = new SKColor(0xE6, 0xE1, 0xD6),
            SurfaceSunken = new SKColor(0xFA, 0xF7, 0xF1),
            TextOnDialog = new SKColor(0x1E, 0x1C, 0x18),
            TextPrimary = new SKColor(0x1E, 0x1C, 0x18),
            TextMuted = new SKColor(0x5C, 0x56, 0x4C),
            ChromeBorder = new SKColor(0xB8, 0xB1, 0xA4),
            Accent = new SKColor(0x1A, 0x4C, 0xA8),
            ButtonNeutral = new SKColor(0x2A, 0x27, 0x22),
            AxisFontSize         = 12f,
            LegendFontSize       = 11f,
            ProfileLetterFontSize = 10f,
            AxisWidth            = 60f,
            AxisHeight           = 40f,
            ProfileWidthFraction = 0.20f,
            ProfileSeparatorWidth = 2f,
        };

        /// <summary>
        /// Deep blue rather than black — the softer alternative to Blackout, for people who find
        /// pure black too stark but a grey theme too flat.
        /// </summary>
        private static ChartTheme MidnightBlue() => new()
        {
            ThemeType            = ThemeType.MidnightBlue,
            Background = new SKColor(0x11, 0x18, 0x2B),
            BackgroundGradientEnd = new SKColor(0x08, 0x0C, 0x18),
            GridLine = new SKColor(0x2A, 0x35, 0x52),
            GridLineMinor = new SKColor(0x1B, 0x23, 0x3A),
            AxisText = new SKColor(0xD7, 0xDF, 0xF2),
            AxisLine = new SKColor(0x46, 0x55, 0x7A),
            Crosshair = new SKColor(0x7D, 0xD3, 0xFC),
            CandleBullishBody = new SKColor(0x77, 0xFF, 0x77),
            CandleBearishBody = new SKColor(0xDD, 0x00, 0x00),
            CandleBullishWick = new SKColor(0x9C, 0xFF, 0x9C),
            CandleBearishWick = new SKColor(0xF2, 0x3B, 0x3B),
            CandleDojiBody = new SKColor(0xC5, 0xCF, 0xE8),
            VolumeBullish = new SKColor(0x77, 0xFF, 0x77, 118),
            VolumeBearish = new SKColor(0xDD, 0x00, 0x00, 118),
            IndicatorPalette = ImmutableList.Create(
                new SKColor(0xE2, 0xE9, 0xF8), new SKColor(0xFF, 0x8A, 0xBE), new SKColor(0xFF, 0xB0, 0x4A),
                new SKColor(0x70, 0xD6, 0xFF), new SKColor(0xFF, 0xE2, 0x70), new SKColor(0xCE, 0x94, 0xFF),
                new SKColor(0x7E, 0xBE, 0xFF), new SKColor(0xFF, 0x9E, 0x6E), new SKColor(0x96, 0xE8, 0xAE),
                new SKColor(0xFF, 0x7A, 0x7A), new SKColor(0xA0, 0xCD, 0xEC), new SKColor(0xE8, 0xE6, 0xBA)),
            ProfilePOC = new SKColor(0xFF, 0xC4, 0x54),
            ProfileValueArea = new SKColor(0xFF, 0xC4, 0x54, 56),
            ProfileSinglePrint = new SKColor(0x7E, 0x9E, 0xFF, 100),
            ProfileNormal = new SKColor(0x74, 0x82, 0xA6, 90),
            ProfileSeparator = new SKColor(0x2A, 0x35, 0x52),
            DrawingLine = new SKColor(0x7D, 0xD3, 0xFC),
            DrawingHandle = new SKColor(0xE8, 0xEF, 0xFC),
            SelectionHighlight = new SKColor(0x7D, 0xD3, 0xFC, 46),
            SurfaceRaised = new SKColor(0x1E, 0x2A, 0x46),
            ChromeTopEnd = new SKColor(0x11, 0x18, 0x2B),
            ChromeBottom = new SKColor(0x08, 0x0C, 0x18),
            ChromeBottomEnd = new SKColor(0x05, 0x08, 0x11),
            SurfaceSunken = new SKColor(0x1A, 0x24, 0x3C),
            TextOnDialog = new SKColor(0xE4, 0xEB, 0xF8),
            TextPrimary = new SKColor(0xE4, 0xEB, 0xF8),
            TextMuted = new SKColor(0xA6, 0xB4, 0xD0),
            ChromeBorder = new SKColor(0x46, 0x55, 0x7A),
            Accent = new SKColor(0x7D, 0xD3, 0xFC),
            ButtonNeutral = new SKColor(0xE4, 0xEB, 0xF8),
            AxisFontSize         = 12f,
            LegendFontSize       = 11f,
            ProfileLetterFontSize = 10f,
            AxisWidth            = 60f,
            AxisHeight           = 40f,
            ProfileWidthFraction = 0.20f,
            ProfileSeparatorWidth = 2f,
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
            ChromeBottom         = new SKColor(14, 14, 14),
            TextOnDialog         = SKColors.White,
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
            // Brightened from (0,140,0): against (200,0,0) the pair differed in hue but barely
            // in luminance, so it stopped carrying direction under red-green deficiency — in
            // the one theme that exists specifically to be legible.
            CandleBullishBody    = new SKColor(0, 175, 0),
            CandleBearishBody    = new SKColor(190, 0, 0),
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
            ChromeBottom         = new SKColor(226, 226, 226),
            TextOnDialog         = SKColors.Black,
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
            CandleBullishBody    = new SKColor(96, 222, 126),
            CandleBearishBody    = new SKColor(196, 54, 64),
            CandleBullishWick    = new SKColor(126, 238, 152),
            CandleBearishWick    = new SKColor(216, 84, 92),
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
            ChromeBottom         = new SKColor(20, 22, 30),
            TextOnDialog         = new SKColor(226, 230, 240),
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
            CandleBullishBody    = new SKColor(174, 199, 20),   // green, lifted for greyscale separation
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
