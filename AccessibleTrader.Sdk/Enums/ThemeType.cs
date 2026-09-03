namespace AccessibleTrader.Sdk.Enums;

/// <summary>
/// Built-in appearance presets. A theme covers the whole window — the chart canvas AND the
/// chrome around it — so switching one never leaves a themed chart inside a fixed-grey frame.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Classic"/> is the default (changed from <see cref="SteelGray"/> on 2026-09-03).
/// The high-contrast themes are accessibility tools rather than a look to greet every new user
/// with, and shipping "black background, white candles" as the first impression made a finished
/// application read as a debug harness. Anyone who needs maximum contrast still has it one
/// setting away, unchanged. The authority for which one ships is
/// <c>ThemeService.DefaultTheme</c>, not this comment.
/// </para>
/// <para>
/// The enum is persisted by NAME (<c>ui.theme</c>), not by ordinal, so entries may be
/// reordered or inserted without invalidating anyone's saved preference.
/// </para>
/// </remarks>
public enum ThemeType
{
    /// <summary>Cool neutral greys, lighter chrome above and below a chart that fades
    /// upward into the toolbar so the window reads as one continuous surface. Was the default
    /// until 2026-09-03.</summary>
    SteelGray,

    /// <summary>Pure black everywhere, white text, dark-grey dialogs. A true dark mode for OLED
    /// panels and for anyone who finds any lit background fatiguing.</summary>
    Blackout,

    /// <summary>Default. The dark navy-and-teal scheme most charting sites use, so someone
    /// arriving from another platform starts from something their eye already knows.</summary>
    Classic,

    /// <summary>Amber phosphor on near-black — a 1970s monitor. Amber-on-dark is a genuinely
    /// restful pairing, not only a period reference.</summary>
    AmberCrt,

    /// <summary>Warm browns and brass. The wood is the FRAME — chrome and dialogs — while the
    /// chart stays deep and neutral so price action reads against it.</summary>
    Walnut,

    /// <summary>A real light theme: warm off-white, near-black ink, muted candles. For daylight,
    /// for projectors, and for printing a chart.</summary>
    Paper,

    /// <summary>Deep blue rather than black — the softer alternative to Blackout.</summary>
    MidnightBlue,

    HighContrastDark,
    HighContrastLight,
    SoftDark,
    Solarized,
    Braille
}
