using System;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Theming;

namespace AccessibleTrader.Sdk.Interfaces;

public interface IThemeService
{
    ChartTheme Current { get; }
    void SetTheme(ThemeType theme);
    event EventHandler<ChartTheme>? ThemeChanged;

    /// <summary>
    /// Re-reads the visual-accessibility override settings (color-vision-safe
    /// direction colors, hollow up-candles) and re-fires <see cref="ThemeChanged"/>
    /// so the chart repaints. Default no-op so test substitutes and simple
    /// implementations don't have to care.
    /// </summary>
    void RefreshAccessibilityOverrides() { }

    /// <summary>
    /// Switches to one of the user's own themes: its base built-in first, then its overrides, then
    /// the appearance preferences as usual.
    ///
    /// <para>
    /// Default no-op so substitutes and simple implementations need not care — a host with no
    /// theme storage has no custom themes to switch to.
    /// </para>
    /// </summary>
    void SetCustomTheme(AccessibleTrader.Sdk.Theming.ThemePreset preset) { }
}
