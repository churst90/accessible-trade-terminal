using SkiaSharp;

namespace AccessibleTrader.Sdk.Theming;

/// <summary>
/// Which region of the window a themeable colour belongs to. The editor renders one section per
/// group, in this order.
/// </summary>
public enum ThemeGroup
{
    /// <summary>The toolbar band above the chart.</summary>
    TopBar,
    /// <summary>The chart canvas: background, grid, axes.</summary>
    ChartArea,
    /// <summary>Price action — candles, wicks, volume.</summary>
    PriceAction,
    /// <summary>Things drawn over the chart: crosshair, drawings, selection, volume profile.</summary>
    Overlays,
    /// <summary>The footer band below the chart.</summary>
    BottomBar,
    /// <summary>Dialogs and panels.</summary>
    Dialogs,
    /// <summary>Text, borders and the accent used across all chrome.</summary>
    TextAndChrome,
}

/// <summary>
/// One themeable colour: how to read it, how to write it, and what to call it in the editor.
/// </summary>
/// <param name="Key">Stable identifier used in saved theme files. Never rename — it is on disk.</param>
/// <param name="Group">Which editor section it appears under.</param>
/// <param name="Label">Short name shown next to the picker.</param>
/// <param name="Description">One line saying what it actually affects. Shown as a hint and read by
/// a screen reader, so it has to make sense without seeing the result.</param>
/// <param name="Get">Reads the current value.</param>
/// <param name="Set">Returns a copy of the theme with the value replaced.</param>
/// <param name="Optional">True when null is meaningful — a gradient end of null means "flat".</param>
public sealed record ThemeField(
    string Key,
    ThemeGroup Group,
    string Label,
    string Description,
    Func<ChartTheme, SKColor?> Get,
    Func<ChartTheme, SKColor?, ChartTheme> Set,
    bool Optional = false);

