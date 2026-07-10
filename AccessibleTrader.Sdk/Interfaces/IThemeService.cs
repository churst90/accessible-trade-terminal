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
}
