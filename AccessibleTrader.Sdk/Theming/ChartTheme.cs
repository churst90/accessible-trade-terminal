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
}