/// <summary>
/// The single catalogue of everything a theme can colour.
///
/// <para>
/// This table is why the theme editor has no hand-written form. It enumerates the fields, groups
/// them into sections, and reads and writes through the lambdas — so adding a themeable colour is
/// one entry here and it appears in the editor, in saved theme files, and in the round-trip tests
/// at once. The alternative, a form with thirty hand-wired pickers, guarantees that the next
/// colour added to <see cref="ChartTheme"/> is themeable in the renderer and invisible in the UI,
/// which is exactly the gap this whole effort started from.
/// </para>
///
/// <para>
/// <b>Keys are on disk.</b> They appear in every saved theme file, so renaming one silently drops
/// that colour from every theme a user has saved. Add and deprecate; never rename.
/// </para>
/// </summary>
public static class ThemeFields
{
    /// <summary>Every themeable colour, in editor order.</summary>
    public static IReadOnlyList<ThemeField> All { get; } = new ThemeField[]
    {
        // ── Top bar ──────────────────────────────────────────────────────
        new("topBar",        ThemeGroup.TopBar, "Toolbar top",
            "Colour at the very top of the window, behind the toolbar buttons.",
            t => t.SurfaceRaised, (t, v) => t with { SurfaceRaised = v ?? t.SurfaceRaised }),
        new("topBarEnd",     ThemeGroup.TopBar, "Toolbar bottom",
            "Colour where the toolbar meets the chart. Set it to the chart's top colour and the seam disappears. Leave it unset for a flat bar.",
            t => t.ChromeTopEnd, (t, v) => t with { ChromeTopEnd = v }, Optional: true),

        // ── Chart area ───────────────────────────────────────────────────
        new("chartTop",      ThemeGroup.ChartArea, "Chart top",
            "Background at the top of the chart.",
            t => t.Background, (t, v) => t with { Background = v ?? t.Background }),
        new("chartBottom",   ThemeGroup.ChartArea, "Chart bottom",
            "Background at the bottom of the chart. Unset means a flat background with no gradient.",
            t => t.BackgroundGradientEnd, (t, v) => t with { BackgroundGradientEnd = v }, Optional: true),
        new("gridMajor",     ThemeGroup.ChartArea, "Gridlines",
            "The round-number gridlines. Keep these close to the background — a grid as loud as the price data is a wall, not a reference.",
            t => t.GridLine, (t, v) => t with { GridLine = v ?? t.GridLine }),
        new("gridMinor",     ThemeGroup.ChartArea, "Gridlines, minor",
            "The lighter gridlines between the labelled ones.",
            t => t.GridLineMinor, (t, v) => t with { GridLineMinor = v ?? t.GridLineMinor }),
        new("axisText",      ThemeGroup.ChartArea, "Axis labels",
            "Prices and dates along the edges of the chart.",
            t => t.AxisText, (t, v) => t with { AxisText = v ?? t.AxisText }),
        new("axisLine",      ThemeGroup.ChartArea, "Axis lines",
            "The rules separating the axes from the chart.",
            t => t.AxisLine, (t, v) => t with { AxisLine = v ?? t.AxisLine }),

        // ── Price action ─────────────────────────────────────────────────
        new("bullBody",      ThemeGroup.PriceAction, "Rising candle",
            "Body of a candle that closed up. Also used for rising volume bars.",
            t => t.CandleBullishBody, (t, v) => t with { CandleBullishBody = v ?? t.CandleBullishBody }),
        new("bearBody",      ThemeGroup.PriceAction, "Falling candle",
            "Body of a candle that closed down. Also used for falling volume bars.",
            t => t.CandleBearishBody, (t, v) => t with { CandleBearishBody = v ?? t.CandleBearishBody }),
        new("bullWick",      ThemeGroup.PriceAction, "Rising wick",
            "The thin high/low line on a rising candle.",
            t => t.CandleBullishWick, (t, v) => t with { CandleBullishWick = v ?? t.CandleBullishWick }),
        new("bearWick",      ThemeGroup.PriceAction, "Falling wick",
            "The thin high/low line on a falling candle.",
            t => t.CandleBearishWick, (t, v) => t with { CandleBearishWick = v ?? t.CandleBearishWick }),
        new("doji",          ThemeGroup.PriceAction, "Unchanged candle",
            "A candle that opened and closed at the same price.",
            t => t.CandleDojiBody, (t, v) => t with { CandleDojiBody = v ?? t.CandleDojiBody }),
        new("volumeBull",    ThemeGroup.PriceAction, "Rising volume",
            "Volume bar under a rising candle. Usually the candle colour at partial transparency.",
            t => t.VolumeBullish, (t, v) => t with { VolumeBullish = v ?? t.VolumeBullish }),
        new("volumeBear",    ThemeGroup.PriceAction, "Falling volume",
            "Volume bar under a falling candle.",
            t => t.VolumeBearish, (t, v) => t with { VolumeBearish = v ?? t.VolumeBearish }),

        // ── Overlays ─────────────────────────────────────────────────────
        new("crosshair",     ThemeGroup.Overlays, "Crosshair",
            "The lines following the cursor bar.",
            t => t.Crosshair, (t, v) => t with { Crosshair = v ?? t.Crosshair }),
        new("drawingLine",   ThemeGroup.Overlays, "Drawing lines",
            "Trend lines, Fibonacci levels and other tools you place yourself.",
            t => t.DrawingLine, (t, v) => t with { DrawingLine = v ?? t.DrawingLine }),
        new("drawingHandle", ThemeGroup.Overlays, "Drawing handles",
            "The anchor points you grab to move a drawing.",
            t => t.DrawingHandle, (t, v) => t with { DrawingHandle = v ?? t.DrawingHandle }),
        new("selection",     ThemeGroup.Overlays, "Selection highlight",
            "The wash over a selected drawing or region.",
            t => t.SelectionHighlight, (t, v) => t with { SelectionHighlight = v ?? t.SelectionHighlight }),
        new("profilePoc",    ThemeGroup.Overlays, "Volume profile: point of control",
            "The price level where the most volume traded.",
            t => t.ProfilePOC, (t, v) => t with { ProfilePOC = v ?? t.ProfilePOC }),
        new("profileValue",  ThemeGroup.Overlays, "Volume profile: value area",
            "The band holding 70 percent of traded volume.",
            t => t.ProfileValueArea, (t, v) => t with { ProfileValueArea = v ?? t.ProfileValueArea }),
        new("profileSingle", ThemeGroup.Overlays, "Volume profile: single prints",
            "Price levels touched by only one bar.",
            t => t.ProfileSinglePrint, (t, v) => t with { ProfileSinglePrint = v ?? t.ProfileSinglePrint }),
        new("profileNormal", ThemeGroup.Overlays, "Volume profile: bars",
            "Ordinary volume-profile bars.",
            t => t.ProfileNormal, (t, v) => t with { ProfileNormal = v ?? t.ProfileNormal }),
        new("profileSep",    ThemeGroup.Overlays, "Volume profile: separator",
            "The rule between the profile and the chart.",
            t => t.ProfileSeparator, (t, v) => t with { ProfileSeparator = v ?? t.ProfileSeparator }),

        // ── Bottom bar ───────────────────────────────────────────────────
        new("bottomBar",     ThemeGroup.BottomBar, "Footer top",
            "Colour where the footer meets the chart. Match it to the chart's bottom colour to remove the seam.",
            t => t.ChromeBottom, (t, v) => t with { ChromeBottom = v ?? t.ChromeBottom }),
        new("bottomBarEnd",  ThemeGroup.BottomBar, "Footer bottom",
            "Colour at the very bottom of the window. Unset for a flat footer.",
            t => t.ChromeBottomEnd, (t, v) => t with { ChromeBottomEnd = v }, Optional: true),

        // ── Dialogs ──────────────────────────────────────────────────────
        new("dialogSurface", ThemeGroup.Dialogs, "Dialog background",
            "Behind the content of every dialog and panel.",
            t => t.SurfaceSunken, (t, v) => t with { SurfaceSunken = v ?? t.SurfaceSunken }),
        new("dialogText",    ThemeGroup.Dialogs, "Dialog text",
            "Text on dialogs. Kept separate from toolbar text so a dark toolbar can carry a light dialog.",
            t => t.TextOnDialog, (t, v) => t with { TextOnDialog = v ?? t.TextOnDialog }),

        // ── Text and chrome ──────────────────────────────────────────────
        new("textPrimary",   ThemeGroup.TextAndChrome, "Text",
            "Labels on the toolbars and status bar.",
            t => t.TextPrimary, (t, v) => t with { TextPrimary = v ?? t.TextPrimary }),
        new("textMuted",     ThemeGroup.TextAndChrome, "Text, secondary",
            "Hints, units and less important captions.",
            t => t.TextMuted, (t, v) => t with { TextMuted = v ?? t.TextMuted }),
        new("border",        ThemeGroup.TextAndChrome, "Borders",
            "Hairlines between regions, and around controls and dialogs.",
            t => t.ChromeBorder, (t, v) => t with { ChromeBorder = v ?? t.ChromeBorder }),
        new("accent",        ThemeGroup.TextAndChrome, "Accent",
            "Selected tabs, primary buttons and focus emphasis.",
            t => t.Accent, (t, v) => t with { Accent = v ?? t.Accent }),
        new("buttonNeutral", ThemeGroup.TextAndChrome, "Button tint",
            "Toolbar buttons with no meaning of their own. Buttons that mean something — green for data, amber for warnings — keep their colour across every theme so muscle memory survives.",
            t => t.ButtonNeutral, (t, v) => t with { ButtonNeutral = v ?? t.ButtonNeutral }),
    };

    /// <summary>Fields in one editor section, in order.</summary>
    public static IEnumerable<ThemeField> InGroup(ThemeGroup group) => All.Where(f => f.Group == group);

    /// <summary>Human name for a section heading.</summary>
    public static string GroupLabel(ThemeGroup group) => group switch
    {
        ThemeGroup.TopBar        => "Top bar",
        ThemeGroup.ChartArea     => "Chart area",
        ThemeGroup.PriceAction   => "Candles and volume",
        ThemeGroup.Overlays      => "Overlays and drawings",
        ThemeGroup.BottomBar     => "Bottom bar",
        ThemeGroup.Dialogs       => "Dialogs",
        ThemeGroup.TextAndChrome => "Text and chrome",
        _                        => group.ToString(),
    };

    /// <summary>Looks a field up by its on-disk key.</summary>
    public static ThemeField? ByKey(string key) =>
        All.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal));
}
