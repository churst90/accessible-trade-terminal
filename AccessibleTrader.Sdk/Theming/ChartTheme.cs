using System.Collections.Immutable;
using SkiaSharp;
using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Sdk.Theming;

public record ChartTheme
{
    public required ThemeType ThemeType { get; init; }
    // Canvas
    public required SKColor Background { get; init; }
    public required SKColor GridLine { get; init; }
    public required SKColor GridLineMinor { get; init; }
    public required SKColor AxisText { get; init; }
    public required SKColor AxisLine { get; init; }
    public required SKColor Crosshair { get; init; }
    // Candles
    public required SKColor CandleBullishBody { get; init; }
    public required SKColor CandleBearishBody { get; init; }
    public required SKColor CandleBullishWick { get; init; }
    public required SKColor CandleBearishWick { get; init; }
    public required SKColor CandleDojiBody { get; init; }
    // Volume
    public required SKColor VolumeBullish { get; init; }
    public required SKColor VolumeBearish { get; init; }
    // Indicator palette (12 color slots)
    public required ImmutableList<SKColor> IndicatorPalette { get; init; }

    // ── Visual accessibility overrides (Phase D, all default OFF) ────────────
    // Set by ThemeService from settings, consulted by the renderers. These are
    // deliberate override modes (like OS high-contrast) — when on, they take
    // precedence over per-component color customisation for direction cues.

    /// <summary>
    /// Color-vision-safe direction colors: up = blue, down = orange instead of
    /// the red/green convention that deuteranopia/protanopia users cannot
    /// separate. Applied to candles and price-action-colored bars.
    /// </summary>
    public bool ColorVisionSafe { get; init; } = false;

    /// <summary>
    /// Draw up-candles hollow (outline only) so direction is readable by shape
    /// alone — the classic colorblind-safe candlestick convention, independent
    /// of any palette.
    /// </summary>
    public bool HollowUpCandles { get; init; } = false;

    /// <summary>
    /// When non-null, the pane background is a vertical linear gradient from
    /// <see cref="Background"/> (top) to this color (bottom) instead of a flat fill.
    /// Purely cosmetic and opt-in (default null = flat); set by ThemeService from the
    /// appearance settings for sighted/low-vision users and screenshots.
    /// </summary>
    public SKColor? BackgroundGradientEnd { get; init; } = null;
    // Profile
    public required SKColor ProfilePOC { get; init; }
    public required SKColor ProfileValueArea { get; init; }
    public required SKColor ProfileSinglePrint { get; init; }
    public required SKColor ProfileNormal { get; init; }
    public required SKColor ProfileSeparator { get; init; }
    // Drawing overlays
    public required SKColor DrawingLine { get; init; }
    public required SKColor DrawingHandle { get; init; }
    public required SKColor SelectionHighlight { get; init; }
    // Typography
    public required float AxisFontSize { get; init; }
    public required float LegendFontSize { get; init; }
    public required float ProfileLetterFontSize { get; init; }
    // Dimensions
    public required float AxisWidth { get; init; }
    public required float AxisHeight { get; init; }
    public required float ProfileWidthFraction { get; init; }
    public required float ProfileSeparatorWidth { get; init; }

    // ── Application chrome ──────────────────────────────────────────────────
    //
    // Everything OUTSIDE the Skia canvas: the toolbars above the chart, the indicator
    // bar below it, dialogs, text and buttons. These are published to CSS custom
    // properties at startup and on every theme change, so a theme covers the whole
    // window instead of stopping at the canvas edge — the seam between a themed chart
    // and a fixed dark-grey toolbar is the single most "unfinished" thing about the
    // app's appearance.
    //
    // Optional with defaults rather than required: an existing theme that has not been
    // given a chrome palette keeps working and simply renders the previous dark chrome.

    // Three INDEPENDENT regions, each with its own vertical fade: the toolbar band above the
    // chart, the chart canvas itself, and the footer band below it. A theme that wants one
    // continuous window-wide fade sets the seams equal (ChromeTopEnd == Background, and
    // BackgroundGradientEnd == ChromeBottom). A theme that wants three distinct blocks — a
    // walnut header, a near-black chart, a lighter footer — simply doesn't. Neither is a
    // special case, which is the point: the regions were previously derived from one fade and
    // could not disagree.

    /// <summary>Top of the toolbar band, at the very top of the window.</summary>
    public SKColor SurfaceRaised { get; init; } = new(30, 30, 30);

    /// <summary>Bottom of the toolbar band, where it meets the chart. Defaults to
    /// <see cref="SurfaceRaised"/> — a flat band rather than a fade.</summary>
    public SKColor? ChromeTopEnd { get; init; } = null;

    /// <summary>Top of the footer band, where it meets the chart.</summary>
    public SKColor ChromeBottom { get; init; } = new(24, 24, 24);

    /// <summary>Bottom of the footer band, at the very bottom of the window. Defaults to
    /// <see cref="ChromeBottom"/>.</summary>
    public SKColor? ChromeBottomEnd { get; init; } = null;

    /// <summary>Dialog bodies and panels — the recessed surface behind content.</summary>
    public SKColor SurfaceSunken { get; init; } = new(18, 18, 18);

    /// <summary>
    /// Text on dialogs. Separate from <see cref="TextPrimary"/> because a dialog surface does
    /// not have to share the chrome's brightness — a theme can perfectly well pair a dark
    /// toolbar with a parchment dialog, and then the two need opposite ink.
    /// </summary>
    public SKColor TextOnDialog { get; init; } = new(240, 240, 240);

    /// <summary>Body text on chrome surfaces.</summary>
    public SKColor TextPrimary { get; init; } = new(255, 255, 255);

    /// <summary>Secondary text: hints, units, disabled captions.</summary>
    public SKColor TextMuted { get; init; } = new(170, 170, 170);

    /// <summary>Hairlines and dividers between chrome regions.</summary>
    public SKColor ChromeBorder { get; init; } = new(68, 68, 68);

    /// <summary>Primary action colour — focus rings, selected tabs, the Load button.</summary>
    public SKColor Accent { get; init; } = new(0, 120, 212);

    /// <summary>
    /// Default tint for toolbar icon buttons that do not carry a semantic variant.
    /// Variants (data / action / warning) stay fixed so muscle memory survives a theme
    /// change — only this neutral base follows the theme.
    /// </summary>
    public SKColor ButtonNeutral { get; init; } = new(180, 180, 200);
}
