namespace AccessibleTrader.Sdk.Enums;

/// <summary>
/// Built-in appearance presets. A theme covers the whole window — the chart canvas AND the
/// chrome around it — so switching one never leaves a themed chart inside a fixed-grey frame.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SteelGray"/> is the default. The high-contrast themes are accessibility tools
/// rather than a look to greet every new user with, and shipping "black background, white
/// candles" as the first impression made a finished application read as a debug harness.
/// Anyone who needs maximum contrast still has it one setting away, unchanged.
/// </para>
/// <para>
/// The enum is persisted by NAME (<c>ui.theme</c>), not by ordinal, so entries may be
/// reordered or inserted without invalidating anyone's saved preference.
/// </para>
/// </remarks>
public enum ThemeType
{
    /// <summary>Default. Cool neutral greys, lighter chrome above and below a chart that fades
    /// upward into the toolbar so the window reads as one continuous surface.</summary>
    SteelGray,

    /// <summary>Pure black everywhere, white text, dark-grey dialogs. A true dark mode for OLED
    /// panels and for anyone who finds any lit background fatiguing.</summary>
    Blackout,

    /// <summary>The dark navy-and-teal scheme most charting sites use. Here so someone arriving
    /// from another platform can start from something their eye already knows.</summary>
    Classic,

    HighContrastDark,
    HighContrastLight,
    SoftDark,
    Solarized,
    Braille
}
